# Story 006: Leash, Target Invalidation and Returning

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 6 h

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-9, CR-AI-10, CR-AI-11 (Returning part), CR-AI-14, F-AI-4, EC-AI-2, EC-AI-5, AC-AI-9, AC-AI-10, AC-AI-11, AC-AI-12b, AC-AI-23, AC-AI-26
**Requirement**: `TR-ai-006`, `TR-ai-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine (Decision 1, Returning speed cap)
**ADR Decision Summary**: Returning entry: `SetSpeed(RETURN_SPEED_CAP)` then `SetDestination(spawn)`. Returning → Dormant: `Stop` then `SetSpeed(MoveSpeed)`. Returning → Pursuing (re-aggro): `SetSpeed(MoveSpeed)` then `SetDestination(target)`.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-003 consumer; fake provider in tests)
**Engine Notes**: `RETURN_SPEED_CAP` = 6.0 m/s (ADR-003; Story 002 config).

**Control Manifest Rules (this layer)**:
- Required: the three Returning call sequences above — ADR-003
- Forbidden: `Resume` from Dormant re-aggro — ADR-003

---

## Acceptance Criteria

- [ ] **Leash (CR-AI-9, F-AI-4)**: checked every tick in Pursuing, WindingUp, AttackExecution, Recovering, Returning; XZ squared distance `> LeashRange²`. Pursuing / Recovering → Returning immediately; WindingUp / AttackExecution → `pendingReturn = true`, cycle continues. *(Returning listed by the GDD: a leash breach while Returning keeps Returning — no re-entry.)*
- [ ] **AC-AI-9**: WindingUp with 3 left, mob moved to `LeashRange + 0.1` → stays WindingUp, `pendingReturn` set, AttackExecution fires, then Recovering, then Returning (not Pursuing / WindingUp).
- [ ] **Target died (CR-AI-10)**: outside the cycle → immediate re-scan of players within `AggroRange` (HP > 0): found → new target, same state; none → standard invalidation. Inside the cycle → `pendingReturn`, finish the cycle, re-scan-or-return on Recovering exit.
- [ ] **AC-AI-10 / AC-AI-12b**: target HP 0 during WindingUp → stays, `pendingReturn` set, AttackExecution skips `CheckHit`, Recovering, then re-scan: new target in range → `TargetEntityID` updated (state per Recovering exit rules); none → Returning.
- [ ] **AC-AI-26**: Pursuing on A, B in range, A HP 0 → next tick still Pursuing, target B, `SetDestination` toward B.
- [ ] **Standard invalidation** (target gone from the zone, or target outside the mob's `LeashRange` from spawn): Dormant / Pursuing / Recovering / Returning → Returning, target cleared; WindingUp / AttackExecution → `pendingReturn`, `CheckHit` skipped on execution. **AC-AI-23**: target removed while Pursuing → Returning next tick, target cleared.
- [ ] **Returning entry**: `SetSpeed(RETURN_SPEED_CAP)` then `SetDestination(spawn)` once; target cleared.
- [ ] **AC-AI-11 / EC-AI-5**: within `RETURN_ARRIVAL_THRESHOLD` (XZ squared) → Dormant, `Stop` then `SetSpeed(MoveSpeed)`, target unset, `pendingReturn` false; a mob already at spawn arrives on its first Returning tick.
- [ ] Returning mobs do not aggro (GDD has no Returning → Pursuing transition); the ADR-003 Returning → Pursuing sequence is therefore unused — record as a note, not code.

---

## Tick Model (binding, added at `/story-readiness` 2026-11-13)

**1. Check order.** Each tick in Pursuing, WindingUp, AttackExecution and Recovering, after the CR-AI-13 dead check:
1. **Leash** (F-AI-4): XZ squared distance from `Position` to `SpawnPoint` `> LeashRange²` (equal is not a breach).
2. **Target validity**, first match wins:
   - no `TargetEntityID`, `TryGetPlayerPosition` false, or non-finite X/Z → **standard invalidation**;
   - target XZ squared distance from `SpawnPoint` `> LeashRange²` → **standard invalidation**;
   - target HP ≤ 0 or NaN (`IZoneEntityStatReader.GetCurrentHP`) → **death** (re-scan).
3. The state's normal work (Stories 004/005).

A leash breach wins: when it fires, the target checks and the re-scan are skipped for that tick.

**2. Outside the attack cycle (Pursuing, Recovering).** Leash breach or standard invalidation → Returning on this tick. Death → re-scan immediately (see 4); found → continue this tick in the same state with the new target; none → Returning.
- In Recovering this runs every tick, including while the counter is still above 0 (CR-AI-9 "each tick"). On the exit tick it runs before the exit rules. This replaces Story 005's hold-in-Recovering placeholder.

**3. Inside the attack cycle (WindingUp, AttackExecution).** Leash breach or invalid target sets `PendingReturn = true`. The state never exits early.
- Hooks: at the start of `TickWindingUp` (before the countdown); again on tick X, before `ResolveAttack`; and in `TickAttackExecution` (tick X+1, before Recovering entry).
- On tick X with an invalid or dead target, `ResolveAttack` is NOT called. `HasAttackedOnce` / `LastAttackExecutionTick` are still written (Story 005 order).
- **Re-scan-only flag.** A private `_pendingReturnRescanOnly` is true when target death is the only cause of the pending return. Any leash or standard-invalidation cause sets it to false, and it stays false.

**4. Re-scan and target change.**
- The re-scan reuses `TryFindAggroTarget`: from `Position`, `AggroRange`, list order, HP > 0. The dead target is excluded by its HP.
- On reassignment:
  - set `TargetEntityID = new`, `HasAttackedOnce = false`, `LastAttackExecutionTick = -1`;
  - in Pursuing, run the rest of the tick against the new target: facing, the attack check, then a **forced** `IssueDestination(newPos, tick)`. The forced call skips the threshold test, because B can be within `PURSUIT_REPOSITION_THRESHOLD` of A's last destination and AC-AI-26 requires a `SetDestination` toward B. When the attack condition is met, WindingUp is entered instead and no `IssueDestination` is issued (Story 005 order).
- A re-scanned target outside the leash is caught by the next tick's standard invalidation. The re-scan does not filter for it.

**5. Recovering exit (remaining ≤ 0).** Rules in order:
1. Run the §1/§2 checks for this tick. Leash breach or standard invalidation → Returning. Death → re-scan; none found → Returning; found → `TargetEntityID` changes and step 2 runs with the new target.
2. Then the pending return:
   - `PendingReturn` set and `!_pendingReturnRescanOnly` → Returning.
   - `PendingReturn` set and `_pendingReturnRescanOnly`, and the current target is still dead → re-scan; none → Returning. Found → **clear `PendingReturn` and `_pendingReturnRescanOnly`**, then step 3. If step 1 already replaced the dead target, this is the found case.
   - **Deviation from CR-AI-14:** CR-AI-14 clears `pendingReturn` only on Dormant entry, but AC-AI-12b requires a successful re-scan to continue combat. Record this in the story's Completion Notes as a GDD note.
3. Story 005 exit rules: in range with the cooldown elapsed → WindingUp; otherwise → Pursuing via `Resume`.

**6. Returning.**
- **Entry** (shared `EnterReturning(tick)`):
  - `EnterState(Returning)`;
  - `SetSpeed(EntityID, _returnSpeedCap)`, then `IssueDestination(SpawnPoint, tick)`, exactly once;
  - `TargetEntityID = null`, `HasAttackedOnce = false`, `LastAttackExecutionTick = -1`;
  - no `Resume`, no `Stop`, no facing write.
  - `PendingReturn` is NOT cleared here (CR-AI-14). `_pendingReturnRescanOnly` is cleared with it on Dormant entry.
- **Per tick:**
  - the arrival check runs from the first `TickReturning`, not on the entry tick: XZ squared distance `Position`→`SpawnPoint` `≤ ReturnArrivalThreshold²` (inclusive) → `Stop`, then `SetSpeed(EntityID, Definition.MoveSpeed)`, then `EnterState(Dormant)`;
  - nothing else runs: no aggro, a leash breach keeps the mob Returning, and there are no target checks (the target is already cleared).
- **Dormant entry** additionally clears `TargetEntityID`, `HasAttackedOnce` / `LastAttackExecutionTick` and `_pendingReturnRescanOnly`.
- Standard invalidation "in Dormant" cannot happen (a Dormant mob has no target). It is not coded.

**7. RETURN_SPEED_CAP source.**
- New `MobController` ctor parameter `float returnSpeedCap`, placed after `config`.
- Finite and > 0, otherwise `ArgumentOutOfRangeException`.
- Navigation owns the value (`NavigationConfigValues.ReturnSpeedCap`, default 6.0). Story 013 wires it; tests pass `NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT`.
- 7 existing ctor calls in 3 test files gain the argument.

**8. Stopped agent → Returning.**
- Verified at readiness: `ZoneNavigationService.TryIssue` sets `IsStopped = false` before `agent.SetDestination` (`ZoneNavigationService.cs:310`). So Returning entry from WindingUp / AttackExecution / Recovering needs no `Resume`.
- Add a service-level regression test: Stop → SetDestination → drain → `IsStopped == false`.

**9. Expected changes to existing tests and docs.**
- Story 004 tests where an unreadable / non-finite / null target made Pursuing a no-op now expect Returning.
- Story 005's "invalid target at Recovering exit holds" test now expects Returning.
- Update the `MobController` class doc, and the `IssueDestination`, `TickPursuing`, `TickWindingUp` and `TickRecovering` docs.

---

## Implementation Notes

- "Disconnected" in the GDD = the target entity is no longer in the zone's player list (slot released). **Resolved at readiness:**
  - A ghost-session player stays in the player list and remains a valid target (Ghost Session GDD CR-GH-3: mobs MAY keep targeting a ghost).
  - When the ghost expires or dies, it is removed (or its HP reaches 0) and the next tick's standard invalidation / re-scan handles it. That covers CR-GH-7 item 2 ("within one AI tick").
  - Wiring `MobDeTargetingCoordinator`'s `issueMobDeTargetCommand` delegate belongs to Story 013.
  - CR-GH-7 says the mob goes to "idle unless another valid target in aggro range"; CR-AI-10 sends it to Returning. CR-AI-10 governs. Record the mismatch as a GDD note in Completion Notes.
- *(Carried from Story 004 code review, 2026-11-13.)*
  - Story 004 checks target HP only at the scan, so a target whose HP reaches 0 while being pursued is still pursued until this story adds the CR-AI-9 re-scan-or-return.
  - In Story 004, `TryGetPlayerPosition` returning false, or a non-finite position, makes Pursuing a no-op. This story turns it into the exit.
  - Pursuing with a null `TargetEntityID` is a no-op today.
  - Clear `TargetEntityID` on Dormant / Returning entry as the GDD requires.
  - Returning → Pursuing re-aggro must issue `SetDestination` through `MobController.IssueDestination`, so the reposition basis is the new target and not the spawn point.
- *(Carried from Story 005 code review, 2026-11-13.)*
  - Reset `HasAttackedOnce` (and `LastAttackExecutionTick` to −1) on target change / Dormant entry; otherwise a new target inherits the old cooldown. The wind-up and recovery counters are re-armed on entry and need no reset.
  - Hooks for leash / target-HP / `pendingReturn`: `TickWindingUp` (before the countdown), `TickAttackExecution`, and the Recovering "invalid target" exit, which today holds the mob in Recovering (Story 005 placeholder) and becomes → Returning here.
  - Returning can be entered from WindingUp or Recovering, where the agent is Stopped. Entry uses `IssueDestination(spawn, tick)` and no `Resume`. Confirmed at readiness: the drain clears `IsStopped` (Tick Model §8). Add the test.
  - `IssueDestination` now takes the tick; a `Stop` on the same tick as a `SetDestination` sets `_repathOnResume` (the request is discarded). Keep every SetDestination going through it.
- **Performance:**
  - The leash and target-validity checks are O(1) per mob per tick.
  - The O(players) re-scan runs only on a target-death event. It reuses `_scanBuffer`, so there are no per-tick allocations.
  - Returning issues one `SetSpeed` + one `SetDestination` on entry, and one `Stop` + one `SetSpeed` on arrival.
  - Within the Story 014 tick budget; nothing new to measure here.

---

## Out of Scope

- Story 007: any Enraged scaling of `MoveSpeed`. Arrival restores `Definition.MoveSpeed`; Story 007 changes that call if it scales speed.
- Story 009: integrating agent movement into `Position`. Until then, arrival and leash only happen in tests that set `Position` directly.
- Story 010: the real `IMobAttackResolver` (`CheckHit`, damage).
- Story 011: mob death (the Dead state mid-Returning or mid-cycle).
- Story 013: composition wiring (the `returnSpeedCap` value, `MobDeTargetingCoordinator`'s de-target delegate).
- The ADR-003 Returning → Pursuing re-aggro sequence: unused, because the GDD has no Returning → Pursuing transition.

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-13. Fake navigation provider records calls in order; fake candidates and stat reader; `Position` set directly.*

1. **Leash boundary row (Pursuing):** mob at exactly `LeashRange` from spawn (XZ) → stays Pursuing. At `LeashRange + 0.01` → Returning on that tick, target null. Y offset ignored.
2. **Leash in Recovering (counter > 0):** breach → Returning on that tick, without waiting for the counter.
3. **AC-AI-9:** WindingUp with 3 left, `Position` at `LeashRange + 0.1` → WindingUp for the remaining ticks, `PendingReturn` true, the resolver IS called on tick X (target valid), then Recovering, then Returning on Recovering exit. Never Pursuing / WindingUp.
4. **Leash breach during AttackExecution (tick X+1)** with R = 0 → `PendingReturn` set, Recovering, Returning on X+1.
5. **AC-AI-10 / AC-AI-12b, none found:** target HP 0 during WindingUp (3 left) → stays, `PendingReturn` true, resolver NOT called on tick X, `HasAttackedOnce` true and `LastAttackExecutionTick` = X, Recovering, no other player → Returning.
6. **AC-AI-12b, found, in range:** as 5, player B within AggroRange and AttackRange at Recovering exit → target B, `PendingReturn` false, cooldown reset (`HasAttackedOnce` false) → WindingUp on the exit tick.
7. **AC-AI-12b, found, out of attack range:** B within AggroRange but outside AttackRange → target B, Pursuing via `Resume`, `PendingReturn` false.
8. **Mixed causes:** target dies in WindingUp, and the leash is breached later in the same cycle → `_pendingReturnRescanOnly` false → Returning at Recovering exit even with B in range.
9. **Death, then leash restored:** target dies in WindingUp, and `Position` is within the leash at Recovering exit → only the re-scan logic applies (no leash Returning).
10. **AC-AI-26:** Pursuing on A, B in AggroRange, A HP 0 → that tick: still Pursuing, target B, the nav log's last call is `SetDestination(B's position)`, even when B is within `PURSUIT_REPOSITION_THRESHOLD` of A's last destination (forced issue). Facing toward B.
11. **Death re-scan in Pursuing with B in attack range** → target B, WindingUp the same tick, Stop issued, no SetDestination.
12. **Death in Pursuing, nobody in range** → Returning, target null.
13. **Death in Recovering (counter > 0), B found** → target B, still Recovering, counter unchanged, no nav calls.
14. **NaN target HP** is treated as death (row of 10/12).
15. **AC-AI-23:** Pursuing, target removed from the candidate list → next tick Returning, target null, nav log `[SetSpeed(cap), SetDestination(spawn)]`.
16. **Target outside the leash from spawn** (mob inside the leash) in Pursuing → Returning. Boundary row: target at exactly `LeashRange` → stays.
17. **Standard invalidation in WindingUp** (target removed) → `PendingReturn`, resolver not called, Returning at Recovering exit.
18. **Returning entry from a stopped agent** (Recovering exit after WindingUp's Stop): log order `SetSpeed(RETURN_SPEED_CAP)` then `SetDestination(spawn)`, no `Resume`, each exactly once. Plus the `ZoneNavigationService` test: Stop → SetDestination → drain → `IsStopped == false`.
19. **Returning per tick:** no further nav calls while away from spawn; a leash breach while Returning → stays Returning, no re-entry calls; a player in AggroRange → no aggro.
20. **AC-AI-11:** Returning, `Position` set within `ReturnArrivalThreshold` (and a row at exactly the threshold, inclusive) → Dormant, log `Stop` then `SetSpeed(MoveSpeed)`, target null, `PendingReturn` false, `HasAttackedOnce` false, `LastAttackExecutionTick` -1.
21. **EC-AI-5:** a mob already at spawn enters Returning → Dormant on its first Returning tick (not the entry tick).
22. **Cooldown reset on target change:** A attacked at tick T, A dies, B acquired by re-scan in Pursuing and in range at T+1 → WindingUp immediately (no cooldown wait).
23. **Ctor:** `returnSpeedCap` 0, negative, NaN or ∞ → `ArgumentOutOfRangeException`.
24. **Regression updates:** the Story 004 unreadable / non-finite / null-target Pursuing tests and the Story 005 invalid-target-at-Recovering-exit test now expect Returning.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_LeashInvalidation_tests.cs` — must exist and pass

**Status**: [x] Created — 40 tests pass

---

## Dependencies

- Depends on: Story 005 (Complete)
- Unlocks: Story 009

---

## Completion Notes
**Completed**: 2026-11-13
**Criteria**: 9/9 passing (all automated)
**Deviations** (advisory): (1) CR-AI-14: a successful re-scan at Recovering exit clears `PendingReturn` (GDD clears it on Dormant entry only; AC-AI-12b requires combat to continue) — GDD note; (2) Ghost Session CR-GH-7 says "idle unless another valid target", CR-AI-10 sends the mob to Returning — CR-AI-10 governs, GDD note; (3) re-scan timing: a death noted in the attack cycle is re-scanned only at Recovering exit (CR-AI-10), a death first seen in Recovering re-scans at once (Tick Model §2), and a pending return with a leash / invalidation cause goes to Returning at exit without a re-scan; (4) a target that died in the cycle but has HP > 0 again at Recovering exit is kept, with its cooldown state (not defined by the GDD); (5) TR-ai-007 / AC-AI-26: a target change sets `_repathOnResume`, so the next reposition check re-paths regardless of `PURSUIT_REPOSITION_THRESHOLD` (it still polls `IsPathStale` first); this also covers a Recovering swap followed by the Pursuing exit; (6) the leash is not evaluated in Returning (a breach keeps Returning, same outcome); (7) the Tick Model §8 service test already existed (`Navigation_TickDrain_tests.test_tick_drain_stop_then_set_destination_before_drain_issues_new_target`), none added. Carry-forward: non-finite `Position` handling noted in Story 009.
**Test Evidence**: Logic — `tests/EditMode/EnemyAI/EnemyAI_LeashInvalidation_tests.cs` 40/40; batch EditMode 7645 / 7516 passed / 0 failed / 129 skipped (2026-11-14 00:57:47Z).
**Code Review**: Complete (`/code-review` APPROVED WITH SUGGESTIONS, all applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
