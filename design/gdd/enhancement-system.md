# Enhancement System

> **Status**: In Design
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-23
> **Implements Pillar**: Legendary Gear (primary), Earned Power (secondary)

## Overview

The Enhancement System is the attempt-based upgrade engine for all equippable items in Project Iron Grind. A player visits the Enhancement NPC in the town hub, selects an eligible item from their inventory, spends an Enhancement Scroll (purchased from the NPC Shop), and the server resolves a probability-weighted attempt: on success, the item's enhancement level increases by +1 and its flat stat bonuses grow; on failure, both the item and the scroll are permanently lost. Enhancement levels +1 through +5 close the stat gap to the next gear tier — a +5 item of any tier has approximately the same effective stats as an unenhanced item of the tier above. Levels +6 and beyond enter prestige territory, where the item outperforms the next tier's baseline and its enhancement level becomes visible to every player in the zone through the PrestigeBand encoding on the equipment appearance byte. Reaching enhancement level +9 on any weapon triggers a server-wide broadcast to all online players, making it a social event rather than a private milestone. The system locks the target inventory slot during each attempt and removes the item from inventory on destruction; both the enhancement level and current slot state are persisted across sessions. Enhancement exists to turn farmed time into a risk decision: every scroll purchase is a bet, and the same grinding loop that yields Bronze gear also yields the scrolls that can make that gear legendary — or destroy it.

## Player Fantasy

The moment a Dark Steel blade enters the zone hub with a High-band glow, the conversation stops. Other players read the shape of the light before they tap to inspect. When they do, there is the number. That number is the story — hours of farming, a calculated string of bets, failures survived, a final push through the destruction window. Enhancement is the system that turns that time into a credential anyone on the server can read.

Each attempt is made with full information: the success percentage is visible before the scroll is spent. The player decides with eyes open — push to +7 at 34% success, or bank this win and walk away. Confirmation sends the percentage away, the slot locks, and for a fraction of a second the outcome is unwritten. The success flash is relief. At and above the destruction threshold, the failure flash is a gut-punch the player chose to risk. There are no hidden pity mechanics, no false safety net. What the UI shows is what the server rolls. That honesty is what makes the wager feel real.

Scarcity is not incidental — destruction above the threshold is the engine behind what the glow means. When someone reaches +9, the server broadcasts it to everyone online by name: "Arion has enhanced a Dark Steel Sword to +9." The PrestigeBand glow is readable at range; the exact number is only revealed on deliberate inspect. Strangers form a read before they know the answer, and ask when they want to know more. A high-enhancement item carries its history wherever it goes. The Enhancement System's job is to make that history legible to the world.

## Detailed Design

### Core Rules

**CR-ENH-1: Enhancement Level — Storage and Default**
Enhancement level is a per-instance property. It is stored as `EnhancementLevel: byte` in `InventorySlotRecord` (Inventory System) and `EquipmentSlotRecord` (Equipment System). It is not an Item Database field. All newly created item instances start at `EnhancementLevel = 0`.

**CR-ENH-2: Enhancement Level — Valid Range**
Valid enhancement levels are integers 0 through `MAX_ENHANCEMENT_LEVEL` (10). An item at `MAX_ENHANCEMENT_LEVEL` cannot be enhanced further. Submitting `ConfirmEnhancement` for such an item returns `RejectedAtMaxLevel` with no state change.

**CR-ENH-3: Scroll Tier Match**
Each attempt consumes exactly one Enhancement Scroll whose `TargetGearTier` matches the target item's `GearTier`:

| Scroll | Valid Target Tier |
|--------|-----------------|
| Bronze Enhancement Scroll | Bronze |
| Iron Enhancement Scroll | Iron |
| Steel Enhancement Scroll | Steel |
| Dark Steel Enhancement Scroll | Dark Steel |

A mismatched scroll is rejected before any RNG is rolled and returns `RejectedTierMismatch`. The scroll is not consumed on rejection.

**CR-ENH-4: Inventory Requirement**
Only items currently in an inventory slot (not equipped) may be enhanced. Targeting an equipped item returns `RejectedItemEquipped`. The player must unequip the item before attempting enhancement.

**CR-ENH-5: Accessory Exclusion**
Items with `GearSlot.Ring` or `GearSlot.Necklace` cannot be targeted. Targeting an accessory returns `RejectedAccessoryType`. Accessories are upgraded exclusively via the Accessory Merge mechanic (Equipment System CR-EQS-14).

**CR-ENH-6: Irrevocability Gate (Two-Tap)**
Item and scroll selection in the UI do not constitute commitment. The server locks the target slot and consumes the scroll only upon receipt of an explicit `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)` request. Before that request is sent, the player may send `CancelEnhancement` at any time with no penalty. Once `ConfirmEnhancement` is sent, the attempt is irrevocable.

**CR-ENH-7: Slot Lock Duration**
Upon receipt of `ConfirmEnhancement`, the server calls `Inventory.LockSlot(slotIndex)` before any other operation. The slot remains locked for the entire attempt lifecycle. The server calls `Inventory.UnlockSlot(slotIndex)` exactly once, after the outcome is committed to persistent storage and the slot is cleared or updated.

**CR-ENH-8: Concurrent Attempt Prevention**
A player may have at most one active enhancement attempt at a time. Submitting `ConfirmEnhancement` while another attempt is in progress for this player (state VALIDATING, LOCKED, or RESOLVING) returns `RejectedConcurrentAttempt`.

**CR-ENH-9: Attempt Resolution — Two Outcomes**
The server draws a single uniform random value `r ∈ [0, 1)` and resolves the outcome for current level `k`:
- **Success** (`r < P_s[k]`): level increments to `k + 1`; item retained; scroll consumed.
- **Fail-Destruction** (`r ≥ P_s[k]`): item permanently removed from inventory; slot cleared; scroll consumed.

There is no partial-failure (Fail-Safe) outcome. Every failed attempt destroys the item. See Formulas for per-level probability values.

**CR-ENH-10: Universal Destruction**
Fail-Destruction is possible at every enhancement level, including +0. Every failed attempt permanently destroys the item. There is no safe floor. The probability of destruction at each level is defined in F-ENH-4.

