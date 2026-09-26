# Story 002: Atomic Pickup Resolution & Stack Limits

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 2 (Stack Limits), Rule 3 (Pickup Resolution — Atomic), Rule 10 (Within-Tick Ordering), F-INV-2 (Stack Overflow on Pickup), Pickup Edge Cases
**Requirement**: `TR-inv-002`, `TR-inv-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (`OnInventoryChanged` emission). Pickup algorithm: none — design-only, LOW risk.
**ADR Decision Summary**: Tier 1 — Loot Table System calls `IInventoryService.Pickup(...)` directly and gets a result back; Tier 2 — `OnInventoryChanged` (`readonly struct` arg) fires once per successful mutation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. Item definitions in tests via `ItemDefinition.SetForTesting` (existing `#if UNITY_EDITOR` seam) + a real or stub `IItemDatabase`.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args, `On`-prefixed event names — ADR-010
- Forbidden: per-emit heap allocation / boxing on the server tick path — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-2**: Slot 2 holds 80 HP Potions (StackLimit=99), slot 5 is empty, all other slots occupied. A pickup of 30 HP Potions succeeds; one `InventoryChangedEvent` contains exactly two entries: slot 2 → quantity 99, slot 5 → quantity 11 (F-INV-2: Overflow = max(0, 80+30−99) = 11; FIFO by slot index).
- [ ] **AC-INV-5**: All 20 slots occupied, exactly two slots each hold 98 HP Potions (StackLimit=99). A pickup of 3 HP Potions fails atomically: `PickupResult(fail)`, both potion slots still hold 98 (the Step 1 partial fills are rolled back), no `InventoryChangedEvent` fires.
- [ ] **AC-INV-6**: One slot holds a Bronze Sword (Equipment, StackLimit=1) and at least one empty slot exists. A pickup of a second Bronze Sword places it in a new empty slot; the original slot still holds exactly 1 Bronze Sword.
- [ ] **AC-INV-12**: Two pickups of different ItemIDs processed in a defined order (same tick) — the first occupies the lowest-index available slot, the second the next available slot. Verified via each `InventoryChangedEvent`'s slot index.
- [ ] **AC-INV-13**: Empty slots at 3, 7, 15 only; no partial stacks of the incoming ItemID. A pickup of HP Potions lands in slot 3.
- [ ] **Rule 3.7 Step 2 (multi-slot placement)**: when the remainder exceeds `StackLimit`, it fills successive empty slots in ascending order (e.g. StackLimit=10, pickup of 25 into an empty bag → slots 0/1/2 = 10/10/5), all reported in one event.
- [ ] **Edge — IncomingQty = 0**: rejected immediately, no slot mutation, no event, no phantom `(ItemID, 0)` slot.
- [ ] **Edge — consumable with StackLimit = 1**: each unit occupies its own slot, identical to equipment.

---

## Implementation Notes

*Derived from GDD Rule 3 (no ADR governs the algorithm):*

- Signature per GDD Interactions table: `Pickup(CharacterID, ItemID, int quantity) → PickupResult` (success/fail). `quantity = 1` for all MVP loot drops, but the algorithm must handle 1–99.
- Algorithm (single atomic transaction):
  1. Read `StackLimit` from `IItemDatabase.GetItem(itemId)`.
  2. **Step 1** — scan slots 0→19; for each slot with the same `ItemID` and `Quantity < StackLimit`, plan `min(remainder, StackLimit − Quantity)` units (F-INV-2). Items with StackLimit=1 skip Step 1.
  3. **Step 2** — scan 0→19 for empty slots; plan `min(remainder, StackLimit)` per empty slot until remainder = 0.
  4. **Step 3** — if remainder > 0 after Step 2, fail with **no writes at all**. Implement as plan-then-commit (compute all target slots first, write only on success) rather than write-then-rollback, so a failed pickup can never leave a partial state or fire an event.
- Locked slots (Story 004): a locked slot is still occupied; Step 1 must not add units to a locked partial stack (locked items "cannot be moved" — treat as unavailable). Until Story 004 lands, no slot can be locked; add the guard in whichever story lands second.
- Unknown `ItemID` (not in Item Database): reject with fail, no mutation. *(Defensive — not an explicit GDD rule; `GetItem` returns null.)*
- Rule 10 FIFO: the service processes calls in the order received; no reordering or priority logic — AC-INV-12 is proven by sequential calls.
- Notification side effects (`InventoryFullNotification`, 30s dedup) are Story 003 — this story only returns `PickupResult(fail)`.

---

## Out of Scope

- Story 003: `InventoryFullNotification` + dedup window on failed pickups
- Story 004: lock state (see guard note above)
- Loot Table System's drop-fate handling on fail (Loot Table GDD CR-LT-13)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_AtomicPickup_tests.cs`

- **AC-INV-2** — Given the seeded state above; When `Pickup(HPPotion, 30)`; Then result success, one event with entries `[{2, HPPotion, 99}, {5, HPPotion, 11}]` in that order; `GetSlot(2)`=99, `GetSlot(5)`=11. Edge: pickup of exactly 19 → only slot 2 changes (99), slot 5 stays empty, event has 1 entry.
- **AC-INV-5** — Given all 20 occupied with two 98-potion slots; When `Pickup(HPPotion, 3)`; Then fail, both slots 98, zero events fired (subscriber call count 0), all 20 slots byte-identical before/after. Edge: `Pickup(HPPotion, 2)` in the same state succeeds (98→99, 98→99).
- **AC-INV-6** — Given sword in slot 0, rest empty; When `Pickup(BronzeSword, 1)`; Then slot 1 = sword qty 1, slot 0 unchanged (qty 1). Edge: sword never merges even though slot 0 has the same ItemID.
- **AC-INV-12** — Given empty bag; When `Pickup(HPPotion,1)` then `Pickup(BronzeSword,1)`; Then first event slotIndex 0, second event slotIndex 1. Edge: reverse call order reverses slot assignment.
- **AC-INV-13** — Given empties at 3/7/15; When `Pickup(HPPotion, 1)`; Then event `slotIndex: 3`, slots 7 and 15 still empty.
- **Multi-slot placement** — StackLimit=10 test consumable, empty bag, `Pickup(25)` → slots 0/1/2 = 10/10/5 in one event. Edge: same pickup with only 2 empty slots → fail, no mutation.
- **Edge — qty 0** — `Pickup(HPPotion, 0)` → fail, no event, all slots unchanged.
- **Edge — consumable StackLimit=1** — two pickups of 1 → two separate slots, each qty 1.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_AtomicPickup_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (slot container, event type, seeding seam)
- Unlocks: Story 003
