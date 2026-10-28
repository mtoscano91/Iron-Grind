# Story 006: Expiry Clamp and Batched Stat Events

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-8 steps 2–3, CR-SE-17, EC-SE-4, EC-SE-5, OQ-SE-1; AC-SE-15, 29. `design/gdd/character-stats.md` — Transaction constraints (Status Effects is a permitted caller of `BeginStatTransaction` / `EndStatTransaction`)
**Requirement**: `TR-se-004` (placeholder — registry empty)

**ADR Governing Implementation**: ADR-010 (Event/Messaging) — `OnStatChanged` is deferred and deduplicated inside a stat transaction
**ADR Decision Summary**: `OnStatChanged` fires once per unique `(EntityID, StatID)` at `EndStatTransaction()`; Status Effects brackets its tick pass with the transaction.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: Character Stats event handlers must not write stats (`IsFiringAndAssert`) — ADR-010, Character Stats Rule 8

---

## Acceptance Criteria

- [ ] **OQ-SE-1 resolved (HP)**: `CharacterStats.ApplyDamage(entity, 0f)` clamps `CurrentHP` to the effective `MaxHP` and fires no event unless HP reaches 0 (verified in code 2026-10-28: `ApplyDamage` returns early at HP 0 and fires only `OnEntityDied` on reaching 0). A Character Stats test pins this: HP 580, MaxHP lowered to 400 by removing a buff modifier, `ApplyDamage(0f)` → HP 400, no `OnEntityDied`, no `OnStatChanged` for `CurrentHP`.
- [ ] **OQ-SE-1 resolved (MP) — new Character Stats API**: `CharacterStats.ClampCurrentMPToMax(EntityID)` sets `CurrentMP = min(CurrentMP, effective MaxMP)`; no event when unchanged; documented as the Status Effects expiry clamp. (`ApplyManaRegen(0f)` returns early and cannot be used.) Also `ClampCurrentHPToMax(EntityID)` is **not** added — `ApplyDamage(0f)` is used as the GDD specifies. Record the resolution in the GDD (OQ-SE-1 → Resolved) and in `character-stats.md` Interactions.
- [ ] **AC-SE-15**: +200 MaxHP buff, `CurrentHP` = MaxHP (600) → at expiry (1) `RemoveBuffModifier(entity, MaxHP, buffId)`; (2) `ApplyDamage(entity, 0f)` immediately after (recorded order); (3) `CurrentHP` = 400.
- [ ] **EC-SE-4 no-op**: `CurrentHP` 300 ≤ new MaxHP 400 → `ApplyDamage(0f)` still called; HP stays 300; no event.
- [ ] **EC-SE-5**: +100 MaxMP buff, `CurrentMP` = MaxMP → at expiry `RemoveBuffModifier(MaxMP)` then `ClampCurrentMPToMax`; `CurrentMP` = reduced MaxMP.
- [ ] **CR-SE-17 — transaction bracket**: every `OnServerTick()` pass that adds or removes any modifier is wrapped in `BeginStatTransaction()` / `EndStatTransaction()` (also when an exception escapes the pass — `try`/`finally`); a pass that touches no modifier may skip the bracket.
- [ ] **AC-SE-29**: a 3-modifier buff (AttackPower, MaxHP, Defense) expires → a counter on `OnStatChanged` sees exactly 3 events on the expiry tick (one per stat). Two effects sharing AttackPower expire on the same tick → AttackPower fires exactly once that tick.
- [ ] Applications from outside the tick (Skill System calls between ticks) are **not** bracketed by Status Effects — `OnStatChanged` fires per add as today; documented.

---

## Implementation Notes

- The clamp goes through the seam (`IStatResourceSink` gains `ClampHPToMax` → `ApplyDamage(id, 0f)` and `ClampMPToMax` → `ClampCurrentMPToMax(id)`), so the expiry tests can record order.
- Read `CharacterStats.BeginStatTransaction` / `EndStatTransaction` / `RollbackStatTransaction` doc comments for nesting rules before bracketing; Leveling also uses them (never in the same tick pass).
- `character-stats.md` uses `DefencePower` in AC-SE-29's text; the code's `StatID` is `Defense` — use the code name.

---

## Out of Scope

- Death cleanup (Story 007) and zone departure (Story 008) reuse this expiry procedure; their own tests cover those paths.

---

## QA Test Cases

- **AC-SE-15 / EC-SE-4 / EC-SE-5** — as listed, real `CharacterStats`, recorded seam order.
- **AC-SE-29** — event counter subscribed to `OnStatChanged`; 3 events for one 3-stat expiry; 1 event for a stat shared by two expiring effects.
- **Bracket on exception** — a fake layer that throws inside the pass → `EndStatTransaction` still runs (no transaction left open; next tick works).
- **ClampCurrentMPToMax** — unit tests in the Character Stats suite: above max → clamped; at or below → unchanged, no event.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_ExpiryClamp_tests.cs` — must exist and pass; Character Stats tests for `ClampCurrentMPToMax` and the `ApplyDamage(0f)` clamp in the existing Character Stats test folder

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Unlocks: Stories 007, 008
