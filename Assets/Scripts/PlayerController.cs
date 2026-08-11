using UnityEngine;

namespace CalcioStumble
{
    // Core actor. Movement / rotation / possession-kick / push / restrain are independent
    // blocks. Input arrives through IPlayerInputSource (only the controlled player has one),
    // so a network input source can drive this class unchanged in phase 2.
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Refs")]
        public TeamMember team;

        [Header("Movement (desired-velocity)")]
        public float moveSpeed = 6f;
        [Tooltip("How fast we reach target velocity (units/s^2).")]
        public float acceleration = 45f;
        [Tooltip("How fast we slow to a stop (units/s^2).")]
        public float deceleration = 55f;
        [Tooltip("Facing turn speed (deg/s) — smoothed, not instant.")]
        public float turnSpeed = 720f;
        public float aimDeadzone = 0.05f;
        public float moveDeadzone = 0.2f;

        [Header("Possession")]
        [Tooltip("Reach (surface gap) at which a free ball is captured. Spec: 0.4.")]
        public float possessionRadius = 0.4f;

        [Header("Kick (8.1)")]
        public float kickMinForce = 3f;
        public float kickMaxForce = 9f;
        public AnimationCurve kickPowerCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public float kickUpwardRatio = 0.12f;
        public float kickCooldown = 0.2f;
        [Tooltip("Drag length (as fraction of screen height) for MAX kick power.")]
        public float aimRefScreenFrac = 0.22f;
        [Tooltip("Drag below this fraction of screen height = aim where you face.")]
        public float aimDeadScreenFrac = 0.04f;

        [Header("Push (8.2)")]
        [Tooltip("Reach (surface gap) to an opponent. Spec: 0.6.")]
        public float pushRadius = 0.6f;
        [Tooltip("Small backward recoil impulse applied to the pushed opponent.")]
        public float pushForce = 14f;
        [Tooltip("Seconds the pushed opponent is stunned (inert + invulnerable).")]
        public float stunDuration = 1f;
        public float pushCooldown = 0.4f;

        [Header("Restrain / Grab (8.3) — Fall Guys style, fixed duration, auto-release")]
        [Tooltip("Fixed grab duration. Holding the button does NOT extend it.")]
        public float restrainMaxDuration = 1.5f;
        public float restrainDistance = 1.1f;
        public float restrainMoveMultiplier = 0.5f;
        [Tooltip("Cooldown after a grab before you can grab again (prevents chain-grabs).")]
        public float grabCooldown = 0.6f;
        [Tooltip("Held beyond this = grab; shorter = tap (kick/push). "
               + "Kept generous so touch taps reliably PUSH instead of grabbing.")]
        public float holdThreshold = 0.35f;

        public PlayerState State { get; private set; } = PlayerState.Normal;

        const float PLAYER_RADIUS = 0.5f;

        IPlayerInputSource _input;
        Rigidbody _rb;
        MeshRenderer _rend;
        MaterialPropertyBlock _mpb;
        Color _baseColor = Color.white;
        float _flashTimer;
        float _currentSpeed01, _kickTimer, _pushTimer, _stumbleTimer, _grabTimer;
        Vector3 _velocity; // desired-velocity locomotion (planar)

        // planar movement info exposed for the ball's dribble steering
        Vector3 _planarMoveDir = Vector3.forward;
        public Vector3 PlanarMoveDir => _planarMoveDir;
        public float PlannedSpeed01 => _currentSpeed01;

        // cached main camera for camera-relative input
        Transform _cam;
        Transform ResolveCam()
        {
            if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
            return _cam;
        }

        // action edge / tap-hold tracking
        bool _prevHeld;
        float _pressTime;
        bool _actionConsumed;

        // Single explicit action authority (KB04) — replaces _aiming/_restraining booleans.
        PlayerAction _action = PlayerAction.None;
        public PlayerAction Action => _action;
        float _restrainStart;
        PlayerController _restrainTarget;
        LineRenderer _aimLine;
        Vector3 _aimDir = Vector3.forward;   // cached last aim (release uses this, not zeroed drag)
        float _aimPower;

