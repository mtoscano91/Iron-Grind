# Enhancement System

> **Status**: Approved (Pass 7 lean, 2026-10-01 — Revision Pass 3 verified, Pass 6 blockers closed). Pre-implementation gates before `/create-epics`: OQ-ENH-7, wire-protocol Enhancement message set (TD-046). Item Database amendment #4 applied 2026-10-02 (item-database.md Rule 13 — gate closed). Previously Approved (Pass 4 lean, 2026-05-23)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-10-07 (OQ-ENH-7 resolved by ADR-011 — CR-ENH-18 last sentence and the OQ entry; wording only, no rule change, no review pass); 2026-10-01 (Revision Pass 3 — Pass 6 lean-review items: `RejectedItemEquipped` removed (equipped items are unaddressable — CR-ENH-4, AC-ENH-4 rewritten); AC-ENH-7 now tests the Inventory lock directly, the client-request path stays in AC-ENH-38; the NPC session gains the 300s wall-clock lifetime shared with the NPC Shop (CR-ENH-17, AC-ENH-39 added); `IsAttemptInProgress` covers `RESULT_*`; `outcome` / `newLevel` ignored on rejection; OQ-ENH-7 covers server-originated mutations; Player Fantasy no longer refers to a destruction threshold.) Earlier the same day (Revision Pass 2 — Pass 5 lean-review blockers: the outcome is applied to the bag before the commit (CR-ENH-15 step 6a/6b) with a caller-owned rollback via `ForceInsert` / `SetEnhancementLevel` / `PickupRequest`; `GetElementalBonus` gains `baseElementalDamage`; `RejectedScrollNotFound` and `RejectedNotUpgradeable` added; CR-ENH-18 attempt exclusivity added; a client disconnect no longer rolls an attempt back; AC-ENH-33..38 added.) Earlier the same day (TD-045/TD-043 design session: CR-ENH-12/15 now name the Inventory API — level written via `Inventory.SetEnhancementLevel` on the locked slot, scroll consumed via `Inventory.ConsumeItem(scrollItemID, 1)` instead of `RemoveItem`, destruction via `Inventory.RemoveItem`; upstream amendments #2 and #3 applied to the Equipment and Inventory GDDs.) Previous: 2026-05-23
> **Implements Pillar**: Legendary Gear (primary), Earned Power (secondary)

## Overview

The Enhancement System is the attempt-based upgrade engine for all equippable items in Project Iron Grind. A player visits the Enhancement NPC in the town hub, selects an eligible item from their inventory, spends an Enhancement Scroll (purchased from the NPC Shop), and the server resolves a probability-weighted attempt: on success, the item's enhancement level increases by +1 and its flat stat bonuses grow; on failure, both the item and the scroll are permanently lost. Enhancement levels +1 through +5 close the stat gap to the next gear tier — a +5 item of any tier has approximately the same effective stats as an unenhanced item of the tier above. Levels +6 and beyond enter prestige territory, where the item outperforms the next tier's baseline and its enhancement level becomes visible to every player in the zone through the PrestigeBand encoding on the equipment appearance byte. Reaching enhancement level +9 on any weapon triggers a server-wide broadcast to all online players, making it a social event rather than a private milestone. The system locks the target inventory slot during each attempt and removes the item from inventory on destruction; both the enhancement level and current slot state are persisted across sessions. Enhancement exists to turn farmed time into a risk decision: every scroll purchase is a bet, and the same grinding loop that yields Bronze gear also yields the scrolls that can make that gear legendary — or destroy it.

## Player Fantasy

The moment a Dark Steel blade enters the zone hub with a High-band glow, the conversation stops. Other players read the shape of the light before they tap to inspect. When they do, there is the number. That number is the story — hours of farming, a calculated string of bets, failures survived, a final push at odds most players walk away from. Enhancement is the system that turns that time into a credential anyone on the server can read.

Each attempt is made with full information: the success percentage is visible before the scroll is spent. The player decides with eyes open — push to +7 at 34% success, or bank this win and walk away. Confirmation sends the percentage away, the slot locks, and for a fraction of a second the outcome is unwritten. The success flash is relief. The failure flash is a gut-punch the player chose to risk — at every level, a failed attempt costs the item. There are no hidden pity mechanics, no false safety net. What the UI shows is what the server rolls. That honesty is what makes the wager feel real.

Scarcity is not incidental — destruction on every failed attempt is the engine behind what the glow means. When someone reaches +9, the server broadcasts it to everyone online by name: "Arion has enhanced a Dark Steel Sword to +9." The PrestigeBand glow is readable at range; the exact number is only revealed on deliberate inspect. Strangers form a read before they know the answer, and ask when they want to know more. A high-enhancement item carries its history wherever it goes. The Enhancement System's job is to make that history legible to the world.

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
Only items currently in an inventory slot (not equipped) may be enhanced. `ConfirmEnhancement` identifies its target by bag slot index only, so an equipped item cannot be addressed: no request field refers to a gear slot, and no rejection code exists for this case. If the item that was in the named bag slot has since been equipped, the slot is empty and step 2 returns `RejectedItemNotFound` (EC-ENH-4). The player must unequip the item before attempting enhancement. *(Revision Pass 3: `RejectedItemEquipped` removed — no check could produce it.)*

**CR-ENH-5: Accessory Exclusion**
Items with `GearSlot.Ring` or `GearSlot.Necklace` cannot be targeted. Targeting an accessory returns `RejectedAccessoryType`. Accessories are upgraded exclusively via the Accessory Merge mechanic (Equipment System CR-EQS-14).

**CR-ENH-6: Irrevocability Gate (Two-Tap)**
Item and scroll selection in the UI do not constitute commitment. The server locks the target slot and consumes the scroll only upon receipt of an explicit `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)` request. Before that request is sent, the player may send `CancelEnhancement` at any time with no penalty. Once `ConfirmEnhancement` is sent, the attempt is irrevocable.

**CR-ENH-7: Slot Lock Duration**
Upon receipt of `ConfirmEnhancement`, the server calls `Inventory.LockSlot(slotIndex)` before any other operation. The slot remains locked for the entire attempt lifecycle. The server calls `Inventory.UnlockSlot(slotIndex)` exactly once, after the outcome is committed to persistent storage and the slot is cleared or updated (or, on a failed commit, after the rollback — CR-ENH-15).

