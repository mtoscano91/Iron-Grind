# Story 001: Navigation Seams, Config and `MobEventBus`

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — Interactions table, Tuning Knobs, CR-NAV-5 (speed source), CR-NAV-13 (`SamplePosition`)
**Requirement**: `TR-nav-002`, `TR-nav-003` (the types they need; behaviour comes in Stories 002–003)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine; ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: `INavigationProvider` has six methods, `Resume` included (ADR-003 Decision 1). Broadcast events are `event Action<T>` with a `readonly struct` T in `IronGrind.Events`, named `On…` (ADR-010).

**Engine**: Unity 6.3 LTS | **Risk**: LOW (no NavMesh API is called in this story)
**Engine Notes**: none. The agent seam exists so every later Logic story can run in EditMode without a real `NavMeshAgent`.

**Control Manifest Rules (this layer)**:
- Required: `INavigationProvider` = `SetDestination`, `SetSpeed`, `Stop`, `Resume`, `IsPathStale`, `GetCurrentVelocity`
- Required: event arg types are `readonly struct`s in `IronGrind.Events`, one file per type; events are `On…`
- Required: gameplay values are data-driven (config asset), not constants
- Required: new systems live in `IronGrind.ServerLogic` (ADR-012)

---

## Acceptance Criteria

