using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace KongBall
{
    // Bootstrap: starts a Fusion NetworkRunner in Shared Mode, joins a fixed
    // session, and spawns one player per client. Each client owns (StateAuthority) its own
    // player; NetPlayer moves it and NetworkTransform replicates to everyone else.
    public class NetLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Prefab (must have NetworkObject + NetworkTransform + NetPlayer)")]
        public NetworkObject playerPrefab;

        [Header("Ball prefab (NetworkObject + NetworkTransform + NetBall)")]
        public NetworkObject ballPrefab;

        [Header("MatchController prefab (NetworkObject + MatchController)")]
        public NetworkObject matchPrefab;

        [Header("Session")]
        public string sessionName = "kongball";
        public int maxPlayers = 6;
        public bool autoStart = false;

        [Header("Team select UI (hidden after choosing)")]
        public GameObject teamPanel;

        [Tooltip("Grace period after joining before the master may spawn missing shared objects, so " +
                 "objects already in the room have time to replicate first.")]
        public float settleSeconds = 3f;

        NetworkRunner _runner;
        Team _chosenTeam = Team.Blue;
        bool _started;
        float _ensureCooldown;
        float _settleUntil;
        ConnectingScreen _screen;

        async void Start()
        {
            if (autoStart) await StartShared();
        }

        // Wired to the BLU (0) / ROSSO (1) buttons on the team-select panel.
        public void SelectTeamAndStart(int team)
        {
            if (_started) return;
            _started = true;
            _chosenTeam = (Team)team;
            if (teamPanel != null) teamPanel.SetActive(false);
            _screen = ConnectingScreen.Show("CONNESSIONE");
            _ = StartShared();
        }

        public async System.Threading.Tasks.Task StartShared()
        {
            if (_runner != null) return;
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = maxPlayers,
            });

            if (result.Ok)
            {
                Debug.Log("[Net] Connected (Shared)");
                if (_screen != null) _screen.SetMessage("ENTRO IN PARTITA");
                return;
            }

            // Without this the player was stuck for good: _started stayed true, so the team buttons
            // did nothing and there was no way back.
            Debug.LogWarning("[Net] StartGame FAILED: " + result.ShutdownReason);
            Destroy(_runner);
            _runner = null;
            _started = false;
            if (_screen != null) _screen.ShowError("CONNESSIONE FALLITA\n" + result.ShutdownReason, ReopenTeamSelect);
            _screen = null;   // the error screen owns itself until the player retries
        }

        void ReopenTeamSelect()
        {
            if (teamPanel != null) teamPanel.SetActive(true);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Shared Mode: each client spawns only its own player object.
            if (player == runner.LocalPlayer) SpawnLocalPlayer(runner, player);
            EnsureMatchObjects(runner);
        }

        void SpawnLocalPlayer(NetworkRunner runner, PlayerRef player)
        {
            bool blue = _chosenTeam == Team.Blue;
            float x = blue ? -6f : 6f;
            float z = ((player.PlayerId % 3) - 1) * 3f; // spread teammates along z
            Vector3 pos = new Vector3(x, 1f, z);
            Quaternion rot = Quaternion.LookRotation(blue ? Vector3.right : Vector3.left, Vector3.up);

            var no = runner.Spawn(playerPrefab, pos, rot, player);
            var np = no != null ? no.GetComponent<NetPlayer>() : null;
            if (np != null) np.NetTeam = (int)_chosenTeam;
            if (no != null) runner.SetPlayerObject(player, no); // the ball looks players up by PlayerRef
            Debug.Log("[Net] Spawned local player team=" + _chosenTeam + " at " + pos);
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

            _ensureCooldown -= Time.deltaTime;
            if (_ensureCooldown > 0f) return;
            _ensureCooldown = 1f;
            EnsureMatchObjects(_runner);
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

        static readonly List<NetworkObject> _orphanScratch = new List<NetworkObject>();
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
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
