# Story 010: Session-End Triggers — Clean Logout and TTL Expiry

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (3 open decisions, listed below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (clean logout and session TTL expiry triggers); CR-CP-10 steps 4 (session release, `UnregisterCharacter`), 9, 10; CR-CP-11; the States and Transitions table; AC-CP-16. `design/gdd/networking-core.md` — CR-NET-2 (the 300-second session TTL).
**Requirement**: `TR-cp-004`, `TR-cp-011`

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

- [ ] **AC-CP-16**: when a disconnected session's TTL expires, `SaveSession(id, SessionTTLExpiry, ct)` is called once; with the in-memory store, the record's `LastSavedAtUtc` is new and its `SaveVersion` is incremented. The test injects the TTL (or the tick count that stands for it); it does not wait 300 seconds.
- [ ] **Clean logout**: a player-initiated logout calls `SaveSession(id, CleanLogout, ct)` once.
- [ ] **Release after success**: after `Success`, for either reason, the session's resources are released and `IInventoryService.UnregisterCharacter(charId)` is called once.
- [ ] **CleanLogout failure (CR-CP-10 step 9)**: on `DatabaseError` the session is not torn down, `UnregisterCharacter` is not called, and the failure is returned to the logout caller so the client can retry.
- [ ] **TTL-expiry failure (step 10)**: on any non-`Success` the session's resources are released regardless, with one critical log (from the save itself); `UnregisterCharacter` is called.
- [ ] **Logout then TTL (EC-CP-4)**: a clean logout followed by a TTL expiry for the same character does not release the session twice and does not call `UnregisterCharacter` twice; the second save, if it is issued at all, ends with `ConcurrencyConflict` and a WARNING.
- [ ] **After a failed irreversible save (CR-CP-5 step 5)**: the session preserved by the coordinator is saved by the TTL expiry like any other, with its rolled-back state.
- [ ] **No await**: the trigger code contains no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.
- [ ] **Shutdown**: a save in flight when the zone shuts down gets its callback from the shutdown drain (ADR-011 Decision 2); the session is released once.

---

## Implementation Notes

- **From Story 007 readiness (2026-10-15):** the service keeps a session table (`CharacterID → { EntityID, identity fields, gear, SaveVersion }`, filled by `ApplyLoadedRecord`). Releasing a session after its save must remove the character's entry (a new service method — decide its name here), alongside `Inventory.UnregisterCharacter`.
- `SESSION_TTL_SECONDS = 300` is declared in `CommitBeforeBroadcastSequencer`; the connection state machine and the ghost-session cleanup hold the session side (`src/ServerLogic/Networking/ConnectionStateMachine/`, `GhostSession/`). Read them before deciding where the trigger lives.
- CR-CP-11 keeps the two reasons apart: a failed logout save is the caller's to retry; a failed TTL save releases anyway — the player's unsaved session state is lost, which is why the save logs a critical error.
- The `ZoneClosed` reason is called by Zone Instancing's teardown (CR-ZI-12 step 2), which has no code; this story does not add that caller.

**Open decisions for `/story-readiness`**

1. **Where the trigger lives**: in the connection state machine / ghost cleanup sequencer (Networking Core, a Complete epic) or in a new session-end component of this epic that they call.
2. **Clean logout has no request today.** Whether a logout request message exists in the wire protocol, and who handles it; if not, this story covers the TTL trigger and a callable entry point only.
3. **What "release the session's resources" is** besides `UnregisterCharacter`: Character Stats, Currency, Leveling state for the character, and the entity. The GDD names the inventory call only.

---

## Out of Scope

- Story 007: `SaveSession` itself
- Zone Instancing's teardown saves (`ZoneClosed`, the HP override)
- Reconnect within the TTL (Networking Core's session contract)
- Story 016: AC-CP-16 against PostgreSQL

---

## QA Test Cases

- **TTL expiry** — Given a disconnected session and an injected TTL, When it expires, Then one `SaveSession(SessionTTLExpiry)`, version + 1, one `UnregisterCharacter`.
- **Clean logout** — Then one `SaveSession(CleanLogout)`, release after Success.
- **Logout failure** — Given a failing store, Then the session is still loaded, zero `UnregisterCharacter`, the caller receives `DatabaseError`. Edge: a retry that succeeds releases once.
- **TTL failure** — Given a failing store, Then the session is released, one `UnregisterCharacter`.
- **Double trigger** — Logout then TTL → one release.
- **After rollback** — Given a preserved session with rolled-back Level 10, When the TTL expires, Then the stored Level is 10.
- **Shutdown** — Given a save in flight, When the shutdown drain runs, Then the callback runs once.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SessionEnd_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007; Story 009 for the "after a failed irreversible save" criterion
- Unlocks: Story 016
