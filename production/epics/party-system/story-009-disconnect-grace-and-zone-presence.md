# Story 009: Disconnect, Reconnect Grace and Zone Presence

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 6 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-8 (leader transfer on disconnect), CR-PS-9 (disconnect TTL), CR-PS-11 (reconnect grace), States (Ghost), Edge Cases (disconnect and reconnect); `zone-instancing.md` entry step 8, teardown step 5
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

- [x] **AC-1 Disconnect and Ghost**: a new `PartyPresenceStep.Poll(currentTick)` reads each real-party member's session state through the member's pool slot `Account` (`ConnectionStateMachine.TryGetSessionState`, behind a narrow `IPartySessionStateSource` seam). `Disconnected_SessionActive` or `Reconnecting` = disconnected; `Connected` = connected; no slot / no session = ignored (release handles it, AC-4). A member first seen disconnected at tick T stays Online (registry status and wire) until T + `ReconnectGraceTicks` (100 at the default 5 s), then becomes Ghost. If that member was leader, leadership moves in the same `Poll` call to the lowest-index non-Ghost member (`PartyRegistry.TransferLeadership`), and the next `PartyBroadcastStep.Flush` sends one `PartyStateUpdate` showing the Ghost slot and the new leader (AC-PS-21, read per D3). Ghost members are XP / loot ineligible (existing Story 004 rule).
- [x] **AC-2 Grace**: disconnected at T, `Connected` again at T + 99 → no `PartyStateUpdate` ever shows that member Ghost, leadership unchanged, grace timer cleared (AC-PS-26). Disconnected at T and still disconnected at T + 100 → exactly one update showing Ghost. A second disconnect after a reconnect starts a fresh 100-tick grace.
- [x] **AC-3 Reconnect**: a Ghost member whose session is `Connected` again → status recomputed without the ghost flag (Online, or Dead if still dead — D4), same slot index, not leader again; one `PartyStateUpdate` (AC-PS-22).
- [x] **AC-4 Slot release = leave**: a new `IZoneSlotReleaseListener` adapter (`PartySlotReleaseListener`) handles `BeforeRelease(entity)` for every slot release (TTL expiry, ghost death, eviction, zone exit, teardown): pending / queued invites dropped (AC-7), `UnregisterPlayer`, and if the former real party now has exactly one member, the Story 008 auto-disband runs at once (`PartyDisbanded` to the remaining member if connected, solo update). Two or more remaining → the next `Flush` sends one update without the member. A party where every member is Ghost is cleaned up as each TTL expires: the first expiry of a 2-member all-Ghost party auto-disbands it; the last expiry unregisters the last solo player; `PartyCount` returns to its pre-party value. Unknown entity → no-op.
- [x] **AC-5 Zone presence**: a real `PartyZonePresence : IPartyZonePresence` replaces `NoPartyZonePresence` (wired in Story 011). `TryGetFormalParty` returns true only for a real (non-solo, non-freed) party; `NotifyZoneExit` sets the member's out-of-zone flag (OutOfZone, XP / loot ineligible — AC-PS-14 status half); `NotifyZoneEntry` clears it. Unknown party or entity → no-op. Zone teardown (step 5 exit calls, then slot releases) leaves `PartyCount` 0 and no presence / grace entries.
- [x] **AC-6 Solo registration**: a new `IZonePlayerEntryListener` adapter (`PartyPlayerEntryListener`) reads the slot's `CharacterID` from the pool on `OnPlayerEntering(slot, entity)` and calls `RegisterPlayer(character, entity)` → the player has a solo PartyID. A token-path reconnect does not re-register (the slot is kept). An already-registered character logs one warning and changes nothing. Slot release removes the registration (AC-4).
- [x] **AC-7 Invite cancellation**: when a member becomes Ghost (AC-1) or its slot is released (AC-4), `PartyInviteService.CancelFor(character)` removes every pending or queued invite where the player is the target or the inviter. A connected inviter whose target vanished gets one `PartyInviteResult Expired` (target CharacterID); a target whose inviter vanished is told nothing (its banner times out; an accept answers `Expired` as today). A player disconnected only within the grace keeps its invites.
- [x] **AC-8 Status precedence (D4)**: the registry keeps three flags per member — ghost, out-of-zone, dead — and derives `PartyMemberStatus` as Ghost > OutOfZone > Dead > Online. `PartyMemberStatusSink` (Death & Respawn) writes only the dead flag. A Ghost whose respawn timer fires stays Ghost (dead flag cleared); a dead player who disconnects shows Ghost and returns to Dead on reconnect if still dead. Existing Story 004 / 005 / 008 tests still pass.

---

## Implementation Notes

