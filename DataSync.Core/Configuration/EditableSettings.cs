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

        public static string[] LogLevelNames { get; } = Enum.GetNames(typeof(Logging.LogLevel));

        public string RigName { get; set; }

        /// <summary>Seconds.</summary>
        public string RealTimeUpdateInterval { get; set; }

        /// <summary>Seconds.</summary>
        public string SyncUpdateInterval { get; set; }

        public bool PrioritizeLatestData { get; set; }
        public bool AdaptiveBatchSize { get; set; }
        public string MinRowLimit { get; set; }
        public string RealTimeBatchMultiplier { get; set; }
        public string SyncBatchMeasurements { get; set; }

        /// <summary>Seconds.</summary>
        public string CommandTimeout { get; set; }

        /// <summary>One-second samples averaged for the displayed copy rate (rows/s).</summary>
        public string RateWindow { get; set; }

        /// <summary>Multiplier applied to the average sync speed to size sync reads; 0 sizes by trying steps instead.</summary>
        public string SyncBatchMultiplier { get; set; }

        public bool SyncMultiplierTest { get; set; }

        /// <summary>Minutes each multiplier is tried in the batch multiplier test.</summary>
        public string SyncMultiplierTestMinutes { get; set; }

        /// <summary>Minutes.</summary>
        public string GapCheckInterval { get; set; }
        public string TimeoutCounterLimit { get; set; }

        public string LogPathDir { get; set; }
        public string RollingInterval { get; set; }
        public bool RollOnFileSizeLimit { get; set; }
        public string FileSizeLimitBytes { get; set; }
        public string RetainedFileCountLimit { get; set; }
        public string LogLevel { get; set; }

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
                SyncUpdateInterval     = ((long)replication.SyncUpdateInterval.TotalSeconds).ToString(Invariant),
                PrioritizeLatestData   = replication.PrioritizeLatestData,
                AdaptiveBatchSize      = replication.AdaptiveBatchSize,
                MinRowLimit            = replication.MinRowLimit.ToString(Invariant),
                RealTimeBatchMultiplier = replication.RealTimeBatchMultiplier.ToString(Invariant),
                SyncBatchMeasurements  = replication.SyncBatchMeasurements.ToString(Invariant),
                CommandTimeout         = ((long)replication.CommandTimeout.TotalSeconds).ToString(Invariant),
                RateWindow             = AppSettings.GetInt(SettingKeys.RateWindow, 10).ToString(Invariant),
                SyncBatchMultiplier    = replication.SyncBatchMultiplier.ToString("0.##", Invariant),
                SyncMultiplierTest     = replication.SyncMultiplierTest,
                SyncMultiplierTestMinutes = replication.SyncMultiplierTestMinutes.ToString(Invariant),
                GapCheckInterval       = ((long)replication.GapCheckInterval.TotalMinutes).ToString(Invariant),
                TimeoutCounterLimit    = replication.FailureLimit.ToString(Invariant),
                LogPathDir             = AppSettings.GetString(SettingKeys.LogPathDir, "logs"),
                RollingInterval        = log.RollingInterval.ToString(),
                RollOnFileSizeLimit    = log.RollOnFileSizeLimit,
                FileSizeLimitBytes     = log.FileSizeLimitBytes.ToString(Invariant),
                RetainedFileCountLimit = log.RetainedFileCountLimit.ToString(Invariant),
                LogLevel               = log.MinimumLevel.ToString()
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
                   ?? CheckInt(SyncUpdateInterval, 0, "Sync pause between batches")
                   ?? CheckInt(MinRowLimit, 1, "Minimum row limit")
                   ?? CheckInt(RealTimeBatchMultiplier, 1, "Real-time batch multiplier")
                   ?? CheckInt(SyncBatchMeasurements, 1, "Sync batch measurements")
                   ?? CheckInt(CommandTimeout, 5, "Command timeout")
                   ?? CheckInt(RateWindow, 1, "Rate window")
                   ?? CheckDouble(SyncBatchMultiplier, 0, "Sync batch multiplier")
                   ?? CheckInt(SyncMultiplierTestMinutes, 1, "Multiplier test minutes")
                   ?? CheckInt(GapCheckInterval, 1, "Gap check interval")
                   ?? CheckInt(TimeoutCounterLimit, 0, "Failures before restart")
                   ?? CheckLogDirectory()
                   ?? (RollingIntervalNames.Contains(RollingInterval) ? null : "Select a log rolling interval.")
                   ?? CheckLong(FileSizeLimitBytes, 1024, "Log file size limit")
                   ?? CheckInt(RetainedFileCountLimit, 1, "Retained log files")
                   ?? (LogLevelNames.Contains(LogLevel) ? null : "Select a log level.");
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
                [SettingKeys.SyncUpdateInterval]     = NormalizeInt(SyncUpdateInterval),
                [SettingKeys.PrioritizeLatestData]   = PrioritizeLatestData ? "true" : "false",
                [SettingKeys.AdaptiveBatchSize]      = AdaptiveBatchSize ? "true" : "false",
                [SettingKeys.MinRowLimit]            = NormalizeInt(MinRowLimit),
                [SettingKeys.RealTimeBatchMultiplier] = NormalizeInt(RealTimeBatchMultiplier),
                [SettingKeys.SyncBatchMeasurements]  = NormalizeInt(SyncBatchMeasurements),
                [SettingKeys.CommandTimeout]         = NormalizeInt(CommandTimeout),
                [SettingKeys.RateWindow]             = NormalizeInt(RateWindow),
                [SettingKeys.SyncBatchMultiplier]    = NormalizeDouble(SyncBatchMultiplier),
                [SettingKeys.SyncMultiplierTest]     = SyncMultiplierTest ? "true" : "false",
                [SettingKeys.SyncMultiplierTestMinutes] = NormalizeInt(SyncMultiplierTestMinutes),
                [SettingKeys.GapCheckInterval]       = NormalizeInt(GapCheckInterval),
                [SettingKeys.TimeoutCounterLimit]    = NormalizeInt(TimeoutCounterLimit),
                [SettingKeys.LogPathDir]             = LogPathDir.Trim(),
                [SettingKeys.RollingInterval]        = RollingInterval,
                [SettingKeys.RollOnFileSizeLimit]    = RollOnFileSizeLimit ? "true" : "false",
                [SettingKeys.FileSizeLimitBytes]     = long.Parse(FileSizeLimitBytes.Trim(), NumberStyles.Integer, Invariant).ToString(Invariant),
                [SettingKeys.RetainedFileCountLimit] = NormalizeInt(RetainedFileCountLimit),
                [SettingKeys.LogLevel]               = LogLevel
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

        private static string CheckDouble(string value, double minimum, string name)
        {
            return double.TryParse(value?.Trim(), NumberStyles.Float, Invariant, out var number) && number >= minimum
                ? null
                : name + " must be a number of at least " + minimum.ToString(Invariant) + ".";
        }

        private static string NormalizeInt(string value)
        {
            return int.Parse(value.Trim(), NumberStyles.Integer, Invariant).ToString(Invariant);
        }

        private static string NormalizeDouble(string value)
        {
            return double.Parse(value.Trim(), NumberStyles.Float, Invariant).ToString("0.##", Invariant);
        }
    }
}
