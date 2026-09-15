using System.Configuration;

namespace DataSync.Core.Configuration
{
    /// <summary>
    /// Typed reads of App.config &lt;appSettings&gt;; a missing or invalid value gives the default.
    /// </summary>
    public static class AppSettings
    {
        public static string GetString(string key, string defaultValue)
        {
            var value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        public static int GetInt(string key, int defaultValue)
        {
            return int.TryParse(ConfigurationManager.AppSettings[key], out var value) ? value : defaultValue;
        }

        public static long GetLong(string key, long defaultValue)
        {
            return long.TryParse(ConfigurationManager.AppSettings[key], out var value) ? value : defaultValue;
        }

        public static bool GetBool(string key, bool defaultValue)
        {
            return bool.TryParse(ConfigurationManager.AppSettings[key], out var value) ? value : defaultValue;
        }
    }
}
