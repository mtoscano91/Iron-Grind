# Story 012: Owner Inventory Sync Messages

> **Epic**: Inventory System
> **Status**: Complete (2026-10-09; `/dev-story`, `/code-review` and `/story-done` the same day; EditMode 2383 / 2383) — was Ready — written 2026-10-09 from the TD-046 amendment of `networking-wire-protocol.md` (Approved, lean verify 2026-10-09); `/story-readiness` 2026-10-09 (NEEDS WORK, closed the same day by the user decisions recorded below). Implement after Enhancement Story 010.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours

> **Created 2026-10-09 (user decision).** TD-046 found that no wire message carried bag changes to the owning client. The amendment added `InventorySlotUpdate` and `InventoryFullSync`. They cover every bag change and every zone entry, not only enhancement, so they are an Inventory story; Enhancement Story 015 uses the hold this story provides.

> **User decisions 2026-10-09 (`/story-readiness`).** (1) The outbound seam is `IClientMessageOutbox`, created by Enhancement Story 010; this story depends on it. (2) Zone entry and disconnect reach the sender as direct calls; the sender keeps its own character → client map. (3) `INVENTORY_SLOT_COUNT` moves to `IronGrind.Foundation`. (4) Estimate 6 hours.

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
- Required: wire message schemas and the code that reads and writes them are `Foundation` — ADR-012
- Required: a type in `IronGrind.Foundation` carries no server-only type in its signature (`IronGrind.Foundation` references no project assembly) — ADR-012
- Required: a move is a folder and assembly change only — the namespace does not change, the `.cs` and its `.meta` move together — ADR-012
- Forbidden: class-typed event args — ADR-010

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md`, scoped to this story. "Sent" and "enqueued" mean one `IClientMessageOutbox.Enqueue` call for the owning client, with `isCapExempt` false.*

- [x] **`InventorySlotUpdate` codec (AC-NC-41, slot-update half)**: messages with 1, 2 and 20 entries encode to bodies of 11, 21 and 201 bytes and every entry round-trips, including an emptied slot (`itemId = 0`, `quantity = 0`, `enhancementLevel = 0`). `itemId = 0` is written without the CR-NET-7.3 assertion (nullable field).
- [x] **`InventoryFullSync` codec (AC-NC-40, AC-NC-42)**: the body is exactly 201 bytes, 20 entries in ascending `slotIndex` order, every field round-trips; a received message whose `count` is 19 is discarded and one anomaly is logged.
- [x] **One update per bag change**: each `InventoryChangedEvent` for a character produces exactly one `InventorySlotUpdate` to that character's owning client, with the same entries — the absolute post-change state of each changed slot; no slot index appears twice in one message; no other client receives it.
- [x] **Full sync on zone entry (AC-NC-44)**: after `OnSessionReadySent(clientId, characterId)`, exactly one `InventoryFullSync` with 20 entries is enqueued for that client before any `InventorySlotUpdate` of that session, and its entries equal the server's bag for that character.
- [x] **Hold**: while a hold is open for a character, zero `InventorySlotUpdate` and zero `InventoryFullSync` are sent for that character; other characters are unaffected.
- [x] **Release**: releasing the hold sends the held `InventorySlotUpdate` messages in the order they were raised. If a zone entry completed during the hold, exactly one `InventoryFullSync` built from the bag at release is sent instead, and the held updates are discarded.
- [x] **Hold overflow**: when more than `MAX_HELD_UPDATES_PER_CHARACTER` updates are raised during one hold, releasing it sends exactly one `InventoryFullSync` built from the bag at release and no `InventorySlotUpdate`; one server warning is logged for that hold.
- [x] **Discard**: discarding the hold sends nothing — neither the held updates, nor updates raised until the discard returns, nor a deferred `InventoryFullSync` on that connection.
- [x] **No client connected**: a bag change for a character with no `OnSessionReadySent` since its last disconnect sends nothing and throws nothing.
- [x] **Disconnect**: after `OnClientDisconnected(clientId)`, nothing is enqueued for that client; the updates an open hold had collected for its character are dropped. The hold itself stays open until it is released or discarded, and a release with no session-ready client sends nothing.
- [x] **Assembly boundary**: `InventoryConstants.INVENTORY_SLOT_COUNT` is in `IronGrind.Foundation`; the codecs read it and the literal 20 is not written in them; the boundary test in `tests/EditMode/Architecture/` passes; the EditMode suite passes with no other test edited than those that name the moved or renamed constants.

---

## Implementation Notes

- **Message types and codecs** go in `src/Foundation/Networking/WireProtocol/` (shared — a client decodes them). `MessageTypeID` values are not assigned by any ADR; use the provisional convention of the existing message types, say so in the doc comments, and do not reuse a value taken by Enhancement Story 010.
- **Wire entry type.** `SlotChange` is server-only, so the codecs use a new `Foundation` struct, `InventorySlotEntry` (slot index, `ItemID`, quantity, enhancement level — 10 bytes on the wire). The sender copies each `SlotChange` into one.
- **Slot count (user decision 2026-10-09).** `InventoryConstants` is in `IronGrind.ServerLogic` today and also holds `BAG_FULL_DEDUP_WINDOW_TICKS`, which is computed from `ServerTickLoop.TICK_RATE_HZ` (server-only), so the class cannot move whole. `InventoryConstants` with `INVENTORY_SLOT_COUNT` moves to `src/Foundation/InventorySystem/` (namespace unchanged, `.meta` moves with it). `BAG_FULL_DEDUP_WINDOW_TICKS` goes to a new server-only `InventoryServerConstants` in `src/ServerLogic/InventorySystem/`; its users are `IInventoryService.cs`, `InventoryService.cs` and `tests/EditMode/InventorySystem/InventorySystem_BagFullNotification_tests.cs`. `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` gains the entry for the moved type. `INVENTORY_SLOT_COUNT` fixes the size of `InventoryFullSync`; above 51 slots the body exceeds 512 bytes (CR-NET-7.6).
- **Sender** is server-only (`src/ServerLogic/`). It subscribes to `InventoryService`'s `OnInventoryChanged` and copies the entries inside the handler (the event's buffer is reused — ADR-010).
- **Outbound path (user decision 2026-10-09).** The sender takes an `IClientMessageOutbox` (`src/ServerLogic/Networking/Outbound/`, created by Enhancement Story 010) and calls `Enqueue(clientId, messageTypeId, body, isCapExempt: false)` for both messages. Tests use Story 010's recording double and decode the captured bodies with the codecs. `INetworkTestObserver` is not used: it has no callback that carries a body.
- **Zone entry and disconnect (user decision 2026-10-09).** Nothing in `src/` sends `SessionReady` today: there is no message type, codec or hook, only the `CrossCuttingRpcGuardChain.MarkSessionReady` flag. The sender exposes `OnSessionReadySent(uint clientId, CharacterID characterId)` and `OnClientDisconnected(uint clientId)` and keeps its own character → client map (`IConnectionCharacterDirectory` resolves client → character only). This story's tests call them directly; the story that sends `SessionReady` calls them in production.
- **The hold is a mechanism, not a rule.** This story exposes open / release / discard per character on the sender. When they are called is Enhancement Story 015's (CR-ENH-11: from CR-ENH-15 step 3 until the step 6b commit returns). This story's tests call them directly.
- **Hold bound.** `MAX_HELD_UPDATES_PER_CHARACTER` is a named constant on the sender, value 8 (an enhancement attempt raises at most a few `InventoryChangedEvent`s). The wire protocol states no bound; replacing an overflowing hold by one `InventoryFullSync` keeps the client's bag correct, since both messages carry absolute state.
- **Redundant echoes are intended.** `MoveResult`, `DiscardResult`, `SellResult` and `UseItemResult` already carry absolute slot state on the same ordered channel; the update is sent as well and applying both is harmless. Those result messages have no code yet and are not part of this story.
- The Inventory `EPIC.md` lists wire-protocol codecs for inventory messages as out of scope "(Networking)". This story is the exception recorded by the user decision of 2026-10-09; the bag request messages (`MoveRequest`, `DiscardRequest`, …) stay out of scope.

**Performance**: the sender runs on every bag change. Outside a hold it allocates nothing per `InventoryChangedEvent` (encode buffers and the per-character hold storage are allocated when the character's session becomes ready or at construction). Held updates are bounded by `MAX_HELD_UPDATES_PER_CHARACTER`.

---

## Out of Scope

- Enhancement Story 015: when the hold is opened, released and discarded; AC-NC-43 end to end
- Enhancement Story 010: the Enhancement and NPC interaction messages; `IClientMessageOutbox` and its recording double
- The `SessionReady` message, its codec and the code that sends it
- The bag request and result messages (`MoveRequest` / `MoveResult`, `DiscardRequest` / `DiscardResult`, `SellRequest`, `UseItemRequest`) and their handlers
- Equipment sync to the owning client on zone entry (wire protocol OQ-NC-SER-5)
- The client-side bag model and the Inventory UI
- The transport adapter (ADR-014) and the production implementation of `IClientMessageOutbox`

---

## QA Test Cases

**Files** (to be created): `tests/EditMode/Networking/WireProtocol_InventorySync_tests.cs` (codecs); `tests/EditMode/Integration/InventorySystem/Inventory_OwnerSync_integration_tests.cs` (sender, over a real `InventoryService`; the folder is new).

- **Slot update sizes** — 1, 2 and 20 entries → 11, 21, 201 bytes; round trip; emptied slot.
- **Full sync** — a bag with items, stacks and empty slots → 201 bytes, ascending order, round trip; `count` 19 → discarded, one anomaly.
- **Pickup** — one pickup into an empty slot → one update with one entry carrying the item, quantity and level 0.
- **Move (swap)** — one `InventoryChangedEvent` with two entries → one update with two entries.
- **Enhancement level** — `SetEnhancementLevel` on slot 0 → one update with `enhancementLevel` set.
- **Owner only** — two characters, one changes its bag → the other client's capture is empty.
- **Zone entry** — `OnSessionReadySent` then a pickup → capture order: one `InventoryFullSync`, one `InventorySlotUpdate`.
- **Hold and release** — hold opened, two bag changes → nothing sent; release → two updates in order.
- **Hold, zone entry, release** — hold opened, bag change, `OnSessionReadySent` → nothing sent; release → one `InventoryFullSync` equal to the bag, no slot update.
- **Hold overflow** — hold opened, `MAX_HELD_UPDATES_PER_CHARACTER` + 1 bag changes, release → one `InventoryFullSync` equal to the bag, no slot update, one warning.
- **Hold and discard** — hold opened, bag change, discard → nothing sent; a bag change after the discard returned is sent.
- **No connection** — bag change for a character with no session-ready client → nothing sent, no exception.
- **Disconnect** — `OnClientDisconnected`, then a bag change → nothing sent; with a hold open: disconnect, release → nothing sent.
- **Constant move** — the boundary test passes with `InventoryConstants` on the shared list; the codec source holds no literal 20.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/InventorySystem/Inventory_OwnerSync_integration_tests.cs` and `tests/EditMode/Networking/WireProtocol_InventorySync_tests.cs` — must exist and pass.

