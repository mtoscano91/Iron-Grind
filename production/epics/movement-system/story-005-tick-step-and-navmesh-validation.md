# Story 005: Movement Tick Step and NavMesh Validation

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-6 steps 1–5, CR-MOV-7, CR-MOV-10 (facing on every message); AC-MOV-21
**Requirement**: `TR-mov-002`

**ADR Governing Implementation**: ADR-002 (Decision 1 tick ordering, Decision 4 process-global NavMesh, main thread only); ADR-014 (systems tick inside the simulation step, order listed by the composition root)
**ADR Decision Summary**: position integration runs last in the simulation; `UnityEngine.AI` is called on the main thread only; one zone per process, so `NavMesh.SamplePosition` queries the one loaded zone mesh.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH — `NavMesh.SamplePosition` in a `UNITY_SERVER` headless build is unverified (ADR-002 Verification Required); EditMode tests use a seam, the Unity adapter gets one Editor test with a real baked NavMesh if one exists in the project, otherwise its verification is recorded as open.
**Engine Notes**: `NavMesh.SamplePosition(Vector3, out NavMeshHit, float, int)` — stable API; `NavMesh.AllAreas`.

**Control Manifest Rules (this layer)**:
- Required: `NavMesh*` APIs on the main thread only — ADR-002
- Forbidden: runtime NavMesh baking (CR-MOV-7, AC-MOV-21)
- Guardrail: no allocation per tick; O(players) per tick

---

## Acceptance Criteria

- [ ] `MovementService` (implements `IZoneSimulationStep`) keeps per-player-slot state in preallocated arrays: committed position, facing angle (×10 short or degrees — one form, documented), last valid speed, and the flags later stories add.
- [ ] `Run(tick, dt)` for each occupied player slot, in this order (CR-MOV-6): (1) take the buffered intent (Story 004) or zero; (2) apply the intent's `FacingAngle` to the stored facing **whenever a message was received**, even with a zero direction; (3) Prohibited check (hook only — Story 006 fills it); (4) integrate (Story 003); (5) validate; (6) commit.
- [ ] An `INavMeshSampler.TrySample(Vector3 candidate, float radius, out Vector3 hit)` seam; the Unity adapter calls `NavMesh.SamplePosition(candidate, out hit, radius, NavMesh.AllAreas)` with radius 0.5. Hit → committed = `hit.position` (including its Y); no hit → committed = previous position.
- [ ] Zero-input ticks call neither the integrator nor the sampler (F-MOV-1 guard; position unchanged).
- [ ] A read API for other systems: `TryGetPosition(EntityID, out Vector3)` and `TryGetFacing(EntityID, out short)`; unknown entity → false, no log.
- [ ] **AC-MOV-21**: no runtime bake — the code contains no `NavMeshBuilder` / `NavMeshSurface.BuildNavMesh` / `UpdateNavMeshData` call (an architecture test greps `src/ServerLogic/MovementSystem/` for these names), and over a 60-second simulated run (1,200 ticks) the sampler is called once per moving tick per moving player and nothing else touches NavMesh.
- [ ] No feedback on a clamp (CR-MOV-7): a no-hit tick logs nothing (Story 007 adds the stall counter).

---

## Implementation Notes

- Files: `src/ServerLogic/MovementSystem/MovementService.cs`, `INavMeshSampler.cs`, `UnityNavMeshSampler.cs`.
- *(From Story 003 code review, 2026-10-29)* `MovementSpeedResolver.TryResolve` logs one `Debug.LogError` per failing call, so a persistently failing entity would log at 20 Hz. When wiring the per-slot "last valid speed" here, log only on the valid → invalid transition (or rate-limit per slot) and keep the stored last valid speed inside [0.5, 20.0]. Decide at this story's readiness.
- Slot → EntityID through the existing `IEntitySlotIndex` / `ZoneSlotPoolEntityIndex` pattern (Status Effects 009) or `ZoneSlotPool` directly — reuse, do not add a third mapping.
- Entity registration (initial position, facing) comes in Story 008; for this story tests register through an `internal` seeding method.
- Mob positions are not moved here (Navigation epic, ADR-002 phase 4 via `GetCurrentVelocity`).

---

## Out of Scope

- Story 006: Prohibited, auto-face override
- Story 007: stall recovery, NavMesh missing, encoding guard
- Story 008: entry/exit, snapshot position source
- Story 009: composition order and broadcast

---

## QA Test Cases

- **Hit**: Given a player at (0, 0, 0), intent (0, 0, 1), speed 5.0, a fake sampler that returns (0, 0.2, 0.24) — Then committed (0, 0.2, 0.24); the sampler was called with candidate (0, 0, 0.25) and radius 0.5.
- **No hit**: fake sampler returns false → committed stays (0, 0, 0).
- **Facing with zero direction**: message dir (0, 0), facing 900 → stored facing 900, position unchanged, sampler not called.
- **No message**: facing and position unchanged.
- **Order**: a recording fake shows intent read → speed read → sampler → commit, once per moving slot.
- **Two players**: independent state; one moving, one idle → one sampler call.
- **AC-MOV-21**: source grep for bake APIs is empty; 1,200 ticks with two moving players → sampler count 2,400, no other NavMesh call (the adapter is the only `UnityEngine.AI` user in the folder).
- **Read API**: unknown EntityID → false; known → the committed values.
- **No allocation**: 50 moving players, one tick, inside `Is.Not.AllocatingGCMemory()` after warm-up.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_TickStep_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003; Story 004 (intent buffer — or a fake of its read side)
- Unlocks: Stories 006, 007, 008, 009
