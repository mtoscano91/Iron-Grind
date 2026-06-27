# Art Bible: Project Iron Grind

*Created: 2026-04-19*
*Status: Complete*
*Engine: Unity 6.3 LTS · Platform: iOS (landscape) · Style: Low-poly 3D*

---

## 1. Visual Identity Statement

### The Visual Rule

> **If it does not communicate combat state or power level, it does not earn screen space.**

Every texture detail, particle effect, UI element, and environmental prop is justified by one question: does this help the player read the fight or read the player's power? Aesthetics that survive that question are welcome. Aesthetics that do not are cut.

---

### Supporting Principles

#### Principle 1 — Legibility First
*(Pillar: Rhythm Mastery)*

Any visual element that conveys gameplay-critical information — health bars, hit numbers, enemy aggro state, character silhouettes — must be instantly readable on a 5-inch mobile screen at arm's length, under any lighting condition in the game world.

**Design test:** When choosing between a stylistically interesting hit number font and a plain high-contrast one, choose plain. When choosing between a detailed enemy silhouette that reads ambiguously at distance and a simplified low-poly shape that reads cleanly, choose simplified.

---

#### Principle 2 — Power Has a Visible Signature
*(Pillar: Legendary Gear)*

Enhancement level must be readable as a glance-level signal, not a tooltip. A +9 weapon must look meaningfully different from a +0 weapon when both characters are in the same frame at social distance — no menu required.

**Design test:** Can a player distinguish +0, +5, and +9 without opening any menu from ten meters away in-world? If not, the signal is too subtle. Enhancement glow escalates on a fixed three-stop scale: neutral metal (+0–+3) → warm gold edge-light (+4–+7) → bright white bloom with trailing particles (+8–+9).

---

#### Principle 3 — Hit Feedback Is the Primary Sensory Budget
*(Pillars: Rhythm Mastery + Earned Power)*

The visual and audio response to a successful hit, a kill, and an upgrade result is where the majority of artistic effort is allocated. Environmental decoration and character cosmetics are secondary. Combat feedback is the art.

**Design test:** When allocating time between polishing a background environment asset and refining hit flash timing, screen shake curve, or kill particle burst — choose combat feedback. The world can be sparse; the hit cannot be.

---

## 2. Mood & Atmosphere

*All lighting values target Unity URP with a single Directional Light + one fill source. No volumetric fog unless batched as a single full-screen pass (draw call budget).*

**Cross-state rule:** Warm hues are reserved for reward, social safety, and success. Cool hues signal grind, threat, and loss. Breaking this mapping requires explicit sign-off — color language consistency is load-bearing for player orientation.

---

### 2.1 Grinding (Solo Mob Farming)

| Property | Value |
|---|---|
| **Primary Emotion** | Hungry |
| **Lighting Character** | Cool-neutral (5500–6000K), low-to-medium contrast, late afternoon directional slant (30° above horizon, side-angled) |
| **Atmosphere** | Repetitive, gritty, workmanlike, slightly oppressive |
| **Energy Level** | Measured |
| **Mood Carrier** | Persistent desaturated blue-grey ambient (`#7A8FA0`, Intensity 0.6). Sky tint `#3D4F5C`. Reserve warm hues entirely for loot and XP rewards — every drop is a contrast event against the grey world. |

---

### 2.2 Party Combat (Grouped, Hard Content)

| Property | Value |
|---|---|
| **Primary Emotion** | Electrified |
| **Lighting Character** | High contrast, cool ambient (`#4A5F8C`) vs. warm weapon VFX (`#FF8C32`), no defined time-of-day |
| **Atmosphere** | Chaotic, urgent, punishing, alive |
| **Energy Level** | Frenetic |
| **Mood Carrier** | Per-character colored rim light (baked as per-material emissive band, not a real light — zero draw calls). Directional light contrast 1.4, Shadow Strength 0.85. Post-process vignette 0.35 to focus the eye center-field. |

---

### 2.3 Town / Social Hub

| Property | Value |
|---|---|
| **Primary Emotion** | Ease |
| **Lighting Character** | Warm (3200–3800K), low contrast, golden-hour sun at 45° elevation, soft shadows |
| **Atmosphere** | Welcoming, unhurried, familiar, lived-in |
| **Energy Level** | Contemplative |
| **Mood Carrier** | Directional light `#FFD580`, Shadow Strength 0.3, ambient boosted to 1.0. Ambient intensity exceeds directional — the only state where this is true. Player name tags render at full opacity (suppressed in combat). |

---

### 2.4 Upgrade Attempt (Enhancement NPC)

| Property | Value |
|---|---|
| **Primary Emotion** | Dread |
| **Lighting Character** | Cool-dark (7500K, desaturated), extreme contrast, single harsh top-down key, no fill |
| **Atmosphere** | Isolated, ceremonial, airless, tense |
| **Energy Level** | Tense |
| **Mood Carrier** | World dims to near-black except the enhancement UI and item being upgraded. Item sits in cold white light (`#C8D4E8`) — the brightest pixel on screen. Trigger Post-Process Volume override: Lift → `#0A0D12`, Exposure -0.4 EV. |

---

### 2.5 Upgrade Success (Visual Payoff)

| Property | Value |
|---|---|
| **Primary Emotion** | Triumph |
| **Lighting Character** | Explosive warm burst (2800K at peak), blooms outward, transitions to zone ambient over 1.5 seconds |
| **Atmosphere** | Radiant, earned, momentary, irreversible |
| **Energy Level** | Percussive (single sharp spike — not sustained) |
| **Mood Carrier** | Single-frame Exposure spike (+1.2 EV) + bloom threshold drop to 0.1 for 3 frames. Item emissive fires to `#FFFFFF` intensity 3.0 for those 3 frames, then settles to permanent tier glow. **Do not loop.** Loop = devalued. Single burst + permanent emissive change is the entire payoff. |

---

### 2.6 Upgrade Failure / Item Destruction

| Property | Value |
|---|---|
| **Primary Emotion** | Gutted |
| **Lighting Character** | Neutral-cold (8000K), contrast collapses to near-flat, scene dims uniformly |
| **Atmosphere** | Hollow, quiet, final, numbing |
| **Energy Level** | Arrested (motion stops, then slow) |
| **Mood Carrier** | No flash, no burst. Item emissive cuts to zero; albedo desaturates to `#3A3A3A`. Post-Process Saturation lerps 1.0 → 0.65 over 1.2 seconds and holds. **Do not use red** — red signals danger. Grey signals loss. Silence is the punishment. |

---

### 2.7 Menus / Character Screen

