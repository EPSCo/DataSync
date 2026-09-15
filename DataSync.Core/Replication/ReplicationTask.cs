using System;
using System.Threading;
using System.Threading.Tasks;
using DataSync.Core.Logging;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// A background loop that repeats <see cref="RunIteration"/> until stopped, with the failure counting, retry
    /// back-off and throttling shared by the replication tasks.
    /// </summary>
    public abstract class ReplicationTask
    {
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly string _name;
        private readonly Action<string> _report;
        private CancellationTokenSource _cts;
        private Task _task;
        private int _state = (int)TaskState.Stop;
        private int _failureCount;

        protected ReplicationTask(string name, ReplicationSettings settings, Action<string> report)
        {
            _name = name;
            Settings = settings;
            _report = report;
        }

        protected ReplicationSettings Settings { get; }

        public TaskState State
        {
            get { return (TaskState)Volatile.Read(ref _state); }
            protected set { Volatile.Write(ref _state, (int)value); }
        }

        /// <summary>
        /// Consecutive failed iterations; reset by the next successful one.
        /// </summary>
        public int FailureCount => Volatile.Read(ref _failureCount);

        public bool IsRunning => _task != null && !_task.IsCompleted;

        /// <summary>
        /// Completes when the loop has exited.
        /// </summary>
        public Task Completion => _task ?? Task.CompletedTask;

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            State = TaskState.Downloading;
            _task = Task.Run(() => Run(token));
        }

        public void RequestStop()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Does one unit of work and returns how long to wait before the next one.
        /// </summary>
        public abstract TimeSpan RunIteration();

        /// <summary>
        /// Resets the failure count and returns the delay before the next read: none while there is a
        /// <paramref name="backlog"/>, otherwise <paramref name="interval"/>; never faster than MaxRowsPerSecond.
        /// </summary>
        protected TimeSpan OnSuccess(int rowCount, bool backlog, TimeSpan interval, TimeSpan elapsed)
        {
            Interlocked.Exchange(ref _failureCount, 0);

            var delay = backlog ? TimeSpan.Zero : interval;
            if (Settings.MaxRowsPerSecond > 0)
            {
                var throttle = TimeSpan.FromMilliseconds(rowCount * 1000.0 / Settings.MaxRowsPerSecond);
                if (throttle > delay)
                {
                    delay = throttle;
                }
            }

            return delay - elapsed;
        }

        /// <summary>
        /// Logs the error, counts the failure and returns the retry delay: 1 s, 2 s, 4 s, ... up to 60 s.
        /// </summary>
        protected TimeSpan OnFailure(string operation, Exception ex)
        {
            var failureCount = Interlocked.Increment(ref _failureCount);
            Log.Error(ex, "Error in " + operation);
            Report("Error in " + operation + ": " + ex.Message);

            var delay = TimeSpan.FromSeconds(1 << Math.Min(failureCount - 1, 6));
            return delay < MaxRetryDelay ? delay : MaxRetryDelay;
        }

        protected void Report(string message)
        {
            _report?.Invoke(message);
        }

        private void Run(CancellationToken token)
        {
            Log.Information("Enter " + _name + " task");
            Report("Enter " + _name + " task");
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var delay = RunIteration();
                    if (delay > TimeSpan.Zero && token.WaitHandle.WaitOne(delay))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in " + _name + " task");
                Report("Unexpected error in " + _name + " task: " + ex.Message);
            }
            finally
            {
                State = TaskState.Stop;
                Log.Information("Leave " + _name + " task");
                Report("Leave " + _name + " task");
            }
        }
    }
}
