# Story 009: Player Entry, Part 1 — Admission (CR-ZI-8 steps 3–5, CR-ZI-7)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22; readiness 2026-10-23 (NEEDS WORK, 8 gaps -> fixed)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 6 hours (readiness 2026-10-23: pre-check, `ApplyLoadedRecord` failure path, pending table and connection drop added)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-7, CR-ZI-8 steps 3–5, EC-ZI-1, EC-ZI-4, AC-ZI-4, AC-ZI-27
**Requirement**: `TR-zi-006`, `TR-zi-007` *(epic placeholders)*

**ADR Governing Implementation**: ADR-014 (connection-level `SessionHandshake`); ADR-011 (persistence task tracked, never awaited on the tick); ADR-006 (`LoadCharacter`)
**ADR Decision Summary**: the handshake is handled per connection; `LoadCharacter` is started as a `Task` and passed to `ITickCompletionQueue.Track`; its continuation runs on the main thread, where the state and capacity check and the slot write happen together.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (persistence task completion on the main thread)
**Engine Notes**: no Unity API beyond `Debug.Log*`; the only engine-adjacent risk is ADR-011's (callbacks run in `ITickCompletionQueue.Drain` on the tick thread).

**Control Manifest Rules (this layer)**:
- Forbidden: `await`, `async` methods, `.Result`, `.Wait()`, `ContinueWith` in tick-driven code — start the `Task` and `Track` it — ADR-011
- Required: zone state mutated only on the main thread — ADR-011
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **Seam (user decision 2026-10-22)**: `ZoneEntryAdmission.BeginEntry(in ZoneEntryRequest)` takes an already-decoded request (connection id, `AccountID`, `CharacterID`, target `ZoneID`, session token) and is called on the tick thread (`ITickCompletionQueue.Track` throws off it). The `SessionHandshake` codec and its dispatcher registration are not part of this story (schema pending OQ-NC-SER-2).
- [ ] **Order of the checks in `BeginEntry`**: (1) a load already pending for the connection → ignored, one warning per connection (`EntryHandshakeDuplicate`) (user decision 2026-10-23); (2) target `ZoneID` ≠ this zone → `ZoneFullResponse(sent zoneId, ZoneDoesNotExist, 0)`; (3) token rejected → `ZoneFullResponse(sent zoneId, Other, 0)`, connection kept open (user decision 2026-10-23); (4) **pre-check** (user decision 2026-10-23; CR-ZI-7 step 2 "no character data is loaded"): zone not Active → `ZoneDoesNotExist, 0`; `OccupiedPlayerCount == maxPlayers` → `ZoneFull, ZONE_FULL_RETRY_AFTER_SECONDS`; (5) `LoadCharacter` started. No reply in (2)-(4) starts a `LoadCharacter`.
- [ ] **Token check (user decision 2026-10-22)**: through an injected `IZoneEntryTokenValidator`; its stand-in `RejectingZoneEntryTokenValidator` rejects every token and logs "[ZoneServer] zone entry token validation not implemented" once per instance (until the gateway / Authentication work defines cross-process validation).
- [ ] **Step 3**: `LoadCharacter` started with a `CancellationTokenSource` the service owns (disposed in the callback) and passed to `Track` in the same statement. In the callback, `TickTaskStatus != Completed` (Faulted, Canceled, TimedOut) or `CharacterLoadCode != Success` → `ZoneFullResponse(LoadCharacterFailed, 0)`, abort.
- [ ] **Step 4 / EC-ZI-1 / AC-ZI-27 (re-check)**: in the callback, if the zone is not Active (Loading, Draining or Closed) → `ZoneFullResponse(ZoneDoesNotExist, 0)`, no slot, no EntityID, no `SaveSession`; if at cap → `ZoneFullResponse(ZoneFull, ZONE_FULL_RETRY_AFTER_SECONDS)`, no slot. `TryAllocatePlayer` failing below cap (EntityID counter exhausted) → `ZoneFullResponse(Other, 0)` and one error log.
- [ ] **AC-ZI-4 (integration)**: two handshakes against 49 / 50 both pass the pre-check; their loads complete in the same `Drain` → one slot, one `ZoneFull(30)`; `OccupiedPlayerCount` 50.
- [ ] **EC-ZI-4**: the losing join's loaded record is discarded; no `ApplyLoadedRecord` and no persistence call follows.
- [ ] **Step 5**: on success, `TryAllocatePlayer` mints the EntityID and writes the slot (`CharacterID`, `AccountID`, client id, `Connected`); then `ApplyLoadedRecord(entityId, record)` in the same callback (user decision 2026-10-23). `ApplyLoadedRecord` ≠ `Success` → `ReleasePlayer`, `ZoneFullResponse(LoadCharacterFailed, 0)`, no hand-off, no `SaveSession`. `Success` → `IZoneEntryContinuation.OnPlayerAdmitted(clientId, slot, entityId)` (Story 010 implements it).
- [ ] **Connection drop**: `ZoneEntryAdmission` implements `IClientConnectionListener`; `RemoveClient(clientId)` with a pending load marks it dropped. Its callback then sends nothing, allocates no slot, and calls neither `ApplyLoadedRecord` nor any persistence method. A later handshake on the same id after `AddClient` is a fresh entry.

