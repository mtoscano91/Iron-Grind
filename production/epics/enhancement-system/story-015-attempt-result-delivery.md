# Story 015: Attempt Result Delivery

> **Epic**: Enhancement System
> **Status**: Blocked — **(1) Story 011 (Commit Orchestration), itself Blocked on the Character Persistence implementation; (2) Inventory Story 012 (owner inventory sync), not started; (3) Story 010 (message types, codecs and the attempt-start seam), not started.**
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: not estimated — set at `/story-readiness` once the blockers clear

> **Created 2026-10-09 by splitting Story 010 (user decision).** Story 010 keeps the codecs and the requests that need no commit. This story is everything a client receives because an attempt was accepted and committed.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-11 (pre-commit inventory events must not reach the client before the commit), CR-ENH-14, CR-ENH-15 steps 3, 4, 6b, 8 and 9, UI-ENH-2 (`EnhancementAttemptResult`), UI-ENH-3 (`ServerBroadcast_Enhancement9`), EC-ENH-1, EC-ENH-8, AC-ENH-13 (delivery half), AC-ENH-18 (delivery half). `design/gdd/networking-wire-protocol.md` — `EnhancementRequestReceived`, `EnhancementAttemptResult`, `ServerBroadcast_Enhancement9`, the Enhancement hold notes of `InventorySlotUpdate` and `InventoryFullSync`, CR-NET-7.7, AC-NC-35, AC-NC-43, AC-NC-47. `design/gdd/networking-core.md` — CR-NET-5.1 to CR-NET-5.5. `design/gdd/networking-session.md` — EC-NET-9.
**Requirement**: `TR-enh-009` (delivery half), `TR-enh-010` (delivery)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**:
- **ADR-011: Asynchronous Persistence in the Server Tick Loop (Accepted 2026-10-07)** — the outcome is delivered from the completion callback on the tick, after the write; no `await` in game logic.
- **ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09)** — `Drain` runs before `DispatchTick`; a write completed before tick N has its outcome delivered before any new request of tick N.
- **ADR-004: Networking Library (NGO)** — transport and envelope.

**ADR Decision Summary**: an irreversible outcome reaches a client only after its persistence write succeeded; the result is handled on the tick thread by the completion queue.

