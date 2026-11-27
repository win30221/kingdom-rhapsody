using System;

namespace Realtime.Core
{
    public enum MessageDirection { Send, Receive }
    public struct ConditioningSettings
    {
        public double DelaySeconds, JitterSeconds, LossProbability;
        public uint Seed;
        public void Validate()
        {
            if (!Valid(DelaySeconds, 1) || !Valid(JitterSeconds, 1) || !Valid(LossProbability, 1))
                throw new ArgumentOutOfRangeException("Conditioning settings must be finite; delay/jitter 0..1 seconds, loss 0..1");
        }
        private static bool Valid(double value, double max) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= max;
    }
    public struct ConditioningSummary
    {
        public int PendingSend, PendingReceive;
        public long DroppedSend, DroppedReceive;
        public double PausedUntil;
    }
    // Application-message conditioning, not TCP/QUIC packet emulation.
    // The adapter chooses which messages may be discarded. Queues are bounded.
    public interface IMessageConditioner
    {
        bool Enqueue(MessageDirection direction, string payload, bool discardable, double observedAt);
        bool TryTake(MessageDirection direction, double now, out string payload);
        void Pause(double now, double seconds);
        void ClearPending(); // connection replacement; an active pause survives reconnect
        ConditioningSummary Summary { get; }
    }
}
