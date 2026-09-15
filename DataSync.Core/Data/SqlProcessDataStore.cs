using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using DataSync.Core.Models;
using DataSync.Core.Replication;

namespace DataSync.Core.Data
{
    /// <summary>
    /// <see cref="IProcessDataStore"/> over the local or remote SQL Server database. Reads go through the
    /// <c>Rep_*</c> stored procedures created by the scripts in <c>SQL\</c>. Exceptions are not caught here; callers log them.
    /// </summary>
    public class SqlProcessDataStore : IProcessDataStore
    {
        private static readonly PropertyInfo[] ColumnProperties = SqlDb.GetColumnProperties(typeof(TProcessData));
        private static readonly string ColumnList = string.Join(", ", ColumnProperties.Select(p => "[" + p.Name + "]"));

        private readonly ConnectionKind _kind;
        private readonly int _commandTimeout;

        /// <param name="commandTimeoutSeconds">Timeout of each command and bulk copy.</param>
        public SqlProcessDataStore(ConnectionKind kind, int commandTimeoutSeconds = SqlDb.DefaultCommandTimeout)
        {
            _kind = kind;
            _commandTimeout = commandTimeoutSeconds;
        }

        private string ConnectionString => DatabaseConfig.Instance.GetConnectionString(_kind);

        public TProcessData GetLastRecord()
        {
            return SqlDb.QueryProcedure<TProcessData>(ConnectionString, _commandTimeout, "[dbo].[Rep_TProcessData_GetLastBaseId]").FirstOrDefault()
                   ?? new TProcessData { BaseID = 0, DateTimeRecord = DateTime.Now };
        }

        public List<BaseIdRange> GetBaseIdRanges()
        {
            // Note the triple "s" in the procedure name.
            return SqlDb.QueryProcedure<BaseIdRange>(ConnectionString, _commandTimeout, "[dbo].[Rep_TProcesssData_GetBaseIdRanges]");
        }

        public List<TProcessData> GetRowsAfter(long baseId, int maxRows)
        {
            return SqlDb.QueryProcedure<TProcessData>(ConnectionString, _commandTimeout, "[dbo].[Rep_TProcessData_GetByBaseId]",
                new SqlParameter("@BaseID", SqlDbType.BigInt) { Value = baseId },
                new SqlParameter("@MaxRows", SqlDbType.Int) { Value = maxRows });
        }

        public List<TProcessData> GetRange(long firstBaseId, long lastBaseId)
        {
            return SqlDb.QueryProcedure<TProcessData>(ConnectionString, _commandTimeout, "[dbo].[Rep_TProcessData_GetRange]",
                    new SqlParameter("@FirstCode", SqlDbType.BigInt) { Value = firstBaseId },
                    new SqlParameter("@LastCode", SqlDbType.BigInt) { Value = lastBaseId })
                .OrderBy(t => t.BaseID)
                .ToList();
        }

        /// <summary>
        /// Inserts the rows in one transaction: bulk-copies them into a temporary table, then inserts only the BaseIDs
        /// not already in TProcessData, so saving a batch again never creates duplicates.
        /// </summary>
        public void SaveBatch(IList<TProcessData> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            var table = new DataTable();
            foreach (var property in ColumnProperties)
            {
                table.Columns.Add(property.Name, property.PropertyType);
            }
            foreach (var item in rows)
            {
                var row = table.NewRow();
                foreach (var property in ColumnProperties)
                {
                    row[property.Name] = property.GetValue(item);
                }
                table.Rows.Add(row);
            }

            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    SqlDb.ExecuteText(connection, transaction, _commandTimeout,
                        "SELECT TOP 0 " + ColumnList + " INTO #TProcessDataBatch FROM [dbo].[TProcessData];");

                    using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction))
                    {
                        bulkCopy.DestinationTableName = "#TProcessDataBatch";
                        bulkCopy.BulkCopyTimeout = _commandTimeout;
                        foreach (var property in ColumnProperties)
                        {
                            bulkCopy.ColumnMappings.Add(property.Name, property.Name);
                        }
                        bulkCopy.WriteToServer(table);
                    }

                    SqlDb.ExecuteText(connection, transaction, _commandTimeout,
                        "INSERT INTO [dbo].[TProcessData] (" + ColumnList + ") " +
                        "SELECT " + ColumnList + " FROM (" +
                        "SELECT *, ROW_NUMBER() OVER (PARTITION BY BaseID ORDER BY (SELECT NULL)) AS BatchRowNumber FROM #TProcessDataBatch" +
                        ") AS batch " +
                        "WHERE batch.BatchRowNumber = 1 " +
                        "AND NOT EXISTS (SELECT 1 FROM [dbo].[TProcessData] AS existing WITH (UPDLOCK, HOLDLOCK) WHERE existing.BaseID = batch.BaseID); " +
                        "DROP TABLE #TProcessDataBatch;");

                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// Empties the run-time tables with <c>Rep_Local_Truncate_Table</c>. Refused for the remote database.
        /// </summary>
        public void TruncateLocalData()
        {
            if (_kind != ConnectionKind.Local)
            {
                throw new InvalidOperationException("Only the local database can be cleared.");
            }

            SqlDb.ExecuteProcedure(ConnectionString, _commandTimeout, "[dbo].[Rep_Local_Truncate_Table]");
        }
    }
}
