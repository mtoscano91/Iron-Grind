# Story 044: Composition Wiring — Connection Pool and Warm-up, the Object Graph, Listener, Dispose

> **Epic**: Networking Core
> **Status**: Ready — `/story-readiness` 2026-10-22: NEEDS WORK (6 gaps: performance note, file list, missing seams, constructor wiring, `isDevelopmentBuild` source, provisional estimate) → fixed. The fixes also found that `NpgsqlRelayListener` does not implement `IZoneRelayListener` yet (AC-8), that no S5 object is `IDisposable` (AC-9 rewritten), and that AC-10 puts the real backend in `ZoneServerComposition.cs`. Re-check READY. Estimate confirmed (12 h).
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 12 hours (confirmed at `/story-readiness` 2026-10-22: about 9 h for the code and the EditMode wiring tests, 3 h for the Database-category tests and their run against the portable PostgreSQL)

*Added 2026-10-22 (`/create-stories networking-core`, ADR-009 Amendment 1 Migration Plan step 2, third of four). Replaces Story 043's faked backend steps (S3, S4, S5, S7) with the real objects. Carries the EPIC Known Gap items from 2026-10-16 / 2026-10-19: the data-source warm-up before the first tick (Story 041 VR-1: the first publish of a fresh process cost 58 ms on the tick) and the Story 014 `NpgsqlCharacterRecordStore` wiring. User decision 2026-10-22: S5 builds the networking and persistence objects only; game systems and static data are wired by later stories in their own epics.*

## Context

