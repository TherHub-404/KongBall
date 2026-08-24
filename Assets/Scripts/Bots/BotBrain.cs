using UnityEngine;

namespace KongBall.Bots
{
    // A bot that plays like somebody rather than like a solver.
    //
    // Two things it can do: chase the ball and hit it (ChaseAndHit), or contest whichever opponent is
    // closest to beating it there (ContestThreat, via the spin attack — jump, then jump again once
    // airborne). The old push/grab tackle is gone project-wide (CORE_GAMEPLAY_RESET); this is its
    // replacement, added once it became clear "the bots don't know how to play any more" meant, in
    // part, that nothing had ever given them the spin attack a human already had. Team roles — two
    // bots coordinating instead of both chasing the same ball — stay future work.
    //
    // Runs only where NetPlayer calls it: on the peer holding State Authority, which for a bot is
    // always the master that spawned it. That is also why Random is safe here — a bot is simulated
    // once per tick on one peer, never re-simulated and never simulated twice for the same tick, and
    // what the others see is the replicated result rather than a re-run of this code.
    public class BotBrain : MonoBehaviour, IPlayerBrain
    {
        [Header("Movement")]
        [Tooltip("Top speed as a fraction of a human's. Just under, never over.")]
        public float topSpeed = 0.92f;

        [Header("Ball approach")]
        [Tooltip("Same range NetPlayer uses to decide ACTION hits the ball. Kept as its own field " +
                 "rather than read off NetPlayer, so a bot can be tuned to commit to the approach a " +
                 "little earlier than a human's exact hit window.")]
        public float hitCommitRange = 1.9f;
        [Tooltip("How far behind the ball (on the side away from the attacking goal) the bot aims its " +
                 "approach, so arriving in hit range means already moving toward goal rather than " +
                 "sideways into it. Too large and the bot takes a wide, obviously artificial arc.")]
        public float approachOffset = 1.4f;

        [Header("Contesting (spin attack on the biggest threat to the ball)")]
        [Tooltip("An opponent counts as a threat once they are this much closer to the ball than the " +
                 "bot itself — close enough that racing straight for the ball would lose, so the bot " +
                 "intercepts them with the spin attack instead. This is the replacement for the old " +
                 "push/grab tackle, which the spin attack replaced project-wide (see CORE_GAMEPLAY_RESET " +
                 "in Bots/AGENTS.md) — bots never got an equivalent until now.")]
        public float threatMargin = 1.5f;
        [Tooltip("How close to the threatening opponent before committing to the spin attack.")]
        public float contestRange = 2.2f;
        [Tooltip("Delay before switching to a newly-appeared decision — a new threat, or the ball no " +
                 "longer being one. Reacting in the same tick something changes is the single biggest " +
                 "\"this is a bot\" tell (Bots/AGENTS.md's own \"Believability\" section names it #4); " +
                 "never delays actually moving or jumping once committed, only the decision itself.")]
        public float reactionSeconds = 0.22f;

        [Header("Jump")]
        [Tooltip("Ball this high and this near overhead: go up for it.")]
        public float jumpBallHeight = 1.8f;
        public float jumpReach = 3.5f;
        public float jumpCooldown = 1.5f;
        [Tooltip("How often a loose hop is considered while the ball is far away.")]
        public float hopEverySeconds = 6f;

        float _jumpCd;
        float _hopIn;
        bool _jump;
        // Fired the grounded jump that starts a contest; the very next tick (now airborne) fires the
        // second jump press that NetPlayer resolves as the spin attack — same two-press shape a human
        // double-tapping the button produces, just decided here instead of by a thumb.
        bool _committedToSpin;
        NetPlayer _pendingThreat;    // NearestThreat's raw pick this tick, not yet acted on
        float _pendingFor;          // how long _pendingThreat has been the top candidate, unbroken
        NetPlayer _committedThreat; // the one actually being contested; sticks until the attempt fires

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
                _jump = false;
                _committedToSpin = false;
                _pendingThreat = null; _pendingFor = 0f; _committedThreat = null;
                return want;
            }

            if (_jumpCd > 0f) _jumpCd -= dt;

