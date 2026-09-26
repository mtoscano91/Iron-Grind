# Story 009: InventorySnapshot Save/Load & Load-Time Validation

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 1.2 (stable slot indices), Interactions table (Character Persistence row), Persistence and Load Edge Cases, Lock State Edge Case (locks not persisted)
**Requirement**: `TR-inv-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-006: Persistence Layer (Accepted 2026-06-27) — for the storage shape only; ADR-010 for the Tier 1 call direction.
**ADR Decision Summary**: ADR-006 stores inventory in the `inventory_slots` JSONB column of `character_records` as a 20-entry array (`{"item_id": N, "count": C}` or `null`); Character Persistence translates to/from the Inventory System's `InventorySnapshot` at the load/save boundary (CR-CP-3 step 5, CR-CP-10 step 4). The Inventory System owns only the snapshot type and its export/import — not SQL or JSON.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. No file I/O or database in tests — the snapshot is an in-memory value (coding-standards: unit tests do no I/O).

**Control Manifest Rules (Core layer)**:
- Required: interface dependency (Character Persistence calls `IInventoryService`) — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-3**: Items in slots 0, 7, and 19 (ItemID/Quantity recorded). Export an `InventorySnapshot`, create a fresh inventory, import it: each item is back in slot 0, 7, 19 with the same ItemID and Quantity; every other slot is empty. No reordering.
- [ ] **Snapshot shape**: `InventorySnapshot { Slots: [{ SlotIndex: byte, ItemId: uint, Quantity: int }] }` containing **non-empty slots only**.
- [ ] **Locks not persisted**: a slot locked at export time loads unlocked, with item and quantity intact.
- [ ] **Edge — unknown ItemId** (not in Item Database): slot cleared to `ItemID.Invalid, Quantity = 0`; server warning logged with CharacterID and the unknown ItemId.
- [ ] **Edge — duplicate SlotIndex**: the first-encountered entry loads; the second is rejected; conflict logged.
- [ ] **Edge — ItemId valid but Quantity = 0**: slot cleared; warning logged.
- [ ] **Edge — Quantity < 0**: slot cleared; warning logged.
- [ ] **Load emits no gameplay event**: importing a snapshot does not fire `OnInventoryChanged` or `OnInventoryFull` (initial state, not a mutation) — **confirm at readiness**; the UI reads full slot state on bag open.

---

## Implementation Notes

- API: `ExportSnapshot(CharacterID) → InventorySnapshot` and `ImportSnapshot(CharacterID, InventorySnapshot)`. Import replaces the whole inventory (all slots start empty, then entries apply).
- Validation gaps the GDD doesn't list, handled defensively the same way (clear/reject + warning, never throw): `SlotIndex ≥ 20`; `Quantity > StackLimit` for that item. Note these as implementation-defined in the completion notes.
- Snapshot uses `ItemId: uint` (raw) — convert to/from `ItemID` at the boundary.
- Do not implement the JSONB column mapping or `SaveSession` — Character Persistence epic.

---

## Out of Scope

- `character_records.inventory_slots` JSONB mapping, Dapper/SQL, session save/load orchestration (Character Persistence)
- Enhancement System's interrupted-attempt recovery on login (Enhancement System — Lock State Edge Case)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_SnapshotSaveLoad_tests.cs`

- **AC-INV-3** — Given Bronze Sword ×1 in slot 0, HP Potion ×42 in slot 7, HP Potion ×99 in slot 19; When export → import into a new `InventorySystem`; Then slots 0/7/19 match exactly, the other 17 slots Invalid/0.
- **Non-empty only** — the exported snapshot from the case above has exactly 3 entries; an empty inventory exports 0 entries.
- **Locks not persisted** — slot 7 locked at export → after import `IsSlotLocked(7)` false, item ×42 intact.
- **Unknown ItemId** — entry `{3, 999999, 5}` → slot 3 empty, warning logged (`LogAssert.Expect` or an injected logger), other entries load.
- **Duplicate SlotIndex** — entries `{5, HPPotion, 10}` then `{5, BronzeSword, 1}` → slot 5 = HP Potion ×10.
- **Quantity 0** — `{5, HPPotion, 0}` → slot 5 empty, warning.
- **Negative quantity** — `{5, HPPotion, -3}` → slot 5 empty, warning.
- **Out-of-range index** — `{20, HPPotion, 1}` → rejected, no exception, warning.
- **No events on load** — subscriber attached before import → 0 `OnInventoryChanged` calls.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SnapshotSaveLoad_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for the not-persisted case)
- Unlocks: Character Persistence integration (future epic)
