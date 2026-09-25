# Damage Calculation

> **Status**: Approved (Pass 2 lean, 2026-05-15)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-28 (Skill System amendment: DamageContext.MagicalSkill added — maps to DamageType.Magical wire value 1; Skill System downstream row updated; AC-DC-F-14b/14c added. Prior: 2026-05-15 — Pass 2 lean revision)
> **Implements Pillar**: Earned Power (primary), Rhythm Mastery (secondary)

## Overview

Damage Calculation is the authoritative resolver of every damage event in Iron Grind. It accepts an attacker identity, a target identity, a base damage value, and a damage type, then applies the complete resolution sequence — Defense mitigation for physical hits, MagicDefense mitigation for elemental hits, optional elemental bonus damage from the attacker's weapon, critical strike evaluation — and returns a single integer FinalDamage for the caller to apply to target health. No caller performs its own damage math; every damage source in the game (auto-attacks, skills) routes through this single resolver. Damage Calculation does not own health state, does not own XP state, and has no knowledge of animations or audio — it computes a number and reports a structured result. Because it is the only path through which damage reaches a target, it is also the single place where kill detection occurs: when FinalDamage would reduce the target's CurrentHP to or below zero, Damage Calculation sets `IsKill = true` in the returned result. Callers (Auto-Attack Combat, Skill System) are responsible for acting on `IsKill` — awarding XP via the Leveling System and applying damage via `CharacterStats.ApplyDamage`, which fires `OnEntityDied` internally per Character Stats EC-08. Damage Calculation does not fire events or award XP; it computes and reports. All damage is integer at system boundaries; intermediate arithmetic is float throughout.

## Player Fantasy

Damage Calculation is infrastructure — the player never sees a formula, never reads a call. What they feel is the math made visible.

**The Honest Verdict** *(Earned Power — primary)*: Every number that floats off an enemy is the game reporting the truth about who you are at this moment. There is no inflation here, no participation-trophy damage curve. A "23" against a mob you can barely scratch is the world telling you: go grind and come back. A "412" crit means every hour invested, every enhancement gamble made, every zone pushed into too early — it's written in that number. This game does not flatter you. The Knight Online veteran's specific emotional memory is this: the numbers meant something. They were honest. When Iron Grind's math is as honest, the player knows that their progress is real — that the difference between then and now was earned, not given.

**The Rhythm Made Visible** *(Rhythm Mastery — secondary)*: A veteran's combat reads like a drum pattern: white hit — white hit — yellow crit — orange skill — white hit. Numbers land on the beat, evenly spaced, with crits punctuating like cymbal hits. A button-masher on identical gear produces numbers that come out clumped, with long dead intervals from wasted ticks. The timing error is written directly into the output — you don't need a stat sheet to see it. Two players fighting side by side produce visibly different number cadences, and anyone watching can read which one has internalized the 1.0-second rhythm. The numbers are the observable evidence of mastery: public, real-time, and honest.

*Pillar alignment*: Earned Power (honest numbers are the direct receipt for genuine effort; a four-digit number is proof of investment, not reward inflation) and Rhythm Mastery (damage output is the legible signal of timing skill, readable by the player themselves and by anyone watching). The social dimension (numbers as public reputation) is a consequence of these two — it requires no separate design.

*Gear inadequacy signal*: When BaseDamage < Defense × (1 − MIN_DAMAGE_FRACTION) and the MIN_DAMAGE_FRACTION floor fires, the resulting low numbers (e.g., 38 against an over-armored boss) are the intentional "go grind and come back" verdict — they are honest, not broken. Legibility amplification for this state (e.g., a "Resisted" label, dampened SFX, visual desaturation of the damage number) is the responsibility of the VFX System and Combat UI, not this formula. This GDD does not define that treatment; it only guarantees the honest number. Constraint on downstream: VFX System and Combat UI GDDs must define how sub-5%-floor hits are visually distinguished from normal hits.

*Design constraint this creates*: Damage number presentation (VFX, audio) must prioritize legibility and timing fidelity over spectacle. Numbers should feel like they *land* on the rhythm. Crits should be visually distinct without overwhelming every auto-attack. This section places requirements on the VFX System and Combat UI GDDs.

## Detailed Design

### Core Rules

**Function Signature and Authority**

1. All damage resolution in Iron Grind routes through a single function: `DamageCalculation(BaseDamage: int, AttackerID: EntityID, TargetID: EntityID, DamageContext: DamageContext) → DamageResult`. This function is called server-side only. No client code path calls it. There is no client-side formula evaluation. **Enforcement**: `DamageCalculation` and all types it owns (`DamageResult`, `DamageContext`) reside in a `ServerLogic.asmdef` assembly excluded from the client build via Unity platform constraints. Using `[Server]` attribute alone is insufficient — it ships code to the client binary. The ADR specifying the complete server/client assembly boundary must be authored before implementation begins.

2. `DamageContext` has three values in MVP: `PhysicalAuto`, `PhysicalSkill`, and `MagicalSkill`. All three pass through an identical resolution pipeline. `DamageContext` is echoed in `DamageResult` for VFX routing — it has no effect on the formula. **MVP rule**: At MVP, all three contexts produce the same formula output for the same `BaseDamage` input. `DamageContext` is a hook for future differentiation only. Any formula divergence must be introduced by the caller's `BaseDamage` computation (e.g., the Skill System uses INT-scaled `BaseDamage` when calling with `MagicalSkill`), not as a formula branch in this function. *The server serializer maps `DamageContext.PhysicalAuto` and `DamageContext.PhysicalSkill` → `DamageType.Physical` (wire value 0); `DamageContext.MagicalSkill` → `DamageType.Magical` (wire value 1); when populating `DamageEvent` and `SelfDamageEvent`. The wire-format `DamageType {Physical=0, Magical=1, True=2}` is a separate type declared in `networking-wire-protocol.md` CR-NET-7.4.*

3. `BaseDamage` is the caller's pre-computed physical base — the result of `GetEffectiveStat(AttackerID, AttackPower)`, already as `int`. Damage Calculation does not re-query AttackPower; it uses the value the caller passes in.

**Resolution Sequence — 11 Steps**

Steps execute in fixed order. No step may be reordered.

- **Step 1 — Read attacker crit stats.** Query `GetEffectiveStat(AttackerID, CritChance)` (float) and `GetEffectiveStat(AttackerID, CritMultiplier)` (float). Cache both for this call only. These values are already clamped to [0.0, 0.75] and [1.0, 3.0] respectively by Character Stats.

- **Step 2 — Read target physical defense.** Query `GetEffectiveStat(TargetID, Defense)` (int).

- **Step 3 — Physical mitigation.** Compute `PhysicalMitigated` (float):
  ```
  PhysicalMitigated = max(BaseDamage × MIN_DAMAGE_FRACTION, BaseDamage − Defense)
  ```
  `MIN_DAMAGE_FRACTION` is a tuning constant (default 0.05). At Defense = 0, the result equals `BaseDamage` exactly — unmitigated, not amplified.

- **Step 4 — Read elemental weapon data.** Query the Equipment System for `AttackerID`'s currently equipped weapon `ItemID`. If the weapon has `ElementType != ElementType.None`, read `ElementalBonus` (int) from the Item Database for that `ItemID`. If the weapon has `ElementType.None` or no weapon is equipped (`ItemID.Invalid`), set `ElementalBonus = 0` and skip Steps 5–6 (elemental contribution is 0.0).

- **Step 5 — Read target magic defense.** Query `GetEffectiveStat(TargetID, MagicDefense)` (int).

- **Step 6 — Elemental mitigation.** Compute `ElementalMitigated` (float):
  ```
  ElementalMitigated = ElementalBonus × max(MIN_ELEMENTAL_FRACTION, 1 − MagicDefense / (MagicDefense + K_MAGIC))
  ```
  `K_MAGIC` and `MIN_ELEMENTAL_FRACTION` are tuning constants (defaults: 200 and 0.10 respectively — see Tuning Knobs). At MagicDefense = 0, the result equals `ElementalBonus` exactly.

