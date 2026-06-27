# Currency System

> **Status**: Approved
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-04-27
> **Implements Pillar**: Social Gravity — gold scarcity powers the enhancement prestige loop

## Overview

The Currency System is the gold economy layer for Project Iron Grind. It maintains a single currency — Gold (g) — as a server-side unsigned integer balance per character. Gold enters the economy exclusively through monster drops (the primary faucet) and exits through NPC Shop purchases (Enhancement scrolls, consumables) and Respec costs (stat and skill resets). The Currency System has no gameplay logic of its own: it is a balance store with debit, credit, and query operations. All spending decisions and drop rates live in downstream systems (NPC Shop, Loot Table System) that call into the Currency System's write API. The system must guarantee that a character's gold balance can never go below 0 (no overdraft), and that all mutations are server-authoritative — the client holds a read-only display copy received via state sync.

## Player Fantasy

The player is not grinding gold — they are grinding *toward something specific*. The Currency System exists to give the grind a name and a finish line: a stack of Enhancement Scrolls, a one-way ticket to the next +1 attempt, a respec that finally lets them go full DPS. The target dollar amount is visible, the per-clear yield is predictable, and the gap between the two is the whole motivational engine of the session.

The system itself is non-intrusive — it surfaces only when needed and never interrupts the grind rhythm. What the player feels is the rhythm of accumulation — clear, count, clear, count — and the weight of the decision at the shop window. Gold is not a score; it is potential energy. Spending it is always a choice to turn saved time into risk. The Currency System's job is to make that exchange feel earned, never trivial.

*Pillar alignment: Social Gravity — gold spent on Enhancement fuels the prestige loop that makes rare gear visible to the whole party.*

## Detailed Design

### Core Rules

**Rule 1 — Single currency.** Gold (g) is the only currency at MVP. No premium currency, no secondary currencies.

**Rule 2 — Per-character, server-authoritative balance.** Each character maintains an independent `uint` Gold balance stored server-side. Gold is not shared across characters on the same account. The client holds a read-only display copy pushed via `GoldSyncEvent`; it never computes its own balance delta.

**Rule 3 — Integer-only amounts.** All gold values (drop amounts, prices, respec costs) are whole numbers. No fractional gold. All inputs to `AddGold` and `TrySpendGold` must be `uint`; callers must not pass float values. Loot Table System gold drop table entries must define integer ranges only.

**Rule 4 — Gold cap.** A character's balance cannot exceed `GOLD_CAP = 9,999,999g`. `AddGold` calls that would breach the cap clamp the balance to `GOLD_CAP` and discard excess gold; the player receives a toast notification ("Gold pouch is full"). Cap enforcement uses overflow-safe arithmetic: `newBalance = (amount > GOLD_CAP - balance) ? GOLD_CAP : balance + amount`.

**Rule 5 — No overdraft.** `TrySpendGold` fails atomically if `balance < cost`. The balance is never modified on a failed spend. No partial spends.

**Rule 6 — Spend-before-grant.** All callers (NPC Shop, Respec System) must call `TrySpendGold` and receive `Success` before granting any benefit. Benefits are never granted speculatively. If item grant fails after a successful debit (e.g., inventory full), the caller owns a compensating `AddGold` call to refund the exact debited amount. The Currency System has no rollback mechanism.

**Rule 7 — Gold is safe on death.** Player death has no effect on Gold balance. *Rationale: meaningful risk is concentrated at the Enhancement moment, not the farming phase. Penalizing gold on death would double-punish players who die through poor play or mobile disconnection — a penalty unrelated to enhancement skill. Enhancement item destruction is the primary consequence mechanic; gold preservation on death lets players farm with full confidence and convert savings into enhancement risk at will.*

**Rule 8 — No P2P gold transfer at MVP.** A `TransferGold` stub exists in the API but is not implemented at MVP. Deferred to Trade System GDD (outside the 35-system MVP scope).

**Rule 9 — No `SetGold` operation.** There is no API to set a balance to an arbitrary value. All mutations are additive (`AddGold`) or subtractive (`TrySpendGold`). Admin correction uses a separate `CurrencyAdminService` not accessible to game systems.

