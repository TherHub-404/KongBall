using UnityEngine;
using UnityEngine.EventSystems;

namespace KongBall
{
    // Full-screen (background) drag catcher for camera look/orbit. Sits behind the joystick
    // and action button, so those consume their own touches; dragging anywhere else rotates
    // the camera (yaw + pitch) via LocalInputSource.
    public class VirtualLookArea : MonoBehaviour, IDragHandler
    {
        public LocalInputSource target;

        public void OnDrag(PointerEventData e)
        {
            if (target != null) target.AddLookDelta(e.delta);
        }
    }
}
