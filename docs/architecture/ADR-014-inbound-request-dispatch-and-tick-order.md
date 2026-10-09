# ADR-014: Inbound Request Dispatch and Tick Order

## Status
Proposed

## Date
2026-10-09

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Networking (server). The dispatcher, the inbox and the tick pipeline are plain C#; the only engine-facing part is the transport adapter that feeds the inbox. |
| **Knowledge Risk** | LOW for Decisions 1 and 3–7 (no engine API). HIGH for the transport adapter of Decision 2: Netcode for GameObjects is past the LLM training cutoff, and `docs/engine-reference/unity/modules/networking.md` does not describe `CustomMessagingManager`, `FastBufferReader`, the player-loop stage in which messages are delivered, or transport size limits. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `modules/networking.md`, `breaking-changes.md` (NGO section), `deprecated-apis.md`. ADR-001, ADR-002, ADR-004, ADR-010, ADR-011, ADR-012; `architecture-review-2026-10-07.md` (P1). Code read: `src/ServerLogic/Networking/RpcGuards/` (`CrossCuttingRpcGuardChain`, `InboundRpcDescriptor`, `RpcTypeTag`, `RpcGuardResult`), `TickLoop/ServerTickLoop.cs`, `MutationGate/` (`ICharacterMutationGate`, `CharacterMutationGate`), `IrreversibleOutcome/IIrreversibleOutcomeCoordinator.cs`, `src/Foundation/Networking/WireProtocol/` (`ClientEntityMessageEnvelope`, `MessageRoutingRegistry`, `StaleDiscardComparer`). |
| **Post-Cutoff APIs Used** | None in the decision. The adapter will use whichever receive API ADR-004 OQ-ADR4-3 settles on; this ADR does not choose it. |
| **Verification Required** | All for the adapter story; none blocks the dispatcher. (1) Receive callbacks and the tick driver are different points of the same frame on the main thread, and no receive callback can run inside `ServerTickLoop.AdvanceTick`. The disconnect callback is the known exception: a server-initiated disconnect may raise it synchronously, inside the tick (Decision 4 is written for that). (2) The receive buffer is not valid after the callback returns; the adapter copies before returning, reads into the pre-allocated storage without allocating, and checks the length (at least the 14-byte envelope, body at most `MAX_INBOUND_BODY_BYTES`) before reading, so a short or oversize message returns false and never throws. (3) The transport's connection id (`ulong` in NGO) maps one-to-one to the `uint` client id the guard chain uses: the adapter drops a message whose id exceeds `uint.MaxValue`, and ids are not reused within a process lifetime (a reconnect gets a new id). (4) The NGO package version that Unity 6.3 resolves, and that its custom-message receive API matches what the adapter uses. (5) For the reliable ordered delivery that requests use, messages of one connection arrive in the order the client sent them. (6) Whether queued data of a connection can still be delivered after its disconnect event in the same frame (Decision 2 drops it either way). |
| **Specialist Validation** | `unity-specialist`, 2026-10-09: SOUND WITH NOTES, no blocking finding. It read this draft, the two engine reference files, and the guard chain, tick loop and gate code. The reference docs are silent on NGO messaging internals, so its statements about NGO behaviour are from memory; they are recorded here as items to verify, not as facts. Five findings changed the text: Pass A no longer depends on `OnGateOpened` (a throwing earlier subscriber would have stranded a hold queue); the guard chain needs three stated changes (Decision 4a); `RemoveConnection` is deferred (a disconnect can be raised inside the tick); guard rejection logging is throttled; each pipeline step is isolated. Its statements about the code were checked against the source by the author and hold. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-004 (project envelope over the transport; `clientId ↔ EntityID` mapping), ADR-010 (network callbacks enqueue, the tick executes), ADR-011 (completion queue, mutation gate, hold rule), ADR-012 (`IronGrind.ServerLogic`; handlers registered by the server composition root). All Accepted. |
| **Enables** | The server composition root; every story that handles a client request (Inventory, Equipment, NPC Shop, Enhancement, Loot bids, Party, Leveling). |
| **Blocks** | Enhancement Story 009 (Attempt Exclusivity — Held Requests). |
| **Ordering Note** | Decisions 1 and 3–7 need no transport and can be built and tested first. The adapter of Decision 2 waits for ADR-004 OQ-ADR4-3. Replaces the per-handler queue example of ADR-010 Decision 5; the rule of that decision stands. How `AdvanceTick` is driven from the player loop, and its relation to NGO's own tick (ADR-004 Decision 5), is not decided here. |

