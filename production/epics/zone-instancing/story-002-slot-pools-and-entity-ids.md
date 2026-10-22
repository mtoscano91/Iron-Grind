# Story 002: Player and Mob Slot Pools, EntityID Minting, Player Roster View

> **Epic**: Zone Instancing
> **Status**: Complete — 2026-10-22 (written and lean readiness 2026-10-22)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 6 hours (confirmed at `/story-readiness` 2026-10-22)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-3, CR-ZI-4, CR-ZI-5, CR-ZI-15, EC-ZI-4, EC-ZI-12, `ZoneSessionState` enum, Runtime Model
**Requirement**: `TR-zi-003`, `TR-zi-004`, `TR-zi-015` *(epic placeholders — not in `tr-registry.yaml`; they stay placeholders until `/architecture-review`, as in Story 001 Decision 4)*

**ADR Governing Implementation**: design rule (main-thread atomicity, GDD Runtime Model); ADR-010 for the roster view consumed by the Relevance Filter
**ADR Decision Summary**: all slot mutations run on the main thread, so the capacity check and the slot write are atomic without locks; consumers reach the roster by a direct call on an injected interface.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — plain C#.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` — ADR-012
- Forbidden: `Interlocked` / `ConcurrentQueue` are not needed (GDD Runtime Model) — do not add them
- Guardrail: allocation and release are O(1) (free-list); no allocation per call after construction

---

## Acceptance Criteria

- [ ] **Player pool**: `MAX_PLAYERS_PER_ZONE` = 50 slots with `byte` indices [0, 49]; free-list; an index is never reassigned while occupied. A slot holds `EntityID`, `CharacterID`, `AccountID`, connection id (`uint clientId`, the type used across `src/`) and `ZoneSessionState`. `ZoneSessionState` is a new `byte` enum in `src/ServerLogic/ZoneInstancing/ZoneSessionState.cs` (`Connected = 0`, `Disconnected_SessionActive = 1`, `Dead = 2`, `Respawning = 3`), separate from `IronGrind.Networking.SessionState`, whose numbering differs.
- [ ] **API** (class `ZoneSlotPool`, `src/ServerLogic/ZoneInstancing/`):
  - `TryAllocatePlayer(ZoneID, CharacterID, AccountID, uint clientId, out byte slot, out EntityID id) : bool` — writes the slot with `ZoneSessionState.Connected` (CR-ZI-8 step 5).
  - `ReleasePlayer(EntityID) : bool`.
  - `TryGetPlayerSlot(EntityID, out byte slot) : bool`.
  - `SetSessionState(EntityID, ZoneSessionState) : bool` — false for an unknown EntityID; never changes occupancy.
  - `TryAllocateMob(ZoneID, out ushort mobSlot, out EntityID id) : bool` and `ReleaseMob(EntityID) : bool` — the base Story 003's `TryAddMob` / `RemoveMob` wrap.
  - `OccupiedPlayerCount : int`, `OccupiedMobCount : int`.
- [ ] **Capacity**: every slot-holding state counts toward capacity; the check and the write are one call (`TryAllocatePlayer`) that fails at cap.
- [ ] **Ghost counts toward capacity** (moved from the deleted `ZoneSessionStateMachine.EvaluateJoinAttempt` tests, Story 001 Decision 2): 49 `Connected` + 1 `Disconnected_SessionActive` slot → the next `TryAllocatePlayer` fails.
- [ ] **AC-ZI-4**: with 49 of 50 slots allocated, two allocations in the same tick give exactly one success and one failure; the occupied count is 50, never 51.
- [ ] **Mob pool**: `MAX_MOBS_PER_ZONE` = 150 slots with `ushort` indices [0, 149]; OWL index of a mob slot = `MAX_PLAYERS_PER_ZONE + mobSlot` (range [50, 199]).
- [ ] **AC-ZI-11**: in a fresh zone, 10 entities (players or mobs, mixed) get `EntityID` 1 to 10 in order; `0` is never issued.
- [ ] **EC-ZI-12**: when the counter would pass `uint.MaxValue` (wrap to 0), no ID is issued and the allocation fails; an `EntityIDCounterExhausted` critical alert is logged once, and `IZoneSlotPoolListener.OnEntityIDCounterExhausted(serverTick)` is called once (later failed allocations fail silently). Story 014 maps it to `RequestTeardown(Fatal)` → `ZoneLifecycle.RequestFatal`.
- [ ] **AC-ZI-15**: `GetPlayerEntityIDsInZone(ZoneID)` returns an `IReadOnlyList<EntityID>` of occupied player slots only (no empty, no mob slots); casting it to `IList<EntityID>` and calling a mutator throws `InvalidCastException` or `NotSupportedException`.
- [ ] Releasing a slot zeroes it and returns it to the free-list; the occupied count drops by 1. Releasing an unknown or already-released EntityID returns false, changes nothing and raises no notification.
- [ ] Every successful player allocation and release calls `IZoneSlotPoolListener.OnOccupiedPlayerSlotsChanged(occupied, serverTick)` once, with the new count and the tick read from the injected `IServerTickSource` (`src/ServerLogic/Networking/InboundDispatch/`). Mob allocation/release and `SetSessionState` do not call it. Story 014 forwards it to `ZoneLifecycle.OnOccupiedPlayerSlotsChanged`.