- **Step 7 — Pre-crit sum.** `PreCritDamage = PhysicalMitigated + ElementalMitigated` (float).

- **Step 8 — Critical strike evaluation.** Roll `r = serverRNG.NextFloat()` on the server, uniform in [0.0, 1.0). If `r < CritChance`: `IsCrit = true`, `DamageAfterCrit = PreCritDamage × CritMultiplier`. Otherwise: `IsCrit = false`, `DamageAfterCrit = PreCritDamage`. Crit applies to the summed `PreCritDamage` — not separately per component. One crit roll produces one damage number and one VFX state.

- **Step 9 — Floor clamp.** `FinalDamage = max(1, Mathf.FloorToInt(DamageAfterCrit))`. The minimum-1 floor ensures every connecting hit deals at least 1 damage. At BaseDamage < 20, `MIN_DAMAGE_FRACTION × BaseDamage` truncates to 0 via `FloorToInt` — the absolute-1 floor is load-bearing here, not a safety net for extreme edge cases. For BaseDamage ≥ 20, the `MIN_DAMAGE_FRACTION` floor in Step 3 produces ≥ 1 before truncation and the absolute-1 floor serves as a fallback against pathological float accumulation only.

- **Step 10 — Kill detection.** Query `CurrentHP = GetEffectiveStat(TargetID, CurrentHP)` (float). **Dead-entity guard**: if `CurrentHP = 0.0f`, log a dev error and set `IsKill = false` — the target is already dead; callers must not invoke this function on a dead target (see Edge Cases). Otherwise: if `(float)FinalDamage >= CurrentHP`: set `IsKill = true`. Otherwise: set `IsKill = false`. Kill detection runs inside Damage Calculation before returning — this function determines kill credit but does not award XP or fire events. No calling system replicates kill detection. **Caller responsibility on kill**: when the returned `DamageResult.IsKill = true`, the caller must award XP (Rule 4a) then call `CharacterStats.ApplyDamage(TargetID, FinalDamage)`, which fires `CharacterStats.OnEntityDied(TargetID)` internally per Character Stats EC-08. One `OnEntityDied` fires per kill, never two. **Cross-GDD ownership**: `OnEntityDied` is owned by Character Stats; Damage Calculation does not fire it.

- **Step 11 — Return.** Return `DamageResult { PhysicalDamage = Mathf.FloorToInt(PhysicalMitigated), ElementalDamage = Mathf.FloorToInt(ElementalMitigated), FinalDamage = FinalDamage, IsCrit = IsCrit, IsKill = IsKill, DamageContext = DamageContext, HasElementalContribution = ElementalMitigated > 0.0f }`.

  **`DamageResult` field contract**: `PhysicalDamage` and `ElementalDamage` are pre-crit, pre-floor per-source contributions. They reflect the damage breakdown before the crit multiplier is applied. Callers must not assume `PhysicalDamage + ElementalDamage == FinalDamage` — on a crit, `FinalDamage = FloorToInt(PreCritDamage × CritMultiplier)` while `PhysicalDamage + ElementalDamage` equals the pre-crit floor sum. Use `HasElementalContribution` for VFX/audio elemental triggers — not `ElementalDamage > 0`, which truncates to 0 via `FloorToInt` for `ElementalBonus < 10` at any MagicDefense. `FinalDamage` is the authoritative value applied to health.

**Kill Credit**

4. The attacker whose call to `DamageCalculation` returns `DamageResult.IsKill = true` is the kill-holder. The caller (Auto-Attack Combat, Skill System) is responsible for XP award when `IsKill = true` — Damage Calculation does not award XP directly. The caller must: (a) call `LevelingSystem.GetXPAward(TargetID)` to obtain the XP amount, (b) award it to all eligible entities per Rule 4a, then (c) call `CharacterStats.ApplyDamage(TargetID, FinalDamage)` to apply damage and trigger `OnEntityDied` internally (Character Stats EC-08). Steps (a)–(b) must precede (c) to ensure XP is awarded before the entity is despawned by `OnEntityDied` subscribers. **Cross-GDD note**: Auto-Attack Combat GDD Step 4 must be updated to include XP award on `IsKill = true` before this system is implemented.

**Party XP**

4a. When `DamageResult.IsKill = true`, the caller must award full XP to every party member within the kill zone, not only the kill-holder. MVP rule: all party members receive the same `GetXPAward(TargetID)` XP amount — no contribution weighting. Contribution-weighted XP split is a Vertical Slice feature. **BLOCKING**: Party System GDD must expose `GetPartyMembersForXP(AttackerID): IReadOnlyList<EntityID>` (or equivalent proximity query) before kill-XP code is written in any caller. Until that interface exists, solo-kill XP (kill-holder only) is the approved MVP fallback.

**No Damage Reversal**

5. Damage Calculation does not support post-commit reversal. Once `FinalDamage` is returned and the caller applies `CharacterStats.ApplyDamage(TargetID, FinalDamage)`, the damage is permanent. This closes Auto-Attack Combat GDD Rule 18 (provisional): the pre-beat grace window (Rule 17) is the complete and sufficient mitigation for timing race conditions. Post-beat reversal is not implemented.

---

### States and Transitions

Damage Calculation is a stateless function. It maintains no runtime state between calls. No lifecycle events exist. Every call is fully independent — no buffered results, no pending events, no per-session state.

The `DamageContext` enum (internal server-side call context; distinct from the wire-format `DamageType {Physical=0, Magical=1, True=2}` in `networking-wire-protocol.md` CR-NET-7.4) and `DamageResult` struct are types owned by this system. Neither carries runtime state.

**`DamageResult` struct (readonly):**

| Field | Type | Description |
|-------|------|-------------|
| `PhysicalDamage` | int | `FloorToInt(PhysicalMitigated)` — pre-crit physical contribution. For VFX/logging only. |
| `ElementalDamage` | int | `FloorToInt(ElementalMitigated)` — pre-crit elemental contribution. May be 0 even for elemental weapons with ElementalBonus < 10. |
| `FinalDamage` | int | Post-crit authoritative damage. ≥ 1 for any connecting hit. Pass to `ApplyDamage`. |
| `IsCrit` | bool | Whether the crit roll succeeded. Independent of damage magnitude. |
| `IsKill` | bool | Whether `FinalDamage ≥ CurrentHP` at Step 10. |
| `DamageContext` | DamageContext | Echoed from input for VFX/audio routing. |
| `HasElementalContribution` | bool | `ElementalMitigated > 0.0f`. Use for elemental VFX/audio triggers — not `ElementalDamage > 0`. |

---

### Interactions with Other Systems

