# Story 002: Enemy AI Config and MVP Mob Roster Asset

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Tuning Knobs (Global Server Constants, Per-Mob Parameters), CR-AI-7
**Requirement**: `TR-ai-001`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary; ADR-009 (zone static data load at S2, consumer of existing code); ADR-003 (`RETURN_SPEED_CAP` owned by Navigation)
**ADR Decision Summary**: Server-only config and data live in `IronGrind.ServerLogic`; the zone loads and validates static data at startup step S2 and a failure aborts startup.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: ScriptableObject assets; follow the existing config pattern (`NavigationConfig` asset + `NavigationConfigValues.FromAsset`, `SkillSystemConfigValues`). `[SerializeField]` on fields only (Unity 6.3).

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven — never hardcoded
- Required: Enemy AI code in `src/ServerLogic/` — ADR-012

---

## Acceptance Criteria

- [x] `EnemyAIConfig` asset + immutable `EnemyAIConfigValues` with `AGGRO_SCAN_INTERVAL_TICKS` (5, [1, 10]), `PURSUIT_REPOSITION_THRESHOLD` (1.0, [0.5, 3.0]), `RETURN_ARRIVAL_THRESHOLD` (0.5, [0.25, 2.0]), `DEATH_LINGER_TICKS` (30, [10, 100]). Out-of-range values are clamped with one warning each; `Defaults` matches the GDD defaults. **Not** redefined here (readiness 2026-11-13): `RETURN_SPEED_CAP` is read from `NavigationConfigValues.ReturnSpeedCap` (ADR-003, Navigation owns it, range [1, 10]); `ATTACK_RANGE_MIN` stays `MobDefinitionValidator.ATTACK_RANGE_MIN` (fixed floor, Story 001).
- [x] `MobRoster` asset holding serializable mob entries that map to `MobDefinition`; a loader validates each entry with Story 001's validator (through `MobDefinitionRegistry.Build`) and builds the `IMobDefinitionRegistry` from the accepted ones (refused types logged, not fatal). An empty registry after load (including a null or empty roster) is fatal (S2 returns false).
- [x] The MVP mob roster asset `Assets/data/Mobs/MobRoster.asset` is authored with the four provisional Bandicoot rows in the table below. A test loads the shipped asset and asserts all four build into the registry with no refusal and no warning. The loot-table coverage check ("each roster `MobTypeID` has a loot table") is **moved to TD-107** (user decision 2026-11-13: no loot table assets ship until Loot is wired into the zone).
- [x] `ZoneMobDataLoader : IZoneStaticDataLoader` joins the S2 loader chain in `ZoneServerHost` the same way as `ZoneItemDataLoader` (wraps the rest of the chain and forwards to it; `[SerializeField] MobRoster` on the host; exposes `LoadedMobRegistry`, null until a successful load). The composition tests that use `StandInZoneStaticDataLoader` stay unchanged and pass.

### MVP mob roster (provisional — user decision 2026-11-13)

No mob stats existed in `entities.yaml`, `design/balance/` or any GDD; these are placeholders derived from a level-1 player (`character-stats.md`: AP 30, Def 20, MaxHP 400; `BASE_MOVE_SPEED` 5.0; `XpThreshold[2]` = 200) and F-DC-1 (`max(AP×0.05, AP−Def)`). Tune at Mob Spawning or a balance pass.

| MobTypeID | DisplayName | MobTier | BaseMaxHP | BaseAttackPower | BaseDefense | AttackRange | MoveSpeed | AggroRange | LeashRange | WindUp | Recovery | Cooldown | KillXP |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Bandicoot | Common | 55 | 28 | 2 | 1.5 | 3.5 | 6 | 12 | 12 | 6 | 10 | 20 |
| 2 | Wild Bandicoot | Uncommon | 120 | 40 | 6 | 1.5 | 3.8 | 7 | 14 | 14 | 6 | 12 | 45 |
| 3 | Bandicoot Scavenger | Rare | 260 | 60 | 12 | 1.8 | 4.0 | 8 | 16 | 16 | 8 | 14 | 100 |
| 4 | Giant Bandicoot | Elite | 600 | 95 | 20 | 2.5 | 3.6 | 9 | 18 | 18 | 10 | 16 | 250 |

