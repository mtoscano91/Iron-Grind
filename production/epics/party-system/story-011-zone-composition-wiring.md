# Story 011: Zone Composition Wiring

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — Interactions table (all rows), CR-PS-6
**Requirement**: `TR-party-012` (wiring), `TR-party-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: The composition root builds each service once per zone, registers inbound handlers, and places each step in the explicit phase list; ADR-010 Tier 1 services are injected, not looked up.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — composition only.

**Control Manifest Rules (this layer)**:
- Required: one composition root (`ZoneServerComposition`); internal override seams for tests
- Guardrail: added steps cost < 0.1 ms per tick at 4 parties of 4 (logged)

---

## Acceptance Criteria

- [ ] **AC-1 Built per zone**: the composed zone builds the party registry / service (config from `PartySystemConfigOverride` ?? defaults) and exposes it internally for tests; nothing is built twice; disposed at teardown.
- [ ] **AC-2 Seams bound**: the party service replaces `NoPartyZonePresence` (zone entry / reclamation / close steps and `ZoneSkillCastPartySource`), `NoOpPartyMemberStatusSink` (Death & Respawn), the XP sinks of Auto-Attack Combat and the Skill System (Story 005 distributor), and is the `IPartyService` handed to loot wherever loot takes one.
- [ ] **AC-3 Requests and steps**: `PartyInviteRequest`, `PartyInviteResponse`, `PartyLeaveRequest`, `PartyDisbandRequest` are registered with the dispatcher; the invite timer / disband timeout step and the state broadcast and health stream are in the phase list at documented positions (order recorded in the story at readiness).
- [ ] **AC-4 Listeners**: entry, slot release and session ghost / reconnect listeners from Story 009 are composed with the existing composites (release composite keeps Combat last).
- [ ] **AC-5 No drop bonus**: a structural / behavioural check that the loot path's drop parameters carry no party-size modifier (`PartyBonus` = 1.0 or no such field) (AC-PS-16).
- [ ] **AC-6 Existing tests**: existing composition tests still pass (any that pinned `NoPartyZonePresence` / `NoOpPartyMemberStatusSink` are updated with a note).

---

## Implementation Notes

- Follow Death & Respawn Story 008 (`BuildDeathAndRespawn`, `ForwardingPlayerDeathStateReader`, override seam, phase step); where a consumer is built before the party service exists, use a forwarding seam bound later.
- Check how loot is composed today (`StandInLootTableService`): if it takes no `IPartyService`, AC-5 is structural and the real-loot binding stays moved out (epic).
- **From Story 003 readiness (2026-11-19):** `PartyRegistry` itself implements `IPartyService` (no separate `PartyService`). Story 003 adds the seam `IPartyXpIneligibleNoticeSink.Notify(CharacterID, PartyID)` with a no-op default; this story writes and binds the production adapter: encode `PartyXpIneligibleNotice` (0xE178, 4-byte body `partyId`), resolve the character's client id, `IClientMessageOutbox.Enqueue` (owning client). Add it to the ACs at this story's readiness.
- **From Story 005 readiness (2026-11-19, G3):** the XP sink binding wraps the existing `CharacterStatsExperienceSink` with `PartyExperienceDistributor` (one instance, passed to both combat systems). Place Story 008's party membership step (queued leave / disband) after phase 3 so a same-tick kill awards before the removal, and record the position as an ADR-014 note.

---

## Out of Scope

- Story 012: end-to-end behaviour
- Real loot drops (moved out of the epic)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: `NavCompositionRig`-style composition → service non-null, same instance everywhere; teardown disposes.
- **AC-2**: reflection / accessor checks that each seam holds the party implementation; Death & Respawn sink is the party one.
- **AC-3**: dispatcher has the four ids; phase list order asserted.
- **AC-4**: release composite order asserted (Combat last).
- **AC-5**: the drop-parameter check.
- **AC-6**: full EditMode `!Database` green.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_Composition_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 005, 007, 008, 009, 010
- Unlocks: Story 012
