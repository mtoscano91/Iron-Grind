# NPC Shop — Design Review Log

## Review — 2026-06-07 (lean pass 2) — Verdict: NEEDS REVISION → APPROVED

Scope signal: XL
Specialists: None (lean mode — single-session analysis)
Blocking items: 5 (all fixed in-session) | Recommended: 3
Prior verdict resolved: Yes — all 10 MAJOR REVISION NEEDED blockers confirmed closed

Summary: All 10 prior blockers resolved. Lean pass found 5 internal consistency issues introduced by the revision: rule numbering collisions across CR-SHOP-6/7 and CR-SHOP-8/9; Interactions table `CompensatingRefund` enum mismatch (showed `Other`); Dependencies upstream table SellItem interface still in 2-param form; ACs 20–25 using old 2-param SellRequest; stale cross-document correction note (Item Database amendment already applied). All 5 fixed in-session. GDD marked Approved pending 4 pre-implementation gates (OQ-NS-4/5/6/7) — these gate sprint commitment only, not design completeness.

---

## Review — 2026-06-07 — Verdict: MAJOR REVISION NEEDED

Scope signal: XL
Specialists: `systems-designer`, `economy-designer`, `ux-designer`, `qa-lead`, `network-programmer`, `game-designer`, `creative-director`
Blocking items: 10 | Recommended: 18
Prior verdict resolved: No — first review

Summary: Two missing foundational contracts drove the majority of findings. (1) No purchase-transaction protocol: gold debit and item grant are unlinked with no PENDING_PURCHASE state, idempotency key, or durable refund obligation — every failure mode (server crash, TTL expiry, zone change mid-transaction) loses gold permanently. (2) Economy baseline unanchored: the stated "90–150 g/hr" farming rate has no derivation, and the Bronze farming window math is self-contradicting (states 30–50 min; actual at cited rates is 36–60 min; higher tiers violate the invariant by 25–75%). Blocking fixes applied in this session: CR-SHOP-4 wall-clock TTL contradiction resolved; CR-SHOP-5 unsupported batching requirement removed + ADR gate added (OQ-NS-5); CR-SHOP-6 touch spec added; CR-SHOP-8 revised to support partial-stack sell; CR-SHOP-11 scroll purchase confirmation added; Player Fantasy rewritten; farming window corrected; 11 new/revised ACs; 3 new pre-implementation open questions (OQ-NS-5/6/7). Four pre-implementation gates remain open before sprint commitment: Purchase Transaction Integrity ADR (OQ-NS-5), Enhancement System preemption callback (OQ-NS-6), wire message registration (OQ-NS-7), Enhancement Scroll Item Database records (OQ-NS-4). Re-review recommended at `--depth lean` after OQ-NS-5/6/7 are resolved.
