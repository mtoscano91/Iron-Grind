# ADR-014: Inbound Request Dispatch and Tick Order

## Status
Proposed

## Date
2026-10-09

Amended 2026-10-09 after `architecture-review-2026-10-09-adr-014.md` (B1, B2, C1–C3, R1–R4): Decision 1 classifies every client→server type; Decision 2 has one intake for requests and connection-level messages, a stale check for U-U types only, and a body arena in place of fixed 64-byte entries; Decision 3 states the release-build registration behaviour. Two findings of the amendment itself: `PartyChatRequest` bodies reach 386 bytes, and `HeartbeatMessage` has no `SenderEntityID`.

Amended a second time 2026-10-09 after `architecture-review-2026-10-09-adr-014-rereview.md` (N1, S1–S4): the intake reports every message of a known connection to an `IConnectionActivitySink` (Decision 2); the highest-seen `SequenceNumber` is kept per U-U type per connection (Decision 2); the dispatcher story registers only the types that have a routing row (Decision 3); one object implements the intake and the dispatcher and takes the development-build switch in its constructor (Decision 3); `AddConnection` releases the storage of removed connections and returns `bool` (Decision 4).

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Networking (server). The dispatcher, the inbox and the tick pipeline are plain C#; the only engine-facing part is the transport adapter that feeds the inbox. |
| **Knowledge Risk** | LOW for Decisions 1 and 3–7 (no engine API). HIGH for the transport adapter of Decision 2: Netcode for GameObjects is past the LLM training cutoff, and `docs/engine-reference/unity/modules/networking.md` does not describe `CustomMessagingManager`, `FastBufferReader`, the player-loop stage in which messages are delivered, or transport size limits. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `modules/networking.md`, `breaking-changes.md` (NGO section), `deprecated-apis.md`. ADR-001, ADR-002, ADR-004, ADR-010, ADR-011, ADR-012; `architecture-review-2026-10-07.md` (P1). Code read: `src/ServerLogic/Networking/RpcGuards/` (`CrossCuttingRpcGuardChain`, `InboundRpcDescriptor`, `RpcTypeTag`, `RpcGuardResult`), `TickLoop/ServerTickLoop.cs`, `MutationGate/` (`ICharacterMutationGate`, `CharacterMutationGate`), `IrreversibleOutcome/IIrreversibleOutcomeCoordinator.cs`, `src/Foundation/Networking/WireProtocol/` (`ClientEntityMessageEnvelope`, `MessageRoutingRegistry`, `StaleDiscardComparer`). |
| **Post-Cutoff APIs Used** | None in the decision. The adapter will use whichever receive API ADR-004 OQ-ADR4-3 settles on; this ADR does not choose it. |
| **Verification Required** | All for the adapter story; none blocks the dispatcher. (1) Receive callbacks and the tick driver are different points of the same frame on the main thread, and no receive callback can run inside `ServerTickLoop.AdvanceTick`. The disconnect callback is the known exception: a server-initiated disconnect may raise it synchronously, inside the tick (Decision 4 is written for that). (2) The receive buffer is not valid after the callback returns; the adapter copies before returning, reads into the pre-allocated storage without allocating, and checks the length (10 to `MAX_INBOUND_MESSAGE_BYTES` bytes) before reading, so a short or oversize message is dropped and never throws. (3) The transport's connection id (`ulong` in NGO) maps one-to-one to the `uint` client id the guard chain uses: the adapter drops a message whose id exceeds `uint.MaxValue`, and ids are not reused within a process lifetime (a reconnect gets a new id). (4) The NGO package version that Unity 6.3 resolves, and that its custom-message receive API matches what the adapter uses. (5) For the reliable ordered delivery that R-OD types use, messages of one connection arrive in the order the client sent them, each exactly once. Nothing is assumed about U-U types. (6) Whether queued data of a connection can still be delivered after its disconnect event in the same frame (Decision 2 drops it either way). |
| **Specialist Validation** | `unity-specialist`, 2026-10-09: SOUND WITH NOTES, no blocking finding. It read this draft, the two engine reference files, and the guard chain, tick loop and gate code. The reference docs are silent on NGO messaging internals, so its statements about NGO behaviour are from memory; they are recorded here as items to verify, not as facts. Five findings changed the text: Pass A no longer depends on `OnGateOpened` (a throwing earlier subscriber would have stranded a hold queue); the guard chain needs three stated changes (Decision 4a); `RemoveConnection` is deferred (a disconnect can be raised inside the tick); guard rejection logging is throttled; each pipeline step is isolated. Its statements about the code were checked against the source by the author and hold. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-002 (simulation phases 1–4, which Decision 6 places in the tick), ADR-004 (project envelope over the transport; `clientId ↔ EntityID` mapping), ADR-010 (network callbacks enqueue, the tick executes), ADR-011 (completion queue, mutation gate, hold rule), ADR-012 (`IronGrind.ServerLogic`; handlers registered by the server composition root). All Accepted. |
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

