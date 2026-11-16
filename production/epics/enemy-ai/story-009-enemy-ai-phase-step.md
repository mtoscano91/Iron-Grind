# Story 009: Enemy AI Phase Step and Position Integration

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-2, CR-AI-10 (disconnected target = standard invalidation), Interactions (Mob Spawning / MobEventBus); `design/gdd/navigation-pathfinding.md` CR-NAV-11 (`GetCurrentVelocity`, XZ only)
**Requirement**: `TR-ai-002`, `TR-ai-007`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (Decision 1 phase order); ADR-014 (phase 3 order); ADR-010
**ADR Decision Summary**: Tick order is sync (1) → drain (2) → game systems read velocity (3) → integrate positions (4). Only `ZoneNavigationService` calls `Tick` / `SyncAgentPositions`. `INavigationProvider` methods (including `GetCurrentVelocity`) are called in phase 3 only. Phase 3 systems run in the order the composition lists.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`desiredVelocity` timing at 20 Hz, ADR-002)
**Engine Notes**: Use `GetCurrentVelocity` XZ only; `desiredVelocity.y` must not feed horizontal integration.

**Control Manifest Rules (this layer)**:
- Required: no module outside `ZoneNavigationService` calls `Navigation.Tick()` / `SyncAgentPositions()` — architecture invariant
- Required: `INavigationProvider` methods only in phase 3 — the integration step (phase 4) reads only the cached velocities, never the provider

---

## Acceptance Criteria

- [ ] **AC-1 Lifecycle.** `EnemyAISystem : IZoneSimulationStep, IDisposable` subscribes to `MobEventBus.OnMobSpawned` in its constructor and unsubscribes in `Dispose` (a second `Dispose` is a no-op). It is constructed after `MobRuntimeStore`, so the store has seeded the mob before the system's handler runs (bus calls handlers in subscription order).
  - On `MobSpawned`: if `store.TryGet(args.Entity, out entry)` is false (the store refused the spawn: foreign zone, duplicate id, unknown `MobTypeID`), no controller is created and nothing is logged (the store already logged). Otherwise a `MobController` is created with the registry's `MobDefinition` for `entry.MobType`, spawn point `entry.SpawnPoint`, and `IsEnraged = entry.Variant.IsEnraged`.
  - A second `MobSpawned` for an id that already has a controller creates no second controller.
  - `Remove(EntityID)` removes the controller and returns true, false for an unknown id (Story 011 calls it at despawn). `Count` returns the number of controllers.
- [ ] **AC-2 Run (phase 3).** `Run(tick, deltaTime)` ticks the controllers in spawn order: the system keeps its own list; `Remove` swap-removes, so the order is deterministic for the same event sequence. An exception from one controller is logged with its `EntityID`; that mob caches no velocity this tick and the other controllers still tick (pattern: `ZoneSimulationStep`). After a controller ticks, if its state is `Pursuing` or `Returning`, `GetCurrentVelocity(id)` is read and cached in a preallocated per-controller buffer; any other state caches nothing (the mob does not move).
- [ ] **AC-3 Integration (phase 4).** `MobPositionIntegrationStep : IZoneSimulationStep`, exposed as `EnemyAISystem.IntegrationStep`, applies the cache: for each mob with a cached velocity `v`, new position = committed + (v.x, 0, v.z) × `deltaTime` (the `Run` parameter, not a constant), Y kept from the committed position. A non-finite result (NaN / ∞ on any axis) keeps the last position and logs one warning per mob (not per tick). The new position is written to `MobController.Position` and to `MobRuntimeStore.TrySetCommitted(id, position, facing)`, so the next tick's phase 1 `CopyCommittedPositions` returns it. Facing (`MobController.FacingAngle` as `short`) is written to the store for every mob every tick, moving or not. The cache is cleared after each apply: an integration run without a preceding `Run` moves nothing.
- [ ] **AC-4 Integration test** (EditMode, fake `INavigationProvider` returning a fixed velocity with non-zero Y): a Pursuing mob moves `v.xz × deltaTime` per tick and its Y does not change; a Returning mob moves; Dormant, WindingUp and Recovering mobs do not move; controller `Position` equals the store's `CommittedPosition` after each tick; the store's `CopyCommittedPositions` returns the new position; a NaN velocity leaves the position unchanged and logs one warning over 2 ticks.
- [ ] **AC-5 Player reader.** `ZoneAggroCandidateSource : IAggroCandidateSource` is implemented over `ZoneSlotPool` (`GetPlayerEntityIDsInZone` for pool order, `TryGetPlayer` for session state) and Movement's committed positions (`MovementService.TryGetPosition`, through a narrow interface if the tests need a fake). Only players in session state `Connected` are candidates: `CopyPlayers` skips ghosts (`Disconnected_SessionActive`), `Dead` and `Respawning` players and players with no movement position; `TryGetPlayerPosition` returns false for those and for unknown ids (the controller then applies CR-AI-10 invalidation). HP is not part of this seam: controllers keep reading it through `CharacterStatsZoneEntityStatReader`. Covered by its own tests.
- [ ] **AC-6 Zero allocation.** With 150 controllers (mixed states, fake provider), `Run` followed by `IntegrationStep.Run` allocates 0 bytes after one warm-up tick (`GC.GetAllocatedBytesForCurrentThread`). `ZoneAggroCandidateSource.CopyPlayers` into a pre-sized list allocates 0 bytes. Story 014 keeps the tick-time budget.

---

## Implementation Notes

