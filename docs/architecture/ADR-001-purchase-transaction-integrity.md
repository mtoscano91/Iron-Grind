# ADR-001: Purchase Transaction Integrity

> **Status**: Accepted (2026-06-07); Amended A1 (2026-06-09 — dedup key extended with messageType discriminator; see Amendment A1); Amended A2 (2026-10-26 — gold authority during a purchase: in-memory Currency first, the store writes the resulting balance; see Amendment A2)
> **Date**: 2026-06-07
> **Deciders**: Technical Director, Lead Programmer
> **Affected systems**: NPC Shop, Currency System, Inventory System, Character Persistence, Networking Session, Consumable Use System

---

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Server / Transaction Integrity (no Unity engine APIs involved) |
| **Knowledge Risk** | LOW — idempotency key and `PendingPurchase` record pattern are pure C# server application logic; no Unity client or server engine APIs are used |
| **References Consulted** | None — this ADR governs server-side transaction semantics, independent of engine version |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | `PendingPurchase` storage correctness (`BeginPurchase` / `CompletePurchase` / `RefundPurchase` lifecycle) is verified at the persistence integration test milestone per ADR-006 Validation Criteria — not at the Unity engine level |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None (standalone — written before ADR-006 Persistence Layer and ADR-007 Hosting Backend) |
| **Enables** | ADR-006 (Persistence Layer): OQ-ADR1-1 resolved by ADR-006 Decision 3 — `PendingPurchase` stored in a dedicated `pending_purchases` table in the same PostgreSQL DB; INSERT shares one `NpgsqlTransaction` with the `TrySpendGold` UPDATE |
| **Blocks** | NPC Shop implementation; Consumable Use System implementation — both require `BeginPurchase` on an Accepted persistence backend (ADR-006) |
| **Ordering Note** | ADR-006 must be Accepted before NPC Shop or Consumable Use implementation epics begin. OQ-ADR1-1 closed by ADR-006 Decision 3. OQ-ADR1-2 (`SellRequest` atomicity) remains open — deferred to a follow-up amendment. |

## Context

The NPC Shop (CR-SHOP-5) performs gold debits and item grants as two sequential, unlinked server calls:

1. `TrySpendGold(charId, totalCost, reason)` — gold debited from Currency System
2. `PickupRequest(charId, itemId, quantity)` — item granted by Inventory System

No mechanism ensures these two operations either both complete or both roll back. Three failure scenarios cause permanent gold loss with no player recourse:

- **Server crash** between step 1 and step 2: gold debited, item never granted, no record of the outstanding debit survives the restart.
- **SESSION_TTL expiry or zone change** during in-flight transaction: npc-shop.md states "refund obligation survives session closure" but no persistence mechanism exists to honor this.
- **Client retransmit on timeout**: `BuyRequest` has no idempotency key — a retransmitted message is indistinguishable from a new request, causing a double debit.

In a live-service game with purchased currency, this bug class is the single highest-severity reputation and chargeback risk.

---

## Decision

### 1. Idempotency Key on All Mutating Shop Messages

`BuyRequest` and `SellRequest` both carry a `requestId: uint` field. The client generates this value as a monotonically incrementing counter per character session (reset to 0 on new `OpenNPCInteraction`).

The server deduplicates on `(charId, messageType, requestId)` before processing, where `messageType` is the `ushort` wire-protocol message type identifier from the envelope header. *(Updated by Amendment A1 — 2026-06-09; original key was `(charId, requestId)`.)* If a duplicate arrives:
- Return the cached `BuyResult`, `SellResult`, or `UseItemResult` without reprocessing.
- Do not re-debit gold, re-grant items, or re-consume the item.

Deduplication window: `SESSION_TTL_SECONDS` (300s). Once the wall-clock session cap expires, the `requestId` namespace is reset and old entries may be discarded.

This must be reflected in `networking-wire-protocol.md` — add `requestId: uint` to both `BuyRequest` and `SellRequest` message schemas.

### 2. PendingPurchase Record

Before calling `TrySpendGold`, the server creates a `PendingPurchase` record in durable storage (persisted via Character Persistence alongside the character state):

```
PendingPurchase {
    charId:      CharacterID
    requestId:   uint
    itemId:      ItemID
    quantity:    int
    totalCost:   uint
    state:       PendingPurchaseState   // GoldDebited | Refunded | Completed
    createdAt:   DateTime
}
```

**Transaction lifecycle:**

