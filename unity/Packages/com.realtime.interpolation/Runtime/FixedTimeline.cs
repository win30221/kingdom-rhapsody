using System;
using Realtime.Core;

namespace Realtime.Interpolation
{
    public sealed class FixedTimeline : IPresentationTimeline, IClockAwareTimeline
    {
        public IServerClock SourceClock { get; set; }
        private readonly double tickRate, delay;
        private long latestTick;
        private double latestArrival;
        public string Mode => "fixed";
        public bool Interpolates => true;
        public double RenderTick { get; private set; } = double.NegativeInfinity;
        public TimelineMetrics Metrics => new TimelineMetrics { BufferSeconds = delay };
        public FixedTimeline(int tickRate, int snapshotRate, double intervals = 2)
        {
            if (tickRate <= 0 || snapshotRate <= 0 || intervals < 1 || double.IsNaN(intervals) || double.IsInfinity(intervals)) throw new ArgumentOutOfRangeException();
            this.tickRate = tickRate; delay = intervals / snapshotRate;
        }
        public void Reset(long tick, double now)
        { if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now)); latestTick = tick; latestArrival = now; RenderTick = tick - delay * tickRate; }
        public void Observe(long tick, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || tick <= latestTick || now < latestArrival) return;
            latestTick = tick; latestArrival = now; Advance(now);
        }
        public void Advance(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now)) return; double target=SourceClock==null?latestTick+(now-latestArrival-delay)*tickRate:SourceClock.Tick(now)-delay*tickRate; RenderTick = Math.Max(RenderTick, Math.Min(latestTick, target)); }
    }
}
