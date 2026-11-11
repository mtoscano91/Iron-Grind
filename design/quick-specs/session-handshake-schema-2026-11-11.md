# Quick Design Spec: SessionHandshake client→server body

**Type**: Tweak
**System**: Networking (wire protocol)
**GDD Reference**: `design/gdd/networking-wire-protocol.md` — Session messages (`SessionHandshake` stub), OQ-NC-SER-2
**Date**: 2026-11-11

## Change Summary

Fixes the `SessionHandshake` client→server body and its `MessageTypeID`. This resolves the handshake half of OQ-NC-SER-2 and unblocks Gateway Story 012 (zone-side client entry path, TD-095).

## Motivation

ADR-016 Decision 4, `networking-session-token.md` CR-TOK-9 and `zone-instancing.md` CR-ZI-8 step 3 already define what the zone does with a handshake (`CharacterID` → admitted entry or ghost → `FixedTimeEquals` on the token). Only the bytes on the wire and the id were missing, so the zone's inbound dispatch cannot decode it and Authentication Story 017 (end to end) cannot run.

## Design Delta

Current GDD says (`networking-wire-protocol.md`, session messages):

> ```
> SessionHandshake {
>     // Full schema pending Character Persistence GDD and Authentication ADR (OQ-NC-SER-2)
>     // Fields: session token, CharacterID, target ZoneID
>     // Wire size: TBD
> }
> ```

This spec changes that to:

```
SessionHandshake (R-OD, client → server, 24-byte body; 34 bytes standalone)
SessionHandshake {
    CharacterID characterId;    // 4 bytes, uint32 LE, never 0
    ZoneID      targetZoneId;   // 4 bytes, uint32 LE, the ZoneID from LoginResponse
    byte[16]    sessionToken;   // 16 bytes, fixed length, raw bytes from LoginResponse
}
```

## New Rules / Values

- **MessageTypeID `0xE136`** — next free id in the zone-entry block (`0xE130`–`0xE135`, after `ZoneSnapshotRequest` 0xE135); inside the R-OD priority range `0xE000`–`0xEFFF`.
- **Connection-level message** (ADR-014 Decision 1): not guarded by the session-ready gate, never held, never enters the inbox. 10-byte envelope only — **no `SenderEntityID`** (the client has no entity yet). **Never carries `AccountID`** (ADR-016 Decision 4, CR-AUTH-2).
- **Decode rules** (zone side, before CR-ZI-8 step 3):
  1. Body length ≠ 24, `characterId == 0`, or `sessionToken` all zeros → malformed: close the connection with no message (CR-TOK-9). No `BeginEntry`.
  2. `targetZoneId` ≠ this zone's `ZoneID` → close the connection with no message, same as "no record" (covers a stale port reused by a new zone process). No `BeginEntry`. *New rule (user decision 2026-11-11).*
  3. Otherwise → `ZoneEntryAdmission.BeginEntry(ZoneEntryRequest(clientId, characterId, targetZoneId, token))`; CR-ZI-8 step 3 applies unchanged (record lookup, `FixedTimeEquals`, `ZoneDoesNotExist` when the zone is not Active, then `LoadCharacter`).
- A handshake reaching a zone still in Loading keeps its existing rule (`ZoneFullResponse(ZoneDoesNotExist)`, `zone-instancing.md` EC-ZI-1 area); unchanged by this spec.

## Affected Systems

| System | Impact | Action Required |
|--------|--------|-----------------|
| `networking-wire-protocol.md` | Stub replaced | Schema + id + decode rules; OQ-NC-SER-2 partially resolved (handshake half) |
| `networking-channel-contract.md` | "Schema pending" in CCR-2 and CCR-3 rows | Point to the schema, `0xE136` |
| `design/registry/entities.yaml` | No `SessionHandshake` entry | New entry |
| `networking-session-token.md` | Dependencies row "propagation required" | Mark propagated |
| Gateway Story 012 | Blocked on this schema | `/story-readiness` next |

## Acceptance Criteria

- [ ] An encoded 24-byte body reaches `BeginEntry` with the same `characterId`, `targetZoneId` and token
- [ ] A 23- or 25-byte body, `characterId = 0`, or an all-zero token closes the connection with no message and no `BeginEntry`
- [ ] A `targetZoneId` other than this zone's closes the connection with no message and no `BeginEntry`
- [ ] `0xE136` has a `MessageRoutingRegistry` row and a connection-level descriptor (`maxBodyBytes 24`, `carriesSenderEntityId false`)
- [ ] No regression: `ZoneEntryAck` (0xE134) and `ZoneSnapshotRequest` (0xE135) keep their ids and behaviour

## Out of Scope (stays under OQ-NC-SER-2)

- `SessionReady` S→C fields (gold, version, HP/MP, level, `heldFreePoints`, requestId continuity fields) and its id.
- Wording conflict: `networking-session.md` (CR-NET-6.4 step 3 and the "Session handshake vs. SessionReady" paragraph) and `consumable-use-system.md` (`SessionHandshake.lastSeenUseItemRequestId`, AC-CUS-32) call the server→client state message "session handshake". By CCR-2 those fields belong to `SessionReady`; fix when the `SessionReady` half is designed.

## GDD Update Required?

Yes — applied 2026-11-11 with this spec (files in the table above).
