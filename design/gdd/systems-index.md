# Systems Index: Project Iron Grind

> **Status**: Draft
> **Created**: 2026-04-19
> **Last Updated**: 2026-06-20
> **Source Concept**: design/gdd/game-concept.md

---

## Overview

Project Iron Grind is a focused mobile MMORPG built around four design pillars (game-concept.md): Earned Power (grind-only progression), Rhythm Mastery (auto-attack timing combat), Social Gravity (solo-viable, party-better play), and Legendary Gear (high-stakes item enhancement with destruction-based scarcity). The system set reflects this focus — every system either enables the core grind loop (kill → loot → enhance → return) or reinforces the social and persistence layer that makes the world feel alive. There is no story system, no exploration system, no crafting system. The scope is deliberately narrow: one zone, two classes, one upgrade ladder. All 35 systems identified are required for the MVP to be testable. Vertical Slice and beyond add content (zones, classes, items) to these same systems rather than introducing new ones. The highest-risk system is the Networking Core — server-authoritative combat tick is the foundational architectural decision that constrains everything else.

---

## Systems Enumeration

| # | System | Category | Priority | Status | Design Doc | Depends On |
|---|--------|----------|----------|--------|------------|------------|
| 1 | Auto-Attack Combat | Gameplay | MVP | Approved (reactive model, 2026-05-27) | design/gdd/auto-attack-combat.md | Damage Calculation, Hit Detection, Character Stats, Networking Core |
| 2 | Skill System | Gameplay | MVP | Approved (2026-05-28) | design/gdd/skill-system.md | Character Stats, Damage Calculation, Status Effects / Buffs, Class System, Leveling System |
| 3 | Damage Calculation | Gameplay | MVP | Approved (Pass 2 lean, 2026-05-15) | design/gdd/damage-calculation.md | Character Stats |
| 4 | Hit Detection | Gameplay | MVP | Approved (lean re-review, 2026-05-25) | design/gdd/hit-detection.md | Networking Core, Damage Calculation, Auto-Attack Combat |
| 5 | Status Effects / Buffs *(inferred)* | Gameplay | MVP | Approved (lean re-review, 2026-05-20) | design/gdd/status-effects.md | Character Stats |
| 6 | Enemy AI *(inferred)* | Gameplay | MVP | Approved (Pass 3 lean, 2026-05-25) | design/gdd/enemy-ai.md | Damage Calculation, Hit Detection, Navigation / Pathfinding, Status Effects / Buffs |
| 7 | Death & Respawn *(inferred)* | Gameplay | MVP | Approved (lean re-review #2, 2026-05-29; OQ-DR-1/2/6 all resolved) | design/gdd/death-and-respawn.md | Character Persistence, Zone Instancing, Auto-Attack Combat |
| 8 | Character Stats *(inferred)* | Core | MVP | Approved | design/gdd/character-stats.md | — |
| 9 | Class System | Core | MVP | Approved (2026-04-29) | design/gdd/class-system.md | Character Stats, Skill System, Leveling System |
| 10 | Leveling System *(inferred)* | Progression | MVP | Approved (2026-05-05) | design/gdd/leveling-system.md | Character Stats |
| 11 | Item Database *(inferred)* | Economy | MVP | Approved | design/gdd/item-database.md | — |
| 12 | Inventory System *(inferred)* | Economy | MVP | Approved (2026-05-17) | design/gdd/inventory-system.md | Item Database |
| 13 | Equipment System *(inferred)* | Economy | MVP | Approved (Pass 5 lean, 2026-05-22) | design/gdd/equipment-system.md | Item Database, Inventory System, Character Stats |
| 14 | Loot Table System | Economy | MVP | Approved (2026-05-17) | design/gdd/loot-table-system.md | Item Database |
| 15 | Enhancement System | Economy | MVP | Approved (Pass 4 lean, 2026-05-23) | design/gdd/enhancement-system.md | Item Database, Inventory System, Currency System |
| 16 | Movement System *(inferred)* | Core | MVP | Approved (Pass 2 lean, 2026-05-26) | design/gdd/movement-system.md | Networking Core, Character Stats, Zone Instancing |
| 17 | Zone Instancing | Core | MVP | Approved (lean re-review 2026-05-30; all 8 OQs resolved; CR-ZI-6 routing fix; 3 new ACs AC-ZI-18/19/20) | design/gdd/zone-instancing.md | Networking Core, Authentication, Character Persistence |
| 18 | Mob Spawning | Gameplay | MVP | Approved (lean review #1, 2026-06-11) | design/gdd/mob-spawning.md | Loot Table System (indirect), Zone Instancing, Enemy AI |
| 19 | Navigation / Pathfinding *(inferred)* | Core | MVP | Approved (Pass 3 lean, 2026-06-14; CR-NAV-15 stale default corrected — all 3-pass blockers closed) | design/gdd/navigation-pathfinding.md | Movement System, Zone Instancing, Enemy AI, Mob Spawning |
| 20 | Party System | Social | MVP | Approved (lean re-review 2026-05-17) | design/gdd/party-system.md | Networking Core, Zone Instancing, Leveling System |
| 21 | Party Chat | Social | MVP | Approved (lean re-review 2026-06-19) | design/gdd/party-chat.md | Networking Core, Party System |
| 22 | Currency System *(inferred)* | Economy | MVP | Approved | design/gdd/currency-system.md | — |
| 23 | NPC Shop *(inferred)* | Economy | MVP | Approved (2026-06-07 — 2 pre-implementation gates open before sprint: OQ-NS-4/6; ADR-001 accepted; OQ-NS-5/7 resolved 2026-06-07) | design/gdd/npc-shop.md | Currency System, Inventory System, Item Database |
| 23a | Consumable Use System *(inferred)* | Economy | MVP | Approved (lean re-review #3 2026-06-11 — all OQs resolved, 3 minor fixes applied, AC-CUS-32 added; 6 bidirectionality rows to verify before sprint) | design/gdd/consumable-use-system.md | Inventory System, Character Stats, Item Database, Character Persistence, Death & Respawn, Zone Instancing, Party System |
| 24 | Authentication *(inferred)* | Persistence | MVP | Approved (lean re-review, 2026-05-21) | design/gdd/authentication.md | Networking Core |
| 24-p1 | Auth Wire Messages *(primitive)* | Persistence | MVP | Draft (2026-05-20) | design/gdd/auth-wire-messages.md | Authentication, Networking Session Token |
| 24-p2 | Auth Sidecar IPC *(primitive)* | Persistence | MVP | Draft (2026-05-20) | design/gdd/auth-sidecar-ipc.md | Authentication |
| 25 | Character Persistence | Persistence | MVP | Approved (lean re-review, 2026-05-24) | design/gdd/character-persistence.md | Networking Core, Authentication, Character Stats |
| 26 | Networking Core | Core | MVP | Approved (Pass 2 lean, 2026-05-14) | design/gdd/networking-core.md | — |
| 26a | Networking Session Lifecycle | Core | MVP | Approved (Pass 9 lean, 2026-05-11) | design/gdd/networking-session.md | Networking Core |
| 26b | Networking Wire Protocol | Core | MVP | Approved (Pass 4 lean, 2026-05-12) | design/gdd/networking-wire-protocol.md | Networking Core |
| 26c | Networking Test Harness | Core | MVP | Approved (lean re-review, 2026-05-14) | design/gdd/networking-test-harness.md | Networking Core |
| 26d | Networking Session Token | Core | MVP | Approved (Pass 1 lean, 2026-05-14) | design/gdd/networking-session-token.md | Networking Session, Networking Wire Protocol |
| 26e | Networking Ghost Session | Core | MVP | Approved (Pass 3 lean, 2026-05-14) | design/gdd/networking-ghost-session.md | Networking Session, Auto-Attack Combat, Networking Wire Protocol, networking-ghost-character-state |
| 26e-p | Networking Ghost Character State *(primitive)* | Core | MVP | Approved (Pass 2 lean, 2026-05-15) | design/gdd/networking-ghost-character-state.md | Networking Ghost Session, Networking Session, Networking Core |
| 26f | Networking Message Criticality Contract | Core | MVP | Approved (lean re-review Pass 2, 2026-05-17) | design/gdd/networking-message-criticality.md | Networking Wire Protocol, Networking Core |
| 26g | Networking Channel Contract | Core | MVP | Approved (lean re-review, 2026-05-18) | design/gdd/networking-channel-contract.md | Networking Message Criticality, Networking Wire Protocol |
| 26h | Networking Relevance Filter | Core | MVP | Approved (lean re-review, 2026-05-19) | design/gdd/networking-relevance-filter.md | Networking Wire Protocol, Networking Channel Contract, Party System, Auto-Attack Combat |
| 26i | Networking OWL Compensation | Core | MVP | Approved (lean re-review Pass 2, 2026-05-18) | design/gdd/networking-owl-compensation.md | Networking Core (CR-NET-8), Networking Session, Networking Test Harness |
| 27 | Client-Side Prediction *(inferred)* | Core | MVP | Approved (lean re-review #2, 2026-06-15) | design/gdd/client-side-prediction.md | Networking Core, Movement System, Wire Protocol, Navigation/Pathfinding |
| 28 | Combat UI *(inferred)* | UI | MVP | Approved (lean re-review, 2026-06-20) | design/gdd/combat-ui.md | Auto-Attack Combat, Skill System, Character Stats |
| 29 | HUD *(inferred)* | UI | MVP | Approved (lean re-review Pass 2, 2026-06-20; 3 pre-implementation gates open before sprint: OQ-HUD-1/7/8) | design/gdd/hud.md | Character Stats, Party System, Zone Instancing, Auto-Attack Combat, Movement System, Party Chat |
| 30 | Inventory UI *(inferred)* | UI | MVP | Not Started | — | Inventory System, Equipment System, Item Database |
| 31 | Enhancement UI *(inferred)* | UI | MVP | Not Started | — | Enhancement System |
| 32 | Map / Minimap *(inferred)* | UI | MVP | Not Started | — | Zone Instancing, Movement System |
| 33 | Audio System *(inferred)* | Audio | MVP | Not Started | — | Auto-Attack Combat, Enhancement System |
| 34 | VFX System *(inferred)* | Audio | MVP | Not Started | — | Auto-Attack Combat, Enhancement System |
| 35 | Onboarding / Beginner Zone *(inferred)* | Meta | MVP | Not Started | — | Zone Instancing, Auto-Attack Combat, Class System |

---

## Categories

| Category | Description |
|----------|-------------|
| **Core** | Foundation systems everything else depends on — stats, networking, movement, instancing |
| **Gameplay** | Systems that make the game fun — combat, AI, skills, death |
| **Progression** | How the player grows — XP, leveling |
| **Economy** | Items, loot, currency, shop, enhancement |
| **Social** | Multiplayer interaction — party, chat |
| **Persistence** | Save state and continuity — auth, character save/load |
| **UI** | Player-facing displays — HUD, inventory, combat feedback |
| **Audio** | Sound, music, and visual effects |
| **Meta** | Outside the core loop — onboarding |

---

## Priority Tiers

| Tier | Definition | Target Milestone |
|------|------------|-----------------|
| **MVP** | Required for the core loop to be testable (1 zone, 2 classes, combat + enhancement + party) | 3–4 months |
| **Vertical Slice** | Adds content to MVP systems (3 zones, 3 classes, world chat) | 6–9 months |
| **Alpha** | Full content scope in rough form (6 zones, 4 classes, balance pass) | 12–15 months |
| **Full Vision** | PvP, guild system, auction house, cosmetics | 18–24 months |

*All 35 systems are MVP. VS/Alpha/Full Vision tiers add content to these systems, not new systems (except world chat, guild system, auction house, PvP — to be indexed when those tiers are scoped).*

---

## Dependency Map

### Foundation Layer (no dependencies)

1. **Character Stats** — base stat schema that all combat, progression, and UI systems read from
2. **Item Database** — pure data definitions; all item-touching systems reference this
3. **Networking Core** — infrastructure all multiplayer systems plug into
4. **Currency System** — simple numeric concept; economy and enhancement reference it

### Core Layer (depends on Foundation)

1. **Authentication** — depends on: Networking Core
2. **Damage Calculation** — depends on: Character Stats
3. **Leveling System** — depends on: Character Stats
4. **Inventory System** — depends on: Item Database
5. **Loot Table System** — depends on: Item Database
6. **Status Effects / Buffs** — depends on: Character Stats

### Core+ Layer

1. **Equipment System** — depends on: Item Database, Inventory System, Character Stats
2. **Character Persistence** — depends on: Networking Core, Authentication, Character Stats
3. **Hit Detection** — depends on: Networking Core, Damage Calculation
4. **Movement System** — depends on: Networking Core
5. **Skill System** — depends on: Character Stats, Damage Calculation, Status Effects / Buffs

### Feature Layer (depends on Core)

1. **Auto-Attack Combat** — depends on: Damage Calculation, Hit Detection, Character Stats, Networking Core
2. **Class System** — depends on: Character Stats, Skill System, Leveling System
3. **Enhancement System** — depends on: Item Database, Inventory System, Currency System
4. **Zone Instancing** — depends on: Networking Core, Authentication, Character Persistence
5. **NPC Shop** — depends on: Currency System, Inventory System, Item Database
6. **Consumable Use System** — depends on: Inventory System, Character Stats
7. **Client-Side Prediction** — depends on: Networking Core, Movement System

### Feature+ Layer

1. **Navigation / Pathfinding** — depends on: Movement System, Zone Instancing
2. **Mob Spawning** — depends on: Loot Table System (indirect via Enemy AI kill event), Zone Instancing, Enemy AI
3. **Death & Respawn** — depends on: Character Persistence, Zone Instancing, Auto-Attack Combat
4. **Party System** — depends on: Networking Core, Zone Instancing, Leveling System
5. **Enemy AI** — depends on: Damage Calculation, Hit Detection, Navigation / Pathfinding, Status Effects / Buffs

### Feature++ Layer

1. **Party Chat** — depends on: Networking Core, Party System

### Presentation Layer

1. **Combat UI** — depends on: Auto-Attack Combat, Skill System, Character Stats
2. **HUD** — depends on: Character Stats, Party System, Zone Instancing, Auto-Attack Combat, Movement System, Party Chat
3. **Inventory UI** — depends on: Inventory System, Equipment System, Item Database
4. **Enhancement UI** — depends on: Enhancement System
5. **Map / Minimap** — depends on: Zone Instancing, Movement System

### Polish Layer

1. **Audio System** — depends on: Auto-Attack Combat, Enhancement System (event hooks)
2. **VFX System** — depends on: Auto-Attack Combat, Enhancement System (event hooks)
3. **Onboarding / Beginner Zone** — depends on: Zone Instancing, Auto-Attack Combat, Class System

---

## Recommended Design Order

| Order | System | Layer | Agent | Est. Effort |
|-------|--------|-------|-------|-------------|
| 1 | Character Stats | Foundation | systems-designer | S |
| 2 | Item Database | Foundation | systems-designer | M |
| 3 | Networking Core | Foundation | network-programmer | L |
| 4 | Currency System | Foundation | systems-designer | S |
| 5 | Damage Calculation | Core | systems-designer | M |
| 6 | Leveling System | Core | systems-designer | S |
| 7 | Inventory System | Core | game-designer | S |
| 8 | Loot Table System | Core | systems-designer | S |
| 9 | Status Effects / Buffs | Core | systems-designer | M |
| 10 | Authentication | Core | network-programmer | S |
| 11 | Equipment System | Core+ | game-designer | S |
| 12 | Character Persistence | Core+ | network-programmer | M |
| 13 | Hit Detection | Core+ | network-programmer | M |
| 14 | Movement System | Core+ | gameplay-programmer | S |
| 15 | Skill System | Core+ | systems-designer | M |
| 16 | **Auto-Attack Combat** | Feature | systems-designer | L |
| 17 | Class System | Feature | game-designer | M |
| 18 | **Enhancement System** | Feature | systems-designer | M |
| 19 | Zone Instancing | Feature | game-designer | L |
| 20 | NPC Shop | Feature | game-designer | S |
| 20a | Consumable Use System | Feature | game-designer | S |
| 21 | Client-Side Prediction | Feature | network-programmer | L |
| 22 | Navigation / Pathfinding | Feature+ | ai-programmer | M |
| 23 | Mob Spawning | Feature+ | game-designer | M |
| 24 | Death & Respawn | Feature+ | game-designer | S |
| 25 | Party System | Feature+ | game-designer | M |
| 26 | Enemy AI | Feature+ | ai-programmer | M |
| 27 | Party Chat | Feature++ | network-programmer | S |
| 28 | Combat UI | Presentation | ui-programmer | M |
| 29 | HUD | Presentation | ui-programmer | M |
| 30 | Inventory UI | Presentation | ui-programmer | S |
| 31 | Enhancement UI | Presentation | ui-programmer | S |
| 32 | Map / Minimap | Presentation | ui-programmer | S |
| 33 | Audio System | Polish | audio-director | M |
| 34 | VFX System | Polish | technical-artist | M |
| 35 | Onboarding / Beginner Zone | Polish | game-designer | M |

*Effort: S = 1 session, M = 2–3 sessions, L = 4+ sessions.*

---

## Circular Dependencies

None found.

---

## High-Risk Systems

| System | Risk Type | Risk Description | Mitigation |
|--------|-----------|-----------------|------------|
| Networking Core | Technical | Server-authoritative combat tick is the defining architectural decision — getting the tick rate, authority model, and latency tolerance wrong cascades into all 15 dependent systems | Prototype the authoritative tick in isolation before designing any dependent system; do not couple combat design to networking assumptions until tick is proven |
| Auto-Attack Combat | Design | The timing window mechanic was designed for mouse/keyboard; it may not translate to satisfying touch input on mobile | Run `/prototype combat-timing` as a single-player build before writing networking; validate the feel hypothesis before investing in infrastructure |
| Client-Side Prediction | Technical | Mobile latency variance makes bad prediction implementations feel broken in a way that can't be tuned away post-launch | Keep prediction scope minimal at MVP (movement only); defer full combat prediction to VS if movement prediction alone proves sufficient |
| Enhancement System | Design | Destruction threshold set too low = players quit; set too high = no social gravity from rare +9 items | Start at +7 as the destruction threshold; instrument failure/quit correlation in early playtests; treat this as a tuning problem, not a design problem |
| Zone Instancing | Technical | Instance creation, player routing, and zone state management at 10–50 players is non-trivial server infrastructure for a first project | Prove a single static instance works before building dynamic instance creation; consider Photon or similar PaaS for MVP |

---

## Progress Tracker

| Metric | Count |
|--------|-------|
| Total systems identified | 38 |
| Design docs completed (all sections written) | 38 |
| Design docs approved (zero blockers, all triad files updated) | 38 |
| Design docs designed pending review | 0 |
| Design docs needs revision (lean re-review pending) | 0 |
| Design docs — MAJOR REVISION NEEDED (pending upstream contracts) | 0 |
| Design docs revised — pending lean re-review | 0 |
| Design docs in review (NEEDS REVISION) | 0 |
| **Approved docs** | Character Stats, Item Database, Currency System, Class System (2026-04-29), Leveling System (2026-05-05), Auto-Attack Combat (Complete), Skill System (2026-05-28), Damage Calculation (Pass 2 lean, 2026-05-15), Networking Core (Pass 2 lean, 2026-05-14), networking-session (Pass 9 lean, 2026-05-11), networking-wire-protocol (Pass 4 lean, 2026-05-12; wire schema additions 2026-05-17), networking-test-harness (lean re-review, 2026-05-14), networking-session-token (Pass 1 lean, 2026-05-14), networking-ghost-session (Pass 3 lean, 2026-05-14), networking-ghost-character-state (Pass 2 lean, 2026-05-15), party-system (lean re-review, 2026-05-17), inventory-system (lean re-review, 2026-05-17), loot-table-system (Pass 2 lean, 2026-05-17), networking-message-criticality (lean re-review Pass 2, 2026-05-17), networking-channel-contract (lean re-review, 2026-05-18), networking-owl-compensation (lean re-review Pass 2, 2026-05-18), networking-relevance-filter (lean re-review, 2026-05-19), status-effects (lean re-review, 2026-05-20), Authentication (lean re-review, 2026-05-21), Equipment System (Pass 5 lean, 2026-05-22), Enhancement System (Pass 4 lean, 2026-05-23), Enemy AI (Pass 3 lean, 2026-05-25), Movement System (Pass 2 lean, 2026-05-26), Death & Respawn (lean re-review #2, 2026-05-29), Zone Instancing (lean re-review 2026-05-30), NPC Shop (2026-06-07), Consumable Use System (lean re-review #3, 2026-06-11), Mob Spawning (lean review #1, 2026-06-11), Navigation / Pathfinding (Pass 3 lean, 2026-06-14), Client-Side Prediction (lean re-review #2, 2026-06-15), Party Chat (lean re-review 2026-06-19), HUD (lean re-review Pass 2, 2026-06-20), **Combat UI (lean re-review, 2026-06-20)** |
| **MAJOR REVISION NEEDED / Upstream contracts pending** | — |
| **Revised / Pending Lean Re-review** | — |
| **In Review / NEEDS REVISION** | — |
| **Draft/pending** | auth-wire-messages (Draft), auth-sidecar-ipc (Draft) |

---

## Next Steps

- [ ] Run `/design-system character-stats` — first in design order (Foundation)
- [ ] Run `/design-system auto-attack-combat` — validate the core hypothesis early (design order #16)
- [ ] Run `/prototype combat-timing` — single-player build to validate auto-attack feel on touch before networking
- [ ] Run `/design-review design/gdd/[system].md` after each completed GDD
- [ ] Run `/gate-check pre-production` when all MVP GDDs are authored and reviewed