*Lock lifetime:* steps 3–6a run within one server tick and step 6b ends when Character Persistence returns a result (a timed-out write returns a non-Success code — character-persistence.md EC-CP-5), so a lock never outlives its attempt: every exit path (success, destruction, step 4 failure, commit failure) ends in `UnlockSlot` or `RemoveItem`. Locks are in-memory only and are never saved; a server crash clears them. No separate lock timeout exists (answers inventory-system.md OQ-INV-4).

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
Steps 3–6 of the attempt sequence (CR-ENH-15) form one atomic unit: the in-memory changes of steps 3–6a become durable through a single database write at step 6b (`SaveIrreversibleOutcome` — one transaction, character-persistence.md CR-CP-6). The server sends result messages only after that write succeeds. If the write fails, this system rolls back every in-memory change (CR-ENH-15 Rollback): the scroll is back in inventory, the item is present at its previous level, and the slot is unlocked. If the server crashes before the write completes, the saved record still holds the pre-attempt state and the player loads with scroll and item intact. A client disconnect does not abort an attempt: once step 3 has run, the server completes the sequence regardless of the connection (EC-ENH-1), so disconnecting cannot avoid a destruction. On reconnect, the client reads the committed state. No client-side recovery flow is required.

*Pre-commit events:* `ConsumeItem`, `SetEnhancementLevel` and `RemoveItem` each raise the Inventory System's `InventoryChangedEvent` at steps 4 and 6a — before the commit. These are server-side events. Any client-facing inventory message derived from them for this character must be held until step 6b succeeds and dropped if it fails; the future client inventory-sync message (TD-046) must honor this.

**CR-ENH-12: Prestige Band Encoding**
The enhancement level maps to a PrestigeBand encoded in the character's `equipmentAppearanceFlags[2:1]` (one byte per character — see the contract mapping below). Four states occupy all 2-bit values in monotonically increasing order:

| Band | Level Range | Bits [2:1] | Glow visible at range |
|------|------------|------------|----------------------|
| NONE | 0–4 | `00` | No |
| VISIBLE_NO_GLOW | 5–6 | `01` | No — badge color only |
| GLOW_LOW | 7 | `10` | Yes — low intensity |
| HIGH | 8–10 | `11` | Yes — high intensity |

The PrestigeBand is visible to other players at zone range. The exact enhancement level is not visible at range; it is revealed only on deliberate inspect.

The Enhancement System writes `EnhancementLevel` to `InventorySlotRecord` only. The Equipment System reads `InventorySlotRecord.EnhancementLevel` when equipping an item; it sets `EquipmentSlotRecord.EnhancementLevel = InventorySlotRecord.EnhancementLevel` and recomputes the character's `equipmentAppearanceFlags[2:1]` from the Weapon slot's level. The Enhancement System does not write directly to `EquipmentSlotRecord`.

*Contract mapping (2026-10-01, TD-045):* `InventorySlotRecord` is the Inventory System's slot `{ItemID, Quantity, EnhancementLevel}` (inventory-system.md Rule 1.1/1.4). The write is `Inventory.SetEnhancementLevel(slotIndex, level)`, which the Inventory System accepts only on a slot this system currently holds locked (Rule 5.14a). The Equipment System never writes an inventory slot's level: it receives the level in `MoveItemOutResult.EnhancementLevel` when the item is equipped and returns it via `MoveItemIn(ItemID, enhancementLevel)` on unequip (equipment-system.md CR-EQS-1/5/6/7). Its only direct slot read is the read-only `GetSlot(slotIndex)` peek used by its same-item guard and request validation (CR-EQS-4/13). `EquipmentSlotRecord` is the Equipment System's `EquipmentSlotEntry`. `equipmentAppearanceFlags` is one byte per character (`ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags`), not per slot; its PrestigeBand bits are computed from the **Weapon slot's** level only — the level of any other equipped item never affects them (equipment-system.md CR-EQS-11; OQ-EQS-8 resolved 2026-10-01).

**CR-ENH-13: Prestige Glow**
Items enhanced to `ENHANCEMENT_GLOW_THRESHOLD` (7) or above render with a visual glow when equipped. The VFX System renders a glow only when `EnhancementLevel ≥ ENHANCEMENT_GLOW_THRESHOLD`; it reads `equipmentAppearanceFlags[2:1]` to select the glow intensity variant (GLOW_LOW band = low glow; HIGH band = high glow).

**CR-ENH-14: Server Broadcast at +9**
When an item successfully reaches enhancement level 9, the server sends a broadcast to all online players:
> `"{PlayerName} has enhanced a {ItemName} to +9."`

The broadcast fires once per successful +9 transition. `ItemName` is the Item Database display name. No broadcast fires for other level transitions.

**CR-ENH-15: Attempt Sequence**
Steps execute in this order; the server does not advance to the next step if the current step fails:

1. Server receives `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)`.
2. Server validates: `NPCInteractionActive = true` for this player, item exists at `itemSlotIndex`, slot unlocked, `IsUpgradeable = true`, `GearSlot ≠ Ring/Necklace`, `EnhancementLevel < MAX_ENHANCEMENT_LEVEL`, scroll exists at `scrollSlotIndex`, scroll `TargetGearTier` matches item `GearTier`, no concurrent attempt for this player. If any check fails, return its rejection code; no state changes. Codes: no NPC session → `RejectedNoNPCSession`; item absent → `RejectedItemNotFound`; slot locked or another attempt in progress → `RejectedConcurrentAttempt`; `IsUpgradeable = false` → `RejectedNotUpgradeable`; accessory → `RejectedAccessoryType`; at maximum level → `RejectedAtMaxLevel`; scroll slot empty or not holding an Enhancement Scroll (an item whose `ScrollData` is `null` — item-database.md Rule 13) → `RejectedScrollNotFound`; tier mismatch → `RejectedTierMismatch`.
3. Server calls `Inventory.LockSlot(itemSlotIndex)`.
4. Server consumes exactly one scroll via `Inventory.ConsumeItem(scrollItemID, 1)`, where `scrollItemID` is the `ItemID` validated at `scrollSlotIndex` in step 2. Scrolls are stackable Consumables, so `RemoveItem` (which clears a whole slot) must not be used. `ConsumeItem` takes the unit from the lowest-index unlocked stack of that scroll type — normally `scrollSlotIndex`, but another stack of the same scroll if it sits at a lower index; scrolls of one type are interchangeable. If `ConsumeItem` fails (no scroll left — e.g. discarded earlier in the same tick), the server calls `Inventory.UnlockSlot(itemSlotIndex)`, returns `RejectedScrollNotFound`, and rolls no outcome. *(Changed 2026-10-01, TD-043.)*
5. Server draws `r ∈ [0, 1)`, resolves outcome per CR-ENH-9.
6. Server applies the outcome, then commits it — in this order:
   - **6a. Apply to the bag.** On success: `Inventory.SetEnhancementLevel(itemSlotIndex, EnhancementLevel + 1)` (the slot is still locked from step 3). On destruction: `Inventory.RemoveItem(itemSlotIndex)` (clears the slot and its lock).
   - **6b. Commit.** `CharacterPersistence.SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger.EnhancementResult)`. The save reads the live bag (`Inventory.ExportSnapshot`), so the committed record holds the scroll consumed and the item at its new level or absent. On any non-Success result the server performs the Rollback below and does not continue.
