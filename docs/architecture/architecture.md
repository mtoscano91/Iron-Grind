# Project Iron Grind — Master Architecture

## Document Status

- **Version:** 1.0
- **Last Updated:** 2026-06-27
- **Engine:** Unity 6.3 LTS (internal 6000.4), C#, URP, IL2CPP (Linux server + iOS client)
- **GDDs Covered:** 38 Approved MVP systems (+2 Draft auth primitives); 6 Presentation/Polish GDDs Not Started
- **ADRs Referenced:** ADR-001 … ADR-008 (all Accepted, 2026-06-27)
- **Source review:** `docs/architecture/architecture-review-2026-06-27.md` (PASS)
- **Technical Director Sign-Off:** 2026-06-27 — APPROVED WITH CONDITIONS (write the two Foundation Required ADRs — scene/zone-load management, event/messaging architecture — before Zone Instancing & Feature-layer implementation sprints)
- **Lead Programmer Feasibility:** skipped — Lean mode

> This document consolidates decisions already locked in ADR-001…008 and the
> dependency map in `design/gdd/systems-index.md`. It makes no new binding decisions;
> the two Foundation gaps it surfaces (scene/zone-load management, event/messaging
> architecture) are listed as **Required New ADRs**, not decided here.

---

## Engine Knowledge Gap Summary

LLM training covers ~Unity 2023 LTS / early 6000.0; the project runs Unity 6.3 (6000.4).
All engine-touching architecture is verified against `docs/engine-reference/unity/`.

| Risk | Domain | Post-cutoff reality | Systems | ADR |
|------|--------|--------------------|---------|-----|
| 🔴 HIGH | Networking (NGO) | `NetworkTransform.Update`→`OnUpdate` (removed 6.3); `CustomMessagingManager` for project-owned envelope; Multiplay Hosting dead (2026-03-31) | Networking Core, Wire Protocol, Session, CSP, Movement | ADR-004, ADR-007 |
| 🔴 HIGH | UI Toolkit | `VisualElement.transform` setter deprecated (6.2, not removed); USS invalid syntax blocks import (6.3); `[SerializeField]` fields-only (6.3); Painter2D for custom draw | HUD, Combat UI | ADR-005, ADR-008 |
| 🔴 HIGH | Navigation / IL2CPP | `NavMeshAgent` in `UnityEngine.AIModule.dll` (link.xml); async pathfinding; `Application.targetFrameRate` server-sim cadence | Navigation, Enemy AI, Mob Spawning | ADR-002, ADR-003 |
| 🔴 HIGH | Rendering (URP render graph) | Compatibility Mode removed (6.3); `AddRenderPasses` + render graph required | VFX System, Map/Minimap *(not started)* | **none — Required ADR** |
| 🟡 MED | Persistence (server .NET) | Npgsql/Dapper IL2CPP `link.xml`; not a Unity API surface | Character Persistence | ADR-006 |
| 🟢 LOW | Core gameplay/data | no significant post-cutoff change | Stats, Damage, Skill, Status, Equipment, Enhancement, Loot, Leveling, etc. | by-design no ADR |

**Every HIGH-risk domain with a *started* system is ADR-covered.** The only uncovered
HIGH-risk domain (URP render graph) belongs to GDDs not yet authored (VFX, Map/Minimap)
and is deferred until those systems are designed.

---

## Architecture Principles

These five principles govern every technical decision, derived from the game pillars
(Earned Power, Rhythm Mastery, Social Gravity, Legendary Gear) and technical preferences:

1. **Server is authoritative; the client renders and predicts.** All combat, economy,
   and persistence outcomes are decided server-side at a fixed 20 Hz tick
   (`NetworkManager.ServerTime.Tick`). The client predicts movement only (ADR-004,
   CSP GDD); it never owns gold, HP, item, or enhancement state.

