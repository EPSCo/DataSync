using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Management;
using DataSync.Core.Logging;
using DataSync.Core.Security;

namespace DataSync.Core.Data
{
    public enum ConnectionKind
    {
        /// <summary>The local copy (normally OfficeDDR).</summary>
        Local,

        /// <summary>The source DDR/DDM database.</summary>
        Remote
    }

    /// <summary>
    /// Connection strings from <c>local.eps</c> / <c>remote.eps</c> next to the executable. Each file is encrypted text
    /// <c>{CPU ProcessorId}@@@@{connection string}</c>, produced by DDUtility; the ProcessorId binds the app to one machine.
    /// </summary>
    public sealed class DatabaseConfig
    {
        public const string LocalFileName = "local.eps";
        public const string RemoteFileName = "remote.eps";
        private const string Separator = "@@@@";

        private static readonly Lazy<DatabaseConfig> LazyInstance =
            new Lazy<DatabaseConfig>(() => new DatabaseConfig(AppDomain.CurrentDomain.BaseDirectory));

        private readonly ConnectionFile _local;
        private readonly ConnectionFile _remote;
        private readonly Dictionary<ConnectionKind, string> _lastConnectionErrors = new Dictionary<ConnectionKind, string>();

        private DatabaseConfig(string directory)
        {
            _local = ConnectionFile.Read(Path.Combine(directory, LocalFileName));
            _remote = ConnectionFile.Read(Path.Combine(directory, RemoteFileName));

            Log.Information("Local database (" + LocalFileName + "): " + Describe(ConnectionKind.Local));
            Log.Information("Remote database (" + RemoteFileName + "): " + Describe(ConnectionKind.Remote));
        }

        public static DatabaseConfig Instance => LazyInstance.Value;

        public string GetConnectionString(ConnectionKind kind)
        {
            return (kind == ConnectionKind.Local ? _local : _remote).ConnectionString;
        }

        /// <summary>
        /// Server, database, authentication and connection options, for the log. Never includes the password.
        /// </summary>
        public string Describe(ConnectionKind kind)
        {
            var connectionString = GetConnectionString(kind);
            if (string.IsNullOrEmpty(connectionString))
            {
                return "no connection string";
            }

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return "server " + builder.DataSource +
                       ", database " + builder.InitialCatalog +
                       (builder.IntegratedSecurity ? ", Windows authentication" : ", SQL login " + builder.UserID) +
                       ", connect timeout " + builder.ConnectTimeout + " s" +
                       ", connect retries " + builder.ConnectRetryCount +
                       (builder.Encrypt ? ", encrypted" : "") +
                       (builder.MultiSubnetFailover ? ", multi-subnet failover" : "");
            }
            catch (Exception ex)
            {
                return "connection string cannot be read (" + ex.GetType().Name + ")";
            }
        }

        /// <summary>
        /// True when both files were issued for this machine's CPU.
        /// </summary>
        public bool VerifyHardwareLock()
        {
            try
            {
                var processorId = GetProcessorId();
                var localMatches = !string.IsNullOrEmpty(processorId) && _local.MachineId == processorId;
                var remoteMatches = !string.IsNullOrEmpty(processorId) && _remote.MachineId == processorId;
                if (string.IsNullOrEmpty(processorId))
                {
                    Log.Warning("Hardware lock: this computer's CPU ProcessorId could not be read");
                }
                else if (!localMatches || !remoteMatches)
                {
                    Log.Warning("Hardware lock: not issued for this computer: " +
                                (localMatches ? "" : LocalFileName + " ") + (remoteMatches ? "" : RemoteFileName));
                }
                return localMatches && remoteMatches;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Hardware lock verification failed");
                return false;
            }
        }

        /// <summary>
        /// Opens a connection and logs the result with how long it took. A failure repeated with the same message
        /// (e.g. while retrying during an outage) is logged on one line, without the stack trace.
        /// </summary>
        public bool CheckConnection(ConnectionKind kind)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var connection = new SqlConnection(GetConnectionString(kind)))
                {
                    connection.Open();
                    Log.Information("Connected to the " + kind + " database (" + connection.DataSource + ", SQL Server " +
                                    connection.ServerVersion + ") in " + stopwatch.ElapsedMilliseconds + " ms");
                    lock (_lastConnectionErrors)
                    {
                        _lastConnectionErrors.Remove(kind);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                var message = "Connection to the " + kind + " database failed after " + stopwatch.ElapsedMilliseconds + " ms";
                bool repeated;
                lock (_lastConnectionErrors)
                {
                    repeated = _lastConnectionErrors.TryGetValue(kind, out var last) && last == ex.Message;
                    _lastConnectionErrors[kind] = ex.Message;
                }

                if (repeated)
                {
                    Log.Error(message + ": " + ex.Message);
                }
                else
                {
                    Log.Error(ex, message);
                }
                return false;
            }
        }

        private static string GetProcessorId()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
            using (var results = searcher.Get())
            {
                foreach (var result in results)
                {
                    using (result)
                    {
                        var id = result["ProcessorId"]?.ToString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id;
                        }
                    }
                }
            }
            return string.Empty;
        }

        private sealed class ConnectionFile
        {
            public string MachineId { get; private set; } = string.Empty;
            public string ConnectionString { get; private set; } = string.Empty;

            public static ConnectionFile Read(string path)
            {
                var file = new ConnectionFile();
                if (!File.Exists(path))
                {
                    Log.Warning("Connection file not found: " + path);
                    return file;
                }

                try
                {
                    var parts = Encryption.Decrypt(File.ReadAllText(path)).Split(new[] { Separator }, StringSplitOptions.None);
                    file.MachineId = parts[0];
                    if (parts.Length > 1)
                    {
                        file.ConnectionString = parts[1];
                    }
                    else
                    {
                        Log.Warning("Connection file has no connection string: " + path);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Cannot read connection file " + path);
                }

                return file;
            }
        }
    }
}
