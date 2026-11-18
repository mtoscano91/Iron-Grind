# Story 008: Zone Composition Wiring

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — Interactions with Other Systems, Dependencies
**Requirement**: `TR-dr-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order (also ADR-010, ADR-012)
**ADR Decision Summary**: The composition root lists every phase 3 step explicitly; services are built per zone with their dependencies injected; subscriptions are torn down with the zone.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#; the config is an existing `ScriptableObject` type).

**Control Manifest Rules (this layer)**:
- Required: services built and wired only in `ZoneServerComposition` (no singletons)
- Required: `Dispose` on zone teardown (ADR-010)

---

## Readiness Decisions (2026-11-18)

Found at `/story-readiness`: nothing loads a config asset at zone startup (Mob Spawning and Skill System use their defaults plus an internal test override); `ZoneSnapshotBuilder`, `ZoneCloseSteps` and `SessionEndCoordinator` are built inside `ZoneInstancingWiring` (`ZoneServerComposition.cs:2270`) before Auto-Attack, Movement and Status Effects (`:2381-2452`), which the service needs; the service has `Tick(long)` but is not an `IZoneSimulationStep`; the release listener composite keeps Combat last on purpose (`:2126`); the service has no enter hook (Story 007's release reset already makes a reused slot Alive); no `GhostExpiredEvent` wire message exists; the starting zone has no authored town respawn point (`_townRespawnPointAuthored: 0`). User decisions:

1. **Config**: an internal `DeathAndRespawnConfigOverride` (a `DeathAndRespawnConfig` asset, test seam like `MobSpawningConfigOverride` / `SkillSystemConfigOverride`). Null → `DeathAndRespawnConfigValues.Defaults` (not `FromAsset(null)`, which warns). An invalid override makes the service constructor throw `ArgumentException`; S5 fails the same way other S5 build failures do.
2. **Late-bound reader**: a new `ForwardingPlayerDeathStateReader : IPlayerDeathStateReader` (unbound → nobody is dead) is created before `ZoneInstancingWiring`, passed through `ZoneInstancingWiringOptions` to `ZoneSnapshotBuilder` and `ZoneCloseSteps`, and bound to the service once it is built (same pattern as `ForwardingZoneEntityStateSource`). `ZoneAggroCandidateSource` (`:2364`) and `ZoneEntityStateSource` (`:2206`) take the service directly if it exists when they are built, otherwise the forwarding reader.
3. **Tick step**: new `DeathAndRespawnPhaseStep : IZoneSimulationStep` calling `Tick` with the server tick; listed in `AddPhaseThreeSteps` after Status Effects and before Movement, with a comment: a player respawned on tick T is teleported and free to move on T, and targetable by Enemy AI / Auto-Attack on T.
4. **Town respawn point**: the starting zone keeps the bounds-centroid fallback for MVP (one `TownRespawnPointNotAuthored` warning); authoring a real point and checking EC-DR-4 is TD-112.
5. **Ghost-death hooks**: `broadcastGhostExpired`, `deTargetMobsOf` and `sendZoneSessionEnded` stay null, each with a comment (no `GhostExpiredEvent` message; mobs drop a target at HP 0; no client attached while Reconnecting). The HP source is `NoOpPreDisconnectHpSource`; the MaxHP fallback reads `ZoneCharacterStats` effective MaxHP (built before the wiring). Both are passed through `ZoneInstancingWiringOptions` to the `SessionEndCoordinator` constructor.

---

## Acceptance Criteria

- [x] **AC-1 Config**: with `DeathAndRespawnConfigOverride` null the service uses `DeathAndRespawnConfigValues.Defaults` and logs no config warning; with a valid override its values are used (e.g. a different respawn delay changes the respawn tick); with an invalid override (respawn delay 4.9 s) S5 fails, the zone does not reach Active, and the failure is logged (AC-DR-12 at the process level).
- [x] **AC-2 Built per zone**: `ZoneServerComposition` builds one `DeathAndRespawnService` per zone with: the config values, the zone slot pool, `CharacterStatsDeathSource`, `ServerTickDeathTickSource` (tick loop), `AutoAttackCombatSystem` (combat prohibition), `MovementService` (movement and `IMovementTeleport`), `NoOpPartyMemberStatusSink`, `ZoneSlotGhostStatusSource`, `SessionEndGhostDeathSink` (over `wiring.SessionEnds` and the pool), the zone's `ITownRespawnPointProvider` (the same one Movement gets), `CharacterStatsRespawnVitalsWriter`, Status Effects (`IStatusEffectsRespawnSink`) and `DeathMessageEmitter` (pool + `ClientMessageOutbox`). Exposed as an internal property for tests.
- [x] **AC-3 Tick placement**: `DeathAndRespawnPhaseStep` appears exactly once in the phase 3 list, after `StatusEffectsService` and before `MovementService` (Decision 3).
- [x] **AC-4 Readers wired**: `ZoneSnapshotBuilder`, `ZoneCloseSteps`, `ZoneAggroCandidateSource` and `ZoneEntityStateSource` all read dead-ness from the service (directly or through the bound forwarding reader, Decision 2). The service is in the pool's `CompositeZoneSlotReleaseListener`, before `AutoAttackCombat` (which stays last).
- [x] **AC-4b Ghost death wiring (Story 010)**: the composed `SessionEndCoordinator` gets `NoOpPreDisconnectHpSource` and the effective-MaxHP reader; the three optional hooks are null with comments (Decision 5).
- [x] **AC-5 Lifecycle**: zone teardown disposes the service (registered with the composition's disposables), so a player HP 0 after teardown changes nothing and throws nothing; a player whose slot is released while Dead leaves the slot Alive for the next player in it.
- [x] **AC-6 Composition test**: using the existing composition rig: a player in a composed zone has HP set to 0 → Dead on that tick; after the configured delay of ticks → Alive at the town point (centroid fallback for the starting zone) with full HP / MP. While dead: the zone snapshot entry has `isInDeadState = true` and the remaining ticks, the composed aggro candidate source does not offer the player, `ZoneEntityStateSource` reports the respawn tick, and a zone close saves the player with the HP 0 overload — one assertion per reader, so a reader left unwired fails the test.

---

## Implementation Notes

- Follow Mob Spawning Story 006's pattern (`MobSpawningPhaseStep`, built at S5, listed by `AddPhaseThreeSteps`).
- Build the service after Auto-Attack, Movement and Status Effects exist; bind the forwarding reader right after construction, before the zone reaches Active.
- Expect one `TownRespawnPointNotAuthored` warning in composition tests that use the starting zone definition (`LogAssert.Expect`), or use `RespawnProviderOverride`.
- The production ghost path stays unreachable until Networking Core marks zone slots disconnected (Story 007 note); wiring it now is still required.

**Performance**: the countdown step loops over the zone's player slots once per tick (O(MaxPlayers), MaxPlayers ≤ 64), with no allocations; reader calls are array lookups. No measurable cost against the 16.6 ms frame budget.

---

## Out of Scope

- Story 009: PlayMode end-to-end with a real mob kill
- A real Party System sink (Party System epic)
- Authoring the starting zone's town respawn point (TD-112)
- `GhostExpiredEvent` wire message, mob de-target command, production pre-disconnect HP source

---

## QA Test Cases

- **AC-1**: override null → Defaults, no warning (`LogAssert.NoUnexpectedReceived`); override with a 7 s delay → respawn tick = death tick + `ToTicks(7)`; override with 4.9 s → S5 fails, zone not Active, error logged.
- **AC-2**: the service property is non-null after S5; a kill reaches it (state Dead) through the composed death source.
- **AC-3**: phase list by type order: `StatusEffectsService` index < `DeathAndRespawnPhaseStep` index < `MovementService` index; step occurs once.
- **AC-4**: see AC-6 (one assertion per reader); release listener order: D&R before Auto-Attack Combat.
- **AC-4b**: a composed ghost death (slot set to `Disconnected_SessionActive` in the test) starts a ghost-death save with `hpOverride` = effective MaxHP.
- **AC-5**: teardown → HP 0 afterwards changes no state, no exception; release while Dead → next player in the slot is Alive.
- **AC-6**: as described; death tick and respawn tick exact; position equals the provider's point; HP / MP equal effective max.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Composition_tests.cs` — must exist and pass

