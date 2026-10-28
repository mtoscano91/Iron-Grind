# Story 004: Refresh on Reapply

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-4 (refresh and its exception), F-SE-4 (hard reset), States (Refreshed), EC-SE-3; AC-SE-11, 12, 27
**Requirement**: `TR-se-003` (placeholder — registry empty)

**ADR Governing Implementation**: ADR-012 (assembly placement)
**ADR Decision Summary**: server-only service.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative core logic in `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **AC-SE-11**: `DurationTicks 60`, 30 ticks advanced (`TicksRemaining 30`), same `BuffID` reapplied with the same values → `Success`; `TicksRemaining 60`; zero `RemoveBuffModifier` and zero add calls; effective stat equal to right after the first application.
- [ ] **AC-SE-12**: `DurationTicks 40`, 35 ticks advanced (`TicksRemaining 5`), reapplied → `TicksRemaining 40`, not 45.
- [ ] **AC-SE-27**: `BuffID X` AttackPower +10 applied; `X` reapplied with +20 → (1) `RemoveBuffModifier(entity, AttackPower, X)` exactly once; (2) one add with flat 20 and the new duration; (3) effective = base + 20; (4) `TicksRemaining` = the new `DurationTicks`; the row's caster is the new caster.
- [ ] **"Same values"** means: same `ModifierCount`, and for each index the same `StatId`, `FlatBonus`, `PctBonus` (exact float equality), and the same regen rates. A difference only in `DurationTicks` or caster is a plain refresh (TicksRemaining = new duration, caster updated), not a remove-then-reapply.
- [ ] **Remove-then-reapply capacity**: the new application is checked against capacity *after* the old effect's slots are freed (an entity at 32 slots can replace its own 4-modifier buff with another 4-modifier version). If the new application then fails (layer add failure), the old effect is gone and the result is the failure — documented in a comment.
- [ ] **EC-SE-3**: reapplying a `BuffID` on an entity marked dead → `InvalidTarget`, no refresh.
- [ ] A refresh skips the row-limit and capacity checks (Story 002) — an entity with 8 rows can refresh one of them.

---

## Implementation Notes

- A refresh only writes the row (`TicksRemaining`, caster) — never Character Stats (CR-SE-4).
- The remove half of the exception uses the expiry procedure of Story 005/006 (remove each modifier, clamp if MaxHP/MaxMP, free the row). If this story lands before 005/006, implement the remove through a shared private method that 005/006 then extend; note any ordering in the completion notes.

---

## Out of Scope

- The tick that decrements (Story 005 — tests may use a minimal `Tick` from 005 or a test seam that sets `TicksRemaining`; prefer implementing after 005 if both are open).

---

## QA Test Cases

- **AC-SE-11 / 12 / 27** — as listed, counting layer calls with a recording fake over the real `CharacterStats`.
- **Plain refresh variants** — same values, different duration → TicksRemaining = new duration, no layer calls; different caster → caster updated, no layer calls.
- **Edges** — refresh at `TicksRemaining 1`; refresh with 8 rows full; replacement at exactly 32 occupied slots; reapply on a dead entity.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_Refresh_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003; Story 005 for advancing ticks (recommended order 001 → 002 → 003 → 005 → 004)
- Unlocks: None
