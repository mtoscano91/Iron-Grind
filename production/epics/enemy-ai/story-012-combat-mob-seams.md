# Story 012: Replace Combat's Mob Stand-Ins

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Interactions (Character Persistence correction: XP from `KillXP` / Enraged XP); `design/gdd/auto-attack-combat.md` Rule 1 (valid target), Rule 10; `design/gdd/leveling-system.md` CR-1.1, OQ-LS-7 (b: no level-differential modifier at MVP); `design/gdd/networking-wire-protocol.md` EntityState mob body (`cycleTimer` 0 for mobs, no auto-attack cycle); `design/gdd/zone-instancing.md` CR-ZI step 6 (`isInCombat`)
**Requirement**: `TR-ai-009`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010 (notes 2026-10-30, 2026-11-04: XP is a direct call by the killer's controller before `ApplyDamage`)
**ADR Decision Summary**: The killer's controller calls `GetXPAward` → `AddExperience` → `ApplyDamage`; Combat raises the one `OnMobDied`.

**User decision (2026-11-13, `/create-stories`)**: this epic replaces Combat's four "until Enemy AI" stand-ins so a player can kill a moving mob end to end.

**User decisions (2026-11-16, `/story-readiness`)**:
1. **Scope split**: this story delivers the seam classes and proves them in a hand-built test rig. Story 013 wires them into `ZoneServerComposition` and deletes the stand-ins (the store and `EnemyAISystem` only exist in the composition from Story 013).
2. **Damage calculators**: the item database, `EquipmentService` (implements `IEquippedWeaponQuery`; returns `ItemID.Invalid` for mobs without logging, EC-EQS-9) and `EnhancementBonusProvider` are already in the zone (Character Persistence Stories 019/020). Story 013 replaces both `StandInAutoAttackDamageCalculator` and `StandInSkillDamageCalculator` with one real `DamageCalculator`. No calculator change in this story.
3. **Mob `isInCombat`**: Pursuing, WindingUp, AttackExecution, Recovering → in combat; Dormant, Returning, Dead → not.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C# in `IronGrind.ServerLogic`; no post-cutoff API).

**Control Manifest Rules (this layer)**:
- Forbidden: awarding XP from an `OnMobDied` subscriber — ADR-010
- Required: tick-thread seams do not allocate (interface contracts of `IAutoAttackTargetValidity`, `IAutoAttackDamageApplier`, `IAutoAttackMobTypeSource`)

---

## Acceptance Criteria

- [ ] **AC-1 Target validity** — `MobTargetValidity : IAutoAttackTargetValidity` over the zone's `ZoneSlotPool`, `MobRuntimeStore` and Character Stats. `IsValidTarget(id)` is true only when `id` holds a mob slot of this zone's pool **and** has a `MobRuntimeStore` entry **and** `GetCurrentHP(id) > 0`. Players, unknown IDs, mobs without a store entry and mobs at HP ≤ 0 (including a lingering Dead corpse) are invalid. It does not read `EnemyAISystem` (Combat is built before Enemy AI, Story 011): a mob enters Dead only once its HP is 0, so HP ≤ 0 covers Dead.
- [ ] **AC-2 Inner damage applier** — `CharacterStatsAutoAttackDamageApplier : IAutoAttackDamageApplier` calls `CharacterStats.ApplyDamage(target, result.FinalDamage)` and nothing else. Combat receives it wrapped in `DamageOutputApplier` (damage events sent); the Skill damage step receives the inner applier unwrapped (Decision B, confirmed: `BuildSkillCastPipeline` passes `_owner.AutoAttackInnerDamageApplier`).
- [ ] **AC-3 Mob type source** — the existing `ZoneMobRosterMobTypeSource` over the zone's `IZoneMobRoster` (`ZoneInstancingWiring.MobRoster`, now reachable) replaces `StandInAutoAttackMobTypeSource`. No new class. The roster still holds the mob at the kill (Enemy AI never calls `RemoveMob`, Story 011).
- [ ] **AC-4 XP award** — `MobRuntimeStoreXpAward : IAutoAttackXpAward` returns `entry.Variant.EffectiveKillXP` (Story 007: `KillXP`, or `RoundToInt(KillXP × EnragedXPMultiplier)` when Enraged). A mob with no store entry returns 0 and logs one warning per call. No level-differential modifier (OQ-LS-7 b). Used for both Combat and the Skill System's XP source. *Deviation (advisory)*: the GDD names this `LevelingSystem.GetXPAward`; `LevelingService` has no such method, and the per-spawn value already lives in the store, so the seam reads the store directly (same value as the GDD formula).
- [ ] **AC-5 Mob combat-state reader (rest of TD-102)** — `EnemyAISystem.TryGetMobState(EntityID, out MobState)` (public, no allocation). `MobAwareZoneEntityCombatReader : IZoneEntityCombatReader` takes the `EnemyAISystem` and an inner reader (`AutoAttackZoneEntityCombatReader`). For a mob that Enemy AI knows: `GetState` returns `AutoAttackState.CombatActive` for Pursuing / WindingUp / AttackExecution / Recovering and `AutoAttackState.Idle` for Dormant / Returning / Dead; `GetActionTimer` and `GetCycleTicks` return 0 (snapshot `cycleTimer` 0, wire protocol mob body). Any other entity is delegated to the inner reader unchanged.
- [ ] **AC-6 End-to-end kill (integration)** — in a hand-built rig (real `AutoAttackCombatSystem`, `MobRuntimeStore`, `EnemyAISystem`, `CharacterStats`, a real `DamageCalculator`, the AC-1..4 seams, `DamageOutputApplier`): a player auto-attacks a seeded mob until its HP reaches 0. Then `AddExperience` ran once with the mob's `EffectiveKillXP`; `ICombatService.OnMobDied` was raised once; the Story 011 death routine ran once; `MobEventBus.MobDied` was raised once. A further auto-attack on the corpse during the linger is rejected by target validity (no damage, no second XP, no second `OnMobDied`). Repeated for an Enraged mob (XP = Enraged value).
- [ ] **AC-7 Calculator decision recorded** — both calculator stand-ins are replaced in Story 013 (user decision 2); this story changes no calculator wiring. The Story 013 note is updated.
- [ ] **AC-8 No allocation** — after warm-up, `IsValidTarget`, `Apply`, `GetXPAward` (known mob), `TryGetMobState` and the reader's three methods allocate 0 bytes (measured with `GC.GetAllocatedBytesForCurrentThread`).

---

## Implementation Notes

- Place `MobTargetValidity` and `MobRuntimeStoreXpAward` in `src/ServerLogic/EnemyAI/` (they read the store). Place `CharacterStatsAutoAttackDamageApplier` in `src/ServerLogic/AutoAttackCombat/` (next to `CharacterStatsExperienceSink`). Place `MobAwareZoneEntityCombatReader` in `src/ServerLogic/ZoneServer/` (next to `AutoAttackZoneEntityCombatReader`, in `ZoneEntityStateSource.cs` or its own file).
- `TryGetMobState` uses the existing `_index` lookup (`TryGetController` stays internal for tests).
- Do **not** delete the stand-ins or touch `ZoneServerComposition.cs`; Story 013 does both. Update the stand-in doc comments only if they now name a wrong replacement.
- Update the `ZoneEntityStateSource` class remark (it says mob combat state comes in "Enemy AI Story 012") to point at the new reader and its Story 013 wiring.
- The rig's `DamageCalculator` can use `EquipmentService` or a fake `IEquippedWeaponQuery` returning `ItemID.Invalid`; a fixed `IRandomProvider` keeps it deterministic (crit chance 0).

---

## Out of Scope

- Story 013: composition construction order, wiring these seams, deleting the six Combat/Skill stand-ins, building the real `DamageCalculator` (players, skills, mobs) and binding `MobAwareZoneEntityCombatReader` to `ZoneEntityStateSource`
- Level-differential XP (OQ-LS-7 b — a future design change)
- Loot wiring (TD-107), Character Stats entity removal on despawn (TD-109)

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-16.*

1. **Validity — player**: a player EntityID in the pool → false.
2. **Validity — live mob**: a mob slot with a store entry and HP > 0 → true.
3. **Validity — no store entry**: a mob slot in the pool but not in the store → false.
4. **Validity — unknown / foreign ID**: an ID not in this zone's pool → false.
5. **Validity — HP 0**: a seeded mob after `ApplyDamage` to 0 → false (corpse during linger).
6. **Applier — partial**: `Apply` with FinalDamage 30 on a 100 HP mob → HP 70; no other stat changes.
7. **Applier — killing hit**: FinalDamage ≥ HP → HP 0.
8. **XP — normal**: non-Enraged mob → `KillXP`.
9. **XP — Enraged**: Enraged mob → `RoundToInt(KillXP × EnragedXPMultiplier)` (the entry's `EffectiveKillXP`).
10. **XP — unknown**: no store entry → 0 and exactly one warning.
11. **Mob type**: `ZoneMobRosterMobTypeSource` returns the roster's type for a spawned mob, false for a player.
12. **State mapping**: each of the 7 `MobState` values (via `ForceStateForTest`) → the AC-5 `AutoAttackState`; timer and cycle 0.
13. **Delegation**: a player ID → the inner reader's state, timer and cycle unchanged; an ID unknown to Enemy AI → delegated.
14. **End-to-end kill** (AC-6): XP once with the right amount, `OnMobDied` once, death routine once, `MobEventBus.MobDied` once.
15. **Enraged end-to-end**: same as 14 with an Enraged mob; XP = Enraged value.
16. **Corpse re-attack**: after the kill, re-targeting the corpse and ticking → no damage event, no XP, no second `OnMobDied`.
17. **No allocation** (AC-8): warm up, then 0 bytes over 1,000 calls of each seam method.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_CombatSeams_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011 (Complete)
- Unlocks: Story 013

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 8/8 passing (AC-7 is a recorded decision, verified in the Story 013 notes; all others by automated tests)
**Deviations** (advisory):
1. XP comes from `MobRuntimeStoreXpAward` (`MobRuntimeEntry.Variant.EffectiveKillXP`), not a `LevelingSystem.GetXPAward` method (none exists); same value as the GDD formula, no level-differential modifier (OQ-LS-7 b).
2. `MobTargetValidity` does not read the Dead state: HP ≤ 0 covers it (Combat is built before `EnemyAISystem`, Story 011).
3. The ADR-010 order (`AddExperience` before `ApplyDamage`) cannot be observed in the test rig (real applier, the XP sink logs only XP); the code follows it (`AutoAttackCombatSystem.ResolveKill`).
4. Deferred to Story 013 by design: stand-in deletion and composition wiring. Optional, not logged: `MobRuntimeStore.Contains`, an `IMobStateSource` interface for the reader. Verified at code review: the Skill target source treats HP 0 as not alive (`ZoneSkillTargetSource`), so a skill cannot double-award XP on a corpse.
**Tech debt**: none new. TD-102 (updated at readiness) closes in Story 013.
**Test Evidence**: Integration — `tests/EditMode/EnemyAI/EnemyAI_CombatSeams_tests.cs` 26/26; batch EditMode 7797 / 7668 passed / 0 failed / 129 skipped (2026-11-16).
**Code Review**: Complete — CHANGES REQUIRED (tests only: pool and store checks not mutation-proof, vacuous AC-8 `Apply`) → all fixes and suggestions applied (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
