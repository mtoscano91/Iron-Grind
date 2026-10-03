# Review Log: Currency System

## Review — 2026-10-03 — Verdict: APPROVED
Scope signal: S
Specialists: None (lean mode — re-review of the `AuctionBid = 9` amendment)
Blocking items: 0 | Recommended: 8
Summary: The amendment is correct and fully propagated — `AuctionBid = 9` is identical in the wire-protocol enum, the registry, this GDD and loot-table-system.md (CR-LT-9, both Currency rows, AC-LT-13); no formula, state or AC changes. Recommended, not blocking: the NPC Shop Interactions row still says Inventory calls `AddGold(ItemSell)` and names `AdminAdjust` for the compensating refund (also EC-CS-5) where everything else uses `CompensatingRefund`; the Overview, the "primary and only" faucet line and the sell-back invariant row predate sell-back and the auction; Rule 6, F-CS-1/F-CS-2 sources and EC-CS-9 do not name Loot Table as a `TrySpendGold` caller; Rule 6's refund wording does not fit the auction's no-refund bag-full path; the Downstream Dependents table still says "Not Started" and OQ-CS-3 is still listed open. Outside this document: CR-LT-9 leaves `ConcurrencyConflict`/`CharacterNotFound` undefined (Story 011 open point), and character-persistence.md cites a non-existent `RespecRefund` (needs a decision).
Prior verdict resolved: Yes — the 2026-10-03 amendment below is now re-reviewed

## Amendment — 2026-10-03 — `GoldTransactionReason.AuctionBid = 9` — not yet re-reviewed
Scope signal: S
Specialists: None (authoring session; user-approved changeset)
Reason: Loot Table Story 011 (Auction Resolution and Gold Pool) was Blocked — CR-LT-9 calls `TrySpendGold(winner, winnerBid)` without a reason and the enum had no auction value.
Changes: currency-system.md — rare-drop auction bid added to the gold sinks; Loot Table rows of the Interactions and Downstream Dependents tables now list the `TrySpendGold` call; the `GoldSyncEvent` `Reason` value list replaced by a pointer to the enum. loot-table-system.md — CR-LT-9, the two Currency rows and AC-LT-13 name the reason. networking-wire-protocol.md — enum block gains `AuctionBid = 9` and the previously missing `CompensatingRefund = 8`. entities.yaml — `AuctionBid=9`.
Decisions (user): one new value, `AuctionBid = 9`; the pool share paid to party members stays `MonsterDrop`.
Left untouched, recorded for later: currency-system.md still names `AdminAdjust` for the NPC Shop's compensating refund (Interactions row, EC-CS-5) where npc-shop.md, ADR-001 and the code use `CompensatingRefund`; character-persistence.md cites `GoldTransactionReason.RespecRefund`, which does not exist.
Next: `/design-review design/gdd/currency-system.md --depth lean` in a separate session; code follow-up (enum value, `WireEnumCodec` range, one round-trip test).

## Review — 2026-04-27 — Verdict: NEEDS REVISION → Approved
Scope signal: M
Specialists: None (lean mode — context budget)
Blocking items: 4 | Recommended: 4
Summary: All ten pass-1 blockers were correctly resolved. Four residual blockers remained: EC-CS-4 still attributed retry logic to the NPC Shop (contradicting Rule 11's internalized retry), the Downstream Dependents table still listed sell-back for NPC Shop (contradicting the Interactions table), EC-CS-5's compensating AddGold was missing its GoldTransactionReason parameter, and AC-CS-F-01 only covered AddGold success rather than all successful balance mutations. All four fixes were surgical wording and parameter changes; no design decisions were required. Currency System is now fully consistent and Approved.
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (2026-04-26), all items addressed

## Review — 2026-04-26 — Verdict: MAJOR REVISION NEEDED → Revised, Pending Re-Review
Scope signal: XL
Specialists: economy-designer, systems-designer, game-designer, qa-lead, network-programmer, creative-director
Blocking items: 10 | Recommended: 8
Summary: GDD was technically sound at the rule/formula level but had critical gaps at the storage/networking contract surface: GoldMutationResult schema was inconsistent across three documents (AC, state machine, error enum), AddGold atomicity and Version-increment semantics were undefined (party-kill lost-update bug guaranteed under read-modify-write), and the reconnect resync contract was missing a session-handshake requirement. Sell-back was cut from MVP as an underconstrained competing faucet. All 10 blockers resolved in-session. GOLD_CAP reframed as a fixed overflow/bot guard rather than a spend-pressure knob. GoldTransactionReason API parameter added to AddGold/TrySpendGold. Groups I and J added to Acceptance Criteria covering reconnect resync and concurrent AddGold atomicity.
Prior verdict resolved: N/A — first review