2. **The wire contract is project-owned; the library is a substrate.** NGO provides
   connection/transport/clock; the project owns its 10-byte envelope, channel routing,
   and batch framing via `CustomMessagingManager`. A library swap must never touch the
   wire format (ADR-004 Decision 4).

3. **Irreversible outcomes commit before they broadcast.** Any gold debit, item grant,
   or enhancement result is persisted (PostgreSQL, ≤50ms P95) and confirmed before the
   result message is sent. Transaction integrity uses idempotency keys + `PendingPurchase`
   reconciliation, never optimistic client trust (ADR-001, ADR-006).

4. **Data-driven gameplay, code-driven contracts.** Gameplay values live in external
   config / `entities.yaml`; never hardcoded. Module boundaries are explicit interfaces
   (`ICharacterPersistence`, `INavigationProvider`) so systems are unit-testable via DI.

5. **Verify post-cutoff engine APIs on device before trusting them.** Every HIGH-risk
   engine assumption carries an empirical verification gate (profiling on iPhone SE 3rd
   gen, headless `UNITY_SERVER` build checks) before the dependent sprint is greenlit.

---

## System Layer Map

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ PRESENTATION                                                                  │
│   HUD ▸ADR-005   Combat UI ▸ADR-008   Inventory UI*   Enhancement UI*         │
│   Map/Minimap*   Audio System*   VFX System*                                  │
├──────────────────────────────────────────────────────────────────────────────┤
│ FEATURE                                                                       │
│   Auto-Attack Combat   Skill System   Enemy AI   Mob Spawning                 │
│   Death & Respawn   Enhancement   NPC Shop   Consumable Use   Zone Instancing │
│   Party System   Party Chat   Class System   Client-Side Prediction          │
├──────────────────────────────────────────────────────────────────────────────┤
│ CORE                                                                          │
│   Networking Core ▸ADR-004  (+ Session, Wire Protocol, Token, Ghost, OWL,     │
│       Criticality, Channel, Relevance sub-contracts)                          │
│   Movement   Damage Calc   Hit Detection   Status Effects                     │
│   Navigation/Pathfinding ▸ADR-002/003   Character Persistence ▸ADR-006        │
│   Authentication (+ Wire Messages*, Sidecar IPC* primitives)                  │
├──────────────────────────────────────────────────────────────────────────────┤
│ FOUNDATION                                                                    │
│   Character Stats   Item Database   Currency   Leveling                        │
│   Inventory   Loot Table                                                       │
│   Hosting/Infra ▸ADR-007                                                       │
│   ⚠️ Scene / Zone-Load Management — NO ADR (Required)                          │
│   ⚠️ Event / Messaging Architecture — NO ADR (Required)                        │
├──────────────────────────────────────────────────────────────────────────────┤
│ PLATFORM                                                                      │
│   Unity 6.3 LTS · IL2CPP Linux headless server (one process per zone)         │
│   iOS Metal client (landscape, touch) · PostgreSQL 16 (co-located, loopback)  │
│   Hetzner CPX41 VPS · NGO direct-connect UDP · Gateway TLS:443                 │
└──────────────────────────────────────────────────────────────────────────────┘
(* = GDD/spec not yet authored)
```

Layer assignment follows the systems-index dependency tiers. The Networking Core
"system" is in fact a cluster of 10 sub-contract GDDs (session, wire-protocol, token,
ghost-session, ghost-character-state, message-criticality, channel-contract,
relevance-filter, OWL-compensation, test-harness) that together define the Core
networking substrate.

---

## Module Ownership

### Foundation Layer

| Module | Owns | Exposes | Consumes | Engine APIs (risk) |
|--------|------|---------|----------|--------------------|
| Character Stats | Base stat schema (STR/DEX/VIT/INT, derived HP/MP/AP/DEF) | Stat read API | — | none (LOW) |
| Item Database | Static item definitions | Item lookup by ID | — | none (LOW) |
| Currency | Gold balance + `GoldTransactionReason` enum, `gold_version` | `TrySpendGold`, `AddGold` | Persistence | none (LOW) |
| Leveling | XP curve, level thresholds, `heldFreePoints` | XP/level API | Character Stats | none (LOW) |
| Inventory | 20-slot inventory + counts | Slot add/remove | Item Database | none (LOW) |
| Loot Table | Drop tables, roll logic | Roll API | Item Database | none (LOW) |
| Hosting/Infra (ADR-007) | VPS topology, gateway routing table, systemd units | `zoneServerPort` assignment | Persistence (co-located) | IL2CPP Linux headless (HIGH) |
| ⚠️ Scene/Zone-Load Mgmt | Zone scene lifecycle, NavMesh load/unload ordering | (TBD — Required ADR) | Zone Instancing, Navigation | `SceneManager`, `NavMesh.AddNavMeshData` (HIGH) |
| ⚠️ Event/Messaging Arch | Intra-server event dispatch pattern | (TBD — Required ADR) | all gameplay systems | none directly (design choice) |

### Core Layer

| Module | Owns | Exposes | Consumes | Engine APIs (risk) |
|--------|------|---------|----------|--------------------|
| Networking Core (ADR-004) | 10-byte envelope, channel routing, 20Hz tick, batch framing | `CustomMessagingManager` send/recv, `ServerTime.Tick` | NGO transport | `CustomMessagingManager`, `NetworkManager.ServerTime`, `NetworkDelivery`, `NetworkConfig.TickRate` (HIGH) |
| Movement | Position integration, intent messages | Movement state | Networking Core, Character Stats | none gameplay-side (LOW) |
| Damage Calc | Damage formula | `ComputeDamage` | Character Stats | none (LOW) |
| Hit Detection | Server-side hit validation | Hit events | Networking Core, Damage Calc | none (LOW) |
| Status Effects | Buff/debuff stacks | Apply/tick/expire | Character Stats | none (LOW) |
| Navigation (ADR-002/003) | `ZoneNavigationService`, agent pool (150), tick-phase order | `INavigationProvider` | NavMesh subsystem | `NavMeshAgent`, `NavMesh.AddNavMeshData` (HIGH) |
| Character Persistence (ADR-006) | 23-field character record, `PendingPurchase`, save-version concurrency | `ICharacterPersistence` (async) | PostgreSQL via Npgsql/Dapper | Npgsql/Dapper IL2CPP (MED) |
| Authentication | Session token issue/validate | Auth API | Networking Core | (sidecar IPC primitive*) |

### Feature & Presentation Layers

Feature systems consume Core via the interfaces above and own their gameplay state
(combat timing, mob FSM, party membership, shop transaction flow). Presentation systems
(HUD ADR-005, Combat UI ADR-008) own only view state and read gameplay state through
presenter/queue decoupling — never writing authoritative state. Full per-module tables
for these layers are deferred until their GDDs reach implementation (most are Approved;
6 are Not Started).

---

## Data Flow

### 1. Authoritative combat tick (20 Hz, server)

```
[Client] MovementIntent / NotifySkillUsed (NGO U-U / R-OD, project envelope)
   │
   ▼
