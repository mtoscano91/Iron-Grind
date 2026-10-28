# Story 004: Refresh on Reapply

> **Epic**: Status Effects / Buffs
> **Status**: Complete — 2026-10-28 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28 NEEDS WORK (2 gaps) → fixed
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours (was 3; +1 for in-place replacement and tick-aware free, `/story-readiness` 2026-10-28)

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-4 (refresh and its exception), F-SE-4 (hard reset), States (Refreshed), EC-SE-3; AC-SE-11, 12, 27
**Requirement**: `TR-se-003` (active)

**ADR Governing Implementation**: ADR-012 (assembly placement)
**ADR Decision Summary**: server-only service.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative core logic in `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [x] **AC-SE-11**: `DurationTicks 60`, 30 ticks advanced (`TicksRemaining 30`), same `BuffID` reapplied with the same values → `Success`; `TicksRemaining 60`; zero `RemoveBuffModifier` and zero add calls; effective stat equal to right after the first application.
- [x] **AC-SE-12**: `DurationTicks 40`, 35 ticks advanced (`TicksRemaining 5`), reapplied → `TicksRemaining 40`, not 45.
- [x] **AC-SE-27**: `BuffID X` AttackPower +10 applied; `X` reapplied with +20 → (1) `RemoveBuffModifier(entity, AttackPower, X)` exactly once; (2) one add with flat 20 and the new duration; (3) effective = base + 20; (4) `TicksRemaining` = the new `DurationTicks`; the row's caster is the new caster.
- [x] **"Same values"** means: same `ModifierCount`, and for each index the same `StatId`, `FlatBonus`, `PctBonus` (exact float equality), and the same regen rates. A difference only in `DurationTicks` or caster is a plain refresh (TicksRemaining = new duration, caster updated), not a remove-then-reapply.
- [x] **Remove-then-reapply capacity**: before any layer call, check `OccupiedSlots − old ModifierCount + new ModifierCount > 32` → `CapacityExceeded`, old effect untouched (zero layer calls, row and occupancy unchanged). An entity at 32 slots can replace its own 4-modifier buff with another 4-modifier version. Only a layer add failure after the old modifiers were removed loses the old effect: the new adds are rolled back (Story 003 `TryAddModifiers`), the row is freed, occupancy drops by the old count, and the result is `CapacityExceeded` — documented in a comment.
- [x] **Replace in place**: a successful remove-then-reapply overwrites the same row index (buff-bar position kept, other rows unmoved): new modifiers, regen, `TicksRemaining` = new `DurationTicks`, new caster; occupancy adjusted by `new − old`. Removals use the old row's modifiers in index order, then the new adds in index order.
- [x] **During a tick (EC-SE-8, via `InjectApplyEffectDuringTick`)**: a refreshed or replaced row the tick has already processed keeps the new `TicksRemaining` (= `DurationTicks`) after the tick; one the tick has not reached yet is decremented by this tick like any existing row (`DurationTicks − 1`, and it gets this tick's regen). A row freed after a failed replacement during a tick is freed through a tick-aware helper: if it is in the slot being processed, the tick's loop index and end are adjusted so no remaining row is skipped and no row created this tick is processed (AC-SE-19 still holds).
- [x] **EC-SE-3**: reapplying a `BuffID` on an entity marked dead → `InvalidTarget`, no refresh.
- [x] A refresh skips the row-limit and capacity checks (Story 002) — an entity with 8 rows can refresh one of them.

---

## Implementation Notes

- A re-apply of an active `BuffID` skips the row-limit check (Story 002 step 3). A plain refresh has the same `ModifierCount`, so occupancy is unchanged; a replacement re-checks capacity with the old slots excluded (criterion above). `StatusEffectsService.FindRow(slot, buffId)` returns the row index to refresh or replace in place.
- A refresh only writes the row (`TicksRemaining`, caster) — never Character Stats (CR-SE-4).
- The remove half of the exception is CR-SE-8 steps 1–3. Story 005 put the modifier removals inside `ProcessRow`; extract them into a shared private helper (e.g. `RemoveModifiers(slot, rowIndex)`) used by both the tick and the replacement, so Story 006 adds the MaxHP/MaxMP clamp (and TD-085 throw-safety) in one place. Step 4 (free the row) is not used on success — the row is overwritten in place.
- Tick-aware free: `OnServerTick` keeps its loop index and end in locals today; move them to fields (e.g. `_tickSlot`, `_tickIndex`, `_tickEnd`, `NO_SLOT` outside a tick) so the failure path can adjust them: freed index < current index → index−1 and end−1; freed index in [index, end) → end−1; freed index ≥ end (created this tick) → no change. Allocation-free.
- `PctBonus`/`FlatBonus` use exact float equality, so a NaN amount never matches and always takes the replacement path (NaN amounts are an open GDD question from Story 002).

---

## Out of Scope

- The tick that decrements (Story 005 — tests may use a minimal `Tick` from 005 or a test seam that sets `TicksRemaining`; prefer implementing after 005 if both are open).

---

## QA Test Cases

- **AC-SE-11 / 12 / 27** — as listed, counting layer calls with a recording fake over the real `CharacterStats`.
- **Plain refresh variants** — same values, different duration → TicksRemaining = new duration, no layer calls; different caster → caster updated, no layer calls.
- **Edges** — refresh at `TicksRemaining 1`; refresh with 8 rows full; replacement at exactly 32 occupied slots; reapply on a dead entity.
- **Replacement** — row index and other rows' order unchanged (3 buffs, replace the middle one); 2→3 and 3→2 modifier counts adjust occupancy; capacity pre-check failure (30 occupied, 2-modifier buff replaced by a 5-modifier one) → `CapacityExceeded`, zero layer calls, old row intact; layer add failure (recording fake `FailOnAddNumber`) → old row gone, later rows shifted down, occupancy −old, `CapacityExceeded`; regen-only change triggers replacement.
- **During a tick** — refresh injected for a row already processed → `DurationTicks` after the tick; for a later row → `DurationTicks − 1`; replacement of a later row → new values, decremented once; failed replacement of an earlier and of a later row in the processing slot → every remaining row processed exactly once, none twice.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_Refresh_tests.cs` — must exist and pass

