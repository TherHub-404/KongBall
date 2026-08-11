using UnityEngine;
using UnityEngine.EventSystems;

namespace CalcioStumble
{
    // On-screen action button. Reports HELD state (tap/hold) AND a drag delta from the press
    // origin (used to aim the kick when the player owns the ball).
    public class VirtualActionButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public LocalInputSource target;
        Vector2 _startPos;

        public void OnPointerDown(PointerEventData e)
        {
            _startPos = e.position;
            if (target != null) { target.SetTouchActionHeld(true); target.SetAimDelta(Vector2.zero); }
        }

        public void OnDrag(PointerEventData e)
        {
            if (target != null) target.SetAimDelta(e.position - _startPos);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (target != null) { target.SetTouchActionHeld(false); target.SetAimDelta(Vector2.zero); }
        }
    }
}
