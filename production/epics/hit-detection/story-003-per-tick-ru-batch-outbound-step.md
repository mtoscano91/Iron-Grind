# Story 003: Per-Tick R-U Batch Outbound Step

> **Epic**: Hit Detection
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` — Path 2a (R-U batch), R-U serialization and overflow lists
**Requirement**: `TR-hd-006` (delivery part; placeholder)

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: `ZoneTickPipeline` runs a fixed step order with one outbound step; R-U is `NetworkChannel.ReliableUnordered`, which `NgoClientMessageSender` maps to `NetworkDelivery.Reliable`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: uses the send seam already verified by Movement Story 009.

**Control Manifest Rules (this layer)**:
- Required: `ZoneTickPipeline` keeps its five-step order
- Forbidden: per-tick allocation on the send path
- Guardrail: batch body at most `RUBatchWriter.MAX_MESSAGE_BODY_BYTES` (512)

---

## Acceptance Criteria

*Why this story exists: `RUBatchWriter` is implemented but has no production caller, so no R-U batch reaches a client today.*

- [x] A new `RUBatchOutboundStep : IRUBatchCollector, IZoneOutboundStep` in `src/ServerLogic/Networking/Outbound/` accepts pending R-U sub-messages during a tick through `Enqueue(clientId, category, messageTypeId, ReadOnlySpan<byte> body)`, which copies the body and never throws. A client unknown to the outbox is dropped silently (a ghost player is routine).
- [x] `Enqueue` refuses `RUBatchCategory.DamageEvent`, `RUBatchCategory.GoldSyncEvent` and a body over `RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes` (496) with one logged error, so `RUBatchWriter.Write` never throws on the flush. A full arena or entry table drops the message with one warning per tick and no disconnect.
- [x] `Flush(uint currentTick)` sends each client's pending messages once per tick through `RUBatchWriter`, on `NetworkChannel.ReliableUnordered`, with that client's counter from `outbox.TryGetSequenceCounter`.
- [x] A client with nothing pending gets no packet and its sequence counter does not move.
- [x] All pending state is empty after each flush. A client gone from the outbox at flush time is discarded without a send or a throw; a `Write` or send exception is logged per client and does not stop other clients or the clearing.
- [x] `RUBatchWriter.Write` allocates nothing on a call with no overflow; its signature and behaviour are unchanged (the existing tests still pass).
- [x] The zone composition's outbound step runs outbox, then R-U, then positions; existing outbox and position broadcast behaviour is unchanged. Teardown step 4b stays outbox-only, and pending R-U messages are discarded at teardown.

---

## Implementation Notes

- Follow `PositionBroadcastStep`: same `CreateSendSeam()` instance as the outbox, `outbox.TryGetSequenceCounter`, `IZoneOutboundStep.Flush(uint currentTick)`.
- **Shape:** one class, as `ClientMessageOutbox` is both queue and step. Story 004 injects `IRUBatchCollector`. Constructor: send seam, `ClientMessageOutbox`, max clients (`pool.MaxPlayers`).
- **Storage (decided 2026-10-30):** a pool of `maxClients` per-client states built in the constructor, each with a byte arena of `RU_ARENA_BYTES` (4096), a `List<PendingSubMessage>` reserved to `RU_MAX_PENDING_MESSAGES` (128) and a `ClientBufferSet` (`RUBatchBuffer`). A state is taken on a client's first `Enqueue` of the tick. `Payload` is a `ReadOnlyMemory<byte>` over the arena; the arena resets each flush, so there is no compaction. The pool being empty counts as full.
- **Disconnect (decided 2026-10-30):** no `IClientConnectionListener`; the transport takes one listener (the outbox) and this class holds nothing between ticks. Pending state lives one tick, so a client that left is found at `Enqueue` or at `Flush` through `TryGetSequenceCounter`.
- **`Write` rewrite:** keep the signature. Replace the arrays with `stackalloc` and the bucket lists with one pass over `otherSubMessages` per category, preserving each category's input order. At most 128 entries × 10 categories per client.
- **Generic path only:** `EntityHealthUpdate` will use `Enqueue`. `DamageEvent` and `GoldSyncEvent` are typed `Write` parameters and are passed as null here; the Auto-Attack epic adds typed intake.
- **Composition:** `OutboundThenPositions` becomes a three-step `ZoneOutboundSteps`. Add `Observe(nameof(RUBatchOutboundStep))` before the positions step and update the exact list at `tests/EditMode/Networking/ZoneServer_CompositionWiring_tests.cs:214`. Expose the step as an internal `RUBatch` property, like `PositionBroadcast`, and update the 4b comment.
- **Empty batch:** if one category overflows alone, `Write` evicts all of it and returns a 12-byte batch with no sub-messages. The sequence number is already issued, so the step sends that packet. This takes 30 `HitCheckResult`s for one client in one tick (29 × 17 + 12 = 505 fits).
- `BatchOverflow` is logged by `Write` and allocates, on the overflow path only.

---

## Out of Scope

- Story 004: producing `HitCheckResult`
- Emitting `DamageEvent`, `EntityHealthUpdate`, `SelfPositionUpdate` or other R-U types
- Client receipt of the batch

---

## QA Test Cases

- **Single client**: one pending message → exactly one packet to that client on `ReliableUnordered`, with `TICK_BATCH_RU`, and the payload decodes back.
- **Isolation**: messages queued for client A never reach client B.
- **Empty tick**: nothing pending → no send, sequence counter unchanged.
- **Flush clears**: the second flush with no new messages sends nothing.
- **Overflow**: pending messages over 512 bytes across categories → `RUBatchWriter`'s drop order applies and the packet fits.
- **Lone category overflow**: one category over 512 bytes → one 12-byte packet with count 0, `BatchOverflow` logged.
- **Refused**: `DamageEvent`, `GoldSyncEvent`, and a 497-byte body → one error each, nothing queued, no throw; a 496-byte body is accepted.
- **Full**: the 129th message and a body past 4096 bytes → dropped with one warning for the tick; earlier messages still sent.
- **More clients than states**: `maxClients` + 1 clients → the extra client's message dropped with a warning, no throw.
- **Disconnect**: client removed from the outbox with messages pending → next flush sends nothing to it and does not throw.
- **Unknown client**: enqueue for an unknown client → dropped, no throw, no log.
- **Send throws**: seam throws for client A → one error, client B still sent, nothing pending afterwards.
- **Sequence**: two ticks with messages → consecutive sequence numbers on that client's shared counter.
- **Write order**: mixed categories enqueued out of order → canonical order, input order kept within a category.
- **Pipeline**: existing `ZoneTickPipeline` and composition tests still pass with the step added; flush order is outbox, R-U, positions.
- **No allocation**: `Enqueue` and a flush with pending messages inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/RUBatch_OutboundStep_tests.cs` — must exist and pass

**Status**: [x] Created; 33 cases, plus 2 composition tests in `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs`; all pass

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 004

---

## Completion Notes
**Completed**: 2026-10-30
**Criteria**: 7/7 passing
**Deviations**: Advisory only. `Enqueue` also refuses a category outside the enum and a call from inside the send seam (`EnqueueDuringFlush`). `ZoneOutboundSteps.Flush` has no try/catch between steps, unchanged from before this story. A flooded client logs `BatchOverflow` every tick, overflow path only. `MovementSystem_PipelineWiring_tests.cs` touched for the flush-order and teardown criteria.
**Test Evidence**: Integration: `tests/EditMode/Networking/RUBatch_OutboundStep_tests.cs` (33 cases) and 2 composition tests; EditMode 5528 / 5399 passed / 0 failed / 129 skipped
**Code Review**: Complete (CHANGES REQUIRED, all applied)
