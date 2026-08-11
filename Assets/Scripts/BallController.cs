using UnityEngine;

namespace CalcioStumble
{
    // Soft, physics-based dribbling (no kinematic magnet). While owned, the ball is steered
    // toward a point ahead of the owner IN THE DIRECTION THE OWNER IS MOVING, easing there
    // fluidly (velocity is blended, not snapped). It rolls, collides, and can be stolen by
    // contact (a closer opponent takes it) or by push/restrain.
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        public Rigidbody Rb { get; private set; }
        public PlayerController Owner { get; private set; }

        [Header("Dribble feel")]
        [Tooltip("How far ahead (facing direction) the ball is carried.")]
        public float dribbleAhead = 1.3f;
        [Tooltip("Trailing smoothness (SmoothDamp time). Lower = tighter/snappier, higher = looser.")]
        public float dribbleSmoothTime = 0.09f;
        [Tooltip("Proportional pull toward the carry point.")]
        public float catchUpGain = 8f;
        [Tooltip("Max planar speed while dribbled (keep >= player speed).")]
        public float maxDribbleSpeed = 10f;
        [Tooltip("How fast the ball's velocity eases toward the target velocity (fluidity).")]
        public float velocityEase = 12f;
        [Tooltip("Surface gap beyond which the ball is knocked loose.")]
        public float loseDistance = 2.5f;

        [Header("Steal")]
        [Tooltip("A challenger must be this much closer (surface gap) than the owner to steal by contact.")]
        public float stealHysteresis = 0.05f;

        [Header("Safety")]
        [Tooltip("Hard cap on ball speed (anti-tunnelling / readability).")]
        public float maxBallSpeed = 24f;

        [Header("Possession timing")]
        public float kickNoPossessTime = 0.4f;
        public float tackleNoPossessTime = 0.03f;

        const float PLAYER_RADIUS = 0.5f;

        float _noPossessTimer;
        float _radius = 0.55f;
        Vector3 _startPos;
        Vector3 _dribbleVel;   // SmoothDamp velocity for dribbling

        public bool IsFree => Owner == null;
        public float Radius => _radius;

        void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            _startPos = transform.position;
            _radius = transform.localScale.x * 0.5f;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        public void SetStartPosition(Vector3 p) { _startPos = p; }

        public void ResetBall() { ResetBallTo(_startPos); }

        public void ResetBallTo(Vector3 pos)
        {
            _startPos = pos;
            Owner = null;
            _noPossessTimer = 0f;
            if (Rb.isKinematic) Rb.isKinematic = false;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            // rb.position too: transform.position alone is reverted by interpolation.
            Rb.position = pos;
            transform.position = pos;
        }

        void SetOwner(PlayerController p) { Owner = p; _dribbleVel = Vector3.zero; }

        public void Release(Vector3 impulse)   // kick
        {
            Owner = null;
            _noPossessTimer = kickNoPossessTime;
            Rb.AddForce(impulse, ForceMode.Impulse);
        }

        public void LoseToTackle()             // stolen by push/restrain
        {
            if (Owner == null) return;
            Owner = null;
            _noPossessTimer = tackleNoPossessTime;
        }

        bool MatchPlaying =>
            GameManager.Instance == null || GameManager.Instance.State == MatchState.Playing;

        float GapTo(PlayerController p)
        {
            return Vector3.Distance(transform.position, p.transform.position) - PLAYER_RADIUS - _radius;
        }

        PlayerController NearestEligible(out float bestGap)
        {
            bestGap = Mathf.Infinity;
            PlayerController best = null;
            var players = GameManager.Instance.players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.State != PlayerState.Normal) continue;
                float g = GapTo(p);
                if (g < bestGap) { bestGap = g; best = p; }
            }
            return best;
        }

        void FixedUpdate()
        {
            if (_noPossessTimer > 0f) _noPossessTimer -= Time.fixedDeltaTime;

            // Safety cap: never let the ball exceed maxBallSpeed (planar), even after a kick.
            Vector3 v = Rb.linearVelocity;
            Vector3 planarV = new Vector3(v.x, 0f, v.z);
            if (planarV.magnitude > maxBallSpeed)
            {
                planarV = planarV.normalized * maxBallSpeed;
                Rb.linearVelocity = new Vector3(planarV.x, v.y, planarV.z);
            }

            if (!MatchPlaying || GameManager.Instance == null) return;

            if (Owner != null)
            {
                if (Owner.State != PlayerState.Normal) { LoseToTackle(); return; }

                // Steal-by-contact: a closer eligible player takes possession.
                var near = NearestEligible(out float nearGap);
                if (near != null && near != Owner
                    && nearGap <= near.possessionRadius
                    && nearGap < GapTo(Owner) - stealHysteresis)
                    SetOwner(near);

                if (GapTo(Owner) > loseDistance) { LoseToTackle(); return; }

                // Dribble: smoothly trail the ball to a point IN FRONT of the owner's facing.
                // Position-based SmoothDamp (not velocity steering) so it can't orbit or end up
                // behind the player, yet still trails with a soft lag instead of a rigid snap.
                Vector3 fwd = Owner.transform.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-4f) fwd.Normalize();
                Vector3 target = Owner.transform.position + fwd * dribbleAhead;
                target.y = _radius;
                Vector3 newPos = Vector3.SmoothDamp(transform.position, target, ref _dribbleVel,
                    dribbleSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
                Rb.MovePosition(newPos);
                return;
            }

            // Free: capture by proximity.
            if (_noPossessTimer > 0f) return;
            var best = NearestEligible(out float gap);
            if (best != null && gap <= best.possessionRadius) SetOwner(best);
        }
    }
}
