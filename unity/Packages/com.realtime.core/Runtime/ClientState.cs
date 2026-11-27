using System;

namespace Realtime.Core
{
    public sealed class SessionState
    {
        public string Credential { get; private set; }
        public string UserID { get; private set; }
        public int Revision { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(Credential);
        public bool Accept(string credential, string userID)
        {
            if (string.IsNullOrEmpty(credential) || string.IsNullOrEmpty(userID)) return false;
            Credential = credential; UserID = userID; Revision++; return true;
        }
        public void Clear() { Credential = UserID = null; Revision++; }
    }
    public sealed class ReconnectPolicy
    {
        private readonly double window, initialDelay, maximumDelay;
        public bool Armed { get; private set; }
        public bool Pending { get; private set; }
        public double Deadline { get; private set; }
        public double Next { get; private set; } = double.PositiveInfinity;
        public int Attempts { get; private set; }
        public ReconnectPolicy(double window = 30, double initialDelay = .5, double maximumDelay = 3)
        {
            if (window <= 0 || initialDelay <= 0 || maximumDelay < initialDelay) throw new ArgumentOutOfRangeException();
            this.window = window; this.initialDelay = initialDelay; this.maximumDelay = maximumDelay;
        }
        public void Stop() { Armed = Pending = false; Deadline = 0; Next = double.PositiveInfinity; Attempts = 0; }
        public void Connected() { Stop(); Armed = true; }
        public bool Fail(double now, bool retryable)
        {
            if (!Armed || !retryable) { Stop(); return false; }
            if (!Pending) { Pending = true; Deadline = now + window; }
            Next = now + Math.Min(maximumDelay, initialDelay * Math.Pow(2, Math.Min(Attempts, 30))); return true;
        }
        public string Poll(double now)
        {
            if (!Pending) return "none";
            if (now >= Deadline) { Stop(); return "expired"; }
            if (now < Next) return "none";
            Attempts++; Next = double.PositiveInfinity; return "retry";
        }
    }
}
