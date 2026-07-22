# Story 011: Commit-Before-Broadcast Generic Pattern

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-core.md`
**Requirement**: `TR-net-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-001-purchase-transaction-integrity.md`
**ADR Decision Summary**: ADR-001's `PendingPurchase` record pattern is the concrete instance of commit-before-broadcast for NPC Shop; this story implements the *generic* mechanism CR-NET-5 describes so any future irreversible-outcome system (Enhancement, Respec, NPC Shop) can compose with it, without duplicating the ordering guarantee per system.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (server-side ordering logic, no engine API)
**Engine Notes**: None — pure C# sequencing.

**Control Manifest Rules (Foundation layer)**:
- Required: any outcome that cannot be reversed (item enhancement destruction, item consumption, level-up stat writes, gold mutation) must be fully committed to persistence before any message describing the outcome is transmitted to any client — source: CR-NET-5.1
- Required: if a `SaveIrreversibleOutcome` write fails, no outcome message is emitted; no retries (not idempotent, retry risks duplicate commit); caller reverts in-memory mutations; client disconnected; session preserved for `SESSION_TTL_SECONDS` — source: CR-NET-5.5

---

## Acceptance Criteria

*From `design/gdd/networking-core.md`, scoped to this story — implemented against a generic mock "irreversible outcome" seam, not real Enhancement/Respec business logic:*

- [x] **AC-NC-09** [BLOCKING]: Given a simulated database write that takes 200ms to confirm, when a mock irreversible-outcome request is submitted, then the client does not receive the outcome message and does not play any outcome-dependent behavior until at least 200ms after submission, verified by comparing client-side receipt timestamp against server-side write confirmation timestamp.
- [x] **AC-NC-15** [BLOCKING]: Given test instrumentation (`IServerCrashInjector` delay variant) that delays the persistence write by 500ms, when a mock irreversible-outcome request is submitted, then the client does not receive the outcome until at least 500ms after submission.
- [x] **AC-CBB-1** [BLOCKING] (write-failure protocol, CR-NET-5.5): Given a simulated persistence write failure, when the generic commit-before-broadcast helper processes it, then: no outcome message is emitted; the caller's revert callback is invoked (caller-owns-rollback); the client is disconnected (`DisconnectReason.Other`); the session is preserved for `SESSION_TTL_SECONDS`; a critical infrastructure alert fires.
- [x] **AC-CBB-2** [BLOCKING] (acknowledgment vs outcome distinction, CR-NET-5.3 generic form): Given a request-acknowledgment message configured to fire before outcome computation, when the mock flow runs, then the acknowledgment is sent immediately upon request validation (before persistence write begins) and is distinguishable from the outcome message (a "processing" signal, not a result).

---

## Implementation Notes

*Derived from CR-NET-5:*

- Implement as a generic helper: `CommitBeforeBroadcast<TOutcome>(Func<TOutcome> computeOutcome, Func<TOutcome, bool> persistOutcome, Action<TOutcome> broadcastOutcome, Action revertOnFailure)` (or equivalent) — the caller supplies its own compute/persist/broadcast/revert delegates; this story's job is guaranteeing the *ordering* (compute → persist → confirm → broadcast, never broadcast before confirm), not the domain logic inside each delegate.
- Sequence (mirrors CR-NET-5.3's enhancement example, generalized): (1) validate request, reject immediately if invalid; (2) emit a "request received/processing" acknowledgment — before computing the outcome; (3) compute the outcome; (4) write atomically to persistence; (5) only after write confirms durable, emit the outcome message; consumer plays outcome-dependent behavior only after receiving it.
- No retries on persistence failure — irreversible outcome writes are not idempotent; a retry risks a duplicate commit. On failure: caller reverts its own in-memory mutations (caller-owns-rollback — this helper does not know how to undo caller-specific state), disconnect the client, preserve the session for `SESSION_TTL_SECONDS` so a later `SaveSession` can write the rolled-back state, fire a critical alert.
- Test this entire pattern using `IServerCrashInjector`'s delay/crash-at-step variants (Story 001) against a mock outcome — do not build real Enhancement or Respec logic to prove this story's ACs.

---

## Out of Scope

*Handled by future epics:*

- Real Enhancement System outcome computation and its wire schema — future Enhancement System epic
- Real Respec Phase 1/2 flow (CR-NET-5.6) — future Character Stats/Leveling epic
- NPC Shop's `PendingPurchase` record and reconciliation-on-reconnect — already implemented at the Currency System layer (`AddGold(CompensatingRefund)`, per ADR-001 Decision 3/4); this story does not re-implement that, it provides the generic pattern NPC Shop's future epic will compose with

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs`

