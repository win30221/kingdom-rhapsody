using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPoint = System.Numerics.Vector2;

namespace KingdomRhapsody
{
    internal readonly struct BattleAimMarker
    {
        internal readonly BattleCard Card;
        internal readonly WorldPoint Point, Direction;
        internal readonly bool Valid;

        internal BattleAimMarker(BattleCard card, WorldPoint point, WorldPoint direction, bool valid)
        {
            Card = card;
            Point = point;
            Direction = direction;
            Valid = valid;
        }
    }

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
            internal readonly VisualElement Root, Art, NextArt, Health, Shield, Flash;
            internal readonly VisualElement[] Markers = new VisualElement[5];
            internal readonly BattleActorArt Model;
            internal Sprite Frame, NextFrame;
            internal int Depth, Facing = -1;
            internal double FadeFrom = -1;

            internal Pawn(BattleActorArt model, BattleActorFrame actor, Sprite shadow, int ownSeat)
            {
                Model = model;
                Root = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform, userData = this };
                Root.style.position = Position.Absolute;
                float size = (model ? model.worldSize : 2f) * Tile;
                var mark = BattleView.Box(actor.IsPrince || actor.IsGuard ? -17 : -11, -5,
                    actor.IsPrince || actor.IsGuard ? 34 : 22, 10);
                mark.style.backgroundColor = Alpha(actor.Seat == ownSeat ? Friendly : Enemy, .35f);
                BattleView.Round(mark, 7);
                Root.Add(mark);
                var ground = BattleView.Box(-size * .30f, -size * .055f, size * .60f, size * .14f);
                BattleView.Image(ground, shadow);
                Root.Add(ground);
                Flash = BattleView.Box(-size * .25f, -7, size * .5f, 14);
                Flash.style.backgroundColor = Alpha(Gold, .43f);
                Flash.style.opacity = 0;
                BattleView.Round(Flash, 7);
                Root.Add(Flash);
                float top = -size * (1 - (model ? model.pivot.y : .2f));
                Art = BattleView.Box(-size * .5f, top, size, size);
                NextArt = BattleView.Box(-size * .5f, top, size, size);
                NextArt.style.display = DisplayStyle.None;
                Root.Add(Art);
                Root.Add(NextArt);
                float barWidth = actor.IsPrince ? 40 : actor.IsGuard ? 33 : 25;
                var meter = BattleView.Box(-barWidth / 2, -size * .69f, barWidth, actor.IsPrince ? 5 : 3);
                meter.style.backgroundColor = (Color)new Color32(14, 25, 27, 245);
                BattleView.Round(meter, 3);
                Root.Add(meter);
                Health = BattleView.Box(0, 0, barWidth, actor.IsPrince ? 5 : 3);
                Health.style.backgroundColor = actor.Seat == ownSeat ? Friendly : Enemy;
                BattleView.Round(Health, 3);
                meter.Add(Health);
                Shield = BattleView.Box(0, -2, barWidth, 2);
                Shield.style.backgroundColor = Crystal;
                meter.Add(Shield);
                var colors = new Color[] { Crystal, new Color32(161, 225, 86, 255), Gold,
                    new Color32(99, 225, 162, 255), Enemy };
                for (int i = 0; i < Markers.Length; i++)
                {
                    var badge = BattleView.Box(i * 6, -8, 4, 4);
                    badge.style.backgroundColor = colors[i];
                    badge.style.display = DisplayStyle.None;
                    BattleView.Round(badge, 2);
                    Markers[i] = badge;
                    meter.Add(badge);
                }
            }
        }

        private readonly BattleArt art;
        private readonly Sprite shadow;
        private readonly VisualElement grid, effects, actors, aimLayer;
        private readonly List<VisualElement> ghosts = new List<VisualElement>(BattleMatch.HandSize);
        private readonly List<BattleAimMarker> aim = new List<BattleAimMarker>(BattleMatch.HandSize);
        private readonly Dictionary<string, Pawn> pawns = new Dictionary<string, Pawn>();
        private readonly List<string> missing = new List<string>();
        private readonly Comparison<VisualElement> byDepth = (a, b) => ((Pawn)a.userData).Depth.CompareTo(((Pawn)b.userData).Depth);
        private BattleSession match;
        private BattlePresentation playback;
        private bool showGrid;
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
            actors = AddLayer("actors");
            effects = AddLayer("battle-effects");
            effects.generateVisualContent += context => DrawEffects(context.painter2D);
            aimLayer = AddLayer("targeting");
            aimLayer.generateVisualContent += context => DrawAim(context.painter2D);
            for (int i = 0; i < BattleMatch.HandSize; i++)
            {
                var ghost = BattleView.Box(0, 0, 0, 0);
                ghost.style.display = DisplayStyle.None;
                aimLayer.Add(ghost);
                ghosts.Add(ghost);
            }
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
            var point = new WorldPoint(local.x / Tile - Battlefield.HalfWidth, Battlefield.HalfHeight - local.y / Tile);
            return Perspective(point);
        }

        internal void AttachMatch(BattleSession value)
        {
            match = value;
            playback = null;
            lastTerritory = -1;
            pawns.Clear();
            actors.Clear();
            MarkAims(Array.Empty<BattleAimMarker>(), false);
            grid.MarkDirtyRepaint();
            MarkDirtyRepaint();
        }

        internal void MarkAims(IReadOnlyList<BattleAimMarker> markers, bool gridVisible)
        {
            if (showGrid != gridVisible)
                grid.MarkDirtyRepaint();
            showGrid = gridVisible;
            aim.Clear();
            for (int i = 0; i < markers.Count; i++)
                aim.Add(markers[i]);
            aimLayer.MarkDirtyRepaint();

            int drawn = 0;
            foreach (BattleAimMarker marker in aim)
            {
                if (marker.Card.IsSpell)
                    continue;
                BattleActorArt model = art.Actor(marker.Card.Id);
                if (!model || drawn >= ghosts.Count)
                    continue;
                VisualElement ghost = ghosts[drawn++];
                Vector2 at = ToBoard(marker.Point);
                float size = model.worldSize * Tile;
                ghost.style.display = DisplayStyle.Flex;
                ghost.style.left = at.x - size * model.pivot.x;
                ghost.style.top = at.y - size * (1 - model.pivot.y);
                ghost.style.width = size;
                ghost.style.height = size;
                ghost.style.opacity = marker.Valid ? .76f : .40f;
                Sprite frame = art.Frame(model, BattlePose.Idle, 0, 0, 0);
                if (!ReferenceEquals(ghost.userData, frame))
                {
                    ghost.userData = frame;
                    BattleView.Image(ghost, frame);
                }
            }
            for (int i = drawn; i < ghosts.Count; i++)
                ghosts[i].style.display = DisplayStyle.None;
        }

        internal void Present(BattlePresentation presentation)
        {
            playback = presentation;
            int territory = presentation.Visible.Territory;
            if (lastTerritory != territory)
            {
                lastTerritory = territory;
                grid.MarkDirtyRepaint();
            }

            effectTick = presentation.RenderTick;
            effects.MarkDirtyRepaint();
            missing.Clear();
            foreach (var pair in pawns)
            {
                if (presentation.Actors.ContainsKey(pair.Key))
                    continue;
                Pawn pawn = pair.Value;
                if (pawn.FadeFrom < 0)
                {
                    pawn.FadeFrom = presentation.Visible.Tick;
                    pawn.Health.style.width = 0;
                    pawn.Shield.style.width = 0;
                    foreach (VisualElement badge in pawn.Markers)
                        badge.style.display = DisplayStyle.None;
                }
                float fade = (float)((effectTick - pawn.FadeFrom) / BattleMatch.DeathAnimationTicks);
                if (fade >= 1)
                    missing.Add(pair.Key);
                else
                {
                    pawn.Root.style.opacity = 1 - fade;
                    pawn.Art.style.scale = pawn.NextArt.style.scale = new Scale(new Vector2(1 + .10f * fade, 1 - .28f * fade));
                }
            }
            foreach (string key in missing)
            {
                pawns[key].Root.RemoveFromHierarchy();
                pawns.Remove(key);
            }

            foreach (var pair in presentation.Actors)
            {
                if (!pawns.TryGetValue(pair.Key, out Pawn pawn))
                {
                    pawn = new Pawn(art.Actor(pair.Value.CardId), pair.Value, shadow, match.Seat);
                    pawns.Add(pair.Key, pawn);
                    actors.Add(pawn.Root);
                }

                WorldPoint position = presentation.Position(pair.Value, out BattleActorFrame pose, out double walkPhase);
                Vector2 onBoard = ToBoard(position);
                pawn.Root.style.translate = new Translate(onBoard.x, onBoard.y, 0);
                pawn.Depth = Mathf.RoundToInt(onBoard.y * 16);
                pawn.Health.style.width = Length.Percent(pair.Value.Health * 100);
                pawn.Shield.style.width = Length.Percent(pair.Value.Shield * 100);
                PaintStatus(pawn, pair.Value);
                if (!pawn.Model)
                    continue;

                WorldPoint heading = Perspective(pose.Heading);
                float angle = Mathf.Atan2(heading.X, heading.Y) * Mathf.Rad2Deg;
                int direction = (Mathf.RoundToInt(angle / 45f) + 8) % 8;
                if (pawn.Facing < 0 || pose.Pose != BattlePose.Walk ||
                    Mathf.Abs(Mathf.DeltaAngle(pawn.Facing * 45, angle)) > 26)
                    pawn.Facing = direction;
                double elapsed = pose.Pose == BattlePose.Walk ? walkPhase : effectTick - pose.PoseTick;
                PaintPose(pawn, art.Evaluate(pawn.Model, pose.Pose, pawn.Facing, elapsed, pose.PoseLength));
            }

            for (int i = 1; i < actors.childCount; i++)
                if (byDepth(actors[i - 1], actors[i]) > 0)
                {
                    actors.Sort(byDepth);
                    break;
                }
        }

        private static void PaintPose(Pawn pawn, BattleArt.Sample pose)
        {
            if (pose.Primary != pawn.Frame)
            {
                BattleView.Image(pawn.Art, pose.Primary);
                pawn.Frame = pose.Primary;
            }
            if (pose.Next != pawn.NextFrame)
            {
                BattleView.Image(pawn.NextArt, pose.Next);
                pawn.NextFrame = pose.Next;
            }
            pawn.Art.style.opacity = 1 - .70f * pose.Blend;
            pawn.NextArt.style.display = pose.Next ? DisplayStyle.Flex : DisplayStyle.None;
            pawn.NextArt.style.opacity = pose.Blend;
        }

        private void PaintStatus(Pawn pawn, BattleActorFrame actor)
        {
            BattleStatus state = actor.Status;
            for (int i = 0; i < pawn.Markers.Length; i++)
                pawn.Markers[i].style.display = (((int)state >> i) & 1) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Color tint = Color.white;
            if ((state & BattleStatus.ShieldDown) != 0)
                tint = new Color(.55f, .52f, .65f);
            else if ((state & BattleStatus.Awakened) != 0 && actor.Pose != BattlePose.Awaken)
                tint = new Color(1, .56f, .62f);
            pawn.Art.style.unityBackgroundImageTintColor = pawn.NextArt.style.unityBackgroundImageTintColor = tint;
            double sinceHit = effectTick - actor.LastHitTick;
            pawn.Flash.style.opacity = actor.LastHitTick >= 0 && sinceHit >= 0 && sinceHit < 5
                ? 1 - (float)(sinceHit / 5) : 0;
        }

        private WorldPoint Perspective(WorldPoint point) => match?.Seat == 1 ? -point : point;

        private Vector2 ToBoard(WorldPoint point)
        {
            point = Perspective(point);
            return new Vector2((point.X + Battlefield.HalfWidth) * Tile, (Battlefield.HalfHeight - point.Y) * Tile);
        }

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
                Color team = seat == (match?.Seat ?? 0) ? Friendly : Enemy;
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
            if (match == null || !showGrid)
                return;
            for (int row = 0; row < Battlefield.Rows; row++)
                for (int col = 0; col < Battlefield.Columns; col++)
                {
                    if (!BattleSession.MayDeploy(lastTerritory, match.Seat, col, row))
                        continue;
                    Color color = BattleSession.MayDeploy(lastTerritory, 1 - match.Seat, col, row)
                        ? new Color32(234, 176, 107, 99) : new Color32(83, 199, 228, 79);
                    Vector2 at = ToBoard(Battlefield.Cell(col, row));
                    Rectangle(painter, new Rect(at.x - Tile / 2 + 1, at.y - Tile / 2 + 1, Tile - 2, Tile - 2), color);
                }
        }

        private void DrawAim(Painter2D painter)
        {
            foreach (BattleAimMarker marker in aim)
            {
                Color color = marker.Valid ? GhostAllowed : GhostDenied;
                Vector2 point = ToBoard(marker.Point);
                if (!marker.Card.IsSpell)
                {
                    Border(painter, new Rect(point.x - Tile / 2, point.y - Tile / 2, Tile, Tile), color, 2);
                }
                else if (marker.Card.Skill == Skill.Rift)
                {
                    Vector2 target = ToBoard(marker.Point + marker.Direction * marker.Card.Range);
                    Line(painter, point, target, Alpha(color, .25f), 2 * Battlefield.RiftHalfWidth * Tile);
                    Line(painter, point, target, color, 2);
                }
                else
                {
                    Circle(painter, point, marker.Card.Range * Tile, Alpha(color, .19f), true);
                    Circle(painter, point, marker.Card.Range * Tile, color, false, 2);
                }
            }
        }

        private void DrawEffects(Painter2D painter)
        {
            if (match == null || playback == null)
                return;
            Rect hazard = new Rect(6 * Tile, 14 * Tile, 6 * Tile, 4 * Tile);
            if (playback.Visible.HazardWarning)
            {
                float alpha = .25f + .19f * Mathf.Sin((float)effectTick * .5f);
                Rectangle(painter, hazard, Alpha(Gold, alpha));
                Border(painter, hazard, Alpha(Gold, .94f), 2);
            }

            foreach (BattleEffect fx in match.Effects)
            {
                if (effectTick < fx.Begin || effectTick >= fx.End)
                    continue;
                float progress = (float)((effectTick - fx.Begin) / (fx.End - fx.Begin));
                Color color = Alpha(EffectColor(fx), 1 - progress);
                Vector2 origin = ToBoard(fx.Origin), target = ToBoard(fx.Target);
                switch (fx.Kind)
                {
                    case EffectKind.Hazard:
                        Rectangle(painter, hazard, Alpha(color, color.a * .64f));
                        Border(painter, hazard, color, 3);
                        break;
                    case EffectKind.MeteorWarning:
                        Circle(painter, target, fx.Radius * Tile, Alpha(Gold, .72f), false, 2);
                        Circle(painter, target, fx.Radius * Tile * (1 - progress), Gold, false, 1.5f);
                        if (progress > .65f)
                        {
                            Vector2 meteor = Vector2.Lerp(target + new Vector2(-33, -98), target, (progress - .65f) / .35f);
                            Line(painter, meteor + new Vector2(-13, -27), meteor, Alpha(Gold, .75f), 5);
                            Circle(painter, meteor, 6.5f, Gold, true);
                        }
                        break;
                    case EffectKind.Area:
                        ImpactArea(painter, fx, target, color, progress);
                        break;
                    case EffectKind.Projectile:
                        if (fx.TrackingKey != null && playback.Actors.TryGetValue(fx.TrackingKey, out BattleActorFrame actor))
                            target = ToBoard(playback.Position(actor, out _));
                        Vector2 forward = (target - origin).normalized;
                        origin += forward * 7 + new Vector2(0, -11);
                        target.y -= 7;
                        Color bright = EffectColor(fx);
                        Vector2 head = Vector2.Lerp(origin, target, progress);
                        Vector2 tail = Vector2.Lerp(origin, target, Mathf.Max(0, progress - .22f));
                        Line(painter, tail, head, Alpha(bright, .8f), 2.5f);
                        Circle(painter, head, fx.Skill is Skill.Cannon or Skill.Flame ? 4.5f : 2.6f, bright, true);
                        break;
                    case EffectKind.Rift:
                        Line(painter, origin, target, Alpha(color, color.a * .46f), Battlefield.RiftHalfWidth * Tile * 2);
                        Zigzag(painter, origin, target, color, 4, 10);
                        break;
                    case EffectKind.Heal:
                        Vector2 above = target + new Vector2(0, -14 - progress * 8);
                        Line(painter, above + new Vector2(0, -4), above + new Vector2(0, 4), color, 2.5f);
                        Line(painter, above + new Vector2(-4, 0), above + new Vector2(4, 0), color, 2.5f);
                        break;
                    case EffectKind.Rally:
                        Circle(painter, target, fx.Radius * Tile, Alpha(color, color.a * .53f), false, 1.5f);
                        break;
                    case EffectKind.ShieldBreak:
                    case EffectKind.ShieldRegrow:
                        Circle(painter, target, Mathf.Lerp(8, 21, progress), color, false, 2.2f);
                        Radial(painter, target, 8 + 17 * progress, 3.5f, color, 6);
                        break;
                    default:
                        if (fx.Skill == Skill.Bloom && origin.y - target.y > Mathf.Abs(target.x - origin.x) * 2.4f)
                            DrawVine(painter, origin + new Vector2(0, -9), target + new Vector2(0, -6), progress);
                        Radial(painter, target, Mathf.Lerp(3, 15, progress), 3.5f, color, 4);
                        break;
                }
            }
        }

        private static void DrawVine(Painter2D painter, Vector2 origin, Vector2 target, float progress)
        {
            float life = 1 - progress;
            Vector2 prior = origin;
            for (int i = 1; i <= 6; i++)
            {
                float fraction = i / 6f;
                Vector2 next = Vector2.Lerp(origin, target, fraction * (1 - .35f * progress));
                next.x += Mathf.Sin(fraction * Mathf.PI) * life * 6;
                Line(painter, prior, next, new Color(.73f, .49f, .27f, life), 3.2f - fraction * 1.8f);
                prior = next;
            }
            Circle(painter, prior, 2.7f * life, new Color(.85f, .27f, .32f, life), true);
        }

        private static void ImpactArea(Painter2D painter, BattleEffect effect, Vector2 target, Color color, float progress)
        {
            float radius = effect.Radius * Tile;
            Circle(painter, target, radius, Alpha(color, color.a * .65f), false, 1.8f);
            Circle(painter, target, radius * Mathf.Lerp(.25f, 1, progress), Alpha(color, color.a * .28f), true);
            if (effect.Skill == Skill.Thunder)
            {
                Zigzag(painter, target + new Vector2(-12, -53), target, color, 3.5f, 5);
                Zigzag(painter, target + new Vector2(22, -32), target, Alpha(color, .66f * color.a), 1.7f, 4);
            }
            else
            {
                float reach = radius * Mathf.Lerp(.21f, .82f, progress);
                Radial(painter, target, reach, effect.Skill == Skill.Hammer ? 4 : 7, color, 8);
            }
        }

        private static void Zigzag(Painter2D painter, Vector2 a, Vector2 b, Color color, float width, int segments)
        {
            Vector2 bend = Vector2.Perpendicular((b - a).normalized) * 4;
            Vector2 previous = a;
            for (int i = 1; i <= segments; i++)
            {
                Vector2 next = Vector2.Lerp(a, b, i / (float)segments);
                if (i < segments)
                    next += (i % 2 == 0 ? bend : -bend);
                Line(painter, previous, next, color, width);
                previous = next;
            }
        }

        private static void Radial(Painter2D painter, Vector2 center, float reach, float length, Color color, int rays)
        {
            for (int i = 0; i < rays; i++)
            {
                float angle = Mathf.PI * 2 * (i + .4f) / rays;
                Vector2 heading = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Line(painter, center + heading * reach, center + heading * (reach + length), color, 1.8f);
            }
        }

        private static Color EffectColor(BattleEffect effect)
        {
            if (effect.Kind is EffectKind.ShieldBreak or EffectKind.ShieldRegrow)
                return Crystal;
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
