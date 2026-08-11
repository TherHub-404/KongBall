using UnityEngine;
using UnityEngine.EventSystems;

namespace CalcioStumble
{
    // Lightweight on-screen joystick. Feeds a normalized [-1,1] vector straight into
    // LocalInputSource. No Input System virtual device involved.
    public class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform background; // area the finger drags within (usually this RectTransform)
        public RectTransform handle;     // visual knob that follows the finger
        public float range = 90f;        // pixels (in canvas space) for full deflection
        public LocalInputSource target;

        public void OnPointerDown(PointerEventData e) { OnDrag(e); }

        public void OnDrag(PointerEventData e)
        {
            if (background == null) return;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background, e.position, e.pressEventCamera, out local);

            Vector2 v = local / range;
            if (v.magnitude > 1f) v = v.normalized;

            if (handle != null) handle.anchoredPosition = v * range;
            if (target != null) target.SetTouchMove(v);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (target != null) target.SetTouchMove(Vector2.zero);
        }
    }
}
