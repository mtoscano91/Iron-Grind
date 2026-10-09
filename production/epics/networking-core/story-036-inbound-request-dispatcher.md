# Story 036: Inbound Request Dispatcher — Intake, Dispatch and Hold Queues

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 10 hours

*Added 2026-10-09. Second of two stories for ADR-014 Migration Plan step 1 (split by user decision 2026-10-09; Story 035 changes the guard chain). This story builds the one path from a client message to a game system: the intake with its inbox, the dispatcher with its two passes, and the hold queues.*

## Context

**GDD**: `design/gdd/networking-core.md` — Cross-Cutting Constraints 1–3 (a request rejected by a guard is dropped, never queued), CR-NET-2 (game logic runs on the tick). `design/gdd/networking-wire-protocol.md` — CR-NET-7.1 (client→server envelope), CR-NET-7.5 (stale discard), CR-NET-7.10 (any packet resets the heartbeat timeout), buffer allocation rule. `design/gdd/networking-channel-contract.md` — CCR-1 (one `SequenceNumber` counter per connection). `design/gdd/networking-message-criticality.md` — AC-MCR-03 (a type with no routing row). `design/gdd/enhancement-system.md` — CR-ENH-18, AC-ENH-38 (bag requests held during an irreversible write; tested end to end by Enhancement Story 009, not here).
**Requirement**: none in the epic's TR table — this story builds a component decided by ADR-014
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09) — Decisions 1–5 and Key Interfaces. Uses ADR-011 Decision 4 (mutation gate, hold rule) and ADR-010 Decision 5 (a network callback enqueues, the tick executes).
**ADR Decision Summary**: one `InboundRequestDispatcher` per zone process is both the intake and the dispatcher. Outside the tick, `TryAccept` reports the connection's activity, decodes the envelope, drops what is unknown, malformed or stale, hands connection-level messages to a sink, and copies requests into a bounded inbox. On the tick, `DispatchTick` releases the held requests of characters whose gate is open (Pass A), then takes the inbox in arrival order and runs guard chain → character → gate → handler for each request (Pass B).

**Engine**: Unity 6.3 LTS | **Risk**: LOW for this story
**Engine Notes**: Plain C# (`Span<byte>`, `BinaryPrimitives`); the only Unity API is `Debug.LogWarning` / `Debug.LogError`. No transport type appears in this story: the adapter that calls `TryAccept` is a later story and carries ADR-014's Verification Required 1–6.
**Performance**: No allocation per message in `TryAccept` or per request in `DispatchTick`; all storage is created in the constructor and at `Seal()`. Per zone: inbox arena 205 KB (50 × 4,096 B), inbox records about 100 KB (3,200 entries), hold pool about 70 KB (800 entries of 64 B plus context). Per request: one guard evaluation, one directory lookup, one gate lookup, one delegate call. Pass A scans at most 50 characters. Worst case 3,200 requests per tick.

**Control Manifest Rules (Foundation layer — "Inbound request dispatch and tick order (ADR-014)")**:
- Required: `TryAccept` runs outside the tick, copies what it keeps, allocates nothing and never throws; its seven steps run in the order of ADR-014 Decision 2
- Required: inbox storage is allocated when the zone is created and emptied completely by every tick's dispatch
- Required: `Register` / `RegisterConnectionLevel`, then `Seal()`; registration after `Seal()` throws; the channel of a type is read from its `MessageRoutingRegistry` entry
- Required: Pass A polls `!gate.IsHeld(charId)` and checks `IsLiveOwner` before each released request; Pass B runs guard chain → character → gate → handler; each request is isolated in one `try`/`catch`
- Required: `RemoveConnection` only marks; storage is released at the start of the next `DispatchTick` or by an earlier `AddConnection`
- Forbidden: running the guard chain at receipt (in `TryAccept`)
- Forbidden: holding a request that a guard rejected
- Forbidden: deduplicating in the dispatcher or reading a `requestId` there
- Forbidden: subscribing the dispatcher to `ICharacterMutationGate.OnGateOpened`
- Forbidden: applying the stale-sequence check to an R-OD type, or keeping one highest-seen `SequenceNumber` per connection
- Forbidden: a transport type in the dispatcher; a per-tick dispatch budget with a carried backlog
- Forbidden: allocating per request on the intake or dispatch path, or renting its buffers from `ArrayPool<T>.Shared`
- Forbidden: `await` in tick-driven code (ADR-011)
- Guardrail: `MAX_INBOX_REQUESTS_PER_CONNECTION = 64`, `MAX_INBOX_BYTES_PER_CONNECTION = 4096`, `MAX_INBOUND_MESSAGE_BYTES = 400`, `MAX_HELD_BODY_BYTES = 64`, `MAX_HELD_REQUESTS_PER_CHARACTER = 16`

---

## Acceptance Criteria

