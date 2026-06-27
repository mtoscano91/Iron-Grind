# Review Log: Character Persistence

## Review — 2026-05-24 (Pass 2, lean) — Verdict: APPROVED
Scope signal: L
Specialists: none (lean mode)
Blocking items: 1 resolved inline | Recommended: 4
Summary: All 20 blockers from Pass 1 confirmed closed. One new cross-document blocker found: CR-NET-5.5 in networking-core.md applied a retry+emit-failure protocol to all persistence write failures, contradicting CR-CP-5's no-retry+disconnect protocol for irreversible outcomes. Fix applied to networking-core.md CR-NET-5.5 (scoped to SaveIrreversibleOutcome failures, reference to CR-CP-5 added). Advisory items: Item Consumption System not listed as upstream dependency; no AC for ItemConsumption failure path; AC numbering non-sequential; OQ-NC-SER-2 wire protocol propagation still pending.
Prior verdict resolved: Yes — NEEDS REVISION → APPROVED

## Review — 2026-05-24 — Verdict: MAJOR REVISION NEEDED → NEEDS REVISION (revisions applied inline)

Scope signal: L
Specialists: network-programmer, systems-designer, game-designer, qa-lead, creative-director
Blocking items: 20 | Recommended: 15+
Prior verdict resolved: No — first review

Summary: The core Commit-Before-Broadcast design and optimistic concurrency architecture are sound and directly serve the Earned Power and Legendary Gear pillars. The document specified the load path in depth (LoadCharacter: 11 steps) but left the write path as a stub — SaveSession had no step sequence, no error branches, and no data-read ordering. This single gap was the root cause behind ~7 of the 20 blockers. Additional gaps: rollback ownership unspecified (caller-owns chosen), CR-NET-5.5 vs CR-CP-5 protocol conflict resolved (Option A — split by stakes), GoldVersion orphaned in load/save, InventoryItemCounts type contradiction (byte→int), load-time range validation absent, and two-branch ConcurrencyConflict severity undefined. All 20 blocking items resolved inline with design rulings. Propagation required to networking-wire-protocol.md (OQ-NC-SER-2 closure) and networking-core.md (CR-NET-5.5 scope note) before implementation.