| Property | Value |
|---|---|
| **Primary Emotion** | Assessment |
| **Lighting Character** | Neutral studio two-point (5000K key, cooler fill), no environmental context |
| **Atmosphere** | Clinical, deliberate, clean, comparative |
| **Energy Level** | Still |
| **Mood Carrier** | Key light: `#F0EAD8` at 45° upper-left, Intensity 1.2. Fill: `#8AACCC` at 135° lower-right, Intensity 0.5. Background solid dark gradient (`#0E1018` → `#1C2230`). Gear must read for quality on its own terms, not flattered by environmental mood. |

---

## 3. Shape Language

*Visual Identity anchor: If a shape does not communicate combat state or power level, it does not earn screen space.*

---

### 3.1 Character Silhouette Philosophy
*(Pillars: Earned Power · Rhythm Mastery)*

**The thumbnail rule:** Every playable class must be identifiable as a unique silhouette at 48×48 pixels — the size of a party frame portrait on a 5-inch screen. If the class reads only by color or texture at that scale, the silhouette has failed.

**The gate every class must pass:** Cover the character in solid black. The outline alone must answer: what class is this, and what is their power level? If both questions cannot be answered from the silhouette, the design goes back.

**Class shape assignments:**

| Class Role | Silhouette Rule | Dominant Shape |
|---|---|---|
| Tank / Warrior | Wide, planted. Shoulder mass is the widest point, wider than hip. Feet planted apart. | Rectangle, mass concentrated at top |
| Assassin / Rogue | Narrow, angled forward. Low center of gravity. One weapon arm extended. | Triangle, apex pointing forward |
| Mage / Caster | Tall, narrow torso, large head relative to body. Robe/coat flares at base. | Inverted triangle on a narrow column |
| Support / Priest | Symmetric, slightly wider at base than warriors, less shoulder mass. Staff head distinct from weapon tip. | Vertical rectangle with centered apex |

**Enhancement modifies the silhouette at +8–+9 only.** Particles and bloom extend the perceived silhouette outward. A +9 Warrior occupies visibly more screen space than a +0 Warrior in the same pose.

**Enemy vs. player shape contract:** Enemies use triangular and irregular silhouettes — pointed appendages, asymmetric outlines, hunched profiles that break vertical symmetry. Player characters use structured, architecturally readable silhouettes. The player's eye learns: structured = mine, irregular = threat.

---

### 3.2 Environment Geometry
*(Pillar: Rhythm Mastery)*

**Dominant geometry: angular and planar.** Flat faces, hard edges, minimal organic curves. This is both a performance directive and an aesthetic one — curves require more polygons without adding clarity.

**Emotional register:** Angles communicate hostility, permanence, and constructed danger. Ruins with sheared edges, cliffs with exposed rock planes, dungeon corridors with trapezoidal cross-sections. The world feels ancient and indifferent, never decorative.

**Organic shapes have one job:** Ground-level variation only. Foliage, terrain breakup, and river banks may use organic curves because they recede in the mid-ground. Rule: organic shapes are allowed only below the player character's knee height or in the far background plane.

**Concave geometry is the combat signal.** Arenas, boss rooms, and ambush zones use concave floor geometry — a sunken pit, a recessed platform, a bowl-shaped clearing. The player learns: flat/convex terrain = transit space, concave terrain = fight space. This must be maintained consistently — it becomes a navigation cue.

**Prop hierarchy by shape complexity:**

| Tier | Examples | Max Triangles |
|---|---|---|
| Foreground combat props | Barrels, loot spawns, spawn markers | 50 |
| Mid-ground structural props | Walls, pillars, platforms | 200 |
| Background fills | Distant mountains, horizon decorations | Billboard / skybox only |

No foreground or mid-ground prop uses curved geometry as its primary read. A pillar is a hexagonal prism, not a cylinder.

---

### 3.3 UI Shape Grammar
*(Pillars: Rhythm Mastery · Legendary Gear)*

**The UI is a distinct visual language, not a mirror of the world.** UI and environment must never compete for visual register — the player cannot mistake interface for geometry in peripheral vision.

**Primary UI shape: the chamfered rectangle.** All buttons, panels, and frames use rectangles with corners cut at 45°. This reads as "forged" — consistent with the gear aesthetic — and is distinct from both environment hard-right-angles and soft modern rounded rectangles.

**Shape rule by UI element type:**

| Element | Shape Rule |
|---|---|
| Action buttons (skills, attacks) | Chamfered square. Equal width/height — signals equivalent weight. |
| Passive info panels (stats, buffs) | Chamfered rectangle, landscape orientation (wider than tall). |
| Alerts and warnings | Diamond (square rotated 45°). Exclusively for time-critical info: status effects, cooldown expiry, incoming AoE. |
| Enhancement / gear upgrade frames | Double-border chamfered rectangle. Gap fills with tier glow color at +4 and above. |
| Party / social frames | Pill shape (full-radius rounded rectangle). The only soft shape in the UI vocabulary. Signals: social space, not combat space. |

**The UI does not decorate.** No filigree, no ornamental corners. The chamfer is the only gesture toward craft. Every pixel that does not carry information is removed.

---

### 3.4 Hero Shapes vs. Supporting Shapes
*(Pillars: Earned Power · Social Gravity · Legendary Gear)*

**Shape weight is attention weight.** In low-poly 3D, the eye is guided by silhouette complexity and size, not texture detail.

| Tier | Elements | Shape Rule |
|---|---|---|
| **Tier 1 — claim attention** | Player characters, active enemies, loot drops, AoE indicators | Most silhouette complexity. Loot = diamond shape. AoE = bold filled circles/arcs with hard outer edges. |
| **Tier 2 — provide context** | Patrolling (inactive) enemies, environmental props | Simple, symmetric geometry. Animation pose change and rim light promote to Tier 1 on aggro. |
| **Tier 3 — orient, don't distract** | Background terrain, skybox, ambient particles | Never use triangular/pointed shapes in the foreground plane — pointed shapes are reserved for Tier 1 threats. |

**The loot drop shape contract:** All items on the ground use the same base shape — a flattened diamond (rhombus, wider than tall) with color fill indicating rarity. Shape is constant; color and glow intensity communicate tier. One shape to learn, one cue to read.

**The attention cascade in any given frame:**
1. Incoming damage / AoE ground indicator (largest, highest contrast)
2. Player character (known position, reference anchor)
3. Active enemy with lowest health or highest threat
4. Loot on ground (diamond)
5. Everything else

---

## 4. Color System

---

### 4.1 Primary Palette

