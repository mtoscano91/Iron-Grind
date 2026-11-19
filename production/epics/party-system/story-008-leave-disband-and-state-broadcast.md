# Story 008: Leave, Disband and State Broadcast

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-9, States and Transitions (Disbanding), Visual/Audio signals; `networking-wire-protocol.md` `PartyStateUpdate` / `PartyDisbanded`; `death-and-respawn.md` CR-DR-20, CR-DR-21
**Requirement**: `TR-party-009` (leave / disband), `TR-party-011` (broadcast)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Leave and disband requests are taken in the dispatch phase and queued; a party membership step applies them after the simulation phase (Readiness L6); party state changes are flushed as outbound messages once per tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: full snapshots, never deltas; per-party monotonic `sequenceId`
- Guardrail: at most one `PartyStateUpdate` per party per tick

---

## Acceptance Criteria

- [x] **AC-1 Leave**: a `PartyLeaveRequest` from a member of a real party is queued; the membership step clears the slot, compacts (leader and cursor rules from Story 003), and gives the leaver a new non-zero unique solo PartyID. That tick's broadcast sends the remaining members a `PartyStateUpdate` without the leaver, and the leaver its own solo `PartyStateUpdate` (`IsSoloParty` true, 1 member) (AC-PS-25 a–c). A leave from a solo player changes nothing and sends nothing.
- [x] **AC-2 Disband**: the leader's `PartyDisbandRequest` is queued; the membership step moves the party to `Disbanding` and sends `PartyDisbanded` to its connected members (L3). Cleanup runs `DisbandTimeoutTicks` (200) later: every remaining member gets a new solo PartyID and its solo `PartyStateUpdate`, after `PartyDisbanded` (acknowledgement messages are not in the protocol — timeout only, L2). A non-leader disband → `PartyInviteResult(NotLeader, 0)` to the sender, no change; a disband request for a party already `Disbanding` changes nothing.
- [x] **AC-3 Auto-disband**: when a leave leaves one member, the party is disbanded at once (no `Disbanding` wait): the remaining member gets a new solo PartyID, `PartyDisbanded` and its solo `PartyStateUpdate`; no `PartyStateUpdate` is sent for the freed party (L1).
- [x] **AC-4 Disbanding rules**: while a party is `Disbanding`: it sends no `PartyStateUpdate`; kills still award XP (Story 005); a member may leave (gets its solo id and update at once); `PartyInviteService` rejects an invite from its leader with `NotLeader` and an accept into it with `Expired` to the target (L2).
- [x] **AC-5 Broadcast**: at most one `PartyStateUpdate` per party per tick, sent by the broadcast step when the party's `SequenceId` differs from the last value sent for it, carrying the current `SequenceId` (strictly greater than the last sent; it may rise by more than 1 when several changes share a tick, L4) and member slots with CharacterID, EntityID, level, status, `xpEligible`. Recipients: connected members in this zone — Online and Dead; not Ghost or OutOfZone (L3). A join made by Story 007's invite flow is broadcast the same way. No change → no message.
- [x] **AC-6 Same tick as a kill**: a leave request and a kill in the same tick → the leaver receives that kill's XP, then is removed (dispatch queues, simulation awards, the membership step removes) (GDD edge case; Story 005 G3).
- [x] **AC-7 Death rows**: party [A, B(Dead), C], `rrNextIndex = 1` → drop to C, cursor 2, then 0 (AC-DR-NEW-7); party disbands while B is Dead → B holds a solo PartyID at cleanup and is `IsSoloParty` on respawn (AC-DR-NEW-9). B (Dead) receives the `PartyStateUpdate` and `PartyDisbanded` messages.
- [x] **AC-8 Intake**: `PartyLeaveRequest` (0xE175) and `PartyDisbandRequest` (0xE176), body 0, not held, are registered with the dispatcher (new `RpcTypeTag` 13 / 14, no rate limit); a 1-byte body is rejected at intake (handler not called).

---

## Implementation Notes

