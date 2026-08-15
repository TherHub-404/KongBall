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

        NetworkRunner _runner;
        Team _chosenTeam = Team.Blue;
        bool _started;

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

            Debug.Log(result.Ok ? "[Net] Connected (Shared)" : "[Net] StartGame FAILED: " + result.ShutdownReason);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Shared Mode: each client spawns only its own player object.
            if (player != runner.LocalPlayer) return;

            bool blue = _chosenTeam == Team.Blue;
            float x = blue ? -6f : 6f;
            float z = ((player.PlayerId % 3) - 1) * 3f; // spread teammates along z
            Vector3 pos = new Vector3(x, 1f, z);
            Quaternion rot = Quaternion.LookRotation(blue ? Vector3.right : Vector3.left, Vector3.up);

            var no = runner.Spawn(playerPrefab, pos, rot, player);
            var np = no != null ? no.GetComponent<NetPlayer>() : null;
            if (np != null) np.NetTeam = (int)_chosenTeam;
            if (no != null) runner.SetPlayerObject(player, no); // ball looks the owner up by PlayerRef
            Debug.Log("[Net] Spawned local player team=" + _chosenTeam + " at " + pos);

            // The Shared-Mode master spawns the single shared ball (once).
            if (runner.IsSharedModeMasterClient && ballPrefab != null && NetBall.Instance == null)
            {
                runner.Spawn(ballPrefab, new Vector3(0f, 0.5f, 0f), Quaternion.identity);
                Debug.Log("[Net] Master spawned ball");
            }
            if (runner.IsSharedModeMasterClient && matchPrefab != null && MatchController.Instance == null)
            {
                runner.Spawn(matchPrefab, Vector3.zero, Quaternion.identity);
                Debug.Log("[Net] Master spawned MatchController");
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
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
