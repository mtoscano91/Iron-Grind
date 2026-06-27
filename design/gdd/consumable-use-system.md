# Consumable Use System

> **Status**: Designed — In Review
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-06-09
> **Implements Pillar**: Earned Power (primary), Rhythm Mastery (secondary), Social Gravity (tertiary)

## Overview

The Consumable Use System is the execution layer for HP and MP potions during combat. When a player taps a potion on the in-combat hotbar, the system validates cooldown state and item availability, fires a server-authoritative use request, decrements the inventory stack via `ConsumeItem()`, and writes the restoration to Character Stats via `ApplyRegen()` or `ApplyManaRegen()` — without interrupting the auto-attack cadence. The system owns four concerns: the **hotbar assignment model** (which item type is pinned to which slot, and what happens when inventory changes), **use-from-hotbar execution** (the primary combat path), **use-from-bag execution** (direct use from the inventory screen without requiring hotbar assignment), and **per-type cooldown state** (HP and MP timers are independent — using an HP Potion does not block an MP Potion). Effect magnitudes and cooldown durations are read from `ConsumableData` at use-time; tuning potion values requires only Item Database changes, not changes to this system. MVP consumables are HP Potions (Small/Medium/Large) and MP Potions (Small/Medium/Large); the schema supports any future consumable type with a compatible `EffectType`.

## Player Fantasy

Every potion in your belt is gold you bled a hillside of monsters to afford — and the moment you tap it, that gold dissolves into your veins and is gone. You are not topping off a bar; you are spending, and the question that grips you when your health runs thin is never "can I heal?" but "is this fight worth what it will cost me to survive it?" A long grind is a war of attrition against your own coin purse, and the player who walks home with potions to spare fought smarter than the one who drank their profits.

Each tier is a different economic commitment. Small is the frequent bet — cheap, 20 seconds to reload, the working currency of attrition when you are chipping down a pull and need to stay above the danger line. Large is the statement: thirty gold pressed into a single emergency, worth it only when a Small will not bridge the gap. Reading which situation you are actually in — and spending the right tier for it — is the skill this system teaches. It does not reward timing reflexes; it rewards **resource judgment under pressure**.

The two-slot hotbar reflects this: you choose your plan before the session, and you live with the choice until you can resupply. When a slot greys empty, it is not a UI failure — it is feedback. You ran over budget. The next pull will test whether you can survive the outcome.

> **Pillar note:** Social Gravity integration (party healer interaction, MP economy in mixed parties) is a downstream design dependency. See Party System dependency. Resource display reconciliation with party healer HP governed by `resource-display-authority.md` (OQ-CUS-4 resolved 2026-06-09).

## Detailed Design

### Core Rules

**Rule 1 — Hotbar Structure**

1. The consumable hotbar has exactly 2 slots: Slot 0 (HP slot) and Slot 1 (MP slot). Each slot's `EffectType` constraint is set at construction and is immutable — Slot 0 accepts only `RestoreHP` items; Slot 1 accepts only `RestoreMP` items.
2. Each slot stores one `AssignedItemID: ItemID`. `ItemID.Invalid` = the slot is unassigned.
3. Assignment validation (applied on both client and server): `GetItem(newItemID).ConsumableData.EffectType == slot.SlotEffectType`. A consumable that does not match the slot type cannot be assigned to that slot.

**Rule 2 — Hotbar Assignment and Reassignment**

4. Initial assignment: in the Inventory screen, tapping a consumable opens the item detail view. The "Assign to Hotbar" action places the item in the matching slot type (HP → Slot 0, MP → Slot 1). If the slot already holds a different `ItemID`, the assignment is replaced.
5. Mid-session reassignment: long-pressing any hotbar slot opens a quick-picker showing all in-bag potions matching the slot's `EffectType`, with their current stack quantities. Tapping an entry reassigns the slot. A `[Clear]` option sets `AssignedItemID = ItemID.Invalid`. Reassignment is permitted at any time, including during active combat (long-press gesture is distinct from the tap-to-use gesture).
6. If the assigned potion type reaches zero inventory, the slot shows a greyed-out icon with a quantity badge of "0". Tapping fires a client-side "No potion" toast (no UseItemRequest is sent). The assignment is retained — when more potions of the same `ItemID` are picked up or purchased, the slot becomes active without requiring re-assignment.
7. Hotbar assignments are persisted as part of Character Persistence (saved to the character record on session end; restored on session load). Cooldown state is not persisted.

**Rule 3 — Use from Hotbar (Primary Path)**

8. A tap on a hotbar slot initiates the following sequence:
   - **Step 1 — Client pre-check**: If (a) `EffectTypeCooldownRemaining > 0` for this slot's type, **or** (b) the slot's target resource is already at maximum (`CurrentHP == MaxHP` for HP slots; `CurrentMP == MaxMP` for MP slots), reject locally: visual shake on slot; toast ("On cooldown" for (a); "Already at full HP" or "Already at full MP" for (b)). No UseItemRequest is sent.
   - **Step 2 — Client predict**: Start the client-side cooldown timer (`EffectTypeCooldownRemaining = ConsumableData.CooldownSeconds`) and grey out the slot immediately.
   - **Step 3 — UseItemRequest**: Client sends `UseItemRequest { requestId: uint, itemId: ItemID, quantity: 1 }` to the server. `requestId` is a monotonically increasing per-session counter; the server deduplicates on `(CharacterID, messageType, requestId)` within `SESSION_TTL_SECONDS` per ADR-001 Amendment A1 (OQ-CUS-3 resolved 2026-06-09). The `messageType` discriminator prevents collision with NPC Shop requestIds from the same character. On same-session reconnect within SESSION_TTL_SECONDS, the client seeds its counter from `SessionHandshake.lastSeenUseItemRequestId + 1` — not reset to 0 (OQ-CUS-1 resolved 2026-06-11; see EC-12 and CR-NET-6.4 in `networking-session.md`).
   - **Step 4 — Server validation**: Server checks: (a) `ZoneSessionState != Dead && ZoneSessionState != Respawning` (if dead/respawning → reject `UseItemRejected{CharacterDead}`, stop); (b) `HasItem(itemId)` (if false → reject `UseItemRejected{RejectedNoItem}`, stop); (c) server-side EffectType cooldown = 0 (if active → reject `UseItemRejected{RejectedOnCooldown}`, stop); (d) `ConsumableData.EffectType` match and `EffectMagnitude > 0` from Item Database; (e) `GetEffectiveStat(MaxHP) > 0` (RestoreHP) or `GetEffectiveStat(MaxMP) > 0` (RestoreMP) — if MaxResource = 0, reject `UseItemRejected{RejectedInvalidTarget}` and stop. Guard prevents consuming a potion against a character class with no resource pool for that type.
   - **Step 5 — Server execution** *(atomic — see note)*: `ConsumeItem(itemId, 1)` on Inventory (ItemID-based; the Inventory System selects the lowest-index slot internally per its FIFO contract — CUS passes only the ItemID, not a slot index); `ApplyRegen(entityId, EffectMagnitude)` (RestoreHP) or `ApplyManaRegen(entityId, EffectMagnitude)` (RestoreMP) on Character Stats; start server-side cooldown timer for the EffectType.
   - **Step 6 — UseItemResult**: Server sends `UseItemResult { requestId, itemId, newResourceValue: int, newInventoryQuantity: int }`. `newResourceValue` = `FloorToInt(NewCurrentHP/MP)` per CR-NET-7.2. `newInventoryQuantity` = **total count of the used ItemID across all inventory slots** (not a single stack count) — used for the hotbar quantity badge. Client updates HP/MP bar from the server-authoritative value.
   - **Step 6b — Server rejection**: If any validation step fails, server sends `UseItemRejected { requestId, reason: UseItemRejectedReason }`. Client immediately resets `EffectTypeCooldownRemaining` to 0, un-greys the slot, and shows a toast ("No potion" or "On cooldown").

