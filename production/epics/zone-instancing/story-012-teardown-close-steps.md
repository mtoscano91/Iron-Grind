# Story 012: Teardown Close Steps (CR-ZI-12 steps 1–8)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-12, EC-ZI-3, EC-ZI-11, Dead/Respawning during Draining, AC-ZI-12, AC-ZI-23 (teardown side); `networking-ghost-character-state.md` CGS-3 / CGS-4; `character-persistence.md` CR-CP-1 (`SaveSession` HP override)
**Requirement**: `TR-zi-012`, `TR-zi-014` *(epic placeholders)*

**ADR Governing Implementation**: ADR-009 Amendment 1 (teardown step 5 — the close steps, and its ordering note); ADR-006 (`SaveSession(ZoneClosed[, hpOverride])`); ADR-011 (tracked saves, drained before exit)
**ADR Decision Summary**: the composition's teardown runs the CR-ZI-12 steps after navigation teardown and before the outbound flush; saves are started as tracked tasks and the teardown persistence drain waits for them before the process exits.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (save ordering across tracked tasks)
**Engine Notes**: none beyond ADR-011.

**Control Manifest Rules (this layer)**:
- Forbidden: `await` / `.Result` / `ContinueWith` in tick-driven code — ADR-011
- Required: direct calls on injected interfaces (Mob Spawning, Party, ghost handler) — ADR-010
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] The real close steps replace `StandInZoneCloseSteps` (`IZoneCloseSteps.RunCloseSteps`), in this order:
  1. `CancelAllSpawnTimers(ZoneID)` on an injected Mob Spawning interface (stand-in logs once).
  2. Per occupied player slot: `Dead` / `Respawning` → `SaveSession(id, ZoneClosed, hpOverride: 0f, ct)`; ghost (`Disconnected_SessionActive`) → no `SaveSession`; otherwise `SaveSession(id, ZoneClosed, ct)`.
  3. Per ghost: the ghost handler saves the CGS-3 pre-disconnect snapshot as its only save, moves the session to `Disconnected_SessionExpired` and invalidates its token; no `GhostExpiredEvent`, no `PlayerLeftZone`, no `ZoneSessionEnded(GhostDeath=3)`.
  4. `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=0)` to every `Connected` client not already sent one at T-4; then every remaining connection is closed by the server and its session enters `Disconnected_SessionExpired`.
  5. `NotifyZoneExit(PartyID, EntityID)` for every occupied player slot, ghosts included — each one only after that character's step 2 or step 3 save has completed (or is committed to the write queue with guaranteed ordering).
  6. All player slots zeroed and freed. 7. All mob slots zeroed and freed. 8. The `ZoneID` retired (no routing table in the zone process — log the retirement; the process exits).
- [ ] **AC-ZI-12**: Active zone, 3 connected players (1 `Dead`), 5 mobs → `SaveSession` for the Dead one with HP 0; the other 2 saved; party fake receives `NotifyZoneExit` for all 3; all slots empty; `ZoneSessionEnded(ZoneClosed)` captured for the connected clients.
- [ ] **AC-ZI-23 (teardown side)**: a ghost that took damage after disconnecting is written exactly once, with the snapshot HP, with no `SaveSession` call; its session is expired with the token invalidated; `NotifyZoneExit` for it fires only after the snapshot save completes; a `Connected` client already told at T-4 gets no second `ZoneSessionEnded` and its transport is closed.
- [ ] **EC-ZI-11**: step 5 runs only for occupied slots; a slot emptied earlier by a logout gets no second `NotifyZoneExit`.
- [ ] Runs once per process; a second call is a no-op. A throwing step is logged and the remaining steps still run (the composition contract: "may throw; logged and goes on" — confirm per step at readiness).

---

## Implementation Notes

*Derived from ADR-009 A1 Decision 3 (step 5 ordering note) and ADR-011:*

- Step 5's "after the save" is satisfied by starting `NotifyZoneExit` from the save task's tracked completion on the main thread, or by the write queue's per-character ordering (Character Persistence `CharacterWriteQueue`) — readiness picks one and the test proves it.
- The ghost handler: reuse `src/ServerLogic/Networking/GhostSession/ZoneCrashCleanupHandler.cs` (CR-GH-11: persist snapshot, expire) behind an interface if it emits nothing the GDD forbids here; otherwise a teardown-specific variant. Check what it emits before deciding.
- Which clients were already told at T-4: Story 014 records it; this story reads it.

---

## Out of Scope

- Story 014: invoking the teardown from the lifecycle's close notification; the T-4 broadcast
- The real Mob Spawning / Party implementations

---

## QA Test Cases

- **AC-ZI-12** — as above, with a persistence fake recording calls and HP overrides, a party fake and the observer.
- **AC-ZI-23 teardown** — ghost snapshot HP 80, tracked HP 30 → one write with 80; no `SaveSession` for the ghost; token invalid; no forbidden messages; `NotifyZoneExit(ghost)` after the snapshot save's completion.
- **Ordering** — persistence fake completes saves late (next tick); `NotifyZoneExit` for each character only after its own save completes.
- **EC-ZI-11** — a slot released before teardown → no `NotifyZoneExit` for it at teardown.
- **Idempotent** — second `RunCloseSteps` → no calls.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Teardown_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 006
- Unlocks: Story 014
