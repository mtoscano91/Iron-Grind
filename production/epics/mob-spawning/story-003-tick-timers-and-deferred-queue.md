# Story 003: Per-Tick Timers, Respawn Delay and Deferred Queue

> **Epic**: Mob Spawning
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3–4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-4 transitions 4–7, CR-MS-6 (deferred queue), CR-MS-9 / F-MS-1 (effective respawn delay), Edge Cases (`TryAddMob` returns Invalid; knob change mid-countdown)
**Requirement**: `TR-ms-005`, `TR-ms-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Phase 3 systems run as steps in an order listed explicitly by the composition root. This story exposes the step's `Tick`; Story 006 places it.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#).

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await` or any yield in tick code — ADR-011; class-typed event args — ADR-010
- Guardrail: CR-MS-3 — 150 int decrements per tick, sub-millisecond, no GC

---

## Acceptance Criteria

- [ ] **AC-1 Dying → Respawning (AC-MS-5)**: a Dying point at `TimerTick = 1` → on the next `Tick`: `RemoveMob(zone, E)` called exactly once, `CurrentEntityID = Invalid`, state Respawning, `TimerTick` = F-MS-1 delay; no `TryAddMob` on this transition.
- [ ] **AC-2 F-MS-1 (AC-MS-10)**: Common, no override, Common delay 60 → `TimerTick` exactly 60; Elite, delay 600 → 600; override 200 → 200 regardless of tier (the override path is tested through the service even though Story 001 refuses it in authored MVP tables). The knobs are fixed for the service's lifetime (the config is a `readonly MobSpawningConfigValues` taken by the constructor; no hot reload in MVP), so the GDD edge case "a later knob change does not alter an in-flight timer" holds by construction: the delay is committed to `TimerTick` once, at Dying → Respawning, and never recomputed. **Registry miss**: an override > 0 skips the registry lookup; otherwise, if `IMobDefinitionRegistry.GetDefinition` returns null (unknown or Invalid `MobTypeID`), log an error naming the `SpawnPointID` and `MobTypeID` and use the Common delay (the same fallback `RespawnDelayForTier` uses for an undefined tier). Story 005's zone-load validation makes this unreachable for validated tables.
- [ ] **AC-3 Respawning → Live (AC-MS-6)**: `TimerTick = 1`, count below cap → `TryAddMob(zone, type, position)` once; `CurrentEntityID` = returned id; Live; `MobSpawned` with the new id, type and authored position.
- [ ] **AC-4 Respawning → Deferred (AC-MS-7)**: `TimerTick = 1`, count = 150 → no `TryAddMob`, Deferred, on the deferred list, no `MobSpawned`, no second `RemoveMob`.
- [ ] **AC-5 Deferred retry (AC-MS-8, AC-MS-3 second half)**: with `SPAWN_DENSITY_RETRY_INTERVAL_TICKS = 20`, the deferred list is polled every 20th tick. The counter is owned by the service: it starts at 0 at construction, goes up by 1 on every `Tick` (including ticks before `PopulateOnActive`), and when it reaches `SpawnDensityRetryIntervalTicks` the poll runs and the counter resets to 0, so polls fall on the 20th, 40th, … `Tick` call. The poll runs after the array pass, so a point deferred during this tick's pass is filled the same tick if the poll fires; below cap → `TryAddMob`, Live, swap-removed from the list, `MobSpawned`; still at cap → stays Deferred until the next interval. Several deferred points: each poll fills as many as the count allows, checking `GetMobCount` before every `TryAddMob`.
- [ ] **AC-6 Invalid guard (AC-MS-13)**: `TryAddMob` returns `EntityID.Invalid` after a passing count check → Deferred (added once, not duplicated), `UnexpectedTryAddMobFailure` logged with the `SpawnPointID`, no `MobSpawned`. Applies to the respawn, the retry and the T-2 fill paths. (Story 002 AC-10 already makes the T-2 path go Deferred without the log; this story wraps it in the shared guard and adds the log.)
- [ ] **AC-7 Timer semantics**: Dying counts `DEATH_LINGER_TICKS`, Respawning counts the respawn delay; both decrement once per `Tick`; Live, Deferred and Uninitialized points are not decremented. (Cancelled points are not decremented either, but no path reaches Cancelled until Story 004; that part is verified there.)
- [ ] **AC-8 No allocation**: `Tick` allocates 0 bytes after warm-up, including ticks with a `RemoveMob`, a respawn and a deferred fill.

---

## Implementation Notes

