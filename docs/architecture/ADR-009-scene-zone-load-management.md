# ADR-009: Scene/Zone-Load Management

## Status
Accepted (2026-06-27) — Amendment 1 Accepted 2026-10-19

## Date
2026-06-27

Note 2026-10-16 (ADR-015 Accepted): Decision 3 gains step 2a — stop the cross-zone relay listener (`NpgsqlRelayListener.Stop()`) after the tick loop has stopped. The listener is also stopped from the server host `MonoBehaviour.OnDestroy`, because this teardown does not run when the Editor leaves Play mode (ADR-015 Decision 3). This adds to the open teardown-order item C2 in `architecture-traceability.md`.

Note 2026-10-19: **Amendment 1 (Accepted 2026-10-19)** at the end of this file replaces the step lists of Decision 2 (startup) and Decision 3 (teardown), adds Decision 5 (the composition root) and replaces `Process.Exit(0)` with `Application.Quit(int)`. Where the body below still shows `Process.Exit(0)` or the old step lists, the amendment wins. It closes C2 and P4 (`architecture-review-2026-10-19-adr-009-a1.md`).

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Core — Scene Management / URP Rendering |
| **Knowledge Risk** | HIGH — `Scene.handle` type change and URP render graph removal are post-cutoff breaking changes |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/breaking-changes.md`, `docs/engine-reference/unity/deprecated-apis.md` |
| **Post-Cutoff APIs Used** | `Scene` struct caching required (`Scene.handle` changed `int` → `SceneHandle` in 6.3); `AddRenderPasses` + `RecordRenderGraph` required for any URP custom pass; `SceneManager.LoadSceneAsync` (stable); `NavMesh.AddNavMeshData` / `RemoveNavMeshData` (stable) |
| **Verification Required** | (1) Confirm `LoadSceneAsync` with `LoadSceneMode.Single` completes all `Awake()`/`Start()` before the coroutine continuation in a headless `UNITY_SERVER` build. (2) Confirm `NavMesh.AddNavMeshData()` called immediately after load completion is safe in the headless build. (3) Verify `Scene` struct equality works for loaded-scene tracking in Unity 6.3. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-002 (NavMesh Service Execution Contract — Accepted ✓): defines `RemoveNavMeshData()` teardown sequence; this ADR extends it to the full scene lifecycle. ADR-004 (NGO — Accepted ✓): defines server tick loop start. ADR-007 (Hosting Backend — Accepted ✓): one Unity process per zone instance — the primary constraint on server scene loading strategy. |
| **Enables** | Zone Instancing implementation sprint (Loading phase). Navigation/Pathfinding implementation sprint (`ZoneNavigationService.Initialize()` call site). Client zone-transition sequence (post-MVP). |
| **Blocks** | Zone Instancing stories covering zone Loading phase (T-1 → T-2 transition). `ZoneDefinition` asset initialization. Client zone-entry loading screen story. |
| **Ordering Note** | ADR-002 Decision 3 (teardown ↔ tick sync) is the upstream contract; this ADR governs the scene-level envelope around it. |

## Context

### Problem Statement

`zone-instancing.md` defines the zone lifecycle (Loading → Active → Draining → Closed) and references `ZoneNavigationService.Initialize(ZoneID, NavMeshData)` during the Loading phase and `Teardown()` during the Closed state. ADR-002 establishes the teardown ↔ tick synchronization contract. Neither the GDD nor ADR-002 specifies:

1. Which Unity API loads the zone scene and when in the Loading phase it must complete before NavMesh init and tick loop start.
2. How `NavMesh.AddNavMeshData()` is sequenced relative to scene load completion and `ZoneNavigationService.Initialize()`.
3. What the full server process startup sequence is — from process launch to gateway registration.
4. What the client zone-transition sequence is (loading overlay, scene swap, NGO reconnect, snapshot dual-gate) for the post-MVP multi-zone case.
5. Which `Scene` APIs are safe in Unity 6.3 given the `Scene.handle` type change from `int` to `SceneHandle`.

Without this, two implementers reading zone-instancing.md and ADR-002 can independently produce divergent server startup sequences — e.g., calling `ZoneNavigationService.Initialize()` before the scene's `Awake()` methods have run, or caching `Scene.handle` as an `int` that throws `MissingFieldException` under precompiled assemblies in Unity 6.3.

### Constraints

- One Unity zone process per active zone instance (ADR-007, `zone_server_process_model` registry stance) — the server process loads the zone scene once and exits on teardown. No hot-swap.
- `NavMesh.AddNavMeshData()` is process-global (ADR-002 Decision 4) — the scene must be fully activated before NavMesh data is added.
- Server build: Unity 6.3 IL2CPP headless (`UNITY_SERVER`), Linux x64, Ubuntu 22.04 LTS (ADR-007).
- Client build: Unity 6.3 IL2CPP, iOS primary. URP Compatibility Mode removed in 6.3 — `AddRenderPasses` + `RecordRenderGraph` are the only supported path for custom rendering passes.
- `Scene.handle` changed from `int` to `SceneHandle` in Unity 6.3 — caching as `int` throws `MissingFieldException` at runtime in precompiled assemblies.
- Zone bounds must be validated before the tick loop starts (CR-ZI-10); bounds are authored in the `ZoneDefinition` asset loaded with the zone scene.
- `[SerializeField]` is a **compile error** on properties in Unity 6.3 — fields only.

### Requirements

- Zone scene must be fully loaded (`Awake()`/`Start()` complete) before `NavMesh.AddNavMeshData()` is called.
- `ZoneNavigationService.Initialize()` must be called after `AddNavMeshData()` returns.
- The tick loop must not start until `Initialize()` completes and bounds are validated.
- Gateway registration must not occur until the tick loop is active.
- Zone teardown must call `ZoneNavigationService.Teardown()` after the tick loop is halted (ADR-002 Decision 3) before scene unload / process exit.
- Client loading overlay must not rely on a URP custom rendering pass using `SetupRenderPasses`.
- Scene structs must be cached as `Scene` (not `int` handles).

---

## Decision

### Decision 1 — Server Zone Scene Loading (Single Mode, Async)

The zone server uses `SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single)` immediately after process initialization. `Single` mode is correct because:

- No persistent bootstrap scene must survive alongside the zone scene. The process exists solely to run one zone instance.
- `Single` mode automatically unloads any prior scene, eliminating explicit bootstrap lifecycle management.
- The process never hot-swaps zone scenes (ADR-007 closes this).

**Scene name resolution:** Each `ZoneTemplateID` maps to a Unity scene name via a `ZoneSceneRegistry` ScriptableObject. The gateway passes `ZoneTemplateID` to the zone process at startup (command-line argument). The process resolves the scene name and loads it.

**Scene caching:** Cache as a `Scene` struct — never as `Scene.handle` (now `SceneHandle` in Unity 6.3, not `int`).

```csharp
private Scene _zoneScene;

