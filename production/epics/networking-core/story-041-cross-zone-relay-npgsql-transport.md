# Story 041: Cross-Zone Relay — the PostgreSQL Transport (Publisher, Listener, Stop)

> **Epic**: Networking Core
> **Status**: Ready — written 2026-10-16 (`/create-stories`, ADR-015 Migration Plan step 2). `/story-readiness` 2026-10-19: NEEDS WORK (7 gaps), fixed the same day at the user's request. Dependencies Character Persistence Story 012 (Complete 2026-10-19, Npgsql 8.0.9) and Story 040 (Complete 2026-10-16) are met. Verification platform: Windows Dedicated Server build, Mono (user decision 2026-10-19, see below).
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-16
> **Estimate**: 14 hours (Story 040 was 12 h; this adds a background thread, reconnect with backoff and a two-process run)

*Added 2026-10-16 after ADR-015 was Accepted. Story 040 builds the relay behind interfaces and tests it on an in-memory bus. This story puts PostgreSQL `LISTEN` / `pg_notify` behind the same interfaces and proves, on the headless server build and in the Editor, the runtime behaviour ADR-015 lists as Verification Required 0–6.*

> **User decision 2026-10-16 (`/create-stories`).** No server composition root, no ADR-009 teardown code and no server host `MonoBehaviour` exist yet. This story builds `NpgsqlRelayListener.Stop()` and tests it by calling it. **The two call sites — ADR-009 teardown step 2a and the server host's `OnDestroy` — belong to the story that builds the server composition root and the ADR-009 teardown sequence (open item C2 in `architecture-traceability.md`)**, and ADR-015 Verification 4 (teardown) and 6 (leaving Play mode) move with them. This story records them as a requirement on that story; it does not write a host or a teardown.

