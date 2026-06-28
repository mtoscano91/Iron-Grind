# ADR-009: Scene/Zone-Load Management

## Status
Accepted (2026-06-27)

## Date
2026-06-27

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.4) |
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