## Context

### Problem Statement
ADR-011 Decision 4 relies on "the session's inbound request dispatcher — the single point between the RPC guard chain and game logic". No decision defines that component. ADR-010 Decision 5 shows one `Queue<T>` per handler, drained by each system's own `Tick()`, and its example uses `[ServerRpc]`, which ADR-004 Decision 4 excludes for gameplay messages. Not defined anywhere: the request descriptor (`HeldDuringIrreversibleWrite`), arrival order across message types, where the guards and request deduplication sit relative to the hold queue, how a flood is bounded, and the complete order of one server tick. Enhancement Story 009 is blocked on it, and no client request can be wired to a game system without it.

### Constraints
- Game state is single-threaded: everything runs on the Unity main thread (`zone-instancing.md` Runtime Model), and game logic runs only inside the tick (ADR-010 Decision 5).
- The tick loop, the completion queue, the mutation gate and the coordinator exist and are tested, and are used as they are. The guard chain is used with the three changes of Decision 4a.
- No per-request allocation on the server hot path; buffers are pre-allocated per zone, never rented from `ArrayPool<T>.Shared` (`networking-wire-protocol.md`, buffer allocation rule).
- No transport type may appear in the dispatcher: the transport choice inside NGO is still open (ADR-004 OQ-ADR4-3), and the dispatcher must be testable in EditMode with no network session.

### Requirements
- One enforcement point for the mutation gate (ADR-011; control manifest: no game system reads the gate or holds a request).
- A client request held during an irreversible write is processed in arrival order when the gate opens, before that tick's new requests (ADR-011 Decision 4; `enhancement-system.md` CR-ENH-18, AC-ENH-38).
- Requests rejected by a cross-cutting guard are dropped, never queued (`networking-core.md` Cross-Cutting Constraints 1–3).
- A fixed, testable order for one tick: completion callbacks, client requests, simulation, outbound.
- One failing request, handler or pipeline step must not stop the tick.

## Decision

**One `InboundRequestDispatcher` per zone process, in `IronGrind.ServerLogic`, is the only path from a client message to a game system. A transport adapter copies each message into a bounded inbox and does nothing else. On the tick, the dispatcher first releases held requests whose gate is open, then takes the inbox in arrival order and, for each request, runs the guard chain, checks the mutation gate, and calls the one handler registered for that message type. A `ZoneTickPipeline` fixes the order of the tick.**

### Decision 1 — Scope: what goes through the dispatcher
- Every client→server message that reaches a game system is a *request* and goes through the dispatcher: inventory, equipment, shop, enhancement, loot bid, party, leveling, targeting, skill use.
- Connection-level messages (session handshake, heartbeat, `ClientBackgrounded` / `ClientForegrounded`) are consumed by the connection and session state machines. They share the adapter and the stale-sequence check of Decision 2, but they are not requests: they have no descriptor, are not guarded by the session-ready gate (some arrive before `SessionReady`), and are never held.
- Server-originated work (loot pickup, auction delivery, AI) does not go through the dispatcher. ADR-011 already says what it does when the gate is closed.

