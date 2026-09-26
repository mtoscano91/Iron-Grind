# Story 008: NPC Shop Sell & Consumable Use Interfaces

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Interactions table (NPC Shop and Consumable Use System rows), Cross-System Interface Edge Cases
**Requirement**: `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: NPC Shop and Consumable Use System call the injected `IInventoryService` directly (Tier 1) and act on the return value; slot changes are broadcast via `OnInventoryChanged`. Gold is never touched here — NPC Shop calls the Currency System itself.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args; interface dependency — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-16**: Slot 4 holds 5 HP Potions, unlocked. `SellItem(4, HPPotion)` returns `quantity = 5`; slot 4 becomes `ItemID.Invalid, Quantity = 0`; one `OnInventoryChanged` with one entry `{ slotIndex: 4, itemId: 0, quantity: 0 }`.
- [ ] **Sell — entire stack only**: `SellItem` always removes the whole stack (no partial-stack sells at MVP).
- [ ] **Edge — ItemID mismatch**: `SellItem(slot, itemId)` where the slot's current ItemID differs → rejected with an error, no mutation, no event.
- [ ] **Sell — locked / empty / out-of-range slot**: rejected, no mutation, no event (locked items cannot be sold — Rule 5.12).
- [ ] **ConsumeItem(ItemID, quantity)**: decrements from the lowest-index slot holding that ItemID first (0→19); a stack reaching 0 becomes `ItemID.Invalid, Quantity = 0`; one event per call listing every slot changed.
- [ ] **Edge — ConsumeItem on a missing item**: `ConsumeItem` for an ItemID no longer in the bag (e.g. discarded after hotbar assignment) returns failure, no mutation, no event.

---

## Implementation Notes

- `SellItem` returns quantity removed (0 or a failure result on rejection — pick a result type that makes failure unambiguous, e.g. `SellItemResult { bool Success; int QuantitySold }`, since a valid sell always removes ≥ 1).
- `ConsumeItem` across multiple stacks: if `quantity` exceeds the first stack, continue into the next lowest-index stack. If the total held is less than `quantity`, fail atomically with no mutation (same plan-then-commit approach as Story 002). MVP calls use `quantity = 1`.
- **Locked stacks and ConsumeItem (confirm at readiness):** the GDD doesn't say whether a locked consumable stack can be consumed. Recommended: skip locked slots (locked items cannot be moved/sold/discarded — consuming is equivalent). Only consumables can be consumed; Enhancement locks equipment in practice, so this is a defensive rule.
- `quantity ≤ 0` → failure, no mutation.

---

## Out of Scope

- Gold payout on sell (NPC Shop → Currency System)
- Consumable effects, cooldowns, hotbar model (Consumable Use System; OQ-INV-2)
- Item detail view Use / Assign to Hotbar UI (AC-INV-11 — deferred to Inventory UI epic)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_SellAndConsume_tests.cs`

- **AC-INV-16** — Given HP Potion ×5 in slot 4; When `SellItem(4, HPPotion)`; Then success, `QuantitySold == 5`, slot 4 Invalid/0, one event `{4, 0, 0}`.
- **Mismatch** — slot 4 HP Potion; `SellItem(4, BronzeSword)` → fail, slot unchanged, 0 events.
- **Locked / empty / out of range** — `LockSlot(4)` then `SellItem(4, HPPotion)` → fail, unchanged; empty slot → fail; `SellItem(20, …)` → fail, no exception.
- **Consume FIFO** — HP Potion ×3 in slot 2 and ×10 in slot 8; `ConsumeItem(HPPotion, 1)` → slot 2 ×2, slot 8 ×10, event `{2, HPPotion, 2}`.
- **Consume empties a stack** — slot 2 ×1; `ConsumeItem(HPPotion, 1)` → slot 2 Invalid/0; next `ConsumeItem` takes from slot 8.
- **Consume across stacks** — slot 2 ×3, slot 8 ×10; `ConsumeItem(HPPotion, 5)` → slot 2 empty, slot 8 ×8, one event with both entries.
- **Insufficient total** — slot 2 ×3 only; `ConsumeItem(HPPotion, 5)` → fail, slot 2 still ×3, 0 events.
- **Missing item** — no HP Potion in bag → `ConsumeItem(HPPotion, 1)` fail, 0 events; `HasItem` false.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SellAndConsume_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for the locked-sell case)
- Unlocks: NPC Shop and Consumable Use System epics (not yet created)
