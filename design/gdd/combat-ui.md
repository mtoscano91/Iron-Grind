# Combat UI

> **Status**: In Review
> **Author**: Manuel Toscano + Claude Code (game-designer, systems-designer)
> **Last Updated**: 2026-06-20
> **Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

## Overview

The Combat UI is the player's primary interaction surface during combat: the skill button bar, its associated cooldown feedback, and the consumable binding system. It occupies Zone E (bottom-right) of the HUD, as defined in `design/ux/hud.md`, and is implemented using UI Toolkit (UIDocument/VisualElement/UXML/USS) per ADR-005.

At runtime the Combat UI reads `SkillInstance` state from the Skill System — `Locked`, `Ready`, or `OnCooldown` — and presents up to 10 class skills across a paged or toggleable bar. Skill slots can also be bound to consumable items (potions), giving players fast access to consumables without leaving the skill input area. When a player taps a button, the Combat UI submits a `SkillCastRequest` to the Skill System and renders rejection feedback (mana, cooldown, range, lock) when the cast fails validation. Cooldown state is driven by server-authoritative `SkillCooldownUpdate` messages, with a client-side `SkillCooldownSnapshot` restoring display state on zone join or reconnect.

The bar is designed for one-handed right-thumb reach in landscape hold: buttons are touch-sized, the paging control is within the right-thumb zone, and the visible slot count balances information density against reachability. The system's feel is central to Rhythm Mastery — a player who can read cooldown state at a glance and land a skill between auto-attack beats is doing exactly what this UI is designed to enable.

## Player Fantasy

The skill bar is an instrument panel, and the player who's locked in reads it cold.

Mid-fight, the eyes sweep the bar in a single beat: Stab two ticks from ready, Rend's cooldown a third through, Shield Bash just lit. One glance, one decision — fire Rend now, hold Fury Strike for the next window, reach for the potion only when you know exactly what it costs. In Iron Grind, every press has a price: a skill cast resets the auto-attack beat, a potion burned is a charge spent — though the cadence keeps ticking regardless. The bar is where you read the trade before you make it.

A Combat UI that serves Rhythm Mastery is one the player can scan without looking away from the mob. Cooldowns are not decorations — they are the current state of a rotation the player is actively managing. The measure of success is a veteran who can tell at a glance whether to hold or spend, and who owns the outcome either way.

This is not a flashy system. It is a precise one.

## Detailed Design

### Core Rules

**§ Bar Structure**

**CR-CUI-1 — Zone and Ownership.** The Combat UI occupies Zone E (bottom-right) of the HUD as defined in `design/ux/hud.md`. Zone E's left boundary is the right safe area edge minus 292dp; its bottom boundary is the bottom safe area edge plus 8dp. The auto-attack toggle is NOT owned by Combat UI — it is owned by the HUD and placed in Zone D.

**CR-CUI-2 — Slot Type.** Each of the 8 slots binds exactly one class skill (`SkillID`) or is empty. Consumable items are not bound to Combat UI slots. Consumables are managed by the Consumable Use System's separate potbar, which renders as a distinct element above or adjacent to the Combat UI skill bar.

**CR-CUI-3 — 8-of-10 Binding.** A class has 10 skills. Only 8 may be bound to the Combat UI bar simultaneously. Skills not bound to the bar are viewable in the Character Screen → Skills tab but cannot be cast during combat. There is no combat-accessible third page. Binding management is done via the in-bar long-press context menu (CR-CUI-11).

**CR-CUI-4 — Duplicate Binding.** The same `SkillID` may be bound to multiple slots. Both slots reflect the same `SkillInstance` state — cooldown sweeps are synchronized.

**§ Layout**

**CR-CUI-5 — Primary Row Layout.** The primary row (S1–S4) is always visible during gameplay. Slot dimensions: 52×52dp. Slot-to-slot gap: 8dp. The [+] toggle button (44×44dp) is positioned 8dp to the right of S4, right-aligned to the right safe area edge minus 8dp. Primary row bottom edge: bottom safe area edge plus 8dp. Total footprint from right safe area edge: 292dp.

**CR-CUI-6 — Expanded Row Layout.** The expanded row (S5–S8) is identical in dimensions to the primary row (4 slots × 52dp, 8dp gaps). When visible, it sits 8dp above the primary row top edge, occupying 68dp–120dp above the bottom safe area edge. The expanded row background is `rgba(0,0,0,0.5)` (semi-transparent) to reduce visual conflict with party frames in party mode.

**§ Expand/Collapse**

**CR-CUI-7 — Toggle Gesture.** Tapping [+] expands the bar; tapping [-] collapses it. No swipe gesture is defined at MVP.

**CR-CUI-8 — Animation.** Expand/collapse uses a 200ms `height` transition (USS `transition` property). On expand: set `display: Flex` → next scheduled callback sets `height: 52dp`. On collapse: animate `height: 0dp` → on `TransitionEndEvent` set `display: None`. Parent container must have `overflow: hidden`. Guard against `TransitionEndEvent` not firing (panel backgrounded or element destroyed mid-transition) by storing collapse-pending state explicitly.