### Decision 2 — Intake: the adapter and the inbox
- The adapter is the only code that touches the transport's receive API. In its callback it checks the length, decodes the 14-byte `ClientEntityMessageEnvelope`, and calls `IInboundRequestInbox.TryAccept(clientId, envelope, body)`. It calls no game system, no guard and no handler.
- `TryAccept` runs on the main thread, outside the tick. It:
  1. drops a message from a connection that was never added or has been removed (`AddConnection` / `RemoveConnection`, Decision 4);
  2. drops a message whose `SequenceNumber` is stale for that connection (CR-NET-7.5, `StaleDiscardComparer`);
  3. drops a message whose body is longer than `MAX_INBOUND_BODY_BYTES` (default 64; the largest client→server body in `networking-wire-protocol.md` today is 12 bytes) or whose `MessageTypeId` has no registered descriptor — anomaly `UnknownInboundMessageType`;
  4. drops it if that connection already has `MAX_INBOX_REQUESTS_PER_CONNECTION` (default 64) undispatched requests — anomaly `InboundInboxOverflow`, logged once per connection per tick;
  5. otherwise copies the body into the inbox's pre-allocated storage and records `(arrivalIndex, clientId, envelope, body location)`.
- The connection's highest seen `SequenceNumber` advances only for a message accepted in step 5.
- `arrivalIndex` is one zone-wide counter: the order in which the server received the messages, across all connections and all message types. Every request type is sent on the reliable ordered channel (R-OD in `networking-wire-protocol.md`), so for one connection receive order is the client's send order (Verification Required 5). Between connections it is receive order, which is the only order the server can know.
- The inbox storage is one flat array allocated when the zone is created: `MAX_PLAYERS_PER_ZONE × MAX_INBOX_REQUESTS_PER_CONNECTION` entries of `MAX_INBOUND_BODY_BYTES` (50 × 64 × 64 B = 205 KB). `TryAccept` allocates nothing.
- The inbox is emptied completely by every tick's dispatch (Decision 4), so no backlog carries from one tick to the next. The per-connection bound is a bound on what one client can add between two ticks.
- Guards do not run at intake. Guard state is read at one fixed point of the tick (Decision 4).

### Decision 3 — Registration: one descriptor and one handler per message type
- The server composition root registers, for each request type, an `InboundRequestDescriptor` and a handler, then calls `Seal()`. Registration after `Seal()` throws.
- The descriptor carries: `MessageTypeId`; `RpcTypeTag` (the rate-limit bucket the guard chain already uses; the enum gains a member per request type, most with no limit); `HeldDuringIrreversibleWrite`.
- `HeldDuringIrreversibleWrite` is true for every request that can change a character's bag, equipment or gold: move, equip, unequip, discard, sell, buy, consumable use, accessory merge (ADR-011 Decision 4). It is false for movement, targeting, chat, party and loot-bid requests, and for the enhancement request itself, which `EnhancementService` rejects as a concurrent attempt.
- A held-type request must not carry a rate-limited `RpcTypeTag`: the guard spends the rate-limit slot when it accepts, and a request then dropped by a full hold queue would have spent it for nothing. No bag-mutating request is rate-limited today.
- Registration fails at startup, not at runtime, when: the type is registered twice; the type has no `MessageRoutingRegistry` entry with direction client→server (`PendingSchemaDispatchException`, `networking-message-criticality.md` AC-MCR-03); the handler is null.
- A handler is `void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)`. It decodes the body with the message's codec and calls its system. It runs on the tick thread. It cannot keep `body` after it returns (a span cannot be stored in a field), must not `await` (ADR-011), and must not read `ICharacterMutationGate`.
- Request deduplication stays in the handler's system (ADR-001 for shop requests, `EnhancementRequestDeduplicator` for enhancement). The dispatcher never reads a `requestId`. A retransmit that arrives while the original is held is held too; on release the original executes and the retransmit receives the cached result.

### Decision 4 — Dispatch: what happens to one request on the tick
`InboundRequestDispatcher.DispatchTick(currentTick)` runs once per tick, after `ITickCompletionQueue.Drain` (Decision 6). It first frees the storage of connections removed since the previous call, then runs two passes.