These seven colors form the world's visual bedrock. Every color in the game derives from this list via tint, shade, or emissive blend — never introduced arbitrarily. Iron Ground and Ash Mid together must cover **no less than 70%** of any in-world screen area. The remaining five are accent-only.

| Name | Hex | Role |
|---|---|---|
| **Iron Ground** | `#2B2D31` | Base environment, unenhanced gear metal, neutral UI chrome. The world at rest. |
| **Ash Mid** | `#4A4E57` | Secondary surfaces, shadow fills, low-tier enemy skin. Recedes, never competes. |
| **Threat Red** | `#C0392B` | Incoming damage, boss telltales, HP deficit. Reserved for "you are losing something." |
| **Vital Amber** | `#E8A020` | HP bar fill, recovery pickups, warm system alerts. Alive but watchful. |
| **Enhancement Gold** | `#D4AF37` | The only warm saturated hue in the base world. Enhancement gain, loot tier (+4–+7). |
| **Ascension White** | `#F0EFE8` | Maximum enhancement bloom, critical hit flash, legendary item text. The ceiling. |
| **Skill Blue** | `#2E6EBF` | MP bar, active skill icons, cooldown rings. Controlled expenditure. |

---

### 4.2 Semantic Color Vocabulary

**Gold = Enhancement Reward.** Gold is reserved for objects the player has improved. It is never decorative. If it appears on an object, that object has been enhanced. Mirrors Diablo 2's magic-item yellow — the target audience carries this expectation in.

**Red = Incoming Threat / Loss.** Threat Red is used only when the player is losing something or about to. Never used for player offensive actions — that would dilute the "you are being hurt" signal. Red communicates *to* the player, not *about* what the player does.

**Amber = Current Life State.** HP bar fill. Desaturates toward grey as HP drops, visually narrowing the warm safety zone. Shifts to red at 30% — not a new color appearing, but the amber zone collapsing into threat territory.

**Blue = Controlled Expenditure.** MP represents intentional resource use, unlike HP which is lost reactively. Cool mood rule reinforces this (cool = grind, controlled effort).

**White = Pinnacle State.** Appears only at maximum enhancement and on critical hits. It is the terminus of an escalation. Using it for any other purpose (loading states, neutral icons, empty bars) dilutes its pinnacle signal.

**Dark Greys = World Default.** Iron Ground and Ash Mid communicate "unenhanced, natural state." An Ash Mid enemy reads as low stakes. A weapon with no gold edge-light reads as base tier. The greys are the semantic baseline from which all other meanings depart.

---

### 4.3 Enhancement Glow Palette (Unity URP Parameters)

| Stop | Level | Base Color | Smoothness | Metallic | Emission |
|---|---|---|---|---|---|
| **Base Metal** | +0 to +3 | `#3D3F44` | 0.45 | 0.75 | None |
| **Warm Edge-Light** | +4 to +7 | `#2E3035` | 0.60 | 0.80 | `#D4AF37` @ intensity 1.8 |
| **White Bloom** | +8 to +9 | `#1C1D20` | 0.85 | 0.90 | `#F5F0E0` @ intensity 4.5 |

URP Bloom settings for +8–+9 only: Threshold 1.0 · Intensity 0.4 · Scatter 0.7

*The base color darkens progressively across all three stops. The dark core makes the white bloom read brighter than its absolute value via simultaneous contrast — the same technique Diablo 2 used on rune word sprites.*

---

### 4.4 UI Palette

The UI palette diverges from the world palette at three points: panel backgrounds use a dedicated color darker than Iron Ground, interactive elements use a brighter blue than Skill Blue, and text uses off-white values reserved away from Ascension White. **UI must always read as a layer above the world, never embedded in it.**

| Name | Hex | Scope | Why It Diverges |
|---|---|---|---|
| **Panel Dark** | `#1A1C1F` | Modal backgrounds, inventory fills | Darker than Iron Ground — reads as a surface *in front of* the world |
| **Panel Border** | `#3A3D44` | Panel outlines, separator lines | Lighter than Ash Mid — structure without a new layer |
| **UI Interactive Blue** | `#4A9EE0` | Tappable buttons (default), selected slot | Lighter/more saturated than Skill Blue — readable against Panel Dark |
| **UI Interactive Active** | `#6AB8F7` | Button press flash, drag highlight | Derives from Interactive Blue +40% lightness |
| **Primary Text** | `#E8E6DF` | Item names, stat values, menu labels | Warm off-white — saves Ascension White for enhancement pinnacle only |
| **Secondary Text** | `#9A9DA6` | Descriptions, flavor text, inactive labels | Desaturated Ash Mid — recedes from primary text |
| **Danger Alert (UI)** | `#E84040` | Low HP HUD flash, out-of-resource warning | Brighter than Threat Red for legibility against Panel Dark |
| **XP Bar Fill** | `#C4912A` | XP progress bar fill only | Muted amber-gold; warm hue signals earned progress. Darker/more brown than Vital Amber `#E8A020`; reserved exclusively for XP bar. Added 2026-06-20 per `design/ux/hud.md`. |

**Colors that appear only in UI and must never appear in world space:** Panel Dark, UI Interactive Blue, Primary Text, Secondary Text, XP Bar Fill.

---

### 4.5 Colorblind Safety

Three critical pairs require non-color backup cues:

**Pair 1: Threat Red vs. Vital Amber (HP bar)**
Collapse under deuteranopia/protanopia into indistinguishable yellow-brown.
- Backup (shape): HP bar gains a pulsing 1px frame-border at 30% threshold (0.8s ease-in-out pulse, always present)
- Backup (icon): Skull icon (`ui_icon_hplow_warning`) appears in HP bar left endcap below 30%

**Pair 2: Enhancement Gold (+4–+7) vs. Base Metal (+0–+3)**
Gold shifts toward green-grey under tritanopia.
- Backup (badge): Enhanced gear (+4+) always displays a numeric badge (`+4`…`+9`) in Primary Text on Panel Dark chip at item icon lower-right corner. The badge is the primary communicator; glow is accent.

**Pair 3: Skill Blue (MP) vs. Vital Amber (HP)**
Bars must be spatially separated and labeled.
- Backup (position + icon): HP bar always top, MP bar always below. Each has a permanent left-endcap icon (heart for HP, gem for MP) in Primary Text color, not colored to match the bar.

**Production check:** Before shipping any HUD update, run a screenshot through a colorblind simulator (e.g., Coblis) for deuteranopia and protanopia. Flag any case where two adjacent semantic colors become indistinguishable.

---

## 5. Character Design Direction

---

