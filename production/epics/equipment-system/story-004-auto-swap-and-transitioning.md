# Story 004: Auto-Swap, Rollback and the Transitioning Guard

> **Epic**: Equipment System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-6 (auto-swap), CR-EQS-8 (`MoveItemIn` failure recovery), CR-EQS-9 (Transitioning guard), CR-EQS-15 (stat transaction API decision), EC-EQS-6, EC-EQS-7, EC-EQS-10
**Requirement**: `TR-equip-005`, `TR-equip-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Tier 1 calls acting on return values; `OnStatChanged` is the Character Stats broadcast event and fires per modifier change — no transaction batching here.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (this layer)**:
- Required: Tier 1 calls; if subscriber order matters, use explicit Tier 1 ordering (ADR-010)
- Forbidden: `async` in tick-driven core code (ADR-011)
- Guardrail: no item is ever lost or duplicated on any failure path

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md`, scoped to this story:*

- [x] **AC-EQS-2** — Iron Sword (+25 STR) equipped, Steel Sword (+35, min 55) in the bag, base STR 60 → swap: net STR +10; ItemA in the bag; Weapon `(ItemB, Occupied)`; ItemB gone from the bag
- [x] **AC-EQS-10** — after a successful swap `IsSlotTransitioning(Weapon)` = false; during the swap (observed from an `AddEquipmentModifier` / `OnStatChanged` spy) it is true
- [x] **AC-EQS-16** — `MoveItemIn` fails at step 6 → `InventoryError`; slot `(ItemA, Occupied)` at level 3; effective stats equal the pre-call value (ItemA's enhanced modifiers re-added exactly once); not transitioning; ItemB still in slot S; `ForceInsert` called 0 times; a server error log containing old item id and entity id
- [x] **AC-EQS-28** — Iron Sword +0 equipped, Iron Sword +5 in slot S → `Success` (not `NoChange`); Weapon holds the +5; the +0 is in the bag at level 0; effective stats reflect only the +5's enhanced modifiers
- [x] **AC-EQS-31** — step 7 `MoveItemOut(S)` returns `SlotLocked` after step 6 placed ItemA in free slot F → `ItemLocked`; Weapon `(ItemA, Occupied)` level 3; stats equal pre-call; not transitioning; F empty again (no duplicate); ItemB still in S. `SlotEmpty` at step 7 → `ItemNotInInventory`, same restoration
- [x] CR-EQS-6 step 3 — swap with a full bag → `InventoryFull`, before any mutation (no flag set, no modifier removed)
- [x] CR-EQS-15 — no `BeginStatTransaction` / `EndStatTransaction` call; a successful swap of a 1-modifier item fires `OnStatChanged` twice (remove, then add)

---

## Implementation Notes

*Derived from the GDD rules:*

- Occupied branch of `Equip`, after story 003's guards (same-item guard and stat gate are CR-EQS-6 steps 1–2). Then exactly:
  3. `HasFreeSlot` else `InventoryFull`.
  4. Slot entry → `IsTransitioning = true` (a new `readonly struct` value written to the array).
  5. `RemoveEquipmentModifier(entityId, mod.StatId, oldItemId)` per old modifier.
  6. `MoveItemIn(oldItemId, entry.EnhancementLevel)`; fail → **CR-EQS-8**: re-add old enhanced modifiers, clear flag, `Debug.LogError("Auto-swap aborted: old item {oldItemID} could not return to inventory. Entity {entityID}.")`, return `InventoryError`. **Never** call `ForceInsert` here. Success → keep `result.SlotIndex` as `oldItemReturnedSlotIndex`.
  7. `MoveItemOut(newItemSlotIndex)`; fail → `MoveItemOut(oldItemReturnedSlotIndex)` to reclaim (its level equals the old level), re-add old enhanced modifiers, clear flag, return `ItemLocked` / `ItemNotInInventory` by the failed code.
  8. Add new enhanced modifiers at the new item's `MoveItemOutResult.EnhancementLevel` (story 002's helper).
  9. Write the new entry, flag false.
- Remove the temporary `NotSupportedException` story 002 left on the occupied branch.
- The reclaim `MoveItemOut` in step 7 should not fail on a single-threaded tick; if it does, log a CriticalError naming both items and return `InventoryError` with the flag cleared — the GDD has no rule for this, so keep it to a log and document it.
- Appearance recompute (CR-EQS-6 step 10) is wired in story 005.

---

## Out of Scope

