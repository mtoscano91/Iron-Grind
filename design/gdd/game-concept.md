# Game Concept: Project Iron Grind

*Created: 2026-04-19*
*Status: Draft*

---

## Elevator Pitch

> It's a mobile MMORPG where you grind monsters, master the auto-attack timing
> system, and risk your best gear in brutal upgrade attempts — in a world where
> a +9 weapon means everyone on the server knows your name.

---

## Core Identity

| Aspect | Detail |
| ---- | ---- |
| **Genre** | MMORPG / Action RPG |
| **Platform** | iOS (primary), Android (later) |
| **Target Audience** | Hardcore mobile gamers, MMORPG veterans, nostalgia-driven players 20–35 |
| **Player Count** | Multiplayer MMO — small instanced zones (10–50 players per zone) |
| **Session Length** | 20–60 minutes typical; grind sessions can extend 2+ hours |
| **Monetization** | TBD — cosmetic-only F2P strongly recommended (no pay-to-win power) |
| **Estimated Scope** | Large (18–24 months solo to Full Vision; MVP in 3–4 months) |
| **Comparable Titles** | Knight Online (2004), Risk Your Life, Curse of Aros |

---

## Core Fantasy

You are a warrior clawing your way up a server hierarchy through raw effort and
nerve. Every level was ground out. Every upgrade was a gamble that could have
destroyed your weapon. When you walk through a town with a +9 sword, other
players stop and look — because they know what it cost.

This game doesn't give you power. You earn it, risk it, and wear it visibly.

---

## Unique Hook

Like Knight Online AND ALSO every high-enhancement item is a server-wide social
signal — because item destruction on failure makes maxed gear genuinely rare,
a +8 or +9 weapon is immediately recognizable as proof of either grinding
mastery or extraordinary luck. Status in this game is worn, not bought.

---

## Player Experience Analysis (MDA Framework)

### Target Aesthetics (What the player FEELS)

| Aesthetic | Priority | How We Deliver It |
| ---- | ---- | ---- |
| **Sensation** (sensory pleasure) | 4 | Punchy hit feedback, satisfying loot sounds, visual upgrade flash |
| **Fantasy** (make-believe, role-playing) | 3 | Class identity, powerful gear visuals, server reputation |
| **Narrative** (drama, story arc) | N/A | No story — flavor text only |
| **Challenge** (obstacle course, mastery) | 1 | Auto-attack timing system, skill rotation, upgrade risk |
| **Fellowship** (social connection) | 2 | Party system, support roles, shared grinding zones |
| **Discovery** (exploration, secrets) | 5 | New zones, mob types, rare drops |
| **Expression** (self-expression, creativity) | 6 | Class choice, gear loadout, build optimization |
| **Submission** (relaxation, comfort zone) | 3 | Grind loop as meditative flow state |

### Key Dynamics (Emergent player behaviors)

- Players will learn and internalize the auto-attack timing window to maximize DPS
- Players will form parties for zones that feel unsafe or inefficient solo
- Players will track enhancement rates and share upgrade outcomes publicly
- Players will develop server economies around high-enhancement gear trades
- Experienced players will mentor newer ones to build party networks

### Core Mechanics (Systems we build)

1. **Auto-attack cadence combat** — melee auto-attacks fire on a fixed timer; skills activate between beats without canceling the cadence; timing mastery = higher DPS
2. **Class system with support roles** — damage, tank, healer, and buffer archetypes; support classes provide buffs/heals that make parties measurably stronger
3. **Enhancement system** — gear upgrades from +1 to a hard max; escalating failure chance at high levels; failure above a threshold destroys the item
4. **Instanced zone grinding** — 10–50 player zones with mob density tuned for solo or party play; party-exclusive elite zones with superior drop tables
5. **Party reward system** — XP throughput advantage (parties kill faster; each member earns slightly less XP per kill but kill rate more than compensates); party-only elite zones; support class buffs as mechanical party incentive. No item drop rate bonus — drop rates are fixed per mob type regardless of party size.

---

## Player Motivation Profile

### Primary Psychological Needs Served

| Need | How This Game Satisfies It | Strength |
| ---- | ---- | ---- |
| **Autonomy** (freedom, meaningful choice) | Class choice, gear priorities, solo vs. party decisions, which zone to grind | Supporting |
| **Competence** (mastery, skill growth) | Combat timing mastery; knowing optimal skill rotations; upgrade success rates | Core |
| **Relatedness** (connection, belonging) | Party bonds, guild membership, server reputation, economy participation | Core |

