# Story 013: Zone Loads the Roster and Class Data

> **Epic**: Skill System
> **Status**: Complete — `/story-done` 2026-11-04 (COMPLETE WITH NOTES)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours (4h + CD-10 validation)

## Context

**GDD**: `design/gdd/skill-system.md` — Skill Roster, CR-SK-12 (snapshot of the caster's 10 skills); `design/gdd/class-system.md` — LC-1 (Leveling reads auto-allocation from `IClassRegistry`), CD-10 (startup validation), AC-CS-16 / 17 / 18
**Requirement**: TR-sk-011 (the 20-skill roster as `SkillDatabase` data), TR-sk-010 (10-slot `SkillCooldownSnapshot` on zone join). No composition TR exists. Class System has no TR entries or epic yet; CD-10 and AC-CS-18 are quoted below.

> CD-10: "On initialization, the Class System validates the `ClassRegistry` asset: no duplicate `ClassType` values; all `ClassType` values are recognized enum members; each field of `AutoAllocIncrement` within [0, 2]; `autoAllocIncrement.Total() + FreePointsPerLevel == 5`."
> AC-CS-18: "GIVEN the `ClassRegistry` asset is absent … THEN the spawn system halts with a critical error in both dev and release builds."

**ADR Governing Implementation**: ADR-009 Amendment 1 Decision 2 (zone startup S0–S10), ADR-012 (Server/Client Assembly Boundary), ADR-014 (dispatch and tick order; unchanged here)
**ADR Decision Summary**: ADR-009 A1 S2: "Static data ready: load and validate ItemDatabase, ClassRegistry, LootTableRegistry, DamageCalculationConfig, EnhancementConfig (and any later static config). Any failure → S-fail." S5: "game systems, each given … the static data from S2". `ZoneServerHost` lives in `IronGrind.ServerLogic`, in a scene only the Dedicated Server build includes (ADR-012).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: A `[SerializeField]` ScriptableObject reference on a scene MonoBehaviour is included in the build with the scene; `ZoneServerHost._registry` (`ZoneSceneRegistry`) already works this way in `Assets/Scenes/Server/ZoneServer.unity`. No `Resources`, no Addressables, no post-cutoff API. `[SerializeField]` on fields only (Unity 6.3).

**Control Manifest Rules**: Server code in `src/ServerLogic/` (ADR-012); the composition root is the only place that builds the object graph; doc comments on public APIs.

---

## Readiness Decisions (user, 2026-11-04)

1. **Asset source**: two `[SerializeField]` fields on `ZoneServerHost` (`SkillDatabase`, `ClassDatabase`), set in `ZoneServer.unity`. The host passes them to a real static-data loader (implements `IZoneStaticDataLoader`) in place of `StandInZoneStaticDataLoader`; the loader still logs the stand-in line for the static data it does not own yet (items, loot tables, damage and enhancement config).
2. **Failure**: a missing or invalid asset fails S2 in **all** builds (ADR-009 S2, AC-CS-18): one error line naming the asset and the check (never a value dump), then the normal S-fail path. On the server this replaces CD-10's "release builds log and continue"; class-system.md CD-10 gets a note during `/dev-story`.
3. **Validation at S2**: CD-10's four checks on each `ClassDatabase` entry (no duplicate `ClassType`; a defined `ClassType` member; each of Str/Dex/Vit/Int auto-allocation in [0, 2]; their sum + `FreePointsPerLevel` == 5), plus: every id in each class's `SkillList` resolves in `SkillDatabase`, and each class has exactly 10 skills.
4. **Fallbacks kept**: S5 uses, in order, the test overrides (`ClassRegistryOverride`, `SkillDatabaseOverride`), then the loader's validated data, then today's silent empty registry / in-code database. The last is reachable only with the stand-in loader (tests); production fails at S2 first. Existing composition tests are unchanged.

---

## Acceptance Criteria

- [x] **AC-1**: With valid assets on the loader, S2 succeeds and S5 builds Leveling and the Skill System over a `ClassRegistry` filled from `ClassDatabase` and over the given `SkillDatabase` (`ClassRegistryInUse` holds Warrior and Healer; `SkillService` resolves skill 101).
- [x] **AC-2**: `ClassRegistryInUse.TryGetClass(Warrior)` and `(Healer)` return true, and `GetClassSkills` returns 101–110 and 201–210 in authored order.
- [x] **AC-3**: When `ClassRegistryOverride` / `SkillDatabaseOverride` are set, they are used instead of the loader's data.
- [x] **AC-4**: A null `SkillDatabase` or `ClassDatabase` on the loader makes `LoadAndValidate` return false and logs one error naming the missing asset; the composition fails at S2 (`startup failed at S2: StepReturnedFalse`) and requests teardown.
- [x] **AC-5**: Each CD-10 violation (duplicate `ClassType`, undefined `ClassType`, an auto-allocation field outside [0, 2], sum + free points ≠ 5) makes `LoadAndValidate` return false with one error naming the class and the check.
- [x] **AC-6**: A class skill id missing from `SkillDatabase`, or a class with other than 10 skills, makes `LoadAndValidate` return false with one error naming the class and the check.
- [x] **AC-7**: The real `Assets/data/Classes/ClassDatabase.asset` and `Assets/data/Skills/SkillDatabase.asset` pass `LoadAndValidate`.
- [x] **AC-8**: `ZoneServer.unity`'s `ZoneServerHost` references both assets (scene YAML check, or an EditMode test loading the scene asset's serialized fields).
- [x] **AC-9**: A player admitted through the composed zone with loader data (class type registered directly, as in the Story 011 composition tests, until Character Persistence Story 018 / TD-090) gets a `SkillCooldownSnapshot` with their class's 10 skill ids.