**CR-ENH-11: Commit-Then-Deliver**
Steps 3–6 of the attempt sequence (CR-ENH-15) execute within a single atomic database transaction. The transaction commits at step 6. The server sends result messages only after the transaction commits. If the server crashes or the connection drops before the transaction commits, the entire sequence is rolled back: the scroll remains in inventory, the slot is unlocked, and the item is unmodified. On reconnect, the client reads the committed state; if no commit occurred, state is unchanged. No client-side recovery flow is required. This prevents deliberate disconnect to avoid destruction — a successful disconnect-before-commit leaves the player with their scroll and item intact, but does not protect against destruction once the commit succeeds.

**CR-ENH-12: Prestige Band Encoding**
The enhancement level maps to a PrestigeBand encoded in `EquipmentSlotRecord.equipmentAppearanceFlags[2:1]`. Four states occupy all 2-bit values in monotonically increasing order:

| Band | Level Range | Bits [2:1] | Glow visible at range |
|------|------------|------------|----------------------|
| NONE | 0–4 | `00` | No |
| VISIBLE_NO_GLOW | 5–6 | `01` | No — badge color only |
| GLOW_LOW | 7 | `10` | Yes — low intensity |
| HIGH | 8–10 | `11` | Yes — high intensity |

The PrestigeBand is visible to other players at zone range. The exact enhancement level is not visible at range; it is revealed only on deliberate inspect.

The Enhancement System writes `EnhancementLevel` to `InventorySlotRecord` only. The Equipment System reads `InventorySlotRecord.EnhancementLevel` when equipping an item; it sets `EquipmentSlotRecord.EnhancementLevel = InventorySlotRecord.EnhancementLevel` and updates `EquipmentSlotRecord.equipmentAppearanceFlags[2:1]` accordingly. The Enhancement System does not write directly to `EquipmentSlotRecord`.

**CR-ENH-13: Prestige Glow**
Items enhanced to `ENHANCEMENT_GLOW_THRESHOLD` (7) or above render with a visual glow when equipped. The VFX System renders a glow only when `EnhancementLevel ≥ ENHANCEMENT_GLOW_THRESHOLD`; it reads `equipmentAppearanceFlags[2:1]` to select the glow intensity variant (GLOW_LOW band = low glow; HIGH band = high glow).

**CR-ENH-14: Server Broadcast at +9**
When an item successfully reaches enhancement level 9, the server sends a broadcast to all online players:
> `"{PlayerName} has enhanced a {ItemName} to +9."`

The broadcast fires once per successful +9 transition. `ItemName` is the Item Database display name. No broadcast fires for other level transitions.

**CR-ENH-15: Attempt Sequence**
Steps execute in this order; the server does not advance to the next step if the current step fails:

1. Server receives `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)`.
2. Server validates: `NPCInteractionActive = true` for this player, item exists at `itemSlotIndex`, slot unlocked, `IsUpgradeable = true`, `GearSlot ≠ Ring/Necklace`, `EnhancementLevel < MAX_ENHANCEMENT_LEVEL`, scroll exists at `scrollSlotIndex`, scroll `TargetGearTier` matches item `GearTier`, no concurrent attempt for this player. If any check fails, return rejection code; no state changes.
3. Server calls `Inventory.LockSlot(itemSlotIndex)`.
4. Server removes scroll from `scrollSlotIndex` via `Inventory.RemoveItem(scrollSlotIndex)`.
5. Server draws `r ∈ [0, 1)`, resolves outcome per CR-ENH-9.
6. Server commits outcome to persistent storage: increment `EnhancementLevel` (success) or remove item (destruction).
7. Server calls `Inventory.UnlockSlot(itemSlotIndex)`.
8. If `EnhancementLevel` reached 9, server sends `ServerBroadcast_Enhancement9 { playerName, itemName }` to all online players.
9. Server sends `EnhancementAttemptResult { outcome, newLevel, resultCode }` to client.

**Transaction boundary:** Steps 3–6 execute within a single atomic database transaction committing at step 6. A transaction failure at any point in steps 3–6 rolls back all changes in that range — slot is unlocked, scroll is present in inventory, item is unmodified. Steps 7–9 execute after commit.

**CR-ENH-16: Enhancement NPC — Location Requirement**
Enhancement may only be initiated by interacting with the Enhancement NPC in the town hub. The Enhancement UI cannot be opened from the inventory screen or from any zone outside the town hub. This prevents enhancement during combat, in dungeons, or in the field.

**CR-ENH-17: NPC Interaction Session**
The server tracks an `NPCInteractionActive: bool` flag per player session. Flag lifecycle:
- **Open**: Client sends `OpenNPCInteraction(npcId)` when the player taps the Enhancement NPC. Server validates player is in the town hub zone; if valid, sets `NPCInteractionActive = true` and returns `NPCInteractionOpened`. The client opens the Enhancement UI on success.
- **Close**: Client sends `CloseNPCInteraction()` when closing the UI or walking away. Server sets `NPCInteractionActive = false`. Zone transition or session end also clears the flag server-side. Closing has no penalty.
- **Enforcement**: `ConfirmEnhancement` validation (CR-ENH-15 step 2) checks `NPCInteractionActive = true`; if false, returns `RejectedNoNPCSession` with no state changes.

---

### States and Transitions

| State | Description | Entry Condition | Valid Transitions |
|-------|-------------|-----------------|------------------|
| `IDLE` | No active attempt; selection UI is non-binding | Session start; previous attempt complete | Player sends `ConfirmEnhancement` → `VALIDATING` |
| `VALIDATING` | Server checking all preconditions (step 2) | `ConfirmEnhancement` received | Validation fails → `IDLE` (rejection code returned, no state change); validation passes → `LOCKED` |
| `LOCKED` | Slot locked; scroll consumed; RNG resolved (steps 3–5) | All preconditions pass | RNG resolved → `RESOLVING` |
| `RESOLVING` | Outcome committed to persistent storage (step 6) | RNG outcome known | Commit complete → `RESULT_SUCCESS` or `RESULT_DESTRUCTION` |
| `RESULT_SUCCESS` | Level incremented; slot unlocked; result sent (steps 7–9) | Commit: success | Server sends `EnhancementAttemptResult` → `IDLE` |
| `RESULT_DESTRUCTION` | Item removed; slot cleared and unlocked; result sent (steps 7–9) | Commit: destruction | Server sends `EnhancementAttemptResult` → `IDLE` |

Invalid transitions: no state may reach `LOCKED` or `RESOLVING` without passing through `VALIDATING`. All `RESULT_*` states resolve to `IDLE` only.

---

### Interactions with Other Systems

