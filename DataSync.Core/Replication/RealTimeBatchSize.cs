using System;
using System.Collections.Generic;
using System.Linq;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Sizes real-time reads from how fast new records arrive. The newest remote BaseID is sampled as reads catch up,
    /// and a read carries <c>multiplier</c> polls' worth of records at the average rate of the last
    /// <see cref="SampleCount"/> samples: e.g. 2 new records per 1 s poll × 5 = 10 rows. While reads come back full
    /// (more rows are waiting than that) the size doubles with each read until they no longer do.
    /// </summary>
    public class RealTimeBatchSize : AdaptiveBatchSize
    {
        public const int SampleCount = 10;

        /// <summary>Samples closer together than this are not taken; their records count towards the next one.</summary>
        private static readonly TimeSpan MinSampleTime = TimeSpan.FromMilliseconds(200);

        private readonly int _multiplier;
        private readonly double _pollSeconds;
        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private long _newestBaseId = -1;
        private TimeSpan _newestAt;
        private int _catchUp;

        /// <param name="multiplier">Polls' worth of new records one read carries.</param>
        /// <param name="pollInterval">The wait between reads once caught up; at least 1 s is assumed.</param>
        public RealTimeBatchSize(int min, int max, int multiplier, TimeSpan pollInterval)
            : base(min, max)
        {
            _multiplier = Math.Max(1, multiplier);
            _pollSeconds = Math.Max(1, pollInterval.TotalSeconds);
        }

        /// <summary>New remote records per second over the recent samples; 0 before the first.</summary>
        public double RecordsPerSecond
        {
            get
            {
                var seconds = _samples.Sum(s => s.Seconds);
                return seconds > 0 ? _samples.Sum(s => s.Records) / seconds : 0;
            }
        }

        /// <summary>
        /// Records the newest remote BaseID known at <paramref name="at"/> (a monotonic clock). Pass only values that
        /// are the newest the remote had, e.g. from GetLastRecord or a read that was not full.
        /// </summary>
        public void OnNewestBaseId(long newestBaseId, TimeSpan at)
        {
            if (_newestBaseId < 0)
            {
                _newestBaseId = newestBaseId;
                _newestAt = at;
                return;
            }

            var elapsed = at - _newestAt;
            if (elapsed < MinSampleTime)
            {
                return;
            }

            _samples.Enqueue(new Sample { Records = Math.Max(0, newestBaseId - _newestBaseId), Seconds = elapsed.TotalSeconds });
            if (_samples.Count > SampleCount)
            {
                _samples.Dequeue();
            }
            _newestBaseId = Math.Max(_newestBaseId, newestBaseId);
            _newestAt = at;
            Resize();
        }

        /// <summary>Records a successful read of <paramref name="rows"/> out of <paramref name="requested"/>.</summary>
        public void OnRead(int requested, int rows)
        {
            CountRead();
            _catchUp = rows >= requested ? (int)Math.Min(int.MaxValue, Current * 2L) : 0;
            Resize();
        }

        public override void OnReadFailed(Exception ex)
        {
            base.OnReadFailed(ex);
            // Stay at the reduced size until a read succeeds.
            _catchUp = Current;
        }

        private void Resize()
        {
            var target = Math.Ceiling(RecordsPerSecond * _pollSeconds * _multiplier);
            SetCurrent(Math.Max((int)Math.Min(int.MaxValue, target), _catchUp));
        }

        private struct Sample
        {
            public long Records;
            public double Seconds;
        }
    }
}
