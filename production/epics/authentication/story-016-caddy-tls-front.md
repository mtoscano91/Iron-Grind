# Story 016: Caddy TLS-only front

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-6; AC-AUTH-08
**Requirement**: `TR-auth-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 2, Verification item 4), with ADR-007 (hosting)
**ADR Decision Summary**: Caddy listens on 443 only, terminates TLS (Let's Encrypt via the TLS-ALPN challenge, so port 80 stays closed) and reverse-proxies to the gateway on `127.0.0.1:GATEWAY_HTTP_PORT`. A plaintext connection to 443 fails the TLS handshake and is closed by Caddy; nothing listens on 80. Local development runs Caddy with `tls internal`.

**Engine**: none (Caddy 2.x) | **Risk**: LOW
**Engine Notes**: Caddy's automatic HTTP→HTTPS redirect opens port 80 unless disabled — use `auto_https disable_redirects` and pin the ACME challenge to TLS-ALPN (`tls { issuer acme { disable_http_challenge } }`). Check the current Caddy 2.x docs for the exact directive names on the day.

**Performance**: no performance impact — outside the gameplay loop; one TLS-terminated HTTPS request per login attempt.

**Control Manifest Rules (this layer)**:
- Required: Caddy listens on 443 only, terminates TLS (Let's Encrypt via TLS-ALPN) and reverse-proxies to the gateway; nothing listens on port 80 — ADR-016 Decisions 1–2
- Forbidden: TLS in the gateway itself — ADR-016 Alternative 4
- Required: Caddy (latest 2.x) on the server and dev machines — Global approved tools

---

## Acceptance Criteria

- [ ] `tools/caddy/Caddyfile.prod` (domain placeholder, `:443` only, `auto_https disable_redirects`, HTTP-01 challenge disabled, `reverse_proxy 127.0.0.1:8080`) and `tools/caddy/Caddyfile.dev` (`localhost:443`, `tls internal`, same proxy) committed, with `tools/caddy/README.md` (install on Windows/Ubuntu, run, trust the dev root — `caddy trust` needs an elevated shell — which file goes where, and how to start the gateway with request logging for the check run)
- [ ] The proxy forwards `POST /v1/login` and `/v1/register` bodies unchanged (binary) and returns the gateway's status and body: login is proven by the TLS check below; register by sending a 3-byte malformed body (`00 FF FF`) and getting the gateway's 400 back
- [ ] AC-AUTH-08: a plaintext TCP connection to 443 that sends `POST /v1/login HTTP/1.1` is closed by the remote end within 2 s (read to EOF, 2 s timeout); the bytes received are either nothing or exactly Go's fixed rejection (`HTTP/1.0 400 Bad Request` … `Client sent an HTTP request to an HTTPS server.`); any other bytes fail
- [ ] AC-AUTH-08: during that probe the gateway log has no `Request starting` line (gateway run with `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information`, stdout redirected to a file passed as `-GatewayLog`)
- [ ] AC-AUTH-08: a TCP connect to port 80 is refused
- [ ] A TLS `POST /v1/login` through Caddy (dev, trusting the `tls internal` root) with `tools/caddy/fixtures/login-unknown-user.bin` returns 200, `Content-Type: application/octet-stream`, body exactly `00 01` (`LoginResponse` failure, `AuthFailureReason.InvalidCredentials`)
- [ ] `tools/caddy/fixtures/login-unknown-user.bin` committed (a `ConnectionRequest` for a username that is never registered, any valid password, all-zero token) and a gateway test `gateway/IronGrind.Gateway.Tests/CaddyFixture_Decode_tests.cs` asserting `AuthBodyCodec.TryDecode` accepts it
- [ ] `tools/caddy/check-tls-only.ps1 -GatewayLog <path>` runs the checks above (plaintext, gateway log, port 80, TLS login, register forwarding) and exits non-zero on any failure; `check-tls-only.sh` is the same for the VPS, written here and first run in Story 017 (no `nc` in Git Bash on the dev machine)
- [ ] Results of the dev run recorded in `production/qa/evidence/caddy-tls-only-evidence.md`, including the exact bytes Caddy returned on the plaintext probe

---

## Implementation Notes

*Derived from ADR-016 Decision 2:*

- Caddy runs on Go's `net/http`: on plaintext HTTP to a TLS port, the TLS handshake fails and Go writes a fixed `HTTP/1.0 400` "Client sent an HTTP request to an HTTPS server." before closing. Decided at readiness 2026-11-09: this satisfies AC-AUTH-08 (no request reaches the gateway, no auth body is returned); the GDD wording was updated to match. If Caddy instead closes without writing, that also passes.
- Prerequisites for the check run: portable PostgreSQL running with migrations applied (the login path reads `accounts`), gateway on 8080, Caddy dev. An unknown username returns InvalidCredentials before any zone is needed (`UnavailableZoneLink` is never reached).
- Do not commit certificates or ACME account keys. Domain name for prod is a placeholder until the VPS exists.
- The scripts use plain TCP (`System.Net.Sockets.TcpClient` in PowerShell, bash's `/dev/tcp` in bash — no `nc`, so no netcat-variant flags) for the plaintext and port-80 checks and `curl --cacert` / `curl` for the TLS check.
- The gateway has no request logging of its own; the check run enables Kestrel's hosting diagnostics through the environment (`Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information`). No gateway code change.

---

## Out of Scope

- Story 017: VPS deployment and the prod run
- Rate limiting in Caddy (OQ-AUTH-1, OQ-AUTH-6 — open, not MVP)
- Running `check-tls-only.sh` (Story 017, on the VPS)

---

## QA Test Cases

- **AC-1 (AC-AUTH-08)**: Plaintext to 443
  - Setup: Caddy dev + gateway running with hosting diagnostics logging to a file
  - Verify: `check-tls-only.ps1` sends `POST /v1/login HTTP/1.1` in plaintext to `localhost:443` and reads to EOF
  - Pass condition: EOF within 2 s; received bytes empty or exactly Go's 400 rejection; no `Request starting` line in the gateway log during the probe
- **AC-2 (AC-AUTH-08)**: Port 80
  - Verify: TCP connect to `localhost:80`
  - Pass condition: connection refused
- **AC-3**: TLS path works
  - Verify: `curl --cacert <caddy root> -X POST --data-binary @tools/caddy/fixtures/login-unknown-user.bin -H "Content-Type: application/octet-stream" https://localhost/v1/login`
  - Pass condition: HTTP 200, body exactly `00 01`
