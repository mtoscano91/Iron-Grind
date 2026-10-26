# Story 007: Restore and Export Gear Slots — the Load and Save Seam

> **Epic**: Equipment System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-15 (non-persistence of `IsTransitioning`, crash recovery, no startup cleanup), Dependencies → Character Persistence row; `design/gdd/character-persistence.md` — CR-CP-3 step 4, CR-CP-10 step 3, EC-CP-1 (`ItemDatabaseMiss`)
**Requirement**: `TR-equip-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-006: Persistence Layer (with ADR-010 for the call style)
**ADR Decision Summary**: `gear_slots` JSONB holds 7 `{item_id, enhancement_level}` entries; equipment state is rebuilt from them on load. Computed modifier values, appearance flags and `IsTransitioning` are never persisted.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: None. Runs synchronously on the tick thread inside `ApplyLoadedRecord` (ADR-011: `LoadCharacter` validates async, `ApplyLoadedRecord` writes synchronously).

**Control Manifest Rules (this layer)**:
- Required: writes on the tick thread; no `async` in this path (ADR-011)
- Forbidden: `await` / `Task.Wait` in tick-driven core code (ADR-011)
- Guardrail: restore registers each modifier exactly once — never a second time on a reload

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md` (and the Character Persistence rows it cites), scoped to this story:*

- [x] **AC-EQS-25** — restoring 7 saved `{ItemID, EnhancementLevel}` pairs into a freshly registered character: `IsSlotTransitioning` false for all 7; the modifier stack holds every item's enhanced modifiers (re-registered from the Item Database at each slot's saved level through `GetFlatBonus`)
- [x] Restore reads every slot's level and item, sets the read surface (`GetEquipmentSlotState`, `GetSlotEnhancementLevel`, weapon reads) and the cached appearance byte; it raises no `OnAppearanceChanged` (initial state reaches clients through the zone snapshot)
- [x] An occupied slot whose `ItemID` the Item Database does not know is left empty, logged with `Debug.LogError` (character id, slot, item id), and reported in the return value so Character Persistence can set `ItemDatabaseMiss`; the other slots still restore (EC-CP-1)
- [x] An item whose `GearSlot` does not match the slot index it was saved under is treated the same way (left empty, logged, reported) — defence against a corrupt record; documented
- [x] No startup cleanup pass: an item that is both in a saved gear slot and in the saved bag stays in both (CR-EQS-15 point 3 — the saved slot is authoritative; duplicate resolution is manual)
- [x] `ExportSlots(charId)` returns the 7 `{ItemID, EnhancementLevel}` pairs of the live registry; `IsTransitioning` is never part of it; empty slots export as `ItemID.Invalid` / 0
- [x] Round trip: export → unregister → register → restore → identical read surface, effective stats and appearance byte

---

## Implementation Notes

*Derived from ADR-006, CR-EQS-15 and CR-CP-3 step 4:*

- **Epic decision (2026-10-26): the Equipment System owns re-registration on load** (CR-EQS-15 point 1). Character Persistence does not call `AddEquipmentModifier` itself; its Story 005 becomes a thin caller of `RestoreSlots` at load step 4 and of `ExportSlots` at save step 3 (CR-CP-10). This settles open decisions 1 and 2 of Character Persistence Story 005 — note it there when that story is picked up.
- Dependency direction is one-way, Persistence → Equipment: define an equipment-owned `readonly struct SavedGearSlot { ItemID ItemId; byte EnhancementLevel }`; Character Persistence converts from its `GearSlotRecord`. The Equipment System never references Character Persistence types.
- **Pinned API (resolved at `/story-readiness` 2026-10-26, user decision).** Both methods on `IEquipmentService` (ADR-010 Tier 1 — Character Persistence calls them through the interface); the two types are new files in `src/ServerLogic/EquipmentSystem/`:
  ```csharp
  public readonly struct SavedGearSlot : IEquatable<SavedGearSlot>
  {
      public SavedGearSlot(ItemID itemId, byte enhancementLevel);
      public ItemID ItemId { get; }
      public byte EnhancementLevel { get; }
      public static readonly SavedGearSlot Empty;   // ItemID.Invalid, 0
  }

  public readonly struct GearRestoreResult
  {
      public byte DroppedSlotMask { get; }           // bit (int)GearSlot set = that slot was left empty
      public int DroppedCount { get; }               // popcount of the mask
      public bool IsDropped(GearSlot slot);
  }

  GearRestoreResult RestoreSlots(CharacterID charId, ReadOnlySpan<SavedGearSlot> slots);
  SavedGearSlot[] ExportSlots(CharacterID charId);   // new array, length 7, index = (int)GearSlot
  ```
  Character Persistence already holds `GearSlotRecord[]`; a converted `SavedGearSlot[]` passes to the span parameter implicitly.
