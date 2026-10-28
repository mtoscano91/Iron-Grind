# Story 008: Zone Departure and Slot Cleanup

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean). **Open decision for `/story-readiness`**: the exact hook point (below).
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-11 (with its sequence contract), CR-SE-13, EC-SE-9, OQ-SE-2; AC-SE-21, 22, 23. `design/gdd/zone-instancing.md` — CR-ZI-14 exit, CR-ZI-17
**Requirement**: `TR-se-004` (placeholder — registry empty)

**ADR Governing Implementation**: ADR-010 (direct call on an injected interface when there is one consumer and order matters); ADR-012 (assembly placement)
**ADR Decision Summary**: cleanup is a synchronous call made by the zone's leave path before the slot is freed — not an event, because the order (cleanup before slot release) is the contract.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#; main thread.

**Control Manifest Rules (this layer)**:
- Required: if invocation order matters, use an explicit direct call (Tier 1), not an event — ADR-010
- Required: zone slot pool listeners must not throw (catch and log) — `IZoneSlotPoolListener` remarks

---

## Acceptance Criteria

- [ ] **OQ-SE-2 resolved**: the departure notification is a direct call `IStatusEffectsSlotCleanup.ClearEntity(EntityID)` made by Zone Instancing on every path that frees a player slot (`ZoneEntryReclamation.LeaveZone`, reached through `ZonePlayerExit.ReleasePlayer` and the stale-slot reclamation path) and by `ZoneSlotPool.ReleaseMob`'s caller for mobs, **before** the slot is released. Record the resolution in the GDD (OQ-SE-2 → Resolved) and in `zone-instancing.md` Interactions.
- [ ] **`ClearEntity`** runs the expiry procedure (Story 005/006, inside a stat transaction) for every row of the entity, Active or PendingRemoval, frees the rows, zeroes occupancy and clears the dead flag; an entity with no rows is a no-op; it never throws (catch, log, and still clear the rows).
- [ ] **AC-SE-22**: 3 active buffs → departure → 3 `RemoveBuffModifier` calls, 3 rows freed, effective values back to base.
- [ ] **AC-SE-23 / EC-SE-9**: a buff, `OnEntityDied`, then departure before the next tick → `RemoveBuffModifier` exactly once per stat (not twice); rows freed; the next tick's cleanup pass finds nothing for the entity.
- [ ] **AC-SE-21 / CR-SE-13**: caster A buffs target B; A departs (its own `ClearEntity`) → B's row unchanged (caster field still A) and `TicksRemaining` keeps decrementing.
- [ ] **Sequence contract**: an integration test with the real `ZoneSlotPool` + leave path proves `ClearEntity` runs before the slot index is released (e.g. the cleanup fake asserts `TryGetPlayerSlot` still succeeds when called), and a new entity allocated into the freed slot starts with zero rows.
- [ ] **Mob slot**: mob slot release clears the mob's rows the same way.

---

## Implementation Notes

- **Open decision for `/story-readiness`** — where the call lives: (a) a new optional constructor parameter on `ZoneEntryReclamation` (and the mob release caller) — explicit, matches ADR-010 Tier 1; or (b) a new `IZoneSlotPoolListener`-style "before release" callback on `ZoneSlotPool` — one hook for players and mobs, but the pool currently notifies only *after* changes. Recommendation: (b) as a separate `IZoneSlotReleaseListener.BeforeRelease(EntityID)` so every release path is covered without touching each caller.
- The zone composition wiring happens in Story 009; this story tests with the real zone classes constructed directly.

---

## Out of Scope

- Persisting buffs across zones (CR-SE-11: not at MVP); wire replication of buff state (TR-se-006).

---

## QA Test Cases

- **AC-SE-22 / 23 / 21** — as listed, real `CharacterStats` and a recording decorator.
- **Sequence** — real `ZoneSlotPool` + leave path; the cleanup fake checks the slot is still allocated when called; after release, a newly allocated entity in that slot has no rows.
- **Robustness** — the layer throws during `ClearEntity` → rows still freed, error logged, slot release proceeds.
- **Edges** — departure of an entity with no rows; departure twice (second is a no-op).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_ZoneDeparture_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 006, 007; Zone Instancing Stories 011, 013 (Complete)
- Unlocks: Story 009