All rows: `EnragedChance` 0.07, `EnragedMaxHPMultiplier` 1.5, `EnragedAttackPowerMultiplier` 1.3, `EnragedXPMultiplier` 1.5, `EnragedLootQualityBonusTier` 1 (GDD defaults). Checks: a level-1 player kills a Bandicoot in 2 hits (28 per hit vs 55 HP, matches `mob-spawning.md` "two hits"); 10 Bandicoots = L1 → L2; every mob slower than the player; every row has Leash ≥ Aggro + 2 and WindUp ≥ 12.

---

## Implementation Notes

- Mob values come from the provisional table above (readiness 2026-11-13); registered as `MVP_MOB_ROSTER` in `design/registry/entities.yaml`.
- Keep `Defaults` as a code constant only for tests and the no-asset composition path (same as `SkillSystemConfigValues.Default`).
- `ZoneMobDataLoader` follows `ZoneItemDataLoader`: own load first, then `_rest.LoadAndValidate()`; forward any extra interfaces the chain relies on (see the `ZoneItemDataLoader` remark about wrappers).
- Performance: loading runs once at startup S2; no per-tick cost.

---

## Out of Scope

- Story 013: wiring the registry into the composition and replacing `StandInNavMobSpeedSource`
- Mob Spawning epic: which mob types spawn where
- Loot tables for the roster mob types and the coverage check (TD-107)
- Final balance of the roster values

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-13.*

- **AC-1 config**
  - Given `Defaults`, then the four knobs equal 5, 1.0, 0.5, 30.
  - For each knob: a value below its minimum is clamped to the minimum and a value above its maximum to the maximum, one warning per clamped knob; an in-range value is kept with no warning.
  - Given several knobs out of range at once, then one warning each (count matches).
  - Edge: non-finite float knob (NaN, ±∞) → falls back to the default with one warning (same as `NavigationConfigValues`).
- **AC-2 loader**
  - Given a roster with one valid and one invalid entry, then the registry holds the valid one, the invalid one is refused with one error log, and load returns true.
  - Given a roster whose every entry is invalid, or an empty roster, or a null roster, then load returns false and `LoadedMobRegistry` is null.
  - Given a duplicate `MobTypeID`, then the first is kept (Story 001 behaviour).
- **AC-3 shipped asset**
  - Load `Assets/data/Mobs/MobRoster.asset`; the registry has exactly MobTypeIDs 1–4, no error and no warning, and each definition's fields equal the table row.
- **AC-4 chain**
  - Given a passing roster and a failing rest-of-chain loader, then `LoadAndValidate` returns false.
  - Given a failing roster, then it returns false (and does not leave a registry behind).
  - Given both passing, then true and `LoadedMobRegistry` is non-null.
  - Existing composition tests with `StandInZoneStaticDataLoader` pass unchanged (batch EditMode run).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Config_tests.cs` — must exist and pass

**Status**: [x] Created and passing — 52 cases (fixture `EnemyAI_Config_Tests` 52/52; batch EditMode 2026-11-13: 7498 / 7369 passed / 0 failed / 129 skipped)

---

## Dependencies

- Depends on: Story 001 (Complete)
- Unlocks: Story 013

## Completion Notes
**Completed**: 2026-11-13
**Criteria**: 4/4 passing (none deferred)
**Deviations**: Advisory only — (1) roster values are provisional placeholders (user decision, `MVP_MOB_ROSTER` in entities.yaml); (2) a mob-data failure now fails S2 before the class/skill and item loaders run (S2 failure is fatal either way); (3) loot-table coverage check moved to TD-107 (user decision at readiness).
**Test Evidence**: Logic — `tests/EditMode/EnemyAI/EnemyAI_Config_tests.cs` (52 cases, all pass)
**Code Review**: Complete (`/code-review` 2026-11-13, CHANGES REQUIRED → all 13 fixes applied); LP-CODE-REVIEW and QL-TEST-COVERAGE skipped (lean)
