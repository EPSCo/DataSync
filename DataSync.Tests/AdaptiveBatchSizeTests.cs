using System;
using DataSync.Core.Replication;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DataSync.Tests
{
    [TestClass]
    public class AdaptiveBatchSizeTests
    {
        private static readonly TimeSpan Target = TimeSpan.FromSeconds(3);

        private static AdaptiveBatchSize GrownToMax(int min = 20, int max = 1000)
        {
            var batch = new AdaptiveBatchSize(min, max, Target);
            for (var i = 0; i < 30; i++)
            {
                batch.OnRead(batch.Current, batch.Current, TimeSpan.FromMilliseconds(1));
            }
            Assert.AreEqual(max, batch.Current);
            return batch;
        }

        [TestMethod]
        public void StartsAtMinAndGrowsGraduallyOnFastFullReads()
        {
            var batch = new AdaptiveBatchSize(20, 1000, Target);
            Assert.AreEqual(20, batch.Current);

            batch.OnRead(20, 20, TimeSpan.FromMilliseconds(100));

            Assert.AreEqual(31, batch.Current);
            GrownToMax();
        }

        [TestMethod]
        public void DoesNotGrowOnShortReads()
        {
            var batch = new AdaptiveBatchSize(20, 1000, Target);

            batch.OnRead(20, 5, TimeSpan.FromMilliseconds(10));
            batch.OnRead(20, 0, TimeSpan.FromMilliseconds(10));

            Assert.AreEqual(20, batch.Current);
        }

        [TestMethod]
        public void SettlesWhereReadsTakeAboutTheTargetTime()
        {
            // A slow link: 0.5 s round trip plus 20 ms per row.
            Func<int, TimeSpan> readTime = rows => TimeSpan.FromSeconds(0.5 + 0.02 * rows);
            var batch = new AdaptiveBatchSize(20, 1000, Target);

            for (var i = 0; i < 50; i++)
            {
                batch.OnRead(batch.Current, batch.Current, readTime(batch.Current));
            }

            var seconds = readTime(batch.Current).TotalSeconds;
            Assert.IsTrue(seconds >= Target.TotalSeconds * 0.5 && seconds <= Target.TotalSeconds * 1.5,
                $"settled at {batch.Current} rows, {seconds} s per read");
        }

        [TestMethod]
        public void ShrinksToFitTargetAfterSlowRead()
        {
            var batch = GrownToMax();

            batch.OnRead(1000, 1000, TimeSpan.FromSeconds(30));

            Assert.AreEqual(100, batch.Current);
        }

        [TestMethod]
        public void TimeoutCutsToQuarterAndOtherFailuresHalve()
        {
            var batch = GrownToMax();

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
            var batch = GrownToMax();
            batch.OnReadFailed(new TimeoutException());

            for (var i = 0; i < 50; i++)
            {
                batch.OnRead(batch.Current, batch.Current, TimeSpan.FromMilliseconds(1));
            }
            Assert.AreEqual(750, batch.Current);

            for (var i = 0; i < 60; i++)
            {
                batch.OnRead(batch.Current, batch.Current, TimeSpan.FromMilliseconds(1));
            }
            Assert.AreEqual(1000, batch.Current);
        }

        [TestMethod]
        public void FixedWhenMinEqualsMax()
        {
            var batch = new AdaptiveBatchSize(1000, 1000, Target);

            batch.OnReadFailed(new TimeoutException());
            batch.OnRead(1000, 1000, TimeSpan.FromMinutes(1));

            Assert.AreEqual(1000, batch.Current);
            Assert.IsFalse(batch.IsAdaptive);
        }

        [TestMethod]
        public void Settings_CreateBatchSize_FixedWhenAdaptiveIsOff()
        {
            var settings = new ReplicationSettings { AdaptiveBatchSize = false, MinRowLimit = 20 };

            Assert.AreEqual(500, settings.CreateBatchSize(500).Current);

            settings.AdaptiveBatchSize = true;
            Assert.AreEqual(20, settings.CreateBatchSize(500).Current);
            Assert.AreEqual(10, settings.CreateBatchSize(10).Current);
        }
    }
}
