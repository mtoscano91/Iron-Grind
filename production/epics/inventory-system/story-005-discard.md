# Story 005: Discard (Server-Side Validation & Mutation)

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 6 (Discard), Discard and Move Edge Cases
**Requirement**: `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (`OnInventoryChanged` emission). Discard rules: none — design-only, LOW risk.
**ADR Decision Summary**: The network handler for `DiscardRequest` calls `IInventoryService.Discard(...)` directly (Tier 1) and maps the returned result to the `DiscardResult` wire message; successful mutations broadcast `OnInventoryChanged`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-4**: Slot locked by the Enhancement System; `Discard(slotIndex, quantity = 1)` returns `success = false, error = SlotLocked`; no `OnInventoryChanged`; ItemID and Quantity unchanged.
- [ ] **AC-INV-7a**: Slot holds 45 HP Potions, unlocked; `Discard(slot, 20)` returns success; slot holds exactly 25; one `OnInventoryChanged` with one entry `{ slotIndex, itemId: HPPotion, quantity: 25 }`.
- [ ] **AC-INV-7b**: Slot holds 45 HP Potions; `Discard(slot, 45)` returns success; slot becomes `ItemID.Invalid, Quantity = 0`; one event entry `{ slotIndex, itemId: 0, quantity: 0 }`.
- [ ] **AC-INV-7c**: Slot holds 45 HP Potions; `Discard(slot, 100)` returns `success = false, error = InvalidQuantity`; no event; slot still 45.
- [ ] **Rule 6.18 validation**: `quantity ≤ 0` → `InvalidQuantity`; empty slot → failure (no item to discard); out-of-range slot index → failure; none mutate state or fire events.
- [ ] **Rule 6.17 equipment**: discarding an equipment item (qty 1) empties the slot.

---

## Implementation Notes

- Result type: `DiscardResult { bool Success; DiscardFailReason Error }`. Reuse the `DiscardFailReason` enum values already specified in `design/gdd/networking-wire-protocol.md` (Inventory System Messages section, OQ-INV-6) — read that section and mirror its members/order exactly so the wire codec can map 1:1 later. If the wire enum has no value for "empty slot" or "out of range", note it and use the closest defined value; do not invent new wire values.
- Validation order: range → lock → empty → quantity bounds (`0 < quantity ≤ current Quantity`). Lock check first among item checks matches AC-INV-4 (a locked slot reports `SlotLocked` even for a valid quantity).
- The hold-to-confirm gesture and quantity selector (Rule 6.16, OQ-INV-1 `DISCARD_HOLD_DURATION`) are client UI — not here.
- Same-tick `DiscardRequest` vs `LockSlot` race (Edge Cases): resolved by FIFO call order — no special handling; covered by a sequential-order test.

---

## Out of Scope

- `DiscardRequest`/`DiscardResult` wire codecs (Networking)
- Discard gesture, quantity selector, hold duration (Inventory UI)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_Discard_tests.cs`

- **AC-INV-4** — Given HP Potion ×45 in slot 6, `LockSlot(6)`; When `Discard(6, 1)`; Then `Success=false`, `Error=SlotLocked`, slot 6 still ×45, 0 events.
- **AC-INV-7a** — Given ×45 in slot 6; When `Discard(6, 20)`; Then success, slot ×25, one event with one entry `{6, HPPotion, 25}`.
- **AC-INV-7b** — Given ×45; When `Discard(6, 45)`; Then success, slot Invalid/0, event `{6, 0, 0}`. Edge: subsequent `HasItem(HPPotion)` false.
- **AC-INV-7c** — Given ×45; When `Discard(6, 100)`; Then `InvalidQuantity`, slot ×45, 0 events. Edge: `Discard(6, 46)` also `InvalidQuantity`; `Discard(6, 0)` and `Discard(6, -1)` → `InvalidQuantity`.
- **Empty / out-of-range** — `Discard(10, 1)` on empty slot → fail, no event; `Discard(20, 1)`, `Discard(-1, 1)` → fail, no exception.
- **Equipment** — Bronze Sword in slot 0, `Discard(0, 1)` → slot empty, event `{0, 0, 0}`.
- **FIFO race** — `Discard(6, 45)` then `LockSlot(6)` → discard succeeds, lock is a no-op (slot empty, unlocked); reverse order → discard rejected `SlotLocked`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_Discard_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for AC-INV-4)
- Unlocks: None
