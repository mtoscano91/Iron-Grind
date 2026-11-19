# Story 003: Shop Wire Messages

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` §NPC Shop System Messages; `networking-message-criticality.md` MCR-2; `networking-channel-contract.md` CCR-3; `npc-shop.md` OQ-NS-7
**Requirement**: `TR-shop-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-001 Amendment A1 (dedup key)
**ADR Decision Summary**: R-OD requests carry `requestId`; the type id comes from the envelope; codecs live in Foundation (ADR-012).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — pure C# codecs.

**Control Manifest Rules (this layer)**:
- Required: R-OD IDs in 0xE000–0xEFFF; codecs allocation-free (Span-based)
- Forbidden: hardcoded type IDs in the dedup layer (A1)

---

## Acceptance Criteria

- [ ] **AC-1 IDs**: 14 new MessageTypeIds assigned, all in 0xE000–0xEFFF and unused: `BuyRequest`, `BuyResult`, `SellRequest`, `SellResult` and the 10 rejections (`RejectedNoNPCSession`, `RejectedInvalidQuantity`, `RejectedItemNotInCatalog`, `RejectedInsufficientFunds`, `RejectedInventoryFull`, `RejectedInvalidSlot`, `RejectedItemMismatch`, `RejectedSlotLocked`, `RejectedUnsellable`, `RejectedRateLimited` — confirm the exact 10 against the wire GDD list). The shop block 0xE114–0xE11F holds 12; use a second free block (e.g. 0xE180+) and record every id in the wire GDD.
- [ ] **AC-2 Codecs**: each message has a struct + codec with `BodySize` matching the wire GDD (BuyRequest 9 B, BuyResult 13 B, SellRequest 10 B, SellResult 17 B, rejections 4 B `requestId`); round-trip tests for every field, including `uint` maxima.
- [ ] **AC-3 Malformed**: `TryRead` on a body shorter than `BodySize` returns false; a `SellResult` / `BuyResult` with field values out of schema range (e.g. quantity 0) is rejected by the decoder where the GDD says so.
- [ ] **AC-4 Routing**: all 14 registered in `MessageRoutingRegistry` with the R-OD priority path, direction (C→S requests; S→owning-client results and rejections) and the MCR-2 pillar; the registry's own consistency tests pass.
- [ ] **AC-5 Wire GDD**: the NPC Shop section lists the ids, and the dedup comment reads `(SenderEntityID's character, messageType, requestId)` per A1 (epic open item 2).

---

## Implementation Notes

- Follow `PartyInviteRequest` / `PartyInviteResult` (Party Story 006) and the existing `OpenNPCInteraction*` files in `src/Foundation/Networking/WireProtocol/` for struct + codec shape and the registry calls.
- Read the field order and widths from the wire GDD (BuyRequest: `requestId uint, itemId uint, quantity byte`; check `quantity` width — byte caps it at 255, the 99 cap is server validation; a negative quantity (AC-NS-37) cannot be expressed in a byte, so note in Story 006 how AC-NS-37 is met).
- Memory rule: check each id against the R-OD range before writing.

---

## Out of Scope

- Stories 005 / 006: handlers; Story 009: dispatcher registration in the zone

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: a test enumerates the 14 ids → all distinct, all in [0xE000, 0xEFFF], none equal to any other registered id.
- **AC-2**: round-trip each message with typical and max values; `BodySize` constants equal the GDD numbers.
- **AC-3**: each `TryRead` with `BodySize − 1` bytes → false.
- **AC-4**: registry lookups for each id → R-OD, correct direction.
- **AC-5**: doc check at review.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_WireMessages_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 004, 005, 006
