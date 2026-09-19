using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataSync.Core.Logging;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Back-fills rows missing locally below the real-time position: compares the BaseID ranges of both databases,
    /// copies the newest missing range first, reading it backwards in windows of SyncRowLimit, then the next one.
    /// Ranges the real-time task skipped (<see cref="AddGap"/>) are taken over at once, ahead of older ranges.
    /// When nothing is left it checks again for new gaps every GapCheckInterval.
    /// </summary>
    public class GapSyncReplicator : ReplicationTask
    {
        private static readonly TimeSpan YieldDelay = TimeSpan.FromSeconds(1);

        /// <summary>Shorter waits for real-time are logged at Debug only, so a busy link does not flood the log.</summary>
        private static readonly TimeSpan LogWaitsFrom = TimeSpan.FromSeconds(10);

        private readonly object _lock = new object();
        private readonly IProcessDataStore _remote;
        private readonly IProcessDataStore _local;
        private readonly Func<long> _getBoundary;
        private readonly Func<bool> _shouldYield;
        private readonly ConcurrentQueue<BaseIdRange> _pendingGaps = new ConcurrentQueue<BaseIdRange>();
        private readonly SyncBatchSize _batch;

        // _ranges, _syncingIndex and _nextBaseId are only changed by the task thread; the lock is for readers.
        private List<SyncRange> _ranges = new List<SyncRange>();
        private int _syncingIndex = -1;
        private long _nextBaseId = -1;
        private volatile bool _refreshRequested = true;
        private bool _recheckGaps; // task thread only: idle, so compare the databases again at the next iteration
        private int _isYielding;
        private readonly HashSet<long> _disabledBegins = new HashSet<long>(); // ranges the user switched off, by BaseIdBegin

        // Task thread only: for the log.
        private readonly Stopwatch _yieldClock = new Stopwatch();
        private TimeSpan _yieldCounted;
        private TimeSpan _summaryWaitTime;
        private long _loggedRangeBegin = -1;

        /// <param name="getBoundary">Returns the real-time position; BaseIDs from there up are not synced.</param>
        public GapSyncReplicator(IProcessDataStore remote, IProcessDataStore local, Func<long> getBoundary,
                                 ReplicationSettings settings, Action<string> report)
            : this(remote, local, getBoundary, settings, report, null)
        {
        }

        /// <param name="getBoundary">Returns the real-time position; BaseIDs from there up are not synced.</param>
        /// <param name="shouldYield">While it returns true the task does not read, leaving the link to the real-time task.</param>
        public GapSyncReplicator(IProcessDataStore remote, IProcessDataStore local, Func<long> getBoundary,
                                 ReplicationSettings settings, Action<string> report, Func<bool> shouldYield)
            : this(remote, local, getBoundary, settings, report, shouldYield, settings.CreateSyncBatchSize())
        {
        }

        private GapSyncReplicator(IProcessDataStore remote, IProcessDataStore local, Func<long> getBoundary,
                                  ReplicationSettings settings, Action<string> report, Func<bool> shouldYield, SyncBatchSize batch)
            : base("Sync", settings, batch, report)
        {
            _remote = remote;
            _local = local;
            _getBoundary = getBoundary;
            _shouldYield = shouldYield;
            _batch = batch;
        }

        /// <summary>
        /// The highest BaseID of the next window to copy, or -1 when there is nothing to sync.
        /// </summary>
        public long NextBaseId
        {
            get
            {
                lock (_lock)
                {
                    return _nextBaseId;
                }
            }
        }

        /// <summary>
        /// True while the task is waiting for the real-time task to catch up or recover.
        /// </summary>
        public bool IsYielding => Volatile.Read(ref _isYielding) != 0;

        /// <summary>
        /// Switches one range off (skip it) or back on, identified by its BaseIdBegin. The choice is kept across
        /// range refreshes until the range is synced. Thread-safe; the task picks it up on its next iteration.
        /// </summary>
        public void SetRangeEnabled(long baseIdBegin, bool enabled)
        {
            lock (_lock)
            {
                if (enabled)
                {
                    _disabledBegins.Remove(baseIdBegin);
                }
                else if (_disabledBegins.Add(baseIdBegin))
                {
                    Log.Information("Sync skipping records from " + baseIdBegin + " (switched off in the Sync Table)");
                }

                foreach (var range in _ranges)
                {
                    if ((range.Status == RangeStatus.NotSync || range.Status == RangeStatus.Syncing) &&
                        range.Range.BaseIdBegin == baseIdBegin)
                    {
                        range.SyncEnabled = enabled;
                    }
                }

                if (!enabled && _syncingIndex >= 0 &&
                    _syncingIndex < _ranges.Count &&
                    _ranges[_syncingIndex].Range.BaseIdBegin == baseIdBegin)
                {
                    // Stop copying this range now (already-copied rows stay) and move to the next enabled one.
                    _ranges[_syncingIndex].Status = RangeStatus.NotSync;
                    SelectNextRange();
                }
                else if (enabled && _syncingIndex < 0)
                {
                    // Nothing is being copied; a re-enabled range can be picked up right away.
                    SelectNextRange();
                }
            }
            Wake();
        }

        public List<SyncRange> GetRangesSnapshot()
        {
            lock (_lock)
            {
                return _ranges.Select(SyncRangePlanner.Copy).ToList();
            }
        }

        /// <summary>
        /// Recomputes the ranges from both databases at the start of the next iteration.
        /// </summary>
        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        /// <summary>
        /// Queues a range the real-time task skipped. It is copied next, newest first, without comparing the databases
        /// and without waiting for the next gap check. Thread-safe.
        /// </summary>
        public void AddGap(BaseIdRange range)
        {
            _pendingGaps.Enqueue(new BaseIdRange { BaseIdBegin = range.BaseIdBegin, BaseIdEnd = range.BaseIdEnd });
            Wake();
        }

        public override TimeSpan RunIteration()
        {
            if (_shouldYield != null && _shouldYield())
            {
                // The newest data comes first: leave the link to the real-time task and look again shortly.
                if (Interlocked.Exchange(ref _isYielding, 1) == 0)
                {
                    _yieldClock.Restart();
                    _yieldCounted = TimeSpan.Zero;
                    Log.Debug("Sync waiting while real-time is behind or failing");
                }
                return YieldDelay;
            }
            if (Interlocked.Exchange(ref _isYielding, 0) != 0)
            {
                var waited = _yieldClock.Elapsed;
                _summaryWaitTime += waited - _yieldCounted;
                _yieldClock.Reset();

                var message = "Sync resumed after waiting " + FormatSeconds(waited) + " for real-time";
                if (waited >= LogWaitsFrom)
                {
                    Log.Information(message);
                }
                else
                {
                    Log.Debug(message);
                }
            }

            var hasPendingGaps = !_pendingGaps.IsEmpty;
            if (_refreshRequested || (_recheckGaps && !hasPendingGaps))
            {
                try
                {
                    RefreshRanges();
                }
                catch (Exception ex)
                {
                    return OnFailure("UpdateSyncStatus", ex);
                }
            }
            else if (hasPendingGaps)
            {
                // Skipped ranges are copied now; the idle re-check waits until they are done.
                _recheckGaps = false;
                AddPendingGaps();
            }

            long windowBegin;
            long windowEnd;
            var requested = Batch.Current;
            SyncRange syncing;
            lock (_lock)
            {
                if (_syncingIndex < 0 || _syncingIndex >= _ranges.Count ||
                    _ranges[_syncingIndex].Status != RangeStatus.Syncing)
                {
                    ReassertSyncing();
                }

                if (_syncingIndex < 0)
                {
                    // Nothing left to back-fill: look again for gaps (e.g. rows the remote wrote late) after a while.
                    if (State != TaskState.NoOldData)
                    {
                        Log.Information("Sync has nothing left to copy; checking for new gaps every " +
                                        FormatSeconds(Settings.GapCheckInterval));
                    }
                    State = TaskState.NoOldData;
                    _recheckGaps = true;
                    return Settings.GapCheckInterval;
                }

                syncing = _ranges[_syncingIndex];
                if (!syncing.SyncEnabled)
                {
                    // Switched off while it was syncing: park it as pending until switched back on.
                    syncing.Status = RangeStatus.NotSync;
                    SelectNextRange();
                    if (_syncingIndex < 0)
                    {
                        State = TaskState.NoOldData;
                        _recheckGaps = true;
                        return Settings.GapCheckInterval;
                    }

                    syncing = _ranges[_syncingIndex];
                }
                windowEnd = _nextBaseId;
                // A toggle may have switched ranges after the cursor was set; never read above the new range.
                if (windowEnd > syncing.Range.BaseIdEnd)
                {
                    windowEnd = syncing.Range.BaseIdEnd;
                    _nextBaseId = windowEnd;
                }
                windowBegin = Math.Max(windowEnd - requested + 1, syncing.Range.BaseIdBegin);
            }

            State = TaskState.Downloading;
            var stopwatch = Stopwatch.StartNew();

            List<TProcessData> rows;
            try
            {
                rows = _remote.GetRange(windowBegin, windowEnd);
            }
            catch (Exception ex)
            {
                return OnReadFailure("GetSyncDataFromRemoteDatabase", ex);
            }
            var readTime = stopwatch.Elapsed;
            RecordRead(readTime);

            try
            {
                _local.SaveBatch(rows);
            }
            catch (Exception ex)
            {
                // The window is not advanced, so it is read and saved again.
                return OnFailure("SaveSyncDataToLocalDatabase", ex);
            }

            // Throughput in BaseIDs covered, not rows returned, so sparse ranges are not mistaken for slow reads.
            var covered = (int)(windowEnd - windowBegin + 1);
            var copyTime = stopwatch.Elapsed;
            AdjustBatch(() => _batch.OnRead(requested, covered, copyTime),
                        () => "measured " + FormatRate(_batch.RecordsPerSecond) + " records");

            Log.Debug("Sync read " + rows.Count + " rows in BaseIDs " + windowBegin + "-" + windowEnd + " in " +
                      FormatSeconds(readTime) + ", saved in " + FormatSeconds(stopwatch.Elapsed - readTime));

            // Every row the remote has in the window is now saved (an empty result means it has none there).
            // The window belongs to the range the read started from: if the toggle switched ranges mid-read,
            // the rows are still applied to that same range object (no SaveBatch happens twice).
            lock (_lock)
            {
                if (syncing.Status != RangeStatus.Syncing && syncing.Status != RangeStatus.NotSync)
                {
                    // The range finished while the read was in flight (e.g. a refresh merged it); nothing to advance.
                    return OnSuccess(rows.Count, false, Settings.SyncUpdateInterval, stopwatch.Elapsed);
                }
                if (!syncing.SyncEnabled)
                {
                    // Switched off while the read was in flight: keep the saved rows but leave the range pending.
                    syncing.Status = RangeStatus.NotSync;
                    if (windowBegin <= syncing.Range.BaseIdBegin)
                    {
                        syncing.Range.BaseIdEnd = syncing.StartSyncPoint;
                    }
                    else
                    {
                        syncing.Range.BaseIdEnd = windowBegin - 1;
                    }
                    ReassertSyncing();
                    if (_syncingIndex < 0)
                    {
                        _nextBaseId = -1;
                    }
                    return OnSuccess(rows.Count, false, Settings.SyncUpdateInterval, stopwatch.Elapsed);
                }

                if (windowBegin <= syncing.Range.BaseIdBegin)
                {
                    _disabledBegins.Remove(syncing.Range.BaseIdBegin);
                    syncing.Status = RangeStatus.Synced;
                    syncing.Range.BaseIdEnd = syncing.StartSyncPoint;
                    syncing.StartSyncPoint = -1;
                    Log.Information("Sync finished records " + syncing.Range.BaseIdBegin + "-" + syncing.Range.BaseIdEnd);
                    SyncRangePlanner.MergeSynced(_ranges);
                    // The merge may have moved ranges; if this range survived, it is still the one being copied.
                    _syncingIndex = _ranges.IndexOf(syncing);
                    _nextBaseId = _syncingIndex >= 0 ? syncing.Range.BaseIdEnd : -1;
                    SelectNextRange();
                }
                else
                {
                    syncing.Range.BaseIdEnd = windowBegin - 1;
                    if (_syncingIndex >= 0 && _syncingIndex < _ranges.Count && ReferenceEquals(_ranges[_syncingIndex], syncing))
                    {
                        _nextBaseId = syncing.Range.BaseIdEnd;
                    }
                }
            }

            return OnSuccess(rows.Count, false, Settings.SyncUpdateInterval, stopwatch.Elapsed);
        }

        private void RefreshRanges()
        {
            // Every range queued so far is below the boundary read next, so the comparison finds it as a gap too.
            while (_pendingGaps.TryDequeue(out _))
            {
            }

            var stopwatch = Stopwatch.StartNew();
            var boundary = _getBoundary();
            var remoteRanges = _remote.GetBaseIdRanges();
            var localRanges = _local.GetBaseIdRanges();
            var ranges = SyncRangePlanner.Normalize(SyncRangePlanner.Build(remoteRanges, localRanges), boundary);

            var missing = ranges.Where(r => r.Status == RangeStatus.NotSync).ToList();
            Log.Information("Sync compared databases in " + FormatSeconds(stopwatch.Elapsed) + ": remote has " +
                            remoteRanges.Count + " BaseID ranges, local " + localRanges.Count + "; " + missing.Count +
                            " missing ranges below BaseID " + boundary + " (" +
                            missing.Sum(r => r.Range.BaseIdEnd - r.Range.BaseIdBegin + 1) + " records)");

            lock (_lock)
            {
                // Ranges are rebuilt here, so carry over the ranges the user switched off (same BaseIdBegin).
                // The refresh runs after Resync too, so the task is never inside a range here and BaseIdBegin
                // is still the range's identity. (While a range is being copied BaseIdEnd moves down instead.)
                foreach (var range in ranges)
                {
                    if ((range.Status == RangeStatus.NotSync || range.Status == RangeStatus.Syncing) &&
                        _disabledBegins.Contains(range.Range.BaseIdBegin))
                    {
                        range.SyncEnabled = false;
                    }
                }

                _ranges = ranges;
                SelectNextRange();
            }

            _refreshRequested = false;
            _recheckGaps = false;
        }

        /// <summary>
        /// Adds the queued skipped ranges as NotSync ranges. They are newer than every known range, so the newest is
        /// copied next; the range being copied keeps its progress and continues afterwards.
        /// </summary>
        private void AddPendingGaps()
        {
            var boundary = _getBoundary();
            lock (_lock)
            {
                ReassertSyncing();
                var added = false;
                var realTimeBegin = long.MinValue;
                while (_pendingGaps.TryDequeue(out var gap))
                {
                    var known = _ranges.Any(r => r.Status != RangeStatus.RealTime &&
                                                 r.Range.BaseIdBegin <= gap.BaseIdEnd && r.Range.BaseIdEnd >= gap.BaseIdBegin);
                    if (gap.BaseIdEnd < gap.BaseIdBegin || known)
                    {
                        Log.Debug("Sync ignored skipped records " + gap.BaseIdBegin + "-" + gap.BaseIdEnd + ": already known");
                        continue;
                    }
                    Log.Debug("Sync queued skipped records " + gap.BaseIdBegin + "-" + gap.BaseIdEnd);

                    // Real-time resumed right above the skipped range; the boundary has moved on since then.
                    realTimeBegin = Math.Max(realTimeBegin, gap.BaseIdEnd + 1);

                    if (_syncingIndex >= 0)
                    {
                        _ranges[_syncingIndex].Status = RangeStatus.NotSync;
                    }

                    _ranges.Add(new SyncRange
                    {
                        Range = new BaseIdRange { BaseIdBegin = gap.BaseIdBegin, BaseIdEnd = gap.BaseIdEnd },
                        StartSyncPoint = gap.BaseIdEnd,
                        Status = RangeStatus.NotSync
                    });
                    added = true;
                }

                if (!added)
                {
                    return;
                }

                var realTime = _ranges.FirstOrDefault(r => r.Status == RangeStatus.RealTime);
                if (realTime == null)
                {
                    realTime = new SyncRange { Range = new BaseIdRange(), StartSyncPoint = -1, Status = RangeStatus.RealTime };
                    _ranges.Add(realTime);
                }
                // Where real-time started copying again, not where it has got to: the boundary read here is already a
                // few records above the skipped range, and the difference would show as a gap in the Sync Table.
                var begin = realTimeBegin == long.MinValue ? boundary : Math.Min(boundary, realTimeBegin);
                realTime.Range.BaseIdBegin = begin;
                realTime.Range.BaseIdEnd = begin;

                _ranges.Sort((a, b) => a.Range.BaseIdBegin.CompareTo(b.Range.BaseIdBegin));
                SelectNextRange();
            }
        }

        // Caller holds _lock. Picks the next range only when nothing is being copied; otherwise it only
        // re-points the cursor at the Syncing range (range lists are rebuilt/sorted, so the index goes stale).
        private void SelectNextRange()
        {
            var index = _ranges.FindIndex(r => r.Status == RangeStatus.Syncing);
            if (index >= 0 && !_ranges[index].SyncEnabled)
            {
                // Switched off while it was syncing: park it as pending until switched back on.
                _ranges[index].Status = RangeStatus.NotSync;
                index = -1;
            }
            if (index >= 0)
            {
                _syncingIndex = index;
                _nextBaseId = _ranges[index].Range.BaseIdEnd;
            }
            else
            {
                _syncingIndex = SyncRangePlanner.FindNextRangeIndex(_ranges);
                if (_syncingIndex >= 0)
                {
                    var range = _ranges[_syncingIndex];
                    range.Status = RangeStatus.Syncing;
                    _nextBaseId = range.Range.BaseIdEnd;
                }
                else
                {
                    _nextBaseId = -1;
                }
            }

            if (_syncingIndex >= 0)
            {
                var range = _ranges[_syncingIndex];

                if (range.Range.BaseIdBegin != _loggedRangeBegin)
                {
                    _loggedRangeBegin = range.Range.BaseIdBegin;
                    Log.Information("Sync copying records " + range.Range.BaseIdBegin + "-" + range.Range.BaseIdEnd + " (" +
                                    (range.Range.BaseIdEnd - range.Range.BaseIdBegin + 1) + "), newest first" +
                                    (range.SyncEnabled ? "" : " [UNEXPECTED: disabled]"));
                }
            }
            else
            {
                _nextBaseId = -1;
            }
        }

        /// <summary>
        /// Makes _syncingIndex point at the Syncing range again (there is at most one). Caller holds _lock.
        /// Range refreshes rebuild the list, and a disabled Syncing range is demoted to NotSync, so the stored
        /// index can also point at the wrong row; without this the next read would use another range's cursor.
        /// </summary>
        private void ReassertSyncing()
        {
            var index = _ranges.FindIndex(r => r.Status == RangeStatus.Syncing);
            if (index >= 0)
            {
                _syncingIndex = index;
                _nextBaseId = _ranges[index].Range.BaseIdEnd;
                return;
            }

            _syncingIndex = -1;
            _nextBaseId = -1;
        }

        /// <summary>Also moves the time spent waiting for real-time into this summary.</summary>
        protected override string DescribeState()
        {
            var count = 0;
            long remaining = 0;
            long next;
            lock (_lock)
            {
                foreach (var range in _ranges)
                {
                    // Switched-off ranges are pending, not left to copy: they are never picked up until switched on.
                    if ((range.Status == RangeStatus.NotSync || range.Status == RangeStatus.Syncing) && range.SyncEnabled)
                    {
                        count++;
                        remaining += range.Range.BaseIdEnd - range.Range.BaseIdBegin + 1;
                    }
                }
                next = _nextBaseId;
            }

            var waited = _summaryWaitTime;
            if (IsYielding)
            {
                waited += _yieldClock.Elapsed - _yieldCounted;
                _yieldCounted = _yieldClock.Elapsed;
            }
            _summaryWaitTime = TimeSpan.Zero;

            return (next > 0 ? "next BaseID " + next : "nothing to copy") + ", " + count + " ranges left (" + remaining +
                   " records), waited " + FormatSeconds(waited) + " for real-time" + (IsYielding ? " (waiting now)" : "");
        }
    }
}
