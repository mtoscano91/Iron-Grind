# Story 004: Aggro Scan and Pursuit

> **Epic**: Enemy AI
> **Status**: Ready
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

- [ ] **Scan cadence (CR-AI-3)**: a Dormant mob scans once every `AGGRO_SCAN_INTERVAL_TICKS` (scan phase per mob fixed at spawn, e.g. on spawn tick + k × interval); non-scan ticks read no player positions.
- [ ] **AC-AI-1**: player at `AggroRange + 0.1` → still Dormant after one interval; at `AggroRange − 0.1` → Pursuing with `TargetEntityID` = that player after the next scan. The comparison is XZ squared distance (`≤ AggroRange²`); Y ignored.
- [ ] **AC-AI-2**: two players in range → the one earlier in the entity list order wins; the other is not targeted.
- [ ] **AC-AI-22**: no player in range for `3 × AGGRO_SCAN_INTERVAL_TICKS` → Dormant, target unset, no navigation call.
- [ ] Players with HP ≤ 0 are skipped by the scan.
- [ ] **Dormant → Pursuing**: `SetDestination(target position)` once on entry; `Resume` is not called.
- [ ] **CR-AI-5 / AC-AI-5 (Pursuing)**: every Pursuing tick writes the mob's facing toward the target (Story 003 helper).
- [ ] **AC-AI-17**: target moves `PURSUIT_REPOSITION_THRESHOLD − 0.01` from the last issued destination → no `SetDestination`; `+ 0.01` → exactly one.
- [ ] **AC-AI-24**: `IsPathStale` true → `SetDestination` that tick regardless of distance; polled every Pursuing tick.
- [ ] **CR-AI-11 on-mesh guard**: the navigation seam exposes "agent registered / on mesh"; when false the call is skipped with one warning (no throw). *(Readiness: decide whether the guard reads a new provider query or relies on the provider's own unregistered-entity warnings — Navigation Story 004 already logs and returns safe defaults.)*
- [ ] Aggro never transfers while the target is valid (EC-AI-7: no cap on mobs per player).

---

## Implementation Notes

- The player list and positions come from an injected reader (players in the zone in pool order + position + HP); Story 008/009 provide the production reader.
- `GetCurrentVelocity` is not read here — position integration is Story 009.

---

## Out of Scope

- Story 005: Pursuing → WindingUp
- Story 006: leash / invalidation exits from Pursuing

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_AggroPursuit_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 005, Story 006