> **Atomicity (Step 4(c)→Step 5):** The EffectType cooldown check and cooldown-set are an uninterruptible operation within server message dispatch. UseItemRequest messages from a single character are processed sequentially — no concurrent request from the same character can pass Step 4(c) after another request has already passed it but before Step 5 completes. This prevents double-use via rapid-fire requests with distinct requestIds.

**Rule 4 — Use from Bag (Secondary Path)**

9. From the Inventory screen, tapping a consumable → item detail view → "Use" action. The Use button is disabled (greyed) if the corresponding EffectType cooldown is active; tapping a disabled Use button shows a toast ("On cooldown") without sending UseItemRequest. If cooldown is 0, the action proceeds with the same UseItemRequest flow as Rule 3 starting at Step 2.
10. Hotbar assignment is not required to use from bag. Any potion type in possession may be used at any time, subject only to the per-EffectType cooldown.

**Rule 5 — Per-EffectType Cooldown**

11. Two independent runtime cooldown timers exist per character: `HPCooldownRemaining` and `MPCooldownRemaining`. Using any HP Potion (Small, Medium, or Large) starts the HP timer, set to `ConsumableData[consumedItemID].CooldownSeconds`. Using any MP Potion starts the MP timer. The two timers are fully independent — using an HP Potion does not affect the MP timer.
12. Both client and server maintain these timers independently. The client timer drives UI (greyed slot, cooldown countdown). The server timer is authoritative for rejection decisions.
13. Cooldown timers initialize to 0 on every zone entry (including disconnect/reconnect). Cooldown state is not persisted. A player reconnecting to the same zone instance (without a zone change) is treated as a zone entry for this purpose — the ZoneEntry signal fires on both client and server regardless of whether the zone changed.

**Rule 6 — Quantity**

14. Each use consumes exactly 1 unit. `ConsumeItem(itemId, quantity: 1)` is always called with quantity=1. There is no bulk-use mechanic in MVP.
15. The Inventory System decrements from the lowest slot index first (0–19 FIFO). If multiple stacks of the same `ItemID` exist, the lowest-index stack is decremented first.

**Rule 7 — Auto-Attack Cadence (No Interruption)**

16. Consumable use is parallel to the auto-attack cadence. Tapping a hotbar slot at any time does not interrupt, delay, or modify the scheduled auto-attack beat. From the combat tick's perspective, `UseItemRequest` is fire-and-forget. There is no animation lockout, no use-recovery period, and no beat restriction.

---

### States and Transitions

**Hotbar Slot State**

| State | Condition | Transitions |
|-------|-----------|-------------|
| **Unassigned** | `AssignedItemID = ItemID.Invalid` | → Assigned-Out-of-Stock or Assigned-Ready (via assignment action) |
| **Assigned — Out of Stock** | `AssignedItemID` valid; `HasItem = false`; `CooldownRemaining = 0` | → Assigned-Ready (pickup or purchase of matching ItemID); → Unassigned (Clear from quick-picker) |
| **Assigned — Ready** | `AssignedItemID` valid; `HasItem = true`; `CooldownRemaining = 0` | → Assigned-On-Cooldown (tap → UseItemRequest sent); → Assigned-Out-of-Stock (last unit consumed, quantity = 0); → Assigned-Ready with new ItemID (reassignment) |
| **Assigned — On Cooldown** | `AssignedItemID` valid; `HasItem = true`; `CooldownRemaining > 0` | → Assigned-Ready when `CooldownRemaining <= 0` (timer expires); → Assigned-Out-of-Stock+On-Cooldown when UseItemResult arrives with `newInventoryQuantity = 0`; → Assigned-Ready immediately on server rejection or zone entry |
| **Assigned — Out of Stock + On Cooldown** | `AssignedItemID` valid; `HasItem = false`; `CooldownRemaining > 0` | → Assigned-Out-of-Stock when `CooldownRemaining <= 0`. **Visual:** cooldown overlay (with countdown) takes priority until timer expires; slot then transitions to standard Out-of-Stock display. Does NOT transition to Assigned-Ready when cooldown expires — `HasItem = false` is the gating condition. |

> **Fifth-state note:** This compound state is entered when the player uses their last potion. The cooldown timer continues running; on expiry the slot becomes Out-of-Stock (no Ready flash). A purchase/pickup while in this state transitions to Assigned-Ready if `CooldownRemaining <= 0`, or Assigned-On-Cooldown if still active.

**Cooldown Timer State (per EffectType)**

| State | Condition | Transitions |
|-------|-----------|-------------|
| **Ready** | `CooldownRemaining = 0` | → On Cooldown (UseItemRequest fires) |
| **On Cooldown** | `CooldownRemaining > 0` | → Ready (`CooldownRemaining <= 0` after decrement via `Time.deltaTime` — use `<= 0` guard, not `== 0`); → Ready immediately (server rejection or zone entry) |

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Inventory System** | → calls | `HasItem(ItemID): bool`; `ConsumeItem(ItemID, quantity: 1)` | Server validation (HasItem) and execution (ConsumeItem) per Rule 3 |
| **Character Stats** | → calls | `ApplyRegen(EntityID, EffectMagnitude: float)` (HP); `ApplyManaRegen(EntityID, EffectMagnitude: float)` (MP) | After ConsumeItem succeeds; EffectMagnitude read from Item Database at use-time |
| **Item Database** | ← reads | `GetItem(ItemID)` → `ConsumableData.EffectType`, `ConsumableData.EffectMagnitude`, `ConsumableData.CooldownSeconds` | At use execution and at assignment validation |
| **Character Persistence** | ↔ | Hotbar slot assignments (`2 × { AssignedItemID }`) added to character save record; restored on load. `SlotEffectType` is **not persisted** — it is always derived from the slot index at load time (Slot 0 = RestoreHP, Slot 1 = RestoreMP). EC-10 validates AssignedItemID against the index-derived type on load. | Session end (save) and session start (load) |
| **HUD / Consumable UI** | ← subscribes | `HotbarStateChangedEvent { slotIndex, newState: HotbarSlotState, assignedItemID, cooldownRemaining, quantityInBag }` fired after any assignment change, use, or inventory mutation affecting an assigned ItemID | On assignment, on use, on `InventoryChangedEvent` for an assigned ItemID |
| **Networking Wire Protocol** | → messages | `UseItemRequest { requestId: uint, itemId: uint, quantity: byte }`; `UseItemResult { requestId, itemId, newResourceValue: int, newInventoryQuantity: int }`; `UseItemRejected { requestId, reason: UseItemRejectedReason }` | Registered in networking-wire-protocol.md (2026-06-09, OQ-CUS-2 resolved). **Note:** `newResourceValue` wire type is `int` (FloorToInt per CR-NET-7.2); game-logic type remains `float` internally. |

