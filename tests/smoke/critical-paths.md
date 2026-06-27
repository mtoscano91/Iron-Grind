# Smoke Test: Critical Paths

**Purpose**: Run these checks in under 15 minutes before any QA hand-off.
**Run via**: `/smoke-check` (which reads this file)
**Update**: Add new entries as core systems are implemented each sprint.

## Core Stability (always run)

1. Game launches to main menu without crash
2. New session can be started from the main menu
3. Main menu responds to all inputs without freezing

## Authentication

4. [Character login/selection flow — update when auth system is implemented]

## Core Mechanic

5. [Auto-attack loop fires correctly — update when combat system is implemented]
6. [Skill bar activates correct skill — update when skill system is implemented]

## Navigation

7. [Character navigates to tapped destination — update when NavMesh is integrated]

## Data Integrity

8. [Save completes without error — update when persistence is implemented]
9. [Load restores correct character state — update when persistence is implemented]

## Networking

10. [Zone join succeeds and character appears in zone — update when zone instancing is implemented]
11. [Another player's position replicates within 100ms — update when networking is integrated]

## Performance

12. No visible frame drops on target hardware (60fps target)
13. Memory does not grow unboundedly over 5 minutes of play

## Economy

14. [NPC shop purchase deducts gold and grants item — update when shop is implemented]
15. [Enhancement attempt updates character stats — update when enhancement is implemented]
