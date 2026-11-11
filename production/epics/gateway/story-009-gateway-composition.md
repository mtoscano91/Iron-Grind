# Story 009: Gateway composition

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-11 (login reaches a zone); `design/gdd/zone-instancing.md` — CR-ZI-6
**Requirement**: `TR-zi-005`, `TR-zi-018`, `TR-auth-003` (link part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decisions 1, 3)
**ADR Decision Summary**: One gateway process serves clients over HTTP (through Caddy) and zones over the loopback link; the routing table, account map, warm spare and launcher run in it. Until the boot wait ends, logins get `ServerStarting`.

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API.

**Control Manifest Rules (this layer)**:
- Required: everything from the manifest "Gateway and authentication (ADR-016)" block; knobs from config
- Forbidden: stand-ins in the running composition once their real types exist

---

## Acceptance Criteria

- [ ] `GatewayHost` registers the real `LinkZoneLink`, `AccountZoneMap`, `FillFirstRouter`, routing table, `WarmSpareKeeper`, `IZoneIdAllocator` and the configured `IZoneLauncher`, and hosts the link listener; `UnavailableZoneLink`, `NoZoneRouter` and `EmptyAccountZoneMap` are deleted (or kept only under test support, with a note)
- [ ] Listener events feed the routing table (Story 006), the map (Story 005), pending requests (Story 004) and the warm spare (Story 007) — one subscription point, documented
- [ ] During the boot wait, `POST /v1/login` with valid credentials returns `LoginResponse(ServerStarting)`; after it, with no routable zone, also `ServerStarting` (and a spare start is requested)
- [ ] End-to-end gateway test (`WebApplicationFactory<Program>` or the Kestrel host, `ZONE_LAUNCHER=none`, test database): a fake zone client connects over loopback and sends `RegisterZone(zone 5, port 7801, capacity 50, [])`; a registered account logs in; the fake zone receives `Admit` with the account, character and a 16-byte token and replies `AdmitResult(accepted)`; the HTTP response is `LoginResponse(success, token, characterId, zoneId 5, port 7801)` with the same token; the map holds account → 5
- [ ] Same setup, fake zone replies `AdmitResult(rejected)` and no other zone exists → `ServerError` (one re-route attempt, nothing to pick)
- [ ] Same setup, fake zone sends `SessionReleased(account)` → map entry gone; a second login sends no `Evict`
- [ ] Startup check fails (and the process exits non-zero) for invalid new options; existing `Gateway_Host_tests` still pass *(code review 2026-11-11: "exits non-zero" is verified by the startup exception from host start — an unhandled `StartAsync` exception ends the process non-zero; no child-process test)*

---

## Implementation Notes

*Derived from ADR-016 Decisions 1 and 3:*

- Follow the existing `GatewayHost.ConfigureServices` pattern and `GatewayStartupCheck`.
- The end-to-end test is `[Category("Database")]` (login reads `accounts`); reuse `AuthTestDatabase`.
- Keep the HTTP endpoints untouched (Authentication Story 012); only the service graph changes.
- *(Added 2026-11-09, Story 007 readiness.)* Fan `IZoneLinkEvents` to `RoutingTable` **before** `WarmSpareKeeper` (the keeper reads updated counts). Schedule `WarmSpareKeeper.Tick()`, `RoutingTable.Sweep()` and `AccountZoneMap.Sweep()` on one timer; pass `options.Value` to `AccountZoneMap`.

---

## Out of Scope

- Story 010 / 011: the real zone side (this story uses a fake zone client)
- Authentication Story 017: real client + real zone end to end

---

## QA Test Cases

- **AC-3**: Boot wait — fake clock or short `GATEWAY_LINK_RETRY_SECONDS` in test config; login before → `ServerStarting`; registered zone after → success
- **AC-4**: Happy path — as stated; assert token equality between `Admit` and `LoginResponse`, and zone/port
- **AC-5**: Rejected — `AdmitResult(rejected)` → `ServerError`; failure count unchanged
- **AC-6**: Release — `SessionReleased` → no `Evict` on next login; Edge: `SessionReleased` from another zone → `Evict` sent on next login

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_Composition_tests.cs` (`[Category("Database")]` for the end-to-end cases) — must pass

**Status**: [x] Created — `Gateway_Composition_tests.cs` (non-Database + 4 Database end-to-end), all passing (dotnet, 2026-11-11)

---

## Dependencies

- Depends on: Stories 002–008
- Unlocks: Story 011 (real zone against the real gateway), Authentication Story 017

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 7/7 passing (all automated; AC-4/5/6 by Database end-to-end tests against `irongrind_auth_test`)
**Deviations** (advisory):
- Boot-wait gap closed: ADR-016 line 102 required `ServerStarting` during the boot wait but nothing enforced it; now checked first in `SessionEstablishment.RunCoreAsync`.
- Boot wait starts when the zone-link listener has bound (`GatewayBootWait.NotStarted` + `Start()` from `ZoneLinkListener.StartAsync`), not at construction — code review; noted in the ADR-016 knob table.
- New knob `GATEWAY_MAINTENANCE_INTERVAL_MS` (default 1000, > 0) for the one maintenance timer; added to the ADR-016 knob table.
- AC-3 spare start comes from the timer's `WarmSpareKeeper.Tick`, not from the login itself.
- End-to-end tests use `WebApplicationFactory` (story allows either); the fake zone uses a real loopback socket and the real codec.
- AC-7 "exits non-zero" verified by the host-start exception, not a child process (AC annotated).
- Story 007 follow-ups S1/S2 applied here (`WarmSpareKeeper`).
- TR-zi-005 / TR-zi-018 have no `tr-registry.yaml` entries (as TR-zi-001).
- `ZoneLinkListener` 3-arg and `SessionEstablishment` 8-arg ctors still accept "no boot wait"; production always passes it.
**Test Evidence**: Integration — `gateway/IronGrind.Gateway.Tests/Gateway_Composition_tests.cs`; gateway suite 648 passed / 1 skipped (non-Database; Unix file-mode test on Windows), Database 51/51
**Code Review**: Complete — CHANGES REQUIRED (boot wait started before the listener bound; hollow throwing-step test; weak AC-3 success test; AC-5 failure count unasserted) → fix all 12 applied (2026-11-11)
