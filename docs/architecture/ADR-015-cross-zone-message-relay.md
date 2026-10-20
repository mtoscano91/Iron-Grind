# ADR-015: Cross-Zone Message Relay — PostgreSQL `LISTEN` / `NOTIFY`

## Status
Accepted (2026-10-16)

## Date
2026-10-16

*Amended 2026-10-16 after `/architecture-review` (`architecture-review-2026-10-16-adr-015.md`): A1 listener stop on leaving Play mode, A2 connection opened inside the tracked publish task, R1 ordering, R2 CTS disposal, R3 allocation wording, R4 clients before `SessionReady` (accepted, user decision).*

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS |
| **Domain** | Networking — server-to-server (inter-process) relay; no Unity engine API is used |
| **Knowledge Risk** | MEDIUM — the relay itself is plain .NET and Npgsql (ADR-006), engine-agnostic. The risk is the runtime: a dedicated background thread blocking in Npgsql inside an IL2CPP headless Linux build, and its shutdown inside ADR-009's teardown. Delivery to clients reuses ADR-004 / ADR-014 code that is already verified (Networking Core Stories 038, 039). |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`; `docs/engine-reference/unity/modules/networking.md`; `docs/engine-reference/unity/breaking-changes.md`; `docs/engine-reference/unity/deprecated-apis.md`; ADR-002, ADR-004, ADR-006, ADR-007, ADR-009, ADR-010, ADR-011, ADR-014 |
| **Post-Cutoff APIs Used** | None known. Npgsql `NpgsqlConnection.Notification`, `NpgsqlConnection.Wait(int)` and the SQL `LISTEN` / `pg_notify` exist in Npgsql 6–8; **Npgsql is not in the project yet and no version is pinned** (Character Persistence Story 012, Blocked). The claim is unverified until that story pins a version and Verification 0 passes. |
| **Verification Required** | All items run on the scripting backend the server build ships with — IL2CPP per ADR-007 (ADR-006 left Mono or IL2CPP open; ADR-007 chose IL2CPP). (0) In the Npgsql version pinned by Character Persistence Story 012, `NpgsqlConnection.Wait(int)` and the `Notification` event exist with the behaviour this ADR assumes; cite that version's documentation. (1) On the headless Linux build, a dedicated `Thread` (`IsBackground = true`) blocking in `NpgsqlConnection.Wait(timeoutMs)` receives a `pg_notify` sent by a second process and raises `Notification` on that thread; nothing touches the main thread. (2) IL2CPP stripping keeps the Npgsql notification path, the `Notification` event included (`link.xml`, ADR-006 Verification 2). (3) Restarting PostgreSQL while a zone runs: the listener logs the loss, reconnects with backoff, re-issues `LISTEN`, and relayed messages flow again; no notification is delivered twice after the reconnect; the tick is never blocked. (4) Zone teardown (ADR-009) stops the listener thread within `RELAY_LISTENER_STOP_TIMEOUT_MS` and the process exits. (5) A `pg_notify` issued from the tick (connection opened and command run inside one task tracked by `ITickCompletionQueue`, never awaited) does not block the tick — also with the ADR-006 pool exhausted — and a publish that hangs is cancelled after `RELAY_PUBLISH_TIMEOUT_MS` and frees its in-flight slot. (6) Editor: with a zone running in Play mode on the real listener, leaving Play mode stops the listener thread within `RELAY_LISTENER_STOP_TIMEOUT_MS` (logged), and the following domain reload completes. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-006 (Accepted — PostgreSQL + Npgsql); ADR-007 (Accepted — all zone processes and PostgreSQL on one VPS); ADR-011 (Accepted — a `Task` started on the tick is tracked by `ITickCompletionQueue`, never awaited); ADR-014 (Accepted — tick order, amended here); ADR-010 (Accepted — no central bus; a network callback only records); ADR-002 (Accepted — one zone per process, the reason a relay is needed); ADR-004 (Accepted — `ClientMessageOutbox` delivers inside a zone); ADR-009 (Accepted — zone teardown, which gains a listener-stop step 2a, Decision 3) |
| **Enables** | A future cross-zone world chat (by an allowlist row, if Vertical Slice scopes world chat across zones) |
| **Blocks** | Enhancement Story 016 (`ServerBroadcast_Enhancement9` delivery, AC-ENH-18 delivery half, AC-NC-47, TR-enh-010) — after acceptance (2026-10-16) it waits only for the Migration Plan step 1 relay story |
| **Ordering Note** | Amends ADR-014 Decision 6 (a new step 1a). ADR-014 receives a dated note when this ADR is Accepted. The relay can be built and tested in EditMode with the in-memory bus before any Npgsql code exists, so Enhancement Story 016 does not wait for Npgsql. The Npgsql transport (Migration Plan step 2) depends on Character Persistence Story 012 (Npgsql in the project at a pinned version — Blocked on how the libraries get in). |

