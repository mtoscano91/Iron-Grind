# Story 009: The Irreversible-Outcome Persist Step and the Level-Up Path

> **Epic**: Character Persistence
> **Status**: Complete — 2026-10-15 (COMPLETE WITH NOTES; see Completion Notes)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (level-up trigger; consecutive level-ups in one tick give one write); CR-CP-5 (the failure protocol); CR-CP-11; EC-CP-5; AC-CP-13, AC-CP-14, AC-CP-17. `design/gdd/networking-core.md` — CR-NET-5 (commit before broadcast, no retry).
**Requirement**: `TR-cp-004`, `TR-cp-005`, `TR-cp-011`

**ADR Governing Implementation**: ADR-011 Decision 5 (the coordinator: `Begin` on tick N, the completion callback on tick N + k; success → deliver, anything else → revert, disconnect, alert, preserve the session, no retry) and Decision 1; ADR-010.
**ADR Decision Summary**: no production caller of `SaveIrreversibleOutcome` awaits it or uses the synchronous sequencer; each goes through `IIrreversibleOutcomeCoordinator.Begin`, whose `startWrite` argument is the persistence call.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the task of a persistence call, in the same statement, to `ITickCompletionQueue.Track` — ADR-011
- Forbidden: `await`, `.Result`, `.Wait()`, `GetResult()`, `ContinueWith` in tick-driven code — ADR-011
- Forbidden: a central EventBus — ADR-010

**Performance**: no tick-time impact expected — the write runs off the tick thread; the persist step adds two delegate allocations per irreversible outcome.

---

## Decisions (`/story-readiness`, 2026-10-15)

1. **The level-up wiring is not in this story.** The only caller of `NotifyExperienceCrossedThreshold` is `CharacterStats.AddExperience` (`CharacterStats.cs:1010`), synchronously inside an XP gain; moving it behind the coordinator edits two Complete epics (Leveling, Character Stats). This story delivers the persist step and proves AC-CP-13 / 14 / 17 through the coordinator with a **test caller**. The wiring is a Leveling follow-up story (see Out of Scope).
2. **What reverts a level-up — moved with the wiring.** No revert exists today: `LevelingStateSnapshot` holds only `HeldFreePoints`, `RestoreLevelingState` only clamps, and `ExecuteLevelUpSequence` writes Level, the auto-allocated base stats, held points, the derived stats and CurrentHP / CurrentMP (CR-2.7). A pre-level-up capture and restore of all of these is the follow-up story's work. Here, the test caller captures and restores its own values.
3. **OQ-CP-1 stays open; `DisconnectReason.Other` is kept.** The shared `IrreversibleWriteFailureProtocol` already uses `Other`; promoting it touches every irreversible-outcome caller and is a pre-launch item.

Two questions found at readiness also go to the follow-up story, since they only arise with the real level-up path: what "Experience at its pre-level-up value" means in AC-CP-14 (before or after the triggering XP gain), and what happens to an XP gain that crosses the next threshold while a level-up write is in flight (`Begin` returns `RejectedWriteInFlight`; `AddExperience` does not check the gate).

---

## Acceptance Criteria

*The GDD's criteria name an `ISavePersistence` mock, an `IEventBus` mock, an `ICriticalAlertService` spy and an `ISessionConfiguration`. None exists, and ADR-010 forbids an event bus. They are restated here against what exists: a controllable fake of `ICharacterPersistence` (or the in-memory store with a held operation), the coordinator's own delegates, and a log assertion for the alert. The level-up is played by a test caller that applies a level-up in memory in `computeAndApplyOutcome` and restores its captured values in `revertOnFailure`.*

- [x] **Persist step**: one place builds the coordinator's `startWrite` and `isSuccess` arguments from `ICharacterPersistence.SaveIrreversibleOutcome` for a given `IrreversibleOutcomeTrigger`; `isSuccess` is true for `CharacterSaveResult.Success` only. The persist step does not await or block.
- [x] **AC-CP-13**: a test-caller level-up whose write is held open delivers no outcome; when the write is released as `Success`, the outcome is delivered on the next tick's drain.
- [x] **AC-CP-14**: a test-caller level-up from Level 10 whose write returns `DatabaseError` — the outcome is not delivered; the caller's revert runs, leaving Level 10 and the test caller's captured Experience; the client is disconnected with `DisconnectReason.Other`; one critical alert is logged; the session is handed to `preserveSessionForTtl` with `SESSION_TTL_SECONDS`, not torn down. Each happens exactly once.
- [x] **The same for a fault, a cancellation and a watchdog timeout** of the write, and for `ConcurrencyConflict` and `CharacterNotFound`.
- [x] **AC-CP-17**: a test caller that applies two level-ups in one `computeAndApplyOutcome` makes one `SaveIrreversibleOutcome(LevelUp)` call, and the written record holds the final level.
- [x] **Gate**: the character's mutation gate is closed from `Begin` until the result has been handled, and open afterwards on both branches.
- [x] **No await**: a source search of the files this story adds or edits outside the persistence layer finds no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.
- [x] **After the failure**: the rolled-back state is what a later `SaveSession(SessionTTLExpiry)` writes (shown with the in-memory store).

