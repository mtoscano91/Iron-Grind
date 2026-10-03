# Review Log: Loot Table System

---

## Review — 2026-10-03 — Verdict: APPROVED (lean re-review #3 of the winner-grace amendment)
Scope signal: L (system, unchanged); amendment S
Specialists: lean — single-session analysis (no specialist agents); run in a fresh session after `/clear`
Blocking items: 0 | Recommended: 6
Summary: Both blockers of lean re-review #2 are closed — CR-LT-9.1 names the party member status as the source of "disconnected", checks it only when the bid is tried and lets a running grace continue; `GroundItemAssigned` carries `uint expiryTick` (12-byte body) in the wire protocol, the channel contract, CR-LT-12, AC-LT-25 and Story 013. R1–R5 of that review are applied and consistent. No primitive gaps. Nothing new blocks Story 013.
Prior verdict resolved: Yes — both blockers of lean re-review #2 closed.

### Recommended (for a later authoring pass)
- R1: `Disconnected` is not a status value. The wire enum `PartyMemberStatus` is `Online, Ghost, OutOfZone, Dead`; party-chat.md AC-PC-3b confirms a member is `Online` inside the 5-second window and `Ghost` after it. Party System CR-PS-7 / CR-PS-9 use "Disconnected" as prose only. CR-LT-9.1, the disconnect edge case, the Interactions row and two AC-LT-25 clauses should say `Ghost` only. This corrects the note in the amendment entry below that party-system.md's `MemberStatus` comment "omits `Disconnected`" — it does not.
- R2: a party disband during an open auction or a grace has no edge case — every bidder has left the party, all bids are skipped, and CR-LT-10 runs round-robin on a party that no longer exists. Predates the grace amendment (leaver-bid rule). Story 011 code not checked for this case.
- R3: `GroundItemAssigned` recipients differ — wire protocol "assigned character", channel contract S→PARTY, Loot Interactions row "zone clients". Pre-existing.
- R4: CR-LT-12 gives the paid-winner invariant path a fresh window read from `GroundItemAssigned`, but the wire note says the message is sent only for the CR-LT-10 fallback.
- R5 (carried): the HUD row in Dependencies and hud.md CR-HUD-15 drive the notification from `GroundItemAssigned`; common drops only get `GroundItemSpawned`. party-system.md does not list the Loot Table status read.
- R6 (carried, deferred by the user): no party-facing signal during a grace.

### Nice-to-have
F-LT-2 Notes say the auction "always" is a net gold sink for the winner (0 at `N = 1`); AC-LT-25 has no clause for gold spent during a grace (Story 013 QA covers it); an `OutOfZone` bidder in a grace has no ground item record, so the client rule picks pickup mode (`OutOfZone` is provisional); carried: CR-LT-13.3 and `Auctioning` items, `HasFreeSlot()` has no character parameter, costless shill bid, a cancelled grace modal cannot be reopened.

Next: `/story-readiness` then `/dev-story` for Story 013, then Story 012.

---

## Amendment — 2026-10-03 — Revision after lean re-review #2 — not yet re-reviewed
Scope signal: S
Specialists: None (revision applied in the review session; user decisions taken in-session)
Addresses: blockers 1–2 and R1–R5 of the review entry below. R6 (party-facing grace signal) stays deferred.
Decisions (user, 2026-10-03): (B1) "disconnected" = the bidder's party member status is `Disconnected` or `Ghost`; it is checked only when a full-bag bid is tried; a disconnect during a running grace does not end it. This withdraws the assistant's mid-grace disconnect rule and Story 013's `NotifyClientDisconnected` push. (B2) resolved from existing docs, no decision needed: `GroundItemAssigned` gains `uint expiryTick` — hud.md CR-HUD-15 already reads it from that message.
Changes — `loot-table-system.md`: CR-LT-9.1 (status source, bid-time-only check); CR-LT-12 (paid-winner invariant path gets the fresh window; client reads `GroundItemAssigned.expiryTick`); CR-LT-15 and both Networking Core rows (+`BagFullPickupBlocked`, `GroundItemExpiryWarning`); `Claiming` row; Interactions and Dependencies (Party member `Status` read); F-LT-2 (`N` range [1, 4], net cost 0 at `N = 1`); disconnect and leaver edge cases; Auction Bid Window trigger (`GroundItemSpawned` with `isAuction = true`, was `AuctionOpen`); AC-LT-25 (disconnected clauses, `GroundItemAssigned` carries `expiryTick`); header.
Changes — other files: `networking-wire-protocol.md` (`GroundItemAssigned` body 8 → 12 bytes, 22 standalone); `networking-channel-contract.md` (`GroundItemAssigned` note); `inventory-system.md` and `currency-system.md` (Last Updated notes); `entities.yaml` (grace knob note); Story 013 (party status query instead of a connection push; `ExpiryTick` on `GroundItemAssignedEventArgs`; disconnected AC and QA case).
Added by the assistant, not confirmed by the user: the name and shape of the party query in Story 013 (`IsMemberConnected`).
Not changed, for a later pass: party-system.md's `MemberStatus` comment (line 111) lists `Online, Dead, Ghost, OutOfZone` and omits `Disconnected`, which CR-PS-7/CR-PS-9 use; party-system.md does not list the Loot Table status read; hud.md CR-HUD-15 shows the notification on `GroundItemAssigned`, but common drops are announced by `GroundItemSpawned` only.
Next: `/clear`, then `/design-review design/gdd/loot-table-system.md --depth lean`.

