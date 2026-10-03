# Story 012: Zone Teardown Loot Flush

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — Edge Cases ("If a zone teardown occurs while ground items exist"), States (`Despawned` entry: "zone teardown")
**Requirement**: `TR-loot-011`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: Zone-scoped services are disposed at zone teardown; every server-side event subscriber implements `IDisposable` and unsubscribes in `Dispose()`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API. The zone teardown sequence itself (ADR-009) is not implemented in code yet.

**Control Manifest Rules (Core layer)**:
- Required: all zone-scoped services implement `IZoneScopedService : IDisposable`; subscribers unsubscribe in `Dispose()` — ADR-010
- Forbidden: lambda captures for persistent subscriptions — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [x] **AC-LT-19** [ADVISORY]: a DarkSteel item is `Auctioning` with 3 valid bids when zone teardown begins → the auction closes immediately at the teardown tick; CR-LT-9 resolves (highest bidder wins); the pickup call and all `AddGold` calls complete before loot state is destroyed. With zero valid bids, CR-LT-10 round-robin applies before teardown.
- [x] **Full bag at teardown** *(added 2026-10-03, GDD teardown edge case / AC-LT-19)*: bids A 400 (bag full) and B 350 (free slot) → no `TrySpendGold(A, …)`; B is debited 350 and receives the item; the pool is split on 350; `OnAuctionResolved` names B. An auction already in a winner grace (Story 013) is resolved the same way, with the reserved bidder tried first: still full → skipped; a slot freed by then → they are charged and win.
- [x] **Nobody can receive it** *(added 2026-10-03)*: every bidder's bag is full → no gold moves, the item is despawned, `OnAuctionResolved` reports the fallback outcome.
- [x] **Edge case — all ground items despawn**: at teardown every remaining non-terminal ground item goes to `Despawned` and its despawn event is raised.
- [x] **Edge case — no double award**: an item already delivered (`Inventory`) before the flush is not announced or processed again.
- [x] **Disposal**: after `Dispose()`, `GroundItemService` no longer reacts to `IInventoryService.OnInventoryChanged`, and `LootAuctionService` no longer reacts to the ground item spawn and despawn events or to inventory changes — verified on services disposed *without* a prior flush, so live items exist and a reaction would be visible.

---

## Implementation Notes

*Derived from ADR-010 and the GDD teardown edge case.*

**Module conventions:** see Story 001.

**API** *(revised at `/story-readiness` 2026-10-03 — the loot state lives in three services since Stories 004–011)*: a new small class `LootTeardownCoordinator` takes `ILootAuctionService`, `IGroundItemService` and `ILootTableService`. `FlushForZoneTeardown(uint teardownTick)`, called by zone teardown before the loot services are disposed, calls in this order:
1. `ILootAuctionService.ResolveAllForTeardown(teardownTick)` *(new)* — resolves every open auction now, whatever its window or expiry tick, including one in a winner grace (Story 013). Same rules as Story 011 / 013, except there is no grace: a bidder with a full bag is skipped at once with no gold moved. If no bidder can receive the item, or there are no valid bids and the round-robin cannot assign it, `DespawnAuctionItem` removes it.
2. `IGroundItemService.DespawnAll()` *(new)* — every remaining live item goes to `Despawned`, one `OnGroundItemDespawned` each.
3. `ILootTableService.Clear()` — damage records. `Clear()` exists on the `LootTableService` class only: add `void Clear()` to the `ILootTableService` interface, doc-commented *(found at `/story-readiness` 2026-10-03)*.

Then dispose in this order: `LootAuctionService` (ground item spawn / despawn subscriptions, and the inventory subscription Story 013 adds), then `GroundItemService` (`IInventoryService.OnInventoryChanged`, Story 008).

