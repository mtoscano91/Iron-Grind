# Story 005: Zone Load — Spawn Table Cap and NavMesh Validation

> **Epic**: Mob Spawning
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-2 invariant (reject > 150 at T-1, `SpawnTableOverCapacity`), AC-MS-1; `design/gdd/navigation-pathfinding.md` — CR-NAV-13, EC-NAV-5, AC-NAV-21 (off-mesh points excluded, abort below `MIN_VALID_SPAWN_FRACTION`)
**Requirement**: `TR-ms-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract
**ADR Decision Summary**: NavMesh queries run on the main thread at defined points; spawn point validation runs once at zone load after Navigation's `Initialize`, through `INavMeshQuery`.

**Navigation epic hand-off (decision 7, Story 007 readiness 2026-11-12)**: Mob Spawning builds `SpawnPointInput` entries from `ZoneSpawnTable` (`SpawnPointID: uint`, authored position), calls `SpawnPointValidator.Validate` after Navigation's `Initialize`, spawns only from `validOut`, and aborts zone initialisation (with Zone Instancing) when `ShouldAbort` is true — the validator only computes the flag.

**Epic default (`/create-stories` 2026-11-16, confirmed at readiness 2026-11-16)**: the 150 cap is checked on the authored table **before** NavMesh validation (an over-capacity table is an authoring error and fails without any NavMesh query). `ZoneSpawnTableValidator.ValidateEntries` already fails on the cap before any per-entry check.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (NavMesh query behind `INavMeshQuery`; the validator's Unity adapter was verified by Navigation Story 007)
**Engine Notes**: `NavMesh.SamplePosition` lives behind `INavMeshQuery`; EditMode tests use a fake query. No new engine API in this story.

**Control Manifest Rules (this layer)**:
- Required: NavMesh calls on the main thread only, at the points ADR-002 lists — ADR-002
- Required: server code in `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

"Fails" below means: `TryLoad` returns false, `service` is null, and the fake roster records 0 calls. Reaching / not reaching Active is Story 006 (the composition acts on the return value).

