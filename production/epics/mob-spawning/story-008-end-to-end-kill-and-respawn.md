# Story 008: End-to-End — Kill a Spawned Mob, See It Respawn

> **Epic**: Mob Spawning
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — Overview / Player Fantasy (the field never runs out), F-MS-2 (cycle time = kill time + `DEATH_LINGER_TICKS` + respawn delay), CR-MS-4 full cycle
**Requirement**: `TR-ms-008` (end-to-end part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract
**ADR Decision Summary**: Navigation runs phases 1–2 before phase 3; agents are claimed on `MobSpawned` and released on `MobDied`; the real `NavMeshAgent` pool is exercised only in PlayMode.

**Also governed by**: ADR-010, ADR-014.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (real `NavMeshAgent` pool and baked NavMesh, as in the Enemy AI tick-budget fixture)
**Engine Notes**: PlayMode with the starting-zone NavMesh; follow `tests/PlayMode/EnemyAI/EnemyAI_TickBudget_tests.cs` and the Navigation PlayMode fakes for setup; batch PlayMode run with `-testFilter`.

**Control Manifest Rules (this layer)**:
- Guardrail: server tick budget — log the spawner step's cost over the run (no hard budget assert unless readiness sets one)

---

## Acceptance Criteria

- [ ] **AC-1 Populated on Active**: the built zone with the shipped starting-zone table (Story 007) has every table point Live after T-2, each with a claimed nav agent (Navigation registered count = point count).
- [ ] **AC-2 Kill**: a player auto-attacks a Common mob to death → XP awarded once, loot stand-in logs (TD-107), `MobEventBus.MobDied` raised once, the spawn point Dying.
- [ ] **AC-3 Corpse removal**: after `DEATH_LINGER_TICKS` the roster slot, the Enemy AI controller / store entry and the nav agent are all released on the same tick; `GetMobCount` drops by one.
- [ ] **AC-4 Respawn**: after the Common delay a new mob (new EntityID, same `MobTypeID`, authored position) is Live, Dormant, with a claimed agent; `GetMobCount` is back to the table count. Total cycle in ticks = kill tick + 30 + 60 (F-MS-2 at defaults).
- [ ] **AC-5 Many cycles**: killing a sequence of mobs (≥ 10 kills across at least two tiers) leaves no leaked roster slots or nav agents (counts equal the table count once every respawn has fired); no error logs.
- [ ] **AC-6 Close**: closing the zone mid-cycle (some Dying, some Respawning) → no spawn events after step 1; all agents released.

---

## Implementation Notes

- Reuse the Enemy AI tick-budget fixture setup (real nav, store, registry, `ZoneAggroCandidateSource`) or the full composition if it runs in PlayMode; developer's choice, recorded in the Completion Notes.
- Kills can be driven through the real Combat path (as in `EnemyAI_Composition_tests` kill test) or by setting HP 0 (CR-AI-13 path) for the bulk kills in AC-5; AC-2 uses the real Combat path.
- Log the spawner step's max / p95 tick cost as `[MobSpawning008]`.

---

## Out of Scope

- Client rendering of spawns / despawns (presentation epics)
- Loot drops (TD-107)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`.*

1. **Populated (AC-1)**: after T-2, roster count = table count = nav registered count.
2. **Kill (AC-2)**: real auto-attack kill of a Common mob → XP once, `MobDied` once, point Dying.
3. **Corpse (AC-3)**: at T+29 slot, controller, agent present; at T+30 all released; count − 1.
4. **Respawn (AC-4)**: at T+90 new EntityID at the authored position, Dormant, agent claimed; count restored.
5. **Soak (AC-5)**: 10+ kills over Common and Uncommon points, run until all respawns fire → counts equal, 0 `LogType.Error/Exception`.
6. **Close (AC-6)**: close with points Dying and Respawning → no `MobSpawned` after step 1; nav agents all back in the pool.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/MobSpawning/MobSpawning_EndToEnd_tests.cs` — must exist and pass (batch PlayMode)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006, Story 007
- Unlocks: None (epic complete)
