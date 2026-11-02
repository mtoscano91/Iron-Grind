# Story 004: Kill Sequence

> **Epic**: Auto-Attack Combat
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rule 10 Step 3; `design/gdd/leveling-system.md` — CR-1.1, OQ-LS-7
**Requirement**: `TR-aa-004`

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

- [x] On `IsKill == true`, calls run in exactly this order: `GetXPAward(TargetID)` → `AddExperience(AttackerID, xpAmount)` → `ApplyDamage(TargetID, FinalDamage)`.
- [x] On `IsKill == false`, only `ApplyDamage` is called.
- [x] After a kill the attacker enters `IDLE`, the timer freezes, and the target is cleared from combat state.
- [x] `ICombatService.OnMobDied` is raised once per kill, carrying a `MobDeathContext`, after `ApplyDamage`.
- [x] XP is awarded exactly once per kill.
- [x] Two players killing one mob on one tick: the second attacker's `Calculate` sees a dead target and awards no second XP.

---

## Implementation Notes

- `GetXPAward` and `IMobDefinitionRegistry` do not exist. Inject an XP-award interface with a fake here; Enemy AI or Leveling provides the real one.
- `AddExperience` exists on `CharacterStats` (`CharacterStats.cs:1022`). Depend on a narrow interface.
- `ApplyDamage(TargetID, FinalDamage)` is the existing `IAutoAttackDamageApplier.Apply` from Story 003. *Resolved at `/story-readiness` 2026-11-02 (user: option 1):* mobs do **not** get `CharacterStats` entries in this epic. Target health stays behind `IAutoAttackDamageApplier`, with a call-recording fake in the tests. The real mob-health applier comes with the Enemy AI epic. Consequence: the real `DamageCalculator` reads target HP through `CharacterStats.GetCurrentHP`, and a mob with no HP record reads as 0 HP. So until Enemy AI provides mob HP, the production `IsKill` stays `false` for mobs and the dead-entity dev error is logged. This story checks the kill path through a fake `IAutoAttackDamageCalculator` that returns `IsKill` from the fake health. Record this as a note for the Enemy AI epic. Do not add mob registration to `CharacterStats` here.
- XP seams: `IAutoAttackXpAward.GetXPAward(EntityID target): int` (fake here; Leveling or Enemy AI provides the real one through `IMobDefinitionRegistry`) and `IAutoAttackExperienceSink.AddExperience(EntityID attacker, int amount)`, with a `CharacterStatsExperienceSink` adapter over `CharacterStats.AddExperience`.
- `ICombatService` (`event Action<MobDeathContext> OnMobDied`) and `MobDeathContext` do not exist. Create them per ADR-010 Decision 3, with no subscribers in this epic. Put the files in `src/ServerLogic/AutoAttackCombat/` (one file per type). Declare `MobDeathContext` in the `IronGrind.Events` namespace, as the control manifest requires for event-argument structs; this story creates that namespace. `AutoAttackCombatSystem` implements `ICombatService`. Field sources: `ZoneId` comes from a `ZoneID` passed to the constructor (one zone per process, ADR-007). `MobTypeId` comes from `IZoneMobRoster.TryGetMob`, or a narrower interface. `DeathPosition` is the target position already decoded in `Resolve`. `KillerEntityId` is the attacker. Raise the event after `Apply`, and catch and log a subscriber exception so a throwing subscriber does not break the tick.
- `CharacterStats.IsFiringAndAssert` blocks mutation inside a stat event. Confirm none of these calls runs inside one.
- Rule 10 Step 3 names Skill System's CR-SK-6 as the matching order. Do not share code with the Skill System here.

---

## Out of Scope

- `KillEvent`, mob despawn, loot, party credit, respawn: Enemy AI epic
- Real `GetXPAward`: Enemy AI, Leveling
- Story 007: damage messages on the wire

---

## Performance

No per-tick cost is added. The kill path runs only when `IsKill` is true: one XP-award call, one XP call, one apply call and one `OnMobDied` raise per kill. `MobDeathContext` is a `readonly struct`, so emitting the event does not allocate. The existing allocation tests must stay at zero, and a kill must be added to the no-allocation test.

---

## QA Test Cases

Use one call-recording fake behind the XP and health seams, so order is asserted across interfaces.

- **Kill order**: `IsKill` true → recorded order is exactly award, add, apply, with the right ids and amounts.
- **No kill**: `IsKill` false → apply only.
- **IDLE**: after a kill the state is `IDLE`, the timer is 0, and no auto fires over the next 100 ticks.
- **`OnMobDied`**: one raise per kill with correct ids, after apply; a throwing subscriber does not break the tick.
- **Double kill**: two attackers, same tick → one XP award.
- **Zero XP**: award 0 → sequence still completes.
- **No allocation**: a kill, including the `OnMobDied` raise with one named-method subscriber, runs inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_KillSequence_tests.cs` — must exist and pass

**Status**: [x] Created and passing (28 tests; EditMode 5705 / 5576 passed / 0 failed / 129 skipped, 2026-11-02)

---

## Dependencies

- Depends on: Story 003
- Unlocks: None within the epic; Enemy AI epic consumes `OnMobDied`

---

## Completion Notes
**Completed**: 2026-11-02
**Criteria**: 6/6 passing (all auto-verified by `AutoAttackCombat_KillSequence_tests.cs`; none deferred)
**Deviations** (advisory, none logged as tech debt):
- Production kills cannot happen yet (option 1 at `/story-readiness`): mobs have no `CharacterStats` HP, so the real `DamageCalculator` reports `IsKill` false for mobs and logs the dead-entity dev error. The kill path is verified through a fake calculator; Enemy AI provides mob health.
- No production `IAutoAttackXpAward`: Story 006 composition needs a placeholder until Leveling / Enemy AI provides the `IMobDefinitionRegistry`-backed one.
- `OnMobDied` uses custom add/remove over a cached handler array (ADR-010 example shows a field-like event): same C# event surface, but each subscriber is isolated in its own try/catch with no allocation on raise (`/code-review` fix).
- For Enemy AI: in a same-tick double kill, the real calculator's dead-entity guard returns damage without a kill, so a second `Apply` lands on the dead mob (mob health must ignore it); a failed mob-type lookup skips `OnMobDied`, so loot and respawn are lost for that mob.
- An exception from the XP or damage seams is not caught: XP stays granted and the exception escapes `Run` (documented; XP-before-Apply is mandated).
**Test Evidence**: Logic: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_KillSequence_tests.cs` (28 tests); kill fakes in `AutoAttackCombat_TestFakes.cs`. EditMode 5705 / 5576 passed / 0 failed / 129 skipped, no `error CS`.
**Code Review**: Complete (`/code-review` 2026-11-02: CHANGES REQUIRED → all applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean)