private IEnumerator LoadZoneScene(string sceneName) {
    var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
    yield return op;                              // guarantees Awake/Start complete
    _zoneScene = SceneManager.GetActiveScene();   // Scene struct, not int handle
    // → proceed to Decision 2
}
```

---

### Decision 2 — NavMesh Initialization Sequence (Server)

After `LoadSceneAsync` completes (the `yield return op` continuation — guaranteeing all `Awake()` and `Start()` have run):

```
1. _navMeshDataInstance = NavMesh.AddNavMeshData(zoneDefinition.BakedNavMesh)
2. ZoneNavigationService.Initialize(zoneId, _navMeshDataInstance)
3. ZoneBoundsValidator.Validate(zoneDefinition.WalkableBoundsXZ)  ← CR-ZI-10
4. if (boundsInvalid) → zone.State = Closed (T-3); log ZoneBoundsExceedEncoding; return
5. zone.State = Active  (T-2 transition)
6. _tickLoop.Start()
7. Gateway.RegisterZone(zoneId, udpPort)          ← zone now available for routing
```

**NavMesh data source:** The `ZoneDefinition` ScriptableObject holds a reference to the pre-baked `NavMeshData` asset. `NavMesh.AddNavMeshData()` returns a `NavMeshDataInstance` that must be cached as a field and used with `RemoveNavMeshData()` at teardown — it cannot be re-acquired.

**`[SerializeField]` field-only rule (Unity 6.3 compile error):**

```csharp
// CORRECT — field only
[SerializeField] private NavMeshData _bakedNavMesh;
public NavMeshData BakedNavMesh => _bakedNavMesh;

// COMPILE ERROR in Unity 6.3 — [SerializeField] on property is forbidden
[SerializeField] public NavMeshData BakedNavMesh { get; private set; }
```

**ADR-002 handoff:** This sequence is the load-time setup that ADR-002 Decision 3 assumes is already complete when the tick loop starts. ADR-002 governs teardown ↔ tick sync; this ADR governs the load-time sequence that precedes it.

---

### Decision 3 — Server Zone Teardown (Extending ADR-002 Decision 3)

On zone `Closed` state entry (T-5 or T-6 triggers):

```
1. _tickLoop.Active = false                       // ADR-002 D3: block new tick cycles
2. WaitForCurrentTickToComplete()                 // tick loop is synchronous — check flag
2a. NpgsqlRelayListener.Stop()                    // ADR-015 (2026-10-16): join ≤ RELAY_LISTENER_STOP_TIMEOUT_MS
3. ZoneNavigationService.Teardown()               // NavMesh.RemoveNavMeshData(_navMeshDataInstance)
4. [CR-ZI-12 teardown steps — save sessions, notify party system, free slots]
5. Process.Exit(0)
```

Because one process = one zone (ADR-007), `Process.Exit(0)` after CR-ZI-12 is the safest teardown. It eliminates any residual Unity state that could interfere with a subsequent zone instance. `SceneManager.UnloadSceneAsync` is deferred to post-MVP if zone hot-swap ever becomes a requirement.

The `NavMeshDataInstance` field must be the same instance returned from step 1 of Decision 2 — not re-constructed.

---

### Decision 4 — Client Zone Transition Sequence (Post-MVP Architecture)

At MVP there is one zone template (CR-ZI-16 defers zone-to-zone transfers). This decision establishes the architectural contract for when zone 2 is added (Vertical Slice scope), so client programmers do not build the wrong loading architecture at MVP.

```
1. ZoneSessionEnded received (CR-ZI teardown from server)
2. Show loading overlay (UI Toolkit VisualElement panel — no URP custom pass)
3. NetworkManager.Singleton.Shutdown()             // disconnect from old zone server
4. await NGO shutdown completion
5. SceneManager.LoadSceneAsync(newZoneName, Single)  // unloads old zone, loads new
6. await async load
7. NetworkManager.Singleton.StartClient(newIP, newPort)  // from gateway redirect message
8. Send SessionHandshake (networking-session.md CR-NET-6)
9. Await ZoneStateSnapshot + SessionReady (dual gate per zone-instancing.md CR-ZI-8)
10. Hide loading overlay
```

**Loading overlay:** Full-screen `VisualElement` on the existing UI Toolkit `UIDocument`, `pickingMode = PickingMode.Position` (blocks all touches during load). No URP custom rendering pass required — this eliminates `SetupRenderPasses` migration risk entirely on this code path.

**If a custom URP pass is ever added to the loading screen:** it MUST use `AddRenderPasses` + `RecordRenderGraph`. `SetupRenderPasses` is removed in Unity 6.3. This is enforced as a forbidden pattern (see registry entry).

---

### Architecture Diagram

```
SERVER ZONE PROCESS LIFECYCLE
─────────────────────────────
Process start
    │
    ▼
