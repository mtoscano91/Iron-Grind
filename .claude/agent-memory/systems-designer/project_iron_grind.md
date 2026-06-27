---
name: project-iron-grind
description: Core facts about Iron Grind — mobile MMORPG game currently in active GDD authoring
metadata:
  type: project
---

Iron Grind is a server-authoritative mobile MMORPG built in Unity 6.3 LTS (C#, URP, iOS primary).

**Why:** Indie project owned by Manuel Toscano (mtoscano@itba.edu.ar).

**How to apply:** All formula design must respect locked registry constants and the 3-layer modifier stack in CharacterStats. Always check entities.yaml before proposing values for cross-system facts.

Key constants locked in entities.yaml (2026-05-19):
- TICK_RATE_HZ = 20 (50ms per tick)
- MAX_PARTY_SIZE = 4
- MAX_PLAYERS_PER_ZONE = 50
- MAX_MOBS_PER_ZONE = 150 (placeholder)
- MAX_ENTITY_SLOTS = 200
- LEVEL_CAP = 60
- CharacterStats buff modifier capacity = 32 entries per entity (hard limit)
- MAX_STATS_PER_BUFF = 8 (max StatModifiers per BuffDefinition — in status-effects GDD)

Two classes: Warrior (DamageTank), Healer (Support).

GDD pipeline uses the GDD Revision Triad: GDD file + entities.yaml + session-state/active.md updated atomically.

Design review uses lean depth for targeted blocker fixes, full depth for first pass or structural rewrites.
