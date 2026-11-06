# Story 017: End-to-end login → zone entry

> **Epic**: Authentication
> **Status**: Ready — cannot start until the gateway epic's link stories are Done (see Dependencies)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-1, CR-AUTH-4 (b), CR-AUTH-11, EC-AUTH-10 (gateway down); AC-AUTH-01, 05a, 05b, 21, 27, 29 (d)
**Requirement**: `TR-auth-001`, `TR-auth-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Validation Criteria, Engine Compatibility verification items 1–2)
**ADR Decision Summary**: The epic closes when AC-AUTH-01, 04, 08, 12, 21, 25, 27, 31 pass against the gateway and a test database; the verification items pass on the Windows Editor, the Linux zone build and an iOS device; and a login → UDP zone entry → disconnect → token reconnect round trip works end to end.

**Engine**: Unity 6.3 LTS + .NET 10 | **Risk**: MEDIUM — the zone's link thread under the server build's scripting backend (Verification 1) and Play-mode exit in the Editor (Verification 2)
**Engine Notes**: Verification 1: Linux zone build connects to `127.0.0.1:GATEWAY_ZONE_LINK_PORT` and reads on its background thread. Verification 2: Windows Editor zone ↔ local gateway; exiting Play mode stops the link thread, entering again re-registers the same ZoneID.

**Control Manifest Rules (this layer)**:
- Required: S10 `Gateway.RegisterZone(zoneId, port)` opens the link and sends `RegisterZone` — ADR-009 Amendment 1, ADR-016
- Guardrail: zone tick within its 50 ms budget while logins are processed — AC-AUTH-05a
- Guardrail: engine behaviour verification-pending items 1–4 — ADR-016 Engine Compatibility

---

## Acceptance Criteria

- [ ] AC-AUTH-01 (real link): a correct login through the gateway, with an Editor zone registered over the real link, returns a non-zero token, valid `CharacterID`, non-zero `zoneId` and the zone's port; the zone received `Admit` with that token before the response (gateway + zone logs with timestamps)
- [ ] AC-AUTH-21 (all parts) end to end: the client connects to the zone with the token, the session is `Connected`, `LoadCharacter` ran once in the zone, the visible name is the character name
- [ ] AC-AUTH-27 end to end: client A in the zone, client B logs in to the same account → A is evicted (saved, closed, no reconnect), B enters
- [ ] AC-AUTH-05b: tick timestamp instrumentation captured per frame on the zone (reuse an existing tick-timing log if one exists; otherwise add a dev-build-only per-tick timestamp log)
- [ ] AC-AUTH-05a: with players connected and `MAX_AUTH_PENDING` logins in flight (scripted), the zone's tick log shows no gap > 50 ms correlated with login events
- [ ] AC-AUTH-29 (d): with the gateway stopped, the zone keeps ticking and serving its connected players (tick log + a client still moving)
- [ ] Round trip: login → UDP zone entry → disconnect → token reconnect works (Editor; and on the VPS when it exists — ADR-007 smoke test step 6)
- [ ] ADR-016 Verification 1 and 2 pass and are recorded
- [ ] Evidence in `production/qa/evidence/auth-end-to-end-evidence.md`; Konscious hash timing on the VPS recorded there when the VPS exists (re-derive `MAX_AUTH_PENDING` if it differs from ~300 ms)

---

## Implementation Notes

*Derived from ADR-016 Validation Criteria:*

- This story wires and verifies; it should add little code beyond instrumentation and a scripted load driver (a small console tool or a gateway test that fires N logins).
- The gateway epic provides the real `IZoneLink`, `IAccountZoneMap`, `IZoneRouter`, the zone's link thread, `IGatewayRegistration` and `IZoneLauncher`. If any of these is not Done, this story stays not started.
- The VPS parts (ADR-007 smoke test, Caddy prod) may be split into a later story if the VPS does not exist when the rest is ready — record the split rather than leaving ACs open silently.
- AC-AUTH-16 end to end (Story 009 code review): the wrong-password path makes one `RecordFailureAsync` database write that the unknown-username path does not, so an end-to-end latency comparison includes it (a few ms locally, under the 20 ms threshold). Story 009's unit timing test uses `ARGON2_MEMORY_SIZE_KB = 65536`; the live-parameter 150 ms floor on the VPS belongs here with TD-093.

---

## Out of Scope

- Login and registration screens (UX specs)
- AC-AUTH-24 (gateway map memory, advisory) — gateway epic

---

## QA Test Cases

- **AC-1 (AC-AUTH-01, 21)**: Real login to zone entry
  - Setup: dev PostgreSQL, gateway, Caddy dev, Editor zone in Play mode, DevHarness client
  - Verify: register, log in, enter the zone
  - Pass condition: player spawned with the character name; gateway log `Admit` timestamp < response timestamp; zone log `LoadCharacter` once
- **AC-2 (AC-AUTH-27)**: Session steal across clients
  - Setup: client A in zone
  - Verify: client B logs in to the same account
  - Pass condition: A disconnected with no reconnect; character saved (DB `updated_at` or equivalent advanced); B in zone
- **AC-3 (AC-AUTH-05a/b)**: Tick budget under login load
  - Setup: 2+ clients connected; load driver sends 10 concurrent logins for 60 s
  - Verify: zone tick log
  - Pass condition: max inter-tick gap ≤ 50 ms during the load window
- **AC-4 (AC-AUTH-29 d)**: Gateway down
  - Verify: stop the gateway process with a client connected
  - Pass condition: client keeps moving for 30 s; zone tick log continuous
- **AC-5 (Verification 2)**: Editor Play-mode exit
  - Verify: exit Play mode, re-enter
  - Pass condition: gateway log shows the old link closed and a `RegisterZone` with the same ZoneID; no orphan link thread (thread list / log)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `production/qa/evidence/auth-end-to-end-evidence.md` (manual runs + logs)
- Any instrumentation added has its own EditMode test

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 012–016; the gateway epic's zone link (both sides), account → zone map, router and `IGatewayRegistration` stories (epic not yet created — ADR-016 Migration Plan step 7)
- Also depends on (no owner yet, found at Story 013 readiness 2026-11-06): the zone-side `SessionHandshake` decode → `ZoneEntryAdmission.BeginEntry`, the zone process's `SessionTokenStore` instance, and the networking `Connected` transition on entry. Assign to the gateway epic or a networking-core story before this story starts.
- Unlocks: Authentication epic Definition of Done
