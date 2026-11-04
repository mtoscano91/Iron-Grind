# Story 012: MVP Skill Roster and BuffIDs

> **Epic**: Skill System
> **Status**: Complete
> **Layer**: Core
> **Type**: Config/Data
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours (raised from 3 at `/story-readiness` 2026-11-04: `BuffDefinitionData` authoring form and `ClassDatabase` asset added)

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

- [x] `Assets/Data/Skills/SkillDatabase.asset` holds the 20 roster skills with the GDD tables' unlock level, effect flags, target constraint, mana cost, cooldown ticks, multiplier and `DamageBaseStat` (Healer Divine Strike, Smite Armor, Judgment = Strength).
- [x] A `ClassDatabase` asset (`Assets/Data/Classes/ClassDatabase.asset`, savable entries converted to `ClassDefinition`) holds the Warrior and Healer definitions: auto-allocations from `class-system.md` CD-2 (Warrior Str 2 / Dex 1 / Vit 1 / Int 0, free 1; Healer 0 / 0 / 1 / 2, free 2) and a `SkillList` of the class's 10 ids in roster order. Registering it in the zone is Story 013.
- [x] **AC-SK-36**: the Healer has exactly one skill with `UnlockLevel == 1` and the `MagicalDamage` flag (Holy Smite).
- [x] **AC-SK-38**: the Warrior has at least one skill with `UnlockLevel == 1` and the `PhysicalDamage` flag (Slash).
- [x] **CR-SK-14 / CR-SK-15**: every Warrior `ManaCost` is in [50, 150]; every Healer `ManaCost` in [80, 200].
- [x] **CR-SK-22**: per class, at least 2 skills unlock in L1–10 (the first at L1, the second at L5 or earlier), at least 3 in L11–30, at least 5 in L31–60.
- [x] Every value is in its schema range (`UnlockLevel` [1, 60], `CooldownTicks` [0, 6000], `RangeUnits` [0, 20], multipliers [0, 5]); ids are unique and non-zero.
- [x] **Decision 4 / OQ-SK-6**: each of the 10 buff and debuff skills (Rend, Rush, Shield Bash, War Cry, Savage Blow, Mortal Wound, Prayer, Bless, Smite Armor, Holy Shield) has a unique, non-zero `BuffID` and a filled `BuffDefinition` matching the approved table in Readiness Decisions below (user approval 2026-11-04). `BuffID` gains `Invalid = 0` and the 10 members.
- [x] The BuffIDs are recorded in `skill-system.md` (OQ-SK-6 closed) and in `entities.yaml`, and they don't collide with any `BuffID` already in code (`ServerLogic/CharacterStats/BuffID.cs`).

---

## Readiness Decisions (`/story-readiness` 2026-11-04, user-approved)

**Buff table** (`pct` is a fraction per `character-stats.md` F-1; 20 ticks = 1 s):

| Skill | BuffID | Modifiers | HpRegenPerTick | DurationTicks |
|---|---|---|---|---|
| Rend | 1 | Defense pct −0.15 | 0 | 100 (5 s = its cooldown) |
| Rush | 2 | MovementSpeed pct +0.40 | 0 | 60 (GDD) |
| Shield Bash | 3 | MovementSpeed pct −0.30 | 0 | 60 |
| War Cry | 4 | AttackPower pct +0.20 | 0 | 200 (GDD) |
| Savage Blow | 5 | Defense pct −0.20 | 0 | 100 |
| Mortal Wound | 6 | AttackPower pct −0.15 (on the target) | 0 | 200 |
| Prayer | 7 | none | 2.0 | 100 (200 HP total ≈ 50% of tier-1 MaxHP 400, `status-effects.md` HpRegenPerTick anchor) |
| Bless | 8 | AttackPower pct +0.10, Defense pct +0.10 | 0 | 600 (30 s = its cooldown) |
| Smite Armor | 9 | Defense pct −0.15 | 0 | 120 (6 s = its cooldown) |
| Holy Shield | 10 | Defense pct +0.25, MagicDefense pct +0.25 | 0 | 200 |

All flat bonuses 0, `MpRegenPerTick` 0. The BuffID enum value names are the skill names (`Rend = 1` … `HolyShield = 10`). Rend and Smite Armor are separate BuffIDs, so they stack additively (−30% Defense with both).

**`/dev-story` decision (2026-11-04, user):** the GDD Healer table put Minor Heal at L8, which breaks CR-SK-22 (second skill by L5). Minor Heal moved to **L5** in the asset and the GDD; CR-SK-22 unchanged.

