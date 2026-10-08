# Control Manifest

> **Engine**: Unity 6.3 LTS (6000.3)
> **Last Updated**: 2026-10-08
> **Manifest Version**: 2026-10-08
> **ADRs Covered**: ADR-001, ADR-002, ADR-003, ADR-004, ADR-005, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010, ADR-011, ADR-012 (and ADR-006 Amendment 1, 2026-10-01)
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
- Wire message schemas and the code that reads and writes them are `Foundation`. Server-side handlers for inbound messages are registered by the server composition root in `ServerLogic`; client-side handlers by the client composition root in `Client`. A server handler validates the envelope and enqueues for the tick — source: ADR-012, ADR-004, ADR-010
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
- Project owns the 10-byte envelope: `MessageTypeID` (ushort), `SequenceNumber` (uint), `ServerTickNumber` (uint); + `SenderEntityID` (uint) for client→server — source: ADR-004
- `SenderEntityID` is a project EntityID, not NGO's `clientId` — server validates the `clientId ↔ EntityID` mapping — source: ADR-004
- Application-level `RttProbe`/echo on U-U channel is the authoritative OWL source; transport RTT seeds the initial estimate only — source: ADR-004
- Bulk messages exceeding transport MTU use fragmenting reliable-ordered delivery (currently `ReliableFragmentedSequenced` — see guardrails) — source: ADR-004
- Channel guarantee mapping (identifiers are verification-pending — see guardrails): R-OD = guaranteed + in-order; R-U = guaranteed + unordered; U-U = best-effort — source: ADR-004