---

## Implementation Notes

- `ZONE_FULL_RETRY_AFTER_SECONDS = 30` (`byte`), a `public const` default passed through the constructor (Story 008's knob pattern).
- Files: `src/ServerLogic/ZoneInstancing/ZoneEntryAdmission.cs`, `ZoneEntryRequest.cs`, `IZoneEntryTokenValidator.cs`, `RejectingZoneEntryTokenValidator.cs`, `IZoneEntryContinuation.cs`. Concrete `ZoneSlotPool` and `ZoneLifecycle`, as in Stories 007 / 008; `ICharacterPersistence` for the load, and a narrow seam for `ApplyLoadedRecord` (it is on `CharacterPersistenceService`, not on the interface) so tests can force `InventoryNotRestored`.
- Pending table: one entry per connection id (`Dictionary<uint, PendingEntry>` with a dropped flag); the callback looks its entry up by client id and removes it. Replies go through `IClientMessageOutbox` like Story 008's (the `ZoneFullResponse` routing row already exists).
- Log prefix `[ZoneEntryAdmission]` for the service's own lines; the token stand-in keeps the user-specified `[ZoneServer]` text.
- Story 014 wires the service: `IClientConnectionListener` fan-out with the outbox (the adapter takes one listener), the real continuation from Story 010, and the token stand-in.

**Open decisions**: resolved at readiness 2026-10-23 (user). Rejected token → `ZoneFullResponse(Other, 0)`, connection kept open. Second handshake while one is pending → ignored. Story 009 calls `ApplyLoadedRecord`; if it fails, the slot is released. Pre-check before the load, re-check at step 4.

**Performance**: connection-driven, not per tick; no allocation per tick. One `Task` and one `CancellationTokenSource` per handshake that passes the pre-check; the pending table is bounded by the connection count.

---

## Out of Scope

- Story 010: steps 6–10 (snapshot, `SessionReady`, party, `PlayerJoinedZone`, tick loop) and the connection-directory fill
- Story 011: duplicate-`CharacterID` handshake (stale slot)
- The `SessionHandshake` codec and the real token validator

---

## QA Test Cases

- **LoadCharacterFailed** — fake persistence returns a failure → `ZoneFullResponse(LoadCharacterFailed, 0)`; no slot.
- **AC-ZI-27** — load pending; zone moves to Draining; load completes → `ZoneDoesNotExist`; no slot, no EntityID, no `SaveSession`.
- **AC-ZI-4** — 49 / 50; two loads complete in one tick → one admitted, one `ZoneFull(30)`; count 50.
- **Token stand-in** — rejected → `ZoneFullResponse(Other, 0)`, no load; two rejections → one log.
- **Wrong zone** — target `ZoneID` ≠ this zone → `ZoneDoesNotExist`, no load.
- **Pre-check** — zone Loading → `ZoneDoesNotExist`, no load; 50 / 50 → `ZoneFull(30)`, no load.
- **Task not completed** — faulted / timed-out load → `LoadCharacterFailed`.
- **Apply fails** — `ApplyLoadedRecord` returns `InventoryNotRestored` → slot released (count back), `LoadCharacterFailed`, no continuation, no `SaveSession`.
- **Duplicate handshake** — second `BeginEntry` while pending → ignored, one warning; one `LoadCharacter` call.
- **Connection drop** — `RemoveClient` while pending; load completes → no reply, no slot.
- **Success** — slot `Connected` with the right `CharacterID`; continuation called once with the slot's EntityID.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_EntryAdmission_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 006 (all Complete); Character Persistence Stories 003 and 004 (`LoadCharacter`, `ApplyLoadedRecord`, Complete); Networking Core Story 030 (`ITickCompletionQueue`, Complete)
- Unlocks: Stories 010, 011
