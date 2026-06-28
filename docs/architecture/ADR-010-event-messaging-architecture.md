# ADR-010: Event/Messaging Architecture

## Status
Accepted (2026-06-27)

## Date
2026-06-27

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.4) |
| **Domain** | Core — C# Messaging / Event Patterns |
| **Knowledge Risk** | LOW — C# `event Action<T>` and interface injection are engine-version-agnostic managed code. No post-cutoff breaking changes in this domain. |
| **References Consulted** | `docs/engine-reference/unity/current-best-practices.md` (confirmed: "C# events for code-to-code — better performance than UnityEvent") |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | None — patterns are pure C# |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-004 (NGO — Accepted ✓): network boundary messaging already established; this ADR governs non-network cross-system messaging only. |
| **Enables** | All Feature-layer and Core-layer implementation stories that involve cross-system communication (mob death → loot + XP; stat change → HUD; zone lifecycle → party notifications). |
| **Blocks** | Feature-layer stories are blocked from merging if they introduce a central EventBus or use `UnityEvent` for server-side game logic (forbidden patterns defined here). |
| **Ordering Note** | ADR-004 governs the network message boundary. This ADR governs everything server-internal and client-internal. NGO RPCs are not "events" in the sense of this ADR. |

## Context

### Problem Statement

The project's 35 GDD systems communicate across many boundaries: mob death triggers loot distribution, XP awards, party kill credit, and mob spawning respawn timers. Stat changes propagate to the HUD. Zone lifecycle transitions notify the party system. Without an authoritative messaging contract, implementers independently choose between a central EventBus singleton, `UnityEvent` fields, raw delegate fields, or direct method calls — producing inconsistent coupling, hidden subscription leaks, and allocation pressure on the 20Hz server tick loop.

`current-best-practices.md` explicitly recommends C# events over `UnityEvent` for code-to-code communication ("better performance"). An explicit decision prevents `UnityEvent` from appearing in server-side game logic where it provides no value and introduces `MonoBehaviour` dependency.

### Constraints

- Server tick loop runs at 20Hz; per-tick allocations compound directly into GC pause pressure. Boxing struct event args (e.g., via `Action<object>`) is forbidden on the server tick path.
- Server-side systems run as plain C# classes (not `MonoBehaviour`) in the headless `UNITY_SERVER` build — `UnityEvent` fields are for inspector wiring in editor/client only.
- Existing ADRs establish direct interface injection as the preferred pattern: `ICharacterPersistence` (ADR-006), `INavigationProvider` (ADR-002), `IZoneStartupService` (ADR-009). This ADR generalises that pattern.
- Network boundary messages (NGO RPCs and custom messages) are governed by ADR-004, not this ADR.
- `Action<T>` delegate allocation happens once at subscription time (not on each emit), making it safe for static subscriptions. Lambda captures that close over heap objects are a leak risk on long-lived systems.

### Requirements

- Point-to-point synchronous calls between systems must use injected interface dependencies, not service locators or singletons.
- Broadcast notifications (one producer, N subscribers) must use C# `event Action<T>` with struct event-argument types to avoid per-emit heap allocation.
- No central EventBus/MessageBus singleton.
- Subscription lifecycle must be explicit: subscribe in constructor/`Initialize()` (server) or `Awake()`/`OnEnable()` (client MonoBehaviour); unsubscribe in `Dispose()`/`OnDestroy()`/`OnDisable()`.
- Server-side cross-system calls that originate in an NGO message handler must go through a queue (per ADR-008 presenter pattern), not call game logic directly.

---

## Decision

### Decision 1 — Three-Tier Messaging Pattern

All cross-system communication in Iron Grind uses exactly one of three patterns, chosen by the nature of the communication:

| Tier | Pattern | When to Use | Allocation |
|------|---------|-------------|------------|
| **1 — Point-to-Point** | Direct method call on injected `IXService` interface | One sender, one receiver; synchronous; return value needed | None |
| **2 — Broadcast** | C# `event Action<T>` with `readonly struct` event args | One producer, ≥2 subscribers; no return value needed | None on emit (delegate allocation at subscribe time only) |
| **3 — Network Boundary** | NGO ClientRpc / ServerRpc / custom messages (ADR-004) | Any message crossing the server↔client network boundary | Governed by ADR-004 |

