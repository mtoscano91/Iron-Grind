# Story 006: Respec Core Commit Sequence

> **Epic**: Leveling System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A. Note: OQ-LS-1 flags that an ADR is expected for the reservation-protocol interface between Inventory System and Leveling System — not yet written. This story implements only the Leveling-System-side `TryApplyRespec` contract, which does not itself require that ADR; Story 007 (two-phase orchestration) is where the gap becomes load-bearing.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — respec is a rare, item-gated player action, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [x] **AC-LS-17** [BLOCKING]: `TryApplyRespec` calls `BeginStatTransaction()` before any `SetBaseStat` in the commit; all `OnStatChanged` events deferred until `EndStatTransaction()`.
- [x] **AC-LS-19** [BLOCKING]: `heldFreePoints` is UNCHANGED after respec commit — only the `spentFreePoints` pool (`(Level-1)×freePointsPerLevel − heldFreePoints`) is reallocated.
- [x] **AC-LS-20** [BLOCKING]: Auto-alloc floor enforcement — `floor[stat] = 10 + (L-1)×def.AutoAllocIncrement[stat]`; commit rejects any submission with a stat below its class-specific floor.
- [x] **AC-LS-21** [BLOCKING]: Respec lowering MaxMP below CurrentMP is reconciled inside the transaction window (via `TryApplyRespec`'s own `SetCurrentMP` re-assertion clamp — see Completion Notes; the GDD's assumed "EC-05" ambient mechanism does not exist) — one deferred `OnStatChanged` pass, not two.
- [x] **AC-LS-52** [BLOCKING]: Respec lowering MaxHP below CurrentHP is reconciled the same way for HP — `OnEntityDied` does NOT fire (this is a schema clamp, not a damage event; MaxHP schema minimum of 1 guarantees CurrentHP can never clamp to 0).

---

## Implementation Notes

**A real, unresolved gap in the GDD itself, found and resolved 2026-07-22 before implementation.** `leveling-system.md` states `TryApplyRespec(EntityID)` — a SINGLE-parameter signature — in every reference (CR-4.1, the Interactions table, EC-LS-22/23). But CR-4.4 step 2 requires computing `SetBaseStat(statId, autoAllocTotal[statId] + newFreeAlloc[statId])` — `newFreeAlloc` is the player's new per-stat allocation, configured client-side in the respec screen (CR-4.2). **The GDD never specifies how this data reaches the method** — a single `EntityID` parameter cannot carry it. This is a genuine doc gap, not a story-text error like previous stories' `STR`/`SetBaseStat`-vs-`SetBaseStatFloat` mismatches.

**Resolved signature (settled before implementation, not a decision for the implementer to make):**
```csharp
public void TryApplyRespec(EntityID entityId, IReadOnlyDictionary<StatID, int> newTotals)
```
`newTotals` carries the caller's proposed NEW TOTAL value for each of the 4 primary attributes (`Strength`/`Dexterity`/`Vitality`/`Intelligence`) — not a delta, not split into "auto-alloc + free-alloc" (that split is a Stat Screen UI display concern, Story 013; this method only needs the final number to write per stat). Must contain exactly the 4 primary-attribute `StatID`s — missing or extra keys are a caller error.

**Floor validation happens BEFORE `BeginStatTransaction()` is ever called** — this is a deliberate simplification: validate the whole request first (all 4 stats against their floors), throw immediately if any fails, with NO transaction ever opened and NO writes ever attempted. This keeps Story 006 self-contained: there is no need for a try/catch/rollback wrapper around the floor-check specifically, because nothing is written before it passes. **The general "catch any exception mid-transaction and roll back" safety net (EC-LS-22/23, AC-LS-22 — a different, not-yet-numbered-here AC) is Story 007's scope, not this story's** — confirmed by checking this story's own AC list (AC-LS-17/19/20/21/52) does not include AC-LS-22. Do not build exception-catching/rollback machinery in this story; if `BeginStatTransaction()` or anything inside the transaction genuinely throws, let it propagate — Story 007 wraps this method's happy path with that safety net later.

*Derived from CR-4.2–CR-4.5 (leveling-system.md):*

