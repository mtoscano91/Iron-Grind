# Epic: NPC Shop

> **Layer**: Feature
> **GDD**: design/gdd/npc-shop.md
> **Architecture Module**: NPC Shop (`architecture.md` layer map, Feature), `src/ServerLogic/` (ADR-012)
> **Status**: Ready
> **Stories**: 10 created 2026-11-19 — see Stories
> **Manifest Version**: 2026-11-06

## Overview

The NPC Shop is the town hub's exchange point. A single shop NPC sells a static, infinite-stock catalog of 10 items — the four tier-matched Enhancement Scrolls (90 / 150 / 230 / 350 g) and HP / MP Potions in Small, Medium and Large (3 / 10 / 30 g) — and buys back any bag item whose `SellPriceGold > 0` at that fixed price (enhanced gear at its base price). A player opens a shop session with `OpenNPCInteraction` (town hub only; it replaces any other NPC session; 300 s wall-clock lifetime; closed by `CloseNPCInteraction`, zone change or expiry). A buy is checked in GDD order, debits gold in memory (`TrySpendGold`), makes the debit durable with a `PendingPurchase` record (`BeginPurchase`, ADR-001 A2), grants the items (`PickupRequest`) and completes the record; a failed grant refunds store first. A sell removes units with `SellItem` and credits gold with `AddGold` (capped at `GOLD_CAP`, reported honestly in `SellResult.goldEarned`). Requests carry a `requestId` and are deduplicated on `(charId, messageType, requestId)` for 300 s, at most 10 per second. Outstanding purchases are refunded at login and on reconnect before any gameplay message runs. The catalog is validated at startup (prices > 0, anti-arbitrage `BuyPrice × 10 ≥ SellPriceGold × 15`); a violation halts the server.

## Scope (user decisions 2026-11-19, `/create-epics`)

**Server only.** As with Death & Respawn and Party: the Buy / Sell modal, quantity selector (CR-SHOP-6, CR-SHOP-8 touch model, F-NS-4), scroll confirmation overlay (CR-SHOP-11) and shop audio go to presentation epics, after `/ux-design npc-shop` (GDD UX flag).

**D1 — Town hub is a zone data flag.** No zone in code or data is marked as the town hub today. This epic adds an `IsTownHub` flag to the zone definition data, a production `ITownHubQuery` that reads it, and marks the current starting zone as the hub for MVP / dev until a real hub zone is authored.

**D2 — No `PendingSell`.** ADR-001 OQ-ADR1-2 is closed by a dated note on ADR-001 (2026-11-19): `SellItem` and `AddGold` both change in-memory state that `SaveSession` persists together, so a crash loses both or neither; there is no durable split to protect. A sell writes nothing to the store on its own.

What already exists (built by earlier epics):

| Piece | Where |
|-------|-------|
| NPC interaction sessions: `INpcInteractionSessions`, `NpcInteractionSessionTracker` (wall-clock lifetime, replace-on-open), `ITownHubQuery` (no production implementation), `NpcInteractionRequestHandlers` (Open / Close) | `src/ServerLogic/NpcInteraction/` (Enhancement Stories 006, 013) |
| Codecs `OpenNPCInteraction` 0xE110, `CloseNPCInteraction` 0xE111, `NPCInteractionOpened` 0xE112, `RejectedNotInTownHub` 0xE113 | `src/Foundation/Networking/WireProtocol/` |
| Purchase records: `BeginPurchase` / `CompletePurchase` / `RefundPurchase` / `LoadOutstandingPurchases`, `OutcomeUnknown`, idempotent `BeginPurchase`, per-character write queue, SQL unique `(character_id, request_id)` | `src/ServerLogic/CharacterPersistence/` (Persistence Stories 011, 015) |
| Re-auth reconciliation loop (CR-NET-6.4 step 2) — production passes `QueryNoPurchases` / `MarkNoPurchase` stand-ins, and the loop credits memory before the store (reverse of ADR-001 A2) | `ConnectionStateMachine.CompleteReAuthSuccess`, `ZoneInstancingWiring.cs:351` |
| `SellItem(charId, slot, itemId, quantity)`, `IsSlotLocked`, `PickupRequest` (atomic) | `src/ServerLogic/InventorySystem/` |
| `TrySpendGold`, `AddGold` (cap-safe), `GoldTransactionReason` (`ScrollPurchase`, `ItemPurchase`, `ItemSell`, `CompensatingRefund`), `GoldSyncEvent` | Currency System |
| Item records for the 10 catalog items (OQ-NS-4 done) | `Assets/data/items/consumables/ItemID(29–38)_*.asset` |
| RPC guard chain (session-ready → rate limit → ownership) | `src/ServerLogic/Networking/RpcGuards/` |
| Wire schemas for `BuyRequest`, `BuyResult`, `SellRequest`, `SellResult` and the 10 shared-schema rejections (design only) | `design/gdd/networking-wire-protocol.md` §NPC Shop System Messages |

