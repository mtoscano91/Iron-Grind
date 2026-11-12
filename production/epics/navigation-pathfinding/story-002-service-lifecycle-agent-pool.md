# Story 002: Service Lifecycle and Agent Pool

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
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
- [ ] `Teardown()`: sets `Enabled = false` on every agent the pool created (pooled and active) and returns active agents to the pool, clears `_activeAgents`, `_pendingRequests`, `_latestRequests` and the per-agent counters, then removes the NavMesh instance through `INavMeshInstanceOps.Remove`, then resets it to `default` — in that order (CR-NAV-3, AC-NAV-02). It is safe after a partial or failed `Initialize` and when called twice.
- [ ] After `Teardown`, `_tornDown` is set: `Tick()` and `SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3> committedPositions)` (GDD lines 167, 201) return immediately; `GetCurrentVelocity(EntityID)` returns `Vector3.zero` (ADR-002 Decision 3). The three are public methods on the service here; `INavigationProvider` is implemented in Story 004.
- [ ] **EC-NAV-4 state**: the service exposes `internal bool IsAcceptingSpawns` — false before a successful `Initialize`, true after one, false after `Teardown`. Story 003's `OnMobSpawned` handler reads it; the `"post-teardown MobSpawned"` log and the no-claim test belong to Story 003.
- [ ] AC-NAV-03: no `Task`, thread pool or `ConfigureAwait` in `src/ServerLogic/Navigation/` (text search test, as AC-SK-28 did), and a main-thread check in the Story 009 PlayMode test.

---

## Implementation Notes

- **File**: `src/ServerLogic/Navigation/ZoneNavigationService.cs`. Constructor takes `NavigationConfig`, `INavAgentFactory`, `INavMobSpeedSource`, `INavMeshQuery` (later stories), an `INavMeshInstanceOps` seam, and the `MobEventBus` (subscription in Story 003).
- **Instance seam**: new file `src/ServerLogic/Navigation/INavMeshInstanceOps.cs` — `interface INavMeshInstanceOps { bool IsValid(NavMeshDataInstance instance); void Remove(NavMeshDataInstance instance); }`. The service never reads `.valid` or calls `.Remove()` on the struct directly. The Unity adapter (`instance.valid` / `instance.Remove()`) is Story 009. In EditMode every instance is `default`, so the test fake answers `IsValid` from a scripted queue (in call order) and records `Remove` calls; "stored instance unchanged" (EC-NAV-6) is shown by `Remove` being called exactly once at `Teardown` and the factory count staying at 150.
- **Ownership** (`IZoneNavigation` doc): the service owns the instance from the moment `Initialize` is called, also if it throws. So store it first, then validate: `Teardown` must still `Remove()` a valid instance whose pool fill threw.
- **Pool fill** happens once, in `Initialize`, after the valid check. No `Instantiate` / `Destroy` after that; count factory calls in tests.
- **Teardown order** test: inject an ordered recorder through the fakes (every created agent's `Enabled = false` write → dictionaries cleared → `Remove()`), as AC-NAV-02 asks. No agent is claimed in this story (claim is Story 003), so the recorder sees the writes on pooled agents; Story 003 adds the same order test with a claimed agent.
- **Composition contract**: `ZoneServerComposition` calls `Teardown` at step 3 only if `MarkNavigationInitialized` ran (line ~1354); the composition swap is Story 010.

---

## Out of Scope

- Story 003: `OnMobSpawned` / `OnMobDied` handling, including the EC-NAV-4 `"post-teardown MobSpawned"` log and the teardown order test with a claimed agent
- Story 009: the Unity `INavMeshInstanceOps` adapter
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
- **IsAcceptingSpawns**: false before `Initialize`; false after an invalid-instance `Initialize`; true after a valid one; false after `Teardown`.
- **No async**: text search of `src/ServerLogic/Navigation/*.cs` finds no `Task`, `ThreadPool`, `ConfigureAwait`, `async`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_Lifecycle_tests.cs` — must exist and pass

**Status**: [x] Created and passing

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 003, Story 009

---

## Completion Notes

**Completed**: 2026-11-12
**Criteria**: 7/7 passing (AC7 main-thread check deferred to the Story 009 PlayMode test, as the AC states)
**Deviations** (advisory):
- Only the first `Initialize` is accepted: a retry after a failed call, or a call after `Teardown`, logs an error and is ignored; the instance it carried stays with the caller (code review fix 1/3; extends EC-NAV-6). `IZoneNavigation.Initialize` doc updated to say so.
- `Teardown` sets `_tornDown` first, disables each agent in its own try/catch (logged), and removes/resets the instance in a `finally` (code review fix 2; stricter than CR-NAV-3).
- Test-only internal members `TrackedStateCount` and `StoredInstance`; pool root name uses `ZoneID.RawValue` (`NavigationPool_7`).
- The no-async scan strips comments (`INavigationProvider.cs` doc mentions async); a string literal containing a forbidden word would still false-positive (INFO, not fixed).
**Test Evidence**: Logic: `tests/EditMode/Navigation/Navigation_Lifecycle_tests.cs`, 25 [Test] + 6 [TestCase] rows = 31, all pass. Instance fake models the global NavMesh (`Add`/`Remove`); distinct instances built by reflection on the struct's int field (guard test fails loudly if Unity renames it). EditMode 7139 / 7010 passed / 0 failed / 129 skipped.
**Code Review**: Complete — unity-specialist ISSUES (no blockers), qa-tester GAPS, ADR-002/003 compliant; fixes 1–8 applied. Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
**Carried**: Story 003 — `IsAcceptingSpawns` guard and teardown order test with a claimed agent (already in Story 003); re-check the torn-down guard with a claimed agent. Story 005 — the guard test becomes meaningful once `Tick` / `SyncAgentPositions` have bodies. Story 009 — `UnityNavMeshInstanceOps` adapter must not cache instances; teardown leaves the pool GameObjects in place (one zone per process).
