# Story 012: Zone-side client entry path (TD-095)

> **Epic**: Gateway
> **Status**: Complete (2026-11-11, `/story-done` COMPLETE WITH NOTES)
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 steps 2–3 (entry sequence); `design/gdd/authentication.md` — AC-AUTH-21 (e); `design/gdd/networking-session-token.md` — CR-TOK-8/9; `design/gdd/networking-wire-protocol.md` — `SessionHandshake` schema
**Quick Spec**: `design/quick-specs/session-handshake-schema-2026-11-11.md`
**Requirement**: `TR-zi-007` (CR-ZI-8 steps 3–10 — token validation → `LoadCharacter` → capacity + slot → …); TD-095
*(TR-zi-* IDs live in `production/epics/zone-instancing/EPIC.md`, not yet in `docs/architecture/tr-registry.yaml` — TD-098. Earlier drafts cited TR-zi-018, which is the S10 registration of Story 011.)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 4 — tokens owned by the zone), with ADR-014 (connection-level messages) and ADR-011 (dispatch on the tick)
**ADR Decision Summary**: The client's `SessionHandshake` carries `CharacterID` and the token, never `AccountID`. The real `IZoneEntryTokenValidator` looks up the admitted entry by `CharacterID` and compares with `CryptographicOperations.FixedTimeEquals` in the zone's local `SessionTokenStore`, without blocking. Character load stays in zone admission.

**Engine**: Unity 6.3 LTS (server build) | **Risk**: LOW
**Engine Notes**: NGO custom messages — follow the existing inbound dispatch (`src/ServerLogic/Networking/InboundDispatch/`); no new NGO API.

**Control Manifest Rules (this layer)**:
- Required: "Zone entry" bullet of the manifest's ADR-016 block
- Required: R-OD message ids in 0xE000–0xEFFF — `0xE136` is inside the range (checked at readiness 2026-11-11)
- Required: every client→server type gets its `MessageRoutingRegistry` row, its ADR-014 Decision 1 classification and its registration in the same story (manifest, ADR-014 block)

---

## Wire Format (decided 2026-11-11)

From `networking-wire-protocol.md` and the quick spec:

```
SessionHandshake (R-OD, client → server, 24-byte body; 34 bytes standalone)
  characterId   : uint32 LE (CharacterID, never 0)
  targetZoneId  : uint32 LE (ZoneID from LoginResponse)
  sessionToken  : byte[16]
  MessageTypeID : 0xE136
  10-byte envelope only — no SenderEntityID; never AccountID
  connection-level (ADR-014): not session-ready gated, never held, never in the inbox
```

Decode rules, before CR-ZI-8 step 3:
1. Body length ≠ 24, `characterId == 0`, or an all-zero `sessionToken` → connection closed with no message; no `BeginEntry`.
2. Otherwise → `ZoneEntryAdmission.BeginEntry(ZoneEntryRequest(clientId, characterId, targetZoneId, token))`. Admission keeps its existing wrong-zone rule: `targetZoneId` ≠ this zone's `ZoneID` → `ZoneFullResponse(ZoneDoesNotExist)` before the token check (user decision 2026-11-11 at `/dev-story`). Then CR-ZI-8 step 3 unchanged.

Session states (user decision 2026-11-11 at `/dev-story`): `ConnectionStateMachine.EnterConnecting(account)` right after the token validates in `BeginEntry`; `CompleteAuthSuccess(account, character)` when the slot is allocated (`OnPlayerAdmitted`); `FailConnecting` when the load or the re-check rejects the entry after that.

---

## Acceptance Criteria

- [ ] **AC-1 Registration**: `SessionHandshake` has a `MessageRoutingRegistry` row at `0xE136` (R-OD, client→server) and a connection-level descriptor with `CarriesSenderEntityId = false` and a body bound large enough that every wrong-length body reaches the decoder and is closed (the dispatcher only drops bodies above the bound — found at `/dev-story`: bound = `MAX_INBOUND_MESSAGE_BYTES` − envelope = 390); `ZoneEntryAck` (0xE134) and `ZoneSnapshotRequest` (0xE131) keep their ids and behaviour.
- [ ] **AC-2 Decode**: a 24-byte body is decoded into `ZoneEntryRequest(clientId, characterId, targetZoneId, token)` with the same values and handed to `ZoneEntryAdmission.BeginEntry` exactly once.
- [ ] **AC-3 Malformed / wrong zone**: a body of 23 or 25 bytes, `characterId = 0` or an all-zero token closes the connection with no message sent to the client, and `BeginEntry` is not called (CR-TOK-9). A well-formed handshake whose `targetZoneId` is not this zone's gets `ZoneFullResponse(ZoneDoesNotExist)` carrying the sent zone id, with no token check and no `LoadCharacter`.
- [ ] **AC-4 Shared token store**: the zone process builds one `SessionTokenStore`, used by both the `Admit` handler (`AdmitToken`) and the `GatewayZoneEntryTokenValidator`; the composition no longer uses the wiring default validator (TD-095).
- [ ] **AC-5 Token check**: for an admitted entry, a handshake with a wrong token (one byte changed) closes the connection with no message and `LoadCharacter` is not called; the same handshake with the admitted token calls `LoadCharacter` once for that `CharacterID`.
- [ ] **AC-6 Connected**: after a successful entry, the networking session for that client is `Connected` (`ConnectionStateMachine`), and the session registry `CharacterName` and the player's `EntityState.characterName` both equal the registered character name (AC-AUTH-21 (e); replaces the Story 013 check on the loaded record).
- [ ] **AC-7 Tech debt**: TD-095 marked resolved in `docs/tech-debt-register.md`; TD-099 (Story 011 AC-5 tested through the direct slot path) re-checked — closed if the admitted-entry → slot conversion is now tested through a real handshake.

