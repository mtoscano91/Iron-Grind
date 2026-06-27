# Enhancement System — Review Log

---

## Review — 2026-05-23 — Verdict: APPROVED
Scope signal: M
Specialists: None (lean — no specialist agents)
Blocking items: 1 | Recommended: 2
Summary: Lean re-review Pass 4. All Pass 3 fixes confirmed present. One blocker found and fixed inline: AC-ENH-17 had a stale pass condition (`10`, GLOW_LOW) left over from the old 3-band encoding — Pass 3 shifted HIGH from bits `10` to `11` when inserting the GLOW_LOW band, but AC-ENH-17 was not updated. Pass condition corrected to `11`. Two recommended items resolved: AC-ENH-32 added for GLOW_LOW band coverage (level 7, bits `10` — the new band had no AC); AC-ENH-16 title updated from stale "MID" to "VISIBLE_NO_GLOW". Document is now fully consistent with the 4-band encoding. All 32 ACs are present, internally consistent, and independently testable. No open blockers. Enhancement System approved.
Prior verdict resolved: Yes — NEEDS REVISION (Pass 3, 2026-05-23) → all Pass 3 items confirmed present; 1 new blocker found and fixed inline.

---

## Review — 2026-05-23 — Verdict: NEEDS REVISION (fixed inline)
Scope signal: M
Specialists: None (lean — no specialist agents)
Blocking items: 2 | Recommended: 3 | Nice-to-have: 1
Summary: Lean re-review Pass 3. Two blockers found and fixed inline: (1) EC-ENH-2 and EC-ENH-6 contradicted each other on whether the scroll survives a DB write failure — design ruling Option A (full atomic transaction, steps 3–6 commit together); CR-ENH-11 rewritten, EC-ENH-2 rewritten, CR-ENH-15 transaction boundary note added. (2) PrestigeBand 2-bit encoding could not distinguish VISIBLE_NO_GLOW (levels 5–6) from GLOW_LOW (level 7) for remote clients — design ruling Option B (4th band state, monotonic 00/01/10/11); CR-ENH-12 table updated to 4 bands; CR-ENH-13, VR-ENH-2/3/4, TK-ENH-4/5/6 updated throughout. Three recommended items resolved: AC-ENH-28/29/30 added for CR-ENH-17 NPC session lifecycle; AC-ENH-31 added for UI-ENH-6 warning acknowledgment gate; AR-ENH-2 tombstone inserted. EC-ENH-5 updated with foreground re-validation note. entities.yaml band names and threshold constraint notes updated. Document converging well — each pass resolves fewer, lower-severity blockers.
Prior verdict resolved: Yes — NEEDS REVISION (Pass 2, 2026-05-23) → all Pass 2 items confirmed present; 2 new blockers found and fixed inline.

---

## Review — 2026-05-23 — Verdict: NEEDS REVISION (fixed inline)
Scope signal: M
Specialists: None (lean — no specialist agents)
Blocking items: 2 | Recommended: 4
Summary: Lean re-review Pass 2. All Pass 1 blockers confirmed present. Two new blockers found and fixed inline: (1) UI-ENH-5 still contained stale "fail-safe" text contradicting the two-outcome model (missed in Revision Pass 1 purge); (2) CR-ENH-16 NPC location restriction had no server-side enforcement mechanism — new CR-ENH-17 added with explicit NPCInteractionActive session flag, OpenNPCInteraction/CloseNPCInteraction messages, and RejectedNoNPCSession rejection code. Four recommended items also resolved: AC-ENH-25 stale P_f variable name; CR-ENH-12 missing EquipmentSlotRecord.EnhancementLevel copy step; EC-ENH-6 missing UnlockSlot on rollback path; IEnhancementBonusProvider interface signatures updated with GearTier and isWeapon parameters. Entities.yaml corrected: interface methods, F-ENH-1 output_range (212→168), Dark Steel Scroll note (11,316→2,727 scrolls). Document converging — each pass reduces blocker count (22→2→2, severity decreasing).
Prior verdict resolved: Yes — NEEDS REVISION (Pass 1, 2026-05-23) → all Pass 1 items confirmed present; 2 new blockers found and fixed inline.

---

## Review — 2026-05-23 — Verdict: MAJOR REVISION NEEDED
Scope signal: XL
Specialists: economy-designer, game-designer, systems-designer, qa-lead, network-programmer, performance-analyst, creative-director (synthesis)
Blocking items: 22 | Recommended: 8
Prior verdict resolved: No — first review