| System | Enhancement System Reads | Enhancement System Writes / Signals | Interface Owner |
|--------|--------------------------|-------------------------------------|-----------------|
| **Item Database** | `IsUpgradeable: bool`, `GearTier`, `GearSlot`, `StatModifiers[].FlatBonus`, `ElementalDamage`, display name | — (stateless; no mutations) | Item Database |
| **Inventory System** | `InventorySlotRecord.EnhancementLevel`, `IsSlotLocked(slotIndex)`, item and scroll existence | `LockSlot(slotIndex)`, `UnlockSlot(slotIndex)`, `RemoveItem(scrollSlotIndex)`, mutates `EnhancementLevel` in item slot record on success, clears item slot on destruction | Inventory System owns `LockSlot`/`UnlockSlot`/`RemoveItem`; Enhancement System owns slot-record mutations |
| **Equipment System** | — | Exposes `IEnhancementBonusProvider` (below) for Equipment System to consume. Does not write to `EquipmentSlotRecord` — item cannot be equipped during enhancement; Equipment System reads `InventorySlotRecord.EnhancementLevel` on equip to update `equipmentAppearanceFlags[2:1]` | Equipment System consumes `IEnhancementBonusProvider` and owns `equipmentAppearanceFlags` update on equip |
| **Damage Calculation** | — | `IEnhancementBonusProvider.GetElementalBonus(level: int, gearTier: GearTier, isWeapon: bool): int` — flat elemental damage bonus for the given level and tier; returns 0 when `isWeapon` is false | Enhancement System owns and implements; Damage Calculation consumes |
| **Character Persistence** | — | `EnhancementLevel` in `InventorySlotRecord` and `EquipmentSlotRecord` must be included in all save and load payloads | Character Persistence |
| **Networking** | Client → Server: `OpenNPCInteraction(npcId)`, `CloseNPCInteraction()`, `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)`, `CancelEnhancement` | Server → Client: `NPCInteractionOpened`, `EnhancementAttemptResult { outcome, newLevel, resultCode }`; Server → All: `ServerBroadcast_Enhancement9 { playerName, itemName }` | Enhancement System authors all server-originated messages |
| **Enhancement UI** | Player input: item selection, scroll selection, confirm, cancel | `EnhancementStateUpdate { currentLevel, P_s, P_d }` on selection; `EnhancementAttemptResult` on attempt completion | Enhancement UI consumes Enhancement System messages |
| **VFX System** | — | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`, `OnPrestigeBandChange(newBand)` | VFX System subscribes |
| **Audio System** | — | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()` | Audio System subscribes |
| **NPC Shop** | — | — (scroll purchase handled by NPC Shop; Enhancement System has no Currency System dependency at MVP) | NPC Shop |

**IEnhancementBonusProvider interface** (owned by Enhancement System, consumed by Equipment System and Damage Calculation):
- `GetFlatBonus(level: int, baseFlatBonus: int, gearTier: GearTier): int` — enhanced flat stat value for a given level, base modifier, and gear tier (selects the correct `BonusPerLevel[tier]`)
- `GetElementalBonus(level: int, gearTier: GearTier, isWeapon: bool): int` — flat elemental damage bonus for the given level and tier; returns 0 if `isWeapon` is false

## Formulas

### F-ENH-1: Enhancement Flat Bonus

Each stat modifier in `StatModifiers[]` gains a flat bonus per enhancement level. The bonus is uniform across all modifier types for a given item tier:

```
EnhancedFlatBonus(modifier, level) = modifier.FlatBonus + (level × BonusPerLevel[modifier.GearTier])
```

| Gear Tier | `BonusPerLevel` (per modifier) |
|-----------|-------------------------------|
| Bronze | 3 |
| Iron | 4 |
| Steel | 6 |
| Dark Steel | 10 |

**Output range**: `[base_FlatBonus, base_FlatBonus + (MAX_ENHANCEMENT_LEVEL × BonusPerLevel[tier])]`. Maximum: Dark Steel tier, level 10, max base 68: `68 + (10 × 10) = 168`.

**Example** (Bronze Sword, Attack modifier base = 10, level = +5):
> `EnhancedFlatBonus = 10 + (5 × 3) = 25`

---

### F-ENH-2: Enhancement Elemental Bonus (Weapons Only)

Weapons gain a flat bonus to `ElementalDamage` per enhancement level:

```
EnhancedElementalDamage(level) = min(
  item.ElementalDamage + (level × ElementalBonusPerLevel[item.GearTier]),
  ElementalDamage_ceiling
)
```
Where `ElementalDamage_ceiling = 9,999`. The unclamped maximum (Dark Steel weapon at +10 with high base) can exceed 9,999; the clamp enforces the ceiling defined in the Damage Calculation GDD.

| Gear Tier | `ElementalBonusPerLevel` |
|-----------|------------------------|
| Bronze | 1 |
| Iron | 2 |
| Steel | 3 |
| Dark Steel | 5 |

Non-weapon items do not gain elemental bonuses. `IEnhancementBonusProvider.GetElementalBonus(level)` returns 0 for non-weapon items.

**Example** (Dark Steel Sword, base ElementalDamage = 15, level = +7):
> `EnhancedElementalDamage = 15 + (7 × 5) = 50`

---

### F-ENH-3: Tier Parity Constraint

A +5 item of tier T must have approximately the same primary stat total as a +0 item of tier T+1. For each pair of adjacent tiers:

```
midpoint(T) + 5 × BonusPerLevel[T] ≈ midpoint(T+1)
```

**Verified tier pairs:**

| Tier T | Stat Range (per modifier) | Midpoint(T) | +5 bonus | Midpoint(T) + 5× | Tier T+1 Range | Midpoint(T+1) | Δ |
|--------|--------------------------|-------------|----------|------------------|----------------|----------------|---|
| Bronze | 8–12 | 10 | +15 | 25 | Iron: 22–28 | 25 | 0 |
| Iron | 22–28 | 25 | +20 | 45 | Steel: 32–42 | 37 | +8 |
| Steel | 32–42 | 37 | +30 | 67 | Dark Steel: 58–68 | 63 | +4 |

**Iron range upstream amendment required**: The Equipment System GDD (F-EQS-2) lists Iron flat bonus range as 16–22 per modifier. With `BonusPerLevel[Bronze] = 3`, the constraint requires Bronze midpoint (10) + 15 = 25, which exceeds the Iron ceiling of 22. The Iron range must be corrected to **22–28** in Equipment System F-EQS-2 before implementation. Steel and Dark Steel pairs show a small surplus (Δ = +8 and +4 respectively); this is acceptable — higher-tier items retain meaningful advantage through their higher ceiling.