**One `InboundRequestDispatcher` per zone process, in `IronGrind.ServerLogic`, is the only path from a client message to a game system. A transport adapter passes each message to one intake and does nothing else; the intake reports the connection's activity, copies requests into a bounded inbox and hands connection-level messages to the session layer. On the tick, the dispatcher first releases held requests whose gate is open, then takes the inbox in arrival order and, for each request, runs the guard chain, checks the mutation gate, and calls the one handler registered for that message type. A `ZoneTickPipeline` fixes the order of the tick.**

### Decision 1 — Scope: what goes through the dispatcher
- Every client→server message that reaches a game system is a *request* and goes through the dispatcher.
- Connection-level messages are consumed by the connection and session state machines. They pass through the same intake as requests (Decision 2), but they are not requests: they are not guarded by the session-ready gate (some arrive before `SessionReady`), are never held, and never enter the inbox.
- Server-originated work (loot pickup, auction delivery, AI) does not go through the dispatcher. ADR-011 already says what it does when the gate is closed.
- The table classifies every client→server type the GDDs define today. The story that adds a type adds its row.

| Message | Channel | Kind | Held | Rate-limited tag | Max body (bytes) |
|---|---|---|---|---|---|
| `MoveRequest` | R-OD | request | yes | — | 2 |
| `EquipRequest` (equip, and unequip with `itemId` 0) | R-OD | request | yes | — | 10 |
| `DiscardRequest` | R-OD | request | yes | — | 5 |
| `BuyRequest` | R-OD | request | yes | — | 9 |
| `SellRequest` | R-OD | request | yes | — | 10 |
| `UseItemRequest` | R-OD | request | yes | — | 9 |
| Accessory merge (`equipment-system.md` CR-EQS-14) | R-OD | request | yes | — | no wire schema yet |
| Respec Phase 1 (`leveling-system.md` CR-4.1) | R-OD | request | yes | — | no wire schema yet |
| `RespecPhase2Request` (`networking-core.md` CR-NET-5.6) | R-OD | request | no | — | no wire schema yet |
| `EnhancementAttemptRequest` | R-OD | request | no | — | 12 |
| `AllocateFreePointRequest` | R-OD | request | no | `AllocateFreePoint` | no wire schema yet |
| `SetTarget` | R-OD | request | no | — | 4 |
| `LootBidRequest` | R-OD | request | no | — | 8 |
| `PartyInviteRequest` | R-OD | request | no | — | 4 |
| `PartyInviteResponse` | R-OD | request | no | — | 5 |
| `PartyChatRequest` (`party-chat.md`) | R-OD | request | no | — | 386 |
| `GhostDismissRequest` | R-OD | request | no | — | 8 |
| `OpenNPCInteraction` | R-OD | request | no | — | 4 |
| `CloseNPCInteraction` | R-OD | request | no | — | 0 |
| `MovementIntentMessage` | U-U | request | no | — | 10 |
| `SkillCastRequest` | U-U | request | no | `NotifySkillUsed` | 16 |
| `SessionHandshake` | R-OD | connection-level | — | — | schema pending (OQ-NC-SER-2) |
| `HeartbeatMessage` | U-U | connection-level | — | — | 0 |
| `RttProbeEcho` | U-U | connection-level | — | — | 0 |
| `ClientBackgrounded` / `ClientForegrounded` | R-OD | connection-level | — | — | 0 |
| `ZoneSnapshotRequest` | R-OD | connection-level | — | — | 8 |

