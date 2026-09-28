# Story 006: Slot Move — Merge, Swap & Relocate

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 7 (Slot Move), Discard and Move Edge Cases
**Requirement**: `TR-inv-002`, `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (`OnInventoryChanged` emission). Move rules: none — design-only, LOW risk.
**ADR Decision Summary**: The `MoveRequest` network handler enqueues the request; the zone tick loop processes it (ADR-010 Decision 5), calling `IInventoryService.Move(...)` directly (Tier 1) and mapping the result to the wire `MoveResult`. On success both changed slots are broadcast in one `OnInventoryChanged`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story. Signature: `MoveResult Move(CharacterID charId, int fromSlot, int toSlot)`.*

- [x] **AC-INV-8**: Slot A holds 50 HP Potions, slot B holds 30 (same ItemID, StackLimit=99). `Move(charId, A, B)` → B = 80, A = `ItemID.Invalid, Quantity = 0`; both changes in a single `OnInventoryChanged`.
- [x] **AC-INV-14**: A holds 70, B holds 60 (same ItemID, StackLimit=99). `Move(charId, A, B)` → B = 99, A = 31 (overflow stays in source); one event with both slots.
- [x] **Full-destination merge** (decided 2026-09-27): same `ItemID` with B already at `StackLimit` → success, nothing transferred, no event; the result echoes both unchanged slots. Covers two identical equipment items (StackLimit 1).
- [x] **AC-INV-9**: A locked, B unlocked → `Success = false, Reason = SourceLocked`. B locked, A unlocked → `DestLocked`. Both slots unchanged; no event.
- [x] **Rule 7.20 swap**: occupied source onto an occupied destination holding a *different* `ItemID` swaps the two slots' contents, any item category (decided 2026-09-27); one event with both slots.
- [x] **Relocate to empty**: occupied source onto an empty destination moves the whole stack; source becomes empty; one event with both slots.
- [x] **Same slot**: `fromSlot == toSlot` → success, no mutation, no event (even if that slot is empty or locked — wire GDD).
- [x] **Validation**: either index out of range → `InvalidSlot` + server warning; unregistered `charId` → `InvalidSlot` + server error; empty source → `InvalidSlot`, no log (decided 2026-09-27). None throw, mutate, or fire events.
- [x] **Re-entrancy**: `Move` called synchronously from an `OnInventoryChanged` subscriber throws `InvalidOperationException`; neither slot changes.
- [x] **Result contract**: every success has `Reason = None`, every failure a non-`None` reason; the result always carries both slots' post-operation `InventorySlot` state (unchanged states on failure; `Invalid/0` when an index is out of range or `charId` is unregistered).

---

## Implementation Notes

- **Signature**: `MoveResult Move(CharacterID charId, int fromSlot, int toSlot)` on `IInventoryService` (with XML docs), matching every other mutation's `charId`-first shape.
- **Result type**: `public readonly struct MoveResult { readonly bool Success; readonly MoveFailReason Reason; readonly InventorySlot FromSlot; readonly InventorySlot ToSlot; }` in `IronGrind.InventorySystem`, following `DiscardResult`: `static MoveResult Fail(MoveFailReason reason, InventorySlot from, InventorySlot to)` with `UnityEngine.Debug.Assert(reason != MoveFailReason.None)`, plus a success factory taking both post-move slots. The wire `MoveResult` echoes both slots' post-operation states on success *and* failure, so the network handler serializes this without re-reading.
- **Naming note**: the future Networking message struct must not reuse the name `MoveResult` (use e.g. `MoveResultMessage`) — out of scope here.
- **Fail enum**: `public enum MoveFailReason : byte { None = 0, SourceLocked = 1, DestLocked = 2, InvalidSlot = 3 }` — mirrors `design/gdd/networking-wire-protocol.md` member-for-member and value-for-value. Do not add members.
- **Validation order**: `ThrowIfDispatching()` → both indices in range → `charId` registered → same slot (no-op success) → source empty → source locked → destination locked → operation. Matches the wire GDD's order (range → source lock → dest lock). Empty-before-lock is observationally identical: an empty slot can never be locked.
- **Operation selection** (after validation):
  - Same `ItemID` → **merge**: `transfer = min(A.Qty, StackLimit − B.Qty)`; `transfer == 0` → no-op success (no record, no event). `StackLimit` comes from the Item Database; if unavailable, log an error and fall back to swap (unreachable under the Pickup guard).
  - Destination empty → **relocate**.
  - Otherwise → **swap**.
- **Mutation-seam contract**: all validation and the no-op checks complete before any `RecordSlotChange`. Record the source entry first, then the destination entry; write both slots; then one `EmitInventoryChanged(charId)`. Failure/no-op paths never record, so they never need `DiscardPendingChanges()`.
- **Logging**: client-caused rejections (locked, empty source) log nothing. Out-of-range index → server warning (wire slot fields are `byte`, so 20–255 is reachable); unregistered `charId` → server error.
- **Performance**: O(1) — two slot reads, two slot writes, no allocation beyond the existing pending-change buffer. Not on a per-frame path.

---

## Out of Scope

- `MoveRequest`/`MoveResult` wire codecs (Networking)
- Client-side sort (display-only reorder, Inventory UI — never moves server slots)
- Moves between inventory and equipment slots (Story 007)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_MoveMergeSwap_tests.cs`