**Status**: [x] Created and passing — 36 tests, 36/36 (full EditMode 4756 / 4627 / 0 failed / 129 skipped, 2026-10-28)

---

## Dependencies

- Depends on: Story 003; Story 005 for advancing ticks (recommended order 001 → 002 → 003 → 005 → 004)
- Unlocks: None

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 9/9 passing, all covered by `tests/EditMode/StatusEffects/StatusEffects_Refresh_tests.cs` (36 tests)
**Deviations** (advisory):
1. A subscriber throw during a replacement add frees the row (tick-aware) and rethrows; the old effect is lost and the caller sees the exception instead of `CapacityExceeded` (documented in the `ApplyEffect` remarks).
2. A regen-only buff (0 modifiers) whose rate changes takes the replacement path with zero layer calls — effectively an overwrite of the row.
3. `FreeRowTickAware` also lowers the tick-start snapshot of a slot other than the one being processed (code-review B1: otherwise a failed replacement in a later slot made the tick process a cleared row and `RemoveRow` threw). The story named only the processing slot.
4. Test support: second internal seam `InjectSecondApplyEffectDuringTick` + `LastSecondInjectedResult` (fires right after the first injection); test fake `FailOnAddNumber` now also applies with an inner layer, new `ThrowOnAddNumber`.
5. TD-085 widened from expiry to expiry + replacement (`RemoveModifiers` is the shared seam for Story 006).
6. Estimate 3 → 4 h (set at `/story-readiness`). `RefreshOrReplace` split into `RefreshOrReplace` + `ReplaceInPlace` at code review.
**Test Evidence**: Logic — `tests/EditMode/StatusEffects/StatusEffects_Refresh_tests.cs`
**Code Review**: Complete — 2026-10-28 CHANGES REQUIRED (1 blocking) → "fix all" applied; QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean)