Summary: The GDD is premature, not poorly reasoned. Three root causes generate the bulk of the blockers: (1) the formula contract is internally broken — F-ENH-5 table diverges from the stated formula by 84% at +8, F-ENH-1's output_range ceiling is wrong (212 vs derivable 168), and F-ENH-2 can exceed the ElementalDamage_ceiling with no clamp defined; (2) the social/prestige transport layer doesn't exist in any approved document — the cross-zone broadcast channel, equipment-appearance replication path, and CR-ENH-4 vs CR-ENH-12 internal contradiction all block the entire prestige layer; (3) the risk curve is misaligned with the emotional architecture — DESTRUCTION_THRESHOLD=3 fires before items earn emotional value (creative-director recommends +5/+6), and full-information transparency at 91% P_d may rationally deter all +9 attempts, collapsing the social gravity the broadcast depends on.

Three design decisions are required before any revision pass will close more than surface-level blockers: (a) DESTRUCTION_THRESHOLD value, (b) social gravity response (opacity vs. Option B: broadcast failures + move payoff threshold, vs. insurance item), and (c) CR-ENH-4/12 contradiction (unequip-to-enhance vs appearance updates while equipped). Creative-director also requires clarification on monetization model (earned-only vs real-money scrolls) to finalize the social-gravity recommendation — Option A (opacity) is off the table for real-money purchases.

Do not re-review until the formula contract is corrected and the three design rulings are documented.

---

## Review — 2026-05-23 — Verdict: NEEDS REVISION (fixed inline)
Scope signal: M
Specialists: None (lean — no specialist agents)
Blocking items: 2 | Recommended: 4
Summary: Lean re-review after Revision Pass 1. All 22 first-review blockers confirmed resolved. Two new blockers found: (1) CR-ENH-13 vs VR-ENH-2 glow threshold contradiction — VFX System rule said MID band = low glow (implying glow at level 5) while ENHANCEMENT_GLOW_THRESHOLD = 7 and VR-ENH-2 said glow active from level 7; design ruling: glow starts at level 7, CR-ENH-13 and VR-ENH-4 updated. (2) Stale `OnEnhancementFailSafe()` signal left in Interactions table Audio row and downstream Dependencies section from pre-revision Fail-Safe model; removed from both locations. All 4 recommended items applied inline. Document structure, formulas (verified correct), state machine, and AC suite are solid.
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (2026-05-23) → all 22 blockers confirmed closed; 2 new blockers found and fixed inline.

---

## Revision Pass 1 — 2026-05-23
Author: Manuel Toscano + Claude Code
Changes applied: 3 design rulings + formula contract corrections + two-outcome model redesign

**Design rulings applied:**
- A) Destruction model: eliminated DESTRUCTION_THRESHOLD and Fail-Safe outcome. All failures = destruction at all levels (two-outcome model).
- B) Social gravity: removed entirely. Each attempt is a fully independent event. "Social Gravity (tertiary)" pillar dropped.
- C) Unequip-to-enhance + NPC location: CR-ENH-4 kept, CR-ENH-12 revised (PrestigeBand computed by Equipment System on equip, not by Enhancement System on success), CR-ENH-16 added (Enhancement NPC in town hub, field enhancement impossible).

**Formula contract corrections:**
- F-ENH-1: output_range annotation added (max = 168, Dark Steel tier level 10 max base 68).
- F-ENH-2: ElementalDamage clamp added (ceiling = 9,999).
- F-ENH-3: worst-case overlap documented as explicit design decision.
- F-ENH-4: complete redesign — two-outcome table (P_s / P_d only), new values anchored at P_d[+4→+5] = 0.35.
- F-ENH-5: expected scrolls recalculated with new table (+9 = ~2,727 scrolls; 955K gold at 350g/scroll).

**Other structural changes:**
- TK-ENH-2 rewritten: DESTRUCTION_THRESHOLD constant removed; replaced with P_s curve inflection tuning note.
- AR-ENH-2 (Fail-Safe sound) removed — no Fail-Safe outcome exists.
- UI-ENH-1: P_f field removed from EnhancementStateUpdate; isDestructionPossible removed (always true); heightened warning threshold set at P_d ≥ 0.35.
- UI-ENH-2: FAILSAFE removed from EnhancementOutcome and EnhancementResultCode enums.
- UI-ENH-6: renamed "Destruction Risk Indicator"; destruction always visible; heightened warning at P_d ≥ 0.35.
- UI-ENH-8: removed Fail-Safe result screen.
- VR-ENH-1: Fail-Safe animation variant removed.
- AC-ENH-10: replaced Fail-Safe test with low-level destruction test (+2, r=0.90).
- AC-ENH-11: updated RNG comment (P_s[4] = 0.65).
- AC-ENH-12: replaced "no destruction below threshold" with two-outcome model test (+0, r=0.96).
- AC-ENH-25: updated probabilities to new table values.
- AC-ENH-26: updated to test standard P_d display vs. heightened warning threshold.
- OQ-ENH-2: resolved — Enhancement NPC in town hub only.

**Remaining open questions (not blocking re-review):**
- +9 broadcast networking channel not yet specified in any approved networking GDD — needs wire protocol amendment before implementation.
- Scroll pricing for Bronze/Iron/Steel tiers (OQ-ENH-1): provisional; requires playtest validation.