| Step | Action | Record state |
|------|--------|-------------|
| 0 | Record created | `GoldDebited` |
| 1 | `TrySpendGold` called | `GoldDebited` |
| 2a (success) | `PickupRequest` succeeds | → `Completed` → deleted |
| 2b (failure) | `PickupRequest` fails → `AddGold(CompensatingRefund)` | → `Refunded` → deleted |
| Crash at step 1 | Server restarts; record survives | `GoldDebited` — reconciled on reconnect |

The record is written before `TrySpendGold` so that any crash after the write but before the call is recoverable. Writing after the call creates a window where the debit exists but no record does.

### 3. Compensating Refund

On `PickupRequest` failure (CR-SHOP-5 step 9e):
1. Call `AddGold(charId, totalCost, CompensatingRefund)` immediately.
2. Update `PendingPurchase.state = Refunded` and delete the record.
3. Return `RejectedInventoryFull`.

`CompensatingRefund` is a dedicated `GoldTransactionReason` enum value (value=8, registered in entities.yaml 2026-06-07). It is distinguishable from `AdminAdjust` and `Other` in audit logs.

If `AddGold` itself fails (e.g., `ConcurrencyConflict` after two retries): log a critical alert with `charId`, `totalCost`, and the error code. Do not delete the `PendingPurchase` record — leave it for manual reconciliation. Return `RejectedInventoryFull` to the client.

### 4. Reconnect Reconciliation

This step executes after `SessionHandshake` is accepted but before any gameplay messages (including `BuyRequest`) are processed for the new session.

**Reconciliation algorithm:**
1. Query all `PendingPurchase` records for `charId` with `state = GoldDebited`.
2. For each record: call `AddGold(charId, totalCost, CompensatingRefund)`.
3. Update record to `Refunded` and delete.
4. Log each reconciliation event: `charId`, `itemId`, `quantity`, `totalCost`, `requestId`.

**Why refund rather than retry?** Retrying `PickupRequest` on reconnect is unsafe: inventory state may have changed (slots occupied by items acquired between sessions), and the item may have been granted by a delayed network message before the crash. A refund is always safe; the player retries the purchase voluntarily if they still want the item.

This must be added as a step to `networking-session.md` in the SessionHandshake accepted sequence.

### 5. Rate Limits

`BuyRequest` and `SellRequest` require server-side rate limiting consistent with other project RPCs (`AllocateFreePoint`, `NotifySkillUsed`):

- Maximum: 10 requests per second per character
- Violation response: `RejectedRateLimited` (no debit, no grant)
- Rate limit window: sliding 1-second window per `charId`

---

## Consequences

### Systems that must be updated before NPC Shop implementation

| System | Change required |
|--------|----------------|
| `networking-wire-protocol.md` | Add `requestId: uint` to `BuyRequest` and `SellRequest` schemas |
| `networking-channel-contract.md` | Register all 17 NPC Shop messages with routing, criticality, and channel assignments (OQ-NS-7) |
| `networking-message-criticality.md` | Classify all 17 NPC Shop messages |
| `character-persistence.md` | Define `PendingPurchase` record storage format and save/load lifecycle |
| `networking-session.md` | Add reconciliation step to SessionHandshake accepted sequence |

### Performance

- Each `BuyRequest` adds one persistence write (record creation) before the gold debit. Shop transactions are infrequent (1–3 per farming session per player) — this overhead is acceptable.
- Reconnect reconciliation is O(n) over `PendingPurchase` records for the character. In the normal case n=0. In the crash case n=1 (the one in-flight purchase). n>1 indicates a systemic issue and should trigger an alert.

### Complexity

Modest increase. The `PendingPurchase` record adds one additional persisted state per in-flight transaction. However, it eliminates the highest-severity live-service bug class: permanent gold loss from server infrastructure failures.

---

## Alternatives Considered

### Alternative 1: Batched GoldSyncEvent delivery (rejected)

The NPC Shop GDD (prior draft) attempted to solve client-side display consistency by batching the debit and refund `GoldSyncEvent` messages into a single wire delivery. This was rejected:
- The Currency System emits events independently with no "defer until next" API.
- The R-U channel overflow policy (CR-NET-7.7) can silently drop messages — the refund event could be dropped while the debit event was already delivered.
- Even if delivery were reliable, batching addresses the client *display* symptom (a flash), not the actual server-side gold loss problem. This was solving the wrong problem at the wrong layer.

### Alternative 2: Full two-phase commit (rejected)

A true 2PC protocol across Currency System and Inventory System with a transaction coordinator. Rejected as over-engineering for MVP: the `PendingPurchase` reconciliation pattern achieves the required safety guarantee with O(1) storage per in-flight transaction and no cross-service coordination complexity.

