using UnityEngine;

namespace KongBall.Bots
{
    // A bot that plays like somebody rather than like a solver.
    //
    // The first version was three lines of intent — run at the ball, run at the goal, shoot — and it
    // was read immediately for what it was: it never challenged, never left the ground, walked the
    // ball to ten metres and scored every time. What follows is the same three decisions with the
    // levers that make them human: a reaction delay on DECISIONS, a ramp on the steering, one
    // committed act at a time, and a shot that is aimed at a spot chosen in advance with an error
    // that grows with distance.
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
                 "steerRamp now costs it. Cut this too hard and the bot can never close on a carrier " +
                 "who simply runs away, and the challenge below stops happening. It is the first " +
                 "number to reach for if the bot never manages a tackle, or still feels quick.")]
        public float topSpeed = 0.92f;

        [Tooltip("Seconds for the steering to swing to a new direction. A thumb is not a step change, " +
                 "and instant reversals are most of what made the old bot read as a machine even at " +
                 "the same top speed.")]
        public float steerRamp = 0.18f;

        [Header("Reaction")]
        [Tooltip("Delay before acting on a change of possession. Applied to DECISIONS, never to " +
                 "movement: the feet keep doing what they were doing, which reads as late rather " +
                 "than as frozen.")]
        public float reactionSeconds = 0.22f;

        [Header("Challenge (opponent carrying)")]
        [Tooltip("Distance at which the bot goes in. NetPlayer needs the target in FRONT of it, which " +
                 "is why the bot steers at the carrier and not at the ball.")]
        public float tackleRange = 1.9f;
        [Tooltip("Chance a challenge is a grab rather than a push.")]
        public float grabChance = 0.35f;
        [Tooltip("How long a grab is held. Must exceed NetPlayer.holdThreshold or it comes out a push.")]
        public float grabHold = 0.9f;
        public float tackleCooldown = 1.2f;

        [Header("Shooting")]
        [Tooltip("Nearest and farthest distance the bot will shoot from. One value is drawn per " +
                 "possession, so the same bot sometimes walks it in and sometimes lets fly.")]
        public float shootNearest = 9f;
        public float shootFarthest = 20f;
        [Tooltip("Aim error in DEGREES at the nearest shooting distance, and at the farthest. An angle " +
                 "rather than a distance on the goal line, because that is how a shot is actually " +
                 "mishit: the same wrist error costs more the further out you are. With these two the " +
                 "bot converts about 99% from 9 m, 77% from 16 and 55% from 20 — deadly up close, " +
                 "which is right when there is no keeper and the mouth is 6.8 m wide, and a gamble " +
                 "from distance.")]
        public float aimErrorNear = 6f;
        public float aimErrorFar = 16f;
        [Tooltip("Chance a shot is a real mistake, with two and a half times the usual error.")]
        public float missChance = 0.15f;

        [Tooltip("Power at the nearest and farthest shooting distance. Nearly flat, and that is not " +
                 "laziness — see Attack for why a harder shot from farther out goes OVER the bar.")]
        public float powerNear = 0.45f;
        public float powerFar = 0.72f;
        [Tooltip("How long the kick is held before release. It fires on the RELEASE edge, exactly as " +
                 "it does for a thumb, so it has to be held for at least one tick.")]
        public float chargeSeconds = 0.18f;
        public float shotCooldown = 0.6f;

        [Header("Jump")]
        [Tooltip("Ball this high and this near overhead: go up for it.")]
        public float jumpBallHeight = 1.8f;
        public float jumpReach = 3.5f;
        public float jumpCooldown = 1.5f;
        [Tooltip("How often a loose hop is considered while the ball is far away.")]
        public float hopEverySeconds = 6f;

        enum Mode { Chase, Attack, Defend, Support }

        Mode _mode = Mode.Chase;
        float _reactFor;        // counts down while a change of mode is being taken in
        Vector3 _steer;         // the ramped move vector — one advance per tick, never two
        float _hold;            // seconds the kick has been held
        float _shotCd, _tackleCd, _jumpCd;
        float _tackleFor;       // >0 while a challenge is being held
        Transform _tackleAt;    // who it is being held on
        float _hopIn;
        bool _jump;

        // The shot plan for THIS possession. Chosen when the ball is won and then stuck to.
        float _shootFrom = 12f;   // until a possession is won and a real plan is drawn
        float _aimZ;
        bool _fluff;            // this one is going wide on purpose

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
            // carried across a kickoff — otherwise the bot would shoot, or finish a tackle, on the
            // whistle.
            var mc = MatchController.Instance;
            if (mc != null && mc.CurPhase != MatchController.Phase.Playing)
            {
                _steer = Vector3.zero;
                _hold = 0f;
                _tackleFor = 0f;
                _jump = false;
                return want;
            }

            if (_shotCd > 0f) _shotCd -= dt;
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

            var carrier = Carrier(ball);
            Mode m = Evaluate(me, carrier);
            if (m != _mode)
            {
                if (_reactFor <= 0f) _reactFor = reactionSeconds;
                _reactFor -= dt;
                if (_reactFor <= 0f) { _mode = m; Enter(ball); }
            }
            else _reactFor = 0f;

            switch (_mode)
            {
                case Mode.Attack: return Attack(me, ball, here, dt);
                case Mode.Defend: return Defend(me, ball, carrier, here, dt);
                case Mode.Support: return Support(me, ball, here, dt);
                default: return Chase(ball, here, dt);
            }
        }

        // --- the four things it can be doing ------------------------------------------------------

        PlayerIntent Chase(NetBall ball, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            want.Move = Steer(Flat(ball.transform.position - here), dt);
            MaybeJump(ball, here, dt);
            return want;
        }

        // Straight at the CARRIER, not at the ball. The ball sits a metre and a half ahead of whoever
        // has it, so a bot that runs at the ball arrives in front of its opponent — and NetPlayer only
        // finds a target that is in front of ITSELF. Running at the person is what makes the challenge
        // land, and it is why the old bot never took the ball off anybody.
        PlayerIntent Defend(NetPlayer me, NetBall ball, NetPlayer carrier, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            if (carrier == null) return Chase(ball, here, dt);

            Vector3 to = Flat(carrier.transform.position - here);
            want.Move = Steer(to, dt);
            MaybeJump(ball, here, dt);

            if (to.magnitude <= tackleRange && _tackleCd <= 0f)
            {
                // A push is a tap; a grab is a hold long enough to cross NetPlayer.holdThreshold. The
                // grab is the stronger move — a held player drops the ball — and that is exactly why
                // it is the rarer one.
                bool grab = Random.value < grabChance;
                _tackleFor = grab ? grabHold : 0.05f;
                _tackleAt = carrier.transform;
                _tackleCd = tackleCooldown;
                want.Action = true;
            }
            return want;
        }

        PlayerIntent Attack(NetPlayer me, NetBall ball, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            float goalX = GoalX(me, ball);

            // Toward the SPOT it means to shoot at, not toward the middle of the goal: the run itself
            // then varies with the plan instead of converging on the same line every time.
            Vector3 to = Flat(new Vector3(goalX, 0f, _aimZ) - here);
            float dist = to.magnitude;
            want.Move = Steer(to, dt);

            if (dist > _shootFrom || _shotCd > 0f) { _hold = 0f; return want; }

            _hold += dt;
            if (_hold < chargeSeconds) { want.Action = true; return want; }

            // Release: this is the tick NetPlayer reads as a kick.
            _hold = 0f;
            _shotCd = shotCooldown;

            Vector3 dir = Flat(new Vector3(goalX, 0f, _aimZ) - here);
            if (dir.sqrMagnitude < 1e-4f) dir = Flat(me.transform.forward);

            // Error that grows with distance, plus the occasional real mistake. Drawn ONCE, here, so a
            // shot is one decision rather than the average of sixty of them.
            float err = Mathf.Lerp(aimErrorNear, aimErrorFar,
                            Mathf.InverseLerp(shootNearest, shootFarthest, dist))
                        * (_fluff ? 2.5f : 1f);
            dir = Quaternion.Euler(0f, Random.Range(-err, err), 0f) * dir.normalized;
            want.KickDir = dir;

            // Power barely rises with distance, and it MUST NOT rise the way it looks like it should.
            //
            // NetBall gives every kick the same fixed lift ratio, so the impulse decides speed and
            // climb together: the ball leaves the foot at 0.6 m and gains roughly 0.32 x distance
            // before gravity takes it back, whatever the power. A goal counts only below goalHeight
            // (3 m). At full power from 20 m the ball crosses the line around 3.9 m — over the bar,
            // never a goal. At ~0.7 it crosses around 2.3 m and still carries the distance.
            //
            // The first bot scored every time from ten metres by passing six centimetres under that
            // bar. Scaling power with distance, which is the obvious thing to write, would have made
            // every long shot sail over and read as a different bug entirely.
            want.KickPower = Mathf.Lerp(powerNear, powerFar,
                Mathf.InverseLerp(shootNearest, shootFarthest, dist));
            _fluff = Random.value < missChance;   // and the next attempt gets its own roll
            return want;
        }

        // A team mate has it. Making a run at the goal is a placeholder: real team roles — one player
        // on the ball and the other holding a support position — are still to come, and nothing
        // reaches this branch today because practice puts exactly one bot on the pitch.
        PlayerIntent Support(NetPlayer me, NetBall ball, Vector3 here, float dt)
        {
            var want = default(PlayerIntent);
            want.Move = Steer(Flat(new Vector3(GoalX(me, ball) * 0.6f, 0f, _aimZ) - here), dt);
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
            // ball picks the nearest player by FLAT distance — so this buys the bot nothing. It is here
            // because somebody who never leaves the ground with the ball over their head does not look
            // like somebody.
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

        // Chosen ONCE when the ball is won, and then stuck to. Re-rolling every tick averages out to
        // the centre of the goal at the shortest range the bot will accept — which is precisely the
        // sniper the first version was: it walked the ball in and never missed.
        void Enter(NetBall ball)
        {
            _hold = 0f;
            if (_mode != Mode.Attack) return;
            _shootFrom = Random.Range(shootNearest, shootFarthest);
            float mouth = Arena.GoalHalfZ * 0.6f;
            _aimZ = Random.Range(-mouth, mouth);
            _fluff = Random.value < missChance;
        }

        static Mode Evaluate(NetPlayer me, NetPlayer carrier)
        {
            if (carrier == null) return Mode.Chase;
            if (carrier == me) return Mode.Attack;
            return carrier.NetTeam != me.NetTeam ? Mode.Defend : Mode.Support;
        }

        // Who has the ball, through public state only: the ball names an object and the live list
        // answers for it.
        static NetPlayer Carrier(NetBall ball)
        {
            if (!ball.OwnerId.IsValid) return null;
            foreach (var np in NetPlayer.Live)
                if (np != null && np.NetId == ball.OwnerId) return np;
            return null;
        }

        // The goal this player attacks. Read off the ball rather than restated here: the ball owns the
        // pitch measurements, and a second copy of "where the goal is" would eventually disagree with
        // the one that decides goals. Blue attacks +x, as everywhere else.
        static float GoalX(NetPlayer me, NetBall ball)
        {
            return me.NetTeam == (int)Team.Blue ? Arena.GoalLineX : -Arena.GoalLineX;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