---

## Review — 2026-10-03 — Verdict: NEEDS REVISION (lean re-review #2 of the winner-grace amendment)
Scope signal: L (system, unchanged); revision S
Specialists: lean — single-session analysis (no specialist agents); run in the same session as the authoring pass
Blocking items: 2 | Recommended: 6
Summary: The three prior blockers are closed (balance read before a grace; `N` on the paying tick everywhere; the client rule is implementable because `GroundItemSpawned.isAuction` gives the client the item state). R2–R8 are applied and consistent. Two new blockers, both from rules added on 2026-10-03.
Prior verdict resolved: Yes — all 3 blockers of the first 2026-10-03 review closed.

### Blocking
1. **"Disconnected" is not defined.** CR-LT-9.1 names no source for the state. party-system.md has `Ghost` / `Disconnected` statuses and a 5-second reconnect window that suppresses `Ghost` (its `MemberStatus` comment omits `Disconnected`). As written a short network blip during a grace costs the bidder the item; the mid-grace rule is an unconfirmed assistant addition; Story 013 invented a `NotifyClientDisconnected` push to cover the gap. Proposed: define it as the party member status, check only when the bid is tried, let a running grace continue.
2. **The client cannot learn the fresh pickup window.** CR-LT-12 resets `expiryTick` on a fallback, but `GroundItemAssigned` carries only `groundItemId` and `assignedTo`; the HUD pie (F-HUD-3) and the modal countdown would use the stale spawn-time value. Dates from the Story 011 amendment (missed by the previous review); the any-grace rule makes it common. Proposed: add `uint expiryTick` to `GroundItemAssigned`.

### Recommended
- R1: `N = 1` on the paying tick (party shrinks to the winner during a grace) — net cost 0; F-LT-2 still gives [2, 4]. State the outcome.
- R2: the `Claiming` → `Assigned` invariant-violation path for a paid winner gets no fresh TTL — after a grace past `expiryTick` the item despawns at once.
- R3: the Auction Bid Window is "triggered by an `AuctionOpen` event" — no such message; it is `GroundItemSpawned` with `isAuction = true`.
- R4: `inventory-system.md` and `currency-system.md` Last Updated lines do not record today's interface-row additions.
- R5: CR-LT-15 and the Networking Core rows omit `BagFullPickupBlocked` and `GroundItemExpiryWarning`.
- R6 (carried, deferred by the user): no party-facing signal during a grace.

### Nice-to-have
`HasFreeSlot(): bool` has no character parameter in the Inventory GDD; CR-LT-13.3 should say an `Auctioning` item sends no expiry warning; carried: costless shill bid, a cancelled grace modal cannot be reopened.

---

## Amendment — 2026-10-03 — Winner-grace revision after the lean re-review — not yet re-reviewed
Scope signal: S–M
Specialists: None (authoring session; user decisions taken in-session)
Addresses: blockers 1–3 and R2–R8 of the review entry below. R1 deferred.
Decisions (user, 2026-10-03): (B1) read `GetBalance` before granting a grace — balance below the bid → disqualified at once, no grace; gold spent during the grace stays a residual case (disqualified at `TrySpendGold`, the discarded item is not restored). (B2) `N` for the pool split is the party size on the paying tick. (B3) no wire change — the client picks the discard modal mode from the ground item's state (`Auctioning` → grace mode). (R2) a disconnected bidder with a full bag gets no grace. (R8) a round-robin fallback that follows any grace gets a fresh TTL. (R1) deferred to the auction UI work. Confirmed: safe range [200, 1200]; leaver during a grace disqualified at once; CR-LT-13.1 does not extend a grace; no new item state.
Changes — `loot-table-system.md`: header status; CR-LT-9 (`N` wording); CR-LT-9.1 (balance read, disconnected rule, spend failure after a grace, single tick wording); CR-LT-12 (any-grace fresh TTL); state table (`Auctioning` grace → `Claiming` exit, `Claiming` fail for an auction item is an invariant violation, `Despawned` entry); Interactions (+`HasFreeSlot`, `InventoryChangedEvent`, `GetBalance`; "any non-`Success`"); F-LT-2 `N`; disconnect and leaver edge cases; Dependencies (Currency, Inventory, HUD and Character Persistence statuses, currency bidirectionality note); discard modal client rule in both sections; AC-LT-25 (+ cannot afford, disconnected, leaver, pool of 3, fresh TTL); AC footer 20/5/25; QA note.
Changes — other files: `networking-channel-contract.md` (`BagFullPickupBlocked` note corrected); `networking-wire-protocol.md` (`BagFullPickupBlocked` client rule; `AuctionResolved` send-time note); `inventory-system.md` (Loot Table as a `HasFreeSlot` / `InventoryChangedEvent` consumer); `currency-system.md` (Loot Table `GetBalance` read; Downstream Dependents status); `entities.yaml` (`AUCTION_WINNER_GRACE_TICKS` note); Story 013 and `EPIC.md` TR-loot-012.
Added by the assistant, not confirmed by the user: a bidder who disconnects *during* a grace is disqualified on the next tick (extension of R2); Story 013's `NotifyClientDisconnected` / `NotifyClientConnected` entries on `ILootAuctionService`.
Still open: R1 (party-facing grace signal — wire change); nice-to-haves (costless shill bid; a grace bidder who cancels the modal cannot reopen it).
Next: `/clear`, then `/design-review design/gdd/loot-table-system.md --depth lean`.

