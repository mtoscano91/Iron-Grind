# Story 006: Slot Move — Merge, Swap & Relocate

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 7 (Slot Move), Discard and Move Edge Cases
**Requirement**: `TR-inv-002`, `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (`OnInventoryChanged` emission). Move rules: none — design-only, LOW risk.
**ADR Decision Summary**: The `MoveRequest` network handler calls `IInventoryService.Move(...)` directly (Tier 1) and maps the result to `MoveResult`; both changed slots are broadcast in one `OnInventoryChanged`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-8**: Slot A holds 50 HP Potions, slot B holds 30 (same ItemID, StackLimit=99). `Move(A, B)` → B = 80, A = `ItemID.Invalid, Quantity = 0`; both changes in a single `OnInventoryChanged`.
- [ ] **AC-INV-14**: A holds 70, B holds 60 (same ItemID, StackLimit=99). `Move(A, B)` → B = 99, A = 31 (overflow stays in source); one event with both slots.
- [ ] **AC-INV-9**: A locked, B unlocked. `Move(A, B)` → `MoveResult(fail, reason = LockedSlot)`; both slots unchanged; no event. Same when B is the locked one.
- [ ] **Rule 7.20 equipment swap**: moving an equipment item onto an occupied slot swaps the two slots' contents; one event with both slots.
- [ ] **Relocate to empty**: moving any item onto an empty slot moves it (source becomes empty).
- [ ] **Edge — same source and destination**: no-op, returns success, no mutation, no event.
- [ ] **Validation**: out-of-range index or empty source → failure, no mutation, no event.

---

## Implementation Notes

- Result type mirrors the `MoveResult` / `MoveFailReason` definitions in `design/gdd/networking-wire-protocol.md` (Inventory System Messages section, OQ-INV-6). The wire `MoveResult` is 20 bytes and carries updated slot states — the service result should expose both slots' post-move `(ItemID, Quantity)` so the network handler can serialize it without re-reading.
- Merge = same consumable `ItemID` on both slots and StackLimit > 1: transfer `min(A.Qty, StackLimit − B.Qty)`; if B is already full (99/99), treat as a no-op success (nothing to transfer) — **confirm at readiness**.
- **Open design question (confirm at `/story-readiness`):** Rule 7 does not specify moving a consumable onto a slot holding a *different* item (or a consumable onto equipment and vice versa). Proposed default: **swap**, matching the equipment rule, so every occupied→occupied non-merge move behaves the same. If approved, add one line to GDD Rule 7.20 as a propagation fix.

---

## Out of Scope

- `MoveRequest`/`MoveResult` wire codecs (Networking)
- Client-side sort (display-only reorder, Inventory UI — never moves server slots)
- Moves between inventory and equipment slots (Story 007)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_MoveMergeSwap_tests.cs`

- **AC-INV-8** — Given A=slot 2 ×50, B=slot 9 ×30 HP Potion; When `Move(2, 9)`; Then slot 9 ×80, slot 2 Invalid/0, exactly one event containing both `{9, HPPotion, 80}` and `{2, 0, 0}`.
- **AC-INV-14** — Given ×70 / ×60; When `Move(2, 9)`; Then slot 9 ×99, slot 2 ×31, one event with both. Edge: ×99 onto ×99 → no transfer (per readiness decision).
- **AC-INV-9** — Given slot 2 locked; `Move(2, 9)` → fail `LockedSlot`, both unchanged, 0 events. Edge: `Move(9, 2)` with slot 2 locked → same result.
- **Equipment swap** — Bronze Sword slot 0, Iron Sword slot 1; `Move(0, 1)` → slot 0 Iron Sword, slot 1 Bronze Sword, one event with both.
- **Relocate** — HP Potion ×10 slot 4, slot 12 empty; `Move(4, 12)` → slot 12 ×10, slot 4 empty.
- **Same slot** — `Move(4, 4)` → success, no event, slot unchanged.
- **Validation** — `Move(-1, 3)`, `Move(3, 20)`, empty source `Move(15, 3)` → fail, no exception, no event.
- **Different-item consumables** — per the readiness decision (default: swap).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_MoveMergeSwap_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for AC-INV-9)
- Unlocks: None
