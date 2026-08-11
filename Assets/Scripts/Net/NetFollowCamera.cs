using UnityEngine;

namespace CalcioStumble
{
    // Simple third-person camera for the network test: tracks the local player's POSITION
    // with a FIXED orientation. Rotation never changes, so camera-relative movement can't
    // create the spin feedback loop we hit earlier. Good framing, stable controls.
    public class NetFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 8f, -9f); // behind & above
        public float pitch = 40f;                          // fixed downward tilt
        public float followLerp = 10f;

        Quaternion _fixedRot;

        void OnEnable() { _fixedRot = Quaternion.Euler(pitch, 0f, 0f); transform.rotation = _fixedRot; }

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);
            transform.rotation = _fixedRot; // constant -> no spin
        }
    }
}