| System | Direction | Data | Notes |
|--------|-----------|------|-------|
| Auto-Attack Combat | → Damage Calc | `(BaseDamage: int, AttackerID, TargetID, DamageContext.PhysicalAuto)` | Called per Beat Event (Step 4 of Auto-Attack Beat Resolution). Caller applies returned `FinalDamage` via `ApplyDamage`. |
| Skill System | → Damage Calc | `(BaseDamage: int, AttackerID, TargetID, DamageContext.PhysicalSkill)` | Called per skill activation. Identical formula to PhysicalAuto. Skill System owns skill-specific BaseDamage computation. |
| Character Stats | ← Damage Calc | Reads `Defense`, `MagicDefense`, `CritChance`, `CritMultiplier`, `CurrentHP` via `GetEffectiveStat()`. Does NOT call `AddExperience` or fire `OnEntityDied` — callers handle kill consequences. | Queried live per call. Kill detection (`IsKill`) is computed here; kill consequences are the caller's responsibility. `OnEntityDied` fires inside `CharacterStats.ApplyDamage` per EC-08 when the caller applies damage on a kill. |
| Equipment System | ← Damage Calc | Reads attacker's equipped weapon `ItemID` via `EquipmentSystem.GetEquippedWeaponID(AttackerID): ItemID`; reads weapon enhancement level via `EquipmentSystem.GetEquippedWeaponEnhancementLevel(AttackerID): byte` | Required to resolve elemental bonus. If no weapon is equipped, returns `ItemID.Invalid` — Damage Calculation treats `Invalid` as `ElementType.None`. |
| Item Database | ← Damage Calc | Reads `ElementType` for the equipped weapon's `ItemID` | Queried only when `ElementType != None`. The +0 base `ElementalBonus` is no longer read directly — the enhanced value is obtained via the Enhancement System (see Enhancement System row). |
| Enhancement System | ← Damage Calc | `IEnhancementBonusProvider.GetElementalBonus(level, gearTier, isWeapon): int` — enhanced flat elemental damage for the equipped weapon (OQ-DC-1 resolved) | `ElementalBonus` input to F-DC-2 is the enhanced value (F-ENH-2), not the +0 base. Level supplied by `Equipment.GetEquippedWeaponEnhancementLevel()`. Returns 0 for non-weapons. |
| Leveling System | *Caller-owned* | `LevelingSystem.GetXPAward(EntityID): int` called by callers on kill | **Not called by Damage Calculation (Option B).** Callers call `GetXPAward(TargetID)` when `DamageResult.IsKill = true`. See OQ-DC-3. |
| VFX System / Combat UI | ← (event) | Receives `DamageResult` broadcast over networking per hit | `IsCrit` drives crit visual treatment; `DamageContext` drives element color/audio category; `IsKill` triggers death VFX. Client reads server-authoritative results — no client-side prediction of damage values. |

---

*Cross-document correction required: Auto-Attack Combat GDD Rule 10 Step 4 currently specifies the return type as `FinalDamage (float)`. After this GDD is complete, the Auto-Attack Combat GDD's dependency table and Step 4 text must be updated to reference `DamageResult`. Rule 18's provisional language must also be closed with the resolution above.*

*OQ-DC-1 (RESOLVED 2026-05-23): `ElementalBonus` DOES scale with enhancement level. The Enhancement System GDD (Approved) F-ENH-2 defines `EnhancedElementalDamage(level)` as a flat additive bonus per level. Damage Calculation obtains the enhanced value via `IEnhancementBonusProvider.GetElementalBonus(level, gearTier, isWeapon)`, supplying the level from `Equipment.GetEquippedWeaponEnhancementLevel()`. The +0 base from Item Database is no longer read directly for the elemental path. See enhancement-system.md F-ENH-2.*

## Formulas

All formulas use float arithmetic internally. Integer truncation via `Mathf.FloorToInt` is applied only in F-DC-4 and when writing `PhysicalDamage` / `ElementalDamage` into `DamageResult`. Do not use `(int)Math.Floor()` — see Character Stats GDD Rule 7 for the IL2CPP reason.

---

**F-DC-1: Physical Mitigation**

```
PhysicalMitigated = max(BaseDamage × MIN_DAMAGE_FRACTION, BaseDamage − Defense)
```

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Base damage | `BaseDamage` | int | [1, 9999] | Caller-supplied; equals `GetEffectiveStat(AttackerID, AttackPower)`. Upper bound follows AttackPower ceiling in Character Stats. |
| Physical defense | `Defense` | int | [0, 9999] | `GetEffectiveStat(TargetID, Defense)`. At 0: result equals BaseDamage exactly — unmitigated, not amplified. |
| Minimum damage fraction | `MIN_DAMAGE_FRACTION` | float | const, default 0.05 | Tuning constant. Guarantees the physical component is at least 5% of BaseDamage regardless of Defense. See Tuning Knobs. |
| Physical mitigated (output) | `PhysicalMitigated` | float | [BaseDamage×0.05, BaseDamage] | Intermediate result. Truncated to int in DamageResult (Step 11). Never negative; never exceeds BaseDamage. |

**Output range:** [BaseDamage × MIN_DAMAGE_FRACTION, BaseDamage]. The floor fires when `Defense ≥ BaseDamage × (1 − MIN_DAMAGE_FRACTION)`. Maximum output (Defense=0, BaseDamage=9999): 9999.0. Minimum output (any valid combination): BaseDamage × 0.05.

**Example (L60 Warrior DPS, BaseDamage=768, Defense=394):**
```
PhysicalMitigated = max(768 × 0.05, 768 − 394) = max(38.4, 374.0) = 374.0
```

---

**F-DC-2: Elemental Mitigation**

```
ElementalMitigated = ElementalBonus × max(MIN_ELEMENTAL_FRACTION, 1 − MagicDefense / (MagicDefense + K_MAGIC))
```

Evaluated only when `ElementType != ElementType.None`. When `ElementalBonus = 0`, this formula returns 0.0 without evaluation (Steps 4–6 short-circuit).

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Elemental bonus | `ElementalBonus` | int | [0, 9999] | Enhanced flat elemental damage for the equipped weapon, from `IEnhancementBonusProvider.GetElementalBonus()` (F-ENH-2 — base + enhancement). 0 when no elemental weapon or non-weapon. Ceiling matches `ElementalDamage_ceiling` in registry. |
| Magic defense | `MagicDefense` | int | [0, 9999] | `GetEffectiveStat(TargetID, MagicDefense)`. Single shared value for all element types. |
| Magic scaling constant | `K_MAGIC` | float | const, default 200 | Tuning constant. At `MagicDefense = K_MAGIC`, absorption = 50%. Lowering K_MAGIC makes MagicDefense more effective per point. See Tuning Knobs. |
| Minimum elemental fraction | `MIN_ELEMENTAL_FRACTION` | float | const, default 0.10 | Tuning constant. Guarantees ≥10% of ElementalBonus reaches the target regardless of MagicDefense. |
| Elemental mitigated (output) | `ElementalMitigated` | float | [ElementalBonus×0.10, ElementalBonus] | Intermediate result. 0.0 when no elemental weapon. |

**Output range:** Asymptotic — the unscaled absorption factor approaches 0 as MagicDefense → ∞ but is floored at `MIN_ELEMENTAL_FRACTION`. No value of MagicDefense reduces elemental damage to zero. Maximum output (MagicDefense=0, ElementalBonus=9999): 9999.0.

**Example (ElementalBonus=50, MagicDefense=8, K_MAGIC=200):**
```
Absorption factor = 1 − 8 / (8 + 200) = 1 − 0.03846 = 0.96154
max(0.10, 0.96154) = 0.96154
ElementalMitigated = 50 × 0.96154 = 48.077
```

---

**F-DC-3: Critical Strike Application**

```
PreCritDamage         = PhysicalMitigated + ElementalMitigated
IsCrit                = (serverRNG.NextFloat() < CritChance)
FinalDamage_pre_floor = IsCrit ? (PreCritDamage × CritMultiplier) : PreCritDamage
```

One RNG roll per call. Crit applies to the summed `PreCritDamage` — not separately per component. IsCrit fires and is reported in `DamageResult` regardless of how small the damage is; crit is not suppressed at the MIN_DAMAGE_FRACTION floor.

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Physical mitigated | `PhysicalMitigated` | float | [BaseDamage×0.05, BaseDamage] | Output of F-DC-1 |
| Elemental mitigated | `ElementalMitigated` | float | [0.0, ElementalBonus] | Output of F-DC-2. 0.0 when no elemental weapon. |
| Pre-crit total | `PreCritDamage` | float | [BaseDamage×0.05, BaseDamage+ElementalBonus] | Sum before crit evaluation |
| Crit chance | `CritChance` | float | [0.0, 0.75] | `GetEffectiveStat(AttackerID, CritChance)`. Already clamped by Character Stats. |
| Crit flag | `IsCrit` | bool | {true, false} | Evaluated once per call. Echoed in DamageResult for VFX routing. |
| Crit multiplier | `CritMultiplier` | float | [1.0, 3.0] | `GetEffectiveStat(AttackerID, CritMultiplier)`. 1.0 floor: a crit at minimum multiplier produces the same number as a normal hit, but still reports `IsCrit = true`. |
| Pre-floor result | `FinalDamage_pre_floor` | float | [BaseDamage×0.05, (BaseDamage+ElementalBonus)×3.0] | Passed to F-DC-4 |

