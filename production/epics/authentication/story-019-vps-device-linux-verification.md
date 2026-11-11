# Story 019: VPS, iOS device and Linux zone build verification

> **Epic**: Authentication
> **Status**: Blocked — the VPS does not exist; Unity 6000.3.10f1 has no Linux build module installed and the server scripting backend (IL2CPP or Mono) is unsettled
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 6 h (excludes VPS provisioning and server build setup)
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — AC-AUTH-08, AC-AUTH-16 (end to end), the login → zone entry round trip on production infrastructure
**Requirement**: `TR-auth-001`, `TR-auth-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Engine Compatibility verification items 1, 3, 4; Validation Criteria); ADR-007 (VPS deployment, smoke test step 6)
**ADR Decision Summary**: ADR-016's verification items must pass on the Linux zone build and an iOS device, and the round trip must work on the VPS. Split from Story 017 at its readiness check, 2026-11-11, because none of these can run before the VPS, the Linux build module and an iOS signing setup exist.

**Engine**: Unity 6.3 LTS + .NET 10 | **Risk**: HIGH — the zone's link thread under the server build's scripting backend (Verification 1), UTP 2.6.0 hostname resolution, iOS ATS
**Engine Notes**: Verification 1: Linux zone build connects to `127.0.0.1:GATEWAY_ZONE_LINK_PORT` and reads on its background thread. Verification 3: `UnityWebRequest` POST with a binary body to Caddy over TLS passes ATS with default certificate validation. Verification 4: a plaintext TCP connection to 443 is closed by Caddy during the TLS handshake. `UnityTransport.SetConnectionData` with a hostname (not an IPv4 string) is unverified on UTP 2.6.0.

**Control Manifest Rules (this layer)**:
- Required: S10 `Gateway.RegisterZone(zoneId, port)` opens the link and sends `RegisterZone` — ADR-009 Amendment 1, ADR-016
- Guardrail: engine behaviour verification-pending items 1, 3, 4 — ADR-016 Engine Compatibility

---

## Acceptance Criteria

- [ ] ADR-016 Verification 1: the Linux zone build connects to `127.0.0.1:GATEWAY_ZONE_LINK_PORT` and reads on its background thread (gateway and zone logs)
- [ ] *(moved from Story 015 via 017)* ADR-016 Verification 3: an iOS device build logs in through Caddy on the VPS over TLS with default certificate validation (Let's Encrypt, or another publicly trusted certificate) and App Transport Security on — `LoginResponse.success = true` logged on the device, no ATS error in the Xcode console; recorded in `production/qa/evidence/auth-client-ios-ats-evidence.md`
- [ ] Caddy prod check (ADR-016 Verification 4, AC-AUTH-08): `bash tools/caddy/check-tls-only.sh --gateway-log <path> --host <domain>` passes checks a–e, plus one deliberate negative per check (e.g. Caddy stopped, wrong host) that makes it fail; the port 80 decision is recorded — `ufw reject 80/tcp`, or a recorded decision that a timeout satisfies AC-AUTH-08
- [ ] Round trip on the VPS (ADR-007 smoke test step 6): login → UDP zone entry → transport drop → token reconnect → `Connected` again with the same character
- [ ] Zone host as a hostname: the client connects to the zone with the domain name passed to `UnityTransport.SetConnectionData`; if it does not, resolve the host with `System.Net.Dns` or set `ZoneHostOverride` to the IP, and record which
- [ ] Konscious hash timing on the VPS with the live parameters recorded (20 runs, mean and p95); `MAX_AUTH_PENDING` re-derived if the mean differs materially from the ~300 ms the ADR assumed (dev machine: mean 68.6 ms, p95 128.5 ms)
- [ ] AC-AUTH-16 end to end on the VPS: the latency gap between the wrong-password and unknown-username paths stays below the 20 ms threshold, including the wrong-password path's `RecordFailureAsync` write; live-parameter 150 ms floor checked (TD-093)
- [ ] Results appended to `production/qa/evidence/auth-end-to-end-evidence.md` (VPS section)

---

## Implementation Notes

- *(from the Story 016 code review, 2026-11-09)* This is the first real run of `check-tls-only.sh` (it has only had `bash -n`). Treat it as re-verification of checks a–e. Port 80: check c requires *refused*; ufw's default deny *drops* packets and makes check c time out (FAIL).
- *(from the Story 009 code review)* The wrong-password path makes one `RecordFailureAsync` database write that the unknown-username path does not, so the end-to-end latency comparison includes it. Story 009's unit timing test uses `ARGON2_MEMORY_SIZE_KB = 65536`; the live-parameter floor belongs here with TD-093.
- *(from the Story 018 code review, 2026-11-11)* The client derives the zone host from the gateway URL, so on the VPS `SetConnectionData` gets a hostname.
- Use Story 017's harness scene, load driver and evidence doc; this story should add no code unless the hostname check fails.

---

## Out of Scope

- Provisioning the VPS (ADR-007)
- Choosing the server scripting backend and installing the Linux build module (Character Persistence epic / ADR-006 amendment; `docs/engine-reference/unity/plugins/npgsql-dapper.md`)
- Login and registration screens (UX specs)

---

## QA Test Cases

- **AC-1 (Verification 1)**: Linux zone ↔ gateway link
  - Setup: gateway and Linux zone build on the VPS
  - Pass condition: gateway log `RegisterZone` from the zone; zone log shows the link read thread receiving
- **AC-2 (Verification 3)**: iOS login over TLS
  - Setup: iOS device build pointed at the VPS domain
  - Pass condition: `LoginResponse.success = true` on the device log; no ATS error in the Xcode console
- **AC-3 (Verification 4 / AC-AUTH-08)**: Caddy TLS-only
  - Pass condition: `check-tls-only.sh` passes a–e; each deliberate negative fails the matching check
- **AC-4 (round trip + hostname)**: VPS round trip
  - Pass condition: client enters the zone via the domain name, survives a transport drop and reconnects with its token
- **AC-5 (timing)**: Konscious timing and AC-AUTH-16
  - Pass condition: timings recorded; wrong-password vs unknown-username gap < 20 ms

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `production/qa/evidence/auth-end-to-end-evidence.md` (VPS section)
- `production/qa/evidence/auth-client-ios-ats-evidence.md` (iOS device run, ADR-016 Verification 3)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 017** (harness scene, load driver, evidence doc); the VPS exists (ADR-007); the Unity Linux build module is installed and the server scripting backend is decided; an iOS device with a signing setup
- Unlocks: Authentication epic Definition of Done
