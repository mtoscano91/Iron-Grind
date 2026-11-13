# Story 005: Attack Cycle — Wind-Up, Execution, Recovery, Cooldown

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 5 h

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

## Tick Model (binding — readiness 2026-11-13)

The GDD does not say on which tick the hit lands. This model is the only one that satisfies F-AI-1 (22 / 18), AC-AI-6 and EC-AI-8 together:

1. **Tick E** (Pursuing, or the Recovering exit tick): attack condition met → enter WindingUp, `windUpTicksRemaining = W`, `Stop`, capture forward.
2. **Ticks E+1 …**: WindingUp decrements by 1. On the tick it reaches 0 (**E+W**) the mob enters AttackExecution **and the attack resolves on that tick** (attack tick **X = E+W**): resolver call (if target valid), `HasAttackedOnce = true`, `LastAttackExecutionTick = X`. State after tick X is AttackExecution.
3. **Tick X+1**: AttackExecution → Recovering with `recoveryTicksRemaining = R`, and the first Recovering decrement-then-check runs in that same tick. The mob leaves Recovering on tick **X + max(R, 1)**.
4. Check: W=12, R=6, C=10 → leaves Recovering at X+6 (cooldown 6 < 10) → Pursuing → WindingUp at X+10 → next attack X+22. C=4 → WindingUp straight from Recovering at X+6 → next attack X+18. R=0 → leaves on X+1 (EC-AI-8). W=1 → AttackExecution observed for exactly one tick, Recovering the next (AC-AI-6).

---

## Acceptance Criteria

- [ ] **Pursuing → WindingUp (CR-AI-6)**: target within `AttackRange` (XZ squared distance from `Position`, inclusive: `≤ AttackRange²`) AND (`!HasAttackedOnce` OR `currentTick − LastAttackExecutionTick ≥ AttackCooldownTicks`).
- [ ] **Pursuing tick order**: read target → write facing (CR-AI-5) → attack check → if it passes, enter WindingUp and skip the reposition check that tick; otherwise the Story 004 reposition logic runs. **EC-AI-3 (same tick)**: on the aggro tick, after Story 004's facing write and `SetDestination`, the same attack check runs; if the target is already in range the mob enters WindingUp on that tick (`Stop` after the `SetDestination`), and the capture is the real bearing.
- [ ] **AC-AI-25**: first attack has no cooldown gate; `LastAttackExecutionTick` is never `int.MinValue` (stays −1 until the first execution).
- [ ] **AC-AI-3**: with `HasAttackedOnce` true and `LastAttackExecutionTick = T`, no WindingUp before `T + AttackCooldownTicks`; WindingUp on that tick.
- [ ] **WindingUp entry** (one shared entry routine, used from Pursuing and Recovering): `windUpTicksRemaining = AttackWindUpTicks`; `CapturedForward = FacingToForward((short)FacingAngle)` (CR-AI-4); `Stop` called once on the entry tick. **AC-AI-18**: Pursuing → WindingUp calls `Stop` exactly once on the entry tick.
- [ ] **AC-AI-4**: facing changed by 90° on T+1 → the resolver receives the T=0 vector.
- [ ] **AC-AI-5**: facing does not change during WindingUp, AttackExecution or Recovering (exit ticks excepted, see Recovering).
- [ ] **AC-AI-6**: `windUpTicksRemaining = 1` → AttackExecution for exactly one tick, Recovering the next tick.
- [ ] **AttackExecution** (tick X, per the Tick Model): target valid → `IMobAttackResolver.ResolveAttack(EntityID, target, Position, targetPos, AttackRange, CapturedForward)`; target invalid → no call. Either way `HasAttackedOnce = true`, `LastAttackExecutionTick = X`.
- [ ] **Target valid (this story)**: `TargetEntityID` set AND `TryGetPlayerPosition` succeeds AND X and Z finite. HP/death of the target is Story 006.
- [ ] **Recovering**: decrement then check ≤ 0 → exit in the same tick (EC-AI-8). Exits:
  - target valid, in range, cooldown elapsed → write facing toward the target (honest bearing, CR-AI-4 intent), then WindingUp entry routine;
  - target valid, in range, cooldown **not** elapsed → Pursuing (re-entry below); Pursuing's gate starts the next WindingUp;
  - target valid, out of range → Pursuing (re-entry below);
  - target invalid → stay in Recovering (no-op each tick); Story 006 replaces this with Returning.
