using System;
using System.Collections.Generic;
using System.Numerics;

namespace KingdomRhapsody
{
    internal abstract class BattleSession
    {
        internal abstract int Seat { get; }
        internal abstract int Tick { get; }
        internal abstract int Winner { get; }
        internal abstract int Territory { get; }
        internal abstract int SprintTicksLeft { get; }
        internal abstract bool CanSprint { get; }
        internal abstract double Energy { get; }
        internal abstract IReadOnlyList<BattleEffect> Effects { get; }
        internal abstract BattleCard NextCard { get; }
        internal abstract BattleCard Hand(int slot);
        internal abstract PlayResult Validate(int slot, BattleCard card, Vector2 point);
        internal abstract PlayResult Play(int slot, BattleCard card, Vector2 point, Vector2 direction);
        internal abstract bool Sprint();

        internal int Elapsed => Math.Max(0, Tick - BattleMatch.OpeningTicks);
        internal bool Overtime => Elapsed >= BattleMatch.RegulationTicks;
        internal int PhaseTicksLeft => Math.Max(0, (Overtime ? BattleMatch.DurationTicks : BattleMatch.RegulationTicks) - Elapsed);
        internal bool Live => Tick >= BattleMatch.OpeningTicks && Winner == BattleMatch.Playing;
        internal Vector2 Forward => Seat == 0 ? Vector2.UnitY : -Vector2.UnitY;

        internal static bool MayDeploy(int territory, int seat, int column, int row) =>
            Battlefield.MayDeploy(seat, column, row, (territory & 1 << (seat * 2)) != 0, (territory & 2 << (seat * 2)) != 0);
    }

    internal sealed class PracticeSession : BattleSession
    {
        internal readonly BattleMatch Match;

        internal PracticeSession(BattleMatch match) => Match = match;

        internal override int Seat => 0;
        internal override int Tick => Match.Tick;
        internal override int Winner => Match.Winner;
        internal override int Territory => Match.TerritoryMask;
        internal override int SprintTicksLeft => Match.SprintTicksLeft(Seat);
        internal override bool CanSprint => Match.CanSprint(Seat);
        internal override double Energy => Match.Energy(Seat);
        internal override IReadOnlyList<BattleEffect> Effects => Match.Effects;
        internal override BattleCard NextCard => Match.NextCard(Seat);
        internal override BattleCard Hand(int slot) => Match.Hand(Seat, slot);
        internal override PlayResult Validate(int slot, BattleCard card, Vector2 point) => Match.Validate(Seat, slot, card, point);
        internal override PlayResult Play(int slot, BattleCard card, Vector2 point, Vector2 direction) =>
            Match.Play(Seat, slot, card, point, direction);
        internal override bool Sprint() => Match.TrySprint(Seat);
    }
}
