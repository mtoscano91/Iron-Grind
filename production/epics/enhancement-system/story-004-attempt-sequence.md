# Story 004: Attempt Sequence and Outcome Resolution

> **Epic**: Enhancement System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-6 (irrevocable once confirmed), CR-ENH-7 (slot lock duration), CR-ENH-9, CR-ENH-10 (two outcomes), CR-ENH-15 steps 3–7 (lock, consume scroll, draw, apply, unlock), CR-ENH-18 (`IsAttemptInProgress`), States and Transitions, AC-ENH-9, 10, 11, 12, 33, 36.
**Requirement**: `TR-enh-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Tier 1 calls into the injected `IInventoryService`, acting on return values).
**ADR Decision Summary**: The Enhancement System mutates the bag only through `IInventoryService` and never caches slot state; inventory changes reach other systems through the Inventory System's own event.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable against the real `InventoryService`.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency; `readonly struct` results — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. In this story the commit step always succeeds (a fake); Story 005 covers its failure.*

- [ ] **AC-ENH-9 (success)**: Bronze item at +2, one Bronze scroll (a stack of 1), draw `r = 0.00` → outcome `SUCCESS`, the item's slot is at level 3, the scroll is gone, the slot is unlocked.
- [ ] **AC-ENH-10 (destruction, low level)**: same setup, `r = 0.90` (above `P_s[2] = 0.85`) → outcome `DESTRUCTION`, the item slot is empty, the scroll is gone, the slot is unlocked.
- [ ] **AC-ENH-11 (destruction)**: Bronze item at +4, `r = 0.99` (above `P_s[4] = 0.65`) → `DESTRUCTION`, item slot empty, scroll gone, slot unlocked.
- [ ] **AC-ENH-12 (destruction at +0)**: Bronze item at +0, `r = 0.96` (above `P_s[0] = 0.95`) → `DESTRUCTION`, item slot empty, scroll gone. Only two outcomes exist at every level.
- [ ] **AC-ENH-33 (one scroll from a stack)**: item at +2 in slot 0, 5 Bronze scrolls in slot 3. With `r = 0.00` and with `r = 0.90`, slot 3 holds 4 scrolls afterwards.
- [ ] **AC-ENH-36 (scroll gone at step 4)**: validation passes but `ConsumeItem(scrollItemID, 1)` fails → `RejectedScrollNotFound`; the item slot is unlocked and still at its previous level; the random source is never drawn; the commit is never called; `IsAttemptInProgress` is false; a following valid `ConfirmEnhancement` is accepted.
- [ ] **Step order**: on a valid request the calls happen in this order — `LockSlot(itemSlotIndex)` → `ConsumeItem(scrollItemID, 1)` → one draw → `SetEnhancementLevel(itemSlotIndex, level + 1)` on success or `RemoveItem(itemSlotIndex)` on destruction → commit → `UnlockSlot(itemSlotIndex)`.
- [ ] **Exactly one draw per attempt**, and none for a rejected request.
- [ ] **`IsAttemptInProgress(CharacterID)`** is false before an attempt and false after it completes; it is true from the lock until the attempt returns to idle (observable from inside the commit fake).
- [ ] **Result**: the call returns the outcome, the new level (0 on destruction) and the result code (`Success` / `Destruction`).

---

## Implementation Notes

- Extends `EnhancementService.ConfirmEnhancement(CharacterID, int itemSlotIndex, int scrollSlotIndex)` from Story 003. Return a `readonly struct` result `{ Outcome, NewLevel, ResultCode }` (UI-ENH-2 field set).
- **Random source is injected** — a `System.Random` passed to the constructor, as the Loot Table service does (`LootRandomFactory` precedent). Draw with `NextDouble()` (`[0, 1)`) and resolve with Story 001's helper. Tests inject a deterministic source that returns scripted values and counts draws.
- **Scroll**: use `ConsumeItem(charId, scrollItemID, 1)` where `scrollItemID` is the item validated at `scrollSlotIndex`. Never `RemoveItem` on the scroll slot — scrolls are stackable. `ConsumeItem` takes the unit from the lowest-index unlocked stack of that scroll, which may not be `scrollSlotIndex`.
- **Step 4 failure**: call `UnlockSlot(itemSlotIndex)`, return `RejectedScrollNotFound`, draw nothing.
- **Apply (step 6a)**: success → `SetEnhancementLevel(itemSlotIndex, currentLevel + 1)` — the slot is still locked, which that call requires. Destruction → `RemoveItem(itemSlotIndex)`, which clears the slot and its lock.
- **Unlock (step 7)**: `UnlockSlot(itemSlotIndex)` after the commit. After a destruction the lock is already gone; check `IInventoryService.UnlockSlot`'s behaviour on an unlocked or empty slot and avoid a logged warning on the normal destruction path.
- **Commit seam**: declare the consumer-side persistence interface now (Story 005 specifies its shape and failure handling). In this story the sequence calls it once between apply and unlock and the tests use a fake that reports success.
- **`IsAttemptInProgress`**: a per-character flag owned by this service — set at step 3, cleared when the attempt returns to idle on every exit path (success, destruction, step 4 failure). Use `try`/`finally` so an exception cannot leave it set.
- States (`IDLE`, `VALIDATING`, `LOCKED`, `RESOLVING`, `RESULT_*`) describe the sequence; a public state enum is not required by any criterion — do not add one unless a test needs it.
- If `SetEnhancementLevel` returns `false` or another inventory call fails unexpectedly after the scroll was consumed, the GDD gives no rule (it treats these as unreachable). Raise at `/story-readiness`.

---

## Out of Scope

- Story 005: a commit that fails or is still in flight; rollback; `RejectedConcurrentAttempt` for a second request
- Story 007: outcome events and the +9 broadcast trigger
- Story 009: holding other inventory requests during an attempt (OQ-ENH-7)
- Story 010: `ConfirmEnhancement` / `CancelEnhancement` as wire requests; sending `EnhancementAttemptResult`

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptSequence_integration_tests.cs` (new). Real `InventoryService`; scripted random source; commit fake reporting success; NPC session stub active.

