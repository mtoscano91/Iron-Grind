# Story 004: Teardown — CancelAllSpawnTimers

> **Epic**: Mob Spawning
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 2 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-4 transition 8, CR-MS-5 (bus lifetime: clear the `MobDied` subscription when cancel returns), CR-MS-8, Edge Cases (cancel during Dying/Respawning; respawn while Draining); `design/gdd/zone-instancing.md` CR-ZI-12 step 1
**Requirement**: `TR-ms-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Teardown is a Tier 1 direct call on the injected `IZoneMobSpawnTimers` (Zone Instancing teardown step 1); event subscriptions are removed by the subscriber with the same named method.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: named-method subscriptions, removed on teardown — ADR-010
- Forbidden: `async` in tick code — ADR-011

---

## Acceptance Criteria

- [x] **AC-1 Interface**: the service implements `IZoneMobSpawnTimers` (`src/ServerLogic/ZoneInstancing/IZoneMobSpawnTimers.cs`).
- [x] **AC-2 Cancel (AC-MS-9)**: with points in Live, Dying, Respawning and Deferred, `CancelAllSpawnTimers(zone)` → every point Cancelled, every `TimerTick` 0, every `CurrentEntityID` `EntityID.Invalid` (a Cancelled point owns no mob; Zone Instancing clears slots at CR-ZI-12 step 7), deferred list empty; afterwards `Tick`, `PopulateOnActive` and a raised `MobDied` cause zero `TryAddMob`, `RemoveMob`, `GetMobCount` calls, zero `MobSpawned` and zero log lines (verified by call log and `LogAssert.NoUnexpectedReceived`). The cancelled check runs first in `Tick` and `PopulateOnActive`, so neither logs after cancel (the "PopulateOnActive called twice" warning does not fire).
- [x] **AC-3 No RemoveMob in teardown (AC-MS-14)**: Dying and Respawning points get no `RemoveMob` during or after cancel (Zone Instancing clears mob slots at CR-ZI-12 step 7).
- [x] **AC-4 Unsubscribe (CR-MS-5)**: after cancel returns, the service is no longer an `OnMobDied` subscriber (other subscribers still receive the raise).
- [x] **AC-5 Idempotent / foreign**: a second cancel for the service's zone is a no-op: no log, no exception. A foreign ZoneID logs exactly one warning naming both zones and changes nothing: point states, `TimerTick`s, `CurrentEntityID`s and the deferred list unchanged, `MobDied` subscription kept (a later `MobDied` for a Live point still moves it to Dying).
- [x] **AC-6 Uninitialized**: cancel before `PopulateOnActive` → all points Cancelled; a later `PopulateOnActive` makes no roster calls and logs nothing.
- [x] **AC-7 Cancel during Tick**: if `CancelAllSpawnTimers` runs re-entrantly inside `Tick` (from a `MobSpawned` subscriber), the rest of that `Tick` (remaining array pass, retry counter, deferred poll) makes no roster calls and raises no `MobSpawned`; the service checks its cancelled flag after each spawn.

---

## Implementation Notes

- `ZoneCloseSteps` already calls `_timers.CancelAllSpawnTimers(_zone)` at step 1; `LoggingZoneMobSpawnTimers` is the stand-in Story 006 replaces.
- A `Cancelled` flag on the service (not only per point) makes the post-cancel guards cheap.
- The ADR-010 unsubscribe (`_bus.OnMobDied -= HandleMobDied`) happens inside `CancelAllSpawnTimers`, only for the service's own zone.
- **Performance**: cancel is O(points) and runs once per zone teardown; the post-cancel guards in `Tick` / `PopulateOnActive` are allocation-free early returns. No tick-budget impact.

---

## Out of Scope

- Story 006: replacing `LoggingZoneMobSpawnTimers` in `ZoneInstancingWiring` and the composition-level teardown test
- An `IDisposable` / `Dispose` on the service (teardown goes through `CancelAllSpawnTimers` only)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refined at `/story-readiness` 2026-11-16.*

1. **Mixed states (AC-2, AC-3)**: one point each Live / Dying / Respawning / Deferred → cancel → all Cancelled, TimerTick 0, CurrentEntityID Invalid, deferred count 0, 0 `RemoveMob` in the log.
2. **Silence after cancel (AC-2)**: then 100 `Tick`s, `PopulateOnActive`, `RaiseMobDied` for the former Live point's id → call log has no entries after the cancel marker; 0 `MobSpawned`; `LogAssert.NoUnexpectedReceived` (no "called twice" warning).
3. **Unsubscribe (AC-4)**: a second counting `OnMobDied` subscriber still gets 1 call; the service's handler gets none (states unchanged, no log).
4. **Twice / foreign (AC-5)**: second cancel → no log, no exception. Fresh service, foreign zone cancel → exactly one warning; states, TimerTicks, entity ids and deferred list unchanged; a following `MobDied` for a Live point's id moves it to Dying.
5. **Before fill (AC-6)**: cancel then `PopulateOnActive` → 0 roster calls, no log.
6. **Cancel inside Tick (AC-7)**: two Respawning points due the same tick; a `MobSpawned` subscriber calls cancel on the first spawn → exactly 1 `TryAddMob` in the log, 1 `MobSpawned`, both points Cancelled afterwards, no deferred poll calls.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_Teardown_tests.cs` — must exist and pass

**Status**: [x] Created — 13 tests, passing

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 006

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 7/7 passing (none deferred)
**Deviations**: Advisory: the AC-4 unsubscribe check reads `MobEventBus._diedHandlers` by reflection (a public subscriber-count hook would touch a file outside this story).
**Test Evidence**: Logic: `tests/EditMode/MobSpawning/MobSpawning_Teardown_tests.cs`, 13/13; batch EditMode `MobSpawning|EnemyAI|ZoneInstancing|AssemblyBoundary` 1252/1252 (end 19:04:39Z, 0 `error CS`)
**Code Review**: Complete (`/code-review` 2026-11-16: APPROVED WITH SUGGESTIONS, suggestions 1–6 applied)
