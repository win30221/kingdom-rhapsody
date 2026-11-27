using System;
using System.Collections.Generic;

namespace Realtime.Core
{
    public struct TimelineMetrics
    {
        public double BufferSeconds, ArrivalIntervalSeconds, JitterSeconds, UnderrunSeconds;
        public int GapSamples, Underruns, Resyncs;
    }
    public interface IPresentationTimeline
    {
        string Mode { get; }
        bool Interpolates { get; }
        double RenderTick { get; }
        TimelineMetrics Metrics { get; }
        void Reset(long tick, double now);
        void Observe(long tick, double now);
        void Advance(double now);
    }
    public sealed class LatestSnapshotTimeline : IPresentationTimeline
    {
        public string Mode => "latest";
        public bool Interpolates => false;
        public double RenderTick { get; private set; } = double.NegativeInfinity;
        public TimelineMetrics Metrics => default;
        public void Reset(long tick, double now) { RenderTick = tick; }
        public void Observe(long tick, double now) { RenderTick = Math.Max(RenderTick, tick); }
        public void Advance(double now) { }
    }
    public sealed class TimelineCatalog
    {
        private readonly Dictionary<string, Func<int, int, IPresentationTimeline>> factories =
            new Dictionary<string, Func<int, int, IPresentationTimeline>>(StringComparer.Ordinal);
        public TimelineCatalog() { Register("latest", (tick, snapshot) => new LatestSnapshotTimeline()); }
        public IEnumerable<string> Modes => factories.Keys;
        public void Register(string id, Func<int, int, IPresentationTimeline> factory)
        {
            if (string.IsNullOrWhiteSpace(id) || factory == null) throw new ArgumentException("Invalid module registration");
            if (factories.ContainsKey(id)) throw new InvalidOperationException("Duplicate timeline module: " + id);
            factories.Add(id, factory);
        }
        public bool Contains(string id) => id != null && factories.ContainsKey(id);
        public IPresentationTimeline Create(string id, int tickRate, int snapshotRate)
        {
            if (!factories.TryGetValue(id, out var factory)) throw new InvalidOperationException("Timeline module is not installed: " + id);
            var timeline = factory(tickRate, snapshotRate);
            if (timeline == null || timeline.Mode != id) throw new InvalidOperationException("Timeline module identity mismatch: " + id);
            return timeline;
        }
    }
}
