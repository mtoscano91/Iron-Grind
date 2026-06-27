# Item Database — Design Review Log

## Review — 2026-04-25 — Verdict: APPROVED (Review Pass 4)

Scope signal: M (moderate complexity, 3 formulas, 11 downstream dependencies)
Specialists: None — API rate limit hit; structural analysis only
Blocking items: 3 | Recommended: 9
Summary: Pass 4 found 3 blockers, all resolved inline. BLOCKER-1: F-2 sell price formula had no integer rounding rule (F-1 had Mathf.RoundToInt; F-2 did not). BLOCKER-2: Tuning Knobs self-documented that SmallBasePrice=2 × SizeMultiplier(Large)=9.0 = 18g > TierBasePrice(Bronze)=10g, an explicit economy invariant violation — resolved by deferring SmallBasePrice to OQ-9 (TBD) and reformulating the note as a clean invariant constraint. BLOCKER-3: AC-39 was missing with no REMOVED label — added Pass-3-numbering-error documentation. 9 recommended revisions identified (EffectType enum formal definition, AC gaps for duplicate StatID warning, FlatBonus ceiling warning, EffectType validation, Consumable GetItemsByCategory count, F-2 deviation AC, GetItemsByCategory ordering contract, loading mechanism ADR, IconAddress typed reference) — carried as advisory improvements for downstream GDD authors. Document approved with OQ-9 (consumable sell price defaults) and pre-existing OQs 1–8 remaining open.
Prior verdict resolved: Yes — all 3 Pass 3 residual blockers were resolved in Pass 3; Pass 4 found 3 new structural gaps.

---

## Review — 2026-04-25 — Verdict: NEEDS REVISION → Revised (Review Pass 3)

Scope signal: M (moderate complexity, 3 formulas, 11 downstream dependencies)
Specialists: game-designer, systems-designer, economy-designer, qa-lead, unity-specialist, creative-director
Blocking items: 14 | Recommended: 10
Summary: Pass 3 found 14 blockers concentrated in three clusters: (1) implementation contract gaps — ItemDefinition base type (ScriptableObject) never stated, EffectMagnitude unit undefined (flat vs. %), F-1 tolerance spec missing rounding rule, IL2CPP-unsafe Enum.IsDefined on StatID; (2) AC coverage holes — TryGetItem had zero ACs (AC-36/37/38 added), AC-18 missing count assertion, AC-5/AC-9 redundancy resolved, AC-30/31/33 promoted to BLOCKING, GetItemsByCategory pre-init untested (AC-40 added), invalid StatID rejection untested (AC-41 added); (3) economic invariant errors — OQ-8 invariant reframed from total cost to expected cost; F-2 Large potion > Bronze gear inversion flagged with invariant note; NPC buy-price arbitrage constraint added to Rule 10. Stale "6 authoring budget slots" prose corrected to "2". Enhancement prestige principle documented: +5 Bronze ≈ +0 Iron in stats. AC count updated to BLOCKING: 33 | ADVISORY: 5 | Total: 38. Blocker trajectory: 14 → 8 → 8 → resolved — Pass 4 target: ≤3 blockers.
Prior verdict resolved: Yes — all 8 Pass 2 blockers were resolved; Pass 3 found 14 new implementation-contract and AC-coverage gaps.

---

## Review — 2026-04-25 — Verdict: NEEDS REVISION → Revised (Review Pass 2)

Scope signal: M (moderate complexity, 3 formulas, 11 downstream dependencies)
Specialists: game-designer, systems-designer, creative-director (economy-designer, qa-lead, unity-specialist rate-limited; coverage absorbed by systems-designer and main-review pre-analysis)
Blocking items: 8 | Recommended: 7
Summary: Pass 1 residue was the dominant issue — 3 stale rarity/PctBonus references survived the Pass 1 revision sweep (Rule 10, two Edge Cases). A critical arithmetic error claimed "36 potential modifier entries stays safely below 16-entry Character Stats cap" (36 > 16); max StatModifiers per item reduced from 6 to 2 to fix the math. Two verbatim duplicate AC pairs (AC-11/16, AC-6/34 with conflicting severity) removed. Unity nullable class sub-schema serialization issue resolved via [SerializeReference] implementation note. F-3 floor-case warning added (9,999 elemental damage unmitigated at MagicDefense=0). All 8 blockers resolved in-session. AC count updated to BLOCKING: 26 | ADVISORY: 8 | Total: 34.
Prior verdict resolved: Yes — 14 Pass 1 blockers were resolved; Pass 2 found 8 new blockers from incomplete propagation and specialist boundary analysis.

---

## Review — 2026-04-24 — Verdict: MAJOR REVISION NEEDED → Revised (Review Pass 1)

Scope signal: M (moderate complexity, 2 formulas, 4 dependencies)
Specialists: game-designer, systems-designer, economy-designer, qa-lead, unity-specialist, creative-director
Blocking items: 14 | Recommended: 5
Summary: The initial GDD had structural, schema, and implementation soundness issues across all domains. All 14 blocking items were resolved in-session. Major design decisions made during revision: rarity removed entirely (all items of same name have identical stats for all players); weapon sub-types collapsed from 3 to 1 (Sword only) at MVP; `IsEnhanceable` renamed to `IsUpgradeable` with clarified semantics (all equipment = true); PctBonus removed from StatModifierEntry (flat bonuses only at MVP, F-4 formula deprecated). Schema was restructured to add a canonical Schema Reference subsection, hoist StackLimit to top-level ItemDefinition, remove phantom IsEquippable field, and specify StatModifierEntry as a named [Serializable] struct (not ValueTuple). IItemDatabase interface expanded with late-subscriber guarantee on OnDatabaseReady. Final AC count: 31 (22 BLOCKING, 9 ADVISORY).
Prior verdict resolved: First review — revision applied in-session.
