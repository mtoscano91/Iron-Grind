# Authentication

> **Status**: Revised 2026-11-04 for ADR-016 (gateway authentication); lean re-review 2026-11-04 NEEDS REVISION (3 blocking) — all fixes applied the same session (separate character name CR-AUTH-13, token-path ban check, Evict save failure, map lifecycle, ACs 35–38). Before: Approved (lean re-review, 2026-05-21)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-11-04
> **Implements Pillar**: Pillar 1 — Earned Power (account identity is the permanent record of everything the player has earned)

## Overview

Authentication is the server-side identity verification layer that gates all player access to Project Iron Grind. When a client connects, Authentication validates the player's credentials, binds the verified account to a `CharacterID`, and issues a session token that all subsequent server operations reference. No character state is loaded, no zone is entered, and no server-authoritative game operation is permitted until Authentication succeeds. Authentication runs in the gateway process (ADR-016), outside the Unity zone servers: it completes before the client is routed to a zone and has no presence in the 20 Hz tick loop. The credential scheme is username + password (Argon2id, CR-AUTH-5); this GDD specifies the behavioural contract, ADR-016 the process, transport and storage.

## Player Fantasy

Authentication is invisible labor the player only notices when it's gone. Working correctly, it produces nothing the player can name — just the quiet assurance that the world remembers them. The fantasy lives entirely in the negative space: the panic that *doesn't* happen when they log in and their character is intact; the dread that *never* arrives because no one can impersonate them. In a game built on Earned Power, this is the most fundamental promise of all — *you are you, and the world knows it.* Break that, and nothing else the game asks of the player is worth doing.

The specific moment this system exists to protect: the login after a long day, when the loading bar clears and the character is standing exactly where they left them — same gear, same name, same hard-won rank. Nothing announces it. The player simply *arrives*, and the world has held its memory of them. The cost of failure is the inverse of that moment — arriving as no one, or worse, finding someone else has arrived as them.

## Detailed Design

### Core Rules

**CR-AUTH-1 — What "Full Authentication" Means**
Full authentication is the server-side process that (a) verifies a credential pair (username + password) against the account store, (b) resolves the verified `AccountID` to its bound `CharacterID`, and (c) issues a session token. The session is not established, no character state is loaded, and no game operation is permitted until all three steps complete. This is the definition satisfying the "full authentication" reference in `networking-session-token.md` CR-TOK-3/CR-TOK-4.

**CR-AUTH-2 — AccountID Type**
`AccountID` is a `readonly struct` wrapping `uint`. `AccountID(0)` = `AccountID.Invalid` — reserved, never assigned to any account. Requires `IEqualityComparer<AccountID>` (nested `AccountIDComparer` class) for `Dictionary` use under IL2CPP. Consistent pattern with `CharacterID` and `ItemID`. `AccountID` is never transmitted to clients.

**CR-AUTH-3 — AccountID Generation**
`AccountID` values are allocated by a server-side monotonically increasing counter starting at 1. Values are never reused. The counter persists across server restarts in the same store as account records. `AccountID` and `CharacterID` counters are separately persisted and independently allocated — no shared state between them.

**CR-AUTH-4 — Authentication Service Architecture**
Authentication runs in the gateway, `IronGrind.Gateway`: one .NET 10 process, separate from the Unity zone server binaries (ADR-016). Clients reach it over HTTPS (Caddy on 443 terminates TLS and proxies to the gateway on loopback). The gateway owns the accounts table, runs Argon2id, issues the session token, and routes the client to a zone (CR-ZI-6). Zones do no authentication work and have no channel to call it; the gateway talks to zones over the loopback gateway↔zone link (`Admit`, `Evict`, `Reconnect`, `RegisterZone`, …). This separation: (a) permits the full .NET NuGet ecosystem (Argon2id library, no IL2CPP constraint); (b) keeps auth work off every zone tick thread by construction. Concurrent hashing is capped by `MAX_AUTH_PENDING`; a request over the cap gets `ServerError` immediately with no failure count change. *(Replaces the named-pipe sidecar of `auth-sidecar-ipc.md`, superseded 2026-11-04.)*

**CR-AUTH-5 — Password Hashing**
Password hashes use **Argon2id** via `Konscious.Security.Cryptography`. Minimum parameters: `iterations=2`, `memorySize=19456 KB` (19 MiB), `parallelism=1`. Output is stored as a PHC string (self-describing: includes salt, parameters, and hash — no separate salt column). Salt: `RandomNumberGenerator.GetBytes(16)`.

bcrypt is not used: its silent 72-byte password truncation is a semantic defect for arbitrary player passwords.

**CR-AUTH-6 — Password Transmission**
The client sends the plaintext username and password over TLS. The server hashes on receipt. The client MUST NOT hash before transmission — doing so converts the hash into the effective credential, defeating Argon2id and preventing algorithm upgrades. The server MUST reject connections that are not TLS-encrypted.

**CR-AUTH-7 — Username Normalization**
All usernames are stored and compared lowercase. The server normalizes to lowercase before any lookup or storage. Valid characters: `[a-z0-9_]`, length `USERNAME_MIN_LENGTH`–`USERNAME_MAX_LENGTH` (post-normalization). Uniqueness is enforced case-insensitively.

**CR-AUTH-8 — Registration Flow**
1. Client sends `RegistrationRequest { username: string, password: string, characterName: string, classType: byte }` to the gateway over HTTPS (`POST /v1/register`, `auth-wire-messages.md`).
2. Gateway normalizes username. Validates, in this order, returning the first failure: username length [`USERNAME_MIN_LENGTH`, `USERNAME_MAX_LENGTH`] and charset `[a-z0-9_]`; password length [`PASSWORD_MIN_LENGTH`, `PASSWORD_MAX_LENGTH`]; `characterName` per CR-AUTH-13; `classType` is a playable class (`class-system.md`) — unknown class: `RegistrationError.ClassInvalid`. All validation runs before hashing.
3. Computes `Argon2id(password, salt)`, generates PHC string.
4. In **one database transaction**: allocates the next `AccountID` (`account_id_seq`) and `CharacterID` (`character_id_seq`); inserts the account record `{ accountId, username_lower, passwordHashPHC, createdAtUtc, isBanned:false, loginFailureCount:0, lockoutUntilUtc:null }`; inserts the character stub `{ characterId, accountId, characterName (as submitted, case preserved), classType }`, every other column taking its DDL default (the CR-CP-9 Level 1 values). Both rows commit or neither does, before the response (CR-NET-5 pattern).
5. Uniqueness is enforced by unique constraints, never by an application-level pre-check: a violation on `accounts.username_lower` rolls the transaction back and returns `RegistrationError.UsernameTaken`; a violation on the case-insensitive character-name index returns `RegistrationError.CharacterNameTaken` (EC-AUTH-1). `UsernameTaken` tells the requester that a login name exists. That is an accepted tradeoff: login names are private (CR-AUTH-13) and never shown in game, and registration spam is OQ-AUTH-6.
6. Responds `RegistrationResult { success:true }`. No session token on registration — player must log in.
7. On failure: `RegistrationResult { success:false, errorCode:RegistrationError }`.

