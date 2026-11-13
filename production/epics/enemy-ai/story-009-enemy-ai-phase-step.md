# Story 009: Enemy AI Phase Step and Position Integration

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-2, Interactions (Mob Spawning / MobEventBus); `design/gdd/navigation-pathfinding.md` CR-NAV-11 (`GetCurrentVelocity`, XZ only)
**Requirement**: `TR-ai-002`, `TR-ai-007`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (Decision 1 phase order); ADR-014 (phase 3 order); ADR-010
**ADR Decision Summary**: Tick order is sync (1) → drain (2) → game systems read velocity (3) → integrate positions (4). Only `ZoneNavigationService` calls `Tick` / `SyncAgentPositions`. Phase 3 systems run in the order the composition lists.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`desiredVelocity` timing at 20 Hz, ADR-002)
**Engine Notes**: Use `GetCurrentVelocity` XZ only; `desiredVelocity.y` must not feed horizontal integration.

**Control Manifest Rules (this layer)**:
- Required: no module outside `ZoneNavigationService` calls `Navigation.Tick()` / `SyncAgentPositions()` — architecture invariant
- Required: `INavigationProvider` methods only in phase 3

---

## Acceptance Criteria

- [ ] `EnemyAISystem : IZoneSimulationStep` owns one `MobController` per live mob: created on `MobEventBus.OnMobSpawned` (after Story 008's seeding), removed at despawn. Subscribes at construction, unsubscribes on `Dispose`.
- [ ] `Run(tick)` ticks controllers in mob roster order (deterministic); an exception in one controller is logged and does not stop the others (pattern: `ZoneSimulationStep`).
- [ ] **Position integration (phase 4 for mobs)**: for moving states (Pursuing, Returning), committed position += `GetCurrentVelocity` (XZ) × tick seconds (0.05); Y kept from the committed position (readiness: confirm Y source — NavMesh sample or agent `nextPosition`); stopped states do not move. The new position is written to the store so the next tick's phase 1 sync sees it.
- [ ] Integration test (EditMode, fake provider returning a fixed velocity): a Pursuing mob moves `speed × 0.05` per tick toward the target; a WindingUp mob does not move.
- [ ] The player reader used by controllers (players in the zone in pool order, position, HP) is implemented over the zone pool, Movement's committed positions and Character Stats.
- [ ] Zero per-tick allocation in `Run` with 150 controllers (checked in Story 014).

---

## Implementation Notes

- Readiness: decide whether integration happens inside `EnemyAISystem.Run` after all controllers tick, or in a separate step placed after Movement — ADR-002 phase 4 is "after all phase 3 systems". Record the decision.
- Mob Spawning does not exist yet: tests raise `MobEventBus.RaiseMobSpawned` directly.

---

## Out of Scope

- Story 013: adding the step to the composition

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_PhaseStep_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006, Story 008
- Unlocks: Stories 010, 011, 012
