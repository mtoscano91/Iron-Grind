---
name: Project Iron Grind — game concept and QA context
description: Core facts about the active game project — Knight Online-style mobile MMORPG, relevant for QA planning and story review
type: project
---

Project "Iron Grind" is a Knight Online-style mobile MMORPG being developed with Unity 6.3 LTS targeting iOS (primary) and Android.

The Character Stats system is in active design/QA review. As of 2026-04-22, the Character Stats GDD has 28 edge cases and a partial AC list (26 ACs). The AC list has been adversarially reviewed and found NOT READY for sprint commitment due to 4 CRITICAL findings:

1. AC-09 (OnEntityDied exactly-once) is untestable without an event counter spec.
2. AC-13/AC-15 error contract is undefined (exception vs. return code vs. silent no-op).
3. AC-07 "same frame" is ambiguous between client render frame and server tick.
4. Formulas F-4, F-5, F-7, F-9, F-10 have zero corresponding ACs.

**Why:** The GDD was reviewed adversarially at the user's request to find testability gaps before sprint commitment — shift-left QA practice.

**How to apply:** When the Character Stats story comes up for sprint planning or story readiness, these 4 blockers must be resolved first. The AC list needs a traceability matrix mapping all 28 GDD edge cases to ACs.
