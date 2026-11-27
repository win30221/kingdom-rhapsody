using System.Collections.Generic;

namespace Realtime.Core
{
    public interface IPlaybackClock
    {
        bool Playing { get; }
        bool Ended { get; }
        double Tick { get; }
        double Speed { get; set; }
        long FirstTick { get; }
        long LastTick { get; }
        int FrameIndex { get; }
        void Load(IReadOnlyList<long> ticks, int tickRate);
        void Play();
        void Pause();
        void Seek(double tick);
        void Advance(double unscaledSeconds);
        void Clear();
    }
}
