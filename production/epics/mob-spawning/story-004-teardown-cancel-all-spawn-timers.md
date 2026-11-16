# Story 004: Teardown — CancelAllSpawnTimers

> **Epic**: Mob Spawning
> **Status**: Ready
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

- [ ] **AC-1 Interface**: the service implements `IZoneMobSpawnTimers` (`src/ServerLogic/ZoneInstancing/IZoneMobSpawnTimers.cs`).
- [ ] **AC-2 Cancel (AC-MS-9)**: with points in Live, Dying, Respawning and Deferred, `CancelAllSpawnTimers(zone)` → every point Cancelled, every `TimerTick` 0, deferred list empty; afterwards `Tick`, `PopulateOnActive` and a raised `MobDied` cause zero `TryAddMob`, `RemoveMob`, `GetMobCount` calls and zero `MobSpawned` (verified by call log).
- [ ] **AC-3 No RemoveMob in teardown (AC-MS-14)**: Dying and Respawning points get no `RemoveMob` during or after cancel (Zone Instancing clears mob slots at CR-ZI-12 step 7).
- [ ] **AC-4 Unsubscribe (CR-MS-5)**: after cancel returns, the service is no longer an `OnMobDied` subscriber (other subscribers still receive the raise).
- [ ] **AC-5 Idempotent / foreign**: a second cancel is a no-op without error; a foreign ZoneID logs one warning and cancels nothing.
- [ ] **AC-6 Uninitialized**: cancel before `PopulateOnActive` → all points Cancelled; a later `PopulateOnActive` adds nothing.

---

## Implementation Notes

- `ZoneCloseSteps` already calls `_timers.CancelAllSpawnTimers(_zone)` at step 1; `LoggingZoneMobSpawnTimers` is the stand-in Story 006 replaces.
- A `Cancelled` flag on the service (not only per point) makes the post-cancel guards cheap.
- A `Dispose` that also unsubscribes is fine; it must be safe after cancel.

---

## Out of Scope

- Story 006: replacing `LoggingZoneMobSpawnTimers` in `ZoneInstancingWiring` and the composition-level teardown test

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`.*

1. **Mixed states (AC-2, AC-3)**: one point each Live / Dying / Respawning / Deferred → cancel → all Cancelled, TimerTick 0, deferred count 0, 0 `RemoveMob` in the log.
2. **Silence after cancel (AC-2)**: then 100 `Tick`s, `PopulateOnActive`, `RaiseMobDied` for the Live point's id → call log has no entries after the cancel marker; 0 `MobSpawned`.
3. **Unsubscribe (AC-4)**: a second counting `OnMobDied` subscriber still gets 1 call; the service's handler gets none.
4. **Twice / foreign (AC-5)**: second cancel → no log, no exception; foreign zone → one warning, states unchanged.
5. **Before fill (AC-6)**: cancel then `PopulateOnActive` → 0 roster calls.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_Teardown_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 006