## Formulas

### F-CUS-1: HP Restoration

**Expression:** `NewCurrentHP = clamp(CurrentHP + EffectMagnitude, 0.0, MaxHP)`

| Symbol | Type | Range | Source |
|--------|------|-------|--------|
| `CurrentHP` | float | [0.0, MaxHP] | `CharacterStats` at use-time |
| `EffectMagnitude` | float | (0.0, ∞) | `ConsumableData.EffectMagnitude` from Item Database |
| `MaxHP` | float | [1.0, ∞) | `CharacterStats.GetEffectiveStat(MaxHP)` |
| `NewCurrentHP` | float | [0.0, MaxHP] | Written by `ApplyRegen()` — the only permitted write path |

**Output range:** [0.0, MaxHP]. Cannot go negative. Cannot exceed MaxHP (no overheal).

**Boundary guards:** `MaxHP ≥ 1.0` is an invariant enforced by Character Stats (never set below 1). `EffectMagnitude > 0` is validated at Step 4(d) — a data authoring error (EffectMagnitude = 0) is caught server-side and rejected before ConsumeItem is called. A use at `CurrentHP = MaxHP` is now blocked at Step 1 (client pre-check) — see Rule 3. The formula is never called with this condition in normal flow.

**Worked example:** L10 Warrior Tank at 310 / 770 HP uses HP Medium (EffectMagnitude = 220): `clamp(310 + 220, 0, 770) = 530`. NewCurrentHP = 530.

---

### F-CUS-2: MP Restoration

**Expression:** `NewCurrentMP = clamp(CurrentMP + EffectMagnitude, 0.0, MaxMP)`

Identical structure to F-CUS-1. Substitute `MaxMP` for `MaxHP` and `ApplyManaRegen()` for `ApplyRegen()`.

| Symbol | Type | Range | Source |
|--------|------|-------|--------|
| `CurrentMP` | float | [0.0, MaxMP] | `CharacterStats` at use-time |
| `EffectMagnitude` | float | (0.0, ∞) | `ConsumableData.EffectMagnitude` from Item Database |
| `MaxMP` | float | [0.0, ∞) | `CharacterStats.GetEffectiveStat(MaxMP)` |
| `NewCurrentMP` | float | [0.0, MaxMP] | Written by `ApplyManaRegen()` — the only permitted write path |

**Output range:** [0.0, MaxMP]. Same boundary behavior as F-CUS-1.

**MaxMP = 0 guard:** `MaxMP` range is [0.0, ∞) per Character Stats — it can be 0 for character classes with no MP pool. `clamp(0 + 80, 0, 0) = 0`: the potion would be consumed for zero effect. Server Step 4(e) guards against this: if `GetEffectiveStat(MaxMP) = 0`, the server rejects with `UseItemRejected{RejectedInvalidTarget}` before ConsumeItem is called. No gold is lost; no cooldown is started.

**Worked example:** L20 Healer at 200 / 1430 MP uses MP Large (EffectMagnitude = 500): `clamp(200 + 500, 0, 1430) = 700`. NewCurrentMP = 700.

---

### F-CUS-3: Gold Efficiency Index

**Expression:** `GoldEfficiency = BuyPrice / EffectMagnitude` (gold per restoration unit — lower is better)

This formula is not executed at runtime. It is a design validation tool showing the per-unit cost of each tier. **Small is the most gold-efficient tier by g/HP** (0.0375 < 0.0455 < 0.0600) — this is intended. The tiers are not designed to be equally efficient; they are designed to be **situationally dominant**: each tier is the best choice in its own combat scenario, not an equal choice in all scenarios.

| Potion | BuyPrice | SellPrice | EffectMagnitude | CooldownSeconds | GoldEfficiency |
|--------|----------|-----------|-----------------|-----------------|----------------|
| HP Potion Small | 3g | 2g | 80 HP | 20s | 0.0375 g/HP |
| HP Potion Medium | 10g | 6g | 220 HP | 30s | 0.0455 g/HP |
| HP Potion Large | 30g | 18g | 500 HP | 45s | 0.0600 g/HP |
| MP Potion Small | 3g | 2g | 80 MP | 20s | 0.0375 g/MP |
| MP Potion Medium | 10g | 6g | 220 MP | 30s | 0.0455 g/MP |
| MP Potion Large | 30g | 18g | 500 MP | 45s | 0.0600 g/MP |

> **Sell-back ratios:** Small = 2/3 = 66.7%; Medium = 6/10 = 60.0%; Large = 18/30 = 60.0%. Small has a slightly higher recovery ratio. This asymmetry is documented; do not adjust values to equalize without re-checking the anti-arbitrage formula (F-NS-3).

**Situational tier model — who buys what and why:**

| Tier | Rational use case | Dominant at | Not rational when |
|------|------------------|-------------|-------------------|
| Small (80 HP, 20s, 3g) | Top-off at end of pull; emergency tap when deficit ≤ 80 HP; budget play | Any HP deficit ≤ 80 HP; maximum gold efficiency always | Deficit > 80 HP and cooldown pressure is low — Medium heals more per 30g |
| Medium (220 HP, 30s, 10g) | Standard between-pull heal at L10+ (200–350 HP deficit); default working-session tier | L10 standard play (Warrior at 770 MaxHP, typical pull damage 200–300 HP) | HP deficit < 80 HP (wasteful overheal); HP deficit > 500 HP and in genuine danger (Large closes faster) |
| Large (500 HP, 45s, 30g) | Emergency recovery from >350 HP deficit; burst heal when one tap must close a large gap | HP deficit > 350 HP; situations where you cannot wait for two Small/Medium cooldowns | L1 (always overcaps 400 MaxHP — pays 30g for up to 400 HP of value); routine between-pull heals at any level (Medium is more efficient per gold actually healed) |

> **L1 note:** HP Large (500 HP) always overcaps L1 MaxHP (400). A player at 100 HP at L1 gets 300 HP of value for 30g vs 3× HP Small for 9g (also 240 HP of value). HP Large at L1 is economically irrational — consider not stocking it at the NPC shop until L6+ (when Warrior MaxHP first exceeds 400 HP).
>
> **MP note for Warriors:** Warriors have MaxMP ~270–350 throughout L1–20. MP Large (500 MP) always overcaps them (identical output to MP Medium at 3× the price). MP Small and MP Medium are the rational Warrior tiers. MP Large is a Healer-primary item; the NPC shop may filter display by class if the trap-purchase rate is high.
>
> **Calibration target:** all three HP tiers should be purchased at every level band. Monitor ratios: if Large > 40% of HP potion purchases below L10, the L1 overprice signal is working against you — cap Large availability. If Small > 70% at L15+, Medium and Large may need cooldown adjustments to create meaningful pull-size scenarios.

**Spend-window comparison (90 seconds — maximum throughput):** Small yields 4 uses × 80 = 320 HP/MP for 12g. Medium yields 3 uses × 220 = 660 HP/MP for 30g. Large yields 2 uses × 500 = 1,000 HP/MP for 60g. These figures assume the player is continuously below MaxHP for the full window. In episodic combat (discrete pulls with recovery between them), actual HP restored per tier is capped by the HP deficit at time of use — at L10 with 250 HP damage per pull, Medium and Large can deliver identical actual healing while costing 2–3× as much.