---

## Review — 2026-10-03 — Verdict: NEEDS REVISION (lean re-review of the two 2026-10-03 amendments)
Scope signal: L (system, unchanged); amendment itself S–M
Specialists: lean — single-session analysis (no specialist agents)
Blocking items: 3 | Recommended: 8
Summary: The Story 011 amendment (non-`Success` disqualification, leaver bids skipped, fresh TTL on fallback) is consistent — no findings. The CR-LT-9.1 winner-grace amendment has three blockers. No primitive gaps: `HasFreeSlot`, `InventoryChangedEvent`, `GetBalance` and the `BagFullPickupBlocked` schema all exist.
Prior verdict resolved: N/A — prior verdict was APPROVED (2026-05-17); this pass covers the amendments below.

### Blocking
1. **Grace granted before affordability is known.** CR-LT-9.1 checks the free slot before `TrySpendGold`, so a full-bag bidder who cannot afford their bid is shown the discard modal, destroys an item, and is then disqualified with `InsufficientFunds`. Proposed: read `GetBalance` before granting a grace; below the bid → disqualify at once, no grace. Gold spent during the grace remains a residual case. **Needs a user decision.** Story 013 (Ready) would implement the flaw as written.
2. **`N` for the pool split defined two ways.** CR-LT-9.1: party size on the paying tick. Edge Cases leaver line and F-LT-2 variable table: party size at `windowCloseTick` / auction close. The paying-tick rule is an unconfirmed assistant addition — **needs user confirmation**, then propagation to both places.
3. **`BagFullPickupBlocked` has two meanings with no written client rule.** Visual/Audio "Discard Modal" still says the modal is dismissed on exit from `PICKUP_RADIUS_UNITS` (a grace bidder is usually outside it); the UI section says "wherever they stand" but not how the client tells the cases apart (schema unchanged). `networking-channel-contract.md` line 71 says the message is "emitted only for explicit pickup attempts, not automatic loot pickup failures" — contradicts CR-LT-13.2 (pre-existing) and the grace (propagation miss).

### Recommended
- R1: the party gets no feedback during a grace — `AuctionResolved` is sent once at the end, so the bid window sits disabled for up to 4 × 30 s; a fix needs a party-facing signal (wire change).
- R2: disconnected bidder with a full bag — the disconnect edge case was not updated; as written a 30 s grace runs that nobody can act on. Decide skip-at-once or let it run.
- R3: state table — `Despawned` entry "`expiryTick` in any non-terminal state" conflicts with no-despawn during a grace; `Auctioning` row lacks the grace → `Claiming` exit; `Claiming → Assigned` on `PickupResult.Fail` for an auction item contradicts "a winner never holds an undelivered auction item" (state it as an invariant violation, TD-051).
- R4: tick wording differs between CR-LT-9.1 ("first tick where the inventory has changed"), AC-LT-25 ("first tick after") and Story 013 ("next tick").
- R5: Interactions table — add the Inventory `HasFreeSlot` read and `InventoryChangedEvent` subscription; the `TrySpendGold` row still says "after an `InsufficientFunds`" (now any non-`Success`); `inventory-system.md` lists only the Equipment System as a `HasFreeSlot` caller.
- R6: stale bookkeeping — AC footer 19/5/24 (now 20/5/25); QA note omits AC-LT-25; HUD and Character Persistence listed as "Not yet designed" (both GDDs exist); the currency bidirectionality note is already done; `currency-system.md` Downstream Dependents lists Loot Table as "Not Started".
- R7: AC-LT-25 has no clause for a bidder leaving the party during a grace, nor for the affordability case once blocker 1 is decided.
- R8: after two graces the round-robin fallback lands ~tick 1,800 of 2,400 — the assignee gets 30 s and the expiry warning fires at once; consider a fresh TTL for any fallback that follows a grace.

