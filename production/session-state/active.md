# Session State

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-22 (Networking Core Story 029 — SetTarget RPC & Target Slot Management — EPIC COMPLETE, all 29 stories)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — self-performed `/code-review` (unity-specialist 1 Required Change→fixed + qa-tester 0 findings, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-029-settarget-rpc-target-slot-management.md` marked Complete. `EPIC.md` Story 029 row updated to Complete. **This closes the entire Networking Core epic — all 29 stories (001–029) are now Complete.**
- `/story-readiness` found no blockers — clean pass (the only note: `TR-net-003` isn't in `tr-registry.yaml`, but that registry is empty project-wide across all 20 stories in this epic that reference `TR-net-XXX`, a pre-existing systemic gap never treated as a blocker; Story 029 also did NOT have the Type/Test-Evidence self-contradiction Stories 027/028 had, confirming that was isolated to those two).
- New production: `SetTarget`/`SetTargetCodec` (standalone C→S R-OD message, reuses the existing `ClientEntityMessageEnvelope` rather than inventing a new envelope type; `MessageTypeId = 0xE040`; deliberately bypasses `WireIdCodec.SerializeEntityId` for the `targetEntityId` body field since `0` = "deselect" is a legitimate wire value there, unlike every other EntityID field in the protocol — a real footgun I flagged explicitly to the implementing agent up front since it's the opposite of this codebase's default CR-NET-7.3 zero-write-guard convention), `SetTargetOutcome` (3-value enum), `TargetSlotTracker` (new sealed **stateful** per-client target-slot store — the first stateful class in this RFR cluster, unlike Story 028's stateless-static `RelevanceFilter`). `RpcTypeTag`/`CrossCuttingRpcGuardChain` (Story 010) extended with a `SetTarget` rate-limit bucket at gap=0 ticks (GDD's own Cross-Cutting Constraint 3: "All other RPCs: no rate limit specified at MVP" — verified this actually behaves as unconstrained against `StaleDiscardComparer.IsTickExpired`'s equality-is-expired semantics before committing to it). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnSelfTargetAttemptLogged`/`OnInvalidTargetEntityIdLogged`.
- **Design decisions I resolved before implementation** (given to the implementing engine-programmer agent as settled, not re-derived by it): (1) `TargetSlotTracker` does not call `CrossCuttingRpcGuardChain` itself — transport-boundary rejection and RFR-specific business validation stay separately layered, composed by the caller, matching `MobDeTargetingCoordinator`/`PartyDisbandCoordinator`'s established delegate-seam precedent; (2) AC-RFR-03's before-flush/after-flush timing claim needs no tick-boundary machinery — proven purely by call-ordering in the test, matching this epic's "structural ordering proof, not a real timer" idiom; (3) `validZoneEntityIds` is a plain caller-supplied collection, not an injected provider — no real zone entity registry exists yet (same forward-dependency treatment as `RelevanceFilter`'s `partyMembers`).
- **Process note**: made the exact coordination mistake this session's own history already warned about (Story 022's note: "don't relay design approval via a fresh `Agent` spawn — resume the same instance via `SendMessage`"). Repeated it here: spawned a fresh `Agent` to say "yes approved," which correctly refused (no memory of the plan it never saw). Corrected by using `SendMessage` to the original agent's ID. Re-noting this for future sessions since it recurred once already despite being documented.
- Code review found 0 BLOCKING + 1 Required Change, applied: unity-specialist caught that the self-target-before-zone-validity check order (RFR-5 checked before RFR-3a) was correctly implemented but not actually pinned by any test — the original AC-RFR-05 test's `validZoneEntityIds` fixture happened to include the client's own EntityID, so both possible check orderings would have produced the same passing result. Fixed by adding `ProcessSetTarget_SelfTargetAndNotInValidZone_RejectedAsSelfTarget_NotAsInvalidTarget`, which deliberately excludes self from `validZoneEntityIds` so only the correct order passes. qa-tester's parallel review found 0 findings — confirmed all 3 blocking ACs are genuinely (non-tautologically) proven, including AC-RFR-03's timing distinction, the one most susceptible to a trivially-passing test.
- Test count: 11 (`tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs`) — independently grep-verified by me both before and after the fix (10→11), matches self-report.
- **No live Unity Editor was open/available this session** (unlike Story 028) — verification is static only (grep-confirmed counts, hand-read assertions, 2 parallel specialist reviews). Story file's Test Evidence section notes this explicitly and recommends a live-Editor confirmation pass before treating this story as launch-ready.
- Tech debt: TD-030 logged (register now 30 items) — the combined "party-set change + `SetTarget` same-tick" ordering scenario from this story's own Implementation Notes is untestable until a real Party System exists; correctly deferred per qa-tester's proactive flag, not a gap in this story.
- Not committed to git — the entire epic (29 stories) remains uncommitted in the working tree, as it has been all session.
- Next: **The Networking Core epic's full story set (001–029) is now Complete.** No further stories are queued in this epic. Recommend: (1) a live Unity Editor full-suite run to confirm everything compiles and passes together, now that all 29 stories are code-complete; (2) `/team-qa sprint` or a milestone review before considering this epic launch-ready; (3) decide whether to commit this large uncommitted working tree, and whether to start the next epic (per `production/epics/`, check what other epics exist or need `/create-epics`).

## Session Extract — TD-029 resolution 2026-07-21 (post-Story-028, user-driven, interactive Unity Editor)

- **TD-029 fully resolved this session** — the user opened the Unity Editor themselves (colliding with a batchmode attempt, confirming they had it open independently) and walked through the 5 pre-existing test failures one at a time by pasting Test Runner output; each was diagnosed and fixed in `tests/EditMode/Networking/`, none required production code changes:
  1. **AC-GH-14**: test only expected `RecordFailedReAuthAttempt`'s transition, missed that `EnterReconnecting` (called immediately before it) also fires its own `OnSessionStateTransitioned` — fixed to expect both, in order.
  2. **AC-GH-10** and **AC-GH-11** (same bug, found second one proactively once the pattern was clear): both asserted `callOrder` *after* appending `"persist"` instead of before, so the assertion always saw its own just-added entry as an unexpected extra — fixed by reordering assert-then-append.
  3. **AC-GH-7** (found proactively, same `GhostCleanupSequencer` cluster predicted in the original TD-029 entry): passed `targetingMobIds: Array.Empty<uint>()` while asserting a `"deTarget"` callback still fired — `MobDeTargetingCoordinator.ProcessTTLExpiry` only invokes the callback once per array entry, so an empty array can never produce it — fixed by supplying a real mob ID.
  4. **Snapshot tuple mismatch** (Story 018): `Assert.AreEqual((9, 9, 9), stored.RespawnPosition)` compared a compiler-inferred `(int,int,int)` literal against the field's actual `(short,short,short)` type — two different `ValueTuple` types with identical `ToString()`, hence the puzzling "Expected: (9,9,9), But was: (9,9,9)" — fixed with explicit `short` casts.
