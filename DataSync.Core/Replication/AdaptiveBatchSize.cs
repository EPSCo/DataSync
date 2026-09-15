using System;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Threading;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Adjusts how many rows one remote read asks for, so a read takes about the target time on the current network:
    /// grows gradually after fast full reads, shrinks quickly after slow or failed ones. With min equal to max the size
    /// stays fixed. Not thread-safe except for <see cref="Current"/>, which may be read from any thread.
    /// </summary>
    public class AdaptiveBatchSize
    {
        private const double Smoothing = 0.3;
        private const double GrowBelow = 0.7;
        private const double ShrinkAbove = 1.5;

        /// <summary>Full reads after a timeout before the size may grow back towards the size that timed out.</summary>
        private const int CeilingReads = 100;

        private readonly int _min;
        private readonly int _max;
        private readonly double _targetSeconds;
        private double _secondsPerRow;
        private bool _hasSample;
        private int _current;
        private int _ceiling;
        private int _ceilingReadsLeft;

        /// <summary>Starts at <paramref name="min"/>, so the first read on a slow link is small.</summary>
        public AdaptiveBatchSize(int min, int max, TimeSpan target)
        {
            _max = Math.Max(1, max);
            _min = Math.Max(1, Math.Min(min, _max));
            _targetSeconds = Math.Max(0.001, target.TotalSeconds);
            _current = _min;
            _ceiling = _max;
        }

        public int Current => Volatile.Read(ref _current);

        public bool IsAdaptive => _min < _max;

        /// <summary>
        /// Records a successful read of <paramref name="covered"/> rows (or BaseIDs) out of <paramref name="requested"/>.
        /// Only full reads can grow the size: a short read says nothing about what the link could carry.
        /// </summary>
        public void OnRead(int requested, int covered, TimeSpan elapsed)
        {
            if (covered <= 0)
            {
                return;
            }

            var seconds = elapsed.TotalSeconds;
            var sample = seconds / covered;
            _secondsPerRow = _hasSample ? Smoothing * sample + (1 - Smoothing) * _secondsPerRow : sample;
            _hasSample = true;

            if (seconds > _targetSeconds * ShrinkAbove)
            {
                // React to this read rather than the average, so one very slow read is not repeated several times.
                SetCurrent(Math.Min(Current / 2, Ideal(sample)));
                return;
            }

            if (covered < requested)
            {
                return;
            }

            if (_ceilingReadsLeft > 0 && --_ceilingReadsLeft == 0)
            {
                _ceiling = _max;
            }

            if (seconds < _targetSeconds * GrowBelow)
            {
                var grown = Math.Min(Ideal(_secondsPerRow), Current + Current / 2 + 1);
                SetCurrent(Math.Max(Current, Math.Min(grown, _ceiling)));
            }
        }

        /// <summary>
        /// Records a failed read. A timeout cuts the size to a quarter and keeps it below three quarters of the size
        /// that timed out for a while; other failures (e.g. a dropped connection mid-transfer) halve it.
        /// </summary>
        public void OnReadFailed(Exception ex)
        {
            if (IsTimeout(ex))
            {
                _ceiling = Math.Max(_min, Current * 3 / 4);
                _ceilingReadsLeft = CeilingReads;
                SetCurrent(Current / 4);
            }
            else
            {
                SetCurrent(Current / 2);
            }
        }

        public static bool IsTimeout(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is TimeoutException)
                {
                    return true;
                }

                // -2: SqlClient command timeout; 258: WAIT_TIMEOUT from the network layer.
                if ((e as SqlException)?.Number == -2 || (e as Win32Exception)?.NativeErrorCode == 258)
                {
                    return true;
                }
            }
            return false;
        }

        private int Ideal(double secondsPerRow)
        {
            return secondsPerRow <= 0 ? _max : (int)Math.Min(_max, _targetSeconds / secondsPerRow);
        }

        private void SetCurrent(int value)
        {
            Volatile.Write(ref _current, Math.Max(_min, Math.Min(_max, value)));
        }
    }
}
