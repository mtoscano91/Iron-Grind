# Story 006: Zone Composition Wiring

> **Epic**: Mob Spawning
> **Status**: Complete
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

- [ ] **AC-1 Build at S6 (readiness 2026-11-16)**: the spawner cannot be built at S5. The `ZoneDefinitionSlot` is filled by the scene loader during S6, and the NavMesh exists only after `_navigation.Initialize`. So:
  - **S5** builds two placeholders that do nothing until S6 sets the spawner: a forwarding `IZoneMobSpawnTimers` and a phase 3 step adapter (AC-2, AC-4).
  - **S6 part 2** (`StartupFinishScene`), after `_navigation.Initialize` and after bounds validation passes, calls `MobSpawningZoneLoader.TryLoad` with:
    - the zone's `MobEventBus`, `wiring.MobRoster` and the S2 mob registry (`MobRegistry`);
    - a `SpawnPointValidator` (see Implementation Notes for the query source);
    - `MobSpawningConfigValues` (`Defaults` unless the override seam is set);
    - `deathLingerTicks` from the **same** `EnemyAIConfigValues` instance Enemy AI is built with (today `EnemyAIConfigValues.Defaults`; one field or local, read once, not two `Defaults` calls).
  - The built spawner is exposed as a test accessor and set on both placeholders.
  - If `TryLoad` returns false, S6 returns false: S-fail at S6 the existing way, and teardown step 3 still runs because `MarkNavigationInitialized` was set first.
  - A composition with no definition slot (EditMode rigs) builds no spawner and logs nothing. The placeholders stay no-ops.
- [ ] **AC-2 Teardown seam**: the S5 forwarder is passed as `ZoneInstancingWiringOptions.MobSpawnTimers`, so production no longer uses `LoggingZoneMobSpawnTimers`. After S6 the forwarder points at the spawner, and the close path's step 1 reaches `CancelAllSpawnTimers` (assert on the built composition). If S6 failed before the spawner was set, `CancelAllSpawnTimers` on the forwarder does nothing and logs nothing.
- [ ] **AC-3 T-2**: `StartupSubscribeTick` (S9) calls `PopulateOnActive()` once, right after `StateOwner.MarkActive(_zoneId)` and before `_network.SubscribeTick`, so the first tick sees Live points. Every valid spawn point is then Live, with a runtime store entry and a Dormant Enemy AI controller. With no spawner, nothing happens.
- [ ] **AC-4 Phase order (epic default confirmed at readiness)**: `MobSpawningService` is not an `IZoneSimulationStep`, so a small adapter step (built at S5, calls `Tick()` once the spawner is set, otherwise does nothing) goes in `AddPhaseThreeSteps`:
  - Position: after `navigationStep`, before `statusEffects`, with a comment citing ADR-014 / ADR-010.
  - It is added in every composition, including those with a navigation seam (it just never ticks a spawner there unless one was built).
  - A composition test asserts the listed step order.
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
- **Phase placement (readiness 2026-11-16)**: `ZoneServerHost` passes the same `ZoneDefinitionSlot` to `UnitySceneLoader` (fills it at S6) and to the bounds validator. `StartupFinishScene` is the only place where both the definition and the NavMesh exist before S9. Keep the `TryLoad` call there, after the bounds check (a T-3 zone never loads its table).
- **Spawn point validator query source**: production builds `new SpawnPointValidator(new UnityNavMeshQuery(), navigationConfig)`, using the same `NavigationConfig` the navigation service gets. Add a test seam on the composition (`SpawnPointValidatorOverride`, set before the run starts, null in production, pattern of `NavMeshInstanceOpsOverride`) so EditMode rigs can pass a validator over Navigation's `FakeMeshQuery`. With a navigation seam and no override, build no spawner (same as no slot).
- **Overrides**: `MobSpawningConfigOverride` (values) is optional. Tests set the table through `ZoneDefinition.SetSpawnTableForTests` on a definition placed in the slot the test passes.
- **Forwarder / adapter**: small internal classes in `ZoneServer` (or private nested), each with a `Spawner` setter. No new interface and no `EventBus` class (manifest).

---

## Out of Scope

- Story 007: the real starting-zone table content
- Story 008: the PlayMode end-to-end run with real NavMesh and a player kill

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`. EditMode composition rig as in `EnemyAI_Composition_tests.cs`, with a test spawn table override.*

1. **S6 build (AC-1)**:
   - The accessor is null after S5 and non-null after S6.
   - With a 151-entry table, S6 fails (S-fail, `SpawnTableOverCapacity` logged) and teardown step 3 runs.
   - With no definition slot: no spawner and no log.
   - The spawner's `deathLingerTicks` equals the Enemy AI config's value.
2. **Teardown seam (AC-2)**:
   - The wiring's timers object is the forwarder, not `LoggingZoneMobSpawnTimers`, and it points at the spawner after S6.
   - Failed-S6 close: S6 fails, then the zone closes. `CancelAllSpawnTimers` on the forwarder does nothing and logs nothing (`LogAssert.NoUnexpectedReceived`).
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

**Status**: [x] Created — 21/21 passing

---

## Dependencies

- Depends on: Story 003, Story 004, Story 005
- Unlocks: Story 008

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 8/8 passing (none deferred)
**Deviations**:
- Advisory: `ZoneDefinition_StartingZone.asset` has no spawn table yet, so the real host S-fails at S6 (CR-MS-2: a null table fails zone load) until Story 007 adds it. No test starts the real host. Do Story 007 next; commit 006 + 007 together.
- Advisory: AC-8 needed no fixture change. Enemy AI composition rigs pass no definition slot, so no spawner is built and hand-raised `MobSpawned` tests stay deterministic.
- Advisory: the linger-sharing test cannot tell "one shared `EnemyAIConfigValues`" from "both use `Defaults`" (no Enemy AI config override seam); accepted, no production seam added for it.
- Scope: test accessors `ZoneCloseSteps.Timers` / `ZoneInstancingWiring.MobSpawnTimers`; `NavCompositionRig` takes an optional `definitionSlot` and exposes `Scene`; step-order tests in EnemyAI (QA 1), MovementSystem and StatusEffects suites updated for the new step.
**Test Evidence**: Integration: `tests/EditMode/MobSpawning/MobSpawning_Composition_tests.cs`, 21/21 (EditMode with fakes; project convention, no `tests/integration/` folder); full EditMode `-testCategory "!Database"` 7825/7825 (end 23:55:35Z, 0 `error CS`)
**Code Review**: Complete (`/code-review` 2026-11-16: CHANGES REQUIRED, all 5 items applied)
