# Control Manifest

> **Engine**: Unity 6.3 LTS (6000.3)
> **Last Updated**: 2026-10-19
> **Manifest Version**: 2026-10-19
> **ADRs Covered**: ADR-001, ADR-002, ADR-003, ADR-004, ADR-005, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010, ADR-011, ADR-012, ADR-013, ADR-014, ADR-015 (and ADR-006 Amendments 1–3, 2026-10-01 and 2026-10-19; ADR-004 Amendment 1, 2026-10-09; ADR-009 Amendment 1, 2026-10-19; the 2026-10-16 notes on ADR-014 Decision 6 and ADR-009 Decision 3; the 2026-10-19 notes on ADR-011 Decision 2 and ADR-010)
> **Status**: Active — regenerate with `/create-control-manifest update` when ADRs change

`Manifest Version` is the date this manifest was generated. Story files embed this date when created. `/story-readiness` compares a story's embedded version to this field to detect stories written against stale rules. Always matches `Last Updated` — they are the same date, serving different consumers.

This manifest is a programmer's quick-reference extracted from all Accepted ADRs, technical preferences, and engine reference docs. For the reasoning behind each rule, see the referenced ADR.

---

## Foundation Layer Rules

*Applies to: scene management, event architecture, save/load, engine initialisation, networking substrate, persistence, hosting infrastructure, assembly layout*

"Foundation layer" here is a layering term. It is not the assembly `IronGrind.Foundation`, which is the shared assembly of ADR-012; most of this layer's code is server-only and lives in `IronGrind.ServerLogic` — source: ADR-012

### Required Patterns