**Rule 10 — Server-only execution.** Currency System resides in `ServerLogic.asmdef`, excluded from client builds via Unity platform constraints (same model as Damage Calculation, Rule 1).

**Rule 11 — Concurrency safety via optimistic locking.** Each balance record carries a monotonic `Version` counter. A `TrySpendGold` write succeeds only if the record's `Version` at write time matches the version captured at read time. On conflict, the Currency System retries internally once; two consecutive conflicts return `ConcurrencyConflict` to the caller, which surfaces "Transaction failed — please try again" to the player.

`AddGold` (server-generated, not player-initiated) MUST be implemented as a **single atomic storage-layer expression** — both the balance update and the `Version` increment occur in one write, with no prior version read. Example: `UPDATE characters SET balance = MIN(balance + ?, GOLD_CAP), version = version + 1 WHERE id = ?`. A read-modify-write implementation is a correctness bug: concurrent party kills generate multiple simultaneous `AddGold` calls for the same character, and lost updates are the normative failure mode of a non-atomic implementation (see AC-CS-J-01).

**Rule 12 — CharacterID type.** `CharacterID` is introduced here as a `readonly struct wrapping uint`, consistent with `EntityID` (Character Stats GDD) and `ItemID` (Item Database GDD). Requires an explicit `IEqualityComparer<CharacterID>` if used as a dictionary key (IL2CPP safety). **Identity space:** `CharacterID` identifies a persistent player account record (used by Currency System, Character Persistence). `EntityID` identifies any runtime combat entity — players, enemies, and NPCs. Player characters have both; enemies have only `EntityID`. Character Persistence owns the `CharacterID → EntityID` mapping. Do not conflate the two types — they serve different identity scopes.

**Gold faucets (MVP):**
- **Monster drops** (primary and only): Loot Table System calls `AddGold(characterId, dropAmount, GoldTransactionReason.MonsterDrop)` on kill.

*Item sell-back IS a gold faucet at MVP. Inventory System GDD (Approved 2026-05-15) introduced `SellItem` with `GoldTransactionReason.ItemSell=7` — OQ-CS-1 original resolution reversed. Sell price is `ItemDefinition.SellPriceGold` from the Item Database. Inventory System owns the `AddGold(CharacterID, sellPrice, GoldTransactionReason.ItemSell)` call; NPC Shop GDD must define the sell-back UX flow when authored. See OQ-CS-1.*

**Gold sinks (MVP):**
- **NPC Shop purchases**: NPC Shop calls `TrySpendGold(characterId, itemPrice, GoldTransactionReason.ScrollPurchase)` for scroll and consumable purchases.
- **Respec costs (stat)**: Class/Leveling System calls `TrySpendGold(characterId, respecCost, GoldTransactionReason.RespecStat)` for stat resets.
- **Respec costs (skill)**: Class/Leveling System calls `TrySpendGold(characterId, respecCost, GoldTransactionReason.RespecSkill)` for skill resets. Currency System has no knowledge of the respec cost formula.

---

### States and Transitions

| State | Condition | `AddGold` behavior | `TrySpendGold` behavior |
|-------|-----------|-------------------|------------------------|
| `Empty` | Balance == 0 | Increases balance → `Normal` or `AtCap` | Returns `InsufficientFunds`; no write |
| `Normal` | 0 < Balance < 9,999,999 | Increases balance; may → `AtCap` | Decreases balance; may → `Empty` |
| `AtCap` | Balance == 9,999,999 | Clamps to GOLD_CAP; returns `Success`, `NewBalance=GOLD_CAP`, `Error=None`; callers detect cap from `result.NewBalance == GOLD_CAP`; no state change | Decreases balance → `Normal` |

**Client sync:** After every successful balance mutation, the server emits `GoldSyncEvent` to the owning client connection. Fields: `CharacterID`, `NewBalance`, `Version`, `Reason` (`MonsterDrop`, `ScrollPurchase`, `RespecStat`, `RespecSkill`, `AdminAdjust`). The client updates its displayed balance only on receiving this event — never speculatively on user action (no optimistic UI updates). **`NewBalance` MUST be transmitted as an absolute balance — never a delta.** Delta encoding would break EC-CS-7's stale-discard correctness under lossy mobile transport (this is a constraint on the Networking Core GDD's serialization format).

