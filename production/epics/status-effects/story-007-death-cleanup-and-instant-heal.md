# Story 007: Death Cleanup and Instant Heal

> **Epic**: Status Effects / Buffs
> **Status**: Complete — 2026-10-28 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28: NEEDS WORK (5 gaps) → fixed
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-3 (`ApplyInstantHeal`), CR-SE-10 (incl. mid-pass death and respawn), States (PendingRemoval), EC-SE-2, EC-SE-3; AC-SE-16, 30 (AC-SE-17 is Story 002). `design/gdd/death-and-respawn.md` — CR-DR-4, CR-DR-11a (calls `OnEntityRespawned`).
**Requirement**: `TR-se-005` (active)

**ADR Governing Implementation**: ADR-010 (Event/Messaging)
**ADR Decision Summary**: subscribe to `CharacterStats.OnEntityDied` in the constructor/`Initialize`, unsubscribe in `Dispose`; no stat writes inside a handler.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: subscribe in constructor/`Initialize()`, unsubscribe in `Dispose()` (server) — ADR-010
- Required: no write to Character Stats inside its event handlers (Character Stats Rule 8; `IsFiringAndAssert`)

---

## Acceptance Criteria

- [x] **Subscription seam**: new `IEntityDeathSource { void Subscribe(CharacterStats.EntityDiedHandler handler); void Unsubscribe(CharacterStats.EntityDiedHandler handler); }` in `src/ServerLogic/StatusEffects/`, production adapter `CharacterStatsDeathSource` forwarding to `CharacterStats.Subscribe` / `Unsubscribe`. The seam is a required 4th constructor parameter of `StatusEffectsService` (all existing call sites updated; tests pass a fake source that can raise a death). The constructor subscribes once; the service implements `IDisposable`; `Dispose()` unsubscribes and is idempotent (a second call does nothing). After `Dispose` a death changes nothing.
- [x] **AC-SE-16**: buff with `DurationTicks 100`; death raised for the entity → (1) zero `RemoveBuffModifier` calls during the handler; (2) the slot's dead flag is set — this flag **is** the PendingRemoval marker for all of the entity's rows (no per-row state field), readable through internal `IsEntityDead(EntityID)`; the rows stay readable (`GetActiveEffects`, `TryGetActiveEffect`) until cleanup. Next `OnServerTick()` → (3) `RemoveBuffModifier` once per stat per effect; (4) rows freed, occupancy 0. Pending rows get no decrement and no regen on that tick. The Story 006 clamps run as for any expiry: the HP clamp is a no-op at HP 0 (`ApplyDamage(0)` returns early, no event); the MP clamp lowers MP to the reduced MaxMP.
- [x] **Death of an entity with no effects**: sets the dead flag only; no calls.
- [x] **Mid-pass death**: `OnServerTick` copies the dead flags at pass start (preallocated `bool[MAX_ENTITY_SLOTS]`, like `_tickStartRowCounts`). Only slots dead at pass start are cleaned in that pass. A slot whose death fires during the pass (e.g. the Story 006 HP clamp after a MaxHP buff expires drives HP to 0): its remaining rows are not processed in that pass (no decrement, no regen), and if the pass has not reached it yet it is skipped; it is cleaned on the next tick. Earlier slots are unaffected.
- [x] **Dead flag lifetime**: stays set after the cleanup tick until either the new public `OnEntityRespawned(EntityID)` (Death & Respawn calls it at T-3, CR-DR-11a) or Story 008's slot cleanup clears it. `OnEntityRespawned` on an alive or unknown entity does nothing and never throws. While set, `ApplyEffect` and `ApplyInstantHeal` reject with `InvalidTarget`; after `OnEntityRespawned` both are accepted again.
- [x] **AC-SE-30**: `ApplyInstantHeal(entity, 100f)` → `ApplyRegen(entity, 100f)` exactly once (through `IStatResourceSink`); no row; occupancy unchanged; `Success`. `ApplyInstantHeal(entity, 0f)` → `InvalidAmount` (also negative and NaN). On a dead entity → `InvalidTarget`. Unknown entity → `InvalidTarget`.
- [x] Death cleanup runs inside the same transaction bracket as the rest of the tick pass (Story 006).

---

## Implementation Notes