*From ADR-014 Decisions 1–5, Migration Plan step 1 and Validation Criteria, scoped to this story:*

**Registration (Decision 3)**

- [x] **Register, then seal**: `Register` and `RegisterConnectionLevel` build one type table; `Seal()` closes it. Either registration after `Seal()` throws `InvalidOperationException`. `TryAccept` before `Seal()` returns false.
- [x] **Startup failures**: registration throws when the type is already registered (as a request, as a connection-level type, or one of each); when the handler or the sink is null; when `MaxBodyBytes` is above `MAX_INBOUND_MESSAGE_BYTES` less the envelope the type uses (14 bytes with `SenderEntityID`, 10 without); when a held type is U-U, carries a tag whose rate-limit gap is above 0, or has `MaxBodyBytes` above `MAX_HELD_BODY_BYTES`; when a request's `RpcTypeTag` is not a member of the enum (`ArgumentOutOfRangeException`; user decision 2026-10-09).
- [x] **Channel from the routing table**: the channel of a type is read from its routing entry at registration; neither descriptor has a channel field.
- [x] **No routing row (AC-MCR-03)**: for a type with no routing entry, or whose entry is not client→server — with `isDevelopmentBuild` true, registration throws `PendingSchemaDispatchException`; with false, the registration is skipped, a server error is logged, and every message of that type is later dropped at intake as `UnknownInboundMessageType`. No exception in the release case.

**Connections (Decision 4)**

- [x] **Known connections only**: `TryAccept` returns false, and reports no activity, for a connection that was never added or has been removed.
- [x] **Remove only marks**: `RemoveConnection` frees nothing and may be called from inside a handler. The passes skip that connection's inbox records and discard its held requests; its storage is released at the start of the next `DispatchTick`, or by an `AddConnection` called outside a pass before it.
- [x] **Slot reuse in the same tick interval**: with all `MAX_PLAYERS_PER_ZONE` slots taken, `RemoveConnection(a)` followed by `AddConnection(b)` before the next `DispatchTick` returns true; the requests `a` had in the inbox are never dispatched.
- [x] **No free slot**: `AddConnection` with `MAX_PLAYERS_PER_ZONE` live connections returns false and logs a server error. Adding a connection that is already live returns false and logs a server error. *(The second case is not in ADR-014; user decision 2026-10-09.)*

**Intake (Decision 2)**

- [x] **Activity**: every message of a known connection is reported exactly once to `IConnectionActivitySink.OnInboundActivity(clientId, IServerTickSource.ServerTickNumber)`, before the envelope is decoded. This holds for an accepted request, a connection-level message, and a message then dropped as unknown, malformed, stale or overflowing.
- [x] **Unknown type**: a `MessageTypeId` with no entry in the type table is dropped — anomaly `UnknownInboundMessageType`.
- [x] **Malformed**: a message shorter than the envelope its type uses, or with a body longer than the type's `MaxBodyBytes`, is dropped — anomaly `InboundMessageMalformed`.
- [x] **Stale check, U-U only, per type**: a U-U message is dropped when its `SequenceNumber` is not newer than the highest seen for that type on that connection (`StaleDiscardComparer`); the first message of a type on a connection is never stale. A stale drop is counted in `StaleDropCount` and not logged. The highest-seen value of a type advances only for a message of that type that reached its sink or was accepted into the inbox.
- [x] **R-OD is never stale**: an R-OD request whose `SequenceNumber` is lower than that of a U-U message already received from the same connection is accepted and dispatched.
- [x] **One U-U type does not affect another**: a U-U request whose `SequenceNumber` is lower than that of a message of a different U-U type already received from the same connection is accepted and dispatched.
- [x] **Connection-level messages**: a connection-level message reaches the `IConnectionMessageSink` registered for its type, inside `TryAccept`, and never enters the inbox. A 10-byte `HeartbeatMessage` reaches its sink with `EntityID.Invalid` and an empty body.
- [x] **Inbox bounds**: a request is dropped when its connection already has `MAX_INBOX_REQUESTS_PER_CONNECTION` undispatched requests, or when its body does not fit in what is left of the connection's `MAX_INBOX_BYTES_PER_CONNECTION` — anomaly `InboundInboxOverflow`. One connection reaching a bound does not affect another connection.
- [x] **Largest message**: a 400-byte message with a 386-byte body, of a type registered with `MaxBodyBytes` 386, is accepted and its handler receives the same 386 bytes.
- [x] **Anomaly logs are bounded**: each of `UnknownInboundMessageType`, `InboundMessageMalformed` and `InboundInboxOverflow` is logged at most once per connection per tick interval; further ones are counted and not logged. *(ADR-014 states this for `InboundInboxOverflow` only; extended to the other two by user decision 2026-10-09, because a formatted log per dropped message would allocate inside `TryAccept` for as many messages as a client cares to send.)*
- [x] **Never throws**: `TryAccept` returns false, and does not throw, for an empty span, a 9-byte span, and a span of any content.

