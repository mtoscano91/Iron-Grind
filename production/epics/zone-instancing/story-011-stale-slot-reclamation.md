# Story 011: Stale Slot Reclamation and Entry Stall (CR-ZI-17, F-ZI-2)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22 (open decision 1 must be settled at `/story-readiness`)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 3 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-17, F-ZI-2, AC-ZI-14, Tuning Knobs (`ZONE_ENTRY_STALL_TIMEOUT_SECONDS`)
**Requirement**: `TR-zi-016` *(epic placeholder)*

**ADR Governing Implementation**: design rule (GDD); ADR-011 for the main-thread mutation
**ADR Decision Summary**: slot mutations run on the main thread; timers count ticks.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` — ADR-012
- Guardrail: the stall check is O(players) at most once per tick

---

## Acceptance Criteria

- [ ] **CR-ZI-17**: a `SessionHandshake` (Story 009's entry request) for a `CharacterID` that already holds an "entering" slot in this zone frees the stale slot first (occupied count −1), then admission proceeds normally; no `PlayerJoinedZone`, no `NotifyZoneExit`, no `SaveSession` for the stale entry.
- [ ] **AC-ZI-14**: an "entering" slot that has not completed entry within `ZONE_ENTRY_STALL_TIMEOUT_SECONDS × TICK_RATE_HZ` ticks is freed; the occupied count drops by 1; the player gets the timeout response (open decision 1); no request attributed to that player is processed afterwards (directory entry removed).
- [ ] A duplicate-`CharacterID` handshake against a slot that has completed entry (`Connected`, or a ghost) is not a stale slot: it follows the session contract (reconnect / duplicate-login rules), not this rule.
- [ ] Freeing a stale slot raises the occupancy change (Story 001: may start the idle timer).
- [ ] **"Entering" state (moved from Story 010 at its readiness, 2026-10-23)**: a slot counts as "entering" from allocation (Story 009 step 5) until entry completes (open decision 1; Story 010's step 10 activation, `ZoneTickRoster.IsInTickLoop`, is a candidate end point). T-7 cannot fire while the slot is occupied (the occupied count is already > 0).

---

## Implementation Notes

- Default 35 s (`ZONE_ENTRY_STALL_TIMEOUT_SECONDS`, range [31, 60]) as a `public const` knob through the constructor.

**Open decisions for `/story-readiness`**

1. **What ends "entering" on the server, and what the timeout response is.** Steps 6–10 run immediately on the server, and the server gets no acknowledgement of reassembly, so "slot allocated, snapshot not acked" has no server-side signal in the GDD. Options: (a) entering ends at the first inbound message from that connection after `SessionReady` (any RPC or heartbeat); (b) entering ends at step 10 and AC-ZI-14 is covered by the ordinary heartbeat timeout instead. The timeout response (`ZoneFullResponse(Other)`? connection close?) is also unspecified. This may need a GDD clarification — if so, stop and run a short authoring session first.

---

## Out of Scope

- Ghost / TTL slot cleanup (session contract; Story 013 for the exit side)
- Client retransmit and reconnect behaviour

---

## QA Test Cases

- **Reclaim** — Given character C entering in slot 4; When a new handshake for C is admitted; Then slot 4 freed first, C gets a new slot, count unchanged overall, no `PlayerJoinedZone` / `NotifyZoneExit` / `SaveSession` for the old entry.
- **AC-ZI-14** — Given entering since tick 0, timeout 700 ticks; When tick 700 passes without entry completing; Then slot freed, count −1, timeout response sent, a later request from that connection is dropped.
- **Not stale** — duplicate handshake for a `Connected` C → not reclaimed by this rule.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_StaleSlot_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 009, 010
- Unlocks: None