**GDD fixes to make during `/dev-story`** (with the OQ-SK-6 close):
- Rend: "damage via Status Effects" dropped. Status Effects has no damage-over-time (regen is positive only), so Rend is a pure armor debuff.
- Mortal Wound: "−healing received" replaced by −15% AttackPower on the target. No `StatID` exists for healing received, and mobs don't heal at MVP. Record "healing received" as post-MVP.
- Prayer: "5-tick duration" (250 ms) → 5 s / 100 ticks. Note: the roster lists Prayer as INT-scaled, but a `BuffDefinition` is flat, so the heal does not scale with INT; flag for the balance pass.
- Visual section line "No HoT skills exist at MVP": stale, fix.

**Ranges** (`RangeUnits`): Warrior `SingleHostile` skills 3.0 (base `AttackRange`, `character-stats.md`); Healer `SingleHostile` and `SingleFriendly` skills 12.0 (Tuning Knobs mobile ceiling); `Self` skills (Rush, War Cry, Holy Shield) 0.

**Ids**: Warrior 101–110, Healer 201–210, in roster order.

**Buff authoring form** (Story 001 carry): `BuffDefinition` is a `readonly struct` and Unity does not save readonly fields. Add a savable `BuffDefinitionData` (`ServerLogic/SkillSystem`: duration, regen values, `StatModifierEntry[]`-style modifier array; precedent `StatModifierEntry` in Foundation `ItemDatabase`) as a `SkillDefinition` field, and have `SkillDatabase.BuildLookup` build each runtime `BuffDefinition` from it once. The cast path (`SkillEffectExecutionStep`, `in cast.Definition.BuffDefinition`) stays unchanged. Validate at load: modifier count ≤ `MAX_STATS_PER_BUFF`, no stat twice in one buff (`ApplyResult.InvalidStat`), `DurationTicks` in [1, 6000], buff skills have a non-Invalid `BuffID` equal to the data's id.

**Class data**: `ClassDefinition` (Foundation, `LevelingSystem`) is also a `readonly struct`. Add a `ClassDatabase` ScriptableObject with savable entries that convert to `ClassDefinition` and can fill a `ClassRegistry`. Placement: next to `ClassDefinition` in Foundation is the expected spot (the client respec screen reads the same registry); confirm against the ADR-012 allow-list at `/dev-story`. Loading both assets in the zone (replacing the empty `ClassRegistry` and the in-code `SkillDatabase` fallbacks in `ZoneServerComposition`) is **Story 013**, not this story.

**Advisory**:
- Slash's ×0.45 is below the Tuning Knobs safe range [0.5, 5.0] but inside the schema [0, 5]; keep the table value.
- Shield Bash's slow only affects a mob if mob movement reads effective `MovementSpeed`; check at `/dev-story` and note the result (no change to this story's data).
- OQ-SK-4 verified: `character-stats.md` uses INT = 246 for the L60 Healer.

---

## Implementation Notes

