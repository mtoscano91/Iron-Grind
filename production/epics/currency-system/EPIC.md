# Epic: Currency System

> **Layer**: Foundation
> **GDD**: design/gdd/currency-system.md
> **Architecture Module**: Currency
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories currency-system`

## Overview

The Currency System is the gold economy layer for Project Iron Grind. It maintains a single currency — Gold (g) — as a server-side unsigned integer balance per character, with `TrySpendGold` and `AddGold` as its only write operations. All spending decisions live in downstream systems (NPC Shop, Enhancement System) that call into this module's API. The system guarantees no overdraft (balance can never go below 0), all mutations are server-authoritative, and every mutation is tagged with a `GoldTransactionReason` for auditability. The client holds a display-only copy received via state sync. Persistence lives in `character_records.gold_balance` (with `gold_version` for optimistic concurrency), owned by Character Persistence (ADR-006). Purchase transaction integrity — including `CompensatingRefund` on reconnect — is enforced by the purchase pipeline in ADR-001.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-001: Purchase Integrity | `GoldTransactionReason` enum (including `CompensatingRefund = 8`); `TrySpendGold` called only after `PendingPurchase` record is durable; reconnect reconciliation calls `AddGold(CompensatingRefund)` | LOW |
| ADR-006: Persistence Layer | `gold_balance` + `gold_version` stored in `character_records`; `gold_version` provides optimistic concurrency; write budget ≤50ms P95 | MEDIUM (Npgsql/Dapper IL2CPP) |
| ADR-010: Event/Messaging Architecture | Gold balance change notification (if any) uses C# `event Action<T>` with struct args — no `UnityEvent`, no central bus | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-currency-001 | `TrySpendGold(amount)` — server-side, atomic; returns false without modifying balance if `gold_balance < amount` (no overdraft) | ❌ No ADR (design-only, LOW risk) |
| TR-currency-002 | `AddGold(amount, GoldTransactionReason)` — logs reason enum on every call; `CompensatingRefund` used for reconnect refunds and failure rollbacks | ADR-001 ✅ |
| TR-currency-003 | `gold_version` provides optimistic concurrency for concurrent save conflicts; `rows-affected = 0` → `ConcurrencyConflict` result | ADR-006 ✅ |
| TR-currency-004 | Client holds read-only display copy of gold balance, received via state sync — client never writes gold balance | ❌ No ADR (design-only, LOW risk) |
| TR-currency-005 | Gold balance persisted in `character_records.gold_balance`; persistence write budget ≤50ms P95 under normal load | ADR-006 ✅ |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria in `design/gdd/currency-system.md` are verified
- Logic stories (`TrySpendGold` no-overdraft guarantee, `AddGold` reason logging, concurrency conflict path) have passing test files in `tests/EditMode/Currency/`
- Integration test covers full spend → persist → reload round-trip

## Next Step

Run `/create-stories currency-system` to break this epic into implementable stories.
