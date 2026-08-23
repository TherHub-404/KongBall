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

        [Header("Jump / gravity")]
        public float jumpVelocity = 8.5f;
        public float gravity = -25f;
        public float fallMultiplier = 1.7f;
        public float coyoteTime = 0.12f;
        public float jumpBufferTime = 0.12f;

        [Header("Hit (ACTION on the ball)")]
        [Tooltip("How close the ball has to be for ACTION to hit it instead of pushing/grabbing an " +
                 "opponent. When both are in range at once, the ball always wins.")]
        public float hitRange = 1.6f;
        [Tooltip("Anti-spam only, not a real gameplay throttle: just long enough that one press can't " +
                 "register twice on the same or an adjacent tick. The real cooldown that matters for " +
                 "pace is push/grab's, below.")]
        public float hitCooldown = 0.15f;

        [Header("Push / Grab (ACTION without ball in range)")]
        public float pushRange = 1.7f;
        public float pushRadius = 1.3f;
        public float pushForce = 11f;
        public float stunDuration = 0.9f;
        public float pushCooldown = 5f;
        public float holdThreshold = 0.3f;   // hold longer than this = GRAB (else = push)
        public float grabDuration = 1.5f;    // max grab hold
        public float grabMoveMultiplier = 0.4f; // grabber can still shuffle slowly while holding

        [Networked] public int NetTeam { get; set; }        // 0 = Blue, 1 = Red
        [Networked] public bool TeamAssigned { get; set; }  // false until the master hands out a side
        [Networked] public bool IsBot { get; set; }         // simulated by the master, no client behind it
        [Networked] TickTimer StumbleUntil { get; set; }    // knocked-back / no control window
        [Networked] TickTimer HeldUntil { get; set; }       // grabbed / rooted in place
        [Networked] TickTimer GrabbingUntil { get; set; }   // I am actively grabbing someone
        [Networked] public int KickSeq { get; set; }        // bumps on each hit (drives hit anim on all clients)

        // Presentation read-only helpers.
        public bool IsStumbled => Runner != null && !StumbleUntil.ExpiredOrNotRunning(Runner);
        public bool IsHeld => Runner != null && !HeldUntil.ExpiredOrNotRunning(Runner);
        public bool IsGrabbing => Runner != null && !GrabbingUntil.ExpiredOrNotRunning(Runner);

        TickTimer _hitCd;
        TickTimer _pushCd;
        TickTimer _grabLock;   // grabber is rooted while holding a victim
        NetPlayer _grabTarget;
        float _actionHeldTime;
        bool _grabFired;
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

        // Presentation reads for the action button: which word it should say, and how full the
        // cooldown ring around it should be. THE BALL IS NEVER POSSESSED, so this is a range check,
        // not "do I have it" — see HandleBall, which uses the identical check to decide hit vs push.
        public bool BallInHitRange => Ball != null && Vector3.Distance(transform.position, Ball.transform.position) <= hitRange;
        public float HitCooldown01 => Runner != null
            ? Mathf.Clamp01((_hitCd.RemainingTime(Runner) ?? 0f) / Mathf.Max(0.0001f, hitCooldown)) : 0f;
        public float PushCooldown01 => Runner != null
            ? Mathf.Clamp01((_pushCd.RemainingTime(Runner) ?? 0f) / Mathf.Max(0.0001f, pushCooldown)) : 0f;

        CharacterController _cc;
        LocalInputSource _input;    // the human's joystick; null on a bot
        IPlayerBrain _brain;        // the bot's brain; null on a human
        Transform _cam;
        Renderer _rend;
        Renderer _ring;
        int _sfxKickSeq;
        bool _wasStumbled;
        static NetBall Ball => NetBall.Instance;
        Collider _ballCol;
        bool _ballIgnored;
        bool _prevAction;
        bool _camReady;
        NameTag _tag;          // debug label over bots; temporary, see UpdateNameTag
        int _tagOrdinal = -1;

        Vector3 _horizVel;   // horizontal velocity (m/s)
        float _vY;           // vertical velocity
        float _coyote;       // coyote timer
        float _jumpBuf;      // jump buffer timer
        bool _grounded;

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

            // Feedback SFX (all clients observe networked state).
            if (KickSeq != _sfxKickSeq) { _sfxKickSeq = KickSeq; if (SfxManager.Instance != null) SfxManager.Instance.PlayKick(); }
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

            UpdateBallIgnore(); // per-client, once: the ball never body-blocks a player's approach

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
            bool held = IsHeld;

            // Grab ends on button release or when it times out.
            if (_grabTarget != null && (_grabLock.ExpiredOrNotRunning(Runner) || !want.Action)) EndGrab();
            bool grabbing = !_grabLock.ExpiredOrNotRunning(Runner);

            // Stumbled / held (victim) = fully rooted. Grabbing (grabber) can still shuffle slowly.
            if (stumbled || held)
            {
                if (stumbled) _horizVel = Vector3.MoveTowards(_horizVel, Vector3.zero, deceleration * dt);
                else _horizVel = Vector3.zero;
                if (_grounded && _vY < 0f) _vY = -2f; else _vY += gravity * (_vY < 0f ? fallMultiplier : 1f) * dt;
                var flagsF = _cc.Move((_horizVel + Vector3.up * _vY) * dt);
                _grounded = (flagsF & CollisionFlags.Below) != 0 || _cc.isGrounded;
                _prevAction = want.Action;
                return;
            }

            // --- Normal control (reduced speed while grabbing) ---
            float spd = grabbing ? moveSpeed * grabMoveMultiplier : moveSpeed;
            Vector3 mdir = want.Move;

            float inMag = Mathf.Clamp01(mdir.magnitude);
            Vector3 wish = (inMag > 0.15f ? mdir.normalized : Vector3.zero) * spd * inMag;
            float rate = (wish.sqrMagnitude > _horizVel.sqrMagnitude ? acceleration : deceleration) * (_grounded ? 1f : airControl);
            _horizVel = Vector3.MoveTowards(_horizVel, wish, rate * dt);

            if (inMag > 0.15f)
            {
                Quaternion target = Quaternion.LookRotation(mdir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            }

            // No jumping while grabbing.
            if (!grabbing && ConsumeJump()) _jumpBuf = jumpBufferTime;
            _jumpBuf -= dt;
            _coyote = _grounded ? coyoteTime : _coyote - dt;
            if (!grabbing && _jumpBuf > 0f && _coyote > 0f) { _vY = jumpVelocity; _jumpBuf = 0f; _coyote = 0f; _grounded = false; }

            if (_grounded && _vY < 0f) _vY = -2f;
            else _vY += gravity * (_vY < 0f ? fallMultiplier : 1f) * dt;

            Vector3 motion = (_horizVel + Vector3.up * _vY) * dt;
            CollisionFlags flags = _cc.Move(motion);
            _grounded = (flags & CollisionFlags.Below) != 0 || _cc.isGrounded;

            // While grabbing, don't process hit/push/new-grab; just keep the action edge in sync.
            if (grabbing) _prevAction = want.Action;
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

        // Contextual ACTION. THE BALL IS NEVER POSSESSED: if it is close enough, ACTION hits it —
        // always, even with an opponent also in range, per CORE_GAMEPLAY_RESET section 10-11. Only
        // when the ball is out of reach does ACTION fall back to push/grab on a player.
        void HandleBall(PlayerIntent want)
        {
            bool action = want.Action;
            bool ballClose = Ball != null && Vector3.Distance(transform.position, Ball.transform.position) <= hitRange;

            if (ballClose)
            {
                // Instantaneous, on the PRESS edge rather than on release: there is no carry window
                // left to hold the button through while aiming. Direction comes from where the player
                // is already heading, falling back to facing when standing still — the same principle
                // push already uses below.
                if (action && !_prevAction && _hitCd.ExpiredOrNotRunning(Runner))
                {
                    Vector3 dir = want.Move.sqrMagnitude > 0.01f ? want.Move.normalized : FlatForward();
                    Hit(dir);
                    _hitCd = TickTimer.CreateFromSeconds(Runner, hitCooldown);
                }
                // Never let a hit's press also arm a grab/push the instant the ball rolls away.
                _actionHeldTime = 0f;
                _grabFired = false;
            }
            else
            {
                // No ball in range: HOLD = GRAB, quick TAP = PUSH.
                if (action) _actionHeldTime += Runner.DeltaTime; else _actionHeldTime = 0f;

                if (action && !_grabFired && _actionHeldTime >= holdThreshold)
                {
                    var target = FindTargetInFront();
                    if (target != null)
                    {
                        target.RPC_Grab(grabDuration);
                        _grabTarget = target;
                        _grabLock = TickTimer.CreateFromSeconds(Runner, grabDuration);
                        GrabbingUntil = TickTimer.CreateFromSeconds(Runner, grabDuration);
                        _grabFired = true;
                    }
                }
                if (!action && _prevAction && !_grabFired && _pushCd.ExpiredOrNotRunning(Runner))
                {
                    var target = FindTargetInFront();
                    if (target != null)
                    {
                        Vector3 dir = target.transform.position - transform.position; dir.y = 0f;
                        target.RPC_Push(dir, pushForce);
                        _pushCd = TickTimer.CreateFromSeconds(Runner, pushCooldown);
                    }
                }
                if (!action) _grabFired = false;
            }

            _prevAction = action;
        }

        // The impulse is applied by the ball's authority; the animation and SFX fire here immediately
        // (KickSeq is on MY object, so that write is authoritative and instant).
        //
        // Two ways to reach the same authority-side method. A remote player has to ask over the wire;
        // whoever is ALREADY the ball's authority — every bot, since bots exist only on the master —
        // calls it directly, because an RPC to oneself is a message with nothing to carry, and
        // RPC_Hit resolves the SENDER, which for a bot would resolve to the master's own avatar.
        void Hit(Vector3 dir)
        {
            var ball = Ball;
            if (ball == null) return;

            if (ball.Object != null && ball.Object.HasStateAuthority) ball.Hit(this, dir);
            else ball.RPC_Hit(dir);

            KickSeq++; // triggers the hit animation on all clients
        }

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

        void EndGrab()
        {
            _grabLock = default;
            GrabbingUntil = default;
            if (_grabTarget != null) { _grabTarget.RPC_Release(); _grabTarget = null; }
        }

        // The ball ignores every player's body, always — not just whoever "has" it, because nobody
        // does any more. Reason: on a non-authority client the ball is a kinematic NetworkTransform
        // proxy, so a moving CharacterController gets blocked by it (the ball acts like a little
        // wall), which would stop a player ever reaching hit range in the first place. The hit itself
        // is a gameplay-authored interaction (NetBall.Hit), not raw collision, by design — see
        // CORE_GAMEPLAY_RESET section 09. Registered once, the first tick both colliders exist.
        void UpdateBallIgnore()
        {
            if (_ballIgnored || _cc == null) return;
            var ball = Ball;
            if (ball == null) return;
            if (_ballCol == null) _ballCol = ball.GetComponent<Collider>();
            if (_ballCol == null) return;
            Physics.IgnoreCollision(_ballCol, _cc, true);
            _ballIgnored = true;
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

        // Executed on the TARGET's authority: apply knockback + stumble to itself.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Push(Vector3 dir, float force)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) dir.Normalize();
            _horizVel = dir * force;
            if (_vY < 2f) _vY = 2f; // small pop
            StumbleUntil = TickTimer.CreateFromSeconds(Runner, stunDuration);
        }

        // Executed on the TARGET's authority: grabbed = rooted in place.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Grab(float dur)
        {
            _horizVel = Vector3.zero;
            HeldUntil = TickTimer.CreateFromSeconds(Runner, dur);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Release()
        {
            HeldUntil = default;
        }
    }
}
