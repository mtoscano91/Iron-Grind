# Story 007: Spawn-Point Validator

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-13, EC-NAV-5, AC-NAV-21, Tuning Knobs (`MIN_VALID_SPAWN_FRACTION`, sample radius); `design/gdd/mob-spawning.md` (`SpawnPointID: uint`)
**Requirement**: `TR-nav-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract
**ADR Decision Summary**: NavMesh queries run on the main thread at zone load. A zone that cannot place enough of its mobs on the mesh must not serve.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (`NavMesh.SamplePosition` with `NavMesh.AllAreas`; real behaviour checked in Story 009)
**Engine Notes**: the validator calls `INavMeshQuery`, never `NavMesh` directly. The area mask is the `NavMesh.AllAreas` constant.

**Control Manifest Rules (this layer)**:
- Required: radius and fraction come from `NavigationConfig`
- Required: main thread only

---

## Acceptance Criteria

- [x] **Types**: `public readonly struct SpawnPointInput { uint Id; Vector3 Position; }` (constructor + doc comments) and `public readonly struct SpawnValidationResult { int ValidCount; int TotalCount; bool ShouldAbort; }`. Signature: `SpawnValidationResult Validate(ZoneID zone, IReadOnlyList<SpawnPointInput> points, List<SpawnPointInput> validOut)`. The caller owns `validOut`; the validator clears it first, then appends the valid points.
- [x] **Sampling** (AC-NAV-21): every point with a finite position is sampled once through `INavMeshQuery.SamplePosition` with the configured `SpawnSampleRadius` and `NavMesh.AllAreas`. Each miss logs one error with `ZoneID`, spawn point id and position and is left out of `validOut`. Valid points keep their **authored** position (not the sampled hit; the claim-time `Warp` snaps the agent, CR-NAV-5) and their declaration order.
- [x] **Non-finite position** (NaN / ∞ in any component): counted as invalid, logs the same error, and the query is **not** called for it.
- [x] **Abort rule** (CR-NAV-13): the fraction is converted once to basis points, `bp = Mathf.RoundToInt(MinValidSpawnFraction * 10000)`; `ShouldAbort = (long)ValidCount * 10000 < (long)bp * TotalCount`. On abort, one `Debug.LogError` with `LOG_PREFIX + "CRITICAL: "`, `ZoneID`, valid and total counts. The validator only computes the flag; aborting zone initialisation is the caller's (Mob Spawning / Zone Instancing, see `EPIC.md` Decision 7 hand-off).
- [x] **EC-NAV-5**: an empty list logs exactly the warning `"Zone {ZoneID} has an empty spawn table — no mobs will spawn. Verify this is intentional."` (with `LOG_PREFIX` if the class uses one — pick and test it), clears `validOut`, returns `0 / 0 / ShouldAbort false`, calls no query, and does no division.
- [x] **Null arguments**: `points` or `validOut` null → `ArgumentNullException` (programmer error at zone load). Constructor null `INavMeshQuery` / `NavigationConfig` → `ArgumentNullException`.
- [x] **Config**: the constructor reads `NavigationConfigValues.FromAsset(config)` once (same path as `ZoneNavigationService`); radius and fraction are not re-read per call.
- [x] **Allocation**: none beyond the caller's `validOut` growth (zone load, not the tick); no LINQ, no per-point allocation on the success path; strings only on log paths.

---

## Implementation Notes

- **Files**: `src/ServerLogic/Navigation/SpawnPointValidator.cs` (class), `SpawnPointInput.cs`, `SpawnValidationResult.cs` (one type per file, PascalCase). Constructor takes `INavMeshQuery` and `NavigationConfig`.
- **Not wired here.** The zone has no `ZoneSpawnTable` yet; the Mob Spawning epic's spawn-table story calls the validator at zone T-1 (after Navigation's `Initialize`) and passes only valid points to the spawner. The hand-off is recorded in `EPIC.md` Decision 7.
- **Why basis points**: a float comparison fails for fractions that `float` stores slightly high (`0.55f` = 0.5500000119, so 11 of 20 valid would wrongly abort); integer percent loses fractions like 0.905. Basis points cover the [0.50, 1.00] range exactly for any 4-decimal value. Comment this in code.
- Duplicate `SpawnPointID`s are not checked here (authoring validation belongs to Mob Spawning).

---

## Out of Scope

- Mob Spawning epic: `ZoneSpawnTable`, building `SpawnPointInput`, calling the validator at T-1, aborting zone initialisation on `ShouldAbort`, duplicate-id checks
- Story 008: the baked mesh the real query runs against
- Story 009: the real `INavMeshQuery` adapter

---

## QA Test Cases

- **One off-mesh point** in 10 → 9 points in `validOut` in order; one error containing zone, id and position; `ShouldAbort` false.
- **Boundary**: 9 of 10 valid at 0.90 → no abort; 8 of 10 → abort, one CRITICAL error with 8 and 10.
- **Float precision**: fraction 0.55, 11 of 20 valid → no abort; 10 of 20 → abort.
- **All invalid** (3 of 3) → no points; abort; three errors + one CRITICAL.
- **Empty list** → the exact warning text and no other log; no abort; no query call; `validOut` empty.
- **Non-finite position** → error logged, query call count excludes it, counted invalid.
- **Null arguments** → `ArgumentNullException` for `points`, `validOut`, and each constructor argument.
- **`validOut` cleared**: passed in with stale entries → contains only this call's valid points.
- **Radius and areas**: the fake query records radius 0.1 (config) and `NavMesh.AllAreas` for each call; changing the config radius changes the recorded value.
- **Authored position kept**: the fake returns a hit offset from the input → `validOut` holds the input position.
- **Declaration order** preserved for the valid points.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_SpawnPointValidation_tests.cs` — must exist and pass

