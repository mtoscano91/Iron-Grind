# Story 004: Member Status and XP Eligibility

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-3 (leader reference), CR-PS-5 eligibility, Exposed Data Types (`PartyMemberSlot`), Interactions (Death & Respawn row); `death-and-respawn.md` CR-DR-5, CR-DR-12, CR-DR-18
**Requirement**: `TR-party-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Death & Respawn calls the Party System through `IPartyMemberStatusSink` (direct call); the slot is retained during the countdown.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: dependency injection; doc comments
- Guardrail: `GetPartyMembersWithStatus` fills a caller buffer (no per-call allocation)

---

## Acceptance Criteria

- [ ] **AC-1 Status**: each member slot carries `PartyMemberStatus` (values and numbers match `networking-wire-protocol.md`, Dead = 3); new members start Online; `GetMemberStatus(partyId, entityOrCharacter)` and `GetPartySlotIndex` report it.
- [ ] **AC-2 Death sink**: `PartyMemberStatusSink` implements `IPartyMemberStatusSink`: Dead on death sets the member Dead without moving the slot; Online on respawn sets Online with the same slot index (AC-DR-13 unit, AC-DR-NEW-4, AC-DR-NEW-8); a solo player or unknown entity is a no-op (no error).
- [ ] **AC-3 Members with status**: `GetPartyMembersWithStatus(partyId, Span/array)` returns `PartyMemberSlot { CharacterID, Status }` for all `MaxPartySize` slots (empty = CharacterID 0).
- [ ] **AC-4 Eligibility**: `IsXpEligible(member)` is true only when status is Online, the member is in this zone, and `|memberLevel − leaderLevel| ≤ LevelRangeGate` (inclusive at 5); level read through an injected level source.
- [ ] **AC-5 Leader change**: after leadership moves to a member whose level puts two others outside ±5, all 4 stay in the party (AC-PS-5) and those two report `IsXpEligible = false`; the party is marked changed (for Story 008's `xpEligible` flags).

---

## Implementation Notes

- `IPartyMemberStatusSink` and `PartyMemberDeathStatus` already exist in `src/ServerLogic/DeathAndRespawn/` (Death & Respawn Story 002): the Party System implements the sink; keep that interface unchanged.
- Level source: a narrow `IPartyMemberLevelSource` over Character Stats / Leveling (check the existing level reader used by `ZoneEntityStateSource`).
- The XP-ineligible system notification (CR-PS-5, last paragraph) needs a message: note it for Story 006 (or defer with a TD) at readiness.

---

## Out of Scope

- Story 005: using eligibility to award XP
- Story 009: Ghost / Disconnected / OutOfZone transitions (this story stores and reports them)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: new member Online; enum numeric values pinned against the wire spec.
- **AC-2**: Dead then Online → slot index equal before / after; sink call for a solo player → no change, no log.
- **AC-3**: party of 3 → 4 slots, last CharacterID 0.
- **AC-4**: leader 30: member 35 eligible, 36 not, 25 eligible, 24 not; Dead member not eligible.
- **AC-5**: leader transfer case → member count 4, eligibility flags as expected.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_MemberStatus_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Stories 003 (status), 005, 009
