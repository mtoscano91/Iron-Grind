# Story 006: Zone Composition Wiring

> **Epic**: Mob Spawning
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-5 (bus lifetime), CR-MS-7 (T-2), CR-MS-8 (teardown), Interactions table; `design/gdd/enemy-ai.md` Dead row (`DEATH_LINGER_TICKS` despawn)
**Requirement**: `TR-ms-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Phase 3 steps are listed in one place in the composition root (`AddPhaseThreeSteps`); construction order is independent of step order.

**Also governed by**: ADR-010 (one `MobEventBus` per zone, created at S5; `MobSpawned` subscribers run in subscription order: runtime store before Enemy AI), ADR-002 (Navigation phases 1–2 run before every phase 3 system; Navigation claims an agent on `MobSpawned`), ADR-012.

**Epic default (`/create-stories` 2026-11-16, revisit at readiness)**: the spawner's step is the **first game-system step of phase 3** — after `ZoneInstancingWiring` and `NavigationPhaseStep` (phases 1–2), before Status Effects and Enemy AI — so a mob spawned on tick T has its runtime store entry and `MobController` before Enemy AI's `Run` on tick T.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (composition code; NavMesh already wired by Navigation)
**Engine Notes**: none new.

**Control Manifest Rules (this layer)**:
- Required: phase 3 order listed explicitly in the composition root — ADR-014
- Forbidden: an `EventBus` class; `async` in tick code — ADR-010, ADR-011

---

## Acceptance Criteria

- [ ] **AC-1 Build at S5**: `ZoneServerComposition` builds the spawner (via Story 005's loader) on the zone's `MobEventBus`, `wiring.MobRoster`, the S2 mob registry, `MobSpawningConfig` and `EnemyAIConfig.DeathLingerTicks`; it is exposed as a test accessor. A load failure (Story 005) fails S5 the existing way.
- [ ] **AC-2 Teardown seam**: the spawner is passed as `ZoneInstancingWiringOptions.MobSpawnTimers`, so production no longer uses `LoggingZoneMobSpawnTimers`; the zone close path calls `CancelAllSpawnTimers` at step 1 (assert on the built composition).
- [ ] **AC-3 T-2**: the T-2 transition (`StateOwner.MarkActive`) runs `PopulateOnActive()` once; every valid spawn point is Live and has a runtime store entry and a Dormant Enemy AI controller after it.
- [ ] **AC-4 Phase order**: `AddPhaseThreeSteps` lists the spawner step per the epic default (or the readiness decision), with a comment; a composition test asserts the step order.
- [ ] **AC-5 Same-tick corpse removal**: in the built composition, a mob killed on tick T (Combat `OnMobDied` → Enemy AI bridge → `MobEventBus.MobDied`) has its roster slot released (`RemoveMob`) and its Enemy AI controller / runtime store entry removed on the **same** tick (T + `DEATH_LINGER_TICKS`); neither happens on an earlier tick. If the counts differ by one, fix the off-by-one on the Mob Spawning side and record it.
- [ ] **AC-6 Respawn**: after the corpse removal, the point respawns after its tier delay with a new EntityID; the new mob has store stats and a controller.
- [ ] **AC-7 Close**: closing the zone after spawns → no `TryAddMob`, `RemoveMob` or `MobSpawned` after step 1.
- [ ] **AC-8 Existing tests**: Enemy AI composition tests that raise `MobSpawned` by hand still pass (they may now also see table spawns — update them so a test fixture with an empty or test spawn table keeps them deterministic).

---

## Implementation Notes

- Composition entry points: `BuildMobStoreAndNavigation`, `BuildCombatAndEnemyAI`, `AddPhaseThreeSteps` (`src/ServerLogic/ZoneServer/ZoneServerComposition.cs` ~1988–2130); `MarkActive` at ~1296; `ZoneInstancingWiring` `MobSpawnTimers` option at ~123 / ~489.
- Bus subscription order on `MobSpawned`: runtime store, then Enemy AI, then Navigation (existing). Mob Spawning only raises; it subscribes to `MobDied` only.
- Enemy AI despawn rule (Story 011): despawn at the first `Run` with `currentTick − DeadEnteredTick ≥ DeathLingerTicks`; for the `OnMobDied` path `DeadEnteredTick` is the tick of the last `Run` (same tick T). Mob Spawning receives `MobDied` during Combat's step on T, and decrements from 30 on ticks T+1…T+30 → `RemoveMob` on T+30, before Enemy AI's `Run` on T+30. Verify, do not assume.
- Test compositions that pass a navigation seam have no Enemy AI; decide per test whether a spawn table is supplied (seam or override, pattern of `MobDefinitionRegistryOverride`).

---

## Out of Scope

- Story 007: the real starting-zone table content
- Story 008: the PlayMode end-to-end run with real NavMesh and a player kill

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`. EditMode composition rig as in `EnemyAI_Composition_tests.cs`, with a test spawn table override.*

1. **S5 build (AC-1)**: the accessor is non-null after S5; with a 151-entry table S5 fails with `SpawnTableOverCapacity`.
2. **Teardown seam (AC-2)**: the wiring's timers object is the spawner, not `LoggingZoneMobSpawnTimers`.
3. **T-2 (AC-3)**: 5-point table → after `MarkActive` 5 roster mobs, 5 store entries, 5 Dormant controllers.
4. **Step order (AC-4)**: the listed phase 3 step types match the documented order.
5. **Same-tick removal (AC-5)**: kill a mob on tick T → on T+29 slot and controller both present; on T+30 both gone.
6. **Respawn (AC-6)**: Common mob → new EntityID Live at T + 30 + 60; store and controller present for the new id.
7. **Close (AC-7)**: close after spawns → spawner call log empty after the step-1 marker.
8. **Regression (AC-8)**: Enemy AI composition suite passes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_Composition_tests.cs` — must exist and pass; batch EditMode suite green

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003, Story 004, Story 005
- Unlocks: Story 008
