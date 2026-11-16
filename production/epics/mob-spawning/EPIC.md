# Epic: Mob Spawning

> **Layer**: Feature
> **GDD**: design/gdd/mob-spawning.md
> **Architecture Module**: Mob Spawning (`architecture.md` layer map, Feature; phase 3 game system of the ADR-002 tick), `src/ServerLogic/` (ADR-012)
> **Status**: In Progress
> **Stories**: 8 created 2026-11-16 — see Stories
> **Manifest Version**: 2026-11-06

## Overview

Mob Spawning is the server-authoritative runtime that keeps each Active zone populated. Level Design authors a `ZoneSpawnTable` per zone template (`SpawnPoint[]`: `SpawnPointID`, `MobTypeID`, `Position`, `RespawnDelayOverrideTicks`); at T-1 the table is loaded into a pre-allocated runtime `SpawnPoint` struct array (≤ `MAX_MOBS_PER_ZONE` = 150), and at T-2 every point is filled through `IZoneMobRoster.TryAddMob` on the same tick, with `MobEventBus.MobSpawned` raised after each success. Each point then cycles Live → Dying (on `MobEventBus.MobDied`, corpse linger `DEATH_LINGER_TICKS`) → Respawning (`RemoveMob` called, tier-based delay F-MS-1) → Live, falling back to a Deferred retry queue when the zone is at the mob cap. Zone teardown calls `CancelAllSpawnTimers(ZoneID)` (CR-ZI-12 step 1), after which nothing is added, removed or raised. Mob Spawning reads `MobDefinition` through Enemy AI's `IMobDefinitionRegistry` and never references Enemy AI directly; the two meet only on the per-zone `MobEventBus`. The player feels it as a field that is never empty, with the Bandicoot variants laid out from weak near the gate to strong at the edge.

## Scope (user decision 2026-11-16, `/create-epics`)

**Server plus data.** The Enemy AI epic decisions carry over: content is split by system (Enemy AI owns the `MobDefinition` roster; this epic authors the starting-zone spawn table), and client visuals / audio go to presentation epics.

What already exists (built by earlier epics):

| Piece | Where |
|-------|-------|
| `IZoneMobRoster` (`TryAddMob`, `RemoveMob`, `GetMobCount`) | `src/ServerLogic/ZoneInstancing/ZoneMobRoster.cs` |
| `MobEventBus` (`OnMobSpawned`, `OnMobDied`, raise methods) | `src/ServerLogic/ZoneInstancing/MobEventBus.cs` |
| `IZoneMobSpawnTimers` + call in teardown step 1; `LoggingZoneMobSpawnTimers` stand-in (to replace) | `ZoneCloseSteps.cs`, `ZoneInstancingWiring.cs` |
| `MobTier` on `MobDefinition`; MVP roster MobTypeIDs 1–4 (Bandicoot family, Common → Elite) | `src/ServerLogic/EnemyAI/`, `Assets/data/Mobs/MobRoster.asset` |
| Spawn point NavMesh validation (CR-NAV-13) | `src/ServerLogic/Navigation/SpawnPointValidator.cs` |
| T-2 entry point | `ZoneLifecycle.MarkActive` via `ZoneServerComposition` |

Gap this epic closes: nothing in production calls `TryAddMob` or `RaiseMobSpawned`, so production zones have no mobs.

Moved out:

