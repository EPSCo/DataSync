using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataSync.Core.Models;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class ReplicationEngineTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private static ReplicationSettings FastSettings()
        {
            return new ReplicationSettings
            {
                RealTimeRowLimit = 50,
                RealTimeUpdateInterval = TimeSpan.FromMilliseconds(20),
                SyncRowLimit = 50,
                SyncUpdateInterval = TimeSpan.Zero,
                MaxRowsPerSecond = 0,
                GapCheckInterval = TimeSpan.FromMilliseconds(100),
                FailureLimit = 10
            };
        }

        private static bool WaitUntil(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < Timeout)
            {
                if (condition())
                {
                    return true;
                }
                Thread.Sleep(20);
            }
            return condition();
        }

        [TestMethod]
        public void Start_CopiesHistoryGapsAndNewRowsConcurrently()
        {
            // Remote has history with a BaseID gap (200-249 never existed); local stopped at 100.
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 199).Concat(InMemoryProcessDataStore.Ids(250, 500)));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 100));
            var engine = new ReplicationEngine(remote, local, FastSettings());

            engine.Start();
            try
            {
                for (var id = 501; id <= 600; id += 10)
                {
                    remote.Add(InMemoryProcessDataStore.Ids(id, id + 9)); // the remote keeps producing rows
                    Thread.Sleep(10);
                }

                Assert.IsTrue(WaitUntil(() => local.BaseIds.SequenceEqual(remote.BaseIds)),
                    $"local has {local.BaseIds.Count} of {remote.BaseIds.Count} rows");

                var status = engine.GetStatus();
                Assert.AreEqual(600, status.LastLocalBaseId);
                Assert.AreEqual(TaskState.Downloading, status.RealTimeState);
                Assert.IsFalse(status.FailureLimitExceeded);
            }
            finally
            {
                var stopwatch = Stopwatch.StartNew();
                engine.Stop();
                Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Stop took {stopwatch.Elapsed}");
            }

            var stopped = engine.GetStatus();
            Assert.AreEqual(TaskState.Stop, stopped.RealTimeState);
            Assert.AreEqual(TaskState.Stop, stopped.SyncState);
        }

        [TestMethod]
        public void PauseAndResumeSync_ResumesBackfill()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 2000));
            var local = new InMemoryProcessDataStore(new long[] { 1 });
            var settings = FastSettings();
            settings.SyncRowLimit = 10;
            settings.MaxRowsPerSecond = 2000;
            var engine = new ReplicationEngine(remote, local, settings);

            engine.Start();
            try
            {
                engine.PauseSync();
                Assert.IsTrue(WaitUntil(() => engine.GetStatus().SyncState == TaskState.Stop));
                var countWhilePaused = local.BaseIds.Count;
                Thread.Sleep(200);
                Assert.AreEqual(countWhilePaused, local.BaseIds.Count, "sync kept copying while paused");

                engine.ResumeSync();

                Assert.IsTrue(WaitUntil(() => local.BaseIds.SequenceEqual(remote.BaseIds)),
                    $"local has {local.BaseIds.Count} of {remote.BaseIds.Count} rows");
            }
            finally
            {
                engine.Stop();
            }
        }

        [TestMethod]
        public void GetStatus_ReportsFailureLimitExceeded()
        {
            var remote = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10));
            var local = new InMemoryProcessDataStore(InMemoryProcessDataStore.Ids(1, 10));
            var settings = FastSettings();
            settings.FailureLimit = 0;
            var engine = new ReplicationEngine(remote, local, settings);
            remote.FailNext(nameof(InMemoryProcessDataStore.GetRowsAfter), 100);

            engine.Start();
            try
            {
                Assert.IsTrue(WaitUntil(() => engine.GetStatus().FailureLimitExceeded));
            }
            finally
            {
                engine.Stop();
            }
        }
    }
}
