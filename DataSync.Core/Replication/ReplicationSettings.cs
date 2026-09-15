using System;
using System.Globalization;
using DataSync.Core.Configuration;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Replication tuning, normally read from App.config with <see cref="FromAppSettings"/>.
    /// </summary>
    public class ReplicationSettings
    {
        /// <summary>Maximum rows per real-time read (the fixed size when <see cref="AdaptiveBatchSize"/> is off).</summary>
        public int RealTimeRowLimit { get; set; } = 1000;

        /// <summary>Wait between real-time reads once caught up (no wait while full batches are returned).</summary>
        public TimeSpan RealTimeUpdateInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Maximum rows per sync (back-fill) batch (the fixed size when <see cref="AdaptiveBatchSize"/> is off).</summary>
        public int SyncRowLimit { get; set; } = 1000;

        /// <summary>
        /// Sizes each remote read between <see cref="MinRowLimit"/> and the row limit from recent read times, aiming at
        /// <see cref="TargetBatchTime"/>. Off: every read uses the row limit, as DDRREP did.
        /// </summary>
        public bool AdaptiveBatchSize { get; set; } = true;

        /// <summary>Smallest read size when adaptive; also the size of the first read.</summary>
        public int MinRowLimit { get; set; } = 20;

        /// <summary>How long one remote read should take when adaptive.</summary>
        public TimeSpan TargetBatchTime { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>SQL command timeout for replication reads and writes.</summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Latest data first: when more rows are waiting than one read carries, the real-time task skips to the newest
        /// rows and the sync task back-fills the skipped range at once, newest first; the sync task also pauses while
        /// real-time is behind or failing. Off: real-time catches up oldest-first and both tasks run independently, as DDRREP did.
        /// </summary>
        public bool PrioritizeLatestData { get; set; } = true;

        /// <summary>Pause between sync batches.</summary>
        public TimeSpan SyncUpdateInterval { get; set; } = TimeSpan.Zero;

        /// <summary>Upper limit on rows copied per second by each task; 0 = no limit.</summary>
        public int MaxRowsPerSecond { get; set; } = 5000;

        /// <summary>Wait between checks for new gaps once there is nothing left to sync.</summary>
        public TimeSpan GapCheckInterval { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>Consecutive failures of one task after which the app should restart.</summary>
        public int FailureLimit { get; set; } = 10;

        public static ReplicationSettings FromAppSettings()
        {
            return new ReplicationSettings
            {
                RealTimeUpdateInterval = TimeSpan.FromSeconds(Math.Max(0, AppSettings.GetInt(SettingKeys.RealTimeUpdateInterval, 1))),
                RealTimeRowLimit       = Math.Max(1, AppSettings.GetInt(SettingKeys.RealTimeRowLimit, 1000)),
                SyncUpdateInterval     = TimeSpan.FromSeconds(Math.Max(0, AppSettings.GetInt(SettingKeys.SyncUpdateInterval, 0))),
                SyncRowLimit           = Math.Max(1, AppSettings.GetInt(SettingKeys.SyncRowLimit, 1000)),
                MaxRowsPerSecond       = Math.Max(0, AppSettings.GetInt(SettingKeys.MaxRowsPerSecond, 5000)),
                GapCheckInterval       = TimeSpan.FromMinutes(Math.Max(1, AppSettings.GetInt(SettingKeys.GapCheckInterval, 10))),
                FailureLimit           = Math.Max(0, AppSettings.GetInt(SettingKeys.TimeoutCounterLimit, 10)),
                AdaptiveBatchSize      = AppSettings.GetBool(SettingKeys.AdaptiveBatchSize, true),
                MinRowLimit            = Math.Max(1, AppSettings.GetInt(SettingKeys.MinRowLimit, 20)),
                TargetBatchTime        = TimeSpan.FromSeconds(Math.Max(1, AppSettings.GetInt(SettingKeys.TargetBatchSeconds, 3))),
                CommandTimeout         = TimeSpan.FromSeconds(Math.Max(5, AppSettings.GetInt(SettingKeys.CommandTimeout, 60))),
                PrioritizeLatestData   = AppSettings.GetBool(SettingKeys.PrioritizeLatestData, true)
            };
        }

        /// <summary>
        /// The values in effect, for the log.
        /// </summary>
        public string Describe()
        {
            return "RealTimeRowLimit=" + RealTimeRowLimit +
                   ", RealTimeUpdateInterval=" + Seconds(RealTimeUpdateInterval) +
                   ", SyncRowLimit=" + SyncRowLimit +
                   ", SyncUpdateInterval=" + Seconds(SyncUpdateInterval) +
                   ", PrioritizeLatestData=" + PrioritizeLatestData +
                   ", AdaptiveBatchSize=" + AdaptiveBatchSize +
                   ", MinRowLimit=" + MinRowLimit +
                   ", TargetBatchTime=" + Seconds(TargetBatchTime) +
                   ", CommandTimeout=" + Seconds(CommandTimeout) +
                   ", MaxRowsPerSecond=" + MaxRowsPerSecond +
                   ", GapCheckInterval=" + Seconds(GapCheckInterval) +
                   ", FailureLimit=" + FailureLimit;
        }

        private static string Seconds(TimeSpan time)
        {
            return time.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s";
        }

        /// <summary>
        /// The read-size controller for a task with the given row limit: adaptive from MinRowLimit, or fixed at the limit.
        /// </summary>
        public AdaptiveBatchSize CreateBatchSize(int rowLimit)
        {
            return new AdaptiveBatchSize(AdaptiveBatchSize ? MinRowLimit : rowLimit, rowLimit, TargetBatchTime);
        }
    }
}
