# Story 009: Disconnect, Reconnect Grace and Zone Presence

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-8 (immediate transfer on disconnect), CR-PS-9 (disconnect TTL), CR-PS-11, States (Ghost), Edge Cases (disconnect and reconnect); `zone-instancing.md` entry step 8, teardown step 5
**Requirement**: `TR-party-009` (disconnect / TTL / grace), `TR-party-010` (Ghost, OutOfZone)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-011: Async Persistence and the Tick Loop
**ADR Decision Summary**: Session end, ghost TTL and slot release run on the tick through the session machine and `SessionEndCoordinator`; Zone Instancing calls `IPartyZonePresence` on entry and exit (ADR-010 note 2026-10-23).

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: tick-based timers; existing session / slot listeners reused, not duplicated
- Guardrail: no per-tick allocation

---

## Acceptance Criteria

- [ ] **AC-1 Disconnect**: a member's connection drops → slot status Disconnected (then Ghost per the session machine's ghost state); if the member was leader, leadership moves within 1 tick to the lowest non-empty non-Ghost index and the others get a `PartyStateUpdate` showing it (AC-PS-21).
- [ ] **AC-2 Grace**: reconnect and handshake within 5 s → no Ghost status broadcast at all (outbound log has no status change for that member) (AC-PS-26); after 5 s the Ghost status is broadcast.
- [ ] **AC-3 Reconnect**: a Ghost member who reconnects within `SESSION_TTL_SECONDS` returns to Online in the same slot and is not leader again (AC-PS-22).
- [ ] **AC-4 TTL expiry**: the session expires → treated as a voluntary leave (Story 008 path); the player's party state is gone; an all-Ghost party is cleaned up when its last TTL expires.
- [ ] **AC-5 Zone presence**: a real `IPartyZonePresence` replaces `NoPartyZonePresence`: `TryGetFormalParty` reports real parties only; `NotifyZoneExit` marks the member OutOfZone (XP / loot ineligible — AC-PS-14 status half); `NotifyZoneEntry` restores Online; zone teardown's exit calls leave no stale state.
- [ ] **AC-6 Solo registration**: zone entry registers a solo PartyID for every player (Story 002) and slot release removes it.

---

## Implementation Notes

- Find the existing hooks first: slot release listener composite (`CompositeZoneSlotReleaseListener`), session machine ghost transitions (`SessionMachineGhostSource`, `ZoneSlotGhostStatusSource`), entry listener composite (`CompositeZonePlayerEntryListener`), and where `IPartyZonePresence` is called (`ZoneEntryCompletion`, `ZoneEntryReclamation`, `ZoneCloseSteps`).
- **From Story 006 (2026-11-19):** `PartyMemberStatusChanged` has no wire message — status rides in `PartyStateUpdate` (0xE170). AC-2 / AC-PS-26 "no Ghost status broadcast" means no `PartyStateUpdate` showing that member Ghost during the 5 s grace.
- Under zone-local parties, a member leaving the zone process is also leaving the party's process: decide at readiness whether exit → OutOfZone (held until TTL) or immediate leave; the GDD and ADR-010 note say OutOfZone until return or TTL.
- `PartyDisbandCoordinator` / `GhostXpPoolTracker` (Networking Core Story 020): decide whether the party service drives them (epic open item 6).

---

## Out of Scope

- Cross-instance parties and co-location routing (moved out of the epic)
- Story 011: composing these listeners in `ZoneServerComposition`

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: leader disconnect at tick T → new leader at T or T+1, one update.
- **AC-2**: reconnect at +99 ticks → no Ghost broadcast; at +101 → one Ghost broadcast.
- **AC-3**: Ghost reconnect → Online, same index, not leader.
- **AC-4**: TTL expiry → leave path; all-Ghost party → cleaned at last expiry.
- **AC-5**: exit → OutOfZone and ineligible; entry → Online; teardown leaves no entries.
- **AC-6**: entry → solo id; release → id gone.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_DisconnectAndPresence_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 003, 004, 008
- Unlocks: Story 011
