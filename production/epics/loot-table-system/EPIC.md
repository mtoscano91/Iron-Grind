# Epic: Loot Table System

> **Layer**: Core
> **GDD**: design/gdd/loot-table-system.md
> **Architecture Module**: Loot Table
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories loot-table-system`

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

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/loot-table-system.md` are verified
- Logic stories (roll architecture, tier classification, drop fate) have passing test files in `tests/EditMode/LootTableSystem/`
- Party-tag stories that require real multi-member party behavior (beyond the solo-as-party-of-1 fallback) are scoped as forward-dependency placeholders until the Party System epic exists, matching this project's established pattern (e.g. Networking Core's `PartyDisbandCoordinator` mock-provider precedent)

## Next Step

Run `/create-stories loot-table-system` to break this epic into implementable stories.
