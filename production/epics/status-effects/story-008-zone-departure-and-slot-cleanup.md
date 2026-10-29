# Story 008: Zone Departure and Slot Cleanup

> **Epic**: Status Effects / Buffs
> **Status**: Complete (2026-10-29) — written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28: NEEDS WORK (7 gaps) → fixed (user decisions: pool hook with set-once registration; batch-open failure → log and clean unbatched)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-11 (sequence contract, notification, `ClearEntity` rules), CR-SE-13, EC-SE-9, OQ-SE-2 (resolved 2026-10-28); AC-SE-21, 22, 23. `design/gdd/zone-instancing.md` — CR-ZI-4 (before-release hook), CR-ZI-12 steps 6–7 (teardown), CR-ZI-17 (entry-stall reclamation).
**Requirement**: `TR-se-004` (active) — the expiry procedure this cleanup reuses. CR-SE-11 has no dedicated TR entry.

**ADR Governing Implementation**: ADR-010 (direct call on an injected interface when there is one consumer and order matters); ADR-012 (assembly placement: everything here is in `IronGrind.ServerLogic`)
**ADR Decision Summary**: cleanup is a synchronous call made by the slot pool before the slot changes — not an event, because the order (cleanup before slot release) is the contract.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#; main thread. Logging uses `UnityEngine.Debug.LogError`, as Zone Instancing does.

**Control Manifest Rules (this layer)**:
- Required: if invocation order matters, use an explicit direct call (Tier 1), not an event — ADR-010
- Required: zone slot pool listeners must not throw (catch and log) — `IZoneSlotPoolListener` remarks

---

## Acceptance Criteria

- [x] **Release hook (OQ-SE-2, CR-ZI-4)**: new `IZoneSlotReleaseListener { void BeforeRelease(EntityID id); }` in `src/ServerLogic/ZoneInstancing/`. `ZoneSlotPool.SetReleaseListener(IZoneSlotReleaseListener)` registers it once (null → `ArgumentNullException`; a second call → `InvalidOperationException`); with no listener the pool behaves exactly as today (existing Zone Instancing tests unchanged). `ReleasePlayer` and `ReleaseMob` call `BeforeRelease(id)` after confirming the id is held and **before** any slot field, index or free-list changes, so `TryGetPlayerSlot` / the mob lookup still succeed during the call. Unknown or already-released ids → no call. The pool catches and logs a listener exception and completes the release anyway. This one hook covers all six release sites (`ZoneEntryReclamation`, `ZoneCloseSteps` steps 6 and 7, both `ZoneEntryAdmission` rollbacks, `ZoneMobRoster.RemoveMob`); no caller changes.
- [x] **`ClearEntity`**: new public `StatusEffectsService.ClearEntity(EntityID)`; the service implements `IZoneSlotReleaseListener` and `BeforeRelease` calls `ClearEntity`. For every row of the entity, Active or PendingRemoval, it runs the expiry procedure (remove modifiers, release occupancy, free the row, clamps — the Story 005/006 steps). Rows are freed with `FreeRowTickAware` so a call during the tick pass neither skips nor double-processes rows. Afterwards rows 0, occupancy 0, dead flag cleared. An unknown entity or one with no rows → no layer call; a second call → no-op.
- [x] **Stat batch**: the removals run inside a stat batch — the tick's if one is open (`_batchOpen`), otherwise its own (Begin / End, as an out-of-tick replacement). If its own `BeginStatBatch` throws (another Character Stats transaction is open): log an error and run the removals without a batch (events fire immediately); the rows are still cleared.
- [x] **Never throws**: any exception from the layer, a clamp, or `EndStatBatch` is caught and logged; rows, occupancy and the dead flag are still reset for the entity, and `ClearEntity` returns normally so the slot release proceeds.
- [x] **AC-SE-22**: 3 active buffs (one stat each) → `ClearEntity` → 3 `RemoveBuffModifier` calls, 3 rows freed, effective values back to base.
- [x] **AC-SE-23 / EC-SE-9**: a buff, `OnEntityDied`, then `ClearEntity` before the next tick → `RemoveBuffModifier` exactly once per stat (not twice); rows freed; dead flag cleared; the next `OnServerTick` makes no call for the entity.
- [x] **AC-SE-21 / CR-SE-13**: caster A buffs target B; `ClearEntity(A)` → B's row unchanged (caster field still A) and `TicksRemaining` keeps decrementing on later ticks.
- [x] **Sequence contract (integration)**: real `ZoneSlotPool` with the real service registered as release listener, plus a test-local `IEntitySlotIndex` over the pool (players: player slot; mobs: `MAX_PLAYERS_PER_ZONE` + mob slot, the OWL index — the production resolver is Story 009). (1) `ReleasePlayer` of a buffed player → its rows are cleared and a recording wrapper proves `BeforeRelease` ran while `TryGetPlayerSlot` still succeeded; (2) a new player allocated into the freed slot starts with zero rows and is not dead; (3) the same for `ReleaseMob`.