- `RestoreSlots` validates everything before registering anything, so a throw leaves the character untouched: unregistered character or any occupied slot (double restore) → `InvalidOperationException`; length ≠ 7 → `ArgumentException`; a level above the `EnhancementConfig` maximum → `ArgumentOutOfRangeException` (Character Persistence validates first and maps to `CorruptRecord`, CR-CP-3 step 4 — this is the re-check, so `GetFlatBonus` never sees an out-of-range level); an empty saved slot (`ItemID.Invalid`) with a non-zero level → `ArgumentException`. An Item Database miss or a `GearSlot` mismatch is not an exception: the slot is left empty, logged, and set in `DroppedSlotMask`.
- `ExportSlots` on an unregistered character throws `InvalidOperationException` — unlike the read surface (log + empty value), because a save that silently exported 7 empty slots would wipe the player's gear. `IsTransitioning` is never exported (CR-EQS-15); there is no mid-swap case, since a swap completes inside one tick-thread call.
- Use the same enhanced-modifier helper as Story 002 and the same appearance recompute as Story 005, without the event.
- **Drift to note in Character Persistence Story 005, not fixed here:** CR-CP-3 step 4 also lists `GetElementalBonus` on load. Equipment writes no elemental data to Character Stats (CR-EQS-12) — Damage Calculation calls `GetElementalBonus` per hit — so that call has no target.

---

## Out of Scope

- Character Persistence Story 005: calling `RestoreSlots` / `ExportSlots` from `ApplyLoadedRecord` and the save snapshot, `ItemDatabaseMiss` code, `CorruptRecord` validation
- Character Persistence Story 016: the real-database round trip (AC-CP-26)

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_RestoreExport_tests.cs` (new — the project keeps integration tests in the EditMode assembly, as Character Persistence does). Real `CharacterStats`, `ItemDatabase` (seeded), `EnhancementBonusProvider`.

- **AC-EQS-25** — Given a registered character with no in-memory equipment and 7 saved pairs (levels 0, 3, 5, 7, 0, 0, 0 — accessories at 0); When `RestoreSlots`; Then all 7 not transitioning, each slot's state/level match the save, effective stats = base + Σ enhanced modifiers (computed with the real provider in the test).
- **Appearance after restore** — Dark Steel Fire sword at 8 + Steel Chest → cached byte `0b11_001_11_1`; no `OnAppearanceChanged` raised.
- **Item Database miss** — slot 2 holds an unknown id → slot 2 Empty, one `LogError` with character id / slot / item id, return value reports slot 2, the other six restored.
- **Slot mismatch** — a Helmet id saved under Weapon → same treatment as a miss.
- **No cleanup** — the weapon's `ItemID` also present in the bag → both remain; no Inventory call during restore (spy).
- **Preconditions** — restore twice → second throws `InvalidOperationException`, modifiers registered once; unregistered character throws; length 6 throws; level 11 throws `ArgumentOutOfRangeException` before any `AddEquipmentModifier`; an empty saved slot with level 3 throws `ArgumentException`; `ExportSlots` on an unregistered character throws `InvalidOperationException`.
- **Export** — after equips and a swap, `ExportSlots` matches the read surface; empty slots `Invalid` / 0.
- **Round trip** — export → unregister → register → restore: read surface, effective stats and appearance byte equal the originals.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: the test file above — must exist and pass

**Status**: [x] Created — 17 tests, all passing 2026-10-26

---

## Dependencies

- Depends on: Stories 002, 004, 005
- Unlocks: Character Persistence Story 005 (then Story 016)

---

## Completion Notes

**Completed**: 2026-10-26
**Criteria**: 7/7 passing, all covered by `EquipmentSystem_RestoreExport_tests.cs` (no deferred items)
**Deviations** (advisory, accepted):
1. An exception thrown by Character Stats itself while the slots are applied is not rolled back (the slots before it stay restored); the story requires validation-first only. Documented in the `RestoreSlots` remarks.
2. For Character Persistence Story 005, not a deviation here: `UnregisterCharacter` does not remove Character Stats modifiers (the stats entity is discarded with the session). A load that reuses a stats entity must clear them first; the round-trip test removes them itself.
**Test Evidence**: Integration — `tests/EditMode/EquipmentSystem/EquipmentSystem_RestoreExport_tests.cs` (17 tests); full EditMode 4454 total / 4343 passed / 0 failed / 111 skipped. `EquipmentSystem_TestDoubles.cs` gained `InventoryDecorator.TotalForwardedCalls` (valid knock-on, outside the story file list).
**Code Review**: Complete — 2026-10-26, APPROVED WITH SUGGESTIONS (unity-specialist: `RestoreSlots` length/complexity WARNING; qa-tester GAPS, none blocking; ADR-006/010/011 compliant) → "fix all" applied (`RestoreSlots` split into `GetRestorableGear` / `ValidateSavedSlots` / `TryRestoreSlot`, levels checked in one pass before empty-with-level; `GearRestoreResult` `#nullable enable` and `GEAR_SLOT_COUNT` bound; tests 13 → 17). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
