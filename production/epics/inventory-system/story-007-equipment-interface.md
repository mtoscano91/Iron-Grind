# Story 007: Equipment System Interface (HasFreeSlot, MoveItemOut, MoveItemIn, ForceInsert)

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 8 (Unequip to Bag), Rule 4.10 (bag-full dedup), Interactions table (Equipment System row, updated 2026-05-22 per OQ-EQS-3), Full Inventory / Capacity Edge Cases. Caller contract: `design/gdd/equipment-system.md` CR-EQS-6/7/8 and the merge flow.
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

*From GDD `design/gdd/inventory-system.md`, scoped to this story. Signatures: `bool HasFreeSlot(CharacterID charId)` (exists, Story 001); `MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex)`; `MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId)`; `bool ForceInsert(CharacterID charId, ItemID itemId)`.*

- [x] **AC-INV-10 (inventory side)**: All 20 slots occupied. `HasFreeSlot()` returns `false`; the (simulated) equip is therefore not executed — all 20 slots are identical before and after (read every slot) and no `OnInventoryChanged` fires. *(Part (a), "equipped item stays equipped", is Equipment System behaviour — verified in that epic.)*
- [x] **Rule 8.23**: `HasFreeSlot()` counts only truly empty slots (`ItemID.Invalid, Quantity = 0`) — a partial consumable stack is not a free slot for equipment.
- [x] **MoveItemOut**: occupied unlocked slot → `{ ItemId, Code = Success }`, slot becomes empty, one event `{slot, 0, 0}`. Empty slot → `{ ItemID.Invalid, SlotEmpty }`. Locked slot → `{ ItemID.Invalid, SlotLocked }`. Out-of-range index or unregistered `charId` → `{ ItemID.Invalid, SlotEmpty }` + server error. A slot holding more than one unit (a consumable stack) → `{ ItemID.Invalid, SlotEmpty }` + server error, never silently removed (decided 2026-09-27 at /code-review; the result carries no quantity). No failure mutates or fires events.
- [x] **MoveItemIn**: places the item (qty 1) in the lowest-index empty slot → `{ true, index }`, one event. No empty slot → `{ false, -1 }`, no mutation, no event, **no `OnInventoryFull`**. The free-slot check happens at execution time (Edge: `HasFreeSlot` was true at query time but the bag filled before the call). Never merges into an existing slot, even with the same `ItemID`.
- [x] **ForceInsert**: identical placement → `true`, one event. Full bag → `false`, no mutation, and the shared `NotifyInventoryFull` path runs — `OnInventoryFull` fires **only outside the 600-tick dedup window** (Story 003 / GDD Rule 4.10).
- [x] **Dedup window** (decided 2026-09-27): a successful `MoveItemIn` / `ForceInsert` does **not** reset the bag-full dedup window — only a successful Pickup does (GDD Rule 4.10, literal).
- [x] **Item validation** (decided 2026-09-27): `MoveItemIn` / `ForceInsert` with `ItemID.Invalid`, an ItemID not in the Item Database, or the database not ready → `{ false, -1 }` / `false` + server error; no mutation, no event, never `OnInventoryFull`. No category check (`ForceInsert` also places Equipment merge results).
- [x] **Unregistered `charId`**: `MoveItemIn` → `{ false, -1 }`, `ForceInsert` → `false`; both log a server error; neither fires `OnInventoryFull`.
- [x] **Re-entrancy**: `MoveItemOut`, `MoveItemIn`, and `ForceInsert` called synchronously from an `OnInventoryChanged` subscriber throw `InvalidOperationException` and mutate nothing.

---

## Implementation Notes