**Server/client assembly boundary (ADR-012):**
- Three assemblies: `IronGrind.Foundation` (`src/Foundation/`, in both builds; may reference Unity engine assemblies and approved packages only), `IronGrind.ServerLogic` (`src/ServerLogic/`, server build only; may reference `IronGrind.Foundation` and the named server-only DLLs), `IronGrind.Client` (`src/Client/`, client build only; may reference `IronGrind.Foundation`). `ServerLogic` and `Client` do not reference each other; `Foundation` references neither — source: ADR-012
- Define constraints, exactly: `IronGrind.ServerLogic` → `["UNITY_SERVER || UNITY_EDITOR"]`; `IronGrind.Client` → `["!UNITY_SERVER || UNITY_EDITOR"]` — source: ADR-012
- All three assemblies are `autoReferenced: false`; any script under `Assets/` that uses project code gets its own asmdef with explicit references — source: ADR-012
- Classify every type by asking in this order, the first "yes" decides: (1) references UI Toolkit, UGUI, rendering, audio or input, or exists only to present → `Client`; (2) computes, validates or mutates authoritative game state, or holds a formula, probability, tuning value, roll, or a rule the player must not be able to read or replace → `ServerLogic`; (3) both sides need it to talk to each other or to describe the same thing (ids, wire enums and structs, wire message schemas, static definitions the client displays) → `Foundation`; (4) otherwise → `ServerLogic` — source: ADR-012
- Result types follow their producer unless the client receives them (`DamageResult`, `DamageContext` are `ServerLogic`; the client gets the wire message built from them) — source: ADR-012
- An interface lives with its consumer. An interface declared so that `Foundation` code can call into server logic is `Foundation`, and its signature must not carry a server-only type — source: ADR-012
- Shared by decision: every type in `src/Foundation/ItemDatabase/` (no move story), and the class data `ClassDefinition`, `IClassRegistry`, `ClassRegistry` — source: ADR-012
- A move is a folder and assembly change only: namespaces do not change; the `.cs` and its `.meta` move together (GUID preserved); a moved type that is serialized through `[SerializeReference]` or stored by name gets `[MovedFrom(true, sourceAssembly: "IronGrind.Foundation")]` and its assets are re-saved; `src/` is searched for `Type.GetType`, `AssemblyQualifiedName` and `TypeNameHandling` — source: ADR-012
- Move order (users first, the most depended-on last): Damage Calculation → Loot Table → Enhancement → NPC Interaction → Inventory → Leveling → Networking (server part, classified file by file) → Currency → Character Stats. Each move story re-checks the graph first (grep for the system's type names outside its folder), deletes its entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list, and leaves the suite green. **Status 2026-10-08: all nine systems are moved.** New server systems are written in `src/ServerLogic/` from their first story — source: ADR-012
- New systems (Hit Detection, Enemy AI, Navigation/Pathfinding, Auto-Attack Combat, Status Effects) are written in `src/ServerLogic/` from their first story — source: ADR-012
- Npgsql and Dapper are referenced by `IronGrind.ServerLogic` only, and each DLL is itself absent from a client player under the same condition (`UNITY_SERVER || UNITY_EDITOR`) — source: ADR-012, ADR-006
- A `NetworkBehaviour` on a prefab clients also instantiate lives in `IronGrind.Foundation` as a thin shell: it declares what NGO needs and hands server-side calls to an interface declared in `Foundation` and implemented in `ServerLogic`, supplied by the server composition root (constructor or `Initialize` injection); on the client the interface is not supplied — source: ADR-012, ADR-010
- Wire message schemas and the code that reads and writes them are `Foundation`. Server-side handlers for inbound messages are registered by the server composition root in `ServerLogic`; client-side handlers by the client composition root in `Client`. On the server, the intake decodes the envelope and copies the request into the inbox, and the handler runs on the tick (see Inbound request dispatch and tick order) — source: ADR-012, ADR-004, ADR-010, ADR-014
- The server composition root and its bootstrap component are in `IronGrind.ServerLogic`; the client's are in `IronGrind.Client`. Server-only components are created in code by the server composition root or live in server-only scenes that are not in the client build profile's scene list — source: ADR-012
- Tests: the single test assembly `IronGrind.Foundation.EditModeTests` references all three assemblies; each new assembly has its own `AssemblyInfo.cs` with `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")` — source: ADR-012
- Boundary test (`tests/EditMode/Architecture/`, runs with the EditMode suite): asserts the asmdef names, the two constraint strings, `autoReferenced: false` and the reference lists; asserts per type that every type in `IronGrind.Foundation` is on the shared allow-list (each entry with its client consumer named in a comment; the not-yet-moved list was removed on 2026-10-08, when the last system moved); asserts that no `Foundation` or `Client` type has a field, property, parameter or base type from `ServerLogic`. The asmdef files are located through `CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName` — source: ADR-012
- Build checks: a client-binary scan under `tools/` fails a client build that contains `IronGrind.ServerLogic`, `Npgsql`, `Dapper` or a forbidden type name, and must be shown to fail on a deliberately misplaced type; a content check fails a client build whose assets depend on a script under `src/ServerLogic/`, a Dedicated Server build whose assets depend on a script under `src/Client/`, and either build on a script under `src/DevHarness/` — source: ADR-012

**Purchase transaction integrity (ADR-001):**
- `BuyRequest` and `SellRequest` must carry `requestId: uint` (monotonically incrementing per session; resets on each new `OpenNPCInteraction`) — source: ADR-001
- Server dedup key for all R-OD messages carrying `requestId`: `(charId, messageType, requestId)`. `messageType` is the `ushort` wire-protocol envelope identifier — source: ADR-001 Amendment A1
- Dedup layer must resolve `messageType` from the envelope field — never hardcode numeric type IDs in the dedup layer — source: ADR-001 Amendment A1
- Dedup window: `SESSION_TTL_SECONDS` (300s) — source: ADR-001
- Create `PendingPurchase` record in durable storage **before** calling `TrySpendGold` — source: ADR-001
- Reconnect reconciliation: query `GoldDebited` PendingPurchase records after `SessionHandshake` accepted, before any gameplay messages are processed — source: ADR-001
- Rate-limit `BuyRequest`/`SellRequest`: 10 requests/second per `charId` (sliding 1-second window) — source: ADR-001
- Use `CompensatingRefund` (GoldTransactionReason enum value = 8) for refund-on-failure and reconnect reconciliation — source: ADR-001

**Networking library — NGO (ADR-004):**
- `NetworkManager.ServerTime.Tick` (NGO `NetworkTickSystem`) is the authoritative tick counter for all tick-numbered operations — source: ADR-004
- `NetworkConfig.TickRate` must be set to `TICK_RATE_HZ = 20` — source: ADR-004
- Hot-path gameplay messages sent via NGO `CustomMessagingManager` with per-send `NetworkDelivery` value — source: ADR-004
- Project messages are NGO **unnamed** messages: the server receives through `CustomMessagingManager.OnUnnamedMessage` (`(ulong clientId, FastBufferReader reader)`) and sends with `SendUnnamedMessage(clientId, writer, networkDelivery)`. The message body is the project envelope and payload; the project's `MessageTypeID` is the only message header — source: ADR-004 Amendment 1
- The transport adapter handles: a reliable send queue that overflows disconnects the client; `DisconnectClient(clientId)` is deferred to the end of the frame and messages of that client already queued still reach the handlers in that frame; a receive handler can run inside a transport disconnect event; the reader given to a handler is disposed when the handler returns. `CustomMessagingManager` is a new instance after each start and `null` before it, so an `OnUnnamedMessage` subscription does not survive a restart — source: ADR-004 Amendment 1, `modules/networking.md` (run 2026-10-12)
- Project owns the 10-byte envelope: `MessageTypeID` (ushort), `SequenceNumber` (uint), `ServerTickNumber` (uint); + `SenderEntityID` (uint) for client→server — source: ADR-004
- `SenderEntityID` is a project EntityID, not NGO's `clientId` — server validates the `clientId ↔ EntityID` mapping — source: ADR-004
- Application-level `RttProbe`/echo on U-U channel is the authoritative OWL source; transport RTT seeds the initial estimate only — source: ADR-004
- Bulk messages above about 1272 bytes of payload use `ReliableFragmentedSequenced`; every other delivery is capped there (in the Editor `SendUnnamedMessage` throws `OverflowException` above it) — source: ADR-004 Amendment 1, `modules/networking.md` (run 2026-10-12)
- Channel guarantee mapping: R-OD = guaranteed + in-order (`ReliableSequenced`); R-U = guaranteed, no ordering required (`Reliable`); U-U = best-effort (`Unreliable`). On `UnityTransport`, `Reliable` uses the same reliable-sequenced pipeline, so an R-U message is delivered in order too: no code may expect an R-U message to overtake a delayed reliable one. The mapping stays `Reliable` for R-U so the intent is visible — source: ADR-004 Amendment 1

**Persistence layer — PostgreSQL (ADR-006):**
- PostgreSQL is the sole persistence store; accessed via Npgsql + Dapper — source: ADR-006
- All SQL is explicit parameterized SQL — no dynamic query generation, no ORM change tracking — source: ADR-006
- Optimistic-concurrency UPDATE: `WHERE character_id = @id AND save_version = @expected`; rows-affected = 0 → return `ConcurrencyConflict` — source: ADR-006
- `PendingPurchase` INSERT and `TrySpendGold` UPDATE share one `NpgsqlTransaction` — source: ADR-006
- IL2CPP `link.xml` must preserve Npgsql and Dapper: `<assembly fullname="Npgsql" preserve="all" />` + `<assembly fullname="Dapper" preserve="all" />` — source: ADR-006. The `link.xml` is project-wide and must not be what brings the DLLs into a client build — source: ADR-012
- At-most-one write in flight per `CharacterID` (service-layer per-CharacterID write-queue) — source: ADR-006
- Per-zone-process connection pool: one `NpgsqlDataSource` per zone process with `Timeout=5;Command Timeout=5;Pooling=true;Minimum Pool Size=2;Maximum Pool Size=10;Connection Idle Lifetime=60` (ADR-006's original 20 is superseded). The composition root builds it at startup step S4 with `NpgsqlConnectionStringBuilder` from server config (host, port, database, username, pool values) and the password from `IRONGRIND_PG_PASSWORD`. `IRONGRIND_DEV_PG` is a test-and-verification variable only — source: ADR-006 Amendment 3, ADR-007
- C# `uint` fields (CharacterID, AccountID, GoldBalance, GoldVersion, LastZoneID, SaveVersion) map to `BIGINT` columns; `last_zone_id` is `BIGINT` — source: ADR-006, ADR-006 Amendment 2
- Each non-null `inventory_slots` JSONB entry is `{"item_id": N, "count": C, "enhancement_level": M}` with `0 ≤ M ≤ MAX_ENHANCEMENT_LEVEL`; the deserializer treats a missing `enhancement_level` key as 0 — source: ADR-006 Amendment 1

**Asynchronous persistence in the tick loop (ADR-011):**
- Tick-driven code calls a `Task`-returning persistence method and passes the returned task, in the same statement, to `ITickCompletionQueue.Track`. This applies to every `ICharacterPersistence` call made from tick code (`LoadCharacter`, `SaveSession`, `CreateStub`, `SaveIrreversibleOutcome`, the purchase calls) — source: ADR-011
- `ITickCompletionQueue.Drain()` runs once per tick, before any game logic of that tick. For every tracked task whose `IsCompleted` is true, in the order the tasks were tracked, it invokes the callback on the tick thread with a `TickTaskResult<T>` (`Completed` / `Faulted` / `Canceled` / `TimedOut`). `Drain` reads `Task.IsCompleted` only, never throws into the tick, and an exception thrown by one callback is caught and logged without stopping the rest — source: ADR-011
- `Track` and `Drain` are called from the tick thread only; both assert it — source: ADR-011
- Watchdog: a task still incomplete `PERSISTENCE_WATCHDOG_TICKS` (200 = 10 s) after it was tracked has its `CancellationToken` cancelled, a critical alert raised, and its callback invoked with `TimedOut`. The persistence layer's own `PERSISTENCE_WRITE_TIMEOUT_SECONDS` (5 s, ADR-006) fires first; the watchdog is the backstop. A cancelled write has an unknown outcome. A late completion of a timed-out task is logged and its `Exception` is read — source: ADR-011
- Shutdown drain (`ITickCompletionQueue.DrainOnShutdown(TimeSpan timeout)`): ADR-009 Amendment 1 teardown step 5 — after the tick is off, the listener stopped, nav teardown, CR-ZI-12 and the one outbound flush, and before the NGO shutdown, the Dispose pass and `Application.Quit` — one bounded blocking wait on all tracked tasks (timeout = the watchdog duration; `AggregateException` caught, not rethrown), then one last `Drain`. Incomplete tasks after the timeout are logged and alerted. It runs on the explicit shutdown path, not from `OnApplicationQuit` — source: ADR-011, ADR-009
- `ICharacterPersistence` implementations copy everything they need from live game state before their first `await`, on the calling (tick) thread; after it they touch only that copy and the database — source: ADR-011
- `ICharacterPersistence` implementations use `ConfigureAwait(false)` on every `await` (including `await using` / `await foreach`); if an awaited call cannot be trusted not to capture the context, the method runs its body through `Task.Run` after the snapshot. Every `TaskCompletionSource` in the persistence layer and in test fakes uses `TaskCreationOptions.RunContinuationsAsynchronously` — source: ADR-011
- `ICharacterMutationGate`: closed by the irreversible-outcome coordinator before the outcome is applied in memory, opened after the result has been handled (success or rollback; opened in a `finally`). `Close` on an already closed gate is an error — one write in flight per character — source: ADR-011
- The inbound request dispatcher (between the RPC guard chain and game logic) checks the gate once per request. A request whose type is marked `HeldDuringIrreversibleWrite` (every request that can change a character's bag, equipment or gold: move, equip, unequip, discard, sell, buy, consumable use, accessory merge, respec Phase 1) is appended to the character's hold queue while the gate is closed; other requests are dispatched normally. When the gate opens, held requests are dispatched in arrival order on the same tick, after `Drain` and before that tick's new requests; if it opened because of a failed write, they are discarded. The dispatcher is specified in the next group — source: ADR-011, ADR-014

**Inbound request dispatch and tick order (ADR-014):**
- One `InboundRequestDispatcher` per zone process, in `IronGrind.ServerLogic` (namespace `IronGrind.Networking`), is the only path from a client message to a game system. Every client→server message that reaches a game system is a request and goes through it. Server-originated work (loot pickup, auction delivery, AI) does not go through it — source: ADR-014
- Every client→server type is classified in the table of ADR-014 Decision 1 as a request or as a connection-level message (`SessionHandshake`, `HeartbeatMessage`, `RttProbeEcho`, `ClientBackgrounded` / `ClientForegrounded`, `ZoneSnapshotRequest`). Connection-level messages pass through the same intake, but are not guarded by the session-ready gate, are never held and never enter the inbox. The story that adds a client→server type adds its `MessageRoutingRegistry` row, its row in that table and its registration together — source: ADR-014
- The transport adapter is the only code that touches the transport's receive API. In its callback it checks that the message is 10 to `MAX_INBOUND_MESSAGE_BYTES` bytes long and calls `IInboundMessageIntake.TryAccept(clientId, message)`. It decodes nothing and calls no game system, no guard and no handler — source: ADR-014
- `TryAccept` runs on the main thread, outside the tick; it copies what it keeps, allocates nothing and never throws (false = dropped). Its steps, in this order: (1) a message from a connection that was never added or has been removed is dropped; every other message is reported once to `IConnectionActivitySink.OnInboundActivity(clientId, IServerTickSource.ServerTickNumber)`, before anything is decoded; (2) the 10-byte CR-NET-7.1 envelope is decoded and `MessageTypeId` looked up in the sealed type table — no entry: dropped, anomaly `UnknownInboundMessageType`; (3) for a type that carries `SenderEntityID`, the 4 bytes that follow are read — a message too short for its type, or with a body longer than the type's `MaxBodyBytes`: dropped, anomaly `InboundMessageMalformed`; (4) for a U-U type only, a stale message is dropped — counted, not logged as an anomaly; (5) a connection-level message is handed to the `IConnectionMessageSink` registered for its type, and processing stops; (6) a request is dropped if the connection already has `MAX_INBOX_REQUESTS_PER_CONNECTION` undispatched requests or its body does not fit in what is left of the connection's `MAX_INBOX_BYTES_PER_CONNECTION` for this tick interval — anomaly `InboundInboxOverflow`, logged once per connection per tick; (7) otherwise the body is copied into the connection's segment of the inbox arena and the record `(arrivalIndex, clientId, envelope fields, senderEntityId, arrivalTick, body offset, body length)` is stored — source: ADR-014
- Activity: every message of a known connection counts, including one then dropped in step 2, 3, 4 or 6 (any packet resets the heartbeat timeout, CR-NET-7.10). Not reported: a message from an unknown or removed connection, and a message the adapter dropped for its length. The session layer implements `IConnectionActivitySink`: it maps the connection to its account and calls `ConnectionStateMachine.RecordInboundActivity` — source: ADR-014
- Stale check (CR-NET-7.5, `StaleDiscardComparer`): U-U types only, against the highest `SequenceNumber` seen for that type on that connection; the first message of a type on a connection is never stale. The highest-seen value of a type advances only for a message of that type that reaches step 5 or is accepted in step 7 — source: ADR-014
- An `IConnectionMessageSink` and the `IConnectionActivitySink` run inside the receive callback, so they only record: reset a timeout, store an echo time, or queue work for the session state machine. A sink does not keep `body` after it returns — source: ADR-014, ADR-010
- `arrivalIndex` is one zone-wide counter: the order in which the server received the requests, across all connections and all request types. `ArrivalTick` and `DispatchTick` are `ServerTickLoop.ServerTickNumber` values, read through `IServerTickSource` (which `ServerTickLoop` implements) — source: ADR-014
- Inbox storage is allocated when the zone is created: a record array of `MAX_PLAYERS_PER_ZONE × MAX_INBOX_REQUESTS_PER_CONNECTION` entries, a byte arena with one `MAX_INBOX_BYTES_PER_CONNECTION` segment per connection slot (filled from its start, reset by each tick's dispatch), and per connection slot one highest-seen `SequenceNumber` per registered U-U type, cleared when the slot is given to a new connection. The inbox is emptied completely by every tick's dispatch; no backlog carries to the next tick — source: ADR-014
- Registration: the server composition root registers, for each request type, an `InboundRequestDescriptor` and an `InboundRequestHandler` (`Register`), and for each connection-level type a `ConnectionMessageDescriptor` and an `IConnectionMessageSink` (`RegisterConnectionLevel`); then it calls `Seal()`. Registration after `Seal()` throws. The two kinds form one type table — source: ADR-014, ADR-012
- `InboundRequestDispatcher` implements both `IInboundRequestDispatcher` and `IInboundMessageIntake`; the adapter receives it as `IInboundMessageIntake` only. Its constructor takes the `IConnectionActivitySink` and `bool isDevelopmentBuild`; the composition root passes `BuildConfiguration.IsDevelopmentBuild` — source: ADR-014
- `InboundRequestDescriptor` carries `MessageTypeId`, `RpcTypeTag` (the enum gains a member per request type, most with no limit), `HeldDuringIrreversibleWrite` (set per type as the table of Decision 1 lists) and `MaxBodyBytes`. `ConnectionMessageDescriptor` carries `MessageTypeId`, `MaxBodyBytes` and `CarriesSenderEntityId`. Every request carries `SenderEntityID`. The channel of a type (R-OD or U-U) is in neither descriptor: registration reads it from the type's `MessageRoutingRegistry` entry — source: ADR-014
- Registration fails at startup, not at runtime, when: the type is registered twice (as a request, as a connection-level type, or as both); the handler or the sink is null; `MaxBodyBytes` exceeds `MAX_INBOUND_MESSAGE_BYTES` less the envelope; a held type is U-U, carries a rate-limited tag, or has `MaxBodyBytes` above `MAX_HELD_BODY_BYTES` — source: ADR-014
- A type with no `MessageRoutingRegistry` entry of direction client→server (AC-MCR-03): in a development build, registration throws `PendingSchemaDispatchException` at startup; in a release build the registration is skipped and logged as a server error, and every message of that type is then dropped at intake as `UnknownInboundMessageType`. No crash in either build. The dispatcher story registers only the types that have a routing row and a message type in code — source: ADR-014
- A handler is `void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)`. It decodes the body with the message's codec and calls its system, on the tick thread — source: ADR-014
- Request deduplication stays in the handler's system (ADR-001 for shop requests, `EnhancementRequestDeduplicator` for enhancement). A retransmit that arrives while the original is held is held too; on release the original executes and the retransmit receives the cached result — source: ADR-014, ADR-001
- `InboundRequestDispatcher.DispatchTick(currentTick)` runs once per tick, after `ITickCompletionQueue.Drain`, called by `ZoneTickPipeline` only. It first releases what connections removed since the previous call still hold (inbox records, arena segment, hold-queue entries back to the pool), then runs Pass A, then Pass B — source: ADR-014
- Pass A (release): the dispatcher keeps the characters that have a non-empty hold queue, in the order each one's first request was held. For each whose gate is not held (`!gate.IsHeld(charId)`, polled), the held requests are dispatched in arrival order. Before each one, `IsLiveOwner` is checked (connection not removed, still owns the entity, still session-ready); a request that fails is discarded. Released requests are not rate-limited again. If a released request closes the gate, the remaining ones stay held — source: ADR-014, ADR-011
- Pass B (new requests): the inbox is taken in `arrivalIndex` order; entries of a connection that is removed or no longer known are skipped. Per request: (1) `CrossCuttingRpcGuardChain.Evaluate(new InboundRpcDescriptor(clientId, senderEntityId, descriptor.RpcTypeTag, currentTick))` — any result other than `Accepted` drops it; (2) the connection's `CharacterID` is resolved through `IConnectionCharacterDirectory` — no character: dropped, anomaly `InboundRequestWithoutCharacter`; (3) if `descriptor.HeldDuringIrreversibleWrite` and (`gate.IsHeld(charId)` or that character still has held requests), the body is copied to the character's hold queue — a full hold queue drops the request, anomaly `HeldRequestOverflow`; (4) otherwise the handler is called. A handler may close the gate; later held-type requests of that character in the same pass are then held — source: ADR-014
- Isolation: steps 1–4 of one request in Pass B, and the `IsLiveOwner` check and handler call of one released request in Pass A, run inside one `try`/`catch`. An exception is logged as a server error with the message type and client id, that request is dropped (in Pass A its hold-queue entry returns to the pool), and the pass continues — source: ADR-014
- `AddConnection(clientId)` is called when a connection is established. Outside a pass it first releases the storage of the connections removed since the last `DispatchTick`; inside a pass it releases nothing. It returns false and logs a server error if no slot is free (a caller error: `MAX_PLAYERS_PER_ZONE` live connections). `RemoveConnection(clientId)`, called on disconnect and on zone transfer, only marks the connection removed and is safe to call inside a pass — source: ADR-014
- The dispatcher sends nothing to a client. A dropped or discarded request gets no reply; replies are the business of the handler's system — source: ADR-014
- `EnhancementAttemptRequest` and `RespecPhase2Request` are not held: each starts an irreversible outcome of its own. While the character's gate is closed, `EnhancementService` rejects the first as a concurrent attempt and `IrreversibleOutcomeCoordinator.Begin` returns `RejectedWriteInFlight` for the second; the handler's system sends the rejection — source: ADR-014, ADR-011
- `CrossCuttingRpcGuardChain` changes, made by the dispatcher story: (1) `GetRequiredTickGap` lists every `RpcTypeTag` member with its gap (0 for no limit), and `Evaluate` skips the last-accepted-tick read and write when the gap is 0; (2) `bool IsLiveOwner(uint clientId, EntityID entityId)` — the entity is registered, owned by that client, and the client is session-ready; it writes nothing; (3) the first rejection per (client, result) per tick is logged in full, the rest are counted and reported in one line per tick — source: ADR-014
- Hold queue storage: at most `MAX_HELD_REQUESTS_PER_CHARACTER` entries per character, each a copy of the request context and body, taken from a pool pre-allocated for `MAX_PLAYERS_PER_ZONE × 16` entries of `MAX_HELD_BODY_BYTES`. An entry returns to the pool when its request is dispatched or discarded, or, if its connection was removed, at the start of the next `DispatchTick` or at an `AddConnection` that comes before it — source: ADR-014
- `ZoneTickPipeline` is one class in `IronGrind.ServerLogic`; the composition root registers its `Tick` as the one tick-driven delegate of `ServerTickLoop`. It runs five steps in this order: (1) Completion — `ITickCompletionQueue.Drain(currentTick)`; (1a) Relay — `ICrossZoneRelayInbox.Drain(currentTick, handler)`, each relayed body handed to `IZoneClientBroadcastSink.EnqueueToAllClients(..., isCapExempt: false)` (ADR-015 Decision 7; the pipeline takes `ICrossZoneRelayInbox` and `ICrossZoneRelayHandler` as constructor arguments; step name `STEP_RELAY`); (2) Requests — `InboundRequestDispatcher.DispatchTick(currentTick)`; (3) Simulation — ADR-002 phases 1–4, in ADR-002's order; (4) Outbound — the per-connection writers flush (priority path, R-U batch, cycle broadcast, position packet). Each step runs in its own `try`/`catch` that logs a server error and continues. `ServerTickLoop` then fires TTL timers and the test observer; a message produced by a TTL callback leaves on the next tick — source: ADR-014, ADR-002, ADR-011, ADR-015
- Game systems that tick (Enemy AI, combat, status effects, loot auction timers, the server-originated bag mutators retrying after `OnGateOpened`) run in phase 3, in an order the composition root lists explicitly — source: ADR-014
- Enforcement: a test enumerates `RpcTypeTag` and asserts that no member makes `Evaluate` throw; a source search finds `ICharacterMutationGate` read only in the dispatcher and in the server-originated bag mutators ADR-011 names, and no subscription to `OnGateOpened` in the dispatcher; a source search finds `RegisterTickDriven(` called only by the composition root, for `ZoneTickPipeline`; the pipeline test shows Drain → Relay → Pass A → Pass B → simulation → outbound — source: ADR-014, ADR-015

**Cross-zone message relay — PostgreSQL `LISTEN` / `NOTIFY` (ADR-015):**
- Game code sends a server-wide (`S→ALL`) message through one service, `IServerWideBroadcaster.Broadcast(messageTypeId, body)`, on the tick thread. It (1) enqueues `body` for every client of this zone through `IZoneClientBroadcastSink.EnqueueToAllClients(messageTypeId, body, isCapExempt: false)` — implemented by `ClientMessageOutbox`, which calls its own `Enqueue` for each client added to it — then (2) calls `ICrossZoneRelayPublisher.Publish(messageTypeId, body)`. A failure of step 2 is logged and changes nothing in step 1 or for the caller. `Broadcast` never throws — source: ADR-015
- `Broadcast` refuses (logs an error, sends nothing, returns false) a `messageTypeId` not on `CrossZoneRelayAllowlist`. The recipient set is every client present in the zone's outbox; it is never derived from `MessageDirection` alone — source: ADR-015
- `NpgsqlRelayPublisher` runs `SELECT pg_notify(@channel, @payload)` through the zone's ADR-006 `NpgsqlDataSource`: `dataSource.CreateCommand(sql)` then `ExecuteNonQueryAsync(token)`, so the pooled connection is opened inside the same task and returned when it ends. The task is handed to `ITickCompletionQueue.Track` with a `CancellationTokenSource` that cancels after `RELAY_PUBLISH_TIMEOUT_MS` (the command timeout is the same value). The completion callback logs a failure or timeout, releases the in-flight slot and disposes the CTS — source: ADR-015, ADR-011
- Publishes in flight are bounded by `RELAY_MAX_PUBLISHES_IN_FLIGHT`; a publish over the bound is dropped and logged. The notification is never sent inside the persistence transaction of the commit — source: ADR-015
- `NpgsqlRelayListener` owns one dedicated `NpgsqlConnection` and one dedicated `Thread` (`IsBackground = true`). Its connection string states `Pooling=false` and `Multiplexing=false` and uses `Tcp Keepalive=true`, not Npgsql's protocol-level `Keepalive`. The thread opens the connection, runs `LISTEN irongrind_relay`, and loops on `connection.Wait(RELAY_LISTENER_WAIT_MS)` until stop is requested — source: ADR-015
- The `Notification` handler (on the listener thread) parses the envelope and drops a payload that is longer than the longest valid envelope, malformed, of an unknown version, not on the allowlist, over the type's `MaxBodyBytes`, or from this process's own `originId`; otherwise it copies the body into `CrossZoneRelayInbox`. It does nothing else — source: ADR-015, ADR-010
- The listener loop body catches every exception type; none escapes the thread. On a failure it disposes the connection, logs a warning (an error after `RELAY_RECONNECT_ALERT_AFTER` consecutive failures), waits with exponential backoff from 500 ms to `RELAY_RECONNECT_MAX_BACKOFF_MS` (cut short by stop), and reconnects and re-issues `LISTEN` — source: ADR-015
- `CrossZoneRelayInbox` is a bounded thread-safe queue of `RELAY_INBOX_CAPACITY` entries; the listener is the only writer, tick step 1a the only reader. An entry over capacity is dropped and counted, logged once per tick. `Drain` hands every entry present at the call, in arrival order — source: ADR-015
- `NpgsqlRelayListener.Stop()` sets the stop flag and joins the thread for at most `RELAY_LISTENER_STOP_TIMEOUT_MS`; a thread that does not stop in time is logged. It is idempotent and is called from (a) ADR-009 zone teardown step 2a, after the tick loop has stopped, and (b) `OnDestroy` of the server host `MonoBehaviour` that owns the zone composition root — ADR-009 teardown does not run when the Editor leaves Play mode. Only the listener thread disposes its connection, on its way out — source: ADR-015, ADR-009
- `CrossZoneRelayAllowlist` is a sealed `MessageTypeId → MaxBodyBytes` table built by the composition root, checked on send and on receive. It holds one row: `ServerBroadcast_Enhancement9`, 52 bytes. A row is added only by a story that names the GDD rule requiring cross-zone delivery — source: ADR-015
- Relay envelope: ASCII `v1:{messageTypeId}:{originId}:{base64(body)}` — `messageTypeId` decimal `ushort`; `originId` 16 hex digits, a random 64-bit value drawn once at process start from `System.Security.Cryptography.RandomNumberGenerator`; `body` exactly as `ClientMessageOutbox.Enqueue` takes it — source: ADR-015, ADR-013
- Delivery is at most once and best-effort, with no order guaranteed (not even from one sender), no replay and no acknowledgement. A zone receives its own notifications and drops them by `originId` — source: ADR-015
- `ServerWideBroadcaster`, `NpgsqlRelayPublisher`, `NpgsqlRelayListener`, `CrossZoneRelayInbox`, `CrossZoneRelayAllowlist` and `RelayEnvelopeCodec` are `IronGrind.ServerLogic`. EditMode and PlayMode tests use `InMemoryCrossZoneRelayBus`, which connects the publishers and inboxes of several in-process test zones; `NpgsqlRelayListener` is created only by the server composition root of a running zone — source: ADR-015, ADR-012
- Relay tuning knobs are server config, not hardcoded: `RELAY_CHANNEL_NAME` (`irongrind_relay`), `RELAY_LISTENER_WAIT_MS` (500), `RELAY_RECONNECT_MAX_BACKOFF_MS` (10 000), `RELAY_RECONNECT_ALERT_AFTER` (5), `RELAY_INBOX_CAPACITY` (64), `RELAY_MAX_PUBLISHES_IN_FLIGHT` (8), `RELAY_PUBLISH_TIMEOUT_MS` (2 000), `RELAY_LISTENER_STOP_TIMEOUT_MS` (2 000) — source: ADR-015

**Server random provider (ADR-013):**
- Every piece of server code that needs a random number takes an `IRandomProvider`: `float NextFloat()` — uniform in [0.0f, 1.0f), `0.0f` is a possible result, `1.0f` is not; `double NextDouble()` — uniform in [0.0, 1.0); `int NextInt(int minInclusive, int maxExclusive)` — uniform in [`minInclusive`, `maxExclusive`), returns `minInclusive` when the two are equal, throws `ArgumentOutOfRangeException` when `minInclusive > maxExclusive` — source: ADR-013
- The interface and its production implementation are in the namespace `IronGrind.Randomness`, folder `src/ServerLogic/Randomness/`, assembly `IronGrind.ServerLogic`. The namespace is added to `ServerOnlyNamespaces` in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`, so the boundary test fails if one of these types appears in `Foundation` or `Client` — source: ADR-013, ADR-012
- A member is added to the interface only when a GDD rule needs a draw the three above cannot express without bias or rounding. Convenience members (pick from a list, shuffle, weighted choice) are static helpers that take an `IRandomProvider` — source: ADR-013
- `SystemRandomProvider` is a sealed class that wraps one `System.Random` handed to its constructor (null → `ArgumentNullException`). `NextFloat()` is `ToUnitFloat(_random.Next())` — one call to the underlying generator per draw; the public static `ToUnitFloat(int sample)` returns `(sample >> 7) * (1f / 16777216f)` and throws `ArgumentOutOfRangeException` for a negative `sample`. `NextDouble()` returns `_random.NextDouble()` unchanged; `NextInt(min, max)` returns `_random.Next(min, max)` unchanged — source: ADR-013
- A caller whose threshold is a `float` either draws `NextFloat()`, or widens the threshold to `double` and draws `NextDouble()` — source: ADR-013
- Injection: a class that rolls takes `IRandomProvider` as a constructor parameter and stores it in a `private readonly` field (a null argument throws `ArgumentNullException`); a static method that rolls takes it as a parameter (`LootDropRoller.Roll(table, random)`); it is not a parameter of a public gameplay method on an instance — source: ADR-013
- The server composition root creates exactly one `IRandomProvider` at startup by calling `RandomProviderFactory.CreateSeededFromEntropy(out int seed)` and passes that same instance to every system it constructs — source: ADR-013
- `RandomProviderFactory` replaces `LootRandomFactory`: it draws a 32-bit seed from `RandomNumberGenerator.Create()`, logs `[Random] PRNG seed: {seed}` once with `UnityEngine.Debug.Log`, and returns `new SystemRandomProvider(new System.Random(seed))` — source: ADR-013
- The provider is drawn from the game-logic thread only (tick code, in ADR-011's terms). It has no lock. If a persistence continuation, a thread-pool callback or a background job needs an outcome, the tick draws it first and passes the value in — source: ADR-013, ADR-011
- Each roll is one independent draw. Each consumer's GDD states how many draws one operation makes, and its tests assert that count — source: ADR-013
- Values that must be unpredictable to an attacker (session tokens, authentication nonces, anything in `networking-session-token.md` or `authentication.md`) use `System.Security.Cryptography.RandomNumberGenerator` — source: ADR-013
- The startup log that carries the seed is treated like a credential until the generator is replaced: not shipped to a third-party log service, not pasted into bug reports, readable by operators only — source: ADR-013
- Tests implement `IRandomProvider`. Two shared doubles live in `tests/EditMode/Randomness/`: `ScriptedRandomProvider` (three queues filled with `EnqueueFloat`, `EnqueueDouble`, `EnqueueInt`; each `Next…` call dequeues from its own queue and returns the value unchanged; a draw from an empty queue throws `InvalidOperationException`; `DrawCount`, `FloatDrawCount`, `DoubleDrawCount`, `IntDrawCount`) and `RecordingRandomProvider` (wraps another `IRandomProvider`, forwards every call, keeps the last value returned by each method plus the same counters) — source: ADR-013
- Test rules: a test that needs a pseudo-random sequence builds `new SystemRandomProvider(new System.Random(SEED))` with `SEED` as a named constant; "guaranteed crit" and similar setups script the roll (`EnqueueFloat(0.0f)`) and do not rely on a stat of 1.0; a test that does not care about the roll still supplies a double; for paths that must not draw, an empty `ScriptedRandomProvider` is the assertion; seeded tests assert properties (range, rate within a band), never specific values — source: ADR-013
- Enforcement: the `System.Random` type rule (see Forbidden Approaches) is asserted by a reflection test in `tests/EditMode/Architecture/`, beside the ADR-012 boundary test, from Migration Plan step 3. The other bans are calls inside method bodies and are checked by a source search in code review; the search must match the unqualified name `Random` too (`LootTableService` and `LootDropRoller` write `Random` under `using System;`) — source: ADR-013
- Migration status: `src/ServerLogic/Randomness/` and `tests/EditMode/Randomness/` are created by Damage Calculation Story 003 (Migration Plan step 1); Loot Table migrated in step 2 and Enhancement in step 3 (Enhancement Story 014, 2026-10-09). Steps 1–3 are done: the `System.Random` type rule has no exception outside `IronGrind.Randomness` — source: ADR-013

**Hosting backend (ADR-007):**
- Unity 6.3 IL2CPP headless server processes on Ubuntu 22.04 LTS (Hetzner VPS) — source: ADR-007
- PostgreSQL co-located on same machine; connect via loopback `127.0.0.1:5432` — source: ADR-007
- Zone processes managed as systemd services (`irongrind-zone-{N}.service`, `Restart=always`) — source: ADR-007
- Gateway process on port 443 (TLS) for auth + zone routing; clients receive `zoneServerPort`, then connect via direct NGO UDP — source: ADR-007
- `NetworkTransform.Update()` override must be migrated to `NetworkTransform.OnUpdate()` before first NGO 6.3 build — source: ADR-007

**Scene/zone-load management (ADR-009):**
- Server zone scene: `SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single)` — source: ADR-009
- Use `yield return op` coroutine (NOT `await`) — guarantees `Awake()`/`Start()` complete before continuation in headless build — source: ADR-009
- Cache loaded scene as `Scene` struct — NEVER as `int` handle (`Scene.handle` changed from `int` to `SceneHandle` in Unity 6.3) — source: ADR-009
- Cache `NavMeshDataInstance` as a field on load; use the same instance for teardown — cannot be re-acquired — source: ADR-009
- Composition root: one `ZoneServerHost` `MonoBehaviour` (in `IronGrind.ServerLogic`, in a scene only the Dedicated Server build includes) owns one `ZoneServerComposition`, a plain C# class. The host holds no game logic: it forwards `Start` (coroutine), `Update`, `OnDestroy` and `Application.wantsToQuit` to the composition. `ZoneServerComposition` is the only place that constructs server systems, the `IRandomProvider`, the `NpgsqlDataSource`, the relay and the `ZoneTickPipeline`; it owns them and releases them in the teardown Dispose pass. ADR-010's `IZoneStartupService.StartZone()` is `ZoneServerComposition.Start` — source: ADR-009 Amendment 1 Decision 5, ADR-010 (note 2026-10-19)
- `ZoneServerComposition` reaches NGO, the scene load, NavMesh and `Application` through small seams, so EditMode tests can run startup order, startup failure at each step, teardown order and idempotence without NGO, a scene or a database; the database part (S4) is tested in the explicit `Database` category — source: ADR-009 Amendment 1 Decision 5
- Server startup sequence (strict order): **S0** command line (`-zoneTemplate`, `-zoneId`, `-port`) and server config → **S1** `Application.wantsToQuit += OnWantsToQuit`; `DontDestroyOnLoad(host.gameObject)` (a root object); `NetworkManager` on a root object outside the zone scene with `DontDestroy = true` and `NetworkConfig.EnableSceneManagement = false` → **S2** static data loaded and validated (ItemDatabase, ClassRegistry, LootTableRegistry, DamageCalculationConfig, EnhancementConfig, later static config) → **S3** the one `IRandomProvider` (`RandomProviderFactory.CreateSeededFromEntropy`) → **S4** `NpgsqlDataSource` + warm-up `Task.Run(() => dataSource.CreateCommand("SELECT 1").ExecuteNonQueryAsync(token)).Wait(DB_WARMUP_TIMEOUT_MS)` → **S5** construction without I/O (queue, tick loop, store, persistence service, game systems, relay, dispatcher, outbox, broadcaster, relay handler, `ZoneTickPipeline`, then the one `tickLoop.RegisterTickDriven(pipeline.Tick)`) → **S6** `LoadSceneAsync` → `NavMesh.AddNavMeshData` → `ZoneNavigationService.Initialize` → `ZoneBoundsValidator.Validate` → **S7** `NpgsqlRelayListener.Start()` → **S8** `NetworkConfig.TickRate = (uint)ServerTickLoop.TICK_RATE_HZ`, `Application.targetFrameRate = TICK_RATE_HZ`, `QualitySettings.vSyncCount = 0`, `NetworkManager.StartServer()` (false → fail), inbound transport shell and outbound sender started → **S9** `zone.State = Active`; `NetworkManager.NetworkTickSystem.Tick += OnNetworkTick` (→ `tickLoop.AdvanceTick`, one call per NGO tick event; subscribe only after `StartServer`, which creates the tick system) → **S10** `Gateway.RegisterZone(zoneId, port)` last — source: ADR-009 Amendment 1, ADR-013, ADR-014, ADR-015
- Startup failure (S-fail): log the failing step (never the connection string), tear down from the first step that has something to undo (skip the drain if no tick ran), exit with `ZoneExitCode.StartupFailed`; no retry inside the process. A quit request during startup stops it at the next step boundary and tears down with `ZoneExitCode.Clean` — source: ADR-009 Amendment 1
- Startup coroutine: each step S0–S10 is a non-yielding method called inside its own `try`/`catch`; the coroutine yields only between steps (S6's scene load is the only real wait). A caught exception goes to S-fail — source: ADR-009 Amendment 1
- Systems not built yet (`ZoneNavigationService`, `ZoneBoundsValidator`, the zone `State` owner, the gateway client) are taken through interfaces (`IZoneNavigation`, `IZoneBoundsValidator`, `IGatewayRegistration`); until their epics ship, stand-ins log `"[ZoneServer] <step> not implemented"` once each and do nothing — source: ADR-009 Amendment 1
- Zone teardown: a trigger (zone `Closed`, a quit request, a fatal error) only calls `RequestTeardown(code)`, which records the first code. The host's `Update` starts the teardown coroutine once. Strict order: **1** `NetworkTickSystem.Tick -= OnNetworkTick` → **2a** `NpgsqlRelayListener.Stop()` → **3** `ZoneNavigationService.Teardown()` (CR-NAV-3) → **4** CR-ZI-12 steps 1–8 → **4b** `outbound.Flush()` once → **5** `queue.DrainOnShutdown(watchdog duration)` → **6** inbound shell `Stop()`, `NetworkManager.Shutdown()`, yield while `NetworkManager != null && ShutdownInProgress` for at most `NGO_SHUTDOWN_WAIT_FRAMES` (warning at the cap) → **7** Dispose pass over everything S5 built, in reverse order, `dataSource.Dispose()` last → **8** `quitAllowed = true; Application.Quit((int)code)`. Every step null-guards what it touches (in the Editor `NetworkManager.OnDestroy` can run first) — source: ADR-009 Amendment 1, ADR-002, ADR-011, ADR-015
- `OnWantsToQuit` returns `true` in the Editor and once step 8 has set `quitAllowed`; otherwise it calls `RequestTeardown(ZoneExitCode.Clean)` and returns `false` — source: ADR-009 Amendment 1
- The host's `OnDestroy` runs teardown steps 1–7 synchronously (no yield in step 6, no step 8), unless the teardown already finished — source: ADR-009 Amendment 1, ADR-015
- `ZoneExitCode`: `Clean = 0` (zone closed, or a quit request such as SIGTERM), `StartupFailed = 1`, `Fatal = 2` (an unhandled failure the host catches at runtime). Tasks still incomplete after the drain are logged and alerted and do not change the code — source: ADR-009 Amendment 1
- `[SerializeField]` on **private fields only** — compile error on properties in Unity 6.3 — source: ADR-009
- `link.xml` must preserve `UnityEngine.SceneManagement` for IL2CPP headless server builds — source: ADR-009
- Client loading overlay: UI Toolkit `VisualElement` with `PickingMode.Position` — no URP custom rendering pass — source: ADR-009

**Event/messaging architecture (ADR-010):**
- All cross-system communication uses exactly one of three tiers: (1) direct method call on injected interface, (2) C# `event Action<T>` with `readonly struct` arg, (3) NGO RPCs/custom messages — source: ADR-010
- Service interface naming: `I[SystemName]Service` (grandfathered exceptions: `ICharacterPersistence`, `INavigationProvider`) — source: ADR-010
- Server-side systems wired at zone startup by `ZoneServerComposition.Start` (ADR-010's `IZoneStartupService.StartZone()`; no interface of that name exists) — source: ADR-010, ADR-009 Amendment 1
- All server-side event subscribers must implement `IDisposable`; `Dispose()` unsubscribes from all events — source: ADR-010
- All zone-scoped services must implement `IZoneScopedService : IDisposable` — source: ADR-010
- NGO message handlers enqueue to `Queue<T>` — never call game-logic methods directly. For client requests the queue is the dispatcher's inbox: the receive callback calls `IInboundMessageIntake.TryAccept`, and ADR-010's example of one `Queue<T>` per handler is replaced (ADR-014 Decision 7) — source: ADR-010, ADR-014

### Forbidden Approaches

- **Never use `(charId, requestId)` alone as the dedup key** — cross-type collision between NPC Shop and Consumable Use System within the 300s dedup window — source: ADR-001 Amendment A1
- **Never use batched `GoldSyncEvent` delivery to fix purchase integrity** — wrong layer (client display symptom), not the server-side gold loss problem — source: ADR-001
- **Never use full two-phase commit for shop purchase integrity** — over-engineering for MVP; `PendingPurchase` reconciliation achieves the same safety guarantee — source: ADR-001
- **Never use `NetworkVariable<T>` or source-generated RPC serialization for authoritative gameplay state hot path** — imposes NGO delta/replication framing that conflicts with the CR-NET-7 wire contract — source: ADR-004
- **Never use Mirror networking library** — not officially supported on Unity 6.x; adopting it invalidates approved CSP rules CR-CSP-3/21/EC-CSP-4 which name `NetworkManager.ServerTime.Tick` — source: ADR-004
- **Never use Photon Fusion or PUN** — transport-layer vendor lock-in with no offsetting benefit — source: ADR-004
- **Never use Entity Framework Core for character persistence** — IL2CPP reflection overhead, migration risk, overkill for a stable 23-field schema — source: ADR-006
- **Never use SQLite for the persistence store** — WAL mode single-writer limit conflicts with multi-zone concurrent character saves; migration post-launch is costly — source: ADR-006
- **Never maintain dual SQLite (dev) / PostgreSQL (prod) implementations** — SQL dialect differences create test-vs-production divergence — source: ADR-006
- **Never use Unity Relay for dedicated server routing** — designed for P2P NAT traversal; a public-IP dedicated server uses direct UDP — source: ADR-007
- **Never use AWS GameLift or Agones at MVP scale** — disproportionate cost and complexity for 1–4 zone instances — source: ADR-007
- **Never use Multiplay Hosting** — shut down 2026-03-31 — source: ADR-007
- **Never override `NetworkTransform.Update()`** — removed in NGO 6.3; use `NetworkTransform.OnUpdate()` — source: ADR-007
- **Never cache `Scene.handle` as `int`** — type changed to `SceneHandle` in Unity 6.3; causes `MissingFieldException` at runtime in precompiled assemblies — source: ADR-009
- **Never use `[SerializeField]` on properties** — compile error in Unity 6.3 — source: ADR-009
- **Never use `LoadSceneMode.Additive` on the zone server** — ADR-007 closes hot-swap (one process per zone); ADR-002 closes multi-zone (NavMesh is process-global) — source: ADR-009
- **Never use `SetupRenderPasses` in any URP `ScriptableRendererFeature`** — removed in Unity 6.3; use `AddRenderPasses` + `RecordRenderGraph` — source: ADR-009
- **Never load NavMeshData via `Resources.Load`** — synchronous; blocks the main thread — source: ADR-009
- **Never use a dedicated "LoadingScreen" Unity scene for client zone transitions** — three-scene memory peak + URP custom pass complexity; use a UI Toolkit overlay instead — source: ADR-009
- **Never end the zone process with `Process.Exit` or `Environment.Exit`** — `Process.Exit` is not a .NET API, and either skips the drain and the Dispose pass; the exit call is `Application.Quit((int)code)` at teardown step 8 — source: ADR-009 Amendment 1
- **Never run the teardown inside a tick, a completion callback or the `Application.wantsToQuit` handler, and never use `OnApplicationQuit` as the teardown path** — teardown stops the tick and blocks on the drain; triggers only call `RequestTeardown(code)` — source: ADR-009 Amendment 1, ADR-011
- **Never return `true` from `OnWantsToQuit` in a player before teardown step 8** — the process would exit without the drain and lose the last saves — source: ADR-009 Amendment 1
- **Never construct a server system, an `IRandomProvider`, an `NpgsqlDataSource`, the relay or the `ZoneTickPipeline` outside `ZoneServerComposition`** — a second generator or data source breaks ADR-013 Decision 4 and the pool budget, and the Dispose pass misses it (tests construct directly) — source: ADR-009 Amendment 1, ADR-013, ADR-006 Amendment 3
- **Never retry a failed startup step inside the zone process** — undo, exit `StartupFailed`; supervision decides — source: ADR-009 Amendment 1, ADR-007
- **Never log the database connection string or put the password in server config** — the password comes from `IRONGRIND_PG_PASSWORD` — source: ADR-006 Amendment 3, ADR-009 Amendment 1
- **Never `yield return` inside a `try` that has a `catch` in the startup coroutine** — C# forbids it, and a throwing coroutine is abandoned by Unity, leaving a hung process; each step is a non-yielding method in its own `try`/`catch` — source: ADR-009 Amendment 1
- **Never subscribe to `NetworkManager.NetworkTickSystem.Tick` before `StartServer()`** — the tick system is created by the start — source: ADR-009 Amendment 1
- **Never use a central EventBus/MessageBus singleton** — per-emit allocation; hidden dependencies; proliferates rapidly once introduced — source: ADR-010
- **Never use `UnityEvent` for server-side game logic** — requires `MonoBehaviour`; not available in the headless server build — source: ADR-010
- **Never use `Action<object>` or class-typed `Action<T>` event args** — boxes struct args; per-emit heap allocation on the 20Hz server tick path — source: ADR-010
- **Never use lambda captures for persistent event subscriptions** — not unsubscribeable by reference; leaks service references across zone teardown — source: ADR-010
- **Never call game-logic methods directly from `ServerRpc` or NGO message handlers** — bypasses the single-threaded main-loop execution guarantee; all game logic must run in the tick loop — source: ADR-010
- **Never use shared mutable state polling between systems** — use C# events for low-frequency broadcasts — source: ADR-010
- **Never use `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith` or `async void` in tick-driven server code** — a continuation resumes when Unity pumps its synchronization context, not at a defined point of the tick; start the `Task` and pass it to `ITickCompletionQueue.Track` in the same statement — source: ADR-011
- **Never block the tick on a persistence write** — one write would stall every player in the zone for up to a full tick, and blocking the main thread on a task can deadlock; the shutdown drain is the only blocking wait in the server — source: ADR-011
- **Never use the synchronous `CommitBeforeBroadcastSequencer.Execute` for a production caller of `SaveIrreversibleOutcome`** — it remains only for a persist step that is genuinely synchronous — source: ADR-011
- **Never read or write live game state after the first `await` in the persistence layer** — game state is single-threaded; copy what is needed in the synchronous prefix, on the tick thread — source: ADR-011
- **Never `await` in the persistence layer without `ConfigureAwait(false)`** (including `await using` and `await foreach`), and never create a `TaskCompletionSource` there or in a test fake without `TaskCreationOptions.RunContinuationsAsynchronously` — a captured Unity context is invisible in EditMode tests and stalls or delays writes on the server — source: ADR-011
- **Never use `UnityEngine.Awaitable` for tick logic** — it resumes at player-loop points, not at a point of `ServerTickLoop` — source: ADR-011
- **Never retry a failed irreversible write** — any non-success code, fault, cancellation or watchdog timeout takes the failure branch once — source: ADR-011 (CR-NET-5.5)
- **Never rely on the `[Server]` attribute or on IL2CPP managed stripping to keep server code out of the client** — `[Server]` blocks execution, not shipping; stripping keeps anything reachable — source: ADR-012
- **Never wrap a type that must not ship to the client in `#if UNITY_SERVER`** — move it to `IronGrind.ServerLogic`; `#if UNITY_SERVER` stays legal only for small differences inside `Foundation`, and not in code under test — source: ADR-012
- **Never use a bare `UNITY_SERVER` define constraint** — it removes the assembly from the Editor, and with it the EditMode suite — source: ADR-012
- **Never use asmdef platform include/exclude lists for the server/client boundary** — there is no Dedicated Server platform name, and the server and a desktop client share a platform — source: ADR-012
- **Never use `InternalsVisibleTo` between production assemblies** — the member becomes public, or the type is in the wrong assembly — source: ADR-012
- **Never put a rule, a formula or a constant in an RPC body or in message-handling code in `Foundation`** — the body ships in the client binary whatever its target; it contains nothing but enqueueing through a `Foundation` interface — source: ADR-012
- **Never add a server-only `NetworkBehaviour` to a prefab the client also instantiates** — NGO addresses behaviours by index on the `NetworkObject`, so both sides need the same component list — source: ADR-012
- **Never reference a `MonoBehaviour`, `ScriptableObject` or `[SerializeReference]` type of `IronGrind.ServerLogic` from a scene, prefab, Addressables group or asset in a client build; never a `IronGrind.Client` one from a scene or asset the Dedicated Server loads; never a `IronGrind.DevHarness` one from either** — it serializes without error in the Editor and is a missing script at runtime — source: ADR-012
- **Never build server logic as a separate project or precompiled DLL** — cost out of proportion for a solo project; the define-constrained assembly gives the same guarantee for the binary — source: ADR-012
- **Never use `BuildReport.packedAssets`, `CompilationPipeline.GetAssemblies` or the `Library/Bee` folders for the client-binary scan** — assets not assemblies; the Editor's active target, not the build in progress; internal layout — source: ADR-012
- **Never hard-code the path of an `.asmdef` in the boundary test** — a `file:` package has no real `Packages/com.irongrind.src/` folder on disk — source: ADR-012
- **Never let a PlayMode test assembly reference `IronGrind.ServerLogic` unless it is Editor-only or carries the same define constraint** — it fails to compile for a client player — source: ADR-012
- **Never distribute a client build before the move stories are complete** — until a system's move is done its code is still in the client build (the move stories were completed on 2026-10-08; AC-DC-I-01 and the second sentence of AC-CS-G-01 stay open until the client-binary scan, ADR-012 Decision 6 check 2) — source: ADR-012
- **Never use `UnityEngine.Random` in `IronGrind.ServerLogic`, in any form** — global state, not injectable — source: ADR-013
- **Never use a static or ambient accessor for the random provider** (`RandomProvider.Current`, a singleton, a service locator) — hidden dependency; tests must set and restore global state, which breaks test isolation — source: ADR-013
- **Never write `new System.Random(...)` in `IronGrind.ServerLogic` anywhere except `RandomProviderFactory`** — source: ADR-013
- **Never use `System.Random` as the type of a field, constructor parameter or method parameter in `IronGrind.ServerLogic`, except inside `SystemRandomProvider` and `RandomProviderFactory`** — holds since ADR-013 Migration Plan step 3 was completed (2026-10-09); there are no other exceptions — source: ADR-013
- **Never cast the result of `NextDouble()` to `float`** — a double just under 1.0 becomes `1.0f`, and a double just under 0.75 becomes `0.75f`, which turns a crit into a non-crit under the strict `<`; a redraw breaks "exactly one draw per call" — source: ADR-013
- **Never create a generator per system or per entity, re-seed, or derive a child generator from the process one** — one generator per zone process — source: ADR-013
- **Never draw from a persistence continuation, a thread-pool callback or a background job** — `System.Random` is not thread-safe and corrupts its state silently under concurrent use — source: ADR-013, ADR-011
- **Never store a previous draw, a draw counter, a streak or a "pity" value that changes later odds; never store random-number state on an entity, on a character, in a session, in the character record or in a zone-transfer handoff** — nothing about randomness is persisted — source: ADR-013
- **Never use `IRandomProvider` for a value that must be unpredictable to an attacker** — it is a seeded, reproducible generator — source: ADR-013
- **Never subclass `System.Random` as a test double** — each double silently inherits real random behaviour for the methods it does not override — source: ADR-013
- **Never call `RandomProviderFactory.CreateSeededFromEntropy` from a test**, except the one test of the factory itself, which asserts the log line and nothing about the values — source: ADR-013
- **Never assert a specific value sequence in a seeded test** — .NET does not guarantee the `System.Random` algorithm across runtime versions — source: ADR-013
- **Never make a public release, or a build in which Enhancement outcomes have real value to players, without a `security-engineer` verdict (`/security-audit`) on the predictable-generator risk** — source: ADR-013
- **Never give a system its own queue for client requests, drained in its own `Tick()`** — arrival order across message types is lost, and every system would have to repeat the guard chain, the gate and the hold rule — source: ADR-014
- **Never put guard, gate or hold logic in the transport's message handlers, and never use a transport type in the dispatcher** — the logic must be testable in EditMode with no network session, and the transport API is still open (ADR-004 OQ-ADR4-3) — source: ADR-014
- **Never run the guard chain at receipt (in the adapter or in `TryAccept`)** — guard state is read at one fixed point of the tick; the inbox bound covers the flood case — source: ADR-014
- **Never hold a request that a guard rejected** — it is dropped — source: ADR-014
- **Never deduplicate in the dispatcher or read a `requestId` there** — the systems own their dedup state — source: ADR-014
- **Never add a per-tick dispatch budget with a carried backlog** — the inbox is bounded per connection and emptied every tick — source: ADR-014
- **Never subscribe the dispatcher to `ICharacterMutationGate.OnGateOpened`** — a subscriber that throws stops the ones after it, so an opening could be missed and the character's bag requests held for the rest of the session; Pass A polls `IsHeld` — source: ADR-014
- **Never call game logic from an `IConnectionMessageSink` or the `IConnectionActivitySink`** — they run inside the receive callback, outside the tick — source: ADR-014, ADR-010
- **Never register a held type that is U-U, carries a rate-limited `RpcTypeTag`, or has `MaxBodyBytes` above `MAX_HELD_BODY_BYTES`** — the guard spends the rate-limit slot when it accepts, and a request then dropped by a full hold queue would have spent it for nothing; the hold pool entries are 64 bytes — source: ADR-014
- **Never apply the stale-sequence check to an R-OD type, and never keep one highest-seen `SequenceNumber` per connection** — `SequenceNumber` is one counter per connection shared by all types (CCR-1), so a U-U packet would make a retransmitted R-OD request, or a U-U request of another type, look stale — source: ADR-014
- **Never keep `body` after a handler returns, `await` in a handler, or read `ICharacterMutationGate` from a handler** — the inbox arena is reused by the next tick; the gate has one enforcement point — source: ADR-014, ADR-011
- **Never call `ServerTickLoop.RegisterTickDriven` for game logic** — the composition root calls it once, for `ZoneTickPipeline` — source: ADR-014
- **Never use the transport's own tick counter for `ArrivalTick` or `DispatchTick`** — source: ADR-014
- **Never free storage in `RemoveConnection`** — it can be called from inside a pass (a disconnect raised synchronously by a handler or a completion callback); storage is released at the start of the next `DispatchTick` or by an earlier `AddConnection` — source: ADR-014
- **Never allocate per request on the intake or dispatch path, and never rent its buffers from `ArrayPool<T>.Shared`** — buffers are pre-allocated per zone — source: ADR-014
- **Never relay through the gateway process, a message broker (Redis pub/sub, NATS) or a polled relay table** — the gateway is not built and its link would be rebuilt on scale-out; a broker is a new service and IL2CPP-unproven library for a few messages per hour; polling loads every zone continuously for a durability the design declined — source: ADR-015
- **Never deliver to the producing zone's own clients only from its own notification** — local delivery would then depend on the database and a relay failure would silence players next to the smith; local delivery goes through the outbox first — source: ADR-015
- **Never issue `pg_notify` inside the persistence transaction of the commit** — couples persistence to broadcasts; the commit-success callback already gives "only after the commit" — source: ADR-015
- **Never relay a request, a reply to one client, state for a game system to apply, or a call between systems, and never add an allowlist row without a story naming the GDD rule** — the relay carries already-encoded `S→ALL` bodies only and must not become a cross-system bus — source: ADR-015, ADR-010
- **Never call game code from the relay listener or its `Notification` handler** — it runs on a background thread; game state is main-thread only; the tick drains the inbox — source: ADR-015, ADR-010
- **Never open a database connection synchronously on the tick for a publish (`Open()`, `ExecuteNonQuery`), and never await the publish** — on an exhausted pool `Open()` blocks the tick up to the 5 s connection timeout; the open happens inside the tracked task — source: ADR-015, ADR-011
- **Never run the listener on a thread-pool thread or on a pooled or multiplexed connection, and never use Npgsql's protocol-level `Keepalive` on it** — multiplexing breaks `LISTEN`; keepalive probes contend with the blocking read in `Wait` — source: ADR-015
- **Never dispose the listener connection from `Stop()` or any thread other than the listener's** — it may be blocked in `Wait` — source: ADR-015
- **Never draw the relay `originId` from `IRandomProvider`** — it would consume a game draw; use `RandomNumberGenerator` — source: ADR-015, ADR-013
- **Never create `NpgsqlRelayListener` in an EditMode or PlayMode test** — a listener thread could outlive the test or a domain reload; use `InMemoryCrossZoneRelayBus` — source: ADR-015

### Performance Guardrails

- **Persistence write**: warning ≥ 50ms; critical ≥ 100ms; hard timeout = 5s (`PERSISTENCE_WRITE_TIMEOUT_SECONDS`) — source: ADR-006
- **Persistence result handling**: the result of a persistence call is handled by `Drain` on the first tick after its task completes (normal case 50–100 ms after the request at 20 Hz); the tick never waits on I/O — source: ADR-011
- **`ITickCompletionQueue.Drain`**: O(tasks in flight) per tick — at most one per character in the zone; no allocation when nothing is in flight — source: ADR-011
- **Persistence watchdog**: `PERSISTENCE_WATCHDOG_TICKS = 200` (10 s) — the backstop above the 5 s `PERSISTENCE_WRITE_TIMEOUT_SECONDS`, which fires first — source: ADR-011, ADR-006
- **Held requests**: `MAX_HELD_REQUESTS_PER_CHARACTER = 16`; a request arriving at a full queue is dropped and logged (anomaly `HeldRequestOverflow`) — source: ADR-011, ADR-014
- **Inbound bounds**: `MAX_INBOX_REQUESTS_PER_CONNECTION = 64`, `MAX_INBOX_BYTES_PER_CONNECTION = 4096`, `MAX_INBOUND_MESSAGE_BYTES = 400` (14-byte envelope with `SenderEntityID` plus the 386-byte `PartyChatRequest` body), `MAX_HELD_BODY_BYTES = 64`. The inbox bounds are per tick interval, not per frame; all are tuning knobs — source: ADR-014
- **Inbound dispatch memory**: per zone, allocated once — inbox arena 205 KB (50 × 4,096 B), inbox records about 100 KB (3,200 entries), hold pool about 70 KB (50 × 16 entries) — source: ADR-014
- **Inbound dispatch CPU**: per request one guard evaluation, one directory lookup, one gate lookup and one delegate call, with no allocation; Pass A scans at most 50 characters; worst case 50 × 64 = 3,200 requests per tick if every client floods to the bound; at intake, one activity-sink call per message — source: ADR-014
- **Transport adapter — engine behaviour** (ADR-014 Verification 1–6; status after the Networking Core Story 038 and 039 runs, Editor, 2026-10-12): (1) with the tick driven from `Update`, no unnamed-message handler ran inside the tick; a server-initiated `DisconnectClient(id, null)` can still raise the disconnect callback inside the tick (Decision 4 is written for that); (2) the reader is disposed when the handler returns — the adapter copies with `ReadBytesSafe(ref byte[], int, int)` into one pre-allocated array after checking `reader.Length - reader.Position`: 0 `GC.Alloc` in the handler; a short or oversize message is dropped and never throws; (3) client ids 1, 2, 3… with no reuse after a disconnect or a `Shutdown()`/`StartServer()`; the `uint.MaxValue` drop stays; (4) NGO 2.13.3 with Unity Transport 2.6.0 resolves on 6000.3 and the receive API compiles as used; (5) 200 `ReliableSequenced` messages of one client arrived in order, each once (loopback); (6) after `DisconnectClient(id)` queued messages of that client still arrive in the same frame — the adapter drops messages of a removed connection. **Still pending:** the same in a headless server build, ordering and exactly-once under packet loss, a send-queue-overflow disconnect, an oversize message in a build without `DEBUG`, NGO's own allocations — source: ADR-014, ADR-004 Amendment 1, `modules/networking.md`
- **Server tick**: target < 30ms at 50 players + 150 mobs (F-NET-9 profiling gate — must be confirmed before Networking Core implementation is greenlit) — source: ADR-004
- **Per-client batch body**: ≤ 512 bytes (F-NET-6) — source: ADR-004
- **Zone server load time**: < 500ms at MVP asset scope — source: ADR-009
- **Blocking waits in the server**: exactly two — the startup data-source warm-up (S4, before the first tick, `DB_WARMUP_TIMEOUT_MS` = 5 000; timeout or fault → S-fail) and the shutdown drain (teardown step 5) — source: ADR-009 Amendment 1, ADR-011 (note 2026-10-19)
- **NGO shutdown wait**: at most `NGO_SHUTDOWN_WAIT_FRAMES` = 100 frames (5 s at 20 frames per second), then a warning and on. `DB_WARMUP_TIMEOUT_MS` and `NGO_SHUTDOWN_WAIT_FRAMES` are server config, not constants — source: ADR-009 Amendment 1
- **Database connections**: per zone process at most 10 pooled + 1 unpooled relay listener; 4 zones = 44 against PostgreSQL's default `max_connections` = 100 — source: ADR-006 Amendment 3, ADR-015
- **Teardown time**: systemd's `TimeoutStopSec` must exceed the worst teardown — listener stop (2 s) + drain (10 s) + NGO shutdown cap (5 s) + the rest; set by the ADR-007 C1 amendment — source: ADR-009 Amendment 1
- **Zone process startup/teardown — engine behaviour is verification-pending** (ADR-009 Amendment 1 A1): (A1-1) SIGTERM raises `Application.wantsToQuit` on the main thread, `false` keeps the process alive, the exit status equals the code — a real unknown; fallback `PosixSignalRegistration` if Unity's profile has it, else a native plugin, either only setting a flag read by `Update`; (A1-2) a second SIGTERM does not cut the drain short; (A1-3) host and `NetworkManager` roots survive `LoadSceneAsync(…, Single)`; (A1-4) `TickRate = 20` with `targetFrameRate = 20`, `vSyncCount = 0` fires 20 ticks per second, 0–2 per frame; (A1-5) Editor: leaving Play mode is not blocked, `OnDestroy` runs teardown 1–7, the listener stops in time; (A1-6) `EnableSceneManagement = false` with the plain scene load gives no NGO warning; (A1-7) Npgsql + Dapper on IL2CPP Linux with `link.xml`. Run locally (Windows Dedicated Server Mono / Editor): A1-3, A1-5, A1-6, a first A1-4. Linux IL2CPP headless gate: A1-1, A1-2, A1-7, the final A1-4 — source: ADR-009 Amendment 1
- **NGO channel mapping — identifiers confirmed**: `ReliableSequenced`, `Reliable`, `Unreliable` and `ReliableFragmentedSequenced` compile and deliver (server → client on the first three, Story 039, Editor, 2026-10-12). Not verified: which pipeline NGO used on the wire for each argument; `NetworkConfig.TickRate = 20` in a run. The guarantee mapping (R-OD/R-U/U-U) is binding — source: ADR-004 Amendment 1, `modules/networking.md`
- **Cross-zone relay cost**: one `pg_notify` of < 100 bytes per relayed message over loopback; step 1a costs one empty-queue check per tick; inbox memory 64 × ≤ 52 B; per zone process one extra unpooled database connection and one thread (~1 MB stack reserved); a relayed message reaches other zones' clients one tick after the producer's own (≤ 50 ms + database round trip) — source: ADR-015
- **Cross-zone relay — runtime behaviour is verification-pending** (ADR-015 Migration Plan step 2, on IL2CPP): (0) in the Npgsql version Character Persistence Story 012 pins, `NpgsqlConnection.Wait(int)` and `Notification` exist and behave as assumed; (1) on the headless Linux build a background thread blocking in `Wait` receives a `pg_notify` from another process on that thread; (2) IL2CPP stripping keeps the notification path (`link.xml`); (3) a PostgreSQL restart: logged, reconnect with backoff, `LISTEN` re-issued, no duplicate, tick never blocked; (4) teardown stops the listener within `RELAY_LISTENER_STOP_TIMEOUT_MS` and the process exits; (5) a publish from the tick does not block it, also with the pool exhausted, and a hung publish is cancelled after `RELAY_PUBLISH_TIMEOUT_MS`; (6) Editor: leaving Play mode stops the listener in time and the domain reload completes. The step 1 relay (in-memory bus) depends on none of these — source: ADR-015
- **Assembly boundary — engine behaviour is verification-pending**: confirm each item on the first build or story that exercises it — (1) a client player contains no `IronGrind.ServerLogic`; (2) a Linux Dedicated Server IL2CPP build contains it and runs; (3) whether `UNITY_SERVER` is defined in the Editor under a server build profile; (4) whether asmdef platform lists offer a Dedicated Server entry (not relied on); (5) `IPostBuildPlayerScriptDLLs` and the output file locations for the client-binary scan; (6) `[MovedFrom]` for `[SerializeReference]` data when a type changes assembly; (7) the import setting that keeps Npgsql and Dapper out of a client player; (8) ~~whether a `MonoBehaviour` in an assembly constrained to `UNITY_EDITOR` can be attached to a scene object~~ — confirmed 2026-10-08 on Unity 6.3 (Leveling Story 014): it attaches and runs in Play mode — source: ADR-012
- **Random draw**: one interface call plus a shift and a multiply per float draw; no allocation per draw; one `SystemRandomProvider` and one `System.Random` per process — source: ADR-013
- **`NextFloat()` resolution**: 24 bits — a probability smaller than 2⁻²⁴ (about 6 × 10⁻⁸) cannot be expressed with `NextFloat()`; such a roll uses `NextDouble()` — source: ADR-013
- **Random provider — runtime behaviour is verification-pending**: (1) `System.Random.Next()` on the project's scripting runtime returns a value in [0, `int.MaxValue`) — asserted by the adapter tests of Migration Plan step 1; (2) `SystemRandomProvider.NextFloat()` allocates nothing per call under IL2CPP; (3) `RandomNumberGenerator.Create()` returns entropy in the Linux IL2CPP Dedicated Server build with managed stripping on — check: two server starts log different seeds — source: ADR-013

---

## Core Layer Rules

*Applies to: core gameplay loop, NavMesh service, navigation agent lifecycle, main player systems, physics, collision*

### Required Patterns

**NavMesh service execution (ADR-002):**
- Canonical tick phase ordering — every tick, no exceptions:
  1. `SyncAgentPositions(committedPositions_N)`
  2. `Navigation.Tick()` — drains path request queue
  3. Game systems read nav state (`GetCurrentVelocity`, `IsPathStale`)
  4. Position integration → `committedPositions_{N+1}`
- `NavMeshAgent` APIs must execute on the Unity main thread only — `UnityEngine.AI` is not thread-safe — source: ADR-002
- Zone teardown nav sequence: `zoneState = Closing` → wait for in-flight tick → `ZoneNavigationService.Teardown()` → `NavMesh.RemoveNavMeshData()` — source: ADR-002
- `ZoneNavigationService` must implement a `_tornDown` boolean guard on all public methods (`Tick`, `SyncAgentPositions`, `GetCurrentVelocity`) as a defensive fallback — source: ADR-002
- Server build: `Application.targetFrameRate = TICK_RATE_HZ` (20) set at process initialization — source: ADR-002
- `link.xml` must preserve `UnityEngine.AI`: `<assembly fullname="UnityEngine.AIModule"><namespace fullname="UnityEngine.AI" preserve="all"/></assembly>` — source: ADR-002
- One zone per server process — NavMesh is process-global — source: ADR-002

**Navigation agent lifecycle (ADR-003):**
- `INavigationProvider` interface: `SetDestination`, `SetSpeed`, `Stop`, `Resume`, `IsPathStale`, `GetCurrentVelocity` — `Resume()` is required; it clears `isStopped` and uses the preserved path without queuing a new destination — source: ADR-003
- On Pursuing re-entry after WindingUp/Recovering: call `Resume(entityId)` first; then check `IsPathStale()` — if stale, call `SetDestination(entityId, currentTargetPos)` — source: ADR-003
- On Returning state entry: call `SetSpeed(entityId, RETURN_SPEED_CAP)` then `SetDestination(entityId, spawnPoint)` — source: ADR-003
- On Returning → Dormant: call `Stop()` then `SetSpeed(entityId, mobDef.MoveSpeed)` to restore combat speed — source: ADR-003
- On Returning → Pursuing (re-aggro): call `SetSpeed(entityId, mobDef.MoveSpeed)` then `SetDestination(entityId, newTarget)` — source: ADR-003
- `RETURN_SPEED_CAP = 6.0 m/s` — source: ADR-003
- `NavMeshAgent.enabled` tracks pool occupancy only: `false` in pool; `true` on MobSpawned (before Warp); `false` on MobDied; `true` in Dormant (with `isStopped = true`) — source: ADR-003
- Drain loop in `Tick()` must guard: after `_activeAgents.TryGetValue`, also call `_latestRequests.TryGetValue(entityId, out var latestDest)` and `continue` if missing — prevents `KeyNotFoundException` on stop-during-pursuit — source: ADR-003
- Pool-exhausted mob (151st): log error, return safe defaults — no crash: `IsPathStale = true`, `GetCurrentVelocity = Vector3.zero` — source: ADR-003

**Event/messaging — Core tier (ADR-010):**
- Broadcast events (one producer, ≥ 2 subscribers): C# `event Action<T>` with `readonly struct` T — zero per-emit allocation — source: ADR-010
- All `event Action<T>` arg types must be `readonly struct` — source: ADR-010
- Event naming: PascalCase prefixed with `On` — `OnMobDied`, `OnStatChanged`, `OnPlayerDied` — source: ADR-010
- Subscribe in constructor/`Initialize()` (server) or `Awake()`/`OnEnable()` (client `MonoBehaviour`) — source: ADR-010
- Unsubscribe in `Dispose()` (server) or `OnDestroy()`/`OnDisable()` (client) — source: ADR-010
- If invocation order between two subscribers matters: collapse into one subscriber or use explicit Tier 1 call ordering — source: ADR-010

**Assembly placement — Core tier (ADR-012):**
- Server-authoritative core logic — resolvers and services, their configs, server state machines, the tick loop, RPC guards and server-side validation — is `IronGrind.ServerLogic`. Hit Detection, Navigation/Pathfinding and Auto-Attack Combat are written in `src/ServerLogic/` from their first story (see Foundation → Server/client assembly boundary) — source: ADR-012

**Random draws — Core tier (ADR-013):**
- `DamageCalculator(stats, weapons, items, bonuses, config, random)` draws `NextFloat()` for the crit roll: exactly one draw per `Calculate` call (including when `CritChance` is 0.0) and none on a rejected call. `Calculate(baseDamage, attackerId, targetId, context)` keeps the signature the GDD specifies (see Foundation → Server random provider) — source: ADR-013
- The AC-DC-F-09b test scripts `0.74999994f`, the float directly below 0.75, not the GDD's `0.7499999` — source: ADR-013

**Tick order — Core tier (ADR-014):**
- A core system that ticks runs in step 3 of `ZoneTickPipeline` (ADR-002 phases 1–4; game systems in phase 3), in the order the composition root lists. Simulation always sees the bag state the requests of that tick left — source: ADR-014

### Forbidden Approaches

- **Never call `Navigation.Tick()` or `SyncAgentPositions()` outside `ZoneNavigationService` and the canonical tick phase** — source: ADR-002
- **Never call `INavigationProvider` methods outside Phase 3 of the tick** — source: ADR-002
- **Never assume `SetDestination()` resolves the path within one frame** — async; path may take multiple frames at any queue depth — source: ADR-002
- **Never run multiple zones in one server process** — `NavMesh.AddNavMeshData` is process-global — source: ADR-002
- **Never disable `NavMeshAgent` in Dormant state** — keep enabled with `isStopped = true`; disabling adds API complexity with negligible CPU savings — source: ADR-003
- **Never force `SetDestination` on every Pursuing re-entry** — use `Resume()` when path is still valid to avoid consuming a drain slot unnecessarily — source: ADR-003
- **Never toggle `autoBraking` per state to fix Returning overshoot** — use `SetSpeed(RETURN_SPEED_CAP)` instead — source: ADR-003
- **Never omit the `_latestRequests.TryGetValue` guard in `Tick()` drain loop** — causes `KeyNotFoundException` on any stop-during-pursuit sequence — source: ADR-003
- **Never call `Resume()` from Dormant re-aggro** — Dormant always requires `SetDestination` (no preserved path) — source: ADR-003
- **Never use lambda captures for persistent event subscriptions** — not unsubscribeable by reference — source: ADR-010
- **Never use `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith` or `async void` in tick-driven core code** — start the `Task` and pass it to `ITickCompletionQueue.Track` — source: ADR-011
- **Never place a formula that decides an outcome the player does not see computed (damage, crit) in `IronGrind.Foundation`** — it would ship in the client — source: ADR-012
- **Never register a core system's own delegate with `ServerTickLoop.RegisterTickDriven`** — the tick order is `ZoneTickPipeline`, one class — source: ADR-014
- **Never pass the roll into `DamageCalculator.Calculate` as a value** — it changes the signature the GDD specifies and moves "one roll per call" to every caller, where a caller can reuse or forget a roll — source: ADR-013

### Performance Guardrails

- **NavMesh simulation**: bounded to 20Hz via `Application.targetFrameRate = 20`; verify empirically on Unity 6.3 `UNITY_SERVER` build — if `targetFrameRate` does not control `NavMeshAgent` simulation cadence, fall back to `Time.fixedDeltaTime = 1f / TICK_RATE_HZ` — source: ADR-002

---

## Feature Layer Rules

*Applies to: secondary mechanics, AI systems, loot, leveling, consumables, secondary features*

### Required Patterns

- All R-OD messages carrying a `requestId` field must use `(charId, messageType, requestId)` as the dedup key — includes `BuyRequest`, `SellRequest`, and `UseItemRequest` — source: ADR-001 Amendment A1
- `Action<T>` specializations for struct event arg types: add generic type preservations to `link.xml` if `MissingMethodException` occurs on any `Action<StructType>` invocation in the first IL2CPP build — source: ADR-010
- `readonly struct` event arg types declared in the shared `IronGrind.Events` namespace (one file per type: `MobDeathContext.cs`, `StatChangedArgs.cs`, etc.) — source: ADR-010
- Server-originated bag mutations that get a synchronous result and cannot be queued (loot auto-pickup, auction delivery) read `ICharacterMutationGate.IsHeld(charId)` before mutating. If held, the mutation is **not attempted** — no bag-full result, no client notice — and the caller retries on the first tick after `OnGateOpened(charId)` — source: ADR-011
- Every irreversible outcome (enhancement result, level-up, respec, item consumption) goes through `IrreversibleOutcomeCoordinator`: `Begin` on tick N (validate → close gate → acknowledge → compute and apply in memory → start the write → `Track`); the completion callback on tick N+k delivers on `Success`, or reverts, disconnects, alerts and preserves the session on any failure. For the Enhancement System: `BeginAttempt` in `Begin`, `CompleteAttempt` on success, `RollBackAttempt` on failure — source: ADR-011
- Feature services and their configs (`EnhancementService`, `LootAuctionService`, `GroundItemService`, `EnhancementConfig`, …) are `IronGrind.ServerLogic`; Enemy AI and Status Effects are written in `src/ServerLogic/` from their first story — source: ADR-012
- A feature type goes in `IronGrind.Foundation` only with a named client consumer, and is added to the boundary test's shared allow-list with that consumer in a comment; a new type in `Foundation` that is on neither list fails the test — source: ADR-012
- Loot Table draws one `NextDouble()` per drop-table entry, compared with `(double)DropChance`, and one `NextInt(GoldMin, GoldMax + 1)` for gold per kill — source: ADR-013
- Enhancement draws one `NextDouble()` per attempt and none on a rejected attempt — source: ADR-013
- Enemy AI's Enraged spawn roll (CR-AI-12) is one `NextDouble()` per spawn on the injected process-level `IRandomProvider` — source: ADR-013
- A system that serves a client request has one `InboundRequestHandler` per request type, registered by the server composition root with its `InboundRequestDescriptor`. The handler decodes the body with the message's codec and calls the system — source: ADR-014
- The handler's system owns the reply to the client and the request deduplication; the dispatcher does neither — source: ADR-014
- A feature that announces to every player of every zone (today: the Enhancement System's `ServerBroadcast_Enhancement9`) calls `IServerWideBroadcaster.Broadcast` from the commit-success delivery callback of its irreversible outcome (ADR-011), never from `BeginAttempt`; its type must be on `CrossZoneRelayAllowlist` — source: ADR-015, ADR-011

### Forbidden Approaches

- **Never call `Resume()` from a Dormant re-aggro transition** — Dormant has no preserved path; use `SetDestination` — source: ADR-003
- **Never introduce an `EventBus` class anywhere in `src/`** — once present it proliferates; a structural architecture violation — source: ADR-010
- **Never use class-typed event args** — forces heap allocation on every emit; use `readonly struct` — source: ADR-010
- **Never use `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith` or `async void` in tick-driven feature code** — start the `Task` and pass it to `ITickCompletionQueue.Track` — source: ADR-011
- **Never hold, reject or defer a client request inside a game system because a write is in flight, and never read `ICharacterMutationGate` from a game system** — the request dispatcher is the single enforcement point; the only readers of the gate are the dispatcher and the server-originated bag mutators named above — source: ADR-011
- **Never keep a queue of client requests inside a game system** — every client request reaches its system through the dispatcher's handler call, on the tick — source: ADR-014
- **Never place a drop roll, enhancement odds or any other outcome formula in `IronGrind.Foundation`** — only a formula whose result the game shows the player, and which the client must evaluate to draw a screen, may be there — source: ADR-012
- **Never seed a `System.Random` at mob spawn** — ADR-013 Decision 4 overrides that sentence of `enemy-ai.md` CR-AI-12; the process-level `IRandomProvider` is injected — source: ADR-013
- **Never add a pity or streak mechanic to a roll** — no system stores a value that changes later odds — source: ADR-013

---

## Presentation Layer Rules

*Applies to: rendering, audio, UI, VFX, shaders, animations*

### Required Patterns

**HUD UI framework — UI Toolkit (ADR-005):**
- HUD screen-space layer: UI Toolkit (`UIDocument` / `VisualElement` / UXML / USS) — source: ADR-005
- World-space damage numbers: separate UGUI world-space Canvas (two-layer mixed-framework is mandatory; the layers are strictly isolated) — source: ADR-005
- Fill bars: `style.scale` on X axis with `UsageHints.DynamicTransform`; USS `transform-origin: left center;` — not `fillAmount`, not `style.width` percentage — source: ADR-005
- All non-interactive HUD leaf elements: `pickingMode = PickingMode.Ignore` explicitly via `.hud-display-only` USS class — **does not propagate to children automatically** — source: ADR-005
- Safe area insets: use `RuntimePanelUtils.ScreenToPanel` to convert `Screen.safeArea` physical pixels to panel logical units; Y-axis must be flipped (Screen = bottom-left origin; UI Toolkit = top-left) — source: ADR-005
- All positional animation: `element.style.translate` — NOT `VisualElement.transform` setter (deprecated Unity 6.2) — source: ADR-005
- `HUD_PanelSettings.clearColor = false` — verified before every build — source: ADR-005
- `EventSystem` must carry `InputSystemUIInputModule` — not legacy `StandaloneInputModule` — source: ADR-005
- `WorldSpaceDamageCanvas`: no `GraphicRaycaster` component — damage numbers are non-interactive — source: ADR-005
- All USS files: pass syntax validation before commit — invalid USS blocks Unity 6.3 import — source: ADR-005
- HUD layer architecture: `HUD_Static` (dirty on zone entry / party join-leave only) / `HUD_Dynamic` (dirty per stat-change) / `HUD_Overlay` — source: ADR-005
- `HUD_PanelSettings.sortingOrder = 0` — source: ADR-005

**Combat UI framework — Painter2D + MonoBehaviour presenter (ADR-008):**
- Cooldown arc renderer: Painter2D `generateVisualContent` callback (NOT shader-based) — source: ADR-008
- Arc draws clockwise from 12 o'clock (start angle = `-90f`); sweep = `fraction * 360°`; `fraction` from F-CUI-1 — source: ADR-008
- `MarkDirtyRepaint()` called every frame in `SkillBarPresenter.Update()` while slot is `OnCooldown`; set `display: none` when `fraction ≤ 0` to stop calls — source: ADR-008
- `SkillBarPresenter : MonoBehaviour` owns `UIDocument` reference and `SkillBarState` value-type cache — source: ADR-008
- NGO message handlers enqueue to `Queue<SkillCooldownUpdate>`; presenter drains in `Update()` — source: ADR-008
- UI Toolkit event callbacks: Unity 6.0 names — `HandleEventTrickleDown`, `HandleEventBubbleUp`, `StopPropagation()` — source: ADR-008
- `SkillBar_PanelSettings.sortingOrder = 1` (above HUD at sortingOrder 0) — source: ADR-008
- `SkillBarState` and `SlotState` are `struct` types — zero heap allocation on copy — source: ADR-008
- `[SerializeField]` on fields only — compile error on properties in Unity 6.3 — source: ADR-008 / ADR-009

**What the client reads (ADR-012):**
- UI and presentation code is in `IronGrind.Client` (`src/Client/`), which references `IronGrind.Foundation` only — source: ADR-012
- Presenters read the local player's state only through read-only view interfaces declared in `Foundation` (working names `ILocalPlayerStatsView`, `ILocalPlayerLevelingView`); the client implementation is a mirror filled from the server's state and event messages, and is `Client` code — source: ADR-012
- A player action on a screen goes out through a request interface declared in `Foundation` (working name `IRespecRequestSender`) whose client implementation sends the message; the server validates and applies, and the result comes back as state — source: ADR-012
- A formula the client must evaluate to draw a screen, and whose result the game shows the player anyway (experience threshold, level tier multiplier, derived-stat preview), lives in a static class in `Foundation`, in one copy that `ServerLogic` also calls, with its client consumer named — source: ADR-012
- The manual HUD harness is in `IronGrind.DevHarness` (`src/DevHarness/`, define constraint `UNITY_EDITOR`, in no player build); a device harness, if wanted, drives the read-only views with fake data — source: ADR-012
- A client handler for a zone-wide message (relayed or not; today `ServerBroadcast_Enhancement9`) tolerates its arrival before `SessionReady` — it drops the message or shows it once the HUD is up. Clients are added to the zone's outbox on transport connect, before `SessionReady` (accepted, user decision 2026-10-16) — source: ADR-015

### Forbidden Approaches

- **Never use `VisualElement.transform` setter** — deprecated in Unity 6.2; use `style.translate`, `style.rotate`, `style.scale` — source: ADR-005
- **Never use `style.width` percentage for fill bars** — triggers layout recalculation per tick; breaks the < 0.3ms HUD update budget — source: ADR-005
- **Never set `PanelSettings.clearColor = true`** — erases the 3D scene beneath the HUD — source: ADR-005
- **Never write raw `Screen.safeArea` pixel values directly to `style.margin*`** — must convert via `RuntimePanelUtils.ScreenToPanel` — source: ADR-005
- **Never assume `PickingMode.Ignore` propagates to children** — set explicitly on every non-interactive leaf element — source: ADR-005
- **Never add `GraphicRaycaster` to `WorldSpaceDamageCanvas`** — creates an unnecessary hit-test layer on non-interactive elements — source: ADR-005
- **Never use UGUI Canvas for screen-space HUD elements** — source: ADR-005
- **Never use HLSL shader background-image material for the cooldown arc** — post-cutoff integration risk for material-property-block UI Toolkit in Unity 6.3; Painter2D is the first-class API for this pattern — source: ADR-008
- **Never write VisualElement properties directly from NGO message handlers** — use `Queue<T>` decoupling; keeps network and UI layers independently testable — source: ADR-008
- **Never use deprecated UI Toolkit event API names in any combat UI C# file**: `ExecuteDefaultAction`, `ExecuteDefaultActionAtTarget`, `PreventDefault()` — CI lint gate enforces this — source: ADR-008
- **Never name a service or the `CharacterStats` class in `IronGrind.Client`** (`LevelingService` after the Leveling move, `CharacterStats` after the Character Stats move) — the client reads views — source: ADR-012
- **Never call `TryApplyRespec`, `RegisterPlayerEntity` or `AttachCharacterStats` from client code** — the server validates and applies — source: ADR-012
- **Never put a `IronGrind.Client` `MonoBehaviour` or `ScriptableObject` in a scene or asset the Dedicated Server loads (zone scenes)** — it is a missing script there; presentation components are added by the client composition root or live in client-only scenes — source: ADR-012, ADR-009

### Performance Guardrails

- **HUD update cost**: < 0.3ms per tick at 60fps on iPhone SE 3rd gen (A15 Bionic) — must be profiled on device before HUD implementation sprint — source: ADR-005
- **Cooldown arc (8 concurrent)**: 8 × `Painter2D.Arc()` + `MarkDirtyRepaint()` per frame must be validated ≤ 0.3ms by performance-analyst before combat UI sprint starts — blocking gate per CR-CUI-19 — source: ADR-008

---

## Global Rules (All Layers)

### Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Classes | PascalCase | `PlayerController` |
| Public fields / properties | PascalCase | `MoveSpeed` |
| Private fields | _camelCase | `_moveSpeed` |
| Methods | PascalCase | `TakeDamage()` |
| C# Events | PascalCase, `On` prefix | `OnHealthChanged` |
| Files | PascalCase matching class | `PlayerController.cs` |
| Scenes / Prefabs | PascalCase | `PlayerCharacter.prefab` |
| Constants | UPPER_SNAKE_CASE | `MAX_ENHANCEMENT_LEVEL` |

### Performance Budgets

| Target | Value |
|---|---|
| Framerate | 60fps |
| Frame budget | 16.6ms |
| Draw calls | ≤ 100 per frame (mobile strict) |
| Memory ceiling | 1.5GB |

### Approved Libraries / Addons

None configured yet — update when dependencies are approved.

### Forbidden APIs (Unity 6.3 LTS)

These APIs are deprecated or removed in Unity 6.3 LTS:

| Forbidden | Replacement | Since | Status |
|---|---|---|---|
| `Object.FindObjectsOfType<T>()` | `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` | 6.0 | Deprecated |
| `Object.FindObjectOfType<T>()` | `Object.FindAnyObjectByType<T>()` | 6.0 | Deprecated |
| `ExecuteDefaultAction` | `HandleEventTrickleDown` | 6.0 | Deprecated |
| `ExecuteDefaultActionAtTarget` | `HandleEventBubbleUp` | 6.0 | Deprecated |
| `PreventDefault()` | `StopPropagation()` | 6.0 | Deprecated |
| `Rigidbody.SetDensity()` | `Rigidbody.mass` | 6.1 | Deprecated |
| PVRTC texture compression | ASTC (iOS) / ETC2 (Android) | 6.1 | Deprecated |
| `SetupRenderPasses` (URP) | `AddRenderPasses` + `RecordRenderGraph` | 6.2 | Deprecated (removed behavior 6.3) |
| `VisualElement.transform` setter | `style.translate`, `style.rotate`, `style.scale` | 6.2 | Deprecated |
| `RenderGraphSettings.enableRenderCompatibilityMode` | (removed — always returns false) | 6.3 | **Removed** |
| `NetworkTransform.Update` override | `NetworkTransform.OnUpdate` | 6.3 | **Removed** |
| `AccessibilityNode.selected` | `AccessibilityNode.invoked` | 6.3 | Deprecated |

Source: `docs/engine-reference/unity/deprecated-apis.md`

### Cross-Cutting Constraints

- **`_FORWARD_PLUS` shader keyword renamed** to `_CLUSTER_LIGHT_LOOP` (Unity 6.1) — fails silently; scan all shader code before assuming the old keyword works — source: `deprecated-apis.md`
- **OpenGL ES on iOS: removed** — Graphics API list must contain Metal only; remove OpenGL ES in Player Settings — source: `current-best-practices.md`
- **C# null-coalescing operators (`?.`, `??`) do not work correctly with Unity `Object` subclasses** — use explicit null checks instead — source: `current-best-practices.md`
- **`async/await` with `LoadSceneAsync`**: use `yield return op` in a coroutine — `await` does not guarantee `Awake()`/`Start()` complete before the continuation in headless builds — source: ADR-009
- **No `await` in server tick code**: tick-driven server code in every layer starts a `Task` and hands it to `ITickCompletionQueue`; it never awaits or blocks on it (see Foundation → Asynchronous persistence in the tick loop). `UnityEngine.Awaitable` is not used for tick logic — source: ADR-011
- **Folder encodes build membership**: `src/Foundation/` ships in both builds, `src/ServerLogic/` in the server only, `src/Client/` in the client only, `src/DevHarness/` in neither. Server-only is the default for game logic (see Foundation → Server/client assembly boundary) — source: ADR-012
- **Every move story is done (2026-10-08): no game-logic system remains in `IronGrind.Foundation`.** A new type goes in `Foundation` only with an entry on the boundary test's shared allow-list and its client consumer named; everything else is written in `src/ServerLogic/` or `src/Client/` — source: ADR-012
- **Server randomness goes through the one injected `IRandomProvider`**: no `UnityEngine.Random`, no `new System.Random` and no static accessor in `IronGrind.ServerLogic` (see Foundation → Server random provider) — source: ADR-013
- **Client requests reach game systems through `InboundRequestDispatcher` only, and the server tick has one order**: `ZoneTickPipeline` runs completion, relay, requests, simulation, outbound (see Foundation → Inbound request dispatch and tick order) — source: ADR-014, ADR-015
- **Server-wide messages go through `IServerWideBroadcaster` and the allowlisted PostgreSQL relay only**: no other component carries a message between zone processes, and the relay never becomes an event bus (see Foundation → Cross-zone message relay) — source: ADR-015
- **The zone process lifecycle belongs to `ZoneServerComposition`**: one startup order (S0–S10), one teardown order (1–8), exit through `Application.Quit((int)ZoneExitCode)` only; every server object is built there and disposed there (see Foundation → Scene/zone-load management) — source: ADR-009 Amendment 1
- **`[SerializeField]` on properties**: compile error in Unity 6.3 — use on private fields only; or use `[field: SerializeField]` for auto-property backing fields — source: ADR-009, `current-best-practices.md`
- **USS syntax errors block import** in Unity 6.3 (was a warning in 6.1/6.2) — all USS must be valid before commit; add USS linting to CI — source: ADR-005
- **SRP Batcher**: enable in URP Asset → Advanced → SRP Batcher for significant CPU win on mobile — source: `current-best-practices.md`
- **iOS build requirements**: IL2CPP + ARM64 + Metal only + ASTC textures — source: `current-best-practices.md`