- **Production class definitions (found at Story 001 `/story-readiness`, 2026-11-02):** nothing in production calls `ClassRegistry.RegisterClass`. Only tests and the DevHarness do, and the zone server builds `new LevelingService(XpThresholdTable.Values)` with an empty registry. So filling `SkillList` is not enough: until the Warrior and Healer `ClassDefinition`s (stat auto-allocations from `class-system.md`, plus the skill lists) are registered by the composition, `GetClassSkills` is empty in a real zone and every player spawns with no skills. Leveling's level-up stat allocation reads the same registry. Settle at this story's readiness whether it adds that source (recommended: a class data asset or table registered in `ZoneServerComposition`), or whether it is already tracked elsewhere. *Settled 2026-11-04: this story adds the `ClassDatabase` asset; Story 013 registers it in the zone (see Readiness Decisions).*
- **Readiness work**: several buff values are not in the GDD. Rend and Smite Armor's armor reduction, Shield Bash's slow, Savage Blow's defense reduction, Mortal Wound's healing-received reduction, Bless's AP/DEF and Holy Shield's defense/magic resistance have no numbers. `/story-readiness` should draft them (systems-designer), with a duration for each, and get the user's approval before `/dev-story`. Check that each modifier maps to a `StatID` the Status Effects code supports. "Healing received" may not exist as a stat; if it doesn't, raise it then.
- **Range**: the roster tables give no `RangeUnits`. Propose melee and caster defaults at readiness (for example the auto-attack range for Warrior melee skills and a caster range ≤ 12.0 for Healer, per the Tuning Knobs note on mobile screens), with `Self` skills at 0.
- **Ids**: suggested scheme Warrior 101–110, Healer 201–210 in roster order (stable, readable in logs). Confirm at readiness.
- **OQ-SK-4** (Healer INT 246 at L60): verify against `character-stats.md` while authoring; if the number differs, note it for the balance pass. It changes no data here.
- **Level-distribution design note**: the GDD suggests moving some L31+ skills earlier. Keep the table values; the note is for playtesting.
- **War Cry / Rogue note**: keep +20% AP at MVP; the note applies when Rogue is designed.
- **Data test**: a test loads the real asset (`AssetDatabase.LoadAssetAtPath` in EditMode) and checks every rule above. This is the Config/Data smoke check, automated.
- **Prayer HoT**: check that the Status Effects `BuffDefinition` supports a per-tick heal; if it doesn't, raise it at readiness. The visual section's "No HoT skills exist at MVP" line is stale; fix it in the GDD.
- **Story 007 carry (2026-11-04)**: `SkillDamageExecutionStep` derives the damage context from the effect flags (CR-SK-5) and never reads `SkillDefinition.DamageContext`. Check at load that the authored value agrees with the flags (PhysicalDamage → PhysicalSkill, MagicalDamage → MagicalSkill), or drop the field. Also validate `DamageMultiplier` in [0.0, 5.0] (the step fails safe to 0 on NaN or a negative product).

---

## Out of Scope

- Skill icons, display names on the client, tooltips: Combat UI
- Balance tuning after playtest
- Loading `SkillDatabase.asset` / `ClassDatabase.asset` in the zone and registering the classes: Story 013

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
- **Buff values**: one `[TestCase]` per buff row asserting modifiers, regen and duration against the Readiness Decisions table; the built `BuffDefinition` from `SkillDatabase` matches the authored `BuffDefinitionData`.
- **Ranges**: Warrior hostile 3.0, Healer 12.0, `Self` 0.
- **Class data**: `ClassDatabase` has Warrior and Healer with the CD-2 auto-allocations, `Str + Dex + Vit + Int + Free == 5`, each field in [0, 2], and `SkillList` equal to the roster ids in order; the filled `ClassRegistry` returns them through `TryGetClass` / `GetClassSkills`.
- **Authoring-form validation**: a duplicate stat in one buff, a zero duration, or a buff skill with `BuffID.Invalid` is reported at load (test with an in-memory database, `LogAssert`).

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Roster_tests.cs` — must exist and pass (automated smoke check of the asset)

**Status**: [x] Created and passing (57 cases; EditMode 6437 / 6308 / 0 failed / 129 skipped, run 2026-11-04 18:22:28Z after the code-review fixes)

---

## Dependencies

- Depends on: Story 001; Story 008 (buff application shape) before the buff data is final
- Unlocks: Story 013 (zone loads the roster and class data)

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 9/9 passing (all automated in `SkillSystem_Roster_tests.cs`, 57 cases; the doc criterion checked against `skill-system.md` OQ-SK-6 and `entities.yaml`)
**Deviations**: Advisory, no tech debt logged: (1) Minor Heal L8 → L5 in the asset and the GDD (user decision at `/dev-story`, CR-SK-22 second skill by L5). (2) The `DamageContext`-vs-flags check is a roster-test assertion, not a load-time error (in-memory test skills keep the default context). (3) Shield Bash's slow is inert until Enemy AI moves mobs by effective `MovementSpeed` (no mob movement in ServerLogic yet). (4) The zone does not load either asset yet: Story 013 (Draft); end-to-end also needs Character Persistence 018 (TD-090). (5) Balance notes recorded in the GDD: Prayer's heal is flat (not INT-scaled); Slash ×0.45 below the Tuning Knobs 0.5. (6) No `production/qa/smoke-*.md`: the automated roster test is the smoke check, as the story's Test Evidence states. Extra files, required by the change: `ClassDatabase` / `ClassDefinitionEntry` in Foundation with 2 allow-list entries. Assets and `.cs.meta` GUIDs written by hand; the real folder is `Assets/data/` (lowercase).
**Test Evidence**: Config/Data: `tests/EditMode/SkillSystem/SkillSystem_Roster_tests.cs`
**Code Review**: Complete (`/code-review` 2026-11-04, APPROVED WITH SUGGESTIONS; all 5 applied)
