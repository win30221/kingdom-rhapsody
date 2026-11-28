using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KingdomRhapsody;

internal static class Program
{
    private static void Main()
    {
        Run("saved deck and guards", SavedLoadout);
        Run("full friendly courts and overlapping stone deployment", Territory);
        Run("bank deployment aligns to stone and rejects water", BankDeployment);
        Run("deployed troops cross their three assigned bridges from either court", ThreeBridges);
        Run("changing bridges routes through a court and pushes stop at stone edges", BridgeDetour);
        Run("deployment rejection leaves energy and hand unchanged", Deployment);
        Run("guard detection, leash and return", GuardReturn);
        Run("guard aggro checks the home radius and weapon edges", GuardDetectionEdges);
        Run("patrol, sprint duration and cooldown", Patrol);
        Run("sprint spends energy once and rejects without charge", SprintCost);
        Run("regulation, overtime energy and final health comparison", Overtime);
        Run("same-tick contact and royal knockout are simultaneous", SameTick);
        Run("both teams take central hazard damage at 8 s", Hazard);
        Run("three spell shapes, timing and prince damage", Spells);
        Run("rift release outside the battlefield cancels drag and board aim", RiftAim);
        Run("all collection cards can enter a deck and be played from either seat", EveryCard);
        Run("melee damage is applied on the contact tick", AttackTiming);
        Run("projectile launches first then damage and presentation agree", Projectile);
        Run("a projectile follows the defeated target to the blast center", ProjectileTargetDeath);
        Run("healer escort, rally range and axe miss", SupportAndCone);
        Run("flower critical, pangolin damage and rhino stun", BeastAttacks);
        Run("poison pierces a shield and armadillo shield returns", BeastStatuses);
        Run("bat awakens below half health and heals on hit", BatAwakening);
        Run("shield break and awakening still play while stunned", SimultaneousStatus);
        Run("footman and shieldman catch a patrolling prince", ChasePrince);
        Run("melee pursuit closes the gap before winding up and resumes its walk", MeleePursuit);
        Run("a prince who sprints after windup can dodge the hit", SprintDodgesMelee);
        Run("GameNetKit presents movement and removes defeated units", Presentation);
        Run("short walks advance between snapshots while attacks and pushes retain their pose", StridePresentation);
        Run("paired seats own independent decks, guards and commands", PairedSeats);
        Run("a forfeit can be received on the latest simulation tick", SameTickForfeit);
        Run("pending command energy reconciles in either delivery order", PendingEnergy);
        Run("complete a match against the practice opponent", CompleteMatch);
    }

    private static void Run(string title, Action test)
    {
        test();
        Console.WriteLine("PASS  " + title);
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Battle assertion failed");
    }

    private static bool Near(double left, double right, double tolerance = .001) => Math.Abs(left - right) < tolerance;

    private static BattleMatch Create(params string[] front)
    {
        string[] deck = front.Concat(BattleCatalog.Cards.Select(card => card.Id)).Distinct().Take(Roster.DeckSize).ToArray();
        var loadout = new BattleLoadout(RoyalGuard.Crossbowman, RoyalGuard.Spearman, deck);
        var match = new BattleMatch(loadout, practiceOpponent: false);
        StepTo(match, BattleMatch.OpeningTicks);
        return match;
    }

    private static BattleUnit Guard(BattleMatch match, int seat, int side) =>
        match.Units.First(unit => unit.Seat == seat && unit.GuardSide == side);

    private static void StepTo(BattleMatch match, int tick)
    {
        while (match.Tick < tick && match.Winner == BattleMatch.Playing)
            match.Step();
    }

    private static int GridCount(BattleMatch match, int seat)
    {
        int count = 0;
        for (int r = 0; r < Battlefield.Rows; r++)
            for (int c = 0; c < Battlefield.Columns; c++)
                if (match.MayDeploy(seat, c, r))
                    count++;
        return count;
    }

    private static BattleUnit Deploy(BattleMatch match, int seat, int slot, int column, int row)
    {
        Check(match.Play(seat, slot, match.Hand(seat, slot), Battlefield.Cell(column, row), Vector2.UnitY) == PlayResult.Accepted);
        return match.Units.Last();
    }

    private static void SavedLoadout()
    {
        var lobby = new LobbyLoadout
        {
            leftGuard = RoyalGuard.Crossbowman,
            rightGuard = RoyalGuard.Spearman,
            deck = new[] { "footman", "footman", "old-unknown", "meteorfall" }
        };
        lobby.Normalize();
        Check(lobby.deck.Length == 8 && lobby.deck[0] == "footman" && lobby.deck[1] == "meteorfall");
        Check(lobby.Reserves().Length == 12);
        var match = new BattleMatch(lobby.Snapshot());
        Check(Guard(match, 0, 0).Card == BattleCatalog.Crossbowman && Guard(match, 0, 1).Card == BattleCatalog.Spearman);
        Check(match.Hand(0, 0).Id == "footman" && match.NextCard(0).Id == lobby.deck[4]);
        Check(match.Play(0, 0, match.Hand(0, 0), Battlefield.Cell(0, 21), Vector2.UnitY) == PlayResult.Inactive);
    }

