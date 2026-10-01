# Enhancement System — Review Log

---

## Review — 2026-10-01 — Verdict: APPROVED
Scope signal: L
Specialists: None (lean — no specialist agents)
Blocking items: 0 | Recommended: 4
Summary: Lean re-review Pass 7 of Revision Pass 3. Both Pass 6 blockers verified closed: `RejectedItemEquipped` is gone from the enum and the repo and AC-ENH-4 is runnable (`RejectedItemNotFound`); AC-ENH-7 tests the Inventory lock directly and no longer overlaps AC-ENH-38. The 300s NPC session lifetime matches npc-shop.md CR-SHOP-3. Not creative-director reviewed (lean), and run in the same session as Revision Pass 3 (no fresh context) — every changed passage was re-read from disk and checked against the sibling GDDs. Main-review synthesis: the commit/rollback contract, result-code set and 39-AC suite are internally consistent and agree with Inventory, Equipment, Damage Calculation, Character Persistence and NPC Shop.
Prior verdict resolved: Yes — NEEDS REVISION (Pass 6, 2026-10-01) → both blockers closed; no new blockers.

Recommended items 1–2 applied in-session with approval (wording only): AC-ENH-7 uses the Inventory GDD's `MoveRequest` / `MoveResult(fail, SourceLocked)` (it named a non-existent `MoveItem`); AC-ENH-39 sends `CancelEnhancement` instead of undefined "selection messages".

Still open:
- No client → server selection request is defined although UI-ENH-1 / EC-ENH-5 assume one triggers `EnhancementStateUpdate` (pre-existing; fold into TD-046).
- Client behaviour when the NPC session expires with the Enhancement UI open is unspecified (Enhancement UI GDD).
- Other documents: npc-shop.md OQ-NS-6 and networking-wire-protocol.md line 980 still require a pre-emption callback; "destruction threshold" wording in item-database.md Rule 28 and the systems-index risk table; systems-index "Depends On" omits Character Persistence; `IsAttemptInProgress` not in entities.yaml.

Pre-implementation gates (before `/create-epics`): OQ-ENH-7 (owner of the CR-ENH-18 request hold — AC-ENH-38 cannot run until it exists), Item Database amendment #4 (scroll records, `ScrollData.TargetGearTier`), wire-protocol Enhancement message set (TD-046).

---

## Revision Pass 3 — 2026-10-01
Author: Manuel Toscano + Claude Code
Scope: both blockers of the Pass 6 lean review + the recommended items inside enhancement-system.md. Lean re-review pending.

**Design decisions (user):**
- Blocker 1: `RejectedItemEquipped` dropped. `ConfirmEnhancement` carries bag slot indices only, so an equipped item is unaddressable; CR-ENH-4 says so, the code is removed from `EnhancementResultCode`, AC-ENH-4 now expects `RejectedItemNotFound` for the vacated bag slot.
- Recommended 1: the Enhancement NPC session has the same 300s wall-clock lifetime from open as the shop session (`SESSION_TTL_SECONDS`, npc-shop.md CR-SHOP-3) — one shared flag, one lifetime. CR-ENH-17 close triggers updated; AC-ENH-39 added (39 ACs total).

**Blocker 2:** AC-ENH-7 rewritten to call the Inventory `MoveItem` directly server-side during a held step 6b write; the client-request path (held by CR-ENH-18) stays in AC-ENH-38.

**Recommended items applied:** Player Fantasy no longer refers to a destruction threshold (three phrases); `IsAttemptInProgress` true through `RESULT_*`, and a second `ConfirmEnhancement` is rejected, never held; `outcome` / `newLevel` ignored on a `Rejected*` code; OQ-ENH-7 covers server-originated mutations (Loot Table `PickupRequest`); Equipment row of the Interactions table matches the CR-ENH-12 contract mapping.

**Registry:** no entities.yaml change — no registered entity, formula or constant changed (`RejectedItemEquipped` was never registered; no other file in the repo referenced it).

**Not applied (other documents — still open):** npc-shop.md OQ-NS-6 and networking-wire-protocol.md line 980 still require a pre-emption callback that CR-ENH-17 says is not needed; "destruction threshold" wording in item-database.md Rule 28 and systems-index.md risk table; systems-index "Depends On" for this system omits Character Persistence; `IsAttemptInProgress` not in entities.yaml. Pre-implementation gates unchanged: Item Database amendment #4, wire-protocol Enhancement message set (TD-046), OQ-ENH-7.

---

