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

        [Header("Push / Grab (ACTION without ball)")]
        public float pushRange = 1.7f;
        public float pushRadius = 1.3f;
        public float pushForce = 11f;
        public float stunDuration = 0.9f;
        public float pushCooldown = 0.6f;
        public float holdThreshold = 0.3f;   // hold longer than this = GRAB (else = push)
        public float grabDuration = 1.5f;    // max grab hold
        public float grabMoveMultiplier = 0.4f; // grabber can still shuffle slowly while holding

        [Networked] public int NetTeam { get; set; }        // 0 = Blue, 1 = Red
        [Networked] public bool TeamAssigned { get; set; }  // false until the master hands out a side
        [Networked] public bool IsBot { get; set; }         // simulated by the master, no client behind it
        [Networked] TickTimer StumbleUntil { get; set; }    // knocked-back / no control window
        [Networked] TickTimer HeldUntil { get; set; }       // grabbed / rooted in place
        [Networked] TickTimer GrabbingUntil { get; set; }   // I am actively grabbing someone
        [Networked] public int KickSeq { get; set; }        // bumps on each kick (drives kick anim on all clients)

        // Presentation read-only helpers.
        public bool IsStumbled => Runner != null && !StumbleUntil.ExpiredOrNotRunning(Runner);
        public bool IsHeld => Runner != null && !HeldUntil.ExpiredOrNotRunning(Runner);
        public bool IsGrabbing => Runner != null && !GrabbingUntil.ExpiredOrNotRunning(Runner);

        TickTimer _pushCd;
        TickTimer _grabLock;   // grabber is rooted while holding a victim
        NetPlayer _grabTarget;
        float _actionHeldTime;
        bool _grabFired;
        int _lastKickoffSeq = -1;

        // Every player currently in the match, humans and bots alike. The ball used to find players
        // through Runner.ActivePlayers, which by definition only knows about people with a
        // connection: a bot would have been invisible to possession. One list, one rule, everybody.
        public static readonly List<NetPlayer> Live = new List<NetPlayer>();

        // Identity as the BALL sees it: the player object, not the person. Used instead of PlayerRef
        // so that something without a PlayerRef can still carry the ball. NetworkId is the type
        // Fusion provides for exactly this — "the unique identifier for a network entity" — and it
        // cannot be confused with any other number the way a raw int can.
        public NetworkId NetId => Object != null ? Object.Id : default;

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
        float _kickIgnoreUntil;
        bool _prevAction;
        bool _camReady;
        Vector2 _lastAim;
        LineRenderer _aimLine;

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
                }
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Live.Remove(this);
        }

        // The list is static and this game leaves a match and starts another without reloading the
        // scene, so an entry that outlived its object would be a ghost player in the next match.
        // Despawned covers the normal path; this covers every other way an object can go.
        void OnDestroy()
        {
            Live.Remove(this);
        }

        // Keep the colour in sync on remote clients once the networked team value arrives.
        // Also set up the local camera once (team is known by the first Render).
        public override void Render()
        {
            ApplyColor();
            UpdateRing();

            // Feedback SFX (all clients observe networked state).
            if (KickSeq != _sfxKickSeq) { _sfxKickSeq = KickSeq; if (SfxManager.Instance != null) SfxManager.Instance.PlayKick(); }
            bool st = IsStumbled;
            if (st && !_wasStumbled && SfxManager.Instance != null) SfxManager.Instance.PlayImpact();
            _wasStumbled = st;

            if (HasStateAuthority && _brain == null)
            {
                if (!_camReady) SetupCamera();
                UpdateAimLine();
            }
        }

        // Team-coloured ground ring under every player; brightens/pulses for the ball owner.
        void UpdateRing()
        {
            if (_ring == null) return;
            bool mine = Ball != null && Ball.OwnerId == NetId;
            Color team = (NetTeam == 1) ? RedColor : BlueColor;
            Color c = team;
            if (mine)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(Time.time * 9f);
                c = Color.Lerp(team, Color.white, 0.55f + 0.35f * p);
            }
            _ring.material.color = c;
        }

        // Ground aim preview while holding the kick button with the ball (local only).
        void UpdateAimLine()
        {
            if (_input == null || Ball == null) { HideAim(); return; }
            bool mine = Ball.OwnerId == NetId;
            if (!(mine && _input.GetActionHeld())) { HideAim(); return; }

            Vector2 d = _input.GetAimDelta();
            float refPx = Mathf.Max(1f, Screen.height * 0.22f);
            float deadPx = Screen.height * 0.04f;
            Vector3 dir; float power;
            if (d.magnitude > deadPx && _cam != null)
            {
                Vector3 f = _cam.forward; f.y = 0f; if (f.sqrMagnitude > 1e-6f) f.Normalize();
                Vector3 r = _cam.right; r.y = 0f; if (r.sqrMagnitude > 1e-6f) r.Normalize();
                dir = r * d.x + f * d.y; dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : FlatForward();
                power = Mathf.Clamp01(d.magnitude / refPx);
            }
            else { dir = FlatForward(); power = 0f; }
            power = Mathf.Clamp(power, 0.15f, 1f);

            EnsureAimLine();
            Vector3 p0 = Ball.VisualPosition; p0.y = 0.12f;   // line up with the ball we can SEE
            Vector3 p1 = p0 + dir * Mathf.Lerp(1.5f, 6.5f, power);
            _aimLine.enabled = true;
            _aimLine.SetPosition(0, p0);
            _aimLine.SetPosition(1, p1);
            Color c = Color.Lerp(new Color(0.4f, 1f, 0.45f), new Color(1f, 0.4f, 0.2f), power);
            _aimLine.startColor = c; _aimLine.endColor = c;
        }

        void HideAim() { if (_aimLine != null) _aimLine.enabled = false; }

        Vector3 FlatForward() { Vector3 f = transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }

        void EnsureAimLine()
        {
            if (_aimLine != null) return;
            var go = new GameObject("NetAimLine");
            _aimLine = go.AddComponent<LineRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _aimLine.material = new Material(sh);
            _aimLine.widthMultiplier = 0.28f;
            _aimLine.numCapVertices = 4;
            _aimLine.positionCount = 2;
            _aimLine.textureMode = LineTextureMode.Stretch;
            _aimLine.alignment = LineAlignment.View;
            _aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _aimLine.enabled = false;
        }

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

            UpdateBallIgnore(); // per-client: the ball ignores ME only while I carry it (+kick grace)

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

            // While grabbing, don't process kick/new-grab; just keep action edge in sync.
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

            // The kick this tick would produce, from the drag remembered while the button was down.
            // Resolved BEFORE the drag is updated, because a kick fires on the RELEASE tick — by
            // which time the finger has gone and GetAimDelta reads zero.
            if (_lastAim.sqrMagnitude > 100f && _cam != null)
            {
                Vector3 f = _cam.forward; f.y = 0f; f.Normalize();
                Vector3 r = _cam.right; r.y = 0f; r.Normalize();
                want.KickDir = (r * _lastAim.x + f * _lastAim.y).normalized;
            }
            else want.KickDir = Vector3.zero;   // straight ahead

            float power = 0.5f;
            if (_lastAim.sqrMagnitude > 1f) power = Mathf.Clamp01(_lastAim.magnitude / (Screen.height * 0.22f));
            want.KickPower = Mathf.Max(power, 0.35f);

            if (want.Action)
            {
                Vector2 aim = _input.GetAimDelta();
                if (aim.sqrMagnitude > 1f) _lastAim = aim;
            }
            else _lastAim = Vector2.zero;

            return want;
        }

        // Polled at the point of use rather than read with the rest of the intent: see IPlayerBrain.
        bool ConsumeJump()
        {
            if (_brain != null) return _brain.ConsumeJump();
            return _input != null && _input.ConsumeJump();
        }

        // Contextual ACTION: with ball = KICK (aim then release), without ball = PUSH / GRAB.
        void HandleBall(PlayerIntent want)
        {
            bool action = want.Action;

            // Possession is NOT claimed from here. The ball's authority decides it by proximity
            // (NetBall.UpdatePossession) and we simply read the result — no client ever writes ball
            // state, which is what makes possession impossible to desync.
            bool mine = Ball != null && Ball.OwnerId == NetId;

            if (mine)
            {
                // Kick on RELEASE, toward the direction the intent named (zero = straight ahead).
                if (!action && _prevAction)
                    Shoot(want.KickDir.sqrMagnitude > 1e-4f ? want.KickDir : transform.forward, want.KickPower);
            }
            else
            {
                // No ball: HOLD = GRAB, quick TAP = PUSH.
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
        // Two ways to reach the same authority-side method. A remote carrier has to ask over the wire
        // and predicts the mesh leaving its foot so the shot does not wait a round trip; whoever is
        // ALREADY the ball's authority — which is every bot, since bots exist only on the master —
        // calls it directly, because an RPC to oneself is a message with nothing to carry and
        // RPC_Kick resolves the SENDER, which for a bot would resolve to the master's own avatar.
        void Shoot(Vector3 dir, float power01)
        {
            var ball = Ball;
            if (ball == null) return;
            power01 = Mathf.Clamp01(power01);

            if (ball.Object != null && ball.Object.HasStateAuthority) ball.Kick(this, dir, power01);
            else { ball.RPC_Kick(dir, power01); ball.NotifyLocalKick(); }

            KickSeq++; // triggers the kick animation on all clients
            _kickIgnoreUntil = Time.time + 0.5f; // let the kicked ball escape my body
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

        // The ball ignores the LOCAL player's body. Reason: on a non-owner's client the ball is a
        // kinematic NetworkTransform proxy, so a moving CharacterController gets blocked by it (the
        // ball acts like a little wall) — which stopped the approaching player from ever reaching
        // claim range ("only one player can attach" bug). Possession is proximity-based; defense is
        // via push/grab/steal, not body-blocking. Owner dribble/back-kick needed this ignore anyway.
        // Registered once, the first tick both colliders exist.
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
            // Spread along the goal line so team mates do not start inside one another. A bot has no
            // PlayerRef to be spread by — every bot in the room answers to the master's — so its own
            // network id does the job instead.
            int id = _brain != null ? (int)(Object.Id.Raw % 3u) : Object.StateAuthority.PlayerId;
            float z = ((id % 3) - 1) * 3f;
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

        // Executed on the TARGET's authority: grabbed = rooted in place (and drops the ball).
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