**An auction in a winner grace at teardown** *(user decision at `/story-readiness`, 2026-10-03)*. The reserved bidder is tried first, under the teardown rule: `HasFreeSlot` is read again — a free slot now (also one freed but not yet processed by a `Tick`) means they are charged and win; a bag still full means they are skipped with no gold moved and the next bid is tried. The pending resolution state of Story 013 is private to `LootAuctionService`, which is why `ResolveAllForTeardown` lives on that service. No blocked notice is raised at teardown.

**Test fixtures first (closes TD-054)** *(user decision at `/story-readiness`, 2026-10-03)*. Before writing the new test file, move the rig and fakes shared by `LootTable_AuctionResolution_integration_tests.cs` and `LootTable_AuctionWinnerGrace_integration_tests.cs` (`FakeItemDatabase`, `GoldCall`, `RecordingCurrencyService`, a party stub with a mutable member list, `NotConnected` and the throw switches, and the rig builder if it fits) into `tests/EditMode/LootTableSystem/LootTestFakes.cs`, and make both files use them. No test may change behaviour or be removed; the full EditMode suite must pass (1427 before this story) before the new tests are added. The new teardown test file uses the shared fakes.

**Zero bids at teardown.** CR-LT-10 still runs: the item is assigned by round-robin (`OnGroundItemAssigned`, cursor advanced) and `OnAuctionResolved` is raised with the fallback outcome; step 2 then despawns it. No pickup is attempted for the assignee.

**`Claiming` at teardown.** The GDD says a pending `PickupRequest` must be treated as delivered and not double-awarded. In this implementation `IInventoryService.Pickup` is a synchronous call, so no item is ever left in `Claiming` between calls and no extra handling is needed — say so in a comment rather than adding dead code.

**`IZoneScopedService` does not exist in code.** The control manifest requires zone-scoped services to implement it, but no such interface has been written (no Zone Instancing code yet). Implement `IDisposable` now and note in the class doc comment that `IZoneScopedService` must be adopted when Zone Instancing defines it. Do not invent the interface here.

---

## Out of Scope

*Handled by neighbouring stories / other systems — do not implement here:*

- **Story 011**: the auction resolution rules this story invokes.
- The zone teardown sequence and its ordering guarantees ("zone teardown does not finalize until all pending loot resolution calls have returned or timed out") — Zone Instancing (ADR-009); this story provides the synchronous flush it will call.
- Persisting ground items across zone sessions — ground items are scoped to the zone's lifetime.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_ZoneTeardown_integration_tests.cs`

- **AC-LT-19**: auction resolves at teardown
  - Given: party `[A, B, C, D]` with gold; a DarkSteel auction with bids A 400, B 350, C 300, before `windowCloseTick`
  - When: `FlushForZoneTeardown(tick)`
  - Then: winner A; `Pickup(A, item, 1)` and 4 `AddGold` calls happened during the flush; `OnAuctionResolved` raised; afterwards no ground item records remain
  - Edge cases: zero bids → round-robin assignment and `OnAuctionResolved(…, true)` happen before the item is despawned

- **Full bag at teardown** *(added 2026-10-03)*
  - Given: a DarkSteel auction with bids A 400 and B 350; A's bag is full, B has a free slot
  - When: flush
  - Then: no `TrySpendGold(A, …)`; `TrySpendGold(B, 350, AuctionBid)`; `Pickup(B, item, 1)`; four `AddGold(_, 87, MonsterDrop)`; `OnAuctionResolved` names B
  - Edge cases: the auction was already in A's winner grace when teardown began → same outcome; A in a winner grace has freed a slot (inventory change raised, no `Tick` since) when teardown begins → `TrySpendGold(A, 400, AuctionBid)`, `Pickup(A, item, 1)`, four `AddGold(_, 100, MonsterDrop)`, `OnAuctionResolved` names A *(added 2026-10-03)*

- **Nobody can receive it** *(added 2026-10-03)*
  - Given: bids A 400 and B 350, both bags full
  - When: flush
  - Then: no `TrySpendGold`, no `AddGold`; one `OnGroundItemDespawned`; `OnAuctionResolved(…, true)`
  - Edge cases: none

