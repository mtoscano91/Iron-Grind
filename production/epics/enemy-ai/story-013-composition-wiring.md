# Story 013: Composition Wiring

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 4–5 h (wiring only: 2 small new classes, 6 stand-ins deleted, ~4 composition test rigs updated)

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-1, CR-AI-2, AC-AI-21
**Requirement**: `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-002 (Decision 1); ADR-012 (Decision 6 boundary test)
**ADR Decision Summary**: The composition root lists phase 3 systems explicitly; navigation phases 1–2 run before all of them. Enemy AI lives in `IronGrind.ServerLogic` and the boundary test enforces no client assembly reference.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: phase 3 order listed explicitly by the composition root — ADR-014
- Required: no module outside `ZoneNavigationService` calls `Tick` / `SyncAgentPositions`

---

## Acceptance Criteria

- [ ] `ZoneServerComposition` builds the registry, `MobRuntimeStore`, `EnemyAISystem` and the Story 010–012 seams; disposes them in the teardown Dispose pass. **Registry source** (user decision 2026-11-16): `MobRegistryOverride` (new internal test seam) `?? (_staticDataLoader as IZoneMobData)?.LoadedMobRegistry`; when both are null, startup logs an error and fails, mirroring `BuildInventoryAndEquipment` with no item database.
- [ ] `StandInNavMobSpeedSource` and `NavigationConfigValues.STAND_IN_MOB_MOVE_SPEED` are removed; navigation gets `MoveSpeed` from the registry through a new `MobDefinitionRegistryNavSpeedSource : INavMobSpeedSource` (known type → `true` + `MoveSpeed`; unknown type or `MobTypeID.Invalid` → `false`).
- [ ] `EmptyMobCommittedPositionSource` is replaced by the runtime store.
- [ ] **CR-AI-2 order** (user decision 2026-11-16: Movement stays before Enemy AI): the simulation step list is, in order, … → `NavigationPhaseStep` (phases 1–2) → Status Effects → Movement → **Enemy AI** → Auto-Attack Combat → Skill → **`EnemyAISystem.IntegrationStep`** (`MobPositionIntegrationStep`, ADR-002 phase 4). Pinned by a composition test asserting the step list order. Enemy AI's aggro scan, pursuit and mob hit checks see the same post-Movement player positions as Combat and Skill.
- [ ] Loot: a logging stand-in `ILootTableService` (logs once) until Loot Table is wired into the zone (TD entry).
- [ ] **AC-AI-21**: the ADR-012 boundary test covers the new Enemy AI files (no `IronGrind.Client` reference); any type added to `Foundation` is on the allow-list with a named client consumer (expected: none).
- [ ] Existing composition, Navigation and Combat test suites pass; full EditMode run recorded.

---

## Implementation Notes

- Comments in `ZoneServerComposition.cs` (lines near `NavigationPhaseStep` and `BuildAutoAttackCombat`) say "follows in the Enemy AI epic" — update them.
- *(From Story 008 readiness, 2026-11-16.)* `MobEventBus` calls `OnMobSpawned` handlers in subscription order: build `MobRuntimeStore` (it seeds stats on spawn) **before** `EnemyAISystem`, so controllers never see an unseeded mob. Pass the store's `IZoneMobTransformReader` adapter to `ZoneEntityStateSource`, update the pinned test `test_composition_entity_state_source_serves_a_roster_mob_after_startup` (raise `MobSpawned` on `MobEvents`; seeded HP instead of 0 — the rig needs the built bus, not a navigation seam), and narrow TD-102 to the mob combat-state reader (Story 012).
- *(From Story 009 readiness, 2026-11-16.)* Mob position integration is a separate step, `EnemyAISystem.IntegrationStep` (ADR-002 phase 4): add `EnemyAISystem` between Status Effects and Auto-Attack, and add `IntegrationStep` **after the last phase 3 system** (after Skill). It reads only velocities cached in phase 3. `NavigationPhaseStep`'s source becomes the `MobRuntimeStore`. Build `ZoneAggroCandidateSource` over the zone's `ZoneSlotPool` and `MovementService`. The pinned step-order test asserts both positions.
- *(From Story 010 readiness, 2026-11-16; calculator part superseded by the Story 012 note below.)* Build `MobAttackResolver` with the zone's `HitResultEmitter`, the shared `DamageCalculatorAutoAttackAdapter`, the zone's Character Stats and an `AutoAttackDamageOutputEmitter`, and pass it to `EnemyAISystem`.
- *(From Story 013 readiness, 2026-11-16.)* Constructor inputs: Enemy AI config `EnemyAIConfigValues.Defaults` (no host asset yet); `returnSpeedCap` from the composition's `NavigationConfigValues.ReturnSpeedCap` (the config built in the navigation build); store / system capacity = the zone's mob capacity (`ZoneInstancingWiring` options `MaxMobs`, default `ZoneSlotPool.MAX_MOBS_PER_ZONE`); candidates `new ZoneAggroCandidateSource(zone, wiring.Pool, new MovementPlayerPositionReader(movement))`; the store's stat writer `CharacterStatsMobStatWriter` over `ZoneCharacterStats`; random = the zone `RandomProvider`. Build order: registry → store (before Combat) → Combat → Enemy AI (after Combat and Movement) → bind `ZoneEntityStateSource`. Step-list order is gap-free per AC-4 regardless of build order.
- *(From Story 013 readiness, 2026-11-16.)* Test rigs: the rigs that set `ItemDatabaseOverride` (`ZoneServer_CompositionWiring_tests`, `Navigation_Composition_tests`, `GatewayLink_ZoneWiring_tests`, the Character Persistence zone rigs) also set `MobRegistryOverride` to a small test registry built with `MobDefinitionRegistry.Build`; rigs that deliberately pass no item database keep failing for that reason first.
- *(From Story 012 readiness, 2026-11-16 — supersedes the Story 010 "no-weapon query" note above.)* The item database, `EquipmentService` (`ZoneEquipment`, implements `IEquippedWeaponQuery`; returns `ItemID.Invalid` for mobs without logging, EC-EQS-9) and `EnhancementBonusProvider` are already in the zone. Build **one** `DamageCalculator` over `ZoneCharacterStats`, `ZoneEquipment`, `ItemDatabaseInUse`, an `EnhancementBonusProvider` (`EnhancementConfig.Default`), `DamageCalculationConfig.Default` and the zone `RandomProvider`; use it for player autos (`DamageCalculatorAutoAttackAdapter`, replacing `StandInAutoAttackDamageCalculator`), skills (`DamageCalculatorSkillAdapter`, replacing `StandInSkillDamageCalculator`; `SkillDamageCalculatorOverride` still wins when set) and `MobAttackResolver`. No no-weapon query and no "mobs physical only" TD.
- *(From Story 012 readiness, 2026-11-16.)* Wire the Story 012 seams: `MobTargetValidity` (pool, store, stats), `CharacterStatsAutoAttackDamageApplier` as the inner applier (still wrapped by `DamageOutputApplier` for Combat; the Skill damage step keeps the inner one), `ZoneMobRosterMobTypeSource(wiring.MobRoster)`, `MobRuntimeStoreXpAward` for both Combat and the Skill damage step. The store must exist before `AutoAttackCombatSystem` (the validity and XP seams read it). Delete the six replaced stand-ins (`StandInAutoAttackTargetValidity`, `StandInAutoAttackDamageApplier`, `StandInAutoAttackXpAward`, `StandInAutoAttackMobTypeSource`, `StandInAutoAttackDamageCalculator`, `StandInSkillDamageCalculator`) and fix the tests that reference them. Bind `ZoneEntityStateSource` with `MobAwareZoneEntityCombatReader(enemyAI, new AutoAttackZoneEntityCombatReader(combat))`; the pinned test then asserts mob `isInCombat` from the mob state. Closes TD-102.
- *(From Story 011, 2026-11-16.)* `EnemyAISystem` constructor is now `(zone, bus, store, registry, navigation, stats, attackResolver, config, returnSpeedCap, candidates, combat, loot, capacity)`: it subscribes to `ICombatService.OnMobDied` in the constructor, so build `AutoAttackCombatSystem` **before** `EnemyAISystem` (construction order is independent of the phase 3 step order). Pass the logging stand-in `ILootTableService` (TD-107). Enemy AI never calls `IZoneMobRoster.RemoveMob` (Mob Spawning transition 4).

---

## Out of Scope

- Mob Spawning epic: something calling `TryAddMob` / `RaiseMobSpawned` in production; the end-to-end smoke run (player kills a spawned mob) belongs there

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-16.*

1. **Step order (AC-4)**: after startup, `SimulationSteps` is exactly `ZoneInstancingWiring`, `NavigationPhaseStep`, `StatusEffectsService`, `MovementService`, `EnemyAISystem`, `AutoAttackCombatSystem`, `SkillCastStep`, `GatewayLinkPump`, `MobPositionIntegrationStep` (exact list asserted, not just relative positions; the link pump stays at the end of phase 3, the integration step is phase 4).
2. **Registry missing (AC-1)**: stand-in S2 loader and no `MobRegistryOverride` → startup fails, error logged (`LogAssert.Expect`), nothing ticks.
3. **Registry from loader (AC-1)**: an `IZoneMobData` loader with a registry and no override → startup succeeds, the system uses that registry.
4. **Teardown (AC-1)**: after Dispose, raising `MobSpawned` on the bus creates no store entry and no controller (handlers unsubscribed); no exception.
5. **Spawn seeding (AC-1, Story 008 note)**: `RaiseMobSpawned` on the built bus → store entry with seeded HP, controller `Dormant`.
6. **Speed source (AC-2)**: `MobDefinitionRegistryNavSpeedSource` returns `MoveSpeed` for a known type; `false` for unknown and `Invalid` (unit tests).
7. **Removed stand-ins (AC-2, AC-3, Story 012 note)**: `StandInNavMobSpeedSource`, `STAND_IN_MOB_MOVE_SPEED`, `EmptyMobCommittedPositionSource` and the six Combat/Skill stand-ins no longer exist in `src/` (verified by grep at `/story-done`; tests referencing them updated).
8. **Committed positions (AC-3)**: `NavigationPhaseStep` syncs a spawned mob at its store position.
9. **Entity state pin (TD-102)**: `test_composition_entity_state_source_serves_a_roster_mob_after_startup` raises `MobSpawned` and asserts seeded HP / MaxHP and `isInCombat` from the mob state (Dormant → false; Pursuing → true).
10. **Composition kill (AC-1, AC-5)**: in the built composition, a player auto-attacks a spawned mob to death → XP awarded once (`EffectiveKillXP`), mob despawned by Enemy AI, loot stand-in logs once (`LogAssert.Expect`) and a second kill does not log again.
11. **Boundary (AC-6)**: `AssemblyBoundary_Tests` passes and covers `src/ServerLogic/EnemyAI/`; no new `Foundation` types (allow-list unchanged).
12. **Regression (AC-7)**: composition, Navigation, Combat, Skill and Movement suites pass; full batch EditMode run recorded with 0 failed.

**Performance**: wiring only; no new per-tick work beyond what Stories 008–012 already own. Tick budget is measured by Story 014.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Composition_tests.cs` — must exist and pass

