using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private const string TimeFormat = "yyyy/MM/dd HH:mm:ss";

        private readonly IShell _shell;
        private readonly bool _designMode;
        private readonly ConcurrentQueue<string> _pendingMessages = new ConcurrentQueue<string>();
        private readonly DispatcherTimer _timer;

        private ReplicationEngine _engine;
        private Func<ReplicationStatus> _getStatus;
        private bool _stopped;
        private bool _restarting;
        private bool _isBusy;

        private string _currentTime;
        private string _realTimeState;
        private string _lastBaseId;
        private string _lastRecordTime;
        private string _syncState;
        private string _syncNextBaseId = "-";
        private string _syncButtonText = "Pause Sync Task";

        public MainViewModel(AppInfo info, string userName, IShell shell, bool designMode)
        {
            _shell = shell;
            _designMode = designMode;

            Title = info.ProgramName + " " + info.Version + " [ " + userName + " ]" + (designMode ? " [UI DESIGN MODE]" : "");
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

            var engine = new ReplicationEngine(new SqlProcessDataStore(ConnectionKind.Remote),
                                               new SqlProcessDataStore(ConnectionKind.Local),
                                               ReplicationSettings.FromAppSettings());
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
                _shell.ShowError("Cannot start replication: " + ex.Message, "Replication Error");
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
                Log.Warning("Too many consecutive failures (RealTime: " + status.RealTimeFailureCount +
                            ", Sync: " + status.SyncFailureCount + "), restarting");
                Restart();
                return;
            }

            RealTimeState = status.RealTimeState.ToString();
            LastBaseId = status.LastLocalBaseId.ToString();
            LastRecordTime = status.LastLocalRecordTime?.ToString(TimeFormat) ?? "";
            SyncState = status.SyncState.ToString();
            SyncNextBaseId = status.SyncNextBaseId > 0 ? status.SyncNextBaseId.ToString() : "-";

            // Downloading or NoOldData (waiting to check for new gaps) both mean the sync task is running.
            SyncButtonText = status.SyncState == TaskState.Stop ? "Resume Sync Task" : "Pause Sync Task";

            UpdateRanges(status);
        }

        private void UpdateRanges(ReplicationStatus status)
        {
            var realTimeEnd = Math.Max(status.RealTimeNextBaseId, status.LastLocalBaseId);
            var ranges = status.Ranges.OrderByDescending(r => r.Range.BaseIdBegin).ToList();

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

                var row = Ranges[i];
                row.BaseIdBegin = range.Range.BaseIdBegin;
                row.BaseIdEnd = isRealTime ? realTimeEnd : range.Range.BaseIdEnd;
                row.StartSyncPoint = !isRealTime && range.StartSyncPoint > 0 ? range.StartSyncPoint.ToString() : "-";
                row.Status = range.Status.ToString();
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
                    _shell.ShowInfo("Connection to the " + name + " database is successful.", kind + " Database Connection");
                }
                else
                {
                    _shell.ShowError("Failed to connect to the " + name + " database.", kind + " Database Connection Error");
                }
            }
            finally
            {
                IsBusy = false;
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
            if (_shell.Confirm("Settings saved. They take effect after the application restarts.\n\nRestart now?", "Settings"))
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