`RegistrationError : byte { UsernameTaken=0, UsernameTooShort=1, UsernameTooLong=2, UsernameInvalidChars=3, PasswordTooShort=4, PasswordTooLong=5, ClassInvalid=6, CharacterNameTaken=7, CharacterNameTooShort=8, CharacterNameTooLong=9, CharacterNameInvalidChars=10, CharacterNameMatchesUsername=11, ServerError=255 }`

**CR-AUTH-9 — Login Flow**
1. Client sends `ConnectionRequest { username:string, password:string, tokenField:byte[16] }` to the gateway over HTTPS (`POST /v1/login`).
2. If `tokenField` is non-zero: delegate to CR-TOK-4 (`networking-session-token.md`). The gateway resolves `AccountID` from the username; no hash runs and the password is not evaluated. If the account is not found, is banned (`isBanned = true`), or no zone holds a session for it, the gateway returns `InvalidCredentials` and sends nothing to any zone. Otherwise it sends `Reconnect` to the zone holding the session. The lockout state is not checked on this path: the token is the credential, and failed tokens are counted by the zone (`REAUTH_FAILURE_LIMIT`).
3. Server normalizes username. Looks up account record by `username_lower`.
4. **If account not found:** Server MUST still execute the full Argon2id computation against a pre-computed dummy hash generated at gateway startup (same live Argon2id parameters — `iterations`, `memorySize`, `parallelism`) before returning failure (prevents timing-oracle username enumeration). Result is discarded.
5. If `lockoutUntilUtc != null` and `UtcNow < lockoutUntilUtc`: increment `loginFailureCount++`; set `lockoutUntilUtc = UtcNow + LockoutDuration(loginFailureCount)`; return auth failure immediately (no hash computation — counter advances even on the locked path).
6. If `isBanned = true`: return auth failure immediately.
7. Run `Argon2id(password, storedSalt)`, compare to `storedPasswordHashPHC` with `CryptographicOperations.FixedTimeEquals`.
8. On mismatch: increment `loginFailureCount`; apply exponential lockout schedule (CR-AUTH-10); return generic auth failure.
9. On match: reset `loginFailureCount=0`, `lockoutUntilUtc=null`. Proceed to CR-AUTH-11.

**Atomicity:** `loginFailureCount` reads and writes MUST be atomic. The gateway does it in one transaction: `SELECT … FOR UPDATE` on the account row, F-AUTH-1 computed in C#, `UPDATE` (ADR-016). The row lock serializes concurrent attempts on one account. Plain read-modify-write without the lock is not safe.

**All failure reasons return identical response content:** `LoginResponse { success:false, errorCode:AuthFailureReason.InvalidCredentials }` — no hint about which condition fired in the response message. Response timing varies by path: locked and banned paths return immediately (<10ms); credential evaluation runs Argon2id (~300ms). This timing delta is a known design tradeoff for DoS resistance (running Argon2id on locked accounts would expose the gateway to compute exhaustion). See AC-AUTH-16 for the credential-path timing floor and AC-AUTH-19 for the locked-path fast-return requirement.

**Abandoned auth:** If the client aborts the HTTPS request or its `LOGIN_HTTP_TIMEOUT_SECONDS` expires during `Auth_Pending`, the gateway lets the hash finish, then checks whether the request was aborted **before writing anything**. If it was: no `loginFailureCount` change, no reset, no `Evict`, no `Admit` — the result is discarded. This is the only abort check. An abort after it (while waiting for `EvictResult` or `AdmitResult`) is not observed: the failure-count write stands, the login runs to completion, and the unused admission expires in the zone after `CONNECTING_TIMEOUT_SECONDS` (CR-AUTH-11 rollback). See EC-AUTH-2 and EC-AUTH-3.

`AuthFailureReason : byte { None=0, InvalidCredentials=1, ServerError=255 }` — intentionally minimal.

**CR-AUTH-10 — Login Failure Lockout (Exponential Cooldown)**
`LOGIN_FAILURE_LIMIT = 5`. Per-account lockout schedule:

| `loginFailureCount` | Lockout duration |
|---|---|
| 1–4 | None — immediate retry |
| 5 | 30 seconds |
| 6 | 60 seconds |
| 7 | 120 seconds |
| 8 | 300 seconds |
| 9+ | 600 seconds (repeating) |

`loginFailureCount` continues incrementing through lockouts. On successful login: both fields reset to 0/null. Admin tool may clear `loginFailureCount` and `lockoutUntilUtc` for any `AccountID` — the only manual override.

> **Note:** `REAUTH_FAILURE_LIMIT` (defined in `networking-session.md`, default=3) governs failed reconnect token validations after disconnect. It is a distinct constant from `LOGIN_FAILURE_LIMIT`. Do not conflate them.

**CR-AUTH-11 — Session Establishment After Successful Login**
All steps run in the gateway (ADR-016 Decision 4):
1. Resolve `AccountID → CharacterID` from `character_records.account_id`. `CharacterID(0)` or no row: EC-AUTH-5.
2. If the gateway's account → zone map holds a live session for this account, send `Evict` to that zone and wait for `EvictResult` (EC-AUTH-6).
3. Pick the zone by fill-first routing (`zone-instancing.md` CR-ZI-6).
4. Generate a 16-byte session token per CR-TOK-1 and send `Admit { accountId, characterId, token }` to that zone. The zone stores the token in its own `ActiveSessionTokens` (CR-TOK-8) and starts the `CONNECTING_TIMEOUT_SECONDS` clock for the client's arrival.
5. On `AdmitResult.accepted`: reply `LoginResponse { success:true, sessionToken, characterId, zoneId, zoneServerPort }` (`auth-wire-messages.md`). `AccountID` is never a field (CR-AUTH-2). The gateway does not keep the token after replying.
6. The client connects to the zone and sends `SessionHandshake`; the zone validates the token, loads the character (Character Persistence) and admits the player (`zone-instancing.md` CR-ZI-8 steps 2–3). Networking session `Connecting → Connected` (networking-session.md ST-NET-1) happens there, not in the gateway.

**Rollback:** If `Admit` is rejected (zone full or not Active), re-route once (step 3), excluding the zone that rejected; if that also fails, or no `AdmitResult` arrives within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, reply `ServerError`. An admitted entry the client never uses is dropped by the zone after `CONNECTING_TIMEOUT_SECONDS`, which then sends `SessionReleased`. The gateway holds no token, so there is nothing to delete on its side.

**Account → zone map (gateway, in memory):** one entry per account with a session in some zone, value = that zone's `ZoneID`.
- **Added** when `AdmitResult.accepted` arrives (so an admitted client that has not arrived yet counts as a live session for EC-AUTH-6).
- **Removed** when that zone sends `SessionReleased` for the account, when `EvictResult` arrives for it, or when the zone's link is replaced or lost with no re-registration listing the account.
- **Rebuilt** from `RegisterZone.liveAccounts` after a gateway restart or a link reconnect (ADR-016 Decision 3). `liveAccounts` includes admitted-but-not-arrived entries.

**CR-AUTH-12 — 1:1 AccountID to CharacterID Mapping (MVP)**
Each `AccountID` maps to exactly one `CharacterID`, established at registration (CR-AUTH-8 step 4) and immutable at MVP. Account and character are **separate DB records linked by FK** — never merged into a single row. This enables multi-character support post-MVP without schema migration. `CharacterID(0)` in the mapping is a persistence corruption error and must halt character load.

