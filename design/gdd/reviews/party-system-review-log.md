# Party System — Design Review Log

---

## Review — 2026-05-17 — Verdict: APPROVED
Scope signal: L
Specialists: None (lean depth)
Blocking items: 3 | Recommended: 2
Summary: Lean re-review after targeted blocker fixes from 2026-05-16. All 7 structural blockers from the first review were correctly applied. Three remaining blockers were stale-reference cleanup from an incomplete MAX_PARTY_SIZE 6→4 edit pass: six locations in prose/ACs/OQs still referenced 6, the CR-PS-2 array size was ambiguous, and three propagation items instructed developers to change correct [1,4] ranges to [1,6]. All fixed in-session. No design decisions required. Document now internally consistent and ready for implementation epics. OQ-PS-4 (F-LS-2 supersession in leveling-system.md) upgraded to HIGH priority — two live formulas with opposite XP direction remain a programmer trap.
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (2026-05-16) fully resolved.

---

## Review — 2026-05-16 — Verdict: MAJOR REVISION NEEDED (Revised same session)
Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, network-programmer, performance-analyst, economy-designer, ux-designer, creative-director (senior synthesis)
Blocking items: 11 | Recommended: 6
Prior verdict resolved: No — first review

Summary: First full-depth review. Core party state architecture (PartyID=0 sentinel, rrNextIndex ownership, flat struct design, Ghost/reconnect grace) is well-specified. Primary structural issues: MAX_PARTY_SIZE=6 infeasible on iPhone landscape and undocumented as a scope change; AC-PS-8 directly contradicted CR-PS-4 (reject vs. queue); rrNextIndex do-while had no termination guarantee; F-PS-1 safe-range cross-product produced negative XP at tuning boundaries; Mathf.RoundToInt banker's rounding claim was factually wrong; LeaderIndex compaction algorithm unspecified; six party wire schemas missing from networking-wire-protocol.md. Creative-director binding decisions: MAX_PARTY_SIZE→4 (MVP), AC-PS-8→queue, F-PS-1 redesign as bonus (overridden by user — deduction kept), L60 exclude from N_eligible (overridden by user — inclusion kept). All non-overridden blockers applied in same session. Wire schemas flagged for dedicated networking session (OQ-PS-6).

User design overrides from creative-director binding decisions:
- F-PS-1 kept as deduction (not redesigned as bonus) — Social Gravity validated by 2× kill speed throughput
- L60 members kept in N_eligible — healer/support contribution to harder content justifies the per-kill impact
