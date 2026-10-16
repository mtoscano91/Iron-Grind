# Story 015: Attempt Result Delivery

> **Epic**: Enhancement System
> **Status**: Ready (2026-10-16, `/story-readiness`) — all dependencies Complete: Story 011 (commit orchestration, 2026-10-16), Inventory Story 012 and Story 010 (2026-10-09).
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 16 hours (`/estimate` 2026-10-16: 2.5 days before the split, about 2 days after it; Medium confidence)

> **Created 2026-10-09 by splitting Story 010 (user decision).** Story 010 keeps the codecs and the requests that need no commit. This story is everything a client receives because an attempt was accepted and committed.

> **Split again 2026-10-16 (user decision).** The +9 broadcast (`ServerBroadcast_Enhancement9`, AC-ENH-18 delivery half, AC-NC-47, TR-enh-010) moved to Story 016, which is Blocked on a cross-zone relay ADR (ADR-002 runs one process per zone).

> **Readiness decisions 2026-10-16 (user).** (1) A committed `requestId` is recorded by a new `EnhancementRequestDeduplicator.RecordCommitted(uint requestId)`, called from the delivery callback (the write succeeded) before the result is enqueued; `TryProcess` stays as it is. The value is in memory only; the durable write is TD-069. (2) The observer-signature criterion moved to Story 016, which revises both observer callbacks together with the broadcast.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-11 (pre-commit inventory events must not reach the client before the commit), CR-ENH-15 steps 3, 4, 6b and 9, UI-ENH-2 (`EnhancementAttemptResult`), EC-ENH-1, AC-ENH-13 (delivery half). `design/gdd/networking-wire-protocol.md` — `EnhancementRequestReceived`, `EnhancementAttemptResult`, the Enhancement hold notes of `InventorySlotUpdate` and `InventoryFullSync`, CR-NET-7.7, AC-NC-35, AC-NC-43. `design/gdd/networking-core.md` — CR-NET-5.1 to CR-NET-5.5. `design/gdd/networking-session.md` — EC-NET-9.
**Requirement**: `TR-enh-009` (delivery half)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**:
- **ADR-011: Asynchronous Persistence in the Server Tick Loop (Accepted 2026-10-07)** — the outcome is delivered from the completion callback on the tick, after the write; no `await` in game logic.
- **ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09)** — `Drain` runs before `DispatchTick`; a write completed before tick N has its outcome delivered before any new request of tick N.
- **ADR-004: Networking Library (NGO)** — transport and envelope.

**ADR Decision Summary**: an irreversible outcome reaches a client only after its persistence write succeeded; the result is handled on the tick thread by the completion queue.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved. Plain C# over `EnhancementCommitOrchestrator`, `ClientMessageOutbox` (Networking Core Story 039, usable in EditMode without NGO) and `OwnerInventorySyncSender`. No transport adapter is needed; ADR-004's NGO risk does not apply to this story.

**Control Manifest Rules (Feature layer, v2026-10-09)**:
- Required: every irreversible outcome goes through `IrreversibleOutcomeCoordinator` — here through `EnhancementCommitOrchestrator.Begin` — ADR-011
- Required: the handler's system owns the reply to the client and the request deduplication; the dispatcher does neither — ADR-014
- Required: new code is written in `src/ServerLogic/` (`IronGrind.ServerLogic`); wire types stay in `IronGrind.Foundation` — ADR-012
- Forbidden: keeping `body` after a handler returns — ADR-014
- Forbidden: `await`, `async` methods, `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith`, `async void` in tick-driven feature code — ADR-011
- Forbidden: reading `ICharacterMutationGate`, or holding / rejecting a request because a write is in flight, inside a game system — ADR-011
- Forbidden: an `EventBus` class; class-typed event args — ADR-010

---

## Acceptance Criteria

