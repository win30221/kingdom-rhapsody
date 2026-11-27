using System;

namespace Realtime.Core
{
    public readonly struct SnapshotSample<T>
    {
        public long Tick { get; }
        public T Value { get; }
        public SnapshotSample(long tick, T value) { Tick = tick; Value = value; }
    }

    public readonly struct SnapshotBracket<T>
    {
        public T Before { get; }
        public T After { get; }
        public long BeforeTick { get; }
        public long AfterTick { get; }
        public bool HasAfter { get; }
        public double Fraction { get; }
        internal SnapshotBracket(T before, long beforeTick, T after, long afterTick, bool hasAfter, double tick)
        {
            Before = before; BeforeTick = beforeTick; After = after; AfterTick = afterTick; HasAfter = hasAfter;
            Fraction = hasAfter && afterTick > beforeTick ? Math.Max(0, Math.Min(1, (tick - beforeTick) / (afterTick - beforeTick))) : 0;
        }
    }

    public delegate bool SnapshotSelector<TSnapshot, TContext, TValue>(TSnapshot snapshot, TContext context, out TValue value);

    // Bounded, ordered snapshot references. The owner validates/clones DTOs and
    // supplies entity selection and continuity policy; this class owns no poses.
    public sealed class SnapshotBuffer<T>
    {
        private readonly SnapshotSample<T>[] samples;
        private int start;
        public int Count { get; private set; }
        public int Capacity => samples.Length;
        public SnapshotBuffer(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            samples = new SnapshotSample<T>[capacity];
        }
        public SnapshotSample<T> this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return samples[(start + index) % Capacity];
            }
        }
        // Duplicated/older ticks are ignored. Clear starts a new time domain.
        public bool Add(long tick, T value)
        {
            if (Count > 0 && tick <= this[Count - 1].Tick) return false;
            if (Count == Capacity)
            {
                samples[start] = new SnapshotSample<T>(tick, value);
                start = (start + 1) % Capacity;
            }
            else samples[(start + Count++) % Capacity] = new SnapshotSample<T>(tick, value);
            return true;
        }
        public void Clear() { Array.Clear(samples, 0, samples.Length); start = Count = 0; }

        // Missing entities are skipped. Before the first match, clamp to it;
        // after the last match, return it without an After (never extrapolate).
        // first lets the application exclude samples from a previous lifetime.
        public bool TryBracket<TContext, TValue>(double tick, TContext context, SnapshotSelector<T, TContext, TValue> select,
            out SnapshotBracket<TValue> bracket, int first = 0)
        {
            if (select == null) throw new ArgumentNullException(nameof(select));
            if (first < 0 || first > Count) throw new ArgumentOutOfRangeException(nameof(first));
            bracket = default;
            if (double.IsNaN(tick)) return false;
            bool found = false;
            TValue before = default;
            long beforeTick = 0;
            for (int i = first; i < Count; i++)
            {
                var sample = this[i];
                if (!select(sample.Value, context, out var value)) continue;
                if (!found || sample.Tick <= tick) { found = true; before = value; beforeTick = sample.Tick; }
                if (sample.Tick >= tick)
                {
                    bracket = new SnapshotBracket<TValue>(before, beforeTick, value, sample.Tick, true, tick);
                    return true;
                }
            }
            if (found) bracket = new SnapshotBracket<TValue>(before, beforeTick, default, 0, false, tick);
            return found;
        }
    }
}