- [ ] `INavigationProvider` (`src/ServerLogic/Navigation/INavigationProvider.cs`) declares exactly the six ADR-003 methods with the GDD signatures (`EntityID`, `Vector3`, `float`, `bool`).
- [ ] `INavAgent` wraps the `NavMeshAgent` members the service uses (`enabled`, `isStopped`, `speed`, `updatePosition`, `updateRotation`, `autoBraking`, obstacle avoidance off, `isOnNavMesh`, `pathPending`, `pathStatus`, `isPathStale`, `desiredVelocity`, `nextPosition`, `Warp`, `SetDestination`, `ResetPath`); `INavAgentFactory` creates one pooled agent under a named root; `INavMeshQuery` wraps `NavMesh.SamplePosition`.
- [ ] `INavMobSpeedSource.TryGetMoveSpeed(MobTypeID, out float)` with a `StandInNavMobSpeedSource` that returns true and `NavigationConfig.StandInMobMoveSpeed` (default 3.5 m/s) for every type and logs once (Enemy AI's `IMobDefinitionRegistry` replaces it, and that epic removes the config field).
- [ ] `NavigationConfig : ScriptableObject` holds the 8 GDD knobs with their defaults (`MAX_AGENT_POOL_SIZE` 150, `MAX_PATH_UPDATES_PER_TICK` 20, `PARTIAL_PATH_TIMEOUT_TICKS` 20, `RETURN_SPEED_CAP` 6.0, `MIN_VALID_SPAWN_FRACTION` 0.90, `NAV_STUCK_REROUTE_DETECTION_TICKS` 10, `NAV_OFF_MESH_RECOVERY_TICKS` 3, spawn sample radius 0.1) plus `StandInMobMoveSpeed` (3.5, range [0.1, 20.0], the CR-NAV-8 clamp range); a validator rejects values outside each GDD safe range and `MAX_AGENT_POOL_SIZE` ≠ `ZoneSlotPool.MAX_MOBS_PER_ZONE`. `MAX_PATH_UPDATES_PER_TICK` is valid in [1, 150]; 1–4 passes with a warning (GDD: below 5 the burst lag passes 1.5 s, F-NAV-3) — readiness decision 2026-11-12.
- [ ] `MobEventBus` (one instance per zone) raises `OnMobSpawned(MobSpawnedArgs)` (`ZoneID`, `EntityID`, `MobTypeID`, `Vector3 SpawnPosition`) and `OnMobDied(MobDiedArgs)` (`ZoneID`, `EntityID`); both arg types are `readonly struct`s in `IronGrind.Events`.

---

## Implementation Notes

- **Folder**: `src/ServerLogic/Navigation/` (namespace `IronGrind.ServerLogic.Navigation`). `MobEventBus` goes in `src/ServerLogic/ZoneInstancing/MobEventBus.cs`: Zone Instancing creates it at T-1 and injects it (CR-MS-5). `MobSpawnedArgs.cs` and `MobDiedArgs.cs` go next to it in `src/ServerLogic/ZoneInstancing/`, with `namespace IronGrind.Events` (the precedent: `MobDeathContext` sits in `src/ServerLogic/AutoAttackCombat/` under that namespace; there is no `Events/` folder).
- **Bus naming**: the GDDs say `MobEventBus.MobSpawned` / `MobDied`; the manifest wants `On…` events, so `OnMobSpawned` / `OnMobDied`. It is a different event from Combat's `ICombatService.OnMobDied` (epic decision 1: Enemy AI bridges one to the other). Say so in the doc comment.
- **Events and raise**: copy `AutoAttackCombatSystem.OnMobDied` (`AutoAttackCombatSystem.cs:89` and `RaiseMobDied` at `:607`): custom `add` / `remove` that rebuild an `Action<T>[]` (setup-time allocation only; `null` ignored; remove drops the last registration), and `RaiseMobSpawned(in MobSpawnedArgs)` / `RaiseMobDied(in MobDiedArgs)` that loop over a snapshot of the array with one `try` / `catch` per subscriber calling `Debug.LogException`. No allocation per raise.
- **`INavAgent`**: properties map 1:1 to `NavMeshAgent`. Obstacle avoidance can be a single `DisableObstacleAvoidance()` call or a property; keep the seam minimal. No `UnityNavAgent` here (Story 009).
- **Config**: follow `SkillSystemConfig` (ScriptableObject + in-code defaults + validation). Compare the pool size with `ZoneSlotPool.MAX_MOBS_PER_ZONE` (`ZoneSlotPool.cs:57`); do not duplicate the number (`LastBeatServerTickTracker.cs:108` already has a second copy — leave it).
- **Stand-in speed**: `StandInNavMobSpeedSource` takes `NavigationConfig` and returns `StandInMobMoveSpeed`; it logs once through `StandInLogOnce` like the other `StandIn…` classes (`ZoneServerStandIns.cs:20`), or the same pattern if that class is not reachable from `Navigation/`.
- **Performance**: no per-tick cost in this story. The bus raises once per mob spawn or death and allocates nothing; config validation runs at load.

---

## Out of Scope

- Story 002: the service, pool fill and lifecycle
- Story 009: Unity adapters for `INavAgent`, `INavAgentFactory`, `INavMeshQuery`
- Story 010: creating the bus per zone in the composition

---

## QA Test Cases

- **Config defaults**: a new `NavigationConfig` → each knob equals the GDD default.
- **Config ranges**: for each knob, a value one step below and one above its safe range → validation fails naming the knob; the bounds themselves pass. `MAX_PATH_UPDATES_PER_TICK`: 0 and 151 fail; 1 and 4 pass with one warning each; 5 and 150 pass with no warning. `StandInMobMoveSpeed`: 0.09 and 20.01 fail; 0.1 and 20.0 pass.
- **Pool size parity**: `MAX_AGENT_POOL_SIZE` ≠ `MAX_MOBS_PER_ZONE` → validation fails.
- **Bus spawned**: subscribe two handlers; `RaiseMobSpawned` → both receive the same zone, entity, type and position.
- **Bus died**: same for `RaiseMobDied`.
- **Throwing subscriber**: the first handler throws → the second still runs; the exception is logged.
- **Arg types**: `MobSpawnedArgs` and `MobDiedArgs` are value types (`typeof(T).IsValueType`) and readonly (`IsReadOnlyAttribute`).
- **Stand-in speed**: `TryGetMoveSpeed(any)` → true and 3.5 with default config; with `StandInMobMoveSpeed` 5.0 → 5.0; logs once across two calls.
- **Unsubscribe**: add a handler twice and remove it once → one raise calls it once; removing a handler never added does nothing; adding `null` does nothing.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_Config_tests.cs` (config, bus, stand-in) — must exist and pass

**Status**: [x] Created 2026-11-12 (53 test cases after code review)

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002, 007

---

## Completion Notes

**Completed**: 2026-11-12
**Criteria**: 5/5 passing (AC2 seam shapes compile-verified here; adapters exercised in Story 009)
**Deviations** (advisory):
- `StandInNavMobSpeedSource` takes `NavigationConfigValues`, not the `NavigationConfig` asset (consistent with "systems take the values struct", Skill precedent).
- Validation rejects out-of-range values instead of clamping (as this story specifies; Skill's config clamps).
- Scope (code review fix 9, user "fix all"): `StandInLogOnce` moved from `ZoneServer/ZoneServerStandIns.cs` to `src/ServerLogic/StandIns/StandInLogOnce.cs` (namespace `IronGrind.ServerLogic.StandIns`) so Navigation does not depend on the composition layer; `using` added to its 5 other users; behaviour and log text unchanged.
- Pool size default and max are `ZoneSlotPool.MAX_MOBS_PER_ZONE` (no duplicated 150).
**Carried**: Story 003 — `HandleMobDied` must treat an already-released entity as a no-op (a handler removed during a raise still receives that raise). Story 009 — `UnityNavMeshQuery` sets `hitPosition = default` on a miss; the agent fake records `DisableObstacleAvoidance()` calls (write-only member).
**Test Evidence**: Logic: `tests/EditMode/Navigation/Navigation_Config_tests.cs`, 42 [Test] + 11 [TestCase] rows = 53, all pass. EditMode 7108 / 6979 passed / 0 failed / 129 skipped (batch, 13:04Z).
**Code Review**: Complete (unity-specialist CLEAN + qa-tester GAPS → "fix all 1–9" applied).
