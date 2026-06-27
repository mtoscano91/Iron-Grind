# Damage Calculation — Design Review Log

## Review — 2026-05-15 — Verdict: APPROVED (Pass 2 lean)
Scope signal: L
Specialists: none (lean mode)
Blocking items: 0 | Recommended: 0 remaining
Summary: All 15 Pass 1 blockers verified resolved. Lean review found 2 residuals: (1) HasElementalContribution was applied to Step 11 and VAR-3 during Pass 1 but missed in VAR-1 rows 1/3 and VAR-2 prose, which still used the old `ElementalDamage = 0 / > 0` condition — creating conflicting VFX trigger logic for small elemental weapons; (2) Step 10 dead-entity guard (CurrentHP = 0.0 → IsKill = false) was specified in Edge Cases but not in the step algorithm itself, so it would not be implemented by following the step sequence. Both fixed in-session. Open questions OQ-DC-2 and OQ-DC-4 remain blocking for implementation (ADRs required before any implementation sprint); Equipment System GDD still not authored (hard dependency in Step 4). Document is design-complete and approved.
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (Pass 1, 2026-04-26); in-session revision was applied before this re-review.

---

## Review — 2026-04-26 — Verdict: MAJOR REVISION NEEDED → Revised (Review Pass 1)

Scope signal: L (multi-system integration, 4 formulas, cross-GDD contract fix with Character Stats, 2 BLOCKING ADR prerequisites)
Specialists: game-designer, systems-designer, qa-lead, network-programmer, creative-director
Blocking items: 15 | Recommended: 15
Summary: Pass 1 found 15 blockers in four clusters: (1) cross-GDD ownership conflict — both Damage Calculation Step 10 and Character Stats EC-08 claimed `OnEntityDied` ownership, causing a guaranteed double-fire on every kill; resolved by Option B (DamageCalculation sets IsKill = true only, Character Stats fires the event inside ApplyDamage); (2) pillar contradiction — last-hit-only XP directly violates Social Gravity ("party play is always better"), resolved by adding Rule 4a (full party XP on kill, Party System GDD must expose `GetPartyMembersForXP`); (3) missing architectural prerequisites — server-only enforcement (Assembly Definition added to Rule 1) and double-kill race condition elevated to BLOCKING OQ-DC-4 requiring server tick ADR; (4) AC coverage gaps — 6 critical ACs added/rewritten (F-09b, F-10b, F-10c, F-12b, E-06, I-02b), 5 ACs formally BLOCKED on OQ-DC-2 (RNG injection ADR), Group B kill-detection ACs restructured for Option B, AC-DC-I-01 rewritten from manual proxy to machine-testable assembly scan. Additionally: DamageResult struct field contract documented (pre-crit semantics, PhysicalDamage + ElementalDamage ≠ FinalDamage on crits), HasElementalContribution bool added to struct to fix tinting dead zone at ElementalBonus < 10, Step 9 "safety net" wording corrected to "load-bearing at BaseDamage < 20", and DamageType cosmetic-only MVP rule made explicit. All 15 blockers resolved inline. 15 recommended revisions carried as advisory improvements. OQ-DC-1 (elemental scaling), OQ-DC-2 (BLOCKING — RNG ADR), OQ-DC-3 (XP interface), OQ-DC-4 (BLOCKING — server tick ADR) remain open.
Prior verdict resolved: First review — revision applied in-session.
