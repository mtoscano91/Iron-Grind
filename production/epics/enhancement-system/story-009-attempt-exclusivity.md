# Story 009: Attempt Exclusivity — Held Requests

> **Epic**: Enhancement System
> **Status**: Complete (2026-10-09; `/story-readiness` 2026-10-09: rewritten from the pre-decision placeholder; ADR-014 Migration Plan step 3)
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 h

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-18 (attempt exclusivity) and AC-ENH-38 (two cases, wording of 2026-10-09). OQ-ENH-7 is resolved by ADR-011. Related: CR-ENH-15 Rollback (which relies on this rule), character-persistence.md CR-CP-5.
**Requirement**: `TR-enh-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**:
- **ADR-011: Asynchronous Persistence in the Server Tick Loop (Accepted 2026-10-07)**, Decision 4: a per-character `ICharacterMutationGate` is closed while an irreversible write is in flight; a request whose type is marked `HeldDuringIrreversibleWrite` is held (up to `MAX_HELD_REQUESTS_PER_CHARACTER` = 16); after a failed write the client is disconnected and its held requests are discarded.
- **ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09)**, Decisions 4–6: the dispatcher checks the gate once per request and appends to the character's hold queue; Pass A releases held requests in arrival order before Pass B takes new ones; `Drain` runs before `DispatchTick` in one tick.

**ADR Decision Summary**: Tick code never awaits a persistence task; the result is handled on the tick by a completion queue, and the request dispatcher holds a character's bag-mutating requests behind a per-character gate until then.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved. The fake write is a `TaskCompletionSource`; the test completes it and then calls `Drain`. It never awaits.

**Control Manifest Rules**:
- Required: the inbound request dispatcher is the single enforcement point; it checks the gate once per request — ADR-011, ADR-014
- Required: a held type is R-OD, carries no rate-limited `RpcTypeTag`, and has `MaxBodyBytes` of at most `MAX_HELD_BODY_BYTES` (64) — ADR-014
- Forbidden: reading `ICharacterMutationGate` from a game system or from a handler — ADR-011, ADR-014
- Forbidden: holding, rejecting or deferring a client request inside a game system because a write is in flight — ADR-011
- Forbidden: `await` in a handler; keeping `body` after a handler returns — ADR-014

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [x] **CR-ENH-18**: while a character's enhancement write is in flight, a held-type client request for that character does not run: `HeldCount` rises by one per request and the bag is unchanged. (Server-originated pickup is not covered here — see Out of Scope.)
- [x] **Held, then released in arrival order**: after a successful write, the held requests run in arrival order on the tick whose `Drain` delivered the outcome, before any new request of that tick, each with `WasHeld` true.
- [x] **AC-ENH-38, both cases**: Bronze item at +4 in slot 0; a Bronze scroll stack of 2 in slot 1; slots 2–19 occupied; destruction injected; the write is held open. While it is in flight the client sends a move of slot 2 → slot 0 (it stands in for the Helmet unequip until the Equipment System exists; like the unequip, it would take the freed slot 0). → While in flight, `IsAttemptInProgress` is true and slot 2 is unchanged.
  - **Case A (the write returns `DatabaseError`)**: the rollback restores the item at level 4 and the scroll stack to 2 with no `CriticalEnhancementRollbackFailed` logged; the client is disconnected; the move never runs; `HeldCount` is 0 after the next `DispatchTick`.
  - **Case B (the write succeeds)**: the destruction outcome is delivered first; the move runs on that same tick, and the item from slot 2 is in slot 0.
- [x] **Second attempt is rejected, not held**: a `BeginAttempt` during the window returns `RejectedConcurrentAttempt` (Story 005); `HeldCount` is unchanged.
- [x] **Other characters are unaffected**: another character's held-type request runs on the tick it arrives.
- [x] **No production source change**: `git diff` on `src/` is empty.

---

## Implementation Notes

- One new test file, plus doubles added to `EnhancementTestDoubles.cs` if needed. The dispatcher harness and doubles in `tests/EditMode/Networking/InboundDispatchTestDoubles.cs` are in the same test assembly and are reused.
- The fixture composes: `InboundDispatchHarness` with a real `CharacterMutationGate`; `TickCompletionQueue`; `IrreversibleOutcomeCoordinator` (queue, gate); `EnhancementService` over a real `InventoryService`.
- `coordinator.Begin` receives: `computeAndApplyOutcome` → `EnhancementService.BeginAttempt`; `startWrite` → the `TaskCompletionSource` task; `deliverOutcome` → `CompleteAttempt`; `revertOnFailure` → `RollBackAttempt`; `disconnectClient` → `dispatcher.RemoveConnection`.
- The held requests are two test-only message types, move and discard: R-OD, client→server rows in `InboundFakeRoutingTable`, registered with `HeldDuringIrreversibleWrite` true. Their handlers decode slot indices from the body and call `InventoryService.Move` / `Discard`. No bag request has a message type or a handler in production code yet.
- Each tick of the test calls `queue.Drain(tick)` and then `dispatcher.DispatchTick(tick)` — the order of ADR-014 Decision 6.
- The second attempt is a direct `BeginAttempt` call: the client `ConfirmEnhancement` message belongs to Story 010.
- When the Equipment System lands, the unequip case of AC-ENH-38 is added there.

---

## Out of Scope

- Story 004: `IsAttemptInProgress`
- Story 005: the rollback and the `RejectedConcurrentAttempt` rule for a second attempt
- Story 011: the production orchestration between `BeginAttempt` and `CompleteAttempt` / `RollBackAttempt` (this story's fixture composes the coordinator itself)
- Story 010: the client `ConfirmEnhancement` message
- The real bag request message types and their handlers (Inventory and Equipment epics), including the Helmet unequip of AC-ENH-38
- Server-originated pickup deferral: no bag mutator reads the gate yet; it belongs to a Loot or Inventory story
- Networking Core Story 036: the request dispatcher itself (Complete)

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptExclusivity_integration_tests.cs` (to be created).

