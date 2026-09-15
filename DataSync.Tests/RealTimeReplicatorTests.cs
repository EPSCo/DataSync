using System;
using System.Linq;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class RealTimeReplicatorTests
    {
        private static ReplicationSettings Settings(int rowLimit = 1000, int maxRowsPerSecond = 0)
        {
            return new ReplicationSettings
            {
                RealTimeRowLimit = rowLimit,
                RealTimeUpdateInterval = TimeSpan.FromSeconds(1),
                MaxRowsPerSecond = maxRowsPerSecond
            };
        }

        private static RealTimeReplicator Create(InMemoryProcessDataStore remote, InMemoryProcessDataStore local, ReplicationSettings settings)
        {
            return new RealTimeReplicator(remote, local, settings, message => { });
        }

        [TestMethod]
        public void Initialize_StartsAtNewestRemoteRowWhenLocalIsBehind()
        {
            var replicator = Create(new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100)),
                                    new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10)), Settings());

            replicator.Initialize();

            Assert.AreEqual(100, replicator.NextBaseId);
            Assert.AreEqual(10, replicator.LastLocalBaseId);
        }

        [TestMethod]
        public void Initialize_StartsAfterNewestLocalRowWhenUpToDate()
        {
            var replicator = Create(new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10)),
                                    new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10)), Settings());

            replicator.Initialize();

            Assert.AreEqual(11, replicator.NextBaseId);
        }

        [TestMethod]
        public void RunIteration_CopiesNewRowsAcrossGapsInRemoteBaseIds()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var replicator = Create(remote, local, Settings(rowLimit: 2));
            replicator.Initialize();
            remote.Add(4, 5, 1000, 1001, 1002);

            for (var i = 0; i < 5; i++)
            {
                replicator.RunIteration();
            }

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.AreEqual(1003, replicator.NextBaseId);
            Assert.AreEqual(1002, replicator.LastLocalBaseId);
        }

        [TestMethod]
        public void RunIteration_ReadsAgainImmediatelyWhileBatchesAreFull()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var replicator = Create(remote, local, Settings(rowLimit: 2));
            replicator.Initialize();
            remote.Add(2, 3, 4);

            var fullBatchDelay = replicator.RunIteration();
            var lastBatchDelay = replicator.RunIteration();

            Assert.IsTrue(fullBatchDelay <= TimeSpan.Zero, $"full batch delay {fullBatchDelay}");
            Assert.IsTrue(lastBatchDelay > TimeSpan.FromMilliseconds(500), $"caught-up delay {lastBatchDelay}");
        }

        [TestMethod]
        public void RunIteration_ThrottlesToMaxRowsPerSecond()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var replicator = Create(remote, local, Settings(rowLimit: 5, maxRowsPerSecond: 10));
            replicator.Initialize();
            remote.Add(InMemoryProcessDataStore.Ids(2, 20));

            var delay = replicator.RunIteration();

            Assert.IsTrue(delay > TimeSpan.FromMilliseconds(300), $"delay {delay}");
        }

        [TestMethod]
        public void RunIteration_BacksOffOnReadFailuresWithoutMovingPosition()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var replicator = Create(remote, local, Settings());
            replicator.Initialize();
            remote.Add(2, 3);
            remote.FailNext(nameof(InMemoryProcessDataStore.GetRowsAfter), 3);

            var delays = Enumerable.Range(0, 3).Select(i => replicator.RunIteration()).ToList();

            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, delays);
            Assert.AreEqual(3, replicator.FailureCount);
            Assert.AreEqual(2, replicator.NextBaseId);

            replicator.RunIteration();

            Assert.AreEqual(0, replicator.FailureCount);
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, local.BaseIds);
        }

        [TestMethod]
        public void RunIteration_RetriesSameRowsAfterSaveFailure()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var replicator = Create(remote, local, Settings());
            replicator.Initialize();
            remote.Add(2, 3);
            local.FailNext(nameof(InMemoryProcessDataStore.SaveBatch));

            replicator.RunIteration();
            Assert.AreEqual(2, replicator.NextBaseId);

            replicator.RunIteration();
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, local.BaseIds);
            Assert.AreEqual(4, replicator.NextBaseId);
        }
    }
}