**Output range:** Non-crit: [BaseDamage×0.05, BaseDamage+ElementalBonus]. Crit: [BaseDamage×0.05×1.0, (BaseDamage+ElementalBonus)×3.0] = up to 59,994.0 at stat ceilings.

**Example (L60 Warrior DPS, crit, BaseDamage=768, ElementalBonus=50, Defense=394, MagicDefense=8):**
```
PreCritDamage = 374.0 + 48.077 = 422.077
IsCrit = true
FinalDamage_pre_floor = 422.077 × 1.5 = 633.116
```

**Example (normal hit, same values):**
```
FinalDamage_pre_floor = 422.077
```

---

**F-DC-4: Final Floor**

```
FinalDamage = max(1, Mathf.FloorToInt(FinalDamage_pre_floor))
```

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Pre-floor result | `FinalDamage_pre_floor` | float | [≥0.05, ≤59,994.0] | Output of F-DC-3 |
| Final damage (output) | `FinalDamage` | int | [1, 59,994] | Server-authoritative integer returned in `DamageResult.FinalDamage`. Applied to target health via `CharacterStats.ApplyDamage`. |

**Output range:** [1, 59,994] under current stat ceilings. The absolute-1 floor is the definitive minimum guarantee. At low BaseDamage values (BaseDamage &lt; 20), `MIN_DAMAGE_FRACTION × BaseDamage` truncates to 0 via `FloorToInt` — the absolute-1 floor is load-bearing here, not merely a safety net. Example: `BaseDamage = 1, Defense ≥ 1` → `FloorToInt(0.05) = 0` → `max(1, 0) = 1`. No explicit FinalDamage cap is required; the maximum theoretical value (59,994) is approximately 35,800× below `int.MaxValue`.

**Example (crit continuation):**
```
Mathf.FloorToInt(633.116) = 633
max(1, 633) = 633  →  DamageResult.FinalDamage = 633
```

**Example (normal continuation):**
```
Mathf.FloorToInt(422.077) = 422
max(1, 422) = 422  →  DamageResult.FinalDamage = 422
```

---

**Calibration Table — L60 Warrior DPS (BaseDamage=768, CritChance=0.257, CritMultiplier=1.5)**

| Target | Defense | MagicDefense | Elemental | Normal Hit | Crit | Notes |
|--------|---------|-------------|----------|-----------|------|-------|
| Same-tier mob | 394 | 8 | Fire=50 | **422** (374 phys + 48 elem) | **633** | 11.4% elemental share |
| Underleveled mob | 20 | 0 | none | **748** | **1,122** | Near-full BaseDamage |
| Over-armored boss | 900 | 0 | none | **38** *(5% floor)* | **57** | Floor fires; honest "go grind" signal |

*Over-armored crit note*: At the MIN_DAMAGE_FRACTION floor (Defense=900), crit fires and produces 38 → 57. The crit multiplier is not suppressed — the rhythm signal is preserved. The low absolute numbers are the intended feedback: the game is honest about the power gap.

## Edge Cases

- **If `BaseDamage = 0` is passed**: Programming error. Return `DamageResult { FinalDamage = 0, IsKill = false, IsCrit = false }` without calling `ApplyDamage` or `OnEntityDied`. Fire a dev-build assert. `BaseDamage = 0` is unreachable through the legitimate call path — `GetEffectiveStat(AttackPower)` clamps to [1, 9999].

- **If `Defense >= BaseDamage` (over-armored target)**: `PhysicalMitigated = BaseDamage × MIN_DAMAGE_FRACTION`. `FinalDamage ≥ 1` is guaranteed by F-DC-4. At `BaseDamage < 20`, `FloorToInt(BaseDamage × 0.05)` may produce 0 — the absolute-1 floor in F-DC-4 is the definitive minimum guarantee at low BaseDamage, not the MIN_DAMAGE_FRACTION floor.

- **If `ElementalBonus > 0` and `MagicDefense = 9999`**: Unscaled absorption factor ≈ 0.0196. The `max(MIN_ELEMENTAL_FRACTION, 0.0196)` clamp fires, yielding 0.10. Target absorbs 90% — never 100%. F-DC-2 handles this natively with no special case.

- **If `CritChance = 0.0` exactly**: `r < 0.0` is always false. `IsCrit = false` for every call. Requires no special case.

- **If `CritChance = 0.75` and roll `r = 0.75` exactly**: `0.75 < 0.75` = false → `IsCrit = false`. The strict `<` operator is load-bearing — the GDD specifies strict less-than and the crit rate approaches but never exactly reaches 75%.

- **If `CritMultiplier = 1.0` and `IsCrit = true`**: `FinalDamage` is numerically identical to a normal hit. `DamageResult.IsCrit = true` still fires. VFX/audio must key on `IsCrit` directly — not on whether the number is visibly larger. A crit at minimum multiplier still plays crit effects.

- **If target's `CurrentHP = 0.0` when `DamageCalculation` is called**: Kill detection would re-fire on an already-dead entity — callers must never invoke `DamageCalculation` on a dead target. Damage Calculation is a defensive second layer: if `CurrentHP = 0.0` at Step 10, log a dev error and return `IsKill = false` without calling `AddExperience` or `OnEntityDied`. Auto-Attack Combat must gate on target liveness before invoking Damage Calculation.

- **If `LevelingSystem.GetXPAward(TargetID)` returns 0**: Valid case (low-level mob, training dummy). `AddExperience(AttackerID, 0)` is called — must not error. `OnEntityDied` still fires. `IsKill = true`. Kill happened regardless of XP yield.

- **If `LevelingSystem.GetXPAward(TargetID)` throws or returns a negative value**: Catch the exception, log a dev error, use `xpAmount = 0`. Kill still completes — `OnEntityDied` fires, `IsKill = true`. Negative XP must never be awarded on kill.

- **If two simultaneous calls both detect `FinalDamage >= CurrentHP` for the same target**: Both return `IsKill = true`. If the first caller applies `CharacterStats.ApplyDamage` before the second attacker's `DamageCalculation` call reads `CurrentHP`, the second call observes `CurrentHP = 0.0` and the dead-entity guard suppresses the duplicate (`IsKill = false`). If both calls read `CurrentHP > 0` before either `ApplyDamage` executes, both callers independently award XP and trigger kill consequences — a double-kill. Prevention requires the server tick to process all Beat Events for a given entity sequentially, with each `ApplyDamage` completing before the next call reads stats. **See OQ-DC-4 (BLOCKING)** — the server tick ordering contract must be documented in an ADR before implementation begins.

- **If `AttackerID == TargetID` (self-damage)**: Illegal in MVP. Dev-build assert + runtime guard — return `DamageResult { FinalDamage = 0 }` without applying damage or firing `OnEntityDied`. Self-damage would award XP to the dying entity.

- **If `GetEquippedWeaponID(AttackerID)` returns `ItemID.Invalid`**: Steps 4–6 short-circuit immediately on `ItemID.Invalid`. The Item Database is not queried. `ElementalBonus = 0`, `ElementalMitigated = 0.0`. Physical damage is unaffected — an unarmed character still deals F-DC-1 physical damage.

- **If `serverRNG.NextFloat()` returns exactly `0.0`**: At any `CritChance > 0.0`, `0.0 < CritChance` is true — crit fires. At `CritChance = 0.0`, `0.0 < 0.0` is false — no crit. Requires `NextFloat()` to include 0.0 in its range ([0.0, 1.0)).