- TD-029 updated in `docs/tech-debt-register.md` from "Backlog" to "Closed," with per-fix root causes documented for future reference.
- My original TD-029 prediction (that AC-GH-10/AC-GH-11/AC-GH-7 shared one root cause) was half right: AC-GH-10/AC-GH-11 did share the identical test bug; AC-GH-7 turned out to be a distinct, unrelated test-setup bug in the same file cluster, not the same root cause.
- Not committed to git.
- Next: pick up Story 029 (SetTarget RPC & Target Slot Management) when ready, or have the user's own open Editor do a final full-suite confirmation of these 5 fixes.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 028 — Relevance Filter Algorithm — FIRST LIVE UNITY EDITOR VERIFICATION THIS SESSION)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — all gates skipped; self-performed `/code-review` (unity-specialist 1 BLOCKING→fixed + qa-tester TESTABLE, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-028-relevance-filter-algorithm.md` marked Complete. `EPIC.md` Story 028 row updated to Complete (also corrected Type column Integration→Logic).
- **`/story-readiness` found the exact same Type/Test-Evidence self-contradiction Story 027 had** (header Logic + EditMode QA Test Cases vs. Test Evidence section demanding PlayMode/playtest) — same fix applied, TD-028 updated to note the recurrence across two consecutive stories (possibly a stale template pattern; Story 029 checked and does NOT have the issue, so not universal).
- **MAJOR SESSION EVENT: a live Unity 6.3.10f1 Editor became available in this environment for the first time**, discovered when the implementing agent used it to attempt a real test run. This is a first for this entire session — every prior story (025-027) was verified only statically. Found and fixed (with user approval) 3 pre-existing compile errors blocking ALL EditMode compilation, none related to Story 028: `SessionTokenStore.cs` (Story 016) used `RandomNumberGenerator.GetBytes(int)`, a .NET 6+ static overload Unity's runtime doesn't expose — replaced with `RandomNumberGenerator.Create().GetBytes(buffer)`; `SessionToken_NSCRT_tests.cs` (Story 016) was missing `using System.Security.Cryptography;`; `MessageRouting_SelfDamageExclusivity_tests.cs` (Story 027) captured a `Span<byte>` ref-struct local inside a lambda (C# forbids this) — changed to `byte[]`.
- New production: `EntityHealthUpdate`/`PartyMemberHealthUpdate` (new concrete R-U batch sub-message schemas, first implementation — previously only opaque `RUBatchCategory` entries; `MessageTypeId = 0x0304`/`0x0305`), `RelevanceFilter` (the RFR-2 algorithm itself, stateless static class, `MAX_PARTY_SIZE=4` new constant). `BatchSubMessageCodec.cs` extended with matching `Write*`/`TryRead*` trios for both new types. Party membership modeled as a plain `IReadOnlyList<PartyMemberHealthUpdate>` (no provider interface), matching `PartyDisbandCoordinator`'s established mock-provider precedent from Story 020. `EntityHealthUpdate`/`PartyMemberHealthUpdate` reused as dual wire-schema/domain-input types for `RelevanceFilter`'s parameters, matching `DamageEvent`/`GoldSyncEvent`'s established pattern.
- **Registry completeness gap discovered and fixed, spanning 3 stories**: this story's own 2 new message types needed `MessageRoutingRegistry` rows (per MCR-2's own rule). While adding them, running the real live-Editor completeness test (`Registry_ContainsExactlyOneRowPerRealWireProtocolMessageTypeId_AC_MCR_04_AC_CCR_09`, Story 025) for the first time revealed Story 026's `GoldSyncEventForcedDelivery` and Story 027's `SelfDamageEvent` were ALSO never registered — an invisible gap since this test could never execute before. Added all 4 rows, cross-referenced against MCR-2/CCR-3 GDD text directly. Code review caught one real error in my own additions: `PartyMemberHealthUpdate`'s direction was set to `MessageDirection.ServerToParty` (broadcast-identical-to-everyone semantic) when the GDD's literal "S→C (per-client for party)" requires `ServerToOwningClient` (per-client-tailored semantic, matching what `RelevanceFilter` actually implements) — fixed and re-verified via a full live test run.
- **TD-029 logged — a major, separate finding**: the first real test run surfaced 5 genuine, pre-existing failures in already-"Complete" Stories 018/020/021, undetected this entire session because nothing had ever compiled/run before. User explicitly chose to log as tech debt and continue rather than fix now. 3 of the 5 (AC-GH-10, AC-GH-11, AC-GH-14 pattern) likely share one root cause in `GhostCleanupSequencer`'s step-ordering (Stories 019-021's shared cleanup code) — full diagnostics captured in TD-029 for whoever picks it up. Confirmed via repeated live runs that all 5 are 100% unrelated to Story 028's own changes (725/730 passing both before and after Story 028's fixes, with the specific 5 failures identical throughout).
- Test count: 11 (`tests/EditMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs`) — independently verified via grep AND, for the first time this session, via genuine live-Editor pass/fail confirmation (not just static code reading).
- Tech debt: TD-029 logged (register now 29 items, "Bug"/High-impact category — distinct from this epic's usual "Test Debt"/Low-impact forward-dependency placeholders).
- Not committed to git.
- Next: Story 029 (SetTarget RPC & Target Slot Management), `Status: Ready`, no Type/Test-Evidence contradiction found. **Recommend running the live Unity Editor test suite as part of every future story's verification now that it's available** — static review alone already proved insufficient to catch the registry-direction bug and the 3 pre-existing compile errors this session.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 027 — SelfDamageEvent vs DamageEvent Delivery Exclusivity)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — all gates skipped; self-performed `/code-review` (unity-specialist 1 BLOCKING→fixed + qa-tester TESTABLE, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-027-selfdamageevent-damageevent-exclusivity.md` marked Complete. `EPIC.md` Story 027 row updated to Complete (also corrected its Type column from Integration to Logic, matching the story-file fix below).
- **`/story-readiness` found a real internal self-contradiction**: the story's header said `Type: Logic`, its QA Test Cases section pointed at an EditMode test file, but its own Test Evidence section separately declared `Story Type: Integration` requiring a `tests/PlayMode/...` test or documented playtest — the first story in this epic to name that tier at all, and inconsistent with every prior story. Asked the user (given this is a live MMORPG, the trade-off was worth surfacing rather than silently picking one): resolved by aligning this story with the established EditMode-composition precedent (matches the GDD's own AC-MCR-05/08 text naming `ITransportFaultInjector`/`INetworkTestObserver` as the expected mechanism), and logging **TD-028** as a separate, project-wide, unresolved question — whether this MMORPG needs a real PlayMode/multi-client Integration testing tier before launch. Not solved inside this story; needs a producer/technical-director decision.
- New production: `SelfDamageEvent`/`SelfDamageEventCodec`/`SelfDamageEventDispatcher` (the first real implementation of `SelfDamageEvent`, a standalone R-OD sibling of `DamageEvent`, mirroring Story 026's `GoldSyncEventForcedDelivery` shape; `MessageTypeId = 0xE030`), `SelfDamageSuppressionGate` (EC-MCR-2 non-arrival/stale-on-arrival suppression, reusing `StaleDiscardComparer.IsTickExpired`), `SelfDamageRecipientGuard` (EC-CCR-2 wrong-recipient client-side defense), `CycleTimerInterpolator` (the first client-render-oriented logic class in this codebase — linear extrapolation loss-tolerance for `CycleTimerBroadcast`, AC-MCR-05/08). `INetworkTestObserver`/`NetworkTestObserver` extended with one new callback (`OnSelfDamageDirectionViolationLogged`) — every other observer hook this story needed (`OnServerSelfDamageEventSerialized`, `OnClientSelfDamageEventReceived`, etc.) was already pre-built by Story 002, unused until now.
- **Design decision I resolved before implementation, given to the implementing agent as settled**: `SelfDamageEventCodec`'s body encoding is deliberately duplicated from (not factored out of) `BatchSubMessageCodec.WriteDamageEvent`, unlike Story 026's shared-body-method precedent for `GoldSyncEvent` — because this story's Out of Scope explicitly forbids touching `DamageEvent`'s existing R-U path, and the story already spans enough new files without adding another out-of-scope file to the diff. Duplication cost accepted as small since both paths already share the true error-prone primitives (`WireIdCodec`, `WireEnumCodec`, `BinaryPrimitives`).
- **A genuine off-by-one bug I caught myself by cross-referencing the GDD's exact wording**, before formal code review: `SelfDamageSuppressionGate.IsStaleOnArrival` reused `StaleDiscardComparer.IsTickExpired`'s native "true at equality" semantics, but the GDD's EC-MCR-2 text has two suppression rules with *different* boundary semantics — rule 1 ("within 5 tick periods... suppress") has no strict qualifier (inclusive, correct as originally implemented), rule 2 ("more than 5 ticks older... suppress") has an explicit strict qualifier that the inclusive `IsTickExpired` reuse violated at exactly 5 ticks stale. Both the implementation and its own test agreed with each other but disagreed with the GDD. Fixed via a `+1` adjustment to `IsStaleOnArrival`'s `expiryTick` computation, converting the helper's native `>=` into the strict `>` the GDD requires; both specialists independently re-derived this same asymmetry from the GDD text during formal code review and confirmed it correct.
- Code review found 1 BLOCKING + 2 Required Changes (including the boundary fix above, applied pre-review), plus 1 more found during formal review, all applied: (1) **BLOCKING**, unity-specialist, independently hand-traced and confirmed by me: `CycleTimerInterpolator.RecordReceivedSample` had no stale/out-of-order guard despite `CycleTimerBroadcast` being delivered over U-U (ADR-004: explicitly "no ordering") — a reordered stale packet could silently make the charge bar jump backward, violating AC-MCR-08. Fixed by guarding with `StaleDiscardComparer.IsNewerVersion`, matching this codebase's established discipline for every other reordering hazard. (2) qa-tester: `SelfDamageEventDispatcher`'s structural "singleton recipient only" claim had no test (unlike the parallel, already-existing reflection test for `SelfDamageSuppressionGate`'s analogous claim) — added a matching reflection-based structural test.
- Test count: 17 (`tests/EditMode/Networking/MessageRouting_SelfDamageExclusivity_tests.cs`) — went through TWO self-report discrepancies this story (agent initially claimed "16," actual was 14; after the 2 post-review fixes agent again said "16," actual was 17) — independently verified via grep at every single stage by me and separately by both specialists, converging consistently on the correct number each time.
- Tech debt: TD-028 logged (see above — project-wide PlayMode/Integration testing tier gap, register now 28 items).
- Not committed to git.
- Next: Story 028 (EntityHealthUpdate/PartyMemberHealthUpdate Relevance Filter Algorithm) or Story 029 (SetTarget RPC & Target Slot Management), both `Status: Ready`. Note: `EPIC.md`'s Story 028 row also lists Type as "Integration" — worth checking during that story's own `/story-readiness` pass for the same kind of self-contradiction this story had.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 026 — GoldSyncEvent Forced-Delivery Overflow Policy)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — all gates skipped; self-performed `/code-review` (unity-specialist CLEAN + qa-tester GAPS, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-026-goldsyncevent-forced-delivery-overflow.md` marked Complete. `EPIC.md` Story 026 row updated to Complete.
- `/story-readiness` found a real, concrete blocker before implementation: both blocking ACs (AC-MCR-01, AC-MCR-07) depend on `IZoneTestConfigurator.SetBatchSizeLimit(int)`, confirmed absent from the interface. Resolved (user-approved) as an in-scope addition to this story, matching `SetZoneCapacity`'s existing sibling pattern.
- New production: `GoldSyncEventForcedDelivery` (field-less marker struct owning provisional `MessageTypeId = 0xE020`), `GoldSyncForcedDeliveryCodec` (standalone R-OD envelope+body wire encoding, no batch framing), `GoldSyncForcedDeliveryTracker` (the core two-counter stateful tracker — see below). `BatchSubMessageCodec.cs` refactored (behavior-preserving) to share `GoldSyncEvent` body-encoding between the R-U batch path and the new standalone path. `IZoneTestConfigurator`/`ZoneTestConfigurator` extended with `SetBatchSizeLimit`; `INetworkTestObserver`/`NetworkTestObserver` extended with 3 new callbacks.
- **Key design work — a two-counter reading of MCR-4, caught and corrected twice before any code was written**: the implementing engine-programmer agent first proposed a design, then during my design review I traced it against F-MCR-1's own rate formula (`TICK_RATE_HZ ÷ (GOLD_MAX_CONSECUTIVE_DROP+1)` = 5/s) and the GDD's literal "100 ticks (5 seconds at 20Hz)" anomaly-threshold text, and found the second counter (`TicksSinceForcedDeliveryRequired`) was defined to increment only on forced-delivery-*fire* ticks rather than every real elapsed tick — a 4x timing bug (would take ~20s instead of 5s to trigger the anomaly). The agent applied the fix, then independently caught and fixed a SECOND bug during its own pre-implementation reasoning (checking the threshold only inside the confirm-call, which under the verified 4-tick cycle mathematically never lands on tick 101). I hand-traced the corrected state machine myself afterward and confirmed both fixes are correct.
- The implementing agent also independently corrected a mistaken claim of mine: I had told it `IZoneTestConfigurator.SetZoneCapacity` was "wired to a real production consumer" (based on a loose grep match); the agent checked `ZoneSessionStateMachine.cs`'s own doc comment and found it explicitly states the opposite. I re-verified this myself and confirmed the agent was right — `SetBatchSizeLimit`'s honest "not yet wired" documentation (TD-027) is consistent with `SetZoneCapacity`'s actual (also unwired) status, not a weaker analogy as I'd assumed.
- Code review found 0 BLOCKING + 2 Required Changes, both applied: (1) qa-tester caught that the specific test built to pin the corrected two-counter design (`RecordRUDeliveryOutcome_RealisticFourTickSustainedOverflowCycle_...`) only asserted against the tracker's own internal counter — tautological, since it would still pass under the exact historical bug (just after ~3x more loop iterations). I independently hand-traced this and confirmed it — added a discriminating `Assert.AreEqual(103, safetyTickBound, ...)` real-tick-count pin. The regression was still incidentally caught by two other, simpler tests in the file, so this wasn't a BLOCKING gap, just a real gap in the test purpose-built for the job. (2) unity-specialist caught a doc-comment inaccuracy (claimed to mirror `GhostEntityTracker`'s "no teardown method" discipline, but that class does have `RemoveGhost`, Story 021) — corrected to describe this tracker's stronger, permanent-by-design guarantee instead.
- Test count: 10 (`tests/EditMode/Networking/MessageRouting_GoldSyncForcedDelivery_tests.cs`) — independently verified via grep both before and after the fix, matches self-report, stable across the fix.
- Tech debt: TD-027 logged (`SetBatchSizeLimit` zero-production-call-sites forward-dependency gap, same class as TD-020/TD-026 — register now 27 items).
- Not committed to git.
- Next: Story 027 (SelfDamageEvent/DamageEvent Delivery Exclusivity), `Status: Ready`, depends on this story's now-closed registry work (Story 025) — no dependency on Story 026 itself.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 025 — Message Criticality/Channel Routing Table & Unclassified-Message Fallback)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-STORY-READY/QL-TEST-COVERAGE/LP-CODE-REVIEW gates all skipped; the self-performed `/code-review` (unity-specialist CLEAN + qa-tester, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-025-message-routing-table-unclassified-fallback.md` marked Complete. `EPIC.md` Story 025 row updated to Complete.
- `/story-readiness` found one real gap before implementation: AC-CCR-01's literal CI text-search wording ("Given the full codebase and docs...") was self-contradictory — the forbidden phrase "server SessionHandshake" necessarily appears in the GDD rule (`networking-channel-contract.md` CCR-2) that defines the prohibition, so a literal repo-wide grep could never pass. Fixed by scoping the AC to `src/`+`tests/` only, excluding `design/gdd/`/`production/epics/`, before implementation began.
- New production: `MessageRoutingRegistry` (`src/Foundation/Networking/WireProtocol/`) — the unified MCR-2/CCR-3 routing table (one `MessageRoutingEntry` struct satisfies both AC-MCR-04 and AC-CCR-09 by construction), plus `DesignPillar`, `NetworkChannel`, `MessageDirection`, `MessageDeliveryContext`, `MessageRoutingEntry`, `MessageRoutingResult`, `PendingSchemaDispatchException`, `BuildConfiguration` (establishes this codebase's first debug/release build-symbol distinction, `DEVELOPMENT_BUILD`), `ConnectionSequenceCounter` (CCR-1's shared per-connection counter primitive). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnUnclassifiedMessageTypeLogged`.
- Key design decisions resolved before implementation (proposed by the implementing engine-programmer agent, reviewed and approved): registry scoped only to the 5 message types that already exist as real classes in `WireProtocol/` (no centralized `MessageTypeID` enum exists in this codebase — each class carries its own `const ushort`); AC-MCR-04/CCR-09's CI check and AC-CCR-01's naming check both implemented as in-file C# reflection/text-search scanners under the existing blocking EditMode test job, reusing Story 008's `AC-AOT-1` scanner precedent rather than adding new `.github/workflows/tests.yml` jobs; `GoldSyncEvent` flagged as an MCR-3 exception despite being single-pillar in the MCR-2 table, resolving a real (harmless) looseness in the GDD's own AC-MCR-06 "multi-pillar" framing.
- Code review found 1 BLOCKING + 2 Required Changes, both applied: (1) **BLOCKING**, caught by qa-tester: the AC-CCR-01 real-tree scanner test scanned its own defining file, which necessarily declares the literal forbidden phrase (`ForbiddenPhrase = "server SessionHandshake"` plus fixture/assertion strings) — guaranteed to fail every run; fixed by excluding the scanner's own file via `[CallerFilePath]` (not a hardcoded string, survives renames), independently re-verified via grep that zero occurrences remain anywhere else under `src/`/`tests/`; (2) private field `MessageRoutingRegistry.Entries` renamed to `_entries` (naming-convention fix, all 4 use sites). 5 additional Suggestions from qa-tester (untested struct equality/hashing, untested exception message content, scanner blind-spot documentation, etc.) were surfaced but declined at user's explicit direction ("apply the required changes") — not logged as tech debt, noted in Completion Notes instead.
- Test count: 28 (`tests/EditMode/Networking/MessageRouting_CriticalityChannelTable_tests.cs`) — independently verified via grep both before and after the blocking fix, matches self-report exactly, stable across the fix (fix modified an existing test + added one non-test helper method, no new `[Test]`).
- Tech debt: TD-026 logged (`ConnectionSequenceCounter` zero-production-call-sites forward-dependency gap, same class as TD-020 — register now 26 items).
- Test Evidence section's original wording (new `.github/workflows/tests.yml` CI scripts) was stale against the actual implementation approach taken — corrected in the story file to describe the in-file-scanner approach actually used.
- Not committed to git.
- Next: Story 026 (GoldSyncEvent Forced-Delivery & Overflow Drop Policy) or Story 027 (SelfDamageEvent/DamageEvent Delivery Exclusivity) — both `Status: Ready`, both depend on this story's now-closed registry.

## Session Extract — /code-review + /story-done 2026-07-20 (Networking Core Story 024 — OWL Compensation sub-cluster CLOSED — all 3 stories complete)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist CLEAN + qa-tester TESTABLE, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-024-owl-threshold-hysteresis-signal.md` marked Complete. `EPIC.md` Story 024 row updated to Complete. **OWL Compensation sub-cluster (022-024) is now fully closed.**
- New production: `OwlThresholdHysteresisTracker` (`src/Foundation/Networking/OwlCompensation/`) — sealed per-entity stateful class implementing CR-NET-8.3's ON/OFF hysteresis signal, deliberately not calling into Story 022's `LastBeatServerTickTracker` or Story 023's `OwlWrapCorrectionFormula` (this class only decides whether compensation is active at all). New constants `MAX_COMPENSATABLE_OWL_SECONDS = 0.12f`, `OWL_HYSTERESIS_BAND_SECONDS = 0.015f`. Also extended `INetworkTestObserver`/`NetworkTestObserver` with `OnConnectionQualityUpdateEmitted`, resolving a real observer-seam gap caught during `/story-readiness` (the story's own QA Test Cases needed an emission-count assertion with no existing hook).
- Key design decision resolved before implementation: a first-ever OWL sample for an entity that itself crosses the entry threshold DOES emit (no "was this entity previously tracked" suppression gate) — confirmed correct, not just defensible, since no other message in this codebase conveys initial connection-quality state at handshake; a session starting already degraded would otherwise never inform the client.
- A `>`/`<` boundary "conflict" in my own story-readiness/dev-story instructions turned out to be a non-issue — float32 precision on the exact GDD literals (0.135f vs computed 0.13499999f; 0.105f vs computed 0.104999997f, bit-identical) makes strict `>`/`<` produce exactly the required boundary behavior. Both specialists independently re-verified this via IEEE-754 tracing.
- Code review found 2 Required Changes + 2 Suggestions, all applied: (1) added `EnterThreshold_ExactComputedValue_DoesNotTrigger_FloatPrecisionProof` — the original entry-boundary test used a literal one ULP *above* the threshold, so it couldn't actually distinguish `>` from `>=` (only the exit-side test, using a bit-identical literal, proved strict comparison); (2) added `FirstSampleForEntity_AboveEntryThreshold_EmitsImmediately` — the approved first-sample-crossing design decision was previously only incidentally proven inside an unrelated-named test; (3) added `RepeatedIdenticalSample_WhileAlreadyFlipped_DoesNotReemit` (idempotency guard); (4) added a doc-comment `owlSeconds` non-negativity assumption note, mirroring Story 023's `OwlWrapCorrectionFormula` precedent.
- Test count: 12 (`tests/EditMode/Networking/OwlCompensation_ThresholdHysteresis_tests.cs`) — independently verified via grep, matches self-report exactly (no discrepancy, third story in a row without one after Story 022's two wrong self-counts).
- Tech debt: TD-025 logged (Story 002's exhaustive-reset test not extended to cover 3 newer observer callbacks, including this story's — register now 25 items).
- Not committed to git.
- Next: Networking Core epic continues past the now-closed OWL Compensation sub-cluster (022-024) to Story 025 (Message Criticality/Channel Routing Table & Unclassified-Message Fallback), `Status: Ready`.

## Session Extract — /code-review + /story-done 2026-07-20 (Networking Core Story 023 — second story of OWL Compensation sub-cluster)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist CLEAN + qa-tester TESTABLE, both parallel, zero blocking findings) served as this story's review — cleanest story in this epic to date.
- Story: `production/epics/networking-core/story-023-owl-wrap-correction-formula.md` marked Complete. `EPIC.md` Story 023 row updated to Complete.
- New production: `OwlWrapCorrectionFormula` (`src/Foundation/Networking/OwlCompensation/`) — stateless static `Evaluate()` method implementing CR-OWL-2/F-OWL-1, composing with Story 022's `LastBeatServerTickTracker.IsWithinWrapCorrectionWindow` rather than reimplementing the sentinel guard. New `SkillGraceWindowResult` readonly struct (matches `SessionHandshakeData`'s plain-data-bundle precedent).
- Uses the established optional-observer convention (`INetworkTestObserver observer = null`, guarded `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`, matching `MobDeTargetingCoordinator`'s precedent) to satisfy AC-OWL-05 — `OnSkillGraceWindowEvaluated` and its recorder list were pre-built by Story 002 specifically for this story.
- AC-NC-29 (cross-document citation from `networking-core.md`) handled as a documented cross-reference to AC-OWL-01/02's tests rather than a duplicate — independently verified by qa-tester reading the GDD source directly (`networking-core.md:488` explicitly says "See AC-OWL-01 and AC-OWL-02").
- Code review found 0 Required Changes + 1 Suggestion (doc-comment only, no new test needed): note the implicit non-negativity/positivity assumptions on `cycleTimer`/`owlSeconds`/`cycleDuration` in `Evaluate`'s doc comment. Applied directly by the orchestrator (not delegated) since it was a trivial one-line addition to an already-reviewed file with direct, current user approval.
- Test count: 8 (`tests/EditMode/Networking/OwlCompensation_WrapCorrectionFormula_tests.cs`) — independently verified via grep, matches self-report exactly (no discrepancy this time, unlike Story 022). All 4 blocking ACs (AC-OWL-01, AC-OWL-02, AC-OWL-05, AC-NC-29) COVERED.
- **Notable process friction this session**: the implementing agent refused to accept ANY `SendMessage`-relayed approval as consent to write — including a message explicitly quoting "Manuel, the project owner, confirming directly" — since the channel itself is always agent-to-agent and indistinguishable from fabrication, regardless of framing. Resolved only by instructing the agent to invoke `Write` directly and let the harness's own permission prompt reach the user without going through any relay. **Lesson for future stories**: if an implementing agent holds this line, don't keep re-wording `SendMessage` approvals — tell it to attempt the tool call directly instead.
- Also hit a minor tooling gap: one specialist's (qa-tester) background-task completion notification arrived without its `<result>` content (only usage stats) — resolved by sending it a message asking it to restate its findings, which worked cleanly on retry.
- Tech debt: None logged (only finding was documentation-only, already resolved in-line).
- Not committed to git.
- Next: Story 024 (OWL Threshold Suspension & Hysteresis Signal) — third and final story of the OWL Compensation sub-cluster (022-024), `Status: Ready`, depends on this story's `OwlWrapCorrectionFormula`.

## Session Extract — /story-done 2026-07-20 (Networking Core Story 022 — first story of OWL Compensation sub-cluster)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist + qa-tester, parallel) served as this story's review.
- Story: `production/epics/networking-core/story-022-lastbeatserverstick-slot-allocation.md` marked Complete. `EPIC.md` Story 022 row updated to Complete.
- New production: `LastBeatServerTickTracker` (`src/Foundation/Networking/OwlCompensation/`) — sealed per-zone-instance class implementing CR-OWL-1's `LastBeatServerTick uint[]` data structure + CR-OWL-4's player/mob slot allocation contract. New constants `MAX_MOBS_PER_ZONE = 150`, `MAX_WRAP_WINDOW_TICKS = 2`.
- Key design resolution (mine, given to the implementing agent as settled): the GDD's AC-OWL-03/04 text names "wrapCorrectionActive," but that's the full F-OWL-1 formula owned by Story 023. This story only owns the sentinel-guarded tick-window sub-check — `IsWithinWrapCorrectionWindow()` implements just that, with an honest doc-comment explaining the scoping (same idiom as every prior story this epic).
- Code review found 1 Required Change + 3 Suggestions, all applied: (1) `DeallocateMobSlot` was missing a "never allocated" guard (only checked double-free) — unity-specialist traced a concrete double-allocation hazard (an early-freed never-issued index could later be handed out again via the independent fresh-index counter); fixed by collapsing the guard to `!= InUse`, symmetric with `DeallocatePlayerSlot`'s existing guard; (2) added `AllocateMobSlot_WithMultipleFreedSlotsInterleaved_ReusesInFreedOrder` (locks in FIFO free-list order); (3) added `DeallocateMobSlot_LastPlayerSlotIndex_ThrowsArgumentOutOfRangeException` (true adjacent-boundary test); (4) added `RecordBeat_OnFreedMobSlot_DoesNotThrowAndSilentlyOverwritesSentinel` (documents a known, accepted fragility — `RecordBeat` has no ownership guard).
- Test count: 27 (`tests/EditMode/Networking/OwlCompensation_SlotAllocation_tests.cs`) — independently verified via grep (two different patterns agreed) after the implementing agent's self-report was wrong twice in a row (claimed 25, then 23, actual 27 both before and after the fix round — pattern of unreliable self-counts continues from prior stories this epic).
- Process note: made a coordination error mid-session — tried to relay design approval via a fresh `Agent` spawn instead of `SendMessage` to the same agent instance; the fresh agent (correctly) refused to write without verifiable context, per this project's collaboration protocol. Corrected by resuming the actual agent via `SendMessage`.
- Tech debt: TD-024 logged (AC-OWL-06a/06c's ghost-period tests are structural, not integration-level — same forward-dependency class as TD-017 through TD-023; register now 24 items).
- Not committed to git.
- Next: Story 023 (OWL Wrap-Correction Compensation Formula) — second story of the OWL Compensation sub-cluster (022-024), `Status: Ready`, depends on this story's `LastBeatServerTickTracker`.

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 021 — Ghost Session cluster CLOSED — all 5 stories complete)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; unity-specialist (APPROVED) + qa-tester (GAPS, resolved) served as this story's review.
- Story: `production/epics/networking-core/story-021-ghost-cleanup-zone-crash-voluntary-dismissal.md` marked Complete. `EPIC.md` Story 021 row updated to Complete. **Ghost Session cluster (017-021) is now fully closed.**
- New production: `GhostEntityTracker.RemoveGhost` (CR-GH-10 step 4, fulfilling Story 017's own predicted need), `GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup` (new sibling, hardcodes `GhostDismissed`), `GhostDismissalCoordinator` (CR-GH-12, inlines de-target via a new shared `MobDeTargetingCoordinator.IssueDeTargetCommands` helper to avoid mislabeling `OnGhostCombatTTLExpired`), `ZoneCrashCleanupHandler` (CR-GH-11, new `GhostZoneCrashSession` struct), 2 new `PersistenceWriteReason` values (`GhostDismissed=6`, `GhostZoneCrash=7`).
- Key story-readiness finding: AC-GH-14 and AC-GH-16 needed **no new production code** — both are composition tests against already-tested Story 012/013/014/019 methods (confirmed via `RecordFailedReAuthAttempt`'s own "TTL never reset" structural guarantee and `EvaluateJoinAttempt`'s own "ghost sessions count toward capacity" doc comment).
- Code review found 2 Required Changes + several Suggestions, all applied: (1) AC-GH-14 test now reads `ConnectionStateMachine.TryGetSessionExpiryTick` back rather than reusing a hardcoded constant (qa-tester catch); (2) added a `ZoneCrashCleanupHandler` partial-failure/atomicity test (mirroring Story 020's `PartyDisbandCoordinator` precedent); (3) extracted the shared de-target loop helper; (4) added a `RemoveGhost` third-branch coverage test; (5) logged TD-021/022/023 for accepted composition-test/ordering limitations.
- Test count: 24 (`tests/EditMode/Networking/GhostSession_Cleanup_Crash_Dismissal_tests.cs`) — independently verified via grep + brace-balance, matches agent self-report exactly. All 5 blocking ACs (AC-GH-10, AC-GH-11, AC-GH-14, AC-GH-16, AC-GH-20) COVERED.
- Tech debt: TD-021, TD-022, TD-023 logged (register now 23 items total). Cluster-wide tech debt spans TD-017 through TD-023 (7 items) — primarily composition-test causal-link weaknesses and forward-dependency gaps (no real Party System, AI subsystem, or persistence/restart layer exists yet).
- Not committed to git.
- Next: Networking Core epic continues with Story 022 (LastBeatServerTick Slot Allocation & Data Structure) — first story of the OWL Compensation sub-cluster (022-024), `Status: Ready`, no dependency on the now-closed Ghost Session cluster.

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 020 — Ghost Session cluster, fourth story CLOSED)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist + qa-tester, both succeeded this time) served as this story's review.
- Story: `production/epics/networking-core/story-020-ghost-reward-forfeit-policy.md` marked Complete. `EPIC.md` Story 020 row updated to Complete.
- Code review found 2 Required Changes + 5 Suggestions; all applied except one (optional `preDisconnectXp >= 0` guard on `BeginTracking` — reviewer explicitly noted it matches existing precedent not to add, skipped and flagged to user):
  1. Applied: removed AC-GH-6's tautological `ZoneTestConfigurator` set-then-read-back assertion — rests solely on the party-roster proof now.
  2. Applied: honest documentation caveats added distinguishing AC-GH-7/12 (legitimate delegate-seam composition) from AC-GH-8 (materially weaker — no seam at all, two causally-independent checks).
  3. Applied: logged **TD-020** in `docs/tech-debt-register.md` for `GhostXpPoolTracker.ResolveFinalXp` having zero production call sites.
  4. Applied: added `ProcessPartyDisband_OneUntrackedMemberInList_ThrowsAfterProcessingEarlierMembers` (partial-failure/atomicity test).
  5. Applied: added `ResolveFinalXp_ThenEndTracking_SubsequentQueryThrows` + `ResolveFinalXp_IncludeShareTrue_AfterStopAccumulation_ReflectsFrozenPoolNotLaterAttempts`.
  6. Applied: sync-risk note added to `GhostXpPoolTracker.cs` class remarks (keep in step with `GhostEntityTracker`).
- Test count grew from 21 to 24 after fixes (verified via grep + brace-balance check on all 3 touched/new files).
- 5/5 blocking ACs (AC-GH-6, AC-GH-7, AC-GH-8, AC-GH-12, AC-GH-18) COVERED with full traceability. No BLOCKING deviations.
- Tech debt: TD-020 logged (total register now 20 items).
- Not committed to git.
- Next: Story 021 (Ghost Cleanup, Zone Crash & Voluntary Dismissal) — last story in the Ghost Session cluster, `Status: Ready`, depends on Stories 017-020 which are now all Complete.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 020 — Ghost Session cluster continues)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gap: all 5 ACs (AC-GH-6/7/8/12/18) dropped GDD "Pass condition" text — restored verbatim from `networking-ghost-session.md`. TR-net-006 registry gap noted as systemic, no new fix needed.
- Story: `production/epics/networking-core/story-020-ghost-reward-forfeit-policy.md` — fourth story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted). Dependencies (017/018/019) all Complete.
- Implemented via spawned `engine-programmer` agent (full context package: story, GDD AC text, ADR-004, control manifest, plus a pre-worked design from my own research). Agent's first message truncated mid-sentence after finishing the two production files (same pattern as Story 018) — resumed via SendMessage, completed the test file cleanly on the second pass.
- New classes: `GhostXpPoolTracker` (stateful two-pool XP registry, mirrors `GhostEntityTracker`'s shape; `ResolveFinalXp(characterId, includePostDisconnectShare)` is the single CR-GH-9/9.1/9.2 forfeit-vs-restore decision point) and `PartyDisbandCoordinator` (stateless static, mirrors Story 019's `MobDeTargetingCoordinator` shape — fires `OnPartyDisbanded` then loops `StopAccumulation` per ghosted member). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnPartyDisbanded(partyId, tickNumber)`, per the story's own Implementation Notes.
- AC-GH-7/AC-GH-12 compose with Story 018's already-tested `GhostCleanupSequencer` (HP-persistence) rather than duplicating it — same precedent as Story 019's AC-GH-9. AC-GH-8 calls the real, unmodified Story 013 `CompleteReAuthSuccess`. AC-GH-6 needed no new production code (absence proof via a test-local mock party roster + real `ZoneTestConfigurator` zone-state check).
- **Honest documentation note**: AC-GH-7/AC-GH-8's GDD pass-condition text references `OnSessionHandshakeEmitted` reporting `currentXp` — that callback has no XP field and was NOT extended (would touch Story 013's already-closed method/call sites). Resolved via `GhostXpPoolTracker.ResolveFinalXp` captured directly in each test's own closure instead — same idiom as Story 019's `MobDeTargetCommand` resolution. Documented in the test file's class remarks.
- Test count: 21 (`tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs`) — independently verified via direct grep count, matches the agent's own self-report exactly (no discrepancy this time). All 5 blocking ACs covered plus 12 null-guard/precondition-guard tests.
- Files updated: `src/Foundation/Networking/GhostSession/GhostXpPoolTracker.cs` (new), `src/Foundation/Networking/GhostSession/PartyDisbandCoordinator.cs` (new), `src/Foundation/Networking/TestHarness/INetworkTestObserver.cs` + `NetworkTestObserver.cs` (extended), `tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs` (new, 21 tests)
- Minor note flagged for code review: AC-GH-6's zone-state check (`SetZoneStateForTesting` → `GetCurrentZoneState`) is a set-then-read-back assertion, trivially true — not wrong, just weak, worth a look.
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- Next: `/code-review` on the files above, then `/story-done production/epics/networking-core/story-020-ghost-reward-forfeit-policy.md`.

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 019 — Ghost Session cluster, third story CLOSED)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` served as this story's review (both specialist agents hit the session's API rate limit and failed).
- Story: `production/epics/networking-core/story-019-ghost-death-mob-detargeting.md` marked Complete. `EPIC.md` Story 019 row updated to Complete.
- Code review found 3 suggestions; user approved 2 (applied), declined 1 (symmetric test in Story 018's closed suite — marginal value):
  1. Applied: honest caveat added to `GhostSession_DeathDeTargeting_tests.cs` class remarks — AC-GH-17's ordering proof is structural/test-constructed, not system-arbitrated (same accepted limitation class as Story 018's AC-CGS-3).
  2. Applied: in-test comment on `..._AC_GH_4` noting the XP-shares=0 clause is correctly out of scope (Story 020 owns it), not silently skipped.
  3. Declined: symmetric test on Story 018's `HandleGhostDeathWhileReconnecting` rejecting `Disconnected_SessionActive` — not built.
