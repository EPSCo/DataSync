using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DataSync.Core.Logging;
using DataSync.Core.Replication;

namespace DataSync.Core.Configuration
{
    /// <summary>
    /// The settings the user can change from the Settings dialog, as text for editing. Values take effect after a restart.
    /// </summary>
    public class EditableSettings
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string[] RollingIntervalNames { get; } = Enum.GetNames(typeof(Logging.RollingInterval));

        public string RigName { get; set; }

        /// <summary>Seconds.</summary>
        public string RealTimeUpdateInterval { get; set; }
        public string RealTimeRowLimit { get; set; }

        /// <summary>Seconds.</summary>
        public string SyncUpdateInterval { get; set; }
        public string SyncRowLimit { get; set; }

        /// <summary>Minutes.</summary>
        public string GapCheckInterval { get; set; }
        public string MaxRowsPerSecond { get; set; }
        public string TimeoutCounterLimit { get; set; }

        public string LogPathDir { get; set; }
        public string RollingInterval { get; set; }
        public bool RollOnFileSizeLimit { get; set; }
        public string FileSizeLimitBytes { get; set; }
        public string RetainedFileCountLimit { get; set; }

        /// <summary>
        /// The current values, as the application would use them (invalid or missing values show their defaults).
        /// </summary>
        public static EditableSettings Load()
        {
            var replication = ReplicationSettings.FromAppSettings();
            var log = LogSettings.FromAppSettings();

            return new EditableSettings
            {
                RigName                = AppSettings.GetString(SettingKeys.RigName, "Unknown"),
                RealTimeUpdateInterval = ((long)replication.RealTimeUpdateInterval.TotalSeconds).ToString(Invariant),
                RealTimeRowLimit       = replication.RealTimeRowLimit.ToString(Invariant),
                SyncUpdateInterval     = ((long)replication.SyncUpdateInterval.TotalSeconds).ToString(Invariant),
                SyncRowLimit           = replication.SyncRowLimit.ToString(Invariant),
                GapCheckInterval       = ((long)replication.GapCheckInterval.TotalMinutes).ToString(Invariant),
                MaxRowsPerSecond       = replication.MaxRowsPerSecond.ToString(Invariant),
                TimeoutCounterLimit    = replication.FailureLimit.ToString(Invariant),
                LogPathDir             = AppSettings.GetString(SettingKeys.LogPathDir, "logs"),
                RollingInterval        = log.RollingInterval.ToString(),
                RollOnFileSizeLimit    = log.RollOnFileSizeLimit,
                FileSizeLimitBytes     = log.FileSizeLimitBytes.ToString(Invariant),
                RetainedFileCountLimit = log.RetainedFileCountLimit.ToString(Invariant)
            };
        }

        /// <summary>
        /// A message describing the first invalid value, or null when all values are valid.
        /// </summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(RigName))
            {
                return "Rig name is required.";
            }

            return CheckInt(RealTimeUpdateInterval, 0, "Real-time update interval")
                   ?? CheckInt(RealTimeRowLimit, 1, "Real-time row limit")
                   ?? CheckInt(SyncUpdateInterval, 0, "Sync pause between batches")
                   ?? CheckInt(SyncRowLimit, 1, "Sync row limit")
                   ?? CheckInt(GapCheckInterval, 1, "Gap check interval")
                   ?? CheckInt(MaxRowsPerSecond, 0, "Max rows per second")
                   ?? CheckInt(TimeoutCounterLimit, 0, "Failures before restart")
                   ?? CheckLogDirectory()
                   ?? (RollingIntervalNames.Contains(RollingInterval) ? null : "Select a log rolling interval.")
                   ?? CheckLong(FileSizeLimitBytes, 1024, "Log file size limit")
                   ?? CheckInt(RetainedFileCountLimit, 1, "Retained log files");
        }

        /// <summary>
        /// The values to write to App.config, normalized. Call only when <see cref="Validate"/> returns null.
        /// </summary>
        public IDictionary<string, string> ToValues()
        {
            return new Dictionary<string, string>
            {
                [SettingKeys.RigName]                = RigName.Trim(),
                [SettingKeys.RealTimeUpdateInterval] = NormalizeInt(RealTimeUpdateInterval),
                [SettingKeys.RealTimeRowLimit]       = NormalizeInt(RealTimeRowLimit),
                [SettingKeys.SyncUpdateInterval]     = NormalizeInt(SyncUpdateInterval),
                [SettingKeys.SyncRowLimit]           = NormalizeInt(SyncRowLimit),
                [SettingKeys.GapCheckInterval]       = NormalizeInt(GapCheckInterval),
                [SettingKeys.MaxRowsPerSecond]       = NormalizeInt(MaxRowsPerSecond),
                [SettingKeys.TimeoutCounterLimit]    = NormalizeInt(TimeoutCounterLimit),
                [SettingKeys.LogPathDir]             = LogPathDir.Trim(),
                [SettingKeys.RollingInterval]        = RollingInterval,
                [SettingKeys.RollOnFileSizeLimit]    = RollOnFileSizeLimit ? "true" : "false",
                [SettingKeys.FileSizeLimitBytes]     = long.Parse(FileSizeLimitBytes.Trim(), NumberStyles.Integer, Invariant).ToString(Invariant),
                [SettingKeys.RetainedFileCountLimit] = NormalizeInt(RetainedFileCountLimit)
            };
        }

        /// <summary>
        /// Validates and writes the values to the application's config file.
        /// </summary>
        public void Save()
        {
            var error = Validate();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            ConfigFile.SaveAppSettings(ToValues());
        }

        private static string CheckInt(string value, int minimum, string name)
        {
            return int.TryParse(value?.Trim(), NumberStyles.Integer, Invariant, out var number) && number >= minimum
                ? null
                : name + " must be a whole number of at least " + minimum + ".";
        }

        private static string CheckLong(string value, long minimum, string name)
        {
            return long.TryParse(value?.Trim(), NumberStyles.Integer, Invariant, out var number) && number >= minimum
                ? null
                : name + " must be a whole number of at least " + minimum + ".";
        }

        private string CheckLogDirectory()
        {
            const string message = "Log directory must be a valid folder path.";
            if (string.IsNullOrWhiteSpace(LogPathDir) || LogPathDir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return message;
            }

            try
            {
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogPathDir.Trim()));
                return null;
            }
            catch (Exception)
            {
                return message;
            }
        }

        private static string NormalizeInt(string value)
        {
            return int.Parse(value.Trim(), NumberStyles.Integer, Invariant).ToString(Invariant);
        }
    }
}
