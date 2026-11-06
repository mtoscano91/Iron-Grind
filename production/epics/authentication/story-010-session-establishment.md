# Story 010: Login — session establishment

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

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

- [ ] `SessionEstablishment.RunAsync(accountId, ct)` → `LoginResponse`, using four injected seams: `IAccountRepository.GetCharacterIDAsync`, `IAccountZoneMap` (lookup / add / link-state query), `IZoneRouter` (`TryPick(excludedZone?) → (ZoneID, port) | NoRoutableZone`), `IZoneLink` (`AdmitAsync`, `EvictAsync`). Real map, router and link come from the gateway epic; this story defines the seam interfaces and uses fakes
- [ ] AC-AUTH-01 (fake link): correct login → response with a non-zero token, the account's `CharacterID`, non-zero `zoneId` and port; the fake link received `Admit` with the same token and `CharacterID` before the response was produced
- [ ] AC-AUTH-23 / EC-AUTH-5: `GetCharacterIDAsync` returns `CharacterID.Invalid` → no `Admit` (fake count 0), critical log with `AccountID` and timestamp, `ServerError`
- [ ] AC-AUTH-27 (a, d — gateway): the map holds account X in zone Z → `Evict(Z, X)` is sent and no `Admit` is sent before `EvictAsync` completes; then `Admit` and `success:true` with the new token
- [ ] EC-AUTH-6: `Evict` with no reply within `GATEWAY_EVICT_TIMEOUT_MS` → `ServerError`, no `Admit`; the session's zone link down (map's link-state query) → `ServerError` at once, no `Evict`
- [ ] AC-AUTH-37: first zone rejects `Admit` → `Admit` to a second zone (router called with the first excluded) → `success:true` for it; both reject → `ServerError` after exactly two `Admit`s and no map entry added
- [ ] AC-AUTH-29 (ii): `Admit` with no reply within `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` → `ServerError`, critical infrastructure log
- [ ] No routable zone → `LoginResponse { success:false, errorCode:ServerStarting }` at once, no `Admit`
- [ ] On accept the map entry `X → zone` is added (via `IAccountZoneMap`); the token is not stored anywhere in the gateway after the response (no field, no map value)
- [ ] `AuthenticationService.AuthenticateAsync` for a zero `tokenField` = Story 009's credential check then this; credential failure never reaches this step

---

## Implementation Notes

*Derived from ADR-016 Decision 4 and Key Interfaces:*

```csharp
public interface IZoneLink
{
    Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct);
    Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct);
    Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct);
}
```

- Timeouts: wrap each link call in a `CancellationTokenSource` with the configured timeout; a timeout is `ServerError`. (Whether the real link enforces the timeout itself is the gateway epic's call; the service must not hang if it does not.)
- Token: `RandomNumberGenerator.GetBytes(16)`; regenerate on the impossible all-zero result.
- `IAccountZoneMap` and `IZoneRouter` are interface-only here, with XML docs quoting ADR-016 Decision 3 rules the gateway epic must implement (late admission → `Evict`, zone-matched removal, link-loss grace, boot wait → `ServerStarting`). The boot-wait `ServerStarting` is the router's `NoRoutableZone` answer, so this story's handling covers it.
- Re-route excludes only the rejecting zone; exactly one re-route.
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

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_SessionEstablishment_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007 (`GetCharacterIDAsync`), Story 009
- Unlocks: Story 011, Story 012, the gateway epic's link/map/router stories (they implement this story's seams)
