using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPoint = System.Numerics.Vector2;

namespace KingdomRhapsody
{
    internal sealed class BattlefieldView : VisualElement
    {
        internal const float Tile = 27.5f;
        internal const float Width = Battlefield.Columns * Tile, Height = Battlefield.Rows * Tile;
        private static readonly Color Friendly = new Color32(88, 193, 244, 255);
        private static readonly Color Enemy = new Color32(244, 120, 109, 255);
        private static readonly Color Crystal = new Color32(149, 224, 243, 255);
        private static readonly Color Gold = new Color32(233, 174, 97, 255);
        private static readonly Color GhostAllowed = new Color32(129, 248, 230, 220);
        private static readonly Color GhostDenied = new Color32(255, 108, 104, 235);

        private sealed class Pawn
        {
            internal readonly VisualElement Root, Art, Health, Shield;
            internal readonly BattleActorArt Model;
            internal Sprite Frame;
            internal int Depth;

            internal Pawn(BattleActorArt model, BattleActorFrame actor, Sprite shadow)
            {
                Model = model;
                Root = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform, userData = this };
                Root.style.position = Position.Absolute;
                float size = (model ? model.worldSize : 2f) * Tile;
                var mark = BattleView.Box(actor.IsPrince || actor.IsGuard ? -17 : -11, -5,
                    actor.IsPrince || actor.IsGuard ? 34 : 22, 10);
                mark.style.backgroundColor = Alpha(actor.Seat == 0 ? Friendly : Enemy, .35f);
                BattleView.Round(mark, 7);
                Root.Add(mark);
                var ground = BattleView.Box(-size * .38f, -size * .12f, size * .76f, size * .25f);
                BattleView.Image(ground, shadow);
                Root.Add(ground);
                Art = BattleView.Box(-size * .5f, -size * (1 - (model ? model.pivot.y : .2f)), size, size);
                Root.Add(Art);
                float barWidth = actor.IsPrince ? 40 : actor.IsGuard ? 33 : 25;
                var meter = BattleView.Box(-barWidth / 2, -size * .69f, barWidth, actor.IsPrince ? 5 : 3);
                meter.style.backgroundColor = (Color)new Color32(14, 25, 27, 245);
                BattleView.Round(meter, 3);
                Root.Add(meter);
                Health = BattleView.Box(0, 0, barWidth, actor.IsPrince ? 5 : 3);
                Health.style.backgroundColor = actor.Seat == 0 ? Friendly : Enemy;
                BattleView.Round(Health, 3);
                meter.Add(Health);
                Shield = BattleView.Box(0, -2, barWidth, 2);
                Shield.style.backgroundColor = Crystal;
                meter.Add(Shield);
            }
        }

        private readonly BattleArt art;
        private readonly Sprite shadow;
        private readonly VisualElement grid, effects, actors, aimLayer, ghost;
        private readonly Dictionary<string, Pawn> pawns = new Dictionary<string, Pawn>();
        private readonly List<string> missing = new List<string>();
        private readonly Comparison<VisualElement> byDepth = (a, b) => ((Pawn)a.userData).Depth.CompareTo(((Pawn)b.userData).Depth);
        private BattleMatch match;
        private BattleCard selected;
        private WorldPoint point, direction;
        private bool aimed, valid;
        private int lastTerritory = -1;
        private double effectTick;

        internal BattlefieldView(BattleArt art, Sprite shadow)
        {
            this.art = art;
            this.shadow = shadow;
            name = "battlefield";
            style.overflow = Overflow.Hidden;
            style.backgroundColor = (Color)new Color32(54, 63, 44, 255);
            generateVisualContent += context => DrawGround(context.painter2D);

            grid = AddLayer("deployment-grid");
            grid.generateVisualContent += context => DrawGrid(context.painter2D);
            effects = AddLayer("battle-effects");
            effects.generateVisualContent += context => DrawEffects(context.painter2D);
            actors = AddLayer("actors");
            aimLayer = AddLayer("targeting");
            aimLayer.generateVisualContent += context => DrawAim(context.painter2D);
            ghost = BattleView.Box(0, 0, 0, 0);
            aimLayer.Add(ghost);
        }

