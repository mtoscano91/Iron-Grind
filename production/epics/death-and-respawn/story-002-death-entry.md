# Story 002: Death Entry — DeathState and T-1

> **Epic**: Death & Respawn
> **Status**: Complete
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

- [x] **AC-1 Service and state (AC-DR-1)**: `DeathAndRespawnService` (`src/ServerLogic/DeathAndRespawn/`) takes an `IEntityDeathSource` (`src/ServerLogic/StatusEffects/IEntityDeathSource.cs`; Character Stats implements it — there is no `OnEntityDied` C# event) and in its constructor stores one `CharacterStats.EntityDiedHandler` delegate instance and calls `Subscribe` with it (same pattern as `StatusEffectsService.cs:105–108`). For a player entity in the zone (`ZoneSlotPool.TryGetPlayer` succeeds) with `DeathState.CurrentState == Alive`, the handler sets `CurrentState = Dead`, `DeathTick = currentTick`, `RespawnTick = DeathTick + (long)RespawnDelayTicks`, and `ZoneSlotPool.SetSessionState(id, Dead)`. `DeathState` is the struct from the GDD (`long` ticks; respawn zone / position filled in Story 004).
- [x] **AC-2 Non-players ignored**: `OnEntityDied` for a mob (or any id not in the pool's player list) changes nothing and logs nothing above Debug.
- [x] **AC-3 Duplicate suppression (AC-DR-2)**: a second `OnEntityDied` for an entity in Dead or Respawning is dropped: `DeathState` unchanged, no timer restart, no second prohibition call, no second party call.
- [x] **AC-4 Combat and movement banned (AC-DR-3)**: in the same handler, `ICombatProhibitionSink.SetCombatProhibited(id, true)` and `IMovementSystem.SetProhibited(id, true)` are each called once. `ICombatProhibitionSink` is declared in `src/ServerLogic/DeathAndRespawn/` with `void SetCombatProhibited(EntityID entity, bool prohibited)`; `AutoAttackCombatSystem` implements it (its existing public method already has that signature — add the interface to its declaration only). Unit tests count calls on fakes for both; no read-back through `AutoAttackCombatSystem.GetState` (it shows `Prohibited` only while auto-attack is active, and the system needs 14 dependencies). Real wiring is verified in Story 008.
- [x] **AC-5 Party seam (CR-DR-5, AC-DR-13 unit part, TR-dr-009)**: `IPartyMemberStatusSink` declared in `src/ServerLogic/DeathAndRespawn/` with `SetMemberStatus(EntityID, PartyMemberDeathStatus)` (`Dead`, `Online`); the service calls it once with `Dead` on entry. A `NoOpPartyMemberStatusSink` ships for production. A fake verifies the call.
- [x] **AC-6 Long ticks (AC-DR-15)**: with a fake tick source returning `(long)uint.MaxValue + 1`, `RespawnTick == DeathTick + RespawnDelayTicks` exactly, no overflow.
- [x] **AC-7 Current tick source**: the handler reads the current tick from an injected `IDeathTickSource { long CurrentTick { get; } }` declared in `src/ServerLogic/DeathAndRespawn/`, not from Unity time. A production adapter `ServerTickDeathTickSource` wraps `IServerTickSource` and returns `(long)ServerTickNumber` (the `uint` counter's own wrap is the tick loop's concern, ADR-014, not this story's). A test covers the adapter's widening.
- [x] **AC-8 Dispose**: `Dispose` calls `IEntityDeathSource.Unsubscribe` with the same delegate instance; idempotent; a death raised through the fake source after `Dispose` changes nothing.
- [x] **AC-9 Query**: `TryGetDeathState(EntityID, out DeathState)` and `IsDead(EntityID)` (true in Dead or Respawning) for later stories and tests.

---

## Implementation Notes

- Per-player state in a slot-indexed array sized to the pool's max players (no dictionary allocation per death), indexed via `ZoneSlotPool.TryGetPlayerSlot`. All entries start Alive (default). Resetting a slot to Alive on release is Story 007 (AC-6); this story adds no enter or release hook.
- Keep the ghost check out of this story's handler, but leave the first line of the handler as the place Story 003 inserts it (CR-DR-Ghost must run before CR-DR-2).
- CR-DR-4: make **no** Status Effects call here; Status Effects has its own `OnEntityDied` subscription.
- Messages (`DeathStateEntered`, `EntityDied`) are Story 005; leave a single private method where they will be enqueued.

---

## Out of Scope

- Story 003: ghost branch
- Story 004: respawn (T-2 / T-3)
- Story 005: wire messages
- Story 007: disconnect / teardown interplay with the slot state, including clearing `DeathState` on slot release
- Story 008: building the service in `ZoneServerComposition` with the real Character Stats, Auto-Attack Combat, Movement and tick loop

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1**: Given player P Alive at tick 1000, delay 140, When `OnEntityDied(P)` fires, Then Dead, DeathTick 1000, RespawnTick 1140, slot state Dead.
- **AC-2**: Given a mob id M, When `OnEntityDied(M)`, Then no state change, no prohibition calls.
- **AC-3**: Given P Dead, When `OnEntityDied(P)` again at tick 1005, Then RespawnTick still 1140, prohibition fakes called once, party fake called once. Repeat with P in Respawning.
- **AC-4**: After death, combat-prohibition fake records exactly one `(P, true)`; movement fake records exactly one `SetProhibited(P, true)`.
- **AC-5**: Party fake records exactly one `(P, Dead)`.
- **AC-6**: Given the fake tick source at 4294967296, Then DeathTick 4294967296, RespawnTick 4294967436.
- **AC-7**: Given a fake `IServerTickSource` at `uint.MaxValue`, Then `ServerTickDeathTickSource.CurrentTick == 4294967295L`.
- **AC-8**: After `Dispose`, the fake death source holds no subscriber and raising death for P leaves P Alive; a second `Dispose` does not throw.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeathEntry_tests.cs` — must exist and pass

**Status**: [x] Created and passing (22 tests, 2026-11-18)

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 004, 006

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 9/9 passing (all automated)
**Deviations** (advisory):
- `internal SetStateForTests(EntityID, DeathStateKind)` test hook to reach Respawning before Story 004; replace with the real T-2 path there.
- Constructor validates the config (`Validate()` → `ArgumentException`, before subscribing), matching `MobSpawningService` (from `/code-review`).
- `_disposed` early return in the handler in addition to `Unsubscribe`.
- `PartyMemberDeathStatus` is `Online = 0, Dead = 1` (unset reads Online).
- Known gap until Story 007: a released slot keeps its `DeathState`, so a new occupant could inherit Dead. No production risk until Story 008 wires the service.
**Test Evidence**: Logic — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeathEntry_tests.cs`, 22 tests; filter `DeathAndRespawn` 57/57; full EditMode `-testCategory "!Database"` 7891/7891.
**Code Review**: Complete (`/code-review` 2026-11-18, all required changes and suggestions applied)
**Files**: `src/ServerLogic/DeathAndRespawn/` — `DeathAndRespawnService`, `DeathState`, `DeathStateKind`, `IDeathTickSource`, `ServerTickDeathTickSource`, `ICombatProhibitionSink`, `IPartyMemberStatusSink`, `PartyMemberDeathStatus`, `NoOpPartyMemberStatusSink`; `AutoAttackCombatSystem.cs` implements `ICombatProhibitionSink` (declaration only).