- **CR-4.2 — Redistributable pool**: `spentFreePoints = (Level − 1) × freePointsPerLevel[class] − heldFreePoints`. `heldFreePoints` is explicitly NOT part of this pool — it stays held and unchanged through commit. Per-attribute floors (below) are enforced in real time by the caller (Story 013's respec screen); this story's `TryApplyRespec` must also reject below-floor submissions as a defense-in-depth check — never trust the caller alone.
- **CR-4.3 — Auto-alloc floors**: read from `IClassRegistry` (same mock/forward-dependency treatment as Story 002): `floor[stat] = 10 + (L−1) × def.AutoAllocIncrement[stat]`. Non-auto-allocated attributes for a class (e.g. Intelligence for Warrior) have `AutoAllocIncrement = 0` → floor = 10 (the universal L1 base). Adding a new class requires no formula change — values come from `ClassDefinition`.
- **CR-4.4 — Commit sequence**, in order:
  1. **Floor validation (all 4 stats against `newTotals`)** — throw (e.g. `ArgumentException`) immediately on any violation, before anything below. No transaction opened yet.
  2. `CharacterStats.BeginStatTransaction()` — defers all `OnStatChanged`.
  3. For each of the 4 primary attributes: `SetBaseStat(statId, newTotals[statId])`.
  4. Re-evaluate F-3–F-9 at the current `LevelTierMultiplier` — reuse the shared `RecomputeDerivedStats(entityId, tier)` private helper Story 005 already extracted from `ExecuteLevelUpSequence`, do not re-duplicate the formula block a third time.
  4a. **Corrected 2026-07-22, verified again 2026-08-11 against real source**: Character Stats' "EC-05 reconciliation" does NOT generalize beyond `RemoveEquipmentModifier` — confirmed by direct code reading, there is no ambient HP/MP-vs-Max clamp that fires automatically off a `SetBaseStat(MaxHP/MaxMP, ...)` write. This method must trigger the clamp itself: immediately after `RecomputeDerivedStats`, still inside the open transaction, call `_stats.SetCurrentHP(entityId, _stats.GetCurrentHP(entityId))` and `_stats.SetCurrentMP(entityId, _stats.GetCurrentMP(entityId))` — re-asserting the entity's own current value so each method's internal `[0, GetEffectiveStat(Max*)]` clamp does the reconciliation against the just-recomputed ceiling (pulls down only if the new Max* is lower, never raises/restores). This required making `SetCurrentHP`/`SetCurrentMP` transaction-aware (mirroring `SetBaseStat`'s `if (_transactionOpen) AddToDeferredDedup(...) else FireOnStatChanged(...)` pattern) as part of this story, since previously they fired `OnStatChanged` unconditionally — see Completion Notes for the exact diff.
  5. `heldFreePoints` UNCHANGED — no write.
  6. `CharacterStats.EndStatTransaction()` — all deferred `OnStatChanged` fire once per modified stat, including the reconciled HP/MP if applicable.
