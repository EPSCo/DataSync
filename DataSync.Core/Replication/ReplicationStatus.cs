using System;
using System.Collections.Generic;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// A point-in-time copy of the replication state for display; safe to read from the UI thread.
    /// </summary>
    public class ReplicationStatus
    {
        public TaskState RealTimeState { get; set; }
        public TaskState SyncState { get; set; }

        public long RealTimeNextBaseId { get; set; }
        public long LastLocalBaseId { get; set; }
        public DateTime? LastLocalRecordTime { get; set; }
        public long SyncNextBaseId { get; set; }

        public int RealTimeFailureCount { get; set; }
        public int SyncFailureCount { get; set; }

        /// <summary>
        /// True when a task has failed more than FailureLimit times in a row; the app should restart.
        /// </summary>
        public bool FailureLimitExceeded { get; set; }

        public List<SyncRange> Ranges { get; set; } = new List<SyncRange>();
    }
}
