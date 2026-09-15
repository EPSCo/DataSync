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
        private readonly List<long> _savedOrder = new List<long>();

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

        /// <summary>
        /// When above 0, reads asking for more rows (or a wider BaseID range) than this throw a TimeoutException,
        /// like a slow link where large reads exceed the command timeout.
        /// </summary>
        public int ReadTimeoutAboveRows { get; set; }

        /// <summary>BaseIDs in the order SaveBatch inserted them (rows added directly are not included).</summary>
        public List<long> SavedOrder
        {
            get
            {
                lock (_lock)
                {
                    return _savedOrder.ToList();
                }
            }
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
                ThrowIfReadTooLarge(maxRows);
                return _rows.Where(r => r.Key > baseId).Take(maxRows).Select(r => r.Value).ToList();
            }
        }

        public List<TProcessData> GetRange(long firstBaseId, long lastBaseId)
        {
            lock (_lock)
            {
                ThrowIfFailureInjected(nameof(GetRange));
                ThrowIfReadTooLarge(lastBaseId - firstBaseId + 1);
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
                    _savedOrder.Add(row.BaseID);
                }
            }
        }

        private void ThrowIfReadTooLarge(long rows)
        {
            if (ReadTimeoutAboveRows > 0 && rows > ReadTimeoutAboveRows)
            {
                throw new TimeoutException("Read of " + rows + " rows timed out");
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
