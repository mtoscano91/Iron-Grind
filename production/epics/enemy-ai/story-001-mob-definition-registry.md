# Story 001: Mob Definition, Registry and Startup Validation

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours
> **Readiness**: NEEDS WORK → fixed → READY (2026-11-13; lean, QL-STORY-READY skipped)

## Context

**GDD**: `design/gdd/enemy-ai.md` — MobDefinition Schema, IMobDefinitionRegistry Interface, CR-AI-7, EC-AI-4, EC-AI-12, AC-AI-7, AC-AI-8
**Requirement**: `TR-ai-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: Enemy AI is written in `IronGrind.ServerLogic` from its first story; nothing in it is referenced by a client assembly.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#; IL2CPP note below)
**Engine Notes**: `MobDefinition` must be a `sealed class`, not a struct — IL2CPP AOT does not support `string` fields in value types in every configuration (GDD schema note).

**Control Manifest Rules (this layer)**:
- Required: new systems (Enemy AI) are written in `src/ServerLogic/` from their first story — ADR-012
- Required: gameplay values are data-driven, never hardcoded (coding standards)
- Forbidden: placing an outcome formula in `IronGrind.Foundation` — ADR-012

---

## Acceptance Criteria

- [x] `MobDefinition` (`sealed class`) holds the GDD schema fields **except `LootTableRef`, `GoldMin`, `GoldMax`** (user decision 2026-11-13: Loot Table owns them — `LootTableRegistry` is keyed by `MobTypeID` and `LootTableDefinition` holds `GoldMin`/`GoldMax`; the join key is `MobTypeID`; the GDD schema gets a dated note). `MobTypeID` reuses the existing `IronGrind.LootTableSystem.MobTypeID` (`Invalid` = 0), not a new type.
- [x] New enum `MobTier { Common, Uncommon, Rare, Elite }` (Mob Spawning CR-MS-9) next to `MobDefinition`; `MobDefinition.MobTier` field.
- [x] `IMobDefinitionRegistry.GetDefinition(MobTypeID)` returns the definition, or `null` for an unknown id and for `MobTypeID` 0. Only Enemy AI adds entries (the registry is built once, then read-only).
- [x] **Validator (CR-AI-7, EC-AI-4, EC-AI-12)**: a definition is refused (one error log naming the type and the failed rule; it is not added to the registry) when any of these fail: `AttackWindUpTicks ≥ MIN_WIND_UP_TICKS` (= `ceil(0.6 × TICK_RATE_HZ)` = 12); `MoveSpeed > 0`; `AggroRange > 0`; `LeashRange ≥ AggroRange + 2.0`; `AttackRange ≥ ATTACK_RANGE_MIN` (0.5); `BaseMaxHP > 0`; `BaseAttackPower > 0`; `BaseDefense ≥ 0`; `AttackRecoveryTicks ≥ 0`; `KillXP > 0`; `EnragedChance ∈ [0, 1]`; `EnragedLootQualityBonusTier ∈ [0, 2]`; `EnragedMaxHPMultiplier ∈ [1.0, 3.0]`; `EnragedAttackPowerMultiplier ∈ [1.0, 2.0]`; `EnragedXPMultiplier ∈ [1.0, 3.0]` (F-AI-E-1 ranges); no `float` field is NaN or infinite; `MobTypeID` ≠ 0; no duplicate `MobTypeID` (the second is refused).
- [x] **AC-AI-7**: `AttackWindUpTicks = 11` → error and refused; `= 12` → accepted.
- [x] **AC-AI-8**: `LeashRange = AggroRange` → refused; `LeashRange = AggroRange + 2.0` → accepted. *(GDD AC-AI-8 says `+ 0.01` succeeds, which contradicts CR-AI-7's 2.0 m gap; CR-AI-7 wins — record a GDD wording fix.)*
- [x] `AttackCooldownTicks < 10` is accepted with one warning (GDD: "recommended minimum"), not refused.
- [x] `F-AI-1` helper `EffectiveAttackIntervalTicks(def)` = `WindUp + Recovery + max(0, Cooldown − Recovery)` (AC-AI-20a, also asserted in Story 005).
- [x] GDD `enemy-ai.md` MobDefinition Schema gets a dated note: `LootTableRef`, `GoldMin`, `GoldMax` are owned by Loot Table (keyed by `MobTypeID`), not by `MobDefinition`.
- [x] **Performance**: startup only (zone static-data load, S2); no per-tick cost.

---

## Implementation Notes

- Files under `src/ServerLogic/EnemyAI/`, namespace `IronGrind.ServerLogic.EnemyAI` (matches `IronGrind.ServerLogic.Navigation`).
- `MIN_WIND_UP_TICKS = ceil(0.6 × ServerTickLoop.TICK_RATE_HZ)` (`src/ServerLogic/Networking/TickLoop/ServerTickLoop.cs`, 20 Hz → 12); owned by Enemy AI (Hit Detection GDD note); do not hardcode 12.
- Tests live in the `IronGrind.Foundation.EditModeTests` assembly, which already has `InternalsVisibleTo` from `IronGrind.ServerLogic`.
- The validator returns a list of issues (pattern: `LootTableValidator` / `LootTableValidationIssue`) so Story 002's loader can log and skip; the registry is built from the accepted definitions only.
- `KillXP` and the Enraged multipliers are read by other systems (XP via Story 012); this story only stores and validates them. Gold and loot table data stay in `LootTableDefinition`.

---

## Out of Scope

- Story 002: loading definitions from an asset and the global tuning config
- Story 007: the Enraged roll itself

---

## QA Test Cases

*Written at `/story-readiness` 2026-11-13 (lean). Each case starts from one valid factory definition and changes one field.*

- **AC-1 (AC-AI-7)**: wind-up minimum
  - Given: valid definition, `AttackWindUpTicks = 11`
  - When: validated and the registry is built
  - Then: exactly one issue (wind-up rule), type absent from the registry, one error log naming the type and rule
  - Edge cases: `= 12` → no issue, `GetDefinition` returns it
- **AC-2 (AC-AI-8, EC-AI-4, EC-AI-12)**: leash gap
  - Given: `LeashRange = AggroRange` → refused; `AggroRange + 1.99` → refused; `AggroRange + 2.0` → accepted
- **AC-3**: one refusal per rule (parametrised)
  - `MoveSpeed` 0; `AggroRange` 0; `AttackRange` 0.49 (0.5 accepted); `BaseMaxHP` 0; `BaseAttackPower` 0; `BaseDefense` −1 (0 accepted); `AttackRecoveryTicks` −1 (0 accepted); `KillXP` 0; `EnragedChance` −0.01 and 1.01 (0 and 1 accepted); `EnragedLootQualityBonusTier` −1 and 3; `EnragedMaxHPMultiplier` 0.99 / 3.01; `EnragedAttackPowerMultiplier` 0.99 / 2.01; `EnragedXPMultiplier` 0.99 / 3.01; NaN and +∞ for each float field; `MobTypeID.Invalid`
  - Then: each yields exactly one issue for that rule and the type is refused
- **AC-4**: duplicate id
  - Given: two valid definitions with the same `MobTypeID`
  - Then: the first is in the registry, the second refused with one error
- **AC-5**: unknown id
  - `GetDefinition` of an unregistered id and of `MobTypeID.Invalid` → null
- **AC-6**: cooldown recommendation
  - `AttackCooldownTicks = 9` → accepted with one warning; `10` → accepted, no warning
- **AC-7 (F-AI-1, AC-AI-20a)**: (12, 6, 10) → 22; (12, 6, 4) → 18; (12, 0, 10) → 22

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Definition_tests.cs` — must exist and pass

