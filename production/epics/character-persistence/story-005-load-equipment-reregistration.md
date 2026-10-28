# Story 005: `LoadCharacter` — Equipment Re-registration

> **Epic**: Character Persistence
> **Status**: Complete — 2026-10-28 (COMPLETE WITH NOTES; see Completion Notes)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 8 hours (load step 4, save step 3 switched to `ExportSlots`, admission fix, 46 constructor call sites in tests)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-3 steps 3–4; CR-CP-10 step 3; EC-CP-1 (`ItemDatabaseMiss`); EC-CP-2; AC-CP-7, AC-CP-11, AC-CP-12, AC-CP-24. `design/gdd/equipment-system.md` — CR-EQS-15 (restore and export of gear slots).
**Requirement**: `TR-cp-003`

**ADR Governing Implementation**: ADR-011 Decision 3; ADR-010.
**ADR Decision Summary**: equipment modifier values are not persisted; they are recomputed on load. The Equipment System owns that recompute (`IEquipmentService.RestoreSlots`, Equipment epic decision 2026-10-26); Character Persistence calls it at load step 4 and calls `ExportSlots` at save step 3 — a direct call on an injected interface (ADR-010). All of it runs in the synchronous `ApplyLoadedRecord` / the save snapshot, on the tick thread (ADR-011).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved beyond `Debug.LogError`, which the Equipment System already calls.

**Control Manifest Rules (this layer)**:
- Required: cross-system communication is a direct call on an injected interface — ADR-010
- Forbidden: reading or writing live game state after the first `await` in the persistence layer — ADR-011 (step 4's validation goes in `LoadCharacter`, which reads only the fetched record; the writes stay in `ApplyLoadedRecord`)

---

## Acceptance Criteria

- [x] **Validation (CR-CP-3 step 4, in `LoadCharacter`)**: an occupied gear slot whose `EnhancementLevel` is above `EnhancementConstants.MAX_ENHANCEMENT_LEVEL` (10) makes the load return `CorruptRecord`, and nothing is applied. Level 10 loads. An empty slot (`ItemID.Invalid`) with a non-zero level is also `CorruptRecord` (`RestoreSlots` would throw on it).
- [x] **Call order (step 4)**: `ApplyLoadedRecord` calls, after `RestoreLevelingState` and before `ImportSnapshot`: `IEquipmentService.UnregisterCharacter(charId)` (a no-op on a first load; a reload would otherwise throw in `RegisterCharacter`), then `RegisterCharacter(charId, entityId)`, then `RestoreSlots(charId, slots)` with the record's 7 gear slots converted to `SavedGearSlot`, index = `(int)GearSlot`. An all-empty record still registers the character and calls `RestoreSlots` with 7 empty slots.
- [x] **AC-CP-11**: for a record with gear in slot 0, all of step 3's writes (11 `SetBaseStat`, 5 `SetBaseStatFloat`, `RestoreLevelingState`) are made before the first `AddEquipmentModifier` call (observed on Character Stats with the real `EquipmentService`).
- [x] **AC-CP-7**: after the load, `GetEffectiveStat(AttackPower)` equals `GetBaseStat(AttackPower)` plus the weapon's flat bonus for its loaded enhancement level (the test's enhancement-bonus provider double gives that bonus).
- [x] **AC-CP-12 / EC-CP-1**: a slot whose `ItemID` the Item Database does not know is left empty, `ApplyLoadedRecord` returns `ItemDatabaseMiss`, the steps after it (inventory, gold, HP / MP) are applied, and the session entry is created. Exactly one `Debug.LogError` per dropped slot — the one `RestoreSlots` logs; Character Persistence adds none. A slot whose item belongs to another gear slot (also in `GearRestoreResult.DroppedSlotMask`) gives the same code.
- [x] **Two misses**: with two unknown items the code is still `ItemDatabaseMiss`, both slots are empty, two errors logged.
- [x] **Admission**: `ZoneEntryAdmission` admits the player when `ApplyLoadedRecord` returns `ItemDatabaseMiss` (no `JoinRejected`, the slot is not released) and still rejects every other code other than `Success`.
- [x] **AC-CP-24 / EC-CP-2**: a record saved with `CurrentHP = 650` whose effective MaxHP falls to 500 because of an item miss loads with `CurrentHP = 500` and no error.
- [x] **Dead on load with gear (Story 004 review obligation)**: a gear modifier makes the effective MaxHP differ from the base; a saved `CurrentHP <= 0` loads as the effective value.
- [x] **Save step 3 (CR-CP-10)**: `SaveSession` and `SaveIrreversibleOutcome` write the gear from `IEquipmentService.ExportSlots(charId)`, not from a load-time copy; the session entry no longer keeps gear. After a load with a miss, the next save writes that slot empty (EC-CP-1 "corrected on the next save"). After an equip in the session, the save writes the new item and level.

