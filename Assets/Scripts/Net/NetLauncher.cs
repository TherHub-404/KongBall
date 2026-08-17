using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace KongBall
{
    // Bootstrap: starts a Fusion NetworkRunner in Shared Mode and spawns one player per client.
    // Each client owns (StateAuthority) its own player; NetPlayer moves it and NetworkTransform
    // replicates to everyone else.
    //
    // Two ways into a match, and they are the SAME Fusion call with different arguments:
    //
    //   quick match   SessionName = null  -> Fusion documents this as "random session matching", and
    //                                        it lands on JoinRandomOrCreateRoom: the server puts you
    //                                        in a matching room or makes one. IsVisible = true.
    //   private room  SessionName = code  -> IsVisible = false, which Photon excludes from random
    //                                        matchmaking ("simulate private rooms"), so a code is
    //                                        the only way in.
    //
    // The mode travels as a session property, which doubles as the matchmaking filter — filters are
    // exact-match, so a 1v1 player can never be dropped into a 2v2 room. PlayerCount carries the
    // same number, so the room's size and the filter cannot disagree.
    public class NetLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Prefab (must have NetworkObject + NetworkTransform + NetPlayer)")]
        public NetworkObject playerPrefab;

        [Header("Ball prefab (NetworkObject + NetworkTransform + NetBall)")]
        public NetworkObject ballPrefab;

        [Header("MatchController prefab (NetworkObject + MatchController)")]
        public NetworkObject matchPrefab;

        [Header("Session")]
        public bool autoStart = false;
        public MatchMode autoStartMode = MatchMode.OneVsOne;

        [Tooltip("Seconds on the result screen before returning to the menu.")]
        public float postMatchSeconds = 6f;

        [Tooltip("How long to wait for the room to fill before giving up and going back to the menu. " +
                 "Restarts whenever somebody joins, so a room that is filling up is never abandoned.")]
        public float waitTimeoutSeconds = 120f;

        [Tooltip("Grace period after joining before the master may spawn missing shared objects, so " +
                 "objects already in the room have time to replicate first.")]
        public float settleSeconds = 3f;

        [Header("Legacy scene UI — the blue/red panel, now replaced by MainMenu")]
        public GameObject teamPanel;

        // Bumped whenever the netcode changes in a way that makes old and new clients incompatible.
        // Travels as a matchmaking filter, so mismatched builds simply never meet instead of meeting
        // and misbehaving. This is NOT the build number: that would split the population every build.
        public const int ProtocolVersion = 1;

        const string ModeKey = "m";
        const string ProtocolKey = "v";

        public static NetLauncher Instance { get; private set; }

        public MatchMode Mode { get; private set; } = MatchMode.OneVsOne;
        public int RequiredPlayers => (int)Mode;
        public string RoomCode { get; private set; }      // null for a quick match

        NetworkRunner _runner;
        bool _started;
        float _ensureCooldown;
        float _settleUntil;
        float _leaveAt;
        float _waitUntil;
        int _lastSeated = -1;
        string _notice;               // shown once we are back on the menu, instead of silently landing there
        ConnectingScreen _screen;
        GameObject _abandon;

        // Seconds left before giving up on the waiting room, or -1 when not waiting. Local on
        // purpose: it measures how long THIS player has been waiting, which is what runs out of
        // patience — not how long the room has existed.
        public float WaitRemaining => _waitUntil > 0f ? Mathf.Max(0f, _waitUntil - Time.time) : -1f;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // The old team-select panel still lives in the scene. It cannot be deleted from outside
            // the Editor, so it is switched off here instead — and its buttons are unreachable.
            if (teamPanel != null) teamPanel.SetActive(false);

            if (autoStart) StartQuickMatch(autoStartMode);
            else MainMenu.Show();
        }

        // --- the three ways in -------------------------------------------------------------------

        public void StartQuickMatch(MatchMode mode)
        {
            Begin(mode, null, true, true, "CERCO UNA PARTITA");
        }

        public void CreatePrivateMatch(MatchMode mode)
        {
            string code = MainMenu.NewCode();
            Begin(mode, code, false, true, "STANZA " + code);
        }

        // The mode comes from whoever created the room, so joining does not choose one. The filter
        // still carries the protocol version: an incompatible friend fails to join rather than
        // joining a match that then misbehaves.
        public void JoinPrivateMatch(string code)
        {
            Begin(Mode, MainMenu.Normalise(code), false, false, "ENTRO NELLA STANZA " + code);
        }

        void Begin(MatchMode mode, string code, bool visible, bool mayCreate, string message)
        {
            if (_started) return;
            _started = true;
            Mode = mode;
            RoomCode = code;
            _leaveAt = 0f;
            _screen = ConnectingScreen.Show(message);
            _ = StartShared(visible, mayCreate);
        }

        public async System.Threading.Tasks.Task StartShared(bool visible, bool mayCreate)
        {
            if (_runner != null) return;

            // The runner lives on its own GameObject so that shutting it down cannot take this
            // launcher with it — we need to survive a match to get back to the menu.
            var host = new GameObject("NetworkRunner");
            _runner = host.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            var props = new Dictionary<string, SessionProperty>
            {
                { ModeKey, (int)Mode },
                { ProtocolKey, ProtocolVersion },
            };

            // MatchmakingMode is left at its default, FillRoom, which the SDK documents as making
            // "most sense with MaxPlayers > 0 and games that can only start with more players" —
            // exactly this game: it packs players into the oldest room instead of scattering them
            // one per room, which is what would leave everybody waiting alone.
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = RoomCode,
                PlayerCount = (int)Mode,
                SessionProperties = props,
                IsVisible = visible,
                IsOpen = true,
                EnableClientSessionCreation = mayCreate,
            });

            if (result.Ok)
            {
                Debug.Log("[Net] joined session '" + (_runner.SessionInfo != null ? _runner.SessionInfo.Name : "?")
                          + "' mode=" + Mode + " visible=" + visible);
                if (_screen != null) _screen.SetMessage(RoomCode != null ? "STANZA " + RoomCode : "ENTRO IN PARTITA");
                return;
            }

            // Without this the player was stuck for good: _started stayed true, so there was no way
            // back to the menu.
            Debug.LogWarning("[Net] StartGame FAILED: " + result.ShutdownReason);
            TearDownRunner();
            _started = false;
            if (_screen != null) _screen.ShowError(FailureText(result.ShutdownReason), MainMenu.Show);
            _screen = null;   // the error screen owns itself until the player taps through
        }

        // Joining by code has three failures a player can actually act on, and "CONNESSIONE FALLITA"
        // tells them none of them apart.
        string FailureText(ShutdownReason reason)
        {
            if (RoomCode != null)
            {
                if (reason == ShutdownReason.GameNotFound)
                    return "NESSUNA STANZA CON IL CODICE\n" + RoomCode;
                if (reason == ShutdownReason.GameClosed)
                    return "LA PARTITA E' GIA' INIZIATA";
                if (reason == ShutdownReason.GameIsFull)
                    return "LA STANZA E' PIENA";
            }
            return "CONNESSIONE FALLITA\n" + reason;
        }

        public void LeaveMatch()
        {
            if (_runner == null) { MainMenu.Show(); return; }
            _runner.Shutdown();   // OnShutdown puts us back on the menu
        }

        void TearDownRunner()
        {
            if (_abandon != null) { Destroy(_abandon); _abandon = null; }
            if (_runner == null) return;
            if (_runner.gameObject != null) Destroy(_runner.gameObject);
            _runner = null;
            _settleUntil = 0f;
        }

        // --- spawning ----------------------------------------------------------------------------

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Shared Mode: each client spawns only its own player object.
            if (player == runner.LocalPlayer) SpawnLocalPlayer(runner, player);
            EnsureMatchObjects(runner);
        }

        // No team is chosen here any more. Six players all picking blue is the reason: with
        // matchmaking the sides have to be balanced by the room, so MatchController assigns a team
        // and the player re-seats itself when it arrives.
        void SpawnLocalPlayer(NetworkRunner runner, PlayerRef player)
        {
            float z = ((player.PlayerId % 3) - 1) * 3f;
            Vector3 pos = new Vector3(-6f, NetPlayer.SpawnHeight, z);
            Quaternion rot = Quaternion.LookRotation(Vector3.right, Vector3.up);

            var no = runner.Spawn(playerPrefab, pos, rot, player);
            if (no != null) runner.SetPlayerObject(player, no); // the ball looks players up by PlayerRef
            Debug.Log("[Net] spawned local player, awaiting team assignment");
        }

        // The master owns the shared objects. This used to live inside the local-player branch of
        // OnPlayerJoined, so if the original master left the session nobody ever recreated them.
        void EnsureMatchObjects(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning || !runner.IsSharedModeMasterClient) return;

            // Objects already in the room arrive asynchronously after joining. Without this wait, a
            // client that becomes master before the existing ball has replicated to it would decide
            // the ball is missing and spawn a second one.
            if (_settleUntil <= 0f) _settleUntil = Time.time + settleSeconds;
            if (Time.time < _settleUntil) return;

            if (ballPrefab != null && NetBall.Instance == null)
            {
                runner.Spawn(ballPrefab, new Vector3(0f, 0.5f, 0f), Quaternion.identity);
                Debug.Log("[Net] Master spawned ball");
            }
            if (matchPrefab != null && MatchController.Instance == null)
            {
                runner.Spawn(matchPrefab, Vector3.zero, Quaternion.identity);
                Debug.Log("[Net] Master spawned MatchController");
            }
        }

        // Master-client reassignment is not guaranteed to have happened by the time OnPlayerLeft
        // fires, so a cheap heartbeat is what actually makes recovery reliable — the callbacks below
        // only make it immediate.
        void Update()
        {
            if (_runner == null || !_runner.IsRunning) { _settleUntil = 0f; return; }

            // The overlay comes down the moment our own player object is in the match — that is when
            // NetPlayer takes over the camera and there is something to look at.
            if (_screen != null && _runner.TryGetPlayerObject(_runner.LocalPlayer, out var mine) && mine != null)
            {
                _screen.Hide();
                _screen = null;
            }

            WatchWaitingRoom();
            WatchForMatchEnd();

            _ensureCooldown -= Time.deltaTime;
            if (_ensureCooldown > 0f) return;
            _ensureCooldown = 1f;
            EnsureMatchObjects(_runner);
        }

        // Waiting for players has to be escapable. A quick match nobody else joins, or a friend who
        // never types the code, would otherwise trap the player in an empty arena for good.
        void WatchWaitingRoom()
        {
            var mc = MatchController.Instance;

            // Joining by code does not pick a mode — the room already has one. Adopt it, so that if
            // this peer later becomes master its idea of the match size is the room's, not the
            // default it happened to start with.
            if (RoomCode != null && mc != null && mc.Seats > 0) Mode = (MatchMode)mc.Seats;

            bool waiting = mc != null && mc.CurPhase == MatchController.Phase.Waiting;
            if (waiting && _abandon == null) _abandon = MainMenu.Overlay("ABBANDONA", LeaveMatch);
            else if (!waiting && _abandon != null) { Destroy(_abandon); _abandon = null; }

            if (!waiting) { _waitUntil = 0f; _lastSeated = -1; return; }

            // Somebody arriving is progress, so the clock starts again. Otherwise a room that fills
            // up slowly would throw out the player who had the patience to wait for it.
            int seated = mc.Seated;
            if (seated != _lastSeated) { _lastSeated = seated; _waitUntil = Time.time + waitTimeoutSeconds; }

            if (Time.time >= _waitUntil)
            {
                _notice = RoomCode != null ? "NESSUNO SI E' UNITO ALLA STANZA\n" + RoomCode
                                           : "NESSUN AVVERSARIO TROVATO";
                Debug.Log("[Net] waiting room timed out after " + waitTimeoutSeconds + "s");
                LeaveMatch();
            }
        }

        // A finished match used to be a dead end: the phase stayed Finished and nothing else ever
        // happened. Each client leaves on its own clock — this is presentation, not shared state.
        void WatchForMatchEnd()
        {
            var mc = MatchController.Instance;
            if (mc == null || mc.CurPhase != MatchController.Phase.Finished) { _leaveAt = 0f; return; }
            if (_leaveAt <= 0f) { _leaveAt = Time.time + postMatchSeconds; return; }
            if (Time.time >= _leaveAt) { _leaveAt = 0f; LeaveMatch(); }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsSharedModeMasterClient) return;
            ReclaimOrphans(runner, player);
            EnsureMatchObjects(runner);
        }

        // Objects whose State Authority just walked out of the session would otherwise sit there
        // unsimulated. The master takes them over. Mirrors the OnPlayerLeft handler in Photon's
        // Pirate Adventure sample: anything Fusion already handles, or that we are not allowed to
        // take, is skipped.
        static void ReclaimOrphans(NetworkRunner runner, PlayerRef gone)
        {
            _orphanScratch.Clear();
            runner.GetAllNetworkObjects(_orphanScratch);
            foreach (var obj in _orphanScratch)
            {
                if (obj == null || obj.StateAuthority != gone) continue;
                var f = obj.Flags;
                if ((f & NetworkObjectFlags.MasterClientObject) == NetworkObjectFlags.MasterClientObject)
                    continue;   // Fusion migrates it for us
                if ((f & NetworkObjectFlags.DestroyWhenStateAuthorityLeaves) == NetworkObjectFlags.DestroyWhenStateAuthorityLeaves)
                    continue;   // it is on its way out
                if ((f & NetworkObjectFlags.AllowStateAuthorityOverride) != NetworkObjectFlags.AllowStateAuthorityOverride)
                    continue;   // not ours to take
                obj.RequestStateAuthority();
                Debug.Log("[Net] Master reclaimed " + obj.name + " from player " + gone.PlayerId);
            }
            _orphanScratch.Clear();
        }

        // The single way out of a match, whatever ended it: the final whistle, a timed-out waiting
        // room, ABBANDONA, or a connection that simply dropped. All of them should leave the player
        // somewhere they can act instead of in a frozen world.
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log("[Net] shutdown: " + shutdownReason);
            TearDownRunner();
            _started = false;
            _leaveAt = 0f;
            _waitUntil = 0f;
            _lastSeated = -1;
            if (_screen != null) { _screen.Hide(); _screen = null; }

            if (_notice != null)
            {
                // Say why, rather than dropping the player on the menu with no explanation.
                ConnectingScreen.Show(_notice).ShowError(_notice, MainMenu.Show);
                _notice = null;
                return;
            }
            MainMenu.Show();
        }

        // Presentation is left to OnShutdown, which Fusion calls next: handling it in both places is
        // what would stack two screens on top of each other.
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.LogWarning("[Net] disconnected: " + reason);
            _notice = "CONNESSIONE PERSA\n" + reason;
        }

        static readonly List<NetworkObject> _orphanScratch = new List<NetworkObject>();
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
