# Story 004: Slot Locks (Enhancement Reservation) & RemoveItem

> **Epic**: Inventory System
> **Status**: Complete
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
- [ ] **RemoveItem on an unlocked slot** (Enhancement CR-ENH-15 step 4 scroll removal): clears the whole slot to `ItemID.Invalid, Quantity = 0` regardless of stack size and fires one `OnInventoryChanged` `{ slotIndex, 0, 0 }`. On an empty in-range slot: no-op, no event.
- [ ] **Edge — RemoveItem out of range** (`< 0` or `≥ 20`): server error logged, no-op, no exception, no event.
- [ ] **Edge — unregistered character**: `LockSlot`/`UnlockSlot`/`RemoveItem` for an unregistered `charId` log a server error, no exception, no event.
- [ ] **Edge — all 20 slots locked**: `HasFreeSlot()` returns `false`; not an error state.
- [ ] **Story 002 guard**: a pickup does not add units to a locked partial stack (it proceeds to the next partial stack / empty slot per Rule 3).

---

## Implementation Notes

- Lock flags are per-slot, session-scoped, and **never persisted** (Story 009 exports contents only).
- **Signatures**: `void LockSlot(CharacterID charId, int slotIndex)`, `void UnlockSlot(CharacterID charId, int slotIndex)`, `void RemoveItem(CharacterID charId, int slotIndex)`. All three return `void` — the Enhancement System treats them as fire-and-forget (Enhancement GDD CR-ENH-15 steps 3, 4, 7); callers that need the resulting state query `IsSlotLocked` / `GetSlot`.
- Out-of-range `LockSlot`/`UnlockSlot`/`IsSlotLocked` indices: treat like `RemoveItem` — log error, no-op, `IsSlotLocked` returns `false`. *(Consistency extension of the GDD's `RemoveItem` rule.)*
- Unregistered `charId` on `LockSlot`/`UnlockSlot`/`RemoveItem`: log a server error, no-op, no event — mirrors the existing `IsSlotLocked`/`GetSlot` guard behavior.
- **`RemoveItem` works on any occupied slot, locked or unlocked** (resolved at `/story-readiness` 2026-09-26). It clears the whole slot to `ItemID.Invalid, Quantity = 0` regardless of `Quantity`, clears the lock flag if set, and fires one `OnInventoryChanged` `{ slotIndex, 0, 0 }`. Rationale: Enhancement GDD CR-ENH-15 step 4 calls `RemoveItem(scrollSlotIndex)` on the **unlocked** scroll slot, so a "locked-only" contract would break Enhancement. `RemoveItem` on an empty in-range slot: no-op, no event.
- **Cross-GDD conflict (open, owner: Enhancement System GDD — does not block this story):** Enhancement Scrolls are `ItemCategory = Consumable` (npc-shop.md:577) and therefore stack, but CR-ENH-15 step 4 consumes the scroll via `RemoveItem`, which clears the entire stack — a player with 20 scrolls in one slot would lose all 20 per attempt. Proposed resolution: change CR-ENH-15 step 4 to `ConsumeItem(scrollId, 1)` (Story 008) or a quantity-aware removal. Must be resolved in an Enhancement GDD authoring session before the Enhancement epic starts. This story implements the Inventory GDD contract as written.
- The Enhancement lock TTL (OQ-INV-4) is **not** implemented here — owned by the Enhancement System GDD.
- The Story 002 locked-partial-stack guard is **already implemented** — `Pickup` passes `_locks[characterId]` into `PlanPartialStacks` (`InventoryService.cs`). It was untestable until now because no lock-mutation API existed; this story adds its test only.

**Performance**: No performance impact expected — lock operations are O(1) flag writes on the existing per-character `bool[20]`, with no allocation; `RemoveItem` is a single slot write plus one event.

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
- **RemoveItem on unlocked stack** — Given HP Potion ×20 unlocked in slot 4; When `RemoveItem(4)`; Then slot 4 = Invalid/0, one event `{4, 0, 0}`. Edge: `RemoveItem(7)` on empty slot 7 → 0 events.
- **RemoveItem out of range** — `RemoveItem(-1)` and `RemoveItem(20)` → no exception, all slots unchanged, no event.
- **Unregistered character** — `LockSlot`/`UnlockSlot`/`RemoveItem` on an unregistered `charId` → no exception, 0 events (use `LogAssert.Expect` for the error logs).
- **All locked** — seed 20 occupied, lock all → `HasFreeSlot` false.
- **Pickup skips locked partial stack** — slot 0 HP Potion 50/99 locked, slot 1 HP Potion 40/99 unlocked, `Pickup(HPPotion, 10)` → slot 0 stays 50, slot 1 = 50.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SlotLocks_tests.cs` — must exist and pass

**Status**: [x] Created — 23 tests, all passing in live Test Runner

---

## Dependencies

- Depends on: Story 001; Story 002 (for the pickup-guard test)
- Unlocks: Stories 005, 006, 007, 008 (their locked-slot rejection cases)

---

## Completion Notes
**Completed**: 2026-09-26
**Criteria**: 9/9 passing (none deferred)
**Deviations** (advisory):
- Tech debt logged: TD-043 (Enhancement Scrolls stack, but CR-ENH-15 step 4 consumes them via `RemoveItem`, which clears the whole slot — owned by the Enhancement GDD, must resolve before the Enhancement epic).
- `TR-inv-004` not registered in `tr-registry.yaml` (epic-wide); implementation verified against GDD Rule 5 + Lock State Edge Cases directly.
- `RemoveItem` on an empty in-range slot is a silent no-op (story left logging open).
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_SlotLocks_tests.cs` (23 tests) + Story 001–003 suites, all passing in live Test Runner.
**Code Review**: Complete — unity-specialist CLEAN, qa-tester GAPS → all 5 suggestions applied (+5 tests, 1 doc fix).
