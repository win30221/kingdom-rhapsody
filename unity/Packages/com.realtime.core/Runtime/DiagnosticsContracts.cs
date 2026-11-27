namespace Realtime.Core
{
    public enum ClientMetric { FrameMS, ConnectionRttMS, InputReplyMS, CorrectionDistance, ClientStepMS, SnapshotIntervalMS }
    public struct MetricSummary
    {
        public int Count;
        public double Mean, Min, Median, P95, Max;
    }
    public struct TrafficSummary
    {
        public long SentBytes, ReceivedBytes, SentMessages, ReceivedMessages;
        public long SentBytesPerSecond, ReceivedBytesPerSecond;
    }
    // All observations and reads use the same monotonic seconds, on one owner thread.
    public interface IClientDiagnostics
    {
        bool Enabled { get; }
        void Reset();
        void Frame(double now);
        void Timing(ClientMetric metric, double value, double now);
        void Traffic(bool sent, int payloadBytes, double now);
        MetricSummary Read(ClientMetric metric, double now);
        TrafficSummary ReadTraffic(double now);
    }
    public sealed class NoClientDiagnostics : IClientDiagnostics
    {
        public bool Enabled => false;
        public void Reset() { }
        public void Frame(double now) { }
        public void Timing(ClientMetric metric, double value, double now) { }
        public void Traffic(bool sent, int payloadBytes, double now) { }
        public MetricSummary Read(ClientMetric metric, double now) => default;
        public TrafficSummary ReadTraffic(double now) => default;
    }
}