**Worst-case overlap — explicit design decision**: A max-roll enhanced item of tier T can exceed the minimum-roll stat of a tier T+1 item. Example: Iron +5 upper base (28) + 20 = 48 > Steel +0 lower base (32). This overlap is intentional. Upper-tier items retain advantage through their higher ceiling, not through a guaranteed floor superiority. Enhancement is a prestige axis, not a hard tier barrier.

---

### F-ENH-4: Probability Table

Two-outcome probability distribution per attempt. Destruction is possible at all levels.

| Level `k` | `P_s[k]` Success | `P_d[k]` Destruction |
|-----------|-----------------|---------------------|
| +0 → +1 | 0.95 | 0.05 |
| +1 → +2 | 0.90 | 0.10 |
| +2 → +3 | 0.85 | 0.15 |
| +3 → +4 | 0.80 | 0.20 |
| +4 → +5 | 0.65 | 0.35 |
| +5 → +6 | 0.50 | 0.50 |
| +6 → +7 | 0.35 | 0.65 |
| +7 → +8 | 0.22 | 0.78 |
| +8 → +9 | 0.12 | 0.88 |
| +9 → +10 | 0.06 | 0.94 |

All rows: `P_s[k] + P_d[k] = 1.00`. `P_d[k] = 1 − P_s[k]` — only two outcomes exist at every level. All `P_s[k]` values are tuning knobs (see Tuning Knobs section).

---

### F-ENH-5: Expected Scrolls to Reach Level N

Expected Enhancement Scrolls to advance from +0 to level N, accounting for destruction-forced restarts:

```
A[k] = (1 + P_d[k] × T[k]) / P_s[k]
T[0] = 0
T[k] = T[k-1] + A[k-1]
```

| Target Level | Expected Scrolls (from +0) |
|-------------|--------------------------|
| +1 | 1.1 |
| +2 | 2.3 |
| +3 | 3.9 |
| +4 | 6.1 |
| +5 | 10.9 |
| +6 | 23.8 |
| +7 | 70.8 |
| +8 | 326 |
| +9 | 2,727 |
| +10 | ~45,500 |

**Economy validation**: At Dark Steel Scroll price 350g and Dark Steel item sell price 270g, expected cost to reach +9 ≈ 955K gold. Scroll-to-item-value ratio ≈ 3,533×. The scroll economy is the primary gold sink; destroyed item value is negligible relative to scroll investment. `GoldTransactionReason.Enhancement = 5` (pre-allocated in Currency System).

## Edge Cases

**EC-ENH-1: Mid-Attempt Disconnect**
If the client disconnects after sending `ConfirmEnhancement` but before receiving `EnhancementAttemptResult`, the server continues processing. Per CR-ENH-11, the outcome is committed to persistent storage before the result message is sent. On reconnect, the client reads the committed state from `InventorySlotRecord` or `EquipmentSlotRecord`. The item is either present at its new enhancement level (success) or absent (destruction). No rollback and no recovery flow are triggered. The server does not re-send the result message on reconnect.

**EC-ENH-2: Disconnect While LOCKED or RESOLVING**
Steps 3–6 execute within a single atomic database transaction (CR-ENH-11). If the server loses the client connection during states LOCKED or RESOLVING — before the transaction commits at step 6 — the transaction is rolled back: the slot lock expires with the session, the scroll remains in inventory, and the item is unmodified. On reconnect, the server detects no in-progress attempt. Both scroll and item are intact. The scroll is only irrevocably consumed when the step 6 transaction commits successfully.

**EC-ENH-3: Attempt at MAX_ENHANCEMENT_LEVEL**
A player submits `ConfirmEnhancement` for an item already at `MAX_ENHANCEMENT_LEVEL` (10). The server rejects at CR-ENH-15 step 2 and returns `RejectedAtMaxLevel`. No scroll is consumed, no slot is locked, no state changes.

**EC-ENH-4: Item Moved or Removed During Selection (Pre-Confirm)**
Between client item selection and `ConfirmEnhancement` send, another operation removes the item (trade, discard, concurrent equip). The server's step 2 validation detects the item is absent or slot mismatched and returns `RejectedItemNotFound`. No scroll is consumed. The UI must handle this rejection gracefully and return the player to IDLE.

**EC-ENH-5: App Backgrounded Mid-Selection (Pre-Confirm)**
The player backgrounds the app between item/scroll selection and the Confirm tap. No server request has been sent; no slot is locked; no scroll is consumed. On foreground return, the UI restores its local selection state (item, scroll, displayed probabilities) and re-requests `EnhancementStateUpdate` for the saved selection to detect any state changes that occurred while backgrounded (e.g., the selected item was moved or removed by another operation). No server action is required if the selection is still valid.

**EC-ENH-6: Scroll Consumed, Item Destroyed in Same Transaction**
On a Fail-Destruction outcome, both the scroll and the item are consumed in a single atomic commit (CR-ENH-15 step 6). There is no state where the scroll survives a destruction, nor where the item survives scroll consumption. If the atomic write fails (database error), the entire operation is rolled back: scroll remains in inventory, item is unmodified. The server calls `Inventory.UnlockSlot(slotIndex)` to release the slot, then logs a `CriticalEnhancementWriteFailed` event for monitoring.

**EC-ENH-7: Tier-Mismatched Scroll Selected**
A player submits `ConfirmEnhancement` with a scroll whose `TargetGearTier` does not match the item's `GearTier`. The server rejects at step 2 and returns `RejectedTierMismatch`. No scroll is consumed. The UI should filter the scroll list by the selected item's tier client-side, but server enforcement is authoritative.

**EC-ENH-8: +9 Broadcast Under Server Load**
If `ServerBroadcast_Enhancement9` cannot be delivered to all players (high load, partial connectivity), the broadcast is sent on a best-effort basis. Delivery failure does not affect the enhancement outcome. The enhancement is committed regardless of broadcast success.

## Dependencies

### Upstream Dependencies

