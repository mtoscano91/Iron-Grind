# Story 004: Intent Intake — Sender Check, Stale Discard, Last-Wins

> **Epic**: Movement System
> **Status**: Complete — 2026-10-29 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (4 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-10, F-MOV-7, EC-MOV-16, 17, 22; AC-MOV-15, 16, 17, 28
**Requirement**: `TR-mov-003` (intake part)

**ADR Governing Implementation**: ADR-014 (inbound request dispatch); ADR-004 (NGO tick = `TickNumber` source)
**ADR Decision Summary**: requests reach game code only through the dispatcher; the intake drops a stale U-U `SequenceNumber` per connection and type before dispatch (counted, not logged); `MovementIntentMessage` is a U-U request, not held during an irreversible write; handlers run in Pass B on the tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (dispatcher already verified — Networking Core Stories 037–039)
**Engine Notes**: none new.

**Control Manifest Rules (this layer)**:
- Required: register through an inbound registration class with a descriptor (held = no, body 10) before the dispatcher is sealed
- Forbidden: per-request allocation on the intake/dispatch path; renting from `ArrayPool<T>.Shared`
- Required: security events logged once per event with both IDs

---

## Acceptance Criteria

- [x] `MovementInboundRegistration` registers the Story 002 type with the dispatcher (U-U, not held, body 10, new tag `RpcTypeTag.MovementIntent`) and hands each decoded message to the movement intent buffer.
- [x] `RpcTypeTag.MovementIntent` is added with no rate limit: `CrossCuttingRpcGuardChain.GetRequiredTickGap` returns 0 for it (as `SetTarget`), so two messages for the same entity in one tick both pass the guard chain (needed by AC-MOV-28).
- [x] **AC-MOV-17 / EC-MOV-16** (test of existing code, no new check): the guard chain already drops a spoofed sender in Pass B before the handler — Guard 1 (`RejectedUnknownEntity`, received EntityID unknown) and Guard 4 (`RejectedNotOwner`, received EntityID owned by another connection). Tests cover both cases for this type: handler not called, no buffered intent for either EntityID, one warning naming the received EntityID and the sending `clientId`. **Deviation (readiness 2026-10-29):** the GDD asks for "the session's authenticated entity ID"; the log names the authenticated connection (`clientId`) instead — extending the guard chain log was declined (it would change the log for every request type). The log is throttled once per (client, result) per tick (ADR-014 Decision 4a).
- [x] EntityID → player slot via `IEntitySlotIndex` (`ZoneSlotPoolEntityIndex`), using `InboundRequestContext.SenderEntityId` (already ownership-checked). No slot (entry/exit window) → message dropped silently: no buffer write, no log.
- [x] **AC-MOV-15 / F-MOV-7**: `TickNumber < serverCurrentTick − STALE_TICK_TOLERANCE` → discarded; the entity's buffered intent for the tick is unchanged.
- [x] **AC-MOV-16 / EC-MOV-22**: when `serverCurrentTick ≤ STALE_TICK_TOLERANCE` (0–3 at default) every message is accepted — `TickNumber = 0` at tick 0, 1, 2, 3 is processed; at tick 4 a `TickNumber = 0` message is stale. No uint underflow.
- [x] **AC-MOV-28 / EC-MOV-17**: two accepted messages for the same entity in one tick → only the last received is kept (one buffered intent per entity per tick).
- [x] A tick with no accepted message → the buffer reports "no intent" (= zero input, CR-MOV-10).
- [x] The buffered `FacingAngle` is passed through `MovementIntentMessageCodec.NormalizeFacing` (Story 002) before storing — a received value outside 0–3599 is wrapped, never a reason to drop the message.
- [x] The intent buffer is preallocated (one slot per player slot, `MAX_PLAYERS_PER_ZONE`) and cleared after the tick reads it (Story 005 consumes it).
- [x] The buffer implements Story 005's `IMovementIntentSource.TryTakeIntent(int playerSlot, out MovementIntentMessage intent)`: true + the last accepted message for that slot this tick, then that slot is cleared; false when none (added at Story 005 readiness, 2026-10-29).
- [x] The buffer records the dispatch tick of each write; `TryTakeIntent` returns true only when that tick equals `IServerTickSource.ServerTickNumber` (requests dispatch before the simulation in the same tick, `ZoneTickPipeline`). An older entry (slot not taken that tick, e.g. not yet occupied by `MovementService`) → cleared, false. Constructor: `(IServerTickSource tickSource, IEntitySlotIndex slotIndex, MovementTuningValues tuning)`.
- [x] Each test names which drop it exercises: the ADR-014 intake `SequenceNumber` drop (Networking Core, not re-implemented here) or the F-MOV-7 `TickNumber` drop (this story).

---

## Implementation Notes

- Pattern: `EquipmentInboundRegistration` + `SetTargetRequestHandler` (an existing U-U-ish request path, `src/ServerLogic/Networking/InboundDispatch/`).
- The handler does **not** look up the connection's EntityID: by the time it runs, `context.SenderEntityId` has passed the guard chain's ownership check. It maps that EntityID → player slot through `IEntitySlotIndex` (readiness 2026-10-29; replaces the earlier `IConnectionCharacterDirectory` note).
- New `RpcTypeTag.MovementIntent` in `src/ServerLogic/Networking/RpcGuards/RpcTypeTag.cs`; `GetRequiredTickGap` returns 0 for it.
- Stale check without uint underflow: `currentTick > tolerance && messageTick < currentTick - (uint)tolerance` (the `MovementTuningValues` doc example omits the first clause).
- `serverCurrentTick` is the tick the dispatcher passes to the handler (`InboundRequestContext`), not a fresh read.
- Tests use the real `InboundRequestDispatcher` (Integration type), as `EquipmentSystem_RequestHandler_tests.cs` does.

---

## Out of Scope

- Story 005: consuming the buffered intent in the tick
- Story 006: Prohibited discarding the direction (the message is still accepted here — facing applies)
- Story 008: flushing the buffer on zone exit

---

## QA Test Cases

- **AC-MOV-17**: Given connection A (clientId 7) owning EntityID 5 — When a message with SenderEntityID 6 is dispatched, (a) 6 unknown, (b) 6 owned by clientId 8 — Then handler not called, no buffered intent for 5 or 6, one warning matching a regex containing `6` and `clientId=7` (`LogAssert.Expect`), nothing else logged.
- **Same-tick pass-through**: two messages from the same entity in one tick both reach the handler (no rate limit on `MovementIntent`).
- **No slot**: an owned EntityID with no slot in `IEntitySlotIndex` → no buffer write, no log.
- **Leftover entry**: write at tick N, `TryTakeIntent` at tick N+1 → false, and the slot is cleared.
- **AC-MOV-15**: tick 100, tolerance 3: TickNumber 97 → accepted; 96 → discarded and the previously buffered intent (if any) unchanged.
- **AC-MOV-16**: ticks 0, 1, 2, 3 with TickNumber 0 → accepted; tick 4 with TickNumber 0 → discarded. Also TickNumber greater than the current tick (client ahead) → accepted (F-MOV-7 only discards old ones).
- **AC-MOV-28**: two messages, same tick, dirs (1, 0) then (0, 1) → buffered dir (0, 1); reverse order → (1, 0).
- **Sequence drop**: a message with a stale `SequenceNumber` never reaches the handler (asserts the intake behaviour for this type, ADR-014 step 4).
- **Unknown connection / no character**: dropped by the dispatcher, handler not called.
- **No allocation**: dispatching 50 messages in one tick inside `Is.Not.AllocatingGCMemory()` (after warm-up).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_IntentIntake_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/MovementSystem/MovementSystem_IntentIntake_tests.cs`, 31 methods, all passing (EditMode 5124 / 4995 passed / 0 failed / 129 skipped, 2026-10-29)

---

## Dependencies

- Depends on: Story 001 (tolerance), Story 002 (type + codec)
- Unlocks: Story 005 (reads the buffer), Story 009 (wiring)

---

## Completion Notes

**Completed**: 2026-10-29
**Criteria**: 13/13 passing (all automated; none deferred)
**Deviations** (advisory):
1. AC-MOV-17: the guard-chain warning names the received EntityID and the sending `clientId`, not the connection's own EntityID (readiness decision; guard-chain log text unchanged). The guard log prints the entity as `EntityID(n)`.
2. TR-mov-003 is a placeholder, not yet in `docs/architecture/tr-registry.yaml` (same as the other epics).
**Scope**: within the story; `RpcTypeTag.cs` and `CrossCuttingRpcGuardChain.cs` were planned edits. Registration not wired into the composition root yet (Story 009).
**Files**: `src/ServerLogic/MovementSystem/MovementIntentBuffer.cs`, `MovementInboundRegistration.cs` (new); `src/ServerLogic/Networking/RpcGuards/RpcTypeTag.cs` (`MovementIntent = 9`), `CrossCuttingRpcGuardChain.cs` (tick gap 0); test file above; `.meta` files.
**Test Evidence**: Integration — `tests/EditMode/MovementSystem/MovementSystem_IntentIntake_tests.cs` (real dispatcher + guard chain)
**Code Review**: Complete — `/code-review` 2026-10-29: APPROVED WITH SUGGESTIONS (unused using, out-of-range slot test, stale-after-leftover and overwrite-across-ticks tests, `LogAssert.NoUnexpectedReceived` on three drop tests, slot constant instead of 50), "fix all" applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
