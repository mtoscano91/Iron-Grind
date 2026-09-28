# Story 005: Discard (Server-Side Validation & Mutation)

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2.5 hours

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

*From GDD `design/gdd/inventory-system.md`, scoped to this story. Signature: `DiscardResult Discard(CharacterID charId, int slotIndex, int quantity)`.*

- [x] **AC-INV-4**: Slot locked by the Enhancement System; `Discard(charId, slot, 1)` returns `Success = false, Reason = SlotLocked`; no `OnInventoryChanged`; ItemID and Quantity unchanged.
- [x] **AC-INV-7a**: Slot holds 45 HP Potions, unlocked; `Discard(charId, slot, 20)` returns success; slot holds exactly 25; one `OnInventoryChanged` with one entry `{ slotIndex, itemId: HPPotion, quantity: 25 }`.
- [x] **AC-INV-7b**: Slot holds 45 HP Potions; `Discard(charId, slot, 45)` returns success; slot becomes `ItemID.Invalid, Quantity = 0`; one event entry `{ slotIndex, itemId: 0, quantity: 0 }`.
- [x] **AC-INV-7c**: Slot holds 45 HP Potions; `Discard(charId, slot, 100)` returns `Success = false, Reason = InvalidQuantity`; no event; slot still 45.
- [x] **Rule 6.18 validation**: `quantity ≤ 0` → `InvalidQuantity`; empty slot → `SlotEmpty`; out-of-range slot index (< 0 or ≥ 20) → `SlotEmpty` + server warning; unregistered `charId` → `SlotEmpty` + server error; none throw, mutate state, or fire events.
- [x] **Rule 6.17 equipment**: discarding an equipment item (qty 1) empties the slot.
- [x] **Mutation-seam re-entrancy**: `Discard` called synchronously from an `OnInventoryChanged` subscriber throws `InvalidOperationException` and does not mutate the slot.
- [x] **Result contract**: every success returns `Reason = None`; every failure returns a non-`None` reason.

---

## Implementation Notes

