using System;
using System.Collections.Generic;
using TowerDuel;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPoint = System.Numerics.Vector2;

namespace KingdomRhapsody
{
    internal sealed class BattleView
    {
        private const float Width = 540, Height = 1070, Left = (Width - BattlefieldView.Width) / 2;
        private const float BoardTop = 56, DockTop = BoardTop + BattlefieldView.Height + 7;
        private static readonly Color Gold = new Color32(255, 221, 158, 255);
        private static readonly Color Ivory = new Color32(255, 248, 224, 255);
        private static readonly Color Cyan = new Color32(133, 225, 244, 255);
        private static readonly Color Coral = new Color32(250, 146, 129, 255);
        private static readonly Color Ink = new Color32(15, 26, 40, 255);
        private static readonly Color Panel = new Color32(31, 47, 64, 255);

        private sealed class CardTile
        {
            internal readonly Button Button;
            internal readonly VisualElement Image, Frame;
            internal readonly Label Name, Cost;
            internal BattleCard Card;

            internal CardTile(Button button, VisualElement image, VisualElement frame, Label name, Label cost)
            {
                Button = button;
                Image = image;
                Frame = frame;
                Name = name;
                Cost = cost;
            }
        }

        private readonly VisualElement root, viewport, stage, dialog, header, dock;
        private readonly Label timer, phase, energyValue, hint, countdown, hazard, title;
        private readonly Label[] princeHealth = new Label[2];
        private readonly VisualElement[] princeBars = new VisualElement[2];
        private readonly VisualElement energyBar;
        private readonly Button sprint, continueButton;
        private readonly CardTile[] hand = new CardTile[BattleMatch.HandSize];
        private readonly CardTile next;
        private readonly DuelCardDrag[] drags = new DuelCardDrag[BattleMatch.HandSize];
        private readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        private readonly BattlefieldView board;
        private readonly Action rematch, resume;
        private readonly Action<string> playCue;
        private readonly Sprite cardArt, selectedArt;
        private BattleSession match;
        private BattleSceneFrame scene;
        private readonly bool online;
        private BattleAim selected;
        private readonly Dictionary<int, BattleAim> dragAims = new Dictionary<int, BattleAim>();
        private readonly List<BattleAimMarker> markers = new List<BattleAimMarker>(BattleMatch.HandSize);
        private int boardPointer = -1;
        private int messageEnd, layoutWidth, layoutHeight;
        private Rect lastSafe;

        internal BattleView(VisualElement document, Font font, BattleArt art, Action pause, Action resume,
            Action back, Action rematch, Action<string> playCue, bool online)
        {
            this.online = online;
            this.rematch = rematch;
            this.resume = resume;
            this.playCue = playCue;
            root = document;
            FillRoot(root);
            root.style.backgroundColor = Ink;
            if (font)
                root.style.unityFontDefinition = FontDefinition.FromFont(font);

            viewport = Box(0, 0, 0, 0);
            viewport.style.overflow = Overflow.Hidden;
            root.Add(viewport);
            stage = Box(0, 0, Width, Height);
            viewport.Add(stage);
            root.RegisterCallback<GeometryChangedEvent>(_ => Layout(true));
            var mapLayer = Box(Left, BoardTop, BattlefieldView.Width, BattlefieldView.Height);
            // Apply the inverse grid-to-art aspect adjustment to the art, input, and effects together.
            mapLayer.style.scale = new Scale(new Vector2(Battlefield.ArtScaleY / Battlefield.ArtScaleX, 1));
            stage.Add(mapLayer);
            AddMapArt(mapLayer);
            board = new BattlefieldView(art, Get("frames/ground-shadow"));
            Place(board, 0, 0, BattlefieldView.Width, BattlefieldView.Height);
            mapLayer.Add(board);
            board.RegisterCallback<PointerDownEvent>(BoardDown);
            board.RegisterCallback<PointerMoveEvent>(BoardMove);
            board.RegisterCallback<PointerUpEvent>(BoardUp);
            board.RegisterCallback<PointerCancelEvent>(e => EndBoardPointer(e.pointerId));
            board.RegisterCallback<PointerCaptureOutEvent>(e => EndBoardPointer(e.pointerId));
            board.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (boardPointer < 0 && selected != null)
                {
                    selected.Reset();
                    RefreshAim();
                }
            });