- **All ground items despawn**
  - Given: two `Assigned` items and one item in `Spawning`
  - When: flush
  - Then: three `OnGroundItemDespawned` events; no records remain
  - Edge cases: none

- **No double award**
  - Given: one item already delivered, one still `Assigned`
  - When: flush
  - Then: exactly one `OnGroundItemDespawned`; no additional `Pickup` call for the delivered item
  - Edge cases: none

- **Disposal**
  - Given: an `Assigned` item whose assignee stands on it with a blocked pickup (bag full); both services disposed without a flush
  - When: the inventory reports a free slot and raises `OnInventoryChanged`, then `Tick`
  - Then: no further `Pickup` call (the retry would have happened had the subscription survived); no exception
  - Edge cases: a ground item spawned after `LootAuctionService.Dispose()` is not tracked — a later `Tick` resolves nothing

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_ZoneTeardown_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 14 tests, all passing (EditMode 1441/1441, Unity 6000.3.10f1 batch mode, 2026-10-03)

---

## Dependencies

- Depends on: Story 011 (auction resolution — Complete 2026-10-03), Story 008 (inventory subscription — Complete), Story 013 (winner grace — Complete 2026-10-03).
- Unlocks: None.

**Performance**: no impact expected — the flush runs once per zone teardown.

---

## Completion Notes
**Completed**: 2026-10-03
**Criteria**: 6/6 passing
**Test Evidence**: Integration: `tests/EditMode/Integration/LootTableSystem/LootTable_ZoneTeardown_integration_tests.cs` (14 tests). Full EditMode suite 1441/1441 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — lean self-review in the implementing session (no specialist agents); APPROVED WITH SUGGESTIONS, suggestions 1–4 applied. QL-TEST-COVERAGE and LP-CODE-REVIEW gates skipped (Lean mode).
**Deviations** (all advisory):
- The disposal criterion's auction half is not observable through the public API: a disposed `LootAuctionService` short-circuits `Tick` and `SubmitBid`, so the test passes whether or not its subscriptions were removed (TD-057). The ground-service half is a real check.
- `ILootTableService.Clear()` is verified through a counting stub, not the real `LootTableService` (it needs seven collaborators).

**User decisions 2026-10-03**: at teardown the reserved grace bidder is tried first with a fresh `HasFreeSlot` read; the shared auction fakes moved to `LootTestFakes.cs` before the new tests were written; auctions resolve at once at zone shutdown (no waiting), and the normal-play rule stays as it is (per-bidder 30-second graces, then the rotation member with a fresh 120 seconds).

**"Nobody wins" at teardown** (rule set by the assistant, accepted by the user after an explanation): at least one bidder passed over for a full bag → `DespawnAuctionItem`, no round-robin, cursor untouched; otherwise (no valid bids, or every payment failed) → the CR-LT-10 round-robin assignment runs first and `DespawnAll` removes the item. Both raise `OnAuctionResolved` with the fallback outcome. The GDD does not spell out the mixed cases (TD-056).

**Additions beyond the story text**: `LootAuctionService.AbandonAuctionAtTeardown` — a party read that throws at teardown despawns the item and announces the fallback once (in `Tick` the same failure is retried next tick); one warning when `ResolveAllForTeardown` is skipped because it was re-entered from a subscriber; `LootTeardownCoordinator.Dispose()` disposes the ground item service even if the auction service's dispose throws.

**Tech debt**: TD-054 narrowed (the three auction / teardown test files share the fakes; other loot test files still carry their own stubs); TD-056 extended (teardown wording); TD-057 added (disposal not observable).

**Still open for Zone Instancing**: the teardown sequence and its ordering guarantees (ADR-009); adopting `IZoneScopedService` on `LootTeardownCoordinator` when that interface exists. `TR-loot-011` is still a placeholder (`tr-registry.yaml` is empty).