- **AC-NC-09**: Given a 200ms-delayed mock write, then the outcome is observed no earlier than 200ms after submission.
- **AC-NC-15**: Given a 500ms-delayed mock write via `IServerCrashInjector`, then the outcome is observed no earlier than 500ms after submission.
- **AC-CBB-1**: Given a simulated write failure, then no outcome fires, revert callback runs, disconnect occurs, session preserved for `SESSION_TTL_SECONDS`, critical alert logged.
- **AC-CBB-2**: Given a valid request, then the acknowledgment fires before the outcome computation begins, and is a distinguishable message type from the outcome.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs` — must exist and pass

**Status**: [x] Created — 14 test methods, all 4 blocking ACs covered (see Completion Notes)

---

## Dependencies

- Depends on: Story 001 (`IServerCrashInjector`), Story 006 (priority-path — outcome/acknowledgment messages are cap-exempt enhancement-path messages when a real system adopts this pattern)
- Unlocks: Future Enhancement System, Leveling System (respec), and NPC Shop epics

---

## Completion Notes

**Completed**: 2026-07-17
**Criteria**: 4/4 passing (AC-NC-09, AC-NC-15, AC-CBB-1, AC-CBB-2) — no deferred items
**Deviations**:
- ADVISORY: `SESSION_TTL_SECONDS` (300, tuning range [60,600]) introduced in code for the first time as a provisional `public const int` on `CommitBeforeBroadcastSequencer` — matches the `ServerTickLoop.TICK_RATE_HZ` precedent (Story 009). Real ownership belongs to a future Networking Core session-lifecycle story (CR-NET-2).
- ADVISORY: TR-net-002 registry gap — `docs/architecture/tr-registry.yaml` has no populated entries; same pre-existing systemic gap documented in every Networking Core story since 003. Implementation proceeded against this story's own embedded Requirement text and `networking-core.md` CR-NET-5 directly.
- ADVISORY: The story's own Implementation Notes referenced an `IServerCrashInjector` "delay/crash-at-step variant" that doesn't exist on the real interface (it only has `RegisterCrashAt(CrashStep)`/`ClearRegistered()`, which simulates a process crash, not a slow/failing write). AC-NC-09/AC-NC-15's delays and AC-CBB-1's failure are instead simulated directly through the `persistOutcome` delegate's own contract (real blocking delay / `false` return / thrown exception) — the mechanism the delegate signature already exists to express.
- ADVISORY: A real GDD inconsistency was found and fixed during `/code-review` — `character-persistence.md`'s CR-CP-11 comparison table stated the write-failure order backwards (alert after session-preservation) relative to CR-CP-5's own authoritative numbered protocol (alert before session-preservation). Corrected (1 line); the numbered list itself was already correct.
- OUT OF SCOPE (justified): `design/gdd/character-persistence.md` was touched for the fix above, outside this story's originally listed file scope — matches this epic's established pattern (Stories 005, 009) of correcting found doc inconsistencies inline.
**Test Evidence**: Logic — `tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs`, 14 test methods. Not yet run in a real Unity Editor (no compiler available in this sandboxed session — same limitation as every prior story in this epic).
**Code Review**: Complete — `/code-review` (lean mode, unity-specialist + qa-tester in parallel). Verdict: APPROVED WITH SUGGESTIONS. Zero BLOCKING findings, including a clean pass on the release-stripping guard around the new `observer` parameter (the exact defect class found in 3 files during Story 010's review). 4 suggestions surfaced and all fixed in the same session: (1) hardened `Execute<TOutcome>` with a try/catch so a thrown `persistOutcome` exception routes into the identical CR-NET-5.5/CR-CP-5 write-failure protocol as a `false` return, rather than silently bypassing it — plus a new regression test; (2) added a literal `SESSION_TTL_SECONDS == 300` assertion; (3) added a doc-comment note on the delegate-seam-vs-descriptor-struct design choice; (4) `.meta` file gap left alone (expected, no Editor available).