**Pass A — release.** The dispatcher keeps the list of characters that have a non-empty hold queue, in the order each one's first request was held. For each of them whose gate is not held (`!gate.IsHeld(charId)`), the held requests are dispatched in arrival order. The dispatcher does not subscribe to `ICharacterMutationGate.OnGateOpened`: polling the gate cannot miss an opening (an event subscriber that throws stops the subscribers after it), and it also serves a gate opened outside `Drain`. Before each released request the dispatcher checks that the connection has not been removed, still owns the entity and is still session-ready (`IsLiveOwner`, Decision 4a); a request that fails is discarded. This is how held requests are discarded after a failed write: the failure protocol has disconnected the client by then. Released requests are not rate-limited again. If a released request closes the gate, the remaining ones stay held.

**Pass B — new requests.** The inbox is taken in `arrivalIndex` order. Entries of a removed connection are skipped. For each request:
1. **Guard chain.** `CrossCuttingRpcGuardChain.Evaluate(new InboundRpcDescriptor(clientId, senderEntityId, descriptor.RpcTypeTag, currentTick))`. Any result other than `Accepted` drops the request. Nothing rejected by a guard is ever held.
2. **Character.** The dispatcher resolves the connection's `CharacterID` through `IConnectionCharacterDirectory`. No character → dropped, anomaly `InboundRequestWithoutCharacter`.
3. **Gate.** If `descriptor.HeldDuringIrreversibleWrite` and (`gate.IsHeld(charId)` or that character still has held requests), the body is copied to that character's hold queue. A full hold queue (`MAX_HELD_REQUESTS_PER_CHARACTER` = 16, ADR-011) drops the request — anomaly `HeldRequestOverflow`.
4. **Handler.** Otherwise the handler is called.

