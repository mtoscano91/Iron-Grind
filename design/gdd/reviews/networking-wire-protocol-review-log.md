# Review Log — Networking Wire Protocol

---

## Review — 2026-05-11 — Verdict: MAJOR REVISION NEEDED (Pass 1)
Scope signal: XL
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst + creative-director synthesis
Blocking items: 4 clusters (C1–C4) | Recommended: 5
Summary: First formal review (document was self-assigned "Pass 1" status during the networking-core.md split with no prior /design-review run). Root causes: C1 — no upstream Message Criticality Contract (channel assignments ad-hoc; PartyMemberHealthUpdate in wrong channel; player-self DamageEvent must be R-OD for Pillar 2 feedback); C2 — no Direction/Channel Contract and SessionHandshake name collision (server-emitted state seed must be renamed SessionStateSnapshot); C3 — Scenario C position count wrong when party present, F-NET-6 pre-drop count understated by ~22%, fragment count must be dynamic; C4 — 9 rules with zero AC coverage, AC-NC-07 missing wraparound test, AC-NC-17 vacuous, AC-NC-30(b) discard layer unspecified. Recommended path: extract networking-message-criticality.md, networking-channel-contract.md, networking-relevance-filter.md before revising this document.
Prior verdict resolved: N/A — first review.

---

## Review — 2026-05-12 — Verdict: NEEDS REVISION (Pass 2)
Scope signal: XL (document scope); S (remaining revision work)
Specialists: lean — no specialist agents
Blocking items: 1 | Recommended: 2
Summary: All four Pass 1 blocker clusters (C1–C4) substantively closed. One arithmetic error survived C3: F-NET-6 peak row post-drop count was ~4,150 (incorrect) — correct value is ~5,650; pre-drop per-client was 148 (should be 147). AC-NC-21 referenced the pre-filter Scenario C figure (~29.6 KB/s) instead of the post-filter value (~26.0 KB/s). EnhancementOutcomeBroadcast was referenced in CR-NET-7.7 and AC-NC-35 but had no schema stub, making AC-NC-35 non-testable. All three items fixed in-session. Document is ready for Pass 3 lean re-review.
Prior verdict resolved: Yes — 4 Pass 1 blocking clusters closed.

---

## Review — 2026-05-12 — Verdict: NEEDS REVISION (Pass 3)
Scope signal: XL (document); S (remaining work)
Specialists: lean — no specialist agents
Blocking items: 1 | Recommended: 2
Summary: All Pass 2 fixes verified correct. New finding: MAX_MESSAGE_BODY_BYTES safe range lower bound stated as 192 but CycleBroadcast at default MAX_PLAYERS_PER_ZONE=50 is 502 bytes — any value below 502 drops CTBs every tick, directly violating the "CycleTimerBroadcast is never dropped" Pillar 2 invariant. Fixed to [502, 1,400] with cross-knob formula. EnhancementOutcomeBroadcast stub text corrected from "attacker's own client" to zone-wide semantics. AC-NC-36 precondition note added for test harness consistency. All three items fixed in-session.
Prior verdict resolved: Yes — all Pass 2 items closed.

---

## Review — 2026-05-12 — Verdict: APPROVED (Pass 4)
Scope signal: XL
Specialists: lean — no specialist agents
Blocking items: 0 | Recommended: 2
Summary: Full document sweep found zero blocking items. All formulas arithmetically verified. All 15 ACs are independently testable. All prior pass blockers confirmed closed. Pillar alignment coherent across all 4 game pillars. Two advisory ACs recommended (FloorToInt HP serialization, buffer-pool exhaustion) and one nice-to-have EC; none block implementation. Networking ADR (OQ-NC-SER-1–4) must be authored before implementation begins — that gate is external to this GDD.
Prior verdict resolved: Yes — all Pass 3 items closed.
