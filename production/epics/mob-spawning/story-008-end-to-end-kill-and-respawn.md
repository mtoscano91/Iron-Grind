# Story 008: End-to-End — Kill a Spawned Mob, See It Respawn

> **Epic**: Mob Spawning
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4–5 h (readiness 2026-11-16: hand-built PlayMode rig with real nav, combat and spawner)
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — Overview / Player Fantasy (the field never runs out), F-MS-2 (cycle time = kill time + `DEATH_LINGER_TICKS` + respawn delay), CR-MS-4 full cycle
**Requirement**: `TR-ms-008` (end-to-end part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract
**ADR Decision Summary**: Navigation runs phases 1–2 before phase 3; agents are claimed on `MobSpawned` and released on `MobDied`; the real `NavMeshAgent` pool is exercised only in PlayMode.

**Also governed by**: ADR-010, ADR-014.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (real `NavMeshAgent` pool and baked NavMesh, as in the Enemy AI tick-budget fixture)
**Engine Notes**: PlayMode with the starting-zone NavMesh; follow `tests/PlayMode/EnemyAI/EnemyAI_TickBudget_tests.cs` and the Navigation PlayMode fakes for setup; batch PlayMode run with `-testFilter`.

**Control Manifest Rules (this layer)**:
- Guardrail: server tick budget — log the spawner step's cost over the run (no hard budget assert unless readiness sets one)

---

## Acceptance Criteria

- [x] **AC-1 Populated on Active**: the hand-built rig (Implementation Notes), loaded through `MobSpawningZoneLoader.TryLoad` with the shipped `ZoneDefinition_StartingZone` (Story 007 table) and the shipped `MobRoster` registry, has every table point Live after `PopulateOnActive()` (T-2), each with a claimed nav agent: `ZoneNavigationService.ActiveCount` = `GetMobCount` = spawner `PointCount` = 48.
- [x] **AC-2 Kill**: a player auto-attacks a Common mob to death through the real `AutoAttackCombatSystem` → XP awarded once (counting XP sink), loot stand-in logs (TD-107), `MobEventBus.MobDied` raised once, the spawn point (`GetPoint`) Dying. The player survives the fight (see Implementation Notes).
- [x] **AC-3 Corpse removal**: the nav agent is released on `MobDied` (the kill tick T, ADR-002 / CR-NAV-6; corrected at `/story-done` 2026-11-17); after `DEATH_LINGER_TICKS` the roster slot and the Enemy AI controller / store entry are all released on the same tick (T + linger); `GetMobCount` drops by one.
- [x] **AC-4 Respawn**: after the Common delay a new mob (new EntityID, same `MobTypeID`, authored position) is Live, Dormant, with a claimed agent; `GetMobCount` is back to the table count. Total cycle in ticks = kill tick + 30 + 60 (F-MS-2 at defaults).
- [x] **AC-5 Many cycles**: ≥ 10 kills across Common and Uncommon points (respawn delays 60 / 120 ticks at defaults), bulk kills by setting HP 0 through Character Stats (CR-AI-13 path), run until last kill + 30 + 120 ticks → no leaked roster slots or nav agents (`GetMobCount` = `ActiveCount` = 48, every point Live); no `LogType.Error` / `Exception` logs.
- [x] **AC-6 Close**: with some points Dying and some Respawning, the rig runs the close order: (1) `CancelAllSpawnTimers(zone)`, (2) `ZoneNavigationService.Teardown()`, (3) N further ticks of every step (N ≥ the Uncommon delay) → no `OnMobSpawned` after (1), every point Cancelled, `ActiveCount == 0` after (2). (Teardown clears the agent pool; agents are not returned to it.)

---

## Implementation Notes

- **Rig (readiness 2026-11-16): hand-built, not the full composition.** `ZoneServerComposition` cannot be built from PlayMode (its rig and `Su*` / `Wi*` fakes live in the EditMode assembly; `MobSpawner` is internal to the composition). Build, in the style of `tests/PlayMode/EnemyAI/EnemyAI_TickBudget_tests.cs`:
  - real `ZoneNavigationService` on `Assets/NavMesh/Zone0_StartingZone.asset` (real `NavMeshAgent` factory);
  - real `ZoneSlotPool`, `ZoneMobRoster`, `MobEventBus`, `MobRuntimeStore`, `EnemyAISystem` and `AutoAttackCombatSystem`, wired as in `tests/EditMode/EnemyAI/EnemyAI_CombatSeams_tests.cs` (Combat `OnMobDied` → Enemy AI death bridge → loot → `MobDied`);
  - the spawner from `MobSpawningZoneLoader.TryLoad` (shipped definition and registry, `MobSpawningConfigValues.Defaults`, `deathLingerTicks` from the same `EnemyAIConfigValues` Enemy AI is built with);
  - per tick, the Story 006 phase order: navigation phase step → spawner `Tick()` → Enemy AI `Run` → combat → one frame.
- **Internals access (user decision 2026-11-16, option a):** add `[assembly: InternalsVisibleTo("IronGrind.PlayModeTests")]` to `src/ServerLogic/AssemblyInfo.cs`, next to the EditMode grant, with a comment that it is test-only (the file's rule forbids grants between production assemblies only). This exposes `GetPoint`, `PointCount`, `LiveCount` and `DeferredCount` to the test.
- **AC-2 survival:** a Common mob aggroes and attacks back. Give the player enough HP for the whole fight, or build Enemy AI with a no-op attack resolver as in `EnemyAI_CombatSeams_tests`; record which in the Completion Notes. XP is counted through a counting experience sink.
- Kills: AC-2 uses the real Combat path; the bulk kills in AC-5 set HP 0 through Character Stats (CR-AI-13 path).
- Log the spawner step's max / p95 tick cost as `[MobSpawning008]` (no budget assert).

---

## Out of Scope

- Client rendering of spawns / despawns (presentation epics)
- Loot drops (TD-107)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refined at `/story-readiness` 2026-11-16.*

1. **Populated (AC-1)**: after T-2, `GetMobCount` = `PointCount` = `ActiveCount` = 48.
2. **Kill (AC-2)**: real auto-attack kill of a Common mob → XP once, `MobDied` once, point Dying.
3. **Corpse (AC-3)**: at T the agent is released (`ActiveCount` − 1) and the point is Dying; at T+29 slot, controller, store entry present; at T+30 all released, count − 1, `ActiveCount` unchanged.
4. **Respawn (AC-4)**: at T+90 new EntityID at the authored position, Dormant, agent claimed; count restored.
5. **Soak (AC-5)**: 10+ kills over Common and Uncommon points, run until all respawns fire → counts equal, 0 `LogType.Error/Exception`.
6. **Close (AC-6)**: close with points Dying and Respawning → no `MobSpawned` after `CancelAllSpawnTimers`; every point Cancelled; `ActiveCount == 0` after navigation `Teardown`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/MobSpawning/MobSpawning_EndToEnd_tests.cs` — must exist and pass (batch PlayMode)

**Status**: [x] Created — 6 `[UnityTest]`, batch PlayMode `MobSpawning|EnemyAI|Navigation` 36/36

---

## Dependencies

- Depends on: Story 006, Story 007
- Unlocks: None (epic complete)

---

## Completion Notes
**Completed**: 2026-11-17
**Criteria**: 6/6 passing (none deferred)
**Deviations** (advisory):
- AC-3 / QA 3 text corrected: Navigation releases the agent on `MobDied` (tick T, ADR-002), not at T + linger; slot, controller and store entry go at T + linger as specified.
- Damage: a test stand-in (`HpAwareCalculator`) replaces `DamageCalculator` (its weapon / item fakes are EditMode-only); Combat's kill, XP award, Enemy AI death bridge, loot stand-in and `MobDied` are real.
- Survival (AC-2): no aggro candidates and a no-op mob attack resolver, so mobs never attack.
- Tick order includes Enemy AI's integration step after combat (as production); the story's order list omitted it.
- Rig: hand-built (readiness decision); `ZoneServerComposition` is not reachable from PlayMode.
- Scope: test-only `InternalsVisibleTo("IronGrind.PlayModeTests")` in `src/ServerLogic/AssemblyInfo.cs` (user decision, option a) and a one-line ADR-012 Decision 5 amendment.
- `[MobSpawning008]` spawner `Tick()` max 2.10 ms, p95 0.0004 ms (192 ticks, Editor batch mode; no budget assert).
**Test Evidence**: Integration: `tests/PlayMode/MobSpawning/MobSpawning_EndToEnd_tests.cs`, 6/6; batch PlayMode `MobSpawning|EnemyAI|Navigation` 36/36 (end 2026-11-17 00:45:54Z, 0 `error CS`); EditMode `!Database` 7834/7834 (00:40:16Z, after the AssemblyInfo change)
**Code Review**: Complete (`/code-review` 2026-11-17: CHANGES REQUIRED, all 6 items + suggestions applied)
