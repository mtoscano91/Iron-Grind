# Story 003: Per-Tick R-U Batch Outbound Step

> **Epic**: Hit Detection
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` — Path 2a (R-U batch), R-U serialization and overflow lists
**Requirement**: `TR-hd-006` (delivery part; placeholder)

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: `ZoneTickPipeline` runs a fixed step order with one outbound step; R-U maps to the `Reliable` channel.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: uses the send seam already verified by Movement Story 009.

**Control Manifest Rules (this layer)**:
- Required: `ZoneTickPipeline` keeps its five-step order
- Forbidden: per-tick allocation on the send path
- Guardrail: batch body at most `RUBatchWriter.MAX_MESSAGE_BODY_BYTES` (512)

---

## Acceptance Criteria

*Why this story exists: `RUBatchWriter` is implemented but has no production caller, so no R-U batch reaches a client today.*

- [ ] A new per-client collector accepts pending R-U sub-messages during a tick; messages for an unknown client are dropped without throwing.
- [ ] A new `IZoneOutboundStep` flushes each client's pending messages once per tick through `RUBatchWriter`, on `NetworkChannel.Reliable`, with that client's sequence counter.
- [ ] A client with nothing pending gets no packet.
- [ ] The collector is empty after each flush, and a client's state is cleared on disconnect.
- [ ] The step joins the composite outbound step in the zone composition, and existing outbox and position broadcast behaviour is unchanged.

---

## Implementation Notes

- Follow `PositionBroadcastStep`: same `CreateSendSeam()` instance as the outbox, `outbox.TryGetSequenceCounter`, one pre-allocated buffer set per player slot, `IZoneOutboundStep.Flush(uint currentTick)`.
- Check at `/story-readiness` whether the existing teardown (4b = outbox only) needs this step.
- Keep the collector generic over `PendingSubMessage`, since `DamageEvent` and `EntityHealthUpdate` will use it for Auto-Attack. Do not add those messages here.
- `PendingSubMessage` payload storage must not allocate per message; pick a per-client arena at `/story-readiness`.
- Check `RUBatchWriter.Write`'s allocation behaviour; it has not run on a per-tick path before.

---

## Out of Scope

- Story 004: producing `HitCheckResult`
- Emitting `DamageEvent`, `EntityHealthUpdate`, `SelfPositionUpdate` or other R-U types
- Client receipt of the batch

---

## QA Test Cases

- **Single client**: one pending message → exactly one packet to that client on `Reliable`, with `TICK_BATCH_RU`, and the payload decodes back.
- **Isolation**: messages queued for client A never reach client B.
- **Empty tick**: nothing pending → no send, sequence counter unchanged.
- **Flush clears**: the second flush with no new messages sends nothing.
- **Overflow**: pending messages over 512 bytes → `RUBatchWriter`'s drop order applies and the packet fits.
- **Disconnect**: client removed with messages pending → next flush sends nothing to it and does not throw.
- **Unknown client**: enqueue for an unknown client → dropped, no throw.
- **Sequence**: two ticks with messages → consecutive sequence numbers on that client's counter.
- **Pipeline**: existing `ZoneTickPipeline` and composition tests still pass with the step added.
- **No allocation**: a flush with pending messages inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/RUBatch_OutboundStep_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 004
