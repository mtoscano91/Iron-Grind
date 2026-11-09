# Story 004: Real `IZoneLink` — requests, replies and timeouts

> **Epic**: Gateway
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-11 (session establishment through the zone); `design/gdd/zone-instancing.md` — "`AdmitResult` rejection"
**Requirement**: `TR-auth-003` (link part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3)
**ADR Decision Summary**: Every G→Z request carries a `requestId` echoed by its reply. A pending `Admit` or `Reconnect` with no reply within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` (2000), or an `Evict` with no reply within `GATEWAY_EVICT_TIMEOUT_MS` (11000), fails the client's login with `ServerError`. An `AdmitResult(accepted)` whose `requestId` is unknown or already timed out is answered with `Evict` for that account, and no map entry is added.

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API.

**Control Manifest Rules (this layer)**:
- Required: timeouts from config (`GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, `GATEWAY_EVICT_TIMEOUT_MS`) — already in `GatewayOptions`
- Required: late admission → `Evict`, no map entry — ADR-016 Decision 3

---

## Acceptance Criteria

- [ ] `LinkZoneLink : IZoneLink` sends `Admit` / `Evict` / `Reconnect` through the Story 003 sender with a fresh `requestId` (monotonic per gateway run, never 0) and completes the caller's task when the matching reply arrives
- [ ] `AdmitAsync` returns true on `AdmitResult(accepted)`, false on rejected; `ReconnectAsync` returns the zone's outcome and new token; `EvictAsync` completes on `EvictResult`
- [ ] Timeouts: `Admit` / `Reconnect` fail (`ZoneLinkTimeout` or the existing timeout type from `ZoneLinkTimeout.cs`) after `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`; `Evict` after `GATEWAY_EVICT_TIMEOUT_MS`; the caller's `CancellationToken` cancels the wait
- [ ] Link down at send time, or link lost while a request is pending, fails the request at once (no wait for the timeout)
- [ ] A reply with an unknown or timed-out `requestId`: `AdmitResult(accepted)` → `Evict { accountId }` is sent to that zone and the map is not touched; any other unknown reply is logged and dropped
- [ ] On `AdmitResult(accepted)` for a live request, the map's add (Story 005 — via `IAccountZoneMap.SetSession`) happens before the caller's task completes
- [ ] Tests use a fake link transport and a fake clock / timer (no real-time waits)

---

## Implementation Notes

*Derived from ADR-016 Decision 3 and the existing session establishment (Authentication Story 010):*

- `SessionEstablishment` already calls `IZoneLink` and maps timeouts to `ServerError`; keep its contract — read it first and match the exception type it expects (`ZoneLinkTimeout.cs`).
- Pending requests: `ConcurrentDictionary<uint, TaskCompletionSource<…>>`, removed on reply, timeout or link loss. Run continuations asynchronously (`TaskCreationOptions.RunContinuationsAsynchronously`).
- The account for a late `AdmitResult` is not in the reply; keep the account in the pending entry and keep timed-out `Admit` entries in a short-lived "expired" set (bounded, e.g. last N or `GATEWAY_EVICT_TIMEOUT_MS`) so a late accept can be answered with `Evict`.
- Inject a timer/clock seam so tests do not wait 2 s / 11 s.

---

## Out of Scope

- Story 005: the map's own rules (removal, grace, registration)
- Story 006: re-route after rejection (already in `SessionEstablishment`; this story only reports the result)

---

## QA Test Cases

- **AC-1/2**: Reply matching
  - Given: fake transport; two concurrent `AdmitAsync` to zone 7
  - When: replies arrive in reverse order
  - Then: each task gets its own result
- **AC-3**: Timeouts
  - Given: fake clock
  - When: no reply and clock advances 1999 ms → still pending; 2000 ms → fails with the timeout type
  - Edge cases: `Evict` pending at 10 999 ms vs 11 000 ms; caller cancels before timeout
- **AC-4**: Link loss
  - Given: pending `Reconnect` to zone 7
  - When: link lost
  - Then: fails at once; a later reply for that `requestId` is ignored
- **AC-5**: Late admission
  - Given: an `Admit` for account 42 timed out
  - When: `AdmitResult(accepted)` with its `requestId` arrives
  - Then: `Evict { requestId: new, accountId: 42 }` sent to that zone; map has no entry for 42

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_ZoneLink_tests.cs` — must pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 003
- Unlocks: Story 005, Story 009
