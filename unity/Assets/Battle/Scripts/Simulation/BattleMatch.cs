using System;
using System.Collections.Generic;
using System.Numerics;

namespace KingdomRhapsody
{
    public enum BattlePose { Idle, Walk, Sprint, Action, Bloom, ShieldBreak, ShieldRegrow, Awaken }
    internal enum PlayResult { Accepted, Inactive, CardChanged, Energy, Bounds, Territory, Occupied, UnitLimit }
    internal enum EffectKind { Point, Area, Projectile, Rift, Heal, Hazard, MeteorWarning }

    internal readonly struct ActionTiming
    {
        internal readonly float Length, ContactFraction;

        internal ActionTiming(int frames, float fps, int contactFrame)
        {
            Length = frames / fps;
            ContactFraction = contactFrame / (float)frames;
        }

        internal static ActionTiming Standard => new(12, 16, 6);
    }

    internal sealed class BattleUnit
    {
        internal readonly int Seat, GuardSide;
        internal readonly string Key;
        internal readonly BattleCard Card;
        internal readonly Vector2 Home;
        internal readonly float LaneX;
        internal readonly bool IsPrince;
        internal Vector2 Position, Heading;
        internal float Health, Shield, PatrolDistance;
        internal int NextAction, ActionContact, ActionEnd, PoseTick, PoseLength, StatusEnd;
        internal int SlowEnd, PoisonEnd, PoisonNext, StunEnd, StunImmuneEnd, ShieldReady, SuccessfulHits;
        internal bool Awakened;
        internal BattlePose Pose;
        internal BattleUnit LockedTarget;
        internal bool Alive => Health > 0;

        internal BattleUnit(int id, int seat, BattleCard card, Vector2 position, int guardSide)
        {
            Key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Seat = seat;
            Card = card;
            GuardSide = guardSide;
            IsPrince = card == BattleCatalog.Prince;
            Position = Home = position;
            LaneX = position.X;
            Heading = seat == 0 ? Vector2.UnitY : -Vector2.UnitY;
            Health = card.Health;
            Shield = card.Skill == Skill.Shield ? 400 : 0;
        }

        internal void SetPose(BattlePose pose, int tick, int length = 0)
        {
            if (Pose == pose && length == 0)
                return;
            Pose = pose;
            PoseTick = tick;
            PoseLength = length;
        }
    }

    internal readonly struct BattleEffect
    {
        internal readonly EffectKind Kind;
        internal readonly Vector2 Origin, Target;
        internal readonly int Begin, End;
        internal readonly float Radius;
        internal readonly Skill Skill;

        internal BattleEffect(EffectKind kind, Vector2 origin, Vector2 target,
            int begin, int end, float radius, Skill skill)
        {
            Kind = kind;
            Origin = origin;
            Target = target;
            Begin = begin;
            End = end;
            Radius = radius;
            Skill = skill;
        }
    }

    internal sealed class BattleMatch
    {
        internal const int TickRate = 30, SnapshotRate = 15;
        internal const int OpeningTicks = 2 * TickRate, DurationTicks = 180 * TickRate;
        internal const int HandSize = 4, MaxUnitsPerSide = 40;
        internal const double MaxEnergy = 10, StartEnergy = 5;
        internal const int Playing = -2, Draw = -1;
        private const float Detection = 7, SprintSpeed = 4.5f;
        private const int SprintLength = 3 * TickRate, SprintCooldown = 15 * TickRate;
        private const int HazardPeriod = (int)(Battlefield.HazardInterval * TickRate);

        // The four-slot hand and draw queue follow Tower Duel's deal / deploy rotation.
        private sealed class Side
        {
            internal readonly BattleCard[] Hand = new BattleCard[HandSize];
            internal readonly Queue<BattleCard> Queue = new Queue<BattleCard>(HandSize);
            internal readonly BattleUnit[] Guards = new BattleUnit[2];
            internal BattleUnit Prince;
            internal double Energy = StartEnergy;
            internal int SprintEnd, SprintReady;