Notes on the table:
- **Held = yes** is every request that can change a character's bag, equipment or gold (ADR-011 Decision 4). Respec Phase 1 is one of them: it moves the scroll out of the active inventory into a reserved slot.
- **`EnhancementAttemptRequest` and `RespecPhase2Request` are not held.** Each starts an irreversible outcome of its own. While the character's gate is closed, `EnhancementService` rejects the first as a concurrent attempt, and `IrreversibleOutcomeCoordinator.Begin` returns `RejectedWriteInFlight` for the second; the handler's system sends the rejection. This is intended (user decision 2026-10-09): an irreversible outcome is never queued behind another one.
- **`AllocateFreePointRequest`** is rate-limited, so it cannot be a held type (Decision 3). It does not touch the bag.
- **`ZoneSnapshotRequest`** is connection-level: it belongs to zone entry, can arrive before the client is session-ready, and carries its own per-connection limit (`zone-instancing.md` CR-ZI-9).
- **`HeartbeatMessage`** has the 10-byte envelope only, with no `SenderEntityID`. `SessionHandshake` is sent before the client has an entity; its schema is pending. Every other type in the table carries `SenderEntityID`.
- **`RttProbeEcho`**: `networking-wire-protocol.md` gives it no body; `networking-channel-contract.md` (line 124) gives it a `probeSequence` field. The body bound follows whichever the GDDs settle on.

### Decision 2 — Intake: the adapter and the inbox
- The adapter is the only code that touches the transport's receive API. In its callback it checks that the message is 10 to `MAX_INBOUND_MESSAGE_BYTES` (default 400) bytes long and calls `IInboundMessageIntake.TryAccept(clientId, message)`. It decodes nothing and calls no game system, no guard and no handler.
- The intake is the one component that separates requests from connection-level messages. It holds the sealed type table of Decision 3 and, per connection, the highest `SequenceNumber` seen on each U-U type. `TryAccept` runs on the main thread, outside the tick. It:
  1. drops a message from a connection that was never added or has been removed (`AddConnection` / `RemoveConnection`, Decision 4). Every other message is reported here, once, to `IConnectionActivitySink.OnInboundActivity(clientId, IServerTickSource.ServerTickNumber)`, before anything is decoded: a message dropped in step 2, 3, 4 or 6 is still a packet from a live client, and any packet resets the heartbeat timeout (`networking-wire-protocol.md` CR-NET-7.10);
  2. decodes the 10-byte CR-NET-7.1 envelope and looks `MessageTypeId` up in the type table; a type with no entry is dropped — anomaly `UnknownInboundMessageType`;
  3. for a type that carries `SenderEntityID`, reads the 4 bytes that follow; a message too short for its type, or with a body longer than the type's `MaxBodyBytes`, is dropped — anomaly `InboundMessageMalformed`;
  4. for a U-U type only, drops a message whose `SequenceNumber` is stale against the highest seen for that type on that connection (CR-NET-7.5, `StaleDiscardComparer`); the first message of a type on a connection is never stale. This is ordinary on an unreliable channel: counted, not logged as an anomaly. The value is kept per type (user decision 2026-10-09): with one value per connection, a `SkillCastRequest` overtaken by the `MovementIntentMessage` sent after it would be dropped with no reply. R-OD types are not checked: the transport delivers each of them once and in order (Verification Required 5), and `SequenceNumber` is one counter per connection shared by all types (`networking-channel-contract.md` CCR-1), so a U-U packet that overtakes a retransmitted R-OD request would otherwise make that request look stale;
  5. hands a connection-level message to the `IConnectionMessageSink` registered for its type, and stops. The sink runs inside the receive callback, so it only records (ADR-010 Decision 5): it resets a timeout, stores an echo time, or queues work for the session state machine;
  6. drops a request if that connection already has `MAX_INBOX_REQUESTS_PER_CONNECTION` (default 64) undispatched requests, or if its body does not fit in what is left of the connection's `MAX_INBOX_BYTES_PER_CONNECTION` (default 4,096) for this tick interval — anomaly `InboundInboxOverflow`, logged once per connection per tick;
  7. otherwise copies the body into the connection's segment of the inbox arena and records `(arrivalIndex, clientId, envelope fields, senderEntityId, arrivalTick, body offset, body length)`.
