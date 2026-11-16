# Story 008: Mob Runtime State and Stat Seeding (TD-102)

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
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

- [ ] **AC-1 Store**: `MobRuntimeStore` (`src/ServerLogic/EnemyAI/`), one per zone, built from the zone `ZoneID`, `MobEventBus`, `IMobDefinitionRegistry`, `IRandomProvider` and `IMobStatWriter`. Per mob `EntityID` it holds: committed position (`Vector3`), `FacingAngle` (`short`, degrees ×10), spawn point, `MobTypeID`, and the `MobSpawnVariant` (IsEnraged, EffectiveKillXP, TierShift). It subscribes to `OnMobSpawned` in the constructor and unsubscribes in `Dispose()`. API: `TryGet(EntityID, out MobRuntimeEntry)`, `Count`, `TrySetCommitted(EntityID, Vector3 position, short facing)` (the Story 009 writer; returns false and logs nothing for an unknown ID), `Remove(EntityID)` (the Story 011 caller).
- [ ] **AC-2 Spawn handler (stat seeding — the stats part of TD-102)**: on `MobSpawned` for this zone the handler looks up the definition, runs `MobSpawnSeeder.Seed` (Story 007), and only then adds the entry (position = `SpawnPosition` including Y, facing 0).
  - Unknown or refused `MobTypeID` (`GetDefinition` returns null) → one error, no entry, no stats; the roster entry stays (no `RemoveMob` inside the handler — later subscribers on the same raise, e.g. navigation, would act on a removed mob).
  - Spawn args for another zone → one warning, nothing changes.
  - Duplicate `MobSpawned` for an ID already in the store → one error, no re-seed, entry unchanged.
  - The seeder's `NextDouble` is consumed only on a successful seed (not for refused, foreign or duplicate spawns).