**Engine**: Unity 6.3 LTS | **Risk**: to be set at `/story-readiness` (depends on whether a transport adapter exists by then; ADR-004's NGO risk is HIGH)
**Engine Notes**: To be filled in at `/story-readiness`.

**Control Manifest Rules**:
- To be listed at `/story-readiness` from the manifest version current then. Known to apply: never keep `body` after a handler returns, `await` in a handler, or read `ICharacterMutationGate` from a handler — ADR-014, ADR-011; never use class-typed event args — ADR-010

---

## What is blocking

1. **Story 011** provides the orchestration this story delivers from: `BeginAttempt` on the tick, the commit, then `CompleteAttempt` or `RollBackAttempt` in the completion callback. Story 011 is Blocked on the Character Persistence implementation; it can be tested against a fake of the persistence interface, and so can this story once Story 011's code exists.
2. **Inventory Story 012** provides `InventorySlotUpdate`, `InventoryFullSync` and the per-character hold this story drives.
3. **Story 010** provides the message types, the codecs and the attempt-start seam this story implements.

---

## Acceptance Criteria

*To be re-checked at `/story-readiness` once the blockers clear.*

- [ ] **Acknowledgment after step 4**: an accepted `EnhancementAttemptRequest` (validation passed, scroll consumed — CR-ENH-15 steps 2–4) → exactly one `EnhancementRequestReceived { requestId, itemSlotIndex }` to the owning client, before the outcome is computed. A `Rejected*` result never follows an acknowledgment for the same `requestId`.
- [ ] **Step 4 failure (AC-NC-43, step 4 case)**: with `Inventory.ConsumeItem` forced to fail at step 4, the capture contains zero `EnhancementRequestReceived` and exactly one `EnhancementAttemptResult` with `RejectedScrollNotFound`; an `InventorySlotUpdate` raised later in the same tick is sent, not held.
- [ ] **Result only after the commit (CR-ENH-11, AC-NC-43)**: with the commit held open, the owning client's capture contains zero `InventorySlotUpdate` and zero `EnhancementAttemptResult`. When the commit returns `Success`, it contains the held `InventorySlotUpdate` messages in the order they were raised and exactly one `EnhancementAttemptResult` (`Success` with the new level, or `Destruction` with `newLevel` 0); their order relative to each other is not asserted. When the commit returns a non-Success code, it contains neither message type, including after the rollback.
- [ ] **Cap exemption (AC-NC-35)**: with the owning client's Path 1 queue at its cap of 8, an `EnhancementAttemptResult` enqueued before the flush is at position 1 of that tick's capture and the displaced message is at position 1 of the next tick's; the same holds for `EnhancementRequestReceived`.
- [ ] **Reconnect during the commit (AC-NC-43, reconnect case; EC-ENH-1)**: the owning client disconnected after step 3 and completes a new zone entry while the commit is held open → its capture contains `SessionReady` and zero `InventoryFullSync`; when the commit returns `Success`, exactly one `InventoryFullSync` follows, its entries equal the committed bag, no `InventorySlotUpdate` raised before the commit appears after it, and the new connection's capture contains zero `EnhancementAttemptResult`; when the commit returns a non-Success code, no `InventoryFullSync` appears on that connection.
- [ ] **AC-ENH-13 (disconnect after commit)**: the connection drops after the commit and before the result is sent → on reconnect the bag shows the committed level, the scroll is gone, the slot is unlocked, and no `EnhancementAttemptResult` is sent on the new connection.
- [ ] **AC-ENH-18 (broadcast delivery)**: on a committed +8 → +9 success every connected client, in every zone, receives exactly one `ServerBroadcast_Enhancement9 { playerName: "TestPlayer", itemName: "Dark Steel Sword" }`, the owner's new connection included if it reconnected during the commit; none is sent for +7 → +8 or +9 → +10; a delivery failure to some clients does not affect the outcome (EC-ENH-8).
- [ ] **Broadcast is not cap-exempt (AC-NC-47)**: client A's Path 1 queue holds exactly 8 non-exempt messages and a +8 → +9 commits before the flush → that tick's capture holds the 8 messages and no broadcast; the broadcast is in the next tick's capture.
- [ ] **`LastEnhancementRequestID` (EC-NET-9)**: after a committed attempt with `requestId = X`, the character's `LastEnhancementRequestID` is `X` and a second `EnhancementAttemptRequest` with `requestId = X` is dropped as Story 010's duplicate criterion states; after a failed commit it is unchanged.
- [ ] **Observer callbacks**: the `INetworkTestObserver` enhancement callbacks carry the amended fields (no `itemId`) and the existing harness tests pass with them.

---

## Implementation Notes

- **Not implementable until the three blockers clear.** The server-side behaviour underneath exists: validation (003), sequence (004), rollback (005), NPC session (006), events (007), exclusivity (009).
- This story implements Story 010's attempt-start seam: it calls `IrreversibleOutcomeCoordinator.Begin` with Story 011's delegates, and adds the client-facing sends around them.
- The hold starts at CR-ENH-15 step 3 and ends when the step 6b commit returns, or at once on a step 4 failure. The hold, release and discard calls are Inventory Story 012's; this story decides when to call them.
- The result is cap-exempt and goes to the front of the queue (CR-NET-7.7), so it may arrive before the released `InventorySlotUpdate` messages. Do not assert their relative order.
- On a failed commit nothing is sent; the client is disconnected (CR-NET-5.5) and gets `InventoryFullSync` on its next zone entry, from the rolled-back bag.
- Player name and item display name for the broadcast: the +9 trigger of Story 007 carries ids; resolve the names here (see Story 007's note on name lookup). Strings longer than 24 UTF-8 bytes are cut by the codec (Story 010).
- Where `LastEnhancementRequestID` is written with the outcome is shared with Story 011 (one transaction, EC-NET-9). Settle the boundary at `/story-readiness`.
- OQ-ENH-8 (replaying a missed result at next login) is deferred to Enhancement UI design; current rule: no replay (EC-ENH-1).

---

## Out of Scope

- Story 010: message types, codecs, preview, cancel, rejected and duplicate requests, NPC session messages
- Story 011: the commit, the rollback call, the disconnect and session preservation on a failed write
- Inventory Story 012: `InventorySlotUpdate` / `InventoryFullSync` codecs, their sending for bag changes in general, and the hold mechanism
- Story 009: holding other requests during an attempt
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
- **AC-ENH-18** — three connected clients in two zones → each receives one broadcast with the two names; +7 → +8 → none; one client's send fails → the outcome stands and the others receive it.
- **AC-NC-47** — as stated in the criterion.
- **Duplicate after commit** — commit `requestId` 7, resend 7 → dropped, one anomaly; after a failed commit, resend 7 → processed.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_ResultDelivery_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 011 (blocking)**; **Inventory Story 012 (blocking)**; **Story 010 (blocking)**; Stories 005 and 007 (Complete); Networking Core Stories 036 and 037 (Complete)
- Unlocks: Enhancement UI epic (result side)
