# Story 006: Prohibited State and Auto-Face Override

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-8, CR-MOV-9, States and Transitions, EC-MOV-7, 8, 9; AC-MOV-18, 20 (server side), 23
**Requirement**: `TR-mov-004`, `TR-mov-005`

**ADR Governing Implementation**: ADR-010 (direct calls on an injected interface — no event)
**ADR Decision Summary**: one producer, one consumer → a direct call on an interface the caller is given.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: `IMovementSystem` is the only way other systems change movement; no static access
- Forbidden: central event bus; `Action<object>`

---

## Acceptance Criteria

- [ ] New file `src/ServerLogic/MovementSystem/IMovementSystem.cs` (namespace `IronGrind.MovementSystem`) declares exactly `bool SetProhibited(EntityID id, bool prohibited)` and `bool InjectFacingOverride(EntityID id, Vector3 direction)`; `MovementService` implements it. Both return true when the entity has a movement slot (same lookup as `TryGetPosition`); unknown entity → false, no log, no throw, no state change. `TryGetPosition` / `TryGetFacing` stay on the class (not on the interface).
- [ ] **AC-MOV-18**: while Prohibited, a message with a non-zero direction → no position delta; the message's `FacingAngle` is still applied to the stored facing that tick.
- [ ] **AC-MOV-23**: an Idle entity set Prohibited → no delta that tick or any later tick while set, whatever the intents say. Clearing Prohibited → the next tick with an intent moves again (state Idle; nothing re-armed).
- [ ] CR-MOV-8: Prohibited entry/exit does not touch the auto-face state.
- [ ] **CR-MOV-9**: `InjectFacingOverride(e, dir)` before tick N → on tick N the integrator uses `dir` (normalized, XZ) as `inputDir` instead of the client intent; on tick N+1 the client intent is used again (exactly one tick). A second injection before the same tick replaces the first.
- [ ] **Override facing (Auto-Attack Rule 20: "rotation is set to face the target AND the movement direction is redirected")**: on tick N the stored facing = `MovementIntentMessageCodec.NormalizeFacing(round(atan2(dir.x, dir.z) × 1800 / π))` (Unity yaw: 0 = +Z, clockwise positive, wire form ×10). It replaces the client message's `FacingAngle` for that tick; on tick N+1 the client's `FacingAngle` is stored again.
- [ ] **EC-MOV-8**: an override and a forward client intent in the same tick → the override wins for that tick.
- [ ] **EC-MOV-9 / AC-MOV-20 (server part)**: an override whose candidate has no NavMesh hit → zero displacement that tick; the next tick the client intent (auto-run forward) moves normally. The server has no auto-run flag (CR-MOV-5) — "auto-run not cancelled" is a client fact, checked in Story 011.
- [ ] **Override while Prohibited** (decided at readiness 2026-10-29; GDD silent): Prohibited wins on position — no delta, no speed read, no sampler call. The override's facing is still applied (CR-MOV-8 permits facing rotation while Prohibited) and the override is consumed (tick N+1 uses the client facing again).
- [ ] **Zero or non-finite override**: XZ length 0, or any non-finite component → a zero-direction override: still consumed, still beats the client intent (zero displacement that tick, no normalize, no sampler call), and stored facing is left unchanged (the client's `FacingAngle` is not applied either).

---

## Implementation Notes

- **No setter exists yet**: no system sets Prohibited today (Status Effects has no stun or root; Auto-Attack Combat and the Skill System have no epic). Tests call the interface directly; the epic's Next Step records that a later epic wires the caller.
- **Performance**: no allocation per tick (manifest guardrail); both new per-slot fields are preallocated arrays sized `ZoneSlotPool.MAX_PLAYERS_PER_ZONE`, O(players) per tick.
- Fills the `ApplyProhibitedHook` placeholder in `MovementService.StepSlot` (Story 005); the client intent is still taken every tick (the buffer clears on take), so a skipped intent is never replayed.

- The Prohibited flag and the pending override are two more preallocated per-slot fields in `MovementService` (Story 005).
- The override is consumed in `Run` whether or not it produced movement.
- Story 008 clears both on zone exit (EC-MOV-13/14); here only their in-tick behaviour.

---

## Out of Scope

- Story 008: clearing on zone exit, Prohibited cleared on zone entry
- Story 011: client auto-run cancel on Prohibited entry (AC-MOV-19)
- Story 012: client facing-only messages while Prohibited

---

## QA Test Cases

- **AC-MOV-18**: Given Prohibited, intent dir (1, 0), facing 450 — Then position unchanged, facing 450, sampler not called.
- **AC-MOV-23**: Prohibited at tick 10 → ticks 10–20 with full intents, position constant; cleared at tick 21 → tick 21 moves 0.25.
- **Override one tick**: client intent (1, 0, 0) every tick; override (0, 0, 1) before tick 5 → tick 5 moves +Z 0.25, ticks 4 and 6 move +X.
- **EC-MOV-8**: same as above, asserting the client message was ignored only on tick 5.
- **EC-MOV-9**: override + fake sampler no-hit on tick 5 → zero displacement; tick 6 moves on the client intent.
- **Double inject**: two overrides before tick 5 → the second's direction is used, then nothing on tick 6.
- **Override while Prohibited**: Prohibited, override (0, 0, 1), client intent (1, 0) facing 450 on tick 5 → position unchanged, sampler not called, facing 0; tick 6 (still Prohibited, client facing 450) → facing 450.
- **Override facing**: override (1, 0, 0) → facing 900; (0, 0, −1) → 1800; (−1, 0, 0) → 2700; client facing on the same tick ignored, restored on tick N+1.
- **Zero / non-finite override**: (0, 1, 0) and (NaN, 0, 0) with a client intent (1, 0) facing 450 → zero displacement, sampler not called, facing unchanged from the previous tick; tick N+1 moves +X.
- **No allocation**: 50 slots, half Prohibited, half with an override, one tick inside `Is.Not.AllocatingGCMemory()` after warm-up.
- **Unknown entity**: both calls return false, no log, no state change.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_ProhibitedAndOverride_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Unlocks: Story 008
