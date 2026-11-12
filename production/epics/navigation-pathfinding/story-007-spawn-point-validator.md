# Story 007: Spawn-Point Validator

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-13, EC-NAV-5, AC-NAV-21, Tuning Knobs (`MIN_VALID_SPAWN_FRACTION`, sample radius)
**Requirement**: `TR-nav-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract
**ADR Decision Summary**: NavMesh queries run on the main thread at zone load. A zone that cannot place enough of its mobs on the mesh must not serve.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (`NavMesh.SamplePosition` with `NavMesh.AllAreas`; real behaviour checked in Story 009)
**Engine Notes**: the validator calls `INavMeshQuery`, never `NavMesh` directly.

**Control Manifest Rules (this layer)**:
- Required: radius and fraction come from `NavigationConfig`
- Required: main thread only

---

## Acceptance Criteria

- [ ] `SpawnPointValidator.Validate(ZoneID, IReadOnlyList<(uint id, Vector3 position)>)` (or a small struct) samples every point with the configured radius and `AllAreas`; each miss logs an error with `ZoneID`, spawn point id and position and is left out of the result (AC-NAV-21).
- [ ] The result carries the valid points in declaration order, the valid and total counts, and `ShouldAbort = validCount / totalCount < MIN_VALID_SPAWN_FRACTION`; on abort it logs one critical error with `ZoneID`, valid and total counts (CR-NAV-13).
- [ ] **EC-NAV-5**: an empty list logs the warning `"Zone {ZoneID} has an empty spawn table — no mobs will spawn. Verify this is intentional."`, returns no points and does not abort.
- [ ] No allocation beyond the result buffer (reuse a caller-provided list, or allocate once at zone load — zone load, not the tick, so a list is fine; say which).

---

## Implementation Notes

- **File**: `src/ServerLogic/Navigation/SpawnPointValidator.cs`. Constructor takes `INavMeshQuery` and `NavigationConfig`.
- **Not wired here.** The zone has no `ZoneSpawnTable` yet; the Mob Spawning epic's spawn-table story calls the validator at zone T-1 (after Navigation's `Initialize`) and passes only valid points to the spawner. Record this hand-off in the Mob Spawning epic when it is created.
- **Fraction boundary**: 0.90 with 9 of 10 valid → 0.9 is not below 0.9 → no abort. Compute with integers (`validCount * 100 < fractionPercent * totalCount`) or a documented float comparison, and test exactly at the boundary.

---

## Out of Scope

- Mob Spawning epic: `ZoneSpawnTable`, calling the validator at T-1, aborting zone initialisation on `ShouldAbort`
- Story 008: the baked mesh the real query runs against

---

## QA Test Cases

- **One off-mesh point** in 10 → 9 points returned in order; one error containing zone, id and position; `ShouldAbort` false.
- **Boundary**: 9 of 10 valid at 0.90 → no abort; 8 of 10 → abort, one critical log with 8 and 10.
- **All invalid** (3 of 3) → no points; abort.
- **Empty list** → the exact warning text; no abort; no query call.
- **Radius and areas**: the fake query records radius 0.1 (config) and `AllAreas` for each call; changing the config radius changes the recorded value.
- **Declaration order** preserved for the valid points.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_SpawnPointValidation_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Mob Spawning epic (spawn-table story)