- **AC-ENH-9** — as stated; also `IsSlotLocked(itemSlot)` false afterwards and the result is `{SUCCESS, 3, Success}`.
- **AC-ENH-10** — as stated; result `{DESTRUCTION, 0, Destruction}`; `GetSlot(itemSlot)` is empty at level 0.
- **AC-ENH-11** — as stated.
- **AC-ENH-12** — as stated.
- **Boundary draw** — item at +2: `r = 0.849` → success; `r = 0.85` → destruction.
- **AC-ENH-33** — both runs leave 4 scrolls in slot 3; success run: slot 0 at level 3; destruction run: slot 0 empty.
- **Scroll in a lower slot** — scrolls in slots 2 and 5, request names slot 5 → one unit leaves slot 2 (lowest index), slot 5 unchanged; the attempt still resolves.
- **AC-ENH-36** — inventory double whose `ConsumeItem` fails: `RejectedScrollNotFound`; slot unlocked; level unchanged; draw count 0; commit count 0; `IsAttemptInProgress` false; a second valid call (with the double restored) is accepted.
- **Step order** — a recording inventory wrapper and commit fake log the sequence; assert the order in the criterion for a success and for a destruction.
- **One draw** — draw count is 1 after a resolved attempt and 0 after each Story 003 rejection.
- **In-progress flag** — read from inside the commit fake: true; after the call returns: false.
- **Level 9 → 10** — item at +9, `r = 0.00` → level 10; a further attempt → `RejectedAtMaxLevel`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptSequence_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (probability table, resolution), Story 003 (validation); Inventory System Story 010 (Complete)
- Unlocks: Story 005, Story 007