            internal Side(IReadOnlyList<string> deck)
            {
                for (int i = 0; i < deck.Count; i++)
                {
                    BattleCard card = BattleCatalog.Find(deck[i]);
                    if (i < HandSize)
                        Hand[i] = card;
                    else
                        Queue.Enqueue(card);
                }
            }

            internal void Cycle(int slot)
            {
                Queue.Enqueue(Hand[slot]);
                Hand[slot] = Queue.Dequeue();
            }
        }

        private readonly struct Spell
        {
            internal readonly int Seat, Impact;
            internal readonly BattleCard Card;
            internal readonly Vector2 Point, Direction;

            internal Spell(int seat, BattleCard card, Vector2 point, Vector2 direction, int impact)
            {
                Seat = seat;
                Card = card;
                Point = point;
                Direction = direction;
                Impact = impact;
            }
        }

        internal static readonly string[] PracticeDeck =
        {
            "footman", "bow-recruit", "shieldman", "light-cavalry",
            "frost-mage", "hammer-guard", "crystal-armadillo", "meteorfall"
        };

        private readonly Side[] sides;
        private readonly Func<string, BattlePose, ActionTiming> getTiming;
        private readonly List<BattleUnit> units = new List<BattleUnit>();
        private readonly List<Spell> spells = new List<Spell>();
        private readonly List<BattleEffect> effects = new List<BattleEffect>();
        private readonly bool practiceOpponent;
        private int nextId, nextBotPlay, nextBotLane;

        internal int Tick { get; private set; }
        internal int Winner { get; private set; } = Playing;
        internal int Elapsed => Math.Max(0, Tick - OpeningTicks);
        internal bool Live => Tick >= OpeningTicks && Winner == Playing;
        internal int HazardTicksLeft => HazardPeriod - Elapsed % HazardPeriod;
        internal bool HazardWarning => Live && HazardTicksLeft <= Battlefield.HazardWarning * TickRate;
        internal IReadOnlyList<BattleUnit> Units => units;
        internal IReadOnlyList<BattleEffect> Effects => effects;

        internal BattleMatch(BattleLoadout loadout, Func<string, BattlePose, ActionTiming> getTiming = null,
            bool practiceOpponent = true)
        {
            this.getTiming = getTiming ?? ((_, _) => ActionTiming.Standard);
            this.practiceOpponent = practiceOpponent;
            sides = new[] { new Side(loadout.TroopIds), new Side(PracticeDeck) };
            for (int seat = 0; seat < sides.Length; seat++)
            {
                Side side = sides[seat];
                side.Prince = AddUnit(seat, BattleCatalog.Prince, Battlefield.Patrol(seat, 0));
                side.Guards[0] = AddUnit(seat, BattleCatalog.Guard(seat == 0 ? loadout.LeftGuard : RoyalGuard.Spearman),
                    Battlefield.GuardHome(seat, 0), 0);
                side.Guards[1] = AddUnit(seat, BattleCatalog.Guard(seat == 0 ? loadout.RightGuard : RoyalGuard.Crossbowman),
                    Battlefield.GuardHome(seat, 1), 1);
            }
            nextBotPlay = OpeningTicks + TickRate;
        }

        internal BattleUnit Prince(int seat) => sides[seat].Prince;
        internal double Energy(int seat) => sides[seat].Energy;
        internal BattleCard Hand(int seat, int slot) => sides[seat].Hand[slot];
        internal BattleCard NextCard(int seat) => sides[seat].Queue.Peek();
        internal int SprintTicksLeft(int seat) => Math.Max(0, sides[seat].SprintReady - Tick);
        internal bool Unlocked(int seat, int side) => !sides[1 - seat].Guards[side].Alive;

        internal bool MayDeploy(int seat, int col, int row) =>
            Battlefield.MayDeploy(seat, col, row, Unlocked(seat, 0), Unlocked(seat, 1));

        internal bool TrySprint(int seat)
        {
            Side side = sides[seat];
            if (!Live || !side.Prince.Alive || Tick < side.SprintReady || Tick < side.Prince.StunEnd)
                return false;
            side.SprintEnd = Tick + SprintLength;
            side.SprintReady = Tick + SprintCooldown;
            return true;
        }