- **Signature**: `DiscardResult Discard(CharacterID charId, int slotIndex, int quantity)` on `IInventoryService` (with XML docs), matching every other mutation's `charId`-first shape.
- **Result type**: `public readonly struct DiscardResult { readonly bool Success; readonly DiscardFailReason Reason; }` in `IronGrind.InventorySystem`, mirroring `PickupResult` exactly: `static readonly DiscardResult Succeeded`, `static DiscardResult Fail(DiscardFailReason reason)` with `UnityEngine.Debug.Assert(reason != DiscardFailReason.None)`.
- **Fail enum**: `public enum DiscardFailReason : byte { None = 0, SlotLocked = 1, InvalidQuantity = 2, SlotEmpty = 3 }` — mirrors `design/gdd/networking-wire-protocol.md` (line ~1375) member-for-member and value-for-value so the future wire codec maps 1:1. Do not add members.
- **Out-of-range / unregistered mapping (decided 2026-09-27, option A)**: the wire enum has no invalid-slot value. Out-of-range `slotIndex` and unregistered `charId` both return `SlotEmpty` — a slot that does not exist holds no item. No GDD change. The wire `slotIndex` is a `byte`, so 20–255 is reachable by a buggy/malicious client; the guard is required, not defensive-only.
- **Naming note**: the future Networking message struct must not reuse the name `DiscardResult` (use e.g. `DiscardResultMessage`) — out of scope here, recorded for the Networking story.
- **Validation order** (matches wire GDD `DiscardRequest` validation order): range → registered → empty → lock → quantity bounds (`0 < quantity ≤ current Quantity`). Empty-before-lock vs lock-before-empty is observationally identical: `LockSlot` refuses empty slots and `RemoveItem` clears the lock, so an empty locked slot cannot exist. AC-INV-4 still holds (occupied locked slot → `SlotLocked` even for a valid quantity).
- **Mutation-seam contract** (Story 001, class remarks): `ThrowIfDispatching()` first → all validation → `RecordSlotChange(charId, slot, newItem, newQty)` → write slot → `EmitInventoryChanged(charId)`. No `RecordSlotChange` before validation completes, so failure paths never need `DiscardPendingChanges()`.
- **Logging**: client-caused rejections (`SlotLocked`, `SlotEmpty` on an in-range empty slot, `InvalidQuantity`) log nothing — they are client-triggerable and must not spam server logs. Out-of-range `slotIndex` → server warning; unregistered `charId` → server error (mirrors `RemoveItem`'s guards).
- **Performance**: O(1) — one slot read, one slot write, no allocation beyond the existing single-entry pending-change buffer. Not on a per-frame path.
- The hold-to-confirm gesture and quantity selector (Rule 6.16, OQ-INV-1 `DISCARD_HOLD_DURATION`) are client UI — not here.
- Same-tick `DiscardRequest` vs `LockSlot` race (Edge Cases): resolved by FIFO call order — no special handling; covered by a sequential-order test.

---

## Out of Scope

- `DiscardRequest`/`DiscardResult` wire codecs (Networking)
- Discard gesture, quantity selector, hold duration (Inventory UI)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_Discard_tests.cs`

*(All calls below take `charId` first; omitted for brevity.)*

- **AC-INV-4** — Given HP Potion ×45 in slot 6, `LockSlot(6)`; When `Discard(6, 1)`; Then `Success=false`, `Reason=SlotLocked`, slot 6 still ×45, 0 events.
- **AC-INV-7a** — Given ×45 in slot 6; When `Discard(6, 20)`; Then success, slot ×25, one event with one entry `{6, HPPotion, 25}`.
- **AC-INV-7b** — Given ×45; When `Discard(6, 45)`; Then success, slot Invalid/0, event `{6, 0, 0}`. Edge: subsequent `HasItem(HPPotion)` false.
- **AC-INV-7c** — Given ×45; When `Discard(6, 100)`; Then `InvalidQuantity`, slot ×45, 0 events. Edge: `Discard(6, 46)` also `InvalidQuantity`; `Discard(6, 0)` and `Discard(6, -1)` → `InvalidQuantity`.
- **Empty / out-of-range / unregistered** — `Discard(10, 1)` on empty slot → `SlotEmpty`, no event, no log; `Discard(20, 1)`, `Discard(-1, 1)` → `SlotEmpty`, no exception, warning logged (`LogAssert.Expect`), all 20 slots unchanged; unregistered `charId` → `SlotEmpty`, error logged, no event.
- **Re-entrancy** — subscriber on `OnInventoryChanged` calls `Discard` on another occupied slot → `InvalidOperationException`; that slot unchanged.
- **Result contract** — every success case asserts `Reason == None`; every failure case asserts the specific non-`None` reason.
- **Equipment** — Bronze Sword in slot 0, `Discard(0, 1)` → slot empty, event `{0, 0, 0}`.
- **FIFO race** — `Discard(6, 45)` then `LockSlot(6)` → discard succeeds, lock is a no-op (slot empty, unlocked; `LockSlot`'s empty-slot warning expected via `LogAssert.Expect`); reverse order → discard rejected `SlotLocked`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_Discard_tests.cs` — must exist and pass

**Status**: [x] Created — 19 tests, all passing in live Test Runner (2026-09-27)

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for AC-INV-4)
- Unlocks: None

---

## Completion Notes
**Completed**: 2026-09-27
**Criteria**: 8/8 passing (none deferred)
**Deviations**: None. TR registry has no `TR-inv-*` entries yet (pre-existing, noted in Context); verified against story ACs + wire GDD `DiscardFailReason`.
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_Discard_tests.cs` (19 tests, passing in live Test Runner)
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all suggestions applied (ADR-010 Decision 5 doc wording on `IInventoryService.Discard`, +6 tests, +1 no-event assertion). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