Rules that follow:
- **Isolation.** Steps 1–4 of one request run inside one `try`/`catch`: an exception from the guard, the directory, the gate or the handler is logged as a server error with the message type and client id, that request is dropped, and the pass continues — the policy of `TickCompletionQueue` callbacks.
- A handler may close the gate (by starting an irreversible outcome). Later held-type requests of that character in the same pass are then held.
- **Connections.** `AddConnection(clientId)` is called when a connection is established. `RemoveConnection(clientId)` — called on disconnect and on zone transfer — only marks the connection removed. It may be called from inside a pass (a handler or a completion callback that disconnects a client can raise the transport's disconnect callback synchronously), so it frees nothing: the passes skip that connection's entries, and their storage is released at the start of the next `DispatchTick`.
- The dispatcher sends nothing to a client. A dropped or discarded request gets no reply; replies are the handler's system's business.

### Decision 4a — Changes to `CrossCuttingRpcGuardChain`
The guard chain was written before any request type other than three existed. Three changes, made by the dispatcher story:
1. **Tags with no rate limit.** `GetRequiredTickGap` throws for a tag it does not list, by design: a new tag must not bypass rate limiting silently. That intent is kept, but moved from the running server to the test suite: every `RpcTypeTag` member is listed in the switch with its gap (0 for no limit), and a test asserts that every member of the enum returns without throwing. `Evaluate` skips the last-accepted-tick read and write when the gap is 0.
2. **A read-only liveness query.** `bool IsLiveOwner(uint clientId, EntityID entityId)`: the entity is registered, owned by that client, and the client is session-ready. It writes nothing, so Pass A can re-check a held request without touching the rate-limit state.
3. **Throttled rejection logs.** `Evaluate` logs every rejection with an interpolated string, which allocates and captures a stack trace. At the inbox bound that is up to 64 log calls per client per tick. The first rejection per (client, result) per tick is logged in full; the rest are counted and reported in one line per tick.

### Decision 5 — Hold queue storage
Each character with held requests has a hold queue of at most 16 entries, each a copy of the request context and body, taken from a pre-allocated pool sized for `MAX_PLAYERS_PER_ZONE × 16` entries. The copy is needed because the inbox storage is reused by the next tick. No allocation when a request is held.

### Decision 6 — The order of one server tick
`ZoneTickPipeline` is one class in `IronGrind.ServerLogic`. The composition root registers its `Tick` as the one tick-driven delegate of `ServerTickLoop`. It reads the tick number from `ServerTickLoop.ServerTickNumber` and runs four steps in this order:

1. **Completion** — `ITickCompletionQueue.Drain(currentTick)` (ADR-011: before any game logic; callbacks open gates).
2. **Requests** — `InboundRequestDispatcher.DispatchTick(currentTick)`: Pass A, then Pass B.
3. **Simulation** — ADR-002 phases 1–4, in ADR-002's order. Game systems that tick (Enemy AI, combat, status effects, loot auction timers, the server-originated bag mutators retrying after `OnGateOpened`) run in phase 3, in an order the composition root lists explicitly.
4. **Outbound** — the per-connection writers flush (priority path, R-U batch, cycle broadcast, position packet).

Each step runs in its own `try`/`catch` that logs a server error and continues. `ServerTickLoop.AdvanceTick` lets an exception from a tick-driven delegate escape and then skips the TTL pass, the drift sample and the observer; with the steps isolated, a failing step never skips the later steps or the rest of `AdvanceTick`.

Then `ServerTickLoop` fires TTL timers and the test observer, as it does today. A message produced by a TTL callback leaves on the next tick.

Consequences of this order: the reply to a request received before tick N starts is written in tick N's outbound step (the transport puts it on the wire at its own point later in the frame); a write completed before tick N has its outcome delivered, and its held requests processed, before any new request of tick N; simulation always sees the bag state the requests of that tick left.

New code does not call `ServerTickLoop.RegisterTickDriven` for game logic. Nothing in `src/` calls it today.

### Decision 7 — ADR-010 Decision 5
The rule of ADR-010 Decision 5 stands: a network callback enqueues and never calls game logic. Its example — a `[ServerRpc]` and one `Queue<T>` per handler drained by the system's own `Tick()` — is replaced by this ADR for client requests. ADR-010 receives a dated note pointing here.

### Architecture Diagram
```
main thread, outside the tick                 main thread, inside ServerTickLoop.AdvanceTick
──────────────────────────────                ───────────────────────────────────────────────
transport receive callback                    ZoneTickPipeline.Tick   (each step isolated)
  └ adapter: length check, decode envelope     1. ITickCompletionQueue.Drain ── callbacks ── gate.Open
      └ IInboundRequestInbox.TryAccept         2. InboundRequestDispatcher.DispatchTick
          connection? stale? oversize?            free storage of removed connections
          unknown type? inbox full?               Pass A: characters with held requests, gate not held
          copy body, arrivalIndex++                 └ IsLiveOwner → handler      (arrival order)
                    │                             Pass B: inbox, arrivalIndex order
                    └────────────────────────────►  guard chain → character → gate ─┬─ held → hold queue
                                                                                    └─ handler → system
                                               3. ADR-002 phases 1–4 (systems tick in phase 3)
                                               4. outbound writers flush
                                              then: TTL timers, observer
```

### Key Interfaces
```csharp
namespace IronGrind.Networking   // assembly IronGrind.ServerLogic
{
    public readonly struct InboundRequestDescriptor
    {
        public readonly ushort MessageTypeId;
        public readonly RpcTypeTag RpcTypeTag;
        public readonly bool HeldDuringIrreversibleWrite;
    }

    public readonly struct InboundRequestContext
    {
        public readonly uint ClientId;
        public readonly EntityID SenderEntityId;
        public readonly CharacterID CharacterId;
        public readonly ushort MessageTypeId;
        public readonly uint ArrivalTick;    // ServerTickLoop.ServerTickNumber when TryAccept ran
        public readonly uint DispatchTick;   // tick on which the handler runs
        public readonly bool WasHeld;
    }

    // A custom delegate: ReadOnlySpan<byte> cannot be a generic argument of Action<>.
    public delegate void InboundRequestHandler(in InboundRequestContext context, ReadOnlySpan<byte> body);

    public interface IInboundRequestInbox
    {
        // Main thread, outside the tick. Copies body. False = dropped (reason logged). Never throws.
        bool TryAccept(uint clientId, in ClientEntityMessageEnvelope envelope, ReadOnlySpan<byte> body);
    }

    public interface IInboundRequestDispatcher
    {
        void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler);
        void Seal();
        void AddConnection(uint clientId);
        void RemoveConnection(uint clientId);    // marks only; safe to call inside a pass
        void DispatchTick(uint currentTick);     // tick thread; called by ZoneTickPipeline only
        int HeldCount(CharacterID charId);       // for tests and diagnostics
    }

    public interface IConnectionCharacterDirectory
    {
        bool TryGetCharacterId(uint clientId, out CharacterID charId);
    }

    // Added to CrossCuttingRpcGuardChain (Decision 4a):
    //   public bool IsLiveOwner(uint clientId, EntityID entityId);
}
```
Constants: `MAX_INBOX_REQUESTS_PER_CONNECTION = 64`, `MAX_INBOUND_BODY_BYTES = 64`, `MAX_HELD_REQUESTS_PER_CHARACTER = 16` (ADR-011, unchanged). `ArrivalTick` and `DispatchTick` are `ServerTickLoop.ServerTickNumber` values, never the transport's own tick counter.

## Alternatives Considered

### Alternative 1: Per-system queues (ADR-010 Decision 5 as written)
- **Description**: each system owns a `Queue<T>` per request type and drains it in its own `Tick()`; a shared helper checks the gate.
- **Pros**: no central component; typed queues, no byte copies.
- **Cons**: arrival order across message types is lost (an unequip and a move sent in that order can run in the other); every system must remember the guard chain, the gate and the hold rule; ADR-011's "single enforcement point" and the control manifest's ban on reading the gate from a game system are both broken.
- **Rejection Reason**: it contradicts ADR-011 and makes CR-ENH-18 depend on every system doing the same thing right.

### Alternative 2: Dispatcher inside the transport layer
- **Description**: guard, gate and hold logic live in the transport's message handlers.
- **Pros**: one less layer; no body copy at intake.
- **Cons**: the logic cannot be tested without a network session; game logic would run in a transport callback, outside the tick, which ADR-010 forbids — or the handlers would enqueue anyway, which is this ADR with the logic in the wrong assembly; it ties the dispatcher to an API that ADR-004 OQ-ADR4-3 may still replace.
- **Rejection Reason**: untestable in EditMode and bound to an unverified post-cutoff API.

### Alternative 3: Guards at receipt
- **Description**: the adapter runs the guard chain before enqueueing.
- **Pros**: rejected requests never occupy the inbox.
- **Cons**: guard state (session-ready, ownership, rate-limit ticks) is read at an arbitrary point between ticks; the adapter depends on the guard chain.
- **Rejection Reason**: decided by the user 2026-10-09 — guards run on the tick; the inbox bound of Decision 2 covers the flood case.

### Alternative 4: Deduplication in the dispatcher
- **Description**: the dispatcher drops a duplicate of a held request.
- **Pros**: retransmits do not use hold-queue slots.
- **Cons**: the dispatcher must read `requestId` from every payload and keep dedup state the systems already own.
- **Rejection Reason**: decided by the user 2026-10-09 — a write lasts one to two ticks, so held retransmits are rare, and the systems' dedup handles them on release.

### Alternative 5: Per-tick dispatch budget with a carried backlog
- **Description**: at most N requests per connection per tick; the rest wait.
- **Pros**: smooths bursts.
- **Cons**: a second queue, added latency, and a backlog to bound as well.
- **Rejection Reason**: decided by the user 2026-10-09 — a bounded inbox emptied every tick is simpler and has the same worst case per tick.

### Alternative 6: Release held requests from `OnGateOpened`
- **Description**: the dispatcher subscribes to the gate's event and records which characters to release.
- **Pros**: no scan of the characters with held requests.
- **Cons**: `CharacterMutationGate.Open` invokes a multicast delegate; a subscriber that throws stops the ones after it, so the dispatcher could miss an opening and the character's bag requests would be held for the rest of the session. It also adds a subscription to dispose.
- **Rejection Reason**: polling `IsHeld` for at most 50 characters per tick is cheaper than the failure it removes.

## Consequences

### Positive
- One place holds, drops and orders client requests; game systems contain none of that logic.
- The whole request path except the adapter is plain C# and testable in EditMode with a fake inbox feed.
- The tick order is code in one class, not a registration convention, and one failing step does not stop the tick.
- Enhancement Story 009 becomes implementable.

### Negative
- Each request body is copied once at intake, and once more when held.
- Handlers decode bytes; a handler per request type must be written and registered.
- `RpcTypeTag` grows with every request type, and `GetRequiredTickGap` with it.
- The guard chain, a Complete story's code, is changed (Decision 4a).
- New components: the inbox, the dispatcher with its hold queues, `ZoneTickPipeline`, `IConnectionCharacterDirectory`, the adapter.

### Risks
- **A request is dropped silently when the inbox or a hold queue is full.** The client gets no reply. Mitigation: the bounds are far above legitimate use (64 requests per connection per 50 ms tick; 16 held during a write of one to two ticks); every drop is an anomaly log; the bounds are tuning knobs.
- **Flood cost.** A client at the inbox bound costs 64 guard evaluations per tick; 50 such clients, 3,200. Mitigation: the evaluations do not allocate; rejection logging is throttled (Decision 4a).
- **The inbox bound is per tick interval, not per frame.** If the server runs frames faster than ticks, or a tick is late, messages of several frames land in one inbox. The bound still holds; a slow tick makes a legitimate client more likely to reach it. Mitigation: the bound is 64; the tick driver and the server frame rate are fixed by the story that drives `AdvanceTick` from the player loop.
- **`IConnectionCharacterDirectory` has no implementation.** The guard chain maps `clientId ↔ EntityID`; nothing maps a connection to its `CharacterID` yet. Mitigation: the session layer that sends `SessionReady` (which carries both ids) implements it; the dispatcher story uses a fake.
- **Phase 3 order between systems is not decided here.** Mitigation: the composition root lists it explicitly; it is decided when the second ticking system exists.
- **The adapter's assumptions about the transport are unverified** (Verification Required 1–6). Mitigation: the adapter story verifies them; the dispatcher is written so that none of them, if false, corrupts state (it copies always, tolerates a disconnect inside the tick, and drops messages of unknown connections).
- **Connection-level messages are outside the dispatcher**, so two code paths read inbound messages. Mitigation: both go through the same adapter and stale-sequence check; Decision 1 lists which types are which.
- **Outbound writers may still hold messages for a removed connection.** Not this ADR's component. Mitigation: the adapter story states that a send to a removed connection is dropped.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| enhancement-system.md | CR-ENH-18, AC-ENH-38 — other bag-mutating requests are held during an attempt and processed in arrival order afterwards | Decision 4: gate check per request, hold queue, Pass A before new requests |
| networking-core.md | Cross-Cutting Constraints 1–3 — unknown entity, session-ready gate, rate limits; rejected requests dropped, not queued | Decision 4 step 1: the existing guard chain runs for every request, before the gate |
| networking-core.md | CR-NET-2 — fixed 20 Hz tick; game logic on the tick | Decision 6: one pipeline, four steps |
| networking-wire-protocol.md | CR-NET-7.1 client→server envelope; CR-NET-7.5 stale discard; pre-allocated buffers | Decision 2: envelope decoded by the adapter, stale check at intake, inbox storage allocated per zone |
| networking-message-criticality.md | AC-MCR-03 — a type with no routing row fails at registration | Decision 3: registration checks `MessageRoutingRegistry` and throws at startup |
| npc-shop.md / ADR-001 | Shop requests deduplicated on `(charId, messageType, requestId)` | Decision 3: dedup stays in the handler's system, after release |
| zone-instancing.md | Runtime Model — single-threaded main loop | Decisions 2 and 4: intake and dispatch both on the main thread, at defined points |

## Performance Implications
- **CPU**: per request, one guard evaluation (dictionary lookups, no allocation), one directory lookup, one gate lookup, one delegate call. Pass A scans at most 50 characters. Worst case per tick 50 × 64 = 3,200 requests if every client floods to the bound; expected load is a few requests per client per second.
- **Memory**: inbox 205 KB and hold pool 50 × 16 × (64 B + context) ≈ 70 KB per zone, allocated once.
- **Load Time**: none.
- **Network**: none. The dispatcher sends nothing.

## Migration Plan
Nothing dispatches client requests today and nothing calls `RegisterTickDriven`, so no behaviour a player can observe changes. One existing class changes: the guard chain (Decision 4a).

1. **Dispatcher story (Networking Core).** `InboundRequestDescriptor`, `InboundRequestContext`, the inbox, the dispatcher with hold queues, `IConnectionCharacterDirectory` (interface and test fake), and the three guard-chain changes of Decision 4a with their tests (every `RpcTypeTag` member returns a gap; `IsLiveOwner` writes nothing; throttled logs). EditMode tests: arrival order across types and connections; each guard rejection drops and never holds; hold while the gate is closed, release order, release before new requests; release when a *different* `OnGateOpened` subscriber throws; discard on release when the connection is gone; `RemoveConnection` called from inside a handler; overflow of inbox and hold queue; registration failures; a throwing handler, guard or directory does not stop the pass.
2. **Tick pipeline story (Networking Core).** `ZoneTickPipeline` with the four isolated steps; tests assert the order with recording fakes, and that a throwing step does not skip the later ones.
3. **Enhancement Story 009.** Unblocked after step 1: its acceptance criteria are tested against the dispatcher with a fake persistence task. Its first criterion lists "pickup" among held requests; under ADR-011 a server pickup is deferred, not held — corrected at its `/story-readiness`.
4. **Adapter story.** After ADR-004 OQ-ADR4-3. Carries Verification Required 1–6.
5. **Documents.** A dated note on ADR-010 Decision 5; `/create-control-manifest update`; `architecture-traceability.md` P1; registry entries as approved by the user.

## Validation Criteria
- The dispatcher tests of Migration Plan step 1 pass with no network session.
- AC-ENH-38 passes against the dispatcher: the unequip sent while the write is in flight runs only after the rollback, and fails for lack of bag space.
- A source search finds `ICharacterMutationGate` read only in the dispatcher and in the server-originated bag mutators ADR-011 names, and no subscription to `OnGateOpened` in the dispatcher.
- A source search finds `RegisterTickDriven(` called only by the composition root, for `ZoneTickPipeline`.
- The pipeline test shows Drain → Pass A → Pass B → simulation → outbound.
- A test enumerating `RpcTypeTag` passes: no member makes `Evaluate` throw.

## Related Decisions
- ADR-010 Decision 5 — the rule this ADR implements; its example is replaced.
- ADR-011 Decision 4 — the gate and the hold rule this ADR gives an owner to. TD-060 (a throwing `OnGateOpened` subscriber stops the later ones) no longer affects the dispatcher; it still affects the server-originated bag mutators.
- ADR-012 Decision 4 — handlers registered by the server composition root.
- ADR-004 Decision 4, Decision 5 and OQ-ADR4-3 — the envelope, the clock source, and the open transport API choice.
- ADR-002 — simulation phases 1–4.
- ADR-001 — shop request deduplication.
- `docs/architecture/architecture-review-2026-10-07.md` P1 — the finding this ADR closes.
