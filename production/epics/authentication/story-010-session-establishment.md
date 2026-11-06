# Story 010: Login — session establishment

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 5 hours (three seam interfaces, the service, `AuthenticateAsync` wiring, one test file with timeout cases, Story 008 test update)

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-1, CR-AUTH-11 (steps 1–5, rollback), EC-AUTH-5, EC-AUTH-6 (gateway side), EC-AUTH-10 (zone link); AC-AUTH-01 (fake link), 23, 27 (a, d — gateway side), 29 (ii), 37
**Requirement**: `TR-auth-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 4 fresh login steps 2–6, Key Interfaces `IZoneLink`), with ADR-013
**ADR Decision Summary**: After the credential check: resolve `CharacterID` (0 or missing → `ServerError` + critical log); if the account → zone map holds a session, `Evict` it and wait (link down → `ServerError` at once); route (no routable zone → `ServerStarting`); generate a 16-byte token with `RandomNumberGenerator` and `Admit`; on accept add the map entry and reply; on reject re-route once, else `ServerError`. The gateway keeps no token after replying.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (this layer)**:
- Required: fresh login steps (2)–(6) in that order; the gateway keeps no token after replying — ADR-016 Decision 4
- Required: tokens from `RandomNumberGenerator` — ADR-013
- Guardrail: `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` (2 000) for `Admit`, `GATEWAY_EVICT_TIMEOUT_MS` (11 000) for `Evict` — ADR-016 Tuning Knobs

---

## Acceptance Criteria

- [ ] `SessionEstablishment.RunAsync(accountId)` → `LoginResponse` (no request token: CR-AUTH-9 "Abandoned auth" — an abort after the credential check is not observed; the login runs to completion and an unused admission expires in the zone). Order: `GetCharacterIDAsync` → `GetLastZoneIdAsync` → map lookup (link down → `ServerError`; session → `Evict`) → route → token → `Admit` → on accept `SetSession` and reply. Injected seams: `IAccountRepository` (`GetCharacterIDAsync`, `GetLastZoneIdAsync`, both called with `CancellationToken.None`); `IAccountZoneMap` (`bool TryGetZone(AccountID, out ZoneID)`, `bool IsLinkUp(ZoneID)`, `void SetSession(AccountID, ZoneID)` — replaces any existing entry for the account); `IZoneRouter` (`bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port)` — `ZoneID.Invalid` means "none" for both inputs, `false` = no routable zone); `IZoneLink` (`AdmitAsync`, `EvictAsync` only — Story 011 adds `ReconnectAsync` and `ReconnectOutcome`). Also injected: `GatewayOptions` (`ZoneRequestTimeoutMs`, `EvictTimeoutMs`), `TimeProvider`, `ILoggerFactory`. Real map, router and link come from the gateway epic; this story defines the seam interfaces and uses fakes
- [ ] AC-AUTH-01 (fake link): correct login → response with a non-zero token, the account's `CharacterID`, non-zero `zoneId` and port; the fake link received `Admit` with the same token and `CharacterID` before the response was produced
- [ ] AC-AUTH-23 / EC-AUTH-5: `GetCharacterIDAsync` returns `CharacterID.Invalid` → no `Admit` (fake count 0), one critical log containing the `AccountID` and the `TimeProvider` UTC time, `ServerError`
- [ ] AC-AUTH-27 (a, d — gateway): the map holds account X in zone Z → `Evict(Z, X)` is sent and no `Admit` is sent before `EvictAsync` completes; then `Admit` and `success:true` with the new token
- [ ] EC-AUTH-6: `Evict` with no reply within `GATEWAY_EVICT_TIMEOUT_MS` → `ServerError`, no `Admit`; the session's zone link down (map's link-state query) → `ServerError` at once, no `Evict`
- [ ] AC-AUTH-37: first zone rejects `Admit` (`AdmitAsync` returns `false`) → `Admit` to a second zone (router called with the first excluded) → `success:true` for it; both reject → `ServerError` after exactly two `Admit`s and no map entry added; after a rejection with no other routable zone → `ServerError` (not `ServerStarting`). Only an explicit rejection re-routes; a timeout or exception never does
- [ ] AC-AUTH-29 (ii): `Admit` with no reply within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` → `ServerError`, one critical infrastructure log, no re-route, no map entry
- [ ] Errors: `GetCharacterIDAsync`, `GetLastZoneIdAsync`, `EvictAsync` or `AdmitAsync` throws, or `Evict` / `Admit` times out → `ServerError` plus exactly one `LogCritical` under `IronGrind.Gateway.Infrastructure` (`AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY`); no log entry contains the token
- [ ] No routable zone on the first pick → `LoginResponse { success:false, errorCode:ServerStarting }` at once, no `Admit`
- [ ] On accept `SetSession(X, zone)` is called (replacing any entry); the token is not stored anywhere in the gateway after the response (no field, no map value)
- [ ] `IAuthenticationService.AuthenticateAsync(ConnectionRequest, CancellationToken)` → `LoginResponse`: for a zero `tokenField` = Story 009's `CredentialCheck` then `SessionEstablishment`; `InvalidCredentials` / `ServerError` → `LoginResponse.ForFailure` of the same reason, `OperationCanceledException` from the credential check propagates; credential failure never reaches session establishment. A non-zero `tokenField` throws `NotSupportedException` until Story 011
- [ ] AC-AUTH-12 (d) becomes behavioural: `AuthenticationService` now takes `CredentialCheck` and `SessionEstablishment` (which holds an `IZoneLink`), so Story 008's structural test `AuthenticationService_Constructors_TakeNoZoneLink` is replaced in `Auth_RegistrationService_tests.cs` by a strict fake `IZoneLink` (fails on any call) shared through `SessionEstablishment`; registration success and failure assert 0 link calls

