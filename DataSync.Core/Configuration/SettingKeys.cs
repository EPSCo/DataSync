namespace DataSync.Core.Configuration
{
    /// <summary>
    /// App.config &lt;appSettings&gt; keys. The names are shared with DDRREP and must not change.
    /// </summary>
    public static class SettingKeys
    {
        public const string ProgramName = "ProgramName";
        public const string ProgramDetails = "ProgramDetails";
        public const string CompanyCopyright = "CompanyCopyright";
        public const string RigName = "RigName";

        public const string LogPathDir = "LogPathDir";
        public const string RollingInterval = "RollingInterval";
        public const string RollOnFileSizeLimit = "RollOnFileSizeLimit";
        public const string FileSizeLimitBytes = "FileSizeLimitBytes";
        public const string RetainedFileCountLimit = "RetainedFileCountLimit";

        public const string RealTimeUpdateInterval = "RealTimeUpdateInterval";
        public const string RealTimeRowLimit = "RealTimeRowLimit";
        public const string SyncUpdateInterval = "SyncUpdateInterval";
        public const string SyncRowLimit = "SyncRowLimit";
        public const string MaxRowsPerSecond = "MaxRowsPerSecond";
        public const string GapCheckInterval = "GapCheckInterval";
        public const string TimeoutCounterLimit = "TimeoutCounterLimit";
    }
}
