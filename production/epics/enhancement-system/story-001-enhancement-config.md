# Story 001: Enhancement Config — Level Cap, Thresholds, Probability Table, Prestige Band

> **Epic**: Enhancement System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-1, CR-ENH-2 (level range), CR-ENH-9, CR-ENH-10 (two outcomes, destruction at every level), CR-ENH-12 (prestige band table), F-ENH-4 (probability table), F-ENH-5 (expected scrolls), TK-ENH-1 to TK-ENH-8 (values, safe ranges, ordering constraints).
**Requirement**: `TR-enh-002`, `TR-enh-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: None — design-only (architecture.md: core gameplay/data, by-design no ADR). ADR-010 applies only to how other systems receive this config (constructor injection, no singleton).
**ADR Decision Summary**: Dependencies are injected through interfaces or plain values; no static mutable game state.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. No engine API beyond `UnityEngine.Debug`.

**Control Manifest Rules (Feature layer)**:
- Required: constants in UPPER_SNAKE_CASE (`MAX_ENHANCEMENT_LEVEL`); `readonly struct` for any event argument type — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [x] **Default values**: the default config holds `MAX_ENHANCEMENT_LEVEL = 10`, `PRESTIGE_MID_THRESHOLD = 5`, `ENHANCEMENT_GLOW_THRESHOLD = 7`, `PRESTIGE_HIGH_THRESHOLD = 8`, `BonusPerLevel` Bronze 3 / Iron 4 / Steel 6 / Dark Steel 10, `ElementalBonusPerLevel` Bronze 1 / Iron 2 / Steel 3 / Dark Steel 5, and `ElementalDamage_ceiling = 9999`.
- [x] **Probability table (F-ENH-4)**: the default `P_s[k]` for k = 0..9 is 0.95, 0.90, 0.85, 0.80, 0.65, 0.50, 0.35, 0.22, 0.12, 0.06. The success probability for a level is read with one call; the destruction probability is `1 − P_s[k]` for every level, including level 0.
- [x] **No entry at the cap**: asking for the success probability at `MAX_ENHANCEMENT_LEVEL` or above throws `ArgumentOutOfRangeException` (no attempt exists from the cap).
- [x] **Probability constraints (TK-ENH-1)**: a config whose `P_s` values are not strictly decreasing, or with any value outside [0.01, 0.95], or whose table length differs from `MAX_ENHANCEMENT_LEVEL`, fails validation at construction.
- [x] **Threshold ordering (TK-ENH-4 to TK-ENH-6)**: validation rejects any threshold set that does not satisfy `PRESTIGE_MID_THRESHOLD < ENHANCEMENT_GLOW_THRESHOLD < PRESTIGE_HIGH_THRESHOLD ≤ MAX_ENHANCEMENT_LEVEL`.
- [x] **Safe ranges are not hard rules** *(decided 2026-10-07 at readiness)*: the tuning knobs' "safe ranges" (cap 9–12, mid 3–6, glow 6–9, high 7–10) are guidance and are not enforced. A config outside them is accepted as long as the probability constraints, the threshold ordering and the table length hold.
- [x] **Outcome resolution (CR-ENH-9)**: for level `k` and a draw `r ∈ [0, 1)`, the outcome is Success when `r < P_s[k]` and Destruction otherwise. `r = P_s[k]` exactly is Destruction.
- [x] **Prestige band mapping (CR-ENH-12)**: levels 0–4 → `NONE` (0b00), 5–6 → `VISIBLE_NO_GLOW` (0b01), 7 → `GLOW_LOW` (0b10), 8–10 → `HIGH` (0b11), derived from the three thresholds and not from literal level numbers. A level above `MAX_ENHANCEMENT_LEVEL` maps to `HIGH` — the mapping depends on the thresholds only and never throws *(decided 2026-10-07 at readiness)*.
- [x] **Expected scrolls (F-ENH-5)**: the expected number of scrolls from +0 to level N, computed from the default table with `A[k] = (1 + P_d[k] × T[k]) / P_s[k]`, `T[0] = 0`, `T[k] = T[k−1] + A[k−1]`, matches the GDD table to the precision it prints (+1 → 1.1, +5 → 10.9, +9 → 2,727).

---

## Implementation Notes

- New folder `src/Foundation/EnhancementSystem/`, namespace `IronGrind.EnhancementSystem` (same placement as `LootTableSystem` and `InventorySystem`).
- **One immutable config object**, e.g. `EnhancementConfig`, built from values and validated in its constructor (throw `ArgumentException` naming the broken constraint). A static `EnhancementConfig.Default` holds the GDD values. Every later story receives the config by constructor injection — no static lookups from service code. This is how the Inventory System already receives the maximum level (`InventoryService(…, byte maxEnhancementLevel)`); the composition root passes `config.MaxEnhancementLevel` there.
- `GearTier.None` has no `BonusPerLevel` entry: asking for it is an error, not 0.
- **`ElementalDamage_ceiling` is owned by the Item Database**, not by this system (`design/registry/entities.yaml`: source `item-database.md`, value 9999). No constant for it exists in `src/` yet, so the config carries the value for Story 002's clamp. It must equal the registry value; say so in the field's doc comment, and replace it with the Item Database's constant if one is added later.
- `PrestigeBand` is a `byte` enum with the four values above; its numeric values are the wire bits of `equipmentAppearanceFlags[2:1]` (equipment-system.md CR-EQS-11). The Equipment System will call the mapping — it never hardcodes the thresholds.
- The outcome resolution helper takes the draw as a parameter; it does not own a random source (Story 004 injects the generator).
- The expected-scrolls function exists to pin the table against the GDD's economy numbers; it is not called at runtime.
- Use `double` for probabilities in the config and compare with the draw as `double`. Do not use `float` equality in tests — compare the F-ENH-5 values within the GDD's printed precision.
- Whether the config is later loaded from an asset or a server config file is not decided anywhere; this story only makes the values injectable.

---

## Out of Scope

- Story 002: the flat and elemental bonus formulas that read `BonusPerLevel` / `ElementalBonusPerLevel`
- Story 004: drawing the random value and running an attempt
- Equipment System epic: encoding the band into `equipmentAppearanceFlags` (AC-ENH-15, 16, 17, 32 are verified end to end there)
- Scroll prices (TK-ENH-9) — Item Database / NPC Shop data

---

## QA Test Cases

**File**: `tests/EditMode/EnhancementSystem/EnhancementSystem_Config_tests.cs` (new)

- **Defaults** — `EnhancementConfig.Default` exposes every value in the first criterion.
- **Table values** — success probability for each k = 0..9 equals the F-ENH-4 column; destruction equals `1 − P_s[k]`; sum is 1.00 for every row.
- **At the cap** — success probability for level 10, and for 11, throws `ArgumentOutOfRangeException`.
- **Not strictly decreasing** — table with two equal neighbours → construction throws. Table with an increase → throws.
- **Out of range** — a value of 0.96, and a value of 0.009 → throws. Boundary values 0.95 and 0.01 are accepted.
- **Length mismatch** — 9 entries with cap 10 → throws; 11 entries with cap 10 → throws.
- **Safe ranges not enforced** — cap 8 with an 8-entry table and thresholds 3 / 5 / 7 → accepted; cap 13 with a 13-entry table → accepted; thresholds 2 / 3 / 4 (each below its safe range, correctly ordered) → accepted.
- **Threshold ordering** — mid = glow → throws; glow = high → throws; high above the cap → throws; high equal to the cap → accepted.
- **Resolution** — level 2 (`P_s` 0.85): r = 0.00 → Success; r = 0.849 → Success; r = 0.85 → Destruction; r = 0.90 → Destruction. Level 0: r = 0.96 → Destruction (AC-ENH-12's premise).
- **Band mapping** — levels 0, 4 → NONE; 5, 6 → VISIBLE_NO_GLOW; 7 → GLOW_LOW; 8, 10 → HIGH. With a config whose thresholds are 4 / 6 / 9, the boundaries move accordingly. Levels 11 and 255 → HIGH, no exception.
- **Band values** — the enum's numeric values are 0, 1, 2, 3 in that order.
- **Expected scrolls** — +1 ≈ 1.1, +2 ≈ 2.3, +5 ≈ 10.9, +9 ≈ 2,727 (tolerance: the last printed digit).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnhancementSystem/EnhancementSystem_Config_tests.cs` — must exist and pass.

**Status**: [x] Created — 23 test methods + 73 parameterised cases (96 cases), passing (EditMode 1597/1597, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: None
- Unlocks: Story 002 (bonus provider), Story 003 (validation), Story 004 (attempt sequence); Equipment System epic (prestige band mapping, `MAX_ENHANCEMENT_LEVEL` for the composition root)

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 9/9 passing (none deferred)
**Deviations**: None blocking. Notes: the default values are constants in code (`EnhancementConstants`, and the F-ENH-4 table inside `EnhancementConfig.CreateDefault`) and reach consumers through the injectable `EnhancementConfig` — where they are loaded from at runtime is still undecided; validation also rejects a NaN probability, which this story did not ask for.
**Test Evidence**: Logic — `tests/EditMode/EnhancementSystem/EnhancementSystem_Config_tests.cs` (96 cases); EditMode 1597/1597 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all five applied (tuning-knob references corrected, `Default` built by `CreateDefault()`, per-tier defaults moved to `EnhancementConstants`, range message uses the constants, NaN test and all ten F-ENH-5 rows). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
**Files**: `src/Foundation/EnhancementSystem/EnhancementConfig.cs`, `EnhancementConstants.cs`, `PrestigeBand.cs`, `EnhancementOutcome.cs`.