- The highest seen `SequenceNumber` of a U-U type on a connection advances only for a message of that type that reaches step 5 or is accepted in step 7.
- The activity sink runs inside the receive callback, so it only records, like a connection-level sink. The session layer implements it: it maps the connection to its account and calls `ConnectionStateMachine.RecordInboundActivity`, which does nothing for an account that is not `Connected`. Not reported: a message from a connection that is unknown or removed, and a message the adapter dropped for its length — the adapter still does nothing but the length check.
- `arrivalIndex` is one zone-wide counter: the order in which the server received the requests, across all connections and all request types. For the R-OD types of one connection, receive order is the client's send order (Verification Required 5). U-U requests (`MovementIntentMessage`, `SkillCastRequest`) can be lost or arrive late, and their order relative to the R-OD requests of the same connection is not guaranteed; no held type is U-U. Between connections it is receive order, which is the only order the server can know.
- `arrivalTick` is `IServerTickSource.ServerTickNumber` when `TryAccept` ran. `ServerTickLoop` implements the interface with the property it already has.
- The inbox storage is allocated when the zone is created: a record array of `MAX_PLAYERS_PER_ZONE × MAX_INBOX_REQUESTS_PER_CONNECTION` entries (3,200), and a byte arena with one `MAX_INBOX_BYTES_PER_CONNECTION` segment per connection slot (50 × 4,096 B = 205 KB), filled from its start and reset by each tick's dispatch. Each connection slot also has one highest-seen `SequenceNumber` per registered U-U type (four types today), cleared when the slot is given to a new connection. `TryAccept` allocates nothing.
- The inbox is emptied completely by every tick's dispatch (Decision 4), so no backlog carries from one tick to the next. The per-connection bounds limit what one client can add between two ticks.
- Guards do not run at intake. Guard state is read at one fixed point of the tick (Decision 4).

### Decision 3 — Registration: one descriptor and one handler per message type
- The server composition root registers, for each request type, an `InboundRequestDescriptor` and a handler, and for each connection-level type a `ConnectionMessageDescriptor` and an `IConnectionMessageSink`. Then it calls `Seal()`. Registration after `Seal()` throws. The two kinds form one type table, which the intake reads (Decision 2).
- `InboundRequestDispatcher` implements both `IInboundRequestDispatcher` and `IInboundMessageIntake`: the type table, the connection set and the inbox are fields of one object, and the adapter receives it as `IInboundMessageIntake` only. Its constructor takes `bool isDevelopmentBuild`; the composition root passes `BuildConfiguration.IsDevelopmentBuild`.
- The request descriptor carries: `MessageTypeId`; `RpcTypeTag` (the rate-limit bucket the guard chain already uses; the enum gains a member per request type, most with no limit); `HeldDuringIrreversibleWrite`; `MaxBodyBytes`. The connection-level descriptor carries `MessageTypeId`, `MaxBodyBytes` and `CarriesSenderEntityId`. Every request carries `SenderEntityID`.
- The channel of a type (R-OD or U-U) is not in either descriptor: registration reads it from the type's `MessageRoutingRegistry` entry, so it cannot disagree with the routing table.
- `HeldDuringIrreversibleWrite` is set per type as the table of Decision 1 lists.
- A held-type request must not carry a rate-limited `RpcTypeTag`: the guard spends the rate-limit slot when it accepts, and a request then dropped by a full hold queue would have spent it for nothing. No bag-mutating request is rate-limited today.
- Registration fails at startup, not at runtime, when: the type is registered twice (as a request, as a connection-level type, or as both); the handler or the sink is null; `MaxBodyBytes` exceeds `MAX_INBOUND_MESSAGE_BYTES` less the envelope; a held type is U-U, carries a rate-limited tag, or has `MaxBodyBytes` above `MAX_HELD_BODY_BYTES` (64).
- A type with no `MessageRoutingRegistry` entry of direction client→server follows `networking-message-criticality.md` AC-MCR-03, with the same development-build switch `MessageRoutingRegistry.ValidateAndRoute` takes. In a development build, registration throws `PendingSchemaDispatchException` at startup. In a release build the registration is skipped and logged as a server error; the type is then absent from the table and every message of it is dropped at intake as `UnknownInboundMessageType`. No crash in either build. `MessageRoutingRegistry` has two client→server rows today (`HeartbeatMessage`, `SetTarget`). The dispatcher story registers the types that have a routing row and a message type in code; the story that adds a client→server type adds its routing row, its row in the table of Decision 1 and its registration together.
- A handler is `void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)`. It decodes the body with the message's codec and calls its system. It runs on the tick thread. It cannot keep `body` after it returns (a span cannot be stored in a field), must not `await` (ADR-011), and must not read `ICharacterMutationGate`.
- Request deduplication stays in the handler's system (ADR-001 for shop requests, `EnhancementRequestDeduplicator` for enhancement). The dispatcher never reads a `requestId`. A retransmit that arrives while the original is held is held too; on release the original executes and the retransmit receives the cached result.

### Decision 4 — Dispatch: what happens to one request on the tick
`InboundRequestDispatcher.DispatchTick(currentTick)` runs once per tick, after `ITickCompletionQueue.Drain` (Decision 6). It first releases what connections removed since the previous call still hold — their inbox records and arena segment, and their hold-queue entries, which go back to the pool — then runs two passes.