    private static void Territory()
    {
        var match = Create("footman", "bow-recruit", "shieldman", "light-cavalry");
        Check(GridCount(match, 0) == 216 && GridCount(match, 1) == 216);
        foreach (int seat in new[] { 0, 1 })
            for (int col = 0; col < Battlefield.Columns; col++)
                for (int row = 0; row < Battlefield.Rows; row++)
                {
                    bool court = Battlefield.Cell(col, row).Y * (seat == 0 ? -1 : 1) >= Battlefield.Shore;
                    Check(match.MayDeploy(seat, col, row) == court);
                    if (court)
                        foreach (BattleCard card in BattleCatalog.Cards.Where(card => !card.IsSpell))
                        {
                            Vector2 spawn = Battlefield.SpawnPoint(col, row, card.Radius);
                            Check(Battlefield.IsWalkable(spawn, card.Radius));
                            Check(Battlefield.Column(spawn) == col && Battlefield.Row(spawn) == row);
                        }
                }
        Guard(match, 1, 0).Health = 0;
        Check(GridCount(match, 0) == 250 && GridCount(match, 1) == 216);
        Check(match.MayDeploy(0, 3, 9) && match.MayDeploy(1, 3, 9));
        BattleCard next = match.Hand(1, 0);
        Deploy(match, 0, 0, 3, 9);
        Check(match.Play(1, 0, next, Battlefield.Cell(3, 9), -Vector2.UnitY) == PlayResult.Occupied);
        Check(Near(match.Energy(1), 5) && match.Hand(1, 0) == next);
        Guard(match, 1, 1).Health = 0;
        Guard(match, 0, 0).Health = 0;
        Guard(match, 0, 1).Health = 0;
        Check(GridCount(match, 0) == 284 && GridCount(match, 1) == 284);
        Check(!match.MayDeploy(0, 8, 16) && !match.MayDeploy(0, 0, 3) && match.MayDeploy(0, 17, 20));
        int overlap = 0;
        for (int row = 0; row < Battlefield.Rows; row++)
            for (int col = 0; col < Battlefield.Columns; col++)
                if (match.MayDeploy(0, col, row) && match.MayDeploy(1, col, row))
                    overlap++;
        Check(overlap == 104);
    }

    private static void BankDeployment()
    {
        var match = Create("footman", "shieldman");
        BattleCard footman = match.Hand(0, 0);
        Vector2 bank = Battlefield.Cell(5, 20), water = new Vector2(bank.X, -4.1f);
        Guard(match, 1, 0).Health = 0;
        Check(Battlefield.Row(water) == Battlefield.Row(bank) && !Battlefield.IsWalkable(water));
        var aim = new BattleAim(0, footman);
        aim.Update(water, Vector2.UnitY, false);
        Check(aim.HasTarget && aim.Point == water);
        Check(match.Play(0, 0, footman, aim.Point, Vector2.UnitY) == PlayResult.Territory);
        Check(match.Play(0, 0, footman, new Vector2(bank.X, 0), Vector2.UnitY) == PlayResult.Territory);
        Check(Near(match.Energy(0), 5) && match.Hand(0, 0) == footman);
        aim.Update(bank, Vector2.UnitY, false);
        Check(aim.Point == Battlefield.SpawnPoint(5, 20, footman.Radius));
        BattleUnit unit = Deploy(match, 0, 0, 5, 20);
        Check(unit.Position == aim.Point && Battlefield.IsWalkable(unit.Position, unit.Card.Radius));
    }

    private static void ThreeBridges()
    {
        foreach (int seat in new[] { 0, 1 })
            for (int lane = 0; lane < 3; lane++)
            {
                var match = Create("footman");
                foreach (BattleUnit guard in match.Units.Where(unit => unit.GuardSide >= 0))
                    guard.Health = 0;
                BattleUnit unit = Deploy(match, seat, 0, lane * 6 + 1, seat == 0 ? 30 : 1);
                bool crossed = false, reached = false;
                int end = match.Tick + 18 * BattleMatch.TickRate;
                while (match.Tick < end && !reached)
                {
                    match.Step();
                    Check(Battlefield.IsWalkable(unit.Position, unit.Card.Radius));
                    if (Math.Abs(unit.Position.Y) < Battlefield.Shore)
                    {
                        crossed = true;
                        Check(Battlefield.Lane(Battlefield.Column(unit.Position)) == lane);
                    }
                    reached = unit.Position.Y * (seat == 0 ? 1 : -1) > Battlefield.Shore + unit.Card.Radius;
                }
                Check(crossed && reached);
            }
    }

    private static void BridgeDetour()
    {
        const float radius = .57f;
        for (int fromLane = 0; fromLane < 3; fromLane++)
            for (int toLane = 0; toLane < 3; toLane++)
            {
                if (fromLane == toLane)
                    continue;
                Vector2 start = new Vector2(Battlefield.BridgeCenter(fromLane), 0);
                Vector2 goal = new Vector2(Battlefield.BridgeCenter(toLane), 0);
                Check(!Battlefield.CanTravel(start, goal, radius));
                Vector2 push = Battlefield.LimitMovement(start, goal, radius);
                Check(Battlefield.IsWalkable(push, radius) && Battlefield.Lane(Battlefield.Column(push)) == fromLane);

                Check(FollowRoute(start, goal, radius));
            }

        float shoulderX = 2.08f * Battlefield.ArtScaleX - radius - .02f;
        float shoulderY = 3.2f * Battlefield.ArtScaleY + radius + .05f;
        Vector2 nearLanding = new Vector2(shoulderX, -shoulderY), farLanding = new Vector2(shoulderX, shoulderY);
        Check(!Battlefield.CanTravel(nearLanding, farLanding, radius));
        FollowRoute(nearLanding, farLanding, radius);
        FollowRoute(new Vector2(0, -6), farLanding, radius);
    }

