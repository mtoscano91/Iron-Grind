# Story 010: Per-Slot Enhancement Level

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 1.1/1.4 (slot `EnhancementLevel`), Rule 5.14a (`SetEnhancementLevel`), Rule 7.20 (level travels; `StackLimit = 1` items swap unless identical), Rule 8.24a (Equipment interface carries the level), Interactions table (Equipment, Enhancement, Character Persistence, Inventory UI rows), Cross-System Interface and Persistence and Load Edge Cases, AC-INV-17 – AC-INV-22. Added by the 2026-10-01 design session (TD-045); revised by the same-day lean re-review (level invariant keyed on `StackLimit = 1`; identical-item move is a no-op).
**Requirement**: `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (the change event's `readonly struct` entries gain a field; Tier 1 calls). ADR-006: Persistence Layer, Amendment 1 (2026-10-01) — `inventory_slots` entries carry `enhancement_level`; this story only extends the in-memory snapshot entry.
**ADR Decision Summary**: Enhancement and Equipment call the injected `IInventoryService` directly (Tier 1) and act on return values; slot changes are broadcast via `OnInventoryChanged` with zero-allocation `readonly struct` entries. The Inventory System stores and transports the level — it never computes with it.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. No engine API beyond `UnityEngine.Debug`.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args; interface dependency — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story. `MaxLevel` below is the injected maximum enhancement level (10 in tests).*

**Slot, event and read surface**

- [x] **Slot carries the level**: `InventorySlot` has `EnhancementLevel: byte`; `InventorySlot.Empty` has level 0; `GetSlot` returns it. `SlotChange` (the `OnInventoryChanged` entry) has `EnhancementLevel: byte` and every mutation's entry carries the slot's post-mutation level.
- [x] **Level resets when a slot empties (AC-INV-21)**: after `Discard` (full quantity), `SellItem` (full quantity), `RemoveItem`, `MoveItemOut` or `ConsumeItem` empties a slot, the slot's level is 0 and its event entry is `{ slot, itemId: 0, quantity: 0, enhancementLevel: 0 }`.
- [x] **Pickup starts at 0 (AC-INV-21)**: every slot created or topped up by `Pickup` has level 0.

**SetEnhancementLevel (AC-INV-17)**

- [x] **AC-INV-17 (success)**: slot 3 holds one Iron Sword at level 5, locked. `SetEnhancementLevel(charId, 3, 6)` returns `true`; slot 3 is level 6; one `OnInventoryChanged` with one entry `{ 3, IronSword, 1, 6 }`. The slot stays locked.
- [x] **AC-INV-17 (unlocked rejected)**: same slot unlocked → returns `false`, level stays 5, no event, server error logged.
- [x] **Rejections (AC-INV-22)**: empty slot, a stackable item (`StackLimit > 1`) at **any** quantity including 1, an over-limit `StackLimit = 1` item with `Quantity > 1`, `level > MaxLevel`, out-of-range slot index, unregistered `charId` → each returns `false`, changes nothing, fires no event, logs one server error.
- [x] **Same level is a no-op success**: `SetEnhancementLevel` with the level the slot already has returns `true` and fires no event.
- [x] **Level 0 allowed**: setting a locked level-5 item to 0 succeeds (needed for Enhancement rollback) and fires one event.

**Equipment interface (AC-INV-18)**

- [x] **AC-INV-18 (out)**: slot 3 holds one Iron Sword at level 5, unlocked. `MoveItemOut(charId, 3)` returns `{ ItemId: IronSword, EnhancementLevel: 5, Code: Success }`; slot 3 is empty at level 0. Every failure result carries `EnhancementLevel = 0`.
- [x] **AC-INV-18 (in)**: `MoveItemIn(charId, IronSword, 5)` places the sword in the lowest-index empty slot at level 5; the event entry is `{ slot, IronSword, 1, 5 }`. `ForceInsert(charId, IronSword, 5)` behaves the same on success.
- [x] **Invalid level (AC-INV-22)**: `MoveItemIn` / `ForceInsert` with `enhancementLevel > MaxLevel`, or with a non-zero level for a stackable item (`StackLimit > 1`), fail with no mutation, no event, no `OnInventoryFull`, and one server error.

**Move and swap (AC-INV-19)**

- [x] **AC-INV-19**: slot A holds an Iron Sword at level 5, slot B an Iron Sword at level 0. `Move(charId, A, B)` succeeds; A holds the level-0 sword, B the level-5 sword; one event with both entries and their levels. Two `StackLimit = 1` items never merge: they swap whenever they differ in `ItemID` or level.
- [x] **AC-INV-19 (identical items)**: two `StackLimit = 1` items with the same `ItemID` **and** the same level moved onto each other → no-op success: neither slot changes, no event; `MoveResult` echoes both unchanged slots.
- [x] **Relocate carries the level**: moving a level-5 sword onto an empty slot leaves the source empty at level 0 and the destination at level 5.
- [x] **Swap with a different item carries both levels**: a level-5 sword swapped with a potion stack ends with the sword's level in its new slot and level 0 on the stack. `MoveResult.FromSlot` / `ToSlot` echo the levels.
- [x] **Stack merge unchanged**: merging two potion stacks behaves exactly as before; both slots are level 0.

**Snapshot (AC-INV-20)**

- [x] **AC-INV-20 (round trip)**: slot 7 holds an Iron Sword at level 5 → `ExportSnapshot` entry is `{ 7, IronSword raw id, 1, 5 }` → `ImportSnapshot` into a fresh service restores slot 7 at level 5.
- [x] **AC-INV-20 (above maximum)**: entry `{ 2, IronSword, 1, 200 }` loads at level `MaxLevel`; one server warning.
- [x] **AC-INV-20 (level on a stack)**: entry `{ 4, HPPotion, 42, 3 }` loads as 42 potions at level 0; one server warning.
- [x] **AC-INV-22 (level on a stackable item at quantity 1)**: entry `{ 6, HPPotion, 1, 3 }` loads as one potion at level 0; one server warning. An over-limit `StackLimit = 1` entry `{ 0, IronSword, 2, 4 }` loads ×2 at level 0 with the over-limit warning and the level warning.
- [x] **Existing snapshot rules unchanged**: every Story 009 load rule still holds; an entry with level 0 loads with no extra warning.

**Regression**

- [x] **Existing suites pass** after their fixtures are updated for the new constructor and the `MoveItemIn` / `ForceInsert` signatures (level 0 everywhere), with no behavioural assertion weakened.
- [x] **Mutation-seam re-entrancy**: `SetEnhancementLevel` called synchronously from an `OnInventoryChanged` subscriber throws `InvalidOperationException` ("mutated synchronously") and changes nothing.

---

## Implementation Notes

- **Maximum level is injected, not hardcoded**: `InventoryService(IItemDatabase itemDatabase, Func<uint> currentTick, byte maxEnhancementLevel)`. `MAX_ENHANCEMENT_LEVEL` is an Enhancement System tuning knob (registry value 10, safe range 9–12); the Inventory System only uses it as an upper bound. All existing test fixtures gain the third argument (same pattern as Story 003's tick provider) — the nine inventory test files and the Loot Table bag-full recovery integration test (see QA Test Cases for the full file list).
- **`InventorySlot`**: add `public readonly byte EnhancementLevel`; keep the 2-argument constructor (level 0) and add `InventorySlot(ItemID, int, byte)`. **`SlotChange`**: same treatment. **`RecordSlotChange`**: add a `byte enhancementLevel` parameter (default 0 is acceptable for call sites that write stacks or empty slots); extend `ValidateSlotContents` — level must be 0 when the slot is empty or `quantity > 1`.
- **`SeedSlotForTesting`**: add an overload taking a level, validated the same way.
- **`MoveItemOutResult`**: add `EnhancementLevel`; `Succeeded(ItemID itemId, byte enhancementLevel)`; `Fail` sets 0.
- **`MoveItemIn(CharacterID, ItemID, byte enhancementLevel)` / `ForceInsert(CharacterID, ItemID, byte enhancementLevel)`**: replace the existing signatures (no overloads — the GDD contract now requires the level). Guard order: unregistered → level above maximum (server error) → item validity → non-zero level for a `StackLimit > 1` item (server error) → placement. `PlaceInLowestEmptySlot` takes the level.
- **`bool SetEnhancementLevel(CharacterID charId, int slotIndex, byte level)`** on `IInventoryService` with XML docs. Order: `ThrowIfDispatching()` → range → registered → empty → **not locked** → item not `StackLimit = 1` (look the item up with `_itemDatabase.TryGetItem`; an unresolvable item is rejected too) → `Quantity != 1` → `level > max` → same level (return `true`, no event) → record + write + emit. All rejections log `Debug.LogError` (Tier 1 caller bug) and return `false`.
- **Move (Rule 7.20)**: in `ExecuteMove`, take the merge path only when the item's `StackLimit > 1`. When `StackLimit == 1` (or the limit cannot be resolved): if both slots hold the same `ItemID` and the same level, return a no-op success with no event (identical items — decided 2026-10-01); otherwise swap. `SwapSlots` swaps whole `InventorySlot` values, so the level travels automatically; pass each slot's level to `RecordSlotChange`.
- **Discard / SellItem / ConsumeItem / Pickup**: these only ever write stacks, single level-0 items or empty slots — pass level 0. A partial decrement can only happen on a stack. Do not add category checks.
- **Snapshot**: `InventorySnapshotEntry` gains `EnhancementLevel: byte` (keep a 3-argument constructor with level 0 and add a 4-argument one). `ExportSnapshot` writes the slot's level. `ApplySnapshotEntry`: after the existing rules, `level > 0 && (StackLimit > 1 || Quantity > 1)` → warn and zero; otherwise `level > max` → warn and clamp; then load. Keep the method under 40 lines (extract a small level-normalising helper).
- **Mutation-seam contract** unchanged: `ThrowIfDispatching()` first, validate fully, then record → write → emit.
- **Performance**: one extra byte per slot and per event entry; `SetEnhancementLevel` is O(1). No new allocation.
- **Wire and persistence are not in this story**: the wire `MoveResult` / `EquipRequest` / `EquipResult` changes and the ADR-006 JSON shape are design-only until the Networking and Character Persistence stories that implement those codecs.

---

## Out of Scope

- Enhancement System logic (attempt sequence, probabilities, calling `SetEnhancementLevel` / `ConsumeItem`) — Enhancement epic
- Equipment System logic (storing the level in the gear slot, enhanced modifier registration, CR-EQS-8) — Equipment epic
- Wire codecs for `MoveResult`, `EquipRequest`, `EquipResult` (Networking) and the `inventory_slots` JSON mapping (Character Persistence)
- Inventory UI level badge (Inventory UI epic)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_EnhancementLevel_tests.cs` (new), plus fixture updates in the nine existing `InventorySystem_*_tests.cs` files and in two Loot Table test files:

