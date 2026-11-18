# Story 009: End-to-End — A Mob Kills a Player, the Player Respawns

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5–6 h (rig is larger than Mob Spawning Story 008's: real aggro, mob attacks, movement, status effects, outbox)
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — full T-1 → T-3 path; `enemy-ai.md` (mob attack resolution; CR-AI-6 target killed → re-scan excluding HP ≤ 0, else Returning / `pendingReturn`)
**Requirement**: `TR-dr-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: The tick order from Story 008 is what this test runs.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (real NavMesh in PlayMode; Navigation's HIGH risk is owned by that epic)
**Engine Notes**: PlayMode test assembly has `InternalsVisibleTo` on ServerLogic (ADR-012 Decision 5 amendment, Mob Spawning Story 008). `UnityNavMeshSampler` is main thread only (ADR-002) — fine in a `[UnityTest]`.

**Control Manifest Rules (this layer)**:
- Required: tests deterministic (seeded `IRandomProvider`, no wall-clock assertions)

---

## Readiness Decisions (2026-11-18)

Found at `/story-readiness`: the Mob Spawning Story 008 rig keeps the player alive on purpose (`NoCandidates` aggro source, `NoopResolver`, a fake zone state that pins the player 1 m behind the target mob, no movement), so it cannot be reused as is. `DeathMessageEmitter` takes a concrete `ClientMessageOutbox` and sends `DeathStateEntered` / `RespawnConfirmed` only to the player's own client id. `AutoAttackCombatSystem.GetState` shows `Prohibited` only when auto-attack is not Idle. The starting zone has no authored town point, so `TownRespawnPointCache` falls back to the bounds centroid and logs one `TownRespawnPointNotAuthored` warning (TD-112). The Zone0 mesh surface is at y ≈ 0.083.

## Dev-Story Decision (2026-11-18)

The first test run found a defect: the starting zone's bounds are ±50, so the centroid fallback is exactly `Vector3.zero`, which `DeathAndRespawnService` reads as "unresolved" (`DeathAndRespawnService.cs:269`: 40 retries, a critical log, then a late respawn at the origin). User chose to resolve TD-112 here: `ZoneDefinition_StartingZone.asset` now has an authored town point (-40, 0, -40) — on the Zone0 NavMesh, more than 40 m from every spawn point (max aggro 9 m, leash 16 m). The test uses the shipped definition unchanged; AC-3 asserts the provider returns that point; AC-5 expects no warnings. `ZoneInstancing_ZoneAssets_tests` asserts the authored point. The zero sentinel itself is unchanged (any future zone whose centroid is the origin would hit it unless its point is authored).

---

## Acceptance Criteria

- [x] **AC-1 Kill**: in the rig below (Story 008 tick order), a mob aggroes a player with low HP and its attacks bring HP to 0 → on that tick D the player is Dead (`TryGetDeathState`), `AutoAttackCombatSystem.GetState(player)` is `Prohibited` (auto-attack was turned on before the kill), and after the outbox flush the recording send seam holds one `DeathStateEntered` to the player's client id and one `EntityDied` to all clients.
- [x] **AC-2 Mob lets go**: from tick D + 1 until respawn, the counting damage output records no damage against the player; the mob that killed them reaches `Returning` within one attack cycle (wind-up + recovery ticks) + 1 tick after D (CR-AI-6: the only player is dead, re-scan finds no one), and `Dormant` before the respawn tick or by a stated tick limit set from the leash distance and return speed.
- [x] **AC-3 Respawn**: on tick D + `RespawnDelayTicks` exactly, the player is Alive; position XZ within 0.1 of `ITownRespawnPointProvider.GetTownRespawnPoint(zone)` and y within 0.1 of the mesh surface (provider value, not a literal); current HP == effective MaxHP and current MP == effective MaxMP; `GetState(player)` is no longer `Prohibited`; after flush the seam holds one `EntityRespawned` (all clients) and one `RespawnConfirmed` (player's client id).
- [x] **AC-4 Back in play**: after respawn the test places the player 1 m behind a chosen mob with `IMovementTeleport.TrySetAuthoritativePosition`, sets that mob as target and turns auto-attack on → at least one hit against the mob resolves within the player's attack cycle + 1 ticks.
- [x] **AC-5 Clean run**: across the run the problem capture records zero errors and zero warnings (the starting-zone town point is authored, see the Dev-Story Decision below); the Death & Respawn step's per-tick cost is logged (`[DeathAndRespawn009]`, max and p95; no threshold — advisory).

---

## Implementation Notes

- Start from `tests/PlayMode/MobSpawning/MobSpawning_EndToEnd_tests.cs` (real nav on the Zone0 mesh, pool, roster, bus, `MobRuntimeStore`, Enemy AI, Auto-Attack Combat, Character Stats, spawner via `MobSpawningZoneLoader.TryLoad` on the shipped definition), and replace the survival fakes with real parts, all in production code:
  - Aggro: `ZoneAggroCandidateSource(zone, pool, new MovementPlayerPositionReader(...), forwardingDeathReader)` — the forwarder (Story 008) bound to the service, so dead players are skipped.
  - Mob attacks: `MobAttackResolver(HitDetector, emitter, calculator, stats, output)` with the stand-in calculator from the Mob Spawning 008 test (`HpAwareCalculator` or equivalent; no crits — avoids the TD-110 crit-multiplier-0 error log) and a counting `IAutoAttackDamageOutput` wrapper that records the target of each applied hit.
  - Movement: `MovementService(new MovementIntentBuffer(...), new CharacterStatsMovementSpeedSource(stats), new UnityNavMeshSampler(), slotIndex)`; register the player through its entry listener so it has a position. The service uses it as `IMovementSystem` and `IMovementTeleport`.
  - Zone state: a source whose player position comes from `MovementService` (real `ZoneEntityStateSource` if its readers are buildable in PlayMode, otherwise a small fake that reads movement) — otherwise Combat and Enemy AI never see the respawn teleport.
  - Status Effects: the real `StatusEffectsService`.
  - Respawn point: `ZoneDefinitionSlotRespawnPointProvider(zone, slot)` on the shipped StartingZone definition (authored point (-40, 0, -40), TD-112 resolved).
  - Outbox: `new ClientMessageOutbox(recordingSendSeam)`; allocate the player slot with a known client id; assert messages by `MessageTypeId` after `Flush`.
  - Service: `DeathAndRespawnService` built as in `ZoneServerComposition.BuildDeathAndRespawn` (`CharacterStatsDeathSource`, a tick source over the rig's tick counter, `NoOpPartyMemberStatusSink`, `ZoneSlotGhostStatusSource`, a ghost-death sink fake that fails the test if called, `CharacterStatsRespawnVitalsWriter`, `DeathMessageEmitter`), stepped through `DeathAndRespawnPhaseStep` after Status Effects and before Movement.
- Placement: player HP set low (e.g. 1) via Character Stats, placed with the teleport inside one mob's `AggroRange`, far enough from other spawn points that only that mob aggroes.
- Determinism: fixed-seed `IRandomProvider` for spawn rolls (Enraged); no wall-clock assertions; tick limits derived from config values (wind-up, recovery, `RespawnDelayTicks`), not literals.
- Problem capture: reuse the `NavProblemCapture` pattern (warnings + errors); `LogAssert.NoUnexpectedReceived` trips on the PRNG seed info line.
- Record every stand-in in Completion Notes.

---

## Out of Scope

- Party behaviour (Party System epic)
- Client presentation
- Changing the `Vector3.zero` "unresolved" sentinel in the respawn provider contract
- Ghost death path (Story 010; the sink fake must stay uncalled here)

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-18.*

- **AC-1**: Player HP 1, auto-attack on (target any mob), one mob in aggro range, seeded RNG → record death tick D; assert Dead, `Prohibited`, one `DeathStateEntered` (player client) + one `EntityDied` (all clients).
- **AC-2**: Damage-output count against the player from D + 1 to respawn == 0; killer mob state `Returning` by D + windUp + recovery + 1; `Dormant` by the stated limit.
- **AC-3**: Tick D + `RespawnDelayTicks` − 1 still Dead; tick D + `RespawnDelayTicks` Alive; XZ vs provider point ≤ 0.1, y within 0.1 of mesh; HP == MaxHP, MP == MaxMP; not `Prohibited`; one `EntityRespawned` + one `RespawnConfirmed`.
- **AC-4**: Teleport behind a mob, target + auto-attack on → ≥ 1 hit on the mob within attack cycle + 1 ticks.
- **AC-5**: Capture: 0 errors, 0 warnings; `[DeathAndRespawn009]` line with max and p95.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/DeathAndRespawn/DeathAndRespawn_EndToEnd_tests.cs` — must exist and pass

**Status**: [x] Created 2026-11-18 — 5 `[UnityTest]`, one per AC; PlayMode filter `DeathAndRespawn|MobSpawning|EnemyAI|Navigation` 41/41 (18:44:17Z).

---

## Dependencies

- Depends on: Stories 006, 008 (Complete)
- Unlocks: None (last story of the epic)

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 5/5 passing, all automated (`tests/PlayMode/DeathAndRespawn/DeathAndRespawn_EndToEnd_tests.cs`); full EditMode `-testCategory "!Database"` 8094/8094.
**Deviations** (advisory):
- Stand-ins: no-crit damage calculator (`HpAwareCalculator`); counting damage output, recording hit-result emitter and counting damage applier (wrapping the real applier); zone state is the real `MovementZoneEntityStateSource` over a small inner fake (not `ZoneEntityStateSource`); pool slot-release listener not wired; `OnPlayerEntering` called by hand in place of `EntryCompletion`; ghost-death sink fake asserted uncalled.
- Downgraded to `Debug.Log` (impossible with shipped data): killer isolation by spawn gap (clusters 3 m apart, largest AggroRange 9 m; attacker == killer on every swing / hit up to D proves it instead); `dormantLimit` (188) < `RespawnDelayTicks` (140) (4x slack; the zero-swing window stays D+1..D+delay-1).
- Timing: the killing swing resolves in the Enemy AI phase of tick D; the service enters Dead synchronously from `OnEntityDied`, so `IsDead` is true at the end of D.
- Defect found and fixed (user decision): the starting zone's bounds centroid was exactly `Vector3.zero`, the service's "unresolved" sentinel (40 retries, critical log, respawn at origin). `ZoneDefinition_StartingZone.asset` now has an authored town point (-40, 0, -40); TD-112 resolved. Sentinel itself → TD-113.
- Out-of-list files (user-approved): `Assets/data/Zones/ZoneDefinition_StartingZone.asset`, `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_ZoneAssets_tests.cs` (asserts the authored point).
**Performance**: `[DeathAndRespawn009] DeathAndRespawnPhaseStep.Run` max 0.0157 ms, p95 0.0004 ms over 178 ticks (advisory, not asserted).
**Test Evidence**: Integration — `tests/PlayMode/DeathAndRespawn/DeathAndRespawn_EndToEnd_tests.cs`
**Code Review**: Complete (`/code-review` 2026-11-18: unity-specialist + qa-tester; all required + suggestions applied). Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
