# Story 001: Skill Data Types and Class Skill Lists

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — SkillDefinition Schema, OQ-CS-1 Resolution (a); `design/gdd/class-system.md` — CD-4
**Requirement**: `TR-sk-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: three assemblies; wire types and their codecs are `IronGrind.Foundation`, authoritative logic is `IronGrind.ServerLogic`. Result types follow their producer unless the client receives them.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `[SerializeField]` on fields only (compile error on properties in 6.3). Unity does not serialize a `readonly struct` array on a ScriptableObject: store raw `uint` (Class System CD-4).

**Control Manifest Rules (this layer)**:
- Required: classify each type with the ADR-012 question order; the first "yes" decides
- Required: gameplay values in data assets, never hardcoded
- Forbidden: `[SerializeField]` on properties

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] **AC-SK-33**: `SkillID` is a `readonly struct` wrapping a single `uint` field; reflection: value type, exactly one field of type `uint`, readonly. `SkillID(0)` is `SkillID.Invalid`.
- [ ] **AC-SK-35**: `ClassDefinition.SkillList` is declared as `uint[]`, and `IClassRegistry.GetClassSkills(classType)` returns `IReadOnlyList<SkillID>`.
- [ ] `GetClassSkills` returns an empty list (never null) when the class has no list, a null array or an unknown class type (Class System EC-CS-8).
- [ ] `SkillDefinition` has every field in the schema table as a public field; `SkillEffectType` is a flags enum `{PhysicalDamage=1, MagicalDamage=2, InstantHeal=4, ApplyBuff=8, ApplyDebuff=16}`; `TargetConstraint {SingleHostile=0, SingleFriendly=1, Self=2}`; `DamageStat {AttackPower=0, Intelligence=1, Strength=2}`; `SkillCastRejectionCode` values match `entities.yaml` (1–9, 0 unused).
- [ ] `SkillDatabase.TryGetDefinition(SkillID, out SkillDefinition)` returns false for `SkillID.Invalid` and for an unknown id.
- [ ] `SkillSystemConfig` holds `MANA_COST_MULTIPLIER` (default 1.0, range [0.5, 2.0]) and `CAST_STALE_TICK_TOLERANCE` (default 5, range [2, 10]); out-of-range asset values are clamped with a warning at load.

---

## Implementation Notes

- **Placement (ADR-012):** `SkillID`, `ClassType`, `TargetConstraint`, `SkillEffectType` and `SkillCastRejectionCode` go in `src/Foundation/SkillSystem/`: the client receives them in wire messages. `SkillDefinition`, `DamageStat`, `SkillDatabase` and `SkillSystemConfig` go in `src/ServerLogic/SkillSystem/`: `SkillDefinition` embeds a `BuffDefinition` and `BuffID`, which are `ServerLogic` (`ServerLogic/StatusEffects/BuffDefinition.cs`, `ServerLogic/CharacterStats/BuffID.cs`). Client skill-bar display data (names, icons) comes with Combat UI.
- **`ClassType`:** no enum exists today. `IClassRegistry.TryGetClass` and `LevelingService` take a `byte classType`. Add `ClassType : byte {Warrior=0, Healer=1}` and keep the `byte` APIs. `GetClassSkills` takes a `byte`, matching `TryGetClass`, and callers cast.
- **`ClassDefinition`** (`src/Foundation/LevelingSystem/ClassDefinition.cs`) is a `readonly struct` with a constructor. Add `public readonly uint[] SkillList` as the last constructor parameter, defaulting to null so existing callers still compile. `ClassRegistry` (`ClassRegistry.cs`) implements `GetClassSkills`. It wraps the raw values into a cached `SkillID[]` per class, built once, so a call does not allocate.
- **`SkillDefinition`** is a `[Serializable]` struct with public fields (GDD schema). `DamageContext` is the existing `IronGrind.DamageCalculation.DamageContext` (`PhysicalSkill = 1`, `MagicalSkill = 2`). `CooldownTicks` is an `int` in [0, 6000].
- **`SkillDatabase : ScriptableObject`** holds a `SkillDefinition[]`. It builds an id → index lookup once (in `OnEnable` or a lazy init); duplicate ids log an error. It stores `ID` as raw `uint` for the same serialization reason. Asset path: `Assets/Data/Skills/` (the `Assets/Data/items` precedent). The GDD's `assets/data/` paths mean this folder.
- **`SkillSystemConfig : ScriptableObject`**, with a code `Default` for tests (the `HitDetectionConfig.Default` precedent).
- Tests construct definitions in code; the 20-skill roster asset is Story 012.

---

## Out of Scope

- Story 002: per-entity `SkillInstance` store
- Story 012: the roster asset and the class skill lists' contents
- Updating `entities.yaml` for `ClassType` / `DamageStat`: do it here if the types are new to the registry (check first)

---

## QA Test Cases

- **AC-SK-33**: reflection on `typeof(SkillID)`: `IsValueType`, one instance field of type `uint`, `IsReadOnly` attribute present; `SkillID.Invalid.Value == 0`; equality and hash by value.
- **AC-SK-35**: reflection: the `SkillList` field type is `uint[]`; the `GetClassSkills` return type is `IReadOnlyList<SkillID>`.
- **Empty list**: a class with a null `SkillList`, an empty array, or an unknown class byte → `Count == 0`, not null.
- **Wrapping**: `SkillList = {3, 7}` → `GetClassSkills` returns `SkillID(3), SkillID(7)` in order; two calls return the same cached instance; no allocation on the second call.
- **Enums**: each enum's underlying type and member values match the GDD and `entities.yaml` (one `[TestCase]` per member).
- **Database lookup**: a known id → true with that definition; `Invalid` → false; an unknown id → false; duplicate ids → error logged (`LogAssert.Expect`).
- **Config**: `Default` values; a multiplier of 3.0 is clamped to 2.0 and a tolerance of 1 to 2, each with a warning.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_DataTypes_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002, 004, 012
