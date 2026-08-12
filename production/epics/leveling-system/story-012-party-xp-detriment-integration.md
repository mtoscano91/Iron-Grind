# Story 012: Party XP Detriment Integration

> **Epic**: Leveling System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-012`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — party XP grants fire once per kill event, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [ ] **AC-LS-33** [BLOCKING]: F-PS-1 party XP detriment multipliers correct at all party sizes — N=1→×1.00, N=2→×0.90, N=3→×0.80, N=4→×0.70 (for `XP_base=1000`). Float-rounding edge case: `XP_base=333`, N=4 → `Mathf.RoundToInt(333×0.7f) = 233`, not `(int)` truncation.
- [ ] **EC-LS-32** [BLOCKING]: The Leveling System is party-unaware (CR-1.5) — it receives only the final per-player XP grant and has no opinion on which `N` value the Party System used or whether a departed member is eligible. This is a confirmed non-case for this story to test as a NEGATIVE assertion: the Leveling System's `AddExperience` call site has no party-size parameter anywhere in its signature.

---

## Implementation Notes

*Derived from CR-1.5, F-LS-2 (superseded), EC-LS-32/33 (leveling-system.md):*

- **This is fundamentally a test-of-the-boundary story, not a new-formula story.** `F-LS-2` (a +30% party XP BONUS) is explicitly **SUPERSEDED** by `party-system.md`'s `F-PS-1_PartyXpDeduction` (a DETRIMENT: `XP_base × (1.0f − 0.10f × (N−1))`, opposite direction). **Do not implement F-LS-2.** The canonical formula is owned by the Party System GDD, which is Approved but has no epic/stories yet in this codebase.
- The actual per-member XP computation (F-PS-1) is the Party System's responsibility, not the Leveling System's. This story's job is narrower: prove that `AddExperience(EntityID, amount)` — the ONLY entry point the Party System calls — correctly accepts a pre-computed, already-detriment-adjusted `int` amount, with the `Mathf.RoundToInt` (not truncating cast) conversion happening on the Party-System side before the call (confirmed contract, OQ-LS-4 resolved 2026-05-17).
- Since no real Party System implementation exists yet, this story tests the CONTRACT via a test-local stand-in that computes F-PS-1 itself (for test purposes only — this is test code proving the interface shape, not production Party System code) and calls the real `AddExperience` with the result. This mirrors the mock-provider pattern used throughout this codebase for forward dependencies.
- **EC-LS-32 negative assertion**: write at least one test that constructs the `AddExperience` call signature and confirms it has no party-related parameter — this is a structural/reflection-style guard against a future regression where someone "helpfully" adds an `N` parameter to `AddExperience`, which would violate CR-1.5's party-unaware design.

---

## Out of Scope

*Handled by neighbouring stories:*

- `AddExperience`'s own accumulation/threshold logic — Story 001
- The real F-PS-1 implementation, party size determination, and XP range radius — future Party System epic (not yet created)

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_PartyXpDetrimentIntegration_tests.cs`

- **AC-LS-33**: Given `XP_base=1000`, When F-PS-1 (test-local stand-in) is evaluated at N=1,2,3,4, Then results are 1000/900/800/700; given `XP_base=333` at N=4, Then `Mathf.RoundToInt(233.1f)=233` via the confirmed rounding method, then `AddExperience(entity, 233)` succeeds normally.
- **EC-LS-32**: Given the `AddExperience` public signature, When inspected (reflection or direct signature check), Then no party-size or `N` parameter exists anywhere in it.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_PartyXpDetrimentIntegration_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`AddExperience` itself)
- Unlocks: None — the real Party System epic will supersede this story's test-local F-PS-1 stand-in with a real implementation once it exists; this story's tests should still pass unmodified at that point since they only exercise the `AddExperience` boundary
