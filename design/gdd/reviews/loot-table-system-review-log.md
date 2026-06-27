# Review Log: Loot Table System

---

## Review — 2026-05-17 — Verdict: APPROVED (Lean Re-Review Pass 2)
Scope signal: L
Specialists: lean — single-session analysis (no specialist agents)
Blocking items: 2 resolved in-session (B-LT-1: Dependencies table rrNextIndex ownership contradicted CR-LT-6; B-LT-2: Party System status "Not yet designed" after Approval) | Recommended: 3 (R-1 CR-LT-13.1 stale OQ-LT-4 reference; R-2 inventory-system.md bidirectionality note stale; R-3 Interactions table "→ writes rrNextIndex" corrected to IPartySystem.AdvanceRrNextIndex call)
Prior verdict resolved: Yes — all 5 NEEDS REVISION blockers from 2026-05-17 Pass 1 confirmed closed; 2 new propagation misses found and fixed in-session.

Summary: All five prior session blockers verified closed. Two new blockers discovered — both propagation misses from the CR-LT-6 ownership rewrite: the Dependencies table still said rrNextIndex was "owned and updated by the Loot Table System" (direct contradiction with CR-LT-6) and still listed Party System as "Not yet designed" despite Approval on the same day. Root cause: the R-1 fix updated CR-LT-6 and the Dependencies section's Reads/Writes description, but not the hard dependency note text or the status field in the same row. Three recommended items also fixed: CR-LT-13.1 OQ-LT-4 forward reference updated to past tense, inventory-system.md bidirectionality note updated to re-Approved, Interactions table AdvanceRrNextIndex call corrected. Document approved.

---

## Review — 2026-05-17 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: lean (no specialist agents — single-session analysis)
Blocking items: 5 resolved in-session (PRNG seeding, wire schemas, InventoryChangedEvent, CR-LT-13.3/AC-LT-24 contradiction, TrySpendGold missing from CR-LT-9) | Recommended: 5 (CR-LT-6 stale language ✓ fixed, Auctioning state clarity, proximity check strategy, pauseBudgetRemaining ownership, zone teardown crash path)
Prior verdict resolved: Yes — all 7 major issues from 2026-05-16 resolved (PartyID collision, API mismatch, state transition gap, N=0 division, zone teardown idempotency, bag-full redesign, player fantasy language); 5 new blockers found and fixed in-session.

Summary: Substantial convergence from MAJOR REVISION NEEDED to NEEDS REVISION. All five priority blockers from the first review were resolved before this pass. Five new blockers were discovered and fixed in-session: TrySpendGold missing from CR-LT-9 (auction economy was broken — no deduction of winner's bid); CR-LT-13.3/AC-LT-24 contradiction on expiry warning re-fire after TTL extension; PRNG seeding policy unspecified; all 10 loot wire message schemas absent from networking-wire-protocol.md; InventoryChangedEvent undefined. All five fixes applied. Document is ready for lean re-review in a fresh session.

---

## Review — 2026-05-16 — Verdict: MAJOR REVISION NEEDED

Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, economy-designer, network-programmer, ux-designer, performance-analyst, creative-director
Blocking items: 15 | Recommended: 10
Prior verdict resolved: N/A — First review

Summary: The auction mechanic is adjudicated sound by the creative director (Social Gravity governs party distribution; Earned Power gets the party to the table). However, 15 blocking items span data model correctness (PartyID=0 key collision pools all solo players into the same damageRecord key), cross-GDD contract mismatches (MoveItemIn vs PickupRequest interface conflict), networking architecture gaps (zero wire schemas for all 6 loot messages, PRNG seed unspecified), runtime crash paths (division by zero at N=0 in F-LT-1, TrySpendGold failure path undefined), platform-incompatible design (bag-full TTL loss without mobile agency — confirmed blocker by creative director), and performance risks (O(G×P) proximity check, unbounded auction bid list). Root cause: the upstream Party System GDD does not exist; 6+ blockers cannot be resolved until that interface contract is authored. The Inventory System API mismatch (MoveItemIn vs PickupRequest) is the second critical upstream primitive to resolve. Re-reviewing without extracting these missing contracts will reproduce these findings.

### Top 5 Priority Blockers for Next Pass

1. PartyID=0 key collision in damageRecord — all solo kills broken at schema level
2. Inventory API mismatch — MoveItemIn(characterID, itemID) vs PickupRequest(ItemID, quantity)
3. Auctioning → Despawned state transition missing from state machine table
4. Division by zero at N=0 in F-LT-1 — no guard in formula definition
5. Zone teardown idempotency — "assume success" for Claiming items creates duplication/loss on crash

### Creative Director Adjudications

- **Auction mechanic**: KEEP. Social Gravity pillar owns party loot distribution. Player Fantasy section needs explicit pillar-handoff language (Earned Power → Social Gravity at rare drop moment).
- **Permanent item loss on bag full**: BLOCKER. iOS/Android OS suspension is platform behavior, not player error. Recommended redesign: TTL pauses while app is backgrounded; explicit discard modal on first pickup attempt; proactive "item on ground" notification on zone entry.

### Next Session Instructions

1. Do NOT re-review. Extract upstream primitives first.
2. Write Party System GDD minimum viable interface (PartyID, N>0 invariant, solo representation).
3. Reconcile Inventory System API — pick one signature between MoveItemIn and PickupRequest; update both GDDs.
4. Set PICKUP_RADIUS_UNITS provisional value with UX rationale; register in entities.yaml.
5. Redesign bag-full fate per creative director adjudication.
6. Run `/design-review --depth lean` only after all four above are done.
