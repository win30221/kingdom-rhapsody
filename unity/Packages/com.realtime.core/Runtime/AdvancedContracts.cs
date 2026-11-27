using System;
using System.Collections.Generic;

namespace Realtime.Core
{
    public interface IServerClock
    {
        string Mode { get; }
        void Reset();
        void Observe(double serverTick, double now, double roundTripSeconds);
        double Tick(double now);
    }
    public interface IClockAwareTimeline { IServerClock SourceClock { get; set; } }
    public sealed class SnapshotClock : IServerClock
    {
        private double tick;
        public string Mode => "snapshot";
        public void Reset() { tick = 0; }
        public void Observe(double serverTick, double now, double roundTripSeconds) { if (!double.IsNaN(serverTick) && !double.IsInfinity(serverTick)) tick = Math.Max(tick, serverTick); }
        public double Tick(double now) => tick;
    }
    public interface IReplicaStore<T> where T : class
    {
        IReadOnlyDictionary<string, T> Values { get; }
        IReadOnlyList<string> Added { get; }
        IReadOnlyList<string> Updated { get; }
        IReadOnlyList<string> Removed { get; }
        bool Apply(uint generation, long tick, T[] values);
        void Clear();
    }
    public enum SpeculativeState { Pending, Confirmed, Rejected, Expired }
    public interface IActionPresentation
    {
        bool Begin(long id, double now);
        bool WasPresented(long id);
        bool Resolve(long id, bool accepted);
        void Advance(double now);
        int Pending { get; }
        long Confirmed { get; }
        long Rejected { get; }
        long Expired { get; }
        void Clear();
    }
    public enum FrameDecodeResult { Applied, Stale, MissingBaseline, Invalid }
    public interface IFrameDecoder
    {
        long Ack { get; }
        FrameDecodeResult Decode(long id, long baseline, string data, out string payload);
        void Reset();
    }
}
