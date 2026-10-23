# Story 010: Player Entry, Part 2 — Snapshot, SessionReady, Party, PlayerJoinedZone (CR-ZI-8 steps 6–10)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22; readiness 2026-10-23 (NEEDS WORK, 10 gaps -> fixed)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 7 hours (readiness 2026-10-23: second-handshake guard, party seam, activation list and directory added; activity sink and "entering" moved out)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 steps 6–10, CR-ZI-14 (entry), dual-gate invariant; `networking-session.md` B-NP-8; `networking-channel-contract.md` (`PlayerJoinedZone` ordering); `party-system.md` (solo pseudo-party)
**Requirement**: `TR-zi-007`, `TR-zi-014` *(epic placeholders)*

**ADR Governing Implementation**: ADR-014 (connection directory, tick-loop entry); ADR-010 (Party System by direct call on an injected interface; Zone Instancing uses the narrower `IPartyZonePresence`, dated note 2026-10-23)
**ADR Decision Summary**: once a connection has a character, the dispatcher finds it through `IConnectionCharacterDirectory`; Party is reached by direct calls.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (no Unity API beyond `Debug.Log*`).

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces (Party) — ADR-010
- Required: a core system that ticks runs in `ZoneTickPipeline` step 3 — ADR-014
- Required: zone state mutated only on the main thread — ADR-011
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **Hand-off**: a `ZoneEntryCompletion` implements Story 009's `IZoneEntryContinuation`; `OnPlayerAdmitted(clientId, slot, entityId)` runs steps 6–9 in that call and schedules step 10. It never throws out of the call (an exception is caught, logged as an error, and the remaining steps are skipped).
- [ ] **Order** for an admitted player: (6) `ZoneSnapshotBuilder.SendSnapshot(clientId)` (Story 007) → (7) `SessionReady`, then `SkillCooldownSnapshot`, to the joiner → (8) `NotifyZoneEntry(PartyID, EntityID, ZoneID)` if the player is in a formal party → (9) `PlayerJoinedZone` to every other client in the zone (not the joiner) → (10) the entity enters the tick loop on the next tick boundary. A test asserts the order through one shared call log across the outbox fake, the responder fake and the party fake.
- [ ] **Steps 7 seam (user decision 2026-10-22)**: `SessionReady` and `SkillCooldownSnapshot` go through an injected `IZoneEntryResponder` (`SendSessionReady(clientId)`, `SendSkillCooldownSnapshot(clientId, entityId)`); the codecs are not part of this story (schema pending OQ-NC-SER-2). Its stand-in `LoggingZoneEntryResponder` logs "[ZoneServer] SessionReady not implemented" once per instance.
- [ ] **Step 8 party seam (user decision 2026-10-23)**: an injected `IPartyZonePresence` with `bool TryGetFormalParty(CharacterID, out PartyID)`, `NotifyZoneEntry(PartyID, EntityID, ZoneID)` and `NotifyZoneExit(PartyID, EntityID)`. A solo pseudo-party counts as "not in a party" (`TryGetFormalParty` → false): no `NotifyZoneEntry` call. The stand-in `NoPartyZonePresence` returns false and logs "[ZoneServer] party zone presence not implemented" once per instance.
- [ ] **Step 9 data**: `PlayerJoinedZone(entityId, characterName, level, posX, posY, posZ)` is filled from `IZoneEntityStateSource.TryGetPlayerState(entityId)` (the snapshot's read seam). A miss → one warning, no `PlayerJoinedZone`, step 10 still runs. Sent through `IClientMessageOutbox` to every client id of `GetPlayerEntityIDsInZone` except the joiner; queued in the same call as step 5, so before any R-U / U-U message that names the EntityID (channel contract ordering).
- [ ] **Step 10 (user decision 2026-10-23)**: the EntityID is added to a pending-activation list; `ActivatePending(currentTick)` (called by `ZoneTickPipeline` step 3 — Story 014 wires it) moves every pending EntityID into the active set at the start of the next tick. `IsInTickLoop(EntityID)` is false in the admission tick and true after the next `ActivatePending`. An EntityID whose slot was released before activation is dropped from the pending list (checked with `ZoneSlotPool.TryGetPlayerSlot`). `Deactivate(EntityID)` removes it (Story 013 calls it on exit).
- [ ] **Connection directory**: `ZoneConnectionCharacterDirectory` implements `IConnectionCharacterDirectory`. `OnPlayerAdmitted` adds (clientId → `CharacterID` of the slot) before step 6; `TryGetCharacterId` then returns it, and the dispatcher no longer drops that connection's requests as `NoCharacter`. `Remove(clientId)` deletes the entry (Story 012 teardown and Story 013 exit call it; this story tests it directly).
- [ ] **Second handshake on an admitted connection (user decision 2026-10-23)**: `ZoneEntryAdmission` (Story 009 file) takes the directory through its constructor; `BeginEntry` from a connection the directory already maps is ignored with one warning per connection (`EntryHandshakeAlreadyAdmitted`), checked first, together with the pending-duplicate check: no reply, no load. The warning flag is cleared by `RemoveClient`.

---

## Implementation Notes

- Files: `src/ServerLogic/ZoneInstancing/ZoneEntryCompletion.cs`, `IZoneEntryResponder.cs`, `LoggingZoneEntryResponder.cs`, `IPartyZonePresence.cs`, `NoPartyZonePresence.cs`, `ZoneConnectionCharacterDirectory.cs`, `ZoneTickRoster.cs` (pending list + active set, `ActivatePending`, `IsInTickLoop`, `Deactivate`); modify `ZoneEntryAdmission.cs` (directory guard). `PartyID` lives in namespace `IronGrind.LootTableSystem` (`src/ServerLogic/LootTableSystem/PartyID.cs`) — reuse it.
- `CharacterID` for the directory and party lookup comes from `ZoneSlotPool.TryGetPlayer(entityId)`; client ids for step 9 come from the pool's player slots.
- `SendSnapshot` reports refused fragments only as a count; a full outbox queue disconnects the client and the exit path cleans up (Story 013). Steps 7–10 run regardless.
- `IZoneEntityStateSource` has no production implementation yet; tests use a fake. Story 014 notes the stand-in.
- Log prefix `[ZoneEntryCompletion]`; stand-ins use the `[ZoneServer] … not implemented` text.
- **Resolved at readiness 2026-10-23 (user):** second handshake on an admitted connection → ignored by `ZoneEntryAdmission` through the directory; party seam `IPartyZonePresence`, solo → no call; the real `IConnectionActivitySink` is deferred to the session epic (no consumer: `ConnectionStateMachine` is not constructed in the composition); "entering" moved to Story 011; step 10 = activation list.

**Performance**: runs once per admission, not per tick. Step 9 is O(players in zone) per entry (one `PlayerJoinedZone` body written into a preallocated buffer, enqueued per client). `ActivatePending` is O(pending) per tick, zero allocation (preallocated list and set sized to `maxPlayers`).

---

## Out of Scope

- Story 011: the "entering" state and stall timeout (moved there at this readiness), duplicate-`CharacterID` handshake
- Story 013: `NotifyZoneExit` on exits, calling `Deactivate` and directory `Remove`
- Story 014: composition wiring (directory into the dispatcher and `ZoneEntryAdmission`, `ActivatePending` in step 3, stand-ins)
- The real `IConnectionActivitySink` (session epic); the `SessionReady` / `SkillCooldownSnapshot` codecs; the real Party System; a production `IZoneEntityStateSource`
- AC-ZI-6 (client dual-gate) — client app

---

## QA Test Cases

- **Order** — Given one existing client and a joiner in a formal party; When admitted; Then the shared log reads: snapshot fragments to the joiner, `SessionReady`, `SkillCooldownSnapshot`, `NotifyZoneEntry`, `PlayerJoinedZone` to the existing client; the joiner gets no `PlayerJoinedZone`.
- **Solo player** — `TryGetFormalParty` false → no `NotifyZoneEntry`; the rest of the order unchanged.
- **Stand-ins** — two admissions → one log line each from `LoggingZoneEntryResponder` and `NoPartyZonePresence`.
- **PlayerJoinedZone data** — name, level and position equal the state source's view; seam miss → one warning, no `PlayerJoinedZone`, entity still activated next tick.
- **Directory** — after admission `TryGetCharacterId(connection)` returns the character; a request from that connection is dispatched by `InboundRequestDispatcher` (not dropped as `NoCharacter`); after `Remove` it is dropped again.
- **Second handshake** — admitted connection sends `BeginEntry` twice → no load, no reply, one `EntryHandshakeAlreadyAdmitted` warning; after `RemoveClient` + directory `Remove`, a new handshake is a fresh entry.
- **Tick entry** — `IsInTickLoop` false after admission, true after the next `ActivatePending`; a slot released before activation is not activated; `Deactivate` removes it.
- **Continuation throws** — a fake responder throws → error logged, `OnPlayerAdmitted` returns normally, no party call or `PlayerJoinedZone`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_EntryCompletion_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 009 (Complete); Networking Core Story 044 (`IConnectionCharacterDirectory` seam and dispatcher, Complete)
- Unlocks: Stories 011, 013, 014
