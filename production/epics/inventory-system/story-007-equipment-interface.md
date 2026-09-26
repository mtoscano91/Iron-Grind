# Story 007: Equipment System Interface (HasFreeSlot, MoveItemOut, MoveItemIn, ForceInsert)

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 8 (Unequip to Bag), Interactions table (Equipment System row, updated 2026-05-22 per OQ-EQS-3), Full Inventory / Capacity Edge Cases
**Requirement**: `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Equipment System depends on the injected `IInventoryService` (Tier 1 direct calls with return values); inventory slot changes are broadcast via `OnInventoryChanged`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. No Equipment System exists yet — tests call the interface directly, standing in for it.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args; dependency via interface, not concrete class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-10 (inventory side)**: All 20 slots occupied. `HasFreeSlot()` returns `false`; the (simulated) equip is therefore not executed — all 20 slots are identical before and after (read every slot) and no `OnInventoryChanged` fires. *(Part (a), "equipped item stays equipped", is Equipment System behaviour — verified in that epic.)*
- [ ] **MoveItemOut(slotIndex) → MoveItemOutResult { ItemID, Code }**: occupied unlocked slot → `Code = Success`, returns its ItemID, slot becomes empty, one event. Empty slot → `SlotEmpty`, no mutation. Locked slot → `SlotLocked`, no mutation.
- [ ] **MoveItemIn(ItemID) → MoveItemInResult { success, slotIndex }**: places the item (qty 1) in the lowest-index empty slot, returns that index, one event. No empty slot → `success = false, slotIndex = -1`, no mutation. The free-slot check happens at execution time (Edge: `HasFreeSlot` was true at query time but the bag filled before the call).
- [ ] **ForceInsert(ItemID) → bool**: inserts the displaced item into any free slot (lowest index), returns `true`; returns `false` only if all 20 slots are occupied, and in that case fires `OnInventoryFull` for the character (triggers the `InventoryFullNotification` wire message per the GDD).
- [ ] **Rule 8.23**: `HasFreeSlot()` counts only truly empty slots (`ItemID.Invalid, Quantity = 0`) — a partial consumable stack is not a free slot for equipment.

---

## Implementation Notes

- `MoveItemIn` and `ForceInsert` place equipment items (StackLimit = 1) — never merge into an existing slot, even with the same ItemID.
- `MoveItemIn` vs `ForceInsert`: identical placement logic; the difference is the contract — `ForceInsert` is the auto-swap displacement path and signals full-bag via `OnInventoryFull`. **Dedup question (confirm at readiness):** does `ForceInsert`'s full-bag signal go through Story 003's 30s dedup window? Recommended: yes — one bag-full notification policy for the whole system.
- Out-of-range `MoveItemOut` index → treat as `SlotEmpty`-equivalent failure with an error log, no exception (no dedicated result code exists in the GDD).
- `MoveItemIn`/`ForceInsert` with `ItemID.Invalid` → reject (`false` / `-1`), no mutation.

---

## Out of Scope

- Equipment System's equip/unequip orchestration, error text ("Inventory full — free a slot before equipping"), and equipped-item retention (Equipment System epic)
- `InventoryFullNotification` wire message (Networking)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_EquipmentInterface_tests.cs`

- **AC-INV-10** — Given 20 occupied slots (snapshot all 20); When the test (as Equipment System) calls `HasFreeSlot()` → false and therefore does not call `MoveItemIn`; Then all 20 slots equal the snapshot, 0 events. Edge: 19 occupied incl. a partial HP Potion stack at 50/99 and 1 empty → `HasFreeSlot` true; 20 occupied incl. a partial stack → false.
- **MoveItemOut** — Bronze Sword slot 5 → `Success`, `ItemID == BronzeSword`, slot 5 empty, event `{5, 0, 0}`; empty slot 6 → `SlotEmpty`; locked slot 5 → `SlotLocked`, slot unchanged, 0 events.
- **MoveItemIn** — empties at 3 and 8 → returns `{true, 3}`, event `{3, BronzeSword, 1}`; full bag → `{false, -1}`, no mutation, no event.
- **Execution-time check** — `HasFreeSlot()` true with one empty slot; fill it via `Pickup`; then `MoveItemIn` → `{false, -1}` (item not silently lost — the caller keeps it).
- **ForceInsert** — one empty slot → `true`, placed there; full bag → `false`, `OnInventoryFull` fired once, no mutation.
- **No merge** — Bronze Sword in slot 0, `MoveItemIn(BronzeSword)` → lands in slot 1, slot 0 still qty 1.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_EquipmentInterface_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 003 (`OnInventoryFull` for `ForceInsert`); Story 004 (`SlotLocked` for `MoveItemOut`)
- Unlocks: Equipment System epic (not yet created)
