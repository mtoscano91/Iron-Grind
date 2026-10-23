# Story 008: ZoneSnapshotRequest Retransmit (CR-ZI-9)

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-23) — written 2026-10-22; readiness 2026-10-23 (NEEDS WORK, 7 gaps -> fixed)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 5 hours (confirmed at `/story-readiness` 2026-10-23)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-9, EC-ZI-6, EC-ZI-7, AC-ZI-7
**Requirement**: `TR-zi-009` *(epic placeholder)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: `ZoneSnapshotRequest` is a connection-level message — not gated by the mutation gate, never held, handled per connection. Its sink runs inside the receive callback and only records (Decision 2 step 5); game work happens on the tick (Decision 6).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: connection-level messages go through the connection message path (`ConnectionMessageDescriptor`, `IConnectionMessageSink`), not the gated request dispatcher — ADR-014
- Required: `IronGrind.ServerLogic` — ADR-012
- Guardrail: rate limit counted in ticks; no wall-clock time

---

## Acceptance Criteria

- [ ] **AC-ZI-7**: a request with `snapshotVersion = V` served at tick `T > V` sends a fresh snapshot (Story 007 `SendSnapshot`) with `snapshotVersion = T` and the zone state at T.
- [ ] **EC-ZI-7**: a forged `snapshotVersion > T` is ignored; the response still carries `T`.
- [ ] **Coalescing**: several requests from one connection recorded before `ServePending` runs produce one assembly (one fragment set).
- [ ] **Rate limit**: a request is served only when `T − lastServedTick ≥ RateLimitWindowTicks` (constructor parameter, default `public const uint SNAPSHOT_REQUEST_RATE_LIMIT_TICKS = 200` = `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` 10 × `TICK_RATE_HZ` 20); the first request on a slot is always served. A dropped request does not restart the window.
- [ ] **Rate-limit logging**: the first drop in a window logs one `SnapshotRequestRateLimited` warning for that connection (`Debug.LogWarning`); later drops in the same window are counted, not logged (user decision 2026-10-23); when the slot is next served after 2 or more drops, one `Debug.Log` summary reports the count (code review 2026-10-23). Requests merged by coalescing are not drops.
- [ ] **EC-ZI-6**: in Draining the request is honoured; when the zone is Closed, or `zoneId` is not this zone, the reply is `ZoneFullResponse(ZoneDoesNotExist, retryAfterSeconds = 0)` and no snapshot is sent.
- [ ] **Identity**: the request is served only if the slot of `senderEntityId` is in this zone and its `ClientId == clientId`; otherwise it is dropped and nothing is sent (covers Loading, when no slot exists). All identity drops of one `ServePending` call, including foreign-list overflow, produce at most one aggregate `SnapshotRequestIdentityMismatch` warning (code review 2026-10-23).
- [ ] **Malformed body**: a body that fails to decode is dropped and nothing is sent; the callback only counts it, and the next `ServePending` logs at most one aggregate `SnapshotRequestMalformed` warning (code review 2026-10-23: no logging inside the receive callback).
- [ ] **Order of checks**: decode → zone mismatch / Closed → identity → rate limit → serve.
- [ ] **Slot reuse**: per-slot state is tagged with the occupant's `EntityID` (never reused); a different occupant starts fresh — not rate-limited, and no pending request carries over from the previous occupant.

---

## Implementation Notes