        internal PlayResult Validate(int seat, int slot, BattleCard expected, Vector2 point)
        {
            if (!Live)
                return PlayResult.Inactive;
            if (slot < 0 || slot >= HandSize || expected == null || Hand(seat, slot) != expected)
                return PlayResult.CardChanged;
            if (Energy(seat) + 1e-8 < expected.Cost)
                return PlayResult.Energy;
            if (!Battlefield.Contains(point))
                return PlayResult.Bounds;
            if (expected.IsSpell)
                return PlayResult.Accepted;

            int col = Battlefield.Column(point), row = Battlefield.Row(point);
            if (!MayDeploy(seat, col, row))
                return PlayResult.Territory;

            Vector2 center = Battlefield.Cell(col, row);
            int count = 0;
            foreach (BattleUnit unit in units)
            {
                if (!unit.Alive)
                    continue;
                if (unit.Seat == seat)
                    count++;
                if (Battlefield.Column(unit.Position) == col && Battlefield.Row(unit.Position) == row ||
                    Within(unit.Position, center, unit.Card.Radius + expected.Radius))
                    return PlayResult.Occupied;
            }
            return count >= MaxUnitsPerSide ? PlayResult.UnitLimit : PlayResult.Accepted;
        }

        internal PlayResult Play(int seat, int slot, BattleCard expected, Vector2 point, Vector2 direction)
        {
            PlayResult check = Validate(seat, slot, expected, point);
            if (check != PlayResult.Accepted)
                return check;

            if (expected.IsSpell)
            {
                Vector2 forward = direction.LengthSquared() >= .01f && float.IsFinite(direction.LengthSquared())
                    ? Vector2.Normalize(direction) : (seat == 0 ? Vector2.UnitY : -Vector2.UnitY);
                var spell = new Spell(seat, expected, point, forward, Tick + (expected.Skill == Skill.Meteor ? TickRate : 0));
                if (spell.Impact == Tick)
                    ResolveSpell(spell);
                else
                {
                    spells.Add(spell);
                    AddEffect(EffectKind.MeteorWarning, point, point, TickRate, expected.Range, Skill.Meteor);
                }
            }
            else
            {
                Vector2 center = Battlefield.Cell(Battlefield.Column(point), Battlefield.Row(point));
                AddUnit(seat, expected, center);
            }

            Side side = sides[seat];
            side.Energy -= expected.Cost;
            side.Cycle(slot);
            return PlayResult.Accepted;
        }

        internal void Step()
        {
            if (Winner != Playing)
                return;

            Tick++;
            if (Tick <= OpeningTicks)
                return;

            effects.RemoveAll(effect => effect.End + TickRate / SnapshotRate < Tick);
            foreach (Side side in sides)
                side.Energy = Math.Min(MaxEnergy, side.Energy + 1d / (3 * TickRate));

            for (int i = 0; i < spells.Count;)
            {
                Spell spell = spells[i];
                if (spell.Impact > Tick)
                {
                    i++;
                    continue;
                }
                ResolveSpell(spell);
                spells.RemoveAt(i);
            }

            if (Elapsed % HazardPeriod == 0)
            {
                foreach (BattleUnit unit in units)
                    if (unit.Alive && Battlefield.InHazard(unit.Position))
                        Damage(unit, unit.Card.Health * .08f);
                AddEffect(EffectKind.Hazard, Vector2.Zero, Vector2.Zero, 12, 0, Skill.Flame);
            }

            foreach (BattleUnit unit in units)
                if (unit.Alive)
                    StepStatus(unit);
            foreach (BattleUnit unit in units)
                if (unit.Alive)
                    StepUnit(unit);

            ResolveBodies();
            units.RemoveAll(unit => !unit.Alive);
            CheckEnd();
            if (Live && practiceOpponent)
                StepOpponent();
        }

        private BattleUnit AddUnit(int seat, BattleCard card, Vector2 location, int guardSide = -1)
        {
            var unit = new BattleUnit(++nextId, seat, card, location, guardSide) { PoseTick = Tick };
            units.Add(unit);
            return unit;
        }

