# Epic: Party System

> **Layer**: Feature
> **GDD**: design/gdd/party-system.md
> **Architecture Module**: Party System (`architecture.md` layer map, Feature), `src/ServerLogic/` (ADR-012)
> **Status**: Complete (2026-11-19)
> **Stories**: 12 created 2026-11-18 — see Stories
> **Manifest Version**: 2026-11-06

## Overview

The Party System is the server authority on group membership for 2–4 players in a shared zone instance. Every player who is not in a formal party has a unique non-zero solo PartyID, so loot attribution never collides. A leader invites another player (±5 levels of the leader, inclusive); the invite expires after 60 s, is re-sent once after 5 s, and further invites to the same player queue. Membership is a join-order-stable array of `MAX_PARTY_SIZE = 4` CharacterIDs, compacted on removal, with a leader index and a round-robin loot cursor (`rrNextIndex`) that skips Ghost, Disconnected, OutOfZone, Dead and empty slots. On a mob kill the Party System computes each eligible member's XP with F-PS-1 (`RoundToInt(Max(0, XP_base × (1 − 0.10 × (N_eligible − 1))))`) and calls `AddExperience` per member. Leadership moves at once when the leader leaves or disconnects; disconnected slots are held for the session TTL, with a 5 s reconnect grace that suppresses the Ghost broadcast. Every membership change sends a full `PartyStateUpdate` (R-OD); member HP / MP stream as `PartyMemberHealthUpdate` (R-U, ≤ 20 Hz, 5 % delta and full-health suppression).

## Scope (user decisions 2026-11-18, `/create-epics`)

**Server only.** As with Death & Respawn: the party panel and invite banner (GDD UI Requirements, UX flag → `/ux-design` for `design/ux/party-panel.md` and `design/ux/invite-banner.md`) and party audio go to presentation epics. Party Chat is its own epic later.

**Zone-local parties at MVP.** Each zone instance is its own server process and the gateway holds no party data (ADR-016). A party lives in one zone process: invites work only between players in the same zone instance; a member who leaves the zone is `OutOfZone` (XP / loot ineligible) until the member returns or the slot TTL expires. Cross-instance parties (party co-location routing, CR-ZI-6 rule 1; members in different processes) are deferred to a later ADR on the ADR-015 relay. Recorded as an ADR-010 note (2026-11-18).

What already exists (built by earlier epics):

| Piece | Where |
|-------|-------|
| Loot seam `IPartyService` (`GetPartyID`, `GetPartyMembers`, `GetMemberAtIndex`, `GetRrNextIndex`, `AdvanceRrNextIndex`, `IsMemberConnected`), `PartyID`, `PartyTagTracker` | `src/ServerLogic/LootTableSystem/` |
| Zone seam `IPartyZonePresence` (`TryGetFormalParty`, `NotifyZoneEntry`, `NotifyZoneExit`); production uses `NoPartyZonePresence` | `src/ServerLogic/ZoneInstancing/` (called from `ZoneEntryCompletion`, `ZoneEntryReclamation`, `ZoneCloseSteps`) |
| Skill party targeting source (built on `NoPartyZonePresence`) | `src/ServerLogic/SkillSystem/ZoneSkillCastPartySource.cs` |
| Death seam `IPartyMemberStatusSink` (Dead on death, Online on respawn); production uses `NoOpPartyMemberStatusSink` | `src/ServerLogic/DeathAndRespawn/` |
| Party disband during the ghost period (EC-GH-7 / AC-GH-18), static coordinator | `src/ServerLogic/Networking/GhostSession/PartyDisbandCoordinator.cs` |
| Kill XP: direct `AddExperience` to the attacker (no party split) | `AutoAttackCombatSystem.cs:571`, `SkillDamageExecutionStep.cs:126` |
| `PartyMemberHealthUpdate` codec (0x0305, R-U) | `src/Foundation/Networking/WireProtocol/PartyMemberHealthUpdate.cs` |
| Wire schemas `PartyStateUpdate`, `PartyDisbanded`, `PartyInviteRequest/Received/Response`, `PartyMemberStatus` (design only) | `design/gdd/networking-wire-protocol.md` §Party System Messages |

Gaps this epic closes: no party exists in production; kill XP always goes to the attacker alone; the loot, zone, skill and death seams have no real implementation.

Moved out:

| Item | Owner |
|------|-------|
| Party panel, invite banner, join / leave / invite audio (GDD Visual/Audio and UI Requirements) | Presentation epics (HUD — OQ-HUD gates; UX specs first) |
| Party Chat (`party-chat.md`) | Party Chat epic (later) |
| Cross-instance parties, party co-location routing (CR-ZI-6 rule 1, OQ-ZI-9) | Later ADR (ADR-015 relay) |
| Real loot drops reaching the round-robin cursor: production still composes `StandInLootTableService` | Loot wiring work (the cursor is tested at service level here) |
| Gold party split F-PS-2 (pending Loot Table amendment) | Loot Table amendment |

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event and Messaging Architecture | Direct-call seams `IPartyService` (loot), `IPartyZonePresence` (zone entry / exit), `IPartyMemberStatusSink` (death); party kill credit on mob death; zone-local party note (2026-11-18) | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | `PartyInviteRequest` (4 B) and `PartyInviteResponse` (5 B) are R-OD requests handled on the tick; invite timers and the party step get an explicit place in the phase list | LOW |
| ADR-011: Async Persistence and the Tick Loop | Disconnected slots held for the session TTL; ghost-period disband through `PartyDisbandCoordinator` | LOW |
| ADR-012: Server/Client Assembly Boundary | Written in `IronGrind.ServerLogic`; wire codecs in Foundation | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-18). No requirement needs a new ADR (cross-instance parties are out of scope).

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-party-001 | Solo PartyID: every player outside a formal party has a unique non-zero solo PartyID (`IsSoloParty`, write-once); 0 never in stable state; solo party cleaned up on join or TTL (CR-PS-1, AC-PS-1) | ADR-010 ✅ |
| TR-party-002 | Membership: 2–4 members, join-order array of `MAX_PARTY_SIZE`, compaction on removal, one party per player, capacity re-checked on accept (CR-PS-2, AC-PS-2, AC-PS-23) | ADR-010 ✅ |
| TR-party-003 | Level gate: member within ±5 of the leader's level at invite / accept, inclusive; a leader change never removes members (CR-PS-3, AC-PS-3 to AC-PS-5) | ADR-014 ✅ |
| TR-party-004 | Invite flow: `PartyInviteRequest` / `PartyInviteReceived` / `PartyInviteResponse`, 60 s expiry, one retry at 5 s, one pending invite per recipient with a queue, send-acknowledgement to queued senders, same zone instance only (CR-PS-4, AC-PS-6 to AC-PS-8, AC-PS-29) | ADR-014 ✅ |
| TR-party-005 | XP: F-PS-1 per eligible member (same zone, ±5 of leader, not Ghost / OutOfZone / Dead / Disconnected), `N_eligible = 0` awards nothing, eligibility snapshot before same-tick membership changes, Disbanding still awards; replaces the direct attacker `AddExperience` (CR-PS-5, CR-PS-10, AC-PS-9 to AC-PS-15, AC-PS-24, AC-PS-30) | ADR-010 ✅ |
| TR-party-006 | No drop-rate bonus: party size never modifies drop rolls (CR-PS-6, AC-PS-16) | ADR-010 ✅ |
| TR-party-007 | Round-robin cursor: `AdvanceRrNextIndex` advances, skips ineligible and empty slots (visit-counted), leaves the cursor unchanged and logs when none is eligible, clamps on shrink, serialized per party (CR-PS-7, AC-PS-17 to AC-PS-20, AC-PS-27) | ADR-010 ✅ |
| TR-party-008 | Leader: first member leads; transfer on leave or disconnect to the lowest non-empty non-Ghost index (wrap), immediate on disconnect, no reclaim on reconnect; `LeaderIndex` tracks compaction (CR-PS-8, AC-PS-21, AC-PS-22, AC-PS-28) | ADR-010 ✅ |
| TR-party-009 | Leave, disband, disconnect: voluntary leave gives a new solo PartyID; leader disband → Disbanding → cleanup after acks or 10 s; last member auto-disbands; disconnected slot held for `SESSION_TTL_SECONDS` then treated as leave; 5 s reconnect grace suppresses the Ghost broadcast (CR-PS-9, CR-PS-11, AC-PS-25, AC-PS-26) | ADR-011 ✅ |
| TR-party-010 | Member status: Online / Dead / Ghost / OutOfZone / Disconnected per slot; real `IPartyMemberStatusSink` (Death & Respawn Dead / Online, slot retained), `IPartyZonePresence` entry / exit → OutOfZone; `GetPartyMembersWithStatus` (`PartyMemberSlot[]`); the Death & Respawn party ACs (AC-DR-13b, AC-DR-18, AC-DR-NEW-4, AC-DR-NEW-7 to NEW-9) | ADR-010 ✅ |
| TR-party-011 | Wire output: `PartyStateUpdate` (R-OD, full snapshot, per-party `sequenceId`) on every membership, leader, status, eligibility or cursor change; `PartyDisbanded` (R-OD); `PartyMemberHealthUpdate` (R-U, absolute values, ≤ 20 Hz to Online / Dead members, 5 % delta and full-health suppression) | ADR-010, ADR-014 ✅ |
| TR-party-012 | Zone wiring: the service built per zone and bound to the loot, zone, skill and death seams; party step in the phase list; end-to-end check that two players form a party, share a kill's XP by F-PS-1 and see a `PartyStateUpdate` | ADR-010, ADR-014 ✅ |

## Open Items for Story Readiness