- **Decisions (user, 2026-11-16, `/story-readiness`):**
  1. **Phase 4 placement: cache + separate step.** `EnemyAISystem.Run` (phase 3) reads velocities after the controllers tick; `MobPositionIntegrationStep` applies them. Story 013 places it after the last phase 3 system (ADR-002 Decision 1), so Auto-Attack and Skill see the start-of-tick mob position, the same one the nav agents were synced to. The integration step never calls `INavigationProvider`.
  2. **Y source: keep committed Y** (spawn Y). Correct for the flat MVP starting zone; sloped terrain needs a Y source (TD entry at `/story-done`).
  3. **Non-finite positions: rejected at integration** (keep last finite position, log once per mob). `MobController` is unchanged. This closes the Story 006 code-review note (leash check and Returning arrival compare a squared distance with `>`, so a NaN position would never breach and would "arrive" at once).
  4. **Candidates: Connected players only** (CR-AI-10: a disconnected target is invalid).
- `MobController` needs `IZoneEntityStatReader`, `IMobAttackResolver`, `EnemyAIConfigValues`, `returnSpeedCap` and `IAggroCandidateSource`: `EnemyAISystem` takes them in its constructor and passes them to every controller. Story 010 supplies the real attack resolver; tests use fakes.
- `ZoneNavigationService` also registers the nav agent on `MobSpawned`; the controller's navigation calls must work whether its handler runs before or after the system's (Story 013 fixes the build order).
- Mob Spawning does not exist yet: tests raise `MobEventBus.RaiseMobSpawned` directly.

---

## Out of Scope

- Story 010: mob → player damage (real `IMobAttackResolver`)
- Story 011: despawn (calls `Remove`), the death bridge
- Story 013: adding `EnemyAISystem` and `IntegrationStep` to the composition, replacing `EmptyMobCommittedPositionSource` with the store
- Story 014: the 150-mob tick-time budget
- Y on sloped terrain (TD entry)
- Mob Spawning epic: anything raising `MobSpawned` in production

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-16.*

1. **Spawn creates a controller** — raise `MobSpawned` for a known type → `Count` 1; the controller's spawn point equals the event's position; `IsEnraged` equals the store's variant (test both values via the fake random).
2. **Refused spawn** — foreign zone, unknown `MobTypeID`, duplicate id → no new controller; the system logs nothing (only the store's log).
3. **Construction order** — a system built after the store sees a seeded entry; `Dispose` unsubscribes (a later spawn creates nothing); a second `Dispose` does not throw.
4. **Remove** — `Remove(id)` true then `Count` 0; second `Remove` false; unknown id false; a removed mob is not ticked or integrated.
5. **Deterministic order** — 3 mobs, a recording fake provider: velocity reads follow spawn order; after removing the first, the order is the swap-remove order and is the same on every run.
6. **Exception isolation** — a controller whose seam throws: one error with its `EntityID`; the other controllers tick and move; the throwing mob does not move.
7. **Pursuing moves** — fixed velocity (2, 5, 1), `deltaTime` 0.05 → position changes by (0.1, 0, 0.05) per tick over 3 ticks; Y unchanged.
8. **Returning moves** — same as 7 for a mob forced into Returning.
9. **Stopped states** — Dormant, WindingUp, Recovering (and AttackExecution) → no velocity read, position unchanged.
10. **Store sync** — after each tick, controller `Position` equals the store's `CommittedPosition`; `CopyCommittedPositions` returns it; facing is written to the store even for a WindingUp mob.
11. **deltaTime used** — `deltaTime` 0.1 doubles the step of case 7.
12. **NaN guard** — NaN and +∞ velocity → position unchanged; exactly one warning over 2 ticks; a later finite velocity moves the mob again.
13. **Cache cleared** — `IntegrationStep.Run` twice after one `Run` moves the mob once; without `Run`, it moves nothing.
14. **Candidate reader** — players in Connected, ghost, Dead and Respawning states plus one with no movement position: `CopyPlayers` returns only the Connected ones with a position, in pool order; `TryGetPlayerPosition` true only for those; false for an unknown id.
15. **Zero allocation** — 150 controllers, mixed states: `Run` + `IntegrationStep.Run` allocate 0 bytes after warm-up; `CopyPlayers` into a pre-sized list allocates 0 bytes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_PhaseStep_tests.cs` — must exist and pass

**Status**: [x] Created — 29/29 passing (2026-11-16)

---

## Dependencies

- Depends on: Story 006 (Complete), Story 008 (Complete)
- Unlocks: Stories 010, 011, 012

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 6/6 passing (all covered by automated tests)
**Deviations** (advisory):
1. AttackExecution is never a post-tick state (`TickAttackExecution` enters Recovering in the same tick), so QA case 9's AttackExecution half is covered through `test_integration_realRecovering_noVelocityReadAndNoMove`.
2. `MovementPlayerPositionReader` has no delegation test (a `MovementService` is costly to build in EditMode); Story 013's wiring exercises it.
3. The zero-allocation test warms up 12 ticks, not 1 (the controllers' first aggro scan fills their buffers), then measures 10 ticks at 0 bytes.
4. Beyond spec (code review): `Remove` called during `Run` is deferred to the end of `Run` (`_pendingRemovals`); the queued mob keeps its slot, caches no velocity, and a second `Remove` in the same `Run` returns false. Ready for Story 011's death path.
5. Ghosts: `ZoneAggroCandidateSource` filters on the pool slot's session state, but TD-105 (pool slot stays `Connected` on heartbeat ghosting) means a ghosted player stays targetable until TD-105 is fixed; note added to TD-105.
**Tech debt**: TD-108 (mob Y on sloped terrain, decision 2) added; TD-105 note.
**Test Evidence**: Integration — `tests/EditMode/EnemyAI/EnemyAI_PhaseStep_tests.cs` 29/29; batch EditMode 7727 / 7598 passed / 0 failed / 129 skipped (2026-11-16).
**Code Review**: Complete — CHANGES REQUIRED (1 test-only) → all fixes applied (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