- 4/4 blocking ACs (AC-GH-4, AC-GH-5, AC-GH-9, AC-GH-17) COVERED with full traceability. No BLOCKING deviations; TR-net-006 registry gap remains the only systemic advisory.
- No new tech debt entries logged — both advisory items resolved via in-code documentation only, matching the precedent set by Story 018's AC-CGS-3 (documented, not separately tracked in the register).
- Not committed to git.
- Next: Story 020 (Ghost Reward Forfeit Policy — Two-Pool XP & Party Slot Retention) or Story 021 (Ghost Cleanup, Zone Crash & Voluntary Dismissal) — both `Status: Ready`, both depend on Stories 017-019 which are now all Complete.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 019 — Ghost Session cluster continues)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gaps: (1) all 4 ACs (AC-GH-4/5/9/17) dropped GDD "Pass condition" text — restored; (2) no test-observability resolution existed for `MobDeTargetCommand` issuance (confirmed no observer callback for it) — resolved via delegate seam, same precedent as Story 018; (3) AC-GH-9 substantially overlaps Story 018's already-tested AC-CGS-1 — clarified this story's test should compose with `GhostCleanupSequencer.CompleteTTLExpiryCleanup`, not re-derive; (4) TR-net-006 (systemic); (5) the story's own "confirm GHOST_COMBAT_TTL constant with design lead" note was stale — pointed to the already-established epic-level interim resolution instead.
- Story: `production/epics/networking-core/story-019-ghost-death-mob-detargeting.md` — third story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted).
- **Design decisions resolved before implementation**: (a) new `ConnectionStateMachine.CompleteGhostDeathFromDisconnected` — the normal (non-racing) `Disconnected_SessionActive → Disconnected_SessionExpired` via "GhostDeath" row, complementing Story 018's `HandleGhostDeathWhileReconnecting` (the `Reconnecting`-race variant); derives `characterId` from the record (learned from Story 018's own code-review fix); (b) new stateless-static `MobDeTargetingCoordinator.ProcessTTLExpiry` (mirrors `GhostCleanupSequencer`'s shape) — fires `OnGhostCombatTTLExpired` then issues a caller-supplied `MobDeTargetCommand` delegate per targeting mob, satisfying the 250ms `DE_TARGET_DEADLINE_MS` budget structurally (zero simulated delay), same "structural, not measured timing" idiom as every prior story; (c) AC-GH-9's test composes directly with Story 018's `GhostCleanupSequencer.CompleteTTLExpiryCleanup` rather than duplicating its HP-persistence logic; (d) AC-GH-17 needs no new production code — proven via a test-constructed entity-ID list filtered strictly after both cleanup calls complete (structural ordering, no new observer hook for "zone tick delivered").
- **Process note**: the agent stopped once for a genuine approval checkpoint (not a truncation) with a fully worked-out plan, including its own judgment call on AC-GH-17's exact test mechanics (a `List<uint>`/`HashSet<uint>` filter proof). Reviewed and approved before it wrote anything.
- Test count: 10 (`tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs`) — independently verified by direct count; the implementing agent's own self-report claimed 12, which was inaccurate — all 4 blocking ACs still fully covered (AC-GH-4: 4 tests incl. a guard distinguishing this method from Story 018's Reconnecting-race method; AC-GH-5: 4 tests; AC-GH-9: 1 composition test; AC-GH-17: 1 ordering test).
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `src/Foundation/Networking/GhostSession/MobDeTargetingCoordinator.cs` (new), `tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs` (new, 10 tests)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- Next: `/code-review` on the files above, then `/story-done production/epics/networking-core/story-019-ghost-death-mob-detargeting.md`.

