# Story 006: SetAutoAttack, Input and Composition

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rule 1, EC-8, AC-16; `design/gdd/networking-relevance-filter.md` — RFR-3a
**Requirement**: `TR-aa-006`

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: every client request goes through the dispatcher. The story that adds a client→server type adds its `MessageRoutingRegistry` row, its row in the table of ADR-014 Decision 1 and its registration together.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: consumer of the project envelope only; no new NGO API.

**Control Manifest Rules (this layer)**:
- Required: a new client→server type adds its routing row, ADR-014 table row and registration together
- Required: `SetAutoAttack` and `SetTarget` apply in step 2, before the simulation step
- Forbidden: `ServerTickLoop.RegisterTickDriven` for game logic

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md`, scoped to this story:*

- [ ] `SetAutoAttack { bool enabled; }` has a `MessageTypeId` in the `0xE0xx` R-OD range, a 1-byte body, a codec, a routing row (C→S, R-OD), an `RpcTypeTag` with a gap, and a dispatcher registration (not held, no rate limit).
- [ ] A malformed body is dropped with a log and changes no state.
- [ ] An accepted `SetTarget` reaches Combat as select or deselect; a rejected one does not (self-target, entity not in the zone).
- [ ] Combat's step is listed in `ZoneServerComposition`'s simulation steps after movement, and the wiring test list is updated.
- [ ] Through the running pipeline, a `SetAutoAttack` and a `SetTarget` dispatched on tick N apply before tick N's fire check (EC-8).
- [ ] The server applies at most one auto damage per player per tick (AC-16).
- [ ] `networking-wire-protocol.md`, `networking-channel-contract.md`, `networking-message-criticality.md` and ADR-014's Decision 1 table carry `SetAutoAttack`.
- [ ] `networking-wire-protocol.md:406` states the one-per-miss-streak exception of CR-HD-10.

---

## Implementation Notes

- Next free id after `SetTarget` (`0xE040`): check `MessageRoutingRegistry` for the first free slot. `SetAutoAttack` wants a name in the same family as `SetTarget.cs` and `SetTargetCodec.cs`.
- Add a new handler mirroring `SetTargetRequestHandler`. The body byte is strict: 0 or 1, anything else is malformed.
- `SetTargetRequestHandler` calls `TargetSlotTracker.ProcessSetTarget`, which returns `SetTargetOutcome`. Forward to Combat only on `Accepted`. `TargetSlotTracker` stays the owner of relevance; Combat adds its own validity check.
- Composition: follow `BuildStatusEffects` and `BuildMovement`, with an `Observe(nameof(...))` call and `_owner._simulationSteps.Add(...)`. Movement first, so the fire check sees this tick's positions. Inject `HitResultEmitter`, and fakes-by-default stand-ins for the mob seams in `ZoneServerStandIns.cs`.
- `SetAutoAttack` is rejected for dead players by state, not by the wire: `SetCombatProhibited` already blocks the fire (`death-and-respawn.md` CR-DR-16).
- A player who leaves the zone must release combat state; use the existing slot-release listener pattern (`CompositeZoneSlotReleaseListener`). `ReleasePlayer` must be reached from `BeforeRelease`, while the player still holds the slot; `Run` does not check occupancy, so a missed hook leaves a released slot active (Story 001 code review).
- Wire `CharacterStatsAttackSpeedSource` as the attack speed seam. A player's stats must be registered before combat can activate: an entity with no stats reads 0 and gets the slowest cycle, 40 ticks (Story 002 code review).

---

## Out of Scope

- Client-side toggle button: Combat UI / HUD
- Real mobs: Enemy AI
- Stories 007–009: outbound messages

---

## QA Test Cases

- **Codec**: round trip for true and false; values 2 and 255 are rejected; wrong length is rejected.
- **Registry**: the routing row validates as C→S R-OD; every `RpcTypeTag` member still returns a gap.
- **Dispatch**: a message through `IInboundMessageIntake.TryAccept` reaches Combat on the next `DispatchTick`, using the fake connection rig of the existing dispatcher tests.
- **Guards**: a message from a non-owner or a client that is not session-ready does not reach Combat.
- **`SetTarget` outcomes**: accepted select, accepted deselect, self-target, unknown entity.
- **EC-8 through the pipeline**: `SetAutoAttack(true)` plus `SetTarget` on tick N → `COMBAT_ACTIVE` on tick N.
- **AC-16**: with one player past ready, drive several `ZoneTickPipeline.Tick` calls → the damage recorder shows at most one entry per tick.
- **Composition**: step order in `ZoneServer_CompositionWiring_tests.cs` lists Combat after movement.
- **Zone exit**: an active player released through `ZoneSlotPool.ReleasePlayer` (the real listener chain, no direct `ReleasePlayer` call) → the slot's new occupant starts `IDLE`, timer 0, toggle OFF, and `Run` does not advance it.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_InputAndComposition_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Stories 007, 008, 009