- [x] **AC-1 Cap (AC-MS-1)**: a table with 151 entries → fails, `SpawnTableOverCapacity` is logged (by `ZoneSpawnTableValidator`), the fake `INavMeshQuery` is never called. 150 entries load (returns true, service has 150 points).
- [x] **AC-2 Story 001 validation**: any other table validation failure from Story 001 (template mismatch, duplicate ID, unknown mob type, out-of-bounds or non-finite position, override > 0) also fails the same way, before any NavMesh query; the loader adds no log of its own.
- [x] **AC-3 NavMesh validation**: a valid table is converted to `SpawnPointInput(SpawnPointID, Position)` entries and passed to `SpawnPointValidator.Validate` exactly once; the `MobSpawningService` is built from the table entries whose IDs are in `validOut`, in authored order; excluded points never spawn.
- [x] **AC-4 Abort**: `ShouldAbort == true` → fails. The only errors are the validator's (one per off-mesh point + its CRITICAL line); the loader adds none.
- [x] **AC-5 Load seam**: the table is read from `ZoneDefinition.SpawnTable` (Story 001 decision 1, via `ZoneDefinitionSlot`). A null table → fails with exactly one error (`ZoneSpawnTableValidator`'s "spawn table is null"). A null `ZoneDefinition` (empty slot) → fails with exactly one loader error naming the zone.
- [x] **AC-6 Order**: precondition — the caller calls `TryLoad` after Navigation's `Initialize` (asserted in Story 006). Inside the loader a call log shows: definition/table null check → Story 001 validation → map to `SpawnPointInput` → `Validate` (once) → service built. The loader never calls the roster (`PopulateOnActive` is T-2, Story 006).
- [x] **AC-7 Empty table (EC-NAV-5)**: an empty table → returns true with a 0-point service, exactly two warnings (Story 001's "spawn table is empty" and the validator's empty-table warning), no abort.

---

## Implementation Notes

- `src/ServerLogic/Navigation/SpawnPointValidator.cs` (`Validate(ZoneID, IReadOnlyList<SpawnPointInput>, List<SpawnPointInput> validOut) → SpawnValidationResult`), `SpawnPointInput`, `SpawnValidationResult.ShouldAbort`.
- Where "zone initialisation fails" is signalled today: follow the existing S5 failure path in `ZoneServerComposition` (e.g. `NO_MOB_REGISTRY_ERROR` handling) so the host does not reach `MarkActive`. Read how Navigation `Initialize` failure is reported and mirror it.
- This story can be a loader / factory class (`MobSpawningZoneLoader` or similar) tested in EditMode with fakes; Story 006 calls it from the composition.
- Suggested signature: `bool TryLoad(ZoneID zone, ZoneDefinition definition, IMobDefinitionRegistry registry, SpawnPointValidator validator, IZoneMobRoster roster, MobEventBus bus, MobSpawningConfigValues config, int deathLingerTicks, out MobSpawningService service)`. Story 001 validation uses `definition.TemplateId`, `WalkableBoundsMin`, `WalkableBoundsMax`.
- Mapping `validOut` back to authored entries is by `SpawnPointID` (unique per Story 001), keeping authored order.
- Logging: the loader logs only the null-definition case; every other failure is already logged by `ZoneSpawnTableValidator` or `SpawnPointValidator`.
- Performance: load time only, once per zone start; no per-tick cost. Allocations (input list, valid list, filtered entries) are acceptable at load.

---

## Out of Scope

- Story 006: calling this from the composition after Navigation `Initialize`, acting on a false return (zone not Active), `MarkActive` → `PopulateOnActive`, phase placement
- Story 007: authoring the starting-zone positions

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`. Fake `INavMeshQuery` (per-point on/off mesh), fake roster with call log.*

*`MIN_VALID_SPAWN_FRACTION` = 0.90 (abort only when valid fraction < 0.90). Every off-mesh point logs one validator error — `LogAssert.Expect` each.*

1. **151 entries (AC-1)**: fails, `LogAssert` `SpawnTableOverCapacity`, fake query never called, 0 roster calls.
2. **150 entries (AC-1)**: all on mesh → returns true, service built with 150 points.
3. **Bad entry (AC-2)**: duplicate SpawnPointID → fails, fake query never called; same for template mismatch.
4. **Exclusion, boundary (AC-3)**: 20 points, 2 off mesh (18/20 = 0.90, boundary, no abort) → 2 expected errors, service has 18 points; after `PopulateOnActive` the 2 excluded positions never appear in `TryAddMob`.
5. **Abort (AC-4)**: 20 points, 3 off mesh (17/20 = 0.85) → 3 point errors + CRITICAL expected, fails, no service, 0 roster calls, no other error.
6. **Missing table / definition (AC-5)**: null `SpawnTable` → one error, fails; null definition → one loader error, fails.
7. **Order (AC-6)**: call log order as stated; `Validate` called exactly once.
8. **Empty table (AC-7)**: 0 entries → returns true, 0-point service, two warnings, fake query never called.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_ZoneLoad_tests.cs` — must exist and pass

**Status**: [x] Created — 21/21 passing (2026-11-16)

---

## Dependencies

- Depends on: Story 001, Story 002
- Unlocks: Story 006, Story 007

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 7/7 passing (none deferred)
**Deviations**: Advisory: the `MobSpawningService` constructor's `ArgumentException` / `ArgumentOutOfRangeException` (ZoneID.Invalid, invalid config, `deathLingerTicks` ≤ 0) propagate from `TryLoad` instead of returning false — programmer/config errors fail fast, documented on `TryLoad`. Advisory for Story 006: its AC-1 says "Build at S5", but `TryLoad` must run after Navigation `Initialize` (S6); resolve the phase placement at Story 006 readiness.
**Test Evidence**: Integration: `tests/EditMode/MobSpawning/MobSpawning_ZoneLoad_tests.cs`, 21/21 (EditMode with fakes; project convention, no `tests/integration/` folder); batch EditMode `MobSpawning|EnemyAI|ZoneInstancing|AssemblyBoundary|Navigation` 1563/1563 (end 22:12:22Z, 0 `error CS`)
**Code Review**: Complete (`/code-review` 2026-11-16: APPROVED WITH SUGGESTIONS, suggestions 1–6 applied)
