# Story 012: Teardown Close Steps (CR-ZI-12 steps 1–8)

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-23) — written 2026-10-22; `/story-readiness` 2026-10-23 NEEDS WORK (9 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 10 hours (readiness 2026-10-23: three new seams with stand-ins, tracked-save ordering tests)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-12, EC-ZI-3, EC-ZI-11, EC-ZI-15, Dead/Respawning during Draining, AC-ZI-12, AC-ZI-23 (teardown side), AC-ZI-31; `networking-ghost-character-state.md` CGS-3 / CGS-4; `character-persistence.md` CR-CP-1 (`SaveSession` HP override)
**Requirement**: `TR-zi-012`, `TR-zi-014` *(epic placeholders)*

**ADR Governing Implementation**: ADR-009 Amendment 1 (teardown step 4 — the close steps — and its ordering note on `NotifyZoneExit`); ADR-006 (`SaveSession(ZoneClosed[, hpOverride])`); ADR-011 (tracked saves, drained before exit); ADR-010 (direct calls on injected interfaces)
**ADR Decision Summary**: the composition's teardown runs the CR-ZI-12 steps after navigation teardown (step 3) and before the outbound flush (step 5); saves are started as tracked tasks and the persistence drain (step 6) delivers their completions before NGO shuts down (step 7) and the process exits.

**Composition teardown facts this story builds on** (`src/ServerLogic/ZoneServer/ZoneServerComposition.cs`, `RunStepBody`): step 1 unsubscribes the tick — no tick runs during or after the close steps; step 4 calls `IZoneCloseSteps.RunCloseSteps()` (only if the zone state was entered); step 5 `FlushOutbound`; step 6 `ITickCompletionQueue.DrainOnShutdown(DRAIN_TIMEOUT)` waits for tracked tasks and runs their completion callbacks on the main thread; step 7 stops the transport and shuts NGO down, which closes every remaining connection.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (save ordering across tracked tasks)
**Engine Notes**: no engine API beyond what the composition already uses. NGO closes connections at teardown step 7 (Networking Core Story 042); this story calls no NGO or transport API.

**Control Manifest Rules (this layer)**:
- Forbidden: `await` / `.Result` / `ContinueWith` in tick-driven code — ADR-011 (saves go through `ITickCompletionQueue.Track`)
- Required: direct calls on injected interfaces (Mob Spawning, Party, session sink) — ADR-010
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Readiness Decisions (2026-10-23)

- **`NotifyZoneExit` ordering (user decision — save callback):** each step 2 `SaveSession` and step 3 ghost snapshot save is passed to `ITickCompletionQueue.Track`; `NotifyZoneExit` for that character is called from the completion callback, which runs inside `DrainOnShutdown` at teardown step 6. A save that completes with a failure, fault or cancellation still triggers `NotifyZoneExit` (the save has finished), with one `LogError` naming the character. A save still running when the drain times out gets no `NotifyZoneExit`; the drain's own timed-out alert covers it. The write-queue alternative was rejected: `CharacterWriteQueue` orders writes inside this process only, so it does not stop the Party System reading stale state.
- **Connection close (user decision — leave to teardown step 7):** step 4 queues `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=0)` **cap-exempt** and expires the session through the session sink, but closes nothing. The outbound flush (teardown step 5) sends it; NGO shutdown (step 7) closes the connections. Closing in step 4 would drop the queued message (same finding as Story 011's code review).
- **Throw policy:** each of the 8 steps runs in its own `try/catch` (`LogError` with the step number and exception type; the next step runs). Steps 2, 3, 5 and 6 also catch per slot, so one bad slot does not skip the others. A save call that throws synchronously counts as a finished failed save: `LogError`, and `NotifyZoneExit` for that character is called at once in step 5.
- **Added at `/code-review` 2026-10-23:** a slot that is both a ghost and entering is handled as a ghost (snapshot save, no step 4, notify after the snapshot). If `TryGetFormalParty` throws for a character, that character's `NotifyZoneExit` is lost (one `LogError`). If `ITickCompletionQueue.Track` throws, the character is notified at once in step 5.
- **Run once:** a `_ran` flag is set before step 1; a second `RunCloseSteps` returns at once and makes no calls.

---

## Acceptance Criteria

- [x] A `ZoneCloseSteps : IZoneCloseSteps` class replaces `StandInZoneCloseSteps` as the real implementation (the stand-in stays for the composition until Story 014 wires this one). Its constructor takes: the player slot pool and mob slot pool, `ZoneConnectionCharacterDirectory`, `ZoneTickRoster`, `ZoneEntryReclamation` (for `IsEntering`), `ICharacterPersistence`, `ITickCompletionQueue`, `IPartyZonePresence`, `IZoneMobSpawnTimers`, `IZoneTeardownSessionSink`, `IZoneDrainNoticeLog`, the outbound message sink, and the `ZoneID`. `RunCloseSteps` runs, in this order:
  1. `IZoneMobSpawnTimers.CancelAllSpawnTimers(ZoneID)`.
  2. Per occupied player slot: `Dead` / `Respawning` → `SaveSession(id, ZoneClosed, hpOverride: 0f, ct)`; ghost (`Disconnected_SessionActive`) → no `SaveSession`; entering (`IsEntering` true) → no `SaveSession`; otherwise (`Connected`) → `SaveSession(id, ZoneClosed, ct)`. Each started save is passed to `ITickCompletionQueue.Track`.
  3. Per ghost slot: `IZoneTeardownSessionSink.SaveGhostSnapshotAndExpire(characterId, clientId)` — the returned task is tracked like a step 2 save. Nothing else is sent or raised for a ghost: no `GhostExpiredEvent`, no `PlayerLeftZone`, no `ZoneSessionEnded`.
  4. Per non-ghost occupied slot (Connected, Dead, Respawning, entering): if `IZoneDrainNoticeLog.WasNotifiedAtDrain(clientId)` is false, queue `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=0)` to that client cap-exempt; then `IZoneTeardownSessionSink.ExpireConnectedSession(clientId)`. No transport call (`IInboundTransportDisconnector.DisconnectClient` is never called by this class).
  5. Per occupied slot whose character is in a formal party (`IPartyZonePresence.TryGetFormalParty` true; solo pseudo-party → no call): `NotifyZoneExit(PartyID, EntityID)` — from the tracked save's completion callback for a saved or ghost slot, immediately for an entering slot or a slot whose save threw synchronously. Exactly one call per character.
  6. Every player slot freed: `ZoneConnectionCharacterDirectory.Remove(clientId)`, `ZoneTickRoster.Deactivate(entityId)`, slot zeroed and returned to the free list. Story 011's `ZoneEntryReclamation.LeaveZone` is **not** used (it sends `PlayerLeftZone` and calls `NotifyZoneExit`, both forbidden here); the per-slot data the step 5 callbacks need (`PartyID`, `EntityID`) is captured before the slots are freed.
  7. Every mob slot zeroed and freed.
  8. The `ZoneID` retirement is logged once (`[ZoneInstancing] zone <id> retired`); the zone process has no routing table and exits after teardown.
- [x] **AC-ZI-12**: Active zone, 3 connected players (1 `Dead`), 5 mobs → `SaveSession` for the Dead one with HP override 0; the other 2 saved without override; after the queue drain the party fake has `NotifyZoneExit` for all 3; all player and mob slots empty; `ZoneSessionEnded(ZoneClosed, 0)` captured for all 3 clients.
- [x] **AC-ZI-31 / EC-ZI-15**: an entering slot is handled like a `Connected` slot except that step 2 makes no `SaveSession` for it and its `NotifyZoneExit` is not gated on a save. The close steps read `IsEntering` as the last tick left it (no tick runs during teardown); a slot that stall reclamation already freed is unoccupied and is skipped. Test: one acked `Connected` and one entering player, both in a formal party → `SaveSession` for the acked one only; `NotifyZoneExit` once each (the entering one before the drain, the acked one after its save completes); both get `ZoneSessionEnded(ZoneClosed)`.
- [x] **AC-ZI-23 (teardown side)**: a ghost that took damage after disconnecting → exactly one `SaveGhostSnapshotAndExpire` for it and no `SaveSession`; no `ZoneSessionEnded`, `PlayerLeftZone` or ghost-expired emission for it; its `NotifyZoneExit` fires only from the snapshot save's completion; a `Connected` client recorded by `IZoneDrainNoticeLog` gets no second `ZoneSessionEnded`, is still expired through the sink, and no `DisconnectClient` call is made.
- [x] **Ordering**: with saves that stay incomplete until the test completes them during `DrainOnShutdown`, no `NotifyZoneExit` for a saved character happens before its own save completes; completing them in reverse order produces `NotifyZoneExit` in that reverse order. A save completing with a failure → `NotifyZoneExit` still called, one `LogError`. A save that never completes within the drain timeout → no `NotifyZoneExit` for that character.
- [x] **EC-ZI-11**: a slot released before teardown (logout) → no save, no `ZoneSessionEnded`, no `NotifyZoneExit` for it.
- [x] **Run once / throw policy**: a second `RunCloseSteps` makes no calls on any fake. A fake that throws in step 1 → steps 2–8 still run; a `SaveSession` that throws synchronously for one slot → the other slots are still saved and that character's `NotifyZoneExit` is called at once; `NotifyZoneExit` throwing for one character → the others still get theirs; each failure logs one `LogError`.
- [x] New seams, each with a logging stand-in (log once, as `StandInLogOnce`):
  - `IZoneMobSpawnTimers.CancelAllSpawnTimers(ZoneID)` — stand-in until the Mob Spawning epic.
  - `IZoneTeardownSessionSink.SaveGhostSnapshotAndExpire(CharacterID, uint clientId) : Task<CharacterSaveResult>` — contract: writes the ghost's CGS-3 pre-disconnect snapshot as its only teardown save, moves the session to `Disconnected_SessionExpired`, deletes `ActiveSessionTokens[AccountID]` only if its `sessionId` is this session's; emits nothing. Stand-in logs and returns a completed task with a success result (TD-079).
  - `IZoneTeardownSessionSink.ExpireConnectedSession(uint clientId)` — contract: `Connected → Disconnected_SessionExpired`, no resume, no persistence write, token deleted only if owned. Stand-in logs (TD-079).
  - `IZoneDrainNoticeLog.WasNotifiedAtDrain(uint clientId) : bool` — Story 014 implements it from the T-4 broadcast; the stand-in returns false (every client gets `ZoneSessionEnded` at teardown, which is correct for T-6 from Active).

---

## Implementation Notes

- **From Story 010 readiness (2026-10-23):** on every slot release call `ZoneConnectionCharacterDirectory.Remove(clientId)` and `ZoneTickRoster.Deactivate(entityId)`, and `IPartyZonePresence.NotifyZoneExit` only when `TryGetFormalParty` is true (solo pseudo-party: no call).
- **Tracked saves:** `ITickCompletionQueue.Track(task, cts, onCompletedOnTick)` with one `CancellationTokenSource` per save (disposed by the callback). The callback runs on the main thread during `DrainOnShutdown`, after the slots were freed in step 6 — it must use only the captured `PartyID` / `EntityID` / `CharacterID`, not the slot.
- **Why not `ZoneCrashCleanupHandler`:** it is static and synchronous, persists HP only, does not invalidate the token and reports no completion, and `ICharacterPersistence` has no snapshot-save method (the ghost classes take `SaveGhost*` delegates nothing implements). Hence the `IZoneTeardownSessionSink` seam; the real implementation belongs to the Networking Core ghost / session story that pays TD-079 (and TD-077).
- **Step 4 audience:** every non-ghost occupied slot. A ghost has no transport connection (GDD step 4), so it gets nothing.
- Tests use a fake `ITickCompletionQueue` whose `DrainOnShutdown` runs callbacks for tasks the test has completed (or the real `TickCompletionQueue` with `TaskCompletionSource` saves — preferred, it proves the real delivery path), plus fakes for persistence, party, mob timers, the session sink, the drain notice log and the outbound sink.

---

## Out of Scope

- Story 014: constructing `ZoneCloseSteps` in the composition in place of the stand-in; invoking the teardown from the lifecycle's close notification; the T-4 broadcast and the real `IZoneDrainNoticeLog`
- The real Mob Spawning (`IZoneMobSpawnTimers`) and Party implementations
- The real `IZoneTeardownSessionSink` (session-layer expiry, token deletion, ghost snapshot persistence) — TD-079
- Closing transport connections (NGO shutdown, teardown step 7, Networking Core Story 042)

---

## QA Test Cases

- **AC-ZI-12** — as above, with a persistence fake recording calls and HP overrides, a party fake, a mob timer fake and a capturing outbound sink.
- **AC-ZI-23 teardown** — one ghost, one Connected client already in the drain notice log → one `SaveGhostSnapshotAndExpire`, no `SaveSession` for the ghost, nothing sent to the ghost, no second `ZoneSessionEnded` to the noticed client, `ExpireConnectedSession` for it, zero `DisconnectClient`; `NotifyZoneExit(ghost)` only after the snapshot task completes.
- **Ordering** — saves complete during the drain in reverse order → `NotifyZoneExit` in that order, none before the drain; failed save → notify + one error; never-completing save → no notify for it.
- **Entering slot (AC-ZI-31)** — as above.
- **EC-ZI-11** — a slot released before teardown → no calls for it.
- **Idempotent** — second `RunCloseSteps` → no calls.
- **Throw policy** — throwing mob fake, throwing `SaveSession` for one slot, throwing `NotifyZoneExit` for one character → other work still done, one `LogError` each (`LogAssert.Expect`).
- **Slots** — after `RunCloseSteps`, player and mob pools report 0 occupied; directory and tick roster empty; retirement logged once.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Teardown_tests.cs` — must exist and pass.

**Status**: [x] Created — 28 tests, all passing (EditMode batch 2026-10-23: 4027 total, 3957 passed, 0 failed, 70 skipped DB)

---

## Dependencies

- Depends on: Stories 001, 002, 003 (mob slots, step 7), 006 (`ZoneSessionEnded` codec), 010 (directory, tick roster, formal party), 011 (entering flag) — all Complete
- Unlocks: Story 014

---

## Completion Notes
**Completed**: 2026-10-23
**Criteria**: 8/8 passing, all covered by automated tests (`ZoneInstancing_Teardown_tests.cs`, 28 tests)
**Deviations** (advisory, accepted):
- `ZoneCloseSteps` is not yet wired: the composition keeps `StandInZoneCloseSteps` until Story 014 (wiring, `IZoneDrainNoticeLog`, drain-flag fix — Story 014 note (e)).
- `IZoneTeardownSessionSink` has only the logging stand-in (TD-079): no ghost snapshot save, no session/token expiry at teardown.
- Behaviours settled at `/code-review` (not in the GDD): ghost wins over entering; `TryGetFormalParty` throwing loses that notify (logged); `Track` throwing notifies at once; a save still running at the drain timeout gets no notify and its `CancellationTokenSource` is not disposed (process exits).
- Constructor takes one `ZoneSlotPool` (players and mobs share it), not two pools.
**Test Evidence**: Integration — `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Teardown_tests.cs`
**Code Review**: Complete — 2026-10-23 CHANGES REQUIRED (3 test gaps) → "fix all" (12 items) applied
