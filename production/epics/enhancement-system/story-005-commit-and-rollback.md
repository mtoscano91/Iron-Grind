# Story 005: Commit-Then-Deliver and Rollback

> **Epic**: Enhancement System
> **Status**: Ready — **one open point to settle at `/story-readiness`: the shape of the commit seam (see Implementation Notes)**
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-7 (lock lifetime), CR-ENH-8 (concurrent attempt prevention), CR-ENH-11 (commit-then-deliver), CR-ENH-15 step 6b, Transaction boundary and Rollback, EC-ENH-1, EC-ENH-2, EC-ENH-6, AC-ENH-7, 8, 13, 23, 34, 35. `design/gdd/character-persistence.md` — `SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger, CancellationToken): Task<CharacterSaveResult>`, CR-CP-5 (caller-owned rollback), CR-CP-6 (one transaction).
**Requirement**: `TR-enh-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-006: Persistence Layer (one-transaction commit of an irreversible outcome; write budget ≤ 50 ms P95). ADR-010: Event/Messaging Architecture (Tier 1 calls).
**ADR Decision Summary**: An irreversible outcome is written in a single transaction and confirmed before any client-visible result is sent; on a failed write the caller restores its own in-memory changes.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C# with `System.Threading.Tasks`; EditMode-testable with a controllable `TaskCompletionSource`. No Unity async API.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [ ] **AC-ENH-35 (committed record holds the outcome)**: item at +2 in slot 0, 2 Bronze scrolls in slot 1. The commit is called exactly once, with trigger `EnhancementResult`. The bag as the commit sees it (`ExportSnapshot`) — success run: slot 0 at level 3, slot 1 quantity 1; destruction run: no entry for slot 0, slot 1 quantity 1. The attempt result becomes available only after the commit reports `Success`.
- [ ] **AC-ENH-34 (commit failure after success)**: item at +2 in slot 0, 3 scrolls in slot 1, `r = 0.00`, commit returns `DatabaseError` → slot 0 holds the item at level 2, unlocked; slot 1 holds 3 scrolls; no attempt result is produced; `CriticalEnhancementWriteFailed` is logged.
- [ ] **AC-ENH-23 (commit failure after destruction)**: item at +4 in slot 0, 3 scrolls in slot 1, all other slots empty, `r = 0.99`, commit returns `DatabaseError` → the bag holds the item exactly once, at level 4, in an unlocked slot; slot 1 holds 3 scrolls; no attempt result; `CriticalEnhancementWriteFailed` is logged.
- [ ] **Rollback order**: item first (`SetEnhancementLevel(itemSlotIndex, previousLevel)` after a success outcome, `ForceInsert(itemID, previousLevel)` after a destruction), then the scroll (`Pickup(charId, scrollItemID, 1)`), then `UnlockSlot(itemSlotIndex)`, then the critical log.
- [ ] **Any non-Success code rolls back**: `CharacterNotFound`, `ConcurrencyConflict` and `DatabaseError` all take the rollback path.
- [ ] **Rollback call fails**: if a rollback call itself fails, `CriticalEnhancementRollbackFailed` is logged with the character, the item's `ItemID` and level, and the scroll's `ItemID`, and the remaining rollback steps still run.
- [ ] **AC-ENH-7 (slot locked during the write)**: while the commit is held open, `IsSlotLocked(itemSlotIndex)` is true and a direct server-side `Move(itemSlotIndex, otherSlot)` fails with the source-locked reason, both slots unchanged.
- [ ] **AC-ENH-8 (concurrent attempt)**: while one attempt's commit is held open, a second `ConfirmEnhancement` for any item of the same player returns `RejectedConcurrentAttempt`; the first attempt completes unaffected.
- [ ] **AC-ENH-13 (outcome stands without delivery)**: when nothing consumes the result after a successful commit (the client is gone), the bag still holds the committed state — item at level 3, scroll gone, slot unlocked — and the service sends nothing further.
- [ ] **No delivery before the commit**: nothing that Story 007 or Story 010 treats as "the outcome happened" is raised until the commit has returned `Success`, and nothing is raised when it fails.

---

## Implementation Notes

> **Update 2026-10-07 (Story 004 readiness) — read this before the notes below.** The commit seam was partly decided: `EnhancementService` is a **two-phase, synchronous** service. `BeginAttempt` applies the outcome and returns a pending attempt; `CompleteAttempt(charId)` finishes it after a successful commit; this story adds **`RollBackAttempt(charId)`** for a failed commit. The service never calls persistence and never sees a `Task`. What is still open for this story's readiness is narrower: **who sits between the two calls** — an orchestrator over the existing synchronous `CommitBeforeBroadcastSequencer`, an asynchronous wrapper around `SaveIrreversibleOutcome`, or both — and whether that needs an ADR. Consequences for the text below: the "commit fake" becomes "the test simply delays `CompleteAttempt` / calls `RollBackAttempt`"; **AC-ENH-8 moved to Story 004** (a second `BeginAttempt` while one is pending); AC-ENH-7's "write held open" is the window between `BeginAttempt` and `CompleteAttempt`. This story's criteria and notes must be rewritten on that basis at its `/story-readiness`.

- **Commit seam (open point).** The GDD's commit is `SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger.EnhancementResult): Task<CharacterSaveResult>`, and three criteria (AC-ENH-7, 8, and Story 009's AC-ENH-38) need a write that is still in flight. Two facts in the codebase bear on this:
  - Character Persistence has no code and no epic. Declare a **consumer-side interface** in this module with the GDD's asynchronous signature; Character Persistence implements it later. Tests use a fake driven by a `TaskCompletionSource`.
  - `IronGrind.Networking`'s `CommitBeforeBroadcastSequencer.Execute` (Networking Core) already implements validate → acknowledge → compute → persist → broadcast with revert, disconnect and session preservation — but its `persistOutcome` is a synchronous `Func<TOutcome, bool>`. It cannot express an in-flight write as written.
  - **Decide at `/story-readiness`**: (a) the Enhancement service owns the asynchronous sequence and does not use the sequencer; (b) the sequencer gains an asynchronous overload (a Networking Core change — likely an ADR or at least a tech-debt item); or (c) the service is synchronous at this layer and the in-flight window is modelled differently. Leveling's `RespecTwoPhaseCommitCoordinator` is the nearest precedent for a caller-owned commit. Do not pick one silently.
- **What this story does not do on failure**: disconnecting the client and preserving the rolled-back session for `SESSION_TTL_SECONDS` are Character Persistence / Networking duties (CR-CP-5). The service reports the failure to its caller; it sends no attempt result and triggers no broadcast.
- **Rollback details** (CR-ENH-15 Rollback):
  1. Item — success outcome: `SetEnhancementLevel(itemSlotIndex, previousLevel)` (the slot is still locked). Destruction outcome: `ForceInsert(itemID, previousLevel)`; the item may land in a different slot from `itemSlotIndex`.
  2. Scroll — the GDD's `PickupRequest(CharacterID, scrollItemID, 1)` is `IInventoryService.Pickup(charId, scrollItemID, 1)` in code.
  3. `UnlockSlot(itemSlotIndex)` (a no-op if the slot was cleared), then `Debug.LogError` with the `CriticalEnhancementWriteFailed` tag.
- Capture `previousLevel`, `itemID` and `scrollItemID` before step 4 — after a destruction the slot no longer holds them.
- The rollback relies on CR-ENH-18: nothing else changes the bag while the write is in flight, so the vacated slot is still free. Enforcing that is Story 009 (blocked on OQ-ENH-7). In this story the tests simply do not mutate the bag during the write, except for AC-ENH-7's locked-slot move, which is expected to fail.
- `IsAttemptInProgress` stays true for the whole in-flight window and is cleared on both the success and the rollback exit.
- A commit that throws (rather than returning a non-Success code) is not covered by the GDD. The sequencer treats a throw as a failure; raise the same question for this seam at `/story-readiness`.

---

## Out of Scope

- Story 004: the in-memory sequence with a commit that succeeds immediately
- Story 007: the events raised after a successful commit
- Story 009: holding other inventory-mutating requests during the write (AC-ENH-38)
- Story 010: sending `EnhancementAttemptResult`; the "result is not re-sent on reconnect" half of AC-ENH-13
- Character Persistence epic: the real `SaveIrreversibleOutcome`, the client disconnect and session preservation of CR-CP-5, and AC-ENH-2 (level persists across sessions)

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitAndRollback_integration_tests.cs` (new). Real `InventoryService`; scripted random source; commit fake that records calls, captures `ExportSnapshot` at call time, and completes on demand with a chosen `CharacterSaveResult`.

