using System;
using System.Collections.Generic;
using System.Collections;

namespace Realtime.Core
{
    // The application supplies the simulator. History owns no positions, heroes,
    // collisions or damage and can be used with another game's input/state types.
    public sealed class InputHistory<TInput>
    {
        public struct Frame { public double Start, End; public TInput Input; }
        private struct Sent { public long Sequence; public double Time; }
        private sealed class Ring<T> : IReadOnlyList<T>
        {
            private readonly T[] values;
            private int start;
            public int Count { get; private set; }
            public Ring(int capacity) { values = new T[capacity]; }
            public T this[int index]
            {
                get { if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index)); return values[(start + index) % values.Length]; }
                set { values[(start + index) % values.Length] = value; }
            }
            public void Add(T value)
            {
                if (Count == values.Length) { values[start] = value; start = (start + 1) % values.Length; }
                else values[(start + Count++) % values.Length] = value;
            }
            public void Truncate(int count)
            { for (int i = count; i < Count; i++) this[i] = default; Count = count; }
            public void RemoveFirst()
            { if (Count == 0) return; values[start] = default; start = (start + 1) % values.Length; Count--; }
            public void Clear() { Array.Clear(values, 0, values.Length); start = Count = 0; }
            public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private readonly Ring<Frame> frames = new Ring<Frame>(241);
        private readonly Ring<Sent> sent = new Ring<Sent>(120);
        public IReadOnlyList<Frame> Frames => frames;
        public void Clear() { frames.Clear(); sent.Clear(); }
        public void Record(double start, double end, TInput input)
        {
            if (end < start || double.IsNaN(start) || double.IsNaN(end) || double.IsInfinity(start) || double.IsInfinity(end)) throw new ArgumentException("Invalid input interval");
            frames.Add(new Frame { Start = start, End = end, Input = input });
            Trim(end);
        }
        public void Trim(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
            while (frames.Count > 0 && frames[0].End < now - 1) frames.RemoveFirst();
            int kept = 0;
            for (int i = 0; i < frames.Count; i++) if (frames[i].End >= now - 1) { if (kept != i) frames[kept] = frames[i]; kept++; }
            frames.Truncate(kept);
            if (kept > 240) frames.RemoveFirst();
        }
        public void SentInput(long sequence, double time)
        { if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time)); sent.Add(new Sent { Sequence = sequence, Time = time }); }
        public bool TryAcknowledgedTime(long sequence, out double time)
        { for (int i = 0; i < sent.Count; i++) { var item = sent[i]; if (item.Sequence == sequence) { time = item.Time; return true; } } time = 0; return false; }
        public void DiscardBefore(long sequence)
        { int kept = 0; for (int i = 0; i < sent.Count; i++) if (sent[i].Sequence >= sequence) sent[kept++] = sent[i]; sent.Truncate(kept); }
        public TInput HeldAt(double time, TInput fallback)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (frame.End > time + 1e-10 && frame.Start <= time + 1e-10) return frame.Input;
            }
            return fallback;
        }
        // Cursor is local to a nondecreasing-time query sequence without mutation.
        public TInput HeldAt(double time, TInput fallback, ref int cursor)
        {
            while (cursor < frames.Count && frames[cursor].End <= time + 1e-10) cursor++;
            for (int i = cursor; i < frames.Count; i++) {
                var frame = frames[i];
                if (frame.End > time + 1e-10 && frame.Start <= time + 1e-10) return frame.Input;
            }
            return fallback;
        }
    }
}
