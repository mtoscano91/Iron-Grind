# Story 004: Kill Sequence

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rule 10 Step 3; `design/gdd/leveling-system.md` — CR-1.1, OQ-LS-7
**Requirement**: `TR-aa-004` (placeholder)

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: damage and XP are direct calls (note 2026-10-30); `OnMobDied` is the Tier 2 event for loot, party kill credit and respawn. Subscribers use named methods, never lambdas.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces for damage and XP
- Forbidden: an XP subscriber on `OnMobDied`
- Forbidden: lambda subscriptions on persistent events

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md`, scoped to this story:*

- [ ] On `IsKill == true`, calls run in exactly this order: `GetXPAward(TargetID)` → `AddExperience(AttackerID, xpAmount)` → `ApplyDamage(TargetID, FinalDamage)`.
- [ ] On `IsKill == false`, only `ApplyDamage` is called.
- [ ] After a kill the attacker enters `IDLE`, the timer freezes, and the target is cleared from combat state.
- [ ] `ICombatService.OnMobDied` is raised once per kill, carrying a `MobDeathContext`, after `ApplyDamage`.
- [ ] XP is awarded exactly once per kill.
- [ ] Two players killing one mob on one tick: the second attacker's `Calculate` sees a dead target and awards no second XP.

---

## Implementation Notes

- `GetXPAward` and `IMobDefinitionRegistry` do not exist. Inject an XP-award interface with a fake here; Enemy AI or Leveling provides the real one.
- `AddExperience` exists on `CharacterStats` (`CharacterStats.cs:1022`). Depend on a narrow interface.
- `ApplyDamage` exists, but mobs have no stat record yet, and `DamageCalculator` logs an error for a target with no HP record. Keep target health behind the Story 003 interface. Settle at `/story-readiness` whether mobs get `CharacterStats` entries.
- `ICombatService` and `MobDeathContext` do not exist. Create them per ADR-010 in `src/ServerLogic/AutoAttackCombat/`, with no subscribers in this epic.
- `CharacterStats.IsFiringAndAssert` blocks mutation inside a stat event. Confirm none of these calls runs inside one.
- Rule 10 Step 3 names Skill System's CR-SK-6 as the matching order. Do not share code with the Skill System here.

---

## Out of Scope

- `KillEvent`, mob despawn, loot, party credit, respawn: Enemy AI epic
- Real `GetXPAward`: Enemy AI, Leveling
- Story 007: damage messages on the wire

---

## QA Test Cases

Use one call-recording fake behind the XP and health seams, so order is asserted across interfaces.

- **Kill order**: `IsKill` true → recorded order is exactly award, add, apply, with the right ids and amounts.
- **No kill**: `IsKill` false → apply only.
- **IDLE**: after a kill the state is `IDLE`, the timer is 0, and no auto fires over the next 100 ticks.
- **`OnMobDied`**: one raise per kill with correct ids, after apply; a throwing subscriber does not break the tick.
- **Double kill**: two attackers, same tick → one XP award.
- **Zero XP**: award 0 → sequence still completes.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_KillSequence_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: None within the epic; Enemy AI epic consumes `OnMobDied`