## Session Extract — /story-done 2026-07-18 (Networking Core Story 018)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-018-pre-disconnect-snapshot-write-ordering.md` — Pre-Disconnect Snapshot & Write-Ordering (second story in the Ghost Session cluster, 017-021)
- 4/4 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); TD-019 logged (no `CrashStep` for the CGS-3 snapshot-WAL write specifically); AC-CGS-2's `wasKilledWhileDisconnected` clause honestly documented as unverifiable at this layer; AC-CGS-3's "priority" honestly documented as caller-discipline-enforced, not system-arbitrated (no dispatcher exists yet).
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 018 row → Complete.
- Tech debt logged this session: TD-019 (already reflected in the register before this closure — register now 19 items).
- Write-ordering foundation (`PreDisconnectSnapshotWal`, `GhostCleanupSequencer`, `ConnectionStateMachine.HandleGhostDeathWhileReconnecting`) now exists for Stories 019 and 021 to build on.
- Next recommended: Story 019 — Ghost Death & Mob De-Targeting — the natural next pick in the Ghost Session cluster (020/021 also `Status: Ready`).

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 018 — Ghost Session cluster continues)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gaps: (1) 3 of 4 ACs (AC-CGS-1/2/4) dropped GDD "Pass condition" text — restored; (2) AC-CGS-3 was missing a critical detail — the GDD's exact pass condition requires `fromState = Reconnecting`, not `Disconnected_SessionActive` (a death racing a reconnect already in progress) — restored with explicit callout; (3) AC-CGS-3's GDD test technique cites `ITransportFaultInjector` for injecting an inbound reconnect ACK — confirmed (same as Story 017's AC-GH-13) that interface is outbound-only, resolved by driving both code paths directly instead; (4) TR-net-006 (systemic); (5) added a performance note.
- Story: `production/epics/networking-core/story-018-pre-disconnect-snapshot-write-ordering.md` — second story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted) — the ghost-specific instance of CR-NET-5's commit-before-broadcast principle (Story 011).
- **Design decisions resolved before implementation**: (a) new minimal `PreDisconnectSnapshot` struct (HP + respawn position only — the GDD's full 14-field CGS-3 list deferred, since Character Stats/Inventory/Buffs systems don't exist yet, same forward-dependency discipline as every prior story); (b) new `PreDisconnectSnapshotWal` class, idempotent write keyed on `(characterId, disconnectTickNumber)` per EC-CGS-2; (c) new stateless-static `GhostCleanupSequencer` (mirrors `CommitBeforeBroadcastSequencer`'s shape) with `CompleteTTLExpiryCleanup`/`CompleteGhostDeathCleanup`, deliberately NOT calling into Story 015's `ConnectionStateMachine.CompleteSessionActiveTTLExpiry` (which hardcodes the wrong `PersistenceWriteReason`) — a new ghost-specific parallel path instead; (d) a genuinely new `ConnectionStateMachine.HandleGhostDeathWhileReconnecting` method — the fifth `Reconnecting`-adjacent transition row, confirmed no prior story built this; (e) both `GhostExpiredEvent` and `ZoneSessionEnded` modeled as caller-supplied delegate seams (confirmed no observer callback exists for either, and none added).
- **Process note**: the implementing agent's response was truncated mid-edit (same recurring failure mode this session), but this time all 4 production files (3 new classes + the `ConnectionStateMachine` extension) were already fully and correctly written before the cutoff — only the test file was missing entirely. Independently verified every production file in full before doing anything else. Resumed the same agent (preserving context) rather than restarting fresh, and it completed the test file cleanly on the second pass.
- Test count: 16 (`tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs`) — all 4 blocking ACs covered (AC-CGS-4 gets 2 tests: pure ordering + the named `IServerCrashInjector.AfterGhostCleanupPersistenceWrite` crash-durability scenario), plus WAL idempotency/query tests and null-guard/precondition tests for every new public method.
- **AC-CGS-3's test is the most structurally important one**: proves CGS-6's single-tick priority not via any synchronization primitive, but by calling `HandleGhostDeathWhileReconnecting` first, then attempting a real `ConnectionStateMachine.CompleteReAuthSuccess` call against the same account and asserting it throws `InvalidOperationException` (via `RequireState`, since the account is no longer `Reconnecting`) — independently verified this call order and every API signature involved.
- Files updated: `src/Foundation/Networking/GhostSession/{PreDisconnectSnapshot,PreDisconnectSnapshotWal,GhostCleanupSequencer}.cs` (new), `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs` (new, 16 tests)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — mechanically verified every claim (release-stripping, EC-CGS-2 idempotency logic, call orders against actual GDD source, no conflict with the 4 existing `Reconnecting`-adjacent methods, crash-simulation soundness). qa-tester: GAPS — found the mechanical correctness didn't fully match the AC's semantic claims: AC-CGS-2's `wasKilledWhileDisconnected` assertion was vacuous (set unconditionally in the test's own lambda, never reading production output); AC-CGS-3's test proves `RequireState`'s generic guard fires post-removal, not that the system arbitrates same-tick priority (no dispatcher/orchestration exists yet to arbitrate — an honest, non-contradictory complement to unity-specialist's "the code is mechanically correct" finding); a real design-consistency gap — `HandleGhostDeathWhileReconnecting` took `characterId` as an independent parameter instead of deriving it from the account's own registry record, unlike every sibling method, with no remarks justification for the deviation; EC-CGS-2's crash-recovery narrative wasn't actually exercised (confirmed no `CrashStep` enum value exists for the CGS-3 snapshot-WAL write specifically).
- All 4 suggestions fixed this session per user direction ("yes"): (1) fixed `HandleGhostDeathWhileReconnecting` to derive `characterId` from `record.CharacterId` (matching every sibling method), removing the mismatch risk structurally rather than just via a test; (2) removed the vacuous `wasKilledWhileDisconnectedWritten` assertion and documented honestly why that clause is unverifiable at this layer; added a reverse-order symmetry test (`CompleteReAuthSuccess_ThenHandleGhostDeathWhileReconnecting_SecondCallThrows`) plus honest doc-comment framing on both AC-CGS-3 tests clarifying what they can and cannot prove about "priority" absent a real dispatcher; (3) added `TryWriteSnapshot_TwoDifferentCharacters_NoCrossCharacterInterference`; (4) logged TD-019 (missing `CrashStep` for the CGS-3 snapshot-WAL write). Final test count: 18 (16 + 2 new).
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (`HandleGhostDeathWhileReconnecting` signature fix), `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs` (+2 tests, 1 vacuous assertion removed, call sites updated for the signature change), `docs/tech-debt-register.md` (+TD-019; register now 19 items)
- Next: `/story-done production/epics/networking-core/story-018-pre-disconnect-snapshot-write-ordering.md`.

## Session Extract — /story-done 2026-07-18 (Networking Core Story 017)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-017-ghost-promotion-state-constraints.md` — Ghost Promotion & State Constraints (first story in the Ghost Session cluster, 017-021)
- 7/7 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); TD-018 logged (AC-GH-2's freeze test tautological until a real Movement/Combat system exists); AC-GH-13's GDD pass-condition text imprecisely named `ITransportFaultInjector` (outbound-only) as the test technique — resolved via the new `ShouldRejectCommand` method, documented honestly.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 017 row → Complete.
- Tech debt logged this session: TD-018 (already reflected in the register before this closure — register now 18 items).
- `GhostEntityTracker` foundation now exists for Stories 018-021 to extend (pre-disconnect snapshot, ghost death/de-targeting, XP forfeit policy, cleanup/zone-crash/voluntary dismissal).
- Next recommended: Story 018 — Pre-Disconnect Snapshot & Write-Ordering — the natural next pick in the Ghost Session cluster (all of 018-021 are `Status: Ready`).

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 017 — Ghost Session cluster opens)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gaps: (1) 4 of 7 ACs (AC-GH-1/2/13/15) omitted the GDD's precise "Pass condition" text naming exact observer callbacks/test techniques — restored; (2) AC-CGS-5's pass condition wants to assert the exact `EntityHealthUpdate.HP` wire value, but no `INetworkTestObserver` callback exposes it (`OnRUBatchEntityHealthUpdates` only reports entity IDs) — resolved by deriving expected HP indirectly (same pattern AC-GH-3 already uses) rather than adding a new callback; (3) TR-net-006 registry gap (systemic); (4) added a performance note.
- Story: `production/epics/networking-core/story-017-ghost-promotion-state-constraints.md` — first story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted); ADR-010 cross-checked and correctly excluded (Accepted, but doesn't govern network-boundary messages).
- `engine-programmer` implemented `GhostEntityTracker` (new, `src/Foundation/Networking/GhostSession/`) — deliberately decoupled from `ConnectionStateMachine` (no dependency, same precedent as `ZoneSessionStateMachine`), a sealed in-memory registry keyed by `characterId`. Composition with `ConnectionStateMachine.EvaluateTimeouts`'s heartbeat-timeout branch happens only in the test (no orchestration layer exists yet), same "test composes them" precedent as Story 014.
- **Agent surfaced a design checkpoint (not a failure this time)** — stopped cleanly to ask approval before writing, listing 8 judgment calls. Reviewed and approved all 8, independently re-verifying the most important one myself: the agent found that `ITransportFaultInjector`'s actual API (confirmed by me reading the interface directly) is outbound-fault-injection only, with no inbound-command-queuing capability — contradicting the GDD's own AC-GH-13 pass-condition text, which names it as the test technique. Resolved by using `ShouldRejectCommand` as the real assertion mechanism, `TransportFaultInjector` instantiated only as scaffolding to honor the named technique, documented honestly in both files rather than silently reinterpreted.
- **`ApplyDamage`'s design is the key structural proof for AC-CGS-5/CGS-1**: the method body never reads `IsGhost` at all — the absence of that branch is itself the "no client-side prediction ever applied to a ghost" proof. A dedicated test constructs both a ghosted and a never-ghosted character and asserts identical `ApplyDamage` output.
- Other judgment calls (all approved): `PromoteToGhost` throws on double-promotion (defensive-guard consistency, not AC-required) → no `ClearGhostState` needed since tests use fresh-instance isolation; `GetAttackQueueDepth`/damage-amount are trivial forward-dependency mocks (no Combat/Skill system exists yet, same treatment as Story 010/015's generic seams); no `GHOST_COMBAT_TTL` timer field (correctly out of scope, deferred to Stories 019/021); `EncodeIsGhostField` is a minimal static encoding-contract proof (0 bytes when false, 1 byte when true), not a full wire message type.
- Test count: 16 (`tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs`) — all 7 blocking ACs covered (AC-GH-1/2/3/13/15, AC-CGS-5 split into 2 tests, AC-GH-19 split into 2 tests) plus 7 null-guard/precondition-guard/query-default tests.
- Files updated: `src/Foundation/Networking/GhostSession/GhostEntityTracker.cs` (new), `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs` (new, 16 tests)
- Independently verified both files in full (read completely, brace-balanced, cross-checked every `NetworkTestObserver` API reference) before reporting — this agent run completed cleanly, no truncation.
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — confirmed ADR-004 compliance, release-stripping guard correct, and independently traced `ApplyDamage`'s body to confirm no `IsGhost` read exists (the CGS-1 proof). qa-tester: GAPS — both specialists independently flagged the same dead-code issue (a `TransportFaultInjector` instance in the AC-GH-13 test that had no fault-injection method ever called on it, only a no-op `Reset()`); also found a missing negative-path test for `ShouldRejectCommand` (registered-but-not-ghosted case), and correctly pushed back on the AC-CGS-5 second test's framing — a single-input-pair equality test can't conclusively prove "no ghost-specific branch," only the direct code read can (which both specialists independently did and confirmed clean).
- All 4 suggestions fixed this session per user direction ("fix all 4 now"): (1) added `ShouldRejectCommand_RegisteredButNotGhosted_ReturnsFalse`; (2) removed the dead `TransportFaultInjector`/`Reset()` instantiation from the AC-GH-13 test; (3) strengthened the AC-CGS-5 second test with 4 input vectors (ordinary, zero-damage, damage-exceeds-HP/negative-result, both-zero) and reframed its doc comment as a regression guard, not "the structural proof" (the code-read of `ApplyDamage`'s body is the actual proof); (4) logged TD-018 (AC-GH-2's freeze test is honestly tautological given no Movement/Combat system exists yet — needs a companion test once one does). Final test count: 17 (16 + 1 new).
- Files updated: `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs` (+1 test, dead-code removed, 1 test strengthened+reframed), `docs/tech-debt-register.md` (+TD-018; register now 18 items)
- Next: `/story-done production/epics/networking-core/story-017-ghost-promotion-state-constraints.md`.

## Session Extract — /story-done 2026-07-18 (Networking Core Story 016)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-016-session-token-nscrt.md` — Session Token Generation, Validation & Rotation (NSCRT)
- 8/8 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` (given extra scrutiny) served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); TD-017 logged (AC-TOK-5 character-state-preservation clause has no owner anywhere in the repo — correctly out of scope for this token-only class); process note that this story was implemented directly rather than via subagent after 3 consecutive delegation failures.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 016 row → Complete.
- Tech debt logged this session: TD-017 (already reflected in the register before this closure — register now 17 items).
- **This closes the last independent piece before the Ghost Session cluster (017-021) can begin in earnest** — all of its dependencies (Session Lifecycle cluster 012-015, plus this story) are now Complete.
- Next recommended: any of Story 017-021 (Ghost Session cluster, all `Status: Ready`) — Story 017 (Ghost Promotion & State Constraints) is the natural first pick as the cluster's opening story.

## Session Extract — /story-readiness + /dev-story (IN PROGRESS) 2026-07-18 (Networking Core Story 016)

- Story: `production/epics/networking-core/story-016-session-token-nscrt.md` — Session Token Generation, Validation & Rotation (NSCRT). No ADR applies (pure .NET BCL crypto). Independent of the now-Complete Session Lifecycle cluster; several prior stories (013, 015) already anticipated it via delegate seams.
- `/story-readiness` verdict was NEEDS WORK → fixed directly in the story file (3 gaps, all applied): (1) AC-TOK-4 restored the GDD's dropped clarification — do NOT attempt a direct old-vs-new token byte comparison (`OnSessionHandshakeEmitted` has no token field yet, deferred per OQ-NC-SER-2), prove rotation only via the old token's invalidation; (2) AC-TOK-8's "1,000 random tokens + timing p<0.05" requirement genuinely conflicts with `.claude/rules/test-standards.md`'s "no random seeds, no time-dependent assertions" rule — resolved in the story text itself: use a fixed/deterministic 1,000-token array (not runtime-random), and treat the timing-comparison assertion as a narrow, approved, one-AC-only exception to the general rule; (3) added a performance note (no per-tick impact, concurrency-safety not throughput is the actual concern).
- **This is the first genuinely concurrency-sensitive class in this codebase** — CR-TOK-8 requires thread-safety since reconnect handlers and TTL expiry can execute concurrently at the 20Hz tick boundary. Every prior Networking Core class (`ConnectionStateMachine`, `ZoneSessionStateMachine`, etc.) is plain-`Dictionary`-backed and implicitly single-threaded; this is new ground.
- **Design decision resolved before implementation** (given to the engine-programmer as settled): a single atomic `TryValidateAndRotate(accountId, presentedToken, newExpiresAtTick, out newToken, out newSessionId)` method using `ConcurrentDictionary<uint, ActiveTokenEntry>.TryUpdate`'s 3-argument compare-and-swap overload (CAS against the exact entry object read, not a blind write) — a losing CAS (lost race) is treated as a failed validation, which is exactly AC-TOK-6's contract ("only the first of two concurrent requests succeeds"). Requires `ActiveTokenEntry` to be an **immutable** class (all-readonly fields, replaced wholesale, not mutated in place) specifically so CAS reference-identity semantics work — a deliberate, documented departure from this epic's usual mutable-record convention (`ConnectionStateMachine.AccountSessionRecord` mutates in place; this one must not).
- **Process note — TWO consecutive agent failures on this story, more severe than prior truncations**: first `engine-programmer` spawn (foreground) did 24 tool calls of research/planning and got cut off mid-sentence ("Now drafting the test file.") with **zero files written to disk** — confirmed via `git status`/`find` (unlike Stories 013/014/015's truncations, which at least had complete file writes before the summary got cut). Resumed the same agent (via `SendMessage`, not a fresh `Agent` spawn, to preserve its research) with an explicit instruction to write immediately. On resume, the agent had the full design drafted but **refused to call Write without explicit user approval** — correctly applying this project's Collaboration Protocol (`CLAUDE.md`: "Agents MUST ask 'May I write this to [filepath]?'"), specifically declining to treat a coordinator/orchestrator message as that approval. Its refusal was correct behavior, not a bug.
- Proposed design (relayed to and approved by the actual user): class named `SessionTokenStore` (not `...Service` — matches this codebase's noun-first naming convention), in `src/Foundation/Networking/SessionToken/`, implementing `IssueToken`, `TryValidateAndRotate` (CAS-based, per above), `InvalidateToken`, `IsZeroToken` (plain comparison, not `FixedTimeEquals` — not a secret, no timing-attack surface), `IsTokenActive`. Test file `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs`, ~20 tests, including a real `Parallel.Invoke`-based concurrent race test for AC-TOK-6 (100 iterations, exactly one winner each) and a `Stopwatch`-based timing comparison for AC-TOK-8 using a fixed deterministic 1,000-wrong-token array.
- **THREE consecutive engine-programmer failures on this exact story** before it landed: (1) first spawn did 24 tool calls of research and got cut off mid-sentence with zero files written; (2) resumed that same agent — it had the full design ready but correctly refused to treat any relayed/coordinator message as the user's own file-write approval (per `CLAUDE.md`'s Collaboration Protocol — this was correct behavior on its part, not a bug); (3) a fresh second `Agent` spawn (to avoid the first instance's transcript baggage) was terminated immediately by the session hitting its own API/usage limit, before any tool call ran.
- **Given three failed delegation attempts, implemented this story directly** (Write tool, no further agent spawn) using the fully-resolved design already produced across the prior attempts — user approved proceeding. Both files written and independently verified (read in full, brace-balance checked, cross-referenced every API call — `RandomNumberGenerator.GetBytes`, `CryptographicOperations.FixedTimeEquals`, `ConcurrentDictionary.TryUpdate`'s 3-arg CAS overload — against real .NET BCL signatures, all explicitly prescribed by the story/GDD text itself so no invented API risk).
- Files created: `src/Foundation/Networking/SessionToken/SessionTokenStore.cs` (new — `IssueToken`, `TryValidateAndRotate` [atomic CAS-based validate+rotate, the concurrency-critical method], `InvalidateToken`, `IsZeroToken`, `IsTokenActive`; `ActiveTokenEntry` nested class deliberately immutable, not mutated in place like every other registry in this epic, because `TryUpdate`'s CAS is reference-identity-based); `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs` (new, 15 tests) — all 8 blocking ACs covered, including a real `Parallel.Invoke`-based 100-iteration concurrency race test for AC-TOK-6 and a `Stopwatch`-based timing-comparison test for AC-TOK-8 using a fixed deterministic 1,000-wrong-token array (per the story's own resolved test-methodology note).
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story; this one in particular has never been compiled, so extra scrutiny in `/code-review` is warranted given no subagent verification happened either.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel, extra scrutiny requested given the self-implementation). unity-specialist: CLEAN — hand-traced the CAS correctness argument from first principles (reference-equality semantics of `ConcurrentDictionary.TryUpdate` for a class with no `Equals` override, per-key bucket lock serialization, argument order), ~97-98% confidence it's correct. qa-tester: GAPS — 3 missing edge-case tests, plus an important cross-story finding: AC-TOK-5's "character state is preserved" clause has no test anywhere in this repo and no other story owns it either (correctly out of scope for this token-only class).
- All 3 suggestions fixed this session per user direction ("fix all 3 now"): (1) added 3 tests — wrong-length token, `IssueToken` double-issue overwrite, cross-account isolation; (2) strengthened the AC-TOK-6 concurrency test with a 2-party `Barrier` forcing deterministic simultaneous execution instead of relying on scheduler-luck overlap; (3) logged TD-017 (AC-TOK-5 character-state-preservation ownership gap) in `docs/tech-debt-register.md` — register now 17 items. Final test count: 18 (15 + 3 new).
- Files updated: `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs` (+3 tests, Barrier strengthening), `docs/tech-debt-register.md` (+TD-017)
- Next: `/story-done production/epics/networking-core/story-016-session-token-nscrt.md`.
- Not yet committed to git (nothing in this epic has been committed since Story 006).

## Session Extract — /story-done 2026-07-18 (Networking Core Story 015 — Session Lifecycle cluster COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` — TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases
- 7/7 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); 2 forward-dependency scope limitations tracked as tech debt (TD-015 AC-NC-35(a), TD-016 AC-NC-42); AC-NC-16/34-CRASH's crash-durability proof accepted as a unit-level mock (mirrors Story 011's own precedent, not separately tracked).
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 015 row → Complete.
- Tech debt logged this session: TD-015, TD-016 (both during `/code-review`, already reflected in the register before this closure — register now 16 items total).
- **The Session Lifecycle cluster (012-015) is now fully Complete** — `ConnectionStateMachine` (012/013/015) and `ZoneSessionStateMachine` (014) together form the full ST-NET-1/ST-NET-2 session/zone lifecycle layer.
- Next recommended: Story 016 — Session Token Generation, Validation & Rotation (NSCRT) (`production/epics/networking-core/story-016-*.md`) — independent, no ADR applies, `Status: Ready`. Several prior stories (013's `HandleReconnectSessionSteal`, 015's crash-recovery work) already anticipated this via delegate seams (`invalidateSessionToken`). Unlocks the Ghost Session cluster (017-021), all `Status: Ready` and depending on the now-Complete Session Lifecycle cluster.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 015 — Session Lifecycle cluster closing story)