| System | GDD Status | What Enhancement System Requires |
|--------|-----------|----------------------------------|
| **Item Database** | Approved | `IsUpgradeable: bool`, `GearTier`, `GearSlot`, `StatModifiers[].FlatBonus`, `ElementalDamage` per item; 4 Enhancement Scroll records (Bronze/Iron/Steel/Dark Steel) with `ScrollData.TargetGearTier` sub-schema |
| **Inventory System** | Approved | `LockSlot(slotIndex)`, `UnlockSlot(slotIndex)`, `RemoveItem(slotIndex)`, `IsSlotLocked(slotIndex)`; `InventorySlotRecord` must include `EnhancementLevel: byte` |
| **Currency System** | Approved | `GoldTransactionReason.Enhancement = 5` (pre-allocated); scroll sale handled by NPC Shop; no direct Currency System dependency at MVP |

### Downstream Dependencies

| System | GDD Status | What They Require from Enhancement System |
|--------|-----------|-------------------------------------------|
| **Equipment System** | Approved | `IEnhancementBonusProvider.GetFlatBonus(level, baseFlatBonus)` in `AddEquipmentModifier`; `EquipmentSlotRecord.EnhancementLevel: byte`; `equipmentAppearanceFlags[2:1]` update on success; F-EQS-2 Iron range correction (see below) |
| **Damage Calculation** | Approved | `IEnhancementBonusProvider.GetElementalBonus(level)` for elemental damage in F-DC-2; `Equipment.GetEquippedWeaponEnhancementLevel(): byte` to supply the level |
| **Character Persistence** | Not Started | `EnhancementLevel: byte` in `InventorySlotRecord` and `EquipmentSlotRecord` save/load payload |
| **Enhancement UI** | Not Started | `EnhancementStateUpdate`, `EnhancementAttemptResult`, `ServerBroadcast_Enhancement9` message schemas; `ConfirmEnhancement` and `CancelEnhancement` request schemas |
| **VFX System** | Not Started | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`, `OnPrestigeBandChange(band)` signals; `ENHANCEMENT_GLOW_THRESHOLD = 7` |
| **Audio System** | Not Started | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()` signals |

### Required Upstream Amendments (Approved GDDs)

Before implementation, these changes must be applied to already-approved documents:

1. **Equipment System F-EQS-2**: Correct Iron flat bonus range 16–22 → **22–28** (F-ENH-3 parity constraint).
2. **Equipment System CR-EQS-11 / EquipmentSlotRecord**: Add `EnhancementLevel: byte`; update `AddEquipmentModifier` to use `IEnhancementBonusProvider.GetFlatBonus`.
3. **Inventory System InventorySlotRecord**: Add `EnhancementLevel: byte`.
4. **Item Database**: Add 4 Enhancement Scroll records; add `ScrollData { TargetGearTier: GearTier }` sub-schema.

## Tuning Knobs

**TK-ENH-1: Probability Table — Per-Level Values**
All 10 `P_s[k]` values in F-ENH-4 (k = 0–9) are independently tunable. `P_d[k] = 1 − P_s[k]` is derived automatically. Constraints: `P_s[k] + P_d[k] = 1.00` for all k; `P_s[k]` strictly decreasing; `P_s[k]` safe range: 0.01–0.95.
*Gameplay effect*: Controls overall enhancement difficulty, expected scroll cost per level, and where the destruction curve steepens.

**TK-ENH-2: P_s Curve Inflection**
The steepness and inflection point of the `P_s` curve determines when risk becomes meaningful. The current curve has a deliberate drop between +3 and +4 (P_s: 0.80 → 0.65) and reaches coin-flip at +5 (P_s = 0.50). Flattening the early curve extends low-risk territory; steepening it compresses the prestige range.
*Gameplay effect*: Earlier inflection = more item destruction during mid-tier progression; later inflection = destruction concentrated in prestige territory.

**TK-ENH-3: MAX_ENHANCEMENT_LEVEL**
Hard cap on enhancement level. Current value: **10**. Safe range: 9–12. Values below 9 remove the +9 broadcast milestone; values above 12 require probability table extension.
*Gameplay effect*: Sets the ceiling of the prestige hierarchy.

**TK-ENH-4: ENHANCEMENT_GLOW_THRESHOLD**
Enhancement level at which PrestigeBand transitions VISIBLE_NO_GLOW → GLOW_LOW, and at which equipped weapons begin rendering a glow visible to other players. Current value: **7**. Safe range: 6–9 (must be greater than `PRESTIGE_MID_THRESHOLD` and less than `PRESTIGE_HIGH_THRESHOLD`).
*Gameplay effect*: Lower = glow is more common; higher = glow is extremely rare.

**TK-ENH-5: PRESTIGE_MID_THRESHOLD**
Level at which PrestigeBand transitions NONE → VISIBLE_NO_GLOW. Current value: **5**. Safe range: 3–6. Must be less than `ENHANCEMENT_GLOW_THRESHOLD`.
*Gameplay effect*: Sets the badge-color visibility floor for enhancement. Aligns with the tier parity crossover.

**TK-ENH-6: PRESTIGE_HIGH_THRESHOLD**
Level at which PrestigeBand transitions GLOW_LOW → HIGH. Current value: **8**. Safe range: 7–10. Must be greater than `ENHANCEMENT_GLOW_THRESHOLD`.
*Gameplay effect*: Determines when the highest-tier glow activates.

**TK-ENH-7: BonusPerLevel (per tier)**
Flat stat bonus per enhancement level per modifier, by tier. Current values: Bronze=3, Iron=4, Steel=6, Dark Steel=10. Changing these values invalidates F-ENH-3 (tier parity); Iron range correction in F-EQS-2 must be re-verified.
*Gameplay effect*: Controls how much raw power enhancement adds relative to base item stats.

**TK-ENH-8: ElementalBonusPerLevel (per tier)**
Flat elemental damage bonus per enhancement level, for weapons only. Current values: Bronze=1, Iron=2, Steel=3, Dark Steel=5. Independent of F-ENH-3 (elemental is not part of the parity constraint).
*Gameplay effect*: Controls elemental damage scaling relative to physical stat scaling.

**TK-ENH-9: Enhancement Scroll Prices (per tier)**
Gold cost per scroll at the NPC Shop. Must satisfy the 30–50 min same-tier farming invariant (Currency System).

| Scroll Tier | Recommended | Min (30 min) | Max (50 min) |
|-------------|-------------|--------------|--------------|
| Bronze | 90g | 75g | 125g |
| Iron | 150g | 125g | 210g |
| Steel | 230g | 190g | 320g |
| Dark Steel | 350g | 270g | 450g |

