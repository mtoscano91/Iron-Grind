# Story 010: Client Requests and Result Delivery

> **Epic**: Enhancement System
> **Status**: Blocked — **TD-046: the wire-protocol amendment was written 2026-10-09 but is not re-reviewed. Do not start until the lean re-review of `networking-wire-protocol.md` passes and ADR-014 lists the two new inbound request types.**
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: not estimated — depends on the amended message set

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-6 (two-tap irrevocability; `CancelEnhancement`), CR-ENH-11 (pre-commit inventory events must not reach the client before the commit), CR-ENH-14, CR-ENH-15 steps 1, 8 and 9, UI-ENH-1 (`EnhancementStateUpdate`), UI-ENH-2 (`EnhancementAttemptResult`), UI-ENH-3 (`ServerBroadcast_Enhancement9`), UI-ENH-4 (requests), EC-ENH-1, EC-ENH-5, EC-ENH-8, AC-ENH-6, AC-ENH-13 (delivery half), AC-ENH-18 (delivery half). `design/gdd/networking-wire-protocol.md` — `EnhancementAttemptRequest` (to be replaced), NPC interaction messages (already defined).
**Requirement**: `TR-enh-009`, `TR-enh-010` (delivery)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-004: Networking Library (NGO) — transport, message envelope, reliability classes. ADR-001 Amendment A1 applies if the amended request carries a `requestId` (dedup key `(charId, messageType, requestId)`).
**ADR Decision Summary**: Client requests and server results travel as typed messages over NGO with the Networking Core envelope; irreversible outcomes are broadcast only after they are committed.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-004 — NGO API changed across Unity 6.0–6.3, beyond the model's training data; check `docs/engine-reference/unity/` before using any NGO API)
**Engine Notes**: Follow the codec and handler patterns of the existing Networking Core stories; do not write NGO calls from memory.

**Control Manifest Rules (Feature layer)**:
- Required: R-OD messages carrying a `requestId` use `(charId, messageType, requestId)` as the dedup key — ADR-001 Amendment A1
- Forbidden: no `EventBus` class; no class-typed event args — ADR-010

---

## What is blocking (TD-046)

1. `networking-wire-protocol.md` defines `EnhancementAttemptRequest { entityId, itemId, requestId }` — item-id based, with no scroll — and its outcome body is still "TBD — pending Enhancement System GDD". The GDD uses `ConfirmEnhancement { itemSlotIndex: byte, scrollSlotIndex: byte }`. An `ItemID` cannot say which of two same-type items at different levels is meant.
2. No wire message carries inventory slot changes to the owning client. After an enhancement success the client has no authoritative slot update. The GDD requires that any such message derived from an attempt's pre-commit inventory events is held until the commit succeeds and dropped if it fails (CR-ENH-11).
3. The final encoding of `EnhancementAttemptResult` (how `outcome` / `newLevel` are sent on a rejection) is deferred to the same fix (UI-ENH-2).