    private static bool FollowRoute(Vector2 point, Vector2 goal, float radius)
    {
        bool visitedCourt = false;
        for (int step = 0; step < 500 && Vector2.DistanceSquared(point, goal) > .000001f; step++)
        {
            Vector2 waypoint = Battlefield.NextWaypoint(point, goal, radius, -1);
            Vector2 offset = waypoint - point;
            Vector2 next = offset.Length() <= .12f ? waypoint : point + Vector2.Normalize(offset) * .12f;
            point = Battlefield.LimitMovement(point, next, radius);
            visitedCourt |= Math.Abs(point.Y) >= Battlefield.Shore + radius;
            Check(Battlefield.IsWalkable(point, radius));
        }
        Check(Vector2.DistanceSquared(point, goal) <= .000001f);
        return visitedCourt;
    }

    private static void Deployment()
    {
        var match = Create("footman", "shieldman", "light-cavalry");
        BattleCard initial = match.Hand(0, 0), next = match.NextCard(0);
        Check(match.Play(0, 0, initial, Vector2.Zero, Vector2.UnitY) == PlayResult.Territory);
        Check(match.Play(0, 0, initial, new Vector2(float.NaN, 0), Vector2.UnitY) == PlayResult.Bounds);
        Check(match.Hand(0, 0) == initial && Near(match.Energy(0), 5));
        BattleUnit footman = Deploy(match, 0, 0, 4, 21);
        Check(footman.Position == Battlefield.Cell(4, 21) && footman.Card.Health == 500);
        Check(match.Hand(0, 0) == next && Near(match.Energy(0), 3));
        Check(match.Play(0, 0, initial, Battlefield.Cell(5, 21), Vector2.UnitY) == PlayResult.CardChanged);
        Check(match.Play(0, 1, match.Hand(0, 1), Battlefield.Cell(5, 21), Vector2.UnitY) == PlayResult.Energy);
        Check(Near(match.Energy(0), 3) && match.Hand(0, 1).Id == "shieldman");
    }

    private static void GuardReturn()
    {
        var match = Create();
        BattleUnit guard = Guard(match, 0, 1);
        BattleUnit enemy = Deploy(match, 1, 0, 15, 9);
        enemy.Position = guard.Home + new Vector2(0, 5.8f);
        StepTo(match, match.Tick + 25);
        float distance = Vector2.Distance(guard.Home, guard.Position);
        Check(distance > .2f && distance <= Battlefield.GuardLeash);
        enemy.Health = 0;
        StepTo(match, match.Tick + 90);
        Check(Vector2.Distance(guard.Position, guard.Home) < .005f && guard.Pose == BattlePose.Idle);
    }

    private static void Patrol()
    {
        var match = Create();
        BattleUnit prince = match.Prince(0);
        Check(prince.Position == new Vector2(0, -14.5f));
        StepTo(match, match.Tick + 30);
        Check(Near(prince.Position.X, 1.2f) && Near(prince.Position.Y, -14.5f));
        int start = match.Tick;
        Check(match.TrySprint(0) && !match.TrySprint(0));
        StepTo(match, start + 90);
        Check(prince.Pose == BattlePose.Sprint && Near(prince.PatrolDistance, 14.7f, .005));
        StepTo(match, start + 91);
        Check(prince.Pose == BattlePose.Walk && match.SprintTicksLeft(0) == 359);
        StepTo(match, start + 450);
        Check(match.TrySprint(0));
        Check(Near(match.Prince(0).Health, 6000));
    }

    private static void Hazard()
    {
        var match = Create("crystal-armadillo");
        StepTo(match, BattleMatch.OpeningTicks + 195);
        Check(match.HazardWarning);
        StepTo(match, BattleMatch.OpeningTicks + 239);
        BattleUnit ally = Deploy(match, 0, 0, 8, 21);
        BattleUnit enemy = Deploy(match, 1, 0, 8, 9);
        ally.Position = new Vector2(-1, 0);
        enemy.Position = new Vector2(1, 0);
        float health = enemy.Health, shield = ally.Shield;
        match.Step();
        Check(!match.HazardWarning && match.Effects.Any(effect => effect.Kind == EffectKind.Hazard));
        Check(Near(ally.Shield, shield - 88) && Near(enemy.Health, health - 40));
    }

    private static void Spells()
    {
        var match = Create("meteorfall");
        BattleUnit enemy = Guard(match, 1, 0);
        int cast = match.Tick;
        Check(match.Play(0, 0, match.Hand(0, 0), enemy.Position, Vector2.UnitY) == PlayResult.Accepted);
        StepTo(match, cast + 29);
        Check(enemy.Health == 1400 && match.Effects.Any(effect => effect.Kind == EffectKind.MeteorWarning));
        match.Step();
        Check(enemy.Health == 1100);

        match = Create("thunder-judgment");
        enemy = match.Prince(1);
        Check(match.Play(0, 0, match.Hand(0, 0), enemy.Position, Vector2.UnitY) == PlayResult.Accepted);
        Check(Near(enemy.Health, 6000));
        match.Step();
        Check(Near(enemy.Health, 5940) && match.Prince(0).Health == 6000);

        match = Create("earth-rift");
        enemy = Guard(match, 1, 0);
        var outside = Guard(match, 1, 1);
        Check(match.Play(0, 0, match.Hand(0, 0), enemy.Position - Vector2.UnitX, Vector2.UnitX) == PlayResult.Accepted);
        match.Step();
        Check(enemy.Health == 1160 && outside.Health == 1000);
    }

