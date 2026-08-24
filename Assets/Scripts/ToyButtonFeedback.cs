using UnityEngine;
using UnityEngine.EventSystems;

namespace KongBall
{
    // "Hover" on a touch-only game (AGENTS.md: iOS only, no mouse) means the moment a finger lands on
    // a button, not a lingering pointer — Unity's UI system still fires PointerEnter the instant a
    // touch overlaps the button's rect, so this reads correctly on a phone despite the name.
    //
    // Ui.ToyButton's visible layers (Depth/Outline/Surface) are children of an INVISIBLE hit target,
    // so Selectable's default ColorTint transition (which tints targetGraphic, here alpha-0) cannot
    // work — this scales the whole composite instead, which every child moves with for free.
    public class ToyButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                                      IPointerDownHandler, IPointerUpHandler
    {
        const float HoverScale = 1.05f;
        const float PressScale = 0.94f;
        const float Smooth = 22f;

        Vector3 _target = Vector3.one;
        bool _over;
        bool _pressed;

        public void OnPointerEnter(PointerEventData e) { _over = true; UiSfx.Hover(); Retarget(); }
        public void OnPointerExit(PointerEventData e) { _over = false; Retarget(); }
        public void OnPointerDown(PointerEventData e) { _pressed = true; Retarget(); }
        public void OnPointerUp(PointerEventData e) { _pressed = false; Retarget(); }

        void Retarget()
        {
            _target = Vector3.one * (_pressed ? PressScale : (_over ? HoverScale : 1f));
        }

        void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, _target, 1f - Mathf.Exp(-Smooth * Time.unscaledDeltaTime));
        }
    }
}