- `CharacterStats` has no `OnEntityDied` C# event: it exposes `Subscribe(EntityDiedHandler)` / `Unsubscribe(EntityDiedHandler)` with a fixed 16-slot array; on overflow it logs an error and returns (one more subscriber fits). Keep the handler as a field (one delegate instance) so `Unsubscribe` matches by reference.
- The handler only resolves the slot and sets `_dead[slot]` (reuse `MarkEntityDead`); it must not call any `CharacterStats` method that writes. It can run inside the tick pass (the death subscriber of a Story 006 clamp) — the loop checks `_dead[slot]` after each row and stops processing that slot.
- Cleanup reuses the Story 005/006 expiry procedure (`RemoveModifiers`, `RemoveRow`, `ClampAfterMaxChange`) for each row of the slot.
- `ApplyInstantHeal` amount check: `!(amount > 0f)` catches NaN.
- `IsEntityDead` is `internal` (tests and Story 008); `OnEntityRespawned` is public (Death & Respawn, not yet implemented).

---

## Out of Scope

- Departure cleanup and slot release clearing the dead flag (Story 008).
- The Death & Respawn caller of `OnEntityRespawned` (Death & Respawn epic).
- Production wiring of `CharacterStatsDeathSource` into the zone composition (Story 009).

---

## QA Test Cases

- **AC-SE-16** — real `CharacterStats` with the entity killed via `ApplyDamage`; a recording decorator proves no remove during the handler; one tick later removes happen and rows are gone; no regen for the pending row.
- **AC-SE-30** — as listed, including NaN and negative.
- **Dispose** — after `Dispose`, killing an entity leaves its rows Active; a second `Dispose` does nothing.
- **Mid-pass death** — two rows on one entity, the first a MaxHP buff that expires and drives HP to 0: the second row is not decremented that pass; cleaned next tick. A later slot dying during an earlier slot's processing is skipped that pass, cleaned next tick.
- **Respawn** — dead entity, cleanup tick, `OnEntityRespawned` → `ApplyEffect` and `ApplyInstantHeal` succeed; `OnEntityRespawned` on an alive / unknown entity is a no-op.
- **MP clamp on a dead entity** — MaxMP buff, MP above base MaxMP, death → cleanup tick lowers MP to MaxMP; HP stays 0, no second death event.
- **Edges** — death and an `ApplyEffect` on the same tick (EC-SE-3) → `InvalidTarget`; two entities, only one dies.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_DeathAndInstantHeal_tests.cs` — must exist and pass

**Status**: [x] Created and passing — 27 cases, full EditMode run 4840 / 4711 passed / 0 failed / 129 skipped (2026-10-28)

---

## Dependencies

- Depends on: Stories 005, 006
- Unlocks: Story 008

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 7/7 passing, all covered by `tests/EditMode/StatusEffects/StatusEffects_DeathAndInstantHeal_tests.cs` (27 cases) plus the null-death-source test in `StatusEffects_Validation_tests.cs`
**Deviations** (advisory):
1. Two Story 005 tests in `StatusEffects_Tick_tests.cs` asserted "dead slots skipped until Story 007": `StatusEffects_Tick_DeadEntity_NoRegenNoDecrementNoRemoval` renamed to `..._NoRegenNoDecrementRowsCleanedOnce` (1 remove, 0 rows); `..._DeadEntityBeforeLiveOne_LiveEntityStillProcessed` asserts the dead entity's row is gone.
2. Real Character Stats cannot drive HP to 0 from a clamp (effective MaxHP floored at 1), so the mid-pass death tests kill the entity from the recorder's new `ClampHpHook` (same limitation as Story 006 note 6).
3. A respawn before the first tick after the death leaves the old rows (cleanup is keyed on the dead flag at pass start). Ruled out by Death & Respawn timing (buffs stripped before the respawn countdown ends); documented on `OnEntityRespawned`, pinned by `RespawnBeforeCleanupTick_RowsKeptAndTickNormally`.
4. `Dispose` hardening (code review W1/W2): unsubscribes before setting `_disposed` (a throwing `Unsubscribe` can be retried); the subscribed handler is a private `OnEntityDied` wrapper that ignores deaths after `Dispose`; `Dispose` must not be called from inside a Character Stats event handler.
5. Character Stats' 16-subscriber limit fails silently (log only) — logged as TD-086.
**Tech debt**: TD-086 (new)
**Test Evidence**: Logic — `tests/EditMode/StatusEffects/StatusEffects_DeathAndInstantHeal_tests.cs`
**Code Review**: Complete — 2026-10-28 APPROVED WITH SUGGESTIONS (unity-specialist, qa-tester) → all suggestions applied ("fix all"); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean)