- `/story-readiness` verdict: NEEDS WORK → proceeded to `/dev-story` per user instruction. Gaps found: (1) Test Evidence/QA Test Cases cited `tests/PlayMode/...` — same recurring PlayMode/EditMode mismatch as Stories 007/013, corrected to EditMode; (2) AC-NC-12's story text dropped the GDD's required `"TTLExpired"` trigger-string literal; (3) TR-net-006 not in registry (systemic, expected); (4) no performance-budget note. Also independently confirmed two things that could have been mismatches but weren't: `ITransportFaultInjector.DropSnapshotFragment(ushort.MaxValue)` really does mean "last fragment" per the real interface, and `IServerCrashInjector.CrashStep.AfterTTLExpiryPersistenceWrite`/`AfterPersistenceWrite` already exist pre-registered for exactly this story's crash scenarios — no design ambiguity to resolve, unlike Stories 011/013. Also confirmed the story's own "AC-NC-34-CRASH" suffix correctly disambiguates a genuine cross-doc collision with `networking-wire-protocol.md`'s own unrelated AC-NC-34 (same collision class as the already-logged AC-NC-38 case).
- Story: `production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` — the closing story of the Session Lifecycle cluster (012-015), 7 blocking ACs spanning 4 distinct concerns.
- **Key design decision resolved before implementation** (to avoid breaking Stories 012/013's already-Complete, already-tested `ConnectionStateMachine` signatures): AC-NC-12 implemented as a NEW caller-driven method (`CompleteSessionActiveTTLExpiry`) on the same class, mirroring `EnterReconnecting`/`RecordFailedReAuthAttempt`'s shape (caller supplies `sessionExpiryTick` per-account, this class never computes/stores it) — rather than modifying `EvaluateTimeouts`'s existing signature/switch, which would have broken every existing Story 012/013 test call site.
- `engine-programmer` implemented 3 pieces: (1) `ConnectionStateMachine.CompleteSessionActiveTTLExpiry` (AC-NC-12) — extends the existing class; (2) `EnhancementRequestDeduplicator` (new, `src/Foundation/Networking/EnhancementRequestDedup/`) — generic EC-NET-9 dedup proof (per-character `LastEnhancementRequestID`-equivalent, no time window, crash-durability proven via constructing a fresh instance seeded from a mock store's post-crash-persisted value, mirroring `CommitBeforeBroadcastSequencer`'s Story 011 mock-outcome scope discipline) for AC-NC-16/27/34-CRASH; (3) `ZoneSnapshotReassemblyTracker` (new, `src/Foundation/Networking/ZoneSnapshotReassembly/`) — genuinely new fragment-reassembly timeout/retransmit-attempt/gate-open tracking for AC-NC-35/40, with `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS = 3` as a provisional public const (same precedent as Story 011's `SESSION_TTL_SECONDS` for an AC-pinned tunable value).
- **AC-NC-42 judgment call (documented, endorsed)**: added no new production code at all — `ConnectionStateMachine`'s existing `TryGetSessionState` already lets a caller guard an RPC's effect on current state, and EC-NET-6's "transition IS the authority boundary" is already true by construction (synchronous, single-dictionary, no concurrency). A wrapper method would only relocate the same one-line check with no real caller yet (no RPC-dispatch layer exists in this codebase). Proven via a 2-case parameterized test driving both legal orderings directly against the existing public API.
- **Process note**: the implementing agent's final response was truncated mid-sentence for the THIRD time this session (same failure mode as Stories 013 and 014's implementation runs). Did not trust the agent's self-report — independently read all 3 new/modified production files in full and cross-verified every internal API reference used in the test file (`ServerCrashInjector.SignalStepReached`, `TransportFaultInjector.TryConsumeSnapshotFragmentDrop`, `DisconnectType.Timeout`) against the real interface/enum definitions before reporting anything as complete. All correct, no partial writes found.
- Test count: 32 executed test cases (30 `[Test]` + 1 `[TestCase]`-parameterized method × 2 cases) across 4 fixtures in `tests/EditMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs`. All 7 blocking ACs covered.
- **Flagged for `/code-review`, not yet resolved**: the AC-NC-35(a) test only asserts `ITransportFaultInjector`'s own drop-tracking contract as a standalone sanity check (self-flagged by the agent in-comment) — it is not actually wired into `ZoneSnapshotReassemblyTracker`'s production code, since no real transport/snapshot-send path exists yet in this codebase for it to wire to. Worth a specialist opinion on whether this is acceptable scope (matching this epic's every-other-forward-dependency precedent) or needs tightening.
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `src/Foundation/Networking/EnhancementRequestDedup/EnhancementRequestDeduplicator.cs` (new), `src/Foundation/Networking/ZoneSnapshotReassembly/ZoneSnapshotReassemblyTracker.cs` (new), `tests/EditMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs` (new, 32 test cases)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — hand-verified the `ZoneSnapshotReassemblyTracker` re-baselining tick math line-by-line, confirmed the `EnhancementRequestDeduplicator` crash-simulation plumbing is a legitimate structural proof. qa-tester: GAPS — flagged that AC-NC-16/34-CRASH's crash simulation is a unit-level contract proof only (no real persistence/process boundary — acceptable interim, not permanent closure), AC-NC-42's test is documentation of a pattern rather than a regression gate (guard is a local function, no real RPC-dispatch layer exists to call into), AC-NC-35(a) is more disconnected from production code than self-flagged (fault injector and tracker are never actually wired together), plus 2 concrete missing tests and the still-stale story-file PlayMode path.
- All 4 suggestions fixed this session per user direction ("fix all 4 now"): (1) corrected story-015's stale `tests/PlayMode/...` references (both QA Test Cases and Test Evidence sections) to the actual EditMode path; (2) added `TryProcess_TwoIndependentCharacterInstances_SameRequestIdOnBothCommitsIndependently` proving `EnhancementRequestDeduplicator`'s per-character dedup scoping; (3) added `CompleteSessionActiveTTLExpiry_AccountReconnecting_Throws` closing the `Reconnecting`-state guard gap; (4) logged TD-015 (AC-NC-35(a) fault-injector/tracker disconnection) and TD-016 (AC-NC-42 synthetic-guard-only coverage) in `docs/tech-debt-register.md`, both explicitly flagging they must be revisited (not silently treated as closed) once the relevant forward-dependency stories land. Final test count: 34 (32 + 2 new).
- Files updated: `production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` (stale path fix), `tests/EditMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs` (+2 tests), `docs/tech-debt-register.md` (+TD-015, TD-016; register now 16 items)
- Next: `/story-done production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` — this is the LAST story in the Session Lifecycle cluster (012-015).

## Session Extract — /story-done 2026-07-18 (Networking Core Story 014)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-014-zone-session-state-machine-capacity.md` — Zone Session State Machine & Capacity Enforcement (ST-NET-2)
- 4/4 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Story file corrected at closure (matching the AC-NC-37 precedent from Story 013): AC-NC-22's GDD-source note fixed (actually `networking-core.md`, not `networking-session.md`), AC-NC-41 expanded to its fuller current-GDD text (the story's original copy omitted the `OnPersistenceWriteCompleted` and `IZoneTestConfigurator` clauses).
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 014 row → Complete.
- Tech debt logged: None new (deviations are documentation-accuracy corrections and the same systemic TR-net-006 registry gap tracked since Story 001).
- Next recommended: Story 015 — TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases (`production/epics/networking-core/story-015-*.md`) — depends on Stories 012/013/014, all now Complete.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 014)