| Item | Owner |
|------|-------|
| Spawn materialize effect, corpse fade, family spawn audio (GDD Visual/Audio Requirements) | VFX / Audio / Combat UI epics (presentation) |
| Boss / event spawn mechanic (OQ-MS-1) | Deferred; `RespawnDelayOverrideTicks` stays reserved, no boss points in the MVP table |
| The 16 roster mob types beyond MobTypeIDs 1–4 (GDD says 20) | Content work, not this epic |

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event and Messaging Architecture | Per-zone `MobEventBus`: Mob Spawning subscribes to `MobDied` (raised by Enemy AI after loot, note 2026-11-16) and raises `MobSpawned`; named-method subscriptions, cleared on teardown | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | The spawner's per-tick step gets an explicit place in the composition root's phase list | LOW |
| ADR-012: Server/Client Assembly Boundary | Written in `IronGrind.ServerLogic`; no client assembly references | LOW |
| ADR-002: NavMesh Service Execution Contract | Spawn points are validated against the NavMesh at zone load (CR-NAV-13); Navigation claims and warps an agent on `MobSpawned` | HIGH (owned by Navigation; this epic's code is plain C#) |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-16). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-ms-001 | `ZoneSpawnTable` data asset per zone template (`SpawnPointID`, `MobTypeID`, `Position`, `RespawnDelayOverrideTicks`); T-1 load rejects a table with more than `MAX_MOBS_PER_ZONE` points (`SpawnTableOverCapacity`, zone does not reach Active, no `TryAddMob`) and 150 loads (CR-MS-2, AC-MS-1) | ADR-012 ✅ |
| TR-ms-002 | Runtime `SpawnPoint` struct array pre-allocated at 150: `State` {Uninitialized, Live, Dying, Respawning, Deferred, Cancelled}, `CurrentEntityID`, `TimerTick`; transitions 1–8; ticked on the main thread with no yield between `GetMobCount` and `TryAddMob` (CR-MS-3, CR-MS-4) | ADR-014 ✅ |
| TR-ms-003 | T-2 population sweep in declaration order on the T-2 tick: `GetMobCount` before each `TryAddMob`, Live + `MobSpawned` on success, Deferred at cap (CR-MS-7, AC-MS-2, AC-MS-3) | ADR-010, ADR-014 ✅ |
| TR-ms-004 | `MobEventBus.MobDied` moves the matching Live point to Dying with `TimerTick = DEATH_LINGER_TICKS` (no `RemoveMob` yet); unknown EntityID and duplicate deaths are ignored (CR-MS-5, AC-MS-4, AC-MS-11, AC-MS-12) | ADR-010 ✅ |
| TR-ms-005 | Dying → Respawning at linger end: `RemoveMob` once, `CurrentEntityID` cleared, `TimerTick` = F-MS-1 (override if > 0, else `RESPAWN_DELAY_{COMMON,UNCOMMON,RARE,ELITE}_TICKS` by `MobTier`, data-driven, read once per transition) (CR-MS-9, F-MS-1, AC-MS-5, AC-MS-10) | ADR-012 ✅ |
| TR-ms-006 | Respawning → Live / Deferred at timer end; deferred `List<int>` (capacity 150) polled every `SPAWN_DENSITY_RETRY_INTERVAL_TICKS` with swap-remove; `TryAddMob` returning `EntityID.Invalid` → Deferred + `UnexpectedTryAddMobFailure`, no `MobSpawned` (CR-MS-6, AC-MS-6, AC-MS-7, AC-MS-8, AC-MS-13) | ADR-010, ADR-014 ✅ |
| TR-ms-007 | `CancelAllSpawnTimers(ZoneID)` replaces `LoggingZoneMobSpawnTimers`: every point Cancelled, deferred list cleared, no `RemoveMob` during teardown, `MobDied` unsubscribed, no `TryAddMob` / `RemoveMob` / `MobSpawned` after return (CR-MS-8, AC-MS-9, AC-MS-14) | ADR-010 ✅ |
| TR-ms-008 | Zone wiring: spawner built per zone with the zone's `MobEventBus`, `IZoneMobRoster` and `IMobDefinitionRegistry`; per-tick step placed in the phase list; T-2 sweep on `MarkActive`; starting-zone `ZoneSpawnTable` authored from roster MobTypeIDs 1–4 (depth gradient, clusters per CR-MS-2); end-to-end check that a player kills a spawned mob and it respawns | ADR-002, ADR-010, ADR-012, ADR-014 ✅ |

## Open Items for Story Readiness

1. **NavMesh validation vs the spawn table.** CR-NAV-13 (`SpawnPointValidator`) excludes off-mesh points at zone load and aborts below `MIN_VALID_SPAWN_FRACTION`; the Mob Spawning GDD does not mention it. Settle how the validated set feeds the runtime array (excluded points never spawn) and whether the 150 cap check runs before or after validation.
2. **Tick placement.** Which phase the spawner's countdown step runs in (ADR-014) and its order relative to Enemy AI, so a `MobSpawned` raised this tick and `MobRuntimeStore` seeding (subscribed before `EnemyAISystem`, Enemy AI Story 013 note) stay consistent.
3. **Corpse timing overlap.** Enemy AI despawns its own controller / store entry after `DEATH_LINGER_TICKS`; Mob Spawning calls `RemoveMob` after the same linger. Confirm both counters start on the same tick, and see TD-109 (mob stats never removed from Character Stats).
4. **Spawn table authoring.** Point count, positions and tier layout for the starting zone (GDD convention: clusters of ≥ 4 within 15 m, ~40% Common near town), within the zone's NavMesh.
5. **GDD propagation checklist** (GDD Open Questions, advisory): Zone Instancing `IMobDefinitionRegistry` dependency, Loot Table row, entities.yaml constants, systems-index Item Database reclassification.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/mob-spawning.md` are verified
- All Logic and Integration stories have passing test files in `tests/`
- All Visual/Feel and UI stories have evidence docs with sign-off in `production/qa/evidence/`
- `LoggingZoneMobSpawnTimers` is no longer used in production composition

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Spawn config and ZoneSpawnTable data](story-001-spawn-config-and-table-data.md) | Logic | Complete | ADR-012 |
| 002 | [Spawn point runtime core — T-2 fill and MobDied](story-002-spawn-point-runtime-core.md) | Logic | Complete | ADR-010 |
| 003 | [Per-tick timers, respawn delay and deferred queue](story-003-tick-timers-and-deferred-queue.md) | Logic | Complete | ADR-014 |
| 004 | [Teardown — CancelAllSpawnTimers](story-004-teardown-cancel-all-spawn-timers.md) | Logic | Ready | ADR-010 |
| 005 | [Zone load — cap and NavMesh validation](story-005-zone-load-validation.md) | Integration | Ready | ADR-002 |
| 006 | [Zone composition wiring](story-006-zone-composition-wiring.md) | Integration | Ready | ADR-014 |
| 007 | [Starting-zone spawn table content](story-007-starting-zone-spawn-table.md) | Config/Data | Ready | ADR-002 |
| 008 | [End-to-end — kill a spawned mob, see it respawn](story-008-end-to-end-kill-and-respawn.md) | Integration | Ready | ADR-002 |

Order: 001 → 002 → 003 → 004; 005 after 001 + 002; 006 after 003 + 004 + 005; 007 after 001 + 005; 008 after 006 + 007.

## Decisions (2026-11-16, `/create-stories`, defaults to revisit at readiness)

1. **Cap before NavMesh**: the 150 cap is checked on the authored table before `SpawnPointValidator` runs (Story 005; resolves Open Item 1's ordering question).
2. **Tick placement**: the spawner is the first game-system step of phase 3, after Navigation phases 1–2 and before Status Effects / Enemy AI (Story 006; Open Item 2).
3. **Corpse timing**: Story 006 AC-5 tests that `RemoveMob` and Enemy AI's despawn of the same corpse happen on the same tick (Open Item 3).
4. **Boss guard**: MVP tables refuse `RespawnDelayOverrideTicks > 0` (`BossSpawnPointNotSupported`) until OQ-MS-1 is decided; the override path is still unit-tested (Stories 001, 003).

## Next Step

Run `/story-readiness production/epics/mob-spawning/story-001-spawn-config-and-table-data.md`, then `/dev-story`.