---

## Implementation Notes

*Derived from ADR-016 Decision 4, ADR-014 and TD-095:*

- TD-095 files: `src/ServerLogic/ZoneServer/ZoneInstancingWiring.cs`, `ZoneServerComposition.cs`, `src/ServerLogic/Networking/ConnectionStateMachine/`.
- New message type: `src/Foundation/Networking/WireProtocol/SessionHandshake.cs` (+ codec), following `ZoneEntryAck` / `ZoneSnapshotRequest` (connection-level, recorded in the receive callback).
- `ConnectionMessageDescriptor` already notes `SessionHandshake` has no `SenderEntityID` after the envelope.
- The zone's own `ZoneID` for the `targetZoneId` check comes from the composition (the id given at launch), not from the message.
- Threads (confirmed at `/code-review` 2026-11-11): the NGO message handler, the connection listener callbacks and the tick loop all run on the Unity main thread, so `SessionHandshakeIntake` has no locks (same as `ZoneEntryReclamation`); NGO's `DisconnectClient` is deferred, so no listener event fires inside `Drain`.
- Token comparison stays in `SessionTokenStore` with `CryptographicOperations.FixedTimeEquals`; the decode step never compares tokens.
- Client-side sending of `SessionHandshake` is Authentication Story 017 / client work — this story is the zone side only; tests feed encoded bytes into the dispatch.

**Performance**: no frame-budget impact expected — one 24-byte connection-level message per zone entry, decoded in the receive callback; `LoadCharacter` is already asynchronous (ADR-011).

---

## Out of Scope

- `SessionReady` schema and id (rest of OQ-NC-SER-2)
- Renaming the server→client "session handshake" wording in `networking-session.md` / `consumable-use-system.md` / `OnSessionHandshakeEmitted` (part of the `SessionReady` design)
- Client sending the handshake (Authentication Story 017)
- Token-path reconnect through a ghost session (`Reconnect` → `SessionHandshake` → `Reconnecting`) beyond what the existing validator already does

---

## QA Test Cases

- **AC-1**: registry lookup of `0xE136` → row with R-OD, client→server; descriptor `MaxBodyBytes 24`, `CarriesSenderEntityId false`; lookups of `0xE131` and `0xE134` unchanged.
- **AC-2**: encode `(characterId 501, targetZoneId = this zone, token 0x01..0x10)` → `BeginEntry` called once with the same three values and the sending `clientId`.
- **AC-3** (one case each): 23-byte body; 25-byte body; `characterId = 0`; all-zero token → connection closed, nothing sent to the client, `BeginEntry` call count 0. `targetZoneId = this zone + 1` (well-formed) → one `ZoneFullResponse(ZoneDoesNotExist)` with that zone id, `LoadCharacter` count 0.
- **AC-6 (states)**: after a valid token, the session is `Connecting` while the load is pending; after admission it is `Connected`; when the load fails it is not `Connected` (`FailConnecting` applied).
- **AC-4**: composition test — the `Admit` handler's `AdmitToken` and the entry validator see the same store (a token admitted through `Admit` validates through the validator); the default validator type is not in the built graph.
- **AC-5**: `Admit(account, character 501, token T)`; handshake with T with byte 7 flipped → closed, no message, `LoadCharacter` count 0; handshake with T → `LoadCharacter` count 1 for character 501.
- **AC-6**: registered character "Brakka" admitted and entered → session state `Connected`; registry `CharacterName == "Brakka"`; `EntityState.characterName == "Brakka"`.
- **AC-7**: tech-debt register rows TD-095 (and TD-099 if covered) updated.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Gateway/ZoneClientEntry_tests.cs` — must pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011 (Complete, 2026-11-11); `SessionHandshake` quick spec (written 2026-11-11, commit `600be7f`)
- Unlocks: Authentication Story 017

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 7/7 passing; AC-6 partly — the session `Connecting` → `Connected` path with the character and the persistence name ("Brakka", composition level) are tested; the `EntityState.characterName` half of AC-AUTH-21 (e) moved to TD-100 (no production `IZoneEntityStateSource`; user decision).
**Deviations** (advisory): wrong `targetZoneId` keeps admission's `ZoneFullResponse(ZoneDoesNotExist)` (user decision; quick spec, wire protocol, channel contract, registry updated); descriptor body bound 390, not 24 (the dispatcher only drops oversized bodies, so wrong-length handshakes must reach the decoder to be closed); `Connecting` is entered just before the load starts (after the capacity pre-check), not at token validation; `TR-zi-007` not in `tr-registry.yaml` (TD-098). Extra test files touched for the fan-out / new types: `AssemblyBoundaryLists.cs`, `ZoneInstancing_Wiring_tests.cs`, `MovementSystem_PipelineWiring_tests.cs`, `ZoneServer_CompositionWiring_tests.cs`.
**Test Evidence**: `tests/EditMode/Gateway/ZoneClientEntry_tests.cs` (32), `tests/EditMode/Networking/ClientConnectionListenerFanOut_tests.cs` (6), `tests/EditMode/Networking/ZoneServer_CompositionHandshake_tests.cs` (1). Full EditMode batch run 2026-11-11: 6847 / 6718 passed / 0 failed / 129 skipped.
**Code Review**: Complete — `/code-review` 2026-11-11 (unity-specialist APPROVED WITH SUGGESTIONS, qa-tester GAPS), all findings applied ("fix all").
**Tech debt**: TD-095, TD-099 resolved; TD-100 (production entity state source) and TD-101 (zone-side session end inputs, connecting timeout) logged.
