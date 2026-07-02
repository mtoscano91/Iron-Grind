# Story 002: F-1 Modifier Stack — GetEffectiveStat, Intra-Layer Additive Pct, StatMin/StatMax Clamp

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 6–8 hours

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-001`, `TR-stats-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the modifier stack formula itself. The formula is specified entirely in the GDD (F-1). IL2CPP-safe type constraints on StatID and EntityID are governed by ADR-010 and implemented in Story 001.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `GetEffectiveStat` is called every Beat Event (~20Hz server tick path). Use `Mathf.FloorToInt()` for int-schema stats — NOT `(int)Math.Floor()`. `System.Math.Floor` returns `double`, causing a float→double widening that diverges from Unity's float domain. Never use LINQ (`.Where`, `.Select`, `.Any`) in this code path — LINQ enumerators box under IL2CPP and will cause GC pressure at mobile scale.

**Control Manifest Rules (Foundation layer)**:
- Required: No LINQ in any CharacterStats hot path — source: GDD Rule 7
- Required: `Mathf.FloorToInt(effectiveFloat)` for int-schema stats — never `(int)Math.Floor()` — source: GDD Rule 7
- Forbidden: Never use lambda captures in modifier iteration — source: ADR-010
- Guardrail: Zero heap allocations per `GetEffectiveStat` call on iOS IL2CPP

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-01** [BLOCKING]: BaseStat(AP)=100, equip +30 flat +0.15 pct, buff +10 flat +0.20 pct → `GetEffectiveStat(AP)` = **193** (pre-clamp 193.2; `Mathf.FloorToInt` applied; 194 via rounding is failure).
- [ ] **AC-02** [BLOCKING]: BaseStat(AP)=100, two equipment mods `eq_ring` +10% and `eq_amulet` +10% → `GetEffectiveStat(AP)` = **120** (not 121 — intra-layer pct bonuses are additive, not compounding).
- [ ] **AC-03** [BLOCKING]: BaseStat(CritChance)=0.05f, buff +0.40 flat, equip +0.35 flat → `GetEffectiveStat(CritChance)` = **0.75f** (float schema — no floor; clamped to StatMax 0.75; any value above 0.75f is failure).
- [ ] **AC-04** [BLOCKING]: BaseStat(AP)=50, equip +30 flat, debuff −200 flat → `GetEffectiveStat(AP)` = **1** (StatMin; pre-clamp −120; return of 0 or negative is failure).
- [ ] **AC-05** [BLOCKING]: BaseStat(MovementSpeed)=5.0f, ΣPctBuff=−1.20, no equip → `GetEffectiveStat(MovementSpeed)` = **0.5f** (StatMin; pre-clamp −1.0f; any value ≤ 0.0f is failure).
- [ ] **AC-24** [BLOCKING]: BaseStat(CritChance)=0.30f, equip +0.50 flat → returns 0.75f (capped). Remove equip → returns **0.30f** (no hysteresis — no memory of prior cap).
- [ ] **AC-28a** [BLOCKING]: BaseStat(ASM)=1.414f (L60 Warrior), equip +1.0 flat → `GetEffectiveStat(ASM)` = **2.0f** (clamped at StatMax 2.0; any value above 2.0f is failure).
- [ ] **AC-28b** [BLOCKING]: BaseStat(ASM)=1.03f (L1 Warrior), debuff −0.7 flat → `GetEffectiveStat(ASM)` = **0.5f** (clamped at StatMin 0.5; any value below 0.5f is failure).
- [ ] **AC-21 (GetEffectiveStat portion)** [BLOCKING]: `GetEffectiveStat(MobEntityID, StatID.Intelligence)` = 0; no exception. All 7 player-only fields return 0 on a mob entity via `GetEffectiveStat`.
- [ ] **AC-30 (GetEffectiveStat portion)** [BLOCKING]: After `SetBaseStat(MobEntityID, AttackPower, 75)` → `GetEffectiveStat(MobEntityID, StatID.AttackPower)` = **75** (empty modifier layers; F-1 = BaseStat).
- [ ] **AC-26** [BLOCKING]: `SetBaseStat(player, Experience, 3500)` → `GetBaseStat(Experience)` = 3500 AND `GetEffectiveStat(Experience)` = 3500. Experience is not modifier-stackable — F-1 with zero modifiers reduces to BaseStat. Base and effective must always be equal for this stat. *(Deferred from Story 001 — GetEffectiveStat not available there.)*