LoadSceneAsync(zoneName, Single)        ← async; spreads across Unity frames
    │ yield (Awake/Start complete)
    ▼
NavMesh.AddNavMeshData(                 ← cache NavMeshDataInstance as field
    zoneDefinition.BakedNavMesh)
    │
    ▼
ZoneNavigationService.Initialize(       ← ADR-002 D1/D2 contract begins
    zoneId, navMeshDataInstance)
    │
    ▼
ZoneBoundsValidator.Validate()          ← CR-ZI-10; fail → T-3, process exits
    │ pass
    ▼
zone.State = Active (T-2)
TickLoop.Start()
Gateway.RegisterZone(zoneId, port)      ← zone now visible to routing table
    │
    │  ... runtime gameplay (ADR-002 D1 tick phases) ...
    │
T-5 / T-6 trigger
    │
    ▼
TickLoop.Active = false                 ← ADR-002 D3: halt before nav teardown
WaitForCurrentTickToComplete()
    │
    ▼
ZoneNavigationService.Teardown()        ← NavMesh.RemoveNavMeshData()
CR-ZI-12 teardown steps
    │
    ▼
Process.Exit(0)


CLIENT ZONE TRANSITION (post-MVP)
──────────────────────────────────
ZoneSessionEnded received
    │
    ▼
Show FullScreenLoadingOverlay           ← UI Toolkit VisualElement; no URP pass
    │
    ▼
NetworkManager.Shutdown()
    │ await
    ▼
LoadSceneAsync(newZoneName, Single)
    │ yield
    ▼
NetworkManager.StartClient(ip, port)
SessionHandshake sent
    │ await dual gate (ZoneStateSnapshot + SessionReady)
    ▼
Hide FullScreenLoadingOverlay
```

### Key Interfaces

```csharp
// Zone process startup coroutine
public interface IZoneStartupService {
    IEnumerator StartZone(ZoneTemplateID template, ZoneID zoneId);
    // Sequence: LoadSceneAsync → AddNavMeshData → Initialize →
    //           ValidateBounds → SetActive → StartTickLoop → RegisterGateway
}

// ZoneDefinition ScriptableObject — [SerializeField] on fields only (Unity 6.3 rule)
public class ZoneDefinition : ScriptableObject {
    [SerializeField] private NavMeshData _bakedNavMesh;
    [SerializeField] private Rect _walkableBoundsXZ;        // ±250m limit, CR-ZI-10
    [SerializeField] private Vector3 _townRespawnPoint;     // GetTownRespawnPoint cache source

    public NavMeshData BakedNavMesh     => _bakedNavMesh;
    public Rect        WalkableBoundsXZ => _walkableBoundsXZ;
    public Vector3     TownRespawnPoint => _townRespawnPoint;
}

// NavMesh handle — cached on load, used for teardown (must not be re-acquired)
private NavMeshDataInstance _navMeshDataInstance;

// Scene tracking — Scene struct, not int handle (Unity 6.3 Scene.handle type change)
private Scene _zoneScene;