- One `Tick(uint serverTick)` (or parameterless, developer's choice) iterates the whole array once, then the deferred poll on its interval. Keep the `GetMobCount` → `TryAddMob` pair adjacent (CR-MS-3).
- Swap-remove on the deferred `List<int>`: replace with the last element and `RemoveAt(Count − 1)`; iterate in a way that does not skip the swapped-in entry.
- The tick-count interpretation: a point that becomes Dying with `TimerTick = 30` on tick T reaches Respawning on tick T + 30 (30 decrements). Story 006 checks this lines up with Enemy AI's despawn (`currentTick − DeadEnteredTick ≥ DeathLingerTicks`).
- Log message keys exactly as the GDD names them: `UnexpectedTryAddMobFailure`.

---

## Out of Scope

- Story 004: Cancelled state and teardown
- Story 006: placing `Tick` in the phase 3 order and the same-tick check with Enemy AI despawn

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`.*

1. **Linger boundary (AC-1)**: Dying at 30 → after 29 ticks still Dying, no `RemoveMob`; tick 30 → one `RemoveMob`, Respawning, TimerTick 60 (Common).
2. **Delay table (AC-2)**: one point per tier, no override → 60 / 120 / 300 / 600; override 200 on an Elite point → 200.
3. **Delay committed at the transition (AC-2)**: two services on separate zones, one built with Common delay 60 and one with 100 → each point's `TimerTick` equals its own service's delay at Dying → Respawning, then counts down by exactly 1 per `Tick` (never recomputed); respawns after exactly 60 and 100 ticks respectively.
4. **Respawn (AC-3)**: Respawning at 1, count 10 → `TryAddMob` once, Live, `MobSpawned(zone, newId, type, authoredPosition)`.
5. **Respawn at cap (AC-4)**: count 150 → Deferred, list size 1, 0 `TryAddMob`, 0 `MobSpawned`, `RemoveMob` total still 1.
6. **Retry cadence (AC-5)**: fresh service, Deferred point after `PopulateOnActive` at cap; fake count drops to 149 before `Tick` call 5 → no `TryAddMob` on calls 1–19; filled on call 20; swap-removed (list empty). A second deferred point still at cap after call 20 is next tried on call 40, not 21.
7. **Retry still full (AC-5)**: count stays 150 → still Deferred after 3 polls, no `TryAddMob`.
8. **Multiple deferred (AC-5)**: 3 deferred, count 148 → 2 filled on one poll, 1 left; list contains the remaining point's index exactly once.
9. **Invalid guard (AC-6)**: fake returns Invalid with count 0 → Deferred once, `LogAssert` `UnexpectedTryAddMobFailure`, no `MobSpawned`; same on the retry path (point not duplicated in the list).
10. **Untouched states (AC-7)**: Live / Deferred / Uninitialized points keep TimerTick 0 across 100 ticks.
11. **Zero allocation (AC-8)**: warm-up, then measure a tick with a `RemoveMob`, one with a respawn, one with a deferred fill → delta 0 each.
12. **Registry miss (AC-2)**: fake registry returns null for the point's `MobTypeID`, no override → `LogAssert` error naming the `SpawnPointID` and `MobTypeID`; `TimerTick` = Common delay; no exception. Same point with override 200 → 200, no registry call, no log.
13. **Same-tick defer and fill (AC-5)**: a Respawning point's timer expires on `Tick` call 20; the fake's `GetMobCount` is scripted per call to return 150 during the array pass and 149 during the poll → the point goes Deferred in the pass, then is filled by the call-20 poll in the same tick (one `TryAddMob`, one `MobSpawned`, list empty).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_TickTimers_tests.cs` — must exist and pass

**Status**: [x] Created — 30/30 passing (2026-11-16)

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 004, Story 006

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 8/8 passing (all by `tests/EditMode/MobSpawning/MobSpawning_TickTimers_tests.cs`, 30/30; batch EditMode `MobSpawning|EnemyAI|ZoneInstancing|AssemblyBoundary` 1239/1239)
**Deviations** (advisory):
1. The constructor now also validates the config (`MobSpawningConfigValues.Validate()` → `ArgumentException`), from the code review. Story 006's composition root must pass a validated config.
2. The T-2 fill path now logs `UnexpectedTryAddMobFailure` through the shared guard (per AC-6); two Story 002 tests expect the log.
3. The deferred poll stops at the cap instead of checking the remaining points; equivalent, since the count only rises within one poll.
4. AC-7's Cancelled part is verified in Story 004 (no path reaches Cancelled yet).
**Test Evidence**: Logic: `tests/EditMode/MobSpawning/MobSpawning_TickTimers_tests.cs`
**Code Review**: Complete (`/code-review` 2026-11-16: CHANGES REQUIRED, 0 blocking; all fixes applied)
