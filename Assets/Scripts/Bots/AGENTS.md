# Bots

Everything that plays without a human behind it lives in this folder. The rest of the game reaches in
through exactly two names, and no others: `BotDirector.Wanted` / `BotDirector.Ensure`, called from
`NetLauncher`. The interface a brain implements (`IPlayerBrain`) deliberately lives OUTSIDE this
folder, next to the player contract it belongs to, so that this folder holds implementations only.

Written in English to match the code comments; the surrounding conversation is in Italian.

## The seams

The rest of the codebase was shaped so a bot needs no special case anywhere. Keep it that way.

| seam | contract |
|---|---|
| `NetPlayer.Live` | every player in the match, humans and bots. The ball picks candidates from here, never from `Runner.ActivePlayers` — that list only knows about peers with a connection. |
| `NetBall.OwnerId` | a `NetworkId` identifying the player **object**, not the person. This is what lets something with no `PlayerRef` carry the ball. Invalid means free. Resolve it with `Runner.TryFindObject`, which answers for anything spawned — `TryGetPlayerObject` by definition cannot. |
| `NetBall.Kick(NetPlayer, dir, power)` | authority-side entry point. A remote carrier asks over `RPC_Kick`, which only resolves the sender; whoever already holds the ball's authority — every bot, since bots exist only on the master — calls it directly. Both go through `NetPlayer.Shoot`. |
| `PlayerIntent` + `IPlayerBrain` | the four values a tick needs, in WORLD space. `NetPlayer.ReadIntent` returns either the joystick resolved against the camera, or `_brain.Think`. Everything below that line — acceleration, turning, possession, push, grab, kick — is shared by construction. |

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

Using the human prefab is what keeps a bot honest: same collider, same speeds, same possession rule,
same kick. It has one consequence, and it is the reason bots are confined to practice for now — that
prefab is flagged `DestroyWhenStateAuthorityLeaves`, so **a bot dies with the master**. In a practice
match the master is the only human and the match ends anyway. Filling a real 2v2 needs a prefab of
its own, flagged `MasterClientObject` so Fusion migrates it; the human prefab must NOT change, because
your avatar should vanish when you quit.

One deliberate side effect of sharing the human's contextual button: if a bot loses the ball during
the fraction of a second it holds the kick, the release lands in the no-ball branch and comes out as a
push. That is the same code a person runs, and it is left alone.

Do not reach for input structs, `OnInput` or input authority. Those are client-server concepts; in
Shared Mode each peer simulates its own objects directly, and a bot is just an object the master
ticks in `FixedUpdateNetwork`.

## What a bot is

A bot fills in a `PlayerIntent`: a world-space move direction, the one contextual button, and — for
the tick the button is released — where the kick goes and how hard. Jump is polled separately, at the
moment it is used, so a press cannot be swallowed by the tick that read it. It moves through the same
`CharacterController` at the same speeds, and it takes the ball by the same proximity rule. If a bot ever needs a shortcut the human
does not have, the design is wrong.

## Three layers

- **steering** — where to go: intercept the ball's predicted position, arrive without overshooting,
  keep off a team mate's toes.
- **player** — a small state machine: go to ball, carry, shoot, support, fall back.
- **team** — exactly one bot per side is on the ball; the other takes a support position. Two bots
  converging on the same ball is the loudest "these are bots" signal in a team game.

## Believability is the point, and it is tuning

A bot that reacts in one tick and aims perfectly reads as a bot immediately. The levers, in rough
order of how much they matter here:

1. **intercept, don't chase** — run at where the ball will be, not where it is
2. **commitment window** — decide, then stick with it for a beat; re-deciding every tick jitters
3. **input ramp** — no thumb produces a step change in direction
4. **reaction delay** on decisions, ~180–260 ms, never on movement
5. **aim error** growing with distance, and the occasional real mistake
6. **top speed** just under a human's

Difficulty is not one number. Reaction, aim, speed, aggression and positional discipline are
separate levers, and moving them together with a single slider is what makes bots feel cheap. Tune
one believable profile first; levels later, if ever.

None of this can be settled by reading: it needs someone playing against it and saying "too slow",
"it always robs me", "it jitters". Expect several passes.

## Status

- [x] step 1 — `OwnerId` identifies an object; `NetPlayer.Live`; forfeit counts humans
- [x] step 2 — one bot, dumbest brain (seek ball, shoot at goal), reachable from ALLENAMENTO in the
      mode menu: a private invisible room of one human, `BotDirector` puts a bot on the other side
- [ ] step 3 — believability: intercept, ramp, delay, commitment, team roles
- [ ] later — a bot prefab of its own, so bots can survive the master leaving and fill a real match