**Pass A — release.** The dispatcher keeps the list of characters that have a non-empty hold queue, in the order each one's first request was held. For each of them whose gate is not held (`!gate.IsHeld(charId)`), the held requests are dispatched in arrival order. The dispatcher does not subscribe to `ICharacterMutationGate.OnGateOpened`: polling the gate cannot miss an opening (an event subscriber that throws stops the subscribers after it), and it also serves a gate opened outside `Drain`. Before each released request the dispatcher checks that the connection has not been removed, still owns the entity and is still session-ready (`IsLiveOwner`, Decision 4a); a request that fails is discarded. This is how held requests are discarded after a failed write: the failure protocol has disconnected the client by then. Released requests are not rate-limited again. If a released request closes the gate, the remaining ones stay held.

**Pass B — new requests.** The inbox is taken in `arrivalIndex` order. Entries of a connection that is removed, or no longer known because an `AddConnection` released its storage, are skipped. For each request:
1. **Guard chain.** `CrossCuttingRpcGuardChain.Evaluate(new InboundRpcDescriptor(clientId, senderEntityId, descriptor.RpcTypeTag, currentTick))`. Any result other than `Accepted` drops the request. Nothing rejected by a guard is ever held.
2. **Character.** The dispatcher resolves the connection's `CharacterID` through `IConnectionCharacterDirectory`. No character → dropped, anomaly `InboundRequestWithoutCharacter`.
3. **Gate.** If `descriptor.HeldDuringIrreversibleWrite` and (`gate.IsHeld(charId)` or that character still has held requests), the body is copied to that character's hold queue. A full hold queue (`MAX_HELD_REQUESTS_PER_CHARACTER` = 16, ADR-011) drops the request — anomaly `HeldRequestOverflow`.
4. **Handler.** Otherwise the handler is called.

