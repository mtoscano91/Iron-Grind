# Story 002: Town Hub Flag and Shop Session

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — CR-SHOP-1, CR-SHOP-3, CR-SHOP-4, States and Transitions; `enhancement-system.md` CR-ENH-16, CR-ENH-17 (shared flag)
**Requirement**: `TR-shop-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: `OpenNPCInteraction` / `CloseNPCInteraction` are R-OD requests handled on the tick through the inbound dispatcher.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: New serialized private field on `ZoneDefinition` (ScriptableObject); fields only.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative; injected clock for wall-clock timers in tests
- Forbidden: `[SerializeField]` on properties

---

## Acceptance Criteria

- [ ] **AC-1 Hub flag**: `ZoneDefinition` gains `IsTownHub` (serialized private `_isTownHub`, default false); `ZoneDefinition_StartingZone.asset` sets it true (epic decision D1, MVP / dev until a real hub is authored). A production `ZoneDefinitionTownHubQuery : ITownHubQuery` answers from the zone the character is in.
- [ ] **AC-2 Open in hub**: a character in a hub zone sending `OpenNPCInteraction(shopNpcId)` → session active, `NPCInteractionOpened` sent (AC-NS-01).
- [ ] **AC-3 Open outside hub**: in a zone with `IsTownHub = false` → `RejectedNotInTownHub`, no session (AC-NS-02).
- [ ] **AC-4 Replace**: an active session for another npcId (Enhancement NPC) is replaced silently by the shop open — `NPCInteractionOpened`, active npcId is the shop's, no other message (AC-NS-03 (1) and (3); (2) "a later Buy succeeds" is checked at Story 006).
- [ ] **AC-5 Close triggers**: `CloseNPCInteraction` → inactive, no reply (AC-NS-06); `NotifyZoneTransition` → inactive (AC-NS-04); clock advanced to open + 300 s → inactive; open + 299.9 s → still active (AC-NS-05); continuous messages do not extend it.
- [ ] **AC-6 Re-open**: re-opening the same shop npc while active → `NPCInteractionOpened` and the lifetime restarts from the new open time (AC-NS-35).
- [ ] **AC-7 Open hook**: the open path exposes a "session opened" notification (event or callback) that Story 004 uses to clear the character's shop dedup entries; no-op until then.

---

## Implementation Notes

- The tracker (`NpcInteractionSessionTracker(ITownHubQuery, Func<double> clockSeconds, double lifetimeSeconds)`), `Open` / `Close` / `NotifyZoneTransition` / `NotifySessionEnded` / `IsActive` / `TryGetActiveNpcId`, and `NpcInteractionRequestHandlers` (Open / Close) already exist from Enhancement Stories 006 and 013 — reuse them, do not duplicate. Read their tests first; extend only where an AC is not already covered.
- `ITownHubQuery` production implementation reads the zone's `ZoneDefinition` (one zone per process: the zone server's own definition). Keep `npcId` unvalidated (TD-058, OQ-NS-1).
- Death never clears the session (CR-SHOP-4 rule 7): do not subscribe to death.

---

## Out of Scope

- Story 009: composing the tracker in the zone server and calling `NotifyZoneTransition` / `NotifySessionEnded` from zone exit and session end
- Story 004: the dedup clear on open (this story only provides the hook)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: load the starting zone asset → `IsTownHub` true; a new `ZoneDefinition` → false; query over a hub / non-hub definition → true / false.
- **AC-2 / AC-3**: handlers + recording outbox + fake clock; hub → `NPCInteractionOpened` (0xE112); non-hub → `RejectedNotInTownHub` (0xE113), `IsActive` false.
- **AC-4**: open enhancement npc 7, then shop npc 9 → one `Opened` per open, active npc 9, no other message.
- **AC-5**: close → inactive, outbox count unchanged; zone transition → inactive; clock 299.9 → active, 300.0 → inactive.
- **AC-6**: open at t=0, re-open at t=200 → active at t=499.9, inactive at t=500.
- **AC-7**: subscriber counts one notification per successful open, none on a rejected open.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Session_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 004, 005, 006, 009