*Gameplay effect*: Scroll price directly sets the gold sink rate and expected gold cost per enhancement level.

## Visual/Audio Requirements

### Visual Requirements

**VR-ENH-1: Enhancement Attempt Animation**
When `EnhancementAttemptResult` is received, the UI plays a result animation before the outcome is revealed:
- **Success**: brief bright flash (item color) → item slot updates with new level badge → success overlay.
- **Fail-Destruction**: longer red flash → item slot clears → destruction overlay with item silhouette dissolve.
Animation must complete before the player can initiate a new attempt.

**VR-ENH-2: Prestige Glow — Equipped Weapons**
Weapons at enhancement level ≥ `ENHANCEMENT_GLOW_THRESHOLD` (7) display a continuous glow when equipped, visible to the owning player and to other players in the zone:
- **GLOW_LOW band (level 7 only)**: low-intensity, cool-toned glow.
- **HIGH band (levels 8–10)**: high-intensity, warm-toned glow with particle pulse.
VFX System reads `equipmentAppearanceFlags[2:1]`. Non-weapon items do not glow regardless of enhancement level.

**VR-ENH-3: Enhancement Level Badge**
Enhanced items show a `+[level]` badge in all inventory contexts (bag, equipment screen, trade, loot preview):
- Levels 1–4: white text.
- Levels 5–7: blue text (VISIBLE_NO_GLOW band at 5–6; GLOW_LOW band at 7).
- Levels 8–10: gold text (HIGH band).

**VR-ENH-4: PrestigeBand Visible at Range**
Other players in the zone see the PrestigeBand glow on equipped items without inspecting. Levels 5–6 (VISIBLE_NO_GLOW band) have no glow visible at range — the enhancement level badge is revealed only on deliberate inspect. Levels 7–10 glow is readable at zone range; only glow intensity is visible at range, and the exact `+[level]` number is revealed on deliberate inspect only.

**VR-ENH-5: Destruction Flash**
On Fail-Destruction: full-screen or near-full-screen red flash (~0.5s), followed by item disappearing from slot with a particle dissolve. Visually unambiguous from a successful attempt.

### Audio Requirements

**AR-ENH-1: Success Sound**
Positive chime on success. Pitch or intensity scales with resulting level (levels 1–4: moderate; 5–7: elevated; 8+: peak).

**AR-ENH-2**: *Removed — Fail-Safe outcome eliminated in Revision Pass 1.*

**AR-ENH-3: Destruction Sound**
Dramatic, punishing, unambiguous. Loud. This is the emotional anti-peak the system is designed around.

**AR-ENH-4: +9 Broadcast Sting**
A brief recognizable announcement sting plays on all clients when `ServerBroadcast_Enhancement9` is received — regardless of who achieved it. Must not be disruptive for players in unrelated activities.

**AR-ENH-5: Confirm Tap Sound**
Short "commit" sound plays when the player sends `ConfirmEnhancement`, before the result is known. Establishes the anticipation window between tap and result reveal.

## UI Requirements

### Server → Client Messages

**UI-ENH-1: EnhancementStateUpdate**
Sent when the player selects a valid item and scroll combination (pre-confirm):
```
EnhancementStateUpdate {
  itemSlotIndex: byte
  currentLevel: byte
  P_s: float           // success probability [0, 1]
  P_d: float           // destruction probability [0, 1]
}
```
Display both `P_s` and `P_d` at all times — destruction is possible at every level. Display a heightened destruction warning (colored border, warning icon, or explicit label) when `P_d ≥ 0.35` (levels +4 and above).

**UI-ENH-2: EnhancementAttemptResult**
Sent after outcome is committed:
```
EnhancementAttemptResult {
  outcome: EnhancementOutcome   // SUCCESS | DESTRUCTION
  newLevel: byte                // 0 if destruction
  resultCode: EnhancementResultCode
}

EnhancementResultCode:
  Success | Destruction
  RejectedAtMaxLevel | RejectedTierMismatch | RejectedItemEquipped
  RejectedAccessoryType | RejectedItemNotFound | RejectedConcurrentAttempt
  RejectedNoNPCSession
```
All `Rejected*` codes return the player to IDLE with an appropriate message. No result animation plays on rejection.

**UI-ENH-3: ServerBroadcast_Enhancement9**
```
ServerBroadcast_Enhancement9 {
  playerName: string
  itemName: string
}
```
Displayed in system chat or announcement overlay: `"{playerName} has enhanced a {itemName} to +9."` Plays `AR-ENH-4` broadcast sting. Message is dismissible.

### Client → Server Requests

**UI-ENH-4: NPC Interaction / ConfirmEnhancement / CancelEnhancement**
```
OpenNPCInteraction  { npcId: uint32 }
CloseNPCInteraction {}
ConfirmEnhancement  { itemSlotIndex: byte, scrollSlotIndex: byte }
CancelEnhancement   {}
```
`OpenNPCInteraction` is sent when the player taps the Enhancement NPC; server responds with `NPCInteractionOpened` or a rejection if the player is not in the town hub. Confirm requires an explicit separate tap from item/scroll selection — not auto-submitted.

### UI Behavior Rules

**UI-ENH-5: Probability Display Before Attempt**
Success and destruction probabilities must be visible before the player taps Confirm. Probabilities may not be hidden or revealed only after commitment.

**UI-ENH-6: Destruction Risk Indicator**
A destruction probability indicator is always visible in the Enhancement UI (destruction is possible at all levels). When `P_d ≥ 0.35`, a heightened warning (colored border, warning icon, or explicit label) must appear before the Confirm button is tappable. Players cannot confirm a high-risk attempt without explicitly acknowledging the warning.

**UI-ENH-7: Item and Scroll Eligibility Filtering**
Item selection excludes: equipped items, locked slots, `GearSlot.Ring/Necklace`, `IsUpgradeable = false`, `EnhancementLevel = MAX_ENHANCEMENT_LEVEL`. Scroll selection filters to `TargetGearTier` matching the selected item's `GearTier`.

**UI-ENH-8: Result State Display**
Success and Destruction result screens are visually and aurally distinct. After Destruction, the empty slot is shown explicitly to communicate item loss. The result screen remains until the player dismisses it.

**UI-ENH-9: Lock State Feedback**
While an attempt is in progress (Confirm sent, result not yet received), the item slot shows a locked indicator and the Confirm button is disabled. No new attempt can be submitted until `EnhancementAttemptResult` is received.