        private VisualElement AddLayer(string id)
        {
            var layer = BattleView.Box(0, 0, Width, Height);
            layer.name = id;
            Add(layer);
            return layer;
        }

        internal WorldPoint World(Vector2 panelPoint)
        {
            Vector2 local = this.WorldToLocal(panelPoint);
            return new WorldPoint(local.x / Tile - Battlefield.HalfWidth, Battlefield.HalfHeight - local.y / Tile);
        }

        internal void AttachMatch(BattleMatch value)
        {
            match = value;
            selected = null;
            lastTerritory = -1;
            pawns.Clear();
            actors.Clear();
            MarkAim(null, false, false, default, default);
            grid.MarkDirtyRepaint();
        }

        internal void MarkAim(BattleCard card, bool hasTarget, bool legal, WorldPoint target, WorldPoint forward)
        {
            if (selected != card)
                grid.MarkDirtyRepaint();
            selected = card;
            aimed = hasTarget && card != null;
            valid = legal;
            point = target;
            direction = forward;
            aimLayer.MarkDirtyRepaint();
            ghost.style.display = DisplayStyle.None;
            if (!aimed || card.IsSpell)
                return;

            BattleActorArt model = art.Actor(card.Id);
            if (!model)
                return;
            Vector2 at = ToBoard(point);
            float size = model.worldSize * Tile;
            ghost.style.display = DisplayStyle.Flex;
            ghost.style.left = at.x - size * model.pivot.x;
            ghost.style.top = at.y - size * (1 - model.pivot.y);
            ghost.style.width = size;
            ghost.style.height = size;
            ghost.style.opacity = legal ? .76f : .40f;
            BattleView.Image(ghost, art.Frame(model, BattlePose.Idle, 0, 0, 0));
        }

        internal void Present(BattlePresentation presentation)
        {
            int territory = TerritoryMask();
            if (lastTerritory != territory)
            {
                lastTerritory = territory;
                grid.MarkDirtyRepaint();
            }

            effectTick = presentation.RenderTick;
            effects.MarkDirtyRepaint();
            missing.Clear();
            foreach (var pair in pawns)
                if (!presentation.Actors.ContainsKey(pair.Key))
                    missing.Add(pair.Key);
            foreach (string key in missing)
            {
                pawns[key].Root.RemoveFromHierarchy();
                pawns.Remove(key);
            }

            foreach (var pair in presentation.Actors)
            {
                if (!pawns.TryGetValue(pair.Key, out Pawn pawn))
                {
                    pawn = new Pawn(art.Actor(pair.Value.CardId), pair.Value, shadow);
                    pawns.Add(pair.Key, pawn);
                    actors.Add(pawn.Root);
                }

                WorldPoint position = presentation.Position(pair.Value, out BattleActorFrame pose);
                Vector2 onBoard = ToBoard(position);
                pawn.Root.style.translate = new Translate(onBoard.x, onBoard.y, 0);
                pawn.Depth = Mathf.RoundToInt(onBoard.y * 16);
                pawn.Health.style.width = Length.Percent(pair.Value.Health * 100);
                pawn.Shield.style.width = Length.Percent(pair.Value.Shield * 100);
                if (!pawn.Model)
                    continue;

                int direction = (Mathf.RoundToInt(Mathf.Atan2(pose.Heading.X, pose.Heading.Y) * Mathf.Rad2Deg / 45f) + 8) % 8;
                Sprite frame = art.Frame(pawn.Model, pose.Pose, direction, effectTick - pose.PoseTick, pose.PoseLength);
                if (frame == pawn.Frame)
                    continue;
                BattleView.Image(pawn.Art, frame);
                pawn.Frame = frame;
            }

            for (int i = 1; i < actors.childCount; i++)
                if (byDepth(actors[i - 1], actors[i]) > 0)
                {
                    actors.Sort(byDepth);
                    break;
                }
        }

        private int TerritoryMask()
        {
            int mask = 0;
            for (int seat = 0; seat < 2; seat++)
                for (int side = 0; side < 2; side++)
                    if (match.Unlocked(seat, side))
                        mask |= 1 << (seat * 2 + side);
            return mask;
        }

        private static Vector2 ToBoard(WorldPoint point) =>
            new((point.X + Battlefield.HalfWidth) * Tile, (Battlefield.HalfHeight - point.Y) * Tile);