    private static void EveryCard()
    {
        int nextSlot = 0;
        foreach (BattleCard card in BattleCatalog.Cards)
        {
            var lobby = new LobbyLoadout
            {
                deck = Roster.Troops.Where(troop => troop != card).Take(Roster.DeckSize).Select(troop => troop.Id).ToArray()
            };
            lobby.Normalize();
            int slot = nextSlot++ % BattleMatch.HandSize;
            string previous = lobby.deck[slot];
            Check(lobby.Reserves().Contains(card.Id));
            Check(lobby.Replace(slot, card.Id));
            string[] reserves = lobby.Reserves();
            Check(reserves.Contains(previous) && !reserves.Contains(card.Id));
            BattleLoadout equipped = lobby.Snapshot();
            BattleLoadout remote = WireLoadout.From(equipped).Validate();
            Check(remote != null && remote.TroopIds[slot] == card.Id);

            var match = new BattleMatch(equipped, remote);
            StepTo(match, BattleMatch.OpeningTicks);
            for (int seat = 0; seat < 2; seat++)
            {
                WireBattleState before = WireBattleState.From(match, seat, new BattleSceneFrame(match), 0);
                Check(before.hand[slot] == card.Id);
                int row = seat == 0 ? 21 : Battlefield.Rows - 1 - 21;
                Vector2 target = Battlefield.Cell(9, row);
                Vector2 direction = seat == 0 ? Vector2.UnitY : -Vector2.UnitY;
                int units = match.Units.Count;
                Check(match.Play(seat, slot, card, target, direction) == PlayResult.Accepted);
                Check(Near(match.Energy(seat), before.energy - card.Cost));
                Check(match.Hand(seat, slot).Id == before.nextCard);
                Check(match.Units.Count == units + (card.IsSpell ? 0 : 1));
            }
        }
    }

    private static void AttackTiming()
    {
        var match = Create("footman");
        BattleUnit unit = Deploy(match, 0, 0, 8, 21);
        BattleUnit enemy = Deploy(match, 1, 0, 8, 9);
        unit.Position = new Vector2(0, -3);
        enemy.Position = new Vector2(0, -1.8f);
        match.Step();
        Check(unit.Pose == BattlePose.Action && unit.ActionContact > match.Tick);
        Check(Near(enemy.Health, 500));
        StepTo(match, unit.ActionContact - 1);
        Check(Near(enemy.Health, 500));
        match.Step();
        Check(Near(enemy.Health, 425));
    }

    private static (BattleMatch Match, BattleUnit Ally, BattleUnit Enemy) Duel(string card, int enemySlot = 2)
    {
        var match = Create(card);
        BattleUnit ally = Deploy(match, 0, 0, 8, 21);
        BattleUnit enemy = Deploy(match, 1, enemySlot, 8, 9);
        ally.Position = new Vector2(0, -4);
        enemy.Position = new Vector2(0, -2.8f);
        return (match, ally, enemy);
    }

    private static void BeastAttacks()
    {
        var (match, ally, enemy) = Duel("bloodthorn-flower");
        bool bloom = false;
        while (ally.SuccessfulHits < 3 && match.Elapsed < 4 * BattleMatch.TickRate)
        {
            match.Step();
            bloom |= ally.Pose == BattlePose.Bloom;
        }
        Check(bloom && enemy.Health == 1480);

        (match, ally, enemy) = Duel("sunder-pangolin");
        match.Step();
        StepTo(match, ally.ActionContact);
        Check(enemy.Health == 1670);

        (match, ally, enemy) = Duel("thunderhorn-rhino", 0);
        match.Step();
        StepTo(match, ally.ActionContact);
        Check(enemy.StunEnd - match.Tick == 12 && enemy.StunImmuneEnd > enemy.StunEnd);
        Check(enemy.Health == 415 && ally.Health == ally.Card.Health - enemy.Card.Power);
    }

    private static void BeastStatuses()
    {
        var (match, ally, enemy) = Duel("venom-toad");
        enemy.Shield = 400;
        match.Step();
        StepTo(match, ally.ActionContact);
        Check(enemy.Health == 1800 && enemy.Shield == 400);
        StepTo(match, ally.ActionContact + ally.Card.FlightTicks);
        Check(enemy.Health == 1800 && enemy.Shield == 360);
        ally.Health = 0;
        StepTo(match, enemy.PoisonNext);
        Check(enemy.Health == 1778 && enemy.Shield == 360);

        match = Create("crystal-armadillo");
        StepTo(match, BattleMatch.OpeningTicks + 239);
        ally = Deploy(match, 0, 0, 8, 21);
        ally.Position = Vector2.Zero;
        ally.Shield = 80;
        match.Step();
        Check(ally.Health == 1092 && ally.Shield == 0 && ally.Pose == BattlePose.ShieldBreak);
        StepTo(match, ally.ShieldReady - 1);
        Check(ally.Shield == 0 && ally.Pose == BattlePose.ShieldRegrow);
        Check((match.Status(ally) & BattleStatus.ShieldDown) != 0);
        match.Step();
        Check(ally.Shield == 400 && ally.Pose == BattlePose.ShieldRegrow);
        Check((match.Status(ally) & BattleStatus.ShieldDown) == 0);
        Check(ally.PoseTick + ActionTiming.Standard.CueTick(ally.PoseLength) == match.Tick);
    }

    private static void BatAwakening()
    {
        var (match, ally, enemy) = Duel("crimsonwing-bat", 0);
        ally.Health = 340;
        match.Step();
        StepTo(match, enemy.ActionContact);
        Check(ally.Awakened && ally.Pose == BattlePose.Awaken && ally.Health == 265);
        enemy.NextAction = match.Tick + 3 * BattleMatch.TickRate;
        StepTo(match, ally.StatusEnd);
        StepTo(match, ally.ActionContact);
        Check(ally.Health == 300);
    }

    private static void ChasePrince()
    {
        foreach (string card in new[] { "footman", "shieldman" })
        {
            var match = Create(card);
            Guard(match, 1, 0).Health = Guard(match, 1, 1).Health = 0;
            Deploy(match, 0, 0, 8, 21);
            StepTo(match, 55 * BattleMatch.TickRate);
            Check(match.Prince(1).Health < 6000);
        }
    }

