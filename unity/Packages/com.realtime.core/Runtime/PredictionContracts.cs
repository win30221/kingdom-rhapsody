using System;

namespace Realtime.Core
{
    public interface IPredictionModel<TState, TInput> where TState : class
    {
        TState CreateState();
        void Copy(TState source, TState destination);
        void Step(TState state, TInput input, double seconds, double serverTick);
    }
    public struct PredictionAck
    {
        public long Sequence;
        public double ServerTick, AppliedInputTicks;
        public bool ResetHistory;
    }
    public struct PredictionMetrics
    {
        public long CommittedSteps, PreviewSteps, ReplayedSteps;
        public double CommittedTick, DroppedSeconds;
    }
    public interface IPrediction<TState, TInput> where TState : class
    {
        string Mode { get; }
        bool Enabled { get; }
        TState State { get; }
        PredictionMetrics Metrics { get; }
        void SentInput(long sequence, double now);
        void Reconcile(TState authority, PredictionAck ack, double now);
        void Advance(double now, TInput input);
    }
    public sealed class AuthorityPrediction<TState, TInput> : IPrediction<TState, TInput> where TState : class
    {
        private readonly IPredictionModel<TState, TInput> model;
        public string Mode => "authority";
        public bool Enabled => false;
        public TState State { get; }
        public PredictionMetrics Metrics { get; private set; }
        public AuthorityPrediction(IPredictionModel<TState, TInput> model)
        { this.model = model ?? throw new ArgumentNullException(nameof(model)); State = model.CreateState(); }
        public void SentInput(long sequence, double now) { }
        public void Reconcile(TState authority, PredictionAck ack, double now)
        { model.Copy(authority, State); Metrics = new PredictionMetrics { CommittedTick = ack.ServerTick }; }
        public void Advance(double now, TInput input) { }
    }
    public interface ICorrectionPolicy
    {
        string Mode { get; }
        bool Smooths { get; }
        bool PreserveOffset(double errorDistance, bool forceSnap);
        double Decay(double seconds);
    }
    public sealed class SnapCorrection : ICorrectionPolicy
    {
        public string Mode => "snap";
        public bool Smooths => false;
        public bool PreserveOffset(double errorDistance, bool forceSnap) => false;
        public double Decay(double seconds) => 0;
    }
}