        private void StepStatus(BattleUnit unit)
        {
            if (unit.PoisonNext != 0 && Tick >= unit.PoisonNext && Tick <= unit.PoisonEnd)
            {
                Damage(unit, 22, true);
                unit.PoisonNext += TickRate;
            }
            if (!unit.Alive)
                return;
            if (unit.ShieldReady != 0 && Tick >= unit.ShieldReady)
            {
                unit.ShieldReady = 0;
                unit.Shield = 400;
                StartStatus(unit, BattlePose.ShieldRegrow);
            }
        }

        private void StepUnit(BattleUnit unit)
        {
            if (Tick < unit.StunEnd)
            {
                CancelAction(unit);
                unit.SetPose(BattlePose.Idle, Tick);
                return;
            }
            if (Tick < unit.StatusEnd)
                return;

            if (unit.IsPrince)
            {
                bool sprint = Tick <= sides[unit.Seat].SprintEnd;
                float speed = sprint ? SprintSpeed : unit.Card.Speed;
                unit.PatrolDistance += speed * SlowFactor(unit) / TickRate;
                Vector2 next = Battlefield.Patrol(unit.Seat, unit.PatrolDistance);
                Face(unit, next);
                unit.Position = next;
                unit.SetPose(sprint ? BattlePose.Sprint : BattlePose.Walk, Tick);
                return;
            }

            if (unit.ActionEnd > Tick)
            {
                if (Tick >= unit.ActionContact && unit.LockedTarget != null)
                {
                    BattleUnit target = unit.LockedTarget;
                    unit.LockedTarget = null;
                    if (CanContact(unit, target))
                        Contact(unit, target);
                }
                return;
            }

            BattleUnit targetUnit = FindTarget(unit);
            if (targetUnit != null && InAttackRange(unit, targetUnit))
            {
                Face(unit, targetUnit.Position);
                if (Tick >= unit.NextAction)
                    StartAction(unit, targetUnit);
                else
                    unit.SetPose(BattlePose.Idle, Tick);
                return;
            }

            Vector2 goal = targetUnit?.Position ?? AdvanceGoal(unit);
            Move(unit, goal);
        }

        private static void Face(BattleUnit unit, Vector2 target)
        {
            Vector2 direction = target - unit.Position;
            if (direction.LengthSquared() > .00001f)
                unit.Heading = Vector2.Normalize(direction);
        }

        private void Move(BattleUnit unit, Vector2 goal)
        {
            Vector2 delta = goal - unit.Position;
            float distance = delta.Length();
            float step = Math.Min(distance, unit.Card.Speed * SlowFactor(unit) / TickRate);
            if (step <= .0001f)
            {
                unit.SetPose(BattlePose.Idle, Tick);
                return;
            }

            Vector2 requested = unit.Position + delta * (step / distance);
            Vector2 next = Constrain(unit, requested);
            if (Vector2.DistanceSquared(unit.Position, next) <= .000001f)
            {
                unit.SetPose(BattlePose.Idle, Tick);
                return;
            }

            Face(unit, next);
            unit.Position = next;
            unit.SetPose(BattlePose.Walk, Tick);
        }

        private Vector2 AdvanceGoal(BattleUnit unit)
        {
            if (unit.GuardSide >= 0)
                return unit.Home;
            if (unit.Card.IsSupport)
                return new Vector2(unit.LaneX, unit.Seat == 0 ? 4 : -4);

            BattleUnit enemyPrince = Prince(1 - unit.Seat);
            float forward = unit.Seat == 0 ? 1 : -1;
            return unit.Position.Y * forward < 9 && !Within(unit.Position, enemyPrince.Position, Detection)
                ? new Vector2(unit.LaneX, forward * 9) : enemyPrince.Position;
        }

        private BattleUnit FindTarget(BattleUnit unit)
        {
            BattleUnit best = null;
            float nearest = float.MaxValue;
            foreach (BattleUnit candidate in units)
            {
                if (!candidate.Alive || candidate == unit)
                    continue;

                if (unit.Card.IsSupport)
                {
                    if (candidate.Seat != unit.Seat || candidate.IsPrince || candidate.Card.IsSupport)
                        continue;
                    if (unit.Card.Skill == Skill.Heal && candidate.Health >= candidate.Card.Health)
                        continue;
                }
                else if (candidate.Seat == unit.Seat || unit.Card.Skill == Skill.Charge && !candidate.IsPrince)
                    continue;

                float distance = Vector2.DistanceSquared(unit.Position, candidate.Position);
                if (distance >= nearest || distance > Detection * Detection)
                    continue;
                if (unit.GuardSide >= 0 && !CanGuardEngage(unit, candidate))
                    continue;

                nearest = distance;
                best = candidate;
            }
            return best;
        }