    private static (BattleMatch Match, BattleUnit Unit, BattleUnit Prince) AtPrinceRange(string card, float margin)
    {
        var match = Create(card);
        foreach (BattleUnit guard in match.Units.Where(unit => unit.GuardSide >= 0))
            guard.Health = 0;
        BattleUnit unit = Deploy(match, 0, 0, 8, 21), prince = match.Prince(1);
        float reach = unit.Card.Range + unit.Card.Radius + prince.Card.Radius;
        unit.Position = prince.Position + (reach - margin) * Vector2.UnitX;
        return (match, unit, prince);
    }

    private static void MeleePursuit()
    {
        foreach (string card in new[] { "footman", "shieldman" })
        {
            var (match, unit, prince) = AtPrinceRange(card, .08f);
            match.Step();
            Check(unit.Pose == BattlePose.Walk);
            bool resumed = false;
            int end = match.Tick + 3 * BattleMatch.TickRate - 1;
            while (match.Tick < end)
            {
                BattlePose before = unit.Pose;
                Vector2 position = unit.Position;
                float walkPhase = unit.WalkPhase, health = prince.Health;
                bool contact = unit.LockedTarget == prince && match.Tick + 1 == unit.ActionContact;
                match.Step();
                Check(unit.Pose == BattlePose.Walk || unit.Pose == BattlePose.Action);
                if (unit.Pose == BattlePose.Action)
                    Check(position == unit.Position);
                else if (before == BattlePose.Action)
                {
                    Check(unit.WalkPhase > walkPhase);
                    resumed = true;
                }
                if (contact)
                    Check(prince.Health < health);
            }
            Check(resumed && unit.SuccessfulHits > 0);
        }
    }

    private static void SprintDodgesMelee()
    {
        var (match, unit, prince) = AtPrinceRange("footman", .65f);
        match.Step();
        Check(unit.Pose == BattlePose.Action && unit.LockedTarget == prince && match.TrySprint(1));
        int start = unit.PoseTick, end = unit.ActionEnd, ready = unit.NextAction;
        float health = prince.Health;
        while (match.Tick + 1 < end)
        {
            match.Step();
            Check(unit.Pose == BattlePose.Action && unit.PoseTick == start && prince.Health == health);
        }
        match.Step();
        Check(unit.Pose == BattlePose.Walk && unit.NextAction == ready && unit.SuccessfulHits == 0);
    }

    private static void Presentation()
    {
        var match = Create("footman");
        var presentation = new BattlePresentation();
        double seconds = match.Tick / (double)BattleMatch.TickRate;
        presentation.Reset(match, seconds);
        Check(presentation.Actors.Count == 6);
        BattleUnit pawn = Deploy(match, 0, 0, 8, 21);
        for (int i = 0; i < 6; i++)
        {
            match.Step();
            seconds = match.Tick / (double)BattleMatch.TickRate;
            presentation.Publish(match, seconds);
            presentation.Advance(seconds);
        }
        Check(presentation.Actors.ContainsKey(pawn.Key));
        Check(presentation.RenderTick < match.Tick);
        Vector2 at = presentation.Position(presentation.Actors[pawn.Key], out BattleActorFrame pose);
        Check(at.Y < pawn.Position.Y && pose.Pose == BattlePose.Walk);
        pawn.Health = 0;
        match.Step();
        presentation.Publish(match, seconds + 1d / BattleMatch.TickRate);
        double finalTime = seconds + 1d / BattleMatch.TickRate;
        presentation.Finish(finalTime);
        presentation.Advance(finalTime + .10);
        Check(!presentation.Actors.ContainsKey(pawn.Key) && !presentation.EndAnimationComplete);
        presentation.Advance(finalTime + 1);
        Check(presentation.EndAnimationComplete);
    }

    private static void StridePresentation()
    {
        const double tickSeconds = 1d / BattleMatch.TickRate;
        foreach (bool shortStride in new[] { false, true })
        {
            var match = Create("footman");
            BattleUnit unit = Deploy(match, 0, 0, 8, 24);
            Vector2 start = unit.Position;
            var presentation = new BattlePresentation();
            presentation.Reset(match, 0);
            match.Step();
            if (shortStride)
                unit.StunEnd = match.Tick + 2;
            match.Step();
            Check(unit.Pose == (shortStride ? BattlePose.Idle : BattlePose.Walk));
            presentation.Publish(match, 2 * tickSeconds);

            presentation.Advance(2.5 * tickSeconds);
            BattleActorSample first = presentation.Sample(presentation.Actors[unit.Key]);
            Check(first.Position.Y > start.Y && first.Pose == BattlePose.Walk && first.Elapsed > 0 && first.Heading.Y > 0);
            presentation.Advance(3.5 * tickSeconds);
            BattleActorSample second = presentation.Sample(presentation.Actors[unit.Key]);
            Check(second.Pose == BattlePose.Walk && second.Position.Y > first.Position.Y && second.Elapsed > first.Elapsed);
        }

        var pushed = Create("footman");
        BattleUnit pawn = Deploy(pushed, 0, 0, 8, 24);
        var pushView = new BattlePresentation();
        pushView.Reset(pushed, 0);
        pawn.StunEnd = pushed.Tick + 5;
        StepTo(pushed, pushed.Tick + 2);
        pawn.StunEnd = 0;
        pawn.Position += Vector2.UnitX * .2f;
        pushView.Publish(pushed, 2 * tickSeconds);
        pushView.Advance(3 * tickSeconds);
        BattleActorSample push = pushView.Sample(pushView.Actors[pawn.Key]);
        Check(push.Pose == BattlePose.Idle && push.Position.X < pawn.Position.X && pawn.WalkPhase == 0);

        var recovering = Create("footman");
        BattleUnit fighter = Deploy(recovering, 0, 0, 8, 24);
        fighter.SetPose(BattlePose.Action, recovering.Tick, 1);
        fighter.ActionEnd = recovering.Tick + 1;
        var attackView = new BattlePresentation();
        attackView.Reset(recovering, 0);
        StepTo(recovering, recovering.Tick + 2);
        attackView.Publish(recovering, 2 * tickSeconds);
        attackView.Advance(2.5 * tickSeconds);
        Check(attackView.Sample(attackView.Actors[fighter.Key]).Pose == BattlePose.Action);
    }

