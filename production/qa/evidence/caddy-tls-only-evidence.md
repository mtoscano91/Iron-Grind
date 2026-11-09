# Evidence: Caddy TLS-only front (Authentication Story 016)

> **Status: dev run PASS (2026-11-09).** VPS run is Story 017.
> Gate: Integration (BLOCKING), `production/epics/authentication/story-016-caddy-tls-front.md`.
> Governs: ADR-016 Decision 2 and Verification item 4; AC-AUTH-08; TR-auth-005.

## 1. Setup

| Item | Value |
|------|-------|
| Run date | 2026-11-09 |
| Operator | Claude Code session (orchestration script), for Manuel Toscano |
| OS | Windows 11 Home 10.0.26200 |
| Caddy version | v2.11.9 (`winget install CaddyServer.Caddy`) |
| Caddy config | `tools/caddy/Caddyfile.dev` (`localhost:443`, `bind 127.0.0.1`, `tls internal`) |
| Gateway commit | `f4c7867` + Story 016 working tree (no gateway code change in this story) |
| Gateway run | `dotnet run --project gateway\IronGrind.Gateway -c Release --no-build`, `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information`, stdout to `%LOCALAPPDATA%\IronGrind\gateway-check.log`; `IRONGRIND_GATEWAY_DB` = gateway role, password read from `secrets\gateway.pw` (never printed) |
| PostgreSQL | 16.15 portable, `%LOCALAPPDATA%\IronGrind\pgsql16`, started with `pg_ctl start -w` for the run and stopped after. Migrations already applied (the login path read `accounts` and answered InvalidCredentials) |
| curl version | curl 8.21.0 (Windows) libcurl/8.21.0 Schannel |
| PowerShell version | 5.1.26100.9711 |
| CA root used | `%AppData%\Caddy\pki\authorities\local\root.crt` (also in `Cert:\CurrentUser\Root` as "Caddy Local Authority - 2026 ECC Root") |
| Check script | `tools/caddy/check-tls-only.ps1 -GatewayLog <path>` |

### Commands

```
caddy validate --config tools/caddy/Caddyfile.dev --adapter caddyfile
caddy run --config tools/caddy/Caddyfile.dev --adapter caddyfile
powershell -NoProfile -ExecutionPolicy Bypass -File tools\caddy\check-tls-only.ps1 -GatewayLog "$env:LOCALAPPDATA\IronGrind\gateway-check.log"
```

`caddy validate` on `Caddyfile.dev`: Valid configuration. On `Caddyfile.prod` with `IRONGRIND_DOMAIN=game.example.com`: Valid configuration.

## 2. Results

| Check | Requirement | Result | Notes |
|-------|-------------|--------|-------|
| a | Plaintext `POST /v1/login HTTP/1.1` to 443: EOF within 2 s; bytes are nothing or exactly Go's 400 rejection | **PASS** | 76 bytes, exactly Go's rejection (section 3) |
| b | No `Request starting` line in the gateway log during (a) | **PASS** | 0 log bytes during (a); positive control: 2 `Request starting` lines from (d)/(e) (re-run after code review) |
| c | TCP connect to port 80 is refused | **PASS** | `ConnectionRefused` |
| d | TLS `POST /v1/login` with `login-unknown-user.bin` returns 200, `application/octet-stream`, body `00 01` | **PASS** | curl exit 0, 200, `application/octet-stream`, body `00-01` (InvalidCredentials) |
| e | TLS `POST /v1/register` with `00 FF FF` returns 400 (forwarded) | **PASS** | curl exit 0, 400, empty body |

Script output: `RESULT: all checks PASS`, exit code 0.

## 3. Plaintext probe bytes (check a)

- Received byte count: 76
- Received text: `HTTP/1.0 400 Bad Request\r\n\r\nClient sent an HTTP request to an HTTPS server.\n`
- Received hex: `48-54-54-50-2F-31-2E-30-20-34-30-30-20-42-61-64-20-52-65-71-75-65-73-74-0D-0A-0D-0A-43-6C-69-65-6E-74-20-73-65-6E-74-20-61-6E-20-48-54-54-50-20-72-65-71-75-65-73-74-20-74-6F-20-61-6E-20-48-54-54-50-53-20-73-65-72-76-65-72-2E-0A`
- Caddy version that produced it: v2.11.9

