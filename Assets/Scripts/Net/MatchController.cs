using Fusion;
using UnityEngine;

namespace KongBall
{
    // Networked, MASTER-AUTHORITATIVE match flow. Owns score, timer, phase and kickoff.
    // Players and the ball read the networked phase and react (freeze during countdown,
    // self-reset on a new kickoff). Presentation (NetScoreUI) only reads this.
    public class MatchController : NetworkBehaviour
    {
        public enum Phase { Countdown, Playing, GoalPause, Finished }

        [Header("Rules")]
        public bool endless = true;         // infinite match for playtesting (no timer / no win)
        public float matchDuration = 120f;
        public int scoreLimit = 5;
        public float countdownDuration = 3f;
        public float goalPauseDuration = 2f;

        [Networked] public int ScoreBlue { get; set; }
        [Networked] public int ScoreRed { get; set; }
        [Networked] public int PhaseId { get; set; }       // Phase
        [Networked] public float PhaseTimer { get; set; }  // countdown / goal-pause remaining
        [Networked] public float MatchTime { get; set; }   // match time remaining
        [Networked] public int Winner { get; set; }        // -1 none, 0 blue, 1 red, 2 draw
        [Networked] public int KickoffSeq { get; set; }    // bumps each kickoff -> players reset

        public static MatchController Instance;
        public Phase CurPhase => (Phase)PhaseId;
        public bool CanScore => (Phase)PhaseId == Phase.Playing;

        // Called by the ball's authority (coordinate goal detection) -> runs on the master.
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Goal(int team) { RegisterGoal(team); }

        public override void Spawned()
        {
            // Same duplicate-on-master-handover case as NetBall: two controllers would mean two
            // scores and two timers. Lowest NetworkId survives, decided identically on every client.
            if (Instance != null && Instance != this)
            {
                bool iAmOlder = Object.Id.Raw <= Instance.Object.Id.Raw;
                var loser = iAmOlder ? Instance : this;
                Instance = iAmOlder ? this : Instance;

                Debug.LogWarning("[Net] duplicate MatchController detected, dropping " + loser.Object.Id);
                if (loser.Object != null && loser.Object.HasStateAuthority)
                    Runner.Despawn(loser.Object);

                if (loser == this) return;
            }
            else Instance = this;

            if (HasStateAuthority)
            {
                ScoreBlue = 0; ScoreRed = 0; Winner = -1;
                MatchTime = matchDuration;
                StartCountdown();
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        void StartCountdown()
        {
            PhaseId = (int)Phase.Countdown;
            PhaseTimer = countdownDuration;
            KickoffSeq++;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            float dt = Runner.DeltaTime;
            switch ((Phase)PhaseId)
            {
                case Phase.Countdown:
                    PhaseTimer -= dt;
                    if (PhaseTimer <= 0f) PhaseId = (int)Phase.Playing;
                    break;
                case Phase.Playing:
                    if (!endless)
                    {
                        MatchTime -= dt;
                        if (MatchTime <= 0f) { MatchTime = 0f; Finish(); }
                    }
                    break;
                case Phase.GoalPause:
                    PhaseTimer -= dt;
                    if (PhaseTimer <= 0f) Kickoff();
                    break;
                case Phase.Finished:
                    break;
            }
        }

        // Called by the ball's authority when the ball crosses a goal line.
        public void RegisterGoal(int scoringTeam)
        {
            if (!HasStateAuthority) return;
            if ((Phase)PhaseId != Phase.Playing) return; // goal lock outside PLAYING
            if (scoringTeam == 0) ScoreBlue++; else ScoreRed++;
            // The ball recentres itself the moment it detects the goal (NetBall.ScoreGoal), so it
            // cannot re-trigger while we switch phase — nothing to reset from here.

            if (!endless && (ScoreBlue >= scoreLimit || ScoreRed >= scoreLimit)) { Finish(); return; }
            PhaseId = (int)Phase.GoalPause;
            PhaseTimer = goalPauseDuration;
        }

        void Kickoff()
        {
            if (NetBall.Instance != null) NetBall.Instance.KickoffReset();
            StartCountdown();
        }

        void Finish()
        {
            PhaseId = (int)Phase.Finished;
            Winner = ScoreBlue > ScoreRed ? 0 : (ScoreRed > ScoreBlue ? 1 : 2);
        }
    }
}
