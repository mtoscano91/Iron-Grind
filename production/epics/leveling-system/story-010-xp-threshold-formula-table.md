# Story 010: XP Threshold Formula & Table

> **Epic**: Leveling System
> **Status**: Complete — implemented and closed 2026-09-25.
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours (implemented 2026-09-25)

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

## OQ-LS-7 Resolution (2026-09-24)

**OQ-LS-7** (`design/gdd/leveling-system.md`, Open Questions) is now **Resolved**: `GetXPAward(EntityID targetId): int` reads the target's `MobDefinition` via `IMobDefinitionRegistry.GetDefinition(MobTypeID)` (Enemy AI, already Approved) and returns `IsEnraged ? EnragedKillXP : KillXP` — both already-Approved flat `int` fields on `MobDefinition`. No level-differential modifier exists at MVP. Full resolution text, including a cross-document conflict found and corrected in `enemy-ai.md` (which had previously described Enemy AI awarding XP directly, which would have double-awarded it alongside the killer's controller's own sequence), `skill-system.md` (function signature correction), and `auto-attack-combat.md` (a missing kill-sequence rule, now added), is in `design/gdd/leveling-system.md`'s OQ-LS-7 entry — read it in full before implementing.

**Remaining gate**: AC-LS-31 still requires **economy-designer sign-off** on the full cumulative `XpThreshold` derivation table (this story's own acceptance criterion, unchanged by the OQ-LS-7 resolution). Run `/story-readiness` on this story to confirm READY before starting `/dev-story`.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story — do not implement until unblocked:*

- [x] **AC-LS-31** [BLOCKING]: `XpThreshold` array populated as a cumulative baseline (not per-level cost). Spot checks: `XpThreshold[1]=0`, `XpThreshold[2]=200`, `XpThreshold[20]=65824`, `XpThreshold[61]=int.MaxValue`. Exact int constants must be pre-computed in double precision and verified against a reference table at build time — the runtime float formula is not trusted for the authoritative values. *(Corrected 2026-09-24: the original `≈62,728` figure was an imprecise hand-arithmetic approximation — see `leveling-system.md` F-LS-1/EC-LS-35 corrections and TD-038. `65824` is independently verified via four separate computations against the stated formula and constants — two pre-implementation, one during implementation, one during code review.)* **Economy-designer sign-off GRANTED (2026-09-25)** on the array/curve itself — pacing, hook strength, and curve shape confirmed sound for MVP. The criterion's separate "205h cap-time anchor validated against realistic mob XP rates, per-tier-hours estimate" sub-clause was split out as unsatisfiable pending unpopulated `MobDefinition.KillXP` data and a not-yet-authored zone-XP GDD — see `leveling-system.md`'s AC-LS-31 text and TD-039.
- [x] **AC-LS-32** [BLOCKING]: `XpThreshold` is monotonically increasing for `i` in `[1,58]`: `XpThreshold[i] < XpThreshold[i+1]`.

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

**Status**: [x] Created and PASSING 2026-09-25 — `LevelingSystem_XpThresholdFormulaTable_tests.cs` (4 tests: AC-LS-31 spot-checks, AC-LS-31 array-length guard added during code review, AC-LS-31 full independent double-precision recomputation, AC-LS-32 monotonicity). Confirmed via a real run through the Unity Editor Test Runner — all 4 pass.

---

## Dependencies

- Depends on: OQ-LS-7 resolution (✓ Resolved 2026-09-24 — see above), economy-designer sign-off (✓ Granted 2026-09-25 on the array/curve — see AC-LS-31 note above)
- Unlocks: Nothing else in this epic strictly requires the REAL table (Story 008's sentinel test uses a test-local fake array) — this story can be picked up independently whenever OQ-LS-7 resolves, without blocking the rest of the epic

---

## Completion Notes
**Completed**: 2026-09-25
**Criteria**: 2/2 passing (AC-LS-31, AC-LS-32)
**Deviations**: ADVISORY — AC-LS-31's "205h cap-time anchor / per-tier-hours estimate" sub-clause was split out per economy-designer's explicit recommendation (unsatisfiable pending unpopulated `MobDefinition.KillXP` data and a not-yet-authored zone-XP GDD); the array/curve portion of AC-LS-31 was fully signed off. Tracked as TD-039.
**Test Evidence**: Logic — `tests/EditMode/LevelingSystem/LevelingSystem_XpThresholdFormulaTable_tests.cs`, 4 tests, confirmed passing via a real Unity Editor Test Runner run (2026-09-25).
**Code Review**: Complete — `unity-specialist` (APPROVED WITH SUGGESTIONS: table independently re-verified a 4th time with no bugs found; non-blocking suggestions on `Values`'s mutable backing array and a rounding-mode note, not applied) + `qa-tester` (initially BLOCKING on the array-length coverage gap, closed by adding `XpThreshold_ArrayLength_IsExactly62Entries`).
