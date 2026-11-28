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
        internal readonly int Seat, PoseTick, PoseLength, LastHitTick;
        internal readonly BattlePose Pose;
        internal readonly BattleStatus Status;
        internal readonly float Health, Shield, WalkPhase;
        internal readonly bool IsPrince, IsGuard;

        internal BattleActorFrame(BattleUnit unit, BattleMatch match)
        {
            Key = unit.Key;
            CardId = unit.Card.Id;
            Position = unit.Position;
            Heading = unit.Heading;
            Seat = unit.Seat;
            PoseTick = unit.PoseTick;
            PoseLength = unit.PoseLength;
            LastHitTick = unit.LastHitTick;
            Pose = unit.Pose;
            Status = match.Status(unit);
            Health = unit.Health / unit.Card.Health;
            Shield = unit.Shield / 400;
            WalkPhase = unit.WalkPhase;
            IsPrince = unit.IsPrince;
            IsGuard = unit.GuardSide >= 0;
        }

        internal BattleActorFrame(WireBattleActor actor)
        {
            Key = actor.id;
            CardId = actor.card;
            Position = new Vector2(actor.x, actor.y);
            Heading = new Vector2(actor.hx, actor.hy);
            Seat = actor.s;
            PoseTick = actor.at;
            PoseLength = actor.len;
            LastHitTick = actor.hit;
            Pose = (BattlePose)actor.p;
            Status = (BattleStatus)actor.flags;
            Health = actor.hp;
            Shield = actor.sh;
            WalkPhase = actor.walk;
            IsPrince = CardId == BattleCatalog.Prince.Id;
            IsGuard = actor.guard;
        }

        internal BattleActorFrame Copy() => (BattleActorFrame)MemberwiseClone();
    }

    internal sealed class BattleSceneFrame
    {
        internal readonly int Tick, Territory;
        internal readonly float HealthA, HealthB;
        internal readonly bool HazardWarning;
        internal readonly BattleActorFrame[] Actors;
        internal readonly Dictionary<string, BattleActorFrame> ById;

        internal BattleSceneFrame(BattleMatch match)
        {
            Tick = match.Tick;
            Territory = match.TerritoryMask;
            HealthA = match.Prince(0).Health;
            HealthB = match.Prince(1).Health;
            HazardWarning = match.HazardWarning;
            Actors = new BattleActorFrame[match.Units.Count];
            for (int i = 0; i < Actors.Length; i++)
                Actors[i] = new BattleActorFrame(match.Units[i], match);
            ById = Index(Actors);
        }

        internal BattleSceneFrame(WireBattleState state)
        {
            Tick = state.tick;
            Territory = state.territory;
            HealthA = state.healthA;
            HealthB = state.healthB;
            HazardWarning = state.warning;
            Actors = Array.ConvertAll(state.actors, actor => new BattleActorFrame(actor));
            ById = Index(Actors);
        }

        private static Dictionary<string, BattleActorFrame> Index(BattleActorFrame[] actors)
        {
            var index = new Dictionary<string, BattleActorFrame>(actors.Length, StringComparer.Ordinal);
            foreach (BattleActorFrame actor in actors)
                index.Add(actor.Key, actor);
            return index;
        }

        internal float Health(int seat) => seat == 0 ? HealthA : HealthB;
    }

    internal sealed class BattlePresentation
    {
        private readonly SnapshotBuffer<BattleSceneFrame> buffer = new SnapshotBuffer<BattleSceneFrame>(12);
        private readonly FixedTimeline timeline = new FixedTimeline(BattleMatch.TickRate, BattleMatch.SnapshotRate, 1);
        private readonly ReplicaStore<BattleActorFrame> replicas =
            new ReplicaStore<BattleActorFrame>(actor => actor.Key, actor => actor.CardId, actor => actor.Copy(),
                BattleMatch.MaxUnitsPerSide * 2);
        private static readonly SnapshotSelector<BattleSceneFrame, string, BattleActorFrame> select =
            (BattleSceneFrame snapshot, string key, out BattleActorFrame value) => snapshot.ById.TryGetValue(key, out value);
        private double finishedAt = -1;

        internal double RenderTick { get; private set; }
        internal BattleSceneFrame Visible { get; private set; }
        internal IReadOnlyDictionary<string, BattleActorFrame> Actors => replicas.Values;
        internal bool EndAnimationComplete => finishedAt >= 0 &&
            RenderTick >= buffer[buffer.Count - 1].Tick + BattleMatch.DeathAnimationTicks + 2;

        internal void Reset(BattleMatch match, double now) => Reset(new BattleSceneFrame(match), now);

        internal void Reset(BattleSceneFrame frame, double now)
        {
            buffer.Clear();
            replicas.Clear();
            finishedAt = -1;
            RenderTick = 0;
            timeline.Reset(frame.Tick, now);
            Publish(frame, now);
            Advance(now);
        }

        internal void Publish(BattleMatch match, double now) => Publish(new BattleSceneFrame(match), now);

        internal void Publish(BattleSceneFrame frame, double now)
        {
            if (buffer.Add(frame.Tick, frame))
                timeline.Observe(frame.Tick, now);
        }

        internal void Finish(double now)
        {
            if (finishedAt < 0)
                finishedAt = now;
        }

        internal void Advance(double now)
        {
            timeline.Advance(now);
            double sample = timeline.RenderTick;
            if (finishedAt >= 0)
            {
                int last = buffer[buffer.Count - 1].Value.Tick;
                sample = last + (now - finishedAt - timeline.Metrics.BufferSeconds) * BattleMatch.TickRate;
            }

            RenderTick = Math.Max(RenderTick, Math.Max(0, sample));
            var visible = buffer[0];
            for (int i = buffer.Count - 1; i > 0; i--)
                if (buffer[i].Tick <= RenderTick + 1e-8)
                {
                    visible = buffer[i];
                    break;
                }
            Visible = visible.Value;
            replicas.Apply(1, visible.Tick, Visible.Actors);
        }

        internal Vector2 Position(BattleActorFrame actor, out BattleActorFrame pose) => Position(actor, out pose, out _);

        internal Vector2 Position(BattleActorFrame actor, out BattleActorFrame pose, out double walkPhase)
        {
            pose = actor;
            walkPhase = actor.WalkPhase;
            if (!buffer.TryBracket(RenderTick, actor.Key, select, out SnapshotBracket<BattleActorFrame> bracket) ||
                !bracket.HasAfter)
                return actor.Position;

            if (bracket.After.PoseTick <= RenderTick && bracket.After.PoseTick > actor.PoseTick)
                pose = bracket.After;
            if (pose.Pose == BattlePose.Walk && bracket.Before.PoseTick == bracket.After.PoseTick)
                walkPhase = bracket.Before.WalkPhase + (bracket.After.WalkPhase - bracket.Before.WalkPhase) * bracket.Fraction;
            return Vector2.Lerp(bracket.Before.Position, bracket.After.Position, (float)bracket.Fraction);
        }
    }
}