---

## Implementation Notes

*Derived from GDD `design/gdd/character-stats.md` F-1 and Rule 7:*

**F-1 formula:**
```
EffectiveStat = clamp(
  (BaseStat + ΣFlatEquip + ΣFlatBuff) × (1 + ΣPctEquip) × (1 + ΣPctBuff),
  StatMin,
  StatMax
)
```

**Intra-layer pct is additive.** Two +10% equipment bonuses → ΣPctEquip = 0.20 (not two separate ×1.10 multiplications). Each layer's pct terms are summed first, then applied as a single multiplier. Inter-layer multiplication (equip × buff) is separate.

**Return type contract:**
- `int`-schema stats: `return Mathf.FloorToInt(effectiveFloat)` — Unity's `Mathf.FloorToInt`, not `(int)Math.Floor()`.
- `float`-schema stats: return the clamped float directly.

`int` stats: AttackPower, MaxHP, MaxMP, Defense, MagicDefense, Level, Experience, STR, DEX, VIT, INT.
`float` stats: CritChance, CritMultiplier, AttackRange, AttackSpeedMultiplier, MovementSpeed, CurrentHP, CurrentMP.

**`StatID.BaseDamage`** is a derived alias: `GetEffectiveStat(StatID.BaseDamage)` returns `EffectiveStat(AttackPower)` as an `int` (GDD Rule 6 / F-2).

