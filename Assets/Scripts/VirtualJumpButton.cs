using UnityEngine;
using UnityEngine.EventSystems;

namespace KongBall
{
    // On-screen JUMP button: queues a single jump on press (edge-triggered).
    public class VirtualJumpButton : MonoBehaviour, IPointerDownHandler
    {
        public LocalInputSource target;

        public void OnPointerDown(PointerEventData e)
        {
            if (target != null) target.QueueJump();
        }
    }
}
