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

        private static GapSyncReplicator Create(InMemoryProcessDataStore remote, InMemoryProcessDataStore local, long boundary, int rowLimit = 10)
        {
            var settings = new ReplicationSettings
            {
                SyncRowLimit = rowLimit,
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
            Assert.IsTrue(replicator.GetRangesSnapshot().All(r => r.Status == RangeStatus.Synced || r.Status == RangeStatus.RealTime));
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
