# Bots

Everything that plays without a human behind it lives in this folder. The rest of the game reaches in
through exactly two names, and no others: `BotDirector.Wanted` / `BotDirector.Ensure`, called from
`NetLauncher`. The interface a brain implements (`IPlayerBrain`) deliberately lives OUTSIDE this
folder, next to the player contract it belongs to, so that this folder holds implementations only.

Written in English to match the code comments; the surrounding conversation is in Italian.

> **CORE_GAMEPLAY_RESET note.** THE BALL IS NEVER POSSESSED any more — there is no carrier, no
> dribble, no "release to shoot". `BotBrain` was rewritten alongside `NetPlayer`/`NetBall` for this;
> most of this document changed with it. If you find a passage that still talks about a bot "carrying"
> the ball, that passage is stale and should be fixed, not trusted.

## The seams

The rest of the codebase was shaped so a bot needs no special case anywhere. Keep it that way.

| seam | contract |
|---|---|
| `NetPlayer.Live` | every player in the match, humans and bots. Anything that needs to find players scans this, never `Runner.ActivePlayers` — that list only knows about peers with a connection. |
| `NetBall.Hit(NetPlayer, dir)` | authority-side entry point. A remote player asks over `RPC_Hit`, which only resolves the sender; whoever already holds the ball's authority — every bot, since bots exist only on the master — calls it directly. Both go through `NetPlayer`'s private `Hit`. There is no ownership check any more: the authority re-validates distance itself instead. |
| `PlayerIntent` + `IPlayerBrain` | the two values a tick needs, in WORLD space: a move direction and the one contextual button. `NetPlayer.ReadIntent` returns either the joystick resolved against the camera, or `_brain.Think`. Everything below that line — acceleration, turning, the hit-vs-push priority, push, grab — is shared by construction. |

Plus two rules that are easy to get wrong.

**The forfeit check counts humans, not players** (`MatchController.TeamAbandoned`). A side whose only
human left is out even if its bot is still standing there, otherwise the match plays itself. The one
exception is a match `WithBots`, where a side is out only when it is completely empty — in practice
the bot's side has no human by design, and the humans-only rule would award the match on the first
whistle.

**Seats are bodies, `HumanSeats` are people.** `MatchController.Seats` is what the waiting room waits
for and includes bots; `MatchMode` and `HumanSeats` count people, and are what a joining client adopts
as the room's mode. A practice match is one human and two seats.

## Authority

Bots are spawned and simulated by the Shared Mode master. `BotDirector` spawns the **human prefab**
and adds `BotBrain` — a plain `MonoBehaviour`, so nothing about the networked object changes — in the
`onBeforeSpawned` callback, which Fusion invokes before `Spawned` and which is therefore also where
the bot's team and `IsBot` are seeded.

Using the human prefab is what keeps a bot honest: same collider, same speeds, same hit range, same
push/grab. It has one consequence, and it is the reason bots are confined to practice for now — that
prefab is flagged `DestroyWhenStateAuthorityLeaves`, so **a bot dies with the master**. In a practice
match the master is the only human and the match ends anyway. Filling a real 2v2 needs a prefab of
its own, flagged `MasterClientObject` so Fusion migrates it; the human prefab must NOT change, because
your avatar should vanish when you quit.

The contextual button is shared code, unconditionally: `NetPlayer.HandleBall` decides hit vs. push/grab
from distance to the ball alone, for a bot exactly as for a person. There is no "the bot was mid-kick"
edge case any more to reason about, because there is no longer a multi-tick kick to be mid of — a hit
is one instantaneous impulse on the press tick.

Do not reach for input structs, `OnInput` or input authority. Those are client-server concepts; in
Shared Mode each peer simulates its own objects directly, and a bot is just an object the master
ticks in `FixedUpdateNetwork`.

## What a bot is

A bot fills in a `PlayerIntent`: a world-space move direction and the one contextual button. When the
ball is within `NetPlayer.hitRange`, pressing that button hits it — direction comes from the mover's
own movement/facing, not from anything the brain names, exactly as for a human. Jump is polled
separately, at the moment it is used, so a press cannot be swallowed by the tick that read it. It moves
through the same `CharacterController` at the same speeds. If a bot ever needs a shortcut the human
does not have, the design is wrong.

## Three layers

- **steering** — where to go: for the ball, aim for a point on its far side (away from the attacking
  goal) so arriving in hit range means already moving toward goal, not sideways into it; for an
  opponent, run straight at them, since `NetPlayer`'s push/grab only finds a target in front of itself.