> **User decision 2026-10-19 (`/story-readiness`): the run uses a Windows Dedicated Server build on Mono, not the IL2CPP Linux server.** The dev machine has no Linux build support, no IL2CPP, no Docker and no WSL. VR-0, 1, 3 and 5 run on two Windows Dedicated Server processes (Mono, development build, `-batchmode -nographics`) against the portable PostgreSQL 16.15 (`tools/postgres/README.md`, "Without Docker"). Precondition: the Unity Hub module "Windows Dedicated Server Build Support" for 6000.3.10f1, the only target that defines `UNITY_SERVER` and so compiles `IronGrind.ServerLogic` and the Npgsql DLLs. **VR-2 (IL2CPP stripping) is NOT RUN here and is carried by the epic's headless-build gate** (with Character Persistence Story 012 (b)–(d)); it does not block `/story-done`. The Mono / Windows deviation is recorded in the evidence file.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-14, EC-ENH-8. `design/gdd/networking-channel-contract.md` — the `ServerBroadcast_Enhancement9` row (`S→ALL`). No rule or value of a GDD changes.
**Requirement**: `TR-enh-010` (the cross-zone half of the delivery path)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: **ADR-015: Cross-Zone Message Relay** — Decisions 2, 3 and 6; Verification Required 0–6; Migration Plan step 2; Risks.
Also: **ADR-006** (PostgreSQL + Npgsql; the zone's `NpgsqlDataSource`; `link.xml`, Verification 2); **ADR-007** (loopback database, `zone_server` role, IL2CPP server build); **ADR-011** (`ITickCompletionQueue.Track` with a caller-owned CTS; the 200-tick watchdog); **ADR-009** (teardown step 2a, built elsewhere — see the decision above); **ADR-012** (Npgsql referenced by `IronGrind.ServerLogic` only).

**ADR Decision Summary**: the publisher runs one `SELECT pg_notify(@channel, @payload)` per relayed message through the zone's pooled data source, inside a task tracked by `ITickCompletionQueue` and cancelled after `RELAY_PUBLISH_TIMEOUT_MS`, never awaited; each zone runs one listener on a dedicated background thread with its own unpooled connection that `LISTEN`s, loops on `Wait`, hands every payload to Story 040's receiver, reconnects with backoff on any failure, and stops on `Stop()`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — no engine API; a background thread blocking in Npgsql 8.0.9 `Wait`. Behaviour not yet run; IL2CPP behaviour unproven (VR-2, carried).
**Engine Notes**: ADR-015 Engine Compatibility. Npgsql is pinned at **8.0.9** (netstandard2.1, Character Persistence Story 012). `NpgsqlDataSource.CreateCommand(string)`, `NpgsqlConnection.Wait(int)`, `NpgsqlConnection.Notification` and `NpgsqlNotificationEventArgs.Payload` are present in that version's `Npgsql.xml`. See `docs/engine-reference/unity/plugins/npgsql-dapper.md` §"API members present in Npgsql 8.0.9", the documentation half of VR-0. The relay uses no Dapper, so TD-073 does not apply. `Assets/link.xml` (Story 012) preserves `Npgsql`. ADR-007's IL2CPP server backend is not run in this story (user decision 2026-10-19 above).

**Control Manifest Rules (Foundation, v2026-10-16)**:
- Required: publish through `dataSource.CreateCommand(sql)` + `ExecuteNonQueryAsync(token)`, the task handed to `ITickCompletionQueue.Track` with a CTS cancelling after `RELAY_PUBLISH_TIMEOUT_MS` (command timeout the same); the callback logs a failure or timeout, frees the in-flight slot and disposes the CTS; at most `RELAY_MAX_PUBLISHES_IN_FLIGHT` in flight, a publish over it dropped and logged — ADR-015, ADR-011
- Required: the listener owns one `NpgsqlConnection` (`Pooling=false`, `Multiplexing=false`, `Tcp Keepalive=true`) and one `Thread` (`IsBackground = true`); `LISTEN irongrind_relay`, then `Wait(RELAY_LISTENER_WAIT_MS)` until stop; the loop catches every exception, disposes, logs (error after `RELAY_RECONNECT_ALERT_AFTER`), backs off 500 ms doubling to `RELAY_RECONNECT_MAX_BACKOFF_MS` (cut short by stop), reconnects — ADR-015
- Required: `Stop()` sets the flag and joins for at most `RELAY_LISTENER_STOP_TIMEOUT_MS`; idempotent; only the listener thread disposes its connection; a thread not stopped in time is logged — ADR-015
- Required: Npgsql is referenced by `IronGrind.ServerLogic` only — ADR-012, ADR-006
- Forbidden: a synchronous `Open()` or `ExecuteNonQuery` on the tick; awaiting the publish — ADR-015, ADR-011
- Forbidden: a thread-pool thread, a pooled or multiplexed listener connection, Npgsql's protocol-level `Keepalive` on it — ADR-015
- Forbidden: disposing the listener connection from `Stop()` or any other thread — ADR-015
- Forbidden: `pg_notify` inside the persistence transaction of the commit — ADR-015
- Forbidden: creating `NpgsqlRelayListener` in an EditMode or PlayMode test — ADR-015
- Guardrail: Verification 0–6 pending; one extra unpooled connection and one thread per zone process — ADR-015

---

## Acceptance Criteria

*The behaviour is tested in EditMode through seams over the Npgsql calls (the listener's loop and the publisher's bookkeeping run against fakes); the real Npgsql behaviour is shown in the run below. No EditMode or PlayMode test creates a real `NpgsqlRelayListener` thread on a database.*

**Publisher (EditMode, fake command seam and a real `TickCompletionQueue`)**
- [ ] **Never blocks, never awaits**: `Publish` starts the command task through the seam and returns; it calls no synchronous `Open()` or `ExecuteNonQuery`; a source search finds no `.Open()`, `.Wait()`, `.Result` or `await` in the publisher's file.
- [ ] **Envelope**: the command's channel parameter is `RELAY_CHANNEL_NAME` and its payload parameter is `RelayEnvelopeCodec`'s encoding with this process's `originId` (Story 040).
- [ ] **Tracked, cancelled, disposed**: the task is handed to `ITickCompletionQueue.Track` with a CTS set to cancel after `RELAY_PUBLISH_TIMEOUT_MS`; a task that never completes is cancelled and its callback runs on the tick after the cancellation; on success, failure and cancellation the callback frees the in-flight slot and disposes the CTS (asserted on a CTS the seam exposes); a failure or a timeout logs one warning.
- [ ] **In-flight bound**: with `RELAY_MAX_PUBLISHES_IN_FLIGHT` publishes pending, the next `Publish` starts nothing, logs one warning and returns; once one completes, a new publish starts.
- [ ] **Never throws**: a seam that throws synchronously frees the slot, disposes the CTS, logs, and `Publish` returns normally; Story 040's `ServerWideBroadcaster` local delivery is unaffected (re-run Story 040's failing-publisher case with this publisher).

