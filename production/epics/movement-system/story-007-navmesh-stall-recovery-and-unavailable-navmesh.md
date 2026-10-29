# Story 007: NavMesh Stall Recovery and Unavailable NavMesh

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — EC-MOV-11, EC-MOV-12, F-MOV-6 (encoding guard), EC-MOV-20; AC-MOV-24, 25
**Requirement**: `TR-mov-006`

**ADR Governing Implementation**: ADR-009 (startup S6: scene load → NavMesh → nav init → bounds; startup failure handling); ADR-002
**ADR Decision Summary**: the zone loads its baked NavMesh at S6 and fails startup when it cannot; NavMesh queries run on the main thread against the one zone mesh.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-002 `UNITY_SERVER` NavMesh verification still open)
**Engine Notes**: as Story 005 — all NavMesh access through `INavMeshSampler`.

**Control Manifest Rules (this layer)**:
- Required: startup failure follows the ADR-009 S-step failure path (exit code, teardown), not a Movement-local abort
- Guardrail: no allocation per tick

---

## Acceptance Criteria

- [ ] A per-slot consecutive-miss counter: every tick with a candidate and no hit increments it; a hit resets it to 0; zero-input ticks leave it unchanged (**decided at readiness** whether a zero-input tick resets it — the GDD says "consecutive ticks without a valid hit", and zero-input ticks do not sample).
- [ ] **AC-MOV-24**: when the counter reaches `NAVMESH_STALL_RECOVERY_TICKS` (10) after tick 10's miss, on tick 11 the server samples the **current committed position** with radius 5.0; a hit → the entity is moved there (teleport), counter reset, one warning logged with the EntityID and both positions.
- [ ] Recovery still finds no hit → the entity is flagged for Zone Instancing as a geometry error through a seam (`IMovementGeometryErrorSink.ReportStuck(EntityID, Vector3)`, logging stand-in until Zone Instancing reacts); reported once per stall, not every tick.
- [ ] **AC-MOV-25 / EC-MOV-12**: when the zone has no NavMesh (the sampler reports "unavailable", or the composition marks movement disabled because S6 found no `BakedNavMesh`), every intent is rejected and no integration happens; one error is logged once, not per tick. **Readiness confirms** whether ADR-009's startup already fails the zone on a missing NavMesh (then AC-MOV-25 is a startup test plus this defensive branch).
- [ ] **F-MOV-6 guard**: before commit, a candidate (or `hit.position`) failing Story 001's `IsEncodable` (|x| or |z| > 327.67) → not committed, previous position held, one `MovementEncodeOverflow` warning per entity per stall.

---

## Implementation Notes

- The 5.0 radius is a GDD constant in EC-MOV-11 (not a tuning knob); keep it a named constant `NAVMESH_RECOVERY_SEARCH_RADIUS`.
- The geometry-error seam goes in `src/ServerLogic/MovementSystem/`; Zone Instancing's handling (despawn, return to town) is a later decision, not this story.
- Facing is not changed by the recovery teleport.

---

## Out of Scope

- Zone Instancing's reaction to a geometry error
- Startup step S6 itself (Zone Instancing / ADR-009, already implemented)

---

## QA Test Cases

- **AC-MOV-24**: Given a fake sampler that misses radius-0.5 queries for ticks 1–10 and hits the radius-5.0 query at (3, 0, 3) — Then ticks 1–10 hold position, tick 11 commits (3, 0, 3), one warning, counter 0.
- **Reset**: misses on ticks 1–9, hit on tick 10 → no recovery on tick 11.
- **Exactly at threshold**: 10 misses → recovery on tick 11, not tick 10 and not tick 12. Tuning 5 → recovery on tick 6.
- **Recovery miss**: radius-5.0 also misses → one `ReportStuck` call; ticks 12–20 keep missing → no second call until a hit resets the stall.
- **Zero-input tick inside a stall**: the readiness rule.
- **AC-MOV-25**: sampler "unavailable" → 20 ticks of intents → position unchanged, integrator not called, exactly one error log.
- **Encoding guard**: hit at (327.70, 0, 0) → not committed, one warning; (327.67, 0, 0) → committed.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_NavMeshFailure_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (guard, stall knob), Story 005
- Unlocks: Story 009