        static readonly Collider[] _overlap = new Collider[32];

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rend = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            if (_rend != null && _rend.sharedMaterial != null && _rend.sharedMaterial.HasProperty("_BaseColor"))
                _baseColor = _rend.sharedMaterial.GetColor("_BaseColor");
        }

        // brief white flash on impact (uses a property block, no material instancing)
        void FlashImpact() { _flashTimer = 0.16f; }

        void UpdateFlash()
        {
            if (_rend == null) return;
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_flashTimer / 0.16f);
                Color c = Color.Lerp(_baseColor, Color.white, t);
                _mpb.SetColor("_BaseColor", c);
                _rend.SetPropertyBlock(_mpb);
            }
            else if (_mpb != null)
            {
                _mpb.SetColor("_BaseColor", _baseColor);
                _rend.SetPropertyBlock(_mpb);
            }
        }

        public void AssignInput(IPlayerInputSource src) { _input = src; }

        bool IsControlled => team != null && team.isControlled && _input != null;
        bool ControlsEnabled => GameManager.Instance == null || GameManager.Instance.ControlsEnabled;
        bool IsBallOwner => GameManager.Instance != null && GameManager.Instance.Ball != null
                            && GameManager.Instance.Ball.Owner == this;

        // Authority (KB04 §03): one place answers "can I move / act / be hit?".
        public bool IsInvulnerable => State != PlayerState.Normal; // stunned or grabbed
        bool CanSelfControl => State == PlayerState.Normal;

        // Cancels any in-progress action (aim/grab) and its visuals. Single exit point.
        void CancelAction()
        {
            if (_action == PlayerAction.Grabbing) EndRestrain();
            else if (_action == PlayerAction.Aiming) { _action = PlayerAction.None; HideAim(); }
        }

        void Update()
        {
            UpdateFlash();

            if (_kickTimer > 0f) _kickTimer -= Time.deltaTime;
            if (_pushTimer > 0f) _pushTimer -= Time.deltaTime;
            if (_grabTimer > 0f) _grabTimer -= Time.deltaTime;

            if (State == PlayerState.Stumbled)
            {
                _stumbleTimer -= Time.deltaTime;
                if (_stumbleTimer <= 0f) Recover();
                return;
            }
            if (State == PlayerState.Restrained) return; // held; anchored by the holder

            if (!IsControlled || !ControlsEnabled)
            {
                _prevHeld = false;
                CancelAction();
                return;
            }

            HandleAction();
        }

        void HandleAction()
        {
            bool held = _input.GetActionHeld();
            bool up = !held && _prevHeld;
            float now = Time.time;

            if (held && !_prevHeld) { _pressTime = now; _actionConsumed = false; }

            if (IsBallOwner)
            {
                // Own the ball: press + DRAG to aim, release to kick/pass in that direction.
                if (_action == PlayerAction.Grabbing) EndRestrain();
                if (held) { _action = PlayerAction.Aiming; UpdateAim(); }
                if (up && _action == PlayerAction.Aiming) { _action = PlayerAction.None; DoAimKick(); HideAim(); }
                if (!held && _action == PlayerAction.Aiming) { _action = PlayerAction.None; HideAim(); }
                _prevHeld = held;
                return;
            }

            // No ball: cancel any stale aim; grab (hold) + push (tap).
            if (_action == PlayerAction.Aiming) { _action = PlayerAction.None; HideAim(); }

            if (_action == PlayerAction.Grabbing) UpdateRestrain(now); // fire-and-forget grab

            if (held && !_actionConsumed && _action == PlayerAction.None && _grabTimer <= 0f)
            {
                if (now - _pressTime >= holdThreshold)
                {
                    var opp = FindNearestOpponent(out float gap);
                    if (opp != null && gap <= pushRadius) { BeginRestrain(opp); _actionConsumed = true; }
                }
            }

            if (up)
            {
                if (_action != PlayerAction.Grabbing && !_actionConsumed && (now - _pressTime) < holdThreshold)
                {
                    var opp = FindNearestOpponent(out float gap);
                    if (opp != null && gap <= pushRadius && _pushTimer <= 0f) Push(opp);
                }
                _actionConsumed = false;
            }

            _prevHeld = held;
        }

        void FixedUpdate()
        {
            // Kill any physics-induced yaw (ball/contacts) so the player never spins on its own.
            _rb.angularVelocity = Vector3.zero;

            if (State != PlayerState.Normal) { _velocity = Vector3.zero; _currentSpeed01 = 0f; return; }
            if (!IsControlled || !ControlsEnabled) { _velocity = Vector3.zero; _currentSpeed01 = 0f; return; }

            Vector2 mv = _input.GetMove();
            // Camera-relative movement. The camera yaw is user-controlled (orbit), NOT slaved
            // to facing, so this no longer creates a spin loop.
            Vector3 dir;
            Transform cam = ResolveCam();
            if (cam != null)
            {
                Vector3 f = cam.forward; f.y = 0f;
                Vector3 r = cam.right; r.y = 0f;
                if (f.sqrMagnitude > 1e-6f) f.Normalize();
                if (r.sqrMagnitude > 1e-6f) r.Normalize();
                dir = r * mv.x + f * mv.y;
            }
            else dir = new Vector3(mv.x, 0f, mv.y);

            float inMag = Mathf.Clamp01(dir.magnitude);
            Vector3 inDir = inMag > 1e-4f ? dir / Mathf.Max(dir.magnitude, 1e-4f) : Vector3.zero;

            float maxSpeed = moveSpeed * (_action == PlayerAction.Grabbing ? restrainMoveMultiplier : 1f);
            Vector3 targetVel = (inMag > moveDeadzone) ? inDir * maxSpeed * inMag : Vector3.zero;
            float rate = (inMag > moveDeadzone) ? acceleration : deceleration;
            _velocity = Vector3.MoveTowards(_velocity, targetVel, rate * Time.fixedDeltaTime);

            // Face the movement direction, smoothed.
            if (inMag > aimDeadzone)
            {
                _planarMoveDir = inDir;
                Quaternion target = Quaternion.LookRotation(inDir, Vector3.up);
                _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, target, turnSpeed * Time.fixedDeltaTime));
            }

            if (_velocity.sqrMagnitude > 1e-6f)
                _rb.MovePosition(_rb.position + _velocity * Time.fixedDeltaTime);

            _currentSpeed01 = Mathf.Clamp01(_velocity.magnitude / Mathf.Max(0.01f, moveSpeed));
        }

        // ---------- Kick (drag-to-aim) ----------
        // Aim direction/power from the action-button drag (camera-relative). If barely dragged,
        // aims where you face; power also gets a floor from your current running speed.
        void ComputeAim(out Vector3 dir, out float power)
        {
            Vector2 d = _input.GetAimDelta();
            float refPx = Mathf.Max(1f, Screen.height * aimRefScreenFrac);
            float deadPx = Screen.height * aimDeadScreenFrac;

            if (d.magnitude > deadPx)
            {
                Transform cam = ResolveCam();
                Vector3 f = cam != null ? cam.forward : Vector3.forward; f.y = 0f;
                Vector3 r = cam != null ? cam.right : Vector3.right; r.y = 0f;
                if (f.sqrMagnitude > 1e-6f) f.Normalize();
                if (r.sqrMagnitude > 1e-6f) r.Normalize();
                dir = r * d.x + f * d.y; dir.y = 0f;
                if (dir.sqrMagnitude > 1e-6f) dir.Normalize(); else dir = FlatForward();
                power = Mathf.Clamp01(d.magnitude / refPx);
            }
            else { dir = FlatForward(); power = 0f; }

            power = Mathf.Clamp(Mathf.Max(power, _currentSpeed01), 0.15f, 1f);
        }

        Vector3 FlatForward() { Vector3 f = transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; }

        void UpdateAim()
        {
            var ball = GameManager.Instance != null ? GameManager.Instance.Ball : null;
            if (ball == null) { HideAim(); return; }
            ComputeAim(out Vector3 dir, out float power);
            _aimDir = dir; _aimPower = power;   // cache: used at release (delta gets zeroed on touch-up)
            EnsureAimLine();
            Vector3 p0 = ball.transform.position; p0.y = 0.12f;
            Vector3 p1 = p0 + dir * Mathf.Lerp(1.5f, 6.5f, power);
            _aimLine.enabled = true;
            _aimLine.SetPosition(0, p0);
            _aimLine.SetPosition(1, p1);
            Color c = Color.Lerp(new Color(0.4f, 1f, 0.45f), new Color(1f, 0.4f, 0.2f), power);
            _aimLine.startColor = c; _aimLine.endColor = c;
        }

        void HideAim() { if (_aimLine != null) _aimLine.enabled = false; }

        void EnsureAimLine()
        {
            if (_aimLine != null) return;
            var go = new GameObject("AimLine");
            go.transform.SetParent(transform, false);
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

        void DoAimKick()
        {
            var ball = GameManager.Instance != null ? GameManager.Instance.Ball : null;
            if (ball == null || ball.Owner != this || _kickTimer > 0f) return;
            Vector3 dir = _aimDir; float power = _aimPower;   // use the cached aimed direction
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up)); // face the kick
            float force = Mathf.Lerp(kickMinForce, kickMaxForce, kickPowerCurve.Evaluate(power));
            Vector3 impulse = dir * force + Vector3.up * force * kickUpwardRatio;
            ball.Release(impulse);
            _kickTimer = kickCooldown;
            if (SfxManager.Instance != null) SfxManager.Instance.PlayKick();
        }

        // ---------- Push ----------
        PlayerController FindNearestOpponent(out float gap)
        {
            gap = Mathf.Infinity;
            PlayerController best = null;
            int n = Physics.OverlapSphereNonAlloc(transform.position, pushRadius + 2f * PLAYER_RADIUS + 0.5f, _overlap);
            for (int i = 0; i < n; i++)
            {
                var pc = _overlap[i].GetComponentInParent<PlayerController>();
                if (pc == null || pc == this) continue;
                if (pc.team == null || team == null || pc.team.team == team.team) continue; // no friendly fire
                if (pc.State != PlayerState.Normal) continue;                                 // invulnerable when down/held
                float g = Vector3.Distance(transform.position, pc.transform.position) - 2f * PLAYER_RADIUS;
                if (g < gap) { gap = g; best = pc; }
            }
            return best;
        }

        void Push(PlayerController target)
        {
            // Uniform small backward recoil + 1s stun (with or without ball).
            Vector3 dir = target.transform.position - transform.position; dir.y = 0f; dir.Normalize();

            var ball = GameManager.Instance != null ? GameManager.Instance.Ball : null;
            if (ball != null && ball.Owner == target) ball.LoseToTackle();

            target.ReceivePush(dir * pushForce, true);
            _pushTimer = pushCooldown;
            if (SfxManager.Instance != null) SfxManager.Instance.PlayImpact();
        }

        public void ReceivePush(Vector3 impulse, bool causesStun)
        {
            if (State != PlayerState.Normal) return; // invulnerable while down/held
            _rb.AddForce(impulse, ForceMode.Impulse);
            FlashImpact();
            if (causesStun) EnterStumble();
        }

        // "Stumbled" here = stunned: brief inert + invulnerable window with a tilt placeholder.
        void EnterStumble()
        {
            CancelAction();                    // drop any grab/aim if we get stunned mid-action
            State = PlayerState.Stumbled;
            _stumbleTimer = stunDuration;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            transform.rotation = Quaternion.Euler(70f, transform.eulerAngles.y, 0f);
        }

        void Recover()
        {
            State = PlayerState.Normal;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // ---------- Restrain (as holder) ----------
        void BeginRestrain(PlayerController target)
        {
            var ball = GameManager.Instance != null ? GameManager.Instance.Ball : null;
            if (ball != null && ball.Owner == target) ball.LoseToTackle();
            _action = PlayerAction.Grabbing;
            _restrainStart = Time.time;
            _restrainTarget = target;
            target.SetRestrained();
        }

        void UpdateRestrain(float now)
        {
            if (_restrainTarget == null) { EndRestrain(); return; }
            float dur = now - _restrainStart;
            float gap = Vector3.Distance(transform.position, _restrainTarget.transform.position) - 2f * PLAYER_RADIUS;
            // Fire-and-forget: ends ONLY on its timer, if the target got too far, or invalid.
            if (dur >= restrainMaxDuration || gap > pushRadius + 0.8f
                || _restrainTarget.State != PlayerState.Restrained)
            {
                EndRestrain();
                return;
            }
            // anchor target in front of the holder at a fixed distance
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 anchor = transform.position + fwd * restrainDistance;
            anchor.y = _restrainTarget.transform.position.y;
            _restrainTarget.AnchorRestrained(anchor);
        }

        void EndRestrain()
        {
            if (_restrainTarget != null) _restrainTarget.ReleaseRestrained();
            _restrainTarget = null;
            _action = PlayerAction.None;
            _grabTimer = grabCooldown;   // must wait before grabbing again
        }

        // ---------- Restrain (as target) ----------
        public void SetRestrained()
        {
            State = PlayerState.Restrained;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        public void AnchorRestrained(Vector3 pos) { if (State == PlayerState.Restrained) _rb.MovePosition(pos); }
        public void ReleaseRestrained() { if (State == PlayerState.Restrained) State = PlayerState.Normal; }

        // ---------- reset ----------
        public void ForceRecover()
        {
            CancelInvoke();
            _stumbleTimer = 0f; _kickTimer = 0f; _pushTimer = 0f;
            CancelAction();
            HideAim();
            if (State == PlayerState.Stumbled) Recover();
            else if (State == PlayerState.Restrained) { State = PlayerState.Normal; }
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
    }
}