- **If `BaseDamage = 1` and `Defense ≥ 1`**: `max(0.05, 0.0) = 0.05`. `FloorToInt(0.05) = 0`. Absolute-1 floor fires. `FinalDamage = 1`. Even at `CritMultiplier = 3.0`: `FloorToInt(0.05 × 3.0) = 0` → `FinalDamage = 1`. `IsCrit` is still reported faithfully.

- **If a weapon has `ElementType = Fire` but `ElementalBonus = 0` (authoring error)**: `ElementalMitigated = 0.0`. Mathematically harmless at runtime. Item Database authoring tooling should warn on `ElementType != None && ElementalBonus == 0`.

- **If an `OnEntityDied` subscriber calls `GetEffectiveStat(TargetID, CurrentHP)` during the event**: Under the current architecture (Option B — Character Stats owns `OnEntityDied`), the event fires inside `CharacterStats.ApplyDamage` after HP has been written to 0 — subscribers observe `CurrentHP = 0.0`. This is the correct, expected behavior per Character Stats EC-08. **Integration rule (promoted from edge case)**: All subscribers must treat `OnEntityDied` as the authoritative kill signal. Do not re-query `CurrentHP` to confirm liveness inside a handler — the event itself is sufficient. All subscribers must execute synchronously to completion before returning from the event handler; async/coroutine subscribers to `OnEntityDied` are not permitted.

- **If `FinalDamage_pre_floor` is expected to be an exact integer but arrives as e.g. `421.9999...` due to IEEE 754 accumulation**: `FloorToInt(421.999...) = 421`. Expected, not a bug. `FloorToInt` is intentional design — one-point differences from theoretical output are acceptable under the honest-numbers pillar.

## Dependencies

**Upstream (systems this one depends on):**

| System | Type | Interface | Notes |
|--------|------|-----------|-------|
| Character Stats | Hard | `GetEffectiveStat(EntityID, StatID)` for `Defense`, `MagicDefense`, `CritChance`, `CritMultiplier`, `CurrentHP` | Queried live per call. Does NOT call `AddExperience` or fire `OnEntityDied` — callers handle kill consequences. `OnEntityDied` fires inside `ApplyDamage` per EC-08. |
| Equipment System | Hard | `EquipmentSystem.GetEquippedWeaponID(EntityID): ItemID`; `EquipmentSystem.GetEquippedWeaponEnhancementLevel(EntityID): byte` | Required to resolve elemental bonus and its enhancement level. `ItemID.Invalid` = no weapon equipped. |
| Item Database | Hard | `IItemDatabase.GetItem(ItemID)` → `.ElementType` | Queried only when `ElementType != ElementType.None`. The +0 base `ElementalBonus` is not read directly — the enhanced value comes from the Enhancement System (OQ-DC-1 resolved). |
| Enhancement System | Hard | `IEnhancementBonusProvider.GetElementalBonus(level, gearTier, isWeapon): int` | Supplies the enhanced flat elemental damage (F-ENH-2) used as the `ElementalBonus` input to F-DC-2. Level from `Equipment.GetEquippedWeaponEnhancementLevel()`. Returns 0 for non-weapons. |
| Leveling System | *Caller-owned* | `LevelingSystem.GetXPAward(EntityID): int` | **Not a Damage Calculation dependency (Option B).** Callers award XP when `DamageResult.IsKill = true`. OQ-DC-3 (interface contract) is now a Leveling System / caller concern. |

**Downstream (systems that depend on this one):**

