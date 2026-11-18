# Story 007: Disconnect, Reconnect, Teardown and Expiry While Dead

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — EC-DR-1, EC-DR-2, EC-DR-3, EC-DR-7, AC-DR-16; `zone-instancing.md` CR-ZI-8 step 6, CR-ZI-12 step 2; `character-persistence.md` CR-CP-3 step 8
**Requirement**: `TR-dr-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-011: Async Persistence and the Tick Loop
**ADR Decision Summary**: Saves are started on the tick and tracked, never awaited; the in-memory state is the source for what is written.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (pure C# server logic, no engine API).

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await`, `Task.Wait()`, `.Result` in tick code (ADR-011)

---

## Readiness Decisions (2026-11-18)

1. **The slot state says only whether the player is connected.** Today one `ZoneSessionState` value holds both facts: the death handler writes `Dead` (`DeathAndRespawnService.cs:192`), which hides a disconnect (`ZoneSlotGhostStatusSource` sees no ghost, the snapshot's disconnected flag is false), and the respawn writes `Connected` (`:297`), which makes a disconnected player look connected (`ZoneAggroCandidateSource` would offer them to mobs). From this story on, Death & Respawn **never writes the slot state**: remove the `SetSessionState(Dead)` call in T-1 and the `Connected` restore in T-3. `DeathState` is the only source of dead-ness, read through a new `IPlayerDeathStateReader` (implemented by `DeathAndRespawnService`). `ZoneSessionState.Dead` / `Respawning` stay in the enum (registry, wire docs) but no production code writes or reads them; fix the `ZoneSlotPool.cs:45` doc example.
2. **No production code sets a slot to `Disconnected_SessionActive` on a disconnect yet.** Only `ZoneSlotPool.RevertToGhost` (a reconnect that did not finish) does; ghost promotion is unwired (Story 003 finding, Networking Core). Tests simulate the disconnect with `pool.SetSessionState(P, Disconnected_SessionActive)` and the reconnect with `pool.RebindClient`.
3. **A Dead player's second death is always dropped (Story 003 AC-6 changes).** In `OnEntityDied`, the duplicate check (`DeathState != Alive` → return) runs **before** the ghost check, so a player who died and then disconnected never takes the ghost path while Dead or Respawning. A ghost that is Alive still takes the ghost path. This deviates from the GDD's order (CR-DR-Ghost "before CR-DR-2") — record it as an advisory deviation at `/story-done`. Update `test_ghost_death_dead_then_disconnected_second_death_takes_ghost_path` (`DeathAndRespawn_GhostDeath_tests.cs:377`) to expect: sink not called, `DeathTick` / `RespawnTick` unchanged; rename it accordingly; update the comment at `DeathAndRespawnService.cs:160`.
4. **Teardown keeps the ghost split.** A dead player who is **connected** uses the HP 0 overload in step 2. A dead player who is **disconnected** is a ghost and stays on step 3 (`SaveGhostSnapshotAndExpire`), unchanged; its in-memory HP is already 0 while Dead. (The production ghost sink is `LoggingZoneTeardownSessionSink`, which only logs — an existing Networking Core gap, out of scope.)
5. **Session-TTL expiry while Dead cannot happen in practice.** `RESPAWN_DELAY_SECONDS` ≤ 10 s, `SESSION_TTL_SECONDS` ≥ 60 s, the ghost TTL ≥ 15 s, and the anchor retry is capped by `RespawnAnchorRetryTicks`, so the timer always respawns the player first (AC-2). The expiry save (`SessionEndCoordinator.cs:286`) uses the normal `SaveSession` overload, which reads the in-memory HP — already 0 while Dead, so no override is needed. AC-5 tests that path against fakes only; no Database variant.
6. **Composition wiring is Story 008.** This story adds the reader, the optional constructor parameters and the release listener; registering them in `ZoneServerComposition` is Story 008.

---

## Acceptance Criteria

- [x] **AC-1 `DeathState` is the source of dead-ness**: new `IPlayerDeathStateReader` (`bool IsDead(EntityID)`; `bool TryGetRespawnTick(EntityID, out long)` — true only while Dead/Respawning) implemented by `DeathAndRespawnService`. The death handler no longer calls `SetSessionState(Dead)` and T-3 no longer restores `Connected`. Three readers take an `IPlayerDeathStateReader` and no longer read `ZoneSessionState.Dead` / `Respawning`:
  - `ZoneSnapshotBuilder` (`:136`) — `isInDeadState` from the reader;
  - `ZoneCloseSteps` — the slot snapshot (`:175–185`) records `IsDead` from the reader; step 2 (`:212`) picks the HP 0 overload from it;
  - `ZoneAggroCandidateSource` (`:55`) — candidate = slot `Connected` **and** not dead (keeps Story 006 AC-1).
  The reader parameter is optional in each (null → nobody is dead), so existing call sites compile. Test: P dies, then the slot is set to `Disconnected_SessionActive` → `IsDead(P)` true, slot state still `Disconnected_SessionActive`, `ZoneSlotGhostStatusSource.IsGhost(P)` true; P dies while connected → slot state stays `Connected`; the aggro source does not offer a dead connected P.
- [x] **AC-2 Timer runs while disconnected (EC-DR-1)**: P dies at T, slot set to `Disconnected_SessionActive` at T + 10; at T + delay the full T-3 runs (HP / MP full, position at town point, party Online, `IsDead` false) and the slot state is still `Disconnected_SessionActive` afterwards. P's client is not in the outbox, so owner messages to P are skipped with no log (Story 005 AC-7: no sequence counter → skipped).
- [x] **AC-3 Reconnect snapshot (EC-DR-1)**: `ZoneEntityStateSource` takes an optional `IPlayerDeathStateReader` (null → `RespawnTick` 0) and fills `ZonePlayerStateView.RespawnTick` (`uint`) from `DeathState.RespawnTick` (`long`): a value above `uint.MaxValue` is clamped to `uint.MaxValue` with one `Debug.LogError` per clamp; a negative value cannot occur (ticks start at 0). P dies at T, disconnects, reconnects at T + 50 (`RebindClient`) → P's `EntityState` has `isInDeadState = true` and `respawnTicksRemaining = RespawnTick − currentTick` (90 with delay 140), and the disconnected flag is false after the rebind. Reconnect after the respawn → `isInDeadState = false`, remaining 0, HP full.
- [x] **AC-4 Teardown (EC-DR-3)**: zone closes with P dead and connected, G dead and disconnected, Q alive → P is saved with `SaveSession(P, ZoneClosed, 0f, ct)` (HP 0 overload); G goes through step 3 (`SaveGhostSnapshotAndExpire`) and is not saved in step 2; Q uses the normal `SaveSession(Q, ZoneClosed, ct)`.
- [x] **AC-5 Expiry and load (AC-DR-16, EC-DR-2, EC-DR-7)**: against the persistence fakes: a character whose in-memory `CurrentHP` is 0 ends its session by TTL expiry → the normal `SaveSession(..., SessionTTLExpiry, ...)` writes `CurrentHP = 0`; the next `LoadCharacter` of that record yields `CurrentHP == MaxHP` (CR-CP-3 step 8 / F-CP-4 clamp, already covered in `CharacterPersistence_LoadRestore_tests.cs` — reuse its fixtures). No Database-backed variant (decision 5).
- [x] **AC-6 Release clears state**: `DeathAndRespawnService` implements `IZoneSlotReleaseListener.BeforeRelease(EntityID)` (the slot is still held, so the slot lookup works): resets that slot's `DeathState` to Alive (all fields zeroed) and `_retryAttempts[slot]` to 0. An unknown id changes nothing and logs nothing. A new occupant of the same slot is Alive (`IsDead == false`) and is not respawned by a later `Tick`.
- [x] **AC-7 Second death while Dead is dropped (Story 003 AC-6 revised)**: P dies (Dead), slot set to `Disconnected_SessionActive`, a second `OnEntityDied(P)` arrives → `IGhostDeathSink` not called, `DeathTick` / `RespawnTick` unchanged, no second `EntityDied` sent. An Alive ghost's death still calls the sink once (Story 003 AC-1 unchanged).

---

## Implementation Notes

- `IPlayerDeathStateReader` lives in `src/ServerLogic/DeathAndRespawn/`. ZoneInstancing, EnemyAI and DeathAndRespawn are all in `IronGrind.ServerLogic` (one asmdef), so no assembly reference changes.
- Remove the comment at `TryRespawn` "slot released while dead; teardown is Story 007" — after AC-6 the slot is reset on release, so that branch only guards a race.
- Story 006 regression: its AC-1 test relied on the slot being `Dead` for `ZoneAggroCandidateSource`; after AC-1 it must still pass through the reader (the `MobController` HP check is the second guard). Run the full `DeathAndRespawn` filter and the `EnemyAI` filter.
- Story 002 tests that assert `pool` state `Dead` after a death and Story 004 tests that assert `Connected` after the respawn change to "slot state unchanged".

---

## Out of Scope

- Client re-rendering of the overlay on reconnect (presentation)
- Ghost deaths and ghost-death session end (Stories 003, 010)
- Setting the slot to `Disconnected_SessionActive` on an actual disconnect (Networking Core ghost promotion wiring)
- The real ghost teardown save behind `SaveGhostSnapshotAndExpire` (`LoggingZoneTeardownSessionSink` is a stub; Networking Core)
- Registering the reader, the release listener and the optional constructor arguments in `ZoneServerComposition` (Story 008)

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-18. Delay 140 ticks unless stated.*

- **AC-1**: P dies → slot state still `Connected`, `IsDead(P)` true; then `SetSessionState(P, Disconnected_SessionActive)` → `IsDead(P)` true, `IsGhost(P)` true. Aggro source with P dead and connected → P not a candidate; Q alive and connected → candidate. Readers built with a null reader → nobody dead.
- **AC-2**: die 1000, disconnect 1010 (slot + client removed from outbox), no reconnect → at 1140 HP / MP full, position town point, party Online, `IsDead` false, slot still `Disconnected_SessionActive`; zero messages to P's client, no log.
- **AC-3**: die 1000, disconnect 1010, `RebindClient` at 1050 → snapshot entry dead, remaining 90, disconnected flag false. Reconnect at 1150 → alive, remaining 0, HP full. Range guard: reader returns `RespawnTick = uint.MaxValue + 1L` → view gets `uint.MaxValue`, one `LogAssert` error. Null reader → `RespawnTick` 0.
- **AC-4**: close with P dead + connected, G dead + disconnected, Q alive → P via HP 0 overload in step 2, G only via step 3, Q via the normal overload.
- **AC-5**: in-memory HP 0, TTL expiry → stored HP 0 (normal overload, no override); load → HP == MaxHP.
- **AC-6**: P dies, retry counter > 0 (unresolved anchor for 2 ticks), slot released → `BeforeRelease` resets state + retry counter; new player in the same slot → Alive, `IsDead == false`, `Tick` past the old `RespawnTick` does nothing to them. Unknown id → no change, `LogAssert.NoUnexpectedReceived`.
- **AC-7**: P dies, slot `Disconnected_SessionActive`, second `OnEntityDied(P)` → sink 0 calls, `DeathTick` / `RespawnTick` unchanged, 0 extra `EntityDied`. Alive ghost dies → sink 1 call.

---

## Performance

One O(1) array lookup per player in the snapshot build, the zone-close slot snapshot and the aggro candidate scan; no allocation. `BeforeRelease` is O(1). No new per-tick work.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DisconnectTeardown_tests.cs` — must exist and pass; plus the updated Story 003 AC-6 test in `DeathAndRespawn_GhostDeath_tests.cs`

**Status**: [x] Created and passing (2026-11-18)

---

## Dependencies

- Depends on: Stories 002, 004 (both Complete)
- Unlocks: Story 008

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 7/7 passing, all automated (none deferred)
**Deviations** (advisory):
1. AC-7: duplicate suppression runs before the ghost check (GDD order is CR-DR-Ghost before CR-DR-2); Story 003 AC-6 and its ordering test were revised.
2. `ZoneSessionState.Dead` / `Respawning` remain in the enum but no production code writes or reads them.
3. The reader and release listener are optional constructor parameters and not composed yet; Story 008 AC-4 / AC-6 now require all four consumers wired and one assertion per reader.
4. AC-3: the real service is tested against `ZoneEntityStateSource` and against `ZoneSnapshotBuilder` separately, not in one chain.
5. AC-5: TTL expiry while dead is unreachable in practice (delay ≤ 10 s < TTL ≥ 60 s); tested against fakes only.
**Test Evidence**: Integration — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DisconnectTeardown_tests.cs` (15), plus `ZoneEntityStateSource_tests.cs` (7 cases), `ZoneInstancing_SnapshotAssembly_tests.cs` (4), `ZoneInstancing_Teardown_tests.cs` (1), `CharacterPersistence_SaveSession_tests.cs` (1); shared `FakePlayerDeathStateReader.cs`, `RealDeathRig.cs`. Full EditMode `-testCategory "!Database"` 8035/8035 (2026-11-18 17:18:37Z).
**Code Review**: Complete (`/code-review`: unity-specialist no logic defects, qa-tester TESTABLE; R1–R2 and S1–S8 applied). Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