- **player** — a small state machine: chase the ball and hit it on contact, or tackle whichever
  opponent is about to reach the ball first. There is no carry/shoot/support split any more — there is
  nothing to carry.
- **team** — STILL NOT DONE. Exactly one bot per side is on the pitch today, so nothing yet exercises
  "two bots, one goes for the ball, the other holds a support position." Two bots converging on the
  same ball will be the loudest "these are bots" signal in a team game, same as before the reset.

## Believability is the point, and it is tuning

A bot that reacts in one tick reads as a bot immediately. The levers, in rough order of how much they
matter here:

1. **approach the ball from the right side, not head-on** — done, `approachOffset`; replaces the old
   aimed-shot-on-release, which no longer exists now that a hit is instantaneous and unaimed
2. **commitment window** — decide, then stick with it for a beat; re-deciding every tick jitters —
   done: one challenge held to the end (`_tackleFor`)
3. **input ramp** — no thumb produces a step change in direction — done, `steerRamp`
4. **reaction delay** on the chase/tackle decision, ~180–260 ms, never on movement — done,
   `reactionSeconds`
5. **top speed** just under a human's — done, `topSpeed`
6. **intercept, don't chase** — run at where the ball will be, not where it is — STILL NOT DONE, same
   as before the reset

What is GONE, because the mechanic it was tuning no longer exists: shot distance drawn per possession,
angular aim error, the miss chance, and the near/far power curve. A hit's power is a fixed constant on
`NetBall` now (`hitImpulse`) — there is nothing left for a bot to decide about how hard it hits, only
where it is standing and which way it is moving when it presses the button.

Difficulty is not one number. Reaction, positioning and aggression are separate levers, and moving
them together with a single slider is what makes bots feel cheap. Tune one believable profile first;
levels later, if ever.

None of this can be settled by reading: it needs someone playing against it and saying "too slow", "it
never scores", "it jitters". Expect several passes — more than usual right after this reset, since the
whole approach-and-hit behaviour is new and has not been felt on a phone yet.

## Two things the pitch does that are not obvious

**A harder hit goes higher, not further along the ground.** `NetBall` applies a fixed `liftRatio` to
every hit, so the impulse sets speed and climb together: the ball leaves the foot at 0.6 m and gains
roughly 0.32 x distance before gravity brings it back. A goal counts only below `goalHeight` (3 m).
Since `hitImpulse` is now a single fixed constant rather than something aimed per-shot, this mostly
matters for choosing THAT constant: too high and every hit from distance sails over the bar, same
failure mode as the old "power scales with distance" bug, just reached a different way.

**Height decides nothing about who can hit the ball.** The range check is flat-agnostic in the sense
that it does not reward height — a jump costs a bot some air control and wins nothing else. It jumps
for a high ball anyway, because somebody who never leaves the ground with the ball over their head does
not look like somebody.

## Tuning

`BotBrain`'s fields carry `[Header]` and `[Tooltip]` for the reader only. The component is added at
runtime by `BotDirector`, not authored on a prefab, so there is no inspector in the loop: **the
numbers in the file are the numbers that ship.** Change them there.

The old conversion-rate measurement (99% from 9 m, down to 55% from 20) was for the aimed, distance-
scaled shot that no longer exists. Nothing here replaces it yet — the new approach-and-hit behaviour
has not been measured against the goal at all. Do not assume a conversion rate for it; play it and
find out.

## Debug scaffolding, currently in

Bots wear a floating **BOT n** label in the match, numbered by network id so every client agrees. It
was asked for explicitly and is meant to come out again: `Scripts/NameTag.cs`, plus
`NetPlayer.UpdateNameTag` and its call in `Render`.

## Status

- [x] step 1 — `NetPlayer.Live`; forfeit counts humans
- [x] step 2 — one bot, dumbest brain (seek ball, hit toward goal), reachable from ALLENAMENTO in the
      mode menu: a private invisible room of one human, `BotDirector` puts a bot on the other side
- [x] CORE_GAMEPLAY_RESET — possession/dribble removed project-wide; `BotBrain` rewritten around
      chase-and-hit + tackle-the-nearest-threat. Not yet felt on a phone: approach angle, hit
      commit range, and the fixed hit impulse are all first guesses.
- [~] believability. Done: challenge the nearest threat to the ball (push or grab, one committed
      act), speed capped under a human's, steering ramp, reaction delay on the chase/tackle decision,
      jump, approach-from-behind-the-ball steering.
      Left: **intercept instead of chase**, and team roles once there is more than one bot — both
      unchanged from before the reset, neither was done then either
- [ ] later — a bot prefab of its own, so bots can survive the master leaving and fill a real match