- `ZoneSnapshotRequestService : IConnectionMessageSink` in `src/ServerLogic/ZoneInstancing/`. `OnConnectionMessage` runs inside the receive callback (ADR-014 Decision 2 step 5): it decodes the body (`ZoneSnapshotRequestCodec`) and records only — `(clientId, senderEntityId, zoneId)` into preallocated per-player-slot storage (size `pool.MaxPlayers`); it never sends and never logs. A malformed body is counted, not recorded.
- `ServePending(uint currentTick)` runs as the **last entry of `ZoneTickPipeline` step 3 (Simulation)** (user decision 2026-10-23), so the snapshot reflects tick T after every system ran; the fragments and any `ZoneFullResponse` flush in step 4 of the same tick. It walks the pending entries, applies the checks in the order above and calls `ZoneSnapshotBuilder.SendSnapshot(clientId)` once per served slot.
- Closed / zone mismatch is decided at serve time (zone state at T, `ZoneLifecycle.State`); the `ZoneFullResponse` is encoded with `ZoneFullResponseCodec` and enqueued to `IClientMessageOutbox`.
- A request whose sender has no slot cannot be stored per slot: the sink keeps it in a small preallocated "rejected / foreign" list, one entry per client per tick (last request wins; bounded, overflow counted into the identity warning — code review 2026-10-23) so that the zone-mismatch / Closed reply and the identity drop still happen at serve time, in the order above.
- Per-slot entry: `EntityID occupant`, `uint clientId`, `bool pending`, `bool hasServed`, `uint lastServedTick`, `bool rateLimitLogged`, `uint droppedInWindow`. Index from `ZoneSlotPool.TryGetPlayerSlot`; a mismatching `occupant` resets the entry. No pool listener change; no allocation per request.
- Provides `ZoneSnapshotRequestService.Descriptor` = `new ConnectionMessageDescriptor(ZoneSnapshotRequest.MessageTypeId, maxBodyBytes: 8, carriesSenderEntityId: true)` and a static `Register(IInboundRequestDispatcher dispatcher, ZoneSnapshotRequestService service)` helper (pattern of `NetworkingCoreInboundRegistration`). `SessionHandshake` is the other connection-level message (Story 009).
- The tick is passed in by the caller; no wall-clock time.

---

## Out of Scope

- Client-side timeout and retransmit (client app)
- Story 011: what happens after the client gives up (stale slot)
- Story 014: calling `Register` in `ZoneServerComposition` and listing `ServePending` last in the simulation order

---

## QA Test Cases

- **AC-ZI-7** — Given V = 100, current tick 160; When served; Then fragments carry 160.
- **Forged** — V = 999,999 at tick 160 → 160.
- **Coalesce** — 3 requests recorded before one `ServePending` → one fragment set.
- **Rate limit** — served at tick 1000; request at 1050 → dropped, one warning; another at 1100 → dropped, no new warning; at 1200 (exactly 200) → served; a later window logs again.
- **Draining / Closed** — Draining → snapshot; Closed → `ZoneFullResponse(ZoneDoesNotExist, 0)`; foreign `zoneId` → same reply.
- **Identity** — sender with no slot; slot whose `ClientId` differs; zone in Loading → dropped, warning, nothing sent.
- **Malformed** — 7-byte body → dropped, warning, nothing sent.
- **Slot reuse** — occupant A served at 1000, slot released, B allocated in it, request at 1010 → served.
- **No send in sink** — `OnConnectionMessage` alone enqueues nothing.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotRequest_tests.cs` — must exist and pass.

**Status**: [x] Created — 43 tests, all passing (EditMode 3893 total, 0 failed, 70 skipped Npgsql; commit `76e7cad`)

---

## Dependencies

- Depends on: Stories 001, 006, 007 (all Complete)
- Unlocks: Story 014

---

## Completion Notes
**Completed**: 2026-10-23
**Criteria**: 10/10 passing, all by automated test (`tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotRequest_tests.cs`, 43 tests).
**Deviations** (advisory; user chose "Complete, notes only"):
1. Log tokens added: `SnapshotRequestMalformed`, `SnapshotRequestIdentityMismatch`; malformed and identity drops aggregated to at most one warning each per `ServePending`; a `Debug.Log` drop-count summary on the next serve after 2+ drops (story text updated at code review).
2. `ZoneFullResponse` carries the `zoneId` the client sent, not this zone's id.
3. A request recorded as foreign is not re-checked against the pool at serve time (dropped even if the slot is allocated later in the same tick; the client retries).
4. Rate-limit state is per player slot keyed by `EntityID`, not per connection — **note for Story 011**: a reconnect that keeps the slot and EntityID inherits the window.
5. Coalescing keeps the last recorded `zoneId` (right-then-wrong zone in one tick yields only the reply).
6. The tick difference is modular: a tick regression serves (fails open).
7. Concrete dependencies (`ZoneSlotPool`, `ZoneLifecycle`, `ZoneSnapshotBuilder`), as in Story 007.
8. `ZoneDoesNotExist` replies are not rate-limited; bounded to one per client per tick.
9. `TR-zi-009` is an epic placeholder, not in `tr-registry.yaml`.
**Story 014**: call `ZoneSnapshotRequestService.Register(dispatcher, service)` in `ZoneServerComposition` before `Seal()`, and list `ServePending(currentTick)` last in step 3 (Simulation).
**Test Evidence**: Logic — `tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotRequest_tests.cs`.
**Code Review**: Complete — lean (unity-specialist + qa-tester), CHANGES REQUIRED -> fix all (`76e7cad`). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
