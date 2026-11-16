# Story 014: 150-Mob Tick Budget Fixture (OQ-AI-4)

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3–4 hours
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — OQ-AI-4 (must be profiled before the first zone load test)
**Requirement**: `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002 (NavMesh service execution contract); ADR-004 (server tick target); ADR-014 (phase 3 step order)
**ADR Decision Summary**: Server tick target < 30 ms at 50 players + 150 mobs (F-NET-9); Enemy AI runs in phase 3 against the real navigation service, after the navigation step and before mob position integration.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (real `NavMeshAgent` on the baked starting-zone mesh)
**Engine Notes**: Editor batch-mode numbers are a data point, not a device measurement (same caveat as Navigation Story 011).

**Control Manifest Rules (this layer)**:
- Guardrail: server tick < 30 ms at 50 players + 150 mobs — ADR-004

---

## Acceptance Criteria

- [x] **AC-1 (setup)**: PlayMode fixture on the starting-zone NavMesh, reusing Navigation Story 011's setup (`tests/PlayMode/Navigation/Navigation_DrainFixture_tests.cs`): mesh, pool roots, `Time.captureDeltaTime = 0.05f`, teardown order, `NavMesh.pathfindingIterationsPerFrame` pinned at 100. 150 mobs spawned through `MobEventBus.MobSpawned` into a real `MobRuntimeStore` and a real `MobDefinitionRegistry` holding one test `MobDefinition`. Mobs are split into four groups by fake player placement:
  - **A — Dormant**: no player within aggro range.
  - **B — Pursuing**: a player within aggro range but outside attack range.
  - **C — attack cycle**: a player within attack range (WindingUp → AttackExecution → Recovering).
  - **D — Returning**: a leash player in aggro range who is moved beyond leash range at measured tick 100; placed so the return to spawn fits inside the remaining 100 ticks.
- [x] **AC-2 (seams and loop)**: Real: navigation service, `EnemyAISystem`, `EnemyAIConfigValues.Defaults`, `NavigationConfigValues.ReturnSpeedCap`, `ZoneAggroCandidateSource` over fake player positions. Fake: stats reader (all players HP > 0), counting `IMobAttackResolver`, `ICombatService`, `ILootTableService`. Each tick, in production order: `NavigationPhaseStep.Run` → `EnemyAISystem.Run` → `MobPositionIntegrationStep.Run` → one frame stepped (`yield return null`). 20 warm-up ticks, then 200 measured ticks. Each of Dormant, Pursuing, WindingUp, AttackExecution, Recovering and Returning is observed on at least one measured tick.
- [x] **AC-3 (measurement and asserts)**: `EnemyAISystem.Run` wall-clock ms (Stopwatch) recorded for the first tick separately and as max + p95 over the measured ticks; allocation per tick measured with `GC.GetAllocatedBytesForCurrentThread()` around `Run` (existing pattern: `tests/EditMode/EnemyAI/EnemyAI_MobState_tests.cs`). Asserts: no exception and no `LogError`; **0 bytes** allocated on every measured tick (a failure means removing the allocation is in scope for this story); **max ≤ 2.0 ms** over the measured ticks (Editor batch mode).
- [x] **AC-4 (results)**: Results logged with a `[EnemyAI014]` prefix (first-tick ms, max, p95, bytes/tick, per-state tick counts), copied into this story's Completion Notes, and added as a data point under OQ-AI-4 in `design/gdd/enemy-ai.md` (OQ stays open — no device number yet).

---

## Implementation Notes

- Reuse Navigation Story 011's setup helpers (mesh, pool roots, `captureDeltaTime`, teardown order); share them rather than copy where practical.
- Pool `MaxMobs` must be ≥ 150; `EnemyAISystem` `capacity` = 150 so its lists, caches and map are pre-sized.
- Measure only `EnemyAISystem.Run` (navigation and integration step costs may be logged separately but are not asserted).
- New folder `tests/PlayMode/EnemyAI/`; the `IronGrind.PlayModeTests` assembly already references `IronGrind.ServerLogic`.

---

## Out of Scope

- Device profiling (iOS / Linux server) — the shared infrastructure blocker
- Real `MobAttackResolver` / hit detection / `CheckHit` cost — follow-up data point
- Building the full `ZoneServerComposition` in PlayMode

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-16.*

- **QA 1 — state coverage**: Given the four groups, when 200 measured ticks run, then each of Dormant, Pursuing, WindingUp, AttackExecution, Recovering and Returning is observed on ≥ 1 measured tick.
- **QA 2 — zero allocation**: For every measured tick, `GC.GetAllocatedBytesForCurrentThread()` delta around `EnemyAISystem.Run` == 0.
- **QA 3 — time budget**: Max `EnemyAISystem.Run` ms over measured ticks ≤ 2.0; first tick logged, not asserted.
- **QA 4 — attack seam**: Fake attack resolver call count > 0 for group C mobs and == 0 for groups A and B.
- **QA 5 — leash**: Group D mobs enter Returning after tick 100 and are Dormant at their spawn point by the last measured tick.
- **QA 6 — teardown**: After teardown, no pooled agents remain active and `Time.captureDeltaTime` is restored.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/EnemyAI/EnemyAI_TickBudget_tests.cs` — must exist and pass