**CR-CUI-9 — State Persistence.** Bar expanded/collapsed state is persisted server-side as part of the `SkillBarLayout` record. The record includes a 1-bit `barIsExpanded` flag alongside the 8 skill bindings. On zone entry, the bar restores to its last persisted state. A character with no saved layout defaults to Collapsed on first zone entry. The flag is written on any expand/collapse gesture, subject to the same save path as binding changes (CR-CUI-15).

**CR-CUI-10 — Casting from Expanded Row.** Skills in S5–S8 can only be cast while the expanded row is visible. The bar does NOT auto-collapse after a cast from the expanded row.

**§ Slot Binding**

**CR-CUI-11 — Long-Press Binding.** Long-pressing any slot (hold ≥ 500ms, finger movement < 8dp) opens the binding context menu for that slot. If the finger moves ≥ 8dp during the hold, the long-press is cancelled (drag disambiguation). A `PointerCancelEvent` (system interrupt — incoming call, Control Center swipe) cancels the long-press and clears the scheduled callback. Binding is available at all times, including during active combat.

**CR-CUI-12 — Binding Menu Content.** The context menu lists all 10 class skills. For each skill: icon, name, unlock level, and current slot assignment (if any). Tapping a skill binds it to the long-pressed slot, replacing any prior binding. Dismissed by selection or by tapping the fullscreen transparent blocker element (see CR-CUI-13).

**CR-CUI-13 — Menu Positioning and Dismissal.** The context menu opens upward from the center-top of the tapped slot. If computed menu top edge < `safeArea.top + 8dp`, the menu is clamped downward to `safeArea.top + 8dp`. A fullscreen transparent `VisualElement` (`PickingMode.Position`) sits behind the menu; tapping it dismisses without binding. The menu is pre-created in `OnEnable` and kept `visible: false` (not `display: none`) to preserve `worldBound` for positioning.

**CR-CUI-14 — Locked Skill Binding.** A player may bind a skill with `IsUnlocked == false` to any slot. While locked, the slot renders at 50% opacity with a lock icon overlay, is non-interactive (tap ignored; no `SkillCastRequest` sent), and displays "Unlocks at Level [X]" on tap. On `SkillUnlockNotification`, the slot transitions to active state without requiring rebinding.

**CR-CUI-15 — Skill Bar Persistence.** The `SkillBarLayout` (8 × `SkillID | Empty`) is persisted server-side as part of the character save record. On first zone entry with no saved layout, the first 8 unlocked skills are auto-assigned to S1–S8 in unlock-level ascending order; remaining slots are empty.

**§ Skill Casting**

**CR-CUI-16 — Client Pre-Checks.** Tapping a slot (hold < 500ms, movement < 8dp) triggers these checks in order. If any check fails, no `SkillCastRequest` is sent:

| Check | Condition | Visual Response |
|---|---|---|
| 1. Slot empty | No `SkillID` bound | No-op |
| 2. Skill locked | `IsUnlocked == false` | Lock-icon pulse animation |
| 3. Character dead | `ZoneSessionState == Dead` or `Respawning` | Slot shake animation |
| 4. On cooldown | `cooldownExpiryTick > clientCurrentTick` | Slot shake; cooldown sweep continues |
| 5. No target (SingleHostile) | No hostile target selected | "Select a target" toast 2s |
| 6. No target (SingleFriendly) | No friendly target and player is not sole valid target | "Select a target" toast 2s; exception: auto-select self if `TargetConstraint == SingleFriendly` and no party exists |

**CR-CUI-17 — Optimistic Cast.** When all pre-checks pass, the client submits `SkillCastRequest { casterEntityID, skillID, targetEntityID, clientTickNumber }` and immediately enters `OnCooldown` visual state for the slot (optimistic). For skills with `CooldownTicks == 0` (e.g., Warrior Slash), no cooldown sweep is shown.

**CR-CUI-18 — Cast Result Handling.** When `SkillCastResult` arrives:

| `SkillCastRejectionCode` | Visual Response |
|---|---|
| *(accepted)* | No change; server `SkillCooldownUpdate` corrects any timing drift |
| `InvalidSkill` (1) | Revert to pre-cast state; silent (console log only) |
| `SkillLocked` (2) | Revert; lock-icon pulse (pre-check 2 should have prevented send) |
| `OnCooldown` (3) | Revert; slot shake; re-apply cooldown from accompanying `SkillCooldownUpdate` |
| `InvalidTarget` (4) | Revert; "No valid target" toast 2s |
| `OutOfRange` (5) | Revert; "Target out of range" toast 2s |
| `InsufficientMana` (6) | Revert; MP bar red flash 500ms; slot red flash 300ms |
| `ServerError` (7) | Revert to pre-cast state; "Server error" toast 2s |
| `Silenced` (8) | Revert; purple tint flash on slot 300ms + X overlay 500ms; "Cannot cast while silenced" toast 2s |
| `CasterNotAlive` (9) | Revert; slot shake (pre-check 3 should have prevented send; dead-state sync lag) |

**§ Cooldown Display**