        private void DrawGround(Painter2D painter)
        {
            for (int row = 0; row < Battlefield.Rows; row++)
            {
                for (int column = 0; column < Battlefield.Columns; column++)
                {
                    bool path = column is 1 or 2 or 3 or 4 or 7 or 8 or 9 or 10 or 13 or 14 or 15 or 16;
                    bool royal = row < 5 || row >= 27;
                    float noise = ((column * 47 + row * 103 + row * column * 13) % 19) / 18f;
                    Color dark = royal ? new Color32(67, 68, 60, 255) : path ? new Color32(79, 77, 61, 255) : new Color32(46, 64, 47, 255);
                    Color light = royal ? new Color32(85, 85, 76, 255) : path ? new Color32(113, 107, 83, 255) : new Color32(61, 80, 56, 255);
                    Rect tile = new Rect(column * Tile, row * Tile, Tile - (path || royal ? .7f : 0), Tile - (path || royal ? .6f : 0));
                    Rectangle(painter, tile, Color.Lerp(dark, light, noise));
                }
            }

            Rectangle(painter, new Rect(0, 0, Width, Tile * 5), Alpha(Enemy, .065f));
            Rectangle(painter, new Rect(0, Tile * 27, Width, Tile * 5), Alpha(Friendly, .075f));
            Rectangle(painter, new Rect(Tile * 6, Tile * 14, Tile * 6, Tile * 4), new Color32(94, 64, 73, 255));

            for (int lane = 1; lane < 3; lane++)
            {
                float x = Tile * lane * 6;
                Line(painter, new Vector2(x, 0), new Vector2(x, Height), new Color32(195, 178, 139, 58), 1);
            }

            for (int seat = 0; seat < 2; seat++)
            {
                Color team = seat == 0 ? Friendly : Enemy;
                painter.BeginPath();
                painter.strokeColor = Alpha(team, .40f);
                painter.lineWidth = 1.45f;
                painter.MoveTo(ToBoard(Battlefield.Patrol(seat, 0)));
                for (float segment = .25f; segment <= 29.375f; segment += .25f)
                    painter.LineTo(ToBoard(Battlefield.Patrol(seat, segment)));
                painter.ClosePath();
                painter.Stroke();
                for (int side = 0; side < 2; side++)
                {
                    Vector2 home = ToBoard(Battlefield.GuardHome(seat, side));
                    Circle(painter, home, 15, new Color32(29, 39, 40, 160), true);
                    Circle(painter, home, 15, Alpha(team, .75f), false, 1.5f);
                }
            }

            for (int i = 0; i < 7; i++)
            {
                float x = Width * .5f + (i - 3) * 16, y = Height * .5f;
                Line(painter, new Vector2(x - 11, y - 37), new Vector2(x + 2, y - 14), new Color32(229, 143, 169, 130), 1.6f);
                Line(painter, new Vector2(x + 2, y - 14), new Vector2(x - 4, y + 6), new Color32(229, 143, 169, 160), 1.6f);
                Line(painter, new Vector2(x - 4, y + 6), new Vector2(x + 12, y + 34), new Color32(229, 143, 169, 130), 1.6f);
            }
        }

        private void DrawGrid(Painter2D painter)
        {
            if (match == null || selected == null || selected.IsSpell)
                return;
            for (int row = 0; row < Battlefield.Rows; row++)
                for (int col = 0; col < Battlefield.Columns; col++)
                {
                    if (!match.MayDeploy(0, col, row))
                        continue;
                    Color color = match.MayDeploy(1, col, row) ? new Color32(234, 176, 107, 99) : new Color32(83, 199, 228, 79);
                    Rectangle(painter, new Rect(col * Tile + 1, row * Tile + 1, Tile - 2, Tile - 2), color);
                }
        }

