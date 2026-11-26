using System;
using System.Collections.Generic;

namespace KingdomRhapsody
{
    public enum RoyalGuard { Spearman, Crossbowman }

    public sealed class BattleLoadout
    {
        public RoyalGuard LeftGuard { get; }
        public RoyalGuard RightGuard { get; }
        public IReadOnlyList<string> TroopIds { get; }

        internal BattleLoadout(RoyalGuard leftGuard, RoyalGuard rightGuard, string[] deck)
        {
            LeftGuard = leftGuard;
            RightGuard = rightGuard;
            TroopIds = Array.AsReadOnly((string[])deck.Clone());
        }
    }

    internal static class Roster
    {
        internal const int DeckSize = 8;
        internal static readonly (string id, string name)[] Troops =
        {
            ("shieldman", "盾兵"),
            ("bow-recruit", "新手弓手"),
            ("cannoneer", "火炮槍兵"),
            ("footman", "近戰小兵"),
            ("hammer-guard", "重鎚兵"),
            ("axe-guard", "巨斧兵"),
            ("frost-mage", "冰霜術士"),
            ("flame-mage", "火焰術士"),
            ("standard-bearer", "軍旗手"),
            ("field-medic", "醫官"),
            ("light-cavalry", "輕騎兵")
        };

        internal static string Name(string id)
        {
            foreach (var troop in Troops)
                if (troop.id == id)
                    return troop.name;
            return "";
        }

        internal static string Name(RoyalGuard guard) => guard == RoyalGuard.Spearman ? "長槍兵" : "弩手";
        internal static string SpriteId(RoyalGuard guard) => guard == RoyalGuard.Spearman ? "royal-spearman" : "royal-crossbowman";
    }

    [Serializable]
    internal sealed class LobbyLoadout
    {
        public RoyalGuard leftGuard = RoyalGuard.Spearman;
        public RoyalGuard rightGuard = RoyalGuard.Crossbowman;
        public bool soundEnabled = true;
        public string[] deck;

        internal void Normalize()
        {
            if (leftGuard != RoyalGuard.Spearman && leftGuard != RoyalGuard.Crossbowman)
                leftGuard = RoyalGuard.Spearman;
            if (rightGuard != RoyalGuard.Spearman && rightGuard != RoyalGuard.Crossbowman)
                rightGuard = RoyalGuard.Crossbowman;

            var accepted = new List<string>(Roster.DeckSize);
            foreach (string id in deck ?? Array.Empty<string>())
            {
                if (accepted.Count == Roster.DeckSize)
                    break;
                if (Roster.Name(id).Length > 0 && !accepted.Contains(id))
                    accepted.Add(id);
            }

            foreach (var troop in Roster.Troops)
            {
                if (accepted.Count == Roster.DeckSize)
                    break;
                if (!accepted.Contains(troop.id))
                    accepted.Add(troop.id);
            }
            deck = accepted.ToArray();
        }

        internal string[] Reserves()
        {
            var result = new List<string>();
            foreach (var troop in Roster.Troops)
                if (Array.IndexOf(deck, troop.id) < 0)
                    result.Add(troop.id);
            return result.ToArray();
        }

        internal bool CanReplace(int slot, string id) =>
            slot >= 0 && slot < deck.Length && Roster.Name(id).Length > 0 && Array.IndexOf(deck, id) < 0;

        internal bool Replace(int slot, string id)
        {
            if (!CanReplace(slot, id))
                return false;
            deck[slot] = id;
            return true;
        }

        internal BattleLoadout Snapshot() => new BattleLoadout(leftGuard, rightGuard, deck);
    }
}
