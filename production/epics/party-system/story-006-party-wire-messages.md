# Story 006: Party Wire Messages

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 5 h (raised from 4 h at readiness 2026-11-19: 9 codecs + wire GDD and ADR-014 table amendments)
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` §Party System Messages and §PartyMemberStatus; `design/gdd/party-system.md` Interactions (Networking Core rows), CR-PS-4, CR-PS-5 (XP-ineligible notice), CR-PS-9
**Requirement**: `TR-party-011` (schemas)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Every client→server type is classified (R-OD request, held or not, max body bytes) and registered for dispatch; `PartyInviteRequest` 4 B and `PartyInviteResponse` 5 B are listed. Server→client R-OD messages go through the outbox with a routing entry.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: codecs in Foundation `WireProtocol`, little-endian, `TryRead`/`Write` on spans (as existing codecs)
- Required: R-OD MessageTypeIDs in 0xE000–0xEFFF; 0xE170–0xE17F is reserved for this epic (free at readiness 2026-11-19 — no 0xE17x in `src/`)
- Guardrail: zero allocation encode / decode

---

## Readiness Decisions (2026-11-19)

1. **Inbound registration splits across stories (G1).** `IInboundRequestDispatcher.Register(descriptor, handler)` needs a handler, and the handlers are Stories 007 / 008. This story adds the routing entries (both directions), the `BodySize` / `MaxBodySize` constants and the ADR-014 table rows; Story 007 registers `PartyInviteRequest` / `PartyInviteResponse` and Story 008 registers `PartyLeaveRequest` / `PartyDisbandRequest` with the dispatcher, each with its intake rejection test (malformed / oversized body).
2. **Unknown status byte is substituted, not rejected (G2).** The wire spec (§PartyMemberStatus) says: unknown bytes → `Online = 0`, log, continue. The `PartyStateUpdate` decoder follows it: returns true, the slot reads `Online`, and an `out bool hadUnknownStatus` reports it so the client can log. Other malformed input is rejected (AC-4).
3. **New message schemas and IDs (G3)** — the table in AC-2 / AC-3. Leave and disband requests have empty bodies: the server knows the sender's party from `SenderEntityID`. `PartyInviteResult` is also the answer to a non-leader `PartyDisbandRequest` (`NotLeader`, `targetCharacterId = 0`; Story 008 AC-2). The invite-failure "no error response at MVP" sentence in the wire GDD is replaced, since `PartyInviteResult` is that response.

---

## Acceptance Criteria

- [x] **AC-1 Existing schemas**: codecs for `PartyStateUpdate` (56-byte body: partyId 4, sequenceId 4, isSoloParty 1, memberCount 1, leaderIndex 1, rrNextIndex 1, 4 × 11-byte `PartyMemberSlot` {characterId 4, entityId 4, level 1, status 1, xpEligible 1}), `PartyDisbanded` (4: partyId), `PartyInviteRequest` (4: targetCharacterId), `PartyInviteReceived` (15–39: inviterCharacterId 4, inviterEntityId 4, inviterName ushort length + ≤ 24 UTF-8 bytes, inviterLevel 1, inviteExpiryTick 4) and `PartyInviteResponse` (5: inviterCharacterId 4, accepted 1) match the wire spec field for field, in that order, little-endian; round-trip tests and golden byte arrays. The slot status is `IronGrind.PartySystem.PartyMemberStatus` (Story 004), not a new enum.
- [x] **AC-2 New schemas**: codecs for the four new messages, and the wire GDD §Party System Messages amended with their schemas, sizes (body and standalone) and IDs:
  - `PartyLeaveRequest` (C→S, 0-byte body; 14 B standalone) — leave the sender's current party.
  - `PartyDisbandRequest` (C→S, 0-byte body; 14 B standalone) — leader only.
  - `PartyInviteResult` (S→C, 5-byte body; 15 B standalone): `PartyInviteResultCode resultCode` (byte: Sent 0, Queued 1, PartyFull 2, LevelRange 3, AlreadyInParty 4, TargetNotFound 5, NotLeader 6, Expired 7, Declined 8) + `CharacterID targetCharacterId` (4; the invited character, or 0 when answering a `PartyDisbandRequest`). Sent to the inviter (Sent, Queued, LevelRange, AlreadyInParty, TargetNotFound, NotLeader, Expired, Declined) or to the accepting target (PartyFull on a late accept).
  - `PartyXpIneligibleNotice` (S→C, 4-byte body; 14 B standalone): `PartyID partyId` — client shows *"You are outside your party's level range. XP grants are suspended."* (CR-PS-5); sent by Story 003.
  - The `PartyInviteRequest` note "Validation failure: server discards silently and logs — no error response at MVP" is replaced with "Validation failure: server sends `PartyInviteResult` with the failure code".
  - A note that `PartyMemberStatusChanged` (named in `party-system.md`) has no schema: status rides in `PartyStateUpdate`.
- [x] **AC-3 IDs and routing**: the nine IDs below are constants on the codecs, distinct, and each is registered in `MessageRoutingRegistry` under a comment "Party System Story 006 … 0xE170-0xE17F reserved for the epic" (R-OD priority path, not cap-exempt, as Death & Respawn Story 005); ADR-014's classification table gains rows for `PartyLeaveRequest` and `PartyDisbandRequest` (R-OD, request, not held, body 0) and the existing party rows stay (4, 5). Each client→server codec exposes `BodySize` (or `MaxBodySize`) for the Story 007 / 008 dispatcher descriptors.

  | ID | Message | Direction (`MessageDirection`) | Body |
  |---|---|---|---|
  | 0xE170 | `PartyStateUpdate` | `ServerToParty` | 56 |
  | 0xE171 | `PartyDisbanded` | `ServerToParty` | 4 |
  | 0xE172 | `PartyInviteRequest` | `ClientToServer` | 4 |
  | 0xE173 | `PartyInviteReceived` | `ServerToOwningClient` | 15–39 |
  | 0xE174 | `PartyInviteResponse` | `ClientToServer` | 5 |
  | 0xE175 | `PartyLeaveRequest` | `ClientToServer` | 0 |
  | 0xE176 | `PartyDisbandRequest` | `ClientToServer` | 0 |
  | 0xE177 | `PartyInviteResult` | `ServerToOwningClient` | 5 |
  | 0xE178 | `PartyXpIneligibleNotice` | `ServerToOwningClient` | 4 |

- [x] **AC-4 Decode safety**: `TryRead` returns false without throwing on: each fixed-size body one byte short; `PartyInviteReceived` truncated inside the name, with a name length > 24, or with trailing bytes missing after the name; `PartyStateUpdate` with `memberCount` > 4; `PartyInviteResult` with a result code > 8; `PartyInviteResponse` with an `accepted` byte other than 0 / 1. A `PartyStateUpdate` with an unknown status byte (e.g. 7) decodes (true), that slot reads `Online` and `hadUnknownStatus` is true; with all statuses valid `hadUnknownStatus` is false. Encoders write exactly `BodySize` bytes (or the computed `PartyInviteReceived` size) and return false on a short destination.

---

## Implementation Notes

- Follow the Death & Respawn Story 005 codecs (0xE160 block): one `readonly struct` per message in `src/Foundation/Networking/WireProtocol/` with `MessageTypeId` and `BodySize` constants, `TryRead`/`Write` on spans, tests in the style of `tests/EditMode/DeathAndRespawn/DeathAndRespawn_WireMessages_tests.cs`.
- Name length: reuse `EntityState.MaxNameBytes` (24) or a party constant equal to it; the encoder cuts at a UTF-8 character boundary like `EntityStateCodec` and never allocates (write from a span / pre-encoded bytes).
- `PartyInviteResultCode` is a new `byte` enum next to the codec in Foundation `WireProtocol` (wire enum, values fixed by AC-2).
- Empty-body codecs (`PartyLeaveRequest`, `PartyDisbandRequest`): `BodySize = 0`, `TryRead` accepts an empty span; a non-empty body is the dispatcher's oversize check (Story 008), not this codec's.
- `PartyMemberHealthUpdate` (0x0305) already exists; Story 010 uses it.
- Update `AssemblyBoundaryLists.cs` if the boundary test flags the new public types (the `PartyMemberStatus` consumer comment already names this story).

---

## Out of Scope

- Stories 007 / 008: handlers, dispatcher registration, intake rejection tests, sending
- Story 003: sending `PartyXpIneligibleNotice`
- Story 010: health stream

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-19.*

- **AC-1**: round-trip each of the five messages; golden byte arrays for `PartyStateUpdate` (2 members: slot 0 Online leader, slot 1 Dead, xpEligible true / false, slots 2–3 all zero) and `PartyInviteReceived` (empty name → 15 bytes; 24-byte name → 39 bytes; a multi-byte UTF-8 name).
- **AC-2**: the four new messages round-trip; golden bytes for `PartyInviteResult` (code 3, target 0x01020304) and `PartyXpIneligibleNotice`; empty-body codecs read an empty span; doc check of the wire GDD at review.
- **AC-3**: registry lookups for all nine IDs return the table's direction, priority path, not cap-exempt; the IDs are distinct and none collides with another registry entry; all nine fall in 0xE170–0xE17F; ADR-014 table rows present (doc check).
- **AC-4**: each malformed case returns false and does not throw; unknown status → true + `Online` + flag; short destination → `Write` false.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_WireMessages_tests.cs` — must exist and pass