---

## Implementation Notes

- New loader in `src/ServerLogic/ZoneServer/` (name at implementation, e.g. `ZoneStaticDataLoader`): constructor takes the two assets; `LoadAndValidate` validates, builds the `ClassRegistry` with `ClassDatabase.RegisterAll`, and exposes the registry and database for S5. How S5 reads them (a small interface checked on `_staticDataLoader`, or a constructor parameter) is decided at implementation; keep `CreateWithRealBackend` callers in tests compiling.
- Duplicate detection must run on the entries (`ClassDatabase.GetEntry`), not on the registry: `RegisterAll` lets a later entry replace an earlier one.
- The S2 error line follows the existing S0 rule: names the asset / class / check, never the asset path or data values.
- Do not destroy the loaded assets in the Dispose pass (only `ownedDatabase`, the in-code fallback, is destroyed).
- GDD during `/dev-story`: class-system.md CD-10 note (the zone server fails S2 in all builds, ADR-009 S2).

**Performance**: no per-tick impact; validation and registry building run once at S2.

---

## Out of Scope

- Authoring the data (Story 012)
- The Persistence applier (Character Persistence Story 018, TD-090)
- A `SkillSystemConfig` asset (the zone keeps `SkillSystemConfigValues.Default`)
- Loaders for ItemDatabase, LootTableRegistry, DamageCalculationConfig, EnhancementConfig (still the stand-in log)
- A Class System epic / spawn-system halt (CD-10 here is the zone S2 check only)

---

## QA Test Cases

- Loader with both real assets → true, no error log; registry Warrior/Healer; skill lists in order.
- Loader with null `SkillDatabase` / null `ClassDatabase` → false, one error each (`LogAssert.Expect`).
- In-memory `ClassDatabase` (test seam) with: duplicate Warrior; `(ClassType)99`; Str = 3; sum + free = 6; a skill id 999; 9 skills → each false with its error.
- Composition with the loader (no overrides) → `ClassRegistryInUse` from the loader; with overrides → overrides win.
- Composition with a failing loader → S2 S-fail line, teardown requested, no S5 construction.
- Entry through the composed zone → snapshot with the class's 10 ids.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_ZoneRosterData_tests.cs` — must exist and pass

**Status**: [x] Created — 32 cases, passing

---

## Dependencies

- Depends on: Story 012 (Complete); Story 011 (Complete)
- Related: Character Persistence Story 018 (AC-9 end to end)
- Unlocks: None within the epic

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 9/9 passing (automated; full EditMode 6469 / 6340 / 0 failed / 129 skipped, run started 18:39:47Z)
**Deviations**: None blocking. Advisory: AC-9 is met through the composition test path that registers the class type directly, until Character Persistence Story 018 (TD-090, already registered); no Windows Server build run yet to confirm the assets ship (scene-referenced, so included); the loader's error lines are asserted word for word. Out of the listed files: optional `staticDataLoader` parameter on the shared `WiRig` test rig (no existing test changed). Code-review additions beyond the readiness checks: a class skill list may not repeat a skill id; `Loaded*` stay null when the remaining static data fails.
**Test Evidence**: Integration: `tests/EditMode/SkillSystem/SkillSystem_ZoneRosterData_tests.cs` (32 cases)
**Code Review**: Complete — `/code-review` 2026-11-04, APPROVED WITH SUGGESTIONS, all 5 applied (unity-specialist + qa-tester); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean)
