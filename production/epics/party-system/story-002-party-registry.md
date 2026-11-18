# Story 002: Party Registry — Solo PartyIDs and Membership

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-1, CR-PS-2, States and Transitions, Edge Cases (solo party join)
**Requirement**: `TR-party-001`, `TR-party-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: The Party System is a Tier 1 service reached by direct calls on injected interfaces; zone-local parties at MVP (note 2026-11-18).

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: dependency injection; doc comments; no allocation per tick in steady state
- Guardrail: fixed-size member arrays (`MaxPartySize`), no LINQ in hot paths

---

## Acceptance Criteria

- [ ] **AC-1 Solo PartyID**: `RegisterPlayer(CharacterID, EntityID)` gives the player a solo party (`IsSoloParty = true`, `MemberCount = 1`, PartyID non-zero); no two registered players share a PartyID; solo and real ids come from one sequential per-process counter starting at 1; PartyID 0 is never returned (AC-PS-1).
- [ ] **AC-2 Create and join**: `TryCreateParty(leader)` turns the leader into a real party (new PartyID, `IsSoloParty = false`, leader at index 0, state `Forming`); `TryAddMember(partyId, character)` appends in join order and moves `Forming → Active` at the second member; the joiner's solo party is cleaned up.
- [ ] **AC-3 Limits**: adding a fifth member fails with `PartyFull` and the count stays 4 (AC-PS-2); a character already in a real party cannot join another (`AlreadyInParty`); a solo party never accepts members (`SoloPartyCannotAccept`); unknown party / character → typed failure, no state change.
- [ ] **AC-4 Compaction**: `RemoveMember` clears the slot and compacts the array preserving join order ([A, B, C] remove B → [A, C]); `MemberCount` updates; removing the last member disbands the party (state cleared, no PartyID reuse).
- [ ] **AC-5 Queries**: `GetPartyID(CharacterID)`, `GetPartyMembers(PartyID)` (join order, `CharacterID[]` view without allocation per call), `GetMemberAtIndex`, `TryGetParty` return consistent state; join order is preserved across changes (AC-PS-23).

---

## Implementation Notes

- New `PartyRegistry` (state) inside `src/ServerLogic/PartySystem/`; `PartyState` holds PartyID, `IsSoloParty`, state enum (`Forming`, `Active`, `Disbanding`, `Ghost`), member `CharacterID[MaxPartySize]`, `EntityID[]`, `LeaderIndex`, `RrNextIndex`, `SequenceId`.
- Counter overflow (OQ-PS-2): per-process `uint` counter; parties are zone-local and die with the process; log an error and refuse allocation at `uint.MaxValue` (never wrap to 0). Record the decision in the GDD open question at story-done.
- GDD leftovers: CR-PS-8 "6-slot" and the `rrNextIndex` "slot index 5" edge case mean 4 — fix the GDD text at story-done.
- Leader transfer, status and the cursor are Stories 003/004; here `RemoveMember` only compacts (leader index handling arrives in 003 and must be tested there).

---

## Out of Scope

- Story 003: leader index rules, round-robin cursor, `IPartyService`
- Story 004: member status
- Story 007: invites (this story exposes the direct add only)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: register 50 players → 50 distinct non-zero ids, all solo.
- **AC-2**: create + add → state Forming then Active; joiner's old solo id no longer resolves.
- **AC-3**: 4 members + add → `PartyFull`, count 4; member of party X joins Y → `AlreadyInParty`; add to solo → refused.
- **AC-4**: [A, B, C] remove B → [A, C]; remove all → party gone, ids not reused by the next allocation.
- **AC-5**: `GetPartyMembers` order after add / remove; GC alloc 0 on repeated query (`GC.GetAllocatedBytesForCurrentThread`).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_Registry_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 004
