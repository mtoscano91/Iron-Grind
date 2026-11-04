# ADR-016: Gateway and Authentication Process

## Status
Proposed

## Date
2026-11-04

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) — zone server and client side only; the gateway is a plain .NET process with no Unity code |
| **Domain** | Networking / Infrastructure |
| **Knowledge Risk** | MEDIUM — the gateway itself carries no engine risk; the zone-side link (`System.Net.Sockets` on a background thread in an IL2CPP Linux headless build) and the client's `UnityWebRequest` HTTPS POST on iOS are stable Unity surfaces but are verified on the target builds before the epic closes |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`; `docs/engine-reference/unity/modules/networking.md`; ADR-004, ADR-006, ADR-007, ADR-009, ADR-011, ADR-013, ADR-015; `docs/registry/architecture.yaml` |
| **Post-Cutoff APIs Used** | None. `UnityWebRequest`, `System.Net.Sockets.TcpClient` and `System.Threading.Thread` are long-standing APIs |
| **Verification Required** | (1) Linux zone build, on whichever scripting backend the server build settles on (open item in `docs/engine-reference/unity/plugins/npgsql-dapper.md`): the gateway link connects to `127.0.0.1:GATEWAY_ZONE_LINK_PORT` and reads on its background thread. (2) Windows Editor zone ↔ local gateway on loopback; exiting Play mode stops the link thread, and entering again re-registers the same ZoneID. (3) iOS build: `UnityWebRequest` POST with a binary body to Caddy over TLS passes App Transport Security with the default certificate validation. (4) A plaintext TCP connection to port 443 is closed by Caddy during the TLS handshake, before any request reaches the gateway (AC-AUTH-08). |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-004 (NGO), ADR-006 (Persistence Layer), ADR-007 (Hosting Backend), ADR-009 (zone startup S10 = gateway registration), ADR-011 (tick-loop async rules), ADR-013 (`RandomNumberGenerator` for tokens) — all Accepted |
| **Enables** | Authentication epic stories; Zone Instancing routing ACs (AC-ZI-1, 2, 3, 16, 19, 20); a gateway epic; the session-epic wiring of logout and ghost TTL release in the zone |
| **Blocks** | Authentication epic (every story); the gateway epic; Zone Instancing routing stories |
| **Ordering Note** | Amends ADR-007 (gateway runtime, zone link transport, `LoginResult` delivery). Supersedes `design/gdd/auth-sidecar-ipc.md`. The GDD changes listed under Migration Plan go in one authoring session after this ADR is Accepted. |

## Context

### Problem Statement
`authentication.md` CR-AUTH-4 places authentication in a .NET sidecar that the Unity game server calls over named pipes (`auth-sidecar-ipc.md`, Draft). ADR-007 places it in a .NET gateway on port 443 that authenticates the client, chooses a zone and returns its port, after which the client connects to the zone over UDP with a session token — so the zone server never receives credentials. The two cannot both be built. Nothing says how a token issued at login reaches the zone that must accept it, there is no accounts table, the Argon2id library is not approved, and the place for code shared between Unity and a non-Unity process is undecided. The Authentication epic cannot be broken into stories, and the zone's `IZoneEntryTokenValidator` and `IGatewayRegistration` stay stand-ins, until this is decided.

### Constraints
- Argon2id must never run on a zone tick thread (CR-AUTH-4 (b), AC-AUTH-05a)
- Credentials travel only over TLS; non-TLS connections are refused (CR-AUTH-6, AC-AUTH-08)
- Session tokens come from `RandomNumberGenerator` (ADR-013); `AccountID` is never sent to a client (CR-AUTH-2)
- The zone's tick must not block on I/O (ADR-011); cross-zone messages use the ADR-015 relay, not the gateway link
- One machine at MVP (ADR-007); the dev machine is Windows, production is Ubuntu
- The iOS client is Unity IL2CPP; App Transport Security requires HTTPS with a valid certificate

