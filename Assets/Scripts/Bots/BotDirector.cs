using Fusion;
using UnityEngine;

namespace KongBall.Bots
{
    // Who is on the pitch that nobody is playing, and when. This is the ONE name the rest of the game
    // calls into this folder: NetLauncher pokes it from the same master-only heartbeat that keeps the
    // ball and the MatchController alive, so a bot that fails to appear is retried rather than lost.
    public static class BotDirector
    {
        // How many bots a mode wants on the pitch.
        //
        // Practice is the only one for now, and not because a mixed match is hard to arrange: a bot
        // in a real match has to survive the master leaving, and this bot cannot — it uses the human
        // prefab, which is flagged DestroyWhenStateAuthorityLeaves so that YOUR avatar vanishes when
        // you quit. Filling a real 2v2 needs a prefab of its own. See AGENTS.md.
        public static int Wanted(MatchMode mode) => mode == MatchMode.Practice ? 1 : 0;

        public static void Ensure(NetworkRunner runner, NetworkObject prefab, MatchMode mode)
        {
            int wanted = Wanted(mode);
            if (wanted <= 0 || prefab == null) return;
            if (runner == null || !runner.IsRunning) return;
            if (!runner.IsSharedModeMasterClient) return;   // bots are the master's to simulate

            int humans = 0;
            foreach (var p in runner.ActivePlayers) humans++;

            int seated = 0, bots = 0, blue = 0, red = 0;
            foreach (var np in NetPlayer.Live)
            {
                if (np == null) continue;
                if (np.IsBot) bots++;
                // Sides are chosen to balance the pitch, so every human has to be on one first.
                // Spawning before that would land the bot on the same half as the player: the master
                // hands out human teams from MatchController, and until it has, a side count means
                // nothing.
                else if (np.TeamAssigned) seated++;
                else return;
                if (np.TeamAssigned) { if (np.NetTeam == (int)Team.Red) red++; else blue++; }
            }
            if (seated < humans) return;   // somebody's player object has not arrived yet
            if (bots >= wanted) return;

            for (int i = bots; i < wanted; i++)
            {
                int team = blue <= red ? (int)Team.Blue : (int)Team.Red;
                if (team == (int)Team.Red) red++; else blue++;
                Spawn(runner, prefab, team, i);
            }
        }

        static void Spawn(NetworkRunner runner, NetworkObject prefab, int team, int index)
        {
            bool blue = team == (int)Team.Blue;
            float z = ((index % 3) - 1) * 3f;
            var pos = new Vector3(blue ? -6f : 6f, NetPlayer.SpawnHeight, z);
            var rot = Quaternion.LookRotation(blue ? Vector3.right : Vector3.left, Vector3.up);

            // The HUMAN prefab, on purpose: a bot has to move, collide, carry, kick and be robbed
            // through exactly the same object a person does. What makes it a bot is the brain added
            // below — a plain MonoBehaviour, so nothing about the networked object changes.
            //
            // Networked values are written in this callback because Fusion invokes it BEFORE Spawned,
            // which is where a spawned object's state is meant to be seeded: NetPlayer then finds both
            // its team and its brain on its very first tick.
            var no = runner.Spawn(prefab, pos, rot, null, (r, obj) =>
            {
                var np = obj.GetComponent<NetPlayer>();
                if (np == null) return;
                np.IsBot = true;
                // The master decides a bot's side directly. RPC_AssignTeam exists because only a peer
                // may write its own state, and there is no peer here — this object is the master's.
                np.NetTeam = team;
                np.TeamAssigned = true;
                obj.gameObject.AddComponent<BotBrain>();
            });

            if (no != null) Debug.Log("[Bots] spawned a bot on " + (Team)team);
            else Debug.LogWarning("[Bots] spawn failed");
        }
    }
}
