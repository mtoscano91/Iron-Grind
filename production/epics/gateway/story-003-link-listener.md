# Story 003: Gateway link listener

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — "Where routing runs (ADR-016)": a zone with no live link is unroutable
**Requirement**: `TR-zi-018`, `TR-auth-003` (link part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3)
**ADR Decision Summary**: The gateway listens for zones on `127.0.0.1:GATEWAY_ZONE_LINK_PORT` (7900). Each zone keeps one TCP connection, opened at S10. A `RegisterZone` for a ZoneID that already has a link replaces it (the older link is closed). Frames larger than `GATEWAY_LINK_MAX_FRAME_BYTES` close the link. A zone with no link is unroutable.

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API. Use `System.Net.Sockets` / `TcpListener` or Kestrel's connection middleware — implementer's choice, stated in the story completion notes.

**Control Manifest Rules (this layer)**:
- Required: loopback only (`127.0.0.1:GATEWAY_ZONE_LINK_PORT`) — ADR-016 Decision 1
- Required: newer `RegisterZone` for the same ZoneID replaces the older link — ADR-016 Decision 3
- Required: knobs from config (`GATEWAY_ZONE_LINK_PORT`, `GATEWAY_LINK_MAX_FRAME_BYTES`) — never hardcoded

---

## Acceptance Criteria

- [x] A hosted service listens on `127.0.0.1:GATEWAY_ZONE_LINK_PORT` only (not `0.0.0.0`); `GATEWAY_LINK_MAX_FRAME_BYTES` (65 536) is added to `GatewayOptions` + `appsettings.json` and validated at startup (> 0, else the host fails to start). `GATEWAY_LINK_INBOX_CAPACITY` is zone-side only (ADR-016 knob table) and is not added to the gateway config — Story 010
- [x] A connection's first frame must be `RegisterZone`; any other first frame closes it. After `RegisterZone` the connection is that zone's link and its messages are delivered to a registered handler (`IZoneLinkEvents`: registered, message received, link lost)
- [x] A second `RegisterZone` for a ZoneID that already has a live link closes the older connection and keeps the newer one; the older one's loss is not reported as the zone's link loss
- [x] Peer close, read error, oversized frame, zero-length frame, unknown type or malformed body closes that link and reports link lost for its ZoneID exactly once
- [x] Outbound: a thread-safe `SendAsync(zoneId, message)` writes whole frames (no interleaving between concurrent senders); sending to a zone with no link returns false without throwing
- [x] `IsLinkUp(zoneId)` is true only between `RegisterZone` and link loss
- [x] Stopping the host closes every link and the listener within 2 s
- [x] Tests use real loopback sockets with a fake zone client (no Unity)

---

## Implementation Notes

*Derived from ADR-016 Decision 3:*

- Codec: Story 001 (`TryReadFrame`, encode). Read into a buffer, decode frames as they complete.
- One read loop per connection (async); writes serialized per connection (`SemaphoreSlim(1)` or a `Channel<T>` + writer loop).
- Log zone ID, remote endpoint and the reason on every link close; never log tokens.
- The events interface is consumed by Story 004 (replies), Story 005 (map) and Story 006 (routing table). Keep it small.

**Performance**: No frame-budget impact — the gateway is a separate .NET process, off the zone tick (ADR-011); link traffic is control-plane only (registration, admit/evict, player counts). Per-link memory is bounded by `GATEWAY_LINK_MAX_FRAME_BYTES`.

---

## Out of Scope

- Story 004: request/reply correlation, timeouts
- Story 005 / 006: what `RegisterZone`, `PlayerCount`, `ZoneDraining`, `SessionReleased` do to the map and routing table
- Story 010: the zone-side client
- A first-frame timeout for connections that never send `RegisterZone` (loopback-only, low risk) — revisit if Story 009 hardening needs it

---

## QA Test Cases

- **AC-2**: First frame
  - Given: listener running; fake client connects
  - When: it sends `PlayerCount` first → Then the connection is closed, no registration event
  - When: it sends `RegisterZone(zone 7)` → Then a registered event for zone 7; `IsLinkUp(7)` true
- **AC-3**: Replacement
  - Given: zone 7 registered on connection A
  - When: connection B sends `RegisterZone(zone 7)`
  - Then: A is closed by the gateway; B is the link; no link-lost event for zone 7; `IsLinkUp(7)` true
- **AC-4**: Violations
  - Given: registered zone 7
  - When: it sends a frame declaring cap + 1 bytes (or length 0, unknown type, short body)
  - Then: link closed, exactly one link-lost event, `IsLinkUp(7)` false
- **AC-5**: Concurrent sends
  - Given: 50 concurrent `SendAsync` to zone 7
  - Then: fake client decodes 50 whole frames; `SendAsync` to zone 8 (no link) returns false
- **AC-7**: Stop
  - Then: listener and links closed within 2 s; fake client sees EOF

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_LinkListener_tests.cs` — must pass

**Status**: [x] Created — 24 tests, all passing (dotnet, 2026-11-09)

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 004, Story 005, Story 006, Story 009

---

## Completion Notes
**Completed**: 2026-11-09
**Criteria**: 8/8 passing (none deferred)
**Deviations**: Advisory only. `TcpListener` on loopback (not Kestrel). `SendAsync` takes encoded frames (`GatewayLinkCodec.Encode`); typed wrappers are Story 004's call. A second `RegisterZone` on the same link and `RegisterZone` with ZoneID 0 are protocol violations. `StopAsync` reports link-lost for each open link — Story 006/009 may ignore losses during shutdown. Listener is single use. `STOP_TIMEOUT` (2 s) is a constant taken from AC-7, not an ADR-016 knob. Not registered in `GatewayHost` (Story 009). Untested: cancel mid-write (not deterministic without timing); covered by inspection.
**Test Evidence**: Integration: `gateway/IronGrind.Gateway.Tests/Gateway_LinkListener_tests.cs` — 24 tests; CI-filter suite 431/431
**Code Review**: Complete — CHANGES REQUIRED (link-lost raised off the read loop) → fixed (read loop is the single finalizer) → lean re-review APPROVED
