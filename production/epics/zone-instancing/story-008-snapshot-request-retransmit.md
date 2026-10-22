# Story 008: ZoneSnapshotRequest Retransmit (CR-ZI-9)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 3 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-9, EC-ZI-6, EC-ZI-7, AC-ZI-7
**Requirement**: `TR-zi-009` *(epic placeholder)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: `ZoneSnapshotRequest` is a connection-level message — not gated by the mutation gate, never held, handled per connection.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: connection-level messages go through the connection message path (`ConnectionMessageDescriptor`, `IConnectionMessageSink`), not the gated request dispatcher — ADR-014
- Required: `IronGrind.ServerLogic` — ADR-012
- Guardrail: rate limit counted in ticks; no wall-clock time

---

## Acceptance Criteria

- [ ] **AC-ZI-7**: a request with `snapshotVersion = V` processed at tick `T > V` sends a fresh snapshot (Story 007) with `snapshotVersion = T` and the zone state at T.
- [ ] **EC-ZI-7**: a forged `snapshotVersion > T` is ignored; the response still carries `T`.
- [ ] **Coalescing**: several requests from one (connection, zone) before the retransmit is assembled produce one assembly.
- [ ] **Rate limit**: at most 1 request per `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS × TICK_RATE_HZ` ticks per connection; excess requests are dropped and logged as `SnapshotRequestRateLimited`.
- [ ] **EC-ZI-6**: in Draining the request is honoured; once Closed (or for a `zoneId` that is not this zone) the reply is `ZoneFullResponse(ZoneDoesNotExist)`.
- [ ] A request from a connection that has no player slot in this zone is dropped and logged (no snapshot to a stranger).

---

## Implementation Notes

- Register `ZoneSnapshotRequest` as a connection-level message per ADR-014 (`SessionHandshake` is the other one, Story 009).
- "Coalesced" and "rate-limited" combine: track the last served tick per connection; a request inside the window is dropped; requests in the same tick before assembly collapse to one.

---

## Out of Scope

- Client-side timeout and retransmit (client app)
- Story 011: what happens after the client gives up (stale slot)

---

## QA Test Cases

- **AC-ZI-7** — Given V = 100, current tick 160; When processed; Then fragments carry 160.
- **Forged** — V = 999,999 at tick 160 → 160.
- **Coalesce** — 3 requests in one tick → one fragment set.
- **Rate limit** — second request 50 ticks after the first (window 200) → dropped, logged; at 200 ticks → served.
- **Draining / Closed** — Draining → snapshot; Closed → `ZoneFullResponse(ZoneDoesNotExist)`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotRequest_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 006, 007
- Unlocks: Story 014
