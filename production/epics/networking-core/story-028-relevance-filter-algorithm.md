# Story 028: EntityHealthUpdate/PartyMemberHealthUpdate Relevance Filter Algorithm

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-relevance-filter.md`
**Requirement**: `TR-net-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: This filter determines which entities' HP data enters the R-U batch (Story 007) for a given client — it's the algorithm that keeps that batch within the 512-byte cap at scale, replacing an O(n²) zone-wide broadcast with O(n × MAX_PARTY_SIZE) linear cost.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (pure server-side filtering logic)
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: two disjoint relevance sets — self+target-slot (`EntityHealthUpdate`, max 2/client/tick) and party-set (`PartyMemberHealthUpdate`, max `MAX_PARTY_SIZE-1`/client/tick) — source: RFR-1
- Required: no entity receives HP data from both sets in the same tick (separation invariant) — source: RFR-1

---

## Acceptance Criteria

*From `design/gdd/networking-relevance-filter.md`, scoped to this story:*

- [x] **AC-RFR-01** [BLOCKING] (Integration): Given a 50-player zone where client A is solo with no target, when 50 ticks are serialized, then each of A's R-U batches contains exactly 1 `EntityHealthUpdate` (self-slot) and zero `PartyMemberHealthUpdate`.
- [x] **AC-RFR-02** [BLOCKING] (Integration): Given client A in a 4-person party (B,C,D) with no target, when 1 tick processes, then A's batch contains exactly 3 `PartyMemberHealthUpdate` (one per B,C,D) and exactly 1 `EntityHealthUpdate` (self-slot only) — none for B/C/D or any other entity.
- [x] **AC-RFR-04** [BLOCKING] (Integration): Given client A in a 4-person party targeting a non-party entity, when the R-U batch serializes at n=50 Scenario C density (10 DamageEvent/tick), then the total batch size is ≤400 bytes (below the 512-byte cap), and no HP or damage sub-message is dropped.
- [x] **AC-RFR-07** [BLOCKING] (Integration): Given client A with HP=750 at zone entry, when 10 ticks of active combat process (HP decrementing), then each R-U batch contains a self-slot `EntityHealthUpdate` reflecting current authoritative HP, matching the server's tracked value.

---

## Implementation Notes

*Derived from RFR-1, RFR-2, RFR-6, F-RFR-1, F-RFR-2:*

- **RFR-1 relevance sets** (per client, per tick): self-slot (own EntityID, always present, exactly 1) + target-slot (currently targeted entity, only if NOT a party member, 0 or 1) → feeds `EntityHealthUpdate` (max 2 sub-messages/client/tick). Party-set (all party members excluding self, max `MAX_PARTY_SIZE-1`=3) → feeds `PartyMemberHealthUpdate`.
- **Separation invariant**: if the current target IS a party member, the target-slot `EntityHealthUpdate` is suppressed (that entity already gets richer data via `PartyMemberHealthUpdate`) — no entity ever appears in both sets in the same tick.
- **RFR-2 algorithm** (per-tick, per-client, during R-U batch flush, before the overflow check runs): append self-slot EHU always → append target-slot EHU only if target exists and is non-party → append one PMHU per party member. Complexity: O(1+|partySet|) ≤ O(4) per client, O(n×MAX_PARTY_SIZE) total per tick — this replaces a prior O(n²) zone-wide iteration.
- **RFR-6**: the filter applies uniformly to all zone entity types — targeting a mob/NPC includes its EHU in the relevance set (intentional, combat feedback), same exclusivity rule as player targets.
- F-RFR-2 worked example at n=50, full party + non-party target: `12(header) + 180(10 DamageEvent) + 32(2 EHU) + 72(3 PMHU) + 17(GoldSyncEvent) + 5(ConnectionQualityUpdate) = 318 bytes` — well under the 512-byte cap, versus 512 bytes/~29-HP-updates-dropped without this filter. *Note: `networking-relevance-filter.md`'s Overview section states "~92%" bandwidth reduction while its own F-RFR-1 section states "~87%" for the same scenario — a minor internal inconsistency in the GDD, not something this story needs to reconcile, just be aware the exact percentage cited in code comments should trace to F-RFR-2's concrete byte math, not either summary percentage.*
- This story implements the filter algorithm itself, called from Story 007's batch-flush step — wire the call site into Story 007's serialization order when both exist.

