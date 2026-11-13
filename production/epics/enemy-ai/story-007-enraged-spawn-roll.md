# Story 007: Enraged Spawn Roll and Stat Scaling

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-12, F-AI-E-1, EC-AI-11, AC-AI-13, AC-AI-14, AC-AI-15
**Requirement**: `TR-ai-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-013: Server Random Provider
**ADR Decision Summary**: One process-level `IRandomProvider` is injected; the Enraged roll is exactly one `NextDouble()` per spawn. No per-mob `System.Random` (overrides that sentence of CR-AI-12).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: Enraged spawn roll = one `NextDouble()` per spawn on the injected `IRandomProvider` — ADR-013
- Forbidden: seeding a `System.Random` at mob spawn; `UnityEngine.Random` — ADR-013
- Forbidden: pity / streak mechanics — ADR-013

---

## Acceptance Criteria

- [ ] At spawn, exactly one `NextDouble()` call; Enraged when `value < EnragedChance`.
- [ ] **AC-AI-13**: 0.0 with chance 1.0 → Enraged; 1.0-returning double (test double) with 0.99 → not; 0.0 with chance 0.0 → not.
- [ ] **Stat seeding at spawn (both variants)** through an injected stat writer over Character Stats `SetBaseStat`: `MaxHP`, `AttackPower`, `Defense` from the definition; `CurrentHP` = MaxHP.
- [ ] **AC-AI-14**: Enraged with BaseMaxHP 100 × 1.5 → MaxHP 150; AttackPower = `RoundToInt(Base × 1.3)`; CurrentHP = 150.
- [ ] **AC-AI-15**: no buff modifier is added for MaxHP or AttackPower; only `SetBaseStat` calls (spy).
- [ ] `IsEnraged` readable; `EnragedKillXP = RoundToInt(KillXP × EnragedXPMultiplier)` exposed for Story 012; `TierShift = IsEnraged ? EnragedLootQualityBonusTier : 0` exposed for Story 011 (EC-AI-11: applies regardless of damage record).

---

## Implementation Notes

- `IRandomProvider` is in `src/ServerLogic/Randomness/IRandomProvider.cs`; tests use a scripted fake.
- Rounding: `Mathf.RoundToInt` (banker's rounding on .5) as the GDD formula states — note it in the test for e.g. 2.5.
- Check how CurrentHP is set in Character Stats (`SetBaseStat(CurrentHP)` vs a dedicated method) — readiness.

---

## Out of Scope

- Story 008: when the seeding runs in the zone (spawn handler) and TD-102

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Enraged_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 008