Gaps this epic closes: no shop exists; Buy / Sell messages have no codecs or IDs; the NPC session tracker is not composed in the zone server; no zone is a town hub; outstanding purchases are never refunded in production (stand-in query, no fresh-login step, memory-first order).

Moved out:

| Item | Owner |
|------|-------|
| Shop modal, quantity selectors, scroll confirmation overlay, sell-tab display (CR-SHOP-6, CR-SHOP-8 touch model, CR-SHOP-11, F-NS-4; AC-NS-16, 26, 29, 32b, 33b, 39, 40, 41) | Presentation epics, after `/ux-design npc-shop` |
| Shop audio (GDD Visual/Audio) | Presentation / audio epics |
| Enhancement System production wiring (its request handlers on the zone dispatcher) | Enhancement epic — this epic composes the shared session tracker it will use |
| Typed NPC session (OQ-NS-1, TD-058: `npcId` stored, not validated) | Before more NPC types are authored |

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-001: Purchase Transaction Integrity | `requestId` dedup on `(charId, messageType, requestId)` for 300 s (A1); `PendingPurchase` written with the in-memory gold after `TrySpendGold` (A2); store-first refund; `OutcomeUnknown` retry; reconnect reconciliation; 10 req/s rate limit; no `PendingSell` (note 2026-11-19) | LOW |
| ADR-006: Persistence Layer | `pending_purchases` table, same transaction as the gold write | LOW |
| ADR-011: Async Persistence and the Tick Loop | Purchase writes are async through the per-character write queue; the tick never blocks on them | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | Buy / Sell / Open / Close are R-OD requests handled on the tick through the dispatcher | LOW |
| ADR-012: Server/Client Assembly Boundary | Shop logic in `IronGrind.ServerLogic`; codecs in Foundation | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-19). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-shop-001 | Catalog: 10 items (4 scrolls, 6 potions), infinite stock, no gear for sale, prices data-driven; startup validation halts on `shopPrice = 0`, a catalog item missing from the Item Database, or F-NS-3 `(BuyPrice × 10) < (SellPriceGold × 15)` for an item with `SellPriceGold > 0` (scrolls exempt), logging ItemID / BuyPrice / SellPriceGold (CR-SHOP-2, CR-SHOP-10, AC-NS-27, 28, 30) | ADR-001 ✅ |
| TR-shop-002 | Shop session: `OpenNPCInteraction` only in a town hub zone (zone data flag, D1) else `RejectedNotInTownHub`; replaces any active NPC session silently; re-open restarts the 300 s wall-clock lifetime; closed by `CloseNPCInteraction`, zone change or expiry; never by death (CR-SHOP-3, CR-SHOP-4, AC-NS-01 to 06, 35) | ADR-014 ✅ |
| TR-shop-003 | Buy: checks in order — session, `1 ≤ quantity ≤ 99` (negative and 0 rejected before any gold call), in catalog, `TrySpendGold(F-NS-1 total)`; then `BeginPurchase` (fail → in-memory refund; `OutcomeUnknown` → retry same call); `PickupRequest` (atomic) → `CompletePurchase` → `BuyResult`; grant failure → `RefundPurchase` then `AddGold(CompensatingRefund)` → `RejectedInventoryFull`; refund obligation survives session close; failed refund → critical log, record kept (CR-SHOP-5, AC-NS-07 to 15, 31, 34, 36, 37) | ADR-001, ADR-006, ADR-011 ✅ |
| TR-shop-004 | Sell: checks in order — session, slot 0–19, slot holds itemId, not locked, `SellPriceGold > 0` (null item → unsellable + log), `1 ≤ quantity ≤ stack`; `SellItem` then `AddGold(ItemSell)`; `goldEarned` is the amount actually credited (GOLD_CAP); enhanced gear at base price; `quantitySold = 0` → error log, no `AddGold`, no `SellResult` (CR-SHOP-7 to 9, AC-NS-17 to 25) | ADR-001 ✅ (no `PendingSell`, D2) |
| TR-shop-005 | Idempotency and rate limit: dedup on `(charId, messageType, requestId)` for `SESSION_TTL_SECONDS`, a duplicate returns the cached result without re-processing; 10 Buy/Sell per second per character → `RejectedRateLimited` | ADR-001 ✅ |
| TR-shop-006 | Wire output: codecs and R-OD IDs for `BuyRequest`, `BuyResult`, `SellRequest`, `SellResult` and the 10 shared-schema rejections (`requestId` only), routing registry entries; a `GoldSyncEvent` is queued after every successful buy and sell (AC-NS-32, 33) | ADR-014 ✅ |
| TR-shop-007 | Reconnect reconciliation: every `GoldDebited` record is refunded once at login and on re-auth before gameplay messages, store first (`RefundPurchase`, then `AddGold`), replacing the `QueryNoPurchases` stand-in; each refund logged (ADR-001 Decision 4, A2) | ADR-001, ADR-011 ✅ |
| TR-shop-008 | Zone wiring: catalog, session tracker, production `ITownHubQuery`, shop handlers and reconciliation composed in the zone server; end-to-end check that a player in the hub opens the shop, buys, sells, and sees `BuyResult` / `SellResult` and `GoldSyncEvent` | ADR-014 ✅ |

