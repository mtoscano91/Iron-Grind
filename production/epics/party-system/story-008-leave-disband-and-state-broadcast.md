# Story 008: Leave, Disband and State Broadcast

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-9, States and Transitions (Disbanding), Visual/Audio signals; `networking-wire-protocol.md` `PartyStateUpdate` / `PartyDisbanded`; `death-and-respawn.md` CR-DR-20, CR-DR-21
**Requirement**: `TR-party-009` (leave / disband), `TR-party-011` (broadcast)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Leave and disband requests are handled in the dispatch phase; party state changes are flushed as outbound messages once per tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: full snapshots, never deltas; per-party monotonic `sequenceId`
- Guardrail: at most one `PartyStateUpdate` per party per tick

---

## Acceptance Criteria

- [ ] **AC-1 Leave**: `PartyLeaveRequest` (or a direct `Leave`) clears the slot, compacts (leader and cursor rules from Story 003), gives the leaver a new non-zero unique solo PartyID, and the remaining members get a `PartyStateUpdate` without the leaver (AC-PS-25 a–c).
- [ ] **AC-2 Disband**: the leader's `PartyDisbandRequest` moves the party to `Disbanding` and sends `PartyDisbanded` to Online members; cleanup (every member back to a solo PartyID, each Online member sent its solo `PartyStateUpdate` after `PartyDisbanded`) runs after the 10 s timeout (acknowledgement messages are not in the protocol — timeout only; note in Completion Notes); a non-leader disband → `NotLeader`.
- [ ] **AC-3 Auto-disband**: the last member leaving disbands the party (no `PartyStateUpdate` to an empty party).
- [ ] **AC-4 Broadcast**: any change in a tick (join, leave, leader, status, eligibility, cursor) produces exactly one `PartyStateUpdate` per party at the end of the tick, to Online members only (not Ghost / OutOfZone), with `sequenceId` +1 and member slots carrying EntityID, level, status, `xpEligible`; no change → no message.
- [ ] **AC-5 Death rows**: party [A, B(Dead), C], `rrNextIndex = 1` → drop to C, cursor 2, then 0 (AC-DR-NEW-7); party disbands while B is Dead → B holds a solo PartyID at once and is `IsSoloParty` on respawn (AC-DR-NEW-9).

---

## Implementation Notes

- A per-party "dirty" flag set by registry / status / cursor changes; a `PartyBroadcastStep` (outbound phase) builds the snapshot with the Story 006 codec into the `ClientMessageOutbox` (as `DeathMessageEmitter`).
- Disband acknowledgements: the GDD mentions "all acknowledgments" but defines no ack message; implement the 10 s timeout and record the deviation.
- `PartyDisbandCoordinator` (Networking Core) handles the ghost-period part of disband — call it from the disband path if it applies (decide at readiness with Story 009).

---

## Out of Scope

- Story 009: disconnect-driven leave (TTL) and Ghost handling
- Story 010: health stream

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: [A, B, C] B leaves → B solo id new + unique; A and C each get one update listing [A, C].
- **AC-2**: leader disband → `PartyDisbanded` at T, solo updates at T + 200 ticks; non-leader → `NotLeader`.
- **AC-3**: two-member party, both leave → no update to an empty party.
- **AC-4**: join + cursor change in one tick → one update, sequenceId +1; Ghost member receives none; idle tick → none.
- **AC-5**: the two death rows.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_LeaveDisbandBroadcast_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 003, 004, 006
- Unlocks: Stories 009, 011
