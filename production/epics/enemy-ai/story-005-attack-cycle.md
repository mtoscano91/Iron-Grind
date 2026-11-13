# Story 005: Attack Cycle — Wind-Up, Execution, Recovery, Cooldown

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-4, CR-AI-6, CR-AI-8, CR-AI-11 (Stop / Resume), F-AI-1, EC-AI-3, EC-AI-8, AC-AI-3, AC-AI-4, AC-AI-5, AC-AI-6, AC-AI-18, AC-AI-20a, AC-AI-20b, AC-AI-25
**Requirement**: `TR-ai-004`, `TR-ai-005`, `TR-ai-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine (Decision 1)
**ADR Decision Summary**: `Stop` on WindingUp entry keeps the path; on Pursuing re-entry after WindingUp/Recovering call `Resume` first, then poll `IsPathStale` and `SetDestination` only if stale.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-003 consumer; fake provider in tests)
**Engine Notes**: none beyond Story 004.

**Control Manifest Rules (this layer)**:
- Required: on Pursuing re-entry after WindingUp/Recovering: `Resume(entityId)` then `IsPathStale()` → `SetDestination` if stale — ADR-003

---

## Acceptance Criteria

- [ ] **Pursuing → WindingUp (CR-AI-6)**: target within `AttackRange` (XZ) AND (`!hasAttackedOnce` OR `currentTick − lastAttackExecutionTick ≥ AttackCooldownTicks`).
- [ ] **AC-AI-25**: first attack has no cooldown gate; `lastAttackExecutionTick` is never `int.MinValue` (unset until the first execution).
- [ ] **AC-AI-3**: with `hasAttackedOnce` true and `lastAttackExecutionTick = T`, no WindingUp before `T + AttackCooldownTicks`; WindingUp on that tick.
- [ ] **WindingUp entry**: `windUpTicksRemaining = AttackWindUpTicks`; `capturedForward` = `FacingToForward(current FacingAngle)` (CR-AI-4); **AC-AI-18**: `Stop` called exactly once on the entry tick.
- [ ] **AC-AI-4**: facing changed by 90° on T+1 → the `CheckHit` call on AttackExecution receives the T=0 vector.
- [ ] **AC-AI-5**: facing does not change during WindingUp, AttackExecution or Recovering.
- [ ] **AC-AI-6**: `windUpTicksRemaining = 1` → AttackExecution for exactly one tick, Recovering the next tick.
- [ ] **AttackExecution**: target valid → `CheckHit(mobPos, targetPos, AttackRange, capturedForward)` through an injected hit seam; the result goes to an injected "mob hit applier" seam (Story 010 makes it real); `hasAttackedOnce = true`, `lastAttackExecutionTick = currentTick` whether or not it hit.
- [ ] **Recovering**: `recoveryTicksRemaining = AttackRecoveryTicks`; each tick decrement then check ≤ 0 → exit in the same tick (EC-AI-8: Recovery 0 exits on the tick after AttackExecution). Exit: target valid and in range and cooldown elapsed → WindingUp; target valid and out of range → Pursuing via `Resume` then `IsPathStale` → `SetDestination` if stale; Returning branch is Story 006.
- [ ] **AC-AI-20a**: F-AI-1 = 22 for (12, 6, 10) and 18 for (12, 6, 4).
- [ ] **AC-AI-20b**: target held in range, two executions 22 ticks apart for (12, 6, 10).

---

## Implementation Notes

- The hit and damage seams are interfaces owned by Enemy AI (e.g. `IMobAttackResolver`); Story 010 implements them over `HitDetector` + `DamageCalculator`.
- `HitDetector.CheckHit(Vector3 attackerPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)` exists in `src/ServerLogic/HitDetection/HitDetector.cs`.

---

## Out of Scope

- Story 006: `pendingReturn` and target invalidation during the cycle
- Story 010: the damage itself

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_AttackCycle_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004
- Unlocks: Story 006, Story 010
