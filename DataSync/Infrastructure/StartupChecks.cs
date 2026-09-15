using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DataSync.Core.Data;
using DataSync.Core.Logging;

namespace DataSync.Infrastructure
{
    /// <summary>
    /// Checks run behind the splash screen. A failure is shown in a message box and the app exits, except an unreachable
    /// remote database, which is retried until it answers or the user clicks Exit.
    /// </summary>
    internal static class StartupChecks
    {
        private static readonly TimeSpan RemoteRetryDelay = TimeSpan.FromSeconds(5);

        /// <param name="report">Progress (0-100) and status text; called on the UI thread.</param>
        /// <param name="showExit">Shows or hides the Exit button while waiting for the remote database; called on the UI thread.</param>
        /// <param name="exitToken">Cancelled when the user clicks Exit.</param>
        public static async Task<bool> RunAsync(Action<int, string> report, bool designMode, Action<bool> showExit, CancellationToken exitToken)
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
                return Fail("Hardware lock verification failed.", "Hardware lock");
            }

            report(60, "Connecting to local database...");
            if (!await Task.Run(() => config.CheckConnection(ConnectionKind.Local)))
            {
                return Fail("Failed to connect to the local database.", "Local database connection error");
            }

            report(85, "Connecting to remote database...");
            if (!await WaitForRemoteAsync(config, report, showExit, exitToken))
            {
                Log.Information("Startup cancelled while waiting for the remote database");
                return false;
            }

            report(100, "Setup finished.");
            await Task.Delay(200);
            return true;
        }

        /// <summary>
        /// Retries the remote connection until it succeeds, so a rig started during a network outage recovers on its
        /// own. False when the user clicks Exit.
        /// </summary>
        private static async Task<bool> WaitForRemoteAsync(DatabaseConfig config, Action<int, string> report,
                                                           Action<bool> showExit, CancellationToken exitToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                if (await Task.Run(() => config.CheckConnection(ConnectionKind.Remote)))
                {
                    showExit(false);
                    return true;
                }

                if (attempt == 1)
                {
                    Log.Warning("Remote database unreachable at startup, retrying every " + RemoteRetryDelay.TotalSeconds + " s");
                    showExit(true);
                }

                if (exitToken.IsCancellationRequested)
                {
                    return false;
                }

                report(85, "Remote database unreachable (attempt " + attempt + "), retrying in " +
                           RemoteRetryDelay.TotalSeconds + " s...");
                try
                {
                    await Task.Delay(RemoteRetryDelay, exitToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                report(85, "Connecting to remote database (attempt " + (attempt + 1) + ")...");
            }
        }

        private static bool Fail(string message, string title)
        {
            Log.Error("Startup check failed: " + message);
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
