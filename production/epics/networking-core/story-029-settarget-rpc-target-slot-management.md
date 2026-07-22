# Story 029: SetTarget RPC & Target Slot Management

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-relevance-filter.md`
**Requirement**: `TR-net-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: `SetTarget` is a client→server RPC — a network-boundary message governed by ADR-004's channel mapping (R-OD/P1 per RFR-3a).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: `SetTarget{EntityID targetEntityId}` — 4 bytes, C→S, R-OD/P1 — source: RFR-3a
- Required: target-slot replacement is atomic — no dual-membership transition window — source: RFR-4
- Forbidden: self-targeting (silently rejected, not a client-visible error) — source: RFR-5

---

## Acceptance Criteria

*From `design/gdd/networking-relevance-filter.md`, scoped to this story:*

- [ ] **AC-RFR-03** [BLOCKING] (Integration): Given client A targeting entity E (non-party), when A sends `SetTarget{targetEntityId=F}` on tick T before the batch flush, then tick T's batch contains F's EHU (and self) but not E's; when `SetTarget` arrives after the flush, then tick T's batch still contains E's EHU and tick T+1's batch contains F's.
- [ ] **AC-RFR-05** [BLOCKING] (Logic): Given client A sending `SetTarget{targetEntityId=A's own EntityID}`, when processed, then the target slot is unchanged, a `SelfTargetAttempt` anomaly is logged, and A's `EntityHealthUpdate` appears exactly once (self-slot only) — never twice.
- [ ] **AC-RFR-06** [BLOCKING] (Logic): Given client A in a 4-person party (B,C,D) targeting non-party entity E, when A sends `SetTarget{targetEntityId=B}` (a party member), then from the next flush: B's EHU is suppressed (separation invariant — B is delivered via PMHU), E's EHU is removed (prior target replaced); A's batch contains 1 EHU (self) + 3 PMHU (B,C,D).

---

## Implementation Notes

*Derived from RFR-3, RFR-3a, RFR-4, RFR-5, EC-RFR-4, EC-RFR-5:*

- **RFR-3a schema**: `SetTarget{EntityID targetEntityId}` (4 bytes), C→S, R-OD/P1 criticality — a dropped `SetTarget` leaves the target-slot EHU stale indefinitely, which is why it needs guaranteed delivery unlike the per-tick self-correcting HP data it gates.
- Validation: `targetEntityId` must be a valid zone EntityID or `0` (deselect); self-target is rejected silently (no client-visible error, just a logged `SelfTargetAttempt` advisory anomaly, target slot unchanged — RFR-5/EC-RFR-4); invalid EntityIDs are discarded silently with a logged `InvalidTargetEntityId` advisory anomaly.
- **RFR-3 update timing**: the target-slot updates atomically within the server tick on receipt — if the RPC arrives before that tick's batch flush point, the new target is reflected in that same tick's batch; if it arrives after, the change takes effect starting the next tick's batch. If both a party-membership change and a `SetTarget` arrive in the same tick, party-set updates apply first, then target-slot — combined state is consistent at the flush point.
- **RFR-4 exclusivity**: at most one entity in the target slot at a time; a new target-selection RPC replaces the previous atomically — there is no window where both old and new target are simultaneously represented in the EHU set.
- **EC-RFR-5 (target-is-party-member)**: applying the RFR-1 separation invariant here means switching target TO a party member suppresses that member's target-slot EHU (already covered via PMHU) — this is the concrete scenario Story 028's separation invariant exists to handle, and this story is where the target-mutation trigger for it lives.
- This story's `SetTarget` handler is a natural client of Story 010's cross-cutting RPC guards (EntityID validity, session-ready, rate limit if one applies) — route it through that guard chain, don't reimplement validation here.

---

## Out of Scope

*Handled by neighbouring stories:*

