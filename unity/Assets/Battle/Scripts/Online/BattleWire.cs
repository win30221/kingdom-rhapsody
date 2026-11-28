using System;
using System.Collections.Generic;
using System.Numerics;

namespace KingdomRhapsody
{
    // Field names are shared by Unity's JsonUtility, the Go gateway and the .NET worker.
    #pragma warning disable CS0649 // Some envelope fields are populated by the peer serializer.
    [Serializable]
    internal sealed class BattlePacket
    {
        internal const int Version = 1;
        public string type, match, code, card;
        public int version, to, seat, seq, slot, result;
        public float x, y, dx, dy;
        public WireLoadout loadout, first, second;
        public WireBattleState state;
        public WireBattleEffect[] effects;
    }

    #pragma warning restore CS0649

    [Serializable]
    internal sealed class WireLoadout
    {
        public int left, right;
        public string[] deck;

        internal static WireLoadout From(BattleLoadout loadout)
        {
            var deck = new string[loadout.TroopIds.Count];
            for (int i = 0; i < deck.Length; i++)
                deck[i] = loadout.TroopIds[i];
            return new WireLoadout { left = (int)loadout.LeftGuard, right = (int)loadout.RightGuard, deck = deck };
        }

        internal BattleLoadout Validate()
        {
            if (left < 0 || left > 1 || right < 0 || right > 1 || deck == null || deck.Length != Roster.DeckSize)
                return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in deck)
                if (BattleCatalog.Find(id) == null || !seen.Add(id))
                    return null;
            return new BattleLoadout((RoyalGuard)left, (RoyalGuard)right, deck);
        }
    }

    [Serializable]
    internal sealed class WireBattleState
    {
        public int tick, winner, territory, cooldown;
        public float healthA, healthB;
        public double energy;
        public bool canSprint, warning;
        public string[] hand;
        public string nextCard;
        public WireBattleActor[] actors;

        internal static WireBattleState From(BattleMatch match, int seat, BattleSceneFrame scene)
        {
            var hand = new string[BattleMatch.HandSize];
            for (int i = 0; i < hand.Length; i++)
                hand[i] = match.Hand(seat, i).Id;
            return new WireBattleState
            {
                tick = scene.Tick, winner = match.Winner, territory = scene.Territory,
                cooldown = match.SprintTicksLeft(seat), healthA = scene.HealthA, healthB = scene.HealthB,
                energy = match.Energy(seat), canSprint = match.CanSprint(seat), warning = scene.HazardWarning,
                hand = hand, nextCard = match.NextCard(seat).Id, actors = Array.ConvertAll(scene.Actors, WireBattleActor.From)
            };
        }
    }

    [Serializable]
    internal sealed class WireBattleActor
    {
        public string id, card;
        public int s, p, at, len, hit, flags;
        public float x, y, hx, hy, hp, sh, walk;
        public bool guard;

        internal static WireBattleActor From(BattleActorFrame unit) => new WireBattleActor
        {
            id = unit.Key, card = unit.CardId, s = unit.Seat, p = (int)unit.Pose, at = unit.PoseTick,
            len = unit.PoseLength, hit = unit.LastHitTick, flags = (int)unit.Status,
            x = unit.Position.X, y = unit.Position.Y, hx = unit.Heading.X, hy = unit.Heading.Y,
            hp = unit.Health, sh = unit.Shield, walk = unit.WalkPhase, guard = unit.IsGuard
        };
    }

    [Serializable]
    internal sealed class WireBattleEffect
    {
        public int id, kind, skill, begin, end;
        public float x, y, tx, ty, radius;
        public string target;

        internal static WireBattleEffect From(BattleEffect effect) => new WireBattleEffect
        {
            id = effect.Id, kind = (int)effect.Kind, skill = (int)effect.Skill, begin = effect.Begin, end = effect.End,
            x = effect.Origin.X, y = effect.Origin.Y, tx = effect.Target.X, ty = effect.Target.Y,
            radius = effect.Radius, target = effect.TrackingKey
        };

        internal BattleEffect Read() => new BattleEffect(id, (EffectKind)kind, new Vector2(x, y), new Vector2(tx, ty),
            begin, end, radius, (Skill)skill, target);
    }
}
