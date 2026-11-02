# Story 012: MVP Skill Roster and BuffIDs

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Config/Data
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — Skill Roster (Warrior, Healer), CR-SK-14, CR-SK-15, CR-SK-22, OQ-SK-4, OQ-SK-6; `design/gdd/status-effects.md` — `BuffDefinition`; `design/gdd/class-system.md` — `SkillList`
**Requirement**: `TR-sk-011`

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: gameplay values are data assets, not code. `SkillDatabase` and its definitions are `IronGrind.ServerLogic` (Story 001).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: assets authored with `[SerializeField]` fields only; create them from an editor script or by hand, and commit the `.asset` and `.meta`.

**Control Manifest Rules (this layer)**:
- Required: balance values link to their source table or formula
- Required: data-driven, never hardcoded

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] `Assets/Data/Skills/SkillDatabase.asset` holds the 20 roster skills with the GDD tables' unlock level, effect flags, target constraint, mana cost, cooldown ticks, multiplier and `DamageBaseStat` (Healer Divine Strike, Smite Armor, Judgment = Strength).
- [ ] The Warrior and Healer `ClassDefinition.SkillList` hold their 10 ids in roster order.
- [ ] **AC-SK-36**: the Healer has exactly one skill with `UnlockLevel == 1` and the `MagicalDamage` flag (Holy Smite).
- [ ] **AC-SK-38**: the Warrior has at least one skill with `UnlockLevel == 1` and the `PhysicalDamage` flag (Slash).
- [ ] **CR-SK-14 / CR-SK-15**: every Warrior `ManaCost` is in [50, 150]; every Healer `ManaCost` in [80, 200].
- [ ] **CR-SK-22**: per class, at least 2 skills unlock in L1–10 (the first at L1, the second at L5 or earlier), at least 3 in L11–30, at least 5 in L31–60.
- [ ] Every value is in its schema range (`UnlockLevel` [1, 60], `CooldownTicks` [0, 6000], `RangeUnits` [0, 20], multipliers [0, 5]); ids are unique and non-zero.
- [ ] **Decision 4 / OQ-SK-6**: each of the 10 buff and debuff skills (Rend, Rush, Shield Bash, War Cry, Savage Blow, Mortal Wound, Prayer, Bless, Smite Armor, Holy Shield) has a unique, non-zero `BuffID` and a filled `BuffDefinition`. Values come from the roster notes (for example Rush +40% movement speed for 60 ticks, War Cry +20% AP for 200 ticks, Prayer a 5-tick HoT) and the gaps are proposed for user approval at `/story-readiness`.
- [ ] The BuffIDs are recorded in `skill-system.md` (OQ-SK-6 closed) and in `entities.yaml`, and they don't collide with any `BuffID` already in code (`ServerLogic/CharacterStats/BuffID.cs`).

---

## Implementation Notes

- **Production class definitions (found at Story 001 `/story-readiness`, 2026-11-02):** nothing in production calls `ClassRegistry.RegisterClass`. Only tests and the DevHarness do, and the zone server builds `new LevelingService(XpThresholdTable.Values)` with an empty registry. So filling `SkillList` is not enough: until the Warrior and Healer `ClassDefinition`s (stat auto-allocations from `class-system.md`, plus the skill lists) are registered by the composition, `GetClassSkills` is empty in a real zone and every player spawns with no skills. Leveling's level-up stat allocation reads the same registry. Settle at this story's readiness whether it adds that source (recommended: a class data asset or table registered in `ZoneServerComposition`), or whether it is already tracked elsewhere.
- **Readiness work**: several buff values are not in the GDD. Rend and Smite Armor's armor reduction, Shield Bash's slow, Savage Blow's defense reduction, Mortal Wound's healing-received reduction, Bless's AP/DEF and Holy Shield's defense/magic resistance have no numbers. `/story-readiness` should draft them (systems-designer), with a duration for each, and get the user's approval before `/dev-story`. Check that each modifier maps to a `StatID` the Status Effects code supports. "Healing received" may not exist as a stat; if it doesn't, raise it then.
- **Range**: the roster tables give no `RangeUnits`. Propose melee and caster defaults at readiness (for example the auto-attack range for Warrior melee skills and a caster range ≤ 12.0 for Healer, per the Tuning Knobs note on mobile screens), with `Self` skills at 0.
- **Ids**: suggested scheme Warrior 101–110, Healer 201–210 in roster order (stable, readable in logs). Confirm at readiness.
- **OQ-SK-4** (Healer INT 246 at L60): verify against `character-stats.md` while authoring; if the number differs, note it for the balance pass. It changes no data here.
- **Level-distribution design note**: the GDD suggests moving some L31+ skills earlier. Keep the table values; the note is for playtesting.
- **War Cry / Rogue note**: keep +20% AP at MVP; the note applies when Rogue is designed.
- **Data test**: a test loads the real asset (`AssetDatabase.LoadAssetAtPath` in EditMode) and checks every rule above. This is the Config/Data smoke check, automated.
- **Prayer HoT**: check that the Status Effects `BuffDefinition` supports a per-tick heal; if it doesn't, raise it at readiness. The visual section's "No HoT skills exist at MVP" line is stale; fix it in the GDD.

---

## Out of Scope

- Skill icons, display names on the client, tooltips: Combat UI
- Balance tuning after playtest

---

## QA Test Cases

- **Count**: 20 definitions; 10 per class; class lists match the roster order.
- **Per skill**: one `[TestCase]` per roster row asserting unlock, flags, target, mana, cooldown, multiplier and base stat against the GDD table.
- **AC-SK-36 / AC-SK-38**: as written.
- **CR-SK-14 / CR-SK-15**: the cost ranges per class.
- **CR-SK-22**: the bucket counts per class, plus first-at-L1 and second-by-L5.
- **Ranges and ids**: schema ranges; unique non-zero ids.
- **BuffIDs**: the 10 buff and debuff skills have unique non-zero ids, no collision with `BuffID` enum members, and a `BuffDefinition` whose `BuffID` matches.
- **Non-buff skills**: `BuffID.Invalid` on the 10 others.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Roster_tests.cs` — must exist and pass (automated smoke check of the asset)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001; Story 008 (buff application shape) before the buff data is final
- Unlocks: None within the epic
