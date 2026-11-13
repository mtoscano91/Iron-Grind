# Story 012: Replace Combat's Mob Stand-Ins

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Interactions (Character Persistence correction: XP from `KillXP` / `EnragedKillXP` read via the registry); `design/gdd/auto-attack-combat.md` Rule 1 (valid target), Rule 10; `design/gdd/leveling-system.md` CR-1.1
**Requirement**: `TR-ai-009`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010 (notes 2026-10-30, 2026-11-04: XP is a direct call by the killer's controller before `ApplyDamage`)
**ADR Decision Summary**: The killer's controller calls `GetXPAward` → `AddExperience` → `ApplyDamage`; Combat raises the one `OnMobDied`.

**User decision (2026-11-13, `/create-stories`)**: this epic replaces Combat's four "until Enemy AI" stand-ins so a player can kill a moving mob end to end.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Forbidden: awarding XP from an `OnMobDied` subscriber — ADR-010

---

## Acceptance Criteria

- [ ] `IAutoAttackTargetValidity` (replaces `StandInAutoAttackTargetValidity`): a target is valid when it is a mob of this zone with a runtime entry, HP > 0 and not Dead; players never.
- [ ] `IAutoAttackDamageApplier` inner applier (replaces `StandInAutoAttackDamageApplier`): applies the hit to the mob's `CurrentHP` through Character Stats `ApplyDamage`; still wrapped by `DamageOutputApplier` so the damage events are sent. The Skill System's damage step uses the same inner applier (Decision B in the composition) — confirm.
- [ ] `IAutoAttackMobTypeSource` (replaces `StandInAutoAttackMobTypeSource`): from the runtime store / roster (`ZoneMobRosterMobTypeSource` if the roster is now reachable).
- [ ] `IAutoAttackXpAward` (replaces `StandInAutoAttackXpAward`, and the Skill System's XP source): `IsEnraged ? EnragedKillXP : KillXP` of the mob's definition; unknown mob → 0 with one warning. Readiness: check Leveling's `GetXPAward` contract (level-difference scaling, if any) and route through it rather than duplicate.
- [ ] Mob combat-state reader for the snapshot (`isInCombat`, `CycleTimer`) if not covered by Story 008 — the remainder of TD-102.
- [ ] Integration test: a player auto-attacks a seeded mob until HP 0 → XP added once, `OnMobDied` raised once, Story 011 bridge runs, `MobEventBus.MobDied` raised once.
- [ ] The damage *calculator* stand-ins (`StandInAutoAttackDamageCalculator`, `StandInSkillDamageCalculator`) stay unless the item database / equipment are wired by then — record which.

---

## Implementation Notes

- Stand-ins live in `src/ServerLogic/ZoneServer/ZoneServerStandIns.cs`; delete the replaced classes and their tests' references, keep the composition tests green.

---

## Out of Scope

- Story 013: composition construction order

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_CombatSeams_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011
- Unlocks: Story 013
