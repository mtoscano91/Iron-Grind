# HUD Design

> **Status**: Complete
> **Author**: Manuel Toscano + Claude Code (ux-designer)
> **Last Updated**: 2026-06-20
> **Template**: HUD Design
> **GDD Source**: design/gdd/hud.md (Approved 2026-06-20)
> **UI Framework**: UI Toolkit — UIDocument / VisualElement / UXML / USS (ADR-005)

---

## HUD Philosophy

The HUD is a combat instrument, not a dashboard. Every persistent element earns its screen space by answering one of three questions: *How much resource do I have? What is my combat cadence? What is my threat state?* Anything that cannot answer one of these is hidden until explicitly needed.

The game's four pillars determine display emphasis order:

- **Earned Power first** — HP, MP, and XP bars are always visible. The player's current condition and progression trajectory are never hidden.
- **Rhythm Mastery second** — the charge bar is the peripheral pulse of combat. It is never the focus; it is always felt. Positioned directly below the MP bar, it reads without demanding gaze direction.
- **Social Gravity third** — party frames at the right edge of the eye. Accountability made visible without a conversation.
- **Legendary Gear** lives in the character model, not the HUD. The HUD displays the numbers behind the gear; the gear itself is the signal.

**Three display tiers:**

| Tier | Elements | Condition |
|------|----------|-----------|
| **Always-On** | Player resource cluster (HP/MP/XP + name/level/gold), charge bar, auto-attack toggle, auto-run toggle, minimap, party frames | Permanent |
| **Contextual** | Target frame, buff/debuff tray | Shown when active; hidden otherwise |
| **Triggered** | Loot notifications, party chat panel | Loot: on assignment event; Chat: party mode only |

The Art Bible §7.1 visual rule applies without exception: *"If it does not communicate combat state or power level, it does not earn screen space."* This rule is the rejection criterion for any element proposed for future addition to the HUD.

---

## Information Architecture

### Full Information Inventory