// Client loading overlay — UI Toolkit, no URP pass
public class FullScreenLoadingOverlay : MonoBehaviour {
    public void Show();   // display = DisplayStyle.Flex; pickingMode = Position
    public void Hide();   // display = DisplayStyle.None
}
```

---

## Alternatives Considered

### Alternative 1: Additive Scene Loading on Server

- **Description**: Keep a minimal bootstrap scene loaded at all times; load the zone scene additively alongside it.
- **Pros**: Would theoretically allow zone hot-swap within the same process without a restart.
- **Cons**: ADR-007 closes hot-swap (one process = one zone). ADR-002 Decision 4 closes multi-zone per process via NavMesh global scope. Additive loading adds bootstrap lifecycle complexity with zero benefit at this topology.
- **Rejection Reason**: Both upstream constraints eliminate the only use case additive loading would serve.

### Alternative 2: External NavMeshData Asset (Loaded Separately)

- **Description**: `NavMeshData` asset stored as a standalone asset, loaded via `Resources.Load` or Addressables — separate from the zone scene and `ZoneDefinition`.
- **Pros**: Could theoretically pre-load the NavMesh before scene load for earlier validation.
- **Cons**: Introduces a second async load that must be explicitly sequenced with the scene load. `Resources.Load` is synchronous and blocks the main thread. `ZoneDefinition` as a ScriptableObject already co-locates all zone data — splitting it adds a cross-reference. Addressables are deferred to post-MVP.
- **Rejection Reason**: Added complexity and load-ordering risk with no benefit at MVP scope.

### Alternative 3: Dedicated Loading Scene on Client

- **Description**: Client loads a separate "LoadingScreen" Unity scene during zone transitions.
- **Pros**: Allows elaborate loading-screen visual content.
- **Cons**: Requires managing three scenes in memory simultaneously (outgoing, loading, incoming). Would require a URP camera and potentially a `SetupRenderPasses`-based custom pass — triggering the Unity 6.3 render graph migration requirement. Mobile memory ceiling (1.5GB) constrains having three active scenes. UI Toolkit VisualElement overlay achieves the same visual result without a scene swap.
- **Rejection Reason**: Higher memory footprint, URP migration risk, and no functional improvement over a UI Toolkit overlay.

---

## Consequences

### Positive
- Server startup sequence is fully deterministic: scene load → NavMesh add → nav init → bounds check → tick loop → gateway register. Every step is independently testable.
- `Process.Exit(0)` teardown eliminates risk of residual Unity state between zone instances. No `UnloadSceneAsync` complexity at MVP.
- `[SerializeField]` field-only constraint on `ZoneDefinition` is compile-safe under Unity 6.3.
- UI Toolkit loading overlay avoids all URP render graph migration risk on the loading code path.
- `Scene` struct caching is correct under Unity 6.3's `Scene.handle` type change.

### Negative
- `Process.Exit(0)` teardown means zone processes cannot be reused for different zone templates. Each zone instance requires a fresh process start (already the case per ADR-007).
- `ZoneDefinition` with embedded `NavMeshData` requires the zone scene to be open in the Unity editor to bake the NavMesh. Post-MVP multi-zone projects must bake each zone template scene separately.

### Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| `LoadSceneAsync` continuation fires before `Awake()`/`Start()` complete in headless build | LOW | HIGH — `ZoneDefinition` not ready when `AddNavMeshData()` is called | Use `yield return op` coroutine (not `await`) — coroutine continuation guarantees engine frame processing is complete. Verify empirically in headless `UNITY_SERVER` build before sprint. |
| `Scene.handle` cached as `int` in a precompiled plugin | MEDIUM | HIGH — `MissingFieldException` at runtime | All project code uses `Scene` struct. Audit precompiled plugins before first Unity 6.3 build. |
| `NavMesh.AddNavMeshData()` called while prior zone NavMesh still registered (if hot-swap is ever added) | LOW (MVP: process exit eliminates risk) | HIGH — cross-zone path queries return incorrect results | Decision 3 mandates process exit, eliminating this risk at MVP. Any future hot-swap ADR must sequence `RemoveNavMeshData` before `AddNavMeshData`. |
| URP `SetupRenderPasses` used in a future loading-screen custom pass | MEDIUM | MEDIUM — compile warning in 6.2, potential issue in future 6.x | Decision 4 explicitly forbids `SetupRenderPasses` on the loading code path. Registry forbidden pattern entry prevents future drift. |

---

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|---|---|---|
| `zone-instancing.md` | T-1 → T-2 (Loading phase): `ZoneDefinition` loaded, bounds validated, slot arrays zeroed, respawn point cached, mob spawner primed | Decision 2 sequences scene load completion (all `Awake()`/`Start()` run) before any `ZoneDefinition` access, ensuring all Loading-phase artifacts are available before T-2 fires |
| `zone-instancing.md` | T-2 gate: zone must not become Active until all Loading-phase initialization is complete | Decision 2 places `zone.State = Active` after scene load → NavMesh add → nav init → bounds validation, strictly in that order |
| `zone-instancing.md` | CR-ZI-10: walkable bounds validated at Loading-phase init; T-3 abort if bounds exceed ±250m per axis | `ZoneBoundsValidator.Validate()` is step 3 of Decision 2, before T-2 transition or tick loop start |
| `zone-instancing.md` | CR-ZI-12: Zone teardown sequence | Decision 3 defines the scene-level envelope: tick halt (ADR-002 D3) → nav teardown → CR-ZI-12 steps → process exit |
| `navigation-pathfinding.md` | CR-NAV-2: `ZoneNavigationService.Initialize(ZoneID, NavMeshData)` during Loading phase | Decision 2 step 2: Initialize called immediately after `NavMesh.AddNavMeshData()` returns |
| `navigation-pathfinding.md` | CR-NAV-3: `ZoneNavigationService.Teardown()` called on zone Closed before per-mob despawn | Decision 3: Teardown called after tick halt, before CR-ZI-12 teardown steps continue |
| `movement-system.md` | Spawn position on zone entry: `GetTownRespawnPoint(ZoneID)` must return a valid Vector3 before client rendering begins | Decision 4 step 9: loading overlay persists until ZoneStateSnapshot dual gate opens; snapshot includes `townRespawnPoint` data (CR-ZI-8 step 6) |

## Performance Implications

- **CPU**: `LoadSceneAsync` spreads asset loading across frames. At `Application.targetFrameRate = 20` (ADR-002 Decision 5a), each frame is 50ms. Expected server zone load time: < 500ms for MVP asset scope.
- **Memory**: `LoadSceneMode.Single` frees the bootstrap scene's memory before the zone scene allocates — no double-scene peak. `ZoneDefinition.BakedNavMesh` adds ~100KB–2MB to zone process heap depending on zone geometry. Well within the 1.5GB mobile ceiling.
- **Load Time**: Client zone transition: `LoadSceneAsync` (~200–500ms on A15 Bionic at MVP asset scope) + NGO reconnect (~100ms RTT) + ZoneStateSnapshot delivery (17–19 fragments, F-ZI-1) = ~500–1500ms total. UI Toolkit overlay covers the gap without a visible frame drop.
- **Network**: Scene load is client-local; no network impact on the zone server. Server tick loop continues undisturbed while the client loads the new scene.

## Migration Plan

Greenfield — no existing scene management code. Implementation order:

1. Create `ZoneDefinition` ScriptableObject with `[SerializeField] private` fields (Unity 6.3 compile rule); bake NavMesh per zone template into the ScriptableObject.
2. Implement `IZoneStartupService.StartZone()` coroutine (Decision 2 sequence) wired into the zone server process's startup `MonoBehaviour`.
3. Implement `ZoneBoundsValidator.Validate()` using `ZoneDefinition.WalkableBoundsXZ`; fail path triggers T-3 via zone lifecycle handler.
4. Implement `FullScreenLoadingOverlay : MonoBehaviour` backed by a UI Toolkit `UIDocument` (no URP custom pass; `PickingMode.Position` to block touches).
5. Implement client zone-transition coroutine (Decision 4 sequence); wire into the zone-exit message handler.
6. Audit `link.xml` — add `UnityEngine.SceneManagement` preservation if not already present (IL2CPP stripping may affect `SceneManager` in headless server builds; add alongside the `UnityEngine.AI` entry from ADR-002 Decision 5b).

## Validation Criteria

- [ ] Server process loads zone scene; `NavMeshDataInstance` is non-null after `NavMesh.AddNavMeshData()`; zone reaches `Active` state within `ZONE_LOAD_TIMEOUT_SECONDS` (30s default)
- [ ] Zone teardown: tick loop quiesces; `NavMesh.RemoveNavMeshData()` is called before process exits; no `InvalidOperationException` from the Unity NavMesh subsystem
- [ ] `ZoneDefinition` ScriptableObject compiles without warnings or errors in Unity 6.3 — all `[SerializeField]` attributes are on private fields, not properties
- [ ] Client loading overlay appears before `NetworkManager.Shutdown()` and hides only after the dual gate opens (`INetworkTestObserver.OnZoneGateOpened(clientId)` from zone-instancing.md OQ-ZI-5)
- [ ] `grep -r "SetupRenderPasses"` in client loading-screen code returns zero matches
- [ ] `Scene _zoneScene` cached as struct (not int handle) — no `scene.handle` field access in any zone management code (code review gate)

## Related Decisions

- ADR-002: NavMesh Service Execution Contract — teardown ↔ tick sync; this ADR provides the scene-level envelope around it
- ADR-004: Networking Library (NGO) — tick loop start gated behind scene + NavMesh init per Decision 2
- ADR-007: Hosting Backend — one process per zone constraint drives `LoadSceneMode.Single` and `Process.Exit(0)` as the correct choices
- `design/gdd/zone-instancing.md` — zone lifecycle states (T-1/T-2/T-3/T-5/T-6), CR-ZI-10, CR-ZI-12
- `design/gdd/navigation-pathfinding.md` — `ZoneNavigationService.Initialize()` / `Teardown()` call sites
- `design/gdd/movement-system.md` — spawn position on zone entry via `GetTownRespawnPoint()`

## Amendment 1 — Zone Process Startup, Teardown and the Composition Root (2026-10-19, Accepted 2026-10-19)

Closes `architecture-traceability.md` C2 (teardown lacks a `Dispose()` pass and the shutdown drain) and P4 (startup lacks NGO start, static-data-ready and wiring). It replaces the step lists of Decision 2 and Decision 3 and adds Decision 5. Decision 1 (scene loading), the `ZoneDefinition` rules and Decision 4 (client) are unchanged. User decisions 2026-10-19: amend this ADR rather than write a new one; thin host + testable composition class; `Application.Quit(int)` as the exit call; quit requests intercepted through `Application.wantsToQuit`; connection string from server config with the password from the environment; a failed startup step undoes what was built and exits nonzero.

### Amendment 1 — Engine Compatibility (additions)

| Field | Value |
|-------|-------|
| **Post-Cutoff / unverified APIs** | `Application.wantsToQuit` and `Application.Quit(int)` on a Linux Dedicated Server IL2CPP player; `Object.DontDestroyOnLoad` across `LoadSceneAsync(…, Single)`; `NetworkManager.NetworkTickSystem.Tick` (`event Action`, can fire more than once per frame — engine reference networking.md); `NetworkManager.StartServer()` return value; `OnDestroy` order when the Editor leaves Play mode |
| **Verification Required (A1)** | All on the Linux Dedicated Server IL2CPP build in batch mode (`-batchmode -nographics`), started under systemd or stopped with `kill -TERM`, except A1-5. (A1-1) SIGTERM raises `Application.wantsToQuit` on the main thread; returning `false` keeps the process alive until the teardown calls `Application.Quit(code)`, and the exit status equals `code` (ADR-011 Verification 4 made concrete). **This is a real unknown, not a formality:** if SIGTERM does not reach `wantsToQuit`, the amendment needs another hook before shipping, in this order: `PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; … })` if Unity's .NET profile has it (a .NET 6 API that may be missing; A1-1 checks), otherwise a small native plugin that installs the signal handler. Either handler runs off the main thread and only sets a volatile flag that the host's `Update` turns into `RequestTeardown(Clean)`. `AppDomain.ProcessExit` is too late for the drain. Record the finding either way. (A1-2) A second SIGTERM during teardown does not cut the drain short. (A1-3) The host (a root GameObject) and the `NetworkManager` object, both `DontDestroyOnLoad`, survive `LoadSceneAsync(…, Single)`. (A1-4) `NetworkConfig.TickRate = 20` set before `StartServer()` makes `NetworkTickSystem.Tick` fire 20 times per second of server time with `Application.targetFrameRate = 20` and `vSyncCount = 0` in batch mode; ticks per frame are 0, 1 or 2. (A1-5) Editor: leaving Play mode is not blocked by the `wantsToQuit` handler, runs the host's `OnDestroy`, which runs the teardown without the exit step, also when `NetworkManager.OnDestroy` ran first; the listener thread stops within `RELAY_LISTENER_STOP_TIMEOUT_MS` (ADR-015 Verification 6). (A1-6) With NGO scene management disabled (`NetworkConfig.EnableSceneManagement = false`), the plain `SceneManager.LoadSceneAsync` of Decision 1 runs with NGO started afterwards and no NGO scene warning. (A1-7) Npgsql 8.0.9 and Dapper 2.1.66 run on the IL2CPP Linux build with `Assets/link.xml` (one real read and one real write; Character Persistence EPIC Known Gap 2, TD-073). **Where each check runs (review 2026-10-19):** this machine has only the Windows Dedicated Server (Mono) module for 6000.3.10f1 and Windows has no SIGTERM, so A1-3, A1-5 and A1-6 run locally (A1-3 and A1-6 on the Windows Dedicated Server build, A1-5 in the Editor), A1-4 runs there as a first measurement, and A1-1, A1-2, A1-7 and the final A1-4 are carried by the epic's Linux IL2CPP headless-build gate, as Networking Core Story 041 did for its VR-2. |

> **Note 2026-10-26 (results of the Networking Core Story 045 runs, 2026-10-22; evidence `production/qa/evidence/networking-core-045-zone-process-verification.md`).** Windows Dedicated Server (Mono, development) build and Editor, Unity 6000.3.10f1, portable PostgreSQL 16.15:
> - **A1-3 PASS.** Host and `NetworkManager` instance ids logged before and after `LoadSceneAsync(…, Single)` were equal in both player runs and in 3 Editor cycles.
> - **A1-6 PASS.** S6 completed, `StartServer` succeeded afterwards, and the log had no `[Netcode]` warning or scene-management message.
> - **A1-4 first measurement PASS, with a note (user decision).** The first second after the first tick is excluded as warm-up: NGO catches up once after `StartServer` (22–23 ticks, one 3-tick frame). Over 73 measured seconds: 20.05 ticks per second, histogram `0=11 1=1442 2=13 3=0`. One second (second 50) counted 22 ticks after a late frame; accepted.
> - **A1-5 PASS.** Over 3 Editor cycles, leaving Play mode was not blocked, `OnDestroy` ran teardown 1–7 without step 8, and no `NullReferenceException` appeared, both when `NetworkManager.OnDestroy` ran first (cycle 1) and when the host ran first.
> - **Exit codes.** A Clean teardown exited 0 with steps 1–8 in order; an unreachable database failed at S4 and exited 1.
> - **Not run:** A1-1, A1-2, A1-7 and the final A1-4 stay with the Linux IL2CPP headless-build gate.
>
> The Decision text is unchanged.

### Decision 2 (replaced) — Zone Process Startup

One `ZoneServerHost` `MonoBehaviour` (in `IronGrind.ServerLogic`, in a scene only the Dedicated Server build includes — ADR-012 forbidden pattern on server MonoBehaviours in client content) owns one `ZoneServerComposition`, a plain C# class that builds, starts and tears down everything. The host holds no game logic: it forwards Unity callbacks (`Start` coroutine, `Update`, `OnDestroy`, `wantsToQuit`) to the composition. Everything runs on the main thread, which is the tick thread (ADR-011).

```
S0. Read the command line (-zoneTemplate, -zoneId, -port) and the server config. Missing or invalid → fail (S-fail).
S1. Application.wantsToQuit += OnWantsToQuit             // quit requests from here on go to teardown (Decision 3)
    DontDestroyOnLoad(host.gameObject)                    // the host is a root GameObject; survives the Single-mode load in S6
    NetworkManager object: a root GameObject outside the zone scene, NetworkManager.DontDestroy = true,
    NetworkConfig.EnableSceneManagement = false           // Decision 1 loads scenes itself; clients load their own (Decision 4)
