# HUD (Heads-Up Display)

> **Status**: Approved (2026-06-20, Revision 2 — lean re-review Pass 2 cleared)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-06-20
> **Implements Pillar**: All four pillars (Earned Power, Rhythm Mastery, Social Gravity, Legendary Gear — HUD surfaces all)

## Overview

The HUD is Project Iron Grind's always-on information layer — the display a player reads between decisions. It simultaneously surfaces five streams: the player's resource bars (HP, MP, XP — subscribed via `OnStatChanged`); the auto-attack charge bar and toggle (hosted here, fully specced in Auto-Attack Combat); up to four party member health frames (driven by Party System membership and `MemberStatus` state); a target frame (enemy name, HP bar, level — hidden when no target is selected); and the persistent party chat panel (hosted here, fully specced in Party Chat). Secondary layers — a minimap, buff/debuff tray (max 6 icons), and timed loot-assignment notifications — are displayed alongside or collapsed until triggered. All elements observe safe area insets and thumb-reach constraints for landscape iOS. The skill button bar is Combat UI (#28) — out of scope for this GDD.

## Player Fantasy

A hundred hours into Project Iron Grind, you stop reading the HUD and start feeling it. HP at a glance, charge bar in peripheral vision, party frames at the edge of your eye — not data to parse, but a pulse you sense without looking. That is the fantasy: the earned competence of a veteran who no longer has to think about the instrument panel.

The HUD's emphasis order signals what this game values. **Earned Power first** — the HP, MP, and XP bars are your character's condition and trajectory made permanently visible: how much you have left, how close you are to the next threshold. **Rhythm Mastery second** — the charge bar is the held breath before the strike, the metronome that organizes every combat decision; you feel it filling before you consciously register the percentage. **Social Gravity third** — the four party frames are accountability made visible; you cannot pretend you didn't see a teammate's health sliding toward red. The moment you react before the word "help" appears in chat is the moment party play earns its weight. Legendary Gear lives in the character model, not the HUD — but the numbers the HUD displays are the reward for the gear you grind to earn.

New players see a cockpit they don't yet know how to fly. Veterans see a single living readout that has become part of how they think. The HUD is what you grow into — and the moment you stop looking at it is the moment you've arrived.

## Detailed Design

### Core Rules

**CR-HUD-1: Always-On Visibility**
The HUD is permanently visible during gameplay. It cannot be toggled off by the player. All elements listed below are visible at all times unless their rule specifies a hide condition.

**CR-HUD-2: Stat Subscription Model**
The HUD subscribes to `OnStatChanged(EntityID, StatID)` for the local player's EntityID. It MUST NOT poll `GetEffectiveStat` on a per-frame basis. Inside the handler, the HUD MAY call `GetEffectiveStat(StatID)` to read the current value; it MUST NOT call any write operations (Character Stats Rule 8).

**CR-HUD-3: HP Display**
HP bar fill = `Clamp01(CurrentHP / MaxHP)` when `MaxHP > 0`; fill = 0.0 if `MaxHP ≤ 0` (entity initialization guard — see F-HUD-1). HP numeric display = `Mathf.FloorToInt(CurrentHP)`. Both update on `OnStatChanged` where `StatID == StatID.CurrentHP || StatID == StatID.MaxHP`.

**CR-HUD-4: HP Critical State**
When `CurrentHP / MaxHP ≤ 0.30`:
- Skull icon appears at the left endcap of the HP bar (Art Bible § 4.5 colorblind backup)
- 1px pulsing border activates around the HP bar (Danger Alert `#E84040`) at ≤ 2 Hz (WCAG 2.3.1 — max 3 flashes/sec; 2 Hz provides safety margin)
Both indicators clear when HP rises above 30%.

**CR-HUD-5: MP Display**
MP bar fill = `CurrentMP / MaxMP` when `MaxMP > 0`; fill = 0.0 if `MaxMP ≤ 0` (init guard — matches F-HUD-1). Updated on `OnStatChanged` where `StatID == StatID.CurrentMP || StatID == StatID.MaxMP`. No numeric display at MVP.

**CR-HUD-6: XP Bar**
XP bar fill = `CurrentXP / XPToNextLevel`. At Level 60: bar fill locked at 1.0 (static, no animation); level badge shows "MAX". No further XP or Level `OnStatChanged` events are processed after Level 60 is set.

**CR-HUD-7: Gold Display**
Gold updates via `GoldSyncEvent`, not `OnStatChanged`. On receiving `GoldSyncEvent{CharacterID, NewBalance, Version, Reason}`:
- Discard if `CharacterID ≠ local player's CharacterID` (party member gold events must not update local display)
- Discard if `Version ≤ cachedGoldVersion` (stale out-of-order delivery)
- Otherwise display `NewBalance`; update `cachedGoldVersion = Version`
- Gold field must accommodate up to 7 digits (GOLD_CAP = 9,999,999)

**CR-HUD-8: Solo Mode vs. Party Mode**
On zone join, read `IsSoloParty`. Re-evaluate on every `PartyStateChanged` event.
- `IsSoloParty = true` (Solo Mode): party frames hidden; party chat panel hidden
- `IsSoloParty = false` (Party Mode): party frames visible; party chat panel visible

**CR-HUD-9: Party Member Frames**
In Party Mode, up to 4 frames are displayed: pill-shaped, stacked vertically, top-right below the minimap. Each frame shows:
- HP bar fill only — no numeric value
- Player name label
- Role badge (`IClassRegistry.GetClass(ClassType).ArchetypeRole`)
- Status-driven appearance (CR-HUD-10)
Minimum HP bar width per frame: 80dp (ensures partial HP is visually distinguishable from full).

**CR-HUD-10: Party Member Status Visual Mapping**

| MemberStatus | HP Bar Fill | Additional Indicators | Name Text |
|---|---|---|---|
| Online | `CurrentHP / MaxHP` | None | Normal |
| Dead | 0% | Skull icon overlay on the pill frame | 50% opacity |
| Ghost | Frozen at value when Ghost transition fired | Dotted 1px border | 50% opacity |
| OutOfZone | 0% | "OUT" badge on the pill frame | 50% opacity |

Ghost state is driven exclusively by `PartyMemberStatusChanged(MemberID, Ghost)`. The HUD MUST NOT implement an independent timeout or inference for Ghost state — doing so conflicts with the Party System's CR-PS-11 5-second suppress window and causes flicker on mobile reconnects.

**CR-HUD-11: Target Frame**
Top-center area. Hidden when no target is selected. When visible:
- Shows: enemy name (text), HP bar (fill, Danger Alert `#E84040` fill color), level badge
- HP updates via the networking relevance layer (not `OnStatChanged`)
- Display shows last known HP if network updates stop before entity despawns
Cleared when:
1. Targeted entity despawns (server-authoritative via despawn event)
2. Player taps anywhere on the target frame (entire frame is the dismiss target; minimum 44×44dp)

**CR-HUD-12: Minimap**
Top-right area, above party frames. Anchor and bounds defined here; all content and zoom rules specified in the Map/Minimap GDD (#35).

**CR-HUD-13: Auto-Attack Charge Bar**
Hosted in the HUD frame; fully specced in the Auto-Attack Combat GDD. HUD responsibilities:
- Anchor: directly below the MP bar (within the player resource cluster)
- Dimensions: 8px height, same width as the MP bar
- `raycast target = false` — the charge bar is read-only; no tap interaction
- In Player Dead state: hidden (not frozen at current fill value)

**CR-HUD-14: Buff/Debuff Tray**
Hidden when no effects are active. Positioned in the top-center area, below the target frame (or occupying the target frame slot when no target is selected); exact position specified in `design/ux/hud.md`.

When active:
- Icons: 32×32px each; up to 6 shown per page
- Ordering: newest-first. Slot 0 = most recently applied
- On refresh (CR-SE-4, same `BuffID` re-applied): icon holds its original slot; duration resets internally without visual movement
- When `activeBuffCount > 6`: scroll arrows appear (minimum 44×44dp each); page 2 shows remaining effects (at most 2, since MAX_ACTIVE_BUFFS_PER_ENTITY = 8)
- When page 2 becomes empty: tray snaps to page 1 automatically

**CR-HUD-15: Loot Assignment Notifications**
When `GroundItemAssigned(ItemID, EntityID, expiryTick)` is received for the local player:
- Show notification: item name, item icon, radial pie countdown
- Countdown fill = `remainingTicks / GROUND_ITEM_TTL_TICKS`; `remainingTicks` computed client-side from `expiryTick - currentTick`
- At `GroundItemExpiryWarning` (600 ticks before expiry): pie fill color changes to Danger Alert `#E84040`
- Authoritative dismiss: `GroundItemDespawned(ItemID)` event (covers both pickup and natural expiry)
- Multiple simultaneous assignments: queue — next notification shown only after current is dismissed
- Notification element: `raycast target = false` (input-transparent to elements beneath)

**CR-HUD-16: Party Chat Panel**
Hosted in the HUD frame; fully specced in the Party Chat GDD. HUD responsibilities:
- `CHAT_HISTORY_DISPLAY_COUNT` = 12 (resolves Party Chat OQ-PC-4)
- Anchor: bottom-center — between the virtual joystick area (left) and the Combat UI skill buttons (right)
- The chat panel bounding rect MUST NOT extend into the joystick activation zone (left half of the safe-area-inset screen width, per Movement System CR-MOV-1). The virtual joystick is floating — it spawns at the touch-start position with no fixed anchor — so the exclusion applies to the entire left-half zone, not a point.
- Hidden when `IsSoloParty = true`
- **Note**: Party Chat GDD UI Requirements specifies "bottom-left" for panel position. This GDD supersedes that placeholder with "bottom-center." A one-line amendment to Party Chat GDD UI Requirements is required before implementation.

**CR-HUD-17: Safe Area Compliance**
All elements are anchored inside `Screen.safeArea` plus 8px interior margin. Safe area values are read at runtime via `Screen.safeArea` in `Awake` or `OnRectTransformDimensionsChange`. Pixel offsets are never hardcoded.

**CR-HUD-18: Non-Interactive Elements**
The following elements are display-only with `raycast target = false`:
- Auto-attack charge bar (CR-HUD-13)
- HP bar, MP bar, XP bar
- Party member HP bars (within each party frame)
- Target HP bar
- Loot notification background (CR-HUD-15)

**CR-HUD-19: Player Dead State**
When local player `ZoneSessionState = Dead`:
- Charge bar: hidden (CR-HUD-13)
- HP bar: shows 0 (display only, no interaction change)
- Target frame: cleared (CR-HUD-11)
- Party frames: remain visible
- Respawn countdown UI: owned by the Death & Respawn GDD — out of scope here

**CR-HUD-20: Auto-Run Toggle**
Hosted in the HUD frame; fully specced in the Movement System GDD. HUD responsibilities:
- Anchor: bottom-left zone, adjacent to the virtual joystick area — within left-thumb reach zone (Zone F per `design/ux/hud.md`). Position is fixed at 8dp from left safe area edge, 72dp from bottom safe area edge. This placement was confirmed by the UX spec (`design/ux/hud.md`) as the correct position for a movement toggle; it overrides the prior right-side placement in an earlier draft of this rule. Exact position defined in `design/ux/hud.md`.
- Minimum touch target: 44×44dp (visual: 44×28dp pill; tap target extends above/below)
- Joystick exclusion: virtual joystick must not spawn with its center within 44dp of the auto-run toggle center
- Visual state (enabled/disabled) driven by Movement System events
- Always visible (not hidden-until-needed)

**CR-HUD-21: Peripheral Design Constraints**
The Player Fantasy of "sensing without looking" is supported by three concrete constraints that translate intent into implementable rules:
1. HP Critical state uses **motion** (pulsing border at 2 Hz, CR-HUD-4) in addition to color — motion is detectable in peripheral vision where color alone may not register on mobile displays held at arm's length.
2. Party member HP bars have a minimum 80dp width (TK-HUD-3) — a 10% fill-change segment at this width is visually distinguishable at arm's length on a 6-inch phone in landscape.
3. All bar fills use distinct Art Bible colors (HP: Vital Amber `#E8A020`; MP: Skill Blue `#2E6EBF`; target: Danger Alert `#E84040`) — no two adjacent bars share the same fill color, enabling bar identification without directing gaze.

**CR-HUD-22: Top-Right Column Height Budget**
The minimap (CR-HUD-12) and party frame stack (CR-HUD-9) share the top-right column. Their combined rendered height MUST fit within `Screen.safeArea.height - 16dp` (safe area height minus 2× interior margin). The UX spec (`design/ux/hud.md`) must include an explicit height proof for the minimum supported device (iPhone SE 3rd gen in landscape: safe area height ≈ 320dp). The auto-run toggle is no longer part of this column (relocated to bottom-right per CR-HUD-20). Party frames MUST NOT overflow or overlap the bottom safe area boundary. Equivalently: the bottom edge of the lowest party frame must not exceed `safeArea.y + safeArea.height - 8dp` (bottom safe area boundary minus the 8dp interior margin).

---

### States and Transitions

| State | Entry Condition | Exit Condition | Visual Effect |
|---|---|---|---|
| **Solo Mode** | `IsSoloParty = true` | `IsSoloParty = false` | Party frames and chat panel hidden |
| **Party Mode** | `IsSoloParty = false` | `IsSoloParty = true` | Party frames and chat panel visible |
| **HP Critical** | `CurrentHP / MaxHP ≤ 0.30` | HP rises above 30% | Skull icon + pulsing Danger Alert border on HP bar |
| **Player Dead** | `ZoneSessionState = Dead` | State exits Dead | Charge bar hidden; target frame cleared |
| **Level Cap** | `Level = 60` | N/A | XP bar full and static; badge = "MAX" |
| **Target: None** | No entity targeted (or manual/death dismiss) | Player taps a targetable entity | Target frame hidden |
| **Target: Selected** | Player taps a targetable entity | Entity despawns OR player taps frame | Target frame shown with enemy name / HP / level |
| **Buff Tray: Inactive** | `activeBuffCount = 0` | `activeBuffCount ≥ 1` | Tray hidden |
| **Buff Tray: Single Page** | `activeBuffCount ∈ [1, 6]` | Count drops to 0 or rises above 6 | 6-slot grid; no scroll arrows |
| **Buff Tray: Paginated** | `activeBuffCount > 6` | Count ≤ 6 | Scroll arrows visible; page 2 auto-collapses when empty |
| **Loot: None** | No assignment pending | `GroundItemAssigned` received | Notification hidden |
| **Loot: Active** | `GroundItemAssigned` received | `GroundItemDespawned` or pickup | Notification + countdown pie visible |
| **Loot: Warning** | `GroundItemExpiryWarning` received | Notification dismissed | Pie fill color = Danger Alert `#E84040` |

---

### Interactions with Other Systems

| System | Direction | Data | HUD Rule |
|---|---|---|---|
| Character Stats | Upstream | `OnStatChanged(EntityID, StatID)` → HP, MP, Level, XP | CR-HUD-2 through CR-HUD-6 |
| Currency System | Upstream | `GoldSyncEvent{NewBalance, Version}` → gold display | CR-HUD-7 |
| Party System | Upstream | `IsSoloParty`, `PartyMemberStatusChanged`, `IClassRegistry` | CR-HUD-8 through CR-HUD-10 |
| Status Effects | Upstream | `OnBuffApplied(EntityID, BuffID, EntityID caster, float remainingTicks)` and `OnBuffExpired(EntityID, BuffID)` events → tray updates (event interface TBD — see OQ-HUD-8) | CR-HUD-14 |
| Zone Instancing | Upstream | `GroundItemAssigned`, `GroundItemDespawned`, `GroundItemExpiryWarning` | CR-HUD-15 |
| Auto-Attack Combat | Bidirectional | Hosts charge bar widget; provides anchor + `raycast target = false` | CR-HUD-13 |
| Party Chat | Bidirectional | Hosts chat panel; provides anchor, `CHAT_HISTORY_DISPLAY_COUNT = 12`, hide rule | CR-HUD-16 |
| Networking (target HP) | Upstream | EntityHealthUpdate for targeted entity (via relevance filter) | CR-HUD-11 |
| Combat UI (#28) | Downstream | Skill buttons occupy the bottom-right zone adjacent to HUD; HUD does not specify skill content | — |
| Map/Minimap (#35) | Downstream | Minimap content and zoom rules; HUD provides only the top-right anchor position | CR-HUD-12 |

## Formulas

**F-HUD-1: HP Bar Fill**
```
if MaxHP ≤ 0: fillHP = 0.0                    // guard: entity not yet initialized
else: fillHP = Clamp01(CurrentHP / MaxHP)
```
| Variable | Type | Range |
|---|---|---|
| `CurrentHP` | float | [0.0, MaxHP] |
| `MaxHP` | float | ≥ 0 (> 0 in steady state; 0 only during entity initialization) |
| Output `fillHP` | float | [0.0, 1.0] |

Example: CurrentHP=180.0, MaxHP=200.0 → fillHP=0.90 (bar at 90%)
Boundary check: CurrentHP=0 → fillHP=0.0. CurrentHP=MaxHP → fillHP=1.0. MaxHP=0 (init guard) → fillHP=0.0 (avoids NaN from division).

---

**F-HUD-2: XP Bar Fill**
```
fillXP = (Level == LEVEL_CAP) ? 1.0 : Clamp01(CurrentXP / XPToNextLevel)
```
| Variable | Type | Range |
|---|---|---|
| `CurrentXP` | float | [0.0, XPToNextLevel) |
| `XPToNextLevel` | float | > 0 (from Leveling System) |
| `LEVEL_CAP` | int constant | 60 |
| Output `fillXP` | float | [0.0, 1.0] |

Example: CurrentXP=8000, XPToNextLevel=10000 → fillXP=0.80
Boundary check: Level=60 → fillXP=1.0 (static full bar, short-circuits division).

---

**F-HUD-3: Loot Countdown Pie Fill**
```
remainingTicks = expiryTick - currentTick
fillLoot = Clamp01((float)remainingTicks / GROUND_ITEM_TTL_TICKS)
```
| Variable | Type | Range |
|---|---|---|
| `expiryTick` | int | > 0 (from `GroundItemAssigned`) |
| `currentTick` | int | client-side tick counter |
| `GROUND_ITEM_TTL_TICKS` | int constant | 2400 (120s at TICK_RATE_HZ=20) |
| Output `fillLoot` | float | [0.0, 1.0] |

Example: item assigned 600 ticks ago → remainingTicks=1800 → fillLoot=0.75 (75% of time remaining)
Boundary check: remainingTicks=0 → fillLoot=0.0; `GroundItemDespawned` is the authoritative dismiss signal before 0.0 in normal play. If client tick diverges from server, `fillLoot` may briefly exceed 1.0 — `Clamp01` prevents visual overflow. The explicit `(float)` cast is required — C# integer division would truncate `1800 / 2400` to 0 for any `remainingTicks < 2400`, producing a permanently empty pie until the final tick.

---

**F-HUD-4: HP Critical Threshold Check**
```
isCritical = (MaxHP > 0) && ((CurrentHP / MaxHP) ≤ HP_CRITICAL_THRESHOLD)
```
| Variable | Type | Value |
|---|---|---|
| `HP_CRITICAL_THRESHOLD` | float constant | 0.30 (Art Bible § 4.5) |
| Output `isCritical` | bool | true / false |

Guard: `MaxHP ≤ 0` short-circuits to `isCritical = false` — an uninitialized entity is never in critical state. Matches the init guard in F-HUD-1.

Not a tuning knob in this GDD — the threshold is defined in the Art Bible and must match across all systems that display HP state.

---

**F-HUD-5: Party Frame HP Fill**
```
fillPartyHP = (status == Dead || status == OutOfZone) ? 0.0
            : (status == Ghost)             ? frozenFill
            : (PartyMemberMaxHP ≤ 0)        ? 0.0         // init guard — matches F-HUD-1
            : Clamp01(PartyMemberCurrentHP / PartyMemberMaxHP)
```
| Variable | Type | Range |
|---|---|---|
| `PartyMemberCurrentHP` | float | [0.0, PartyMemberMaxHP] |
| `PartyMemberMaxHP` | float | ≥ 0 (> 0 in steady state; 0 only during member initialization) |
| `frozenFill` | float | fill value at time `PartyMemberStatusChanged(MemberID, Ghost)` fired |
| Output `fillPartyHP` | float | [0.0, 1.0] |

Ghost fill is captured when the Ghost transition event fires and held until the next non-Ghost status event clears it. `PartyMemberMaxHP ≤ 0` guard prevents NaN on party member initialization — consistent with F-HUD-1.

## Edge Cases

**EC-HUD-1: Race Between HP=0 and MemberStatus=Dead**
A party member's HP update may reach the HUD before their MemberStatus=Dead event from the Party System. Until `PartyMemberStatusChanged(MemberID, Dead)` is received, the frame shows a 0% HP bar with MemberStatus=Online styling (no skull overlay, no dimming). Dead status is never inferred from HP alone — the Party System is authoritative.

**EC-HUD-2: Level 59 → 60 Transition**
At Level 60, the HUD ignores further XP and Level `OnStatChanged` events. If XP events arrive after Level=60 is set (delayed network delivery), they are discarded. The Level Cap state is entered on the first `OnStatChanged` where Level=60, regardless of which StatID triggered the handler.

**EC-HUD-3: currentTick > expiryTick (Tick Drift)**
If the client's tick counter drifts past the item's `expiryTick`, `remainingTicks` is negative and `fillLoot` clamps to 0.0. The notification remains visible at 0% pie fill until `GroundItemDespawned` arrives — the server is the authoritative dismiss signal; the client does not self-dismiss.

**EC-HUD-4: Ghost Member with 0% HP at Transition**
If a party member's HP was 0% when the Ghost transition fired, `frozenFill = 0.0`. The frame shows 0% HP with a dotted border — visually similar to Dead, but distinct: Ghost uses a dotted border and 50% name opacity, not a skull overlay. Both are valid states; a Ghost can reconnect and return to combat.

**EC-HUD-5: Page 2 of Buff Tray Becomes Empty**
When `activeBuffCount = 7` and the 7th effect expires, `activeBuffCount` drops to 6 — scroll arrows disappear and the tray snaps to page 1. No visual flash between single-icon page 2 and the snap.

**EC-HUD-6: Solo→Party Transition Mid-Fight**
When `IsSoloParty` changes from true to false, party frames and chat panel appear simultaneously. This layout shift MUST NOT displace active loot notifications or the joystick area. Party frames and chat panel animate in from their anchored positions.

**EC-HUD-7: Gold cachedGoldVersion at Session Start**
Initialize `cachedGoldVersion = 0` at session start. Character Persistence sends a `GoldSyncEvent` on load with Version ≥ 1 — always accepted. Gold display shows 0 until this event arrives; it is never blank or missing.

**EC-HUD-8: Active Loot Notification Picked Up**
When the active notification's item is picked up, `GroundItemDespawned` fires immediately. The notification dismisses and the next queued notification (if any) appears without delay.

**EC-HUD-9: Target Frame Dismissed While Enemy Still Alive**
If the player manually taps to dismiss the target frame while the enemy is alive, the frame hides and the entity has not despawned. Which entity (if any) becomes the new target is owned by the targeting input logic — out of scope for this GDD.

**EC-HUD-10: Safe Area Change Mid-Session**
The game is landscape-only. Safe area changes mid-session are not expected. However, `Screen.safeArea` is re-read on `OnRectTransformDimensionsChange` (CR-HUD-17) — if the device reports a change, all element anchors recalculate without a scene reload.

## Dependencies

### Upstream Dependencies (HUD depends on these)

| System | What the HUD Consumes | HUD Rule |
|---|---|---|
| **Character Stats** (Approved) | `OnStatChanged(EntityID, StatID)` for HP, MP, Level, XP; `GetEffectiveStat` inside handlers | CR-HUD-2 through CR-HUD-6 |
| **Currency System** (Approved) | `GoldSyncEvent{NewBalance, Version}` for gold display | CR-HUD-7 |
| **Leveling System** (Approved) | `XPToNextLevel` value consumed inside `OnStatChanged(Level)` handler | F-HUD-2 |
| **Class System** (Approved) | `IClassRegistry.GetClass(ClassType).ArchetypeRole` for party frame role badge | CR-HUD-9 |
| **Party System** (Approved) | `IsSoloParty`, `PartyMemberStatusChanged(MemberID, MemberStatus)` | CR-HUD-8 through CR-HUD-10 |
| **Status Effects** (Approved) | `OnBuffApplied(EntityID, BuffID, EntityID caster, float remainingTicks)` and `OnBuffExpired(EntityID, BuffID)` events for tray management — event interface TBD (see OQ-HUD-8; Status Effects GDD notes buff broadcast messages not yet defined) | CR-HUD-14 |
| **Auto-Attack Combat** (Approved) | Charge bar visual spec (HUD provides anchor + `raycast target = false`) | CR-HUD-13 |
| **Movement System** (Approved) | Auto-run toggle spec (HUD provides anchor + 44×44dp touch target) | CR-HUD-20 |
| **Party Chat** (Approved) | Chat panel spec (HUD provides anchor, `CHAT_HISTORY_DISPLAY_COUNT = 12`, hide rule) | CR-HUD-16 |
| **Zone Instancing** (Approved) | `GroundItemAssigned`, `GroundItemDespawned`, `GroundItemExpiryWarning`; `GROUND_ITEM_TTL_TICKS` | CR-HUD-15, F-HUD-3 |
| **Networking Core** (Approved) | EntityHealthUpdate via relevance filter for targeted entity HP | CR-HUD-11 |
| **Art Bible** (`design/art/art-bible.md`) | HUD layout (§ 7.1), colorblind rules (§ 4.5), UI palette (§ 4.4), shape grammar (§ 3.3) | Referenced throughout |

### Downstream Dependents (these depend on HUD)

| System | What It Needs From HUD | Notes |
|---|---|---|
| **Party Chat** (Approved) | Panel anchor position (bottom-center); `CHAT_HISTORY_DISPLAY_COUNT = 12` | Party Chat GDD UI Requirements must be amended: "bottom-left" → "bottom-center" |
| **Map/Minimap** (#35, Not Started) | Top-right anchor position for minimap widget | HUD GDD is the positional authority |
| **Combat UI** (#28, Not Started) | Bottom-right zone boundary (adjacent to chat panel and joystick zone); target-nearest button spec | Combat UI owns skill bar content and target-nearest affordance; HUD owns the adjacent layout |

### Bidirectionality Note
Auto-Attack Combat, Movement System, and Party Chat appear in both tables because the HUD *hosts* their UI widgets while those GDDs *specify* widget behavior. All three GDDs should reference the HUD GDD in their downstream dependent tables. Verify before the design review.

## Tuning Knobs

**TK-HUD-1: CHAT_HISTORY_DISPLAY_COUNT**
- **Value**: 12
- **Unit**: visible chat message lines in the party chat panel
- **Safe range**: [8, 20]
- **Effect**: Lower values lose conversational context mid-pull but reduce panel visual footprint. Higher values preserve context for long sessions but extend the panel height into the combat zone.
- **Note**: This constant resolves Party Chat GDD OQ-PC-4. Changing this value requires re-layout of the chat panel and re-verification that the panel does not encroach on the joystick or skill zones.

**TK-HUD-2: BUFF_TRAY_PAGE_SIZE**
- **Value**: 6
- **Unit**: icons visible per tray page
- **Safe range**: [4, 8]
- **Effect**: At 8, the tray never paginates (MAX_ACTIVE_BUFFS_PER_ENTITY = 8 → all fit on one page). At 4, pagination starts earlier and icons are larger. At the default of 6, page 2 holds at most 2 overflow icons.
- **Note**: Changing this value does not require a registry update; the hard cap remains `MAX_ACTIVE_BUFFS_PER_ENTITY`.

**TK-HUD-3: PARTY_FRAME_HP_BAR_MIN_WIDTH_DP**
- **Value**: 80dp
- **Unit**: density-independent pixels
- **Safe range**: [60dp, 120dp]
- **Effect**: Controls the minimum width of each party member's HP bar in their pill frame. Below 60dp, a 10% HP difference is visually indistinguishable at arm's length on mobile. Above 120dp, party frames become too wide to stack 4 in the top-right zone.

## Visual/Audio Requirements

### Visual Requirements

**Color Specifications**

| Element | Color | Value |
|---|---|---|
| HP bar fill | Vital Amber | `#E8A020` |
| HP bar fill (critical state) | Unchanged fill; border changes | Pulsing 1px Danger Alert `#E84040` border |
| MP bar fill | Skill Blue | `#2E6EBF` |
| XP bar fill | TBD — deferred to `design/ux/hud.md` | — |
| Gold display text | Primary Text | `#E8E6DF` |
| Target frame HP bar fill | Danger Alert | `#E84040` (enemy HP always shown in threat color) |
| Loot countdown pie (normal) | Vital Amber | `#E8A020` |
| Loot countdown pie (warning) | Danger Alert | `#E84040` (on `GroundItemExpiryWarning`) |
| Panel backgrounds | Panel Dark | `#1A1C1F` |
| Panel borders | Panel Border | `#3A3D44` |
| Primary text (names, badges) | Primary Text | `#E8E6DF` |
| Dimmed text (Dead/Ghost/OOZ party members) | — | 50% opacity applied to Primary Text |

**Colorblind Safety (Art Bible § 4.5)**
- Heart icon: permanent left-endcap icon on the HP bar (positional backup — HP always above MP)
- Gem icon: permanent left-endcap icon on the MP bar
- Skull icon: HP bar left-endcap, visible when `isCritical = true`
- All three icons render at full opacity at all times regardless of bar state

**Typography**
- All HUD text: grotesque sans-serif, 2 weights only (regular and bold) per Art Bible
- Player name, level badge, gold display: minimum 13sp
- Damage numbers: 18sp normal / 26sp critical (specced in Auto-Attack Combat GDD)

**Shapes (Art Bible § 3.3)**
- Player resource cluster (HP/MP/XP + name): chamfered rectangle panel
- Party member frames: pill shape (the only soft shape in the UI vocabulary)
- Target frame: chamfered rectangle
- Buff/debuff tray: chamfered rectangle panel
- Loot notification: chamfered rectangle

**Sizing**
- Charge bar: 8px height × MP bar width (specced in Auto-Attack Combat GDD; listed here for layout reference)
- Buff/debuff icons: 32×32px each
- Buff tray scroll arrows: minimum 44×44dp touch target
- Party frame HP bar: minimum 80dp width (TK-HUD-3)
- Auto-run toggle: minimum 44×44dp
- Target frame dismiss region: minimum 44×44dp (entire frame acts as the dismiss target)

---

### Audio Requirements

The HUD fires the following audio events in response to state transitions. Sound design and content are owned by the Audio System GDD.

| Trigger | Audio Event | Notes |
|---|---|---|
| HP Critical state entry | `HUD_HP_CriticalWarning` | Fires once when HP drops to ≤ 30%; does not repeat on additional damage within critical state |
| Loot notification becomes active | `HUD_LootAssigned` | Fires when a queued notification moves to active display (not on queue) |

All other audio associated with HUD-visible events (level-up fanfare, gold sound, buff apply sounds) is fired by the source system — not the HUD.

## UI Requirements

**UI Framework**
Recommended: Unity 6.3 UI Toolkit (UXML/USS). UGUI (Canvas/`UnityEngine.UI`) is also fully supported — the choice must be resolved by ADR before implementation begins. The charge bar is specced in Auto-Attack Combat GDD using UGUI API (`Image.FillMethod.Horizontal`, `fillAmount`); if UI Toolkit is chosen, the charge bar implementation must substitute `VisualElement` fill logic for the UGUI `Image.fillAmount` API.

**Touch Targets**
All interactive elements must have a minimum 44×44dp tap region:
- Buff tray scroll arrows
- Auto-run toggle (CR-HUD-20)
- Auto-attack toggle (specced in Auto-Attack Combat GDD)
- Target frame dismiss region (entire frame, CR-HUD-11)
- Party chat compose button (specced in Party Chat GDD)

**Non-Interactive Elements**
All display-only elements must have `raycast target = false` (CR-HUD-18). Enforced at the component level — no tap handlers registered.

**Unity 6.x API Constraints**
- `VisualElement.transform` deprecated (Unity 6.2) — use `element.style.translate` for all positional animation
- USS files with invalid syntax now block import (Unity 6.3 — previously a warning, now an error); all USS must be validated before committing
- `AccessibilityRole` enum changed from flags to standard enum with `byte` underlying type (Unity 6.3); do not use bitwise combination of role values

**Safe Area**
Safe area insets are applied at runtime via `Screen.safeArea` in `Awake` and recalculated in `OnRectTransformDimensionsChange`. No pixel offsets are hardcoded (CR-HUD-17). Handles notch and Dynamic Island on iPhone X+ in landscape.

**Thumb-Reach Compliance**
Per Art Bible § 7.1:
- Left 45% / bottom 60% = left thumb zone
- Right 45% / bottom 60% = right thumb zone
- Top half = low-frequency taps only (no active combat actions)
- Interactive elements in the top half: target frame dismiss (non-combat), buff tray scroll (non-combat), minimap tap to open (non-combat)

**Canvas Layer Architecture (UGUI)**
To prevent full-HUD redraws on every HP tick, the UGUI Canvas hierarchy uses three nested Canvases with dirty-isolation:

| Canvas Layer | Elements | Rebuild Trigger |
|---|---|---|
| `HUD_Static` (root Canvas) | Panel backgrounds, bar frames, icon slots, party frame shells, colorblind icons | Zone entry / party join-leave |
| `HUD_Dynamic` (child Canvas, `PixelPerfect = false`) | HP/MP/XP fill images, charge bar fill, party frame HP fills, gold text, buff icons, target HP fill | Per `OnStatChanged` / `GoldSyncEvent` / buff event |
| `HUD_Overlay` (child Canvas, higher `sortOrder`) | Loot notification stack, chat panel | Item assignment / despawn / chat message |

Rationale: separating `HUD_Static` from `HUD_Dynamic` means a single HP tick update redraws only the 5–7 fill images in `HUD_Dynamic`, not the 40+ background shapes and static icons in `HUD_Static`. On mobile GPU, a full-HUD dirty costs ~2ms/frame at 60fps — the three-layer split targets < 0.3ms per tick update.

If UI Toolkit is chosen (pending OQ-HUD-1 ADR): equivalent isolation is achieved via separate `VisualElement` subtrees with `usageHints = UsageHints.DynamicColor` applied to fill elements. The Canvas layer spec above is the reference architecture; the UI Toolkit equivalent must match the same dirty-isolation boundaries between static, dynamic, and overlay content.

**UX Spec Dependency**
Exact pixel dimensions, animation curves, transition durations, and element anchor positions are specified in `design/ux/hud.md`. This GDD defines behavioral rules and minimum constraints; the UX spec defines exact visual implementation.

## Acceptance Criteria

**AC-HUD-1: No Per-Frame Polling**
Instrument `OnStatChanged` call count and `GetEffectiveStat` call count per frame. When HP changes once, exactly one `OnStatChanged` callback fires and `GetEffectiveStat` is called inside it — zero calls are made from `Update()` or `FixedUpdate()`.

**AC-HUD-2: HP Bar Fill Formula**
Given CurrentHP=60.0, MaxHP=200.0 → HP bar fill reads 0.30 (± 0.001). Given CurrentHP=0.0 → fill=0.0. Given CurrentHP=MaxHP → fill=1.0.

**AC-HUD-3: HP Critical State Activation**
Given CurrentHP=59.9, MaxHP=200.0 (29.95% HP): skull icon is visible at the HP bar endcap AND the HP bar has a pulsing 1px border. Given CurrentHP=60.1 (30.05%): skull icon is NOT visible; border is NOT pulsing.

**AC-HUD-4: MP Bar Fill Formula**
Given CurrentMP=100.0, MaxMP=300.0 → MP bar fill reads 0.333 (± 0.001). Given CurrentMP=0.0 → fill=0.0.

**AC-HUD-5: XP Bar at Level Cap**
Given Level=60: XP bar fill is 1.0 and the bar displays no animation. Level badge text reads "MAX". A subsequent `OnStatChanged` event (any StatID) while Level=60 does not change the XP bar display.

**AC-HUD-6: Gold Stale Detection**
Send `GoldSyncEvent{NewBalance=500, Version=3}` followed by `GoldSyncEvent{NewBalance=200, Version=2}`. Gold display shows 500 (higher-Version event). The Version=2 event is discarded without updating the display.

**AC-HUD-7: Gold 7-Digit Capacity**
Set gold balance to 9,999,999 (GOLD_CAP). The gold display field renders the full 7-digit number without truncation, ellipsis, or overflow clipping.

**AC-HUD-8: Solo/Party Mode Transition**
Set `IsSoloParty = false`: party frames and chat panel are visible. Set `IsSoloParty = true`: party frames and chat panel both disappear atomically in the same frame.

**AC-HUD-9: Party Member Status Visual States**
For each MemberStatus value, assert the correct visual output:
- Online: HP bar at correct fill; no overlays; name at 100% opacity
- Dead: HP bar at 0%; skull icon overlay on frame; name at 50% opacity
- Ghost: HP bar frozen at value from transition time; dotted 1px border; name at 50% opacity
- OutOfZone: HP bar at 0%; "OUT" badge visible; name at 50% opacity

**AC-HUD-10: Ghost Frame Authority**
Set MemberStatus=Ghost with HP frozen at 60%. Simulate 5 seconds of client updates. Confirm HP bar remains at 60%. The HP bar changes only on a new `PartyMemberStatusChanged` event. While MemberStatus remains Ghost, no client-side `Update()` call, stat event, or timer may alter the frozen fill value.

**AC-HUD-11: Target Frame Dismiss — Death/Despawn**
Select an enemy target (target frame visible). Fire the entity despawn event. Target frame hides within 1 frame of event receipt.

**AC-HUD-12: Target Frame Dismiss — Manual Tap**
Select an enemy target (target frame visible). Tap anywhere on the target frame. Target frame hides; the enemy entity has not despawned.

**AC-HUD-13: Charge Bar Non-Interactive**
Confirm the charge bar element has `raycast target = false`. Tap the charge bar area: no Unity input event is logged; no handler fires.

**AC-HUD-14: Buff Tray Pagination**
Apply 7 active buffs. Tray shows 6 icons on page 1 and 1 icon on page 2; scroll arrows are visible. Let the 7th buff expire: `activeBuffCount` = 6 → tray shows 6 icons on page 1 with no scroll arrows, having snapped to page 1 automatically.

**AC-HUD-15: Buff Refresh Slot Position**
Apply Buff A (slot 0) and Buff B (slot 1). Re-apply Buff A. Confirm Buff A remains at slot 0 and Buff B remains at slot 1. Neither icon moves.

**AC-HUD-16: Loot Notification Queue**
Assign 3 ground items simultaneously. Confirm only one notification is visible. After the first item is picked up (GroundItemDespawned), the second notification appears. After the second, the third appears.

**AC-HUD-17: Loot Countdown Formula**
Assign a ground item with `expiryTick = currentTick + 2400`. Confirm pie fill = 1.0 immediately. After 600 client ticks, confirm fill = 0.75 (± 0.01).

**AC-HUD-18: Loot Warning Color Change**
Assign a ground item. At 600 ticks before expiry (`GroundItemExpiryWarning` fires), confirm pie fill color changes to Danger Alert `#E84040` within 1 frame.

**AC-HUD-19: Chat Panel / Joystick No-Overlap**
Confirm the chat panel's bounding rect does not intersect the joystick activation zone rect. Initiate a joystick drag from any point within the joystick zone: confirm no chat panel scroll event fires.

**AC-HUD-20: CHAT_HISTORY_DISPLAY_COUNT = 12**
Send 15 sequential party chat messages. Confirm the chat panel displays exactly 12 messages at the initial scroll position (3 oldest scrolled out of view).

**AC-HUD-21: Safe Area Compliance**
On a physical iPhone X+ in landscape (or Xcode simulator with safe area enabled): confirm all HUD elements are inside `Screen.safeArea` + 8px. No element overlaps the notch or home indicator region.

**AC-HUD-22: Player Dead State**
Trigger local player death (`ZoneSessionState = Dead`). Confirm: charge bar is hidden (not at 0% fill — completely hidden). Target frame is cleared. Party member frames remain visible.

**AC-HUD-23: HP Critical Pulse Frequency**
Trigger HP Critical state. Using Unity's Animation window or a frame-time log, measure the pulse period of the HP bar border. Confirm one full opacity cycle (off → on → off) completes in ≥ 500ms (≤ 2 Hz). Confirm the period does not exceed 2000ms (≥ 0.5 Hz — pulse must not appear static).

**AC-HUD-24: Canvas Dirty-Isolation**
Using Unity Frame Debugger: fire a single `OnStatChanged` event that changes HP. Confirm `Canvas.SendWillRenderCanvases()` is called on `HUD_Dynamic` only — not on `HUD_Static` or `HUD_Overlay`. Zero dirty calls on `HUD_Static` between zone entry and next party state change.

**AC-HUD-25: Top-Right Column Height Budget**
On an iPhone SE 3rd gen device or simulator in landscape (safe area height ≈ 320dp): confirm the minimap bottom edge and party frame stack bottom edge both fall within `Screen.safeArea.height - 8dp`. No party frame is clipped or zero-height. No overlap with the bottom safe area boundary.

**AC-HUD-26: Target Frame HP Update Source**
Select an enemy target. Fire an `OnStatChanged` event for the targeted entity (any StatID): confirm the target frame HP bar does NOT update. Then fire an `EntityHealthUpdate` for the same entity with a new HP value: confirm the target frame HP bar updates within 1 frame. This verifies the target HP path uses the networking relevance layer (CR-HUD-11), not `OnStatChanged`.

## Open Questions

**OQ-HUD-1** *(blocking implementation)*: **HUD Layer Framework Choice — Mixed Architecture Now Mandatory**
World-space damage numbers (Auto-Attack Combat GDD) require a UGUI world-space Canvas overlay regardless of what framework the HUD panel uses. Mixed UGUI + UI Toolkit is therefore mandatory at the project level. The ADR question is re-scoped: **which framework owns the HUD layer specifically**, given that a UGUI overlay already exists.

- **Option A (UGUI throughout)**: HUD uses UGUI Canvas with the three-layer dirty-isolation architecture (CR-HUD-22 / Canvas Layer Architecture in UI Requirements). Charge bar uses `Image.FillMethod.Horizontal` / `fillAmount` as Auto-Attack Combat specifies. Single renderer, no framework bridge. Simpler implementation.
- **Option B (UI Toolkit HUD + UGUI world-space overlay)**: HUD uses UI Toolkit (`VisualElement`, `UsageHints.DynamicColor`). Charge bar requires a UGUI-to-UIToolkit bridge or re-implementation of fill behavior in USS. Adds complexity at the boundary; requires deliberate event routing between the two systems.

The ADR must choose one option, document the charge bar implementation approach, and confirm the Canvas dirty-isolation architecture applies in either case. Until this ADR is filed, all HUD implementation epics are blocked.

**OQ-HUD-2** ~~*(blocking implementation)*~~ **RESOLVED (2026-06-19)**: **Party Chat GDD Position Amendment**
Party Chat GDD UI Requirements updated: "bottom-left" → "bottom-center, between the virtual joystick area (left) and the Combat UI skill buttons (right)." Cross-reference to CR-HUD-16 added. This blocker is closed.

**OQ-HUD-3** *(documentation)*: **Movement System GDD Downstream Table**
The Movement System GDD should list the HUD as a downstream dependent (HUD hosts the auto-run toggle, CR-HUD-20). Verify or add a downstream row in that GDD's Dependencies section.

**OQ-HUD-4** *(documentation)*: **Auto-Attack Combat GDD Downstream Table**
The Auto-Attack Combat GDD should list the HUD as a downstream dependent (HUD hosts charge bar and auto-attack toggle). Verify or add a downstream row in that GDD's Dependencies section.

**OQ-HUD-5** *(design gap)*: **XP Bar Fill Color**
No XP bar fill color has been formally established in the art bible. This GDD defers the color to `design/ux/hud.md`. The UX spec must define it before implementation.

**OQ-HUD-6** *(design gap — Combat UI scope)*: **Target-Nearest-Monster Button**
A target-cycling affordance (tap to select nearest targetable entity) is a common mobile MMORPG feature. Ownership has been designated to Combat UI (#28, Not Started). The Combat UI GDD must address this before implementation.

**OQ-HUD-7** *(pre-implementation gate)*: **UX Spec Required Before Implementation Epics**
`design/ux/hud.md` must be authored via `/ux-design` before any HUD implementation story begins. It will define: exact element positions and sizes, XP bar color, animation curves, transition durations, buff tray anchor position, top-right column height proof (CR-HUD-22), auto-run toggle exact position in bottom-right zone (CR-HUD-20), and safe area layout on target devices.

**OQ-HUD-8** *(pre-implementation gate)*: **Status Effects Event Interface**
CR-HUD-14 (Buff/Debuff Tray) requires events from Status Effects for buff apply and expire. The Status Effects GDD explicitly states: "The wire protocol does not yet define buff-state broadcast messages; client display of buff icons and durations is a provisional dependency on networking additions."

The HUD requires the following interface from Status Effects before buff tray implementation can begin:
- `OnBuffApplied(EntityID target, BuffID buffId, EntityID casterEntityID, float remainingTicks)` — fires on initial application and on refresh (CR-SE-4)
- `OnBuffExpired(EntityID target, BuffID buffId)` — fires at expiry (CR-SE-7/8)

These events may be defined as C# events on the Status Effects service, wire protocol broadcast messages, or both. Resolution requires an amendment to the Status Effects GDD and/or the networking wire protocol. This gate must be resolved before buff tray implementation epics are scheduled.
