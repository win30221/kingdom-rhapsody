using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomRhapsody
{
    public sealed class BattlePractice : MonoBehaviour
    {
        private const double StepSeconds = 1d / BattleMatch.TickRate;
        private const int SnapshotInterval = BattleMatch.TickRate / BattleMatch.SnapshotRate;
        private const string ServerPreference = "kingdom-rhapsody.battle.server";

        private readonly BattleArt art = new BattleArt();
        private readonly BattlePresentation presentation = new BattlePresentation();
        private readonly HashSet<string> soundFrame = new HashSet<string>();
        private PanelSettings panel;
        private UIDocument document;
        private BattleView view;
        private Label menuStatus;
        private Button onlineButton, practiceButton;
        private TextField address;
        private PracticeSession practice;
        private OnlineBattleSession online;
        private BattleSession session;
        private BattleLoadout loadout;
        private Font font;
        private Action close;
        private Action<string> playCue;
        private double carry, localClock;
        private int lastSound;
        private bool paused, closing, resultShown;
        private string endpoint;

        internal void Open(BattleLoadout selected, Font font, Action close, Action<string> playCue)
        {
            loadout = selected;
            this.font = font;
            this.close = close;
            this.playCue = playCue;
            panel = Instantiate(Resources.Load<PanelSettings>("Battle/BattlePanel"));
            panel.sortingOrder = 10;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            endpoint = PlayerPrefs.GetString(ServerPreference, DefaultEndpoint());
            ShowMenu();
        }

        private static string DefaultEndpoint()
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer &&
                Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri location) &&
                (location.Scheme == "http" || location.Scheme == "https"))
                return location.GetLeftPart(UriPartial.Authority);
            return "http://127.0.0.1:18120";
        }

        private void ShowMenu(string detail = "")
        {
            session = null;
            practice = null;
            online?.Dispose();
            online = null;
            view = null;
            resultShown = paused = false;
            var root = document.rootVisualElement;
            root.Clear();
            var screen = new VisualElement();
            screen.style.flexGrow = 1;
            screen.style.justifyContent = Justify.Center;
            screen.style.alignItems = Align.Center;
            screen.style.backgroundColor = (Color)new Color32(15, 26, 40, 255);
            if (font)
                screen.style.unityFontDefinition = FontDefinition.FromFont(font);
            root.Add(screen);

            var card = new VisualElement();
            card.style.width = Length.Percent(86);
            card.style.maxWidth = 390;
            card.style.paddingLeft = card.style.paddingRight = 22;
            card.style.paddingTop = card.style.paddingBottom = 22;
            card.style.backgroundColor = (Color)new Color32(31, 47, 64, 255);
            BattleView.Round(card, 12);
            screen.Add(card);

            var title = new Label("出戰");
            title.style.fontSize = 30;
            title.style.color = (Color)new Color32(255, 221, 158, 255);
            title.style.marginBottom = 16;
            card.Add(title);
            address = new TextField("伺服器") { value = endpoint, maxLength = 350 };
            address.style.marginBottom = 12;
            card.Add(address);
            onlineButton = AddButton(card, "雙人對戰", () => BeginOnline(address.value), true);
            practiceButton = AddButton(card, "本機對練", BeginPractice, false);
            AddButton(card, "回大廳", Close, false);
            menuStatus = new Label(detail);
            menuStatus.style.whiteSpace = WhiteSpace.Normal;
            menuStatus.style.color = (Color)new Color32(230, 226, 211, 255);
            menuStatus.style.marginTop = 10;
            card.Add(menuStatus);
            onlineButton.Focus();
        }

        private static Button AddButton(VisualElement panel, string text, Action press, bool primary)
        {
            var button = new Button(press) { text = text };
            button.style.height = 45;
            button.style.marginBottom = 8;
            button.style.fontSize = 18;
            button.style.backgroundColor = primary ? (Color)new Color32(233, 174, 97, 255) : (Color)new Color32(47, 66, 86, 255);
            button.style.color = primary ? (Color)new Color32(15, 26, 40, 255) : (Color)new Color32(255, 239, 203, 255);
            BattleView.Round(button, 7);
            panel.Add(button);
            return button;
        }

        private void BeginPractice()
        {
            playCue("confirm");
            BuildView(false);
            Restart();
        }

        private void BeginOnline(string server)
        {
            if (!OnlineBattleSession.TryEndpoint(server, out _))
            {
                menuStatus.text = "請輸入有效的伺服器網址";
                return;
            }
            endpoint = server.Trim();
            PlayerPrefs.SetString(ServerPreference, endpoint);
            PlayerPrefs.Save();
            online?.Dispose();
            var connection = new OnlineBattleSession();
            online = connection;
            menuStatus.text = "正在連線";
            onlineButton.SetEnabled(false);
            practiceButton.SetEnabled(false);
            address.SetEnabled(false);

            connection.Status += status =>
            {
                if (status == "waiting")
                    menuStatus.text = "正在尋找對手";
                else
                    ShowMenu(status switch
                    {
                        "version_mismatch" => "伺服器與遊戲版本不同",
                        "invalid_loadout" => "護衛或牌組無效",
                        "server_busy" => "伺服器忙碌，請稍後再試",
                        "match_unavailable" => "對戰暫時無法開啟",
                        _ => "連線已中斷，請重新配對"
                    });
            };
            connection.Snapshot += (frame, received, initial) =>
            {
                if (initial)
                {
                    BuildView(true);
                    session = connection;
                    presentation.Reset(frame, received);
                    view.AttachMatch(connection, presentation);
                }
                else
                    presentation.Publish(frame, received);

                if (connection.Winner != BattleMatch.Playing)
                    presentation.Finish(received);
            };
            connection.CommandResult += (slot, result) => view?.ReportResult(slot, result);
            try
            {
                connection.Connect(endpoint, loadout);
                playCue("confirm");
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is System.IO.IOException)
            {
                ShowMenu("無法連上對戰伺服器");
            }
        }

        private void BuildView(bool network)
        {
            var root = document.rootVisualElement;
            root.Clear();
            var battle = new VisualElement();
            root.Add(battle);
            view = new BattleView(battle, font, art, Pause, Resume, Close, Restart, playCue, network);
            lastSound = 0;
            resultShown = false;
        }

        private void Restart()
        {
            if (online != null)
            {
                ShowMenu();
                BeginOnline(endpoint);
                return;
            }
            var match = new BattleMatch(loadout, art.Timing);
            practice = new PracticeSession(match);
            session = practice;
            carry = localClock = 0;
            lastSound = 0;
            resultShown = paused = false;
            presentation.Reset(match, localClock);
            view.AttachMatch(practice, presentation);
        }

        private void Update()
        {
            if (closing || document == null)
                return;

            online?.Pump();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (view == null)
                {
                    Close();
                    return;
                }
                if (session.Winner == BattleMatch.Playing)
                {
                    if (view.DialogOpen)
                        Resume();
                    else if (!view.ClearAim())
                        Pause();
                }
            }

            if (view == null || resultShown)
                return;
            if (practice != null)
                UpdatePractice();
            else if (online != null)
                Render(OnlineBattleSession.Now);
        }

        private void UpdatePractice()
        {
            if (paused)
                return;
            BattleMatch match = practice.Match;
            double delta = Math.Min(.25d, Time.unscaledDeltaTime);
            localClock += delta;
            if (match.Winner == BattleMatch.Playing)
            {
                carry += delta;
                while (carry >= StepSeconds)
                {
                    carry -= StepSeconds;
                    match.Step();
                    bool finished = match.Winner != BattleMatch.Playing;
                    double observed = localClock - carry;
                    if (finished || match.PresentationChanged || match.Tick % SnapshotInterval == 0)
                        presentation.Publish(match, observed);
                    if (finished)
                    {
                        presentation.Finish(observed);
                        break;
                    }
                }
            }
            Render(localClock);
        }

        private void Render(double now)
        {
            presentation.Advance(now);
            view.Present(presentation);
            PlayBattleSounds();
            if (!presentation.EndAnimationComplete || session.Winner == BattleMatch.Playing)
                return;
            resultShown = true;
            playCue(session.Winner == BattleMatch.Draw ? "draw" : session.Winner == session.Seat ? "victory" : "defeat");
            view.ShowDialog(true);
        }

        private void PlayBattleSounds()
        {
            soundFrame.Clear();
            foreach (BattleEffect effect in session.Effects)
            {
                if (effect.Id <= lastSound || effect.Begin > presentation.RenderTick)
                    continue;
                lastSound = effect.Id;
                // A resumed browser may have received several seconds of old battle events.
                if (presentation.RenderTick - effect.Begin > 5)
                    continue;
                string cue = effect.Kind switch
                {
                    EffectKind.Projectile => "shoot",
                    EffectKind.Point => "hit",
                    EffectKind.Area or EffectKind.Rift or EffectKind.Hazard => "spell",
                    EffectKind.ShieldBreak => "shield-break",
                    EffectKind.ShieldRegrow or EffectKind.Heal or EffectKind.Rally => "support",
                    EffectKind.Death => "defeated",
                    _ => null
                };
                if (cue != null && soundFrame.Add(cue))
                    playCue(cue);
            }
        }

        private void Pause()
        {
            if (session.Winner != BattleMatch.Playing || view.DialogOpen)
                return;
            paused = practice != null;
            view.ShowDialog(false);
        }

        private void Resume()
        {
            paused = false;
            view.HideDialog();
        }

        private void Close()
        {
            if (closing)
                return;
            closing = true;
            online?.Dispose();
            gameObject.SetActive(false);
            close();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            online?.Dispose();
            art.Clear();
            if (panel)
                Destroy(panel);
        }
    }
}