### 5.1 Player Character Visual Archetype

The player character at level 1, +0 gear reads as a functional combatant — not a hero, not a beggar. Armor is present and fitted. It shows wear: seams, dents, asymmetric panel shapes that imply repair history. Nothing gleams. Nothing is decorative.

**Three signals that mark a model as the player's avatar:**

| Signal | Rule |
|---|---|
| Structural silhouette | Matches class shape assignment (Section 3.1). Symmetric, planted, no irregular appendages. Any humanoid with irregular geometry, hunched posture, or asymmetric mass is an enemy or NPC. |
| Name-tag layer | Player name renders at full opacity in Town; suppressed in combat. Primary "this is a player" marker in social contexts. |
| Gear metallic baseline | Player gear is always metallic (Metallic 0.75+). NPCs and background characters use flat/matte materials (Metallic 0.2 or below). Metallic sheen in sunlight is a player-type signal at a glance. |

**What the default model does NOT have:** No capes or cloth physics at base tier. No emissive markings at +0–+3. No visible face customization — at combat camera distance, facial detail is wasted budget. The helmet or hood is the face.

---

### 5.2 Class Distinguishing Feature Rules

**Material language by class:**

| Class | Primary Material | Secondary Material | Forbidden |
|---|---|---|---|
| Warrior | Hammered plate metal (Metallic 0.75, Smoothness 0.35). Surface implies denting and grinding. | Leather straps at joints — matte brown, no sheen. | No fabric panels larger than a scarf-width in the torso silhouette. |
| Rogue | Articulated leather (Metallic 0.20, Smoothness 0.55). Looks flexible, not rigid. | Single metal element (one pauldron or bracer) to break the all-leather read. | No full-plate torso. Must have asymmetry — one arm visibly differs from the other. |
| Mage | Layered cloth (Metallic 0.0, Smoothness 0.20). Heavy cloth folds are the texture signal. | Staff: bone-white or dark horn, never metal. | No metal plate on torso. Absence of plate IS the signal. |
| Priest | Mixed cloth and light chainmail. Chainmail reads as small diamond pattern — visible at close distance, flattens to mid-grey at far. | Mace head blunt-capped, not bladed or pointed. Staff held centered and vertical. | No forward-angled posture. Priest idle is upright and symmetric. |

**Class accent tints (one region only per class):**

| Class | Accent Region | Tint |
|---|---|---|
| Warrior | Shoulder armor inner face | Subdued Threat Red `#8B2A22` — visible only when shoulder rotates toward camera |
| Rogue | Hood lining / inner cloak surface | Ash Mid shifted purple-grey `#4A4A5A` — visible on movement only |
| Mage | Cloth hem edge (lowest 15% of robe) | Skill Blue desaturated `#3D5070` — faint, reinforces MP color association |
| Priest | Staff ornament top or grip wrap | Vital Amber `#E8A020` at 0.6 opacity — only warm accent in the class set |

**Prop read rules (fastest class ID when silhouette is obscured):**

| Class | Prop Signal | Unique Feature |
|---|---|---|
| Warrior | Shield plane extends beyond shoulder width | Only class carrying a flat plane — the shield is the class identifier |
| Rogue | One arm angled forward, weapon tip exits the silhouette | Only class where the weapon tip crosses the body outline at idle |
| Mage | Staff extends above head height | Only class with a prop reaching above the head |
| Priest | Staff or mace held centered, parallel to body axis | Centered vertical axis distinguishes from Mage's vertical-with-head-above |

---

### 5.3 Enemy Archetype Design Rules

**Tier 1 — Trash Mob**
- Silhouette fits inside a 1.0× unit cube relative to player character height
- Material: flat, Metallic 0.1 or below, Ash Mid skin with no variation
- No emissive values at any base state
- Health bar: thin (50% of elite bar height), Ash Mid fill, Threat Red depletion
- Design rule: trash mobs must be clearly subordinate to the player in visual weight

**Tier 2 — Elite Mob**
- Silhouette extends at least 1.25× player height OR 1.5× player shoulder width
- One metallic element required (Metallic 0.5+) — signals "this was equipped, not born"
- One distinguishing prop that a trash mob variant does not carry
- On aggro: low-intensity emissive activates on metallic element — Enhancement Gold `#D4AF37` at intensity 0.6
- Health bar: full-width, single-border chamfered frame
- Must pass the 48×48px thumbnail test (Section 3.1)

**Tier 3 — Boss**
- Silhouette violates standard humanoid ratios in at least two axes (taller, wider, or deeper)
- One disproportionate body part communicates the attack type (oversized arms = melee, oversized chest/forward lean = charge, multiple limbs = AoE)
- Mixed materials: one flat section, one high-metallic section, one emissive region (the attack origin)
- Attack tell: emissive region ramps Threat Red `#C0392B` from 0 → intensity 2.5 over windup duration. Red glow = dodge window closing.
- Name plate: full-width bar above head, double-border chamfered frame
- Design rule: player encountering a boss for the first time must identify it as a boss (not an elite) within 2 seconds of entering the room

---

### 5.4 Expression and Pose Style Targets

**Vocabulary: Weighted Stylized** — deliberate, grounded, slightly over-held at extremes. Movements read as "this character has carried heavy things for a long time."

**Five rules:**
1. **Ground contact is primary.** Feet always weighted. No floaty idle cycles. Even the Mage has a planted stance.
2. **Minimal anticipation.** Strike animations use a 4–6 frame compressed load at 60fps, then fast release. Skilled fighters don't telegraph.
3. **Ease-out holds at attack extremes.** 3–4 frame hold at furthest weapon point before returning. This is where the hit reads.
4. **Idle breathing is chest-only.** Subtle chest rise (scale Y ~1.02, 0.6s cycle). Shoulders are still — shoulder bob generates silhouette noise in crowds.
5. **Collapse deaths, not ragdoll.** Baked collapse animations toward center of gravity. Ragdoll is a physics budget cost and visual noise problem in multi-enemy fights.

**Idle stance by class:**

| Class | Stance | Communicates |
|---|---|---|
| Warrior | Feet shoulder-width plus, weight equal, weapon at low ready angled toward ground | Patience. Has waited to fight before. |
| Rogue | Weight on back foot, front heel only raised, one arm extended forward | Readiness to move, not aggress |
| Mage | Feet closer together, slight lean back, staff hand low, off-hand raised slightly | Concentration — body still, hands ready |
| Priest | Fully symmetric, staff held two-handed at center, head slightly elevated | Authority and endurance |

---

### 5.5 LOD Philosophy

