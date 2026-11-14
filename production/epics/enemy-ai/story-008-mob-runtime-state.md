# Story 008: Mob Runtime State and Stat Seeding (TD-102)

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Interactions (Entity State reads/writes), CR-AI-1, CR-AI-5; `design/gdd/navigation-pathfinding.md` (committed positions, phase 1)
**Requirement**: `TR-ai-002`, `TR-ai-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (phase 1 sync of committed positions); ADR-010 (`MobEventBus`)
**ADR Decision Summary**: Phase 1 syncs each agent to the mob's committed position; the server owns position integration. Mob lifecycle reaches Enemy AI only through the per-zone `MobEventBus`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (feeds the HIGH-risk navigation sync; pure data here)
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: subscribe in the constructor / `Initialize()` and unsubscribe at teardown — ADR-010
- Required: zero per-tick allocation on the hot path (reuse dictionaries; manifest performance guardrails)

---

## Acceptance Criteria

- [ ] `MobRuntimeStore` (one per zone): per mob `EntityID` → committed position, `FacingAngle` (`short` ×10), spawn point, `MobTypeID`, `IsEnraged`. Added on `MobSpawned` (position = spawn position, facing 0) and removed at despawn (Story 011).
- [ ] **Stat seeding on spawn (closes TD-102)**: the spawn handler runs Story 007's roll and seeding before anything else reads the mob; an unknown or refused `MobTypeID` → one error, no runtime entry, no stats (the roster entry stays — readiness: decide whether to `RemoveMob` it).
- [ ] Implements `IMobCommittedPositionSource.CopyCommittedPositions` (replaces `EmptyMobCommittedPositionSource`), no allocation per call.
- [ ] The zone entity-state mob view (`ZoneEntityStateSource.TryGetMobState`) reads position and facing from the store and HP / MaxHP from Character Stats; the pinned test `test_composition_entity_state_source_serves_a_roster_mob_after_startup` is updated (0 / 0 HP assertions become the seeded values) and TD-102 is marked RESOLVED (mob combat-state reader part: readiness decides whether Story 012 covers it).
- [ ] Combat and Skill target lookups that read mob positions get the store's position (not the roster spawn point).

---

## Implementation Notes

- `ZoneEntityStateSource` (`src/ServerLogic/ZoneServer/`) reads the roster today; give it a mob-state reader seam rather than a reference to Enemy AI types.
- Character Stats is per zone (`ZoneStats`); confirm a mob `EntityID` needs no registration before `SetBaseStat`.
- **Carry-forward from Story 007 code review (2026-11-13):** `MobSpawnSeeder.Seed` must not be called from inside a `CharacterStats.OnStatChanged` handler (re-entrant writes throw in Editor/Development, are dropped in release → partial stats), and must get a fresh or cleared mob `EntityID` (a stale MaxHP buff/equipment modifier on a reused ID changes the effective MaxHP that `SetCurrentHP` clamps to). Readiness: confirm how mob `EntityID`s are allocated and whether despawn clears their stats/modifiers.

---

## Out of Scope

- Story 009: who writes the position each tick
- Story 011: despawn and removal

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_MobState_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007
- Unlocks: Story 009
