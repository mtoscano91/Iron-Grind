# Story 013: Zone admits a gateway token

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-11 steps 4 and 6, CR-AUTH-13 (visible name); AC-AUTH-21 (a, c, d, e — zone side); `networking-session-token.md` CR-TOK-8; `zone-instancing.md` CR-ZI-8
**Requirement**: `TR-auth-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 4 — fresh login step 5, Zone entry), with ADR-011 (tick never blocks)
**ADR Decision Summary**: The zone stores the gateway's token with a new `SessionTokenStore.AdmitToken(accountId, token, expiresAtTick)` (false for null, not 16 bytes, or all zeros), records the expected `(AccountID, CharacterID)` and starts the `CONNECTING_TIMEOUT_SECONDS` clock; with no `SessionHandshake` in time it drops the entry and reports `SessionReleased`. The real `IZoneEntryTokenValidator` looks up the admitted entry by `CharacterID` and compares with `FixedTimeEquals` locally, without blocking. Character load stays in zone admission.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Tick-thread code; EditMode tests drive ticks directly (no NGO).

**Control Manifest Rules (this layer)**:
- Required: zone `Admit` handler — `AdmitToken`, record the expected `(AccountID, CharacterID)`, start `CONNECTING_TIMEOUT_SECONDS`; on timeout drop the entry and send `SessionReleased`; an accepted `Admit` cancels the idle timer (T-7) — ADR-016 Decision 4
- Required: zone entry — `SessionHandshake` carries `CharacterID` and the token, never `AccountID`; the validator looks up by `CharacterID` and uses `FixedTimeEquals`, non-blocking — ADR-016 Decision 4
- Required: zone-side gateway code in `src/ServerLogic/Gateway/` — ADR-016 Decision 6

---

## Acceptance Criteria

- [ ] `SessionTokenStore.AdmitToken(uint accountId, byte[] token, uint expiresAtTick)` stores a copy of the token; returns false (store unchanged) for null, length ≠ 16, or all zeros; `IssueToken` is unchanged
- [ ] `AdmittedEntries` (in `src/ServerLogic/Gateway/`) records `AccountID → (CharacterID, admittedAtTick)` and offers lookup by `CharacterID`
- [ ] `GatewayAdmitHandler.Handle(Admit)` → `AdmitResult { accepted }`: rejected (`accepted=false`, reason) when the zone is not Active or occupied + admitted ≥ capacity; otherwise `AdmitToken` + entry recorded + idle timer cancelled + `accepted=true`
- [ ] `GatewayZoneEntryTokenValidator : IZoneEntryTokenValidator` replaces `RejectingZoneEntryTokenValidator` in `ZoneServerComposition` wiring: valid when an admitted entry exists for the request's `CharacterID` and the token matches (`FixedTimeEquals`); no allocation-heavy or blocking work
- [ ] AC-AUTH-21 (a): after `Admit`, `ActiveSessionTokens[AccountID]` holds the 16-byte token sent
- [ ] AC-AUTH-21 (c, d): a `SessionHandshake` with that `CharacterID` and token moves the networking session to `Connected` and calls `LoadCharacter(CharacterID)` exactly once (by zone admission); a wrong token or unknown `CharacterID` is rejected with no load
- [ ] AC-AUTH-21 (e): for a character `"Grim_Iron7"` owned by login `"alice_login"`, the session registry `CharacterName` and `EntityState.characterName` are `"Grim_Iron7"`; `"alice_login"` appears in neither (the zone never receives the username)
- [ ] No handshake within `CONNECTING_TIMEOUT_SECONDS` (in ticks) → entry and token dropped, `SessionReleased { accountId }` queued for the gateway link
- [ ] The handler and validator are pure tick-thread code: callable from tests without a socket; the real link that feeds `Admit` in is the gateway epic

---

## Implementation Notes

*Derived from ADR-016 Decision 4 and Key Interfaces:*

```csharp
public bool AdmitToken(uint accountId, byte[] token, uint expiresAtTick); // false if token is null, not 16 bytes or all zeros
```

- `SessionTokenStore` remarks explain its entry immutability and `FixedTimeEquals` use — follow them; add `AdmitToken` beside `IssueToken` with the same doc depth.
- Outgoing link messages (`AdmitResult`, `SessionReleased`) go to an `IGatewayLinkOutbox` seam (queue from the tick; the gateway epic's link thread drains it). Define the seam here if the gateway epic has not.
- "Occupied + admitted" is the same count `PlayerCount` reports (ADR-016 Decision 3 warm spare) — expose it from one place.
- `ZoneEntryAdmission` already takes an `IZoneEntryTokenValidator`; swap the wiring in `ZoneInstancingWiring` / `ZoneServerComposition`, keep `RejectingZoneEntryTokenValidator` for tests that need it or delete it if nothing uses it (note which).
- Character name: the zone reads `character_records.character_name` at load (Character Persistence); this story's test proves the username never reaches the zone (it is not in `Admit`).
- Tests in `tests/EditMode/Authentication/` (or `tests/EditMode/Gateway/` — pick one and keep Story 014 the same).

---

## Out of Scope

- Story 014: `Evict` and `Reconnect` handlers
- Gateway epic: link thread, inbox drain, `RegisterZone`, `PlayerCount` messages, zone draining (`ZoneDraining`)
- Story 017: the same flow over a real link

---

## QA Test Cases

- **AC-1**: `AdmitToken` input checks
  - When: null, 15 bytes, 17 bytes, 16 zeros, valid 16 bytes
  - Then: false ×4 (store unchanged), true; mutating the caller's array afterwards does not change the stored token
- **AC-2**: Admit accepted / rejected
  - Given: Active zone, capacity 2, one occupied
  - When: `Admit(A)`, then `Admit(B)`
  - Then: A accepted; B rejected (1 occupied + 1 admitted = 2); zone not Active → rejected
- **AC-3 (AC-AUTH-21 a,c,d)**: Handshake
  - Given: `Admit(account 1, character 77, token T)`
  - When: `SessionHandshake(77, T)`
  - Then: token store holds T for account 1; session `Connected`; `LoadCharacter(77)` once
  - Edge cases: `(77, T')` → rejected, no load; `(78, T)` → rejected, no load
- **AC-4 (AC-AUTH-21 e)**: Visible name
  - Given: persistence fake returns `character_name = "Grim_Iron7"`
  - Then: registry `CharacterName` and `EntityState.characterName` = `"Grim_Iron7"`; no string `"alice_login"` anywhere in the zone's state (it was never given)
- **AC-5**: Admission timeout
  - Given: `Admit` at tick t, no handshake
  - When: ticks advance past `CONNECTING_TIMEOUT_TICKS`
  - Then: entry gone, token gone, outbox holds `SessionReleased(1)`

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/Authentication/Authentication_ZoneAdmit_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`AccountID` location)
- Unlocks: Story 014, Story 017