    private static BattleLoadout Loadout(string card, RoyalGuard left = RoyalGuard.Spearman, RoyalGuard right = RoyalGuard.Crossbowman)
    {
        string[] deck = new[] { card }.Concat(BattleCatalog.Cards.Select(item => item.Id)).Distinct().Take(Roster.DeckSize).ToArray();
        return new BattleLoadout(left, right, deck);
    }

    private static BattleMatch Both(string first, string second)
    {
        var match = new BattleMatch(Loadout(first), Loadout(second));
        StepTo(match, BattleMatch.OpeningTicks);
        return match;
    }

    private static void GuardDetectionEdges()
    {
        var match = Create();
        BattleUnit guard = Guard(match, 0, 0);
        BattleUnit enemy = Deploy(match, 1, 0, 0, 9);
        guard.Position = guard.Home - Vector2.UnitY * 4;
        enemy.Position = guard.Home + Vector2.UnitY * 5.5f;
        match.Step();
        Check(guard.Pose == BattlePose.Walk && guard.Position.Y > guard.Home.Y - 4);

        match = Create();
        guard = Guard(match, 0, 0);
        enemy = Deploy(match, 1, 1, 0, 9);
        enemy.Position = guard.Home + Vector2.UnitY * 6.3f;
        match.Step();
        Check(guard.LockedTarget == enemy && enemy.LockedTarget == guard);
    }

    private static void SprintCost()
    {
        var match = Create("meteorfall", "footman");
        Check(match.TrySprint(0) && Near(match.Energy(0), 5 - BattleMatch.SprintCost));
        Check(!match.TrySprint(0) && Near(match.Energy(0), 5 - BattleMatch.SprintCost));
        int ready = match.Tick + match.SprintTicksLeft(0);
        StepTo(match, ready);
        Check(match.CanSprint(0));

        match = Create("meteorfall");
        Check(match.Play(0, 0, match.Hand(0, 0), Vector2.Zero, Vector2.UnitY) == PlayResult.Accepted);
        Check(Near(match.Energy(0), 1) && !match.TrySprint(0) && match.SprintTicksLeft(0) == 0);
    }

    private static void Overtime()
    {
        var match = Create("meteorfall");
        int boundary = BattleMatch.OpeningTicks + BattleMatch.RegulationTicks;
        StepTo(match, boundary - 90);
        Check(match.Play(0, 0, match.Hand(0, 0), Vector2.Zero, Vector2.UnitY) == PlayResult.Accepted);
        StepTo(match, boundary);
        Check(match.Live && match.Overtime && match.PhaseTicksLeft == BattleMatch.OvertimeTicks);
        Check(Near(match.Energy(0), 7));
        StepTo(match, boundary + 45);
        Check(Near(match.Energy(0), 8));

        match.Prince(0).Health = 5900;
        StepTo(match, boundary + BattleMatch.OvertimeTicks - 1);
        Check(match.Live && match.PhaseTicksLeft == 1);
        match.Step();
        Check(match.Winner == 1);

        match = Create();
        StepTo(match, BattleMatch.OpeningTicks + BattleMatch.DurationTicks);
        Check(match.Winner == BattleMatch.Draw);
    }

    private static void SameTick()
    {
        foreach (bool secondFirst in new[] { false, true })
        {
            var match = Both("footman", "footman");
            int firstSeat = secondFirst ? 1 : 0;
            BattleUnit a = Deploy(match, firstSeat, 0, 8, firstSeat == 0 ? 21 : 9);
            BattleUnit b = Deploy(match, 1 - firstSeat, 0, 8, firstSeat == 0 ? 9 : 21);
            a.Position = new Vector2(0, -4.2f);
            b.Position = new Vector2(0, -3);
            a.Health = b.Health = 75;
            match.Step();
            Check(a.ActionContact == b.ActionContact);
            StepTo(match, a.ActionContact);
            Check(!a.Alive && !b.Alive);
        }

        var both = Both("thunder-judgment", "thunder-judgment");
        both.Prince(0).Health = both.Prince(1).Health = 60;
        foreach (int seat in new[] { 0, 1 })
            Check(both.Play(seat, 0, both.Hand(seat, 0), both.Prince(1 - seat).Position, Vector2.UnitY) == PlayResult.Accepted);
        Check(both.Live);
        both.Step();
        Check(both.Winner == BattleMatch.Draw && both.Prince(0).Health == 0 && both.Prince(1).Health == 0);
    }

