# Story 011: Commit Orchestration

> **Epic**: Enhancement System
> **Status**: Ready — `/story-readiness` 2026-10-16 (rewritten from the pre-decision placeholder; decisions D1–D5 below, user-approved). Blockers cleared: ADR-011 Accepted 2026-10-07; `SaveIrreversibleOutcome` implemented and proven through the coordinator (Character Persistence Stories 008, 009); TD-046 closed in design 2026-10-09.
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours

> **Created 2026-10-07 at Story 005's readiness.** Split out of the original Story 005 ("Commit-Then-Deliver and Rollback") when the service became two-phase. Story 005 keeps the service-side rollback; this story is whoever stands between `BeginAttempt` and `CompleteAttempt` / `RollBackAttempt`.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-11 (commit-then-deliver), CR-ENH-15 step 6b, Transaction boundary, Rollback step 4, EC-ENH-1, EC-ENH-2, EC-ENH-6, and the commit halves of AC-ENH-13, AC-ENH-23, AC-ENH-34 and AC-ENH-35. `design/gdd/character-persistence.md` — `SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger, CancellationToken): Task<CharacterSaveResult>`, CR-CP-5 (failure protocol: caller-owned rollback, client disconnect, session preserved), CR-CP-6 (one transaction), CR-CP-7 (at most one write in flight per character). `design/gdd/networking-core.md` — CR-NET-5 (commit-before-broadcast, no retry).
**Requirement**: `TR-enh-006` (commit side) — "Commit-then-deliver: the outcome becomes durable through one `SaveIrreversibleOutcome` write before any result is sent; on a failed write the caller restores the item (previous level, or `ForceInsert`) and the scroll, unlocks, and logs `CriticalEnhancementWriteFailed` (CR-ENH-11, CR-ENH-15 step 6b and Rollback)"