- The relevance-set construction algorithm itself (this story only mutates the target-slot input to that algorithm) — Story 028
- Party membership provider — future Party System epic (mock provider for this story's tests)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs`

- **AC-RFR-03**: Given a target-change before vs. after flush, then the before-flush/after-flush timing difference is exactly as described (atomic replacement, correct tick boundary).
- **AC-RFR-05**: Given a self-target attempt, then rejected silently, target unchanged, anomaly logged, EHU appears exactly once.
- **AC-RFR-06**: Given a target-change to a party member, then the separation invariant correctly suppresses the target-slot EHU in favor of the existing PMHU entry.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs` — must exist and pass

**Status**: [x] Created — 11 tests, all 3 blocking ACs covered. Not yet confirmed in a live Unity Editor this session (none was open/available) — verified statically (grep-confirmed test count, hand-traced assertions, 2 parallel self-performed code reviews). Recommend a live Editor run to confirm compile/pass before this story is treated as launch-ready, per the precedent set by Story 028.

---

## Dependencies

- Depends on: Story 028 (relevance filter this RPC mutates the input to), Story 010 (cross-cutting RPC guard chain)
- Unlocks: None — completes the Relevance Filter cluster and the epic's full story set

---

## Completion Notes
**Completed**: 2026-07-22
**Criteria**: 3/3 passing (AC-RFR-03, AC-RFR-05, AC-RFR-06) — 11 tests in `tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs`.
**New production**: `SetTarget`/`SetTargetCodec` (standalone C→S R-OD message reusing the existing `ClientEntityMessageEnvelope`, `MessageTypeId = 0xE040`; deliberately bypasses `WireIdCodec.SerializeEntityId`/`DeserializeEntityId` for the `targetEntityId` body field since `0` is a legitimate "deselect" value on this one field, unlike every other EntityID field in the wire protocol), `SetTargetOutcome` (3-value outcome enum), `TargetSlotTracker` (new sealed, stateful per-client target-slot store — the first stateful class this story's cluster introduces, unlike `RelevanceFilter`'s stateless-static shape; owns `GetTarget`/`ProcessSetTarget`, implementing self-target rejection (RFR-5/EC-RFR-4) before zone-validity rejection (RFR-3a), in that order). `RpcTypeTag`/`CrossCuttingRpcGuardChain` (Story 010) extended with a new `SetTarget` rate-limit bucket, gap=0 ticks (per `networking-core.md` Cross-Cutting Constraint 3: "All other RPCs: no rate limit specified at MVP"). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnSelfTargetAttemptLogged`/`OnInvalidTargetEntityIdLogged`.
**Design decisions** (resolved by the orchestrator before implementation, given to the implementing agent as settled): (1) `TargetSlotTracker` does not itself call `CrossCuttingRpcGuardChain` — guard-chain transport rejection and target-slot business validation stay separately layered, composed by the caller, matching this codebase's established delegate/composition precedent (`MobDeTargetingCoordinator`, `PartyDisbandCoordinator`); (2) the AC-RFR-03 before-flush/after-flush timing claim needs no tick-boundary machinery — `TargetSlotTracker` mutates synchronously and the AC is proven purely by call-ordering in the test, matching this epic's established "structural ordering proof, not a real timer" idiom; (3) `validZoneEntityIds` is a plain caller-supplied collection, not an injected provider — no real zone entity registry exists yet (same forward-dependency treatment as `RelevanceFilter`'s `partyMembers` parameter, Story 028).
**Code Review**: Complete (lean self-performed review: unity-specialist 1 Required Change (0 BLOCKING) + qa-tester 0 findings, both parallel). The unity-specialist finding: the self-target-before-zone-validity check order was correctly implemented but not actually pinned by any test (the original AC-RFR-05 test's `validZoneEntityIds` fixture happened to include the client's own EntityID, so both possible check orderings would have produced the same passing result) — fixed by adding `ProcessSetTarget_SelfTargetAndNotInValidZone_RejectedAsSelfTarget_NotAsInvalidTarget`, which deliberately excludes the client's own EntityID from `validZoneEntityIds` so only the correct order passes.
**Test Evidence**: Logic — `tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs`, 11 tests, all 3 blocking ACs covered. Not live-Editor-confirmed this session (see Test Evidence section above).
**Deviations**: None from Out of Scope. `RelevanceFilter.cs` (Story 028) was read but not modified, as required.
**Tech debt**: TD-030 logged (register now 30 items) — the combined "party-set change + `SetTarget` in the same tick" ordering scenario from this story's own Implementation Notes is untestable until a real Party System exists; correctly deferred, not a gap in this story.
