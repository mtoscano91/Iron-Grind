# Equipment System — Review Log

---

## Review — 2026-10-01 — Verdict: APPROVED (Pass 7 lean)

Scope signal: XL
Specialists: Lean — single-session analysis, no specialist agent delegation
Blocking items: 0 | Recommended: 6
Prior verdict resolved: Yes — all 5 Pass 6 blockers verified closed (`GetSlot` read in CR-EQS-4/13 and both inventory-system.md interface rows; `GetSlotEnhancementLevel(GearSlot)`; `EquipResult` → wire mapping with `InventoryError = 6`; Dependencies and bidirectionality; OQ-EQS-8 Weapon-slot-only with AC-EQS-30)
Summary: The TD-044/045 amendment is internally consistent and agrees with inventory-system.md, enhancement-system.md, networking-wire-protocol.md and the registry on every contract it touches. Arithmetic re-derived and correct for AC-EQS-2/11/29/30 and the F-EQS-4 table. Two stale registry entries were corrected at approval (`MoveItemOutResult` gains `EnhancementLevel`; `EquipResult` values gain `Success`).
Pre-implementation gate: OQ-EQS-9 — CR-EQS-13's stale-render guard and CR-EQS-5 step 2 name no `EquipResult` code, and EC-EQS-4's "Item is locked" message conflicts with CR-EQS-6 step 7's `InventoryError`. Resolve before `/create-stories`, then add an AC for the stale-render guard.
Recommended, not applied: AC-EQS-25 should assert the saved level is used on load; AC-EQS-8 setup should place the second ItemA in a named inventory slot; EC-EQS-4 note still says Inventory's `MoveItemOut` returns `ItemID`; EC-EQS-10 "concurrent write race" vs CR-EQS-8 single-threaded tick; cross-document drift unchanged since Pass 6 (character-persistence.md `GetFlatBonus` signature, "no save API" row, `CorruptRecord` vs clamp; damage-calculation.md method names; networking-wire-protocol.md `AppearanceChangedEvent` "future Enhancement outcome" sentence).

---

## Review — 2026-10-01 — Verdict: NEEDS REVISION (Pass 6 lean — TD-044/TD-045 amendments)