## Review — 2026-10-01 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: None (lean — no specialist agents)
Blocking items: 2 | Recommended: 6
Summary: Lean re-review Pass 6 of Revision Pass 2. All 5 Pass 5 blockers verified closed: apply-then-commit with caller-owned rollback is sound (slot arithmetic checked for every rollback case, including a quantity-1 scroll stack and a full bag), the `GetElementalBonus` signature agrees across five documents and the registry, and F-ENH-5 re-derives correctly. Not creative-director reviewed (lean); main-review synthesis: the two remaining blockers are AC-level inconsistencies, not structural — no escalation to full depth.
Prior verdict resolved: Yes — NEEDS REVISION (Pass 5, 2026-10-01) → all 5 blockers closed; 2 new blockers found.

Blocking items:
1. `RejectedItemEquipped` is unreachable — `ConfirmEnhancement` addresses bag slots only and the CR-ENH-15 step 2 code list has no check producing it; AC-ENH-4 cannot be run as written. Pre-existing; exposed by the explicit code list added in Revision Pass 2.
2. AC-ENH-7 expects a `MoveItem` during the attempt to be rejected, but CR-ENH-18 (new in Revision Pass 2) holds client requests until `IDLE`, after which the move succeeds on a success outcome.

Primitive gaps (pre-implementation gates, not counted as blockers): OQ-ENH-7 / CR-ENH-18 has no enforcement owner and no reference outside this GDD, and must cover server-originated mutations; Item Database amendment #4 unapplied; wire-protocol Enhancement message set differs (TD-046).

Recommended: NPC session 300s wall-clock lifetime disagrees with npc-shop.md on a shared flag; OQ-NS-6 not propagated to npc-shop.md / wire-protocol; stale "destruction threshold" wording (Player Fantasy, systems-index, item-database Rule 28); systems-index "Depends On" omits Character Persistence; `outcome` undefined on rejection; `IsAttemptInProgress` state coverage vs the CR-ENH-18 window.

---

## Revision Pass 2 — 2026-10-01
Author: Manuel Toscano + Claude Code
Scope: all 5 blockers of the Pass 5 lean review + the recommended items inside enhancement-system.md. Lean re-review pending.

**Design decisions (user):**
- Blocker 1: the outcome is applied to the bag before the commit. CR-ENH-15 step 6 is now 6a (`SetEnhancementLevel` on success / `RemoveItem` on destruction) then 6b (`SaveIrreversibleOutcome(EnhancementResult)`), so the saved record contains the outcome. On a failed commit the caller-owned rollback puts a destroyed item back with the existing `ForceInsert(itemID, previousLevel)` (it may land in a different free slot), reverts a level write with `SetEnhancementLevel`, and restores the scroll with `PickupRequest(CharacterID, scrollItemID, 1)`. No new Inventory method.
- Blocker 2: `GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)` returns the full clamped F-ENH-2 value; Damage Calculation passes the Item Database base.
- A client disconnect never aborts an attempt once step 3 has run (CR-ENH-11, EC-ENH-2 rewritten); rollback happens only on a failed commit; a server crash before the commit leaves the pre-attempt record.
- New CR-ENH-18 (attempt exclusivity): other inventory-mutating requests for the character are held while an attempt is in progress, so the rollback cannot fail for lack of space. `IsAttemptInProgress(CharacterID)` exposed. Enforcement layer = OQ-ENH-7 (pre-implementation gate).

**Blockers 3–5:** non-destructive-failure clause removed from step 6; `RejectedScrollNotFound` named for step 2 / step 4 and `LOCKED → IDLE`, `RESOLVING → IDLE` transitions added; AC-ENH-33 (stack N → N−1), AC-ENH-34 (success-path rollback), AC-ENH-35 (committed record contains the outcome), AC-ENH-36 (step 4 failure), AC-ENH-37 (`RejectedNotUpgradeable`), AC-ENH-38 (requests held during an in-flight commit) added; AC-ENH-23 rewritten; AC-ENH-9/10/11 setups state a single scroll; AC-ENH-20/21 use the new signature (38 ACs total).

**Recommended items applied:** rollback calls declared in the Inventory interface rows (both GDDs); pre-commit `InventoryChangedEvent` rule (CR-ENH-11); EC-ENH-6 names the CR-CP-5 disconnect; OQ-ENH-3 and OQ-ENH-6 resolved, lock lifetime stated in CR-ENH-7 (answers inventory OQ-INV-4); CR-ENH-17 gains `RejectedNotInTownHub`, session-end and pre-emption close triggers and the in-flight rule; step 2 lists the rejection code per check, `RejectedNotUpgradeable` added; Dependencies refreshed (Equipment, Damage Calculation, Character Persistence up- and downstream, NPC Shop); AC-ENH-15/16/17/32 read the character's flags byte; header status corrected; OQ-ENH-8 added (result replay, from character-persistence OQ-CP-2).