- `/story-readiness` verdict: NEEDS WORK → proceeded to `/dev-story` per user instruction. Gaps found: (1) AC-NC-22 misattributed — story's Context section cites `networking-session.md` as the GDD, but AC-NC-22 (zone capacity overflow) is actually defined in `networking-core.md`'s "Zone Capacity" section; wording matches verbatim, just wrong source; (2) AC-NC-41 in the story is understated vs. the GDD's current text — missing the `OnPersistenceWriteCompleted(characterId, ExplicitDisconnect)` and `IZoneTestConfigurator.GetCurrentZoneState` same-tick-boundary clauses; (3) TR-net-006 not in registry (systemic, expected); (4) no performance-budget note despite touching the tick-loop-driven zone bookkeeping (same gap class as Story 012).
- Story: `production/epics/networking-core/story-014-zone-session-state-machine-capacity.md` — Zone Session State Machine & Capacity Enforcement (ST-NET-2) — first story past the now-Complete Story 012/013 pair in the Session Lifecycle cluster (012-015)
- **Real design ambiguity resolved before implementation** (surfaced by my own due-diligence read of `IZoneTestConfigurator`/`ZoneTestConfigurator`, not by the agent): the GDD's current AC-NC-41 requires `IZoneTestConfigurator.GetCurrentZoneState` to reflect the transition, but the concrete `ZoneTestConfigurator` (Story 001) is a pure in-memory override stub with zero wiring to any real zone logic — its own doc comment defers that wiring to "a future story." Resolved: the new `ZoneSessionStateMachine` production class never touches `IZoneTestConfigurator` at all (a production class depending on a test-only, `#if`-guarded interface type would be backwards); its own `TryGetZoneState` is the sole production source of truth, and the AC-NC-41 test independently drives both the real state machine and `ZoneTestConfigurator.SetZoneStateForTesting` to prove they agree on the same tick.
- `engine-programmer` implemented `ZoneSessionStateMachine` (sealed, stateful class — the first zone-level registry in this codebase, structurally parallel to but deliberately decoupled from `ConnectionStateMachine`'s per-account registry) in `src/Foundation/Networking/ZoneSessionStateMachine/`. Per this story's Out of Scope boundary: never queries `ConnectionStateMachine` directly — every method that needs a player/session count takes it as a caller-supplied parameter (same delegate-seam/loose-coupling precedent as every prior story this epic). No real TTL timer — `CompleteTTLExpiryTeardown` is caller-driven, mirroring `ConnectionStateMachine.EvaluateTimeouts`'s caller-supplied-tick-counts precedent (Story 015 owns the real sweep).
- **Judgment call — split teardown method, not one unified `TransitionToClosed`**: `PersistenceWriteReason` is declared entirely inside the `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` guard, so a single method taking a caller-supplied reason parameter would fail to compile in Release. Resolved with two separate methods (`CompleteExplicitDisconnectTeardown` hardcoding `ExplicitDisconnect`, `CompleteTTLExpiryTeardown` hardcoding the pre-existing `ZoneClose` reason — which Story 002 had already pre-registered specifically for this row) — mirrors `ConnectionStateMachine`'s existing convention of hardcoded literal reasons for every GDD-determinate transition.
- **Process note**: the implementing agent's final response was truncated mid-sentence (mid-run cutoff, same failure mode as the Story 002 session). Did not trust the agent's self-report — independently read both the full 601-line implementation file and the full 601-line test file before reporting anything as complete. Both were fully correct, complete, and consistent with the resolved design decisions above; no partial writes or corruption found.
- Test count: 31 (`tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs`) — all 4 blocking ACs covered (AC-NC-14, AC-NC-22, AC-NC-24, AC-NC-41 using the fuller GDD text), plus the full remaining ST-NET-2 transition table and null/precondition guards on every new method.
- **Minor asymmetry flagged for `/code-review`, not yet fixed**: `EvaluatePlayerCountChange` validates `totalOccupiedSlots == 0` (throwing, directing the caller to `CompleteExplicitDisconnectTeardown` instead) only on the `Active`-state branch — the equivalent `Draining`-state combination (`hasAnyConnectedSession: false`, `totalOccupiedSlots: 0`, already `Draining`) silently no-ops instead of throwing. No AC exercises this combination; non-blocking.
- Files updated: `src/Foundation/Networking/ZoneSessionStateMachine/ZoneSessionStateMachine.cs` (new), `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs` (new, 31 tests)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — release-stripping guard verified correct on all 5 observer-taking methods; confirmed `EvaluateJoinAttempt`'s no-observer-parameter design is correct (no matching callback exists in `INetworkTestObserver`'s surface); confirmed the split-teardown-method design is a compile-time necessity, not overengineering. qa-tester: GAPS — found the AC-NC-41 `IZoneTestConfigurator` cross-check test was **tautological**: it hardcoded `ZoneState.Closed` directly into `SetZoneStateForTesting` independent of the state machine's actual result, so a real regression producing `Draining` instead of `Closed` would not have been caught. Both specialists also independently flagged the same `EvaluatePlayerCountChange` asymmetry (zero-occupied-slots guard only threw on the `Active` branch, not `Draining`).
- All 5 suggestions fixed this session per user direction ("fix all 5 now"): (1) fixed the tautological test — now drives `SetZoneStateForTesting` from the real `TryGetZoneState` result, renamed to `CompleteExplicitDisconnectTeardown_RealStateMirroredIntoZoneTestConfiguratorSeam_ReadsBackAsClosed`, with a doc comment explicitly scoping what it does/doesn't prove (a regression guard on this test's own mirroring + `TryGetZoneState`, not a production-integration proof — the real orchestration layer composing `ZoneSessionStateMachine` with `IZoneTestConfigurator` doesn't exist yet); (2) added a 2-cycle `Active↔Draining` regression test; (3) added the 2 missing wrong-state guard tests for `CompleteExplicitDisconnectTeardown` (registered-but-`Draining`, registered-but-`Closed`); (4) added a `CompleteTTLExpiryTeardown` empty-list success-path test; (5) made the `EvaluatePlayerCountChange` zero-occupied-slots guard symmetric across `Active`/`Draining` (restructured the `else if (Active)` branch into a unified `else` block with the zero-check first, applicable to both states) + added the matching `...FromDraining_Throws` test. Final test count: 36 (31 + 5 new; the tautology fix was a rewrite, not a new test).
- Files updated: `src/Foundation/Networking/ZoneSessionStateMachine/ZoneSessionStateMachine.cs` (guard symmetry fix + doc comments), `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs` (+5 tests, 1 rewritten)
- Next: `/story-done production/epics/networking-core/story-014-zone-session-state-machine-capacity.md`

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 013)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-013-connection-state-machine-reconnect-session-stealing.md` — Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth Limits
- `/code-review` (lean, unity-specialist + qa-tester parallel): APPROVED WITH SUGGESTIONS. unity-specialist: CLEAN — release-stripping guard verified correct on all 4 new methods (EnterReconnecting, CompleteReAuthSuccess, RecordFailedReAuthAttempt, HandleReconnectSessionSteal); EC-NET-7 in-place-mutation and TTL-never-written-on-failure claims verified directly against code, not just doc comments. qa-tester: TESTABLE — all 5 blocking ACs (AC-NC-11, AC-NC-13, AC-NC-CR64-RECONCILE, AC-NC-37, AC-NC-38-REAUTH) proven with real production code paths (real `CurrencySystem`, not mocked). One flagged item, not a code defect: the story's own AC-NC-37 checkbox text was a byte-for-byte duplicate of Story 012's `AC-NC-39-SESSION` (Connected-state steal) rather than describing the Reconnecting-state row this story's `HandleReconnectSessionSteal` actually implements and tests.
- All 3 suggestions fixed this session per user direction ("fix all 3 now"): (1) reworded AC-NC-37's checkbox text (both Acceptance Criteria and QA Test Cases sections) to describe the `Reconnecting`-state scenario actually tested; (2) added `CompleteReAuthSuccess_TwoPendingGoldDebitedPurchases_ReconcilesBothInOrderAndSumsRefund` — hardens the reconciliation loop against an off-by-one/early-break regression that the existing single-record test couldn't catch; (3) extended the AC-NC-11 handshake test to assert all 7 `SessionHandshakeData` pass-through fields (previously only 4 of 7 were asserted — `goldVersion`/`currentHp`/`currentMp`/`heldFreePoints`/`classType` were silently unchecked). Final test count: 27 (26 + 1 new).
- `/story-done`: 5/5 blocking ACs COVERED with full traceability. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW phase-gates skipped, the already-run `/code-review` served as this story's review (consistent with every prior story this session).
- Deviations (both advisory, neither new): TR-net-006 not in the still-empty `tr-registry.yaml` (systemic gap, GDD used as source of truth directly); Story 016 (session token) dependency still `Status: Ready`, implemented against a delegate seam (`invalidateSessionToken`) per this epic's forward-dependency precedent.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 013 row → Complete.
- Tech debt logged: None new (both deviations are already-tracked systemic/precedent items, not new follow-up work).
- **Session Lifecycle cluster (012-015) status**: 012 and 013 now Complete. Story 014 (Zone Session State Machine & Capacity Enforcement, ADR-004) and Story 015 (TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases, ADR-004) both `Status: Ready`, unblocked.
- Next recommended: Story 014 — Zone Session State Machine & Capacity Enforcement (`production/epics/networking-core/story-014-*.md`) — next unstarted story in the Session Lifecycle cluster.

## Session Extract — /dev-story 2026-07-17 (Networking Core Story 011)

- Story: `production/epics/networking-core/story-011-commit-before-broadcast-pattern.md` — Commit-Before-Broadcast Generic Pattern — last story in the Tick Loop & Authority cluster (009-011)
- `engine-programmer` implemented `CommitBeforeBroadcastSequencer` (static `Execute<TOutcome>` ordering helper: validate → acknowledge → compute → persist → confirm → broadcast, never broadcast before confirm) in `src/Foundation/Networking/CommitBeforeBroadcast/`, against a generic mock "irreversible outcome" seam per the story's Out of Scope boundary — no real Enhancement/Respec/NPC Shop logic built.
- **Mid-implementation design checkpoint** (surfaced by the agent, verified independently before approving): the story's own text said to use "`IServerCrashInjector`'s delay/crash-at-step variants" — checked the actual interface, it only has `RegisterCrashAt(CrashStep)`/`ClearRegistered()`, no delay variant. Resolved by simulating delay/failure via the caller-supplied `persistOutcome` delegate instead (real `Stopwatch`-timed blocking for AC-NC-09/15's 200ms/500ms delays; `false` return for AC-CBB-1's failure case) — same class of story-text-vs-actual-API mismatch as Stories 005/009.
- **Real GDD inconsistency found and fixed**: CR-NET-5.5 (`networking-core.md`) defers to CR-CP-5 (`character-persistence.md`) as the authoritative write-failure protocol, but CR-CP-5's own comparison table (line 259) stated the alert-vs-session-preservation order backwards relative to CR-CP-5's own numbered protocol list (lines 174-182: revert→disconnect→**alert**→**preserve**). I independently verified this against the numbered list before approving the agent's resolution. Fixed the one line in the table; numbered list (authoritative) was already correct, untouched.
- `SESSION_TTL_SECONDS` (default 300, referenced across 5+ GDDs but never previously in code) introduced here for the first time, as a provisional `public const int` on `CommitBeforeBroadcastSequencer` — matches the `ServerTickLoop.TICK_RATE_HZ` precedent from Story 009. Real ownership belongs to a future session-lifecycle story (CR-NET-2).
- Added `OnCriticalInfrastructureAlertFired(uint clientId, string reason)` to `INetworkTestObserver`/`NetworkTestObserver` (interface + recorder + `Reset()` clear line, all verified consistent).
- **Agent-handling note (process, not content)**: my first attempt to resume the implementing agent after its design-checkpoint question used the `Agent` tool (spawning a fresh, context-less duplicate) instead of `SendMessage` (which resumes with full history). Caught before the duplicate wrote any files — stopped it via `TaskStop`, confirmed via `git status` that no conflicting changes landed, then correctly resumed the original agent via `SendMessage`. No file damage occurred; noting this so a future session doesn't repeat it.
- Test count: 13 (`tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs`) — 5 behavioral (happy path, reject path, 200ms delay, 500ms delay, write-failure protocol) + 8 null-argument-guard tests (one per required delegate parameter).
- All 4 blocking ACs (AC-NC-09, AC-NC-15, AC-CBB-1, AC-CBB-2) covered — see mapping in conversation; not yet independently re-verified by a second specialist pass (that's `/code-review`, not yet run this session).
- Files updated: `src/Foundation/Networking/CommitBeforeBroadcast/{CommitBeforeBroadcastSequencer,CommitBeforeBroadcastResult}.cs` (new), `tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs` (new, 13 tests), `src/Foundation/Networking/TestHarness/{INetworkTestObserver,NetworkTestObserver}.cs` (new observer callback), `design/gdd/character-persistence.md` (1-line table-order fix)
- Not yet run in a real Unity Editor — no compiler available in this sandboxed session, same limitation as every prior story.
- Not yet committed to git — nothing in this epic has been committed since Story 006 (`3ab0fe6`); Stories 007-011 are all still uncommitted, per project convention (no commits without explicit user instruction).
- **Reminder carried forward**: the `origin` git remote still has a live-looking GitHub PAT embedded in plaintext in `.git/config` (first flagged during Story 010's session) — not yet rotated.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). Zero BLOCKING findings — the release-stripping guard around the new `observer` parameter was verified clean on first pass (correctly applying the Story 010 lesson). 4 suggestions surfaced, all fixed in this same session per user direction ("fix all 4 now"): (1) **exception-safety hardening** — qa-tester found that if `persistOutcome` throws instead of returning `false`, the entire CR-NET-5.5 write-failure protocol would silently never run; added a try/catch inside `Execute<TOutcome>` routing thrown exceptions into the identical failure-protocol path as a `false` return, plus a new regression test (`Execute_PersistOutcomeThrows_...`); (2) added a literal `Assert.AreEqual(300, ...)` check so a regression to `SESSION_TTL_SECONDS`'s tuned default wouldn't slip past a self-consistency-only check; (3) added a doc-comment sentence explaining the delegate-seam-vs-descriptor-struct shape difference from Story 010's `CrossCuttingRpcGuardChain`; (4) `.meta` file gap left alone (expected, no Editor in this sandbox). All 4 fixes independently re-verified by direct file read (not just trusting the agent's report) before closing. Final test count: 14 (13 + 1 new exception-path test).
- **`/story-done` result: COMPLETE WITH NOTES.** 4/4 blocking ACs COVERED with traceability. Review mode is `lean` (`production/review-mode.txt`) so the QL-TEST-COVERAGE and LP-CODE-REVIEW phase-gates were skipped — the `/code-review` pass already run served as this story's review, consistent with every prior story this session.
- **New finding during closure**: a genuine TR-ID collision — Story 009 and Story 011 both cite `Requirement: TR-net-002`, but `EPIC.md`'s own placeholder table defines that ID as Story 009's "20Hz tick" requirement, not Story 011's commit-before-broadcast pattern. Didn't block either story (both sourced requirements directly from the GDD, not the empty `tr-registry.yaml`). Logged as TD-014.
- Story 011 marked `Status: Complete` with Completion Notes; `EPIC.md` Story 011 row → Complete, plus the new TR-net-002 collision added to the epic's "Known blockers/inconsistencies" list.
- Tech debt logged: TD-013 (`SESSION_TTL_SECONDS` provisional ownership, resolve when Story 012/013 builds the real session registry), TD-014 (TR-net-002 collision, resolve when the TR registry is actually populated). Register updated: 12 → 14 items.
- **The Tick Loop & Authority cluster (009-011) is now fully Complete.**

## Session Extract — /story-done 2026-07-17 (Networking Core Story 011)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-011-commit-before-broadcast-pattern.md` — Commit-Before-Broadcast Generic Pattern
- Tech debt logged: 2 items (TD-013, TD-014)
- Next recommended: Story 012 — Player Connection State Machine — Core Transitions (`production/epics/networking-core/story-012-player-connection-state-machine-core-transitions.md`) — first story past the now-complete Tick Loop & Authority cluster; per `EPIC.md`, depends on Stories 004/006/007/009/010/011, all Complete.

