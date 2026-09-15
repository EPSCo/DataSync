using System.Reflection;
using DataSync.Core.Configuration;

namespace DataSync.Infrastructure
{
    /// <summary>
    /// Names shown in the UI: from App.config, except the version, which is the assembly version.
    /// </summary>
    public sealed class AppInfo
    {
        public string ProgramName { get; private set; }
        public string ProgramDetails { get; private set; }
        public string Version { get; private set; }
        public string Copyright { get; private set; }
        public string RigName { get; private set; }

        public static AppInfo Load()
        {
            return new AppInfo
            {
                ProgramName    = AppSettings.GetString("ProgramName", "DataSync"),
                ProgramDetails = AppSettings.GetString("ProgramDetails", "Drilling Data Replication"),
                Version        = Assembly.GetEntryAssembly().GetName().Version.ToString(3),
                Copyright      = AppSettings.GetString("CompanyCopyright", "©2026 EPSCo. All rights reserved"),
                RigName        = AppSettings.GetString("RigName", "Unknown")
            };
        }
    }
}
