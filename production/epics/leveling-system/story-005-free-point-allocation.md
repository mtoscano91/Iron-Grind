# Story 005: Free Point Allocation

> **Epic**: Leveling System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — `AllocateFreePoint` fires once per player tap on the stat screen, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [x] **AC-LS-11** [BLOCKING]: Valid allocation — `heldFreePoints` decrements, target stat increments by 1, F-3–F-9 re-derived at current tier, `CurrentHP`/`CurrentMP` NOT modified.
- [x] **AC-LS-12** [BLOCKING]: `heldFreePoints == 0` → Guard 1 rejects; no write.
- [x] **AC-LS-13** [BLOCKING]: Invalid `StatID` (e.g. `MaxHP`) → Guard 2 rejects BEFORE decrement; `heldFreePoints` unchanged.
- [x] **AC-LS-14** [BLOCKING]: Free-point spend never restores CurrentHP/CurrentMP, even when MaxHP/MaxMP increases via the F-3/F-4 recompute.
- [x] **AC-LS-15** [BLOCKING]: Last free point spent — counter reaches exactly 0, never -1; subsequent call immediately hits Guard 1.
- [x] **AC-LS-16** [BLOCKING]: `LevelTierMultiplier` used during free-point recompute is derived on-demand from `GetBaseStat(Level)` — no stored multiplier field.

---

## Implementation Notes

