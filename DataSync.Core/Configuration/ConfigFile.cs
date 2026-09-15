using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DataSync.Core.Configuration
{
    /// <summary>
    /// Writes &lt;appSettings&gt; values into the application's .config file. Edits the XML in place, so comments and
    /// formatting are kept (ConfigurationManager's own Save would rewrite the section and drop them).
    /// </summary>
    public static class ConfigFile
    {
        /// <summary>The running application's config file (e.g. DataSync.exe.config).</summary>
        public static string DefaultPath => AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;

        /// <summary>
        /// Saves to the running application's config file and reloads the section, so
        /// <see cref="AppSettings"/> returns the new values.
        /// </summary>
        public static void SaveAppSettings(IDictionary<string, string> values)
        {
            SaveAppSettings(DefaultPath, values);
            ConfigurationManager.RefreshSection("appSettings");
        }

        /// <summary>
        /// Sets each key's value, adding keys that are missing.
        /// </summary>
        public static void SaveAppSettings(string path, IDictionary<string, string> values)
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new InvalidOperationException("The config file has no root element: " + path);

            var appSettings = root.Element("appSettings");
            if (appSettings == null)
            {
                appSettings = new XElement("appSettings");
                root.Add(appSettings);
            }

            foreach (var pair in values)
            {
                var entry = appSettings.Elements("add").FirstOrDefault(e => (string)e.Attribute("key") == pair.Key);
                if (entry == null)
                {
                    appSettings.Add(new XElement("add", new XAttribute("key", pair.Key), new XAttribute("value", pair.Value)));
                }
                else
                {
                    entry.SetAttributeValue("value", pair.Value);
                }
            }

            // Write a temporary file first so a failed write cannot leave a truncated config behind.
            var tempPath = path + ".tmp";
            document.Save(tempPath, SaveOptions.DisableFormatting);
            File.Replace(tempPath, path, null);
        }
    }
}