## Open Items for Story Readiness

1. **Message IDs.** 14 new R-OD messages; the shop block `0xE114`–`0xE11F` holds only 12. Pick a second free block (0xE180 upward is free) and record IDs in `networking-wire-protocol.md`. Check every ID against the R-OD range 0xE000–0xEFFF.
2. **Wire doc dedup comment.** The wire GDD still states the pre-A1 key `(SenderEntityID, requestId)` in the section header; fix to `(charId, messageType, requestId)` at the codec story.
3. **Catalog data source.** The catalog prices are authored values (GDD CR-SHOP-2); decide asset shape (ScriptableObject list, like the other data assets) at the catalog story. Prices must not be hardcoded.
4. **Time injection.** AC-NS-05 needs an injectable wall clock or configurable TTL; the tracker already takes `Func<double> clockSeconds` and a lifetime — confirm at the session story.
5. **Mid-transaction zone change (AC-NS-36).** The buy path is async (`BeginPurchase`); the test must inject a zone change between the debit and the grant. Decide the seam at the buy story.
6. **Reconciliation order.** The existing re-auth loop credits memory before marking the record (`markPurchaseRefunded`); ADR-001 A2 requires store first. Fix it in TR-shop-007's story and add the fresh-login step.
7. **`requestId` counter reset.** ADR-001 says the client counter resets on each `OpenNPCInteraction`; the server dedup window must not return a stale cached result from a previous session with the same id (clear the character's shop dedup entries on open).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All server-side acceptance criteria from `design/gdd/npc-shop.md` are verified (client ACs moved out above)
- All Logic and Integration stories have passing test files in `tests/`
- ADR-001 carries the OQ-ADR1-2 note; `networking-wire-protocol.md` carries the assigned message IDs

## Stories

Created 2026-11-19 (lean: QL-STORY-READY skipped; QA test cases written at creation, refine at `/story-readiness`). Suggested order: 001 → 003 → 002 → 004 → 005 → 006 → 007 → 008 → 009 → 010 (sell before buy: no async step).

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Shop catalog and startup validation](story-001-shop-catalog-and-startup-validation.md) | Logic | Ready | ADR-001, ADR-012 |
| 002 | [Town hub flag and shop session](story-002-town-hub-flag-and-shop-session.md) | Integration | Ready | ADR-014 |
| 003 | [Shop wire messages](story-003-shop-wire-messages.md) | Logic | Ready | ADR-014, ADR-001 A1 |
| 004 | [Repeat-request cache and rate limit](story-004-request-dedup-and-rate-limit.md) | Logic | Ready | ADR-001 |
| 005 | [Sell transaction](story-005-sell-transaction.md) | Integration | Ready | ADR-001, ADR-014 |
| 006 | [Buy transaction — happy path and rejections](story-006-buy-transaction.md) | Integration | Ready | ADR-001 A2, ADR-006, ADR-011 |
| 007 | [Buy failure and refund paths](story-007-buy-failure-and-refund-paths.md) | Integration | Ready | ADR-001 A2 |
| 008 | [Reconnect purchase refunds](story-008-reconnect-purchase-refunds.md) | Integration | Ready | ADR-001, ADR-011 |
| 009 | [Zone composition wiring](story-009-zone-composition-wiring.md) | Integration | Ready | ADR-014 |
| 010 | [End-to-end buy and sell](story-010-end-to-end-buy-and-sell.md) | Integration | Ready | ADR-014, ADR-001 |

## Next Step

Run `/story-readiness production/epics/npc-shop/story-001-shop-catalog-and-startup-validation.md`, then `/dev-story`.