- **AC-4**: Register forwarding
  - Verify: same call to `/v1/register` with body `00 FF FF`
  - Pass condition: HTTP 400 from the gateway (proves the body was forwarded; no account created)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tools/caddy/check-tls-only.ps1` / `.sh`
- `gateway/IronGrind.Gateway.Tests/CaddyFixture_Decode_tests.cs`
- `production/qa/evidence/caddy-tls-only-evidence.md`

**Status**: [x] Created — dev run PASS 2026-11-09

---

## Dependencies

- Depends on: Story 012 (gateway endpoints)
- Unlocks: Story 017 (which now also holds the iOS device check, ADR-016 Verification 3 — moved from Story 015 on 2026-11-06)

---

## Completion Notes
**Completed**: 2026-11-09
**Criteria**: 9/9 passing (the `.sh` has a syntax check only; its first run is the VPS run in Story 017, by design)
**Deviations**: None blocking. Advisory: implemented by `devops-engineer` (not `network-programmer`; story is config + scripts), verification and dev run finished in the main session after the agent's turn limit; GDD AC-AUTH-08 wording changed at readiness (Go's fixed 400 accepted); Story 017 notes added from code review (VPS `.sh` re-verification, ufw drop vs reject on 80); Caddy's local root was trusted without elevation on the dev machine (README corrected)
**Test Evidence**: `tools/caddy/check-tls-only.ps1` / `.sh`; `gateway/IronGrind.Gateway.Tests/CaddyFixture_Decode_tests.cs` (2 tests); `production/qa/evidence/caddy-tls-only-evidence.md`
**Code Review**: Complete — CHANGES REQUIRED (check b positive control, UTF-8 gateway log) fixed; suggestions 1–4, 6–8 applied; `.gitattributes` suggestion awaiting user decision
