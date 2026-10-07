# Story 004: Attempt Sequence and Outcome Resolution

> **Epic**: Enhancement System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-6 (irrevocable once confirmed), CR-ENH-7 (slot lock duration), CR-ENH-8 (concurrent attempt prevention), CR-ENH-9, CR-ENH-10 (two outcomes), CR-ENH-15 steps 3–7 (lock, consume scroll, draw, apply, unlock), CR-ENH-18 (`IsAttemptInProgress`), States and Transitions, AC-ENH-8, 9, 10, 11, 12, 33, 36.
**Requirement**: `TR-enh-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Tier 1 calls into the injected `IInventoryService`, acting on return values).
**ADR Decision Summary**: The Enhancement System mutates the bag only through `IInventoryService` and never caches slot contents; inventory changes reach other systems through the Inventory System's own event.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable against the real `InventoryService`. No `Task`, no engine async API.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency; `readonly struct` results — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Design decision — two-phase, synchronous service *(decided 2026-10-07 at readiness)*

The GDD's commit (CR-ENH-15 step 6b) is an asynchronous persistence call, and no ADR says where game logic resumes after it. The service therefore does not call persistence at all. It exposes the attempt in two synchronous calls, and whoever orchestrates the commit sits between them:

1. **`BeginAttempt(charId, itemSlotIndex, scrollSlotIndex)`** — steps 2 to 6a: validate, lock the item slot, consume one scroll, draw, apply the outcome to the bag. Returns either a rejection (nothing changed) or a **pending attempt**.
2. *(the caller commits — Story 005 decides who and how)*
3. **`CompleteAttempt(charId)`** — step 7 after a successful commit: unlock the slot, end the attempt, return the final result.

The attempt is "in progress" from a pending `BeginAttempt` until it is completed. The failed-commit path is a separate call, `RollBackAttempt(charId)`, added by Story 005 — it is not a flag on `CompleteAttempt`, so this story has no half-implemented branch.

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. "Run the attempt" means `BeginAttempt` returning a pending attempt, followed by `CompleteAttempt`.*

- [x] **AC-ENH-9 (success)**: Bronze item at +2, one Bronze scroll (a stack of 1), draw `r = 0.00` → run the attempt → outcome `SUCCESS`, the item's slot is at level 3, the scroll is gone, the slot is unlocked.
- [x] **AC-ENH-10 (destruction, low level)**: same setup, `r = 0.90` (above `P_s[2] = 0.85`) → outcome `DESTRUCTION`, the item slot is empty, the scroll is gone, the slot is unlocked.
- [x] **AC-ENH-11 (destruction)**: Bronze item at +4, `r = 0.99` (above `P_s[4] = 0.65`) → `DESTRUCTION`, item slot empty, scroll gone, slot unlocked.
- [x] **AC-ENH-12 (destruction at +0)**: Bronze item at +0, `r = 0.96` (above `P_s[0] = 0.95`) → `DESTRUCTION`, item slot empty, scroll gone. Only two outcomes exist at every level.
- [x] **AC-ENH-33 (one scroll from a stack)**: item at +2 in slot 0, 5 Bronze scrolls in slot 3. With `r = 0.00` and with `r = 0.90`, slot 3 holds 4 scrolls afterwards.
- [x] **AC-ENH-36 (scroll gone at step 4)**: validation passes but `ConsumeItem(scrollItemID, 1)` fails → `BeginAttempt` returns `RejectedScrollNotFound` and no pending attempt; the item slot is unlocked and still at its previous level; the random source is never drawn; `IsAttemptInProgress` is false; a following valid `BeginAttempt` is accepted.
- [x] **Pending state after `BeginAttempt`**: on a valid request the bag already shows the outcome (level + 1, or the item slot empty) and the scroll is consumed; after a success outcome the item slot is still locked; `IsAttemptInProgress(charId)` is true; the pending attempt reports the outcome and the new level (0 on destruction).
- [x] **Step order**: `LockSlot(itemSlotIndex)` → `ConsumeItem(scrollItemID, 1)` → one draw → `SetEnhancementLevel(itemSlotIndex, level + 1)` on success or `RemoveItem(itemSlotIndex)` on destruction — all inside `BeginAttempt`; `UnlockSlot(itemSlotIndex)` happens in `CompleteAttempt`, not before.
- [x] **Exactly one draw per pending attempt**, and none for a rejected request.
- [x] **`CompleteAttempt` result**: returns the outcome, the new level (0 on destruction) and the result code (`Success` / `Destruction`); afterwards `IsAttemptInProgress` is false. Calling it with no pending attempt for that character throws `InvalidOperationException`.
- [x] **AC-ENH-8 (concurrent attempt)**: while an attempt is pending, a second `BeginAttempt` for the same character — for any item — returns `RejectedConcurrentAttempt` and changes nothing; the first attempt completes unaffected. Another character's attempt is not affected.
- [x] **Broken invariant** *(decided 2026-10-07 at readiness — the GDD treats this as unreachable)*: if `SetEnhancementLevel` returns `false` after the scroll was consumed, the service unlocks the item slot, leaves no pending attempt, and throws `InvalidOperationException` naming the character, the slot and the item. The scroll is not restored.

---

## Implementation Notes

- **API** (all on `EnhancementService`):
  - `public EnhancementAttemptStart BeginAttempt(CharacterID charId, int itemSlotIndex, int scrollSlotIndex)` — `readonly struct` with `IsPending`; `RejectionCode` (meaningful only when not pending); and when pending `Outcome` (`EnhancementOutcome`) and `NewLevel`.
  - `public EnhancementAttemptResult CompleteAttempt(CharacterID charId)` — `readonly struct { Outcome, NewLevel, ResultCode }` (the UI-ENH-2 field set).
  - `public bool IsAttemptInProgress(CharacterID charId)` — now real: true iff a pending attempt is stored for the character.
  - The pending attempt's outcome is known before the commit. It exists so the commit orchestrator can pass it on **after** the commit succeeds; nothing may send it to a client earlier (CR-ENH-11). Say so in the type's doc comment.
- **Pending state**: one entry per character (a dictionary keyed by `CharacterID`) holding what later steps need — item slot index, `ItemId`, previous level, `ScrollItemId`, outcome, new level. Story 005's `RollBackAttempt` reads the same entry, so keep the previous level and both ids even though this story does not use them. `ValidateAttempt` (Story 003) already returns the three values.
- **Random source is injected** — a `System.Random` added to the constructor (`LootRandomFactory` precedent). Draw with `NextDouble()` (`[0, 1)`) and resolve with `EnhancementConfig.ResolveOutcome` (Story 001). Tests inject a subclass that overrides `NextDouble()` to return scripted values and count draws.
- **Constructor gains a fifth argument**, so the Story 003 test fixture must be updated (see QA Test Cases for the file list).
- **Scroll**: `ConsumeItem(charId, scrollItemID, 1)` where `scrollItemID` is the validated scroll. Never `RemoveItem` on the scroll slot — scrolls are stackable. `ConsumeItem` takes the unit from the lowest-index **unlocked** stack of that scroll, which may not be `scrollSlotIndex`, and skips locked stacks. The item slot is locked before this call, which is harmless: it holds no scroll.
- **Step 4 failure**: `UnlockSlot(itemSlotIndex)`, return `RejectedScrollNotFound`, draw nothing, store no pending attempt. A real way to reach it: the only scroll stack is locked — validation does not reject a locked scroll slot (Story 003), and `ConsumeItem` does not count locked stacks.
- **Apply (step 6a)**: success → `SetEnhancementLevel(itemSlotIndex, previousLevel + 1)` — the slot is still locked, which that call requires. Destruction → `RemoveItem(itemSlotIndex)`, which clears the slot and its lock.
- **Unlock (step 7)**: `IInventoryService.UnlockSlot` is documented as a silent no-op on a slot that is not locked, so call it unconditionally in `CompleteAttempt`; after a destruction it does nothing and logs nothing.
- **In-progress check**: `ValidateAttempt` already calls `IsAttemptInProgress` second in its order; making the method real is what activates `RejectedConcurrentAttempt` for a second request.
- **Exception safety**: if anything throws inside `BeginAttempt`, no pending entry may be left behind (the character would be stuck "in progress"). Use `try`/`finally` or record the entry only as the last step.
- **Stale doc comments to correct in `src/Foundation/InventorySystem/IInventoryService.cs`** (doc-only, no behaviour change): `ConsumeItem` says "Only the Consumable Use System calls this" — the Enhancement System now calls it for scrolls (CR-ENH-15 step 4, TD-043); `RemoveItem` says it is "also used by the Enhancement System on an unlocked scroll slot" for scroll consumption — it is not; it is used for item destruction only.
- States (`IDLE`, `VALIDATING`, `LOCKED`, `RESOLVING`, `RESULT_*`) describe the sequence; a public state enum is not required by any criterion — do not add one.

---

## Out of Scope

- Story 005: who calls persistence between the two phases; `RollBackAttempt` (a failed commit); what the committed record contains; AC-ENH-7 and AC-ENH-13
- Story 007: outcome events and the +9 broadcast trigger
- Story 009: holding other inventory requests during an attempt (OQ-ENH-7)
- Story 010: the wire requests and sending `EnhancementAttemptResult`
- A pending attempt for a character who disconnects or is unregistered — Story 005 / Character Persistence

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptSequence_integration_tests.cs` (new). Real `InventoryService`; scripted random source; NPC session stub active. **Fixture update**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` — its `new EnhancementService(...)` calls gain the random-source argument (no behavioural assertion changed), and its `IsAttemptInProgress_…IsFalseInThisStory` test is renamed to say "with no attempt pending".

- **AC-ENH-9** — as stated; `BeginAttempt` is pending with `{SUCCESS, 3}`; `CompleteAttempt` returns `{SUCCESS, 3, Success}`; `IsSlotLocked(itemSlot)` false afterwards.
- **AC-ENH-10** — as stated; pending `{DESTRUCTION, 0}`; result `{DESTRUCTION, 0, Destruction}`; `GetSlot(itemSlot)` is empty at level 0.
- **AC-ENH-11** — as stated.
- **AC-ENH-12** — as stated.
- **Boundary draw** — item at +2: `r = 0.849` → success; `r = 0.85` → destruction.
- **AC-ENH-33** — both runs leave 4 scrolls in slot 3; success run: slot 0 at level 3; destruction run: slot 0 empty.
- **Scroll in a lower slot** — scrolls in slots 2 and 5, request names slot 5 → one unit leaves slot 2 (lowest index), slot 5 unchanged; the attempt still resolves.
- **AC-ENH-36** — the only scroll stack is locked: `BeginAttempt` → not pending, `RejectedScrollNotFound`; item slot unlocked; level unchanged; draw count 0; `IsAttemptInProgress` false; unlock the scroll stack → a second `BeginAttempt` is pending.
- **Pending state** — after a success `BeginAttempt`: level already 3, scroll already consumed, item slot locked, `IsAttemptInProgress` true. After a destruction `BeginAttempt`: slot empty and unlocked, `IsAttemptInProgress` true.
- **Step order** — a recording `IInventoryService` decorator (or the sequence of `OnInventoryChanged` events plus lock state) shows lock → consume → apply inside `BeginAttempt`, and unlock only in `CompleteAttempt`, for a success and for a destruction.
- **One draw** — draw count is 1 after a pending attempt and 0 after each kind of rejection.
- **Complete with nothing pending** — `CompleteAttempt` for a character with no attempt → `InvalidOperationException`. Twice in a row after one attempt → the second throws.
- **AC-ENH-8** — attempt pending on slot 0; a second `BeginAttempt` naming another valid item and scroll → `RejectedConcurrentAttempt`, that item not locked, no scroll consumed, no draw; `CompleteAttempt` then returns the first attempt's result. A different character's `BeginAttempt` during the window is pending normally.
- **After completion** — `IsAttemptInProgress` false; a new attempt on the same item is accepted.
- **Level 9 → 10** — item at +9, `r = 0.00` → level 10; a further `BeginAttempt` → `RejectedAtMaxLevel`.
- **Broken invariant** — an inventory decorator whose `SetEnhancementLevel` returns `false`: `BeginAttempt` throws `InvalidOperationException`; the item slot is unlocked; `IsAttemptInProgress` false.
- **Rejections pass through** — a Story 003 rejection (e.g. tier mismatch) from `BeginAttempt` → not pending, that code, nothing changed.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptSequence_integration_tests.cs` — must exist and pass; the Story 003 validation suite must still pass.

