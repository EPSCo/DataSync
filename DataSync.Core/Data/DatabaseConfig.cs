using System;
using System.Data.SqlClient;
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

        private DatabaseConfig(string directory)
        {
            _local = ConnectionFile.Read(Path.Combine(directory, LocalFileName));
            _remote = ConnectionFile.Read(Path.Combine(directory, RemoteFileName));
        }

        public static DatabaseConfig Instance => LazyInstance.Value;

        public string GetConnectionString(ConnectionKind kind)
        {
            return (kind == ConnectionKind.Local ? _local : _remote).ConnectionString;
        }

        /// <summary>
        /// True when both files were issued for this machine's CPU.
        /// </summary>
        public bool VerifyHardwareLock()
        {
            try
            {
                var processorId = GetProcessorId();
                return !string.IsNullOrEmpty(processorId) &&
                       _local.MachineId == processorId &&
                       _remote.MachineId == processorId;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Hardware lock verification failed");
                return false;
            }
        }

        public bool CheckConnection(ConnectionKind kind)
        {
            try
            {
                using (var connection = new SqlConnection(GetConnectionString(kind)))
                {
                    connection.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Connection to the " + kind + " database failed");
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
