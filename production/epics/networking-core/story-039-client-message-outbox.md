# Story 039: Client Message Outbox — the Production Send Path for Priority Messages

> **Epic**: Networking Core
> **Status**: Not Started — written 2026-10-09; `/story-readiness` pending (six open decisions, listed below). Blocked on Story 038 (the NGO package compiling in `IronGrind.ServerLogic`, the `NetworkManager` ownership and the connection id mapping).
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

*Added 2026-10-09 (user decision: the adapter work is two stories). `IClientMessageOutbox` was defined by Enhancement Story 010 as the server-to-client send seam and has only a recording test double. Four callers enqueue on it today — the Enhancement request handlers, the NPC interaction handlers, `OwnerInventorySyncSender`, and Enhancement Story 015 when it is written — and nothing sends. This story writes the production implementation: per-client queueing under the priority-path cap, the server envelope, and the send through NGO.*

> **User decisions 2026-10-09.** Messages are NGO unnamed custom messages (ADR-004 Amendment 1). The NGO-facing code lives inside `IronGrind.ServerLogic` (ADR-012 Decision 1).

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
- [ ] **Envelope**: each sent message starts with a `ServerMessageEnvelope` carrying the enqueued `messageTypeId`, the tick number of the flush, and the sequence number decided at `/story-readiness` (open decision 2).
- [ ] **Cap (CR-NET-7.7)**: with 11 non-exempt messages enqueued for one client in one tick, 8 are sent at that tick's flush and 3 at the next, in emission order; none is dropped. The cap is per client: a second client's 8 are all sent the same tick.
- [ ] **Exemption**: with 8 non-exempt messages queued, an exempt message enqueued before the flush is sent first that tick, and the tick sends 9; the non-exempt messages keep their order.
- [ ] **Exemption after the flush**: an exempt message enqueued after the tick's flush is first in the next tick's flush.
- [ ] **Delivery**: each message is sent with the channel of its `MessageRoutingRegistry` row; a `messageTypeId` with no row is not sent, and one server error is logged (development builds may throw at startup instead, per AC-MCR-03 — open decision 4).
- [ ] **Size**: a body above `MAX_MESSAGE_BODY_BYTES` (512, CR-NET-7.6 — the cap is on the body, the envelope is not counted) is not sent and one server error is logged; a body of exactly 512 bytes is sent.
- [ ] **Unknown or removed client**: `Enqueue` for a client that was never added or has been removed sends nothing, throws nothing, and logs at most one warning per client per tick; messages still queued for a client are discarded when it is removed.
- [ ] **Never throws**: `Enqueue` and the flush do not throw for any input above, and a throwing send seam does not stop the flush of the other clients; the failure is logged once per client per tick. `IClientMessageOutbox`'s doc comment states that `Enqueue` does not throw.
- [ ] **No allocation**: `Enqueue` and a flush allocate nothing per message once a client's storage exists (asserted over 1,000 messages).
- [ ] **Callers unchanged**: the existing tests of the Enhancement request handlers, the NPC interaction handlers and `OwnerInventorySyncSender` pass unedited; `OwnerInventorySyncSender`'s remark that `Enqueue` is assumed not to throw is updated to cite this story.

**NGO sender (compiles in `IronGrind.ServerLogic`)**

- [ ] **Mapping**: `ReliableOrdered` → `NetworkDelivery.ReliableSequenced`, `ReliableUnordered` → `NetworkDelivery.Reliable`, `Unreliable` → `NetworkDelivery.Unreliable` (ADR-004 Decision 2 and Amendment 1); the mapping is one table, tested in EditMode without a session.
- [ ] **Send**: one `SendUnnamedMessage(clientId, writer, delivery)` per message, the writer created with `Allocator.Temp` and disposed in the same call; a source search finds `SendUnnamedMessage` and `FastBufferWriter` in the sender's file only, in all of `src/`.

**Verification in a real run (evidence document)**

*Recorded in `production/qa/evidence/networking-core-039-ngo-send-verification.md`, PASS / FAIL / NOT RUN with the observation.*

- [ ] **VS-1 Round trip**: an `InventoryFullSync` (211 bytes with its envelope) and an `InventorySlotUpdate` enqueued on the server arrive at a client byte for byte, in order.
- [ ] **VS-2 Cap on the wire**: 11 messages enqueued in one tick arrive as 8 and then 3, one tick apart, by the envelope's tick number.
- [ ] **VS-3 Removed client**: enqueueing for a client after the adapter's `Disconnect` puts nothing on the wire and logs no NGO error.
- [ ] **VS-4 Allocation**: the profiler shows no managed allocation in the outbox and the sender per message; NGO's own are recorded as observed.
- [ ] **VS-5 Oversize**: what NGO does with a 1,300-byte `ReliableSequenced` message in a development build and in a release build is recorded (the outbox never sends one; this pins the reference).

---

## Implementation Notes

