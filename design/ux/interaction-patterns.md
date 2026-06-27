# Interaction Pattern Library: Iron Grind

> **Status**: Draft
> **Author**: ux-designer
> **Last Updated**: 2026-06-27
> **Version**: 1.0
> **Engine**: Unity 6.3 LTS
> **UI Framework**: UI Toolkit (UIDocument / VisualElement / UXML / USS) per ADR-005
> **Related Documents**:
> - `design/art/art-bible.md` — visual standards (colors, typography, iconography)
> - `design/accessibility-requirements.md` — Standard tier commitments
> - `design/ux/hud.md` — HUD screen spec (references patterns throughout)
> - `docs/architecture/ADR-005-hud-ui-framework-ui-toolkit.md` — UI framework constraints
> - `docs/architecture/ADR-008-combat-ui-framework.md` — Combat UI constraints

> **Why this document exists**: Every screen spec can say "uses Skill Slot pattern"
> rather than re-specifying tap behavior, cooldown overlay, long-press threshold, and
> rejection shake from scratch. This library is the single source of truth for all
> reusable interaction behaviors across Iron Grind. Programmers implement from here;
> designers reference here before inventing new interactions.
>
> **When to update**: Add a row to the catalog index and a new pattern entry whenever
> a `/ux-design` session introduces an interaction not already listed. Never design an
> interaction without checking here first.
>
> **Status definitions**:
> - **Draft**: Specified but not yet implemented or validated
> - **Stable**: Implemented, tested, and validated in at least one shipped screen
> - **Deprecated**: Being phased out — do not use in new screens

---

## How to Use This Library