S2. Static data ready: load and validate ItemDatabase, ClassRegistry, LootTableRegistry,
    DamageCalculationConfig, EnhancementConfig (and any later static config). Any failure → S-fail.
S3. random = RandomProviderFactory.CreateSeededFromEntropy(out seed)     // ADR-013 Decision 4: the one generator
S4. Database (ADR-006 Decision 5 as amended by Amendment 3):
      connectionString = build from config + IRONGRIND_PG_PASSWORD (NpgsqlConnectionStringBuilder; never logged)
      dataSource = NpgsqlDataSource.Create(connectionString)
      warm-up: Task.Run(() => dataSource.CreateCommand("SELECT 1").ExecuteNonQueryAsync(token))
               .Wait(DB_WARMUP_TIMEOUT_MS)            // default 5 000; a timeout or fault → S-fail
S5. Construct, no I/O, in this order (the Dispose pass runs it backwards):
      TickCompletionQueue; ServerTickLoop
      NpgsqlCharacterRecordStore(dataSource); CharacterPersistenceService(store, stats, leveling)
      game systems, each given `random` and the static data from S2
      relay: allowlist, origin, inbox, receiver, NpgsqlRelayPublisher(dataSource, queue, origin)
      dispatcher (ADR-014); ClientMessageOutbox; ServerWideBroadcaster(outbox, publisher, allowlist); ZoneClientRelayHandler
      ZoneTickPipeline(queue, inbox, relayHandler, dispatcher, simulation, outbound, tickLoop)
      tickLoop.RegisterTickDriven(pipeline.Tick)       // the only RegisterTickDriven call (registry forbidden pattern)
