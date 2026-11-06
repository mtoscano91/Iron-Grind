# Story 016: Caddy TLS-only front

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-6; AC-AUTH-08
**Requirement**: `TR-auth-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 2, Verification item 4), with ADR-007 (hosting)
**ADR Decision Summary**: Caddy listens on 443 only, terminates TLS (Let's Encrypt via the TLS-ALPN challenge, so port 80 stays closed) and reverse-proxies to the gateway on `127.0.0.1:GATEWAY_HTTP_PORT`. A plaintext connection to 443 fails the TLS handshake and is closed by Caddy; nothing listens on 80. Local development runs Caddy with `tls internal`.

**Engine**: none (Caddy 2.x) | **Risk**: LOW
**Engine Notes**: Caddy's automatic HTTP→HTTPS redirect opens port 80 unless disabled — use `auto_https disable_redirects` and pin the ACME challenge to TLS-ALPN (`tls { issuer acme { disable_http_challenge } }`). Check the current Caddy 2.x docs for the exact directive names on the day.

**Control Manifest Rules (this layer)**:
- Required: Caddy listens on 443 only, terminates TLS (Let's Encrypt via TLS-ALPN) and reverse-proxies to the gateway; nothing listens on port 80 — ADR-016 Decisions 1–2
- Forbidden: TLS in the gateway itself — ADR-016 Alternative 4
- Required: Caddy (latest 2.x) on the server and dev machines — Global approved tools

---

## Acceptance Criteria

- [ ] `tools/caddy/Caddyfile.prod` (domain placeholder, `:443` only, `auto_https disable_redirects`, HTTP-01 challenge disabled, `reverse_proxy 127.0.0.1:8080`) and `tools/caddy/Caddyfile.dev` (`localhost:443`, `tls internal`, same proxy) committed, with `tools/caddy/README.md` (install on Windows/Ubuntu, run, trust the dev root, which file goes where)
- [ ] The proxy forwards `POST /v1/login` and `/v1/register` bodies unchanged (binary) and returns the gateway's status and body
- [ ] AC-AUTH-08: a plaintext TCP connection to 443 that sends an HTTP request is closed within one round trip; the gateway's request log is empty for it; no `LoginResponse` / `RegistrationResult` bytes are returned
- [ ] AC-AUTH-08: a TCP connect to port 80 is refused
- [ ] A TLS `POST /v1/login` through Caddy (dev, trusting the `tls internal` root) reaches the gateway and returns 200 with a `LoginResponse` body
- [ ] A scripted check `tools/caddy/check-tls-only.ps1` (and `.sh` for the VPS) runs the three checks above and exits non-zero on any failure
- [ ] Results of the dev run recorded in `production/qa/evidence/caddy-tls-only-evidence.md`; the VPS run is part of Story 017

---

## Implementation Notes

*Derived from ADR-016 Decision 2:*

- Caddy's default when it sees plaintext HTTP on a TLS port may be to answer with a 400 "Client sent an HTTP request to an HTTPS server" rather than drop — check the actual behaviour. AC-AUTH-08 requires that no gateway response is produced and the gateway sees nothing; if Caddy writes a 400 page, record it and confirm with the user whether that satisfies "closed during the TLS handshake" before marking the AC (a GDD wording fix may be the right answer, not a workaround).
- Do not commit certificates or ACME account keys. Domain name for prod is a placeholder until the VPS exists.
- The scripts use plain TCP (`System.Net.Sockets.TcpClient` in PowerShell, `nc` in bash) for the plaintext and port-80 checks and `curl --cacert` / `curl` for the TLS check.
- The gateway request log: Story 012's logging, or Kestrel's request logging at Information for the check run.

---

## Out of Scope

- Story 017: VPS deployment and the prod run
- Rate limiting in Caddy (OQ-AUTH-1, OQ-AUTH-6 — open, not MVP)

---

## QA Test Cases

- **AC-1 (AC-AUTH-08)**: Plaintext to 443
  - Setup: Caddy dev + gateway running
  - Verify: `check-tls-only.ps1` sends `POST /v1/login HTTP/1.1` in plaintext to `localhost:443`
  - Pass condition: connection closed (or the documented Caddy rejection — see notes) within one round trip; gateway log has no request
- **AC-2 (AC-AUTH-08)**: Port 80
  - Verify: TCP connect to `localhost:80`
  - Pass condition: connection refused
- **AC-3**: TLS path works
  - Verify: `curl --cacert <caddy root> -X POST --data-binary @login.bin -H "Content-Type: application/octet-stream" https://localhost/v1/login`
  - Pass condition: HTTP 200, body decodes as `LoginResponse`

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tools/caddy/check-tls-only.ps1` / `.sh`
- `production/qa/evidence/caddy-tls-only-evidence.md`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 012 (gateway endpoints)
- Unlocks: Story 015 device check, Story 017
