# Alfrik — the plan (agreed with the owner, 2026-10-31)

Goal: a late-game demigod that is unpredictable and fun, and a reward either way: spare him and he follows you, or
kill him and take his god weapons. One change set per test, as always (rules in `NOTES.md`).

## What the logs showed (0.9.11–0.9.12)

- His battle axe swings missed a moving player (3 of 16 landed; Odin's Charge's slashes 0 of 6). Area blows (the slam,
  the rings, the rupture, his counters) landed.
- Odin's Wrath landed 0 of 18 bolts: each fell where the target stood ~1 s before; the bolt line did not show in game.
- Moves came one at a time: once each warning is learned he is predictable.
- His bar was 66 HP until 0.9.11 (fixed), and 4000 per phase is mid-game.

## Pillars

1. **Every move threatens a moving player** — swings that turn with you, area blows, lightning called down where
   you are heading, gap closers that end in an area blow.
2. **Combos, not single moves** — openers, links and finishers, chosen by what the player is doing (keeping away,
   blocking, attacking, out of stamina), with random branches and the odd feint.
3. **Demigod** — 10000 health per phase (20000 in all, like the hardest vanilla boss), more for a group, no burst.
4. **Every move keeps its warning** — the surprise is which move comes next, not an unannounced hit.

## Roadmap

| Version | What | State |
|---|---|---|
| 0.9.13 | His hits connect: swings turn with the target (reach 3 m, 180 deg); Odin's Charge strike + impact ring; Odin's Wrath aimed ahead + a visible bolt; War Horn opens a combo; 10000 health per phase, +50% per extra player | built in the cloud |
| 0.9.15 | Owner's 0.9.14 test: phase-1 parry stuns and the leap punishes, the 6 s parry cap and parry reading, tracking axes, dodges, close-range finishers, Huginn's Mark in phase 2, anti-cheese (built things do not hurt him, no building within 40 m, **Thunder Step**, a 60 m leash) | built in the cloud |
| 0.10.0 | Phase-2 combo picker (chains below, branches, feints) | next |
| 0.10.x | **Mjölnir's Return**: Fjallbrjótr thrown past you, it comes back through you, he catches it into a Grave Ring | |
| 0.11.0 | Phase-1 combos (Charge → Twin Reaper, Ravens' Flight → the Left Fist, …) and tracking axe swings | |
| 0.12.0 | Late phase 2 (below ~30%): lightning falls about the arena, his combos run without pauses | |
| 0.13.0 | *(the local session's, not the cloud's)* **The ending**: when his last bar empties he kneels. Spare him (interact) and he joins you as now; strike him down and he dies and drops his 3 god weapons — Hugr, Hamr, Fjallbrjótr — as player weapons. A trinket comes later. | |

### Phase-2 combo ideas (0.10.0)

- **Horn of the Hunt** — War Horn (slows) → Odin's Charge → impact ring → slashes. *(in 0.9.13)*
- **Storm Call** — War Horn → Odin's Wrath barrages, aimed ahead → Thunder Step beside you → Executioner.
- **Mountain Breaker** — Earthsplitter → Odin's Fall to the end of the rupture → landing rings → FURY.
- **Wall and Answer** — Iron Advance → the bash → Reaping Sweep.
- **Mjölnir's Return** — hammer thrown past you, back through you, caught into a Grave Ring.

### Open questions for later

- The weapons in a player's hands: only Alfrik has the held-model code (HeldAxes); players need it too, and late-game
  stats for each.
- Hugr and Hamr: two one-handed axes, or the pair as one dual-axe item?
- The trinket's effect (idea: *Odin's Fist* — full adrenaline: no stagger and lightning on your hits for a few s).
