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
        Run("90 / 162 / 234 grid cells and overlapping claims", Territory);
        Run("deployment rejection leaves energy and hand unchanged", Deployment);
        Run("guard detection, leash and return", GuardReturn);
        Run("guard aggro checks the home radius and weapon edges", GuardDetectionEdges);
        Run("patrol, sprint duration and cooldown", Patrol);
        Run("sprint spends energy once and rejects without charge", SprintCost);
        Run("regulation, overtime energy and final health comparison", Overtime);
        Run("same-tick contact and royal knockout are simultaneous", SameTick);
        Run("both teams take central hazard damage at 8 s", Hazard);
        Run("three spell shapes, timing and prince damage", Spells);
        Run("all cards can be played from a legal hand", EveryCard);
        Run("melee damage is applied on the contact tick", AttackTiming);
        Run("projectile launches first then damage and presentation agree", Projectile);
        Run("healer escort, rally range and axe miss", SupportAndCone);
        Run("flower critical, pangolin damage and rhino stun", BeastAttacks);
        Run("poison pierces a shield and armadillo shield returns", BeastStatuses);
        Run("bat awakens below half health and heals on hit", BatAwakening);
        Run("shield break and awakening still play while stunned", SimultaneousStatus);
        Run("footman and shieldman catch a patrolling prince", ChasePrince);
        Run("GameNetKit presents movement and removes defeated units", Presentation);
        Run("paired seats own independent decks, guards and commands", PairedSeats);
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
        Check(GridCount(match, 0) == 90 && GridCount(match, 1) == 90);
        Guard(match, 1, 0).Health = 0;
        Check(GridCount(match, 0) == 162 && GridCount(match, 1) == 90);
        Check(match.MayDeploy(0, 3, 9) && match.MayDeploy(1, 3, 9));
        BattleCard next = match.Hand(1, 0);
        Deploy(match, 0, 0, 3, 9);
        Check(match.Play(1, 0, next, Battlefield.Cell(3, 9), -Vector2.UnitY) == PlayResult.Occupied);
        Check(Near(match.Energy(1), 5) && match.Hand(1, 0) == next);
        Guard(match, 1, 1).Health = 0;
        Guard(match, 0, 0).Health = 0;
        Guard(match, 0, 1).Health = 0;
        Check(GridCount(match, 0) == 234 && GridCount(match, 1) == 234);
        Check(!match.MayDeploy(0, 8, 16) && !match.MayDeploy(0, 0, 3) && match.MayDeploy(0, 17, 20));
        int overlap = 0;
        for (int row = 0; row < Battlefield.Rows; row++)
            for (int col = 0; col < Battlefield.Columns; col++)
                if (match.MayDeploy(0, col, row) && match.MayDeploy(1, col, row))
                    overlap++;
        Check(overlap == 168);
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
        foreach (BattleCard card in BattleCatalog.Cards)
        {
            var match = Create(card.Id);
            Check(match.Play(0, 0, card, Battlefield.Cell(9, 21), Vector2.UnitY) == PlayResult.Accepted);
            Check(Near(match.Energy(0), 5 - card.Cost));
            Check(match.Units.Count == (card.IsSpell ? 6 : 7));
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

    private static void SupportAndCone()
    {
        var match = Create("field-medic");
        BattleUnit medic = Deploy(match, 0, 0, 3, 25);
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
        WireBattleState first = WireBattleState.From(match, 0, frame), second = WireBattleState.From(match, 1, frame);
        Check(first.hand[0] == "footman" && second.hand[0] == "meteorfall");
        Check(first.actors.Select(actor => actor.id).SequenceEqual(second.actors.Select(actor => actor.id)));

        StepTo(match, BattleMatch.OpeningTicks + 90);
        Check(match.Units.Count == 6);
        match.Forfeit(1);
        Check(match.Winner == 0);
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