## Acceptance Criteria

**AC-ENH-1: Enhancement Level Default Zero**
*Setup*: New item (any tier, `IsUpgradeable = true`) placed in player inventory via test injection.
*Action*: Read `InventorySlotRecord.EnhancementLevel`.
*Pass*: Value is 0.

**AC-ENH-2: Enhancement Level Persists Across Sessions**
*Setup*: Item advanced to +3 (RNG injected for success). Note slot index and player ID. Terminate session.
*Action*: Start new session for same player. Read `InventorySlotRecord.EnhancementLevel`.
*Pass*: Value is 3.

**AC-ENH-3: Scroll Tier Mismatch Rejected — Scroll Not Consumed**
*Setup*: Player inventory contains Bronze-tier item and Iron Enhancement Scroll.
*Action*: `ConfirmEnhancement(bronzeItemSlot, ironScrollSlot)`.
*Pass*: Returns `RejectedTierMismatch`. Iron Scroll remains. Item enhancement level unchanged.

**AC-ENH-4: Equipped Item Rejected**
*Setup*: Bronze Sword equipped (not in bag). Bronze Enhancement Scroll in inventory.
*Action*: `ConfirmEnhancement(equippedSwordSlot, scrollSlot)`.
*Pass*: Returns `RejectedItemEquipped`. Scroll remains. Equipped item unchanged.

**AC-ENH-5: Accessory Rejected**
*Setup*: Ring in inventory. Bronze Enhancement Scroll in inventory.
*Action*: `ConfirmEnhancement(ringSlot, scrollSlot)`.
*Pass*: Returns `RejectedAccessoryType`. Scroll remains.

**AC-ENH-6: Cancel Before Confirm — No State Change**
*Setup*: Player selects valid item and scroll (no `ConfirmEnhancement` sent).
*Action*: `CancelEnhancement`.
*Pass*: Slot was never locked. Scroll in inventory. Enhancement level unchanged. No error returned.

**AC-ENH-7: Slot Locked During Attempt**
*Setup*: Test hook pauses state machine between `ConfirmEnhancement` receipt and outcome commit.
*Action*: Attempt `MoveItem` targeting the same item slot during the pause.
*Pass*: `IsSlotLocked(itemSlotIndex)` returns true. `MoveItem` is rejected.

**AC-ENH-8: Concurrent Attempt Rejected**
*Setup*: One attempt paused in VALIDATING state (test injection). Player sends second `ConfirmEnhancement`.
*Action*: Second `ConfirmEnhancement` for any item.
*Pass*: Returns `RejectedConcurrentAttempt`. First attempt unaffected.

**AC-ENH-9: Success — Level Increments, Scroll Consumed**
*Setup*: Bronze item at +2, Bronze Enhancement Scroll. RNG injected: `r = 0.00`.
*Action*: `ConfirmEnhancement`.
*Pass*: `outcome = SUCCESS`, `InventorySlotRecord.EnhancementLevel = 3`. Scroll absent. Slot unlocked.

**AC-ENH-10: Fail-Destruction at Low Level — Item Removed, Scroll Consumed**
*Setup*: Bronze item at +2, Bronze Enhancement Scroll. RNG injected: `r = 0.90` (above P_s[2] = 0.85).
*Action*: `ConfirmEnhancement`.
*Pass*: `outcome = DESTRUCTION`. Item slot empty. Scroll absent. Slot unlocked.

**AC-ENH-11: Fail-Destruction — Item Removed, Scroll Consumed**
*Setup*: Bronze item at +4, Bronze Enhancement Scroll. RNG injected: `r = 0.99` (above P_s[4] = 0.65).
*Action*: `ConfirmEnhancement`.
*Pass*: `outcome = DESTRUCTION`. Item slot empty. Scroll absent. Slot unlocked.

**AC-ENH-12: Two-Outcome Model — Destruction Possible at +0**
*Setup*: Bronze item at +0, Bronze Enhancement Scroll. RNG injected: `r = 0.96` (above P_s[0] = 0.95).
*Action*: `ConfirmEnhancement`.
*Pass*: `outcome = DESTRUCTION`. Item slot empty. Scroll absent. No FAILSAFE outcome exists at any level.

**AC-ENH-13: Commit-Then-Deliver — Disconnect After Commit**
*Setup*: Test hook drops client connection after step 6 (outcome committed) but before step 9 (result sent). Bronze item at +2, RNG injected for success.
*Action*: `ConfirmEnhancement`, hook fires.
*Pass*: On reconnect, `InventorySlotRecord.EnhancementLevel = 3`. Scroll absent. Slot unlocked. No result message re-sent.

**AC-ENH-14: MAX_ENHANCEMENT_LEVEL Blocks Further Enhancement**
*Setup*: Item at `MAX_ENHANCEMENT_LEVEL = 10` (injected). Matching tier scroll in inventory.
*Action*: `ConfirmEnhancement`.
*Pass*: Returns `RejectedAtMaxLevel`. Scroll remains. Item at level 10.

**AC-ENH-15: PrestigeBand Bits — NONE Below Level 5**
*Setup*: Item at +4, equipped.
*Action*: Read `EquipmentSlotRecord.equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `00`.

**AC-ENH-16: PrestigeBand Bits — VISIBLE_NO_GLOW at Level 5**
*Setup*: Item advanced to +5 (injected), equipped.
*Action*: Read `EquipmentSlotRecord.equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `01`.