S6. Scene and NavMesh: Decision 1, then old Decision 2 steps 1–4 (AddNavMeshData, ZoneNavigationService.Initialize,
    ZoneBoundsValidator.Validate; invalid bounds → zone T-3 and S-fail)
S7. listener = new NpgsqlRelayListener(listenerConnectionString, receiver); listener.Start()
      // listenerConnectionString = connectionString + Pooling=false; Multiplexing=false; Tcp Keepalive=true (ADR-015 Decision 3)
S8. NGO: NetworkConfig.TickRate = ServerTickLoop.TICK_RATE_HZ; Application.targetFrameRate = TICK_RATE_HZ
    (TR-net-002, ADR-002); QualitySettings.vSyncCount = 0;
    if (!NetworkManager.StartServer()) → S-fail
    NgoInboundTransportShell.Start(); outbound sender started
S9. zone.State = Active (T-2); NetworkManager.NetworkTickSystem.Tick += OnNetworkTick
      // OnNetworkTick → tickLoop.AdvanceTick(measured duration); one call per NGO tick event (ADR-004 Decision 5)
S10. Gateway.RegisterZone(zoneId, port)                 // last: the zone is routable only when everything runs

S-fail: log the failing step (never the connection string), run the teardown from the first step that has
something to undo (Decision 3, skipping the drain if no tick ever ran), exit with ZoneExitCode.StartupFailed.
No retry inside the process; supervision decides (ADR-007, C1).
A quit request during startup (S1 onward) makes startup stop at the next step boundary and tear down with ZoneExitCode.Clean.
```

**Coroutine structure.** C# forbids `yield return` inside a `try` that has a `catch`, and Unity logs and abandons a coroutine that throws, which would leave a hung process. Each step S0–S10 is therefore a non-yielding method that the coroutine calls inside its own `try`/`catch`; the coroutine yields only between steps (S6's scene load is the only real wait). A caught exception goes to S-fail.

**Blocking waits.** The warm-up in S4 is a bounded blocking wait on a task before any tick exists; ADR-011 Decision 2's "only blocking wait on a task in the server" becomes "the only two: the startup warm-up, before the first tick, and the shutdown drain" (dated note on ADR-011). `Task.Run` keeps the wait off any captured context.

**Systems not built yet.** `ZoneNavigationService`, `ZoneBoundsValidator`, the zone `State` owner (Zone Instancing) and the gateway client do not exist in code. The composition takes them through interfaces (`IZoneNavigation`, `IZoneBoundsValidator`, `IGatewayRegistration`); until their epics ship, the composition-root stories pass stand-ins that log one line each and do nothing. A stand-in is never a silent success in a release build: each logs `"[ZoneServer] <step> not implemented"` once.

### Decision 3 (replaced) — Zone Process Teardown

A trigger (zone `Closed` from game code, a quit request, a fatal error) only calls `RequestTeardown(code)`, which records the first code and returns. The host's `Update` then starts the teardown coroutine once — so teardown never runs inside a tick or inside the `wantsToQuit` handler. Because the tick runs synchronously on the main thread, the old step 2 (`WaitForCurrentTickToComplete`) is satisfied by construction. Every step null-guards the objects it touches (in the Editor, `NetworkManager.OnDestroy` can run before the host's).

```
1.  NetworkTickSystem.Tick -= OnNetworkTick            // no new tick (ADR-002 Decision 3: nav calls halted)
2a. listener.Stop()                                    // ADR-015: join ≤ RELAY_LISTENER_STOP_TIMEOUT_MS
3.  ZoneNavigationService.Teardown()                   // CR-NAV-3: first Zone Instancing action on Closed, before mob despawn
4.  CR-ZI-12 steps 1–8 (save sessions — tracked by the queue —, ghost handler, ZoneSessionEnded, party, free slots, retire ZoneID)
4b. outbound.Flush() once                              // the tick is stopped; sends CR-ZI-12 step 4's ZoneSessionEnded
5.  queue.DrainOnShutdown(PERSISTENCE_WATCHDOG duration)   // ADR-011 Decision 2: bounded wait, then a last Drain
6.  NgoInboundTransportShell.Stop(); NetworkManager.Shutdown();
    yield while NetworkManager != null && NetworkManager.ShutdownInProgress,
          at most NGO_SHUTDOWN_WAIT_FRAMES frames      // NGO finishes its shutdown at the end of a frame; a destroyed or
                                                       // inactive NetworkManager never gets that frame: at the cap, log a warning and go on