[Server tick N]  Phase 1: SyncAgentPositions(committed)      ◄ ADR-002 canonical order
                 Phase 2: Navigation.Tick() (drain path queue)
                 Phase 3: Game systems — Hit Detection, Damage Calc, Status, Enemy AI
                          (read GetCurrentVelocity / IsPathStale)
                 Phase 4: IntegratePositions → committed_{N+1}
   │
   ▼
[Server] Build per-client batch (≤512B, relevance-filtered) → CustomMessagingManager send
   │
   ▼
[Client] Apply snapshot; CSP reconciles predicted movement vs ServerTick
```

Transfer: movement intents (U-U), skill/combat (R-OD with dedup), state snapshots
(U-U batched). Producer = server tick loop; consumers = clients. Clock = NGO
`NetworkManager.ServerTime.Tick` @ 20 Hz (ADR-004 Decision 5).

### 2. Irreversible outcome (purchase / enhancement) — commit-before-broadcast

```
[Client] BuyRequest{requestId} (R-OD)
   │
   ▼
[Server] dedup (charId, messageType, requestId)  ◄ ADR-001 A1
   │  new request:
   ▼
   BeginTransaction:  INSERT pending_purchases (GoldDebited)  ┐ one NpgsqlTransaction
                      UPDATE gold_balance (TrySpendGold)      ┘ (ADR-006 Decision 3)
   Commit  ──► PickupRequest (grant item)
                 success → state Completed → delete → BuyResult
                 fail    → AddGold(CompensatingRefund) → Refunded → delete → Rejected
   │
   ▼