**Status**: [x] Created — 28 [Test] + 7 [TestCase] rows, all pass (EditMode 7327 / 7198 / 0 failed / 129 skipped, 2026-11-12 18:22:24Z)

---

## Dependencies

- Depends on: Story 001 (Complete)
- Unlocks: Mob Spawning epic (spawn-table story)

---

## Readiness Notes (2026-11-12)

`/story-readiness` NEEDS WORK → fixes 1–9 applied: (1) `SpawnPointInput` / `SpawnValidationResult` structs, caller-owned `validOut` cleared first; (2) basis-point abort rule (float and integer-percent both wrong at some fractions); (3) authored position kept; (4) null → `ArgumentNullException`, non-finite → invalid without a query; (5) CRITICAL = `LogError` with a `"CRITICAL: "` prefix; (6) config read once via `NavigationConfigValues.FromAsset`, `NavMesh.AllAreas`; (7) Mob Spawning hand-off recorded in `EPIC.md` Decision 7 (validator only computes `ShouldAbort`); (8) +5 QA cases; (9) estimate 2 → 2–3 h.

---

## Completion Notes
**Completed**: 2026-11-12
**Criteria**: 8/8 passing (allocation AC verified by code review; no automated allocation test)
**Deviations** (advisory):
- EC-NAV-5 warning carries the `[Navigation] ` prefix and prints the zone as `ZoneID(n)`; content matches the GDD text.
- The validator only computes `ShouldAbort`; the abort belongs to Mob Spawning / Zone Instancing (`EPIC.md` Decision 7 hand-off).
- Test names use camelCase segments inside `test_[scenario]_[expected]` (other Navigation test files use snake_case) — cosmetic.
- Story 009 carries the real `SamplePosition` checks (radius vs baked mesh, edges, `AllAreas`, no mesh loaded → false).
**Test Evidence**: Logic — `tests/EditMode/Navigation/Navigation_SpawnPointValidation_tests.cs` (28 [Test] + 7 [TestCase] rows).
**Code Review**: Complete — unity-specialist CLEAN (suggestions) + qa-tester GAPS, no blockers → "fix all 1–6" applied. Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.
**Tech debt**: none.