    private static void RiftAim()
    {
        var match = Create("earth-rift");
        var aim = new BattleAim(0, match.Hand(0, 0));
        var origin = new Vector2(0, -6);
        var release = new Vector2(2, -3);
        aim.Update(origin, Vector2.UnitY, true);
        Check(aim.HasTarget && aim.Point == origin);
        aim.Update(new Vector2(0, -20), Vector2.UnitY, true);
        Check(!aim.HasTarget && match.Hand(0, 0) == aim.Card && Near(match.Energy(0), 5));

        aim.Update(release, Vector2.UnitY, true);
        Check(aim.HasTarget && aim.Point == origin && aim.Direction == Vector2.Normalize(release - origin));
        aim.Reset();
        Check(!aim.HasTarget);
        aim.Update(release, Vector2.UnitY, false);
        aim.Update(new Vector2(0, -20), Vector2.UnitY, true);
        Check(!aim.HasTarget);
        aim.Update(release, Vector2.UnitY, true);
        Check(aim.Point == release && aim.Direction == Vector2.UnitY);
        Check(match.Play(0, aim.Slot, aim.Card, aim.Point, aim.Direction) == PlayResult.Accepted);
        Check(Near(match.Energy(0), 5 - aim.Card.Cost));
    }

    private static void Projectile()
    {
        var match = Both("bow-recruit", "footman");
        BattleUnit arrow = Deploy(match, 0, 0, 8, 21);
        BattleUnit target = Deploy(match, 1, 0, 8, 9);
        arrow.Position = new Vector2(0, -6);
        target.Position = new Vector2(0, -.6f);

        var presentation = new BattlePresentation();
        presentation.Reset(match, match.Tick / 30d);
        while (arrow.ActionContact == 0 || match.Tick < arrow.ActionContact + arrow.Card.FlightTicks)
        {
            match.Step();
            double seconds = match.Tick / 30d;
            if (match.PresentationChanged || match.Tick % 2 == 0)
                presentation.Publish(match, seconds);
            presentation.Advance(seconds);

            if (match.Tick == arrow.ActionContact)
            {
                BattleEffect fx = match.Effects.Single(effect => effect.Kind == EffectKind.Projectile);
                Check(target.Health == 500 && fx.TrackingKey == target.Key && fx.End == match.Tick + arrow.Card.FlightTicks);
            }
        }

        Check(target.Health == 450 && target.LastHitTick == match.Tick);
        Check(match.Effects.Any(effect => effect.Kind == EffectKind.Point && effect.Begin == match.Tick));
        presentation.Advance((match.Tick + 2) / 30d);
        Check(Near(presentation.Actors[target.Key].Health, .9) && presentation.Visible.Tick == match.Tick);
    }

    private static void ProjectileTargetDeath()
    {
        foreach (string card in new[] { "cannoneer", "flame-mage" })
        {
            var match = Both(card, "light-cavalry");
            BattleUnit shooter = Deploy(match, 0, 0, 8, 21);
            BattleUnit target = Deploy(match, 1, 0, 8, 9);
            shooter.Position = new Vector2(0, -6);
            target.Position = new Vector2(0, -2);
            target.StunEnd = match.Tick + 120;
            match.Step();
            StepTo(match, shooter.ActionContact);
            BattleEffect shot = match.Effects.Single(effect => effect.Kind == EffectKind.Projectile && effect.Skill == shooter.Card.Skill);
            Check(shot.TrackingKey == target.Key);

            var presentation = new BattlePresentation();
            presentation.Reset(match, match.Tick / 30d);
            target.Position += new Vector2(.2f, .3f);
            match.Step();
            presentation.Publish(match, match.Tick / 30d);
            presentation.Advance((match.Tick + 2) / 30d);
            Check(presentation.ProjectileTarget(shot, match.Effects) == target.Position);

            target.Position += new Vector2(.2f, .3f);
            target.Health = 0;
            match.Step();
            BattleEffect removed = match.Effects.Single(effect => effect.Kind == EffectKind.Death && effect.TrackingKey == target.Key);
            BattleEffect deathOnClient = WireBattleEffect.From(removed).Read();
            presentation.Publish(match, match.Tick / 30d);
            presentation.Advance((match.Tick + 2) / 30d);
            Check(!presentation.Actors.ContainsKey(target.Key));
            Vector2 endpoint = presentation.ProjectileTarget(shot, new[] { deathOnClient });
            Check(endpoint == target.Position && endpoint != shot.Target);

            StepTo(match, shot.End);
            BattleEffect blast = match.Effects.Single(effect => effect.Kind == EffectKind.Area && effect.Skill == shot.Skill);
            Check(blast.Begin == shot.End && blast.Target == endpoint);
        }
    }

    private static void SupportAndCone()
    {
        var match = Create("field-medic");
        BattleUnit medic = Deploy(match, 0, 0, 3, 23);
        BattleUnit guard = Guard(match, 0, 0);
        Vector2 before = medic.Position;
        match.Step();
        Check(medic.Position == before && medic.Pose == BattlePose.Idle);
        guard.Health -= 100;
        match.Step();
        Check(medic.LockedTarget == guard);
        StepTo(match, medic.ActionContact);
        Check(guard.Health == guard.Card.Health - 100);
        StepTo(match, medic.ActionContact + medic.Card.FlightTicks);
        Check(guard.Health == guard.Card.Health - 30);

        match = Create("standard-bearer");
        BattleUnit banner = Deploy(match, 0, 0, 8, 25);
        guard = Guard(match, 0, 0);
        banner.Position = guard.Home + new Vector2(3.25f, 0);
        match.Step();
        Check(banner.Pose == BattlePose.Walk && (match.Status(guard) & BattleStatus.Rally) == 0);
        banner.Position = guard.Home + new Vector2(2.95f, 0);
        match.Step();
        Check(banner.Pose == BattlePose.Action && (match.Status(guard) & BattleStatus.Rally) != 0);

        var (duel, axe, foe) = Duel("axe-guard", 0);
        foe.NextAction = duel.Tick + 1000;
        duel.Step();
        foe.Position = axe.Position + new Vector2(1.2f, 0);
        StepTo(duel, axe.ActionContact);
        Check(axe.SuccessfulHits == 0 && foe.Health == 500 && !duel.Effects.Any(fx => fx.Skill == Skill.Axe));
    }