- **AC-ENH-35 success run** — one commit call, trigger `EnhancementResult`; captured snapshot: slot 0 level 3, slot 1 quantity 1; no result before the fake completes; result `{SUCCESS, 3, Success}` after.
- **AC-ENH-35 destruction run** — captured snapshot: no slot 0 entry, slot 1 quantity 1.
- **AC-ENH-34** — as stated; also no `OnInventoryFull`, and the scroll stack is back at 3 in slot 1.
- **AC-ENH-23** — as stated; count the item across all 20 slots: exactly 1, level 4, `IsSlotLocked` false for its slot.
- **Rollback when the scroll stack was emptied** — one scroll (stack of 1) in slot 1, destruction, commit fails → slot 1 holds 1 scroll again.
- **Each failure code** — `CharacterNotFound`, `ConcurrencyConflict`, `DatabaseError` (`[TestCase]`) → rolled back, critical log once.
- **Rollback order** — recording inventory wrapper: item call, then `Pickup`, then `UnlockSlot`.
- **Rollback call fails** — inventory double whose `Pickup` fails during rollback → `CriticalEnhancementRollbackFailed` logged with the three identifiers; `UnlockSlot` still called; `CriticalEnhancementWriteFailed` still logged.
- **AC-ENH-7** — commit held open: `IsSlotLocked(itemSlot)` true; `Move(itemSlot, emptySlot)` fails with the source-locked reason; both slots unchanged. Complete the commit → slot unlocked.
- **AC-ENH-8** — commit held open: second `ConfirmEnhancement` (a different valid item and scroll) → `RejectedConcurrentAttempt`, its item never locked, its scroll not consumed. Complete the first → it succeeds normally.
- **AC-ENH-13** — success, commit completes, the test never reads the result → bag at level 3, scroll gone, unlocked; no further inventory events.
- **In-progress flag** — true while held open; false after success; false after rollback.
- **No delivery on failure** — the result/notification seam records zero outcomes after a failed commit.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitAndRollback_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (attempt sequence); Inventory System Story 010 (Complete)
- Unlocks: Story 009 (when OQ-ENH-7 closes), Story 010 (when TD-046 closes)