### Requirements
- Full authentication = credential check + `AccountID → CharacterID` + token issued, before any game operation (CR-AUTH-1)
- Registration commits the account and the character stub before replying (CR-AUTH-8, AC-AUTH-12)
- Monotonic, never-reused `AccountID` and `CharacterID` counters, persisted (CR-AUTH-3, AC-AUTH-04)
- Atomic login-failure counter (CR-AUTH-9)
- Bounded concurrent hashing (`MAX_AUTH_PENDING`)
- Session stealing: a new login evicts the previous session first (EC-AUTH-6, AC-AUTH-27)
- Fill-first zone routing (zone-instancing CR-ZI-6) at login

## Decision

**The gateway is the authentication service.** One .NET process, `IronGrind.Gateway`, terminates client logins and registrations, runs Argon2id, owns the accounts table and routes the client to a zone. There is no separate sidecar and no auth channel from the zone server. Each zone keeps its own session token store, filled by the gateway before the client is told where to go.

### 1. Gateway process
- .NET 10 (the current LTS; .NET 8 support ends 2026-11-10) console app on ASP.NET Core minimal APIs, in `gateway/IronGrind.Gateway/`. Runs as a systemd service (ADR-007). Listens for clients on `http://127.0.0.1:GATEWAY_HTTP_PORT` only, and for zones on `127.0.0.1:GATEWAY_ZONE_LINK_PORT`.
- Argon2id via `Konscious.Security.Cryptography.Argon2` with CR-AUTH-5 parameters, output as a PHC string. The dummy hash for unknown usernames (CR-AUTH-9 step 4) is computed at gateway startup with the live parameters.
- `MAX_AUTH_PENDING` is a `SemaphoreSlim` around hashing in login and registration. When it is full, the request gets `ServerError` immediately and no failure count changes. Hashing runs on the thread pool (`Task.Run`), never on a request's I/O path.
- The `auth-sidecar-ipc.md` health gate is removed: there is no zone → auth dependency. If the gateway is down, clients cannot log in; zones keep serving their current sessions.

