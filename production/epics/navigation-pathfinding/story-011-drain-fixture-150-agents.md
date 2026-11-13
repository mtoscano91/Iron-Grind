# Story 011: 150-Agent Drain Fixture (AC-NAV-20)

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — F-NAV-3, EC-NAV-9, AC-NAV-20, OQ-NAV-2
**Requirement**: `TR-nav-009` (fixture half; the bake is Story 008)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (Decisions 1, 2)
**ADR Decision Summary**: Paths are async; `nextPosition` updates during `pathPending` may extend the pending window to 2 ticks. With R = 20 a burst of 150 drains in 8 ticks, and every mob is serviced within 10 ticks.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (real pathfinding timing on Unity 6.3)
**Engine Notes**: closes OQ-NAV-2 (baked mesh + 150-agent fixture).

**Control Manifest Rules (this layer)**:
- Required: phase order sync → drain → read, every tick
- Guardrail: 16.6 ms frame budget (record the drain-tick cost for OQ-AI-4)

---

## Acceptance Criteria

- [x] **AC-NAV-20 / F-NAV-3** (PlayMode, Story 008 mesh, real adapters): 150 agents claimed across the floor; `SetDestination` for all 150 queued before tick 0; each tick runs through `NavigationPhaseStep.Run` (sync → drain) and steps one frame at 20 Hz. Tick numbering (indices from 0): tick `t` = `Run(t, 0.05f)` → one `yield return null` with `Time.captureDeltaTime = 0.05f` → poll `PathPending`. Drain tick = the tick on which the agent's `SetDestination` is called; serviced tick = the first tick whose poll reads `PathPending == false` (at or after the drain tick). Assert: every entity's serviced tick ≤ 9 (10 ticks); at least 140 of 150 have serviced tick ≤ 7 (8 ticks); drain tick of the i-th queued entity = ⌊i / 20⌋ (0–7).
- [x] The test records per entity the drain tick and the serviced tick, and logs the distribution (count per serviced tick) and the maximum.
- [x] The test records the wall-clock cost of `Run()` on the drain ticks 0–7 (Stopwatch, max ms) as a first data point for OQ-AI-4 (no pass/fail threshold; noted in Completion Notes), and logs `NavMesh.pathfindingIterationsPerFrame` (left at its default) with it.
- [x] If the fixture cannot run reliably in the Editor test runner, the GDD fallback applies: a documented manual run with per-entity timestamps, QA sign-off in `production/qa/evidence/nav-ac20-playtest-[date].md` (ADVISORY).

---

## Implementation Notes

- Spread spawn points on a grid inside the floor (for example 15 × 10, 5 m apart, within ±40 m) and destinations mirrored across the origin, so paths are non-trivial.
- Step frames between ticks so Unity's pathfinding can complete (`yield return null` per tick, with `Time.captureDeltaTime` or fixed waits so the test is deterministic in tick count, not in wall time).
- Determinism (testing standards): fixed positions, no random seeds; the assertion is on tick counts, not milliseconds.
- **Observation through the public API only** (readiness 2026-11-13): the PlayMode assembly has no `InternalsVisibleTo` (ADR-012 Decision 5 forbids adding one), so `IsQueued` / `PendingRequestCount` are not available. Wrap the real `UnityNavAgentFactory` in a recording `INavAgentFactory` decorator (pattern: `RecordingFactory` in `Navigation_UnityAdapter_tests.cs`) whose agents forward to the real `UnityNavAgent`, record the current tick when `SetDestination` is called (drain tick), and expose `PathPending` / `NextPosition` for the per-tick poll.
- **Drive through `NavigationPhaseStep`**: ADR-002 Decision 1 / EC-NAV-7 — only the phase step calls `SyncAgentPositions` and `Tick()`. The test builds `NavigationPhaseStep(service, source)` with a test `IMobCommittedPositionSource` that returns each claimed agent's current `NextPosition`, so sync writes `nextPosition` every tick (exercises EC-NAV-9).
- Claim agents via `MobEventBus.RaiseMobSpawned` (as Story 009's tests do); load the mesh from the same `MESH_ASSET_PATH` with `AssetDatabase` + `NavMesh.AddNavMeshData`, and initialize the service with that instance.
- Clean up in `[UnityTearDown]`: `service.Teardown()` (removes the mesh instance through the real ops), then in `finally` `UnityNavAgentFactory.DestroyPoolRoots()` (150 pooled GameObjects otherwise leak into later PlayMode tests), then `Time.captureDeltaTime = 0`.

---

## Out of Scope

- Enemy AI tick budget profiling at full density (OQ-AI-4, Enemy AI epic) — this story only records the navigation part

---

## QA Test Cases

- **AC-NAV-20**:
  - Given: the Story 008 mesh loaded, 150 agents claimed at grid points, R = 20
  - When: `SetDestination` for all 150 queued before tick 0, then ticks 0–9 of `NavigationPhaseStep.Run` with one frame stepped per tick
  - Then: every entity's serviced tick ≤ 9; ≥ 140 with serviced tick ≤ 7
  - Edge cases: the pending window extended by `nextPosition` updates (EC-NAV-9) — sync runs every tick in the fixture so the extension is exercised
- **Drain schedule**: the drain tick per entity is ⌊index / 20⌋ (0–7) — confirms F-NAV-1 on the real service.
- **Cost record**: maximum drain-tick ms is logged and copied to Completion Notes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Navigation/Navigation_DrainFixture_tests.cs` must exist and pass (BLOCKING once the fixture runs), or the fallback evidence doc with sign-off (ADVISORY)

**Status**: [x] Created — PlayMode `-testFilter Navigation` 21/21 (batch mode 2026-11-13)

---

## Dependencies

- Depends on: Stories 008, 009
- Unlocks: None within the epic

---

## Completion Notes

**Completed**: 2026-11-13
**Criteria**: 4/4 (AC-NAV-20, per-entity record + distribution log, cost record; fallback AC N/A — the fixture runs in the Editor test runner, so AC-NAV-20 is BLOCKING-tested and OQ-NAV-2 is closed)
**Results** (`[Nav011]`, batch PlayMode): serviced-tick distribution t0–t6 = 20 each, t7 = 10; max serviced tick 7; 150/150 by tick 7; 0 paths not `PathComplete`; drain tick = ⌊i/20⌋ for all 150.
**OQ-AI-4 data point**: `NavigationPhaseStep.Run()` main-thread cost — t0 (first call) 6.043 ms, ticks 1–7 max 0.198 ms; excludes pathfinding, which runs in the frame; `pathfindingIterationsPerFrame` pinned at 100. Editor batch mode, not a server build.
**Deviations (advisory)**:
- EC-NAV-9 not provoked: the position source nudges `nextPosition` ±0.05 m while `PathPending`, but wrote 0 nudges — in tick order sync → drain → frame, every path on the Story 008 mesh finishes within the frame after its drain, so `pathPending` never spans a sync. The QA case's "extension is exercised" did not happen; the nudge applies automatically if a path ever takes > 1 frame (denser mesh, lower iteration budget).
- Cost record measures `Run()` main-thread cost only (story wording implied the full drain cost).
**Test Evidence**: Integration — `tests/PlayMode/Navigation/Navigation_DrainFixture_tests.cs` (2 UnityTests), Navigation PlayMode 21/21.
**Code Review**: Complete — `/code-review` 2026-11-13 CHANGES REQUIRED (PathComplete check, EC-NAV-9 nudge, pinned iterations, per-tick ms, renames, helper split) → fixed. Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.
**Tech debt**: none.
