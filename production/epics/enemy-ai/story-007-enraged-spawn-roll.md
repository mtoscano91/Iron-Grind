# Story 007: Enraged Spawn Roll and Stat Scaling

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-12, F-AI-E-1, EC-AI-11, AC-AI-13, AC-AI-14, AC-AI-15
**Requirement**: `TR-ai-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-013: Server Random Provider
**ADR Decision Summary**: One process-level `IRandomProvider` is injected; the Enraged roll is exactly one `NextDouble()` per spawn. No per-mob `System.Random` (overrides that sentence of CR-AI-12).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (pure server logic; `Mathf.RoundToInt` only).

**Control Manifest Rules (this layer)**:
- Required: Enraged spawn roll = one `NextDouble()` per spawn on the injected `IRandomProvider` — ADR-013
- Forbidden: seeding a `System.Random` at mob spawn; `UnityEngine.Random` — ADR-013
- Forbidden: pity / streak mechanics — ADR-013

**Design decision (2026-11-13, readiness, user):** the roll and the stat seeding live in a separate pure class `MobSpawnSeeder`, not in the `MobController` constructor. CR-AI-12 says "`MobController` accepts `IRandomProvider` at construction"; this is a deviation, recorded as a GDD note at `/story-done`. `MobController` is unchanged by this story; Story 008 calls the seeder from the spawn handler and stores `IsEnraged` in the runtime store.

---

## Acceptance Criteria

- [x] **Roll**: `MobSpawnSeeder.Seed(EntityID, MobDefinition, IRandomProvider, IMobStatWriter)` calls `NextDouble()` exactly once per call — including when `EnragedChance` is 0.0 or 1.0 (no short-circuit; keeps the shared stream deterministic, ADR-013). Enraged when `value < (double)definition.EnragedChance`; `value == chance` → not Enraged.
- [x] **AC-AI-13**: fake returning 0.0 with chance 1.0 → Enraged; fake returning 1.0 with chance 0.99 → not; fake returning 0.0 with chance 0.0 → not.
- [x] **Stat seeding (both variants)** through `IMobStatWriter`, in this order: `SetBaseStat(MaxHP)`, `SetBaseStat(AttackPower)`, `SetBaseStat(Defense)`, then `SetCurrentHP(MaxHP written)`. Not Enraged → `BaseMaxHP`, `BaseAttackPower`, `BaseDefense` unchanged. Defense is never scaled.
- [x] **AC-AI-14**: Enraged with `BaseMaxHP` 100 × 1.5 → MaxHP 150; AttackPower = `Mathf.RoundToInt(BaseAttackPower × EnragedAttackPowerMultiplier)`; CurrentHP = 150. Also verified end to end through `CharacterStatsMobStatWriter` over a real `CharacterStats`: `GetEffectiveStat(MaxHP) == 150`, `GetEffectiveStat(AttackPower)` as computed, `GetCurrentHP == 150`.
- [x] **AC-AI-15**: the spy writer records exactly 3 `SetBaseStat` calls and 1 `SetCurrentHP` call; `IMobStatWriter` has no buff-modifier method, and with the real `CharacterStats` no buff modifier exists for MaxHP or AttackPower.
- [x] **Result**: `Seed` returns a `MobSpawnVariant` (readonly struct) with `IsEnraged`, `EffectiveKillXP = IsEnraged ? Mathf.RoundToInt(KillXP × EnragedXPMultiplier) : KillXP` (for Story 012) and `TierShift = IsEnraged ? EnragedLootQualityBonusTier : 0` (for Story 011; EC-AI-11: a property of the mob, independent of any damage record). Null `definition`, `random` or `writer` → `ArgumentNullException`, no RNG call, no writes.

---

## Implementation Notes

- New files in `src/ServerLogic/EnemyAI/` (namespace as the folder's existing files):
  - `IMobStatWriter` — `void SetBaseStat(EntityID, StatID, int)`; `void SetCurrentHP(EntityID, float)`. Nothing else.
  - `CharacterStatsMobStatWriter` — thin adapter over `IronGrind.CharacterStats.CharacterStats` (`SetBaseStat`, `SetCurrentHP`); null ctor argument → `ArgumentNullException`.
  - `MobSpawnSeeder` (static or sealed, no state) and `MobSpawnVariant` (readonly struct).
- `IRandomProvider` is in `src/ServerLogic/Randomness/IRandomProvider.cs` (`IronGrind.Randomness`); tests use a scripted fake that counts calls.
- The code's stat enum is `StatID` (`src/Foundation/CharacterStats/StatID.cs`: MaxHP 4, AttackPower 5, Defense 6, CurrentHP 8); the GDD's `StatType` means `StatID`.
- **CurrentHP**: `SetBaseStat(StatID.CurrentHP, …)` writes the int array, which `GetCurrentHP` does not read — use `CharacterStats.SetCurrentHP` (`CharacterStats.cs:803`). It clamps to the effective MaxHP, so it must run after the MaxHP write (hence the AC order).
- `SetBaseStat` needs no prior entity registration (it creates the stat array on first write, `CharacterStats.cs:212`).
- Rounding: `Mathf.RoundToInt` rounds .5 to even (as the GDD formula states) — test 3 × 1.5 = 4.5 → 4 and 5 × 1.5 = 7.5 → 8; note it in the test. `100 × 1.3f` = 129.99999… → 130.
- `EnragedChance` is `float`; compare as `double` after widening (`0.99f` → 0.9900000095…, so a 1.0 fake still fails the roll).
- Definition ranges are already enforced by `MobDefinitionValidator`; the seeder does not re-validate.
- GDD note to write at `/story-done`: CR-AI-12 deviation (seeder, not `MobController` ctor, takes the RNG); `StatType` → `StatID`; CurrentHP via `SetCurrentHP`.

---

## Out of Scope

- Story 008: calling the seeder from the zone spawn handler, storing `IsEnraged` in the runtime store, wiring the zone's `CharacterStats` instance, TD-102.
- Story 011: consuming `TierShift` in the loot roll.
- Story 012: awarding `EffectiveKillXP`.
- Any change to `MobController`.

---

## Performance

Runs once per spawn: 1 RNG call + 4 stat writes, no allocation beyond what `CharacterStats` does on an entity's first write. No per-tick cost.

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

1. Fake 0.0, chance 1.0 → `IsEnraged` true (AC-AI-13).
2. Fake 1.0, chance 0.99 → false (AC-AI-13).
3. Fake 0.0, chance 0.0 → false (AC-AI-13).
4. Fake value == chance (e.g. 0.5 with 0.5f) → false.
5. Exactly one `NextDouble()` call for chance 0.0, 0.07 and 1.0; no `NextFloat` / `NextInt` calls.
6. Not Enraged: writes MaxHP = BaseMaxHP, AttackPower = BaseAttackPower, Defense = BaseDefense, `SetCurrentHP(BaseMaxHP)`.
7. Enraged, BaseMaxHP 100 × 1.5 → MaxHP 150, CurrentHP 150 (AC-AI-14).
8. Enraged AttackPower = `RoundToInt(Base × mult)`, e.g. 10 × 1.3 → 13; Defense unchanged.
9. Rounding to even: BaseMaxHP 3 × 1.5 → 4; BaseMaxHP 5 × 1.5 → 8.
10. Write order recorded by the spy: [SetBaseStat MaxHP, SetBaseStat AttackPower, SetBaseStat Defense, SetCurrentHP]; exactly 3 + 1 calls (AC-AI-15).
11. Real `CharacterStats` through `CharacterStatsMobStatWriter`: Enraged → `GetEffectiveStat(MaxHP)` 150, `GetCurrentHP` 150, AttackPower as computed, no buff modifier on MaxHP / AttackPower (AC-AI-14, AC-AI-15).
12. `EffectiveKillXP`: Enraged KillXP 10 × 1.5 → 15; not Enraged → KillXP.
13. `TierShift`: Enraged → `EnragedLootQualityBonusTier` (e.g. 1, and 0 when the definition's tier is 0); not Enraged → 0.
14. Null `definition` / `random` / `writer` → `ArgumentNullException`, zero RNG calls, zero writes; null `CharacterStats` in the adapter ctor → `ArgumentNullException`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Enraged_tests.cs` (includes the adapter test over a real `CharacterStats`) — must exist and pass

**Status**: [x] Created and passing (26/26)

---

## Dependencies

- Depends on: Story 003 (Complete)
- Unlocks: Story 008

---

## Completion Notes
**Completed**: 2026-11-13
**Criteria**: 6/6 passing (none deferred)
**Deviations** (advisory, user accepted):
1. CR-AI-12: the roll lives in `MobSpawnSeeder.Seed`, not the `MobController` constructor (user decision at readiness, 2026-11-13). GDD note to write.
2. GDD `StatType` = code `StatID`; current HP set with `SetCurrentHP` (clamps to effective MaxHP, so it runs after the MaxHP write), not `SetBaseStat(CurrentHP)`. GDD note to write.
3. Caller contract (code review): not from inside an `OnStatChanged` handler; fresh or cleared `EntityID`. Documented on `Seed`; carried forward to Story 008's Implementation Notes.
**Test Evidence**: Logic: `tests/EditMode/EnemyAI/EnemyAI_Enraged_tests.cs` 26/26; batch EditMode 7671 / 7542 passed / 0 failed / 129 skipped (2026-11-14 01:16:41Z).
**Code Review**: Complete (APPROVED WITH SUGGESTIONS, all 4 applied). Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