**GDD**: `design/gdd/networking-core.md` (tick pipeline, outbox, relay as the substrate other systems plug into); `design/gdd/character-persistence.md` (the store the service writes through). No rule or value of a GDD changes.
**Requirement**: `TR-net-008` (the tick-driven pipeline exists and is registered before the tick starts); `TR-cp-006` (character writes through the Npgsql store — wiring only; the store is Character Persistence Story 014)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: **ADR-009 Amendment 1** — Decision 2 steps S3, S4, S5, S7; Decision 3 step 7 (Dispose pass, data source last); Decision 5 (the only place that constructs).
Also: **ADR-006 Amendment 3** (pool string, password from `IRONGRIND_PG_PASSWORD`, never logged); **ADR-013** Decision 4 (one `IRandomProvider`, created by the composition root); **ADR-014** (dispatcher constructor, registration then `Seal()`, the one `RegisterTickDriven`); **ADR-015** Decisions 1–3 (relay objects; the listener's own unpooled connection); **ADR-011** Decision 2 (the warm-up is one of exactly two blocking waits).

**ADR Decision Summary**: `ZoneServerComposition` builds one `NpgsqlDataSource` from server config plus the environment password, proves it with a bounded warm-up before any tick, constructs the zone's object graph in a fixed order without I/O, starts the relay listener on its own unpooled connection, and is the only code that constructs any of it; the Dispose pass releases it in reverse, the data source last.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — Npgsql 8.0.9 (`NpgsqlDataSource.Create`, `NpgsqlConnectionStringBuilder`) on Mono in the Editor and the Windows Dedicated Server build; IL2CPP Linux (A1-7) is carried by the epic's headless-build gate. `Task.Run(...).Wait(timeout)` keeps the warm-up off any captured context.
**Engine Notes**: Npgsql 8.0.9 and Dapper 2.1.66 per `docs/server-libraries.md` and `docs/engine-reference/unity/plugins/npgsql-dapper.md`; compiled only under `UNITY_SERVER || UNITY_EDITOR` (`IronGrind.ServerLogic` define constraints). TD-073 (Dapper on IL2CPP) unchanged. `Assets/link.xml` already preserves the Story 014 DTOs.

**Control Manifest Rules (Foundation, v2026-10-19)**:
- Required: one `NpgsqlDataSource` per zone process, `Timeout=5;Command Timeout=5;Pooling=true;Minimum Pool Size=2;Maximum Pool Size=10;Connection Idle Lifetime=60`, built at S4 with `NpgsqlConnectionStringBuilder` from server config and the password from `IRONGRIND_PG_PASSWORD` — ADR-006 A3
- Required: the composition root creates exactly one `IRandomProvider` with `RandomProviderFactory.CreateSeededFromEntropy(out int seed)` and passes that instance to every system it constructs; the seed log is credential-grade — ADR-013
- Required: the composition root registers each request handler and connection-level sink, then calls `Seal()`; the dispatcher gets `BuildConfiguration.IsDevelopmentBuild` — ADR-014
- Required: `ZoneTickPipeline.Tick` is the one tick-driven delegate of `ServerTickLoop` — ADR-014
- Required: `NpgsqlRelayListener` is created only by the server composition root of a running zone; its connection string states `Pooling=false`, `Multiplexing=false`, `Tcp Keepalive=true` — ADR-015
- Required: `CrossZoneRelayAllowlist` built by the composition root, one row (`ServerBroadcast_Enhancement9`, 52 bytes) — ADR-015
- Forbidden: constructing a server system, an `IRandomProvider`, an `NpgsqlDataSource`, the relay or the `ZoneTickPipeline` outside `ZoneServerComposition` (tests construct directly) — ADR-009 A1, ADR-013, ADR-006 A3
- Forbidden: logging the connection string; a password in server config — ADR-006 A3
- Forbidden: calling `ServerTickLoop.RegisterTickDriven` for game logic — ADR-014
- Guardrail: blocking waits — the S4 warm-up (`DB_WARMUP_TIMEOUT_MS` = 5 000; timeout or fault → S-fail) is one of exactly two — ADR-009 A1, ADR-011
- Guardrail: database connections per zone process at most 10 pooled + 1 unpooled listener — ADR-006 A3, ADR-015

---

## Acceptance Criteria

*From ADR-009 Amendment 1 Decision 2 (S3, S4, S5, S7), Decision 3 step 7 and Decision 5, scoped to this story:*

- [ ] **AC-1** S3 calls `RandomProviderFactory.CreateSeededFromEntropy(out seed)` exactly once per process, logs the seed on one startup line only, and keeps the instance in an internal `RandomProvider` property. No object S5 builds in this story takes an `IRandomProvider`. The game-system stories pass this same instance to every system they add.
- [ ] **AC-2** S4 builds the connection string with `NpgsqlConnectionStringBuilder` from `ZoneServerConfig` (host, port, database, username, `Timeout`, `CommandTimeout`, `Pooling=true`, `MinPoolSize`, `MaxPoolSize`, `ConnectionIdleLifetime`) and the password read from the environment variable `IRONGRIND_PG_PASSWORD` (read through a seam so tests supply it).
- [ ] **AC-3** S4 fails (S-fail at S4) when `IRONGRIND_PG_PASSWORD` is unset or empty; the log line names the variable, not a value.
- [ ] **AC-4** S4 creates the data source with `NpgsqlDataSource.Create(connectionString)` and registers it first for the Dispose pass (so it is disposed last); then warms it with `Task.Run(() => dataSource.CreateCommand("SELECT 1").ExecuteNonQueryAsync(token)).Wait(DbWarmupTimeoutMs)`; a timeout or a fault is S-fail at S4, and on a timeout the token is cancelled.
- [ ] **AC-5** No log line written by the composition at any step, success or failure, contains the connection string, the password, or the builder's `ToString()`; exceptions are logged by type name (and the Npgsql `SqlState` when present).
- [ ] **AC-6** S5 constructs, without I/O, in this order, registering each `IDisposable` for the Dispose pass: `TickCompletionQueue`; `ServerTickLoop`; `NpgsqlCharacterRecordStore(dataSource)`; `CharacterPersistenceService(store)`; relay — `CrossZoneRelayAllowlist.CreateProduction()`, `CrossZoneRelayOrigin`, `CrossZoneRelayInbox(allowlist.LargestMaxBodyBytes)`, `CrossZoneRelayReceiver`, `NpgsqlRelayPublisher(dataSource, queue, origin)`; `CrossCuttingRpcGuardChain()`; `CharacterMutationGate()`; `InboundRequestDispatcher(guardChain, gate, directoryStandIn, activitySinkStandIn, tickLoop, MessageRoutingRegistry.TryGetEntry, BuildConfiguration.IsDevelopmentBuild)`; `ClientMessageOutbox(network.CreateSendSeam())`; `ServerWideBroadcaster(outbox, publisher, allowlist)`; `ZoneClientRelayHandler(outbox)`; the ordered simulation step (empty list); `ZoneTickPipeline(queue, inbox, relayHandler, dispatcher, simulation, outbox, tickLoop)` (the 7-argument constructor). Then `dispatcher.Seal()`, `tickLoop.RegisterTickDriven(pipeline.Tick)`, and `transport = network.CreateInboundTransport(dispatcher, dispatcher, outbox)` (the dispatcher is both the intake and the dispatcher; the outbox is the client-connection listener). S5 ends with `AttachOwnedObjects(queue, outbox, null, transport, currentTick from tickLoop, null)` and `AttachTickLoop(tickLoop)`. No outbound sender is attached: the pipeline flushes the outbox each tick, so S8 skips the sender start.
- [ ] **AC-7** `IConnectionCharacterDirectory` and `IConnectionActivitySink` stand-ins log `"[ZoneServer] <name> not implemented"` once each; the directory finds no character for any connection (requests are then dropped by the dispatcher's existing `NoCharacter` path), the activity sink records nothing.
- [ ] **AC-8** `NpgsqlRelayListener` implements `IZoneRelayListener` (Story 042's interface; its existing `Stop()` is idempotent). S7 constructs `NpgsqlRelayListener(connectionString, receiver)`; the listener applies `Pooling=false`, `Multiplexing=false` and `Tcp Keepalive=true` itself (Story 041). S7 attaches it through a new `internal void AttachListener(IZoneRelayListener)` before calling `Start()`, so teardown step 2a stops it even when `Start()` throws.
- [ ] **AC-9** The data source is registered for the Dispose pass at S4, right after creation. It is the only `IDisposable` this story builds (none of the S5 objects implements `IDisposable`), so it is disposed last. After a failure at S4 (warm-up), at S5 or at S7, the Dispose pass disposes the data source exactly once. After a failure at S3, nothing is disposed.
- [ ] **AC-10** A source search over `src/` (comments stripped) finds `RegisterTickDriven(`, `NpgsqlDataSource.Create(`, `CreateSeededFromEntropy(` and `new NpgsqlRelayListener(` only in `src/ServerLogic/ZoneServer/ZoneServerComposition.cs`, with one named exemption: `src/ServerLogic/Networking/CrossZoneRelay/Verification/RelayVerificationHost.cs` (Story 041's verification call site).
- [ ] **AC-11** Database category (`[Explicit][Category("Database")]`, `Assert.Ignore` without `IRONGRIND_DEV_PG`): against the portable PostgreSQL 16.15, S3–S5 and S7 run with the real backend; the warm-up succeeds; the listener starts; the Dispose pass leaves no connection of this test's application name in `pg_stat_activity`.
- [ ] **AC-12** Database category: a wrong password fails at S4 within `DbWarmupTimeoutMs` + 1 s, exits `StartupFailed`, and no captured log line contains the password or the connection string.
- [ ] **AC-13** Database category: an unreachable host (closed port) fails at S4 by timeout or fault, never hangs past `DbWarmupTimeoutMs` + 1 s.

---

## Implementation Notes

*Derived from ADR-009 Amendment 1 Decision 2, ADR-006 Amendment 3, ADR-014, ADR-015:*

- **Where the real backend lives.** AC-10 allows `NpgsqlDataSource.Create(`, `CreateSeededFromEntropy(`, `RegisterTickDriven(` and `new NpgsqlRelayListener(` only in `ZoneServerComposition.cs`. The real `IZoneBackendSteps` implementation is therefore a private nested class (or private methods) in that file, not a separate `ZoneBackendSteps.cs`. Same step order, same S-fail path. The internal constructor of Story 043 stays for tests; a new constructor (or factory method) without the `backend` parameter builds the real one.
- **Environment.** Read it through a new `IZoneEnvironment.GetVariable(string)` (real: `Environment.GetEnvironmentVariable`), so EditMode tests never set process variables.
- **Warm-up seam.** Tests cannot reference Npgsql types (Story 014 rule). `NpgsqlDataSource.Create` does no I/O, so EditMode tests create a real data source and replace only the warm-up: `internal Func<CancellationToken, Task> WarmUpOverride` on the composition. When null, the real `dataSource.CreateCommand("SELECT 1").ExecuteNonQueryAsync(token)` runs. AC-4's timeout and fault cases use the override.
- **Seam additions** (Story 045 implements them over Unity and NGO):
  - `IZoneNetwork.CreateSendSeam()` returning `IClientMessageSendSeam` (Story 045: `NgoClientMessageSender(networkManager)`).
  - `IZoneNetwork.CreateInboundTransport(IInboundMessageIntake, IInboundRequestDispatcher, IClientConnectionListener)` returning `IZoneInboundTransport` (Story 045: `NgoInboundTransportShell` adapted to `IZoneInboundTransport`).
  - Story 043's test fakes (`SuNetwork`, `ZsNetwork`) get the new members as stubs; their tests do not change.
- **Performance.** Startup only: S3–S5 and S7 run once per process, and the S4 warm-up is a bounded blocking wait before any tick. The tick path is the pipeline ADR-014 already specifies; this story adds no per-tick work and no per-tick allocation. No impact on the 50 ms server tick budget at 20 Hz.
- Set `ApplicationName` on the builder (e.g. `irongrind-zone-<zoneId>`) — needed for AC-11's `pg_stat_activity` check and harmless in production.
- Build the string once; keep it in a private field used only by S4 and S7 and cleared after S7 (never in a property, never logged). The listener derives its own unpooled string from it (`NpgsqlRelayListener` builds it — do not duplicate that code).
- The ordered simulation step is a small class in `src/ServerLogic/ZoneServer/` implementing `IZoneSimulationStep` over an explicit `IReadOnlyList` the composition fills (empty here; game-system stories append in the order the composition lists — manifest: phase 3 order listed by the composition root).
- Send seam: `ClientMessageOutbox(IClientMessageSendSeam)` — the composition gets the seam from the NGO seam (`IZoneNetwork.CreateSendSeam()`; Story 045 returns `NgoClientMessageSender(networkManager)`); tests pass a fake.
- `isDevelopmentBuild`: the composition passes `BuildConfiguration.IsDevelopmentBuild` (ADR-014, manifest), not a seam. The dispatcher's debug and release paths are already tested directly with literal `true`/`false` by its own tests.
- The Database test follows Character Persistence Story 014's pattern: `tests/EditMode/Networking/ZoneServer_CompositionDatabase_tests.cs`, `[Explicit][Category("Database")]`, connection settings from `IRONGRIND_DEV_PG` (portable PG per `reference_portable_postgres` memory), run with `-testFilter`. No Npgsql type is referenced from test code — expose what the test needs through an internal ServerLogic test-access class.
- Source search (AC-10): reuse the comment-aware source search helper from Story 014's tests.
- Doc comments on every public type and member.

### Files
- **Modify:**
  - `src/ServerLogic/ZoneServer/ZoneServerComposition.cs`: the real backend (S3, S4, S5, S7), `RandomProvider`, `AttachListener`, `WarmUpOverride`, `ConstructionObserver`, the connection-string field.
  - `src/ServerLogic/ZoneServer/IZoneNetwork.cs`: `CreateSendSeam()` and `CreateInboundTransport(...)`.
  - `src/ServerLogic/ZoneServer/ZoneServerStandIns.cs`: the `IConnectionCharacterDirectory` and `IConnectionActivitySink` stand-ins.
  - `src/ServerLogic/Networking/CrossZoneRelay/NpgsqlRelayListener.cs`: implements `IZoneRelayListener`.
  - `tests/EditMode/Networking/ZoneServer_CompositionStartup_tests.cs` and `ZoneServer_CompositionTeardown_tests.cs`: fake stubs only.
- **Create:**
  - `src/ServerLogic/ZoneServer/IZoneEnvironment.cs`.
  - `src/ServerLogic/ZoneServer/ZoneSimulationStep.cs`: the ordered simulation step.
  - `src/ServerLogic/ZoneServer/ZoneCompositionDatabaseTestAccess.cs`: internal, for AC-2 (builder output), AC-9 (data source disposed flag) and AC-11; no Npgsql type in its signatures.
  - `tests/EditMode/Networking/ZoneServer_CompositionWiring_tests.cs`.
  - `tests/EditMode/Networking/ZoneServer_CompositionDatabase_tests.cs`.

---

## Out of Scope

- Story 042/043: teardown order, startup order, S-fail mechanics, config parsing (already done; this story plugs into them).
- Story 045: the Unity host, real NGO / scene / Application seams, `NgoInboundTransportShell` and `NgoClientMessageSender` construction over the live `NetworkManager`, the local runs.
- Game systems in S5 and their handler registrations (e.g. `EnhancementInboundRegistration.Register`) and the static data loaders — later stories in their epics (EPIC Known Gap).
- Real `IConnectionCharacterDirectory` / `IConnectionActivitySink` (Zone Instancing / session epics).
- Character Persistence 015/016 (blocked on Story 011's open decision).

---

## QA Test Cases

*Written at story creation (lean mode — QL-STORY-READY skipped). The developer implements against these.*

- **AC-1**: one generator
  - Given: a composition with fakes for NGO and the warm-up override. When: startup through S5. Then: `RandomProvider` is set once and keeps the same reference after S5 (`Assert.AreSame`); exactly one log line carries the seed. (The "called once" check is structural: AC-10's source search finds a single call site.)
- **AC-2**: connection string fields
  - Given: config with non-default values and password `p@ss;word`. When: S4 builds the string (test access to the builder output). Then: `NpgsqlConnectionStringBuilder` parsed back has each field equal, `Pooling == true`, password round-trips (the `;` is escaped by the builder).
- **AC-3**: missing password
  - Given: environment seam returns null, then `""`. Then: S-fail at S4, `Quit(1)`, log names `IRONGRIND_PG_PASSWORD`.
- **AC-4**: warm-up timeout
  - Given: `WarmUpOverride` returning a task that never completes (until its token is cancelled), `DbWarmupTimeoutMs` 50. Then: S-fail at S4 after ≈ 50 ms (assert < 1 s), the token cancelled, the data source disposed by the teardown.
  - Edge cases: warm-up task faults → S-fail at S4 with the exception type logged.
- **AC-5**: no secrets in logs
  - Given: captured logs (`LogAssert` / log handler) across a full fake startup, an S4 failure and a teardown. Then: no line contains the password, `Host=`, `Password=` or the full string.
- **AC-6**: construction order
  - Given: an internal `Action<string> ConstructionObserver` on the composition that S5 calls with each constructed type name, and once each for `Seal`, `RegisterTickDriven` and `CreateInboundTransport` (null in production, no allocation when null). Then: the observed sequence equals the AC-6 list; `Seal()` comes after all registrations; `RegisterTickDriven` occurs exactly once, and the delegate registered is the pipeline's `Tick` (one `AdvanceTick` on the attached tick loop runs the pipeline once).
  - Edge cases: registering a handler after `Seal()` throws (existing dispatcher behaviour — assert the composition never does it).
- **AC-7**: stand-ins
  - Given: a request intake for connection 5. When: one tick. Then: the request is dropped as `NoCharacter`; the two stand-in lines logged once each.
- **AC-8**: listener
  - EditMode: `NpgsqlRelayListener` is assignable to `IZoneRelayListener`. With a full fake startup, the listener is attached before `Start()`, so teardown step 2a stops it.
  - Database category: the started listener's `IsRunning` is true after S7 and false after teardown step 2a.
- **AC-9**: Dispose
  - Given: a fault injected at S4 (warm-up), then at S5 (a throwing `CreateSendSeam`), then at S7, each a separate case. Then: the Dispose pass disposes the data source exactly once (observed through an internal disposed flag on the test-access class). Given a fault at S3: no data source exists and nothing is disposed.
- **AC-10**: source search
  - Given: `src/**/*.cs`, comments stripped. Then: the four patterns occur only in the composition file and the named exemption.
- **AC-11**: real database, happy path (Explicit, Database)
  - Given: portable PG running, `IRONGRIND_DEV_PG` set. When: S3–S5, S7, then teardown steps 2a and 7. Then: warm-up < `DbWarmupTimeoutMs`; the listener started (its thread alive; delivery itself was Story 041's run); after Dispose, `pg_stat_activity` shows no row with this test's `application_name` (poll up to 2 s).
- **AC-12**: wrong password (Explicit, Database)
  - Then: S-fail at S4 within the bound; Npgsql `SqlState` `28P01` logged; no secret in logs.
- **AC-13**: unreachable host (Explicit, Database)
  - Given: port with nothing listening. Then: S-fail at S4 within `DbWarmupTimeoutMs` + 1 s.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Networking/ZoneServer_CompositionWiring_tests.cs` — must exist and pass
- `tests/EditMode/Networking/ZoneServer_CompositionDatabase_tests.cs` — Explicit `Database` category, run against the portable PostgreSQL 16.15 with `-testFilter`; results recorded in the story's Completion Notes

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 043 (Complete 2026-10-22, `d4485aa`); Character Persistence Story 014 (Complete); Story 041 (Complete)
- Unlocks: Story 045
