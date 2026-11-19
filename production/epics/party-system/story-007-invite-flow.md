# Story 007: Invite Flow

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 6 h
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

- [x] **AC-1 Send**: a valid `PartyInviteRequest` (sender registered in this zone and solo or leader of a real party; that party not full; target registered in this zone and not in a real party; target level within ±5 of the sender's party leader, or of the sender if solo) sends `PartyInviteReceived` to the target (inviter id, entity, name from `ICharacterNameLookup`, level, expiry tick = now + `InviteTimeoutTicks`) and `PartyInviteResult(Sent, target)` to the sender. No party is created at send: a solo sender stays solo until an accept (I2).
- [x] **AC-2 Reject at send**: target level 36 vs leader 30 → `LevelRange` and no change (AC-PS-3); 24 → `LevelRange`, 25 and 35 → `Sent` (inclusive); target unknown or not in this zone → `TargetNotFound`; sender a non-leader member of a real party → `NotLeader`; sender's real party at `MaxPartySize` → `PartyFull`; target in a real party, target is the sender, or target already in the sender's party → `AlreadyInParty`. A rejection creates no pending invite.
- [x] **AC-3 Accept**: on `PartyInviteResponse(accepted)` matching the target's pending invite, the server re-checks at accept time: inviter still registered and solo or leader of a real party (else `Expired` to the target); target not in a real party (else `AlreadyInParty` to the target); capacity (`PartyFull` to the target, count stays 4 — AC-PS-2); level against the current leader (`LevelRange` to the target, AC-PS-3 / AC-PS-4). On success a solo inviter becomes leader of a new real party (`TryCreateParty`, Story 002) and the target is added (`TryAddMember`); a real party gains the target. The pending slot is freed and the queue advances (AC-6).
- [x] **AC-4 Decline / expiry**: decline → `Declined` to the inviter; no response within `InviteTimeoutTicks` (1200 at 20 Hz) of delivery → `Expired` to the inviter (AC-PS-7). Either frees the pending slot; a new invite to the same target then succeeds, and the queue advances.
- [x] **AC-5 Retry**: no response → exactly one re-send of the same `PartyInviteReceived` (same expiry tick) `InviteRetryTicks` (100) after delivery, none after (outbound log shows 2 sends) (AC-PS-6).
- [x] **AC-6 Queue**: an invite to a target with a pending invite is queued (FIFO, one entry per sender, capacity `MaxPartySize × 2` = 8) and its sender gets `Queued` (not an error); the target sees no second invite. When the pending invite resolves (accept, decline, expiry), the next queued invite is re-validated (AC-1 rules) and delivered in the same tick with its own timer and retry starting at delivery; one that fails re-validation is answered with its code (e.g. target joined a party → `AlreadyInParty`) and the next is tried (AC-PS-8, AC-PS-29). Queue full → `Declined` to that sender plus one `LogWarning`.
- [x] **AC-7 Repeats and strays**: the same sender inviting the same target again creates no new entry and gets the current state (`Sent` while pending — no banner re-send — or `Queued`); a `PartyInviteResponse` that matches no pending invite (unknown inviter id, no pending invite, or a queued-not-delivered inviter) changes nothing, sends nothing, and logs one `LogWarning`.
- [x] **AC-8 Intake**: `PartyInviteRequest` (0xE172, body 4) and `PartyInviteResponse` (0xE174, body 5) are registered with the inbound dispatcher; a 6-byte invite body is rejected at intake (handler not called); a 3-byte body reaches the handler (the dispatcher rejects only bodies longer than `MaxBodyBytes`), whose `TryRead` drops it with one `[PartyInvite] malformed body` warning and never calls the service (wording corrected at /story-done).

---

## Implementation Notes

- `PartyInviteService` with per-recipient pending invite + fixed-capacity FIFO queue (`MaxPartySize × 2`, preallocated; no per-request allocation); timers in ticks from `PartySystemConfigValues.InviteTimeoutTicks` / `InviteRetryTicks`; a tick step (`Tick(currentTick)`) handles retry and expiry. Tick source injected (as Death & Respawn's `IDeathTickSource`).
- Seams: `PartyRegistry` (membership, leader, `TryCreateParty` / `TryAddMember`), `IPartyMemberLevelSource` (levels), `ICharacterNameLookup` (inviter name), outbound through `ZoneSlotPool` → client id + `ClientMessageOutbox` like `DeathMessageEmitter`; results and invites go to the owning client only.
- Register the two handlers with the inbound dispatcher (ADR-014) the way `MovementInboundRegistration` / Death & Respawn do; `SenderEntityID` from the envelope identifies the sender's slot.
- **From Story 006 readiness (2026-11-19):** this story owns the dispatcher registration of `PartyInviteRequest` (0xE172, body 4) and `PartyInviteResponse` (0xE174, body 5) — descriptor from the codec `BodySize`, a new `RpcTypeTag` if the guard chain needs one — plus an intake test: a 6-byte invite body is rejected at intake (handler not called); a 3-byte body reaches the handler (the dispatcher rejects only bodies longer than `MaxBodyBytes`), whose `TryRead` drops it with one `[PartyInvite] malformed body` warning and never calls the service (wording corrected at /story-done). Results go out as `PartyInviteResult` (0xE177, codes Sent 0 … Declined 8); a late-accept `PartyFull` goes to the accepting target.
- Invites are delivered regardless of combat state (CR-PS-4); a Dead target receives the invite and may accept it (I6).
- Zone-local: target lookup by CharacterID within this zone's slot pool only (ADR-010 note 2026-11-18).

---

## Readiness Decisions (2026-11-19)

- **I1 Queue bound.** One entry per sender, capacity `MaxPartySize × 2` = 8; overflow → `Declined` to the sender + `LogWarning` (no new wire code).
- **I2 Party creation at accept.** A solo inviter stays solo until an accept; no one-member `Forming` parties left by declines or expiries.
- **I3 Accept-time re-check.** Inviter gone / no longer solo or leader → `Expired` to the target; target now in a real party → `AlreadyInParty`; capacity and level re-checked against the current leader.
- **I4 Queued invites.** Timer and retry start at delivery; re-validated at delivery; a failure is answered and the next queued invite is tried.
- **I5 Repeats and strays.** Same sender → same target: no new entry, current state repeated, no banner re-send; self / already-in-your-party → `AlreadyInParty`; unmatched response → ignored + `LogWarning`.
- **I6 Dead target.** Receives and may accept (CR-PS-4: regardless of combat state).
- **I7 Seams.** Name lookup, slot pool + outbox, level source, zone-only target lookup; estimate 5 → 6 h.
- **Out of this story:** cancelling invites when a player disconnects or leaves the zone is Story 009 (until then they expire on the timer; accept-time re-check covers a vanished inviter).

---

## Out of Scope

- Story 008: `PartyStateUpdate` broadcast after the join (this story may call a "changed" hook)
- Client banner UI (presentation)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

*Refined at `/story-readiness` 2026-11-19.*

- **AC-1**: two players in a fake pool + recording outbox → X gets `PartyInviteReceived` (inviter id / entity / name / level, expiry = now + 1200); A gets `Sent`; A is still solo (no party created).
- **AC-2**: level table 24 / 25 / 35 / 36 vs 30 → LevelRange / Sent / Sent / LevelRange; target absent → TargetNotFound; non-leader member sender → NotLeader; full party → PartyFull; target in a party, self, own member → AlreadyInParty; no pending invite after any rejection.
- **AC-3**: solo A invites X, X accepts → real party [A, X], A leader; party filled to 4 between send and accept → X gets PartyFull, count 4; leader level changed to make X out of range → LevelRange; A joined another party as non-leader → X gets Expired; X joined another party first → AlreadyInParty; Dead X accepts → joins.
- **AC-4**: decline → A gets Declined, slot free; tick +1199 → nothing; +1200 → A gets Expired, slot free, new invite Sent.
- **AC-5**: tick to +100 → 2 sends (same expiry tick); tick to +1199 → still 2.
- **AC-6**: A invites X, B invites X → B Queued, X has 1 banner; A's invite times out at +1200 → X receives B's that tick with expiry +2400 and retry at +1300; queued C whose target joined a party meanwhile → C gets AlreadyInParty and D is delivered; 9th distinct queued sender → Declined + one warning.
- **AC-7**: A re-invites X while pending → Sent, no extra banner, one entry; while queued → Queued; response for unknown inviter or a queued-not-delivered inviter → no change, no message, one warning.
- **AC-8**: dispatcher has 0xE172 / 0xE174; 6-byte invite body rejected at intake (handler call count 0); 3-byte body reaches the handler, dropped with one warning, service not called.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_InviteFlow_tests.cs` — must exist and pass

**Status**: [x] Created — passing (see Completion Notes)

---

## Dependencies

- Depends on: Stories 002, 004, 006
- Unlocks: Stories 011, 012

---

## Completion Notes

**Completed**: 2026-11-19
**Criteria**: 8/8 passing (all automated)
**Deviations / decisions (advisory)**:
- AC-8: the inbound dispatcher only rejects over-long bodies, so a 3-byte invite reaches the handler and is dropped by `TryRead` with a warning (same as Enhancement); a 6-byte body is rejected at intake. AC wording corrected.
- Results sent to the target (accept-time `Expired` / `AlreadyInParty` / `PartyFull` / `LevelRange`) carry the inviter id; results to the inviter carry the target id.
- A queued sender gets `Queued` once and nothing on promotion; a queued invite that fails re-validation is answered with its code.
- Unreadable target level → `TargetNotFound`; unreadable leader level → `LevelRange`. Send check order: `NotLeader` → `TargetNotFound` → `AlreadyInParty` → `PartyFull` → `LevelRange`.
- Handlers take the tick from `InboundRequestContext.DispatchTick` (no separate tick-source seam).
- Pending entries are pooled (32 preallocated); past 32 simultaneous recipients one entry is allocated once and reused.
- A join refused by the registry at accept (e.g. `IdsExhausted`) → `Expired` to the target + `LogError`; the slot resolves and the queue advances. `TryAddMember` failing after a successful `TryCreateParty` is unreachable (reviewed); it would leave a one-member party.
- New `RpcTypeTag` members `PartyInviteRequest = 11`, `PartyInviteResponse = 12` (no rate limit); `PartyRegistry.TryGetEntity` added.
**Hand-offs**: Story 008 — a successful join sends nothing; the `PartyStateUpdate` broadcast detects it from the registry change counter. Story 009 — cancel invites on disconnect / zone exit. Story 011 — call `PartyInviteService.Tick(currentTick)` every tick after dispatch (first sub-step of `PartyTickStep`, Story 011 AC-4), register `PartyInboundRegistration.Register` before `Seal()`, bind `OutboxPartyInviteMessageSink`.
**Test Evidence**: Integration — `tests/EditMode/PartySystem/PartySystem_InviteFlow_tests.cs` (service fixture + `PartySystem_InviteFlow_Sink_Tests` for the production outbox sink); PartySystem filter 334/334; full EditMode `-testCategory "!Database"` 8428/8428 (13:57:50Z), 0 `error CS`.
**Code Review**: Complete (`/code-review` 2026-11-19: C1–C3 and T1–T13 + suggestions applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — Lean mode.
