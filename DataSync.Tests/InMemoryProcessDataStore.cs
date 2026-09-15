using System;
using System.Collections.Generic;
using System.Linq;
using DataSync.Core.Models;
using DataSync.Core.Replication;

namespace DataSync.Tests
{
    /// <summary>
    /// Thread-safe in-memory TProcessData table with failure injection, standing in for a SQL Server database.
    /// </summary>
    public class InMemoryProcessDataStore : IProcessDataStore
    {
        private readonly object _lock = new object();
        private readonly SortedDictionary<long, TProcessData> _rows = new SortedDictionary<long, TProcessData>();
        private readonly Dictionary<string, int> _failuresToInject = new Dictionary<string, int>();

        public InMemoryProcessDataStore(IEnumerable<long> baseIds = null)
        {
            if (baseIds != null)
            {
                Add(baseIds.ToArray());
            }
        }

        public static IEnumerable<long> Ids(long first, long last)
        {
            for (var id = first; id <= last; id++)
            {
                yield return id;
            }
        }

        public static TProcessData Row(long baseId)
        {
            return new TProcessData { BaseID = baseId, DateTimeRecord = new DateTime(2026, 1, 1).AddSeconds(baseId), WOB = baseId * 0.5 };
        }

        public List<long> BaseIds
        {
            get
            {
                lock (_lock)
                {
                    return _rows.Keys.ToList();
                }
            }
        }

        public void Add(params long[] baseIds)
        {
            lock (_lock)
            {
                foreach (var baseId in baseIds)
                {
                    _rows[baseId] = Row(baseId);
                }
            }
        }

        public void Add(IEnumerable<long> baseIds)
        {
            Add(baseIds.ToArray());
        }

        public void Remove(IEnumerable<long> baseIds)
        {
            lock (_lock)
            {
                foreach (var baseId in baseIds)
                {
                    _rows.Remove(baseId);
                }
            }
        }

        /// <summary>
        /// Makes the next <paramref name="count"/> calls of <paramref name="operation"/> throw.
        /// </summary>
        public void FailNext(string operation, int count = 1)
        {
            lock (_lock)
            {
                _failuresToInject[operation] = count;
            }
        }

        public TProcessData GetLastRecord()
        {
            lock (_lock)
            {
                ThrowIfFailureInjected(nameof(GetLastRecord));
                return _rows.Count == 0 ? new TProcessData { BaseID = 0, DateTimeRecord = DateTime.Now } : _rows.Values.Last();
            }
        }

        public List<BaseIdRange> GetBaseIdRanges()
        {
            lock (_lock)
            {
                ThrowIfFailureInjected(nameof(GetBaseIdRanges));
                var ranges = new List<BaseIdRange>();
                foreach (var baseId in _rows.Keys)
                {
                    var last = ranges.LastOrDefault();
                    if (last != null && last.BaseIdEnd + 1 == baseId)
                    {
                        last.BaseIdEnd = baseId;
                    }
                    else
                    {
                        ranges.Add(new BaseIdRange { BaseIdBegin = baseId, BaseIdEnd = baseId });
                    }
                }
                return ranges;
            }
        }

        public List<TProcessData> GetRowsAfter(long baseId, int maxRows)
        {
            lock (_lock)
            {
                ThrowIfFailureInjected(nameof(GetRowsAfter));
                return _rows.Where(r => r.Key > baseId).Take(maxRows).Select(r => r.Value).ToList();
            }
        }

        public List<TProcessData> GetRange(long firstBaseId, long lastBaseId)
        {
            lock (_lock)
            {
                ThrowIfFailureInjected(nameof(GetRange));
                return _rows.Where(r => r.Key >= firstBaseId && r.Key <= lastBaseId).Select(r => r.Value).ToList();
            }
        }

        public void SaveBatch(IList<TProcessData> rows)
        {
            lock (_lock)
            {
                ThrowIfFailureInjected(nameof(SaveBatch));
                foreach (var row in rows.Where(r => !_rows.ContainsKey(r.BaseID)))
                {
                    _rows.Add(row.BaseID, row);
                }
            }
        }

        // Caller holds _lock.
        private void ThrowIfFailureInjected(string operation)
        {
            if (_failuresToInject.TryGetValue(operation, out var remaining) && remaining > 0)
            {
                _failuresToInject[operation] = remaining - 1;
                throw new InvalidOperationException("Injected failure in " + operation);
            }
        }
    }
}
