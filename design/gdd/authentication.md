# Authentication

> **Status**: Approved
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-21
> **Implements Pillar**: Pillar 1 — Earned Power (account identity is the permanent record of everything the player has earned)

## Overview

Authentication is the server-side identity verification layer that gates all player access to Project Iron Grind. When a client connects, Authentication validates the player's credentials, binds the verified account to a `CharacterID`, and issues a session token that all subsequent server operations reference. No character state is loaded, no zone is entered, and no server-authoritative game operation is permitted until Authentication succeeds. Authentication runs entirely in the server's connection-driven path — it fires before the tick loop and before zone assignment, and has no presence in the 20 Hz tick loop. The specific credential scheme (device token, platform SSO, custom account system) is deferred to an Architecture Decision Record; this GDD specifies the behavioural contract that any credential scheme must satisfy.

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
Authentication runs as a standalone .NET sidecar process, separate from the Unity headless game server binary. The game server calls the auth service via an internal IPC channel (named pipes; see `auth-sidecar-ipc.md` for transport spec, concurrency cap `MAX_AUTH_PENDING`, and health check contract). This separation: (a) permits the full .NET NuGet ecosystem (Argon2id library, no IL2CPP constraint); (b) ensures auth logic never runs on the combat tick thread. The boundary is defined by `IAuthenticationService`.

**CR-AUTH-5 — Password Hashing**
Password hashes use **Argon2id** via `Konscious.Security.Cryptography`. Minimum parameters: `iterations=2`, `memorySize=19456 KB` (19 MiB), `parallelism=1`. Output is stored as a PHC string (self-describing: includes salt, parameters, and hash — no separate salt column). Salt: `RandomNumberGenerator.GetBytes(16)`.

bcrypt is not used: its silent 72-byte password truncation is a semantic defect for arbitrary player passwords.

**CR-AUTH-6 — Password Transmission**
The client sends the plaintext username and password over TLS. The server hashes on receipt. The client MUST NOT hash before transmission — doing so converts the hash into the effective credential, defeating Argon2id and preventing algorithm upgrades. The server MUST reject connections that are not TLS-encrypted.

**CR-AUTH-7 — Username Normalization**
All usernames are stored and compared lowercase. The server normalizes to lowercase before any lookup or storage. Valid characters: `[a-z0-9_]`, length `USERNAME_MIN_LENGTH`–`USERNAME_MAX_LENGTH` (post-normalization). Uniqueness is enforced case-insensitively.

**CR-AUTH-8 — Registration Flow**
1. Client sends `RegistrationRequest { username: string, password: string }` over TLS.
2. Server normalizes username. Validates: length [`USERNAME_MIN_LENGTH`, `USERNAME_MAX_LENGTH`], charset `[a-z0-9_]`, password length [`PASSWORD_MIN_LENGTH`, `PASSWORD_MAX_LENGTH`].
3. Checks username uniqueness (case-insensitive). On conflict: `RegistrationError.UsernameTaken`.
4. Computes `Argon2id(password, salt)`, generates PHC string.
5. Allocates next `AccountID` from server counter. Writes account record to persistence: `{ accountId, username_lower, passwordHashPHC, createdAtUtc, isBanned:false, loginFailureCount:0, lockoutUntilUtc:null }`. Persistence commit before response (CR-NET-5 pattern).
6. Allocates a `CharacterID` (server counter, same pattern). Writes stub character record linked to `AccountID` for Character Persistence to expand.
7. Responds `RegistrationResult { success:true }`. No session token on registration — player must log in.
8. On failure: `RegistrationResult { success:false, errorCode:RegistrationError }`.

`RegistrationError : byte { UsernameTaken=0, UsernameTooShort=1, UsernameTooLong=2, UsernameInvalidChars=3, PasswordTooShort=4, PasswordTooLong=5, ServerError=255 }`

**CR-AUTH-9 — Login Flow**
1. Client sends `ConnectionRequest { username:string, password:string, tokenField:byte[16] }` over TLS.
2. If `tokenField` is non-zero: delegate entirely to CR-TOK-4 (`networking-session-token.md`). Do not evaluate credentials.
3. Server normalizes username. Looks up account record by `username_lower`.
4. **If account not found:** Server MUST still execute the full Argon2id computation against a pre-computed dummy hash generated at sidecar startup (same live Argon2id parameters — `iterations`, `memorySize`, `parallelism`; see `auth-sidecar-ipc.md`) before returning failure (prevents timing-oracle username enumeration). Result is discarded.
5. If `lockoutUntilUtc != null` and `UtcNow < lockoutUntilUtc`: increment `loginFailureCount++`; set `lockoutUntilUtc = UtcNow + LockoutDuration(loginFailureCount)`; return auth failure immediately (no hash computation — counter advances even on the locked path).
6. If `isBanned = true`: return auth failure immediately.
7. Run `Argon2id(password, storedSalt)`, compare to `storedPasswordHashPHC` with `CryptographicOperations.FixedTimeEquals`.
8. On mismatch: increment `loginFailureCount`; apply exponential lockout schedule (CR-AUTH-10); return generic auth failure.
9. On match: reset `loginFailureCount=0`, `lockoutUntilUtc=null`. Proceed to CR-AUTH-11.