**Status**: [x] Created — 22 [Test] + 70 [TestCase] = 92 cases, all pass

---

## Dependencies

- Depends on: None
- Unlocks: Story 002, Story 003, Story 007

---

## Completion Notes

**Completed**: 2026-11-13
**Criteria**: 10/10 passing (performance criterion verified by design: startup-only `Build` / `Validate`, nothing ticks)
**Deviations** (advisory): (1) `LootTableRef`, `GoldMin`, `GoldMax` left out of `MobDefinition` — Loot Table owns them, keyed by `MobTypeID` (user decision at readiness; GDD schema dated note); (2) GDD AC-AI-8 "`+0.01` succeeds" contradicts CR-AI-7's 2.0 m gap — CR-AI-7 implemented, recorded in the same GDD note; (3) extra validator rule from code review: an undefined `MobTier` value is refused (`MobTier` has explicit values 0–3).
**Test Evidence**: Logic: `tests/EditMode/EnemyAI/EnemyAI_Definition_tests.cs` (22 [Test] + 70 [TestCase] = 92 cases). Batch EditMode 7446 / 7317 passed / 0 failed / 129 skipped.
**Code Review**: Complete — unity-specialist APPROVED WITH SUGGESTIONS, qa-tester GAPS (none blocking); all applied ("fix all"): `MobTier` values + rule, `Validate` split into three helpers, culture-invariant messages, `Build` sole-logger doc, `MobDefinition` doc fixes, +28 test cases.
**Tech debt**: none.
