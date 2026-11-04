# Story 018: Zone Wires the Full Persistence Applier

> **Epic**: Character Persistence
> **Status**: Complete (2026-11-04; readiness 2026-11-04; written 2026-11-04 to resolve TD-090, found by the Skill System Story 011 code review)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — the load path (record applied on zone entry: class, level, XP, base stats, HP / MP, free points, inventory, currency, equipment); `design/gdd/zone-instancing.md` — CR-ZI-8 step 3 (`LoadCharacter` during admission)
**Requirement**: `TR-cp-003` — CR-CP-3, the load sequence in its exact order (stat initialisation before equipment re-registration); `LoadCharacter` fetches and validates only (async); `ApplyLoadedRecord` does every write synchronously on the tick thread.

**ADR Governing Implementation**: ADR-006 (persistence), ADR-014 (composition root creates server-only components in code)
**ADR Decision Summary**: ADR-006: the zone's `CharacterPersistenceService` is the only path that applies a stored record to the zone's live state. ADR-014: the composition root builds every server-only component in code, in a fixed S5 order that the construction-order test checks.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (pure C#, no Unity API).

---

## Problem (TD-090)

`ZoneServerComposition.BuildPersistence` builds `CharacterPersistenceService` with the store-only constructor at the start of S5, before Character Stats and Leveling exist (`BuildStatusEffects`). `ZoneEntryAdmission` applies the loaded record through `ILoadedRecordApplier` → `CharacterPersistenceService.ApplyLoadedRecord`, which throws unless Stats, Leveling, Inventory, Currency and Equipment are all set. The zone has only `StandInZoneInventoryService` / `StandInZoneEquipmentService` and no currency restore. So every admission logs an error and releases the slot: no class type is registered, no stats or items are restored, and the Skill System (Story 011) never spawns a skill store outside tests.

---

## Acceptance Criteria

- [x] S5 builds the class registry, `LevelingService` and `ZoneStats` before `CharacterPersistenceService`, and builds Persistence with the six-service constructor (store, Stats, Leveling, Inventory, Currency, Equipment). The construction-order list in `ZoneServer_CompositionWiring_tests.cs` is updated to match: `CharacterStats` comes before `CharacterPersistenceService`, which comes before `ZoneInstancingWiring`, which comes before `StatusEffectsService`.
- [x] `ZoneInstancingWiring` and Status Effects use the same `ZoneStats` / `LevelingService` instances that Persistence holds (identity checked in the test).
- [x] A player admitted through the composed zone with a loaded record is not released: `ApplyLoadedRecord` returns `Success`, and no admission error or `ItemDatabaseMiss` warning is logged.
- [x] After admission, `LevelingService.TryGetClassType` returns the record's class, and level, XP, the 9 integer and 5 float base stats, held free points, and current HP / MP match the record (HP is full when the record's HP is ≤ 0).
- [x] Currency is the real `CurrencySystem`: after admission, `TryGetState` returns the record's gold balance and version.
- [x] `StandInZoneInventoryService.ImportSnapshot` returns true, and `StandInZoneEquipmentService` accepts `UnregisterCharacter` / `RegisterCharacter` / `RestoreSlots` (0 dropped). Each stand-in logs "[ZoneServer] … not implemented" exactly once per instance across all calls. Their Export methods still throw.
- [x] The Skill System spawn listener creates the joiner's skill store: the joiner's `SkillCooldownSnapshot` carries the class skill ids, and no "no skill store" or class-type warning is logged. The direct class-type registration in `SkillSystem_Composition_tests.cs` / `SkillSystem_ZoneRosterData_tests.cs` that worked around TD-090 is removed, and those tests still pass.

---

## Implementation Notes

- Split `BuildStatusEffects`: the class registry, Leveling and `ZoneStats` move into a new step built before `BuildPersistence` (it needs no wiring). `StatusEffectsService` stays after `BuildZoneInstancing` because it needs `wiring.Pool`. No late binding.
- The six-service constructor leaves the zone locator and write queue unset, so `SaveSession` still throws synchronously (`ZoneCloseSteps` step 2 logs a failed save start). This is intended until real Inventory and Equipment exist: see Out of Scope and TD-091.
- `ZoneServer_CompositionDrain_tests.cs:122` builds the wiring directly; adjust it only if its constructor arguments change.
- Performance: no per-tick cost; only the construction order and the one-time apply at admission change.

---

## Out of Scope

- Persisting skill cooldowns (cooldowns are per zone process, EC-SK-13)
- The zone save path (`SaveSession`, ghost snapshot). With stand-in Inventory and Equipment a save would write an empty bag and empty gear over the record. It stays failing-safe (throws before writing) until real Inventory and Equipment and an `ICharacterZoneLocator` exist (TD-091).
- Real Inventory / Equipment services in the zone.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneApplier_tests.cs` — must exist and pass

**Status**: [x] Created — 14 tests, all passing (full EditMode 6483 / 6354 passed / 0 failed / 129 skipped, 2026-11-04 19:03:38Z)

---

## Dependencies

- Depends on: Skill System Story 011 (Complete; for the skill-store criterion)
- Unlocks: live zone tests with real characters (load only; saving needs TD-091)

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 7/7 passing (all automated; none deferred)
**Deviations** (advisory, no new tech debt):
- The zone-close save (`ZoneCloseSteps` step 2) now logs one failed save start per admitted player (the service has no save wiring, so `SaveCore` throws at its wiring check before any export). Nothing is written; covered by TD-091. **Correction (Story 019, 2026-11-04):** in the composed zone an admitted player stays "entering" (SessionReady is not wired), so step 2 skips it and no save start is logged; `SaveSession` called directly still throws.
- Inventory and gear loaded from the record are discarded in the zone (stand-ins accept and drop them): no bag, no gear modifiers until real services exist. Documented in the stand-ins' remarks; replace them together with enabling the save path (TD-091).
- Logout / session-TTL saves would throw synchronously, but neither is reachable in the composed zone yet (logging ghost source, no `BeginCleanLogout` caller).
- Test-only seams added: `RecordStoreOverride`, `EntryTokenValidatorOverride` (internal, null in production).
**Test Evidence**: Integration: `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneApplier_tests.cs` (14 tests, real `BeginEntry` → `LoadCharacter` → `ApplyLoadedRecord` → admit over an in-memory store). Full EditMode 6483 / 6354 passed / 0 failed / 129 skipped, started 2026-11-04 19:03:38Z.
**Code Review**: Complete: `/code-review` APPROVED WITH SUGGESTIONS (unity-specialist CLEAN, qa-tester TESTABLE); all 8 suggestions applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
**Resolves**: TD-090.