- `tests/EditMode/LootTableSystem/LootTestFakes.cs` — `RecordingInventoryService` implements `IInventoryService`: add `SetEnhancementLevel` and change `MoveItemIn` / `ForceInsert` to the new signatures, all three as `throw Unsupported()` like their neighbours.
- `tests/EditMode/Integration/LootTableSystem/LootTable_BagFullRecovery_integration_tests.cs` — its `new InventoryService(database, () => 0u)` gains the third constructor argument.

*(All calls below take `charId` first; omitted for brevity. Entries are `{slot, item, quantity, level}`. `MaxLevel = 10`.)*

- **AC-INV-17 success** — Iron Sword ×1 level 5 in slot 3, `LockSlot(3)`; `SetEnhancementLevel(3, 6)` → `true`, slot level 6, one event `{3, IronSword, 1, 6}`, `IsSlotLocked(3)` still true.
- **AC-INV-17 unlocked** — same, not locked → `false`, level 5, 0 events, error logged.
- **Set rejections** — empty slot; potion ×5 locked slot; potion ×1 locked slot (stackable at quantity 1 — AC-INV-22); level 11; slot 20 and −1; unregistered character → `false`, no change, 0 events, one error each.
- **Set same level** — locked level-5 sword, `SetEnhancementLevel(3, 5)` → `true`, 0 events, no log.
- **Set to 0** — locked level-5 sword, `SetEnhancementLevel(3, 0)` → `true`, level 0, one event.
- **Set at maximum** — locked sword, `SetEnhancementLevel(3, 10)` → `true`, level 10.
- **Set re-entrancy** — subscriber calls `SetEnhancementLevel` during a pickup dispatch → `InvalidOperationException` ("mutated synchronously"); level unchanged.
- **AC-INV-18 out** — level-5 sword in slot 3; `MoveItemOut(3)` → `{IronSword, 5, Success}`; slot 3 Invalid/0/level 0; event `{3, 0, 0, 0}`. Locked slot / empty slot → failure with `EnhancementLevel == 0`.
- **AC-INV-18 in** — `MoveItemIn(IronSword, 5)` with slot 0 lowest empty → slot 0 level 5, event `{0, IronSword, 1, 5}`. `ForceInsert(IronSword, 5)` → same.
- **In with invalid level** — `MoveItemIn(IronSword, 11)`, `ForceInsert(IronSword, 11)`, `MoveItemIn(HPPotion, 3)` and `ForceInsert(HPPotion, 3)` → fail, all slots empty, 0 events, 0 full notifications, one error each. `MoveItemIn(HPPotion, 0)` still succeeds.
- **Equip round trip** — `MoveItemOut(3)` then `MoveItemIn(item, result.EnhancementLevel)` → the sword is back in the bag at level 5.
- **AC-INV-19** — sword level 5 in slot 2, sword level 0 in slot 8; `Move(2, 8)` → slot 2 level 0, slot 8 level 5; one event, entries `{2, IronSword, 1, 0}` then `{8, IronSword, 1, 5}`; `MoveResult` echoes both. Identical items (same `ItemID`, both level 0, and separately both level 5) → no-op success: 0 events, both slots unchanged, `MoveResult` echoes them.
- **Relocate** — level-5 sword slot 2 → empty slot 8: slot 2 Invalid/0/0, slot 8 level 5.
- **Swap with stack** — level-5 sword slot 2, HP Potion ×10 slot 8; `Move(2, 8)` → slot 2 potion ×10 level 0, slot 8 sword level 5.
- **Merge unchanged** — potion ×50 onto potion ×30 → ×80 and empty, both level 0.
- **Reset on empty** — level-5 sword: `Discard(slot, 1)`, `SellItem(slot, IronSword, 1)`, `RemoveItem(slot)` (locked) each leave the slot level 0 with event level 0.
- **Pickup level 0** — picking up a sword and potions → every touched slot level 0.
- **AC-INV-20 round trip** — level-5 sword in slot 7 → export entry `{7, raw id, 1, 5}` → import into a new service → slot 7 level 5, no warning.
- **AC-INV-20 clamp** — entry `{2, IronSword, 1, 200}` → slot 2 level 10, one warning naming the character and slot.
- **AC-INV-20 stack** — entry `{4, HPPotion, 42, 3}` → slot 4 ×42 level 0, one warning.
- **AC-INV-22 stackable at quantity 1** — entry `{6, HPPotion, 1, 3}` → slot 6 ×1 level 0, one warning; a later `Pickup(HPPotion, 5)` tops the stack up to ×6 at level 0.
- **AC-INV-21** — level-5 sword in slot 3, `Discard(3, 1)` → slot Invalid/0/level 0, event `{3, 0, 0, 0}`; then `Pickup(IronSword, 1)` → slot 3 level 0.
- **Clamp boundary** — entry level 10 → loads at 10, no warning; level 11 → 10, warning.
- **Regression** — all existing inventory suites pass with the updated fixtures.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_EnhancementLevel_tests.cs` — must exist and pass; the nine existing inventory test files and the Loot Table suites must still pass.

**Status**: [x] Created — 57 test methods (60 cases), passing (EditMode 1501/1501, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: Story 001 (slot, event), Story 004 (locks, `RemoveItem`), Story 005 (discard), Story 006 (move), Story 007 (Equipment interface), Story 008 (sell, consume), Story 009 (snapshot) — all Complete
- Unlocks: Enhancement System and Equipment System epics (both need the level on the inventory side)

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 23/23 passing (none deferred)
**Deviations**: None blocking. Notes: `ConsumeItem` has no level-reset test (it can only empty stacks, which are always level 0); an invalid level on `MoveItemIn` returns the same `MoveItemInResult.Failed` as an invalid item (the server error tells them apart); a same-item move with an unresolvable `StackLimit` is a no-op when the levels match (it used to swap) — as this story specifies.
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_EnhancementLevel_tests.cs` (57 test methods, 60 cases); EditMode 1501/1501 in Unity 6000.3.10f1 batch mode. Fixture-only updates in the nine existing inventory test files, `LootTestFakes.cs` and `LootTable_BagFullRecovery_integration_tests.cs`.
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all six applied (clamp warning content pinned, unresolvable-item test, seed-validation tests, `EnhancementLevel` doc wording, `TryClaimSnapshotSlot` extracted so `ApplySnapshotEntry` is under 40 lines, overload-specific doc links). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
