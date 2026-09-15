using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using DataSync.Core.Logging;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Copies the newest remote rows as they appear, reading forward from <see cref="NextBaseId"/>.
    /// Rows below its starting position are left to <see cref="GapSyncReplicator"/>.
    /// With <see cref="ReplicationSettings.PrioritizeLatestData"/>, when more rows are waiting than one read carries (after an
    /// outage or on a slow link) it skips ahead to the newest rows and hands the skipped range to the sync task through
    /// <see cref="RangeSkipped"/>, so the local copy gets the latest data first.
    /// </summary>
    public class RealTimeReplicator : ReplicationTask
    {
        /// <summary>Retry cap with PrioritizeLatestData, so new data flows again soon after the link returns.</summary>
        public static readonly TimeSpan LatestFirstMaxRetryDelay = TimeSpan.FromSeconds(10);

        private readonly IProcessDataStore _remote;
        private readonly IProcessDataStore _local;
        private long _nextBaseId = 1;
        private long _lastLocalBaseId;
        private long _lastLocalRecordTicks; // 0 = unknown
        private int _isBehind;

        // Task thread only.
        private bool _checkNewest = true;
        private bool _behindReported;

        public RealTimeReplicator(IProcessDataStore remote, IProcessDataStore local, ReplicationSettings settings, Action<string> report)
            : base("RealTime", settings, settings.RealTimeRowLimit, report)
        {
            _remote = remote;
            _local = local;
        }

        /// <summary>
        /// A range of BaseIDs skipped to reach the newest rows; they still have to be copied. Raised on the task thread.
        /// </summary>
        public event EventHandler<BaseIdRange> RangeSkipped;

        /// <summary>
        /// The next BaseID to copy. Everything below it is either copied or the sync task's job.
        /// </summary>
        public long NextBaseId => Interlocked.Read(ref _nextBaseId);

        public long LastLocalBaseId => Interlocked.Read(ref _lastLocalBaseId);

        protected override TimeSpan MaxRetryDelay =>
            Settings.PrioritizeLatestData ? LatestFirstMaxRetryDelay : DefaultMaxRetryDelay;

        public DateTime? LastLocalRecordTime
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastLocalRecordTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks);
            }
        }

        /// <summary>
        /// True while more rows are waiting than the last read carried.
        /// </summary>
        public bool IsBehind => Volatile.Read(ref _isBehind) != 0;

        /// <summary>
        /// Sets the starting position: after the newest local row, or at the newest remote row when the local copy is
        /// further behind (the rows in between are back-filled by the sync task). Call before <see cref="ReplicationTask.Start"/>.
        /// </summary>
        public void Initialize()
        {
            var localLast = ReadLastRecord(_local, "LoadLastLocalBaseId");
            var remoteLast = ReadLastRecord(_remote, "LoadLastRemoteBaseId");

            if (localLast != null)
            {
                SetLastLocal(localLast);
            }

            var localLastBaseId = localLast?.BaseID ?? 0;
            var remoteLastBaseId = remoteLast?.BaseID ?? 0;
            Interlocked.Exchange(ref _nextBaseId, Math.Max(remoteLastBaseId, localLastBaseId + 1));

            Log.Information("RealTime starts at BaseID " + NextBaseId +
                            " (newest local " + (localLast == null ? "unknown" : localLastBaseId.ToString()) +
                            ", newest remote " + (remoteLast == null ? "unknown" : remoteLastBaseId.ToString()) + ")");
        }

        public override TimeSpan RunIteration()
        {
            var stopwatch = Stopwatch.StartNew();
            var requested = Batch.Current;

            if (Settings.PrioritizeLatestData && _checkNewest)
            {
                try
                {
                    SkipAheadIfBehind(requested);
                }
                catch (Exception ex)
                {
                    return OnReadFailure("GetNewestRemoteRecord", ex);
                }
            }

            List<TProcessData> rows;
            try
            {
                // Rows after the last copied one, in BaseID order; gaps in the remote BaseIDs are skipped.
                rows = _remote.GetRowsAfter(NextBaseId - 1, requested);
            }
            catch (Exception ex)
            {
                // Rows pile up while the link is down: see how far behind we are before the next read.
                _checkNewest = true;
                return OnReadFailure("GetRealTimeDataFromRemoteDatabase", ex);
            }
            var readTime = stopwatch.Elapsed;
            RecordRead(requested, rows.Count, readTime);

            try
            {
                _local.SaveBatch(rows);
            }
            catch (Exception ex)
            {
                // NextBaseId is not advanced, so the same rows are read and saved again (unless newer rows come first).
                _checkNewest = true;
                return OnFailure("SaveRealTimeDataToLocalDatabase", ex);
            }

            if (rows.Count > 0)
            {
                var newest = rows[0];
                foreach (var row in rows)
                {
                    if (row.BaseID > newest.BaseID)
                    {
                        newest = row;
                    }
                }

                if (newest.BaseID >= LastLocalBaseId)
                {
                    SetLastLocal(newest);
                }
                Interlocked.Exchange(ref _nextBaseId, Math.Max(NextBaseId, newest.BaseID + 1));
            }

            Log.Debug("RealTime read " + rows.Count + " of " + requested + " rows" +
                      (rows.Count > 0 ? " (BaseID " + rows[0].BaseID + "-" + rows[rows.Count - 1].BaseID + ")" : "") +
                      " in " + FormatSeconds(readTime) + ", saved in " + FormatSeconds(stopwatch.Elapsed - readTime));

            // A full batch means more rows are waiting, so read again without the poll interval.
            var backlog = rows.Count >= requested;
            _checkNewest = backlog;
            Volatile.Write(ref _isBehind, backlog ? 1 : 0);
            if (!backlog && _behindReported)
            {
                _behindReported = false;
                Log.Information("RealTime caught up with the newest remote data");
                Report("RealTime caught up with the newest remote data");
            }

            return OnSuccess(rows.Count, backlog, Settings.RealTimeUpdateInterval, stopwatch.Elapsed);
        }

        /// <summary>
        /// When more rows are waiting than one read of <paramref name="batchSize"/> carries, moves to the newest
        /// <paramref name="batchSize"/> rows and raises <see cref="RangeSkipped"/> for the rows in between.
        /// </summary>
        private void SkipAheadIfBehind(int batchSize)
        {
            var newest = _remote.GetLastRecord().BaseID;
            var next = NextBaseId;
            var waiting = newest - next + 1;
            if (waiting <= batchSize)
            {
                return;
            }

            var newStart = newest - batchSize + 1;

            // Move first, then hand over the range: the sync task never copies from the real-time position up, so a
            // range it also finds by comparing the databases is at worst queued twice (and ignored), never lost.
            Interlocked.Exchange(ref _nextBaseId, newStart);
            Volatile.Write(ref _isBehind, 1);

            var message = "RealTime is " + waiting + " records behind: copying the newest from " + newStart +
                          "; records " + next + "-" + (newStart - 1) + " are left to the sync task";
            Log.Information(message);
            if (!_behindReported)
            {
                _behindReported = true;
                Report(message);
            }

            RangeSkipped?.Invoke(this, new BaseIdRange { BaseIdBegin = next, BaseIdEnd = newStart - 1 });
        }

        protected override string DescribeState()
        {
            var time = LastLocalRecordTime;
            return "next BaseID " + NextBaseId + ", newest local BaseID " + LastLocalBaseId +
                   (time.HasValue
                       ? " recorded " + time.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                         " (" + FormatAge(DateTime.Now - time.Value) + " old by this computer's clock)"
                       : "") +
                   (IsBehind ? ", behind" : ", up to date");
        }

        /// <summary>Seconds below a minute, otherwise [days.]hh:mm:ss; negative when the record is from the future.</summary>
        private static string FormatAge(TimeSpan age)
        {
            if (Math.Abs(age.TotalSeconds) < 60)
            {
                return FormatSeconds(age);
            }
            return (age < TimeSpan.Zero ? "-" : "") + age.Duration().ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private TProcessData ReadLastRecord(IProcessDataStore store, string operation)
        {
            try
            {
                return store.GetLastRecord();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception in " + operation);
                Report("Exception in " + operation + ": " + ex.Message);
                return null;
            }
        }

        private void SetLastLocal(TProcessData record)
        {
            Interlocked.Exchange(ref _lastLocalBaseId, record.BaseID);
            Interlocked.Exchange(ref _lastLocalRecordTicks, record.DateTimeRecord.Ticks);
        }
    }
}