**CR-AUTH-13 — Character Name (separate from the login username)**
The login username is private: it is never shown to other players, never sent in any game message, and never used as a character name. The player picks a separate `characterName` at registration. It is what other players see (nameplates, party, chat — `party-chat.md` `CharacterName`). Rules:
- Charset `[A-Za-z0-9_]` (ASCII only, so byte count = character count); case is preserved for display.
- Length [`CHARACTER_NAME_MIN_LENGTH`, `CHARACTER_NAME_MAX_LENGTH`] characters; the maximum must stay ≤ 26 (`character_name VARCHAR(26)`, ADR-006).
- Unique case-insensitively across all characters (unique index on `lower(character_name)`, migration 003).
- Must not equal the username case-insensitively, so the visible name does not reveal the login name. Otherwise `RegistrationError.CharacterNameMatchesUsername`.
- Failures: `CharacterNameTooShort`, `CharacterNameTooLong`, `CharacterNameInvalidChars` (same non-ASCII handling as EC-AUTH-9), `CharacterNameMatchesUsername`, `CharacterNameTaken`.

Rationale (lean re-review 2026-11-04): with name = username, every login name is public. Combined with CR-AUTH-10's no-decay lockout, any player could keep any visible player locked out by sending one bad login every few minutes, and credential stuffing would only need passwords. Renaming is out of scope at MVP.

---

### States and Transitions

| Auth State | Description | Entry Condition | Exit Condition |
|---|---|---|---|
| `Auth_Idle` | Request received; body not yet decoded | HTTPS `POST /v1/login` or `/v1/register` reaches the gateway | `ConnectionRequest` or `RegistrationRequest` decoded |
| `Auth_Pending` | Credentials received; Argon2id executing | `ConnectionRequest` with zero `tokenField` | Match → `Auth_Success`; mismatch → `Auth_Failed`; request aborted → result discarded (CR-AUTH-9 "Abandoned auth") |
| `Auth_Success` | Credentials verified; CharacterID resolved; zone admitted the token | Argon2id match, then CR-AUTH-11 steps 1–4 succeed | `LoginResponse { success:true }` sent; request ends |
| `Auth_Failed` | Login failed — credential failures return an identical generic response | Mismatch, lockout check, or unknown account (CR-AUTH-9); `ServerError` causes (EC-AUTH-5, EC-AUTH-10) | `LoginResponse { success:false }` sent; request ends |
| `Auth_Registration` | Processing a registration request | `RegistrationRequest` decoded | `RegistrationResult` sent; request ends; no session issued |
| `Auth_TokenPath` | Reconnect via token; delegated entirely to CR-TOK-4 | `ConnectionRequest.tokenField != byte[16]{0}` | Zone accepts `Reconnect` → `LoginResponse` with the rotated token; rejected → `Auth_Failed` |

States are per gateway HTTPS request (ADR-016), not per zone connection. `Connecting → Connected` (networking-session.md ST-NET-1) fires in the zone, when the client's `SessionHandshake` is accepted after `Auth_Success`. `CONNECTING_TIMEOUT_SECONDS` (30s per networking-session.md) bounds the gap between `Admit` and that handshake. The client bounds the login request itself with `LOGIN_HTTP_TIMEOUT_SECONDS` (15s, ADR-016).

---

### Interactions with Other Systems

| System | Direction | Contract |
|---|---|---|
| Gateway (ADR-016) | Runs in | Authentication is the gateway's login and registration handling. Clients reach it over HTTPS; zones never call it. |
| Networking Core | Precedes | The zone's connection path (`OnNewConnection`, `SessionHandshake`) runs only after the gateway's `Auth_Success`; the zone validates the admitted token, it does not authenticate. |
| networking-session-token.md | Delegates to | Auth resolves `AccountID` from credentials and generates the token; the zone that holds the session stores, validates and rotates it (CR-TOK-4/5/8). All token logic is owned by that GDD. Auth does not re-implement it. |
| Character Persistence | Precedes | The character loads at zone admission (`zone-instancing.md` CR-ZI-8 step 3), after the client arrives with the admitted token — not in the gateway. Registration writes the character stub (CR-AUTH-8). `CharacterID(0)` must never be admitted — EC-AUTH-5. |
| Zone Instancing | Exports to | The gateway routes by CR-ZI-6 and sends `Admit (AccountID, CharacterID, token)` to the chosen zone before replying. Zone Instancing admits nothing without a matching admitted token. |
| `IAuthenticationService` | Implements (gateway-internal) | `Task<AuthResult> AuthenticateAsync(ConnectionRequest)`, `Task<RegistrationResult> RegisterAsync(RegistrationRequest)`, `Task<CharacterID> GetCharacterIDAsync(AccountID)`. Gateway code only, not a Unity interface. Hashing runs on the thread pool, never on a request's I/O path. |

```csharp
// IAuthenticationService return types (gateway-internal)
AuthResult {
  Success: bool,
  AccountID: AccountID,       // AccountID.Invalid on failure
  CharacterID: CharacterID,   // CharacterID.Invalid on failure
  FailureReason: AuthFailureReason,
  SessionToken: byte[16]      // zero-filled on failure
}
```

## Formulas

Authentication is an infrastructure system with no runtime game-math. The two formulas below capture design-intent constraints and sizing estimates not obvious from the rules tables alone.

**F-AUTH-1 — Login Failure Lockout Duration (Piecewise Dispatch)**

```
LockoutDuration(F) =
  0                                      if F < LOGIN_FAILURE_LIMIT                         (no lockout)
  LockoutTable[F]                        if LOGIN_FAILURE_LIMIT ≤ F ≤ LOGIN_FAILURE_LIMIT + 4   (escalating)
  LockoutTable[LOGIN_FAILURE_LIMIT + 4]  if F > LOGIN_FAILURE_LIMIT + 4                     (cap, repeating)

LockoutTable = { 5→30s, 6→60s, 7→120s, 8→300s, 9→600s }
  (offset-keyed: key = F; valid range [LOGIN_FAILURE_LIMIT, LOGIN_FAILURE_LIMIT + 4] = [5, 9])

Rule: lockoutUntilUtc is set on EVERY failed attempt (including during an active lockout),
resetting the window to UtcNow + LockoutDuration(new F). There is no "lockout already set, skip update" shortcut.
```

**Variables:**

| Symbol | Type | Range | Description |
|---|---|---|---|
| F | int | [0, unbounded] | `loginFailureCount` on the account record |
| LOGIN_FAILURE_LIMIT | int constant | 5 | Failure count at which first lockout fires |
| LockoutDuration | int | 0 or {30,60,120,300,600} | Seconds added to `UtcNow` to set `lockoutUntilUtc` |

**Output Range:** Discrete set {0, 30, 60, 120, 300, 600} seconds. Capped at 600s for all F ≥ 9 — deliberate policy, not a formula overflow. F continues incrementing through lockouts; the cap fires permanently until successful login resets F to 0.

**Example:** Player at F=7: `lockoutUntilUtc = UtcNow + 120s`. Fails again (F=8): `UtcNow + 300s`. Fails again (F=9): `UtcNow + 600s`. All further failures produce 600s until login resets F.

