using Fusion;
using UnityEngine;
using UnityEngine.UI;

namespace KongBall
{
    // Networked ball — ONE SIMULATOR, MANY REQUESTERS.
    // A single peer (the master) owns and simulates the ball for the whole match; authority never
    // migrates. No client ever writes ball state, which is what makes its behaviour impossible to
    // desync. Non-authority peers keep the Rigidbody kinematic and follow via NetworkTransform.
    //
    // THE BALL IS NEVER POSSESSED. There is no owner, no carry, no dribble. A player asks to HIT it
    // when it is close enough; the authority applies one impulse and the ball goes back to being a
    // fully independent physical object — CORE_GAMEPLAY_RESET, "the player never owns the ball, only
    // controls how they hit it." This replaces the earlier OwnerId/dribble/steal-radius possession
    // model wholesale, not alongside it: see the PR description for what depended on the old model
    // and what happened to each dependant.
    [RequireComponent(typeof(Rigidbody))]
    public class NetBall : NetworkBehaviour, IStateAuthorityChanged
    {
        [Header("Ball")]
        public float radius = 0.75f;

        [Header("Hit")]
        // Impulse hits set VELOCITY, not force, so a lighter ball flies further from the same push
        // unless the impulse comes down to match — impulse and mass were raised together here (mass
        // 0.35->0.45, this 5.8->7.5) to keep horizontal shot speed the same while liftRatio came DOWN
        // (0.32->0.22): the ball was launching too high off a hit. Not felt on a phone yet — tune
        // again once someone has.
        public float hitImpulse = 7.5f;
        public float liftRatio = 0.22f;
        public float spinRatio = 0.5f;
        [Tooltip("Authority-side validation range, on top of whatever range the asking NetPlayer " +
                 "already checked on its own client — generous, to tolerate the latency between the " +
                 "two, but still a real gate: no client writes ball state on its own say-so.")]
        public float hitValidationRange = 2.2f;

        [Header("Bump (passive body contact)")]
        [Tooltip("A CharacterController does not push a Rigidbody just by walking into it — Unity " +
                 "never applies that force for you, so without this the ball would sit there and let " +
                 "a player pass straight through it. Scales with the player's own horizontal speed, " +
                 "so a stationary bump does nothing and a sprint sends it rolling.")]
        public float bumpForceMultiplier = 2.5f;

        // The pitch and the goals are NOT described here any more: they live in Arena, because the
        // wall the ball bounces off, the paint the player sees and the checks below all have to
        // agree. This file used to carry halfX/halfZ as dead fields while the real limit was a
        // hardcoded "|z| > 16" twenty lines down — so when the pitch grew, the ball started
        // teleporting to the centre while it was still in play.

        // Bumps on every landed Hit — cosmetic only, drives a brief impact pop on the visual mesh so
        // a hit reads as a hit even before the impulse has visibly moved the ball. Presentation reacts
        // to the counter, never a value, for the same reason KickSeq does on NetPlayer.
        [Networked] public int HitSeq { get; set; }

        // Who last hit it, for whichever presentation wants to know who to credit a goal to (the
        // camera zoom, currently) — resolved fresh on every Hit, read by nobody inside this class.
        [Networked] public NetworkId LastHitterId { get; set; }

        // Single shared ball per session — resolved once instead of searched every frame.
        public static NetBall Instance { get; private set; }

        Rigidbody _rb;
        Collider _ballCol;
        int _seenHitSeq = -1;
        float _hitPulse;          // 0..1, decays after a hit — purely cosmetic scale pop
        Vector3 _visualBaseScale = Vector3.one;

        // A generated model does not always have its pivot at the centre of the mesh: this ball's
        // origin sits on its underside. WireArtModels compensates with a local offset on the Visual,
        // and this code must preserve it — writing Visual.position directly would wipe it out and
        // draw the ball half a diameter above its own collider. Recomputed every frame because the
        // offset rotates with the ball, exactly as the mesh's own off-centre geometry does, and the
        // ball spins.
        Transform _visual;
        Vector3 _visualBaseLocal;
        Vector3 CentringOffset => _visual != null ? transform.TransformVector(_visualBaseLocal) : Vector3.zero;

