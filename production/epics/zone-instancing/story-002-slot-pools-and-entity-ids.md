# Story 002: Player and Mob Slot Pools, EntityID Minting, Player Roster View

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-3, CR-ZI-4, CR-ZI-5, CR-ZI-15, EC-ZI-4, EC-ZI-12, `ZoneSessionState` enum, Runtime Model
**Requirement**: `TR-zi-003`, `TR-zi-004`, `TR-zi-015` *(epic placeholders)*

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

- [ ] **Player pool**: `MAX_PLAYERS_PER_ZONE` = 50 slots with `byte` indices [0, 49]; free-list; an index is never reassigned while occupied. A slot holds `EntityID`, `CharacterID`, `AccountID`, connection id and `ZoneSessionState` (`Connected = 0`, `Disconnected_SessionActive = 1`, `Dead = 2`, `Respawning = 3`).
- [ ] **Capacity**: every slot-holding state counts toward capacity; the check and the write are one call (`TryAllocatePlayer`) that fails at cap.
- [ ] **AC-ZI-4**: with 49 of 50 slots allocated, two allocations in the same tick give exactly one success and one failure; the occupied count is 50, never 51.
- [ ] **Mob pool**: `MAX_MOBS_PER_ZONE` = 150 slots with `ushort` indices [0, 149]; OWL index of a mob slot = `MAX_PLAYERS_PER_ZONE + mobSlot` (range [50, 199]).
- [ ] **AC-ZI-11**: in a fresh zone, 10 entities (players or mobs, mixed) get `EntityID` 1 to 10 in order; `0` is never issued.
- [ ] **EC-ZI-12**: when the counter would pass `uint.MaxValue` (wrap to 0), no ID is issued, an `EntityIDCounterExhausted` critical alert is logged once, and a T-6 request is raised (callback consumed by Story 001 / 014).
- [ ] **AC-ZI-15**: `GetPlayerEntityIDsInZone(ZoneID)` returns an `IReadOnlyList<EntityID>` of occupied player slots only (no empty, no mob slots); casting it to `IList<EntityID>` and calling a mutator throws `InvalidCastException` or `NotSupportedException`.
- [ ] Releasing a slot zeroes it and returns it to the free-list; the occupied count drops by 1; releasing an empty slot is a no-op.
- [ ] An occupancy-changed notification (new occupied player count) is raised on every allocation and release, for the lifecycle (Story 001).

---

## Implementation Notes

- Knobs as `public const` defaults through constructor parameters; the counter start value is a constructor parameter so EC-ZI-12 is testable without 4 billion allocations.
- One `ZoneID` per process: methods taking `ZoneID` reject a foreign id (log, return the empty / failure result).
- The roster view: a wrapper over a reused list (for example `ReadOnlyCollection<EntityID>`) refreshed on change, so the Relevance Filter can read it every tick without allocation.

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

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotPools_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 003, 007, 009, 011, 012, 013
