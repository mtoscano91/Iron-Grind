# Story 007: Death Cleanup and Instant Heal

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-3 (`ApplyInstantHeal`), CR-SE-10, States (PendingRemoval), EC-SE-2, EC-SE-3; AC-SE-16, 30 (AC-SE-17 is Story 002)
**Requirement**: `TR-se-005` (placeholder — registry empty)

**ADR Governing Implementation**: ADR-010 (Event/Messaging)
**ADR Decision Summary**: subscribe to `CharacterStats.OnEntityDied` in the constructor/`Initialize`, unsubscribe in `Dispose`; no stat writes inside a handler.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: subscribe in constructor/`Initialize()`, unsubscribe in `Dispose()` (server) — ADR-010
- Required: no write to Character Stats inside its event handlers (Character Stats Rule 8; `IsFiringAndAssert`)

---

## Acceptance Criteria

- [ ] **Subscription**: the service subscribes to `CharacterStats.OnEntityDied` when constructed with the real stats (through a small `IEntityDeathSource` seam so tests can raise it) and unsubscribes in `Dispose()`; after `Dispose` a death changes nothing.
- [ ] **AC-SE-16**: buff with `DurationTicks 100`; `OnEntityDied(entity)` raised → (1) zero `RemoveBuffModifier` calls during the handler; (2) the entity's rows are `PendingRemoval` (and the slot's dead flag is set). Next `OnServerTick()` → (3) `RemoveBuffModifier` once per stat per effect (with the MaxHP/MaxMP clamp of Story 006 **skipped** for a dead entity — HP is 0); (4) rows freed, occupancy 0. Pending rows get no decrement and no regen on that tick.
- [ ] **Death of an entity with no effects**: sets the dead flag only; no calls.
- [ ] **Dead flag lifetime**: stays set after the cleanup tick (the entity is still dead) until the slot is cleaned by Story 008 (departure / slot release); until then `ApplyEffect` and `ApplyInstantHeal` reject with `InvalidTarget`.
- [ ] **AC-SE-30**: `ApplyInstantHeal(entity, 100f)` → `ApplyRegen(entity, 100f)` exactly once; no row; occupancy unchanged; `Success`. `ApplyInstantHeal(entity, 0f)` → `InvalidAmount` (also negative and NaN). On a dead entity → `InvalidTarget`. Unknown entity → `InvalidTarget`.
- [ ] Death cleanup runs inside the same transaction bracket as the rest of the tick pass (Story 006).

---

## Implementation Notes

- `OnEntityDied` is the named delegate `EntityDiedHandler(EntityID)` on `CharacterStats` (Character Stats Story 005); fixed-capacity subscriber array of 16 — one more subscriber is fine.
- The handler only resolves the slot and flips flags; it must not call any `CharacterStats` method that writes.
- `ApplyInstantHeal` amount check: `!(amount > 0f)` catches NaN.

---

## Out of Scope

- Clearing the dead flag and departure cleanup (Story 008).

---

## QA Test Cases

- **AC-SE-16** — real `CharacterStats` with the entity killed via `ApplyDamage`; a recording decorator proves no remove during the handler; one tick later removes happen and rows are gone; no regen for the pending row.
- **AC-SE-30** — as listed, including NaN and negative.
- **Dispose** — after `Dispose`, killing an entity leaves its rows Active.
- **Edges** — death and an `ApplyEffect` on the same tick (EC-SE-3) → `InvalidTarget`; two entities, only one dies.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_DeathAndInstantHeal_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 005, 006
- Unlocks: Story 008