**Status**: [x] Created — 48 cases, passing

---

## Dependencies

- Depends on: Story 004 (Complete — `PartyMemberStatus` in `src/Foundation/PartySystem/`)
- Unlocks: Stories 003 (XP-ineligible notice), 007, 008

---

## Completion Notes
**Completed**: 2026-11-19
**Criteria**: 4/4 passing (all automated)
**Deviations** (advisory, documented in code and pinned by tests):
- `PartyInviteReceived.TryRead` allocates the name string (same as `EntityState`) — exception to the zero-allocation decode guardrail.
- `isSoloParty` / `xpEligible` decode leniently (non-zero = true); `leaderIndex` / `rrNextIndex` are not range-checked by the decoder (consumer checks, per wire spec). `PartyInviteResponse.accepted` is strict (0 / 1).
- `PartyStateUpdate` exposes slots as `Member0`–`Member3` + `GetMember(i)` (no array, allocation-free).
- `PartyMemberStatusChanged` has no wire schema: status rides in `PartyStateUpdate` (wire GDD note; `party-system.md` note added; the HUD event of that name is client-side, raised from `PartyStateUpdate` diffs).
**Test Evidence**: Logic — `tests/EditMode/PartySystem/PartySystem_WireMessages_tests.cs` (34 methods / 48 cases); PartySystem 186/186; full EditMode `!Database` 8280/8280 (2026-11-19 12:58:14Z)
**Code Review**: Complete (`/code-review` 2026-11-19, CHANGES REQUIRED → all fixed); LP-CODE-REVIEW and QL-TEST-COVERAGE skipped (lean)
**Also changed**: `MessageRoutingRegistry.cs` (9 entries), `networking-wire-protocol.md` §Party System Messages, ADR-014 table (+2 rows), `AssemblyBoundaryLists.cs` (11 entries). Dispatcher registration of the four request types is Stories 007 / 008.