**Reconnect behavior:** On session establishment (including reconnect), the server MUST include the current `CharacterID`, `Balance`, and `Version` in the session handshake (requirement on Networking Core GDD). The client seeds its cached `Version` from the handshake. If the cached `Version` is behind the server's, it calls `GetBalance(CharacterID) → (Balance: uint, Version: uint)` to confirm current state. It does not interpolate the delta from session history.

---

### Interactions with Other Systems

| System | Direction | Currency System provides | Other system provides |
|--------|-----------|------------------------|----------------------|
| Loot Table System | Caller → Currency | `AddGold(CharacterID, uint amount, GoldTransactionReason.MonsterDrop) → GoldMutationResult` | Monster kill event + integer drop amount |
| NPC Shop | Caller → Currency | `TrySpendGold(CharacterID, uint itemPrice, GoldTransactionReason.ScrollPurchase) → GoldMutationResult` for purchases; compensating `AddGold(CharacterID, itemPrice, GoldTransactionReason.AdminAdjust)` on failed item grants (EC-CS-5). Sell-back is MVP-scope (OQ-CS-1 reversed 2026-05-15) — NPC Shop GDD owns sell-back UX; Inventory System calls `AddGold(CharacterID, sellPrice, GoldTransactionReason.ItemSell)`. | Item price and sell-back price from NPC Shop / Item Database |
| Class/Leveling System (Respec) | Caller → Currency | `TrySpendGold(CharacterID, uint respecCost, GoldTransactionReason.RespecStat / RespecSkill) → GoldMutationResult` | Respec cost value (computed internally; flat `uint` debit) |
| Character Persistence | Bidirectional | `GetBalance(CharacterID) → (Balance: uint, Version: uint)` on save; accepts loaded `Balance + Version` on load; emits `GoldSyncEvent` to client after load | Saved `Balance + Version` from character record |
| Networking Core | Dependency | Requires session handshake to carry `CharacterID`, `Balance`, `Version` to client on connect/reconnect (EC-CS-6). `GoldSyncEvent` transport channel. | Session establishment event (Networking Core GDD owns the handshake format) |
| HUD | Consumer | `GoldSyncEvent(CharacterID, NewBalance: uint, Version: uint, Reason: GoldTransactionReason)` | — |

## Formulas

### F-CS-1 — Cap-Safe Gold Addition

```
NewBalance = (amount > GOLD_CAP - balance) ? GOLD_CAP : balance + amount
```

| Variable | Type | Range | Source |
|----------|------|-------|--------|
| `balance` | `uint` | [0, 9,999,999] | Current character gold balance |
| `amount` | `uint` | [1, 9,999,999] | Incoming gold (from Loot Table monster drops at MVP) |
| `GOLD_CAP` | `uint constant` | 9,999,999 | Defined in this GDD |
| `NewBalance` | `uint` | [0, 9,999,999] | Output — new balance after credit |

**Output range:** [1, 9,999,999] for any non-zero input.

**Why subtraction-first:** `balance + amount` overflows `uint` when both are near `GOLD_CAP`. The guard form `amount > GOLD_CAP - balance` evaluates `GOLD_CAP - balance` first (safe — `balance` is always ≤ `GOLD_CAP`), then compares without risk of overflow.

**Example (normal case):** balance=500, amount=150 → 150 > 9,999,499? No → NewBalance = 650.

**Example (cap clamp):** balance=9,999,900, amount=200 → 200 > 99? Yes → NewBalance = 9,999,999. 101g discarded.

---

### F-CS-2 — Spend Guard

```
TrySpendGold succeeds if and only if: balance >= cost
NewBalance (on success) = balance - cost
```

| Variable | Type | Range | Source |
|----------|------|-------|--------|
| `balance` | `uint` | [0, 9,999,999] | Current character gold balance |
| `cost` | `uint` | [1, 9,999,999] | Requested spend amount (from NPC Shop or Respec System) |
| `NewBalance` | `uint` | [0, 9,999,998] | Output — new balance after debit |

**Output range:** [0, 9,999,998] on success; balance unchanged on failure.

**No overflow risk on subtraction:** The `balance >= cost` guard is enforced before the subtraction. If the guard passes, `balance - cost` cannot underflow.

