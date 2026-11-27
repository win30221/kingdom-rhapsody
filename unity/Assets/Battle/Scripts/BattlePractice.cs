using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomRhapsody
{
    public sealed class BattlePractice : MonoBehaviour
    {
        private const double StepSeconds = 1d / BattleMatch.TickRate;
        private const int SnapshotInterval = BattleMatch.TickRate / BattleMatch.SnapshotRate;

        private readonly BattleArt art = new BattleArt();
        private readonly BattlePresentation presentation = new BattlePresentation();
        private PanelSettings panel;
        private BattleView view;
        private BattleMatch match;
        private BattleLoadout loadout;
        private Action close;
        private double carry;
        private bool paused, closing;

        internal void Open(BattleLoadout selected, Font font, Action close, Action<string> playCue)
        {
            loadout = selected;
            this.close = close;
            panel = Instantiate(Resources.Load<PanelSettings>("Battle/BattlePanel"));
            panel.sortingOrder = 10;
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            view = new BattleView(document.rootVisualElement, font, art, Pause, Resume, Close, Restart, playCue);
            Restart();
        }

        private void Restart()
        {
            match = new BattleMatch(loadout, art.Timing);
            presentation.Reset(match, Time.unscaledTimeAsDouble);
            view.AttachMatch(match);
            carry = 0;
            paused = false;
        }

        private void Update()
        {
            if (match == null || closing)
                return;

            if (Input.GetKeyDown(KeyCode.Escape) && match.Winner == BattleMatch.Playing)
            {
                if (paused)
                    Resume();
                else if (!view.ClearAim())
                    Pause();
            }
            if (paused || match.Winner != BattleMatch.Playing)
                return;

            double now = Time.unscaledTimeAsDouble;
            carry += Math.Min(.25d, Time.unscaledDeltaTime);
            while (carry >= StepSeconds)
            {
                carry -= StepSeconds;
                match.Step();
                bool finished = match.Winner != BattleMatch.Playing;
                if (finished || match.Tick % SnapshotInterval == 0)
                    presentation.Publish(match, now - carry);
                if (finished)
                    break;
            }

            bool ended = match.Winner != BattleMatch.Playing;
            presentation.Advance(now, ended);
            view.Present(presentation);
            if (ended)
                view.ShowDialog(true);
        }

        private void Pause()
        {
            if (paused || match.Winner != BattleMatch.Playing)
                return;
            paused = true;
            view.ShowDialog(false);
        }

        private void Resume()
        {
            if (!paused)
                return;
            carry = 0;
            paused = false;
            presentation.Reset(match, Time.unscaledTimeAsDouble);
            view.HideDialog();
        }

        private void Close()
        {
            if (closing)
                return;
            closing = true;
            gameObject.SetActive(false);
            close();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            art.Clear();
            if (panel)
                Destroy(panel);
        }
    }
}
