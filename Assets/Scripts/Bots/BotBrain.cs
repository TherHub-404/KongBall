using UnityEngine;

namespace KongBall.Bots
{
    // A bot that plays like somebody rather than like a solver.
    //
    // Simplified again alongside the spin-attack change: ACTION away from the ball does nothing any
    // more (push/grab are gone), so the old Tackle mode — steer at the nearest threat to the ball and
    // hold ACTION — no longer does anything either. Contesting an opponent is now the spin attack's
    // job (jump while already airborne), which needs its own timing decision a bot doesn't make yet.
    // Until that exists, the bot only does the one thing it can still affect the game with: chase the
    // ball and hit it. Team roles and opponent-contesting AI both stay future work.
    //
    // Runs only where NetPlayer calls it: on the peer holding State Authority, which for a bot is
    // always the master that spawned it. That is also why Random is safe here — a bot is simulated
    // once per tick on one peer, never re-simulated and never simulated twice for the same tick, and
    // what the others see is the replicated result rather than a re-run of this code.
    public class BotBrain : MonoBehaviour, IPlayerBrain
    {
        [Header("Movement")]
        [Tooltip("Top speed as a fraction of a human's. Just under, never over — and only just under: " +
                 "most of what made the old bot feel fast was the instant direction changes, which " +
                 "steerRamp now costs it. Cut this too hard and the bot can never close on the ball.")]
        public float topSpeed = 0.92f;

        [Tooltip("Seconds for the steering to swing to a new direction. A thumb is not a step change, " +
                 "and instant reversals are most of what made the old bot read as a machine even at " +
                 "the same top speed.")]
        public float steerRamp = 0.18f;

        [Header("Ball approach")]
        [Tooltip("Same range NetPlayer uses to decide ACTION hits the ball. Kept as its own field " +
                 "rather than read off NetPlayer, so a bot can be tuned to commit to the approach a " +
                 "little earlier than a human's exact hit window.")]
        public float hitCommitRange = 1.9f;
        [Tooltip("How far behind the ball (on the side away from the attacking goal) the bot aims its " +
                 "approach, so arriving in hit range means already moving toward goal rather than " +
                 "sideways into it. Too large and the bot takes a wide, obviously artificial arc.")]
        public float approachOffset = 1.4f;

        [Header("Jump")]
        [Tooltip("Ball this high and this near overhead: go up for it.")]
        public float jumpBallHeight = 1.8f;
        public float jumpReach = 3.5f;
        public float jumpCooldown = 1.5f;
        [Tooltip("How often a loose hop is considered while the ball is far away.")]
        public float hopEverySeconds = 6f;

        float _jumpCd;
        Vector3 _steer;   // the ramped move vector — one advance per tick, never two
        float _hopIn;
        bool _jump;

        public bool ConsumeJump()
        {
            if (!_jump) return false;
            _jump = false;
            return true;
        }

        public PlayerIntent Think(NetPlayer me, float dt)
        {
            var want = default(PlayerIntent);
            var ball = NetBall.Instance;
            if (me == null || ball == null) return want;

            // Nothing to decide outside live play, and everything in flight is dropped rather than
            // carried across a kickoff.
            var mc = MatchController.Instance;
            if (mc != null && mc.CurPhase != MatchController.Phase.Playing)
            {
                _steer = Vector3.zero;
                _jump = false;
                return want;
            }

            if (_jumpCd > 0f) _jumpCd -= dt;
            return ChaseAndHit(me, ball, me.transform.position, dt);
        }

        // Approach the far side of the ball (away from the goal being attacked) rather than the ball
        // itself, so that by the time the bot is close enough to hit it, it is already moving toward
        // goal instead of sideways into it — THE BALL IS NEVER POSSESSED, so there is no aim step
        // afterward to correct a bad approach angle.
        PlayerIntent ChaseAndHit(NetPlayer me, NetBall ball, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            Vector3 ballPos = ball.transform.position;
            float goalX = GoalX(me);

            Vector3 towardGoal = Flat(new Vector3(goalX, 0f, ballPos.z) - ballPos);
            if (towardGoal.sqrMagnitude > 1e-4f) towardGoal.Normalize(); else towardGoal = Flat(me.transform.forward);
            Vector3 approachPoint = ballPos - towardGoal * approachOffset;

            want.Move = Steer(Flat(approachPoint - here), dt);
            MaybeJump(ball, here, dt);

            if (Flat(ballPos - here).magnitude <= hitCommitRange) want.Action = true;
            return want;
        }

        // The input ramp. Called exactly once per tick on every path, because two advances in one tick
        // would quietly halve the ramp it exists to impose.
        Vector3 Steer(Vector3 dir, float dt)
        {
            Vector3 wish = dir.sqrMagnitude > 0.0625f ? dir.normalized * topSpeed : Vector3.zero;
            _steer = Vector3.MoveTowards(_steer, wish, topSpeed / Mathf.Max(0.02f, steerRamp) * dt);
            return _steer;
        }

        void MaybeJump(NetBall ball, Vector3 here, float dt)
        {
            _hopIn -= dt;
            if (_jumpCd > 0f) return;

            Vector3 bp = ball.transform.position;
            float flat = Flat(bp - here).magnitude;

            // Up for a high ball. Note that in this game's rules height does not decide anything — the
            // ball is taken by FLAT distance — so this buys the bot nothing mechanically. It is here
            // because somebody who never leaves the ground with the ball over their head does not
            // look like somebody. It also never chains into a spin attack (see class comment) — a
            // bot's jump is still only ever a jump.
            if (bp.y > jumpBallHeight && flat < jumpReach)
            {
                _jump = true;
                _jumpCd = jumpCooldown;
                return;
            }

            // And a loose hop now and then, only while the ball is far enough away that it cannot cost
            // anything: air control is lower than ground control, so a jump is a small real price.
            if (_hopIn > 0f) return;
            _hopIn = hopEverySeconds;
            if (flat > 8f && Random.value < 0.5f) { _jump = true; _jumpCd = jumpCooldown; }
        }

        // The goal this player attacks. Read off Arena rather than restated here: it owns the pitch
        // measurements, and a second copy of "where the goal is" would eventually disagree with the
        // one that decides goals. Blue attacks +x, as everywhere else.
        static float GoalX(NetPlayer me) => me.NetTeam == (int)Team.Blue ? Arena.GoalLineX : -Arena.GoalLineX;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