1. **GDD leftovers from the 6 → 4 resize.** CR-PS-8 ("6-slot member array") and the `rrNextIndex` edge case ("fills slot index 5") still say 6; read them as 4 and fix the GDD at the first story that touches them.
2. **PartyID counter (OQ-PS-2).** Range, wrap-around and restart behaviour are open. Zone-local parties live only as long as the zone process, so a per-process counter may be enough; decide at the first story.
3. **OutOfZone (OQ-PS-1).** Under the zone-local decision, OutOfZone means "left this zone process"; check `zone-instancing.md` entry step 8 and teardown step 5 for the exact calls.
4. **Message IDs.** `PartyStateUpdate`, `PartyDisbanded`, `PartyInviteReceived`, `PartyInviteRequest`, `PartyInviteResponse` need MessageTypeIDs and codecs (R-OD ids in 0xE000–0xEFFF); check the routing registry first.
5. **Kill XP hand-off.** Both kill paths (`AutoAttackCombatSystem`, `SkillDamageExecutionStep`) call `AddExperience` on the attacker; the party split needs one seam both use (direct call, ADR-010).
6. **`PartyDisbandCoordinator` and `GhostXpPoolTracker`.** Built by Networking Core; decide whether the party service drives them or replaces them. **Resolved at Story 009 readiness (2026-11-19):** not driven at MVP — neither is constructed in production and Ghost members are XP-ineligible (CR-PS-5), so there is no post-disconnect share to stop.
7. **Leveling F-LS-2 (OQ-PS-4).** `leveling-system.md` still shows the old additive bonus; F-PS-1 is authoritative.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/party-system.md` are verified, except those moved out above
- The Death & Respawn party criteria moved here (AC-DR-13b, AC-DR-18, AC-DR-NEW-4, AC-DR-NEW-7 to NEW-9) are verified
- All Logic and Integration stories have passing test files in `tests/`
- Two players in a production-composed zone can form a party and split kill XP by F-PS-1

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Party config and F-PS-1](story-001-party-config-and-xp-formula.md) | Logic | Complete | ADR-012 |
| 002 | [Party registry — solo PartyIDs and membership](story-002-party-registry.md) | Logic | Complete | ADR-010 |
| 003 | [Leader index and round-robin cursor](story-003-leader-and-round-robin.md) | Logic | Complete | ADR-010 |
| 004 | [Member status and XP eligibility](story-004-member-status-and-eligibility.md) | Logic | Complete | ADR-010 |
| 005 | [Party XP distribution](story-005-party-xp-distribution.md) | Integration | Complete | ADR-010 |
| 006 | [Party wire messages](story-006-party-wire-messages.md) | Logic | Complete | ADR-014 |
| 007 | [Invite flow](story-007-invite-flow.md) | Integration | Complete | ADR-014 |
| 008 | [Leave, disband and state broadcast](story-008-leave-disband-and-state-broadcast.md) | Integration | Complete | ADR-014 |
| 009 | [Disconnect, reconnect grace and zone presence](story-009-disconnect-grace-and-zone-presence.md) | Integration | Complete | ADR-011 |
| 010 | [Party health stream](story-010-party-health-stream.md) | Logic | Complete | ADR-014 |
| 011 | [Zone composition wiring](story-011-zone-composition-wiring.md) | Integration | Complete | ADR-014 |
| 012 | [End-to-end — two players party up and share a kill](story-012-end-to-end-party-kill.md) | Integration | Complete | ADR-014 |

Order: 001 → 002 → 004 → 006 → 003 → 005 (006 before 003 for the XP-ineligible notice, Story 004 readiness 2026-11-19); 007 and 008 after 006; 009 after 008; 010 after 008; 011 after 005 and 007–010; 012 last.

## Findings at `/create-stories` (2026-11-18)

1. **Missing client → server messages.** The wire protocol has no leave request, disband request or invite result / error message, though CR-PS-4 and CR-PS-9 need them; Story 006 adds them (ids 0xE170–0xE17F) and amends `networking-wire-protocol.md`. Readiness 2026-11-19 fixed the IDs (0xE170 `PartyStateUpdate` … 0xE178 `PartyXpIneligibleNotice`), empty-body leave / disband requests, `PartyInviteResult` (9 codes, also the non-leader disband answer) and the ADR-014 table rows; dispatcher registration of the four request types moves to Stories 007 / 008.
2. **`PartyMemberStatusChanged`** is named in the GDD but has no schema; status travels in `PartyStateUpdate`.
3. **Disband acknowledgements** are not in the protocol: Story 008 uses the 10 s timeout only.

## Next Step

Stories 001, 002 (2026-11-18), 004, 006, 003, 005, 007, 008, 009, 010, 011 and 012 (2026-11-19) Complete. **Epic COMPLETE 2026-11-19 (12/12).** Story 012 drove two clients end to end in an EditMode composition (invite, accept, shared skill kill by F-PS-1, leave / auto-disband, second kill at full XP, bystander unaffected; party step p95 about 0.002 ms against the 0.1 ms guardrail) and found that no production path registered players with Leveling, so kill XP was dropped: fixed in `CharacterPersistenceService.ApplyLoadedRecord` (unregister on release is TD-115). Carried: loot round-robin end to end (TD-107, loot not composed), party UI to presentation, Party Chat later, TD-114. Next: roadmap step 3 (shop / consumables).
