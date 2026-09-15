using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Reflection;

namespace DataSync.Core.Data
{
    /// <summary>
    /// Small ADO.NET helpers: run a stored procedure and map result columns to properties with the same name.
    /// </summary>
    internal static class SqlDb
    {
        private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> PropertyMaps =
            new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();

        /// <summary>SqlCommand's own default, in seconds.</summary>
        public const int DefaultCommandTimeout = 30;

        public static List<T> QueryProcedure<T>(string connectionString, int commandTimeout, string procedure, params SqlParameter[] parameters)
            where T : new()
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateProcedure(connection, commandTimeout, procedure, parameters))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    return Map<T>(reader);
                }
            }
        }

        public static void ExecuteProcedure(string connectionString, int commandTimeout, string procedure, params SqlParameter[] parameters)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = CreateProcedure(connection, commandTimeout, procedure, parameters))
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public static void ExecuteText(SqlConnection connection, SqlTransaction transaction, int commandTimeout, string sql)
        {
            using (var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = commandTimeout })
            {
                command.ExecuteNonQuery();
            }
        }

        public static PropertyInfo[] GetColumnProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }

        private static SqlCommand CreateProcedure(SqlConnection connection, int commandTimeout, string procedure, SqlParameter[] parameters)
        {
            var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = commandTimeout };
            if (parameters != null)
            {
                command.Parameters.AddRange(parameters);
            }
            return command;
        }

        private static List<T> Map<T>(SqlDataReader reader) where T : new()
        {
            var properties = PropertyMaps.GetOrAdd(typeof(T), BuildPropertyMap);

            // Columns without a matching writable property are skipped.
            var columns = new PropertyInfo[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                properties.TryGetValue(reader.GetName(i), out columns[i]);
            }

            var result = new List<T>();
            while (reader.Read())
            {
                var item = new T();
                for (var i = 0; i < columns.Length; i++)
                {
                    if (columns[i] != null && !reader.IsDBNull(i))
                    {
                        SetValue(item, columns[i], reader.GetValue(i));
                    }
                }
                result.Add(item);
            }
            return result;
        }

        private static Dictionary<string, PropertyInfo> BuildPropertyMap(Type type)
        {
            var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in GetColumnProperties(type))
            {
                if (property.CanWrite)
                {
                    map[property.Name] = property;
                }
            }
            return map;
        }

        private static void SetValue(object item, PropertyInfo property, object value)
        {
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!targetType.IsInstanceOfType(value))
            {
                value = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            property.SetValue(item, value);
        }
    }
}
