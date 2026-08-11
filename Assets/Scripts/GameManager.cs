using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CalcioStumble
{
    // Singleton match flow: score, timer, state, kickoff resets, random controlled
    // selection, and an out-of-bounds safety net that restores anything that escapes.
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Refs")]
        public BallController Ball;
        public List<PlayerController> players = new List<PlayerController>();
        public LocalInputSource localInput;
        public MatchCamera matchCamera;
        public MatchUI ui;

        [Header("Tuning (single source of truth)")]
        public GameTuning tuning;
        [Tooltip("Re-apply tuning every frame so you can tweak the asset live during playtests.")]
        public bool liveTuning = true;

        [Header("Rules")]
        public int goalsToWin = 3;
        public float matchDuration = 180f;
        public float goalPauseDuration = 1.5f;
        [Tooltip("Which player index is controlled at kickoff (fixed, not random).")]
        public int startControlledIndex = 0;

        [Header("Field safety (out-of-bounds recovery)")]
        public float fieldHalfX = 17.5f;
        public float fieldHalfZ = 10f;
        public float minY = -2f;

        public PlayerController Controlled => _controlled;
        public MatchState State { get; private set; } = MatchState.Playing;
        public bool ControlsEnabled { get; private set; } = true;
        public int ScoreBlue { get; private set; }
        public int ScoreRed { get; private set; }
        public float TimeLeft { get; private set; }

        readonly Dictionary<PlayerController, Vector3> _startPos = new Dictionary<PlayerController, Vector3>();
        readonly Dictionary<PlayerController, Quaternion> _startRot = new Dictionary<PlayerController, Quaternion>();
        readonly Dictionary<Transform, Vector3> _lastValid = new Dictionary<Transform, Vector3>();
        PlayerController _controlled;

        void Awake() { Instance = this; }

        void Start()
        {
            CacheStartTransforms();
            ApplyTuning();
            if (Ball != null) Ball.SetStartPosition(new Vector3(0f, Ball.Radius, 0f)); // always centre
            // Title screen first; the match begins on tap (BeginMatch).
            State = MatchState.Title;
            ControlsEnabled = false;
            AssignRandomControlled(); // gives the camera a target to frame under the title
            Kickoff();
            if (ui != null)
            {
                ui.UpdateScore(0, 0);
                ui.UpdateTimer(matchDuration);
                ui.ShowTitle();
            }
        }

        // Called by the title screen tap.
        public void BeginMatch()
        {
            if (State != MatchState.Title) return;
            if (ui != null) ui.HideTitle();
            StartMatch();
        }

        // Push the single tuning source into every player and the ball.
        public void ApplyTuning()
        {
            if (tuning == null) return;
            foreach (var p in players)
            {
                if (p == null) continue;
                p.moveSpeed = tuning.moveSpeed;
                p.acceleration = tuning.acceleration;
                p.deceleration = tuning.deceleration;
                p.turnSpeed = tuning.turnSpeed;
                p.kickMinForce = tuning.kickMinForce;
                p.kickMaxForce = tuning.kickMaxForce;
                p.kickUpwardRatio = tuning.kickUpwardRatio;
                p.kickCooldown = tuning.kickCooldown;
                p.pushRadius = tuning.pushRadius;
                p.pushForce = tuning.pushForce;
                p.stunDuration = tuning.stunDuration;
                p.pushCooldown = tuning.pushCooldown;
                p.restrainMaxDuration = tuning.restrainMaxDuration;
                p.restrainDistance = tuning.restrainDistance;
                p.restrainMoveMultiplier = tuning.restrainMoveMultiplier;
                p.grabCooldown = tuning.grabCooldown;
                p.holdThreshold = tuning.holdThreshold;
                p.possessionRadius = tuning.possessionRadius;
            }
            if (Ball != null)
            {
                Ball.dribbleAhead = tuning.dribbleAhead;
                Ball.dribbleSmoothTime = tuning.dribbleSmoothTime;
                Ball.catchUpGain = tuning.catchUpGain;
                Ball.maxDribbleSpeed = tuning.maxDribbleSpeed;
                Ball.velocityEase = tuning.velocityEase;
                Ball.loseDistance = tuning.loseDistance;
                Ball.maxBallSpeed = tuning.maxBallSpeed;
            }
        }

        void CacheStartTransforms()
        {
            _startPos.Clear(); _startRot.Clear();
            foreach (var p in players)
            {
                if (p == null) continue;
                _startPos[p] = p.transform.position;
                _startRot[p] = p.transform.rotation;
            }
        }

        public void StartMatch()
        {
            ScoreBlue = 0; ScoreRed = 0;
            TimeLeft = matchDuration;
            State = MatchState.Playing;
            AssignRandomControlled();
            Kickoff();
            ControlsEnabled = true;
            if (ui != null)
            {
                ui.HideEndPanel();
                ui.HideGoalBanner();
                ui.UpdateScore(ScoreBlue, ScoreRed);
                ui.UpdateTimer(TimeLeft);
            }
        }

        void AssignRandomControlled()
        {
            foreach (var p in players)
            {
                if (p == null) continue;
                if (p.team != null) p.team.SetControlled(false);
                p.AssignInput(null);
            }
            if (players.Count == 0) return;
            int idx = Mathf.Clamp(startControlledIndex, 0, players.Count - 1); // always the same player
            _controlled = players[idx];
            if (_controlled.team != null) _controlled.team.SetControlled(true);
            _controlled.AssignInput(localInput);
            // Orbit cam, initial yaw toward the enemy goal: Blue attacks +x, Red -x.
            Vector3 attackDir = (_controlled.team != null && _controlled.team.team == Team.Red)
                ? Vector3.left : Vector3.right;
            if (matchCamera != null) matchCamera.SetTarget(_controlled.transform, attackDir);
        }

        // Reset everyone to kickoff formation, ball free at centre.
        void Kickoff()
        {
            foreach (var p in players)
            {
                if (p == null) continue;
                p.ForceRecover();
                _startPos.TryGetValue(p, out var pos);
                _startRot.TryGetValue(p, out var rot);
                var rb = p.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // Set rb.position (transform.position alone is reverted by interpolation).
                    rb.position = pos; rb.rotation = rot;
                    rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                }
                p.transform.SetPositionAndRotation(pos, rot);
            }
            if (Ball != null) Ball.ResetBall();
            _lastValid.Clear();
        }

        public void OnGoal(Team scorer)
        {
            if (State != MatchState.Playing) return;
            if (scorer == Team.Blue) ScoreBlue++; else ScoreRed++;
            if (ui != null) ui.UpdateScore(ScoreBlue, ScoreRed);

            if (ScoreBlue >= goalsToWin || ScoreRed >= goalsToWin) { EndMatch(); return; }

            State = MatchState.GoalPause;
            ControlsEnabled = false;
            StartCoroutine(GoalSequence());
        }

        // GOAL! celebration -> reset to kickoff -> 3..2..1 countdown -> resume.
        IEnumerator GoalSequence()
        {
            if (SfxManager.Instance != null) SfxManager.Instance.PlayGoal();
            if (ui != null) ui.ShowGoalBanner("GOAL!");
            yield return new WaitForSeconds(goalPauseDuration);

            Kickoff();                                   // ball to centre + formation

            for (int n = 3; n >= 1; n--)
            {
                if (ui != null) ui.ShowGoalBanner(n.ToString());
                yield return new WaitForSeconds(1f);
            }

            if (ui != null) ui.HideGoalBanner();
            State = MatchState.Playing;
            ControlsEnabled = true;
        }

        void Update()
        {
            if (liveTuning) ApplyTuning();

            if (State == MatchState.Playing)
            {
                TimeLeft -= Time.deltaTime;
                if (ui != null) ui.UpdateTimer(TimeLeft);
                if (TimeLeft <= 0f) { TimeLeft = 0f; EndMatch(); }
            }
            // GoalPause is driven by the GoalSequence coroutine.
        }

        void FixedUpdate()
        {
            SafetyCheck();
        }

        // Restore anything that escapes the field. The BALL always returns to CENTRE
        // (a ball off the pitch restarts from the middle); players return to last valid spot.
        void SafetyCheck()
        {
            if (Ball != null)
            {
                Vector3 bp = Ball.transform.position;
                bool ballOut = bp.y < minY || Mathf.Abs(bp.x) > fieldHalfX || Mathf.Abs(bp.z) > fieldHalfZ;
                if (ballOut) Ball.ResetBallTo(new Vector3(0f, Ball.Radius, 0f));
            }
            foreach (var p in players)
            {
                if (p == null) continue;
                CheckBounds(p.transform, p.GetComponent<Rigidbody>());
            }
        }

        void CheckBounds(Transform t, Rigidbody rb)
        {
            Vector3 pos = t.position;
            bool outOfBounds = pos.y < minY || Mathf.Abs(pos.x) > fieldHalfX || Mathf.Abs(pos.z) > fieldHalfZ;
            if (outOfBounds)
            {
                if (_lastValid.TryGetValue(t, out var v))
                {
                    t.position = v;
                    if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                }
            }
            else
            {
                _lastValid[t] = pos;
            }
        }

        void EndMatch()
        {
            State = MatchState.Finished;
            ControlsEnabled = false;
            if (ui != null) ui.HideGoalBanner();
            string msg;
            if (ScoreBlue > ScoreRed) msg = "Vittoria Squadra Blu";
            else if (ScoreRed > ScoreBlue) msg = "Vittoria Squadra Rossa";
            else msg = "Pareggio";
            if (ui != null) ui.ShowEndPanel(msg);
        }

        public void Replay() { StartMatch(); }
    }
}
