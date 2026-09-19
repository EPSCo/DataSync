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
                SyncUpdateInterval = " 2 ",
                AdaptiveBatchSize = true,
                MinRowLimit = "5",
                RealTimeBatchMultiplier = "5",
                SyncBatchMeasurements = "3",
                CommandTimeout = "60",
                RateWindow = " 10 ",
                SyncBatchMultiplier = " 1.5 ",
                SyncMultiplierTest = false,
                SyncMultiplierTestMinutes = " 20 ",
                GapCheckInterval = "10",
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
            settings.MinRowLimit = "0";
            StringAssert.StartsWith(settings.Validate(), "Minimum row limit");

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
            settings.RateWindow = "0";
            StringAssert.StartsWith(settings.Validate(), "Rate window");

            settings = ValidSettings();
            settings.SyncBatchMultiplier = "abc";
            StringAssert.StartsWith(settings.Validate(), "Sync batch multiplier");

            settings = ValidSettings();
            settings.SyncMultiplierTestMinutes = "0";
            StringAssert.StartsWith(settings.Validate(), "Multiplier test minutes");

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
            Assert.AreEqual("2", values[SettingKeys.SyncUpdateInterval]);
            Assert.IsFalse(values.ContainsKey(SettingKeys.SyncRowLimit));
            Assert.IsFalse(values.ContainsKey(SettingKeys.RealTimeRowLimit));
            Assert.IsFalse(values.ContainsKey(SettingKeys.MaxRowsPerSecond));
            Assert.AreEqual("true", values[SettingKeys.RollOnFileSizeLimit]);
            Assert.AreEqual("true", values[SettingKeys.AdaptiveBatchSize]);
            Assert.AreEqual("60", values[SettingKeys.CommandTimeout]);
            Assert.AreEqual("10", values[SettingKeys.RateWindow]);
            Assert.AreEqual("1.5", values[SettingKeys.SyncBatchMultiplier]);
            Assert.AreEqual("false", values[SettingKeys.SyncMultiplierTest]);
            Assert.AreEqual("20", values[SettingKeys.SyncMultiplierTestMinutes]);
            Assert.AreEqual("Debug", values[SettingKeys.LogLevel]);
        }

        [TestMethod]
        public void FontSettings_ValidatesRangeAndNormalizes()
        {
            var settings = FontSettings.Defaults();
            Assert.IsNull(settings.Validate());

            settings.Items[0].Size = " 20 ";
            Assert.AreEqual("20", settings.ToValues()[SettingKeys.FontSizeBody]);
            Assert.AreEqual("12", settings.ToValues()[SettingKeys.FontSizeButton]);

            settings.Items[1].Size = "7";
            StringAssert.StartsWith(settings.Validate(), "Section titles size");

            settings.Items[1].Size = "abc";
            Assert.IsNotNull(settings.Validate());

            settings.Items[1].Size = "37";
            Assert.IsNotNull(settings.Validate());
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