**Example (success):** balance=1,200, cost=400 → 1,200 >= 400? Yes → NewBalance = 800.

**Example (failure):** balance=300, cost=400 → 300 >= 400? No → return `InsufficientFunds`, NewBalance unchanged at 300.

---

*Note: All other gold values — monster drop amounts, NPC Shop prices, Enhancement Scroll costs, Respec costs — are defined in their respective GDDs (Loot Table System, NPC Shop, Enhancement System, Class/Leveling System) and passed to Currency System as flat `uint` values. Currency System enforces only the formulas above.*

## Edge Cases

**EC-CS-1 — Zero-amount call**
`AddGold` or `TrySpendGold` called with `amount = 0` or `cost = 0`. Return `InvalidAmount`; no write. This is a caller bug, not a player-facing error — log server-side and alert.

**EC-CS-2 — Balance at exactly GOLD_CAP, AddGold called**
`balance = 9,999,999`, `AddGold(amount = any)`. F-CS-1 clamp fires: `NewBalance = GOLD_CAP`, excess discarded. Player receives "Gold pouch is full" toast. System stays in `AtCap` state. No error returned to caller.

**EC-CS-3 — Balance at zero, TrySpendGold called**
Return `InsufficientFunds`; no write. System stays in `Empty` state.

**EC-CS-4 — Concurrency conflict on TrySpendGold**
Two `TrySpendGold` calls for the same character arrive near-simultaneously. The first write succeeds (Version matches). The second write finds the Version stale (0 rows updated). The Currency System retries internally once by re-reading the balance. If the balance now satisfies the cost, the retry succeeds. If the balance is insufficient, returns `InsufficientFunds`. A second consecutive conflict surfaces `ConcurrencyConflict` to the caller, which shows "Transaction failed — please try again" to the player.

**EC-CS-5 — Item grant fails after gold debit**
NPC Shop calls `TrySpendGold` (succeeds), then calls Inventory to add the item. Inventory reports full. NPC Shop calls `AddGold(characterId, itemCost, GoldTransactionReason.AdminAdjust)` as a compensating transaction. The refund `AddGold` cannot overflow: `balance` was just decremented by `cost`, so `balance + cost ≤ previous_balance ≤ GOLD_CAP`. The compensating call always succeeds. Player receives an inventory-full error.

**EC-CS-6 — Session disconnect mid-transaction**
Player disconnects after `TrySpendGold` succeeds on the server but before `GoldSyncEvent` is received by the client. Server state is authoritative. On reconnect, the client calls `GetBalance` and receives the correct post-debit balance. The client does not re-apply the debit; it displays whatever the server reports.

**EC-CS-7 — Stale GoldSyncEvent (out-of-order delivery)**
Multiple `GoldSyncEvent` messages arrive out of order due to network reordering. The client checks the incoming event's `Version` against its cached `Version`. If incoming `Version ≤ cachedVersion`, the event is discarded as stale. Only events with `Version > cachedVersion` update the display state.

**EC-CS-8 — TransferGold stub called**
`TransferGold` is not implemented at MVP. Returns `NotImplemented`; no write. Any system calling `TransferGold` at runtime is a code error — log and alert.

**EC-CS-9 — CharacterID not found**
`AddGold` or `TrySpendGold` called with a `CharacterID` that does not exist in the server's active records. Return `CharacterNotFound`. For `AddGold` (a monster drop): the gold is lost; log server-side. For `TrySpendGold` (a purchase or respec): surface an error to the caller. This state should never occur in normal operation — it indicates a session management bug.

## Dependencies

### Upstream Dependencies (systems this GDD depends on)

**None.** Currency System is in the Foundation layer. It has no upstream dependencies.

*Types introduced here that downstream GDDs must reference:*
- `CharacterID` — `readonly struct wrapping uint` — introduced in this GDD; all systems that identify characters should use this type.
- `GOLD_CAP` — `uint = 9,999,999` — any system displaying a gold balance or validating drop amounts must reference this constant.

---

### Downstream Dependents (systems that depend on this GDD)

