# Story 011: Zone Composition Wiring

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — Interactions table (all rows), CR-PS-5 (XP-ineligible notice), CR-PS-6
**Requirement**: `TR-party-012` (wiring), `TR-party-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: The composition root builds each service once per zone, registers inbound handlers before `Seal()`, and places each step in the explicit phase list; ADR-010 Tier 1 services are injected, not looked up.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — composition only, no engine API.

**Control Manifest Rules (this layer)**:
- Required: one composition root (`ZoneServerComposition`); internal override seams for tests
- Guardrail: added steps cost < 0.1 ms per tick at 4 parties of 4 (advisory; not an AC — measured in Story 012 or a profiling pass)

---

## Acceptance Criteria

- [x] **AC-1 Built once per zone**: `ZoneServerComposition` builds one `PartyRegistry` from `PartySystemConfigOverride` (new `internal PartySystemConfig` seam, via `PartySystemConfigValues.FromAsset`) or `PartySystemConfigValues.Defaults`, and exposes it internally (`PartyRegistry`). Every party consumer receives that same instance. An override that fails `Validate()` makes construction throw `ArgumentException` (S5 logs "startup failed at S5", as Death & Respawn). Party objects with teardown work are registered with `RegisterDisposable`.
- [x] **AC-2 Seams bound**: the registry-backed party objects replace every stand-in:
  - `PartyZonePresence(registry)` is `ZoneInstancingWiringOptions.Party` (entry / reclamation / close steps) and the presence inside `ZoneSkillCastPartySource` (both `NoPartyZonePresence`s gone from production composition);
  - `PartyMemberStatusSink(registry)` replaces `NoOpPartyMemberStatusSink` in `BuildDeathAndRespawn`;
  - ONE `PartyExperienceDistributor(new CharacterStatsExperienceSink(stats), registry, eligibility, config)` is the experience sink of both Auto-Attack Combat and the Skill System (`SkillDamageExecutionStep`);
  - the registry is the `IPartyService` for any loot object that takes one (production loot is `StandInLootTableService`, which takes none; no binding needed until real loot returns).
- [x] **AC-3 Requests registered**: before `Seal()`, `PartyInboundRegistration.Register(dispatcher, new PartyInviteClientRequestHandlers(invites))` and `PartyInboundRegistration.RegisterLeaveAndDisband(dispatcher, …)` put `PartyInviteRequest`, `PartyInviteResponse`, `PartyLeaveRequest` (0xE175) and `PartyDisbandRequest` (0xE176) in the dispatcher.
- [x] **AC-4 Party tick step in the phase list**: a new `PartyTickStep : IZoneSimulationStep` runs, in this order, `PartyInviteService.Tick(t)` → `PartyMembershipStep.ApplyQueued(t)` → `PartyPresenceStep.Poll(t)` → `PartyBroadcastStep.Flush()` → `PartyHealthStreamStep.Run(t)`. It is added in `AddPhaseThreeSteps` after the gateway link pump and before `enemyAI.IntegrationStep` (which stays last). An exception in one sub-step is logged and does not skip the rest of the tick's party sub-steps. So on tick T: dispatch (requests queue leave / disband, accept joins) → simulation (kills award XP before removal) → party step → outbound (`ZoneOutboundSteps` flushes the outbox and the R-U batch holding the health updates the same tick).
- [x] **AC-5 Listeners composed**: `PartyPlayerEntryListener(registry, pool)` is the last entry listener in `CompositeZonePlayerEntryListener` (after Movement and Skill spawn); `PartySlotReleaseListener(registry, invites, membership, presence)` is in `CompositeZoneSlotReleaseListener` after Death & Respawn and before Auto-Attack Combat, which stays last — also in the branch where Death & Respawn is not built.
- [x] **AC-6 XP-ineligible notice**: new `OutboxPartyXpIneligibleNoticeSink(registry, pool, outbox) : IPartyXpIneligibleNoticeSink` encodes `PartyXpIneligibleNotice` (0xE178, 4-byte body `partyId`) and enqueues it on `ClientMessageOutbox` for the character's client (character → entity via registry, entity → slot → client via pool; unresolved character → dropped, one warning per Notify). The composition builds `PartyXpIneligibleNotifier(registry, eligibility, sink)`, which installs itself as the registry's leader-change listener.
- [x] **AC-7 No drop bonus (AC-PS-16)**: a structural check that the loot drop path carries no party-size modifier: the drop-roll parameters / `LootTableService` drop computation have no party bonus field or argument (`LootDropDistributor`'s party size only routes Rare items to auction and is excluded by name in the test).
- [x] **AC-8 Existing tests**: existing tests still pass. Tests that referenced the stand-ins (`NoPartyZonePresence` / `NoOpPartyMemberStatusSink`) keep doing so only where they build those objects themselves; any composition assertion that pinned a stand-in is updated with a one-line note. Files to check: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeathEntry_tests.cs`, `tests/EditMode/DeathAndRespawn/RealDeathRig.cs`, `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_EntryCompletion_tests.cs`, `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Wiring_tests.cs`, `tests/EditMode/SkillSystem/SkillSystem_CastOutput_tests.cs`, `tests/EditMode/SkillSystem/SkillSystem_Composition_tests.cs`, `tests/PlayMode/DeathAndRespawn/DeathAndRespawn_EndToEnd_tests.cs`.