Rules that follow:
- **Isolation.** Steps 1–4 of one request in Pass B, and the `IsLiveOwner` check and handler call of one released request in Pass A, run inside one `try`/`catch`: an exception from the guard, the directory, the gate or the handler is logged as a server error with the message type and client id, that request is dropped (in Pass A its hold-queue entry is returned to the pool), and the pass continues — the policy of `TickCompletionQueue` callbacks.
- A handler may close the gate (by starting an irreversible outcome). Later held-type requests of that character in the same pass are then held.
- **Connections.** `AddConnection(clientId)` is called when a connection is established. Called outside a pass, it first releases the storage of the connections removed since the last `DispatchTick`, as the start of `DispatchTick` does, so a slot freed by a disconnect can be taken in the same tick interval; called inside a pass, it releases nothing. It returns false and logs a server error if no slot is free: after the release that means `MAX_PLAYERS_PER_ZONE` live connections, which the capacity check of `zone-instancing.md` rules out, so it is a caller error (user decision 2026-10-09). `RemoveConnection(clientId)` — called on disconnect and on zone transfer — only marks the connection removed. It may be called from inside a pass (a handler or a completion callback that disconnects a client can raise the transport's disconnect callback synchronously), so it frees nothing: the passes skip that connection's inbox and hold-queue entries, and their storage is released at the start of the next `DispatchTick`, or by an `AddConnection` that comes before it.
- The dispatcher sends nothing to a client. A dropped or discarded request gets no reply; replies are the handler's system's business.

### Decision 4a — Changes to `CrossCuttingRpcGuardChain`
The guard chain was written before any request type other than three existed. Three changes, made by the dispatcher story:
1. **Tags with no rate limit.** `GetRequiredTickGap` throws for a tag it does not list, by design: a new tag must not bypass rate limiting silently. That intent is kept, but moved from the running server to the test suite: every `RpcTypeTag` member is listed in the switch with its gap (0 for no limit), and a test asserts that every member of the enum returns without throwing. `Evaluate` skips the last-accepted-tick read and write when the gap is 0.
2. **A read-only liveness query.** `bool IsLiveOwner(uint clientId, EntityID entityId)`: the entity is registered, owned by that client, and the client is session-ready. It writes nothing, so Pass A can re-check a held request without touching the rate-limit state.
3. **Throttled rejection logs.** `Evaluate` logs every rejection with an interpolated string, which allocates and captures a stack trace. At the inbox bound that is up to 64 log calls per client per tick. The first rejection per (client, result) per tick is logged in full; the rest are counted and reported in one line per tick.

### Decision 5 — Hold queue storage
Each character with held requests has a hold queue of at most 16 entries, each a copy of the request context and body, taken from a pre-allocated pool sized for `MAX_PLAYERS_PER_ZONE × 16` entries of `MAX_HELD_BODY_BYTES` (64). Registration guarantees that no held type has a longer body (Decision 3); the longest today is 10 bytes. The copy is needed because the inbox arena is reused by the next tick. No allocation when a request is held. An entry returns to the pool when its request is dispatched or discarded, or, if its connection was removed, at the start of the next `DispatchTick` or at an `AddConnection` that comes before it.

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
  └ adapter: length check only                 1. ITickCompletionQueue.Drain ── callbacks ── gate.Open
      └ IInboundMessageIntake.TryAccept        2. InboundRequestDispatcher.DispatchTick
          connection? → activity sink; envelope   free storage of removed connections
          unknown type? malformed?                Pass A: characters with held requests, gate not held
          U-U only: stale?                          └ IsLiveOwner → handler      (arrival order)
          connection-level → sink (stop)          Pass B: inbox, arrivalIndex order
          inbox full? copy body, arrivalIndex++     guard chain → character → gate ─┬─ held → hold queue
                    └────────────────────────────►                                  └─ handler → system
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
        public readonly ushort MaxBodyBytes;
    }

    public readonly struct ConnectionMessageDescriptor
    {
        public readonly ushort MessageTypeId;
        public readonly ushort MaxBodyBytes;
        public readonly bool CarriesSenderEntityId;   // false: HeartbeatMessage, SessionHandshake
    }

    public readonly struct InboundRequestContext
    {
        public readonly uint ClientId;
        public readonly EntityID SenderEntityId;
        public readonly CharacterID CharacterId;
        public readonly ushort MessageTypeId;
        public readonly uint ArrivalTick;    // IServerTickSource.ServerTickNumber when TryAccept ran
        public readonly uint DispatchTick;   // tick on which the handler runs
        public readonly bool WasHeld;
    }

    // A custom delegate: ReadOnlySpan<byte> cannot be a generic argument of Action<>.
    public delegate void InboundRequestHandler(in InboundRequestContext context, ReadOnlySpan<byte> body);

    public interface IInboundMessageIntake
    {
        // Main thread, outside the tick. message = envelope + optional SenderEntityID + body, as received.
        // Copies what it keeps. False = dropped. Never throws.
        bool TryAccept(uint clientId, ReadOnlySpan<byte> message);
    }

    public interface IConnectionMessageSink
    {
        // Called from TryAccept, inside the receive callback. Records only; body is not valid after return.
        // senderEntityId is EntityID.Invalid for a type that does not carry it.
        void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body);
    }

    public interface IConnectionActivitySink
    {
        // Called from TryAccept, inside the receive callback, once per message of a known connection,
        // before the message is decoded. Records only. Implemented by the session layer.
        void OnInboundActivity(uint clientId, uint serverTick);
    }

    public interface IServerTickSource
    {
        uint ServerTickNumber { get; }           // implemented by ServerTickLoop
    }

    // InboundRequestDispatcher implements this interface and IInboundMessageIntake (Decision 3).
    // Its constructor takes the IConnectionActivitySink and bool isDevelopmentBuild.
    public interface IInboundRequestDispatcher
    {
        void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler);
        void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink);
        void Seal();
        bool AddConnection(uint clientId);       // releases removed connections first; false = no free slot
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
Constants: `MAX_INBOX_REQUESTS_PER_CONNECTION = 64`, `MAX_INBOX_BYTES_PER_CONNECTION = 4096`, `MAX_INBOUND_MESSAGE_BYTES = 400` (14-byte envelope with `SenderEntityID` plus the 386-byte `PartyChatRequest` body, the largest client→server message), `MAX_HELD_BODY_BYTES = 64`, `MAX_HELD_REQUESTS_PER_CHARACTER = 16` (ADR-011, unchanged). `MAX_INBOUND_BODY_BYTES` of the first version is replaced by the per-type `MaxBodyBytes`. `ArrivalTick` and `DispatchTick` are `ServerTickLoop.ServerTickNumber` values, never the transport's own tick counter.

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
- New components: the intake with its inbox, the dispatcher with its hold queues, `ZoneTickPipeline`, `IConnectionCharacterDirectory`, `IConnectionMessageSink`, `IConnectionActivitySink`, `IServerTickSource`, the adapter.
- The classification table of Decision 1 must be kept in step with the wire protocol; four of its request types have no wire schema yet.

