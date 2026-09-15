using System;
using DataSync.Core.Configuration;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Replication tuning, normally read from App.config with <see cref="FromAppSettings"/>.
    /// </summary>
    public class ReplicationSettings
    {
        /// <summary>Maximum rows per real-time read.</summary>
        public int RealTimeRowLimit { get; set; } = 1000;

        /// <summary>Wait between real-time reads once caught up (no wait while full batches are returned).</summary>
        public TimeSpan RealTimeUpdateInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Maximum rows per sync (back-fill) batch.</summary>
        public int SyncRowLimit { get; set; } = 1000;

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
                RealTimeUpdateInterval = TimeSpan.FromSeconds(Math.Max(0, AppSettings.GetInt("RealTimeUpdateInterval", 1))),
                RealTimeRowLimit       = Math.Max(1, AppSettings.GetInt("RealTimeRowLimit", 1000)),
                SyncUpdateInterval     = TimeSpan.FromSeconds(Math.Max(0, AppSettings.GetInt("SyncUpdateInterval", 0))),
                SyncRowLimit           = Math.Max(1, AppSettings.GetInt("SyncRowLimit", 1000)),
                MaxRowsPerSecond       = Math.Max(0, AppSettings.GetInt("MaxRowsPerSecond", 5000)),
                GapCheckInterval       = TimeSpan.FromMinutes(Math.Max(1, AppSettings.GetInt("GapCheckInterval", 10))),
                FailureLimit           = Math.Max(0, AppSettings.GetInt("TimeoutCounterLimit", 10))
            };
        }
    }
}
