# Story 009: InventorySnapshot Save/Load & Load-Time Validation

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3.5 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 1.2 (stable slot indices), Interactions table (Character Persistence row), Persistence and Load Edge Cases, Lock State Edge Case (locks not persisted), AC-INV-3
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

*From GDD `design/gdd/inventory-system.md`, scoped to this story. Signatures: `InventorySnapshot ExportSnapshot(CharacterID charId)`, `bool ImportSnapshot(CharacterID charId, InventorySnapshot snapshot)`, `void UnregisterCharacter(CharacterID charId)`.*

**Export**

- [x] **Snapshot shape**: `InventorySnapshot { Slots: [{ SlotIndex: byte, ItemId: uint, Quantity: int }] }` containing **non-empty slots only**, in ascending `SlotIndex` order. An empty inventory exports 0 entries. Lock flags are not part of the snapshot.
- [x] **Export is a read**: `ExportSnapshot` mutates nothing and fires no event. An unregistered `charId` logs a server error and returns a snapshot with 0 entries.

**Import**

- [x] **AC-INV-3**: Items in slots 0, 7, and 19 (ItemID/Quantity recorded). Export an `InventorySnapshot`, import it into a fresh `InventoryService`: `ImportSnapshot` returns `true`; each item is back in slot 0, 7, 19 with the same ItemID and Quantity; every other slot is empty. No reordering.
- [x] **Import replaces and registers**: `ImportSnapshot` performs the same reset as `RegisterCharacter` (registers `charId` if new; clears all 20 slots, all locks and the bag-full dedup window) and then applies the entries. Contents present before the import are gone unless the snapshot contains them. It works on a character that was never registered.
- [x] **Locks not persisted**: a slot locked at export time loads unlocked, with item and quantity intact.
- [x] **Load emits no event** (confirmed 2026-10-01): `ImportSnapshot` fires neither `OnInventoryChanged` nor `OnInventoryFull` — it is initial state, not a mutation; the UI reads full slot state on bag open.
- [x] **Edge — unknown ItemId** (not in Item Database): slot left empty (`ItemID.Invalid, Quantity = 0`); server warning logged with the CharacterID and the unknown ItemId; other entries still load.
- [x] **Edge — duplicate SlotIndex**: the first-encountered entry for a slot claims it — even if that entry was itself cleared by validation; every later entry for the same slot is rejected with a server warning.
- [x] **Edge — Quantity ≤ 0** (`Quantity = 0` or `Quantity < 0` with a valid ItemId): slot left empty; server warning logged.
- [x] **Edge — SlotIndex ≥ 20** (implementation-defined, not in GDD): entry rejected; server warning; no exception.
- [x] **Edge — ItemId = 0** (`ItemID.Invalid`; implementation-defined): slot left empty; server warning (a snapshot holds non-empty slots only).
- [x] **Edge — Quantity > StackLimit** (implementation-defined; decided 2026-10-01): the entry loads **as-is** with its full quantity; server warning logged. No units are destroyed.
- [x] **Item Database not ready**: `ImportSnapshot` logs a server error, returns `false`, and changes nothing — the character's existing registration and contents (if any) are untouched. A `null` snapshot behaves the same way (server error, `false`, nothing changed).
- [x] **Return contract**: `ImportSnapshot` returns `true` whenever the import was applied, including when individual entries were rejected or cleared.
- [x] **Mutation-seam re-entrancy**: `ImportSnapshot` called synchronously from an `OnInventoryChanged` subscriber throws `InvalidOperationException` and changes nothing.

**UnregisterCharacter (resolves TD-042)**

- [x] **Unregister releases state**: `UnregisterCharacter(charId)` removes the character's slots, locks and bag-full dedup window; afterwards the character is unregistered for every API (e.g. `GetSlot` logs the unregistered-character error and returns `InventorySlot.Empty`). Fires no event. Other characters are unaffected.
- [x] **Unregister is idempotent**: calling it for a character that is not registered is a silent no-op (no log, no exception).
- [x] **Unregister respects the dispatch guard**: called synchronously from an `OnInventoryChanged` subscriber it throws `InvalidOperationException` and removes nothing.
- [x] **Round trip**: export → `UnregisterCharacter` → `ImportSnapshot` restores the same slots (the save/logout/login path).

---

## Implementation Notes