| Element | Data Type | Source System | Update Trigger |
|---------|-----------|---------------|----------------|
| Player name | string | Character Stats | Session start (static) |
| Level badge | int (1–60) or "MAX" | Character Stats | `OnStatChanged(Level)` |
| HP bar fill | float [0, 1] | Character Stats | `OnStatChanged(CurrentHP \| MaxHP)` |
| HP numeric value | int (floor) | Character Stats | same |
| HP skull icon | bool | HP formula | `isCritical` threshold |
| HP critical border | bool | HP formula | `isCritical` threshold |
| MP bar fill | float [0, 1] | Character Stats | `OnStatChanged(CurrentMP \| MaxMP)` |
| XP bar fill | float [0, 1] | Character Stats + Leveling | `OnStatChanged(CurrentXP \| XPToNextLevel)` |
| Gold display | int (0–9,999,999) | Currency System | `GoldSyncEvent` |
| Auto-attack charge bar fill | float [0, 1] | Auto-Attack Combat | Per-tick from charge timer |
| Auto-attack toggle state | bool (active/inactive) | Auto-Attack Combat | Toggle tap |
| Auto-run toggle state | bool (active/inactive) | Movement System | Toggle tap |
| Minimap widget | visual | Map/Minimap (#35) | Zone entry |
| Party frames (×4 max) | per member: name, role, HP fill, MemberStatus | Party System | `PartyMemberStatusChanged` |
| Target frame: enemy name | string | Targeting (server) | Target selection |
| Target frame: enemy level | int | Targeting (server) | Target selection |
| Target frame: HP fill | float [0, 1] | Networking relevance layer | `EntityHealthUpdate` |
| Buff/debuff icons (×6 max) | icon + remaining ticks | Status Effects | `OnBuffApplied` / `OnBuffExpired` |
| Buff tray scroll arrows | bool | Buff count | `activeBuffCount > 6` |
| Loot notification: item icon | sprite | Zone Instancing | `GroundItemAssigned` |
| Loot notification: item name | string | Zone Instancing | `GroundItemAssigned` |
| Loot countdown pie fill | float [0, 1] | Zone Instancing | Per client tick |
| Party chat: message history | string[] (12 lines) | Party Chat | `PartyChatMessage` |
| Party chat: input field | text input | Party Chat | User interaction |

### Categorization (Always-On vs. Hidden-Until-Needed)

**Always-On** (visible every frame during gameplay):

| Element | Hide Condition |
|---------|---------------|
| Player resource cluster (name, level, HP, MP, XP, gold) | Never hidden |
| Auto-attack charge bar | Hidden when player is Dead (CR-HUD-19) |
| Auto-attack toggle | Never hidden |
| Auto-run toggle | Never hidden |
| Minimap | Never hidden |
| Party frames | Hidden in Solo Mode (`IsSoloParty = true`) |

**Contextual** (shown/hidden based on game state, no user trigger):

| Element | Shown When | Hidden When |
|---------|-----------|-------------|
| Target frame | Enemy targeted | No target; entity despawns; player taps frame |
| Buff/debuff tray | `activeBuffCount ≥ 1` | `activeBuffCount = 0` |
| HP skull icon + pulsing border | `CurrentHP / MaxHP ≤ 0.30` | HP rises above 30% |

**Triggered** (require a specific event or state to appear):

| Element | Shown When | Hidden When |
|---------|-----------|-------------|
| Loot notification | `GroundItemAssigned` received | `GroundItemDespawned` |
| Party chat panel | `IsSoloParty = false` | `IsSoloParty = true` |

---

## Layout Zones

All coordinates are in density-independent pixels (dp). Safe area origin is top-left. All positions are relative to the safe area boundary, plus 8dp interior margin.

### Layout Map

```
Safe area (after 8dp interior margin)
┌────────────────────────────────────────────────────────────────┐
│[ZONE A: PLAYER RESOURCE CLUSTER]   [ZONE B: TARGET/BUFF]  [C] │
│ Name ▪ Lv.##  ▪  ☿ 9,999,999     ┌─[Target Frame]─────┐ MAP │
│ [██████████████████ HP ██] ❤ 180  │ Enemy Name  Lv.##   │     │
│ [████████ MP ████]  💎            │ [■■■■■■■■■■■■■■]    │  80 │
│ [━━━━━━━━━━━━━━━━━━━━ CHARGE ━━━] └────────────────────┘ ×80 │
│ [░░░░░░░░░░░░░░░░░░░ XP ░░░░░░░]  [BUFF TRAY — when     ]    │
│                                    [buffs active          ] [Z]│
│                                                            [O]  │
│                                                            [N]  │
│                    [ZONE D: PARTY CHAT — PARTY MODE ONLY]  [E] │
│ [JOYSTICK ZONE]    [bottom-center, clears joystick zone]  [C]  │
│ (left 45%)         between joystick left and skills right      │
│                                          [SK1][SK2] [AUTO-ATK] │
│                                          [SK3][SK4] [AUTO-RUN] │
└────────────────────────────────────────────────────────────────┘
                                            ↑ Zone E (Combat UI / right thumb)
```

*Note: skill buttons [SK1–SK4] are Combat UI (#28) scope. The bottom-right zone boundary is defined here for layout adjacency only.*

### Zone Definitions

| Zone | Label | Anchor | Content |
|------|-------|--------|---------|
| **A** | Player Resource Cluster | Top-left, 8dp from safe area edges | Name + level badge, HP bar, MP bar, charge bar, XP bar, gold display |
| **B** | Target / Buff Layer | Top-center, horizontally centered, 8dp from top safe area edge | Target frame (contextual); buff tray below or instead |
| **C** | Status Column | Top-right, 8dp from top + right safe area edges | Minimap (top), party frames stacked below |
| **D** | Party Chat Panel | Bottom-center, 8dp above bottom safe area edge | Chat history + input (party mode only); must not enter left-half joystick zone |
| **E** | Right Action Zone | Bottom-right, 8dp from right + bottom safe area edges | Auto-attack toggle; Combat UI skill buttons adjacent |
| **F** | Left Movement Zone | Bottom-left | Virtual joystick (finger-relative spawn, left 45% of screen / bottom 60% of height); auto-run toggle (fixed, bottom-left corner) |
| **G** | Notification Layer | Bottom edge, above chat panel | Loot notifications (slides up, z-ordered above Zone D) |

### Zone Adjacency Rules

- Zone F (joystick) boundary: left half of the safe-area-inset screen width. Zone D (chat panel) left edge must be ≥ safe area midpoint.
- Zone E right edge must stay within right 45% of screen.
- Zone A must not overlap Zone B. If Zone A content extends rightward beyond the left-panel boundary, target frame re-positions downward (not implemented at MVP — parties of ≤4 make overlap unlikely at standard safe area widths).
- Zone C auto-run toggle relocated out of Zone C to Zone E (CR-HUD-20). Zone C is minimap + party frames only.

---

## HUD Elements

### Player Resource Cluster (Top-Left)

Anchor: top-left corner, 8dp from each safe area edge. Panel shape: chamfered rectangle (Art Bible §3.3). Background: Panel Dark `#1A1C1F`.

**Row 1 — Name + Level + Gold** (row height: 24dp)
- Player name: Primary Text `#E8E6DF`, 13sp, Medium weight
- Level badge: `Lv.##`, Bold 13sp, 4dp gap right of name
- Gold display: right-aligned in row — icon `☿`, value in Primary Text 13sp, 7-digit capacity

**Row 2 — HP Bar** (gap above: 4dp, height: 16dp)
- Fill: Vital Amber `#E8A020`; width: 160dp
- Left endcap: heart icon (`ui_icon_hp_heart`), 14×14dp, always visible, Primary Text color
- Right of bar: HP numeric `Mathf.FloorToInt(CurrentHP)`, 13sp Bold, 4dp gap
- Critical state: skull replaces heart; 1px Danger Alert `#E84040` pulsing border (2 Hz)
- Non-interactive — `PickingMode.Ignore` on element; `UsageHints.DynamicTransform`; fill via `style.scale` X-axis per ADR-005

**Row 3 — MP Bar** (gap above: 4dp, height: 12dp)
- Fill: Skill Blue `#2E6EBF`; width: 160dp
- Left endcap: gem icon (`ui_icon_mp_gem`), 12×12dp, always visible
- No numeric display at MVP
- Non-interactive; same ADR-005 fill pattern

**Row 4 — Auto-Attack Charge Bar** (gap above: 4dp, height: 8dp)
- Width: 160dp (matches MP bar)
- Fill color: owned by Auto-Attack Combat GDD
- Non-interactive; `PickingMode.Ignore`; hidden in Dead state (CR-HUD-19)
- ⚠️ **Open question — see Open Questions section**: charge bar may be obsolete; resolution required before implementation

**Row 5 — XP Bar** (gap above: 4dp, height: 4dp)
- Width: 160dp; no numeric display
- Fill color: see Color Decisions section (OQ-HUD-5)
- At Level 60: fill locked at 1.0, static
- Non-interactive

**Total resource cluster: 80dp tall × ~176dp wide** (including endcap icons)

---

### Minimap (Top-Right)

Anchor: top-right corner, 8dp from each safe area edge.
- Dimensions: 80×80dp (Art Bible §7.1)
- Border: Panel Border `#3A3D44`, 1dp, chamfered rectangle
- Tap target: 80×80dp → opens full map overlay
- Content rendering: owned by Map/Minimap GDD (#35); HUD provides anchor and bounding rect only

---

### Party Frames (Top-Right, Below Minimap)

Anchor: top-right, left-aligned with minimap, 8dp below minimap bottom edge.
- Shape: pill — full-radius rounded rectangle (Art Bible §3.3, the only soft shape)
- Frame size: **96dp wide × 46dp tall** per frame
- Gap between frames: 8dp
- Stack height (4 frames): 4 × 46 + 3 × 8 = **208dp**
- Hidden when `IsSoloParty = true`
- Non-interactive (`PickingMode.Ignore` per element)

**Per-frame internal layout:**
- Left 8dp: role badge icon, 22×22dp, vertically centered
- Right of badge (8dp gap): player name, 11sp Medium, Primary Text
- Below name: HP bar, 80dp wide × 8dp tall (minimum 80dp per TK-HUD-3), Vital Amber fill
- Right padding: 6dp

**MemberStatus visual mapping:**

| MemberStatus | HP Fill | Name Opacity | Additional |
|---|---|---|---|
| Online | `CurrentHP / MaxHP` | 100% | — |
| Dead | 0.0 | 50% | Skull icon overlay on pill |
| Ghost | Frozen fill value | 50% | Dotted 1px border |
| OutOfZone | 0.0 | 50% | "OUT" text badge |

**iPhone SE 3rd gen height proof (CR-HUD-22):**
- Safe area height ≈ 320dp
- Column budget: 320 − 16dp (2× 8dp interior margin) = **304dp**
- Minimap: 80dp; gap: 8dp; remaining: 304 − 80 − 8 = **216dp**
- Party stack (46dp frames, 8dp gaps): 4 × 46 + 3 × 8 = **208dp** ≤ 216dp **✓** (8dp clearance)
- Party frame bottom edge from safe top: 8 + 80 + 8 + 208 = **304dp**
- Safe area bottom boundary: 320 − 8 = 312dp
- 304dp < 312dp **✓ constraint satisfied on minimum device**

---

### Target Frame (Top-Center)

Anchor: top-center, horizontally centered in safe area, 8dp from top safe area edge.
- Shape: chamfered rectangle panel
- Size: 180dp wide × 52dp tall
- Background: Panel Dark `#1A1C1F`; border: Panel Border `#3A3D44`, 1dp
- Hidden when no target selected

**Layout:**
- Row 1 (24dp): enemy name (Primary Text, 13sp Medium) + level badge (Bold 13sp), 4dp gap between
- Row 2 (8dp, gap above 4dp): HP bar, full panel width − 16dp padding, fill Danger Alert `#E84040`
- HP updates via `EntityHealthUpdate` from networking relevance layer only (not `OnStatChanged`)

Tap target: entire 180×52dp frame → dismisses target (CR-HUD-11)

---

### Buff / Debuff Tray (Top-Center, Below Target Frame)

Anchor: top-center, horizontally centered. Positioned directly below target frame when target active (8dp gap); slides to target frame anchor when no target.
- Shape: chamfered rectangle panel; Panel Dark `#1A1C1F`
- Size: 180dp wide
- Icon grid: 2 rows × 3 columns, icon size 32×32dp, 6dp gap horizontal and vertical
  - Grid width: 3×32 + 2×6 = 108dp (centered in 180dp)
  - Grid height: 2×32 + 1×6 = 70dp
  - Panel height: 70 + 16dp padding = **86dp**
- Scroll arrows (when `activeBuffCount > 6`): 44×44dp tap target each, at panel right edge
- Hidden when `activeBuffCount = 0` (100ms linear fade)
- Icon ordering: newest-first in slot 0 (CR-HUD-14)

---

### Auto-Attack Charge Bar (Below MP Bar)

Covered fully in Player Resource Cluster Row 4. Restatement for reference:
- 8dp tall × 160dp wide; anchored within resource cluster 4dp below MP bar
- Spec ownership: Auto-Attack Combat GDD; HUD owns anchor and `PickingMode.Ignore`
- ⚠️ Potentially obsolete — see Open Questions

---

### Auto-Run Toggle (Bottom-Left, Near Joystick)

*This position overrides CR-HUD-20 (which placed toggle bottom-right). The UX spec is the authoritative position document. CR-HUD-20 in hud.md must be updated to match before implementation.*

Anchor: Zone F (bottom-left), fixed position — 8dp from left safe area edge, 72dp from bottom safe area edge.
- Visual size: 44×28dp (pill shape — Art Bible §7.1)
- Tap target: 44×44dp (8dp invisible padding above and below)
- Shape: pill
- Inactive: Panel Dark fill `#1A1C1F`, Panel Border `#3A3D44`
- Active: UI Interactive Blue fill `#4A9EE0` (Art Bible §7.1)
- Label: "RUN", 11sp Bold, centered
- Joystick exclusion zone: virtual joystick must not spawn with its center within 44dp of the toggle center (prevents accidental toggle activation during joystick drag start)

---

### Loot Notification (Bottom, Zone G)

Anchor: bottom-center, slides up from bottom safe area edge. Z-ordered above party chat panel.
- Size: 220dp wide × 52dp tall
- Shape: chamfered rectangle; Panel Dark `#1A1C1F` at 90% opacity
- Non-interactive (`PickingMode.Ignore`)

**Layout:**
- Left 8dp + icon: item icon 32×32dp, vertically centered
- Center: item name, Primary Text 13sp Medium
- Right: countdown pie 36×36dp
  - Fill: Vital Amber `#E8A020` (normal state)
  - Fill: Danger Alert `#E84040` on `GroundItemExpiryWarning`

**Animations (Art Bible §7.4):**
- Entry: 160ms ease-out, slides up 32dp + fade 0→1
- Hold: visible until `GroundItemDespawned` (or 3s auto-clear if event never arrives)
- Exit: 100ms linear fade-out
- `GroundItemDespawned` forces immediate exit regardless of hold timer

---

### Party Chat Panel (Bottom-Center, Zone D)

Anchor: bottom-center, 8dp above bottom safe area edge. Visible only when `IsSoloParty = false`.
- Width: spans from joystick zone right edge (left 45% of safe area width) to skill zone left edge
  - On SE landscape (651dp usable): ~170dp wide (center corridor)
- Visible lines: **6** (scrollable to 12-message history); reduces footprint to ~132dp height
  - 6 lines × 16dp line height = 96dp + 36dp input row + 8dp padding = **140dp total**
- Shape: chamfered rectangle; Panel Dark `#1A1C1F` at 75% opacity (semi-transparent, 3D scene visible)
- Message text: Secondary Text `#9A9DA6`, 11sp Medium
- Input field: Primary Text color, 11sp; send button 44×44dp
- Hidden in Solo Mode (instant hide, no animation)

---

## Color Decisions

### XP Bar Fill Color (Resolves OQ-HUD-5)

**Decision: Muted Amber-Gold `#C4912A`**

Rationale: Warm hue signals "earned reward" — consistent with the Art Bible rule (warm = reward/success, cool = controlled expenditure). Visually distinct from Vital Amber HP `#E8A020` (darker, more brown-orange) and Skill Blue MP `#2E6EBF` (separate hue family). Enhancement Gold `#D4AF37` is reserved for gear enhancement; XP uses a darker desaturated variant to avoid conflict.

Colorblind safety: HP amber and XP muted-gold may appear similar under deutanopia/protanopia. Mitigation: XP bar is always the bottom-most and thinnest bar (4dp vs. 16dp HP bar) — spatial position and size differentiate it without color. No additional icon backup required.

| Bar | Color | Hex | Role |
|-----|-------|-----|------|
| HP | Vital Amber | `#E8A020` | Current life — immediate danger signal |
| MP | Skill Blue | `#2E6EBF` | Resource expenditure |
| XP | Muted Amber-Gold | `#C4912A` | Earned progression toward next level |

This color must be added to the project's UI palette in the Art Bible before implementation (Art Bible §4.4 extension).

---

## Dynamic Behaviors

### Bar Fill Animation

All fill bars (HP, MP, XP, charge bar, party frame HP, target HP, loot pie) use the `style.scale` X-axis pattern per ADR-005. Fill updates are driven by events (`OnStatChanged`, `EntityHealthUpdate`, etc.) — never per-frame polling.

| Bar | Fill change | Number change |
|-----|------------|---------------|
| HP | 150ms ease-out cubic when decreasing; instant when increasing (healing should feel immediate) | Instant — no lerp |
| MP | 120ms ease-out cubic | N/A (no numeric display at MVP) |
| XP | Instant (XP increments are discrete per-kill rewards; no mid-kill lerp) | N/A |
| Charge bar | Driven by Auto-Attack Combat GDD tick timer — smooth, per-tick | N/A |
| Party frame HP | Instant | N/A |
| Target HP | Instant on `EntityHealthUpdate` | N/A |
| Loot pie fill | Per client tick — smooth drain | N/A |

*Art Bible §7.4 rule: "HP/MP bar values (instant number change; bar fill animates, number does not lerp)"*

### State Transition Animations

| Transition | Duration | Curve | What changes |
|-----------|---------|-------|--------------|
| HP Critical — enter | Instant | — | Skull icon appears; pulsing border activates at 2 Hz |
| HP Critical — exit | Instant | — | Skull disappears; border deactivates |
| Target frame — appear | 100ms | Ease-out cubic | Slides down 12dp from top safe area edge + fade 0→1 |
| Target frame — dismiss | 80ms | Linear fade | Fade only, no slide |
| Buff tray — appear | 150ms | Ease-out cubic | Slides down 12dp + fade 0→1 |
| Buff tray — dismiss | 100ms | Linear fade | Fade only |
| Party frames — appear (join party) | 180ms | Ease-out cubic | Fade 0→1 (Art Bible §7.4 panel open) |
| Party frames — hide (go solo) | 100ms | Linear fade | Fade only |
| Chat panel — appear | Instant | — | No animation; appears on party join |
| Chat panel — hide | 100ms | Linear fade | |
| Charge bar — hide (Dead state) | Instant | — | Element hidden; no fade |
| Charge bar — show (resurrect) | 100ms | Ease-out fade | Fade in |

### Hidden-Until-Needed Transitions

All contextual and triggered elements use the general rule: **fast in (ease-out), faster out (linear fade).** No element makes the player wait to receive information or to clear it.

| Element | Entry | Exit | Notes |
|---------|-------|------|-------|
| Target frame | 100ms slide-down + fade | 80ms fade | Tapping dismisses; death/despawn also triggers exit |
| Buff tray | 150ms slide-down + fade | 100ms fade | Exits when `activeBuffCount = 0` |
| Loot notification | 160ms slide-up + fade (from bottom edge) | 100ms fade | Auto-clear after 3s if `GroundItemDespawned` doesn't arrive |
| Party chat panel | Instant appear | 100ms fade | Party join/leave; no positional animation |

---

## Platform & Device Compliance

### Safe Area Layout

Safe area insets are applied at runtime via `RuntimePanelUtils.ScreenToPanel` in Unity 6.3 (per ADR-005). Raw `Screen.safeArea` pixel values cannot be written directly to UI Toolkit `style.margin*` (panel logical units differ from physical pixels). The conversion must apply a Y-axis flip (Screen space Y origin is bottom-left; UI Toolkit is top-left).

```csharp
void ApplySafeArea(VisualElement hudRoot) {
    IPanel panel = hudRoot.panel;
    Rect sa = Screen.safeArea;
    Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel,
        new Vector2(sa.xMin, Screen.height - sa.yMax));
    Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel,
        new Vector2(sa.xMax, Screen.height - sa.yMin));
    hudRoot.style.marginLeft   = topLeft.x     + 8f;
    hudRoot.style.marginTop    = topLeft.y     + 8f;
    hudRoot.style.marginRight  = (panelWidth  - bottomRight.x) + 8f;
    hudRoot.style.marginBottom = (panelHeight - bottomRight.y) + 8f;
}
```

Call in `Awake` and on `OnRectTransformDimensionsChange`. Never hardcode pixel values. Handles iPhone X notch, Dynamic Island (top safe area ≈ 62pt in landscape), and home indicator bottom insets.

**Key device safe area values (landscape orientation):**

| Device | Safe area top | Bottom | Left/Right | Notes |
|--------|--------------|--------|------------|-------|
| iPhone SE 3rd gen | ~0pt | ~0pt | 0pt | No notch; home button below screen |
| iPhone X / 11 / 12 / 13 | ~44pt | ~34pt | ~44pt | Notch left/right, home indicator |
| iPhone 14 Pro / 15 | ~62pt | ~34pt | ~44pt | Dynamic Island |

### Top-Right Column Height Proof — iPhone SE 3rd Gen (Resolves CR-HUD-22)

**Minimum device: iPhone SE 3rd gen, landscape, safe area height ≈ 320dp**

| Component | Height | Cumulative from safe area top |
|-----------|--------|------------------------------|
| Top interior margin | 8dp | 8dp |
| Minimap | 80dp | 88dp |
| Gap (minimap → party frames) | 8dp | 96dp |
| Party frames (4 × 46dp + 3 × 8dp gaps) | 208dp | 304dp |
| Bottom interior margin | 8dp | 312dp |

Safe area height (320dp) − used height (312dp) = **8dp clearance ✓**

Party frame bottom edge (304dp from safe top) does not exceed `safeArea.height − 8dp` (312dp). **Constraint satisfied.**

### Thumb-Reach Compliance

Per Art Bible §7.1, landscape two-handed hold:
- **Left thumb zone**: left 45% of screen / bottom 60% of height
- **Right thumb zone**: right 45% of screen / bottom 60% of height
- **Top half**: low-frequency taps only — no active combat inputs

| Element | Zone | Frequency | Compliant? |
|---------|------|-----------|------------|
| HP/MP/XP bars | Top-left (top half) | Read-only | ✓ display only |
| Auto-run toggle | Bottom-left | Low (tap to toggle) | ✓ left thumb zone |
| Virtual joystick | Bottom-left (floating) | High (continuous) | ✓ left thumb zone |
| Auto-attack toggle | Bottom-right | Low-medium (tap to toggle) | ✓ right thumb zone |
| Skill buttons (Combat UI) | Bottom-right | High | ✓ right thumb zone |
| Target frame dismiss | Top-center | Low (non-combat) | ✓ top = non-combat ok |
| Buff tray scroll | Top-center | Low (non-combat) | ✓ top = non-combat ok |
| Minimap tap | Top-right | Low (non-combat) | ✓ top = non-combat ok |
| Party chat input | Bottom-center | Low-medium | ⚠️ requires two-thumb stretch or explicit chat mode |

---

## Accessibility

Primary reference: Art Bible §4.5 (Colorblind Safety). WCAG 2.3.1 (flash rate ≤ 3 flashes/sec) is cited in CR-HUD-4. No dedicated accessibility requirements document exists yet.

### Colorblind Safety

| Pair | Risk | Backup cue |
|------|------|------------|
| Vital Amber (HP) vs. Muted Amber-Gold (XP) | Similar under deutanopia | Spatial: XP always bottom-most, 4dp thin vs 16dp HP. Heart icon on HP endcap. |
| Vital Amber (HP) vs. Skill Blue (MP) | Safe (different hue families) | Heart vs. gem endcap icons; HP always above MP |
| HP critical Danger Alert `#E84040` vs. normal fill | Risk under protanopia | Pulsing border (motion) + skull icon — both are non-color cues |
| Target HP Danger Alert `#E84040` vs. normal context | Enemy HP is always this color | No confusion risk — target frame has dedicated zone and enemy name label |

### Motion Safety

HP critical pulsing border: 2 Hz (one full cycle every 500ms). Max permitted by WCAG 2.3.1 is 3 flashes/sec. Current design is safely below threshold.

No other pulsing or flashing elements in the HUD.

### Minimum Touch Targets

All interactive HUD elements satisfy 44×44dp (Apple HIG minimum) and 52×52dp for combat-critical elements:

| Element | Visual | Tap target | Compliant? |
|---------|--------|-----------|------------|
| Auto-run toggle | 44×28dp | 44×44dp | ✓ |
| Auto-attack toggle | 44×44dp | 44×44dp | ✓ |
| Target frame dismiss | 180×52dp | 180×52dp | ✓ |
| Buff tray scroll arrows | 24×24dp visual | 44×44dp | ✓ |
| Minimap tap | 80×80dp | 80×80dp | ✓ |
| Chat send button | 32×32dp visual | 44×44dp | ✓ |

### Font Size Minimums (Art Bible §7.2)

| Text | Minimum size | In this spec |
|------|-------------|-------------|
| HUD HP/MP value | 13sp | 13sp ✓ |
| Player name, level badge | 13sp | 13sp ✓ |
| Party frame name | 11sp | 11sp — at the floor; do not reduce |
| Buff tray: none (icon-only) | — | — |
| Chat messages | 11sp (tooltip floor) | 11sp — at the floor |

### Screen Reader

UI Toolkit `AccessibilityRole` required on all interactive elements. Roles must not be bitwise-combined (Unity 6.3 changed to standard enum — Art Bible §7.1 / hud.md UI Requirements).

Display-only elements: `AccessibilityRole.None` or omit; set `PickingMode.Ignore` so they are invisible to the accessibility tree.

---

## Open Questions

**OQ-UX-HUD-1** *(pre-implementation — charge bar)*: **Is the auto-attack charge bar still required?**
The charge bar was specced in Auto-Attack Combat GDD as a visual indicator of the attack cycle timer. If the rhythm system's feel is communicated through audio + animation alone (as in Knight Online, where the attack cadence was implicit), the bar may not add value and adds visual clutter. Resolution requires a playtest of the combat prototype. If removed, Row 4 of the player resource cluster is eliminated and XP bar moves to Row 4.
*Owner*: Auto-Attack Combat GDD + playtest results.

**OQ-UX-HUD-2** *(RESOLVED 2026-06-20 — design/gdd/combat-ui.md)*: **Expanded skill bar and skill bar toggle**
*Resolution*: Combat UI GDD (#28) specifies 4 visible primary slots (S1–S4) + [+] toggle that expands to 8 slots (S5–S8 appear above the primary row). Zone E footprint = 292dp. See `design/gdd/combat-ui.md` §CR-CUI-5, CR-CUI-6, CR-CUI-8.
*Owner*: Resolved — Combat UI GDD (#28).

**OQ-UX-HUD-3** *(RESOLVED 2026-06-20 — design/gdd/combat-ui.md)*: **Consumables on skill buttons (Knight Online style)**
*Resolution*: Combat UI GDD (#28) chose Option B — skill bar slots are skills-only (8 slots). Consumable Use System retains its own 2-slot potbar (HP slot index 0, MP slot index 1) as a separate UI element in Zone E adjacent to the skill bar. No CUS GDD amendments required. See `design/gdd/combat-ui.md` §CR-CUI-2.
*Owner*: Resolved — CUS potbar specification remains in Consumable Use System GDD.

**OQ-UX-HUD-4** *(documentation — HUD GDD update required)*: **CR-HUD-20 must be updated**
CR-HUD-20 in `design/gdd/hud.md` specifies auto-run toggle in the bottom-right zone (right thumb). This UX spec overrides that position to bottom-left near the joystick (left thumb), which is the more natural placement for a movement toggle. The HUD GDD must be amended to match before implementation begins.
*Owner*: hud.md CR-HUD-20 — one-line amendment to position spec.

**OQ-UX-HUD-5** *(documentation — Art Bible update required)*: **XP bar color `#C4912A` must be registered**
The Art Bible §4.4 UI Palette does not include the XP bar color. The color decided in this spec (`#C4912A`, Muted Amber-Gold) must be formally added to the UI palette table with its semantic label and usage rule before implementation. The art direction team should confirm it before adding.
*Owner*: Art Bible §4.4 — requires sign-off.

**OQ-UX-HUD-6** *(pre-implementation — Status Effects)*: **Status Effects event interface (OQ-HUD-8)**
The buff/debuff tray cannot be implemented until `OnBuffApplied` / `OnBuffExpired` event contracts are defined. This is an existing gate (OQ-HUD-8 in hud.md). The buff tray spec in this document is complete — implementation is blocked on event interface definition only.
*Owner*: Status Effects GDD + networking wire protocol (amendment required).

**OQ-UX-HUD-7** *(design question — chat panel)*: **Chat panel thumb-reach in two-handed landscape hold**
The party chat input field is in the bottom-center zone, which is not comfortably reachable by either thumb without shifting grip. This is flagged in the thumb-reach compliance table above. A common mobile solution: tapping the chat panel enters a dedicated "chat mode" where the keyboard appears and combat controls are temporarily suppressed. This behavior should be specified in the Party Chat GDD before implementation.
*Owner*: Party Chat GDD — input mode spec.
