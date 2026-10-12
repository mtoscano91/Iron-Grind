# Story 005: `LoadCharacter` — Equipment Re-registration

> **Epic**: Character Persistence
> **Status**: Blocked — written 2026-10-12. **The Equipment System has no code** (no `EquipmentSystem` folder under `src/ServerLogic`; its epic is Ready with design gate OQ-EQS-9 open), and the GDD contradicts itself on who holds the gear slots in memory (open decision 1).
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-3 step 4; EC-CP-1 (`ItemDatabaseMiss`); EC-CP-2; AC-CP-7, AC-CP-11, AC-CP-12, AC-CP-24. `design/gdd/enhancement-system.md` — the `GetFlatBonus(level, baseFlatBonus, gearTier)` and `GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)` signatures.
**Requirement**: `TR-cp-003`

**ADR Governing Implementation**: ADR-011 Decision 3; ADR-010.
**ADR Decision Summary**: equipment modifier values are not persisted; they are recomputed on load from the Item Database and the enhancement bonus provider.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: cross-system communication is a direct call on an injected interface — ADR-010
- Forbidden: reading or writing live game state after the first `await` in the persistence layer — ADR-011

---

## Acceptance Criteria

- [ ] **AC-CP-11**: for a record with gear in slot 0, all the `SetBaseStat` calls of step 3 are made before the first `AddEquipmentModifier` call.
- [ ] **Enhancement level check**: an occupied gear slot whose `EnhancementLevel` is above `MAX_ENHANCEMENT_LEVEL` (10) makes the load return `CorruptRecord`, and `IEnhancementBonusProvider` is not called with that level. Level 10 loads.
- [ ] **Re-registration**: for each occupied slot the load calls `TryGetItem` on the Item Database, then `GetFlatBonus` / `GetElementalBonus` on `IEnhancementBonusProvider`, then `AddEquipmentModifier`. Empty slots make no call.
- [ ] **AC-CP-7**: after the load, `GetEffectiveStat(AttackPower)` equals `GetBaseStat(AttackPower)` plus the weapon's flat bonus for its loaded enhancement level.
- [ ] **AC-CP-12 / EC-CP-1**: a slot whose `ItemID` the Item Database does not know is removed from the in-memory gear, one critical alert is logged, `Code` becomes `ItemDatabaseMiss`, and the load goes on to the next slot and completes; the client is not disconnected.
- [ ] **AC-CP-24**: a record saved with `CurrentHP = 650` whose MaxHP falls to 500 because of an item miss loads with `CurrentHP = 500` and no error (the clamp is Story 004's; this test joins the two).
- [ ] **Two misses**: with two unknown items the code is still `ItemDatabaseMiss`, both slots are removed, two alerts.

---

## Implementation Notes

- `CharacterStats.AddEquipmentModifier(EntityID, StatID, EquipmentModifierEntry)`, `IEnhancementBonusProvider` and the Item Database exist in code.
- The removed slot is corrected in the database "on the next save" (EC-CP-1): nothing is written during the load.
- `Code = ItemDatabaseMiss` is a success-with-a-warning: the load returns it with a valid `LastZoneID`. A caller that treats every non-`Success` code as a failed login would reject the player — state this in `CharacterLoadCode`'s doc comment.

**Open decisions for `/story-readiness`**

1. **Who holds the gear slots in memory.** The Interactions table of the GDD says "Save: reads `GearSlots` directly from record — Equipment System does not provide a save API"; CR-CP-10 step 3 says "read `{ItemID, EnhancementLevel}` from Equipment System". With no Equipment System in code, either this system keeps the loaded gear until that system exists, or this story waits for it.
2. **The base bonus and gear tier inputs** of `GetFlatBonus` / `GetElementalBonus`: which Item Database fields supply them, and which `StatID` each modifier goes to.
3. **Alerts.** There is no critical-alert service in code; `Debug.LogError` with a fixed prefix is what the networking code uses.

---

## Out of Scope

- Story 004: the clamp itself
- Story 007: writing gear back on save
- The Equipment System's own equip / unequip rules

---

## QA Test Cases

- **Order** — Given gear in slot 0, Then the recorded calls show every `SetBaseStat` before the first `AddEquipmentModifier`.
- **Level bound** — Level 10 → loads; level 11 → `CorruptRecord`, provider call count 0.
- **Effective stat** — Given a +7 weapon, Then effective AttackPower = base + the provider's flat bonus for 7.
- **Item miss** — Given slot 2 unknown, Then slot 2 empty in memory, one alert, `ItemDatabaseMiss`, the other slots registered. Edge: two misses.
- **Clamp after a miss** — Given saved HP 650 and a missed item that lowers MaxHP to 500, Then HP 500.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadEquipment_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 003, 004; **the Equipment System epic's stories, or open decision 1 taken the other way (blocking)**
- Unlocks: Story 016 (AC-CP-26)
