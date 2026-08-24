using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace KongBall
{
    // Networked player. In Shared Mode the client with StateAuthority simulates
    // its own movement; NetworkTransform replicates it. Team is a [Networked] value set by the
    // owner at spawn, so every client colours the capsule by team consistently.
    public class NetPlayer : NetworkBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 7f;
        public float acceleration = 55f;
        public float deceleration = 65f;
        public float turnSpeed = 720f;
        public float airControl = 0.55f;
        [Tooltip("How long a held direction takes to reach full acceleration. Phone-test feedback: " +
                 "the old flat-rate ramp read as linear/instant, not a build-up to a standard speed.")]
        public float rampUpTime = 0.35f;
        [Tooltip("Acceleration multiplier at the very start of a held direction (t=0), before " +
                 "rampUpTime's smoothstep brings it up to 1. Not 0 — a dead first tick reads as lag.")]
        public float rampStartMul = 0.3f;

        [Header("Jump / gravity")]
        public float jumpVelocity = 8.5f;
        public float gravity = -25f;
        public float fallMultiplier = 1.7f;
        public float coyoteTime = 0.12f;
        public float jumpBufferTime = 0.12f;
        [Tooltip("Phone-test feedback: jump was spammable. Each ground jump before jumpFatigueRecover " +
                 "has passed since the last one adds a fatigue stack, down to jumpFatigueMinMul at " +
                 "jumpFatigueMaxStacks; resting that long resets to full power.")]
        public int jumpFatigueMaxStacks = 3;
        public float jumpFatigueMinMul = 0.4f;
        public float jumpFatigueRecover = 2.5f;

        [Header("Hit (ACTION on the ball)")]
        [Tooltip("How close the ball has to be for ACTION to hit it. Out of this range, ACTION does " +
                 "nothing on the ground — the only way to affect anything else is the spin attack.")]
        public float hitRange = 1.6f;
        [Tooltip("Anti-spam only, not a real gameplay throttle: just long enough that one press can't " +
                 "register twice on the same or an adjacent tick.")]
        public float hitCooldown = 0.15f;

        [Header("Spin attack (JUMP while already airborne)")]
        [Tooltip("Replaces push/grab entirely: with the ball never possessed, this is now the only way " +
                 "to affect an opponent, and the strong way to hit the ball. One per jump — landing " +
                 "resets it, so it can't be chained in the air.")]
        public float spinLungeSpeed = 9f;
        public float spinDuration = 0.35f;
        public float spinHitRange = 1.8f;
        public float spinBallPowerMultiplier = 1.6f;
        public float spinCooldown = 1f;

        [Header("Knockback (dealt by the spin attack)")]
        public float pushRange = 1.7f;
        public float pushRadius = 1.3f;
        public float pushForce = 11f;
        public float stunDuration = 0.9f;

        [Networked] public int NetTeam { get; set; }        // 0 = Blue, 1 = Red
        [Networked] public bool TeamAssigned { get; set; }  // false until the master hands out a side
        [Networked] public bool IsBot { get; set; }         // simulated by the master, no client behind it
        [Networked] TickTimer StumbleUntil { get; set; }    // knocked-back / no control window
        [Networked] TickTimer SpinUntil { get; set; }       // mid-spin-attack, for remote presentation
        [Networked] public int KickSeq { get; set; }        // bumps on each hit (drives hit anim on all clients)

        // Presentation read-only helpers.
        public bool IsStumbled => Runner != null && !StumbleUntil.ExpiredOrNotRunning(Runner);
        public bool IsSpinning => Runner != null && !SpinUntil.ExpiredOrNotRunning(Runner);
        public bool Grounded => _grounded;
        // 0..1+, current horizontal speed as a fraction of moveSpeed — RunDust reads this to decide
        // "full regime" rather than any speed above zero.
        public float SpeedFraction01 => moveSpeed > 0.01f ? _horizVel.magnitude / moveSpeed : 0f;

        TickTimer _hitCd;
        TickTimer _spinCd;
        bool _usedSpin;       // one spin attack per jump; clears on landing
        float _spinFor;       // >0 while the current spin's active hit window is open
        Vector3 _spinDir;
        int _lastKickoffSeq = -1;

        // Every player currently in the match, humans and bots alike. The ball used to find players
        // through Runner.ActivePlayers, which by definition only knows about people with a
        // connection: a bot would have been invisible to it. One list, one rule, everybody.
        public static readonly List<NetPlayer> Live = new List<NetPlayer>();

        // Identity as other systems see it: the player OBJECT, not the person. NetworkId rather than
        // a raw int: it is the type Fusion documents as "the unique identifier for a network entity",
        // it carries its own serialisation, and it will not silently compare equal to some other
        // number.
        public NetworkId NetId => Object != null ? Object.Id : default;

        // The local human's own avatar, for the on-screen action button to read from — it needs to
        // know whether the BALL I'm near is in MY hit range, not any NetPlayer it happens to find.
        // Set next to _input below, the same "this is the human, not the brain" branch.
        public static NetPlayer Local { get; private set; }

        // Presentation reads for the action button: whether ACTION currently does anything, and how
        // full the cooldown ring around it should be. THE BALL IS NEVER POSSESSED, so this is a range
        // check, not "do I have it" — see HandleBall, which uses the identical check.
        public bool BallInHitRange => Ball != null && Vector3.Distance(transform.position, Ball.transform.position) <= hitRange;
        public float HitCooldown01 => Runner != null
            ? Mathf.Clamp01((_hitCd.RemainingTime(Runner) ?? 0f) / Mathf.Max(0.0001f, hitCooldown)) : 0f;

        CharacterController _cc;
        LocalInputSource _input;    // the human's joystick; null on a bot
        IPlayerBrain _brain;        // the bot's brain; null on a human
        Transform _cam;
        Renderer _rend;
        Renderer _ring;
        int _sfxKickSeq;
        bool _wasStumbled;
        static NetBall Ball => NetBall.Instance;
        bool _prevAction;
        bool _camReady;
        NameTag _tag;          // debug label over bots; temporary, see UpdateNameTag
        int _tagOrdinal = -1;

        Vector3 _horizVel;   // horizontal velocity (m/s)
        float _vY;           // vertical velocity
        float _coyote;       // coyote timer
        float _jumpBuf;      // jump buffer timer
        bool _grounded;
        float _accelT;              // seconds spent accelerating in the current held direction
        int _jumpFatigueStacks;     // consecutive ground jumps without a full rest
        float _timeSinceLastJump = 999f;   // large at spawn: the very first jump is always full power

        static readonly Color BlueColor = new Color(0.20f, 0.55f, 1.00f);
        static readonly Color RedColor = new Color(1.00f, 0.30f, 0.25f);

        public override void Spawned()
        {
            if (!Live.Contains(this)) Live.Add(this);

            _cc = GetComponent<CharacterController>();
            var vis = transform.Find("Visual");
            _rend = vis != null ? vis.GetComponentInChildren<Renderer>() : GetComponentInChildren<Renderer>();
            var ring = transform.Find("GroundRing");
            if (ring != null) _ring = ring.GetComponent<Renderer>();
            ApplyColor();

            if (HasStateAuthority)
            {
                // A bot carries its brain on its own object. Asking the OBJECT rather than the
                // networked IsBot flag keeps this independent of replication order — and the camera
                // must not be touched: on the master's client it belongs to the person playing, and a
                // bot that called SetupCamera would point it at itself.
                _brain = GetComponent<IPlayerBrain>();
                if (_brain == null)
                {
                    _input = UnityEngine.Object.FindAnyObjectByType<LocalInputSource>();
                    if (Camera.main != null) _cam = Camera.main.transform;
                    Local = this;
                }
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Live.Remove(this);
            if (Local == this) Local = null;
        }

        // The list is static and this game leaves a match and starts another without reloading the
        // scene, so an entry that outlived its object would be a ghost player in the next match.
        // Despawned covers the normal path; this covers every other way an object can go.
        void OnDestroy()
        {
            Live.Remove(this);
            if (Local == this) Local = null;
        }

        // Keep the colour in sync on remote clients once the networked team value arrives.
        // Also set up the local camera once (team is known by the first Render).
        public override void Render()
        {
            ApplyColor();
            UpdateRing();
            UpdateNameTag();

            // Feedback SFX + camera shake (every client observes the same networked counter, so a
            // hit reads and feels the same on every screen it is visible from — not only on whoever
            // threw it). Shake scales with proximity to THIS client's own camera: a hit across the
            // pitch is a whisper, one at your feet is felt.
            if (KickSeq != _sfxKickSeq)
            {
                _sfxKickSeq = KickSeq;
                if (SfxManager.Instance != null) SfxManager.Instance.PlayKick();
                var cam = Camera.main != null ? Camera.main.GetComponent<MatchCamera>() : null;
                if (cam != null)
                {
                    float d = Vector3.Distance(transform.position, cam.transform.position);
                    cam.Shake(Mathf.Clamp01(1f - d / 14f));
                }
            }
            bool st = IsStumbled;
            if (st && !_wasStumbled && SfxManager.Instance != null) SfxManager.Instance.PlayImpact();
            _wasStumbled = st;

            if (HasStateAuthority && _brain == null && !_camReady) SetupCamera();
        }

        // DEBUG, and asked for as such: bots wear their name while their behaviour is being judged.
        // To remove it, delete this method, its call above, the two fields, and Scripts/NameTag.cs.
        //
        // Driven by the networked IsBot and not by the brain: the brain exists only on the peer that
        // simulates the bot, and this has to be readable on every phone in the match.
        void UpdateNameTag()
        {
            if (!IsBot)
            {
                if (_tag != null) { Destroy(_tag.gameObject); _tag = null; _tagOrdinal = -1; }
                return;
            }
            if (_tag == null) _tag = NameTag.Attach(transform, "");
            if (_tag == null) return;

            int ord = BotOrdinal();
            if (ord == _tagOrdinal) return;   // the string is rebuilt only when the number changes
            _tagOrdinal = ord;
            _tag.Set("BOT " + ord);
        }

        // 1, 2, 3... in an order every client agrees on, because it is decided by network id — the
        // same number everywhere. No [Networked] field to add now and remember to remove with the
        // label.
        int BotOrdinal()
        {
            uint mine = Object != null ? Object.Id.Raw : 0u;
            int n = 1;
            foreach (var np in Live)
                if (np != null && np != this && np.IsBot && np.Object != null && np.Object.Id.Raw < mine) n++;
            return n;
        }

        // Team-coloured ground ring under every player. It used to brighten/pulse for whoever carried
        // the ball; there is no carrier any more, so it is a plain team colour now — see the PR for
        // what, if anything, should replace it as feedback for "the ball is in MY hit range".
        void UpdateRing()
        {
            if (_ring == null) return;
            _ring.material.color = (NetTeam == 1) ? RedColor : BlueColor;
        }

        Vector3 FlatForward() { Vector3 f = transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }

        void SetupCamera()
        {
            var camObj = Camera.main;
            if (camObj == null) return;
            _cam = camObj.transform;
            Vector3 attackDir = (NetTeam == 1) ? Vector3.left : Vector3.right; // Blue attacks +x, Red -x

            var orbit = camObj.GetComponent<MatchCamera>();
            if (orbit != null) orbit.SetTarget(transform, attackDir);      // real orbit camera (MainMatch look)
            else
            {
                var follow = camObj.GetComponent<NetFollowCamera>();
                if (follow == null) follow = camObj.gameObject.AddComponent<NetFollowCamera>();
                follow.target = transform;
            }
            _camReady = true;
        }

        void ApplyColor()
        {
            if (_rend == null) return;
            Color c = (NetTeam == 1) ? RedColor : BlueColor;
            if (Runner != null && !StumbleUntil.ExpiredOrNotRunning(Runner)) c = Color.Lerp(c, Color.gray, 0.65f);
            _rend.material.color = c;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _cc == null) return;
            if (_input == null && _brain == null) return;
            float dt = Runner.DeltaTime;

            // Safety net. The ball has always had one; the player never did, so anything that put a
            // player through the floor meant falling forever with no way back into the match.
            Vector3 here = transform.position;
            if (here.y < -5f || Mathf.Abs(here.x) > 40f || Mathf.Abs(here.z) > 30f)
            {
                Debug.LogWarning("[Net] player out of bounds at " + here + " — respawning");
                ResetToSpawn();
                return;
            }

            // One read per tick, one shape, whoever produced it. Everything below this line is the
            // same code for a person and for a bot.
            PlayerIntent want = ReadIntent(dt);

            // Match phase: self-reset on a new kickoff, and freeze unless we're PLAYING.
            var mc = MatchController.Instance;
            if (mc != null)
            {
                if (mc.KickoffSeq != _lastKickoffSeq) { _lastKickoffSeq = mc.KickoffSeq; ResetToSpawn(); }
                if (mc.CurPhase != MatchController.Phase.Playing)
                {
                    _horizVel = Vector3.zero;
                    if (_grounded && _vY < 0f) _vY = -2f; else _vY += gravity * dt;
                    var ff = _cc.Move(Vector3.up * _vY * dt);
                    _grounded = (ff & CollisionFlags.Below) != 0 || _cc.isGrounded;
                    _prevAction = want.Action;
                    return;
                }
            }

            bool stumbled = IsStumbled;

            // Stumbled (knocked back by a spin attack) = fully rooted, decelerating.
            if (stumbled)
            {
                _horizVel = Vector3.MoveTowards(_horizVel, Vector3.zero, deceleration * dt);
                if (_grounded && _vY < 0f) _vY = -2f; else _vY += gravity * (_vY < 0f ? fallMultiplier : 1f) * dt;
                var flagsF = _cc.Move((_horizVel + Vector3.up * _vY) * dt);
                _grounded = (flagsF & CollisionFlags.Below) != 0 || _cc.isGrounded;
                _prevAction = want.Action;
                return;
            }

            if (_grounded) _usedSpin = false; // landed: the next jump gets a fresh spin attack

            // --- Normal control ---
            Vector3 mdir = want.Move;

            float inMag = Mathf.Clamp01(mdir.magnitude);
            Vector3 wish = (inMag > 0.15f ? mdir.normalized : Vector3.zero) * moveSpeed * inMag;
            bool speedingUp = wish.sqrMagnitude > _horizVel.sqrMagnitude;

            // Progressive, not linear: a held direction builds up to full acceleration over
            // rampUpTime instead of applying it from the first tick, so reaching moveSpeed reads as
            // a run-up rather than a snap. Resets the instant the stick releases or the player is
            // already fast enough not to need it, so letting go and pressing again re-triggers it.
            if (inMag > 0.15f && speedingUp) _accelT += dt; else _accelT = 0f;
            float rampMul = speedingUp ? Mathf.Lerp(rampStartMul, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_accelT / rampUpTime))) : 1f;

            float rate = (speedingUp ? acceleration * rampMul : deceleration) * (_grounded ? 1f : airControl);
            _horizVel = Vector3.MoveTowards(_horizVel, wish, rate * dt);

            if (inMag > 0.15f)
            {
                Quaternion target = Quaternion.LookRotation(mdir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            }

            // Jump, or — if already airborne and the one spin attack for this jump hasn't fired yet —
            // a spin attack instead. Same button, contextual on ground state rather than on a timer.
            bool jumpPressed = ConsumeJump();
            if (jumpPressed) _jumpBuf = jumpBufferTime;
            _jumpBuf -= dt;
            _coyote = _grounded ? coyoteTime : _coyote - dt;
            _timeSinceLastJump += dt;
            bool canGroundJump = _jumpBuf > 0f && _coyote > 0f;
            if (canGroundJump)
            {
                // Anti-spam, from a phone test: full power on the first jump (or after resting
                // jumpFatigueRecover seconds), weaker on each one that follows too soon, down to
                // jumpFatigueMinMul by jumpFatigueMaxStacks — bunny-hopping tires the legs out.
                if (_timeSinceLastJump >= jumpFatigueRecover) _jumpFatigueStacks = 0;
                float fatigueMul = Mathf.Lerp(1f, jumpFatigueMinMul, (float)_jumpFatigueStacks / jumpFatigueMaxStacks);
                _vY = jumpVelocity * fatigueMul;
                _jumpFatigueStacks = Mathf.Min(_jumpFatigueStacks + 1, jumpFatigueMaxStacks);
                _timeSinceLastJump = 0f;
                _jumpBuf = 0f; _coyote = 0f; _grounded = false;
            }
            else if (jumpPressed && !_grounded && !_usedSpin && _spinCd.ExpiredOrNotRunning(Runner))
            {
                StartSpin();
            }

            if (_grounded && _vY < 0f) _vY = -2f;
            else _vY += gravity * (_vY < 0f ? fallMultiplier : 1f) * dt;

            Vector3 motion = (_horizVel + Vector3.up * _vY) * dt;
            CollisionFlags flags = _cc.Move(motion);
            _grounded = (flags & CollisionFlags.Below) != 0 || _cc.isGrounded;

            if (_spinFor > 0f) ResolveSpin(dt);
            else HandleBall(want);
        }

        // The joystick, resolved into the same world-space intent a brain produces. The camera maths
        // lives HERE and nowhere further down, because the camera belongs to the person: a bot has
        // none, and once the two paths meet in PlayerIntent nothing below needs to know which ran.
        PlayerIntent ReadIntent(float dt)
        {
            if (_brain != null) return _brain.Think(this, dt);

            var want = default(PlayerIntent);
            if (_input == null) return want;

            Vector2 mv = _input.GetMove();
            if (_cam != null)
            {
                Vector3 f = _cam.forward; f.y = 0f;
                Vector3 r = _cam.right; r.y = 0f;
                if (f.sqrMagnitude > 1e-6f) f.Normalize();
                if (r.sqrMagnitude > 1e-6f) r.Normalize();
                want.Move = r * mv.x + f * mv.y;
            }
            else want.Move = new Vector3(mv.x, 0f, mv.y);

            want.Action = _input.GetActionHeld();
            return want;
        }

        // Polled at the point of use rather than read with the rest of the intent: see IPlayerBrain.
        bool ConsumeJump()
        {
            if (_brain != null) return _brain.ConsumeJump();
            return _input != null && _input.ConsumeJump();
        }

        // Contextual ACTION, and it is now a SMALL contract: the ball is never possessed, so ACTION
        // hits it when it's close enough, and otherwise does nothing on the ground at all. Affecting
        // an opponent — or hitting the ball harder — is the spin attack's job now, not ACTION's.
        void HandleBall(PlayerIntent want)
        {
            bool action = want.Action;
            bool ballClose = Ball != null && Vector3.Distance(transform.position, Ball.transform.position) <= hitRange;

            if (ballClose && action && !_prevAction && _hitCd.ExpiredOrNotRunning(Runner))
            {
                Vector3 dir = want.Move.sqrMagnitude > 0.01f ? want.Move.normalized : FlatForward();
                Hit(dir, 1f);
                _hitCd = TickTimer.CreateFromSeconds(Runner, hitCooldown);
            }

            _prevAction = action;
        }

        // One jump attack, one shot: lunges forward in the current heading and stays "live" for
        // spinDuration, during which the first thing it touches — ball or opponent — resolves it.
        // Direction is taken once, at launch, exactly like a real jump-kick would commit to a line
        // rather than steering mid-air.
        void StartSpin()
        {
            _usedSpin = true;
            _spinFor = spinDuration;
            _spinCd = TickTimer.CreateFromSeconds(Runner, spinCooldown);
            SpinUntil = TickTimer.CreateFromSeconds(Runner, spinDuration);
            _spinDir = _horizVel.sqrMagnitude > 0.1f ? new Vector3(_horizVel.x, 0f, _horizVel.z).normalized : FlatForward();
            _horizVel = _spinDir * spinLungeSpeed;
        }

        // Checked every tick the spin is live. Ball beats opponent if somehow both are in range on
        // the same tick — same priority ACTION already gives the ball everywhere else.
        void ResolveSpin(float dt)
        {
            _spinFor -= dt;

            var ball = Ball;
            if (ball != null && Vector3.Distance(transform.position, ball.transform.position) <= spinHitRange)
            {
                Hit(_spinDir, spinBallPowerMultiplier);
                _spinFor = 0f;
                return;
            }

            var target = FindTargetInFront();
            if (target != null)
            {
                Vector3 dir = target.transform.position - transform.position; dir.y = 0f;
                target.RPC_Push(dir, pushForce);
                _spinFor = 0f;
            }
        }

        // The impulse is applied by the ball's authority; the animation and SFX fire here immediately
        // (KickSeq is on MY object, so that write is authoritative and instant). powerMultiplier is
        // 1 for a normal ACTION hit, higher for a spin attack that connects — same fundamental path,
        // never a second ball-physics system for the "harder" version.
        //
        // Two ways to reach the same authority-side method. A remote player has to ask over the wire;
        // whoever is ALREADY the ball's authority — every bot, since bots exist only on the master —
        // calls it directly, because an RPC to oneself is a message with nothing to carry, and
        // RPC_Hit resolves the SENDER, which for a bot would resolve to the master's own avatar.
        void Hit(Vector3 dir, float powerMultiplier)
        {
            var ball = Ball;
            if (ball == null) return;

            if (ball.Object != null && ball.Object.HasStateAuthority) ball.Hit(this, dir, powerMultiplier);
            else ball.RPC_Hit(dir, powerMultiplier);

            KickSeq++; // triggers the hit animation on all clients
        }

        // Also the spin attack's opponent target — same "must be in front" query pushing used.
        NetPlayer FindTargetInFront()
        {
            Vector3 center = transform.position + transform.forward * (pushRange * 0.5f);
            var hits = Physics.OverlapSphere(center, pushRadius);
            NetPlayer best = null; float bestD = float.MaxValue;
            foreach (var h in hits)
            {
                var np = h.GetComponentInParent<NetPlayer>();
                if (np == null || np == this || np.NetTeam == NetTeam) continue;
                Vector3 to = np.transform.position - transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d > 0.01f && Vector3.Dot(transform.forward, to.normalized) < 0.25f) continue; // must be in front
                if (d < bestD) { bestD = d; best = np; }
            }
            return best;
        }

        // The physical bump: a CharacterController does not push a Rigidbody just by colliding with
        // it, Unity never applies that force on its own, so without this the ball sat still while a
        // player visibly overlapped it. Fires every tick the CharacterController is actively moving
        // into the ball's collider, on the mover's own authority client — not on every collision
        // (e.g. the ball rolling into a stationary player), because ordinary physics already handles
        // a moving Rigidbody hitting a static collider on its own. The deliberate ACTION hit
        // (NetBall.Hit) stays the strong, precise interaction; this is only the ambient "I walked
        // into it" push.
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!HasStateAuthority || Runner == null) return;
            var hitBall = hit.collider.GetComponentInParent<NetBall>();
            if (hitBall == null || hitBall != Ball) return;

            Vector3 vel = _horizVel;
            if (vel.sqrMagnitude < 0.01f) return;

            // This tick's own delta time: Bump needs it to turn a velocity into a one-shot nudge that
            // does not depend on how often this callback happens to fire (see NetBall.Bump).
            float dt = Runner.DeltaTime;
            if (hitBall.Object != null && hitBall.Object.HasStateAuthority) hitBall.Bump(vel, dt);
            else hitBall.RPC_Bump(vel, dt);
        }

        // Teleport back to the team's kickoff spot (called on a new kickoff, and by the out-of-bounds
        // safety net). Spawn height: the capsule is 1.9 tall with its pivot at the centre, so the
        // feet sit 0.95 below this Y and the collision floor's top face is at y=0. The old 1.0/1.1
        // left barely 5-15cm of clearance — thinner than it looks once skin width and float error
        // are in play, and a CharacterController that starts the frame already intersecting a
        // collider can be pushed out downwards instead of up. SpawnHeight keeps a real margin.
        public const float SpawnHeight = 1.5f;

        void ResetToSpawn()
        {
            bool blue = NetTeam == 0;
            float x = blue ? -6f : 6f;
            // A fixed home slot for the whole match: how many team mates have a lower NetworkId than
            // me, among those already assigned a side. Counting rank is unique by construction: no
            // two team mates can ever share a slot, and it means the same thing every kickoff for as
            // long as the roster doesn't change.
            int slot = 0;
            foreach (var np in Live)
                if (np != null && np != this && np.TeamAssigned && np.NetTeam == NetTeam
                    && np.Object != null && Object != null && np.Object.Id.Raw < Object.Id.Raw)
                    slot++;
            float z = (slot - 1) * 3f;
            Vector3 pos = new Vector3(x, SpawnHeight, z);
            Quaternion rot = Quaternion.LookRotation(blue ? Vector3.right : Vector3.left, Vector3.up);
            if (_cc != null) _cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            if (_cc != null) _cc.enabled = true;
            _horizVel = Vector3.zero; _vY = 0f;
            _grounded = false;   // let the next tick re-detect it instead of assuming the old value
            _usedSpin = false; _spinFor = 0f;
            _accelT = 0f; _jumpFatigueStacks = 0; _timeSinceLastJump = 999f;
            StumbleUntil = default;
        }

        // Sides are decided by the master now that matchmaking replaced the blue/red buttons, but only
        // this peer may write its own networked state — so the master asks and we do it. Re-seating is
        // the point: we spawned on the blue half before anyone knew which side was ours.
        public void ApplyTeam(int team)
        {
            if (!HasStateAuthority) return;
            NetTeam = team;
            TeamAssigned = true;
            ResetToSpawn();
        }

        // Executed on the TARGET's authority: apply knockback + stumble to itself. Now only reached
        // from a landed spin attack — there is no more tap-push to also call it.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Push(Vector3 dir, float force)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) dir.Normalize();
            _horizVel = dir * force;
            if (_vY < 2f) _vY = 2f; // small pop
            StumbleUntil = TickTimer.CreateFromSeconds(Runner, stunDuration);
        }
    }
}
