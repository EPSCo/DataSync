using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using DataSync.Core.Logging;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Finds the best sync batch-size multiplier by trying each of several multipliers for a fixed time and counting
    /// the rows the sync task received. Each multiplier is set on the sync task's batch size, held for the phase
    /// (normally 20 minutes), and the rows copied during the phase are logged; a summary at the end lists them all.
    /// </summary>
    public class SyncMultiplierExperiment
    {
        public static readonly double[] DefaultMultipliers = { 0.5, 0.75, 1, 1.5, 2, 3, 4, 5, 6, 8, 10 };

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private readonly SyncBatchSize _batch;
        private readonly Func<long> _rowsCopied;
        private readonly double[] _multipliers;
        private readonly TimeSpan _phase;
        private readonly Action<string> _report;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _thread;

        /// <param name="batch">The sync task's batch size; its multiplier is changed each phase.</param>
        /// <param name="rowsCopied">Returns the sync task's running total of rows copied.</param>
        /// <param name="multipliers">The multipliers to try, in order.</param>
        /// <param name="phase">How long each multiplier is tried.</param>
        /// <param name="report">Where progress messages go (the application's message list); may be null.</param>
        /// <param name="csvDirectory">Folder for the results CSV file; the log folder when null or empty.</param>
        public SyncMultiplierExperiment(SyncBatchSize batch, Func<long> rowsCopied, IEnumerable<double> multipliers,
                                        TimeSpan phase, Action<string> report, string csvDirectory = null)
        {
            _batch = batch;
            _rowsCopied = rowsCopied;
            _multipliers = new List<double>(multipliers).ToArray();
            _phase = phase;
            _report = report;
            _csvPath = Path.Combine(string.IsNullOrEmpty(csvDirectory)
                                        ? LogSettings.FromAppSettings().Directory
                                        : csvDirectory,
                                    "BatchMultiplierTest_" +
                                    DateTime.Now.ToString("yyyyMMdd_HHmmss", Invariant) + ".csv");
        }

        private readonly string _csvPath;

        /// <summary>The CSV header, written once when the file is created.</summary>
        private const string CsvHeader = "Start (UTC),Multiplier,Minutes,Rows,RowsPerSecond";

        /// <summary>Appends one line for a finished phase, creating the file with its header first.</summary>
        private void WriteCsvLine(DateTime start, double multiplier, double minutes, long rows, double rowsPerSecond)
        {
            try
            {
                var directory = Path.GetDirectoryName(_csvPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = string.Join(",",
                                       start.ToString("yyyy-MM-dd HH:mm:ss", Invariant),
                                       multiplier.ToString("0.##", Invariant),
                                       minutes.ToString("0.#", Invariant),
                                       rows.ToString(Invariant),
                                       rowsPerSecond.ToString("0.0", Invariant));
                var exists = File.Exists(_csvPath);
                File.AppendAllText(_csvPath, (exists ? "\r\n" : CsvHeader + "\r\n") + line);
            }
            catch (Exception ex)
            {
                Log.Warning("Cannot write the batch multiplier test CSV file " + _csvPath + ": " + ex.Message);
            }
        }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(() => Run(_cts.Token))
            {
                IsBackground = true,
                Name = "SyncMultiplierExperiment"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _cts.Cancel();
            _thread?.Join(TimeSpan.FromSeconds(5));
        }

        private void Run(CancellationToken token)
        {
            var minutes = _phase.TotalMinutes.ToString("0", Invariant);
            Log.Information("Batch multiplier test starting: multipliers " + Join(_multipliers) +
                            ", " + minutes + " minutes each");
            _report?.Invoke("Batch multiplier test starting: multipliers " + Join(_multipliers) +
                   ", " + minutes + " minutes each");

            var results = new List<string>();
            foreach (var multiplier in _multipliers)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                _batch.SetMultiplier(multiplier);
                var startRows = _rowsCopied();
                var start = DateTime.UtcNow;
                var end = start + _phase;
                while (DateTime.UtcNow < end && !token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
                {
                }

                var rows = Math.Max(0, _rowsCopied() - startRows);
                var seconds = Math.Max(0.001, (DateTime.UtcNow - start).TotalSeconds);
                var line = "multiplier " + multiplier.ToString("0.##", Invariant) + ": " + rows + " rows in " +
                           (seconds / 60).ToString("0.0", Invariant) + " min (" +
                           (rows / seconds).ToString("0.0", Invariant) + " rows/s)";
                results.Add(line);
                Log.Information("[Batch multiplier test] " + line);
                _report?.Invoke("Batch multiplier test, " + line);
                WriteCsvLine(start, multiplier, seconds / 60, rows, rows / seconds);
            }

            _batch.SetMultiplier(0);
            Log.Information("Batch multiplier test finished. Rows received per multiplier:\r\n  " +
                            string.Join("\r\n  ", results));
            _report?.Invoke("Batch multiplier test finished; results are in " + _csvPath);
        }

        private static string Join(double[] values)
        {
            var text = new StringBuilder();
            foreach (var value in values)
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.Append(value.ToString("0.##", Invariant));
            }

            return text.ToString();
        }
    }
}
