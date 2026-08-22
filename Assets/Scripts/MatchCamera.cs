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

        // The orbit arm is 10 m and the player can stand against a wall, so the camera's ideal
        // position lands up to 31,8 m out in x and 27,2 m out in z — well outside the arena, whose
        // interior is clear only to about 24 x 19 m below the roofline. Left alone the camera ends
        // up inside the stands, looking at the back of them.
        //
        // This was already happening before the arena became one model: the camera went through the
        // palms and the stand rows that used to be planted around the pitch. Solid geometry only
        // makes it impossible to miss.
        //
        // The arm is shortened instead of the position being clamped: pulling the camera in along
        // its own direction keeps the player where the drag put him on screen. Clamping the position
        // would slide him off centre, and the whole point of this rig is that the drag means one
        // thing and only one thing.
        [Header("Stay inside the arena")]
        public float insideHalfX = 23.8f;
        public float insideHalfZ = 18.8f;
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
            // Ultima difesa: se nemmeno il braccio minimo ci sta, la posizione viene tagliata. Questo
            // sposta il giocatore dal centro dello schermo, ed e' il motivo per cui viene per ultima e
            // non per prima — ma e' meglio di uno schermo pieno del dietro degli spalti. Serve solo
            // quando il giocatore e' appiccicato a un muro e la camera guarda verso l'esterno.
            camPos.x = Mathf.Clamp(camPos.x, -insideHalfX, insideHalfX);
            camPos.z = Mathf.Clamp(camPos.z, -insideHalfZ, insideHalfZ);
            if (instant)
                transform.position = camPos;
            else
                transform.position = Vector3.Lerp(transform.position, camPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            transform.rotation = rot;
        }

        // How much of the arm fits before the camera leaves the arena's clear interior.
        float ArmFraction(Vector3 focus, Vector3 arm)
        {
            float f = Mathf.Min(AxisFraction(focus.x, arm.x, insideHalfX),
                                AxisFraction(focus.z, arm.z, insideHalfZ));
            return Mathf.Clamp(f, minArm, 1f);
        }

        // Fraction of d that can be walked from p before |p + f*d| passes half. Returns 1 when the
        // arm points inward, or when it is short enough not to matter.
        static float AxisFraction(float p, float d, float half)
        {
            if (Mathf.Abs(d) < 1e-4f) return 1f;
            float wall = d > 0f ? half : -half;
            float f = (wall - p) / d;
            // f <= 0 means p has already passed that wall and the arm points further out: give up
            // the whole arm, ArmFraction's floor will keep the camera off the player.
            return f <= 0f ? 0f : Mathf.Min(1f, f);
        }
    }
}