7.  Dispose pass: everything S5 built that is IDisposable, in reverse order; dataSource.Dispose() last
8.  quitAllowed = true; Application.Quit((int)code)    // replaces Process.Exit(0), which is not a .NET API
```

Steps 3 and 4 are the old Decision 3 order (nav teardown, then CR-ZI-12); what is new is 2a, 4b, the drain after the writes CR-ZI-12 starts, the NGO shutdown, the Dispose pass and the exit call. The drain relies on ADR-011's rule that persistence code uses `ConfigureAwait(false)` on every await: a continuation posted to the main thread would wait out the drain's timeout, because the main thread is blocked in it.

`ZoneExitCode`: `Clean = 0` (zone closed, or a quit request such as SIGTERM), `StartupFailed = 1`, `Fatal = 2` (an unhandled failure the host catches at runtime). Tasks still incomplete after the drain are logged and alerted (ADR-011) and do not change the code.

**Quit requests.** `OnWantsToQuit` returns `true` in the Editor (so leaving Play mode is never blocked) and once step 8 has set `quitAllowed`; otherwise it calls `RequestTeardown(Clean)` and returns `false`. This is how SIGTERM from systemd reaches the drain (ADR-011 Verification 4, A1-1). systemd's `TimeoutStopSec` must exceed the worst teardown: listener stop (2 s) + drain (10 s) + the rest — noted for ADR-007's C1 amendment.

Note 2026-10-22 (zone lifecycle reconciliation, user decision; `zone-instancing.md` CR-ZI-2 now owns the zone lifecycle, `networking-session.md` ST-NET-2 only drives it): **a quit request no longer calls `RequestTeardown(Clean)` directly.** In a player, outside the two `true` cases above, `OnWantsToQuit` starts the zone's T-4 drain (Active → Draining: `ZoneSessionEnded` with `ZONE_DRAIN_GRACE_SECONDS`, no new handshakes) and returns `false`. `RequestTeardown(Clean)` is then called by game code on the zone reaching `Closed` — T-5 (last session released, or the grace expired) or T-7 (idle close) — which is the "zone `Closed` from game code" trigger already listed in Decision 3. A second quit request while Draining, or a quit request while the zone is still Loading, is T-6 and calls `RequestTeardown(Clean)` at once; a fatal error still calls `RequestTeardown(Fatal)` at once. Steps 1–8 are unchanged, and A1-2 (a second SIGTERM during teardown does not cut the persistence drain short) is unchanged — T-6 only skips the grace window, never the drain. Consequence for the stop timeout: `TimeoutStopSec` must now exceed `ZONE_DRAIN_GRACE_SECONDS` (60 s default) + the worst teardown above, so the systemd default of 90 s leaves about 18 s of margin; the unit file should set it explicitly (for ADR-007's C1 amendment). Implemented by a Zone Instancing story (EPIC known gap); until then the code still does the direct `RequestTeardown(Clean)`.

**Editor and `OnDestroy`.** The host's `OnDestroy` runs steps 1–7 synchronously (no yield in step 6, no step 8: the process must not exit there), unless the teardown already finished. This is the second stop path of ADR-015 Decision 3 and Verification 6. In a player, `OnDestroy` after step 8 finds the teardown already done.

### Decision 5 (new) — The Composition Root

- `ZoneServerComposition` is the only place in the server that constructs systems, the `IRandomProvider`, the `NpgsqlDataSource` and the relay; it is the "server composition root" named by ADR-011, ADR-013, ADR-014 and ADR-015. It owns everything it constructs, and the teardown's Dispose pass releases it.
- Its Unity-facing work (NGO, scene load, NavMesh, `Application`) goes through small seams so that EditMode tests can run startup order, startup failure at each step, teardown order and idempotence without NGO, a scene or a database. The database part is tested against PostgreSQL in the explicit `Database` category, as Character Persistence Story 014 did.
- Configuration values introduced here are server config, not constants: `DB_WARMUP_TIMEOUT_MS` (5 000), `NGO_SHUTDOWN_WAIT_FRAMES` (100, 5 s at 20 frames per second) and the connection fields of ADR-006 Amendment 3.

### Key Interfaces (Amendment 1)

```csharp
public enum ZoneExitCode { Clean = 0, StartupFailed = 1, Fatal = 2 }