---

## Open Questions

**OQ-ADR1-1 — Storage backend for PendingPurchase**: Should `PendingPurchase` records be stored in the same persistence layer as character state (Character Persistence), or in a dedicated transaction log? For MVP, co-location with character state is simpler. For production scale, a dedicated transaction log table (with its own flush cadence) may be preferable. Defer to Character Persistence GDD author.

**OQ-ADR1-2 — Sell-back atomicity**: This ADR covers `BuyRequest` (debit → grant). `SellRequest` also has a failure window: `SellItem` (item removed) before `AddGold` (gold credited). Should a `PendingSell` record follow the same pattern? Recommended yes — use the same mechanism for symmetry. Deferring full spec to a follow-up amendment of this ADR.

---

## Amendment A1 — 2026-06-09: Dedup Key Extended with messageType Discriminator

**Triggered by:** OQ-CUS-3 (`design/gdd/consumable-use-system.md`)
**Amends:** Decision 1 — Idempotency Key on All Mutating Messages

### Problem

The original dedup key `(charId, requestId)` is shared across all message types. Two systems — NPC Shop (`BuyRequest`, `SellRequest`) and Consumable Use System (`UseItemRequest`) — independently generate per-session monotonically increasing `requestId` counters that each reset at different events:

- NPC Shop counter resets to 0 on each `OpenNPCInteraction`
- CUS counter resets to 0 on zone entry (including same-zone reconnect)

Within `SESSION_TTL_SECONDS` (300s), this sequence is possible:

1. `UseItemRequest { requestId=1 }` processed → cached as `(charId, 1)`
2. Player opens NPC shop → BuyRequest counter resets to 0
3. `BuyRequest { requestId=1 }` arrives → server matches cache key `(charId, 1)` → returns cached `UseItemResult`

The client receives HP/inventory data in response to a purchase request. The gold debit still executes on the server; the client has no purchase confirmation and incorrect resource values.

### Decision

Change the dedup key in Decision 1 from:

```
(charId, requestId)
```

to:

```
(charId, messageType, requestId)
```

`messageType` is the `ushort` wire-protocol envelope identifier present in every message. No wire format changes are required — the field is already in the envelope before it reaches the server's dedup layer.

**Scope rule:** All R-OD messages carrying a `requestId` field must use `(charId, messageType, requestId)` as the dedup key. Per-subsystem dedup stores are an equivalent implementation (separate stores implicitly namespace by message type) — both satisfy the cross-type collision invariant.

**Note on BuyRequest + SellRequest counter sharing:** Both counters reset on `OpenNPCInteraction` and increment together within a single NPC interaction session, ensuring monotonically unique requestIds within that session. With messageType discrimination, `BuyRequest(requestId=1)` and `SellRequest(requestId=1)` use distinct cache entries — correct, since they are different operations.

### Alternative Considered and Rejected

Per-subsystem dedup stores (separate cache per message type). Rejected: functionally equivalent but higher implementation overhead. A shared 3-tuple store is easier to audit, extend to new R-OD message types, and validate in tests.

### Affected Messages

| Message | Old key | New key |
|---------|---------|---------|
| `BuyRequest` | `(charId, requestId)` | `(charId, BuyRequest_typeId, requestId)` |
| `SellRequest` | `(charId, requestId)` | `(charId, SellRequest_typeId, requestId)` |
| `UseItemRequest` | `(charId, requestId)` | `(charId, UseItemRequest_typeId, requestId)` |

Wire-protocol type IDs are authoritative in `networking-wire-protocol.md`. Do not hardcode numeric IDs in the dedup layer — resolve `messageType` from the envelope field directly.

### Downstream Updates Applied

| Document | Change |
|----------|--------|
| `networking-wire-protocol.md` | `BuyRequest`, `UseItemRequest` dedup comment updated to `(SenderEntityID, messageType, requestId)` |
| `networking-channel-contract.md` | `BuyRequest`, `UseItemRequest` channel note dedup key updated |
| `consumable-use-system.md` | OQ-CUS-3 marked RESOLVED 2026-06-09 |
| `design/registry/entities.yaml` | `UseItemRequest` note OQ-CUS-3 flag updated to RESOLVED |

---

## Amendment A2 — 2026-10-26: Gold Authority During a Purchase

### Problem

