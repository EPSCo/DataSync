using System;
using System.Collections.Generic;
using System.Linq;

namespace DataSync.Core.Replication
{
    /// <summary>
    /// Sizes sync reads for the most records copied per second, without chasing network jitter. The size moves along
    /// steps (5, 10, 20, 30 ... 100, 200 ... 1000, 2000 ...). Throughput is measured as BaseIDs covered per second of read
    /// and save time; one measurement takes at least <see cref="ReadsPerMeasurement"/> reads and
    /// <see cref="MeasurementTime"/> (or <see cref="LongMeasurementTime"/> of slow reads). Decisions are judged over
    /// several measurements (the SyncBatchMeasurements setting):
    /// <list type="bullet">
    /// <item>The size is held. After <see cref="HoldMeasurements"/> measurements the next step up or down (alternately)
    /// is tried, and kept only when it copies clearly more, in which case the following step that way is tried too.
    /// Each try that does not help doubles the wait before the next, up to <see cref="MaxHoldMeasurements"/>.</item>
    /// <item>When throughput at the held size stays sharply lower (the link got slower), smaller steps are tried at once.</item>
    /// </list>
    /// </summary>
    public class SyncBatchSize : AdaptiveBatchSize
    {
        public const int ReadsPerMeasurement = 5;

        /// <summary>Measurements a tried step or a drop is judged over when not configured.</summary>
        public const int DefaultJudgeMeasurements = 3;

        /// <summary>Measurements at a settled size before the first try of a neighbouring step.</summary>
        public const int HoldMeasurements = 10;

        /// <summary>The longest wait between tries, reached by doubling after tries that do not help.</summary>
        public const int MaxHoldMeasurements = 80;

        public static readonly TimeSpan MeasurementTime = TimeSpan.FromSeconds(5);

        /// <summary>A measurement ends after this long even with fewer reads, so very slow reads are acted on.</summary>
        public static readonly TimeSpan LongMeasurementTime = TimeSpan.FromSeconds(30);

        /// <summary>A tried step is kept only when it copies at least this fraction more; smaller gains are noise.</summary>
        private const double Improvement = 0.10;

        /// <summary>Throughput this fraction below the held size's average means the link may have got slower.</summary>
        private const double Drop = 0.20;

        private const double Smoothing = 0.3;

        private readonly int[] _steps;
        private readonly int _judgeMeasurements;
        private readonly Totals _measurement = new Totals();
        private readonly Totals _probe = new Totals();
        private readonly Totals _drop = new Totals();
        private int _anchor;
        private double _anchorRate = -1;
        private int _holdLength = HoldMeasurements;
        private int _held = HoldMeasurements; // try larger sizes straight after the first measurement
        private int _nextProbe = 1;
        private int _probing; // 0 while holding, otherwise the direction of the step being tried

        /// <param name="judgeMeasurements">Measurements a tried step, or a drop at the held size, is judged over.</param>
        public SyncBatchSize(int min, int max, int judgeMeasurements = DefaultJudgeMeasurements)
            : base(min, max)
        {
            _steps = BuildSteps(Min, Max);
            _judgeMeasurements = Math.Max(1, judgeMeasurements);
        }

        /// <summary>BaseIDs per second at the held size; 0 before the first measurement.</summary>
        public double RecordsPerSecond => Math.Max(0, _anchorRate);

        /// <summary>The sizes used: <paramref name="min"/>, 5, 10, 20, 30 ... 100, 200 ... in between, and <paramref name="max"/>.</summary>
        public static int[] BuildSteps(int min, int max)
        {
            var steps = new SortedSet<int> { min, max };
            for (long step = 5; step < max; step += step < 10 ? 5 : Decade(step))
            {
                if (step > min)
                {
                    steps.Add((int)step);
                }
            }
            return steps.ToArray();
        }

