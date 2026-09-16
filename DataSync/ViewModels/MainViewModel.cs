using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using DataSync.Core.Data;
using DataSync.Core.Logging;
using DataSync.Core.Models;
using DataSync.Core.Replication;
using DataSync.Infrastructure;

namespace DataSync.ViewModels
{
    /// <summary>
    /// Main window state. A one-second timer polls the replication engine (which runs on background threads) and is
    /// the only place the displayed values change.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private const int MaxMessages = 100;
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
        private static readonly TimeSpan DatabaseCheckInterval = TimeSpan.FromSeconds(30);

        private readonly IShell _shell;
        private readonly bool _designMode;
        private readonly ConcurrentQueue<string> _pendingMessages = new ConcurrentQueue<string>();
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _rateClock = new Stopwatch();

        private ReplicationEngine _engine;
        private Func<ReplicationStatus> _getStatus;
        private bool _stopped;
        private bool _restarting;
        private bool _checkingDatabases;
        private bool _unreachableReported;
        private DateTime _nextDatabaseCheck;
        private bool _isBusy;

        private string _currentTime;
        private string _realTimeState;
        private string _lastBaseId;
        private string _lastRecordTime;
        private string _syncState;
        private string _syncNextBaseId = "-";
        private string _syncButtonText = "Pause sync task";
        private string _realTimeBatch = "-";
        private string _syncBatch = "-";
        private long _realTimeRowsCopied;
        private long _syncRowsCopied;
        private double _realTimeRate;
        private double _syncRate;
        private int _dotFrame;

        public MainViewModel(AppInfo info, IShell shell, bool designMode)
        {
            _shell = shell;
            _designMode = designMode;

            Title = info.ProgramName + " " + info.Version + (designMode ? " [UI DESIGN MODE]" : "");
            RigName = info.RigName;
            CurrentTime = DateTime.Now.ToString(TimeFormat);
            RealTimeState = TaskState.Stop.ToString();
            SyncState = TaskState.Stop.ToString();
            LastBaseId = "-1";

            ToggleSyncCommand = new RelayCommand(ToggleSync, () => _engine != null && !IsBusy);
            TestRemoteConnectionCommand = new RelayCommand(() => TestConnection(ConnectionKind.Remote), () => !_designMode && !IsBusy);
            TestLocalConnectionCommand = new RelayCommand(() => TestConnection(ConnectionKind.Local), () => !_designMode && !IsBusy);
            ClearLocalDataCommand = new RelayCommand(ClearLocalData, () => !_designMode && !IsBusy);
            RestartCommand = new RelayCommand(Restart, () => !IsBusy);
            ClearMessagesCommand = new RelayCommand(() => Messages.Clear());
            OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
            SettingsCommand = new RelayCommand(EditSettings, () => !IsBusy);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Refresh();
        }

        public string Title { get; }

        public string RigName { get; }

        public ObservableCollection<SyncRangeRow> Ranges { get; } = new ObservableCollection<SyncRangeRow>();

        /// <summary>Newest first.</summary>
        public ObservableCollection<string> Messages { get; } = new ObservableCollection<string>();

        public ICommand ToggleSyncCommand { get; }
        public ICommand TestRemoteConnectionCommand { get; }
        public ICommand TestLocalConnectionCommand { get; }
        public ICommand ClearLocalDataCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand ClearMessagesCommand { get; }
        public ICommand OpenLogFolderCommand { get; }
        public ICommand SettingsCommand { get; }

        public string CurrentTime
        {
            get { return _currentTime; }
            private set { SetProperty(ref _currentTime, value); }
        }

        public string RealTimeState
        {
            get { return _realTimeState; }
            private set { SetProperty(ref _realTimeState, value); }
        }

        public string LastBaseId
        {
            get { return _lastBaseId; }
            private set { SetProperty(ref _lastBaseId, value); }
        }

        public string LastRecordTime
        {
            get { return _lastRecordTime; }
            private set { SetProperty(ref _lastRecordTime, value); }
        }

        public string SyncState
        {
            get { return _syncState; }
            private set { SetProperty(ref _syncState, value); }
        }

        /// <summary>Highest BaseID of the next sync window, or "-" when there is nothing to sync.</summary>
        public string SyncNextBaseId
        {
            get { return _syncNextBaseId; }
            private set { SetProperty(ref _syncNextBaseId, value); }
        }