- [ ] **Pursuing re-entry from Recovering** (CR-AI-11, ADR-003): on the exit tick call `Resume`, write facing, then run the Story 004 reposition check once: `IsPathStale` called once; `SetDestination` (only through `IssueDestination`) if stale OR the target is more than `PursuitRepositionThreshold` from the stored destination. `SetDestination` is never called without `Resume` first after a `Stop`.
- [ ] **AC-AI-20a**: F-AI-1 = 22 for (12, 6, 10) and 18 for (12, 6, 4) (covered by `MobDefinition.EffectiveAttackIntervalTicks`, Story 001 test; reference it).
- [ ] **AC-AI-20b**: target held in range, two executions 22 ticks apart for (12, 6, 10), and 18 apart for (12, 6, 4).

---

## Implementation Notes

- **Attack seam**: new `IMobAttackResolver` (Enemy AI owned) with `void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)`. It **replaces** the `HitDetector` constructor parameter of `MobController` (and the `HitDetector` property); Story 010 implements it over `HitDetector.CheckHit` + `DamageCalculator`. Update the constructor calls in the Story 003 and 004 tests. Tests use a recording fake.
- `HitDetector.CheckHit(Vector3 attackerPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)` exists in `src/ServerLogic/HitDetection/HitDetector.cs` (Story 010 consumer).
- New private fields: `_windUpTicksRemaining`, `_recoveryTicksRemaining` (int). Cooldown math in `long` (`currentTick − LastAttackExecutionTick`).
- Fix the `HasAttackedOnce` doc comment: set on AttackExecution (CR-AI-6), not on attack start.
- Recovering → WindingUp also calls `Stop` (the shared entry routine); harmless, the agent is already stopped.
- *(Carried from Story 004 code review, 2026-11-13.)*
  - Every `SetDestination` goes through `MobController.IssueDestination`, which keeps `_lastIssuedDestination` current.
  - Re-entering Pursuing from Recovering uses `Resume` (CR-AI-11), so the stored reposition basis is the destination from before WindingUp. Test: after Recovering → Pursuing, the re-entry tick re-paths if the target is more than `PursuitRepositionThreshold` from that stored destination.
  - EC-AI-3: Story 004 writes facing on the aggro tick, so `CapturedForward` is the real bearing.
- **Deviation (advisory)**: facing written toward the target on the Recovering → WindingUp exit tick is not stated in the GDD; chosen for CR-AI-4's "honest bearing" (readiness 2026-11-13).

- *(Code review 2026-11-13.)* `Stop` discards a destination requested but not yet issued (`INavigationProvider.Stop`), so on the EC-AI-3 aggro tick (SetDestination then Stop) the request is lost. `EnterWindingUp` sets `_repathOnResume` when Stop lands on the same tick as an `IssueDestination`; the next reposition check re-paths. `HasAttackedOnce` / `LastAttackExecutionTick` are written before `ResolveAttack` (a throwing resolver cannot skip the cooldown). `ForceStateForTest` does not arm the attack-cycle counters (documented).

**Performance**: O(1) per mob per tick; no allocation (resolver takes value arguments; no LINQ, closures or boxing).

---

## Out of Scope