---

## Implementation Notes

*Derived from ADR-016 Decision 4 and Key Interfaces:*

```csharp
public interface IZoneLink
{
    Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct);
    Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct);
    // ReconnectAsync / ReconnectOutcome: added in Story 011 (ADR-016 Key Interfaces)
}
```

- Timeouts: wrap each link call in a `CancellationTokenSource` with the configured timeout (the only token the link sees); a timeout is `ServerError`. Also bound the wait itself (e.g. `Task.WaitAsync(timeout)`) so a fake or real link that ignores the token cannot hang the service. Tests set both timeouts to 50 ms; Story 012 wires the real `GatewayOptions`.
- Map contract: ADR-016 Decision 3 says the entry is added "when `AdmitResult(accepted)` arrives". In this design `SessionEstablishment` performs that add via `SetSession`, so the real link must not add it a second time; a late `AdmitResult` after a timeout gets an `Evict` from the link and no entry. Zone-matched removal on `EvictResult` / `SessionReleased` stays in the map (gateway epic); `SetSession` replaces whatever entry remains.
- Token: `RandomNumberGenerator.GetBytes(16)`; regenerate on the impossible all-zero result.
- `IAccountZoneMap` and `IZoneRouter` are interface-only here, with XML docs quoting ADR-016 Decision 3 rules the gateway epic must implement (late admission → `Evict`, zone-matched removal, link-loss grace, boot wait → `ServerStarting`). The boot-wait `ServerStarting` is the router's `NoRoutableZone` answer, so this story's handling covers it.
- Re-route excludes only the rejecting zone and passes the same `lastZone`; exactly one re-route.
- `zoneServerPort` comes from the router's pick (`RegisterZone.udpPort`).

---

## Out of Scope

- Gateway epic: real `IZoneLink` (TCP), `IAccountZoneMap` rules, `IZoneRouter` (fill-first, warm spare), AC-AUTH-24
- Story 014: what the zone does on `Evict`
- Story 017: AC-AUTH-01 against the real link

---

## QA Test Cases