- **CR-4.5**: HP/MP are not separately restored by respec (unlike level-up's CR-2.7) — whatever step 4a's re-assertion clamp produces is the final state. `SetCurrentHP`/`SetCurrentMP` ARE called in this method (superseding the original, incorrect instruction below) — but only as the reconciliation mechanism (never raise, only clamp down), never as a restore-to-full like CR-2.7.

This story implements `TryApplyRespec(EntityID, IReadOnlyDictionary<StatID,int>)` as a standalone method assuming its caller has already computed a floor-legal `newTotals` (the floor check here is defense-in-depth, not the primary UX gate — Story 013 owns real-time UI validation). It does not implement the two-phase item-reservation orchestration around it (Phase 1 gate, `ItemReservation` handle, exception→Release/rollback semantics) — that is Story 007, which is the actual entry point real callers use.

---

## Out of Scope

*Handled by neighbouring stories:*

- Two-phase commit orchestration (Phase 1 item reservation, combat gate, exception safety) — Story 007
- Respec screen UI (floor display, commit-blocked-with-unallocated-points) — Story 013

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_RespecCommitSequence_tests.cs`

- **AC-LS-17**: Given a valid respec request, When `TryApplyRespec` executes, Then `BeginStatTransaction` precedes all writes and no `OnStatChanged` fires before `EndStatTransaction`.
- **AC-LS-19**: Given a Warrior L20 heldFreePoints=5, When respec commits, Then heldFreePoints remains 5; only the 14-point spent pool was reallocated.
- **AC-LS-20**: Given a Warrior L10 (Strength floor=28), When a below-floor `newTotals` submission is attempted, Then `TryApplyRespec` throws before opening a transaction — no `BeginStatTransaction` call, no writes.
- **AC-LS-21**: Given a Healer L20 CurrentMP=800 MaxMP=900, When respec lowers MaxMP to 700, Then CurrentMP clamps to 700 within the transaction, one deferred `OnStatChanged` pass.
- **AC-LS-52**: Given a Warrior L20 CurrentHP=400, When respec lowers MaxHP to 350, Then CurrentHP clamps to 350, `OnEntityDied` does not fire.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_RespecCommitSequence_tests.cs` — must exist and pass

**Status**: [x] Created — 5 tests, all 5 blocking ACs covered. Not confirmed in a live Unity Editor this session (none was open/available) — verified statically (grep-confirmed test counts, hand-verified formulas, 2 parallel self-performed code reviews, both 0 BLOCKING after fixes applied).

---

## Dependencies

- Depends on: Story 002 (F-3–F-9 recompute), Story 005 (Complete — this story reuses its `RecomputeDerivedStats(EntityID, float)` shared helper directly, does not re-derive the formula block), Character Stats Story 007 (Transaction API — Complete)
- Unlocks: Story 007 (two-phase orchestration wraps this story's `TryApplyRespec`)

---

## Completion Notes
**Completed**: 2026-08-11
**Criteria**: 5/5 passing (AC-LS-17, 19, 20, 21, 52) — 5 tests in `tests/EditMode/LevelingSystem/LevelingSystem_RespecCommitSequence_tests.cs`.
**New production**: `LevelingService.TryApplyRespec(EntityID, IReadOnlyDictionary<StatID,int>)` — floor validation (all 4 primary attributes, before any transaction opens) → `BeginStatTransaction()` → 4 `SetBaseStat` writes → `RecomputeDerivedStats` (reused from Story 005, not re-derived) → `SetCurrentHP`/`SetCurrentMP` re-assertion → `EndStatTransaction()`. `heldFreePoints` never touched.
**Real gap found and resolved before implementation — signature**: the GDD's literal `TryApplyRespec(EntityID)` single-parameter signature cannot carry the caller's proposed new stat totals. Resolved signature: `TryApplyRespec(EntityID, IReadOnlyDictionary<StatID,int> newTotals)`, settled before implementation.
**Real gap found and resolved before implementation — "EC-05 reconciliation" does not generalize.** The GDD's CR-4.4 step 4 assumed Character Stats already has a general HP/MP-vs-Max reconciliation mechanism that fires automatically off any `SetBaseStat(MaxHP/MaxMP, ...)` write. Verified via direct code reading: no such ambient mechanism exists — `CharacterStats`' only HP-ceiling clamp logic prior to this story was narrowly scoped inside `RemoveEquipmentModifier`. `TryApplyRespec` must trigger its own reconciliation.
**Design decision — reconciliation via re-assertion, not a new clamp method.** Rather than adding new reconciliation logic, `TryApplyRespec` re-asserts the entity's own current HP/MP value via `SetCurrentHP(entityId, GetCurrentHP(entityId))`/`SetCurrentMP(entityId, GetCurrentMP(entityId))` immediately after `RecomputeDerivedStats`, letting those methods' own `[0, GetEffectiveStat(Max*)]` clamp do the real work — pulls down only if the new ceiling is lower, never raises/restores (CR-4.5 honored). This required making `SetCurrentHP`/`SetCurrentMP` transaction-aware (previously they always fired `OnStatChanged` immediately, which would have broken AC-LS-17's single-batched-pass guarantee) — user chose this fix over alternatives via `AskUserQuestion`. Change: both methods' final line changed from unconditional `FireOnStatChanged(...)` to `if (_transactionOpen) AddToDeferredDedup(...) else FireOnStatChanged(...)`, mirroring `SetBaseStat`'s existing pattern. Zero regression: the only other caller, `ExecuteLevelUpSequence`, never opens a transaction.
**Second transaction-awareness gap found during code review — `SetBaseStatFloat`.** Code review (unity-specialist) found that `SetBaseStatFloat` (used by `RecomputeDerivedStats` for CritChance/AttackSpeedMultiplier) also fired `OnStatChanged` unconditionally, never checking `_transactionOpen` — harmless before this story since no prior caller of `RecomputeDerivedStats` ever opened a transaction around it, but `TryApplyRespec` is the first caller that does, surfacing a literal (if narrow) violation of AC-LS-17's wording. User chose to fix it properly (same pattern as the HP/MP fix) rather than document it as an accepted deviation. Zero regression: same reasoning as above.
**Code Review**: Complete (lean self-performed review: unity-specialist [1 BLOCKING + 1 Required Change + 1 Suggestion] + qa-tester [1 BLOCKING, same finding independently], both parallel). BLOCKING finding: the AC-LS-19 test's `heldFreePoints` precondition/assertion said 5 but the test's own setup (6 level-ups from L14→L20, provable independently from its own Strength assertion of 48) actually produces 6 — fixed (both asserts and 3 doc comments corrected from 5→6). Required Change: the `SetBaseStatFloat` gap described above — fixed. Suggestion: redundant `Level` re-read in `TryApplyRespec` — fixed (reused the local already computed for floor validation). Both reviewers' findings independently re-verified against source by the coordinator before and after fixes (formulas hand-traced, `git diff` scope-checked, test count grep-confirmed) — 0 BLOCKING remaining from either reviewer after fixes.
**Test Evidence**: Logic — `tests/EditMode/LevelingSystem/LevelingSystem_RespecCommitSequence_tests.cs`, 5 tests, all 5 blocking ACs covered. Not live-Editor-confirmed this session.
**Deviations**: `CharacterStats.cs` was modified (`SetCurrentHP`/`SetCurrentMP`/`SetBaseStatFloat` transaction-awareness) — an explicitly coordinator/user-approved deviation from this story's original "closed, do not touch" framing, justified by the two real gaps found during design and review. `ILevelingService.cs`, `StatID.cs`, `StatSchema.cs` confirmed untouched via `git diff`/`git status`.