*(All calls take `charId` first; omitted for brevity. Event entries are located by slot index — tests must not depend on entry order.)*

- **AC-INV-8** — Given A=slot 2 ×50, B=slot 9 ×30 HP Potion; When `Move(2, 9)`; Then slot 9 ×80, slot 2 Invalid/0, exactly one event containing both `{9, HPPotion, 80}` and `{2, 0, 0}`; result carries the same two states.
- **AC-INV-14** — Given ×70 / ×60; When `Move(2, 9)`; Then slot 9 ×99, slot 2 ×31, one event with both.
- **Full-destination merge** — ×99 onto ×99 → success, both unchanged, 0 events. Edge: Bronze Sword onto Bronze Sword → success, 0 events.
- **AC-INV-9** — Given slot 2 locked; `Move(2, 9)` → `SourceLocked`, both unchanged, 0 events. `Move(9, 2)` with slot 2 locked → `DestLocked`, same. Each failure result echoes both unchanged slots.
- **Equipment swap** — Bronze Sword slot 0, Iron Sword slot 1; `Move(0, 1)` → slot 0 Iron Sword, slot 1 Bronze Sword, one event with both.
- **Different-item swap** — HP Potion ×10 slot 4, Bronze Sword slot 5; `Move(4, 5)` → slot 4 Bronze Sword, slot 5 HP Potion ×10, one event with both.
- **Relocate** — HP Potion ×10 slot 4, slot 12 empty; `Move(4, 12)` → slot 12 ×10, slot 4 empty, one event with both.
- **Same slot** — `Move(4, 4)` → success, no event, slot unchanged; also for an empty slot and a locked slot.
- **Validation** — `Move(-1, 3)`, `Move(3, 20)` → `InvalidSlot`, warning (`LogAssert.Expect`), result slots Invalid/0, all 20 slots unchanged; empty source `Move(15, 3)` → `InvalidSlot`, no log; unregistered `charId` → `InvalidSlot`, error logged. No exception, no event in any case.
- **Re-entrancy** — subscriber on `OnInventoryChanged` calls `Move` on two occupied slots → `InvalidOperationException`; both slots unchanged.
- **Result contract** — every success asserts `Reason == None`; every failure asserts the specific non-`None` reason.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_MoveMergeSwap_tests.cs` — must exist and pass

**Status**: [x] Created — 24 tests, all passing in live Test Runner (2026-09-27)

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for AC-INV-9)
- Unlocks: None

---

## Completion Notes
**Completed**: 2026-09-27
**Criteria**: 10/10 passing (none deferred)
**Deviations**: None. Readiness decisions (2026-09-27) — different-item swap, full-destination no-op, empty source → `InvalidSlot` — propagated to GDD Rule 7.20 and AC-INV-9. TR registry has no `TR-inv-*` entries yet (pre-existing).
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_MoveMergeSwap_tests.cs` (24 tests, passing in live Test Runner)
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all applied (`TryGetStackLimit` caller-named log prefix, +6 tests). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
