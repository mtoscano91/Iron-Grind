# Story 010: Player Entry, Part 2 — Snapshot, SessionReady, Party, PlayerJoinedZone (CR-ZI-8 steps 6–10)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 steps 6–10, CR-ZI-14 (entry), dual-gate invariant; `networking-session.md` B-NP-8; `networking-channel-contract.md` (`PlayerJoinedZone` ordering)
**Requirement**: `TR-zi-007`, `TR-zi-014` *(epic placeholders)*

**ADR Governing Implementation**: ADR-014 (connection directory, activity sink, tick-loop entry); ADR-010 (Party System by direct call on an injected interface)
**ADR Decision Summary**: once a connection has a character, the dispatcher finds it through `IConnectionCharacterDirectory` and records activity through `IConnectionActivitySink`; Party is reached by direct calls.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces (Party) — ADR-010
- Required: a core system that ticks runs in `ZoneTickPipeline` step 3 — ADR-014
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **Order** for an admitted player (Story 009): (6) snapshot fragments queued to the joining client (Story 007) → (7) `SessionReady` queued only after every fragment is queued, then `SkillCooldownSnapshot` → (8) `NotifyZoneEntry(PartyID, EntityID, ZoneID)` if the player is in a party → (9) `PlayerJoinedZone` to every existing client of the zone (not the joiner) → (10) the entity enters the tick loop on the next tick boundary. A test asserts this order through the observer / fakes.
- [ ] **Seam (user decision 2026-10-22)**: `SessionReady` and `SkillCooldownSnapshot` are sent through an injected interface (for example `IZoneEntryResponder`); the `SessionReady` codec is not part of this story (schema pending OQ-NC-SER-2). Its stand-in logs once.
- [ ] Party is reached through an injected interface with `NotifyZoneEntry` / `NotifyZoneExit` (Party System has no epic); its stand-in logs once. A player not in a party gets no call.
- [ ] `PlayerJoinedZone` for an EntityID is queued before any R-U / U-U message that names it (channel contract ordering).
- [ ] The real `IConnectionCharacterDirectory` and `IConnectionActivitySink` replace the Story 042–045 stand-ins: after step 5 the dispatcher resolves the connection to its `CharacterID` (no more `NoCharacter` drop for an admitted player); entries are removed when the slot is released.
- [ ] The admitted slot counts as "entering" from step 4 until step 10 (T-7 does not fire on it; Story 011 uses it).

---

## Implementation Notes

- Steps 6–10 run in the same main-thread continuation as step 5, except step 10, which takes effect on the next tick boundary.
- Character fields for `PlayerJoinedZone` (name, level, position) come from the loaded character (`CharacterLoadResult`).

---

## Out of Scope

- Story 013: `NotifyZoneExit` on exits
- The `SessionReady` / `SkillCooldownSnapshot` codecs; the real Party System
- AC-ZI-6 (client dual-gate) — client app

---

## QA Test Cases

- **Order** — Given one existing client and a joiner in a party; When admitted; Then the joiner's outbound stream has all fragments then `SessionReady` then `SkillCooldownSnapshot`; party fake records `NotifyZoneEntry` after `SessionReady` was queued; the existing client receives `PlayerJoinedZone`; the joiner does not.
- **No party** — no `NotifyZoneEntry` call.
- **Directory** — after admission, `TryGetCharacterId(connection)` returns the character; a request from that connection is dispatched (not dropped as `NoCharacter`).
- **Tick entry** — the entity is absent from the current tick's processing and present on the next.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_EntryCompletion_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 009
- Unlocks: Stories 011, 013, 014
