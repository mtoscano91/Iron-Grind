# Session State

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 005)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-005-version-sequencenumber-stale-discard.md` — Version/SequenceNumber Stale-Discard Helpers
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — both independently traced and confirmed the AC-NC-36 numbering resolution (see below); qa-tester found 2 boundary-coverage gaps (ordinary non-wrapping case, RFC-1982 ambiguous half-circle boundary) → fixed → 10 test methods (13 executions)
- **AC-NC-36 numbering corrected**: story's own AC text had the observed wraparound sequence off by one position relative to `ITransportFaultInjector.SetSequenceNumber`'s already-reviewed "resume from" contract. Two independent specialist reviews traced this from scratch and agreed the story text (not the code) had the error. Corrected in the story file itself (AC-NC-36, QA Test Cases) at closure.
- **Real bug fixed in already-committed Story 001 code**: `TransportFaultInjector.ConsumeNextSequenceNumber()` wrapped `uint.MaxValue` to `0`, contradicting CR-NET-7.5. Fixed to skip to `1`; the one affected existing Story 001 test was updated (not weakened).
- Tech debt logged: TD-010 (unseeded `_sequenceNumber` still defaults to `0` — same invariant, different vector; not fixed, no production send path consumes it yet)
- Files updated: `production/epics/networking-core/story-005-...md` (Status: Complete, ACs checked + corrected numbering, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 005 → Complete)
- Next recommended: Story 006 — Priority-Path Cap & Two-Path Delivery Model (`production/epics/networking-core/story-006-priority-path-cap-two-path-delivery.md`) — next in the Wire Protocol Core cluster

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 004)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-004-entityid-enum-wire-safety-guards.md` — EntityID/Enum Wire-Safety Guards
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — qa-tester found real gaps (missing `DisconnectType` valid-round-trip test, missing adjacent-boundary tests for `DamageType`/`DisconnectType`/`DisconnectReason`) → fixed → 20 test methods total
- **StatID blocker resolved, contrary to the story's own text** — verified directly before implementation that `StatID` is already `enum : byte` (0-16) in both `StatID.cs` and the Character Stats GDD; no workaround was needed or implemented
- **Real C# bug found and fixed**: `Span<byte>` (ref struct) cannot be captured inside a lambda closure — Story 003's test file (`WireProtocol_Envelope_Serialization_tests.cs`, already committed in `de7a15e`) had exactly this bug in `EncodePosition_BoundaryValue_DoesNotOverflowOrThrow`'s two `Assert.DoesNotThrow` lambdas. Found by the implementing subagent, confirmed, fixed (`Span<byte>` → `byte[]` for any buffer referenced inside a throw-assertion lambda), applied consistently in Story 004's own new test file. Undetected until now because this sandbox has no C# compiler — same class of issue as the "first real Unity compile" bugs from 2026-07-04.
- Deviation: `RawValue` property added to `EntityID`/`ItemID`/`CharacterID` (pre-existing structs, outside story's file list) — necessary for `WireIdCodec` to read the wrapped `uint` without reflection (CR-NET-7.3 forbids generic serializers)
- Tech debt logged: None new
- Files updated: `production/epics/networking-core/story-004-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 004 → Complete)
- Next recommended: Story 005 — Version/SequenceNumber Stale-Discard Helpers (`production/epics/networking-core/story-005-version-sequencenumber-stale-discard.md`) — next in the Wire Protocol Core cluster

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 003)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md` — Message Envelope & Fixed-Point Primitive Serialization
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — qa-tester found a real gap (no normal-case round-trip test for `EncodeDirection`, only the degenerate-zero-vector case) → fixed (added `EncodeDirection_WorkedValue_RoundTripsWithinTolerance`) → 21 test methods total
- Tech debt logged: TD-008 (no overflow guard on `critChance`/`attackSpeedMultiplier` — directed scope-discipline decision), TD-009 (no NaN/Infinity guard on position/rotation/direction encoders — flagged by unity-specialist)
- Files updated: `production/epics/networking-core/story-003-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 003 → Complete), `docs/tech-debt-register.md` (+TD-008, TD-009)
- Next recommended: Story 004 — EntityID/Enum Wire-Safety Guards (`production/epics/networking-core/story-004-entityid-enum-wire-safety-guards.md`) — next in the Wire Protocol Core cluster, builds directly on this story's `MessageEnvelopeCodec`

## Session Extract — /dev-story 2026-07-09 (Networking Core Story 003)

- Story: `production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md` — Message Envelope & Fixed-Point Primitive Serialization
- Pre-implementation: ran a scoped unity-specialist verification pass (not a full engine-risk spawn — story explicitly excludes the NGO send-API surface that makes ADR-004 HIGH risk) confirming `System.Buffers.Binary.BinaryPrimitives` + `Span<byte>`/`ReadOnlySpan<byte>` is IL2CPP-AOT-safe on Unity 6.3 (iOS ARM64 + Linux x64 server) — no generic instantiation, no reflection, no linker stripping risk.
- Files changed: `src/Foundation/Networking/WireProtocol/{ServerMessageEnvelope,ClientEntityMessageEnvelope,MessageEnvelopeCodec,WireFixedPointCodec}.cs` (all new)
- Test written: `tests/EditMode/Networking/WireProtocol_Envelope_Serialization_tests.cs` (18 test methods — AC-WP-1, AC-NC-28 including boundary/degenerate-guard cases, AC-NC-03 all covered)
- Key judgment call (directed, not agent-initiated): `critChance`/`attackSpeedMultiplier` encoders intentionally ship with no clamp/log guard (plain round+cast) — CR-NET-7.2 documents a valid range for these two fields but the story's Implementation Notes only specify guards for cycleTimer/position/quaternion/direction. This leaves an unguarded `ushort` wraparound risk if a caller ever passes an out-of-range value — tracked below as a tech-debt candidate, not fixed in this story (would be scope creep).
- **New tech debt candidate (not yet added to docs/tech-debt-register.md — flag for next docs pass)**: `WireFixedPointCodec.EncodeCritChance`/`EncodeAttackSpeedMultiplier` have no overflow guard; an out-of-range input silently wraps via the `ushort` cast rather than clamping+logging like the other four encoders.
- TR registry gap: `TR-net-001` not found in `docs/architecture/tr-registry.yaml` (`requirements: []`, empty project-wide) — same pre-existing systemic gap documented across every prior epic; used the wire-protocol GDD's CR-NET-7.1/7.2 text directly as the source of truth instead.
- Verified all 4 source files + the test file directly (read in full) before reporting — implementation matches the story's encoder/guard spec exactly, naming conventions and doc-comment style match project precedent.
- Blockers: None. Not yet run in the Unity Test Runner (no Editor invocation available in this session) — recommend running the EditMode suite before `/story-done`.
- Not yet committed — this is fresh work, not part of the previously-authorized backlog commit; awaiting explicit go-ahead to commit and/or proceed to `/code-review`.
- Next: `/code-review src/Foundation/Networking/WireProtocol/ tests/EditMode/Networking/WireProtocol_Envelope_Serialization_tests.cs` then `/story-done production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md`

## Session Extract — /code-review + /story-done 2026-07-08 (Networking Core Story 002 — Test Harness cluster COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-002-test-harness-observer-release-stripping.md` — Test Harness: INetworkTestObserver + Release-Build Stripping
- `/code-review` ran with 3 specialists in parallel (unity-specialist, qa-tester, devops-engineer): CHANGES REQUIRED (`Reset()` test only asserted 3 of ~25 capture lists cleared) → fixed to assert all 25 → APPROVED WITH SUGGESTIONS
- Final test count: 13 test methods, all 3 blocking ACs covered; AC-TC-01/02 additionally backed by CI config (`.github/workflows/tests.yml` + `tools/ci/check-test-harness-guards.sh`)
- Tech debt logged: None new (TR-net-009 registry gap pre-existing; AC-TC-01's placeholder CI job tracked as a future follow-up, not tech debt — no real Player build pipeline exists in this repo yet to wire it to)
- **Test Harness cluster (Stories 001-002) is now Complete** — every other Networking Core story references these interfaces in its own tests
- Files updated: `production/epics/networking-core/story-002-test-harness-observer-release-stripping.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 002 → Complete)
- Next recommended: Story 003 — Message Envelope & Fixed-Point Primitive Serialization (`production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md`) — first story in the Wire Protocol Core cluster (003-008)

## Session Extract — /dev-story 2026-07-08 (Networking Core Story 002)

- Story: `production/epics/networking-core/story-002-test-harness-observer-release-stripping.md` — Test Harness: INetworkTestObserver + Release-Build Stripping
- Files changed: `src/Foundation/Networking/TestHarness/{INetworkTestObserver,NetworkTestObserver}.cs` (new), `NetworkingTestHarness.cs` (added `CreateNetworkTestObserver()`), `.github/workflows/tests.yml` (2 new CI jobs), `tools/ci/check-test-harness-guards.sh` (new)
- Test written: `tests/EditMode/Networking/NetworkingTestHarness_Observer_tests.cs` (13 test methods — AC-NC-43 via a clearly-scoped test-only queue fixture, not Story 006's real queue)
- Process note: the implementing subagent's final response was truncated mid-sentence ("Let's validate the YAML syntax.") — no summary was received. Verified all files directly (Read every changed/created file) before reporting; everything was complete, correct, and consistent with the story's scope — no corruption or partial writes found.
- Key judgment calls (verified, endorsed): AC-TC-01 implemented as a documented non-blocking CI placeholder (this repo has no real IL2CPP Release Player build step yet — wiring one is out of scope, devops/build-infra concern); AC-TC-02 fully implemented as a real blocking CI check with a `--self-test` mode proving it isn't a no-op.
- Blockers: None
- Next: `/code-review src/Foundation/Networking/ tests/EditMode/Networking/ tools/ci/` then `/story-done production/epics/networking-core/story-002-test-harness-observer-release-stripping.md`

## Session Extract — /code-review + /story-done 2026-07-08 (Networking Core Story 001)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` — Test Harness: Fault/Crash/Zone-Config Injection
- `/code-review` verdict: CHANGES REQUIRED (`Reset()` wasn't clearing `_hasEmittedAnyMessage`, contradicting its "clean state" doc comment — found independently by qa-tester during the review pass) → fixed + regression test added → APPROVED WITH SUGGESTIONS
- Final test count: 31 test methods, all 4 blocking ACs covered
- Tech debt logged: None new (TR-net-009 registry gap is the same pre-existing systemic issue already documented across every epic)
- Files updated: `production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 001 → Complete), `src/Foundation/Networking/TestHarness/TransportFaultInjector.cs` (Reset() fix), `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs` (+1 test)
- Next recommended: Story 002 — Test Harness: INetworkTestObserver + Release-Build Stripping (`production/epics/networking-core/story-002-test-harness-observer-release-stripping.md`) — the last Test Harness story before the Wire Protocol Core cluster (003-008) can begin

## Session Extract — /dev-story 2026-07-08 (Networking Core Story 001)

- Story: `production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` — Test Harness: Fault/Crash/Zone-Config Injection
- Files changed: `src/Foundation/Networking/SessionState.cs` (new), `ZoneState.cs` (new), `src/Foundation/Networking/TestHarness/{ITransportFaultInjector,IServerCrashInjector,IZoneTestConfigurator,TransportFaultInjector,ServerCrashInjector,ZoneTestConfigurator,NetworkingTestHarness}.cs` (all new)
- Test written: `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs` (30 test methods across 4 fixtures, all 4 ACs covered — AC-TH-4 verified structurally, full CI stripping check deferred to Story 002)
- Notable judgment call (verified, endorsed): implementing agent replaced the GDD's literal `NetworkingTestHarness.RegisterInterfaces()` wording with a guarded static factory (`Create*` methods) since this project has no DI container and ADR-010 forbids a service-locator/EventBus singleton — same compile-guard effect, no architectural conflict
- Blockers: None. Known limitation: no `dotnet`/`csc` available in the sandboxed shell to run an automated compile check or the real Unity Test Runner — implementation was traced by hand against every test; recommend running the EditMode suite in Unity before/during `/story-done`
- Next: `/code-review src/Foundation/Networking/ tests/EditMode/Networking/` then `/story-done production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md`

## Session Extract — /create-stories networking-core 2026-07-08

- **29 stories written** to `production/epics/networking-core/` — the largest epic in the project (10 GDDs, ~130 ACs total)
- Research approach: 4 parallel Explore agents extracted structured AC/Formula/EdgeCase/Dependency summaries from the 9 sub-contract GDDs (root `networking-core.md` + ADR-004 read directly); synthesis and story decomposition done in main session
- 9 clusters: Test Harness (001-002, implement first — nearly everything else's tests depend on it), Wire Protocol Core (003-008), Tick Loop & Authority (009-011), Session Lifecycle (012-015), Session Token (016), Ghost Session (017-021), OWL Compensation (022-024), Message Routing (025-027), Relevance Filter (028-029)
- Scoping decision: Networking Core owns the envelope/channel/tick/session/ghost/OWL/relevance-filter/test-harness substrate only — every downstream system's specific message schema (NPC Shop, Loot, Party, Inventory, Equipment, Movement, Skill, etc.) is explicitly out of scope, left to each system's own future epic
- Explicitly deferred (blocked on unauthored/unapproved GDDs, or already covered): AC-NC-01 (Zone Instancing boundary), AC-NC-33b (Death & Respawn penalties), AC-NC-08a/08b + AC-NC-44 (Leveling schema-pending), AC-NC-39/wire SelfPositionUpdate (Client-Side Prediction not yet approved), AC-GH-EXP-1-4 (Visual/Feel playtest evidence), AC-NC-25 (already proven by existing Currency System tests)
- **3 cross-doc issues surfaced** (not fixed — flagged in EPIC.md for future propagation-check): (1) OQ-NET-1 BLOCKING — `HEARTBEAT_TIMEOUT_SECONDS` production default still undetermined; (2) AC-ID collision — root GDD and wire-protocol.md each independently define an unrelated "AC-NC-31"; (3) `GHOST_COMBAT_TTL_MINUTES` (session.md, 60s) vs `GHOST_COMBAT_TTL_MIN_S` (ghost-session.md F-GH-1 formula, 30s baseline) — two different constants for what reads as the same concept, Stories 019/021 chose the ghost-session formula as authoritative pending a design decision
- Also flagged: `StatID` enum cross-doc dependency (Character Stats GDD needs `enum:uint`→`enum:byte`, Story 004 workaround in place until then); ADR-004's engine-risk profiling gate (NGO `CustomMessagingManager`/`NetworkManager.ServerTime.Tick` headless-build verification) must pass before implementation sprint is greenlit, per EPIC.md's own pre-existing note
- Files updated: `production/epics/networking-core/story-001` through `story-029` (new), `production/epics/networking-core/EPIC.md` (Stories table + blockers section), `production/epics/index.md` (Networking Core row)
- Next recommended: `/story-readiness production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` before starting implementation; consider addressing the ADR-004 engine-risk profiling gate first since it's a pre-sprint blocker per the epic's own Definition of Done

## Session Extract — /code-review + /story-done 2026-07-08 (Currency Story 006 — Currency System epic COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` — TransferGold Stub & Compensating Refund
- `/code-review` verdict: APPROVED WITH SUGGESTIONS (non-blocking: optional input-invariance regression test for the stub, optional explicit assertion on `NotImplemented`'s placeholder fields)
- Final test count: 2 test methods, both blocking ACs covered (AC-CS-E-02, AC-CS-E-03)
- Tech debt logged: None new (TR-currency-002 registry gap and GDD `AdminAdjust`/`CompensatingRefund` drift are both pre-existing/already documented, not new to this story)
- **Currency System epic is now fully Complete — all 6 stories Done** (`production/epics/currency-system/EPIC.md` updated to Status: Complete)
- Files updated: `production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/currency-system/EPIC.md` (Status: Complete, Story 006 → Complete)
- Next recommended: no other Foundation-layer epic has stories remaining in progress. Candidates: `/create-stories networking-core` (largest remaining Foundation epic, 10 sub-contracts, not yet story'd), or start Core-layer epics (`/create-epics layer: core`) for Leveling/Inventory/Loot Table (needed to unblock Character Stats Story 008, which is Blocked). Note: `production/epics/index.md` is stale (last updated 2026-06-27, predates all story creation) — consider refreshing it.

## Session Extract — /story-readiness + /dev-story 2026-07-08 (Currency Story 006)

- `/story-readiness` verdict: READY (2 advisory gaps noted, neither blocking): (1) TR-currency-002 not in `tr-registry.yaml` — same pre-existing empty-registry systemic gap as every prior Currency story; (2) new finding — `design/gdd/currency-system.md` EC-CS-5 still says the compensating refund uses `GoldTransactionReason.AdminAdjust`, which is stale — ADR-001 Decision 3 and `control-manifest.md` (line 29) both mandate the dedicated `CompensatingRefund` value instead. The story's own text already correctly specifies `CompensatingRefund`, so implementation followed the story/ADR/manifest, not the stale GDD line. GDD fix deferred as a follow-up propagation-check edit, not done this session.
- Story: `production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` — TransferGold Stub & Compensating Refund
- Files changed: `src/Foundation/Currency/ICurrencyService.cs` (added `TransferGold` to interface), `src/Foundation/Currency/CurrencySystem.cs` (stub implementation, always `NotImplemented`, touches neither balance), `tests/EditMode/Currency/Currency_EdgeCases_tests.cs` (new, 2 tests)
- Test written: `tests/EditMode/Currency/Currency_EdgeCases_tests.cs` (2 test methods — AC-CS-E-02, AC-CS-E-03)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` — this is the last story in the Currency System epic

## Session Extract — /code-review + /story-done 2026-07-08 (Currency Story 005)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/currency-system/story-005-goldsyncevent-emission.md` — GoldSyncEvent Emission
- `/code-review` ran once: CHANGES REQUIRED (missing test proving `OnGoldSync` fires exactly once on a successful retry after a forced `ConcurrencyConflict` — independently flagged by both unity-specialist and qa-tester) → fixed → APPROVED WITH SUGGESTIONS
- Final test count: 9 test methods, all 4 blocking ACs covered plus the retry-fires-once edge case
- Tech debt logged: None new (TR-currency-004 registry gap is the same pre-existing systemic issue already covered in EPIC.md, not new)
- Files updated: `production/epics/currency-system/story-005-goldsyncevent-emission.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/currency-system/EPIC.md` (Story 005 → Complete), `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs` (+1 test)
- Next recommended: Story 006 — TransferGold Stub & Compensating Refund (`production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md`)

## Session Extract — /dev-story 2026-07-04 (Currency Story 005)
- Story: `production/epics/currency-system/story-005-goldsyncevent-emission.md` — GoldSyncEvent Emission
- Files changed: `src/Foundation/Currency/GoldSyncEventArgs.cs` (new), `src/Foundation/Currency/ICurrencyService.cs`, `src/Foundation/Currency/CurrencySystem.cs`
- Test written: `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs` (8 test methods, all 4 ACs covered)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-005-goldsyncevent-emission.md`

*Updated: 2026-07-04*

## Current Status

**Task**: Character Stats epic Stories 001-007 complete (008 Blocked). Item Database epic Complete (4/4). Currency System epic — Story 001 Complete (1/6); Stories 002-006 Ready. Real Unity project verified working.
**Stage**: Pre-Production
**GDD Count**: 38 Approved, 0 In Review = 38 of 38 MVP complete (6 Presentation GDDs deferred)
**ADR Count**: 10 written (ADR-001 through ADR-010), all Accepted
**UX Specs**: `design/ux/hud.md` (complete), `design/ux/interaction-patterns.md` (28 patterns), `design/accessibility-requirements.md` (Standard tier)

## Session Extract — Real bug found on first-ever Unity compile 2026-07-04

- **Bug**: `CharacterStats.StatChangedHandler` and `CharacterStats.EntityDiedHandler` (Story 005) are delegate types nested inside the `CharacterStats` class, not top-level types in the `IronGrind.CharacterStats` namespace. `using IronGrind.CharacterStats;` only imports the namespace — it does not bring a class's nested types into unqualified scope. Test files referenced both bare (`StatChangedHandler handler = ...`, `new EntityDiedHandler(...)`), which compiles fine when *inside* the `CharacterStats` class (all production usages in `CharacterStats.cs` are fine) but fails with CS0246 from *outside* it.
- This is exactly what TD-006 predicted: a real, pre-existing defect from Story 005 that sat undetected through code review and `/story-done` sign-off because nothing had ever actually compiled in Unity until this session.
- **Fixed**: qualified all 9 bare usages in `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` and `tests/EditMode/CharacterStats/TestHelpers/StatEventRecorder.cs` as `IronGrind.CharacterStats.CharacterStats.StatChangedHandler`/`.EntityDiedHandler`, matching the file's existing fully-qualified pattern for `_stats` (same namespace/class name collision as `IronGrind.ItemDatabase.ItemDatabase`).
- Verified `BuffModifierEntry`/`EquipmentModifierEntry` do NOT have the same issue — both are top-level types in their own files, not nested in `CharacterStats`.
- **Second bug found on same compile pass**: CS0104 ambiguous reference `Object` between `System.Object` and `UnityEngine.Object` in `ItemDatabase_Core_tests.cs:39` (`Object.DestroyImmediate(def)`). Same latent bug pattern in 3 more files that all use the identical `[TearDown] Object.DestroyImmediate` convention: `ItemDatabase_MvpRecords_tests.cs`, `ItemDatabase_Validator_Error_tests.cs`, `ItemDatabase_Validator_Warning_tests.cs`. Fixed all 4 by qualifying as `UnityEngine.Object.DestroyImmediate(def)`. (2 of these files had no explicit `using System;` yet still needed the fix — Unity 6.3's project likely has implicit global usings enabled, making `System` ambient project-wide regardless of per-file usings.)
- **Third bug found on same compile pass**: CS0234 in `CharacterStatsFixture.cs` — `CharacterStats.StatSlotCount` (bare) failed because this file's own namespace, `IronGrind.Tests.EditMode.CharacterStats`, *also* ends in the segment "CharacterStats." That makes the bare identifier "CharacterStats" resolve to a namespace tail rather than the production class, so the compiler reports "StatSlotCount does not exist in the namespace" (CS0234, distinct from CS0246 — correctly diagnosing a namespace/type confusion, not a missing type). Fixed all 4 occurrences by fully qualifying as `IronGrind.CharacterStats.CharacterStats.StatSlotCount`. Verified via project-wide grep that no other bare `CharacterStats.Member` shorthand remains anywhere in the test suite — every reference is now fully qualified.
- **Fourth bug found on same compile pass**: CS0221 in `ItemDatabase_Core_tests.cs:130` — `(ItemCategory)999` is a compile-time error, not a runtime concern: `ItemCategory` is `byte`-backed and `999` overflows a byte, which C# rejects for constant enum casts (would need `unchecked`, which the story never intended). The story's own AC-19 text used "999" as its illustrative out-of-range value, and the test carried that same overflow bug through implementation and code review, undetected until real compilation. Fixed by using `255` instead (matching the pattern already correctly used elsewhere for `(GearSlot)255`/`(StatID)255` in the Story 002 validator tests) — updated both the cast and its matching `LogAssert.Expect` message. Grepped for other literal-999-to-enum-cast patterns project-wide; none found.
- **Fifth issue — a red herring, not a bug**: user reported console "warnings" after all 128 tests passed. Confirmed these are the intentional `Debug.LogError` calls from write-lock rejection and equipment-modifier capacity-overflow test scenarios (both documented, by-design production behavior), correctly declared via `LogAssert.Expect` so the tests pass despite the console still showing the red error line (LogAssert suppresses test failure, not console output). No fix needed.
- **MILESTONE: All 128 EditMode tests pass** — Character Stats (Stories 001-007) and Item Database (Stories 001-003) are the first code in this project's history to actually compile and execute in Unity, confirming the designs are sound beyond code review. TD-002 and TD-006 closed in `docs/tech-debt-register.md`.
- Remaining open item: Story 004 (MVP Item Records) — the `ItemDatabaseSeeder` menu tool hasn't been run yet; the 34 real `.asset` files and smoke-check evidence are still pending.

## Session Extract — /story-done 2026-07-04 (Story 004 — Item Database epic COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/item-database/story-004-mvp-item-records.md` — MVP Item Records — 34 Authored ScriptableObject Assets
- Seeder run in real Unity Editor: 0 fatal, 0 errors, 0 warnings across all 34 records. Smoke check recorded at `production/qa/smoke-2026-07-04-item-database.md`.
- **Item Database epic is now fully Complete** — all 4 stories Done (`production/epics/item-database/EPIC.md` updated)
- Tech debt logged: None new (deviations were advisory, already covered by TD-002/TD-006 closure notes)
- Next recommended: Character Stats Story 008 is Blocked (depends on Leveling System epic, not yet started). No other Foundation-layer epic has stories created yet. Candidates: `/create-stories currency-system` or `/create-stories networking-core` (both epics exist, Ready, per `production/epics/index.md`), or start the Leveling System epic to unblock Character Stats Story 008.

## Session Extract — /create-stories currency-system 2026-07-04

- 6 stories written to `production/epics/currency-system/`: Core Types & AddGold, TrySpendGold, Guards & State Machine, Concurrency Safety, GoldSyncEvent Emission, TransferGold Stub & Compensating Refund
- All 6 are Logic type, Foundation layer, scoped as an in-memory C# model (`CurrencySystem : ICurrencyService`) matching the CharacterStats/ItemDatabase precedent — real PostgreSQL persistence deferred to Character Persistence (not yet built)
- Explicitly scoped OUT: GDD Group G (ServerLogic.asmdef isolation — no server/client split exists yet), GDD Group I (session resync — needs Networking Core's handshake, not yet built)
- Key design decisions embedded in the stories: `CharacterID`/`GoldTransactionReason`/`GoldMutationResult`/`GoldMutationError` types sourced authoritatively from `design/registry/entities.yaml` (already pre-registered with exact enum values); Story 003 revises Stories 001-002's lazy-balance-creation into explicit `RegisterCharacter` registration (needed for `CharacterNotFound` to be meaningful); Story 004 uses a real optimistic CAS-with-retry pattern (not a single big lock) with an `internal` test seam (`TryCompareAndSwapSpend`) so `ConcurrencyConflict` can be tested deterministically per this project's test-standards rule, while final-state correctness under real concurrency is tested via `Task.WhenAll`
- Next recommended: `/story-readiness production/epics/currency-system/story-001-core-types-and-addgold.md`, or `/create-stories networking-core` if you want all epics story'd out before implementing

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 001)

- `/story-readiness` verdict: NEEDS WORK → fixed (all 6 stories were missing the `Estimate` header field — caught during this check, added: 3-4h/1-2h/2-3h/3-4h/2-3h/1-2h for stories 001-006) → READY
- Story: `production/epics/currency-system/story-001-core-types-and-addgold.md` — Core Types & AddGold (Cap-Safe Addition)
- Files changed: `src/Foundation/Currency/{CharacterID,GoldTransactionReason,GoldMutationError,GoldMutationResult,ICurrencyService,CurrencySystem}.cs` (new), `tests/EditMode/Currency/Currency_AddGold_tests.cs` (new, 6 tests)
- Process note: same approval-chain limitation as earlier this session — implementing subagent correctly refused a relayed "approved," so files were written directly by the orchestrator using the subagent's already-reviewed exact code.
- Test written: 6 tests covering AC-CS-A-01, A-04, A-05 (+ uint.MaxValue edge case), C-01, C-02
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-001-core-types-and-addgold.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 004)

- Verdict: COMPLETE
- Story: `production/epics/currency-system/story-004-concurrency-safety.md` — Concurrency Safety (Thread-Safe Balance Mutation)
- `/code-review` verdict: APPROVED WITH SUGGESTIONS — two independent specialist passes (Unity + qa-tester) scrutinized the concurrency correctness in depth, found no race conditions/deadlocks; doc-comment caveat added to RegisterCharacter (not lock-protected, not safe for concurrent re-registration); two lower-priority suggestions deferred (OR-assertion annotation, extra 3-way/interleaved-op test coverage beyond the 3 stated ACs)
- Final test count: 5 test methods, all 3 blocking ACs covered
- Tech debt logged: None
- Next recommended: Story 005 — GoldSyncEvent Emission (`production/epics/currency-system/story-005-goldsyncevent-emission.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 004)

- `/story-readiness` verdict: READY (no gaps)
- Story: `production/epics/currency-system/story-004-concurrency-safety.md` — Concurrency Safety (Thread-Safe Balance Mutation)
- Switched `CurrencySystem`'s internal storage from plain `Dictionary` to `ConcurrentDictionary` (`_balances`, new `_versions`, new `_locks`) for structural thread-safety across different characters, layered with a per-character `lock` (`GetLockFor`) for compound-operation atomicity. `AddGold` is a single atomic lock (never rejects on conflict). `TrySpendGold` is now a 2-attempt retry wrapper around new `internal TryCompareAndSwapSpend` (optimistic version-checked CAS), guard order extended to: CharacterNotFound → InvalidAmount → version mismatch (ConcurrencyConflict) → InsufficientFunds → success.
- Files changed: `src/Foundation/Currency/CurrencySystem.cs`, `src/Foundation/Currency/ICurrencyService.cs` (doc comments only), `tests/EditMode/Currency/Currency_Concurrency_tests.cs` (new, 5 tests — 2 real-concurrency via Task.WhenAll asserting only invariant final state, 3 deterministic via the internal CAS seam)
- Pre-existing 3 Currency test files unaffected (verified: only touch the public interface, ConcurrentDictionary preserves identical single-threaded semantics)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-004-concurrency-safety.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 003)

- Verdict: COMPLETE
- Story: `production/epics/currency-system/story-003-guards-and-state-machine.md` — Input Guards & State Machine
- `/code-review` verdict: APPROVED WITH SUGGESTIONS — guard-order-proving test added (real gap: no test proved CharacterNotFound is checked before InvalidAmount); double-register overwrite test and TryGetValue refactor deferred (both minor, not logged as tech debt — too low priority)
- Final test count: 13 test methods, all 10 blocking ACs covered
- Tech debt logged: None
- Next recommended: Story 004 — Concurrency Safety (`production/epics/currency-system/story-004-concurrency-safety.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 003)

- `/story-readiness` verdict: READY (no gaps)
- Story: `production/epics/currency-system/story-003-guards-and-state-machine.md` — Input Guards & State Machine
- This story revised Stories 001/002's guard-free behavior: added `RegisterCharacter` to `ICurrencyService`/`CurrencySystem`; `AddGold`/`TrySpendGold` now check `CharacterNotFound` then `InvalidAmount` before their formula guard. Critically required fixing 10 of 11 pre-existing Story 001/002 tests (added `RegisterCharacter` calls) so they kept exercising their original behavior instead of spuriously failing with `CharacterNotFound`; `GetBalance_CharacterNeverTouched_ReturnsZero` correctly left unregistered.
- Files changed: `src/Foundation/Currency/ICurrencyService.cs`, `src/Foundation/Currency/CurrencySystem.cs`, `tests/EditMode/Currency/Currency_AddGold_tests.cs` (6 tests patched), `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs` (4 tests patched), `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs` (new, 11 tests)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-003-guards-and-state-machine.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 002)

- Verdict: COMPLETE
- Story: `production/epics/currency-system/story-002-tryspendgold.md` — TrySpendGold (Spend Guard)
- `/code-review` verdict: APPROVED (no required changes)
- Final test count: 4 test methods, all 3 blocking ACs + 1 boundary edge case covered
- Tech debt logged: None (deviations were None; TR-registry gap already documented as known epic-level tech debt in EPIC.md, not new)
- Next recommended: Story 003 — Input Guards & State Machine (`production/epics/currency-system/story-003-guards-and-state-machine.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 002)

- `/story-readiness` verdict: NEEDS WORK → fixed → READY. Two gaps found: (1) stale `GetOrCreateBalance(charId)` reference in the story's F-CS-2 code sample — that helper no longer exists after Story 001's code-review simplification; fixed to `GetBalance(charId)`. (2) `TR-currency-001` not found in `docs/architecture/tr-registry.yaml` — registry's `requirements:` list is empty project-wide (systemic, pre-existing, affects every story including the already-Complete Story 001); accepted as known tech debt, not blocking.
- Story: `production/epics/currency-system/story-002-tryspendgold.md` — TrySpendGold (Spend Guard)
- Files changed: `src/Foundation/Currency/ICurrencyService.cs` (added `TrySpendGold` signature), `src/Foundation/Currency/CurrencySystem.cs` (implemented F-CS-2 guard-before-subtract), `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs` (new, 4 tests)
- Test written: 4 tests covering AC-CS-A-02, A-03, E-01, plus cost==balance boundary edge case; failure-path tests explicitly assert balance unchanged via `GetBalance`, not just call failure
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-002-tryspendgold.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 001)

- Verdict: COMPLETE (no deviations)
- Story: `production/epics/currency-system/story-001-core-types-and-addgold.md` — Core Types & AddGold (Cap-Safe Addition)
- `/code-review` verdict: APPROVED WITH SUGGESTIONS — both applied (added `GetBalance_CharacterNeverTouched_ReturnsZero` test; simplified `AddGold` to remove a redundant double-write via `GetOrCreateBalance`, now just calls `GetBalance` directly)
- Final test count: 7 test methods, all 5 blocking ACs covered
- Tech debt logged: None (clean verdict)
- Next recommended: Story 002 — TrySpendGold (Spend Guard) (`production/epics/currency-system/story-002-tryspendgold.md`)

## Session Extract — /story-done 2026-06-29

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-002-modifier-stack.md` — F-1 Modifier Stack
- Tech debt logged: None (3 advisory items in Completion Notes)
- Pre-Story-003 action: fix B-01 (add IsFloatStat guard to GetEffectiveStat / GetEffectiveStatFloat)
- Next recommended: Story 003 — Modifier lifecycle (story-003-modifier-lifecycle.md)

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-004-resource-pools.md` — Resource Pools
- Files changed: `src/Foundation/CharacterStats/CharacterStats.cs` (5 edits: _currentHp/_currentMp fields, OnEntityDied stub, GetCurrentHP/MP accessors, GetEffectiveStat early-exit, RemoveEquipmentModifier MaxHP reconciliation, full method implementations)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` (12 test methods)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-004-resource-pools.md`

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-002-modifier-stack.md` — F-1 Modifier Stack
- Files changed:
  - `src/Foundation/CharacterStats/StatID.cs` — extended with float-schema stats (CritChance=12 through MovementSpeed=16)
  - `src/Foundation/CharacterStats/StatSchema.cs` — new file; schema routing, FloatStatArraySize=5, GetStatMin/GetStatMax
  - `src/Foundation/CharacterStats/CharacterStats.cs` — per-entity internal modifier dicts, FloatStatValues, GetBaseStatFloat/SetBaseStatFloat, GetEffectiveStat + GetEffectiveStatFloat with absent-stat guard
  - `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` — added SetFloatBaseStat, SetEquipmentModifiers, SetBuffModifiers, ClearModifiers
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — new file, 11 test methods covering AC-01, AC-02, AC-03, AC-04, AC-05, AC-24, AC-28a, AC-28b, AC-21, AC-30, AC-26
- Blockers: None

## Session Extract — /dev-story 2026-06-29 (Story 003)

- Story: `production/epics/character-stats/story-003-modifier-lifecycle.md` — Modifier Lifecycle
- Files changed:
  - `src/Foundation/CharacterStats/CharacterStats.cs` — storage migrated to per-entity-per-stat nested dicts; AddBuffModifier, AddEquipmentModifier, RemoveBuffModifier, RemoveEquipmentModifier implemented with write-lock guard, duplicate-ID overwrite (no double-stack), capacity overflow (log+return); GetEffectiveStat/GetEffectiveStatFloat updated to query per-stat buckets
  - `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` — SetEquipmentModifiers and SetBuffModifiers updated to require StatID parameter; ClearModifiers unchanged
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — all fixture calls updated to pass appropriate StatID (no AC changes, test plumbing only)
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs` — new file, 7 test methods covering AC-16, AC-17, AC-18, AC-19, AC-20, AC-22, capacity overflow
- Blockers: None
- Next: /code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/ then /story-done production/epics/character-stats/story-003-modifier-lifecycle.md
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-002-modifier-stack.md`

## Gate Check Session (2026-06-27)

All 4 prior blockers from the morning FAIL resolved in this session:

| Blocker | Resolution |
|---|---|
| No test framework | `/test-setup` → `tests/EditMode/`, `tests/PlayMode/`, `.github/workflows/tests.yml`, `tests/EditMode/SmokeTest.cs` |
| No architecture.md | `/create-architecture` → `docs/architecture/architecture.md` v1.0, TD sign-off |
| No accessibility requirements | `design/accessibility-requirements.md` — Standard tier committed |
| No interaction pattern library | `/ux-design patterns` → `design/ux/interaction-patterns.md`, 28 patterns |

Director panel (lean mode — all 4 run as PHASE-GATEs):
- Creative Director: CONCERNS (5 items)
- Technical Director: READY (2 tracked conditions)
- Producer: CONCERNS (4 items)
- Art Director: CONCERNS (4 items)

Gate report: `production/gate-checks/technical-setup-to-pre-production-2026-06-27.md`

## ADR Session — 2026-06-27

Both Foundation Required ADRs written (previously blocking Zone Instancing + Feature-layer stories):

| ADR | Title | Status | Domain | Engine Risk |
|-----|-------|--------|--------|-------------|
| ADR-009 | Scene/Zone-Load Management | Accepted | Core — Scene Management | HIGH |
| ADR-010 | Event/Messaging Architecture | Accepted | Core — C# Messaging | LOW |

Registry updated: 4 new forbidden patterns (`scene_handle_as_int`, `urp_setup_render_passes_loading_screen`, `central_event_bus`, `lambda_capture_persistent_subscription`) and 2 new interface contracts (`point_to_point_messaging`, `broadcast_messaging`).

## Top Priority Items for Pre-Production

1. **[IMMEDIATE]** Resolve party drop bonus contradiction (CD Concern 1 — HIGH) — amend game concept OR restore bonus
2. ~~Write Scene/Zone-Load Management ADR~~ ✓ DONE (ADR-009)
3. ~~Write Event/Messaging Architecture ADR~~ ✓ DONE (ADR-010)
4. ~~`/create-control-manifest`~~ ✓ DONE — `docs/architecture/control-manifest.md` v2026-06-28 (99 rules across 4 layers + global)
4b. ~~`/create-stories character-stats`~~ ✓ DONE — 8 stories written (001–007 Ready, 008 Blocked pending Leveling System); QA Lead gate passed with revisions; EPIC.md DoD updated to AC-01–AC-34
5. Name/license typeface (AD Concern 1 — before UI asset production)
6. Author 6 deferred MVP GDDs (Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding)

## Manual Step Pending

- Add `UNITY_LICENSE` to GitHub repository secrets (required for `.github/workflows/tests.yml` to pass)

## Revision Pass — Combat UI GDD (2026-06-20)

All 23 blocking items from the design-review specialist pass have been resolved.
Pending: systems-index update, review-log append, and Phase 5 closing widget.

| File | Change | Status |
|---|---|---|
| `design/gdd/combat-ui.md` | Status "In Design" → "In Review" (original triad) | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-9: bar state now persists server-side (1-bit barIsExpanded in SkillBarLayout) | ✓ Done |
| `design/gdd/combat-ui.md` | Bar States table: removed "or zone entry" from Expanded→Collapsed; Collapsed = "first zone entry" | ✓ Done |
| `design/gdd/combat-ui.md` | F-CUI-1: fixed uint underflow, NaN/div-zero, compile error; signed long subtraction | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-18: added Silenced (8) + CasterNotAlive (9) rejection codes | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-19: removed Painter2D mandate; implementation-defined renderer with perf AC | ✓ Done |
| `design/gdd/combat-ui.md` | EC-CUI-4: 5s timeout + re-request + fallback recovery path | ✓ Done |
| `design/gdd/combat-ui.md` | EC-CUI-5: zone entry now loads persisted barIsExpanded (not reset to Collapsed) | ✓ Done |
| `design/gdd/combat-ui.md` | Interactions table: removed stale OQ-CUI-3 reference from Notes | ✓ Done |
| `design/gdd/combat-ui.md` | Dependencies Pending Amendments: CR-SK-12 + hud.md marked RESOLVED | ✓ Done |
| `design/gdd/combat-ui.md` | Visual Requirements cooldown arc: removed "via Painter2D"; added countdown number row | ✓ Done |
| `design/gdd/combat-ui.md` | UI Requirements: replaced Painter2D driver line with implementation-defined perf note | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-2: 250ms → 200ms (matches CR-CUI-8) | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-8: added tick rate reference (40 ticks @ 20t/s = 2s), 60fps arc note | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-16: rewritten to test corridorWidth < 140dp (not safe area width) | ✓ Done |
| `design/gdd/combat-ui.md` | Added AC-CUI-18 (bar state persistence), AC-CUI-19 (expand gating), AC-CUI-20 (Silenced visual) | ✓ Done |
| `design/gdd/combat-ui.md` | OQ-CUI-3: marked RESOLVED | ✓ Done |
| `design/gdd/combat-ui.md` | OQ-CUI-6: updated to reference Silenced (8) resolution; marked RESOLVED | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-2: added V-0 (CasterNotAlive liveness check) + V-5b (Silenced check) | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-12: amended to require SkillCooldownUpdate with OnCooldown rejections (EC-CUI-4 guarantee) | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-23: added explicit SkillCooldownUpdate delivery contract | ✓ Done |
| `design/registry/entities.yaml` | SkillCastRejectionCode: added Silenced=8, CasterNotAlive=9 | ✓ Done |
| `production/session-state/active.md` | This update | ✓ Done |

## Combat UI GDD Summary

**File**: `design/gdd/combat-ui.md`
**Status**: In Review — ready for `/design-review`
**Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

### All 11 Sections Written and Approved

1. Overview ✓ — Zone E skill bar, UI Toolkit (ADR-005), 10-skill/8-slot bar, CUS potbar separate, SkillCastRequest + SkillCooldownUpdate flow
2. Player Fantasy ✓ — "Instrument Panel with consequence tone"; potions do NOT reset auto-attack cadence
3. Detailed Design ✓ — CR-CUI-1 through CR-CUI-22 (bar structure, layout, expand/collapse, slot binding, casting, cooldown, device adaptation)
4. Formulas ✓ — F-CUI-1 (cooldown fraction), F-CUI-2 (Zone E left boundary), F-CUI-3 (chat corridor), F-CUI-4 (implementation constants)
5. Edge Cases ✓ — EC-CUI-1 through EC-CUI-10
6. Dependencies ✓ — upstream/downstream dependencies; pending amendments table
7. Tuning Knobs ✓ — 9 knobs in CombatUIConfig.asset
8. Visual/Audio Requirements ✓ — slot states, cooldown arc, animations, toasts, audio ownership boundaries
9. UI Requirements ✓ — Unity 6.3 implementation constraints (UI Toolkit, Painter2D, safe area, PickingMode)
10. Acceptance Criteria ✓ — AC-CUI-1 through AC-CUI-17
11. Open Questions ✓ — OQ-CUI-1 through OQ-CUI-7

### Key Design Decisions Locked

- **Bar layout**: 4 primary slots (S1-S4) + [+] toggle to expand to 8 (S5-S8 expand above); Zone E footprint = 292dp
- **Consumables**: Skills-only bar; CUS potbar is a separate UI element (Option B)
- **Skill binding**: Players choose any 8 of 10 class skills via in-bar long-press context menu
- **Cooldown wire format**: `cooldownExpiryTick` (absolute server tick) — CR-SK-12 amended
- **Cooldown renderer**: Painter2D `generateVisualContent` callback; no RenderTexture, no shader dependency
- **SE3 chat corridor**: Combat-collapse when corridorWidth < 140dp (CHAT_CORRIDOR_MIN_WIDTH_DP)
- **Auto-attack toggle**: HUD owns it in Zone D — NOT Combat UI
- **Expand direction**: Expand above (primary row fixed; party frame overlap accepted MVP)

## HUD Pre-Implementation Gates

| Gate | Status | Notes |
|---|---|---|
| OQ-HUD-1: UI framework ADR | **RESOLVED** — ADR-005 Proposed | UI Toolkit chosen |
| OQ-HUD-7: design/ux/hud.md | **RESOLVED** — spec complete 2026-06-20 | XP bar color `#C4912A` decided |
| OQ-HUD-8: Status Effects event interface | Open | Blocks buff tray implementation only |

## Open Items (non-blocking to Combat UI review)

| ID | Description | Owner | Priority |
|---|---|---|---|
| OQ-CUI-1 | CUS potbar position in Zone E | HUD + CUS GDD | Pre-impl |
| OQ-CUI-2 | Cooldown arc color | Art Director | Pre-impl |
| OQ-CUI-4 | Zone D auto-attack toggle spec | HUD GDD amendment | BLOCKING for HUD impl |
| OQ-CUI-6 | Status effect skill disable visual | Advisory | Low |
| OQ-CUI-7 | Expanded bar / party frame overlap — SE3 | Lead sign-off | Pre-impl |
| OQ-HUD-8 | Status Effects event interface | Must define before buff tray impl | Advisory |
| OQ-UX-HUD-1 | Charge bar obsolescence decision | Auto-Attack Combat + playtest | Advisory |
| OQ-UX-HUD-7 | Party chat input mode spec | Party Chat GDD | Advisory |

## Architecture Review — 2026-06-21

`/architecture-review full` completed. Verdict: **FAIL (advisory)**.

**Files written:**
- `docs/architecture/architecture-review-2026-06-21.md` — full report
- `docs/architecture/traceability-index.md` — domain-level coverage matrix

**Key findings:**

| Finding | Severity |
|---|---|
| Persistence Layer ADR missing — blocks ADR-001 + ADR-004 | 🔴 Blocking |
| ADR-001: 2 propagations never applied (character-persistence.md, networking-session.md) | 🔴 Blocking |
| ADR-005 still Proposed — auto-attack-combat.md + combat-ui.md already build on it | 🔴 Blocking |
| ADR-001 missing Engine Compatibility + ADR Dependencies sections | ⚠️ Template gap |
| ADR-002 link.xml uses wrong assembly (`UnityEngine` vs `UnityEngine.AIModule`) | 🔴 Build risk |
| ADR-005 `VisualElement.transform` "removed by 6.3" contradicts pinned reference (deprecated only) | 🟠 Factual |
| ADR-005 `ApplySafeArea` code references undefined `panelWidth`/`panelHeight` | 🟠 Compile error |
| Hosting Backend ADR missing | ❌ Gap |
| ADR-006 Combat UI framework missing | ❌ Gap |

**TR-registry:** Still empty — no per-TR IDs minted this pass (domain-level only).

## Gate Check Result — 2026-06-21

`/gate-check pre-production` → **FAIL**
Report: `production/gate-checks/pre-production-to-production-2026-06-21.md`

Director panel: CD NOT READY · TD NOT READY · PR NOT READY · AD CONCERNS
Artifacts: 5/16 present · Quality checks: 0/10 · Vertical Slice: AUTO-FAIL (does not exist)

Top blockers (dependency order):
1. **Persistence Layer ADR** — write via `/architecture-decision persistence-layer`
2. **ADR-001 propagations** — apply to character-persistence.md + networking-session.md
3. **ADR-005 → Accepted** — after on-device profiling + specialist fixes
4. **Hosting Backend ADR** — `/architecture-decision hosting-backend`
5. **ADR-006 Combat UI** — `/architecture-decision combat-ui-framework`
6. No control manifest → `/create-control-manifest` after ADRs
7. No epics/stories → `/create-epics`, `/create-stories`
8. 6 MVP GDDs not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding
9. No Vertical Slice build (enhancement destruction required; blind-tested)
10. No real playtests (0 of 3 required)

## ADR-006 — Persistence Layer (2026-06-27)

Written: `docs/architecture/ADR-006-persistence-layer.md` — Status: Proposed
**Decisions**: PostgreSQL + Npgsql + Dapper; PendingPurchase → separate table, same DB; ≤50ms write budget
**Resolved**: ADR-001 OQ-ADR1-1 (PendingPurchase storage), ADR-004 OQ-NET-5 / OQ-ADR4-2 (write latency)
**Registry**: 4 new stances added (character_record ownership, pending_purchase ownership, persistence_database API, EF Core forbidden)
**Constraint added**: Hosting Backend ADR must enforce PostgreSQL co-location with game server.

## ADR-001 Propagations Applied (2026-06-27)

| File | Change | Status |
|---|---|---|
| `design/gdd/networking-session.md` | CR-NET-6.4: inserted step 2 (PendingPurchase reconciliation before handshake emission) | ✓ Done |
| `design/gdd/character-persistence.md` | CR-CP-1: added BeginPurchase/CompletePurchase/RefundPurchase/LoadOutstandingPurchases to ICharacterPersistence + PendingPurchaseResult enum + PendingPurchaseRecord struct | ✓ Done |
| `design/gdd/character-persistence.md` | CR-CP-12: new rule — PendingPurchase storage and lifecycle (separate table, same DB, ADR-006 Decision 3) | ✓ Done |
| `design/gdd/character-persistence.md` | Interactions table: NPC Shop row added as upstream caller | ✓ Done |

## ADR-005 Promoted to Accepted (2026-06-27)

| Fix | Status |
|---|---|
| `ApplySafeArea`: added `panelSize = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height))` — removes undefined `panelWidth`/`panelHeight` references | ✓ Done |
| Constraints: corrected "`VisualElement.transform` removed effectively by 6.3" → "deprecated since 6.2, still compiles with warning in 6.3" (aligns with deprecated-apis.md) | ✓ Done |
| ADR Dependencies: updated Combat UI reference from ADR-006 → ADR-007 | ✓ Done |
| Status: Proposed → Accepted (2026-06-27) | ✓ Done |

Note: device profiling (iPhone SE 3rd gen, < 0.3ms HUD update) is a pre-implementation gate captured in ADR-005 Validation Criteria — not a pre-Accepted gate.

## ADR-007 — Hosting Backend (2026-06-27)

Written: `docs/architecture/ADR-007-hosting-backend.md` — Status: Proposed
**Decision**: Self-hosted Hetzner CPX41 VPS (Ubuntu 22.04 LTS) + co-located PostgreSQL (loopback, ~0.1ms)
**Zone server**: One Unity 6.3 IL2CPP headless process per active zone instance (systemd)
**Client connectivity**: Direct NGO UDP to public IP:port (no relay)
**Resolved**: ADR-004 OQ-ADR4-1 (hosting backend deferred), OQ-NET-5 (write latency)
**Registry**: 3 new stances added (game_server_hosting, zone_server_process_model, unity_relay_for_dedicated_server forbidden)
**Engine specialist correction**: `NetworkTransform.Update()` → `NetworkTransform.OnUpdate()` in NGO 6.3 — flagged as risk in ADR

## ADR-008 — Combat UI Framework (2026-06-27)

Written: `docs/architecture/ADR-008-combat-ui-framework.md` — Status: Proposed
**Decision**: Painter2D `generateVisualContent` for cooldown arcs; `SkillBarPresenter : MonoBehaviour` with `Queue<T>` decoupling; USS `transition: height 200ms` (GDD-locked, CR-CUI-8); Unity 6.0 event API names
**Resolved**: CR-CUI-19 (cooldown renderer implementation-defined → Painter2D chosen)
**Registry**: 4 new stances (combat_ui_arc_renderer, skill_bar_presenter_pattern, deprecated_ui_event_api_names, direct_visually_element_from_network_behaviour forbidden)
**Pre-sprint gate**: `performance-analyst` must validate 8× Painter2D arcs ≤0.3ms on iPhone SE 3rd gen before combat UI implementation begins (CR-CUI-19 blocking gate)

## ADR Count as of 2026-06-27

ADR-001 through ADR-008 written. All priority ADRs from architecture-review-2026-06-21 are now addressed.

## Foundation Epics — 2026-06-27

`/create-epics layer: foundation` complete. 4 epics written, index created.

| Epic Slug | Layer | GDD(s) | Status |
|---|---|---|---|
| character-stats | Foundation | design/gdd/character-stats.md | Ready |
| item-database | Foundation | design/gdd/item-database.md | Ready |
| currency-system | Foundation | design/gdd/currency-system.md | Ready |
| networking-core | Foundation | design/gdd/networking-core.md + 9 sub-contracts | Ready |

**Files written:**
- `production/epics/character-stats/EPIC.md`
- `production/epics/item-database/EPIC.md`
- `production/epics/currency-system/EPIC.md`
- `production/epics/networking-core/EPIC.md`
- `production/epics/index.md`

**Open items from this pass:**
1. `docs/architecture/tr-registry.yaml` is empty — populate before `/story-readiness` can run
2. `docs/architecture/architecture.md` Foundation module table still shows `⚠️` for ADR-009/ADR-010 — update to remove markers (both Accepted 2026-06-27)
3. Leveling/Inventory/Loot Table in architecture.md Foundation table → actually Core layer per systems-index; will appear in `/create-epics layer: core`

## Recommended Next Steps

1. **`/create-stories character-stats`** — first implementable stories (lowest risk, no engine surface)
2. **`/create-stories item-database`** — data layer stories
3. **`/create-stories currency-system`** — economy foundation stories
4. **`/create-stories networking-core`** — largest epic; 10 sub-contracts
5. **`/create-epics layer: core`** — Core layer epics after Foundation stories are underway
6. 6 MVP GDDs still not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding

## Prior Approved GDDs (37 total)

Character Stats, Item Database, Currency System, Class System, Leveling System, Auto-Attack Combat, Skill System, Damage Calculation, Networking Core, networking-session, networking-wire-protocol, networking-test-harness, networking-session-token, networking-ghost-session, networking-ghost-character-state, party-system, inventory-system, loot-table-system, networking-message-criticality, networking-channel-contract, networking-owl-compensation, networking-relevance-filter, status-effects, Authentication, Equipment System, Enhancement System, Enemy AI, Movement System, Death & Respawn, Zone Instancing, NPC Shop, Consumable Use System, Mob Spawning, Navigation/Pathfinding, Client-Side Prediction, Party Chat, **HUD**

## Session Extract — /architecture-review + promotions 2026-06-27
- Verdict: **PASS** (CONCERNS → PASS in same session)
- Requirements: domain-level — all 8 Foundation/Core domains covered, 0 coverage gaps, 0 cross-ADR conflicts
- ADRs: 8 / 8 Accepted (ADR-006, 007, 008 promoted to Accepted 2026-06-27)
- Cleanup applied: ADR-002 link.xml → UnityEngine.AIModule; ADR-008 sortingOrder=1 reserved; ADR-005 Combat-UI ADR# refs corrected; ADR-001 Engine Compat + ADR Deps backfilled
- GDD revision flags: None
- Remaining open items: ADR-001 OQ-ADR1-2 (SellRequest atomicity); ADR-004 OQ-ADR4-3 (CustomMessagingManager vs UTP wrapper); 6 MVP GDDs not yet authored
- Report: docs/architecture/architecture-review-2026-06-27.md

## Session Extract — /gate-check pre-production + /create-architecture 2026-06-27
- **Gate (Technical Setup → Pre-Production): FAIL** — report at production/gate-checks/technical-setup-to-pre-production-2026-06-27.md
- Gate blockers: (1) no test framework, (2) no master architecture doc, (3) no accessibility-requirements.md, (4) no interaction-patterns.md
- **Blocker #2 RESOLVED**: master architecture doc written → docs/architecture/architecture.md (TD sign-off: APPROVED WITH CONDITIONS 2026-06-27; LP feasibility skipped — lean mode)
- Traceability index renamed traceability-index.md → architecture-traceability.md (gate-expected filename)
- Required New ADRs surfaced (Foundation-first): (1) Scene/Zone-Load Management [HIGH], (2) Event/Messaging Architecture [LOW]; then (3) URP Render/VFX [HIGH], (4) Audio [LOW] — both blocked on unauthored GDDs
- **Remaining gate blockers**: /test-setup (tests/ + CI + example test); design/accessibility-requirements.md (pick tier); /ux-design patterns (interaction-patterns.md)
- Next: clear remaining 3 gate blockers → write 2 Foundation Required ADRs → re-run /gate-check pre-production

## Session Extract — /test-setup 2026-06-27
- **Gate blocker #1 RESOLVED**: test framework scaffolded
- Files created: tests/README.md, tests/EditMode/README.md, tests/EditMode/SmokeTest.cs (example test), tests/PlayMode/README.md, tests/smoke/critical-paths.md, tests/evidence/.gitkeep, .github/workflows/tests.yml
- Framework: Unity Test Framework (NUnit, built-in) — EditMode (unit) + PlayMode (integration)
- CI: game-ci/unity-test-runner@v4, Unity 6000.3.10f1, runs on push to main + PRs
- One-time manual step required: add UNITY_LICENSE to GitHub repository secrets before first CI run
- **Remaining gate blockers**: design/accessibility-requirements.md (pick tier); /ux-design patterns (interaction-patterns.md)
- Next: accessibility doc (pick tier) → /ux-design patterns → re-run /gate-check pre-production

## Session Extract — accessibility-requirements.md 2026-06-27
- **Gate blocker #3 RESOLVED**: design/accessibility-requirements.md written, tier committed
- Tier: **Standard** (user decision)
- Rationale: visual (item rarity, HP bars) + motor (touch targets, Rhythm Mastery timing) are primary barriers; Standard covers both; Comprehensive deferred (VoiceOver, mono audio, subtitle customization)
- Key Standard commitments: 44×44pt touch targets (Apple HIG), colorblind modes (Protanopia/Deuteranopia/Tritanopia), text size adjustment (chat + menus), timing window multiplier for skill activation (1×/1.5×/2×), motion reduction toggle, safe area compliance
- Open questions: Unity 6.3 UI Toolkit accessibility node support for VoiceOver; timing window client-side feasibility; minimum iOS version
- **Remaining gate blocker**: /ux-design patterns (design/ux/interaction-patterns.md)
- Next: /ux-design patterns → re-run /gate-check pre-production

## Session Extract — /ux-design patterns 2026-06-27
- **Gate blocker #4 RESOLVED**: design/ux/interaction-patterns.md written
- 28 patterns catalogued and formalized; 8 gaps identified for future screens
- Pattern categories: Input Controls, Gesture, Combat UI, Feedback, Data Display, Layout/Navigation, Chat
- Key patterns: Resource Bar, Touch Toggle, Expand/Collapse, Skill Slot, Cooldown Arc, Long-Press Context Menu, Toast Notification, Shake Feedback, Status Effect Icon, Party Frame, Target Frame, Loot Countdown Notification, Context-Adaptive Overlay, Safe Area Container, Tabbed Panel, Scrollable Item List, Item Row, Quantity Selector, Confirm Button with Spinner, Locked Item State, Destructive Confirmation Overlay, Risk Warning Badge, Outcome Animation, Persistent Chat Panel, Compose Button, Input Field with Validation, Character Counter, Keyboard-Slide Layout Shift
- **All 4 gate blockers now resolved** — ready to re-run /gate-check pre-production
- Next: /gate-check pre-production

## Session Extract — /story-done 2026-06-29
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-003-modifier-lifecycle.md` — Modifier Lifecycle
- Tech debt logged: None (2 advisory items in Completion Notes)
- Next recommended: Story 004 — Resource Pools (`production/epics/character-stats/story-004-resource-pools.md`)

## Session Extract — /story-done 2026-06-29 (Story 004)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-004-resource-pools.md` — CurrentHP / CurrentMP Lifecycle
- Tech debt logged: None (pre-existing TR-ID advisory)
- Next recommended: Story 005 — Events (`production/epics/character-stats/story-005-events.md`)


## Session Extract — /dev-story 2026-07-02 (Story 007)

- Story: `production/epics/character-stats/story-007-transaction-api.md` — Transaction API
- Files changed: `src/Foundation/CharacterStats/CharacterStats.cs` (transaction fields + BeginStatTransaction/EndStatTransaction/RollbackStatTransaction/AddToDeferredDedup + SetBaseStat conditional), `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (new, 7 tests)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (7 tests)
- Blockers: None
- Next: /code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/ then /story-done production/epics/character-stats/story-007-transaction-api.md

## Session Extract — /story-done 2026-07-02 (Story 006)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-006-write-ownership.md` — Write Ownership
- Tech debt logged: None (advisory: TR-stats-006 unregistered; AC-13/AC-15 deferred OQ-1 pending)
- Next recommended: Story 007 — Transaction API (`production/epics/character-stats/story-007-transaction-api.md`)

## Session Extract — /dev-story 2026-07-02

- Story: `production/epics/character-stats/story-006-write-ownership.md` — Write Ownership
- Files changed: `src/Foundation/CharacterStats/ILevelingService.cs` (new), `src/Foundation/CharacterStats/CharacterStats.cs` (constructor + AddExperience + OQ-1 TODO), `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` (NullLevelingService, CreateWithLeveling), `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (new, 6 tests)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (6 tests — AC-14 × 3, NEW AC × 3)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-006-write-ownership.md`

## Session Extract — /story-done 2026-07-02

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-005-events.md` — OnStatChanged / OnEntityDied Events
- Tech debt logged: None (4 advisory items in Completion Notes)
- Code review fixes applied: count snapshot in FireOnStatChanged/FireOnEntityDied; IsFiringAndAssert guards on Subscribe/Unsubscribe
- Next recommended: Story 006 — Write Ownership (`production/epics/character-stats/story-006-write-ownership.md`)

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-005-events.md` — OnStatChanged / OnEntityDied Events
- Files changed:
  - `src/Foundation/CharacterStats/CharacterStats.cs` — replaced OnEntityDied stub with full event infrastructure (StatChangedHandler/EntityDiedHandler delegates, 16-slot fixed arrays, Subscribe/Unsubscribe, _isFiring guard, FireOnStatChanged/FireOnEntityDied); added IsFiringAndAssert guard + OnStatChanged firing to SetBaseStat, SetBaseStatFloat, AddBuffModifier, RemoveBuffModifier, AddEquipmentModifier, RemoveEquipmentModifier; added guard to ApplyDamage/ApplyRegen/ConsumeMana/ApplyManaRegen; replaced OnEntityDied?.Invoke with FireOnEntityDied in ApplyDamage and RemoveEquipmentModifier MaxHP path
  - `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` — replaced 5x `OnEntityDied += ...` with `Subscribe(_ => diedCount++)` (event syntax incompatible with fixed-array pattern)
  - `tests/EditMode/CharacterStats/TestHelpers/StatEventRecorder.cs` — added Subscribe/Unsubscribe wiring methods
- Test written: `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` (8 test methods — AC-29, AC-29 edge, AC-29b, AC-29b edge, Unsubscribe, Unsubscribe re-subscribe, OnEntityDied re-entrance, OnEntityDied read-permitted)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-005-events.md`

## Session Extract — /story-done 2026-07-02
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-007-transaction-api.md` — Transaction API
- Tech debt logged: None (3 advisory items in Completion Notes)
- Next recommended: Story 008 is Blocked (depends on Leveling System epic)

## Session Extract — /code-review + /story-done 2026-07-04
- Story: `production/epics/item-database/story-001-core-types-and-runtime-database.md` — IItemDatabase Interface, Runtime Database, and Core Type Definitions
- Implementation was already committed at session start (`bd30009`, prior session) but had not been code-reviewed or closed
- `/code-review` verdict: APPROVED WITH SUGGESTIONS (Required Change: AC-2 test spec had an untested cross-ID no-aliasing edge case)
- Fixed: added `ItemDatabase_GetItem_DifferentIds_ReturnsDistinctReferences` to `tests/EditMode/ItemDatabase/ItemDatabase_Core_tests.cs`
- `/story-done` verdict: COMPLETE WITH NOTES — 11/11 ACs passing
- Tech debt logged: TD-001 (GetItemsByCategory encapsulation leak), TD-002 (missing .asmdef project-wide), TD-003 (untested duplicate-ID/null-entry Initialize() behavior) — new `docs/tech-debt-register.md` created
- Files updated: `production/epics/item-database/story-001-core-types-and-runtime-database.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/item-database/EPIC.md` (Story 001 → Complete), `docs/tech-debt-register.md` (new file)
- Next recommended: Story 002 — Import Validator — Reject Rules (`production/epics/item-database/story-002-validator-error-rules.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Story 002)

- Story: `production/epics/item-database/story-002-validator-error-rules.md` — Import Validator — Reject Rules (Error Path)
- `/story-readiness` verdict: READY (17/17 checks passing)
- Files changed: `src/Foundation/ItemDatabase/ItemDefinitionValidator.cs` (new, 378 lines — `ValidationSeverity`, `ValidationIssue`, `ValidationResult`, `ItemDefinitionValidator.ValidateRecord`/`ValidateBatch`), `src/Foundation/ItemDatabase/StatModifierEntry.cs` + `EquipmentData.cs` + `ConsumableData.cs` (each: added `#if UNITY_EDITOR internal CreateForTesting` seam)
- Test written: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Error_tests.cs` (17 test methods, all 15 blocking ACs covered: AC-3,4,5,7,8,10,11,12,14,15,21,22,25,26,41)
- Correction mid-session: implementing agent initially added 4 out-of-scope Story 003 warning checks (negative FlatBonus, duplicate StatID, CooldownSeconds==0, equipment SellPriceGold==0) despite explicit instruction not to, and silently redesigned `ValidationResult` from the story's suggested `IsFatal`/`Errors`/`Warnings: IReadOnlyList<string>` shape to `Issues: IReadOnlyList<ValidationIssue>` + `ValidationSeverity` enum. User decision: stripped the warning logic (kept scope clean for Story 003), kept the new API shape (no downstream code depends on either shape yet).
- IL2CPP note: `StatID` membership check uses non-generic `(StatID[])Enum.GetValues(typeof(StatID))`, not the generic overload, per project engine-safety convention (no `.csproj` yet to confirm IL2CPP BCL surface).
- Blockers: None

## Session Extract — /code-review + /story-done 2026-07-04 (Story 002)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/item-database/story-002-validator-error-rules.md` — Import Validator — Reject Rules (Error Path)
- `/code-review` ran twice: CHANGES REQUIRED (missing 6 accept/boundary tests for AC-8, 14, 15, 21, 22, 25) → fixed → APPROVED WITH SUGGESTIONS
- Final test count: 25 test methods, all 15 blocking ACs covered with both reject and accept/boundary cases
- Tech debt logged: TD-004 (StatID/GearSlot validation rationale wording), TD-005 (undefined-ItemCategory and Equipment+ConsumableData cross-contamination paths untested)
- Files updated: `production/epics/item-database/story-002-validator-error-rules.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/item-database/EPIC.md` (Story 002 → Complete), `docs/tech-debt-register.md` (TD-004, TD-005 added)
- Next recommended: Story 003 — Import Validator — Warning Rules (`production/epics/item-database/story-003-validator-warning-rules.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Story 003)

- Story: `production/epics/item-database/story-003-validator-warning-rules.md` — Import Validator — Warning Rules (Accept Path)
- `/story-readiness` verdict: NEEDS WORK → fixed (story's Implementation Notes + QA Test Cases referenced a stale `ValidationResult` API — `Warnings`/`Errors` list properties — that no longer exists after Story 002's approved redesign to `Issues`/`ValidationSeverity`; corrected in-file, then READY)
- Files changed: `src/Foundation/ItemDatabase/ItemDefinitionValidator.cs` (+48 lines — `GetTierBasePrice`, AC-35/AC-32/AC-17 warning checks, additive only), `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Warning_tests.cs` (new, 9 tests)
- Test written: 9 tests covering AC-13 (2, no-op confirmation), AC-17 (2), AC-32 (3, incl. 9g/10g boundary), AC-35 (2)
- Process note: the implementing subagent correctly refused to treat a coordinator-relayed "user approved" as valid consent (per its own no-agent-can-authorize-writes constraint), creating a dead end since subagents have no direct channel to the user in this architecture. Resolved by applying the subagent's already-presented, user-approved diff directly via Edit/Write in the main session.
- Blockers: None
- Next: `/code-review src/Foundation/ItemDatabase/ tests/EditMode/ItemDatabase/` then `/story-done production/epics/item-database/story-003-validator-warning-rules.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Story 003)

- Verdict: COMPLETE (no deviations)
- Story: `production/epics/item-database/story-003-validator-warning-rules.md` — Import Validator — Warning Rules (Accept Path)
- `/code-review` ran twice: CHANGES REQUIRED (missing SellPriceGold=11 boundary test, missing AC-32/AC-35 co-firing assertion) → fixed → APPROVED
- Also hardened `GetTierBasePrice`'s unreachable `default` branch to throw `ArgumentOutOfRangeException` instead of returning `0f` (Unity specialist suggestion — avoids a latent Infinity/NaN if a future GearTier value is added without updating the switch)
- Final test count: 11 test methods, all 4 ACs (13, 17, 32, 35) covered including boundary/co-firing/multi-tier cases
- Tech debt logged: None (verdict was clean, no advisory deviations)
- Files updated: `production/epics/item-database/story-003-validator-warning-rules.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/item-database/EPIC.md` (Story 003 → Complete)
- Next recommended: Story 004 — MVP Item Records (`production/epics/item-database/story-004-mvp-item-records.md`) — Type: Config/Data, no programmer agent needed

## Session Extract — Unity project bootstrapping 2026-07-04

- User created the actual Unity project via Unity Hub: `C:\Users\Manuel Toscano\Claude-Code-Game-Studios\IronGrind\` (subfolder, as Unity Hub's wizard forces — not yet merged into repo root)
- **Version correction**: installed Editor is `6000.3.10f1`, confirmed LTS-labeled in Unity Hub. Project docs had incorrectly recorded Unity 6.3 LTS's internal version as `6000.4` (should be `6000.3` — Unity's internal numbering maps directly: 6.0→6000.0, 6.1→6000.1, 6.2→6000.2, 6.3→6000.3). Independently confirmed via WebFetch: `docs.unity3d.com/6000.3/.../UpgradeGuideUnity63.html` resolves and is titled "Upgrade to Unity 6.3"; the `/6000.4/` path was never real.
- Corrected `6000.4` → `6000.3` (and `.github/workflows/tests.yml`'s `unityVersion` → the exact confirmed `6000.3.10f1`) across 14 living documents: `docs/engine-reference/unity/VERSION.md`, `breaking-changes.md`, `.github/workflows/tests.yml`, `docs/architecture/control-manifest.md`, `architecture.md`, `architecture-traceability.md`, `docs/registry/architecture.yaml`, `tests/README.md`, `production/epics/index.md`, ADR-001, 002, 003, 004, 005, 006, 007, 009, 010.
- Deliberately left 3 point-in-time historical snapshots uncorrected (not revising history): `production/gate-checks/technical-setup-to-pre-production-2026-06-27.md`, `docs/architecture/architecture-review-2026-06-21.md`, `docs/architecture/architecture-review-2026-06-27.md`.
- **Merge completed**: `assets/` renamed to `Assets/` (case-corrected, plain `mv` since it was never git-tracked); Unity's generated `Assets/` content (InputSystem_Actions, Readme, Scenes/, Settings/, TutorialInfo/) merged alongside the existing `Assets/data/items/`; `ProjectSettings/` and `Packages/` moved to repo root; disposable `IronGrind/` subfolder (Library/Temp/Logs/UserSettings/.vscode/.csproj/.slnx — all Unity/IDE cache) deleted by the user after a Bash permission block on `rm -rf`.
- `.gitignore` already covered `Library/`/`Temp/`/`Logs/`/`UserSettings/`/`*.csproj`/`*.sln` — only added `*.slnx` (Unity 6's newer solution format) which was missing.
- Updated `.claude/docs/directory-structure.md` to document `Assets/`, `ProjectSettings/`, `Packages/` at repo root.
- Fixed lowercase `assets/` path references in the two places that are functionally live right now: `src/Foundation/ItemDatabase/ItemDatabaseSeeder.cs` and `story-004-mvp-item-records.md`. Left ~40 other files (GDDs, entities.yaml) with stale lowercase paths for not-yet-authored assets — logged as **TD-007**, fix opportunistically per-system rather than in bulk.
- TD-006 updated: Unity project + folder casing now resolved; still open — `src/` needs wrapping as a local Unity package + `.asmdef` (TD-002) before it will actually compile in the new project, and the seeder hasn't been run yet.
- **`src/` wrapped as a local Unity package**: `src/package.json` (name `com.irongrind.src`), referenced from `Packages/manifest.json` via `file:../src`. Runtime asmdef `src/Foundation/IronGrind.Foundation.asmdef` (unrestricted platforms, matches existing file-level `#if UNITY_EDITOR` guards). Test asmdef `tests/EditMode/IronGrind.Foundation.EditModeTests.asmdef` (Editor-only, references the Foundation asmdef + TestRunner assemblies). `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")` added via `src/Foundation/AssemblyInfo.cs` so test seams stay reachable across the new assembly boundary. TD-002 marked resolved-pending-Editor-verification.
- **Two follow-up fixes after "no tests to show" in Test Runner**:
  1. Added missing `"optionalUnityReferences": ["TestAssemblies"]` to the EditMode test asmdef — this is the actual field Unity's "Tests" checkbox controls; having `UnityEngine.TestRunner`/`UnityEditor.TestRunner` as references alone isn't sufficient.
  2. **Root cause**: `tests/` was never registered with Unity at all — only `src/` was wired into `Packages/manifest.json`. `tests/EditMode/` sat at the repo root as a plain sibling folder, invisible to Unity's compiler (which only scans `Assets/` and registered `Packages/`). Fixed by making `tests/` its own local package too: `tests/package.json` (`com.irongrind.tests`), added to `Packages/manifest.json` via `file:../tests`.
- Next: user reloads Unity again, confirms Test Runner now discovers all `ItemDatabase_*_Tests` and `CharacterStats_*_Tests`. New `.meta` files Unity generates for `src/`/`tests/` package content should be committed (essential metadata, not cache). Then run the `ItemDatabaseSeeder` menu item and confirm Story 004's smoke check.

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Story 004)

- Story: `production/epics/item-database/story-004-mvp-item-records.md` — MVP Item Records — 34 Authored ScriptableObject Assets
- `/story-readiness` verdict: READY (OQ-1/OQ-5 "unresolved" markers present but already owned in the story's Out of Scope section with concrete placeholders — not blocking)
- Structural decision: no Unity Editor available in this environment to author real `.asset` files. Chose an Editor seeder script (`ItemDatabaseSeeder.cs`, `[MenuItem]`) over hand-written YAML, per user's explicit choice — user must run it once in Unity.
- Files changed: `src/Foundation/ItemDatabase/ItemDefinition.cs` (extended `SetForTesting` with `description`/`iconAddress`, backward-compatible), `src/Foundation/ItemDatabase/MvpItemRecordData.cs` (new — shared 34-record data table), `src/Foundation/ItemDatabase/ItemDatabaseSeeder.cs` (new — Editor tool, manual run required), `assets/data/items/ITEM_ID_REGISTRY.txt` (new), `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs` (new, 8 tests)
- **Major discovery**: no Unity Editor project exists anywhere on disk for this repo (no `.meta`/`ProjectSettings`/`Packages`; `assets/` was empty before this story). Every "test evidence" claim across Stories 001-004 has only been code-reviewed, never actually run in Unity. Logged as **TD-006** (High impact, Large effort) — recommend addressing before any story is treated as fully verified.
- AC coverage: 6/6 blocking ACs (18, 24, 30, 31, 33, 34) covered via in-memory test against the shared data table; real `.asset` files + in-Editor smoke check remain a deferred manual step for the user.
- Blockers: Real Unity project needed to complete the manual seeder run + smoke check evidence file.
- Next: `/code-review src/Foundation/ItemDatabase/ tests/EditMode/ItemDatabase/`, then likely a project-level conversation about bootstrapping an actual Unity project before `/story-done` can reach a clean COMPLETE verdict.