### Unconfirmed assistant additions — reviewer's view
Safe range [200, 1200]: fine. Leaver during a grace disqualified at once: fine (consistent with CR-LT-9). `N` on the paying tick: sensible, see blocker 2. CR-LT-13.1 does not extend a grace: fine (no gold at risk, party is waiting) — but it departs from the 2026-05-16 "backgrounding is not player error" stance for this one case. No new state: fine once R3 is fixed. All five still need the user's confirmation.

### Nice-to-have
Costless shill bid (a full-bag bidder bids high, lets the grace expire, an ally's low bid wins — already possible by overbidding one's balance; raise at `/review-all-gdds`). A grace bidder who cancels the modal cannot reopen it from the HUD indicator (it tracks `Assigned` items only).

Next: authoring session to apply blockers 1–3 (decisions on 1 and 2 first), then `/design-review design/gdd/loot-table-system.md --depth lean`. Story 013 should not start until blocker 1 is decided.

---

## Amendment — 2026-10-03 — Winner grace on bag full (CR-LT-9.1) — not yet re-reviewed
Scope signal: M
Specialists: None (user decision taken at `/story-readiness` for Story 012)
Changes: (1) New CR-LT-9.1: a bidder whose bag is full when their bid is tried is not charged; the item is reserved for them for `AUCTION_WINNER_GRACE_TICKS`; freeing a slot runs the normal CR-LT-9 sequence without proximity; on expiry the next bid is tried with its own grace; all disqualified → CR-LT-10. (2) CR-LT-9 last sentence now points to CR-LT-9.1 (was "CR-LT-13 applies — the gold pool is still distributed"). (3) CR-LT-12: the fresh TTL no longer names a bag-full winner (that case no longer exists); it covers a fallback reached at or after `expiryTick`. (4) `Auctioning` state row. (5) Teardown edge case and AC-LT-19: no grace at teardown; a full-bag bidder is skipped; nobody can receive → despawn, no gold. (6) AC-LT-12 bag-full clause → new AC-LT-25. (7) Tuning knob `AUCTION_WINNER_GRACE_TICKS` (also in `entities.yaml`). (8) Discard modal shown during a grace. (9) networking-wire-protocol.md: `BagFullPickupBlocked` is also sent for a grace; no schema change.
Decisions (user, 2026-10-03): the grace applies to every auction close; length 600 ticks; no grace at teardown; the bid is debited only when delivery is possible (no refund path).
Added by the assistant, not confirmed by the user: the safe range [200, 1200]; a bidder who leaves the party during their grace is disqualified at once; `N` for the pool split is the party size on the tick the winner pays; CR-LT-13.1 (TTL pause on background) does not extend a grace; the item stays in `Auctioning` (no new state).
Supersedes: the "fresh TTL for a bag-full winner" extension recorded in the amendment below (confirmed by the user earlier the same day, now moot). Code impact: Story 011's bag-full-winner behaviour is replaced by new Story 013; most of TD-051 goes away.
Next: `/design-review design/gdd/loot-table-system.md --depth lean` in a separate session (covers this amendment and the one below).

---

## Amendment — 2026-10-03 — Story 011 readiness decisions + `AuctionBid` reason — not yet re-reviewed
Scope signal: S
Specialists: None (user decisions taken at `/story-readiness` for Story 011)
Changes: (1) CR-LT-9, the Currency rows of the Interactions and Dependencies tables and AC-LT-13 name `GoldTransactionReason.AuctionBid` for the winner's `TrySpendGold` (Currency amendment, re-reviewed and Approved in the Currency log). (2) CR-LT-9: any non-`Success` `TrySpendGold` result (`CharacterNotFound`, `ConcurrencyConflict`) disqualifies the bidder and logs a server error. (3) CR-LT-9 and the Edge Cases leaver line: a bid placed by a character who has left the party is skipped at resolution. (4) CR-LT-12: an item that becomes `Assigned` out of an auction closed at `expiryTick` gets `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS`.
Decisions (user): (2), (3), and the fresh pickup window for the round-robin fallback in (4). The extension of (4) to a winner whose bag is full was added by the assistant on the same reasoning and is not yet confirmed by the user.
No new ACs were added for (2)–(4); Story 011 carries test cases for them. No registry change (no new constant or entity).
Next: `/design-review design/gdd/loot-table-system.md --depth lean` in a separate session.

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
