using System;
using System.Collections.Generic;
using System.Numerics;
using Realtime.Core;
using Realtime.Interpolation;
using Realtime.Replication;

namespace KingdomRhapsody
{
    internal sealed class BattleActorFrame
    {
        internal readonly string Key, CardId;
        internal readonly Vector2 Position, Heading;
        internal readonly int Seat, PoseTick, PoseLength;
        internal readonly BattlePose Pose;
        internal readonly float Health, Shield;
        internal readonly bool IsPrince, IsGuard;

        internal BattleActorFrame(BattleUnit unit)
        {
            Key = unit.Key;
            CardId = unit.Card.Id;
            Position = unit.Position;
            Heading = unit.Heading;
            Seat = unit.Seat;
            PoseTick = unit.PoseTick;
            PoseLength = unit.PoseLength;
            Pose = unit.Pose;
            Health = unit.Health / unit.Card.Health;
            Shield = unit.Shield / 400;
            IsPrince = unit.IsPrince;
            IsGuard = unit.GuardSide >= 0;
        }

        internal BattleActorFrame Copy() => (BattleActorFrame)MemberwiseClone();
    }

    internal sealed class BattlePresentation
    {
        private sealed class Snapshot
        {
            internal readonly BattleActorFrame[] Roster;
            internal readonly Dictionary<string, BattleActorFrame> ById;

            internal Snapshot(IReadOnlyList<BattleUnit> units)
            {
                Roster = new BattleActorFrame[units.Count];
                ById = new Dictionary<string, BattleActorFrame>(units.Count, StringComparer.Ordinal);
                for (int i = 0; i < units.Count; i++)
                {
                    var frame = new BattleActorFrame(units[i]);
                    Roster[i] = frame;
                    ById.Add(frame.Key, frame);
                }
            }
        }

        private readonly SnapshotBuffer<Snapshot> buffer = new SnapshotBuffer<Snapshot>(6);
        private readonly FixedTimeline timeline = new FixedTimeline(BattleMatch.TickRate, BattleMatch.SnapshotRate, 1);
        private readonly ReplicaStore<BattleActorFrame> replicas =
            new ReplicaStore<BattleActorFrame>(actor => actor.Key, actor => actor.CardId, actor => actor.Copy(),
                BattleMatch.MaxUnitsPerSide * 2);
        private static readonly SnapshotSelector<Snapshot, string, BattleActorFrame> select =
            (Snapshot snapshot, string key, out BattleActorFrame value) => snapshot.ById.TryGetValue(key, out value);

        internal double RenderTick { get; private set; }
        internal IReadOnlyDictionary<string, BattleActorFrame> Actors => replicas.Values;

        internal void Reset(BattleMatch match, double now)
        {
            buffer.Clear();
            replicas.Clear();
            timeline.Reset(match.Tick, now);
            Publish(match, now);
            Advance(now);
        }

        internal void Publish(BattleMatch match, double now)
        {
            if (buffer.Add(match.Tick, new Snapshot(match.Units)))
                timeline.Observe(match.Tick, now);
        }

        internal void Advance(double now, bool final = false)
        {
            timeline.Advance(now);
            RenderTick = final ? buffer[buffer.Count - 1].Tick : Math.Max(0, timeline.RenderTick);
            var visible = buffer[0];
            for (int i = buffer.Count - 1; i > 0; i--)
                if (buffer[i].Tick <= RenderTick)
                {
                    visible = buffer[i];
                    break;
                }
            replicas.Apply(1, visible.Tick, visible.Value.Roster);
        }

        internal Vector2 Position(BattleActorFrame actor, out BattleActorFrame pose)
        {
            pose = actor;
            if (!buffer.TryBracket(RenderTick, actor.Key, select, out SnapshotBracket<BattleActorFrame> bracket) ||
                !bracket.HasAfter)
                return actor.Position;

            if (bracket.After.PoseTick <= RenderTick && bracket.After.PoseTick > actor.PoseTick)
                pose = bracket.After;
            return Vector2.Lerp(bracket.Before.Position, bracket.After.Position, (float)bracket.Fraction);
        }
    }
}
