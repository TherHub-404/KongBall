using Fusion;
using UnityEngine;

namespace CalcioStumble
{
    // Networked ball — AUTHORITY FOLLOWS THE POSSESSOR (Shared Mode best practice).
    // Whoever holds the ball requests StateAuthority and simulates it LOCALLY (dribble/kick),
    // so every possessor gets the same instant feel as anyone else. Non-authority peers keep
    // the Rigidbody kinematic and follow it via NetworkTransform. Requires the NetworkObject's
    // "Allow State Authority Override" flag so possession can pass between players.
    [RequireComponent(typeof(Rigidbody))]
    public class NetBall : NetworkBehaviour
    {
        [Header("Ball")]
        public float radius = 0.5f;

        [Header("Dribble (tight lead)")]
        public float dribbleAhead = 1.3f;
        public float dribbleSmoothTime = 0.06f;

        [Header("Possession")]
        public float possessionRadius = 1.5f;
        public float loseDistance = 2.8f;

        [Header("Field / goals")]
        public float halfX = 20f;
        public float halfZ = 12f;
        public float goalHalfZ = 3.4f;   // goal mouth half-width (z)
        public float goalLineX = 21.5f;  // x the ball must cross to score
        public float goalHeight = 3.0f;  // max y that still counts

        [Networked] public PlayerRef Owner { get; set; }
        [Networked] TickTimer StealLock { get; set; }   // brief lock after a possession change

        Rigidbody _rb;
        Vector3 _dribbleVel;
        Collider _ballCol;

        public bool CanClaim => StealLock.ExpiredOrNotRunning(Runner);

        public override void Spawned()
        {
            _rb = GetComponent<Rigidbody>();

            _ballCol = GetComponent<Collider>();
            if (_ballCol != null && _ballCol.sharedMaterial == null)
            {
                _ballCol.sharedMaterial = new PhysicsMaterial("BallPhys")
                {
                    bounciness = 0.35f,
                    dynamicFriction = 0.4f,
                    staticFriction = 0.4f,
                    frictionCombine = PhysicsMaterialCombine.Average,
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                };
            }
            SyncKinematic();
        }

        // Only the state authority runs the Rigidbody; others follow NetworkTransform.
        void SyncKinematic() { if (_rb.isKinematic == HasStateAuthority) _rb.isKinematic = !HasStateAuthority; }

        public override void FixedUpdateNetwork()
        {
            SyncKinematic();
            if (!HasStateAuthority || _rb == null) return;

            // Out-of-bounds safety net.
            Vector3 bp = _rb.position;
            if (bp.y < -3f || Mathf.Abs(bp.x) > 27f || Mathf.Abs(bp.z) > 16f) { ResetToCentre(); return; }

            // RECONCILE (robustness): the StateAuthority is the ONLY client that may own the ball.
            // When authority just transferred to us, Owner can still hold the PREVIOUS authority's
            // ref (their write is stale — Fusion overwrites non-authority property writes). If we
            // let that stand, that player would "own" a ball only WE can simulate: they can't kick
            // (Kick needs HasStateAuthority) and we'd dribble the ball onto them -> ball glued &
            // uncontrollable for everyone until they wander off. Clearing it frees the ball so
            // whoever is actually in range re-claims cleanly this tick (see NetPlayer.HandleBall).
            if (Owner != PlayerRef.None && Owner != Runner.LocalPlayer)
            {
                Owner = PlayerRef.None;
                _dribbleVel = Vector3.zero;
            }

            if (Owner != PlayerRef.None)
            {
                var op = GetPlayer(Owner);
                bool lose = op == null || op.IsStumbled || op.IsHeld || FlatDist(op.transform.position, _rb.position) > loseDistance;
                if (!lose)
                {
                    // Tight local dribble (fluid — this client is the owner AND the authority).
                    Vector3 anchor = op.transform.position + op.transform.forward * dribbleAhead;
                    anchor.y = Mathf.Max(radius, op.transform.position.y - 0.35f);
                    Vector3 newPos = Vector3.SmoothDamp(_rb.position, anchor, ref _dribbleVel, dribbleSmoothTime);
                    _rb.MovePosition(newPos);
                    _rb.linearVelocity = _dribbleVel;
                    return;
                }
                FreeBall(op);
            }

            // Free ball: GOAL detection by coordinate (robust — the ball is trapped in the goal
            // pocket, so no physics-trigger tunnelling; the authority has the true position).
            var mc = MatchController.Instance;
            if (mc != null && mc.CanScore)
            {
                Vector3 fp = _rb.position;
                if (Mathf.Abs(fp.z) < goalHalfZ && fp.y < goalHeight)
                {
                    if (fp.x > goalLineX) { mc.RPC_Goal(0); ResetToCentre(); return; }   // Blue scores (+x)
                    if (fp.x < -goalLineX) { mc.RPC_Goal(1); ResetToCentre(); return; }  // Red scores (-x)
                }
            }

            // Unity physics + the real walls handle roll / bounce / arc.
        }

        // Called by a player's client once it has (or is taking) authority.
        public void SetOwner(PlayerRef p)
        {
            if (!HasStateAuthority) return;
            Owner = p;
            _dribbleVel = Vector3.zero;
            StealLock = TickTimer.CreateFromSeconds(Runner, 0.35f);
        }

        // Called by the owning player's client (owner == authority) to shoot. No RPC needed.
        public void Kick(Vector3 dir, float power01)
        {
            if (!HasStateAuthority) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();
            Owner = PlayerRef.None;
            float impulse = Mathf.Lerp(kickMin, kickMax, Mathf.Clamp01(power01));
            bool aerial = _rb.position.y > radius + 0.6f;
            float lift = aerial ? liftRatio * 2.2f : liftRatio;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.AddForce(dir * impulse + Vector3.up * impulse * lift, ForceMode.Impulse);
            _rb.AddTorque(Vector3.Cross(Vector3.up, dir) * impulse * spinRatio, ForceMode.Impulse);
            StealLock = TickTimer.CreateFromSeconds(Runner, 0.5f);  // don't let the kicker instantly re-own
        }

        [Header("Kick (impulse)")]
        public float kickMin = 6f;
        public float kickMax = 15f;
        public float liftRatio = 0.32f;
        public float spinRatio = 0.5f;

        void FreeBall(NetPlayer from)
        {
            Owner = PlayerRef.None;
            _dribbleVel = Vector3.zero;
            Vector3 away = from != null ? (_rb.position - from.transform.position) : Vector3.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.04f) away = from != null ? -from.transform.forward : Vector3.forward;
            _rb.linearVelocity = away.normalized * 5f + Vector3.up * 1.5f;
            StealLock = TickTimer.CreateFromSeconds(Runner, 0.4f);
        }

        public void KickoffReset()
        {
            if (!HasStateAuthority) return;
            ResetToCentre();
        }

        void ResetToCentre()
        {
            Owner = PlayerRef.None;
            _dribbleVel = Vector3.zero;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = new Vector3(0f, radius + 0.3f, 0f);
            transform.position = _rb.position;
        }

        Transform GetPlayerTransform(PlayerRef p) { var pl = GetPlayer(p); return pl != null ? pl.transform : null; }

        NetPlayer GetPlayer(PlayerRef p)
        {
            var players = UnityEngine.Object.FindObjectsByType<NetPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var pl in players)
                if (pl.Object.InputAuthority == p) return pl;
            return null;
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