- No dirty flag: a `PartyBroadcastStep` (outbound phase) keeps the last `SequenceId` sent per party and compares it with the registry's; it needs a new allocation-free party enumeration on `PartyRegistry` (e.g. `int CopyPartyIds(Span<PartyID>)` or a struct enumerator). Snapshots use the Story 006 codec; delivery through a new `IPartyStateMessageSink` with an outbox adapter like Story 007's `OutboxPartyInviteMessageSink` (owning client per recipient).
- `PartyMembershipStep.ApplyQueued(currentTick)` applies queued leave / disband requests and runs disband cleanup when a `Disbanding` party's timeout tick is reached; Story 011 places it after phase 3 and before the broadcast step.
- Leveling: the `level` field and `xpEligible` come from `IPartyMemberLevelSource` and `PartyXpEligibility` (Story 004).
- **From Story 006 readiness (2026-11-19):** this story owns the dispatcher registration of `PartyLeaveRequest` (0xE175) and `PartyDisbandRequest` (0xE176), both body 0, not held — plus an intake test: a 1-byte body is rejected before the handler runs. The non-leader disband answer is `PartyInviteResult(NotLeader)` with `targetCharacterId = 0`. `PartyStateUpdate` (0xE170) and `PartyDisbanded` (0xE171) route as `MessageDirection.ServerToParty`.
- **From Story 005 readiness (2026-11-19, G3):** the GDD same-tick rule (kill and leave in one tick → the leaver still gets that kill's XP) needs leave / disband to apply *after* the kill. ADR-014 dispatch runs before simulation, so the `PartyLeaveRequest` / `PartyDisbandRequest` handlers must queue the change, and a party membership step applies the queue after phase 3 (placed by Story 011). Added as AC-6 (readiness 2026-11-19).
- Disband acknowledgements: the GDD mentions "all acknowledgments" but defines no ack message; implement the 10 s timeout and record the deviation.
- **Open at readiness (from Story 002 readiness, 2026-11-18):** CR-PS-2 says a real party has 2–4 members and CR-PS-9 says "last member leaves: auto-disband", but neither says whether an `Active` party that drops to one member auto-disbands (the remaining member re-soloed) or stays a party of one. **Decided 2026-11-19 (L1): auto-disband at once.**
- `PartyDisbandCoordinator` (Networking Core) handles the ghost-period part of disband — **decided 2026-11-19 (L8): Story 009** (ghost members only).

---

## Readiness Decisions (2026-11-19)

- **L1 One member left.** An Active party that drops to one member is disbanded at once (no `Disbanding` wait, nobody to acknowledge); answers the open item from Story 002 readiness.
- **L2 Disbanding.** Timeout-only cleanup at `DisbandTimeoutTicks`; no invites or accepts into it (`PartyInviteService` change in this story); leaves allowed; XP awards; no `PartyStateUpdate` until cleanup.
- **L3 Recipients.** Connected members in this zone: Online and Dead (a dead player's party panel stays current). Amends `networking-wire-protocol.md` (`PartyStateUpdate` delivery line) in this readiness pass.
- **L4 SequenceId.** The message carries the registry's current value; strictly increasing per party, not necessarily +1 per message.
- **L5 Solo updates.** The leaver and every re-soloed member get their solo `PartyStateUpdate`.
- **L6 Same-tick ordering.** Leave / disband are queued at dispatch and applied by the membership step after phase 3 (Story 005 G3).
- **L7 Change detection.** Last-sent `SequenceId` per party + registry party enumeration.
- **L8 `PartyDisbandCoordinator`.** Ghost-period disband only → Story 009.
- **L9 Wiring.** `RpcTypeTag` 13 / 14, intake rejects a 1-byte body, `IPartyStateMessageSink` + outbox adapter; estimate 4 → 6 h.

---

## Out of Scope

- Story 009: disconnect-driven leave (TTL) and Ghost handling
- Story 010: health stream

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

*Refined at `/story-readiness` 2026-11-19.*

- **AC-1**: [A, B, C] B leaves → B solo id new + unique; A and C each get one update listing [A, C]; B gets one solo update; solo player leave → nothing.
- **AC-2**: leader disband at T → `PartyDisbanded` to connected members at T; nothing at T + 199; at T + 200 each member solo + solo update after `PartyDisbanded`; non-leader → `NotLeader` (target 0); second disband during `Disbanding` → nothing.
- **AC-3**: [A, B] B leaves → A gets `PartyDisbanded` + solo update the same tick; no update for the freed party.
- **AC-4**: `Disbanding` party: invite from its leader → `NotLeader`; accept into it → `Expired` to the target; member leaves → solo at once; XP distributor still awards; no `PartyStateUpdate` during the wait.
- **AC-5**: join + cursor change in one tick → one update with the current SequenceId (> last sent); Online and Dead members receive, Ghost and OutOfZone do not; idle tick → none; a Story 007 accept is broadcast.
- **AC-6**: queue leave for B, award a kill (distributor) in the same tick, then the membership step → B got the split XP, then is removed.
- **AC-7**: the two death rows; Dead B receives the messages.
- **AC-8**: dispatcher has 0xE175 / 0xE176; 1-byte body → handler count 0.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_LeaveDisbandBroadcast_tests.cs` — must exist and pass

**Status**: [x] Created — passing (see Completion Notes)

---

## Dependencies

- Depends on: Stories 003, 004, 006
- Unlocks: Stories 009, 011

---

## Completion Notes

**Completed**: 2026-11-19
**Criteria**: 8/8 passing (all automated)
**Deviations / implementation decisions (advisory)**:
- Disband cleanup is timeout-only (`DisbandTimeoutTicks`, 200): the protocol has no acknowledgement message (GDD CR-PS-9 "all acknowledgments").
- `PartyDisbanded` is sent by `PartyMembershipStep.ApplyQueued`; every `PartyStateUpdate` (party and solo) only by `PartyBroadcastStep.Flush`, which runs after it — so `PartyDisbanded` always precedes the solo update. A solo-update request is dropped at flush if the player has joined a real party meanwhile.
- Leave during `Disbanding`: the leaver is re-soloed at once; if one member remains the party is finished at once (solo update, no second `PartyDisbanded`).
- Same-tick leave + disband from the leader: leave first → disband ignored (leader already solo); disband first → `Disbanding`, then the leader leaves at once.
- A non-leader disband on a party already `Disbanding` is ignored silently (the Disbanding check runs before the leader check); a disband from a solo player is ignored silently.
- Request queue: capacity 256, de-duplicated per (character, kind) per tick; overflow drops the request with one `LogWarning` per `PartyMembershipStep` lifetime (the flag never resets — a later overflow is silent; pinned by test).
- Snapshot level is clamped to 0–255 (0 when unreadable); `xpEligible` from `PartyXpEligibility`.
- `Flush` clears solo requests in a `finally` and records the last-sent `SequenceId` only after sending, so a throwing level / eligibility read does not lose the party's update.
- Registration is a second method, `PartyInboundRegistration.RegisterLeaveAndDisband`; `PartyRegistry` gained `BeginDisband` (internal) and `CopyRealPartyIds` (public); `RpcTypeTag` `PartyLeaveRequest = 13`, `PartyDisbandRequest = 14`.
**Hand-offs**: Story 011 — per tick: invite `Tick` → dispatch → simulation → `PartyMembershipStep.ApplyQueued` → `PartyBroadcastStep.Flush`; call both registration methods; the membership step takes the invite sink for its `NotLeader` answer. Story 009 — disconnect-driven leave goes through `EnqueueLeave`; a Ghost leaver gets a solo id but no message; `PendingCleanupCount` is the only cleanup hook for `PartyDisbandCoordinator`.
**Test Evidence**: Integration — `tests/EditMode/PartySystem/PartySystem_LeaveDisbandBroadcast_tests.cs` (logic, intake and outbox-sink fixtures, 58 cases); PartySystem filter 392/392; full EditMode `-testCategory "!Database"` 8486/8486, 0 `error CS`.
**Code Review**: Complete (`/code-review` 2026-11-19: C1, C2, T1–T19 and suggestions applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — Lean mode.
