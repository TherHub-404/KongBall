using Fusion;
using UnityEngine;

namespace KongBall
{
    // Networked ball — ONE SIMULATOR, MANY REQUESTERS.
    // A single peer (the master) owns and simulates the ball for the whole match; authority never
    // migrates on possession. That peer alone decides who has the ball, by proximity, and applies
    // every kick. No client ever writes ball state, which is what makes possession impossible to
    // desync. Non-authority peers keep the Rigidbody kinematic and follow via NetworkTransform,
    // and the carrier's client predicts only the MESH so dribbling still feels immediate.
    [RequireComponent(typeof(Rigidbody))]
    public class NetBall : NetworkBehaviour, IStateAuthorityChanged
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

        [Header("Visual prediction (carrier's client only)")]
        [Tooltip("Seconds to ease the mesh onto the predicted dribble position.")]
        public float predictBlendIn = 0.10f;
        [Tooltip("Seconds to ease the mesh back onto the networked position.")]
        public float predictBlendOut = 0.15f;
        [Tooltip("Hard cap on how far the mesh may sit from the real ball (metres).")]
        public float maxPredictOffset = 1.2f;

        // The pitch and the goals are NOT described here any more: they live in Arena, because the
        // wall the ball bounces off, the paint the player sees and the checks below all have to
        // agree. This file used to carry halfX/halfZ as dead fields while the real limit was a
        // hardcoded "|z| > 16" twenty lines down — so when the pitch grew, the ball started
        // teleporting to the centre while it was still in play.

        // WHICH OBJECT holds the ball, not which person. It used to be a PlayerRef, which quietly
        // meant "only something with a network connection can ever carry the ball" — so a bot, which
        // has no PlayerRef and sends no RPCs, could not have touched it.
        //
        // NetworkId rather than a raw int: it is the type Fusion documents as "the unique identifier
        // for a network entity", it carries its own serialisation, and it will not silently compare
        // equal to some other number. Invalid means free ball.
        [Networked] public NetworkId OwnerId { get; set; }
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

        // Presentation-only prediction state (never networked, never read by simulation).
        Transform _visual;
        // A generated model does not always have its pivot at the centre of the mesh: this ball's
        // origin sits on its underside. WireArtModels compensates with a local offset on the Visual,
        // and this code must preserve it — writing Visual.position directly would wipe it out and
        // draw the ball half a diameter above its own collider.
        Vector3 _visualBaseLocal;
        Vector3 _predictPos, _predictVel;
        float _predictWeight;
        int _seenSeq = -1;
        bool _localReleased;      // I kicked; stop gluing the mesh to me before the state confirms
        float _localReleasedAt;

        // The centring offset in world space. It rotates with the ball, exactly as the mesh's own
        // off-centre geometry does, so the two cancel and the ball spins in place.
        Vector3 CentringOffset => _visual != null ? transform.TransformVector(_visualBaseLocal) : Vector3.zero;

        // Where the ball LOOKS like it is on this client — its CENTRE, not the mesh pivot, so that
        // aiming and FX line up with the ball rather than with an arbitrary point on it.
        public Vector3 VisualPosition => _visual != null ? _visual.position - CentringOffset : transform.position;

