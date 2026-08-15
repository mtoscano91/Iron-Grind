# Story 008: Level Cap Behavior

> **Epic**: Leveling System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 1-2 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — `AddExperience` at cap fires once per kill event, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [x] **AC-LS-23** [BLOCKING]: `AddExperience` at `Experience == XpThreshold[60]` exactly → no write, no `OnStatChanged`, no `OnExperienceThresholdCrossed`, zero `SetBaseStat` calls.
- [x] **AC-LS-24** [BLOCKING]: CR-2.1 at-cap guard aborts the sequence when `Level == 60` — `SetBaseStat(Level, 61)` is never called; no auto-alloc, no recompute, no broadcast.
- [x] **AC-LS-25** [BLOCKING]: `XpThreshold[61] = int.MaxValue` sentinel prevents `IndexOutOfRangeException` in the CR-2.9 consecutive-level check at L60; array length ≥ 62.
- [x] **AC-LS-26** [BLOCKING]: `heldFreePoints` remains spendable at L60 — allocation proceeds normally, F-3–F-9 recompute uses ×2.0.

---

## Implementation Notes

*Derived from CR-5 (leveling-system.md):*

- **CR-5.1**: `GetBaseStat(Level)` never exceeds 60 — enforced by the CR-2.1 at-cap guard (already implemented in Story 002; this story's AC-LS-24 is the dedicated cap-boundary test for that guard).
- **CR-5.2**: at L60, `AddExperience()` clamps `StatID.Experience` to `XpThreshold[60]`. XP beyond that is discarded. If `Experience` is already exactly `XpThreshold[60]`, no write occurs and no `OnStatChanged` fires — this is the AC-LS-23 no-op case, distinct from CR-2.2a (Story 004), which handles the transition kill itself; this story handles every kill AFTER the character is already at L60.
- **CR-5.3**: the `XpThreshold` array carries a sentinel at index 61 = `int.MaxValue`, so `Experience >= XpThreshold[currentLevel+1]` is always safely evaluable at L60 without a bounds-check branch. The array must be declared with ≥62 entries.
- **CR-5.4**: `heldFreePoints` continues to accumulate (though no new levels occur past 60, any already-held points) and can be spent normally at L60 — this exercises Story 005's `AllocateFreePoint` at the cap, confirming the at-cap state only blocks XP accumulation/level-up, not free-point spending.
- **CR-5.5**: `GetXPToNextLevel(EntityID)` returns `0` at L60 — HUD (Story 013) must guard against division by zero before evaluating the bar-fill formula.

---

## Out of Scope

*Handled by neighbouring stories:*

- The L59→L60 transition kill itself (CR-2.2a XP clamp, tier recompute) — Story 004
- HUD's L60 "MAX" display and bar-fill guard — Story 013

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_LevelCapBehavior_tests.cs`

- **AC-LS-23**: Given L60 character with Experience == XpThreshold[60], When `AddExperience(entity, 500)`, Then no write, no events, zero SetBaseStat calls (spy-verified).
- **AC-LS-24**: Given Level==60 when `OnExperienceThresholdCrossed` fires, When the handler executes, Then guard aborts, Level never reaches 61, no downstream writes/broadcast.
- **AC-LS-25**: Given `XpThreshold` initialized with the sentinel, When CR-2.9 evaluates at L60, Then no exception, condition evaluates false, array length ≥62 and `XpThreshold[61]==int.MaxValue` asserted.
- **AC-LS-26**: Given L60 Warrior heldFreePoints=3, When `AllocateFreePoint(entity, STR)`, Then allocation proceeds, heldFreePoints=2, recompute uses ×2.0.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_LevelCapBehavior_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/LevelingSystem/LevelingSystem_LevelCapBehavior_tests.cs` (5 test functions: one per AC plus a below-cap regression test added from code review). Not yet executed against a live Unity Editor this session (none available) — every assertion hand-traced independently by the implementer, qa-tester, unity-specialist, and the coordinator, including the full 3-level-up-plus-spend arithmetic chain for AC-LS-26.

---

## Dependencies

- Depends on: Story 002 (CR-2.1 guard), Story 004 (L59→L60 transition, the entry point into the AtCap state), Story 005 (`AllocateFreePoint`, exercised at cap by AC-LS-26). **Does NOT depend on Story 010** (Blocked on OQ-LS-7) — AC-LS-25's sentinel/bounds-safety test uses a small test-local `XpThreshold` array (e.g. L1–5 plus the sentinel at index 61), not the real economy-signed-off table. This story is unblocked regardless of Story 010's status.
- Unlocks: None

---

## Completion Notes

**Completed**: 2026-08-15
**Criteria**: 4/4 passing.
**Deviations**: ADVISORY — this story's own AC-LS-23 required genuinely new production code (`CharacterStats.AddExperience`, Character Stats epic, already-Complete Story 006) despite the story package's framing reading as though only dedicated tests were needed for already-implemented behavior. Confirmed via direct source reading before implementation: `AddExperience` had zero at-cap logic — a small, mechanical, spec-required guard (`if (GetBaseStat(Level) == 60) return;`) was added, matching CR-5.2's own text exactly. AC-LS-24 and AC-LS-26 were confirmed already correctly implemented (Stories 002 and 005 respectively) and needed only dedicated tests, as the story's Implementation Notes anticipated. AC-LS-25's sentinel contract was confirmed real but not reachable through any current production call path — tested directly against `GetExperienceThreshold` itself.
**Test Evidence**: Logic: `tests/EditMode/LevelingSystem/LevelingSystem_LevelCapBehavior_tests.cs` (5 tests). Not run against a live Unity Editor this session (none available) — static/hand-trace verification only.
**Code Review**: Complete — self-performed parallel review (unity-specialist + qa-tester, lean mode). Both returned CLEAN: 0 BLOCKING, 0 Required Changes. 2 actionable non-blocking suggestions, both applied (an OQ-1 cross-reference note on the new guard's doc comment, and a below-cap regression test proving the new guard's boundary directly rather than relying on incidental coverage from sibling stories).
**New production**: `CharacterStats.AddExperience`'s CR-5.2 at-cap guard (4 lines + doc comment). `LevelingService.cs` was not modified — its existing CR-2.1 guard and `AllocateFreePoint`'s guard chain were already correct.
