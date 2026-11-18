# Story 005: Death and Respawn Wire Messages

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3–4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-6, CR-DR-7, CR-DR-13, Event 5; `networking-wire-protocol.md` Death & Respawn schemas; `networking-relevance-filter.md` RFR-7
**Requirement**: `TR-dr-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture; ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Messages are enqueued on the tick that produces them and flushed at the end of the tick's network step; outbound delivery goes through the zone's existing send seam / outbox.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (`BinaryPrimitives`, spans).

**Control Manifest Rules (this layer)**:
- Required: R-OD messages use MessageTypeIDs 0xE000–0xEFFF (wire protocol; memory note "message id range check")
- Required: a wire type goes in `IronGrind.Foundation` only with a named client consumer, added to the boundary test's allow-list (ADR-012); consumer: the client death overlay / VFX (presentation epic)

---

## Acceptance Criteria

- [ ] **AC-1 Structs and codecs**: `EntityDied` (0xE160, 4-byte body), `DeathStateEntered` (0xE161, 5-byte body), `EntityRespawned` (0xE162, 10-byte body, CR-NET-7.2 position encoding with the overflow guard), `RespawnConfirmed` (0xE163, 4-byte body), each a `readonly struct` with a codec matching `networking-wire-protocol.md` byte for byte; round-trip tests; IDs checked unique against the existing set. (Readiness: confirm 0xE160–0xE163 are still free.)
- [ ] **AC-2 Death messages (AC-DR-NEW-1, AC-DR-NEW-2)**: on a non-ghost death the outbox holds exactly one `DeathStateEntered` to the dying player with `respawnTimerSeconds` = configured seconds (the byte field per the schema), and exactly one `EntityDied` per relevant zone client (relevance-filtered per RFR-7), including other players, in the death tick.
- [ ] **AC-3 Respawn messages (AC-DR-NEW-3)**: at T-3, `EntityRespawned(entityId, position)` to zone clients and `RespawnConfirmed` to the respawning player only, both enqueued in the T-3 tick.
- [ ] **AC-4 Ghost (AC-DR-11)**: a ghost death enqueues no `EntityDied` and no `DeathStateEntered`.
- [ ] **AC-5 Disconnected recipient**: a player whose slot is not connected is skipped by the sends without error (the reconnect snapshot covers them, Story 007).
- [ ] **AC-6 Boundary test**: the four types are on the Foundation allow-list with their client consumer named; the boundary test passes.

---

## Implementation Notes

- Reuse how `CycleTimerBroadcastStep` / `PositionBroadcastStep` reach clients (`sendSeam`, `outbox`) and the existing relevance filter; do not build a new send path.
- `SelfPositionUpdate` suppression while Dead (wire protocol, AC-NC-39) does not apply yet: nothing in production emits `SelfPositionUpdate` (client-side prediction is not on the roadmap). Record this in the Completion Notes; no code.

---

## Out of Scope

- Client decoding and the death overlay (presentation epics)
- Story 007: reconnect snapshot fields

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1**: Each codec: encode → decode equals input; body length as specified; position overflow clamps and logs `...EncodeOverflow`.
- **AC-2**: Zone with players P (dies), Q and R in range, S out of relevance → `DeathStateEntered` only to P; `EntityDied` to P, Q, R per RFR-7, not S (adjust to RFR-7's exact rule at readiness).
- **AC-3**: At T-3, `EntityRespawned` to all relevant, `RespawnConfirmed` count 1 addressed to P.
- **AC-4**: Ghost death → zero death messages in the outbox.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_WireMessages_tests.cs` (+ codec tests beside the existing wire protocol tests) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 004
- Unlocks: Story 008
