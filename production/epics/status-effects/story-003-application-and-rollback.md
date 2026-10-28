# Story 003: Application, Atomic Rollback and Caster Attribution

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-3 (atomic application), CR-SE-13 (caster attribution), Interactions (Character Stats row); AC-SE-01, 05, 09, 31
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

- [ ] **Seam**: `IBuffModifierLayer` (interface defined in Story 002; this story adds the production adapter) with `bool TryAddBuffModifier(EntityID, StatID, BuffModifierEntry)`, `void RemoveBuffModifier(EntityID, StatID, BuffID)`. The production adapter over `CharacterStats` returns false when `AddBuffModifier` did not add the entry (write-locked stat, or the 32-per-stat capacity hit — today it logs and returns), detected by comparing the stat's buff count before and after or by an equivalent check; it never swallows a successful add.
- [ ] **AC-SE-01**: one modifier (AttackPower, flat +10, pct 0), `DurationTicks 40` → `Success`; exactly one add with `(entity, AttackPower, flat 10, pct 0, duration 40, buffId)`; with the real `CharacterStats`, `GetEffectiveStat(AttackPower)` = base + 10; the entity has one row with `TicksRemaining 40`.
- [ ] **AC-SE-05 — atomic rollback**: a fake layer fails the 2nd add of a 3-modifier buff → (1) the 1st modifier was added then removed; (2) the 3rd was never added; (3) effective values unchanged for all 3 stats; (4) no row; occupancy unchanged; result is a failure (`CapacityExceeded` — the only failure Character Stats can produce on add for a permitted stat; document the mapping).
- [ ] **AC-SE-09**: MaxHP flat +100 → `Success`, `GetEffectiveStat(MaxHP)` = base + 100. MaxMP flat +50 → `Success`, base + 50.
- [ ] **AC-SE-31**: `ApplyEffect(target, caster 42, def)` → the row's `CasterEntityId` = 42; readable through a read-only accessor (e.g. `TryGetActiveEffect(EntityID, BuffID, out ActiveEffect)` and an allocation-free per-entity enumeration) without calling Character Stats.
- [ ] **Regen-only definition**: `ModifierCount 0`, `HpRegenPerTick 5` → `Success`, a row exists, zero adds.
- [ ] **Snapshot**: the row stores the definition's modifier values; changing nothing else, a later read returns the same values (the snapshot is what expiry removes — Story 005).

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
- **AC-SE-31** — caster 42 stored; accessor reads it; accessor makes no layer call.
- **Adapter** — real `CharacterStats`: adding to a write-locked stat via the adapter returns false (and the error log is expected); a normal add returns true.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_Application_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Stories 004, 005
