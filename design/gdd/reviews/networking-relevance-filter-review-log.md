# Review Log: Networking Relevance Filter

## Review — 2026-05-19 — Verdict: APPROVED
Scope signal: L
Specialists: lean — no agents (targeted fix verification)
Blocking items: 0 | Recommended: 2 (resolved in this session)
Summary: All 9 MAJOR REVISION NEEDED blockers from 2026-05-18 confirmed resolved. Two lightweight recommended revisions applied: removed stale "(not yet authored)" qualifier from Party System GDD dependency row (party-system.md is APPROVED 2026-05-17); added INetworkTestObserver.OnRUBatchEntityHealthUpdates verification reference to AC-RFR-03 for consistency with AC-RFR-01/07. Three pre-implementation gates remain open as documented OQs: OQ-RFR-2 (SetTarget registration in MCR-2/CCR-3/wire-protocol — BLOCKING), OQ-RFR-3 (wire-protocol Scenario C update — BLOCKING), OQ-RFR-4 (observer hook in test-harness — advisory).
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (2026-05-18, 9 blockers)

## Authoring Pass 1 — 2026-05-19
Blockers addressed: 9/9 | Recommended: 3 addressed (O(n²) complexity, cascade docs, mutation order)
Design decisions: (1) Self-HP via self-slot EHU in R-U batch; (2) EntityHealthUpdate suppressed for party members — separation invariant; (3) SetTarget schema inline in RFR-3a, external doc updates flagged as OQ-RFR-2.
Summary: Complete structural rewrite of relevance set model. Single HashSet replaced with two-set architecture (EHU set = {self, non-party target}; PMHU set = {party members}). Algorithm complexity reduced from O(n²) to O(n). SetTarget RPC schema defined inline. Self-HP live update path established via self-slot. EC-RFR-2/3/4/5 updated. All 7 ACs rewritten or replaced; 2 new ACs added. Open Questions section added with 4 items (2 blocking before implementation). entities.yaml updated.
Ready for: lean re-review

## Review — 2026-05-18 — Verdict: MAJOR REVISION NEEDED
Scope signal: L
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, creative-director
Blocking items: 9 | Recommended: 5
Summary: Five independent blockers prevent correct implementation: (1) target-selection RPC has no wire message definition anywhere in the Channel Contract, blocking RFR-3/RFR-4 and three ACs; (2) self-HP live update path is broken — EntityHealthUpdate excludes self and no R-OD message delivers mid-combat HP damage to the player's own bar; (3) EntityHealthUpdate and PartyMemberHealthUpdate double-count currentHP for party members with no suppression rule; (4) F-RFR-2 byte arithmetic is stale — PartyMemberHealthUpdate is 24 bytes (not 16) after OQ-PS-6 revision; (5) O(n²) server-side complexity at 20Hz with no CPU cost accounting against tick budget. Additional blockers: dead party member permanently occupies relevance slot (slot blocker in active combat); concurrent mutation order undefined; three ACs are blocking (AC-RFR-01 un-mechanizable, before-flush timing case untested, RFR-5 own-HP exclusion has no AC); MAX_RELEVANCE_SET_SIZE cascade undocumented.
Prior verdict resolved: No — first review