[Client] BuyResult (only after DB commit confirmed)   ◄ Principle 3
```

Crash recovery: on reconnect, `SessionHandshake` queries `PendingPurchase` where
`state=GoldDebited`, refunds each, logs (ADR-001 Decision 4 → `networking-session.md`
step 2). Write budget ≤50ms P95 (ADR-006 Decision 4).

### 3. Save/load path

Serialised state = the 23-field `character_records` row (+ `gear_slots`/`inventory_slots`
JSONB). Owner of serialisation = `CharacterPersistenceService : ICharacterPersistence`.
Optimistic concurrency via `WHERE save_version = @expected`; rows-affected=0 →
`ConcurrencyConflict`. At-most-one in-flight write per CharacterID (service-layer queue,
CR-CP-7). `PendingPurchase` lives in a separate `pending_purchases` table, never in the
character row.

### 4. Initialisation order (server boot)

```
1. Application.targetFrameRate = 20        (ADR-002 5a — bounds NavMeshAgent sim)
2. PostgreSQL connection pool (Npgsql)      (ADR-006/007 loopback)
3. Gateway process: TLS:443, routing table  (ADR-007)
4. Per zone process: NavMesh.AddNavMeshData (ADR-002 Decision 4 — one zone/process)
   → ZoneNavigationService.Initialize
5. NGO server transport bind UDP :700N      (ADR-004/007)
6. Accept SessionHandshake → reconciliation → gameplay
```

---

## API Boundaries

The load-bearing contracts programmers implement against (defined in ADRs/GDDs):

```csharp
// Navigation (ADR-003) — sole interface Enemy AI uses
interface INavigationProvider {
    void SetDestination(EntityID id, Vector3 target);
    void SetSpeed(EntityID id, float speed);
    void Stop(EntityID id);
    void Resume(EntityID id);                 // clears isStopped, uses preserved path
    bool IsPathStale(EntityID id);
    Vector3 GetCurrentVelocity(EntityID id);
}

// Persistence (ADR-006 / character-persistence.md)
interface ICharacterPersistence {
    Task<CharacterLoadResult>  LoadCharacter(CharacterID id, CancellationToken ct);
    Task<CharacterSaveResult>  SaveSession(CharacterRecord rec, CancellationToken ct);
    Task<CharacterSaveResult>  SaveIrreversibleOutcome(CharacterRecord rec, CancellationToken ct);
    Task<PendingPurchaseResult> BeginPurchase(CharacterID id, PendingPurchaseRecord r, CancellationToken ct);
    Task                       CompletePurchase(...);  Task RefundPurchase(...);
    Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID id, CancellationToken ct);
}

