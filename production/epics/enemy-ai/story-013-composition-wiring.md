# Story 013: Composition Wiring

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

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

- [ ] `ZoneServerComposition` builds the registry (Story 002 loader at S2), `MobRuntimeStore`, `EnemyAISystem` and the Story 010–012 seams; disposes them in the teardown Dispose pass.
- [ ] `StandInNavMobSpeedSource` and `NavigationConfigValues.STAND_IN_MOB_MOVE_SPEED` are removed; navigation gets `MoveSpeed` from the registry (`INavMobSpeedSource` over `IMobDefinitionRegistry`).
- [ ] `EmptyMobCommittedPositionSource` is replaced by the runtime store.
- [ ] **CR-AI-2 order**: simulation steps run Navigation (1–2) → … → Status Effects → Enemy AI → Auto-Attack Combat → Skill; Movement's placement relative to Enemy AI and mob position integration is decided at readiness (Story 009 note) and pinned by a composition test asserting the step list order.
- [ ] Loot: a logging stand-in `ILootTableService` (logs once) until Loot Table is wired into the zone (TD entry).
- [ ] **AC-AI-21**: the ADR-012 boundary test covers the new Enemy AI files (no `IronGrind.Client` reference); any type added to `Foundation` is on the allow-list with a named client consumer (expected: none).
- [ ] Existing composition, Navigation and Combat test suites pass; full EditMode run recorded.

---

## Implementation Notes

- Comments in `ZoneServerComposition.cs` (lines near `NavigationPhaseStep` and `BuildAutoAttackCombat`) say "follows in the Enemy AI epic" — update them.
- *(From Story 008 readiness, 2026-11-16.)* `MobEventBus` calls `OnMobSpawned` handlers in subscription order: build `MobRuntimeStore` (it seeds stats on spawn) **before** `EnemyAISystem`, so controllers never see an unseeded mob. Pass the store's `IZoneMobTransformReader` adapter to `ZoneEntityStateSource`, update the pinned test `test_composition_entity_state_source_serves_a_roster_mob_after_startup` (raise `MobSpawned` on `MobEvents`; seeded HP instead of 0 — the rig needs the built bus, not a navigation seam), and narrow TD-102 to the mob combat-state reader (Story 012).
- *(From Story 009 readiness, 2026-11-16.)* Mob position integration is a separate step, `EnemyAISystem.IntegrationStep` (ADR-002 phase 4): add `EnemyAISystem` between Status Effects and Auto-Attack, and add `IntegrationStep` **after the last phase 3 system** (after Skill). It reads only velocities cached in phase 3. `NavigationPhaseStep`'s source becomes the `MobRuntimeStore`. Build `ZoneAggroCandidateSource` over the zone's `ZoneSlotPool` and `MovementService`. The pinned step-order test asserts both positions.

---

## Out of Scope

- Mob Spawning epic: something calling `TryAddMob` / `RaiseMobSpawned` in production; the end-to-end smoke run (player kills a spawned mob) belongs there

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Composition_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 010, 011, 012
- Unlocks: Story 014
