# Story 002: Service Lifecycle and Agent Pool

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-1, CR-NAV-2, CR-NAV-3, CR-NAV-4, EC-NAV-4, EC-NAV-6, AC-NAV-01 to 04
**Requirement**: `TR-nav-001`, `TR-nav-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (Decisions 3, 4); ADR-003 (Decisions 3, 5); ADR-009 Amendment 1
**ADR Decision Summary**: The scene loader adds the zone's NavMesh; the service receives the `NavMeshDataInstance` at S6 and owns it from that call, even if it throws. Teardown disables agents, clears state, then removes the data; a `_tornDown` guard makes later calls harmless. `NavMeshAgent.enabled` is false while an agent sits in the pool. One zone per process.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (NavMesh is process-global; behaviour in `UNITY_SERVER` is past the training cutoff)
**Engine Notes**: `NavMeshDataInstance.valid` / `.Remove()` are called through a small seam so EditMode tests can fake a valid or invalid instance. Real-instance behaviour is covered by Story 009.

**Control Manifest Rules (this layer)**:
- Required: NavMesh APIs on the main thread only
- Required: `_tornDown` guard on `Tick`, `SyncAgentPositions`, `GetCurrentVelocity`
- Required: `enabled` tracks pool occupancy (false in the pool)
- Required: one zone per process

---

## Acceptance Criteria

- [ ] `ZoneNavigationService : IZoneNavigation` (and later `INavigationProvider`). `Initialize(ZoneID, NavMeshDataInstance)`: if the instance is invalid, log an error with the `ZoneID` and throw (`InvalidOperationException`), so S6 fails and no mob spawns without a mesh (CR-NAV-2 step 2, AC-NAV-01).
- [ ] With a valid instance, `Initialize` fills the pool with exactly `MAX_AGENT_POOL_SIZE` agents created by `INavAgentFactory` under root `NavigationPool_{ZoneID}`, each with `updatePosition`, `updateRotation`, `autoBraking` false, obstacle avoidance off, and `enabled` false (CR-NAV-4, AC-NAV-04 first half).
- [ ] **EC-NAV-6**: a second `Initialize` while the first instance is valid logs an error and returns without touching the pool or the stored instance.
- [ ] `Teardown()`: disables every active agent and returns it to the pool, clears `_activeAgents`, `_pendingRequests`, `_latestRequests` and the per-agent counters, then removes the NavMesh instance, then resets it to `default` — in that order (CR-NAV-3, AC-NAV-02). It is safe after a partial or failed `Initialize` and when called twice.
- [ ] After `Teardown`, `_tornDown` is set: `Tick` and `SyncAgentPositions` return immediately; `GetCurrentVelocity` returns `Vector3.zero` (ADR-002 Decision 3).
- [ ] **EC-NAV-4**: an `OnMobSpawned` delivered after teardown (instance not valid) logs `"post-teardown MobSpawned"` and claims nothing (the guard sits in Story 003's handler; this story provides the state it reads).
- [ ] AC-NAV-03: no `Task`, thread pool or `ConfigureAwait` in `src/ServerLogic/Navigation/` (text search test, as AC-SK-28 did), and a main-thread check in the Story 009 PlayMode test.

---

## Implementation Notes

- **File**: `src/ServerLogic/Navigation/ZoneNavigationService.cs`. Constructor takes `NavigationConfig`, `INavAgentFactory`, `INavMobSpeedSource`, `INavMeshQuery` (later stories), an `INavMeshInstanceHandle` seam (or a `Func<bool> isValid` + `Action remove` pair) wrapping `NavMeshDataInstance`, and the `MobEventBus` (subscription in Story 003).
- **Ownership** (`IZoneNavigation` doc): the service owns the instance from the moment `Initialize` is called, also if it throws. So store it first, then validate: `Teardown` must still `Remove()` a valid instance whose pool fill threw.
- **Pool fill** happens once, in `Initialize`, after the valid check. No `Instantiate` / `Destroy` after that; count factory calls in tests.
- **Teardown order** test: inject an ordered recorder through the fakes (agent `enabled = false` → dictionaries cleared → `Remove()`), as AC-NAV-02 asks.
- **Composition contract**: `ZoneServerComposition` calls `Teardown` at step 3 only if `MarkNavigationInitialized` ran (line ~1354); the composition swap is Story 010.

---

## Out of Scope

- Story 003: `OnMobSpawned` / `OnMobDied` handling
- Story 005: `Tick` and `SyncAgentPositions` bodies (only the `_tornDown` early return here; stub the rest)
- Story 010: replacing `StandInZoneNavigation` in the host

---

## QA Test Cases

- **Invalid instance**: `Initialize` with an invalid fake → throws; one error log with the `ZoneID`; factory never called; a later `Teardown` does not call `Remove()` on it (nothing valid to remove) and does not throw.
- **Pool fill**: valid fake → factory called exactly 150 times; every agent has the five flags set as listed; `AvailableCount == 150`, `ActiveCount == 0`; root name `NavigationPool_{ZoneID}`.
- **Pool fill throws on agent 37**: `Initialize` throws; `Teardown` still calls `Remove()` once and disables the 36 created agents.
- **Double Initialize (EC-NAV-6)**: second call → one error log; factory call count stays 150; stored instance unchanged.
- **Teardown order**: recorder shows [agents disabled, collections cleared, `Remove()`], `Remove()` exactly once; instance reset to `default`.
- **Teardown twice**: second call does nothing (no second `Remove()`, no throw).
- **Torn-down guard**: after `Teardown`, `Tick()` and `SyncAgentPositions(..)` touch no agent; `GetCurrentVelocity(any)` → `Vector3.zero`.
- **No async**: text search of `src/ServerLogic/Navigation/*.cs` finds no `Task`, `ThreadPool`, `ConfigureAwait`, `async`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_Lifecycle_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 003, Story 009
