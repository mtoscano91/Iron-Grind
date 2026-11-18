# Story 002: Death Entry — DeathState and T-1

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3–4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-1, CR-DR-2, CR-DR-3, CR-DR-5, CR-DR-6, States and Transitions (T-1), `DeathState` struct, EC-DR-6
**Requirement**: `TR-dr-002`, `TR-dr-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Cross-system notifications are C# events subscribed by named method and unsubscribed on dispose; follow-up work in another system is a direct call through a narrow interface.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#).

**Control Manifest Rules (this layer)**:
- Required: named-method subscriptions, unsubscribed in `Dispose` (ADR-010)
- Forbidden: an `EventBus` class; class-typed event args (ADR-010)
- Forbidden: `async`/`await` in tick or handler code (ADR-011)

---

## Acceptance Criteria

- [ ] **AC-1 Service and state (AC-DR-1)**: `DeathAndRespawnService` (`src/ServerLogic/DeathAndRespawn/`) subscribes to Character Stats `OnEntityDied` in its constructor. For a player entity in the zone (`ZoneSlotPool.TryGetPlayer` succeeds) with `DeathState.CurrentState == Alive`, the handler sets `CurrentState = Dead`, `DeathTick = currentTick`, `RespawnTick = DeathTick + (long)RespawnDelayTicks`, and `ZoneSlotPool.SetSessionState(id, Dead)`. `DeathState` is the struct from the GDD (`long` ticks; respawn zone / position filled in Story 004).
- [ ] **AC-2 Non-players ignored**: `OnEntityDied` for a mob (or any id not in the pool's player list) changes nothing and logs nothing above Debug.
- [ ] **AC-3 Duplicate suppression (AC-DR-2)**: a second `OnEntityDied` for an entity in Dead or Respawning is dropped: `DeathState` unchanged, no timer restart, no second prohibition call, no second party call.
- [ ] **AC-4 Combat and movement banned (AC-DR-3)**: in the same handler, `SetCombatProhibited(id, true)` on Auto-Attack Combat and `SetProhibited(id, true)` on Movement are each called once. A test reads the combat prohibition back through the existing `AutoAttackState.Prohibited` surface (no `IsCombatProhibited` API exists; do not add one unless readiness asks).
- [ ] **AC-5 Party seam (CR-DR-5, AC-DR-13 unit part, TR-dr-009)**: `IPartyMemberStatusSink` declared in `src/ServerLogic/DeathAndRespawn/` with `SetMemberStatus(EntityID, PartyMemberDeathStatus)` (`Dead`, `Online`); the service calls it once with `Dead` on entry. A `NoOpPartyMemberStatusSink` ships for production. A fake verifies the call.
- [ ] **AC-6 Long ticks (AC-DR-15)**: with `currentTick = (long)uint.MaxValue + 1`, `RespawnTick == DeathTick + RespawnDelayTicks` exactly, no overflow.
- [ ] **AC-7 Current tick source**: the handler reads the current tick from an injected tick source (the zone's tick counter), not from Unity time.
- [ ] **AC-8 Dispose**: `Dispose` unsubscribes; an `OnEntityDied` after `Dispose` changes nothing.
- [ ] **AC-9 Query**: `TryGetDeathState(EntityID, out DeathState)` and `IsDead(EntityID)` (true in Dead or Respawning) for later stories and tests.

---

## Implementation Notes

- Per-player state in a slot-indexed array sized to the pool's max players (no dictionary allocation per death); reset to Alive when a player enters the zone and cleared on release.
- Keep the ghost check out of this story's handler, but leave the first line of the handler as the place Story 003 inserts it (CR-DR-Ghost must run before CR-DR-2).
- CR-DR-4: make **no** Status Effects call here; Status Effects has its own `OnEntityDied` subscription.
- Messages (`DeathStateEntered`, `EntityDied`) are Story 005; leave a single private method where they will be enqueued.

---

## Out of Scope

- Story 003: ghost branch
- Story 004: respawn (T-2 / T-3)
- Story 005: wire messages
- Story 007: disconnect / teardown interplay with the slot state

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1**: Given player P Alive at tick 1000, delay 140, When `OnEntityDied(P)` fires, Then Dead, DeathTick 1000, RespawnTick 1140, slot state Dead.
- **AC-2**: Given a mob id M, When `OnEntityDied(M)`, Then no state change, no prohibition calls.
- **AC-3**: Given P Dead, When `OnEntityDied(P)` again at tick 1005, Then RespawnTick still 1140, prohibition fakes called once, party fake called once. Repeat with P in Respawning.
- **AC-4**: After death, combat state for P reads Prohibited; movement fake `SetProhibited(P, true)` once.
- **AC-5**: Party fake records exactly one `(P, Dead)`.
- **AC-6**: Given tick 4294967296, Then RespawnTick 4294967436.
- **AC-8**: After `Dispose`, `OnEntityDied(P)` leaves P Alive.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeathEntry_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 004, 006
