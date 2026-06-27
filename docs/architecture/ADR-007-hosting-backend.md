# ADR-007: Hosting Backend — Self-Hosted VPS with Co-located PostgreSQL

## Status
Accepted (2026-06-27)

## Date
2026-06-27

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS |
| **Domain** | Infrastructure — OS/Cloud (no Unity engine APIs involved) |
| **Knowledge Risk** | LOW — hosting topology is cloud infrastructure, not Unity APIs. Unity IL2CPP Linux headless build is a supported, stable configuration confirmed by the engine reference. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`; `docs/engine-reference/unity/breaking-changes.md`; ADR-004 (NGO); ADR-006 (Persistence Layer) |
| **Post-Cutoff APIs Used** | None — this decision is cloud infrastructure, not Unity APIs. One post-cutoff NGO breaking change is noted in Risks. |
| **Verification Required** | (1) Unity 6.3 IL2CPP Linux headless build compiles and runs on Ubuntu 22.04 LTS without errors. (2) NGO server transport binds to UDP port correctly in headless mode. (3) Any game code subclassing `NetworkTransform` must migrate `Update()` override to `OnUpdate()` — NGO 6.3 breaking change. Consult Unity 6.3 upgrade guide (https://docs.unity3d.com/6000.4/Documentation/Manual/UpgradeGuideUnity63.html) for full NGO release notes before implementation. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-004 (NGO — Accepted ✓): establishes NGO as the networking library; this ADR chooses a hosting topology compatible with NGO's dedicated-server direct-connect model. ADR-006 (Persistence Layer — Proposed): establishes the PostgreSQL co-location constraint (≤50ms write latency); this ADR fulfils that constraint by running PostgreSQL on the same machine as the zone server processes. |
| **Enables** | Server infrastructure setup; zone server deployment; Networking Core implementation epic (ADR-004 OQ-ADR4-1 now resolved). |
| **Blocks** | All server implementation epics — no server code can be deployed without a hosting topology. |
| **Ordering Note** | ADR-006 should be promoted to Accepted before or alongside this ADR; its co-location constraint is load-bearing for the hosting decision. |

## Context

### Problem Statement
Zone Instancing requires Unity 6.3 IL2CPP headless server processes running on Linux (CR-ZI Runtime Model). The Persistence Layer ADR (ADR-006) mandates PostgreSQL co-located with the game server at ≤50ms P95 write latency. ADR-004 deferred the hosting backend decision (OQ-ADR4-1); Multiplay Hosting is not an option (shut down 2026-03-31). No server infrastructure work can begin until a hosting backend is chosen and the co-location constraint is resolved.

### Constraints
- PostgreSQL must achieve ≤50ms P95 write latency from the game server process (ADR-006 Decision 4; `PERSISTENCE_WRITE_CRITICAL_MS` = 100ms hard ceiling, `PERSISTENCE_WRITE_WARNING_MS` = 50ms target)
- The game server is a Unity 6.3 LTS IL2CPP headless build for Linux — must run on a Linux VM or bare metal host
- Multiplay Hosting is unavailable (shut down 2026-03-31)
- NGO direct-connect model: clients connect via UDP to the server's public IP:port; Unity Relay is a NAT traversal tool for P2P topologies, not applicable to a public-IP dedicated server
- Indie MVP: simplicity and cost take priority over managed-service features
- One authored zone template at MVP (StartingZone); total concurrent load at launch is modest

### Requirements
- Host Unity 6.3 IL2CPP headless server processes (one process per active zone instance)
- PostgreSQL accessible at localhost latency (~0.1ms) from every zone server process
- Clients connect via NGO UDP transport to the zone server's public IP and port
- Gateway/routing layer: clients need a fixed entry point (stable IP + port) to authenticate and receive zone assignment before connecting to a zone server process
- Cost: < €30/month at MVP launch (single-region, one zone template, low concurrent player count)
- Zone process count: 1–4 active zone instances at MVP launch (fill-first routing CR-ZI-6; `ZONE_OPEN_THRESHOLD` = 35; new instance only when all existing instances have ≥ 35 players)

## Decision

Self-hosted Linux VPS (Hetzner) with all components co-located on a single machine at MVP.

### Architecture Diagram

```
          Clients (iOS/Android)
               │
               ├─── TLS (Port 443, TCP) ──────────────────────────────┐
               │                                                       ▼
               │                    ┌──────────────────────────────────────────────────────────────┐
               │                    │  Hetzner CPX41 — Ubuntu 22.04 LTS — Primary Game Server VPS  │
               │                    │  ──────────────────────────────────────────────────────────  │
               │                    │                                                              │
               │                    │  ┌────────────────────────────────────┐                     │
               └───────────────────►│  │  Gateway Process (C# .NET 8)       │                     │
                                    │  │  Port 443 TLS                      │                     │
                                    │  │  - Session token issue              │                     │
                                    │  │  - Zone assignment (CR-ZI-6)        │                     │
                                    │  │  - Returns zone server port         │                     │
                                    │  └──────────────┬─────────────────────┘                     │
                                    │                 │ in-memory routing table                   │
                                    │    ┌────────────┼───────────────┐                           │
                                    │    ▼            ▼               ▼                           │
                                    │  ┌──────────┐ ┌──────────┐ ┌──────────┐                    │
                                    │  │  Zone 0  │ │  Zone 1  │ │  Zone N  │  Unity 6.3 IL2CPP  │
                                    │  │  :7000   │ │  :7001   │ │  :700N   │  headless process  │
                                    │  │  NGO UDP │ │  NGO UDP │ │  NGO UDP │  per zone instance │
                                    │  └──────────┘ └──────────┘ └──────────┘                    │
                                    │       ▲              ▲            ▲                          │
                                    │       └──── Client UDP ───────────┘  (after zone routing)   │
                                    │                                                              │
                                    │  ┌──────────────────────────────────────────────────────┐   │
                                    │  │  PostgreSQL 16 — 127.0.0.1:5432 (loopback)           │   │
                                    │  │  character_records + pending_purchases                │   │
                                    │  │  Write latency: ~0.1ms P95                           │   │
                                    │  └──────────────────────────────────────────────────────┘   │
                                    └──────────────────────────────────────────────────────────────┘
```

All components (gateway, zone server processes, PostgreSQL) run on a single Hetzner CPX41 VPS at MVP. Zone server processes connect to PostgreSQL via the loopback interface (127.0.0.1), satisfying the ADR-006 co-location constraint at ~0.1ms — 500× headroom below the 50ms write budget.

### Key Interfaces

**Client → Gateway (auth + zone routing)**

```
TLS connection → Port 443 (TCP)
→ Authenticate (session token exchange with Authentication system)
→ Receive LoginResult { uint zoneId; ushort zoneServerPort; }
→ Close TLS connection
→ Open UDP connection to [same public IP]:[zoneServerPort]
→ Send SessionHandshake (NGO R-OD) per networking-session.md CR-NET-6
```

`zoneServerPort` is a new field added to `LoginResult` by this ADR. The existing `zoneId: uint` field was established by zone-instancing.md OQ-ZI-4. A follow-up amendment to zone-instancing.md and auth-wire-messages.md is required to register `zoneServerPort` formally.

**Zone Server ↔ Gateway (process registry)**

At MVP the gateway and zone server processes run on the same machine. The gateway maintains an in-memory routing table (`Dictionary<ZoneID, ushort> zonePortMap`). Zone processes register with the gateway on startup via a local Unix domain socket. On scale-out to a second VPS, this registry moves to a lightweight shared store (Redis or file-based) — deferred to post-MVP.

**PostgreSQL connection string (per zone process)**

```
Host=127.0.0.1;Port=5432;Database=irongrind;Username=zone_server;Password=[env];
Timeout=5;CommandTimeout=5;Pooling=true;MinPoolSize=2;MaxPoolSize=10;
```

One connection pool per zone process. All zone processes connect to the same PostgreSQL instance on the loopback. `CommandTimeout=5` is `PERSISTENCE_WRITE_TIMEOUT_SECONDS` from ADR-006.

**Zone server process management (systemd)**

Zone server processes are managed as systemd services. The gateway spawns and monitors child processes; systemd provides crash recovery (`Restart=always`). Unit name pattern: `irongrind-zone-{N}.service`.

### Decision Rationale

1. **Same-machine co-location** (PostgreSQL + game server on one VPS): trivially satisfies the ADR-006 ≤50ms write budget at ~0.1ms loopback latency. No network-layer concern for the persistence write path.

2. **Self-hosted VPS over managed services**: at MVP player counts (1–4 active zone instances, ≤200 concurrent players), managed game-server fleet services (GameLift, Agones) add operational complexity that returns value only at 20+ concurrent instances. Hetzner CPX41 at ~€20/month handles 4 concurrent Unity IL2CPP processes with headroom.

3. **One process per zone instance**: matches the Zone Instancing GDD runtime model exactly (CR-ZI Runtime Model: "all zone management logic executes on the Unity main thread"). Multiple zone instances in one process would require threading the Unity main loop — not supported without DOTS architecture.

4. **Direct UDP (no relay)**: NGO in dedicated-server mode establishes UDP transport directly between client and server. Unity Relay is a NAT traversal service for P2P topologies; a public-IP VPS does not need NAT traversal. Direct UDP minimises client RTT and eliminates per-GB relay billing.

5. **Hetzner over DigitalOcean / AWS**: Hetzner CPX41 (8 vCPU, 16GB RAM, 20 TB traffic) at ~€20/month provides the best compute-per-euro for this workload. DigitalOcean CPU-Optimised is comparable at ~$40/month. AWS EC2 (c6i.2xlarge) runs ~$250/month on-demand — 12× the cost at MVP scale.

## Alternatives Considered

### Alternative A: AWS GameLift + RDS PostgreSQL (same AZ)
- **Description**: Managed game server fleet scheduling (GameLift) with RDS PostgreSQL in the same availability zone. GameLift allocates and monitors Unity server processes; RDS handles the database.
- **Pros**: Auto-scaling fleet; managed server health checks; global fleet routing; no ops burden for server lifecycle.
- **Cons**: GameLift fleet configuration complexity (build upload, fleet config, session management API); RDS PostgreSQL in same AZ adds ~1–5ms LAN latency (satisfies ≤50ms budget but at 50× the write latency of localhost); Cost: ~$60–$100/month for MVP load — 3–5× self-hosted; GameLift SDK integration wraps the Unity server process.
- **Rejection Reason**: Cost and complexity are disproportionate to MVP scale. The co-location constraint is met but at 50× worse write latency. GameLift SDK adds an engineering dependency that provides no value at 1–4 concurrent zone instances.

### Alternative B: Agones on GKE + Cloud SQL PostgreSQL
- **Description**: Open-source game server orchestration (Agones) on Google Kubernetes Engine; game server processes run as Kubernetes pods; Cloud SQL PostgreSQL in the same GKE region.
- **Pros**: Open-source orchestration; scales to hundreds of pods; portable across cloud providers.
- **Cons**: Kubernetes operational complexity (cluster management, node pools, Agones CRDs) is substantial for an indie team; Cloud SQL in same region adds ~1–3ms LAN latency; Unity IL2CPP Linux build must be containerised (Docker image → container registry → GKE pull); GKE + Cloud SQL: ~$80–$120/month for MVP — 4–6× self-hosted.
- **Rejection Reason**: Container orchestration is engineering overhead that returns value only at 10+ concurrent zone instances. Kubernetes is a full-time operational concern. Deferred to post-launch scale-out path if player count justifies it.

### Alternative C: Unity Relay (NGO transport relay)
- **Description**: Use Unity Gaming Services Relay to route all NGO UDP traffic through Unity's relay servers.
- **Pros**: No server infrastructure to manage; works through NAT.
- **Cons**: Relay is designed for P2P NAT traversal — a dedicated server with a public IP does not need relay. All traffic routes through Unity's relay datacenter, adding ~20–50ms RTT per hop. Billed per data-gigabyte. Not suitable for a server-authoritative dedicated server model with 50 players at 20Hz.
- **Rejection Reason**: Wrong tool for the topology. A dedicated server on a public IP uses direct UDP. Relay would add unnecessary latency (conflicting with CR-NET-5 commit-before-broadcast performance requirements) and bandwidth cost.

## Consequences

### Positive
- PostgreSQL write latency: ~0.1ms P95 (loopback) — 500× headroom below the 50ms ADR-006 budget; `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` = 200ms is trivially achievable on this topology
- Zero managed-service dependencies at MVP — all components run on one Linux machine under full team control
- Cost: ~€20/month (Hetzner CPX41) at MVP launch; scales to ~€40–80/month before requiring a second VPS
- Simplest possible deployment: systemd services, standard Linux tooling, no container orchestration
- NGO direct-connect UDP eliminates relay hop; client RTT is purely network geography

### Negative
- No auto-scaling: adding zone capacity requires manual intervention (provision a second VPS, deploy a zone process). Acceptable at MVP player counts.
- No automated fleet management: zone process crashes trigger systemd restart (near-instant) but do not auto-redistribute in-session players. Players receive `ZoneSessionEnded` and reconnect via the gateway, which creates a new zone instance per CR-ZI-6.
- Single point of failure at MVP: if the VPS goes down, all zones go down. Mitigated by Hetzner's SLA (99.9% uptime), PostgreSQL daily backups, and the reconnect/ghost-session machinery in networking-session.md.

### Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| VPS hardware failure or DDoS | HIGH | Hetzner 99.9% uptime SLA; daily PostgreSQL backup to object storage; cold standby VPS provisionable in <10 minutes from backup |
| Zone process memory leak → OOM kill | MEDIUM | systemd `Restart=always`; `MemoryMax` set per unit; Unity IL2CPP GC monitored via structured server logs |
| PostgreSQL disk fills | MEDIUM | `character_records` rows are O(player count); `pending_purchases` rows cleaned on reconciliation; alert at 80% disk usage via `pg_stat_user_tables` |
| Multi-VPS scale-out needed before gateway IPC is ready | LOW | Gateway → zone registry is in-process at MVP; multi-VPS refactor (Unix socket → Redis) is a discrete story completable in <1 sprint |
| `NetworkTransform.Update` override breaks in NGO 6.3 | MEDIUM | NGO 6.3 breaking change: `NetworkTransform.Update()` can no longer be overridden; must use `NetworkTransform.OnUpdate()` instead. Any zone server code subclassing `NetworkTransform` must be audited before the first IL2CPP build. See Unity 6.3 upgrade guide. |
| Unity IL2CPP Linux headless build fails to compile | MEDIUM | ADR-006 link.xml for Npgsql/Dapper already specified; NGO IL2CPP compatibility established by ADR-004; first build is a Validation Criteria gate before any stories are marked Done |

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|---|---|---|
| `zone-instancing.md` | CR-ZI (Runtime Model): Unity headless server build (Unity 6.3 LTS, IL2CPP) on Linux | VPS runs Ubuntu 22.04 LTS; zone server binary is a Unity 6.3 IL2CPP Linux headless build deployed as a systemd service |
| `zone-instancing.md` | CR-ZI-6 (Fill-first routing): Zone assignment computed server-side; routing table in-memory, rebuilt at server start | Gateway process holds the in-memory routing table (`Dictionary<ZoneID, ushort port>`); rebuilt when zone processes register on startup |
| `zone-instancing.md` | CR-ZI-3 (Player Capacity): `MAX_PLAYERS_PER_ZONE` = 50; capacity enforced server-side | Capacity enforcement is logic within the Unity zone process — no hosting-layer constraint required; one process per zone instance isolates capacity enforcement naturally |
| `character-persistence.md` | ADR-006 Decision 4: PostgreSQL co-located with game server, ≤50ms P95 write latency | PostgreSQL runs on the same VPS as zone server processes; loopback connection (~0.1ms P95) — 500× headroom below the 50ms budget |
| `networking-core.md` | OQ-NET-5: Persistence write latency must not exceed `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` = 200ms budget | ~0.1ms write latency leaves 199.9ms for RTT and server processing; CR-NET-5 commit-before-broadcast is achievable |
| `networking-core.md` | ADR-004 OQ-ADR4-1: Hosting backend deferred (Multiplay not an option) | Direct-connect NGO UDP to public-IP VPS — no relay, no managed fleet, no Multiplay |

## Performance Implications

- **CPU**: Hetzner CPX41 provides 8 vCPU. Each Unity IL2CPP zone process at 20Hz tick with 50 players + 150 mobs: estimated 1–2 vCPU under load (F-NET-9 profiling requirement still open). Supports 4 concurrent zones with headroom for gateway and PostgreSQL. The F-NET-9 mob-density tick budget profiling pass must be run on the target VPS hardware tier.
- **Memory**: Unity IL2CPP headless process: ~200–350MB per zone at MVP asset scope. 4 zones: ~1.4GB. PostgreSQL: ~300–500MB at default `shared_buffers`. Gateway: ~50MB. Total: ~2GB of 16GB VPS RAM — well within budget.
- **Disk I/O**: PostgreSQL writes are small (23-field character record UPDATE, ~1KB). At 50 concurrent players with periodic session saves and purchase records, I/O is negligible on a local NVMe SSD (Hetzner CPX VMs use local NVMe).
- **Network**: F-NET-6 bandwidth model: ~6 Mbit/s per zone at n=50 full load. 4 zones: ~24 Mbit/s. Hetzner CPX41 includes 20 TB/month traffic — no per-GB billing at this scale.

## Migration Plan

Greenfield — no existing server infrastructure. First deployment sequence:

1. Provision Hetzner CPX41 VPS (Ubuntu 22.04 LTS); configure firewall (allow TCP 443, UDP 7000–7099)
2. Install PostgreSQL 16; apply DDL from ADR-006 (`character_records`, `pending_purchases`, indexes)
3. Build Unity 6.3 IL2CPP Linux headless server binary; verify `link.xml` includes Npgsql + Dapper (ADR-006 IL2CPP section); audit any `NetworkTransform.Update()` overrides → migrate to `NetworkTransform.OnUpdate()`
4. Deploy gateway process (C# .NET 8 binary) as systemd service on port 443 TLS
5. Deploy zone server processes as systemd units; verify gateway routing table populates on process register
6. Smoke test: iOS client → TLS auth → receive `zoneServerPort` → UDP connect → `SessionHandshake` completes → `SessionReady` received

**Scale-out path (post-MVP)**:
- Add a second Hetzner VPS: gateway moves to a standalone routing VPS; PostgreSQL primary + streaming replica on a Hetzner managed database; zone processes on worker VPSes connect to primary over Hetzner private LAN (~1ms — within ADR-006 budget); gateway registry migrates from in-process dict to Redis
- At larger scale: Agones on Hetzner k3s or GKE for zone process orchestration; managed Cloud SQL replacing self-hosted PostgreSQL

## Validation Criteria

- Unity 6.3 IL2CPP headless server binary compiles for Linux x64 and runs on Ubuntu 22.04 LTS without errors; NGO transport binds UDP port successfully in headless mode
- PostgreSQL write P95 latency ≤ 1ms on the target VPS measured via `EXPLAIN ANALYZE` on the `character_records` UPDATE with `save_version` constraint (hard ceiling 50ms per ADR-006)
- Zone server process handles 50 concurrent simulated players at 20Hz tick loop for 60 seconds without tick drift (per EC-NET-10: no tick averaging >50ms over a 60s window), verified via `INetworkTestObserver.OnTickCompleted`
- Hetzner CPX41 monthly cost ≤ €25/month at MVP launch (1–4 active zone instances)
- Crash recovery: zone process killed with `SIGKILL` restarts within 5 seconds via systemd; gateway process killed restarts within 5 seconds

## Related Decisions

- ADR-004: Networking Library — NGO (Accepted): establishes NGO direct-connect UDP as the transport; this ADR selects a hosting topology compatible with it
- ADR-006: Persistence Layer — PostgreSQL + Npgsql + Dapper (Proposed): establishes co-location constraint and connection string pattern this ADR fulfils
- `design/gdd/zone-instancing.md`: Runtime Model (CR-ZI), Capacity (CR-ZI-3), Fill-First Routing (CR-ZI-6)
- `design/gdd/networking-core.md`: OQ-NET-5 (persistence write latency budget); ADR-004 OQ-ADR4-1 (hosting backend deferred)
- `design/gdd/networking-session.md`: `SESSION_TTL_SECONDS`, reconnect and ghost-session machinery (zone crash recovery path)
