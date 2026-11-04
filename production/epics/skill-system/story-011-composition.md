# Story 011: Composition and Tick-Order Wiring

> **Epic**: Skill System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-1 (phase-3 note), EC-SK-1, EC-SK-6, AC-SK-49; `design/gdd/leveling-system.md` — CR-2.10
**Requirement**: `TR-sk-004`, `TR-sk-003`

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: `ZoneTickPipeline` fixes the tick order. Handlers register before `Seal` and run in the dispatch step. Phase-3 systems run in the order the composition root lists. The Skill System step goes after Auto-Attack Combat (epic Decision 3).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: the tick order is code in one class; one failing step does not stop the tick
- Required: the composition root creates server-only components in code
- Forbidden: lambda subscriptions on persistent events

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [x] `ZoneServerComposition` builds the Skill System with production adapters and stand-ins, adds its step to `_simulationSteps` after `AutoAttackCombatSystem`, and registers `SkillCastRequestHandler` before `Seal`.
- [x] **Spawn**: a player admitted to the zone gets `OnEntitySpawned(entity, classType)` before the `SkillCooldownSnapshot` is built (the entry listener runs first).
- [x] **Despawn**: the slot release listener calls `OnEntityDespawned`; Skill is added to `CompositeZoneSlotReleaseListener` so its `BeforeRelease` still finds the player's slot.
- [x] **Level-up**: the Skill System is subscribed to `LevelingService.OnLevelUp` with a named method, once.
- [x] **EC-SK-1**: in the real `ZoneTickPipeline`, an auto that fires and a cast by the same player on tick T both resolve; the auto resolves first; the timer is 0 after the tick.
- [x] **AC-SK-49 / EC-SK-6**: when `OnLevelUp(entity, N)` fires on tick T before the Skill step (for example from an auto-attack kill's XP in the Combat step), a cast of the level-N skill processed on T is accepted, not `SkillLocked`.
- [x] A `SkillCastRequest` sent on tick T is resolved in phase 3 of tick T (stored in dispatch, consumed in phase 3), and its `SkillCastResult` leaves in tick T's R-U batch.
- [x] Wiring test: the composed Skill System's seams are the expected production types (the Auto-Attack Story 009 composition test precedent: assert by type).

---

## Implementation Notes

- **Where**: add `BuildSkillSystem(...)` next to `BuildAutoAttackCombat` in `src/ServerLogic/ZoneServer/ZoneServerComposition.cs` (around line 1631), called after it, so `_simulationSteps.Add(skill)` comes after `_simulationSteps.Add(combat)`. Pass in the `AutoAttackCombatSystem` (as `IAutoAttackSkillUseSink`), the `AutoFaceEmitter` (`_owner.AutoAttackFaceOutput`), the inner stand-in damage applier (Decision B: not the `DamageOutputApplier` wrapper), `ZoneStats` / `CharacterStats`, `StatusEffectsService`, `TargetSlotTracker`, the outbox and the R-U step.
- **Release listener**: `wiring.Pool.SetReleaseListener` is called once in `BuildAutoAttackCombat` with `new CompositeZoneSlotReleaseListener(statusEffects, movement, combat)`. Move that call so it runs once, after both builds, with Skill included. Order: Combat last stays, per its comment. Put Skill before Combat, or after: check whether `BeforeRelease` order matters for Skill (it does not read other systems on despawn), and keep Combat last.
- **Entry listener**: `ZoneEntryCompletion.SetEntryListener` takes one `IZonePlayerEntryListener`, and `MovementService` is it today. Add a small composite listener (Movement first, then Skill) instead of replacing Movement. The Skill side reads the class type from `LevelingService` (add a public `TryGetClassType(EntityID, out byte)` if none exists; `GetClassType` is private today) and calls `OnEntitySpawned`.
- **Level-up**: subscribe with the Story 003 method, once, in the build method. Unsubscribe in the composition's dispose path if one exists for other subscriptions.
- **Cooldown snapshot**: see the Story 010 carry below (forwarding responder, readiness 2026-11-04).
- **Observe**: call `Observe(nameof(SkillSystemService))` as the other builds do.
- **Pipeline tests**: drive the real `ZoneTickPipeline` with test doubles at the edges, as `AutoAttackCombat_AutoFace_tests.cs` did for N / N+1 and the write order.
- **Story 007 carry (2026-11-04)**: wire `SkillDamageExecutionStep` as `SkillCastExecutor`'s execution step (a composite with Story 008's step): `CharacterStatsSkillStatSource`, `DamageCalculatorSkillAdapter`, the zone's `AutoAttackCombatSystem` as `IAutoAttackSkillUseSink` and `IMobKillReporter`, `AutoFaceEmitter`, the stand-in XP award and the inner `StandInAutoAttackDamageApplier` (not `DamageOutputApplier`). Add the Decision B test Story 007 could only assert structurally: a skill hit through the production inner applier with a recording outbox and batch sends zero `DamageEvent` / `SelfDamageEvent`.
- **Story 008 carry (2026-11-04)**: the execution step is `new CompositeSkillExecutionStep(damageStep, new SkillEffectExecutionStep(new StatusEffectsSkillEffectSink(zoneStatusEffectsService), statSource))`, using the same `ISkillStatSource` as the damage step. Add one integration test: a buff skill through the composed zone reaches the real `StatusEffectsService` (`TryGetActiveEffect` true), and a heal reports the delivered `HealAmount` in `SendAccepted`.
- **Story 009 carry (2026-11-04)**: the zone's `ISkillCastOutput` is `new SkillCastOutputEmitter(pool, zoneId, targetSlotTracker, new ZoneSkillCastPartySource(pool, partyZonePresence), ruBatchCollector, skillSystemService)`; pass the same instance to `SkillCastStep` (rejections) and `SkillCastExecutor` (accepts). Add one integration test through the real `RUBatchCollector` / `RUBatchWriter`: an accepted cast puts the caster's `SkillCastResult` and `SkillCooldownUpdate` in one R-U batch, result written first (the "same batch" half of CR-SK-23 that Story 009 showed only as "same call").
- **Story 010 carry (2026-11-04; decided at readiness 2026-11-04): forwarding responder.** `ZoneEntryCompletion` takes its responder once, at construction inside `ZoneInstancingWiring` (`options.Responder ?? new LoggingZoneEntryResponder()`, `ZoneInstancingWiring.cs:278`, readonly field), and `BuildZoneInstancing` (`ZoneServerComposition.cs:1505`) runs before the Skill System can exist (it needs `wiring.Pool`). So: add a small `ForwardingZoneEntryResponder : IZoneEntryResponder` (in `src/ServerLogic/ZoneServer/`, next to the composition) whose `Target` defaults to a `LoggingZoneEntryResponder`; pass it as `options.Responder` in `BuildZoneInstancing`; after the Skill System is built set `Target = new SkillCooldownSnapshotResponder(new LoggingZoneEntryResponder(), skillSystemService, outbox)`, so `SessionReady` still reaches the logging stub. This follows the `_disconnector.Target` precedent in the same file; `ZoneEntryCompletion` (Zone Instancing epic) is not changed. A null `Target` is not allowed (setter throws). In the pipeline test assert the joiner's `SkillCooldownSnapshot` carries its class skill ids (the `OnEntitySpawned` hook ran before the snapshot, `ZoneEntryCompletion.cs:113` then 120), not the all-zero no-store fallback.
- **Reconnect (decided at readiness 2026-11-04): cooldowns reset, nothing to build.** The zone has no ghost reattach: a reconnect is a fresh admission with a new slot and a new EntityID (the counter never reuses ids, `ZoneSlotPool`), and the old entity's slot release removes its store. So `OnEntitySpawned`'s re-initialize-in-place path is never reached by a reconnect, and a reconnecting player's snapshot has no running cooldowns — the same as consumables (`consumable-use-system.md` rule 13) and EC-SK-13 ("returns to Ready"). CR-SK-12's "snapshot on reconnect" still holds (the snapshot is sent); keeping running cooldowns across a reconnect waits on ghost reattach (Networking Core). Covered by the "Despawn … re-admit → no cooldowns" QA case. At `/story-done`, add one line to `skill-system.md` CR-SK-12 stating this (as the Story 008 heal note was handled).

