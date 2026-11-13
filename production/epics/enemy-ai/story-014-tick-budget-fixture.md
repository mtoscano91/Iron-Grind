# Story 014: 150-Mob Tick Budget Fixture (OQ-AI-4)

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — OQ-AI-4 (must be profiled before the first zone load test)
**Requirement**: `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002 (tick phases); ADR-004 (server tick target)
**ADR Decision Summary**: Server tick target < 30 ms at 50 players + 150 mobs (F-NET-9); Enemy AI runs in phase 3 against the real navigation service.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (real `NavMeshAgent` on the baked starting-zone mesh)
**Engine Notes**: Editor batch-mode numbers are a data point, not a device measurement (same caveat as Navigation Story 011).

**Control Manifest Rules (this layer)**:
- Guardrail: server tick < 30 ms at 50 players + 150 mobs — ADR-004

---

## Acceptance Criteria

- [ ] PlayMode fixture on the starting-zone NavMesh (pattern: `tests/PlayMode/Navigation/Navigation_DrainFixture_tests.cs`): 150 mobs spawned through `MobEventBus`, a set of fake players placed to put mobs in every state (Dormant, Pursuing, attack cycle, Returning).
- [ ] Runs N ticks through navigation + Enemy AI steps in production order; records per-tick `EnemyAISystem.Run` ms (max, p95; first tick separately) and GC allocation per tick.
- [ ] Asserts: no exception; 0 bytes allocated per tick after warm-up in `EnemyAISystem.Run`; Enemy AI max ms below a budget agreed at readiness (proposal: ≤ 2 ms Editor batch mode).
- [ ] Results logged with a `[EnemyAI014]` prefix and recorded in the story's Completion Notes; OQ-AI-4 in the GDD updated with the data point.

---

## Implementation Notes

- Reuse Navigation Story 011's setup helpers (mesh, pool roots, `captureDeltaTime`, teardown order).

---

## Out of Scope

- Device profiling (iOS / Linux server) — the shared infrastructure blocker

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/EnemyAI/EnemyAI_TickBudget_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 013
- Unlocks: None (last story)