        private void DrawAim(Painter2D painter)
        {
            if (!aimed)
                return;
            Color color = valid ? GhostAllowed : GhostDenied;
            Vector2 p = ToBoard(point);
            if (!selected.IsSpell)
            {
                Border(painter, new Rect(p.x - Tile / 2, p.y - Tile / 2, Tile, Tile), color, 2);
                return;
            }
            if (selected.Skill == Skill.Rift)
            {
                Vector2 target = ToBoard(point + direction * selected.Range);
                Line(painter, p, target, Alpha(color, .25f), 2 * Battlefield.RiftHalfWidth * Tile);
                Line(painter, p, target, color, 2);
            }
            else
            {
                Circle(painter, p, selected.Range * Tile, Alpha(color, .19f), true);
                Circle(painter, p, selected.Range * Tile, color, false, 2);
            }
        }

        private void DrawEffects(Painter2D painter)
        {
            if (match == null)
                return;
            Rect hazard = new Rect(6 * Tile, 14 * Tile, 6 * Tile, 4 * Tile);
            if (match.HazardWarning)
            {
                float alpha = .25f + .19f * Mathf.Sin((float)effectTick * .5f);
                Rectangle(painter, hazard, Alpha(Gold, alpha));
                Border(painter, hazard, Alpha(Gold, .94f), 2);
            }

            foreach (BattleEffect fx in match.Effects)
            {
                if (effectTick < fx.Begin || effectTick > fx.End)
                    continue;
                float progress = (float)((effectTick - fx.Begin) / (fx.End - fx.Begin));
                Color color = Alpha(EffectColor(fx), 1 - progress);
                Vector2 origin = ToBoard(fx.Origin), target = ToBoard(fx.Target);
                switch (fx.Kind)
                {
                    case EffectKind.Hazard:
                        Rectangle(painter, hazard, Alpha(color, color.a * .79f));
                        Border(painter, hazard, color, 3);
                        break;
                    case EffectKind.MeteorWarning:
                        Circle(painter, target, fx.Radius * Tile, Alpha(Gold, .7f), false, 2);
                        Circle(painter, target, fx.Radius * Tile * (1 - progress), Gold, false, 1.5f);
                        break;
                    case EffectKind.Area:
                        Circle(painter, target, fx.Radius * Tile * Mathf.Lerp(.3f, 1, progress), Alpha(color, color.a * .45f), true);
                        Circle(painter, target, fx.Radius * Tile, color, false, 2);
                        break;
                    case EffectKind.Projectile:
                    case EffectKind.Heal:
                        Vector2 head = Vector2.Lerp(origin, target, progress);
                        Line(painter, Vector2.Lerp(origin, target, Mathf.Max(0, progress - .18f)), head, color, 2.8f);
                        Circle(painter, head, 3.5f, color, true);
                        break;
                    case EffectKind.Rift:
                        Line(painter, origin, target, Alpha(color, color.a * .48f), Battlefield.RiftHalfWidth * Tile * 2);
                        Line(painter, origin, target, color, 3);
                        break;
                    default:
                        Circle(painter, target, Mathf.Lerp(3.5f, 12f, progress), color, false, 2.6f);
                        break;
                }
            }
        }

        private static Color EffectColor(BattleEffect effect)
        {
            return effect.Skill switch
            {
                Skill.Heal or Skill.Rally => new Color32(147, 255, 152, 255),
                Skill.Frost or Skill.Thunder or Skill.Stun => Crystal,
                Skill.Poison => new Color32(191, 216, 107, 255),
                Skill.Flame or Skill.Meteor or Skill.Rift => new Color32(255, 179, 108, 255),
                _ => new Color32(255, 226, 154, 255)
            };
        }

        internal static Color Alpha(Color color, float amount)
        {
            color.a = amount;
            return color;
        }

        internal static void Rectangle(Painter2D painter, Rect rect, Color color)
        {
            Shape(painter, rect);
            painter.fillColor = color;
            painter.Fill();
        }

        private static void Border(Painter2D painter, Rect rect, Color color, float width)
        {
            Shape(painter, rect);
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.Stroke();
        }

        private static void Shape(Painter2D painter, Rect rect)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
        }

        internal static void Line(Painter2D painter, Vector2 start, Vector2 end, Color color, float width)
        {
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.Stroke();
        }

        internal static void Circle(Painter2D painter, Vector2 center, float radius, Color color, bool fill, float width = 1)
        {
            if (radius <= .001f)
                return;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360));
            painter.ClosePath();
            if (fill)
            {
                painter.fillColor = color;
                painter.Fill();
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = width;
                painter.Stroke();
            }
        }
    }
}
