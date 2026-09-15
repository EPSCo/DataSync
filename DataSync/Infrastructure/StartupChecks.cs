using System;
using System.Threading.Tasks;
using System.Windows;
using DataSync.Core.Data;
using DataSync.Core.Logging;

namespace DataSync.Infrastructure
{
    /// <summary>
    /// Checks run behind the splash screen. A failure is shown in a message box and the app exits.
    /// </summary>
    internal static class StartupChecks
    {
        /// <param name="report">Progress (0-100) and status text; called on the UI thread.</param>
        public static async Task<bool> RunAsync(Action<int, string> report, bool designMode)
        {
            report(10, "Loading configuration...");
            if (designMode)
            {
                report(100, "UI design mode: startup checks skipped.");
                await Task.Delay(400);
                return true;
            }

            var config = await Task.Run(() => DatabaseConfig.Instance);

            report(35, "Verifying hardware lock...");
            if (!await Task.Run(() => config.VerifyHardwareLock()))
            {
                return Fail("Hardware lock verification failed.", "Hardware Lock");
            }

            report(60, "Connecting to local database...");
            if (!await Task.Run(() => config.CheckConnection(ConnectionKind.Local)))
            {
                return Fail("Failed to connect to the local database.", "Local Database Connection Error");
            }

            report(85, "Connecting to remote database...");
            if (!await Task.Run(() => config.CheckConnection(ConnectionKind.Remote)))
            {
                return Fail("Failed to connect to the remote database.", "Remote Database Connection Error");
            }

            report(100, "Setup finished.");
            await Task.Delay(200);
            return true;
        }

        private static bool Fail(string message, string title)
        {
            Log.Error("Startup check failed: " + message);
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
