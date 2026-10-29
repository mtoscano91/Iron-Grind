# Story 005: Movement Tick Step and NavMesh Validation

> **Epic**: Movement System
> **Status**: Complete — 2026-10-29 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (6 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-6 steps 1–5, CR-MOV-7, CR-MOV-10 (facing on every message); AC-MOV-21
**Requirement**: `TR-mov-002`

**ADR Governing Implementation**: ADR-002 (Decision 1 tick ordering, Decision 4 process-global NavMesh, main thread only); ADR-014 (systems tick inside the simulation step, order listed by the composition root)
**ADR Decision Summary**: position integration runs last in the simulation; `UnityEngine.AI` is called on the main thread only; one zone per process, so `NavMesh.SamplePosition` queries the one loaded zone mesh.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH — `NavMesh.SamplePosition` in a `UNITY_SERVER` headless build is unverified (ADR-002 Verification Required); EditMode tests use the `INavMeshSampler` seam. **No baked NavMesh exists under `Assets/` (checked at readiness 2026-10-29), so `UnityNavMeshSampler` gets no automated test in this story; its verification stays open under ADR-002 Verification Required.** AC-MOV-21 is still covered by the architecture grep test and the fake-sampler call count.
**Engine Notes**: `NavMesh.SamplePosition(Vector3, out NavMeshHit, float, int)` — stable API; `NavMesh.AllAreas`.

**Control Manifest Rules (this layer)**:
- Required: `NavMesh*` APIs on the main thread only — ADR-002
- Forbidden: runtime NavMesh baking (CR-MOV-7, AC-MOV-21)
- Guardrail: no allocation per tick; O(players) per tick

---

## Acceptance Criteria

- [ ] `MovementService` (implements `IZoneSimulationStep`) keeps per-player-slot state in preallocated arrays sized `ZoneSlotPool.MAX_PLAYERS_PER_ZONE`: `occupied` flag, `EntityID`, committed position, facing as a **`short` ×10 (0–3599, the wire form)**, last valid speed (`0f` = none), `speedFailing` flag, and the flags later stories add.
- [ ] **Slots**: `Run` loops over slot indices 0..`MAX_PLAYERS_PER_ZONE − 1` and skips slots not `occupied` (no `GetPlayerEntityIDsInZone`, which returns a list). Slots are filled by an `internal` seeding method (`SeedForTesting(int slot, EntityID id, Vector3 position, short facing)`; resets last valid speed to `0f` and `speedFailing` to false) now, by Story 008 later. EntityID → slot uses an injected `IEntitySlotIndex` (`ZoneSlotPoolEntityIndex`, reused from Status Effects — no third mapping).
- [ ] **Intent seam**: `IMovementIntentSource.TryTakeIntent(int playerSlot, out MovementIntentMessage intent)` — false = no message this tick (zero input, CR-MOV-10); a take clears that slot. Story 004's buffer implements it; this story's tests use a fake. Direction = `MovementIntentMessageCodec.DecodeDirection(intent.DirX, intent.DirZ)` → `(x, 0, z)`. Facing is stored as received (Story 004 normalizes before buffering).
- [ ] `Run(tick, dt)` for each occupied player slot, in this order (CR-MOV-6): (1) take the intent or zero; (2) apply the intent's `FacingAngle` to the stored facing **whenever a message was received**, even with a zero direction; (3) Prohibited check (hook only — Story 006 fills it); (4) if the direction is zero → stop (no speed read, no integrate, no sample); else read the speed, integrate (Story 003); (5) validate; (6) commit.
- [ ] **Speed (injected `IMovementSpeedSource`)**: read **only on moving ticks** via a new non-logging overload `MovementSpeedResolver.TryResolve(source, id, ref lastValidSpeed, out float vEff, out bool readFailed)`; the existing logging overload is rewritten to call it (Story 003 tests stay green, unchanged). `TryResolve` false → position unchanged, sampler not called. **Logging**: `MovementService` logs one `Debug.LogError` (`[Movement]` prefix) when a slot's read goes valid → failing (`speedFailing` false → true), nothing while it stays failing, and clears `speedFailing` on the next valid read (a later failure logs again). The stored last valid speed is always inside [0.5, 20.0] because only valid reads write it.
- [ ] An `INavMeshSampler.TrySample(Vector3 candidate, float radius, out Vector3 hit)` seam; the Unity adapter calls `NavMesh.SamplePosition(candidate, out hit, radius, NavMesh.AllAreas)` with radius 0.5. Hit → committed = `hit.position` (including its Y); no hit → committed = previous position.
- [ ] Zero-input ticks call neither the integrator nor the sampler (F-MOV-1 guard; position unchanged).
- [ ] A read API for other systems: `TryGetPosition(EntityID, out Vector3)` and `TryGetFacing(EntityID, out short)`; unknown entity → false, no log.
- [ ] **AC-MOV-21**: no runtime bake — the code contains no `NavMeshBuilder` / `NavMeshSurface.BuildNavMesh` / `UpdateNavMeshData` call (an architecture test greps `src/ServerLogic/MovementSystem/` for these names), and over a 60-second simulated run (1,200 ticks) the sampler is called once per moving tick per moving player and nothing else touches NavMesh.
- [ ] No feedback on a clamp (CR-MOV-7): a no-hit tick logs nothing (Story 007 adds the stall counter).

---

## Implementation Notes

- Files: `src/ServerLogic/MovementSystem/MovementService.cs`, `INavMeshSampler.cs`, `UnityNavMeshSampler.cs`, `IMovementIntentSource.cs`; modify `MovementSpeedResolver.cs` (add the non-logging overload).
- *(From Story 003 code review, 2026-10-29 — decided at readiness)* Per-call resolver logging would log at 20 Hz for a persistently failing entity → the service uses the non-logging overload and logs on the valid → failing transition only (AC above).
- Constructor: `MovementService(IMovementIntentSource intents, IMovementSpeedSource speeds, INavMeshSampler sampler, IEntitySlotIndex slotIndex)`; null → `ArgumentNullException`.
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
- **Order**: a recording fake shows intent take → speed read → sampler → commit, once per moving slot; an idle slot (no message, or zero direction) records the intent take only — no speed read, no sampler.
- **Speed failure**: moving slot, fake speed source valid 5.0 on tick 1, failing on ticks 2–4, valid on tick 5, failing on tick 6 → errors logged on ticks 2 and 6 only (`LogAssert.Expect` ×2, `LogAssert.NoUnexpectedReceived`); ticks 2–4 move with 5.0. Failing from the first tick → no movement, sampler not called, one error.
- **Resolver overload**: non-logging `TryResolve(..., out readFailed)` → no log on failure, `readFailed` true; the Story 003 tests still pass unchanged.
- **Slot lookup**: an EntityID the index maps to a slot movement never seeded → `TryGetPosition` false.
- **Two players**: independent state; one moving, one idle → one sampler call.
- **AC-MOV-21**: source grep for bake APIs is empty; 1,200 ticks with two moving players → sampler count 2,400, no other NavMesh call (the adapter is the only `UnityEngine.AI` user in the folder).
- **Read API**: unknown EntityID → false; known → the committed values.
- **No allocation**: 50 moving players, one tick, inside `Is.Not.AllocatingGCMemory()` after warm-up.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_TickStep_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/MovementSystem/MovementSystem_TickStep_tests.cs`, 32 methods / 33 cases, all passing (EditMode 5093 / 4964 passed / 0 failed / 129 skipped, 2026-10-29)

---

## Dependencies

- Depends on: Story 003 (Complete); Story 004 not required first — this story defines `IMovementIntentSource` and tests with a fake; Story 004's buffer implements it
- Unlocks: Stories 006, 007, 008, 009

---

## Completion Notes

**Completed**: 2026-10-29
**Criteria**: 9/9 passing (all automated; none deferred)
**Deviations** (advisory):
1. `UnityNavMeshSampler` has no automated test (no baked NavMesh in the project); headless `NavMesh.SamplePosition` verification stays open under ADR-002 Verification Required. With no mesh loaded it returns false, so moving players hold position until Story 007's stall handling.
2. Code-review additions beyond the ACs: a non-finite candidate holds position without sampling; the read API also requires the slot's seeded EntityID to match (guards a slot reassigned before re-seeding).
3. `MovementSpeedResolver`: `ResolveCore` + `FailureReason` made `internal` and the failure text extracted to `internal DescribeFailure`, so the service's transition log names the cause with the same text as the logging overload.
4. TR-mov-002 is a placeholder, not yet in `docs/architecture/tr-registry.yaml` (same as the other epics).
**Scope**: story 009 Implementation Notes gained a note (confirm the tick runs on the Unity main thread) — documentation only, user-approved.
**Files**: `src/ServerLogic/MovementSystem/MovementService.cs`, `IMovementIntentSource.cs`, `INavMeshSampler.cs`, `UnityNavMeshSampler.cs` (new); `MovementSpeedResolver.cs` (non-logging overload, internal core); test file above; `.meta` files.
**Test Evidence**: Logic — `tests/EditMode/MovementSystem/MovementSystem_TickStep_tests.cs`
**Code Review**: Complete — `/code-review` 2026-10-29: CHANGES REQUIRED (read API returned a reassigned slot's data) + suggestions (cause in transition log, non-finite candidate guard, code-form source greps with AllDirectories, commit-order / miss / take-clears tests), "fix all" applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
