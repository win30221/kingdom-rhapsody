using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KingdomRhapsody
{
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    public sealed class LobbyPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private const float PressScale = 0.94f;
        private RectTransform visual;
        private Button button;
        private Vector3 rest;
        private int? activePointer;
        private float pulse;

        internal void Bind(RectTransform target)
        {
            visual = target;
            rest = target.localScale;
            button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || activePointer.HasValue || !button.IsInteractable())
                return;
            activePointer = e.pointerId;
            visual.localScale = rest * PressScale;
        }

        public void OnPointerUp(PointerEventData e) => Release(e);
        public void OnPointerExit(PointerEventData e) => Release(e);
        internal void Pulse() => pulse = 0.12f;

        private void Release(PointerEventData e)
        {
            if (e.pointerId == activePointer)
                activePointer = null;
        }

        private void Update()
        {
            if (visual == null)
                return;
            if (!button.IsInteractable())
            {
                OnDisable();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            pulse = Mathf.Max(0f, pulse - dt);
            float size = activePointer.HasValue ? PressScale : 1f + 0.035f * pulse / 0.12f;
            visual.localScale = Vector3.Lerp(visual.localScale, rest * size, 1f - Mathf.Exp(-26f * dt));
        }

        private void OnDisable()
        {
            pulse = 0f;
            activePointer = null;
            if (visual != null)
                visual.localScale = rest;
        }
    }
}