## Session Extract — /story-readiness + /dev-story + /code-review 2026-07-17 (Networking Core Story 012)

- `/story-readiness` verdict: NEEDS WORK → both gaps minor and consistent with epic-wide precedent, user chose to proceed straight to `/dev-story` without fixing first. Gaps: (1) TR-net-006 not in the (empty) `tr-registry.yaml` — same systemic gap as every prior story; (2) no performance-budget note despite touching the tick path.
- Story: `production/epics/networking-core/story-012-connection-state-machine-core-transitions.md` — Player Connection State Machine — Core Transitions (the 6-row core, non-reconnect subset of ST-NET-1)
- `engine-programmer` implemented `ConnectionStateMachine` (sealed, stateful class — the first real per-account `SessionState` registry in this codebase) in `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs`, against injected triggers per ADR-004's own guidance (NGO connection-callback binding deferred to a future story).
- **Two design judgment calls surfaced and approved before coding**: (1) `OnSessionStateTransitioned`'s non-nullable `fromState` has no representation for the `— → Connecting` (brand-new-connection) transition — resolved by reusing `SessionState.Disconnected_SessionExpired` as the synthetic fromState (semantically accurate for the session-steal case; adding a 6th enum member would violate the control manifest's explicit "ST-NET-1 five states" rule); (2) no GDD/story-prescribed trigger string exists for explicit-disconnect — resolved as `"ExplicitDisconnect"`, mirroring the existing `PersistenceWriteReason.ExplicitDisconnect` enum member name.
- **Explicitly distinguished from Story 008's `HeartbeatActivityTracker`**: that class tracks *outbound* packets for heartbeat-send scheduling; this story needed its own *inbound*-silence tracking for AC-NC-10 (a different concern) — flagged in the brief specifically to prevent the two from being conflated, and the implementation correctly built its own tracking rather than misusing the existing class.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). Zero BLOCKING findings — release-stripping guard verified clean across all 6 public methods + 1 private helper; dictionary-mutation-during-enumeration safety confirmed correct by hand-trace against actual `Dictionary<TKey,TValue>` semantics (deferred-removal-via-scratch-list correctly applies the lesson from Story 009's own code review). qa-tester found one real **test-quality gap** (not a production defect): the AC-NC-39-SESSION ordering test caught 3 of 4 possible adjacent-step reorderings but missed a swap between `OnPersistenceWriteCompleted` and `OnSessionStateTransitioned(SessionSteal)` — fixed with one added assertion. Also added, per user direction ("fix all 4 now"): 4 missing null-guard tests (both specialists independently found the same gap) and 2 parity/robustness tests (`RecordInboundActivity` no-op on a `Connecting` account — flagged as moderate-value since a regression here would silently defeat AC-NC-39-CONNECTING; `FailConnecting_AccountNotConnecting_Throws` for consistency with the other three `RequireState`-guarded methods). Final test count: 25 (19 original + 6 new — the ordering fix was an added assertion, not a new test method).
- All 4 blocking ACs (AC-NC-10, AC-NC-26, AC-NC-39-SESSION, AC-NC-39-CONNECTING) covered — both AC-NC-10 boundary directions (59/60/61 ticks) explicitly tested.
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (new), `tests/EditMode/Networking/Session_ConnectionStateMachine_Core_tests.cs` (new, 25 tests)
- Not yet run in a real Unity Editor — no compiler available in this sandboxed session, same limitation as every prior story. Verified independently by direct file re-reads (not just agent self-report) after both the implementation and the review-fix passes.
- Not yet committed to git.
- Next: `/story-done production/epics/networking-core/story-012-connection-state-machine-core-transitions.md`

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 013)

