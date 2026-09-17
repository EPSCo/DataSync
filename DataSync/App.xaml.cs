using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DataSync.Core.Configuration;
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

        /// <summary>Passed to the instance started by <see cref="Restart"/>, so it waits for this one to let go.</summary>
        private const string RestartArgument = "--restart";

        /// <summary>How long a restarted instance waits for the one it replaces to release the lock.</summary>
        private static readonly TimeSpan RestartLockTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Held for the lifetime of the process; see <see cref="TryAcquireInstanceLock"/>.</summary>
        private static Mutex _instanceLock;
        private static bool _hasInstanceLock;

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
            var arguments = Environment.GetCommandLineArgs()
                .Skip(1)
                .Where(a => !string.Equals(a, RestartArgument, StringComparison.OrdinalIgnoreCase))
                .Select(a => "\"" + a.Replace("\"", "\\\"") + "\"")
                .Concat(new[] { RestartArgument });
            Log.Information("Restarting application");

            // The replacement cannot start while this process holds the instance lock. Releasing it here lets it start
            // at once; the RestartArgument makes it wait anyway, in case this process is gone before it looks.
            ReleaseInstanceLock();
            Process.Start(Assembly.GetEntryAssembly().Location, string.Join(" ", arguments));
            Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application shutting down");
            ReleaseInstanceLock();
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
#if DEBUG
            IsDesignMode = e.Args.Any(a => string.Equals(a, DesignModeArgument, StringComparison.OrdinalIgnoreCase));
#endif

            // One instance per copy of the program: two of them would replicate into the same local database, each
            // reading and writing the rows the other is already copying. Design mode touches no database, so it is free
            // to run next to a working instance.
            if (!IsDesignMode && !TryAcquireInstanceLock(e.Args))
            {
                ShowAlreadyRunning();
                Shutdown();
                return;
            }

            LogEnvironment(e.Args, logSettings);
            FontSizes.Apply(FontSettings.Load());

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
        /// Takes the lock that marks this copy of the program as running, or returns false when another instance holds
        /// it. The name is tied to the executable's path, so a second installation (a test copy in another folder) is
        /// not blocked by the one in service. An instance started by <see cref="Restart"/> waits for the one it
        /// replaces to shut down.
        /// </summary>
        private static bool TryAcquireInstanceLock(string[] args)
        {
            var name = "Local\\DataSync-" + HashPath(Assembly.GetEntryAssembly().Location);
            var timeout = args.Any(a => string.Equals(a, RestartArgument, StringComparison.OrdinalIgnoreCase))
                ? RestartLockTimeout
                : TimeSpan.Zero;

            try
            {
                _instanceLock = new Mutex(false, name);
                _hasInstanceLock = _instanceLock.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // The instance holding it ended without releasing it (killed, or a crash): the lock is ours now.
                Log.Warning("The previous instance did not shut down cleanly");
                _hasInstanceLock = true;
            }
            catch (Exception ex)
            {
                // Never keep the program from starting because the lock itself failed.
                Log.Error(ex, "Cannot check whether another instance is running");
                return true;
            }

            return _hasInstanceLock;
        }

        private static void ReleaseInstanceLock()
        {
            if (!_hasInstanceLock)
            {
                return;
            }

            try
            {
                _hasInstanceLock = false;
                _instanceLock.ReleaseMutex();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot release the instance lock");
            }
        }

        /// <summary>Hexadecimal MD5 of the path, lower-cased: the same folder always gives the same lock name.</summary>
        private static string HashPath(string path)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }

        /// <summary>
        /// Brings the running instance's window to the front, so starting the program again looks like returning to it.
        /// Says so in a dialog when that window cannot be found (it may still be on the splash screen).
        /// </summary>
        private static void ShowAlreadyRunning()
        {
            Log.Warning(Info.ProgramName + " is already running; this instance is closing");

            if (FocusRunningInstance())
            {
                return;
            }

            new MessageWindow(Info.ProgramName, Info.ProgramName + " is already running on this computer." +
                                                Environment.NewLine + Environment.NewLine +
                                                "Only one instance can replicate at a time.", "OK", null, false)
                .ShowDialog();
        }

        private static bool FocusRunningInstance()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var other = Process.GetProcessesByName(current.ProcessName)
                    .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);
                if (other == null)
                {
                    return false;
                }

                NativeMethods.ShowWindow(other.MainWindowHandle, NativeMethods.SW_RESTORE);
                return NativeMethods.SetForegroundWindow(other.MainWindowHandle);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot bring the running instance to the front");
                return false;
            }
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