### 2. Client ↔ gateway: HTTPS through Caddy
- Caddy listens on 443 only, terminates TLS (Let's Encrypt via the TLS-ALPN challenge, so port 80 stays closed) and reverse-proxies to the gateway. A plaintext connection to 443 fails the TLS handshake and is closed by Caddy; nothing is listening on 80 (AC-AUTH-08).
- Endpoints: `POST /v1/login` and `POST /v1/register`, `Content-Type: application/octet-stream`. The request body is a `ConnectionRequest` or `RegistrationRequest`. The response body is `LoginResponse` or `RegistrationResult`. Every outcome, including failure, returns HTTP 200 with the result in the body; non-200 means transport or server failure and the client treats it as `ServerError`.
- Body encoding follows `networking-wire-protocol.md` field rules (little-endian integers, length-prefixed UTF-8 strings). The schemas live in shared code (Decision 6) so that client and gateway use one codec.
- `LoginResponse` is a single body carrying what `auth-wire-messages.md` sends as two messages: `success`, `errorCode`, then on success `sessionToken[16]`, `characterId`, `zoneId`, `zoneServerPort` (ADR-007's field). `AccountID` is not a field (CR-AUTH-2, AC-AUTH-30).
- `RegistrationRequest` gains `classType: byte` and `characterName: string`. The character name is chosen separately and must not equal the username (`authentication.md` CR-AUTH-13): login names stay private, because public login names plus the no-decay lockout would let any player lock out any other. *(Amended 2026-11-04 after the authentication.md lean re-review; the first draft used the username as the character name.)*
- The client uses `UnityWebRequest` with default certificate validation and an explicit `timeout` of `LOGIN_HTTP_TIMEOUT_SECONDS`. That value is above the worst-case login: hashing, plus `GATEWAY_EVICT_TIMEOUT_MS`, plus `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`. No `CertificateHandler` that accepts every certificate may ship.
- Client builds never use plain HTTP. Local development runs Caddy with `tls internal`. If the Editor cannot trust that certificate, a dev-only certificate handler for it lives only in `IronGrind.DevHarness`. Player Settings keep HTTP downloads disallowed.

### 3. Gateway ↔ zone link: loopback TCP
- Each zone opens one TCP connection to `127.0.0.1:GATEWAY_ZONE_LINK_PORT` at startup step S10 (ADR-009 Amendment 1) and keeps it. Frames: `uint32 length` (LE) + `byte messageType` + payload. Every request carries a `uint requestId` echoed by its reply.
- Zone side: one dedicated background `Thread` (`IsBackground = true`, named) does connect/read/write. Received messages go into a bounded concurrent inbox (`GATEWAY_LINK_INBOX_CAPACITY`) that the tick drains at its start; replies are queued from the tick and written by the link thread. The tick never touches the socket (ADR-011). The thread follows the ADR-015 listener's shape (`NpgsqlRelayListener`): it catches and logs its own exceptions, and the process never dies from one.
- Stopping: a blocking socket read only returns when the socket is closed, so `Stop()` sets a stop signal, closes the socket from the calling thread, then `Join`s with a timeout. The retry loop waits on that stop signal, never on `Thread.Sleep`. `Stop()` runs in ADR-009 teardown step 2a (next to the relay listener) and from the server host's `OnDestroy`, because leaving Play mode in the Editor does not run the teardown (same as ADR-015).
- Frames larger than `GATEWAY_LINK_MAX_FRAME_BYTES` close the link on either side; it is then re-established as on link loss.
- Messages:

| Direction | Message | Purpose |
|-----------|---------|---------|
| Z → G | `RegisterZone { zoneId, templateId, udpPort, capacity, liveAccounts[] }` | S10 and every reconnect; `liveAccounts` lets a restarted gateway rebuild its account → zone map |
| Z → G | `PlayerCount { count }` | On change; feeds fill-first routing (CR-ZI-6) |
| Z → G | `ZoneDraining {}` | Zone left Active; the gateway stops routing to it |
| Z → G | `SessionReleased { accountId }` | The account's session ended in this zone (logout, ghost TTL, entering-slot reclaim, admission not used in time) |
| G → Z | `Admit { requestId, accountId, characterId, token[16] }` | Before the client is told to come here |
| Z → G | `AdmitResult { requestId, accepted, reason }` | Rejected when full or not Active |
| G → Z | `Evict { requestId, accountId }` | Session stealing: zone runs its session-steal path (save, close, token deleted) |
| Z → G | `EvictResult { requestId }` | Sent after the eviction's save has completed or failed |
| G → Z | `Reconnect { requestId, accountId, token[16] }` | Token-path login |
| Z → G | `ReconnectResult { requestId, accepted, newToken[16] }` | Zone validated and rotated (CR-TOK-4/5) |

- Link loss: the zone keeps its sessions, retries every `GATEWAY_LINK_RETRY_SECONDS` and sends `RegisterZone` again with its live accounts. The gateway marks a zone with no link unroutable. A pending `Admit` or `Reconnect` with no reply within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, or an `Evict` with no reply within `GATEWAY_EVICT_TIMEOUT_MS`, fails the client's login with `ServerError`.
- `ZoneID` comes from the zone's launch arguments as today (`ZoneStartupArgs`). A `RegisterZone` for a ZoneID that already has a link replaces it: the gateway closes the older link and keeps the newer one. Rejecting it instead would lock a zone out after a leaked link, for example one left behind by an Editor Play-mode exit. Spawning a new instance at `ZONE_OPEN_THRESHOLD` stays as ADR-007 describes.
- This link replaces ADR-007's Unix domain socket. It is not used for zone-to-zone messages (ADR-015).

### 4. Session tokens are owned by the zone that holds the session
**Fresh login** (`tokenField` all zeros):
1. Hash check, lockout, ban and failure counting as CR-AUTH-9 and CR-AUTH-10, against the accounts table (Decision 5).
2. Resolve `CharacterID` from `character_records.account_id`. If it is `CharacterID(0)` or missing, fail with `ServerError` and log critical (EC-AUTH-5).
3. If the gateway's account → zone map holds a live session for this account, send `Evict` to that zone and wait for `EvictResult` (EC-AUTH-6).
4. Pick the zone by fill-first routing (CR-ZI-6). Rule 1 (party co-location) is skipped at MVP: the gateway has no party data (`zone-instancing.md` OQ-ZI-9). Rule 2 reads `character_records.last_zone_id`.
5. Generate a 16-byte token with `RandomNumberGenerator` and send `Admit`. The zone calls `SessionTokenStore.IssueToken`, records the expected `(AccountID, CharacterID)` and starts the `CONNECTING_TIMEOUT_SECONDS` clock. If no `SessionHandshake` arrives in time, the zone drops the entry and sends `SessionReleased`.
6. On `AdmitResult.accepted`, reply `LoginResponse` with token, CharacterID, ZoneID and port. On rejection, re-route once; otherwise reply `ServerError`. The gateway does not keep the token after replying.

**Token path** (`tokenField` non-zero): the gateway resolves AccountID from the normalized username, and does not hash. If the account is unknown or banned (`is_banned`), or no zone holds a session for it, it returns `InvalidCredentials`. Otherwise it sends `Reconnect` to that zone; the zone runs `TryValidateAndRotate` and the `REAUTH_FAILURE_LIMIT` count, and the gateway relays the new token.

**Zone leaves Active after Admit**: the zone rejects further `Admit`s, drops admitted entries whose client has not arrived and sends `SessionReleased` for each. A late `SessionHandshake` gets `ZoneFullResponse(ZoneDoesNotExist)` and the client returns to login. `RoutingRedirectMessage` (`auth-wire-messages.md`, 2026-05-29) is retired: zones cannot route.

**Zone entry**: `SessionHandshake` carries CharacterID and the token, never AccountID. The real `IZoneEntryTokenValidator` looks up the admitted entry by CharacterID and compares the token with `CryptographicOperations.FixedTimeEquals` in the local store, without blocking. Character load stays in zone admission (Zone Instancing), so CR-AUTH-11 step 5 ("load character") runs in the zone, after the client arrives.

### 5. Account store
New migration `tools/db/migrations/003_create_accounts.sql`:

```sql
CREATE SEQUENCE account_id_seq   AS BIGINT START 1 MINVALUE 1 MAXVALUE 4294967295 NO CYCLE;
CREATE SEQUENCE character_id_seq AS BIGINT START 1 MINVALUE 1 MAXVALUE 4294967295 NO CYCLE;

CREATE TABLE accounts (
    account_id           BIGINT       PRIMARY KEY,
    username_lower       VARCHAR(32)  NOT NULL UNIQUE,
    password_hash_phc    TEXT         NOT NULL,
    created_at_utc       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    is_banned            BOOLEAN      NOT NULL DEFAULT FALSE,
    login_failure_count  INTEGER      NOT NULL DEFAULT 0,
    lockout_until_utc    TIMESTAMPTZ  NULL
);

ALTER TABLE character_records
    ADD CONSTRAINT fk_character_records_account
    FOREIGN KEY (account_id) REFERENCES accounts (account_id);
CREATE INDEX ix_character_records_account_id ON character_records (account_id);
CREATE UNIQUE INDEX ux_character_records_name_lower ON character_records (lower(character_name));
```

- `VARCHAR(32)` is the top of `USERNAME_MAX_LENGTH`'s safe range, so tuning the knob needs no migration. `CHARACTER_NAME_MAX_LENGTH` must stay ≤ 26 (`character_name VARCHAR(26)`). Dev databases whose existing character names collide case-insensitively must be cleaned before the unique index is created.
- Registration is one transaction: `nextval` both sequences, `INSERT accounts`, `INSERT character_records (character_id, account_id, character_name, class_type)` (`character_name` = the request's `characterName`) with every other column taking its DDL default (the CR-CP-9 Level 1 values). Both rows commit or neither does; this is stronger than EC-AUTH-4's allowed orphan state. Sequence values are consumed even on rollback, which is the never-reissued rule (EC-AUTH-4, AC-AUTH-31 (b)). A unique violation (`23505`) on `username_lower` returns `UsernameTaken`; one on `ux_character_records_name_lower` returns `CharacterNameTaken` (told apart by the violated constraint's name; EC-AUTH-1, AC-AUTH-25, AC-AUTH-35).
- Failure counting is one transaction: `SELECT … FOR UPDATE` on the account row, compute F-AUTH-1 in C#, `UPDATE`. Concurrent attempts on one account are serialized by the row lock (CR-AUTH-9 atomicity).
- The gateway connects as its own role `gateway`: all rights on `accounts` and both sequences, `INSERT` and `SELECT (character_id, account_id, last_zone_id)` on `character_records`, nothing else. `last_zone_id` is read for CR-ZI-6 rule 2 (returning character). Zone roles get no rights on `accounts`.

### 6. Code placement and shared code
- `gateway/IronGrind.Gateway/` (app), `gateway/IronGrind.Gateway.Tests/` (NUnit, `dotnet test`), `gateway/IronGrind.Gateway.sln`. The folder is outside `src/` and `Assets/`, so Unity never compiles it.
- Auth rules (username normalization and validation, F-AUTH-1, the login and registration decision flow, the Argon2id wrapper, the account repository) live in the gateway only. Their tests live in `gateway/IronGrind.Gateway.Tests/` — the test-evidence path for gateway stories, in place of `tests/unit/`.
- Shared source: files that both Unity and the gateway compile live in `src/Foundation/Shared/`, a new folder inside `IronGrind.Foundation` that holds no `.csproj`.
  - `AccountID` and `ZoneID` (now in `src/Foundation/CharacterPersistence/`, namespace `IronGrind.CharacterPersistence`) and `CharacterID` (now in `src/Foundation/Currency/`, namespace `IronGrind.Currency`) move there with their `.meta` files. Their namespaces stay unchanged.
  - The auth body schemas and codecs and the gateway-link message codec also live there.
- `gateway/IronGrind.Shared/` is a small `netstandard2.1` project with `<LangVersion>9.0</LangVersion>`. It compiles those files through `<Compile Include="../../src/Foundation/Shared/**/*.cs" />`, and the gateway references it.
  - That build enforces the rules for shared files: no `UnityEngine`/`Unity.*` reference, nothing above .NET Standard 2.1 (Unity's `apiCompatibilityLevel` 6), C# 9 at most.
  - The gateway itself uses current C#.
- `AccountID` is in Foundation, so the client can see the type. It must still never appear in a client-facing schema (CR-AUTH-2).
- Zone side: `src/ServerLogic/Gateway/` — the link thread and inbox, the real `IGatewayRegistration`, the real `IZoneEntryTokenValidator`, and handlers for `Admit` / `Evict` / `Reconnect`.
- Client side: `src/Client/Auth/` — the HTTPS login and registration client. Screens wait for the login and registration UX specs (`/ux-design`).

### 7. Libraries and tooling
- Gateway only, as NuGet `PackageReference`s (normal NuGet, unlike the Unity DLL delivery for the zone): `Konscious.Security.Cryptography.Argon2`, `Npgsql` (current line for .NET 10), `Dapper`. Exact versions are pinned by the first gateway story and added to `technical-preferences.md` Allowed Libraries.
- The .NET 10 SDK is required on dev machines and CI. CI gets a `dotnet test gateway/IronGrind.Gateway.sln` job beside the Unity test runner.
- Caddy (latest 2.x) on the server and on dev machines (`tls internal` locally).

### Architecture Diagram

```
 iOS client ──HTTPS 443──► Caddy (TLS) ──HTTP 127.0.0.1──► IronGrind.Gateway (.NET 10)
     │                                                        │  Argon2id (MAX_AUTH_PENDING)
     │                                                        │  accounts + sequences ──► PostgreSQL
     │                                                        │  routing table, account → zone map
     │                                                        │
     │                                   loopback TCP link    │ Admit / Evict / Reconnect
     │                                   (one per zone)       ▼
     └──────── NGO UDP (port from LoginResponse) ──────► Zone N (Unity IL2CPP)
                 SessionHandshake(characterId, token)        SessionTokenStore (ActiveSessionTokens)
                                                             IZoneEntryTokenValidator (local, non-blocking)
                                                             character load ──► PostgreSQL
```

### Key Interfaces

```csharp
// gateway — hashing
public interface IPasswordHasher
{
    string Hash(string password);                 // PHC string, 16-byte salt from RandomNumberGenerator
    bool Verify(string password, string phc);     // FixedTimeEquals on the derived hash
}

// gateway — account store (Npgsql + Dapper)
public interface IAccountRepository
{
    Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct);
    Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct);
    Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct); // row lock, F-AUTH-1
    Task ResetFailuresAsync(AccountID account, CancellationToken ct);
    Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct);
}

// gateway — zone link
public interface IZoneLink
{
    Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct);
    Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct);
    Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct);
}

// zone — existing seams, now real
IGatewayRegistration      // S10: opens the link, sends RegisterZone
IZoneEntryTokenValidator  // bool IsValid(in ZoneEntryRequest) — local SessionTokenStore lookup
```

`IAuthenticationService` (authentication.md) is the gateway's internal service over these interfaces; it is no longer a cross-process boundary.

## Alternatives Considered

### Alternative 1: Separate auth sidecar behind the gateway
- **Description**: Gateway does routing; a second .NET process does hashing and accounts, reached over a local pipe.
- **Pros**: Hashing load isolated from routing.
- **Cons**: A third process, a second IPC protocol and a health gate, for no gain at MVP scale; both would be .NET on the same machine.
- **Rejection Reason**: `MAX_AUTH_PENDING` already bounds hashing inside the gateway. Splitting later is a refactor inside one codebase.

### Alternative 2: Sidecar called by each zone server (authentication.md CR-AUTH-4 as written)
- **Description**: The client sends credentials to the zone; the zone forwards them to a sidecar over named pipes.
- **Pros**: No gateway needed for login.
- **Cons**: Credentials would travel over NGO UDP, which has no TLS in this setup; zone routing needs a fixed entry point before the client knows a zone (ADR-007); named pipes from Unity IL2CPP on Linux are an untested path.
- **Rejection Reason**: Breaks CR-AUTH-6 and ADR-007's routing model.

### Alternative 3: Gateway as a second Unity headless build
- **Description**: Build the gateway from the Unity project with no zone scene.
- **Pros**: One toolchain and one test runner.
- **Cons**: An HTTP server inside Unity (`HttpListener` under IL2CPP), Argon2id under IL2CPP, a heavy process and slow rebuilds for a service that needs no engine.
- **Rejection Reason**: User decision 2026-11-04: plain .NET.

### Alternative 4: Raw TLS socket instead of HTTPS
- **Description**: The gateway runs `SslStream` on 443 with wire-protocol frames.
- **Pros**: No proxy.
- **Cons**: TLS socket code on the iOS client and certificate management in the gateway.
- **Rejection Reason**: HTTPS through `UnityWebRequest` is the supported path on iOS; Caddy handles certificates.

### Alternative 5: Gateway owns the tokens; zones ask it on entry
- **Description**: `ActiveSessionTokens` lives in the gateway; the zone validates each `SessionHandshake` by a request to the gateway.
- **Pros**: One token store.
- **Cons**: Every zone entry waits on a cross-process round trip; the zone's existing `SessionTokenStore` and ghost reconnect logic would move.
- **Rejection Reason**: User decision 2026-11-04: the zone keeps the store, and the gateway fills it before replying.

### Alternative 6: PostgreSQL LISTEN/NOTIFY or a Unix domain socket for the gateway ↔ zone link
- **Description**: Reuse the ADR-015 relay, or keep ADR-007's Unix socket.
- **Pros**: No new channel (NOTIFY); as written (Unix socket).
- **Cons**: NOTIFY puts a database round trip on every login and has no request/reply; Unix sockets under Unity on a Windows dev machine are risky.
- **Rejection Reason**: User decision 2026-11-04: loopback TCP.

## Consequences

### Positive
- One login path, one process to secure; the zone never sees passwords.
- The zone's existing `SessionTokenStore`, ghost reconnect and session-steal code stay where they are.
- Accounts and characters have real counters and a real foreign key.
- Zone Instancing's routing ACs and the gateway client have a design to be built against.

### Negative
- A second toolchain (.NET 10 SDK) and a second test runner in CI.
- Shared files are limited to .NET Standard 2.1 / C# 9 with no Unity references.
- Login waits for a gateway → zone round trip (Admit; Evict as well when a session is stolen).
- Several GDD texts change (see Migration Plan); `auth-sidecar-ipc.md` is superseded.

### Risks
- **Gateway is a single point of failure for logins.** Mitigation: systemd restart (ADR-007); zones re-register with live accounts so the account → zone map is rebuilt; play in progress is unaffected.
- **Admit without arrival leaves a token in the zone.** Mitigation: the `CONNECTING_TIMEOUT_SECONDS` clock on the admitted entry, then `SessionReleased`.
- **Evict stalls a login while the old zone saves.** Mitigation: `GATEWAY_EVICT_TIMEOUT_MS`, longer than the save's own timeout so a save that finishes in time is never cut off; on timeout the login fails with `ServerError` and the player retries.
- **Shared-code drift** (a Unity-only API sneaks into `src/Foundation/Shared/`). Mitigation: the gateway build fails; CI runs it on every push.
- **Link thread outlives the zone in the Editor** (Play-mode exit skips teardown; a blocking read ignores timeouts under Mono — ADR-015's finding). Mitigation: `Stop()` closes the socket from the calling thread; `OnDestroy` calls it; the gateway replaces a stale link on re-registration; Verification (2).
- **A malformed or flooding peer.** Mitigation: `GATEWAY_LINK_MAX_FRAME_BYTES` and the bounded inbox.
- **Konscious Argon2 performance on the VPS** differs from the ~300 ms estimate. Mitigation: benchmark at the first hashing story; re-derive `MAX_AUTH_PENDING` and the timeout margin (formula kept from `auth-sidecar-ipc.md`).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| authentication.md | CR-AUTH-1 full authentication | Gateway: hash check → CharacterID → token, then Admit, before replying |
| authentication.md | CR-AUTH-2/3 AccountID, monotonic counters | `account_id_seq`, `character_id_seq`; AccountID never in a client body |
| authentication.md | CR-AUTH-4 auth outside the Unity binary, off the tick | Auth runs in the .NET gateway; zones do no auth work (AC-AUTH-05a holds by construction) |
| authentication.md | CR-AUTH-5 Argon2id | Konscious Argon2 in the gateway, PHC strings |
| authentication.md | CR-AUTH-6 TLS only | Caddy on 443 only; port 80 closed |
| authentication.md | CR-AUTH-8 / CR-AUTH-13 registration | One transaction, account + stub; `classType` and a separate `characterName` (unique, case-insensitive) |
| authentication.md | CR-AUTH-9/10 login, lockout, atomic counter | Row lock + F-AUTH-1 in the gateway; dummy hash at startup |
| authentication.md | CR-AUTH-11 session establishment | Token generated by the gateway, stored by the zone via Admit; load at zone admission |
| authentication.md | EC-AUTH-6 session stealing | Evict before Admit |
| networking-session-token.md | CR-TOK-1, 4, 5, 8 | Token from `RandomNumberGenerator`; store in the zone; reconnect validated and rotated by the zone |
| zone-instancing.md | CR-ZI-6 fill-first routing | Gateway routing table fed by `RegisterZone` / `PlayerCount` / `ZoneDraining` |
| auth-wire-messages.md | ConnectionRequest / LoginResult / RegistrationRequest | HTTPS bodies; `LoginResponse` merges `ConnectionAccepted` + `LoginResult` + `zoneServerPort`; `classType` added |

## Performance Implications
- **CPU**: Argon2id ~200–400 ms per login on gateway thread-pool threads, at most `MAX_AUTH_PENDING` (10) at once. Zone tick: draining the link inbox is a few messages per second at most.
- **Memory**: Gateway ≤ 10 × 19 MiB hashing working set + ~50 MB baseline (ADR-007 estimate). Zone: one socket and one thread.
- **Load Time**: Login adds one loopback round trip (Admit) to the hash time; well inside `CONNECTING_TIMEOUT_SECONDS`.
- **Network**: One HTTPS request per login or registration; no change to UDP gameplay traffic.

## Migration Plan
1. Accept this ADR. Amend ADR-007 (gateway runtime .NET 10, HTTPS via Caddy, loopback TCP link, `LoginResponse`) with a pointer here.
2. One GDD authoring session: `authentication.md` (CR-AUTH-4 rewritten; CR-AUTH-8 `classType` + separate `characterName` (CR-AUTH-13) + one transaction; CR-AUTH-11 step 5 at zone admission; EC-AUTH-10 / AC-AUTH-29 re-targeted to gateway → database or zone-link failure; `CHARACTER_NAME_MAX_LENGTH` ≤ 26 joint constraint) — done 2026-11-04; `auth-wire-messages.md` (HTTPS bodies, `LoginResponse`, `classType`, then Approved); `auth-sidecar-ipc.md` → Superseded by ADR-016; `networking-session-token.md` CR-TOK-8 (filled by Admit); `zone-instancing.md` CR-ZI-6 (routing in the gateway).
3. Install the .NET 10 SDK; add the gateway solution and CI job.
4. Apply migration 003. Dev databases with character rows whose `account_id` has no account: insert matching account rows or truncate first; the FK will refuse otherwise.
5. Move `AccountID`, `CharacterID`, `ZoneID` into `src/Foundation/Shared/` (namespaces unchanged; `.meta` files move with them).
6. `/create-stories authentication`, then a gateway epic for routing and the zone link.

## Validation Criteria
- AC-AUTH-01, 04, 08, 12, 21, 25, 27, 31 pass against the gateway and a test database.
- The verification items in Engine Compatibility pass on the Windows Editor, the IL2CPP Linux zone build and an iOS device.
- A login → UDP zone entry → disconnect → token reconnect round trip works end to end on the VPS (ADR-007 smoke test step 6, extended).
- The gateway build fails if a `src/Foundation/Shared/` file references Unity.

## Related Decisions
- ADR-004 (NGO), ADR-006 (persistence; this ADR adds the accounts table), ADR-007 (amended), ADR-009 (S10 registration), ADR-011 (no blocking on the tick), ADR-013 (token randomness), ADR-015 (cross-zone relay stays separate)
- `design/gdd/authentication.md`, `auth-wire-messages.md`, `auth-sidecar-ipc.md` (superseded), `networking-session-token.md`, `zone-instancing.md`

## Tuning Knobs Introduced
| Knob | Default | Notes |
|------|---------|-------|
| `GATEWAY_HTTP_PORT` | 8080 | Loopback only |
| `GATEWAY_ZONE_LINK_PORT` | 7900 | Loopback only |
| `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` | 2000 | Admit / Reconnect reply wait |
| `GATEWAY_EVICT_TIMEOUT_MS` | 7000 | Evict reply wait; must exceed `PERSISTENCE_WRITE_TIMEOUT_SECONDS` (5 s), because the reply follows the old zone's save |
| `GATEWAY_LINK_RETRY_SECONDS` | 2 | Zone reconnect interval |
| `MAX_AUTH_PENDING` | 10 | Moved from `auth-sidecar-ipc.md` |
| `LOGIN_HTTP_TIMEOUT_SECONDS` | 15 | Client `UnityWebRequest.timeout`; must exceed hashing + `GATEWAY_EVICT_TIMEOUT_MS` + `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` (~9.5 s) |
| `GATEWAY_LINK_MAX_FRAME_BYTES` | 65536 | Larger frame closes the link |
| `GATEWAY_LINK_INBOX_CAPACITY` | 1024 | Zone-side inbox bound; full inbox closes the link |
