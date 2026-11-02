# Story 008: CycleTimerBroadcast Step

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rule 9; `design/gdd/networking-core.md`; `design/gdd/networking-channel-contract.md`; `design/gdd/networking-message-criticality.md`
**Requirement**: `TR-aa-007` (placeholder)

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
- [ ] A player in `IDLE` follows the rule settled at `/story-readiness` (see Implementation Notes).
- [ ] The step is added to `ZoneOutboundSteps`, keeping outbox → R-U → positions order, and the wiring test list is updated.
- [ ] A ghost or removed client gets nothing and nothing throws.

---

## Implementation Notes

- `CycleBroadcastPacketWriter.Write(destination, sequenceNumber, tickNumber, entries, clientIdForLogging)` and `CycleTimerBroadcast` exist; the entry `IReadOnlyList` must be reused, not allocated.
- **Settle at `/story-readiness`:** `networking-channel-contract.md:48` says "one entry per zone entity other than the receiver". Then the attacker's own charge bar has no server value, though Rule 9 and `EC-20` need one. Recommendation: include the receiver's own entry and amend the contract.
- **Settle at `/story-readiness`:** whether `IDLE` players are sent, and the batch-size limit at 50 players, against the 512-byte cap.
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
- **Own entry**: per the readiness decision.
- **Scale**: 50 players → fits the cap, or follows the rule the readiness decision records.
- **Ghost / removed client**: no packet, no throw.
- **Flush order**: this step's place in `ZoneOutboundSteps` is asserted.
- **Allocation**: a flush for 50 players inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_CycleBroadcast_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006
- Unlocks: Combat UI / HUD charge bar