**Propagated to:** inventory-system.md (interface rows, OQ-INV-4), damage-calculation.md (Step 4, interface rows, OQ-DC-1 note), character-persistence.md (CR-CP-5 rollback sentence, `IEnhancementBonusProvider` signatures), item-database.md (Rule 19), entities.yaml (`IEnhancementBonusProvider`, F-ENH-2).

**Still open (pre-implementation gates, unchanged):** Item Database amendment #4 (scroll records, `ScrollData.TargetGearTier`); wire-protocol Enhancement message set (TD-046); npc-shop.md OQ-NS-6 (now answerable from CR-ENH-17 — not edited there); OQ-ENH-7.

---

## Review — 2026-10-01 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: None (lean — no specialist agents)
Blocking items: 5 | Recommended: 9
Summary: Lean re-review Pass 5 of the 2026-10-01 TD-043/TD-045 amendments (CR-ENH-12 contract mapping, CR-ENH-15 steps 4/6, transaction boundary, Interactions/Dependencies rows, AC-ENH-15/16/17/32). The Inventory and Equipment contracts agree in both directions and all formula arithmetic re-derives correctly (F-ENH-3, F-ENH-5). Not creative-director reviewed (lean); main-review synthesis: remaining issues are contract fixes, not structural — no escalation to full depth.
Prior verdict resolved: N/A — prior verdict was APPROVED (Pass 4, 2026-05-23); this pass reviews the 2026-10-01 amendments.

Blocking items:
1. Destruction is not in the committed record — the transaction-boundary paragraph issues `RemoveItem` only after the commit, but `SaveIrreversibleOutcome(CharacterID, trigger)` has no payload and saves the live bag (`Inventory.ExportSnapshot`), so the commit persists the item as present with the scroll gone; step 6 and the boundary paragraph also disagree on apply-at-commit vs apply-after-commit. Needs a design decision (remove-before-commit + an Inventory restore call, or an explicit outcome passed to Character Persistence).
2. `IEnhancementBonusProvider.GetElementalBonus(level, gearTier, isWeapon)` has no base parameter, so it cannot return F-ENH-2's value; AC-ENH-20 (expects 50) is unsatisfiable (max 35), and damage-calculation.md treats the return as base + enhancement while no longer reading the base — an elemental weapon at +0 would deal 0 elemental damage. Pre-existing (missed in Passes 2–4). Propagates to damage-calculation.md, character-persistence.md, item-database.md Rule 19, entities.yaml.
3. CR-ENH-15 step 6 "on a non-destructive failure the item slot is not written" contradicts CR-ENH-9/10 (no such outcome).
4. CR-ENH-15 step 4 failure returns an unnamed rejection code absent from `EnhancementResultCode`; state table has no `LOCKED → IDLE` transition.
5. No AC for the TD-043 behaviour (stack of N scrolls → N−1) and none for success-path rollback (level reverted, scroll restored); AC-ENH-9/10/11 would pass under the old whole-stack `RemoveItem` bug.

Primitive gaps (pre-implementation gates, not counted as blockers): Item Database amendment #4 unapplied (no scroll records, no `ScrollData.TargetGearTier`, no scroll `StackLimit`; item-database.md still asserts 34 records); wire-protocol message set differs from this GDD's (`EnhancementAttemptRequest`/`EnhancementRequestReceived`/`EnhancementOutcomeBroadcast` vs `ConfirmEnhancement`/`EnhancementAttemptResult`/`EnhancementStateUpdate`/`CancelEnhancement`/`ServerBroadcast_Enhancement9`); npc-shop OQ-NS-6 pre-emption callback undefined here.

Recommended (not applied — review session): declare the rollback `PickupRequest` in both interface rows (signature takes `CharacterID`) and state the no-interleaving guarantee; `InventoryChangedEvent` from `SetEnhancementLevel` fires before commit (CR-ENH-11); EC-ENH-6/AC-ENH-23 omit the CR-CP-5 disconnect and `SaveIrreversibleOutcome(EnhancementResult)` (CR-CP-5 itself still says `GearSlot.EnhancementLevel`); OQ-ENH-3 stale and Inventory's lock-lifetime question unanswered; CR-ENH-17 omits `SESSION_TTL` / pre-emption close triggers and `RejectedNotInTownHub`; stale Dependencies rows (Equipment "on success", Damage Calc and F-ENH-2 old `GetElementalBonus(level)` signature, Character Persistence "Not Started" + OQ-ENH-6, NPC Shop missing); AC-ENH-15/16/17/32 read the flags from `EquipmentSlotRecord` (byte is per character); no result code for `IsUpgradeable = false`; header Status "In Design" vs index.

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