- Grace in ticks: add `PartySystemConfigValues.ReconnectGraceTicks => ToTicks(ReconnectGraceSeconds)` (100 at 5 s, 20 Hz).
- `PartyPresenceStep` holds a fixed-size per-member record (character → first-disconnected tick, or none). Pre-size from `MAX_PLAYERS_PER_ZONE`; reuse the `CopyRealPartyIds` buffer pattern from `PartyBroadcastStep`. No per-tick allocation. Solo players are not polled (no party to inform; a solo player's invites are handled by AC-7 at release).
- Story 011 order: invite `Tick` → dispatch → simulation → `ApplyQueued` → `Poll` → `Flush`. The session machine's heartbeat evaluation runs earlier in the zone wiring tick step, so `Poll` sees this tick's transitions.
- Factor the "one member left → auto-disband" tail of `PartyMembershipStep.ApplyLeave` into an internal method the release adapter shares; do not duplicate it.
- `UnregisterPlayer` already removes a real-party member with compaction and leader-index adjustment; when the released member was leader, apply CR-PS-8 (`TransferLeadership`) if the party still has ≥ 2 members.
- The adapters (`PartySlotReleaseListener`, `PartyPlayerEntryListener`, `PartyZonePresence`) are built and tested here against a real `ZoneSlotPool`; adding them to `CompositeZoneSlotReleaseListener` / `CompositeZonePlayerEntryListener` and `ZoneInstancingWiring.Party` is Story 011.
- **From Story 006 (2026-11-19):** `PartyMemberStatusChanged` has no wire message — status rides in `PartyStateUpdate` (0xE170). "No Ghost status broadcast" = no `PartyStateUpdate` showing that member Ghost.

---

## Readiness Decisions (2026-11-19)

- **D1 Disconnect signal.** No production code sets a pool slot to `Disconnected_SessionActive` on a heartbeat timeout (TD-105); the session machine does. Party polls the session machine state per member through the slot `Account` each tick. TD-105 stays a separate fix; when it lands, the source may switch to the slot without changing the step.
- **D2 Grace.** Starts at the tick the session is first seen disconnected; lasts `ReconnectGraceTicks`. During the grace the member is Online in the registry and on the wire. With the 10 s heartbeat timeout a silent drop shows as Ghost about 15 s after the last packet.
- **D3 Leader transfer at Ghost.** CR-PS-8 / AC-PS-21 ("within 1 tick of disconnect") and CR-PS-11 ("suppress Ghost entirely during the grace") conflict. Leadership transfers within 1 tick of the member becoming Ghost, so a mobile blip does not cost leadership permanently (AC-3: not leader again). GDD note added under AC-PS-21.
- **D4 Status precedence.** One status value cannot hold "dead" and "disconnected" at once; Death & Respawn's respawn (which runs while disconnected, D&R Story 007 AC-2) would overwrite Ghost with Online. Flags + derived status (AC-8).
- **D5 Reconnect.** Ghost flag cleared, same slot, no leadership restore.
- **D6 Release = leave.** Every slot release is a session end (or teardown) in this zone process: immediate leave without the Story 008 queue (the entity is gone by the next tick), then unregister. All-Ghost cleanup falls out of the Story 008 one-member auto-disband.
- **D7 OutOfZone.** In production `NotifyZoneExit` runs only on session end (`ZonePlayerExit` from `SessionEndCoordinator`) and teardown step 5, and the slot is released right after. OutOfZone is set and then removed by the release; holding an OutOfZone member until return is deferred to the zone-travel epic (no zone travel exists).
- **D8 Invites.** Cancelled at Ghost (not during the grace) and at release; the connected inviter is told `Expired`, the target is not.
- **D9 Ghost XP / disband coordinators.** `PartyDisbandCoordinator` and `GhostXpPoolTracker` (Networking Core Story 020) have no production construction, and Ghost members are XP-ineligible (CR-PS-5), so there is no post-disconnect share to stop. Not driven by the Party System at MVP (epic open item 6 closed).

---

## Out of Scope

- Cross-instance parties, co-location routing, and holding an OutOfZone member across zone travel (zone-travel epic)
- Setting the pool slot ghost state on a heartbeat timeout (TD-105)
- `PartyDisbandCoordinator` / `GhostXpPoolTracker` (D9)
- "You were removed from your party while offline" system message (GDD edge case; HUD / UX)
- Story 011: composing these adapters and steps in `ZoneServerComposition`

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-19. Fakes: `IPartySessionStateSource` (per account), recording `IPartyStateMessageSink` / `IPartyInviteMessageSink`; real `PartyRegistry`, `ZoneSlotPool`, `PartyMembershipStep`, `PartyBroadcastStep`.*

- **AC-1**: party A(leader) + B + C; A disconnected at 1000 → `Poll` 1000…1099 + `Flush` each tick: no update shows A Ghost, leader A; at 1100 A Ghost, leader B (lowest non-Ghost), one update; A ineligible for XP. Non-leader disconnect → Ghost, leader unchanged. Leader disconnect when every other member is Ghost → no transfer, one info log. `Reconnecting` counts as disconnected.
- **AC-2**: disconnect 1000, Connected 1099 → zero updates with A Ghost over 1000–1200, leader unchanged; disconnect again 1150 → Ghost at 1250 (fresh grace).
- **AC-3**: A Ghost, then Connected → status Online, index unchanged, leader still B, one update. A dead, disconnected, Ghost, reconnects → Dead.
- **AC-4**: release of a member of 3 → one update with 2 members, no solo update for the released player, `IsRegistered` false; release of the leader of 3 → leader transfers; release from a party of 2 → remaining gets `PartyDisbanded` + solo update; all-Ghost party of 2 → first release disbands, second release → `PartyCount` back to baseline; release of a solo player → solo party freed; unknown entity → no-op.
- **AC-5**: `TryGetFormalParty` true for a real party, false for solo / unregistered; exit → OutOfZone (one update; ineligible); entry → Online; teardown of a zone with a party of 3 (exit calls then releases) → `PartyCount` 0, presence step holds no entries.
- **AC-6**: entry → solo PartyID; second `OnPlayerEntering` for the same character → one warning, PartyID unchanged; token-path reconnect (`RebindClient`) → no listener call, same PartyID.
- **AC-7**: A invites D; D's slot released → A gets one `Expired` with D's id, `HasPending(D)` false; A's slot released while D has A's invite pending → no message to D, entry gone; A disconnected within grace → invite kept; A Ghost → invites cancelled.
- **AC-8**: dead flag + ghost flag → Ghost; respawn (sink Online) while Ghost → still Ghost; reconnect after respawn → Online; out-of-zone + dead → OutOfZone.
- **Allocation**: `Poll` with 4 parties of 4, no state changes, 100 ticks → no GC allocation (`GC.GetAllocatedBytesForCurrentThread` delta 0).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_DisconnectAndPresence_tests.cs` — must exist and pass

**Status**: [x] Created and passing (2026-11-19)

---

## Dependencies

- Depends on: Stories 003, 004, 007, 008 (all Complete)
- Unlocks: Story 011

---

## Completion Notes

**Completed**: 2026-11-19
**Criteria**: 8/8 passing (all automated)
**Deviations / implementation decisions (advisory)**:
- Ghost / OutOfZone flags are cleared at every re-solo in `PartyMembershipStep` (leave, auto-disband, disband cleanup) through `internal PartyRegistry.ClearPresenceFlags`, not inside `PartyRegistry.CreateSolo`: Story 004 tests pin that a bare registry re-solo keeps the status. A re-soloed Ghost is therefore Online / connected (solo players are not polled).
- Disconnect leave is the slot release (`PartySlotReleaseListener`, immediate), not `EnqueueLeave`: the entity is gone before the next membership step.
- Release order: grace record forgotten, `UnregisterPlayer`, auto-disband tail (if one member left) in `try`; `PartyInviteService.CancelFor` in `finally`. The leader of a released member is handled by `PartyRegistry.RemoveSlot` (lowest non-Ghost, else index 0) — no second transfer.
- `BecomeGhost`: `SetGhost` → `TransferLeadership` (if leader) → `CancelFor`; every member poll is in try/catch (logged), so one throw does not skip the rest of the tick.
- A Ghost leader is replaced as soon as a connected member is polled (covers "every other member was Ghost when the leader dropped").
- `CancelFor`: target vanished → the pending inviter and every queued inviter (if registered and not Ghost) get one `Expired` naming the target; inviter vanished → target told nothing, removed from queues, the next queued invite promoted by `Resolve` at the service's last seen tick.
- Session adapter: `Connected` → connected; `Disconnected_SessionActive` / `Reconnecting` → disconnected; any other state, no slot or no session → ignored.
- `PartyZonePresence` acts only when the entity's character is in the given party; `PartyPlayerEntryListener` logs one warning on a failed registration (incl. no pool slot).
- Grace records: pre-sized `Dictionary<CharacterID, uint>` (64) with a per-poll sweep of members no longer in a real party; zero-allocation poll pinned by test.
- `PartyRegistry.SetMemberStatus` kept as a test seam / legacy shim (Online clears all flags); production uses `SetGhost` / `SetOutOfZone` / `SetDead`.
**Test Evidence**: Integration — `tests/EditMode/PartySystem/PartySystem_DisconnectAndPresence_tests.cs` (45 cases); PartySystem 437/437; full EditMode `-testCategory "!Database"` 8531/8531 (2026-11-19)
**Code Review**: Complete (`/code-review` 2026-11-19: R1–R4 + tests applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — Lean mode
**Story 011 hand-off**: compose `PartyPlayerEntryListener`, `PartySlotReleaseListener` (Combat stays last), `PartyZonePresence` (wiring + skill cast source), `PartyMemberStatusSink`, `SessionMachinePartySessionStateSource` + `PartyPresenceStep`; order invite `Tick` → dispatch → simulation → `ApplyQueued` → `Poll` → `Flush`.