**Listener loop (EditMode, fake connection seam, the loop run on the test thread or a test-owned thread)**
- [ ] **Start**: the loop opens the connection, issues `LISTEN` on `RELAY_CHANNEL_NAME`, then calls `Wait(RELAY_LISTENER_WAIT_MS)` repeatedly until stop.
- [ ] **Notification**: a notification raised by the fake is passed, as its payload string, to Story 040's `CrossZoneRelayReceiver.OnPayload` and to nothing else.
- [ ] **Failure and reconnect**: an exception of each type from `Open`, `LISTEN` or `Wait` (`NpgsqlException`, `IOException`, `ObjectDisposedException`, `InvalidOperationException`) does not escape the loop: the connection is disposed, a warning is logged, the loop waits 500, 1 000, 2 000 … ms capped at `RELAY_RECONNECT_MAX_BACKOFF_MS` (asserted through an injected delay seam), reopens and re-issues `LISTEN`; the `RELAY_RECONNECT_ALERT_AFTER`-th consecutive failure logs an error; a successful `LISTEN` resets the count and the backoff.
- [ ] **Stop**: `Stop()` sets the flag; the loop leaves within one `Wait`, or at once if it is in a backoff delay; the loop disposes the connection on its way out; `Stop()` itself never touches the connection.
- [ ] **Idempotent**: a second and third `Stop()` return at once; `Stop()` before start and after the thread has ended returns at once; none throws.
- [ ] **Join timeout**: a fake `Wait` that never returns makes `Stop()` return after `RELAY_LISTENER_STOP_TIMEOUT_MS` and log one warning naming the timeout.

