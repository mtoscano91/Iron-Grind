# Character Persistence

> **Status**: In Review
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-29 (OQ-ZI-6: ZoneClosed added to SessionEndReason; SaveSession HP-override overload added)
> **Implements Pillar**: Earned Power (every stat, every item, every enhancement level is permanently remembered — the world holds the record of what the player earned)

## Overview

Character Persistence is the server-side save and load layer that makes character state durable across sessions. On successful authentication, it loads the character record for the authenticated `CharacterID` — restoring base stats to Character Stats, equipment slots to the Equipment System, inventory contents to the Inventory System, and gold balance to the Currency System — so the game server resumes exactly where the player left off. Writes flow in the other direction: irreversible outcomes (level-ups, enhancements, respecs) are flushed to the database immediately per the Commit-Before-Broadcast contract (Networking Core CR-NET-5); all remaining session state is written when the session expires — either on clean logout or after the 300-second session TTL following a disconnect. Character Persistence does not compute any game values — it serializes and restores what other systems own. It owns the character record schema, the load sequence, and the session-expiry save protocol.

## Player Fantasy

Character Persistence is infrastructure — the player never calls a save function, never watches a progress bar, never sees a confirmation. What they feel is the covenant.

**The Ledger That Never Forgets** *(Earned Power — continuity of accumulation)*: A player lands a +9 at 2am, exhausted, and closes the app before they've fully processed what happened. There is no save prompt. No confirmation. The next morning, the +9 is simply there — not because the game was lucky, but because the system inscribed the outcome the instant it was computed and confirmed, before any outcome message was ever sent to the client. The Commit-Before-Broadcast rule is not a technical constraint. It is this promise made mechanical: *you cannot lose what the server already wrote down.*

*Pillar alignment*: Earned Power. The grind is only meaningful if the record is permanent. Every level, every enhancement, every session of farming that moved a character forward is real because this system makes it impossible for the server to forget. Authentication owns identity — no one can impersonate you. Character Persistence owns accumulation — everything you built is still there when you come back.

## Detailed Design

### Core Rules

**CR-CP-1 — ICharacterPersistence Interface**

Character Persistence is a plain .NET service class (not MonoBehaviour). All methods are async with a `CancellationToken` parameter.

```csharp
interface ICharacterPersistence
{
    Task<CharacterLoadResult>  LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct);
    Task<CharacterSaveResult>  SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct);
    Task<CharacterSaveResult>  SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct);
    // HP-override overload: used by Zone Instancing (CR-ZI-12 step 2) to write CurrentHP=0 for Dead/Respawning
    // slots on zone teardown. hpOverride replaces the CurrentHP value read from Character Stats at save time.
    // All other fields (including CurrentMP) follow the normal CR-CP-10 save sequence.
    Task<CharacterSaveResult>  SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct);
    Task                       CreateStub(AccountID accountId, CharacterID characterId, ClassType classType, CancellationToken ct);

    // PendingPurchase lifecycle — ADR-001 Decisions 2–4; stored in separate pending_purchases table, same DB (ADR-006 Decision 3)
    Task<PendingPurchaseResult>                BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct);
    Task<CharacterSaveResult>                  CompletePurchase(long purchaseId, CancellationToken ct);
    Task<CharacterSaveResult>                  RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct);
    Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct);
}

enum PendingPurchaseResult : byte
{
    Success             = 0,
    InsufficientGold    = 1,
    ConcurrencyConflict = 2,
    DatabaseError       = 255,
}

struct PendingPurchaseRecord
{
    long     Id;         // DB-generated primary key (BIGSERIAL); 0 before insert
    CharacterID CharId;
    uint     RequestId;
    uint     ItemId;
    int      Quantity;
    uint     TotalCost;
    PendingPurchaseState State;  // GoldDebited=0, Refunded=1, Completed=2
}
```

struct CharacterLoadResult { CharacterLoadCode Code; ZoneID LastZoneID; }

enum CharacterLoadCode : byte
{
    Success            = 0,
    CharacterNotFound  = 1,
    AccountMismatch    = 2,
    CorruptRecord      = 3,
    ItemDatabaseMiss   = 4,
    DatabaseError      = 255,
}

enum CharacterSaveResult : byte
{
    Success              = 0,
    CharacterNotFound    = 1,
    ConcurrencyConflict  = 2,
    DatabaseError        = 255,
}

enum IrreversibleOutcomeTrigger : byte
{
    LevelUp            = 0,
    EnhancementResult  = 1,
    RespecCommit       = 2,
    ItemConsumption    = 3,
}

