# Story 003: Leader Index and Round-Robin Cursor

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-5 (last paragraph, XP-ineligible notice), CR-PS-7, CR-PS-8, Edge Cases (`rrNextIndex` correctness, leader next-in-line)
**Requirement**: `TR-party-007`, `TR-party-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Loot reaches the Party System through the direct-call `IPartyService` (`src/ServerLogic/LootTableSystem/IPartyService.cs`); the Party System implements it.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: dependency injection; doc comments
- Guardrail: visit-counted loops (no unbounded loops)

---

## Readiness Decisions (2026-11-19, `/story-readiness`, user "apply G1–G5")

- **G1 Implementer and notice seam**: there is no separate `PartyService` class. `PartyRegistry` implements `IPartyService` directly; it already has `GetPartyID`, `GetPartyMembers` and `GetMemberAtIndex`. The XP-ineligible notice goes through a new seam `IPartyXpIneligibleNoticeSink` (`void Notify(CharacterID character, PartyID partyId)`, in `src/ServerLogic/PartySystem/`), injected into the registry (optional constructor argument; null → a no-op sink). Tests use a recording fake. The production adapter (encode `PartyXpIneligibleNotice` 0xE178, resolve the character's client id, `IClientMessageOutbox.Enqueue`) is **Story 011**.
- **G2 Cursor read is a pure read**: `GetRrNextIndex` returns the stored `RrNextIndex` unchanged (the CR-LT-6 contract the loot module uses: read, resolve, assign, advance). Skipping happens only in `AdvanceRrNextIndex`. **Known limitation:** a member who becomes ineligible while the cursor points at them still receives the next drop, and with every member ineligible the loot module still assigns to the cursor slot (AC-PS-19 "no item is assigned" is not met end to end). The fix belongs to the Loot module (a "next eligible member" call, see the `IPartyService.GetRrNextIndex` doc); log it as tech debt at `/story-done`. This story's AC-3 covers the cursor half only.
- **G3 No eligible leader**:
  - Leader leaves voluntarily (or is unregistered) with at least one member left: after compaction the lowest-index member that is not Ghost leads; if every remaining member is Ghost, index 0 leads anyway (a party always has a leader).
  - `TransferLeadership` (disconnect, Story 009) with no other non-Ghost member: leadership does not move, the call returns `false` and logs one `Debug.Log` line. The retry when a member's status returns to Online is Story 009's call, not automatic here.
- **G4 Search order and who can lead**: candidates are scanned at indices `0 … MemberCount − 1` in order, skipping the outgoing leader and any Ghost member; the first hit leads. Dead and OutOfZone members can lead (the GDD excludes only Ghost / Disconnected). "Wrap to 0" in CR-PS-8 has no effect with a scan from 0 and is not implemented separately. API: `public bool TransferLeadership(PartyID partyId)` — `true` when the leader changed; `false` for an unknown or solo party, a party with no other non-Ghost member, or a freed party.
- **G5 Details**:
  - "Disconnected" in the GDD is `Ghost` at MVP (GDD note, Story 004); there is no separate status value.
  - Frozen-cursor log: `Debug.LogWarning`, message starting `"[PartyRegistry] round-robin cursor frozen"` and including the party id and the cursor value.
  - `SequenceId` (`PartyState.MarkChanged()`) bumps once per operation and only when `RrNextIndex` or `LeaderIndex` actually changes value (a removal already bumps it through Story 002/004 paths — do not bump twice for one removal).
  - XP-ineligible notice: after a leader change, for each member, compare `IsXpEligible` before and after; notify only members that went from eligible to ineligible. Members already ineligible and the new leader get no notice.

---

## Acceptance Criteria

- [ ] **AC-1 Cursor advance**: party [A, B, C] all Online, `RrNextIndex = 2` → `GetRrNextIndex` = 2, `GetMemberAtIndex(party, 2)` = C, and after `AdvanceRrNextIndex` the cursor is 0 (AC-PS-17); from 0 it advances to 1.
- [ ] **AC-2 Skip**: `AdvanceRrNextIndex` skips slots whose status is Ghost, OutOfZone or Dead, and empty slots, advancing at most `MemberCount` times (AC-PS-18; Dead per CR-DR-20).
- [ ] **AC-3 None eligible**: all members ineligible → `RrNextIndex` equals its pre-call value, `SequenceId` unchanged, and exactly one `LogWarning` frozen-cursor line (AC-PS-19 cursor half, AC-PS-27).
- [ ] **AC-4 Clamp**: party of 4, `RrNextIndex = 3`, member 3 leaves → `RrNextIndex = 0` immediately (AC-PS-20); `RrNextIndex = 1`, member 3 leaves → stays 1; a solo party's cursor is always 0.
- [ ] **AC-5 Leader compaction**: [A, B, C], `LeaderIndex = 2`, B leaves → [A, C], `LeaderIndex = 1` (AC-PS-28); removal above the leader leaves it unchanged; removal of the leader applies the transfer rule (G3, G4) — through both `RemoveMember` and `UnregisterPlayer`.
- [ ] **AC-6 Transfer**: `TransferLeadership` moves the leader to the lowest-index non-Ghost member other than the current leader (G4) and returns `true`; with no candidate it returns `false` and the leader is unchanged (G3); a former leader whose status returns to Online does not regain leadership (AC-PS-22); the last member leaving frees the party (no transfer).
- [ ] **AC-7 Notice**: a leader change (leave or transfer) that makes a member XP-ineligible calls `IPartyXpIneligibleNoticeSink.Notify(member, party)` once for that member only (CR-PS-5); member count is unchanged by the eligibility change.
- [ ] **AC-8 `IPartyService`**: `PartyRegistry` implements `IPartyService` (`GetPartyID`, `GetPartyMembers`, `GetMemberAtIndex`, `GetRrNextIndex`, `AdvanceRrNextIndex`, `IsMemberConnected`); unknown party → `GetRrNextIndex` 0 and `AdvanceRrNextIndex` no-op; `IsMemberConnected` false only for Ghost.

---

## Implementation Notes

- Status values come from Story 004 (`PartyMemberStatus` Online 0 / Ghost 1 / OutOfZone 2 / Dead 3, `GetMemberStatus`, internal `SetMemberStatus`, `PartyState.MarkChanged()`, `IsXpEligible` reading the current `LeaderIndex`). Replace the internal `SetLeaderIndexForTests` uses with the real transfer where tests allow; keep an internal cursor setter for tests if needed.
- **From Story 002 (2026-11-18):** `PartyRegistry.RemoveMember` and `UnregisterPlayer` compact the slots through `PartyState.RemoveAt` but never touch `LeaderIndex` or `RrNextIndex`. Hook the leader compaction (CR-PS-8 a/b/c) and the cursor clamp (`RrNextIndex % newCount` when `RrNextIndex >= newCount`) into that removal path, for both callers. Freed parties are marked `PartyState.IsFreed`; `IPartyService` calls resolve parties by id, never a cached `PartyState`. Read members with indexed loops (no `foreach`).
- Loot call order confirmed (`LootDropDistributor.AssignByRoundRobin`, `LootAuctionService.AssignByRoundRobin`): `GetRrNextIndex` → `GetMemberAtIndex` → assign → `AdvanceRrNextIndex` once, also for an invalid slot. Do not change the loot module (G2).
- Eligibility snapshot for AC-7: up to `MaxPartySize` booleans in a `stackalloc` or fixed buffer; no allocation per leader change.
- Story 009 calls `TransferLeadership` on disconnect and decides when to retry it after a reconnect (G3).

---

## Out of Scope

- Story 008: broadcasting `PartyStateUpdate` on change
- Story 009: disconnect detection and calling `TransferLeadership` (this story exposes it)
- Story 011: the production `IPartyXpIneligibleNoticeSink` adapter (outbox + client id) and binding the registry as the loot `IPartyService`
- Loot-side skip of an ineligible member at the cursor (G2, tech debt)
- Real loot drops reaching the cursor (production still uses `StandInLootTableService`)

---

## QA Test Cases

- **AC-1**: [A, B, C] at 2 → read 2, member C, advance → 0; at 0 → 1.
- **AC-2**: [A, B(Ghost), C] at 0 → advance → 2; same with B Dead; same with B OutOfZone; [A, B(Ghost), C(Ghost)] at 0 → advance → 0 (A is eligible again after a full visit — wraps back to A).
- **AC-3**: all Ghost at 1 → still 1, `SequenceId` unchanged, `LogAssert.Expect(LogType.Warning, regex "round-robin cursor frozen")` once; mixed Ghost / OutOfZone / Dead same result.
- **AC-4**: party of 4 at 3, remove index 3 → 0; at 1, remove index 3 → 1; at 2, remove index 0 → cursor clamps only if `>= newCount` (2 < 3 → 2); solo `GetRrNextIndex` → 0.
- **AC-5**: the three compaction cases; leader removed via `RemoveMember` and via `UnregisterPlayer`; leader at 0 leaves [A, B(Ghost), C] → [B, C], C leads (index 1); leader leaves with all others Ghost → index 0 leads.
- **AC-6**: [A(leader), B(Ghost), C] → transfer → C, `true`, `SequenceId` +1; [A(leader), B(Ghost)] → `false`, leader A, `SequenceId` unchanged; A later Online → leader stays C; Dead / OutOfZone member can receive leadership; unknown and solo party → `false`; last member leaves → party freed.
- **AC-7**: leader transfer → two members fall outside the level gate → `IsXpEligible` false for both, sink called twice (one per member), member count unchanged; a member already ineligible before the change gets no notice; the new leader gets no notice; no sink set → no throw.
- **AC-8**: each `IPartyService` method against registry state; unknown party for each; `IsMemberConnected` per status (only Ghost false).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_LeaderAndCursor_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002, Story 004 (status enum and eligibility), Story 006 (`PartyXpIneligibleNotice` schema used by the Story 011 adapter) — all Complete
- Unlocks: Stories 005, 008, 009, 011 (notice adapter)