---

## Implementation Notes

- **Equipment API in code** (`src/ServerLogic/EquipmentSystem/IEquipmentService.cs`): `RegisterCharacter(CharacterID, EntityID)` throws `InvalidOperationException` if the character or the entity is already registered; `UnregisterCharacter` is a silent no-op for an unknown character; `RestoreSlots(CharacterID, ReadOnlySpan<SavedGearSlot>)` validates everything before changing anything, returns `GearRestoreResult` (`DroppedSlotMask`, `DroppedCount`), logs each dropped slot itself, raises no `OnAppearanceChanged` and makes no Inventory call; `ExportSlots(CharacterID)` returns a new length-7 array and throws if the character is unregistered.
- **Conversion**: `GearSlotRecord` (Persistence) → `SavedGearSlot` (Equipment) and back, field by field. Equipment owns `SavedGearSlot` so the dependency stays Persistence → Equipment.
- **Step 4 position**: inside `ApplyLoadedRecord`, between `RestoreLevelingState` and the inventory import (the existing remark at `CharacterPersistenceService.cs` already marks the spot). Keep a local `CharacterLoadCode` = `ItemDatabaseMiss` when `DroppedCount > 0`; an `InventoryNotRestored` later still wins and returns early. Return the local code at the end instead of `Success`.
- **`ItemDatabaseMiss` is success with a warning**: state it in `CharacterLoadCode`'s doc comment (the caller must admit the player) and in `ApplyLoadedRecord`'s `<returns>`.
- **A failed apply after step 4** (`InventoryNotRestored`, or a throw) leaves the character registered with Equipment until session end or the next load; the next load's `UnregisterCharacter` clears it. Equipment's `UnregisterCharacter` does not remove modifiers from Character Stats — the entity is discarded, as with the stat writes.
- **Wiring (user decision 2026-10-28)**: `IEquipmentService` becomes a required parameter of the 5-argument and 9-argument constructors (null → `ArgumentNullException`), and `ApplyLoadedRecord`'s wiring check includes it. Update the 46 test call sites (13 files) — an `EquipmentService` built with the existing Equipment test doubles, or a recording fake where a test only checks order. `ZoneServerComposition` still uses the 1-argument constructor; no change there.
- **Save**: remove `SessionEntry.GearSlots` and its clone at load; step 3 calls `ExportSlots` while the snapshot is taken on the tick thread (same place as today's `Array.Copy`). The save runs before `SessionEndCoordinator.Release` unregisters equipment (Equipment Story 008's order), so the character is still registered. An `ExportSlots` throw is a wiring/order bug — let it propagate as the other step-2 getters do.
- **Admission** (`src/ServerLogic/ZoneInstancing/ZoneEntryAdmission.cs`, the `applied != CharacterLoadCode.Success` check): also accept `ItemDatabaseMiss`, with a `Debug.LogWarning` naming the client. Cross-epic file, delegated by this story.
- **Alerts**: the Equipment System logs the dropped slot with `Debug.LogError` (the project's critical-alert form); Character Persistence does not log again.

---

## Out of Scope

- Story 004: the HP / MP clamp itself (this story's tests only join it to a miss)
- The Equipment System's own equip / unequip and restore rules (Equipment Stories 001–007)
- Wiring the real `IEquipmentService` into `ZoneServerComposition` (server bootstrap; Equipment carry-over from Stories 008 and 009)
- Sending the appearance byte in the zone snapshot (OQ-EQS-6)

---

## QA Test Cases

- **Level bound** — Level 10 → loads; level 11 → `CorruptRecord`, no `ApplyLoadedRecord` writes. Empty slot with level 3 → `CorruptRecord`.
- **Order** — Given gear in slot 0, Then the recorded calls show every step-3 write before the first `AddEquipmentModifier`, and `ImportSnapshot` after `RestoreSlots`.
- **Reload** — Load the same character twice into two entities: the second load does not throw and its modifiers are on the new entity.
- **Effective stat** — Given a +7 weapon, Then effective AttackPower = base + the provider's flat bonus for 7.
- **Item miss** — Given slot 2 unknown, Then slot 2 empty, one error logged, `ItemDatabaseMiss`, the other slots restored, inventory / gold / HP applied. Edge: two misses; slot mismatch.
- **Clamp after a miss** — Given saved HP 650 and a missed item that lowers MaxHP to 500, Then HP 500.
- **Dead on load with gear** — saved HP 0, gear adds MaxHP → HP = effective MaxHP.
- **Admission** — `ItemDatabaseMiss` admitted; `InventoryNotRestored` still rejected.
- **Save** — after a miss, the saved gear has that slot empty; after an equip, the saved gear has the new item.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadEquipment_tests.cs` — must exist and pass; the admission case in the Zone Instancing admission tests; the save case in the Character Persistence save tests. Full EditMode suite green.

**Status**: [x] Created and passing — 24 tests in the file, plus `test_apply_item_database_miss_is_admitted_with_warning_and_slot_kept` in `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_EntryAdmission_tests.cs`; EditMode 2026-10-28: 4570 total / 4459 passed / 0 failed / 111 skipped.

---

## Dependencies

- Depends on: Stories 003, 004, 007 (Complete); Equipment System epic (Complete 2026-10-28 — `RegisterCharacter`, `RestoreSlots`, `ExportSlots`, `UnregisterCharacter`)
- Unlocks: Story 016 (AC-CP-26)

---

## Readiness Record (2026-10-28)

Lean mode, QL-STORY-READY skipped. Fixed: open decisions 1–3 closed by the Equipment epic decision (2026-10-26: Equipment owns re-registration through `RestoreSlots`; bonus inputs and alerts are inside it); criteria rewritten to the real Equipment calls; empty-slot-with-level → `CorruptRecord` (user decision); save step 3 moves to `ExportSlots` in this story (user decision; Equipment EPIC decision 2); admission accepts `ItemDatabaseMiss` (user decision); equipment added to the existing constructors (user decision); slot mismatch → `ItemDatabaseMiss`; manifest version 2026-10-09 → 2026-10-26 (rules added since concern purchases, zone lifecycle and the relay — none apply); estimate 6 → 8 hours. GDD lines written: CR-CP-3 step 4, CR-CP-10 step 3, EC-CP-1, Interactions.

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 10/10 passing, all covered by automated tests (none deferred).
**Deviations** (advisory, accepted; no tech debt logged — user choice):
1. The 8-argument convenience constructor (`…, currency, equipment, zones, utcNow`) also takes `IEquipmentService`. It chains to the 10-argument one; the story named only the 6- and 10-argument constructors.
2. AC-CP-11 is observed by reading Character Stats (all 11 int stats, 5 float stats and held free points) inside the fake equipment service's `RestoreSlots` call, not by recording the real `AddEquipmentModifier` calls. The real-`EquipmentService` AC-CP-7 test shows the bonus lands on an intact base.
3. `LoadCharacter` checks levels against `EnhancementConstants.MAX_ENHANCEMENT_LEVEL`; `RestoreSlots` checks `EnhancementConfig`'s maximum. Equal in the default config; a config with a lower maximum would make `ApplyLoadedRecord` throw. Commented at `AreGearSlotsValid`.
4. An `ExportSlots` failure at save time (throw, null, or a length other than 7) gives `DatabaseError` with one error log through the existing snapshot handler, rather than propagating as the implementation notes said; this matches the other snapshot getters.
**Test Evidence**: Integration — `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadEquipment_tests.cs` (24 tests) + admission test in `ZoneInstancing_EntryAdmission_tests.cs`; EditMode 4570 / 4459 passed / 0 failed / 111 skipped.
**Code Review**: Complete — `/code-review` 2026-10-28 APPROVED WITH SUGGESTIONS, "fix all" applied (exception and partial-failure docs, fresh-entity note, export length guard, 6 more tests, strengthened assertions). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
**Carry-over**: the real `IEquipmentService` is still not wired into `ZoneServerComposition` (server bootstrap, Equipment carry-over from Stories 008/009); until then the zone server's `CharacterPersistenceService` uses the 1-argument constructor and does not apply records.
