# Story 011: Login — token path

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 hours (token path, link + router extension, existing fakes updated, one test file)

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-9 step 2, EC-AUTH-7 (token path), CR-AUTH-13 rationale (accepted token-path weaknesses); AC-AUTH-15, 38; `networking-session-token.md` CR-TOK-4/5
**Requirement**: `TR-auth-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 4 — token path)
**ADR Decision Summary**: With a non-zero `tokenField` the gateway does not hash. Unknown account, banned account, or no zone holding a session → `InvalidCredentials`, nothing sent to any zone. Otherwise `Reconnect` to that zone; the zone validates and rotates, and the gateway relays the new token. Lockout is not checked on this path.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (this layer)**:
- Required: token path — no hashing; unknown account, `is_banned`, or no zone holding a session → `InvalidCredentials`; otherwise `Reconnect` to that zone, which runs `TryValidateAndRotate` and the `REAUTH_FAILURE_LIMIT` count, and the gateway relays the new token — ADR-016 Decision 4

---

## Acceptance Criteria

- [ ] `AuthenticationService.AuthenticateAsync` dispatches on `tokenField`: all zeros → Stories 009 + 010; non-zero → `TokenPath.RunAsync(username, token, ct)`. `AuthenticationService`'s constructor gains `TokenPath`; Story 010's `NotSupportedException` for a non-zero token is removed
- [ ] `TokenPath` constructor: `(IAccountRepository, IAccountZoneMap, IZoneRouter, IZoneLink, GatewayOptions, ILoggerFactory)` — no hasher, no gate. Order: normalize → `FindByUsernameAsync(ct)` (null → `InvalidCredentials`) → `IsBanned` → `InvalidCredentials` → map `TryGetZone` (no entry → `InvalidCredentials`) → `IsLinkUp` false → `ServerError` at once + one Warning (AccountID + zone), no `Reconnect` → `GetCharacterIDAsync(None)` (`CharacterID.Invalid` → `ServerError` + one Critical, EC-AUTH-5) → `IZoneRouter.TryGetPort(zone)` (false → `ServerError`) → `ct` check → `ReconnectAsync`. The map is not changed on this path (no `SetSession`)
- [ ] `IZoneLink` gains `Task<ReconnectOutcome> ReconnectAsync(ZoneID, AccountID, byte[] token, CancellationToken)`; `ReconnectOutcome` = `readonly record struct ReconnectOutcome(bool Accepted, byte[]? NewToken)`. `IZoneRouter` gains `bool TryGetPort(ZoneID zone, out ushort port)` (the zone's `RegisterZone.udpPort`)
- [ ] Cancellation: `ct` reaches `FindByUsernameAsync` and is checked once before `Reconnect` is sent; after that it is not observed (the zone rotates the token, so the request must finish and deliver it). `Reconnect` gets only its own `ZoneRequestTimeoutMs` token and the wait is bounded — `WithTimeoutAsync<T>` moves from `SessionEstablishment` to a shared `internal static class ZoneLinkTimeout` used by both
- [ ] Errors: `FindByUsernameAsync` / `GetCharacterIDAsync` / `ReconnectAsync` throws, `Reconnect` times out, or an accepted outcome carries a token that is null, not 16 bytes or all zeros → `ServerError` + exactly one `LogCritical` under `IronGrind.Gateway.Infrastructure`. Unknown username, banned, no session and rejected token are each logged once at Information under the `TokenPath` category (AccountID when known) — never the username or any token
- [ ] AC-AUTH-15: non-zero token + wrong password, account with a live session → `Reconnect` sent to the zone in the map; the hasher is never called; `RecordFailure` / `ResetFailures` never called
- [ ] AC-AUTH-38: (i) unknown username, (ii) no map entry for the account, (iii) live session but `IsBanned` → each `InvalidCredentials`, no `Reconnect`, no hash
- [ ] AC-AUTH-38: live session, not banned → exactly one `Reconnect` to the zone in the map
- [ ] `ReconnectOutcome.Accepted` → `LoginResponse { success:true }` with the **rotated** token from the zone, the session's `CharacterID`, that `zoneId` and its port; rejected → `InvalidCredentials`
- [ ] No reply within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, or the zone's link down → `ServerError`
- [ ] The lockout fields are not read or written on this path (an account inside an active lockout with a valid token still reconnects)
- [ ] The gate (`MAX_AUTH_PENDING`) is not taken on this path (no hashing) — by construction (`TokenPath` has no gate or hasher) and asserted through `AuthenticateAsync` with a counting gate and hasher (0 calls)
- [ ] Existing files updated: `IZoneLink.cs`, `IZoneRouter.cs`, `SessionEstablishment.cs` (uses `ZoneLinkTimeout`), `AuthenticationService.cs`; test fakes in `Auth_SessionEstablishment_tests.cs` (new methods; `AuthenticateAsync_NonZeroTokenField_ThrowsNotSupported` replaced by a dispatch test) and `Auth_RegistrationService_tests.cs` (strict link gains `ReconnectAsync`, stays strict; construction gains `TokenPath`)

---

## Implementation Notes

*Derived from ADR-016 Decision 4:*

- `ReconnectOutcome(bool Accepted, byte[]? NewToken)` per `ReconnectResult`. The response's `CharacterID` comes from `GetCharacterIDAsync`, the port from `IZoneRouter.TryGetPort(zone)` (decided at readiness 2026-11-06; the router already holds each zone's `RegisterZone.udpPort`).
- Token "non-zero" = any byte non-zero (the codec guarantees 16 bytes).
- The username is normalized the same way as the credential path (Story 005).
- The accepted weaknesses in CR-AUTH-13's rationale (timing tells whether a session exists; bad tokens can expire a session) are by design — do not add mitigations here.

---

## Out of Scope

- Story 014: the zone-side `Reconnect` handler (`TryValidateAndRotate`, `REAUTH_FAILURE_LIMIT`)
- Gateway epic: the real link

---

## QA Test Cases

- **AC-1 (AC-AUTH-15)**: No credential evaluation
  - Given: account 1 in map → zone 3; token 0xAA…; password `"wrong"`; failure count 2
  - When: `AuthenticateAsync`
  - Then: link log = [`Reconnect(3, 1, token)`]; hasher calls 0; failure count still 2
- **AC-2 (AC-AUTH-38 i–iii)**: Rejected without zone traffic
  - Given: (i) unknown username; (ii) account without a map entry; (iii) map entry, `IsBanned` true
  - Then: each `InvalidCredentials`; link calls 0; hasher calls 0
- **AC-3 (AC-AUTH-38)**: Exactly one Reconnect
  - Given: live session, not banned, zone accepts with new token 0xBB…
  - Then: one `Reconnect`; response success with token 0xBB…, zone 3, its port
  - Edge cases: zone rejects → `InvalidCredentials`; zone silent → `ServerError` after the test timeout
- **AC-4**: Lockout ignored
  - Given: `lockoutUntilUtc` = now + 600 s, valid token
  - Then: success; repository write calls 0
- **AC-5**: Link down, port, errors
  - Given: session zone link down → `ServerError`, one Warning, no `Reconnect`; router has no port for the zone → `ServerError`
  - Given: each of `FindByUsernameAsync` / `GetCharacterIDAsync` / `ReconnectAsync` throws; `Reconnect` never completes and ignores its token (50 ms test timeout); accepted with a null / 15-byte / all-zero token → `ServerError`, one Critical, no token in any log
  - Given: `CharacterID.Invalid` → `ServerError`, one Critical, no `Reconnect`
- **AC-6**: Cancellation and dispatch
  - Given: `ct` already cancelled → `OperationCanceledException`, no `Reconnect`; `Reconnect` receives a token other than the request `ct`
  - Given: `AuthenticateAsync` with a non-zero token → `TokenPath` runs, counting hasher and gate both 0, map `SetSession` calls 0

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_TokenPath_tests.cs`

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: Story 007, Story 010 (seam interfaces)
- Unlocks: Story 012

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 13/13 passing (`Auth_TokenPath_tests.cs` 22 cases, pure fakes, 50 ms timeouts; dispatch also in `Auth_SessionEstablishment_tests.cs`)
**Deviations**: Advisory only — (1) `TokenPath` ctor also takes `TimeProvider` (EC-AUTH-5 Critical carries the UTC time, AC-AUTH-23); (2) new shared `Auth/SessionTokenBytes.cs` (LENGTH, `IsValid`, `NewRandom`) used by both login paths; (3) `TryGetPort` false logs one Warning; (4) a link-raised `OperationCanceledException` during `Reconnect` becomes `ServerError` + Critical, never a request cancellation
**Test Evidence**: Logic — `gateway/IronGrind.Gateway.Tests/Auth_TokenPath_tests.cs`; gateway suite (`TestCategory!=Database`) 281/281, 0 warnings
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, 3 applied (Warning on missing port; `TimeProvider` timestamp on EC-AUTH-5; `SessionTokenBytes` single token definition)
