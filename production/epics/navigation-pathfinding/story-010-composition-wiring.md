# Story 010: Composition Wiring

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-2, CR-NAV-3, CR-NAV-12, CR-NAV-14, EC-NAV-7, Interactions table
**Requirement**: `TR-nav-001`, `TR-nav-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002 (Decisions 1, 3); ADR-014: Inbound Request Dispatch and Tick Order; ADR-009 Amendment 1
**ADR Decision Summary**: The simulation step runs ADR-002 phases 1–4; phase-3 systems run in the order the composition lists. Navigation phases 1–2 come before every phase-3 system. Teardown halts the tick before navigation teardown (step 3).

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (wiring only; engine behaviour verified in Story 009)
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: the tick order is code in one class; one failing step does not stop the tick
- Required: the composition root creates server-only components in code
- Required: zone teardown nav sequence — tick halted, then `Teardown()`, then NavMesh removed
- Forbidden: lambda subscriptions on persistent events

---

## Acceptance Criteria

- [ ] `ZoneServerHost` passes a real `ZoneNavigationService` (Unity adapters from Story 009, `NavigationConfig` with in-code defaults and an override hook like the other `…Override` options, `StandInNavMobSpeedSource`) instead of `StandInZoneNavigation`.
- [ ] The composition creates one `MobEventBus` per zone, before S6, and exposes it for the Enemy AI and Mob Spawning epics; the service subscribes in `Initialize`.
- [ ] A navigation phase step (Story 005 entry point) joins `_simulationSteps` before Status Effects, Movement, Combat and Skill (phase 1–2 before phase 3); `IMobCommittedPositionSource` is an empty stand-in until Enemy AI.
- [ ] S6: a valid instance initialises navigation (pool of 150 created); an invalid instance makes `Initialize` throw → S6 fails → teardown runs step 3 (`Teardown`) because `MarkNavigationInitialized` was set first.
- [ ] Teardown step 3 calls the real `Teardown` after the tick is halted; `NavMeshDataInstance` is removed once (no stand-in "removal belongs to the Navigation epic" path left in `UnitySceneLoader` comments).
- [ ] The composed service is reachable as `INavigationProvider` for the Enemy AI epic (a property or the composition's existing service registry).
- [ ] Wiring test by type (the Auto-Attack Story 009 / Skill Story 011 precedent) and step-order test; the full EditMode suite still passes; the Story 008 smoke (zone start + login) still passes.

---

## Implementation Notes

- **Host**: `src/ServerLogic/ZoneServer/ZoneServerHost.cs:66` builds `new StandInZoneNavigation()`. Keep `StandInZoneNavigation` for tests that need it (`ZoneServer_CompositionTeardown_tests.cs:870`).
- **Pool root**: the factory's root `GameObject` must survive the Single-mode scene load like the host object (ADR-009 A1-3), or be created after the load in `Initialize`. `Initialize` runs after the load at S6, so creating the root there is enough; confirm it is destroyed in `Teardown`.
- **Step position**: `_simulationSteps` today is (link pump?), Zone Instancing wiring, Status Effects, Movement, Combat, Skill cast (`ZoneServerComposition.cs` ~1862–2128). Insert the navigation step right after the Zone Instancing wiring step and before Status Effects; add a comment citing ADR-002 Decision 1 and CR-AI-2 (Status Effects → Enemy AI → Auto-Attack follows in the Enemy AI epic). Check where the link pump sits relative to "phase 3" before inserting.
- **Observe**: call `Observe(nameof(ZoneNavigationService))` like the other builds.
- **Existing tests** that count steps or assert `Observe` order will need updating (Skill Story 011 precedent: three wiring tests updated).
- **Bus lifetime**: Zone Instancing destroys the bus when the zone closes (CR-MS-5); at minimum unsubscribe navigation in `Teardown` (Story 003) and drop the reference at composition dispose.

---

## Out of Scope

- Enemy AI epic: the committed-position source, speed source (`IMobDefinitionRegistry`), the phase-3 Enemy AI step
- Mob Spawning epic: raising `OnMobSpawned`, calling the spawn-point validator

---

## QA Test Cases

- **Host type**: the composed zone's navigation is a `ZoneNavigationService` with Unity adapters; the speed source is `StandInNavMobSpeedSource`.
- **Step order**: the composed `_simulationSteps` lists the navigation step before `StatusEffectsService`, Movement, Combat and the Skill cast step.
- **Bus**: one `MobEventBus` per composition; raising `OnMobSpawned` after S6 reaches the service (fake agent factory through the override hook → a claim).
- **Valid start**: S6 with a valid fake instance → pool of 150 (fake factory count); startup continues.
- **Invalid start**: S6 with an invalid instance → S6 returns false or the startup fails as other S6 failures do; teardown calls `Teardown` once; no throw escapes teardown.
- **Teardown order**: tick halted before `Teardown` (recorder across the composition's teardown steps).
- **INavigationProvider exposed**: the composition returns the same instance as `INavigationProvider`.
- **Regression**: full EditMode suite passes; smoke from Story 008 passes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Navigation/Navigation_Composition_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 006, 008, 009
- Unlocks: Enemy AI epic (consumes `INavigationProvider` and the bus); Mob Spawning epic