        private static bool CanGuardEngage(BattleUnit guard, BattleUnit target)
        {
            if (!Within(guard.Home, target.Position, Battlefield.GuardDetection))
                return false;
            float minimumDistance = Vector2.Distance(guard.Home, target.Position) - Battlefield.GuardLeash
                - guard.Card.Radius - target.Card.Radius;
            return minimumDistance <= guard.Card.Range;
        }

        private static bool CanContact(BattleUnit attacker, BattleUnit target) =>
            target.Alive && InAttackRange(attacker, target) &&
            (attacker.GuardSide < 0 || Within(attacker.Home, target.Position, Battlefield.GuardDetection));

        private static bool InAttackRange(BattleUnit attacker, BattleUnit target)
        {
            if (ContactDistance(attacker, target) > attacker.Card.Range + .001f)
                return false;
            return attacker.Card.Skill switch
            {
                Skill.Hammer => Within(attacker.Position, target.Position, 1.8f + target.Card.Radius),
                Skill.Axe => Within(attacker.Position, target.Position, 1.5f + target.Card.Radius),
                _ => true
            };
        }

        private void StartAction(BattleUnit unit, BattleUnit target)
        {
            float speed = HasRally(unit) ? 1.15f : 1;
            int interval = Math.Max(2, ToTicks(unit.Card.Interval / speed));
            BattlePose pose = unit.Card.Skill == Skill.Bloom && unit.SuccessfulHits % 3 == 2
                ? BattlePose.Bloom : BattlePose.Action;
            ActionTiming timing = getTiming(unit.Card.Id, pose);
            int duration = Math.Clamp(ToTicks(timing.Length), 2, interval);
            int contact = Math.Clamp((int)Math.Ceiling(duration * timing.ContactFraction), 1, duration - 1);
            unit.LockedTarget = target;
            unit.ActionContact = Tick + contact;
            unit.ActionEnd = Tick + duration;
            unit.NextAction = Tick + interval;
            unit.SetPose(pose, Tick, duration);
        }

        private bool HasRally(BattleUnit unit)
        {
            if (unit.Card.IsSupport)
                return false;
            foreach (BattleUnit friendly in units)
                if (friendly.Alive && friendly.Seat == unit.Seat && friendly.Card.Skill == Skill.Rally &&
                    Within(unit.Position, friendly.Position, 3))
                    return true;
            return false;
        }