**Dispatch — Pass B (Decision 4)**

- [x] **Arrival order**: requests accepted from several connections and of several types are dispatched in the order `TryAccept` accepted them.
- [x] **Context**: the handler receives `ClientId`, `SenderEntityId`, `CharacterId`, `MessageTypeId`, `ArrivalTick` (the tick source's value when `TryAccept` ran), `DispatchTick` (the argument of `DispatchTick`), `WasHeld` false, and a body equal to the bytes after the 14-byte envelope.
- [x] **Guard rejection drops**: for each of the four rejecting `RpcGuardResult` values, the request is dropped: the handler is not called and `HeldCount` stays 0, also for a held type whose character's gate is closed.
- [x] **No character**: a request that passes the guards but whose connection has no character in `IConnectionCharacterDirectory` is dropped — anomaly `InboundRequestWithoutCharacter`.
- [x] **Inbox emptied every tick**: after `DispatchTick` no request is undispatched; a second `DispatchTick` with no new message calls no handler; a connection that reached a bound can send again.
- [x] **Guard log summary**: `DispatchTick` calls `CrossCuttingRpcGuardChain.FlushRejectionSummary()` once, after Pass B.

**Hold and release — Pass A and Decision 5**

- [x] **Held while the gate is closed**: a request of a type with `HeldDuringIrreversibleWrite`, for a character whose gate is closed, is copied to that character's hold queue; its handler is not called; `HeldCount` rises by 1. A request of a type without the flag is dispatched normally for the same character.
- [x] **Held behind earlier held requests**: when a character still has held requests after Pass A, a new held-type request of that character is held too, even if the gate is open at that moment.
- [x] **Release order and timing**: on the first `DispatchTick` at which the gate is open, the held requests of that character run in arrival order, before any request of that tick's inbox, each with `WasHeld` true, its original `ArrivalTick` and the body it arrived with.
- [x] **Polled, not subscribed**: held requests are released when another `OnGateOpened` subscriber throws during `Open`. The dispatcher has no subscription to `OnGateOpened`.
- [x] **A handler may close the gate**: when a handler closes the gate in Pass B, later held-type requests of that character in the same pass are held. When a released request closes the gate in Pass A, the remaining held requests of that character stay held, in order.
- [x] **Discard on release**: a held request whose connection was removed, no longer owns the entity, or is no longer session-ready (`IsLiveOwner`, Story 035) is discarded when its turn comes, not dispatched.
- [x] **Released requests are not guarded again**: release does not call `Evaluate`.
- [x] **Hold queue bound**: the 17th held request of a character is dropped — anomaly `HeldRequestOverflow`; the 16 held ones are unaffected.
- [x] **Pool accounting**: every hold entry returns to the pool when its request is dispatched or discarded, or when its removed connection's storage is released. After 800 hold-and-release cycles on one dispatcher a request can still be held.
- [x] **Characters are independent**: a closed gate for character A does not delay or hold a request of character B.

**Isolation**

- [x] **One failure does not stop a pass**: when the handler, the guard chain, the directory or the gate throws for one request, a server error is logged with the message type and the client id, that request is dropped (in Pass A its hold entry returns to the pool), and the remaining requests of both passes are processed. `DispatchTick` does not throw. One exception: when the gate poll of Pass A (`gate.IsHeld`) throws, nothing is known about that character's gate, so its held requests stay held, in order, a server error is logged, and Pass A goes on with the next character; they are tried again on the next tick (ADR-014's Isolation rule covers the `IsLiveOwner` check and the handler call in Pass A, not the poll; added at code review 2026-10-09).
- [x] **The dispatcher sends nothing**: it has no outbound dependency; a dropped or discarded request produces no reply.

**Production registrations (Decision 3, last bullet)**

- [x] **`SetTarget`**: `NetworkingCoreInboundRegistration.Register` registers `SetTarget` as a request (tag `RpcTypeTag.SetTarget`, not held, `MaxBodyBytes` 4) with `SetTargetRequestHandler`, against the real `MessageRoutingRegistry`. A `SetTarget` message built with `SetTargetCodec.Write` and passed through `TryAccept` and `DispatchTick` sets the target in `TargetSlotTracker`.
- [x] **`HeartbeatMessage`**: the same method registers `HeartbeatMessage` as connection-level (`MaxBodyBytes` 0, no `SenderEntityID`) with the sink it is given.
- [x] **Only those two**: no other type is registered by this story (user decision 2026-10-09: registrations are part of this story, for the types that have a routing row and a message type in code).

**Source rules**

- [x] A search of `src/ServerLogic/Networking/InboundDispatch/` finds no `OnGateOpened`, `ArrayPool`, `await`, `async`, `Unity.Netcode`, `requestId` or `RegisterTickDriven`.