**Camera distance buckets:** Combat camera sits ~8–12 units above/behind player. Social hub pulls to 15+ units. Character occupies ~100–140px vertical screen space at combat distance on a 1080p landscape display.

| LOD | Trigger | Triangle Budget | Preserved | Eliminated |
|---|---|---|---|---|
| **LOD0 — Close** | 0–8 units | 800 tris max | Everything | Nothing |
| **LOD1 — Combat** | 8–15 units | 400 tris max | Class silhouette, weapon presence, enhancement glow region | Finger geometry, strap detail, secondary material layering |
| **LOD2 — Far** | 15–25 units | 150 tris max | Class silhouette dominant shape, enhancement glow if +4+ | All secondary props, material zone distinction |
| **LOD3 — Billboard** | 25+ units | Sprite only | Movement direction, class color accent | All 3D geometry — town background population only |

**Override rule:** Enhancement glow is never eliminated at any LOD range within draw distance. At LOD2, emissive is baked into albedo as a color tint: +4–+7 weapon region shifts toward `#D4AF37` at 30% blend; +8–+9 shifts toward `#F0EFE8` at 60% blend. Zero additional draw calls.

**Detail preservation priority when cutting triangles:**
1. Shoulder width ratio (Warrior vs. others)
2. Weapon type read (bladed vs. blunt vs. staff)
3. Shield presence on Warrior
4. Staff-above-head on Mage
5. Hood/helm read
6. Everything else — Items 1–3 survive to LOD2 non-negotiably; items 4–5 must survive LOD1.

---

### 5.6 Enhancement Visual Progression

**Threshold:** +4 is the first visible tier. +8 is the social broadcast threshold.

| Level | Visual Read | Social Signal |
|---|---|---|
| +0 to +3 | Dark metal, no glow. Metallic sheen on direct light only. | "A player." No power signal. |
| +4 | Gold edge-light on weapon and helm edge. Visible in peripheral vision in low-light zones. Not visible in bright town ambient. | "Some investment." Readable at close range in combat zones. |
| +5 to +6 | Gold edge-light extends to chest armor edge. Two emissive regions active. Readable in town at medium distance. | "Has been grinding." Peers at social distance notice. |
| +7 | Gold edge-light traces shoulder silhouette. Warm outline visible at combat distance. | "Meaningfully enhanced." Strangers stop to look — the +7 glow is aspirational. |
| +8 | White bloom initiates on weapon. 3–5 drifting spark particles constant at weapon tip. | "Near the ceiling." Visible at any draw distance. Silhouette appears to expand from bloom spread. |
| +9 | Full white bloom. 8–12 drifting sparks, bloom trails on movement. Enhancement badge `+9` in white on Panel Dark (colorblind backup). | The social broadcast. Walking through town is a visible event. No tooltip required. |

---

## 6. Environment Design Language

---

### 6.1 Architectural Style

**Reference frame: post-collapse military-industrial civilization.**

The world is built on the ruins of something that had discipline and scale — fortifications, foundries, aqueduct infrastructure, watch towers, carved stone roads. That civilization stopped functioning generations ago. Nobody is rebuilding it. Players fight through its remains.

**Cultural anchor:** Late Roman military architecture crossed with Central Asian steppe fortification. Heavy stone, load-bearing walls with no ornament, arched passages only where structurally demanded. Towers are functional watchtowers, not cathedral spires. Gates are portcullises and ram walls, not ceremonial arches.

**What this architecture must never look like:**
- Gothic cathedral (vertical emphasis, spiritual aspiration — wrong power register)
- Medieval European castle (tournament aesthetic — conflicts with gritty workmanlike tone)
- Fantasy dungeon with skull motifs (decorative danger signaling — the world does not perform its threat)

**The architecture communicates through absence:** Collapsed roofs, blocked corridors, sections that were once rooms and are now rubble pits. The player moves through something that used to be larger than it is now. Power was here and is gone. The player accumulates the power the architecture lost.

---

### 6.2 Zone Tier Visual Differentiation

Four axes only. A fifth creates noise that dilutes readability.

| Axis | Tier 1 (Entry) | Tier 2 (Mid) | Tier 3 (Endgame) |
|---|---|---|---|
| **Light temperature** | 5500–6000K (cool-neutral) | 4500–5000K (cooler, grey-blue shift) | 3500–4000K or 7500K+ (no middle ground) |
| **Ambient intensity** | 0.6 | 0.45 | 0.25–0.3 (black pools in corners) |
| **Structural integrity** | Damaged but mostly standing. Collapsed sections are navigable bypasses. | Partial collapse. Multiple areas inaccessible. Navigation requires routing around failures. | Near-total collapse. Original structure is a suggestion. |
| **Surface color** | Base palette only (Iron Ground, Ash Mid). Warm hues on reward items only. | Ash Mid surfaces gain cool-teal tint (`#4A5060`). Starts to look sick, not neutral. | Surfaces shift toward `#353840`. Floor transitions gain faint red-brown staining (`#3D2822`) — old blood, mineral deposit. A tint, not a statement. |

**Structural integrity is the primary read** — players feel it in routing difficulty and the visual weight of overhead geometry that looks like it may fall.

**Forbidden tier differentiators:** Adding more glowing hazards (glow = reward in the color system). Larger enemies as primary signal. New architectural styles per zone (the world is one ruined civilization; it degrades, it does not transform).

---

### 6.3 Texture Philosophy

**Choice: Flat-color base with painted detail pass** (hand-authored vertex color + one diffuse texture atlas per zone tier). Not PBR.

**Technical rationale:** PBR requires 3 texture samples minimum; flat-color halves texture memory and bandwidth. At 50–200 tri prop counts, normal maps have nothing meaningful to map to. Baked lighting works well on flat-color surfaces.

**Aesthetic rationale:** This read matches Knight Online / Diablo 2 — the target audience has pattern recognition for it. It reads as familiar, not cheap.

**Texture specifications:**

| Asset category | Resolution | Format | Mip maps |
|---|---|---|---|
| Zone terrain tile | 512×512 | Diffuse only | Yes (4 levels) |
| Structural props (walls, pillars) | 256×256 | Diffuse only | Yes (3 levels) |
| Foreground combat props | 128×128 | Diffuse only | No |
| Character bodies | 256×256 per character | Diffuse only | Yes (3 levels) |
| UI | 2048×2048 atlas | RGBA | No |

**Painted detail pass rules:** Maximum two shades from the base hex value (lighter top face, darker bottom face). Staining only at base of props and wall-floor junctions. No surface decals — use vertex color variation instead.

---