---

## Out of Scope

- New behaviour: this story only wires Stories 001–010
- The roster asset: Story 012 (composition loads whatever `SkillDatabase` is configured; tests use an in-code database)

---

## QA Test Cases

- **Step order**: the composed `_simulationSteps` lists Movement, then Combat, then Skill.
- **Handler registered**: a `SkillCastRequest` sent to the composed dispatcher reaches the Skill intake.
- **Spawn before snapshot**: admit a player through the composed zone → its `SkillCooldownSnapshot` carries the class's skill ids in class-list order (proof `OnEntitySpawned` ran first), every expiry 0, and no "no skill store" warning. (A pre-seeded cooldown is not reachable: a new admission has a new EntityID and a fresh store.)
- **Forwarding responder**: before `Target` is set, both calls reach the logging stub; after, `SendSessionReady` reaches the inner logging stub and the snapshot is sent; setting `Target = null` throws.
- **Despawn**: release the slot → the store is gone; re-admit → no cooldowns.
- **Level-up subscription**: raise `OnLevelUp` on the composed `LevelingService` → the skill unlocks; exactly one subscriber from the Skill System (no double subscription).
- **EC-SK-1**: real pipeline, timer at `CycleTicks` and a cast queued on T → `HitCheckResult` / damage from the auto, then the `SkillCastResult`; timer 0 after T.
- **AC-SK-49**: on tick T, a Combat-step kill grants XP to level N; a level-N skill cast stored on T → accepted.
- **Same-tick result**: request on T → result in T's batch.
- **Wiring by type**: each injected seam is the expected production or stand-in type.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Composition_tests.cs` — must exist and pass

**Status**: [x] Created 2026-11-04 (23 tests after code review; EditMode 6380 / 6251 / 0 failed / 129 skipped)

---

## Dependencies

- Depends on: Stories 003, 008, 009, 010
- Unlocks: None within the epic

---

## Completion Notes

**Completed**: 2026-11-04
**Criteria**: 8/8 passing (all automated; none deferred)
**Deviations** (advisory):
- **Spawn criterion met in tests only — TD-090 (P1).** In the zone process every admission fails: `CharacterPersistenceService` is store-only, so `ApplyLoadedRecord` throws and no class type is registered. Fix: Character Persistence Story 018 (drafted).
- **User decisions (2026-11-04):** `ClassRegistryOverride` shared with Leveling (empty default); `StandInSkillDamageCalculator` (rejects every hit; `DamageCalculatorSkillAdapter` unwired); `SkillDatabaseOverride` / `SkillSystemConfigOverride` with in-code defaults. Defaults are silent (composition tests fail on unexpected logs).
- The zone's entity state source is still `LoggingZoneEntityStateSource` (knows no entities), so live casts are rejected `InvalidTarget` until a real source exists; tests use `EntityStateSourceOverride`.
- `BuildStatusEffects` now keeps the zone `LevelingService` and calls `AttachCharacterStats`: XP / level-up paths are live in the zone (dormant: stand-in XP award gives 0).
- Reconnect: cooldowns reset (fresh admission); `skill-system.md` CR-SK-12 clarified.
- EC-SK-1 "auto first" asserted by execution order (the R-U writer puts `SkillCastResult` before `HitCheckResult`); AC-SK-49 level-up raised from an inserted XP step (stand-in damage never kills).
- Story 008 bug fixed here: `SkillEffectExecutionStep.ApplyHeal` reports HP delivered (`CurrentHP` after − before); Effects tests model HP.
- Tests read private members by reflection (`LevelingService.OnLevelUp`, owner fields for wiring-by-type, `_disposables` for teardown — the full zone teardown throws on `SaveSession`, TD-090).
- Added beyond the notes: `ForwardingZoneEntryResponder` + `CompositeZonePlayerEntryListener`, `SkillZoneListeners.cs` (spawn / release adapters, `StaticSkillSystemConfigSource`, `SkillSystemTeardown`), `LevelingService.TryGetClassType`, `SkillDamageCalculatorOverride`, `EntityStateSourceOverride`; three existing wiring tests updated (Observe order, 5 steps).
**Test Evidence**: Integration: `tests/EditMode/SkillSystem/SkillSystem_Composition_tests.cs`, 23 tests. EditMode 6380 / 6251 / 0 failed / 129 skipped, started 17:56:46Z.
**Code Review**: Complete (unity-specialist ISSUES + qa-tester GAPS → "fix all 2–12" applied; item 1 → TD-090 / Story 018).
