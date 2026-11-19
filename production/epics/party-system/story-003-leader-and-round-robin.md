# Story 003: Leader Index and Round-Robin Cursor

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-7, CR-PS-8, Edge Cases (`rrNextIndex` correctness, leader next-in-line)
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

## Acceptance Criteria

- [ ] **AC-1 Cursor advance**: party [A, B, C], `rrNextIndex = 2` → the drop goes to C (`GetRrNextIndex` = 2) and `AdvanceRrNextIndex` sets 0 (AC-PS-17).
- [ ] **AC-2 Skip**: the cursor skips slots whose status is Ghost, Disconnected, OutOfZone or Dead, and empty slots, advancing at most `MemberCount` times (AC-PS-18; Dead per CR-DR-20).
- [ ] **AC-3 None eligible**: all members ineligible → `rrNextIndex` unchanged from its pre-call value and one frozen-cursor log line (AC-PS-19, AC-PS-27).
- [ ] **AC-4 Clamp**: party of 4, `rrNextIndex = 3`, member 3 leaves → `rrNextIndex = 0` immediately (AC-PS-20); solo party keeps 0.
- [ ] **AC-5 Leader compaction**: [A, B, C], `LeaderIndex = 2`, B leaves → [A, C], `LeaderIndex = 1` (AC-PS-28); removal above the leader leaves it unchanged; removal of the leader applies the transfer rule first.
- [ ] **AC-6 Transfer**: leader leaves (or `TransferLeadership` on disconnect) → the lowest non-empty index that is not Ghost / Disconnected (wrap to 0); a reconnecting former leader does not regain leadership (AC-PS-22); last member leaving disbands instead.
- [ ] **AC-7 `IPartyService`**: `PartyService` implements `IPartyService` (`GetPartyID`, `GetPartyMembers`, `GetMemberAtIndex`, `GetRrNextIndex`, `AdvanceRrNextIndex`, `IsMemberConnected`) over the registry.

---

## Implementation Notes

- Status values come from Story 004; this story may define the `PartyMemberStatus` enum (Online = 0, Ghost, OutOfZone, Dead = 3 per `networking-wire-protocol.md`, plus Disconnected if the wire enum has it — check) and a setter for tests, or depend on 004 landing first (decide at readiness).
- Cursor changes mark the party "state changed" so Story 008 can broadcast; only when the value changes (CR-PS-7 step 3).
- Read `LootTableSystem/PartyTagTracker` and the loot distributor to confirm the call order they expect (`GetRrNextIndex` then `AdvanceRrNextIndex`).
- **From Story 002 (2026-11-18):** `PartyRegistry.RemoveMember` and `UnregisterPlayer` compact the slots through `PartyState.RemoveAt` but never touch `LeaderIndex` (always 0) or `RrNextIndex`; after removing index 0 the leader index is stale. Hook the AC-5 leader compaction and the cursor clamp into that removal path (both callers), and test removal of the leader through both `RemoveMember` and `UnregisterPlayer`. Freed parties are marked `PartyState.IsFreed`; `IPartyService` calls must resolve parties by id, never a cached `PartyState`. Read `GetPartyMembers` / `Members` with indexed loops (no `foreach`, it allocates).

---

## Out of Scope

- Story 008: broadcasting `PartyStateUpdate` on change
- Story 009: disconnect detection (this story exposes the transfer)
- Real loot drops reaching the cursor (production still uses `StandInLootTableService`)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: [A, B, C] at 2 → advance → 0; at 0 → 1.
- **AC-2**: [A, B(Ghost), C] at 0 → advance → 2; Dead treated like Ghost (AC-DR-NEW-7 cursor half).
- **AC-3**: all Ghost → unchanged + `LogAssert` frozen-cursor line.
- **AC-4**: party of 4 at 3, remove index 3 → 0; at 1, remove index 3 → 1.
- **AC-5**: the three compaction cases.
- **AC-6**: leader 0 leaves with [_, B(Ghost), C] → C leads; wrap case; former leader re-added → not leader.
- **AC-7**: each `IPartyService` method against registry state.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_LeaderAndCursor_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (and Story 004 for status, if 004 lands first — decide at readiness)
- Unlocks: Stories 005, 008, 009