**ADR Governing Implementation**: ADR-011 Decision 5 (`IIrreversibleOutcomeCoordinator.Begin`: apply on tick N, deliver or revert on tick N + k; no `await` in game logic); ADR-006 (one transaction per irreversible outcome); ADR-010 (outcome events stay direct C# events, raised by `CompleteAttempt`); ADR-014 (the handler's system owns the reply and the deduplication).
**ADR Decision Summary**: an enhancement attempt is applied in memory, persisted with `SaveIrreversibleOutcome(EnhancementResult)` without awaiting, and completed (events, result) only after the write succeeds; on any other result the caller rolls back and the coordinator disconnects, alerts and preserves the session.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved (plain C# over the coordinator, the completion queue and the persistence service).

**Control Manifest Rules (Feature layer, v2026-10-09)**:
- Required: every irreversible outcome goes through `IrreversibleOutcomeCoordinator` — for the Enhancement System `BeginAttempt` in `Begin`, `CompleteAttempt` on success, `RollBackAttempt` on failure — ADR-011
- Required: the handler's system owns the reply to the client and the request deduplication; the dispatcher does neither — ADR-014
- Required: Enhancement draws one `NextDouble()` per attempt and none on a rejected attempt — ADR-013 (unchanged by this story; verified by the D1 rejection test)
- Forbidden: `await`, `async` methods, `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith`, `async void` in tick-driven feature code — ADR-011
- Forbidden: reading `ICharacterMutationGate`, or holding / rejecting a request because a write is in flight, inside a game system — ADR-011
- Forbidden: an `EventBus` class; class-typed event args — ADR-010

**Performance**: no tick-time impact expected — the write runs off the tick thread via `ITickCompletionQueue`; one delegate set and one pending record per attempt (a player-initiated, NPC-gated action).

---

## Decisions (`/story-readiness` 2026-10-16, user-approved)

1. **D1 — `BeginAttempt` runs in `validateRequest`.** The coordinator's order is validate → close gate → acknowledge → compute. `BeginAttempt` (CR-ENH-15 steps 2–6a, scroll consumption included) can still reject at step 4, so it runs as the validation: `validateRequest` = `BeginAttempt(...)` is `Pending` (the pending start is kept); `computeAndApplyOutcome` returns that kept start. A rejected start → `Begin` returns `RejectedInvalidRequest`: no acknowledgment, no write, no gate change, and the caller learns the rejection code to send (Story 015). The outcome is applied a moment before the gate closes, inside the same synchronous call — no request can interleave. No Networking Core change. This satisfies Story 015's "a `Rejected*` result never follows an acknowledgment".
2. **D2 — the clientId comes through the seam.** `IEnhancementAttemptStarter.StartAttempt` gains a `uint clientId` parameter; `EnhancementClientRequestHandlers` passes `context.ClientId`. Story 010's tests change for the signature only.
3. **D3 — the durable `LastEnhancementRequestID` is out of scope.** EC-NET-9 wants it in the same transaction as the outcome, but `CharacterRecord` has no such field and `character-persistence.md` does not mention one. Logged as TD-069. The in-memory `EnhancementRequestDeduplicator` update stays with Story 015.
4. **D4 — closure against the in-memory store**, as Leveling Story 016 and Character Persistence Story 009: the real coordinator, completion queue, mutation gate, persistence service and in-memory store. The database run is Character Persistence Story 016's.
5. **D5 — no release path for a pending attempt.** The write queue runs the enhancement write before a later logout or TTL save of the same character, and `Drain` runs callbacks in tracking order, so `CompleteAttempt` / `RollBackAttempt` always runs before the session-end release. EC-ENH-2 follows; one test shows it.

---

## Acceptance Criteria

*The GDD's persistence and alert mocks are restated against what exists (as in Character Persistence Story 009): the in-memory store with a held operation, or a controllable `ICharacterPersistence` fake; a log assertion for each alert. Unless a criterion says otherwise, the setup is the real `EnhancementService`, `InventoryService`, `IrreversibleOutcomeCoordinator`, `TickCompletionQueue`, `CharacterMutationGate` and `CharacterPersistenceService` over the in-memory store.*

- [ ] **One commit per attempt (AC-ENH-35)**: a pending attempt makes exactly one `SaveIrreversibleOutcome` call with trigger `EnhancementResult`; `Begin` returns `Started` and the character's gate is closed until the drain that handles the result.
- [ ] **Rejected attempt (D1)**: a request that `BeginAttempt` rejects (validation failure, or the step-4 scroll consumption forced to fail) → `Begin` returns `RejectedInvalidRequest`, the caller receives the rejection code, and there is no acknowledgment, no write, no draw and no gate change.
- [ ] **The committed record holds the outcome (AC-ENH-35)**: after a successful write, the stored record shows the item at its new enhancement level (or no item after a destruction) and one scroll fewer.
- [ ] **Success path**: with the write held open, `IsAttemptInProgress` is true, the item slot is locked, and no `OnEnhancementSuccess` / `OnEnhancementDestruction` is raised and the delivery callback is not called. When the write is released as `Success`, the next drain calls `CompleteAttempt` once and hands its `EnhancementAttemptResult` to the delivery callback once; the slot is unlocked.
- [ ] **Failure path (AC-ENH-23, AC-ENH-34)**: for `DatabaseError`, `ConcurrencyConflict` and `CharacterNotFound`, and for a faulted task, a cancelled task and a watchdog timeout → `RollBackAttempt` runs once (bag restored, slot unlocked); the delivery callback is not called and no outcome event is raised; the client is disconnected with `DisconnectReason.Other` once and the session preserved for `SESSION_TTL_SECONDS` once; the coordinator's critical alert and `CriticalEnhancementWriteFailed` are each logged once.
- [ ] **Write fails to start**: a `SaveIrreversibleOutcome` that throws synchronously → `Begin` returns `PersistenceFailed`, the same rollback, disconnect and preserve each happen once, and the gate is open again.
- [ ] **No retry (CR-NET-5.5)**: after any failure the persistence service has received exactly one `SaveIrreversibleOutcome` call for the attempt.
- [ ] **Throw after `BeginAttempt`**: if the acknowledgment delegate throws, the pending attempt is rolled back (`IsAttemptInProgress` false, bag restored, slot unlocked), no write starts, the gate is open, and the exception reaches the caller.
- [ ] **AC-ENH-13 / EC-ENH-2 (D5)**: a disconnect, and a clean logout started, while the write is in flight do not abort it: on `Success` the outcome stands (item at its new level, scroll gone, slot unlocked) and `CompleteAttempt` runs before the logout save's release; the stored record after the logout save holds the outcome.
- [ ] **Seam signature (D2)**: `IEnhancementAttemptStarter.StartAttempt` takes the `clientId`, the handler passes `context.ClientId`, and Story 010's handler tests pass with only the signature change.
- [ ] **No await**: a source search of the files this story adds or edits finds no `await`, `async`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.

---

## Implementation Notes

- **New**: `src/ServerLogic/EnhancementSystem/EnhancementCommitOrchestrator.cs` — constructed over `IIrreversibleOutcomeCoordinator`, `EnhancementService`, `ICharacterPersistence` and the two injected failure steps (`Action<uint, DisconnectReason> disconnectClient`, `Action<uint, int> preserveSessionForTtl`), as `CoordinatedLevelUpCommit` (Leveling Story 016). One entry point per attempt taking `clientId`, `charId`, the two slot indices, an acknowledgment callback and a delivery callback (`Action<EnhancementAttemptResult>`); it returns the `IrreversibleOutcomeBeginResult` and, on `RejectedInvalidRequest`, the rejection code. Exact signature is a code-review choice.
- Delegates passed to `Begin`: `validateRequest` = `BeginAttempt` is `Pending` (D1; the start kept in a local captured by the closures, not a service field — one attempt per call, see Leveling Story 016's cross-entity snapshot bug); `emitAcknowledgment` = the caller's acknowledgment callback; `computeAndApplyOutcome` = return the kept start; `startWrite` = `IrreversibleOutcomePersistStep.StartWrite<EnhancementAttemptStart>(persistence, charId, IrreversibleOutcomeTrigger.EnhancementResult)`; `isSuccess` = `IrreversibleOutcomePersistStep.IsSuccess`; `deliverOutcome` = `CompleteAttempt(charId)` then the delivery callback; `revertOnFailure` = `RollBackAttempt(charId)`.
- **Throw after `BeginAttempt`**: wrap `Begin` so that an exception escaping it while a pending attempt is stored and no write was started calls `RollBackAttempt` before rethrowing. When the write did start (`PersistenceFailed` or an off-tick-thread call), the coordinator has already run `revertOnFailure`; do not roll back twice (`RollBackAttempt` throws on no pending attempt — guard with `IsAttemptInProgress`).
- TD-061 (a throwing `isSuccess` leaves the outcome applied) cannot fire here: `IrreversibleOutcomePersistStep.IsSuccess` is a plain enum comparison.
- `CompleteAttempt` raises the outcome events (Story 007), so "nothing is announced before the commit" follows from calling it only in `deliverOutcome` — verified end to end by the success-path criterion.
- `RollBackAttempt` logs `CriticalEnhancementWriteFailed`; the coordinator's failure protocol logs its own critical alert. Both are expected — assert both with `LogAssert`.
- **D2 seam change**: `src/ServerLogic/EnhancementSystem/IEnhancementAttemptStarter.cs` and the call in `EnhancementClientRequestHandlers.cs`; update the test fakes that implement the interface.
- The production `IEnhancementAttemptStarter` (acknowledgment message, result messages, the inventory-update hold, the in-memory dedup update) is Story 015's, built on this orchestrator.
- `SESSION_TTL_SECONDS` is the provisional constant on `CommitBeforeBroadcastSequencer`, passed by the coordinator.

---

## Out of Scope

- Story 004 / Story 005: the service's own behaviour in each phase and its rollback
- Story 007: the events themselves
- Story 015: the production `IEnhancementAttemptStarter`, everything a client receives (`EnhancementRequestReceived`, `EnhancementAttemptResult`, `ServerBroadcast_Enhancement9`, the held `InventorySlotUpdate`s), "not re-sent on reconnect" (AC-ENH-13 delivery half), and the in-memory `LastEnhancementRequestID` update
- The durable `LastEnhancementRequestID` in the same transaction (EC-NET-9) — TD-069 (D3)
- Character Persistence Stories 012–016: the database-backed store
- OQ-CP-1 (`DisconnectReason.PersistenceFailure`)

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitOrchestration_integration_tests.cs`

- **One commit, right trigger** — pending attempt → one `SaveIrreversibleOutcome(EnhancementResult)`; `Begin` = `Started`; gate closed until the handling drain.
- **Rejected, no ack** — tier mismatch → `RejectedInvalidRequest` with that code; zero acks, zero writes, zero draws (random-provider fake count unchanged), gate open. Same with `ConsumeItem` forced to fail at step 4.
- **Stored record** — success run: stored record slot at level 3, scroll count one lower; destruction run: no item in the slot.
- **Held then success** — held: in progress, slot locked, no events, no delivery; release `Success`, drain → `{Success, 3}` delivered once, after `OnEnhancementSuccess`; slot unlocked.
- **Each failure** — `[TestCase]` over `DatabaseError`, `ConcurrencyConflict`, `CharacterNotFound`, fault, cancellation, watchdog timeout → bag restored, slot unlocked, zero deliveries, zero outcome events, one disconnect (`Other`), one preserve (TTL), both critical logs, one write call.
- **Start throws** — persistence fake throws synchronously → `PersistenceFailed`, same failure assertions, gate open.
- **Ack throws** — acknowledgment callback throws → exception surfaces; not in progress, bag restored, slot unlocked, zero writes, gate open.
- **Logout during the write** — held write, `BeginCleanLogout` → release `Success`, drain(s) → `CompleteAttempt` ran before the release; stored record holds the outcome.
- **Seam** — handler test passes `context.ClientId` to the starter fake.
- **Source scan** — no `await` / `async` / `.Result` / `.Wait(` / `GetResult(` / `ContinueWith` in the touched files.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitOrchestration_integration_tests.cs` — must exist and pass; the full EditMode suite passes.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Enhancement Stories 004, 005, 007, 010 (Complete); Character Persistence Stories 008, 009, 010 (Complete); Networking Core Stories 030, 031 (gate, coordinator — Complete)
- Unlocks: Story 015 (result delivery; split out of Story 010 on 2026-10-09)