> **Implementation note:** LockoutTable values are not derivable from a single exponential base (30→60→120 is ×2, but 120→300 is ×2.5). Implement as a switch/lookup, not a computed expression. Recommend 0-indexed offset keying: `key = F - LOGIN_FAILURE_LIMIT`, range [0, 4].

---

**F-AUTH-2 — Account-Character Map Memory Overhead (Informational)**

*Not evaluated at runtime. Documents that the gateway's in-memory account → zone/character map is safe to keep in-process at design-load concurrency.*

```
MapSizeBytes(N) = N × (sizeof(AccountID) + sizeof(CharacterID) + DictionaryOverheadPerEntry)
               = N × (4 + 4 + 24) = N × 32
```

**Variables:**

| Symbol | Type | Range | Description |
|---|---|---|---|
| N | int | [1, 200] | Concurrent sessions across all zones (MVP: 1–4 zones × `MAX_PLAYERS_PER_ZONE` 50, ADR-007). The map value is the `ZoneID` (4 bytes, same size as `CharacterID`) |
| DictionaryOverheadPerEntry | int constant | ~24 bytes | .NET `Dictionary` bucket + pointer overhead per entry |
| MapSizeBytes | int | [32, 6400] bytes | Total map footprint |

**Output Range:** 32 to 6,400 bytes at 4 full zones (1,600 bytes per full zone).

**Example:** N=50 (full zone): 50 × 32 = **1,600 bytes**. The token store (`networking-session-token.md` F-TOK-2, ~3 KB at 50 sessions) lives in each zone, not in the gateway. No in-memory sizing concern.

---

> Token entropy is fully specified in `networking-session-token.md` F-TOK-1 — not re-specified here. Argon2id parameters (`iterations=2`, `memorySize=19456 KB`, `parallelism=1`) are configuration constants defined in CR-AUTH-5 and Tuning Knobs — not runtime calculations.

## Edge Cases

**EC-AUTH-1 — Duplicate registration race (same username or same character name, simultaneous requests)**: The first transaction commits; the second hits the database-level unique constraint on `username_lower` (→ `RegistrationError.UsernameTaken`) or on `lower(character_name)` (→ `CharacterNameTaken`). If both collide, the error is whichever constraint the database reports first. No partial record is written for the loser. Application-level read-then-write uniqueness checks have a TOCTOU window — the DB constraint is the authoritative guard.

**EC-AUTH-2 — Client aborts the request during `Auth_Pending` (Argon2id interrupted)**: The gateway completes the computation, sees the request was aborted, and discards the result before any write. No `loginFailureCount` change, no `Evict`, no `Admit` (CR-AUTH-9 "Abandoned auth"). No zone session exists yet, so nothing else is released. Rationale: completing and discarding is cheaper under attacker control than aborting mid-computation (no retry cost). `MAX_AUTH_PENDING` (gateway hashing semaphore, ADR-016) bounds concurrent in-flight computations and is the primary DoS mitigation against drop-cycling. *(See OQ-AUTH-1 re: network-layer rate limiting as a secondary layer.)*

**EC-AUTH-3 — Client `LOGIN_HTTP_TIMEOUT_SECONDS` (15s) expires during `Auth_Pending`**: The client abandons the request and treats it as `ServerError`; the gateway sees the aborted request and discards the result as in EC-AUTH-2. No `loginFailureCount` change. Client must log in again from scratch. A 15s expiry during normal hashing implies gateway overload or configuration error — neither is a player credential issue.

**EC-AUTH-4 — Persistence write failure during registration**: Gateway returns `RegistrationResult { success:false, errorCode:ServerError=255 }`. The account and character stub are one transaction (CR-AUTH-8 step 4), so neither row exists — there is no orphan account. The `AccountID` and `CharacterID` sequence values are consumed even on rollback and never re-issued. Client must retry as a fresh registration.

**EC-AUTH-5 — `CharacterID(0)` returned from account-character mapping (corruption)**: Gateway sends no `Admit` (so no zone loads the character), logs corruption as a critical error with `AccountID` and timestamp, and the client receives `LoginResponse { success:false, errorCode:ServerError=255 }`. Account requires manual operator remediation.

**EC-AUTH-6 — `CharacterID` already active in a live session (duplicate login / session-stealing)**: Before routing, the gateway sends `Evict` to the zone holding the account's session and waits for `EvictResult` (up to `GATEWAY_EVICT_TIMEOUT_MS`; no reply → `ServerError`). That zone follows networking-session.md's session-stealing protocol: prior session invalidated (token deleted per CR-TOK-6), character state written to persistence, resources released; `EvictResult` follows the save. The new login then proceeds to `Admit` and `Auth_Success`. **If the eviction's save fails**, the new login still proceeds: the old session is already closed either way, so the new zone loads the last committed state (progress since the last successful save is lost), and the old zone logs a critical persistence event. Returning `ServerError` would gain nothing — the next attempt would find no live session and load the same state. If the account's entry is an admission whose client has not arrived, the zone just deletes the entry and token (nothing to save) and answers `EvictResult`. No duplicate session for the same `CharacterID` is permitted. Prior client receives connection close with no reconnect window.