**Update 2026-10-09 — amendment written, re-review pending.** `networking-wire-protocol.md` now defines: `EnhancementAttemptRequest { requestId, itemSlotIndex, scrollSlotIndex }` (the GDD's `ConfirmEnhancement` under its wire name), `EnhancementRequestReceived { requestId, itemSlotIndex }`, `EnhancementAttemptResult { requestId, resultCode, newLevel }` (`outcome` is not sent — derived from `resultCode`), `ServerBroadcast_Enhancement9`, `CancelEnhancement`, `EnhancementPreviewRequest` → `EnhancementStateUpdate` (probabilities as `ushort` × 10,000) or `EnhancementPreviewRejected`, and `InventorySlotUpdate` / `InventoryFullSync` with the CR-ENH-11 hold rule. The three points above are answered there; the criteria and test cases below still use the pre-amendment wording and must be re-read against the amended schemas.

Still to do before this story is Ready: (1) lean re-review of `networking-wire-protocol.md` in a fresh session; (2) ADR-014 Decision 1 rows for `EnhancementPreviewRequest` and `CancelEnhancement` (held or not); (3) re-run `/story-readiness` on this file. Code this story must also bring in line: comments naming `EnhancementOutcomeBroadcast` or `(entityId, itemId)` in `INetworkTestObserver.cs`, `CommitBeforeBroadcastSequencer.cs`, `PriorityPathQueue.cs`, `QueuedMessage.cs` and two test files, the `INetworkTestObserver` enhancement callback signatures (still item-id based), and the "provisional values" remark in `EnhancementResultCode.cs`.

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. Message names and fields are the GDD's; the wire schemas are whatever the TD-046 amendment defines.*

- [ ] **Confirm request**: a `ConfirmEnhancement` request reaches `EnhancementService.ConfirmEnhancement` with the sender's character and the two slot indices, and the result code comes back to that client in `EnhancementAttemptResult { outcome, newLevel, resultCode }`. On a `Rejected*` code, `resultCode` is authoritative.
- [ ] **AC-ENH-6 (cancel before confirm)**: `CancelEnhancement` with no confirm sent changes nothing — no slot was ever locked, the scroll is in the bag, the level is unchanged, no error is returned.
- [ ] **State update (UI-ENH-1)**: selecting a valid item and scroll yields `EnhancementStateUpdate { itemSlotIndex, currentLevel, P_s, P_d }` with the table values for that level (e.g. a +4 item → 0.65 / 0.35; a +2 item → 0.85 / 0.15).
- [ ] **Result only after the commit (CR-ENH-11)**: `EnhancementAttemptResult` is sent only after the commit succeeds; on a failed commit no result is sent.
- [ ] **AC-ENH-13 (disconnect after commit)**: the connection drops after the commit and before the result is sent → on reconnect the bag shows level 3, the scroll is gone, the slot is unlocked, and the result message is not re-sent.
- [ ] **AC-ENH-18 (broadcast delivery)**: on a +8 → +9 success every connected client receives `ServerBroadcast_Enhancement9 { playerName: "TestPlayer", itemName: "Dark Steel Sword" }`; none is sent for other transitions; a delivery failure to some clients does not affect the outcome (EC-ENH-8).
- [ ] **Pre-commit inventory changes are withheld**: no client-facing inventory message derived from the attempt's `ConsumeItem` / `SetEnhancementLevel` / `RemoveItem` reaches the owning client before the commit succeeds, and none is sent if it fails.
- [ ] **NPC session messages**: `OpenNPCInteraction` / `CloseNPCInteraction` call Story 006's tracker and answer with `NPCInteractionOpened` / `RejectedNotInTownHub`.

---

## Implementation Notes

- **Not implementable until TD-046 is fixed.** Server-side behaviour this story will sit on top of already exists by then: validation (003), sequence (004), commit and rollback (005), NPC session (006), events (007).
- The fourth criterion is the wire half of Story 005's "no delivery before the commit" and the last criterion can be split off if the NPC session handlers are wanted before the Enhancement messages are fixed — those four messages are already defined in the wire protocol's NPC Shop section and are not part of TD-046.
- `EnhancementStateUpdate`: its trigger is `EnhancementPreviewRequest { itemSlotIndex, scrollSlotIndex }` (user decision 2026-10-09; enhancement-system.md UI-ENH-1 and UI-ENH-4, networking-wire-protocol.md). An invalid selection is answered with `EnhancementPreviewRejected`.
- Player name for the broadcast: see Story 007's note on name lookup.
- OQ-ENH-8 (replaying a missed result at next login) is deferred to Enhancement UI design; current rule: no replay (EC-ENH-1).

---

## Out of Scope

- Stories 003–007: all server-side behaviour behind the messages
- Story 009: holding other requests during an attempt
- Enhancement UI epic: screens, probability display, the heightened destruction warning and its acknowledgment (AC-ENH-25, 26, 31; UI-ENH-5 to UI-ENH-9)
- VFX and Audio epics: result presentation

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_ClientMessages_integration_tests.cs` (to be created; codec round-trip tests follow the Networking Core convention once the schemas exist).

- **Confirm round trip** — request with slots (0, 1) → service called with (sender, 0, 1) → result message carries the service's `{outcome, newLevel, resultCode}`.
- **Rejection** — tier mismatch → result with `RejectedTierMismatch`; client-ignored fields per the amended encoding.
- **AC-ENH-6** — cancel with no confirm → no service mutation, no lock, no error.
- **State update** — +4 Bronze item → `P_s` 0.65, `P_d` 0.35; +2 → 0.85 / 0.15.
- **Result after commit** — commit held open → no result sent; complete with `Success` → result sent once. Complete with `DatabaseError` → no result.
- **AC-ENH-13** — drop the connection between commit and send → no send attempted on reconnect; bag state as committed.
- **AC-ENH-18** — three connected clients → all three receive the broadcast with the two names; +7 → +8 → none.
- **Withheld inventory messages** — commit held open → zero inventory slot-update messages to the owner; after `Success` → the updates are sent; after a failure → none.
- **Codec** — encode/decode round trip for each amended message (cases to be written from the amended schemas).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_ClientMessages_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **TD-046 wire-protocol amendment (blocking)**; Story 011 (commit orchestration — the result this story delivers comes from it); Stories 005, 006 and 007
- Unlocks: Enhancement UI epic
