# Story 010: Session-End Triggers — Clean Logout and TTL Expiry

> **Epic**: Character Persistence
> **Status**: Complete (2026-10-15) — written 2026-10-12; `/story-readiness` 2026-10-15 (3 decisions settled, below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (clean logout and session TTL expiry triggers); CR-CP-10 steps 4 (session release, `UnregisterCharacter`), 9, 10; CR-CP-11; the States and Transitions table; AC-CP-16. `design/gdd/networking-core.md` — CR-NET-2 (the 300-second session TTL).
**Requirement**: `TR-cp-004`, `TR-cp-010`, `TR-cp-011`

**ADR Governing Implementation**: ADR-011 Decisions 1 and 2 (the save is started from tick code and tracked; its result is handled in `Drain`; the shutdown drain).
**ADR Decision Summary**: session-end saves are started, not awaited; what follows the save (release or keep the session) happens in the completion callback on the tick thread.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the task, in the same statement, to `ITickCompletionQueue.Track` — ADR-011
- Forbidden: `await` in tick-driven code — ADR-011
- Guardrail: the result of a persistence call is handled by `Drain` on the first tick after its task completes — ADR-011

---

## Acceptance Criteria

"Release" below means, in this order: the persistence service's session-table entry is removed (the new service method), `IInventoryService.UnregisterCharacter(charId)`, `LevelingService.UnregisterPlayerEntity(entityId)`, then the existing `ConnectionStateMachine` method (`ProcessExplicitDisconnect` for a logout, `CompleteSessionActiveTTLExpiry` for a TTL expiry) called with a no-op persist delegate, which removes the entity, broadcasts `PlayerLeftZone` and removes the session. Each of these runs exactly once per release.

- [x] **AC-CP-16**: when a `Disconnected_SessionActive` session's TTL expires, `SaveSession(id, SessionTTLExpiry, ct)` is called once and its task is passed to `Track`; with the in-memory store, the record's `LastSavedAtUtc` is new and its `SaveVersion` is incremented. The test injects the expiry tick; it does not wait 300 seconds.
- [x] **Clean logout entry point**: `BeginCleanLogout(accountId, onResult)` on a `Connected` session calls `SaveSession(id, CleanLogout, ct)` once and passes its task to `Track`. Nothing is released before the save completes.
- [x] **Release after success**: after `Success`, for either reason, the release runs once (every step above once) and, for a logout, `onResult` receives `Success` once.
- [x] **CleanLogout failure (CR-CP-10 step 9)**: on `DatabaseError` (and on a fault, cancellation or watchdog timeout) no release step runs, the session stays `Connected`, and `onResult` receives the failure once so the client can retry. A retry that then succeeds releases once.
- [x] **TTL-expiry failure (step 10)**: on any non-`Success` the release runs regardless, with one critical log (from the save itself).
- [x] **No double end (EC-CP-4)**: while a session end is in flight for a character, a second `BeginCleanLogout` or a TTL expiry for the same character starts no second save and runs no second release; a logout started after a failed logout's callback has run starts a new save.
- [x] **TTL save in flight**: the TTL save starts on the first tick at or after the expiry tick, and only for a session that is `Disconnected_SessionActive` on that tick. From then on the reconnect window is closed (Networking Core refuses a reconnect after the expiry tick), so the session is still `Disconnected_SessionActive` when the callback runs; the callback calls `CompleteSessionActiveTTLExpiry` with the drain tick and the same expiry tick, and its expiry check passes. A test holds the save across several ticks and asserts the release runs once, on the drain after completion.
- [x] **After a failed irreversible save (CR-CP-5 step 5)**: the session preserved by the coordinator is saved by the TTL expiry like any other, with its rolled-back state.
- [x] **No await**: the session-end code contains no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.
- [x] **Shutdown**: a save in flight when the zone shuts down gets its callback from the shutdown drain (ADR-011 Decision 2); the release runs once.

---

## Implementation Notes

- **From Story 007 readiness (2026-10-15):** the service keeps a session table (`CharacterID → { EntityID, identity fields, gear, SaveVersion }`, filled by `ApplyLoadedRecord`). Releasing a session after its save must remove the character's entry (a new service method — decide its name here), alongside `Inventory.UnregisterCharacter`.
- `SESSION_TTL_SECONDS = 300` is declared in `CommitBeforeBroadcastSequencer`; the connection state machine and the ghost-session cleanup hold the session side (`src/ServerLogic/Networking/ConnectionStateMachine/`, `GhostSession/`). Read them before deciding where the trigger lives.
- CR-CP-11 keeps the two reasons apart: a failed logout save is the caller's to retry; a failed TTL save releases anyway — the player's unsaved session state is lost, which is why the save logs a critical error.
- The `ZoneClosed` reason is called by Zone Instancing's teardown (CR-ZI-12 step 2), which has no code; this story does not add that caller.

**Decisions (`/story-readiness` 2026-10-15)**

1. **Where the trigger lives: a new session-end component in this epic; Networking Core is not edited.** `ConnectionStateMachine.ProcessExplicitDisconnect` (AC-NC-26) and `CompleteSessionActiveTTLExpiry` (AC-NC-12) already hold the release sequence, but they call a synchronous `persistFinalCharacterState` delegate and then remove the session unconditionally — that breaks ADR-011 (release in the `Drain` callback) and CR-CP-10 step 9 (a failed logout save keeps the session). The component starts `SaveSession`, passes the task to `Track`, and in the completion callback calls the state-machine method with a no-op persist delegate (the save has already happened, so persist-before-remove still holds). On a failed logout save it does not call the state machine; the session stays `Connected`. The component tracks one in-flight session end per character (EC-CP-4). The ghost-session paths (`GhostCleanupSequencer`) are not changed.
2. **Clean logout: callable entry point only.** The wire protocol has no client-to-server logout or disconnect message (`DisconnectType.Graceful` is only a value of the `PlayerLeftZone` broadcast). The story adds `BeginCleanLogout(accountId, onResult)`; `onResult` (called from `Drain`) is how the failure is "returned to the logout caller". The inbound message and its handler are a later story.
3. **Release = persistence session entry + Inventory + Leveling + the state machine's own release.** The persistence service gets a method removing the character's session-table entry (name chosen at implementation; it is a no-op for an unknown character). `IInventoryService.UnregisterCharacter` and `LevelingService.UnregisterPlayerEntity` exist. Character Stats and Currency have no per-character release method today — logged as TD-066, not added here. The entity is removed by the state machine's `removeEntityFromZone` delegate.

**Performance:** no impact expected — one save started per session end; the release runs once, in `Drain`.

---

## Out of Scope

- Story 007: `SaveSession` itself
- Zone Instancing's teardown saves (`ZoneClosed`, the HP override)
- Reconnect within the TTL (Networking Core's session contract)
- Story 016: AC-CP-16 against PostgreSQL
- The inbound logout message, its wire-protocol entry and handler (decision 2)
- Changes to `ConnectionStateMachine` or `GhostCleanupSequencer` (decision 1)
- Per-character release in Character Stats and Currency (TD-066)

---

## QA Test Cases

- **TTL expiry** — Given a disconnected session and an injected expiry tick, When it expires, Then one `SaveSession(SessionTTLExpiry)`, version + 1, one release (session entry gone, one `UnregisterCharacter`, one `UnregisterPlayerEntity`, state machine transition `TTLExpired`). Edge: the tick before expiry starts nothing.
- **Clean logout** — Then one `SaveSession(CleanLogout)`; no release before the drain; release and `onResult(Success)` after it.
- **Logout failure** — Given a failing store, Then the session is still `Connected` and loaded, zero release steps, `onResult` receives `DatabaseError`. Edge: a retry that succeeds releases once.
- **TTL failure** — Given a failing store, Then the session is released once.
- **Double trigger** — Two `BeginCleanLogout` calls before the drain → one save, one release.
- **Held TTL save** — Given the TTL save held across several ticks, Then the release runs once, on the drain after completion.
- **After rollback** — Given a preserved session with rolled-back Level 10, When the TTL expires, Then the stored Level is 10.
- **Shutdown** — Given a save in flight, When the shutdown drain runs, Then the callback runs once.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SessionEnd_tests.cs` — must exist and pass.

**Status**: [x] Created — 28 cases (22 `[Test]` methods plus two `[TestCase]` methods run over 3 failure kinds each), all passing

---

## Dependencies

- Depends on: Story 007; Story 009 for the "after a failed irreversible save" criterion (both Complete); Networking Core's `ConnectionStateMachine` (Complete epic, used unchanged)
- Unlocks: Story 016

---

## Completion Notes

**Completed**: 2026-10-15
**Criteria**: 10/10 passing (all automated; "No await" by source scan of `SessionEndCoordinator.cs`)
**Deviations** (advisory):
- `ConnectionStateMachine.TryGetCharacterId` (read-only, no behavior change) was added. It is the one user-approved exception to "no `ConnectionStateMachine` changes"; nothing else maps an account to its character.
- The session-table release is `ICharacterSessionRelease.ReleaseSession(charId, out entityId)`, implemented by `CharacterPersistenceService`. It is kept off `ICharacterPersistence`, whose method set is the CR-CP-1 contract enforced by the contract tests.
- Nothing in Networking Core refuses a reconnect after the expiry tick, which the "TTL save in flight" criterion assumed. The completion callback therefore re-checks the session state; if it changed, no release step runs and one error is logged. Logged as TD-067.
- The TTL callback cannot see the drain tick, so it passes the expiry tick as both ticks to `CompleteSessionActiveTTLExpiry` (an equal tick counts as expired).
- Choices made at implementation:
  - A fault, cancellation or watchdog timeout on a logout save is reported as `DatabaseError`.
  - A second logout while one is in flight returns false and its `onResult` is never called.
  - A release step that throws is logged once and the remaining steps are skipped; a logout still reports Success.
  - A timed-out save's token source is not disposed.
  - Leveling's `UnregisterPlayerEntity` is injected as an `Action<EntityID>`, because `ILevelingService` does not declare it.
**Test Evidence**: Integration — `tests/EditMode/CharacterPersistence/CharacterPersistence_SessionEnd_tests.cs` (28 cases); EditMode 2865 / 2865 in batch mode
**Code Review**: Complete — CHANGES REQUIRED, all 6 items fixed (token source kept for a timed-out save, exception-safe release, token source disposed on a synchronous start failure, character-id assert, `onResult` docs, 11 added test cases)