**Status**: [x] Created — 15/15 pass (2026-11-16)

---

## Dependencies

- Depends on: Stories 002, 010, 011, 012 (all Complete)
- Unlocks: Story 014

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 7/7 passing (all by automated tests; AC-7 by the recorded batch run)
**Deviations** (advisory):
1. The asserted phase 3 list also contains `ZoneInstancingWiring` (first) and `GatewayLinkPump` (end of phase 3, before the phase 4 `MobPositionIntegrationStep`); QA 1 updated to the exact list. The order is written in one place (`AddPhaseThreeSteps`); construction order differs (Combat before Enemy AI for `OnMobDied`).
2. The zone's `MobEventBus` and `MobRuntimeStore` are now built always; with a navigation seam (test compositions) there is no `EnemyAISystem`, `NavigationPhaseStep` or integration step (no `INavigationProvider`), but the real Combat seams run over the store. Production passes no seam. In seam rigs a roster mob that never raised `MobSpawned` reads a zero transform.
3. QA 10 reports kills through `AutoAttackCombat.ReportKill` (XP via `MobRuntimeStoreXpAward`, both mobs Dead, loot stand-in logs once) instead of a full player auto kill: the composition rig has no loaded, named player, so Combat's state source gives the player no position. The full kill path is covered by `EnemyAI_CombatSeams_tests.cs` (Story 012).
4. `BuildOutboundAndPipeline` split into `BuildMobStoreAndNavigation`, `BuildCombatAndEnemyAI`, `AddPhaseThreeSteps` (no change to construction, Observe or step order). Extra files touched for the stand-in deletion: `tests/PlayMode/Navigation/NavigationPlayModeFakes.cs` (new), `Navigation_SpawnPointValidation_tests.cs`, `ZoneServer_CompositionHandshake_tests.cs`.
**Tech debt**: TD-102 closed. TD-107 stays open (the logging `StandInLootTableService` is the seam it replaces). None new.
**Test Evidence**: Integration — `tests/EditMode/EnemyAI/EnemyAI_Composition_tests.cs` 15/15; batch EditMode 7803 / 7674 passed / 0 failed / 129 skipped (2026-11-16); PlayMode `Navigation_UnityAdapter_tests` + `Navigation_DrainFixture_tests` 21/21.
**Code Review**: Complete — CHANGES REQUIRED (one missing test: mob `isInCombat` through the mob-aware reader; Dispose references; stale comments) → all fixes and suggestions applied (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
