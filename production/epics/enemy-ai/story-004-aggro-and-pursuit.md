# Story 004: Aggro Scan and Pursuit

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-3, CR-AI-5, CR-AI-11 (Pursuing part), F-AI-2, EC-AI-3, EC-AI-6, EC-AI-7, AC-AI-1, AC-AI-2, AC-AI-5 (Pursuing part), AC-AI-17, AC-AI-22, AC-AI-24
**Requirement**: `TR-ai-003`, `TR-ai-004`, `TR-ai-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine (Decision 1, Enemy AI usage contract); ADR-002 (phase 3, async paths)
**ADR Decision Summary**: Enemy AI calls `INavigationProvider` only in phase 3. A Dormant re-aggro issues a fresh `SetDestination` — never `Resume`. `SetDestination` is asynchronous; no path exists the same tick.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (consumer of the ADR-002/003 `NavMeshAgent` contract; tests use a fake `INavigationProvider`)
**Engine Notes**: `IsPathStale` is O(1) and false while a path is pending (Navigation Story 004).

**Control Manifest Rules (this layer)**:
- Required: `INavigationProvider` methods only in phase 3 — ADR-003
- Forbidden: calling `Resume()` from a Dormant re-aggro — ADR-003

---

## Acceptance Criteria

- [ ] **Scan cadence (CR-AI-3, EC-AI-3)**: `_ticksUntilScan` is set to `AGGRO_SCAN_INTERVAL_TICKS` on every Dormant entry (including construction); each Dormant tick decrements it and scans when it reaches 0, then resets it to the interval. The first scan therefore fires exactly `AGGRO_SCAN_INTERVAL_TICKS` Dormant ticks after entry. Non-scan ticks make no `IAggroCandidateSource` call.
- [ ] **AC-AI-1**: player at `AggroRange + 0.1` → still Dormant after one interval; at `AggroRange − 0.1` → Pursuing with `TargetEntityID` = that player after the next scan. The comparison is XZ squared distance (`≤ AggroRange²`); Y ignored.
- [ ] **AC-AI-2**: two players in range → the one earlier in the entity list order wins; the other is not targeted.
- [ ] **AC-AI-22**: no player in range for `3 × AGGRO_SCAN_INTERVAL_TICKS` → Dormant, target unset, no navigation call.
- [ ] Players with HP ≤ 0 are skipped by the scan.
- [ ] **Dormant → Pursuing**: `SetDestination(target position)` once on entry; `Resume` is not called.
- [ ] **CR-AI-5 / AC-AI-5 (Pursuing)**: every Pursuing tick writes the mob's facing toward the target (Story 003 helper).
- [ ] **AC-AI-17**: target moves `PURSUIT_REPOSITION_THRESHOLD − 0.01` from the last issued destination → no `SetDestination`; `+ 0.01` → exactly one.
- [ ] **AC-AI-24**: `IsPathStale` true → `SetDestination` that tick regardless of distance; polled every Pursuing tick.
- [ ] **CR-AI-11 on-mesh guard — delegated to the provider (decided at readiness 2026-11-13)**: `MobController` adds no on-mesh / registered query and never touches `NavMeshAgent`. The provider owns the guard: off-mesh agents are warped back (EC-NAV-10), and calls for an unregistered entity log one warning and return a safe default (ADR-003 unregistered-entity table). Test: with a fake provider that treats the mob as unregistered, Pursuing ticks do not throw. **Known edge case (accepted):** a pool-exhausted mob sees `IsPathStale == true` every tick, so in Pursuing it calls `SetDestination` every tick, and the provider warns each time. Suppressing that would need an `IsRegistered` query, which is a Navigation-epic change and not in scope.
- [ ] Aggro never transfers while the target is valid (EC-AI-7: no cap on mobs per player).

---

## Implementation Notes

**Estimate**: 4 h

- **Player seam (new, in `src/ServerLogic/EnemyAI/`)**: `IAggroCandidateSource` with
  - `void CopyPlayers(List<(EntityID Id, Vector3 Position)> into)`: fills a list the caller owns (the caller clears it), in zone pool order (= "entity list order", AC-AI-2). The controller reuses one list, so there is no per-scan allocation.
  - `bool TryGetPlayerPosition(EntityID id, out Vector3 position)`: the target's position in Pursuing. If it returns false, Pursuing does nothing that tick (no nav call, no facing write). Handling the lost target is Story 006.
  - HP for the "HP ≤ 0 skipped" rule comes from the existing `IZoneEntityStatReader.GetCurrentHP`, not from the new seam. NaN HP counts as dead and is skipped, the same as Story 003.
  - Stories 008/009 provide the production adapter; tests use a fake.
- **Mob position**: `MobController.Position` (`Vector3`), initialised to `SpawnPoint`, with an `internal` setter so tests can place the mob (`src/ServerLogic/AssemblyInfo.cs` already grants `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")`). Scan distance and facing use `Position`. Story 009 owns integrating it.
- **Config injection**: the constructor gains an `EnemyAIConfigValues config` parameter. It is a `readonly struct`, so there is no null case. The constructor throws `ArgumentOutOfRangeException` for `AggroScanIntervalTicks < 1` or `PursuitRepositionThreshold ≤ 0`, which rejects `default` (code review 2026-11-13). It supplies `AggroScanIntervalTicks` and `PursuitRepositionThreshold`. Update the Story 003 test factory to match.
- **Reposition check**: keep `_lastIssuedDestination`. Call `SetDestination` when the XZ distance from the target to `_lastIssuedDestination` is greater than `PursuitRepositionThreshold` (strict `>`, compared squared), or when `IsPathStale` is true. Each call updates `_lastIssuedDestination`. Dormant → Pursuing entry issues the first `SetDestination` and sets it.
- **Order inside a Pursuing tick**: get the target position → write facing (`BearingToFacing(Position, target, FacingAngle)` → `WriteFacing`) → poll `IsPathStale` once → `SetDestination` if stale or past the threshold.
- `GetCurrentVelocity` is not read here; position integration is Story 009.

