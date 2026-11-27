using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace KingdomRhapsody
{
    [DisallowMultipleComponent]
    public sealed class PortraitLobby : MonoBehaviour
    {
        private enum Cue { Silent, Tap, Back, Confirm, Deploy }

        private static readonly Vector2 DesignSize = new Vector2(1000, 1568);
        private const float HeaderHeight = 248f;
        private const float LobbyFooterHeight = 648f;
        private const float FormationHeight = 685f;
        private const float DeckFooterHeight = 185f;
        private const float DeckContentHeight = 1116f;
        private const float MiddlePadding = 8f;
        // Formation-local coordinates keep the three characters on a shared ground line.
        private static readonly Rect LeftPosition = new Rect(77, 125, 306, 520);
        private static readonly Rect RightPosition = new Rect(625, 125, 300, 520);
        private static readonly Color Ivory = new Color32(255, 243, 207, 255);
        private static readonly Color Gold = new Color32(255, 220, 129, 255);
        private static readonly Color Ink = new Color32(55, 30, 17, 255);
        private const string SaveKey = "kingdom-rhapsody.lobby.v1";

        private sealed class Card
        {
            internal Button Button;
            internal Image Portrait;
            internal Image Frame;
            internal Image Check;
            internal Text Label;
            internal Rect PortraitBounds;
        }

        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private LobbyLoadout loadout;
        private AudioSource audioSource;
        private AudioClip[] clips;
        private Font font;
        private Canvas canvas;
        private Image background;
        private AspectRatioFitter backgroundFit;
        private CanvasGroup pages;
        private CanvasGroup overlay;
        private RectTransform viewport;
        private RectTransform dialogViewport;
        private RectTransform home;
        private RectTransform formation;
        private RectTransform deck;
        private RectTransform deckContent;
        private RectTransform guardDialog;
        private RectTransform soundDialog;
        private RectTransform deployDialog;
        private RectTransform activeDialog;
        private Button goToDeck;
        private Button backToLobby;
        private Button soundButton;
        private Button departBack;
        private Button swap;
        private GameObject priorFocus;
        private Image leftGuard;
        private Image rightGuard;
        private Card leftTile;
        private Card rightTile;
        private Card[] guardOptions;
        private Card[] deckCards;
        private Card[] reserveCards;
        private string[] reserveIds;
        private Text guardTitle;
        private Text soundState;
        private Text deploySummary;
        private Text swapLabel;
        private int deckSlot = -1;
        private string replacement;
        private bool editLeft;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private float lastScale;

        public event Action<BattleLoadout> BattleRequested;
        public BattleLoadout CurrentLoadout => loadout?.Snapshot();

        private void Awake()
        {
            loadout = new LobbyLoadout();
            string cache = PlayerPrefs.GetString(SaveKey);
            if (!string.IsNullOrEmpty(cache))
            {
                try { JsonUtility.FromJsonOverwrite(cache, loadout); }
                catch (ArgumentException) { loadout = new LobbyLoadout(); }
            }
            loadout.Normalize();

            font = Resources.Load<Font>("Lobby/Fonts/KingdomUiTC");
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.73f;
            audioSource.mute = !loadout.soundEnabled;
            clips = new[] { null, Clip("tap"), Clip("back"), Clip("confirm"), Clip("deploy") };

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }

            BuildCanvas();
            BuildLobby();
            BuildDeck();
            BuildDialogs();
            RefreshGuards();
            FitSafeArea();
            ShowLobby();
        }

        private void OnDestroy()
        {
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        private static AudioClip Clip(string name) => Resources.Load<AudioClip>("Lobby/Sounds/" + name);

        private void Play(Cue cue)
        {
            AudioClip clip = clips[(int)cue];
            if (loadout.soundEnabled && clip != null)
                audioSource.PlayOneShot(clip);
        }

        private void Save()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(loadout));
            PlayerPrefs.Save();
        }

        private void BuildCanvas()
        {
            var root = new GameObject("LobbyCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = DesignSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            RectTransform backdrop = Stretch(root.transform, "Background");
            // A wider-than-art viewport shows black at the sides instead of cropping vertically.
            var matte = backdrop.gameObject.AddComponent<Image>();
            matte.color = Color.black;
            matte.raycastTarget = false;

            // Keep the full artwork height visible and reveal more of its width on wider screens.
            RectTransform picture = Node(backdrop, "Artwork");
            picture.anchorMin = new Vector2(.5f, 0f);
            picture.anchorMax = new Vector2(.5f, 1f);
            picture.pivot = new Vector2(.5f, .5f);
            picture.sizeDelta = Vector2.zero;
            picture.anchoredPosition = Vector2.zero;
            background = picture.gameObject.AddComponent<Image>();
            background.raycastTarget = false;
            background.preserveAspect = true;
            backgroundFit = picture.gameObject.AddComponent<AspectRatioFitter>();
            backgroundFit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            RectTransform screens = Stretch(root.transform, "Pages");
            pages = screens.gameObject.AddComponent<CanvasGroup>();
            viewport = Box(screens, "SafeArea", new Rect(Vector2.zero, DesignSize));
        }

        private void BuildLobby()
        {
            home = Stretch(viewport, "Lobby");

            RectTransform header = DesignGroup(home, "Header", new Vector2(.5f, 1f), HeaderHeight);
            Header(header, "GameTitle", "王國狂想", new Rect(235, 14, 530, 234), true);
            Button settings = IconButton(home, "SettingsButton", "gear", new Rect(0, 0, 88, 88), ShowSound, Cue.Tap);
            Anchor((RectTransform)settings.transform, new Vector2(1f, 1f), new Vector2(-30f, -48f));

            RectTransform middle = MiddleRegion(home, "CharacterRegion", HeaderHeight, LobbyFooterHeight);
            formation = DesignGroup(middle, "Formation", new Vector2(.5f, .5f), FormationHeight);
            Picture(formation, "LeftShadow", "frames/ground-shadow", new Rect(91, 610, 273, 60));
            Picture(formation, "RightShadow", "frames/ground-shadow", new Rect(636, 610, 266, 60));
            Picture(formation, "PrinceShadow", "frames/ground-shadow", new Rect(302, 617, 394, 68));
            leftGuard = Picture(formation, "LeftGuard", "characters/royal-spearman", LeftPosition);
            rightGuard = Picture(formation, "RightGuard", "characters/royal-crossbowman", RightPosition);
            var princeRect = new Rect(258, 0, 484, 652);
            Image prince = Picture(formation, "Prince", "characters/prince", princeRect);
            Fit(prince, prince.sprite, princeRect, true);

            RectTransform footer = DesignGroup(home, "Footer", new Vector2(.5f, 0f), LobbyFooterHeight);
            Picture(footer, "GuardPanel", "frames/stone-panel", new Rect(50, 38, 900, 352), true);
            Header(footer, "GuardHeader", "護衛配置", new Rect(286, 0, 428, 103));
            leftTile = GuardTile(footer, "LeftGuardButton", "左護衛", new Rect(95, 144, 397, 161), true);
            rightTile = GuardTile(footer, "RightGuardButton", "右護衛", new Rect(508, 144, 397, 161), false);

            goToDeck = MakeButton(footer, "TroopDeckButton", "buttons/secondary-blue",
                new Rect(56, 450, 386, 137), ShowDeck, Cue.Tap, out RectTransform deckArt, out _);
            Picture(deckArt, "CardsIcon", "icons/card-stack", new Rect(39, 39, 63, 63)).preserveAspect = true;
            Label(deckArt, "Label", "帶兵牌組", new Rect(101, 0, 253, 128), 50, Ivory);
            TextButton(footer, "DeployButton", "出戰", "buttons/primary-gold",
                new Rect(453, 438, 491, 159), 81, Ink, RequestBattle, Cue.Deploy, out _);
        }

        private Card GuardTile(Transform parent, string name, string role, Rect rect, bool left)
        {
            Button button = MakeButton(parent, name, "buttons/guard-slot", rect,
                () => ShowGuards(left), Cue.Tap, out RectTransform visual, out _);
            var portraitRect = new Rect(23, 12, 144, 135);
            Image portrait = Picture(visual, "Portrait", null, portraitRect);
            Label(visual, "Role", role, new Rect(174, 18, 163, 51), 30, new Color32(177, 198, 226, 255), TextAnchor.MiddleLeft);
            Text value = Label(visual, "UnitName", "", new Rect(172, 64, 175, 73), 47, Ivory, TextAnchor.MiddleLeft);
            Picture(visual, "OpenIcon", "icons/disclosure-down", new Rect(349, 68, 26, 22)).preserveAspect = true;
            return new Card { Button = button, Portrait = portrait, PortraitBounds = portraitRect, Label = value };
        }

        private void BuildDeck()
        {
            deck = Stretch(viewport, "TroopDeck");
            RectTransform header = DesignGroup(deck, "Header", new Vector2(.5f, 1f), HeaderHeight);
            Header(header, "PageTitle", "帶兵牌組", new Rect(235, 14, 530, 234), true);
            backToLobby = IconButton(deck, "BackButton", "back", new Rect(0, 0, 88, 88), ShowLobby, Cue.Back);
            Anchor((RectTransform)backToLobby.transform, new Vector2(0f, 1f), new Vector2(33f, -52f));

            RectTransform middle = MiddleRegion(deck, "CardRegion", HeaderHeight, DeckFooterHeight);
            deckContent = DesignGroup(middle, "CardContent", new Vector2(.5f, .5f), DeckContentHeight);
            Picture(deckContent, "DeckPanel", "frames/stone-panel", new Rect(65, 27, 870, 625), true);
            Header(deckContent, "DeckHeader", "出戰牌組", new Rect(287, 0, 426, 103));
            Label(deckContent, "DeckCount", Roster.DeckSize + "/" + Roster.DeckSize, new Rect(789, 45, 94, 60), 36, Ivory);

            deckCards = new Card[Roster.DeckSize];
            for (int i = 0; i < deckCards.Length; i++)
            {
                int slot = i;
                deckCards[i] = MakeCard(deckContent, "Deck" + (i + 1),
                    new Rect(111 + i % 4 * 199, 108 + i / 4 * 252, 181, 240), 31,
                    () => { deckSlot = slot; RefreshDeck(); }, Cue.Tap);
            }

            Picture(deckContent, "ReservesPanel", "frames/stone-panel", new Rect(65, 732, 870, 384), true);
            Header(deckContent, "ReservesHeader", "兵種收藏", new Rect(287, 701, 426, 103));
            reserveCards = new Card[Roster.Troops.Length - Roster.DeckSize];
            for (int i = 0; i < reserveCards.Length; i++)
            {
                int slot = i;
                reserveCards[i] = MakeCard(deckContent, "Reserve" + (i + 1), new Rect(170 + i * 222, 810, 216, 280), 38,
                    () => { replacement = reserveIds[slot]; RefreshDeck(); }, Cue.Tap);
            }
            RectTransform footer = DesignGroup(deck, "Footer", new Vector2(.5f, 0f), DeckFooterHeight);
            swap = TextButton(footer, "ReplaceCardButton", "", "buttons/primary-gold",
                new Rect(218, 0, 564, 140), 50, Ink, Replace, Cue.Confirm, out swapLabel);
        }

        private Card MakeCard(Transform parent, string name, Rect rect, int size, Action click, Cue cue)
        {
            var card = new Card();
            card.Button = MakeButton(parent, name, "frames/card-field", rect, click, cue, out RectTransform art, out Image field);
            field.color = new Color32(33, 48, 76, 255);
            card.PortraitBounds = new Rect(rect.width * .11f, rect.height * .095f, rect.width * .78f, rect.height * .68f);
            card.Portrait = Picture(art, "Portrait", null, card.PortraitBounds);
            card.Frame = Picture(art, "Frame", "frames/card-gold", new Rect(0, 0, rect.width, rect.height));
            card.Label = Label(art, "Name", "", new Rect(rect.width * .095f, rect.height * .77f,
                rect.width * .81f, rect.height * .17f), size, Ivory);
            card.Check = Picture(art, "Check", "icons/check", new Rect(rect.width * .725f, rect.height * .10f,
                rect.width * .16f, rect.width * .13f));
            return card;
        }

        private void SetCard(Card card, string art, string name, string frame, bool check)
        {
            Fit(card.Portrait, Sprite(art), card.PortraitBounds, true);
            card.Label.text = name;
            card.Frame.sprite = Sprite("frames/" + frame);
            card.Check.gameObject.SetActive(check);
        }

        private void BuildDialogs()
        {
            RectTransform layer = Stretch(canvas.transform, "Dialogs");
            overlay = layer.gameObject.AddComponent<CanvasGroup>();
            var scrim = Stretch(layer, "DismissBackdrop").gameObject.AddComponent<Image>();
            scrim.color = new Color(.02f, .04f, .08f, .74f);
            var dismiss = scrim.gameObject.AddComponent<Button>();
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => { Play(Cue.Back); CloseDialog(); });
            dialogViewport = Box(layer, "SafeArea", new Rect(Vector2.zero, DesignSize));

            guardDialog = Dialog("GuardOptions", "", new Rect(159, 624, 682, 416), out guardTitle);
            guardOptions = new[]
            {
                MakeCard(guardDialog, "Spearman", new Rect(69, 65, 236, 316), 44,
                    () => ChooseGuard(RoyalGuard.Spearman), Cue.Confirm),
                MakeCard(guardDialog, "Crossbowman", new Rect(377, 65, 236, 316), 44,
                    () => ChooseGuard(RoyalGuard.Crossbowman), Cue.Confirm)
            };

            soundDialog = Dialog("SoundSettings", "音效設定", new Rect(165, 600, 670, 327), out _);
            Label(soundDialog, "SoundLabel", "按鈕音效", new Rect(56, 82, 558, 67), 47, Ivory);
            soundButton = TextButton(soundDialog, "SoundToggle", "", "buttons/secondary-blue",
                new Rect(151, 174, 368, 108), 49, Gold, ToggleSound, Cue.Silent, out soundState);

            deployDialog = Dialog("Deployment", "出戰配置", new Rect(125, 550, 750, 447), out _);
            deploySummary = Label(deployDialog, "Loadout", "", new Rect(52, 68, 646, 239), 32, Ivory);
            departBack = TextButton(deployDialog, "ReturnButton", "返回", "buttons/secondary-blue",
                new Rect(215, 324, 320, 100), 49, Gold, CloseDialog, Cue.Back, out _);

            guardDialog.gameObject.SetActive(false);
            soundDialog.gameObject.SetActive(false);
            deployDialog.gameObject.SetActive(false);
            layer.gameObject.SetActive(false);
        }

        private RectTransform Dialog(string id, string name, Rect rect, out Text heading)
        {
            RectTransform panel = Box(dialogViewport, id, rect);
            Picture(panel, "Panel", "frames/guard-popup-panel", new Rect(0, 0, rect.width, rect.height), true).raycastTarget = true;
            heading = Header(panel, "Header", name, new Rect(rect.width * .26f, -40, rect.width * .48f, 88));
            IconButton(panel, "CloseButton", "close", new Rect(rect.width - 83, 13, 56, 56), CloseDialog, Cue.Back);
            return panel;
        }

        private void ShowLobby()
        {
            deck.gameObject.SetActive(false);
            home.gameObject.SetActive(true);
            Backdrop("castle-courtyard");
            Focus(goToDeck);
        }

        private void ShowDeck()
        {
            deckSlot = -1;
            replacement = null;
            home.gameObject.SetActive(false);
            deck.gameObject.SetActive(true);
            Backdrop("royal-armory");
            RefreshDeck();
            Focus(backToLobby);
        }

        private void Backdrop(string id)
        {
            background.sprite = Sprite("backgrounds/" + id);
            background.enabled = background.sprite != null;
            if (background.sprite != null)
                backgroundFit.aspectRatio = background.sprite.rect.width / background.sprite.rect.height;
        }

        private void RefreshGuards()
        {
            RefreshGuard(leftGuard, leftTile, loadout.leftGuard, LeftPosition);
            RefreshGuard(rightGuard, rightTile, loadout.rightGuard, RightPosition);
        }

        private void RefreshGuard(Image full, Card tile, RoyalGuard guard, Rect rect)
        {
            string id = Roster.SpriteId(guard);
            Fit(full, Sprite("characters/" + id), rect, true);
            Fit(tile.Portrait, Sprite("portraits/" + id), tile.PortraitBounds);
            tile.Label.text = Roster.Name(guard);
        }

        private void ShowGuards(bool left)
        {
            editLeft = left;
            guardTitle.text = left ? "左護衛" : "右護衛";
            RoyalGuard chosen = left ? loadout.leftGuard : loadout.rightGuard;
            for (int i = 0; i < guardOptions.Length; i++)
            {
                var guard = (RoyalGuard)i;
                bool selected = chosen == guard;
                SetCard(guardOptions[i], "portraits/" + Roster.SpriteId(guard), Roster.Name(guard),
                    selected ? "card-gold-selected" : "card-steel", selected);
            }
            OpenDialog(guardDialog, guardOptions[(int)chosen].Button);
        }

        private void ChooseGuard(RoyalGuard guard)
        {
            if (editLeft)
                loadout.leftGuard = guard;
            else
                loadout.rightGuard = guard;
            Save();
            RefreshGuards();
            CloseDialog();
        }

        private void RefreshDeck()
        {
            reserveIds = loadout.Reserves();
            for (int i = 0; i < deckCards.Length; i++)
            {
                string id = loadout.deck[i];
                SetCard(deckCards[i], "portraits/troops/" + id, Roster.Name(id),
                    i == deckSlot ? "card-gold-selected" : "card-gold", false);
            }
            for (int i = 0; i < reserveCards.Length; i++)
            {
                string id = reserveIds[i];
                bool selected = id == replacement;
                SetCard(reserveCards[i], "portraits/troops/" + id, Roster.Name(id),
                    selected ? "card-cyan-selected" : "card-gold", selected);
            }
            swap.interactable = loadout.CanReplace(deckSlot, replacement);
            swapLabel.text = swap.interactable ? "換上" + Roster.Name(replacement) :
                deckSlot < 0 ? "選擇要換下的卡" : "選擇要換上的兵種";
        }

        private void Replace()
        {
            if (!loadout.Replace(deckSlot, replacement))
                return;
            replacement = null;
            Save();
            RefreshDeck();
            Focus(deckCards[deckSlot].Button);
        }

        private void ShowSound()
        {
            RefreshSound();
            OpenDialog(soundDialog, soundButton);
        }

        private void ToggleSound()
        {
            loadout.soundEnabled = !loadout.soundEnabled;
            audioSource.mute = !loadout.soundEnabled;
            if (loadout.soundEnabled)
                Play(Cue.Confirm);
            else
                audioSource.Stop();
            RefreshSound();
            Save();
        }

        private void RefreshSound() => soundState.text = loadout.soundEnabled ? "已開啟" : "已靜音";

        private void RequestBattle()
        {
            BattleLoadout selected = loadout.Snapshot();
            if (BattleRequested != null)
            {
                BattleRequested.Invoke(selected);
                return;
            }

            var names = Array.ConvertAll(loadout.deck, id => Roster.Name(id));
            deploySummary.text = "左護衛 " + Roster.Name(selected.LeftGuard) + "  右護衛 " + Roster.Name(selected.RightGuard)
                + "\n\n" + string.Join("  ", names, 0, 4) + "\n" + string.Join("  ", names, 4, 4);
            OpenDialog(deployDialog, departBack);
        }

        private void OpenDialog(RectTransform panel, Button start)
        {
            priorFocus = EventSystem.current?.currentSelectedGameObject;
            activeDialog = panel;
            pages.interactable = pages.blocksRaycasts = false;
            overlay.alpha = 0f;
            overlay.gameObject.SetActive(true);
            panel.gameObject.SetActive(true);
            Focus(start);
        }

        private void CloseDialog()
        {
            if (activeDialog == null)
                return;

            activeDialog.gameObject.SetActive(false);
            activeDialog = null;
            overlay.gameObject.SetActive(false);
            pages.interactable = pages.blocksRaycasts = true;
            if (priorFocus != null && priorFocus.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(priorFocus);
            else
                Focus(leftTile.Button);
            priorFocus = null;
        }

        private static void Focus(Button button) => EventSystem.current?.SetSelectedGameObject(button.gameObject);

        private void LateUpdate()
        {
            FitSafeArea();
            if (activeDialog != null)
                overlay.alpha = Mathf.MoveTowards(overlay.alpha, 1f, Time.unscaledDeltaTime * 9f);
            if (!Input.GetKeyDown(KeyCode.Escape))
                return;
            if (activeDialog != null)
            {
                Play(Cue.Back);
                CloseDialog();
            }
            else if (deck.gameObject.activeSelf)
            {
                Play(Cue.Back);
                ShowLobby();
            }
        }

        private void FitSafeArea()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;
            var screen = new Vector2Int(Screen.width, Screen.height);
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0)
                safe = new Rect(0, 0, screen.x, screen.y);
            float scale = canvas.scaleFactor;
            if (lastScreenSize == screen && lastSafeArea == safe && Mathf.Approximately(lastScale, scale))
                return;
            lastScreenSize = screen;
            lastSafeArea = safe;
            lastScale = scale;

            var anchor = new Vector2(safe.center.x / screen.x, safe.center.y / screen.y);
            float pixelsPerUnit = Mathf.Min(safe.width / DesignSize.x, safe.height / DesignSize.y);
            float fit = pixelsPerUnit / Mathf.Max(.001f, scale);

            // Keep one uniform art scale, but let edge anchors use the entire safe area.
            Center(viewport, anchor, fit);
            Vector2 safeSize = safe.size / pixelsPerUnit;
            viewport.sizeDelta = safeSize;
            FitMiddle(formation, safeSize.y - HeaderHeight - LobbyFooterHeight);
            FitMiddle(deckContent, safeSize.y - HeaderHeight - DeckFooterHeight);

            // Modal artwork retains its fixed design canvas and remains centered.
            Center(dialogViewport, anchor, fit);
        }

        private static void Center(RectTransform rect, Vector2 anchor, float fit)
        {
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one * fit;
        }

        private Sprite Sprite(string id)
        {
            if (id == null)
                return null;
            if (!sprites.TryGetValue(id, out Sprite value))
            {
                value = Resources.Load<Sprite>("Lobby/" + id);
                sprites.Add(id, value);
            }
            return value;
        }

        private static RectTransform Node(Transform parent, string name)
        {
            var node = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Box(Transform parent, string name, Rect bounds)
        {
            RectTransform rect = Node(parent, name);
            Position(rect, bounds);
            return rect;
        }

        private static RectTransform Stretch(Transform parent, string name)
        {
            RectTransform rect = Node(parent, name);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset;
        }

        private static RectTransform DesignGroup(Transform parent, string name, Vector2 anchor, float height)
        {
            RectTransform rect = Node(parent, name);
            Anchor(rect, anchor, Vector2.zero);
            rect.sizeDelta = new Vector2(DesignSize.x, height);
            return rect;
        }

        private static RectTransform MiddleRegion(Transform parent, string name, float top, float bottom)
        {
            RectTransform rect = Stretch(parent, name);
            rect.offsetMin = new Vector2(0f, bottom);
            rect.offsetMax = new Vector2(0f, -top);
            return rect;
        }

        private static void FitMiddle(RectTransform rect, float availableHeight)
        {
            float factor = Mathf.Clamp01((availableHeight - 2f * MiddlePadding) / rect.sizeDelta.y);
            rect.localScale = Vector3.one * factor;
        }

        private static void Position(RectTransform rect, Rect bounds)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y);
            rect.sizeDelta = bounds.size;
        }

        private Image Picture(Transform parent, string name, string id, Rect bounds, bool sliced = false)
        {
            var image = Box(parent, name, bounds).gameObject.AddComponent<Image>();
            image.sprite = Sprite(id);
            image.raycastTarget = false;
            if (sliced)
                image.type = Image.Type.Sliced;
            return image;
        }

        private static void Fit(Image image, Sprite art, Rect bounds, bool bottom = false)
        {
            image.sprite = art;
            if (art == null)
                return;
            float factor = Mathf.Min(bounds.width / art.rect.width, bounds.height / art.rect.height);
            float w = art.rect.width * factor, h = art.rect.height * factor;
            Position(image.rectTransform, new Rect(bounds.x + (bounds.width - w) * .5f,
                bounds.y + (bounds.height - h) * (bottom ? 1f : .5f), w, h));
        }

        private Text Label(Transform parent, string id, string value, Rect rect, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = Box(parent, id, rect).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.text = value;
            if (color != Ink)
            {
                var outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color32(14, 17, 36, 225);
                outline.effectDistance = new Vector2(1.25f, -1.25f);
            }
            return label;
        }

        private Text Header(Transform parent, string id, string value, Rect rect, bool crown = false)
        {
            Image plate = Picture(parent, id, crown ? "frames/title-banner" : "frames/section-header", rect);
            Rect bounds = crown
                ? new Rect(rect.width * .12f, rect.height * .43f, rect.width * .76f, rect.height * .36f)
                : new Rect(0, 0, rect.width, rect.height * .94f);
            return Label(plate.transform, "Title", value, bounds, crown ? 64 : 50, Gold);
        }

        private Button MakeButton(Transform parent, string name, string id, Rect bounds, Action click, Cue cue,
            out RectTransform visual, out Image face)
        {
            RectTransform hit = Box(parent, name, bounds);
            var target = hit.gameObject.AddComponent<Image>();
            target.color = new Color(1f, 1f, 1f, 0.004f);
            target.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            visual = Stretch(hit, "Artwork");
            face = Picture(visual, "Face", id, new Rect(0, 0, bounds.width, bounds.height));
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color32(255, 246, 219, 255),
                selectedColor = new Color32(255, 239, 193, 255),
                pressedColor = new Color32(223, 184, 121, 255),
                disabledColor = new Color32(166, 167, 182, 200),
                colorMultiplier = 1f,
                fadeDuration = .09f
            };
            var feedback = hit.gameObject.AddComponent<LobbyPressFeedback>();
            feedback.Bind(visual);
            button.onClick.AddListener(() => { feedback.Pulse(); Play(cue); click(); });
            return button;
        }

        private Button IconButton(Transform parent, string name, string icon, Rect rect, Action click, Cue cue)
        {
            Button button = MakeButton(parent, name, "buttons/icon-button", rect, click, cue,
                out RectTransform art, out _);
            Picture(art, "Icon", "icons/" + icon, new Rect(rect.width * .23f, rect.height * .22f,
                rect.width * .54f, rect.height * .54f)).preserveAspect = true;
            return button;
        }

        private Button TextButton(Transform parent, string name, string value, string id, Rect rect,
            int size, Color color, Action click, Cue cue, out Text label)
        {
            Button button = MakeButton(parent, name, id, rect, click, cue, out RectTransform art, out _);
            label = Label(art, "Label", value, new Rect(12, 0, rect.width - 24, rect.height * .94f), size, color);
            return button;
        }
    }
}
