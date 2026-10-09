# Story 012: Owner Inventory Sync Messages

> **Epic**: Inventory System
> **Status**: Not Started — written 2026-10-09 from the TD-046 amendment of `networking-wire-protocol.md` (Approved, lean verify 2026-10-09); `/story-readiness` pending
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: not estimated — set at `/story-readiness`

> **Created 2026-10-09 (user decision).** TD-046 found that no wire message carried bag changes to the owning client. The amendment added `InventorySlotUpdate` and `InventoryFullSync`. They cover every bag change and every zone entry, not only enhancement, so they are an Inventory story; Enhancement Story 015 uses the hold this story provides.

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` — `InventorySlotUpdate`, `InventoryFullSync` and their Enhancement hold notes, CR-NET-7.3 (nullable ID fields), AC-NC-40 (`InventoryFullSync` size), AC-NC-41 (slot-update half), AC-NC-42 (`count` half), AC-NC-44. `design/gdd/inventory-system.md` — `InventoryChangedEvent` (the entries this story sends), `INVENTORY_SLOT_COUNT`, the Inventory UI interaction row. `design/gdd/enhancement-system.md` — CR-ENH-11 (the rule the hold exists for).
**Requirement**: none registered — the requirement is the wire protocol's TD-046 amendment. `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**:
- **ADR-012: Server/Client Assembly Boundary (Accepted 2026-10-08)** — message types and codecs a client decodes are shared; the sender is server-only.
- **ADR-010** — `InventoryChangedEvent` is a reused-buffer `readonly struct`; a subscriber copies what it needs before it returns.
- **ADR-004: Networking Library (NGO)** — transport and envelope. No NGO call is written in this story.

**ADR Decision Summary**: the server owns the bag; the owning client's view is replaced by absolute state sent on the R-OD channel, never by deltas.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — codecs over spans and an event subscriber; no engine API.
**Engine Notes**: Follow `SetTarget` / `SetTargetCodec` for the message types and codecs, and `WireIdCodec` for `ItemID`.

