using System.Collections.Generic;
using System.IO;
using DataSync.Core.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class ConfigurationTests
    {
        private static EditableSettings ValidSettings()
        {
            return new EditableSettings
            {
                RigName = " MD-2 ",
                RealTimeUpdateInterval = "1",
                RealTimeRowLimit = "1000",
                SyncUpdateInterval = "0",
                SyncRowLimit = " 500 ",
                AdaptiveBatchSize = true,
                MinRowLimit = "5",
                RealTimeBatchMultiplier = "5",
                SyncBatchMeasurements = "3",
                CommandTimeout = "60",
                GapCheckInterval = "10",
                MaxRowsPerSecond = "0",
                TimeoutCounterLimit = "10",
                LogPathDir = "logs",
                RollingInterval = "Day",
                RollOnFileSizeLimit = true,
                FileSizeLimitBytes = "1048576",
                RetainedFileCountLimit = "10",
                LogLevel = "Debug"
            };
        }

        [TestMethod]
        public void Validate_AcceptsValidSettings()
        {
            Assert.IsNull(ValidSettings().Validate());
        }

        [TestMethod]
        public void Validate_RejectsValuesTheAppWouldClamp()
        {
            var settings = ValidSettings();
            settings.SyncRowLimit = "0";
            StringAssert.StartsWith(settings.Validate(), "Sync row limit");

            settings = ValidSettings();
            settings.GapCheckInterval = "abc";
            StringAssert.StartsWith(settings.Validate(), "Gap check interval");

            settings = ValidSettings();
            settings.RollingInterval = "3";
            StringAssert.StartsWith(settings.Validate(), "Select a log rolling interval");

            settings = ValidSettings();
            settings.CommandTimeout = "3";
            StringAssert.StartsWith(settings.Validate(), "Command timeout");

            settings = ValidSettings();
            settings.RealTimeBatchMultiplier = "0";
            StringAssert.StartsWith(settings.Validate(), "Real-time batch multiplier");

            settings = ValidSettings();
            settings.LogLevel = "Verbose";
            StringAssert.StartsWith(settings.Validate(), "Select a log level");

            settings = ValidSettings();
            settings.RigName = " ";
            Assert.IsNotNull(settings.Validate());
        }

        [TestMethod]
        public void ToValues_TrimsAndNormalizes()
        {
            var values = ValidSettings().ToValues();

            Assert.AreEqual("MD-2", values[SettingKeys.RigName]);
            Assert.AreEqual("500", values[SettingKeys.SyncRowLimit]);
            Assert.AreEqual("true", values[SettingKeys.RollOnFileSizeLimit]);
            Assert.AreEqual("true", values[SettingKeys.AdaptiveBatchSize]);
            Assert.AreEqual("60", values[SettingKeys.CommandTimeout]);
            Assert.AreEqual("Debug", values[SettingKeys.LogLevel]);
        }

        [TestMethod]
        public void SaveAppSettings_UpdatesAndAddsKeysAndKeepsComments()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<configuration>\r\n" +
                    "  <appSettings>\r\n" +
                    "    <!-- Rows per read -->\r\n" +
                    "    <add key=\"SyncRowLimit\" value=\"1000\" />\r\n" +
                    "  </appSettings>\r\n" +
                    "</configuration>\r\n");

                ConfigFile.SaveAppSettings(path, new Dictionary<string, string>
                {
                    [SettingKeys.SyncRowLimit] = "250",
                    [SettingKeys.RigName] = "MD-9"
                });

                var text = File.ReadAllText(path);
                StringAssert.Contains(text, "<!-- Rows per read -->");
                StringAssert.Contains(text, "<add key=\"SyncRowLimit\" value=\"250\" />");
                StringAssert.Contains(text, "<add key=\"RigName\" value=\"MD-9\" />");
                Assert.IsFalse(text.Contains("value=\"1000\""));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