**Status**: [x] Created — 28 test methods + 4 parameterised cases (32 cases), passing (EditMode 1708/1708, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: Story 001 (probability table, resolution), Story 003 (validation) — both Complete; Inventory System Story 010 (Complete)
- Unlocks: Story 005, Story 007

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 12/12 passing (none deferred)
**Deviations**: None blocking. Notes: the GDD's single `ConfirmEnhancement` step is two calls in code (`BeginAttempt` / `CompleteAttempt`, decided at readiness) — `enhancement-system.md` still describes one sequence and names neither; the review added an unlock-and-rethrow for any exception after the item lock (bag changes already made on that path are not undone); `IInventoryService.cs` had two doc comments corrected (in this story's file list, no behaviour change).
**Test Evidence**: Integration — `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptSequence_integration_tests.cs` (32 cases, real `InventoryService`); shared doubles in `EnhancementTestDoubles.cs`; EditMode 1708/1708 in Unity 6000.3.10f1 batch mode. One earlier run the same day was 1707/1708 — the failure was the known flaky wall-clock test logged as TD-050 (Networking Core, untouched here); the rerun with no changes passed.
**Code Review**: Complete — /code-review CHANGES REQUIRED; the required change (item slot left locked when an exception escapes after the lock) and both suggestions applied. LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
**Files**: `src/Foundation/EnhancementSystem/EnhancementService.cs`, `EnhancementAttemptStart.cs`, `EnhancementAttemptResult.cs`; `src/Foundation/InventorySystem/IInventoryService.cs` (doc comments).
**For Story 005**: the pending record already holds the item slot index, `ItemId`, previous level and `ScrollItemId` for `RollBackAttempt`. After a destruction the item slot is empty and unlocked while the attempt is still pending. `EnhancementTestDoubles.cs` has `ScriptedRandom`, `StubNpcSessions` and `RecordingInventoryDecorator` (records `ForceInsert` and `Pickup` too).