### Risks
- **A request is dropped silently when the inbox or a hold queue is full.** The client gets no reply. Mitigation: the bounds are far above legitimate use (64 requests and 4,096 body bytes per connection per 50 ms tick — ten chat messages of maximum length; 16 held during a write of one to two ticks); every drop is an anomaly log; the bounds are tuning knobs.
- **An R-OD request and a U-U request of one connection can be dispatched in an order other than the one the client sent them in.** Mitigation: no held type is U-U, and the two U-U requests (movement, skill cast) do not depend on the bag requests around them; the stale check keeps each U-U type's own stream moving forward.
- **Flood cost.** A client at the inbox bound costs 64 guard evaluations per tick; 50 such clients, 3,200. Mitigation: the evaluations do not allocate; rejection logging is throttled (Decision 4a).
- **The inbox bound is per tick interval, not per frame.** If the server runs frames faster than ticks, or a tick is late, messages of several frames land in one inbox. The bound still holds; a slow tick makes a legitimate client more likely to reach it. Mitigation: the bound is 64; the tick driver and the server frame rate are fixed by the story that drives `AdvanceTick` from the player loop.
- **`IConnectionCharacterDirectory` has no implementation.** The guard chain maps `clientId ↔ EntityID`; nothing maps a connection to its `CharacterID` yet. Mitigation: the session layer that sends `SessionReady` (which carries both ids) implements it; the dispatcher story uses a fake.
- **Phase 3 order between systems is not decided here.** Mitigation: the composition root lists it explicitly; it is decided when the second ticking system exists.
- **The adapter's assumptions about the transport are unverified** (Verification Required 1–6). Mitigation: the adapter story verifies them; the dispatcher is written so that none of them, if false, corrupts state (it copies always, tolerates a disconnect inside the tick, and drops messages of unknown connections).
- **Connection-level messages are outside the dispatcher**, so two consumers read inbound messages. Mitigation: both are fed by the one intake, from one sealed type table; Decision 1 lists which types are which.
- **A connection-level sink and the activity sink run inside the receive callback.** Mitigation: they only record (Decision 2); the session story's review checks that no sink calls game logic.
- **A client that sends only malformed or unknown messages keeps its session alive.** Activity is reported before the message is decoded. Mitigation: each such message is an anomaly log; what to do with a connection that produces them is the session layer's decision, not the intake's.
- **Outbound writers may still hold messages for a removed connection.** Not this ADR's component. Mitigation: the adapter story states that a send to a removed connection is dropped.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| enhancement-system.md | CR-ENH-18, AC-ENH-38 — other bag-mutating requests are held during an attempt and processed in arrival order afterwards | Decision 4: gate check per request, hold queue, Pass A before new requests. After a failed write the held requests are discarded (ADR-011 Decision 4); AC-ENH-38 says they are processed, and is corrected after acceptance (Migration Plan step 3) |
| networking-core.md | Cross-Cutting Constraints 1–3 — unknown entity, session-ready gate, rate limits; rejected requests dropped, not queued | Decision 4 step 1: the existing guard chain runs for every request, before the gate |
| networking-core.md | CR-NET-2 — fixed 20 Hz tick; game logic on the tick | Decision 6: one pipeline, four steps |
| networking-wire-protocol.md | CR-NET-7.1 client→server envelope; CR-NET-7.5 stale discard; pre-allocated buffers | Decision 2: envelope decoded by the intake, stale check at intake for U-U types, per type and connection, inbox storage allocated per zone |
| networking-wire-protocol.md | CR-NET-7.10 — any packet from the client resets the heartbeat timeout; the client skips its heartbeat while it sends other messages | Decision 2 step 1: every message of a known connection is reported to `IConnectionActivitySink`, including the ones dropped later |
| networking-channel-contract.md | CCR-1 — one `SequenceNumber` counter per connection, shared by all types | Decision 2 step 4: the stale check is limited to U-U types, so the shared counter cannot make an R-OD request look stale |
| networking-message-criticality.md | AC-MCR-03 — a type with no routing row raises `PendingSchemaDispatch` in debug builds and is logged in release builds; no crash in either | Decision 3: development build throws at startup; release build skips the registration and logs, and the type is dropped at intake |
| party-chat.md | `PartyChatRequest` — body up to 386 bytes | Decisions 2 and 3: per-type `MaxBodyBytes`, a byte arena per connection |
| npc-shop.md / ADR-001 | Shop requests deduplicated on `(charId, messageType, requestId)` | Decision 3: dedup stays in the handler's system, after release |
| zone-instancing.md | Runtime Model — single-threaded main loop | Decisions 2 and 4: intake and dispatch both on the main thread, at defined points |

