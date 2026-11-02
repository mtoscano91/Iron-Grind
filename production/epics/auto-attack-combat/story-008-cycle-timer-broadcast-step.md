# Story 008: CycleTimerBroadcast Step

> **Epic**: Auto-Attack Combat
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rule 9; `design/gdd/networking-core.md`; `design/gdd/networking-channel-contract.md`; `design/gdd/networking-message-criticality.md`
**Requirement**: `TR-aa-007`

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate; ADR-014 Decision 6
**ADR Decision Summary**: `CycleTimerBroadcast` is U-U in its own CycleBroadcast packet, never merged with position data, flushed in the outbound step.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: consumer only.

**Control Manifest Rules (this layer)**:
- Required: own CycleBroadcast packet (`TICK_BATCH_UU_CYCLE = 0x0103`)
- Forbidden: allocation per tick or per client
- Guardrail: no entries for mobs (`networking-core.md:322`)

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md` and the networking GDDs, scoped to this story:*

- [ ] A new `IZoneOutboundStep` sends each connected client one CycleBroadcast packet per tick through `CycleBroadcastPacketWriter`.
- [ ] Each entry carries `_actionTimer / CycleTicks × 10,000`, clamped to 10,000 when the timer is past `CycleTicks`.
- [ ] Mobs never appear in the packet.
- [ ] Each client's packet includes its own entry.
- [ ] A player in `IDLE` is sent with its frozen `_actionTimer` value.
- [ ] The step is added to `ZoneOutboundSteps`, keeping outbox → R-U → positions order, and the wiring test list is updated.
- [ ] A ghost or removed client gets nothing and nothing throws.

---

## Implementation Notes

- `CycleBroadcastPacketWriter.Write(destination, sequenceNumber, tickNumber, entries, clientIdForLogging)` and `CycleTimerBroadcast` exist; the entry `IReadOnlyList` must be reused, not allocated.
- **Decided at `/story-readiness` 2026-11-02:** the receiver's own entry is included, so the attacker's own charge bar has a server value (Rule 9, `EC-20`). `networking-channel-contract.md:48` amended to match.
- **Decided at `/story-readiness` 2026-11-02:** `IDLE` players are sent with their frozen `_actionTimer`; every player in the zone appears every tick. No batch-size limit: 50 players = 12 + 50 × 10 = 512 bytes, and the writer throws only above 512. Update the stale `CycleTimerBroadcast.cs` ("other than the receiving client") and `CycleBroadcastPacketWriter.cs` ("49 × … = 502") doc comments.
- `ZoneOutboundSteps` takes three fixed constructor arguments; a fourth means changing it. The composition test in `MovementSystem_PipelineWiring_tests.cs` has the rig for flush order.
- `CycleTimerInterpolator` is the client side and is not touched here.
- Read `ClientBufferSet` before sizing buffers.

---

## Out of Scope

- Charge bar UI, smoothing and snap: Combat UI / HUD
- AC-MCR-05, AC-MCR-08: client interpolation under loss

---

## QA Test Cases

- **Normalized value**: `[TestCase]` timer 0 → 0; 5 of 20 → 2500; 20 of 20 → 10000; 60 of 20 → 10000.
- **Per tick**: three ticks → three packets for a client, with ticks matching.
- **Packet type**: decoded header is `0x0103`; no position data in it.
- **Mobs**: a zone with mobs → no mob ids.
- **Own entry**: the receiver's own id is present in its packet.
- **IDLE**: an `IDLE` player with a frozen timer is sent with that value.
- **Scale**: 50 players → 512 bytes, accepted without an exception.
- **Ghost / removed client**: no packet, no throw.
- **Flush order**: this step's place in `ZoneOutboundSteps` is asserted.
- **Allocation**: a flush for 50 players inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_CycleBroadcast_tests.cs` — must exist and pass

**Status**: [x] Created and passing (17 [Test] + 7 [TestCase])

---

## Dependencies

- Depends on: Story 006
- Unlocks: Combat UI / HUD charge bar

---

## Completion Notes
**Completed**: 2026-11-02
**Criteria**: 7/7 passing (all covered by automated tests; none deferred)
**Deviations**:
- Advisory: readiness decisions (user, 2026-11-02) amended `networking-channel-contract.md:48` and `networking-wire-protocol.md` (receiver's own entry, IDLE sent, 50 players = 512 bytes): `MAX_MESSAGE_BODY_BYTES` has zero headroom at the default zone size.
- Advisory: entries are in slot order, not sorted by EntityID (no GDD rule requires it).
- Advisory: an over-cap packet (zone-size misconfiguration) logs the writer's CRITICAL error and the step's `SendThrew` per client; the sequence number is consumed. Documented in the class.
- Advisory: zone players still loading (pre-`SessionReady`) receive the packet, like positions and `DamageEvent` (TD-089 extended).
- `CycleTimerBroadcast.MessageTypeId` `0x0302` remains provisional (pre-existing).
**Test Evidence**: Integration: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_CycleBroadcast_tests.cs` (17 [Test] + 7 [TestCase]) + `test_composition_tick_flushes_outbox_then_ru_batch_then_cycle_broadcast_then_positions` in `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs` (order and consecutive sequence numbers). EditMode 5820 / 5691 / 0 failed / 129 skipped.
**Code Review**: Complete: `/code-review` APPROVED WITH SUGGESTIONS, "fix all", items 1-8 and 10 applied (item 9 not needed: `CharacterID` lives in `IronGrind.Currency`). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