            // Reaction delay: a freshly-appeared (or freshly-gone) threat has to stay the answer for
            // reactionSeconds, unbroken, before the bot actually commits to it — otherwise the switch
            // happens on the very tick the situation changes, which reads as inhuman regardless of
            // how good the underlying decision is.
            var rawThreat = NearestThreat(me, ball);
            if (rawThreat != _pendingThreat) { _pendingThreat = rawThreat; _pendingFor = 0f; }
            _pendingFor += dt;

            if (_committedThreat == null && rawThreat != null && _pendingFor >= reactionSeconds)
                _committedThreat = rawThreat;
            else if (_committedThreat != null && !_committedThreat.IsStumbled && rawThreat == null && _pendingFor >= reactionSeconds)
                _committedThreat = null; // the threat picture cleared and stayed clear — stand down

            if (_committedThreat != null) return ContestThreat(me, _committedThreat);
            _committedToSpin = false; // no live contest; the next one starts clean
            return ChaseAndHit(me, ball, me.transform.position, dt);
        }

        // Approach the far side of the ball (away from the goal being attacked) rather than the ball
        // itself, so that by the time the bot is close enough to hit it, it is already moving toward
        // goal instead of sideways into it — THE BALL IS NEVER POSSESSED, so there is no aim step
        // afterward to correct a bad approach angle.
        //
        // No ramp of its own on the move vector any more: NetPlayer's own acceleration curve (added
        // for the same "a thumb is not a step change" reason this used to exist here) now applies to
        // every mover, bot included — stacking a second ramp on top just made the bot noticeably
        // mushier to steer than a human, for no reason once the shared one existed.
        PlayerIntent ChaseAndHit(NetPlayer me, NetBall ball, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            Vector3 ballPos = ball.transform.position;
            float goalX = GoalX(me);

            Vector3 towardGoal = Flat(new Vector3(goalX, 0f, ballPos.z) - ballPos);
            if (towardGoal.sqrMagnitude > 1e-4f) towardGoal.Normalize(); else towardGoal = Flat(me.transform.forward);
            Vector3 approachPoint = ballPos - towardGoal * approachOffset;

            want.Move = Toward(Flat(approachPoint - here));
            MaybeJump(ball, here, dt);

            if (Flat(ballPos - here).magnitude <= hitCommitRange) want.Action = true;
            return want;
        }

        // The opponent most likely to reach the ball before this bot would — the spin attack's
        // target. Replaces the old push/grab tackle (gone project-wide, see CORE_GAMEPLAY_RESET in
        // Bots/AGENTS.md); nothing filled in the equivalent for a bot until now, which is plausibly
        // most of "the bots don't know how to play any more" — a human can contest with the spin
        // attack and a bot until now simply could not.
        NetPlayer NearestThreat(NetPlayer me, NetBall ball)
        {
            float myDist = Flat(ball.transform.position - me.transform.position).magnitude;
            NetPlayer best = null; float bestDist = float.MaxValue;
            foreach (var np in NetPlayer.Live)
            {
                if (np == null || np == me || np.NetTeam == me.NetTeam || np.IsStumbled) continue;
                float d = Flat(ball.transform.position - np.transform.position).magnitude;
                if (d + threatMargin < myDist && d < bestDist) { bestDist = d; best = np; }
            }
            return best;
        }

        // Run at the threat, then jump; the SECOND jump press, fired the next tick once airborne, is
        // what NetPlayer resolves as the spin attack (see class doc) — the same shape ConsumeJump
        // already expects from a human double-tapping the button.
        PlayerIntent ContestThreat(NetPlayer me, NetPlayer threat)
        {
            var want = default(PlayerIntent);
            Vector3 toThreat = Flat(threat.transform.position - me.transform.position);
            want.Move = Toward(toThreat);

            if (me.Grounded)
            {
                if (toThreat.magnitude <= contestRange && _jumpCd <= 0f)
                {
                    _jump = true;
                    _committedToSpin = true;
                    _jumpCd = jumpCooldown;
                }
                else _committedToSpin = false;
            }
            else if (_committedToSpin)
            {
                _jump = true;
                _committedToSpin = false; // one attempt per commitment, win or lose
                _committedThreat = null; // spent; Think() picks a fresh target next tick
            }
            return want;
        }

        Vector3 Toward(Vector3 dir) => dir.sqrMagnitude > 0.0625f ? dir.normalized * topSpeed : Vector3.zero;

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