**Persistence layer — PostgreSQL (ADR-006):**
- PostgreSQL is the sole persistence store; accessed via Npgsql + Dapper — source: ADR-006
- All SQL is explicit parameterized SQL — no dynamic query generation, no ORM change tracking — source: ADR-006
- Optimistic-concurrency UPDATE: `WHERE character_id = @id AND save_version = @expected`; rows-affected = 0 → return `ConcurrencyConflict` — source: ADR-006
- `PendingPurchase` INSERT and `TrySpendGold` UPDATE share one `NpgsqlTransaction` — source: ADR-006
- IL2CPP `link.xml` must preserve Npgsql and Dapper: `<assembly fullname="Npgsql" preserve="all" />` + `<assembly fullname="Dapper" preserve="all" />` — source: ADR-006. The `link.xml` is project-wide and must not be what brings the DLLs into a client build — source: ADR-012
- At-most-one write in flight per `CharacterID` (service-layer per-CharacterID write-queue) — source: ADR-006
- Per-zone-process connection pool: `MaxPoolSize=10, MinPoolSize=2` (ADR-007 authoritative for the connection string; ADR-006's pre-topology estimate of 20 is superseded by ADR-007) — source: ADR-007
- Each non-null `inventory_slots` JSONB entry is `{"item_id": N, "count": C, "enhancement_level": M}` with `0 ≤ M ≤ MAX_ENHANCEMENT_LEVEL`; the deserializer treats a missing `enhancement_level` key as 0 — source: ADR-006 Amendment 1

**Asynchronous persistence in the tick loop (ADR-011):**
- Tick-driven code calls a `Task`-returning persistence method and passes the returned task, in the same statement, to `ITickCompletionQueue.Track`. This applies to every `ICharacterPersistence` call made from tick code (`LoadCharacter`, `SaveSession`, `CreateStub`, `SaveIrreversibleOutcome`, the purchase calls) — source: ADR-011
- `ITickCompletionQueue.Drain()` runs once per tick, before any game logic of that tick. For every tracked task whose `IsCompleted` is true, in the order the tasks were tracked, it invokes the callback on the tick thread with a `TickTaskResult<T>` (`Completed` / `Faulted` / `Canceled` / `TimedOut`). `Drain` reads `Task.IsCompleted` only, never throws into the tick, and an exception thrown by one callback is caught and logged without stopping the rest — source: ADR-011
- `Track` and `Drain` are called from the tick thread only; both assert it — source: ADR-011
- Watchdog: a task still incomplete `PERSISTENCE_WATCHDOG_TICKS` (200 = 10 s) after it was tracked has its `CancellationToken` cancelled, a critical alert raised, and its callback invoked with `TimedOut`. The persistence layer's own `PERSISTENCE_WRITE_TIMEOUT_SECONDS` (5 s, ADR-006) fires first; the watchdog is the backstop. A cancelled write has an unknown outcome. A late completion of a timed-out task is logged and its `Exception` is read — source: ADR-011
- Shutdown drain (`ITickCompletionQueue.DrainOnShutdown(TimeSpan timeout)`): in the zone teardown sequence, after the tick loop has stopped and before `Process.Exit(0)`, one bounded blocking wait on all tracked tasks (timeout = the watchdog duration; `AggregateException` caught, not rethrown), then one last `Drain`. Incomplete tasks after the timeout are logged and alerted. It runs on the explicit shutdown path, not from `OnApplicationQuit` — source: ADR-011, ADR-009
- `ICharacterPersistence` implementations copy everything they need from live game state before their first `await`, on the calling (tick) thread; after it they touch only that copy and the database — source: ADR-011
- `ICharacterPersistence` implementations use `ConfigureAwait(false)` on every `await` (including `await using` / `await foreach`); if an awaited call cannot be trusted not to capture the context, the method runs its body through `Task.Run` after the snapshot. Every `TaskCompletionSource` in the persistence layer and in test fakes uses `TaskCreationOptions.RunContinuationsAsynchronously` — source: ADR-011
- `ICharacterMutationGate`: closed by the irreversible-outcome coordinator before the outcome is applied in memory, opened after the result has been handled (success or rollback; opened in a `finally`). `Close` on an already closed gate is an error — one write in flight per character — source: ADR-011
- The inbound request dispatcher (between the RPC guard chain and game logic) checks the gate once per request. A request whose type is marked `HeldDuringIrreversibleWrite` (every bag-mutating request: move, equip, unequip, discard, sell, buy, consumable use, accessory merge) is appended to the character's hold queue while the gate is closed; other requests are dispatched normally. When the gate opens, held requests are dispatched in arrival order on the same tick, after `Drain` and before that tick's new requests; if it opened because of a failed write, they are discarded — source: ADR-011

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
- Server startup sequence (strict order): `LoadSceneAsync` → `NavMesh.AddNavMeshData` → `ZoneNavigationService.Initialize` → `ZoneBoundsValidator.Validate` → `zone.State = Active` → `TickLoop.Start` → `Gateway.RegisterZone` — source: ADR-009
- Zone teardown sequence (strict order): `TickLoop.Active = false` → wait for current tick → `ZoneNavigationService.Teardown()` → CR-ZI-12 steps → `Process.Exit(0)` — source: ADR-009
- `[SerializeField]` on **private fields only** — compile error on properties in Unity 6.3 — source: ADR-009
- `link.xml` must preserve `UnityEngine.SceneManagement` for IL2CPP headless server builds — source: ADR-009
- Client loading overlay: UI Toolkit `VisualElement` with `PickingMode.Position` — no URP custom rendering pass — source: ADR-009

**Event/messaging architecture (ADR-010):**
- All cross-system communication uses exactly one of three tiers: (1) direct method call on injected interface, (2) C# `event Action<T>` with `readonly struct` arg, (3) NGO RPCs/custom messages — source: ADR-010
- Service interface naming: `I[SystemName]Service` (grandfathered exceptions: `ICharacterPersistence`, `INavigationProvider`) — source: ADR-010
- Server-side systems wired at zone startup via `IZoneStartupService.StartZone()` — source: ADR-010
- All server-side event subscribers must implement `IDisposable`; `Dispose()` unsubscribes from all events — source: ADR-010
- All zone-scoped services must implement `IZoneScopedService : IDisposable` — source: ADR-010
- NGO message handlers enqueue to `Queue<T>` — never call game-logic methods directly — source: ADR-010

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

### Performance Guardrails

- **Persistence write**: warning ≥ 50ms; critical ≥ 100ms; hard timeout = 5s (`PERSISTENCE_WRITE_TIMEOUT_SECONDS`) — source: ADR-006
- **Persistence result handling**: the result of a persistence call is handled by `Drain` on the first tick after its task completes (normal case 50–100 ms after the request at 20 Hz); the tick never waits on I/O — source: ADR-011
- **`ITickCompletionQueue.Drain`**: O(tasks in flight) per tick — at most one per character in the zone; no allocation when nothing is in flight — source: ADR-011
- **Persistence watchdog**: `PERSISTENCE_WATCHDOG_TICKS = 200` (10 s) — the backstop above the 5 s `PERSISTENCE_WRITE_TIMEOUT_SECONDS`, which fires first — source: ADR-011, ADR-006
- **Held requests**: `MAX_HELD_REQUESTS_PER_CHARACTER = 16`; a request arriving at a full queue is dropped and logged — source: ADR-011
- **Server tick**: target < 30ms at 50 players + 150 mobs (F-NET-9 profiling gate — must be confirmed before Networking Core implementation is greenlit) — source: ADR-004
- **Per-client batch body**: ≤ 512 bytes (F-NET-6) — source: ADR-004
- **Zone server load time**: < 500ms at MVP asset scope — source: ADR-009
- **NGO channel mapping — identifiers are verification-pending**: `ReliableSequenced` / `Reliable` / `Unreliable` / `ReliableFragmentedSequenced` are the proposed identifiers per ADR-004 Decision 2, but the Unity 6.3 NGO/UTP API is post-LLM-cutoff. The guarantee mapping (R-OD/R-U/U-U) is binding; confirm exact enum member names against the engine reference before writing networking code — source: ADR-004
- **Assembly boundary — engine behaviour is verification-pending**: confirm each item on the first build or story that exercises it — (1) a client player contains no `IronGrind.ServerLogic`; (2) a Linux Dedicated Server IL2CPP build contains it and runs; (3) whether `UNITY_SERVER` is defined in the Editor under a server build profile; (4) whether asmdef platform lists offer a Dedicated Server entry (not relied on); (5) `IPostBuildPlayerScriptDLLs` and the output file locations for the client-binary scan; (6) `[MovedFrom]` for `[SerializeReference]` data when a type changes assembly; (7) the import setting that keeps Npgsql and Dapper out of a client player; (8) ~~whether a `MonoBehaviour` in an assembly constrained to `UNITY_EDITOR` can be attached to a scene object~~ — confirmed 2026-10-08 on Unity 6.3 (Leveling Story 014): it attaches and runs in Play mode — source: ADR-012

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

### Forbidden Approaches

- **Never call `Resume()` from a Dormant re-aggro transition** — Dormant has no preserved path; use `SetDestination` — source: ADR-003
- **Never introduce an `EventBus` class anywhere in `src/`** — once present it proliferates; a structural architecture violation — source: ADR-010
- **Never use class-typed event args** — forces heap allocation on every emit; use `readonly struct` — source: ADR-010
- **Never use `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith` or `async void` in tick-driven feature code** — start the `Task` and pass it to `ITickCompletionQueue.Track` — source: ADR-011
- **Never hold, reject or defer a client request inside a game system because a write is in flight, and never read `ICharacterMutationGate` from a game system** — the request dispatcher is the single enforcement point; the only readers of the gate are the dispatcher and the server-originated bag mutators named above — source: ADR-011
- **Never place a drop roll, enhancement odds or any other outcome formula in `IronGrind.Foundation`** — only a formula whose result the game shows the player, and which the client must evaluate to draw a screen, may be there — source: ADR-012

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
- **`[SerializeField]` on properties**: compile error in Unity 6.3 — use on private fields only; or use `[field: SerializeField]` for auto-property backing fields — source: ADR-009, `current-best-practices.md`
- **USS syntax errors block import** in Unity 6.3 (was a warning in 6.1/6.2) — all USS must be valid before commit; add USS linting to CI — source: ADR-005
- **SRP Batcher**: enable in URP Asset → Advanced → SRP Batcher for significant CPU win on mobile — source: `current-best-practices.md`
- **iOS build requirements**: IL2CPP + ARM64 + Metal only + ASTC textures — source: `current-best-practices.md`
