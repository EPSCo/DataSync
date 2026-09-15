using System;
using System.IO;
using DataSync.Core.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class LogTests
    {
        [TestMethod]
        public void Write_SkipsMessagesBelowMinimumLevel()
        {
            var directory = Path.Combine(Path.GetTempPath(), "DataSyncLogTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Log.Configure("Test", new LogSettings
                {
                    Directory = directory,
                    RollingInterval = RollingInterval.Infinite,
                    MinimumLevel = LogLevel.Information
                });

                Log.Debug("debug line");
                Log.Information("information line");
                Log.Error(new InvalidOperationException("boom"), "error line");
                Log.Close();

                var text = File.ReadAllText(Path.Combine(directory, "Test-log.log"));
                Assert.IsFalse(text.Contains("debug line"), "a Debug message was written below the minimum level");
                StringAssert.Contains(text, "[INF] Thread");
                StringAssert.Contains(text, "information line");
                StringAssert.Contains(text, "[ERR] Thread");
                StringAssert.Contains(text, "System.InvalidOperationException: boom");
                Assert.AreEqual(directory, Log.LogDirectory);
            }
            finally
            {
                Log.Configure("Test", null);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