enum SessionEndReason : byte
{
    CleanLogout       = 0,
    SessionTTLExpiry  = 1,
    ZoneClosed        = 2,  // zone instance entered Closed state; Zone Instancing calls SaveSession per slot (CR-ZI-12 step 2)
}
```

**CR-CP-2 — Character Record Schema**

The character record stores 23 logical fields. Fields marked *not persisted* exist only in server memory during a session.

| Field | Type | Notes |
|-------|------|-------|
| CharacterID | uint | Primary key |
| AccountID | uint | Foreign key — verified on every load |
| CharacterName | string | Max 26 bytes UTF-8 |
| ClassType | byte | |
| Level | int | |
| Experience | int | |
| HeldFreePoints | int | Unspent stat points |
| STR / DEX / VIT / INT | int × 4 | Base attribute values |
| MaxHP / MaxMP / AttackPower / Defense / MagicDefense | int × 5 | Computed base stats |
| CritChance / CritMultiplier / AttackRange / AttackSpeedMultiplier / MovementSpeed | float × 5 | Computed base stats |
| CurrentHP / CurrentMP | float | Saved as of last write |
| GoldBalance | uint | |
| GoldVersion | uint | Optimistic concurrency field for Currency System |
| LastZoneID | uint | Zone the character was in when the session ended |
| GearSlots[7] | {ItemID, EnhancementLevel: byte}[] | Fixed-capacity array (IL2CPP safe) |
| InventorySlots[20] | ItemID[] | Fixed-capacity array |
| InventoryItemCounts[20] | int[] | Parallel to InventorySlots. Typed as `int` to match Inventory System's in-memory `Quantity: int` representation. The character record uses a dense fixed-capacity layout; the Inventory System's sparse snapshot wire format `({SlotIndex, ItemId, Quantity})` is distinct — Character Persistence translates between them at load/save boundaries (see CR-CP-3 step 5, CR-CP-10 step 4). |
| SaveVersion | uint | Incremented on every write; used for optimistic concurrency |
| CreatedAtUtc | DateTime | |
| LastSavedAtUtc | DateTime | |

Fields **not persisted** (server memory only, never written to DB):
- Buff modifier values
- Equipment modifier values (recomputed from Item Database on load)
- PrestigeBand / equipmentAppearanceFlags (derived from EnhancementLevel on equip)
- IsTransitioning (transient Equipment System state)

**CR-CP-3 — Load Sequence**

Executed by `LoadCharacter` in this exact order. Step 3 (stat init) must complete before step 4 (equipment re-registration).

| Step | Action | Error path |
|------|--------|-----------|
| 1 | Validate `CharacterID != CharacterID.Invalid` | Return `CorruptRecord` |
| 2 | Fetch record; verify `AccountID` FK matches `ownerAccountID` | `CharacterNotFound` or `AccountMismatch` |
| 3 | Pre-validate all persisted int/float fields against their schema ranges before calling `SetBaseStat()`: Level ∈ [1, 60]; Experience ≥ 0; HeldFreePoints ≥ 0; STR/DEX/VIT/INT ∈ [1, 999]; MaxHP/MaxMP ∈ [1, 99999]; AttackPower/Defense/MagicDefense ≥ 0; all float stats > 0. If any field violates its range → return `CorruptRecord` immediately without calling `SetBaseStat()`. Then call `SetBaseStat()` for all 20 fields. | `CorruptRecord` if range validation fails or any `SetBaseStat()` call rejects |
| 4 | Re-register equipment modifiers: for each occupied GearSlot, validate `slot.EnhancementLevel ≤ MAX_ENHANCEMENT_LEVEL (10)` first — if violated, return `CorruptRecord` (out-of-range level must not reach IEnhancementBonusProvider). Then call `TryGetItem()` on Item Database, `GetFlatBonus()` / `GetElementalBonus()` on IEnhancementBonusProvider, `AddEquipmentModifier()`. On ItemDatabaseMiss: remove slot, log alert, set Code = `ItemDatabaseMiss`, continue. | `CorruptRecord` on invalid EnhancementLevel. ItemDatabaseMiss is non-blocking (load continues). |
| 5 | Restore inventory: write all 20 slots and counts into Inventory System (dense-to-system translation: iterate slots 0–19, skip slots where ItemID = ItemID.Invalid) | |
| 6 | Restore gold: write `GoldBalance` and `GoldVersion` directly into Currency System (state restore — not `AddGold()`). Both fields are required: `GoldBalance` for the correct balance; `GoldVersion` for the `GoldSyncEvent` sent in step 9. | |
| 7 | AtCap check: if `Level == 60`, notify Leveling System (no formula re-evaluation, no level-up events) | |
| 8 | **Dead-on-load correction (EC-DR-2, death-and-respawn.md):** If `savedCurrentHP <= 0`, set `RestoredHP = MaxHP` before applying the clamp — a player whose session TTL expired during a Death & Respawn DEAD state must load alive, not dead. Then clamp `CurrentHP` to `[0, MaxHP]`; clamp `CurrentMP` to `[0, MaxMP]` — after step 4 so MaxHP/MaxMP are final. | |
| 9 | Emit `GoldSyncEvent` (reason: `GoldTransactionReason.Other`) to client | |
| 10 | Emit `StatSnapshotEvent` to client to establish baseline | |
| 11 | Return `CharacterLoadResult { Code, LastZoneID }` | |

**CR-CP-4 — Save Triggers**

All saves are event-driven. There is no periodic or heartbeat save.

| Trigger | Method | When |
|---------|--------|------|
| Level-up | `SaveIrreversibleOutcome(LevelUp)` | After level granted, before broadcast (CR-NET-5) |
| Enhancement result | `SaveIrreversibleOutcome(EnhancementResult)` | After enhancement applied, before broadcast |
| Respec commit | `SaveIrreversibleOutcome(RespecCommit)` | After respec applied, before broadcast |
| Item consumption | `SaveIrreversibleOutcome(ItemConsumption)` | After item consumed, before broadcast |
| Clean logout | `SaveSession(CleanLogout)` | On player-initiated logout |
| Session TTL expiry | `SaveSession(SessionTTLExpiry)` | 300s after disconnect (CR-NET-2) |
| Zone teardown — alive slot | `SaveSession(ZoneClosed)` | Zone instance entering Closed state; per-slot (CR-ZI-12 step 2) |
| Zone teardown — Dead/Respawning slot | `SaveSession(ZoneClosed, hpOverride: 0f)` | Same trigger; slot in `Dead` or `Respawning` state must persist HP=0 (CR-ZI-12 step 2) |

Consecutive level-ups within a single tick: one atomic write for the final state after all levels are granted. No intermediate writes.

**CR-CP-5 — SaveIrreversibleOutcome Failure Protocol**

If `SaveIrreversibleOutcome` returns any non-Success code:
1. Do **not** broadcast the outcome to the client.
2. Signal the calling system to revert its in-memory mutations (**caller-owns rollback**):
   - **Leveling System**: reverts `Level`, `Experience`, and any derived stat changes back to pre-level-up values.
   - **Enhancement System**: reverts `GearSlot.EnhancementLevel` (or restores the item slot on a destruction outcome).
   - **Item Consumption System**: reverts inventory slot changes.
   - **Respec System (RespecCommit)**: reverts stat changes AND issues a compensating `AddGold(amount, GoldTransactionReason.RespecRefund)` to refund the gold debit. The compensating `AddGold()` call must occur before the disconnect is issued.
3. Disconnect the client (`DisconnectReason.Other` — see OQ-CP-1 to promote to `PersistenceFailure`).
4. Fire a critical infrastructure alert.
5. **Preserve the session's rolled-back in-memory state for `SESSION_TTL_SECONDS`.** Do NOT tear down the session immediately. When the TTL fires, `SaveSession(SessionTTLExpiry)` writes the rolled-back state normally, protecting mid-session gold and other non-irreversible session state. The client is disconnected; the server session remains in the Loaded state until TTL expiry.

This protocol applies exclusively to `SaveIrreversibleOutcome` failures. `SaveSession` failures are governed by CR-CP-11.

**CR-CP-6 — Atomicity and Optimistic Concurrency**

Every save is a single database transaction. `SaveVersion` is incremented by 1 on every write. The `UPDATE` statement includes a `WHERE SaveVersion = <expected>` clause. If the update affects zero rows (version mismatch or record deleted), return `ConcurrencyConflict`. No partial writes; no retries on concurrency conflict.

`ConcurrencyConflict` log severity depends on the triggering method:
- `SaveIrreversibleOutcome` → `ConcurrencyConflict`: log a **CRITICAL** error and abort the session. No expected same-session double-call scenario exists for irreversible commits — this indicates an unexpected external write or a persistence layer bug.
- `SaveSession` → `ConcurrencyConflict`: log a **WARNING**. The clean-logout + TTL double-call race (EC-CP-4) is the expected cause; character data is not corrupted.

**CR-CP-7 — Concurrent Save Invariant**

At most one in-flight save per `CharacterID` at any moment. A second save call while one is in flight is queued, never executed in parallel. This is enforced by the persistence layer; callers do not need to coordinate.

**CR-CP-8 — CHARACTER_LOAD_TIMEOUT_SECONDS = 10**

`LoadCharacter` receives a `CancellationToken` that fires after 10 seconds. This is less than the networking layer's `CONNECTING_TIMEOUT_SECONDS` (30s), so a load timeout is surfaced to the auth layer before the connection itself times out.

**CR-CP-9 — CreateStub**

`CreateStub` writes a complete Level 1 record at registration. No special path in `LoadCharacter` for first-login — all records are loaded uniformly.

Stub default values (all classes identical at Level 1):

| Field | Value |
|-------|-------|
| Level | 1 |
| Experience | 0 |
| HeldFreePoints | 0 |
| STR / DEX / VIT / INT | 10 each |
| MaxHP | 400 |
| MaxMP | 220 |
| AttackPower | 30 |
| Defense | 20 |
| MagicDefense | 4 |
| CritChance | 0.065 |
| CritMultiplier | 1.5 |
| AttackRange | 3.0 |
| AttackSpeedMultiplier | 1.030 |
| MovementSpeed | 5.0 |
| CurrentHP | 400 |
| CurrentMP | 220 |
| GoldBalance | 0 |
| GoldVersion | 0 |
| LastZoneID | 0 |
| GearSlots | All empty |
| InventorySlots | All empty |
| SaveVersion | 0 |

`LastZoneID = 0` (`ZoneID.Invalid`) is the valid **first-login sentinel**. `CharacterLoadResult.LastZoneID = 0` signals Zone Instancing to place the character in the designated starting zone rather than restoring to a prior zone. Zone Instancing must handle this case explicitly (tracked as a dependency constraint on the Zone Instancing GDD).

**CR-CP-10 — SaveSession Step Sequence**

Executed by `SaveSession` in this exact order. Steps 1–6 gather in-memory state; step 7 commits atomically.

| Step | Action | Error path |
|------|--------|-----------|
| 1 | Acquire the per-`CharacterID` save slot (CR-CP-7 queue invariant — at-most-one write in flight) | Waits if a save is already in flight |
| 2 | Read all 20 base stat values from Character Stats: `GetBaseStat()` for each field. **HP-override path:** if the HP-override overload was called, replace the `CurrentHP` value read from Character Stats with `hpOverride` before step 7. `CurrentMP` is read normally from Character Stats regardless. | — |
| 3 | Read GearSlots: for each of the 7 slots, read `{ItemID, EnhancementLevel}` from Equipment System (explicitly excludes `IsTransitioning`) | — |
| 4 | Read InventorySlots and InventoryItemCounts: iterate all 20 slots, read `{ItemID, Count}` for each; slots with `ItemID.Invalid` are written as empty (int count = 0) | — |
| 5 | Read `GoldBalance` and `GoldVersion` from Currency System via `GetBalance(CharacterID)` → `(Balance: uint, Version: uint)` | — |
| 6 | Read `LastZoneID` from Zone Instancing for the character's current zone | — |
| 7 | Write DB transaction: `UPDATE` character record with all 23 persisted fields; include `LastSavedAtUtc = UtcNow`, `SaveVersion = SaveVersion + 1`; predicate: `WHERE CharacterID = @id AND SaveVersion = @expected` | 0 rows affected → `ConcurrencyConflict` (CR-CP-6). DB error → `DatabaseError`. |
| 8 | On `ConcurrencyConflict`: log per CR-CP-6 severity rule; return `ConcurrencyConflict` to caller | See EC-CP-4 for the expected double-call race |
| 9 | On `DatabaseError` from `SaveSession(CleanLogout)`: log critical error; return `DatabaseError` to the auth/logout layer. The client may retry logout or receive an error response. The session is **not** torn down immediately. | — |
| 10 | On `DatabaseError` from `SaveSession(SessionTTLExpiry)`: log critical error; release session resources regardless (client already disconnected). | — |
| 11 | On `Success`: release save slot; return `Success` | — |

**CR-CP-11 — Save Failure Protocol Authority**

CR-CP-5 (caller-rollback + session-preservation + disconnect) applies **exclusively** to `SaveIrreversibleOutcome` failures. `SaveSession` failures do NOT trigger CR-CP-5. This aligns with `networking-core.md` CR-NET-5.5, which governs retry policy for non-irreversible save failures.

| Failure path | Protocol |
|---|---|
| `SaveIrreversibleOutcome` → non-Success | CR-CP-5: caller rollback → client disconnect → critical alert → session preserved for TTL |
| `SaveSession(CleanLogout)` → non-Success | Log critical error; return `DatabaseError` to caller. Client may retry logout. Character Persistence does not issue disconnect. |
| `SaveSession(SessionTTLExpiry)` → non-Success | Log critical error; release session resources regardless. Client already disconnected. |
| `SaveSession(ZoneClosed[, hpOverride])` → non-Success | Log critical error; Zone Instancing continues teardown (slot released regardless — zone is closing). Client already disconnected or connection will be closed by `ZoneSessionEnded`. Character Persistence does not block zone teardown on save failure. |

**CR-CP-12 — PendingPurchase Storage and Lifecycle** *(propagated from ADR-001 Decision 2–4, 2026-06-27)*

`PendingPurchase` records are stored in a dedicated `pending_purchases` table in the same PostgreSQL database as `character_records` (ADR-006 Decision 3). They are **not** part of the character record schema (CR-CP-2) and are not read or written by `LoadCharacter`, `SaveSession`, or `SaveIrreversibleOutcome`.

`BeginPurchase` is the only write path that shares a database transaction with another system: the `pending_purchases` INSERT and the Currency System's gold-debit UPDATE on `character_records.gold_balance` execute atomically in a single `NpgsqlTransaction`. If the gold balance is insufficient (`gold_balance < totalCost`), both operations are rolled back and `InsufficientGold` is returned — no record is created and no gold is debited.

`LoadOutstandingPurchases` returns all records with `state = GoldDebited` for a given `charId`. This is called during the reconnect reconciliation step (CR-NET-6.4 step 2 in `networking-session.md`) to refund any purchases whose item-grant step did not complete before a server crash or session expiry. Each outstanding record is resolved by `RefundPurchase`, which calls `AddGold(charId, totalCost, CompensatingRefund)` and deletes the record.

`CompletePurchase` is called on successful `PickupRequest` — it marks the record `Completed` and deletes it.

`RefundPurchase` is called on `PickupRequest` failure (ADR-001 Decision 3) or on reconnect reconciliation — it credits the gold, marks the record `Refunded`, and deletes it.

Both `CompletePurchase` and `RefundPurchase` are idempotent on missing `purchaseId` (record already deleted by a prior reconciliation) — they return `Success` without error.

### States and Transitions

A character record passes through the following lifecycle states on the server:

| State | Meaning |
|-------|---------|
| **Stub** | Record written by `CreateStub` at registration; never loaded into a live session |
| **Unloaded** | Record exists in DB; no active server session holds it |
| **Loading** | `LoadCharacter` in progress; no client traffic yet |
| **Loaded** | Character active in a session; reads and writes are live |
| **Saving (Partial)** | `SaveIrreversibleOutcome` in flight; session continues after write completes |
| **Saving (Full)** | `SaveSession` in flight; session ends after write completes |

| From | To | Trigger |
|------|----|---------|
| — | Stub | Registration (`CreateStub`) |
| Stub | Unloaded | Automatic (Stub and Unloaded are equivalent for load purposes) |
| Unloaded | Loading | Auth success → `LoadCharacter` called |
| Loading | Loaded | `LoadCharacter` returns `Success` |
| Loading | Unloaded | `LoadCharacter` returns any non-Success code |
| Loaded | Saving (Partial) | Irreversible outcome event |
| Saving (Partial) | Loaded | `SaveIrreversibleOutcome` returns `Success` |
| Saving (Partial) | Loaded (client disconnected) | `SaveIrreversibleOutcome` fails → caller rollback + client disconnect; session preserved in memory for SESSION_TTL_SECONDS |
| Loaded (client disconnected) | Saving (Full) | `SessionTTLExpiry` fires after SESSION_TTL_SECONDS |
| Loaded | Saving (Full) | Clean logout or SessionTTLExpiry |
| Saving (Full) | Unloaded | `SaveSession` completes (Success or failure) |

### Interactions with Other Systems

| System | Direction | Contract |
|--------|-----------|---------|
| **Authentication** | Upstream caller | Calls `LoadCharacter` on Auth_Success; calls `CreateStub` at registration (CR-AUTH-8 step 6) |
| **Character Stats** | Downstream | Load: calls `SetBaseStat()` for all 20 base-stat fields (step 3). Save: calls `GetBaseStat()` to read current values. Must not call `GetEffectiveStat()` — persists base values only |
| **Equipment System** | Downstream | Load: calls `AddEquipmentModifier()` per occupied slot after stat init (step 4). Save: reads `GearSlots` directly from record — Equipment System does not provide a save API |
| **Item Database** | Downstream | Load: calls `TryGetItem(ItemID)` to resolve each occupied gear slot for modifier re-registration |
| **IEnhancementBonusProvider** | Downstream | Load: calls `GetFlatBonus(level, gearTier, isWeapon)` and `GetElementalBonus(level, gearTier, isWeapon)` per slot during equipment re-registration |
| **Inventory System** | Downstream | Load: writes all 20 slots and counts directly (state restore). Save: reads slot contents directly |
| **Currency System** | Downstream | Load: writes `GoldBalance` directly (state restore, not `AddGold()`); emits `GoldSyncEvent`. Save: reads current balance |
| **Leveling System** | Downstream | Load: notifies at-cap if `Level == 60` (step 7). Save trigger: `LevelUp` irreversible outcome fires after level is granted |
| **Networking Core** | Upstream trigger | CR-NET-2: session TTL expiry triggers `SaveSession(SessionTTLExpiry)`. CR-NET-5: irreversible outcome events trigger `SaveIrreversibleOutcome` before any broadcast |
| **Zone Instancing** | Bidirectional | Downstream consumer: receives `LastZoneID` from `CharacterLoadResult` for zone placement. Upstream caller: calls `SaveSession(ZoneClosed)` per slot during zone teardown (CR-ZI-12 step 2); calls `SaveSession(ZoneClosed, hpOverride: 0f)` for Dead/Respawning slots |
| **NPC Shop** | Upstream caller | Calls `BeginPurchase` (atomic PendingPurchase INSERT + gold debit), `CompletePurchase` (PickupRequest success), `RefundPurchase` (PickupRequest failure). Reconnect reconciliation calls `LoadOutstandingPurchases` + `RefundPurchase` per outstanding record (ADR-001 Decisions 2–4; CR-CP-12; `networking-session.md` CR-NET-6.4 step 2) |

## Formulas

**F-CP-1 — Load Timeout Budget**

`CHARACTER_LOAD_TIMEOUT_SECONDS = 10`

This value is a sub-budget of the networking layer's connection timeout:

`CHARACTER_LOAD_TIMEOUT_SECONDS (10) < CONNECTING_TIMEOUT_SECONDS (30)`

This guarantees the persistence layer surfaces a timeout before the connection layer drops the client, allowing a clean `LoadCharacter` failure path (return `DatabaseError`, auth layer rejects login gracefully) rather than a silent connection drop.

**F-CP-2 — SaveVersion Increment**

On every successful write:

`SaveVersion_new = SaveVersion_current + 1`

Write predicate: `UPDATE … WHERE CharacterID = @id AND SaveVersion = @expected`. If rows affected = 0 → `ConcurrencyConflict`.

`SaveVersion` is a monotonically increasing uint. Overflow at `uint.MaxValue` (4,294,967,295) wraps to 0; a character would require ~4.3 billion saves for this to occur (accepted for the optimistic-concurrency purpose only). **`SaveVersion` MUST NOT be used by monitoring, log correlation, or event ordering systems as a monotonic sequence number** — `LastSavedAtUtc` is the correct ordering field for audit trail purposes. The monotonic increase guarantee does not survive wrap events.

**F-CP-3 — Stub Level-1 Base Stats**

Pre-computed constants — no formula; these are the canonical L1 values for all classes. Source: Character Stats GDD (L1 spawn values). Owned by Character Stats; Character Persistence stores them verbatim.

| Stat | Value | Type |
|------|-------|------|
| MaxHP | 400 | int |
| MaxMP | 220 | int |
| AttackPower | 30 | int |
| Defense | 20 | int |
| MagicDefense | 4 | int |
| CritChance | 0.065 | float |
| CritMultiplier | 1.5 | float |
| AttackRange | 3.0 | float |
| AttackSpeedMultiplier | 1.030 | float |
| MovementSpeed | 5.0 | float |
| CurrentHP (initial) | 400.0 | float |
| CurrentMP (initial) | 220.0 | float |
| STR / DEX / VIT / INT | 10 each | int |

**F-CP-4 — CurrentHP/MP Clamp on Load**

```
RestoredHP = clamp(saved_CurrentHP, 0, computed_MaxHP)
RestoredMP = clamp(saved_CurrentMP, 0, computed_MaxMP)
```

Applied at load step 8, after equipment modifiers are re-registered (step 4) so `MaxHP`/`MaxMP` reflect equipment bonuses. A `MaxHP` reduced by an item removal clamps the restored HP down without error.

Example: `saved_CurrentHP = 650`, `computed_MaxHP = 500` (after ItemDatabaseMiss removed a slot) → `RestoredHP = 500`.

## Edge Cases

**EC-CP-1 — ItemDatabaseMiss on Load**

A gear slot references an ItemID that no longer exists in the Item Database (item deleted post-ship, data corruption, migration error).

*What happens*: The slot is removed from the in-memory record (the DB record is corrected on the next save). Load continues without interruption. `CharacterLoadResult.Code` is set to `ItemDatabaseMiss`. A critical alert is logged. The character logs in without the missing item equipped; HP/MP are clamped to the now-lower effective MaxHP/MaxMP via F-CP-4.

*What does not happen*: The load is not aborted. The client is not disconnected. No player-facing error is surfaced at this stage.

**EC-CP-2 — CurrentHP Exceeds MaxHP on Load**

Saved `CurrentHP` is greater than `computed_MaxHP` after equipment re-registration (e.g., a bonus item was removed from the DB since last save).

*What happens*: F-CP-4 clamp is applied at step 8: `RestoredHP = clamp(saved_CurrentHP, 0, computed_MaxHP)`. The character loads with full HP relative to their new max. No error, no alert. The clamp is always applied — not only on mismatch.

**EC-CP-3 — IsTransitioning Flag Set at Disconnect**

A player disconnects while `IsTransitioning` is true in the Equipment System (a merge or equip operation mid-flight).

*What happens*: `IsTransitioning` is not persisted (CR-CP-2). On `SaveSession(SessionTTLExpiry)` after the 300s TTL, the in-memory record is written. If the Equipment System's transaction completed before disconnect, the state is consistent and writes correctly. If the transaction did not complete, the Equipment System's rollback path (CR-EQS-14) restored pre-operation state — also correct to save. Character Persistence does not inspect `IsTransitioning`; the Equipment System owns that invariant.

**EC-CP-4 — Duplicate SaveSession Call**

`SaveSession` is called twice for the same `CharacterID` (e.g., clean logout and session TTL both fire within the 300-second window).

*What happens*: The second call hits the optimistic concurrency check. The first write incremented `SaveVersion`; the second write's `WHERE SaveVersion = @expected` predicate matches zero rows and returns `ConcurrencyConflict`. This is logged as a warning (not a critical error — this race is expected). Character data is not corrupted; the first write captured the correct final state.

**EC-CP-5 — SaveIrreversibleOutcome Failure**

The DB write for an irreversible outcome fails or times out.

*What happens*: Per CR-CP-5 — the outcome is not broadcast. The **calling system reverts its own in-memory mutations** (caller-owns rollback). For `RespecCommit`: the caller also issues a compensating `AddGold()` to refund the gold debit before disconnect. The client is disconnected (`DisconnectReason.Other`). A critical infrastructure alert fires. The server **preserves the session's rolled-back in-memory state for `SESSION_TTL_SECONDS`** — it does not tear down the session immediately. When the TTL fires, `SaveSession(SessionTTLExpiry)` writes the rolled-back state to DB, protecting mid-session gold and other non-irreversible session state accumulated before the failed outcome. The DB record reflects the state before the failed outcome. On re-authentication, the character loads from the last successfully written state; the outcome is absent as if it never occurred. No duplication; no phantom progress.

*What does not happen*: The outcome is never broadcast before the write succeeds. The Commit-Before-Broadcast rule (CR-NET-5) is the mechanical guarantee of this invariant. The session is not immediately torn down — the 300s TTL save still fires.

## Dependencies

**Upstream — Systems that call into Character Persistence**

| System | What it calls | When |
|--------|--------------|------|
| **Authentication** | `LoadCharacter`, `CreateStub` | On Auth_Success; on account registration |
| **Networking Core** | Triggers `SaveIrreversibleOutcome` (CR-NET-5); triggers `SaveSession` (CR-NET-2) | On irreversible outcomes; on session TTL expiry |
| **Zone Instancing** | `SaveSession(ZoneClosed)` and `SaveSession(ZoneClosed, hpOverride)` | Per slot during zone teardown (CR-ZI-12 step 2) |

**Downstream — Systems Character Persistence calls into**

| System | What Character Persistence calls | GDD |
|--------|----------------------------------|-----|
| **Character Stats** | `SetBaseStat()` on load; `GetBaseStat()` on save | character-stats.md |
| **Equipment System** | `AddEquipmentModifier()` on load | equipment-system.md |
| **Item Database** | `TryGetItem(ItemID)` on load | item-database.md |
| **IEnhancementBonusProvider** | `GetFlatBonus()`, `GetElementalBonus()` on load | enhancement-system.md |
| **Inventory System** | Direct slot/count write on load; direct slot/count read on save | inventory-system.md |
| **Currency System** | Direct GoldBalance write on load; `GoldSyncEvent` emit; direct GoldBalance read on save | currency-system.md |
| **Leveling System** | At-cap notification on load if `Level == 60` | leveling-system.md |

**Downstream consumers (receive output)**

| System | What it receives | When |
|--------|-----------------|------|
| **Zone Instancing** | `CharacterLoadResult.LastZoneID` | After `LoadCharacter` returns `Success` |
| **Death & Respawn** | In-session respawn restores character HP/position via Death & Respawn system directly in memory — `LoadCharacter` is **NOT** called on respawn (the character is already loaded in server memory). `SaveIrreversibleOutcome` is not triggered by death. Character Persistence is only invoked at login (`LoadCharacter`) and session end (`SaveSession`). | Not yet authored (#7) — must enforce this contract |

**Bidirectional verification status**

- Authentication — references Character Persistence (CR-AUTH-8) ✓
- Networking Core — references Character Persistence (CR-NET-2, CR-NET-5) ✓
- Zone Instancing — calls `LoadCharacter` on entry; calls `SaveSession(ZoneClosed[, hpOverride])` on teardown (CR-ZI-12); receives `LastZoneID` ✓ (OQ-ZI-6 resolved 2026-05-29)
- Character Stats, Equipment System, Inventory System, Currency System, Leveling System — must be verified for reverse reference before implementation
- Death & Respawn — not yet authored; must include Character Persistence when designed

## Tuning Knobs

| Knob | Current Value | Safe Range | Effect |
|------|--------------|------------|--------|
| `CHARACTER_LOAD_TIMEOUT_SECONDS` | 10 | 5–25 | How long `LoadCharacter` waits for the DB before returning `DatabaseError`. **Must stay below `CONNECTING_TIMEOUT_SECONDS` (30s)** — exceeding it makes a DB timeout look like a connection timeout to the client. Lower values surface DB failures faster at the cost of rejecting slow-but-valid loads during DB load spikes. |
| `SESSION_TTL_SECONDS` | 300 | 60–600 | Time the server preserves a disconnected session before firing `SaveSession(SessionTTLExpiry)` and releasing resources. Owned by Networking Core (CR-NET-2) — listed here for cross-reference only. Lower values reduce server memory pressure; higher values allow seamless reconnect at the cost of holding resources longer. |

`SaveVersion` overflow at `uint.MaxValue` is an accepted behavior (F-CP-2), not a tunable knob.

## Visual/Audio Requirements

None. Character Persistence is server-side infrastructure. It produces no visual or audio output. Client-side feedback for load state (connecting screen, etc.) belongs to the UI layer and is out of scope for this system.

## UI Requirements

None. Character Persistence exposes no UI surface. The `CharacterLoadResult.Code` field is available to the auth/login flow for mapping to player-facing error messages, but the message text and display logic belong to the UI system.

## Acceptance Criteria

**Load Sequence**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-1 | `LoadCharacter` called with valid `CharacterID` and matching `AccountID` | Returns `CharacterLoadResult { Code = Success, LastZoneID = <saved value> }` |
| AC-CP-2 | `LoadCharacter` called with `CharacterID.Invalid` | Returns `Code = CorruptRecord`; no DB query issued |
| AC-CP-3 | `LoadCharacter` called with `CharacterID` whose DB record has a different `AccountID` | Returns `Code = AccountMismatch` |
| AC-CP-4 | `LoadCharacter` called for a `CharacterID` not in the DB | Returns `Code = CharacterNotFound` |
| AC-CP-5 | `LoadCharacter` called; DB responds after 10.5 seconds (injected delay) | Returns `Code = DatabaseError` before `CONNECTING_TIMEOUT_SECONDS` (30s) elapses |
| AC-CP-6 | After `LoadCharacter` succeeds, `GetBaseStat(MaxHP)` | Returns the value stored in the DB record |
| AC-CP-7 | After `LoadCharacter` succeeds, `GetEffectiveStat(AttackPower)` | Equals `GetBaseStat(AttackPower)` + weapon flat bonus for the loaded enhancement level |
| AC-CP-8 | After `LoadCharacter` succeeds | `GoldSyncEvent` received by the client |
| AC-CP-9 | After `LoadCharacter` succeeds | `StatSnapshotEvent` received by the client |
| AC-CP-10 | `LoadCharacter` for a character with `Level == 60` | Leveling System reports at-cap; no level-up event emitted |

**Gear slot load — step ordering**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-11 | Character has gear in slot 0; `TryGetItem` returns the item | All 20 `SetBaseStat` calls complete before first `AddEquipmentModifier` call |
| AC-CP-12 | Gear slot references an ItemID not in Item Database (injected: item deleted) | Slot removed from in-memory record; `Code = ItemDatabaseMiss`; load completes; client not disconnected |

**Save triggers**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-13 | Level-up event fires; `SaveIrreversibleOutcome(LevelUp)` succeeds | Level-up broadcast sent only after DB write confirms success. **Test requires**: `ISavePersistence` mock with controllable Task delay — hold write open, assert no LevelUpBroadcast received on `IEventBus` mock, release write, assert LevelUpBroadcast received within one server tick. |
| AC-CP-14 | `SaveIrreversibleOutcome(LevelUp)` DB write fails (injected failure); character was at Level 10 pre-outcome | Outcome not broadcast. Caller (Leveling System) reverts: `GetBaseStat(Level)` = 10 (pre-outcome); `Experience` = pre-level-up value. `DisconnectReason.Other` sent to client. `ICriticalAlertService.AlertFired` count = 1. Session preserved in memory (not immediately torn down). `SaveSession(SessionTTLExpiry)` fires after TTL and writes rolled-back state. **Requires**: `IEventBus` mock, `ISavePersistence` fault injection, `ICriticalAlertService` spy, injectable TTL. |
| AC-CP-15 | Clean logout then re-login | `LoadCharacter` returns all 23 persisted schema fields matching pre-logout values: all 20 base stats via `GetBaseStat()`, `GoldBalance`, `GoldVersion`, `LastZoneID`, all 7 `GearSlots` (`ItemID` + `EnhancementLevel`), all 20 `InventorySlots` (`ItemID` + `Count`), `CurrentHP`, `CurrentMP`. `SaveVersion` = N+1 (incremented by `SaveSession`). `CreatedAtUtc` unchanged. **Explicitly excluded from verification** (not persisted per CR-CP-2): buff modifier values, equipment modifier computed values, `PrestigeBand`, `IsTransitioning`. |
| AC-CP-16 | Session TTL expires after disconnect | `SaveSession(SessionTTLExpiry)` called; DB record's `LastSavedAtUtc` updated; `SaveVersion` incremented. **Test requires**: injectable `SESSION_TTL_SECONDS` (set to 1s, not 300s real-time wait) via `ISessionConfiguration`; injectable system clock. |
| AC-CP-17 | Two consecutive level-ups granted in one tick | One `SaveIrreversibleOutcome` call with final level; not two |
| AC-CP-27 | Enhancement result fires; `SaveIrreversibleOutcome(EnhancementResult)` succeeds | Enhancement result broadcast sent only after DB write confirms success. Verified via `IEventBus` mock + `ISavePersistence` Task delay injection (same pattern as AC-CP-13). |
| AC-CP-28 | Respec committed; `SaveIrreversibleOutcome(RespecCommit)` DB write fails (injected) | Caller (Respec/Class System) reverts stat changes. Compensating `AddGold(amount, GoldTransactionReason.RespecRefund)` called before disconnect — verify via Currency System spy that `GoldBalance` returns to pre-debit value. Outcome not broadcast. Client disconnected. Critical alert fired. |
| AC-CP-29 | Item consumed; `SaveIrreversibleOutcome(ItemConsumption)` succeeds | Item consumption broadcast sent only after DB write confirms success. Verified via `IEventBus` mock + `ISavePersistence` Task delay injection. |
| AC-CP-30 | `SaveSession(id, ZoneClosed, hpOverride: 0f, ct)` called for a character whose in-memory `CurrentHP` = 650 | DB record's `CurrentHP` = 0 after write. `CurrentMP` unchanged (reads from Character Stats normally). `SaveVersion` incremented. |
| AC-CP-31 | `SaveSession(id, ZoneClosed, ct)` (no HP override) called for a character with `CurrentHP` = 350 | DB record's `CurrentHP` = 350 (normal read from Character Stats — null-override path is identical to standard `SaveSession`). |

**Optimistic concurrency**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-18 | `SaveSession` called; write succeeds | DB row's `SaveVersion` = previous value + 1 |
| AC-CP-19 | `SaveVersion` mismatch injected on a `SaveIrreversibleOutcome` call via direct DB row update by a separate connection before the write commits | Returns `ConcurrencyConflict`. `Logger.Critical()` called exactly once (per CR-CP-6: `SaveIrreversibleOutcome` conflict = CRITICAL). Session aborted. **Requires**: `ILogger` spy injected at construction. |
| AC-CP-20 | `SaveSession` called twice for the same `CharacterID` before first DB round-trip returns (Task delay injected on first write) | Second call returns `ConcurrencyConflict`. `Logger.Warning()` called; `Logger.Critical()` NOT called (per CR-CP-6: `SaveSession` conflict = WARNING). Character data in-memory state matches pre-first-call snapshot. **Requires**: `ILogger` spy; `ISavePersistence` mock with Task delay. |

**Concurrent save invariant**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-21 | Two `SaveIrreversibleOutcome` calls issued for same `CharacterID`; first write is held open via injected Task delay (500ms) | Second call does not begin its DB operation until first completes — verified via `ISaveCoordinator` spy recording `(CharacterID, write-started-timestamp, write-completed-timestamp)` with no timestamp overlap. Both writes complete with SaveVersion N+1 then N+2. Zero parallel DB writes for same `CharacterID`. **Requires**: `ISaveCoordinator` spy interface defined in the persistence layer. |

**CreateStub**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-22 | `CreateStub` called for new AccountID/CharacterID | DB record exists; subsequent `LoadCharacter` returns `Success`; `GetBaseStat(MaxHP)` = 400, `GetBaseStat(AttackPower)` = 30, GoldBalance = 0 |
| AC-CP-23 | `LoadCharacter` called for a freshly created stub | Load proceeds identically to any other character — no special code path |

**HP/MP clamp**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-24 | Character saved with `CurrentHP = 650`; on reload, ItemDatabaseMiss reduces computed `MaxHP` to 500 | `RestoredHP = 500`; no error |
| AC-CP-25 | Character saved with `CurrentHP = 300`, `MaxHP = 400`; no equipment changes on reload | `RestoredHP = 300` (unchanged) |

**End-to-end**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-CP-26 | Player closes app with a +9 weapon equipped; re-opens 8 hours later | +9 weapon present; enhancement level and gear slot assignment identical to the moment the app was closed |

## Open Questions

| ID | Question | Blocking? | Status |
|----|----------|-----------|--------|
| OQ-CP-1 | Should `DisconnectReason.PersistenceFailure` be added as a distinct disconnect code (vs. the current `DisconnectReason.Other` used in CR-CP-5)? A dedicated code would allow client telemetry to distinguish persistence failures from other disconnects, and a client-side message ("session ended — no changes to your character") would prevent player distrust after a failed enhancement attempt. | **Recommended** — escalated: player trust in the Legendary Gear pillar depends on distinguishing "server error, data safe" from a generic disconnect. | Open — target pre-launch |
| OQ-CP-2 | If the client closes between `SaveIrreversibleOutcome` DB write and broadcast delivery (e.g., app killed mid-animation after an enhancement), the player re-logs with the enhancement present but receives no celebration notification. Should the login flow or Enhancement System track whether the enhancement broadcast was acknowledged and replay it on next login? | No — Character Persistence does not own this. | Open — dependency on Enhancement System + Login UI; must be tracked in the Enhancement System GDD. |
| OQ-CP-3 | `networking-wire-protocol.md` has OQ-NC-SER-2: `SessionReady` schema is pending Character Persistence GDD fields (`currentHP`, `maxHP`, `currentMP`, `maxMP`, `level`, `heldFreePoints`, `goldBalance`, `goldVersion`). All of these are defined in CR-CP-2. | No — resolved by this GDD. | **Action required**: Update `networking-wire-protocol.md` to close OQ-NC-SER-2 before implementation begins. |
| OQ-CP-4 | Should `CreateStub` have a defined timeout constant analogous to `CHARACTER_LOAD_TIMEOUT_SECONDS`? The `CancellationToken` parameter exists but its timeout source is unspecified. If `CreateStub` hangs, registration completes from the auth side but the character record is never written. | No — auth layer's connection timeout implicitly bounds it. | Open — recommended to define explicitly (`CHARACTER_STUB_TIMEOUT_SECONDS`) before implementation. |