        /// <summary>Next read size and recent copy rate of the real-time task.</summary>
        public string RealTimeBatch
        {
            get { return _realTimeBatch; }
            private set { SetProperty(ref _realTimeBatch, value); }
        }

        /// <summary>Next read size and recent copy rate of the sync task.</summary>
        public string SyncBatch
        {
            get { return _syncBatch; }
            private set { SetProperty(ref _syncBatch, value); }
        }

        public string SyncButtonText
        {
            get { return _syncButtonText; }
            private set { SetProperty(ref _syncButtonText, value); }
        }

        /// <summary>True while a connection test, sync toggle or clear is in progress.</summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Starts the UI timer and replication (or sample data in design mode).
        /// </summary>
        public async void Start()
        {
            _timer.Start();

            if (_designMode)
            {
                LoadDesignData();
                Refresh();
                return;
            }

            var settings = ReplicationSettings.FromAppSettings();
            var commandTimeout = (int)settings.CommandTimeout.TotalSeconds;
            var engine = new ReplicationEngine(new SqlProcessDataStore(ConnectionKind.Remote, commandTimeout),
                                               new SqlProcessDataStore(ConnectionKind.Local, commandTimeout),
                                               settings);
            engine.MessageLogged += (s, message) => _pendingMessages.Enqueue(message);
            _engine = engine;

            AddMessage("Starting replication...");
            try
            {
                // Reads the starting position from both databases, so keep it off the UI thread.
                await Task.Run(() => engine.Start());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot start replication");
                _shell.ShowError("Cannot start replication: " + ex.Message, "Replication error");
                return;
            }

            if (_stopped)
            {
                // The window was closed while starting.
                engine.Stop();
                return;
            }

            _getStatus = engine.GetStatus;
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Stops the timer and both replication tasks, waiting for them to finish (up to 30 seconds).
        /// </summary>
        public void Shutdown()
        {
            _stopped = true;
            _timer.Stop();

            var engine = _engine;
            _engine = null;
            engine?.Stop();
        }

        private void Refresh()
        {
            CurrentTime = DateTime.Now.ToString(TimeFormat);

            while (_pendingMessages.TryDequeue(out var message))
            {
                AddMessage(message);
            }

            var status = _getStatus?.Invoke();
            if (status == null)
            {
                return;
            }

            if (status.FailureLimitExceeded)
            {
                RestartIfDatabasesReachable(status);
            }
            else
            {
                _unreachableReported = false;
            }

            RealTimeState = status.RealTimeBehind && status.RealTimeState != TaskState.Stop
                ? "Catching up"
                : status.RealTimeState.ToString();
            LastBaseId = FormatRecord(status.LastLocalBaseId);
            LastRecordTime = status.LastLocalRecordTime?.ToString(TimeFormat) ?? "";
            SyncState = status.SyncYielding && status.SyncState != TaskState.Stop
                ? "Waiting for real-time"
                : status.SyncState.ToString();
            SyncNextBaseId = status.SyncNextBaseId > 0 ? FormatRecord(status.SyncNextBaseId) : "-";
            UpdateBatchInfo(status);

            // Downloading or NoOldData (waiting to check for new gaps) both mean the sync task is running.
            SyncButtonText = status.SyncState == TaskState.Stop ? "Resume sync task" : "Pause sync task";

            UpdateRanges(status);
        }

        /// <summary>
        /// Restarts after too many consecutive failures, but only when both databases answer. While one is unreachable
        /// (a network outage) a restart cannot help and would stop at the startup checks; the replication tasks keep
        /// retrying and resume on their own when the link returns.
        /// </summary>
        private async void RestartIfDatabasesReachable(ReplicationStatus status)
        {
            if (_checkingDatabases || DateTime.UtcNow < _nextDatabaseCheck)
            {
                return;
            }

            _checkingDatabases = true;
            try
            {
                var reachable = await Task.Run(() => DatabaseConfig.Instance.CheckConnection(ConnectionKind.Remote) &&
                                                     DatabaseConfig.Instance.CheckConnection(ConnectionKind.Local));
                if (_stopped)
                {
                    return;
                }

                if (reachable)
                {
                    Log.Warning("Too many consecutive failures (RealTime: " + status.RealTimeFailureCount +
                                ", Sync: " + status.SyncFailureCount + "), restarting");
                    Restart();
                    return;
                }

                _nextDatabaseCheck = DateTime.UtcNow + DatabaseCheckInterval;
                if (!_unreachableReported)
                {
                    _unreachableReported = true;
                    const string message = "Database unreachable after repeated failures: not restarting, replication keeps retrying";
                    Log.Warning(message);
                    AddMessage(message);
                }
            }
            finally
            {
                _checkingDatabases = false;
            }
        }

        private void UpdateBatchInfo(ReplicationStatus status)
        {
            // Rates from the change in running totals between timer ticks, smoothed over a few seconds.
            var seconds = _rateClock.Elapsed.TotalSeconds;
            if (_rateClock.IsRunning && seconds > 0)
            {
                _realTimeRate = Smooth(_realTimeRate, (status.RealTimeRowsCopied - _realTimeRowsCopied) / seconds);
                _syncRate = Smooth(_syncRate, (status.SyncRowsCopied - _syncRowsCopied) / seconds);
            }
            _realTimeRowsCopied = status.RealTimeRowsCopied;
            _syncRowsCopied = status.SyncRowsCopied;
            _rateClock.Restart();

            RealTimeBatch = FormatBatch(status.RealTimeBatchSize, status.AdaptiveBatchSize, _realTimeRate);
            SyncBatch = FormatBatch(status.SyncBatchSize, status.AdaptiveBatchSize, _syncRate);
        }

        private static double Smooth(double previous, double sample)
        {
            return 0.3 * sample + 0.7 * previous;
        }

        private static string FormatBatch(int batchSize, bool adaptive, double rowsPerSecond)
        {
            return batchSize + " R" + (adaptive ? "" : " (fixed)") + ", " + rowsPerSecond.ToString("0") + " R/s";
        }

        private void UpdateRanges(ReplicationStatus status)
        {
            var realTimeEnd = Math.Max(status.RealTimeNextBaseId, status.LastLocalBaseId);
            var ranges = status.Ranges.OrderByDescending(r => r.Range.BaseIdBegin).ToList();
            _dotFrame = (_dotFrame + 1) % 3;

            while (Ranges.Count > ranges.Count)
            {
                Ranges.RemoveAt(Ranges.Count - 1);
            }

            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                var isRealTime = range.Status == RangeStatus.RealTime;

                if (i == Ranges.Count)
                {
                    Ranges.Add(new SyncRangeRow());
                }

                // Queued and syncing ranges are copied backwards from StartSyncPoint; their BaseIdEnd is the progress.
                var hasStart = !isRealTime && range.StartSyncPoint > 0;
                var inProgress = range.Status == RangeStatus.Syncing ||
                                 (hasStart && range.Status == RangeStatus.NotSync && range.Range.BaseIdEnd != range.StartSyncPoint);

                var row = Ranges[i];
                row.BaseIdBegin = range.Range.BaseIdBegin;
                row.BaseIdEnd = isRealTime ? "—" : FormatRecord(hasStart ? range.StartSyncPoint : range.Range.BaseIdEnd);
                row.CurrentRecord = isRealTime ? FormatRecord(realTimeEnd) : inProgress ? FormatRecord(range.Range.BaseIdEnd) : "—";
                row.Percent = isRealTime ? string.Join(" ", Enumerable.Repeat(".", _dotFrame + 1)) : DescribePercent(range, hasStart);
                row.Status = DescribeStatus(range.Status);
            }
        }