    private static void SimultaneousStatus()
    {
        foreach (string card in new[] { "crystal-armadillo", "crimsonwing-bat" })
        {
            var match = Both("thunderhorn-rhino", card);
            BattleUnit rhino = Deploy(match, 0, 0, 8, 21);
            BattleUnit target = Deploy(match, 1, 0, 8, 9);
            bool tank = card == "crystal-armadillo";
            if (tank) target.Shield = 80;
            else target.Health = 340;
            rhino.Position = new Vector2(0, -4);
            target.Position = new Vector2(0, -2.8f);
            match.Step();
            StepTo(match, rhino.ActionContact);
            BattlePose expected = tank ? BattlePose.ShieldBreak : BattlePose.Awaken;
            BattleStatus status = tank ? BattleStatus.ShieldDown : BattleStatus.Awakened;
            Check(target.Pose == expected && (match.Status(target) & (status | BattleStatus.Stun)) == (status | BattleStatus.Stun));
            StepTo(match, target.StunEnd + 1);
            Check(target.Pose == expected && (match.Status(target) & status) != 0);
        }
    }

    private static void PairedSeats()
    {
        var match = new BattleMatch(Loadout("footman"), Loadout("meteorfall", RoyalGuard.Crossbowman, RoyalGuard.Spearman));
        StepTo(match, BattleMatch.OpeningTicks);
        Check(match.Hand(0, 0).Id == "footman" && match.Hand(1, 0).Id == "meteorfall");
        Check(Guard(match, 1, 1).Card == BattleCatalog.Crossbowman && Guard(match, 1, 0).Card == BattleCatalog.Spearman);
        var frame = new BattleSceneFrame(match);
        WireBattleState first = WireBattleState.From(match, 0, frame, 7), second = WireBattleState.From(match, 1, frame, 11);
        Check(first.hand[0] == "footman" && second.hand[0] == "meteorfall");
        Check(first.processedSeq == 7 && second.processedSeq == 11);
        Check(first.actors.Select(actor => actor.id).SequenceEqual(second.actors.Select(actor => actor.id)));

        StepTo(match, BattleMatch.OpeningTicks + 90);
        Check(match.Units.Count == 6);
        match.Forfeit(1);
        Check(match.Winner == 0);
    }

    private static void SameTickForfeit()
    {
        var playing = new WireBattleState { tick = 72, winner = BattleMatch.Playing };
        var final = new WireBattleState { tick = 72, winner = 1 };
        Check(!playing.Follows(playing) && final.Follows(playing) && !playing.Follows(final));
        final.tick--;
        Check(!final.Follows(playing));
    }

    private static void PendingEnergy()
    {
        var pending = new PendingBattleCommands();
        pending.Add(1, 0, 2);
        Check(Near(pending.Energy(5), 3) && pending.HasSlot(0));
        pending.Advance(1);
        Check(Near(pending.Energy(3), 3) && !pending.HasSlot(0));
        pending.Add(2, 1, 2);
        Check(pending.Acknowledge(1, PlayResult.Accepted, out int first) && first == 0);
        Check(pending.Acknowledge(2, PlayResult.Accepted, out int second) && second == 1);
        Check(Near(pending.Energy(3), 1) && pending.HasSlot(1));
        pending.Advance(2);
        Check(Near(pending.Energy(1), 1) && !pending.HasSlot(1));
        Check(!pending.Acknowledge(2, PlayResult.Accepted, out _));

        pending.Add(3, -1, 2);
        Check(Near(pending.Energy(4), 2) && pending.HasSlot(-1));
        Check(pending.Acknowledge(3, PlayResult.Territory, out int rejected) && rejected == -1);
        Check(Near(pending.Energy(4), 4) && !pending.HasSlot(-1));
        pending.Advance(3);
        Check(Near(pending.Energy(4), 4));
    }

    private static void CompleteMatch()
    {
        var chosen = new LobbyLoadout
        {
            deck = new[] { "footman", "light-cavalry", "shieldman", "thunder-judgment",
                "cannoneer", "crimsonwing-bat", "venom-toad", "meteorfall" }
        };
        chosen.Normalize();
        var match = new BattleMatch(chosen.Snapshot());
        int placed = 0;
        int botUnits = 0;
        var seen = new HashSet<string>();
        while (match.Winner == BattleMatch.Playing)
        {
            match.Step();
            foreach (BattleUnit unit in match.Units)
                if (unit.Seat == 1 && !unit.IsPrince && unit.GuardSide < 0 && seen.Add(unit.Key))
                    botUnits++;
            if (!match.Live || match.Tick % BattleMatch.TickRate != 0)
                continue;

            int lane = placed % 3;
            for (int slot = 0; slot < BattleMatch.HandSize; slot++)
            {
                BattleCard card = match.Hand(0, slot);
                Vector2 point = card.IsSpell ? match.Prince(1).Position : Battlefield.Cell(2 + lane * 6, 21);
                if (match.Play(0, slot, card, point, Vector2.UnitY) != PlayResult.Accepted)
                    continue;
                placed++;
                break;
            }
        }

        Check(placed >= 10 && botUnits >= 10);
        Check(match.Tick <= BattleMatch.OpeningTicks + BattleMatch.DurationTicks);
        Check(match.Prince(0).Health < 6000 && match.Prince(1).Health < 6000);
        Console.WriteLine($"      {match.Elapsed / (double)BattleMatch.TickRate:0.0}s, player cards {placed}, AI units {botUnits}, winner {match.Winner}");
    }
}