        // TEMPORARY debug scaffolding, same spirit as the "BOT n" nametag (Scripts/NameTag.cs,
        // Bots/AGENTS.md) — meant to come out again once the "two balls in allenamento" report is
        // actually pinned down. Every NetBall.Spawned() call, on every client, bumps this and repaints
        // a small on-screen counter, so a real device (no console attached) can show, by itself,
        // whether this is truly a second NetworkObject spawning (count reaches 2+) or something else
        // entirely (visual/rendering, count stays 1). To remove: this field, ShowSpawnDebug(), and its
        // two call sites below.
        static int _spawnCount;
        static Text _spawnDebugLabel;

        public override void Spawned()
        {
            _spawnCount++;
            ShowSpawnDebug();

            // Two balls can briefly coexist: a client that becomes master before the room's existing
            // ball has replicated to it sees no ball and spawns one. Resolve it deterministically —
            // lowest NetworkId survives, so every client independently picks the same one — and let
            // whoever holds authority over the loser despawn it.
            if (Instance != null && Instance != this)
            {
                bool iAmOlder = Object.Id.Raw <= Instance.Object.Id.Raw;
                var loser = iAmOlder ? Instance : this;
                Instance = iAmOlder ? this : Instance;

                Debug.LogWarning("[Net] duplicate ball detected (" + Object.Id + " vs " + Instance.Object.Id
                    + "), dropping " + loser.Object.Id + " — spawn #" + _spawnCount + " this session");
                if (loser.Object != null && loser.Object.HasStateAuthority)
                    Runner.Despawn(loser.Object);

                if (loser == this) return;   // this one is on its way out; don't initialise it
            }
            else Instance = this;

            _rb = GetComponent<Rigidbody>();
            _visual = transform.Find("Visual");
            if (_visual != null)
            {
                _visualBaseLocal = _visual.localPosition;   // centring authored by WireArtModels
                _visualBaseScale = _visual.localScale;
            }

            _ballCol = GetComponent<Collider>();
            if (_ballCol != null && _ballCol.sharedMaterial == null)
            {
                _ballCol.sharedMaterial = new PhysicsMaterial("BallPhys")
                {
                    // Was 0.65 with bounceCombine Maximum, which governs EVERY collision this ball
                    // has, not just a deliberate Hit — a wall (or anything with its own material) was
                    // always bounced at least this bouncy regardless of what it's made of. Felt on a
                    // phone as walls sending the ball flying. Not retuned beyond "clearly lower" yet.
                    bounciness = 0.4f,
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
            // Same temporary debug scaffolding as Spawned()'s counter: whether a duplicate actually
            // gets despawned (vs. just detected and left sitting there) is exactly the kind of thing
            // that's obvious from a log and invisible from reading the code that's supposed to do it.
            Debug.Log("[Net] ball despawned: " + Object.Id + (Instance == this ? " (was Instance)" : ""));
            if (Instance == this) Instance = null;
        }

        // Only the state authority runs the Rigidbody; others follow NetworkTransform.
        void SyncKinematic() { if (_rb.isKinematic == HasStateAuthority) _rb.isKinematic = !HasStateAuthority; }

        // Fired on every client when the ball's authority moves — in practice only on a master
        // migration, since nothing else contends for it.
        public void StateAuthorityChanged()
        {
            if (!HasStateAuthority && Object != null) Object.ResetToLatestState();
            if (_rb != null) SyncKinematic();
        }

        public override void FixedUpdateNetwork()
        {
            SyncKinematic();
            if (!HasStateAuthority || _rb == null) return;

            // Held perfectly still outside PLAYING. Without this, ResetToCentre placed it and then
            // let go: gravity and its own bounce physics were free to drift and
            // bounce it for the whole 2s GoalPause + 3s Countdown, so "the same starting point" was
            // actually "wherever it happened to settle that time" — a few centimetres of difference
            // every single kickoff. Zeroing velocity every tick holds it in place without going
            // kinematic, so it still falls the same short, consistent distance the instant PLAYING
            // begins rather than snapping.
            var mc = MatchController.Instance;
            if (mc == null || mc.CurPhase != MatchController.Phase.Playing)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return;
            }

            // Out-of-bounds safety net. The slack is deliberate: the wall already keeps the ball
            // in, so getting here means physics tunnelled through it. Resetting the instant the
            // ball touches the touchline would instead punish a legal ball resting against the wall.
            Vector3 bp = _rb.position;
            if (bp.y < -3f || !Arena.Contains(bp, 2f)) { ResetToCentre(); return; }

            // GOAL detection by coordinate (robust — the ball is trapped in the goal pocket, so no
            // physics-trigger tunnelling; the authority has the true position). mc.CanScore is
            // guaranteed true here — it means exactly "Playing", which the freeze check above already
            // confirmed — so there is nothing left to gate on.
            Vector3 fp = _rb.position;
            if (Mathf.Abs(fp.z) < Arena.GoalHalfZ && fp.y < Arena.GoalHeight)
            {
                if (fp.x > Arena.GoalLineX) { ScoreGoal(mc, 0); return; }   // Blue scores (+x)
                if (fp.x < -Arena.GoalLineX) { ScoreGoal(mc, 1); return; }  // Red scores (-x)
            }

            // Unity physics + the real walls handle roll / bounce / arc / rest.
        }

        // Recentres the visual mesh on the (possibly spinning) networked root every frame. Nothing
        // here is read by the simulation, so running outside the Fusion callbacks is safe; it must be
        // LateUpdate and not Render because the mesh is a child of the networked root and has to be
        // placed AFTER NetworkTransform finishes moving that root for the frame.
        void LateUpdate()
        {
            if (_visual == null) return;
            _visual.position = transform.position + CentringOffset;

            // Impact pop: purely cosmetic, every client, driven by the replicated counter rather than
            // by simulating the hit locally — it must read the same instant on every screen a hit is
            // visible on, not whenever that client happens to also be the one who threw the punch.
            if (HitSeq != _seenHitSeq) { _seenHitSeq = HitSeq; _hitPulse = 1f; }
            _hitPulse = Mathf.MoveTowards(_hitPulse, 0f, Time.deltaTime * 6f);
            _visual.localScale = _visualBaseScale * (1f + _hitPulse * 0.18f);
        }

        // Ball and MatchController are both spawned by — and simulated on — the master, so this is
        // normally a direct call. The RPC stays as the fallback for the brief window around a master
        // migration, when the two objects can momentarily sit on different peers.
        void ScoreGoal(MatchController mc, int team)
        {
            if (mc.Object != null && mc.Object.HasStateAuthority) mc.RegisterGoal(team, LastHitterId);
            else mc.RPC_Goal(team, LastHitterId);
            ResetToCentre();
        }

        // --- Hit -------------------------------------------------------------------------------
        // THE BALL IS NEVER POSSESSED: a hit is a single instantaneous impulse, not the release of a
        // carried object. Direction comes from the hitter's own facing/movement at the moment of
        // contact (NetPlayer decides that; this only applies it), and power is a fixed constant
        // rather than something aimed — see CORE_GAMEPLAY_RESET section 08: consistency and
        // readability first, tune the number later once someone has actually felt it.

        // Sent by whoever is trying to hit it. Only the authority actually applies the impulse: the
        // sender never writes ball state. RpcInfo.Source is the real sender, so a client cannot hit
        // on someone's behalf. A bot has no client and sends no RPC — it calls Hit directly, because
        // bots exist only on the master, which already IS the ball's authority.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Hit(Vector3 dir, float powerMultiplier, RpcInfo info = default)
        {
            if (Runner == null) return;
            if (!Runner.TryGetPlayerObject(info.Source, out var no) || no == null) return;
            Hit(no.GetComponent<NetPlayer>(), dir, powerMultiplier);
        }

        // Authority side. There is no ownership to check any more — instead the authority re-checks
        // distance itself with its own replicated positions, rather than trusting whatever the asking
        // client believed on its own (possibly stale, by network latency) view of the world.
        // powerMultiplier is 1 for a normal ACTION hit, higher for a spin attack that connects — one
        // impulse path for both, per CORE_GAMEPLAY_RESET section 12 ("do not create a second ball
        // physics system for the harder version").
        public void Hit(NetPlayer who, Vector3 dir, float powerMultiplier = 1f)
        {
            if (!HasStateAuthority || who == null) return;
            if (FlatDist(who.transform.position, _rb.position) > hitValidationRange) return;

            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();

            float impulse = hitImpulse * Mathf.Max(0.1f, powerMultiplier);
            bool aerial = _rb.position.y > radius + 0.6f;
            // Was 2.2x: combined with the old liftRatio that sent an aerial hit (e.g. the spin
            // attack, which almost always connects mid-air) far too high. 1.6x still gives an aerial
            // hit noticeably more air than a grounded one, just not an escape-the-pitch one.
            float lift = aerial ? liftRatio * 1.6f : liftRatio;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.AddForce(dir * impulse + Vector3.up * impulse * lift, ForceMode.Impulse);
            _rb.AddTorque(Vector3.Cross(Vector3.up, dir) * impulse * spinRatio, ForceMode.Impulse);

            LastHitterId = who.NetId;
            HitSeq++; // the cosmetic pop, on every client, at the same replicated instant
        }

        // --- Bump ------------------------------------------------------------------------------
        // A gentler, continuous cousin of Hit: NetPlayer calls this from OnControllerColliderHit
        // every tick its CharacterController is pushing into the ball, so walking through it moves
        // it instead of passing through — the deliberate ACTION hit stays the strong, precise one.

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Bump(Vector3 horizVel, float dt)
        {
            Bump(horizVel, dt);
        }

        // ForceMode.Force needs reapplying every PHYSICS fixed step to have its full effect, but this
        // is only ever called once per Fusion network tick (from OnControllerColliderHit, itself
        // fired once per FixedUpdateNetwork) — and Fusion's tick rate and Unity's own physics fixed
        // step are two different clocks that do not line up. Force was therefore being dropped
        // between mismatched steps, which is why the ball felt far heavier than bumpForceMultiplier
        // implied. VelocityChange scaled by this tick's own delta time sidesteps the mismatch
        // entirely: it is a direct, one-shot velocity nudge that does not need reapplying to land.
        public void Bump(Vector3 horizVel, float dt)
        {
            if (!HasStateAuthority || _rb == null) return;
            horizVel.y = 0f;
            if (horizVel.sqrMagnitude < 0.01f) return;
            _rb.AddForce(horizVel * bumpForceMultiplier * dt, ForceMode.VelocityChange);
        }

        public void KickoffReset()
        {
            if (!HasStateAuthority) return;
            ResetToCentre();
        }

        void ResetToCentre()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = new Vector3(0f, radius + 0.3f, 0f);
            transform.position = _rb.position;
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // See the field comments above — temporary, remove together with _spawnCount/_spawnDebugLabel
        // once the duplicate-ball report is pinned down. Top-right, but below where it would collide
        // with MatchMenu's practice-only RESET button (same corner, sizeDelta 104x60 at -26,-26).
        static void ShowSpawnDebug()
        {
            if (_spawnDebugLabel == null)
            {
                var go = new GameObject("BallSpawnDebug");
                UnityEngine.Object.DontDestroyOnLoad(go);
                Ui.NewOverlayCanvas(go, 4800);
                _spawnDebugLabel = Ui.NewText("Count", go.transform, 22);
                if (_spawnDebugLabel != null)
                {
                    _spawnDebugLabel.color = Color.yellow;
                    _spawnDebugLabel.alignment = TextAnchor.UpperRight;
                    var rt = _spawnDebugLabel.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.sizeDelta = new Vector2(520f, 44f);
                    rt.anchoredPosition = new Vector2(-26f, -96f);
                }
            }
            if (_spawnDebugLabel != null) _spawnDebugLabel.text = "BALL SPAWNS: " + _spawnCount;
        }
    }
}
