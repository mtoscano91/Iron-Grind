# Story 008: Zone Composition Wiring

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — Interactions with Other Systems, Dependencies
**Requirement**: `TR-dr-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order (also ADR-010, ADR-012)
**ADR Decision Summary**: The composition root lists every phase 3 step explicitly; services are built per zone with their dependencies injected; subscriptions are torn down with the zone.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: services built and wired only in `ZoneServerComposition` (no singletons)
- Required: `Dispose` on zone teardown (ADR-010)

---

## Acceptance Criteria

- [ ] **AC-1 Startup config**: the server loads the Death & Respawn config asset at startup; a validation failure (Story 001) logs and stops startup, as other config failures do (AC-DR-12 at the process level).
- [ ] **AC-2 Built per zone**: `ZoneServerComposition` builds `DeathAndRespawnService` with the zone's Character Stats, Auto-Attack Combat, Movement, Status Effects, `ITownRespawnPointProvider`, `CharacterStatsRespawnVitalsWriter`, Movement as `IMovementTeleport`, Status Effects as `IStatusEffectsRespawnSink`, slot pool, `ZoneSlotGhostStatusSource`, the Story 010 `IGhostDeathSink` adapter, outbound send seam / outbox, tick source and `NoOpPartyMemberStatusSink`.
- [ ] **AC-3 Tick placement**: the countdown step is in `AddPhaseThreeSteps` at a fixed, commented position. Proposed default (confirm at readiness): after Status Effects and before Enemy AI / Auto-Attack, so a player respawned this tick is targetable and can act from the same tick's combat step, and Status Effects' next-tick death cleanup (CR-SE-10) has already run on the tick after death.
- [ ] **AC-4 Readers wired**: the snapshot builder and teardown get the service as `IPlayerDeathStateReader` (Story 007); `ZonePlayerStateView.RespawnTick` is filled.
- [ ] **AC-5 Lifecycle**: player enter resets the player's `DeathState`; release clears it; zone teardown disposes the service (no `OnEntityDied` handling afterwards).
- [ ] **AC-6 Composition test**: an EditMode test using the existing composition rig: a player in a composed zone has HP set to 0 → Dead on that tick; after the configured delay of ticks → Alive at the town point with full HP / MP.

---

## Implementation Notes

- Follow Mob Spawning Story 006's pattern (`MobSpawningPhaseStep`, built at S5, listed by `AddPhaseThreeSteps`).
- Zone0 has no authored town respawn point (Mob Spawning Story 007 note). Readiness: decide whether to author one now (and re-check the spawn-table gradient and that no mob aggro reaches it, EC-DR-4) or accept the provider's centroid fallback for MVP.

---

## Out of Scope

- Story 009: PlayMode end-to-end with a real mob kill
- A real Party System sink (Party System epic)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1**: Config with 4.9 → startup reports failure and does not reach Active.
- **AC-3**: The phase list contains the step once, at the agreed index (assert by type order).
- **AC-5**: Teardown → `OnEntityDied` afterwards changes nothing; no exception.
- **AC-6**: As described; death tick and respawn tick exact.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Composition_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001–007, 010
- Unlocks: Story 009
