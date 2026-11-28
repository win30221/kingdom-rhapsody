using System;
using System.Collections.Generic;
using System.Numerics;

namespace KingdomRhapsody
{
    public enum BattlePose { Idle, Walk, Sprint, Action, Bloom, ShieldBreak, ShieldRegrow, Awaken }
    internal enum PlayResult { Accepted, Inactive, CardChanged, Energy, Bounds, Territory, Occupied, UnitLimit, Pending }
    internal enum EffectKind { Point, Area, Projectile, Rift, Heal, Rally, Hazard, MeteorWarning, ShieldBreak, ShieldRegrow, Death }

    [Flags]
    internal enum BattleStatus { None = 0, Slow = 1, Poison = 2, Stun = 4, Rally = 8, Awakened = 16, ShieldDown = 32 }

    internal readonly struct ActionTiming
    {
        internal readonly float Length, ContactFraction;

        internal ActionTiming(int frames, float fps, int contactFrame)
        {
            Length = frames / fps;
            ContactFraction = contactFrame / (float)frames;
        }

        internal static ActionTiming Standard => new(12, 16, 6);

        internal int DurationTicks => Math.Max(2, (int)Math.Ceiling(Length * BattleMatch.TickRate));
        internal int CueTick(int duration) => Math.Clamp((int)Math.Ceiling(duration * ContactFraction), 1, duration - 1);
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
        internal int LastHitTick = -1;
        internal float WalkPhase;
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
            if (pose == BattlePose.Walk)
                WalkPhase = 0;
        }
    }

    internal readonly struct BattleEffect
    {
        internal readonly EffectKind Kind;
        internal readonly Vector2 Origin, Target;
        internal readonly int Id, Begin, End;
        internal readonly float Radius;
        internal readonly Skill Skill;
        internal readonly string TrackingKey;

        internal BattleEffect(int id, EffectKind kind, Vector2 origin, Vector2 target,
            int begin, int end, float radius, Skill skill, string trackingKey)
        {
            Id = id;
            Kind = kind;
            Origin = origin;
            Target = target;
            Begin = begin;
            End = end;
            Radius = radius;
            Skill = skill;
            TrackingKey = trackingKey;
        }
    }

    internal sealed class BattleMatch
    {
        internal const int TickRate = 30, SnapshotRate = 15;
        internal const int OpeningTicks = 2 * TickRate, RegulationTicks = 180 * TickRate;
        internal const int OvertimeTicks = 60 * TickRate, DurationTicks = RegulationTicks + OvertimeTicks;
        internal const int HandSize = 4, MaxUnitsPerSide = 40;
        internal const double MaxEnergy = 10, StartEnergy = 5;
        internal const int SprintCost = 2, OvertimeEnergyMultiplier = 2;
        internal const float RallyRadius = 3;
        internal const int DeathAnimationTicks = 12;
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

        private readonly struct Delivery
        {
            internal readonly BattleUnit Source, Target;
            internal readonly int Impact;

            internal Delivery(BattleUnit source, BattleUnit target, int impact)
            {
                Source = source;
                Target = target;
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
        private readonly List<Delivery> deliveries = new List<Delivery>();
        private readonly List<(BattleUnit Source, BattleUnit Target)> contacts = new List<(BattleUnit, BattleUnit)>();
        private readonly List<(BattleUnit Target, float Power, Skill Skill, bool Pierce)> damage =
            new List<(BattleUnit, float, Skill, bool)>();
        private readonly List<(BattleUnit Target, float Power)> healing = new List<(BattleUnit, float)>();
        private readonly List<BattleEffect> effects = new List<BattleEffect>();
        private readonly bool practiceOpponent;
        private int nextId, nextEffectId, nextBotPlay, nextBotLane;

        internal int Tick { get; private set; }
        internal int Winner { get; private set; } = Playing;
        internal int Elapsed => Math.Max(0, Tick - OpeningTicks);
        internal bool Overtime => Elapsed >= RegulationTicks;
        internal int PhaseTicksLeft => Math.Max(0, (Overtime ? DurationTicks : RegulationTicks) - Elapsed);
        internal bool Live => Tick >= OpeningTicks && Winner == Playing;
        internal bool PresentationChanged { get; private set; }
        internal int HazardTicksLeft => HazardPeriod - Elapsed % HazardPeriod;
        internal bool HazardWarning => Live && HazardTicksLeft <= Battlefield.HazardWarning * TickRate;
        internal IReadOnlyList<BattleUnit> Units => units;
        internal IReadOnlyList<BattleEffect> Effects => effects;

        internal BattleMatch(BattleLoadout loadout, Func<string, BattlePose, ActionTiming> getTiming = null,
            bool practiceOpponent = true)
            : this(loadout, new BattleLoadout(RoyalGuard.Crossbowman, RoyalGuard.Spearman, PracticeDeck), getTiming, practiceOpponent) { }

        internal BattleMatch(BattleLoadout first, BattleLoadout second, Func<string, BattlePose, ActionTiming> getTiming = null,
            bool practiceOpponent = false)
        {
            this.getTiming = getTiming ?? ((_, _) => ActionTiming.Standard);
            this.practiceOpponent = practiceOpponent;
            var loadouts = new[] { first, second };
            sides = new[] { new Side(first.TroopIds), new Side(second.TroopIds) };
            for (int seat = 0; seat < sides.Length; seat++)
            {
                Side side = sides[seat];
                side.Prince = AddUnit(seat, BattleCatalog.Prince, Battlefield.Patrol(seat, 0));
                RoyalGuard left = seat == 0 ? loadouts[seat].LeftGuard : loadouts[seat].RightGuard;
                RoyalGuard right = seat == 0 ? loadouts[seat].RightGuard : loadouts[seat].LeftGuard;
                side.Guards[0] = AddUnit(seat, BattleCatalog.Guard(left),
                    Battlefield.GuardHome(seat, 0), 0);
                side.Guards[1] = AddUnit(seat, BattleCatalog.Guard(right),
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
        internal int TerritoryMask => (Unlocked(0, 0) ? 1 : 0) | (Unlocked(0, 1) ? 2 : 0) |
            (Unlocked(1, 0) ? 4 : 0) | (Unlocked(1, 1) ? 8 : 0);

        internal void Forfeit(int seat)
        {
            if (Winner != Playing)
                return;
            Tick++;
            Winner = 1 - seat;
        }

        internal bool MayDeploy(int seat, int col, int row) =>
            Battlefield.MayDeploy(seat, col, row, Unlocked(seat, 0), Unlocked(seat, 1));

        internal bool CanSprint(int seat)
        {
            Side side = sides[seat];
            return Live && side.Prince.Alive && Tick >= side.SprintReady && Tick >= side.Prince.StunEnd &&
                side.Energy + 1e-8 >= SprintCost;
        }

        internal bool TrySprint(int seat)
        {
            if (!CanSprint(seat))
                return false;
            Side side = sides[seat];
            side.Energy = Math.Max(0, side.Energy - SprintCost);
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
                if (Battlefield.BlocksSpawn(unit.Position, unit.Card.Radius, col, row, center, expected.Radius))
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
                // Commands accepted between ticks share the next impact phase.
                var spell = new Spell(seat, expected, point, forward, Tick + (expected.Skill == Skill.Meteor ? TickRate : 1));
                spells.Add(spell);
                if (expected.Skill == Skill.Meteor)
                    AddEffect(EffectKind.MeteorWarning, point, point, TickRate, expected.Range, Skill.Meteor);
            }
            else
            {
                Vector2 center = Battlefield.Cell(Battlefield.Column(point), Battlefield.Row(point));
                AddUnit(seat, expected, center);
            }

            Side side = sides[seat];
            side.Energy = Math.Max(0, side.Energy - expected.Cost);
            side.Cycle(slot);
            return PlayResult.Accepted;
        }

        internal void Step()
        {
            if (Winner != Playing)
                return;

            Tick++;
            PresentationChanged = false;
            if (Tick <= OpeningTicks)
                return;

            effects.RemoveAll(effect => effect.End + TickRate / SnapshotRate < Tick);
            double energyPerTick = (Elapsed > RegulationTicks ? OvertimeEnergyMultiplier : 1) / (3d * TickRate);
            foreach (Side side in sides)
                side.Energy = Math.Min(MaxEnergy, side.Energy + energyPerTick);

            contacts.Clear();
            damage.Clear();
            healing.Clear();
            foreach (BattleUnit unit in units)
                if (unit.Alive)
                    StepStatus(unit);
            foreach (BattleUnit unit in units)
                if (unit.Alive)
                    StepUnit(unit);

            ResolveBodies();
            // Collect this tick's valid contacts before applying any damage or control.
            foreach (var contact in contacts)
                if (CanContact(contact.Source, contact.Target))
                    ReleaseContact(contact.Source, contact.Target);

            for (int i = 0; i < deliveries.Count;)
            {
                Delivery delivery = deliveries[i];
                if (delivery.Impact > Tick)
                {
                    i++;
                    continue;
                }
                Contact(delivery.Source, delivery.Target);
                deliveries.RemoveAt(i);
            }

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
                        QueueDamage(unit, unit.Card.Health * .08f);
                AddEffect(EffectKind.Hazard, Vector2.Zero, Vector2.Zero, 12, 0, Skill.Flame);
            }

            ResolveImpacts();
            foreach (BattleUnit unit in units)
                if (!unit.Alive)
                    AddEffect(EffectKind.Death, unit.Position, unit.Position, DeathAnimationTicks, 0, Skill.Strike, unit.Key);
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
                QueueDamage(unit, 22, pierce: true);
                unit.PoisonNext += TickRate;
            }
            if (unit.ShieldReady == 0)
                return;

            ActionTiming timing = getTiming(unit.Card.Id, BattlePose.ShieldRegrow);
            if (Tick == unit.ShieldReady - timing.CueTick(timing.DurationTicks))
                StartStatus(unit, BattlePose.ShieldRegrow);
            if (Tick >= unit.ShieldReady)
            {
                unit.ShieldReady = 0;
                unit.Shield = 400;
                AddEffect(EffectKind.ShieldRegrow, unit.Position, unit.Position, 12, 0, Skill.Shield);
            }
        }

        private void StepUnit(BattleUnit unit)
        {
            if (Tick < unit.StatusEnd)
                return;
            if (Tick < unit.StunEnd)
            {
                CancelAction(unit);
                unit.SetPose(BattlePose.Idle, Tick);
                return;
            }
            if (unit.IsPrince)
            {
                bool sprint = Tick <= sides[unit.Seat].SprintEnd;
                float speed = sprint ? SprintSpeed : unit.Card.Speed;
                unit.PatrolDistance += speed * SlowFactor(unit) / TickRate;
                Vector2 next = Battlefield.Patrol(unit.Seat, unit.PatrolDistance);
                MoveTo(unit, next, sprint ? BattlePose.Sprint : BattlePose.Walk);
                return;
            }

            if (unit.ActionEnd > Tick)
            {
                if (Tick >= unit.ActionContact && unit.LockedTarget != null)
                {
                    BattleUnit target = unit.LockedTarget;
                    unit.LockedTarget = null;
                    contacts.Add((unit, target));
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

            if (targetUnit == null && unit.Card.Skill == Skill.Heal)
            {
                BattleUnit escort = FindTarget(unit, followHealthy: true);
                if (escort != null)
                {
                    Move(unit, Within(unit.Position, escort.Position, 3) ? unit.Position : escort.Position);
                    return;
                }
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

            MoveTo(unit, next, BattlePose.Walk);
        }

        private void MoveTo(BattleUnit unit, Vector2 next, BattlePose pose)
        {
            float distance = Vector2.Distance(unit.Position, next);
            Face(unit, next);
            unit.Position = next;
            unit.SetPose(pose, Tick);
            if (pose == BattlePose.Walk)
                unit.WalkPhase += distance * TickRate / unit.Card.Speed;
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

        private BattleUnit FindTarget(BattleUnit unit, bool followHealthy = false)
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
                    if (!followHealthy && unit.Card.Skill == Skill.Heal && candidate.Health >= candidate.Card.Health)
                        continue;
                }
                else if (candidate.Seat == unit.Seat || unit.Card.Skill == Skill.Charge && !candidate.IsPrince)
                    continue;

                float distance = Vector2.DistanceSquared(unit.Position, candidate.Position);
                if (distance >= nearest || (unit.GuardSide >= 0
                    ? !CanGuardEngage(unit, candidate) : distance > Detection * Detection))
                    continue;

                nearest = distance;
                best = candidate;
            }
            return best;
        }

        private static bool CanGuardEngage(BattleUnit guard, BattleUnit target)
        {
            float edgeDistance = Vector2.Distance(guard.Home, target.Position) - guard.Card.Radius - target.Card.Radius;
            return edgeDistance <= Battlefield.GuardDetection && edgeDistance <= Battlefield.GuardLeash + guard.Card.Range;
        }

        private static bool CanContact(BattleUnit attacker, BattleUnit target) =>
            target.Alive && InAttackRange(attacker, target) &&
            (attacker.GuardSide < 0 || CanGuardEngage(attacker, target));

        private static bool InAttackRange(BattleUnit attacker, BattleUnit target)
        {
            if (attacker.Card.Skill == Skill.Rally)
                return Within(attacker.Position, target.Position, RallyRadius);
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
            int duration = Math.Min(timing.DurationTicks, interval);
            int contact = timing.CueTick(duration);
            unit.LockedTarget = target;
            unit.ActionContact = Tick + contact;
            unit.ActionEnd = Tick + duration;
            unit.NextAction = Tick + interval;
            unit.SetPose(pose, Tick, duration);
        }

        private bool HasRally(BattleUnit unit)
        {
            if (unit.IsPrince || unit.Card.IsSupport)
                return false;
            foreach (BattleUnit friendly in units)
                if (friendly.Alive && friendly.Seat == unit.Seat && friendly.Card.Skill == Skill.Rally &&
                    Within(unit.Position, friendly.Position, RallyRadius))
                    return true;
            return false;
        }

        internal BattleStatus Status(BattleUnit unit)
        {
            BattleStatus status = BattleStatus.None;
            if (Tick < unit.SlowEnd) status |= BattleStatus.Slow;
            if (unit.PoisonEnd != 0 && Tick <= unit.PoisonEnd) status |= BattleStatus.Poison;
            if (Tick < unit.StunEnd) status |= BattleStatus.Stun;
            if (HasRally(unit)) status |= BattleStatus.Rally;
            if (unit.Awakened) status |= BattleStatus.Awakened;
            if (unit.Card.Skill == Skill.Shield && unit.Shield == 0) status |= BattleStatus.ShieldDown;
            return status;
        }

        private void ReleaseContact(BattleUnit unit, BattleUnit target)
        {
            if (unit.Card.FlightTicks == 0)
            {
                Contact(unit, target);
                return;
            }

            deliveries.Add(new Delivery(unit, target, Tick + unit.Card.FlightTicks));
            AddEffect(EffectKind.Projectile, unit.Position, target.Position, unit.Card.FlightTicks, 0, unit.Card.Skill, target.Key);
        }

        private void Contact(BattleUnit unit, BattleUnit target)
        {
            Skill skill = unit.Card.Skill;
            float power = unit.Card.Power;
            if (skill == Skill.Cannon || skill == Skill.Flame || skill == Skill.Hammer)
            {
                Vector2 center = skill == Skill.Hammer ? unit.Position : target.Position;
                float radius = skill == Skill.Hammer ? 1.8f : skill == Skill.Cannon ? 1.5f : 1.1f;
                foreach (BattleUnit enemy in units)
                    if (enemy.Alive && enemy.Seat != unit.Seat && Within(enemy.Position, center, radius + enemy.Card.Radius))
                        QueueDamage(enemy, power);
                AddEffect(EffectKind.Area, unit.Position, center, 12, radius, skill);
                return;
            }

            if (skill == Skill.Axe)
            {
                bool hit = false;
                foreach (BattleUnit enemy in units)
                {
                    Vector2 offset = enemy.Position - unit.Position;
                    if (!enemy.Alive || enemy.Seat == unit.Seat || !InAttackRange(unit, enemy) ||
                        Vector2.Dot(unit.Heading, offset) < .5f * offset.Length())
                        continue;
                    QueueDamage(enemy, power);
                    AddEffect(EffectKind.Point, unit.Position, enemy.Position, 9, 0, skill);
                    hit = true;
                }

                if (hit)
                    unit.SuccessfulHits++;
                return;
            }

            if (!target.Alive)
                return;
            if (skill == Skill.Rally)
            {
                AddEffect(EffectKind.Rally, unit.Position, unit.Position, 12, RallyRadius, skill);
                return;
            }
            if (skill == Skill.Heal)
            {
                healing.Add((target, power));
                return;
            }

            unit.SuccessfulHits++;
            if (skill == Skill.Sunder && !target.IsPrince)
                power += target.Card.Health * .05f;
            if (skill == Skill.Bloom && unit.SuccessfulHits % 3 == 0)
                power *= 2;

            QueueDamage(target, power, skill);
            if (skill == Skill.Lifesteal && unit.Awakened)
                healing.Add((unit, 35));
            AddEffect(EffectKind.Point, unit.Position, target.Position, 9, 0, skill);
        }

        private void QueueDamage(BattleUnit target, float power, Skill skill = Skill.Strike, bool pierce = false) =>
            damage.Add((target, power, skill, pierce));

        private void ResolveImpacts()
        {
            foreach (var hit in damage)
            {
                BattleUnit target = hit.Target;
                if (!target.Alive)
                    continue;
                ApplyAilment(target, hit.Skill);
                Damage(target, hit.Power, hit.Pierce);
            }

            foreach (var heal in healing)
            {
                BattleUnit target = heal.Target;
                if (!target.Alive || target.Health >= target.Card.Health)
                    continue;
                Heal(target, heal.Power);
                AddEffect(EffectKind.Heal, target.Position, target.Position, 10, 0, Skill.Heal);
            }
        }

        private void ApplyAilment(BattleUnit target, Skill skill)
        {
            switch (skill)
            {
                case Skill.Frost:
                    target.SlowEnd = Tick + ToTicks(1.5f);
                    break;
                case Skill.Poison:
                    if (target.PoisonEnd < Tick || target.PoisonNext == 0)
                        target.PoisonNext = Tick + TickRate;
                    target.PoisonEnd = Tick + 4 * TickRate;
                    break;
                case Skill.Stun when Tick >= target.StunImmuneEnd:
                    target.StunEnd = Tick + ToTicks(.4f);
                    target.StunImmuneEnd = target.StunEnd + ToTicks(.6f);
                    CancelAction(target);
                    if (Tick >= target.StatusEnd)
                        target.SetPose(BattlePose.Idle, Tick);
                    break;
            }
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
                        QueueDamage(enemy, enemy.IsPrince ? 45 : spell.Card.Power);
                }
                else if (delta.LengthSquared() <= spell.Card.Range * spell.Card.Range)
                {
                    float damage = spell.Card.Skill == Skill.Thunder
                        ? enemy.Card.Health * (enemy.IsPrince ? .01f : spell.Card.Power)
                        : (enemy.IsPrince ? 60 : spell.Card.Power);
                    QueueDamage(enemy, damage);
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
            unit.LastHitTick = Tick;
            PresentationChanged = true;
            if (!pierce && unit.Shield > 0)
            {
                float absorbed = Math.Min(unit.Shield, amount);
                unit.Shield -= absorbed;
                amount -= absorbed;
                if (unit.Shield == 0)
                {
                    unit.ShieldReady = Tick + 5 * TickRate;
                    StartStatus(unit, BattlePose.ShieldBreak);
                    AddEffect(EffectKind.ShieldBreak, unit.Position, unit.Position, 12, 0, Skill.Shield);
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
            int duration = getTiming(unit.Card.Id, pose).DurationTicks;
            unit.StatusEnd = Tick + duration;
            unit.SetPose(pose, Tick, duration);
            PresentationChanged = true;
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
                    Vector2 previousA = a.Position, previousB = b.Position;
                    if (!a.IsPrince)
                        a.Position = Constrain(a, a.Position - axis * (b.IsPrince ? push : push * .5f));
                    if (!b.IsPrince)
                        b.Position = Constrain(b, b.Position + axis * (a.IsPrince ? push : push * .5f));
                    if (a.IsPrince || b.IsPrince)
                        continue;

                    float appliedA = Vector2.Dot(previousA - a.Position, axis);
                    float appliedB = Vector2.Dot(b.Position - previousB, axis);
                    if (appliedA < push * .5f)
                        b.Position = Constrain(b, b.Position + axis * (push * .5f - appliedA));
                    if (appliedB < push * .5f)
                        a.Position = Constrain(a, a.Position - axis * (push * .5f - appliedB));
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
                Winner = a == b ? Draw : (a > b ? 0 : 1);
        }

        private void AddEffect(EffectKind kind, Vector2 origin, Vector2 target, int duration, float radius,
            Skill skill, string trackingKey = null)
        {
            effects.Add(new BattleEffect(++nextEffectId, kind, origin, target, Tick, Tick + duration, radius, skill, trackingKey));
            PresentationChanged = true;
        }

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