- `/story-readiness` verdict: NEEDS WORK → proceeded straight to `/dev-story` per user instruction. Gaps found: (1) header `Type: Logic` contradicted Test Evidence's `Story Type: Integration` + a nonexistent `tests/PlayMode/...` path that also contradicted QA Test Cases' own EditMode path — same 3-way inconsistency pattern Story 007 hit; (2) TR-net-006 not in the (still-empty) registry — same systemic gap; (3) dependency Story 016 (session token) is `Status: Ready`, not Complete — user chose "proceed anyway," implemented against a delegate seam per this epic's forward-dependency precedent.
- Story: `production/epics/networking-core/story-013-connection-state-machine-reconnect-session-stealing.md` — extends Story 012's `ConnectionStateMachine` with the 4 `Reconnecting`-state ST-NET-1 rows (not a new/parallel class).
- **Real discrepancy found and resolved before coding**: the story's own AC-NC-37 text is word-for-word the same `Connected`-state scenario Story 012 already tests as `AC-NC-39-SESSION` — but this story's scope is the `Reconnecting`-state additions, and its Out of Scope section explicitly excludes Story 012's territory. Resolved: implemented the GDD's distinct (and un-numbered in the GDD's own AC list) `Reconnecting → Disconnected_SessionExpired` session-steal row instead (new method `HandleReconnectSessionSteal`), documented in the class's `<remarks>`, did not duplicate Story 012's existing test.
- **Mid-implementation session-limit interruption**: the implementing agent hit its API session limit mid-task, right after finishing `TryGetSessionExpiryTick` (its second-to-last planned step). Rather than resuming blind, I independently verified every file it had touched before treating anything as complete: read `ConnectionStateMachine.cs` in full (all 5 new methods present, brace-balanced, correctly incorporating a requested clarification — see below), all 3 new supporting type files, and the full 26-test test file (spot-checked the two trickiest tests — the `HandleReconnectSessionSteal` ordering proof and the 3-attempt exhaustion test — both correct). Cross-verified real dependencies actually exist as assumed: `CharacterID(uint)` constructor, `CurrencySystem.RegisterCharacter`, `ICurrencyService.GetBalance`. Found exactly one piece genuinely left undone — the story-file `Type`/Test-Evidence documentation fix — and applied it directly myself rather than waiting on the rate-limited agent.
- **One design clarification requested and correctly incorporated before the interruption**: `RecordFailedReAuthAttempt` requires a fresh `EnterReconnecting` call before each failed attempt (matching the GDD's literal bounce-back transition table), which only works if `EnterReconnecting` mutates the *existing* `AccountSessionRecord` in place rather than replacing it — otherwise `ReauthFailureCount` would silently reset each cycle, defeating EC-NET-7. Verified in the actual code: `EnterReconnecting` does `record.State = ...; record.SessionExpiryTick = ...` on the existing object (not `_sessions[accountId] = new AccountSessionRecord{...}`), with an explicit doc-comment note added exactly as requested.
- Test count: 26 (`tests/EditMode/Networking/Session_ConnectionStateMachine_Reconnect_tests.cs`) — all 5 ACs (AC-NC-11, AC-NC-13 a/b/none, AC-NC-CR64-RECONCILE, AC-NC-37, AC-NC-38-REAUTH) covered, plus null-guard and precondition-guard tests for each new method. Gold-reconciliation tests use a real `CurrencySystem` instance (Currency System is production code, not a forward dependency).
- NP-NEW-2 (TTL elapses mid-reauth) explicitly deferred to Story 015 — approved before implementation; no AC in this story tests it, and it overlaps Story 015's CR-NET-6.5 scope.
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `src/Foundation/Networking/ConnectionStateMachine/{PendingPurchaseRecord,RespecReservationStatus,SessionHandshakeData}.cs` (new, +`.meta`), `tests/EditMode/Networking/Session_ConnectionStateMachine_Reconnect_tests.cs` (new, 26 tests), story file (`Type: Logic`→`Integration`, Test Evidence path corrected)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- Next: `/code-review` on the files above.

## Session Extract — /story-done 2026-07-17 (Networking Core Story 012)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-012-connection-state-machine-core-transitions.md` — Player Connection State Machine — Core Transitions
- 4/4 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 012 row → Complete.
- Tech debt logged: None new (all 3 advisory deviations are either already-tracked systemic gaps — TR-net-006/TD-014 — or self-resolving design notes documented inline in the code, not new follow-up work).
- Next recommended: Story 013 — Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth (`production/epics/networking-core/story-013-connection-state-machine-reconnect-session-stealing.md`) — builds directly on Story 012's `ConnectionStateMachine`, depends on Story 012 (now Complete).

## Session Extract — /dev-story + /code-review + /story-done 2026-07-12 (Networking Core Story 010)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-010-cross-cutting-rpc-guards.md` — Cross-Cutting RPC Guards (EntityID Validity, Session-Ready, Rate Limiting) — next in the Tick Loop & Authority cluster (009-011)
- `/dev-story`: `engine-programmer` implemented `CrossCuttingRpcGuardChain` (4-stage guard pipeline: EntityID validity → session-ready → rate limit → ownership) in `src/Foundation/Networking/RpcGuards/`, against a generic `InboundRpcDescriptor`/`RpcTypeTag` carrier since `AllocateFreePointRequest`/`NotifySkillUsed` don't exist as concrete types yet (Leveling/Skill epics not started) — same forward-dependency pattern as Story 007/009. Two design judgment calls surfaced and approved before writing code: (1) `InboundRpcDescriptor` needed a `ClientId` field beyond the story's literal 3-field text, for the ownership/session-ready checks to have any meaning; (2) `RpcTypeTag` split into its own file (not nested) to match this codebase's one-enum-per-file convention.
- **Major finding during `/code-review`**: both unity-specialist and my own manual verification (by actually running `tools/ci/check-test-harness-guards.sh`, the real CI script built in Story 002 to enforce AC-TC-02) confirmed `CrossCuttingRpcGuardChain.Evaluate` referenced `INetworkTestObserver` in an unguarded production method signature — a genuine Release-Player-build compile-breaking defect, not just a style issue. **The identical defect was already present, undetected, in two already-"Complete" files**: `ServerTickLoop.cs` (Story 009) and `RUBatchWriter.cs` (Story 007) — both completely unguarded top-to-bottom. Per user direction, fixed all three in one pass (`#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` around the observer parameter + its usage in each), plus reworded 4 doc-comment mentions (including one in `PendingSubMessage.cs`) that tripped the CI script's text-only scanner without being real compile risks. Re-ran the CI script: now passes clean project-wide. Re-verified by a second unity-specialist pass (hand-traced the fix and the 2 new tests against the actual code) — confirmed complete and correct.
- qa-tester found 2 real coverage gaps (session-ready-checked-before-rate-limit specifically was never proven, only asserted in prose; `OnSkillUsedRateLimitRejected` never proven to NOT fire for `AllocateFreePoint` rejections) → both closed with new tests. Final test count: 23 (21 original + 2 gap-closing).
- All 4 blocking ACs (AC-NC-02, AC-NC-20, AC-NC-46, AC-NC-23) COVERED — full traceability table produced, no gaps. Rate-limit/ownership gate-ordering interaction (can a non-owner's rejected request corrupt the real owner's rate-limit bookkeeping?) specifically hand-traced by both specialists and confirmed correct — `_lastAcceptedTick` only ever writes after all 4 guards pass.
- Tech debt: None new logged (the release-stripping bug was found AND fixed in this same session, not left open — matches this project's established pattern of not tech-debt-logging bugs that are already resolved).
- **⚠️ Separately surfaced, unrelated to this story**: `git remote -v` shows the `origin` remote URL contains a live-looking GitHub Personal Access Token embedded in plaintext (`https://github_pat_...@github.com/...`). This is a credential exposure sitting in `.git/config` — flagged to the user directly in-conversation, recommend rotating/revoking the token and reconfiguring the remote to use a credential helper instead. Not acted upon (out of scope for this story), no file was written or read to extract/expose it further.
- Files updated: `src/Foundation/Networking/RpcGuards/{RpcGuardResult,InboundRpcDescriptor,RpcTypeTag,CrossCuttingRpcGuardChain}.cs` (new, +release-stripping fix), `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_tests.cs` (new, 23 tests), `src/Foundation/Networking/TickLoop/ServerTickLoop.cs` (release-stripping fix), `src/Foundation/Networking/WireProtocol/RUBatchWriter.cs` (release-stripping fix), `src/Foundation/Networking/WireProtocol/PendingSubMessage.cs` (doc-comment reword), `production/epics/networking-core/story-010-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 010 → Complete)
- Not yet run in a real Unity Editor — no compiler available in this sandboxed session, same limitation as every prior story.
- Not yet committed to git (per project convention, committing requires explicit user instruction).
- Next recommended: Story 011 — Commit-Before-Broadcast Pattern (`production/epics/networking-core/story-011-commit-before-broadcast-pattern.md`) — last story in the Tick Loop & Authority cluster (009-011)

## Session Extract — /code-review + /story-done 2026-07-12 (Networking Core Story 009)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-009-fixed-20hz-server-tick-loop.md` — Fixed 20Hz Server Tick Loop — first story in the Tick Loop & Authority cluster (009-011)
- **Continuation context**: implementation (`src/Foundation/Networking/TickLoop/ServerTickLoop.cs`) and its test suite (`tests/EditMode/Networking/TickLoop_Core_tests.cs`) were already fully written and sitting uncommitted at the start of this session (a prior `/dev-story` run, evidenced by a stray `bash.exe.stackdump` — the session likely crashed before code review/story-done ran). Picked up directly at `/code-review`.
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): first pass found a real medium-severity bug — `AdvanceTick`'s tick-driven delegate dispatch loop indexed the live mutable `_tickDrivenDelegates` list, so a delegate unregistering an earlier sibling mid-tick would shift the list and silently skip a not-yet-invoked delegate for that tick (the same hazard the TTL registry's backward iteration was already written to avoid). Fixed via a snapshotted dispatch count + deferred-removal reentrancy guard (`_isDispatchingTickDriven` + `_pendingTickDrivenUnregistrations`, wrapped in try/finally so it's exception-safe too). Re-verified CLEAN by a second unity-specialist pass that hand-traced the fix and all 9 new tests against the actual code.
- 9 tests added closing gaps found across both review passes: Bug A regression, exception-propagation-doesn't-corrupt-state, `ArgumentNullException` guards on both Register methods, `UnregisterTickDriven` false-return path, TTL timer registering a new TTL timer during the expiry loop (reentrancy), drift-alert boundary exclusivity (just-below/just-above 25ms, avoiding an exact-25ms float-precision-risk test), and a two-window drift-accumulator-reset test. Final count: 25 test methods (16 original + 9 new).
- All 4 blocking ACs (AC-NC-04, AC-NC-05, AC-TICK-1, AC-TICK-2) COVERED — full traceability table produced, no gaps.
- Two theoretical edge cases flagged by unity-specialist but judged non-blocking (no code in this codebase exercises them): nested/reentrant `AdvanceTick` calls on the same instance, and calling `UnregisterTickDriven` twice mid-dispatch on a duplicate-instance registration. Added one doc-comment line on `AdvanceTick` noting the non-reentrancy expectation; no test/tech-debt entry needed.
- Deviations logged in story (non-blocking): (1) story's own Dependencies text ("tick loop calls their Flush methods" for Stories 006/007) was only half-accurate — this story builds only the generic `RegisterTickDriven` extension point, doesn't itself call `PriorityPathQueue.Flush`/`RUBatchWriter.Write`; corrected in the story file, matches the judgment-call writeup already in `ServerTickLoop.cs`'s class remarks. (2) `HeartbeatActivityTracker.cs` (Story 008's file) doc comment corrected — its claim that `TICK_RATE_HZ` "does not exist anywhere in this codebase yet" is now stale since this story introduces it (doc-only, no behavior change).
- Tech debt: None new (TR-net-002 registry gap is the same pre-existing systemic issue documented across every prior story)
- **Not yet run in a real Unity Editor** — no compiler available in this sandboxed session (same limitation as Stories 003/004/005/006/008). Recommend running the EditMode suite before treating this as fully closed.
- Files updated: `src/Foundation/Networking/TickLoop/ServerTickLoop.cs` (bug fix + doc corrections), `tests/EditMode/Networking/TickLoop_Core_tests.cs` (+9 tests), `production/epics/networking-core/story-009-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 009 → Complete)
- **Still uncommitted in git, along with Stories 007/008 before it** — nothing in this epic has been committed since Story 006 (commit `3ab0fe6`). Per project convention ("No commits without user instruction"), committing is a separate explicit step the user hasn't requested yet.
- Next recommended: Story 010 — Cross-Cutting RPC Guards (`production/epics/networking-core/story-010-cross-cutting-rpc-guards.md`) — next in the Tick Loop & Authority cluster (009-011)

## Session Extract — /story-done 2026-07-11 (Networking Core Story 008 — Wire Protocol Core cluster COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-008-heartbeat-il2cpp-aot-guardrails.md` — Heartbeat Message & IL2CPP AOT Guardrails
- 3/3 ACs passing, verified by direct file review + 2 independent specialist passes (unity-specialist independently re-ran the AOT scanner's regexes against the real source tree by hand; qa-tester confirmed the AC-NC-38 test genuinely proves the full narrative, not a shortcut). **Not yet confirmed in a real Unity Editor run** — unlike Story 007, this one is still file-review-only; recommend a real Editor pass before treating it as fully closed.
- **Wire Protocol Core cluster (Stories 003-008) is now fully Complete** — the last foundational layer before Tick Loop & Authority (009-011).
- Tech debt: TD-011 extended (now covers 5 provisional MessageTypeIDs across Stories 007-008, including `HeartbeatMessage` = 0x0210)
- Also surfaced: a pre-existing cross-doc AC-ID collision (AC-NC-38 means unrelated things in `networking-wire-protocol.md` vs. `networking-session.md`) — logged in `EPIC.md`'s known-inconsistencies list, not a defect in this story
- Files updated: story-008 file (Status: Complete, ACs checked, Completion Notes), `EPIC.md` (Story 008 → Complete, +AC-NC-38 collision note), `docs/tech-debt-register.md` (TD-011 extended)
- Next recommended: Story 009 — Fixed 20Hz Server Tick Loop (`production/epics/networking-core/story-009-fixed-20hz-server-tick-loop.md`) — first story in the Tick Loop & Authority cluster (009-011); depends on Stories 001/002/006/007, all Complete

## Session Extract — /story-done 2026-07-11 (Networking Core Story 007)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md` — R-U/U-U Batch Framing, Buffer Pooling & Overflow Drop Policy — closes out the Wire Protocol Core cluster (003-008 minus 008)
- 4/4 ACs passing, confirmed in a **real Unity Editor Test Runner run** — first such confirmation in this epic (Stories 001-006 were closed on file-review-only verification, never actually compiled until this session)
- Tech debt logged: TD-011 (4 provisional MessageTypeIDs — resolve before Story 025-027 dispatch), TD-012 (GC allocations in `RUBatchWriter.Write` hot path — SUGGESTION not BLOCKING per unity-specialist)
- Files updated: story-007 file (Status: Complete, ACs checked, Completion Notes), `EPIC.md` (Story 007 → Complete), `docs/tech-debt-register.md` (+TD-011, TD-012)
- Next recommended: Story 008 — Heartbeat Message & IL2CPP AOT Guardrails (`production/epics/networking-core/story-008-heartbeat-message-il2cpp-aot-guardrails.md`) — last story in the Wire Protocol Core cluster

## Session Extract — Currency System test fixes from first real Unity Test Runner pass, 2026-07-11

- **Context**: user ran the full EditMode suite in a real Unity Editor for the first time this session (prompted by the Story 007 code review's recommendation). Result: 0 failures in Networking Core (Stories 001-007, first real compile — clean); a handful of failures in the already-"Complete" Currency System epic.
- **Root cause diagnosed from one pasted failure** (`AddGold_UnregisteredCharacter_DoesNotFireOnGoldSync`, "Unhandled log message" on `CurrencySystem`'s deliberate `Debug.LogError` for the `CharacterNotFound` guard): a systemic, mechanical test-authoring gap, NOT a production-code bug. `CurrencySystem.AddGold`/`TrySpendGold` correctly log `Debug.LogError` on their two guard-rejection paths (`CharacterNotFound`, `InvalidAmount` — by design, per the class's own doc comment: "caller bug, not a runtime condition"). Several tests exercise those exact guard paths without declaring `LogAssert.Expect` first, so Unity Test Framework auto-fails them on the unhandled log — same class of issue as the 2026-07-04 "first real compile" incident (passed code review, never actually run in Unity until now).
- **Grepped both files exercising `CharacterNotFound`/`InvalidAmount`, found 10 affected tests total, fixed all 10** (added `using System.Text.RegularExpressions;`/`using UnityEngine.TestTools;` + a `LogAssert.Expect(LogType.Error, new Regex(...))` immediately before the guard-triggering call, matching this project's own established convention already used correctly elsewhere — e.g. Story 007's `BufferPoolExhausted` test):
  - `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs`: `AddGold_ZeroAmount_...`, `AddGold_UnregisteredCharacter_...`, `TrySpendGold_ZeroCost_...`, `TrySpendGold_UnregisteredCharacter_...` (4 tests)
  - `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs`: `AddGold_ZeroAmountOnRegisteredCharacter_...`, `TrySpendGold_ZeroCostOnRegisteredCharacter_...`, `AddGold_UnregisteredCharacter_...`, `TrySpendGold_UnregisteredCharacter_...`, `AddGold_UnregisteredCharacterWithZeroAmount_...`, `TrySpendGold_UnregisteredCharacterWithZeroCost_...` (6 tests)
  - Confirmed NOT affected (checked, no matching guard/log path): `Currency_AddGold_tests.cs`, `Currency_TrySpendGold_tests.cs`, `Currency_Concurrency_tests.cs`, `Currency_EdgeCases_tests.cs`, and `Currency_GoldSyncEvent_tests.cs`'s own `TrySpendGold_InsufficientBalance_...` test (InsufficientFunds guard does not log an error).
- No production code changed — this was purely a missing test-declaration gap. The underlying guard logic was already correct and already verified during Story 003's code review.
- **Follow-up compile error**: both fixed files used `LogType.Error` but only imported `using UnityEngine.TestTools;` (not `using UnityEngine;`, where `LogType` actually lives) — real `CS0246` compile error, caught from the generic Unity Test Runner message alone (no console text needed) since it was an obvious self-inflicted gap in the just-made edits. Fixed by adding `using UnityEngine;` to both files.
- **Verified: user re-ran the full EditMode suite in real Unity — all tests pass clean.** This is the first real compile+test confirmation for the entire Wire Protocol Core cluster (Stories 003-008) and closes out the Currency System regression from the missing `LogAssert.Expect` declarations.
- Files updated: `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs`, `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs`

## Session Extract — /story-readiness + /dev-story 2026-07-11 (Networking Core Story 007)

- `/story-readiness` verdict: NEEDS WORK → fixed → proceeded. Two gaps found and fixed directly in the story file: (1) header `Type: Logic` contradicted the Test Evidence section's `Story Type: Integration` — corrected header to `Type: Integration` (AC-NC-21/AC-NC-33 both require multi-tick, multi-client load fixtures, which is Integration in nature); (2) QA Test Cases section named an EditMode test path while Test Evidence required PlayMode — reconciled to EditMode, consistent with every prior Networking Core story's deterministic in-process fixture pattern (no real PlayMode multiplayer session exists in this project).
- Story: `production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md` — R-U/U-U Batch Framing, Buffer Pooling & Overflow Drop Policy — last story in the Wire Protocol Core cluster (003-008)
- Files changed (all new): `src/Foundation/Networking/WireProtocol/{RUBatchCategory,PendingSubMessage,BatchSubMessageFraming,BatchHeaderCodec,DamageEvent,GoldSyncEvent,CycleTimerBroadcast,EntityPositionUpdate,BatchSubMessageCodec,RUBatchWriter,CycleBroadcastPacketWriter,PositionPacketWriter,ClientBufferSet,ZoneBufferPool}.cs`; `src/Foundation/Networking/WireProtocol/WireEnumCodec.cs` extended with `DecodeGoldTransactionReason` (substitute-and-continue, mirrors the existing `DecodeDamageType`/`DecodeDisconnectType` pattern — a deviation from the brief, which said a plain cast was fine; the implementing agent judged the existing codec pattern was a better fit and I agree)
- Test written: `tests/EditMode/Networking/WireProtocol_BatchFraming_tests.cs` (21 test methods, 23 executed cases counting a 3-case `[TestCase]`) — all 4 blocking ACs covered (AC-NC-19 absolute-balance + observer hook, AC-NC-21 50-client/200-tick byte budget, AC-NC-33 100-tick 512-byte packet cap, AC-BUF-1 buffer pool exhaustion), plus proactive edge-case coverage (DamageEvent intra-class overflow, category-level atomic eviction, CycleBroadcast never-drop guard, Position packet highest-ID-first drop, all 4 codec round-trips + short-buffer guards)
- Key scoping discipline (verified, endorsed): only `DamageEvent` and `GoldSyncEvent` got concrete typed wire schemas (both owned by `networking-wire-protocol.md` itself); the other 7 R-U categories (EntityHealthUpdate, PartyMemberHealthUpdate, SelfPositionUpdate, SkillCastResult, SkillCooldownUpdate, LootBidUpdate, ConnectionQualityUpdate) are carried generically via `PendingSubMessage` (opaque pre-serialized payload + category tag) because their owning systems (Skill System, Party, Loot Table, Client-Side Prediction, Relevance Filter) don't exist in this codebase yet — mirrors Story 006's `PriorityPathQueue<T>` genericization precedent exactly.
- Judgment calls documented in code (both flagged prominently, both consistent with this epic's established pattern of resolving ambiguous GDD language explicitly rather than silently guessing): (1) the 8-step overflow drop order evicts whole categories atomically, not partial/interleaved per-message drops; (2) all 4 concrete sub-message `MessageTypeId` values (DamageEvent 0x0301, GoldSyncEvent 0x0520, CycleTimerBroadcast 0x0302, EntityPositionUpdate 0x0303) are provisional — not formally registered in ADR-004 (which only registers the 0x0100-0x01FF batch-header range); DamageEvent/GoldSyncEvent reused values already present unchanged in Story 004's doc-comment examples, Cycle/Position picked fresh adjacent values with no prior precedent. Flagged for future ADR-004 amendment or a dedicated MessageTypeID registry — a collision or renumbering after Story 025-027 (dispatch) exists would be a breaking wire change. (3) `GoldTransactionReason` out-of-range byte substitutes `Other` (not `AdminAdjust` as I'd originally briefed) — the implementing agent found `GoldTransactionReason.cs`'s own pre-existing doc comment already specifies "unknown byte values → `Other`, still apply the balance update" as the authoritative receiver contract, and correctly treated that over my guess.
- Tech debt / follow-ups: MessageTypeID registry gap (above) should be resolved before any other story starts assigning application-range IDs ad hoc; buffer pool has no real connection-lifecycle wiring yet (pure data structure, by design — a future story wires `ZoneBufferPool.TryAllocate`/`Release` to real NGO connect/disconnect events).
- Not yet run in the Unity Test Runner (no Editor invocation available in this sandboxed session) — implementation and tests were verified by direct file review (all ~22 files read in full), not by compiling. Recommend running the EditMode suite in Unity before/during `/story-done`.

## Session Extract — /code-review 2026-07-11 (Networking Core Story 007)

- Verdict: APPROVED WITH SUGGESTIONS
- `/code-review` ran with 2 specialists in parallel (unity-specialist, qa-tester). unity-specialist: no bugs found, verified all byte-size math/overflow logic/IEquatable implementations correct, IL2CPP-clean (no LINQ/boxing/Enum.IsDefined/ArrayPool.Shared). qa-tester: found 3 real coverage gaps → fixed → all closed.
- Gaps found and fixed: (1) `MaxSingleOpaqueSubMessagePayloadBytes` (496-byte) boundary guard had zero test coverage — added `RUBatchWriter_OpaqueSubMessagePayloadAtMaxBoundary_WritesSuccessfully` (496, succeeds) and `..._OneByteOverBoundary_ThrowsInvalidOperationExceptionAndLogsOversizedSubMessage` (497, throws); (2) no test proved the destination buffer stays untouched when `RUBatchWriter.Write` throws — extended the existing misrouted-category test with a `CollectionAssert.AreEqual(new byte[...], buffer)` check; (3) the two 200/100-tick load-fixture tests generated an unasserted flood of `Debug.LogWarning` (Position-packet overflow fires every tick/client by design) — wrapped both in `LogAssert.ignoreFailingMessages = true/false` to stop it masking a future genuine failure or tripping a stricter CI logging policy. Test count: 21 → 23 methods (25 executed cases).
- Non-blocking findings, logged as follow-ups (not fixed this pass): (a) `RUBatchWriter.Write` allocates several `List<T>`/array objects on every call (up to 1000 calls/sec at the documented 50-client/20Hz scenario) — contradicts the GDD's stated "zero GC on the hot path" rationale for buffer pooling; unity-specialist rated this SUGGESTION not BLOCKING (small short-lived Gen0 objects, mechanical fix), recommends threading pre-allocated scratch structures through as a follow-up, mirroring the `ClientBufferSet` pattern; (b) 4 provisional `MessageTypeID` values (`DamageEvent` 0x0301, `GoldSyncEvent` 0x0520, `CycleTimerBroadcast` 0x0302, `EntityPositionUpdate` 0x0303) are not formally registered anywhere except this story's own doc comments — both specialists agree this is acceptable-but-flagged, not an ADR violation, and should be resolved via a Networking ADR amendment or a dedicated ID registry before Story 025-027 (dispatch) or any client decoder is built against them.
- Tech debt logged: TD candidate — GC allocations in `RUBatchWriter.Write` hot path (not yet added to `docs/tech-debt-register.md`, flag for next docs pass); MessageTypeID registry gap (same).
- Files updated: `tests/EditMode/Networking/WireProtocol_BatchFraming_tests.cs` (+2 new tests, 2 tests hardened against log-flood flakiness, 1 test extended with an untouched-buffer assertion)
- Next: `/story-done production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md` — this is the last story in the Wire Protocol Core cluster (003-008)

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 006)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-006-priority-path-cap-two-path-delivery.md` — Priority-Path Cap & Two-Path Delivery Model
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — 2 doc-accuracy issues found and fixed (Flush's "up to PRIORITY_PATH_CAP" claim was false when exempt exceeds cap; misleading StaleDiscardComparer cross-reference), 1 coverage gap found and fixed (exempt-overflow test) → 6 test methods total
- **Third design-inconsistency resolution this epic**: story's `PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued` formula (additive) contradicted AC-NC-35's explicit displacement language. Implemented displacement (matching the AC). Verified correct THREE times independently: my own pre-review hand-trace, plus both unity-specialist and qa-tester independently re-traced from scratch and agreed.
- Tech debt: None new (bulk-transfer exemption and exempt-overflow behavior documented as explicit scope boundaries, not implemented — neither required by any AC)
- Files updated: `production/epics/networking-core/story-006-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 006 → Complete)
- Next recommended: Story 007 — R-U/U-U Batch Framing, Buffer Pooling & Overflow Policy (`production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md`) — last story in the Wire Protocol Core cluster (003-008), Type: Integration

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