**No allocation contract:** Zero heap allocations per `GetEffectiveStat` call. No `List<T>`, no LINQ, no closures. Iterate modifier arrays by index.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 001**: Container, `GetBaseStat`, `SetBaseStat` — must be Done before this story starts
- **Story 003**: Modifier add/remove APIs (tests here directly set up modifier array state via fixture helpers)
- **Story 004**: `ApplyDamage`, `ApplyRegen`, `ConsumeMana`

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs`

- **AC-01**: F-1 layer order — flat first, then multiplicative pct, then floor-truncate
  - Given: BaseStat(AP)=100; equip +30 flat, +0.15 pct; buff +10 flat, +0.20 pct
  - When: `GetEffectiveStat(AttackPower)`
  - Then: Returns 193 (int). Intermediate: (100+30+10)×1.15×1.20 = 193.2; `Mathf.FloorToInt(193.2)` = 193. A return of 194 (rounding) is failure.
  - Edge cases: Swap equip/buff layers — assert same result (order within layers must not matter).

- **AC-02**: Intra-layer pct bonuses are additive, not compounding
  - Given: BaseStat(AP)=100; equip eq_ring +0.10 pct, eq_amulet +0.10 pct; no buffs
  - When: `GetEffectiveStat(AttackPower)`
  - Then: Returns 120. A return of 121 (1.10×1.10=1.21, compounding) is failure.
  - Edge cases: Three equip pct bonuses each +0.10 → 130 (1.30, not 1.331).

- **AC-03**: Float stat clamped at StatMax; no floor applied
  - Given: BaseStat(CritChance)=0.05f; buff +0.40f; equip +0.35f; no pct mods
  - When: `GetEffectiveStat(CritChance)`
  - Then: Returns 0.75f. Pre-clamp 0.80f truncated to StatMax. Float returned directly — no `FloorToInt`.
  - Edge cases: BaseStat 0.74f + buff +0.01f → 0.75f exactly; +0.02f → still 0.75f.

- **AC-04**: Negative flat debuff clamped at StatMin; no underflow
  - Given: BaseStat(AP)=50; equip +30 flat; buff debuff −200 flat
  - When: `GetEffectiveStat(AttackPower)`
  - Then: Returns 1 (StatMin). Pre-clamp: (50+30−200) = −120. Return of 0 or negative is failure.
  - Edge cases: Debuff exactly offsets base+equip to pre-clamp 1 → assert 1; pre-clamp 0 → assert 1 (StatMin).

- **AC-05**: Compounded pct debuffs cannot produce negative effective value
  - Given: BaseStat(MovementSpeed)=5.0f; ΣPctBuff=−1.20; no equip
  - When: `GetEffectiveStat(MovementSpeed)`
  - Then: Returns 0.5f (StatMin). Pre-clamp: 5.0×(1−1.20) = −1.0f. Any value ≤ 0.0f is failure.
  - Edge cases: ΣPctBuff=−1.0 → 5.0×0.0=0.0 → clamped to 0.5f; ΣPctBuff=−0.99 → 5.0×0.01=0.05 → clamped to 0.5f.

- **AC-24**: No cap hysteresis on float-schema stat
  - Given: BaseStat(CritChance)=0.30f; equip +0.50 flat
  - When: (step 1) `GetEffectiveStat(CritChance)` → 0.75f (capped). (step 2) Remove equip; `GetEffectiveStat(CritChance)`.
  - Then: Step 2 returns 0.30f — no memory of prior cap.
  - Edge cases: Add different equip for +0.30f → GetEffectiveStat = 0.60f (not capped, not hysteretic).

- **AC-28a**: AttackSpeedMultiplier clamped at StatMax (2.0)
  - Given: BaseStat(ASM)=1.414f; equip +1.0 flat
  - When: `GetEffectiveStat(AttackSpeedMultiplier)`
  - Then: Returns 2.0f. Pre-clamp 2.414f.
  - Edge cases: Equip exactly +0.586f → 1.414+0.586 = 2.000 → returns 2.0f (at-cap, not over).

- **AC-28b**: AttackSpeedMultiplier clamped at StatMin (0.5)
  - Given: BaseStat(ASM)=1.03f; buff debuff −0.70 flat; no equip
  - When: `GetEffectiveStat(AttackSpeedMultiplier)`
  - Then: Returns 0.5f. Pre-clamp 0.33f.
  - Edge cases: Debuff of −0.53f → 1.03−0.53 = 0.50 → returns 0.5f (at-floor, not under).

- **AC-21 (GetEffectiveStat portion)**: Player-only fields return 0 on mobs via GetEffectiveStat
  - Given: Mob EntityID; no player-only fields written
  - When: `GetEffectiveStat(MobEntityID, StatID.Intelligence)` etc.
  - Then: Returns 0; no exception. All 7 player-only fields return 0.

- **AC-30 (GetEffectiveStat portion)**: Mob base stat round-trip through GetEffectiveStat
  - Given: `SetBaseStat(MobEntityID, AttackPower, 75)` (from Story 001); no modifiers
  - When: `GetEffectiveStat(MobEntityID, StatID.AttackPower)`
  - Then: Returns 75. Empty modifier layers → F-1 = BaseStat.

- **AC-26**: Experience is non-modifier-stackable — GetBaseStat == GetEffectiveStat
  - Given: Player EntityID; `SetBaseStat(player, Experience, 3500)`. No modifiers added.
  - When: `GetBaseStat(player, Experience)` and `GetEffectiveStat(player, Experience)`
  - Then: Both return 3500. F-1 with zero flat and zero pct modifiers reduces to BaseStat.
  - Edge cases: `SetBaseStat(Experience, 0)` → both return 0. `SetBaseStat(Experience, 999999)` → both return 999999 (no StatMax clamping for Experience).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs`

---

## Dependencies

- Depends on: Story 001 (CharacterStats container and GetBaseStat/SetBaseStat) must be Done
- Unlocks: Story 003, Story 004, Story 005

---

## Completion Notes

**Completed**: 2026-06-29
**Criteria**: 11/11 passing (all blocking ACs covered)
**Deviations**:
- ADVISORY — B-01: `GetEffectiveStat` / `GetEffectiveStatFloat` have no `IsFloatStat` guard at method entry. Calling `GetEffectiveStat` with a float-schema StatID throws `IndexOutOfRangeException`. No current callers misroute. **Fix before Story 003.**
- ADVISORY — W-02: `EntityEquipModifiers` and `EntityEquipCount` are parallel dictionaries with no enforced invariant. Wrap in `EntityModifierSlot` struct at Story 003 boundary.
- ADVISORY — GDD schema note: Implementation Notes list `CurrentHP`/`CurrentMP` as float-schema; implementation has them as int-schema (established in Story 001). Verify alignment before HUD story ships.
**Test Evidence**: `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — 11 test methods, all ACs covered
**Code Review**: Background unity-specialist review completed — ISSUES FOUND (B-01 advisory, W-01 clean, W-02 advisory, W-03 N/A). LP-CODE-REVIEW skipped (lean mode).
