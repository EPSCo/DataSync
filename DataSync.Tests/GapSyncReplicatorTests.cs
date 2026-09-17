using System;
using System.Linq;
using DataSync.Core.Models;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class GapSyncReplicatorTests
    {
        private static readonly TimeSpan GapCheckInterval = TimeSpan.FromMinutes(10);

        // Most tests check the windows at a fixed size; the adaptive test turns it on.
        private static GapSyncReplicator Create(InMemoryProcessDataStore remote, InMemoryProcessDataStore local, long boundary,
                                                int rowLimit = 10, bool adaptive = false)
        {
            var settings = new ReplicationSettings
            {
                SyncRowLimit = rowLimit,
                AdaptiveBatchSize = adaptive,
                SyncUpdateInterval = TimeSpan.Zero,
                MaxRowsPerSecond = 0,
                GapCheckInterval = GapCheckInterval
            };
            return new GapSyncReplicator(remote, local, () => boundary, settings, message => { });
        }

        /// <summary>Runs iterations until the replicator reports it has nothing left to sync.</summary>
        private static int RunUntilIdle(GapSyncReplicator replicator, int maxIterations = 200)
        {
            for (var i = 1; i <= maxIterations; i++)
            {
                replicator.RunIteration();
                if (replicator.State == TaskState.NoOldData)
                {
                    return i;
                }
            }
            Assert.Fail("Sync did not finish within " + maxIterations + " iterations");
            return maxIterations;
        }

        [TestMethod]
        public void RunIteration_CopiesNewestGapFirstReadingBackwards()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10).Concat(InMemoryProcessDataStore.Ids(51, 60)));
            var replicator = Create(remote, local, boundary: 101);

            replicator.RunIteration();

            CollectionAssert.IsSubsetOf(InMemoryProcessDataStore.Ids(91, 100).ToList(), local.BaseIds);
            Assert.IsFalse(local.BaseIds.Contains(90));
            Assert.IsFalse(local.BaseIds.Contains(11));
            Assert.AreEqual(90, replicator.NextBaseId);
        }

        [TestMethod]
        public void RunIteration_BackfillsAllGapsBelowBoundary()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10).Concat(InMemoryProcessDataStore.Ids(51, 60)));
            var replicator = Create(remote, local, boundary: 101);

            RunUntilIdle(replicator);

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.AreEqual(-1, replicator.NextBaseId);
            Assert.AreEqual("Synced[1-100] RealTime[101-101]",
                            string.Join(" ", replicator.GetRangesSnapshot().Select(r => $"{r.Status}[{r.Range.BaseIdBegin}-{r.Range.BaseIdEnd}]")));
        }

        [TestMethod]
        public void AddGap_FinishedSkippedRangeJoinsSyncedRanges()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            long boundary = 101;
            var settings = new ReplicationSettings
            {
                SyncRowLimit = 10,
                AdaptiveBatchSize = false,
                SyncUpdateInterval = TimeSpan.Zero,
                MaxRowsPerSecond = 0,
                GapCheckInterval = GapCheckInterval
            };
            var replicator = new GapSyncReplicator(remote, local, () => boundary, settings, message => { });
            RunUntilIdle(replicator);

            remote.Add(InMemoryProcessDataStore.Ids(101, 200));
            local.Add(InMemoryProcessDataStore.Ids(191, 200));
            boundary = 201;
            replicator.AddGap(new BaseIdRange { BaseIdBegin = 101, BaseIdEnd = 190 });
            RunUntilIdle(replicator);

            Assert.AreEqual(1, replicator.GetRangesSnapshot().Count(r => r.Status == RangeStatus.Synced));
        }

        [TestMethod]
        public void RunIteration_CopiesRowsWrittenWhileAppWasStopped()
        {
            // Regression: the gap crossing the real-time start point was dropped instead of synced.
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 40));
            var replicator = Create(remote, local, boundary: 100);

            RunUntilIdle(replicator);

            CollectionAssert.AreEqual(InMemoryProcessDataStore.Ids(1, 99).ToList(), local.BaseIds);
        }

        [TestMethod]
        public void RunIteration_DoesNotSkipRowsWhenReadsFail()
        {
            // Regression: a failed read used to move the window back one row and lose it.
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore();
            var replicator = Create(remote, local, boundary: 101, rowLimit: 7);

            replicator.RunIteration();
            remote.FailNext(nameof(InMemoryProcessDataStore.GetRange), 3);
            RunUntilIdle(replicator);

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.AreEqual(0, replicator.FailureCount);
        }

        [TestMethod]
        public void RunIteration_RetriesWindowWhenSaveFails()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 30));
            var local = new InMemoryProcessDataStore();
            var replicator = Create(remote, local, boundary: 31);

            replicator.RunIteration();
            local.FailNext(nameof(InMemoryProcessDataStore.SaveBatch), 2);
            replicator.RunIteration();
            replicator.RunIteration();

            Assert.AreEqual(2, replicator.FailureCount);
            Assert.AreEqual(20, replicator.NextBaseId);

            RunUntilIdle(replicator);
            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
        }

        [TestMethod]
        public void RunIteration_SkipsWindowsWhereRemoteHasNoRows()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 50));
            var local = new InMemoryProcessDataStore();
            var replicator = Create(remote, local, boundary: 51);
            replicator.RunIteration(); // ranges computed, 41-50 copied

            // Rows deleted on the remote after the ranges were computed must not stall the sync.
            remote.Remove(InMemoryProcessDataStore.Ids(21, 40));
            RunUntilIdle(replicator);

            CollectionAssert.AreEqual(InMemoryProcessDataStore.Ids(1, 20).Concat(InMemoryProcessDataStore.Ids(41, 50)).ToList(), local.BaseIds);
        }

        [TestMethod]
        public void RunIteration_WhenIdle_WaitsThenFindsNewGaps()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 40));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 40));
            var replicator = Create(remote, local, boundary: 51);

            var idleDelay = replicator.RunIteration();

            Assert.AreEqual(TaskState.NoOldData, replicator.State);
            Assert.AreEqual(GapCheckInterval, idleDelay);

            remote.Add(41, 42, 43, 44, 45); // rows the remote wrote late, below the real-time position
            RunUntilIdle(replicator);

            CollectionAssert.AreEqual(InMemoryProcessDataStore.Ids(1, 45).ToList(), local.BaseIds);
        }

        [TestMethod]
        public void RunIteration_AdaptiveBatchSize_BackfillsEverythingDespiteTimeouts()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3000));
            var local = new InMemoryProcessDataStore();
            var replicator = Create(remote, local, boundary: 3001, rowLimit: 1000, adaptive: true);
            remote.ReadTimeoutAboveRows = 100;

            RunUntilIdle(replicator, maxIterations: 1000);

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.IsTrue(replicator.BatchSize <= 100, $"batch size {replicator.BatchSize}");
        }

        [TestMethod]
        public void AddGap_CopiesSkippedRangeBeforeOlderGapsNewestFirst()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore();
            long boundary = 101;
            var settings = new ReplicationSettings
            {
                SyncRowLimit = 10,
                AdaptiveBatchSize = false,
                SyncUpdateInterval = TimeSpan.Zero,
                MaxRowsPerSecond = 0,
                GapCheckInterval = GapCheckInterval
            };
            var replicator = new GapSyncReplicator(remote, local, () => boundary, settings, message => { });
            replicator.RunIteration(); // back-filling 1-100: copies 91-100

            // After an outage the real-time task copied the newest rows (291-300) and skipped 101-290.
            remote.Add(InMemoryProcessDataStore.Ids(101, 300));
            local.Add(InMemoryProcessDataStore.Ids(291, 300));
            boundary = 301;
            replicator.AddGap(new BaseIdRange { BaseIdBegin = 101, BaseIdEnd = 290 });

            replicator.RunIteration();

            CollectionAssert.IsSubsetOf(InMemoryProcessDataStore.Ids(281, 290).ToList(), local.BaseIds);
            Assert.IsFalse(local.BaseIds.Contains(90), "the older gap was continued before the skipped range");
            Assert.AreEqual(280, replicator.NextBaseId);

            RunUntilIdle(replicator);

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
        }

        [TestMethod]
        public void AddGap_RealTimeRangeStartsWhereTheSkippedRangeEnds()
        {
            // Regression: the real-time row began at the boundary read when the gap was taken over, which the
            // real-time task had already moved past, leaving a gap between the rows of the Sync Table.
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore();
            long boundary = 101;
            var settings = new ReplicationSettings
            {
                SyncRowLimit = 10,
                AdaptiveBatchSize = false,
                SyncUpdateInterval = TimeSpan.Zero,
                MaxRowsPerSecond = 0,
                GapCheckInterval = GapCheckInterval
            };
            var replicator = new GapSyncReplicator(remote, local, () => boundary, settings, message => { });
            replicator.RunIteration(); // first iteration compares the databases

            // Real-time skipped 101-290 and started again at 291; by the time the sync task takes the range over it
            // has copied ten more rows, so the boundary it reads is already 301.
            remote.Add(InMemoryProcessDataStore.Ids(101, 300));
            local.Add(InMemoryProcessDataStore.Ids(291, 300));
            replicator.AddGap(new BaseIdRange { BaseIdBegin = 101, BaseIdEnd = 290 });
            boundary = 301;
            replicator.RunIteration();

            var realTime = replicator.GetRangesSnapshot().Single(r => r.Status == RangeStatus.RealTime);
            Assert.AreEqual(291, realTime.Range.BaseIdBegin);
        }

        [TestMethod]
        public void AddGap_IgnoresRangeAlreadyFoundByComparingDatabases()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(91, 100));
            var replicator = Create(remote, local, boundary: 91);
            replicator.AddGap(new BaseIdRange { BaseIdBegin = 1, BaseIdEnd = 90 }); // queued before the first refresh

            RunUntilIdle(replicator);
            replicator.AddGap(new BaseIdRange { BaseIdBegin = 50, BaseIdEnd = 60 }); // overlaps a synced range

            Assert.AreEqual(TaskState.NoOldData, replicator.State);
            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.AreEqual(1, replicator.GetRangesSnapshot().Count(r => r.Status == RangeStatus.Synced));
        }

        [TestMethod]
        public void RunIteration_WhileRealTimeNeedsTheLink_Waits()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 50));
            var local = new InMemoryProcessDataStore();
            var realTimeBusy = true;
            var settings = new ReplicationSettings { SyncRowLimit = 10, MaxRowsPerSecond = 0, GapCheckInterval = GapCheckInterval };
            var replicator = new GapSyncReplicator(remote, local, () => 51, settings, message => { }, () => realTimeBusy);

            var delay = replicator.RunIteration();

            Assert.IsTrue(replicator.IsYielding);
            Assert.IsTrue(delay > TimeSpan.Zero);
            Assert.AreEqual(0, local.BaseIds.Count);

            realTimeBusy = false;
            RunUntilIdle(replicator);

            Assert.IsFalse(replicator.IsYielding);
            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
        }

        [TestMethod]
        public void RunIteration_RetriesRefreshWhenRangesCannotBeRead()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 20));
            var local = new InMemoryProcessDataStore();
            var replicator = Create(remote, local, boundary: 21);
            remote.FailNext(nameof(InMemoryProcessDataStore.GetBaseIdRanges));

            var delay = replicator.RunIteration();

            Assert.AreEqual(TimeSpan.FromSeconds(1), delay);
            RunUntilIdle(replicator);
            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
        }
    }
}