| System | Type | What it expects | Notes |
|--------|------|-----------------|-------|
| Auto-Attack Combat | Hard | `DamageCalculation(BaseDamage, AttackerID, TargetID, DamageContext.PhysicalAuto)` → `DamageResult` | On `IsKill = true`: caller must call `GetXPAward(TargetID)`, then `AddExperience(AttackerID, xpAmount)` (party-XP distribution, if any, is applied by the Party System's own F-PS-1 detriment logic on top of this single-killer sequence — see `party-system.md`; not a claim of this GDD), then call `CharacterStats.ApplyDamage(TargetID, FinalDamage)`. **Resolved 2026-09-24**: Auto-Attack Combat GDD Rule 10 Step 3 now specifies this kill sequence (previous text here referenced a "Step 4"/"Rule 4a" that did not exist in that GDD — corrected to point at the real location). Rule 18 provisional is closed: no damage reversal supported. |
| Skill System | Hard | `DamageCalculation(BaseDamage, AttackerID, TargetID, DamageContext.PhysicalSkill)` for `PhysicalDamage` skills; `DamageContext.MagicalSkill` for Healer `MagicalDamage` skills (INT-scaled `BaseDamage`) → `DamageResult` | Skill System owns `BaseDamage` computation for both contexts. Formula pipeline is identical at MVP; `MagicalSkill` maps to `DamageType.Magical` on wire. |
| VFX System / Combat UI | Soft | `DamageResult.IsCrit`, `DamageResult.IsKill`, `DamageResult.DamageContext`, `DamageResult.FinalDamage`, `DamageResult.HasElementalContribution` via server broadcast | Soft dependency — VFX enhances presentation but does not affect game logic. Client reads server-broadcast results only. Use `HasElementalContribution` for elemental tint/audio triggers — not `ElementalDamage > 0`. |

**Bidirectional consistency notes:**
- Character Stats GDD Rule 9 lists Damage Calculation as the owner of `AddExperience(on-kill)`. ⚠️ **Stale** — under Option B, AddExperience is called by callers, not Damage Calculation. Character Stats GDD Rule 9 must be updated to reflect this ownership change.
- Character Stats GDD EC-08 specifies `ApplyDamage` fires `OnEntityDied` when amount ≥ CurrentHP. ✅ Consistent with Option B — this is the single authoritative `OnEntityDied` emission.
- Character Stats GDD Interactions table lists Damage Calculation reading `AttackPower, Defense, MagicDefense, CritChance, CritMultiplier`. The row should also list `CurrentHP` (queried in kill detection at Step 10).
- Auto-Attack Combat GDD dependency table lists `FinalDamage (float)` as the return type. Must be corrected to `DamageResult`. Auto-Attack Combat Step 4 must be updated with the full kill sequence (XP award + ApplyDamage).

## Tuning Knobs

| Knob | Default | Safe Range | Too Low | Too High |
|------|---------|-----------|---------|---------|
| `MIN_DAMAGE_FRACTION` | 0.05 (5%) | [0.01, 0.20] | At very low BaseDamage values, F-DC-1 output approaches 0 before F-DC-4 floor can catch it — the game signal becomes "you are completely ineffective" rather than "you are outgeared" | Over-armored targets still take large minimum hits; Defense stops functioning as a meaningful stat at high values |
| `K_MAGIC` | 200 | [50, 500] | MagicDefense becomes hyper-efficient — small INT investment negates most elemental damage; elemental weapons lose value | MagicDefense investment yields negligible returns; elemental weapons deal near-full damage against any target; Healer's INT-to-MagicDefense identity weakens |
| `MIN_ELEMENTAL_FRACTION` | 0.10 (10%) | [0.05, 0.30] | Elemental bonus disappears against high-MagicDefense targets; elemental weapons only matter against zero-MagicDefense mobs | Elemental bonus is nearly unmitigatable regardless of MagicDefense; the stat becomes irrelevant; undermines Healer INT investment payoff |

**Interaction notes:**
- `MIN_DAMAGE_FRACTION` and `K_MAGIC` are fully independent — changing one does not affect the other.
- `K_MAGIC` calibration: at default 200, the `MIN_ELEMENTAL_FRACTION` floor triggers when `MagicDefense ≥ 9 × K_MAGIC = 1800`. At K_MAGIC = 100, the floor triggers at MagicDefense ≥ 900 — reachable by a geared Healer Support build.
- Raising `MIN_ELEMENTAL_FRACTION` above 0.30 effectively decouples elemental damage from its mitigation stat entirely — MagicDefense would never meaningfully apply. Do not exceed 0.30 without reviewing the Healer class value proposition.
- These three constants are the only tuning levers owned by Damage Calculation. All other inputs (stat values, elemental bonus, crit parameters) are owned by Character Stats, Item Database, and the Leveling System respectively and tuned in those GDDs.

## Visual/Audio Requirements

*This section is a constraint document for the VFX System and Combat UI GDDs. It does not specify implementation — it establishes enforceable rules those systems must satisfy.*

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:damage-calculation` to produce per-asset visual descriptions, dimensions, and generation prompts from this section.

---

### VAR-1 — Damage Number Display

Damage numbers spawn in world space at the point of contact and rise vertically. They do not anchor to screen space.

| State | Trigger | Color | Size | Animation |
|-------|---------|-------|------|-----------|
| Normal hit | `!IsCrit`, `!HasElementalContribution` | `#E8E6DF` (Primary Text) | 18sp | Linear rise |
| Normal hit (elemental) | `!IsCrit`, `HasElementalContribution` | Elemental tint (see VAR-3) | 18sp | Linear rise |
| Critical hit (physical) | `IsCrit = true`, `!HasElementalContribution` | `#F0EFE8` (Ascension White) | 26sp | Spring bounce |
| Critical hit (elemental) | `IsCrit = true`, `HasElementalContribution` | Elemental tint (see VAR-3) | 26sp | Spring bounce |
| Kill blow | `IsKill = true` | No change over normal/crit rules | Per above | Per above + VAR-4 burst |

No damage number shadows, outlines, glow, or decorative font styling. Art Bible principle: legibility only.

`IsCrit = true` with `CritMultiplier = 1.0` still displays as a crit (26sp, spring animation, Ascension White or elemental tint) — the crit flag drives display, not the magnitude of the number.

---

### VAR-2 — Critical Hit Differentiation

A crit is distinguished from a normal hit by exactly two properties:
1. **Color**: Primary Text `#E8E6DF` → Ascension White `#F0EFE8` (or elemental tint if `HasElementalContribution`)
2. **Size**: 18sp → 26sp (44% increase)

**Animation**: Crit numbers play a single spring-bounce on spawn — scale 1.0 → 1.3 → 1.0 over the first 150ms of a 500ms total lifetime. Normal hit numbers rise linearly (no bounce). Spring-bounce duration is fixed; it does not scale with damage magnitude.

**Collision rule**: If the previous number has not faded when the next number spawns, the new number spawns at a minimum 12px horizontal offset from the previous number's current position. The VFX System must implement stagger to prevent overlap at consistent attack speeds.

*Colorblind compliance*: Size differentiation alone is sufficient — size is the primary differentiator. Color adds a second cue but is not the only one.

---

### VAR-3 — Elemental Damage Expression

The elemental component (`DamageResult.ElementalDamage`) is not displayed as a separate number. A single `DamageResult.FinalDamage` number is shown. The elemental origin is expressed through color tinting only.

| Weapon ElementType | Damage number tint | Notes |
|--------------------|-------------------|-------|
| `Fire` | `#E8692A` (warm orange-red) | Distinct from Threat Red `#C0392B` — fire reads "offensive bonus," not "threat/danger" |
| `None` | No tint — `#E8E6DF` (Primary Text) | Baseline |
| Other elements (post-MVP) | Defined per element in future spec | Not in MVP scope |

**When `IsCrit = true` and `HasElementalContribution`**: Elemental tint replaces Ascension White as the number color. Crit size (26sp) and spring animation are preserved. Two of three crit signals (size, animation) remain — sufficient differentiation.

Elemental tint is a secondary cue — communicates flavor, not a critical gameplay decision. No colorblind shape backup is required for elemental tinting at MVP.

---

### VAR-4 — Kill Feedback

When `IsKill = true`, two events fire in addition to the normal/crit damage number:

1. **Kill particle burst** — single non-looping burst at the target's origin. Max 30 particles, lifetime ≤ 1.0s, color `#F0EFE8` (Ascension White) with additive blending. Does not loop.
2. **Kill audio cue** — `sfx_hit_kill` one-shot event (see VAR-6). Plays alongside the hit sound.

**Excluded from kill feedback**: No screen shake, no damage number inflation beyond normal crit rules, no color change to the damage number, no slow-motion. Screen shake in mob-dense farming sessions becomes noise, not signal.

---

### VAR-5 — Timing Fidelity

**Hard constraint**: Damage number display latency ≤ 1 frame at 60fps (≤ 16.6ms) from `DamageResult` received. The number must appear on the same frame the hit event fires. Pre-delay animations (grow-in from zero before the number is readable) violate this rule.

**Animation lifetimes:**

| Number type | Total lifetime | Motion |
|-------------|--------------|--------|
| Normal hit | 400ms | Rise 18px over 200ms → hold 100ms → fade 100ms |
| Critical hit | 500ms | Rise 18px + spring bounce (150ms) → hold → fade |

---

### VAR-6 — Audio Categories

Four categories at MVP. Each is a distinct one-shot cue — not a pitched variant of another.

| Category | Naming convention | Trigger | Tonal character |
|----------|------------------|---------|-----------------|
| `sfx_hit_normal` | `sfx_hit_phys_normal_01.ogg` | `!IsCrit`, `!IsKill` | Short percussive impact. Dry mid-range thud. Duration ≤ 200ms. No tail. |
| `sfx_hit_crit` | `sfx_hit_phys_crit_01.ogg` | `IsCrit = true` | Louder, brighter than normal. Hard transient + short metallic overtone. Duration ≤ 300ms. |
| `sfx_hit_kill` | `sfx_hit_kill_01.ogg` | `IsKill = true` (layers with hit/crit) | Low-end emphasis — "thud with weight." Duration ≤ 400ms. Plays at -0dB; kill sound mixes on top of hit sound without masking it. |
| `sfx_hit_elemental` | `sfx_hit_elem_fire_01.ogg` | `HasElementalContribution` | Element-flavored secondary layer. Fire: dry crackle ≤ 150ms. Plays at -6dB relative to hit sound — augments, does not replace. |

`PhysicalAuto` and `PhysicalSkill` use the same audio category pool at MVP. `DamageContext` is available in `DamageResult` for future routing to a distinct skill-hit sound, deferred to Vertical Slice.

*Format*: All assets mono `.ogg` Vorbis q7, 44,100Hz, 16-bit.

## UI Requirements

Damage Calculation does not own any screen or HUD element. It is a pure computation layer — it produces `DamageResult` and fires events. The floating damage numbers specified in VAR-1 are owned by the VFX System / Combat UI GDD, not this system.

## Acceptance Criteria

*Prerequisites: Tests marked (Unit) require a mock stat provider and injectable server RNG. Tests marked (Integration) require the full multi-system test harness. Before writing crit-dependent tests (AC-DC-F-07, F-09, F-09b, F-11, AC-DC-E-03), the server RNG injection contract must be documented in `docs/architecture/` — without it, crit tests cannot be deterministic, violating the automated test rules in coding-standards.md. These five ACs are **BLOCKED on OQ-DC-2**.*

### Group A — Formula Tests (Unit)

**AC-DC-F-01** — GIVEN `BaseDamage = 768`, `Defense = 394`, no elemental weapon, `CritChance = 0.0`, WHEN `DamageCalculation` is called, THEN `DamageResult.PhysicalDamage = 374`.

**AC-DC-F-02** — GIVEN `BaseDamage = 100`, `Defense = 9999`, WHEN called, THEN `DamageResult.PhysicalDamage = 5` (MIN_DAMAGE_FRACTION floor fires: `FloorToInt(100 × 0.05)`).

**AC-DC-F-03** — GIVEN `BaseDamage = 768`, `Defense = 0`, no elemental weapon, `CritChance = 0.0`, WHEN called, THEN `DamageResult.PhysicalDamage = 768` exactly (unmitigated, not amplified).

**AC-DC-F-04** — GIVEN `ElementalBonus = 50`, `MagicDefense = 8`, `K_MAGIC = 200`, `CritChance = 0.0`, WHEN called, THEN `DamageResult.ElementalDamage = 48` (`FloorToInt(50 × 0.96154)`).

**AC-DC-F-05** — GIVEN `ElementalBonus = 100`, `MagicDefense = 9999`, `K_MAGIC = 200`, WHEN called, THEN `DamageResult.ElementalDamage = 10` (MIN_ELEMENTAL_FRACTION floor fires — never 0).

**AC-DC-F-06** — GIVEN `EquipmentSystem.GetEquippedWeaponID` returns `ItemID.Invalid`, WHEN called, THEN `DamageResult.ElementalDamage = 0` and `IItemDatabase.GetItem` is not invoked.

**AC-DC-F-07** — GIVEN `CritChance = 1.0` (mocked RNG = 0.0, guaranteed crit), `CritMultiplier = 1.5`, `BaseDamage = 768`, `Defense = 394`, `ElementalBonus = 50`, `MagicDefense = 8`, WHEN called, THEN `DamageResult.IsCrit = true` and `DamageResult.FinalDamage = 633`.

**AC-DC-F-08** — GIVEN `CritChance = 0.0`, same stat config as AC-DC-F-07, WHEN called, THEN `DamageResult.IsCrit = false` and `DamageResult.FinalDamage = 422`.

**AC-DC-F-09** — GIVEN `CritChance = 0.75`, mocked RNG returns exactly `0.75`, WHEN called, THEN `DamageResult.IsCrit = false` (strict `<` operator: `0.75 < 0.75` = false).

**AC-DC-F-09b** *(BLOCKED:OQ-DC-2)* — GIVEN `CritChance = 0.75`, mocked RNG returns `0.7499999` (nearest representable float below 0.75), WHEN called, THEN `DamageResult.IsCrit = true` (`0.7499999 < 0.75` = true — confirms strict less-than, not `<=`).

**AC-DC-F-10** — GIVEN `BaseDamage = 1`, `Defense = 1`, `CritChance = 0.0`, no elemental weapon, WHEN called, THEN `DamageResult.FinalDamage = 1` (absolute-1 floor fires: `FloorToInt(0.05) = 0`).

**AC-DC-F-10b** — GIVEN `BaseDamage = 19`, `Defense = 9999` (MIN_DAMAGE_FRACTION fires), `CritChance = 0.0`, no elemental weapon, WHEN called, THEN `FloorToInt(19 × 0.05) = FloorToInt(0.95) = 0`, absolute-1 floor fires, `DamageResult.FinalDamage = 1`. *(Top of load-bearing range — confirms F-DC-4 is load-bearing at BaseDamage = 19.)*

**AC-DC-F-10c** — GIVEN `BaseDamage = 20`, `Defense = 9999`, `CritChance = 0.0`, no elemental weapon, WHEN called, THEN `FloorToInt(20 × 0.05) = FloorToInt(1.0) = 1`, `DamageResult.FinalDamage = 1` — produced via MIN_DAMAGE_FRACTION path, not absolute floor. Both F-10b and F-10c output 1 but via different code paths; code coverage must distinguish them.

**AC-DC-F-11** — GIVEN `BaseDamage = 1`, `Defense = 1`, `CritChance = 1.0` (mocked RNG = 0.0), `CritMultiplier = 3.0`, WHEN called, THEN `DamageResult.FinalDamage = 1` and `DamageResult.IsCrit = true` (crit flag reported faithfully even at absolute floor).

**AC-DC-F-12** — GIVEN `BaseDamage = 768`, `Defense = 394`, `ElementalBonus = 50`, `MagicDefense = 8`, `CritChance = 0.0`, WHEN called, THEN `PhysicalDamage = 374`, `ElementalDamage = 48`, `FinalDamage = 422` (components truncated independently).

**AC-DC-F-12b** — GIVEN inputs that produce `PhysicalMitigated = 374.6` and `ElementalMitigated = 48.6` (fractional parts sum ≥ 1.0), `CritChance = 0.0`, WHEN called, THEN `DamageResult.FinalDamage = FloorToInt(374.6 + 48.6) = 423` AND `DamageResult.PhysicalDamage + DamageResult.ElementalDamage = 374 + 48 = 422`. Verify that `FinalDamage ≠ PhysicalDamage + ElementalDamage` is the correct specified behavior — not an error.

**AC-DC-F-13** — GIVEN a call with `DamageContext = PhysicalSkill`, WHEN called, THEN `DamageResult.DamageContext = PhysicalSkill` exactly.

**AC-DC-F-13b** — GIVEN a call with `DamageContext = PhysicalAuto`, WHEN called, THEN `DamageResult.DamageContext = PhysicalAuto` exactly.

**AC-DC-F-14** — GIVEN two calls with identical stat inputs and identical mocked RNG — one `DamageContext.PhysicalAuto`, one `DamageContext.PhysicalSkill` — WHEN both calls complete, THEN `FinalDamage`, `PhysicalDamage`, `ElementalDamage`, and `IsCrit` are identical across both results.

**AC-DC-F-14b** — GIVEN a call with `DamageContext = MagicalSkill`, WHEN called, THEN `DamageResult.DamageContext = MagicalSkill` exactly.

**AC-DC-F-14c** — GIVEN two calls with identical stat inputs and identical mocked RNG — one `DamageContext.PhysicalSkill`, one `DamageContext.MagicalSkill` — WHEN both calls complete, THEN `FinalDamage`, `PhysicalDamage`, `ElementalDamage`, and `IsCrit` are identical across both results (at MVP, `MagicalSkill` runs the same pipeline as `PhysicalSkill`; only `DamageResult.DamageContext` differs).

---

### Group B — Kill Detection Tests (Unit)

**AC-DC-K-01** — GIVEN target `CurrentHP = 100.0`, call produces `FinalDamage = 100`, WHEN called, THEN `DamageResult.IsKill = true`. Verify via mock CharacterStats that `DamageCalculation` does NOT call `AddExperience` or fire `OnEntityDied` — those are caller responsibilities (Rule 4).

**AC-DC-K-02** — GIVEN target `CurrentHP = 101.0`, call produces `FinalDamage = 100`, WHEN called, THEN `DamageResult.IsKill = false`.

**AC-DC-K-03** — GIVEN a kill scenario, WHEN called, THEN `DamageCalculation` does NOT call `LevelingSystem.GetXPAward`, does NOT call `CharacterStats.AddExperience`, and does NOT fire `CharacterStats.OnEntityDied`. All verified via mock objects — DamageCalculation's kill responsibility is `IsKill = true` only.

**AC-DC-K-04** — GIVEN target `CurrentHP = 50.0`, call produces `FinalDamage = 50` (exact equality), WHEN called, THEN `DamageResult.IsKill = true` (strict `>=` fires at equality).

**AC-DC-K-05** — GIVEN target `CurrentHP = 100.5`, call produces `FinalDamage = 100`, WHEN called, THEN `DamageResult.IsKill = false` (`100.0 < 100.5` — fractional HP boundary).

**AC-DC-K-06** — GIVEN target `GetEffectiveStat(TargetID, CurrentHP)` returns `0.0` at Step 10, WHEN called, THEN `DamageResult.IsKill = false` (dead-entity guard — no kill consequence re-fires on an already-dead entity).

**AC-DC-K-07** — GIVEN target `CurrentHP = 0.1`, call produces `FinalDamage = 1` (minimum possible after F-DC-4), WHEN called, THEN `DamageResult.IsKill = true` (`1.0 >= 0.1`).

---

### Group C — Edge Case Tests (Unit)

**AC-DC-E-01** — GIVEN `BaseDamage = 0`, WHEN called, THEN `DamageResult.FinalDamage = 0`, `IsKill = false`, `ApplyDamage` not called, `OnEntityDied` not fired.

**AC-DC-E-02** — GIVEN `AttackerID == TargetID` (same entity), WHEN called, THEN `DamageResult.FinalDamage = 0`, `IsKill = false`, `CharacterStats.ApplyDamage` not called, `CharacterStats.OnEntityDied` not fired, `CharacterStats.AddExperience` not called, `LevelingSystem.GetXPAward` not called. All verified via mock objects.

**AC-DC-E-03** — GIVEN `CritChance = 1.0` (mocked RNG = 0.0), `CritMultiplier = 1.0`, `BaseDamage = 100`, `Defense = 0`, no elemental weapon, WHEN called, THEN `DamageResult.IsCrit = true` and `DamageResult.FinalDamage = 100` (numerically equal to a normal hit — crit flag is independent of damage magnitude).

**AC-DC-E-04** — GIVEN a weapon with `ElementType = Fire` and `ElementalBonus = 0`, WHEN called, THEN `DamageResult.ElementalDamage = 0` and no error or crash.

**AC-DC-E-05** — GIVEN `CritChance = 0.75` over 10,000 calls with a seeded deterministic RNG, WHEN calls complete, THEN observed crit rate is within [0.737, 0.763] (±3σ from expected 0.75) and no call where `r >= 0.75` (deterministically verifiable with seed) returns `IsCrit = true`. *(Note: with a fixed seed the rate is deterministic — the [0.737, 0.763] band is a sanity check; the `r >= 0.75 → IsCrit = false` assertion is the primary gate.)*

**AC-DC-E-06** — GIVEN an attacker with `ElementType = Fire` and `ElementalBonus = 9`, `MagicDefense = 9999`, `K_MAGIC = 200`, `CritChance = 0.0`, WHEN called, THEN `DamageResult.ElementalDamage = 0` (`FloorToInt(9 × 0.10) = FloorToInt(0.9) = 0`) AND `DamageResult.HasElementalContribution = true` (`ElementalMitigated = 0.9 > 0.0f`). Verify that VAR-3/VAR-6 triggers fire (keyed on `HasElementalContribution`) even though `ElementalDamage = 0`.

---

### Group D — Integration Tests (multi-system harness required)

**AC-DC-I-01** — GIVEN a server build and client build, WHEN the client assembly is inspected (automated: namespace scan of client IL or assembly manifests), THEN no type matching `DamageCalculation`, `DamageResult`, or `DamageContext` from `ServerLogic.asmdef` exists in the client binary. **AC-DC-I-01-MANUAL** (advisory fallback — only if build harness unavailable): Code reviewer documents in `production/qa/evidence/` that they inspected client assembly references and found no damage calculation code path; requires technical-director sign-off. This fallback does NOT satisfy the BLOCKING gate — the automated assembly scan must be implemented before the Integration milestone.

**AC-DC-I-02** — GIVEN a kill scenario with an `OnEntityDied` subscriber that reads `GetEffectiveStat(TargetID, CurrentHP)` inside the event handler, WHEN `DamageCalculation` returns `IsKill = true` and the caller applies `CharacterStats.ApplyDamage(TargetID, FinalDamage)`, THEN the subscriber observes `CurrentHP = 0.0` at event time (the event fires inside `ApplyDamage` after HP has been written to 0 — this is the correct expected behavior per Character Stats EC-08 and Option B architecture).

**AC-DC-I-02b** — GIVEN a kill scenario with a sequencing mock that records call order, WHEN `DamageCalculation` returns and the caller executes the kill sequence, THEN the order is: [GetXPAward called] → [AddExperience called] → [ApplyDamage called] → [OnEntityDied fires inside ApplyDamage]. `OnEntityDied` must not fire before `ApplyDamage` is called by the caller.

**AC-DC-I-03** — GIVEN `BaseDamage = 768`, `Defense = 394`, `MagicDefense = 8`, `ElementalBonus = 50`, `CritChance = 0.0`, `K_MAGIC = 200`, `MIN_DAMAGE_FRACTION = 0.05`, `MIN_ELEMENTAL_FRACTION = 0.10`, WHEN called (full pipeline), THEN `{ PhysicalDamage: 374, ElementalDamage: 48, FinalDamage: 422, IsCrit: false, IsKill: false }`.

**AC-DC-I-04** — GIVEN same config as AC-DC-I-03 with `CritChance = 1.0` (mocked RNG) and `CritMultiplier = 1.5`, WHEN called, THEN `FinalDamage = 633` and `IsCrit = true`.

**AC-DC-I-05** — GIVEN `BaseDamage = 768`, `Defense = 900`, `CritChance = 0.0`, no elemental weapon, `CurrentHP = 1000.0`, WHEN called, THEN `FinalDamage = 38` (5% floor fires) and `IsKill = false`.

**AC-DC-I-06** — GIVEN `BaseDamage = 500`, `Defense = 0`, no elemental weapon, `CritChance = 0.0`, target `CurrentHP = 100.0`, WHEN called, THEN `DamageResult.IsKill = true` and `DamageResult.FinalDamage = 500`. Verify DamageCalculation does NOT call `AddExperience` or fire `OnEntityDied`. WHEN the caller then executes the full kill sequence (GetXPAward → AddExperience(AttackerID, 500) → ApplyDamage), THEN `OnEntityDied(TargetID)` fires exactly once from inside `ApplyDamage`.

## Open Questions

**OQ-DC-1** *(BLOCKING — Enhancement System GDD)*: Does `ElementalBonus` scale with enhancement level? Step 4 reads `ElementalBonus` from Item Database for the item at +0. If the Enhancement System applies a multiplier to elemental damage on enhanced weapons, Damage Calculation must instead read the scaled value from Equipment System (which would own the post-enhancement value), not from Item Database directly. This dependency boundary must be resolved before the Enhancement System GDD is authored. If the answer is "yes, Enhancement scales elemental," the `ReadElementalBonus(ItemID)` call in Step 4 moves from Item Database to Equipment System.

**OQ-DC-2** *(BLOCKING — ADR required before implementation)*: Server RNG injection contract. Acceptance Criteria AC-DC-F-07, AC-DC-F-09, AC-DC-F-09b, AC-DC-F-11, and AC-DC-E-03 require deterministic crit simulation in tests — **5 ACs are unwritable until this ADR is documented**. The architectural pattern (interface design, injection point, test double contract) must be resolved before implementation begins. The ADR should also confirm the RNG is stateless per call (no sequence tracking) to survive zone migration safely — a stateful RNG reset on zone transfer would corrupt crit streams. Flagged in Section H (Acceptance Criteria prerequisite note).

**OQ-DC-3** *(Leveling System GDD)*: `GetXPAward(EntityID): int` interface ownership. Under Option B, the callers (Auto-Attack Combat, Skill System) call this when `DamageResult.IsKill = true` — Damage Calculation is no longer the caller. The Leveling System GDD must define: (a) what determines the XP award for a given mob, (b) whether the award is fixed per mob type or computed dynamically (e.g., scaled by attacker level — which would create a gray-mob falloff mechanic with significant design implications for Earned Power), and (c) whether the interface returns a single value or a parameterized one. Note three-way option: (i) fixed lookup in Item/Mob Database, (ii) attacker-level-scaled, (iii) Enhancement-scaled (if enhanced gear affects XP yield — unlikely but possible). Callers must know which before the kill-handling code is written.

**OQ-DC-4** *(BLOCKING — Server Architecture ADR required before implementation)*: Double-kill race condition. If two attackers both read `CurrentHP > 0` before either calls `ApplyDamage`, both callers award XP and trigger kill consequences. Prevention requires the server tick to guarantee sequential Beat Event resolution per entity within a single frame, with `ApplyDamage` completing before any subsequent call reads that entity's stats. This architectural constraint must be formally documented in a server tick ADR. Without it, the double-kill suppression in the dead-entity guard (Edge Cases) is not guaranteed. **Implication**: any refactor of the server tick model (e.g., moving to a parallel physics step, batching damage application) requires this ADR to be re-validated.
