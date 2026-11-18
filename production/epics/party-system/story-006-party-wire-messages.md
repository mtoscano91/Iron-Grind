# Story 006: Party Wire Messages

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` §Party System Messages; `design/gdd/party-system.md` Interactions (Networking Core rows), CR-PS-4, CR-PS-9
**Requirement**: `TR-party-011` (schemas)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Every client→server type is classified (R-OD request, max body bytes) and registered for dispatch; `PartyInviteRequest` 4 B and `PartyInviteResponse` 5 B are listed. Server→client R-OD messages go through the outbox with a routing entry.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: codecs in Foundation `WireProtocol`, little-endian, `TryRead`/`Write` on spans (as existing codecs)
- Guardrail: zero allocation encode / decode

---

## Acceptance Criteria

- [ ] **AC-1 Schemas**: codecs for `PartyStateUpdate` (56-byte body, 4 × 11-byte `PartyMemberSlot`), `PartyDisbanded` (4), `PartyInviteRequest` (4), `PartyInviteReceived` (15–39, name ≤ 24 UTF-8 bytes), `PartyInviteResponse` (5) match the wire spec field for field; round-trip tests and exact byte layouts.
- [ ] **AC-2 New messages** (wire-protocol amendment, same story): `PartyLeaveRequest` (C→S, 0 or 4-byte body), `PartyDisbandRequest` (C→S), `PartyInviteResult` (S→C: result code — Sent, Queued, PartyFull, LevelRange, AlreadyInParty, TargetNotFound, NotLeader, Expired, Declined — plus target CharacterID) and, if Story 004 readiness kept it, an XP-ineligible notice. Schemas written into `networking-wire-protocol.md` §Party System Messages with sizes.
- [ ] **AC-3 IDs and routing**: MessageTypeIDs in 0xE170–0xE17F (R-OD), registered in `MessageRoutingRegistry` (server→client entries) and the ADR-014 inbound classification (client→server, request, max body bytes) — malformed / oversized bodies rejected at intake.
- [ ] **AC-4 Decode safety**: truncated bodies, a name longer than 24 bytes, an invalid status byte and an invalid result code are rejected without throwing.

---

## Implementation Notes

- Follow the Death & Respawn Story 005 codecs (0xE160 block) for file layout, routing registration and tests.
- `PartyMemberStatusChanged` (named in the GDD) has no schema: status rides in `PartyStateUpdate` — note in the GDD at story-done.
- `PartyMemberHealthUpdate` (0x0305) already exists; Story 010 uses it.
- Check the message id range rule (memory: R-OD must be 0xE000–0xEFFF).

---

## Out of Scope

- Stories 007 / 008: handlers and sending
- Story 010: health stream

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: round-trip each message; golden byte arrays for `PartyStateUpdate` (with 2 members) and `PartyInviteReceived` (empty and 24-byte names).
- **AC-2**: new schemas round-trip; GDD section updated (doc check at review).
- **AC-3**: registry lookups by id return the expected direction / class; ids unique across the registry.
- **AC-4**: each malformed case returns false.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_WireMessages_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None (can run in parallel with 001–005)
- Unlocks: Stories 007, 008