No other patterns (EventBus singleton, `UnityEvent` for server logic, direct field access between systems, polling shared mutable state) are allowed for cross-system communication.

---

### Decision 2 — Tier 1: Direct Interface Injection

Systems that call each other synchronously do so via constructor-injected interface dependencies:

```csharp
// Interface naming convention: I[SystemName]Service
public interface IPartyService {
    void NotifyZoneEntry(PartyID partyId, EntityID entityId, ZoneID zoneId);
    void NotifyZoneExit(PartyID partyId, EntityID entityId);
}

public interface IZoneService {
    Vector3 GetTownRespawnPoint(ZoneID zoneId);
    IReadOnlyList<EntityID> GetPlayerEntityIDsInZone(ZoneID zoneId);
}

// Consumer receives dependency at construction — no singleton lookup
public class DeathAndRespawnService {
    private readonly IZoneService _zones;
    private readonly ICharacterPersistence _persistence;

    public DeathAndRespawnService(IZoneService zones, ICharacterPersistence persistence) {
        _zones = zones;
        _persistence = persistence;
    }

    private void ExecuteRespawn(EntityID entityId, ZoneID zoneId) {
        Vector3 spawnPos = _zones.GetTownRespawnPoint(zoneId);  // direct call
        // ...
    }
}
```

**Naming rule:** All service interfaces use the `I[SystemName]Service` prefix pattern. Exceptions where an established name already exists: `ICharacterPersistence` (ADR-006), `INavigationProvider` (ADR-002) — these are grandfathered.

**Injection point:** Server-side systems are wired at zone startup via `IZoneStartupService.StartZone()` (ADR-009). Client-side systems are wired via `MonoBehaviour.Awake()` in the scene hierarchy or by a `GameServices` facade `MonoBehaviour` that is set up once on scene load.

---

### Decision 3 — Tier 2: Broadcast Events (C# event with readonly struct)

When a single producer must notify multiple independent consumers, use a C# event with a `readonly struct` argument type:

```csharp
// Event arg: readonly struct — zero boxing, zero heap allocation on emit
public readonly struct MobDeathContext {
    public readonly ZoneID ZoneId;
    public readonly EntityID MobEntityId;
    public readonly MobTypeID MobTypeId;
    public readonly Vector3 DeathPosition;
    public readonly EntityID KillerEntityId;

    public MobDeathContext(ZoneID zoneId, EntityID mobId, MobTypeID typeId,
                           Vector3 pos, EntityID killer) {
        ZoneId = zoneId; MobEntityId = mobId; MobTypeId = typeId;
        DeathPosition = pos; KillerEntityId = killer;
    }
}

// Producer declares event on its interface
public interface ICombatService {
    event Action<MobDeathContext> OnMobDied;
    // ... other methods ...
}

// Consumers subscribe at construction (server) or Awake (client MonoBehaviour)
public class LootService : ILootService, IDisposable {
    private readonly ICombatService _combat;

    public LootService(ICombatService combat) {
        _combat = combat;
        _combat.OnMobDied += HandleMobDeath;   // subscribe once at construction
    }

    private void HandleMobDeath(MobDeathContext ctx) {
        // evaluate loot table, distribute items
    }

    public void Dispose() {
        _combat.OnMobDied -= HandleMobDeath;    // unsubscribe on zone teardown
    }
}
```

**Struct constraint:** All `event Action<T>` types must use a `readonly struct` for `T`. Never use a class type (boxes on emit). Never use `Action<object>`.

**Event naming:** Events use PascalCase prefixed with `On`: `OnMobDied`, `OnStatChanged`, `OnEnhancementCompleted`, `OnPlayerDied`.

**Multi-subscriber order:** Unity makes no guarantee about invocation order among multiple subscribers. If ordering matters between two subscribers, collapse into a single subscriber that coordinates both, or redesign with explicit Tier 1 call ordering.

---

### Decision 4 — Subscription Lifecycle

Leaked subscriptions are the primary maintenance failure mode of the `event` pattern. Lifecycle rules are mandatory:

**Server-side (plain C# classes):**
```csharp
// Subscribe: constructor
public XPService(ICombatService combat) {
    _combat = combat;
    combat.OnMobDied += AwardXP;
}

// Unsubscribe: IDisposable.Dispose()
public void Dispose() {
    _combat.OnMobDied -= AwardXP;
}
```

All server-side services that subscribe to events must implement `IDisposable`. The zone teardown sequence (ADR-009 Decision 3 → CR-ZI-12) must call `Dispose()` on all zone-scoped services before process exit.

**Client-side (MonoBehaviour):**
```csharp
private void Awake() {
    _stats.OnStatChanged += UpdateStatDisplay;
}

private void OnDestroy() {
    if (_stats != null) _stats.OnStatChanged -= UpdateStatDisplay;
}
```

`OnDisable` / `OnEnable` is acceptable for components that are toggled during gameplay.

**Lambda captures are forbidden for persistent subscriptions:**
```csharp
// FORBIDDEN — lambda closes over 'this'; not unsubscribeable by reference
_combat.OnMobDied += ctx => ProcessDeath(ctx.MobEntityId);

// CORRECT — named method; unsubscribeable
_combat.OnMobDied += HandleMobDeath;
private void HandleMobDeath(MobDeathContext ctx) => ProcessDeath(ctx.MobEntityId);
```

---

### Decision 5 — NGO Message Handlers Must Not Call Game Logic Directly

When an NGO message arrives in a `NetworkBehaviour` callback (`ServerRpc`, custom message handler), the callback must enqueue the message for processing in the tick loop — not call game-logic methods directly. This generalises the ADR-008 presenter pattern to all NGO→logic boundaries.

```csharp
// FORBIDDEN — direct game-logic call from NGO callback
[ServerRpc]
private void RequestAttackServerRpc(AttackRequest req) {
    _combatService.ProcessAttack(req);  // FORBIDDEN: outside tick loop
}

// CORRECT — enqueue for tick processing
private readonly Queue<AttackRequest> _pendingAttacks = new();

[ServerRpc]
private void RequestAttackServerRpc(AttackRequest req) {
    _pendingAttacks.Enqueue(req);  // safe: enqueue only, no game logic
}

// Called once per tick by the zone tick loop
public void Tick() {
    while (_pendingAttacks.TryDequeue(out var req))
        _combatService.ProcessAttack(req);
}
```

This preserves the single-threaded main-loop execution guarantee (zone-instancing.md Runtime Model) and keeps all game logic inside the tick loop.

---

### Architecture Diagram

```
TIER 1 — POINT-TO-POINT (direct interface call)
────────────────────────────────────────────────
ZoneInstancing ──IPartyService.NotifyZoneEntry()──► PartyService
DeathRespawn   ──IZoneService.GetTownRespawnPoint()──► ZoneService
CombatService  ──ILootTableService.EvaluateLoot()──► LootTableService


TIER 2 — BROADCAST (C# event, readonly struct arg)
────────────────────────────────────────────────────
                                       ┌──► LootService.HandleMobDeath()
CombatService ──OnMobDied event───────┼──► LevelingService.AwardXP()
                                       ├──► MobSpawningService.ScheduleRespawn()
                                       └──► PartyService.RecordKillCredit()

                                       ┌──► HUDStatDisplay.UpdateStatDisplay()
CharacterStats ──OnStatChanged event──┴──► CombatService.RecalculateDamage()


TIER 3 — NETWORK BOUNDARY (NGO — ADR-004)
──────────────────────────────────────────
Client ──ServerRpc──► Queue<T> ──Tick()──► Game Logic (server)
Server ──ClientRpc──────────────────────► Client MonoBehaviour


FORBIDDEN
──────────
❌ EventBus.Publish(new MobDeathEvent())          // central singleton
❌ combat.OnMobDied += ctx => ...                 // lambda capture (persistent subscription)
❌ ServerRpc → _combatService.ProcessAttack()     // bypasses tick loop
❌ UnityEvent for server-side game logic          // MonoBehaviour dependency, no value on server
```

### Key Interfaces

```csharp
// Convention: I[System]Service for all new service interfaces
public interface ICombatService {
    event Action<MobDeathContext> OnMobDied;
    event Action<PlayerDeathContext> OnPlayerDied;
}

public interface ICharacterStatService {
    event Action<StatChangedArgs> OnStatChanged;
    float GetStat(EntityID entityId, StatType stat);
}

public interface IPartyService {
    void NotifyZoneEntry(PartyID partyId, EntityID entityId, ZoneID zoneId);
    void NotifyZoneExit(PartyID partyId, EntityID entityId);
}

public interface IZoneService {
    Vector3 GetTownRespawnPoint(ZoneID zoneId);
    IReadOnlyList<EntityID> GetPlayerEntityIDsInZone(ZoneID zoneId);
}

// All server-side event subscribers implement IDisposable
// Zone teardown (ADR-009 D3) calls Dispose() on all zone-scoped services
public interface IZoneScopedService : IDisposable {
    void Initialize(ZoneID zoneId);
}
```

---

## Alternatives Considered

### Alternative 1: Central EventBus Singleton

- **Description**: `EventBus.Subscribe<MobDeathEvent>(handler)` / `EventBus.Publish(new MobDeathEvent(...))` — any system can publish/subscribe without holding a reference to the producer.
- **Pros**: Fully decoupled — producer does not know consumers exist.
- **Cons**: Hidden dependencies (nothing shows what subscribes to what without searching the entire codebase); subscription leaks are harder to trace; `Publish(new MobDeathEvent(...))` allocates a managed object per call — on the server at peak mob-death rate this compounds into measurable GC pressure; makes unit tests require a running EventBus instance.
- **Rejection Reason**: Allocation pressure at server tick rate is unacceptable. Direct interface injection is more testable and more explicit about dependencies.

### Alternative 2: UnityEvent for All Cross-System Communication

- **Description**: `[SerializeField] UnityEvent<MobDeathArgs> onMobDied` — inspector-configurable wiring between systems.
- **Pros**: Designer-configurable without code changes; visible in the Unity Inspector.
- **Cons**: Requires `MonoBehaviour` on all systems (impossible for server headless). Slower than C# delegates (reflection-based invocation path). `[SerializeField]` on properties is a compile error in Unity 6.3 — fields only. No benefit on server where there is no editor/inspector.
- **Rejection Reason**: Incompatible with the headless server build. `current-best-practices.md` explicitly recommends C# events over UnityEvent for code-to-code communication.

### Alternative 3: Shared Mutable State (Polling Pattern)

- **Description**: Systems write to shared state objects; other systems poll them each tick rather than receiving notifications.
- **Pros**: Eliminates subscriber coupling entirely.
- **Cons**: Polling every system every tick for changes that rarely occur wastes CPU. Violates the state ownership principle (ADR-006 established `CharacterPersistenceService` as the sole writer of `character_record`).
- **Rejection Reason**: Polling is only appropriate when changes happen every tick (like `GetCurrentVelocity()` in ADR-002 D1). For low-frequency events like mob death, C# events are the correct pattern.

---

## Consequences

### Positive
- Three-tier pattern covers all cross-system communication without exceptions — implementers have one decision (which tier?), not an open-ended style choice.
- Tier 1 (direct injection) is unit-testable by substituting mock interfaces with no test infrastructure beyond the mock itself.
- Tier 2 (C# events with `readonly struct`) has zero per-emit allocation — safe on the 20Hz server tick path.
- No central singleton to boot, tear down, or debug.
- Explicit subscription lifecycle (Dispose/OnDestroy) prevents reference leaks from zone instances that have been torn down.

### Negative
- More explicit wiring at startup: `IZoneStartupService.StartZone()` must manually wire all service dependencies. Hidden coupling is replaced by explicit wiring at the zone lifecycle boundary.
- `readonly struct` event args require recompiling all subscribers when a new field is added to the struct. For frequently-changing event payloads, split into more specific event types rather than adding fields to an existing struct.

### Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| Lambda captures in event subscriptions leaking service references | MEDIUM | MEDIUM — GC pressure; incorrect behavior after zone teardown | Forbidden pattern in registry. Code review checklist: any `event +=` with a lambda expression is a review flag. |
| Invocation-order bugs when two subscribers depend on each other's side effects | LOW | MEDIUM — order-dependent behavior that passes individual tests but fails integration | If order matters, collapse into a single subscriber or use explicit Tier 1 call ordering. Document the dependency. |
| New team member adds a central EventBus "for convenience" | LOW | HIGH — once an EventBus exists, it proliferates rapidly | Forbidden pattern registered in architecture.yaml. Architecture review will flag any `EventBus` class in the codebase. |

---

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|---|---|---|
| `zone-instancing.md` | `NotifyZoneEntry(PartyID, EntityID, ZoneID)` and `NotifyZoneExit(PartyID, EntityID)` — party system notification on all zone entry/exit causes (CR-ZI-14) | Tier 1: direct method call on `IPartyService` injected into Zone Instancing at startup |
| `zone-instancing.md` | `GetPlayerEntityIDsInZone(ZoneID)` read by Networking Relevance Filter each tick (CR-ZI-15) | Tier 1: direct method call on `IZoneService` injected into Relevance Filter |
| `auto-attack-combat.md` | Mob death must trigger loot distribution, XP award, party kill credit, and respawn timer — multiple independent subscribers | Tier 2: `ICombatService.OnMobDied: event Action<MobDeathContext>` — loot, leveling, party, and mob spawning all subscribe independently |
| `character-stats.md` | Stat changes must propagate to the HUD and to combat damage recalculation | Tier 2: `ICharacterStatService.OnStatChanged: event Action<StatChangedArgs>` — HUD and combat both subscribe |
| `loot-table-system.md` | Loot evaluation is a synchronous call with a return value | Tier 1: `ILootTableService.EvaluateLoot(MobTypeID, ZoneID) : LootResult` — direct call from the mob death handler |
| `networking-core.md` | Server tick loop is the single execution context (CR-ZI Runtime Model: single-threaded main loop); NGO callbacks must not modify game state | Decision 5: NGO callbacks enqueue to `Queue<T>`; tick loop dequeues and processes |

## Performance Implications

- **CPU (per emit, Tier 2)**: One delegate dispatch per subscriber + value copy per subscriber. At peak theoretical mob-death rate with 4 subscribers to `OnMobDied`: ~600 delegate dispatches/second. Negligible vs. the 20Hz tick loop's per-frame work.
- **Memory (subscribe time)**: One `Action<T>` allocation per subscription at zone startup. Total subscriptions at MVP: ~20–30 delegate objects allocated once per zone. Zero ongoing allocation at emit time.
- **GC**: Zero GC pressure from event emission when `T` is `readonly struct`. `IDisposable` teardown pattern ensures event handler delegates are released per zone instance lifecycle.

## Migration Plan

Greenfield — no existing cross-system messaging code. Implementation order:

1. Define `IZoneScopedService : IDisposable` interface; wire into `IZoneStartupService.StartZone()` service registration.
2. As each system is implemented, define its service interface (`I[System]Service`) with events and methods following Decisions 2–3.
3. Define `readonly struct` event arg types in a shared `IronGrind.Events` namespace (one file per event type: `MobDeathContext.cs`, `StatChangedArgs.cs`, etc.).
4. Verify `Action<T>` specializations survive IL2CPP stripping in the first server build — add generic type preservations to `link.xml` if `MissingMethodException` occurs on any `Action<StructType>` invocation.

## Validation Criteria

- [ ] All cross-system calls in server-side code use Tier 1 (injected interface) or Tier 2 (C# event) — zero references to any `EventBus` class in `src/`
- [ ] All event arg types declared as `readonly struct` — `grep -rn "event Action<" src/` shows no class-typed T arguments
- [ ] All server-side event subscribers implement `IDisposable`; `Dispose()` unsubscribes from all events
- [ ] All `MonoBehaviour` event subscribers unsubscribe in `OnDestroy()` — no subscription leaked across scene load
- [ ] Zero lambda captures in persistent event subscriptions — `grep -rn "+= ctx =>" src/` returns zero matches in non-disposable, non-one-shot contexts
- [ ] No `ServerRpc` or NGO message handler calls game-logic methods directly — all go through `Queue<T>` (code review gate per ADR-008)

## Related Decisions

- ADR-004: Networking Library (NGO) — Tier 3 (network boundary); this ADR governs Tier 1 and Tier 2 only
- ADR-006: Persistence Layer — `ICharacterPersistence` is the established direct-injection interface pattern this ADR generalises
- ADR-008: Combat UI Framework — `SkillBarPresenter + Queue<T>` is the established NGO→logic decoupling pattern this ADR formalises in Decision 5
- ADR-009: Scene/Zone-Load Management — zone startup wiring is the injection point for Tier 1 service dependencies
