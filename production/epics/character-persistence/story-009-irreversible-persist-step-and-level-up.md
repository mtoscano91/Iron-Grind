# Story 009: The Irreversible-Outcome Persist Step and the Level-Up Path

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (3 open decisions, listed below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (level-up trigger; consecutive level-ups in one tick give one write); CR-CP-5 (the failure protocol); CR-CP-11; EC-CP-5; AC-CP-13, AC-CP-14, AC-CP-17. `design/gdd/networking-core.md` — CR-NET-5 (commit before broadcast, no retry).
**Requirement**: `TR-cp-004`, `TR-cp-005`, `TR-cp-011`

**ADR Governing Implementation**: ADR-011 Decision 5 (the coordinator: `Begin` on tick N, the completion callback on tick N + k; success → deliver, anything else → revert, disconnect, alert, preserve the session, no retry) and Decision 1; ADR-010.
**ADR Decision Summary**: no production caller of `SaveIrreversibleOutcome` awaits it or uses the synchronous sequencer; each goes through `IIrreversibleOutcomeCoordinator.Begin`, whose `startWrite` argument is the persistence call.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the task of a persistence call, in the same statement, to `ITickCompletionQueue.Track` — ADR-011
- Forbidden: `await`, `.Result`, `.Wait()`, `GetResult()`, `ContinueWith` in tick-driven code — ADR-011
- Forbidden: a central EventBus — ADR-010

---

## Acceptance Criteria

*The GDD's criteria name an `ISavePersistence` mock, an `IEventBus` mock, an `ICriticalAlertService` spy and an `ISessionConfiguration`. None exists, and ADR-010 forbids an event bus. They are restated here against what exists: a controllable fake of `ICharacterPersistence` (or the in-memory store with a held operation), the level-up event of `LevelingService`, the coordinator's own callbacks, and a log assertion for the alert.*

- [ ] **Persist step**: one place builds the coordinator's `startWrite` and `isSuccess` arguments from `ICharacterPersistence.SaveIrreversibleOutcome` for a given trigger; `isSuccess` is true for `CharacterSaveResult.Success` only.
- [ ] **AC-CP-13**: a level-up whose write is held open delivers no level-up outcome to the client; when the write is released as `Success`, the outcome is delivered on the next tick's drain.
- [ ] **AC-CP-14**: a level-up from Level 10 whose write fails — the outcome is not delivered; Level and Experience are back at their pre-level-up values; the client is disconnected with `DisconnectReason.Other`; one critical alert is logged; the session is handed to `preserveSessionForTtl` with `SESSION_TTL_SECONDS`, not torn down. Each happens exactly once.
- [ ] **The same for a fault, a cancellation and a watchdog timeout** of the write, and for `ConcurrencyConflict` and `CharacterNotFound`.
- [ ] **AC-CP-17**: two level-ups granted in one tick make one `SaveIrreversibleOutcome(LevelUp)` call, with the final level in the record.
- [ ] **Gate**: the character's mutation gate is closed from `Begin` until the result has been handled, and open afterwards on both branches.
- [ ] **No await**: a source search of the files this story adds or edits outside the persistence layer finds no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.
- [ ] **After the failure**: the rolled-back state is what a later `SaveSession(SessionTTLExpiry)` writes (shown with the in-memory store).

---

## Implementation Notes

- `IIrreversibleOutcomeCoordinator.Begin<TOutcome, TResult>` exists (`src/ServerLogic/Networking/IrreversibleOutcome/`); its `startWrite` is `Func<TOutcome, CancellationToken, Task<TResult>>`. `LevelingService` raises `OnLevelUp` and has `ExecuteLevelUpSequence`; it does not use the coordinator today (read 2026-10-12, by grep only).
- The rollback is the caller's (CR-CP-5 step 2): the Leveling System reverts Level, Experience and the derived stats.
- AC-CP-27 (enhancement) is Enhancement Story 011. AC-CP-28 (respec, with its compensating `AddGold`) and AC-CP-29 (item consumption) belong to systems with no code; this story does not cover them.

**Open decisions for `/story-readiness`**

1. **Does the level-up path belong here.** Moving `LevelingService`'s level-up behind the coordinator edits the Leveling System (a Complete epic) from this epic. The alternative: this story delivers the persist step and proves AC-CP-13 / 14 / 17 with a test caller, and a Leveling story does the wiring.
2. **What reverts a level-up.** Whether `LevelingService` can restore the pre-level-up state today, and the HP/MP restore of CR-2.7 that a level-up applies.
3. **OQ-CP-1** (GDD, recommended): a dedicated `DisconnectReason.PersistenceFailure` in place of `Other` — a wire-visible change to `DisconnectReason`.

---

## Out of Scope

- Story 008: the save itself
- Enhancement Story 011 (AC-CP-27); the Respec and Consumable Use systems (AC-CP-28, AC-CP-29)
- Story 010: the TTL-expiry save that follows a failure

---

## QA Test Cases

- **Held write** — Given a level-up with the write held, Then no outcome delivered across three ticks; When released as Success, Then delivered on the next drain.
- **Failed write** — Given Level 10 and a failing write, Then Level 10 and the old Experience, one disconnect with `Other`, one critical log, one `preserveSessionForTtl(…, 300)`, zero deliveries.
- **Each failure kind** — `ConcurrencyConflict`, `CharacterNotFound`, `DatabaseError`, fault, cancellation, watchdog timeout → the same five effects, once.
- **Two level-ups, one tick** — Then the persistence fake records one call, and the written Level is the final one.
- **Gate** — Closed during the write; open after success and after failure.
- **Edge** — a write already complete when tracked is handled by the next drain.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_IrreversibleOutcome_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 008; Networking Core's coordinator, completion queue and mutation gate — Complete
- Unlocks: Enhancement Story 011 (with Story 008)