- **Result types** (in `IronGrind.InventorySystem`, XML-documented, following `DiscardResult`'s factory style):
  - `public readonly struct MoveItemOutResult { readonly ItemID ItemId; readonly MoveItemOutCode Code; }` — `ItemId` is `ItemID.Invalid` on any failure.
  - `public enum MoveItemOutCode : byte { Success = 0, SlotEmpty = 1, SlotLocked = 2 }` — GDD member names.
  - `public readonly struct MoveItemInResult { readonly bool Success; readonly int SlotIndex; }` — `SlotIndex = -1` on failure.
- **Guard order**:
  - `MoveItemOut`: `ThrowIfDispatching()` → range → registered → empty → locked → `RecordSlotChange(slot, Invalid, 0)` → write → `EmitInventoryChanged`.
  - `MoveItemIn` / `ForceInsert`: `ThrowIfDispatching()` → registered → item valid (reuse `TryGetStackLimit(nameof(...), itemId, out _)` for the unknown-item / DB-not-ready error log) → lowest empty slot → `RecordSlotChange(slot, itemId, 1)` → write → `EmitInventoryChanged`.
- **Shared placement**: a single private helper finds the lowest empty slot and performs the record/write/emit for both `MoveItemIn` and `ForceInsert`. `ForceInsert` differs only by calling `NotifyInventoryFull(charId)` when no slot is free. Neither touches `_bagFullWindowExpiry` on success.
- Equipment items (StackLimit = 1) are always placed at quantity 1 into an empty slot — never merged.
- **Logging**: these are Tier 1 server-only calls (never wire-reachable), so out-of-range, unregistered, and unknown-item inputs are caller bugs → server error. Locked / empty `MoveItemOut` log nothing (the Equipment System can legitimately hit a locked slot). A full bag logs nothing (reported via result / `OnInventoryFull`).
- **Performance**: at most one O(20) slot scan; no allocation beyond the existing pending-change buffer. Not on a per-frame path.
- **Informational (Equipment epic)**: `MoveItemIn` and `ForceInsert` share placement logic and the server is single-threaded, so equipment-system.md CR-EQS-8's `ForceInsert` immediately after a same-tick `MoveItemIn` failure will also fail. Not changed here — flag for the Equipment System epic.

---

## Out of Scope

- Equipment System's equip/unequip orchestration, error text ("Inventory full — free a slot before equipping"), and equipped-item retention (Equipment System epic)
- `InventoryFullNotification` wire message (Networking)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_EquipmentInterface_tests.cs`

*(All calls take `charId` first; omitted for brevity. Every success asserts the success code/flag; every failure asserts the specific failure value.)*

- **AC-INV-10** — Given 20 occupied slots (snapshot all 20); When the test (as Equipment System) calls `HasFreeSlot()` → false and therefore does not call `MoveItemIn`; Then all 20 slots equal the snapshot, 0 events. Edge: 19 occupied incl. a partial HP Potion stack at 50/99 and 1 empty → `HasFreeSlot` true; 20 occupied incl. a partial stack → false.
- **MoveItemOut** — Bronze Sword slot 5 → `Success`, `ItemId == BronzeSword`, slot 5 empty, event `{5, 0, 0}`; empty slot 6 → `SlotEmpty`, `ItemId == Invalid`; locked slot 5 → `SlotLocked`, `ItemId == Invalid`, slot unchanged, 0 events. Out-of-range −1 and 20, and unregistered `charId` → `SlotEmpty`, error via `LogAssert.Expect`, 0 events.
- **MoveItemIn** — empties at 3 and 8 → returns `{true, 3}`, event `{3, BronzeSword, 1}`; full bag → `{false, -1}`, no mutation, 0 `OnInventoryChanged`, 0 `OnInventoryFull`.
- **Execution-time check** — `HasFreeSlot()` true with one empty slot; fill it via `Pickup`; then `MoveItemIn` → `{false, -1}` (item not silently lost — the caller keeps it).
- **ForceInsert** — one empty slot → `true`, placed there, one event; full bag → `false`, no mutation, `OnInventoryFull` fired once. Dedup: a second full-bag `ForceInsert` within the window → no further `OnInventoryFull`; a failed `Pickup` shares the same window; a successful `ForceInsert` in between does not reset it (free a slot via `RemoveItem`, `ForceInsert` succeeds, fill again, full-bag `ForceInsert` within the original window → no `OnInventoryFull`). Advance the injected tick source past the window → fires again.
- **No merge** — Bronze Sword in slot 0, `MoveItemIn(BronzeSword)` → lands in slot 1, slot 0 still qty 1. Same for `ForceInsert`.
- **Item validation** — `ItemID.Invalid`, an ID not in the stub database, and the stub database with `IsReady = false`, for both `MoveItemIn` and `ForceInsert` → `{false, -1}` / `false`, error via `LogAssert.Expect`, no mutation, 0 events, 0 `OnInventoryFull`.
- **Unregistered `charId`** — `MoveItemIn` / `ForceInsert` → failure, error logged, 0 `OnInventoryFull`.
- **Re-entrancy** — one test per mutator: subscriber on `OnInventoryChanged` calls it → `InvalidOperationException`; target slots unchanged.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_EquipmentInterface_tests.cs` — must exist and pass

**Status**: [x] Created — 35 tests, all passing in live Test Runner (2026-09-27)

---

## Dependencies

- Depends on: Story 001; Story 003 (`OnInventoryFull` + `NotifyInventoryFull` dedup for `ForceInsert`); Story 004 (`SlotLocked` for `MoveItemOut`); Story 006 (`TryGetStackLimit` caller-named log prefix)
- Unlocks: Equipment System epic (not yet created)

---

## Completion Notes
**Completed**: 2026-09-27
**Criteria**: 9/9 passing (none deferred)
**Deviations**: None. Readiness decisions (2026-09-27): successful `MoveItemIn`/`ForceInsert` do not reset the bag-full dedup window; unknown / invalid / DB-not-ready items rejected with a server error. Code-review decision (2026-09-27): `MoveItemOut` rejects slots holding more than one unit (`SlotEmpty` + error). No GDD change needed. CR-EQS-8 same-tick `ForceInsert` retry logged as TD-044. TR registry has no `TR-inv-*` entries yet (pre-existing).
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_EquipmentInterface_tests.cs` (35 tests, passing in live Test Runner)
**Code Review**: Complete — /code-review CHANGES REQUIRED (swapped MoveItemOut/MoveItemIn doc summaries); all fixes and suggestions applied (+7 tests, `PlaceInLowestEmptySlot` rename, `UnityEngine.Debug` qualification). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
