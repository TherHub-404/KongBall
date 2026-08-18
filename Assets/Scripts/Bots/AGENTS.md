# Bots

Everything that plays without a human behind it lives in this folder. Nothing else in the project
may reference a type from here: the rest of the game must not be able to tell a bot from a person.

Written in English to match the code comments; the surrounding conversation is in Italian.

## The three seams

The rest of the codebase was shaped so a bot needs no special case anywhere. Keep it that way.

| seam | contract |
|---|---|
| `NetPlayer.Live` | every player in the match, humans and bots. The ball picks candidates from here, never from `Runner.ActivePlayers` — that list only knows about peers with a connection. |
| `NetBall.OwnerId` | a `NetworkId` identifying the player **object**, not the person. This is what lets something with no `PlayerRef` carry the ball. Invalid means free. Resolve it with `Runner.TryFindObject`, which answers for anything spawned — `TryGetPlayerObject` by definition cannot. |
| `NetBall.Kick(NetPlayer, dir, power)` | authority-side entry point. Humans reach it through `RPC_Kick`, which only resolves the sender; a bot calls it directly. |

Plus one rule that is easy to get wrong: **the forfeit check counts humans, not players**
(`MatchController.TeamAbandoned`). A side whose only human left is out even if its bot is still
standing there, otherwise the match plays itself.

## Authority

Bots are spawned and simulated by the Shared Mode master, and their prefab is a **Master Client
Object** so Fusion migrates them when the host leaves. The human prefab must stay
`DestroyWhenStateAuthorityLeaves` — your avatar should vanish when you quit — which is why bots need
a prefab of their own rather than a flag on `NetPlayer`.

Do not reach for input structs, `OnInput` or input authority. Those are client-server concepts; in
Shared Mode each peer simulates its own objects directly, and a bot is just an object the master
ticks in `FixedUpdateNetwork`.

## What a bot is

A bot produces the same four values `NetPlayer` reads from a human: `GetMove()`, `GetActionHeld()`,
`ConsumeJump()`, `GetAimDelta()`. It moves through the same `CharacterController`, at the same
speeds, and it takes the ball by the same proximity rule. If a bot ever needs a shortcut the human
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
- [ ] step 2 — one bot, dumbest brain (seek ball, shoot at goal), to prove it can hold, be robbed and score
- [ ] step 3 — believability: intercept, ramp, delay, commitment, team roles