## Context

### Problem Statement
ADR-002 Decision 4 makes one zone instance per server process a binding constraint, and ADR-007 runs 1–4 such processes on one VPS. `ClientMessageOutbox` (ADR-004, Networking Core Story 039) reaches only the clients connected to its own process. `networking-channel-contract.md` codes `ServerBroadcast_Enhancement9` as `S→ALL (all connected clients, every zone)`, and enhancement-system.md CR-ENH-14 sends it "to all online players". No component today can carry a message from one zone process to the clients of another, so Enhancement Story 016 is Blocked. The gdd-cross-review of 2026-05-23 (W-2) tracked this gap as a pre-implementation amendment.

### Constraints
- All zone logic runs on the main thread; game state is not thread-safe (ADR-007, ADR-011).
- No `await` in tick-driven code; a task started on the tick is handed to `ITickCompletionQueue` (ADR-011).
- No central EventBus / MessageBus / publish-subscribe singleton for cross-system communication (ADR-010; registry forbidden pattern). The relay must not become one.
- A network-side callback only records; game logic runs on the tick (ADR-010 Decision 5, ADR-014 Decision 7).
- The ADR-007 gateway process is designed but not built; no zone ↔ gateway IPC exists.
- Indie MVP: no new infrastructure component unless it pays for itself; cost < €30/month (ADR-007).

### Requirements
- R1: a message produced in one zone reaches every connected client of every other running zone process (AC-ENH-18).
- R2: the committing zone's own clients receive it through the existing per-zone outbox, in the same tick's outbound step as today (AC-NC-47: not cap-exempt, subject to the priority-path cap).
- R3: a failure to relay to another zone never changes the outcome and never stops local delivery (EC-ENH-8).
- R4: the tick never blocks on the relay, in either direction.
- R5: the volume is small: at most a few +9 broadcasts per hour server-wide; the body is ≤ 52 bytes.
- R6: works unchanged if zone processes later move to a second VPS that can reach the same PostgreSQL.

## Decision

**Zone processes relay server-wide messages to each other through PostgreSQL `LISTEN` / `NOTIFY` on one channel. The producing zone delivers to its own clients directly and publishes one notification; every other zone delivers the relayed body to its own clients. Delivery is best-effort and at most once. Only message types on an explicit allowlist can be relayed.**

### Decision 1 — Local delivery first, then publish
Game code calls one service, `IServerWideBroadcaster.Broadcast(messageTypeId, body)`, on the tick thread. It:
1. enqueues `body` for every client of this zone through `IZoneClientBroadcastSink.EnqueueToAllClients(messageTypeId, body, isCapExempt: false)` — implemented by `ClientMessageOutbox`, which already holds the zone's client set (`AddClient` / `RemoveClient`) and calls its own `Enqueue` for each. "Connected client" means a client present in the zone's outbox. `InboundTransportAdapter` adds a client on transport connect, before `SessionReady`, so a client still loading also receives the message; this is accepted (user decision 2026-10-16) and holds for every zone-wide message, not only relayed ones. The client's handler for such a message must tolerate it before `SessionReady` (drop it, or show it once the HUD is up). It is the same path as any zone message, so the cap (AC-NC-47) and the outbound step's timing apply unchanged;
2. then calls `ICrossZoneRelayPublisher.Publish(messageTypeId, body)`.

