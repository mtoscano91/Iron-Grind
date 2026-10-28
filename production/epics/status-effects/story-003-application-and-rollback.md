# Story 003: Application, Atomic Rollback and Caster Attribution

> **Epic**: Status Effects / Buffs
> **Status**: Complete — 2026-10-28 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28 NEEDS WORK (1 gap) → fixed
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-2 (distinct stats per definition), CR-SE-3 (atomic application), CR-SE-13 (caster attribution), EC-SE-11, Interactions (Character Stats row); AC-SE-01, 05, 08 (repeated-stat clause), 09, 31
**Requirement**: `TR-se-003` (placeholder — registry empty)

**ADR Governing Implementation**: ADR-012 (assembly placement); ADR-010 (Character Stats' `OnStatChanged` is the notification path — this story adds no event)
**ADR Decision Summary**: server-only service; no new event — subscribers learn of modifier changes through Character Stats' existing `OnStatChanged`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative core logic in `IronGrind.ServerLogic` — ADR-012
- Required: Status Effects is the only caller of `AddBuffModifier` / `RemoveBuffModifier` (Character Stats GDD, Interactions)

---

## Acceptance Criteria

- [x] **Seam**: `IBuffModifierLayer` (interface defined in Story 002; this story adds the production adapter) with `bool TryAddBuffModifier(EntityID, StatID, BuffModifierEntry)`, `void RemoveBuffModifier(EntityID, StatID, BuffID)`. The production adapter over `CharacterStats` returns false when `AddBuffModifier` did not add the entry (write-locked stat, or the 32-per-stat capacity hit — today it logs and returns), detected by comparing the stat's buff count before and after or by an equivalent check; it never swallows a successful add.
- [x] **AC-SE-01**: one modifier (AttackPower, flat +10, pct 0), `DurationTicks 40` → `Success`; exactly one add with `(entity, AttackPower, flat 10, pct 0, duration 40, buffId)`; with the real `CharacterStats`, `GetEffectiveStat(AttackPower)` = base + 10; the entity has one row with `TicksRemaining 40`.
- [x] **AC-SE-05 — atomic rollback**: a fake layer fails the 2nd add of a 3-modifier buff → (1) the 1st modifier was added then removed; (2) the 3rd was never added; (3) effective values unchanged for all 3 stats; (4) no row; occupancy unchanged; result is a failure (`CapacityExceeded`; document the mapping). After validation passes, Character Stats can reject an add for two reasons: the 32-per-stat capacity is full, or `AddBuffModifier` is called while Character Stats is firing `OnStatChanged` (`IsFiringAndAssert` — e.g. from inside an `OnStatChanged` subscriber). Both map to `CapacityExceeded`; the second also logs an assertion from Character Stats.
- [x] **AC-SE-09**: MaxHP flat +100 → `Success`, `GetEffectiveStat(MaxHP)` = base + 100. MaxMP flat +50 → `Success`, base + 50.
- [x] **AC-SE-31**: `ApplyEffect(target, caster 42, def)` → the row's `CasterEntityId` = 42; readable through a read-only accessor (e.g. `TryGetActiveEffect(EntityID, BuffID, out ActiveEffect)` and an allocation-free per-entity enumeration) without calling Character Stats.
- [x] **Repeated stat rejected (CR-SE-2, EC-SE-11)**: a definition with two modifiers on `AttackPower` → `InvalidStat`; zero layer calls; no row; occupancy unchanged. The check goes in `StatusEffectsService.ValidateDefinition`, after the prohibited-stat check, so the adapter never sees the same `(entity, stat, BuffID)` twice in one apply.
- [x] **Regen-only definition**: `ModifierCount 0`, `HpRegenPerTick 5` → `Success`, a row exists, zero adds.
- [x] **Snapshot**: the row stores the definition's modifier values; changing nothing else, a later read returns the same values (the snapshot is what expiry removes — Story 005).

---

## Implementation Notes

- `BuffModifierEntry(float flatBonus, float pctBonus, int durationTicks, BuffID id)` is the existing Character Stats type; `durationTicks` is passed for audit only (GDD Interactions) — Status Effects owns expiry. Cast `uint` → `int` safely (≤ 6000).
- Rollback removes, in reverse order, every modifier this attempt added; it runs before any row is written.
- Apply happens after Story 002's checks pass; the row and the occupancy count are written only after every add succeeded.

---

## Out of Scope

- Refresh of an existing `BuffID` (Story 004); ticking and expiry (005); batching of `OnStatChanged` (006).

---

## QA Test Cases

- **AC-SE-01** — Given a real `CharacterStats` entity with AttackPower base 20, When the +10 buff is applied, Then one add with the exact arguments, effective 30, row `TicksRemaining` 40.
- **AC-SE-05** — Given a fake layer failing call 2, When a 3-modifier buff is applied, Then calls are add(1), add(2 → false), remove(1), no add(3); no row; effective values unchanged. Edge: failing call 1 → no removes; failing call 3 → removes 2 then 1.
- **AC-SE-09** — MaxHP +100 and MaxMP +50 succeed and show in effective stats.
- **Repeated stat** — Given a definition with AttackPower flat +10 and AttackPower pct +5%, When applied, Then `InvalidStat`, no layer calls, no row. Edge: AttackPower + MaxHP + AttackPower (repeat not adjacent) → `InvalidStat`; CurrentHP twice → `InvalidStat`.
- **AC-SE-31** — caster 42 stored; accessor reads it; accessor makes no layer call.
- **Adapter** — real `CharacterStats`: adding to a write-locked stat via the adapter returns false (and the error log is expected); a normal add returns true.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_Application_tests.cs` — must exist and pass

**Status**: [x] Created — 28 tests, 28/28 passing (full EditMode 4692 / 4563 passed / 0 failed / 129 skipped, 2026-10-28)

---

## Dependencies

- Depends on: Story 002
- Unlocks: Stories 004, 005

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 8/8 passing, all covered by `tests/EditMode/StatusEffects/StatusEffects_Application_tests.cs`
**Deviations** (advisory):
1. AC-SE-05's mapping note is correct only for release builds: in the Editor and development builds Character Stats' `IsFiringAndAssert` throws `InvalidOperationException` on a re-entrant call, and `ApplyEffect` lets it propagate (no state change: the first add is rejected before anything is stored). Release builds return and map to `CapacityExceeded`. Test `Reentrancy` pins the Editor behaviour.
2. Additions from `/code-review`: a `StatID` outside the defined range returns `InvalidStat` (it would otherwise throw mid-application and skip the rollback); an exception from an `OnStatChanged` subscriber during an add removes the modifiers added so far (including the current one) and is rethrown; `GetActiveEffects(EntityID)` returns a `ReadOnlySpan<ActiveEffect>` and `TryGetActiveEffect` is public.
3. `CharacterStatsBuffModifierLayer` detects a rejected add by comparing Character Stats' `internal` `EntityBuffCount` before and after (same assembly; Character Stats' API unchanged). An overwrite of an existing same-BuffID entry reports false; unreachable from the service today, and relies on Stories 005/007/008 removing a freed row's modifiers.
4. Story 002 tests adjusted: the blanket zero-layer-call TearDown removed (rejected applies still assert no layer calls), and the `Def` helper gives each extra modifier a distinct stat (CR-SE-2).
**Files**: `src/ServerLogic/StatusEffects/` (`StatusEffectsService`, new `CharacterStatsBuffModifierLayer`, `IBuffModifierLayer` doc); tests `StatusEffectsTestFakes.cs`, `StatusEffects_Validation_tests.cs`, new `StatusEffects_Application_tests.cs`.
**Test Evidence**: Logic — `tests/EditMode/StatusEffects/StatusEffects_Application_tests.cs`
**Code Review**: Complete — `/code-review` 2026-10-28 APPROVED WITH SUGGESTIONS, all items applied except merging the two stat loops (dismissed). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