// Networking envelope ownership (ADR-004 Decision 4) — project owns these fields,
// NGO owns clientId/transport/sequencing disjointly:
//   MessageTypeID (ushort), SequenceNumber (uint), ServerTickNumber (uint),
//   SenderEntityID (uint, project EntityID — NOT NGO clientId)
```

Invariant callers must respect: no module outside `ZoneNavigationService` calls
`Navigation.Tick()`/`SyncAgentPositions()`; `INavigationProvider` methods only in Phase 3.
No gameplay system sends a result message before `SaveIrreversibleOutcome` returns Success.

---

## ADR Audit

From the 2026-06-27 architecture review (PASS). All 8 ADRs Accepted, engine-stamped,
GDD-linked, no cross-ADR conflicts, no dependency cycles.

| ADR | Engine Compat | Version | GDD Linkage | Conflicts | Valid |
|-----|:---:|:---:|:---:|---|:---:|
| ADR-001 Purchase Transaction Integrity | ✅ (backfilled) | ✅ | ✅ | None | ✅ |
| ADR-002 NavMesh Execution Contract | ✅ | ✅ | ✅ | None | ✅ (link.xml → AIModule fixed) |
| ADR-003 Nav Agent Lifecycle | ✅ | ✅ | ✅ | None | ✅ |
| ADR-004 Networking Library (NGO) | ✅ | ✅ | ✅ | None | ✅ |
| ADR-005 HUD UI Framework (UI Toolkit) | ✅ | ✅ | ✅ | None | ✅ |
| ADR-006 Persistence (PostgreSQL+Npgsql+Dapper) | ✅ | ✅ | ✅ | None | ✅ |
| ADR-007 Hosting Backend (Hetzner VPS) | ✅ | ✅ | ✅ | None | ✅ |
| ADR-008 Combat UI (Painter2D + presenter) | ✅ | ✅ | ✅ | None | ✅ |

Traceability coverage is tracked at domain granularity in
`docs/architecture/architecture-traceability.md` — zero Foundation-layer coverage gaps for
*started* systems.

---

## Required ADRs

Decisions surfaced by this architecture that lack an ADR, Foundation-first:

**Must have before coding the affected layer:**
1. **Scene / Zone-Load Management** — Unity scene lifecycle per zone process, NavMesh
   `AddNavMeshData`/`RemoveNavMeshData` ordering vs. tick loop (interacts with ADR-002
   Decision 3 teardown and ADR-007 one-process-per-zone). Foundation. Engine Risk: HIGH.
2. **Event / Messaging Architecture (intra-server)** — how server systems communicate
   without tight coupling (event bus vs. direct calls vs. NGO callback dispatch). Affects
   every Feature system. Foundation. Engine Risk: LOW (design choice, not engine API).

**Should have before the relevant system is built:**
3. **URP Render Pipeline / VFX strategy** — render graph (Compat Mode removed in 6.3),
   draw-call budget ≤100, VFX Graph vs. shader. Blocks VFX System + Map/Minimap GDDs
   (Not Started). Engine Risk: HIGH.
4. **Audio architecture** — mixing, event hooks, addressable audio. Blocks Audio System
   GDD (Not Started). Engine Risk: LOW.

**Can defer to implementation:**
5. ADR-001 OQ-ADR1-2 (`SellRequest` atomicity / `PendingSell`) — amendment to ADR-001.
6. ADR-004 OQ-ADR4-3 (`CustomMessagingManager` vs. thin UTP wrapper) — Networking Core spike.

---

## Open Questions

Deferred decisions that must be resolved before the relevant layer is built:

- **Scene/Zone-Load Management ADR** (Required ADR #1) — blocks Zone Instancing
  implementation; teardown ordering is partially specified in ADR-002 Decision 3 but
  the scene-lifecycle owner is undefined.
- **Event/Messaging Architecture ADR** (Required ADR #2) — blocks a clean Feature-layer
  build; currently each system improvises its own dispatch.
- **6 unauthored GDDs** — Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX
  System, Onboarding/Beginner Zone. Their requirements are not yet in the baseline; the
  Presentation/Polish module tables are intentionally incomplete until they are designed.
- **On-device verification gates** (carried from ADRs): UI Toolkit HUD <0.3ms on iPhone
  SE 3rd gen (ADR-005); 8× Painter2D arcs ≤0.3ms (ADR-008); NavMeshAgent sim cadence and
  `link.xml` AIModule stripping in headless build (ADR-002); NGO tick <30ms at n=50+m=150
  (ADR-004); PostgreSQL write ≤50ms P95 (ADR-006).
