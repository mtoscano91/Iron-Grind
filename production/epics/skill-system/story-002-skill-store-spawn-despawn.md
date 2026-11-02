# Story 002: Per-Entity Skill Store, Spawn and Despawn

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-20, States and Transitions (SkillInstance), OQ-CS-1 (b), EC-SK-12
**Requirement**: `TR-sk-002`

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: the Skill System is a new system and lives in `IronGrind.ServerLogic` from its first story.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: dependency injection over singletons; narrow interfaces for what is read from other systems
- Guardrail: no allocation on the per-tick path; per-entity storage allocated at zone creation or on spawn

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] **AC-SK-10**: a skill with `UnlockLevel = N` has `IsUnlocked == false` after `OnEntitySpawned` for an entity at level `N - 1`.
- [ ] **AC-SK-13**: for a freshly spawned level-1 entity, every one of the 10 slots with `UnlockLevel > 1` has `IsUnlocked == false`.
- [ ] **AC-SK-47**: after `OnEntitySpawned(entity, Warrior)` at level 1, every skill with `UnlockLevel == 1` is unlocked and every skill with `UnlockLevel > 1` is locked.
- [ ] **AC-SK-48**: `OnEntitySpawned` with a class whose `GetClassSkills` is empty gives a 0-length skill array, logs a warning, and does not throw (EC-SK-12).
- [ ] Slots are positionally aligned with `GetClassSkills(classType)`; every slot starts with `CooldownExpiryTick = 0`.
- [ ] `OnEntityDespawned` releases the entity's store; a later spawn of the same `EntityID` starts with no cooldowns (`CooldownExpiryTick` reset, States table).
- [ ] A class skill id with no `SkillDatabase` definition keeps its slot locked forever, with an error logged once at spawn.

---

## Implementation Notes

- **`ISkillSystem`** (`entities.yaml`): `OnEntitySpawned(EntityID, ClassType)`, `OnEntityDespawned(EntityID)`, `OnLevelUp(EntityID, int)`. `OnLevelUp` is implemented in Story 003; it is a no-op stub here.
- **`SkillSystemService`** in `src/ServerLogic/SkillSystem/` implements `ISkillSystem`. It holds a fixed array of per-entity records, sized `MAX_PLAYERS_PER_ZONE` and indexed through the zone slot index (the `IEntitySlotIndex` pattern in `ServerLogic/StatusEffects/`). Each record holds a `SkillInstance[10]` allocated once at zone creation, plus a used-count. The 0-length case (AC-SK-48) is a used-count of 0. `SkillInstance` is a struct: `SkillID SkillID`, `bool IsUnlocked`, `uint CooldownExpiryTick`.
- **Level at spawn**: read through a narrow seam, `ISkillLevelSource.GetLevel(EntityID)`, with an adapter over `CharacterStats.GetEffectiveStat(entity, StatID.Level)`. Unlock check: `level >= def.UnlockLevel` (the States table's `OnEntitySpawned` rows).
- **Class type at spawn**: comes as the `OnEntitySpawned` parameter. Story 011 gets it from `LevelingService` (which caches it in `RegisterPlayerClassType` from `CharacterPersistenceService`); it needs a public lookup there.
- **Mobs** never call `OnEntitySpawned` (players only at MVP, CR-SK-17).
- Expose read access for tests and later stories, e.g. `TryGetSkill(EntityID, int slot, out SkillInstance)` and `TryFindSlot(EntityID, SkillID, out int slot)`, both `internal` with `InternalsVisibleTo` (the Auto-Attack `AutoAttackFaceOutput` precedent).
- Spawn and despawn wiring (`IZonePlayerEntryListener`, `CompositeZoneSlotReleaseListener`) is Story 011.

---

## Out of Scope

- Story 003: unlock on level-up and `SkillUnlockNotification`
- Story 011: hooking `OnEntitySpawned` / `OnEntityDespawned` to zone entry and slot release

---

## Performance

Store memory is fixed at zone creation (`MAX_PLAYERS_PER_ZONE × 10 × ~12 bytes`, about 6 KB at 50 players). Spawn does one `GetClassSkills` call (cached, Story 001) and ten database lookups; nothing per tick.

---

## QA Test Cases

- **AC-SK-10**: spawn at level 4 with a skill of `UnlockLevel` 5 → locked; at level 5 → unlocked (boundary pair).
- **AC-SK-13 / AC-SK-47**: a 10-skill fake class with unlock levels {1, 1, 5, 15, 20, 25, 35, 42, 50, 58}, spawned at level 1 → slots 0–1 unlocked, 2–9 locked; slot order matches `GetClassSkills`.
- **AC-SK-48**: an empty class → no throw, 0 slots, `LogAssert.Expect(Warning)`.
- **Cooldowns start at 0**: all slots `CooldownExpiryTick == 0` after spawn.
- **Despawn / respawn**: write a cooldown into a slot (test-only setter), despawn, spawn the same id → `CooldownExpiryTick == 0`.
- **Missing definition**: a class list naming an id absent from the database → that slot locked at any level, one error logged.
- **Spawn twice without despawn**: the second spawn re-initializes the record (document the chosen behaviour) and does not leak a second record.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_SkillStore_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 005, 010