---

## Implementation Notes

- `IIrreversibleOutcomeCoordinator.Begin<TOutcome, TResult>` exists (`src/ServerLogic/Networking/IrreversibleOutcome/`); its `startWrite` is `Func<TOutcome, CancellationToken, Task<TResult>>`, and its failure steps go through `IrreversibleWriteFailureProtocol` (`Other`, critical alert, `SESSION_TTL_SECONDS`).
- `ICharacterPersistence.SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger, CancellationToken)` returns `Task<CharacterSaveResult>` (Story 008). The persist step adapts it: `startWrite = (_, token) => persistence.SaveIrreversibleOutcome(charId, trigger, token)`, `isSuccess = r => r == CharacterSaveResult.Success`. Enhancement Story 011 is its next caller.
- The rollback is the caller's (CR-CP-5 step 2).
- AC-CP-27 (enhancement) is Enhancement Story 011. AC-CP-28 (respec, with its compensating `AddGold`) and AC-CP-29 (item consumption) belong to systems with no code; this story does not cover them.

---

## Out of Scope

- Story 008: the save itself
- **The real level-up path** — a Leveling follow-up story (not yet written): `LevelingService`'s level-up behind the coordinator, the pre-level-up capture and revert (decision 2), the AC-CP-14 Experience reading and the in-flight XP-gain rule (above). AC-CP-13 / 14 / 17 are re-proven there against `LevelingService`.
- OQ-CP-1 (`DisconnectReason.PersistenceFailure`)
- Enhancement Story 011 (AC-CP-27); the Respec and Consumable Use systems (AC-CP-28, AC-CP-29)
- Story 010: the TTL-expiry save that follows a failure

---

## QA Test Cases

- **Held write** — Given a test-caller level-up with the write held, Then no outcome delivered across three ticks; When released as Success, Then delivered on the next drain.
- **Failed write** — Given Level 10 and a write returning `DatabaseError`, Then Level 10 and the captured Experience, one disconnect with `Other`, one critical log, one `preserveSessionForTtl(…, 300)`, zero deliveries.
- **Each failure kind** — `ConcurrencyConflict`, `CharacterNotFound`, `DatabaseError`, fault, cancellation, watchdog timeout → the same five effects, once.
- **Two level-ups, one outcome** — Then the persistence fake records one call with `LevelUp`, and the written Level is the final one.
- **Gate** — Closed during the write; open after success and after failure.
- **Edge** — a write already complete when tracked is handled by the next drain.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_IrreversibleOutcome_tests.cs` — must exist and pass.

**Status**: [x] Created — 22 cases (18 `[Test]` methods, two run over every enum value via `[ValueSource]`), all passing

---

## Dependencies

- Depends on: Story 008; Networking Core's coordinator, completion queue and mutation gate — Complete
- Unlocks: Enhancement Story 011 (with Story 008); the Leveling follow-up story (the real level-up path)

---

## Completion Notes

**Completed**: 2026-10-15
**Criteria**: 8/8 passing (all automated; "No await" by source search of `IrreversibleOutcomePersistStep.cs`)
**Deviations** (advisory):
- AC-CP-13 / 14 / 17 are proven with a test caller, not `LevelingService`, as decided at readiness. They are to be re-proven against the real level-up in the Leveling follow-up story (not yet written), which also owns the level-up revert, the AC-CP-14 Experience reading and the in-flight XP-gain rule.
**Test Evidence**: Integration — `tests/EditMode/CharacterPersistence/CharacterPersistence_IrreversibleOutcome_tests.cs` (22 cases); EditMode 2837 / 2837 in batch mode
**Code Review**: Complete — APPROVED WITH SUGGESTIONS; all suggestions applied (conflict log regex, stored row unchanged after a failed write, in-flight count and no-failure checks on success paths, every enum value, held-write release in `finally`, doc wording)
