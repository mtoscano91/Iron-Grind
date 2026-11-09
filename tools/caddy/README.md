# Caddy TLS-only front

Caddy is the public HTTPS front for the gateway (ADR-016 Decision 2, Authentication Story 016, AC-AUTH-08).
It listens on 443 only, terminates TLS and reverse-proxies to the gateway on `127.0.0.1:8080`.
Nothing listens on port 80. A plaintext connection to 443 is closed by Caddy.

## Files

| File | Purpose |
|------|---------|
| `Caddyfile.prod` | VPS config (Story 017). Domain from `IRONGRIND_DOMAIN`. Let's Encrypt via TLS-ALPN-01. |
| `Caddyfile.dev` | Dev config. `localhost:443`, `tls internal`. |
| `check-tls-only.ps1` | Story 016 checks (plaintext, gateway log, port 80, TLS login, register forwarding). Windows, PowerShell 5.1 or 7. |
| `check-tls-only.sh` | Same checks for the VPS (Story 017). Not run in Story 016. |
| `fixtures/login-unknown-user.bin` | 58-byte `ConnectionRequest` for an unregistered user. Decoded by `CaddyFixture_Decode_tests.cs`. |

Evidence is recorded in `production/qa/evidence/caddy-tls-only-evidence.md`. No certificates, keys or ACME
account data belong in this repository.

## 1. Install Caddy (latest 2.x)

**Windows:**

```
winget install CaddyServer.Caddy
caddy version
```

**Ubuntu** (official apt repository, from Caddy's install page; re-check the commands there before use):

```
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install caddy
caddy version
```

## 2. Validate the configs

Run from the repository root.

```
caddy validate --config tools/caddy/Caddyfile.dev --adapter caddyfile
```

Production (dummy domain for validation only):

```
$env:IRONGRIND_DOMAIN = 'game.example.com'
caddy validate --config tools/caddy/Caddyfile.prod --adapter caddyfile
```

## 3. Trust the dev root (optional for the check script, needed for browsers)

`caddy trust` writes Caddy's local root into the Windows trust store. It talks to a running Caddy's admin API
(`localhost:2019`), so start Caddy first; on its first `caddy run` with a `tls internal` site, Caddy installs the root
itself. On the dev machine (2026-11-09) the root went into the **current user's** Root store
(`Cert:\CurrentUser\Root`, "Caddy Local Authority - … ECC Root") without an elevated shell; Windows may show a
confirmation dialog. The check script does not need it: it passes the root to `curl.exe --cacert`.

```
caddy trust
```

Remove it later with `caddy untrust`.

**Revocation check (Windows `curl.exe`, dev only).** Windows' `curl.exe` uses schannel, which checks certificate
revocation. Caddy's local CA publishes no CRL or OCSP, so `curl.exe` fails against it (exit 35 or 60) with or without
`--cacert`. `check-tls-only.ps1` therefore adds `--ssl-no-revoke` automatically when `-HostName` is `localhost`, or
when `-NoRevocationCheck` is passed. Never use it against the VPS: its Let's Encrypt certificate has a working
revocation endpoint, and the `.sh` script does not need it.

## 4. Where the dev root lives (Windows)

Caddy stores its local CA in its data directory. On Windows that is `%AppData%\Caddy`, and the root certificate is:

```
%AppData%\Caddy\pki\authorities\local\root.crt
```

`check-tls-only.ps1` uses this path as the default for `-CaCert`. Pass `-CaCert` explicitly if yours is elsewhere.
Do not copy `root.crt` into the repository.

## 5. Run Caddy for the dev check

In its own PowerShell window (leave it open; Ctrl+C stops it):

```
caddy run --config tools/caddy/Caddyfile.dev --adapter caddyfile
```

Binding 443 does not need an elevated shell on Windows 10/11 for this config. If it fails with a permission
error, report it rather than running Caddy elevated by default.

## 6. Prerequisites for the check run

1. **Portable PostgreSQL running** (`tools/postgres/README.md`, "Without Docker"). Start it from PowerShell, not from
   a Git Bash window you will close.
2. **Migrations applied** to the `irongrind` database (`tools/db/README.md`). The login path reads `accounts`.
   An unknown username returns `InvalidCredentials` before any zone is needed.
3. **Gateway role connection string** in `IRONGRIND_GATEWAY_DB` for the current shell. It is never committed
   (`tools/db/README.md`, "Gateway role").

## 7. Start the gateway with request logging (for the check run)

The gateway has no request logging of its own. The check run turns on Kestrel's hosting diagnostics through the
environment, which logs `Request starting` for every request. No code change is involved.

PowerShell, from the repository root:

```
$env:IRONGRIND_GATEWAY_DB = '<connection string for the gateway role>'
${env:Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics} = 'Information'
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\IronGrind" | Out-Null
$gw = Start-Process -PassThru -WindowStyle Hidden dotnet `
    -ArgumentList 'run','--project','gateway\IronGrind.Gateway','-c','Release' `
    -RedirectStandardOutput "$env:LOCALAPPDATA\IronGrind\gateway-check.log" `
    -RedirectStandardError "$env:LOCALAPPDATA\IronGrind\gateway-check.err.log"
```

Stdout goes to a file, which is what `-GatewayLog` reads. **The log must be UTF-8 or ASCII.** Do not use `>` or
`Out-File` without `-Encoding utf8` in Windows PowerShell 5.1: they write UTF-16, in which the script cannot find
`Request starting`. (The script's positive control catches this: check b fails with "request logging not visible".)
The child process inherits the two environment variables set above. Leave the gateway running until the check
finishes, then stop it with `Stop-Process -Id $gw.Id` (and any `IronGrind.Gateway` process `dotnet run` started).

## 8. Run the checks

With the gateway and Caddy dev both running:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\caddy\check-tls-only.ps1 -GatewayLog "$env:LOCALAPPDATA\IronGrind\gateway-check.log"
```

Each check prints `PASS` or `FAIL`. The script exits 0 when all pass and 1 otherwise. Copy the output, including the
plaintext bytes printed by check (a), into `production/qa/evidence/caddy-tls-only-evidence.md`.

Optional parameters: `-HostName` (default `localhost`), `-CaCert` (default above), `-Fixture` (default
`tools\caddy\fixtures\login-unknown-user.bin`).

For the VPS (Story 017), on Ubuntu:

```
bash tools/caddy/check-tls-only.sh --gateway-log /path/to/gateway.log --host game.example.com
```

`--cacert` is optional there because the production certificate is publicly trusted. Run it with `bash`, not `sh`:
the probes use bash's `/dev/tcp`. If the VPS firewall drops (rather than rejects) port 80, check c times out and
fails; see Story 017.

## Notes

- Both scripts measure the gateway log before and after the plaintext probe and fail on any new
  `Request starting` line in that window. Check b is reported last: the TLS checks d and e must add
  `Request starting` lines afterwards (positive control), otherwise request logging is not visible and b fails.
  Run each script as a single pass against a gateway nothing else is calling.
- The login fixture is a valid `ConnectionRequest` for `caddy_check_unknown`, which is never registered. It creates
  no account. The register probe (`00 FF FF`) is malformed and is rejected with 400 before the service is called.
