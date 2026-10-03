# Epic: Loot Table System

> **Layer**: Core
> **GDD**: design/gdd/loot-table-system.md
> **Architecture Module**: Loot Table
> **Status**: In Progress (11/12 — Stories 001–010 Complete 2026-10-02, Story 011 Complete 2026-10-03)
> **Stories**: 12 stories created (001–012) on 2026-10-02 — 11 Complete, 1 Ready (012 — its dependency 011 is Complete)

## Overview

The Loot Table System is the server-authoritative probability engine that determines what items and gold each mob drops on death. It owns a static data layer — one loot table definition per mob type — specifying drop pools, per-entry drop probabilities, gold ranges, and party bonus rules. At runtime the server queries the system exactly once per kill event, draws from the mob's table using a server-seeded RNG, and resolves the outcome: zero or more item pickups plus a gold award, delivered to the killing character's Inventory System (`PickupRequest`) and Currency System (`GoldTransactionReason.MonsterDrop`) respectively. The system also owns **drop fate** — what happens to an item when a pickup fails because inventory is full.

**Depends on**: Item Database (Complete, MVP epic), Inventory System (this Core-layer batch, sibling epic), Currency System (Complete, MVP epic). Party-tag mechanics (CR-LT-3/4) reference `PartyID` — usable now with a solo-player-as-party-of-1 fallback (already specified), full multi-member party behavior is a forward dependency on the not-yet-created Party System epic.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| *(none)* | Pure server-side probability/data engine, no engine API surface — by-design no ADR (architecture.md Engine Knowledge Gap Summary: 🟢 LOW) | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-loot-001 | Independent per-entry Bernoulli rolls at mob death; single process-level `System.Random` seeded once at startup from system entropy, seed logged for auditability; tests must inject a seeded instance via DI (CR-LT-1) | ❌ No ADR (design-only, LOW risk) |
| TR-loot-002 | Equipment definitions cached once at server startup via `IItemDatabase.GetItemsByCategory`; loot table data is static, immutable at runtime (CR-LT-2) | ❌ No ADR (design-only, LOW risk) |
| TR-loot-003 | Party tag: damage accumulated per-party in `damageRecord`; solo players treated as a party of size 1 with a unique `PartyID` (CR-LT-3) | ❌ No ADR (design-only, LOW risk) |
| TR-loot-004 | Party tag locks to the first party crossing `Mathf.CeilToInt(mob.MaxHP × TAG_THRESHOLD_FRACTION)` (default 0.33); fallback to highest cumulative damage at death, ties break by earliest first-damage tick; empty `damageRecord` at death = no drops (CR-LT-4) | ❌ No ADR (design-only, LOW risk) |
| TR-loot-005 | Drop tier classification: Bronze/Iron/None (Consumables) = Common drop via round-robin (CR-LT-6); Steel/DarkSteel = Rare drop via gold bid auction (CR-LT-8) (CR-LT-5) | ❌ No ADR (design-only, LOW risk) |
| TR-loot-006 | Kill resolution entry point `ResolveMobDrop(EntityID, tierShift)` (enemy-ai.md); gold drawn uniformly from `[GoldMin, GoldMax]`, split `floor(baseGold / N)` per winning-party member via `AddGold(…, MonsterDrop)`, no call when the share is 0; no attacker = no drops, no gold (CR-LT-14, F-LT-1) *(added 2026-10-02 at story creation)* | ❌ No ADR (design-only, LOW risk) |
| TR-loot-007 | Ground item lifecycle: `Spawning → Assigned / Auctioning → Claiming → Inventory / Despawned`; `expiryTick = spawnTick + GROUND_ITEM_TTL_TICKS`; outcome events only, server-authoritative (CR-LT-12, CR-LT-15, States) *(added 2026-10-02)* | ADR-010 (events) |
| TR-loot-008 | Common drops assigned by the Party System's round-robin cursor — read via `IPartyService`, advanced via `AdvanceRrNextIndex`; a solo player's rare drop takes the common path (CR-LT-6, CR-LT-11) *(added 2026-10-02)* | ❌ No ADR (design-only, LOW risk) |
| TR-loot-009 | Proximity auto-pickup for the assigned character; bag-full drop fate: item stays assigned, retry on re-entry or in-radius after a slot frees, blocked notice, expiry warning, TTL pause on app background with a per-assignment budget (CR-LT-7, CR-LT-13, CR-LT-13.1–13.3) *(added 2026-10-02)* | ADR-010 (events) |
| TR-loot-010 | Rare drop gold-bid auction for parties of 2+: 600-tick window, floor = `SellPriceGold`, transparent bids, resolution by highest bid then earliest tick, `TrySpendGold` with disqualification, pool split `floor(bid / N)`, zero-bid fallback to round-robin (CR-LT-8, CR-LT-9, CR-LT-10, F-LT-2) *(added 2026-10-02)* | ADR-010 (events) |
| TR-loot-011 | Zone teardown: open auctions resolve immediately, remaining ground items despawn, no double award (Edge Cases) *(added 2026-10-02)* | ADR-010 (zone-scoped disposal) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/loot-table-system.md` are verified
- Logic stories (roll architecture, tier classification, drop fate) have passing test files in `tests/EditMode/LootTableSystem/`
- Party-tag stories that require real multi-member party behavior (beyond the solo-as-party-of-1 fallback) are scoped as forward-dependency placeholders until the Party System epic exists, matching this project's established pattern (e.g. Networking Core's `PartyDisbandCoordinator` mock-provider precedent)

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Loot Table Definitions and Startup Validation | Logic | Complete | None (design-only) |
| 002 | Drop Roll, Equipment Cache and Tier Classification | Logic | Complete | None (design-only) |
| 003 | Party Tag — Damage Record, Threshold Lock, Fallback | Integration | Complete | None (design-only) |
| 004 | Kill Resolution and Gold Distribution | Integration | Complete | None (design-only) |
| 005 | Ground Item Lifecycle and TTL Despawn | Logic | Complete | ADR-010 |
| 006 | Common Drop Round-Robin Assignment | Integration | Complete | None (design-only) |
| 007 | Proximity Pickup and Bag-Full Drop Fate | Integration | Complete | None (design-only) |
| 008 | Bag-Full Recovery — Blocked Notice, In-Radius Retry, Expiry Warning | Integration | Complete | ADR-010 |
| 009 | TTL Pause on App Background | Integration | Complete | ADR-010 |
| 010 | Rare Drop Auction — Open, Bid Validation, Broadcast | Integration | Complete | ADR-010 |
| 011 | Auction Resolution and Gold Pool | Integration | Complete | ADR-010 |
| 012 | Zone Teardown Loot Flush | Integration | Ready | ADR-010 |

Work through stories in order — each story's `Depends on:` field tells you what must be Done before you can start it.

**Open items recorded at story creation (2026-10-02):**
- **Story 011 is Blocked**: `TrySpendGold` needs a `GoldTransactionReason` and none exists for an auction debit. Amend currency-system.md (plus the wire enum and entities.yaml) to add one. **Design done 2026-10-03** — `GoldTransactionReason.AuctionBid = 9`; the story stays Blocked until the value is added in code (`GoldTransactionReason.cs`, `WireEnumCodec` range, one test case). **Unblocked 2026-10-03** — the value, the codec range and the test case are in; story 011 is Ready (the new test case has not yet been run in the Unity Test Runner).
- **F-LT-3 arithmetic**: `Mathf.CeilToInt(300 × 0.33f)` is 100 in single precision, but AC-LT-4 expects 99. Story 003 computes the threshold in exact integer arithmetic from a per-mille constant (`TAG_THRESHOLD_PERMILLE = 330`); the GDD formula wording and the registry's float `TAG_THRESHOLD_FRACTION` entry need a fix.
- **`tierShift`**: `ResolveMobDrop` accepts it and does not apply it (enemy-ai.md OQ-AI-1 — the loot GDD has no tier-shift rule).
- **Round-robin cursor**: party-system.md exposes no getter for `rrNextIndex`; Story 006 declares one on its consumer-side `IPartyService`.
- **CR-LT-13.1**: undefined what happens when `expiryTick` passes while the client is still backgrounded (Story 009).
- **Not covered by stories 001–012**: loot UI and VFX (no ACs, no UX spec), per-mob loot table assets (no mob roster), wire codecs for the 10 loot messages.
- Party System, Enemy AI / mob data, character positions and zone lifecycle have no code; the stories declare narrow consumer-side interfaces (`IPartyService`, `IMobInfoProvider`, `ICharacterPositionProvider`) and test against stubs.
