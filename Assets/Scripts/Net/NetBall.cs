using Fusion;
using UnityEngine;

namespace KongBall
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
        [Tooltip("Reach multiplier when taking the ball off another player (hysteresis).")]
        public float stealRadiusFactor = 0.7f;
        [Tooltip("How much closer than the carrier a challenger must be to steal (metres).")]
        public float stealMargin = 0.35f;
        [Tooltip("Possession is locked for this long after any change (anti-thrash).")]
        public float stealLockDuration = 0.5f;

        [Header("Field / goals")]
        public float halfX = 20f;
        public float halfZ = 12f;
        public float goalHalfZ = 3.4f;   // goal mouth half-width (z)
        public float goalLineX = 21.5f;  // x the ball must cross to score
        public float goalHeight = 3.0f;  // max y that still counts

        [Networked] public PlayerRef Owner { get; set; }
        // Bumped on EVERY possession change. Fusion's eventual consistency can collapse a value
        // that flips back and forth (A -> None -> A) into no change at all, so presentation reacts
        // to this counter, never to a transition of Owner itself.
        [Networked] public int PossessionSeq { get; set; }
        [Networked] TickTimer StealLock { get; set; }   // brief lock after a possession change

        // Single shared ball per session — resolved once instead of searched every frame.
        public static NetBall Instance { get; private set; }

        Rigidbody _rb;
        Vector3 _dribbleVel;
        Collider _ballCol;

        public override void Spawned()
        {
            Instance = this;
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

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
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

            // POSSESSION — decided here and nowhere else. Picking the ball up is a purely spatial
            // fact, so the authority (which already holds everyone's replicated positions) resolves
            // it directly: no request, no authority handoff, no window in which two peers disagree.
            UpdatePossession();

            if (Owner != PlayerRef.None)
            {
                var op = GetPlayer(Owner);
                if (op != null)
                {
                    // Tight dribble toward a point led out in front of the carrier.
                    Vector3 anchor = op.transform.position + op.transform.forward * dribbleAhead;
                    anchor.y = Mathf.Max(radius, op.transform.position.y - 0.35f);
                    Vector3 newPos = Vector3.SmoothDamp(_rb.position, anchor, ref _dribbleVel, dribbleSmoothTime);
                    _rb.MovePosition(newPos);
                    _rb.linearVelocity = _dribbleVel;
                    return;
                }
            }

            // Free ball: GOAL detection by coordinate (robust — the ball is trapped in the goal
            // pocket, so no physics-trigger tunnelling; the authority has the true position).
            var mc = MatchController.Instance;
            if (mc != null && mc.CanScore)
            {
                Vector3 fp = _rb.position;
                if (Mathf.Abs(fp.z) < goalHalfZ && fp.y < goalHeight)
                {
                    if (fp.x > goalLineX) { ScoreGoal(mc, 0); return; }   // Blue scores (+x)
                    if (fp.x < -goalLineX) { ScoreGoal(mc, 1); return; }  // Red scores (-x)
                }
            }

            // Unity physics + the real walls handle roll / bounce / arc.
        }

        // Ball and MatchController are both spawned by — and simulated on — the master, so this is
        // normally a direct call. The RPC stays as the fallback for the brief window around a master
        // migration, when the two objects can momentarily sit on different peers.
        void ScoreGoal(MatchController mc, int team)
        {
            if (mc.Object != null && mc.Object.HasStateAuthority) mc.RegisterGoal(team);
            else mc.RPC_Goal(team);
            ResetToCentre();
        }

        // --- Possession, authority side only ---------------------------------------------------

        void UpdatePossession()
        {
            // Does the current carrier still hold it?
            if (Owner != PlayerRef.None)
            {
                var op = GetPlayer(Owner);
                if (op == null || op.IsStumbled || op.IsHeld
                    || FlatDist(op.transform.position, _rb.position) > loseDistance)
                {
                    FreeBall(op);
                }
            }

            if (!StealLock.ExpiredOrNotRunning(Runner)) return;

            // Nobody picks the ball up outside live play, so a player standing on the centre spot
            // can't walk into possession during the countdown or a goal pause.
            var mc = MatchController.Instance;
            if (mc != null && mc.CurPhase != MatchController.Phase.Playing) return;

            // A free ball is taken at full radius. Taking it OFF someone needs the challenger to be
            // clearly closer (hysteresis), so two players jostling can't trade it every tick.
            bool free = Owner == PlayerRef.None;
            float reach = free ? possessionRadius : possessionRadius * stealRadiusFactor;

            NetPlayer best = null;
            float bestD = float.MaxValue;
            foreach (var p in Runner.ActivePlayers)
            {
                if (p == Owner) continue;
                var np = GetPlayer(p);
                if (np == null || np.IsStumbled || np.IsHeld) continue;
                float d = FlatDist(np.transform.position, _rb.position);
                if (d < reach && d < bestD) { bestD = d; best = np; }
            }
            if (best == null) return;

            if (!free)
            {
                var op = GetPlayer(Owner);
                // The carrier keeps it unless the challenger beats them by a real margin.
                if (op != null && bestD > FlatDist(op.transform.position, _rb.position) - stealMargin) return;
            }

            TakePossession(best.Object.StateAuthority);
        }

        void TakePossession(PlayerRef p)
        {
            Owner = p;
            PossessionSeq++;
            _dribbleVel = Vector3.zero;
            // Protect the new carrier briefly so possession doesn't thrash between two close players
            // (the dribble-lead moves the ball ahead, separating them within this window).
            StealLock = TickTimer.CreateFromSeconds(Runner, stealLockDuration);
        }

        // --- Kick ------------------------------------------------------------------------------

        // Sent by the carrier's client. Only the authority actually shoots: the sender never writes
        // ball state. RpcInfo.Source is the real sender, so a client cannot kick on someone's behalf.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Kick(Vector3 dir, float power01, RpcInfo info = default)
        {
            if (Owner == PlayerRef.None || Owner != info.Source) return;  // not yours to kick
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();

            Owner = PlayerRef.None;
            PossessionSeq++;
            _dribbleVel = Vector3.zero;

            float impulse = Mathf.Lerp(kickMin, kickMax, Mathf.Clamp01(power01));
            bool aerial = _rb.position.y > radius + 0.6f;
            float lift = aerial ? liftRatio * 2.2f : liftRatio;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.AddForce(dir * impulse + Vector3.up * impulse * lift, ForceMode.Impulse);
            _rb.AddTorque(Vector3.Cross(Vector3.up, dir) * impulse * spinRatio, ForceMode.Impulse);
            StealLock = TickTimer.CreateFromSeconds(Runner, stealLockDuration);  // no instant re-take
        }

        [Header("Kick (impulse)")]
        public float kickMin = 6f;
        public float kickMax = 15f;
        public float liftRatio = 0.32f;
        public float spinRatio = 0.5f;

        void FreeBall(NetPlayer from)
        {
            if (Owner == PlayerRef.None) return;
            Owner = PlayerRef.None;
            PossessionSeq++;
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
            if (Owner != PlayerRef.None) { Owner = PlayerRef.None; PossessionSeq++; }
            _dribbleVel = Vector3.zero;
            StealLock = TickTimer.CreateFromSeconds(Runner, stealLockDuration);
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = new Vector3(0f, radius + 0.3f, 0f);
            transform.position = _rb.position;
        }

        // Every client registers its own player object via Runner.SetPlayerObject at spawn
        // (NetLauncher) and that association replicates, so any peer can resolve any player
        // directly — no scene-wide search inside the simulation loop.
        NetPlayer GetPlayer(PlayerRef p)
        {
            if (p == PlayerRef.None || Runner == null) return null;
            if (!Runner.TryGetPlayerObject(p, out var no) || no == null) return null;
            return no.GetComponent<NetPlayer>();
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
