# Story 006: Common Drop Round-Robin Assignment

> **Epic**: Loot Table System
> **Status**: Complete
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

**Performance**: Runs once per kill, not per tick. Per drop: one cursor read, one member lookup, one `Spawn`, one advance. No per-tick cost. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

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
- [ ] **Invalid member** *(readiness decision, 2026-10-02)*: if `GetMemberAtIndex` returns `CharacterID(0)` for the current cursor, the loot service logs exactly one server error, does not spawn that item, still calls `AdvanceRrNextIndex` once, and processes the remaining items of the kill normally.

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

**Settled at readiness (2026-10-02):**
- **Class shape:** a new `LootDropDistributor` implements `ILootDropSink` (Story 004's seam; never called with an empty list). Constructor: `IPartyService`, `LootEquipmentCache`, `IGroundItemService`, and a `Func<uint>` tick source — the same one `PartyTagTracker` takes; the sink signature carries no tick and `Spawn` needs one.
- **`IGroundItemService`:** extract an interface from `GroundItemService` (Story 005 code review) — `Spawn`, `Tick`, `TryGetGroundItem` and the two events; the distributor depends on the interface.
- **Solo parties:** `AdvanceRrNextIndex` is called after every assignment, solo included (CR-PS-7: a solo cursor is 0 and ignored, so the call is harmless).
- **Invalid member:** the distributor checks the member itself and logs its own error; it does not call `Spawn` with `CharacterID(0)` (that would log a second error).
- **Existing test stubs:** `IPartyService` gains three members, so the stubs in `LootTable_PartyTag_integration_tests.cs`, `LootTable_KillResolution_integration_tests.cs` and `LootTable_GroundItemLifecycle_tests.cs` gain them too. No other change to those files.

**Added at code review (2026-10-02, user: "fix all"):**
- Each drop is handled on its own: the kill's gold is already paid and its record cleared when the sink is called, so an exception from a party call on one item is logged (`Debug.LogException`) and the remaining items are still distributed. A failed item does not advance the cursor.
- A null member list counts as party size 0 (as `LootTableService.ResolveMobDrop` treats it).
- The Rare branch is `partySize >= 2` → (Story 010: auction; today: round-robin); every smaller size, including 0, takes the common path and can never open an auction.
- A drop with `ItemID` 0 logs one error and does not consume a round-robin turn (no cursor read, no advance).
- `IGroundItemService` now carries the full contract (including "`Spawn` does not throw on a subscriber failure"); `GroundItemService` inherits the docs.
- `IPartyService` docs state the cursor domain (`[0, MemberCount - 1]`, 0 for a solo or unknown party) and that `GetMemberAtIndex` shares that index domain and never throws.
- For Story 010: `BeginRareDrop` is synchronous and `void`; an auction spans ticks, and `Spawn` hard-codes `isAuction = false` — prefer a separate `SpawnAuction` over a bool parameter. The auction needs the party size at drop time; consider `GetPartySize(PartyID)` instead of a member-list snapshot.

**Party-side gaps (not fixed here; for the Party System GDD):** party-system.md is inconsistent about the slot count — `MAX_PARTY_SIZE = 4` (line 31–32), but CR-PS-8 speaks of "the 6-slot member array" and an edge case of "slot index 5". A single "next round-robin member" call would replace `GetRrNextIndex` + `GetMemberAtIndex` and close the stale-eligibility gap below. the loot service reads the cursor before assigning, and the Party System only skips ineligible members when advancing — a member who becomes Ghost, Dead or OutOfZone after the last advance still receives the next drop. CR-PS-7 also says "no loot is assigned" when every member is ineligible, but the interface gives the loot service no way to learn that.

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

- **Invalid member**
  - Given: a stub party whose `GetMemberAtIndex` returns `CharacterID(0)` for the first cursor value and a real member for the next; a drop list of 2 Common items
  - When: resolved
  - Then: exactly one error logged; one item spawned, assigned to the real member; `AdvanceRrNextIndex` called twice
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_RoundRobin_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 22 test methods (24 NUnit cases), all 5 criteria covered

---

## Dependencies

- Depends on: Story 005 (ground item spawn), Story 003 (`IPartyService`).
- Unlocks: Story 007, Story 010.

---

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 5/5 passing (0 deferred). "No auction is opened" (AC-LT-15) cannot fail before Story 010 — the assertions guard that story's change; the leave/join halves of AC-LT-7 are tested by setting the party stub to the resulting state (clamping is the Party System's, which has no code)
**Deviations**: Advisory only — `TR-loot-008` is not in the TR registry (same accepted gap as Stories 001–005); rules not in the GDDs: an invalid member loses the item and still advances the cursor (user decision), a drop with `ItemID` 0 consumes no round-robin turn (code review), a party call that throws on one item does not stop the remaining items (code review); a Rare drop in a party of 2+ takes the round-robin path until Story 010; `GetRrNextIndex` is declared consumer-side and is not in party-system.md; 17 test methods go beyond the written QA cases (4 from implementation, 13 from the code review)
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_RoundRobin_integration_tests.cs` (22 test methods, 24 NUnit cases; real `GroundItemService` and `LootEquipmentCache`, stub party service with call counters and call-order log). Full EditMode suite 1267/1267 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `LootDropDistributor.cs` and siblings returned CHANGES REQUIRED (no per-item exception isolation; null member list; Rare branch shape; interface docs; four weak assertions); all applied except adding `GetPartySize(PartyID)` (left for Story 010); suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-048 extended (round-robin rules not in the GDDs; Party GDD gaps: no cursor getter, stale eligibility at read time, "no loot assigned" not observable, slot-count inconsistency)
**Files outside the story's own list**: `StubPartyService` in `LootTable_PartyTag_integration_tests.cs`, `LootTable_KillResolution_integration_tests.cs` and `LootTable_GroundItemLifecycle_tests.cs` gained the three new `IPartyService` members
**Note for Story 007**: depend on `IGroundItemService`; it needs a claim begin / complete / release API, and `Tick` must not expire an item in `Claiming`
**Note for Story 010**: replace the `partySize >= 2` branch of `BeginRareDrop` with the auction; see the Story 010 notes under "Added at code review"