- [ ] **AC-3 Position source**: `MobRuntimeStore : IMobCommittedPositionSource`; `CopyCommittedPositions` writes exactly one entry per live mob and allocates nothing (test: `GC.GetAllocatedBytesForCurrentThread` delta 0 across 100 calls with 150 mobs, after a warm-up call). Wiring it into the composition is Story 013.
- [ ] **AC-4 Entity-state seam**: new `IZoneMobTransformReader` (`IronGrind.ServerLogic.ZoneServer`: `bool TryGetTransform(EntityID, out Vector3 position, out short facing)`) plus an adapter over the store. `ZoneEntityStateSource` takes it as an optional constructor argument (null = today's zeros, so existing tests are unchanged). When the reader has the mob, the view carries position and facing in wire units — the same conversion as `MovementZoneEntityStateSource` (centimetres, clamped to `WireFixedPointCodec.MaxPositionMeters`; rotation = yaw quaternion from `facing / 10` × `ROTATION_SCALE`); HP / MaxHP still come from Character Stats. A roster mob the reader does not have → zeros, still `true`. Integration test: a spawned mob reads its seeded `MaxHP` / `CurrentHP` and spawn position, including an Enraged roll (scaled values).
- [ ] **AC-5 Lookups**: `ZoneSkillPositionSource` over that `ZoneEntityStateSource` returns the store's position after `TrySetCommitted`, not the spawn point (test). Combat reads mob positions through the same `TryGetMobState` (via `MovementZoneEntityStateSource`, which passes mobs through) — a test confirms the decorator path returns the store's position.

---

## Implementation Notes

- `ZoneEntityStateSource` (`src/ServerLogic/ZoneServer/`) reads the roster today; give it the `IZoneMobTransformReader` seam rather than a reference to Enemy AI types.
- Character Stats is per zone (`ZoneStats`); the Story 007 `CharacterStatsMobStatWriter` already writes a mob `EntityID` without registration — reuse it.
- **Carry-forward from Story 007 code review (2026-11-13), resolved at readiness (2026-11-16):**
  - Mob `EntityID`s are never reused: `ZoneSlotPool.TryMintId` uses a counter that only goes up, so a stale MaxHP modifier on a reused ID cannot happen.
  - `CharacterStats` has no per-entity removal, so a despawned mob's stats entries stay in memory → note for Story 011 (TD candidate there), not this story.
  - Re-entrancy: the spawn handler is raised by spawning code, never from inside a `CharacterStats.OnStatChanged` handler; document this on the handler.
- `MobEventBus` calls `OnMobSpawned` handlers in subscription order: the store must subscribe before `EnemyAISystem` (Story 009) so controllers see seeded stats. Story 013 builds them in that order.
- `MobController` keeps its own `Position` / `FacingAngle`; the store holds the committed copies. Story 009 copies controller → store each tick via `TrySetCommitted`.
- Pre-size the store's dictionary to the zone's mob pool capacity; enumerate with `foreach` over `Dictionary` (struct enumerator, no allocation).
- Mob Spawning does not exist yet: tests build a `MobEventBus` and call `RaiseMobSpawned` directly, with `ScriptedRandomProvider` and `CharacterStatsFixture` (Story 007 helpers).

---

## Out of Scope

- Story 009: who writes the position each tick (`TrySetCommitted` callers), `EnemyAISystem`
- Story 011: despawn and removal callers; the Character Stats leak note
- Story 012: mob combat-state reader (`isInCombat` / `CycleTimer`) — the rest of TD-102
- Story 013: composition wiring, replacing `EmptyMobCommittedPositionSource` in `ZoneServerComposition`, updating the pinned test `test_composition_entity_state_source_serves_a_roster_mob_after_startup`, narrowing TD-102 in the register
- Mob Spawning epic: validating `MobTypeID` before spawning

---

## QA Test Cases

*Lean review: written at `/story-readiness` (2026-11-16).*

1. Spawn → entry with position = spawn position (Y kept), facing 0, spawn point, `MobTypeID`.
2. Spawn → MaxHP, AttackPower, Defense, CurrentHP seeded from the definition.
3. Enraged roll → scaled stats and `IsEnraged` true in the entry's variant.
4. Unknown type → one error, no entry, no stats written, no random value consumed.
5. Foreign zone → one warning, no entry, no stats, no random value consumed.
6. Duplicate spawn → one error, entry and stats unchanged, no random value consumed.
7. `Dispose` → a later raise adds nothing; second `Dispose` is harmless.
8. `TrySetCommitted` on an unknown ID → false, no log.
9. `Remove` then `TryGet` → false; `Count` drops.
10. `CopyCommittedPositions` → one entry per live mob with the committed positions.
11. `CopyCommittedPositions` with 150 mobs → 0 bytes allocated over 100 calls.
12. `ZoneEntityStateSource` with the reader → view position / facing / rotation in wire units, including clamp at the position limit.
13. `ZoneEntityStateSource` with a roster mob the reader does not have (and with a null reader) → zeros, `true`.
14. `ZoneSkillPositionSource` and the `MovementZoneEntityStateSource` decorator → the store's position after `TrySetCommitted`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_MobState_tests.cs` — must exist and pass

**Status**: [x] Created — 27/27 pass (2026-11-16)

---

## Dependencies

- Depends on: Story 007 (Complete)
- Unlocks: Story 009, Story 013

---

## Completion Notes

**Completed**: 2026-11-16
**Criteria**: 5/5 passing (none deferred)
**Deviations** (advisory):
1. `MobRuntimeEntry` lives in its own file and has a `WithCommitted` helper; behaviour as specified.
2. TD-102 stays open: the stats part is implemented here but only takes effect in production once Story 013 wires the store; Story 013 then narrows TD-102 to the mob combat-state reader (Story 012).
3. A re-spawn with the same ID after `Remove` would be accepted and seeded again; documented in `MobRuntimeStore` remarks. Moot because mob IDs are never reused.
4. `CharacterStats` has no per-entity removal, so a despawned mob's stats stay in memory; carried to Story 011 Implementation Notes.
**Test Evidence**: Integration — `tests/EditMode/EnemyAI/EnemyAI_MobState_tests.cs` 27/27; batch EditMode 7698 / 7569 passed / 0 failed / 129 skipped (2026-11-16).
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, all 7 suggestions applied (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
