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

- [ ] `IMovementSystem` exposes `SetProhibited(EntityID, bool)` and `InjectFacingOverride(EntityID, Vector3 direction)`; `MovementService` implements it. Unknown entity → no-op returning false (no throw).
- [ ] **No setter exists yet**: no system sets Prohibited today (Status Effects has no stun or root; Auto-Attack Combat and the Skill System have no epic). Tests call the interface directly; the epic's Next Step records that a later epic wires the caller.
- [ ] **AC-MOV-18**: while Prohibited, a message with a non-zero direction → no position delta; the message's `FacingAngle` is still applied to the stored facing that tick.
- [ ] **AC-MOV-23**: an Idle entity set Prohibited → no delta that tick or any later tick while set, whatever the intents say. Clearing Prohibited → the next tick with an intent moves again (state Idle; nothing re-armed).
- [ ] CR-MOV-8: Prohibited entry/exit does not touch the auto-face state.
- [ ] **CR-MOV-9**: `InjectFacingOverride(e, dir)` before tick N → on tick N the integrator uses `dir` (normalized, XZ) as `inputDir` instead of the client intent; on tick N+1 the client intent is used again (exactly one tick). A second injection before the same tick replaces the first.
- [ ] **EC-MOV-8**: an override and a forward client intent in the same tick → the override wins for that tick.
- [ ] **EC-MOV-9 / AC-MOV-20 (server part)**: an override whose candidate has no NavMesh hit → zero displacement that tick; the next tick the client intent (auto-run forward) moves normally. The server has no auto-run flag (CR-MOV-5) — "auto-run not cancelled" is a client fact, checked in Story 011.
- [ ] Override while Prohibited: **decided at readiness** (the GDD is silent; candidate: Prohibited wins — position locked — and the override is consumed).
- [ ] Zero-vector override → treated as no input for that tick (no normalize of zero).

---

## Implementation Notes

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
- **Override while Prohibited**: the readiness rule.
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