- Story 005: appearance flag recompute after a swap
- Story 007: crash-window behaviour on reload (CR-EQS-15 points 1–3)

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_AutoSwap_tests.cs` (new). Inventory decorator to fail `MoveItemIn` or a chosen `MoveItemOut` and to count `ForceInsert`.

- **Read surface, Transitioning (carried over from Story 001 `/code-review` 2026-10-26)** — while a slot's `IsTransitioning` is true (observed from inside a decorated collaborator call mid-swap), `GetEquipmentSlotState` = `(old item, Transitioning)` and `IsSlotTransitioning` = true; the two reads always agree (`GetEquipmentSlotState` checks the flag before emptiness).

- **AC-EQS-2** — Given base STR 60, ItemA (+25) equipped, ItemB (+35, min 55) in S; When `Equip(Weapon, B, S)`; Then `Success`, STR delta +10, ItemA in the bag at level 0, Weapon `(ItemB, Occupied)`, S empty.
- **AC-EQS-10** — spy on `OnStatChanged`: on the add-phase callback `IsSlotTransitioning(Weapon)` = true; after return false; result `Success`, Weapon `(ItemB, Occupied)`.
- **AC-EQS-16** — ItemA level 3 equipped, ItemB in S, `MoveItemIn` fails: `InventoryError`, Weapon `(ItemA, Occupied)` level 3, effective stats equal the recorded pre-call values, flag false, S holds ItemB, `ForceInsert` count 0, `LogAssert.Expect` an error containing ItemA's id and the entity id.
- **AC-EQS-28** — +0 sword equipped, +5 sword in S, a free slot → `Success`, Weapon level 5, the +0 in the bag at level 0, STR = base + enhanced(+5) only.
- **AC-EQS-31** — exactly one free slot F; `MoveItemOut(S)` returns `SlotLocked`: `ItemLocked`, Weapon `(ItemA)` level 3, stats unchanged, flag false, F empty, S holds ItemB. Repeat with `SlotEmpty` → `ItemNotInInventory`.
- **Full bag** — occupied Weapon, all 20 bag slots filled (ItemB is one of them): `InventoryFull`, 0 stat events, flag never set (spy).
- **No transaction / two events** — 1-modifier items: exactly 2 `OnStatChanged` for that stat on a successful swap; transaction methods never called (only checkable by event count — the two events must not be collapsed).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EquipmentSystem/EquipmentSystem_AutoSwap_tests.cs` — must exist and pass

**Status**: [x] Created — 13 tests (11 `[Test]` + 2 `[TestCase]`), all passing (EditMode 4234 / 0 failed, 2026-10-26)

---

## Dependencies

- Depends on: Stories 002, 003 (the same-item guard and stat gate are swap steps 1–2; AC-EQS-28 needs the level comparison)
- Unlocks: Stories 005, 007

---

## Completion Notes

**Completed**: 2026-10-26
**Criteria**: 7/7 passing, all covered by `EquipmentSystem_AutoSwap_tests.cs` (no deferred items)
**Deviations** (advisory, accepted):
1. Old item's definition missing from the Item Database during a swap: the swap is refused before any mutation (`InventoryError`, one server error naming the item and entity; Unequip clears such a slot per CR-EQS-7 step 2). No GDD rule; the Inventory rejects an unknown item in `MoveItemIn`, so the swap could never complete.
2. Step 7 reclaim `MoveItemOut` also fails (user decision at `/code-review`, option A): the gear slot is left Empty, the old item stays in the bag with its modifiers removed, a `CRITICAL:` server error names both items, both bag slots, the failed code and the entity, and the result is `InventoryError` — no item lost or duplicated. The story note asked for log + flag cleared + `InventoryError`; leaving the slot Empty is new. CR-EQS-6 has no rule for this path — candidate GDD note.
3. "CriticalError" is `Debug.LogError` with a `CRITICAL:` prefix (no separate critical log API; matches the file).
**Scope**: `EquipmentSystem_TestDoubles.cs` (`InventoryDecorator`: per-slot `MoveItemOut` failure, `ForceInsertCalls`, `BeforeMoveItemIn` / `BeforeMoveItemOut` hooks); the two Story 002/003 tests that expected `NotSupportedException` rewritten as swap assertions (`EquipUnequip_tests`, `Validation_tests`). No source outside `src/ServerLogic/EquipmentSystem/`; appearance flags untouched (Story 005).
**Test Evidence**: Logic — `tests/EditMode/EquipmentSystem/EquipmentSystem_AutoSwap_tests.cs`
**Code Review**: Complete — 2026-10-26, CHANGES REQUIRED (missing-definition branch unreachable as written, reclaim-failure duplicate, `Swap` length) → "fix all, option A" applied (`Swap` / `CompleteSwap` / `RollBackSwap`, two-stat test item, 3 more tests). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