### 6.4 Prop Density Rules

**Every prop answers at least one of three questions:** Is this space safe to stand in? What happened here? Where do I go next? If a prop answers none of these, it does not exist.

**Density budget by zone type:**

| Zone type | Max props per 10×10m | Orientation rule |
|---|---|---|
| Transit corridor | 2 | Against walls only. Center floor clear. Lane 4m+ wide. |
| Farming field | 4 | Clustered at edges or as flanking islands. Concave center unobstructed. |
| Boss room / arena | 3 max | Cover anchors at cardinal quadrants only. Center floor is sacred. |
| Town / social hub | 8 | Higher density signals safety here. NPCs count as props. |

**Rule:** The less safe the space, the sparser the props.

**Prop reuse policy:** Same mesh across all zone tiers — material swap only. A Tier 3 barrel is the same mesh as a Tier 1 barrel with a darker base color and light staining. No new meshes per zone.

---

### 6.5 Environmental Storytelling

**Use evidence only — traces of activity by beings with goals.** No lore text, no pose-storytelling skeletons (scope trap), no decorative set dressing.

| Element | Communicates | Placement rule |
|---|---|---|
| Scattered weapons on ground | A fight ended here. The losers didn't retrieve them. | Farming areas only. 2–3 cluster, same model as enemy weapon type. |
| Extinguished fire pit (cold ash) | People camped here. They didn't come back. | Mid-zone waypoints. Max one per zone area. |
| Drag marks (vertex color groove in terrain) | Something large was moved. Recently. Groove shows direction. | Boss room approach only. Points toward boss spawn. |
| Barricade remnants (broken) | Someone tried to stop something. | Transition between safe and dangerous areas. Faces the dangerous side. |
| Scattered unlit torches | Infrastructure failed. | Tier 2 and 3 zones only. Tier 1 has working (baked) lights. |
| Intact supply crates | Someone expected to return. | Town and first tier only. |

**Hard limits:** No text on surfaces. Max 2 storytelling elements active in any single visible area.

---

### 6.6 Navigation Clarity

**The game has a minimap. The environment is designed as if it doesn't.**

Five navigation tools only:

1. **Light gradient toward objective** — objective area ambient always +0.15 intensity above zone average.
2. **Concave floor as destination signal** — boss rooms and objective areas use concave floor (shape rule from Section 3.2 doing double duty).
3. **Enemy patrol gaps** — patrol paths leave a gap in the direction of the objective. Players following minimum resistance route toward the correct direction.
4. **Architectural framing** — corridor walls and archways frame the objective in the distance. The thing to walk toward is centered or upper-third in any 20m forward view.
5. **Unique landmark prop** — one per zone transition and objective entrance. Used exactly once per zone. If a prop appears twice in the same zone, it fails as a landmark.

**What the environment must not do for navigation:** Floating objective markers embedded in world geometry (UI responsibility). Colored light sources as path indicators (color is reserved for combat and power state). Arrow shapes or directional geometry in terrain (breaks the "ancient indifferent world" tone).

---

## 7. UI/HUD Visual Direction

---

### 7.1 HUD Layout Philosophy

**Core rule:** The HUD is a combat instrument, not a dashboard. Every persistent element must answer one of three questions: How much HP do I have? What is my skill state? What is my threat level? Everything else is hidden until the player initiates.

#### Always-Visible Layer (Combat HUD)

```
┌──────────────────────────────────────────────────────┐
│ [Name / Lv.##]            [Target Frame]       [MAP] │
│ [████████████ HP ████████]                     [80px]│
│ [████ MP ████]                          [Party      ]│
│ [── EXP ───────────────────]            [frames     ]│
│                                         [stacked    ]│
│                                                      │
│  [◉ JOYSTICK  ] [▶auto]       [SK1][SK2]            │
│  [            ]               [SK3][SK4]  [AUTO-ATK] │
└──────────────────────────────────────────────────────┘
```

| Zone | Element | Position | Notes |
|---|---|---|---|
| Top-left | Player name + level badge | 16px from safe area edge | Name in Primary Text Medium, level badge in Bold |
| Top-left (below name) | HP bar + value label | Stacked under name | Full-width of left panel |
| Top-left (below HP) | MP bar | Stacked under HP, smaller height | |
| Top-left (below MP) | EXP bar | Stacked under MP, thinnest bar | No value label — progress only |
| Top-center | Target frame (enemy name + HP bar + level badge) | Below Dynamic Island / notch safe area, horizontally centered | Hidden when no target selected |
| Top-right | Minimap | 80×80px, 12px from safe area corner | Tap to open full map overlay |
| Top-right (below minimap) | Party frames | Stacked vertically, pill-shaped | Up to 4 members; HP bar only per frame |
| Bottom-left | Virtual joystick | 16px from safe area edge, 72px from bottom | Floating joystick preferred (finger-relative spawn) over fixed position |
| Bottom-left (adjacent) | Auto-run toggle | Pill shape, 44×28pt visual / 44×44pt tap target, above or beside joystick | One tap to engage / disengage; active state: `#4A9EE0` fill |
| Bottom-right | Skill buttons (2×2 grid) | 16px from right safe area, 72px from bottom | 56×56pt per button, 8pt gap |
| Bottom-right (beside skills) | Auto-attack toggle | Pill shape, 44×44pt | Persistent state indicator; active = `#4A9EE0` fill |

**Safe area compliance:** All elements inset by `SafeAreaInset + 8px` on all edges. On iPhone with Dynamic Island, top safe area inset reaches ~62px. Implement via `Screen.safeArea` at runtime — never hardcode pixel offsets.

#### Hidden-Until-Needed Layer

- **Inventory button**: Small chamfered icon button, top-right below party frames. Zero during combat flow.
- **Settings / Menu**: Collapsed behind a single icon, top-right corner.
- **Buff/debuff tray**: Slides in from right edge above skill bar when active buffs exist. Maximum 6 icons at 32×32px. Disappears when empty.
- **Loot notification**: Slides up from bottom edge, auto-dismisses after 3 seconds. Never blocks skills or HP.
- **Chat**: Collapsed to a single pill icon. Opens as translucent overlay; does not displace HUD elements.

**Thumb reach constraint (5-inch landscape):** Left thumb zone covers left 45% of screen width, bottom 60% of height — joystick, auto-run, HP/MP bars. Right thumb zone covers right 45%, bottom 60% — all 4 skills and auto-attack toggle. Top half of screen: low-frequency taps only (no action that must be triggered during active combat).

---

### 7.2 Typography Direction