        private void Contact(BattleUnit unit, BattleUnit target)
        {
            Skill skill = unit.Card.Skill;
            float power = unit.Card.Power;
            if (skill == Skill.Rally || skill == Skill.Heal)
            {
                if (skill == Skill.Heal)
                    Heal(target, power);
                AddEffect(EffectKind.Heal, unit.Position, target.Position, 10, 0, skill);
                return;
            }

            unit.SuccessfulHits++;
            switch (skill)
            {
                case Skill.Cannon:
                case Skill.Flame:
                case Skill.Hammer:
                    Vector2 center = skill == Skill.Hammer ? unit.Position : target.Position;
                    float radius = skill == Skill.Hammer ? 1.8f : skill == Skill.Cannon ? 1.5f : 1.1f;
                    foreach (BattleUnit enemy in units)
                        if (enemy.Alive && enemy.Seat != unit.Seat && Within(enemy.Position, center, radius + enemy.Card.Radius))
                            Damage(enemy, power);
                    AddEffect(EffectKind.Area, unit.Position, center, 12, radius, skill);
                    return;
                case Skill.Axe:
                    foreach (BattleUnit enemy in units)
                    {
                        Vector2 offset = enemy.Position - unit.Position;
                        if (enemy.Alive && enemy.Seat != unit.Seat && InAttackRange(unit, enemy) &&
                            Vector2.Dot(unit.Heading, offset) >= .5f * offset.Length())
                            Damage(enemy, power);
                    }
                    break;
                case Skill.Frost:
                    target.SlowEnd = Tick + ToTicks(1.5f);
                    break;
                case Skill.Poison:
                    if (target.PoisonEnd < Tick || target.PoisonNext == 0)
                        target.PoisonNext = Tick + TickRate;
                    target.PoisonEnd = Tick + 4 * TickRate;
                    break;
                case Skill.Stun:
                    if (Tick >= target.StunImmuneEnd)
                    {
                        target.StunEnd = Tick + ToTicks(.4f);
                        target.StunImmuneEnd = target.StunEnd + ToTicks(.6f);
                        CancelAction(target);
                    }
                    break;
                case Skill.Sunder:
                    if (!target.IsPrince)
                        power += target.Card.Health * .05f;
                    break;
                case Skill.Bloom:
                    if (unit.SuccessfulHits % 3 == 0)
                        power *= 2;
                    break;
            }
            if (skill != Skill.Axe)
                Damage(target, power);
            if (skill == Skill.Lifesteal && unit.Awakened)
                Heal(unit, 35);
            AddEffect(unit.Card.Range >= 4 ? EffectKind.Projectile : EffectKind.Point,
                unit.Position, target.Position, 9, 0, skill);
        }

        private void ResolveSpell(Spell spell)
        {
            foreach (BattleUnit enemy in units)
            {
                if (!enemy.Alive || enemy.Seat == spell.Seat)
                    continue;
                Vector2 delta = enemy.Position - spell.Point;
                if (spell.Card.Skill == Skill.Rift)
                {
                    float length = Vector2.Dot(delta, spell.Direction);
                    if (length >= 0 && length <= spell.Card.Range &&
                        (delta - spell.Direction * length).LengthSquared() <= Battlefield.RiftHalfWidth * Battlefield.RiftHalfWidth)
                        Damage(enemy, enemy.IsPrince ? 45 : spell.Card.Power);
                }
                else if (delta.LengthSquared() <= spell.Card.Range * spell.Card.Range)
                {
                    float damage = spell.Card.Skill == Skill.Thunder
                        ? enemy.Card.Health * (enemy.IsPrince ? .01f : spell.Card.Power)
                        : (enemy.IsPrince ? 60 : spell.Card.Power);
                    Damage(enemy, damage);
                }
            }
            if (spell.Card.Skill == Skill.Rift)
                AddEffect(EffectKind.Rift, spell.Point, spell.Point + spell.Direction * spell.Card.Range, 12,
                    Battlefield.RiftHalfWidth, Skill.Rift);
            else
                AddEffect(EffectKind.Area, spell.Point, spell.Point, 13, spell.Card.Range, spell.Card.Skill);
        }

        private void Damage(BattleUnit unit, float amount, bool pierce = false)
        {
            if (!unit.Alive)
                return;
            if (!pierce && unit.Shield > 0)
            {
                float absorbed = Math.Min(unit.Shield, amount);
                unit.Shield -= absorbed;
                amount -= absorbed;
                if (unit.Shield == 0)
                {
                    unit.ShieldReady = Tick + 5 * TickRate;
                    StartStatus(unit, BattlePose.ShieldBreak);
                }
            }

            unit.Health = Math.Max(0, unit.Health - amount);
            if (unit.Alive && unit.Card.Skill == Skill.Lifesteal && !unit.Awakened && unit.Health < unit.Card.Health / 2)
            {
                unit.Awakened = true;
                StartStatus(unit, BattlePose.Awaken);
            }
        }

        private void StartStatus(BattleUnit unit, BattlePose pose)
        {
            CancelAction(unit);
            int duration = ToTicks(getTiming(unit.Card.Id, pose).Length);
            unit.StatusEnd = Tick + duration;
            unit.SetPose(pose, Tick, duration);
        }

        private static void Heal(BattleUnit unit, float amount)
        {
            unit.Health = Math.Min(unit.Card.Health, unit.Health + amount);
            if (unit.Health == unit.Card.Health)
                unit.Awakened = false;
        }