---

## Implementation Notes

- From Story 002 code review (2026-10-28): slot cleanup resets the slot's rows, `_rowCounts`, `_occupiedSlots` and `_dead` flag. Reuse `ExpireRow`'s steps per row (Story 007) but free through `FreeRowTickAware`; resolve the slot once at the start (the entity is still allocated during `BeforeRelease`).
- `ClearEntity` must leave the tables consistent even when a layer call throws part-way: on a caught exception, reset the slot directly (clear the remaining rows, `_rowCounts[slot] = 0`, `_occupiedSlots[slot] = 0`, `_dead[slot] = false`) after logging. Modifiers whose removal threw may remain in Character Stats — logged, accepted (the entity is leaving).
- Construction order (why set-once, not a constructor parameter): the service needs a slot index built over the pool, and the pool would need the service at construction. Story 009 builds the pool, then the service, then calls `SetReleaseListener(service)`.
- The pool's `BeforeRelease` call is a new step inside `ReleasePlayer` / `ReleaseMob` only; `IZoneSlotPoolListener` is unchanged.
- **Performance**: allocation-free. Zone teardown calls it up to 200 times (each a small batch); nothing is added to the tick path.

---

## Out of Scope

- Persisting buffs across zones (CR-SE-11: not at MVP); wire replication of buff state (TR-se-006).
- Production wiring — the pool-backed `IEntitySlotIndex`, `SetReleaseListener(service)` in the zone composition, and service disposal at teardown (Story 009).

---

## QA Test Cases

- **AC-SE-22 / 23 / 21** — as listed, real `CharacterStats` and a recording decorator.
- **Sequence** — real `ZoneSlotPool` + service as listener; the recording wrapper checks the slot is still allocated during `BeforeRelease`; after release a newly allocated entity in that slot has no rows; player and mob.
- **Pool hook** — no listener → release unchanged; unknown / already-released id → no `BeforeRelease`; a throwing listener → error logged (`LogAssert.Expect`), release completes; `SetReleaseListener` twice → `InvalidOperationException`; null → `ArgumentNullException`.
- **Robustness** — the layer throws during `ClearEntity` → rows, occupancy and dead flag reset, error logged, no exception out of `ClearEntity`; `BeginStatBatch` throws (transaction already open) → error logged, removals still made, rows cleared.
- **Edges** — `ClearEntity` on an entity with no rows and on an unknown entity (no calls); twice (second is a no-op); a MaxHP buff cleared → HP clamp runs.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_ZoneDeparture_tests.cs` — must exist and pass (pool-hook unit tests may go in the Zone Instancing test folder next to the existing `ZoneSlotPool` tests)

**Status**: [x] Created and passing — `tests/EditMode/StatusEffects/StatusEffects_ZoneDeparture_tests.cs` (23 tests: 18 `ClearEntity`, including 3 during a tick, and 5 pool sequence) and `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotReleaseListener_tests.cs` (9 tests). Full EditMode suite 2026-10-29: 4872 / 4743 passed / 0 failed / 129 skipped.

---

## Dependencies

- Depends on: Stories 006, 007 (Complete); Zone Instancing Stories 011, 013 (Complete)
- Unlocks: Story 009

---

## Completion Notes
**Completed**: 2026-10-29
**Criteria**: 8/8 passing (all covered by automated tests; none deferred)
**Deviations** (advisory, accepted):
- Outside the tick, a clamp exception inside `ClearEntity` is logged and cleared, not rethrown ("never throws"); inside the tick it is left for `OnServerTick` to rethrow, like any clamp in the pass.
- When its own `BeginStatBatch` fails, no `EndStatBatch` is called (asserted by a test).
- New internal test seam `InjectClearEntityDuringTick` (same pattern as `InjectApplyEffectDuringTick`) for the in-tick tests.
- `ZoneSlotPool` has no public mob-slot lookup: the test slot index records each mob's slot at allocation and maps it through `GetOwlIndexForMobSlot`. Story 009's production resolver needs the same, or a new pool lookup (carried in EPIC.md Next Step).
**Test Evidence**: Integration — `tests/EditMode/StatusEffects/StatusEffects_ZoneDeparture_tests.cs`, `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotReleaseListener_tests.cs`
**Code Review**: Complete 2026-10-29 (CHANGES REQUIRED: in-tick `ClearEntity` untested → fixed with 3 tests; `ClearEntity` split under 40 lines; dead-flag asserts; doc lines). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
