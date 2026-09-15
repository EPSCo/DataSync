using System.Collections.Generic;
using System.Linq;
using DataSync.Core.Models;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class SyncRangePlannerTests
    {
        private static BaseIdRange R(long begin, long end)
        {
            return new BaseIdRange { BaseIdBegin = begin, BaseIdEnd = end };
        }

        private static string Describe(IEnumerable<SyncRange> ranges)
        {
            return string.Join(" ", ranges.Select(r => $"{r.Status}[{r.Range.BaseIdBegin}-{r.Range.BaseIdEnd}]"));
        }

        [TestMethod]
        public void Build_SplitsRemoteRangeIntoSyncedPartsAndGaps()
        {
            var ranges = SyncRangePlanner.Build(new[] { R(1, 100) }, new[] { R(1, 20), R(41, 60) });

            Assert.AreEqual("Synced[1-20] NotSync[21-40] Synced[41-60] NotSync[61-100]", Describe(ranges));
            Assert.AreEqual(40, ranges[1].StartSyncPoint);
            Assert.AreEqual(100, ranges[3].StartSyncPoint);
        }

        [TestMethod]
        public void Normalize_KeepsPartOfGapThatCrossesBoundary()
        {
            // Startup after an outage: local stopped at 500, remote reached 1000, real-time starts at 1000.
            var built = SyncRangePlanner.Build(new[] { R(1, 1000) }, new[] { R(1, 500) });

            var ranges = SyncRangePlanner.Normalize(built, 1000);

            Assert.AreEqual("Synced[1-500] NotSync[501-999] RealTime[1000-1000]", Describe(ranges));
            Assert.AreEqual(999, ranges[1].StartSyncPoint);
        }

        [TestMethod]
        public void Normalize_DropsRangesAtOrAboveBoundaryAndReplacesRealTimeRow()
        {
            var built = SyncRangePlanner.Build(new[] { R(1, 100), R(200, 300) }, new[] { R(1, 100) });
            var first = SyncRangePlanner.Normalize(built, 150);

            var second = SyncRangePlanner.Normalize(first, 150);

            Assert.AreEqual("Synced[1-100] RealTime[150-150]", Describe(second));
        }

        [TestMethod]
        public void FindNextRangeIndex_PicksNewestNotSyncRange()
        {
            var ranges = SyncRangePlanner.Build(new[] { R(1, 100) }, new[] { R(21, 40), R(61, 80) });

            var index = SyncRangePlanner.FindNextRangeIndex(ranges);

            Assert.AreEqual("NotSync[81-100]", Describe(new[] { ranges[index] }));
        }

        [TestMethod]
        public void FindNextRangeIndex_ReturnsMinusOneWhenEverythingIsSynced()
        {
            var ranges = SyncRangePlanner.Normalize(SyncRangePlanner.Build(new[] { R(1, 100) }, new[] { R(1, 100) }), 101);

            Assert.AreEqual(-1, SyncRangePlanner.FindNextRangeIndex(ranges));
        }
    }
}
