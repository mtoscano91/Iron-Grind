# Story 005: Rollback and the Pending Window

> **Epic**: Enhancement System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

> **Rewritten 2026-10-07 at readiness.** This story was created as "Commit-Then-Deliver and Rollback" for a single `ConfirmEnhancement` call with an injected commit. Story 004 made the service two-phase and synchronous (`BeginAttempt` → the caller commits → `CompleteAttempt`), so this story now covers what the service itself owes on the failure path. Everything that needs a real commit — who calls `SaveIrreversibleOutcome`, mapping its result codes, withholding the client result, disconnecting the client — moved to **Story 011 (Commit Orchestration)**, which is Blocked. AC-ENH-8 moved to Story 004 and is Complete.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-7 (lock lifetime: every exit path ends in `UnlockSlot` or `RemoveItem`), CR-ENH-11 (commit-then-deliver: on a failed write the system rolls back every in-memory change), CR-ENH-15 Rollback (steps 1–3 and the "rollback call itself fails" rule), EC-ENH-6, AC-ENH-7, and the bag-state halves of AC-ENH-23, AC-ENH-34 and AC-ENH-35. `design/gdd/character-persistence.md` — CR-CP-5 (rollback is caller-owned).
**Requirement**: `TR-enh-006` (service side)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Tier 1 calls into the injected `IInventoryService`, acting on return values). ADR-006 (Persistence Layer) governs the commit itself, which is Story 011 — this story does not call persistence.
**ADR Decision Summary**: The Enhancement System mutates the bag only through `IInventoryService`; on a failed irreversible write the caller, not the persistence layer, restores its own in-memory changes.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable against the real `InventoryService`. No `Task`, no engine async API.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. "Pending" means `BeginAttempt` returned a pending attempt and neither `CompleteAttempt` nor `RollBackAttempt` has run.*

- [x] **AC-ENH-34 (rollback after a success outcome — bag state)**: item at +2 in slot 0, 3 Bronze scrolls in slot 1, draw `r = 0.00`; `BeginAttempt` is pending (slot 0 at level 3, 2 scrolls). `RollBackAttempt` → slot 0 holds the item at level 2, unlocked; slot 1 holds 3 scrolls; the attempt is no longer in progress.
- [x] **AC-ENH-23 (rollback after a destruction — bag state)**: item at +4 in slot 0, 3 Bronze scrolls in slot 1, all other slots empty, draw `r = 0.99`; `BeginAttempt` is pending (slot 0 empty, 2 scrolls). `RollBackAttempt` → the bag holds the item exactly once, at level 4, in an unlocked slot; slot 1 holds 3 scrolls; the attempt is no longer in progress.
- [x] **Rollback order (CR-ENH-15 Rollback)**: item first — `SetEnhancementLevel(itemSlotIndex, previousLevel)` after a success outcome, `ForceInsert(itemID, previousLevel)` after a destruction — then the scroll (`Pickup(charId, scrollItemID, 1)`), then `UnlockSlot(itemSlotIndex)`, then the critical log.
- [x] **Critical log**: every `RollBackAttempt` logs one server error containing `CriticalEnhancementWriteFailed` and naming the character.
- [x] **A rollback call fails**: if the item call or the scroll call does not succeed, one server error containing `CriticalEnhancementRollbackFailed` is logged with the character, the item's `ItemID` and previous level, and the scroll's `ItemID`; the remaining rollback steps still run; the attempt still ends (not in progress, item slot unlocked).
- [x] **`RollBackAttempt` produces no result**: it returns nothing a caller could send to a client, and after it `CompleteAttempt` for that character throws (nothing is pending).
- [x] **Nothing pending**: `RollBackAttempt` for a character with no pending attempt throws `InvalidOperationException`.
- [x] **AC-ENH-7 (item locked during the pending window)**: while a success-outcome attempt is pending, `IsSlotLocked(itemSlotIndex)` is true and a direct `Move(itemSlotIndex, otherSlot)` fails with `MoveFailReason.SourceLocked`, both slots unchanged. After `CompleteAttempt` the slot is unlocked and the same move succeeds.
- [x] **AC-ENH-35 (the bag a commit would read already holds the outcome)**: item at +2 in slot 0, 2 Bronze scrolls in slot 1. While pending, `ExportSnapshot` shows — success run: slot 0 at level 3, slot 1 quantity 1; destruction run: no entry for slot 0, slot 1 quantity 1.
- [x] **Rollback restores the pre-attempt snapshot**: for both outcomes, `ExportSnapshot` after `RollBackAttempt` equals the snapshot taken before `BeginAttempt` whenever the item returns to its own slot (see the note on slot positions).
- [x] **A new attempt after a rollback is accepted**, on the same item and scroll.

