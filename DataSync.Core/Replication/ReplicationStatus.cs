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

        /// <summary>More rows are waiting than the real-time task's last read carried.</summary>
        public bool RealTimeBehind { get; set; }

        /// <summary>The sync task is waiting for the real-time task to catch up or recover.</summary>
        public bool SyncYielding { get; set; }

        /// <summary>Rows the next remote read of each task asks for.</summary>
        public int RealTimeBatchSize { get; set; }
        public int SyncBatchSize { get; set; }

        /// <summary>False when the batch sizes are fixed at the row limits.</summary>
        public bool AdaptiveBatchSize { get; set; }

        /// <summary>Running totals, for computing copy rates.</summary>
        public long RealTimeRowsCopied { get; set; }
        public long SyncRowsCopied { get; set; }

        public int RealTimeFailureCount { get; set; }
        public int SyncFailureCount { get; set; }

        /// <summary>
        /// True when a task has failed more than FailureLimit times in a row. The app restarts only if both databases are
        /// reachable: during an outage a restart cannot help, and the tasks keep retrying on their own.
        /// </summary>
        public bool FailureLimitExceeded { get; set; }

        public List<SyncRange> Ranges { get; set; } = new List<SyncRange>();
    }
}
