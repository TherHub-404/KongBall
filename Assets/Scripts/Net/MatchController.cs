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
        // Three minutes, or the first team to three goals — whichever comes first.
        //
        // The prefab carries these values and overrides the defaults here, so both are kept in step:
        // `endless` was not in the prefab at all (the field was added to the script after the prefab
        // was last saved), which meant the code default alone decided it. It is written explicitly
        // now, so there is no question about which of the two wins.
        public bool endless = false;        // true = no timer and no win, for playtesting alone
        public float matchDuration = 180f;
        public int scoreLimit = 3;
        public float countdownDuration = 3f;
        public float goalPauseDuration = 2f;

        [Tooltip("How long a team must have nobody left before the match is awarded to the other one. " +
                 "A moment of tolerance, so a replication hiccup cannot end a match.")]
        public float abandonGrace = 1.5f;

        [Networked] public int ScoreBlue { get; set; }
        [Networked] public int ScoreRed { get; set; }
        [Networked] public int PhaseId { get; set; }       // Phase
        [Networked] public float PhaseTimer { get; set; }  // countdown / goal-pause remaining
        [Networked] public float MatchTime { get; set; }   // match time remaining
        [Networked] public int Winner { get; set; }        // -1 none, 0 blue, 1 red, 2 draw
        [Networked] public int KickoffSeq { get; set; }    // bumps each kickoff -> players reset
        [Networked] public int Seats { get; set; }         // players this match waits for, bots included
        [Networked] public bool ByForfeit { get; set; }    // the win was awarded, not played out
        [Networked] public bool WithBots { get; set; }     // this match was SET UP with bots on the pitch
        [Networked] public int HumanSeats { get; set; }    // of those seats, how many a person must fill

        public static MatchController Instance;
        public Phase CurPhase => (Phase)PhaseId;
        public bool CanScore => (Phase)PhaseId == Phase.Playing;
        public int Seated => CountPlayers();

        // Teams the master has handed out but that have not come back over the network yet. Without
        // this, two players joining in the same breath would both be told the emptier side is theirs.
        readonly Dictionary<PlayerRef, int> _pending = new Dictionary<PlayerRef, int>();
        readonly HashSet<PlayerRef> _live = new HashSet<PlayerRef>();
        readonly List<PlayerRef> _stale = new List<PlayerRef>();
        float _assignCooldown;
        int _emptyTeam = -1;
        float _emptyFor;

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
                // Bodies, not humans: a practice match holds one human and two players.
                Seats = NetLauncher.Instance != null ? NetLauncher.Instance.RequiredBodies : 2;
                HumanSeats = NetLauncher.Instance != null ? NetLauncher.Instance.RequiredPlayers : 2;
                WithBots = NetLauncher.Instance != null && NetLauncher.Instance.WithBots;
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
                    if (TeamAbandoned()) break;
                    PhaseTimer -= dt;
                    if (PhaseTimer <= 0f) PhaseId = (int)Phase.Playing;
                    break;
                case Phase.Playing:
                    if (TeamAbandoned()) break;
                    if (!endless)
                    {
                        MatchTime -= dt;
                        if (MatchTime <= 0f) { MatchTime = 0f; Finish(); }
                    }
                    break;
                case Phase.GoalPause:
                    if (TeamAbandoned()) break;
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
        //
        // One list and one rule for humans and bots, rather than a pass over Runner.ActivePlayers and
        // a special case for the rest. For a human this asks exactly what it asked before — a player
        // object that exists and carries a team — because that is what the old two-step amounted to.
        bool Ready()
        {
            if (CountPlayers() < (Seats > 0 ? Seats : 2)) return false;
            foreach (var np in NetPlayer.Live)
                if (np == null || !np.TeamAssigned) return false;
            return true;
        }

        // Players on the pitch, not peers in the room: a bot is neither in ActivePlayers nor able to
        // join, and a match that waits for connections would never start one that includes it.
        int CountPlayers()
        {
            int n = 0;
            foreach (var np in NetPlayer.Live) if (np != null) n++;
            return n;
        }

        // The master hands out sides. A client cannot do this for itself: with matchmaking, two
        // clients deciding at the same moment would both see an empty pitch and both pick blue.
        void AssignTeams()
        {
            _assignCooldown -= Runner.DeltaTime;
            if (_assignCooldown > 0f) return;
            _assignCooldown = 0.2f;

            // Anyone who left before acknowledging would otherwise stay in _pending for good — and
            // Photon reuses player slots, so the next arrival on that slot would look already handled
            // and never be given a side, leaving the room waiting for a team that cannot complete.
            if (_pending.Count > 0)
            {
                _live.Clear();
                foreach (var p in Runner.ActivePlayers) _live.Add(p);
                _stale.Clear();
                foreach (var kv in _pending)
                {
                    var np = PlayerOf(kv.Key);
                    if (!_live.Contains(kv.Key) || (np != null && np.TeamAssigned)) _stale.Add(kv.Key);
                }
                foreach (var p in _stale) _pending.Remove(p);
            }

            int blue = 0, red = 0;
            foreach (var p in Runner.ActivePlayers)
            {
                var np = PlayerOf(p);
                if (np == null) continue;
                if (np.TeamAssigned)
                {
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

        // --- forfeit -----------------------------------------------------------------------------

        // One rule covers both modes: a side is out when it has nobody left. In 1v1 that is one
        // player leaving, in 2v2 it takes both — which is exactly the behaviour asked for, without a
        // special case per mode, and it still holds if a 3v3 ever exists.
        //
        // It counts who is STILL HERE rather than who pressed the forfeit button, so closing the app,
        // crashing and losing the line all end the match the same way. That matters more than the
        // polite forfeit: leaving the opponent alone in a match that can never end is the failure
        // this is here to prevent.
        //
        // Returns true when the match has just been decided, so the caller stops running the phase.
        bool TeamAbandoned()
        {
            // Over the live player list rather than Runner.ActivePlayers, which only knows about
            // peers with a connection. Humans and bodies are counted separately because the rule
            // differs, and it differs for one reason:
            //
            //   normally  — a side is out when nobody is left on it who CHOSE to be there. Counting
            //               bots would mean a match whose only human walked out keeps playing itself.
            //   with bots — a side is out only when it is completely EMPTY. In a practice match the
            //               bot's side has no human by design, and the rule above would award the
            //               match to the player on the first whistle. There is no risk of a match
            //               playing itself here: it lives on the peer of the person who started it.
            int blueH = 0, redH = 0, blueAny = 0, redAny = 0;
            foreach (var np in NetPlayer.Live)
            {
                if (np == null || !np.TeamAssigned) continue;
                bool red = np.NetTeam == (int)Team.Red;
                if (red) redAny++; else blueAny++;
                if (np.IsBot) continue;
                if (red) redH++; else blueH++;
            }

            int gone = WithBots
                ? (blueAny == 0 ? (int)Team.Blue : (redAny == 0 ? (int)Team.Red : -1))
                : (blueH == 0 ? (int)Team.Blue : (redH == 0 ? (int)Team.Red : -1));
            if (gone < 0) { _emptyTeam = -1; _emptyFor = 0f; return false; }

            if (_emptyTeam != gone) { _emptyTeam = gone; _emptyFor = 0f; }
            _emptyFor += Runner.DeltaTime;
            if (_emptyFor < abandonGrace) return false;

            PhaseId = (int)Phase.Finished;
            Winner = gone == (int)Team.Blue ? (int)Team.Red : (int)Team.Blue;
            ByForfeit = true;
            Debug.Log("[Net] team " + (Team)gone + " abandoned, match awarded to " + (Team)Winner);
            return true;
        }
    }
}
