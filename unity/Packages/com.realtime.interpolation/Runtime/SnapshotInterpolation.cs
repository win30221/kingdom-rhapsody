using System;
using Realtime.Core;

namespace Realtime.Interpolation
{
    // One monotonic presentation clock for remote actors and projectiles.
    // All times are local monotonic seconds; server ticks never set local time.
    public sealed class SnapshotInterpolation : IPresentationTimeline, IClockAwareTimeline
    {
        public IServerClock SourceClock { get; set; }
        public string Mode => "adaptive";
        public bool Interpolates => true;
        public TimelineMetrics Metrics => new TimelineMetrics { BufferSeconds = Delay, ArrivalIntervalSeconds = ArrivalInterval,
            JitterSeconds = Jitter, GapSamples = GapSamples, Underruns = Underruns, UnderrunSeconds = UnderrunSeconds, Resyncs = Resyncs };
        private const int Capacity = 64;
        private readonly double tickRate, interval;
        private readonly double[] gaps = new double[Capacity], times = new double[Capacity];
        private int cursor, count;
        private long latestTick;
        private double latestArrival, lastUpdate;
        private bool initialized, seenSnapshot, starving;
        public double RenderTick { get; private set; } = double.NegativeInfinity;
        public double Delay { get; private set; }
        public double TargetDelay { get; private set; }
        public double ArrivalInterval { get; private set; }
        public double Jitter { get; private set; }
        public int GapSamples { get; private set; }
        public int Underruns { get; private set; }
        public double UnderrunSeconds { get; private set; }
        public int Resyncs { get; private set; }

        public SnapshotInterpolation(int tickRate, int snapshotRate)
        {
            if (tickRate <= 0 || snapshotRate <= 0) throw new ArgumentOutOfRangeException();
            this.tickRate = tickRate; interval = 1.0 / snapshotRate;
            Delay = TargetDelay = 2 * interval;
        }

        public void Reset(long tick, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
            initialized = true; seenSnapshot = starving = false;
            latestTick = tick; latestArrival = lastUpdate = now;
            Delay = TargetDelay = 2 * interval;
            RenderTick = tick - Delay * tickRate;
            cursor = count = GapSamples = Underruns = Resyncs = 0;
            ArrivalInterval = Jitter = UnderrunSeconds = 0;
            Array.Clear(gaps, 0, Capacity); Array.Clear(times, 0, Capacity);
        }

        public void Observe(long tick, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return;
            if (!initialized) { Reset(tick, now); return; }
            if (tick <= latestTick || now < latestArrival) return;
            Advance(now);
            double gap = now - latestArrival;
            if (seenSnapshot)
            {
                double variation = Math.Abs(gap - (tick - latestTick) / tickRate);
                ArrivalInterval = GapSamples == 0 ? gap : ArrivalInterval + .1 * (gap - ArrivalInterval);
                Jitter = GapSamples == 0 ? variation : Jitter + .1 * (variation - Jitter);
                gaps[cursor] = gap; times[cursor] = now;
                cursor = (cursor + 1) % Capacity; count = Math.Min(count + 1, Capacity); GapSamples++;
            }
            seenSnapshot = true;
            latestTick = tick; latestArrival = now; starving = false;
            UpdateTarget(now);
            // A long suspension/outage must not leave the clock minutes behind.
            // Ordinary jitter/loss never takes this recovery path.
            if (tick - RenderTick > 6 * interval * tickRate)
            {
                Delay = TargetDelay;
                RenderTick = Math.Max(RenderTick, tick - Delay * tickRate);
                Resyncs++;
            }
        }

        private void UpdateTarget(double now)
        {
            double gap = interval;
            for (int i = 0; i < count; i++)
                if (now - times[i] <= 4) gap = Math.Max(gap, gaps[i]);
            // One observed delivery gap plus a quarter-interval safety margin.
            // Retain the startup buffer until eight actual intervals are known.
            TargetDelay = Math.Max(1.25 * interval, Math.Min(4 * interval, gap + .25 * interval));
            if (GapSamples < 8) TargetDelay = Math.Max(2 * interval, TargetDelay);
        }

        public void Advance(double now)
        {
            if (!initialized || double.IsNaN(now) || double.IsInfinity(now) || now <= lastUpdate) return;
            double dt = now - lastUpdate;
            lastUpdate = now;
            UpdateTarget(now);
            // Grow quickly, recover cautiously (20 ms per second). Changing the
            // desired delay never rewinds or teleports a normal presentation.
            double change = TargetDelay - Delay;
            Delay += Math.Max(-.02 * dt, Math.Min(dt, change));
            double target = SourceClock == null ? latestTick + (now - latestArrival - Delay) * tickRate : SourceClock.Tick(now) - Delay * tickRate;
            double nominal = RenderTick + dt * tickRate;
            double speed = 1 + Math.Max(-.1, Math.Min(.1, (target - nominal) / (tickRate * .5)));
            double next = RenderTick + dt * tickRate * speed;
            if (seenSnapshot && next > latestTick + 1e-9)
            {
                if (!starving) Underruns++;
                starving = true;
                UnderrunSeconds += (next - latestTick) / (tickRate * speed);
            }
            RenderTick = Math.Max(RenderTick, Math.Min(latestTick, next));
        }
    }
}
