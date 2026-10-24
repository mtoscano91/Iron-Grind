# Story 013: Player Exit and Slot Release (CR-ZI-14 exit, AC-ZI-13)

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-23) — written 2026-10-22; `/story-readiness` 2026-10-23: NEEDS WORK (8 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-14, CR-ZI-17 (leave path), EC-ZI-11, T-5 / T-7 triggers, AC-ZI-13; `networking-session.md` ST-NET-2 (occupancy rules: disconnect, ghost TTL expiry)
**Requirement**: `TR-zi-014` *(epic placeholder — exit half; the entry half is Story 010)*

**ADR Governing Implementation**: ADR-010 (Party by direct call); ADR-011 (exit save tracked, completion on the tick thread)
**ADR Decision Summary**: the session layer saves and then reports the exit from the save's completion callback; Zone Instancing releases the slot, notifies Party once, and the occupancy change drives the lifecycle.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — pure C#, no engine API.

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces — ADR-010
- Forbidden: `await` in tick-driven code — ADR-011
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **Entry point**: `ReleasePlayer(uint characterId, DisconnectType disconnectType)` on the Zone Instancing exit service (a new class or a method next to `ZoneEntryReclamation.LeaveZone` — implementer's choice). Its signature matches `ConnectionStateMachine`'s `broadcastPlayerLeftZone` callback, so Story 014 passes it there directly. It finds the occupied player slot whose `Character` equals `characterId` (scan of the ≤ `MAX_PLAYERS_PER_ZONE` occupied slots; no new index) and runs the shared leave path `LeaveZone(entityId, disconnectType)` once: roster off, one `PlayerLeftZone` (with the given type: `Graceful` for a clean logout, `Timeout` for TTL expiry) to the other clients, `NotifyZoneExit(PartyID, EntityID)` only for a formal party, slot + directory entry freed, occupancy change raised. Returns whether a slot was freed.
- [ ] **One `PlayerLeftZone`**: the `removeEntityFromZone` callback is wired to a no-op (documented on the entry point) because `LeaveZone` already removes the entity. A test drives `ConnectionStateMachine.ProcessExplicitDisconnect` and `CompleteSessionActiveTTLExpiry` with `ReleasePlayer` as `broadcastPlayerLeftZone` and the no-op as `removeEntityFromZone`, and captures exactly 1 `PlayerLeftZone` per exit.
- [ ] **Ordering after the exit save**: `NotifyZoneExit` fires only after that character's exit save has completed (CR-ZI-12 step 5's rule, applied to logout and TTL). This holds by construction — `SessionEndCoordinator` (Character Persistence Story 010) calls the state machine, and so `ReleasePlayer`, only from the save's completion callback in `Drain`. A test with the real `SessionEndCoordinator`, a controllable save fake and the tick completion queue shows 0 `NotifyZoneExit` while the save is pending and 1 after the drain.
- [ ] **Save outcomes**: a clean logout whose save fails (any non-Success, fault, cancellation, watchdog timeout) releases nothing — slot kept, 0 `NotifyZoneExit`, session still `Connected` (CR-CP-10 step 9). A TTL expiry whose save fails still releases — 1 `NotifyZoneExit` (CR-CP-10 step 10).
- [ ] **AC-ZI-13**: a counting party fake records exactly 1 `NotifyZoneExit` for the EntityID — never 0, never 2 — for each single exit cause: logout, TTL expiry, teardown (Story 012's `ZoneCloseSteps` + drain), entry-stall reclamation (Story 011's case (a)), and a reclamation and a teardown in the same tick (reclamation's `OnTick` first, then the close steps — step 5 skips the freed slot).
- [ ] **EC-ZI-11**: `ReleasePlayer` for a character that holds no slot (never admitted, already released, or freed by teardown step 6) changes nothing, sends no `PlayerLeftZone` and no second notification, and returns false. A logout save still in flight when the close steps run gives exactly 1 `NotifyZoneExit` (step 6 frees the slot first; the late `ReleasePlayer` is a no-op).
- [ ] **Lifecycle**: the release raises the occupancy change (through the pool, as `LeaveZone` does), so the lifecycle (Story 001) fires T-5 on the tick of the last release while Draining, and starts the idle timer on the last release while Active.

---

## Implementation Notes

- **Readiness decisions (2026-10-23, user):** (1) hook shape — `ReleasePlayer(uint characterId, DisconnectType)` goes into the `broadcastPlayerLeftZone` callback, `removeEntityFromZone` is a no-op; no `ExitCause` enum. (2) AC-ZI-22 release side (a ghost reconnecting while Draining) is deferred — see Out of Scope.
- **Call sites (readiness 2026-10-23):** the only session-end code is `src/ServerLogic/CharacterPersistence/SessionEndCoordinator.cs`. `OnLogoutSaved` / the TTL callback call `ConnectionStateMachine.ProcessExplicitDisconnect(accountId, NoOpPersist, removeEntityFromZone, broadcastPlayerLeftZone)` and `CompleteSessionActiveTTLExpiry(...)`; the state machine calls `removeEntityFromZone(characterId)` then `broadcastPlayerLeftZone(characterId, Graceful | Timeout)`. Nothing in `src/` constructs `SessionEndCoordinator` or calls `BeginCleanLogout` / `EvaluateSessionTTL` today — that wiring (coordinator construction with `ReleasePlayer`, the per-tick TTL evaluation, the logout request handler) is Story 014's. This story tests through the coordinator and state machine with fakes; it does not change Character Persistence or Networking Core.
- **The `uint` is the character id**: the state machine passes the session's `characterId` (`CharacterID` value). Compare with `ZonePlayerSlot.Character`; do not use `ZoneConnectionCharacterDirectory` for the lookup (a ghost's connection is gone).
- **From Story 011 readiness:** Story 011 built the shared leave path; `ReleasePlayer` reuses `LeaveZone` unchanged (it already guards a missing slot, catches a throwing outbox / party seam and frees the slot anyway). An acked slot's disconnect is this story's; an entering slot's is Story 011's case (c).
- **From Story 010 readiness:** `LeaveZone` already calls `ZoneConnectionCharacterDirectory.Remove(clientId)`, `ZoneTickRoster.Deactivate(entityId)` and `NotifyZoneExit` only when `TryGetFormalParty` is true (solo pseudo-party: no call).
- The exit save itself (`SaveSession(CleanLogout)` / TTL save) is Character Persistence Story 010's — this story does not re-implement it.

---

## Out of Scope

- Story 012: teardown exits (reused by the AC-ZI-13 teardown case, not changed)
- Story 014: constructing `SessionEndCoordinator`, per-tick `EvaluateSessionTTL`, the logout request handler, passing `ReleasePlayer` into the state machine callbacks
- **AC-ZI-22 release side** (a ghost's reconnect while Draining releases its slot with its final save): deferred with the gateway re-routing work (`RoutingRedirectMessage`, `auth-wire-messages.md`; Zone Instancing EPIC gateway blocker). It needs an early ghost-end entry point on `SessionEndCoordinator` (Character Persistence) and an admission branch for a ghost-held character while Draining. Today such a handshake is refused (`ZoneDoesNotExist` / `CharacterAlreadyInZone`) and the ghost is released by TTL expiry or grace expiry.
- Zone transfers (CR-ZI-16, deferred)

---

## QA Test Cases

- **AC-ZI-13 logout** — party member, real coordinator, save pending → 0 notifications; drain → 1, and 1 `PlayerLeftZone(Graceful)`.
- **AC-ZI-13 TTL** — ghost, TTL evaluated at expiry, save completes → 1 notification, 1 `PlayerLeftZone(Timeout)`.
- **AC-ZI-13 teardown** — Story 012's close steps + drain → 1.
- **AC-ZI-13 reclamation** — entering slot stalls → 1; stall and teardown in the same tick → 1.
- **Failed logout save** — `DatabaseError` → slot kept, 0 notifications, session Connected.
- **Failed TTL save** — released, 1 notification.
- **Solo player** — no formal party → 0 `NotifyZoneExit`, slot freed.
- **Double release** — `ReleasePlayer` twice → 1 notification, second returns false.
- **Logout in flight at teardown** — close steps run before the logout drain → 1 notification.
- **Lifecycle** — Draining with 1 slot; release → T-5 on that tick. Active with 1 slot; release → idle timer starts.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_PlayerExit_tests.cs` — must exist and pass.

**Status**: [x] Created 2026-10-23 — 24 test cases after `/code-review`; EditMode batch 4051 total, 3981 passed, 0 failed, 70 skipped (DB)

---

## Dependencies

- Depends on: Stories 001, 002, 010, 011 (leave path), 012 (teardown case) — all Complete; Character Persistence Story 010 (`SessionEndCoordinator`, Complete)
- Unlocks: Story 014

---

## Completion Notes
**Completed**: 2026-10-23
**Criteria**: 7/7 passing (all test-covered)
**Deviations**: (1) Unwired until Story 014 — nothing constructs `SessionEndCoordinator` yet, so logout / TTL exits do not run in the live server. (2) AC-ZI-22 release side (ghost reconnect while Draining) deferred with gateway re-routing (readiness decision). (3) Test names omit the `[system]` part of `test-standards.md` (implied by the fixture, as in Stories 011–012). (4) "First match" relies on admission's one-slot-per-character invariant (`PrepareHandshake`), documented in the `ReleasePlayer` remarks.
**Test Evidence**: Integration — `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_PlayerExit_tests.cs` (24 cases); EditMode batch 4051 total, 3981 passed, 0 failed, 70 skipped (DB).
**Code Review**: Complete — CHANGES REQUIRED (5 test gaps + dead branch) → fix all.
