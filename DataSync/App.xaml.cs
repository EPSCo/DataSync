using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DataSync.Core.Data;
using DataSync.Core.Logging;
using DataSync.Core.Models;
using DataSync.Core.Security;
using DataSync.Infrastructure;
using DataSync.Views;

namespace DataSync
{
    public partial class App : Application
    {
        private const string DesignModeArgument = "--design";

        public static AppInfo Info { get; private set; }

        public static User CurrentUser { get; private set; }

        /// <summary>
        /// UI design mode (Debug builds started with <c>--design</c>): skips the hardware lock, database checks, login
        /// and replication, and shows sample data.
        /// </summary>
        public static bool IsDesignMode { get; private set; }

        /// <summary>
        /// Starts a new instance with the same arguments and shuts this one down. Stop replication first.
        /// </summary>
        public static void Restart()
        {
            var arguments = Environment.GetCommandLineArgs().Skip(1).Select(a => "\"" + a.Replace("\"", "\\\"") + "\"");
            Log.Information("Restarting application");
            Process.Start(Assembly.GetEntryAssembly().Location, string.Join(" ", arguments));
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application shutting down");
            Log.Close();
            base.OnExit(e);
        }

        private async void OnStartup(object sender, StartupEventArgs e)
        {
            var binaryName = Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location);
            var logSettings = LogSettings.FromAppSettings();
            Log.Configure(binaryName, logSettings);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                Log.Error(args.ExceptionObject as Exception, "Unhandled exception");
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };

            Info = AppInfo.Load();
            LogEnvironment(e.Args, logSettings);
#if DEBUG
            IsDesignMode = e.Args.Any(a => string.Equals(a, DesignModeArgument, StringComparison.OrdinalIgnoreCase));
#endif

            var splash = new SplashWindow();
            splash.Show();
            var ready = await StartupChecks.RunAsync(splash.ReportProgress, IsDesignMode, splash.ShowExitButton, splash.ExitToken);
            splash.Close();
            if (!ready)
            {
                Shutdown();
                return;
            }

            CurrentUser = IsDesignMode ? new User { UserName = "designer" } : await Task.Run(() => TryAutoLogin());
            if (CurrentUser == null)
            {
                var login = new LoginWindow();
                if (login.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                CurrentUser = login.AuthenticatedUser;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }

        /// <summary>
        /// Writes what is needed to understand a log file from another computer: build, machine, network and settings.
        /// </summary>
        private static void LogEnvironment(string[] args, LogSettings logSettings)
        {
            var location = Assembly.GetEntryAssembly().Location;
            Log.Information("==================== " + Info.ProgramName + " " +
                            Assembly.GetEntryAssembly().GetName().Version + " starting ====================");
            try
            {
                Log.Information("Build " + File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm:ss") + ", executable " + location +
                                (args.Length > 0 ? ", arguments: " + string.Join(" ", args) : ""));
                Log.Information("Rig " + Info.RigName + ", computer " + Environment.MachineName + ", Windows user " +
                                Environment.UserDomainName + "\\" + Environment.UserName);
                Log.Information("OS " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " 64-bit" : " 32-bit") +
                                ", CLR " + Environment.Version + (Environment.Is64BitProcess ? " 64-bit" : " 32-bit") +
                                " process " + Process.GetCurrentProcess().Id + ", time zone " + TimeZoneInfo.Local.Id +
                                " (UTC" + TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).ToString(@"\+hh\:mm") + ")");

                var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal)
                    .Select(a => a.ToString());
                Log.Information("IP addresses: " + string.Join(", ", addresses));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot read environment details");
            }
            Log.Information("Logging to " + logSettings.Directory + " at level " + logSettings.MinimumLevel);
        }

        /// <summary>
        /// Logs in with the credentials saved in PW.txt, if they match a user.
        /// </summary>
        private static User TryAutoLogin()
        {
            var saved = CredentialStore.Load();
            if (string.IsNullOrEmpty(saved.UserName) || saved.Password == null)
            {
                return null;
            }

            try
            {
                var user = UserRepository.FindByCredentials(new UserRepository(ConnectionKind.Local).GetAll(), saved.UserName, saved.Password);
                if (user != null)
                {
                    Log.Information("Auto-login as " + user.UserName);
                }
                return user;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-login failed");
                return null;
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled UI exception");
            MessageBox.Show("An unexpected error occurred:\n\n" + e.Exception.Message, Info?.ProgramName ?? "DataSync",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
