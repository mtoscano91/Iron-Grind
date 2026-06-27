# Review Log: Currency System

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