---

## Implementation Notes

- **API**: `public void RollBackAttempt(CharacterID charId)` on `EnhancementService`. It reads the pending record Story 004 stores (item slot index, `ItemId`, previous level, `ScrollItemId`, outcome) and removes it.
- **Steps** (CR-ENH-15 Rollback):
  1. *Item.* Success outcome → `SetEnhancementLevel(charId, itemSlotIndex, previousLevel)`; the slot is still locked, which that call requires. Destruction outcome → `ForceInsert(charId, itemId, previousLevel)`; it places the item in the lowest-index empty slot.
  2. *Scroll.* `Pickup(charId, scrollItemId, 1)` — the GDD's `PickupRequest`. It tops up an existing stack of that scroll or fills an empty slot.
  3. `UnlockSlot(charId, itemSlotIndex)` — a documented silent no-op when the slot is not locked (after a destruction).
  4. `Debug.LogError` with the `CriticalEnhancementWriteFailed` tag.
- **Slot positions after a destruction rollback.** The GDD says the restored scroll "refills the slot it emptied" and that the item "may be a different slot from `itemSlotIndex`". With the item restored first into the lowest empty slot, an emptied scroll slot with a *lower* index than the item's old slot is taken by the item, and the scroll then lands in the item's old slot. The bag's contents are correct; the two positions swap. No GDD criterion depends on the position (AC-ENH-23 and AC-ENH-34 keep the scroll stack non-empty, or the item in the lower slot). Tests for that arrangement assert contents, not slot indices. Do not add logic to prefer the original slots — `IInventoryService` has no call for it.
- **A rollback call fails** (`SetEnhancementLevel` returns `false`, `ForceInsert` returns `false`, or `Pickup` is not a success): log `CriticalEnhancementRollbackFailed` once per failed call with the identifiers needed for manual restoration, and continue with the remaining steps. The GDD treats this as a caller bug made unreachable by CR-ENH-18 (nothing else changes the bag during the window); that rule is enforced by Story 009, not here.
- **The attempt always ends.** Remove the pending record and unlock even if an inventory call throws (a subscriber's exception propagates out of `IInventoryService` calls — Story 004's review): use `try`/`finally`, then let the exception continue.
- **Who calls this**: the commit orchestrator (Story 011), when the commit reports any non-success result. Disconnecting the client, preserving the rolled-back session and withholding the client result are that story's and Character Persistence's duties (CR-CP-5), not this method's.
- `IsAttemptInProgress` stays true until `RollBackAttempt` has finished, then is false.
- Reuse the Story 004 test doubles in `tests/EditMode/Integration/EnhancementSystem/EnhancementTestDoubles.cs` (`ScriptedRandom`, `StubNpcSessions`, `RecordingInventoryDecorator`). The decorator will need two more switches for the failing-rollback cases (make `ForceInsert` and `Pickup` fail without forwarding), in the same style as `FailSetEnhancementLevel`.

---

## Out of Scope