**Designing a screen**: Browse the Pattern Catalog Index before inventing new
interactions. Reference patterns by name in screen specs (e.g., "uses Quantity
Selector pattern"). If no pattern fits, propose a new entry here before writing
the screen spec.

**Implementing a screen**: When a spec says "use [PatternName] pattern," find it here
for the complete specification, Unity-specific implementation notes, and accessibility
requirements. The spec cannot override a pattern's accessibility requirements.

**Reviewing a screen spec**: All interactive elements must reference a pattern by name
or include their own full interaction specification. "Standard button" is not valid.

**Updating a pattern**: Changes to a Stable pattern affect every screen that uses it.
Audit all usages (search screen specs for the pattern name), get ux-designer approval,
and update this document simultaneously with any implementation change.

---

## Pattern Catalog Index

| Pattern Name | Category | One-Line Description | Used In | Status |
|---|---|---|---|---|
| Resource Bar | Data Display | Linear fill bar for HP, MP, XP, or charge — with numeric value and threshold states | HUD | Draft |
| Touch Toggle | Input | Binary on/off button that retains state between sessions | HUD (auto-attack, auto-run) | Draft |
| Expand/Collapse | Input | Single-tap toggle that reveals or hides a secondary content region with an animated height transition | HUD, Combat UI (skill bar) | Draft |
| Skill Slot | Combat UI | Tap-to-activate ability slot with cooldown arc overlay and long-press binding | Combat UI | Draft |
| Cooldown Arc | Combat UI | Radial arc + countdown number that depletes over a skill's cooldown duration | Combat UI | Draft |
| Long-Press Context Menu | Gesture | Hold ≥ 500ms to open a bottom-anchored option sheet; drag cancels | Combat UI (skill binding) | Draft |
| Toast Notification | Feedback | Non-blocking temporary message that auto-dismisses after a fixed duration | Combat UI, HUD (loot) | Draft |
| Shake Feedback | Feedback | Brief horizontal oscillation applied to a UI element to signal a rejected or blocked action | Combat UI (skill rejection) | Draft |
| Status Effect Icon | Data Display | Small icon displaying an active buff or debuff with optional stack count; tray scrolls when count exceeds max visible | HUD | Draft |
| Party Frame | Data Display | Compact panel showing one party member's name, role, HP fill, and status | HUD | Draft |
| Target Frame | Data Display | Enemy name + level + HP bar shown while a hostile target is selected; tap anywhere on frame to dismiss | HUD | Draft |
| Loot Countdown Notification | Feedback | Item assignment toast with a shrinking pie-timer indicating pickup expiry | HUD | Draft |
| Context-Adaptive Overlay | Feedback | A panel background that dims during active gameplay and restores on new content or tap | HUD (chat panel) | Draft |
| Safe Area Container | Layout | Root element that applies `Screen.safeArea` insets before any child layout is computed | HUD, Combat UI, all screens | Draft |
| Tabbed Panel | Navigation | Two-tab switcher within a single modal screen | NPC Shop (Buy / Sell) | Draft |
| Scrollable Item List | Layout | Vertically scrollable list with optional section headers | NPC Shop | Draft |
| Item Row | Data Display | Single row in a list or grid showing item icon, name, and a numeric value (price or sell value) | NPC Shop, Inventory (future) | Draft |
| Quantity Selector | Input | Integer stepper (− / value / +) with live total preview that updates as the value changes | NPC Shop | Draft |
| Confirm Button with Spinner | Input | CTA button that disables and shows a spinner immediately on tap, re-enables only on server response | NPC Shop, Enhancement | Draft |
| Locked Item State | Data Display | Greyed-out item slot with a lock icon — visible but non-interactive; communicates deliberate protection | NPC Shop (Sell tab) | Draft |
| Destructive Confirmation Overlay | Modal | Full-screen blocking overlay requiring explicit secondary confirm before an irreversible server action | NPC Shop (scroll purchase), Enhancement | Draft |
| Risk Warning Badge | Feedback | Colored border + warning icon applied to a UI element when a high-risk outcome probability exceeds a threshold | Enhancement | Draft |
| Outcome Animation | Feedback | Distinct VFX + label sequence communicating a binary irreversible outcome (success burst vs. destruction crack) | Enhancement | Draft |
| Persistent Chat Panel | Layout | Always-visible scrollable message history with passive read surface and dedicated compose trigger | Party Chat | Draft |
| Compose Button | Input | Dedicated 44pt button at the edge of the chat panel that opens the keyboard; the panel itself does not open the keyboard on tap | Party Chat | Draft |
| Input Field with Validation | Input | Text input that enforces byte/codepoint limits; send action disabled when field is empty or limit exceeded | Party Chat | Draft |
| Character Counter | Feedback | Live X/N counter inside or below an input field; turns red when the limit is reached or exceeded | Party Chat | Draft |
| Keyboard-Slide Layout Shift | Layout | Panel or screen region that animates upward in sync with the iOS soft keyboard animation when the keyboard raises | Party Chat | Draft |

---

## Patterns

---

### Resource Bar

**Category**: Data Display
**Used In**: HUD (HP bar, MP bar, XP bar, auto-attack charge bar)
**Status**: Draft

**Description**: A horizontal fill bar representing a bounded continuous value. Always
accompanied by a numeric or percentage readout for colorblind-safe communication.
Threshold states (critical, full) apply visual treatments beyond color alone.

**Specification**:
- Fill value is `currentValue / maxValue` clamped to [0, 1]
- Numeric label displays `floor(currentValue)` or a percentage — never hidden
- **Critical threshold** (HP only): at `currentValue / maxValue < 0.2`, apply: red
  tint, pulsing border, skull icon. The border pulse is the non-color indicator;
  the red tint is secondary
- **Full state**: no special treatment — full bars are the neutral/positive state
- Fill updates smoothly (lerp at 10 units/sec, or instantly at zone entry) — never
  snaps except on zone load
- Bar itself is non-interactive (`PickingMode.Ignore` in UI Toolkit)
- Touch target: none — this is a display element. If the bar is also a tap target
  (e.g., target frame HP bar to dismiss), the tap target is a separate transparent
  overlay element

**Accessibility**:
- Numeric value always shown — color alone never communicates the fill state
- Critical threshold uses a shape change (skull icon) in addition to color
- Minimum bar height: 8dp (legible at arm's length on iPhone SE)
- Colorblind modes: do not shift the base bar color; shift the critical-state tint

**When to Use**: Any bounded resource that the player must monitor in real time
(HP, MP, XP, charge progress).

**When NOT to Use**: Discrete counts (skill charges, stack counts) — use a numeric
label or pip display instead. Non-real-time progress (quest progress, achievement
progress) — use a progress indicator in the relevant screen, not the HUD.

---

### Touch Toggle

**Category**: Input
**Used In**: HUD (auto-attack toggle, auto-run toggle)
**Status**: Draft

**Description**: A binary on/off button that communicates its current state through
visual weight and icon change — not color alone. State persists across zone transitions
and is server-authoritative (the server does not act on the toggle; the client uses
it to decide whether to send inputs).

**Specification**:
- Two visual states: **Active** (filled background, solid icon) and **Inactive**
  (outline background, desaturated icon)
- On tap: state flips immediately (optimistic local update); no network round-trip
- Tap target: minimum 44×44dp
- No hold behavior — this is a single tap action
- Icon must communicate state without relying on color alone: use filled vs. outline
  icon variant (e.g., filled sword = auto-attack on; outline sword = off)
- State is read by the client each tick; it does not disable the underlying system —
  it stops the client from sending inputs to that system

**Accessibility**:
- Accessible name must be dynamic: "Auto-attack: On" / "Auto-attack: Off"
- State change produces a brief tactile response (iOS haptic feedback, if enabled)
- Tap target is 44×44dp regardless of visual icon size

**When to Use**: Persistent on/off preferences that the player switches infrequently
during active play.

**When NOT to Use**: Actions with a discrete outcome per press (casting a skill,
buying an item). Those use Confirm Button or Skill Slot.

---

### Expand/Collapse

**Category**: Input
**Used In**: HUD, Combat UI (skill bar expand/collapse via +/− button)
**Status**: Draft

**Description**: A single-tap toggle that reveals or hides a secondary content region
using an animated height transition. The primary content region does not move. The
toggle button icon updates to reflect the current state (+/−).

**Specification**:
- On tap: the secondary region transitions from `height: 0` to its full height (or
  vice versa) using USS `transition: height 200ms ease-in-out`
- The primary region is anchored — it must not shift on expand or collapse
- Toggle icon: `+` when collapsed (secondary hidden), `−` when expanded (secondary
  visible)
- Tap target: minimum 44×44dp on the toggle button
- State persistence: the expanded/collapsed state is server-persisted (1-bit flag in
  `SkillBarLayout`); restored on zone entry
- No drag gesture — expand/collapse is tap-only; a drag on the toggle button is
  treated as a scroll, not an expand action

**Accessibility**:
- Accessible role: toggle button
- Accessible state: "expanded" / "collapsed"
- Transition respects Reduce Motion: if enabled, skip the height animation (instant
  show/hide)

**When to Use**: Secondary content that is relevant only some of the time and would
clutter the primary layout if always visible (skill bar second row, filter panels).

**When NOT to Use**: Content that changes the underlying data or performs an action —
that is a Confirm Button, not an expand toggle. Do not use for navigation (tabs belong
to Tabbed Panel).

---

### Skill Slot

**Category**: Combat UI
**Used In**: Combat UI (S1–S8)
**Status**: Draft

**Description**: A 52×52dp touch target that activates an ability on tap. Displays
the bound skill's icon, a cooldown arc overlay when the skill is on cooldown, and a
lock/disabled mask when the skill cannot be cast. Long-press (≥ 500ms without drag)
opens the binding context menu.

**Specification**:
- **Visual states**:
  - *Ready*: full-opacity icon, no overlay, interactive
  - *Cooldown*: icon dimmed to 50%, cooldown arc overlay active, countdown number visible, non-interactive
  - *Disabled (no mana)*: icon dimmed, MP-insufficient indicator (see Shake Feedback), immediately interactive again after feedback
  - *No skill bound*: empty slot background, no icon, non-interactive for casting but interactive for binding
  - *Locked (class gate)*: greyed icon, lock icon overlay, non-interactive
- **Tap behavior**: client pre-checks before sending `SkillCastRequest` — if cooldown active or no target selected, do not send; show appropriate feedback (Shake or Toast)
- **Long-press threshold**: 500ms. If finger moves ≥ 8dp before 500ms elapses, cancel long-press (treat as scroll/drag)
- **Slot size**: 52×52dp visual; 56×56dp tap target (4dp invisible extension on all sides)
- `PickingMode.Position` on the slot root; `PickingMode.Ignore` on all overlay elements (arc, lock icon, countdown)

**Accessibility**:
- Accessible name: "[Skill Name] — Ready" / "[Skill Name] — Cooldown: [N]s" / "Empty slot"
- Accessible role: button
- Slot tap target meets 44pt minimum (56dp tap area)

**When to Use**: Any activatable ability mapped to a persistent slot.

**When NOT to Use**: Inventory items, consumables (use Item Row or a dedicated consumable pattern), or settings toggles (use Touch Toggle).

---

### Cooldown Arc

**Category**: Combat UI
**Used In**: Combat UI (overlaid on Skill Slot)
**Status**: Draft

**Description**: A radial arc rendered via Painter2D `generateVisualContent` callback
that depletes clockwise as a skill's cooldown elapses. A numeric countdown (whole
seconds) displays in the slot center. The arc interpolates at 60fps using
`Time.time`-based math — it does not step at the server tick rate.

**Specification**:
- **Arc origin**: `cooldownExpiryTick` (absolute server tick from `SkillCooldownUpdate`
  or `SkillCooldownSnapshot`)
- **Local time interpolation**: `fractionRemaining = (expiryTick − ServerTime.Tick) / totalCooldownTicks`, clamped [0, 1]; updated every frame via `Time.deltaTime` accumulation
- **Arc sweep**: starts at 12 o'clock, sweeps clockwise. Full arc (1.0) = skill just cast; empty arc (0.0) = skill ready
- **Countdown number**: `ceil(secondsRemaining)` displayed in slot center; hidden when `secondsRemaining < 0.5`
- **Arc disappearance**: arc is hidden and slot becomes interactive when `fractionRemaining ≤ 0`
- **Cold-start**: on zone entry before `SkillCooldownSnapshot` arrives, all slots show Ready. Snapshot arrives ~1–2s later; slots update to correct state

**Accessibility**:
- Non-color arc indicator: arc shape + countdown number together communicate cooldown state without color
- Arc tint must not be the sole differentiator — the number is the primary signal for colorblind players

**When to Use**: Only on Skill Slot elements that have a server-authoritative cooldown.

**When NOT to Use**: Client-only timers (loot countdown uses Loot Countdown Notification pattern instead). Non-skill timers.

**Implementation Note**: Painter2D arcs must be validated by the performance analyst
for ≤ 0.3ms at 8 simultaneous arcs on iPhone SE 3rd gen before the Combat UI
implementation sprint begins (CR-CUI-19 gate).

---

### Long-Press Context Menu

**Category**: Gesture / Input
**Used In**: Combat UI (skill slot binding)
**Status**: Draft

**Description**: A bottom-anchored option sheet that opens after the player holds a
target element for ≥ 500ms without moving the finger. If the finger travels ≥ 8dp
before the threshold elapses, the long-press is cancelled and no menu opens. Each
option in the sheet is a full-width tap target (minimum 44dp tall).

**Specification**:
- **Trigger**: pointer-down held ≥ 500ms with ≤ 8dp finger travel
- **Cancel condition**: pointer moves ≥ 8dp at any point before 500ms → no menu, treat as scroll or drag
- **Appearance**: sheet slides up from screen bottom (200ms ease-out); backdrop dims to 40% opacity
- **Dismiss**: tap outside the sheet, tap a backdrop, or tap a cancel option
- **Option height**: minimum 56dp per row (provides 44pt tap target with padding)
- **Option count**: no maximum, but sheets taller than 60% screen height should scroll internally
- **Accessibility**: each option must have an accessible name; the sheet itself must be announced as "context menu, [N] options"
- **No nested menus**: this pattern is always flat; hierarchical menus are out of scope

**When to Use**: Secondary or configuration actions triggered from an element that has a primary tap action (skill slot cast → long-press to bind).

**When NOT to Use**: Destructive actions that require confirmation — those belong in Destructive Confirmation Overlay. Navigation actions — those belong in explicit buttons.

---

### Toast Notification

**Category**: Feedback
**Used In**: Combat UI (skill rejection), HUD (loot assignment — see Loot Countdown Notification for the timer variant)
**Status**: Draft

**Description**: A non-blocking text message that appears near the triggering element
or at a fixed screen position and auto-dismisses after 2–3 seconds. The player does
not need to interact with it. Multiple toasts stack vertically; oldest dismiss first.

**Specification**:
- **Duration**: 2s default; 3s for messages requiring more reading time (> 20 characters)
- **Position**: near the element that triggered the toast, or fixed bottom-center for system messages
- **Stack behavior**: new toasts appear above existing ones; maximum 3 simultaneous toasts; oldest toast is dismissed early if a 4th arrives
- **Dismiss**: auto-dismiss only; no tap-to-dismiss (tap passes through to the game world)
- **Animation**: fade-in 150ms, hold, fade-out 200ms; no slide (Reduce Motion compatibility)
- **Content**: short label (≤ 32 characters); no icons required but supported

**Accessibility**:
- Duration minimum 2s (per Standard tier — no auto-dismiss in under 2s)
- Text must meet 4.5:1 contrast against the toast background
- Screen reader should announce the toast content on appearance

**When to Use**: Ephemeral feedback for rejections, confirmations, or status changes that do not require player action.

**When NOT to Use**: Outcomes that require player acknowledgement (use Destructive Confirmation Overlay or a modal). Timers or countdowns (use Loot Countdown Notification). Persistent status (use Status Effect Icon or a HUD element).

---

### Shake Feedback

**Category**: Feedback
**Used In**: Combat UI (skill slot — blocked tap feedback)
**Status**: Draft

**Description**: A brief horizontal oscillation applied to a UI element to signal that
an action was attempted but rejected. Does not open any menu or require player input.
Communicates "this didn't work" without interrupting flow.

**Specification**:
- **Duration**: 300ms total, 3 oscillation cycles (50ms left, 50ms right, 50ms left, 50ms right, return)
- **Amplitude**: 6dp horizontal displacement
- **Trigger**: immediately on tap-down rejection (no network round-trip needed — client pre-check)
- **Concurrent shakes**: only the tapped element shakes; other elements are unaffected
- **Easing**: ease-in-out for natural feel

**Accessibility**:
- Reduce Motion: skip the shake; substitute a brief (200ms) red border flash instead
- The rejection must also be communicated via a Toast Notification — shake alone is not sufficient for screen readers

**When to Use**: Rejected or blocked tap actions where the element stays in place and the player should understand "try something else."

**When NOT to Use**: Destructive actions, network errors, or any situation where the player needs to read a message. Combine with Toast Notification for those cases.

---

### Status Effect Icon

**Category**: Data Display
**Used In**: HUD (buff/debuff tray)
**Status**: Draft

**Description**: A small icon (32×32dp) representing an active status effect, with an
optional stack count badge. Effects display in a horizontal tray; when the active count
exceeds the tray's visible capacity (6), scroll arrows appear.

**Specification**:
- **Icon size**: 32×32dp visual; non-interactive by default (PickingMode.Ignore)
- **Stack count badge**: displayed bottom-right of icon when stacks > 1; minimum 14pt font
- **Tray capacity**: 6 visible slots; scroll arrows appear at position 1 and 6 when count > 6; scroll arrows are 44×44dp tap targets
- **Color coding**: buff icons use a warm tint; debuff icons use a cool tint — but the icon shape is the primary differentiator (colorblind safety)
- **Duration indicator**: optional; if shown, use a Resource Bar arc or underline — not a countdown number (too small at 32dp)
- **Update trigger**: `OnBuffApplied` / `OnBuffExpired` events from server

**Accessibility**:
- Long-press on icon (500ms): opens a Toast Notification with the full effect name and remaining duration
- Color tint is secondary — icon shape must differ between buffs and debuffs

**When to Use**: Active status effects with a defined duration, managed by the Status Effects system.

**When NOT to Use**: Permanent character attributes (those belong in a character sheet). Cooldowns (use Cooldown Arc on the Skill Slot).

---

### Party Frame

**Category**: Data Display
**Used In**: HUD (right edge, up to 4 members)
**Status**: Draft

**Description**: A compact panel showing one party member's name, role icon, HP fill
bar, and member status (online, offline, dead). Frames are stacked vertically. The
full panel is hidden when `IsSoloParty = true`.

**Specification**:
- **Frame dimensions**: full width ≈ 120dp; height ≈ 48dp per member
- **Contents**: role icon (16×16dp), member name (truncated at 80dp), HP bar (Resource Bar pattern, scaled), MemberStatus indicator
- **MemberStatus states**: Online (neutral), Offline (grey overlay), Dead (skull icon overlay), Out-of-range (faded)
- **Non-interactive**: entire frame is display-only (PickingMode.Ignore). No tap-to-target from party frame in MVP.
- **Update trigger**: `PartyMemberStatusChanged` events from server
- **Max count**: 4 frames (party cap is 5; player's own frame is in the resource cluster)

**Accessibility**:
- HP bar uses numeric value (Resource Bar pattern applies)
- Status must be communicated by icon shape, not color alone

**When to Use**: Always-on party health monitoring during active sessions.

**When NOT to Use**: Large party displays or roster management — those belong in a dedicated party management screen.

---

### Target Frame

**Category**: Data Display
**Used In**: HUD (contextual — shown only when a hostile target is selected)
**Status**: Draft

**Description**: A panel showing the targeted enemy's name, level, and HP bar. Appears
when the player taps an enemy; dismisses when the player taps the frame itself, changes
target, or the enemy dies. The entire frame is a dismiss tap target.

**Specification**:
- **Trigger**: player taps a valid hostile entity in the game world → server confirms target selection
- **Contents**: enemy name (string), enemy level (int), enemy HP bar (Resource Bar pattern)
- **Dismiss**: tap anywhere on the frame; or enemy dies; or player taps a new target (frame updates to new target, does not hide)
- **Position**: upper-left quadrant (low-frequency tap zone per thumb-reach rules)
- **Tap target for dismiss**: the entire frame area (minimum 44dp tall)
- **HP update**: driven by `EntityHealthUpdate` from the server networking relevance layer

**Accessibility**:
- HP numeric value shown (Resource Bar accessibility rules apply)
- Dismiss tap is the full frame area — no small close button required

**When to Use**: While a hostile target is selected during combat.

**When NOT to Use**: Friendly targets (party members use Party Frame). Non-combat interactables (NPCs open interaction modals, not target frames).

---

### Loot Countdown Notification

**Category**: Feedback
**Used In**: HUD (loot assignment events)
**Status**: Draft

**Description**: A Toast Notification variant with an embedded pie-timer countdown
indicating the player's remaining time to pick up an assigned ground item. Multiple
notifications stack vertically. Each auto-dismisses when its timer expires or the
item is collected.

**Specification**:
- **Trigger**: `GroundItemAssigned` event from Zone Instancing
- **Contents**: item icon (24×24dp), item name (string), shrinking pie-timer fill (circular, clockwise)
- **Duration**: driven by the item's server-side assignment duration; the pie timer reflects this
- **Stack**: multiple simultaneous notifications stack above each other (newest on top); maximum 3 visible
- **Dismiss**: auto-dismiss on timer expiry or on `GroundItemCollected` event; no tap-to-dismiss
- **Position**: same fixed position as Toast Notifications (bottom-center or designated HUD zone)

**Accessibility**:
- Item name is always text — icon alone is insufficient
- Timer is the pie fill; no countdown number required (the fill is visible enough at the notification size)

**When to Use**: Ground item loot assignments only.

**When NOT to Use**: General notifications (use Toast Notification). Timer-based cooldowns (use Cooldown Arc).

---

### Context-Adaptive Overlay

**Category**: Feedback / Layout
**Used In**: HUD (chat panel background during combat)
**Status**: Draft

**Description**: A panel background that reduces opacity automatically during active
gameplay to reduce visual noise, then restores when new content arrives or the player
taps the panel. The text layer always remains at full opacity — only the background dims.

**Specification**:
- **Active state background opacity**: 70%
- **Dimmed state background opacity**: 30% (reached after 4s of no new events during active combat)
- **Restore trigger**: new content event (e.g., `PartyChatMessage`) OR player tap on the panel reading surface
- **Text layer**: always 100% opacity (WCAG AA contrast maintained against dimmed background)
- **Transition**: 300ms linear fade between states
- **Combat detection**: client-side — "active combat" = local player's auto-attack cycle timer is running

**Accessibility**:
- Text must remain at 4.5:1 contrast at the minimum background opacity (30%)
- Reduce Motion: skip the opacity transition (instant switch between 30% and 70%)

**When to Use**: Panels that must remain visible during gameplay but should not compete with world-space visuals.

**When NOT to Use**: Modals, full-screen overlays, or any element where reduced visibility creates confusion about availability.

---

### Safe Area Container

**Category**: Layout
**Used In**: All screens (HUD, Combat UI, all future screens)
**Status**: Draft

**Description**: The root layout element of any screen that applies `Screen.safeArea`
insets before any child element is positioned. Ensures no interactive element is
obscured by the iPhone notch, Dynamic Island, or home indicator in landscape mode.

**Specification**:
- Applied in `Awake()` and recalculated in `OnRectTransformDimensionsChange()`
- Uses `RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height))` to convert pixel insets to panel space (ADR-005 ApplySafeArea method)
- No pixel offsets are hardcoded — all insets come from `Screen.safeArea` at runtime
- All child positioning is relative to the safe area container, not the raw screen rect
- Must be applied before any child layout is computed

**Accessibility**:
- Non-negotiable for iOS — failure to apply safe area insets means tap targets may land under the notch, making them permanently unreachable

**When to Use**: Every screen. Every screen root element is a Safe Area Container.

**When NOT to Use**: Never skip. Even a debug screen needs safe area compliance.

---

### Tabbed Panel

**Category**: Navigation
**Used In**: NPC Shop (Buy tab / Sell tab)
**Status**: Draft

**Description**: A two-tab navigation element at the top of a modal screen that
switches the content area between two distinct views without closing the modal.
The selected tab has higher visual weight (filled background, bold label). Each tab
is a 44dp-tall tap target.

**Specification**:
- **Tab count at MVP**: 2 (Buy / Sell). Extend to more if future screens require, but validate layout at each count.
- **Tab height**: 44dp minimum (tap target)
- **Active indicator**: filled background or underline on the active tab; not color-only (active tab may also use bold text weight or uppercase label)
- **Content area**: full-width region below the tab bar; switches content on tap without animation (or with a 150ms cross-fade)
- **State persistence**: tab selection is not persisted between sessions (defaults to first tab on open)
- **Tab labels**: short (1–2 words); do not truncate

**Accessibility**:
- Active tab accessible state: "selected"
- Inactive tabs accessible state: "not selected"
- Tab bar accessible role: tab list

**When to Use**: Modal screens with two to four distinct views sharing context (same data set, different operations).

**When NOT to Use**: Top-level navigation (use a navigation bar or bottom tab bar). More than 4 tabs (use a scrollable content approach instead). Screens where the two views are completely unrelated.

---

### Scrollable Item List

**Category**: Layout
**Used In**: NPC Shop (Buy tab item list)
**Status**: Draft

**Description**: A vertically scrollable list of Item Row elements, optionally
organized with sticky section header labels. Momentum scrolling (iOS native feel).
No horizontal scrolling.

**Specification**:
- **Scroll direction**: vertical only
- **Section headers**: sticky (remain visible at the top of the scroll area as the user scrolls past items in that section); section header height: 28dp; text: 12pt, semi-bold
- **Item row height**: 56dp minimum (allows for icon + two text lines + padding)
- **Overscroll**: iOS rubber-band bounce on overscroll edges
- **Empty state**: if the list has no items, show a centered empty-state label ("No items available") — never show an empty scroll area
- **Loading state**: show skeleton placeholder rows (3–5) while data loads; replace with real rows when data arrives

**Accessibility**:
- Each list item is a single, named accessible element
- Screen reader reads: "[Item name], [price] gold" per row

**When to Use**: Lists of items where the count is variable and may exceed the visible screen area.

**When NOT to Use**: Short fixed-length lists (fewer than 5 items that always fit on screen — use a static layout instead). Grid layouts (use a 2-column grid variant for square items like an inventory).

---

### Item Row

**Category**: Data Display / Input
**Used In**: NPC Shop (Buy tab, Sell tab)
**Status**: Draft

**Description**: A single row or card showing an item's icon, name, and a numeric
value (buy price or sell price). Tapping the row selects it; a selected row has a
visible selection indicator (border or background tint). The row is the tap target —
no separate select button.

**Specification**:
- **Row height**: 56dp minimum
- **Contents**: item icon (32×32dp), item name (16pt), secondary info (price, stack count — 14pt, subdued color)
- **Selection state**: selected row shows a 2dp colored border or a 10% background tint; the border is the non-color indicator
- **Rarity indicator**: item rarity shown as a colored border on the icon + a rarity label badge (not color-only — see accessibility-requirements.md color audit)
- **Disabled state** (locked items): greyed-out row with lock icon; tap produces no selection; uses Locked Item State pattern

**Accessibility**:
- Accessible name: "[Item Name], [rarity], [price]"
- Selected state is announced on selection

**When to Use**: Any selectable item in a list context.

**When NOT to Use**: Grid cells (use a square variant). Non-selectable display items (omit tap handler and selection state).

---

### Quantity Selector

**Category**: Input
**Used In**: NPC Shop (Buy quantity, Sell quantity)
**Status**: Draft

**Description**: An integer stepper control (− / value / +) that lets the player
choose a quantity within a defined range. A live total preview (quantity × unit price)
updates with every step. The confirm action is always a separate Confirm Button with
Spinner — the quantity selector never triggers a server action on its own.

**Specification**:
- **Components**: decrement button (−), numeric value display, increment button (+)
- **Button size**: minimum 44×44dp each
- **Range**: [1, selectorMax] per game rules (F-NS-4 in npc-shop.md)
- **Default value**: 1 (Buy); full stack count (Sell)
- **Boundary behavior**: decrement button disables at minimum (1); increment button disables at maximum
- **Live preview**: total cost/yield label updates on every step without network request; formula: `quantity × unitPrice`
- **Disabled state**: entire selector dims to 40% opacity and all buttons are non-interactive when `selectorMax = 0` (e.g., cannot afford even 1 unit)

**Accessibility**:
- Accessible name for buttons: "Decrease quantity" / "Increase quantity"
- Value display accessible label: "[N] of [max]"
- No swipe gesture — only explicit button taps

**When to Use**: Any context where the player must select a numeric quantity before confirming a transaction.

**When NOT to Use**: Continuous values (use a Slider). Non-transactional counters. Quantities with no defined maximum (open-ended inputs use Input Field with Validation).

---

### Confirm Button with Spinner

**Category**: Input
**Used In**: NPC Shop (Confirm Buy, Confirm Sell), Enhancement (Confirm Attempt)
**Status**: Draft

**Description**: A primary call-to-action button that disables and replaces its label
with a spinner immediately on tap, before the server responds. Re-enables only when
the server response arrives. Prevents double-submission without any client-side lock
management.

**Specification**:
- **Default state**: enabled CTA button (high visual weight per primary button conventions)
- **On tap**: immediately disable, replace label text with a centered spinner; send network request
- **Re-enable trigger**: `BuyResult` / `SellResult` / `EnhancementAttemptResult` arrives (success or error)
- **Timeout**: if no server response after 10s, re-enable the button and show a Toast Notification ("Connection issue — try again")
- **Disabled condition** (pre-tap): the button must be pre-disabled when the action cannot be taken (e.g., `selectorMax = 0`, no item selected, item is locked). Pre-disabled state: 40% opacity, no spinner.
- **Do not** optimistically update any displayed balance or inventory — wait for the server response

**Accessibility**:
- While spinner is active: accessible label changes to "Processing…" or "[Action]: in progress"
- Re-enable restores the original accessible label

**When to Use**: Any action that sends a network request and waits for a server commit before the player can re-attempt.

**When NOT to Use**: Instant local-only actions (use Touch Toggle or Expand/Collapse). Actions that require secondary confirmation (wrap in Destructive Confirmation Overlay first; the inner confirm button then uses this pattern).

---

### Locked Item State

**Category**: Data Display
**Used In**: NPC Shop (Sell tab — locked equipment slots)
**Status**: Draft

**Description**: An Item Row displayed in a greyed-out visual state with a lock icon
overlay, communicating that the item is visible but currently cannot be interacted
with due to a deliberate player protection (e.g., item is locked to prevent accidental
sale). Tap produces no selection or action.

**Specification**:
- **Visual treatment**: item icon and text at 40% opacity; lock icon (16×16dp) in the bottom-right corner of the item icon
- **Tap behavior**: no selection highlight; no action; optionally a Toast Notification explaining the lock ("Item is locked — unlock in inventory to sell")
- **Lock icon**: must use a shape icon (padlock silhouette), not color alone, to communicate the locked state
- **Unlock path**: defined by the inventory system; the NPC Shop does not provide an unlock flow — it only shows the state

**Accessibility**:
- Accessible label: "[Item Name] — locked. Cannot sell while locked."
- Dimmed state is not sufficient for screen readers — the label must explicitly state "locked"

**When to Use**: Any item that the player has deliberately locked and that should remain visible (not hidden) to avoid false impressions of item loss.

**When NOT to Use**: Items that are unavailable for a different reason (out of stock, level-locked, insufficient currency). Those use a different disabled state treatment appropriate to the context.

---

### Destructive Confirmation Overlay

**Category**: Modal
**Used In**: NPC Shop (scroll purchase confirmation before BuyRequest), Enhancement (attempt confirmation)
**Status**: Draft

**Description**: A full-screen blocking overlay that presents a summary of the
irreversible action the player is about to take, and requires an explicit secondary
tap to proceed. Dismissing the overlay (via Cancel) returns the player to the previous
state with no action taken. No action is sent to the server until the player confirms
in this overlay.

**Specification**:
- **Trigger**: player taps the primary Confirm Button for a destructive or high-stakes action
- **Layout**: semi-transparent backdrop (60% black), centered card; card contains: action summary, key data (item name, cost, risk probabilities), Cancel button, Confirm button
- **Cancel**: dismisses overlay with no action; accessible as "Cancel" and via the back gesture
- **Confirm**: sends the network request using Confirm Button with Spinner pattern (the confirm inside this overlay is itself a Confirm Button with Spinner)
- **Backdrop tap**: dismisses as Cancel
- **Animation**: card fades and scales in (200ms); reverses on dismiss
- **No timeout**: this overlay does not auto-dismiss. Player must make an explicit choice.
- **Risk data**: when applicable (Enhancement), display `P_s` (success %) and `P_d` (destruction %) prominently. If `P_d ≥ 0.35`, apply Risk Warning Badge pattern to the destruction probability display.

**Accessibility**:
- When overlay opens, focus moves to the first interactive element (Cancel button)
- Overlay must be announced: "Confirmation required: [action summary]"
- Cancel and Confirm must both be reachable without dismissing the overlay

**When to Use**: Any action that is irreversible from the player's perspective (gold spent, item consumed, item potentially destroyed).

**When NOT to Use**: Reversible actions or low-stakes confirmations. Do not wrap routine actions in this overlay — it loses meaning through overuse.

---

### Risk Warning Badge

**Category**: Feedback
**Used In**: Enhancement (when destruction probability P_d ≥ 0.35)
**Status**: Draft

**Description**: A visual treatment applied to a data display element when a numeric
risk value exceeds a design-defined threshold. Communicates elevated risk beyond the
baseline through a colored border, a warning icon, and an explicit label — not color
alone.

**Specification**:
- **Trigger threshold**: `P_d ≥ 0.35` (destruction probability at +4 and above)
- **Visual treatment**:
  - 2dp warning-colored border applied to the affected element
  - Warning icon (⚠ or equivalent) placed adjacent to the risk percentage text
  - Optional: bold text weight on the risk percentage itself
- **Non-color indicator**: the warning icon is the primary signal; the border color is secondary
- **Removal**: badge disappears when risk falls below threshold (e.g., player changes item selection)
- **No animation**: badge appears/disappears instantly — no transition (it represents a persistent state, not a momentary event)

**Accessibility**:
- Warning icon must have an accessible label: "High destruction risk"
- Border color must differ from the border used for selection state (do not reuse selection border style for risk)

**When to Use**: Any context where a real-time numeric risk value exceeds a threshold the player should notice before confirming.

**When NOT to Use**: Static risk displays (items always have the same risk at a given level — the badge is relevant only when the player has selected a specific combination). Non-risk errors (use Toast Notification or Shake Feedback instead).

---

### Outcome Animation

**Category**: Feedback
**Used In**: Enhancement (SUCCESS or DESTRUCTION result)
**Status**: Draft

**Description**: A distinct VFX sequence + text label that plays immediately after a
binary irreversible outcome is committed by the server. Success and failure outcomes
use visually opposite animations so the result is unambiguous even before the player
reads the label.

**Specification**:
- **Trigger**: `EnhancementAttemptResult` received with `outcome: SUCCESS` or `outcome: DESTRUCTION`
- **Success sequence**: burst particle effect (upward, warm colors) + "Success!" label (green tint; supplemented by upward motion cue for colorblind)
- **Destruction sequence**: crack particle effect (item shatters downward, cool/grey) + "Destroyed" label (red tint; supplemented by downward motion cue for colorblind)
- **Duration**: 1.5–2.0s; non-skippable (server result is committed; player must see the outcome)
- **Label**: text label is always shown alongside the VFX — VFX alone is insufficient
- **Post-animation**: inventory and stats update to reflect the committed outcome (no further server request needed — the result message carries the new state)

**Accessibility**:
- Success vs. destruction are differentiated by animation direction (up vs. down) in addition to color
- Label text meets 4.5:1 contrast against the overlay background
- Reduce Motion: replace particle VFX with a full-opacity label flash (200ms pulse); direction cue via text position (success label moves up slightly, destruction label down)

**When to Use**: Binary irreversible outcomes with emotional weight. Do not overuse — this pattern commands player attention.

**When NOT to Use**: Routine transaction results (NPC shop Buy/Sell confirmations use Toast Notification instead — the outcome is expected and low-stakes).

---

### Persistent Chat Panel

**Category**: Layout
**Used In**: HUD (party chat, visible during active play when in a party)
**Status**: Draft

**Description**: An always-visible scrollable message history panel positioned in the
HUD. The reading surface is passive — tapping it does not open the keyboard. A
dedicated Compose Button is the only keyboard trigger. The panel opacity adapts during
combat (see Context-Adaptive Overlay pattern).

**Specification**:
- **Position**: bottom-center of safe area, between the joystick zone (left) and Combat UI skill buttons (right)
- **Dimensions**: defined by HUD GDD (CR-HUD-16); must not overlap the virtual joystick activation area
- **Message display**: last `CHAT_HISTORY_DISPLAY_COUNT` messages; each line: `SenderName` + ": " + message text; long messages wrap within panel width (no horizontal scroll)
- **Auto-scroll**: new message arrival auto-scrolls to bottom; suppressed if the player has scrolled up (restored when player scrolls to bottom or sends a message)
- **Reading surface tap**: does not open keyboard; restores combat opacity if dimmed (Context-Adaptive Overlay)
- **Combat opacity**: see Context-Adaptive Overlay pattern — background dims 70% → 30% after 4s of no new messages during combat
- **Minimum font size**: 12pt
- **Safe area**: left and bottom edges anchored to `Screen.safeArea` bounds

**Accessibility**:
- Text must maintain 4.5:1 contrast at minimum (30%) background opacity
- Scroll position is communicated by standard scroll indicator

**When to Use**: In-session party communication where the player must be able to read messages without leaving the gameplay context.

**When NOT to Use**: Asynchronous messaging (friend chat, mail) — those belong in dedicated screens, not the HUD.

---

### Compose Button

**Category**: Input
**Used In**: HUD (party chat panel)
**Status**: Draft

**Description**: A small dedicated button at the edge of the chat panel that is the
sole trigger for opening the soft keyboard. The chat reading surface itself never
opens the keyboard on tap — this prevents accidental keyboard open during combat input.

**Specification**:
- **Position**: top-right corner of the chat panel (furthest from the joystick dead zone)
- **Size**: minimum 44×44dp tap target; visual icon may be smaller (e.g., 24×24dp pencil icon)
- **Behavior on tap**: activates the Input Field (see Input Field with Validation), raises the iOS soft keyboard
- **Visibility**: always visible while the chat panel is shown (never hidden on inactivity)
- **Icon**: pencil or compose icon — must not be confused with a close button

**Accessibility**:
- Accessible name: "Compose message"
- Must be reachable in logical tab order after the chat reading area

**When to Use**: Any read-heavy panel that must prevent accidental keyboard activation during active gameplay.

**When NOT to Use**: Screens where keyboard activation on field tap is expected (standard form inputs). The Compose Button is a specialized safeguard for gameplay-adjacent panels.

---

### Input Field with Validation

**Category**: Input
**Used In**: HUD (party chat input when compose mode is active)
**Status**: Draft

**Description**: A single-line text input field that enforces dual limits (codepoint
count and UTF-8 byte count independently). The send action is disabled while the field
is empty or either limit is exceeded. The Character Counter pattern is displayed inline.

**Specification**:
- **Codepoint limit**: `PARTY_CHAT_MAX_CODEPOINTS` (128 at MVP)
- **Byte limit**: `PARTY_CHAT_MAX_BYTES` (defined in party-chat.md)
- **Send button**: enabled only when `fieldText.Length > 0 AND UTF8ByteCount ≤ MAX_BYTES AND codepointCount ≤ MAX_CODEPOINTS`
- **Send trigger**: Send button tap OR keyboard Return key
- **Post-send**: field clears; keyboard remains open (supports rapid follow-up messages)
- **Keyboard dismiss**: player dismisses explicitly — back gesture, tap outside field, or a cancel button; do NOT auto-dismiss keyboard after send
- **Validation enforcement**: both limits enforced character-by-character as the player types; input is blocked (not accepted) when both limits are simultaneously hit

**Accessibility**:
- Accessible role: text field
- Field accessible label: "Chat message input"
- Character counter (see Character Counter pattern) provides live character count for screen readers

**When to Use**: Any text input with enforced length limits where both character and byte counts matter independently.

**When NOT to Use**: Numeric-only inputs (use Quantity Selector). Free-form inputs with no length constraint (remove the Character Counter).

---

### Character Counter

**Category**: Feedback
**Used In**: HUD (party chat input field)
**Status**: Draft

**Description**: A live counter displayed inside or below an input field showing the
current codepoint count against the maximum. Turns red and send is disabled when the
limit is reached or exceeded.

**Specification**:
- **Format**: `[current]/[max]` — e.g., "0/128" → "64/128" → "128/128"
- **Position**: bottom-right corner of the input field, or immediately below it
- **Normal state**: subdued color (60% opacity), 12pt font
- **At-limit state**: full opacity, red tint, bold weight; send button disables simultaneously
- **Over-limit state** (byte limit exceeded before codepoint limit): same at-limit treatment; send button disables
- **Counter updates**: on every keystroke

**Accessibility**:
- Screen reader should announce the count at intervals (every 10 characters) and when the limit is reached
- At-limit state accessible announcement: "Character limit reached — [N] of [max] characters"

**When to Use**: Any input field with a character or byte limit where the player should be aware of how much space remains.

**When NOT to Use**: Short inputs with generous limits where the count is not a meaningful concern (password fields, search bars with a 256-char limit).

---

### Keyboard-Slide Layout Shift

**Category**: Layout
**Used In**: HUD (party chat panel when keyboard is raised)
**Status**: Draft

**Description**: A panel or layout region that animates upward in sync with the iOS
soft keyboard slide-in animation when the keyboard raises, ensuring the input field
and relevant content remain visible above the keyboard. Reverses when the keyboard
dismisses.

**Specification**:
- **Shift trigger**: iOS keyboard `UIKeyboardWillShowNotification` (or equivalent Unity keyboard event)
- **Shift amount**: panel bottom edge anchored 10dp above keyboard top edge
- **Animation timing**: matches the iOS keyboard animation duration and curve exactly (system-provided values — do not hardcode a duration)
- **Shift constraint**: the "must not overlap joystick" layout constraint is suspended while the keyboard is active; the player is not using combat controls while typing
- **Restore**: keyboard dismiss → panel returns to default position with the same animation (matches keyboard slide-out)
- **No static offset**: offset is computed at runtime from the keyboard frame, not hardcoded

**Accessibility**:
- Reduce Motion: shift is instant (0ms) rather than animated
- The input field must remain fully above the keyboard at all times — partial obstruction is not acceptable

**When to Use**: Any panel with an active text input field that the player uses while the game world is still active (i.e., the input does not full-screen / does not push all game UI off screen).

**When NOT to Use**: Dedicated full-screen input screens (character naming, search) where it is appropriate for the keyboard to overlay a minimal or empty background.

---

## Gaps & Patterns Needed

The following patterns are referenced in GDDs or the HUD spec but not yet formally
defined. Add them as new screens are designed.

| Pattern Needed | Needed By | Notes |
|---|---|---|
| **Virtual Joystick** | Movement System, HUD GDD | Touch-based directional input for player movement; needs dead zone spec, visual appearance, and thumb-reach zone definition |
| **Enemy Tap-to-Target** | Combat, Enemy AI | Tap on a world-space enemy entity to select it as the target; needs raycasting-to-UI event routing pattern |
| **Inventory Grid** | Inventory System (not yet in UX) | 4×5 slot grid; tappable cells; drag-to-swap? Needed before inventory UX spec |
| **Equipment Slot Display** | Equipment System (not yet in UX) | Named equipment slots (weapon, armor, etc.) with icon + stat delta preview |
| **Loading Screen** | Zone Instancing | Transition between zones; progress indicator; Iron Grind visual branding |
| **Login / Auth Screen** | Authentication | Credential input + submit; error states for invalid credentials |
| **Character Select** | Authentication / Zone Instancing | Character roster display; create new character CTA |
| **Map / Minimap** | Map/Minimap GDD (not yet authored) | In-game minimap widget; tap to expand to full map |

---

## Open Questions

| Question | Owner | Deadline |
|---|---|---|
| Does the Virtual Joystick need a fixed-position ring or a dynamic-spawn model (spawns wherever the player first touches the left zone)? | game-designer / ux-designer | Before Movement System implementation sprint |
| Should Enemy Tap-to-Target use a selection ring in the game world, a highlight effect, or both? | art-director / ux-designer | Before combat implementation sprint |
| Inventory Grid: drag-to-swap, or tap-to-select + tap-destination to swap? | ux-designer | Before inventory UX spec |
| Should Toast Notifications dismiss on tap, or always auto-dismiss? HUD GDD says no tap-to-dismiss, but future screens may differ. | ux-designer | Before first non-HUD screen spec |