- **Types** in `IronGrind.InventorySystem` (with XML docs):
  - `public readonly struct InventorySnapshotEntry { public readonly byte SlotIndex; public readonly uint ItemId; public readonly int Quantity; }` with a public constructor. `ItemId` is the raw `uint` — convert to/from `ItemID` at the boundary (`ItemID.RawValue` / `new ItemID(uint)`).
  - `public sealed class InventorySnapshot { public IReadOnlyList<InventorySnapshotEntry> Slots { get; } }` with a constructor taking the list (`ArgumentNullException` on `null`) and a `static readonly InventorySnapshot Empty`. A class, not a struct: it owns a list and is created once per save/load, not per frame.
- **API** on `IInventoryService`: `ExportSnapshot`, `ImportSnapshot`, `UnregisterCharacter`. Update `RegisterCharacter`'s docs: seeding from a saved snapshot is `ImportSnapshot`.
- **ImportSnapshot order**: `ThrowIfDispatching()` → `null` snapshot → error, `false` → `!_itemDatabase.IsReady` → error, `false` → reset (same three writes as `RegisterCharacter`) → apply entries in list order → `true`. The ready check comes before the reset so a too-early import can never wipe a bag.
- **Per-entry validation order** (first match wins; all are warnings, none throw): `SlotIndex ≥ 20` → reject; slot already claimed by an earlier entry → reject (track claims in a local `bool[20]`, set as soon as an in-range entry is seen); `ItemId == 0` → leave empty; `Quantity ≤ 0` → leave empty; `TryGetItem` fails → leave empty, log CharacterID + ItemId; `Quantity > StackLimit` → warn, then load as-is; otherwise load.
- **Why over-limit loads as-is**: clearing or clamping would destroy player items whenever a designer lowers a `StackLimit`. `Pickup` already skips stacks at or above the limit (Story 002), and `Move` merges transfer `≤ 0` into them, so an over-limit stack is safe in memory.
- **No events, no change buffer**: import writes slots directly — never `RecordSlotChange` / `EmitInventoryChanged` / `NotifyInventoryFull`. Look items up with `_itemDatabase.TryGetItem` directly, not `TryGetStackLimit` (which logs an error).
- **ExportSnapshot** does not call `ThrowIfDispatching` — it is a read, like `GetSlot`.
- **UnregisterCharacter**: `ThrowIfDispatching()` then remove from `_inventories`, `_locks`, `_bagFullWindowExpiry`. Update the class remarks' lifecycle paragraph and mark TD-042 resolved in `docs/tech-debt-register.md` at `/story-done`.
- **Logging**: per-entry problems → `Debug.LogWarning` (corrupt or outdated data, not a caller bug), each naming the CharacterID and the entry. `null` snapshot, Item Database not ready, unregistered export → `Debug.LogError` (caller bugs).
- **Performance**: export and import are each one pass over at most 20 slots / the entry list, once per session start or end — never on a per-frame path. Export allocates one list (acceptable off the hot path).
- **Known gap (TD-045, not this story)**: the snapshot has no per-item `EnhancementLevel`. The Approved Inventory GDD defines none; the Enhancement GDD expects one. Decided 2026-10-01: implement per the Inventory GDD; resolve in a dedicated design session before the Enhancement and Equipment epics.
- Do not implement the JSONB column mapping or `SaveSession` — Character Persistence epic.

---

## Out of Scope

