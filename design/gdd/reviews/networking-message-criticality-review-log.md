# Review Log: Networking Message Criticality Contract

## Review — 2026-05-17 — Verdict: NEEDS REVISION → APPROVED (Pass 2 lean, inline fix)
Scope signal: S
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 | Recommended: 2
Summary: All 15 prior blockers confirmed closed. One new blocker introduced by the MCR-2 expansion: `PartyMemberHealthUpdate` was added with Pillar 1 + Pillar 3 classification and R-U channel, but lacked the explicit "MCR-3 exception:" annotation required by MCR-3's own text and AC-MCR-06's named exception list — a CI audit would fail this row. Fixed inline: MCR-3 exception annotation added to `PartyMemberHealthUpdate` rationale; `PartyMemberHealthUpdate` added to AC-MCR-06 exception list. Document approved after fix.
Prior verdict resolved: Yes — all 15 prior blockers closed; 1 new blocker found and resolved inline

---

## Review — 2026-05-17 — Verdict: MAJOR REVISION NEEDED
Scope signal: L
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, creative-director (senior)
Blocking items: 15 | Recommended: 6
Summary: MCR-2 table covered only 26 of ~49 wire protocol messages, placing the CI gate (AC-MCR-04) in guaranteed-failure state. Three additional blocker layers were found simultaneously: misclassifications in existing rows (EntityHealthUpdate missing Pillar 3, PartyMemberHealthUpdate missing Pillar 1, LootBidUpdate delivery ambiguous), MCR-5 over-generalizing to event-driven messages that cannot self-correct, and specification errors (F-MCR-1 missing variable table, GOLD_MAX_CONSECUTIVE_DROP=0 description inverted, anomaly threshold fires after 0.4s of normal AoE overflow). All 15 blockers were addressed in the same session: MCR-2 expanded to 49 rows, MCR-5 split into periodic (5a) and event-driven (5b), MCR-4 updated with consecutive-tick anomaly logic and counter reset semantics, F-MCR-1 variable table added, EC-MCR-2 dead code removed, and ACs AC-MCR-06/07/08 added. Pending lean re-review to confirm closure.
Prior verdict resolved: No — first review
