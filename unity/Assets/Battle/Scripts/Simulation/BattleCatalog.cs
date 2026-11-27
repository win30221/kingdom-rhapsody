using System;
using System.Collections.Generic;

namespace KingdomRhapsody
{
    internal enum Skill
    {
        Strike, Cannon, Hammer, Axe, Frost, Flame, Rally, Heal, Charge, Bloom,
        Shield, Poison, Lifesteal, Stun, Sunder, Meteor, Thunder, Rift
    }

    internal sealed class BattleCard
    {
        internal readonly string Id, Name, Action;
        internal readonly int Cost;
        internal readonly float Health, Power, Interval, Range, Speed, Radius;
        internal readonly Skill Skill;
        internal bool IsSpell => Health == 0;
        internal bool IsSupport => Skill == Skill.Rally || Skill == Skill.Heal;

        internal BattleCard(string id, string name, string action, int cost, float health, float power,
            float interval, float range, float speed, Skill skill = Skill.Strike, float radius = .33f)
        {
            Id = id;
            Name = name;
            Action = action;
            Cost = cost;
            Health = health;
            Power = power;
            Interval = interval;
            Range = range;
            Speed = speed;
            Skill = skill;
            Radius = radius;
        }

        internal BattleCard(string id, string name, string action, int cost, float power, float range, Skill skill)
            : this(id, name, action, cost, 0, power, 0, range, 0, skill, 0) { }
    }

    internal static class BattleCatalog
    {
        // card-balance-v0.1.md. Keep the first eight IDs in their existing save/default order.
        internal static readonly BattleCard[] Cards =
        {
            new("shieldman", "盾兵", "盾擊", 4, 1800, 50, 1.4f, 1, 1.8f, radius: .42f),
            new("bow-recruit", "新手弓手", "射箭", 2, 420, 50, .8f, 6, 2.5f),
            new("cannoneer", "火炮槍兵", "舉炮與後坐", 4, 650, 150, 2, 5.5f, 2, Skill.Cannon),
            new("footman", "近戰小兵", "短劍揮砍", 2, 500, 75, 1, 1, 3),
            new("hammer-guard", "重鎚兵", "重鎚砸地", 4, 1000, 155, 1.6f, 1.2f, 2, Skill.Hammer, .42f),
            new("axe-guard", "巨斧兵", "橫掃", 3, 850, 100, 1.25f, 1.5f, 2.5f, Skill.Axe),
            new("frost-mage", "冰霜術士", "冰霜施法", 3, 550, 55, 1.25f, 5, 2.5f, Skill.Frost),
            new("flame-mage", "火焰術士", "火焰施法", 4, 600, 90, 1, 4.5f, 2.5f, Skill.Flame),
            new("standard-bearer", "軍旗手", "舉旗", 3, 700, 0, 2, 3, 2.5f, Skill.Rally),
            new("field-medic", "醫官", "遞出藥瓶", 3, 500, 70, 1.5f, 4, 2.5f, Skill.Heal),
            new("light-cavalry", "輕騎兵", "衝鋒", 3, 800, 95, 1.05f, 1.2f, 4.2f, Skill.Charge, .57f),
            new("bloodthorn-flower", "血棘花妖", "藤蔓鞭擊", 3, 750, 80, .9f, 1.2f, 2.5f, Skill.Bloom),
            new("crystal-armadillo", "晶甲犰狳", "滾動衝撞", 4, 1100, 45, 1.5f, 1, 1.8f, Skill.Shield, .55f),
            new("venom-toad", "毒囊蟾蜍", "毒液噴射", 3, 550, 40, 1.5f, 5, 2.5f, Skill.Poison),
            new("crimsonwing-bat", "赤翼蝠獸", "撲咬", 3, 650, 55, .65f, 1, 3.8f, Skill.Lifesteal),
            new("thunderhorn-rhino", "雷角犀牛", "雷角衝撞", 4, 1100, 85, 1.8f, 1.2f, 2.5f, Skill.Stun, .57f),
            new("sunder-pangolin", "裂甲穿山獸", "裂甲爪擊", 3, 900, 40, .9f, 1, 2.5f, Skill.Sunder),
            new("meteorfall", "隕星墜落", "延遲圓形傷害", 4, 300, 2.4f, Skill.Meteor),
            new("thunder-judgment", "雷霆裁決", "最大血量比例傷害", 3, .2f, 2, Skill.Thunder),
            new("earth-rift", "裂地波動", "定向直線傷害", 3, 240, 8, Skill.Rift)
        };

        internal static readonly BattleCard Prince = new("prince", "王子", "", 0, 6000, 0, 0, 0, 1.2f, radius: .36f);
        internal static readonly BattleCard Spearman = new("royal-spearman", "長槍兵", "長槍突刺", 0, 1400, 110, 1, 1.8f, 3);
        internal static readonly BattleCard Crossbowman = new("royal-crossbowman", "弩手", "弩箭射擊", 0, 1000, 165, 1.5f, 6, 2.8f);

        private static readonly Dictionary<string, BattleCard> byId = BuildIndex();

        internal static BattleCard Find(string id) => id != null && byId.TryGetValue(id, out var card) ? card : null;

        internal static BattleCard Guard(RoyalGuard guard) => guard == RoyalGuard.Spearman ? Spearman : Crossbowman;

        private static Dictionary<string, BattleCard> BuildIndex()
        {
            var index = new Dictionary<string, BattleCard>(Cards.Length, StringComparer.Ordinal);
            foreach (BattleCard card in Cards)
                index.Add(card.Id, card);
            return index;
        }
    }
}
