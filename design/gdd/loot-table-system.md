# Loot Table System

> **Status**: Approved (2026-10-07 — CR-LT-16 amendment, Enhancement Scroll exclusion at MVP, AC-LT-26: lean re-review found 1 blocking item, the duplicate AC-LT-25 ID; fixed the same day and the revision accepted by the user without a further review pass. Earlier: lean re-review #3, 2026-10-03, of the 2026-10-03 amendments; base design Approved 2026-05-17)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-10-07 (CR-LT-16 — one sentence added at the close of Enhancement Story 008: table validation runs after the Item Database is ready, and a database that is not ready is a startup error while the knob is `false`; wording that follows the code, no review pass); 2026-10-07 (revision after the lean re-review of the CR-LT-16 amendment: the scroll criterion renumbered AC-LT-25 → AC-LT-26 — AC-LT-25 is the winner-grace criterion; AC-LT-26 gained a clause for two offending entries; the two "Consumables" drop rate rows exclude Enhancement Scrolls at MVP); 2026-10-07 (CR-LT-16 — Enhancement Scroll exclusion at MVP, knob `ALLOW_ENHANCEMENT_SCROLL_DROPS`, Item Database `ScrollData` read at table validation, Enhancement System as a downstream dependent); 2026-10-03 (revision after lean re-review #2: CR-LT-9.1 — "disconnected" defined as the party member status and checked only when the bid is tried, a running grace is not ended by a disconnect; CR-LT-12 — the fresh `expiryTick` is carried by `GroundItemAssigned`, and also applies to the paid-winner invariant path; `N = 1` on the paying tick stated; `AuctionOpen` corrected; message lists completed — lean re-review pending); 2026-10-03 (winner-grace revision after the lean re-review: CR-LT-9.1 — balance read before a grace, no grace for a disconnected bidder, `N` on the paying tick; CR-LT-12 — fresh TTL for any fallback that follows a grace; state table, Interactions, F-LT-2, the disconnect and leaver edge cases, the discard modal client rule, AC-LT-25 and bookkeeping aligned — lean re-review pending); 2026-10-03 (winner grace: new CR-LT-9.1 — a bidder with a full bag is not charged and gets `AUCTION_WINNER_GRACE_TICKS` = 600 to free a slot, then the next bid is tried; no grace at zone teardown; CR-LT-9 last sentence, CR-LT-12, the `Auctioning` state row, the teardown edge case, AC-LT-12, AC-LT-19 and the discard modal updated; new AC-LT-25 and tuning knob — lean re-review pending); 2026-10-03 (Story 011 readiness decisions: CR-LT-9 — any non-`Success` `TrySpendGold` result disqualifies the bidder, and bids from characters who left the party are skipped; CR-LT-12 — an item `Assigned` out of an auction closed at `expiryTick` gets a fresh TTL; Edge Cases leaver line aligned — lean re-review pending); 2026-10-03 (Currency amendment: CR-LT-9, the Currency rows of the Interactions and Dependencies tables, and AC-LT-13 now name `GoldTransactionReason.AuctionBid` for the auction winner's `TrySpendGold` — no rule change); 2026-05-17 (lean re-review pass 2: B-LT-1 Party System status + rrNextIndex ownership propagated to Dependencies table; B-LT-2 Party System "Not yet designed" corrected; R-1 CR-LT-13.1 OQ-LT-4 reference updated; R-2 inventory-system.md re-approval noted; R-3 Interactions table AdvanceRrNextIndex corrected)
> **Implements Pillar**: Earned Power (primary — solo) / Social Gravity (primary — party) / Legendary Gear (contextual)

## Overview

The Loot Table System is the server-authoritative probability engine that determines what items and gold each mob drops on death. It owns a static data layer — one loot table definition per mob type — that specifies drop pools, per-entry drop probabilities, gold ranges, and party bonus rules. At runtime the server queries the system exactly once per kill event, draws from the mob's table using a server-seeded RNG, and resolves the outcome: zero or more item pickups plus a gold award. Item drops are delivered to the killing character's Inventory System via the established `PickupRequest` protocol; gold drops are submitted to the Currency System with reason code `GoldTransactionReason.MonsterDrop`. The Loot Table System also owns **drop fate** — the authoritative rule for what happens to an item when a pickup attempt returns `PickupResult(fail)` because the player's inventory is full. Drop table entries are read-only data after initialization; the system pre-indexes drop pools from the Item Database at startup and holds no mutable runtime state beyond an internal RNG seed counter. The Loot Table System is the loop's payoff engine: every kill either deposits something in the player's bag or it doesn't — and this system decides which.

## Player Fantasy

Every kill is a pull on the lever, and every drop has weight. The player has ground the same mob a thousand times and knows the table by heart — and that's exactly why the moment still matters. When the mob's HP bar empties, their eyes are already on the ground next to the corpse. An item lands with a sound. It exists in the world for a beat before they walk over to claim it. That beat is the system working.

Solo, the fantasy is the **gambler's clarity**: full knowledge of the odds, full ownership of the outcome. The drop isn't a surprise — it's a receipt. Confirmation that the table is fair and the hours were worth something. The player doesn't feel lucky when something good drops. They feel **owed**. Earned Power in its purest form.

In a party, that receipt becomes a ceremony. The item hits the ground and everyone sees it. Nobody moves for a beat. The party's collective attention settles on the object — its tier badge, its column of light. That three-second pause before anyone types anything is **Social Gravity at work**. A Steel item doesn't matter more because it's rare; it matters more because four other people are standing there watching you walk over and claim it.

The rare drop goes further. An auction opens. Numbers appear. People who were killing mobs a second ago are now bidding in real time, every bid visible to every party member, the window counting down. The item being contested still exists as a physical object on the ground — no menu abstraction, no UI fiction. When the auction closes and the winner walks to it, that walk is witnessed too.

The loot system starts as a solo grind engine — Earned Power, receipt by receipt. The moment a second player enters the frame, the same mechanics begin serving Social Gravity instead. Both pillars use the same objects, the same sounds, the same floor-landing animation. The handoff is seamless. The item just has an audience now.

*Pillar alignment: Earned Power (primary — solo play) / Social Gravity (primary — party play) — the same mechanics serve both pillars; the handoff is contextual, not structural. Legendary Gear (contextual) — the DarkSteel auction is where a piece of gear first acquires social value, before it ever leaves the ground.*

*Design test: If debating whether drops should auto-pickup silently or land on the ground with a visual and sound, this fantasy says land on the ground — in solo and party. The beat before claiming is the receipt (solo) and the witness moment (party). If debating whether to show item drop probabilities, this fantasy says yes — gambler's clarity requires knowing the odds. If debating whether the auction should be visible to all party members in real time, this fantasy says yes — the drama requires a shared frame.*

## Detailed Design

### Core Rules

**CR-LT-1 — Drop Roll Architecture**
The system uses independent per-entry rolls. At mob death, each entry in the mob's loot table is evaluated as an independent Bernoulli trial. The server draws one `float ∈ [0,1)` from a server-controlled PRNG for each table entry and compares it to the entry's authored `DropChance`. Entries where `roll < DropChance` are added to the pending drop list. All entries are evaluated before any item is spawned. The result is a `List<ItemID>` of length 0 to N (where N = table entries). A result of zero items is expected and common.

**PRNG seeding:** The server uses a single process-level `IRandomProvider` (ADR-013), created once at server startup and seeded explicitly from system entropy so that the seed can be logged. The seed value is written to the server startup log immediately after initialization for post-hoc auditability. No per-zone or per-mob re-seeding occurs. Tests that require deterministic drop sequences must inject a seeded or scripted `IRandomProvider` via the Loot Table System's dependency injection boundary.

**CR-LT-2 — Drop Pool Initialization**
At server startup the system calls `IItemDatabase.GetItemsByCategory(ItemCategory.Equipment)` and caches all equipment definitions in a static `Dictionary<ItemID, ItemDefinition>`. This call is made exactly once — never per-drop. Loot table definitions (per-mob entries, gold ranges) are authored as static data assets loaded at startup. No loot table data is mutable at runtime.

**CR-LT-3 — Party Tag: Damage Accumulation**
When a mob takes damage, the server attributes `DamageResult.FinalDamage` to the attacker's party in a per-mob `Dictionary<PartyID, (uint cumulativeDamage, uint firstDamageTick)>` called `damageRecord`. Solo players are treated as a party of size 1 with a unique `PartyID`.

**CR-LT-4 — Party Tag: Threshold Lock**
The tag locks to the **first** party whose `cumulativeDamage` in `damageRecord` reaches or exceeds `Mathf.CeilToInt(mob.MaxHP × TAG_THRESHOLD_FRACTION)` (default `TAG_THRESHOLD_FRACTION = 0.33`). The lock fires within the same server tick as the threshold-crossing hit — subsequent damage from any party does not change tag ownership. **Fallback**: if no party crosses the threshold before the mob dies (possible on very low HP mobs or a one-shot kill), the party with the highest `cumulativeDamage` at death wins; ties break by earliest `firstDamageTick`. **No attacker**: if `damageRecord` is empty at death (trap kill, scripted death, no player involvement), no drops or gold are distributed.

**CR-LT-5 — Drop Tier Classification**
Each ItemID in the pending drop list is classified by `itemDef.EquipmentData.GearTier` from the pre-indexed cache:
- `GearTier.Bronze`, `GearTier.Iron`, or `GearTier.None` (Consumables): **Common drop** — auto-assigned via round-robin (CR-LT-6).
- `GearTier.Steel` or `GearTier.DarkSteel`: **Rare drop** — enters gold bid auction (CR-LT-8).

**CR-LT-6 — Common Drop Assignment: Round-Robin**
The winning party's round-robin cursor is owned and maintained by the Party System (CR-PS-7) as `rrNextIndex` on `PartyState`. When a common drop resolves, the Loot Table System reads the assignment target via `IPartySystem.GetMemberAtIndex(partyID, rrNextIndex)` and exclusively assigns the item to that character. After assignment, the Loot Table System calls `IPartySystem.AdvanceRrNextIndex(partyID)` — the Party System advances the cursor, handles clamping on size change, and skips ineligible slots (Ghost, Disconnected, OutOfZone). Member ordering (join-time stable, ascending) and cursor lifecycle (leave, join, disband) are governed by Party System CR-PS-7.

**CR-LT-7 — Common Drop Pickup**
An assigned character has exclusive pickup rights for the item's ground timer duration. When the character enters `PICKUP_RADIUS_UNITS` of the ground item, the server automatically calls `IInventorySystem.PickupRequest(assignedCharacterID, itemID, 1)` — no explicit player input required. If `PickupResult.Success`: item is removed from ground (terminal → Inventory). If `PickupResult.Fail` (bag full): drop fate rule applies (CR-LT-13).

**CR-LT-8 — Rare Drop: Auction Window**
When a rare drop lands in a party with size ≥ 2, an open gold bid auction is started with window duration `AUCTION_WINDOW_TICKS = 600` ticks (30 seconds). Each party member may submit any number of `LootBidRequest` messages with `BidAmount ≥ itemDef.SellPriceGold` (minimum bid = item's NPC sell price: 90g for Steel, 270g for DarkSteel). Bids below the floor are rejected server-side without notification to other members. All valid bids are broadcast to all party members as `LootBidUpdate` messages in real time (fully transparent). Members may revise their bid upward any number of times before the window closes; bids cannot be reduced once submitted.

**CR-LT-9 — Rare Drop: Auction Resolution**
At `windowCloseTick`, the server identifies the highest valid bid. In a bid tie, the earlier-submitted bid (by server tick) wins. The server calls `TrySpendGold(winner, winnerBid, GoldTransactionReason.AuctionBid)`. If `TrySpendGold` returns `Success`: the winner receives the item via `PickupRequest`; `goldPerMember = floor(winnerBid / N)` where N is the party size on the tick `TrySpendGold` succeeds (the paying tick — `windowCloseTick` unless a CR-LT-9.1 grace ran); remainder is discarded; every party member (including the winner) receives `AddGold(characterID, goldPerMember, GoldTransactionReason.MonsterDrop)`. The winner's net cost is `winnerBid − goldPerMember`. If `TrySpendGold` returns `InsufficientFunds`: the current winner is disqualified and the server re-runs resolution on the remaining valid bids in descending bid order, calling `TrySpendGold` on each in turn; the first bidder whose `TrySpendGold` returns `Success` wins and resolution proceeds normally. Any other non-`Success` result (`CharacterNotFound`, `ConcurrencyConflict`) is treated the same way — the bidder is disqualified, a server error is logged, and resolution moves to the next bid *(added 2026-10-03)*. Bids placed by a character who is no longer a party member at resolution are not valid bids and are skipped *(added 2026-10-03)*. If no bidder passes `TrySpendGold`, the auction falls to CR-LT-10 (round-robin; no gold changes hands). A bidder whose bag is full is not charged and is handled by CR-LT-9.1 *(replaces "CR-LT-13 applies — the gold pool is still distributed regardless", 2026-10-03)*.

**CR-LT-9.1 — Winner Grace on Bag Full** *(added 2026-10-03)*
Before `TrySpendGold` is called on a bidder, the server checks that the bidder has a free inventory slot (`HasFreeSlot`). If they have one, CR-LT-9 proceeds unchanged (spend, `PickupRequest`, pool split, `AuctionResolved`). If their bag is full, the server decides whether a grace is worth granting *(added 2026-10-03, revision)*: it reads `GetBalance(bidder)` and the bidder's party member status, and if the balance is below their bid, or the status is `Disconnected` or `Ghost` (Party System CR-PS-9 / CR-PS-11 — a bidder inside the 5-second reconnect window is not yet `Ghost` and counts as connected), the bidder is disqualified at once — no grace, no `BagFullPickupBlocked`, no gold moved — and the next bid in order is tried on the same tick. Otherwise no gold moves; the item is reserved for that bidder for `AUCTION_WINNER_GRACE_TICKS` (default 600 ticks = 30 seconds) and the server sends them `BagFullPickupBlocked(groundItemID, itemID, displayName, remainingTicks)` with `remainingTicks` = the ticks left in the grace. On the first server tick after an `InventoryChangedEvent` that leaves the bidder with a free slot, CR-LT-9 proceeds for that bidder — no proximity to the item is required; `N` for the pool split is the party size on that tick. If that `TrySpendGold` fails because the bidder spent gold during the grace, they are disqualified as in CR-LT-9; an item they discarded to make room is not restored. If the grace expires, or the bidder leaves the party during it, the bidder is disqualified with no gold moved (on the first server tick after the event) and the next bid in order is tried under the same rule, with its own grace. A disconnect during a grace does not end it — the grace runs to its deadline, so a short network drop costs the bidder nothing *(revised 2026-10-03: the connection status is checked only when the bid is tried)*. If every bidder is disqualified, CR-LT-10 applies. The item stays `Auctioning` during a grace: bids stay closed, it does not despawn at `expiryTick`, and CR-LT-13.1 (TTL pause) does not apply to the grace. `AuctionResolved` is sent once, at the final outcome. A winner therefore never holds an undelivered auction item, and never pays before delivery is possible.

**CR-LT-10 — Rare Drop: Zero Bids**
If no party member submits a valid bid before `windowCloseTick`, the item is reclassified as a common drop. It is assigned via round-robin (CR-LT-6) at `windowCloseTick`. No gold changes hands.

**CR-LT-11 — Rare Drop: Solo Player**
If the winning party is size 1 (solo player), no auction is opened. The item is auto-assigned to the solo player via the common drop path. CR-LT-7 and CR-LT-13 apply normally.

**CR-LT-12 — Ground Item Timer**
Every spawned ground item carries `expiryTick = spawnTick + GROUND_ITEM_TTL_TICKS` (default `GROUND_ITEM_TTL_TICKS = 2,400` ticks = 120 seconds). If the item has not entered terminal state by `expiryTick`, it despawns. An open auction whose ground item reaches `expiryTick` closes immediately and resolves with whatever valid bids exist at that tick (normal CR-LT-9, or CR-LT-10 if zero valid bids). An item in a CR-LT-9.1 grace does not despawn at `expiryTick`. An item that becomes `Assigned` out of an auction (the CR-LT-10 round-robin fallback) gets a fresh pickup window, `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS`, in two cases: the auction resolved at or after its `expiryTick`, or at least one CR-LT-9.1 grace ran before the fallback *(added 2026-10-03; the bag-full-winner case was removed the same day — CR-LT-9.1 replaces it; the any-grace case was added in the revision)*. The same two cases apply to an auction item that ends `Assigned` to a paid winner on the `Claiming` invariant-violation path (States table). The client learns the item's current `expiryTick` from `GroundItemAssigned.expiryTick` and replaces the value it held from `GroundItemSpawned`.

**CR-LT-13 — Drop Fate on Bag Full (resolves OQ-INV-5)**
If `PickupRequest` returns `PickupResult.Fail` (bag full), the ground item remains on the ground in `Assigned` state with its original character assignment intact. The item does not reassign to another party member. The item remains assigned to the original character until `expiryTick`. If `expiryTick` is reached with the item still undeliverable, the item despawns — **the drop is permanently lost**. CR-LT-13.1 through CR-LT-13.3 define extensions to this base rule for mobile-specific recovery flows.

**CR-LT-13.1 — TTL Pause on App Background**
When the assigned character's client sends `ClientBackgrounded`, the server records `backgroundedAtTick` for that character. When `ClientForegrounded` is received, the server computes `pausedTicks = foregroundTick − backgroundedAtTick` and extends each assigned bag-full item's `expiryTick += min(pausedTicks, pauseBudgetRemaining)`, then decrements the budget by the same amount. Each assignment is initialized with `pauseBudgetRemaining = GROUND_ITEM_TTL_PAUSE_CAP_TICKS` (1,200 ticks / 60 seconds). Once `pauseBudgetRemaining` reaches 0, TTL advances normally even if the player backgrounds again. The adjusted `expiryTick` is used as the reference point for CR-LT-13.3 warning timing and CR-LT-12 despawn. Wire schemas for `ClientBackgrounded` and `ClientForegrounded` are in `networking-wire-protocol.md` (OQ-LT-4 resolved 2026-05-17).

**CR-LT-13.2 — Discard Modal on Proximity Fail**
On `PickupResult.Fail` while the character is within `PICKUP_RADIUS_UNITS`: the server sends `BagFullPickupBlocked(groundItemID, itemID, displayName, remainingTicks)` to the assigned character. The client renders a discard modal (see UI Requirements). While the character remains within `PICKUP_RADIUS_UNITS` of the assigned item, the server monitors for any `InventoryChangedEvent` from that character that results in at least one free slot — `InventoryChangedEvent` is a server-internal pub/sub event raised by the Inventory System on any character inventory mutation; it is not a wire message; definition authority: Inventory System GDD — on such an event, `PickupRequest` is retried automatically without requiring the player to leave and re-enter pickup radius. If the player exits `PICKUP_RADIUS_UNITS` before freeing a slot, the modal is dismissed — standard retry-on-re-entry applies per CR-LT-13 base.

**CR-LT-13.3 — Expiry Warning**
When `expiryTick − currentTick == EXPIRY_WARNING_TICKS` (600 ticks / 30 seconds), the server sends `GroundItemExpiryWarning(groundItemID, itemID, displayName, remainingTicks)` to the assigned character. This event fires once per deadline approach — if `expiryTick` is extended by CR-LT-13.1 after the initial warning fires, a fresh warning fires at the new `expiryTick − EXPIRY_WARNING_TICKS`. Suppressed if the item is in `Claiming` or terminal state when the threshold is crossed.

**CR-LT-14 — Gold Distribution**
At mob death the server draws a gold amount uniformly from `[GoldMin, GoldMax]` authored on the mob's table entry. This is distributed to the winning party: `goldPerMember = floor(baseGold / N)` where N = winning party size at kill time. Remainder is discarded. Each member receives `AddGold(characterID, goldPerMember, GoldTransactionReason.MonsterDrop)`. If no party won the tag (CR-LT-4 no-attacker fallback), no gold is distributed. Gold is computed and applied in the same server tick as item drop spawning, regardless of whether any items are picked up.

**CR-LT-15 — Server Authority**
All drop roll results, tag resolution, round-robin advancement, auction bids, bid validation, and gold distribution are computed server-side. The client receives outcome events only (`GroundItemSpawned`, `GroundItemAssigned`, `LootBidUpdate`, `AuctionResolved`, `GroundItemDespawned`, `BagFullPickupBlocked`, `GroundItemExpiryWarning`). No client has any vote in any drop outcome. All `LootBidRequest` messages are validated server-side before application.

**CR-LT-16 — Enhancement Scroll Exclusion (MVP)** *(added 2026-10-07)*
At MVP, no loot table entry may name an Enhancement Scroll — an item whose `ItemDefinition.ScrollData` is non-null (item-database.md Rule 36). While `ALLOW_ENHANCEMENT_SCROLL_DROPS` is `false` (the MVP default), startup data validation reports one issue per offending entry, naming the mob type, the entry index and the `ItemID`, and the table set is rejected like any other validation failure. An entry whose `ItemID` is not in the Item Database is not a scroll for this rule. Table validation runs after the Item Database is ready; while the knob is `false`, validating against a database that is not ready is a startup error, not a pass — a database that is not ready finds no item, so no scroll entry would be seen *(added 2026-10-07, Enhancement Story 008)*. *Why*: at MVP the scroll economy is the primary gold sink (enhancement-system.md F-ENH-5, AC-ENH-24); a dropped scroll is an attempt that removes no gold. *Post-MVP intent*: scrolls become a rare drop on some monsters. Setting the knob to `true` is gated on enhancement-system.md OQ-ENH-9: the scroll economy must be re-validated, and scrolls need a drop classification of their own — under CR-LT-5 a scroll has `GearTier.None` and would be a Common round-robin drop, not a rare one.

---

### States and Transitions

**Entity: GroundItem**

| State | Description | Entry | Exit |
|-------|-------------|-------|------|
| `Spawning` | Item created; `GroundItemSpawned` broadcast to zone clients. Lasts 1 server tick. | Mob death drop resolution produces ≥ 1 ItemID | → `Assigned` (common) or `Auctioning` (rare, party size ≥ 2) |
| `Assigned` | Common drop (or zero-bid auction fallback). Exclusively assigned to one character. `expiryTick` may be extended by CR-LT-13.1 (TTL pause on background). | CR-LT-5/6 common path, or CR-LT-10 fallback | → `Claiming` on proximity trigger; → `Despawned` on `expiryTick` |
| `Auctioning` | Rare drop. Bid window open. All bids visible to party. | CR-LT-8: Steel/DarkSteel with party size ≥ 2 | → `Claiming` at `windowCloseTick` with ≥ 1 valid bid whose bidder has a free slot; stays `Auctioning` (bids closed) while a bidder with a full bag is in a CR-LT-9.1 grace; → `Claiming` when the bidder in a grace frees a slot; → `Assigned` with 0 valid bids or when every bidder is disqualified (CR-LT-10); → immediate resolution if `expiryTick` occurs first while the bid window is open; → `Despawned` at zone teardown when no bidder can receive it |
| `Claiming` | `PickupRequest` call in flight. Awaiting `PickupResult`. | Proximity trigger (common), or auction resolution (rare) | → `Inventory` on `PickupResult.Success`; → `Assigned` on `PickupResult.Fail` (common drop). For an auction item `PickupResult.Fail` is an invariant violation — the free slot was checked on the same tick (CR-LT-9.1); if it happens the item goes to `Assigned` for the winner, with the CR-LT-12 fresh pickup window where it applies, and a server error is logged (TD-051) |
| `Inventory` | Terminal success. Item in character's inventory. Ground record destroyed. | `PickupResult.Success` | *(terminal)* |
| `Despawned` | Terminal failure. `expiryTick` reached. Item lost. | `expiryTick` in `Assigned` state (an `Auctioning` item resolves at `expiryTick` instead, and does not despawn during a CR-LT-9.1 grace); zone teardown | *(terminal)* |

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Item Database** | ← reads | `GetItemsByCategory(ItemCategory.Equipment)` → full item list cached | Server startup (once) |
| **Item Database** | ← reads | `GetItem(ItemID)` → `GearTier` (classification), `SellPriceGold` (auction floor) | At drop spawn, per-item |
| **Item Database** | ← reads | `GetItem(ItemID)` → `ScrollData` (is the entry an Enhancement Scroll — CR-LT-16) | Server startup, during table validation |
| **Inventory System** | → calls | `PickupRequest(characterID, itemID, quantity)` → `PickupResult` | On proximity trigger or auction resolution |
| **Inventory System** | ← reads | `HasFreeSlot(): bool` for the bidder | Before `TrySpendGold` on each bidder (CR-LT-9.1) |
| **Inventory System** | ← subscribes | `InventoryChangedEvent` (server-internal) | Retry on a freed slot: in pickup radius (CR-LT-13.2) and during an auction grace (CR-LT-9.1) |
| **Currency System** | → calls | `AddGold(characterID, amount, GoldTransactionReason.MonsterDrop)` | Per party member at kill resolution (gold split) and per party member at auction resolution (pool split) |
| **Currency System** | → calls | `TrySpendGold(characterID, winnerBid, GoldTransactionReason.AuctionBid)` | On the auction winner at resolution (CR-LT-9); repeated on the next bidder after any non-`Success` result |
| **Currency System** | ← reads | `GetBalance(characterID)` → `(Balance, Version)` | On a bidder whose bag is full, before a grace is granted (CR-LT-9.1) |
| **Party System** | ← reads | Party membership array (CharacterID[], join-order stable), current `rrNextIndex` | At drop spawn for round-robin assignment |
| **Party System** | ← reads | Member `Status` of a bidder (`Disconnected` / `Ghost`) | When a bidder's bag is full, before a grace is granted (CR-LT-9.1) |
| **Party System** | → calls | `IPartySystem.AdvanceRrNextIndex(partyID)` — Party System advances cursor and handles clamping | Per common drop |
| **Mob Spawning** | ← called by | Mob Spawning provides `MobTypeID` on kill event; Loot Table System resolves the drop table for that `MobTypeID` | Per kill event |
| **Networking Core** | → sends | `GroundItemSpawned`, `GroundItemAssigned`, `GroundItemDespawned` to zone clients; `LootBidUpdate`, `AuctionResolved` to party member clients; `BagFullPickupBlocked`, `GroundItemExpiryWarning` to the affected character; validates incoming `LootBidRequest` from client | Per ground item lifecycle event |
| **HUD** | ← triggers via Networking | "Bag full — item on ground" notification shown while ≥ 1 undelivered `Assigned` ground item is linked to the local player | On `PickupResult.Fail`; cleared on delivery or despawn |

---

### Drop Rate Reference

Economy calibration targets for the `DropChance` field on loot table entries. Final per-entry values are authored in the data assets; these are the calibration targets.

| GearTier | Drop Rate Target | Expected kills per specific piece (7 slots) |
|----------|-----------------|---------------------------------------------|
| Bronze | 40–60% | 12–17 |
| Iron | 15–25% | 28–47 |
| Steel | 3–6% | 117–233 |
| DarkSteel | 0.5–1.5% | 467–1,400 |
| Consumables (potions, any type — not Enhancement Scrolls at MVP, CR-LT-16) | 20–35% | — |

## Formulas

**F-LT-1: Gold per Member**

The Gold per Member formula is defined as:

`goldPerMember = floor(baseGold / N)`

where `baseGold` is drawn uniformly from `[GoldMin, GoldMax]` authored on the mob's table entry.

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Base gold draw | `baseGold` | int | [GoldMin, GoldMax] — authored per mob | Gold amount resolved at kill; uniform draw from the mob's authored range |
| Party size | `N` | int | [1, 4] | Number of members in the winning party at kill time |
| Floor division | `floor` | — | — | `Mathf.FloorToInt` — IL2CPP requirement |

**Output Range:** 0 to GoldMax (solo). At N=4 and GoldMin=2: `floor(2/4) = 0` — a valid outcome; see tuning note in Section G.

**Example:** Mob authored with GoldMin=25, GoldMax=75. Server draws baseGold=52. Party of 3: `floor(52/3) = 17g` per member. Remainder (52 − 17×3 = 1g) is discarded.

**Notes:** Gold is distributed to each party member independently via `AddGold`. `GoldMin` must be ≥ MAX_PARTY_SIZE (4) on any authored mob entry to prevent zero-gold distributions. Data validation must enforce this at startup.

---

**F-LT-2: Auction Net Cost**

The Auction Net Cost formula is defined as:

`netCost = winnerBid − floor(winnerBid / N)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Winner's bid | `winnerBid` | uint | [SellPriceGold(tier), GOLD_CAP] | The bid the auction winner submitted |
| Party size | `N` | int | [1, 4] | Party size on the paying tick — the tick the winner's `TrySpendGold` succeeds; equal to `windowCloseTick` unless a CR-LT-9.1 grace ran. An auction only opens at party size ≥ 2; `N = 1` occurs only when every other member has left before the winner pays |

**Output Range:** `winnerBid × (N-1) / N` — ranges from `winnerBid/2` (N=2) to `winnerBid × 0.75` (N=4). At `N = 1` the net cost is 0: the winner is the whole party and receives the full pool back, the same outcome as the solo rule (CR-LT-11).

**Example:** Winner bids 400g on a Steel item. Party of 4: pool split = `floor(400/4) = 100g` per member. Winner receives 100g back. Net cost: 400 − 100 = 300g. The other 3 members each gain 100g.

**Notes:** At the 90g floor bid for Steel, N=4: net cost = 90 − 22 = 68g. The auction always results in a net gold sink for the winner, with the pool redistributed. Gold sink scales with bid amount and party size.

---

**F-LT-3: Tag Threshold**

The Tag Threshold formula is defined as:

`tagThreshold = Mathf.CeilToInt(mob.MaxHP × TAG_THRESHOLD_FRACTION)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Mob max HP | `mob.MaxHP` | int | [1, 9999] | The mob's authored maximum HP |
| Tag fraction | `TAG_THRESHOLD_FRACTION` | float | [0.10, 0.50] (default: 0.33) | Fraction of MaxHP a party must deal to lock the tag |

**Output Range:** 1 to `Mathf.CeilToInt(9999 × 0.33) = 3300`.

**Example:** Mob with MaxHP=120. Tag threshold = `Mathf.CeilToInt(120 × 0.33) = 40`. The first party or solo player to accumulate 40 damage against this mob locks the drop ownership.

**Notes:** `Mathf.CeilToInt` ensures the threshold is always at least 1 and never equals `mob.MaxHP × 1.0`. At default 0.33, the first party to clear roughly the first third of the mob's HP wins — rewarding sustained early engagement over burst-finishers.

---

**F-LT-4: Expected Drops per N Kills (informational)**

For a single table entry with drop chance `p`, the expected number of drops over `N` kills is:

`E[drops] = N × p`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Drop chance | `p` | float | [0.0, 1.0] | Authored `DropChance` on a single table entry |
| Kill count | `N` | int | [1, ∞) | Number of kills against mobs with this table |

**Output Range:** [0, ∞) — unbounded expectation.

**Example:** DarkSteel Sword at p=0.01 (1%). After 700 kills: E[drops] = 7. A player has a 50% chance of seeing it within ~69 kills; 95% within ~299 kills; 99% within ~458 kills.

**Notes:** Informational only — for designer calibration, not evaluated at runtime. Use it to sanity-check authored `DropChance` values against the drop rate targets in Section C before committing to a table entry.

## Edge Cases

**If a party member disconnects during an open auction:** Their last submitted valid bid (if any) remains in the auction record and is included in resolution at `windowCloseTick`. A disconnected member can win the auction on their final bid. If they win while disconnected, the server calls `PickupRequest` against their server-side session record — the item enters their inventory and persists. Their share of the gold pool split is delivered via `AddGold` regardless of connection state. If their bag is full when their bid is tried and their party member status is `Disconnected` or `Ghost`, they get no CR-LT-9.1 grace — nobody could act on it — and are disqualified at once. A bidder who disconnects during a grace keeps it until its deadline *(added 2026-10-03)*.

**If a party member disconnects while a common drop is assigned to them and on the ground:** The item remains assigned — the assignment is fixed at spawn time and does not change on disconnect. The ground timer continues. If they reconnect and enter pickup radius before `expiryTick`, normal delivery applies. If `expiryTick` is reached while disconnected, the item despawns. The item does not reassign to another party member.

**If all party members have full inventories when drops are assigned:** Every `PickupRequest` call returns `PickupResult.Fail`. Items remain on the ground in their `Assigned` states. Ground timers run subject to CR-LT-13.1 (TTL pauses if the assigned player backgrounds). Each assigned player sees the discard modal on proximity and the bag-full HUD indicator. If any item reaches `expiryTick` undeliverable, it despawns — the drop is permanently lost.

**If a mob's MaxHP is 1 (one-shot mob):** `tagThreshold = Mathf.CeilToInt(1 × 0.33) = 1`. The first damage event against this mob from any party immediately locks the tag. This is correct behavior — the threshold never falls below 1.

**If two parties both cross the 33% tag threshold in the same server tick:** The server processes damage events sequentially within a tick. The party whose threshold-crossing damage event is processed first wins the tag. Tick-order is deterministic (events ordered by network receipt sequence within the tick).

**If the drop roll produces zero items:** All Bernoulli trials miss. No `GroundItemSpawned` messages are sent. Gold is still distributed normally (F-LT-1). This is an expected and common outcome for tables with low per-entry `DropChance` values.

**If a solo player's bag is full when a rare item auto-assigns:** CR-LT-13 applies. The item sits on the ground for `GROUND_ITEM_TTL_TICKS`. If the player discards something before `expiryTick`, delivery retries on next proximity entry. If `expiryTick` is reached, the item despawns.

**If `floor(baseGold / N)` produces 0:** Happens when `baseGold < N` (e.g., 2g draw in a party of 4). `AddGold` must not be called with `amount = 0` — callers must guard with `if (goldPerMember > 0)` before calling. Data validation at startup must flag any loot table where `GoldMin < 4` (MAX_PARTY_SIZE) as an error.

**If a `LootBidRequest` arrives after `windowCloseTick`:** Rejected by server — resolution has already begun. The client must disable the bid UI at `windowCloseTick − 1` to prevent late submissions. The server must validate bid timestamp and reject any bid with `receivedTick > windowCloseTick`.

**If a party member leaves between kill time and auction close:** The gold pool split uses party size `N` on the paying tick (CR-LT-9), not kill time — that is `windowCloseTick` unless a CR-LT-9.1 grace ran. If every other member has left by then, `N = 1` and the winner's net cost is 0 (F-LT-2). A member who leaves the party before the winner pays is not eligible to bid and receives no pool share; a bid they placed before leaving is excluded at resolution (CR-LT-9). The round-robin counter uses `currentPartySize` at each drop assignment moment (CR-LT-6).

**If a zone teardown occurs while ground items exist:** All ground items despawn immediately. For items in `Auctioning` state: the auction closes immediately and resolves with whatever valid bids exist at teardown tick (CR-LT-9; or CR-LT-10 if zero valid bids). There is no CR-LT-9.1 grace at teardown: a bidder whose bag is full is passed over at once with no gold moved and the next bid is tried; a grace already running ends the same way. If no bidder can receive the item it despawns, no gold moves, and `AuctionResolved` reports the fallback outcome *(added 2026-10-03)*. For items in `Claiming` state where `PickupRequest` success is pending: treat as successfully delivered — do not double-award on reconnect. Zone teardown must flush all pending `PickupRequest` outcomes before shutting down loot state.

**If a `LootBidRequest` carries an amount below `itemDef.SellPriceGold`:** Rejected silently server-side. No other party member is notified. The client should validate the floor locally before enabling the Submit button to reduce invalid request traffic.

## Dependencies

**Upstream dependencies — systems the Loot Table System depends on:**

| System | GDD | Dependency Type | Interface | Hard/Soft |
|--------|-----|----------------|-----------|-----------|
| **Item Database** | Approved | Reads | `GetItemsByCategory(ItemCategory.Equipment)` at startup; `GetItem(ItemID)` per-drop for `GearTier` (classification) and `SellPriceGold` (auction floor); `GetItem(ItemID)` at table validation for `ScrollData` (CR-LT-16) | **Hard** — cannot build drop pools or run auctions without item definitions |
| **Currency System** | Approved | Reads/Writes | `AddGold(characterID, amount, GoldTransactionReason.MonsterDrop)`; `TrySpendGold(characterID, winnerBid, GoldTransactionReason.AuctionBid)` (auction winner, CR-LT-9); `GetBalance(characterID)` (CR-LT-9.1) | **Hard** — cannot distribute gold without Currency System |
| **Inventory System** | Approved | Reads/Writes | `PickupRequest(characterID, itemID, quantity)` → `PickupResult`; `HasFreeSlot(): bool` and `InventoryChangedEvent` (CR-LT-9.1, CR-LT-13.2) | **Hard** — cannot deliver items without Inventory System |
| **Party System** | Approved (2026-05-17) | Reads/Calls | Party membership array (CharacterID[], join-order stable) via `IPartySystem.GetMemberAtIndex(partyID, rrNextIndex)`; cursor advance via `IPartySystem.AdvanceRrNextIndex(partyID)`; member `Status` read for a full-bag bidder (CR-LT-9.1) | **Hard** — round-robin and auction distribution require party state. Party System CR-PS-7 owns and maintains `rrNextIndex`; Loot Table System reads and advances it exclusively via the IPartySystem interface. |
| **Networking Core** | Approved | Sends via | Message dispatch for `GroundItemSpawned`, `GroundItemAssigned`, `LootBidUpdate`, `AuctionResolved`, `GroundItemDespawned`, `BagFullPickupBlocked`, `GroundItemExpiryWarning`; validates incoming `LootBidRequest` | **Hard** — ground item lifecycle events require network delivery to zone clients |

**Downstream dependents — systems that depend on Loot Table System:**

| System | GDD | Dependency Type | What they need | Hard/Soft |
|--------|-----|----------------|----------------|-----------|
| **Mob Spawning** | Approved (design/gdd/mob-spawning.md) | Kill event source + data consumer | Enemy AI raises `MobEventBus.MobDied` after `ResolveMobDrop`; Mob Spawning establishes the `(EntityID ↔ MobTypeID)` binding used at drop resolution time; `LootTableRef` is a field in `MobDefinition` | **Hard** — Mob Spawning cannot drop items without Loot Table resolution |
| **HUD** | Approved (2026-06-20, design/gdd/hud.md) | Consumes ground item state | "Bag full — item on ground" notification driven by `GroundItemAssigned` events scoped to the local player; HUD must clear the indicator on delivery or despawn | **Soft** — gameplay continues; only the notification is absent |
| **Character Persistence** | Approved (2026-05-24, design/gdd/character-persistence.md) | Indirect | Inventory System persists items received through this system; Character Persistence must support items awarded by the Loot Table System | **Indirect** — mediated by Inventory System |
| **Enhancement System** | Approved (design/gdd/enhancement-system.md) | Data constraint | The MVP scroll source restriction (AC-ENH-24): no loot table may yield an Enhancement Scroll while `ALLOW_ENHANCEMENT_SCROLL_DROPS` is `false` (CR-LT-16). No runtime call in either direction | **Soft** — an economy rule, lifted post-MVP through enhancement-system.md OQ-ENH-9 |

**Bidirectionality notes:**
- `design/gdd/item-database.md` — Loot Table System already listed as a downstream dependent ✓
- `design/gdd/enhancement-system.md` — Loot Table System listed in its dependency table (CR-LT-16, added 2026-10-07) ✓
- `design/gdd/inventory-system.md` — ✓ DONE (2026-05-17): Interactions table updated; `PickupRequest(CharacterID, ItemID, quantity)` is now the canonical signature. Lean re-review complete (2026-05-17) — re-Approved.
- `design/gdd/currency-system.md` — ✓ DONE (2026-10-03): Loot Table System is listed as a caller of `AddGold`, `TrySpendGold` and `GetBalance` in the Interactions and Downstream Dependents tables.
- `design/gdd/party-system.md` — ✓ DONE (Approved 2026-05-17): `rrNextIndex` is owned by Party System (CR-PS-7); Loot Table System reads and advances it via IPartySystem interface.

## Tuning Knobs

**Tag Locking**

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| `TAG_THRESHOLD_FRACTION` | 0.33 | [0.10, 0.50] | Too low (→0.10): almost any grazing hit locks the tag — rewards aggressive tag-racing over sustained engagement. Too high (→0.50): a passing burst party can steal the tag from a party that did most of the work. At 0.33, a party genuinely fighting the mob locks within the first third of its HP. |

**Drop Timing**

| Knob | Default | Ticks | Safe Range | What Breaks |
|------|---------|-------|-----------|-------------|
| `GROUND_ITEM_TTL_TICKS` | 2,400 | 120 seconds | [600, 7,200] | Too low (→30s): mobile players who switch apps briefly lose drops; full-bag players can't react fast enough. Too high (→360s): ground item records accumulate — 20 players clearing 5 mobs/min for 6 minutes = 600+ live ground records. |
| `AUCTION_WINDOW_TICKS` | 600 | 30 seconds | [200, 1,200] | Too low (→10s): insufficient time for mobile players to read tooltip, decide, type bid. Too high (→60s): party waits one minute per Steel drop — interrupts grind flow. |
| `AUCTION_WINNER_GRACE_TICKS` | 600 | 30 seconds | [200, 1,200] | Too low (→10s): a mobile player cannot open the discard modal, pick a slot and confirm in time — they lose an item they bid for. Too high (→60s): each full-bag bidder in turn can hold the item and the party's pool share for a minute. Worst case per auction = number of bidders × this value (CR-LT-9.1). *(added 2026-10-03)* |
| `PICKUP_RADIUS_UNITS` | 2.0 | — | [1.0, 5.0] | Too small: players must walk directly on top of the item; fiddly on touch. Too large: auto-delivery triggers from across a room; undermines the "walk to claim" player fantasy. Provisional — calibrate during first playtest with item pickup feeling. |
| `GROUND_ITEM_TTL_PAUSE_CAP_TICKS` | 1,200 | 60 seconds | [0, 3,600] | Too low (0): no mobile benefit. Too high: players hold items indefinitely by backgrounding. Governs total accumulated background pause per item assignment (CR-LT-13.1). |
| `EXPIRY_WARNING_TICKS` | 600 | 30 seconds | [100, 1,200] | Too low: insufficient reaction time. Too high: warning fires so early it loses urgency. Must be < GROUND_ITEM_TTL_TICKS. Warning fires once at adjusted expiryTick − this value (CR-LT-13.3). |

**Gold Drops**

Gold ranges are authored per mob individually. The following tier guidelines calibrate the authoring:

| Mob Tier | GoldMin (guideline) | GoldMax (guideline) | Solo gold/kill range |
|----------|--------------------|--------------------|---------------------|
| Bronze mobs | 4g | 8g | 4–8g |
| Iron mobs | 8g | 25g | 8–25g |
| Steel mobs | 25g | 75g | 25–75g |
| DarkSteel / boss mobs | 75g | 200g | 75–200g |

**Constraint**: `GoldMin ≥ 4` on every authored entry — prevents zero-gold distributions in full parties (F-LT-1). Data validation must enforce this at startup.

**Cross-reference**: `GOLD_CAP = 9,999,999g` (owned by `design/gdd/currency-system.md`). No authored `GoldMax` may exceed this value.

**Data Validation Switches**

| Knob | Default | Values | What Breaks |
|------|---------|--------|-------------|
| `ALLOW_ENHANCEMENT_SCROLL_DROPS` | `false` | `false` / `true` | `false` (MVP): any loot table entry naming an Enhancement Scroll is a validation error (CR-LT-16). `true`: scrolls may be authored as drops — every dropped scroll bypasses the gold sink, so do not enable before enhancement-system.md OQ-ENH-9 is resolved (economy re-validation and a scroll drop classification). |

**Drop Rates**

Drop rates are authored per table entry. The following calibration targets apply to each `DropChance` field:

| GearTier | DropChance Target | Expected kills per specific piece (1 of 7 slots) |
|----------|-----------------|-------------------------------------------------|
| Bronze | 0.40–0.60 | 12–17 |
| Iron | 0.15–0.25 | 28–47 |
| Steel | 0.03–0.06 | 117–233 |
| DarkSteel | 0.005–0.015 | 467–1,400 |
| Consumables (potions, per type — not Enhancement Scrolls at MVP, CR-LT-16) | 0.20–0.35 | — |

*What breaks:* Bronze too low → new players equip slowly, churn before reaching enhancement. DarkSteel too high → Legendary Gear scarcity evaporates. DarkSteel too low (→0.001) → mobile players churn before seeing first drop.

**Auction**

| Knob | Default | Notes |
|------|---------|-------|
| `AUCTION_MIN_BID_STEEL` | 90g | Equals `SellPriceGold` for Steel-tier items. Changing independently of `TierBasePrice(Steel)` in Item Database creates an inconsistency — keep synchronized. |
| `AUCTION_MIN_BID_DARKSTEEL` | 270g | Equals `SellPriceGold` for DarkSteel-tier items. Same synchronization constraint. |

*What breaks:* Minimum bid too low (→1g) enables guild pre-assignment via trivial bids, defeating the auction. Minimum bid too high (→500g for Steel) price-floors items above party members' gold reserves, causing repeated zero-bid outcomes and items defaulting to round-robin.

## Visual/Audio Requirements

**Ground Item Spawn**
When a `GroundItemSpawned` event fires, a vertical beacon rises from the item position. Beacon color is tier-differentiated: Bronze = warm amber, Iron = cool white, Steel = electric blue, DarkSteel = deep violet pulse, Consumable = bright green. Spawn is accompanied by a tier-matched impact sound: Bronze — light metallic ping; Iron — heavier clink; Steel — resonant chime; DarkSteel — low impact with reverb tail.

**Drop Assignment — Common**
When a common drop is assigned to the local player, a 2-second toast notification appears: "[Item Name] — walk to claim." Accompanied by a soft notification chime. No assignment notification is shown for drops assigned to other party members.

**Auction Open — Rare Drop**
When a rare item enters `Auctioning` state, all eligible party member clients receive the auction-open event. The client shows the auction window and plays a 1–2 second "auction fanfare" audio cue — distinct from the common drop chime.

**Pickup Success**
When `PickupRequest` succeeds: a short "item acquired" particle burst at the item position and a pickup sound (tier-matched). The item and beacon vanish at the frame of `PickupResult.Success`.

**Bag Full — Item On Ground**
On `PickupResult.Fail`: the beacon shifts to a pulsing amber/red. A short "blocked" audio ping plays once (not looping). The HUD bag-full indicator activates. The discard modal appears immediately (CR-LT-13.2).

**Discard Modal**
Triggered by `BagFullPickupBlocked`. Overlay showing: item being claimed (icon + name + tier badge), TTL countdown (real-time seconds remaining), and the player's inventory grid. Player selects any unlocked slot and taps "Discard" to free space. On confirm: item discarded, pickup retried automatically. The modal has two modes, chosen by the client from the state of the ground item named in `BagFullPickupBlocked` *(added 2026-10-03)*: **pickup mode** (item `Assigned`, CR-LT-13.2) — countdown is the item's TTL; dismissed on exit from `PICKUP_RADIUS_UNITS` or on successful delivery. **Grace mode** (item `Auctioning`, CR-LT-9.1) — countdown is the grace remaining; not dismissed on exit from `PICKUP_RADIUS_UNITS`; closed on successful delivery, on `AuctionResolved` for that item, or when the countdown reaches zero.

**Expiry Warning**
Triggered by `GroundItemExpiryWarning` (30 seconds before `expiryTick`). HUD flashes urgent red (distinct from the amber bag-full beacon). A distinct escalating audio cue plays — not the same as the blocked beacon sound; must convey "act now" urgency. The flash persists until the item is delivered, despawns, or the player taps to acknowledge.

**Despawn (TTL Expiry)**
Item fades out over 1 second. Brief red flash particle at item position. A fade-out audio note plays. If the expiring item was assigned to the local player: HUD updates to "Item lost" for 3 seconds before clearing.

**Visibility Rules**
- Common drop beacons: visible only to the assigned character — prevents visual clutter in multi-party zones.
- Rare drop (auction) beacons: visible to all current party members.
- All despawn VFX: visible to all zone clients.

**Audio Director Handoff**
This section specifies trigger events and emotional intent only. Exact asset names, mix categories, priority levels, and occlusion rules are owned by the Audio Director.

## UI Requirements

**Auction Bid Window**
Triggered by `GroundItemSpawned` with `isAuction = true` for a rare item the local player is eligible to bid on *(corrected 2026-10-03 — was "an `AuctionOpen` event", which is not a wire message)*.

Contents:
- Item name, icon, and tier badge
- Countdown timer — ticks down from `AUCTION_WINDOW_TICKS` (30 seconds) in real time; displayed as seconds remaining
- Bid history — scrollable list of all party member bids, most recent at top; shows CharacterName + BidAmount; updates in real time as `LootBidUpdate` messages arrive; fully transparent to all party members
- Bid input field — numeric; pre-fills with 1g above current highest bid; minimum bid floor displayed below field (90g for Steel, 270g for DarkSteel); Submit button disabled when input < minimum bid (client validates; server re-validates on receipt)
- Pass button — dismisses the window without bidding; does not affect server state; non-bidders remain eligible for round-robin fallback on zero bids
- Window does not pause movement or combat input

At `windowCloseTick`: all input elements disable. `AuctionResolved` determines the outcome display.

**Ground Item — Bag Full HUD Indicator**
Active while ≥1 ground item is assigned to the local player with `PickupResult.Fail` pending.
- Text: `"{N} item(s) on ground — bag full"`
- Tapping the indicator opens the discard modal for the nearest assigned bag-full item (replaces the old "opens Inventory screen" behavior — the modal surfaces the inventory inline)
- Clears when all such items are delivered or despawned

**Discard Modal (UI)**
- Triggered by `BagFullPickupBlocked` event or by tapping the bag-full HUD indicator
- Also shown to an auction bidder in a CR-LT-9.1 grace, wherever they stand *(added 2026-10-03)*. Client rule: if the ground item named in `BagFullPickupBlocked` is `Auctioning` in the client's ground item state, the modal is in grace mode — the countdown is the grace remaining (`remainingTicks`), leaving `PICKUP_RADIUS_UNITS` does not dismiss it, and it closes on delivery, on `AuctionResolved` for that item, or when the countdown reaches zero. Any other item state → pickup mode (CR-LT-13.2). No wire field distinguishes the two cases
- Contents: item icon + name + tier badge at top; TTL countdown in seconds (live); full inventory grid (20 slots) below
- Locked slots (Enhancement lock) are dimmed and non-selectable
- Selecting a slot highlights it and enables the "Discard" confirm button
- "Discard" button: large, thumb-reachable; confirms destructive action with a single tap (no second confirm — the TTL countdown creates sufficient urgency)
- "Cancel" button: dismisses modal; item remains on ground; beacon stays amber/red
- Modal renders above all other HUD layers including combat overlay
- TTL countdown updates every second; turns red when ≤10 seconds remain

**Drop Assignment Toast**
- Displays when a common drop is assigned to the local player
- Content: item icon + "[Item Name] assigned to you"
- Duration: 2 seconds; non-interactive; auto-dismisses
- If multiple drops arrive in quick succession, they queue and display sequentially (no stacking)

## Acceptance Criteria

**AC-LT-1** [BLOCKING]
GIVEN a mob loot table with three entries at `DropChance = 1.0`, `DropChance = 0.0`, and `DropChance = 1.0`,
WHEN the mob dies and the drop roll resolves,
THEN the result contains exactly the two items with `DropChance = 1.0`, all three entries were evaluated (confirmed via roll-count instrument), and no item is conditionally skipped based on any prior entry's result.

**AC-LT-2** [BLOCKING]
GIVEN a fresh server process starting with the Item Database available,
WHEN the Loot Table System initializes,
THEN `GetItemsByCategory(ItemCategory.Equipment)` is called exactly once, and a subsequent mob kill does not call it again (call count remains 1).

**AC-LT-3** [BLOCKING]
GIVEN a mob with `MaxHP = 120` and `TAG_THRESHOLD_FRACTION = 0.33`,
WHEN the server computes `tagThreshold`,
THEN `tagThreshold = 40` (`Mathf.CeilToInt(39.6) = 40`). GIVEN a mob with `MaxHP = 1`, THEN `tagThreshold = 1` — the threshold is never zero.

**AC-LT-4** [BLOCKING] [Integration — requires Party System stub and damage event pipeline]
GIVEN a mob with MaxHP=300 (tagThreshold=99), two parties attacking, Party A crossing 99 cumulative damage before Party B,
WHEN Party A's threshold-crossing damage event is processed,
THEN the tag locks to Party A within the same server tick; Party B's subsequent damage does not change ownership; drops and gold are attributed to Party A.
Fallback: if the mob is killed before any party crosses threshold, the party with the highest cumulative damage at death wins; ties break by earliest `firstDamageTick`.

**AC-LT-5** [BLOCKING]
GIVEN a mob that dies with `damageRecord` empty (trap kill — no player attacker),
WHEN the kill event resolves,
THEN no `GroundItemSpawned` messages are sent, no `AddGold` calls are made, and the resolution completes silently. Zero items and zero gold is the correct expected result, not an error.

**AC-LT-6** [BLOCKING]
GIVEN a pending drop list containing items of Bronze, Iron, None (Consumable), Steel, and DarkSteel tiers,
WHEN the server classifies each item at drop spawn,
THEN Bronze, Iron, and None items are routed to the common drop path (round-robin); Steel and DarkSteel are routed to the rare drop path (auction). Classification uses the pre-indexed cache — no new `GetItem` calls are made during this operation.

**AC-LT-7** [BLOCKING] [Integration — requires Party System stub]
GIVEN a party of 3 members [A, B, C] ordered by join time with `rrNextIndex = 0`, and 4 consecutive common drops resolve,
WHEN each drop is assigned,
THEN the assignment sequence is A, B, C, A and `rrNextIndex = 1` after the fourth drop.
GIVEN `rrNextIndex = 2` and member C leaves: `rrNextIndex = 2 % 2 = 0`, next drop assigns to A.
GIVEN a party of 2 and member D joins (appended at index 2): `rrNextIndex` unchanged; next drop still assigns to B; D is not retroactively eligible.

**AC-LT-8** [BLOCKING] [Integration — requires Inventory System stub]
GIVEN a common drop assigned to CharacterID=42,
WHEN Character 42 enters `PICKUP_RADIUS_UNITS` of the item,
THEN the server calls `PickupRequest(42, itemID, 1)` automatically with no client input required. If `PickupResult.Success`: item transitions to Inventory state.
If `PickupResult.Fail` (bag full): item remains in `Assigned` state assigned to Character 42 only — not reassigned to any other party member.

**AC-LT-9** [BLOCKING] [Integration — requires Inventory System stub]
GIVEN a bag-full drop fate where Character 42's bag was full, Character 42 frees one slot and re-enters pickup radius before `expiryTick`,
WHEN re-entry triggers,
THEN the server retries `PickupRequest(42, itemID, 1)` and delivers the item on success.
GIVEN Character 42 is already within `PICKUP_RADIUS_UNITS` when they free the slot (via discard modal),
THEN the server retries `PickupRequest(42, itemID, 1)` automatically within 1 server tick of the `InventoryChangedEvent` — no leave/re-enter required — and delivers the item on success.
If `expiryTick` is reached before Character 42 can retry: item transitions to `Despawned`, `GroundItemDespawned` is broadcast, and the drop is permanently lost.

**AC-LT-10** [BLOCKING]
GIVEN a mob with GoldMin=40/GoldMax=60, server draws baseGold=52, winning party of N=3,
WHEN gold distributes,
THEN `goldPerMember = floor(52/3) = 17`; `AddGold` is called exactly 3 times at 17g each; remainder (1g) is discarded.
GIVEN baseGold=2 and N=4: `goldPerMember = floor(2/4) = 0`; `AddGold` is NOT called with amount=0 (guard fires); authoring violation is logged.

**AC-LT-11** [BLOCKING] [Integration — requires Party System stub]
GIVEN a Steel item drop (SellPriceGold=90g) in a party of 2,
WHEN the drop resolves,
THEN the item enters `Auctioning` state. A bid of 89g is rejected silently (no `LootBidUpdate` broadcast). A bid of exactly 90g is accepted and broadcast to all party members.

**AC-LT-12** [BLOCKING] [Integration — requires Party System stub, Inventory System stub, Currency System stub]
GIVEN a DarkSteel auction in a party of 4: A bids 400g at tick 100, B bids 400g at tick 120, C bids 350g at tick 80, D bids nothing; `windowCloseTick` is reached,
THEN A wins (earlier 400g bid at tick 100 < 120); `PickupRequest(A, itemID, 1)` is called once; `goldPerMember = floor(400/4) = 100`; all 4 members receive `AddGold(characterID, 100, GoldTransactionReason.MonsterDrop)` (4 calls total).
If A's bag is full: CR-LT-9.1 applies — see AC-LT-25 *(replaces "CR-LT-13 applies to the item; gold pool split still executes", 2026-10-03)*.

**AC-LT-25** [BLOCKING] [Integration — requires Party System stub, Inventory System stub, Currency System stub] *(added 2026-10-03)*
GIVEN the DarkSteel auction of AC-LT-12, A's bag is full at `windowCloseTick`, and A is connected with a balance ≥ 400g,
THEN no `TrySpendGold` and no `AddGold` is called; A receives one `BagFullPickupBlocked` with `remainingTicks = AUCTION_WINNER_GRACE_TICKS`; the item stays `Auctioning`; no `AuctionResolved` is sent.
GIVEN A frees a slot before the grace expires: on the first server tick after A's `InventoryChangedEvent`, `TrySpendGold(A, 400, AuctionBid)` is called once, `PickupRequest(A, itemID, 1)` once, `AddGold` 4 times (100g each), and `AuctionResolved` names A.
GIVEN A does not free a slot: at `windowCloseTick + AUCTION_WINNER_GRACE_TICKS` A is disqualified with no gold moved and B (400g at tick 120) is tried on that tick under the same rule.
GIVEN A's bag is full and A's balance is 399g at `windowCloseTick`: no `BagFullPickupBlocked` is sent to A, no `TrySpendGold(A, …)` is called, and B is tried on `windowCloseTick`.
GIVEN A's bag is full and A's party member status is `Disconnected` at `windowCloseTick`: no `BagFullPickupBlocked` is sent, and B is tried on `windowCloseTick`.
GIVEN A leaves the party during the grace: on the first server tick after, A is disqualified with no gold moved and B is tried on that tick.
GIVEN A's status becomes `Disconnected` during the grace: nothing changes — A is still reserved until `windowCloseTick + AUCTION_WINNER_GRACE_TICKS`.
GIVEN a party member leaves during A's grace and A then frees a slot: `AddGold` is called 3 times with `floor(400 / 3) = 133g` each.
GIVEN every bidder's grace expires: CR-LT-10 round-robin applies, no gold moves, `AuctionResolved` reports the fallback outcome, the item's `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS`, and `GroundItemAssigned` carries that `expiryTick`.
The item does not despawn during a grace, including past its `expiryTick`.

**AC-LT-13** [BLOCKING] [Integration — requires Currency System stub]
GIVEN the same DarkSteel auction resolved in AC-LT-12 (winner A bids 400g at tick 100, party of 4, goldPerMember=100) and A has sufficient gold at `windowCloseTick`,
WHEN auction resolves,
THEN `TrySpendGold(A, 400, GoldTransactionReason.AuctionBid)` is called exactly once and returns Success; `AddGold` is called exactly 4 times (100g each). Winner's balance delta = −300g (−400g spent + 100g pool share). Each non-winner's balance delta = +100g. Sum of all 4 balance deltas = 0g (gold-neutral).
GIVEN the same auction but A's balance is 300g (below their 400g bid) at `windowCloseTick` (every `TrySpendGold` call below carries `GoldTransactionReason.AuctionBid`): `TrySpendGold(A, 400)` returns InsufficientFunds; the server re-runs resolution on B (next-highest valid bid = 400g, submitted at tick 120); `TrySpendGold(B, 400)` is called; if B has sufficient gold, B wins; `TrySpendGold(B, 400)` returns Success; resolution proceeds with B as winner. — verified via Currency System call log showing the disqualification pass then the successful pass.

**AC-LT-14** [BLOCKING]
GIVEN a Steel Blade in `Auctioning` state with zero valid bids when `windowCloseTick` is reached,
WHEN the window closes,
THEN the item reclassifies to the common drop path, is assigned via round-robin at that tick, no `AddGold` is called, and `AuctionResolved` is broadcast with a zero-bid outcome.

**AC-LT-15** [BLOCKING]
GIVEN a solo player (party size=1) kills a mob that drops a DarkSteel Greaves (`GearTier.DarkSteel`),
WHEN the drop resolves,
THEN no auction is opened; the item is assigned directly to the solo player via the common drop path; `PickupRequest(soloPlayerID, itemID, 1)` is called on proximity. No bid UI, no auction window.

**AC-LT-16** [BLOCKING]
GIVEN a common drop in `Assigned` state with no pickup occurring, `expiryTick` reached,
THEN the item transitions to `Despawned`; `GroundItemDespawned` is broadcast; no `PickupRequest` is attempted; the drop is permanently lost.
GIVEN a Steel Blade in `Auctioning` state where `expiryTick` is reached before `windowCloseTick`:
THEN the auction closes immediately at `expiryTick`; if ≥1 valid bid exists → CR-LT-9 resolves normally; if zero valid bids → CR-LT-10 round-robin applies. The item does not despawn silently while valid bids exist.

**AC-LT-17** [BLOCKING]
GIVEN an auction where `windowCloseTick` has been reached and resolution has begun,
WHEN the server receives a `LootBidRequest` with `receivedTick > windowCloseTick`,
THEN the bid is rejected; no auction state changes; no `LootBidUpdate` is broadcast; the in-progress resolution continues uninterrupted; the rejection is logged for diagnostics.

**AC-LT-18** [BLOCKING]
GIVEN a party of 4 with all members at full inventory (20/20 slots), a mob drops Item X assigned to A via round-robin and Item Y assigned to B,
WHEN both proximity triggers fire and both `PickupRequest` calls return `PickupResult.Fail`,
THEN Item X remains exclusively assigned to A (not to B, C, or D); Item Y remains exclusively assigned to B; both items retain their original `expiryTick`; a bag-full HUD notification is active on A's client for X and on B's client for Y.

**AC-LT-19** [ADVISORY] [Integration — requires zone lifecycle management]
GIVEN a DarkSteel item in `Auctioning` state with 3 valid bids when a zone teardown signal is received,
WHEN the zone begins shutdown,
THEN the auction closes immediately at the teardown tick; CR-LT-9 resolves (highest bidder wins); `PickupRequest` and all `AddGold` calls complete before zone state is destroyed. Zone teardown does not finalize until all pending loot resolution calls have returned or timed out.
GIVEN zero valid bids at teardown: CR-LT-10 round-robin applies before teardown.
GIVEN the highest bidder's bag is full at teardown: that bidder is passed over with no `TrySpendGold`; the next bidder with a free slot is charged and wins. GIVEN no bidder has a free slot: no gold moves, the item despawns, and `AuctionResolved` reports the fallback outcome *(added 2026-10-03)*.

**AC-LT-20** [BLOCKING]
GIVEN a mob with GoldMin=10/GoldMax=10 killed by a solo player (N=1),
WHEN the kill resolves,
THEN `AddGold(soloPlayerID, 10, GoldTransactionReason.MonsterDrop)` is called exactly once, in the same server tick as any `GroundItemSpawned` events. Solo player receives the full 10g.
GIVEN the same mob killed with empty `damageRecord` (no-attacker): `AddGold` is not called for any character. Zero gold distributed.

**AC-LT-21** [ADVISORY] [Integration — requires background detection]
GIVEN a ground item assigned to Character 42 with `GROUND_ITEM_TTL_TICKS = 2400` (120s remaining) and `pauseBudgetRemaining = GROUND_ITEM_TTL_PAUSE_CAP_TICKS = 1200`,
WHEN Character 42's client sends `ClientBackgrounded` at tick T and `ClientForegrounded` at tick T+500 (25 seconds backgrounded),
THEN `expiryTick` is extended by 500 ticks and `pauseBudgetRemaining = 700` — verified via server ground item state log showing adjusted `expiryTick`.
GIVEN Character 42 subsequently backgrounds again for 800 ticks: only 700 additional ticks are applied (budget cap); `pauseBudgetRemaining = 0` — verified via server log showing `expiryTick` extended by exactly 700, not 800.

**AC-LT-22** [ADVISORY] [Integration — requires Inventory System stub]
GIVEN a common drop assigned to Character 42 with a full bag (20/20 slots occupied),
WHEN Character 42 enters `PICKUP_RADIUS_UNITS` of the ground item,
THEN `PickupResult.Fail` fires and the server sends `BagFullPickupBlocked` to Character 42 within 1 server tick — verified via server outbound message log showing `BagFullPickupBlocked` with the correct `groundItemID`, `itemID`, `displayName`, and `remainingTicks`.

**AC-LT-23** [ADVISORY] [Integration — requires Inventory System stub]
GIVEN Character 42 has received `BagFullPickupBlocked` and remains within `PICKUP_RADIUS_UNITS`,
WHEN Character 42 discards one item (any slot), triggering `InventoryChangedEvent` with at least one free slot,
THEN the server fires `PickupRequest(42, itemID, 1)` within 1 server tick of the `InventoryChangedEvent` — no movement required — and the item is delivered on success. Verified via server event log showing `InventoryChangedEvent` → `PickupRequest` → `PickupResult.Success` in the same or next tick.

**AC-LT-24** [ADVISORY]
GIVEN a ground item assigned to Character 42 with `expiryTick` exactly 601 ticks in the future,
WHEN the server advances 1 tick (601 → 600 ticks remaining, equal to `EXPIRY_WARNING_TICKS`),
THEN `GroundItemExpiryWarning` is sent to Character 42 exactly once — verified via server outbound log showing exactly one `GroundItemExpiryWarning` for that `groundItemID`.
GIVEN the same item has `expiryTick` extended by CR-LT-13.1 after the initial warning fires: a second `GroundItemExpiryWarning` IS sent at the new `expiryTick − EXPIRY_WARNING_TICKS` tick — verified via server outbound log showing exactly two `GroundItemExpiryWarning` messages for that `groundItemID`, one at each deadline threshold crossing.

**AC-LT-26** [BLOCKING] [Logic] *(added 2026-10-07 — CR-LT-16; renumbered from a duplicate AC-LT-25 the same day)*
GIVEN a table set in which one mob's table has an entry naming the Bronze Enhancement Scroll (an item with `ScrollData`) and `ALLOW_ENHANCEMENT_SCROLL_DROPS = false`,
WHEN the set is validated at startup,
THEN exactly one issue is reported for that entry, naming the mob type, the entry index and the `ItemID`, and no registry is created.
GIVEN a set with two scroll entries (in one table, or one in each of two tables): exactly two issues are reported by this rule, one per entry.
GIVEN the same set with `ALLOW_ENHANCEMENT_SCROLL_DROPS = true`: no issue is reported by this rule and the registry is created.
GIVEN a set whose entries name only items without `ScrollData`, or an `ItemID` that is not in the Item Database: no issue is reported by this rule.

**BLOCKING: 21 | ADVISORY: 5 | Total: 26**

*QA note: AC-LT-8, AC-LT-9 require `PICKUP_RADIUS_UNITS` to be authored before integration tests can be implemented. AC-LT-4, AC-LT-7, AC-LT-11, AC-LT-12, AC-LT-13, AC-LT-19, AC-LT-25 require a Party System stub or interface contract — the Party System GDD should define at minimum `rrNextIndex`, the membership array shape, and `PartyID` type before these integration tests are written.*

## Open Questions

~~**OQ-LT-1 — `PICKUP_RADIUS_UNITS`: value TBD**~~ **RESOLVED (2026-05-17)**
Provisional value: `2.0` units. Registered in `design/registry/entities.yaml`. AC-LT-8 and AC-LT-9 can now be written as integration tests. Value must be re-evaluated during first playtest session — 2.0 is a midpoint estimate, not playtested.

~~**OQ-LT-2 — Party System interface contract: not yet designed**~~ **RESOLVED (2026-05-17)**
Party System GDD authored and approved (2026-05-17). `rrNextIndex` is now owned by the Party System (CR-PS-7); Loot Table System advances it via `IPartySystem.AdvanceRrNextIndex(PartyID)`. Membership array: `CharacterID[]` join-order stable, 4-slot max (`MAX_PARTY_SIZE = 4`). `PartyID` type defined in Party System GDD CR-PS-1. AC-LT-4, AC-LT-7, AC-LT-11, AC-LT-12, AC-LT-13, AC-LT-19 can now be written as integration tests using the Party System stub.

~~**OQ-LT-3 — Interface name conflict with Inventory System**~~ **RESOLVED (2026-05-17)**
Canonical interface: `PickupRequest(CharacterID, ItemID, quantity)` → `PickupResult(success/fail)`. Both GDDs updated: this GDD now uses `PickupRequest(characterID, itemID, quantity)` throughout; `inventory-system.md` Interactions table updated to include `CharacterID` parameter and `quantity=1` note for MVP loot drops. `MoveItemIn` in `inventory-system.md` remains the Equipment System's unequip-to-bag interface — distinct method, distinct caller. `inventory-system.md` requires a lean re-review pass since it is Approved and its interface signature was modified.

~~**OQ-LT-4 — New wire messages from bag-full redesign (CR-LT-13.1–13.3)**~~ **RESOLVED (2026-05-17)**
Wire schemas for all 10 loot messages added to `networking-wire-protocol.md`: `GroundItemSpawned`, `GroundItemAssigned`, `GroundItemDespawned`, `LootBidUpdate` (R-U batch), `AuctionResolved`, `LootBidRequest`, `BagFullPickupBlocked`, `GroundItemExpiryWarning`, `ClientBackgrounded`, `ClientForegrounded`. `GroundItemID` wire type registered in CR-NET-7.9. AC-LT-22, AC-LT-23, AC-LT-24 can now be validated.
