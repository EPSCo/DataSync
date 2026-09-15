namespace DataSync.Core.Models
{
    /// <summary>
    /// State of a replication task, shown as-is in the UI.
    /// </summary>
    public enum TaskState
    {
        /// <summary>Running, with nothing left to back-fill; waiting to check for new gaps.</summary>
        NoOldData,
        Downloading,
        Stop
    }

    public enum RangeStatus
    {
        RealTime,
        Synced,
        Syncing,
        NotSync
    }

    /// <summary>
    /// A contiguous run of BaseIDs (inclusive).
    /// </summary>
    public class BaseIdRange
    {
        public long BaseIdBegin { get; set; }
        public long BaseIdEnd { get; set; }
    }

    /// <summary>
    /// A BaseID range and whether it is present locally.
    /// </summary>
    public class SyncRange
    {
        public BaseIdRange Range { get; set; }

        /// <summary>For a NotSync/Syncing range, the BaseID the back-fill started from; otherwise -1.</summary>
        public long StartSyncPoint { get; set; }

        public RangeStatus Status { get; set; }
    }
}
