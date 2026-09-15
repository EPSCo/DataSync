using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataSync.Core.Logging;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// A background loop that repeats <see cref="RunIteration"/> until stopped, with the failure counting, retry
    /// back-off, throttling and logging shared by the replication tasks. Every minute it logs a summary of its reads.
    /// </summary>
    public abstract class ReplicationTask
    {
        protected static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(1);

        private readonly string _name;
        private readonly Action<string> _report;
        private CancellationTokenSource _cts;
        private Task _task;
        private int _state = (int)TaskState.Stop;
        private int _failureCount;
        private long _rowsCopied;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        // Task thread only: figures for the periodic summary and the failure log.
        private readonly Stopwatch _summaryClock = new Stopwatch();
        private readonly Stopwatch _failingClock = new Stopwatch();
        private int _summaryReads;
        private long _summaryRows;
        private int _summaryFailures;
        private TimeSpan _summaryReadTime;
        private TimeSpan _summaryMaxReadTime;
        private string _lastFailure;

        /// <param name="batch">Sizes the task's remote reads.</param>
        protected ReplicationTask(string name, ReplicationSettings settings, AdaptiveBatchSize batch, Action<string> report)
        {
            _name = name;
            Settings = settings;
            Batch = batch;
            _report = report;
        }

        protected ReplicationSettings Settings { get; }

        /// <summary>The longest wait before retrying after a failure.</summary>
        protected virtual TimeSpan MaxRetryDelay => DefaultMaxRetryDelay;

        /// <summary>Rows to ask for in the next remote read. Only the task thread reports reads to it.</summary>
        protected AdaptiveBatchSize Batch { get; }

        /// <summary>Rows the next remote read asks for.</summary>
        public int BatchSize => Batch.Current;

        /// <summary>Rows read and saved since the task was created (including rows that already existed locally).</summary>
        public long RowsCopied => Interlocked.Read(ref _rowsCopied);

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
        /// Ends the current wait between iterations early, so new work is picked up now. Thread-safe.
        /// </summary>
        public void Wake()
        {
            _wake.Set();
        }

        /// <summary>
        /// Does one unit of work and returns how long to wait before the next one.
        /// </summary>
        public abstract TimeSpan RunIteration();

        /// <summary>
        /// Task-specific state (position, work left) appended to the periodic summary. Called on the task thread.
        /// </summary>
        protected virtual string DescribeState()
        {
            return null;
        }

        /// <summary>
        /// Resets the failure count and returns the delay before the next read: none while there is a
        /// <paramref name="backlog"/>, otherwise <paramref name="interval"/>; never faster than MaxRowsPerSecond.
        /// </summary>
        protected TimeSpan OnSuccess(int rowCount, bool backlog, TimeSpan interval, TimeSpan elapsed)
        {
            var failures = Interlocked.Exchange(ref _failureCount, 0);
            Interlocked.Add(ref _rowsCopied, rowCount);
            _summaryRows += rowCount;

            if (failures > 0)
            {
                var message = _name + " recovered after " + failures + " failed attempt" + (failures == 1 ? "" : "s") +
                              " (" + FormatSeconds(_failingClock.Elapsed) + ")";
                Log.Information(message);
                Report(message);
                _failingClock.Reset();
                _lastFailure = null;
            }

            var delay = backlog ? TimeSpan.Zero : interval;
            if (Settings.MaxRowsPerSecond > 0)
            {
                var throttle = TimeSpan.FromMilliseconds(rowCount * 1000.0 / Settings.MaxRowsPerSecond);
                if (throttle > delay)
                {
                    delay = throttle;
                    Log.Debug(_name + " throttled to " + Settings.MaxRowsPerSecond + " rows/s: waiting " + FormatSeconds(delay));
                }
            }

            return delay - elapsed;
        }

        /// <summary>
        /// Logs the error, counts the failure and returns the retry delay: 1 s, 2 s, 4 s, ... up to <see cref="MaxRetryDelay"/>.
        /// </summary>
        protected TimeSpan OnFailure(string operation, Exception ex)
        {
            var failureCount = Interlocked.Increment(ref _failureCount);
            _summaryFailures++;
            if (failureCount == 1)
            {
                _failingClock.Restart();
            }

            var delay = TimeSpan.FromSeconds(1 << Math.Min(failureCount - 1, 6));
            if (delay > MaxRetryDelay)
            {
                delay = MaxRetryDelay;
            }

            var message = "Error in " + operation + ": " + ex.Message;
            var details = message + " (failure " + failureCount + ", failing for " + FormatSeconds(_failingClock.Elapsed) +
                          ", retry in " + FormatSeconds(delay) + ")";
            if (message == _lastFailure)
            {
                // The same error again, e.g. during an outage: one line, without repeating the stack trace.
                Log.Error(details);
            }
            else
            {
                Log.Error(ex, details);
                _lastFailure = message;
            }
            Report(message);

            return delay;
        }

        /// <summary>
        /// Counts a successful remote read for the periodic summary.
        /// </summary>
        protected void RecordRead(TimeSpan elapsed)
        {
            _summaryReads++;
            _summaryReadTime += elapsed;
            if (elapsed > _summaryMaxReadTime)
            {
                _summaryMaxReadTime = elapsed;
            }
        }

        /// <summary>
        /// Runs <paramref name="adjust"/>, which feeds <see cref="Batch"/>, and logs a size change at Debug with the reason.
        /// </summary>
        protected void AdjustBatch(Action adjust, Func<string> reason)
        {
            var before = Batch.Current;
            adjust();
            if (Batch.Current != before)
            {
                Log.Debug(_name + " batch size changed from " + before + " to " + Batch.Current + " rows: " + reason());
            }
        }

        /// <summary>
        /// Shrinks <see cref="Batch"/> after a failed remote read, then handles the failure like <see cref="OnFailure"/>.
        /// </summary>
        protected TimeSpan OnReadFailure(string operation, Exception ex)
        {
            var before = Batch.Current;
            Batch.OnReadFailed(ex);
            if (Batch.Current < before)
            {
                Log.Information(_name + " batch size reduced from " + before + " to " + Batch.Current + " rows: " +
                                (AdaptiveBatchSize.IsTimeout(ex) ? "a read timed out" : "a read failed"));
            }
            return OnFailure(operation, ex);
        }

        protected void Report(string message)
        {
            _report?.Invoke(message);
        }

        protected static string FormatSeconds(TimeSpan time)
        {
            return time.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        protected static string FormatRate(double perSecond)
        {
            return perSecond.ToString("0.0", CultureInfo.InvariantCulture) + "/s";
        }

        private void LogSummary()
        {
            var elapsed = _summaryClock.Elapsed;
            var text = new StringBuilder()
                .Append(_name).Append(" summary for the last ").Append(FormatSeconds(elapsed)).Append(": ")
                .Append(_summaryReads).Append(" reads, ").Append(_summaryRows).Append(" rows (")
                .Append((elapsed.TotalSeconds > 0 ? _summaryRows / elapsed.TotalSeconds : 0).ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" rows/s)");
            if (_summaryReads > 0)
            {
                text.Append(", read time avg ").Append(FormatSeconds(TimeSpan.FromTicks(_summaryReadTime.Ticks / _summaryReads)))
                    .Append(" max ").Append(FormatSeconds(_summaryMaxReadTime));
            }
            text.Append(", ").Append(_summaryFailures).Append(" failures, batch ").Append(Batch.Current).Append(" rows");

            string state;
            try
            {
                state = DescribeState();
            }
            catch (Exception ex)
            {
                state = "state unavailable: " + ex.Message;
            }
            if (!string.IsNullOrEmpty(state))
            {
                text.Append("; ").Append(state);
            }

            Log.Information(text.ToString());

            _summaryReads = 0;
            _summaryRows = 0;
            _summaryFailures = 0;
            _summaryReadTime = TimeSpan.Zero;
            _summaryMaxReadTime = TimeSpan.Zero;
            _summaryClock.Restart();
        }

        private void Run(CancellationToken token)
        {
            Log.Information("Enter " + _name + " task");
            Report("Enter " + _name + " task");
            _summaryClock.Restart();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var delay = RunIteration();
                    if (_summaryClock.Elapsed >= SummaryInterval)
                    {
                        LogSummary();
                    }

                    if (delay > TimeSpan.Zero && WaitHandle.WaitAny(new[] { token.WaitHandle, _wake }, delay) == 0)
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
                LogSummary();
                Log.Information("Leave " + _name + " task");
                Report("Leave " + _name + " task");
            }
        }
    }
}