Scope signal: XL
Specialists: Lean — single-session analysis, no specialist agent delegation
Blocking items: 5 | Recommended: 5
Prior verdict resolved: N/A — prior verdict was APPROVED (Pass 5); this pass reviews the 2026-10-01 amendments (per-slot `EnhancementLevel`, CR-EQS-8 rewrite)
Summary: The amendment's core is sound — the level is carried consistently through CR-EQS-1/5/6/7/8/15 and matches inventory-system.md Rule 8.24a; the CR-EQS-8 abort-in-place rewrite is correct. Five edge gaps blocked implementation: (1) CR-EQS-4's level comparison needed an inventory slot read the declared interface did not grant; (2) no read accessor for a non-weapon slot's level, though ACs, Character Persistence save and Inventory UI need it; (3) `EquipResult.InventoryError` had no wire `EquipFailReason` value and `NoChange` had no stated mapping; (4) Dependencies still said equipment is reconstructible "from ItemIDs alone" and listed two Approved GDDs as "Not yet mapped"; (5) OQ-EQS-8 (PrestigeBand source slot) open with no AC.
Revised in-session (user chose "revise now"): `Inventory.GetSlot` added to CR-EQS-4/13 and to the interface rows here and in inventory-system.md; `GetSlotEnhancementLevel(GearSlot)` added; `EquipResult` → wire mapping table added and `InventoryError = 6` added to the wire enum; Dependencies and Bidirectionality refreshed; OQ-EQS-8 resolved by the user — Weapon slot only (CR-EQS-11, AC-EQS-30; enhancement-system.md CR-ENH-12 note and AC-ENH-15/16/17/32 wording aligned; registry ownership note corrected). Recommended applied: CR-EQS-7 null-item path resets the level; AC-EQS-31 added (step 7 rollback keeps the level); F-EQS-4 collapsed to the F-ENH-3 result; `Equip`/`Unequip` signatures stated; AC-EQS-2 value, AC-EQS-11 setup, AC-EQS-25 tag, AC-EQS-29 note, OQ-EQS-1/5/7 refreshed.
Still open: OQ-EQS-9 (new — which layer produces wire `ItemLocked` / `ItemNotInInventory`; CR-EQS-5 step 2 names no result code); OQ-EQS-5 (F-EQS-3 calibration); cross-document drift not touched this pass — damage-calculation.md method names/entity parameter, character-persistence.md `GetFlatBonus` signature and over-max gear level handling (`CorruptRecord` vs Inventory's clamp).
Next: lean re-review in a fresh session to verify these fixes.

---

## Review — 2026-05-22 — Verdict: APPROVED (Pass 5 lean)

Scope signal: XL
Specialists: Lean — single-session analysis, no specialist agent delegation
Blocking items: 0 | Recommended: 0
Prior verdict resolved: Yes — all Pass 4 fixes verified clean and internally consistent
Summary: All Pass 4 changes confirmed. RejectedMergeError / CriticalRollbackFailed split verified consistent across CR-EQS-14, UI Requirements, AC-EQS-24, AC-EQS-26, and AC-EQS-27. CR-EQS-15 crash recovery correctly describes step-7→9 window as the new-item loss risk. Defense-in-depth RejectedEquipped semantics correctly permit merging when only inventory copies (not the equipped copy) are selected. Zero blockers. Document approved for implementation.

---

## Review — 2026-05-22 — Verdict: NEEDS REVISION (Pass 4 lean)

Scope signal: XL
Specialists: Lean — single-session analysis, no specialist agent delegation
Blocking items: 1 | Recommended: 3
Prior verdict resolved: Yes — Pass 3 blocker (CR-EQS-14 entry point unspecified) confirmed present and correct
Summary: All Pass 3 fixes verified. One blocker found: AC-EQS-24 / CR-EQS-14 had a three-part MergeResult code gap — no result code existed for "ForceInsert(result) fails, rollback succeeds"; CriticalError was asserted in the AC but not specified in the rule for the clean-rollback path; `CriticalRollbackFailed` definition (rollback incomplete) was inconsistent with AC-EQS-24's pass condition (rollback succeeded). Resolved by adding `RejectedMergeError` for clean-rollback path, reserving `CriticalRollbackFailed` for rollback-fails path, updating CR-EQS-14 rollback branch, UI Requirements codes, and AC-EQS-24. AC-EQS-26 added for the `CriticalRollbackFailed` path. AC-EQS-27 added as ADVISORY for `RejectedEquipped` defense-in-depth. CR-EQS-15 crash recovery language rewritten to correctly identify the new item (not the old) as the loss risk in crash-mid-step-7. Document ready for lean re-review in a new session.

---

## Review — 2026-05-22 — Verdict: NEEDS REVISION (Pass 3 lean)

Scope signal: XL
Specialists: Lean — single-session analysis, no specialist agent delegation
Blocking items: 1 | Recommended: 3
Prior verdict resolved: Yes — both Pass 2 blockers confirmed present and correct
Summary: All Pass 2 fixes verified. One new blocker found: CR-EQS-14's merge trigger API entry point was unspecified — `RequestMerge(slotIdx1, slotIdx2, slotIdx3): MergeResult` added (Shape A: UI provides slot indices). Three recommended fixes applied: CR-EQS-11 PrestigeBand Mid range clarified from overlapping ≥ conditions to `[MID, HIGH−1]`; `RequestMerge` and full `MergeResult` codes added to UI Requirements; AC-EQS-21 Action updated and AC-EQS-22–25 added covering merge failure paths and IsTransitioning persistence. Document is structurally sound and ready for a clean lean re-review in a new session.

---

## Review — 2026-05-22 — Verdict: NEEDS REVISION (Pass 2 lean)

Scope signal: XL
Specialists: Lean — single-session analysis, no specialist agent delegation
Blocking items: 2 | Recommended: 4
Prior verdict resolved: Yes — all 16 original blockers from Pass 1 (MAJOR REVISION NEEDED) confirmed addressed
Summary: All 16 first-pass blockers confirmed resolved. Two precision gaps found in recovery paths: (1) CR-EQS-6 step 7 rollback was missing the slot index source — `MoveItemIn` returns `{ success, slotIndex }` in Inventory System but the Equipment System spec didn't direct the programmer to retain `slotIndex` for the step 7 undo; (2) CR-EQS-14 merge rollback left "restore three source items" mechanism unspecified. Both blockers fixed inline. Three recommended fixes also applied: States table Transitioning row ambiguity resolved; networking-wire-protocol.md added to Dependencies; step 7 recovery prose reordered (reclaim item before re-applying modifiers). Document is structurally sound and ready for a clean lean re-review in a new session.

---

## Review — 2026-05-22 — Verdict: MAJOR REVISION NEEDED

Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, economy-designer, network-programmer, performance-analyst, creative-director
Blocking items: 16 | Recommended: 6
Prior verdict resolved: N/A — First review

Summary: The core architecture (equip/unequip-via-stat-modifier, 7-slot structure, stat-gate) is sound. Three pillar-collapse blockers dominate: (1) L1 accessory exploit — no stat gate on Ring/Necklace means a L1 character can equip Dark Steel accessories for 873–1,007% Attack increase, collapsing the Earned Power pillar; (2) missing equip/unequip wire messages — no EquipRequest/EquipResult exists, per-tick appearance broadcast absent, mid-session equips invisible to other players for the rest of the session (Social Gravity broken); (3) prestige constraint oscillation — the F-EQS-4 prestige principle holds only at midpoints, failing at valid boundary combinations where +5 Bronze outperforms +0 Iron by 31%. Two Approved GDDs (Item Database, Inventory System) require additions before this GDD's core mechanics have approved data contracts (EquipRequirementStat/Min fields; ForceInsert/MoveItemOutResult). 5 of 15 Acceptance Criteria are not independently testable; 3 Core Rules have zero AC coverage. CD recommendation: write the 4 upstream contracts (wire messages, Item Database fields, Inventory additions, accessory gate rule) before the next review pass — a full re-review now will rediscover them from six angles.
