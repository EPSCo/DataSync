using System.Collections.Generic;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// The TProcessData operations replication needs from one database (the remote source or the local copy).
    /// </summary>
    public interface IProcessDataStore
    {
        /// <summary>
        /// The row with the highest BaseID, or a row with BaseID 0 when the table is empty.
        /// </summary>
        TProcessData GetLastRecord();

        /// <summary>
        /// The contiguous BaseID ranges present in the table, ordered by BaseIdBegin.
        /// </summary>
        List<BaseIdRange> GetBaseIdRanges();

        /// <summary>
        /// Up to <paramref name="maxRows"/> rows with a BaseID greater than <paramref name="baseId"/>, in BaseID order.
        /// </summary>
        List<TProcessData> GetRowsAfter(long baseId, int maxRows);

        /// <summary>
        /// The rows with a BaseID between <paramref name="firstBaseId"/> and <paramref name="lastBaseId"/> (inclusive).
        /// </summary>
        List<TProcessData> GetRange(long firstBaseId, long lastBaseId);

        /// <summary>
        /// Saves the rows in one transaction, skipping BaseIDs that already exist.
        /// </summary>
        void SaveBatch(IList<TProcessData> rows);
    }
}
