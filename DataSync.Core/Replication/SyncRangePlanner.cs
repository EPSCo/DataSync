using System.Collections.Generic;
using System.Linq;
using DataSync.Core.Models;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Range arithmetic for the sync task: which BaseID ranges are missing locally and which one to copy next.
    /// No database access, so it can be tested directly.
    /// </summary>
    public static class SyncRangePlanner
    {
        /// <summary>
        /// Splits every remote BaseID range into Synced parts (present locally) and NotSync gaps.
        /// </summary>
        public static List<SyncRange> Build(IEnumerable<BaseIdRange> remoteRanges, IEnumerable<BaseIdRange> localRanges)
        {
            var local = localRanges.ToList();
            var result = new List<SyncRange>();

            foreach (var remoteRange in remoteRanges)
            {
                var relevantLocalRanges = local
                    .Where(l => l.BaseIdBegin >= remoteRange.BaseIdBegin && l.BaseIdEnd <= remoteRange.BaseIdEnd)
                    .OrderBy(l => l.BaseIdBegin)
                    .ToList();

                var currentStart = remoteRange.BaseIdBegin;

                foreach (var localRange in relevantLocalRanges)
                {
                    if (currentStart < localRange.BaseIdBegin)
                    {
                        result.Add(Create(currentStart, localRange.BaseIdBegin - 1, localRange.BaseIdBegin - 1, RangeStatus.NotSync));
                    }

                    result.Add(Create(localRange.BaseIdBegin, localRange.BaseIdEnd, -1, RangeStatus.Synced));

                    currentStart = localRange.BaseIdEnd + 1;
                }

                if (currentStart <= remoteRange.BaseIdEnd)
                {
                    result.Add(Create(currentStart, remoteRange.BaseIdEnd, remoteRange.BaseIdEnd, RangeStatus.NotSync));
                }
            }

            return result;
        }

        /// <summary>
        /// Keeps only the part of each range below <paramref name="boundary"/> (from the boundary up is the real-time
        /// task's job) and appends the RealTime row. A range crossing the boundary is trimmed, not dropped: at startup
        /// that range holds the rows the remote wrote while the app was stopped.
        /// </summary>
        public static List<SyncRange> Normalize(IEnumerable<SyncRange> ranges, long boundary)
        {
            var result = new List<SyncRange>();

            foreach (var item in ranges)
            {
                if (item.Status == RangeStatus.RealTime)
                {
                    continue; // replaced by the new RealTime row below
                }

                if (item.Status == RangeStatus.Syncing)
                {
                    item.Status = RangeStatus.NotSync; // picked again by FindNextRangeIndex
                }

                if (item.Range.BaseIdEnd < boundary)
                {
                    result.Add(item);
                }
                else if (item.Range.BaseIdBegin < boundary)
                {
                    item.Range.BaseIdEnd = boundary - 1;
                    if (item.Status == RangeStatus.NotSync)
                    {
                        item.StartSyncPoint = boundary - 1;
                    }
                    result.Add(item);
                }
            }

            MergeSynced(result);
            result.Add(Create(boundary, boundary, -1, RangeStatus.RealTime));
            return result;
        }

        /// <summary>
        /// Joins neighbouring Synced ranges (in a list ordered by BaseIdBegin) into one. BaseIDs between them are
        /// missing on the remote too, so there is nothing to copy there and one row is shown instead of many.
        /// </summary>
        public static void MergeSynced(List<SyncRange> ranges)
        {
            for (var i = ranges.Count - 1; i > 0; i--)
            {
                var previous = ranges[i - 1];
                var item = ranges[i];
                if (previous.Status == RangeStatus.Synced && item.Status == RangeStatus.Synced)
                {
                    previous.Range.BaseIdEnd = System.Math.Max(previous.Range.BaseIdEnd, item.Range.BaseIdEnd);
                    ranges.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Index of the NotSync range with the highest BaseIdBegin (newest data first), or -1 if there is none.
        /// </summary>
        public static int FindNextRangeIndex(IList<SyncRange> ranges)
        {
            var maxIndex = -1;
            var maxBaseIdBegin = long.MinValue;

            for (var i = 0; i < ranges.Count; i++)
            {
                var item = ranges[i];
                if (item.Status == RangeStatus.NotSync && item.Range.BaseIdBegin > maxBaseIdBegin)
                {
                    maxBaseIdBegin = item.Range.BaseIdBegin;
                    maxIndex = i;
                }
            }

            return maxIndex;
        }

        public static SyncRange Copy(SyncRange item)
        {
            return Create(item.Range.BaseIdBegin, item.Range.BaseIdEnd, item.StartSyncPoint, item.Status);
        }

        private static SyncRange Create(long begin, long end, long startSyncPoint, RangeStatus status)
        {
            return new SyncRange
            {
                Range = new BaseIdRange { BaseIdBegin = begin, BaseIdEnd = end },
                StartSyncPoint = startSyncPoint,
                Status = status
            };
        }
    }
}
