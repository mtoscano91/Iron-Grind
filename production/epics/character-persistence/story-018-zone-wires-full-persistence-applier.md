# Story 018: Zone Wires the Full Persistence Applier

> **Epic**: Character Persistence
> **Status**: Draft — written 2026-11-04 to resolve TD-090 (found by the Skill System Story 011 code review); run `/story-readiness` before implementing
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours (provisional)

## Context

**GDD**: `design/gdd/character-persistence.md` — the load path (record applied on zone entry: class, level, XP, base stats, HP / MP, free points, inventory, currency, equipment); `design/gdd/zone-instancing.md` — CR-ZI-8 step 3 (`LoadCharacter` during admission)
**Requirement**: TBD at readiness (the Character Persistence load requirement; check `docs/architecture/tr-registry.yaml`)

**ADR Governing Implementation**: ADR-006 (persistence), ADR-014 (composition root creates server-only components in code)
**ADR Decision Summary**: TBD at readiness.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

---

## Problem (TD-090)

`ZoneServerComposition.BuildPersistence` builds `CharacterPersistenceService` with the store-only constructor at the start of S5, before Character Stats and Leveling exist (`BuildStatusEffects`). `ZoneEntryAdmission` applies the loaded record through `ILoadedRecordApplier` → `CharacterPersistenceService.ApplyLoadedRecord`, which throws unless Stats, Leveling, Inventory, Currency and Equipment are all set. The zone has only `StandInZoneInventoryService` / `StandInZoneEquipmentService` and no currency restore. So every admission logs an error and releases the slot: no class type is registered, no stats or items are restored, and the Skill System (Story 011) never spawns a skill store outside tests.

---

## Acceptance Criteria (draft)

- [ ] The zone's `CharacterPersistenceService` holds Character Stats, Leveling, Inventory, Currency and Equipment (real or stand-in), built after Stats / Leveling exist.
- [ ] A player admitted through the composed zone with a loaded record is not released: `ApplyLoadedRecord` returns success.
- [ ] After admission, `LevelingService.TryGetClassType` returns the record's class, and level / XP / base stats match the record.
- [ ] The Skill System spawn listener creates the player's skill store: the joiner's `SkillCooldownSnapshot` carries the class skill ids (no "no skill store" warning, no class-type warning).
- [ ] Inventory / currency / equipment restore: decided at readiness (real services, or stand-ins that accept the restore and log once).

---

## Open Questions (settle at readiness)

- Build Persistence later in S5, or late-bind the services into it? (Persistence is currently the first thing built.)
- Currency restore: real `ICurrencyStateRestore` or a stand-in?
- Which existing composition tests assume the store-only constructor?

---

## Out of Scope

- Persisting skill cooldowns (cooldowns are per zone process, EC-SK-13)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneApplier_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Skill System Story 011 (for the skill-store criterion)
- Unlocks: live zone tests with real characters
