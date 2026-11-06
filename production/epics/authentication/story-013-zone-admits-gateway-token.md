# Story 013: Zone admits a gateway token

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 8 hours (token store additions, admitted entries + timeout + T-4 keep, Admit handler, validator signature change and `ZoneEntryRequest.Account` removal across ~10 existing test files, silent close on step 3, lifecycle count, one new test file)
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

- [ ] `SessionTokenStore.AdmitToken(uint accountId, byte[] token, uint expiresAtTick)` stores a copy of the token; returns false (store unchanged) for null, length ≠ 16, or all zeros; `IssueToken` is unchanged. `expiresAtTick` = admit tick + `CONNECTING_TIMEOUT_TICKS` (metadata only — the store enforces no expiry; `AdmittedEntries` drives the drop). New `SessionTokenStore.TokenMatches(uint accountId, byte[] presented)`: `FixedTimeEquals` against the stored token, no rotation, false for no entry / null / wrong length
- [ ] `AdmittedEntries` (in `src/ServerLogic/Gateway/`) records `AccountID → (CharacterID, admittedAtTick)`, offers lookup by `CharacterID`, and exposes `ReservedCount` (entries still holding a reservation)
- [ ] `GatewayAdmitHandler.Handle(Admit { requestId, accountId, characterId, token })` → `AdmitResult { requestId, accepted, reason }` (`requestId` echoed; `AdmitRejectReason` = `None`, `NotActive`, `Full`, `InvalidToken`): rejected when the zone is not Active (`NotActive`), occupied + reserved ≥ `MAX_PLAYERS_PER_ZONE` (`Full`) or `AdmitToken` returns false (`InvalidToken`); otherwise token stored + entry recorded + `accepted=true`
- [ ] A second `Admit` for an account that already has an admitted entry replaces it: new token and `CharacterID`, timeout restarts from the new tick, reserved count unchanged, the capacity check does not count the old entry
- [ ] The zone's session count — `occupied + reserved` — is computed in one place and is what both the `Admit` capacity check and the lifecycle idle input use (the `PlayerCount` value of ADR-016 Decision 3; the message itself is the gateway epic). An accepted `Admit` at zero sessions disarms the T-7 idle timer; an entry dropped unused at zero occupied re-arms it under the existing lifecycle rule (AC-ZI-21)
- [ ] `IZoneEntryTokenValidator.IsValid(in request)` becomes `bool TryValidate(in ZoneEntryRequest request, out AccountID account)`; `ZoneEntryRequest` loses its client-claimed `Account` (CR-TOK-8: the client never sends `AccountID`) and `ZoneEntryAdmission.BeginEntry` passes the validator's `account` to `LoadCharacter`. `RejectingZoneEntryTokenValidator` and every test fake are updated to the new signature
- [ ] `GatewayZoneEntryTokenValidator` replaces `RejectingZoneEntryTokenValidator` as the default in `ZoneInstancingWiring` / `ZoneServerComposition`: valid when an admitted entry (or a T-4-kept token, below) exists for the request's `CharacterID` and `TokenMatches` is true; `account` = that entry's `AccountID`; no blocking work, no allocation per call
- [ ] Step 3 reject (`zone-instancing.md` CR-ZI-8 step 3): a wrong token or unknown `CharacterID` closes the connection with **no message** — no `ZoneFullResponse`, no `LoadCharacter`, no state change (replaces today's `JoinRejectedReason.Other` reply)
- [ ] AC-AUTH-21 (a): after `Admit`, `ActiveSessionTokens[AccountID]` holds the 16-byte token sent
- [ ] AC-AUTH-21 (c, d), at the admission seam: `BeginEntry` with that `CharacterID` and token calls `LoadCharacter(CharacterID, AccountID)` exactly once with the admitted `AccountID`; a wrong token or unknown `CharacterID` → connection closed, no load. (The wire decode and the networking `Connected` transition are not wired by any story yet — see Dependencies.)
- [ ] Reservation ends on slot allocation (step 4): the admitted entry is removed, the token stays, the session count does not change, and the timeout can no longer fire for that account (no `SessionReleased`)
- [ ] AC-AUTH-21 (e): for a character `"Grim_Iron7"` owned by login `"alice_login"`, the session registry `CharacterName` and `EntityState.characterName` are `"Grim_Iron7"`; `"alice_login"` appears in neither (the zone never receives the username)
- [ ] No handshake within `CONNECTING_TIMEOUT_TICKS` → entry and token dropped, `SessionReleased { accountId }` queued on `IGatewayLinkOutbox`
- [ ] Leaving Active (T-4, AC-ZI-35): every admitted entry loses its reservation at once and `SessionReleased` is queued for it; its token is kept until its own `CONNECTING_TIMEOUT_TICKS` runs out, then dropped (no second `SessionReleased`). A handshake with a matching kept token → `ZoneFullResponse(ZoneDoesNotExist)`, close, no `LoadCharacter`
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
- **Validator signature change (readiness 2026-11-06):** `TryValidate(in ZoneEntryRequest, out AccountID)`; `ZoneEntryRequest` drops `Account`. Callers today: `ZoneEntryAdmission.cs` (2 uses) and the request constructions in `tests/EditMode/ZoneInstancing/` (StaleSlot, SlotPools), `tests/EditMode/Integration/ZoneInstancing/` (Wiring, PlayerExit, EntryAdmission, EntryAdmissionDuplicateLoad) and `tests/EditMode/CharacterPersistence/` (ZoneApplier, ZoneRealInventory, ZoneSavePath). Their fake validators return the account the test used to put in the request, so the tests keep their meaning.
- **Silent close on step 3:** `ZoneEntryAdmission` currently replies `JoinRejectedReason.Other` when the validator fails; replace with a close through the existing connection-close seam (`_reclamation?.CloseConnection` path or the disconnector the wiring already has) and update the `EntryAdmission` tests that assert the `Other` reply. The validator must say *why* it failed only for the T-4 case: a match on a kept token while not Active → the existing `TryPreCheck` → `ZoneDoesNotExist` path already runs after a successful validation, so return true with the kept entry's account and let `TryPreCheck` answer; close after that reply.
- **Session count:** `ZoneLifecycle.OnOccupiedPlayerSlotsChanged` is the idle input today (occupied only). Feed it `occupied + reserved` from one helper (e.g. on `ZoneInstancingWiring`) called from both the slot pool listener and `AdmittedEntries` changes; the `Admit` capacity check reads the same helper.
- **Timeout tick:** `AdmittedEntries.Tick(serverTick)` runs in the wiring's tick step (before the lifecycle tick) and drops entries whose `admittedAtTick + CONNECTING_TIMEOUT_TICKS` has passed; `CONNECTING_TIMEOUT_TICKS = CONNECTING_TIMEOUT_SECONDS × TICK_RATE_HZ` comes from options, not a constant (`networking-session.md`).
- Character name: the zone reads `character_records.character_name` at load (Character Persistence); this story's test proves the username never reaches the zone (it is not in `Admit`).
- Tests in `tests/EditMode/Authentication/`; Story 014 uses the same folder.

---

## Out of Scope

- Story 014: `Evict` and `Reconnect` handlers
- Gateway epic: link thread, inbox drain, `RegisterZone`, `PlayerCount` messages, zone draining (`ZoneDraining`)
- Story 017: the same flow over a real link

---

## QA Test Cases

- **AC-1**: `AdmitToken` / `TokenMatches` input checks
  - When: null, 15 bytes, 17 bytes, 16 zeros, valid 16 bytes
  - Then: false ×4 (store unchanged), true; mutating the caller's array afterwards does not change the stored token; `TokenMatches` true for T, false for T', null, 15 bytes, unknown account
- **AC-2**: Admit accepted / rejected
  - Given: Active zone, capacity 2, one occupied
  - When: `Admit(A, requestId 5)`, then `Admit(B, requestId 6)`
  - Then: `AdmitResult(5, true, None)`; `AdmitResult(6, false, Full)` (1 occupied + 1 reserved = 2); zone not Active → `NotActive`; all-zero token → `InvalidToken`, store unchanged
  - Edge case: `Admit(A)` again with token T2 and character 78 at capacity 2, one occupied → accepted (old entry not counted), lookup by 78 finds A, by 77 finds nothing, T2 stored, timeout counted from the second tick
- **AC-3 (AC-AUTH-21 a,c,d)**: Handshake at the admission seam
  - Given: `Admit(account 1, character 77, token T)`
  - When: `BeginEntry(character 77, token T)`
  - Then: token store holds T for account 1; `LoadCharacter(77, account 1)` once; after the slot is allocated the admitted entry is gone, the token stays, session count unchanged, and advancing past the timeout queues no `SessionReleased`
  - Edge cases: `(77, T')` → connection closed, no reply, no load; `(78, T)` → closed, no reply, no load
- **AC-3b**: Idle timer
  - Given: Active zone that has held a player, now 0 occupied, idle timer armed
  - When: `Admit` accepted → Then: timer disarmed (no T-7 at the old deadline)
  - When: that entry times out unused → Then: timer re-armed from that tick
- **AC-3c (AC-ZI-35)**: T-4 keeps the token
  - Given: `Admit(1, 77, T)` at tick t, then `RequestQuit` at t+1
  - Then: `SessionReleased(1)` queued at t+1; reserved count 0; `BeginEntry(77, T)` at t+2 → `ZoneFullResponse(ZoneDoesNotExist)`, close, no `LoadCharacter`; past t + `CONNECTING_TIMEOUT_TICKS` the token is gone and no second `SessionReleased` is queued
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

**Status**: [x] Created and passing (2026-11-06) — 18 tests

---

## Dependencies

- Depends on: Story 001 (`AccountID` location)
- Unlocks: Story 014, Story 017
- **Ownership gap (readiness 2026-11-06):** no story in any epic wires the `SessionHandshake` wire decode into `ZoneEntryAdmission.BeginEntry`, gives the zone process its `SessionTokenStore` instance, or moves the networking session to `Connected` (`ZoneInstancingWiring` remarks: "belongs to the session epic"). This story builds and tests everything up to the admission seam; Story 017 cannot run until that wiring has an owner.

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 15/15 passing (AC-AUTH-21 (e) partly covered — see Deviations)
**Deviations**:
- ADVISORY: AC-AUTH-21 (e) is checked on the applied record's `CharacterName` and the slot, not the session registry / `EntityState.characterName`; the session wiring that reaches those has no owner (TD-095).
- ADVISORY: `BeginEntry` also closes the connection after its not-Active `ZoneDoesNotExist` reply (CR-ZI-8 steps 3/4); the post-load re-check still replies without closing.
- Code review fixes: an admitted entry does not time out while its `LoadCharacter` is in flight (`ZoneEntryAdmission.IsLoadPending`); an `Admit` for a character another account holds releases that account's entry (token invalidated, `SessionReleased`, warning); `SetReservationSink` is set-once.
- Out of the story's file list: 7 existing Zone Instancing / Character Persistence test files updated for `TryValidate` and the removed `ZoneEntryRequest.Account`; two assertions changed for the silent close / close after `ZoneDoesNotExist`.
**Test Evidence**: Logic — `tests/EditMode/Authentication/Authentication_ZoneAdmit_tests.cs` (18 tests); full EditMode suite 6598: 6469 passed, 0 failed, 129 skipped
**Code Review**: Complete (2026-11-06, CHANGES REQUIRED → 4 fixes applied)
**Tech debt**: TD-095