### Player Type Appeal (Bartle Taxonomy)

- [x] **Achievers** — Level milestones, enhancement ladder, rare gear collection — this is the primary audience
- [ ] **Explorers** — Minimal; new zones offer some discovery but the game isn't built for this
- [x] **Socializers** — Party grinding, guild play, server economy, server-famous gear
- [x] **Killers/Competitors** — Post-MVP: PvP zones will serve this type; even pre-PvP, server hierarchy through gear status scratches this itch

### Flow State Design

- **Onboarding curve**: Starting zone with forgiving mobs; no tutorial handholding — players learn by doing, classic MMO style
- **Difficulty scaling**: Zone tiers with clearly communicated level requirements; elite zones as party-gated difficulty spikes
- **Feedback clarity**: Level-up notification, drop sound cues, visible DPS numbers, upgrade result flash
- **Recovery from failure**: Death sends you back to nearest town (no item loss on death for MVP); upgrade destruction is the meaningful risk, not death

---

## Core Loop

### Moment-to-Moment (30 seconds)

Target a mob → auto-attacks fire on a fixed cadence (~1s for melee) → slot active skills between auto-attack beats to maximize DPS without wasting a tick → mob dies → loot drops → pick up → retarget. The skill ceiling is in *when* you cast, not how fast you tap.

### Short-Term (5–15 minutes)

Pull and clear a group of mobs in a zone → evaluate drops (vendor trash / upgrade material / gear) → manage HP/MP with consumables or party healer → pull the next group. With a party: faster clears, higher XP/hr through kill speed (each member earns slightly less per kill but the rate compensates), support buffs making everyone stronger. "One more pull" psychology.

### Session-Level (30–60 minutes)

Grind a zone toward a level threshold or farming a target item → return to town to sell, repair, visit upgrade NPC → attempt enhancements (high tension: item destruction risk) → decide whether to push a harder zone with a party. Natural stopping point: level up, major drop, successful upgrade, or item destroyed (emotional moment either way).

### Long-Term Progression

Level progression (cap TBD, likely 100+) → gear tier advancement (bronze → iron → steel → dark steel, etc.) → enhancement ladder (+1 to +10 max) → class advancement milestones → server reputation built through visible gear. The long-term goal is a maxed character known on the server.

### Retention Hooks

- **Curiosity**: What drops in the next tier zone? Can I survive the elite zone yet?
- **Investment**: That +6 weapon took 12 failed attempts to reach — you're not walking away
- **Social**: Your party needs a healer tonight; your guild is running the elite zone
- **Mastery**: Optimizing the auto-attack timing window; learning ideal skill rotation per class

---

## Game Pillars

### Pillar 1: Earned Power
Every point of power the player has was genuinely worked for. Gear upgrades are
hard, destructible, and unforgiving. Levels require real time investment. Nothing
is handed to the player.

*Design test*: If debating whether to add a shortcut purchase vs. keeping upgrades
grind-only, Earned Power says no shortcut — ever.

### Pillar 2: Rhythm Mastery
Combat rewards players who internalize the auto-attack timing system. Skills slot
between auto-attack beats. A skilled player on identical gear visibly outperforms
a new player. Mastery is observable and satisfying.

*Design test*: If debating whether a skill should interrupt auto-attacks for a
bigger effect vs. slotting cleanly between them, Rhythm Mastery says slot cleanly —
preserve the timing system above all else.

### Pillar 3: Social Gravity
Solo play is always viable. Party play is always better. The best content, the
fastest leveling — all reward grouping without ever forcing it. Players should
want to find each other. Drop rates are fixed per mob type; the party advantage
is throughput (kill speed → XP/hr) and elite zone access, not drop table
manipulation.

*Design test*: If debating whether a zone should be soloable at lower reward vs.
party-only with locked access, Social Gravity says soloable with clear
incentive to party — never lock out the solo player entirely.

### Pillar 4: Legendary Gear
High-enhancement items are rare, visible, and carry social weight. Because failure
destroys items above a threshold, a +9 weapon is a server event. Everyone knows
who has one. The gear economy is shaped by genuine scarcity.

*Design test*: If debating adding a "safe upgrade" token to reduce frustration vs.
keeping item destruction, Legendary Gear says keep destruction — scarcity and
risk are the entire point.

### Anti-Pillars (What This Game Is NOT)

