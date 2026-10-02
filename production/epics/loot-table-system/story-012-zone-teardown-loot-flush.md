# Story 012: Zone Teardown Loot Flush

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

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

- [ ] **AC-LT-19** [ADVISORY]: a DarkSteel item is `Auctioning` with 3 valid bids when zone teardown begins → the auction closes immediately at the teardown tick; CR-LT-9 resolves (highest bidder wins); the pickup call and all `AddGold` calls complete before loot state is destroyed. With zero valid bids, CR-LT-10 round-robin applies before teardown.
- [ ] **Edge case — all ground items despawn**: at teardown every remaining non-terminal ground item goes to `Despawned` and its despawn event is raised.
- [ ] **Edge case — no double award**: an item already delivered (`Inventory`) before the flush is not announced or processed again.
- [ ] **Disposal**: after the flush the loot service has unsubscribed from every event it subscribed to.

---

## Implementation Notes

*Derived from ADR-010 and the GDD teardown edge case.*

**Module conventions:** see Story 001.

**API:** `FlushForZoneTeardown(uint teardownTick)`, called by zone teardown before the loot service is disposed:
1. Resolve every `Auctioning` item exactly as Story 011 does at window close, using `teardownTick`.
2. Despawn every remaining non-terminal item, raising `OnGroundItemDespawned` for each.
3. Clear ground-item and damage-record state.

`Dispose()` then unsubscribes from `IInventoryService.OnInventoryChanged` (Story 008).

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
  - Given: a flushed and disposed loot service
  - When: the inventory raises `OnInventoryChanged`
  - Then: the loot service does not react (no `Pickup` call, no exception)
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_ZoneTeardown_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011 (auction resolution — currently Blocked), Story 008 (inventory subscription).
- Unlocks: None.