- [ ] **Acknowledgment after step 4**: an accepted `EnhancementAttemptRequest` (validation passed, scroll consumed — CR-ENH-15 steps 2–4) → exactly one `EnhancementRequestReceived { requestId, itemSlotIndex }` to the owning client, before the outcome is computed. A `Rejected*` result never follows an acknowledgment for the same `requestId`.
- [ ] **Step 4 failure (AC-NC-43, step 4 case)**: with `Inventory.ConsumeItem` forced to fail at step 4, the capture contains zero `EnhancementRequestReceived` and exactly one `EnhancementAttemptResult` with `RejectedScrollNotFound`; an `InventorySlotUpdate` raised later in the same tick is sent, not held.
- [ ] **Result only after the commit (CR-ENH-11, AC-NC-43)**: with the commit held open, the owning client's capture contains zero `InventorySlotUpdate` and zero `EnhancementAttemptResult`. When the commit returns `Success`, it contains the held `InventorySlotUpdate` messages in the order they were raised and exactly one `EnhancementAttemptResult` (`Success` with the new level, or `Destruction` with `newLevel` 0); their order relative to each other is not asserted. When the commit returns a non-Success code, it contains neither message type, including after the rollback.
- [ ] **Cap exemption (AC-NC-35)**: with the owning client's Path 1 queue at its cap of 8, an `EnhancementAttemptResult` enqueued before the flush is at position 1 of that tick's capture and the displaced message is at position 1 of the next tick's; the same holds for `EnhancementRequestReceived`.
- [ ] **Reconnect during the commit (AC-NC-43, reconnect case; EC-ENH-1)**: the owning client disconnected after step 3 and, while the commit is held open, the new connection is announced with `OwnerInventorySyncSender.OnSessionReadySent` (nothing in `src/` sends `SessionReady` yet; the test calls it) → that connection's capture contains zero `InventoryFullSync`; when the commit returns `Success`, exactly one `InventoryFullSync` follows, its entries equal the committed bag, no `InventorySlotUpdate` raised before the commit appears after it, and the new connection's capture contains zero `EnhancementAttemptResult`; when the commit returns a non-Success code, no `InventoryFullSync` appears on that connection.
- [ ] **AC-ENH-13 (disconnect after commit)**: the connection drops after the commit and before the result is sent → after `OnSessionReadySent` for the new connection, its `InventoryFullSync` shows the committed level, the scroll is gone, the slot is unlocked, and no `EnhancementAttemptResult` is sent on the new connection.
- [ ] **`LastEnhancementRequestID` (EC-NET-9, in memory)**: after a committed attempt with `requestId = X`, the character's `EnhancementRequestDeduplicator.LastEnhancementRequestId` is `X` (set by `RecordCommitted` before the result is enqueued) and a second `EnhancementAttemptRequest` with `requestId = X` is dropped as Story 010's duplicate criterion states; after a failed commit, a rejection or a step 4 failure it is unchanged. Not durable across a restart (TD-069).
- [ ] **`RecordCommitted`**: `EnhancementRequestDeduplicator.RecordCommitted(uint requestId)` sets `LastEnhancementRequestId` to `requestId`; `TryProcess` behaves as before (its existing tests pass unchanged). A character with no deduplicator (`IEnhancementRequestDedupLookup.TryGetDeduplicator` returns false) still gets its result, and one warning is logged.

---

## Implementation Notes

