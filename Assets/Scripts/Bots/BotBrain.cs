using UnityEngine;

namespace KongBall.Bots
{
    // The dumbest brain that can still play football: run at the ball, and once you have it, run at
    // the goal and shoot. No interception, no passing, no positioning, no tackling, no jumping.
    //
    // That is deliberate. Step 2 exists to prove ONE thing — that something with no connection and no
    // PlayerRef can hold the ball, be robbed of it and score, driving exactly the code a person
    // drives. Everything that makes a bot feel like a player is listed in AGENTS.md and belongs to
    // step 3; putting it here would mean debugging the seam and the behaviour at the same time, and
    // the seam is the part that cannot be judged by playing.
    //
    // Runs only where NetPlayer calls it: on the peer holding State Authority, which for a bot is
    // always the master that spawned it.
    public class BotBrain : MonoBehaviour, IPlayerBrain
    {
        [Tooltip("Distance from the goal at which the bot decides to shoot (metres).")]
        public float shootRange = 11f;

        [Tooltip("How long the kick is held before release. The kick fires on the RELEASE edge, " +
                 "exactly as it does for a thumb, so it has to be held for at least one tick.")]
        public float chargeSeconds = 0.18f;

        [Tooltip("Quiet period after a shot, so a kick the ball refuses is not retried every tick.")]
        public float shotCooldown = 0.6f;

        float _hold;        // seconds the kick has been held
        float _cooldown;    // seconds before another shot may be charged

        public bool ConsumeJump() => false;   // step 2 does not jump

        public PlayerIntent Think(NetPlayer me, float dt)
        {
            var want = default(PlayerIntent);
            var ball = NetBall.Instance;
            if (me == null || ball == null) return want;

            // Nothing to do outside live play, and the charge is dropped rather than carried across a
            // kickoff — otherwise the bot would shoot the instant the whistle went.
            var mc = MatchController.Instance;
            if (mc != null && mc.CurPhase != MatchController.Phase.Playing)
            {
                _hold = 0f;
                return want;
            }

            if (_cooldown > 0f) _cooldown -= dt;

            Vector3 here = me.transform.position;
            bool mine = ball.OwnerId == me.NetId;

            if (!mine)
            {
                // Straight at the ball, which is the crudest thing possible and reads as a bot
                // immediately: a person runs at where the ball will BE. That is the first fix in
                // step 3, and the reason it is first.
                _hold = 0f;
                Vector3 toBall = Flat(ball.transform.position - here);
                want.Move = toBall.magnitude > 0.25f ? toBall.normalized : Vector3.zero;
                return want;   // Action stays false: no pushing and no grabbing yet
            }

            Vector3 toGoal = Flat(Goal(me, ball) - here);
            float dist = toGoal.magnitude;
            want.Move = dist > 0.25f ? toGoal.normalized : Vector3.zero;

            if (dist > shootRange || _cooldown > 0f) { _hold = 0f; return want; }

            _hold += dt;
            if (_hold < chargeSeconds) { want.Action = true; return want; }

            // Release: this is the tick NetPlayer reads as a kick.
            _hold = 0f;
            _cooldown = shotCooldown;
            want.KickDir = dist > 1e-3f ? toGoal / dist : me.transform.forward;
            want.KickPower = Mathf.Clamp(dist / shootRange, 0.5f, 1f);
            return want;
        }

        // The goal this player attacks. Read off the ball rather than restated here: the ball owns the
        // pitch measurements, and a second copy of "where the goal is" would eventually disagree with
        // the one that actually decides goals. Blue attacks +x, as everywhere else.
        static Vector3 Goal(NetPlayer me, NetBall ball)
        {
            float x = me.NetTeam == (int)Team.Blue ? ball.goalLineX : -ball.goalLineX;
            return new Vector3(x, 0f, 0f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