- **AC-1 (AC-AUTH-01)**: Order of operations
  - Given: fake router → zone 5 port 7777; fake link accepts; fake repo CharacterID 77
  - When: `RunAsync(AccountID 1)`
  - Then: response token non-zero, characterId 77, zoneId 5, port 7777; link log = [`Admit(5, 1, 77, sameToken)`] recorded before the response returned
- **AC-2 (AC-AUTH-23)**: Corrupt mapping
  - Given: repo returns `CharacterID.Invalid`
  - Then: `ServerError`; link calls 0; one critical log containing `AccountID(1)`
- **AC-3 (AC-AUTH-27 a,d)**: Evict before Admit
  - Given: map has 1 → zone 3; fake `EvictAsync` completes only when the test releases it
  - When: `RunAsync` started
  - Then: link log = [`Evict(3,1)`] while pending; after release, `Admit` follows and the result is success
  - Edge cases: evict never completes → `ServerError` after the (test-shortened) timeout, no `Admit`; map reports zone 3's link down → `ServerError` immediately, link log empty
- **AC-4 (AC-AUTH-37)**: Re-route once
  - Given: router picks 5 then 6; zone 5 rejects
  - Then: `Admit(5)`, `Admit(6)`; success for zone 6; with both rejecting → `ServerError`, exactly two `Admit`s, map unchanged
- **AC-5 (AC-AUTH-29 ii)**: Admit timeout
  - Given: `AdmitAsync` never completes; timeout configured to 50 ms for the test
  - Then: `ServerError`; critical infrastructure log
- **AC-6**: No routable zone
  - Then: `ServerStarting`; link calls 0
- **AC-7**: Token not retained
  - Then: after success, reflection over `SessionEstablishment` and the fake map shows no stored 16-byte token
- **AC-8**: Routing input and errors
  - Given: repo last zone 4 → router receives `lastZone` 4, `excluded` Invalid; on re-route `lastZone` 4, `excluded` = rejecting zone
  - Given: each of `GetCharacterIDAsync` / `GetLastZoneIdAsync` / `EvictAsync` / `AdmitAsync` throws → `ServerError`, one Critical entry, no map entry
  - Given: zone 5 rejects and the router has nothing else → `ServerError`, one `Admit`
- **AC-9**: `AuthenticateAsync`
  - Given: credential check `InvalidCredentials` → `ForFailure(InvalidCredentials)`, link calls 0, router calls 0; `ServerError` → `ForFailure(ServerError)`; `Passed` → session establishment runs with that `AccountID`; non-zero `tokenField` → `NotSupportedException`
  - Given: Story 008 registration tests with a strict fake link → 0 link calls (replaces the structural constructor test)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_SessionEstablishment_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Auth_RegistrationService_tests.cs` (modified: strict fake link replaces the structural constructor test)

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: Story 007 (`GetCharacterIDAsync`), Story 009
- Unlocks: Story 011, Story 012, the gateway epic's link/map/router stories (they implement this story's seams)

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 12/12 passing (`Auth_SessionEstablishment_tests.cs` 24 cases, pure fakes, timeouts 50 ms; `Auth_RegistrationService_tests.cs` strict-link tests replace the structural constructor test)
**Deviations**: Advisory only — (1) `RunAsync` has one outer try/catch: any exception or timeout → `ServerError` + exactly one Critical (a programming error also surfaces as `ServerError`); (2) each `Admit` gets a fresh token (re-route does not reuse the first); (3) the link-down path logs one Warning (AccountID + zone) in the infrastructure category
**Test Evidence**: Logic — `gateway/IronGrind.Gateway.Tests/Auth_SessionEstablishment_tests.cs`; gateway suite (`TestCategory!=Database`) 259/259, 0 warnings
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, 3 applied (fresh token per Admit; Warning on link down; `WithTimeoutAsync<T>(call, ms, step)` rethrows timeouts as `TimeoutException` naming the step, never the token)