public sealed class ZoneServerComposition : IDisposable {
    public IEnumerator Start(ZoneStartupArgs args);   // S0–S10; on failure requests Teardown(StartupFailed)
    public void RequestTeardown(ZoneExitCode code);    // records the first code; never tears down itself
    public IEnumerator RunTeardown();                  // steps 1–8, started once by the host's Update
    public void TeardownNow();                         // steps 1–7, synchronous, for OnDestroy; no-op if already done
    public bool OnWantsToQuit();                       // Application.wantsToQuit handler; true in the Editor
}

public sealed class ZoneServerHost : MonoBehaviour {   // forwards Start/Update/OnDestroy/wantsToQuit only
}

public interface IZoneNavigation { void Initialize(ZoneID zone, NavMeshDataInstance instance); void Teardown(); }
public interface IGatewayRegistration { void RegisterZone(ZoneID zone, ushort port); }
```

> **Note 2026-10-22 (Networking Core Story 043, `/story-done`).**
> - **Start signature.** As implemented it is `public IEnumerator Start(string[] commandLine)`. S0 itself runs `ZoneStartupArgs.Parse` and loads the config through `IZoneConfigSource`, so a bad command line or config goes through S-fail at S0 like any other step. `ZoneStartupArgs` remains the parsed form.
> - **Teardown on S-fail.** Step 3 runs only once S6 has called `Initialize`. Step 4 runs only once the zone state owner was told of T-2 or T-3. Steps 4b and 5 run only after S9 subscribed the tick. That is how "from the first step that has something to undo" is enforced for seams injected at construction.
>
> The Decision text is unchanged.

> **Note 2026-10-22 (Networking Core Story 045, `/dev-story`, user decision).**
> - **Listen port at S8.** S8 did not say where the transport's port comes from, and `-port` reached only `Gateway.RegisterZone` at S10, so every zone would have listened on `UnityTransport`'s default 127.0.0.1:7777. S8 now also sets the transport's port: `IZoneNetwork.Configure(tickRate, targetFrameRate, vSyncCount, ushort port)` receives the parsed `-port`, and `NgoZoneNetwork` sets `UnityTransport.ConnectionData` (`Address` and `ServerListenAddress` "0.0.0.0", `Port`), so the server listens on all interfaces. It does not call `SetConnectionData`: an overload takes a `NetworkEndpoint`, which would add `Unity.Networking.Transport` to ServerLogic's references (ADR-012 Decision 1). The port registered at S10 is the port the server listens on.
>
> The Decision text is unchanged.

### Migration Plan (Amendment 1)

1. ADR-006 Amendment 3 (pool) and a dated note on ADR-011 Decision 2 (two blocking waits) — with this amendment.
2. Networking Core stories: `ZoneServerComposition` + `ZoneServerHost`, startup S0–S5 and S7–S10 with stand-ins for nav, bounds, CR-ZI-12 and gateway, teardown 1–8, exit codes, quit interception; EditMode tests on the seams; Database-category test of S4. Carries ADR-015 Verification 4 and 6 and A1-1 to A1-7.
3. Zone Instancing / Navigation stories replace the stand-ins (S6, teardown 3 and 4, S10) when those epics are written.
4. ADR-007 C1 amendment picks up exit codes and `TimeoutStopSec`, and `Restart=always`, which also restarts a zone that exited `Clean`.

Notes for the stories (review 2026-10-19):
- `NetworkConfig.TickRate` is a `uint`; S8 casts `ServerTickLoop.TICK_RATE_HZ` (an `int`) explicitly.
- NGO's `DontDestroy = true` (the default) already makes the root `NetworkManager` object `DontDestroyOnLoad`; S1 only has to keep it a root object.
- CR-ZI-12 step 5 (`NotifyZoneExit`) runs before the saves of step 2 complete: their completions are delivered by the drain at teardown step 5. The GDD allows this only when the saves are "committed to the write queue with guaranteed ordering"; the Zone Instancing story that replaces the CR-ZI-12 stand-in states how that ordering holds, or moves `NotifyZoneExit` into the save completion callbacks.