        public override void Spawned()
        {
            // Two balls can briefly coexist: a client that becomes master before the room's existing
            // ball has replicated to it sees no ball and spawns one. Resolve it deterministically —
            // lowest NetworkId survives, so every client independently picks the same one — and let
            // whoever holds authority over the loser despawn it.
            if (Instance != null && Instance != this)
            {
                bool iAmOlder = Object.Id.Raw <= Instance.Object.Id.Raw;
                var loser = iAmOlder ? Instance : this;
                Instance = iAmOlder ? this : Instance;

                Debug.LogWarning("[Net] duplicate ball detected, dropping " + loser.Object.Id);
                if (loser.Object != null && loser.Object.HasStateAuthority)
                    Runner.Despawn(loser.Object);

                if (loser == this) return;   // this one is on its way out; don't initialise it
            }
            else Instance = this;

            _rb = GetComponent<Rigidbody>();
            _visual = transform.Find("Visual");
            if (_visual != null) _visualBaseLocal = _visual.localPosition;   // centring authored by WireArtModels
            _predictPos = transform.position;

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

        // Fired on every client when the ball's authority moves — in practice only on a master
        // migration, since nothing contends for it any more. Drop anything locally predicted so the
        // hand-over cannot leave a stale value behind, and re-seed the mesh where it currently is.
        public void StateAuthorityChanged()
        {
            if (!HasStateAuthority && Object != null) Object.ResetToLatestState();
            _dribbleVel = Vector3.zero;
            _predictVel = Vector3.zero;
            _predictPos = _visual != null ? _visual.position - CentringOffset : transform.position;
            _localReleased = false;
            if (_rb != null) SyncKinematic();
        }

        public override void FixedUpdateNetwork()
        {
            SyncKinematic();
            if (!HasStateAuthority || _rb == null) return;

            // Out-of-bounds safety net. The slack is deliberate: the wall already keeps the ball
            // in, so getting here means physics tunnelled through it. Resetting the instant the
            // ball touches the touchline would instead punish a legal ball resting against the wall.
            Vector3 bp = _rb.position;
            if (bp.y < -3f || !Arena.Contains(bp, 2f)) { ResetToCentre(); return; }

            // POSSESSION — decided here and nowhere else. Picking the ball up is a purely spatial
            // fact, so the authority (which already holds everyone's replicated positions) resolves
            // it directly: no request, no authority handoff, no window in which two peers disagree.
            UpdatePossession();

            if (OwnerId.IsValid)
            {
                var op = GetPlayer(OwnerId);
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
                if (Mathf.Abs(fp.z) < Arena.GoalHalfZ && fp.y < Arena.GoalHeight)
                {
                    if (fp.x > Arena.GoalLineX) { ScoreGoal(mc, 0); return; }   // Blue scores (+x)
                    if (fp.x < -Arena.GoalLineX) { ScoreGoal(mc, 1); return; }  // Red scores (-x)
                }
            }

            // Unity physics + the real walls handle roll / bounce / arc.
        }

        // --- Visual prediction -----------------------------------------------------------------
        // The networked root stays the single source of truth: collisions, possession and goals all
        // read it. Only the MESH lies, and only on the client currently carrying the ball, so that
        // a non-master carrier sees the ball glued to their feet instead of trailing by a round
        // trip. The lie is bounded by maxPredictOffset and unwound as soon as possession ends.
        //
        // Deliberately LateUpdate and not Render: the mesh is a child of the networked root, so we
        // must write it after NetworkTransform has finished placing that root for the frame. Nothing
        // here is read by the simulation, so running outside the Fusion callbacks is safe.
        void LateUpdate()
        {
            if (_visual == null || Runner == null) return;

            // Any confirmed possession change re-seeds the prediction from where the mesh actually
            // is, so gaining or losing the ball never snaps. Driven by the counter rather than by a
            // transition of Owner, which Fusion is allowed to collapse away.
            if (PossessionSeq != _seenSeq)
            {
                _seenSeq = PossessionSeq;
                _localReleased = false;
                _predictPos = _visual.position - CentringOffset;
                _predictVel = Vector3.zero;
            }

            // Safety valve: if a kick is never confirmed (dropped RPC), stop suppressing anyway.
            if (_localReleased && Time.time - _localReleasedAt > 1f) _localReleased = false;

            Vector3 truth = transform.position;
            bool predicting = !HasStateAuthority && !_localReleased
                              && OwnerId.IsValid && OwnerId == LocalPlayerId;

            if (predicting)
            {
                var me = GetPlayer(OwnerId);
                if (me != null)
                {
                    Vector3 anchor = me.transform.position + me.transform.forward * dribbleAhead;
                    anchor.y = Mathf.Max(radius, me.transform.position.y - 0.35f);
                    _predictPos = Vector3.SmoothDamp(_predictPos, anchor, ref _predictVel, dribbleSmoothTime);
                    // Never let the mesh drift further than this from the real ball.
                    _predictPos = truth + Vector3.ClampMagnitude(_predictPos - truth, maxPredictOffset);
                }
                else predicting = false;
            }

            float rate = Time.deltaTime / Mathf.Max(0.0001f, predicting ? predictBlendIn : predictBlendOut);
            _predictWeight = Mathf.MoveTowards(_predictWeight, predicting ? 1f : 0f, rate);

            Vector3 centre = _predictWeight > 0.0001f
                ? Vector3.Lerp(truth, _predictPos, _predictWeight)
                : truth;
            _visual.position = centre + CentringOffset;
        }

        // Called on the kicker's client the instant it sends RPC_Kick. Without this the mesh would
        // stay glued to the foot for a full round trip after the player has clearly shot.
        public void NotifyLocalKick()
        {
            _localReleased = true;
            _localReleasedAt = Time.time;
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
            if (OwnerId.IsValid)
            {
                var op = GetPlayer(OwnerId);
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
            bool free = !OwnerId.IsValid;
            float reach = free ? possessionRadius : possessionRadius * stealRadiusFactor;

            NetPlayer best = null;
            float bestD = float.MaxValue;
            // NetPlayer.Live, not Runner.ActivePlayers: the second only lists people with a
            // connection, so anything the master simulates on its own would never be a candidate.
            foreach (var np in NetPlayer.Live)
            {
                if (np == null || np.NetId == OwnerId) continue;
                if (np.IsStumbled || np.IsHeld) continue;
                float d = FlatDist(np.transform.position, _rb.position);
                if (d < reach && d < bestD) { bestD = d; best = np; }
            }
            if (best == null) return;

            if (!free)
            {
                var op = GetPlayer(OwnerId);
                // The carrier keeps it unless the challenger beats them by a real margin.
                if (op != null && bestD > FlatDist(op.transform.position, _rb.position) - stealMargin) return;
            }

            TakePossession(best);
        }

        void TakePossession(NetPlayer p)
        {
            OwnerId = p != null ? p.NetId : default;
            PossessionSeq++;
            _dribbleVel = Vector3.zero;
            // Protect the new carrier briefly so possession doesn't thrash between two close players
            // (the dribble-lead moves the ball ahead, separating them within this window).
            StealLock = TickTimer.CreateFromSeconds(Runner, stealLockDuration);
        }

        // --- Kick ------------------------------------------------------------------------------

        // Sent by the carrier's client. Only the authority actually shoots: the sender never writes
        // ball state. RpcInfo.Source is the real sender, so a client cannot kick on someone's behalf.
        //
        // The RPC only resolves WHO is asking and then hands over to Kick, which is also the entry
        // point for anything the authority simulates itself. A bot has no client and sends no RPC, so
        // without that split it would have had no way to shoot at all.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Kick(Vector3 dir, float power01, RpcInfo info = default)
        {
            if (Runner == null) return;
            if (!Runner.TryGetPlayerObject(info.Source, out var no) || no == null) return;
            Kick(no.GetComponent<NetPlayer>(), dir, power01);
        }

        // Authority side. Refuses anyone who is not currently carrying the ball, whoever asked.
        public void Kick(NetPlayer who, Vector3 dir, float power01)
        {
            if (!HasStateAuthority) return;
            if (who == null || !OwnerId.IsValid || OwnerId != who.NetId) return;   // not yours to kick

            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();

            OwnerId = default;
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
            if (!OwnerId.IsValid) return;
            OwnerId = default;
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
            if (OwnerId.IsValid) { OwnerId = default; PossessionSeq++; }
            _dribbleVel = Vector3.zero;
            StealLock = TickTimer.CreateFromSeconds(Runner, stealLockDuration);
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = new Vector3(0f, radius + 0.3f, 0f);
            transform.position = _rb.position;
        }

        // Runner.TryFindObject is Fusion's own lookup from a NetworkId, and it answers for anything
        // spawned — including something the master simulates with no player behind it, which
        // TryGetPlayerObject by definition cannot.
        NetPlayer GetPlayer(NetworkId id)
        {
            if (!id.IsValid || Runner == null) return null;
            if (!Runner.TryFindObject(id, out var no) || no == null) return null;
            return no.GetComponent<NetPlayer>();
        }

        // This peer's own player object, for the prediction check: "am I the one carrying it".
        NetworkId LocalPlayerId
        {
            get
            {
                if (Runner == null) return default;
                if (!Runner.TryGetPlayerObject(Runner.LocalPlayer, out var no) || no == null) return default;
                return no.Id;
            }
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
