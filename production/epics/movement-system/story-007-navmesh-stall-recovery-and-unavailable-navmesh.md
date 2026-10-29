# Story 007: NavMesh Stall Recovery and Unavailable NavMesh

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (5 gaps) → fixed → Ready
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

- [ ] **Stall counter** (per slot): a tick that reaches the NavMesh sample and commits its hit resets the counter to 0; a tick whose sample misses, or whose hit is rejected by the encoding guard (below), adds 1. A tick that never reaches the sample leaves it unchanged — zero input, Prohibited, failed speed read, non-finite candidate, NavMesh unavailable (decided at readiness: no sample = no evidence either way).
- [ ] **AC-MOV-24 recovery**: recovery runs on the first stepped tick where the counter is ≥ `NAVMESH_STALL_RECOVERY_TICKS` (default 10 → after tick 10's miss, on tick 11), whatever that tick's input; it is skipped while Prohibited or while the NavMesh is unavailable. It replaces that tick's normal move: the server samples the **current committed position** with radius `NAVMESH_RECOVERY_SEARCH_RADIUS` (5.0). A hit that passes the encoding guard → committed as-is (including its Y), counter reset to 0, one warning logged with the EntityID and both positions. The tick's facing from the intent / override still applies; the teleport itself does not change facing.
- [ ] **Recovery miss**: the radius-5.0 query misses (or its hit fails the encoding guard) → `IMovementGeometryErrorSink.ReportStuck(EntityID, Vector3 committedPosition)` is called once and the slot is marked "reported". No further recovery attempts until a committed hit clears the stall (counter 0, flag cleared); normal radius-0.5 sampling continues on moving ticks.
- [ ] **AC-MOV-25 / EC-MOV-12 (defensive branch)**: new `MovementService.SetNavMeshAvailable(bool)`, default `true`. While unavailable, each tick still takes the intent and any facing override (both clear) but applies nothing: no facing, no speed read, no integration, no sample, no counter change. One error is logged on the first rejected tick after becoming unavailable; the once-flag resets when availability is set back to `true`.
- [ ] **F-MOV-6 guard**: `MovementPositionGuard.IsEncodable` (Story 001, |x| or |z| > 327.67 fails) is checked on the value that would be committed — the normal hit and the recovery hit, not the raw candidate. Failing → not committed, previous position held, counts as a miss for the stall counter, one `MovementEncodeOverflow` warning per entity until its next committed hit.

---

## Implementation Notes

- The 5.0 radius is a GDD constant in EC-MOV-11 (not a tuning knob); keep it a named constant `NAVMESH_RECOVERY_SEARCH_RADIUS`.
- **Constructor**: add the overload `MovementService(intents, speeds, sampler, slotIndex, MovementTuningValues tuning, IMovementGeometryErrorSink geometryErrors)` (all required; `tuning.NavMeshStallRecoveryTicks`, same `MovementTuningValues` type Story 004's buffer takes). The existing 4-argument constructor chains to it with the default tuning and a `LoggingMovementGeometryErrorSink` stand-in, so the 8 existing call sites in the Story 005 / 006 tests stay unchanged.
- The geometry-error seam (`IMovementGeometryErrorSink`) and its logging stand-in go in `src/ServerLogic/MovementSystem/`; Zone Instancing's handling (despawn, return to town) is a later decision, not this story.
- `SetNavMeshAvailable` default `true` keeps existing tests unaffected; Story 009 calls it with `NavMeshDataInstance.valid` at S6. Ticks start at S10, so the default never governs a production tick.
- **Readiness finding (ADR-009 / EC-MOV-12 deviation)**: S6 does **not** currently fail startup on a missing NavMesh — `UnitySceneLoader.AddNavMeshData` logs `[StandIn] no zone NavMesh data` and returns `default(NavMeshDataInstance)`, which `StandInZoneNavigation` accepts. So AC-MOV-25 here is only the Movement-side defensive branch. Follow-up for Zone Instancing: S6 fails startup (S-fail, `StartupFailed`) on a null `BakedNavMesh`.
- Per-slot state (counter, reported flag, overflow-warned flag) lives in preallocated arrays like Story 005 / 006 state; `SeedForTesting` resets all three.
- Facing is not changed by the recovery teleport.

---

## Out of Scope

- Zone Instancing's reaction to a geometry error
- Startup step S6 itself (Zone Instancing / ADR-009), including making S6 fail on a missing NavMesh (follow-up above)
- Calling `SetNavMeshAvailable` from the composition (Story 009)

---

## QA Test Cases

- **AC-MOV-24**: Given a fake sampler that misses radius-0.5 queries for ticks 1–10 and hits the radius-5.0 query at (3, 0, 3) — Then ticks 1–10 hold position, tick 11 commits (3, 0, 3), one warning, counter 0, and a subsequent radius-0.5 sample at (3, 0, 3) (fake: hit) commits normally on tick 12.
- **Reset**: misses on ticks 1–9, hit on tick 10 → no recovery on tick 11.
- **Exactly at threshold**: 10 misses → recovery on tick 11, not tick 10 and not tick 12. Tuning 5 → recovery on tick 6.
- **Recovery miss**: radius-5.0 also misses → one `ReportStuck` call with the committed position; ticks 12–20 keep missing → no second call and no second radius-5.0 query; a hit then a new 10-miss stall → recovery runs again.
- **Zero-input tick inside a stall**: 5 misses, 3 zero-input ticks, 5 misses → recovery on the tick after the 10th miss (counter unchanged by the zero-input ticks).
- **Recovery tick input**: counter at threshold and the recovery tick has zero input → recovery still runs. Prohibited → no recovery until un-Prohibited.
- **Recovery facing**: recovery tick with an intent facing 900 → facing 900; with no intent → facing unchanged.
- **AC-MOV-25**: `SetNavMeshAvailable(false)` → 20 ticks of intents (and one override) → position and facing unchanged, sampler and speed source not called, exactly one error log; set `true` then `false` again → one more error.
- **Encoding guard**: hit at (327.70, 0, 0) → not committed, one warning, counter +1; (327.67, 0, 0) → committed. Recovery hit at (0, 0, −327.70) → not committed, `ReportStuck` called.
- **No allocation**: 50 occupied slots stalling through recovery → no GC allocation per tick (as Story 005 / 006 alloc cases).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_NavMeshFailure_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (guard, stall knob), Story 005
- Unlocks: Story 009
