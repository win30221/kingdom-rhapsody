using System;
using System.Collections.Generic;

namespace Realtime.Core
{
    // Tracks application request IDs using one monotonic clock. No wire format,
    // transport, or server-tick policy is imposed on the caller.
    public sealed class ReplyTracker
    {
        private readonly Dictionary<long, double> pending = new Dictionary<long, double>();
        private readonly List<long> removed = new List<long>();
        private readonly int capacity;
        private readonly double lifetime;
        public int Count => pending.Count;
        public ReplyTracker(int capacity = 128, double lifetime = 5)
        {
            if (capacity < 1 || capacity > 4096 || !Finite(lifetime) || lifetime <= 0)
                throw new ArgumentOutOfRangeException();
            this.capacity = capacity; this.lifetime = lifetime;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public bool Contains(long id) => pending.ContainsKey(id);
        public bool TryStart(long id, double now)
        {
            if (id <= 0 || !Finite(now)) return false;
            Expire(now);
            if (pending.Count >= capacity || pending.ContainsKey(id)) return false;
            pending.Add(id, now); return true;
        }
        public bool TryComplete(long id, double now, out double seconds)
        {
            seconds = 0;
            if (!Finite(now) || !pending.TryGetValue(id, out double sent)) return false;
            pending.Remove(id);
            double elapsed = now - sent;
            if (elapsed < 0 || elapsed > lifetime) return false;
            seconds = elapsed; return true;
        }
        public void Expire(double now)
        {
            if (!Finite(now)) return;
            removed.Clear();
            foreach (var item in pending) if (now - item.Value > lifetime) removed.Add(item.Key);
            foreach (long id in removed) pending.Remove(id);
            removed.Clear();
        }
        // Cumulative acknowledgements retire earlier requests without inventing
        // latency samples for IDs that were not explicitly observed in the reply.
        public void DiscardThrough(long id)
        {
            removed.Clear();
            foreach (var item in pending) if (item.Key <= id) removed.Add(item.Key);
            foreach (long key in removed) pending.Remove(key);
            removed.Clear();
        }
        public void Clear() { pending.Clear(); removed.Clear(); }
    }

    public sealed class ProbeScheduler
    {
        private readonly ReplyTracker replies;
        private readonly double interval;
        private long sequence;
        private double next;
        public ProbeScheduler(double interval = .5, double lifetime = 5, int capacity = 16)
        {
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval = interval; replies = new ReplyTracker(capacity, lifetime);
        }
        public bool Contains(long id) => replies.Contains(id);
        public bool TryBegin(double now, out long id)
        {
            id = 0;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < next || sequence >= 9007199254740991L) return false;
            if (!replies.TryStart(sequence + 1, now)) return false;
            id = ++sequence; next = now + interval; return true;
        }
        public bool TryComplete(long id, double now, out double seconds) => replies.TryComplete(id, now, out seconds);
        public void Clear() { replies.Clear(); sequence = 0; next = 0; }
    }
}
