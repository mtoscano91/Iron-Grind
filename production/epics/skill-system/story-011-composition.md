# Story 011: Composition and Tick-Order Wiring

> **Epic**: Skill System
> **Status**: Ready
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

- [ ] `ZoneServerComposition` builds the Skill System with production adapters and stand-ins, adds its step to `_simulationSteps` after `AutoAttackCombatSystem`, and registers `SkillCastRequestHandler` before `Seal`.
- [ ] **Spawn**: a player admitted to the zone gets `OnEntitySpawned(entity, classType)` before the `SkillCooldownSnapshot` is built (the entry listener runs first).
- [ ] **Despawn**: the slot release listener calls `OnEntityDespawned`; Skill is added to `CompositeZoneSlotReleaseListener` so its `BeforeRelease` still finds the player's slot.
- [ ] **Level-up**: the Skill System is subscribed to `LevelingService.OnLevelUp` with a named method, once.
- [ ] **EC-SK-1**: in the real `ZoneTickPipeline`, an auto that fires and a cast by the same player on tick T both resolve; the auto resolves first; the timer is 0 after the tick.
- [ ] **AC-SK-49 / EC-SK-6**: when `OnLevelUp(entity, N)` fires on tick T before the Skill step (for example from an auto-attack kill's XP in the Combat step), a cast of the level-N skill processed on T is accepted, not `SkillLocked`.
- [ ] A `SkillCastRequest` sent on tick T is resolved in phase 3 of tick T (stored in dispatch, consumed in phase 3), and its `SkillCastResult` leaves in tick T's R-U batch.
- [ ] Wiring test: the composed Skill System's seams are the expected production types (the Auto-Attack Story 009 composition test precedent: assert by type).

---

## Implementation Notes

- **Where**: add `BuildSkillSystem(...)` next to `BuildAutoAttackCombat` in `src/ServerLogic/ZoneServer/ZoneServerComposition.cs` (around line 1631), called after it, so `_simulationSteps.Add(skill)` comes after `_simulationSteps.Add(combat)`. Pass in the `AutoAttackCombatSystem` (as `IAutoAttackSkillUseSink`), the `AutoFaceEmitter` (`_owner.AutoAttackFaceOutput`), the inner stand-in damage applier (Decision B: not the `DamageOutputApplier` wrapper), `ZoneStats` / `CharacterStats`, `StatusEffectsService`, `TargetSlotTracker`, the outbox and the R-U step.
- **Release listener**: `wiring.Pool.SetReleaseListener` is called once in `BuildAutoAttackCombat` with `new CompositeZoneSlotReleaseListener(statusEffects, movement, combat)`. Move that call so it runs once, after both builds, with Skill included. Order: Combat last stays, per its comment. Put Skill before Combat, or after: check whether `BeforeRelease` order matters for Skill (it does not read other systems on despawn), and keep Combat last.
- **Entry listener**: `ZoneEntryCompletion.SetEntryListener` takes one `IZonePlayerEntryListener`, and `MovementService` is it today. Add a small composite listener (Movement first, then Skill) instead of replacing Movement. The Skill side reads the class type from `LevelingService` (add a public `TryGetClassType(EntityID, out byte)` if none exists; `GetClassType` is private today) and calls `OnEntitySpawned`.
- **Level-up**: subscribe with the Story 003 method, once, in the build method. Unsubscribe in the composition's dispose path if one exists for other subscriptions.
- **Cooldown snapshot**: install the Story 010 responder (or decorator) in `ZoneInstancingWiring` options.
- **Observe**: call `Observe(nameof(SkillSystemService))` as the other builds do.
- **Pipeline tests**: drive the real `ZoneTickPipeline` with test doubles at the edges, as `AutoAttackCombat_AutoFace_tests.cs` did for N / N+1 and the write order.
- **Story 007 carry (2026-11-04)**: wire `SkillDamageExecutionStep` as `SkillCastExecutor`'s execution step (a composite with Story 008's step): `CharacterStatsSkillStatSource`, `DamageCalculatorSkillAdapter`, the zone's `AutoAttackCombatSystem` as `IAutoAttackSkillUseSink` and `IMobKillReporter`, `AutoFaceEmitter`, the stand-in XP award and the inner `StandInAutoAttackDamageApplier` (not `DamageOutputApplier`). Add the Decision B test Story 007 could only assert structurally: a skill hit through the production inner applier with a recording outbox and batch sends zero `DamageEvent` / `SelfDamageEvent`.
- **Story 008 carry (2026-11-04)**: the execution step is `new CompositeSkillExecutionStep(damageStep, new SkillEffectExecutionStep(new StatusEffectsSkillEffectSink(zoneStatusEffectsService), statSource))`, using the same `ISkillStatSource` as the damage step. Add one integration test: a buff skill through the composed zone reaches the real `StatusEffectsService` (`TryGetActiveEffect` true), and a heal reports the delivered `HealAmount` in `SendAccepted`.
- **Story 009 carry (2026-11-04)**: the zone's `ISkillCastOutput` is `new SkillCastOutputEmitter(pool, zoneId, targetSlotTracker, new ZoneSkillCastPartySource(pool, partyZonePresence), ruBatchCollector, skillSystemService)`; pass the same instance to `SkillCastStep` (rejections) and `SkillCastExecutor` (accepts). Add one integration test through the real `RUBatchCollector` / `RUBatchWriter`: an accepted cast puts the caster's `SkillCastResult` and `SkillCooldownUpdate` in one R-U batch, result written first (the "same batch" half of CR-SK-23 that Story 009 showed only as "same call").
- **Story 010 carry (2026-11-04)**: wrap the zone's entry responder: `new SkillCooldownSnapshotResponder(options.Responder ?? new LoggingZoneEntryResponder(), skillSystemService, outbox)` (`ZoneInstancingWiring.cs:278`), so `SessionReady` still reaches the inner responder. In the pipeline test assert the joiner's `SkillCooldownSnapshot` carries its class skill ids (the `OnEntitySpawned` hook ran before the snapshot, `ZoneEntryCompletion.cs:113` then 120), not the all-zero no-store fallback. Same-zone reconnect (`zone-instancing.md:378`, CR-ZI-17 (b)): the spawn hook must not reset an existing store for the same entity, so the snapshot carries the running cooldowns (CR-SK-12); `OnEntitySpawned` today re-initializes a held record in place, so decide and test this.

---

## Out of Scope

- New behaviour: this story only wires Stories 001–010
- The roster asset: Story 012 (composition loads whatever `SkillDatabase` is configured; tests use an in-code database)

---

## QA Test Cases

- **Step order**: the composed `_simulationSteps` lists Movement, then Combat, then Skill.
- **Handler registered**: a `SkillCastRequest` sent to the composed dispatcher reaches the Skill intake.
- **Spawn before snapshot**: admit a player with a skill on cooldown (pre-seeded) → the snapshot carries it; admit a fresh player → all zero, no warning.
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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 003, 008, 009, 010
- Unlocks: None within the epic
