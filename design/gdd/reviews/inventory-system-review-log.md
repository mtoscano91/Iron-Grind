# Review Log: Inventory System

---

## Review — 2026-10-01 — Verdict: NEEDS REVISION → APPROVED (Lean Re-Review, revised in-session)
Scope signal: M
Specialists: lean — single-session analysis (no specialist agents). Caveat: the reviewer authored the amendments under review earlier the same session; a full specialist pass would give an independent read.
Blocking items: 2 resolved in-session | Recommended: 5 resolved in-session
Prior verdict resolved: Yes — APPROVED (2026-05-17). Re-review triggered by amendments since then: Story 006/008/009 readiness clarifications (Rule 7.20, `SellItem` quantity, `ConsumeItem` lock skip, load edge cases) and the 2026-10-01 TD-045/TD-043 design session (per-slot `EnhancementLevel`).

Summary: All 8 sections complete; all dependency GDDs exist except the Inventory UI GDD and `design/ux/inventory-screen.md` (both pre-existing gaps). Blocker 1: the level invariant was keyed on `Quantity = 1`, which let a stackable item at quantity 1 hold a level that a later pickup or merge would carry into a stack — re-keyed on `StackLimit = 1` across Rule 1.4, 5.14a, 8.24a and the load rule. Blocker 2: the new read of `MAX_ENHANCEMENT_LEVEL` (owned by the Enhancement System) was not declared — added as a soft upstream dependency and a cross-system knob, with the reverse reference in enhancement-system.md. Recommended items applied: Rule 7.20 no-op sentence scoped to stackable items; moving an item onto an identical one (same ItemID and level) decided as a no-op with no event (user decision); AC-INV-21/22 added for reset-on-empty, pickup-at-0 and the rejection paths (24 blocking ACs); `GetSlot` named as the read interface; header status refreshed. Primitive gaps still open: client delivery of slot changes (TD-046), `DISCARD_HOLD_DURATION` and sort order (UX). Story 010, entities.yaml and systems-index updated to match.

---

## Review — 2026-05-17 — Verdict: APPROVED (Lean Re-Review)
Scope signal: L
Specialists: lean — single-session analysis (no specialist agents)
Blocking items: 1 resolved in-session (B-INV-1: PickupRequest signature in Dependencies section) | Recommended: 3 (R-2 MoveItemIn return type added; OQ-INV-5 marked RESOLVED citing CR-LT-13.1–13.3; OQ-INV-6 added for inventory wire schemas)
Prior verdict resolved: Yes — Needs Re-Review status assigned 2026-05-17 after PickupRequest interface reconciliation added CharacterID parameter; single stale reference in Dependencies section was the only blocker.

Summary: Targeted re-review triggered by the 2026-05-17 PickupRequest interface reconciliation (CharacterID added). Interactions table was already correct; only the Dependencies section downstream dependents row was stale. All 8 sections remain complete. Three recommended items also resolved: MoveItemIn return type aligned between Interactions table and Dependencies section; OQ-INV-5 ("blocking for Loot Table authoring") marked resolved since CR-LT-13.1–13.3 now defines bag-full fate; OQ-INV-6 added to track wire schemas for DiscardRequest/Result, MoveRequest/MoveResult, InventoryFullNotification needed in networking-wire-protocol.md. Document re-approved.

---

## Review — 2026-05-15 — Verdict: APPROVED (Pass 2 Lean)
Scope signal: L
Specialists: lean — single-session analysis (no specialist agents)
Blocking items: 0 | Recommended: 4
Summary: All 15 Pass 1 blockers confirmed closed. Four recommended items resolved in-session: AC-INV-7a/7b notation aligned with InventoryChangedEvent changes-array schema; AC-INV-16 added for SellItem interface (NPC Shop sell path); Quantity < 0 edge case added to Persistence load rules; Consumable Use System added to systems-index.md (#23a). GDD status updated to Approved. Downstream dependents (Equipment System, Enhancement System, NPC Shop, Loot Table, Consumable Use System, Character Persistence, Inventory UI) not yet authored — expected at this pre-production stage.
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (Pass 1, 2026-05-15)

---

## Review — 2026-05-15 — Verdict: MAJOR REVISION NEEDED (Pass 1 Full + In-Session Revision)
Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, economy-designer, ux-designer, creative-director (senior synthesis)
Blocking items: 15 | Recommended: 7
Summary: The core data model (20-slot array, stable indices, atomic pickup, lock semantics) is sound and well-specified. Blockers clustered into four themes: Player Fantasy delivery failures (notification spam, missing partial discard, consumable hotbar friction), cross-system authority violations (drop fate owned by wrong GDD, ItemSell removed from registry but present in GDD), undefined interface contracts (MoveItemIn return type, ConsumeItem multi-stack decrement order, F-INV-1 constraint, F-INV-2 precondition, sort type vs. stable indices), and untestable acceptance criteria (missing InventoryChangedEvent schema, sequence-number primitive, no test fixture mechanism). All 15 blockers addressed in-session with 6 design decisions made by the author. AC count grew from 12 to 17 (3 ACs added, 1 split into 3). GoldTransactionReason.ItemSell=7 restored in entities.yaml. Drop fate deferred to Loot Table System GDD via OQ-INV-5.
Prior verdict resolved: N/A — first review