---

## Implementation Notes

- Knobs as `public const` defaults through constructor parameters; the counter start value is a constructor parameter so EC-ZI-12 is testable without 4 billion allocations.
- One `ZoneID` per process: methods taking `ZoneID` reject a foreign id (log, return the empty / failure result).
- The roster view: a wrapper over a reused list (for example `ReadOnlyCollection<EntityID>`) refreshed on change, so the Relevance Filter can read it every tick without allocation.
- Constructor: `ZoneSlotPool(ZoneID zone, IServerTickSource ticks, IZoneSlotPoolListener listener, int maxPlayers = MAX_PLAYERS_PER_ZONE, int maxMobs = MAX_MOBS_PER_ZONE, uint firstEntityId = 1)`.
- `IZoneSlotPoolListener` (new file): `OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick)`, `OnEntityIDCounterExhausted(uint serverTick)`. Direct call per ADR-010, main thread; listeners must not throw (same rule as Story 001's `IZoneLifecycleListener`). The pool's count is final before the listener runs, so a re-entrant call sees a consistent pool.

---

## Out of Scope

- Story 003: the mob-facing interface (`TryAddMob`, `RemoveMob`, `GetMobCount`)
- Story 009: the entry handler that calls `TryAllocatePlayer`
- Story 011: stale-slot reclamation

---

## QA Test Cases

- **AC-ZI-4** — Given 49 allocated; When two `TryAllocatePlayer` calls in one tick; Then one true, one false, count 50.
- **Stable index** — Given slots 0–2 allocated; When slot 1 is released and a new player allocated; Then slots 0 and 2 keep their EntityIDs; the new player gets the freed index.
- **AC-ZI-11** — Given a fresh zone; When 10 mixed allocations; Then IDs 1..10; none 0.
- **EC-ZI-12** — Given counter start `uint.MaxValue`; When one allocation succeeds (gets `uint.MaxValue`) and the next is attempted; Then the second fails, alert logged once, T-6 callback raised once.
- **AC-ZI-15** — cast to `IList<EntityID>` then `Add` → `NotSupportedException` or `InvalidCastException`; list excludes mobs and empty slots.
- **OWL offset** — mob slot 0 → OWL index 50; mob slot 149 → 199.
- **Ghost capacity** — Given 50 allocated, one moved by `SetSessionState` to `Disconnected_SessionActive`; When `TryAllocatePlayer`; Then false, count 50.
- **Notification** — Given a tick source at 100; When allocate then release; Then the listener sees (1, 100) then (0, 100); a failed allocation, an unknown release, `SetSessionState` and mob calls add nothing.
- **Unknown id** — `ReleasePlayer`, `SetSessionState`, `TryGetPlayerSlot` with an unallocated EntityID → false.
- **Foreign ZoneID** — `TryAllocatePlayer` / `TryAllocateMob` / `GetPlayerEntityIDsInZone` with another zone → failure / empty list, one log each.

---

## Files

- New: `src/ServerLogic/ZoneInstancing/ZoneSlotPool.cs`, `ZoneSessionState.cs`, `IZoneSlotPoolListener.cs`
- New: `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotPools_tests.cs`

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotPools_tests.cs` — must exist and pass.

**Status**: [x] Created — 64 cases, all passing (batch EditMode 2026-10-22)

---

## Dependencies

- Depends on: None
- Unlocks: Stories 003, 007, 009, 011, 012, 013

---

## Completion Notes
**Completed**: 2026-10-22
**Criteria**: 11/11 passing (none deferred)
**Deviations** (advisory, accepted):
- The roster is updated in O(1) by swap-remove, so its order is unspecified (not slot-index order); CR-ZI-15 requires no order.
- `GetPlayerEntityIDsInZone` with a foreign `ZoneID` warns once per pool, not once per call (it is polled every tick); `TryAllocatePlayer` / `TryAllocateMob` still warn on every foreign-zone call.
- `EntityIDCounterExhausted` is raised on the first allocation attempt that reaches ID minting after `uint.MaxValue` was issued; a full-pool or foreign-zone attempt fails earlier and raises nothing (pinned by a test).
- Extra public API: `TryGetPlayer` / `ZonePlayerSlot` (own file), `Zone`, `MaxPlayers`, `MaxMobs` getters, `GetOwlIndexForMobSlot(ushort)` (instance method, uses the configured `maxPlayers`).
- `TR-zi-003`, `TR-zi-004`, `TR-zi-015` are still epic placeholders (readiness decision). Control-manifest "broadcast events are `event Action<T>`" does not apply: single ordered consumer → listener interface (as Story 001 Decision 3, ADR-010).
**Notes for Story 014**: forward `IZoneSlotPoolListener.OnOccupiedPlayerSlotsChanged` to `ZoneLifecycle.OnOccupiedPlayerSlotsChanged` and map `OnEntityIDCounterExhausted` to `RequestTeardown(Fatal)`; the listener must not throw.
**Test Evidence**: Logic — `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotPools_tests.cs` (64 cases).
**Code Review**: Complete — `/code-review` 2026-10-22 APPROVED WITH SUGGESTIONS, all 8 suggestions applied (commit `6d9754d`). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