**Control Manifest Rules**:
- Required: cross-system communication is a direct call on an injected interface or a C# `event Action<T>` with a `readonly struct` arg — ADR-010
- Forbidden: class-typed event args — ADR-010

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md`, scoped to this story:*

- [ ] **`InventorySlotUpdate` codec (AC-NC-41, slot-update half)**: messages with 1, 2 and 20 entries encode to bodies of 11, 21 and 201 bytes and every entry round-trips, including an emptied slot (`itemId = 0`, `quantity = 0`, `enhancementLevel = 0`). `itemId = 0` is written without the CR-NET-7.3 assertion (nullable field).
- [ ] **`InventoryFullSync` codec (AC-NC-40, AC-NC-42)**: the body is exactly 201 bytes, 20 entries in ascending `slotIndex` order, every field round-trips; a received message whose `count` is 19 is discarded and one anomaly is logged.
- [ ] **One update per bag change**: each `InventoryChangedEvent` for a character produces exactly one `InventorySlotUpdate` to that character's owning client, with the same entries — the absolute post-change state of each changed slot; no slot index appears twice in one message; no other client receives it.
- [ ] **Full sync on zone entry (AC-NC-44)**: when `SessionReady` has been sent for a zone entry, exactly one `InventoryFullSync` with 20 entries follows it on the R-OD path before any `InventorySlotUpdate` of that session, and its entries equal the server's bag for that character.
- [ ] **Hold**: while a hold is open for a character, zero `InventorySlotUpdate` and zero `InventoryFullSync` are sent for that character; other characters are unaffected.
- [ ] **Release**: releasing the hold sends the held `InventorySlotUpdate` messages in the order they were raised. If a zone entry completed during the hold, exactly one `InventoryFullSync` built from the bag at release is sent instead, and the held updates are discarded.
- [ ] **Discard**: discarding the hold sends nothing — neither the held updates, nor updates raised until the discard returns, nor a deferred `InventoryFullSync` on that connection.
- [ ] **No client connected**: a bag change for a character with no session-ready connection sends nothing and throws nothing.

---

## Implementation Notes

- **Message types and codecs** go in `src/Foundation/Networking/WireProtocol/` (shared — a client decodes them). `MessageTypeID` values are not assigned by any ADR; use the provisional convention of the existing message types and say so in the doc comments.
- **Sender** is server-only (`src/ServerLogic/`). It subscribes to `InventoryService`'s `InventoryChangedEvent` and copies the entries inside the handler (the event's buffer is reused — ADR-010).
- **The hold is a mechanism, not a rule.** This story exposes open / release / discard per character on the sender. When they are called is Enhancement Story 015's (CR-ENH-11: from CR-ENH-15 step 3 until the step 6b commit returns). This story's tests call them directly.
- **Zone entry.** The trigger is "`SessionReady` sent". Confirm at `/story-readiness` which existing type raises that (the zone session state machine or the connection state machine) and whether a hook exists; if none does, the story adds one or takes it as a call from its fixture.
- **Outbound path.** No transport adapter exists (the adapter story of ADR-014 is not written). Enqueue on the R-OD path the way existing server→client messages are, and assert through `INetworkTestObserver` captures. Confirm the exact seam at `/story-readiness`.
- **Redundant echoes are intended.** `MoveResult`, `DiscardResult`, `SellResult` and `UseItemResult` already carry absolute slot state on the same ordered channel; the update is sent as well and applying both is harmless. Those result messages have no code yet and are not part of this story.
- **`INVENTORY_SLOT_COUNT`** fixes the size of `InventoryFullSync`; read it from the inventory constants, do not write 20 in the codec. Above 51 slots the body exceeds 512 bytes (CR-NET-7.6).
- The Inventory `EPIC.md` lists wire-protocol codecs for inventory messages as out of scope "(Networking)". This story is the exception recorded by the user decision of 2026-10-09; the bag request messages (`MoveRequest`, `DiscardRequest`, …) stay out of scope.

---

## Out of Scope

- Enhancement Story 015: when the hold is opened, released and discarded; AC-NC-43 end to end
- Enhancement Story 010: the Enhancement and NPC interaction messages
- The bag request and result messages (`MoveRequest` / `MoveResult`, `DiscardRequest` / `DiscardResult`, `SellRequest`, `UseItemRequest`) and their handlers
- Equipment sync to the owning client on zone entry (wire protocol OQ-NC-SER-5)
- The client-side bag model and the Inventory UI
- The transport adapter (ADR-014)

---

## QA Test Cases

**Files** (to be created): `tests/EditMode/Networking/WireProtocol_InventorySync_tests.cs` (codecs); `tests/EditMode/Integration/InventorySystem/Inventory_OwnerSync_integration_tests.cs` (sender, over a real `InventoryService`).

- **Slot update sizes** — 1, 2 and 20 entries → 11, 21, 201 bytes; round trip; emptied slot.
- **Full sync** — a bag with items, stacks and empty slots → 201 bytes, ascending order, round trip; `count` 19 → discarded, one anomaly.
- **Pickup** — one pickup into an empty slot → one update with one entry carrying the item, quantity and level 0.
- **Move (swap)** — one `InventoryChangedEvent` with two entries → one update with two entries.
- **Enhancement level** — `SetEnhancementLevel` on slot 0 → one update with `enhancementLevel` set.
- **Owner only** — two characters, one changes its bag → the other client's capture is empty.
- **Zone entry** — `SessionReady` then a pickup → capture order: `SessionReady`, one `InventoryFullSync`, one `InventorySlotUpdate`.
- **Hold and release** — hold opened, two bag changes → nothing sent; release → two updates in order.
- **Hold, zone entry, release** — hold opened, bag change, zone entry completes → `SessionReady` only; release → one `InventoryFullSync` equal to the bag, no slot update.
- **Hold and discard** — hold opened, bag change, discard → nothing sent; a bag change after the discard returned is sent.
- **No connection** — bag change for a character with no session-ready client → nothing sent, no exception.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/InventorySystem/Inventory_OwnerSync_integration_tests.cs` and `tests/EditMode/Networking/WireProtocol_InventorySync_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`InventoryChangedEvent`), Story 010 (per-slot enhancement level), Story 011 (the service is in `IronGrind.ServerLogic`) — all Complete. The TD-046 design gate is closed (wire protocol Approved 2026-10-09).
- Unlocks: Enhancement Story 015 (the CR-ENH-11 hold); the Inventory UI epic (the client's authoritative bag)
