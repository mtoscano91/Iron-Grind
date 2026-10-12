# Story 039: Client Message Outbox — the Production Send Path for Priority Messages

> **Epic**: Networking Core
> **Status**: Complete — 2026-10-12 (`/dev-story`, `/code-review` with its fixes applied, `/story-done`; see Completion Notes). Was Ready: written 2026-10-09; `/story-readiness` 2026-10-12 (NEEDS WORK, closed the same day by the user decisions below). Story 038 is Complete.
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 12 hours

*Added 2026-10-09 (user decision: the adapter work is two stories). `IClientMessageOutbox` was defined by Enhancement Story 010 as the server-to-client send seam and has only a recording test double. Four callers enqueue on it today — the Enhancement request handlers, the NPC interaction handlers, `OwnerInventorySyncSender`, and Enhancement Story 015 when it is written — and nothing sends. This story writes the production implementation: per-client queueing under the priority-path cap, the server envelope, and the send through NGO.*

> **User decisions 2026-10-09.** Messages are NGO unnamed custom messages (ADR-004 Amendment 1). The NGO-facing code lives inside `IronGrind.ServerLogic` (ADR-012 Decision 1).

> **User decisions 2026-10-12 (`/story-readiness`).**
> 1. **Exemption displaces.** With the cap full, an exempt message is sent first and the oldest non-exempt message moves to the next tick: the tick sends 8, not 9 (AC-NC-35, and `PriorityPathQueue` as built by Story 006). The "cap + 1" sentence of CR-NET-7.7 and F-CCR-1 say otherwise; that wording is not fixed here.
> 2. **`PriorityPathQueue` gets a non-allocating flush overload** in this story. Its existing `Flush` and the Story 006 tests stay unedited.
> 3. **Queue bound**: 128 messages and 16 KB of bodies per client; on overflow the message is not queued, one error is logged and the client is disconnected.
> 4. **A type with no routing row** is not sent and logs one error, in development and release builds alike. `Enqueue` never throws.
> 5. **Story 038's adapter core** tells the outbox of added and removed clients.
> 6. **VS-5 is the Editor run only**; the release-build half is out of scope.
> 7. **Estimate 12 hours.**
>
> **Taken by the main session inside the approved fixes:** the outbox is the priority path only; the envelope's sequence number comes from one `ConnectionSequenceCounter` per client (CCR-1, AC-NC-36); the run extends Story 038's PlayMode harness; VS-4 is read by a `GC.Alloc` recorder; the evidence rule is Story 038's.

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` — CR-NET-7.1 (server → client envelope, 10 bytes), CR-NET-7.6 (512-byte cap), CR-NET-7.7 (two-path delivery: the priority path, its cap of 8 messages per client per tick, the enhancement-path exemption and its timing), AC-NC-35 and AC-NC-47 (cap and exemption, asserted end to end by Enhancement Story 015). `design/gdd/networking-channel-contract.md` — CCR-3 (direction and channel of each message). No rule or value of a GDD changes.
**Requirement**: TR-net-005 (channel routing per criticality classification) for the delivery mapping; otherwise none in the epic's TR table
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**:
- **ADR-004: Networking Library (NGO)** — Decision 2 (channel → `NetworkDelivery`), Decision 4 (project envelope as the body), Amendment 1 (unnamed messages; `Reliable` is ordered on `UnityTransport`; a reliable send queue that overflows disconnects the client).
- **ADR-014** — Decision 6 (the Outbound step is the last step of the tick); Risks ("the adapter story states that a send to a removed connection is dropped").
- **ADR-012** — Decision 1 and Decision 4.
- **ADR-010** — a direct call on an injected interface.

**ADR Decision Summary**: the server owns what each client sees; a system enqueues a message for one client on the tick, and the outbound step of that tick hands the client's priority messages to the transport, at most eight per client per tick plus the exempt ones.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH — Netcode for GameObjects 2.13 is past the LLM training cutoff.
**Engine Notes**: read `docs/engine-reference/unity/modules/networking.md`, section "NGO 2.13 — Custom Messaging, Transport and Frame Timing". Researched 2026-10-09, **not run**. Facts that shape this story: `SendUnnamedMessage(ulong clientId, FastBufferWriter messageBuffer, NetworkDelivery networkDelivery)` only queues, and the bytes go on the wire in `PostLateUpdate`; `new FastBufferWriter(size, Allocator.Temp)` is a native allocation, disposed with `using`; every delivery except `ReliableFragmentedSequenced` is capped at about 1272 bytes of payload; `UnityTransport` has no reliable-unordered delivery; a reliable send queue that overflows disconnects the client; NGO 2.x throws `OverflowException` from the send functions for an oversize message in DEBUG builds only.

**Control Manifest Rules**:
- Required: four tick steps in this order — Completion, Requests, Simulation, Outbound; the per-connection writers flush in the Outbound step — ADR-014
- Required: cross-system communication is a direct call on an injected interface — ADR-010
- Required: wire message schemas and the code that reads and writes them are `Foundation`; the sender is server-only — ADR-012
- Forbidden: a `MonoBehaviour` of `IronGrind.ServerLogic` in a scene, prefab or asset a client build includes — ADR-012

---

## Acceptance Criteria

*"The outbox" is the plain C# implementation of `IClientMessageOutbox`; "the send seam" is the small interface it sends through; "the NGO sender" implements the send seam. "Sent" means one call on the send seam.*

**Outbox (EditMode, recording send seam)**

- [ ] **Enqueue copies**: after `Enqueue` returns, overwriting the caller's span does not change what is later sent.
- [ ] **Flush order**: messages enqueued for one client in one tick are sent in the order enqueued, at the Outbound step of that tick, each as one message: the 10-byte server envelope followed by the body.
- [ ] **Envelope**: each sent message starts with a `ServerMessageEnvelope` carrying the enqueued `messageTypeId`, the tick number of the flush, and a sequence number issued at the flush, in send order, by that client's own `ConnectionSequenceCounter` (CCR-1: one counter per connection, shared by every type the server sends to it). A client's first message carries 1; two clients' numbers are independent; a client removed and added again starts at 1.
- [ ] **Cap (CR-NET-7.7)**: with 11 non-exempt messages enqueued for one client in one tick, 8 are sent at that tick's flush and 3 at the next, in emission order; none is dropped. The cap is per client: a second client's 8 are all sent the same tick.
- [ ] **Exemption (AC-NC-35)**: with 8 non-exempt messages queued, an exempt message enqueued before the flush is sent first that tick and the tick sends 8: the exempt message, then the second to the eighth non-exempt message. The first non-exempt message is sent first at the next flush.
- [ ] **Exemption after the flush**: an exempt message enqueued after the tick's flush is first in the next tick's flush.
- [ ] **Delivery**: each message is sent with the channel of its `MessageRoutingRegistry` row. A `messageTypeId` with no row, or outside the priority range `0xE000–0xEFFF`, is not sent and one server error is logged, in development and release builds alike. *(AC-MCR-03's startup throw belongs to handler registration, `MessageRoutingRegistry.ValidateAndRoute`; its release fallback, "routed to R-U", has no path in this outbox. This is a stated deviation for the send path.)*
- [ ] **Queue full**: a client's queue holds at most `OUTBOX_MAX_QUEUED_MESSAGES` (128) messages and `OUTBOX_ARENA_BYTES` (16,384) bytes of bodies. With 128 queued, the 129th `Enqueue` queues nothing, logs one server error and requests the disconnect of that client once, through the disconnect seam; the same holds for a body that does not fit the remaining bytes. The 128 already queued are discarded when the client is removed. Another client's queue is not affected. Space is free again once a message is sent: 49 messages enqueued in one tick are all sent over 7 ticks, and 128 more can then be queued (EC-CCR-1).
- [ ] **Client list**: `InboundTransportAdapter` calls the outbox's `AddClient` after `AddConnection` returns true and `RemoveClient` beside each `RemoveConnection` (the disconnect event and `Disconnect`), through one listener seam; a refused connect adds no client; an exception from the listener is caught and logged once, and the adapter's own behaviour is unchanged. The existing tests of `InboundTransportAdapter_tests.cs` pass with no assertion changed.
- [ ] **Size**: a body above `MAX_MESSAGE_BODY_BYTES` (512, CR-NET-7.6 — the cap is on the body, the envelope is not counted) is not sent and one server error is logged; a body of exactly 512 bytes is sent.
- [ ] **Unknown or removed client**: `Enqueue` for a client that was never added or has been removed sends nothing, throws nothing, and logs at most one warning per client per tick; messages still queued for a client are discarded when it is removed.
- [ ] **Never throws**: `Enqueue` and the flush do not throw for any input above, and a throwing send seam does not stop the flush of the other clients; the failure is logged once per client per tick. `IClientMessageOutbox`'s doc comment states that `Enqueue` does not throw.
- [ ] **No allocation**: `Enqueue` and a flush allocate nothing per message once a client's storage exists (asserted over 1,000 messages with `Is.Not.AllocatingGCMemory()`, as Story 038 does). The flush uses a new overload of `PriorityPathQueue<T>` that fills a list the caller owns and reuses the queue's own lists; the existing `Flush(uint)` keeps its signature and behaviour, and `PriorityPathQueue`'s existing tests pass unedited. New tests show the overload returns the same messages in the same order as `Flush(uint)` for the cap, deferral and displacement cases.
- [ ] **Callers unchanged**: the existing tests of the Enhancement request handlers, the NPC interaction handlers and `OwnerInventorySyncSender` pass unedited; `OwnerInventorySyncSender`'s remark that `Enqueue` is assumed not to throw is updated to cite this story.

**NGO sender (compiles in `IronGrind.ServerLogic`)**

- [ ] **Mapping**: `ReliableOrdered` → `NetworkDelivery.ReliableSequenced`, `ReliableUnordered` → `NetworkDelivery.Reliable`, `Unreliable` → `NetworkDelivery.Unreliable` (ADR-004 Decision 2 and Amendment 1); the mapping is one table, tested in EditMode without a session.
- [ ] **Send**: one `SendUnnamedMessage(clientId, writer, delivery)` per message, the writer created with `Allocator.Temp` and disposed in the same call; a source search finds `SendUnnamedMessage` and `FastBufferWriter` in the sender's file only, in all of `src/`.

**Verification in a real run (evidence document)**

*Recorded in `production/qa/evidence/networking-core-039-ngo-send-verification.md`, PASS / FAIL / NOT RUN with the observation. The run is a PlayMode test on loopback in Story 038's harness (a server and one client `NetworkManager` in one process, the test-only tick driver), with a client-side receive of unnamed messages added. A FAIL is recorded and escalated and does not by itself block `/story-done`; an item NOT RUN blocks it.*

- [ ] **VS-1 Round trip**: an `InventoryFullSync` (211 bytes with its envelope) and an `InventorySlotUpdate` enqueued on the server arrive at a client byte for byte, in order.
- [ ] **VS-2 Cap on the wire**: 11 messages enqueued in one tick arrive as 8 and then 3, one tick apart, by the envelope's tick number.
- [ ] **VS-3 Removed client**: enqueueing for a client after the adapter's `Disconnect` puts nothing on the wire and logs no NGO error.
- [ ] **VS-4 Allocation**: a `GC.Alloc` profiler recorder scoped to the outbox's flush reads 0 samples over 500 messages, with a control that shows the recorder counts an allocation (the method of Story 038's VR-2; no one reads a Profiler window). NGO's own allocations are recorded as observed, not asserted.
- [ ] **VS-5 Oversize**: what NGO does with a 1,300-byte `ReliableSequenced` message in the Editor run is recorded, through the NGO sender directly (the outbox never sends one; this pins the reference). A release build is out of scope.

---

## Implementation Notes

- **Three parts.** The outbox (plain C#): per-client storage, `Enqueue`, `Flush(tick)`. The send seam: one method taking the client id, the message bytes and the `NetworkChannel`. The NGO sender: the seam's implementation over `CustomMessagingManager`. All behaviour is tested on the outbox with a recording seam.
- **Reuse `PriorityPathQueue<T>`** (`src/ServerLogic/Networking/WireProtocol/`, Story 006): it already implements the cap of 8, the exempt front slot with displacement, and `Flush(tickNumber)`. **Checked 2026-10-12: `Flush` allocates three `List`s per call** (`combinedNonExempt`, `result`, `nextDeferred`). This story adds an overload that takes the destination list and keeps the working lists as fields; the ordering rule stays in that one class. Do not change `Flush(uint)`.
- **Storage.** `Enqueue` must copy the body (the interface says so) into storage that exists before the message arrives: a per-client byte arena of `OUTBOX_ARENA_BYTES` and an entry table of `OUTBOX_MAX_QUEUED_MESSAGES`, both created at `AddClient`, in the manner of the inbox arena of Story 036. Deferred messages outlive a flush, so the arena cannot simply be reset each tick: it must give back the space of sent messages (a ring, or compaction after the flush). The two constants are structural, like `PRIORITY_PATH_CAP`. 128 messages covers EC-CCR-1's 49 queued `PlayerJoinedZone`; **the byte size of `PlayerJoinedZone` was not checked** — 16 KB holds 128 messages of 128 bytes, or 32 of the 512-byte maximum.
- **Overflow disconnect.** The outbox requests the disconnect through a small injected seam that the composition root binds to `InboundTransportAdapter.Disconnect`. That call comes back into `RemoveClient` while `Enqueue` is still on the stack; the outbox must tolerate it. Request it once per client, not once per refused message.
- **The flush is the Outbound step** of `ZoneTickPipeline` (the `outboundStep` delegate of its constructor). The composition root wires it; this story provides the method and tests it by calling it.
- **Envelope.** `ServerMessageEnvelope` and `MessageEnvelopeCodec` exist in `Foundation`. The outbox writes the envelope at flush time, so the tick number is the flush tick, and the sequence number is issued then too: a deferred message gets its number when it is sent, so numbers on the wire rise in send order.
- **Sequence counter.** `ConnectionSequenceCounter` (`Foundation`) starts at 1 and skips 0 on wrap; one instance per client, created at `AddClient`. CCR-1 makes it the one counter for everything the server sends on that connection: the batch-path story must draw from the same instance, so keep it reachable (a per-client accessor, or inject a counter source) and do not bury it. Whether a client discards on it is client-side and not this story.
- **Clients.** The outbox learns of a client through explicit `AddClient(uint)` / `RemoveClient(uint)` calls. `InboundTransportAdapter` makes them through a new listener interface taken as an optional constructor argument (null = no listener, so the existing tests construct it as they do now): after `AddConnection` returns true, and beside the `RemoveConnection` of `OnClientDisconnected` and of `Disconnect`. Where the adapter skips `RemoveConnection` (the pending-disconnect entry consumed by the event that follows a `Disconnect`), it skips `RemoveClient` too.
- **Reliable queue overflow.** NGO disconnects a client whose reliable send queue overflows. The outbox cannot prevent it; the disconnect arrives through Story 038's adapter like any other. State this in the class remarks.
- **The broadcast.** `ServerBroadcast_Enhancement9` goes to every connected client of every zone. The outbox is per client; the caller (Enhancement Story 015) loops, or a later story adds a broadcast call. Not decided here.

**The six open decisions of 2026-10-09 are closed** by the decisions block at the top of this file (2026-10-12): priority path only; the per-client `ConnectionSequenceCounter`; 128 messages / 16 KB with a disconnect on overflow; no routing row → not sent, one error; the adapter core adds and removes clients; the run extends Story 038's harness.

**Exempt messages and the cap.** `PriorityPathQueue` never defers an exempt message, so more than 8 exempt messages in one tick are all sent that tick. Today two types are exempt (`EnhancementRequestReceived`, `EnhancementAttemptResult`); the outbox does not police the flag — the caller passes it.

**Performance**: per message, one copy at `Enqueue`, one envelope write and one copy into the writer at flush; no allocation in project code. Worst case per tick 50 clients × (8 + exempt) messages. NGO's framing and queues are outside this story.

---

## Out of Scope

- Story 038: receiving, the connection events, `Disconnect`
- The batch path (Path 2): `RUBatchWriter`, relevance filtering, the 512-byte per-client batch
- Bulk messages (`0xF000–0xFFFF`, `ReliableFragmentedSequenced`), including `ZoneStateSnapshot`
- A broadcast call on the outbox
- Enhancement Story 015 (AC-NC-35 and AC-NC-47 end to end) and the story that sends `SessionReady`
- The client side: decoding, the client composition root
- The server composition root, the production tick driver, the headless-build engine-risk gate
- Any change to `IClientMessageOutbox`'s signature
- Any change to `PriorityPathQueue.Flush(uint)` or to the Story 006 tests
- The GDD wording that disagrees with AC-NC-35: the "`PRIORITY_PATH_CAP + 1` or more" sentence of CR-NET-7.7 and formula F-CCR-1 (a design fix, not made here)
- What NGO does with an oversize message in a release build (with the headless-build engine-risk gate)
- AC-NC-36 (the sequence wrap through `ITransportFaultInjector.SetSequenceNumber`)

---

## QA Test Cases

**Files** — to be created: `src/ServerLogic/Networking/Outbound/` (the outbox, the send seam, the NGO sender, the client listener interface, the disconnect seam); `tests/EditMode/Networking/ClientMessageOutbox_tests.cs`; `tests/PlayMode/Networking/ClientMessageOutbox_ngo_run_tests.cs`; `production/qa/evidence/networking-core-039-ngo-send-verification.md`. To be edited: `src/ServerLogic/Networking/WireProtocol/PriorityPathQueue.cs` (the overload) and its test file (new tests only); `src/ServerLogic/Networking/Transport/InboundTransportAdapter.cs` (the listener) and `tests/EditMode/Networking/InboundTransportAdapter_tests.cs` (new tests only); the doc comments of `IClientMessageOutbox` and `OwnerInventorySyncSender`.

- **Copy** — enqueue, overwrite the source span, flush → the original bytes are sent.
- **Order and envelope** — three messages, one client → three sends in order; each starts with the type id and the flush tick; sequence numbers 1, 2, 3.
- **Sequence** — two clients each start at 1; a message deferred to the next tick is numbered after the ones sent before it; a client removed and added again starts at 1.
- **Cap** — 11 non-exempt → 8 then 3; two clients with 8 each → 16 the same tick.
- **Exempt before the flush** — 8 queued, one exempt → 8 sent, the exempt first, then non-exempt 2 to 8; non-exempt 1 is first at the next flush.
- **Exempt after the flush** — sent first at the next flush.
- **Channel** — each send carries its row's channel; the three-channel mapping is covered by the mapping table test.
- **No routing row / outside the priority range** — not sent, one error, no exception, with the development-build flag on and off.
- **Queue full** — 128 queued, the 129th → not queued, one error, one disconnect request; a second refused message → no second request; a body larger than the remaining bytes → the same; another client unaffected; 49 enqueued in one tick drain over 7 ticks and 128 more are then accepted.
- **Disconnect re-entry** — a disconnect seam that calls `RemoveClient` from inside the overflowing `Enqueue` → no exception, the queue is discarded.
- **Client list (adapter)** — connect → `AddClient` once; refused connect → none; disconnect event → `RemoveClient` once; `Disconnect` then its event → `RemoveClient` once; a throwing listener → one error, `AddConnection` / `RemoveConnection` still called.
- **Queue overload** — the same messages in the same order as `Flush(uint)` for 12 non-exempt, for 8 plus one exempt, and across three ticks; no allocation over 1,000 flushes.
- **Size** — body 512 bytes sent; body 513 bytes not sent, one error.
- **Unknown client** — nothing sent, no exception; **removed client** — queued messages discarded.
- **Throwing seam** — client A's send throws → client B's messages are still sent that tick; one error.
- **No allocation** — 1,000 messages, zero bytes allocated in `Enqueue` and flush.
- **Mapping table** — the three channels map to the three `NetworkDelivery` members named above.
- **Run** — the five VS items, each with its observation.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/ClientMessageOutbox_tests.cs` — must exist and pass, with the full EditMode suite; `production/qa/evidence/networking-core-039-ngo-send-verification.md` — the run, every VS item PASS or FAIL with its observation, **none NOT RUN**.