**Status**: [x] Created and passing 2026-10-09 — `WireProtocol_InventorySync_tests.cs` 16 / 16 cases, `Inventory_OwnerSync_integration_tests.cs` 38 / 38 cases (EditMode run of 2026-10-09 21:24 local, 2383 / 2383)

---

## Dependencies

- Depends on: **Enhancement Story 010** (`IClientMessageOutbox` and its recording test double) — Complete 2026-10-09. Story 001 (`InventoryChangedEvent`), Story 010 (per-slot enhancement level), Story 011 (the service is in `IronGrind.ServerLogic`) — all Complete. The TD-046 design gate is closed (wire protocol Approved 2026-10-09).
- Unlocks: Enhancement Story 015 (the CR-ENH-11 hold); the Inventory UI epic (the client's authoritative bag)

---

## Completion Notes

**Completed**: 2026-10-09
**Criteria**: 11 / 11 passing (EditMode 2383 / 2383; no `error CS`)
**Test Evidence**: `tests/EditMode/Networking/WireProtocol_InventorySync_tests.cs` (16 cases) and `tests/EditMode/Integration/InventorySystem/Inventory_OwnerSync_integration_tests.cs` (38 cases)
**Code Review**: Complete — `/code-review` 2026-10-09 (unity-specialist and qa-tester), APPROVED WITH SUGGESTIONS, 0 blocking; the fixes were applied the same day. `/story-done` ran in lean mode (QL-TEST-COVERAGE and LP-CODE-REVIEW skipped).

**What exists**: `InventorySlotEntry`, `InventorySlotUpdate` (`MessageTypeId` `0xE120`), `InventoryFullSync` (`0xE121`) and their codecs in `src/Foundation/Networking/WireProtocol/`; `InventoryConstants` in `src/Foundation/InventorySystem/`; `InventoryServerConstants`, `IOwnerInventorySyncHold` and `OwnerInventorySyncSender` in `src/ServerLogic/InventorySystem/`.

**Deviations (advisory)**:
- Two additions beyond the story's text: two rows in `MessageRoutingRegistry.cs` (the routing table test requires one row per `MessageTypeId` constant) and the interface `IOwnerInventorySyncHold` (`OpenHold`, `ReleaseHold`, `DiscardHold`), which Enhancement Story 015 injects.
- "The literal 20 is not written in them" was checked by a word-boundary grep of the two codecs and the two message types on 2026-10-09; no automated test scans the sources.
- The performance note (no allocation per event outside a hold) is met by construction — buffers allocated at construction, struct keys, no interpolation outside warning paths — and is not asserted by a test.
- A character's state is freed when it has neither a client nor an open hold and is allocated again at its next session-ready or `OpenHold` (user decision at `/code-review`); the Implementation Notes describe the allocation only.
- `OnSessionReadySent` requires a character registered with the inventory service; this is a documented precondition, not a check (user decision at `/code-review`). For an unregistered character the sync would list every slot empty.
- `IClientMessageOutbox.Enqueue` is assumed not to throw; a throw during `ReleaseHold` would lose the remaining held updates. Left as is (user decision at `/code-review`) and recorded in the sender's remarks for the transport adapter story (ADR-014).
- `DiscardHold` after a zone entry during the hold leaves the connected client with no `InventoryFullSync` before later updates. This is the Discard criterion as written; it is correct only because a failed commit disconnects the client (CR-NET-5.5). Enhancement Story 015 carries the note: a step 4 failure calls `ReleaseHold`.
- Misuse of the hold — opening an open hold, releasing or discarding with none open — logs one warning and changes nothing. The story does not state this.
- The sender forwards the service's entries unchanged; "no slot index appears twice" rests on `InventoryService` recording each slot once per event. The decoders check the count and the lengths only.
- The `MessageTypeId` values are provisional; no ADR assigns them.
- `OnSessionReadySent` and `OnClientDisconnected` are on the concrete class only; the story that sends `SessionReady` may want an interface.