---

## Implementation Notes

- **Construction order (readiness 2026-11-19, G5):** the registry needs only its config, so build it at the start of `BuildOutboundAndPipeline`, before `BuildZoneInstancing` — every consumer (wiring options, Combat, Skill, Death & Respawn) then takes it directly; no forwarding seam is needed. Build the rest once the pool and outbox exist (after `BuildZoneInstancing`): level source, eligibility, sinks, invites, broadcast, membership, presence, health stream, notifier.
- **Inputs:**
  - Stat reads: `CharacterStatsZoneEntityStatReader(_owner.ZoneCharacterStats)` for both `StatReaderPartyMemberLevelSource` and `PartyHealthStreamStep`. (`ZoneEntityStateSource` is an `IZoneEntityStateSource`, not an `IZoneEntityStatReader` — the Story 010 note was wrong.)
  - Names for invites: `_owner.PersistenceService` (`ICharacterNameLookup`).
  - Session state: `SessionMachinePartySessionStateSource(wiring.Pool, _sessionMachine)`.
  - Clients: `PoolPartyClientResolver(registry, wiring.Pool, outbox)`; R-U collector: the `RUBatchOutboundStep`; capacity: `wiring.Pool.MaxPlayers`.
  - Sinks: `OutboxPartyInviteMessageSink`, `OutboxPartyStateMessageSink`, `OutboxPartyXpIneligibleNoticeSink`, all over `(registry, wiring.Pool, outbox)`.