Step 2 failing (refused, database down, timeout) is logged and has no effect on step 1 or on the caller. `Broadcast` never throws.

The broadcaster rejects (logs an error, sends nothing) a `messageTypeId` that is not on the relay allowlist (Decision 4). The recipient set is "every connected client"; it is never derived from `MessageDirection` alone (Story 016 note).

### Decision 2 — Publishing: one `pg_notify` per message, never awaited
`NpgsqlRelayPublisher` serializes the relay envelope (Decision 5) to text and runs `SELECT pg_notify(@channel, @payload)` through the zone's existing ADR-006 `NpgsqlDataSource`: `dataSource.CreateCommand(sql)` then `ExecuteNonQueryAsync(token)`, so opening a pooled connection is part of the same task and the connection returns to the pool when the task ends (success, failure or cancellation). Nothing on the tick thread calls a synchronous `Open()` — on an exhausted pool it would block the tick for up to the connection `Timeout` (5 s in ADR-007's string). It does not await: the `Task` is tracked by `ITickCompletionQueue.Track` (ADR-011) with a `CancellationTokenSource` that cancels after `RELAY_PUBLISH_TIMEOUT_MS` (default 2 000; the command's own timeout is set to the same value). The completion callback logs a failure or a timeout, releases the in-flight slot, and disposes the `CancellationTokenSource` (`ITickCompletionQueue` never disposes a caller-owned one). A hung publish therefore frees its in-flight slot instead of holding it until the 10 s ADR-011 watchdog. `NOTIFY` outside a transaction is delivered when the statement completes.

The notification is not sent inside the persistence transaction of the commit: the relay stays independent of `ICharacterPersistence`, and the commit-success callback that raises the broadcast already runs only after the write succeeded (ADR-011, CR-ENH-15 step 8).

Publishes in flight are bounded by `RELAY_MAX_PUBLISHES_IN_FLIGHT` (default 8); a publish over the bound is dropped and logged.

> **Note 2026-10-19 (Networking Core Story 041, Verification 5; user decision).** The run confirmed that a publish never blocks the tick: with the ADR-006 pool exhausted, it is cancelled at `RELAY_PUBLISH_TIMEOUT_MS` and frees its slot. The claim does **not** hold for a server that accepts the connection and never answers (PostgreSQL processes suspended). There, the CTS fires at 2 s, but Npgsql 8.0.9 under Mono does not complete the cancelled command (also with `Cancellation Timeout=-1`). The slot is then freed by the ADR-011 watchdog (`TickTaskStatus.TimedOut`, 200 ticks), and the outcome is unknown: one such publish was still delivered after the server resumed. Accepted as TD-074: the tick stayed unaffected, and with a few messages per hour the 8 slots are not exhausted. Two more facts from the same run: the composition root must warm the data source before its tick loop starts, because the first publish of a fresh process cost 58 to 75 ms on the tick; and the listener must use `WaitAsync` (Decision 3 note).

### Decision 3 — Receiving: one dedicated listener thread per zone process
`NpgsqlRelayListener` owns one dedicated `NpgsqlConnection` and one dedicated `Thread` (`IsBackground = true`, never a thread-pool thread). The connection string states `Pooling=false` and `Multiplexing=false` explicitly (multiplexing breaks `LISTEN`; off by default, stated as a guard) and uses `Tcp Keepalive=true`, not Npgsql's protocol-level `Keepalive`, whose probes would contend with the blocking read inside `Wait`. The thread:
1. opens the connection and runs `LISTEN irongrind_relay`;
2. loops on `connection.Wait(RELAY_LISTENER_WAIT_MS)` (default 500) until stop is requested;
3. in the `Notification` handler — which runs on this thread — parses the payload (Decision 5), drops it if it is malformed, not on the allowlist, or from this process's own `OriginId`, and otherwise copies the body into `CrossZoneRelayInbox`. It calls no game code (ADR-010 Decision 5); its own work is copying the body into the bounded inbox (Npgsql still allocates the payload string and event arguments for each notification — off the tick thread, a few per hour); a payload longer than the longest valid envelope is dropped before decoding.
4. The loop body catches every exception type (`NpgsqlException`, `IOException`, `ObjectDisposedException`, anything else) — none escapes the thread. On a failure it disposes the connection, logs a warning (an error after `RELAY_RECONNECT_ALERT_AFTER` consecutive failures, default 5), waits with exponential backoff (500 ms doubling to `RELAY_RECONNECT_MAX_BACKOFF_MS`, default 10 000; the wait is cut short by stop), and reconnects from step 1. Notifications sent while it is disconnected, including between the reconnect and the re-issued `LISTEN`, are lost (Decision 6).

> **Note 2026-10-19 (Networking Core Story 041, VR-1 first run; user decision).** Step 2's `Wait(RELAY_LISTENER_WAIT_MS)` is implemented as `WaitAsync(RELAY_LISTENER_WAIT_MS, CancellationToken.None)`, on which the dedicated listener thread blocks. Npgsql 8.0.9's synchronous `Wait(int)` uses `Socket.ReceiveTimeout` and recognises the expiry by the socket error code, which it expects to be `WouldBlock` when the `Mono.Runtime` type exists. Under Unity's Mono on Windows (the Editor and the Mono server build), every empty wait was instead treated as a broken connection: the listener reconnected about once a second and lost messages. `WaitAsync` implements the timeout with a timer and returns false on expiry. On that path Npgsql raises `Notification` on the thread that completes the read, so the connection queues each payload and the listener thread hands it to the receiver after the wait returns. Step 3 therefore still runs on the listener thread, one payload at a time. The dedicated thread, the stop flag, `Stop()` and the backoff are unchanged. Verification 1 now reads "raises `Notification`, and hands the payload to the receiver on that thread". Whether the synchronous `Wait` works under IL2CPP stays unproven and no longer matters.

`CrossZoneRelayInbox` is a bounded thread-safe queue (`RELAY_INBOX_CAPACITY`, default 64 entries, each ≤ the type's `MaxBodyBytes`); an entry over capacity is dropped and counted, logged once per tick. The listener is the only writer; the tick is the only reader.

**Stop.** `NpgsqlRelayListener.Stop()` sets the stop flag and joins the thread for at most `RELAY_LISTENER_STOP_TIMEOUT_MS` (default 2 000, longer than one `Wait`). It is idempotent and is called from two places: (a) ADR-009 Decision 3 zone teardown, as a new step 2a after the tick loop has stopped (step 2) and before the process exits; (b) `OnDestroy` of the server host `MonoBehaviour` that owns the zone composition root. Only the listener thread disposes its connection, on its way out — `Stop()` never disposes it under a thread blocked in `Wait`. A thread that does not stop in time is logged; being a background thread it does not hold the process open.

**Editor.** `NpgsqlRelayListener` is created only by the server composition root of a running zone, never by EditMode or PlayMode tests (they use `InMemoryCrossZoneRelayBus`). ADR-009 teardown runs on zone `Closed` and ends in a process exit; it does **not** run when the Editor leaves Play mode. Path (b) covers that case: Unity destroys the scene's objects on leaving Play mode, before any domain reload (and still when domain reload is disabled in Enter Play Mode Options), so the host's `OnDestroy` stops the thread and no listener thread survives (Verification 6).

### Decision 4 — Allowlist and handlers
`CrossZoneRelayAllowlist` is a sealed table built by the composition root: `MessageTypeId → MaxBodyBytes`. At this ADR it holds one row: `ServerBroadcast_Enhancement9`, 52 bytes. A type is added only by a story that names the GDD rule requiring cross-zone delivery. Both the broadcaster (send) and the listener (receive) check it.

The relay carries an already-encoded `S→ALL` message body and nothing else. It never carries a request, a reply to one client, state to be applied by a game system, or a call between systems — those stay point-to-point inside a zone (ADR-010).

### Decision 5 — Relay envelope
The `NOTIFY` payload is ASCII text, `v1:{messageTypeId}:{originId}:{base64(body)}`:
- `messageTypeId` — decimal `ushort`;
- `originId` — 16 hex digits, a random 64-bit value the zone process draws once at start (from `System.Security.Cryptography.RandomNumberGenerator`, not from the game's `IRandomProvider`, ADR-013, so it consumes no game draw); identifies the sender across restarts and zone ids;
- `body` — the wire body exactly as `ClientMessageOutbox.Enqueue` takes it.

At 52 bytes of body the payload is under 100 characters, far below PostgreSQL's 8 000-byte `NOTIFY` limit. An unknown version prefix is dropped and logged.

### Decision 6 — Guarantees
- **At most once, best-effort.** No replay, no acknowledgement, no durable relay table. A notification sent while a listener is reconnecting, or dropped by a full inbox, is lost and logged. EC-ENH-8 already accepts a failed delivery, and a +9 announcement replayed late would be stale.
- **Order.** None is guaranteed, not even from one sender: publishes in flight run on different pooled connections, which are different sessions, and PostgreSQL orders notifications only within one session. Harmless: +9 announcements are independent of each other.
- **Only after the commit.** Guaranteed by the caller: `Broadcast` is called from the commit-success delivery callback (ADR-011), never from `BeginAttempt`.
- **A zone receives its own notifications** (it also listens). It drops them by `originId`; its clients were served by Decision 1.

### Decision 7 — Tick order (amends ADR-014 Decision 6)
`ZoneTickPipeline` gains step **1a — Relay**, between step 1 (Completion) and step 2 (Requests): `CrossZoneRelayInbox.Drain(currentTick, handler)` removes every entry present when the step starts and, for each, the handler calls `IZoneClientBroadcastSink.EnqueueToAllClients(messageTypeId, body, isCapExempt: false)`. Like the other steps it runs in its own `try` / `catch` (a `STEP_RELAY` name for `LogStepFailed`). The pipeline takes `ICrossZoneRelayInbox` and `ICrossZoneRelayHandler` as new constructor arguments; its doc comments and ADR-014's "four steps" become five. A relayed message therefore leaves in the outbound step of the first tick after the listener queued it; a locally produced one leaves in its own tick's outbound step.

### Architecture Diagram

```
 Zone process A (commit +8 → +9)                      Zone process B
 ─────────────────────────────────                    ──────────────────────────────
 tick: Completion → commit-success callback           listener thread (background)
        │                                               LISTEN irongrind_relay
        ▼                                               Wait(500) loop
 IServerWideBroadcaster.Broadcast(type, body)             │ Notification
   1. ClientMessageOutbox.Enqueue(each A client)          ▼
   2. ICrossZoneRelayPublisher.Publish ──┐              parse v1 envelope, allowlist,
                                         │              drop own originId
        pg_notify('irongrind_relay', ..) │                 │
        (ExecuteNonQueryAsync, tracked   │                 ▼
         by ITickCompletionQueue)        │              CrossZoneRelayInbox (bounded)
                                         ▼                 │
                    ┌──────────────────────────────┐        │ tick step 1a: Drain
                    │ PostgreSQL 127.0.0.1:5432    │──────▶ ▼
                    │ channel irongrind_relay      │      ClientMessageOutbox.Enqueue
                    └──────────────────────────────┘        (each B client, not cap-exempt)
                                         │                 │
                    (A also receives its own                ▼ step 4: Outbound
                     notification and drops it)          clients of B
```

### Key Interfaces

```csharp
/// <summary>Sends an S→ALL message to every connected client of every zone. Tick thread only. Never throws.</summary>
public interface IServerWideBroadcaster
{
    /// <summary>Enqueues <paramref name="body"/> for every client of this zone, then publishes it to the other zones.
    /// Returns false (and sends nothing) if <paramref name="messageTypeId"/> is not on the relay allowlist.</summary>
    bool Broadcast(ushort messageTypeId, ReadOnlySpan<byte> body);
}

/// <summary>Sends one message to every client of this zone. Tick thread only. Implemented by ClientMessageOutbox.</summary>
public interface IZoneClientBroadcastSink
{
    /// <summary>Calls Enqueue(clientId, messageTypeId, body, isCapExempt) for every client added to this zone's outbox.</summary>
    void EnqueueToAllClients(ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt);
}

/// <summary>Publishes one relay envelope to the other zone processes. Tick thread only. Never blocks, never throws.</summary>
public interface ICrossZoneRelayPublisher
{
    void Publish(ushort messageTypeId, ReadOnlySpan<byte> body);
}

/// <summary>Relayed messages waiting for the tick. Written by the listener thread, drained by tick step 1a.</summary>
public interface ICrossZoneRelayInbox
{
    /// <summary>Listener thread. Returns false if the entry was dropped (inbox full).</summary>
    bool TryAdd(ushort messageTypeId, ReadOnlySpan<byte> body);

    /// <summary>Tick thread. Hands every entry present at the call to <paramref name="handler"/>, in arrival order.</summary>
    int Drain(uint currentTick, ICrossZoneRelayHandler handler);
}

/// <summary>Delivers one relayed body to this zone's clients (production: IZoneClientBroadcastSink.EnqueueToAllClients, not cap-exempt).</summary>
public interface ICrossZoneRelayHandler
{
    void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body);
}
```

Production types: `ServerWideBroadcaster`, `NpgsqlRelayPublisher`, `NpgsqlRelayListener`, `CrossZoneRelayInbox`, `CrossZoneRelayAllowlist`, `RelayEnvelopeCodec` — all in `IronGrind.ServerLogic` (ADR-012). Test double: `InMemoryCrossZoneRelayBus`, which connects the publishers and inboxes of several in-process test zones, so EditMode tests can run two zones without PostgreSQL.

**Tuning knobs** (server config, ADR coding standards — not hardcoded): `RELAY_CHANNEL_NAME` (`irongrind_relay`), `RELAY_LISTENER_WAIT_MS` (500), `RELAY_RECONNECT_MAX_BACKOFF_MS` (10 000), `RELAY_RECONNECT_ALERT_AFTER` (5), `RELAY_INBOX_CAPACITY` (64), `RELAY_MAX_PUBLISHES_IN_FLIGHT` (8), `RELAY_PUBLISH_TIMEOUT_MS` (2 000), `RELAY_LISTENER_STOP_TIMEOUT_MS` (2 000).

## Alternatives Considered

### Alternative 1: Relay through the gateway / coordinator process
- **Description**: Zone processes send the message to the ADR-007 gateway over the Unix domain socket the gateway uses for zone registration; the gateway fans it out to every other zone.
- **Pros**: No database load; the gateway already knows every zone; a natural home for later cross-zone features (whispers, guilds).
- **Cons**: The gateway is not built, and its zone link is specified only for registration; this would add a second IPC protocol, framing, reconnect handling and a gateway outage mode to gameplay delivery. ADR-007 plans to replace the Unix socket with "Redis or file-based" on scale-out, so the relay would be rebuilt then.
- **Rejection Reason**: Much more to build and operate for one message type, and it breaks on the planned scale-out. Revisit if cross-zone messaging grows into routed (per-player) traffic, which pub/sub to all zones does not serve.

### Alternative 2: Message broker (Redis pub/sub, NATS)
- **Description**: A broker process on the VPS; zones publish and subscribe through its .NET client.
- **Pros**: Purpose-built pub/sub; high throughput; scales across machines.
- **Cons**: A new service to install, secure, monitor and back up; a new client library to prove under IL2CPP stripping; the same at-most-once semantics as `NOTIFY` for Redis pub/sub. None of its throughput is needed at a few messages per hour.
- **Rejection Reason**: Cost and risk with no benefit at MVP volume. Revisit if relayed traffic grows to sustained rates (for example cross-zone world chat at scale).

### Alternative 3: Polling a relay table
- **Description**: The producer inserts a row; every zone polls `SELECT … WHERE id > @lastSeen` every N ticks.
- **Pros**: Durable; replay after a reconnect comes for free.
- **Cons**: Constant query load from every zone even when idle; latency equal to the poll interval; table cleanup needed. Durability was declined (Decision 6).
- **Rejection Reason**: Pays continuously for a guarantee the design does not want.

### Alternative 4: Every zone, the producer included, delivers only from the notification
- **Description**: The producer does not enqueue locally; it waits for its own notification like every other zone.
- **Pros**: One delivery path.
- **Cons**: Local delivery then depends on the database and takes extra milliseconds and a tick; a relay failure would silence the +9 even for the players standing next to the smith.
- **Rejection Reason**: Rejected by user decision 2026-10-16; Decision 1 keeps local delivery independent of the relay.

### Alternative 5: `NOTIFY` inside the persistence transaction of the commit
- **Description**: Issue `pg_notify` in the same transaction as `SaveIrreversibleOutcome`, so the notification exists if and only if the commit does.
- **Pros**: Atomic with the commit.
- **Cons**: Couples the persistence layer to broadcasts and to name resolution; the commit-success callback already gives "only after the commit".
- **Rejection Reason**: Coupling for a guarantee the tick already provides.

## Consequences

### Positive
- No new process, service or library: uses PostgreSQL and Npgsql, which every zone already runs (ADR-006, ADR-007).
- Works unchanged across machines that share the database (R6).
- Local delivery keeps the exact behaviour of a zone-wide message, so AC-NC-47 and the outbox tests hold.
- The tick never blocks: publish is tracked, receive is drained.
- The allowlist keeps the relay from turning into a cross-system bus.
- EditMode tests run several zones on the in-memory bus, with no database.

### Negative
- One extra dedicated database connection and one extra thread per zone process (4 zones: 4 connections; the listener connection is unpooled, so it is outside the per-zone pool; that pool is `Maximum Pool Size=10` since ADR-006 Amendment 3, 2026-10-19, which counts 4 × 10 pooled + 4 listener connections = 44 against `max_connections` = 100).
- Messages are lost while a listener reconnects; a zone can miss a +9 announcement after a database restart.
- A relayed message reaches other zones' clients one tick later than the producer's own clients (≤ 50 ms + database round trip).
- The relay is down whenever PostgreSQL is down — but so is every commit, so no +9 can happen then.

### Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Npgsql notification path stripped or misbehaving under IL2CPP Linux | MEDIUM | Verification Required 1–2; `link.xml` entry per ADR-006; the in-memory bus keeps all game-side tests independent of it |
| Npgsql version not pinned; `Wait(int)` / `Notification` could differ in the version chosen | MEDIUM | Verification 0 against the version Character Persistence Story 012 pins; the game-side relay depends only on the interfaces above |
| Listener thread blocks shutdown, or the connection is disposed under a thread blocked in `Wait` | LOW | Background thread; bounded `Wait`; join with timeout; only the listener thread disposes its connection (Verification 4) |
| Listener thread survives leaving Play mode / a domain reload in the Editor | LOW | Created only by the zone composition root; tests use the in-memory bus; the server host's `OnDestroy` stops it, since ADR-009 teardown does not run on leaving Play mode (Decision 3, Editor; Verification 6) |
| A publish blocks the tick waiting for a pooled connection | LOW | Connection opened inside the tracked task through `NpgsqlDataSource`, never a synchronous `Open()` on the tick (Decision 2; Verification 5) |
| Notifications lost between a reconnect and the re-issued `LISTEN` | LOW | Accepted by Decision 6 (best-effort); counted in the reconnect log |
| A hung `pg_notify` holds an in-flight slot | LOW | `RELAY_PUBLISH_TIMEOUT_MS` cancellation through the tracked CTS (Verification 5). Verified for an exhausted pool. Against a frozen server the slot is held until the ADR-011 watchdog (2026-10-19, TD-074, accepted) |
| A burst of relayed messages floods clients' priority path | LOW | Allowlisted types only; inbox capacity 64; not cap-exempt, so the per-client cap still applies |
| Relay grows into a general bus over time | MEDIUM | Decision 4: one allowlist, a row added only by a story naming the GDD rule; registry forbidden pattern |
| A bad or hostile payload on the channel (any database user can `NOTIFY`) | LOW | Database is loopback-only with the `zone_server` role (ADR-007); the listener validates version, type, length and base64 and drops anything else |

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| enhancement-system.md | CR-ENH-14 / UI-ENH-3 — `ServerBroadcast_Enhancement9` to all online players | Decision 1 (own zone) + Decisions 2–3, 7 (every other zone) |
| enhancement-system.md | CR-ENH-15 step 8 — broadcast only after the commit | Decision 6: called from the ADR-011 commit-success callback only |
| enhancement-system.md | EC-ENH-8 — a delivery failure does not affect the outcome | Decision 1: publish failure is logged only; Decision 6: best-effort |
| networking-channel-contract.md | `ServerBroadcast_Enhancement9` — S→ALL (all connected clients, every zone), R-OD, P1 | Local delivery through the zone outbox (R-OD, P1); relay for every other zone |
| networking-wire-protocol.md | AC-NC-47 — the broadcast is not cap-exempt; body 4–52 bytes | `isCapExempt: false` on both paths; allowlist row caps the body at 52 bytes |
| gdd-cross-review-2026-05-23.md | W-2 — no channel for the cross-zone +9 broadcast | Closed by this ADR |

## Performance Implications
- **CPU**: negligible — one `pg_notify` per +9; the listener thread is idle in `Wait`; step 1a costs one empty-queue check per tick.
- **Memory**: inbox 64 × ≤ 52 B; one connection and one thread stack per zone (~1 MB stack reserved).
- **Load Time**: zone start opens one more connection (< 10 ms on loopback).
- **Network**: one notification of < 100 bytes per +9 over loopback; client bandwidth unchanged from a zone-wide message.

## Migration Plan
New code only; nothing existing is replaced.
1. `RelayEnvelopeCodec`, `CrossZoneRelayAllowlist`, `CrossZoneRelayInbox`, `ServerWideBroadcaster`, `IZoneClientBroadcastSink` on `ClientMessageOutbox`, `InMemoryCrossZoneRelayBus`, and `ZoneTickPipeline` step 1a — EditMode-testable, no database (Networking Core story).
2. `NpgsqlRelayPublisher`, `NpgsqlRelayListener`, composition-root wiring, both stop paths (ADR-009 teardown step 2a and the server host's `OnDestroy`) — with Verification Required 0–5 on the headless build and 6 in the Editor (Networking Core story; depends on Character Persistence Story 012).
3. Enhancement Story 016 calls `IServerWideBroadcaster.Broadcast` from Story 015's commit-success delivery. Story 016 and the client story that handles `ServerBroadcast_Enhancement9` note that the message can arrive before `SessionReady` (Decision 1).
4. ADR-014 receives a dated note on Decision 6 (step 1a). ADR-009 receives a dated note on Decision 3: step 2a "stop relay listener (`NpgsqlRelayListener.Stop()`)" after the tick loop stops — this adds to open item C2 (teardown order) in `architecture-traceability.md`. `networking-channel-contract.md` gets a note on the `ServerBroadcast_Enhancement9` row naming this ADR as the cross-zone path.

## Validation Criteria
- EditMode: two zones on `InMemoryCrossZoneRelayBus` — a broadcast in zone A reaches every client of A in A's tick and every client of B in B's next tick, exactly once each; A does not deliver its own echo twice.
- EditMode: a type not on the allowlist is refused on send and dropped on receive; malformed and wrong-version payloads are dropped; a full inbox drops and counts.
- EditMode: a failing publisher leaves local delivery and the caller unaffected (EC-ENH-8).
- Headless build: Verification Required 1–5 recorded in `production/qa/evidence/`; Editor: Verification 6 recorded there too.

## Related Decisions
- ADR-002 (one zone per process), ADR-004 (NGO, outbox), ADR-006 (PostgreSQL, Npgsql), ADR-007 (single VPS, gateway), ADR-009 (zone teardown), ADR-010 (no central bus), ADR-011 (tick completion queue), ADR-012 (assembly boundary), ADR-013 (random provider), ADR-014 (tick order — amended by Decision 7)
- `production/epics/enhancement-system/story-016-enhancement9-broadcast-delivery.md`
- `design/gdd/gdd-cross-review-2026-05-23.md` W-2
