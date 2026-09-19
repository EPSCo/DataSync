using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class AdaptiveBatchSizeTests
    {
        private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

        private static TimeSpan Seconds(double seconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        private static RealTimeBatchSize RealTimeGrownToMax()
        {
            var batch = new RealTimeBatchSize(20, 1000, 5, OneSecond);
            while (batch.Current < 1000)
            {
                batch.OnRead(batch.Current, batch.Current);
            }
            return batch;
        }

        /// <summary>A fast link: larger reads always copy more per second.</summary>
        private static readonly Func<int, double> FastLink = rows => 0.1 + 0.001 * rows;

        /// <summary>Full reads, each taking <paramref name="seconds"/> for its size; returns the size after each.</summary>
        private static List<int> Feed(SyncBatchSize batch, Func<int, double> seconds, int reads)
        {
            var sizes = new List<int>();
            for (var i = 0; i < reads; i++)
            {
                batch.OnRead(batch.Current, batch.Current, Seconds(seconds(batch.Current)));
                sizes.Add(batch.Current);
            }
            return sizes;
        }

        private static int Changes(List<int> sizes)
        {
            return Enumerable.Range(1, sizes.Count - 1).Count(i => sizes[i] != sizes[i - 1]);
        }

        [TestMethod]
        public void RealTime_CarriesMultiplierTimesAverageNewRecordsPerPoll()
        {
            var batch = new RealTimeBatchSize(1, 1000, 5, OneSecond);
            long newest = 100;
            batch.OnNewestBaseId(newest, Seconds(0));

            for (var i = 1; i <= 10; i++)
            {
                newest += i % 2 == 0 ? 3 : 1; // 2 per second on average
                batch.OnNewestBaseId(newest, Seconds(i));
            }

            Assert.AreEqual(2, batch.RecordsPerSecond, 1e-9);
            Assert.AreEqual(10, batch.Current);
        }

        [TestMethod]
        public void RealTime_AveragesTheLastTenSamples()
        {
            var batch = new RealTimeBatchSize(1, 1000, 3, OneSecond);
            long newest = 0;
            batch.OnNewestBaseId(newest, Seconds(0));

            for (var i = 1; i <= 10; i++)
            {
                newest += 20;
                batch.OnNewestBaseId(newest, Seconds(i));
            }
            Assert.AreEqual(60, batch.Current);

            for (var i = 11; i <= 20; i++)
            {
                newest += 2;
                batch.OnNewestBaseId(newest, Seconds(i));
            }
            Assert.AreEqual(6, batch.Current);
        }

        [TestMethod]
        public void RealTime_MeasuresPerSecondAcrossLongWaitsAndIgnoresSamplesTooCloseTogether()
        {
            var batch = new RealTimeBatchSize(1, 1000, 5, OneSecond);
            batch.OnNewestBaseId(0, Seconds(0));

            batch.OnNewestBaseId(20, Seconds(10)); // e.g. after retry delays: still 2 per second
            Assert.AreEqual(10, batch.Current);

            batch.OnNewestBaseId(25, Seconds(10.05));
            Assert.AreEqual(10, batch.Current);

            batch.OnNewestBaseId(25, Seconds(11)); // the 5 records count here: 25 in 11 s
            Assert.AreEqual(12, batch.Current);
        }

        [TestMethod]
        public void RealTime_StaysAtMinWhileNothingArrives()
        {
            var batch = new RealTimeBatchSize(5, 1000, 5, OneSecond);

            for (var i = 0; i < 5; i++)
            {
                batch.OnNewestBaseId(7, Seconds(i));
                batch.OnRead(5, 0);
            }

            Assert.AreEqual(5, batch.Current);
        }

        [TestMethod]
        public void RealTime_DoublesWhileReadsAreFullThenReturnsToArrivalRate()
        {
            var batch = new RealTimeBatchSize(5, 1000, 5, OneSecond);
            batch.OnNewestBaseId(0, Seconds(0));
            batch.OnNewestBaseId(2, Seconds(1));
            Assert.AreEqual(10, batch.Current);

            batch.OnRead(10, 10);
            Assert.AreEqual(20, batch.Current);
            batch.OnRead(20, 20);
            Assert.AreEqual(40, batch.Current);

            batch.OnRead(40, 7);
            Assert.AreEqual(10, batch.Current);
        }

        [TestMethod]
        public void TimeoutCutsToQuarterAndOtherFailuresHalve()
        {
            var batch = RealTimeGrownToMax();

            batch.OnReadFailed(new InvalidOperationException("wrapper", new TimeoutException()));
            Assert.AreEqual(250, batch.Current);

            batch.OnReadFailed(new InvalidOperationException("connection lost"));
            Assert.AreEqual(125, batch.Current);

            for (var i = 0; i < 20; i++)
            {
                batch.OnReadFailed(new TimeoutException());
            }
            Assert.AreEqual(20, batch.Current);
        }

        [TestMethod]
        public void AfterTimeout_StaysBelowTheSizeThatTimedOutForAWhile()
        {
            var batch = RealTimeGrownToMax();
            batch.OnReadFailed(new TimeoutException());

            for (var i = 0; i < 50; i++)
            {
                batch.OnRead(batch.Current, batch.Current);
            }
            Assert.AreEqual(750, batch.Current);

            for (var i = 0; i < 50; i++)
            {
                batch.OnRead(batch.Current, batch.Current);
            }
            Assert.AreEqual(1000, batch.Current);
        }

        [TestMethod]
        public void Sync_Steps()
        {
            CollectionAssert.AreEqual(new[] { 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200, 300, 400, 500 },
                                      SyncBatchSize.BuildSteps(5, 500));
            CollectionAssert.AreEqual(new[] { 7, 10, 20, 25 }, SyncBatchSize.BuildSteps(7, 25));
            CollectionAssert.AreEqual(new[] { 1000 }, SyncBatchSize.BuildSteps(1000, 1000));
        }

        [TestMethod]
        public void Sync_ClimbsToTheLimitWhileLargerReadsCopyMorePerSecond()
        {
            var batch = new SyncBatchSize(5, 1000);
            Assert.AreEqual(5, batch.Current);

            // One second per read whatever its size: larger reads always copy more per second.
            Feed(batch, rows => 1, SyncBatchSize.ReadsPerMeasurement);
            Assert.AreEqual(10, batch.Current);

            Feed(batch, rows => 1, 400);
            Assert.AreEqual(1000, batch.Current);
        }

        [TestMethod]
        public void Sync_SettlesAroundTheStepWithTheMostRecordsPerSecond()
        {
            // Reads above 40 rows get much slower: 40 rows/read gives the most records per second.
            Func<int, double> link = rows => 0.5 + 0.01 * rows + (rows > 40 ? 0.05 * (rows - 40) : 0);
            var batch = new SyncBatchSize(5, 1000);

            Feed(batch, link, 300);

            for (var i = 0; i < 100; i++)
            {
                Feed(batch, link, 1);
                Assert.IsTrue(batch.Current >= 30 && batch.Current <= 50, "batch size " + batch.Current);
            }
        }

        [TestMethod]
        public void Sync_HoldsOneSizeMostOfTheTimeDespiteNoisyReadTimes()
        {
            var random = new Random(1);
            Func<int, double> link = rows => (0.5 + 0.01 * rows + (rows > 40 ? 0.05 * (rows - 40) : 0)) * (0.9 + 0.2 * random.NextDouble());
            var batch = new SyncBatchSize(5, 1000);
            Feed(batch, link, 300);

            var changes = 0;
            var atBest = 0;
            for (var i = 0; i < 1000; i++)
            {
                var before = batch.Current;
                Feed(batch, link, 1);
                changes += batch.Current != before ? 1 : 0;
                atBest += batch.Current == 40 ? 1 : 0;
            }

            // Tries (two changes each) that do not help come further and further apart.
            Assert.IsTrue(changes <= 20, changes + " size changes in 1000 reads");
            Assert.IsTrue(atBest >= 800, atBest + " of 1000 reads at 40 rows");
        }

        [TestMethod]
        public void Sync_RarelyChangesSizeOnAJitteryNetwork()
        {
            // Read times vary by ±40% and one read in 20 takes three times as long, as on a busy WAN link.
            var random = new Random(7);
            Func<int, double> link = rows =>
            {
                var jitter = 0.6 + 0.8 * random.NextDouble();
                var spike = random.NextDouble() < 0.05 ? 3 : 1;
                return (0.5 + 0.01 * rows + (rows > 40 ? 0.05 * (rows - 40) : 0)) * jitter * spike;
            };
            var batch = new SyncBatchSize(5, 1000);
            Feed(batch, link, 500);

            var changes = 0;
            var atBest = 0;
            const int reads = 5000; // about 80 minutes of reads near the best size
            for (var i = 0; i < reads; i++)
            {
                var before = batch.Current;
                Feed(batch, link, 1);
                changes += batch.Current != before ? 1 : 0;
                atBest += batch.Current >= 30 && batch.Current <= 50 ? 1 : 0;
            }

            Assert.IsTrue(changes <= 40 && atBest >= reads * 0.9, changes + " size changes, " + atBest + " of " + reads + " reads at 30-50 rows");
        }

        [TestMethod]
        public void Sync_StepsDownWhenReadsTakeLonger()
        {
            var batch = new SyncBatchSize(5, 1000);
            Feed(batch, rows => 1, 400);
            Assert.AreEqual(1000, batch.Current);

            // The link slows down: large reads now take much longer; about 100 rows per read copies the most per second.
            Func<int, double> slow = rows => 0.2 + rows * (double)rows / 20000;
            Feed(batch, slow, SyncBatchSize.DefaultJudgeMeasurements - 1);
            Assert.AreEqual(1000, batch.Current, "slow measurements are not acted on until they last");
            Feed(batch, slow, 1);
            Assert.AreEqual(900, batch.Current, "reads slow enough to end each measurement step down once the drop lasts");

            Feed(batch, slow, 300);
            Assert.AreEqual(100, batch.Current);
            for (var i = 0; i < 60; i++)
            {
                Feed(batch, slow, 1);
                Assert.IsTrue(batch.Current >= 90 && batch.Current <= 200, "batch size " + batch.Current);
            }
        }

        [TestMethod]
        public void Sync_TimeoutCutsToAStepAndStaysBelowTheSizeThatTimedOut()
        {
            var batch = new SyncBatchSize(5, 1000);
            Feed(batch, rows => 1, 400);

            batch.OnReadFailed(new TimeoutException());
            Assert.AreEqual(200, batch.Current);

            // For the 100 reads after the timeout the size stays below three quarters of the size that timed out.
            for (var i = 0; i < 99; i++)
            {
                Feed(batch, rows => 1, 1);
                Assert.IsTrue(batch.Current <= 700, "batch size " + batch.Current);
            }
            Assert.IsTrue(batch.Current > 200, "grows back after holding: " + batch.Current);

            Feed(batch, rows => 1, 400);
            Assert.AreEqual(1000, batch.Current);
        }

        [TestMethod]
        public void Sync_JudgesATriedSizeOverTheConfiguredMeasurements()
        {
            var measurement = SyncBatchSize.ReadsPerMeasurement; // 1 s reads: 5 reads and 5 s
            var quick = new SyncBatchSize(5, 1000, judgeMeasurements: 1);
            var steady = new ReplicationSettings { MinRowLimit = 5, SyncRowLimit = 1000, SyncBatchMeasurements = 4 }.CreateSyncBatchSize();

            Feed(quick, rows => 1, measurement);
            Feed(steady, rows => 1, measurement);
            Assert.AreEqual(10, quick.Current, "the first measurement starts trying larger sizes");
            Assert.AreEqual(10, steady.Current);

            Feed(quick, rows => 1, measurement);
            Feed(steady, rows => 1, 3 * measurement);
            Assert.AreEqual(20, quick.Current);
            Assert.AreEqual(10, steady.Current, "still judging 10 rows after 3 of 4 measurements");

            Feed(steady, rows => 1, measurement);
            Assert.AreEqual(20, steady.Current);
        }

        [TestMethod]
        public void Sync_IgnoresShortWindowsForThroughput()
        {
            var batch = new SyncBatchSize(5, 1000);

            for (var i = 0; i < 10; i++)
            {
                batch.OnRead(5, 2, Seconds(0.01));
            }

            Assert.AreEqual(5, batch.Current);
        }

        [TestMethod]
        public void FixedWhenMinEqualsMax()
        {
            var sync = new SyncBatchSize(1000, 1000);
            sync.OnReadFailed(new TimeoutException());
            sync.OnRead(1000, 1000, TimeSpan.FromMinutes(1));

            var realTime = new RealTimeBatchSize(1000, 1000, 5, OneSecond);
            realTime.OnReadFailed(new TimeoutException());
            realTime.OnNewestBaseId(0, Seconds(0));
            realTime.OnNewestBaseId(1, Seconds(10));
            realTime.OnRead(1000, 1);

            Assert.AreEqual(1000, sync.Current);
            Assert.AreEqual(1000, realTime.Current);
            Assert.IsFalse(sync.IsAdaptive);
            Assert.IsFalse(realTime.IsAdaptive);
        }

        [TestMethod]
        public void Settings_CreateBatchSizes_FixedWhenAdaptiveIsOff()
        {
            var settings = new ReplicationSettings { AdaptiveBatchSize = false, MinRowLimit = 5, RealTimeRowLimit = 500, SyncRowLimit = 300 };

            Assert.AreEqual(500, settings.CreateRealTimeBatchSize().Current);
            Assert.AreEqual(300, settings.CreateSyncBatchSize().Current);

            settings.AdaptiveBatchSize = true;
            Assert.AreEqual(5, settings.CreateRealTimeBatchSize().Current);
            Assert.AreEqual(5, settings.CreateSyncBatchSize().Current);

            settings.SyncRowLimit = 3;
            Assert.AreEqual(3, settings.CreateSyncBatchSize().Current);
        }

        // Multiplier mode: the read size follows multiplier × average speed, rounded to the nearest step.

        private static void FeedFixed(SyncBatchSize batch, int rows, double seconds, int reads)
        {
            for (var i = 0; i < reads; i++)
            {
                batch.OnRead(rows, rows, Seconds(seconds));
            }
        }

        [TestMethod]
        public void Multiplier_SizesToTheNearestStep()
        {
            // Speed 100 rows/s (100 rows per 1 s read), multiplier 2 → 200, an exact step.
            var batch = new SyncBatchSize(5, 1000, 3, 2, 10);
            FeedFixed(batch, 100, 1, 3);
            Assert.AreEqual(200, batch.Current);
        }

        [TestMethod]
        public void Multiplier_RoundsToTheNearestStep()
        {
            // Speed 10 rows/s, multiplier 1.2 → 12, which is nearer 10 than 20.
            var batch = new SyncBatchSize(5, 1000, 3, 1.2, 10);
            FeedFixed(batch, 10, 1, 3);
            Assert.AreEqual(10, batch.Current);
        }

        [TestMethod]
        public void Multiplier_FollowsSpeedChanges()
        {
            var batch = new SyncBatchSize(5, 100000, 3, 1, 10);
            FeedFixed(batch, 100, 1, 5); // speed 100 → step 100
            Assert.AreEqual(100, batch.Current);

            FeedFixed(batch, 500, 1, 15); // window of 10 s of read time fills with the faster reads → speed 500
            Assert.AreEqual(500, batch.Current);
        }

        [TestMethod]
        public void Multiplier_StaysWithinMax()
        {
            var batch = new SyncBatchSize(5, 300, 3, 10, 10);
            FeedFixed(batch, 100, 0.1, 5); // speed 1000 → 10000, clamped to Max 300
            Assert.AreEqual(300, batch.Current);
        }

        [TestMethod]
        public void Multiplier_AFailureStillShrinksTheSize()
        {
            var batch = new SyncBatchSize(5, 1000, 3, 2, 10);
            FeedFixed(batch, 100, 1, 3);
            Assert.AreEqual(200, batch.Current);

            batch.OnReadFailed(new TimeoutException());
            Assert.IsTrue(batch.Current < 200);
        }

        [TestMethod]
        public void Multiplier_ZeroKeepsTheStepProbingBehaviour()
        {
            var batch = new SyncBatchSize(5, 1000, 3, 0, 10);
            var sizes = Feed(batch, FastLink, 60);
            Assert.IsTrue(Changes(sizes) > 0, "expected step probing to change the size");
        }

        [TestMethod]
        public void MultiplierTest_WritesResultsToCsv()
        {
            var directory = Path.Combine(Path.GetTempPath(), "DataSyncTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var batch = new SyncBatchSize(5, 1000, 3, 0, 10);
                long rows = 0;
                var experiment = new SyncMultiplierExperiment(batch, () => Interlocked.Increment(ref rows) * 100,
                                                              new double[] { 1, 2 }, TimeSpan.FromSeconds(1),
                                                              null, directory);
                experiment.Start();
                Thread.Sleep(2600); // two 1-second phases
                experiment.Stop();

                var file = new DirectoryInfo(directory).GetFiles("BatchMultiplierTest_*.csv");
                Assert.AreEqual(1, file.Length);
                var lines = File.ReadAllLines(file[0].FullName);
                Assert.AreEqual(3, lines.Length); // header + one line per multiplier
                StringAssert.StartsWith(lines[0], "Start (UTC),Multiplier,Minutes,Rows,RowsPerSecond");
                StringAssert.Matches(lines[1], new System.Text.RegularExpressions.Regex(@"^[0-9-]+ [0-9:]+,1,"));
                StringAssert.Matches(lines[2], new System.Text.RegularExpressions.Regex(@"^[0-9-]+ [0-9:]+,2,"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