- The server-side behaviour underneath exists: validation (003), sequence (004), rollback (005), NPC session (006), events (007), exclusivity (009), commit orchestration (011).
- This story implements Story 010's attempt-start seam (`IEnhancementAttemptStarter.StartAttempt(clientId, characterId, requestId, itemSlotIndex, scrollSlotIndex)`). It calls `EnhancementCommitOrchestrator.Begin` and supplies `emitAcknowledgment` (enqueue `EnhancementRequestReceived`) and `deliverResult` (`RecordCommitted`, then enqueue `EnhancementAttemptResult`), and turns `RejectedInvalidRequest` into the rejection message. The delivery callback must not throw: per Story 011, a throw after a successful write leaves the client with no result.
- `IEnhancementRequestDedupLookup` stays injected; its production implementation goes with the server composition (with TD-068). Tests use the existing test double.
- The hold starts at CR-ENH-15 step 3 and ends when the step 6b commit returns, or at once on a step 4 failure. The hold, release and discard calls are Inventory Story 012's; this story decides when to call them.
- **Which call ends the hold (Inventory Story 012 `/code-review`, 2026-10-09).** The hold is injected as `IOwnerInventorySyncHold` (`src/ServerLogic/InventorySystem/`; `OpenHold`, `ReleaseHold`, `DiscardHold`). A step 4 failure and a successful step 6b commit call `ReleaseHold`. `DiscardHold` is only for a failed commit, where the client is disconnected (CR-NET-5.5): it drops a deferred `InventoryFullSync`, so a client that completed a zone entry during the hold and stays connected after a discard would receive `InventorySlotUpdate`s with no full sync before them.
- The result is cap-exempt and goes to the front of the queue (CR-NET-7.7), so it may arrive before the released `InventorySlotUpdate` messages. Do not assert their relative order.
- On a failed commit nothing is sent; the client is disconnected (CR-NET-5.5) and gets `InventoryFullSync` on its next zone entry, from the rolled-back bag.
- `LastEnhancementRequestID` is recorded in memory only, in the delivery callback (readiness decision 1). EC-NET-9's same-transaction durable write is TD-069 and is not part of this story.
- OQ-ENH-8 (replaying a missed result at next login) is deferred to Enhancement UI design; current rule: no replay (EC-ENH-1).

---

## Out of Scope

- Story 010: message types, codecs, preview, cancel, rejected and duplicate requests, NPC session messages
- Story 011: the commit, the rollback call, the disconnect and session preservation on a failed write
- Inventory Story 012: `InventorySlotUpdate` / `InventoryFullSync` codecs, their sending for bag changes in general, and the hold mechanism
- Story 009: holding other requests during an attempt
- Story 016: the +9 broadcast (`ServerBroadcast_Enhancement9`, AC-ENH-18 delivery half, AC-NC-47, name lookup) and the `INetworkTestObserver` enhancement callback signatures
- TD-069: persisting `LastEnhancementRequestID`
- The production `IEnhancementRequestDedupLookup` and the server composition (TD-068)
- Enhancement UI, VFX and Audio epics: result presentation

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_ResultDelivery_integration_tests.cs` (to be created).

- **Acknowledgment** — valid request → one `EnhancementRequestReceived` with the request's `requestId` and item slot, on the tick the request is dispatched.
- **Step 4 failure** — as stated in the criterion.
- **Result after commit** — commit held open → no result, no slot update; complete with `Success` → result once, held updates in order. Complete with `DatabaseError` → neither.
- **Destruction** — destruction injected, commit succeeds → `EnhancementAttemptResult { Destruction, newLevel 0 }` and a slot update with `itemId = 0`.
- **Cap exemption** — AC-NC-35 (a)–(d) with the real message types.
- **Reconnect** — the three outcomes of the reconnect criterion.
- **AC-ENH-13** — drop the connection between commit and send → no result on the new connection; bag as committed.
- **Duplicate after commit** — commit `requestId` 7, resend 7 → dropped, one anomaly; after a failed commit, resend 7 → processed.
- **RecordCommitted** — sets the value; `TryProcess` tests unchanged; no deduplicator → result still sent, one warning.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_ResultDelivery_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011 (Complete 2026-10-16); Inventory Story 012 (Complete 2026-10-09); Story 010 (Complete 2026-10-09); Stories 005 and 007 (Complete); Networking Core Stories 036 and 037 (Complete)
- Unlocks: Story 016 (the +9 broadcast hooks into this story's commit-success delivery); Enhancement UI epic (result side)
