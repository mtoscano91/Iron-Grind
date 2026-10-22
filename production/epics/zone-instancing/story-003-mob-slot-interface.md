# Story 003: Mob Slot Interface for Mob Spawning (CR-ZI-13)

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-22) — written 2026-10-22; lean readiness 2026-10-22 (NEEDS WORK → fixed → Ready)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 3 hours (confirmed at `/story-readiness` 2026-10-22)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-13, EC-ZI-10, AC-ZI-17
**Requirement**: `TR-zi-013` *(epic placeholder — not in `tr-registry.yaml`; stays a placeholder until `/architecture-review`, as in Stories 001 and 002)*

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

- [ ] Interface `IZoneMobRoster` (the GDD's `IZoneInstancing` mob API, kept as its own small interface) with `TryAddMob(ZoneID, MobTypeID, Vector3) : EntityID`, `RemoveMob(ZoneID, EntityID)`, `GetMobCount(ZoneID) : int`, implemented by class `ZoneMobRoster` in `src/ServerLogic/ZoneInstancing/` over an injected `ZoneSlotPool` (Story 002: `TryAllocateMob` / `ReleaseMob`, shared EntityID counter). `ZoneSlotPool` is not changed. `MobTypeID` is `IronGrind.LootTableSystem.MobTypeID`.
- [ ] Each mob slot keeps its `MobTypeID` and **spawn** position in arrays inside `ZoneMobRoster`, indexed by mob slot and allocated once in the constructor; `TryGetMob(EntityID, out MobTypeID, out Vector3) : bool` reads them for Story 007. A mob's current position belongs to Enemy AI / Movement; Story 007 decides which one the snapshot uses.
- [ ] **AC-ZI-17** (corrected 2026-10-22): with 0 mobs, 150 `TryAddMob` calls return distinct non-zero IDs and `GetMobCount` = 150; after 50 `RemoveMob`, `GetMobCount` = 100; of 51 more `TryAddMob` calls, the first 50 return distinct new non-zero IDs and the 51st returns `EntityID(0)`; `GetMobCount` = 150.
- [ ] **EC-ZI-10**: `TryAddMob` at cap returns `EntityID(0)` without retrying. `MobSlotExhausted` is logged (warning) on the first cap-full failure, then again only on a cap-full failure at least `60 × TICK_RATE_HZ` = 1200 ticks after the last log (unsigned tick subtraction, wrap-safe), reading the tick from an injected `IServerTickSource`.
- [ ] A `TryAddMob` that fails because the EntityID counter is spent (pool not full) returns `EntityID(0)` and logs no `MobSlotExhausted` (Story 002's counter alert covers it).
- [ ] `RemoveMob` with an unknown or already-removed `EntityID` changes nothing and logs one warning per call.
- [ ] Foreign `ZoneID`: `TryAddMob` returns `EntityID(0)` (with the pool's warning); `RemoveMob` logs one warning per call and changes nothing; `GetMobCount` returns 0 and warns once per roster (Mob Spawning polls it).

---

## Implementation Notes

- The `Mob Spawning` → `CancelAllSpawnTimers(ZoneID)` direction belongs to Story 012 (teardown step 1).
- Constructor: `ZoneMobRoster(ZoneSlotPool pool, IServerTickSource ticks, uint exhaustedLogIntervalTicks = MOB_SLOT_EXHAUSTED_LOG_INTERVAL_TICKS)` with `public const uint MOB_SLOT_EXHAUSTED_LOG_INTERVAL_TICKS = 1200` (60 s × 20 Hz). The tick comes from `IServerTickSource`, never wall-clock time. To tell a full pool from a spent counter after a failed `TryAllocateMob`, compare `pool.OccupiedMobCount` with `pool.MaxMobs`.

**Decisions at `/story-readiness` 2026-10-22**

1. **AC-ZI-17 arithmetic.** After removing 50 of 150 there are 50 free slots, so the GDD's "each returns `EntityID(0)` and the count stays 100" cannot happen. Intended assertion: 50 succeed, the 51st returns `EntityID(0)`, count 150. GDD AC-ZI-17 corrected the same day.
2. **Names and storage.** `IZoneMobRoster` / `ZoneMobRoster` wrap Story 002's pool; type and spawn position live in the roster, not the pool.
3. **Tick source.** Injected `IServerTickSource` (as Story 002), because `TryAddMob` carries no tick.

---

## Out of Scope

- Mob Spawning itself (no epic yet); a test double drives the interface
- Story 012: `CancelAllSpawnTimers` at teardown

---

## QA Test Cases

- **AC-ZI-17** — Given 0 mobs; When 150 adds, 50 removes, 51 adds; Then 150 distinct non-zero IDs, count 100 after the removes, 50 new distinct non-zero IDs then one `EntityID(0)`, count 150.
- **Cap** — Given 150 mobs; When `TryAddMob`; Then `EntityID(0)`, count 150.
- **Log rate** — Given cap full; When `TryAddMob` is called every tick for 2401 ticks; Then `MobSlotExhausted` logged 3 times (ticks 0, 1200, 2400).
- **Unknown remove** — `RemoveMob` of an unknown id → count unchanged, warning logged.
- **Stored data** — `TryAddMob(zone, type, pos)` then `TryGetMob(id)` → same type and position; after `RemoveMob`, `TryGetMob` false.
- **Spent counter** — pool with `firstEntityId = uint.MaxValue`: first add succeeds, second returns `EntityID(0)` with the counter alert and no `MobSlotExhausted`.
- **Foreign zone** — `TryAddMob` → `EntityID(0)` + one warning; `RemoveMob` → one warning; `GetMobCount` twice → 0, one warning.

---

## Files

- New: `src/ServerLogic/ZoneInstancing/IZoneMobRoster.cs`, `ZoneMobRoster.cs`
- New: `tests/EditMode/ZoneInstancing/ZoneInstancing_MobSlots_tests.cs`

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_MobSlots_tests.cs` — must exist and pass.

**Status**: [x] Created — 25 tests, all passing (batch EditMode 2026-10-22)

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 007 (mob entries in the snapshot); the Mob Spawning epic

---

## Completion Notes
**Completed**: 2026-10-22
**Criteria**: 7/7 passing (none deferred)
**Deviations** (advisory, accepted):
- Extra behaviour: `exhaustedLogIntervalTicks` = 0 throws `ArgumentOutOfRangeException`; `RemoveMob` warns when `ZoneSlotPool.ReleaseMob` returns false and still drops the roster entry; a foreign-zone `TryAddMob` on a full pool logs only the pool's warning (no `MobSlotExhausted`).
- The roster keeps its own EntityID → slot map (the pool's is private), so it must be the pool's only mob allocator: a mob allocated directly on the pool is counted by `GetMobCount` but unknown to `RemoveMob` / `TryGetMob` (documented in the class remarks, pinned by a test).
- O(1) and allocation-free per call except on warning paths (the log message is formatted).
- `TryGetMob` is part of `IZoneMobRoster` (Story 007 reads it through the interface).
- `TR-zi-013` is still an epic placeholder (readiness decision).
**Notes for Story 007**: `TryGetMob` returns the **spawn** position; the snapshot's current mob position belongs to Enemy AI / Movement. **Notes for Story 014**: construct `ZoneMobRoster` over the same `ZoneSlotPool` and `IServerTickSource`, and give Mob Spawning only `IZoneMobRoster`.
**Test Evidence**: Logic — `tests/EditMode/ZoneInstancing/ZoneInstancing_MobSlots_tests.cs` (25 tests).
**Code Review**: Complete — `/code-review` 2026-10-22 APPROVED WITH SUGGESTIONS, all 8 suggestions applied (commit `3a1fd5c`). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