- **Tick order (G2):** the pipeline is completion → relay → gateway link → dispatch → simulation → outbound and has no hook before dispatch. The earlier note "invite `Tick` → dispatch" is replaced by the order in AC-4 (Story 007's hand-off already said "after dispatch"). Add an ADR-014 note recording the party step position.
- **Same-tick rule (Story 005, G3):** `ApplyQueued` after phase 3, so a kill on tick T awards XP before a leave queued on T removes the member — satisfied by AC-4.
- Expose internally for tests: `PartyRegistry`, `PartyTickStep`, `PartyExperienceSink` (the distributor). Follow Death & Respawn Story 008 for the override seam and step-in-list pattern.
- Keep `PartyTickStep` thin: no logic beyond the five calls and per-sub-step try / catch with one log line naming the sub-step.

---

## Out of Scope

- Story 012: end-to-end behaviour (two clients invite, join, kill, leave)
- Real loot drops and the `IPartyService` binding into `LootTableService` (moved out of the epic)
- Measuring the < 0.1 ms guardrail
- TD-114 (Loot-side skip of an ineligible member at the cursor)

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-19. Rig: the existing composition rig pattern (`SkillSystem_Composition_tests` / Death & Respawn Story 008).*

- **AC-1**: composed zone → `PartyRegistry` non-null; the same instance is reachable from `PartyZonePresence`, the D&R status sink, the distributor and the tick step (reference equality via internal accessors); override with a valid asset → registry `MaxPartySize` matches the asset; invalid override → S5 fails with the logged ArgumentException; build counted once.
- **AC-2**: wiring party is `PartyZonePresence`; `ZoneSkillCastPartySource` presence is `PartyZonePresence`; D&R status sink is `PartyMemberStatusSink`; Combat and Skill experience sinks are the same `PartyExperienceDistributor` instance (`Assert.AreSame`).
- **AC-3**: dispatcher has handlers for all four message ids after `Seal()`.
- **AC-4**: simulation step list contains exactly one `PartyTickStep`, index = link pump index + 1, and `IntegrationStep` (when built) is last; behavioural: a fake / recording run shows sub-step order Tick → ApplyQueued → Poll → Flush → Run; a throwing sub-step logs once (`LogAssert`) and the later sub-steps still run.
- **AC-5**: release composite order: … D&R → Party → Combat (last); also with D&R not built; entry composite: Party last.
- **AC-6**: sink unit tests: known character → outbox holds one 0xE178 message for that client with body = partyId; unknown character → nothing enqueued + one warning; composition: registry's leader-change listener is the notifier.
- **AC-7**: reflection over the loot drop parameters / `LootTableService` drop method signatures: no member named like `*Party*Bonus*` / `*PartySize*` besides the documented auction routing.
- **AC-8**: full EditMode `-testCategory "!Database"` green; PlayMode D&R end-to-end green.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_Composition_tests.cs` — must exist and pass (plus sink tests for AC-6, may live in the same file)

**Status**: [x] Created — 26 tests, all passing (2026-11-19)

---

## Readiness Decisions (2026-11-19)

- G1: XP-ineligible notice adapter added as AC-6 (Story 003 hand-off).
- G2: party sub-steps in one `PartyTickStep` after the link pump, before the integration step (AC-4).
- G3: stat reader is `CharacterStatsZoneEntityStatReader`.
- G4: `PartySystemConfigOverride` is new in this story.
- G5: registry built first, no forwarder; one distributor for Combat and Skill.
- G6: release composite: party after D&R, before Combat; entry: party last.
- G7: AC-7 structural loot check; AC-8 file list; estimate 5 → 6 h.

---

## Dependencies

- Depends on: Stories 003, 005, 007, 008, 009, 010 (all Complete)
- Unlocks: Story 012

---

## Completion Notes

**Completed**: 2026-11-19
**Criteria**: 8/8 passing, all automated (AC-8: full EditMode `-testCategory "!Database"` 8612/8612, 0 `error CS`)
**Test Evidence**: Integration — `tests/EditMode/PartySystem/PartySystem_Composition_tests.cs`, 26 tests
**Code Review**: Complete (`/code-review` 2026-11-19, unity-specialist + qa-tester; R1–R3 and suggestions applied). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.

**Implementation**
- `ZoneServerComposition`: `PartySystemConfigOverride` seam; `BuildPartyCore` (registry, presence, level source, eligibility, one `PartyExperienceDistributor`) before `BuildZoneInstancing`; `BuildPartyWiring` (sinks, invites, broadcast, membership, presence, health stream, notifier, both registrations, listeners, tick step) after Death & Respawn; stand-ins replaced (wiring `Party`, `ZoneSkillCastPartySource`, D&R status sink, Combat + Skill XP sink).
- New `PartyTickStep` (invite `Tick` → `ApplyQueued` → `Poll` → `Flush` → health `Run`; one catch per sub-step, first failure per sub-step logged) after the gateway link pump, before `MobPositionIntegrationStep`. ADR-014 note 2026-11-19.
- New `OutboxPartyXpIneligibleNoticeSink` (0xE178, never throws, unresolved client dropped silently: a member in disconnect grace has no client).

**Deviations / notes (advisory)**
- Out of story file list, valid: internal read-only test seams `PartyRegistry.LeaderListener`, `CompositeZoneSlotReleaseListener.Listeners`, `CompositeZonePlayerEntryListener.Listeners`, `InboundRequestDispatcher.IsRegistered`; AC-8 pins updated with notes in 6 older tests (EnemyAI, MovementSystem, StatusEffects, Death & Respawn, ZoneServer CompositionWiring, SkillSystem composition).
- AC-6: the unknown / unresolved character path is silent (readiness text said one warning); changed at code review because disconnect grace is an expected case.
- Not tested: outbox throwing inside `Notify` (no cheap seam). PlayMode suite not run (the D&R end-to-end test builds its own sink, not the composition).
- Manifest guardrail (< 0.1 ms per tick at 4 parties of 4) not measured — Story 012 or a profiling pass.
- TR-party-012's end-to-end clause is Story 012.