7. Server calls `Inventory.UnlockSlot(itemSlotIndex)` (a no-op after a destruction — `RemoveItem` already cleared the lock).
8. If `EnhancementLevel` reached 9, server sends `ServerBroadcast_Enhancement9 { playerName, itemName }` to all online players.
9. Server sends `EnhancementAttemptResult { outcome, newLevel, resultCode }` to client.

**Transaction boundary:** Steps 3–6a are in-memory changes made within a single server tick. Step 6b is the only database write — one transaction (character-persistence.md CR-CP-6). Steps 7–9 execute only after step 6b returns `Success`.

**Rollback (step 6b returns any non-Success code):** rollback is caller-owned (character-persistence.md CR-CP-5). This system restores the bag in this order; steps 8–9 do not run:
1. *Item.* After a success outcome: `Inventory.SetEnhancementLevel(itemSlotIndex, previousLevel)` (the slot is still locked). After a destruction outcome: `Inventory.ForceInsert(itemID, previousLevel)` — the item returns to a free slot at quantity 1, which may be a different slot from `itemSlotIndex`. The slot it vacated is still free (CR-ENH-18), so the call cannot fail for lack of space.
2. *Scroll.* `Inventory.PickupRequest(CharacterID, scrollItemID, 1)` — tops up the stack the unit was taken from, or refills the slot it emptied.
3. `Inventory.UnlockSlot(itemSlotIndex)` (a no-op if the slot was cleared), then log `CriticalEnhancementWriteFailed`.
4. Character Persistence disconnects the client and preserves the rolled-back session for `SESSION_TTL_SECONDS` (CR-CP-5). No `EnhancementAttemptResult` and no +9 broadcast are sent.

If a rollback call itself fails — a caller bug; CR-ENH-18 makes it unreachable — the server logs `CriticalEnhancementRollbackFailed` with the character, the item's `ItemID` and level, and the scroll's `ItemID` for manual restoration, and continues with the remaining rollback steps.

**CR-ENH-16: Enhancement NPC — Location Requirement**
Enhancement may only be initiated by interacting with the Enhancement NPC in the town hub. The Enhancement UI cannot be opened from the inventory screen or from any zone outside the town hub. This prevents enhancement during combat, in dungeons, or in the field.

**CR-ENH-17: NPC Interaction Session**
The server tracks an `NPCInteractionActive: bool` flag per player session. Flag lifecycle:
- **Open**: Client sends `OpenNPCInteraction(npcId)` when the player taps the Enhancement NPC. Server validates player is in the town hub zone; if valid, sets `NPCInteractionActive = true` and returns `NPCInteractionOpened`. If the player is not in the town hub, the server returns `RejectedNotInTownHub` and the flag stays false. The client opens the Enhancement UI on success.
- **Close**: Client sends `CloseNPCInteraction()` when closing the UI or walking away. Server sets `NPCInteractionActive = false`. The server also clears the flag on: zone transition; expiry of the session's wall-clock lifetime — `SESSION_TTL_SECONDS` (300s) measured from the moment `OpenNPCInteraction` succeeds, regardless of client activity (the same rule as npc-shop.md CR-SHOP-3 — the flag is shared, so one lifetime applies; a new `OpenNPCInteraction` restarts it); session end (logout, or `SESSION_TTL_SECONDS` expiry after a disconnect); and pre-emption — an `OpenNPCInteraction` for another NPC (e.g. the NPC Shop, npc-shop.md CR-SHOP-3) clears this session before the new one opens. Closing has no penalty.
- **Enforcement**: `ConfirmEnhancement` validation (CR-ENH-15 step 2) checks `NPCInteractionActive = true`; if false, returns `RejectedNoNPCSession` with no state changes.
- **In-flight attempts**: the flag is read only at step 2. Clearing it (close, expiry, pre-emption, zone transition, disconnect) never cancels an attempt that has passed step 2; the attempt runs to step 9 and `EnhancementAttemptResult` is delivered on the player's session regardless of the flag. No pre-emption callback is needed (answers npc-shop.md OQ-NS-6 from this side).

**CR-ENH-18: Attempt Exclusivity**
From step 3 until the attempt returns to `IDLE`, the server processes no other inventory-mutating request for that character — move, equip, unequip, discard, sell, buy, consumable use, pickup, accessory merge. Requests that arrive in that window are held and processed in arrival order once the attempt is `IDLE`. This matters only while the step 6b write is in flight (steps 3–6a complete within one tick). It guarantees that the bag the Rollback restores is the bag the attempt left, so the rollback calls cannot fail for lack of space. The Enhancement System exposes `IsAttemptInProgress(CharacterID): bool` (true from step 3 until the attempt returns to `IDLE` — states `LOCKED`, `RESOLVING` and `RESULT_*`). A second `ConfirmEnhancement` is never held: CR-ENH-8 rejects it. The layer that holds the requests is decided by ADR-011 (OQ-ENH-7, resolved 2026-10-07): the session's request dispatcher holds client requests behind a per-character mutation gate; a server-originated bag mutation (e.g. a loot pickup) is not queued — it is not attempted while the gate is closed and is retried once it opens.

---

### States and Transitions

