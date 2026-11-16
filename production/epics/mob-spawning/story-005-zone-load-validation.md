# Story 005: Zone Load — Spawn Table Cap and NavMesh Validation

> **Epic**: Mob Spawning
> **Status**: Ready
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

**Epic default (`/create-stories` 2026-11-16, revisit at readiness)**: the 150 cap is checked on the authored table **before** NavMesh validation (an over-capacity table is an authoring error and fails without any NavMesh query).

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (NavMesh query behind `INavMeshQuery`; the validator's Unity adapter was verified by Navigation Story 007)
**Engine Notes**: `NavMesh.SamplePosition` lives behind `INavMeshQuery`; EditMode tests use a fake query. No new engine API in this story.

**Control Manifest Rules (this layer)**:
- Required: NavMesh calls on the main thread only, at the points ADR-002 lists — ADR-002
- Required: server code in `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **AC-1 Cap (AC-MS-1)**: a table with 151 entries → zone initialisation fails, the zone never reaches Active, `SpawnTableOverCapacity` is logged, `SpawnPointValidator.Validate` is not called and no `TryAddMob` is made. 150 entries load.
- [ ] **AC-2 Story 001 validation**: any other table validation failure from Story 001 also fails zone initialisation the same way (logged, not Active, no `TryAddMob`).
- [ ] **AC-3 NavMesh validation**: a valid table is converted to `SpawnPointInput(SpawnPointID, Position)` entries and passed to `SpawnPointValidator.Validate` once, after Navigation's `Initialize`; only the points in `validOut` become runtime spawn points (Story 002 service built from them); excluded points never spawn.
- [ ] **AC-4 Abort**: `ShouldAbort == true` → zone initialisation fails (zone not Active, error logged by the caller, no `TryAddMob`).
- [ ] **AC-5 Load seam**: the table is read from `ZoneDefinition.SpawnTable` (Story 001 decision 1, via `ZoneDefinitionSlot`); a null table logs one error and fails zone initialisation (a zone template without a spawn table is an authoring error in MVP).
- [ ] **AC-6 Order**: a call log shows: table load → Story 001 validation → Navigation `Initialize` → `Validate` → spawner built. Nothing calls the roster before T-2 (`PopulateOnActive`, Story 006).

---

## Implementation Notes

- `src/ServerLogic/Navigation/SpawnPointValidator.cs` (`Validate(ZoneID, IReadOnlyList<SpawnPointInput>, List<SpawnPointInput> validOut) → SpawnValidationResult`), `SpawnPointInput`, `SpawnValidationResult.ShouldAbort`.
- Where "zone initialisation fails" is signalled today: follow the existing S5 failure path in `ZoneServerComposition` (e.g. `NO_MOB_REGISTRY_ERROR` handling) so the host does not reach `MarkActive`. Read how Navigation `Initialize` failure is reported and mirror it.
- This story can be a loader / factory class (`MobSpawningZoneLoader` or similar) tested in EditMode with fakes; Story 006 calls it from the composition.
- Mapping `validOut` back to authored entries is by `SpawnPointID` (unique per Story 001).

---

## Out of Scope

- Story 006: calling this from the composition, `MarkActive` → `PopulateOnActive`, phase placement
- Story 007: authoring the starting-zone positions

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`. Fake `INavMeshQuery` (per-point on/off mesh), fake roster with call log.*

1. **151 entries (AC-1)**: fails, `LogAssert` `SpawnTableOverCapacity`, fake query never called, 0 roster calls.
2. **150 entries (AC-1)**: all on mesh → service built with 150 points.
3. **Bad entry (AC-2)**: duplicate SpawnPointID → fails before NavMesh validation.
4. **Exclusion (AC-3)**: 20 points, 2 off mesh, fraction above threshold → service has 18 points; after `PopulateOnActive` the 2 excluded IDs never appear in `TryAddMob` positions.
5. **Abort (AC-4)**: off-mesh share above the abort threshold → fails, no service, 0 roster calls.
6. **Missing table (AC-5)**: no table for the template → one error, fails.
7. **Order (AC-6)**: call log order as stated; `Validate` called exactly once.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_ZoneLoad_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002
- Unlocks: Story 006, Story 007
