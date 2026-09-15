using System;
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

        public ReplicationEngine(IProcessDataStore remote, IProcessDataStore local, ReplicationSettings settings)
        {
            _settings = settings;
            _realTime = new RealTimeReplicator(remote, local, settings, Report);
            _sync = new GapSyncReplicator(remote, local, () => _realTime.NextBaseId, settings, Report);
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
            _realTime.Initialize();
            _sync.RequestRefresh();
            _realTime.Start();
            _sync.Start();
        }

        public void PauseSync()
        {
            _sync.RequestStop();
        }

        public void ResumeSync()
        {
            if (_sync.State != TaskState.Stop || !WaitForStop(_sync.Completion))
            {
                return;
            }

            _sync.RequestRefresh();
            _sync.Start();
        }

        /// <summary>
        /// Cancels both tasks and waits for them to finish (up to 30 seconds).
        /// </summary>
        public void Stop()
        {
            _realTime.RequestStop();
            _sync.RequestStop();
            WaitForStop(_realTime.Completion, _sync.Completion);
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