**AC-ENH-17: PrestigeBand Bits — HIGH at Level 8**
*Setup*: Item at +8 (injected), equipped.
*Action*: Read `EquipmentSlotRecord.equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `11`.

**AC-ENH-18: Server Broadcast at +9 — Content Correct, Not Fired for Other Levels**
*Setup*: Player "TestPlayer" has "Dark Steel Sword" at +8. RNG injected for success.
*Action*: `ConfirmEnhancement` to reach +9.
*Pass*: All clients receive `ServerBroadcast_Enhancement9 { playerName: "TestPlayer", itemName: "Dark Steel Sword" }`. No broadcast was sent for the +1 through +8 transitions.

**AC-ENH-19: Flat Bonus Formula (F-ENH-1)**
*Setup*: Bronze item with `StatModifiers = [{ FlatBonus: 10 }]` at +5.
*Action*: `IEnhancementBonusProvider.GetFlatBonus(5, 10, GearTier.Bronze)`.
*Pass*: Returns 25.

**AC-ENH-20: Elemental Bonus Formula (F-ENH-2)**
*Setup*: Dark Steel Sword, `ElementalDamage = 15`, at +7.
*Action*: `IEnhancementBonusProvider.GetElementalBonus(7, GearTier.DarkSteel, true)`.
*Pass*: Returns 50.

**AC-ENH-21: Non-Weapon Elemental Bonus Returns 0**
*Setup*: Bronze Armor (non-weapon) at +5.
*Action*: `IEnhancementBonusProvider.GetElementalBonus(5, GearTier.Bronze, false)`.
*Pass*: Returns 0.

**AC-ENH-22: Item Removed During Selection — Rejection**
*Setup*: Player selects item in slot 3. Before `ConfirmEnhancement`, inject `RemoveItem(3)` server-side.
*Action*: `ConfirmEnhancement(3, scrollSlot)`.
*Pass*: Returns `RejectedItemNotFound`. Scroll remains.

**AC-ENH-23: Atomic Commit — Write Failure Rolls Back**
*Setup*: Item at +4 (destruction possible). RNG injected for destruction. Inject database write failure at step 6.
*Action*: `ConfirmEnhancement`.
*Pass*: Scroll remains in inventory. Item unmodified. Server logs `CriticalEnhancementWriteFailed`. Slot is unlocked after rollback detection.

**AC-ENH-24: Scroll Source Restriction — No Monster Loot Table Entry**
*Setup*: All monster loot table entries in Item Database (automated scan).
*Action*: Search all loot tables for any Enhancement Scroll `ItemId`.
*Pass*: Zero matches. Enhancement Scrolls appear only in NPC Shop purchase records.

**AC-ENH-25: UI Shows Probability Before Confirm**
*Setup*: Player selects a +4 Bronze item and a Bronze Enhancement Scroll in the Enhancement UI.
*Action*: Observe UI state before tapping Confirm.
*Pass*: UI displays P_s = 0.65, P_d = 0.35. No third probability category displayed — only P_s and P_d appear. Heightened destruction warning visible (P_d ≥ 0.35). Confirm button requires explicit tap.

**AC-ENH-26: UI Shows P_d at All Levels — Heightened Warning Only Above Threshold**
*Setup*: Player selects a +2 Bronze item and Bronze Enhancement Scroll at the Enhancement NPC.
*Action*: Observe Enhancement UI.
*Pass*: UI displays P_s = 0.85, P_d = 0.15. Standard P_d display visible. No heightened warning present (P_d < 0.35). Confirm button tappable immediately.

**AC-ENH-27: Enhancement NPC Location Restriction**
*Setup*: Player is in an active combat zone (not the town hub). `NPCInteractionActive = false` for this player. Player inventory contains a valid item and a matching tier scroll.
*Action*: Send `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)` directly (no prior `OpenNPCInteraction`).
*Pass*: Returns `RejectedNoNPCSession`. No slot is locked, no scroll is consumed, no state changes.

**AC-ENH-28: NPC Interaction Opens Successfully in Town Hub**
*Setup*: Player is in the town hub zone. `NPCInteractionActive = false`.
*Action*: Send `OpenNPCInteraction(enhancementNpcId)`.
*Pass*: Server returns `NPCInteractionOpened`. `NPCInteractionActive = true` server-side.

**AC-ENH-29: CloseNPCInteraction Clears Flag**
*Setup*: Player has an active NPC session (`NPCInteractionActive = true`).
*Action*: Send `CloseNPCInteraction()`.
*Pass*: `NPCInteractionActive = false`. Subsequent `ConfirmEnhancement` returns `RejectedNoNPCSession`.

**AC-ENH-30: Zone Transition Clears NPCInteractionActive**
*Setup*: Player has an active NPC session (`NPCInteractionActive = true`). Inject zone transition server-side.
*Action*: Zone transition completes.
*Pass*: `NPCInteractionActive = false` without explicit `CloseNPCInteraction`. Subsequent `ConfirmEnhancement` returns `RejectedNoNPCSession`.

**AC-ENH-31: High-Risk Warning Gates Confirm Button**
*Setup*: Player is at the Enhancement NPC in town hub. Player selects a +4 Bronze item and a Bronze Enhancement Scroll (`P_d = 0.35` — at the heightened warning threshold).
*Action*: Observe Enhancement UI state after selection, before any user interaction with the warning.
*Pass*: Confirm button is not tappable / disabled until the player performs the required acknowledgment action. Confirm becomes tappable only after acknowledgment.

**AC-ENH-32: PrestigeBand Bits — GLOW_LOW at Level 7**
*Setup*: Item advanced to +7 (injected), equipped.
*Action*: Read `EquipmentSlotRecord.equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `10`.

## Open Questions

**OQ-ENH-1: Scroll pricing for Bronze/Iron/Steel tiers**
Recommended prices (TK-ENH-9) are provisional. Requires playtest validation once farming rates are measured. Currency System farming-rate invariant (30–50 min same-tier) is the binding constraint.

**OQ-ENH-2: Enhancement UI entry point — RESOLVED**
Enhancement UI is accessed exclusively by interacting with the Enhancement NPC in the town hub. No inline inventory access. Travel to town is required. See CR-ENH-16.

**OQ-ENH-3: Lock timeout for LOCKED/RESOLVING state on server crash**
CR-ENH-7 specifies that slot locks are session-scoped and expire on session end (EC-ENH-2). If the server (not the client) crashes mid-attempt after scroll consumption, does the scroll refund on recovery? Current spec: no refund (scroll consumed at step 4, before crash window). Requires confirmation from server recovery architecture.

**OQ-ENH-4: +9 broadcast for items obtained at +9 via trade**
CR-ENH-14 fires the broadcast at the moment of the successful +9 transition. If a +9 item is traded, no new broadcast fires. Is a transfer announcement needed? Deferred to social systems design.

**OQ-ENH-5: Enhancement level display in zone (third-person view)**
VR-ENH-4 specifies PrestigeBand glow is visible at range. Should the enhancement level number be visible in the name tag above the player in zone? Currently: number revealed only on inspect. Requires input from UX and art direction.

**OQ-ENH-6: Character Persistence GDD scope**
Section F lists Character Persistence (Not Started) as a downstream dependency. The `EnhancementLevel` field must be included in the persistence payload. This must be specified before the Character Persistence GDD is authored.