**Per-level restore % (HP path):**

| Potion | L1 MaxHP 400 | L10 Warrior 770 HP | L20 Warrior 1,392 HP |
|--------|--------------|--------------------|----------------------|
| Small (80 HP) | 20% | 10.4% | 5.7% |
| Medium (220 HP) | 55% | 28.6% | 15.8% |
| Large (500 HP) | capped (full) | 64.9% | 35.9% |

**Per-level restore % (MP path — Healer, binding case):**

| Potion | L1 MaxMP 220 | L10 Healer 650 MP | L20 Healer 1,430 MP |
|--------|--------------|-------------------|---------------------|
| Small (80 MP) | 36% | 12.3% | 5.6% |
| Medium (220 MP) | capped | 33.8% | 15.4% |
| Large (500 MP) | capped | 76.9% | 35.0% |

> **Class note:** Warriors have MaxMP ~270–350 throughout L1–20. MP Large (500 MP) always overcaps them, making MP Small and MP Medium the natural Warrior spend tier. Healers follow the same Large-scaling path for MP that Warriors follow for HP. This asymmetry emerges from the same value set with no class-specific authoring.

---

### CooldownSeconds — Authored Constants

`CooldownSeconds` is not a computed formula. It is an authored `float` constant on each item's `ConsumableData` record in the Item Database. Rule 5 reads `ConsumableData[consumedItemID].CooldownSeconds` at use-time and assigns it to `HPCooldownRemaining` or `MPCooldownRemaining`. The 20s/30s/45s values above are the starting authored defaults; tuning them requires only an Item Database data change, not a Consumable Use System logic change.

> **Pillar alignment:** Small's 20-second cooldown aligns with exactly 20 auto-attack beats at the 20Hz tick rate, tying the budget potion's rhythm into the Rhythm Mastery cadence.

> **Resolves Item Database OQ-5** (CooldownSeconds untuned): this section locks the authored CooldownSeconds defaults for all 6 MVP potion types. Item Database records must be updated to reflect these values before the implementation sprint.

## Edge Cases

**EC-1: Double-tap before client predict settles**
The player taps the hotbar twice within one frame. The first tap starts the client predict (Step 2), setting `HPCooldownRemaining = CooldownSeconds`. The second tap hits Step 1 (`HPCooldownRemaining > 0`) and is rejected locally — no second UseItemRequest reaches the server.

**EC-2: Server rejects after client predict (state mismatch)**
The client has already started the cooldown timer and greyed the slot (Step 2), then receives `UseItemRejected` (Step 6b). The client resets `HPCooldownRemaining` to 0, un-greys the slot, and shows a toast. The item was not consumed (ConsumeItem was never called). The client's inventory count is not locally modified by the predict step — it waits for `UseItemResult.newInventoryQuantity`.

**EC-3: Disconnect during pending request**
UseItemRequest was sent; connection drops before UseItemResult arrives. The server may or may not have processed the request. On reconnect, full character state is restored from server persistence — inventory count and HP/MP are authoritative. No duplicate or lost item results regardless of server processing state at time of disconnect.

**EC-4: Zone change while UseItemRequest is in-flight**
Identical resolution to EC-3. Server processes the request (or does not); client receives authoritative state from server persistence on the new zone load.

**EC-5: Hotbar slot reassigned while UseItemResult is in-flight**
The player long-presses to reassign Slot 0 between UseItemRequest and UseItemResult. The arriving UseItemResult carries its original `requestId` and `itemId`. The client matches `requestId` to the pending request and applies `newResourceValue` to the HP/MP bar regardless of the slot's current `AssignedItemID`. The response is not discarded because the slot was reassigned.

**EC-6: Last unit consumed**
`HasItem` passes (quantity = 1). Server calls `ConsumeItem` — quantity becomes 0. `UseItemResult` carries `newInventoryQuantity = 0`. If `CooldownRemaining = 0` at this moment, the slot transitions Assigned-Ready → Assigned-Out-of-Stock. If `CooldownRemaining > 0` (the typical case — the use just started the cooldown), the slot enters the **Assigned-Out-of-Stock + On-Cooldown** fifth state (see State table). When the cooldown expires in this state, it transitions to Assigned-Out-of-Stock, not Assigned-Ready. The `AssignedItemID` is retained; when more units of the same `ItemID` are picked up or purchased, the slot returns to Assigned-Ready (or Assigned-On-Cooldown if still cooling down) without user action.

**EC-7: Use-from-bag while hotbar HP cooldown is active**
Both paths share the server-side HP cooldown timer. A UseItemRequest initiated from the bag while the HP cooldown is active is rejected with `RejectedOnCooldown`. The bag tap does not reset or bypass the client-side cooldown timer, which was set by the original hotbar use.

**EC-8: UseItemRequest received while character is dead**
The client disables hotbar and bag potion interaction in Dead and Respawning states (Death & Respawn GDD responsibility). However, a UseItemRequest may arrive at the server in a race condition where character death was processed between tap and receipt. Server Step 4 checks `ZoneSessionState != Dead && ZoneSessionState != Respawning` first, before any other validation. If the character is dead or respawning, the server rejects with `UseItemRejected(CharacterDead)`. `ConsumeItem` and `ApplyRegen`/`ApplyManaRegen` are not called.

**EC-9: Reconnect to same zone — cooldown reset**
Rule 5 states cooldowns reset on every zone entry including reconnect. A player reconnecting to the same zone instance (Disconnected_SessionActive → Connected transition in Zone Instancing) triggers the ZoneEntry signal on both client and server. Both `HPCooldownRemaining` and `MPCooldownRemaining` reset to 0. "Zone entry" explicitly includes same-zone reconnect, not only zone-change events.

> **EC-3 / EC-9 co-dependency:** These two edge cases together cover full state recovery after a disconnect. EC-3 handles inventory count and HP/MP (restored from authoritative server persistence). EC-9 handles cooldown state (reset to 0 by ZoneEntry — cooldown is not persisted by design). The combination guarantees a consistent and safe post-reconnect state: character values are authoritative, cooldowns are reset to 0 (conservative — the player may use immediately). A test covering the compound case (use in-flight at disconnect, same-zone reconnect) must verify both: (a) HP and inventory match server state, and (b) cooldowns are 0 on reconnect. See AC-CUS-18.

**EC-10: AssignedItemID invalid at hotbar load**
On session restore, the Consumable Use System's hotbar initialization path validates each slot: (a) `GetItem(assignedItemID)` must return a non-null item; (b) `ConsumableData.EffectType` must match `slot.SlotEffectType`. If either check fails, `AssignedItemID` is set to `ItemID.Invalid` (Unassigned) and a warning is logged. Check (b) covers a patch that changes an item's EffectType without removing it from the database. Character Persistence deserializes raw bytes only; this validation is owned by this system's load path.

**EC-11: Use-from-bag on an item also assigned to the hotbar**
UseItemRequest fires normally. `ConsumeItem` decrements the stack. `UseItemResult.newInventoryQuantity` is returned. The Consumable Use System observes `InventoryChangedEvent` for all ItemIDs currently assigned to hotbar slots. If `newInventoryQuantity = 0`, the slot transitions to Assigned-Out-of-Stock; otherwise the quantity badge updates. No special case needed.

