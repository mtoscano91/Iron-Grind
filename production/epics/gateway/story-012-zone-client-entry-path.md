# Story 012: Zone-side client entry path (TD-095)

> **Epic**: Gateway
> **Status**: Blocked — `SessionHandshake` client→server wire format not specified (OQ-NC-SER-2, no MessageTypeId). Unblock with a `/quick-design` session that fixes the body and updates `networking-wire-protocol.md` (+ `networking-channel-contract.md`, entities registry), then `/story-readiness`.
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 (entry sequence); `design/gdd/authentication.md` — AC-AUTH-21 (e); `design/gdd/networking-session-token.md` — CR-TOK-8/9
**Requirement**: `TR-zi-018` (entry after registration); TD-095
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 4 — tokens owned by the zone), with ADR-011 (dispatch on the tick)
**ADR Decision Summary**: The client's `SessionHandshake` carries `CharacterID` and the token, never `AccountID`. The real `IZoneEntryTokenValidator` looks up the admitted entry by `CharacterID` and compares with `CryptographicOperations.FixedTimeEquals` in the zone's local `SessionTokenStore`, without blocking. Character load stays in zone admission.

**Engine**: Unity 6.3 LTS (server build) | **Risk**: LOW
**Engine Notes**: NGO custom messages — follow the existing inbound dispatch (`src/ServerLogic/Networking/InboundDispatch/`); no new NGO API.

**Control Manifest Rules (this layer)**:
- Required: "Zone entry" bullet of the manifest's ADR-016 block
- Required: R-OD message ids in 0xE000–0xEFFF (check at readiness against the delivery class)

---

## Blocker

`networking-wire-protocol.md` still has `SessionHandshake` as a stub ("schema pending OQ-NC-SER-2"), and `MessageRoutingRegistry` has no id for it. Proposal agreed at `/create-stories` (2026-11-09), to be written into the GDD by a `/quick-design` session:

```
SessionHandshake (C→S, R-OD)
  characterId   : uint32  (CharacterID)
  targetZoneId  : uint32  (ZoneID)
  sessionToken  : byte[16]
  = 24-byte body + 10-byte envelope
  no SenderEntityID, never AccountID
  MessageTypeId: 0xE0xx (R-OD range)
```

The rest of OQ-NC-SER-2 (the `SessionReady` S→C fields) is not part of this story.

---

## Acceptance Criteria

*Final wording to be confirmed at readiness, after the schema is in the GDD:*

- [ ] `SessionHandshake` is decoded by the zone's inbound dispatch into `ZoneEntryRequest(clientId, characterId, targetZoneId, token)` and handed to `ZoneEntryAdmission.BeginEntry`; a malformed body or wrong length closes the connection with no message (CR-TOK-9)
- [ ] The zone process builds one `SessionTokenStore`, shared by the `Admit` handler (`AdmitToken`) and the `GatewayZoneEntryTokenValidator`; the wiring default is replaced
- [ ] On successful entry the networking session moves to `Connected` (`ConnectionStateMachine`) and the session registry holds the character name
- [ ] AC-AUTH-21 (e): after entry, the session registry `CharacterName` and the player's `EntityState.characterName` equal the registered character name (the Story 013 check on the loaded record is replaced)
- [ ] A handshake with a wrong token closes the connection with no message; a valid one for an admitted entry proceeds to `LoadCharacter`
- [ ] TD-095 closed in `docs/tech-debt-register.md`

---

## Implementation Notes

*Derived from ADR-016 Decision 4 and TD-095:*

- TD-095 files: `src/ServerLogic/ZoneServer/ZoneInstancingWiring.cs`, `ZoneServerComposition.cs`, `src/ServerLogic/Networking/ConnectionStateMachine/`.
- `ConnectionMessageDescriptor` already notes `SessionHandshake` has no `SenderEntityID` after the envelope.
- Client-side sending of `SessionHandshake` is Authentication Story 017 / client work — this story is the zone side only; tests feed encoded bytes into the dispatch.

---

## Out of Scope

- `SessionReady` schema (rest of OQ-NC-SER-2)
- Client sending the handshake (Authentication Story 017)

---

## QA Test Cases

*To be completed at readiness once the schema is fixed. Minimum:*

- **AC-1**: encoded 24-byte body → `BeginEntry` called with the same character, zone, token; 23-byte body → connection closed, no `BeginEntry`
- **AC-4 (AC-AUTH-21e)**: registered character "Brakka" → after entry, registry name and `EntityState.characterName` = "Brakka"
- **AC-5**: wrong token → closed, no message, no `LoadCharacter`

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Gateway/ZoneClientEntry_tests.cs` — must pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: the `SessionHandshake` schema spec (`/quick-design`); Story 011
- Unlocks: Authentication Story 017
