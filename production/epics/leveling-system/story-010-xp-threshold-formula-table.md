# Story 010: XP Threshold Formula & Table

> **Epic**: Leveling System
> **Status**: Blocked — OQ-LS-7 (`GetXPAward` unspecified) must resolve before this story can be implemented, per the GDD's own explicit text
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours (once unblocked)

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-011`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — the `XpThreshold` array is a static, build-time-populated lookup table; runtime cost is a single array index read, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## BLOCKED — Do Not Implement Yet

**OQ-LS-7** (`design/gdd/leveling-system.md`, Open Questions): `GetXPAward(EntityID): int` is unspecified anywhere in the design set, despite being the sole XP faucet at MVP and referenced by this GDD (CR-1.1, AC-LS-31), Damage Calculation, and Auto-Attack Combat. It is the root cause of the unvalidated 205-hour cap-time anchor this table's constants depend on.

**The GDD's own text is explicit**: "This OQ must be resolved before the implementation sprint begins — the Leveling System cannot be fully validated without it." AC-LS-31 additionally requires an **economy-designer co-sign** on the full cumulative `XpThreshold` derivation table, which itself requires `GetXPAward`'s resolution first (to validate the 205h anchor and per-tier pacing against realistic mob XP rates).

**Do not implement this story until**:
1. `GetXPAward(EntityID): int` is specified (data source, level-differential modifier, mob-XP schema, ownership) — likely as an addition to a not-yet-authored Mob Definition sub-GDD or an Economy System XP Rate appendix.
2. Economy-designer sign-off is obtained on the derived `XpThreshold` cumulative table.

Run `/story-readiness` on this story once both gates clear — it will currently return BLOCKED.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story — do not implement until unblocked:*

- [ ] **AC-LS-31** [BLOCKING]: `XpThreshold` array populated as a cumulative baseline (not per-level cost). Spot checks: `XpThreshold[1]=0`, `XpThreshold[2]=200`, `XpThreshold[20]≈62,728`, `XpThreshold[61]=int.MaxValue`. Exact int constants must be pre-computed in double precision and verified against a reference table at build time — the runtime float formula is not trusted for the authoritative values. **Requires economy-designer co-sign** (see blocker above).
- [ ] **AC-LS-32** [BLOCKING]: `XpThreshold` is monotonically increasing for `i` in `[1,58]`: `XpThreshold[i] < XpThreshold[i+1]`.

---

## Implementation Notes

*Derived from F-LS-1 (leveling-system.md) — read in full once unblocked:*

```
XP(L) = Mathf.RoundToInt(C × L^α × R^L)
```
C=178.6, α=0.648, R=1.1198 (interdependent — recalibrate all three together if any anchor point shifts; never tune in isolation). `XP(L)` is the PER-LEVEL cost (width of level L's bar); the implementation array `XpThreshold` is the CUMULATIVE baseline: `XpThreshold[1]=0`, `XpThreshold[L] = Σ XP(i) for i=[1,L-1]`. Sentinel: `XpThreshold[61] = int.MaxValue`. Do not confuse `XP(L)` (formula output) with `XpThreshold[L]` (array index) — the GDD calls this out explicitly as a common implementation error.

---

## Out of Scope

*Handled by neighbouring stories:*

- Consumption of the table (level-up threshold checks, at-cap clamping) — Stories 002, 003, 008
- `LevelTierMultiplier` (a separate table, F-LS-3) — Story 011

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_XpThresholdFormulaTable_tests.cs` (do not create until unblocked)

- **AC-LS-31**: Spot-check `XpThreshold` at indices 1, 2, 20, 61 against the economy-designer-signed reference table.
- **AC-LS-32**: Iterate `XpThreshold[1..59]`, assert strict monotonic increase.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_XpThresholdFormulaTable_tests.cs` — must exist and pass, once unblocked

**Status**: [ ] Not yet created — BLOCKED, do not start

---

## Dependencies

- Depends on: OQ-LS-7 resolution (external — Mob Definition / Economy System GDD authorship, not a Leveling System story), economy-designer sign-off
- Unlocks: Nothing else in this epic strictly requires the REAL table (Story 008's sentinel test uses a test-local fake array) — this story can be picked up independently whenever OQ-LS-7 resolves, without blocking the rest of the epic