- Story 011: calling `SaveIrreversibleOutcome`; mapping `CharacterNotFound` / `ConcurrencyConflict` / `DatabaseError` to a rollback; "exactly one commit call"; "no result before or without a successful commit"; client disconnect and session preservation; a commit that throws; AC-ENH-13
- Story 007: outcome events and the +9 broadcast trigger
- Story 009: holding other inventory-mutating requests during the window (AC-ENH-38)
- Story 010: sending `EnhancementAttemptResult`
- A pending attempt whose character disconnects or is unregistered — Story 011 / Character Persistence

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_Rollback_integration_tests.cs` (new). Real `InventoryService` behind the `RecordingInventoryDecorator`; `ScriptedRandom`; NPC session stub active. **Also modified**: `EnhancementTestDoubles.cs` (two failure switches on the decorator).

- **AC-ENH-34** — as stated; also no `OnInventoryFull`; `IsAttemptInProgress` false afterwards.
- **AC-ENH-23** — as stated; count the item across all 20 slots: exactly 1, level 4, `IsSlotLocked` false for its slot.
- **Emptied scroll stack, item in the lower slot** — item in slot 0, one scroll (a stack of 1) in slot 1, destruction → rollback → slot 0 holds the item at its previous level, slot 1 holds 1 scroll.
- **Emptied scroll stack, item in the higher slot** — one scroll in slot 1, item in slot 3, destruction → rollback → the bag holds the item once at its previous level and exactly 1 scroll; assert contents only (the two positions swap).
- **Rollback order** — the decorator's `Calls` after `BeginAttempt` + `RollBackAttempt`: success outcome `LockSlot, ConsumeItem, SetEnhancementLevel, SetEnhancementLevel, Pickup, UnlockSlot`; destruction `LockSlot, ConsumeItem, RemoveItem, ForceInsert, Pickup, UnlockSlot`.
- **Critical log** — each rollback: `LogAssert.Expect(LogType.Error, …CriticalEnhancementWriteFailed…)`, and nothing else unexpected.
- **Item restore fails** — decorator makes `ForceInsert` fail after a destruction → one `CriticalEnhancementRollbackFailed` error naming the item and level; `Pickup` and `UnlockSlot` still called; the `CriticalEnhancementWriteFailed` error still logged; not in progress.
- **Scroll restore fails** — decorator makes `Pickup` fail → one `CriticalEnhancementRollbackFailed` error naming the scroll; the item is restored; `UnlockSlot` still called; not in progress.
- **Level restore fails** — `FailSetEnhancementLevel` switched on after a pending success → one `CriticalEnhancementRollbackFailed`; scroll restored; slot unlocked; not in progress.
- **No result** — after `RollBackAttempt`, `CompleteAttempt` throws `InvalidOperationException`.
- **Nothing pending** — `RollBackAttempt` with no attempt → `InvalidOperationException`; with only another character's attempt pending → throws and leaves that attempt pending.
- **AC-ENH-7** — pending success: `IsSlotLocked` true; `Move(itemSlot, emptySlot)` → not success, `SourceLocked`, both slots unchanged. After `CompleteAttempt`: unlocked, the move succeeds.
- **AC-ENH-35** — pending success: snapshot has slot 0 at level 3 and slot 1 quantity 1. Pending destruction: no slot 0 entry, slot 1 quantity 1.
- **Snapshot restored** — snapshot before `BeginAttempt` equals the snapshot after `RollBackAttempt`, for a success and for a destruction (item in the lower slot).
- **Attempt after rollback** — a following `BeginAttempt` on the same slots is pending.
- **In-progress flag** — true while pending; false after `RollBackAttempt`.
- **Exception during rollback** — an inventory subscriber throws during the scroll restore → the exception propagates; the attempt is no longer in progress and the item slot is unlocked.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_Rollback_integration_tests.cs` — must exist and pass; the Story 003 and Story 004 suites must still pass.

**Status**: [x] Created — 20 test methods + 4 parameterised cases (24 cases), passing (EditMode 1732/1732, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: Story 004 (two-phase attempt, pending record) — Complete; Inventory System Story 010 (Complete)
- Unlocks: Story 011 (Commit Orchestration — also needs its own blockers cleared); closes the service side of Story 009's rollback assumption

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 11/11 passing (none deferred)
**Deviations**: None blocking. Notes: on an exception from a restore step the implementation goes further than this story asked — the scroll restore still runs when the item restore throws, and `CriticalEnhancementWriteFailed` is logged on that path too (code review); after a destruction rollback the item and an emptied scroll stack can swap slot positions (recorded at readiness; differs from the GDD's "refills the slot it emptied").
**Test Evidence**: Integration — `tests/EditMode/Integration/EnhancementSystem/Enhancement_Rollback_integration_tests.cs` (24 cases, real `InventoryService`); EditMode 1732/1732 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all three applied (scroll restore always runs, critical log on every path, log contents pinned in the tests). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
**Files**: `src/Foundation/EnhancementSystem/EnhancementService.cs` (`RollBackAttempt`, `RestoreItem`, `RestoreScroll`); `tests/EditMode/Integration/EnhancementSystem/EnhancementTestDoubles.cs` (`FailForceInsert`, `FailPickup`).
**For Story 011**: call `RollBackAttempt(charId)` for any non-success commit result; it logs `CriticalEnhancementWriteFailed` itself, returns nothing, and can rethrow a subscriber's exception after ending the attempt.
