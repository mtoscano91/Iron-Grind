# Story 004: Intent Intake — Sender Check, Stale Discard, Last-Wins

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
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

- [ ] `MovementInboundRegistration` registers the Story 002 type with the dispatcher (U-U, not held, body 10) and hands each decoded message to the movement intent buffer.
- [ ] **AC-MOV-17 / EC-MOV-16**: a message whose `SenderEntityID` differs from the connection's EntityID (from the connection character directory) → discarded, no buffer write, one security log line containing both the received and the authenticated EntityID. **Readiness checks** whether the dispatcher or guard chain already performs this check for C→S envelopes; if it does, this AC becomes a test that it covers this type, not new code.
- [ ] **AC-MOV-15 / F-MOV-7**: `TickNumber < serverCurrentTick − STALE_TICK_TOLERANCE` → discarded; the entity's buffered intent for the tick is unchanged.
- [ ] **AC-MOV-16 / EC-MOV-22**: when `serverCurrentTick ≤ STALE_TICK_TOLERANCE` (0–3 at default) every message is accepted — `TickNumber = 0` at tick 0, 1, 2, 3 is processed; at tick 4 a `TickNumber = 0` message is stale. No uint underflow.
- [ ] **AC-MOV-28 / EC-MOV-17**: two accepted messages for the same entity in one tick → only the last received is kept (one buffered intent per entity per tick).
- [ ] A tick with no accepted message → the buffer reports "no intent" (= zero input, CR-MOV-10).
- [ ] The intent buffer is preallocated (one slot per player slot, `MAX_PLAYERS_PER_ZONE`) and cleared after the tick reads it (Story 005 consumes it).
- [ ] Each test names which drop it exercises: the ADR-014 intake `SequenceNumber` drop (Networking Core, not re-implemented here) or the F-MOV-7 `TickNumber` drop (this story).

---

## Implementation Notes

- Pattern: `EquipmentInboundRegistration` + `SetTargetRequestHandler` (an existing U-U-ish request path, `src/ServerLogic/Networking/InboundDispatch/`).
- The handler maps connection → EntityID through the existing `IConnectionCharacterDirectory` / `ZoneConnectionCharacterDirectory`; then EntityID → player slot index for the buffer.
- `serverCurrentTick` is the tick the dispatcher passes to the handler (`InboundRequestContext`), not a fresh read.
- Tests use the real `InboundRequestDispatcher` (Integration type), as `EquipmentSystem_RequestHandler_tests.cs` does.

---

## Out of Scope

- Story 005: consuming the buffered intent in the tick
- Story 006: Prohibited discarding the direction (the message is still accepted here — facing applies)
- Story 008: flushing the buffer on zone exit

---

## QA Test Cases

- **AC-MOV-17**: Given connection A mapped to EntityID 5 — When a message with SenderEntityID 6 is dispatched — Then no buffered intent for 5 or 6, one log line matching `/5.*6|6.*5/` (`LogAssert.Expect`), nothing else logged.
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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (tolerance), Story 002 (type + codec)
- Unlocks: Story 005 (reads the buffer), Story 009 (wiring)
