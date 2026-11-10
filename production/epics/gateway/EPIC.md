# Epic: Gateway

> **Layer**: Core
> **GDD**: design/gdd/zone-instancing.md (CR-ZI-1, CR-ZI-6, CR-ZI-8, EC-ZI-1); design/gdd/authentication.md (account → zone map, AC-AUTH-24)
> **Architecture Module**: Gateway process (.NET 10, `gateway/IronGrind.Gateway/`): routing table, zone link, zone launch; zone-side link client in `src/ServerLogic/Gateway/`
> **Status**: Ready
> **Stories**: 12 (2026-11-09) — 11 Ready, 012 Blocked (`SessionHandshake` schema)
> **Created**: 2026-11-09 (`/create-epics gateway`, lean — PR-EPIC skipped). ADR-016 Migration Plan step 7.

## Overview

The gateway is the one .NET 10 process that clients reach (through Caddy) and that every zone process registers with. The Authentication epic built its HTTP endpoints, account store and login logic against three stand-in seams (`IZoneLink` → `UnavailableZoneLink`, `IZoneRouter` → `NoZoneRouter`, an in-memory `IAccountZoneMap`); on the zone side, S10 calls an `IGatewayRegistration` stand-in and the `Admit` / `Evict` / `Reconnect` handlers reply through `LoggingGatewayLinkOutbox`. This epic replaces those stand-ins with the real pieces from ADR-016 Decision 3: the loopback TCP link (gateway listener; zone-side background link thread with a bounded inbox drained by the tick, retry and re-registration on loss, stop at teardown step 2a and `OnDestroy`); the account → zone map with its lifecycle rules (zone-matched removal, link-loss grace, registration replace + `Evict`, late admission, gateway boot wait); fill-first routing with party co-location, admitted entries counting against capacity, the warm spare and `LoginResponse(ServerStarting)`; `ZoneID` allocation from `zone_id_seq`; and the `IZoneLauncher` (production: `/run/irongrind/zone-<id>.env` + `systemctl start irongrind-zone@<id>.service`, template unit with `Restart=no`, polkit rule; dev: `Process.Start` or no-op). It also owns TD-095: the zone-side client entry path (`SessionHandshake` decode into `ZoneEntryAdmission.BeginEntry`, the zone's `SessionTokenStore`, the networking session moving to `Connected`), which Authentication Story 017 needs end to end.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-016: Gateway and Authentication Process (Decision 3; also 1, 5, 6) | Loopback TCP link (`uint32` LE length + `byte` type + payload, `requestId` echoed); messages `RegisterZone`, `PlayerCount`, `ZoneDraining`, `SessionReleased`, `Admit`/`AdmitResult`, `Evict`/`EvictResult`, `Reconnect`/`ReconnectResult`; zone side one named background thread, never touched by the tick; account → zone map rules; warm spare + `ServerStarting`; `zone_id_seq`; `IZoneLauncher`; link codec in `src/ServerLogic/Gateway/Shared/` compiled by both sides | MEDIUM — link thread in the IL2CPP Linux build |
| ADR-007: Hosting Backend (amended by ADR-016) | One Unity process per zone; the gateway holds the routing table and starts zones as `irongrind-zone@<zoneId>` systemd units (`Restart=no`) | HIGH — IL2CPP Linux headless |
| ADR-009: Scene / Zone-Load Management (Amendment 1) | S10 is the real gateway registration (last startup step, after S9 Active); teardown step 2a stops the gateway link | HIGH — scene load APIs (not touched here) |
| ADR-011 / ADR-015 (pattern references) | The tick never blocks on I/O; the link thread follows the `NpgsqlRelayListener` shape (catches and logs its own exceptions, stop signal + socket close + `Join` with timeout) | — |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-zi-001 | CR-ZI-1 — `ZoneTemplateID : byte` (`StartingZone = 0`) and `ZoneID : uint` (0 = Invalid), allocated from a monotonic counter that skips 0 and never reuses a value | ADR-016 Decision 3/5 (`zone_id_seq`; gateway passes `-zoneId`) ✅ |
| TR-zi-005 | CR-ZI-6 — fill-first routing (rules 1–5), party co-location, warm spare + `ServerStarting`, admitted entries reserve capacity, ZoneID from `zone_id_seq` | ADR-016 Decision 3 ✅ |
| TR-zi-018 | S10 `Gateway.RegisterZone(zoneId, port)` — the zone → gateway registration protocol | ADR-016 Decision 3 ✅ |
| TR-auth-003 (link part) | Authentication in the .NET gateway; loopback TCP link to zones (the HTTPS part is done in the Authentication epic) | ADR-016 ✅ |

**Specified by ADR-016 Decision 3 with no TR-ID** (stories trace to the ADR directly): account → zone map lifecycle; link loss, retry (`GATEWAY_LINK_RETRY_SECONDS`) and gateway boot wait; request timeouts (`GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, `GATEWAY_EVICT_TIMEOUT_MS`); frame cap (`GATEWAY_LINK_MAX_FRAME_BYTES`) and inbox capacity (`GATEWAY_LINK_INBOX_CAPACITY`); zone launcher, systemd template unit and polkit rule.

**Acceptance criteria taken over:** AC-ZI-1, 2, 3, 16, 19, 20 (from the Zone Instancing epic); AC-AUTH-24 (map memory, advisory, from the Authentication epic).

**Tech debt owned:** TD-095 (zone-side client entry path) — due before Authentication Story 017.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- The stand-ins are gone from the running composition: `UnavailableZoneLink`, `NoZoneRouter`, the in-memory-only map wiring, `IGatewayRegistration` stand-in, `LoggingGatewayLinkOutbox`
- AC-ZI-1, 2, 3, 16, 19, 20 and AC-AUTH-24 are verified; TD-095 is closed
- All Logic and Integration stories have passing test files (`gateway/IronGrind.Gateway.Tests/` for the gateway, `tests/EditMode/` for the zone side)
- ADR-016 Verification items 1 (Linux zone build link) and 2 (Editor zone ↔ local gateway, Play-mode exit stops the link thread, re-entry re-registers) are recorded — item 1 may wait for the Linux IL2CPP headless build gate, recorded rather than left open silently
- Authentication Story 017 is unblocked

## Stories

*Written 2026-11-09 (`/create-stories gateway`, lean — QL-STORY-READY skipped; QA test cases drafted from the GDD acceptance criteria). User decision: Story 012 is written Blocked until a `/quick-design` session fixes the `SessionHandshake` client→server body in `networking-wire-protocol.md` (proposal in the story).*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Link frame codec | Logic | Complete | ADR-016 |
| 002 | ZoneID allocator | Integration | Complete | ADR-016 |
| 003 | Gateway link listener | Integration | Complete | ADR-016 |
| 004 | Real `IZoneLink` — requests, replies and timeouts | Logic | Complete | ADR-016 |
| 005 | Account → zone map | Logic | Ready | ADR-016 |
| 006 | Routing table and fill-first router | Logic | Ready | ADR-016 |
| 007 | Warm spare and `IZoneLauncher` | Logic | Ready | ADR-016 |
| 008 | Zone launchers and deploy files | Integration | Ready | ADR-016, ADR-007 |
| 009 | Gateway composition | Integration | Ready | ADR-016 |
| 010 | Zone link thread | Integration | Ready | ADR-016, ADR-015 |
| 011 | Zone link wiring | Integration | Ready | ADR-016, ADR-009 |
| 012 | Zone-side client entry path (TD-095) | Integration | Blocked (`SessionHandshake` schema) | ADR-016 |

Order: 001 first; gateway side 002–008 (002 and 006 can start in parallel with 003), zone side 010 in parallel; then 009 and 011; 012 after its schema spec. Each story's `Depends on:` says what must be Done first.

## Next Step

Story 001 Complete 2026-11-09. Next: `/story-readiness production/epics/gateway/story-003-link-listener.md` (002 and 010 also unblocked). Separately, a `/quick-design` session for the `SessionHandshake` body unblocks Story 012.
