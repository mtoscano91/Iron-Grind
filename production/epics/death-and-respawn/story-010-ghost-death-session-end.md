# Story 010: Ghost Death Session End

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-Ghost, T-4; `networking-ghost-session.md` CR-GH-6, CR-GH-7; `networking-ghost-character-state.md` CGS-5, GD-CGS-2
**Requirement**: `TR-dr-003` (production half: what happens after the handler hands off)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-011: Async Persistence and the Tick Loop (also ADR-010)
**ADR Decision Summary**: A session end is started from the tick, its save is tracked behind the mutation gate, and its completion (release, state transition) runs on the main thread when the save finishes. Nothing is awaited in the tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#).

**Control Manifest Rules (this layer)**:
- Required: persistence writes go through the existing `SessionEndCoordinator` mutation gate (ADR-011)
- Forbidden: `async`/`await` in tick code; no second save path beside the coordinator's; no synchronous save in the tick

---

## Readiness Decisions (2026-11-18)

Found at `/story-readiness`: no production pre-disconnect HP snapshot (`PreDisconnectSnapshot` is used only inside `Networking/GhostSession`; a ghost's live HP is 0 when it dies); no position column in `character_records` (001 migration; only `last_zone_id`); `wasKilledWhileDisconnected` stored nowhere (no column, not in the save, `SessionHandshakeData` placeholder); `CompleteGhostDeathFromDisconnected` removes the session record and calls neither `removeEntityFromZone` nor `broadcastPlayerLeftZone`, so the zone slot (freed by `ZonePlayerExit.ReleasePlayer` through `broadcastPlayerLeftZone`, `ZoneInstancingWiring.cs:495`) would never be freed; the TTL path releases whatever the save result (`SessionEndCoordinator.cs:443`); `HandleGhostDeathWhileReconnecting` persists synchronously; `GhostCleanupSequencer` / `MobDeTargetingCoordinator` are delegate-only helpers with no production caller. User decisions:

1. **HP**: the save writes `IPreDisconnectHpSource.TryGetHp(entity, out hp)` through `SaveSession(..., hpOverride, ...)`; with no snapshot it writes the effective MaxHP (the respawn HP, CR-DR-9, `CharacterStatsRespawnVitalsWriter.cs:26`). The production source records nothing until Networking Core marks disconnects (a no-op source → always the fallback for now).
2. **No position in the record**: `character_records` has no position column; the next login places the character at the zone's spawn point. CGS-5 step 2 ("respawn position substituted") is N/A — recorded as a deviation.
3. **Ghost-death mark in memory**: `ConnectionStateMachine` keeps a per-account ghost-death mark (lost on server restart). The DB column and the handshake `wasKilledWhileDisconnected = true` are tech debt (log a TD entry at `/story-done`). **Dev-story revision (2026-11-18, user):** a reconnect after a ghost death goes through the gateway `Reconnect` first (`GatewayReconnectHandler.cs:66`), which answers `ReconnectResult(accepted:false)` when there is no session, and `ReconnectResult` has no reason field — so the client never reaches the zone and no `ZoneSessionEnded(GhostDeath)` can be sent. The gateway handler consumes the mark and rejects as today; telling the client the reason (a `ReconnectResult` reason field) is tech debt with the DB column. In the `Reconnecting` race (AC-5) no client is attached either, so `ZoneSessionEnded` goes through an optional `sendZoneSessionEnded` delegate (null → nothing).
4. **Save failure like TTL**: log the error, then release and transition anyway (same as every other coordinator end; avoids a later TTL save writing HP 0).
5. **States**: `Disconnected_SessionActive` and `Reconnecting` are accepted. The save is always async (ADR-011); if the account is `Reconnecting` when the save completes, the existing `HandleGhostDeathWhileReconnecting` runs with a no-op persist.

---

## Acceptance Criteria

- [x] **AC-1 Begin**: `SessionEndCoordinator.BeginGhostDeath(accountId, entity)` returns true and tracks exactly one save with the new `SessionEndReason.GhostDeath` (= 4) for an account in `Disconnected_SessionActive` or `Reconnecting`; when the character's mutation gate is held it waits like the TTL end. It returns false, starts nothing and logs one warning when an end is already in flight or the account is in any other state (or unknown).
- [x] **AC-2 Save (GD-CGS-2)**: the save's `hpOverride` is the HP from `IPreDisconnectHpSource` when it has one, else the entity's effective MaxHP. No position is written (Decision 2).
- [x] **AC-3 Order (CR-GH-6, CR-GH-7)**: when the save completes and the account is still `Disconnected_SessionActive`: `Release(...)` (session release, inventory, equip request store, equipment, entity unregister) → `CompleteGhostDeathFromDisconnected` with a no-op persist (trigger "GhostDeath", state `Disconnected_SessionExpired`) → `removeEntityFromZone` → `broadcastPlayerLeftZone` (frees the zone slot) → ghost-death mark set for the account → optional `broadcastGhostExpired(characterId, "GhostDeath")` → optional `deTargetMobsOf(entity)`. Both optional delegates are constructor parameters; null → nothing happens.
- [x] **AC-4 Save failure**: a failed or timed-out save logs one error, and the AC-3 chain still runs in full (Decision 4).
- [x] **AC-5 Reconnect race**: if the save completes while the account is `Reconnecting`, `HandleGhostDeathWhileReconnecting` runs with a no-op persist and the optional `sendZoneSessionEnded(accountId, DisconnectReason.GhostDeath)` delegate (null → nothing), the zone slot is freed as in AC-3, and the mark is set. If the account is `Connected` (reconnect finished during the save), completion logs one warning and releases nothing (existing coordinator rule).
- [x] **AC-6 Reconnect after ghost death (CR-GH-6 step 6, revised)**: the gateway `Reconnect` for a marked account answers `accepted:false` (as for any account with no session) and consumes the mark; `ConnectionStateMachine` exposes the mark (`HasGhostDeathMark` / `TryConsumeGhostDeathMark`). A new session for the account (`EnterConnecting`) clears the mark. Delivering `GhostDeath` as the reason to the client is out of scope (tech debt).
- [x] **AC-7 Sink adapter**: `SessionEndGhostDeathSink : IGhostDeathSink` maps the EntityID to the slot's `Account` through the zone slot pool and calls `BeginGhostDeath`. An EntityID with no player slot logs one warning and does nothing; a repeated death while the end is in flight returns false with no second save (the sink must be idempotent, Story 003).

---

## Implementation Notes

- Mirror `EvaluateSessionTTL` / `StartTtlSave` / `OnTtlSaved` and the `PendingEnd` gate wait (add a ghost-death flag or kind to `PendingEnd`); reuse `Release(...)` and `HasState(...)`.
- New `SessionEndReason.GhostDeath = 4`: check every switch / mapping over the enum (persistence reason handling, logs, tests that enumerate it).
- New `IPreDisconnectHpSource` (`bool TryGetHp(EntityID entity, out float hp)`) plus a no-op production implementation (always false); the coordinator (or the sink adapter, whichever owns the entity) needs Character Stats' effective MaxHP for the fallback — pass a `Func<EntityID, float>` or the stats reader, not the whole service.
- The ghost-death mark lives in `ConnectionStateMachine` (it owns account sessions); `GatewayReconnectHandler.Handle` consumes it in its no-session rejection branch (no change to `ReconnectResult`).
- Fix the stale `HandleGhostDeathWhileReconnecting` remark that says no `ZoneSessionEnded` serializer exists.
- Mobs already drop a target at HP 0 (Story 006); `deTargetMobsOf` is for CR-GH-7 completeness and is optional.

---

## Out of Scope

- Story 003: the handler branch and the seams
- Story 008: composing the sink, the no-op `IPreDisconnectHpSource` and the two optional delegates into `ZoneServerComposition`
- The production pre-disconnect HP writer (Networking Core disconnect wiring)
- A DB column for `wasKilledWhileDisconnected`, a true value in the session handshake, and a reason field in `ReconnectResult` so the client learns GhostDeath (tech debt)
- Party XP forfeit for the ghost period (CR-GH-9.1) — Party System epic

---

## QA Test Cases

Fakes for persistence, completion queue and mutation gate (as in the TTL tests) with the real `ConnectionStateMachine`; ordered call log across the delegates.

- **AC-1**: `Disconnected_SessionActive` → true, one save with `GhostDeath`; `Reconnecting` → true; `Connected`, unknown account, end already in flight → false, one warning, no save; gate held → no save until opened, then one save.
- **AC-2**: HP source has 42 → save `hpOverride` 42; HP source empty → effective MaxHP.
- **AC-3**: recorded order release → state transition (Disconnected_SessionExpired) → remove → left-zone (slot freed) → mark → GhostExpired → de-target; both optional delegates null → no exception, rest of the chain unchanged.
- **AC-4**: save result failed and timed out → one error logged, full chain still runs.
- **AC-5**: account moved to `Reconnecting` before completion → `sendZoneSessionEnded(account, GhostDeath)` called once, slot freed, mark set; delegate null → no exception; account `Connected` before completion → one warning, no release, no transition.
- **AC-6**: marked account gateway `Reconnect` → `accepted:false`, mark consumed; `EnterConnecting` for a marked account clears the mark; unmarked account behaviour unchanged.
- **AC-7**: known EntityID → one `BeginGhostDeath` for the slot's account; unknown EntityID → one warning, no begin; second death in flight → no second save.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_GhostSessionEnd_tests.cs` — must exist and pass

**Status**: [x] Created and passing (2026-11-18)

---

## Dependencies

- Depends on: Story 003 (seam, Complete), Story 004 (Complete)
- Unlocks: Story 008

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 7/7 passing, all automated (none deferred)
**Deviations** (advisory):
1. CGS-5 step 2: no position is written (no position column; next login spawns at the zone's spawn point) — Decision 2.
2. GD-CGS-2 HP: production writes effective MaxHP until Networking Core supplies a pre-disconnect HP (`NoOpPreDisconnectHpSource`) — Decision 1.
3. CR-GH-6 step 6: a reconnect after a ghost death is rejected by the gateway with plain `accepted:false` (no reason field); the client is not told "GhostDeath". The mark is in memory only; `wasKilledWhileDisconnected` is not persisted and the handshake value stays a placeholder (TD-111).
4. Save failure releases and ends the session like the TTL end (story originally said keep the session) — Decision 4.
5. `IPreDisconnectHpSource` lives in `IronGrind.CharacterPersistence` (moved at code review); GhostExpired, de-target and `sendZoneSessionEnded` are optional delegates, not composed until Story 008 (AC-4b).
**Test Evidence**: Integration — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_GhostSessionEnd_tests.cs` (36), plus `CharacterPersistence_Contract_tests.cs` (`SessionEndReason` pin + `GhostDeath = 4`). Full EditMode `-testCategory "!Database"` 8071/8071 (2026-11-18 17:41:42Z).
**Code Review**: Complete (`/code-review`: unity-specialist ADR-011 compliant, qa-tester 3 test gaps; C1–C5, T1–T3, S1–S11 applied). Lean: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