**EC-12: requestId counter continuity across reconnect**
The `requestId` is a per-session monotonic `uint` counter. On same-session reconnect within SESSION_TTL_SECONDS, the client does not reset to 0 — it seeds its counter from `SessionHandshake.lastSeenUseItemRequestId + 1` (CR-NET-6.4 step 2 in `networking-session.md`). The server echoes the highest `UseItemRequest.requestId` it processed before the disconnect; the client resumes counting from the next unused value. This ensures no reconnect-session request can collide with a SESSION_TTL-scoped dedup cache entry from the pre-disconnect session. Counter resets to 0 only on a new game session (post SESSION_TTL expiry) or first entry into a new zone with no prior session history. *OQ-CUS-1 resolved 2026-06-11 — CR-NET-6.4 amended in networking-session.md; UseItemRequest comment updated in networking-wire-protocol.md.*

**EC-13: Potion tap at full HP or MP**
A character at `CurrentHP = MaxHP` tapping an HP Potion is **blocked at Step 1 (client pre-check)**: condition (b) fires, visual shake plays, "Already at full HP" toast is shown. No UseItemRequest is sent, no cooldown starts, no item is consumed. The same guard applies for `CurrentMP = MaxMP` on MP Potions. This is consistent with the double-tap guard at condition (a): both protect against accidental waste from a mobile UI interaction. The server does not need a corresponding guard for this case — the client pre-check is the authoritative gate.

**EC-14: MP potion used by a character with MaxMP = 0**
A character class with no MP pool (`GetEffectiveStat(MaxMP) = 0`) may have an MP potion in inventory (e.g., looted from a monster). If they attempt to use it (via hotbar or bag), Step 4(e) on the server rejects the request: `UseItemRejected{reason: RejectedInvalidTarget}`. `ConsumeItem` is not called (item is not consumed), no cooldown starts. The client receives UseItemRejected, resets cooldown predict to 0, and shows a rejection toast. Gold is not spent.

**EC-15: UseItemResult arrives for a slot now in Assigned-Out-of-Stock**
Concurrent path: UseItemRequest was sent for an item also used from the bag (EC-11), and the bag use consumed the last unit before the hotbar's UseItemResult arrives. UseItemResult is matched by `requestId` and its HP/MP update is applied normally. However, `newInventoryQuantity = 0` — the slot is already in Assigned-Out-of-Stock or Assigned-Out-of-Stock+On-Cooldown. The HP/MP bar update applies regardless of slot state; slot state is not modified by a late UseItemResult if the slot has already transitioned away from Assigned-On-Cooldown.

## Dependencies

### Upstream Dependencies (this system depends on)

| System | GDD | Interface Used | Notes |
|--------|-----|----------------|-------|
| **Inventory System** | `inventory-system.md` ✓ | `HasItem(ItemID): bool`; `ConsumeItem(ItemID, int)` | Server validation and execution path. Inventory System GDD must list Consumable Use System as a downstream caller of ConsumeItem. |
| **Character Stats** | `character-stats.md` ✓ | `ApplyRegen(EntityID, float)`; `ApplyManaRegen(EntityID, float)`; `GetEffectiveStat(MaxHP/MaxMP)` | Only permitted write paths for HP/MP. Character Stats GDD must list Consumable Use System as a caller. |
| **Item Database** | `item-database.md` ✓ | `GetItem(ItemID)` → `ConsumableData { EffectType, EffectMagnitude, CooldownSeconds, StackLimit }` | Read at use-time and at assignment validation. Item Database GDD holds all tuning values; this system reads them, never writes them. |
| **Character Persistence** | `character-persistence.md` ✓ | Hotbar state (2 × `{ SlotEffectType, AssignedItemID }`) added to character save/load record | Hotbar assignments persisted; cooldown state is not. Character Persistence GDD must include hotbar state in its save schema. |
| **Death & Respawn** | `death-and-respawn.md` ✓ | `ZoneSessionState` enum values (`Dead`, `Respawning`) read in server Step 4 (EC-8) | Client also reads dead state to disable hotbar input. Death & Respawn GDD must list Consumable Use System as a ZoneSessionState consumer. |
| **Zone Instancing** | `zone-instancing.md` ✓ | `ZoneEntry` signal — triggers cooldown reset to 0 on both client and server (including same-zone reconnect) | Zone Instancing GDD must list Consumable Use System as a ZoneEntry subscriber. |
| **Networking Wire Protocol** | `networking-wire-protocol.md` ✓ | `UseItemRequest`, `UseItemResult`, `UseItemRejected` message schemas | Registered 2026-06-09 (OQ-CUS-2 resolved). Schemas in wire protocol CUS section. Dedup key `(CharacterID, messageType, requestId)` per ADR-001 Amendment A1 (OQ-CUS-3 resolved 2026-06-09). |
| **Party System** | `party-system.md` ✓ | Concurrent HP restoration events from party healer may arrive alongside `UseItemResult`; Resource Display Authority contract governs reconciliation | Downstream interaction only in current MVP — CUS does not call into Party System. Resource Display Authority contract at `resource-display-authority.md` (OQ-CUS-4 resolved 2026-06-09). |

### Downstream Dependents (systems that depend on this one)

| System | GDD | What it expects | Notes |
|--------|-----|-----------------|-------|
| **HUD / Consumable UI** | *(not yet authored)* | `HotbarStateChangedEvent { slotIndex, assignedItemID, cooldownRemaining, quantityInBag }` | UI subscribes to this event for all slot rendering. UI GDD must reference this event contract. |
| **Networking Wire Protocol** | `networking-wire-protocol.md` ✓ | UseItemRequest/Result/Rejected message schemas | Registered 2026-06-09 (OQ-CUS-2 resolved). Schemas in networking-wire-protocol.md Consumable Use System section. |

### Bidirectionality Status

| Dependency GDD | CUS listed as dependent? | Action needed |
|----------------|--------------------------|---------------|
| inventory-system.md | To verify | Add Consumable Use System to Interactions table as a `ConsumeItem` caller |
| character-stats.md | To verify | Add Consumable Use System to Interactions table as an `ApplyRegen`/`ApplyManaRegen` caller |
| item-database.md | Partial (OQ-5 references CUS) | Confirm `referenced_by` in entities.yaml for ConsumableData fields includes consumable-use-system |
| character-persistence.md | To verify | Add hotbar slot data to save schema and interactions table |
| death-and-respawn.md | To verify | Add CUS as a ZoneSessionState consumer |
| zone-instancing.md | To verify | Add CUS as a ZoneEntry subscriber |

*Bidirectionality verification is a propagation check target before the implementation sprint.*

## Tuning Knobs

### EffectMagnitude — HP Potions

| Knob | Default | Safe Range | Gameplay Effect |
|------|---------|-----------|-----------------|
| HP Small — EffectMagnitude | 80 HP | [30, 200] | Attrition heal depth. Below 30 feels negligible at L1; above 200 makes Small restore >50% of L1 MaxHP, reducing Large's value proposition. |
| HP Medium — EffectMagnitude | 220 HP | [100, 400] | Mid-tier patch heal. Must remain between Small×1.5 and Large×0.6 to avoid making Medium dominant at any level band. |
| HP Large — EffectMagnitude | 500 HP | [300, 900] | Emergency heal. At L20 Warrior (1,392 HP) = 35.9%. Above 900 HP risks trivializing L10 encounters (770 MaxHP). |

