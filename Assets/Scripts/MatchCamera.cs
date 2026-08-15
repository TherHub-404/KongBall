using UnityEngine;

namespace KongBall
{
    // Orbit camera: follows the player's POSITION but its yaw/pitch are controlled by the
    // player's screen drag (VirtualLookArea) — NOT auto-slaved to the player's facing.
    // Decoupling the camera yaw from facing is what removes the movement "spin" loop.
    public class MatchCamera : MonoBehaviour
    {
        [Header("Orbit rig")]
        public float backDistance = 10f;
        public float lookHeight = 1.4f;
        public float followLerp = 12f;
        [Tooltip("How smoothly the orbit angle eases toward the dragged target (higher = snappier).")]
        public float rotationLerp = 10f;

        [Header("Look (drag) sensitivity")]
        public float yawSensitivity = 0.22f;   // deg per screen pixel
        public float pitchSensitivity = 0.16f;
        public float pitchMin = 12f;
        public float pitchMax = 72f;
        public float pitchDefault = 34f;

        Transform _target;
        float _yaw, _pitch;          // target orbit angles (driven by drag)
        float _curYaw, _curPitch;    // smoothed angles actually applied
        LocalInputSource _input;     // the single local input source in the scene

        public void SetTarget(Transform player, Vector3 attackDir)
        {
            _target = player;
            attackDir.y = 0f;
            if (attackDir.sqrMagnitude < 1e-4f) attackDir = Vector3.right;
            _yaw = _curYaw = Mathf.Atan2(attackDir.x, attackDir.z) * Mathf.Rad2Deg; // toward enemy goal
            _pitch = _curPitch = pitchDefault;
            Apply(true);
        }

        void LateUpdate()
        {
            if (_target == null) return;

            if (_input == null) _input = UnityEngine.Object.FindAnyObjectByType<LocalInputSource>();
            if (_input != null)
            {
                Vector2 look = _input.ConsumeLookDelta();
                _yaw += look.x * yawSensitivity;
                _pitch = Mathf.Clamp(_pitch - look.y * pitchSensitivity, pitchMin, pitchMax);
            }
            Apply(false);
        }

        void Apply(bool instant)
        {
            if (instant) { _curYaw = _yaw; _curPitch = _pitch; }
            else
            {
                float k = 1f - Mathf.Exp(-rotationLerp * Time.deltaTime);
                _curYaw = Mathf.LerpAngle(_curYaw, _yaw, k);
                _curPitch = Mathf.Lerp(_curPitch, _pitch, k);
            }

            Quaternion rot = Quaternion.Euler(_curPitch, _curYaw, 0f);
            Vector3 focus = _target.position + Vector3.up * lookHeight;
            Vector3 camPos = focus - (rot * Vector3.forward) * backDistance;
            if (instant)
                transform.position = camPos;
            else
                transform.position = Vector3.Lerp(transform.position, camPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            transform.rotation = rot;
        }
    }
}