- Story 006: `pendingReturn`, leash/target checks during WindingUp, target invalidation (HP 0, lost target) during the cycle, Recovering → Returning, `HasAttackedOnce` reset on target change
- Story 009: movement updating `Position`
- Story 010: the damage itself (`IMobAttackResolver` implementation)
- Story 011: AC-AI-19 (Dead pre-empts AttackExecution)

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-13. File: `tests/EditMode/EnemyAI/EnemyAI_AttackCycle_tests.cs`.*

1. First attack, target in range, `HasAttackedOnce` false → WindingUp on that Pursuing tick; `LastAttackExecutionTick == -1` until the first execution (AC-AI-25).
2. AC-AI-3: `HasAttackedOnce` true, `LastAttackExecutionTick = T` → no WindingUp at T+C−1; WindingUp at T+C.
3. Range boundary: target at exactly `AttackRange` (XZ) → WindingUp; just beyond → stays Pursuing; Y ignored.
4. Pursuing → WindingUp: `Stop` called exactly once on the entry tick; no `SetDestination` that tick (AC-AI-18).
5. `CapturedForward == FacingToForward((short)FacingAngle)` at entry, facing written that tick before capture.
6. AC-AI-4: target moved 90° on T+1 → resolver receives the T=0 vector.
7. AC-AI-5: `FacingAngle` unchanged through WindingUp, AttackExecution and Recovering while the target moves.
8. AC-AI-6: `windUpTicksRemaining = 1` → AttackExecution for exactly one tick, Recovering the next.
9. Attack tick = WindingUp entry + W; resolver called once with (mob id, target id, Position, target pos, AttackRange, CapturedForward).
10. Target invalid at the attack tick (unreadable or non-finite) → no resolver call; `HasAttackedOnce` true, `LastAttackExecutionTick = X`.
11. EC-AI-8: R=0 → leaves Recovering on X+1.
12. R=6 → still Recovering at X+5, leaves at X+6.
13. Recovering exit, in range, cooldown not elapsed (12/6/10) → Pursuing with `Resume`; WindingUp at X+10.
14. Recovering exit, in range, cooldown elapsed (12/6/4) → WindingUp straight away, facing refreshed toward the target before capture, `Stop` called.
15. Recovering exit, out of range → `Resume` then `IsPathStale` once; `SetDestination` only if stale; order Resume before SetDestination.
16. Recovering exit, target moved more than the threshold from the stored destination → `SetDestination` on the re-entry tick, basis updated.
17. Recovering exit, target invalid → stays Recovering, no nav calls.
18. AC-AI-20b: target held in range, (12, 6, 10) → executions 22 apart; (12, 6, 4) → 18 apart.
19. EC-AI-3: target within `AttackRange` at the aggro scan → state WindingUp after that same tick; `SetDestination` then `Stop` once each; captured vector points at the target. Target in aggro range but outside `AttackRange` → Pursuing.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_AttackCycle_tests.cs` — must exist and pass

**Status**: [x] Created — 42 tests pass

---

## Dependencies

- Depends on: Story 004 (Complete)
- Unlocks: Story 006, Story 010

---

## Completion Notes
**Completed**: 2026-11-13
**Criteria**: 14/14 passing (AC-AI-20a via the Story 001 `EffectiveAttackIntervalTicks` test)
**Deviations** (advisory): (1) facing refreshed toward the target on Recovering → WindingUp (readiness decision; not in the GDD); (2) `IMobAttackResolver` replaces the `HitDetector` ctor parameter (Story 010 implements it); (3) invalid target at the Recovering exit holds the mob in Recovering until Story 006; (4) code review: `Stop` on the same tick as a `SetDestination` discards the request, so `_repathOnResume` forces the next re-path.
**Test Evidence**: Logic — `tests/EditMode/EnemyAI/EnemyAI_AttackCycle_tests.cs` 42/42; batch EditMode 7605 / 7476 passed / 0 failed / 129 skipped (2026-11-13 21:45:47Z).
**Code Review**: Complete (`/code-review` APPROVED WITH SUGGESTIONS, all 7 applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