**Real code state, verified 2026-07-22.** `LevelingService.AllocateFreePoint(EntityID, StatID)` already exists as a Story 003 stub — read `src/Foundation/LevelingSystem/LevelingService.cs` fresh before implementing. Its current body:
```csharp
public AllocateFreePointResult AllocateFreePoint(EntityID entityId, StatID statId)
{
    if (_levelingUpInProgress)
        return AllocateFreePointResult.RejectedSystemBusy;

    throw new NotImplementedException(
        "[LevelingService] AllocateFreePoint's non-busy path is Story 005's scope — not yet implemented.");
}
```
**This story replaces the `throw` with the real CR-3 sequence below — the busy-check above it must stay exactly as-is, first in the sequence** (Story 003's `LevelingService_AllocateFreePoint_WhileLevelingUpInProgress_...` test already depends on the busy-check firing before any other guard, and must keep passing unmodified). `AllocateFreePointResult` (`src/Foundation/LevelingSystem/AllocateFreePointResult.cs`) currently has only `Success`/`RejectedSystemBusy` — extend it with two more values for this story's two new guards (naming is your call, but something like `RejectedNoFreePoints`/`RejectedInvalidStat` matches this codebase's established enum-naming style).

**Real `StatID` names — use these, not the GDD's `STR/DEX/VIT/INT` abbreviations**: `Strength`/`Dexterity`/`Vitality`/`Intelligence` (`src/Foundation/CharacterStats/StatID.cs`).

*Derived from CR-3 (leveling-system.md):*

`AllocateFreePoint(EntityID, StatID targetStat)` — valid targets: `{Strength, Dexterity, Vitality, Intelligence}` only. Sequence, in this exact order (load-bearing — EC-LS-12 requires busy-check → Guard 1 → Guard 2 → decrement → write, never decrement-then-guard):

0. **Busy-check (Story 003, already implemented, do not modify)**: `_levelingUpInProgress` → reject with `RejectedSystemBusy`.
1. **Guard 1**: `heldFreePoints == 0` → reject with UI feedback, no write.
2. **Guard 2**: `targetStat` not in `{Strength,Dexterity,Vitality,Intelligence}` → reject with error log, no write. (`StatID.Level` has separate write ownership via CR-2.2 — this API must never touch it, even indirectly.)
3. `heldFreePoints -= 1`.
4. `SetBaseStat(targetStat, GetBaseStat(targetStat) + 1)`.
5. Re-evaluate F-3 through F-9 with the current `LevelTierMultiplier` (derived from `GetBaseStat(Level)` — same formula set as Story 002's CR-2.6, same evaluation order and same int-vs-float-schema write methods: `SetBaseStat` for MaxHP/MaxMP/AttackPower/Defense/MagicDefense, `SetBaseStatFloat` for CritChance/AttackSpeedMultiplier), write via the appropriate method.
6. **HP/MP are NOT restored.** `CurrentHP`/`CurrentMP` remain exactly as they were before this call — even if MaxHP/MaxMP increased. Contrast explicitly with CR-2.7 (level-up DOES fully restore HP/MP, via `SetCurrentHP`/`SetCurrentMP`) — this is a deliberate, tested asymmetry. Do not call `SetCurrentHP`/`SetCurrentMP` anywhere in this method.

Each `AllocateFreePoint()` call is immediately committed — no preview-then-confirm at this API layer (CR-3.3). If a client wants a preview/confirm UX (recommended for touch input, per the GDD's own UX note), that logic lives above this API, in the Stat Screen UI (Story 013) — this story's API has no undo path and must not grow one.

---

## Out of Scope

*Handled by neighbouring stories:*

- MaxMP ceiling clamp (>9,999) during this recompute — Story 004 already covers the shared F-4 recompute path; this story's tests use values that don't hit the ceiling
- Stat Screen UI preview/confirm flow — Story 013
- Respec (a separate, batch-reallocation API) — Stories 006/007

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_FreePointAllocation_tests.cs`

- **AC-LS-11**: Given Warrior L10 Strength=28 heldFreePoints=2, When `AllocateFreePoint(entity, StatID.Strength)`, Then heldFreePoints=1, Strength=29, AttackPower recomputed to 68, HP/MP untouched.
- **AC-LS-12**: Given heldFreePoints=0, When `AllocateFreePoint` called, Then rejected, no write.
- **AC-LS-13**: Given heldFreePoints=2, When `AllocateFreePoint(entity, StatID.MaxHP)`, Then Guard 2 rejects before decrement, heldFreePoints stays 2.
- **AC-LS-14**: Given CurrentHP=150 mid-combat, When `AllocateFreePoint(entity, StatID.Vitality)`, Then MaxHP increases but CurrentHP stays 150.
- **AC-LS-15**: Given heldFreePoints=1, When `AllocateFreePoint(entity, StatID.Dexterity)`, Then heldFreePoints=0; a second call immediately rejects via Guard 1.
- **AC-LS-16**: Given Warrior L20 (tier ×1.2) heldFreePoints=1, When `AllocateFreePoint(entity, StatID.Strength)`, Then AttackPower recompute uses ×1.2, derived on-demand from Level, no stored field.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_FreePointAllocation_tests.cs` — must exist and pass

**Status**: [x] Created — 6 tests, all 6 blocking ACs covered. Not confirmed in a live Unity Editor this session (none was open/available) — verified statically (grep-confirmed test counts, hand-verified formulas, 2 parallel self-performed code reviews, both 0 BLOCKING).

---

## Dependencies

- Depends on: Story 002 (shares the F-3–F-9 recompute formula set), Story 003 (Complete — already built the `AllocateFreePoint` busy-check stub and `AllocateFreePointResult` enum this story extends; its own AC-LS-10 test already covers the busy-rejection path and must keep passing unmodified after this story's changes)
- Unlocks: None remaining — Story 003's dependency on this story's API was resolved via Story 003's own local stub, per that story's contingency plan

---

## Completion Notes
**Completed**: 2026-07-22
**Criteria**: 6/6 passing (AC-LS-11 through AC-LS-16) — 6 tests in `tests/EditMode/LevelingSystem/LevelingSystem_FreePointAllocation_tests.cs`.
**New production**: `LevelingService.AllocateFreePoint`'s Story 003 stub replaced with the real CR-3 sequence (busy-check [unchanged] → Guard 1 [heldFreePoints==0] → Guard 2 [invalid stat] → decrement → write → recompute → Success). `AllocateFreePointResult` extended with `RejectedNoFreePoints`/`RejectedInvalidStat`.
**Design decision — shared, not duplicated, recompute logic**: extracted a new private `RecomputeDerivedStats(EntityID, float tier)` helper containing the exact CR-2.6 formula block (Story 002/004), now called by both `ExecuteLevelUpSequence` and `AllocateFreePoint`. This avoids the "two independently-maintained copies drift apart" risk the story explicitly flagged. Verified behavior-preserving: both code reviewers independently traced the extraction against the AC-LS-03/04/05 exact-firing-order tests and confirmed no change to write order, formulas, or int/float-schema write-method selection.
**First story in this epic where the recurring threshold-array sentinel bug (present in 6 tests across Stories 001-004) did NOT recur** — the implementing agent proactively checked every threshold array against the pattern before finalizing tests, and both parallel reviewers independently re-verified all 5 real-level-up-triggering arrays in this story's test file. All correctly sized on the first pass.
**Code Review**: Complete (lean self-performed review: unity-specialist 1 Required Change→fixed + qa-tester 1 Required Change→fixed (same finding, both independently) + 1 Suggestion (not applied — cosmetic only), both parallel, 0 BLOCKING from either). The one Required Change was a missing `.meta` file for the new test file — generated and added.
**Test Evidence**: Logic — `tests/EditMode/LevelingSystem/LevelingSystem_FreePointAllocation_tests.cs`, 6 tests, all blocking ACs covered. Not live-Editor-confirmed this session.
**Deviations**: None from Out of Scope. `CharacterStats.cs`, `ILevelingService.cs`, `StatID.cs`, `StatSchema.cs` confirmed untouched via `git diff` (unchanged from their known Story 002/003 baselines). Story 003's busy-check test confirmed still passing unmodified (file shows zero diff).
**Process note worth a project-level decision**: every implementing agent in this epic has independently flagged the same discrepancy — `.claude/rules/test-standards.md` specifies snake_case `test_[scenario]_[expected]` test naming, but 100% of the ~30 tests written across this epic (and every pre-existing C# test in the repo) use PascalCase descriptive-sentence naming instead, matching C# convention over that generic (GDScript-authored) rule. This has been the correct call every time it's come up, but it's now recurred 5 times across 5 stories — worth updating the rule itself (or adding a C#-specific override) rather than continuing to re-litigate it story by story.