| State | Description | Entry Condition | Valid Transitions |
|-------|-------------|-----------------|------------------|
| `IDLE` | No active attempt; selection UI is non-binding | Session start; previous attempt complete | Player sends `ConfirmEnhancement` → `VALIDATING` |
| `VALIDATING` | Server checking all preconditions (step 2) | `ConfirmEnhancement` received | Validation fails → `IDLE` (rejection code returned, no state change); validation passes → `LOCKED` |
| `LOCKED` | Slot locked; scroll consumed; RNG resolved (steps 3–5) | All preconditions pass | RNG resolved → `RESOLVING`; step 4 `ConsumeItem` fails → `IDLE` (slot unlocked, `RejectedScrollNotFound` returned, no outcome rolled) |
| `RESOLVING` | Outcome applied to the bag and committed to persistent storage (steps 6a–6b) | RNG outcome known | Commit succeeds → `RESULT_SUCCESS` or `RESULT_DESTRUCTION`; commit fails → `IDLE` (Rollback per CR-ENH-15; no result sent; client disconnected per CR-CP-5) |
| `RESULT_SUCCESS` | Level incremented; slot unlocked; result sent (steps 7–9) | Commit: success | Server sends `EnhancementAttemptResult` → `IDLE` |
| `RESULT_DESTRUCTION` | Item removed; slot cleared and unlocked; result sent (steps 7–9) | Commit: destruction | Server sends `EnhancementAttemptResult` → `IDLE` |

Invalid transitions: no state may reach `LOCKED` or `RESOLVING` without passing through `VALIDATING`. All `RESULT_*` states resolve to `IDLE` only.

---

### Interactions with Other Systems

| System | Enhancement System Reads | Enhancement System Writes / Signals | Interface Owner |
|--------|--------------------------|-------------------------------------|-----------------|
| **Item Database** | `IsUpgradeable: bool`, `GearTier`, `GearSlot`, `StatModifiers[].FlatBonus`, `ElementalDamage`, display name | — (stateless; no mutations) | Item Database |
| **Inventory System** | Slot `ItemID` + `EnhancementLevel` (`InventorySlotRecord.EnhancementLevel`), `IsSlotLocked(slotIndex)`, item and scroll existence | `LockSlot(slotIndex)`, `UnlockSlot(slotIndex)`, `ConsumeItem(scrollItemID, 1)` (scroll — one unit), `SetEnhancementLevel(itemSlotIndex, level)` on success, `RemoveItem(itemSlotIndex)` on destruction. On a failed commit only (CR-ENH-15 Rollback): `ForceInsert(itemID, previousLevel)` to put a destroyed item back, `PickupRequest(CharacterID, scrollItemID, 1)` to put the scroll back | Inventory System owns all methods and stores the level; Enhancement System is the only caller of `LockSlot`/`UnlockSlot`/`SetEnhancementLevel`/`RemoveItem` and decides the values written *(updated 2026-10-01, TD-043/TD-045; rollback calls added in Revision Pass 2)* |
| **Equipment System** | — | Exposes `IEnhancementBonusProvider` (below) for Equipment System to consume. Does not write to `EquipmentSlotRecord` — item cannot be equipped during enhancement; the Equipment System receives the level in `MoveItemOutResult.EnhancementLevel` on equip and recomputes `equipmentAppearanceFlags[2:1]` (CR-ENH-12 contract mapping) | Equipment System consumes `IEnhancementBonusProvider` and owns `equipmentAppearanceFlags` update on equip |
| **Damage Calculation** | — | `IEnhancementBonusProvider.GetElementalBonus(level: int, baseElementalDamage: int, gearTier: GearTier, isWeapon: bool): int` — the weapon's enhanced elemental damage (F-ENH-2: base + enhancement, clamped); returns 0 when `isWeapon` is false. Damage Calculation supplies `baseElementalDamage` from the Item Database | Enhancement System owns and implements; Damage Calculation consumes |
| **Character Persistence** | — | Calls `SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger.EnhancementResult)` at CR-ENH-15 step 6b; on a non-Success result performs the caller-owned rollback (CR-CP-5). `EnhancementLevel` in `InventorySlotRecord` and `EquipmentSlotRecord` must be included in all save and load payloads | Character Persistence |
| **Networking** | Client → Server: `OpenNPCInteraction(npcId)`, `CloseNPCInteraction()`, `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)`, `CancelEnhancement` | Server → Client: `NPCInteractionOpened`, `EnhancementAttemptResult { outcome, newLevel, resultCode }`; Server → All: `ServerBroadcast_Enhancement9 { playerName, itemName }` | Enhancement System authors all server-originated messages |
| **Enhancement UI** | Player input: item selection, scroll selection, confirm, cancel | `EnhancementStateUpdate { currentLevel, P_s, P_d }` on selection; `EnhancementAttemptResult` on attempt completion | Enhancement UI consumes Enhancement System messages |
| **VFX System** | — | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`, `OnPrestigeBandChange(newBand)` | VFX System subscribes |
| **Audio System** | — | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()` | Audio System subscribes |
| **NPC Shop** | — | — (scroll purchase handled by NPC Shop; both systems share the `NPCInteractionActive` flag — opening the shop pre-empts an Enhancement NPC session, CR-ENH-17; Enhancement System has no Currency System dependency at MVP) | NPC Shop |