**Personality:** Utilitarian. No serifs, no calligraphy. The game earns drama through stat numbers and enhancement results, not typeface style.

**Font stack:** Single grotesque sans-serif, two weights only.

| Weight | Use cases |
|---|---|
| **Medium (500)** | Body text, stat values, item names, HUD labels, button labels |
| **Bold (700)** | Damage numbers, enhancement results, level-up callouts, primary headings |

No light, thin, or italic variants. If a third weight is needed, solve it with color or size.

**Minimum sizes (iOS landscape, ~264 PPI at 5 inches):**

| Context | Minimum | Notes |
|---|---|---|
| HUD HP/MP value | 13sp | Must read during fast combat animation |
| Skill cooldown timer (on icon) | 11sp | Absolute floor |
| Item name in inventory row | 12sp | Medium weight |
| Damage number | 18sp base, 26sp crits | Bold only |
| Button label | 13sp | |
| Tooltip / description body | 11sp | Use sparingly |

---

### 7.3 Iconography Style

**Style: Outlined, single-weight 2px stroke, 44×44px canvas, 36×36px active area.**

**Construction rules:**
1. 4px padding on all sides (touch safe buffer, not drawn)
2. Stroke: 2px at 44px canvas. Do not scale stroke proportionally — keep 2px at all sizes
3. Fill: stroke-only by default. Interior fill only for active state (skill active: `#4A9EE0` fill; cooldown: stroke only at 40% opacity)
4. No gradients. Flat tint only.
5. Corner language: chamfered corners for container/panel icons, diamond silhouettes for alert icons
6. Gear slot icons: outline silhouette of item category at 32px, one internal landmark detail permitted at 64px+

**Readability test:** Every icon identifiable at 32×32px on `#1A1C1F` background on a physical 5-inch device.

---

### 7.4 Animation Feel

**Principle:** Fast in, faster out. UI never makes the player wait. Exceptions only for high-drama events.

| UI Event | Duration | Curve | Notes |
|---|---|---|---|
| Panel open | 180ms | Ease-out cubic | Slides up 24px + fade 0→1 |
| Panel close | 100ms | Linear fade | No slide on close |
| Button press | 80ms scale to 0.94, 80ms return | Ease-in-out | Scale punch only |
| Skill activated | 60ms scale to 1.12, 60ms return | Ease-out | Single bright flash frame |
| Cooldown completion | 200ms radial wipe inward | Linear | Reveals icon as cooldown expires |
| Damage number | 400ms total | Fast rise, ease-out, hold, fade | Rises 18px over 200ms, holds 100ms, fades 100ms |
| Critical hit number | 500ms | Spring, single bounce (damping ~0.6) | Scale 1.0→1.3→1.0 in first 150ms |
| Enhancement SUCCESS | 600ms | Ease-in-out, 200ms hold at peak | Panel flash `#4A9EE0` at 60% opacity + scale pulse 1.0→1.06→1.0 on item card |
| Enhancement FAIL | 400ms | Sharp ease-in, linear decay | Panel flash `#8B1A1A` at 40% opacity. No scale pulse. Intentionally distinct from success. |
| Loot notification | 160ms in, 3s hold, 100ms out | Ease-out in, linear out | Slides up 32px from bottom edge |

**What does not animate:** Static panels once open. Stat number updates (instant). HP/MP bar values (instant number change; bar fill animates, number does not lerp). Screen transitions (cut, not dissolve).

---

### 7.5 Inventory and Gear Screen Layout

**Layout paradigm:** Organized by equipment slot, not acquisition order.

**Landscape layout (40/60 split):**

| Left Panel (40%) | Right Panel (60%) |
|---|---|
| Character silhouette with tap-to-open slot overlays | Selected item card (large icon, name, stat delta) |
| Slots: Head, Chest, Legs, Weapon, Off-hand, Gloves, Ring ×2, Necklace | Compare row: Equipped vs. Selected, side-by-side stat diff |
| | Enhance button — chamfered rectangle, full right-panel width, bottom-anchored |

**Key rules:**
- Gear slot tap targets: minimum 52×52px chamfered tap target on silhouette. Empty slots show faint outline silhouette of expected item category.
- Item grid (below silhouette): scrollable, 4 columns, 64×64px cards, 6px gutter. Rarity by border color only — no rarity text labels in grid view.
- Stat comparison delta: Bold weight, `#4AE08A` improvements, `#E04A4A` downgrades, `#E8E6DF` unchanged.
- Enhancement: accessed from right panel only, never a separate screen. Result (success/fail flash) plays within the right panel and item card.

---

### 7.6 Touch Target Sizing

**Minimum:** 44×44pt (Apple HIG). Combat-critical elements during active play: **52×52pt minimum**.

| Element | Visual size | Tap target | Notes |
|---|---|---|---|
| Skill bar button | 56×56pt | 56×56pt | Visual = tap target; primary combat input |
| Auto-attack toggle | 44×44pt | 44×44pt | |
| Auto-run toggle | 44×28pt visual | 44×44pt | Invisible padding applied |
| Joystick | 100pt diameter | 120pt active zone | Floating spawn at finger-touch point |
| Minimap | 80×80pt | 80×80pt | |
| Buff icon | 32×32pt visual | 40×40pt | |
| Inventory button (HUD) | 32×32pt visual | 44×44pt | |
| Item card in grid | 64×64pt | 64×64pt | |
| Gear slot on silhouette | Variable | 52×52pt min | |
| Enhance confirm button | Full right-panel width | 52pt tall | High-stakes action; large target intentional |

**Inter-element spacing:** 8pt minimum gap between any two tappable elements. 12pt minimum between combat-critical elements (skills, auto-attack toggle) to prevent fat-finger misfires.

---

## 8. Asset Standards

### File Naming Schema

Pattern: `[category]_[name]_[variant]_[size].[ext]`
- All lowercase. Hyphens within name segments, underscores between fields.
- Example: `char_warrior_idle_lod0.fbx`, `ui_hpbar_fill_256.png`, `vfx_hit_melee_burst.png`

| Code | Category |
|------|----------|
| `char` | Player character |
| `enemy` | Enemy unit |
| `prop` | Environmental prop |
| `env` | Terrain / environment base |
| `ui` | UI element / sprite |
| `vfx` | Visual effect asset |
| `sfx` | Sound effect |
| `mus` | Music track |
| `mat` | Material |
| `tex` | Standalone texture |

---

### Polygon Budgets