Decision 2 orders "record, then `TrySpendGold`" (the in-memory Currency System). ADR-006 Decision 3 and `character-persistence.md` CR-CP-12 instead have `BeginPurchase` debit `character_records.gold_balance` in the database, with an `InsufficientGold` check against the stored balance. During a session the two balances differ: the in-memory balance carries every change since the last `SaveSession`. A stored-balance check can accept a purchase that the in-memory balance cannot pay (stored 500, in memory 300 after an unsaved spend, cost 400), and a later `SaveSession` overwrites a database-only debit with the in-memory value. Found at Character Persistence Story 011 `/story-readiness` 2026-10-26 (open decision 1).

### Decision

**The Currency System's in-memory balance is the authority during a session. The store writes the resulting balance and gold version as absolute values, in the same transaction as the record; it never computes a debit or a credit.** `save_version` is not advanced by a purchase write, so the next `SaveSession` (which writes `gold_balance` and `gold_version` from memory, guarded by `save_version`) writes the same values.

| Step | Action |
|------|--------|
| Buy (a) | `TrySpendGold(charId, totalCost, reason)` in memory — the authoritative gate (`npc-shop.md` CR-SHOP-5 step 9d). Insufficient funds → reject; the store is not called. |
| Buy (b) | `BeginPurchase(charId, record, goldBalance, goldVersion)` with the in-memory values after (a): one transaction — INSERT the record in state `GoldDebited` and `UPDATE character_records SET gold_balance = @goldBalance, gold_version = @goldVersion`. |
| Buy (c) | (b) fails → `AddGold(charId, totalCost, CompensatingRefund)` in memory; reject. Nothing was made durable. |
| Grant succeeds | `CompletePurchase(purchaseId)` deletes the record. No gold write. |
| Grant fails (Decision 3) / reconnect (Decision 4) | **Store first:** compute `goldBalance` = cap-safe (F-CS-1) in-memory balance + `totalCost` and `goldVersion` = in-memory version + 1; `RefundPurchase(purchaseId, charId, goldBalance, goldVersion)` deletes the record and writes those values in one transaction; only on `Success` call `AddGold(charId, totalCost, CompensatingRefund)` in memory. |

"Record before the debit" (Decision 2) now reads "record before the debit is **durable**": the in-memory debit of Buy (a) is lost with the process on a crash, and the durable debit and the record commit together, so no crash leaves a durable debit without a record.

The refund runs the store first so that a refund is applied at most once. If memory were credited first and `RefundPurchase` then failed, a later `SaveSession` could persist the credit while the record survives as `GoldDebited`, and reconnect reconciliation would refund it a second time. With the store first, a failed `RefundPurchase` leaves the record outstanding and memory not credited: the player sees the debit until the next reconnect refunds it once (Decision 3's critical alert still applies).

`Refunded` and `Completed` are never stored: both methods delete the record in the same write. The `PendingPurchaseState` values stay for logs and the enum's wire stability.

**Store results.** `BeginPurchase` no longer returns `InsufficientGold` or `ConcurrencyConflict` (the Currency System rejects first; the purchase write is not version-guarded); an unknown character returns `DatabaseError` and writes nothing. The enum values stay.

**Known side effect.** A purchase write also persists any gold change made in memory since the last save, slightly ahead of the rest of the character record. Accepted: memory is the authority, and the next `SaveSession` writes the same gold values.

### Alternatives Considered and Rejected

- **Debit the stored balance, then reload Currency from it** — adds a read and a Currency sync point that does not exist, and still disagrees with unsaved in-memory changes.
- **Record only, no stored gold write (ADR-001 Decision 2 literally)** — the debit becomes durable only at the next `SaveSession`; a crash after the record and before that save leaves a record whose debit never reached the database, and reconciliation would refund gold that was never taken.

### Downstream Updates Applied (2026-10-26)

| Document | Change |
|----------|--------|
| `ADR-006-persistence-layer.md` | Dated note under Decision 3: absolute balance and version, no `save_version` bump |
| `design/gdd/character-persistence.md` | CR-CP-12 Begin / Complete / Refund paragraphs rewritten |
| `design/gdd/networking-session.md` | CR-NET-6.4 step 2: store first, then `AddGold` |
| `design/gdd/npc-shop.md` | CR-SHOP-5 step 9d note: `BeginPurchase` after `TrySpendGold` |
| `docs/architecture/control-manifest.md` | Two ADR-001 / ADR-006 lines reworded (manifest version unchanged; regenerate with `/create-control-manifest`) |
| `production/epics/character-persistence/story-011-*.md`, `story-015-*.md` | New method signatures and criteria |
