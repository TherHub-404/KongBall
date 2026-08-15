using UnityEngine;

namespace KongBall
{
    // Procedural "toy" animation for the (rig-less) monkey mesh. Presentation only:
    // it reads gameplay state and squashes/leans/bobs the VISUAL transform. Runs on every
    // client for every player (local + remote), driving off replicated position/state, so it
    // needs no extra networking. Attach to the "Visual" child; never touches the CharacterController.
    public class MonkeyAnimator : MonoBehaviour
    {
        [Header("Run")]
        public float runRefSpeed = 6f;     // speed that = full run
        public float bobHeight = 0.14f;    // hop height at full run
        public float bobRate = 13f;        // steps per second-ish
        public float leanMax = 16f;        // forward lean degrees at full run
        public float squashAmt = 0.10f;

        [Header("Air")]
        public float airThreshold = 1.3f;  // |vertical speed| to count as airborne
        public float stretchAmt = 0.16f;

        [Header("Kick / stumble")]
        public float kickTime = 0.28f;
        public float kickWhip = 40f;       // degrees
        public float tumbleRate = 520f;    // deg/sec while stumbled

        [Header("Smoothing")]
        public float smooth = 16f;

        NetPlayer _player;
        Transform _t;
        Vector3 _baseScale, _baseLocalPos;
        Vector3 _lastPos;
        float _bob, _tumble, _kickTimer;
        int _lastKickSeq;

        void Awake()
        {
            _t = transform;
            _baseScale = _t.localScale;
            _baseLocalPos = _t.localPosition;
            _player = GetComponentInParent<NetPlayer>();
            _lastPos = _t.position;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 pos = _player != null ? _player.transform.position : _t.position;
            Vector3 vel = (pos - _lastPos) / dt;
            _lastPos = pos;

            float hSpeed = new Vector2(vel.x, vel.z).magnitude;
            float vSpeed = vel.y;
            float run01 = Mathf.Clamp01(hSpeed / runRefSpeed);
            bool stumbled = _player != null && _player.IsStumbled;
            bool held = _player != null && _player.IsHeld;
            bool grabbing = _player != null && _player.IsGrabbing;
            bool airborne = Mathf.Abs(vSpeed) > airThreshold;

            if (_player != null && _player.KickSeq != _lastKickSeq) { _lastKickSeq = _player.KickSeq; _kickTimer = kickTime; }

            Vector3 targetScale = _baseScale;
            Vector3 targetPos = _baseLocalPos;
            Quaternion targetRot = Quaternion.identity;

            if (stumbled)
            {
                _tumble += tumbleRate * dt;
                targetRot = Quaternion.Euler(_tumble, 0f, 22f); // roll over
            }
            else if (held)
            {
                // Grabbed victim: squashed + struggling shake.
                _tumble = 0f;
                float shake = Mathf.Sin(Time.time * 30f) * 9f;
                targetRot = Quaternion.Euler(0f, 0f, shake);
                targetScale = new Vector3(_baseScale.x * 1.12f, _baseScale.y * 0.82f, _baseScale.z * 1.12f);
                targetPos = _baseLocalPos + new Vector3(0f, 0.05f, 0f);
            }
            else if (grabbing)
            {
                // Grabber: leaning forward, arms-out reaching, small effort bob.
                _tumble = 0f;
                float b = Mathf.Sin(Time.time * 12f) * 0.04f;
                targetRot = Quaternion.Euler(28f, 0f, 0f);
                targetPos = _baseLocalPos + new Vector3(0f, b, 0f);
            }
            else if (airborne)
            {
                float s = Mathf.Clamp(vSpeed * 0.03f, -stretchAmt, stretchAmt);
                targetScale = new Vector3(_baseScale.x * (1f - s * 0.6f), _baseScale.y * (1f + s), _baseScale.z * (1f - s * 0.6f));
            }
            else
            {
                _tumble = 0f;
                _bob += dt * bobRate * Mathf.Max(0.15f, run01);
                float hop = Mathf.Abs(Mathf.Sin(_bob)) * bobHeight * run01;
                targetPos = _baseLocalPos + new Vector3(0f, hop, 0f);
                float sq = Mathf.Sin(_bob * 2f) * squashAmt * run01;
                targetScale = new Vector3(_baseScale.x * (1f + sq * 0.5f), _baseScale.y * (1f - sq), _baseScale.z * (1f + sq * 0.5f));
                targetRot = Quaternion.Euler(run01 * leanMax, 0f, 0f); // lean into the run
            }

            // Kick whip (adds a quick forward snap over the lean)
            if (_kickTimer > 0f)
            {
                _kickTimer -= dt;
                float k = Mathf.Clamp01(_kickTimer / kickTime);
                float whip = Mathf.Sin((1f - k) * Mathf.PI); // 0 -> 1 -> 0
                targetRot = targetRot * Quaternion.Euler(whip * kickWhip, 0f, 0f);
            }

            float a = 1f - Mathf.Exp(-smooth * dt);
            _t.localScale = Vector3.Lerp(_t.localScale, targetScale, a);
            _t.localPosition = Vector3.Lerp(_t.localPosition, targetPos, a);
            _t.localRotation = stumbled ? targetRot : Quaternion.Slerp(_t.localRotation, targetRot, a);
        }
    }
}