- `character_records.inventory_slots` JSONB mapping, Dapper/SQL, session save/load orchestration, and calling `UnregisterCharacter` from the logout path (Character Persistence)
- Enhancement System's interrupted-attempt recovery on login (Enhancement System — Lock State Edge Case)
- Per-item `EnhancementLevel` in slots or snapshots (TD-045)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_SnapshotSaveLoad_tests.cs`

*(All calls below take `charId` first; omitted for brevity. Entries are written `{SlotIndex, ItemId, Quantity}`.)*

- **AC-INV-3** — Given Bronze Sword ×1 in slot 0, HP Potion ×42 in slot 7, HP Potion ×99 in slot 19; When export → import into a new `InventoryService`; Then import returns `true`, slots 0/7/19 match exactly, the other 17 slots Invalid/0.
- **Non-empty only, ascending** — the exported snapshot from the case above has exactly 3 entries in order 0, 7, 19 with raw ItemIds and quantities; an empty inventory exports 0 entries.
- **Export unregistered** — unregistered `charId` → 0 entries, error logged; no event.
- **Export is a read** — export leaves all slots and locks unchanged and fires 0 events.
- **Locks not persisted** — slot 7 locked at export → after import `IsSlotLocked(7)` false, item ×42 intact.
- **Import replaces** — character holds MP Potion ×5 in slot 3; import a snapshot with only `{7, HPPotion, 42}` → slot 3 empty, slot 7 ×42; a slot locked before the import is unlocked after.
- **Import registers** — `ImportSnapshot` on a never-registered character → `true`; `GetSlot` then reads the slot with no error log.
- **Import resets dedup window** — full bag, blocked pickup (1 notification), import the same full snapshot, blocked pickup again → 2 notifications.
- **Unknown ItemId** — entries `{3, 999999, 5}`, `{4, HPPotion, 2}` → slot 3 empty, warning naming the character and 999999; slot 4 ×2.
- **Duplicate SlotIndex** — `{5, HPPotion, 10}` then `{5, BronzeSword, 1}` → slot 5 = HP Potion ×10, warning. Edge: `{5, HPPotion, 0}` then `{5, BronzeSword, 1}` → slot 5 empty (first entry claimed the slot), two warnings.
- **Quantity 0 / negative** — `{5, HPPotion, 0}` → slot 5 empty, warning; `{5, HPPotion, -3}` → slot 5 empty, warning.
- **Out-of-range index** — `{20, HPPotion, 1}` and `{255, HPPotion, 1}` → rejected, no exception, warning each, all 20 slots empty, import returns `true`.
- **ItemId 0** — `{5, 0, 3}` → slot 5 empty, warning.
- **Over-limit quantity** — `{5, HPPotion, 150}` (StackLimit 99) → slot 5 ×150, warning; `{0, BronzeSword, 2}` (StackLimit 1) → slot 0 ×2, warning.
- **Item Database not ready** — stub `IsReady = false`; character holds HP Potion ×5 in slot 2; `ImportSnapshot` → `false`, error logged, slot 2 still ×5. Never-registered character → `false`, still unregistered.
- **Null snapshot** — `ImportSnapshot(charId, null)` → `false`, error logged, existing contents unchanged.
- **No events on load** — subscribers attached before import → 0 `OnInventoryChanged` and 0 `OnInventoryFull` calls, including for a snapshot that fills all 20 slots.
- **Import re-entrancy** — subscriber on `OnInventoryChanged` calls `ImportSnapshot` → `InvalidOperationException` ("mutated synchronously"); slots unchanged apart from the triggering pickup.
- **Unregister** — registered character with items and a locked slot; `UnregisterCharacter` → `GetSlot` logs the unregistered error and returns Empty; 0 events; a second registered character's slots unchanged.
- **Unregister idempotent** — `UnregisterCharacter` twice, and on a never-registered character → no log, no exception.
- **Unregister re-entrancy** — subscriber calls `UnregisterCharacter` → `InvalidOperationException`; character still registered with its slots.
- **Round trip** — seed slots 0/7/19, export, unregister, import → slots 0/7/19 restored, others empty, no locks.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SnapshotSaveLoad_tests.cs` — must exist and pass

**Status**: [x] Created — 39 tests, all passing in live Test Runner (2026-10-01)

---

## Dependencies

- Depends on: Story 001; Story 003 (bag-full dedup window cleared on import/unregister); Story 004 (lock state for the not-persisted case)
- Unlocks: Character Persistence integration (future epic)

---

## Completion Notes
**Completed**: 2026-10-01
**Criteria**: 19/19 passing (none deferred)
**Deviations**: None blocking. Four load rules were implementation-defined at implementation time (SlotIndex ≥ 20, ItemId 0, over-limit quantity loads as-is, Item Database not ready) — added to the GDD's Persistence and Load Edge Cases at close-out (2026-10-01). No per-item `EnhancementLevel` in the snapshot (TD-045, by decision). `UnregisterCharacter`'s removal of the dedup window alone is not observable through the public API (re-registration also clears it) — verified by code review, not by test. TR registry has no `TR-inv-*` entries yet (pre-existing).
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_SnapshotSaveLoad_tests.cs` (39 tests, passing in live Test Runner)
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all applied (+10 tests, strict no-unexpected-log asserts on 9 warning tests, `InventorySnapshot?` annotation, interface crefs). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: TD-042 resolved (`UnregisterCharacter`); the logout-path call belongs to the Character Persistence epic.