- **NOT story-driven**: No main quest line, no cutscenes, no narrative arcs. Kill quests and fetch quests only. Story would dilute the grind focus and add scope.
- **NOT PvP-first (for MVP)**: PvP is explicitly post-MVP. Combat architecture should *allow* for it, but no PvP systems ship in MVP.
- **NOT hand-holding**: No step-by-step tutorials explaining every system. Beginner zones exist; the upgrade destruction mechanic is not soft-pedaled. Players learn by doing.
- **NOT visually ambitious**: Art budget goes to combat feedback (hit effects, upgrade flash, UI clarity) — not polygon count, not cinematic cutscenes.

---

## Inspiration and References

| Reference | What We Take From It | What We Do Differently | Why It Matters |
| ---- | ---- | ---- | ---- |
| Knight Online (2004) | Auto-attack + skill timing combat; gear upgrade with destruction; open zone grinding | Mobile-first design; instanced zones instead of open world; no PvP at MVP | Proves the core combat and upgrade loop has a passionate audience |
| Risk Your Life | Korean MMO gear grind; server hierarchy through visible power; class archetypes | iOS-first; lower art bar; party-focused over PvP-focused at launch | Validates upgrade destruction as a retention mechanic |
| Diablo 2 | Purposeful loot loop; rare items that feel genuinely rare; build depth | Persistent online world instead of single-player; mobile sessions | Proves loot scarcity + build investment drives long-term engagement |
| Curse of Aros | Stripped-down mobile MMO that found an audience with simple systems | 3D instead of 2D; deeper combat mechanics; higher stakes upgrades | Proves old-school mobile MMO can succeed without modern bloat |

**Non-game inspirations**: Korean PC bang culture of the early 2000s — the social energy of grinding together in the same room, server events becoming shared memories.

---

## Target Player Profile

| Attribute | Detail |
| ---- | ---- |
| **Age range** | 22–38 |
| **Gaming experience** | Mid-core to Hardcore |
| **Time availability** | 30–60 minute sessions on weekdays; longer on weekends |
| **Platform preference** | Mobile (plays during commute, lunch, evenings on the couch) |
| **Current games they play** | Curse of Aros, Albion Online Mobile, old Knight Online private servers |
| **What they're looking for** | An honest grind with real stakes — no energy systems, no gacha banners, no pay-to-win |
| **What would turn them away** | Pay-to-win upgrades; modern MMORPG quest bloat; tutorials that won't let them play |

---

## Technical Considerations

| Consideration | Assessment |
| ---- | ---- |
| **Engine** | Unity — best mobile tooling, Mirror or Unity Netcode for networking, strong asset store for low-poly art |
| **Key Technical Challenges** | Server-authoritative combat tick (auto-attack cadence synced across clients); item destruction must be server-side; real-time zone instancing; iOS App Store compliance |
| **Art Style** | Low-poly 3D with top-down or third-person camera; readable silhouettes over visual fidelity |
| **Art Pipeline Complexity** | Low — asset store base meshes modified and retextured; no custom animations at MVP |
| **Audio Needs** | Moderate — satisfying hit sounds and upgrade result audio are critical to feel; ambient zone music |
| **Networking** | Client-server with dedicated backend; Mirror (open source) or Unity Netcode for GameObjects; Photon for matchmaking/instancing is a viable option for MVP |
| **Content Volume (MVP)** | 1 zone, 1–2 classes, ~20 mob types, ~30–40 base items, ~10 skills per class |
| **Procedural Systems** | None — all zones hand-authored; loot tables are probability-based but not procedural |

---

## Risks and Open Questions

### Design Risks

- **Upgrade destruction may frustrate rather than engage** — if the destruction threshold is tuned wrong, players will quit instead of retry. Needs careful playtesting.
- **Auto-attack timing system may not translate to touch** — the mechanic was designed for mouse/keyboard. Touch input timing on mobile needs prototyping to confirm it feels as satisfying.
- **Solo viability vs. party incentive tension** — if solo is too good, nobody parties; if solo is too weak, casual players leave. Balance requires data.

### Technical Risks

- **Server infrastructure is non-trivial for a first project** — even small-scale MMO requires auth server, character DB, zone server, real-time combat sync. This is the highest technical risk.
- **Client-side prediction for auto-attack feel** — combat must feel responsive on mobile with variable latency. Getting this right requires networking expertise.
- **iOS App Store policies on online games** — real-money economy, loot mechanics, and server infrastructure all have App Store review implications.