**Status**: [x] Created — PlayMode `-testFilter "EnemyAI|Navigation"` 29/29 (batch mode 2026-11-16)

---

## Dependencies

- Depends on: Story 013 (Complete)
- Unlocks: None (last story)

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 4/4 passing
**Test Evidence**: Integration — `tests/PlayMode/EnemyAI/EnemyAI_TickBudget_tests.cs` (8 `[UnityTest]`), batch PlayMode `-testFilter "EnemyAI|Navigation"` 29/29 (XML end 2026-11-16 17:41:38Z, 0 `error CS`)
**Code Review**: Complete — `/code-review` 2026-11-16 CHANGES REQUIRED (0 blocking: QA 6 captureDeltaTime tautology, agent check read service bookkeeping, ignored TryGet returns, state counts pooled across groups, `EnsureRun` length) → user "fix all" → fixed. Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.

**Results** (`[EnemyAI014]`, Editor batch mode, not a server build): `EnemyAISystem.Run` first tick 0.596 ms; measured max 0.586 ms (measured tick 100, the group D leash break), p95 0.086 ms over 200 ticks; 0 bytes allocated on every measured tick; mob-ticks Dormant 10933 / Pursuing 12468 / WindingUp 4104 / AttackExecution 342 / Recovering 1786 / Returning 367 / Dead 0; missed lookups 0; resolver calls A 0 / B 0 / C 380 / D 0; group D 37/37 Dormant at the end, ≤ 0.48 m from spawn; `pathfindingIterationsPerFrame` 100.

**Fixture setup**: test definition AggroRange 10, AttackRange 2, LeashRange 20, MoveSpeed 0.4, WindUp 12, Recovery 6, Cooldown 10, EnragedChance 0; groups A 38 / B 37 / C 38 / D 37 at sites ≥ 25 m apart; B and D players 9 m from their site; D player moved to (200, 17.5) at measured tick 100.

**Deviations (advisory)**:
1. MoveSpeed 0.4 m/s is deliberately slow so group B never reaches attack range (QA 4); the measured cost covers pursuit, not closing in.
2. Not in the measured cost: real `MobAttackResolver` / `CheckHit` (out of scope by decision), target death / target-lost branches (players never die), pursuit reposition (players don't move mid-pursuit).
3. Setup copied from the Navigation drain fixture rather than shared (Navigation test files untouched).
4. Warnings are counted, not asserted; only Error / Exception / Assert log types fail the run.

**Tech debt**: none new.