        /// <summary>
        /// Records a successful read and save of <paramref name="covered"/> BaseIDs out of <paramref name="requested"/>
        /// taking <paramref name="elapsed"/>.
        /// </summary>
        public void OnRead(int requested, int covered, TimeSpan elapsed)
        {
            if (covered <= 0)
            {
                return;
            }

            CountRead();
            if (!IsAdaptive || covered < requested)
            {
                // A range's last, shorter window: its fixed overhead would understate the rate.
                return;
            }

            _measurement.Add(covered, Math.Max(0.001, elapsed.TotalSeconds));
            var complete = _measurement.Count >= ReadsPerMeasurement && _measurement.Seconds >= MeasurementTime.TotalSeconds;
            if (!complete && _measurement.Seconds < LongMeasurementTime.TotalSeconds)
            {
                return;
            }

            var measuredCovered = _measurement.Covered;
            var measuredSeconds = _measurement.Seconds;
            _measurement.Reset();
            if (_probing != 0)
            {
                OnProbeMeasured(measuredCovered, measuredSeconds);
            }
            else
            {
                OnHeldMeasured(measuredCovered, measuredSeconds);
            }
        }

        public override void OnReadFailed(Exception ex)
        {
            base.OnReadFailed(ex);
            // Hold the smaller size and measure it afresh instead of comparing with a rate from before the failure.
            _measurement.Reset();
            _probe.Reset();
            _drop.Reset();
            _anchor = IndexAtOrBelow(Current);
            _anchorRate = -1;
            _probing = 0;
            _held = 0;
            _holdLength = HoldMeasurements;
            _nextProbe = 1;
        }

        protected override int Snap(int value)
        {
            return _steps[Math.Max(0, IndexAtOrBelow(value))];
        }

        private void OnHeldMeasured(long covered, double seconds)
        {
            _anchor = IndexAtOrBelow(Current);
            var rate = covered / seconds;
            if (_anchorRate >= 0 && rate < _anchorRate * (1 - Drop))
            {
                // A slower link or just a slow spell: act only when it lasts, and keep it out of the average meanwhile.
                _drop.Add(covered, seconds);
                if (_drop.Count >= _judgeMeasurements)
                {
                    _anchorRate = _drop.Rate;
                    _drop.Reset();
                    _holdLength = HoldMeasurements;
                    Probe(-1);
                }
                return;
            }

            _drop.Reset();
            _anchorRate = _anchorRate < 0 ? rate : Smoothing * rate + (1 - Smoothing) * _anchorRate;
            if (++_held >= _holdLength)
            {
                Probe(_nextProbe);
            }
        }

        private void OnProbeMeasured(long covered, double seconds)
        {
            _probe.Add(covered, seconds);
            if (_probe.Count < _judgeMeasurements)
            {
                return;
            }

            var rate = _probe.Rate;
            _probe.Reset();
            if (rate > _anchorRate * (1 + Improvement))
            {
                // Clearly better: keep it and try the next step the same way.
                _anchor = IndexAtOrBelow(Current);
                _anchorRate = rate;
                _holdLength = HoldMeasurements;
                Probe(_probing);
                return;
            }

            // Not clearly better: back to the held size; try the other way next time, after a longer wait.
            _nextProbe = -_probing;
            _probing = 0;
            _held = 0;
            _holdLength = Math.Min(_holdLength * 2, MaxHoldMeasurements);
            SetCurrent(_steps[_anchor]);
        }

        private void Probe(int direction)
        {
            _held = 0;
            var target = _anchor + direction;
            if (target >= 0 && target < _steps.Length)
            {
                SetCurrent(_steps[target]);
                if (IndexAtOrBelow(Current) != _anchor)
                {
                    _probing = direction;
                    return;
                }
            }

            // No step that way (or held down after a timeout): stay, and try the other way next time.
            _probing = 0;
            _nextProbe = -direction;
        }

        private int IndexAtOrBelow(int value)
        {
            var index = Array.BinarySearch(_steps, value);
            return index >= 0 ? index : ~index - 1;
        }

        /// <summary>10 below 100, 100 below 1000, and so on.</summary>
        private static long Decade(long step)
        {
            long decade = 10;
            while (step >= decade * 10)
            {
                decade *= 10;
            }
            return decade;
        }

        /// <summary>BaseIDs and seconds summed over reads or measurements.</summary>
        private sealed class Totals
        {
            public int Count;
            public long Covered;
            public double Seconds;

            public double Rate => Seconds > 0 ? Covered / Seconds : 0;

            public void Add(long covered, double seconds)
            {
                Count++;
                Covered += covered;
                Seconds += seconds;
            }

            public void Reset()
            {
                Count = 0;
                Covered = 0;
                Seconds = 0;
            }
        }
    }
}
