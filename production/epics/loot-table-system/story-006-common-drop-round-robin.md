# Story 006: Common Drop Round-Robin Assignment

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-6 (Round-Robin), CR-LT-11 (Rare Drop: Solo Player), CR-LT-5 routing. Party side: `design/gdd/party-system.md` CR-PS-7 (`rrNextIndex`), Interactions table.
**Requirement**: `TR-loot-008`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: None for loot logic (by design — see EPIC "Governing ADRs"). ADR-010: Event/Messaging Architecture (Accepted) governs injection.
**ADR Decision Summary**: The Party System is reached through the injected `IPartyService` (Tier 1). The Party System owns the cursor; the loot service only reads it and asks for it to be advanced.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API.

**Control Manifest Rules (Core layer)**:
- Required: `I[SystemName]Service` naming (`IPartyService`) — ADR-010
- Forbidden: mutating another system's state directly — the loot service never writes `rrNextIndex` (party-system.md CR-PS-7)

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-7** [BLOCKING]: party of 3 `[A, B, C]` in join order with `rrNextIndex = 0`; 4 consecutive common drops resolve → the assignment sequence is A, B, C, A and `rrNextIndex = 1` afterwards. With members `[A, B]` and `rrNextIndex = 0` (the state after C leaves a party whose cursor was 2), the next drop assigns to A. With members `[A, B, D]` and `rrNextIndex = 1` (D joined a party of 2), the next drop assigns to B.
- [ ] **AC-LT-15** [BLOCKING]: a solo player (party size 1) kills a mob that drops a DarkSteel item → no auction is opened; the item is assigned to the solo player through the common path.
- [ ] **CR-LT-6**: after each common-drop assignment the loot service calls `AdvanceRrNextIndex(partyID)` exactly once and never writes the cursor itself.
- [ ] **CR-LT-5 routing**: a Common drop always takes the round-robin path, whatever the party size.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-6, CR-LT-11 and party-system.md.*

**Module conventions:** see Story 001.

**Assignment:** for each Common item in the pending drop list, in list order: read the cursor, `assignee = GetMemberAtIndex(partyID, cursor)`, `Spawn(item, mobPosition, assignee, tick)` (Story 005), then `AdvanceRrNextIndex(partyID)`.

**`IPartyService` additions (extend the interface from Story 003):** `CharacterID GetMemberAtIndex(PartyID, int index)` and `void AdvanceRrNextIndex(PartyID)` — both listed in party-system.md's Interactions table.

**Contract gap — reading the cursor.** CR-LT-6 says the loot service reads the target via `GetMemberAtIndex(partyID, rrNextIndex)`, but party-system.md exposes no way to read `rrNextIndex`. Declare `int GetRrNextIndex(PartyID)` on the consumer-side `IPartyService` and note in its doc comment that party-system.md must add it (or an equivalent "next round-robin member" call) when the Party System is implemented.

**Party-side behaviour belongs to the Party System.** Clamping the cursor when a member leaves (`rrNextIndex % newCount`), leaving it unchanged on a join, and skipping Ghost / Disconnected / OutOfZone slots when advancing are all CR-PS-7. This story's stub implements only `(index + 1) % count` for `AdvanceRrNextIndex`; the leave/join parts of AC-LT-7 are tested by setting the stub to the resulting state and asserting the loot service honours it.

**Rare drops.** Classify each pending item (Story 002). Route every Rare item through one private method (e.g. `BeginRareDrop`):
- party size 1 → common path (CR-LT-11, AC-LT-15);
- party size ≥ 2 → in this story, the CR-LT-10 fallback: assign by round-robin like a common drop. Story 010 replaces this branch with the auction.

Party size is the member count of the winning party at drop time.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 007**: pickup of the assigned item.
- **Story 010**: the auction for Rare drops in a party of 2 or more.
- The Party System itself — cursor clamping, eligibility skipping, membership changes (party-system.md CR-PS-7; no epic yet).

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_RoundRobin_integration_tests.cs` (stub `IPartyService` with a settable member list and cursor)

- **AC-LT-7**: rotation
  - Given: stub party `[A, B, C]`, cursor 0; a kill whose drop list is 4 Common items
  - When: resolved
  - Then: the spawned items are assigned A, B, C, A in order; the stub's cursor is 1; `AdvanceRrNextIndex` was called 4 times
  - Edge cases: stub `[A, B]`, cursor 0, one drop → A; stub `[A, B, D]`, cursor 1, one drop → B

- **AC-LT-15**: solo rare drop
  - Given: a solo party `[S]`; a drop list with one DarkSteel item
  - When: resolved
  - Then: one item spawned, assigned to S, `isAuction = false`; the item's state after one tick is `Assigned`, not `Auctioning`
  - Edge cases: none

- **CR-LT-6**: cursor ownership
  - Given: stub party `[A, B]`, cursor 1; one Common drop
  - When: resolved
  - Then: assigned to B; `AdvanceRrNextIndex` called exactly once; no other `IPartyService` method that could mutate state was called
  - Edge cases: none

- **CR-LT-5 routing**
  - Given: a party of 3 and a drop list `[Bronze, Consumable]`
  - When: resolved
  - Then: both items are assigned by round-robin (two different members, cursor advanced twice)
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_RoundRobin_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005 (ground item spawn), Story 003 (`IPartyService`).
- Unlocks: Story 007, Story 010.
