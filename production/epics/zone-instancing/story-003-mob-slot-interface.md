# Story 003: Mob Slot Interface for Mob Spawning (CR-ZI-13)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 2 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-13, EC-ZI-10
**Requirement**: `TR-zi-013` *(epic placeholder)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Mob Spawning reaches Zone Instancing by direct calls on an injected interface; no event bus.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `UnityEngine.Vector3` is the position type (GDD Runtime Model).

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces between server systems — ADR-010
- Required: `IronGrind.ServerLogic` — ADR-012
- Guardrail: O(1) per call; no allocation per call

---

## Acceptance Criteria

- [ ] An interface (for example `IZoneMobRoster`) with `TryAddMob(ZoneID, MobTypeID, Vector3) : EntityID`, `RemoveMob(ZoneID, EntityID)`, `GetMobCount(ZoneID) : int`, implemented over Story 002's mob pool and shared EntityID counter. The mob slot keeps `MobTypeID` and position for the snapshot (Story 007).
- [ ] **AC-ZI-17**: with 0 mobs, 150 `TryAddMob` calls return distinct non-zero IDs and `GetMobCount` = 150; after 50 `RemoveMob`, `GetMobCount` = 100; 51 more `TryAddMob` calls … *(as the GDD states: each returns `EntityID(0)` and the count stays 100 — see open decision 1)*.
- [ ] **EC-ZI-10**: `TryAddMob` at cap returns `EntityID(0)` without retrying; a sustained cap-full condition logs `MobSlotExhausted` at most once per zone per minute (`60 × TICK_RATE_HZ` ticks, counted from the server tick passed in).
- [ ] `RemoveMob` with an unknown or already-removed `EntityID` is a no-op that logs a warning; a foreign `ZoneID` is rejected (log, no change).

---

## Implementation Notes

- The `Mob Spawning` → `CancelAllSpawnTimers(ZoneID)` direction belongs to Story 012 (teardown step 1).
- The pool tracks the current server tick for the once-per-minute log through the call (or an injected `IServerTickSource`), not wall-clock time.

**Open decisions for `/story-readiness`**

1. **AC-ZI-17 arithmetic.** After removing 50 of 150 there are 50 free slots, so 51 more `TryAddMob` calls give 50 successes and one `EntityID(0)`, ending at 150 — not "each returns `EntityID(0)` and the count stays 100". The GDD AC is internally inconsistent; readiness picks the intended assertion (likely: 50 succeed, the 51st returns `EntityID(0)`, count 150) and records the GDD correction.

---

## Out of Scope

- Mob Spawning itself (no epic yet); a test double drives the interface
- Story 012: `CancelAllSpawnTimers` at teardown

---

## QA Test Cases

- **AC-ZI-17** — per the assertion fixed at readiness.
- **Cap** — Given 150 mobs; When `TryAddMob`; Then `EntityID(0)`, count 150.
- **Log rate** — Given cap full; When `TryAddMob` is called every tick for 2401 ticks; Then `MobSlotExhausted` logged 3 times (ticks 0, 1200, 2400).
- **Unknown remove** — `RemoveMob` of an unknown id → count unchanged, warning logged.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_MobSlots_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 007 (mob entries in the snapshot); the Mob Spawning epic