| Asset | LOD0 | LOD1 | LOD2 | LOD3 |
|-------|------|------|------|------|
| Player character | 800 tri | 400 tri | 150 tri | Billboard |
| T1 enemy | 400 tri | 200 tri | 80 tri | Billboard |
| T2 enemy | 600 tri | 300 tri | 120 tri | Billboard |
| T3 boss | 1,200 tri | 600 tri | 240 tri | Billboard |
| Foreground prop | ≤50 tri | — | — | — |
| Mid-ground prop | ≤200 tri | ≤100 tri | — | — |

---

### Texture Budgets

| Asset | Max Resolution | Notes |
|-------|---------------|-------|
| Terrain | 512×512 | Tiled; flat-color base + painted detail |
| Structural prop | 256×256 | — |
| Foreground prop | 128×128 | — |
| Player character | 256×256 | — |
| UI sprite atlas | 2048×2048 | One atlas per screen category |

- **All iOS textures: ASTC compression.** No PVRTC (deprecated Unity 6.1).
- **No PBR.** No normal maps, no metallic/roughness maps. Flat-color base + painted detail pass only.

---

### Material Slots

| Asset | Max Material Slots | Emissive |
|-------|--------------------|----------|
| Player character | 2 | `MaterialPropertyBlock` at runtime — never baked |
| T1 enemy | 1 | — |
| T2 enemy | 2 | — |
| T3 boss | 3 | — |

Enhancement glow emissive is always applied via `MaterialPropertyBlock` at runtime, keyed to enhancement level. It is never baked into the texture or material asset.

---

### LOD Group Configuration

- **Characters**: Cross Fade mode. LOD transitions at screen-height percentages (tuned per zone camera distance — document in zone spec).
- **LOD3**: Always `BillboardRenderer`. Enhancement glow at LOD3 = gold or white color tint baked into the billboard albedo (zero additional draw calls).
- **Enhancement glow rule**: The glow signal must survive to LOD2 (emissive baked into albedo as a color tint) and LOD3 (billboard albedo tint). It is never fully eliminated at any LOD.

---

### VFX Particle Budgets

| Effect | Max Particles | Lifetime |
|--------|--------------|---------|
| Melee hit burst | 8 | ≤0.3s |
| Kill burst | 30 | ≤1.0s |
| Enhancement success | 40 | ≤1.5s |
| Weapon trail | 16 | Persistent while active |

- **Global simultaneous cap: 80 particles.** VFX system must enforce this ceiling.
- **Single particle atlas**: All particle sprites share one 256×256 ASTC texture atlas.
- **No Lit particle shaders.** All particles use Unlit/additive blending only.

---

### Audio Formats

| Type | Master Format | Runtime Format | Quality |
|------|--------------|----------------|---------|
| Sound effects | `.wav` | `.ogg` (Vorbis) | q7 |
| Music | `.ogg` (Vorbis) | `.ogg` (Vorbis) | q6 |

- **Sample rate**: 44,100 Hz | **Bit depth**: 16-bit
- **Positional audio (SFX)**: Mono
- **Music and ambient**: Stereo

---

### DCC Export Checklist

Before exporting from Blender, Maya, or equivalent:

- [ ] Apply all transforms (scale, rotation, location)
- [ ] Triangulate manually before export (do not rely on exporter auto-triangulate)
- [ ] Axis: Y-up, -Z forward
- [ ] Remove all cameras and lights from the export selection
- [ ] Export format: FBX Binary, scale factor 1.0
- [ ] Confirm polygon count matches LOD budget before handoff

---

## 9. Reference Direction

These references define specific, bounded domains. They are not full aesthetic inspirations — each reference owns exactly one domain of the visual language.

---

### Knight Online (2004) — Combat Feedback Domain

**Domain:** Damage number placement and legibility.

Damage numbers appear at the exact point of contact in world space, not anchored to screen space. They arc upward and fade in under 0.8 seconds. Numbers for the player's own hits are white; enemy hits to the player are red. Critical hits are larger (1.4× scale), not a different color — color is already reserved for team/threat distinction.

**What to copy:** World-space origin, upward arc, sub-second fade, scale differentiation for crits.
**What to discard:** Any decorative font styling, shadows, or glow on the numbers themselves — legibility only.

---

### Diablo 2 (2000) — Environment Tone Domain

**Domain:** Ground plane color as danger tier signal.

The ground surface color is the primary environmental cue for zone danger level. Warm, muted earth tones = early/safe zones. Dark grey-brown = mid zones. Near-black with red undertone = high-danger zones. This operates without UI text, without icons, and without player awareness — it is ambient and subconscious.

**What to copy:** The ground-to-danger mapping. The desaturation curve as difficulty increases.
**What to discard:** The isometric perspective, the darkness of interiors (we are outdoor/arena), the gothic horror palette — our aesthetic reads "post-collapse military," not "hell."

---

### Dark Souls III (2016) — Lighting and Atmosphere Domain

**Domain:** Single-source enemy lighting with directional hierarchy.

Enemy characters are lit with a single cool-temperature rim light from above-rear. Player characters receive a warm fill from the front. This creates an immediate team-identification read based on lighting temperature before the player reads the silhouette.

**What to copy:** The cool rear rim for enemies, the warm front fill for players. The rule that only one strong light source exists per character at a time.
**What to discard:** The environmental darkness, the lens flare and god-ray usage, the cinematic camera behavior.

---

### Warframe Early Era (2013–2015) — Power State Readability Domain

**Domain:** Two-phase enhancement glow (activation pulse → persistent edge-light).

When enhancement level increases, the weapon fires a single pulse bloom (600ms, wide) that resolves into a persistent edge-light at the new tier. The pulse communicates "something changed"; the persistent light communicates "current state." Both phases are necessary — the pulse without the persistent light loses the status read; the persistent light without the pulse loses the moment of change.

**What to copy:** The two-phase structure. The pulse-then-settle behavior. The rule that the persistent state must be readable at social distance.
**What to discard:** The sci-fi particle density, the neon color palette (ours is gold → white, not colored light), the full-body energy effects.

---

### Gustave Doré Engravings (1860s–1880s) — UI Language Domain

**Domain:** Value contrast hierarchy without color dependency.

Doré's engravings achieve extreme depth and hierarchy using only black and white — the darkest shadow is placed directly adjacent to the lightest highlight, creating sharp reads with no color cue required. The UI palette follows this rule: the highest-priority information element on any screen has the highest contrast ratio against its immediate background. No UI element earns prominence through color alone.

**What to copy:** The dark-adjacent-to-light rule for contrast hierarchy. The principle that hierarchy is legible in greyscale before color is applied.
**What to discard:** The engraving texture itself, the crosshatch style — this is a contrast principle, not an art style.