**Status**: [x] `tests/EditMode/Networking/ClientMessageOutbox_tests.cs` (47 tests), EditMode 2487 / 2487 (2026-10-12 18:56:54Z); `production/qa/evidence/networking-core-039-ngo-send-verification.md`, PlayMode 20 / 20 (19:01:04Z), VS-1 to VS-5 PASS

---

## Dependencies

- Depends on: Story 038 (the adapter core, the NGO shell, the PlayMode harness — Complete 2026-10-12); Story 006 (`PriorityPathQueue`), the envelope types and codec in `Foundation`, Story 037 (`ZoneTickPipeline`), Enhancement Story 010 (`IClientMessageOutbox`, the recording double) — Complete. ADR-004 Amendment 1 (2026-10-09).
- Unlocks: Enhancement Story 015 (result delivery reaches a client); the story that sends `SessionReady` and calls `OwnerInventorySyncSender.OnSessionReadySent`; the batch path story

---

## Completion Notes
**Completed**: 2026-10-12
**Criteria**: 21/21 passing (14 outbox and adapter criteria, Mapping, Send, VS-1 to VS-5). None deferred, no VS item NOT RUN or FAIL.
**Files**: `src/ServerLogic/Networking/Outbound/` — `ClientMessageOutbox.cs`, `NgoClientMessageSender.cs`, `IClientMessageSendSeam.cs`, `IClientConnectionListener.cs`, `IClientDisconnectRequester.cs`, `IClientMessageOutbox.cs` (doc comment); `src/ServerLogic/Networking/WireProtocol/PriorityPathQueue.cs`; `src/ServerLogic/Networking/Transport/InboundTransportAdapter.cs`, `NgoInboundTransportShell.cs`; `src/ServerLogic/InventorySystem/OwnerInventorySyncSender.cs` (doc comment); `tests/EditMode/Networking/ClientMessageOutbox_tests.cs` (47), `NgoClientMessageSender_tests.cs` (5), `WireProtocol_PriorityPathCap_tests.cs` (+8), `InboundTransportAdapter_tests.cs` (+7); `tests/EditMode/IronGrind.Foundation.EditModeTests.asmdef`; `tests/PlayMode/Networking/ClientMessageOutbox_ngo_run_tests.cs` (8).
**Deviations** (advisory, accepted by the user 2026-10-12):
- *Files beyond the list*: `NgoInboundTransportShell.cs` takes the optional listener and passes it to the adapter; the EditMode test asmdef references `Unity.Netcode.Runtime` so the mapping table is tested in EditMode; `NgoClientMessageSender_tests.cs` is new.
- *API beyond the story*: `PriorityPathQueue` has `Clear()`, `Count` and `Reserve(int)` beside the flush overload; `ClientMessageOutbox` takes an optional `MessageRoutingLookup` (null = the registry), which is how the channel clause of *Delivery* is tested — every priority row of the registry is `ReliableOrdered`.
- *Queue full*: `QueueFull` is logged, and the disconnect requested, on the first refusal of a client only; later refusals of that client are silent. With no requester set the error says so.
- *Exempt at 128 queued*: an exempt message is refused like any other and the client is disconnected. A test pins it as current behaviour; no design rule states it.
- *Sequence numbers*: a send that throws has already taken its number, so the client would see a gap.
- *Sender*: the message is written to the `FastBufferWriter` byte by byte. One `WriteBytesSafe` from a reusable array needs `Unity.Collections` on `IronGrind.ServerLogic`, which ADR-012's boundary test forbids.
- *Never throws*: the `FlushThrew` catch is kept and no test reaches it; a client removed from inside the send seam loses the rest of its messages of that flush; past 64 distinct unknown client ids in one tick no further warning is logged.
- *QA case "development-build flag on and off"*: the outbox reads no such flag (user decision 4), so there is nothing to flip.
- *VS-4*: the `GC.Alloc` marker read by code, one recorder around `Flush` (0 samples over 504 messages for the outbox alone, control 24 of 24, 0 with the sender and NGO's queueing over 504 more). A first two-recorder version read 0 for its own control and was replaced. No one looked at a Profiler window.
- *Limits of the run*: loopback, one client, Editor Test Runner (not a headless build); the delivery NGO used on the wire is not observed; a reliable send-queue overflow was not exercised.
**Test Evidence**: Integration — `tests/EditMode/Networking/ClientMessageOutbox_tests.cs`; `production/qa/evidence/networking-core-039-ngo-send-verification.md`.
**Code Review**: Complete — CHANGES REQUIRED (tests only), all required changes and suggestions applied except the two named above (sender write, `FlushThrew`); not re-run after the fixes. LP-CODE-REVIEW and QL-TEST-COVERAGE skipped — lean mode.
**Tech debt logged**: none (user's choice).