        /// <summary>
        /// Share of a range copied so far. Sync reads backwards from the end record (StartSyncPoint), so the records
        /// between it and the current record (BaseIdEnd) are done.
        /// </summary>
        private static string DescribePercent(SyncRange range, bool hasStart)
        {
            if (range.Status == RangeStatus.Synced)
            {
                return "Completed";
            }
            // Only the range being copied shows progress; queued ranges show "—".
            if (range.Status != RangeStatus.Syncing || !hasStart)
            {
                return "—";
            }

            var total = range.StartSyncPoint - range.Range.BaseIdBegin + 1;
            var copied = range.StartSyncPoint - range.Range.BaseIdEnd;
            var percent = total > 0 ? Math.Max(0, Math.Min(100, copied * 100.0 / total)) : 0;
            // Rounded down so a range still being copied never shows 100.00.
            return (Math.Floor(percent * 100) / 100).ToString("0.00", CultureInfo.InvariantCulture) + " %";
        }

        /// <summary>Record numbers are grouped in thousands (32,467,044); the Sync table columns do the same.</summary>
        private static string FormatRecord(long baseId)
        {
            return baseId.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>Status text for the Sync Table; the row colours in MainWindow.xaml match on these words.</summary>
        private static string DescribeStatus(RangeStatus status)
        {
            switch (status)
            {
                case RangeStatus.RealTime:
                    return "Real-time";
                case RangeStatus.NotSync:
                    return "Queued";
                default:
                    return status.ToString();
            }
        }

        private void AddMessage(string message)
        {
            Messages.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            while (Messages.Count > MaxMessages)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        }

        private async void ToggleSync()
        {
            var engine = _engine;
            if (engine == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                // Resuming waits for the paused task to finish, so keep it off the UI thread.
                if (engine.GetStatus().SyncState != TaskState.Stop)
                {
                    await Task.Run(() => engine.PauseSync());
                }
                else
                {
                    await Task.Run(() => engine.ResumeSync());
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void TestConnection(ConnectionKind kind)
        {
            var name = kind == ConnectionKind.Remote ? "remote" : "local";

            IsBusy = true;
            try
            {
                if (await Task.Run(() => DatabaseConfig.Instance.CheckConnection(kind)))
                {
                    _shell.ShowInfo("Connection to the " + name + " database is successful.", kind + " database connection");
                }
                else
                {
                    _shell.ShowError("Failed to connect to the " + name + " database.", kind + " database connection error");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenLogFolder()
        {
            var directory = Log.LogDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                _shell.ShowError("Logging is not configured.", "Logs");
                return;
            }

            try
            {
                _shell.OpenFolder(directory);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Cannot open log folder " + directory);
                _shell.ShowError("Cannot open the log folder " + directory + ": " + ex.Message, "Logs");
            }
        }

        private async void ClearLocalData()
        {
            if (!_shell.ConfirmClearData())
            {
                return;
            }

            IsBusy = true;
            AddMessage("Stopping replication and clearing local data...");
            var engine = _engine;
            try
            {
                await Task.Run(() =>
                {
                    engine?.Stop();
                    new SqlProcessDataStore(ConnectionKind.Local).TruncateLocalData();
                });
                Log.Information("Local database cleared");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to clear local data");
                _shell.ShowError("Failed to clear data. Error: " + ex.Message, "Error");
            }

            Restart();
        }

        private void EditSettings()
        {
            if (!_shell.EditSettings())
            {
                return;
            }

            AddMessage("Settings saved.");
            // Settings are read at startup (replication, logging, rig name), so they apply after a restart.
            if (_shell.Confirm("Settings saved. They take effect after the application restarts.\n\nRestart now?", "Settings",
                               "Restart now", "Later"))
            {
                Restart();
            }
        }

        private void Restart()
        {
            if (_restarting)
            {
                return;
            }

            _restarting = true;
            Shutdown();
            _shell.RestartApplication();
        }

        /// <summary>
        /// UI design mode only: sample values instead of replication. No background tasks are started.
        /// </summary>
        private void LoadDesignData()
        {
            var sampleStatus = new ReplicationStatus
            {
                RealTimeState       = TaskState.Downloading,
                SyncState           = TaskState.Downloading,
                LastLocalBaseId     = 125000,
                RealTimeNextBaseId  = 125001,
                LastLocalRecordTime = DateTime.Now,
                SyncNextBaseId      = 90000,
                RealTimeBatchSize   = 20,
                SyncBatchSize       = 340,
                AdaptiveBatchSize   = true,
                Ranges = new List<SyncRange>
                {
                    Sample(1, 50000, -1, RangeStatus.Synced),
                    Sample(50001, 80000, 80000, RangeStatus.NotSync),
                    Sample(80001, 90000, 110000, RangeStatus.Syncing),
                    Sample(110001, 124000, -1, RangeStatus.Synced),
                    Sample(124001, 124001, -1, RangeStatus.RealTime)
                }
            };
            _getStatus = () => sampleStatus;

            AddMessage("UI design mode: hardware lock, database checks and replication tasks are disabled.");
        }

        private static SyncRange Sample(long begin, long end, long startSyncPoint, RangeStatus status)
        {
            return new SyncRange
            {
                Range = new BaseIdRange { BaseIdBegin = begin, BaseIdEnd = end },
                StartSyncPoint = startSyncPoint,
                Status = status
            };
        }
    }
}