**Performance**: one XZ squared-distance check per player, per mob, on scan ticks only; one O(1) `IsPathStale` per Pursuing tick; no heap allocation per tick (the reused list, value-tuple elements). No measurable frame impact expected at 150 mobs.

---

## Out of Scope

- Story 005: Pursuing → WindingUp, including EC-AI-3's "WindingUp on the same tick as aggro" half
- Story 006: leash / invalidation exits from Pursuing (target lost, dead or disconnected)
- Story 009: integrating mob position and the production `IAggroCandidateSource` adapter
- An `IsRegistered` / on-mesh query on `INavigationProvider` (see the CR-AI-11 AC)

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-13. Fakes: `FakeNavigationProvider` (records calls; settable `IsPathStale`), `FakeAggroCandidateSource` (ordered player list + positions), `FakeStatReader` (HP per entity).*

1. **Cadence**: interval N. Tick the mob N−1 Dormant ticks with a player in range → still Dormant, 0 `CopyPlayers` calls; tick N → 1 call, Pursuing.
2. **AC-AI-1 boundary**: player at XZ distance `AggroRange + 0.1` → Dormant after N ticks; move to `AggroRange − 0.1` → Pursuing after the next N ticks, `TargetEntityID` = that player. Exactly `AggroRange` → aggro (`≤`).
3. **Y ignored**: player at XZ distance `AggroRange − 0.1` with Y offset +50 → aggro.
4. **AC-AI-2 order**: players A then B, both in range (B nearer) → target A.
5. **Dead skipped**: A HP 0, B alive, both in range → target B. A HP NaN → skipped. All in range dead → stays Dormant.
6. **AC-AI-22**: no player in range for 3N ticks → Dormant, `TargetEntityID` null, 0 nav calls of any kind.
7. **Entry nav**: the aggro tick → exactly 1 `SetDestination(target position)`, 0 `Resume`.
8. **Facing**: target due +X of the mob → `FacingAngle` 900 on the first Pursuing tick; move the target to +Z → 0 on the next tick (written every tick).
9. **AC-AI-17**: after entry, move the target `threshold − 0.01` from the last destination → 0 new calls; move it to `threshold + 0.01` from the last destination → exactly 1. A later move under the threshold from the *new* destination → 0.
10. **AC-AI-24**: target moved < threshold, `IsPathStale` true → 1 `SetDestination` that tick; `IsPathStale` polled exactly once per Pursuing tick (also when not stale).
11. **Lost target position**: `TryGetPlayerPosition` false → no nav call and no facing change that tick; state stays Pursuing (Story 006 owns the exit).
12. **Unregistered provider**: the fake treats the mob as unregistered (stale true, ignores calls) → Pursuing ticks don't throw.
13. **No transfer (EC-AI-7)**: Pursuing A; a nearer player B enters range → target stays A; no player-list read in Pursuing.
14. **Re-entry**: force Dormant → `_ticksUntilScan` resets (the next scan comes N ticks later).
15. **Ctor**: null `candidates` → `ArgumentNullException`; `default(EnemyAIConfigValues)` → `ArgumentOutOfRangeException`; `Position == SpawnPoint` at construction.
16. *(Code review 2026-11-13)* **Non-finite target / positions**: a NaN target in Pursuing → no facing write, no poll, no `SetDestination` (even when stale); a NaN player position is skipped by the scan; a NaN mob `Position` never aggroes.
17. *(Code review 2026-11-13)* **Aggro tick**: facing is written toward the target on the aggro tick (CR-AI-5); exactly 1 nav call (the `SetDestination`).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_AggroPursuit_tests.cs` — must exist and pass

**Status**: [x] Created — 34 tests pass

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 005, Story 006

---

## Completion Notes
**Completed**: 2026-11-13
**Criteria**: 11/11 passing
**Deviations** (advisory): (1) `EnemyAIConfigValues` is a struct — no null case; the ctor rejects `default` (scan interval < 1 / threshold not > 0) with `ArgumentOutOfRangeException` (story text amended). (2) Accepted: a pool-exhausted mob calls `SetDestination` every Pursuing tick (provider warns each time); pinned by a test. (3) `Position` = spawn point, set only by tests until Story 009. (4) Unreadable or non-finite target position → Pursuing no-op; Story 006 turns it into the exit.
**Test Evidence**: Logic — `tests/EditMode/EnemyAI/EnemyAI_AggroPursuit_tests.cs`, 34/34; `EnemyAI_Controller_Tests` 33/33; batch EditMode 7565 / 7436 passed / 0 failed / 129 skipped.
**Code Review**: Complete (APPROVED WITH SUGGESTIONS; all 7 applied). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