- **Three parts.** The outbox (plain C#): per-client storage, `Enqueue`, `Flush(tick)`. The send seam: one method taking the client id, the message bytes and the `NetworkChannel`. The NGO sender: the seam's implementation over `CustomMessagingManager`. All behaviour is tested on the outbox with a recording seam.
- **Reuse `PriorityPathQueue<T>`** (`src/ServerLogic/Networking/WireProtocol/`, Story 006): it already implements the cap of 8, the exempt front slot and `Flush(tickNumber)`. Its `Flush` returns an `IReadOnlyList`; check that it allocates nothing per flush before relying on it for the no-allocation criterion, and record what was found.
- **Storage.** `Enqueue` must copy the body (the interface says so) into storage that exists before the message arrives: a per-client byte arena sized at `AddClient`, in the manner of the inbox arena of Story 036. Its size, and what happens when it is full, are open decision 3.
- **The flush is the Outbound step** of `ZoneTickPipeline` (the `outboundStep` delegate of its constructor). The composition root wires it; this story provides the method and tests it by calling it.
- **Envelope.** `ServerMessageEnvelope` and `MessageEnvelopeCodec` exist in `Foundation`. The outbox writes the envelope at flush time, so the tick number is the flush tick.
- **Clients.** The outbox learns of a client through explicit `AddClient(uint)` / `RemoveClient(uint)` calls, like the dispatcher's `AddConnection` / `RemoveConnection`; who calls them is open decision 5.
- **Reliable queue overflow.** NGO disconnects a client whose reliable send queue overflows. The outbox cannot prevent it; the disconnect arrives through Story 038's adapter like any other. State this in the class remarks.
- **The broadcast.** `ServerBroadcast_Enhancement9` goes to every connected client of every zone. The outbox is per client; the caller (Enhancement Story 015) loops, or a later story adds a broadcast call. Not decided here.

**Open decisions for `/story-readiness`**

1. **Priority path only?** Every caller today enqueues `0xE000–0xEFFF` types, and the interface carries `isCapExempt`, a priority-path notion. Proposed: this outbox is Path 1 only and rejects a type outside the priority range with an error; the batch path (Path 2, `RUBatchWriter`, the 512-byte per-client batch of TR-net-003) is a later story.
2. **The server envelope's `SequenceNumber`.** `ConnectionSequenceCounter` exists; CCR-1 states one counter per connection for the client's messages. Which counter numbers server → client messages, and whether the client checks it, must be read from `networking-channel-contract.md` and `networking-wire-protocol.md` before the criterion is final.
3. **Arena size and overflow.** Proposed: per client, enough for the cap plus the exempt messages at 512 bytes each for two ticks; on overflow, the message is not queued, one error is logged, and the client is disconnected — a priority message is never silently dropped (CR-NET-7.7: "no message is dropped, only deferred"). This is a design decision: the GDD states no bound on the deferred queue.
4. **A type with no routing row.** AC-MCR-03 says a development build throws at startup and a release build logs. `Enqueue` runs at runtime, not at startup; decide whether it throws in development builds, given the "never throws" criterion.
5. **Who adds and removes clients.** Proposed: Story 038's adapter core calls `AddClient` / `RemoveClient` beside `AddConnection` / `RemoveConnection`, so one place owns the connection list; that edits Story 038's class from this story.
6. **The run.** Same harness as Story 038's verification (its open decision 1); this story adds a client-side receive of unnamed messages to it.

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

---

## QA Test Cases

**Files** (to be created): `tests/EditMode/Networking/ClientMessageOutbox_tests.cs`; the run's additions to Story 038's harness; `production/qa/evidence/networking-core-039-ngo-send-verification.md`.

- **Copy** — enqueue, overwrite the source span, flush → the original bytes are sent.
- **Order and envelope** — three messages, one client → three sends in order; each starts with the type id, the flush tick and the decided sequence number.
- **Cap** — 11 non-exempt → 8 then 3; two clients with 8 each → 16 the same tick.
- **Exempt before the flush** — 8 queued, one exempt → 9 sent, the exempt first.
- **Exempt after the flush** — sent first at the next flush.
- **Channel** — one R-OD type, one R-U type, one U-U type (if any is in the priority range; otherwise the mapping table test covers it) → the row's channel on each send.
- **No routing row** — not sent, one error.
- **Size** — body 512 bytes sent; body 513 bytes not sent, one error.
- **Unknown client** — nothing sent, no exception; **removed client** — queued messages discarded.
- **Throwing seam** — client A's send throws → client B's messages are still sent that tick; one error.
- **No allocation** — 1,000 messages, zero bytes allocated in `Enqueue` and flush.
- **Mapping table** — the three channels map to the three `NetworkDelivery` members named above.
- **Run** — the five VS items, each with its observation.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/ClientMessageOutbox_tests.cs` — must exist and pass; `production/qa/evidence/networking-core-039-ngo-send-verification.md` — the run, every VS item PASS / FAIL / NOT RUN with its observation.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 038 (blocking)**; Story 006 (`PriorityPathQueue`), the envelope types and codec in `Foundation`, Story 037 (`ZoneTickPipeline`), Enhancement Story 010 (`IClientMessageOutbox`, the recording double) — Complete. ADR-004 Amendment 1 (2026-10-09).
- Unlocks: Enhancement Story 015 (result delivery reaches a client); the story that sends `SessionReady` and calls `OwnerInventorySyncSender.OnSessionReadySent`; the batch path story
