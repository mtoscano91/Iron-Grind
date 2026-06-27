# Review Log: Networking Channel Contract

## Review — 2026-05-18 — Verdict: MAJOR REVISION NEEDED
Scope signal: L
Specialists: network-programmer, systems-designer, qa-lead, game-designer
Blocking items: 14 | Recommended: 9
Summary: CCR-3 routing table covers only 31 of MCR-2's 49 messages — 20 messages (all loot/party/inventory messages added in MCR-2 expansion on 2026-05-17) have no direction/channel/context routing entry, placing the AC-MCR-04 CI gate in guaranteed-failure state. Beyond the structural gap: protocol correctness issues (RttProbe correlation unimplementable, GoldSyncEvent forced R-OD path invisible in CCR-3, PlayerJoinedZone ordering invariant unenforceable across separate transport paths, GhostDismissRequest has no rejection response), CCR-4 invariants incorrectly scoped (R-OD absolute-state rule applies to events and commands), F-CCR-1 missing mandatory variable table, and four blocking AC defects including zero ACs for CCR-4 channel invariants.
Prior verdict resolved: No — first review

## Authoring Pass 1 — 2026-05-18
Blockers addressed: 14/14 | Recommended: 0 addressed (deferred)
Summary: Added 20 missing CCR-3 routing rows (loot/party/inventory messages from MCR-2 expansion). Defined GhostDismissRejected (schema pending). Fixed GoldSyncEvent forced-path documentation, PlayerJoinedZone ordering invariant (server-side guarantee + client buffering), RttProbeEcho probeSequence correlation, CCR-4 absolute-state invariant scope (exempt C→S commands), F-CCR-1 variable table. Fixed AC-CCR-02; added AC-CCR-06 through AC-CCR-09 (CCR-4 invariant coverage + routing CI gate). Open follow-up: GhostDismissRejected requires MCR-2 entry before schema acceptance.
Ready for: lean re-review

## Review — 2026-05-18 — Verdict: APPROVED
Scope signal: L
Specialists: None (lean — single-session analysis)
Blocking items: 0 | Recommended: 5
Summary: All 14 blockers from the 2026-05-18 MAJOR REVISION NEEDED pass verified closed. CCR-3 routing table now covers all MCR-2 messages (52 entries including schema-pending rows). Protocol correctness fixes confirmed (RttProbeEcho probeSequence, GoldSyncEvent forced path, PlayerJoinedZone ordering, CCR-4 scope). F-CCR-1 variable table complete. 9 ACs present and testable. Advisory items: GOLD_MAX_CONSECUTIVE_DROP missing from Tuning Knobs; AC-CCR-06 pass criterion should be automatable; ordering ACs needed for GroundItemSpawned chain and PartyStateUpdate prerequisite; externally-owned constants need cross-reference pointers; GhostDismissRejected MCR-2 entry still pending (known CI noise item).
Prior verdict resolved: Yes
