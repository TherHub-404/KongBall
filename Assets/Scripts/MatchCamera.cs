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

        // The orbit arm is 10 m and the player can stand against the wall, so the camera's ideal
        // position lands well outside the pitch — and outside the pitch is the stands. Left alone
        // the camera ends up inside them, looking at their backs.
        //
        // This was already happening before the arena became one model: the camera went through the
        // palms and the stand rows that used to be planted around the pitch. Solid geometry only
        // makes it impossible to miss.
        //
        // The arm is shortened rather than the position clamped: pulling the camera in along its own
        // direction keeps the player where the drag put him on screen. Clamping the position would
        // slide him off centre, and the whole point of this rig is that the drag means one thing.
        //
        // The limit is Arena's own shape, not a pair of numbers copied from it — the last time the
        // pitch changed, every place holding its own copy of the bounds became wrong at once.
        [Tooltip("Never shorten the arm below this fraction, or the camera ends up inside the player.")]
        public float minArm = 0.3f;

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
            Vector3 arm = -(rot * Vector3.forward) * backDistance;
            Vector3 camPos = focus + arm * ArmFraction(focus, arm);
            // The arm has a floor, so with the player pressed against the wall and the camera
            // dragged straight outward the shortened arm can still poke through. Then, and only
            // then, the position is pushed back in — which slides the player off centre, which is
            // why it is the last resort and not the first.
            camPos = Arena.PushInside(camPos);
            if (instant)
                transform.position = camPos;
            else
                transform.position = Vector3.Lerp(transform.position, camPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            transform.rotation = rot;
        }

        // How much of the arm fits before the camera leaves the pitch. Bisection rather than an
        // analytic solve: the boundary has rounded corners, and twelve halvings land within a
        // centimetre of it for a 10 m arm — far below anything the eye can see in a camera move.
        float ArmFraction(Vector3 focus, Vector3 arm)
        {
            if (Arena.Distance(focus.x + arm.x, focus.z + arm.z) < 0f) return 1f;   // the usual case
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 12; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (Arena.Distance(focus.x + arm.x * m, focus.z + arm.z * m) < 0f) lo = m;
                else hi = m;
            }
            return Mathf.Clamp(lo, minArm, 1f);
        }
    }
}
