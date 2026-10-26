# Story 008: `EquipRequest` / `EquipResult` — Codecs, Request Handler, Owner Delivery

> **Epic**: Equipment System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-13 (entry points the wire carries), `EquipResult` → wire mapping table, UI Requirements; `design/gdd/networking-wire-protocol.md` — `EquipRequest`, `EquipResult`, `EquipFailReason`; `design/gdd/networking-channel-contract.md` — `EquipRequest` row (R-OD, P1)
**Requirement**: `TR-equip-011`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-004: Networking Library (NGO); ADR-014: Inbound Request Dispatch and Tick Order; ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: The project owns its envelope and codecs on top of NGO custom messages (ADR-004 Amendment 1). Every client→server request reaches a game system only through the zone's `InboundRequestDispatcher`, behind the session-ready gate; `EquipRequest` is a **held** R-OD request with a 10-byte max body (ADR-014 Decision 1 table). Codecs and wire enums shared with the client live in `IronGrind.Foundation`; handlers live in `IronGrind.ServerLogic` (ADR-012).

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-004 — NGO 2.13.3 is post-cutoff)
**Engine Notes**: No direct NGO call is needed — follow the existing codec + `MessageRoutingRegistry` + dispatcher + `IZoneOutboundSender` pattern of Enhancement Stories 010 and 015. Do not add `NamedMessage` or `SetConnectionData` usage (control manifest).

**Control Manifest Rules (this layer)**:
- Required: one dispatcher per zone is the only path from a client message to a game system (ADR-014); a new request type adds its routing-registry entry and its dispatcher registration (ADR-014: "the story that adds a type adds its row"); core systems run in step 3 of `ZoneTickPipeline` (ADR-014)
- Forbidden: registering a core system's delegate with `ServerTickLoop.RegisterTickDriven` (ADR-014); server logic in `IronGrind.Foundation` (ADR-012)
- Guardrail: `EquipRequest` body ≤ 10 bytes, `EquipResult` body 12 bytes; reject a malformed body without touching the Equipment System

---

## Acceptance Criteria

*From the GDDs above, scoped to this story:*

- [ ] `EquipRequest` codec: `{uint requestId, byte gearSlot, uint itemId, byte inventorySlot}` — 10-byte body, round-trips; a short or long body is rejected
- [ ] `EquipResult` codec: `{uint requestId, bool success, byte failReason, byte affectedSlot, uint slotItemId, byte slotEnhancementLevel}` — 12-byte body, round-trips
- [ ] `EquipFailReason : byte` wire enum in `IronGrind.Foundation`: `None = 0`, `StatRequirementNotMet = 1`, `InventoryFull = 2`, `SlotMismatch = 3`, `ItemLocked = 4`, `ItemNotInInventory = 5`, `InventoryError = 6`, `CriticalFailure = 255`
- [ ] Routing: `itemId = 0` → `Unequip(charId, gearSlot)` (`inventorySlot` ignored); otherwise `Equip(charId, gearSlot, itemId, inventorySlot)`; a `gearSlot` outside `[0, 6]` reaches the service's own guard (Story 003) — the handler does not pre-validate
- [ ] Every `EquipResult` (system) value maps exactly as the GDD table states: `Success` / `NoChange` → `success = true, failReason = None`; each failure → `success = false` and the same-named `EquipFailReason`; `CriticalFailure` → 255. 1:1, no handler-side derivation (OQ-EQS-9 option a)
- [ ] Slot fields always echo the slot **after** the operation: success → new contents; unequip success → `slotItemId = 0`, level 0; `NoChange` and every failure → the unchanged slot (old item and level)
- [ ] The result goes to the requesting connection only
- [ ] Duplicate `requestId` from the same sender (retransmit) → the stored `EquipResult` of the original processing is re-sent; `Equip` / `Unequip` is not called again (wire protocol `EquipRequest` note)
- [ ] `EquipRequest` is registered in `MessageRoutingRegistry` and with the dispatcher as a **held** request (ADR-014 table), so an equip that arrives during an irreversible write waits and runs in arrival order when the gate opens
- [ ] Session end unregisters the character from the Equipment System: `SessionEndCoordinator.Release` calls `IEquipmentService.UnregisterCharacter(charId)` right after `_inventory.UnregisterCharacter(charId)` (new constructor dependency; update the callers in `ZoneInstancingWiring` and the three test fixtures that construct it); the handler's dedup entry for that sender is also cleared at session end, so a new session's `requestId` counter is not taken for a duplicate (the hook is chosen at `/dev-story` and tested). (Registration on load belongs to Character Persistence Story 005 — readiness decision 2026-10-26.)

---

## Implementation Notes

*Derived from ADR-004, ADR-014, ADR-012 and the existing Enhancement wire code:*

