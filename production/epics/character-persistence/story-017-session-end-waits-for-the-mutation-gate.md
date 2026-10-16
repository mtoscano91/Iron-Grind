# Story 017: Session-End Saves Wait for the Mutation Gate

> **Epic**: Character Persistence
> **Status**: Complete (2026-10-16) — written 2026-10-16 to resolve TD-070; decisions settled with the user (below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (clean logout and session TTL expiry), CR-CP-7 (one in-flight save per character; the snapshot-and-version note), CR-CP-10 steps 9 and 10, EC-CP-4, AC-CP-20 (unchanged by this story). `design/gdd/enhancement-system.md` — EC-ENH-2 (logout during an enhancement write).
**Requirement**: `TR-cp-004`, `TR-cp-010`

**ADR Governing Implementation**: ADR-011 Decisions 1, 3 and 4 (start and `Track`, never await; snapshot on the tick thread before the queue; the per-character mutation gate).
**ADR Decision Summary**: an irreversible write closes the character's mutation gate until its completion callback has delivered or rolled back; a session-end save must not take its snapshot while the gate is closed.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the task, in the same statement, to `ITickCompletionQueue.Track` — ADR-011
- Forbidden: `await` in tick-driven code — ADR-011
- Guardrail: the result of a persistence call is handled by `Drain` on the first tick after its task completes — ADR-011

---

## Problem (TD-070)

`SaveSession` takes its expected `SaveVersion` and its snapshot at call time. A clean logout or TTL save started while an irreversible write (enhancement result, level-up) is in flight is queued behind that write, carries a stale version and returns `ConcurrencyConflict`: the logout fails and the session is not released. Reading the version at the queue turn instead (the register's first proposal) was rejected: it breaks EC-CP-4 / AC-CP-20, and the call-time snapshot can hold an outcome the in-flight write later rolls back, which a successful session save would then make durable.

**Fix (user decision 2026-10-16):** `SessionEndCoordinator` does not start a session-end save while the character's mutation gate is held. It waits for `ICharacterMutationGate.OnGateOpened` and starts the save then, so the snapshot is taken after the outcome is delivered or rolled back and no write is ahead of it in the queue. `CharacterPersistenceService` is not changed.

---

## Acceptance Criteria

- [x] **Gate open: unchanged behaviour.** When the gate is not held, `BeginCleanLogout` and `EvaluateSessionTTL` start `SaveSession` and `Track` it in the same call, exactly as in Story 010 (all Story 010 tests pass unchanged apart from the constructor argument).
- [x] **Logout waits.** `BeginCleanLogout` on a `Connected` account whose character's gate is held starts no save, returns `true` and marks the account in flight. When the gate opens and the account is still `Connected`, `SaveSession(CleanLogout)` is started and tracked once; release and `onResult` follow as in Story 010.
- [x] **TTL waits.** `EvaluateSessionTTL` at or after the expiry tick on a `Disconnected_SessionActive` account whose gate is held starts no save, returns `true` and marks the account in flight. When the gate opens and the account is still `Disconnected_SessionActive`, `SaveSession(SessionTTLExpiry)` is started once; the release follows as in Story 010, with the original expiry tick.
- [x] **Re-held on open.** If the gate is held again when the `OnGateOpened` handler runs (an earlier subscriber closed it), nothing is started and the account keeps waiting for the next open.
- [x] **Logout after a failed write (decision 1).** A logout waiting on a write that fails: the failure protocol disconnects and preserves the session before the gate opens, so at the open the account is no longer `Connected`. No save is started, the in-flight mark is cleared, `onResult` receives `DatabaseError` once, one warning is logged, and no release step runs. The preserved session is later saved by its TTL expiry with the rolled-back state.
- [x] **TTL wait and a state change.** A TTL end waiting on the gate whose account is no longer `Disconnected_SessionActive` at the open (e.g. a reconnect) starts nothing, clears the in-flight mark and logs one error (the same rule as Story 010's completion re-check).
- [x] **No double end (EC-CP-4).** While an account waits on the gate, a second `BeginCleanLogout` or `EvaluateSessionTTL` for it starts nothing and returns `false`; one open starts exactly one save.
- [x] **TD-070 closed end to end.** `Enhancement_CommitOrchestration_integration_tests.Commit_CleanLogoutStartedWhileTheWriteIsInFlight_…` expects `Success`, the order `ack, success-event, delivered, logout-result` with the release after `delivered`, the stored record holding the outcome, and the session released; the FINDING comment is removed. No `ConcurrencyConflict` warning is expected.
- [x] **Gate subscribers isolated (TD-060, decision 2).** `CharacterMutationGate.Open` invokes each `OnGateOpened` subscriber in its own `try`/`catch`; a throwing subscriber is logged once with `Debug.LogError` and the later subscribers still run; `Open` does not throw. The test `Open_SubscriberThrows_ExceptionPropagatesAndGateIsAlreadyOpen` is replaced by one asserting this.
- [x] **No await.** `SessionEndCoordinator.cs` contains no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.
- [x] **AC-CP-20 still holds.** The existing Story 007 double-`SaveSession` conflict test passes unchanged.

---

## Implementation Notes

- `SessionEndCoordinator` gains an `ICharacterMutationGate` constructor parameter and subscribes once to `OnGateOpened` in the constructor. Keep a `Dictionary<CharacterID, PendingEnd>` of waiting ends (account id, reason, expiry tick, `onResult`); the account stays in `_inFlight` while it waits. The handler looks up the character, re-checks `IsHeld` and the account state, then starts the save through the same code path the entry points use (extract a private `StartLogoutSave` / `StartTtlSave`).
- The handler runs inside `IrreversibleOutcomeCoordinator`'s completion callback (the gate opens in its `finally`), i.e. inside `Drain` on the tick thread. Calling `Track` from there is allowed; the new task is drained on a later tick.
- Order at the open, from `IrreversibleOutcomeCoordinator.OnCompleted`: deliver **or** failure protocol (revert, disconnect, preserve) → token disposal → `gate.Open`. So the logout sees `Connected` after a delivery and `Disconnected_SessionActive` after a failure — decision 1 relies on this order; add a comment citing it.
- Update every `SessionEndCoordinator` construction in tests (Story 010 tests, the Story 011 integration test) to pass a gate; a real `CharacterMutationGate` is fine.
- Log prefix and message style as in `SessionEndCoordinator` today.
- `character-persistence.md` CR-CP-7 snapshot note: add one sentence — a session-end save (clean logout, TTL expiry) is started only when the character's mutation gate is open; one requested while an irreversible write is in flight waits for the gate (ADR-011 Decision 4). Update `docs/tech-debt-register.md`: TD-070 and TD-060 → Resolved, citing this story.

**Decisions (2026-10-16, user)**

1. A logout waiting on a write that fails is dropped: no save, `onResult(DatabaseError)`, the TTL path saves the preserved session later.
2. TD-060 is fixed in this story, since this is the first production `OnGateOpened` subscriber.

**Performance:** no impact expected — one dictionary entry per waiting end; the handler is O(1) per gate open.

---

## Out of Scope

- Any change to `CharacterPersistenceService` or `SaveSession`'s version rule (AC-CP-20 stays)
- `ZoneClosed` session saves (Zone Instancing teardown, no code)
- Reconnect refusal after the TTL expiry tick (TD-067)
- The server composition wiring the coordinator, gate and session-end component together (TD-068)
- TD-061 (`isSuccess` throwing in the coordinator)

---

## QA Test Cases

- **Logout during a held write, success** — Given a closed gate, When `BeginCleanLogout`, Then no `SaveSession` call; When `Open`, Then one `SaveSession(CleanLogout)`; after the drain, one release and `onResult(Success)`.
- **Logout during a held write, failure** — Given a closed gate and a logout waiting, When the account goes `Disconnected_SessionActive` and then `Open`, Then no `SaveSession`, `onResult(DatabaseError)` once, zero release steps, one warning. Edge: a later TTL expiry saves once.
- **TTL during a held write** — Given a closed gate at the expiry tick, Then nothing started; on `Open`, one `SaveSession(SessionTTLExpiry)`, release once after the drain.
- **Re-held** — A subscriber registered before the coordinator re-closes the gate on open; Then nothing started; the next `Open` starts one save.
- **Double request while waiting** — Two `BeginCleanLogout` (or logout then TTL) while held → second returns `false`; one save after `Open`.
- **Open for another character** — Opening a different character's gate starts nothing.
- **Gate subscriber isolation** — Two subscribers, the first throws: `Open` does not throw, one error logged, the second runs, `IsHeld` false.
- **Enhancement end to end** — the updated Story 011 logout test (criterion "TD-070 closed").

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SessionEnd_tests.cs` (new cases), `tests/EditMode/Networking/TickLoop_CharacterMutationGate_tests.cs` (TD-060), `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitOrchestration_integration_tests.cs` (updated) — must exist and pass; full EditMode suite green.

**Status**: [x] Created — 16 new `[Test]` methods in `CharacterPersistence_SessionEnd_tests.cs`; the gate test replaced; the HoldQueue and Story 011 logout tests updated; EditMode 2932 / 2932

---

## Dependencies

- Depends on: Story 010 (Complete); Networking Core `CharacterMutationGate` and `IrreversibleOutcomeCoordinator` (Complete); Enhancement Story 011 (Complete; its test is updated here)
- Unlocks: the release half of Enhancement Story 011 decision D5; closes TD-070 and TD-060

---

## Completion Notes

**Completed**: 2026-10-16
**Criteria**: 11/11 passing (all automated; "No await" by the existing source scan)
**Test Evidence**: Integration — `tests/EditMode/CharacterPersistence/CharacterPersistence_SessionEnd_tests.cs` (16 new cases), `tests/EditMode/Networking/TickLoop_CharacterMutationGate_tests.cs`, `tests/EditMode/Networking/InboundDispatch_HoldQueue_tests.cs`, `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitOrchestration_integration_tests.cs`; EditMode 2932 / 2932 (batch mode)
**Code Review**: Complete (`/code-review` 2026-10-16, `unity-specialist` + `qa-tester`, APPROVED WITH SUGGESTIONS; all 10 items applied)
**Deviations** (advisory):
- Two files beyond the story's evidence list: `ICharacterMutationGate.cs` (docs only) and `InboundDispatch_HoldQueue_tests.cs` (it asserted that `Open` throws, which TD-060 removes).
- A waiting TTL end that finds the player reconnected (`Connected`) logs a warning, not an error (review item); other state changes still log an error.
- The `OnGateOpened` handler is split into `ResumeLogout` / `ResumeTtl` (40-line limit).
- The Story 011 logout test asserts the session was released instead of `IsSlotLocked` (the released character is no longer registered with Inventory).
- Session ends still waiting on a gate at server shutdown are lost without a log — documented in the class remarks, logged as TD-071.
- TD-070 and TD-060 closed in the register.