---

## Out of Scope

*Handled by neighbouring stories:*

- `SetTarget` RPC and target-slot mutation itself — Story 029
- The R-U batch this filter's output feeds into — Story 007 (this story is a filter stage Story 007's flush calls)
- Real Party System membership provider — future Party System epic (mock provider for this story's tests)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs`

- **AC-RFR-01**: Given solo/no-target at n=50, then exactly 1 EHU, 0 PMHU per tick.
- **AC-RFR-02**: Given a full 4-person party with no target, then exactly 3 PMHU + 1 EHU (self) per tick, none for party members via EHU.
- **AC-RFR-04**: Given full party + non-party target at Scenario C density, then total batch ≤400 bytes, nothing dropped.
- **AC-RFR-07**: Given 10 ticks of HP-changing combat, then self-slot EHU tracks authoritative HP every tick.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs` — must exist and pass. Corrected during `/story-readiness` (2026-07-21): this section previously said "Story Type: Integration" with a `tests/PlayMode/...` requirement, contradicting the header (`Type: Logic`) and this document's own QA Test Cases section (EditMode path) — the same self-contradiction Story 027 had. All 4 blocking ACs are verified via `INetworkTestObserver.OnRUBatchEntityHealthUpdates` per the GDD's own text, the same structural EditMode-composition mechanism used throughout this epic (including other "n=50"/multi-tick scenarios) — nothing here requires a live tick loop or PlayMode session. See TD-028 for the broader (unresolved, project-wide) question of whether this MMORPG needs a real PlayMode/Integration testing tier — this is further evidence for that entry, not a new tech-debt item.

**Status**: [x] Created — 11 tests, all 4 blocking ACs covered, confirmed passing in a live Unity 6.3.10f1 Editor

---

## Dependencies

- Depends on: Story 007 (R-U batch this filter feeds), Story 001 (test harness `OnRUBatchEntityHealthUpdates`)
- Unlocks: Story 029 (target-slot mutation this filter reads)

---

## Completion Notes
**Completed**: 2026-07-21
**Criteria**: 4/4 passing (AC-RFR-01, AC-RFR-02, AC-RFR-04, AC-RFR-07) — 11 tests in `tests/EditMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs`. First story this session verified via an actual live Unity 6.3.10f1 Editor test run (725/730 project-wide tests passing), not just static review.
**Deviations**:
- `MessageRoutingRegistry` was missing rows for `EntityHealthUpdate`/`PartyMemberHealthUpdate` (this story's own new message types) — added. While adding them, discovered Story 026's `GoldSyncEventForcedDelivery` and Story 027's `SelfDamageEvent` were also never registered (an invisible gap until the registry's own completeness test could actually run in a live Editor for the first time). Added all 4 rows, cross-referenced against the GDD; code review caught and fixed one incorrect direction value (`PartyMemberHealthUpdate` used `ServerToParty` instead of `ServerToOwningClient`).
- TD-029 logged: 5 genuine, pre-existing test failures surfaced in already-"Complete" Stories 018/020/021 — unrelated to this story, left for a dedicated future session.
- Fixed 3 unrelated pre-existing compile errors blocking all EditMode compilation (Story 016's `SessionTokenStore.cs` and its test file; Story 027's test file) — necessary to get any real test run this session.
- TD-028 (systemic Type/Test-Evidence pattern) and the empty `tr-registry.yaml` — both pre-existing, unchanged.
**Test Evidence**: Logic — `tests/EditMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs`, 11 tests, all blocking ACs covered, live-confirmed passing.
**Code Review**: Complete (lean self-performed review: unity-specialist 1 BLOCKING (wrong `MessageDirection` on the `PartyMemberHealthUpdate` registry row, independently verified against the GDD and fixed) + qa-tester TESTABLE, both parallel).