            header = Box(Left, 4, BattlefieldView.Width, 47);
            header.style.backgroundColor = Ink;
            Round(header, 6);
            stage.Add(header);
            MakePrinceMeter(header, 1, 0, "敵方王子", Coral);
            MakePrinceMeter(header, 0, BattlefieldView.Width - 162, "我方王子", Cyan);
            phase = Text(header, "延長賽", 207, 0, 89, 19, 14, Coral);
            timer = Text(header, "", 207, 17, 89, 27, 24, Gold);
            var pauseButton = ActionButton(header, online ? "離開" : "暫停", 163, 9, 47, 30, pause, false);
            pauseButton.style.fontSize = 13;

            countdown = Text(stage, "", Left + 10, BoardTop + 315, BattlefieldView.Width - 20, 102, 51, Ivory);
            countdown.style.backgroundColor = (Color)new Color32(13, 26, 42, 205);
            hazard = Text(stage, "中央裂痕即將爆發", Left + 135, BoardTop + 345, 225, 32, 17, Ink);
            hazard.style.backgroundColor = Gold;
            Round(hazard, 12);

            dock = Box(Left, DockTop, BattlefieldView.Width, 121);
            dock.style.backgroundColor = Ink;
            Round(dock, 6);
            stage.Add(dock);
            energyValue = Text(dock, "", 0, 0, 130, 24, 16, Cyan, TextAnchor.MiddleLeft);
            var energyTrack = Box(0, 25, 130, 5);
            energyTrack.style.backgroundColor = Panel;
            Round(energyTrack, 3);
            dock.Add(energyTrack);
            energyBar = Box(0, 0, 130, 5);
            energyBar.style.backgroundColor = Cyan;
            Round(energyBar, 3);
            energyTrack.Add(energyBar);
            hint = Text(dock, "", 135, 0, 240, 32, 15, Ivory);
            sprint = ActionButton(dock, "王子疾行", 377, 0, 118, 32, Sprint, true);
            sprint.style.fontSize = 16;

            cardArt = Get("frames/card-gold");
            selectedArt = Get("frames/card-cyan-selected");
            for (int i = 0; i < hand.Length; i++)
            {
                int slot = i;
                hand[i] = MakeCard(dock, slot * 101, false);
                drags[i] = new DuelCardDrag(() => CanSelect(slot),
                    (pointer, _) => BeginDrag(slot, pointer), Drag, EndDrag, CancelDrag,
                    () => ToggleSlot(slot));
                hand[i].Button.AddManipulator(drags[i]);
                hand[i].Button.clicked += () => ToggleSlot(slot);
            }
            next = MakeCard(dock, 405, true);

