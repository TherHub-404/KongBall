using UnityEngine;

namespace KongBall
{
    // Fallback third-person camera (used when the scene has no MatchCamera): tracks the local
    // player POSITION with a FIXED orientation. Rotation never changes, so camera-relative
    // movement cannot create a spin feedback loop. Good framing, stable controls.
    public class NetFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 8f, -9f); // behind & above
        // NOTE: this rig has the same problem MatchCamera solves — 9 m behind a player standing on
        // the far touchline puts the camera inside the arena's stands. It is left alone because the
        // scene carries a MatchCamera and this code never runs there; if it ever becomes the real
        // camera, bring over MatchCamera's arm shortening first.
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