---

## Implementation Notes

- **Files** (namespace `IronGrind.Networking`, assembly `IronGrind.ServerLogic`), new in `src/ServerLogic/Networking/InboundDispatch/`:
  - `InboundRequestDescriptor.cs`, `ConnectionMessageDescriptor.cs`, `InboundRequestContext.cs` — readonly structs with the fields of ADR-014 Key Interfaces and a constructor each
  - `InboundRequestHandler.cs` — `public delegate void InboundRequestHandler(in InboundRequestContext context, ReadOnlySpan<byte> body);`
  - `IInboundMessageIntake.cs`, `IInboundRequestDispatcher.cs`, `IConnectionMessageSink.cs`, `IConnectionActivitySink.cs`, `IServerTickSource.cs`, `IConnectionCharacterDirectory.cs` — as in ADR-014 Key Interfaces
  - `MessageRoutingLookup.cs` — `public delegate bool MessageRoutingLookup(ushort messageTypeId, out MessageRoutingEntry entry);`
  - `InboundDispatchConstants.cs` — `MAX_INBOX_REQUESTS_PER_CONNECTION = 64`, `MAX_INBOX_BYTES_PER_CONNECTION = 4096`, `MAX_INBOUND_MESSAGE_BYTES = 400`, `MAX_HELD_BODY_BYTES = 64`
  - `InboundRequestDispatcher.cs` — `sealed`, implements `IInboundRequestDispatcher` and `IInboundMessageIntake`
  - `SetTargetRequestHandler.cs`, `NetworkingCoreInboundRegistration.cs`