            dialog = Box(0, 0, Width, Height);
            dialog.pickingMode = PickingMode.Position;
            dialog.style.backgroundColor = (Color)new Color32(9, 18, 30, 203);
            stage.Add(dialog);
            var dialogFrame = Box(74, 380, 392, 260);
            Image(dialogFrame, Get("frames/stone-panel"), ScaleMode.StretchToFill);
            dialogFrame.style.backgroundColor = Ink;
            Round(dialogFrame, 16);
            dialog.Add(dialogFrame);
            title = Text(dialogFrame, "", 30, 26, 332, 84, 37, Gold);
            continueButton = ActionButton(dialogFrame, "繼續戰鬥", 75, 116, 242, 52, Continue, true);
            ActionButton(dialogFrame, "回大廳", 75, 186, 242, 51, back, false);
            if (font)
                root.Query<TextElement>().ForEach(label => label.style.unityFontDefinition = FontDefinition.FromFont(font));
            HideDialog();
        }

        internal void AttachMatch(BattleSession value, BattlePresentation presentation)
        {
            ClearAim();
            HideDialog();
            match = value;
            messageEnd = 0;
            board.AttachMatch(value);
            Present(presentation);
        }

        internal void Present(BattlePresentation presentation)
        {
            Layout();
            scene = presentation.Visible;
            board.Present(presentation);
            RefreshHud();
            if (selected != null || dragAims.Count > 0)
                RefreshAim();
        }

        internal void ShowDialog(bool result)
        {
            ClearAim();
            dialog.style.display = DisplayStyle.Flex;
            title.text = result ? (match.Winner == BattleMatch.Draw ? "平手" : match.Winner == match.Seat ? "勝利" : "戰敗") :
                online ? "離開對戰？" : "暫停對練";
            continueButton.text = result ? (online ? "再次配對" : "再戰一場") : "繼續戰鬥";
            continueButton.Focus();
        }

        internal void HideDialog() => dialog.style.display = DisplayStyle.None;
        internal bool DialogOpen => dialog.style.display.value == DisplayStyle.Flex;

        private bool SlotDragged(int slot)
        {
            foreach (BattleAim aim in dragAims.Values)
                if (aim.Slot == slot)
                    return true;
            return false;
        }

        private bool CanSelect(int slot) => match != null && match.Live && !DialogOpen && !SlotDragged(slot) &&
            (boardPointer < 0 || selected?.Slot != slot) && match.Hand(slot) != null;

        private void Continue()
        {
            if (match.Winner == BattleMatch.Playing)
                resume();
            else
                rematch();
        }

        private void Sprint()
        {
            if (DialogOpen || !match.Sprint())
                return;
            if (!online)
                playCue("confirm");
            RefreshHud();
        }

        private void ToggleSlot(int slot)
        {
            if (!CanSelect(slot))
                return;
            if (selected?.Slot == slot)
                selected = null;
            else
            {
                selected = new BattleAim(slot, match.Hand(slot));
                messageEnd = 0;
                playCue("tap");
            }
            RefreshAim();
            RefreshHud();
        }

        private void BeginDrag(int slot, int pointer)
        {
            if (!CanSelect(slot) || dragAims.ContainsKey(pointer))
                return;
            if (selected?.Slot == slot)
                selected = null;
            dragAims.Add(pointer, new BattleAim(slot, match.Hand(slot)));
            messageEnd = 0;
            RefreshAim();
        }

        private void Drag(int pointer, Vector2 at)
        {
            if (dragAims.TryGetValue(pointer, out BattleAim aim))
            {
                aim.Update(board.World(at), match.Forward, true);
                RefreshAim();
            }
        }

        private void EndDrag(int pointer, Vector2 at)
        {
            if (!dragAims.TryGetValue(pointer, out BattleAim aim))
                return;
            dragAims.Remove(pointer);
            Commit(aim, board.World(at));
        }

        private void CancelDrag(int pointer)
        {
            if (dragAims.Remove(pointer))
                RefreshAim();
        }

        private void BoardDown(PointerDownEvent evt)
        {
            if (boardPointer >= 0 || selected == null || DialogOpen || !match.Live ||
                evt.pointerType == UnityEngine.UIElements.PointerType.mouse && evt.button != 0)
                return;

            boardPointer = evt.pointerId;
            selected.Reset();
            selected.Update(board.World(evt.position), match.Forward, true);
            board.CapturePointer(boardPointer);
            RefreshAim();
            evt.StopImmediatePropagation();
        }

        private void BoardMove(PointerMoveEvent evt)
        {
            if (selected == null || boardPointer >= 0 && boardPointer != evt.pointerId)
                return;
            selected.Update(board.World(evt.position), match.Forward, boardPointer >= 0);
            RefreshAim();
        }

        private void BoardUp(PointerUpEvent evt)
        {
            if (evt.pointerId != boardPointer)
                return;
            int released = boardPointer;
            boardPointer = -1;
            board.ReleasePointer(released);
            Commit(selected, board.World(evt.position));
            evt.StopImmediatePropagation();
        }

        private void EndBoardPointer(int pointer)
        {
            if (pointer != boardPointer)
                return;
            boardPointer = -1;
            if (selected != null)
            {
                selected.Reset();
                RefreshAim();
            }
        }

        private void Commit(BattleAim aim, WorldPoint location)
        {
            if (aim == null)
                return;
            aim.Update(location, match.Forward, true);
            PlayResult result = aim.HasTarget ? match.Play(aim.Slot, aim.Card, aim.Point, aim.Direction) : PlayResult.Bounds;
            if (result == PlayResult.Accepted || result == PlayResult.Pending)
            {
                if (selected == aim)
                    selected = null;
            }
            else if (selected == aim)
                aim.Reset();
            ReportResult(aim.Slot, result);
            RefreshAim();
        }

        internal void ReportResult(int slot, PlayResult result)
        {
            if (result == PlayResult.Accepted)
            {
                playCue(slot < 0 ? "confirm" : "deploy");
                messageEnd = 0;
                return;
            }

            hint.text = result switch
            {
                PlayResult.Pending => "等待確認",
                PlayResult.Energy => "能量不足",
                PlayResult.Territory => "此格不能下兵",
                PlayResult.Occupied => "此格已有角色",
                PlayResult.UnitLimit => "戰場兵力已達上限",
                PlayResult.Bounds => "請在戰場內放開",
                _ => "請重新選牌"
            };
            hint.style.color = result == PlayResult.Pending ? Ivory : Coral;
            messageEnd = match.Tick + BattleMatch.TickRate * 2;
        }

        private void RefreshAim()
        {
            markers.Clear();
            bool showGrid = CollectMarker(selected);
            foreach (BattleAim aim in dragAims.Values)
                showGrid |= CollectMarker(aim);
            board.MarkAims(markers, showGrid);
        }

        private bool CollectMarker(BattleAim aim)
        {
            if (aim == null)
                return false;
            if (aim.HasTarget)
            {
                bool valid = match.Validate(aim.Slot, aim.Card, aim.Point) == PlayResult.Accepted;
                markers.Add(new BattleAimMarker(aim.Card, aim.Point, aim.Direction, valid));
            }
            return !aim.Card.IsSpell;
        }

        internal bool ClearAim()
        {
            bool active = selected != null || dragAims.Count > 0;
            int pointer = boardPointer;
            boardPointer = -1;
            dragAims.Clear();
            selected = null;
            if (pointer >= 0 && board.HasPointerCapture(pointer))
                board.ReleasePointer(pointer);
            foreach (DuelCardDrag drag in drags)
                drag.Cancel();
            messageEnd = 0;
            RefreshAim();
            return active;
        }

        private void MakePrinceMeter(VisualElement parent, int seat, float left, string name, Color color)
        {
            Text(parent, name, left, 0, 162, 20, 16, color);
            var track = Box(left, 22, 162, 22);
            track.style.backgroundColor = Panel;
            Round(track, 6);
            parent.Add(track);
            var bar = Box(0, 0, 162, 22);
            bar.style.backgroundColor = BattlefieldView.Alpha(color, .35f);
            Round(bar, 6);
            track.Add(bar);
            princeBars[seat] = bar;
            princeHealth[seat] = Text(track, "", 0, 0, 162, 21, 15, Ivory);
        }

        private static void AddMapArt(VisualElement mapLayer)
        {
            float width = 2 * Battlefield.ArtHalfWidth * Battlefield.ArtScaleX * BattlefieldView.Tile;
            float height = 2 * Battlefield.ArtHalfHeight * Battlefield.ArtScaleY * BattlefieldView.Tile;
            var map = Box((BattlefieldView.Width - width) / 2, (BattlefieldView.Height - height) / 2, width, height);
            map.name = "rift-courts-map";
            Image(map, Resources.Load<Sprite>("Battle/Maps/RiftCourts"), ScaleMode.StretchToFill);
            mapLayer.Add(map);
        }

        private CardTile MakeCard(VisualElement parent, float x, bool isNext)
        {
            var button = new Button { name = isNext ? "next-card" : "battle-card" };
            Place(button, x, 36, isNext ? 90 : 95, 85);
            button.style.marginBottom = 0;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.backgroundColor = Panel;
            button.style.overflow = Overflow.Hidden;
            Round(button, 6);
            Edges(button, 0);
            if (isNext)
            {
                button.pickingMode = PickingMode.Ignore;
                button.focusable = false;
            }
            var illustration = Box(4, isNext ? 11 : 4, 83, isNext ? 54 : 59);
            button.Add(illustration);
            var frame = Box(0, 0, isNext ? 90 : 95, 85);
            button.Add(frame);
            var strip = Box(4, 61, isNext ? 82 : 87, 20);
            strip.style.backgroundColor = (Color)new Color32(18, 31, 46, 239);
            button.Add(strip);
            Label name = Text(strip, "", 0, 0, isNext ? 82 : 87, 20, 13, Ivory);
            Label cost = Text(button, "", 5, 4, isNext ? 49 : 22, 20, 13, Ink);
            cost.style.backgroundColor = Gold;
            Round(cost, 9);
            parent.Add(button);
            return new CardTile(button, illustration, frame, name, cost);
        }

        private void RefreshHud()
        {
            int remaining = Mathf.CeilToInt(match.PhaseTicksLeft / (float)BattleMatch.TickRate);
            Write(timer, $"{remaining / 60:00}:{remaining % 60:00}");
            phase.style.display = match.Overtime ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < 2; i++)
            {
                float health = scene.Health(i == 0 ? match.Seat : 1 - match.Seat);
                Write(princeHealth[i], $"{Mathf.CeilToInt(health)} / {(int)BattleCatalog.Prince.Health}");
                princeBars[i].style.width = Length.Percent(health / BattleCatalog.Prince.Health * 100);
            }
            Write(energyValue, $"能量 {match.Energy:0.0} / 10");
            energyBar.style.width = Length.Percent((float)(match.Energy / BattleMatch.MaxEnergy * 100));
            int sprintLeft = Mathf.CeilToInt(match.SprintTicksLeft / (float)BattleMatch.TickRate);
            Write(sprint, sprintLeft == 0 ? $"疾行 {BattleMatch.SprintCost} 點" : $"疾行 {sprintLeft}s");
            sprint.SetEnabled(match.CanSprint && !DialogOpen);
            bool starting = match.Tick < BattleMatch.OpeningTicks && match.Winner == BattleMatch.Playing;
            countdown.style.display = starting ? DisplayStyle.Flex : DisplayStyle.None;
            hazard.style.display = scene.HazardWarning ? DisplayStyle.Flex : DisplayStyle.None;
            if (starting)
                Write(countdown, Mathf.CeilToInt((BattleMatch.OpeningTicks - match.Tick) / (float)BattleMatch.TickRate).ToString());
            if (match.Tick >= messageEnd)
            {
                hint.style.color = Ivory;
                Write(hint, DeploymentHint());
            }
            for (int i = 0; i < hand.Length; i++)
            {
                BattleCard card = match.Hand(i);
                PaintCard(hand[i], card, false);
                Image(hand[i].Frame, i == selected?.Slot || SlotDragged(i) ? selectedArt : cardArt, ScaleMode.StretchToFill);
                hand[i].Button.style.opacity = match.Live && match.Energy + 1e-8 >= card.Cost ? 1f : .58f;
            }
            PaintCard(next, match.NextCard, true);
        }

        private string DeploymentHint()
        {
            if (dragAims.Count != 0)
                return "拖入戰場後放開";
            if (selected == null)
                return "點手牌，再點戰場";
            if (selected.Card.Skill == Skill.Rift)
                return "在戰場拖曳定向";
            return selected.Card.IsSpell ? "點戰場施法" : "點部署格下兵";
        }

        private void PaintCard(CardTile tile, BattleCard card, bool isNext)
        {
            if (tile.Card == card)
                return;
            tile.Card = card;
            tile.Name.text = card.Name;
            tile.Cost.text = isNext ? "下一張" : card.Cost.ToString();
            tile.Button.tooltip = card.Action;
            Image(tile.Frame, cardArt, ScaleMode.StretchToFill);
            Image(tile.Image, Portrait(card));
            tile.Image.generateVisualContent -= SpellArtwork;
            tile.Image.userData = card;
            if (card.IsSpell)
                tile.Image.generateVisualContent += SpellArtwork;
            tile.Image.MarkDirtyRepaint();
        }

        private Sprite Portrait(BattleCard card)
        {
            if (card.IsSpell)
                return null;
            if (!portraits.TryGetValue(card.Id, out Sprite sprite))
            {
                sprite = Get("illustrations/characters/" + card.Id) ?? Get("card-portraits/" + card.Id) ??
                    Get("portraits/troops/" + card.Id);
                portraits.Add(card.Id, sprite);
            }
            return sprite;
        }

        private static void SpellArtwork(MeshGenerationContext context)
        {
            BattleCard card = (BattleCard)context.visualElement.userData;
            var painter = context.painter2D;
            var center = new Vector2(42, 30);
            Color baseColor = card.Skill == Skill.Thunder ? Cyan : Gold;
            BattlefieldView.Circle(painter, center, 18, BattlefieldView.Alpha(baseColor, .18f), true);
            if (card.Skill == Skill.Meteor)
            {
                BattlefieldView.Line(painter, new Vector2(59, 9), new Vector2(43, 24), baseColor, 6);
                BattlefieldView.Line(painter, new Vector2(53, 6), new Vector2(37, 24), BattlefieldView.Alpha(Gold, .4f), 3);
                BattlefieldView.Circle(painter, center, 9, Gold, true);
            }
            else
            {
                float offset = card.Skill == Skill.Rift ? 8 : 0;
                Vector2 a = new Vector2(47 + offset, 10), b = new Vector2(33, 29), c = new Vector2(46, 27), d = new Vector2(36 - offset, 49);
                BattlefieldView.Line(painter, a, b, baseColor, 4);
                BattlefieldView.Line(painter, b, c, baseColor, 4);
                BattlefieldView.Line(painter, c, d, baseColor, 4);
            }
        }

        private void Layout(bool force = false)
        {
            Rect safe = Screen.safeArea;
            if (!force && layoutWidth == Screen.width && layoutHeight == Screen.height && lastSafe == safe)
                return;
            if (root.panel == null || root.resolvedStyle.width <= 0 || root.resolvedStyle.height <= 0)
                return;
            layoutWidth = Screen.width;
            layoutHeight = Screen.height;
            lastSafe = safe;
            Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(safe.xMin, Screen.height - safe.yMax));
            Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(safe.xMax, Screen.height - safe.yMin));
            if (bottomRight.x <= topLeft.x || bottomRight.y <= topLeft.y)
            {
                topLeft = Vector2.zero;
                bottomRight = new Vector2(root.resolvedStyle.width, root.resolvedStyle.height);
            }
            Vector2 available = bottomRight - topLeft;
            Place(viewport, topLeft.x, topLeft.y, available.x, available.y);
            // Keep the field tall while its outer cell centers remain within reach.
            float playableWidth = BattlefieldView.Width - BattlefieldView.Tile * .8f;
            float scale = Mathf.Min(available.x / playableWidth, available.y / Height);
            var center = available * .5f;
            stage.style.left = center.x - Width / 2;
            stage.style.top = center.y - Height / 2;
            stage.style.scale = new Scale(new Vector2(scale, scale));
            float hudScale = Mathf.Min(1, available.x / (scale * BattlefieldView.Width));
            header.style.scale = dock.style.scale = new Scale(new Vector2(hudScale, 1));
        }

        private static void FillRoot(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
            element.style.width = element.style.height = StyleKeyword.Auto;
        }

        private static Label Text(VisualElement parent, string text, float x, float y, float width,
            float height, int fontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            Place(label, x, y, width, height);
            label.style.unityTextAlign = align;
            label.style.fontSize = fontSize;
            label.style.color = color;
            parent.Add(label);
            return label;
        }

        private static void Write(TextElement label, string text)
        {
            if (label.text != text)
                label.text = text;
        }

        private static Button ActionButton(VisualElement parent, string text, float x, float y, float width,
            float height, Action action, bool gold)
        {
            var button = new Button(action) { text = text };
            Place(button, x, y, width, height);
            button.style.marginLeft = button.style.marginTop = 0;
            button.style.fontSize = 20;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.backgroundColor = gold ? Gold : Panel;
            button.style.color = gold ? Ink : Ivory;
            Edges(button, 0);
            Round(button, 7);
            parent.Add(button);
            return button;
        }

        internal static VisualElement Box(float x, float y, float width, float height)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            Place(element, x, y, width, height);
            return element;
        }

        private static void Place(VisualElement element, float x, float y, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = x;
            element.style.top = y;
            element.style.width = width;
            element.style.height = height;
        }

        internal static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }

        private static void Edges(VisualElement element, float width)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth =
                element.style.borderLeftWidth = element.style.borderRightWidth = width;
        }

        internal static void Image(VisualElement element, Sprite sprite, ScaleMode scale = ScaleMode.ScaleToFit)
        {
            element.style.backgroundImage = sprite ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
            element.style.backgroundSize = BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize(scale);
            element.style.backgroundRepeat = BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat(scale);
            element.style.backgroundPositionX = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(scale);
            element.style.backgroundPositionY = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(scale);
        }

        private static Sprite Get(string id) => Resources.Load<Sprite>("Lobby/" + id);
    }
}
