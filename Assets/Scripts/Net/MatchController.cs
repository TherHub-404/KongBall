using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace KongBall
{
    // Networked, MASTER-AUTHORITATIVE match flow. Owns score, timer, phase, kickoff and — since
    // matchmaking replaced the blue/red buttons — which side each player is on.
    // Players and the ball read the networked phase and react (freeze during countdown,
    // self-reset on a new kickoff). Presentation (NetScoreUI) only reads this.
    public class MatchController : NetworkBehaviour
    {
        public enum Phase { Waiting, Countdown, Playing, GoalPause, Finished }

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
        [Networked] public int Seats { get; set; }         // players this match waits for (2 or 4)

        public static MatchController Instance;
        public Phase CurPhase => (Phase)PhaseId;
        public bool CanScore => (Phase)PhaseId == Phase.Playing;
        public int Seated => CountPlayers();

        // Teams the master has handed out but that have not come back over the network yet. Without
        // this, two players joining in the same breath would both be told the emptier side is theirs.
        readonly Dictionary<PlayerRef, int> _pending = new Dictionary<PlayerRef, int>();
        float _assignCooldown;

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
                // Published so every client can show "2/4" without knowing how the match was started.
                Seats = NetLauncher.Instance != null ? NetLauncher.Instance.RequiredPlayers : 2;
                PhaseId = (int)Phase.Waiting;
                PhaseTimer = 0f;
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
                case Phase.Waiting:
                    AssignTeams();
                    if (Ready()) { CloseSession(); StartCountdown(); }
                    break;
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

        // --- waiting room ------------------------------------------------------------------------

        // Everyone present AND everyone knowing which side they are on. Starting on the count alone
        // would kick off with a player still standing on the wrong half.
        bool Ready()
        {
            int seats = Seats > 0 ? Seats : 2;
            int n = 0;
            foreach (var p in Runner.ActivePlayers)
            {
                var np = PlayerOf(p);
                if (np == null || !np.TeamAssigned) return false;
                n++;
            }
            return n >= seats;
        }

        int CountPlayers()
        {
            if (Runner == null) return 0;
            int n = 0;
            foreach (var p in Runner.ActivePlayers) n++;
            return n;
        }

        // The master hands out sides. A client cannot do this for itself: with matchmaking, two
        // clients deciding at the same moment would both see an empty pitch and both pick blue.
        void AssignTeams()
        {
            _assignCooldown -= Runner.DeltaTime;
            if (_assignCooldown > 0f) return;
            _assignCooldown = 0.2f;

            int blue = 0, red = 0;
            foreach (var p in Runner.ActivePlayers)
            {
                var np = PlayerOf(p);
                if (np == null) continue;
                if (np.TeamAssigned)
                {
                    _pending.Remove(p);
                    if (np.NetTeam == (int)Team.Red) red++; else blue++;
                }
                else if (_pending.TryGetValue(p, out int t))
                {
                    if (t == (int)Team.Red) red++; else blue++;
                }
            }

            foreach (var p in Runner.ActivePlayers)
            {
                var np = PlayerOf(p);
                if (np == null || np.TeamAssigned || _pending.ContainsKey(p)) continue;
                int team = blue <= red ? (int)Team.Blue : (int)Team.Red;
                if (team == (int)Team.Red) red++; else blue++;
                _pending[p] = team;
                RPC_AssignTeam(p, team);
                Debug.Log("[Net] assigned player " + p.PlayerId + " to " + (Team)team);
            }
        }

        // Broadcast rather than targeted: only the player it names acts on it, and this keeps the
        // call to the same shape the rest of the codebase already uses.
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_AssignTeam(PlayerRef who, int team)
        {
            if (Runner == null || who != Runner.LocalPlayer) return;
            var np = PlayerOf(who);
            if (np != null) np.ApplyTeam(team);
        }

        NetPlayer PlayerOf(PlayerRef p)
        {
            if (Runner == null || !Runner.TryGetPlayerObject(p, out var no) || no == null) return null;
            return no.GetComponent<NetPlayer>();
        }

        // Once the whistle goes, nobody else walks in. Photon excludes closed and invisible rooms
        // from random matchmaking, so this is also what stops a fresh player being matched into a
        // match already in progress.
        void CloseSession()
        {
            var si = Runner != null ? Runner.SessionInfo : null;
            if (si == null || !si.IsValid) return;
            si.IsOpen = false;
            si.IsVisible = false;
            Debug.Log("[Net] session closed for joins");
        }

        // --- scoring -----------------------------------------------------------------------------

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
