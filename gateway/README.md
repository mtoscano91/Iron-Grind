# IronGrind gateway

ASP.NET Core (.NET 10) minimal-API process for login and registration (ADR-016). It listens on `http://127.0.0.1:GATEWAY_HTTP_PORT` only; Caddy terminates TLS in front of it.

## Endpoints

- `POST /v1/login` and `POST /v1/register`, `Content-Type: application/octet-stream` (exact), bodies from `IronGrind.Shared` (`AuthBodyCodec`).
- Every authentication outcome, failures included, is HTTP 200 with the result in the body.
- 400 malformed body, 405 wrong method, 413 body over 1024 bytes, 415 wrong or missing content type, 500 unhandled error (empty body, one Critical log entry under `IronGrind.Gateway.Infrastructure`). None of these hash.
- A request the client aborts is discarded after the hash, before any database write.
- Request bodies are never logged.

## Run

```
export IRONGRIND_GATEWAY_DB="Host=127.0.0.1;Port=5432;Database=irongrind;Username=gateway;Password=..."
dotnet run --project gateway/IronGrind.Gateway -c Release
```

Startup fails when `IRONGRIND_GATEWAY_DB` is missing or empty, when it sets `Include Error Detail=true` (database errors would log the failing row, password hash included), or when any option is invalid. Until the gateway epic adds the zone link, a correct login answers `ServerStarting`.

## Configuration

Every knob is a flat key in `appsettings.json`, so the same name works as an environment variable or `--KEY=value` argument. Defaults equal ADR-016 and the authentication GDD.

| Key | Default |
|-----|---------|
| `IRONGRIND_GATEWAY_DB` | required, environment only, never committed |
| `GATEWAY_HTTP_PORT` | 8080 |
| `GATEWAY_ZONE_LINK_PORT` | 7900 |
| `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` | 2000 |
| `GATEWAY_EVICT_TIMEOUT_MS` | 11000 |
| `GATEWAY_LINK_RETRY_SECONDS` | 2 |
| `MAX_AUTH_PENDING` | 10 |
| `LOGIN_FAILURE_LIMIT` | 5 |
| `LOCKOUT_TABLE_SECONDS` | `[30, 60, 120, 300, 600]` |
| `ARGON2_ITERATIONS` / `ARGON2_MEMORY_SIZE_KB` / `ARGON2_PARALLELISM` | 2 / 19456 / 1 |
| `USERNAME_MIN_LENGTH` / `USERNAME_MAX_LENGTH` | 3 / 24 |
| `CHARACTER_NAME_MIN_LENGTH` / `CHARACTER_NAME_MAX_LENGTH` | 3 / 16 |
| `PASSWORD_MIN_LENGTH` / `PASSWORD_MAX_LENGTH` | 8 / 72 |

### LOCKOUT_TABLE_SECONDS override (TD-092)

.NET configuration merges arrays by index. An override that sets only some entries (for example `LOCKOUT_TABLE_SECONDS__0=10`) keeps the remaining entries from `appsettings.json`. **A `LOCKOUT_TABLE_SECONDS` override must supply the full table**, every index (`LOCKOUT_TABLE_SECONDS__0` through `__4` for five entries). The validator rejects a non-monotonic result.

## Tests

```
dotnet test gateway/IronGrind.Gateway.sln -c Release --filter "TestCategory!=Database"
```

Database tests need `IRONGRIND_GATEWAY_DB` and `IRONGRIND_GATEWAY_TEST_ADMIN_DB`; see `tools/db/README.md`.