## Performance Implications
- **CPU**: per request, one guard evaluation (dictionary lookups, no allocation), one directory lookup, one gate lookup, one delegate call. Pass A scans at most 50 characters. Worst case per tick 50 × 64 = 3,200 requests if every client floods to the bound; expected load is a few requests per client per second. At intake, one activity-sink call per message.
- **Memory**: inbox arena 205 KB, inbox records 3,200 × about 32 B ≈ 100 KB, and hold pool 50 × 16 × (64 B + context) ≈ 70 KB per zone, allocated once.
- **Load Time**: none.
- **Network**: none. The dispatcher sends nothing.

## Migration Plan
Nothing dispatches client requests today and nothing calls `RegisterTickDriven`, so no behaviour a player can observe changes. One existing class changes: the guard chain (Decision 4a).

1. **Dispatcher story (Networking Core).** `InboundRequestDescriptor`, `ConnectionMessageDescriptor`, `InboundRequestContext`, the intake with its inbox, `IConnectionMessageSink`, `IConnectionActivitySink` (interface and test fake; the session layer's implementation is not part of this story), `IServerTickSource` (implemented by `ServerTickLoop`), the dispatcher with hold queues, `IConnectionCharacterDirectory` (interface and test fake), and the three guard-chain changes of Decision 4a with their tests (every `RpcTypeTag` member returns a gap; `IsLiveOwner` writes nothing; throttled logs). Of the table of Decision 1, it registers the types that have a routing row and a message type in code (Decision 3). EditMode tests: arrival order across types and connections; an R-OD request with a lower `SequenceNumber` than a U-U message already received is accepted; a stale U-U message is dropped; a `SkillCastRequest` with a lower `SequenceNumber` than a `MovementIntentMessage` already received is accepted; a request, a stale U-U message, a malformed message and a request dropped by a full inbox each report activity once, and a message from an unknown connection reports none; `AddConnection` after a `RemoveConnection`, with every slot taken and before the next `DispatchTick`, succeeds, and the removed connection's inbox records are not dispatched; `AddConnection` with `MAX_PLAYERS_PER_ZONE` live connections returns false; a 10-byte `HeartbeatMessage` reaches its sink and never the inbox; a body over the type's `MaxBodyBytes` and a message too short for its type are dropped; each guard rejection drops and never holds; hold while the gate is closed, release order, release before new requests; release when a *different* `OnGateOpened` subscriber throws; discard on release when the connection is gone, with the hold entries back in the pool; `RemoveConnection` called from inside a handler; overflow of inbox (count and bytes) and hold queue; registration failures, and the release-build skip for a type with no routing row; a throwing handler, guard or directory does not stop either pass.
2. **Tick pipeline story (Networking Core).** `ZoneTickPipeline` with the four isolated steps; tests assert the order with recording fakes, and that a throwing step does not skip the later ones.
3. **Enhancement Story 009.** Unblocked after step 1: its acceptance criteria are tested against the dispatcher with a fake persistence task. Its first criterion lists "pickup" among held requests; under ADR-011 a server pickup is deferred, not held — corrected at its `/story-readiness`. `enhancement-system.md` AC-ENH-38 (lines 698–701) is corrected with it, as a wording fix with no status change (user decision 2026-10-09): after the failed write the held unequip is discarded with the disconnected client and the Helmet is still equipped; a second case covers a successful write, where the held unequip runs after the outcome is delivered.
4. **Adapter story.** After ADR-004 OQ-ADR4-3. Carries Verification Required 1–6.
5. **Documents.** A dated note on ADR-010 Decision 5; `/create-control-manifest update`; `architecture-traceability.md` P1; registry entries as approved by the user.

## Validation Criteria
- The dispatcher tests of Migration Plan step 1 pass with no network session.
- AC-ENH-38, as corrected (Migration Plan step 3), passes against the dispatcher in both cases. Failed write: the unequip sent while the write is in flight is never processed — the client is disconnected, the request is discarded in Pass A, and the Helmet is still equipped. Successful write: the unequip runs in Pass A of the tick whose `Drain` delivered the outcome, before any new request of that tick.
- A reliable request that arrives after a U-U message with a higher `SequenceNumber` from the same connection is dispatched.
- A `SkillCastRequest` that arrives after a `MovementIntentMessage` with a higher `SequenceNumber` from the same connection is dispatched.
- A `MovementIntentMessage` reports activity to the `IConnectionActivitySink`; so does a message of a known connection that the intake then drops.
- A `HeartbeatMessage` and a 386-byte `PartyChatRequest` are both accepted at intake.
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