| System | GDD Status | How it depends on Currency System | Bidirectionality required |
|--------|-----------|-----------------------------------|--------------------------|
| NPC Shop (#23) | Not Started | Calls `TrySpendGold` (purchases) and compensating `AddGold` (failed grants). Sell-back is MVP-scope (OQ-CS-1 reversed 2026-05-15) — NPC Shop GDD defines sell-back UX; Inventory System calls `AddGold(ItemSell)`. | NPC Shop GDD must list Currency System in its Dependencies |
| Loot Table System (#8) | Not Started | Calls `AddGold` on each monster kill; must define drop amounts as integer values only (no floats) | Loot Table GDD must list Currency System in its Dependencies |
| Class/Leveling System (#10) | Not Started | Calls `TrySpendGold` for Respec costs; computes respec cost internally and passes as flat `uint` | Class/Leveling GDD must list Currency System in its Dependencies |
| Character Persistence (#25) | Not Started | Calls `GetBalance` on save; pushes loaded `Balance + Version` on load; emits `GoldSyncEvent` to client after load | Character Persistence GDD must list Currency System in its Dependencies |
| Enhancement System (#15) | Approved (2026-05-23) | No direct API dependency — interacts with Currency System indirectly through NPC Shop scroll purchases (`GoldTransactionReason.Enhancement = 5` pre-allocated). If Enhancement ever adds a direct gold cost (attempt surcharge), it will call `TrySpendGold` and must list this dependency at that time. | |
| HUD (#29) | Not Started | Consumes `GoldSyncEvent` (NewBalance, Reason) to display the player's current gold balance | HUD GDD must list Currency System in its Dependencies |
| Networking Core (#26) | Not Started | Session handshake must carry `CharacterID`, `Balance`, `Version` to client on connect/reconnect (EC-CS-6). `GoldSyncEvent.NewBalance` must be transmitted as an absolute value, not a delta (EC-CS-7). | Networking Core GDD must list Currency System in its Dependencies and document the session handshake contract |

---

*Bidirectionality flag: None of the above GDDs exist yet. When they are authored, each must include Currency System in their Dependencies section. This GDD already describes the interface contract for each relationship.*

**Note — Social Gravity pillar at MVP:** The Social Gravity pillar's economy dimension (server economies, player-to-player gold exchange) requires Trade System and gold transfer mechanics explicitly out of scope at MVP (Rule 8). At MVP, party economic benefits are delivered via XP and gold throughput (kill speed advantage with per-member diminishing return per kill — see party-system.md F-PS-1; same −10% per additional member model applies to gold) and elite zone loot access, not via currency exchange. There is no party gold drop bonus. Player-to-player gold transfer is a Full Vision feature — this is a deliberate scope cut, not a design gap.

## Tuning Knobs

### System-Owned Knobs

| Knob | Current Value | Safe Range | Gameplay Effect |
|------|--------------|-----------|----------------|
| `GOLD_CAP` | 9,999,999g | Fixed — not a tuning knob | Overflow guard and bot accumulation ceiling. This value is defined by the 7-digit HUD display field, not by economy calibration. The cap is unreachable in normal play — do not tune it for spend pressure. Functions: (1) prevents uint arithmetic overflow in F-CS-1; (2) caps the maximum gold a bot can accumulate without spending, creating a detection ceiling. Changing `GOLD_CAP` requires resizing the HUD gold display field and updating all ACs that reference the constant. |

---

### Cross-System Economy Invariants (not owned by Currency System — documented here as required constraints for downstream GDDs)

The Currency System has no gameplay logic for pricing or drop rates, but the health of the gold economy depends on these invariants being respected by downstream GDDs. Violating them during balance passes requires a coordinated review of all affected GDDs.

| Invariant | Rule | Owned By |
|-----------|------|---------|
| **Scroll cost ≈ 30–50 min same-tier farming** | Enhancement Scroll price at tier T should cost approximately 30–50 minutes of efficient solo farming in the same tier zone. This calibrates attempt weight — enough to make each attempt feel meaningful, not enough to make failure catastrophic. | NPC Shop GDD |
| **Sell-back price < monster drop rate** *(post-MVP)* | Deferred — sell-back is not a gold faucet at MVP (see OQ-CS-1 resolution). When NPC Shop GDD introduces sell-back, this invariant applies: sell-back yield per session must not exceed 20% of monster drop yield for the same zone/session. | NPC Shop GDD (when authored) |
| **Party gold bonus (if implemented)** | If Loot Table System implements a party gold bonus, it MUST be **additive, not multiplicative per member**. Recommended formula: `bonus = 1.0 + 0.1 × (party_size − 1)` (2-person: ×1.1; 4-person: ×1.3; values below ×1.1 are imperceptible; values above ×1.5 total risk undermining solo viability). Multiplicative per-member bonuses compound with `LevelTierMultiplier` and collapse enhancement attempt costs at end-game — a 4-person L60 party at ×1.5/member × ×2.0 tier = ×10.1 gold/hour, making enhancement attempts effectively free and destroying the Legendary Gear pillar. | Loot Table System GDD |
| **Farming yield × zone multiplier tracks `LevelTierMultiplier`** | Gold/kill rates should scale proportionally with the LevelTierMultiplier (×1.0/×1.2/×1.5/×2.0) across zone tiers. If a balance pass adjusts farming yields by X%, Enhancement Scroll prices must adjust by X% to preserve the scroll cost invariant. | Loot Table System GDD (yield) + NPC Shop GDD (prices) |

## Visual/Audio Requirements

*The Currency System is a server-side data layer. Visual and audio feedback are the responsibility of downstream systems (HUD, Loot Table, NPC Shop VFX) that consume `GoldSyncEvent`. This section documents what events the Currency System emits that trigger visual/audio responses.*

| Trigger | Event emitted | Responsible downstream system |
|---------|--------------|------------------------------|
| Monster kill gold drop | `GoldSyncEvent(Reason: MonsterDrop, NewBalance)` | Loot Table System VFX hook → floating "+Xg" text above monster |
| Scroll/consumable purchase | `GoldSyncEvent(Reason: ScrollPurchase, NewBalance)` | NPC Shop UI → purchase sound |
| Respec cost deducted | `GoldSyncEvent(Reason: RespecStat / RespecSkill, NewBalance)` | Class System UI → transaction confirmation |
| Gold cap reached (AddGold clamped) | Toast notification: "Gold pouch is full" | HUD toast system |

## UI Requirements

*Currency System is not a UI system. The following are requirements the Currency System imposes on HUD and shop UIs that consume its state.*

**HUD gold display:**
- Display format: `[gold icon] X,XXX,XXX` (comma-separated, 7-digit max at GOLD_CAP). Do not display decimal places — gold is always integer.
- Update trigger: `GoldSyncEvent` only. No speculative updates.
- At-cap indicator: When `NewBalance == GOLD_CAP`, display a visual indicator (e.g., gold text turns amber) to signal the player is at the ceiling.

**Shop/spend UI:**
- Confirm button must be disabled (or show InsufficientFunds state) while the balance is below the item cost — do not allow the tap, then fail after.
- After a purchase tap, disable the confirm button and show a spinner until `GoldSyncEvent` arrives. Do not show the new balance speculatively.
- If `TrySpendGold` returns `ConcurrencyConflict` after both attempts, surface: "Transaction failed — please try again."

## Acceptance Criteria

### Group A: Core Balance Operations

**AC-CS-A-01** — `AddGold(charId, 150)` on a character with balance=500: `GetBalance` returns 650. `GoldSyncEvent` fires with `NewBalance=650`, `Reason=MonsterDrop`.

**AC-CS-A-02** — `TrySpendGold(charId, 400)` on a character with balance=1,200: returns `Success`, `NewBalance=800`. `GetBalance` returns 800. `GoldSyncEvent` fires with `NewBalance=800`.

**AC-CS-A-03** — `TrySpendGold(charId, 400)` on a character with balance=300: returns `InsufficientFunds`. `GetBalance` still returns 300. No `GoldSyncEvent` emitted.

**AC-CS-A-04** — `AddGold(charId, 200)` on a character with balance=9,999,900: returns `Success`, `NewBalance=9,999,999`, `Error=None`. `GetBalance` returns 9,999,999. 101g discarded. Callers detect cap was hit from `result.NewBalance == GOLD_CAP`. `GoldSyncEvent` fires with `NewBalance=9,999,999`.

**AC-CS-A-05** — `AddGold(charId, GOLD_CAP)` on a character with balance=1: `GetBalance` returns 9,999,999. `NewBalance` never exceeds GOLD_CAP regardless of input size.

---

### Group B: Guard Validation

**AC-CS-B-01** — `AddGold(charId, 0)`: returns `InvalidAmount`. No write. `GetBalance` unchanged. No `GoldSyncEvent`.

**AC-CS-B-02** — `TrySpendGold(charId, 0)`: returns `InvalidAmount`. No write. `GetBalance` unchanged. No `GoldSyncEvent`.

**AC-CS-B-03** — `AddGold` or `TrySpendGold` with a `CharacterID` that does not exist in active records: returns `CharacterNotFound`. No write.

---

### Group C: Overflow Safety (uint arithmetic)

**AC-CS-C-01** — `AddGold(charId, 2)` on a character with balance=`GOLD_CAP - 1` (9,999,998): `GetBalance` returns 9,999,999. Does NOT return 0 or any value less than GOLD_CAP (no uint wrap). *(Tests F-CS-1 overflow guard.)*

**AC-CS-C-02** — `AddGold(charId, GOLD_CAP)` on a character with balance=0: `GetBalance` returns exactly 9,999,999 (not overflow, not less).

---

### Group D: Concurrency (Double-Spend Prevention)

**AC-CS-D-01** — Two concurrent `TrySpendGold(charId, 600)` calls on a character with balance=800 (combined cost 1,200 > 800): exactly one call returns `Success` with `NewBalance=200`; the other returns `InsufficientFunds` or `ConcurrencyConflict`. Final `GetBalance` returns exactly 200. Balance never goes below 0 and never shows a double-debit.

**AC-CS-D-02** — After a `ConcurrencyConflict` result, a retry `TrySpendGold` on the same character with sufficient balance: returns `Success`. Balance decremented correctly.

---

### Group E: Edge Case Behaviour

**AC-CS-E-01** — `TrySpendGold(charId, 1)` on a character with balance=0: returns `InsufficientFunds`. No write.

**AC-CS-E-02** — `TransferGold(fromId, toId, 100)`: returns `NotImplemented`. No write to either balance. `GetBalance` for both characters unchanged.

**AC-CS-E-03** — Compensating refund flow: call `TrySpendGold(charId, 400)` (Success, balance=800); simulate item-grant failure; call `AddGold(charId, 400)` as compensating transaction. `GetBalance` returns 1,200 (original balance fully restored). No uint overflow.

---

### Group F: GoldSyncEvent Emission

**AC-CS-F-01** — `GoldSyncEvent` is emitted after every successful balance mutation (`AddGold` or `TrySpendGold`). Fields: `CharacterID` matches caller, `NewBalance` matches post-operation `GetBalance`, `Version` = pre-operation `Version + 1`, `Reason` matches the `source` enum passed by caller.

**AC-CS-F-02** — `GoldSyncEvent` is NOT emitted when `AddGold` returns `InvalidAmount` or `CharacterNotFound`.

**AC-CS-F-03** — `GoldSyncEvent` is NOT emitted when `TrySpendGold` returns `InsufficientFunds`, `InvalidAmount`, or `CharacterNotFound`.

**AC-CS-F-04** — `GoldSyncEvent.Version` increments monotonically: after N sequential mutations to the same character's balance, the final `GoldSyncEvent.Version = initial_version + N`. No Version skips or repeats.

---

### Group G: Server Assembly Isolation

**AC-CS-G-01** — Static assembly scan: `CurrencySystem` class is present in `ServerLogic.asmdef`. It is absent from the client assembly manifest (confirmed via `UnityEditor.Compilation.CompilationPipeline` or assembly definition file inspection). No client build includes this class.

---

### Group H: State Machine

**AC-CS-H-01** — Balance=0 (Empty), `AddGold(1)`: balance transitions to Normal (0 < balance < GOLD_CAP).

**AC-CS-H-02** — Balance=`GOLD_CAP - 1` (Normal), `AddGold(1)`: balance transitions to AtCap (balance = GOLD_CAP = 9,999,999).

**AC-CS-H-03** — Balance=`GOLD_CAP` (AtCap), `TrySpendGold(1)`: balance transitions to Normal.

**AC-CS-H-04** — Balance=1 (Normal), `TrySpendGold(1)`: balance transitions to Empty (balance = 0).

**AC-CS-H-05** — Balance=`GOLD_CAP` (AtCap), `AddGold(any)`: balance remains `GOLD_CAP`. State does not change.

**AC-CS-H-06** — Balance=0 (Empty), `AddGold(GOLD_CAP)`: balance transitions directly to AtCap (balance = GOLD_CAP = 9,999,999). Returns `Success`, `NewBalance=GOLD_CAP`.

**AC-CS-H-07** — Balance=`GOLD_CAP` (AtCap), `TrySpendGold(GOLD_CAP)`: returns `Success`, `NewBalance=0`. Balance transitions directly to Empty.

---

### Group I: Session Resync

**AC-CS-I-01** — `TrySpendGold(charId, 400)` succeeds on server (balance 500→100, Version→V+1). Client disconnects before receiving `GoldSyncEvent`. On reconnect, session handshake delivers `Balance=100, Version=V+1` (or client calls `GetBalance` and receives the same). Client displays 100g. *(Tests EC-CS-6: server state is authoritative; client never shows pre-debit balance after reconnect.)*

**AC-CS-I-02** — Two `GoldSyncEvents` generated in sequence: V=6 (`NewBalance=1100`, `MonsterDrop`), V=7 (`NewBalance=1200`, `MonsterDrop`). Client receives V=7 first (out-of-order), updates to 1200g, `cachedVersion=7`. V=6 arrives; incoming `Version=6 ≤ cachedVersion=7` → event discarded. Final display: 1200g. *(Tests EC-CS-7: stale-discard prevents display regression under mobile packet reorder.)*

---

### Group J: Concurrent AddGold Atomicity

**AC-CS-J-01** — 4 concurrent `AddGold(charId, 100, GoldTransactionReason.MonsterDrop)` calls on a character with balance=1000 (simulating a party of 4 receiving monster drop gold simultaneously). After all 4 calls complete: `GetBalance` returns exactly 1400. Exactly 4 `GoldSyncEvents` emitted with unique incrementing Version values. No lost updates (balance ≠ 1100, 1200, or 1300). *(Tests Rule 11 atomic write: the normative party-kill scenario must not produce lost gold.)*

## Open Questions

**OQ-CS-1 — REVISED (2026-05-15): Sell-back restored to MVP**
Original resolution (2026-04-26): Sell-back cut from MVP to eliminate competing-faucet risk.
Revision: Inventory System design review (2026-05-15, Approved) reversed this decision — `SellItem` interface and AC-INV-16 added to Inventory System GDD; `GoldTransactionReason.ItemSell=7` restored to the wire enum. Sell yield is bounded by `ItemDefinition.SellPriceGold` (Item Database), keeping sell income well below monster drop yield for any farming session. The competing-faucet concern is mitigated by fixed sell prices (not auction-based). NPC Shop GDD must define the sell-back UX flow when authored; Inventory System owns the `AddGold(ItemSell)` call.

**OQ-CS-2 — Respec cost formula**
Currency System receives respec cost as a flat `uint` debit from the Class/Leveling System. The formula that computes this cost is not defined here. Two candidate approaches were identified during design: (A) flat fee per gear tier bracket; (B) `BaseRespecCost × LevelTierMultiplier × CharacterLevel` — self-adjusting, always costs a few sessions' worth of farming. The Class/Leveling System GDD must define and own this formula.

*Resolution owner: Class/Leveling System GDD. Non-blocking for Currency System implementation.*

**OQ-CS-3 — GoldSyncEvent transport layer**
This GDD specifies that `GoldSyncEvent` is emitted to the owning client connection after each balance mutation. The actual network delivery mechanism — who serializes the event, which channel it uses, how the client identifies the session connection — is not defined here. This requires a server-side event bus or RPC pattern that depends on the Networking Core GDD architecture.

**Constraint placed on Networking Core GDD:** (1) `GoldSyncEvent.NewBalance` MUST be serialized as an absolute balance value, never a delta — EC-CS-7's stale-discard logic depends on this. (2) The session-establishment handshake must carry current `CharacterID`, `Balance`, and `Version` for EC-CS-6 reconnect resync.

*Resolution owner: Networking Core GDD. Non-blocking for Currency System design.*
