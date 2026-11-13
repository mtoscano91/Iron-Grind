# Story 002: Enemy AI Config and MVP Mob Roster Asset

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Tuning Knobs (Global Server Constants, Per-Mob Parameters), CR-AI-7
**Requirement**: `TR-ai-001`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary; ADR-009 (zone static data load at S2, consumer of existing code)
**ADR Decision Summary**: Server-only config and data live in `IronGrind.ServerLogic`; the zone loads and validates static data at startup step S2 and a failure aborts startup.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: ScriptableObject assets; follow the existing config pattern (`NavigationConfig` asset + `NavigationConfigValues.FromAsset`, `SkillSystemConfigValues`).

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven — never hardcoded
- Required: Enemy AI code in `src/ServerLogic/` — ADR-012

---

## Acceptance Criteria

- [ ] `EnemyAIConfig` asset + immutable `EnemyAIConfigValues` with `AGGRO_SCAN_INTERVAL_TICKS` (5, [1, 10]), `PURSUIT_REPOSITION_THRESHOLD` (1.0, [0.5, 3.0]), `RETURN_ARRIVAL_THRESHOLD` (0.5, [0.25, 2.0]), `DEATH_LINGER_TICKS` (30, [10, 100]), `ATTACK_RANGE_MIN` (0.5, fixed), `RETURN_SPEED_CAP` (6.0, ADR-003). Out-of-range values are clamped with one warning each; `Defaults` matches the GDD defaults.
- [ ] `MobRoster` asset holding `MobDefinition` entries; a loader validates each entry with Story 001's validator and builds the `IMobDefinitionRegistry` from the accepted ones (refused types logged, not fatal). An empty registry after load is fatal (S2 returns false).
- [ ] The MVP mob roster asset is authored with at least the starting-zone mob types (values from `design/registry/entities.yaml` / `design/balance` where defined; otherwise the GDD defaults: WindUp 12, Recovery 6, Cooldown 10, `EnragedChance` 0.07, multipliers 1.5 / 1.3 / 1.5, bonus tier 1). Each roster `MobTypeID` has a table in the existing loot table registry (checked by test against the shipped assets; `MobDefinition` carries no `LootTableRef` — Story 001 decision 2026-11-13).
- [ ] The loader plugs into the zone's static-data load (`IZoneStaticDataLoader`, S2) or a sibling seam, without breaking the composition tests that use the stand-in.

---

## Implementation Notes

- Readiness must confirm where the MVP mob values come from (`entities.yaml` mob entries, Loot Table GDD mob list) — do not invent numbers that already exist elsewhere.
- Keep `Defaults` as a code constant only for tests and the no-asset composition path (same as `SkillSystemConfigValues.Default`).

---

## Out of Scope

- Story 013: wiring the registry into the composition and replacing `StandInNavMobSpeedSource`
- Mob Spawning epic: which mob types spawn where

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Config_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 013
