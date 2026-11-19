# Story 007: Invite Flow

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-3, CR-PS-4, Edge Cases (invite races); `networking-wire-protocol.md` `PartyInviteRequest` / `PartyInviteResponse` validation notes
**Requirement**: `TR-party-003`, `TR-party-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Requests are queued at intake and handled in the dispatch phase of the tick; timers advance on the tick (no wall clock).

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative validation; tick-based timers; deterministic tests
- Guardrail: bounded invite queue per recipient

---

## Acceptance Criteria

- [ ] **AC-1 Send**: a valid `PartyInviteRequest` (sender has a session, sender's party not full, target in this zone instance, target not in a real party, level within ±5 of the sender's party leader — or the sender if solo) sends `PartyInviteReceived` to the target (expiry tick = now + 60 s) and `PartyInviteResult(Sent)` to the sender; a solo sender becomes leader of a new `Forming` party.
- [ ] **AC-2 Reject**: level 36 vs leader 30 → `PartyInviteResult(LevelRange)` and no change (AC-PS-3); level 35 → accepted on response (AC-PS-4); target in another zone instance / unknown → `TargetNotFound`; sender not leader of a real party → `NotLeader`.
- [ ] **AC-3 Accept / decline**: accept adds the target (Story 002), re-checking capacity and level at accept time (late accept into a full party → `PartyFull` to the target, count stays 4 — AC-PS-2); decline or expiry frees the pending slot and notifies the inviter (`Declined` / `Expired`).
- [ ] **AC-4 Retry**: no response → exactly one re-send of `PartyInviteReceived` 5 s after the first, none after (outbound log shows 2 sends) (AC-PS-6).
- [ ] **AC-5 Expiry**: no response for 60 s → invite expired, pending slot freed, a new invite to the same target succeeds (AC-PS-7).
- [ ] **AC-6 Queue**: a second invite to a target with a pending invite is queued; its sender gets `PartyInviteResult(Queued)` (not an error); the target sees no second invite until the first resolves, then receives the queued one at once (AC-PS-8, AC-PS-29).

---

## Implementation Notes

- `PartyInviteService` with per-recipient pending invite + FIFO queue (bound it, e.g. `MaxPartySize × 2`; overflow → result code, decide at readiness); timers in ticks from Story 001 config.
- Register the two handlers with the inbound dispatcher (ADR-014) the way `MovementInboundRegistration` / Death & Respawn do; `SenderEntityID` from the envelope identifies the sender's slot.
- **From Story 006 readiness (2026-11-19):** this story owns the dispatcher registration of `PartyInviteRequest` (0xE172, body 4) and `PartyInviteResponse` (0xE174, body 5) — descriptor from the codec `BodySize`, a new `RpcTypeTag` if the guard chain needs one — plus an intake test: a 3-byte and a 6-byte invite body are rejected before the handler runs. Results go out as `PartyInviteResult` (0xE177, codes Sent 0 … Declined 8); a late-accept `PartyFull` goes to the accepting target.
- Invites are delivered regardless of combat state (CR-PS-4); a dead target may receive an invite (no rule against it — confirm at readiness).
- Zone-local: target lookup by CharacterID within this zone's slot pool only (ADR-010 note 2026-11-18).

---

## Out of Scope

- Story 008: `PartyStateUpdate` broadcast after the join (this story may call a "changed" hook)
- Client banner UI (presentation)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: two players in a fake pool + recording outbox → invite received with expiry tick; sender result Sent.
- **AC-2**: level table 24/25/35/36 vs 30; target absent.
- **AC-3**: accept → member added; party filled to 4 between send and accept → `PartyFull`; decline → inviter told.
- **AC-4**: tick to +100 → 2 sends; tick to +1199 → still 2.
- **AC-5**: tick to +1200 → expired; new invite accepted.
- **AC-6**: A invites X, B invites X → B Queued, X has 1 banner; A's invite times out → X receives B's at that tick.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_InviteFlow_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 004, 006
- Unlocks: Stories 011, 012