- **AC-ENH-38 case A** — as stated in the criterion.
- **AC-ENH-38 case B** — as stated in the criterion.
- **Held move** — write held open; a move for another slot of the same character arrives → not applied while in flight, `HeldCount` 1; applied after the write succeeds.
- **Arrival order** — two held requests whose result depends on order (move 2 → 0, then discard slot 0) are applied in arrival order.
- **Second attempt is rejected, not held** — `RejectedConcurrentAttempt` immediately; `HeldCount` unchanged.
- **Other character** — a move for a different character during the window is applied on the tick it arrives.
- **After the gate opens** — a request that arrives after the outcome was delivered runs with `WasHeld` false.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptExclusivity_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 7 tests

---

## Dependencies

- Depends on: Story 004 (attempt sequence) and Story 005 (rollback); Networking Core Story 036 (request dispatcher) and Story 037 (tick order) — all Complete. No longer depends on Story 011 or on the OQ-ENH-7 decision (ADR-011).
- Unlocks: closes the assumption the Story 005 rollback relies on

---

## Completion Notes

**Completed**: 2026-10-09
**Criteria**: 6/6 passing. AC-ENH-38 is covered in both cases.
**Test Evidence**: Integration — `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptExclusivity_integration_tests.cs`, 7 tests. First run 2026-10-09 15:26 local: EditMode 2210/2210, this fixture 7/7, no compile error (read from `TestResults.xml`). The run after the review fixes is **reported by the user ("all green"), not confirmed from disk**: the fixed file compiled at 15:31 with no `error CS`, but `TestResults.xml` still held the 15:26 run when the story was closed.
**Code Review**: Complete — `/code-review` (`unity-specialist` + `qa-tester`; ADR-011 / ADR-014 check by the main session): APPROVED WITH SUGGESTIONS, nothing blocking; all seven suggestions applied (user decision 2026-10-09). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
**Scope**: one new test file and its `.meta`; `git diff --stat src/` is empty.
**Deviations** (advisory):
- AC-ENH-38 runs with a bag move (slot 2 → slot 0) standing in for the Helmet unequip (user decision at `/story-readiness`); the unequip case is added when the Equipment System lands.
- No `DatabaseError` type exists in `src/`; the fixture defines a private `WriteResult { Committed, DatabaseError }` enum.
- Case A expects two error logs, in order: `[EnhancementService] CriticalEnhancementWriteFailed`, then `[IrreversibleOutcomeCoordinator] PersistenceWriteFailed`. The absence of `CriticalEnhancementRollbackFailed` is shown by `LogAssert.NoUnexpectedReceived()`.
- Case A asserts `HeldCount` 0 on the tick that delivers the failure (the disconnect happens in `Drain`, so that tick's `DispatchTick` discards the held move) and again one tick later.
- The held requests are test-only message types (0xE201 move, 0xE202 discard; `RpcTypeTag.SetTarget`, 2-byte bodies): no bag request has a message type or a handler in production yet.
- The second-attempt test goes beyond its criterion: after the rejection it completes the write and checks the outcome is delivered once and the held move runs once.
- `TR-enh-007` is not in `tr-registry.yaml` (registry entries are a separate open item).
**Tech debt**: none logged.
