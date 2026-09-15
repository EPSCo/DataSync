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

        // _ranges, _syncingIndex and _nextBaseId are only changed by the task thread; the lock is for readers.
        private List<SyncRange> _ranges = new List<SyncRange>();
        private int _syncingIndex = -1;
        private long _nextBaseId = -1;
        private volatile bool _refreshRequested = true;
        private bool _recheckGaps; // task thread only: idle, so compare the databases again at the next iteration
        private int _isYielding;

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
            : base("Sync", settings, settings.SyncRowLimit, report)
        {
            _remote = remote;
            _local = local;
            _getBoundary = getBoundary;
            _shouldYield = shouldYield;
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
            lock (_lock)
            {
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

                windowEnd = _nextBaseId;
                windowBegin = Math.Max(windowEnd - requested + 1, _ranges[_syncingIndex].Range.BaseIdBegin);
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
            // Sized by BaseIDs in the window, not rows returned, so sparse ranges are not mistaken for short reads.
            var readTime = stopwatch.Elapsed;
            RecordRead(requested, (int)(windowEnd - windowBegin + 1), readTime);

            try
            {
                _local.SaveBatch(rows);
            }
            catch (Exception ex)
            {
                // The window is not advanced, so it is read and saved again.
                return OnFailure("SaveSyncDataToLocalDatabase", ex);
            }

            Log.Debug("Sync read " + rows.Count + " rows in BaseIDs " + windowBegin + "-" + windowEnd + " in " +
                      FormatSeconds(readTime) + ", saved in " + FormatSeconds(stopwatch.Elapsed - readTime));

            // Every row the remote has in the window is now saved (an empty result means it has none there).
            lock (_lock)
            {
                var current = _ranges[_syncingIndex];
                if (windowBegin <= current.Range.BaseIdBegin)
                {
                    current.Status = RangeStatus.Synced;
                    current.Range.BaseIdEnd = current.StartSyncPoint;
                    current.StartSyncPoint = -1;
                    Log.Information("Sync finished records " + current.Range.BaseIdBegin + "-" + current.Range.BaseIdEnd);
                    SelectNextRange();
                }
                else
                {
                    current.Range.BaseIdEnd = windowBegin - 1;
                    _nextBaseId = current.Range.BaseIdEnd;
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
                var added = false;
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
                realTime.Range.BaseIdBegin = boundary;
                realTime.Range.BaseIdEnd = boundary;

                _ranges.Sort((a, b) => a.Range.BaseIdBegin.CompareTo(b.Range.BaseIdBegin));
                SelectNextRange();
            }
        }

        // Caller holds _lock.
        private void SelectNextRange()
        {
            _syncingIndex = SyncRangePlanner.FindNextRangeIndex(_ranges);
            if (_syncingIndex >= 0)
            {
                var range = _ranges[_syncingIndex];
                range.Status = RangeStatus.Syncing;
                _nextBaseId = range.Range.BaseIdEnd;

                if (range.Range.BaseIdBegin != _loggedRangeBegin)
                {
                    _loggedRangeBegin = range.Range.BaseIdBegin;
                    Log.Information("Sync copying records " + range.Range.BaseIdBegin + "-" + range.Range.BaseIdEnd + " (" +
                                    (range.Range.BaseIdEnd - range.Range.BaseIdBegin + 1) + "), newest first");
                }
            }
            else
            {
                _nextBaseId = -1;
            }
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
                    if (range.Status == RangeStatus.NotSync || range.Status == RangeStatus.Syncing)
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