**Atomicity:** `loginFailureCount` reads and writes MUST be atomic. Use DB-level atomic increment (`UPDATE ... SET loginFailureCount = loginFailureCount + 1 WHERE ...`) or a serialized per-account lock. Plain read-modify-write is not safe under concurrent login attempts.

**All failure reasons return identical response content:** `LoginResult { success:false, errorCode:AuthFailureReason.InvalidCredentials }` — no hint about which condition fired in the response message. Response timing varies by path: locked and banned paths return immediately (<10ms); credential evaluation runs Argon2id (~300ms). This timing delta is a known design tradeoff for DoS resistance (running Argon2id on locked accounts would expose the sidecar to compute exhaustion). See AC-AUTH-16 for the credential-path timing floor and AC-AUTH-19 for the locked-path fast-return requirement.

**Abandoned auth:** If the connection drops or `CONNECTING_TIMEOUT_SECONDS` expires during `Auth_Pending` (before step 7 completes), the `loginFailureCount` is NOT incremented — no credential evaluation completed. See EC-AUTH-2 and EC-AUTH-3.

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
1. Resolve `AccountID → CharacterID` from the account-character mapping.
2. Generate a 16-byte session token per CR-TOK-1 (`networking-session-token.md`). Store at `ActiveSessionTokens[AccountID]` per CR-TOK-8.
3. Emit `ConnectionAccepted` containing session token and `CharacterID` to client. (`AccountID` is server-internal only — CR-AUTH-2; wire schema defined in `auth-wire-messages.md`.)
4. Networking session transitions: `Connecting → Connected` (networking-session.md ST-NET-1).
5. Character Persistence loads character state for the resolved `CharacterID`. Zone assignment proceeds after load.

**Rollback:** If step 3, 4, or 5 fails after step 2 has written the token, delete `ActiveSessionTokens[AccountID]` and transition to `Auth_Failed`.

**CR-AUTH-12 — 1:1 AccountID to CharacterID Mapping (MVP)**
Each `AccountID` maps to exactly one `CharacterID`, established at registration (CR-AUTH-8 step 6) and immutable at MVP. Account and character are **separate DB records linked by FK** — never merged into a single row. This enables multi-character support post-MVP without schema migration. `CharacterID(0)` in the mapping is a persistence corruption error and must halt character load.

---

### States and Transitions

| Auth State | Description | Entry Condition | Exit Condition |
|---|---|---|---|
| `Auth_Idle` | Transport connected; no credentials received | TCP connection established | `ConnectionRequest` or `RegistrationRequest` received |
| `Auth_Pending` | Credentials received; Argon2id executing | `ConnectionRequest` with zero `tokenField` | Match → `Auth_Success`; mismatch → `Auth_Failed` |
| `Auth_Success` | Credentials verified; identities resolved; session token issued | Argon2id match confirmed | `ConnectionAccepted` sent; transitions to `Connected` |
| `Auth_Failed` | Login failed — all causes return identical generic response to client | Mismatch, lockout check, or unknown account (see CR-AUTH-9) | Connection closed; no retry on this connection |
| `Auth_Registration` | Processing a registration request | `RegistrationRequest` received | Completes (success or failure); connection closed; no session issued |
| `Auth_TokenPath` | Reconnect via token; delegated entirely to CR-TOK-4 | `ConnectionRequest.tokenField != byte[16]{0}` | Token valid → `Reconnecting` (networking-session.md); invalid → `Auth_Failed` |

`Connecting → Connected` (networking-session.md ST-NET-1) fires only when `Auth_Success` is reached. `CONNECTING_TIMEOUT_SECONDS` (30s per networking-session.md) is the outer envelope — if Authentication does not complete within that window, the session is abandoned.

---

### Interactions with Other Systems

| System | Direction | Contract |
|---|---|---|
| Networking Core | Receives hook from | Auth is the first handler in `OnNewConnection`. Zone assignment must not proceed until `Auth_Success`. |
| networking-session-token.md | Delegates to | Auth resolves `AccountID` from credentials; token GDD uses `AccountID` as its dictionary key. All token logic is owned by that GDD. Auth does not re-implement it. |
| Character Persistence | Exports to | On `Auth_Success`: Auth calls `ICharacterPersistence.LoadCharacter(CharacterID)`. `CharacterID(0)` must never be forwarded — it is a persistence corruption error. |
| Zone Instancing | Exports to | After character load: Auth hands off `(AccountID, CharacterID, ZoneID from character record)` to Zone Instancing for zone placement. Zone Instancing is not invoked until `Auth_Success` is confirmed. |
| `IAuthenticationService` | Implements | `Task<AuthResult> AuthenticateAsync(ConnectionRequest)`, `Task<RegistrationResult> RegisterAsync(RegistrationRequest)`, `Task<CharacterID> GetCharacterIDAsync(AccountID)`. All async — Argon2id must never block the tick thread. |

