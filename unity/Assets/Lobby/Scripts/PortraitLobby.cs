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
        private const float DeckIdleFooterHeight = 40f;
        private const float DeckReplaceFooterHeight = 156f;
        private const float MiddlePadding = 8f;
        private const int CardColumns = 4;
        private const float TroopCardWidth = 181f;
        private const float TroopCardHeight = 240f;
        private const float CardColumnGap = 18f;
        private const float CardRowGap = 22f;
        private const float CardGridX = 111f;
        private const float ReservePanelY = 697f;
        private const float ReserveViewportX = 101f;
        private const float ReserveViewportY = 771f;
        private const float ReserveBottomInset = 30f;
        private const float ReserveGridPadding = 12f;
        private const float ReserveViewportWidth = 798f;
        private const float ReserveMinCardScale = .85f;
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
            internal RectTransform Visual;
            internal CanvasGroup VisualGroup;
            internal Outline Glow;
            internal int NameSize;
        }

        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, AudioClip> battleSounds = new Dictionary<string, AudioClip>();
        private LobbyLoadout loadout;
        private AudioSource audioSource;
        private AudioClip[] clips;
        private Font font;
        private Font extendedFont;
        private bool ownsExtendedFont;
        private bool attemptedExtendedFont;
        private bool warnedMissingGlyphs;
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
        private RectTransform deckMiddle;
        private RectTransform deckFooter;
        private RectTransform reservePanelRect;
        private RectTransform reserveGrid;
        private RectTransform reservePaging;
        private Button reservePrevious;
        private Button reserveNext;
        private Text reservePageNumber;
        private Text reserveEmpty;
        private int reservePage;
        private int reservePageSize = CardColumns;
        private float reserveCardScale = 1f;
        private RectTransform guardDialog;
        private RectTransform soundDialog;
        private RectTransform activeDialog;
        private Button goToDeck;
        private Button backToLobby;
        private Button soundButton;
        private RectTransform cardActionBackdrop;
        private RectTransform cardActionLayer;
        private RectTransform cardActionMenu;
        private CanvasGroup cardActionFade;
        private Button inspectCard;
        private Button addCard;
        private RectTransform replaceBar;
        private Text replaceHint;
        private Button cancelReplace;
        private RectTransform troopDetail;
        private Image detailPortrait;
        private Image detailShadow;
        private Text detailName;
        private Text detailStatus;
        private Text detailAction;
        private Text detailCost;
        private Button detailBack;
        private Button detailAdd;
        private string detailTroop;
        private readonly Vector3[] cardCorners = new Vector3[4];
        private GameObject priorFocus;
        private Image leftGuard;
        private Image rightGuard;
        private Image leftShadow;
        private Image rightShadow;
        private Card leftTile;
        private Card rightTile;
        private Card[] guardOptions;
        private Card[] deckCards;
        private Card[] reserveCards;
        [SerializeField, Tooltip("Fixed matchmaking URL. Leave empty to use the Web page origin or the local desktop server.")]
        private string battleServerUrl = "";
        private int ReservePageCount => Mathf.Max(1, (reserveCards.Length + reservePageSize - 1) / reservePageSize);
        private string[] reserveIds;
        private Text guardTitle;
        private Text soundState;
        private BattlePractice practice;
        private Card selectedCard;
        private string selectedTroop;
        private string pendingTroop;
        private float actionOpened;
        private int flashSlot = -1;
        private float flashStarted;
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
            ShowLobby();
            BattleRequested += BeginPractice;
        }

        private void OnDestroy()
        {
            BattleRequested -= BeginPractice;
            if (canvas != null)
                Destroy(canvas.gameObject);
            if (ownsExtendedFont && extendedFont != null)
                Destroy(extendedFont);
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
            // Bring the shadow cores up to the visible feet (the character sprites include transparent padding).
            leftShadow = Picture(formation, "LeftShadow", "frames/ground-shadow", new Rect(91, 565, 273, 60));
            rightShadow = Picture(formation, "RightShadow", "frames/ground-shadow", new Rect(636, 565, 266, 60));
            Sprite princeArt = Sprite("illustrations/characters/prince");
            bool illustratedPrince = princeArt != null;
            Picture(formation, "PrinceShadow", "frames/ground-shadow",
                new Rect(302, illustratedPrince ? 614 : 580, 394, 68));
            leftGuard = Picture(formation, "LeftGuard", "characters/royal-spearman", LeftPosition);
            rightGuard = Picture(formation, "RightGuard", "characters/royal-crossbowman", RightPosition);
            var princeRect = illustratedPrince ? new Rect(235, 0, 530, 652) : new Rect(258, 0, 484, 652);
            Image prince = Picture(formation, "Prince", null, princeRect);
            Fit(prince, princeArt ?? Sprite("characters/prince"), princeRect, true);

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
            // Place dismissal behind the cards so a tap can select a different card directly.
            cardActionBackdrop = Stretch(deck, "DismissCardActions");
            var outside = cardActionBackdrop.gameObject.AddComponent<Image>();
            outside.color = Color.clear;
            outside.raycastTarget = true;
            var dismiss = cardActionBackdrop.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = outside;
            dismiss.transition = Selectable.Transition.None;
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
            dismiss.onClick.AddListener(DismissCardActions);
            cardActionBackdrop.gameObject.SetActive(false);

            RectTransform header = DesignGroup(deck, "Header", new Vector2(.5f, 1f), HeaderHeight);
            Header(header, "PageTitle", "帶兵牌組", new Rect(235, 14, 530, 234), true);
            // The back sprite's visible arrow is optically left of its texture center.
            backToLobby = IconButton(deck, "BackButton", "back", new Rect(0, 0, 88, 88), ShowLobby, Cue.Back,
                opticalOffsetX: .08f);
            Anchor((RectTransform)backToLobby.transform, new Vector2(0f, 1f), new Vector2(33f, -52f));

            deckMiddle = MiddleRegion(deck, "CardRegion", HeaderHeight, DeckIdleFooterHeight);
            deckContent = DesignGroup(deckMiddle, "CardContent", new Vector2(.5f, 1f), DesignSize.y - HeaderHeight - DeckIdleFooterHeight);
            deckContent.anchoredPosition = new Vector2(0f, -MiddlePadding);
            Picture(deckContent, "DeckPanel", "frames/stone-panel", new Rect(65, 27, 870, 625), true);
            Header(deckContent, "DeckHeader", "出戰牌組", new Rect(287, 0, 426, 103));
            Label(deckContent, "DeckCount", Roster.DeckSize + "/" + Roster.DeckSize, new Rect(789, 45, 94, 60), 36, Ivory);

            deckCards = new Card[Roster.DeckSize];
            for (int i = 0; i < deckCards.Length; i++)
            {
                int slot = i;
                deckCards[i] = MakeCard(deckContent, "Deck" + (i + 1),
                    TroopCardRect(i, CardGridX, 108f), 31,
                    () => SelectDeckSlot(slot), Cue.Tap);
            }

            // The collection shares the active deck's four column centers.
            reservePanelRect = Picture(deckContent, "ReservesPanel", "frames/stone-panel",
                new Rect(65, ReservePanelY, 870, 490), true).rectTransform;
            Header(deckContent, "ReservesHeader", "卡牌收藏", new Rect(287, 665, 426, 103));
            BuildReserveCollection();
            deckFooter = DesignGroup(deck, "Footer", new Vector2(.5f, 0f), DeckIdleFooterHeight);
            replaceBar = Box(deckFooter, "ReplacePrompt", new Rect(50, 0, 900, 140));
            replaceHint = Label(replaceBar, "Hint", "", new Rect(0, 0, 620, 132), 38, Ivory);
            cancelReplace = TextButton(replaceBar, "CancelReplace", "取消", "buttons/secondary-blue",
                new Rect(650, 18, 245, 104), 44, Gold, CancelReplacement, Cue.Back, out _);
            replaceBar.gameObject.SetActive(false);

            cardActionLayer = Stretch(deck, "CardActionLayer");
            cardActionMenu = Box(cardActionLayer, "CardActions", new Rect(0, 0, 436, 104));
            cardActionFade = cardActionMenu.gameObject.AddComponent<CanvasGroup>();
            inspectCard = TextButton(cardActionMenu, "InspectCard", "看資訊", "buttons/secondary-blue",
                new Rect(0, 0, 208, 104), 40, Gold, InspectSelected, Cue.Tap, out _);
            addCard = TextButton(cardActionMenu, "AddCard", "上陣", "buttons/primary-gold",
                new Rect(228, 0, 208, 104), 44, Ink, AddSelected, Cue.Confirm, out _);
            cardActionLayer.gameObject.SetActive(false);
        }

        private static Rect TroopCardRect(int index, float x, float y)
        {
            return new Rect(x + index % CardColumns * (TroopCardWidth + CardColumnGap),
                y + index / CardColumns * (TroopCardHeight + CardRowGap), TroopCardWidth, TroopCardHeight);
        }

        private void BuildReserveCollection()
        {
            reserveGrid = Box(deckContent, "CollectionGrid",
                new Rect(ReserveViewportX, ReserveViewportY, ReserveViewportWidth, TroopCardHeight + 24f));
            var touchArea = reserveGrid.gameObject.AddComponent<Image>();
            touchArea.color = Color.clear;
            touchArea.raycastTarget = true;

            reservePaging = Box(deckContent, "CollectionPages", new Rect(101, 698, ReserveViewportWidth, 64));
            reservePrevious = IconButton(reservePaging, "PreviousPage", "back", new Rect(37, 0, 64, 64),
                () => TurnReservePage(-1), Cue.Tap);
            reserveNext = IconButton(reservePaging, "NextPage", "back", new Rect(696, 0, 64, 64),
                () => TurnReservePage(1), Cue.Tap, mirror: true);
            reservePageNumber = Label(reservePaging, "PageNumber", "", new Rect(610, 0, 82, 64), 28, Ivory);

            BindReservePageInput();

            reserveCards = new Card[Mathf.Max(0, Roster.Troops.Length - Roster.DeckSize)];
            for (int i = 0; i < reserveCards.Length; i++)
            {
                int slot = i;
                reserveCards[i] = MakeCard(reserveGrid, "Reserve" + (i + 1),
                    TroopCardRect(i, CardGridX - ReserveViewportX, ReserveGridPadding), 31,
                    () => SelectReserve(slot), Cue.Tap);
            }
            reserveEmpty = Label(reserveGrid, "EmptyCollection", "沒有未上陣的卡牌",
                new Rect(0, 0, ReserveViewportWidth, TroopCardHeight), 36, Ivory);
            reserveEmpty.gameObject.SetActive(reserveCards.Length == 0);
        }

        private void BindReservePageInput()
        {
            var trigger = reserveGrid.gameObject.AddComponent<EventTrigger>();
            var tap = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            tap.callback.AddListener(data =>
            {
                if (((PointerEventData)data).button == PointerEventData.InputButton.Left)
                    DismissCardActions();
            });
            trigger.triggers.Add(tap);

            var scroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            scroll.callback.AddListener(data =>
            {
                float delta = ((PointerEventData)data).scrollDelta.y;
                if (delta != 0f)
                    TurnReservePage(delta > 0f ? -1 : 1);
            });
            trigger.triggers.Add(scroll);

            var swipe = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
            swipe.callback.AddListener(data =>
            {
                var pointer = (PointerEventData)data;
                Vector2 delta = pointer.position - pointer.pressPosition;
                float direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? -delta.x : delta.y;
                float threshold = Mathf.Max(18f, 46f * canvas.scaleFactor * viewport.localScale.x);
                if (Mathf.Abs(direction) >= threshold)
                    TurnReservePage(direction > 0f ? 1 : -1);
            });
            trigger.triggers.Add(swipe);
        }

        private void TurnReservePage(int direction)
        {
            if (pendingTroop != null || activeDialog != null)
                return;
            int page = Mathf.Clamp(reservePage + direction, 0, ReservePageCount - 1);
            if (page == reservePage)
                return;
            CloseCardActions();
            reservePage = page;
            RefreshDeck();

            GameObject focused = EventSystem.current?.currentSelectedGameObject;
            if (focused == reservePrevious.gameObject && !reservePrevious.interactable)
                Focus(reserveNext);
            else if (focused == reserveNext.gameObject && !reserveNext.interactable)
                Focus(reservePrevious);
            else if (focused == null || !focused.activeInHierarchy)
                Focus(reserveCards[reservePage * reservePageSize].Button);
        }

        private Card MakeCard(Transform parent, string name, Rect rect, int size, Action click, Cue cue)
        {
            var card = new Card();
            card.Button = MakeButton(parent, name, "frames/card-field", rect, click, cue, out RectTransform art, out Image field);
            field.color = new Color32(33, 48, 76, 255);
            card.Visual = art;
            card.VisualGroup = art.gameObject.AddComponent<CanvasGroup>();
            // Reserve space for the frame and the separate name plate.
            card.PortraitBounds = new Rect(rect.width * .14f, rect.height * .095f, rect.width * .72f, rect.height * .59f);
            card.Portrait = Picture(art, "Portrait", null, card.PortraitBounds);
            card.Frame = Picture(art, "Frame", "frames/card-gold", new Rect(0, 0, rect.width, rect.height));
            card.Glow = card.Frame.gameObject.AddComponent<Outline>();
            card.Glow.effectDistance = new Vector2(4f, -4f);
            card.Glow.effectColor = new Color(1f, .75f, .18f, .8f);
            card.Glow.enabled = false;
            card.Label = Label(art, "Name", "", new Rect(rect.width * .12f, rect.height * .77f,
                rect.width * .76f, rect.height * .17f), size, Ivory);
            card.NameSize = size;
            card.Check = Picture(art, "Check", "icons/check", new Rect(rect.width * .725f, rect.height * .10f,
                rect.width * .16f, rect.width * .13f));
            return card;
        }

        private void SetCard(Card card, Sprite art, string name, string frame, bool check)
        {
            Fit(card.Portrait, art, card.PortraitBounds);
            if (card.Label.text != name)
            {
                SetLabelValue(card.Label, name);
                card.Label.fontSize = card.NameSize;
                float width = card.Label.rectTransform.rect.width - 4f;
                float preferredWidth = card.Label.preferredWidth;
                if (width > 0f && preferredWidth > width)
                    card.Label.fontSize = Mathf.Max(1, Mathf.FloorToInt(card.NameSize * width / preferredWidth));
            }
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

            BuildTroopDetail();
            guardDialog.gameObject.SetActive(false);
            soundDialog.gameObject.SetActive(false);
            layer.gameObject.SetActive(false);
        }

        private void BuildTroopDetail()
        {
            troopDetail = Dialog("TroopDetail", "卡牌資訊", new Rect(75, 220, 850, 1128), out _);
            Picture(troopDetail, "CharacterStage", "frames/card-field", new Rect(55, 93, 740, 532)).color =
                new Color32(28, 44, 75, 255);
            detailShadow = Picture(troopDetail, "StageShadow", "frames/ground-shadow", new Rect(262, 568, 326, 48));
            detailPortrait = Picture(troopDetail, "DetailPortrait", null, new Rect(98, 109, 654, 480));
            detailName = Label(troopDetail, "TroopName", "", new Rect(48, 635, 754, 80), 60, Gold);
            detailStatus = Label(troopDetail, "TroopStatus", "", new Rect(48, 710, 754, 60), 36, Ivory);
            detailAction = Label(troopDetail, "RepresentativeAction", "", new Rect(52, 792, 746, 78), 40, Ivory);
            detailCost = Label(troopDetail, "CardCost", "", new Rect(52, 874, 746, 55), 32,
                new Color32(177, 198, 226, 255));
            detailBack = TextButton(troopDetail, "DetailBack", "返回", "buttons/secondary-blue",
                new Rect(78, 976, 330, 112), 46, Gold, CloseDialog, Cue.Back, out _);
            detailAdd = TextButton(troopDetail, "DetailAdd", "上陣", "buttons/primary-gold",
                new Rect(442, 976, 330, 112), 46, Ink, AddFromDetail, Cue.Confirm, out _);
            troopDetail.gameObject.SetActive(false);
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
            ResetDeckInteraction();
            deck.gameObject.SetActive(false);
            home.gameObject.SetActive(true);
            Backdrop("castle-courtyard");
            Focus(goToDeck);
        }

        private void ShowDeck()
        {
            home.gameObject.SetActive(false);
            deck.gameObject.SetActive(true);
            Backdrop("royal-armory");
            ResetDeckInteraction();
            Focus(backToLobby);
        }

        private void Backdrop(string id)
        {
            background.sprite = Sprite("backgrounds/" + id);
            background.enabled = background.sprite != null;
            if (background.sprite != null)
                backgroundFit.aspectRatio = background.sprite.rect.width / background.sprite.rect.height;

            // A page may have a different background aspect ratio.
            FitSafeArea();
        }

        private void RefreshGuards()
        {
            RefreshGuard(leftGuard, leftShadow, leftTile, loadout.leftGuard, LeftPosition, true);
            RefreshGuard(rightGuard, rightShadow, rightTile, loadout.rightGuard, RightPosition, false);
        }

        private void RefreshGuard(Image full, Image shadow, Card tile, RoyalGuard guard, Rect rect, bool left)
        {
            string id = Roster.SpriteId(guard);
            bool mirror = left && guard == RoyalGuard.Crossbowman;
            Sprite illustration = Sprite("illustrations/characters/" + id);
            if (illustration != null)
            {
                // Wider, tightly cropped presentation art needs more room for its pose.
                rect = new Rect(left ? 24 : 620, rect.y, 356, rect.height);
                float shadowWidth = left ? 273f : 266f;
                Position(shadow.rectTransform, new Rect(rect.center.x - shadowWidth * .5f,
                    rect.yMax - 34f, shadowWidth, 60));
            }
            else
            {
                Position(shadow.rectTransform, left ? new Rect(91, 565, 273, 60) : new Rect(636, 565, 266, 60));
            }
            Fit(full, illustration ?? Sprite("characters/" + id), rect, bottom: true, mirror: mirror);
            Fit(tile.Portrait, CardPortrait("portraits/" + id), tile.PortraitBounds, mirror: mirror);
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
                SetCard(guardOptions[i], CardPortrait("portraits/" + Roster.SpriteId(guard)), Roster.Name(guard),
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

        private void ResetDeckInteraction()
        {
            CloseCardActions();
            pendingTroop = null;
            flashSlot = -1;
            reservePage = 0;
            RefreshDeck();
        }

        private void RefreshDeck()
        {
            reserveIds = loadout.Reserves();
            bool replacing = pendingTroop != null;
            for (int i = 0; i < deckCards.Length; i++)
            {
                Card card = deckCards[i];
                string id = loadout.deck[i];
                SetCard(card, TroopPortrait(id), Roster.Name(id),
                    replacing ? "card-gold-selected" : card == selectedCard ? "card-cyan-selected" : "card-gold",
                    false);
            }
            for (int i = 0; i < reserveCards.Length; i++)
            {
                Card card = reserveCards[i];
                string id = reserveIds[i];
                bool selected = replacing ? id == pendingTroop : card == selectedCard;
                SetCard(card, TroopPortrait(id), Roster.Name(id),
                    selected ? "card-cyan-selected" : "card-gold", false);
                card.VisualGroup.alpha = replacing && !selected ? .42f : 1f;
                card.Button.interactable = !replacing;
            }
            replaceBar.gameObject.SetActive(replacing);
            LayoutDeckRegions();
            if (replacing)
                SetLabelValue(replaceHint, "選擇要換下的卡：" + Roster.Name(pendingTroop));
            UpdateDeckEffects();
        }

        private void SelectDeckSlot(int slot)
        {
            if (pendingTroop == null)
            {
                OpenCardActions(deckCards[slot], loadout.deck[slot]);
                return;
            }
            if (!loadout.Replace(slot, pendingTroop))
            {
                CancelReplacement();
                return;
            }

            // The second tap commits the exchange immediately, never a confirmation modal.
            pendingTroop = null;
            flashSlot = slot;
            flashStarted = Time.unscaledTime;
            Save();
            RefreshDeck();
            Play(Cue.Confirm);
            Focus(deckCards[slot].Button);
        }

        private void SelectReserve(int slot)
        {
            if (pendingTroop == null)
                OpenCardActions(reserveCards[slot], reserveIds[slot]);
        }

        private bool IsReserve(string id) => loadout.CanReplace(0, id);

        private void OpenCardActions(Card card, string id)
        {
            CloseCardActions();
            selectedCard = card;
            selectedTroop = id;
            addCard.gameObject.SetActive(IsReserve(id));
            actionOpened = Time.unscaledTime;
            cardActionFade.alpha = 0f;
            cardActionBackdrop.gameObject.SetActive(true);
            cardActionLayer.gameObject.SetActive(true);
            cardActionLayer.SetAsLastSibling();
            RefreshDeck();
            Canvas.ForceUpdateCanvases();
            LayoutCardActions();
            Focus(inspectCard);
        }

        private void LayoutCardActions()
        {
            if (selectedCard == null || !cardActionLayer.gameObject.activeSelf)
                return;
            var tile = (RectTransform)selectedCard.Button.transform;
            tile.GetWorldCorners(cardCorners);
            Vector3 bottomLeft = cardActionLayer.InverseTransformPoint(cardCorners[0]);
            Vector3 topRight = cardActionLayer.InverseTransformPoint(cardCorners[2]);
            Rect safe = cardActionLayer.rect;
            float width = addCard.gameObject.activeSelf ? 436f : 208f;
            const float height = 104f;
            const float gap = 10f;
            const float margin = 16f;
            float centerX = (bottomLeft.x + topRight.x) * .5f - safe.xMin;
            float bottom = safe.yMax - bottomLeft.y;
            float top = safe.yMax - topRight.y;
            float x = Mathf.Clamp(centerX - width * .5f, margin,
                Mathf.Max(margin, safe.width - width - margin));
            bool below = bottom + gap + height <= safe.height - margin;
            float y = below ? bottom + gap : top - gap - height;
            float t = Mathf.Clamp01((Time.unscaledTime - actionOpened) / .14f);
            float eased = 1f - (1f - t) * (1f - t);
            y += (below ? -12f : 12f) * (1f - eased);
            y = Mathf.Clamp(y, margin, Mathf.Max(margin, safe.height - height - margin));
            Position(cardActionMenu, new Rect(x, y, width, height));
            cardActionFade.alpha = eased;
        }

        private void CloseCardActions()
        {
            selectedCard = null;
            selectedTroop = null;
            cardActionBackdrop.gameObject.SetActive(false);
            cardActionLayer.gameObject.SetActive(false);
        }

        private void DismissCardActions()
        {
            Card origin = selectedCard;
            if (origin == null)
                return;
            Play(Cue.Back);
            CloseCardActions();
            RefreshDeck();
            Focus(origin.Button);
        }

        private void AddSelected()
        {
            string id = selectedTroop;
            BeginReplacement(id);
        }

        private void BeginReplacement(string id)
        {
            if (!IsReserve(id))
                return;
            CloseCardActions();
            pendingTroop = id;
            flashSlot = -1;
            RefreshDeck();
            Focus(deckCards[0].Button);
        }

        private void CancelReplacement()
        {
            string id = pendingTroop;
            pendingTroop = null;
            RefreshDeck();
            int index = Array.IndexOf(reserveIds, id);
            if (index >= 0)
            {
                reservePage = index / reservePageSize;
                LayoutReservePage();
                Focus(reserveCards[index].Button);
            }
        }

        private void UpdateDeckEffects()
        {
            float now = Time.unscaledTime;
            bool replacing = pendingTroop != null;
            float flashTime = now - flashStarted;
            if (flashSlot >= 0 && flashTime >= .35f)
                flashSlot = -1;
            for (int i = 0; i < deckCards.Length; i++)
            {
                Card card = deckCards[i];
                bool flashing = flashSlot == i;
                float wave = Mathf.Sin(now * 8f + i * .65f);
                card.Visual.localRotation = replacing ? Quaternion.Euler(0f, 0f, 1.7f * wave) : Quaternion.identity;
                card.Glow.enabled = replacing || flashing;
                if (card.Glow.enabled)
                {
                    float alpha = replacing ? .52f + .30f * wave : Mathf.Clamp01(1f - flashTime / .35f);
                    card.Glow.effectColor = new Color(1f, .78f, .22f, alpha);
                }
                card.Frame.color = flashing ? Color.Lerp(Color.white, Gold, Mathf.Clamp01(1f - flashTime / .35f)) : Color.white;
            }
        }

        private void InspectSelected()
        {
            if (selectedCard == null || string.IsNullOrEmpty(selectedTroop))
                return;
            Card origin = selectedCard;
            string id = selectedTroop;
            CloseCardActions();
            RefreshDeck();
            Focus(origin.Button);

            detailTroop = id;
            bool reserve = IsReserve(id);
            BattleCard card = BattleCatalog.Find(id);
            detailShadow.enabled = !card.IsSpell;
            Fit(detailPortrait, TroopPortrait(id), new Rect(98, 109, 654, 480), bottom: !card.IsSpell);
            SetLabelValue(detailName, Roster.Name(id));
            SetLabelValue(detailStatus, reserve ? "未上陣" : "已上陣");
            SetLabelValue(detailAction, (card.IsSpell ? "法術：" : "代表動作：") + Roster.ActionName(id));
            SetLabelValue(detailCost, $"能量 {card.Cost} 點");
            detailAdd.gameObject.SetActive(reserve);
            Position((RectTransform)detailBack.transform,
                reserve ? new Rect(78, 976, 330, 112) : new Rect(260, 976, 330, 112));
            OpenDialog(troopDetail, detailBack);
        }

        private void AddFromDetail()
        {
            string id = detailTroop;
            CloseDialog();
            BeginReplacement(id);
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

        private void RequestBattle() => BattleRequested?.Invoke(loadout.Snapshot());

        private void BeginPractice(BattleLoadout selected)
        {
            if (practice != null)
                return;

            var battle = new GameObject("BattlePractice");
            battle.transform.SetParent(transform, false);
            practice = battle.AddComponent<BattlePractice>();
            canvas.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
            Font battleFont = Resources.Load<Font>("Lobby/Fonts/KingdomUiTCFull") ?? font;
            practice.Open(selected, battleFont, battleServerUrl, ReturnFromPractice, PlayBattleCue);
        }

        private void ReturnFromPractice()
        {
            practice = null;
            canvas.gameObject.SetActive(true);
            ShowLobby();
            Play(Cue.Back);
        }

        private void PlayBattleCue(string cue)
        {
            if (!loadout.soundEnabled)
                return;
            switch (cue)
            {
                case "tap": Play(Cue.Tap); return;
                case "confirm": Play(Cue.Confirm); return;
                case "deploy": Play(Cue.Deploy); return;
            }
            if (!battleSounds.TryGetValue(cue, out AudioClip clip))
            {
                clip = Resources.Load<AudioClip>("Battle/Sounds/" + cue);
                battleSounds.Add(cue, clip);
            }
            if (clip)
                audioSource.PlayOneShot(clip, .65f);
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

            if (activeDialog == troopDetail)
                detailTroop = null;
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
            if (practice != null)
                return;
            FitSafeArea();
            if (activeDialog != null)
                overlay.alpha = Mathf.MoveTowards(overlay.alpha, 1f, Time.unscaledDeltaTime * 9f);
            if (deck.gameObject.activeSelf)
            {
                LayoutCardActions();
                UpdateDeckEffects();
            }
            if (!Input.GetKeyDown(KeyCode.Escape))
                return;
            if (activeDialog != null)
            {
                Play(Cue.Back);
                CloseDialog();
            }
            else if (cardActionLayer.gameObject.activeSelf)
                DismissCardActions();
            else if (pendingTroop != null)
            {
                Play(Cue.Back);
                CancelReplacement();
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
            safe = VisibleArtworkSafeArea(screen, safe, background.sprite);
            float scale = canvas.scaleFactor;
            if (lastScreenSize == screen && lastSafeArea == safe && Mathf.Approximately(lastScale, scale))
                return;
            lastScreenSize = screen;
            lastSafeArea = safe;
            lastScale = scale;

            var anchor = new Vector2(safe.center.x / screen.x, safe.center.y / screen.y);
            float pixelsPerUnit = Mathf.Min(safe.width / DesignSize.x, safe.height / DesignSize.y);
            float fit = pixelsPerUnit / Mathf.Max(.001f, scale);

            // Keep edge anchors inside the visible artwork and the device safe area.
            Center(viewport, anchor, fit);
            Vector2 safeSize = safe.size / pixelsPerUnit;
            viewport.sizeDelta = safeSize;
            FitMiddle(formation, safeSize.y - HeaderHeight - LobbyFooterHeight);
            LayoutDeckRegions();

            // Modal artwork retains its fixed design canvas and remains centered.
            Center(dialogViewport, anchor, fit);
        }

        private void LayoutDeckRegions()
        {
            // Keep the active deck in place when the replacement controls appear.
            bool replacing = pendingTroop != null;
            float footerHeight = replacing ? DeckReplaceFooterHeight : DeckIdleFooterHeight;
            deckMiddle.offsetMin = new Vector2(0f, footerHeight);
            deckFooter.sizeDelta = new Vector2(DesignSize.x, footerHeight);

            float height = Mathf.Max(1f, viewport.rect.height - HeaderHeight - footerHeight - 2f * MiddlePadding);
            deckContent.sizeDelta = new Vector2(DesignSize.x, height);
            deckContent.localScale = Vector3.one;
            Position(reservePanelRect, new Rect(65, ReservePanelY, 870, Mathf.Max(1f, height - ReservePanelY)));

            float viewHeight = Mathf.Max(1f, height - ReserveViewportY - ReserveBottomInset);
            Position(reserveGrid, new Rect(ReserveViewportX, ReserveViewportY, ReserveViewportWidth, viewHeight));
            Position(reserveEmpty.rectTransform, new Rect(0, 0, ReserveViewportWidth, viewHeight));

            int selectedIndex = replacing ? Array.IndexOf(reserveIds, pendingTroop) : Array.IndexOf(reserveCards, selectedCard);
            int anchorIndex = selectedIndex >= 0 ? selectedIndex : reservePage * reservePageSize;
            int totalRows = Mathf.Max(1, (reserveCards.Length + CardColumns - 1) / CardColumns);
            float cardSpace = Mathf.Max(1f, viewHeight - 2f * ReserveGridPadding);
            int rowsThatFit = Mathf.FloorToInt((cardSpace / ReserveMinCardScale + CardRowGap) / (TroopCardHeight + CardRowGap));
            int rowsPerPage = Mathf.Clamp(rowsThatFit, 1, totalRows);
            reservePageSize = rowsPerPage * CardColumns;
            reserveCardScale = Mathf.Min(1f, cardSpace / ReserveRowsHeight(rowsPerPage));
            reservePage = Mathf.Clamp(anchorIndex / reservePageSize, 0, ReservePageCount - 1);
            LayoutReservePage();
        }

        private static float ReserveRowsHeight(int rows) => rows * TroopCardHeight + (rows - 1) * CardRowGap;

        private void LayoutReservePage()
        {
            int first = reservePage * reservePageSize;
            int cardsOnPage = Mathf.Min(reservePageSize, reserveCards.Length - first);
            int rows = Mathf.Max(1, (cardsOnPage + CardColumns - 1) / CardColumns);
            float rowsHeight = ReserveRowsHeight(rows) * reserveCardScale;
            float top = (reserveGrid.rect.height - rowsHeight) * .5f;
            for (int i = 0; i < reserveCards.Length; i++)
                LayoutReserveCard(reserveCards[i], i - first, top);
            int pages = ReservePageCount;
            bool browsing = pendingTroop == null;
            reservePaging.gameObject.SetActive(pages > 1);
            reservePrevious.interactable = browsing && reservePage > 0;
            reserveNext.interactable = browsing && reservePage + 1 < pages;
            reservePageNumber.text = (reservePage + 1) + " / " + pages;
        }

        private void LayoutReserveCard(Card card, int slot, float top)
        {
            var tile = (RectTransform)card.Button.transform;
            bool visible = slot >= 0 && slot < reservePageSize;
            tile.gameObject.SetActive(visible);
            if (!visible)
                return;
            float x = CardGridX - ReserveViewportX + (1f - reserveCardScale) * TroopCardWidth * .5f;
            Rect rect = TroopCardRect(slot, x, 0f);
            rect.y = top + rect.y * reserveCardScale;
            Position(tile, rect);
            tile.localScale = Vector3.one * reserveCardScale;
        }

        private static Rect VisibleArtworkSafeArea(Vector2Int screen, Rect safe, Sprite art)
        {
            if (art == null || art.rect.width <= 0f || art.rect.height <= 0f)
                return safe;

            // HeightControlsWidth centers the full-height artwork in screen space.
            // Intersect its visible horizontal span with the device's safe area,
            // so letterbox bars never become part of the UI layout.
            float artworkWidth = screen.y * (art.rect.width / art.rect.height);
            float inset = Mathf.Max(0f, (screen.x - artworkWidth) * .5f);
            float left = Mathf.Max(safe.xMin, inset);
            float right = Mathf.Min(safe.xMax, screen.x - inset);

            // A pathological safe area with no artwork intersection has no valid
            // shared region; retain the device-safe fallback instead of dividing by zero.
            if (right <= left)
                return safe;

            return Rect.MinMaxRect(left, safe.yMin, right, safe.yMax);
        }

        private static void Center(RectTransform rect, Vector2 anchor, float fit)
        {
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one * fit;
        }

        // Dedicated presentation portraits are independent of the battle sprite sheets.
        // Legacy assets remain usable until the optional illustration package is installed.
        private Sprite CardPortrait(string legacyPath, bool fullBody = false)
        {
            if (string.IsNullOrEmpty(legacyPath))
                return null;
            string id = legacyPath.Substring(legacyPath.LastIndexOf('/') + 1);
            // Troop cards use complete silhouettes: resizing a pre-cropped portrait
            // cannot recover weapons or a mount outside its source image.
            if (fullBody)
            {
                Sprite illustration = Sprite("illustrations/characters/" + id);
                if (illustration != null)
                    return illustration;
            }
            return Sprite("card-portraits/" + id) ?? Sprite(legacyPath);
        }

        private Sprite TroopPortrait(string id) => BattleCatalog.Find(id)?.IsSpell == true
            ? Sprite("illustrations/spells/" + id)
            : CardPortrait("portraits/troops/" + id, true);

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

        private static void Fit(Image image, Sprite art, Rect bounds, bool bottom = false, bool mirror = false)
        {
            image.sprite = art;
            image.enabled = art != null;
            if (art == null)
                return;
            float factor = Mathf.Min(bounds.width / art.rect.width, bounds.height / art.rect.height);
            float w = art.rect.width * factor, h = art.rect.height * factor;
            RectTransform rect = image.rectTransform;
            Position(rect, new Rect(bounds.x + (bounds.width - w) * .5f,
                bounds.y + (bounds.height - h) * (bottom ? 1f : .5f), w, h));

            // Mirror about the artwork center without moving its fitted rectangle.
            if (mirror)
            {
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(bounds.center.x, -bounds.y - (bounds.height - h) * (bottom ? 1f : .5f));
            }
            rect.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
        }

        private static bool SupportsText(Font candidate, string value)
        {
            if (candidate == null)
                return false;
            foreach (char ch in value)
                if (!char.IsWhiteSpace(ch) && !candidate.HasCharacter(ch))
                    return false;
            return true;
        }

        private Font FontForText(string value)
        {
            if (SupportsText(font, value))
                return font;
            if (!attemptedExtendedFont)
            {
                attemptedExtendedFont = true;
                extendedFont = Resources.Load<Font>("Lobby/Fonts/KingdomUiTCFull");
                // The shipped legacy subset intentionally omits many new glyphs.
                // An installed TC font is useful in the Editor/desktop; builds should
                // include the extended subset produced by Prepare-LobbyFont.py.
                if (extendedFont == null && Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    try
                    {
                        string[] available = Font.GetOSInstalledFontNames();
                        var candidates = new List<string>();
                        foreach (string family in new[] { "Microsoft JhengHei", "PingFang TC", "Noto Sans CJK TC", "Noto Sans TC", "Heiti TC", "Arial Unicode MS" })
                            if (Array.Exists(available, name => string.Equals(name, family, StringComparison.OrdinalIgnoreCase)))
                                candidates.Add(family);
                        if (candidates.Count > 0)
                        {
                            extendedFont = Font.CreateDynamicFontFromOSFont(candidates.ToArray(), 48);
                            ownsExtendedFont = extendedFont != null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Lobby font fallback unavailable: " + ex.Message);
                    }
                }
            }
            if (SupportsText(extendedFont, value))
                return extendedFont;
            if (!warnedMissingGlyphs)
            {
                warnedMissingGlyphs = true;
                Debug.LogWarning("Lobby text needs additional Traditional Chinese glyphs. Generate KingdomUiTCFull.otf with tools/Prepare-LobbyFont.py.");
            }
            return font;
        }

        private void SetLabelValue(Text label, string value)
        {
            label.font = FontForText(value);
            label.fontStyle = label.font == extendedFont && ownsExtendedFont ? FontStyle.Bold : FontStyle.Normal;
            label.text = value;
        }

        private Text Label(Transform parent, string id, string value, Rect rect, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = Box(parent, id, rect).gameObject.AddComponent<Text>();
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            SetLabelValue(label, value);
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
                ? new Rect(rect.width * .12f, rect.height * .38f, rect.width * .76f, rect.height * .36f)
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

        private Button IconButton(Transform parent, string name, string icon, Rect rect, Action click, Cue cue,
            float opticalOffsetX = 0f, bool mirror = false)
        {
            Button button = MakeButton(parent, name, "buttons/icon-button", rect, click, cue,
                out RectTransform art, out _);
            var bounds = new Rect(rect.width * (.23f + opticalOffsetX), rect.height * .22f,
                rect.width * .54f, rect.height * .54f);
            Image iconImage = Picture(art, "Icon", "icons/" + icon, bounds);
            iconImage.preserveAspect = true;
            if (mirror)
                Fit(iconImage, iconImage.sprite, bounds, mirror: true);
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
