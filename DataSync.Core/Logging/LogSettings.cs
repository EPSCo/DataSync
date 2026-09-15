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
    /// How important a log message is. Messages below <see cref="LogSettings.MinimumLevel"/> are not written.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Every remote read, for troubleshooting a network. Writes much more.</summary>
        Debug,
        Information,
        Warning,
        Error
    }

    /// <summary>
    /// Log file settings, read from App.config with <see cref="FromAppSettings"/>.
    /// </summary>
    public class LogSettings
    {
        public string Directory { get; set; }
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
        public RollingInterval RollingInterval { get; set; } = RollingInterval.Day;
        public bool RollOnFileSizeLimit { get; set; } = true;
        public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;
        public int RetainedFileCountLimit { get; set; } = 10;

        public static LogSettings FromAppSettings()
        {
            var directory = AppSettings.GetString(SettingKeys.LogPathDir, "logs");
            if (!Path.IsPathRooted(directory))
            {
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);
            }

            return new LogSettings
            {
                Directory              = directory,
                MinimumLevel           = Enum.TryParse(AppSettings.GetString(SettingKeys.LogLevel, "Information"), true, out LogLevel level)
                                             ? level
                                             : LogLevel.Information,
                RollingInterval        = Enum.TryParse(AppSettings.GetString(SettingKeys.RollingInterval, "Day"), true, out RollingInterval interval)
                                             ? interval
                                             : RollingInterval.Day,
                RollOnFileSizeLimit    = AppSettings.GetBool(SettingKeys.RollOnFileSizeLimit, true),
                FileSizeLimitBytes     = Math.Max(1024, AppSettings.GetLong(SettingKeys.FileSizeLimitBytes, 10 * 1024 * 1024)),
                RetainedFileCountLimit = Math.Max(1, AppSettings.GetInt(SettingKeys.RetainedFileCountLimit, 10))
            };
        }
    }
}
