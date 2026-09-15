using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DataSync.Core.Logging
{
    /// <summary>
    /// Minimal thread-safe rolling file logger. Files are named <c>{name}-{period}.log</c> (e.g. <c>DataSync-20260915.log</c>),
    /// with <c>_001</c>, <c>_002</c>... appended when a file reaches its size limit. Does nothing until
    /// <see cref="Configure"/> is called, and never throws.
    /// </summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string[] LevelTags = { "DBG", "INF", "WRN", "ERR" };

        private static LogSettings _settings;
        private static string _baseName;
        private static StreamWriter _writer;
        private static string _period;
        private static int _sequence;

        public static void Configure(string baseName, LogSettings settings)
        {
            lock (Sync)
            {
                CloseWriter();
                _baseName = baseName;
                _settings = settings;
                _period = null;
            }
        }

        /// <summary>
        /// The folder the log files are written to, or null before <see cref="Configure"/>.
        /// </summary>
        public static string LogDirectory => Volatile.Read(ref _settings)?.Directory;

        public static void Debug(string message) => Write(LogLevel.Debug, message, null);

        public static void Information(string message) => Write(LogLevel.Information, message, null);

        public static void Warning(string message) => Write(LogLevel.Warning, message, null);

        public static void Error(string message) => Write(LogLevel.Error, message, null);

        public static void Error(Exception exception, string message) => Write(LogLevel.Error, message, exception);

        /// <summary>
        /// Flushes and closes the current file. Later writes reopen it.
        /// </summary>
        public static void Close()
        {
            lock (Sync)
            {
                CloseWriter();
                _period = null;
            }
        }

        private static void Write(LogLevel level, string message, Exception exception)
        {
            var settings = Volatile.Read(ref _settings);
            if (settings == null || level < settings.MinimumLevel)
            {
                return;
            }

            var now = DateTime.Now;
            var text = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(LevelTags[(int)level]).Append("] Thread ")
                .Append(Thread.CurrentThread.ManagedThreadId).Append(' ')
                .AppendLine(message);
            if (exception != null)
            {
                text.AppendLine(exception.ToString());
            }

            lock (Sync)
            {
                if (_settings == null)
                {
                    return;
                }

                try
                {
                    var line = text.ToString();
                    EnsureWriter(now, Utf8NoBom.GetByteCount(line));
                    _writer.Write(line);
                }
                catch
                {
                    // Logging must never take the application down.
                    CloseWriter();
                    _period = null;
                }
            }
        }

        // Caller holds Sync.
        private static void EnsureWriter(DateTime now, int byteCount)
        {
            var period = FormatPeriod(now);
            if (_writer == null || period != _period)
            {
                CloseWriter();
                _period = period;
                _sequence = 0;
                OpenWriter();
                DeleteOldFiles();
            }

            if (_settings.RollOnFileSizeLimit && _writer.BaseStream.Length > 0 &&
                _writer.BaseStream.Length + byteCount > _settings.FileSizeLimitBytes)
            {
                CloseWriter();
                _sequence++;
                OpenWriter();
                DeleteOldFiles();
            }
        }

        // Caller holds Sync. Skips files already full (e.g. from an earlier run in the same period).
        private static void OpenWriter()
        {
            Directory.CreateDirectory(_settings.Directory);

            string path;
            while (true)
            {
                path = GetPath(_sequence);
                if (!_settings.RollOnFileSizeLimit || !File.Exists(path) || new FileInfo(path).Length < _settings.FileSizeLimitBytes)
                {
                    break;
                }
                _sequence++;
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream, Utf8NoBom) { AutoFlush = true };
        }

        // Caller holds Sync.
        private static void DeleteOldFiles()
        {
            var current = GetPath(_sequence);
            var oldFiles = new DirectoryInfo(_settings.Directory)
                .GetFiles(_baseName + "-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(_settings.RetainedFileCountLimit);

            foreach (var file in oldFiles)
            {
                if (string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                    // In use by another process; try again at the next roll.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // Caller holds Sync.
        private static void CloseWriter()
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
                // ignored
            }
            _writer = null;
        }

        private static string GetPath(int sequence)
        {
            var name = _baseName + "-" + _period + (sequence > 0 ? "_" + sequence.ToString("000", CultureInfo.InvariantCulture) : "");
            return Path.GetFullPath(Path.Combine(_settings.Directory, name + ".log"));
        }

        private static string FormatPeriod(DateTime now)
        {
            switch (_settings.RollingInterval)
            {
                case RollingInterval.Year:   return now.ToString("yyyy", CultureInfo.InvariantCulture);
                case RollingInterval.Month:  return now.ToString("yyyyMM", CultureInfo.InvariantCulture);
                case RollingInterval.Day:    return now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                case RollingInterval.Hour:   return now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
                case RollingInterval.Minute: return now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                default:                     return "log";
            }
        }
    }
}