- **Foundation** (`src/Foundation/Networking/WireProtocol/`): `EquipRequest.cs`, `EquipRequestCodec.cs`, `EquipResultMessage.cs` (name it so it does not clash with the server `EquipResult` enum — the precedent is `EnhancementAttemptResultMessage`), `EquipResultMessageCodec.cs`, `EquipFailReason.cs`. Use `WireIdCodec` / `WireEnumCodec` like the existing codecs.
- **Message type ids (readiness decision 2026-10-26):** `EquipRequest.MessageTypeId = 0xE140`, `EquipResultMessage.MessageTypeId = 0xE141` — a new equipment block after Inventory sync (`0xE120`) and zone (`0xE130`). Both R-OD on Path 1 (priority, not cap-exempt), registered in `MessageRoutingRegistry` with `AddPriority` (`EquipRequest` `ClientToServer`, `EquipResultMessage` `ServerToClient`). The wire protocol's Path 1 list names both (GDD note 2026-10-26).
- **Rpc type tag:** add `RpcTypeTag.EquipRequest = 8` with no rate limit (ADR-014 table: "—"); the descriptor is `heldDuringIrreversibleWrite: true, maxBodyBytes: 10`.
- **ServerLogic** (`src/ServerLogic/EquipmentSystem/`): `EquipmentClientRequestHandlers.cs` and `EquipmentInboundRegistration.cs`, mirroring `EnhancementClientRequestHandlers` / `EnhancementInboundRegistration`. Sender `EntityID` → `CharacterID` through the session lookup the Enhancement handlers use.
- **Mapping** is one `static EquipFailReason ToWire(EquipResult)` with an exhaustive switch, unit-tested across all 9 values.
- **Dedup store:** per sender, keep the last processed `requestId` and its encoded result (the client counter is monotonic — wire protocol). A `requestId` ≤ the last seen is a duplicate: re-send the stored result for an equal id, drop an older one (document). Clear on session end.
- Validation order on the wire (gearSlot → stale-render guard → slot type → stat gate → free slot) is the service's (Story 003); the handler adds only body-length validation.
- **Wiring (readiness decision 2026-10-26):** `EquipmentInboundRegistration.Register(dispatcher, handlers)` is driven by the tests, like `EnhancementInboundRegistration`; neither is called from `ZoneServerComposition` yet (it runs on stand-ins). Calling it from the composition root, and passing `throwOnInvalidItem` explicitly (Story 003 carry-over), belong to the server bootstrap work. `RegisterCharacter` on load is Character Persistence Story 005's.
- **Forward dependencies — not in this story:** `StatSnapshotEvent` to the equipping player on success (no codec exists; Leveling / Networking); an owner gear-slot sync message on zone entry (wire protocol OQ-NC-SER-2 — "blocking for the Equipment UI"); a merge request message (no wire schema; ADR-014 table row "no wire schema yet").

---

## Out of Scope

- Story 009: `AppearanceChangedEvent` to the zone
- Equipment `RegisterCharacter` on load (Character Persistence Story 005) and calling the inbound registration from `ZoneServerComposition` (server bootstrap)
- Client-side Inventory UI handling of `EquipResult`
- The forward dependencies listed above

---

## QA Test Cases

**Files**: `tests/EditMode/Networking/EquipMessages_Codec_tests.cs` (new) and `tests/EditMode/EquipmentSystem/EquipmentSystem_RequestHandler_tests.cs` (new). Handler tests drive the real `InboundRequestDispatcher` with a fake outbound sender, as Enhancement Story 010 / 015 do.

- **Codec round trip** — `EquipRequest` with max values (`requestId = uint.MaxValue`, slot 6, `itemId = uint.MaxValue`, slot 19) and with `itemId = 0, inventorySlot = 0xFF`; body length exactly 10; 9- and 11-byte bodies rejected. Same for `EquipResultMessage` (12 bytes).
- **Mapping table** — all 9 `EquipResult` values → expected `(success, failReason)` pairs; byte value of each `EquipFailReason` member asserted.
- **Equip success** — request for a bag item into an empty Weapon slot → one `EquipResultMessage` to the sender only: `success`, `None`, `affectedSlot = 0`, new item id and level.
- **Unequip** — `itemId = 0` with `inventorySlot = 0xFF` → `Unequip` path; `slotItemId = 0`, level 0.
- **Failure echoes old slot** — swap rejected by the stat gate → `success = false`, `StatRequirementNotMet`, slot fields = the old item and level.
- **NoChange** — `success = true`, `None`, unchanged slot fields.
- **Locked / stale** — locked source → `ItemLocked` (4); stale slot → `ItemNotInInventory` (5).
- **Duplicate** — same `requestId` twice → two identical results sent, the service's `Equip` ran once (inventory and stats unchanged by the second).
- **Held** — with the mutation gate closed, an `EquipRequest` is held and processed when it opens, in arrival order with a second held request.
- **Session end** — `SessionEndCoordinator.Release` for a registered character → `IEquipmentService.UnregisterCharacter` called once, after the inventory unregister; a later request with the old `requestId` is processed, not answered from the dedup store. (File: `tests/EditMode/CharacterPersistence/CharacterPersistence_SessionEnd_tests.cs`.)
- **Routing entries** — `MessageRoutingRegistry` holds `0xE140` and `0xE141` as R-OD priority-path entries with the right directions.
- **Malformed body** — wrong length → no service call, no result (or the dispatcher's standard rejection — assert whichever the dispatcher defines).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: both test files above — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 003, 004 (every `EquipResult` value must be producible)
- Unlocks: Inventory UI equipment panel (Presentation, not yet in an epic)
