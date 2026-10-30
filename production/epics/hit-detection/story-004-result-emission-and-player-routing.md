# Story 004: Result Emission and Player Routing

> **Epic**: Hit Detection
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/hit-detection.md` — CR-HD-10, CR-HD-11, AC-HD-11a, AC-HD-11b
**Requirement**: `TR-hd-006` (placeholder)

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture; ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: callers use direct calls on injected interfaces; the authoritative tick comes from the tick source.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: dependency injection over singletons
- Forbidden: allocation per emitted result
- Guardrail: `CheckHit` stays pure; emission is a separate call

---

## Acceptance Criteria

*From GDD `design/gdd/hit-detection.md`, scoped to this story:*

- [ ] A new seam (working name `IHitResultEmitter`) lets a caller report one `CheckHit` result with attacker, target and `HitResult`.
- [ ] Player attacker: one `HitCheckResult` is queued in the R-U batch for the attacking player's client, for every result value; `Result`, both entity ids and `ServerTick` match the inputs and the current tick (AC-HD-11a).
- [ ] Monster attacker: one `HitCheckResult` is queued for the defending player's client, never for the monster (AC-HD-11b).
- [ ] No player is involved or the player has no client: nothing is queued, nothing throws.
- [ ] The emitter is built in the zone composition, ready for the Auto-Attack and Enemy AI epics to inject.

---

## Implementation Notes

- `CheckHit` does not emit (it stays pure, AC-HD-17). Callers call `CheckHit`, then the emitter. Auto-Attack and Enemy AI own making that call on every attack.
- Player versus mob: slots are by entity id (mob ids OWL ≥ 50, as in Movement Story 008); confirm the check at `/story-readiness`.
- Client lookup: `ZoneConnectionCharacterDirectory.TryGetClientId` takes a `CharacterID`; confirm how an `EntityID` maps to it.
- Player attacking a player does not exist at MVP; state the attacker rule so it stays defined.

---

## Out of Scope

- Auto-Attack Combat and Enemy AI calling the emitter (AC-HD-12, AC-HD-13, AC-HD-15)
- `AutoFaceEvent` and `FacingAngle` updates (AC-HD-18, AC-HD-19)
- Client miss and hit feedback

---

## QA Test Cases

- **AC-HD-11a**: player attacker, for each of `Hit`, `MissOutOfRange`, `MissFacing` → one message to that client, all fields match, `ServerTick` equals the tick source.
- **AC-HD-11b**: monster attacker → one message to the defending player's client, none to any other client.
- **Other clients**: a third player in the zone receives nothing.
- **Disconnected player**: the recipient has no client → nothing queued, no throw.
- **Two attackers, one tick**: two players hit one monster → each gets their own message (EC-HD-6).
- **End to end**: emit, flush the Story 003 step, decode the packet → `HitCheckResult` fields match.
- **No allocation**: emit inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/HitDetection/HitDetection_ResultEmission_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003
- Unlocks: Auto-Attack Combat and Enemy AI epics (not yet created)
