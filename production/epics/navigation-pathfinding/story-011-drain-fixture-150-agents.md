# Story 011: 150-Agent Drain Fixture (AC-NAV-20)

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
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

- [ ] **AC-NAV-20 / F-NAV-3** (PlayMode, Story 008 mesh, real adapters): 150 agents claimed across the floor; `SetDestination` for all on tick 0; the test drives sync → drain each tick and steps frames at 20 Hz. Assert: no mob's request is serviced (drained and `pathPending` false) later than 10 ticks after issue; at least 140 of 150 within 8 ticks.
- [ ] The test records per entity the drain tick and the tick `pathPending` cleared, and logs the distribution and the maximum.
- [ ] The test records the wall-clock cost of the drain ticks (max ms) as a first data point for OQ-AI-4 (no pass/fail threshold; noted in Completion Notes).
- [ ] If the fixture cannot run reliably in the Editor test runner, the GDD fallback applies: a documented manual run with per-entity timestamps, QA sign-off in `production/qa/evidence/nav-ac20-playtest-[date].md` (ADVISORY).

---

## Implementation Notes

- Spread spawn points on a grid inside the floor (for example 15 × 10, 5 m apart, within ±40 m) and destinations mirrored across the origin, so paths are non-trivial.
- Step frames between ticks so Unity's pathfinding can complete (`yield return null` per tick, with `Time.captureDeltaTime` or fixed waits so the test is deterministic in tick count, not in wall time).
- Determinism (testing standards): fixed positions, no random seeds; the assertion is on tick counts, not milliseconds.
- Clean up: `Teardown` the service and remove the mesh in `[UnityTearDown]`.

---

## Out of Scope

- Enemy AI tick budget profiling at full density (OQ-AI-4, Enemy AI epic) — this story only records the navigation part

---

## QA Test Cases

- **AC-NAV-20**:
  - Given: the Story 008 mesh loaded, 150 agents claimed at grid points, R = 20
  - When: `SetDestination` for all 150 on tick 0, then 10 ticks of sync → drain with frames stepped
  - Then: every entity drained and `pathPending == false` by tick 10; ≥ 140 by tick 8
  - Edge cases: the pending window extended by `nextPosition` updates (EC-NAV-9) — sync runs every tick in the fixture so the extension is exercised
- **Drain schedule**: the drain tick per entity is ⌊index / 20⌋ (0–7) — confirms F-NAV-1 on the real service.
- **Cost record**: maximum drain-tick ms is logged and copied to Completion Notes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Navigation/Navigation_DrainFixture_tests.cs` must exist and pass (BLOCKING once the fixture runs), or the fallback evidence doc with sign-off (ADVISORY)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 008, 009
- Unlocks: None within the epic