### EffectMagnitude — MP Potions

| Knob | Default | Safe Range | Gameplay Effect |
|------|---------|-----------|-----------------|
| MP Small — EffectMagnitude | 80 MP | [30, 180] | Budget MP restore. 80 MP = 12.3% of L10 Healer MaxMP; overcaps Warriors below L15. |
| MP Medium — EffectMagnitude | 220 MP | [100, 400] | Workhorse MP restore for Healers at L10–L20. |
| MP Large — EffectMagnitude | 500 MP | [300, 800] | Bulk MP restore; always overcaps Warriors (intended — see Section D class note). Above 800 MP risks making Healers feel unlimited MP at L20 (1,430 MaxMP). |

### CooldownSeconds

| Knob | Default | Safe Range | Gameplay Effect |
|------|---------|-----------|-----------------|
| Small — CooldownSeconds | 20s | [10, 30] | Small's refresh rate. 20s = exactly 20 auto-attack beats at 20Hz (Rhythm Mastery alignment). Below 10s enables rapid-fire abuse; above 30s makes Small feel identical to Medium. |
| Medium — CooldownSeconds | 30s | [20, 45] | Must remain between Small and Large to preserve tier differentiation. |
| Large — CooldownSeconds | 45s | [35, 90] | Large's use frequency. 45s = 2 uses per 90-second window. Raising to 90s makes Large a per-encounter resource; lowering toward 35s erodes the throughput premium of Medium. |

> HP and MP potions share the same default EffectMagnitude and CooldownSeconds per size tier. They can be tuned independently per item record in the Item Database without any code change.

### Hotbar Slot Count (design constant)

| Knob | Value | Notes |
|------|-------|-------|
| Slot count | 2 (fixed) | Slot 0 = RestoreHP, Slot 1 = RestoreMP. Not a runtime tuning knob — changing the count requires code and UI changes. |

> **How to tune:** All EffectMagnitude and CooldownSeconds values are authored in Item Database `ConsumableData` records. No Consumable Use System code change is required for balance tuning.
>
> **Playtesting signal:** A healthy distribution has all three tiers purchased at every level band. If HP Large captures >70% of purchases below L15, tighten its cooldown toward 60s or reduce EffectMagnitude. If HP Medium falls below 15% of total consumable sales, it is the trap option — adjust Medium cooldown to 25s or raise EffectMagnitude to 260 HP.

## Visual/Audio Requirements

**Visual feedback — minimum requirements:**
- HP bar smooth lerp to `newResourceValue` over ~300ms on UseItemResult receipt
- Hotbar slot grey-out on cooldown start; un-greys when cooldown reaches 0
- Cooldown countdown timer displayed on greyed slot (seconds remaining, whole number)
- Quantity badge on each slot showing current stack count; turns red at quantity = 1; shows "0" at quantity = 0 (slot shows greyed icon + "0" badge — badge does not disappear so players can distinguish Out-of-Stock from On-Cooldown)
- Visual shake on slot when tap is rejected by client pre-check (cooldown active)

**Audio — minimum requirements:**
- Distinct consumption sound per potion tier (Small/Medium/Large); HP and MP sounds should feel different in character (HP = physical, MP = arcane)
- Rejection sound (short, low-energy) on local cooldown reject or server UseItemRejected
- No audio on cooldown expiry (timer is passive)

> ⚠️ Full visual/audio spec flagged for audio-director and art-director pass before UI implementation sprint. Exact asset specs, animation curves, and sound file references are out of scope for this GDD.

## UI Requirements

**Hotbar layout:**
- 2 slots fixed in the HUD, positioned within thumb-reach zone in landscape hold (lower right quadrant for right-thumb access; final position by UX designer)
- Minimum tap target: 44dp × 44dp per slot (iOS Human Interface Guidelines)
- Slots are always visible in combat; hidden or collapsed on non-combat screens (TBD by UX designer)

**Slot states (visual):**
- Assigned-Ready: potion icon + quantity badge; full opacity
- Assigned-On-Cooldown: potion icon at 50% opacity + cooldown seconds remaining overlay + grey tint
- Assigned-Out-of-Stock: potion icon at 30% opacity + "0" badge; tapping fires a client-side "No potion" toast (no UseItemRequest sent)
- Unassigned: empty slot with "+" indicator; tapping opens Inventory screen

**Long-press quick-picker:**
- Long-press threshold: ≥400ms to distinguish from tap-to-use. **Dead zone behavior:** a press released before 400ms is treated as a tap-to-use (UseItemRequest fires if cooldown = 0); the picker does not open. No ambiguous "neither" state. *(Note: UX review recommends raising this threshold to 500–600ms to reduce false positives during active combat — see UX designer flag below.)*
- Shows in-bag potions matching slot's EffectType with quantity counts; [Clear] option at bottom
- Dismissible by tapping outside the picker overlay
- Usable during active combat (Rule 2)

**Bag "Use" button:**
- Disabled (greyed, not interactive) when matching EffectType cooldown is active
- Shows cooldown seconds remaining in button label or tooltip when disabled
- Enabled when cooldown = 0

> ⚠️ 📌 UI spec flagged for UX designer review via `/ux-design` before implementation sprint. Touch gesture tuning, safe area inset placement, and exact HUD anchor points are out of scope for this GDD.

## Acceptance Criteria

**AC-CUS-01 — Slot 0 rejects MP potion assignment**
Given Slot 0 is empty, when a RestoreMP consumable is assigned to Slot 0, then Slot 0.AssignedItemID = ItemID.Invalid (assignment rejected).
*Test type: Unit*

**AC-CUS-02 — Slot 1 rejects HP potion assignment**
Given Slot 1 is empty, when a RestoreHP consumable is assigned to Slot 1, then Slot 1.AssignedItemID = ItemID.Invalid (assignment rejected).
*Test type: Unit*

**AC-CUS-03 — Initial hotbar assignment via inventory screen**
Given Slot 0 is unassigned and HP Small is in inventory, when the player taps HP Small and selects "Assign to Hotbar," then Slot 0.AssignedItemID = HP Small's ItemID.
*Test type: Integration*

**AC-CUS-04 — Mid-combat reassignment via long-press quick-picker**
Given Slot 0 is assigned HP Small during active combat, when the player long-presses Slot 0 and selects HP Medium from the quick-picker, then Slot 0.AssignedItemID = HP Medium's ItemID.
*Test type: Integration*

**AC-CUS-05 — Slot cleared via quick-picker [Clear] option**
Given Slot 0 is assigned HP Small, when the player long-presses Slot 0 and selects [Clear], then Slot 0.AssignedItemID = ItemID.Invalid.
*Test type: Integration*

**AC-CUS-06 — Hotbar use applies correct HP gain**
Given CurrentHP = 100, MaxHP = 500, Slot 0 = HP Small (EffectMagnitude = 80), HP cooldown = 0, when the player taps Slot 0 and UseItemResult is received, then newResourceValue = 180 and client HP display = 180.
*Test type: Integration*