**CR-CUI-19 — Cooldown Renderer.** Each slot has a cooldown overlay `VisualElement` with `PickingMode.Ignore`. The overlay renders a radial arc (clockwise from 12 o'clock) at `fraction` from F-CUI-1. **Renderer mechanism is implementation-defined** — the implementation team must choose an approach that satisfies: *8 concurrent cooldown arcs (all 8 slots in `OnCooldown` per EC-CUI-3) must sustain 60fps within the draw-call budget (≤100 total) on iPhone SE 3rd gen (A15 Bionic, iOS Metal TBDR).* Acceptable approaches include a shader-based quad with a `_Progress` float uniform, or `Painter2D` `generateVisualContent`. Arc position must update at 60fps (per render frame using `Time.time`-based interpolation, not per server tick) to produce smooth animation. When `fraction ≤ 0`, the overlay is hidden. Any cooldown arc implementation must be validated by `performance-analyst` before the UI implementation sprint begins.

**CR-CUI-20 — Cold-Start Display.** On zone entry, all slots display as Ready until `SkillCooldownSnapshot` arrives. When the snapshot arrives, any slot with `cooldownExpiryTick > clientCurrentTick` immediately enters `OnCooldown` visual state. The transient all-Ready state is intentional: a false-positive (showing Ready when on cooldown) causes at worst one wasted cast; a false-negative would suppress valid casts.

**§ Device Adaptation**

**CR-CUI-21 — Chat Combat-Collapse.** The HUD's composition layer computes available chat corridor width as: `corridorWidth = safeArea.width − 292dp (skill bar) − joystickZoneWidth`. If `corridorWidth < 140dp`, the HUD signals the chat panel to enter combat-collapsed mode (renders as a small expand-button; full chat accessible by tapping). Combat UI exposes its footprint (292dp) as a layout constant; the collapse decision and signaling are owned by the HUD.

---

### States and Transitions

**Skill Slot States (per slot):**

| State | Condition |
|---|---|
| `Empty` | No `SkillID` bound |
| `Locked` | `SkillID` bound; `IsUnlocked == false` |
| `Ready` | `SkillID` bound; `IsUnlocked == true`; `cooldownExpiryTick ≤ clientCurrentTick` |
| `OnCooldown` | `SkillID` bound; `IsUnlocked == true`; `cooldownExpiryTick > clientCurrentTick` |
| `Disabled` | `ZoneSessionState == Dead` or `Respawning` — overrides all other states, entire bar |

| Transition | Trigger | Owner |
|---|---|---|
| Any → `Empty` | Player clears slot binding | Combat UI |
| `Empty` → `Ready` | Player binds an unlocked skill | Combat UI |
| `Empty` → `Locked` | Player binds a locked skill | Combat UI |
| `Locked` → `Ready` | `SkillUnlockNotification { skillID }` received | Skill System |
| `Ready` → `OnCooldown` | `SkillCastRequest` sent (optimistic) or `SkillCooldownUpdate` received | Combat UI / Skill System |
| `OnCooldown` → `Ready` | `clientCurrentTick ≥ cooldownExpiryTick` | Combat UI (client tick) |
| Any → `Disabled` | `ZoneSessionState` changes to `Dead` or `Respawning` | Zone Session |
| `Disabled` → [prior state] | `ZoneSessionState` changes to `Alive` | Zone Session |

**Bar States:**

| State | Condition |
|---|---|
| `Collapsed` | Only primary row (S1–S4) visible; default on first zone entry (no saved layout) |
| `Expanded` | Both rows visible (S1–S8) |

| Transition | Trigger |
|---|---|
| `Collapsed` → `Expanded` | Player taps [+] |
| `Expanded` → `Collapsed` | Player taps [−] |

---

### Interactions with Other Systems

| System | Data IN | Data OUT | Interface Owner | Notes |
|---|---|---|---|---|
| **Skill System** | `SkillCooldownUpdate { skillID, cooldownExpiryTick }` (S→C, after each cast); `SkillCooldownSnapshot` (S→C, on zone join); `SkillCastResult { castAccepted, rejectionCode, skillID }` (S→C, R-U batch); `SkillUnlockNotification { skillID }` (S→C, R-OD) | `SkillCastRequest { casterEntityID, skillID, targetEntityID, clientTickNumber }` (C→S, U-U, 16 bytes) | Wire protocol owned by Networking Core | `cooldownExpiryTick` is an absolute server tick per CR-SK-12 (amended 2026-06-20) |
| **Character Stats** | `GetEffectiveStat(entityID, StatID.CurrentMP)` (read-only; for MP bar visual feedback on `InsufficientMana` rejection) | None | `ICharacterStatsProvider` | Authoritative mana validation is server-side only |
| **Zone Session** | `ZoneSessionState` (current: `Alive`, `Dead`, `Respawning`) | None | `ZoneSessionManager` | State change to `Dead` triggers `Disabled` state on all slots |
| **Character Persistence** | `SkillBarLayout` (8 × `SkillID \| Empty`) read at zone entry | `SkillBarLayout` written on any binding change | Character Persistence owns save schema | |
| **HUD** | Zone E boundary coordinates requested by HUD for composition | `skillBarFootprint = 292dp` constant | HUD is parent layout owner; Combat UI reports its footprint | HUD owns auto-attack toggle (Zone D); HUD computes chat corridor |
| **CUS** | None (CUS potbar is a separate sibling element) | None | CUS owns its potbar independently | The two elements render independently in Zone E |

## Formulas

**F-CUI-1 — Cooldown Sweep Fraction**

Determines the fraction of the cooldown overlay arc to display. Called every client tick while a slot is in `OnCooldown` state.

```
// Guard: exit immediately for these cases — do not render overlay
if CooldownTicks == 0: return (no overlay rendered; slot has no cooldown)
if cooldownExpiryTick ≤ clientCurrentTick: return (cooldown already expired; overlay hidden)

// Safe signed subtraction: cast to long before subtracting to prevent uint underflow
remainingTicks = max( 0L, (long)cooldownExpiryTick − (long)clientCurrentTick )
fraction = Mathf.Clamp01( (float)remainingTicks / (float)CooldownTicks )
```

| Variable | Type | Source | Range |
|---|---|---|---|
| `cooldownExpiryTick` | `uint` | `SkillCooldownUpdate` / `SkillCooldownSnapshot` from Skill System | [0, MAX_UINT] |
| `clientCurrentTick` | `uint` | `NetworkManager.ServerTime.Tick` (ADR-004) | [0, MAX_UINT] |
| `CooldownTicks` | `int` | `SkillDefinition.CooldownTicks` (Skill System) | [1, 6000] (0 is guarded above) |
| `remainingTicks` | `long` | Intermediate (signed subtraction) | [0, MAX_UINT] |
| `fraction` | `float` | Output | [0.0, 1.0] |

`fraction = 1.0` → cooldown just started (full overlay). `fraction = 0.0` → cooldown expired (overlay hidden, slot transitions to Ready).

When `CooldownTicks == 0` (e.g., Warrior Slash — mana-gated, no cooldown), no overlay is rendered; F-CUI-1 is not called. The guard at the top of the formula ensures this case can never reach the division.

**Why signed subtraction matters (C# uint arithmetic):** If `clientCurrentTick > cooldownExpiryTick` (normal post-expiry state), unsigned subtraction wraps to near-MAX_UINT, causing `fraction` to clamp to 1.0 — the arc renders as fully-full instead of hidden. The `(long)` cast prevents this.

**Example**: Stab cast at tick 4000. `CooldownTicks = 40`. `cooldownExpiryTick = 4040`. At tick 4020: `remainingTicks = 4040 − 4020 = 20`. `fraction = 20.0 / 40.0 = 0.5` — half the arc is displayed.

---

**F-CUI-2 — Zone E Left Boundary**

The x-coordinate of S1's left edge in panel coordinate space, measured from the left safe area edge.

```
zoneELeftEdge = safeArea.width − 292dp
```

| Variable | Source |
|---|---|
| `safeArea.width` | `RuntimePanelUtils.ScreenToPanel(panel, Screen.safeArea)` — panel coordinate space width |
| `292dp` | Constant: 4×52 + 3×8 + 8 + 44 + 8 (primary row footprint) |

| Device | `safeArea.width` | `zoneELeftEdge` |
|---|---|---|
| iPhone SE 3rd gen | 651dp | 359dp |
| iPhone X | 812dp | 520dp |
| iPhone 14 Pro | 844dp | 552dp |

---

**F-CUI-3 — Chat Corridor Width**

Computed by the HUD composition layer to determine whether combat-collapse mode is required for the chat panel.

```
corridorWidth = safeArea.width − 292dp − joystickZoneWidth
```

| Variable | Source | Notes |
|---|---|---|
| `safeArea.width` | Panel coordinate space | Device-dependent |
| `292dp` | Combat UI footprint constant | Fixed |
| `joystickZoneWidth` | HUD layout (≈ 45% of `safeArea.width`) | ~293dp–380dp by device |

If `corridorWidth < 140dp`, the HUD triggers combat-collapse mode for the chat panel.

Example at SE3: `651 − 292 − 293 = 66dp` → combat-collapse activated.

---

**F-CUI-4 — Implementation Constants**

| Constant | Value | Effect |
|---|---|---|
| `LONG_PRESS_DURATION_MS` | 500 | Hold must exceed this before context menu opens |
| `LONG_PRESS_CANCEL_DISTANCE_DP` | 8 | Finger movement ≥ this cancels the long-press |
| `CHAT_CORRIDOR_MIN_WIDTH_DP` | 140 | Below this, chat panel enters combat-collapsed mode |
| `CONTEXT_MENU_SAFE_TOP_MARGIN_DP` | 8 | Context menu top edge clamped to `safeArea.top + 8dp` |

## Edge Cases

**EC-CUI-1 — First Zone Entry With No Saved Layout.**
Character has no persisted `SkillBarLayout` (fresh character or reset). Auto-assignment: slots S1–S8 are filled with the first 8 skills from `IClassRegistry.GetClassSkills(classType)` whose `IsUnlocked == true`, sorted by `UnlockLevel` ascending. If fewer than 8 skills are unlocked, remaining slots are `Empty`. Fewer than 1 unlocked skill at L1 is impossible per CR-SK-22.

**EC-CUI-2 — Invalid Bound SkillID at Zone Entry.**
On zone entry, the loaded `SkillBarLayout` is validated: any bound `SkillID` not present in `IClassRegistry.GetClassSkills(classType)` is cleared to `Empty` and the corrected layout is re-persisted. The slot renders as empty without a player-facing error.

**EC-CUI-3 — All 8 Slots Bound to the Same Skill.**
All 8 slots drive from the same `SkillInstance`. One tap casts the skill; all 8 slots simultaneously enter `OnCooldown`; all 8 expire simultaneously. Valid player choice; no special handling required.

**EC-CUI-4 — `SkillCooldownSnapshot` Lost or Delayed.**
All unlocked slots display as Ready until the snapshot arrives. Slots self-correct on the first rejected cast: the `OnCooldown` rejection handler (CR-CUI-18) restores cooldown state from the accompanying `SkillCooldownUpdate` (guaranteed by CR-SK-12). If no `SkillCooldownSnapshot` arrives within 5 seconds of zone entry, the client re-requests it via the Zone Session interface. If the second request also produces no snapshot within a further 5 seconds, the client accepts the all-Ready state permanently for this zone session and logs a warning. Self-correction via rejected casts remains active for the rest of the session. No slot transitions to `OnCooldown` unless a `SkillCooldownUpdate` or `SkillCooldownSnapshot` with `cooldownExpiryTick > clientCurrentTick` has been received.

**EC-CUI-5 — Expand Animation Interrupted.**
If the panel is backgrounded or destroyed mid-transition, `TransitionEndEvent` does not fire. The row remains at `height: 0` with `display: Flex` instead of `display: None` — no visible impact but a stale display state. On next zone entry, bar state loads from the persisted `SkillBarLayout` (`barIsExpanded` flag), which triggers a clean state restore and resolves the stale display.

**EC-CUI-6 — Context Menu Open During Zone Transition.**
`OnZoneExit()` must call `DismissContextMenu()` to dismiss any open binding menu and its fullscreen blocker before zone unload. A persisted blocker element intercepts all touches in the new zone.

**EC-CUI-7 — Multi-Touch Simultaneous Slot Taps.**
Two touches on two different slots in the same frame are processed independently: each triggers its own pre-check and sends its own `SkillCastRequest` if valid. No multi-touch suppression on the skill bar. The server validates each request in arrival order.

**EC-CUI-8 — Client Tick Approaching `uint` Overflow.**
Guard in F-CUI-1: if `cooldownExpiryTick < clientCurrentTick` and `clientCurrentTick − cooldownExpiryTick > CooldownTicks × 2`, treat the slot as Ready. Log as a warning. This edge case occurs at ~497 days of continuous tick accumulation at 20 ticks/second.

**EC-CUI-9 — Cast Submitted While Prior `SkillCastResult` Is Pending.**
The client may have multiple outstanding `SkillCastRequest` messages simultaneously. Each `SkillCastResult` is matched by `skillID` and handled independently. No client-side cast queue or serialization.

**EC-CUI-10 — Server-Side Layout Correction Push.**
If the server pushes a corrected `SkillBarLayout` mid-session (e.g., correcting a persistence error), the client applies it immediately: clears existing bindings, applies the new layout, and refreshes all slot visual states. In-flight `SkillCastRequest` messages are not cancelled; their results are handled normally against the post-update state.

## Dependencies

**Upstream Dependencies** (systems this GDD depends on):

| System | GDD | What Combat UI depends on |
|---|---|---|
| Skill System | `design/gdd/skill-system.md` | `SkillInstance` state; `SkillCooldownUpdate`, `SkillCooldownSnapshot`, `SkillCastResult`, `SkillUnlockNotification` messages; `SkillCastRequest` submission; `IClassRegistry.GetClassSkills()` for binding menu and auto-assignment |
| Character Stats | `design/gdd/character-stats.md` | `ICharacterStatsProvider.GetEffectiveStat(entityID, StatID.CurrentMP)` — read-only; for MP bar visual feedback on `InsufficientMana` rejection |
| Zone Instancing | `design/gdd/zone-instancing.md` | `ZoneSessionState` (Alive / Dead / Respawning) for slot `Disabled` state |
| Networking Core | `design/gdd/networking-core.md` | Wire protocol delivery guarantees for `SkillCastRequest` (U-U) and `SkillCastResult` (R-U batch) |
| Character Persistence | *(no standalone GDD at MVP)* | `SkillBarLayout` save/load; Combat UI reads on zone entry, writes on binding change |
| HUD | `design/gdd/hud.md`, `design/ux/hud.md` | Zone E boundary definition; parent layout composition; auto-attack toggle ownership (Zone D, HUD-owned) |
| CUS | `design/gdd/consumable-use-system.md` | Potbar occupies Zone E adjacent to the skill bar; Combat UI must expose its 292dp footprint constant so HUD can position the CUS potbar without overlap |

**Downstream Dependents** (systems that depend on this GDD):

None at MVP. Combat UI is a leaf-level Presentation Layer system — no other designed system depends on it.

**Pending Amendments Triggered by This GDD:**

| Document | Amendment Required | Status |
|---|---|---|
| `design/gdd/skill-system.md` | CR-SK-12: change `SkillCooldownUpdate { skillID, ticksRemaining }` to `SkillCooldownUpdate { skillID, cooldownExpiryTick }` (absolute server tick) per F-CUI-1 | **RESOLVED** — amended 2026-06-20 |
| `design/gdd/hud.md` | Zone D: confirm auto-attack toggle is assigned to Zone D, owned by HUD | Open |
| `design/ux/hud.md` | OQ-UX-HUD-2 and OQ-UX-HUD-3: mark RESOLVED — expanded skill bar and consumable slots addressed by this GDD | **RESOLVED** — marked 2026-06-20 |

## Tuning Knobs

All values stored in `assets/data/CombatUIConfig.asset` (ScriptableObject). None are hardcoded in C#.

| Knob | Default | Safe Range | Gameplay Effect |
|---|---|---|---|
| `LongPressDurationMs` | 500 | [300, 1000] | Hold time before binding menu opens. Lower = more accidental activations during combat; higher = slower deliberate binding. |
| `LongPressCancelDistanceDp` | 8 | [4, 20] | Finger movement that cancels long-press. Lower = more accidental cancels; higher = tolerates hand drift. |
| `ExpandAnimationDurationMs` | 200 | [100, 400] | Speed of expand/collapse height transition. Shorter feels snappier; longer helps players track the state change. |
| `ExpandedRowBackgroundOpacity` | 0.5 | [0.0, 0.85] | Opacity of the expanded row's dark background. Lower = party frames bleed through; higher = obscures party information. |
| `CooldownOverlayOpacity` | 0.7 | [0.4, 0.9] | Opacity of the radial cooldown arc. Lower = harder to read; higher = may obscure the skill icon. |
| `RejectionToastDurationMs` | 2000 | [1000, 3000] | Visibility duration for rejection toast messages (no target, out of range, server error). |
| `InsufficientManaFlashDurationMs` | 500 | [200, 800] | Duration of MP bar red flash on `InsufficientMana` rejection. |
| `SlotFlashDurationMs` | 300 | [100, 600] | Duration of red slot flash on `InsufficientMana` rejection. |
| `ChatCorridorMinWidthDp` | 140 | [100, 200] | Threshold below which chat panel enters combat-collapsed mode. Do not reduce below 100dp — chat becomes unreadable at that width. |

## Visual/Audio Requirements

**Visual:**

| Element | Requirement |
|---|---|
| Skill slot button | 52×52dp touch target; 44×44dp icon area (4dp padding all sides); dark panel background with 1dp border |
| Slot icon | Class skill icon sprite. 50% opacity when `Locked`. Full opacity when `Ready` or `OnCooldown`. |
| Lock icon overlay | Padlock sprite, center-anchored, 16×16dp. Visible only in `Locked` state. |
| Cooldown arc | Radial sweep (implementation-defined renderer — see CR-CUI-19). Color: white at `CooldownOverlayOpacity` (default 0.7). Sweeps clockwise from 12 o'clock. Arc stroke width: 4dp. Arc updates at 60fps via `Time.time`-based interpolation. |
| Cooldown countdown number | Centered Bold 11sp text in `#E8E6DF`, rendered atop the cooldown arc. Displays remaining ticks as a whole-second countdown: `ceil((cooldownExpiryTick − clientCurrentTick) / 20)` (at 20 ticks/sec). Hidden when `fraction == 0`. Matches skill-system.md CR-SK-10 visual spec. |
| Slot shake animation | 4dp horizontal oscillation, 3 cycles, 150ms total. Applied on cooldown-tap and dead-state-tap. |
| Red slot flash | Slot background tints red for `SlotFlashDurationMs`. Applied on `InsufficientMana` rejection only. |
| [+] / [-] toggle button | 44×44dp; chevron or +/- icon; no skill icon content. |
| Expanded row background | `rgba(0,0,0,0.5)` behind S5–S8; `border-radius: 8dp`. |
| Toast messages | Centered horizontally within Zone E, 72dp above bottom safe area edge. Font: 12sp white bold. Duration: `RejectionToastDurationMs`. |

**Audio:**

Combat UI does not own combat sounds (skill cast SFX, impact). Those are owned by the Skill System's audio integration. Combat UI owns only UI event sounds:

| Event | Sound |
|---|---|
| Tap a Ready skill | Short UI tap SFX |
| Tap an `OnCooldown` or `Locked` slot | Negative feedback SFX ("deny") |
| Context menu open | Subtle menu-open SFX |
| Context menu close / dismiss | Subtle dismiss SFX |
| Bar expand | Short swoosh SFX |
| Bar collapse | Short reverse swoosh SFX |

SFX asset selection is delegated to the Audio Director. Combat UI engineers implement trigger points only.

## UI Requirements

The Combat UI is itself the UI layer — this section specifies implementation constraints.

- **Framework**: UI Toolkit (UIDocument / VisualElement / UXML / USS) per ADR-005. No UGUI components.
- **USS file isolation**: Skill bar styles in `USS_CombatUI.uss`; animation transitions in `USS_CombatUI_Animations.uss` to contain syntax errors during iteration.
- **USS CI gate**: Per ADR-005 — any `.uss` syntax error blocks merge.
- **`PickingMode.Ignore`**: Must be set individually on all non-interactive overlay elements (cooldown arc, lock icon, disabled mask). Does not propagate to children.
- **Cooldown renderer**: Implementation-defined (see CR-CUI-19). Must be validated by `performance-analyst` before UI implementation sprint. Arc must update at 60fps via `Time.time`-based interpolation, not per server tick. No `style.scale` or `style.transform` on cooldown elements (Unity 6.3: `VisualElement.transform` setter removed — use `style.translate` / `style.rotate` / `style.scale` USS instead).
- **Safe area application**: Combat UI root element applies safe area insets via `RuntimePanelUtils.ScreenToPanel` before layout computation. The 8dp margins in this GDD are applied after the safe area conversion.
- **`PanelSettings.clearColor`**: Must remain `false` — verified each build. Owned by HUD, not Combat UI.

## Acceptance Criteria

**AC-CUI-1 — Skill Bar Default Layout.**
QA: Launch a zone with any character. The primary row (4 skill slots + [+] toggle) is visible in the bottom-right corner. Slots are 52×52dp. Slot-to-slot gap is 8dp. [+] is 8dp to the right of S4.
Pass: all elements present at correct dimensions. Fail: any slot missing, overlapping, or outside Zone E boundary.

**AC-CUI-2 — Expand/Collapse.**
QA: Tap [+]. The expanded row (S5–S8) appears above the primary row within 200ms with an animation (CR-CUI-8: 200ms `height` transition). Primary row does not move. [+] becomes [-]. Tap [-]: expanded row disappears within 200ms.
Pass: both transitions animate within 200ms; primary row stationary; toggle icon updates. Fail: snap without animation; transition exceeds 200ms; primary row shifts; icon unchanged.

**AC-CUI-3 — Auto-Assignment on Fresh Character.**
QA: Create a new L1 character. Enter a zone for the first time.
Pass: S1 is pre-filled with the class's L1 skill. Fail: all slots empty on first zone entry.

**AC-CUI-4 — Skill Binding via Long-Press.**
QA: Long-press any slot for ≥ 500ms without moving the finger. Binding context menu opens. Tap a skill. Slot icon updates to the selected skill.
Pass: menu opens; skill bound; icon visible. Fail: menu does not open; selection has no effect; icon unchanged.

**AC-CUI-5 — Long-Press Cancel on Drag.**
QA: Press and hold a slot, then slide finger ≥ 8dp before 500ms.
Pass: no context menu opens. Fail: menu opens after drag.

**AC-CUI-6 — Binding Persistence Across Zones.**
QA: Bind a skill to S3. Leave the zone. Re-enter.
Pass: S3 still shows the bound skill on re-entry. Fail: S3 is empty.

**AC-CUI-7 — Only 8 of 10 Skills Bindable.**
QA: With 10 skills unlocked, bind all 8 slots. Open the binding menu on any slot.
Pass: all 10 skills appear in the menu; replacing a bound skill works. Fail: menu shows fewer than 10 options; S9/S10 hidden after slots are filled.

**AC-CUI-8 — Cooldown Display.**
QA: Cast Warrior Stab (40-tick cooldown at 20 ticks/sec = 2.0 seconds). A radial arc immediately appears on the slot and shrinks smoothly over ~2 seconds (at 60fps, not per server tick), then disappears and the slot is tappable again.
Pass: arc visible immediately post-cast; countdown number reads "2" then "1" then hidden; arc expires ~2 seconds later; slot interactive after expiry. Fail: no arc; arc updates at 20Hz (visible stepping); slot blocked after expiry.

**AC-CUI-9 — Cold-Start All-Ready State.**
QA: Cast a 30s cooldown skill. Leave zone before it expires. Re-enter.
Pass: slot briefly shows Ready immediately after zone entry, then updates to the correct cooldown state when `SkillCooldownSnapshot` arrives (~1-2s later). Fail: slot shows cooldown before snapshot arrives; fails to update when snapshot arrives.

**AC-CUI-10 — Client Pre-Check: Cooldown.**
QA: Tap a slot visually in cooldown.
Pass: slot shakes; no `SkillCastRequest` sent (verify via network log). Fail: request sent; no animation.

**AC-CUI-11 — Client Pre-Check: No Target.**
QA: Tap a `SingleHostile` skill with no enemy selected.
Pass: "Select a target" toast visible for ~2s; no `SkillCastRequest` sent. Fail: request sent; no toast.

**AC-CUI-12 — Rejection: InsufficientMana.**
QA: Deplete mana below a skill's cost. Tap that skill.
Pass: MP bar red flash ~500ms; slot red flash ~300ms; slot returns to Ready state. Fail: no visual feedback; slot remains in cooldown.

**AC-CUI-13 — Rejection: OutOfRange.**
QA: Select an enemy; move out of range; tap a skill.
Pass: "Target out of range" toast 2s; slot returns to Ready. Fail: no toast; slot stays in cooldown.

**AC-CUI-14 — Disabled State on Death.**
QA: Die in combat. Tap all 8 skill slots.
Pass: all slots shake; no `SkillCastRequest` sent for any slot. Fail: any slot sends a request while dead.

**AC-CUI-15 — Locked Skill Slot.**
QA: Bind a skill above the character's current level. Tap the slot.
Pass: lock icon visible on slot; no cast animation; no `SkillCastRequest` sent. Fail: slot appears Ready; cast attempted.

**AC-CUI-16 — Chat Combat-Collapse on Narrow Devices.**
QA: Test on iPhone SE 3rd gen (safe area width = 651dp, joystickZoneWidth ≈ 293dp → corridorWidth ≈ 66dp, which is < 140dp). Enter a zone.
Pass: `corridorWidth < CHAT_CORRIDOR_MIN_WIDTH_DP (140dp)` is true → chat panel renders in combat-collapsed mode (small expand button only). Tapping expand button opens full chat. Fail: full-width chat panel renders when corridorWidth < 140dp.

**AC-CUI-17 — Multi-Touch Simultaneous Cast.**
QA: With two skills Ready, tap S1 and S2 simultaneously with two fingers.
Pass: two `SkillCastRequest` messages sent within one frame (verify via network log). Fail: only one request sent.

**AC-CUI-18 — Bar State Persistence Across App Restart.**
QA: Expand the bar (tap [+]). Force-quit the app. Relaunch and re-enter the same zone.
Pass: bar is in Expanded state on zone entry (persisted `barIsExpanded == true` restored from `SkillBarLayout`). Fail: bar resets to Collapsed on every launch.

**AC-CUI-19 — Expanded Row Casting Gating.**
QA: Collapse the bar. Attempt to cast S5–S8 by any means (e.g., network injection or keybind). Then expand the bar and cast from S5 normally.
Pass: S5–S8 are non-interactive while collapsed (no `SkillCastRequest` sent); cast succeeds from expanded row. Also verify: casting from expanded row does NOT auto-collapse the bar.
Fail: cast possible from collapsed bar; or bar collapses after a successful cast from S5–S8.

**AC-CUI-20 — Silenced Rejection Visual.**
QA: Apply a Silence debuff to the player character (test harness or debug command). Tap any skill slot.
Pass: client pre-check 3 passes (alive), server validates and returns `Silenced (8)` rejection; slot shows purple tint flash 300ms + X overlay 500ms; "Cannot cast while silenced" toast visible for 2s. Fail: no visual distinction from other rejections; toast absent; server does not return code 8.

## Open Questions

**OQ-CUI-1 — CUS Potbar Position in Zone E.**
This GDD specifies that the Combat UI exposes a 292dp footprint constant and that the CUS potbar is a separate adjacent element. The exact position of the CUS potbar in Zone E (above the skill bar? left-adjacent?) is not specified here — owned by the HUD layout and CUS GDD.
*Owner: HUD GDD + CUS GDD. Blocks HUD implementation sprint.*

**OQ-CUI-2 — Cooldown Arc Visual Design.**
Arc color is placeholder white at 0.7 opacity. Pending Art Director review. Candidates: element-colored arc, dark dimming overlay, white arc with border. The art bible does not currently define a UI cooldown overlay color.
*Owner: Art Director + art-bible.md. Advisory.*

**OQ-CUI-3 — CR-SK-12 Wire Format Amendment. RESOLVED 2026-06-20.**
CR-SK-12 amended in `design/gdd/skill-system.md`: `SkillCooldownUpdate { skillID, ticksRemaining }` → `SkillCooldownUpdate { skillID, cooldownExpiryTick }` (absolute server tick). No longer blocking.

**OQ-CUI-4 — Zone D Auto-Attack Toggle Specification (BLOCKING).**
This GDD reassigns the auto-attack toggle from Zone E to Zone D (HUD-owned). The HUD GDD must formally specify Zone D contents and the toggle's exact dp coordinates before the HUD implementation sprint.
*Owner: HUD GDD. Blocks HUD implementation.*

**OQ-CUI-5 — OQ-UX-HUD-2 and OQ-UX-HUD-3 Resolution.**
`design/ux/hud.md` logged OQ-UX-HUD-2 (expanded skill bar) and OQ-UX-HUD-3 (consumables on skill buttons) as open. Both resolved by this GDD:
- OQ-UX-HUD-2: Skill bar expands from 4 to 8 slots via [+] toggle.
- OQ-UX-HUD-3: Consumables handled by CUS potbar (separate element) — skill slots are skills-only.
*Owner: `design/ux/hud.md`. Action: mark both RESOLVED, referencing this GDD.*

**OQ-CUI-6 — Status Effect Skill Disable Visual. RESOLVED 2026-06-20.**
Added `SkillCastRejectionCode: Silenced (8)` per CR-CUI-18. Silenced slots show a distinct visual: purple tint flash 300ms + X overlay 500ms + toast. Server validates and emits code 8 on cast attempt while silenced. Slot does not display a persistent "silenced" state between attempts — reactive feedback only. Advisory for MVP only if a persistent silenced slot icon is desired; not blocking.
*Owner: Status Effects GDD + Combat UI. Advisory (persistent icon).*

**OQ-CUI-7 — Expanded Bar / Party Frame Overlap Acceptance.**
Expanded row collides with party frames by ~44dp × 104dp on iPhone SE 3rd gen in party mode. Accepted for MVP with semi-transparent background mitigation. Formal lead sign-off required before implementation. Post-MVP: reduce visible party frames to 3 when bar is expanded.
*Owner: Lead designer. Advisory.*
