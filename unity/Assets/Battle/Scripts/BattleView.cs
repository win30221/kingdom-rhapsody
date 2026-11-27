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

        private readonly VisualElement root, stage, dialog;
        private readonly Label timer, energyValue, hint, countdown, hazard, title;
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
        private BattleMatch match;
        private BattleCard selected;
        private int selectedSlot = -1, dragPointer = -1, boardPointer = -1;
        private bool hasOrigin, hasTarget;
        private WorldPoint origin, point, direction = WorldPoint.UnitY;
        private int messageEnd, layoutWidth, layoutHeight;
        private Rect lastSafe;

        internal BattleView(VisualElement document, Font font, BattleArt art, Action pause, Action resume,
            Action back, Action rematch, Action<string> playCue)
        {
            this.rematch = rematch;
            this.resume = resume;
            this.playCue = playCue;
            root = document;
            FillRoot(root);
            root.style.backgroundColor = Ink;
            if (font)
                root.style.unityFontDefinition = FontDefinition.FromFont(font);

            var backdrop = Box(0, 0, 0, 0);
            FillRoot(backdrop);
            Image(backdrop, Get("backgrounds/castle-courtyard"), ScaleMode.ScaleAndCrop);
            backdrop.style.opacity = .18f;
            root.Add(backdrop);

            stage = Box(0, 0, Width, Height);
            root.Add(stage);
            root.RegisterCallback<GeometryChangedEvent>(_ => Layout(true));
            var bezel = Box(Left - 3, BoardTop - 3, BattlefieldView.Width + 6, BattlefieldView.Height + 6);
            bezel.style.backgroundColor = (Color)new Color32(199, 157, 94, 255);
            Round(bezel, 5);
            stage.Add(bezel);
            board = new BattlefieldView(art, Get("frames/ground-shadow"));
            Place(board, Left, BoardTop, BattlefieldView.Width, BattlefieldView.Height);
            stage.Add(board);
            board.RegisterCallback<PointerDownEvent>(BoardDown);
            board.RegisterCallback<PointerMoveEvent>(BoardMove);
            board.RegisterCallback<PointerUpEvent>(BoardUp);
            board.RegisterCallback<PointerCancelEvent>(e => EndBoardPointer(e.pointerId));
            board.RegisterCallback<PointerCaptureOutEvent>(e => EndBoardPointer(e.pointerId));
            board.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (boardPointer < 0 && dragPointer < 0)
                    ClearPoint();
            });

            var header = Box(Left, 4, BattlefieldView.Width, 47);
            stage.Add(header);
            MakePrinceMeter(header, 1, 0, "敵方王子", Coral);
            MakePrinceMeter(header, 0, BattlefieldView.Width - 162, "我方王子", Cyan);
            timer = Text(header, "", 203, 0, 89, 45, 24, Gold);
            var pauseButton = ActionButton(header, "暫停", 164, 9, 50, 30, pause, false);
            pauseButton.style.fontSize = 13;

            countdown = Text(stage, "", Left + 10, BoardTop + 315, BattlefieldView.Width - 20, 102, 51, Ivory);
            countdown.style.backgroundColor = (Color)new Color32(13, 26, 42, 205);
            hazard = Text(stage, "中央裂痕即將爆發", Left + 135, BoardTop + 345, 225, 32, 17, Ink);
            hazard.style.backgroundColor = Gold;
            Round(hazard, 12);

            var dock = Box(Left, DockTop, BattlefieldView.Width, 121);
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

        internal void AttachMatch(BattleMatch value)
        {
            ClearAim();
            HideDialog();
            match = value;
            messageEnd = 0;
            board.AttachMatch(value);
            RefreshHud();
        }

        internal void Present(BattlePresentation presentation)
        {
            Layout();
            board.Present(presentation);
            RefreshHud();
            if (selected != null && hasTarget)
                RefreshAim();
        }

        internal void ShowDialog(bool result)
        {
            ClearAim();
            dialog.style.display = DisplayStyle.Flex;
            title.text = result ? (match.Winner == BattleMatch.Draw ? "平手" : match.Winner == 0 ? "勝利" : "戰敗") : "暫停對練";
            continueButton.text = result ? "再戰一場" : "繼續戰鬥";
            continueButton.Focus();
        }

        internal void HideDialog() => dialog.style.display = DisplayStyle.None;
        private bool DialogOpen => dialog.style.display.value == DisplayStyle.Flex;
        private bool CanSelect(int slot) => match != null && match.Live && !DialogOpen && dragPointer < 0 && boardPointer < 0 && match.Hand(0, slot) != null;

        private void Continue()
        {
            if (match.Winner == BattleMatch.Playing)
                resume();
            else
                rematch();
        }

        private void Sprint()
        {
            if (DialogOpen || !match.TrySprint(0))
                return;
            playCue("confirm");
            RefreshHud();
        }

        private void ToggleSlot(int slot)
        {
            if (!CanSelect(slot))
                return;
            if (slot == selectedSlot)
                ClearAim();
            else
            {
                SelectSlot(slot);
                playCue("tap");
            }
            RefreshHud();
        }

        private void SelectSlot(int slot)
        {
            selectedSlot = slot;
            selected = match.Hand(0, slot);
            messageEnd = 0;
            hasOrigin = false;
            ClearPoint();
        }

        private void BeginDrag(int slot, int pointer)
        {
            if (!CanSelect(slot))
                return;
            dragPointer = pointer;
            SelectSlot(slot);
        }

        private void Drag(int pointer, Vector2 at)
        {
            if (pointer == dragPointer)
                AimAt(board.World(at));
        }

        private void EndDrag(int pointer, Vector2 at)
        {
            if (pointer != dragPointer)
                return;
            dragPointer = -1;
            Commit(board.World(at));
        }

        private void CancelDrag(int pointer)
        {
            if (pointer != dragPointer)
                return;
            dragPointer = -1;
            ClearAim();
        }

        private void BoardDown(PointerDownEvent evt)
        {
            if (dragPointer >= 0 || boardPointer >= 0 || selected == null || DialogOpen ||
                evt.pointerType == UnityEngine.UIElements.PointerType.mouse && evt.button != 0)
                return;

            boardPointer = evt.pointerId;
            hasOrigin = false;
            AimAt(board.World(evt.position));
            board.CapturePointer(boardPointer);
            evt.StopImmediatePropagation();
        }

        private void BoardMove(PointerMoveEvent evt)
        {
            if (selected == null || dragPointer >= 0 || boardPointer >= 0 && boardPointer != evt.pointerId)
                return;
            AimAt(board.World(evt.position));
        }

        private void BoardUp(PointerUpEvent evt)
        {
            if (evt.pointerId != boardPointer)
                return;
            int released = boardPointer;
            boardPointer = -1;
            board.ReleasePointer(released);
            Commit(board.World(evt.position));
            evt.StopImmediatePropagation();
        }

        private void EndBoardPointer(int pointer)
        {
            if (pointer != boardPointer)
                return;
            boardPointer = -1;
            hasOrigin = false;
            ClearPoint();
        }

        private void AimAt(WorldPoint location)
        {
            if (selected == null || !Battlefield.Contains(location))
            {
                ClearPoint();
                return;
            }

            hasTarget = true;
            point = selected.IsSpell ? location : Battlefield.Cell(Battlefield.Column(location), Battlefield.Row(location));
            direction = WorldPoint.UnitY;
            if (selected.Skill == Skill.Rift && (boardPointer >= 0 || hasOrigin))
            {
                if (!hasOrigin)
                {
                    origin = location;
                    hasOrigin = true;
                }
                WorldPoint aim = location - origin;
                if (aim.LengthSquared() >= .04f)
                    direction = WorldPoint.Normalize(aim);
                point = origin;
            }
            RefreshAim();
        }

        private void Commit(WorldPoint location)
        {
            AimAt(location);
            PlayResult result = hasTarget ? match.Play(0, selectedSlot, selected, point, direction) : PlayResult.Bounds;
            if (result == PlayResult.Accepted)
            {
                playCue("deploy");
                ClearAim();
                return;
            }
            hasOrigin = false;
            if (!hasTarget)
                ClearPoint();
            hint.text = result switch
            {
                PlayResult.Energy => "能量不足",
                PlayResult.Territory => "此格尚未解鎖",
                PlayResult.Occupied => "此格已有角色",
                PlayResult.UnitLimit => "戰場兵力已達上限",
                PlayResult.Bounds => "請在戰場內放開",
                _ => "請重新選牌"
            };
            hint.style.color = Coral;
            messageEnd = match.Tick + BattleMatch.TickRate * 2;
        }

        private void RefreshAim()
        {
            bool valid = match.Validate(0, selectedSlot, selected, point) == PlayResult.Accepted;
            board.MarkAim(selected, hasTarget, valid, point, direction);
        }

        private void ClearPoint()
        {
            hasTarget = false;
            board.MarkAim(selected, false, false, default, default);
        }

        internal bool ClearAim()
        {
            bool active = selected != null;
            int pointer = boardPointer;
            dragPointer = boardPointer = selectedSlot = -1;
            if (pointer >= 0 && board.HasPointerCapture(pointer))
                board.ReleasePointer(pointer);
            foreach (DuelCardDrag drag in drags)
                drag.Cancel();
            hasOrigin = false;
            selected = null;
            messageEnd = 0;
            ClearPoint();
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
            int remaining = Mathf.CeilToInt((BattleMatch.DurationTicks - match.Elapsed) / (float)BattleMatch.TickRate);
            Write(timer, $"{remaining / 60:00}:{remaining % 60:00}");
            for (int seat = 0; seat < 2; seat++)
            {
                BattleUnit prince = match.Prince(seat);
                Write(princeHealth[seat], $"{Mathf.CeilToInt(prince.Health)} / {(int)prince.Card.Health}");
                princeBars[seat].style.width = Length.Percent(prince.Health / prince.Card.Health * 100);
            }
            Write(energyValue, $"能量 {match.Energy(0):0.0} / 10");
            energyBar.style.width = Length.Percent((float)(match.Energy(0) / BattleMatch.MaxEnergy * 100));
            int sprintLeft = Mathf.CeilToInt(match.SprintTicksLeft(0) / (float)BattleMatch.TickRate);
            Write(sprint, sprintLeft == 0 ? "王子疾行" : $"疾行 {sprintLeft}s");
            sprint.SetEnabled(match.Live && sprintLeft == 0 && !DialogOpen);
            bool starting = match.Tick < BattleMatch.OpeningTicks;
            countdown.style.display = starting ? DisplayStyle.Flex : DisplayStyle.None;
            hazard.style.display = match.HazardWarning ? DisplayStyle.Flex : DisplayStyle.None;
            if (starting)
                Write(countdown, Mathf.CeilToInt((BattleMatch.OpeningTicks - match.Tick) / (float)BattleMatch.TickRate).ToString());
            if (match.Tick >= messageEnd)
            {
                hint.style.color = Ivory;
                Write(hint, selected == null ? "點選或拖曳手牌" : selected.Skill == Skill.Rift ? "選牌後在戰場拖曳定向" :
                    selected.IsSpell ? "放開施法" : "放開部署");
            }
            for (int i = 0; i < hand.Length; i++)
            {
                BattleCard card = match.Hand(0, i);
                PaintCard(hand[i], card, false);
                Image(hand[i].Frame, i == selectedSlot ? selectedArt : cardArt, ScaleMode.StretchToFill);
                hand[i].Button.style.opacity = match.Live && match.Energy(0) + 1e-8 >= card.Cost ? 1f : .58f;
            }
            PaintCard(next, match.NextCard(0), true);
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
            float scale = Mathf.Min((bottomRight.x - topLeft.x) / Width, (bottomRight.y - topLeft.y) / Height);
            Vector2 center = (topLeft + bottomRight) * .5f;
            stage.style.left = center.x - Width / 2;
            stage.style.top = center.y - Height / 2;
            stage.style.scale = new Scale(new Vector2(scale, scale));
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
