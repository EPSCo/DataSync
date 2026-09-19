using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DataSync.Core.Logging;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Runs the real-time and sync tasks together. The UI starts/stops it and polls <see cref="GetStatus"/>.
    /// </summary>
    public class ReplicationEngine
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

        private readonly ReplicationSettings _settings;
        private readonly RealTimeReplicator _realTime;
        private readonly GapSyncReplicator _sync;
        private readonly SyncBatchSize _syncBatch;
        private readonly SyncMultiplierExperiment _multiplierTest;

        public ReplicationEngine(IProcessDataStore remote, IProcessDataStore local, ReplicationSettings settings)
        {
            _settings = settings;
            _realTime = new RealTimeReplicator(remote, local, settings, Report);

            // Latest data first: the sync task leaves the link to the real-time task while it is behind or failing,
            // and copies the ranges real-time skipped as soon as they are handed over.
            Func<bool> syncShouldYield = null;
            if (settings.PrioritizeLatestData)
            {
                syncShouldYield = () => _realTime.IsBehind || _realTime.FailureCount > 0;
            }
            _syncBatch = settings.CreateSyncBatchSize();
            _sync = new GapSyncReplicator(remote, local, () => _realTime.NextBaseId, settings, Report, syncShouldYield,
                                          _syncBatch);
            _realTime.RangeSkipped += (s, range) => _sync.AddGap(range);

            if (settings.SyncMultiplierTest && settings.AdaptiveBatchSize)
            {
                _multiplierTest = new SyncMultiplierExperiment(_syncBatch, () => _sync.RowsCopied,
                                                               SyncMultiplierExperiment.DefaultMultipliers,
                                                               TimeSpan.FromMinutes(settings.SyncMultiplierTestMinutes),
                                                               Report);
            }
        }

        /// <summary>
        /// Messages for the user. Raised on background threads.
        /// </summary>
        public event EventHandler<string> MessageLogged;

        /// <summary>
        /// Reads the starting position from both databases, then starts both tasks. Blocks while reading.
        /// </summary>
        public void Start()
        {
            Log.Information("Replication starting: " + _settings.Describe());
            _realTime.Initialize();
            _sync.RequestRefresh();
            _realTime.Start();
            _sync.Start();
            _multiplierTest?.Start();
        }

        public void PauseSync()
        {
            Log.Information("Pausing the sync task");
            _sync.RequestStop();
        }

        /// <summary>
        /// Switches one sync range off (skip it) or back on. The task picks it up on its next iteration.
        /// </summary>
        public void SetRangeEnabled(long baseIdBegin, bool enabled)
        {
            _sync.SetRangeEnabled(baseIdBegin, enabled);
        }

        public void ResumeSync()
        {
            if (_sync.State != TaskState.Stop || !WaitForStop(_sync.Completion))
            {
                return;
            }

            Log.Information("Resuming the sync task");
            _sync.RequestRefresh();
            _sync.Start();
        }

        /// <summary>
        /// Cancels both tasks and waits for them to finish (up to 30 seconds).
        /// </summary>
        public void Stop()
        {
            Log.Information("Replication stopping");
            var stopwatch = Stopwatch.StartNew();
            _multiplierTest?.Stop();
            _realTime.RequestStop();
            _sync.RequestStop();
            if (WaitForStop(_realTime.Completion, _sync.Completion))
            {
                Log.Information("Replication stopped in " + stopwatch.ElapsedMilliseconds + " ms");
            }
        }

        public ReplicationStatus GetStatus()
        {
            var realTimeFailures = _realTime.FailureCount;
            var syncFailures = _sync.FailureCount;

            return new ReplicationStatus
            {
                RealTimeState        = _realTime.State,
                SyncState            = _sync.State,
                RealTimeNextBaseId   = _realTime.NextBaseId,
                LastLocalBaseId      = _realTime.LastLocalBaseId,
                LastLocalRecordTime  = _realTime.LastLocalRecordTime,
                SyncNextBaseId       = _sync.NextBaseId,
                RealTimeBehind       = _realTime.IsBehind,
                SyncYielding         = _sync.IsYielding,
                RealTimeBatchSize    = _realTime.BatchSize,
                SyncBatchSize        = _sync.BatchSize,
                AdaptiveBatchSize    = _settings.AdaptiveBatchSize,
                RealTimeRowsCopied   = _realTime.RowsCopied,
                SyncRowsCopied       = _sync.RowsCopied,
                RealTimeFailureCount = realTimeFailures,
                SyncFailureCount     = syncFailures,
                FailureLimitExceeded = realTimeFailures > _settings.FailureLimit || syncFailures > _settings.FailureLimit,
                Ranges               = _sync.GetRangesSnapshot()
            };
        }

        private static bool WaitForStop(params Task[] tasks)
        {
            try
            {
                if (Task.WaitAll(tasks, StopTimeout))
                {
                    return true;
                }

                Log.Warning("Replication tasks did not stop within " + StopTimeout);
                return false;
            }
            catch (AggregateException ex)
            {
                Log.Error(ex, "Replication task failed while stopping");
                return true;
            }
        }

        private void Report(string message)
        {
            MessageLogged?.Invoke(this, message);
        }
    }
}
