# Story 028: EntityHealthUpdate/PartyMemberHealthUpdate Relevance Filter Algorithm

> **Epic**: Networking Core
> **Status**: Ready
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

- [ ] **AC-RFR-01** [BLOCKING] (Integration): Given a 50-player zone where client A is solo with no target, when 50 ticks are serialized, then each of A's R-U batches contains exactly 1 `EntityHealthUpdate` (self-slot) and zero `PartyMemberHealthUpdate`.
- [ ] **AC-RFR-02** [BLOCKING] (Integration): Given client A in a 4-person party (B,C,D) with no target, when 1 tick processes, then A's batch contains exactly 3 `PartyMemberHealthUpdate` (one per B,C,D) and exactly 1 `EntityHealthUpdate` (self-slot only) — none for B/C/D or any other entity.
- [ ] **AC-RFR-04** [BLOCKING] (Integration): Given client A in a 4-person party targeting a non-party entity, when the R-U batch serializes at n=50 Scenario C density (10 DamageEvent/tick), then the total batch size is ≤400 bytes (below the 512-byte cap), and no HP or damage sub-message is dropped.
- [ ] **AC-RFR-07** [BLOCKING] (Integration): Given client A with HP=750 at zone entry, when 10 ticks of active combat process (HP decrementing), then each R-U batch contains a self-slot `EntityHealthUpdate` reflecting current authoritative HP, matching the server's tracked value.

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

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs` OR documented playtest evidence (load-density ACs need a running tick loop; the core set-construction algorithm may additionally have EditMode unit coverage)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007 (R-U batch this filter feeds), Story 001 (test harness `OnRUBatchEntityHealthUpdates`)
- Unlocks: Story 029 (target-slot mutation this filter reads)