**Status**: [x] Created and passing (2026-11-18)

---

## Dependencies

- Depends on: Stories 001–007, 010 (all Complete)
- Unlocks: Story 009

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 7/7 passing (AC-1 to AC-6 plus AC-4b), all automated (none deferred)
**Deviations** (advisory):
1. A zone with no town respawn provider (no definition slot and no `RespawnProviderOverride`) builds no service, lists no step, and logs one warning ("Death and Respawn not built…"); nobody is ever dead there. Production zones have a definition slot, so they always get the service. Two existing composition tests now expect that warning (`EnemyAI_Composition_tests.cs`, `ZoneServer_CompositionWiring_tests.cs`) — outside the listed files, required by the change.
2. The starting zone respawns at the bounds centroid with a `TownRespawnPointNotAuthored` warning (TD-112, Decision 4).
3. Ghost hooks (`broadcastGhostExpired`, `deTargetMobsOf`, `sendZoneSessionEnded`) are null by design (Decision 5).
4. AC-1 "no config warning" is asserted through the rig's problem capture (zero warnings/errors), not `LogAssert.NoUnexpectedReceived` (a startup PRNG info line trips it).
5. The snapshot builder, zone close and release-listener order also keep structural (field-identity) tests alongside the behavioural ones.
**Test Evidence**: Integration — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Composition_tests.cs` (18: config override, build, step order, readers by behaviour — snapshot bytes, zone-close stored HP, aggro, entity state — ghost-death stored MaxHP, release order, teardown, slot reuse, respawn on the exact tick), plus `DeathAndRespawn_ForwardingReader_tests.cs` (5). Full EditMode `-testCategory "!Database"` 8094/8094 (2026-11-18 18:17:34Z).
**Code Review**: Complete (`/code-review`: unity-specialist wiring correct, qa-tester R1–R4 behavioural gaps; all required + suggestions applied). Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