**AC-CUS-07 — Hotbar use starts per-EffectType cooldown only**
Given HP Small is consumed from Slot 0 (let C = HP Small's `CooldownSeconds` read from Item Database at test setup), HP CooldownRemaining = 0, MP CooldownRemaining = 0 before the tap, and UseItemResult is received, then HP CooldownRemaining = C, SlotState[0] = AssignedOnCooldown, and MP CooldownRemaining = 0.
*Test type: Integration*
*Note: CooldownSeconds is read from Item Database at test setup — not hardcoded. If HP Small's cooldown changes during balance tuning, this test updates automatically.*

**AC-CUS-08 — Client pre-check: active cooldown suppresses tap with no server call**
Given HP CooldownRemaining = 10, when the player taps Slot 0, then no UseItemRequest is sent to the server and CooldownRemaining = 10 (unchanged).
*Test type: Unit*

**AC-CUS-09 — Server rejects use while character is dead (EC-8)**
Given ZoneSessionState = Dead, when the server receives UseItemRequest, then server returns UseItemRejected { reason: CharacterDead } and inventory quantity is unchanged.
*Test type: Server-unit*

**AC-CUS-10 — Server rejects use when item quantity is 0**
Given inventory quantity for the requested itemId = 0, when the server receives UseItemRequest, then server returns UseItemRejected { reason: RejectedNoItem } and inventory is unchanged.
*Test type: Server-unit*

**AC-CUS-11 — Server rejection resets client cooldown to 0**
Given the client predicted a use and set CooldownRemaining = T, when UseItemRejected is received for any reason, then client CooldownRemaining = 0.
*Test type: Integration*

**AC-CUS-12 — Use-from-bag activates shared HP cooldown**
Given HP cooldown = 0 and HP Medium is in inventory (not hotbar-assigned), when HP Medium is used from the bag and UseItemResult is received, then HP cooldown = 30.
*Test type: Integration*

**AC-CUS-13 — Active HP cooldown blocks bag use client-side (no request sent)**
Given HP CooldownRemaining > 0, when the player taps "Use" on a RestoreHP item in the item detail view, then no UseItemRequest is sent to the server.
*Test type: Unit*
*Note: Requires injectable network sender (verify DI setup before writing test). "Toast is displayed" is a separate UI advisory story.*

**AC-CUS-13b — Active HP cooldown shows feedback on bag use attempt**
Given HP CooldownRemaining > 0, when the player taps "Use" on a RestoreHP item in the item detail view, then a "On cooldown" toast visible to the player is displayed.
*Test type: Advisory/UI — Evidence: screenshot in `production/qa/evidence/`. Manual sign-off required.*

**AC-CUS-14 — HP cooldown does not block MP use**
Given HP CooldownRemaining = 15 and MP cooldown = 0, when the player taps Slot 1 (RestoreMP), then UseItemRequest is sent.
*Test type: Unit*

**AC-CUS-15 — MP cooldown does not block HP use**
Given MP CooldownRemaining = 10 and HP cooldown = 0, when the player taps Slot 0 (RestoreHP), then UseItemRequest is sent.
*Test type: Unit*

**AC-CUS-16 — Slot returns to Assigned-Ready after cooldown expires (HasItem = true)**
Given Slot 0 is in SlotState = AssignedOnCooldown with HP CooldownRemaining = C (read from test setup) and HasItem = true, when CooldownRemaining decrements to ≤ 0, then SlotState[0] = AssignedReady.
*Test type: Integration*
*Note: Uses injectable clock or `Time.deltaTime` fast-forward. Guard is `<= 0`, not `== 0`.*

**AC-CUS-17 — Hotbar assignments persist across session end and start**
Given Slot 0 = HP Small and Slot 1 = MP Small at session end, when a new session starts, then Slot 0.AssignedItemID = HP Small's ItemID and Slot 1.AssignedItemID = MP Small's ItemID.
*Test type: Integration*

**AC-CUS-18 — Cooldowns initialized to 0 on zone entry and same-zone reconnect**
Given HP cooldown = 15 and MP cooldown = 25, when the player enters a zone or reconnects (including same-zone reconnect), then HP cooldown = 0 and MP cooldown = 0.
*Test type: Integration*

**AC-CUS-19 — Potion use does not delay the next auto-attack**
Given a deterministic test harness with injectable clock and `IAutoAttackScheduler` exposing `NextFireTime: long` and `AttackIntervalTicks: long` as readable properties, auto-attack `scheduler.NextFireTime = T` and `scheduler.AttackIntervalTicks = I` (both readable), when UseItemResult is received at any time `T - X` where `0 < X < I`, then `scheduler.NextFireTime = T` (unchanged, exact match) and `scheduler.AttackIntervalTicks = I` (unchanged).
*Test type: Integration*
*Pre-condition: Requires `IAutoAttackScheduler` to expose `NextFireTime` and `AttackIntervalTicks` as readable properties. Flag as testability dependency for lead-programmer before sprint start — if this interface does not exist, the AC cannot be verified.*

**AC-CUS-20 — EC-13 (updated): potion tap at full HP is blocked client-side**
Given CurrentHP = MaxHP = 500, HP CooldownRemaining = 0, Slot 0 = HP Small, when the player taps Slot 0, then no UseItemRequest is sent to the server, SlotState[0] remains AssignedReady, and HP CooldownRemaining = 0 (no cooldown started).
*Test type: Unit*

**AC-CUS-20b — Server clamp: heal overflow capped at MaxHP (overcap path)**
Given CurrentHP = 450, MaxHP = 500, HP Small (EffectMagnitude = 80), HP cooldown = 0, when UseItemRequest is sent directly (bypassing client pre-check, e.g. from bag use where client MaxHP knowledge may be stale), then server returns UseItemResult with newResourceValue = 500 (clamped, not 530) and newInventoryQuantity decremented by 1.
*Test type: Server-unit*

**AC-CUS-21 — Formula clamp: heal overflow capped at MaxHP**
Given CurrentHP = 450 and MaxHP = 500, when HP Small (EffectMagnitude = 80) is applied, then newResourceValue = 500 (not 530).
*Test type: Server-unit*

**AC-CUS-22 — FIFO: lowest inventory slot index consumed first**
Given HP Small occupies inventory slots 3 and 7 (two stacks), when HP Small is used once, then slot 3 quantity decrements and slot 7 quantity is unchanged.
*Test type: Server-unit*

**AC-CUS-23 — EC-10: invalid AssignedItemID at load defaults to Unassigned**
Given Slot 0.AssignedItemID = X in the save record and item X is not present in the Item Database at load time, when the hotbar initializes on session start, then Slot 0.AssignedItemID = ItemID.Invalid.
*Test type: Unit*

**AC-CUS-24 — Non-HP/MP EffectType assignment to Slot 0 is rejected**
Given Slot 0 is unassigned and a consumable with EffectType ≠ RestoreHP is assigned to Slot 0, then Slot 0.AssignedItemID = ItemID.Invalid (assignment rejected).
*Test type: Unit*

**AC-CUS-25 — Out-of-stock slot retains AssignedItemID after last unit consumed**
Given Slot 0 = HP Small and HP Small inventory quantity = 1, when HP Small is used and UseItemResult arrives with newInventoryQuantity = 0, then Slot 0.AssignedItemID = HP Small's ItemID (assignment retained) and SlotState[0] = AssignedOutOfStockOnCooldown (or AssignedOutOfStock if cooldown had already expired before UseItemResult arrived).
*Test type: Integration*

**AC-CUS-26 — Server rejects UseItemRequest when server-side EffectType cooldown is active**
Given server-side HP CooldownRemaining > 0 (server and client clocks diverged, or exploit attempt), when the server receives UseItemRequest for a RestoreHP item, then server returns UseItemRejected { reason: RejectedOnCooldown } and inventory quantity is unchanged.
*Test type: Server-unit*

**AC-CUS-27 — Server rejects MP potion use on MaxMP = 0 character (EC-14)**
Given character GetEffectiveStat(MaxMP) = 0, when the server receives UseItemRequest for a RestoreMP item, then server returns UseItemRejected { reason: RejectedInvalidTarget } and inventory quantity is unchanged.
*Test type: Server-unit*

**AC-CUS-28 — HotbarStateChangedEvent fires on slot assignment**
Given Slot 0 is Unassigned, when a RestoreHP consumable (ItemID = X) is assigned to Slot 0, then HotbarStateChangedEvent is raised with SlotIndex = 0, NewState = AssignedReady (or AssignedOutOfStock if inventory = 0), AssignedItemID = X.
*Test type: Unit*

**AC-CUS-29 — HotbarStateChangedEvent fires on cooldown start**
Given Slot 0 = HP Small in AssignedReady state and UseItemResult is received, then HotbarStateChangedEvent is raised with SlotIndex = 0, NewState = AssignedOnCooldown, CooldownRemaining = C (where C = HP Small's CooldownSeconds from Item Database).
*Test type: Unit*

**AC-CUS-30 — HotbarStateChangedEvent fires on cooldown expiry**
Given Slot 0 is in AssignedOnCooldown and CooldownRemaining decrements to ≤ 0 with HasItem = true, then HotbarStateChangedEvent is raised with SlotIndex = 0, NewState = AssignedReady.
*Test type: Integration*

**AC-CUS-31 — HotbarStateChangedEvent fires on slot clear**
Given Slot 0 is assigned HP Small, when the player selects [Clear] from the quick-picker, then HotbarStateChangedEvent is raised with SlotIndex = 0, NewState = Unassigned, AssignedItemID = ItemID.Invalid.
*Test type: Integration*

**AC-CUS-32 — requestId counter seeds from SessionHandshake.lastSeenUseItemRequestId on reconnect (EC-12)**
Given a character who sent `UseItemRequest{requestId = N}` before disconnect (server processed it and records `lastSeenUseItemRequestId = N`), when the client reconnects within `SESSION_TTL_SECONDS` and receives `SessionHandshake.lastSeenUseItemRequestId = N`, then the first `UseItemRequest` sent after reconnect carries `requestId = N + 1` — not 0 or 1. The server does not treat this as a duplicate; it processes the request normally and returns a `UseItemResult`.
*Test type: Integration — requires test harness that injects a controlled `lastSeenUseItemRequestId` value into the reconnect SessionHandshake and inspects the outbound requestId of the first post-reconnect UseItemRequest.*

---

**Gate classification:**

| Gate | ACs | Required before |
|------|-----|-----------------|
| Blocking — Unit | 01, 02, 08, 13, 14, 15, 20, 23, 24, 28, 29 | Any Consumable Use story marked Done — test files in `tests/unit/consumable-use/` |
| Blocking — Server-unit | 09, 10, 20b, 21, 22, 26, 27 | QA hand-off |
| Advisory — Integration | 03–07, 11–12, 16, 17–19, 25, 30, 31, 32 | Automated integration test or documented playtest in `production/qa/evidence/` |
| Advisory — UI | 13b | Manual sign-off with screenshot in `production/qa/evidence/` |

## Open Questions

**OQ-CUS-1 — requestId counter continuity across same-session reconnect**
*Status: **RESOLVED 2026-06-11** | Owner: Network Programmer*
The `requestId` is a per-session monotonic `uint` counter following ADR-001. The current wire protocol spec (line 1102 of `networking-wire-protocol.md`) encodes "reset to 0 on zone entry including same-zone reconnect" as the defined behavior — this is the **buggy behavior**: on reconnect within `SESSION_TTL_SECONDS`, a new `requestId = 1` collides with the cached dedup entry for the original `requestId = 1`, and the server returns stale UseItemResult data (wrong HP/inventory values) to the client. Additionally, the SessionHandshake (CR-NET-6.4 in `networking-session.md`) does not include the `requestId` counter in its field list — no mechanism exists for the server to echo the last-seen counter on reconnect. **Resolution requires:** (1) amend `networking-wire-protocol.md` to specify counter-persistence across reconnect; (2) amend `networking-session.md` CR-NET-6.4 to include `lastSeenRequestId` per subsystem in the SessionHandshake. **The CUS implementation sprint cannot begin until these amendments are authored and approved.** This is not a coordination item — the current spec is incorrect and will produce a stale-state injection bug if built as written.

**OQ-CUS-2 — Wire message registration**
*Status: **RESOLVED 2026-06-09** | Owner: Network Programmer*
`UseItemRequest` (R-OD, C→S, 23B standalone), `UseItemResult` (R-OD, S→C, 26B standalone), and `UseItemRejected` (R-OD, S→C, 15B standalone) registered in `networking-wire-protocol.md`, `networking-channel-contract.md`, and `networking-message-criticality.md`. All three are Pillar 1 / Guaranteed delivery / R-OD. **CR-NET-7.2 correction applied:** `UseItemResult.newResourceValue` wire type changed from `float` to `int` (FloorToInt); game-logic type remains `float`. Interactions table updated accordingly.

**OQ-CUS-3 — ADR-001 deduplication key missing message-type discriminator**
*Status: **RESOLVED 2026-06-09** | Owner: Network Programmer*
ADR-001 Amendment A1 (2026-06-09) updated the dedup key from `(SenderEntityID, requestId)` to `(SenderEntityID, messageType, requestId)`, where `messageType` is the `ushort` wire-protocol envelope identifier. The discriminator prevents cross-subsystem cache collisions between NPC Shop and CUS `requestId` counters within `SESSION_TTL_SECONDS`. Updated in `networking-wire-protocol.md` (BuyRequest, UseItemRequest comments) and `networking-channel-contract.md` (BuyRequest, UseItemRequest channel notes).

**OQ-CUS-4 — Resource Display Authority and Reconciliation contract**
*Status: **RESOLVED 2026-06-09** | Owner: Network Programmer + Lead Programmer*
`resource-display-authority.md` authored 2026-06-09. Display Authority Rule uses CR-NET-7.1 `ServerTickNumber` (already in all envelopes) for stale-discard — no wire format changes to `UseItemResult` or `EntityHealthUpdate` required. Rule: `EntityHealthUpdate` applied with `tick >= HPDisplayTick` (updates `HPDisplayTick`); `UseItemResult` HP applied only if `tick > HPDisplayTick` (strictly greater; does not update `HPDisplayTick`). This ensures tick-end EHU (all tick-T effects resolved) supersedes mid-tick `UseItemResult` for the same server tick regardless of arrival order.