This is the response accepted at readiness (2026-11-09) and written into AC-AUTH-08: the TLS handshake fails, Go writes this fixed reply and closes. It contains no `LoginResponse` / `RegistrationResult` bytes, and the gateway saw nothing (check b).

## 4. Port 80

- `check-tls-only.ps1` check c: PASS (`ConnectionRefused`).
- Listeners on 80/443 with Caddy dev running (`Get-NetTCPConnection -State Listen`): one line, `127.0.0.1:443`, owned by the Caddy process. Nothing on 80.
- `caddy adapt` of both files: dev `"listen":["127.0.0.1:443"]`, prod `"listen":[":443"]`; both `"disable_redirects":true`; prod `"challenges":{"http":{"disabled":true}}`.

## 5. Caddy directive verification

- Directives used: `auto_https disable_redirects`, `https://{$IRONGRIND_DOMAIN}` site address, `tls { issuer acme { disable_http_challenge } }`, `tls internal`, `bind`, `reverse_proxy`.
- Accepted by Caddy v2.11.9 (`caddy validate` and `caddy adapt`, section 4). The adapted JSON confirms the intended effect of each port-80 directive.

## 6. Request log evidence

- Gateway log path: `%LOCALAPPDATA%\IronGrind\gateway-check.log`
- Log bytes added during check (a): 0
- Gateway lines `Request starting` during check (a): 0
- Gateway lines `Request starting` produced by checks d and e: 2 (proves request logging was on, so check b is meaningful):
  - `Request starting HTTP/1.1 POST http://localhost/v1/login - application/octet-stream 58`
  - `Request starting HTTP/1.1 POST http://localhost/v1/register - application/octet-stream 3`
- The log contains no username or password text.

## 7. Script defects found and fixed during the dev run

- `$PSScriptRoot` is empty inside `param()` defaults on Windows PowerShell 5.1: the `-Fixture` default is now resolved in the script body.
- A one-element `@('--ssl-no-revoke')` returned from an `if` unrolled to a string; splatting it passed one character per argument to curl (`curl: option -: is unknown`). Now `[string[]]$revocationArgs = @(if ...)`.
- The script's `Format-Hex` helper was shadowed by the built-in `Format-Hex`; renamed `Format-ByteHex`.
- TCP connect timeout raised to 6 s (`$ConnectTimeoutMs`): Windows retries a refused SYN to a local port for about 2 s per address, and `localhost` resolves to `::1` and `127.0.0.1`. The AC-AUTH-08 EOF window stays 2 s.
- Windows `curl.exe` (Schannel) fails against Caddy's local CA without `--ssl-no-revoke` (no CRL/OCSP); the script adds it for `localhost` only (README section 3).

### After code review (2026-11-09)

- Check b now has a positive control in both scripts: it is reported last and fails unless (d) and (e) add `Request starting` lines, so a silent, wrong or UTF-16 log can no longer pass. The README's gateway start command no longer uses `>` (UTF-16 in Windows PowerShell 5.1); it uses `Start-Process -RedirectStandardOutput`.
- Full dev run repeated after the changes: all five checks PASS, exit 0; b reports "0 log bytes during (a) with 0 'Request starting'; 2 lines from (d)/(e)".
- Negative control: with nothing running and a static log file, b FAILS with "request logging not visible" (and a, d, e fail; c passes).
- A failed write of the plaintext request (Caddy closed first) now counts as "closed with no bytes" in both scripts; the `.sh` ignores SIGPIPE and forces `LC_ALL=C`.

## 8. Out of scope for this record

- VPS run and the production certificate: Story 017 (`tools/caddy/check-tls-only.sh --gateway-log <path> --host <domain>`).
- iOS device check (ADR-016 Verification 3): Story 017.
- Rate limiting at Caddy (OQ-AUTH-1, OQ-AUTH-6): not in MVP.
