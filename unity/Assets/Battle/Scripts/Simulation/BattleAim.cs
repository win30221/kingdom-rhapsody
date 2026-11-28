using System.Numerics;

namespace KingdomRhapsody
{
    internal sealed class BattleAim
    {
        internal readonly int Slot;
        internal readonly BattleCard Card;
        private bool anchored;
        private Vector2 origin;
        internal bool HasTarget { get; private set; }
        internal Vector2 Point { get; private set; }
        internal Vector2 Direction { get; private set; }

        internal BattleAim(int slot, BattleCard card)
        {
            Slot = slot;
            Card = card;
        }

        internal void Update(Vector2 location, Vector2 forward, bool anchorRift)
        {
            HasTarget = Battlefield.Contains(location);
            if (!HasTarget)
                return;

            Direction = forward;
            if (Card.Skill == Skill.Rift && anchorRift)
            {
                if (!anchored)
                {
                    anchored = true;
                    origin = location;
                }
                Vector2 heading = location - origin;
                if (heading.LengthSquared() >= .04f)
                    Direction = Vector2.Normalize(heading);
                Point = origin;
                return;
            }

            Point = Card.IsSpell ? location : Battlefield.Cell(Battlefield.Column(location), Battlefield.Row(location));
        }

        internal void Reset()
        {
            anchored = false;
            HasTarget = false;
        }
    }
}