- **Files modified**: `src/ServerLogic/Networking/TickLoop/ServerTickLoop.cs` (`: IServerTickSource`; the property exists). `src/Foundation/Networking/WireProtocol/SetTargetCodec.cs` only if it has no way to read the 4-byte body on its own: add a body-only read and have `TryRead` call it, keeping its rule that `targetEntityId` does not go through `WireIdCodec`.
- **Reused, not redefined**: `MAX_HELD_REQUESTS_PER_CHARACTER` (`TickCompletionConstants`), `ZoneBufferPool.MAX_PLAYERS_PER_ZONE`, `MessageEnvelopeCodec`, `StaleDiscardComparer`, `PendingSchemaDispatchException`, `CharacterID` (`IronGrind.Currency`), `EntityID`.
- **Constructor**:
  ```csharp
  public InboundRequestDispatcher(
      CrossCuttingRpcGuardChain guardChain,
      ICharacterMutationGate gate,
      IConnectionCharacterDirectory characterDirectory,
      IConnectionActivitySink activitySink,
      IServerTickSource tickSource,
      MessageRoutingLookup routingLookup,
      bool isDevelopmentBuild)
  ```
  plus an optional `INetworkTestObserver observer = null` inside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`, passed to `Evaluate` so that `OnSkillUsedRateLimitRejected` still fires (Story 002 release-stripping contract). Null arguments throw `ArgumentNullException`.
- **Why `routingLookup` is a parameter** (beyond ADR-014 Key Interfaces; decided 2026-10-09): `MessageRoutingRegistry` is static and has two client→server rows, `HeartbeatMessage` (U-U) and `SetTarget` (R-OD, not held). No held type and no U-U request type can be registered against it, so most of the tests of Migration Plan step 1 could not be written. Production passes `MessageRoutingRegistry.TryGetEntry`; tests pass a fake table. The registry is not changed.
- **Envelope**: decode the first 10 bytes with `MessageEnvelopeCodec.TryRead(…, out ServerMessageEnvelope)`. For a request, and for a connection-level type with `CarriesSenderEntityId`, the next 4 bytes are `SenderEntityID` (little-endian) and the body starts at offset 14; otherwise the body starts at offset 10. A body shorter than the message's codec expects is not the intake's concern: the descriptor has only a maximum, and the handler's codec rejects it.
- **Tag check at registration**: `Register` calls `CrossCuttingRpcGuardChain.IsRateLimited(descriptor.RpcTypeTag)` (Story 035) for every request descriptor: it throws `ArgumentOutOfRangeException` for a value outside the enum, and its result is the "carries a rate-limited tag" test for a held type. The tag given to `Evaluate` is always the registered descriptor's; no byte of the message is cast to `RpcTypeTag`.
- **Guard tick**: Pass B builds every `InboundRpcDescriptor` with the `currentTick` argument of `DispatchTick`, never `ArrivalTick` or the tick source; a different value within one pass resets the guard chain's log throttle (Story 035 review).
- **Stale check**: stale means `!StaleDiscardComparer.IsNewerVersion(highestSeen, sequenceNumber)`, so an equal value is stale. Use a has-value flag per (connection slot, U-U type), not a sentinel number. `Seal()` gives each registered U-U type an index; the per-slot arrays are sized then and cleared when the slot is given to a new connection. The check covers connection-level U-U types as well (`HeartbeatMessage`).
- **Inbox**: one record array of `MAX_PLAYERS_PER_ZONE × MAX_INBOX_REQUESTS_PER_CONNECTION` filled in acceptance order, so the array order is the `arrivalIndex` order and Pass B is one loop; a per-slot count and a per-slot arena fill offset enforce the two bounds; all are reset at the end of Pass B.
- **Connection slots**: 50 slots; a map from client id to slot created once with capacity 50. A removed connection keeps its slot, marked, until release. Release = clear the slot's inbox count, arena offset and stale state, and return its hold entries to the pool.
- **Hold queues**: a pool of `MAX_PLAYERS_PER_ZONE × MAX_HELD_REQUESTS_PER_CHARACTER` entries (context plus a 64-byte body area in one shared array). Each entry stores the `CharacterID` resolved when it was held. Keep the characters that have held requests in the order each one's first request was held. No collection is created when a request is held.
- **Pass A** per character, while `!gate.IsHeld(charId)` — check before each request, since a released request may close the gate: take the oldest entry; if its connection is removed or `!guardChain.IsLiveOwner(clientId, senderEntityId)`, discard it; otherwise call the handler with `WasHeld` true. Return the entry to the pool in both cases, also when the handler throws.
- **Pass B step 3**: hold when `descriptor.HeldDuringIrreversibleWrite && (gate.IsHeld(charId) || HeldCount(charId) > 0)`.
- **Logging**: anomalies are `Debug.LogWarning`, server errors `Debug.LogError`, both with the prefix `[InboundRequestDispatcher] <Name>:` and the client id and message type. Build the string only in the branch that logs. A per-slot flag per anomaly kind, cleared at `DispatchTick`, gives "once per connection per tick interval".
- **Diagnostics members** on the class (not on the interface, except `HeldCount`): `StaleDropCount`, `PendingRequestCount(uint clientId)`, `FreeHoldEntryCount`. Tests use them; nothing else does.
- **`SetTargetRequestHandler`**: constructor `(TargetSlotTracker tracker, IReadOnlyCollection<EntityID> validZoneEntityIds)` — the zone's live entity set, which has no provider yet and is supplied by the composition root. `Handle` drops a body that is not exactly `SetTarget.BodySize` bytes (anomaly log), otherwise calls `tracker.ProcessSetTarget(context.ClientId, context.SenderEntityId, target, validZoneEntityIds)`. It sends no reply; what the tracker does with a rejected target is Story 029's behaviour and is not changed.
- **`NetworkingCoreInboundRegistration.Register(IInboundRequestDispatcher dispatcher, SetTargetRequestHandler setTargetHandler, IConnectionMessageSink heartbeatSink)`**: two registrations, no `Seal()` — the composition root seals after every system has registered.
- **Not wired in**: nothing in production constructs the dispatcher, calls `TryAccept` or calls `DispatchTick` yet. Say so in the completion notes.
- **Doc comments** on every public type and member, with the ADR-014 decision each one implements.

---

## Out of Scope

- `ZoneTickPipeline` and the order of the tick (ADR-014 Decision 6; the next Networking Core story).
- The transport adapter, its length check and Verification Required 1–6 (after ADR-004 OQ-ADR4-3). What happens if `TryAccept` is called while a pass is running is part of that story: Verification Required 1 says it cannot happen.
- The session layer's implementations of `IConnectionActivitySink`, `IConnectionCharacterDirectory` and the heartbeat sink; this story uses fakes.
- The server composition root, and the provider of the zone's valid entity set.
- Every request type that has no routing row or no message type in code (19 of the 21 request rows of ADR-014 Decision 1), and their `RpcTypeTag` members.
- AC-ENH-38 and CR-ENH-18 end to end (Enhancement Story 009, unblocked by this story).
- Request deduplication, and any reply to the client.
- Registry entries for the new contracts (`design/registry`, user approval pending).

---

## QA Test Cases

**Files** (new, in `tests/EditMode/Networking/`): `InboundDispatch_Registration_tests.cs`, `InboundDispatch_Intake_tests.cs`, `InboundDispatch_Dispatch_tests.cs`, `InboundDispatch_HoldQueue_tests.cs`, `InboundDispatch_CoreRegistration_tests.cs`, and `InboundDispatchTestDoubles.cs`.
**Test doubles**: a fake routing table; a settable tick source; a recording activity sink and connection sink; a directory backed by a dictionary; a recording handler that copies the context and the body; a message builder that writes the envelope, the optional `SenderEntityID` and a body. The guard chain and the mutation gate are the real classes. Fake types: `HELD_ROD` (R-OD, held, 10 bytes), `PLAIN_ROD` (R-OD, 12 bytes), `UU_A` and `UU_B` (U-U requests; `UU_B` with tag `NotifySkillUsed`), `CHAT` (R-OD, 386 bytes), `CONN_UU` (connection-level, no `SenderEntityID`). Type ids and sizes are named constants in the doubles file. No sleeps, no real threads, no `async` test. Every expected `Debug.LogError` is declared with `LogAssert.Expect` before the act.

**Registration**
- **After seal** — `Register` and `RegisterConnectionLevel` after `Seal()` → `InvalidOperationException`.
- **Before seal** — `TryAccept` before `Seal()` → false, no activity reported.
- **Duplicates** — request twice; connection-level twice; one of each for the same id → each throws.
- **Null handler, null sink** → `ArgumentNullException`.
- **Body bound** — request with `MaxBodyBytes` 387 → throws; 386 → accepted. Connection-level without `SenderEntityID`: 391 → throws; 390 → accepted.
- **Held type rules** — held and U-U → throws; held with tag `AllocateFreePoint` → throws; held with `MaxBodyBytes` 65 → throws; 64 → accepted.
- **Undefined tag** — a request descriptor with `(RpcTypeTag)255`, not held → `ArgumentOutOfRangeException`.
- **No routing row, development build** → `PendingSchemaDispatchException` with the type id.
- **No routing row, release build** → no exception; one error logged; after `Seal()` a message of that type → false, `UnknownInboundMessageType`.
- **Row of the wrong direction** — a server→client row → same two results as no row.

**Intake**
- **Activity matrix** — an accepted request; a connection-level message; an unknown type; a malformed message; a stale U-U message; a request dropped by a full inbox → the activity sink recorded exactly one call for each, with the client id and the tick source's value.
- **Unknown or removed connection** — never added; added then removed → false, no activity, no sink call.
- **Malformed** — a 12-byte message of a type with `SenderEntityID`; a `PLAIN_ROD` with a 13-byte body → false; handler never called after `DispatchTick`.
- **Stale** — `UU_A` with sequence 10, then 9, then 10, then 11 → the handler receives 10 and 11; `StaleDropCount` is 2.
- **First message is never stale** — `UU_A` with sequence 0 on a new connection → accepted.
- **Wraparound** — `UU_A` with sequence `uint.MaxValue`, then 0 → both accepted.
- **Stale state is per connection** — sequence 10 from client 1, then 5 from client 2 → both accepted.
- **Stale state is cleared with the slot** — client 1 sends 10, is removed, `DispatchTick`, a new connection takes the slot and sends 5 → accepted.
- **R-OD after U-U** — `UU_A` with sequence 50, then `PLAIN_ROD` with sequence 40 → both dispatched.
- **U-U type after another U-U type** — `UU_A` with sequence 50, then `UU_B` with sequence 40 → both dispatched.
- **A dropped message does not advance the value** — `UU_A` 20 with an oversize body (dropped), then `UU_A` 15 → accepted.
- **Heartbeat** — the real 10-byte `HeartbeatMessage` envelope, registered as connection-level → the sink recorded `EntityID.Invalid` and a zero-length body; `PendingRequestCount` is 0.
- **Connection-level before session-ready** — a client that is not session-ready sends a connection-level message → the sink receives it.
- **Count bound** — 65 `PLAIN_ROD` from one client → 64 true, the 65th false; one `InboundInboxOverflow` warning; a request from a second client is accepted.
- **Byte bound** — 11 `CHAT` messages of 386 bytes from one client → 10 true (3,860 bytes), the 11th false.
- **Bounds reset** — after `DispatchTick`, the client of either case above can send 64 requests again, and the warning can appear again.
- **Largest message** — a 400-byte `CHAT` → the handler's copy of the body equals the 386 bytes sent.
- **Bounded logs** — 10 unknown-type messages from one client in one tick interval → 1 warning.
- **Never throws** — empty span, 9 bytes, 401 bytes of `0xFF` → false each.

**Dispatch**
- **Arrival order** — client 1 `PLAIN_ROD`, client 2 `HELD_ROD` (gate open), client 1 `UU_A`, client 2 `PLAIN_ROD` → the recording handler lists them in that order.
- **Context** — one request accepted at tick 7 and dispatched at tick 8 → every context field as stated in the criterion; body equal.
- **Guard rejections** — unknown entity; not session-ready; rate-limited (`UU_B` twice on one tick: first dispatched, second dropped); not owner → handler not called for the rejected one; `HeldCount` 0. Repeated with `HELD_ROD` and a closed gate for the first, second and fourth → `HeldCount` 0.
- **No character** — the directory has no entry for the client → handler not called; `InboundRequestWithoutCharacter` warning.
- **Empty after dispatch** — second `DispatchTick` → no handler call; `PendingRequestCount` 0.
- **Handler throws** — the first of three requests throws → one error logged with the type and client id; the other two dispatched.
- **Directory throws, gate throws** — a throwing fake for one request → same result.
- **Remove inside a handler** — client 1's first handler calls `RemoveConnection(1)`; client 1 has two more requests in the inbox and client 2 has one → client 1's two are skipped, client 2's is dispatched; no exception.
- **Slot reuse** — 50 connections; client 1 sends a request; `RemoveConnection(1)`; `AddConnection(51)` → true; `DispatchTick` → client 1's request not dispatched; client 51 can send.
- **Full zone** — 50 live connections, `AddConnection(51)` → false, one error. `AddConnection` of a live client → false, one error.
- **Summary flushed** — three identical guard rejections in one `DispatchTick` → one full warning and one summary line.

**Hold queue**
- **Hold** — gate closed for A; A sends `HELD_ROD` → not dispatched, `HeldCount(A)` 1. A sends `PLAIN_ROD` in the same tick → dispatched.
- **Release order** — A sends three `HELD_ROD` over two ticks with the gate closed; the gate opens; A sends `PLAIN_ROD`; `DispatchTick` → the handler lists held 1, 2, 3, then the new request; `WasHeld` true for the three, with their original `ArrivalTick` and bodies.
- **Held behind held** — A has two held requests and the gate is open; the first one's handler closes the gate in Pass A, so one remains. In the same tick's inbox, B's `PLAIN_ROD` (whose handler opens A's gate) arrives before A's new `HELD_ROD` → A's new request is held although the gate is open when it is reached; `HeldCount(A)` 2; the next `DispatchTick` runs both in order.
- **A subscriber throws** — a throwing handler subscribed to `OnGateOpened`; `gate.Open(A)` throws to the test; `DispatchTick` → A's held requests are dispatched.
- **Handler closes the gate in Pass B** — A sends `PLAIN_ROD` whose handler closes A's gate, then `HELD_ROD` → the second is held.
- **Released request closes the gate** — of three held, the first one's handler closes the gate → two remain held, in order; released after the next opening.
- **Discard: connection removed** — A holds two; `RemoveConnection`; gate opens; `DispatchTick` → no handler call; `FreeHoldEntryCount` back to 800.
- **Discard: not session-ready** — A holds one; `ClearSessionReady`; gate opens → discarded.
- **Discard: ownership lost** — A holds one; the entity is unregistered → discarded.
- **Not guarded again** — in the two discard cases above, a log handler counts no `[CrossCuttingRpcGuardChain] Evaluate:` warning at release (`IsLiveOwner` logs nothing; `Evaluate` would have logged the rejection). A request held across five ticks is dispatched on release.
- **Overflow** — 17 `HELD_ROD` for A with the gate closed → `HeldCount` 16; one `HeldRequestOverflow` warning; after opening, 16 handler calls.
- **Pool accounting** — 800 cycles of hold then release on one dispatcher → `FreeHoldEntryCount` 800; a further request can be held.
- **Two characters** — gate closed for A only; A and B each send `HELD_ROD` → B's dispatched, A's held.
- **Released handler throws** — one error logged; the next held request is dispatched; `FreeHoldEntryCount` restored.
- **Released handler removes its connection** *(code review 2026-10-09)* — A holds two; the gate opens; the first one's handler calls `RemoveConnection(A)` → one handler call; the second is discarded; `FreeHoldEntryCount` back to 800.
- **Gate poll throws in Pass A** *(code review 2026-10-09)* — A holds one; the gate opens; the next `IsHeld` call throws → one error logged, no handler call, `HeldCount(A)` 1; the next `DispatchTick` dispatches it with `WasHeld` true.

**Added at code review 2026-10-09**
- **Inbox-bound drop does not advance the value** (Intake) — 64 `PLAIN_ROD`, then `UU_A` 20 (dropped by the count bound), `DispatchTick`, `UU_A` 15 → accepted.
- **Connection sink throws** (Intake) — a connection-level sink that throws → `TryAccept` returns false, one `TryAcceptFailed` error, no exception.
- **`AddConnection` of a removed client inside a handler** (Dispatch) — a handler calls `RemoveConnection(1)` then `AddConnection(1)` → false, one error: inside a pass the removed connection still has its slot.

**Core registration**
- **`SetTarget` end to end** — the real `MessageRoutingRegistry.TryGetEntry`; `NetworkingCoreInboundRegistration.Register`; `Seal()`; a message from `SetTargetCodec.Write`; `TryAccept`, `DispatchTick` → `TargetSlotTracker.GetTarget(clientId)` returns the target.
- **`SetTarget` wrong body length** — a 3-byte body → handler logs an anomaly; target unchanged.
- **Heartbeat registered** — the heartbeat sink given to `Register` receives a `HeartbeatMessage`.
- **Development build accepts both** — `Register` with `isDevelopmentBuild` true throws nothing: both types have a client→server row.

**Checked at `/story-done`, not automated**
- The source search of the last acceptance criterion.
- Allocation: a read of `TryAccept` and `DispatchTick` finds no `new` of a reference type, no boxing, no LINQ, no capturing lambda and no string built outside a branch that logs.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: the five `InboundDispatch_*_tests.cs` files in `tests/EditMode/Networking/` — must exist and pass. The suite total before this story is the total recorded by Story 035; record the total after it at `/story-done`.

**Status**: [x] Created — `InboundDispatch_Registration_tests.cs` (25), `InboundDispatch_Intake_tests.cs` (32), `InboundDispatch_Dispatch_tests.cs` (21), `InboundDispatch_HoldQueue_tests.cs` (16), `InboundDispatch_CoreRegistration_tests.cs` (5), with `InboundDispatchTestDoubles.cs` — 99 tests; suite 2182 / 2182 on 2026-10-09

---

## Dependencies

- Depends on: Story 035 (Complete — `IsLiveOwner`, `IsRateLimited`, gap 0, `FlushRejectionSummary`); Story 030 (Complete — `CharacterMutationGate`); Story 010 (Complete — guard chain); Story 009 (Complete — `ServerTickLoop`); Story 025 (Complete — `MessageRoutingRegistry`); Story 029 (Complete — `SetTarget`, `TargetSlotTracker`); Story 003 and Story 005 (Complete — envelope codec, `StaleDiscardComparer`). ADR-014 Accepted (2026-10-09).
- Unlocks: the tick pipeline story (ADR-014 Migration Plan step 2); Enhancement Story 009 (Migration Plan step 3).

---

## Completion Notes
**Completed**: 2026-10-09
**Criteria**: 41/41 passing
- Suite: 2182 total, 2182 passed, 0 failed (totals read from `TestResults.xml`, run of 2026-10-09 14:40 local, after the review fixes; 2083 before the story). The five `InboundDispatch_*` fixtures 99/99; `TickLoop_CrossCuttingGuards_Dispatch_Tests` 24/24 with no diff on its file. No compile error.
- Verified by reading, not by a test: the source search of the last criterion (no hit in `src/ServerLogic/Networking/InboundDispatch/`); "the dispatcher sends nothing" (no outbound dependency); no allocation in `TryAccept` or `DispatchTick` outside a branch that logs (main session and `unity-specialist`).
- Untested branch: a throwing guard chain (the class is sealed, and registration rejects the only input that makes `Evaluate` throw).
- Not wired in: nothing in production constructs the dispatcher, calls `TryAccept` or calls `DispatchTick`. The composition root, the transport adapter and the session layer's sinks and directory are later stories.
- Decisions taken at `/story-readiness` 2026-10-09: unknown-type and malformed logs at most once per connection per tick interval; `AddConnection` of a live client returns false; a tag outside the enum throws at registration.
- `SetTargetCodec` gained `TryReadBody`; `TryRead` calls it. `ServerTickLoop` implements `IServerTickSource`.
**Deviations** (advisory):
- Pass A gate poll: when `gate.IsHeld` throws, the character's held requests stay held and are tried again on the next tick; the request is not dropped. The Isolation criterion was reworded at code review and a test added. The user answered the review list and the `/story-done` report with "continue"; the point was not named explicitly.
- The once-per-connection-per-tick-interval log bound also covers `InboundRequestWithoutCharacter` and `HeldRequestOverflow` (the story lists three kinds).
- Extra diagnostics member `SuppressedAnomalyCount`.
- Two bounds ADR-014 does not mention: the inbox record array is not compacted when a slot is released (TD-065); at most 100 hold queues (two per connection slot).
- `TryAccept` and `DispatchTick` each wrap their body in a catch-all that logs a server error (`TryAcceptFailed`, `DispatchTickFailed`); a message shorter than 10 bytes logs `InboundMessageMalformed`.
- Only the `Unreliable` channel gets the stale check; a reliable-unordered type is treated like R-OD. The highest-seen value advances before a connection-level sink is called, so a sink that throws still counts as reached.
- Five methods of `InboundRequestDispatcher` exceed 40 lines (`TryAcceptCore` about 100, `ReleaseHeldRequests` about 70, `ReleaseSlot` about 60, `Register` about 53, `DispatchInbox` about 48); not split.
- 99 tests; the QA list gained five cases at code review.
**Test Evidence**: Logic — the five `InboundDispatch_*_tests.cs` files in `tests/EditMode/Networking/`
**Code Review**: Complete — `/code-review` 2026-10-09 (`unity-specialist` + `qa-tester`; ADR-014 check by the main session): APPROVED WITH SUGGESTIONS; items 1–6 applied, item 7 (the `observer` parameter doc) left as the sibling classes have it.
**Tech debt**: TD-065.