**Construction (EditMode, no database)**
- [ ] **Connection string**: the listener's connection string, built from the zone's base string, has `Pooling=false`, `Multiplexing=false`, `Tcp Keepalive=true` and no `Keepalive`; asserted with `NpgsqlConnectionStringBuilder`, without connecting.
- [ ] **Thread**: the listener's thread is a dedicated `Thread` with `IsBackground = true` and a name; no `Task.Run`, `ThreadPool` or `async` in the listener's file (source search).
- [ ] **Tuning knobs**: the seven `RELAY_*` values this story uses (all of ADR-015's except `RELAY_INBOX_CAPACITY`, owned by Story 040) are `public const` defaults with ADR-015's values, passed in through constructor parameters, the same pattern as `CrossZoneRelayInbox` (`RELAY_INBOX_CAPACITY`); no other literal for them in the code.
- [ ] **Data source**: `NpgsqlRelayPublisher` takes an `NpgsqlDataSource` in its constructor and never creates or disposes it; the listener takes the zone's base connection string. In this story the verification host creates both from `IRONGRIND_DEV_PG`. The zone's production data source belongs to the composition-root story.
- [ ] **Assembly**: `NpgsqlRelayPublisher` and `NpgsqlRelayListener` are in `src/ServerLogic/Networking/CrossZoneRelay/`, `IronGrind.ServerLogic`; the client-binary scan and the boundary test pass.

**Verification in a real run (evidence document)**

*Recorded in `production/qa/evidence/networking-core-041-relay-verification.md`, PASS / FAIL / NOT RUN with the observation, on two **Windows Dedicated Server (Mono, development) processes** on one machine against the portable PostgreSQL 16.15 (user decision 2026-10-19). The processes run `RelayVerificationHost` (see Implementation Notes); it is not the composition root. A FAIL is recorded and escalated and does not by itself block `/story-done`. An item NOT RUN blocks it, except VR-2 (carried by the headless-build gate) and VR-4 and VR-6 (moved).*

- [ ] **VR-0 Npgsql version**: the pinned version's documentation for `NpgsqlConnection.Wait(int)`, `Notification` and `NpgsqlDataSource.CreateCommand` is cited, and the code compiles against it.
- [ ] **VR-1 Cross-process notification**: process B, its listener blocking in `Wait`, receives a `pg_notify` sent by process A on its listener thread (thread id logged); the main thread is not involved.
- [ ] **VR-2 Stripping** — **NOT RUN in this story** (no IL2CPP on the dev machine; user decision 2026-10-19). Recorded as carried by the epic's headless-build gate, naming `Assets/link.xml`'s `Npgsql` entry as the expected cover.
- [ ] **VR-3 Database restart**: PostgreSQL restarted while both zones run → the listener logs the loss, reconnects with backoff, re-issues `LISTEN`; relayed messages flow again; no notification is delivered twice after the reconnect; the tick never stalls (tick duration logged across the restart).
- [ ] **VR-5 Publish never blocks**: with the ADR-006 pool exhausted (all connections held by a test), a publish from the tick does not lengthen the tick; a publish against a hung server is cancelled after `RELAY_PUBLISH_TIMEOUT_MS` and frees its slot.
- **VR-4 Teardown** and **VR-6 Leaving Play mode** — **moved** to the composition-root / ADR-009 teardown story (user decision 2026-10-16). Not run here; recorded in the evidence file as "moved", with the story that owns them.

---

## Implementation Notes

*Derived from ADR-015 Decisions 2, 3 and 6.*

- **Seams.** Keep the Npgsql calls behind two small internal seams — a command starter for the publisher (`Task<int> Start(string channel, string payload, CancellationToken)`) and a connection for the listener (`Open`, `Listen(channel)`, `Wait(int)`, the notification callback, `Dispose`) — so the loop and the bookkeeping are tested in EditMode. The production implementations are thin and are what the run exercises.
- **Publish.** `dataSource.CreateCommand("SELECT pg_notify(@channel, @payload)")`, the two parameters, `CommandTimeout` set from `RELAY_PUBLISH_TIMEOUT_MS`, then `ExecuteNonQueryAsync(token)`; dispose the command in the continuation or rely on the data-source command disposing its connection — check the pinned version's documentation and state which. `Track(task, cts, OnPublishFinished)`, a method group, not a lambda capturing state per call (ADR-010) — keep the in-flight count in a field. `ITickCompletionQueue` never disposes a caller-owned CTS: the callback does.
- **Listener.** `new Thread(Run) { IsBackground = true, Name = "IronGrindRelayListener" }`. A `volatile bool` stop flag. The backoff delay waits on a `ManualResetEventSlim` (or similar) that `Stop()` sets, so a stop cuts it short. Subscribe to `Notification` with a method, not a lambda; pass `e.Payload` to the receiver. The receiver allocates nothing of its own; Npgsql allocates the payload string and event args per notification (off the tick, a few per hour) — state it in the remarks.
- **Own notifications.** The listener receives this zone's own `pg_notify`; Story 040's receiver drops it by `originId`. Nothing to add here.
- **No order, at most once** (Decision 6). Publishes run on different pooled connections, so even one zone's notifications may arrive out of order; do not try to order them.
- **Not in this story** (user decision 2026-10-16): calling `Stop()` from the ADR-009 teardown (step 2a, after the tick loop has stopped, before the process exits) and from the server host `MonoBehaviour.OnDestroy`. When the composition-root / teardown story is written, it must take both call sites and ADR-015 Verification 4 and 6. Add a line for it to the epic's blocker list.
- **`link.xml`.** ADR-006's entry (`<assembly fullname="Npgsql" preserve="all" />`) is expected to cover the notification path; VR-2 confirms it. If Character Persistence Story 012 places `link.xml` differently, follow it.
- **Verification host.** `src/ServerLogic/Networking/CrossZoneRelay/Verification/RelayVerificationHost.cs` is a server-only `MonoBehaviour` in `IronGrind.ServerLogic`. It reads `IRONGRIND_DEV_PG` and a zone name from the command line, builds Story 040's broadcaster, inbox and receiver plus this story's publisher and listener, and runs a minimal tick that drains the inbox. It logs every received payload with the thread id, plus the tick duration, and publishes a test `ServerBroadcast_Enhancement9` on a command-line interval. Its `OnDestroy` calls `Stop()`. This is a test call site; the production ones stay with the composition-root story. It lives in the scene `Assets/Scenes/Verification/RelayVerification.unity`, built manually from a Build Profile (Windows Server, Mono, Development Build, that scene only) to `Builds/RelayVerification/`, which is added to `.gitignore`. The build settings go in the evidence file. No build script.

---

## Out of Scope

- Story 040: everything behind the interfaces (broadcaster, inbox, receiver, codec, allowlist, step 1a, in-memory bus)
- The server composition root; wiring the publisher and listener into a zone; the ADR-009 teardown sequence; the server host `MonoBehaviour`; ADR-015 Verification 4 and 6 (user decision 2026-10-16)
- Getting Npgsql into the project and pinning its version (Character Persistence Story 012)
- Durable or ordered relay delivery (declined by ADR-015 Decision 6)
- Database roles and `pg_hba.conf` (ADR-007)

---

## QA Test Cases

**Files** — to be created: `src/ServerLogic/Networking/CrossZoneRelay/NpgsqlRelayPublisher.cs`, `NpgsqlRelayListener.cs` and their seams; `tests/EditMode/Networking/CrossZoneRelay_Npgsql_tests.cs`; `Verification/RelayVerificationHost.cs` and `Assets/Scenes/Verification/RelayVerification.unity`; `production/qa/evidence/networking-core-041-relay-verification.md`.

- **Publish returns at once** — a seam whose task never completes → `Publish` returns; after `RELAY_PUBLISH_TIMEOUT_MS` the callback runs on the tick, logs a timeout, frees the slot, the CTS is disposed.
- **Publish success / failure** — completed task → slot freed, CTS disposed, nothing logged; faulted task → one warning, slot freed, CTS disposed.
- **Bound** — 8 pending, 9th → nothing started, one warning; complete one → the next publish starts.
- **Throwing seam** — synchronous throw → no exception, slot freed, CTS disposed, one warning; local delivery of Story 040's broadcaster unaffected.
- **Payload** — the seam receives the channel name and a payload that Story 040's codec decodes to the published type, body and this origin.
- **Loop start** — open, `LISTEN irongrind_relay`, then `Wait` calls.
- **Notification** — fake raises one → the receiver gets that payload once.
- **Reconnect** — each of the four exception types at `Open`, `LISTEN`, `Wait` → disposed, warning, backoff 500/1000/2000/…/10000 ms, reconnect, `LISTEN` again; 5th consecutive → error; a success resets.
- **Stop** — during `Wait` → leaves within one wait; during backoff → leaves at once; connection disposed by the loop, never by `Stop()`.
- **Idempotent** — `Stop()` ×3, before start, after end → all return at once, no throw.
- **Join timeout** — a `Wait` that never returns → `Stop()` returns after the timeout with one warning.
- **Connection string** — builder shows `Pooling=false`, `Multiplexing=false`, `Tcp Keepalive=true`, no `Keepalive`.
- **Source searches** — no `.Open()`, `.Wait()`, `.Result`, `await` in the publisher; no `Task.Run`, `ThreadPool`, `async` in the listener.
- **Run** — VR-0, 1, 3, 5 each with its observation; 2 recorded NOT RUN (carried); 4 and 6 recorded as moved.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/CrossZoneRelay_Npgsql_tests.cs` — must exist and pass, with the full EditMode suite; `production/qa/evidence/networking-core-041-relay-verification.md` — VR-0, 1, 3 and 5 PASS or FAIL with the observation, **none NOT RUN**; VR-2 NOT RUN, carried by the headless-build gate; VR-4 and VR-6 recorded as moved.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Character Persistence Story 012** (Complete 2026-10-19, Npgsql 8.0.9); **Story 040** (Complete 2026-10-16; the interfaces, `CrossZoneRelayReceiver`, `RelayEnvelopeCodec`, `CrossZoneRelayOrigin`); ADR-015 (Accepted 2026-10-16); Story 030 (`ITickCompletionQueue`) — Complete. Evidence-run precondition (not a code dependency): Unity Hub module "Windows Dedicated Server Build Support" for 6000.3.10f1.
- Unlocks: the composition-root / ADR-009 teardown story (it wires the publisher and listener and calls `Stop()` from both stop paths); cross-zone delivery in a real deployment. Enhancement Story 016 does **not** wait for this story.
