using UnityEngine;

namespace KongBall.Bots
{
    // A bot that plays like somebody rather than like a solver.
    //
    // Rewritten for CORE_GAMEPLAY_RESET: THE BALL IS NEVER POSSESSED any more, so the old Mode.Attack
    // ("I am the carrier, walk to a chosen spot, charge, release an aimed shot") and Mode.Support
    // ("a team mate is the carrier") no longer describe anything real — there is no carrier. What is
    // left is simpler by construction, not by choice: approach the ball from the side that sends it
    // goalward on contact, press ACTION when close, and contest whichever opponent is about to reach
    // a ball the bot cannot win first. Team roles (Support, in the old naming) stay future work, as
    // they already were before this reset — practice still puts exactly one bot on the pitch.
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
                 "steerRamp now costs it. Cut this too hard and the bot can never close on the ball or " +
                 "on an opponent, and the challenge below stops happening.")]
        public float topSpeed = 0.92f;

        [Tooltip("Seconds for the steering to swing to a new direction. A thumb is not a step change, " +
                 "and instant reversals are most of what made the old bot read as a machine even at " +
                 "the same top speed.")]
        public float steerRamp = 0.18f;

        [Header("Reaction")]
        [Tooltip("Delay before acting on a change between chasing the ball and tackling a threat. " +
                 "Applied to the DECISION, never to movement: the feet keep doing what they were " +
                 "doing, which reads as late rather than as frozen.")]
        public float reactionSeconds = 0.22f;

        [Header("Ball approach")]
        [Tooltip("Same range NetPlayer uses to decide ACTION hits the ball instead of pushing. Kept as " +
                 "its own field rather than read off NetPlayer, so a bot can be tuned to commit to the " +
                 "approach a little earlier than a human's exact hit window.")]
        public float hitCommitRange = 1.9f;
        [Tooltip("How far behind the ball (on the side away from the attacking goal) the bot aims its " +
                 "approach, so arriving in hit range means already moving toward goal rather than " +
                 "sideways into it. Too large and the bot takes a wide, obviously artificial arc.")]
        public float approachOffset = 1.4f;

        [Header("Challenge (contesting a threat to the ball)")]
        [Tooltip("Distance at which the bot goes in on an opponent it judges will reach the ball " +
                 "first. NetPlayer needs the target in FRONT of it, which is why the bot steers at " +
                 "the opponent and not at the ball while doing this.")]
        public float tackleRange = 1.9f;
        [Tooltip("Chance a challenge is a grab rather than a push.")]
        public float grabChance = 0.35f;
        [Tooltip("How long a grab is held. Must exceed NetPlayer.holdThreshold or it comes out a push.")]
        public float grabHold = 0.9f;
        public float tackleCooldown = 1.2f;

        [Header("Jump")]
        [Tooltip("Ball this high and this near overhead: go up for it.")]
        public float jumpBallHeight = 1.8f;
        public float jumpReach = 3.5f;
        public float jumpCooldown = 1.5f;
        [Tooltip("How often a loose hop is considered while the ball is far away.")]
        public float hopEverySeconds = 6f;

        enum Mode { Chase, Tackle }

        Mode _mode = Mode.Chase;
        float _reactFor;        // counts down while a change of mode is being taken in
        Vector3 _steer;         // the ramped move vector — one advance per tick, never two
        float _tackleCd, _jumpCd;
        float _tackleFor;       // >0 while a challenge is being held
        Transform _tackleAt;    // who it is being held on
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
            // carried across a kickoff — otherwise the bot would hit, or finish a tackle, on the
            // whistle.
            var mc = MatchController.Instance;
            if (mc != null && mc.CurPhase != MatchController.Phase.Playing)
            {
                _steer = Vector3.zero;
                _tackleFor = 0f;
                _jump = false;
                return want;
            }

            if (_tackleCd > 0f) _tackleCd -= dt;
            if (_jumpCd > 0f) _jumpCd -= dt;

            Vector3 here = me.transform.position;

            // A challenge is ONE committed act: decide push or grab, hold the button for as long as
            // that act takes, then let go. Re-deciding every tick would stutter and never cross the
            // hold threshold NetPlayer uses to tell a push from a grab.
            if (_tackleFor > 0f)
            {
                _tackleFor -= dt;
                want.Action = true;
                want.Move = Steer(_tackleAt != null ? Flat(_tackleAt.position - here) : Vector3.zero, dt);
                return want;
            }

            var threat = ClosestOpponentToBall(me, ball);
            Mode m = threat != null ? Mode.Tackle : Mode.Chase;
            if (m != _mode)
            {
                if (_reactFor <= 0f) _reactFor = reactionSeconds;
                _reactFor -= dt;
                if (_reactFor <= 0f) _mode = m;
            }
            else _reactFor = 0f;

            return _mode == Mode.Tackle && threat != null
                ? Tackle(me, ball, threat, here, dt)
                : ChaseAndHit(me, ball, here, dt);
        }

        // --- the two things it can be doing --------------------------------------------------------

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

        // Straight at the OPPONENT, not at the ball. NetPlayer's push/grab only finds a target that is
        // in front of the player throwing the challenge, so running at the person is what makes it
        // land — the same reason the old carrier-tackle worked this way.
        PlayerIntent Tackle(NetPlayer me, NetBall ball, NetPlayer opponent, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            Vector3 to = Flat(opponent.transform.position - here);
            want.Move = Steer(to, dt);
            MaybeJump(ball, here, dt);

            if (to.magnitude <= tackleRange && _tackleCd <= 0f)
            {
                // A push is a tap; a grab is a hold long enough to cross NetPlayer.holdThreshold. The
                // grab is the stronger move and that is exactly why it is the rarer one.
                bool grab = Random.value < grabChance;
                _tackleFor = grab ? grabHold : 0.05f;
                _tackleAt = opponent.transform;
                _tackleCd = tackleCooldown;
                want.Action = true;
            }
            return want;
        }

        // --- the levers --------------------------------------------------------------------------

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
            // look like somebody.
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

        // The opponent most likely to reach the ball before this bot does, so the bot can spend its
        // effort contesting them instead of losing a race to the ball it cannot win. "Closer to the
        // ball than I am" is a coarse stand-in for "will win the race" — it ignores speed and heading
        // — but it is the same order of sophistication the rest of this bot uses, and cheap to widen
        // later if it reads as wrong.
        static NetPlayer ClosestOpponentToBall(NetPlayer me, NetBall ball)
        {
            Vector3 ballPos = ball.transform.position;
            float myDist = Flat(ballPos - me.transform.position).magnitude;

            NetPlayer best = null;
            float bestD = myDist;
            foreach (var np in NetPlayer.Live)
            {
                if (np == null || np == me || np.NetTeam == me.NetTeam) continue;
                float d = Flat(ballPos - np.transform.position).magnitude;
                if (d < bestD) { bestD = d; best = np; }
            }
            return best;
        }

        // The goal this player attacks. Read off Arena rather than restated here: it owns the pitch
        // measurements, and a second copy of "where the goal is" would eventually disagree with the
        // one that decides goals. Blue attacks +x, as everywhere else.
        static float GoalX(NetPlayer me) => me.NetTeam == (int)Team.Blue ? Arena.GoalLineX : -Arena.GoalLineX;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