        private static void CancelAction(BattleUnit unit)
        {
            unit.LockedTarget = null;
            unit.ActionEnd = 0;
        }

        private void ResolveBodies()
        {
            // As in Tower Duel, resolve overlaps after movement in stable unit-ID order.
            // Patrol is kinematic: its route cannot be jammed, and other bodies are displaced.
            for (int i = 0; i < units.Count; i++)
            {
                BattleUnit a = units[i];
                if (!a.Alive)
                    continue;
                for (int j = i + 1; j < units.Count; j++)
                {
                    BattleUnit b = units[j];
                    if (!b.Alive)
                        continue;
                    Vector2 delta = b.Position - a.Position;
                    float gap = a.Card.Radius + b.Card.Radius;
                    if (delta.LengthSquared() >= gap * gap)
                        continue;
                    float distance = delta.Length();
                    Vector2 axis = distance > .0001f ? delta / distance : Vector2.UnitX;
                    float push = gap - distance;
                    if (!a.IsPrince)
                        a.Position = Constrain(a, a.Position - axis * (b.IsPrince ? push : push * .5f));
                    if (!b.IsPrince)
                        b.Position = Constrain(b, b.Position + axis * (a.IsPrince ? push : push * .5f));
                }
            }
        }

        private void StepOpponent()
        {
            BattleUnit prince = Prince(1);
            if (prince.Health < 4200)
                foreach (BattleUnit unit in units)
                    if (unit.Seat == 0 && Within(unit.Position, prince.Position, 2.5f))
                    {
                        TrySprint(1);
                        break;
                    }

            if (Tick < nextBotPlay)
                return;
            nextBotPlay = Tick + TickRate;
            for (int slot = 0; slot < HandSize; slot++)
            {
                BattleCard card = Hand(1, slot);
                if (Energy(1) + 1e-8 < card.Cost)
                    continue;
                if (card.IsSpell)
                {
                    foreach (BattleUnit target in units)
                        if (target.Seat == 0 && !target.IsPrince &&
                            Play(1, slot, card, target.Position, -Vector2.UnitY) == PlayResult.Accepted)
                        {
                            nextBotPlay = Tick + 3 * TickRate;
                            return;
                        }
                    continue;
                }

                for (int offset = 0; offset < Battlefield.Columns; offset++)
                {
                    int col = (nextBotLane * 6 + 2 + offset) % Battlefield.Columns;
                    for (int row = 22; row >= 6; row--)
                        if (Play(1, slot, card, Battlefield.Cell(col, row), -Vector2.UnitY) == PlayResult.Accepted)
                        {
                            nextBotLane = (Battlefield.Lane(col) + 1) % 3;
                            nextBotPlay = Tick + 3 * TickRate;
                            return;
                        }
                }
            }
        }

        private void CheckEnd()
        {
            float a = Prince(0).Health, b = Prince(1).Health;
            if (a == 0 || b == 0 || Elapsed >= DurationTicks)
                Winner = Math.Abs(a - b) < .01f ? Draw : (a > b ? 0 : 1);
        }

        private void AddEffect(EffectKind kind, Vector2 origin, Vector2 target, int duration, float radius, Skill skill) =>
            effects.Add(new BattleEffect(kind, origin, target, Tick, Tick + duration, radius, skill));

        private static bool Within(Vector2 a, Vector2 b, float range) => Vector2.DistanceSquared(a, b) <= range * range;
        private static float ContactDistance(BattleUnit a, BattleUnit b) =>
            Math.Max(0, Vector2.Distance(a.Position, b.Position) - a.Card.Radius - b.Card.Radius);
        private float SlowFactor(BattleUnit unit) => Tick < unit.SlowEnd ? .75f : 1;
        private static int ToTicks(float seconds) => (int)Math.Ceiling(seconds * TickRate);
        private static Vector2 Constrain(BattleUnit unit, Vector2 point)
        {
            if (unit.GuardSide >= 0)
                point = Battlefield.InsideLeash(point, unit.Home);
            return Battlefield.Clamp(point, unit.Card.Radius);
        }
    }
}
