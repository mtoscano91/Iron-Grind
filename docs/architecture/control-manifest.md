# Control Manifest

> **Engine**: Unity 6.3 LTS (6000.4)
> **Last Updated**: 2026-06-28
> **Manifest Version**: 2026-06-28
> **ADRs Covered**: ADR-001, ADR-002, ADR-003, ADR-004, ADR-005, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010
> **Status**: Active — regenerate with `/create-control-manifest update` when ADRs change

`Manifest Version` is the date this manifest was generated. Story files embed this date when created. `/story-readiness` compares a story's embedded version to this field to detect stories written against stale rules. Always matches `Last Updated` — they are the same date, serving different consumers.

This manifest is a programmer's quick-reference extracted from all Accepted ADRs, technical preferences, and engine reference docs. For the reasoning behind each rule, see the referenced ADR.

---

## Foundation Layer Rules

*Applies to: scene management, event architecture, save/load, engine initialisation, networking substrate, persistence, hosting infrastructure*

### Required Patterns

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
- IL2CPP `link.xml` must preserve Npgsql and Dapper: `<assembly fullname="Npgsql" preserve="all" />` + `<assembly fullname="Dapper" preserve="all" />` — source: ADR-006
- At-most-one write in flight per `CharacterID` (service-layer per-CharacterID write-queue) — source: ADR-006
- Per-zone-process connection pool: `MaxPoolSize=10, MinPoolSize=2` (ADR-007 authoritative for the connection string; ADR-006's pre-topology estimate of 20 is superseded by ADR-007) — source: ADR-007

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

### Performance Guardrails

- **Persistence write**: warning ≥ 50ms; critical ≥ 100ms; hard timeout = 5s (`PERSISTENCE_WRITE_TIMEOUT_SECONDS`) — source: ADR-006
- **Server tick**: target < 30ms at 50 players + 150 mobs (F-NET-9 profiling gate — must be confirmed before Networking Core implementation is greenlit) — source: ADR-004
- **Per-client batch body**: ≤ 512 bytes (F-NET-6) — source: ADR-004
- **Zone server load time**: < 500ms at MVP asset scope — source: ADR-009
- **NGO channel mapping — identifiers are verification-pending**: `ReliableSequenced` / `Reliable` / `Unreliable` / `ReliableFragmentedSequenced` are the proposed identifiers per ADR-004 Decision 2, but the Unity 6.3 NGO/UTP API is post-LLM-cutoff. The guarantee mapping (R-OD/R-U/U-U) is binding; confirm exact enum member names against the engine reference before writing networking code — source: ADR-004

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

### Performance Guardrails

- **NavMesh simulation**: bounded to 20Hz via `Application.targetFrameRate = 20`; verify empirically on Unity 6.3 `UNITY_SERVER` build — if `targetFrameRate` does not control `NavMeshAgent` simulation cadence, fall back to `Time.fixedDeltaTime = 1f / TICK_RATE_HZ` — source: ADR-002

---

## Feature Layer Rules

*Applies to: secondary mechanics, AI systems, loot, leveling, consumables, secondary features*

### Required Patterns

- All R-OD messages carrying a `requestId` field must use `(charId, messageType, requestId)` as the dedup key — includes `BuyRequest`, `SellRequest`, and `UseItemRequest` — source: ADR-001 Amendment A1
- `Action<T>` specializations for struct event arg types: add generic type preservations to `link.xml` if `MissingMethodException` occurs on any `Action<StructType>` invocation in the first IL2CPP build — source: ADR-010
- `readonly struct` event arg types declared in the shared `IronGrind.Events` namespace (one file per type: `MobDeathContext.cs`, `StatChangedArgs.cs`, etc.) — source: ADR-010

### Forbidden Approaches

- **Never call `Resume()` from a Dormant re-aggro transition** — Dormant has no preserved path; use `SetDestination` — source: ADR-003
- **Never introduce an `EventBus` class anywhere in `src/`** — once present it proliferates; a structural architecture violation — source: ADR-010
- **Never use class-typed event args** — forces heap allocation on every emit; use `readonly struct` — source: ADR-010

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
- **`[SerializeField]` on properties**: compile error in Unity 6.3 — use on private fields only; or use `[field: SerializeField]` for auto-property backing fields — source: ADR-009, `current-best-practices.md`
- **USS syntax errors block import** in Unity 6.3 (was a warning in 6.1/6.2) — all USS must be valid before commit; add USS linting to CI — source: ADR-005
- **SRP Batcher**: enable in URP Asset → Advanced → SRP Batcher for significant CPU win on mobile — source: `current-best-practices.md`
- **iOS build requirements**: IL2CPP + ARM64 + Metal only + ASTC textures — source: `current-best-practices.md`
