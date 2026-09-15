using System;
using System.Collections.Generic;
using System.Linq;
using DataSync.Core.Models;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class RealTimeReplicatorTests
    {
        // Most tests cover the forward read; the skip-ahead tests turn PrioritizeLatestData on.
        private static ReplicationSettings Settings(int rowLimit = 1000, int maxRowsPerSecond = 0, bool prioritizeLatest = false)
        {
            return new ReplicationSettings
            {
                RealTimeRowLimit = rowLimit,
                RealTimeUpdateInterval = TimeSpan.FromSeconds(1),
                MaxRowsPerSecond = maxRowsPerSecond,
                PrioritizeLatestData = prioritizeLatest
            };
        }

        private static ReplicationSettings LatestFirst(int rowLimit)
        {
            var settings = Settings(rowLimit: rowLimit, prioritizeLatest: true);
            settings.AdaptiveBatchSize = false;
            return settings;
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
        public void RunIteration_PrioritizeLatest_RetriesAtLeastEveryTenSeconds()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var latestFirst = Create(remote, new InMemoryProcessDataStore(new long[] { 1 }), LatestFirst(rowLimit: 10));
            var oldestFirst = Create(remote, new InMemoryProcessDataStore(new long[] { 1 }), Settings());
            latestFirst.Initialize();
            oldestFirst.Initialize();
            remote.FailNext(nameof(InMemoryProcessDataStore.GetRowsAfter), 14); // the link is down

            var latestFirstDelays = Enumerable.Range(0, 7).Select(i => latestFirst.RunIteration().TotalSeconds).ToList();
            var oldestFirstDelays = Enumerable.Range(0, 7).Select(i => oldestFirst.RunIteration().TotalSeconds).ToList();

            CollectionAssert.AreEqual(new double[] { 1, 2, 4, 8, 10, 10, 10 }, latestFirstDelays);
            CollectionAssert.AreEqual(new double[] { 1, 2, 4, 8, 16, 32, 60 }, oldestFirstDelays);
        }

        [TestMethod]
        public void RunIteration_ReportsRecoveryAfterFailures()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var messages = new List<string>();
            var replicator = new RealTimeReplicator(remote, local, Settings(), messages.Add);
            replicator.Initialize();
            remote.Add(2);
            remote.FailNext(nameof(InMemoryProcessDataStore.GetRowsAfter), 2);

            for (var i = 0; i < 3; i++)
            {
                replicator.RunIteration();
            }

            Assert.AreEqual(2, messages.Count(m => m.StartsWith("Error in GetRealTimeDataFromRemoteDatabase")));
            Assert.IsTrue(messages.Any(m => m.StartsWith("RealTime recovered after 2 failed attempts")), string.Join("\n", messages));
        }

        [TestMethod]
        public void RunIteration_AdaptiveBatchSize_SettlesBelowReadsThatTimeOut()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var replicator = Create(remote, local, Settings(rowLimit: 1000));
            replicator.Initialize();
            remote.Add(InMemoryProcessDataStore.Ids(2, 3001));
            remote.ReadTimeoutAboveRows = 100;

            var iterations = 0;
            while (local.BaseIds.Count < remote.BaseIds.Count && iterations++ < 200)
            {
                replicator.RunIteration();
            }

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.IsTrue(replicator.BatchSize <= 100, $"batch size {replicator.BatchSize}");
            Assert.AreEqual(3000, replicator.RowsCopied);
        }

        [TestMethod]
        public void RunIteration_FixedBatchSize_AlwaysReadsRowLimit()
        {
            var remote = new InMemoryProcessDataStore(new long[] { 1 });
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var settings = Settings(rowLimit: 100);
            settings.AdaptiveBatchSize = false;
            var replicator = Create(remote, local, settings);
            replicator.Initialize();
            remote.Add(InMemoryProcessDataStore.Ids(2, 1001));

            replicator.RunIteration();

            Assert.AreEqual(101, local.BaseIds.Count);
            Assert.AreEqual(100, replicator.BatchSize);
        }

        [TestMethod]
        public void RunIteration_PrioritizeLatest_CopiesNewestRowsFirstAndHandsOverSkippedRange()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var replicator = Create(remote, local, LatestFirst(rowLimit: 10));
            BaseIdRange skipped = null;
            replicator.RangeSkipped += (s, range) => skipped = range;
            replicator.Initialize();
            remote.Add(InMemoryProcessDataStore.Ids(4, 100)); // written while the link was down

            replicator.RunIteration();

            CollectionAssert.AreEqual(InMemoryProcessDataStore.Ids(1, 3).Concat(InMemoryProcessDataStore.Ids(91, 100)).ToList(), local.BaseIds);
            Assert.AreEqual(4, skipped.BaseIdBegin);
            Assert.AreEqual(90, skipped.BaseIdEnd);
            Assert.AreEqual(101, replicator.NextBaseId);
            Assert.AreEqual(100, replicator.LastLocalBaseId);
            Assert.IsTrue(replicator.IsBehind);

            replicator.RunIteration(); // nothing newer: caught up

            Assert.IsFalse(replicator.IsBehind);
        }

        [TestMethod]
        public void RunIteration_PrioritizeLatest_DoesNotSkipWhenOneReadCarriesEverything()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var replicator = Create(remote, local, LatestFirst(rowLimit: 10));
            var skipped = false;
            replicator.RangeSkipped += (s, range) => skipped = true;
            replicator.Initialize();
            remote.Add(InMemoryProcessDataStore.Ids(4, 13));

            replicator.RunIteration();

            CollectionAssert.AreEqual(remote.BaseIds, local.BaseIds);
            Assert.IsFalse(skipped);
        }

        [TestMethod]
        public void RunIteration_PrioritizeLatest_SkipsAheadWhenReadsRecover()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 3));
            var replicator = Create(remote, local, LatestFirst(rowLimit: 10));
            BaseIdRange skipped = null;
            replicator.RangeSkipped += (s, range) => skipped = range;
            replicator.Initialize();
            replicator.RunIteration(); // caught up

            remote.Add(InMemoryProcessDataStore.Ids(4, 100));
            remote.FailNext(nameof(InMemoryProcessDataStore.GetRowsAfter)); // the link drops
            replicator.RunIteration();
            replicator.RunIteration();

            CollectionAssert.AreEqual(InMemoryProcessDataStore.Ids(1, 3).Concat(InMemoryProcessDataStore.Ids(91, 100)).ToList(), local.BaseIds);
            Assert.AreEqual(4, skipped.BaseIdBegin);
            Assert.AreEqual(90, skipped.BaseIdEnd);
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
