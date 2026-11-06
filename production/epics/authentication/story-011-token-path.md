# Story 011: Login — token path

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

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

- [ ] `AuthenticationService.AuthenticateAsync` dispatches on `tokenField`: all zeros → Stories 009 + 010; non-zero → `TokenPath.RunAsync(username, token, ct)`
- [ ] AC-AUTH-15: non-zero token + wrong password, account with a live session → `Reconnect` sent to the zone in the map; the hasher is never called; `RecordFailure` / `ResetFailures` never called
- [ ] AC-AUTH-38: (i) unknown username, (ii) no map entry for the account, (iii) live session but `IsBanned` → each `InvalidCredentials`, no `Reconnect`, no hash
- [ ] AC-AUTH-38: live session, not banned → exactly one `Reconnect` to the zone in the map
- [ ] `ReconnectOutcome.Accepted` → `LoginResponse { success:true }` with the **rotated** token from the zone, the session's `CharacterID`, that `zoneId` and its port; rejected → `InvalidCredentials`
- [ ] No reply within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`, or the zone's link down → `ServerError`
- [ ] The lockout fields are not read or written on this path (an account inside an active lockout with a valid token still reconnects)
- [ ] The gate (`MAX_AUTH_PENDING`) is not taken on this path (no hashing)

---

## Implementation Notes

*Derived from ADR-016 Decision 4:*

- `ReconnectOutcome { Accepted, NewToken[16] }` per `ReconnectResult`. Where the `CharacterID` and port in the response come from: the `CharacterID` from `GetCharacterIDAsync`, the port from the router's view of that zone (`IZoneRouter` gets a `TryGetPort(ZoneID)` or the map returns it — keep it on the seam interfaces from Story 010 and document it).
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

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_TokenPath_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007, Story 010 (seam interfaces)
- Unlocks: Story 012
