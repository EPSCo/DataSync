using System;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Threading;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// How many rows one remote read asks for, between a minimum and a maximum. The real-time and sync tasks size their
    /// reads differently (<see cref="RealTimeBatchSize"/>, <see cref="SyncBatchSize"/>); both shrink after failed reads.
    /// With min equal to max the size stays fixed. Not thread-safe except for <see cref="Current"/>, which may be read
    /// from any thread.
    /// </summary>
    public abstract class AdaptiveBatchSize
    {
        /// <summary>Successful reads after a timeout before the size may grow back towards the size that timed out.</summary>
        private const int CeilingReads = 100;

        private int _current;
        private int _ceiling;
        private int _ceilingReadsLeft;

        /// <summary>Starts at <paramref name="min"/>, so the first read on a slow link is small.</summary>
        protected AdaptiveBatchSize(int min, int max)
        {
            Max = Math.Max(1, max);
            Min = Math.Max(1, Math.Min(min, Max));
            _current = Min;
            _ceiling = Max;
        }

        public int Min { get; }

        public int Max { get; }

        public int Current => Volatile.Read(ref _current);

        public bool IsAdaptive => Min < Max;

        /// <summary>
        /// Records a failed read. A timeout cuts the size to a quarter and keeps it below three quarters of the size
        /// that timed out for a while; other failures (e.g. a dropped connection mid-transfer) halve it.
        /// </summary>
        public virtual void OnReadFailed(Exception ex)
        {
            if (IsTimeout(ex))
            {
                _ceiling = Math.Max(Min, Current * 3 / 4);
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

        /// <summary>Counts a successful read towards lifting the limit a timeout set.</summary>
        protected void CountRead()
        {
            if (_ceilingReadsLeft > 0 && --_ceilingReadsLeft == 0)
            {
                _ceiling = Max;
            }
        }

        /// <summary>Sets the size, kept between Min and Max and below the limit after a timeout, then <see cref="Snap"/>ped.</summary>
        protected void SetCurrent(int value)
        {
            Volatile.Write(ref _current, Snap(Math.Max(Min, Math.Min(Math.Min(Max, _ceiling), value))));
        }

        /// <summary>Rounds an allowed size to one the task uses; at least Min.</summary>
        protected virtual int Snap(int value)
        {
            return value;
        }
    }
}
