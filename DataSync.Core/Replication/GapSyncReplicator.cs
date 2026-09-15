using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Back-fills rows missing locally below the real-time position: compares the BaseID ranges of both databases,
    /// copies the newest missing range first, reading it backwards in windows of SyncRowLimit, then the next one.
    /// When nothing is left it checks again for new gaps every GapCheckInterval.
    /// </summary>
    public class GapSyncReplicator : ReplicationTask
    {
        private readonly object _lock = new object();
        private readonly IProcessDataStore _remote;
        private readonly IProcessDataStore _local;
        private readonly Func<long> _getBoundary;

        // _ranges, _syncingIndex and _nextBaseId are only changed by the task thread; the lock is for readers.
        private List<SyncRange> _ranges = new List<SyncRange>();
        private int _syncingIndex = -1;
        private long _nextBaseId = -1;
        private volatile bool _refreshPending = true;

        /// <param name="getBoundary">Returns the real-time position; BaseIDs from there up are not synced.</param>
        public GapSyncReplicator(IProcessDataStore remote, IProcessDataStore local, Func<long> getBoundary,
                                 ReplicationSettings settings, Action<string> report)
            : base("Sync", settings, report)
        {
            _remote = remote;
            _local = local;
            _getBoundary = getBoundary;
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
            _refreshPending = true;
        }

        public override TimeSpan RunIteration()
        {
            if (_refreshPending)
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

            long windowBegin;
            long windowEnd;
            lock (_lock)
            {
                if (_syncingIndex < 0)
                {
                    // Nothing left to back-fill: look again for gaps (e.g. rows the remote wrote late) after a while.
                    State = TaskState.NoOldData;
                    _refreshPending = true;
                    return Settings.GapCheckInterval;
                }

                windowEnd = _nextBaseId;
                windowBegin = Math.Max(windowEnd - Settings.SyncRowLimit + 1, _ranges[_syncingIndex].Range.BaseIdBegin);
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
                return OnFailure("GetSyncDataFromRemoteDatabase", ex);
            }

            try
            {
                _local.SaveBatch(rows);
            }
            catch (Exception ex)
            {
                // The window is not advanced, so it is read and saved again.
                return OnFailure("SaveSyncDataToLocalDatabase", ex);
            }

            // Every row the remote has in the window is now saved (an empty result means it has none there).
            lock (_lock)
            {
                var current = _ranges[_syncingIndex];
                if (windowBegin <= current.Range.BaseIdBegin)
                {
                    current.Status = RangeStatus.Synced;
                    current.Range.BaseIdEnd = current.StartSyncPoint;
                    current.StartSyncPoint = -1;
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
            var boundary = _getBoundary();
            var remoteRanges = _remote.GetBaseIdRanges();
            var localRanges = _local.GetBaseIdRanges();
            var ranges = SyncRangePlanner.Normalize(SyncRangePlanner.Build(remoteRanges, localRanges), boundary);

            lock (_lock)
            {
                _ranges = ranges;
                SelectNextRange();
            }

            _refreshPending = false;
        }

        // Caller holds _lock.
        private void SelectNextRange()
        {
            _syncingIndex = SyncRangePlanner.FindNextRangeIndex(_ranges);
            if (_syncingIndex >= 0)
            {
                _ranges[_syncingIndex].Status = RangeStatus.Syncing;
                _nextBaseId = _ranges[_syncingIndex].Range.BaseIdEnd;
            }
            else
            {
                _nextBaseId = -1;
            }
        }
    }
}
