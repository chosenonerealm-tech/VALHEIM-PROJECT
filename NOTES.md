# Alfrik — Valheim boss plugin

Alfrik, "The Left Fist of Odin": a Vikings for Hire viking with a VRM avatar (ValheimVRM), summoned at Odin's
Challenge Stone. He fights in two boss phases, then joins the player who beat him as a companion.

- Phase 1, **The Berserker**: Hugr & Hamr (dual axes, Jotunn copy of `AxeBerzerkr`). Fast, every attack blockable and
  parryable, a perfect parry staggers him. Moves in `Brain.cs`.
- Phase 2, **The Unbroken**: Fjallbrjótr (great hammer, copy of `SledgeIron`, prefab id kept as `Alfrik_Skeggold`).
  Slower, never staggered, nothing of his can be blocked or parried, every hit that hurts staggers you. Moves in
  `Brain.Phase2.cs`.
- Companion: VfH level 10 on joining; chooses hammer or axes for the fight; the unused weapon hangs on his back.

## Layout

| Path | What |
|---|---|
| `Alfrik/` | Plugin source (C#, net48, BepInEx 5 + HarmonyX + Jotunn). `Alfrik.csproj` builds from `../refs`. |
| `refs/` | Game/mod DLLs to compile against. **Not in git while the repo is public** — ask the owner. |
| `decomp/` | Decompiled Valheim, Vikings for Hire and ValheimVRM classes for reference. **Not in git while public.** |
| `logs/` | Trimmed BepInEx logs from real test fights (newest first by name). Read these before changing behaviour. |

Build: `cd Alfrik && dotnet build -c Release` → `Alfrik/bin/Release/Alfrik.dll`.

## Dependencies (the owner's setup — build and test against these)

| What | Version | How Alfrik uses it |
|---|---|---|
| Valheim | Unity 6000.0.75f1 build (game DLLs in `refs/`) | everything |
| BepInExPack Valheim | 5.4.2350 (BepInEx 5.4.23.5) | plugin loader, HarmonyX patches |
| Jotunn | 2.30.2 | **hard dependency** (`[BepInDependency]`): items, piece, status effects, console command, synced admin config |
| Vikings for Hire (+ Companions, Settlement, Factions, Traders, World Events) | 1.6.6 | **soft, required in practice**: Alfrik *is* a VfH viking. Reached only by reflection (`ValheimNpc.VikingNpc`, `ValheimNpc.VikingSpawner`, `ValheimNpc.Plugin` config). VfH loads *after* Alfrik, so its types are looked up in `Start()`, not `Awake()`. Any VfH update can rename members → check `decomp/vikingsforhire/` against the new version. |
| ValheimVRM | 1.3.12 (its DLLs sit in `valheim_Data/Managed`) | the avatar (`Alfrik.vrm` in `Valheim/ValheimVRM/`), `VRMAnimationSync`; UniGLTF loads the weapon `.glb` models |

`[NetworkCompatibility(EveryoneMustHaveMod, Minor)]`: host and every client need the same minor version — the owner and
a friend (who uses a ready-made copy of the plugins folder) are kept in sync by the local session.

## Compatibility with the owner's other mods (66 loaded; full list in the newest `logs/` file)

Watch these when touching related code:
- **Performance mods** (Valheim Performance Optimizations 2.0.0, Performance Booster 7.7.7, SteadyFrame 1.1.0,
  NetworkPerformanceSystem 1.18.1, M182 Big Building Performance): may cull or throttle animators/updates — the
  anti-shake fix forces his animator to `Normal`/`AlwaysAnimate` every frame (`Boss.KeepAnimating`).
- **Attack Cancel 1.4 / Attack Cancel Dodge 1.1, Digitalroot's Slope Combat Assistance 2.0.30**: change the player's
  attacks and blocking — his parry/stagger logic (`Brain.BlockingHim`, `HitDelay`) reads the player's current attack.
- **Better Archery 2.0.5, MT Magia/MT Espada (Mushoku Tensei)**: player projectiles and spells → Iron Wall / Returned
  Shot (`ReturnedShot.cs`) must cope with any `HitData.m_ranged` hit.
- **WeaponHolsterOverhaul 1.2.2, WeaponAdditions, ValheimArmory**: weapon visuals/holsters (players) — keep
  `HeldAxes`/`BackWeapons` limited to Alfrik.
- **Set Compat Patch 1.51.0**: the owner's own compatibility plugin (separate project, not in this repo).
- **ValheimCommunityPatch 0.39.0, Expand World Data 1.79, MT Monstruos**: world/creature changes — his AI and spawn
  (sky dive onto `ZoneSystem.GetSolidHeight`) must not assume vanilla terrain.

## How work flows

1. The cloud session edits `Alfrik/` on a branch and pushes it.
2. The local session pulls, builds, reviews the diff, installs the DLL (only while Valheim is closed) into the
   owner's game and the friend's folder, and after a test pushes the trimmed log to `logs/`.
3. The owner plays and reports.

## Rules (from the owner — follow them exactly)

- **Bump `Plugin.Version`** in `Plugin.cs` with every change set.
- **Keep the log diagnostics.** Every ability logs its name (`Alfrik: <Ability>`), outcomes log too (`… catches N`,
  `… returns X's shot`), plus the 15 s damage/swing reports and the after-swing traces. New moves must log the same way:
  the owner does not want to test repeatedly, the logs are how changes are verified.
- **Describe abilities without counterplay**: no counters, weaknesses or punish windows in descriptions or comments
  meant for the owner.
- **No recycled assets.** No vanilla/mod models, textures or sounds as his look/sound, except where the owner asked
  (the vanilla sledge's own slam effect). New assets: CC0/CC-BY only (OpenGameArt, Sketchfab CC-BY), credited in
  `CREDITS.txt` in the plugin folder.
- **No regressions**: change one thing at a time, keep each change small, don't rewrite working systems. The owner
  rejects "config toggles instead of fixes".
- **Never** T-poses, odd poses or shaking. The shake was fixed in 0.9.2 by `Animator.updateMode = Normal` /
  `AlwaysAnimate` (`Boss.KeepAnimating`) — don't touch it.
- Phase names: phase 1 "The Berserker", phase 2 "The Unbroken". Same HP and armour in both phases.
- Analysis first when the owner says "don't code yet".

## Architecture notes

- Two bodies: the hidden VfH viking body runs animation/logic; ValheimVRM copies its HumanPose to the visible VRM
  avatar every LateUpdate. Held/back weapons are laid on the **avatar's** bones in `Application.onBeforeRender`
  (`HeldAxes`, `BackWeapons`).
- Multiplayer: the owner of his ZNetView runs Brain/Boss; visuals go out by routed RPCs (`Alfrik_Fx`, `Alfrik_Wave`,
  `Alfrik_Split`, `Alfrik_Strike`, `Alfrik_Return`, …); only the owner deals damage.
- Vikings for Hire is reached by reflection (`ValheimNpc.VikingNpc`), ZDO keys `vnpc_*`; Alfrik's own keys `sc_alfrik*`.
- Warning poses are additive muscle pushes (`Poses.cs`), eased in/out.

## Current state (0.9.12)

0.9.12 (cloud, not built here; branch `cloud/phase2-fixes`, on top of 0.9.11, which is not yet played either):
- Jumpable ground waves: Grave Ring, War Stomp and Odin's Fall's landing burst outward in stepped rings (`Quake` in
  `GroundWave.cs`, RPC `Alfrik_Quake`); Earthsplitter stays a line. A wave catches only those on the ground as it
  reaches them (`GroundWave.Airborne`: own character `IsOnGround`, others by synced height).
- Range loop: Odin's Wrath (cd 6 s) is his main move at range; once the target is worn down (local player's stamina
  under 30% after a barrage, or 3 barrages) he does Odin's Charge (burst run, up to 3 slashes in reach).
- He never swings at the air: FURY / Reaping Sweep close in first; Executioner and the phase-2 bash are skipped out of
  reach.
- War Horn: short hold (0.8 s) and +30% speed for him for 6 s.
- Odin's Wrath's bolt and light fade out over 1.2 s instead of blinking.
- Phase-2 Shoulder bash reaches as far as the battle axe swing (it hit 0 of 4 in the 0.9.11 log).
- New log lines: "Alfrik's Grave Ring / War Stomp / Odin's Fall landing / Earthsplitter catches N (M jumped it)",
  "Alfrik: Odin's Charge at X m (stamina N% | 3 barrages)", "Alfrik's Odin's Charge slash N lands on N",
  "… ends after N of 3 blows: target out of reach", "Executioner: target out of reach … no chop",
  "War Horn slows N; he is quickened for 6 s".

0.9.11 (cloud, not built here; branch `cloud/phase2-fixes`, on top of 0.9.10):
- Fjallbrjótr: a battle axe's swing (borrowed from vanilla `Battleaxe`, its sounds left out) is his normal attack; the
  sledge slam is his heavy blow, used only in big moves (Executioner, Earthsplitter, Grave Ring, FURY's last blow,
  counters). The sledge's shockwave effect now plays with the slam only.
- Blows synced to the swing's hit frame (`Humanoid.OnAttackTrigger`): phase-2 Shoulder bash, Earthsplitter rupture,
  Grave Ring; none of them fires if the swing never started.
- Max health kept at [Boss] PhaseHealth (the last logs showed a bar of ~66); phase start logs his health.
- Retribution is a stance (no new moves while it lasts; out-of-reach strikers kept); its end is logged.
- Odin's Fall keeps his body kinematic and unsmoothed in the air, even if Vikings for Hire lets go of it.
- Phase-2 pacing: the next move waits 2.5-4 s after the last one ends.
- New log lines: "Fjallbrjotr swing: …", "Alfrik's pace, 15 s: …", "Alfrik's <move> lands on N",
  "Alfrik's Earthsplitter catches N", "… rupture goes out / Grave Ring is marked (with the blow | by time)",
  "Alfrik's Shoulder Breaker hits N", "Alfrik's Retribution ends: …", "Alfrik's max health was …",
  "Alfrik's leap: his body was let go mid-air …", "Skeggold hold: … in a swing|slam".

0.9.10, played (`logs/2026-10-31_0112.log`): the unused phase-2 Bleed (`Bleed.cs`, [Boss] BleedPerSecond/BleedSeconds) and Bearded
Hook (`HookPull.cs`, `HookChain.cs`, its pose and effect) removed; the stun on every blow that hurts replaces the
bleed. Their files in the plugin folder (`Audio/p2bleed*`, `Audio/p2chain*`, the `p2_blood` texture) are now unused.

0.9.9, built and installed, not yet played: the vanilla sledge slam effect/sound restored and its area 1.5× wider,
weapons on his back, companion weapon choice. Last played: 0.9.8 (`logs/2026-10-29_v0.9.8_sledge-fight.log`).
