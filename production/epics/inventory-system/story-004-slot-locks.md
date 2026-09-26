# Story 004: Slot Locks (Enhancement Reservation) & RemoveItem

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 5 (Item Locks), States and Transitions (Occupied-Locked), Lock State Edge Cases, `RemoveItem` out-of-range edge case
**Requirement**: `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Tier 1 for the Enhancement → Inventory calls; `OnInventoryChanged` on `RemoveItem`). Lock logic: none — design-only, LOW risk.
**ADR Decision Summary**: Enhancement System calls `LockSlot`/`UnlockSlot`/`RemoveItem` directly on the injected `IInventoryService`; slot-content changes are broadcast via `OnInventoryChanged`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **Rule 5.12/5.13**: `LockSlot(characterId, slotIndex)` on an occupied slot sets the lock; `IsSlotLocked` returns `true`. `UnlockSlot` clears it; `IsSlotLocked` returns `false`. The item's `ItemID`/`Quantity` never change from lock/unlock, and no `OnInventoryChanged` fires for lock/unlock alone.
- [ ] **Edge — LockSlot on an empty slot**: no-op, no lock flag set, server warning logged.
- [ ] **Edge — UnlockSlot on an unlocked slot**: no-op, no exception (defensive double-unlock from Enhancement timeout paths must be safe).
- [ ] **States — RemoveItem on a locked slot**: `RemoveItem(characterId, slotIndex)` (item destruction) clears the slot to `ItemID.Invalid, Quantity = 0`, clears the lock, and fires one `OnInventoryChanged` `{ slotIndex, itemId: 0, quantity: 0 }`. It is the only path from Occupied-Locked to Empty.
- [ ] **Edge — RemoveItem out of range** (`< 0` or `≥ 20`): server error logged, no-op, no exception, no event.
- [ ] **Edge — all 20 slots locked**: `HasFreeSlot()` returns `false`; not an error state.
- [ ] **Story 002 guard**: a pickup does not add units to a locked partial stack (it proceeds to the next partial stack / empty slot per Rule 3).

---

## Implementation Notes

- Lock flags are per-slot, session-scoped, and **never persisted** (Story 009 exports contents only).
- Out-of-range `LockSlot`/`UnlockSlot`/`IsSlotLocked` indices: treat like `RemoveItem` — log error, no-op, `IsSlotLocked` returns `false`. *(Consistency extension of the GDD's `RemoveItem` rule.)*
- `RemoveItem` on an **unlocked** occupied slot: the GDD only defines it for the Enhancement destroy outcome on a locked slot. Recommended: allow it (clears the slot, fires the event) since Enhancement may destroy immediately; confirm at `/story-readiness` if a stricter "locked-only" contract is wanted.
- The Enhancement lock TTL (OQ-INV-4) is **not** implemented here — owned by the Enhancement System GDD.
- Add the Story 002 locked-partial-stack guard here if Story 002 landed first (and its test).

---

## Out of Scope

- Rejecting discard/move/sell/equip on locked slots — Stories 005, 006, 007, 008 each enforce and test their own lock check
- Enhancement System logic and lock TTL (OQ-INV-4)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_SlotLocks_tests.cs`

- **Lock/unlock round-trip** — Given sword in slot 3; When `LockSlot(3)`; Then `IsSlotLocked(3)` true, slot contents unchanged, 0 `OnInventoryChanged`; When `UnlockSlot(3)`; Then false. Edge: other slots' lock state unaffected.
- **Lock empty slot** — `LockSlot(5)` on empty slot → `IsSlotLocked(5)` false; a later pickup into slot 5 lands normally and the slot is unlocked.
- **Double unlock** — `UnlockSlot(3)` twice on a never-locked slot → no exception, state unchanged.
- **RemoveItem on locked slot** — Given sword locked in slot 3; When `RemoveItem(3)`; Then slot 3 = Invalid/0, `IsSlotLocked(3)` false, one event `{3, 0, 0}`.
- **RemoveItem out of range** — `RemoveItem(-1)` and `RemoveItem(20)` → no exception, all slots unchanged, no event.
- **All locked** — seed 20 occupied, lock all → `HasFreeSlot` false.
- **Pickup skips locked partial stack** — slot 0 HP Potion 50/99 locked, slot 1 HP Potion 40/99 unlocked, `Pickup(HPPotion, 10)` → slot 0 stays 50, slot 1 = 50.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SlotLocks_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 002 (for the pickup-guard test)
- Unlocks: Stories 005, 006, 007, 008 (their locked-slot rejection cases)
