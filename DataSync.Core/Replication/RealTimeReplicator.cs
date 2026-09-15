using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using DataSync.Core.Logging;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Copies the newest remote rows as they appear, reading forward from <see cref="NextBaseId"/>.
    /// Rows below its starting position are left to <see cref="GapSyncReplicator"/>.
    /// </summary>
    public class RealTimeReplicator : ReplicationTask
    {
        private readonly IProcessDataStore _remote;
        private readonly IProcessDataStore _local;
        private long _nextBaseId = 1;
        private long _lastLocalBaseId;
        private long _lastLocalRecordTicks; // 0 = unknown

        public RealTimeReplicator(IProcessDataStore remote, IProcessDataStore local, ReplicationSettings settings, Action<string> report)
            : base("RealTime", settings, report)
        {
            _remote = remote;
            _local = local;
        }

        /// <summary>
        /// The next BaseID to copy. Everything below it is either copied or the sync task's job.
        /// </summary>
        public long NextBaseId => Interlocked.Read(ref _nextBaseId);

        public long LastLocalBaseId => Interlocked.Read(ref _lastLocalBaseId);

        public DateTime? LastLocalRecordTime
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastLocalRecordTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks);
            }
        }

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
        }

        public override TimeSpan RunIteration()
        {
            var stopwatch = Stopwatch.StartNew();

            List<TProcessData> rows;
            try
            {
                // Rows after the last copied one, in BaseID order; gaps in the remote BaseIDs are skipped.
                rows = _remote.GetRowsAfter(NextBaseId - 1, Settings.RealTimeRowLimit);
            }
            catch (Exception ex)
            {
                return OnFailure("GetRealTimeDataFromRemoteDatabase", ex);
            }

            try
            {
                _local.SaveBatch(rows);
            }
            catch (Exception ex)
            {
                // NextBaseId is not advanced, so the same rows are read and saved again.
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

            // A full batch means more rows are waiting, so read again without the poll interval.
            return OnSuccess(rows.Count, rows.Count >= Settings.RealTimeRowLimit, Settings.RealTimeUpdateInterval, stopwatch.Elapsed);
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