### Market Risks

- **Critical player mass required** — empty servers kill the social fantasy. Launch strategy needs a concentrated server population, not spread across many servers.
- **"Old school" appeal may be niche** — the target audience exists but is smaller than modern casual mobile MMO audiences. Needs to be OK with a smaller, passionate base rather than mass market.

### Scope Risks

- **Networking adds 2–3x the scope** — every feature is harder with a multiplayer backend. First-time developer must budget for this realistically.
- **Content volume can spiral** — MMOs are content-hungry. The MVP must be ruthlessly scoped to one zone and prove the loop before adding content.

### Open Questions

- **Does the auto-attack timing feel good on touch?** — Answer with a single-player combat prototype in Unity before building any networking.
- **What's the right upgrade destruction threshold?** — Needs playtesting data; start with +7 as the danger zone and adjust.
- **Monetization model** — Cosmetic-only F2P is recommended for the target audience, but viability needs validation. No pay-to-win ever (violates Earned Power pillar).

---

## MVP Definition

**Core hypothesis**: *Players find the auto-attack timing combat and high-stakes item upgrade loop engaging enough to sustain 30+ minute sessions and return the next day.*

**Required for MVP**:
1. Auto-attack + skill timing combat system (server-authoritative, at least 2-player co-op)
2. One complete grinding zone with ~20 mob types and a level range
3. Item drop system with base gear (at least weapon + armor slots)
4. Enhancement/upgrade system with item destruction above a threshold
5. Party system (2–4 players) with XP throughput advantage (faster kills, diminishing per-member share per kill)
6. Basic character persistence (login, save character, return to world)
7. One damage class and one support class (healer or buffer)

**Explicitly NOT in MVP**:
- PvP of any kind
- Guild/clan system
- Auction house or player trading
- Multiple zones beyond the first
- More than 2 classes
- Chat beyond basic party chat
- Cosmetics or cash shop

### Scope Tiers

| Tier | Content | Features | Timeline (solo) |
| ---- | ---- | ---- | ---- |
| **MVP** | 1 zone, 2 classes, 20 mob types, 30 items | Auto-attack combat, enhancement with destruction, party (2–4), persistence | 3–4 months |
| **Vertical Slice** | 3 zones, 3 classes, 60 items | + Buffer class, NPC shop economy, world chat, level cap ~40 | 6–9 months |
| **Alpha** | 6 zones, 4 classes, 120 items | All core systems rough, class balance pass, level cap ~75 | 12–15 months |
| **Full Vision** | 10+ zones, 5+ classes, 200+ items, PvP zones | PvP, guild system, auction house, cosmetics, live service backend | 18–24 months |

---

## Visual Identity Anchor

**Selected direction**: Functional Combat Clarity

**One-line visual rule**: Every visual element must communicate combat state or power level — beauty is a side effect, not a goal.

**Supporting principles**:
1. *Readability over fidelity* — Enemy health bars, hit numbers, and character silhouettes must be instantly legible at mobile screen sizes. Design test: "Can I read this at arm's length?"
2. *Power must be visible* — High-enhancement gear should have a subtle visual difference (glow, particle, color shift) that is distinguishable but not gaudy. Design test: "Can an experienced player tell a +7 weapon from a +3 at a glance?"
3. *Hit feedback is the art* — The visual and audio response to hits, kills, and upgrade results is the primary sensory investment. Design test: "Does hitting this mob feel satisfying with sound off?"

**Color philosophy**: Muted base world palette (greys, browns, dark greens) with high-contrast UI and effect colors. Gear enhancement glow escalates from neutral → warm gold → bright white at max. The world is grim; power glows.

---

## Next Steps

- [ ] Run `/setup-engine` to configure Unity and populate version-aware reference docs
- [ ] Run `/art-bible` to create the visual identity specification (before writing any GDDs)
- [ ] Run `/design-review design/gdd/game-concept.md` to validate concept completeness
- [ ] Run `/map-systems` to decompose concept into individual systems with dependencies
- [ ] Author per-system GDDs with `/design-system` for each MVP system
- [ ] Run `/create-architecture` to produce the master architecture blueprint
- [ ] Run `/architecture-decision` for key technical decisions (networking stack, combat tick, upgrade server authority)
- [ ] Run `/gate-check` before committing to production
- [ ] Run `/prototype combat-timing` to validate auto-attack feel on touch before any networking work
- [ ] Run `/playtest-report` after prototype to validate the core hypothesis
