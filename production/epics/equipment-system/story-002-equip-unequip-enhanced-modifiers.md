# Story 002: Equip Into an Empty Slot, Unequip, Enhanced Modifiers

> **Epic**: Equipment System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-5 (equip into empty slot), CR-EQS-7 (unequip), "Enhanced modifier registration" rule, F-EQS-1, EC-EQS-1, EC-EQS-4
**Requirement**: `TR-equip-003`, `TR-equip-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Direct Tier 1 calls into the injected Inventory and Character Stats services, acting on their return values; `readonly struct` data across boundaries.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: No engine API. `Debug.LogError` with a fixed prefix is the project's critical-log convention (no alert service in code).

**Control Manifest Rules (this layer)**:
- Required: Tier 1 calls into injected services; act on return values (ADR-010)
- Forbidden: `async` / `await` / `Task.Wait` in tick-driven core code (ADR-011) — every call here is synchronous
- Guardrail: no allocation per equip beyond what Character Stats does

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md`, scoped to this story:*

- [x] **AC-EQS-1** — equip into an empty slot registers every modifier: STR 50 → 60, DEF 30 → 38; slot `(ItemX, Occupied)`; item gone from the bag
- [x] **AC-EQS-3** — unequip returns the item to the bag and removes its modifiers: MaxHP 512 → 500; slot `(Invalid, Empty)`
- [x] **AC-EQS-7** — unequip with a full bag returns `InventoryFull`; no modifier removed; slot unchanged (CR-EQS-7 step 1, EC-EQS-1)
- [x] **AC-EQS-13** — after equipping weapon ItemX, `GetEquippedWeaponItemID` = ItemX on repeated calls
- [x] **AC-EQS-14** — all 7 slots equip independently; each stat's effective value = baseline + the sum of the modifiers on it
- [x] **AC-EQS-17** — a +5 Iron Sword keeps level 5 through equip (slot level 5, weapon level 5, source slot empty) and unequip (lowest free bag slot holds it at level 5; weapon slot Empty, level 0)
- [x] **AC-EQS-29** — `AddEquipmentModifier` receives `IEnhancementBonusProvider.GetFlatBonus(level, modifier.FlatBonus, item.GearTier)` (stub returns 28 for `(5, 10, Bronze)` → 28 registered; level 0 → 10), never the raw value
- [x] **AC-EQS-32** — equip into an empty slot from a locked bag slot returns `ItemLocked`; nothing changes (no `AddEquipmentModifier`, no `OnStatChanged`); a `SlotEmpty` code from `MoveItemOut` returns `ItemNotInInventory` (CR-EQS-5 step 2)
- [x] CR-EQS-7 step 5 — `MoveItemIn` failing on unequip re-adds the modifiers (enhanced, at the slot's level), clears `IsTransitioning`, returns `InventoryError`; slot unchanged
- [x] CR-EQS-7 step 2 — `GetItem` null on unequip: `Debug.LogError` containing `Unequip GetItem null` and the item id, no modifier removal, slot cleared (Invalid, level 0, not transitioning), returns `Success`

---

## Implementation Notes

*Derived from the GDD rules (ADR-010 governs the call style):*

- **`Equip(CharacterID, GearSlot slot, ItemID itemId, int inventorySlotIndex)`, empty-slot path (CR-EQS-5):** `GetItem(itemId).EquipmentData` → `MoveItemOut(charId, inventorySlotIndex)`; on `Code ≠ Success` return `ItemLocked` (`SlotLocked`) or `ItemNotInInventory` (`SlotEmpty`) with no other call → for each `StatModifiers[]` entry `AddEquipmentModifier(entityId, mod.StatId, new EquipmentModifierEntry(enhancedFlat, 0f, itemId))` (PctBonus 0: `StatModifierEntry` has no PctBonus field; the GDD's `modifier.PctBonus` is stale — `/code-review` 2026-10-26) with `enhancedFlat = GetFlatBonus(result.EnhancementLevel, …, equipment.GearTier)` → write `{itemId, false, result.EnhancementLevel}` to the slot.
  Validation (slot type, stat gate, same-item, input) is story 003; the occupied-slot branch is story 004 — until then an occupied target may throw `NotSupportedException`, which story 004 removes.
- **`Unequip(CharacterID, GearSlot slot)` (CR-EQS-7):** order is exactly `HasFreeSlot` → `GetItem` (null branch) → `IsTransitioning = true` → `RemoveEquipmentModifier` per modifier → `MoveItemIn(itemId, entry.EnhancementLevel)` (fail → re-add, clear flag, `InventoryError`) → clear the slot. Unequipping an empty slot: return `NoChange` (no rule names a code — document it; story 003 may revisit).
- **Enhanced modifier registration** is one private helper used by every add path (equip, swap, rollbacks, restore in story 007) so the rule cannot be bypassed.
- **FlatBonus conversion (decided at `/story-readiness` 2026-10-26):** `GetFlatBonus` takes `int baseFlatBonus`; `StatModifierEntry.FlatBonus` is `float`. The enhanced-registration helper passes `(int)MathF.Round(mod.FlatBonus, MidpointRounding.AwayFromZero)`; when `mod.FlatBonus` is not a whole number it calls `Debug.LogError` with the `[EquipmentService]` prefix (the service's existing log prefix), the item id and the stat id (a data defect: the GDD declares the base as `int`), then continues with the rounded value. A NaN, infinite or out-of-`int`-range value logs and that modifier is skipped (`/code-review` 2026-10-26). It never throws on the equip path.
- **Modifier layer cap (CR-EQS-5 step 3, EC-EQS-5; decided at `/story-readiness` 2026-10-26):** unreachable with valid data. There are 7 slots and the Item Database validator allows at most 2 modifiers per item, so a stat gets at most 14 equipment modifiers, below the Character Stats limit of 16. `AddEquipmentModifier` returns `void`, and Character Stats already calls `Debug.LogError` when it drops a modifier at the limit; that log covers step 3. Equipment adds no check, Character Stats is unchanged, and no test is written for it.
- Appearance flags update (CR-EQS-5 step 5, CR-EQS-7 step 7) is wired in story 005.

---

## Out of Scope

- Story 003: input validation, `SlotMismatch`, stat gate, same-item guard
- Story 004: auto-swap into an occupied slot
- Story 005: appearance flag recompute after equip / unequip

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_EquipUnequip_tests.cs` (new). Real `CharacterStats` and `InventoryService`; a stub `IEnhancementBonusProvider` where the AC says so, otherwise the real `EnhancementBonusProvider`; an Inventory decorator to inject `MoveItemIn` / `MoveItemOut` failures.

- **Read surface, occupied (carried over from Story 001 `/code-review` 2026-10-26)** — after a successful equip of item X at level N in a slot, every read reports it: `GetEquipmentSlotState` = `(X, Occupied)`, `GetSlotEnhancementLevel` = N, `IsSlotTransitioning` false; for the Weapon slot also `GetEquippedWeaponItemID` = X, `GetEquippedWeaponEnhancementLevel(charId)` = N, and through `IEquippedWeaponQuery` with the registered `EntityID`: `GetEquippedWeaponID` = X, `GetEquippedWeaponEnhancementLevel` = N. Story 001 could not reach these paths (nothing could fill a slot).

- **AC-EQS-1** — Given STR 50 / DEF 30 and Item X `[{STR,10},{DEF,8}]` at level 0 in slot 0; When `Equip(Weapon, X, 0)`; Then `Success`, STR 60, DEF 38, state `(X, Occupied)`, bag slot 0 empty.
- **AC-EQS-3** — Given Item Y (+12 MaxHP) equipped in Helmet, MaxHP 512; When `Unequip(Helmet)`; Then MaxHP 500, Y in the bag, state `(Invalid, Empty)`.
- **AC-EQS-7** — Given an equipped item and a full bag; When `Unequip`; Then `InventoryFull`, effective stats unchanged, slot unchanged, zero `RemoveEquipmentModifier` (stat-change subscriber sees no event).
- **AC-EQS-13** — Given Weapon slot empty (Invalid confirmed); When ItemX is equipped; Then `GetEquippedWeaponItemID` = X on three consecutive calls.
- **AC-EQS-14** — 7 items, one per slot, one modifier each; every call `Success`, all 7 Occupied, per-stat delta = sum of that stat's modifiers.
- **AC-EQS-17** — +5 Iron Sword in slot S, another slot free; equip → slot level 5, weapon level 5, S empty; unequip → lowest free slot holds the sword at level 5, weapon `(Invalid, Empty)`, level 0. Edge: S is not the lowest free slot afterwards (assert by index).
- **AC-EQS-29** — stub returns 28 for `(5, 10, Bronze)` and 10 for `(0, 10, Bronze)`; equip at level 5 → effective STR delta 28 and the stub saw `(5, 10, Bronze)`; same sword at level 0 → delta 10.
- **AC-EQS-32** — sword at level 2 in locked slot S; `Equip(Weapon, sword, S)` → `ItemLocked`, slot `(Invalid, Empty)`, level 0, not transitioning, S still holds the sword at level 2 and is locked, effective stats unchanged, no stat event.
- **Fractional FlatBonus** — fake Item Database item with `FlatBonus` 10.6 on STR at level 0; equip → `Success`, the stub saw `baseFlatBonus` 11, exactly one `LogError` containing the item id (`LogAssert.Expect`). Control: `FlatBonus` 10.0 logs nothing.
- **`SlotEmpty` mapping** — decorator makes `MoveItemOut` return `SlotEmpty` → `ItemNotInInventory`, no stat call.
- **Unequip `MoveItemIn` fails** — decorator fails `MoveItemIn`; item at level 3 → `InventoryError`, slot `(item, Occupied)` level 3, effective stats equal the pre-call value (modifiers re-added once, enhanced), `IsSlotTransitioning` false.
- **Unequip `GetItem` null** — item definition missing (fake Item Database) → `Success`, slot `(Invalid, Empty)` level 0, one `LogError` containing the item id (`LogAssert.Expect`), no `RemoveEquipmentModifier`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EquipmentSystem/EquipmentSystem_EquipUnequip_tests.cs` — must exist and pass

**Status**: [x] Created — 36 tests (27 `[Test]` + 9 `[TestCase]`), all passing (EditMode 4185 / 0 failed, 2026-10-26)

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 004, 006

---

## Completion Notes

**Completed**: 2026-10-26
**Criteria**: 10/10 passing, all covered by `tests/EditMode/EquipmentSystem/EquipmentSystem_EquipUnequip_tests.cs` (none deferred)
**Deviations** (all advisory):
1. Unregistered character, slot outside `[0, 6]`, or an item with no `EquipmentData` on `Equip` logs and returns `NoChange`; unequipping an empty slot returns `NoChange`. No GDD rule names these codes — Story 003 may revisit.
2. `PctBonus` registered as 0: `StatModifierEntry` has no PctBonus field (the GDD's `modifier.PctBonus` is stale).
3. FlatBonus: rounded half away from zero; a fractional value logs (again on an unequip rollback re-add); NaN / infinite / out-of-`int`-range logs and that modifier is skipped.
4. CR-EQS-5 step 3 modifier-cap CriticalError not implemented — unreachable (7 × 2 = 14 < 16), Character Stats logs its own drop (decided at `/story-readiness`).
5. No check on the level `MoveItemOut` returns: the inventory rejects a level above the `EnhancementConfig` cap at placement, so it cannot hold one (code comment; `/code-review` fix #1 found not implementable).
6. `Equip` does not check that `MoveItemOut` returned `itemId`; Story 003's stale-render guard (AC-EQS-33 (b)) closes it — noted in Story 003.
7. Occupied target throws `NotSupportedException` until Story 004.
**Test Evidence**: Logic — `tests/EditMode/EquipmentSystem/EquipmentSystem_EquipUnequip_tests.cs`, 36/36; suite 4185 passed / 0 failed / 111 skipped
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, "fix all" applied 2026-10-26
