---
name: Networking Channel Contract GDD adversarial review Pass 1
description: 4 BLOCKING items — fragment count unanchored, EC-CCR-1 compound scenario understated, cap=4 worst case unanalyzed, HOL blocking claim logically incoherent; cross-doc BLOCKING against MCR gold lag bound
metadata:
  type: project
---

Adversarial performance review of networking-channel-contract.md, 2026-05-17.

**Known prior context:** B-22 (wire protocol Pass 5) flagged ZoneStateSnapshot fragment/cap interaction under mass zone join; marked resolved in wire protocol. This CCR document reintroduces the same scenario and must be re-examined on its own merits.

**4 BLOCKING items (within document scope):**

1. **B-CCR-1 — "7 snapshot fragments" is an unanchored estimate**
F-CCR-1 states "Zone entry at n=50 (7 snapshot fragments)" as a given. No derivation exists. The wire protocol defines per-entity byte costs, but no formula in this document ties fragment count to serialized zone state size. At 1,400-byte MTU, a bare-minimum n=50 snapshot (positions + HP + state flags) is ~900-1,100 bytes -- 1 fragment, not 7. For 7 fragments to be correct, the serialized state must be ~8,400-9,800 bytes, which is never stated or derived. PathCapacity_effective for zone entry and EC-CCR-1's "350ms drain" claim both depend on this number. Required fix: derive fragment count from wire protocol byte costs using ceil(SerializedZoneStateBytes / (MTU - header bytes)).

2. **B-CCR-2 — EC-CCR-1 compound scenario is incomplete; worst-case delivery time understated**
EC-CCR-1 models 49 PlayerJoinedZone + 7 cap-exempt fragments + 1 SessionReady. It does not specify SessionReady's priority relative to PlayerJoinedZone within the non-exempt queue. If they share equal priority, the non-exempt queue is 50 messages (ceil(50/8) = 7 ticks). SessionReady can be deferred to tick 7 (350ms). Additionally, during zone restart, GoldSyncEvent, KillEvent, SelfDamageEvent (all R-OD non-exempt per MCR) compete for the same 8 slots per tick, reducing PlayerJoinedZone throughput below 8/tick and extending the stated 350ms estimate. Neither the SessionReady deferral risk nor the concurrent R-OD competition is acknowledged. "Acceptable" is stated without scoping conditions.

3. **B-CCR-3 — PRIORITY_PATH_CAP minimum value (cap=4) produces 650ms zone-join latency with no analysis**
The safe range is [4, 16]. At cap=4: ceil(49/4) = 13 ticks = 650ms. With SessionReady competing: ceil(50/4) = 13 ticks still, but additional R-OD competition can extend to 14-15 ticks (700-750ms). The tuning knob table describes only the high-end risk ("above 16 risks HOL blocking"). The low-end impact is completely absent. "Safe range" implies both bounds are safe, but no analysis supports the lower bound. Required fix: state worst-case PlayerJoinedZone drain time as a function of cap, derive minimum cap from maximum acceptable join latency.

4. **B-CCR-4 — HOL blocking claim for cap > 16 is logically incoherent**
Tuning Knobs state "above 16 risks head-of-line blocking for enhancement-path messages." Enhancement-path messages are cap-exempt -- they pass through regardless of cap value. Raising the non-exempt cap cannot block cap-exempt messages by definition. The stated upper-bound rationale is logically false. The only coherent mechanism would be transport-layer receive buffer saturation (not a P1 queue ordering issue), which is not the argument the document makes. The upper bound of [4, 16] has no valid justification. Required fix: state the actual limiting factor for the upper bound (e.g., transport queue depth, per-tick serialization headroom) with derivation.

**1 RECOMMENDED item:**

5. **R-CCR-1 — Simultaneous organic multi-join flood is unscoped; O(n²) burst not flagged**
EC-CCR-1 analyzes server-restart repopulation. Normal zone fills (k players joining simultaneously) are not analyzed. At k=5 simultaneous joins in an n=45 zone: 5 PlayerJoinedZone per existing player, drains in 1 tick at cap=8 -- fine. At k=25 (zone shard merge): 25 messages per player, 4 ticks = 200ms. Zone-wide send volume is O(k × n) -- at n=50, k=50, this is 2,500 messages in one scheduling window, plus ZoneStateSnapshotFragments for each joiner. Document should confirm organic multi-join is bounded by EC-CCR-1 and flag the zone-wide O(n²) burst as a server-side concern cross-referencing wire protocol tick budget. Also compounds B-23 (ghost session buffer overflow) if join burst precedes cleanup.

**1 cross-document BLOCKING item (requires MCR reconciliation):**

6. **B-CCR-X — MCR states 200ms max gold lag; CCR EC-CCR-1 shows 350ms of full-cap P1 traffic**
B-MCR-3 (MCR review, same date) found that forced GoldSyncEvent is not cap-exempt and can be deferred when the P1 queue is full. EC-CCR-1 shows exactly this scenario: 7 ticks of full-cap P1 traffic during zone-restart repopulation. A forced GoldSyncEvent queued during repopulation waits up to 350ms, not the 200ms stated as the maximum in the MCR document. One bound is wrong; escalate to both GDD owners.

**Cross-document links:**
- [[Networking Wire Protocol adversarial review]]: B-22 (fragment/cap interaction, marked resolved) reappears here as B-CCR-1/B-CCR-2.
- [[Networking Message Criticality Contract adversarial review Pass 1]]: B-MCR-3 (forced gold deferral) conflicts with EC-CCR-1's 350ms sustained full-cap scenario.
- [[Networking Core GDD adversarial review (all passes)]]: B-23 (ghost session buffer overflow) compounds R-CCR-1 simultaneous-join burst.

**How to apply:** When reviewing any GDD that references zone entry or PlayerJoinedZone delivery timing, use 350ms (cap=8) or 650ms (cap=4) as the worst-case bounds -- and note both are unvalidated until the fragment count derivation (B-CCR-1) is fixed. Do not accept "7 fragments" as authoritative without a formula. The CCR HOL blocking rationale is invalid; do not propagate the "cap>16 = enhancement blocking" claim to any downstream document.
