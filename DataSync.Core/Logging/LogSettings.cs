using System;
using System.IO;
using DataSync.Core.Configuration;

namespace DataSync.Core.Logging
{
    public enum RollingInterval
    {
        Infinite,
        Year,
        Month,
        Day,
        Hour,
        Minute
    }

    /// <summary>
    /// Log file settings, read from App.config with <see cref="FromAppSettings"/>.
    /// </summary>
    public class LogSettings
    {
        public string Directory { get; set; }
        public RollingInterval RollingInterval { get; set; } = RollingInterval.Day;
        public bool RollOnFileSizeLimit { get; set; } = true;
        public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;
        public int RetainedFileCountLimit { get; set; } = 10;

        public static LogSettings FromAppSettings()
        {
            var directory = AppSettings.GetString("LogPathDir", "logs");
            if (!Path.IsPathRooted(directory))
            {
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            }

            return new LogSettings
            {
                Directory              = directory,
                RollingInterval        = Enum.TryParse(AppSettings.GetString("RollingInterval", "Day"), true, out RollingInterval interval)
                                             ? interval
                                             : RollingInterval.Day,
                RollOnFileSizeLimit    = AppSettings.GetBool("RollOnFileSizeLimit", true),
                FileSizeLimitBytes     = Math.Max(1024, AppSettings.GetLong("FileSizeLimitBytes", 10 * 1024 * 1024)),
                RetainedFileCountLimit = Math.Max(1, AppSettings.GetInt("RetainedFileCountLimit", 10))
            };
        }
    }
}