**IEnhancementBonusProvider interface** (owned by Enhancement System, consumed by Equipment System and Damage Calculation):
- `GetFlatBonus(level: int, baseFlatBonus: int, gearTier: GearTier): int` — enhanced flat stat value for a given level, base modifier, and gear tier (selects the correct `BonusPerLevel[tier]`)
- `GetElementalBonus(level: int, baseElementalDamage: int, gearTier: GearTier, isWeapon: bool): int` — enhanced elemental damage per F-ENH-2 for the given level, base value (the weapon's Item Database `ElementalDamage`) and tier, clamped to `ElementalDamage_ceiling`; returns 0 if `isWeapon` is false *(base parameter added 2026-10-01 — without it the method could not return the F-ENH-2 value)*

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

`IEnhancementBonusProvider.GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)` returns this value; the caller supplies `baseElementalDamage` = `item.ElementalDamage` from the Item Database. At level 0 the result is the base value — a +0 elemental weapon deals its base elemental damage. Non-weapon items do not gain elemental bonuses: the method returns 0 when `isWeapon` is false.

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

**Economy validation**: At Dark Steel Scroll price 350g and Dark Steel item sell price 270g, expected cost to reach +9 ≈ 955K gold. Scroll-to-item-value ratio ≈ 3,533×. The scroll economy is the primary gold sink; destroyed item value is negligible relative to scroll investment. These figures assume scrolls are bought, never dropped — true at MVP (AC-ENH-24); post-MVP scroll drops (OQ-ENH-9) require this validation to be redone. `GoldTransactionReason.Enhancement = 5` (pre-allocated in Currency System).

## Edge Cases

**EC-ENH-1: Mid-Attempt Disconnect**
If the client disconnects after sending `ConfirmEnhancement` but before receiving `EnhancementAttemptResult`, the server continues processing. Per CR-ENH-11, the outcome is committed to persistent storage before the result message is sent. On reconnect, the client reads the committed state from `InventorySlotRecord` or `EquipmentSlotRecord`. The item is either present at its new enhancement level (success) or absent (destruction). No rollback and no recovery flow are triggered. The server does not re-send the result message on reconnect.

**EC-ENH-2: Disconnect or Server Crash While LOCKED or RESOLVING**
A client disconnect in `LOCKED` or `RESOLVING` does not abort the attempt: steps 3–6a complete within one server tick and the step 6b write runs to completion (CR-ENH-11, EC-ENH-1). The outcome stands if the write succeeds. If the server process crashes before the step 6b write completes, every in-memory change is lost with the process and the saved record holds the pre-attempt state: on the next login both scroll and item are present, the item at its previous level, and no slot is locked (locks are never saved). The scroll is irrevocably consumed only when the step 6b write succeeds.

**EC-ENH-3: Attempt at MAX_ENHANCEMENT_LEVEL**
A player submits `ConfirmEnhancement` for an item already at `MAX_ENHANCEMENT_LEVEL` (10). The server rejects at CR-ENH-15 step 2 and returns `RejectedAtMaxLevel`. No scroll is consumed, no slot is locked, no state changes.

**EC-ENH-4: Item Moved or Removed During Selection (Pre-Confirm)**
Between client item selection and `ConfirmEnhancement` send, another operation removes the item (trade, discard, concurrent equip). The server's step 2 validation detects the item is absent or slot mismatched and returns `RejectedItemNotFound`. No scroll is consumed. The UI must handle this rejection gracefully and return the player to IDLE.

**EC-ENH-5: App Backgrounded Mid-Selection (Pre-Confirm)**
The player backgrounds the app between item/scroll selection and the Confirm tap. No server request has been sent; no slot is locked; no scroll is consumed. On foreground return, the UI restores its local selection state (item, scroll, displayed probabilities) and re-requests `EnhancementStateUpdate` for the saved selection to detect any state changes that occurred while backgrounded (e.g., the selected item was moved or removed by another operation). No server action is required if the selection is still valid.

**EC-ENH-6: Commit Failure — Outcome Rolled Back**
The step 6b write saves the scroll consumption and the outcome (the new level, or the item's absence) in one record, so no saved state exists in which the scroll survives a destruction, or in which the item holds its new level with the scroll unspent. If `SaveIrreversibleOutcome(EnhancementResult)` returns any non-Success code, this system performs the CR-ENH-15 Rollback: the item is restored at its previous level (in a free slot, after a destruction), the scroll is restored, the slot is unlocked, and `CriticalEnhancementWriteFailed` is logged. Per character-persistence.md CR-CP-5 the client is then disconnected, a critical alert fires, and the rolled-back session is preserved for `SESSION_TTL_SECONDS`. No result message and no +9 broadcast are sent.

**EC-ENH-7: Tier-Mismatched Scroll Selected**
A player submits `ConfirmEnhancement` with a scroll whose `TargetGearTier` does not match the item's `GearTier`. The server rejects at step 2 and returns `RejectedTierMismatch`. No scroll is consumed. The UI should filter the scroll list by the selected item's tier client-side, but server enforcement is authoritative.

**EC-ENH-8: +9 Broadcast Under Server Load**
If `ServerBroadcast_Enhancement9` cannot be delivered to all players (high load, partial connectivity), the broadcast is sent on a best-effort basis. Delivery failure does not affect the enhancement outcome. The enhancement is committed regardless of broadcast success.

## Dependencies

### Upstream Dependencies

| System | GDD Status | What Enhancement System Requires |
|--------|-----------|----------------------------------|
| **Item Database** | Approved | `IsUpgradeable: bool`, `GearTier`, `GearSlot`, `StatModifiers[].FlatBonus`, `ElementalDamage` per item; 4 Enhancement Scroll records (Bronze/Iron/Steel/Dark Steel) with `ScrollData.TargetGearTier` sub-schema (item-database.md Rule 13, added 2026-10-02 — `StackLimit = 99`, `ConsumableData` is `null` on scroll records) |
| **Inventory System** | Approved | `LockSlot(slotIndex)`, `UnlockSlot(slotIndex)`, `SetEnhancementLevel(slotIndex, level)`, `RemoveItem(slotIndex)`, `ConsumeItem(ItemID, quantity)`, `IsSlotLocked(slotIndex)`, `GetSlot(slotIndex)`; for the failed-commit Rollback only, `ForceInsert(ItemID, enhancementLevel)` and `PickupRequest(CharacterID, ItemID, quantity)`; slot record includes `EnhancementLevel: byte` (inventory-system.md Rule 1.4, added 2026-10-01). Reverse reference: the Inventory System reads this GDD's `MAX_ENHANCEMENT_LEVEL` as the upper bound for a bag item's level and declares it as a soft upstream dependency. |
| **Currency System** | Approved | `GoldTransactionReason.Enhancement = 5` (pre-allocated); scroll sale handled by NPC Shop; no direct Currency System dependency at MVP |
| **Character Persistence** | Approved | `SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger.EnhancementResult): Task<CharacterSaveResult>` — the step 6b commit; the CR-CP-5 failure protocol (caller-owned rollback, client disconnect, session preserved) |

### Downstream Dependencies

| System | GDD Status | What They Require from Enhancement System |
|--------|-----------|-------------------------------------------|
| **Equipment System** | Approved | `IEnhancementBonusProvider.GetFlatBonus(level, baseFlatBonus, gearTier)` in `AddEquipmentModifier`; `EquipmentSlotRecord.EnhancementLevel: byte`; `equipmentAppearanceFlags[2:1]` recomputed by the Equipment System from the Weapon slot's level on every equip / unequip (CR-ENH-12) — never written at enhancement time; F-EQS-2 Iron range correction (see below) |
| **Damage Calculation** | Approved | `IEnhancementBonusProvider.GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)` for the `ElementalBonus` input of F-DC-2 — Damage Calculation passes the weapon's base `ElementalDamage` from the Item Database and the level from `Equipment.GetEquippedWeaponEnhancementLevel(): byte` |
| **Character Persistence** | Approved | `EnhancementLevel: byte` per bag slot and per gear slot in the save/load payload; `IEnhancementBonusProvider` on load (equipment re-registration). Also an upstream dependency (above) |
| **NPC Shop** | Approved | Shares the `NPCInteractionActive` flag (CR-ENH-17 — opening the shop pre-empts an Enhancement NPC session; no callback, see OQ-NS-6 there); sells the four Enhancement Scrolls at the TK-ENH-9 prices; no direct API dependency |
| **Loot Table System** | Approved (CR-LT-16 amendment Approved 2026-10-07) | Enforces the MVP scroll source restriction: while `ALLOW_ENHANCEMENT_SCROLL_DROPS` is `false`, loot table validation rejects any entry naming an item with `ScrollData` (loot-table-system.md CR-LT-16, AC-LT-26; AC-ENH-24 here). No runtime API dependency. Post-MVP scroll drops: OQ-ENH-9 |
| **Enhancement UI** | Not Started | `EnhancementStateUpdate`, `EnhancementAttemptResult`, `ServerBroadcast_Enhancement9` message schemas; `ConfirmEnhancement` and `CancelEnhancement` request schemas |
| **VFX System** | Not Started | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`, `OnPrestigeBandChange(band)` signals; `ENHANCEMENT_GLOW_THRESHOLD = 7` |
| **Audio System** | Not Started | `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()` signals |

### Required Upstream Amendments (Approved GDDs)

Before implementation, these changes must be applied to already-approved documents:

1. **Equipment System F-EQS-2**: Correct Iron flat bonus range 16–22 → **22–28** (F-ENH-3 parity constraint). ✅ Applied 2026-05-23.
2. **Equipment System CR-EQS-11 / EquipmentSlotRecord**: Add `EnhancementLevel: byte`; update `AddEquipmentModifier` to use `IEnhancementBonusProvider.GetFlatBonus`. ✅ Applied 2026-10-01 (equipment-system.md CR-EQS-1, enhanced modifier registration rule, `GetEquippedWeaponEnhancementLevel()`; the registry entry was updated 2026-05-22 but the GDD body was not).
3. **Inventory System InventorySlotRecord**: Add `EnhancementLevel: byte`. ✅ Applied 2026-10-01 (inventory-system.md Rule 1.1/1.4, 5.14a, 7.20, 8.24a; snapshot and change event carry the level). Code: Inventory Story 010.
4. **Item Database**: Add 4 Enhancement Scroll records; add `ScrollData { TargetGearTier: GearTier }` sub-schema. ✅ Applied 2026-10-02 (item-database.md Rule 13, `ScrollData` schema table, AC-42–47; scroll `StackLimit = 99`). Code: follow-up Item Database story (not yet created).

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
  RejectedAtMaxLevel | RejectedTierMismatch | RejectedAccessoryType
  RejectedItemNotFound | RejectedConcurrentAttempt | RejectedNoNPCSession
  RejectedScrollNotFound | RejectedNotUpgradeable
```
All `Rejected*` codes return the player to IDLE with an appropriate message. No result animation plays on rejection. On a `Rejected*` code, `outcome` and `newLevel` carry no meaning and the client ignores them — `resultCode` is authoritative (final wire encoding: TD-046).

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
`OpenNPCInteraction` is sent when the player taps the Enhancement NPC; server responds with `NPCInteractionOpened`, or `RejectedNotInTownHub` if the player is not in the town hub. Confirm requires an explicit separate tap from item/scroll selection — not auto-submitted.

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

**AC-ENH-4: Equipped Item Cannot Be Targeted**
*Setup*: Bronze Sword at +2 equipped in the Weapon slot; the bag slot it was equipped from (slot 0) is empty. Bronze Enhancement Scroll in slot 1.
*Action*: `ConfirmEnhancement(0, 1)`.
*Pass*: Returns `RejectedItemNotFound`. Scroll remains. The equipped sword is still at level 2. No slot is locked.

**AC-ENH-5: Accessory Rejected**
*Setup*: Ring in inventory. Bronze Enhancement Scroll in inventory.
*Action*: `ConfirmEnhancement(ringSlot, scrollSlot)`.
*Pass*: Returns `RejectedAccessoryType`. Scroll remains.

**AC-ENH-6: Cancel Before Confirm — No State Change**
*Setup*: Player selects valid item and scroll (no `ConfirmEnhancement` sent).
*Action*: `CancelEnhancement`.
*Pass*: Slot was never locked. Scroll in inventory. Enhancement level unchanged. No error returned.

**AC-ENH-7: Slot Locked During Attempt**
*Setup*: Valid item and scroll. Test hook holds the step 6b write open (attempt in `RESOLVING`).
*Action*: During the pause, invoke the Inventory System's `MoveRequest(itemSlotIndex, otherSlot)` handling directly, server-side — not as a client request (client requests are held by CR-ENH-18; that path is AC-ENH-38).
*Pass*: `IsSlotLocked(itemSlotIndex)` returns true. The move returns `MoveResult(fail, reason=SourceLocked)` (inventory-system.md) and both slots are unchanged.

**AC-ENH-8: Concurrent Attempt Rejected**
*Setup*: One attempt paused in VALIDATING state (test injection). Player sends second `ConfirmEnhancement`.
*Action*: Second `ConfirmEnhancement` for any item.
*Pass*: Returns `RejectedConcurrentAttempt`. First attempt unaffected.

**AC-ENH-9: Success — Level Increments, Scroll Consumed**
*Setup*: Bronze item at +2, exactly one Bronze Enhancement Scroll (a stack of quantity 1). RNG injected: `r = 0.00`.
*Action*: `ConfirmEnhancement`.
*Pass*: `outcome = SUCCESS`, `InventorySlotRecord.EnhancementLevel = 3`. Scroll absent. Slot unlocked.

**AC-ENH-10: Fail-Destruction at Low Level — Item Removed, Scroll Consumed**
*Setup*: Bronze item at +2, exactly one Bronze Enhancement Scroll (a stack of quantity 1). RNG injected: `r = 0.90` (above P_s[2] = 0.85).
*Action*: `ConfirmEnhancement`.
*Pass*: `outcome = DESTRUCTION`. Item slot empty. Scroll absent. Slot unlocked.

**AC-ENH-11: Fail-Destruction — Item Removed, Scroll Consumed**
*Setup*: Bronze item at +4, exactly one Bronze Enhancement Scroll (a stack of quantity 1). RNG injected: `r = 0.99` (above P_s[4] = 0.65).
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
*Setup*: Weapon at +4, equipped in the Weapon slot.
*Action*: Read the character's `equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `00`.

**AC-ENH-16: PrestigeBand Bits — VISIBLE_NO_GLOW at Level 5**
*Setup*: Weapon advanced to +5 (injected), equipped in the Weapon slot.
*Action*: Read the character's `equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `01`.

**AC-ENH-17: PrestigeBand Bits — HIGH at Level 8**
*Setup*: Weapon at +8 (injected), equipped in the Weapon slot.
*Action*: Read the character's `equipmentAppearanceFlags[2:1]`.
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
*Action*: `IEnhancementBonusProvider.GetElementalBonus(7, 15, GearTier.DarkSteel, true)`; then `GetElementalBonus(0, 15, GearTier.DarkSteel, true)`; then `GetElementalBonus(10, 9990, GearTier.DarkSteel, true)`.
*Pass*: Returns 50 (`15 + 7 × 5`); 15 (the base value at +0); 9,999 (`9990 + 50` clamped to `ElementalDamage_ceiling`).

**AC-ENH-21: Non-Weapon Elemental Bonus Returns 0**
*Setup*: Bronze Armor (non-weapon) at +5.
*Action*: `IEnhancementBonusProvider.GetElementalBonus(5, 0, GearTier.Bronze, false)`.
*Pass*: Returns 0.

**AC-ENH-22: Item Removed During Selection — Rejection**
*Setup*: Player selects item in slot 3. Before `ConfirmEnhancement`, inject `RemoveItem(3)` server-side.
*Action*: `ConfirmEnhancement(3, scrollSlot)`.
*Pass*: Returns `RejectedItemNotFound`. Scroll remains.

**AC-ENH-23: Commit Failure After Destruction — Rolled Back**
*Setup*: Bronze item at +4 in slot 0; 3 Bronze Enhancement Scrolls in slot 1; all other slots empty. RNG injected for destruction (`r = 0.99`). `SaveIrreversibleOutcome` injected to return `DatabaseError` at step 6b.
*Action*: `ConfirmEnhancement(0, 1)`.
*Pass*: The bag holds the item exactly once, at level 4, in an unlocked slot. Slot 1 holds 3 scrolls. No `EnhancementAttemptResult` is sent. Server logs `CriticalEnhancementWriteFailed`. The client is disconnected (CR-CP-5).

**AC-ENH-24: Scroll Source Restriction (MVP) — No Monster Loot Table Entry**
*Setup*: A loot table set containing an entry that names an Enhancement Scroll (`ScrollData` non-null), with `ALLOW_ENHANCEMENT_SCROLL_DROPS = false`.
*Action*: Startup loot table validation runs (loot-table-system.md CR-LT-16).
*Pass*: The entry is reported as a validation error and no loot table registry is created (loot-table-system.md AC-LT-26). At MVP, Enhancement Scrolls appear only in NPC Shop purchase records.
*Note (2026-10-07)*: this is an MVP constraint, not a permanent one. Post-MVP, scrolls are intended to become a rare drop on some monsters — see OQ-ENH-9. Enforcement moved from a one-off scan to a validation rule because no production loot tables exist yet and every table must pass validation.

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
*Setup*: Weapon advanced to +7 (injected), equipped in the Weapon slot.
*Action*: Read the character's `equipmentAppearanceFlags[2:1]`.
*Pass*: Bits = `10`.

**AC-ENH-33: One Scroll Consumed From a Stack**
*Setup*: Bronze item at +2 in slot 0; 5 Bronze Enhancement Scrolls in slot 3. Run twice: RNG `r = 0.00` (success) and `r = 0.90` (destruction).
*Action*: `ConfirmEnhancement(0, 3)`.
*Pass*: In both runs slot 3 holds 4 scrolls afterwards. Success run: slot 0 item at level 3. Destruction run: slot 0 empty.

**AC-ENH-34: Commit Failure After Success — Rolled Back**
*Setup*: Bronze item at +2 in slot 0; 3 Bronze Enhancement Scrolls in slot 1. RNG injected: `r = 0.00`. `SaveIrreversibleOutcome` injected to return `DatabaseError` at step 6b.
*Action*: `ConfirmEnhancement(0, 1)`.
*Pass*: Slot 0 holds the item at level 2, unlocked. Slot 1 holds 3 scrolls. No `EnhancementAttemptResult` and no `ServerBroadcast_Enhancement9` are sent. Server logs `CriticalEnhancementWriteFailed`. The client is disconnected (CR-CP-5).

**AC-ENH-35: Committed Record Contains the Outcome**
*Setup*: Bronze item at +2 in slot 0; 2 Bronze Enhancement Scrolls in slot 1. A persistence spy captures the inventory snapshot read by `SaveIrreversibleOutcome`. Run twice: RNG `r = 0.00` (success) and `r = 0.90` (destruction).
*Action*: `ConfirmEnhancement(0, 1)`.
*Pass*: `SaveIrreversibleOutcome` is called exactly once, with trigger `EnhancementResult`. Captured snapshot — success run: slot 0 at level 3, slot 1 quantity 1; destruction run: no entry for slot 0, slot 1 quantity 1. `EnhancementAttemptResult` is sent only after the call returns `Success`.

**AC-ENH-36: Scroll Gone at Step 4 — Attempt Aborted**
*Setup*: Valid Bronze item at +2 in slot 0 and a Bronze Enhancement Scroll in slot 1 (step 2 passes). The Inventory test double makes `ConsumeItem(scrollItemID, 1)` return failure. RNG spy and persistence spy attached.
*Action*: `ConfirmEnhancement(0, 1)`.
*Pass*: Returns `RejectedScrollNotFound`. Slot 0 is unlocked and the item is still at level 2. The RNG is never drawn. `SaveIrreversibleOutcome` is not called. `IsAttemptInProgress` is false, and a following valid `ConfirmEnhancement` is accepted.

**AC-ENH-37: Non-Upgradeable Item Rejected**
*Setup*: A test item record with `IsUpgradeable = false` in slot 0; a scroll of the matching tier in slot 1.
*Action*: `ConfirmEnhancement(0, 1)`.
*Pass*: Returns `RejectedNotUpgradeable`. Scroll remains. Slot 0 was never locked.

**AC-ENH-38: Inventory Requests Held During an In-Flight Commit**
*Setup*: Bronze item at +4 in slot 0; one Bronze Enhancement Scroll stack in slot 1 (quantity 2); slots 2–19 occupied; a Bronze Helmet equipped. RNG injected for destruction. The step 6b write is held open (injected Task delay), then made to return `DatabaseError`.
*Action*: `ConfirmEnhancement(0, 1)`. While the write is held open, the client sends an unequip request for the Helmet (which would take the freed slot 0).
*Pass*: While the write is in flight, `IsAttemptInProgress` is true and the Helmet is still equipped (the request is held, not processed). After the write fails, the Rollback restores the item at level 4 and the scroll stack to quantity 2 with no `CriticalEnhancementRollbackFailed` logged. The held unequip request is processed only after that and fails for lack of bag space.

**AC-ENH-39: NPC Session Wall-Clock Lifetime**
*Setup*: Player in the town hub; `OpenNPCInteraction(enhancementNpcId)` succeeds at time T. `SESSION_TTL_SECONDS` and the server clock are injected. The client keeps sending `CancelEnhancement` (never a new `OpenNPCInteraction`, which would restart the lifetime) while the clock advances past T + `SESSION_TTL_SECONDS`.
*Action*: `ConfirmEnhancement` with a valid item and scroll.
*Pass*: `NPCInteractionActive = false` (client activity did not extend the session). Returns `RejectedNoNPCSession`. No slot is locked, no scroll is consumed.

## Open Questions

**OQ-ENH-1: Scroll pricing for Bronze/Iron/Steel tiers**
Recommended prices (TK-ENH-9) are provisional. Requires playtest validation once farming rates are measured. Currency System farming-rate invariant (30–50 min same-tier) is the binding constraint.

**OQ-ENH-2: Enhancement UI entry point — RESOLVED**
Enhancement UI is accessed exclusively by interacting with the Enhancement NPC in the town hub. No inline inventory access. Travel to town is required. See CR-ENH-16.

**OQ-ENH-3: Lock timeout for LOCKED/RESOLVING state on server crash — RESOLVED (2026-10-01)**
No refund logic and no lock timeout are needed. Scroll consumption at step 4 is an in-memory change until the step 6b write succeeds; a server crash before that leaves the saved record in its pre-attempt state, so the player loads with scroll and item intact (CR-ENH-11, EC-ENH-2). Locks are in-memory only, never saved, and bounded by the attempt (CR-ENH-7).

**OQ-ENH-4: +9 broadcast for items obtained at +9 via trade**
CR-ENH-14 fires the broadcast at the moment of the successful +9 transition. If a +9 item is traded, no new broadcast fires. Is a transfer announcement needed? Deferred to social systems design.

**OQ-ENH-5: Enhancement level display in zone (third-person view)**
VR-ENH-4 specifies PrestigeBand glow is visible at range. Should the enhancement level number be visible in the name tag above the player in zone? Currently: number revealed only on inspect. Requires input from UX and art direction.

**OQ-ENH-6: Character Persistence GDD scope — RESOLVED**
character-persistence.md (Approved) persists `EnhancementLevel` for every bag slot (`InventoryEnhancementLevels`, added 2026-10-01) and every gear slot, and defines the `SaveIrreversibleOutcome(EnhancementResult)` commit used at CR-ENH-15 step 6b.

~~**OQ-ENH-7: Which layer holds requests during an attempt (CR-ENH-18)**~~ **RESOLVED (2026-10-07) by ADR-011** (`docs/architecture/ADR-011-async-persistence-tick-loop.md`, Status: Accepted 2026-10-07). The session request dispatcher holds a character's bag-mutating client requests while a per-character mutation gate is closed (closed for the length of any `SaveIrreversibleOutcome` write, so it also serves level-up, respec and item consumption) and releases them in arrival order when it opens. Server-originated bag mutations — the Loot Table System's pickup — read the same gate, are not attempted while it is closed, and are retried after it opens. Individual mutating systems do not check anything. *Original question:*
CR-ENH-18 requires that a character's other inventory-mutating requests are held while `IsAttemptInProgress(CharacterID)` is true. Two candidates: the session's request dispatcher checks the flag once for every inbound request (one enforcement point; needs a Networking Core rule), or each mutating system (Inventory, Equipment, NPC Shop, Consumable Use) checks it. The same question applies to any other caller-owned rollback behind `SaveIrreversibleOutcome` (level-up, respec, item consumption). The decision must also cover server-originated bag mutations that are not client requests — e.g. the Loot Table System's `PickupRequest`, which returns a synchronous `PickupResult` and so cannot be deferred by a request dispatcher.
*Owner*: Lead Programmer / Networking Core. *Target*: before `/create-epics` for the Enhancement System — pre-implementation gate.

**OQ-ENH-8: Replaying a missed result on next login**
character-persistence.md OQ-CP-2 assigns this here: if the client closes between the step 6b commit and delivery of `EnhancementAttemptResult`, the player logs back in with the outcome applied but never saw the result screen. Should the server record an unacknowledged result and replay it at login? Current spec: no replay (EC-ENH-1). Deferred to Enhancement UI design.

**OQ-ENH-9: Post-MVP scroll drops** *(opened 2026-10-07)*
At MVP, Enhancement Scrolls are sold only by the NPC Shop (AC-ENH-24; enforced by loot-table-system.md CR-LT-16 while `ALLOW_ENHANCEMENT_SCROLL_DROPS` is `false`). The intent after MVP is for scrolls to be a rare drop on some monsters. Before the switch is set to `true`, four things must be decided: (1) the F-ENH-5 economy validation redone with a drop source — the cost-to-+9 figures and the "primary gold sink" claim assume every scroll is bought; (2) the TK-ENH-9 prices re-checked against the drop rates chosen; (3) a drop classification for scrolls in loot-table-system.md — under CR-LT-5 a scroll has `GearTier.None` and is a Common round-robin drop, so a "rare" scroll needs its own rule (rate target, and whether it is auctioned); (4) AC-ENH-24 retired or rewritten, and the "NPC Shop only" notes in npc-shop.md and the entity registry updated.
*Owner*: Economy Designer / Game Designer. *Target*: post-MVP — not a pre-implementation gate.