```csharp
// IAuthenticationService return types
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

*Not evaluated at runtime. Documents that the in-memory account-character map is safe to keep in-process at design-load concurrency.*

```
MapSizeBytes(N) = N × (sizeof(AccountID) + sizeof(CharacterID) + DictionaryOverheadPerEntry)
               = N × (4 + 4 + 24) = N × 32
```

**Variables:**

| Symbol | Type | Range | Description |
|---|---|---|---|
| N | int | [1, 50] | Concurrent authenticated sessions (bounded by MAX_PLAYERS_PER_ZONE) |
| DictionaryOverheadPerEntry | int constant | ~24 bytes | .NET `Dictionary` bucket + pointer overhead per entry |
| MapSizeBytes | int | [32, 1600] bytes | Total map footprint |

**Output Range:** 32 to 1,600 bytes at MAX_PLAYERS_PER_ZONE=50.

**Example:** N=50 (full zone): 50 × 32 = **1,600 bytes**. Combined with token store from `networking-session-token.md` F-TOK-2 (~3 KB at 50 sessions): total auth service session overhead ≈ **4.6 KB** at peak. No in-memory sizing concern.

---

> Token entropy is fully specified in `networking-session-token.md` F-TOK-1 — not re-specified here. Argon2id parameters (`iterations=2`, `memorySize=19456 KB`, `parallelism=1`) are configuration constants defined in CR-AUTH-5 and Tuning Knobs — not runtime calculations.

## Edge Cases

**EC-AUTH-1 — Duplicate registration race (same username, simultaneous requests)**: The first write succeeds; the second hits the database-level unique constraint on `username_lower` and receives `RegistrationError.UsernameTaken`. No partial record is written for the loser. Application-level read-then-write uniqueness checks have a TOCTOU window — the DB constraint is the authoritative guard.

**EC-AUTH-2 — Connection drops during `Auth_Pending` (Argon2id interrupted)**: The sidecar completes the computation and discards the result. No `loginFailureCount` increment is applied (no credential evaluation completed). Session slot transitions to `Disconnected_SessionExpired` immediately. Rationale: completing and discarding is cheaper under attacker control than aborting mid-computation (no retry cost). `MAX_AUTH_PENDING` (`auth-sidecar-ipc.md`) bounds concurrent in-flight computations and is the primary DoS mitigation against drop-cycling. *(See OQ-AUTH-1 re: network-layer rate limiting as a secondary layer.)*

**EC-AUTH-3 — `CONNECTING_TIMEOUT_SECONDS` (30s) expires during `Auth_Pending`**: Session transitions to `Disconnected_SessionExpired`; in-flight Argon2id computation completes and is discarded. No `loginFailureCount` increment. Client must reconnect from scratch. A 30s expiry during normal hashing implies server overload or configuration error — neither is a player credential issue.

**EC-AUTH-4 — Persistence write failure during registration**: Server returns `RegistrationResult { success:false, errorCode:ServerError=255 }`. No partial account record exists — write must commit before response (CR-NET-5 pattern). The allocated `AccountID` counter value is permanently consumed and not re-issued. Client must retry as a fresh registration. Two-phase compensating cleanup is deferred to post-MVP.

**EC-AUTH-5 — `CharacterID(0)` returned from account-character mapping (corruption)**: Server does NOT forward to Character Persistence. Session transitions to `Disconnected_SessionExpired`, corruption is logged as a critical error with `AccountID` and timestamp, and client receives `LoginResult { success:false, errorCode:ServerError=255 }`. Account requires manual operator remediation.

**EC-AUTH-6 — `CharacterID` already active in a live session (duplicate login / session-stealing)**: Server follows networking-session.md's session-stealing protocol: prior session immediately invalidated (token deleted per CR-TOK-6), character state written to persistence, resources released. New connection proceeds to `Auth_Success`. No duplicate session for the same `CharacterID` is permitted. Prior client receives connection close with no reconnect window.

**EC-AUTH-7 — Account banned while player is `Connected` (admin action mid-session)**: `isBanned` is checked at login only, not on every tick. A currently connected session is NOT immediately terminated by the auth system. An explicit admin kick command (`AdminBanKick(AccountID)`) must terminate the in-flight session. Without it, the ban takes effect on the player's next login attempt. Admin kick mechanism is outside auth scope. *(See OQ-AUTH-4.)* See AC-AUTH-33 for the acceptance criterion codifying this intentional two-step policy.

**EC-AUTH-8 — `loginFailureCount` with long inactivity (no decay)**: The counter does not decay over time. A counter of 9 (600s lockout repeating) remains 9 permanently until a successful login resets it to 0. Time-based decay would allow an attacker to brute-force `LOGIN_FAILURE_LIMIT − 1` credentials, wait for decay, and repeat indefinitely. The admin override (CR-AUTH-10) is the only release valve for locked-out legitimate players. **Design position:** No decay — intentional. Admin override is the only release valve at MVP. Self-service account unlock (e.g. email-based) is deferred to post-MVP.

**EC-AUTH-9 — Username passes client validation but fails server (Unicode / multibyte bypass)**: Server normalizes to lowercase via `ToLowerInvariant`, then validates against the `[a-z0-9_]` ASCII-only charset. Any non-ASCII character (including Cyrillic homoglyphs, accented chars, CJK) fails this check regardless of visual similarity. Length is validated by character count post-normalization, not byte count. Result: `RegistrationError.UsernameInvalidChars`. The ASCII whitelist is simpler and more robust than a Unicode denylist.

**EC-AUTH-10 — Auth sidecar unavailable (IPC channel down)**: For both login and registration: return `ServerError=255`, do not increment `loginFailureCount`, transition session to `Disconnected_SessionExpired` immediately. Sidecar unavailability is logged as a critical infrastructure event, separate from account-level auth failures. If the sidecar is unreachable at game server startup, the game server refuses new connections until the sidecar is confirmed healthy. *(See OQ-AUTH-5 re: queuing vs. immediate fail.)* **Degraded sidecar (slow but not down):** If the sidecar is responding but latency exceeds expected bounds, the game server does NOT short-circuit — it allows the in-flight request to complete until `CONNECTING_TIMEOUT_SECONDS` expires. The health check hysteresis (`SIDECAR_HEALTH_FAILURE_THRESHOLD` and `RECOVERY_THRESHOLD`) governs when the sidecar is reclassified as down; see `auth-sidecar-ipc.md`.

## Dependencies

| Document | Type | Relationship |
|---|---|---|
| `networking-core.md` | **Upstream** (hard) | Networking Core defines the connection-driven execution path Authentication plugs into. CR-NET-2: "On new connection: authentication, zone assignment, session handshake emission." Authentication receives the `OnNewConnection` hook; Networking Core must not invoke zone assignment before `Auth_Success`. `SESSION_TTL_SECONDS` (300s) and `CONNECTING_TIMEOUT_SECONDS` (30s) govern session expiry and the outer auth timeout. |
| `networking-session.md` | **Upstream** (hard) | Defines ST-NET-1 session state machine. `Auth_Success` triggers `Connecting → Connected`. `REAUTH_FAILURE_LIMIT` (value=3, reconnect-token path) is owned by this GDD and must not be redefined here — it is distinct from `LOGIN_FAILURE_LIMIT`. |
| `networking-session-token.md` | **Upstream** (hard) | Defines session token generation, storage, validation, and rotation (CR-TOK-1 through CR-TOK-8). Authentication delegates all token logic here. Auth provides `AccountID` (the dictionary key); the token GDD owns everything else. `F-TOK-2` documents token store memory overhead at MAX_PLAYERS_PER_ZONE. |
| `character-persistence.md` | **Downstream** (hard, Not Started) | Character Persistence receives `(AccountID, CharacterID)` from Authentication on `Auth_Success` and loads character state. Must not load any character without a valid pair from `IAuthenticationService`. Also owns the full character record whose stub is created during registration. Must reference `AccountID` (this GDD) as the FK in its schema. |
| `zone-instancing.md` | **Downstream** (hard, Not Started) | Zone Instancing receives `(AccountID, CharacterID, ZoneID)` from Authentication after character load and places the player in a zone. Zone assignment must not begin before `Auth_Success`. Must reference `AccountID` and `CharacterID` from this GDD. |

**Bidirectional consistency:**
- `networking-core.md` mentions authentication in its connection-driven path — consistent with CR-AUTH-4 and the auth state machine ✅
- `networking-session-token.md` uses `AccountID` as its dictionary key but does not define it — this GDD defines it ✅
- `networking-session.md` owns `REAUTH_FAILURE_LIMIT` (reconnect path); this GDD owns `LOGIN_FAILURE_LIMIT` (credential path) — no naming collision ✅
- `character-persistence.md` and `zone-instancing.md` are Not Started — bidirectional rows must be added when those GDDs are authored

## Tuning Knobs

| Knob | Default | Safe Range | What Breaks if Too Low | What Breaks if Too High |
|---|---|---|---|---|
| `LOGIN_FAILURE_LIMIT` | 5 | [3, 10] | >3 consecutive typos lock out legitimate players | 10+ attempts before any friction — generous gift to credential-stuffing attacks |
| `LockoutTable[5]` (first lockout) | 30s | [15s, 120s] | 15s barely deters automated scripts | 120s at first lockout frustrates legitimate players who mistyped once too many |
| `LockoutTable[9+]` (maximum lockout) | 600s | [300s, 3600s] | 300s — attacker can script around short caps | 3600s — legitimate player with stuck counter is effectively locked out until admin intervenes |
| Argon2id `iterations` | 2 | [2, 4] | < 2: violates OWASP minimum; brute-force resistance degrades | > 4: hash computation exceeds 1–2s per attempt; auth latency becomes noticeable under concurrent logins |
| Argon2id `memorySize` | 19456 KB (19 MiB) | [19456, 65536] | < 19456: violates OWASP minimum | > 65536 (64 MiB): may saturate sidecar heap under concurrent logins at MVP server specs |
| Argon2id `parallelism` | 1 | [1, 2] | — | > 2: no benefit on single-threaded auth path |
| `USERNAME_MIN_LENGTH` | 3 | [2, 6] | 2-char usernames allow squatting of short premium names | 6-char minimum too restrictive for most desired usernames |
| `USERNAME_MAX_LENGTH` | 24 | [12, 32] | 12-char max too tight for creative names | 32-char max expands all display fields; may cause HUD layout issues |
| `PASSWORD_MIN_LENGTH` | 8 | [6, 12] | 6-char allows weak passwords | 12-char frustrates mobile users on virtual keyboards |
| `PASSWORD_MAX_LENGTH` | 72 | [72, 128] | Do not reduce below 72 — mobile UX choice (virtual keyboard limit), not an Argon2id constraint; Argon2id safe floor is [16, 256] | 128+ functionally uncapped in Argon2id — no concern |
| `MAX_AUTH_PENDING` | 10 | [5, 20] | < 5: bursts of legitimate logins may hit the cap during server load | > 20: sidecar memory budget exceeded (see `auth-sidecar-ipc.md` for sizing formula) |

**Joint constraints:**
- `LockoutTable[5]` through `LockoutTable[9+]` must be monotonically increasing — a lockout step must never be shorter than the prior step.
- `LOGIN_FAILURE_LIMIT` and `LockoutTable[9+]` determine brute-force deterrence together; tune as a pair.
- Argon2id `iterations` and `memorySize` must be tuned together — re-derive performance benchmarks on target server hardware when changing either.

**Cross-system note:** `REAUTH_FAILURE_LIMIT` (value=3, owned by `networking-session.md`) governs the reconnect-token re-auth path. It must not be confused with `LOGIN_FAILURE_LIMIT`.

## Visual/Audio Requirements

Authentication has no visual effects or audio events. The auth sidecar is invisible to the player during successful authentication. The moment of arrival in the zone (loading transition → world) is owned by Zone Instancing, not Authentication. Login and registration screen animations or sounds are UI-layer concerns owned by the screen's UX spec.

## UI Requirements

**Login screen**: Username input field (max 24 chars, ASCII-only keyboard, auto-lowercase on submit), password input field (masked, max 72 chars), login button, link to registration screen.

**Registration screen**: Username input (same constraints as login), password input (masked), confirm-password input (client-side match check before submit — do not send mismatched passwords to server), register button, link to login screen.

**Error display**: All auth failures show a single generic message "Invalid username or password." — no distinction between wrong password, unknown account, or lockout is transmitted to the client.

**Lockout feedback**: No lockout duration is transmitted to the client (by design — the response is always generic). The client shows "Please try again later." A local retry cooldown on the client is a UX decision outside this GDD's scope. **Design position (no-duration exposure):** intentional — lockout state and remaining duration are not exposed to the client. Client-side retry logic MUST NOT assume or infer server lockout state from response timing or message content.

**Registration error messages**: The server returns `RegistrationError` codes; the client maps them to user-readable strings: `UsernameTaken`, `UsernameTooShort/TooLong`, `UsernameInvalidChars`, `PasswordTooShort`, `PasswordTooLong`.

> **📌 UX Flag — Authentication**: This system has UI requirements (login screen, registration screen). In Phase 4 (Pre-Production), run `/ux-design` to create UX specs for these screens before writing implementation epics. Stories that reference the login or registration UI should cite `design/ux/login-screen.md` and `design/ux/registration-screen.md`, not this GDD directly.

## Acceptance Criteria

**AC-AUTH-01** [Integration] BLOCKING — Given a client sends a valid `ConnectionRequest` (correct credentials, zero `tokenField`) over TLS, when the server processes it, then all three steps complete before `ConnectionAccepted` is sent: (a) Argon2id credential verification, (b) `AccountID → CharacterID` mapping resolution, (c) session token generation and storage in `ActiveSessionTokens`. Verified by confirming the response carries a non-zero token, a valid `CharacterID`, and `ActiveSessionTokens[AccountID]` is populated at response time.

**AC-AUTH-02** [Logic] BLOCKING — Given `AccountID(0)` is used as a dictionary key or operand, when compared via `AccountIDComparer`, then it equals `AccountID.Invalid` and is never produced by the server counter. Unit test: construct `AccountID(0)`, assert `Equals(AccountID.Invalid)` is `true`; assert first counter allocation is 1.

**AC-AUTH-03** [Logic] BLOCKING — Given two `AccountID` instances with the same wrapped `uint`, when compared via `AccountIDComparer`, then `Equals` returns `true` and hash codes match. With different `uint` values: `Equals` returns `false`. Unit test both.

**AC-AUTH-04** [Logic] BLOCKING — Given a fresh `AccountID` counter initialized from persistence, when three accounts are registered in order, then allocated IDs are strictly monotonically increasing starting at 1. Assert counter state is durably persisted: a second instantiation reads the last-issued value, not 1.

**AC-AUTH-05a** [Integration] BLOCKING — Given a `ConnectionRequest` arrives while the 20 Hz game server tick is executing, when the auth sidecar processes Argon2id (~200–400ms), then the game server tick completes within its 50ms budget during the computation. Verified by server-side tick timestamp logs showing no gap exceeding 50ms correlated with login events.

**AC-AUTH-05b** [Infrastructure] PREREQUISITE — Tick timestamp instrumentation must exist and be captured per-frame before AC-AUTH-05a can be verified. This AC gates sprint commitment on AC-AUTH-05a — do not schedule AC-AUTH-05a until instrumentation story is complete.

**AC-AUTH-06** [Logic] BLOCKING — Given a plaintext password is submitted for hashing, when `Argon2id(password, salt)` runs, then the stored PHC string encodes `iterations=2`, `memorySize=19456`, `parallelism=1`, and a 16-byte salt. Unit test: call hashing wrapper; parse PHC string; assert all four fields; assert salt differs across 5 consecutive calls.

**AC-AUTH-07** [Logic] BLOCKING — Given a password is hashed and stored as a PHC string, when verified with the same plaintext, then returns `true`. With a different plaintext: returns `false`. Unit test round-trip.

**AC-AUTH-08** [Security] BLOCKING — Given a client attempts a plaintext (non-TLS) connection, when the server's connection handler evaluates it, then the connection is closed before any application-layer data is read, and no `LoginResult` or `RegistrationResult` is emitted. Integration test: connect via plaintext TCP; assert closed within one round-trip.

**AC-AUTH-09** [Security] BLOCKING — Given a client sends `ConnectionRequest` with plaintext credentials over TLS, when the server processes it, then: (a) `account.passwordHashPHC` begins with `$argon2id$`, not the plaintext; (b) server logs contain no substring matching the submitted password. For (b): log capture method (stdout redirect or sidecar log file path) must be specified in auth sidecar config before sprint — flag as GAP-6.

**AC-AUTH-10** [Logic] BLOCKING — Given usernames `"Alice"`, `"ALICE"`, and `"alice"` are submitted, when the server normalizes and looks up each, then all three resolve to the same account record `"alice"`. Unit test: register `"alice"`; login with `"Alice"` and `"ALICE"`; assert both succeed.

**AC-AUTH-11** [Logic] BLOCKING — Given boundary username inputs (2-char, 25-char, `!` char, space, 3-char, 24-char, `alice_0`), when the server validates each, then: 2-char → `UsernameTooShort`; 25-char → `UsernameTooLong`; `!` and space → `UsernameInvalidChars`; 3-char, 24-char, `alice_0` → pass validation. One assertion per boundary.

**AC-AUTH-12** [Integration] BLOCKING — Given a valid `RegistrationRequest`, when registration completes, then: (a) account record is committed to persistence with all required fields before response is sent; (b) stub character record exists linked to `AccountID` by FK; (c) response is `RegistrationResult { success:true }`; (d) no session token is issued.

**AC-AUTH-13** [Logic] BLOCKING — Given a password of length 7 (one below minimum), when submitted to registration, then response is `RegistrationError.PasswordTooShort`. Given length 8: passes validation. Unit test both boundary values.

**AC-AUTH-14** [Integration] BLOCKING — Given `"alice"` is registered, when `"Alice"` is submitted for registration, then response is `RegistrationError.UsernameTaken` and exactly one account record for `"alice"` exists in the DB.

**AC-AUTH-15** [Integration] BLOCKING — Given a `ConnectionRequest` with non-zero `tokenField` and a wrong password, when the server receives it, then it delegates to CR-TOK-4 (reconnect path) and does NOT evaluate the credential fields. Verified by asserting the reconnect path is followed, not the credential failure path.

**AC-AUTH-16** [Security] BLOCKING — Given 20 login attempts against a non-existent username and 20 against a known username that fail at Argon2id, when response latencies are measured, then: (a) both paths take ≥150ms per sample (Argon2id runs in both cases); (b) mean latency delta between the two distributions is < 20ms across the 20-sample sets. Assert no sample from the non-existent path completes in < 150ms. The 150ms floor and 20ms delta threshold are named constants (`AUTH_TIMING_FLOOR_MS = 150`, `AUTH_TIMING_DELTA_THRESHOLD_MS = 20`) — define in test config before sprint.

**AC-AUTH-17** [Logic] BLOCKING — Given an account with `loginFailureCount=4`, when a correct login is submitted, then `loginFailureCount=0` and `lockoutUntilUtc=null` on the account record before the response is sent.

**AC-AUTH-18** [Logic] BLOCKING — Given `LockoutDuration(F)` is evaluated for F ∈ {0, 1, 4, 5, 6, 7, 8, 9, 15, 100}, when each is computed, then the results are exactly {0, 0, 0, 30, 60, 120, 300, 600, 600, 600} seconds respectively. Assert `lockoutUntilUtc = UtcNow + LockoutDuration` within 1-second tolerance.

**AC-AUTH-19** [Logic] BLOCKING — Given an account with `lockoutUntilUtc` in the future, when a login attempt arrives, then: (a) the response arrives in < 10ms (no Argon2id latency); (b) `loginFailureCount` increments by 1; (c) `lockoutUntilUtc` is reset to `UtcNow + LockoutDuration(new loginFailureCount)`. Assert all three outcomes via account record inspection after the attempt.

**AC-AUTH-20** [Logic] BLOCKING — Given `loginFailureCount=9`, when a failed login is submitted, then `loginFailureCount=10` and `lockoutUntilUtc ≈ UtcNow + 600s`. Repeat with `loginFailureCount=50`: assert `lockoutUntilUtc ≈ UtcNow + 600s` (cap holds).

**AC-AUTH-21** [Integration] BLOCKING — Given a successful login, when session establishment runs, then: (a) `ActiveSessionTokens[AccountID]` contains a new non-zero 16-byte token; (b) `ConnectionAccepted` carries the session token and `CharacterID` — assert no `AccountID` field is present in the wire message (per `auth-wire-messages.md`); (c) networking session transitions to `Connected`; (d) `ICharacterPersistence.LoadCharacter(CharacterID)` is called exactly once. Verified via test spy on the interface.

**AC-AUTH-22** [Integration] BLOCKING — Given an MVP account, when `GetCharacterIDAsync(AccountID)` is called, then the result matches the stub created at registration. Account and character are separate DB rows — assert account row contains no character data fields; character row contains `accountId` FK.

**AC-AUTH-23** [Logic] BLOCKING — Given `GetCharacterIDAsync` returns `CharacterID(0)`, when the server checks the result, then: `LoadCharacter` is NOT called (spy count = 0); critical error is logged with `AccountID` and timestamp; session transitions to `Disconnected_SessionExpired`; response is `AuthFailureReason.ServerError=255`.

**AC-AUTH-24** [Logic] ADVISORY — Given the account-character map is instantiated with 50 entries, when `GC.GetTotalMemory` is measured before and after (with `GC.Collect()` before), then delta does not exceed 3,200 bytes (2× the 1,600-byte formula estimate). Assert across 3 consecutive runs.

**AC-AUTH-25** [Integration] BLOCKING — Given two simultaneous `RegistrationRequest` messages for the same username, when both are processed concurrently, then exactly one account record exists; one response is `success:true`; the other is `RegistrationError.UsernameTaken`.

**AC-AUTH-26** [Integration] BLOCKING — Given a client drops the TCP connection (RST) during `Auth_Pending` (Argon2id executing), when the sidecar completes and discards the result, then `loginFailureCount` on the account record is unchanged from its pre-attempt value. *(Requires TCP RST injection hook — flag GAP-3 for test infrastructure design before sprint.)*

**AC-AUTH-27** [Integration] BLOCKING — Given client A is `Connected` for account X, when client B submits valid credentials for account X, then: (a) client A's session transitions to `Disconnected_SessionExpired` with final state written to persistence; (b) `ActiveSessionTokens[AccountID X]` is repopulated with a new token for client B; (c) client A's connection is closed (no reconnect window); (d) client B reaches `Auth_Success`. All four outcomes verified via test instrumentation.

**AC-AUTH-28** [Security] BLOCKING — Given username inputs `"аlice"` (Cyrillic U+0430), `"ａlice"` (Fullwidth U+FF41), `"àlice"` (U+00E0), `"日本語"` (CJK), when each is submitted to registration, then all return `RegistrationError.UsernameInvalidChars`. Four separate assertions.

**AC-AUTH-29** [Integration] BLOCKING — Given the auth sidecar IPC channel is unavailable, when a client attempts login or registration, then: (a) response is `ServerError=255`; (b) session transitions to `Disconnected_SessionExpired`; (c) `loginFailureCount` is NOT incremented; (d) server logs a critical infrastructure event (not an account-level auth failure). If sidecar is unreachable at startup: `OnNewConnection` is not invoked until sidecar health check passes.

**AC-AUTH-30** [Security] BLOCKING — Given a `ConnectionAccepted` is sent to a client on `Auth_Success`, when the client-side message is deserialized, then the message struct does not contain an `AccountID` field — verified by asserting the wire message schema (`auth-wire-messages.md`) has no `accountId` field. *(AccountID is server-internal only per CR-AUTH-2.)*

**AC-AUTH-31** [Integration] BLOCKING — Given a persistence fault is injected between the account write and the character stub write during registration (EC-AUTH-4 path), when the fault fires, then: (a) no partial account row exists — the account record is either fully committed or fully absent (column-level atomicity guaranteed; note: account-committed + character-stub-absent is a known possible orphan state at MVP, to be resolved by post-MVP compensating cleanup per EC-AUTH-4); (b) the allocated `AccountID` counter value is consumed (not re-issued); (c) client receives `RegistrationResult { success:false, errorCode:ServerError=255 }`. Requires `IAccountRepository` mock that throws on character stub write after account commit.

**AC-AUTH-32** [Integration] BLOCKING — Given `CONNECTING_TIMEOUT_SECONDS` (30s) expires while a login is in `Auth_Pending` (Argon2id executing), when the timeout fires, then: (a) session transitions to `Disconnected_SessionExpired`; (b) `loginFailureCount` on the account record is unchanged from its pre-attempt value; (c) the sidecar completes the computation and discards the result without incrementing failure state. Distinct from AC-AUTH-26 (TCP RST path) — this tests the server-side timeout path. *(Flag for test infrastructure: requires controllable Argon2id delay injection in test environment.)*

**AC-AUTH-33** [Logic] BLOCKING — Given an account is banned (`isBanned = true`) while a session for that account is in `Connected` state, when the auth system processes the ban, then the live session is NOT auto-terminated — the player remains connected. The ban takes effect on the next login attempt only. Verified by: (1) establishing a `Connected` session; (2) setting `isBanned = true` on the account record; (3) asserting session remains `Connected` for ≥ 5s with no auto-disconnect event. *(Tests intentional two-step policy: ban + explicit admin kick required; see EC-AUTH-7 and OQ-AUTH-4.)*

---

### Testability Gaps (flag before sprint commitment)

| Gap | AC affected | Risk | Resolution needed |
|---|---|---|---|
| GAP-1 | AC-AUTH-01, AC-AUTH-05a | MEDIUM | Server-side step-timestamp and tick-timing instrumentation (prerequisite tracked by AC-AUTH-05b) |
| GAP-2 | AC-AUTH-16 | MEDIUM | Define latency floor (≥150ms) as a named constant before sprint |
| GAP-3 | AC-AUTH-26 | HIGH | TCP RST injection hook in test infrastructure |
| GAP-4 | EC-AUTH-3 (no AC) | LOW | Implicit coverage via AC-AUTH-26; add explicit AC if session-state transitions are observable |
| GAP-5 | EC-AUTH-4 (no AC) | LOW | Requires persistence layer fault injection (`IAccountRepository` mock that throws on write) |
| GAP-6 | AC-AUTH-09(b) | MEDIUM | Log capture interface (stdout redirect or sidecar log file path) must be specified in auth sidecar config before sprint |

## Open Questions

| ID | Question | Blocking? | Escalate To |
|---|---|---|---|
| OQ-AUTH-1 | Should per-IP rate limiting for TCP connect-and-drop cycling be specified? `loginFailureCount` does not increment on abandoned auth — rapid drop cycling is unmitigated by the account lockout system. | No | Architecture / Security review |
| OQ-AUTH-2 | Should the auth sidecar expose a server-side monitor alert when `Auth_Pending` duration exceeds 2s? Would surface Argon2id performance regression before it reaches the 30s timeout. | No | Monitoring / observability ADR |
| OQ-AUTH-3 | `AccountID` counter advances on failed registration (non-contiguous IDs). Functionally harmless (AccountID is never client-visible). Confirm no admin or analytics requirement changes this. | No | User |
| OQ-AUTH-4 | Should the auth sidecar push a notification to the game server when `isBanned` is set (enabling auto-kick), or is the two-step "ban + explicit kick" admin workflow acceptable for MVP? | No | Admin tooling design |
| OQ-AUTH-5 | Should the game server queue `ConnectionRequest` messages for ~5s during a transient sidecar outage and retry, or fail immediately? Immediate failure is the safer MVP default (prevents session slot exhaustion during prolonged outages). | No | Auth sidecar resilience design |
| OQ-AUTH-6 | Should per-IP rate limiting on the registration endpoint be specified? The `AccountID` counter advances on each attempt — sustained registration spam from one IP could exhaust the counter over a very long time horizon (functionally harmless at MVP scale, but a DoS vector on launch). | No | Architecture / Security review |