**EC-AUTH-7 — Account banned while player is `Connected` (admin action mid-session)**: `isBanned` is checked at every gateway login request — the credential path (CR-AUTH-9 step 6) and the token path (CR-AUTH-9 step 2, so a banned player's ghost cannot reconnect) — not on every tick. A currently connected session is NOT immediately terminated by the auth system. An explicit admin kick command (`AdminBanKick(AccountID)`) must terminate the in-flight session. Without it, the ban takes effect on the player's next login attempt. Admin kick mechanism is outside auth scope. *(See OQ-AUTH-4.)* See AC-AUTH-33 for the acceptance criterion codifying this intentional two-step policy.

**EC-AUTH-8 — `loginFailureCount` with long inactivity (no decay)**: The counter does not decay over time. A counter of 9 (600s lockout repeating) remains 9 permanently until a successful login resets it to 0. Time-based decay would allow an attacker to brute-force `LOGIN_FAILURE_LIMIT − 1` credentials, wait for decay, and repeat indefinitely. The admin override (CR-AUTH-10) is the only release valve for locked-out legitimate players. **Design position:** No decay — intentional. Admin override is the only release valve at MVP. Self-service account unlock (e.g. email-based) is deferred to post-MVP.

**EC-AUTH-9 — Username passes client validation but fails server (Unicode / multibyte bypass)**: Server normalizes to lowercase via `ToLowerInvariant`, then validates against the `[a-z0-9_]` ASCII-only charset. Any non-ASCII character (including Cyrillic homoglyphs, accented chars, CJK) fails this check regardless of visual similarity. Length is validated by character count post-normalization, not byte count. Result: `RegistrationError.UsernameInvalidChars`. The ASCII whitelist is simpler and more robust than a Unicode denylist.

**EC-AUTH-10 — Infrastructure unavailable (gateway, database, or zone link)**: Three cases, all failing immediately (OQ-AUTH-5 resolved — no queuing):
- **Gateway down:** the client's HTTPS request fails (connection refused, non-200, or `LOGIN_HTTP_TIMEOUT_SECONDS`); the client shows the `ServerError` message. Zones keep serving their current sessions — there is no zone → auth dependency and no startup health gate.
- **Database unavailable to the gateway:** login and registration return `ServerError=255`; `loginFailureCount` is not changed (no write happened); a critical infrastructure event is logged, separate from account-level auth failures.
- **Zone link down, or no `AdmitResult` / `ReconnectResult` within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`:** the login returns `ServerError=255` and a critical infrastructure event is logged. A zone with no link is unroutable until it re-registers. The credential check already completed, so a correct password has reset `loginFailureCount` as normal (CR-AUTH-9 step 9) — the failure is not the player's.

**Degraded gateway (slow but not down):** no short-circuit; the client's `LOGIN_HTTP_TIMEOUT_SECONDS` is the bound, and EC-AUTH-3 applies.

## Dependencies

| Document | Type | Relationship |
|---|---|---|
| ADR-016 (`docs/architecture/ADR-016-gateway-and-authentication-process.md`) | **Architecture** (hard) | Places authentication in the gateway; defines HTTPS transport, the gateway↔zone link, the accounts table (migration 003) and token ownership by the zone. |
| `networking-core.md` | **Upstream** (hard) | Networking Core defines the zone's connection-driven path. CR-NET-2's "authentication" step is now the zone's validation of a gateway-admitted token at `SessionHandshake`; credentials are never checked in the zone. `SESSION_TTL_SECONDS` (300s) and `CONNECTING_TIMEOUT_SECONDS` (30s, the Admit → handshake window) govern session expiry. |
| `networking-session.md` | **Upstream** (hard) | Defines ST-NET-1 session state machine. `Connecting → Connected` fires in the zone when the `SessionHandshake` carrying the token from a gateway `Auth_Success` is accepted (CR-AUTH-11 step 6). `REAUTH_FAILURE_LIMIT` (value=3, reconnect-token path) is owned by this GDD and must not be redefined here — it is distinct from `LOGIN_FAILURE_LIMIT`. |
| `networking-session-token.md` | **Upstream** (hard) | Defines session token generation, storage, validation, and rotation (CR-TOK-1 through CR-TOK-8). Authentication delegates all token logic here. Auth provides `AccountID` (the dictionary key) and the fresh token via `Admit`; the zone's store owns everything else. `F-TOK-2` documents token store memory overhead at MAX_PLAYERS_PER_ZONE. |
| `character-persistence.md` | **Downstream** (hard, Approved) | Owns the full character record whose stub registration creates (`character_records`, FK `account_id` → `accounts`, migration 003). Loads character state at zone admission for the `CharacterID` the gateway admitted. |
| `zone-instancing.md` | **Downstream** (hard, Approved) | The gateway applies Zone Instancing's routing rules (CR-ZI-6) at login and sends `Admit` to the chosen zone; Zone Instancing admits the player at `SessionHandshake` (CR-ZI-8). |

**Bidirectional consistency:**
- `networking-core.md` and `networking-session.md` ST-NET-1 describe the zone connection path as token validation of a gateway login (updated 2026-11-04) ✅
- `networking-session-token.md` uses `AccountID` as its dictionary key but does not define it — this GDD defines it ✅
- `networking-session.md` owns `REAUTH_FAILURE_LIMIT` (reconnect path); this GDD owns `LOGIN_FAILURE_LIMIT` (credential path) — no naming collision ✅
- `character-persistence.md` and `zone-instancing.md` are Approved and reference this GDD; `zone-instancing.md` CR-ZI-6 / CR-ZI-8 updated for the gateway 2026-11-04 ✅

## Tuning Knobs

| Knob | Default | Safe Range | What Breaks if Too Low | What Breaks if Too High |
|---|---|---|---|---|
| `LOGIN_FAILURE_LIMIT` | 5 | [3, 10] | >3 consecutive typos lock out legitimate players | 10+ attempts before any friction — generous gift to credential-stuffing attacks |
| `LockoutTable[5]` (first lockout) | 30s | [15s, 120s] | 15s barely deters automated scripts | 120s at first lockout frustrates legitimate players who mistyped once too many |
| `LockoutTable[9+]` (maximum lockout) | 600s | [300s, 3600s] | 300s — attacker can script around short caps | 3600s — legitimate player with stuck counter is effectively locked out until admin intervenes |
| Argon2id `iterations` | 2 | [2, 4] | < 2: violates OWASP minimum; brute-force resistance degrades | > 4: hash computation exceeds 1–2s per attempt; auth latency becomes noticeable under concurrent logins |
| Argon2id `memorySize` | 19456 KB (19 MiB) | [19456, 65536] | < 19456: violates OWASP minimum | > 65536 (64 MiB): may saturate gateway memory under concurrent logins at MVP server specs (`MAX_AUTH_PENDING` × memorySize) |
| Argon2id `parallelism` | 1 | [1, 2] | — | > 2: no benefit on single-threaded auth path |
| `USERNAME_MIN_LENGTH` | 3 | [2, 6] | 2-char usernames allow squatting of short premium names | 6-char minimum too restrictive for most desired usernames |
| `USERNAME_MAX_LENGTH` | 24 | [12, 32] | 12-char max too tight for memorable login names | > 32: exceeds `accounts.username_lower VARCHAR(32)` — needs a migration |
| `CHARACTER_NAME_MIN_LENGTH` | 3 | [2, 4] | 2-char names allow squatting of very short names | > 4: blocks common short names |
| `CHARACTER_NAME_MAX_LENGTH` | 16 | [12, 26] | < 12: too tight for creative names | > 16: long names crowd nameplates, party frames and chat lines on a phone screen; > 26 exceeds `character_name VARCHAR(26)` |
| `PASSWORD_MIN_LENGTH` | 8 | [6, 12] | 6-char allows weak passwords | 12-char frustrates mobile users on virtual keyboards |
| `PASSWORD_MAX_LENGTH` | 72 | [72, 128] | Do not reduce below 72 — mobile UX choice (virtual keyboard limit), not an Argon2id constraint; Argon2id safe floor is [16, 256] | 128+ functionally uncapped in Argon2id — no concern |
| `MAX_AUTH_PENDING` | 10 | [5, 20] | < 5: bursts of legitimate logins may hit the cap during server load | > 20: gateway hashing memory (`MAX_AUTH_PENDING` × 19 MiB) exceeds its budget (ADR-007 / ADR-016) |

**Joint constraints:**
- `LockoutTable[5]` through `LockoutTable[9+]` must be monotonically increasing — a lockout step must never be shorter than the prior step.
- `LOGIN_FAILURE_LIMIT` and `LockoutTable[9+]` determine brute-force deterrence together; tune as a pair.
- Argon2id `iterations` and `memorySize` must be tuned together — re-derive performance benchmarks on target server hardware when changing either.
- `CHARACTER_NAME_MAX_LENGTH` ≤ 26 (`character_name VARCHAR(26)`, ADR-006). `USERNAME_MAX_LENGTH` ≤ 32 (`accounts.username_lower VARCHAR(32)`, ADR-016). Both are tunable inside their ranges without a migration.
- `CHARACTER_NAME_MIN_LENGTH` < `CHARACTER_NAME_MAX_LENGTH`.
- `LOGIN_HTTP_TIMEOUT_SECONDS` (ADR-016) must exceed hashing + `GATEWAY_EVICT_TIMEOUT_MS` + `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`.

**Cross-system note:** `REAUTH_FAILURE_LIMIT` (value=3, owned by `networking-session.md`) governs the reconnect-token re-auth path. It must not be confused with `LOGIN_FAILURE_LIMIT`.

## Visual/Audio Requirements

Authentication has no visual effects or audio events. The gateway is invisible to the player during successful authentication. The moment of arrival in the zone (loading transition → world) is owned by Zone Instancing, not Authentication. Login and registration screen animations or sounds are UI-layer concerns owned by the screen's UX spec.

## UI Requirements

**Login screen**: Username input field (max 24 chars, ASCII-only keyboard, auto-lowercase on submit), password input field (masked, max 72 chars), login button, link to registration screen.

**Registration screen**: Username input (same constraints as login; labelled as the private login name, with a note that other players never see it), password input (masked), confirm-password input (client-side match check before submit — do not send mismatched passwords to server), character name input (`[A-Za-z0-9_]`, max `CHARACTER_NAME_MAX_LENGTH`, case kept; note that this is the name other players see), class picker (one per playable class, `class-system.md`), register button, link to login screen.

**Error display**: All credential failures (`InvalidCredentials`) show a single generic message "Invalid username or password." — no distinction between wrong password, unknown account, ban or lockout is transmitted to the client. `ServerError` and any transport failure (non-200, no response, `LOGIN_HTTP_TIMEOUT_SECONDS`) show "The server is unavailable. Please try again." Exact strings are owned by the login and registration UX specs.

**Lockout feedback**: No lockout duration is transmitted to the client (by design — the response is always generic). The client shows "Please try again later." A local retry cooldown on the client is a UX decision outside this GDD's scope. **Design position (no-duration exposure):** intentional — lockout state and remaining duration are not exposed to the client. Client-side retry logic MUST NOT assume or infer server lockout state from response timing or message content.

**Registration error messages**: The server returns `RegistrationError` codes; the client maps each to a user-readable string: `UsernameTaken`, `UsernameTooShort/TooLong`, `UsernameInvalidChars`, `PasswordTooShort`, `PasswordTooLong`, `ClassInvalid`, `CharacterNameTaken`, `CharacterNameTooShort/TooLong`, `CharacterNameInvalidChars`, `CharacterNameMatchesUsername`, `ServerError`.

> **📌 UX Flag — Authentication**: This system has UI requirements (login screen, registration screen). In Phase 4 (Pre-Production), run `/ux-design` to create UX specs for these screens before writing implementation epics. Stories that reference the login or registration UI should cite `design/ux/login-screen.md` and `design/ux/registration-screen.md`, not this GDD directly.

## Acceptance Criteria

**AC-AUTH-01** [Integration] BLOCKING — Given a client sends a valid `ConnectionRequest` (correct credentials, zero `tokenField`) to the gateway, when the gateway processes it, then all three steps complete before `LoginResponse` is sent: (a) Argon2id credential verification, (b) `AccountID → CharacterID` mapping resolution, (c) session token generation and its `Admit` accepted by the routed zone. Verified against a test database and a fake zone link: the response carries a non-zero token, a valid `CharacterID`, a non-zero `zoneId` and port, and the fake zone received `Admit` with the same token and `CharacterID` before the response was written.

**AC-AUTH-02** [Logic] BLOCKING — Given `AccountID(0)` is used as a dictionary key or operand, when compared via `AccountIDComparer`, then it equals `AccountID.Invalid` and is never produced by the server counter. Unit test: construct `AccountID(0)`, assert `Equals(AccountID.Invalid)` is `true`; assert first counter allocation is 1.

**AC-AUTH-03** [Logic] BLOCKING — Given two `AccountID` instances with the same wrapped `uint`, when compared via `AccountIDComparer`, then `Equals` returns `true` and hash codes match. With different `uint` values: `Equals` returns `false`. Unit test both.

**AC-AUTH-04** [Integration] BLOCKING — Given a freshly migrated test database, when three accounts are registered in order, then the allocated `AccountID`s are strictly monotonically increasing starting at 1. Restart the gateway and register a fourth: its `AccountID` is 4, not 1 (`account_id_seq` is durable).

**AC-AUTH-05a** [Integration] BLOCKING — Given logins are submitted to the gateway while a zone runs its 20 Hz tick with connected players, when the gateway processes Argon2id (~200–400ms each, up to `MAX_AUTH_PENDING` at once) and sends `Admit` to that zone, then the zone tick completes within its 50ms budget throughout. Verified by server-side tick timestamp logs showing no gap exceeding 50ms correlated with login events.

**AC-AUTH-05b** [Infrastructure] PREREQUISITE — Tick timestamp instrumentation must exist and be captured per-frame before AC-AUTH-05a can be verified. This AC gates sprint commitment on AC-AUTH-05a — do not schedule AC-AUTH-05a until instrumentation story is complete.

**AC-AUTH-06** [Logic] BLOCKING — Given a plaintext password is submitted for hashing, when `Argon2id(password, salt)` runs, then the stored PHC string encodes `iterations=2`, `memorySize=19456`, `parallelism=1`, and a 16-byte salt. Unit test: call hashing wrapper; parse PHC string; assert all four fields; assert salt differs across 5 consecutive calls.

**AC-AUTH-07** [Logic] BLOCKING — Given a password is hashed and stored as a PHC string, when verified with the same plaintext, then returns `true`. With a different plaintext: returns `false`. Unit test round-trip.

**AC-AUTH-08** [Security] BLOCKING — Given a client attempts a plaintext (non-TLS) connection to port 443, when Caddy evaluates it, then the connection is closed during the TLS handshake, no request reaches the gateway, and no `LoginResponse` or `RegistrationResult` is emitted; and nothing listens on port 80. Integration test: plaintext TCP to 443 → closed within one round-trip and the gateway request log is empty; TCP connect to 80 → refused.

**AC-AUTH-09** [Security] BLOCKING — Given a client sends `ConnectionRequest` with plaintext credentials over TLS, when the server processes it, then: (a) `account.passwordHashPHC` begins with `$argon2id$`, not the plaintext; (b) server logs contain no substring matching the submitted password. For (b): capture the gateway's log output during the test — GAP-6.

**AC-AUTH-10** [Logic] BLOCKING — Given usernames `"Alice"`, `"ALICE"`, and `"alice"` are submitted, when the server normalizes and looks up each, then all three resolve to the same account record `"alice"`. Unit test: register `"alice"`; login with `"Alice"` and `"ALICE"`; assert both succeed.

**AC-AUTH-11** [Logic] BLOCKING — Given boundary username inputs (2-char, 25-char, `!` char, space, 3-char, 24-char, `alice_0`), when the server validates each, then: 2-char → `UsernameTooShort`; 25-char → `UsernameTooLong`; `!` and space → `UsernameInvalidChars`; 3-char, 24-char, `alice_0` → pass validation. One assertion per boundary.

**AC-AUTH-12** [Integration] BLOCKING — Given a valid `RegistrationRequest`, when registration completes, then: (a) account record is committed to persistence with all required fields before response is sent; (b) stub character record exists linked to `AccountID` by FK, with `character_name` = the request's `characterName` (case preserved), `class_type` = the request's `classType`, and every other column at its DDL default; (c) response is `RegistrationResult { success:true }`; (d) no session token is issued and no zone receives `Admit`.

**AC-AUTH-13** [Logic] BLOCKING — Given a password of length 7 (one below minimum), when submitted to registration, then response is `RegistrationError.PasswordTooShort`. Given length 8: passes validation. Unit test both boundary values.

**AC-AUTH-14** [Integration] BLOCKING — Given `"alice"` is registered, when `"Alice"` is submitted for registration, then response is `RegistrationError.UsernameTaken` and exactly one account record for `"alice"` exists in the DB.

**AC-AUTH-15** [Integration] BLOCKING — Given a `ConnectionRequest` with non-zero `tokenField` and a wrong password, when the gateway receives it, then it delegates to CR-TOK-4 (sends `Reconnect` to the zone holding the session) and does NOT evaluate the credential fields. Verified by asserting the fake zone received `Reconnect`, no hash ran, and `loginFailureCount` is unchanged.

**AC-AUTH-16** [Security] BLOCKING — Given 20 login attempts against a non-existent username and 20 against a known username that fail at Argon2id, when response latencies are measured, then: (a) both paths take ≥150ms per sample (Argon2id runs in both cases); (b) mean latency delta between the two distributions is < 20ms across the 20-sample sets. Assert no sample from the non-existent path completes in < 150ms. The 150ms floor and 20ms delta threshold are named constants (`AUTH_TIMING_FLOOR_MS = 150`, `AUTH_TIMING_DELTA_THRESHOLD_MS = 20`) — define in test config before sprint.

**AC-AUTH-17** [Logic] BLOCKING — Given an account with `loginFailureCount=4`, when a correct login is submitted, then `loginFailureCount=0` and `lockoutUntilUtc=null` on the account record before the response is sent.

**AC-AUTH-18** [Logic] BLOCKING — Given `LockoutDuration(F)` is evaluated for F ∈ {0, 1, 4, 5, 6, 7, 8, 9, 15, 100}, when each is computed, then the results are exactly {0, 0, 0, 30, 60, 120, 300, 600, 600, 600} seconds respectively. Assert `lockoutUntilUtc = UtcNow + LockoutDuration` within 1-second tolerance.

**AC-AUTH-19** [Logic] BLOCKING — Given an account with `lockoutUntilUtc` in the future, when a login attempt arrives, then: (a) the gateway's handling time for the request (measured in the gateway handler against a local test database, not client round trip) is < 10ms (no Argon2id latency); (b) `loginFailureCount` increments by 1; (c) `lockoutUntilUtc` is reset to `UtcNow + LockoutDuration(new loginFailureCount)`. Assert all three outcomes via account record inspection after the attempt.

**AC-AUTH-20** [Logic] BLOCKING — Given `loginFailureCount=9`, when a failed login is submitted, then `loginFailureCount=10` and `lockoutUntilUtc ≈ UtcNow + 600s`. Repeat with `loginFailureCount=50`: assert `lockoutUntilUtc ≈ UtcNow + 600s` (cap holds).

**AC-AUTH-21** [Integration] BLOCKING — Given a successful login followed by the client's `SessionHandshake` at the routed zone, when session establishment runs, then: (a) the zone's `ActiveSessionTokens[AccountID]` contains the non-zero 16-byte token from `Admit`; (b) `LoginResponse` carries the same token, `CharacterID`, `zoneId` and `zoneServerPort` — assert no `AccountID` field is present (per `auth-wire-messages.md`); (c) the zone's networking session transitions to `Connected` on the handshake; (d) `LoadCharacter(CharacterID)` is called exactly once, by the zone at admission, never by the gateway. Verified via a gateway test with a fake zone link for (a)–(b) and a zone test spy for (c)–(d).

**AC-AUTH-22** [Integration] BLOCKING — Given an MVP account, when `GetCharacterIDAsync(AccountID)` is called, then the result matches the stub created at registration. Account and character are separate DB rows — assert account row contains no character data fields; character row contains `accountId` FK.

**AC-AUTH-23** [Logic] BLOCKING — Given `GetCharacterIDAsync` returns `CharacterID(0)`, when the gateway checks the result, then: no `Admit` is sent (fake zone link count = 0); a critical error is logged with `AccountID` and timestamp; response is `LoginResponse { success:false, errorCode:ServerError=255 }`.

**AC-AUTH-24** [Logic] ADVISORY — Given the gateway's account map is instantiated with 200 entries, when `GC.GetTotalMemory` is measured before and after (with `GC.Collect()` before), then delta does not exceed 12,800 bytes (2× the 6,400-byte formula estimate). Assert across 3 consecutive runs.

**AC-AUTH-25** [Integration] BLOCKING — Given two simultaneous `RegistrationRequest` messages for the same username, when both are processed concurrently, then exactly one account record exists; one response is `success:true`; the other is `RegistrationError.UsernameTaken`.

**AC-AUTH-26** [Integration] BLOCKING — Given a client aborts its login request during `Auth_Pending` (Argon2id executing), once with a wrong password and once with the correct one, when the gateway completes and discards the result, then in both cases `loginFailureCount` and `lockoutUntilUtc` are unchanged from their pre-attempt values and no `Evict` or `Admit` was sent. *(Abort injected through the gateway test host's request-aborted token — GAP-3.)*

**AC-AUTH-27** [Integration] BLOCKING — Given client A is `Connected` in zone Z for account X, when client B submits valid credentials for account X, then: (a) the gateway sends `Evict` to Z and sends no `Admit` before `EvictResult` arrives; (b) client A's session transitions to `Disconnected_SessionExpired` with final state written to persistence before `EvictResult` is sent; (c) client A's connection is closed (no reconnect window) and its token deleted; (d) the routed zone's `ActiveSessionTokens[AccountID X]` holds client B's new token and client B receives `LoginResponse { success:true }`. All four verified via test instrumentation. (e) With the old zone's save forced to fail: `EvictResult` is still sent, client B still receives `LoginResponse { success:true }`, the new zone loads the last committed state, and the old zone logs a critical persistence event (EC-AUTH-6).

**AC-AUTH-28** [Security] BLOCKING — Given username inputs `"аlice"` (Cyrillic U+0430), `"ａlice"` (Fullwidth U+FF41), `"àlice"` (U+00E0), `"日本語"` (CJK), when each is submitted to registration, then all return `RegistrationError.UsernameInvalidChars`. Four separate assertions.

**AC-AUTH-29** [Integration] BLOCKING — Given (i) the gateway's database is unavailable, and separately (ii) the routed zone's link is down or never answers `Admit` within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, when a client attempts login (and, for (i), registration), then: (a) the response is `ServerError=255`; (b) for (i), `loginFailureCount` is unchanged; for (ii), `loginFailureCount` is 0 after a correct password (normal reset) and no zone holds a token for the account; (c) the gateway logs a critical infrastructure event (not an account-level auth failure); (d) with the gateway process stopped, zones with live sessions keep ticking and serving them.

**AC-AUTH-30** [Security] BLOCKING — Given a `LoginResponse` is sent to a client on `Auth_Success`, when the client decodes the body, then it contains no `AccountID` field — verified by asserting the shared codec's `LoginResponse` schema (`auth-wire-messages.md`) has no `accountId` field and the encoded success body is exactly the documented length. *(AccountID is server-internal only per CR-AUTH-2.)*

**AC-AUTH-31** [Integration] BLOCKING — Given a fault is injected after the account insert and before the character stub insert during registration (EC-AUTH-4 path), when the fault fires, then: (a) neither the account row nor the character row exists (one transaction, CR-AUTH-8 step 4); (b) the `AccountID` and `CharacterID` sequence values taken by the failed attempt are not re-issued — the next successful registration gets higher values; (c) the client receives `RegistrationResult { success:false, errorCode:ServerError=255 }`. Requires a fault hook in the gateway's account repository between the two inserts, against a test database.

**AC-AUTH-32** [Integration] BLOCKING — Given the client's `LOGIN_HTTP_TIMEOUT_SECONDS` expires while a login is in `Auth_Pending` (Argon2id delayed past it), when the client gives up, then: (a) the client reports `ServerError`; (b) `loginFailureCount` on the account record is unchanged from its pre-attempt value; (c) the gateway completes the computation and discards the result, sending no `Admit`. Distinct from AC-AUTH-26 (explicit abort) — this tests the client timeout path. *(Requires controllable Argon2id delay injection in the gateway test host.)*

**AC-AUTH-33** [Logic] BLOCKING — Given an account is banned (`isBanned = true`) while a session for that account is in `Connected` state, when the auth system processes the ban, then the live session is NOT auto-terminated — the player remains connected. The ban takes effect on the next login attempt only. Verified by: (1) establishing a `Connected` session; (2) setting `isBanned = true` on the account record; (3) asserting session remains `Connected` for ≥ 5s with no auto-disconnect event. *(Tests intentional two-step policy: ban + explicit admin kick required; see EC-AUTH-7 and OQ-AUTH-4.)*

**AC-AUTH-34** [Integration] BLOCKING — Given `RegistrationRequest`s with an unknown `classType` (e.g. `0` and `255`), and separately one per playable class, when registration runs, then: unknown → `RegistrationError.ClassInvalid` and no rows written; each playable class → `success:true`, `character_records.class_type` equals the request's `classType`, and `character_name` equals the request's `characterName`. *(Added 2026-11-04, ADR-016.)*

**AC-AUTH-35** [Integration] BLOCKING — Given registrations with these `characterName`s (username `"alice_login"`, defaults 3/16), when each runs, then: 2 chars → `CharacterNameTooShort`; 17 chars → `CharacterNameTooLong`; `"Bad Name"` (space), `"Évan"` and `"аlex"` (Cyrillic U+0430) → `CharacterNameInvalidChars`; `"ALICE_LOGIN"` → `CharacterNameMatchesUsername`; 3 chars, 16 chars and `"Grim_Iron7"` → `success:true` with `character_name` stored exactly as sent; then `"grim_iron7"` for a different username → `CharacterNameTaken`, with exactly one character row named case-insensitively `grim_iron7`. One assertion per case. *(Added 2026-11-04, CR-AUTH-13.)*

**AC-AUTH-36** [Integration] BLOCKING — Given `MAX_AUTH_PENDING` hashes are in flight (Argon2id delayed in the gateway test host), when one more login with a correct password arrives, then it receives `LoginResponse { success:false, errorCode:ServerError=255 }` without hashing, its account's `loginFailureCount` and `lockoutUntilUtc` are unchanged, and no `Admit` is sent; the same holds for one more registration (`RegistrationResult` `ServerError`, no rows written).

**AC-AUTH-37** [Integration] BLOCKING — Given two routable zones where the first choice rejects `Admit` (`AdmitResult.accepted = false`), when a correct login runs, then the gateway sends `Admit` to the second zone and the client receives `LoginResponse { success:true }` for it; given both zones reject, the client receives `ServerError` after exactly two `Admit`s, and the account map has no entry for the account.

**AC-AUTH-38** [Integration] BLOCKING — Given a non-zero `tokenField`, when (i) the username is unknown, (ii) no zone holds a session for the account, and (iii) the account has a live session but `isBanned = true`, then each returns `LoginResponse { success:false, errorCode:InvalidCredentials }`, no `Reconnect` is sent to any zone, and no hash runs. Given a live session and `isBanned = false`, exactly one `Reconnect` is sent to the zone in the account map.

---

### Testability Gaps (flag before sprint commitment)

| Gap | AC affected | Risk | Resolution needed |
|---|---|---|---|
| GAP-1 | AC-AUTH-01, AC-AUTH-05a | MEDIUM | Server-side step-timestamp and tick-timing instrumentation (prerequisite tracked by AC-AUTH-05b) |
| GAP-2 | AC-AUTH-16 | MEDIUM | Define latency floor (≥150ms) as a named constant before sprint |
| GAP-3 | AC-AUTH-26 | MEDIUM | Request-abort injection in the gateway test host (ASP.NET Core test server, request-aborted token) |
| GAP-4 | AC-AUTH-32 | LOW | Argon2id delay injection in the gateway test host (shared with AC-AUTH-36) |
| GAP-5 | AC-AUTH-31 | LOW | Fault hook between the two registration inserts in the gateway account repository |
| GAP-6 | AC-AUTH-09(b) | MEDIUM | Gateway log output capturable in tests (in-memory logger provider) |

## Open Questions

| ID | Question | Blocking? | Escalate To |
|---|---|---|---|
| OQ-AUTH-1 | Should per-IP rate limiting (e.g. in Caddy) be specified for login requests that are aborted during hashing? `loginFailureCount` does not change on abandoned auth — rapid abort cycling is unmitigated by the account lockout system; `MAX_AUTH_PENDING` only bounds it. | No | Architecture / Security review |
| OQ-AUTH-2 | Should the gateway expose a server-side monitor alert when `Auth_Pending` duration exceeds 2s? Would surface Argon2id performance regression before it reaches the client's 15s `LOGIN_HTTP_TIMEOUT_SECONDS`. | No | Monitoring / observability ADR |
| OQ-AUTH-3 | `AccountID` counter advances on failed registration (non-contiguous IDs). Functionally harmless (AccountID is never client-visible). Confirm no admin or analytics requirement changes this. | No | User |
| OQ-AUTH-4 | Should the gateway push a notification (over the zone link) to the zone holding the session when `isBanned` is set (enabling auto-kick), or is the two-step "ban + explicit kick" admin workflow acceptable for MVP? | No | Admin tooling design |
| OQ-AUTH-5 | ~~Should the game server queue `ConnectionRequest` messages for ~5s during a transient sidecar outage and retry, or fail immediately?~~ | — | **RESOLVED 2026-11-04 (ADR-016)** — immediate `ServerError`; there is no sidecar, and the gateway's infrastructure failures are EC-AUTH-10. |
| OQ-AUTH-6 | Should per-IP rate limiting on the registration endpoint be specified? The `AccountID` counter advances on each attempt — sustained registration spam from one IP could exhaust the counter over a very long time horizon (functionally harmless at MVP scale, but a DoS vector on launch). | No | Architecture / Security review |
