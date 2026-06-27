# Zone Instancing

> **Status**: Needs Revision
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-29
> **Implements Pillar**: Social Gravity (primary — instanced shared zones are the social world), Earned Power (zone tiers gate content progression)

## Overview

Zone Instancing is the server-side runtime that manages the lifecycle of instanced zone sessions for Project Iron Grind. It owns zone creation and initialization, player routing and entry (assigning characters to the correct zone instance on login or reconnect), the authoritative entity roster for each active zone (players and mobs), and zone teardown. It exposes the `GetTownRespawnPoint(ZoneID) : Vector3` API consumed by Death & Respawn, notifies the Party System when members transition between zones, and sends the `ZoneStateSnapshot` bulk packet to clients on zone entry. Zone Instancing is the organizational boundary for all real-time gameplay: the server-authoritative combat tick, loot distribution, relevance filtering, ghost sessions, and position encoding all operate within the zone as their unit of scope.

At MVP, Iron Grind ships with one authored zone. Zone Instancing is designed to support N zones from the start — the zone count is a content decision, not a system constraint. Zone instances are identified by `ZoneID` (a `uint`, `0` = Invalid, never reused within a server run). All zone-aware systems — Networking Core, Auto-Attack Combat, Death & Respawn, Party System, Loot Table, Movement, Character Persistence — hold references to a `ZoneID` and depend on the contracts defined here.

## Player Fantasy

None — directly. Zone Instancing is infrastructure; the player never sees it. What they feel is *arrival into a world that didn't wait for them.* You log in and the field is already alive: a dozen players grinding the same spawns, a party pulling a pack three screens over, someone's +8 weapon glinting across the zone. Nobody pauses to greet you. The grind was happening before you arrived and will continue after you log off. That indifference is the fantasy — Iron Grind is a place, not a level loaded for your benefit. When Zone Instancing fails, the spell breaks instantly: empty fields, a world that feels switched on just for you, the lie that you matter more than you've earned.

The mechanical delivery of this fantasy depends on **fill-first routing** (CR-ZI-6): zones fill toward social density before new instances are created. A world with 40 players spread across 10 instances of 4 each is a world of empty fields. Zone Instancing's routing policy is the single most important design lever for whether the stated fantasy is real.

## Detailed Design

### Runtime Model

Zone Instancing runs as a **Unity headless server build** (Unity 6.3 LTS, IL2CPP). All zone management logic — slot allocation, routing, snapshot assembly, lifecycle transitions — executes on the Unity main thread. Atomicity guarantees throughout this document (e.g., CR-ZI-3's capacity-check-and-allocate, EC-ZI-9's loading-in-progress coalescing) are satisfied by single-threaded main-loop execution. Async I/O operations (e.g., character load in CR-ZI-8 step 3) use Unity coroutines; all zone-state mutations are deferred to the main thread via coroutine continuations. The zone server does not use `Thread` objects or `Task.ConfigureAwait(false)` patterns that could introduce concurrent access to zone state.

**Implications:** No `Interlocked` primitives or `ConcurrentQueue` are required for slot management. `[SerializeField]` rules apply — fields only, no properties. `UnityEngine.Vector3` is the coordinate type. Unity Test Framework (NUnit + headless mode) is used for all ACs.

---

### Core Rules

**CR-ZI-1 — Zone Identifiers**

Zone Instancing distinguishes two zone identifier types:

- **`ZoneTemplateID`** (`enum ZoneTemplateID : byte`): identifies an authored zone *definition* — terrain, mob spawn tables, respawn point, and walkable bounds. Stable across server runs. MVP defines one value: `StartingZone = 0`. All content systems reference templates; the template ID is never transmitted as a live session identifier.
- **`ZoneID`** (`uint`, `0` = Invalid): identifies a live zone *instance* allocated at server runtime. Never reused within a server run. Allocated from a server-process-level monotonic counter starting at 1; the counter is not reset except on server process restart. The allocator must skip `0` if the counter ever wraps (practical only after ~4 billion zone instances per server run). **Cross-run note:** Character Persistence saves `LastZoneID` to the database. After a server restart, the new run's counter starts at 1 — previously saved `LastZoneID` values from the prior run may collide with new instance allocations in the current run. This is safe because CR-ZI-6 rule 2 treats any unrecognised `LastZoneID` as "zone closed, fall through to same-template routing." The "never reused" invariant is per-run, not cross-run.

---

**CR-ZI-2 — Zone Lifecycle**

Zone instances transition through four states: Loading → Active → Draining → Closed (terminal). See States and Transitions table for full trigger and side-effect specification.

---

**CR-ZI-3 — Player Capacity**

Each zone instance holds at most `MAX_PLAYERS_PER_ZONE` = 50 concurrent player sessions. The count includes `Connected`, `Disconnected_SessionActive`, `Dead`, and `Respawning` sessions — any session holding a player slot counts toward capacity. The capacity check is **atomic with slot allocation** by virtue of single-threaded main-loop execution (see Runtime Model): only one coroutine continuation executes at a time, so no two join handlers can interleave between a count-check and a slot-write. The byte-index slot pool supports up to 255 slots maximum — the `MAX_PLAYERS_PER_ZONE` safe range [10, 100] is well within this; the byte index type is the practical capacity ceiling.

---

**CR-ZI-4 — Entity Slot Pools**

Each zone instance maintains two fixed-size slot arrays:

| Pool | Size | Index type | Slot range |
|------|------|-----------|------------|
| Player slots | `MAX_PLAYERS_PER_ZONE` = 50 | `byte` | [0, 49] |
| Mob slots | `MAX_MOBS_PER_ZONE` = 150 | `ushort` | [0, 149] |

Slot indices are **stable** for the lifetime of an entity's presence in the zone — a slot is not reassigned while the entity is alive. This is required because the OWL compensation array (`LastBeatServerTick[]` in `networking-owl-compensation.md` CR-OWL-4) is indexed directly by entity slot index. The array has size `MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE` = 200 entries; player slots occupy [0, 49], mob slots occupy [50, 199].

Slot allocation uses a free-list per pool. Slots are returned to the free-list on entity removal.

---

**CR-ZI-5 — EntityID Allocation**

Zone Instancing mints `EntityID` (`uint`, `0` = Invalid) for all entities entering a zone — both players and mobs. EntityIDs are allocated from a per-zone-instance monotonic counter starting at 1. They are unique within a zone instance but **not** globally unique across zones. A player's EntityID changes when they enter a different zone instance. EntityID `0` must never be issued; the allocator asserts `nextId != 0` before assignment.

---

**CR-ZI-6 — Zone Assignment on Login**

The server assigns a `ZoneID` during authentication, before the client sends `SessionHandshake`. The assigned ZoneID is included in the `LoginResult` response from the auth layer.

**Fill-First Routing Invariant (Social Gravity):** A new zone instance is only created when ALL existing Active instances of the same template have ≥ `ZONE_OPEN_THRESHOLD` players. Routing fills existing instances to density before sharding. This is the primary mechanical delivery of the "world that didn't wait for you" Player Fantasy.

**Party Co-Location Priority:** If the logging-in character is a party member and any party member's slot is in a specific Active zone instance, that instance is the preferred assignment — regardless of its current player count — unless it is at `MAX_PLAYERS_PER_ZONE` hard cap.

**Zone selection rules (evaluated in order):**

1. **Party co-location check:** If the character is in a party with members currently in an Active instance of the appropriate template: route to that instance. If at hard cap: fall through to rule 2.
2. **Returning character (`LastZoneID != 0`):** Find the Active instance matching that exact `ZoneID`. If found and below hard cap: route to it. If not found (zone retired, or prior server run) or at hard cap: fall through to rule 3.
3. **Fill-first fallback:** Find all Active instances of the same template with player count **< `ZONE_OPEN_THRESHOLD`** (below density threshold, below hard cap). Route to the one with the **highest** current player count. If none found: yield no result (fall to rule 4).
4. **Create new instance:** Only if no Active instance of the same template exists below hard cap, OR if no Active instance has < `ZONE_OPEN_THRESHOLD` players (all existing instances have reached the density target). Create a new instance (Loading → Active). If creation fails (T-3): apply CR-ZI-7.
5. **New character (`LastZoneID = 0`):** Apply rules 1, 3, and 4 targeting `ZoneTemplateID.StartingZone`.

**Coalescing rule:** If a same-template instance is already in Loading state when a new routing request arrives: wait for that instance to reach Active (route to it) or Closed (then apply rule 4 to create another). The routing table maintains a per-template `loadingInProgress` flag (set on T-1, cleared on T-2 or T-3) to prevent duplicate creation during simultaneous first logins. Wait is bounded by `ZONE_LOAD_TIMEOUT_SECONDS`; if the Loading instance does not transition within that window, treat as T-3 and proceed to rule 4.

**Template lookup on restore:** Character Persistence saves `LastZoneID` (per-run instance ID). On restore, Zone Instancing maps the instance to its template via the routing table. If the ZoneID is unrecognised (zone retired or prior server run), rule 2 falls through transparently. The routing table is server-side in-memory, rebuilt at server start.

---

**CR-ZI-7 — Zone Capacity Rejection**

If the target zone instance is at `MAX_PLAYERS_PER_ZONE` when a new player attempts to join:
1. Server emits `ZoneFullResponse` (R-OD, server → client) carrying `ZoneID zoneId` and `JoinRejectedReason reason`.
2. No entity slot is allocated. No `SessionHandshake` response is sent. No character data is loaded.
3. The client is responsible for displaying a retry prompt. The server does not queue the player.

`JoinRejectedReason : byte` enum: `ZoneFull = 0`, `ZoneDoesNotExist = 1`, `LoadCharacterFailed = 2`.

`ZoneFullResponse` also carries a `retryAfterSeconds : byte` field — the server's advisory backoff recommendation before the client retries (proposed value: 30s). This is advisory; the client must not treat it as an automatic redirect. The client is responsible for displaying an appropriate prompt; the server does not queue the player.

*(`ZoneFullResponse` schema defined in `networking-wire-protocol.md` — OQ-ZI-1 resolved 2026-05-29. Fields: `ZoneID zoneId`, `JoinRejectedReason reason`, `byte retryAfterSeconds`.)*

---

**CR-ZI-8 — Player Entry Sequence**

The following steps execute in order. Steps 1–9 are connection-driven (outside the tick loop); step 10 adds the entity to the tick loop.

| Step | Action | Owner |
|------|--------|-------|
| 1 | Authentication completes. `LoginResult` sent to client carrying assigned `ZoneID` and session token. | Authentication |
| 2 | Client sends `SessionHandshake` targeting the assigned `ZoneID`. | Client |
| 3 | Server validates session token. `LoadCharacter` called (Character Persistence). If `LoadCharacterCode != Success`: send `ZoneFullResponse(LoadCharacterFailed)` and abort. | Character Persistence |
| 4 | Atomic capacity check + slot allocation. If zone at cap: send `ZoneFullResponse(ZoneFull)` and abort. | Zone Instancing |
| 5 | EntityID minted. Player slot written: `EntityID`, `CharacterID`, `ZoneSessionState = Connected`. | Zone Instancing |
| 6 | `ZoneStateSnapshot` assembled and emitted to joining client (bulk transfer, R-OD, `0xF000–0xFFFF`). Snapshot includes all current player **and** mob EntityState entries. Each entry carries `EntityType : byte` (`Player=0`, `Mob=1`). Player entries carry all EntityState fields. Mob entries carry `MobTypeID : ushort` in place of `characterName`, and omit character-specific fields (level, equipmentAppearanceFlags). Dead player entries carry `isInDeadState = true` and `respawnTicksRemaining = max(0, RespawnTick − currentTick)` — resolves Death & Respawn OQ-DR-6. *(EntityState restructured as tagged discriminated union — OQ-ZI-2 resolved 2026-05-29. Player body: 53–77B; mob body: 35B.)* | Zone Instancing |
| 7 | `SessionReady` emitted **only after** the snapshot has been fully queued for transmission (per `networking-session.md` B-NP-8). `SkillCooldownSnapshot` emitted immediately after. | Networking Core |
| 8 | Party System notified if player is a party member: `NotifyZoneEntry(PartyID, EntityID, ZoneID)`. **Deferred to this step** (after `SessionReady` is queued) to prevent party state divergence — existing party members must not be told the player is "in zone" before the player has received zone state. | Zone Instancing |
| 9 | `PlayerJoinedZone` broadcast to all existing zone clients (R-OD). | Networking Core |
| 10 | Entity enters the server tick loop on the next tick boundary. | Networking Core |

**Dual-gate invariant (client):** The client must receive both `SessionReady` and a complete `ZoneStateSnapshot` reassembly before rendering or sending RPCs. Tick-rate messages (`EntityPositionUpdate`, `CycleTimerBroadcast`) received before the gate opens are **discarded** by the client — the next tick delivers fresh authoritative values.

---

**CR-ZI-9 — ZoneSnapshotRequest (Reassembly Retry)**

If the client's fragment reassembly times out (`FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS`), it sends `ZoneSnapshotRequest { ZoneID zoneId; uint snapshotVersion; }` (C→S, R-OD). On receipt, Zone Instancing assembles a **fresh snapshot of the current zone state** at the time of assembly — not a cached version of the original. Entity states in the retransmit reflect the current tick; the client must expect entity states to differ from the original. The new `snapshotVersion` equals the current server tick (≥ the client's requested version). The client discards any lingering old-version fragments on receipt of `fragmentIndex=0` with the newer version (reassembly protocol step 5, `networking-wire-protocol.md`).

**Deduplication:** The server coalesces concurrent `ZoneSnapshotRequest` messages from the same `(ConnectionID, ZoneID)` pair — multiple requests arriving before the first retransmit completes are merged into a single assembly cycle.

**Rate-limiting:** The server processes at most 1 `ZoneSnapshotRequest` per `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` per connection. Excess requests are silently dropped and logged as `SnapshotRequestRateLimited`. This prevents a client sending `snapshotVersion=0` repeatedly from triggering perpetual snapshot assembly.

*(`ZoneSnapshotRequest` schema defined in `networking-wire-protocol.md` — OQ-ZI-3 resolved 2026-05-29. Fields: `ZoneID zoneId`, `uint snapshotVersion`. Standalone: 22 bytes, R-OD, C→S.)*

---

**CR-ZI-10 — Zone Bounds Constraint**

Each zone template defines a walkable area as an axis-aligned rectangle in world-space. The **absolute maximum half-extent on any axis must not exceed 250 meters** — a conservative margin below the position encoding hard cap of ±327.67m per axis (CR-NET-7.2, `networking-wire-protocol.md`). Zone Instancing validates bounds at Loading-phase initialization and refuses to transition to Active if any walkable bound exceeds ±250m, logging a `ZoneBoundsExceedEncoding` critical alert. Specific bounds for each zone template are authored in the `ZoneDefinition` asset (Level Design responsibility). The movement system's `NAVMESH_STALL_RECOVERY_TICKS` teleport must not place entities outside zone bounds.

---

**CR-ZI-11 — GetTownRespawnPoint API**

Zone Instancing exposes: `GetTownRespawnPoint(ZoneID) : Vector3`

- Returns the static town-entry respawn `Vector3` authored for the zone template matching this `ZoneID`.
- Cached as a `Dictionary<ZoneTemplateID, Vector3>` keyed on **template** (not per-instance), built at server startup from all loaded `ZoneDefinition` assets. The lookup maps `ZoneID → ZoneTemplateID` via the routing table, then fetches from the template cache. The lookup is O(1) and synchronous. Since the cache is keyed on `ZoneTemplateID`, it remains valid after any zone instance closes — there is no window where a retiring `ZoneID` invalidates the respawn point lookup.
- **Fallback chain on missing authored point:**
  1. Authored `ZoneDefinition.townRespawnPoint` — return it.
  2. Zone bounds centroid (computed from walkable bounds min/max) — return it; log `TownRespawnPointNotAuthored` warning.
  3. `Vector3.zero` — log `TownRespawnPointMissing` critical alert (content authoring error).
  No exception is thrown in any case. The caller (`death-and-respawn.md` CR-DR-8) handles the retry/fallback path.
- Resolves Death & Respawn OQ-DR-2.

---

**CR-ZI-12 — Zone Teardown Sequence**

When a zone enters `Closed` state, the following steps execute in order before any slot pool is released:

1. `CancelAllSpawnTimers(ZoneID)` called on Mob Spawning — prevents post-teardown spawn events.
2. For each occupied player slot:
   - `ZoneSessionState == Dead` or `Respawning`: call `SaveSession(id, ZoneClosed, hpOverride: 0f, ct)` on Character Persistence.
   - Otherwise: call `SaveSession(id, ZoneClosed, ct)`.
   *(HP-override overload defined in `character-persistence.md` CR-CP-1 — OQ-ZI-6 resolved 2026-05-29.)*
3. Ghost session handler notified for each `Disconnected_SessionActive` slot — must cache the `ZoneID` before the slot is freed (required for `ZoneSessionEnded(GhostDeath=3)` delivery on future reconnect — `networking-ghost-session.md` CR-GH-6).
4. `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=ZONE_DRAIN_GRACE_SECONDS)` sent to all `Connected` clients. **Step 4 must execute before step 3's ghost handler notification triggers any outbound message to the ghost client's queued connection**, to prevent ghost-session messages racing with `ZoneSessionEnded` on the same connection.
5. `NotifyZoneExit(PartyID, EntityID)` called on Party System for every occupied player slot — ensures no `OutOfZone` state is left stale. **SaveSession (step 2) must complete (or be committed to the write queue with guaranteed ordering) before `NotifyZoneExit` fires**, to prevent the Party System from querying stale character state.
6. All player slots zeroed and returned to the free-list.
7. All mob slots zeroed and returned to the free-list.
8. `ZoneID` removed from routing table and retired. Never reallocated.

**Draining maximum duration:** A Draining zone waits for the last player session to be released. The maximum wait is ≥ `SESSION_TTL_SECONDS` (300s) — a ghost session can hold a slot for up to that duration. T-5 fires when the last session is released (including TTL expiry), not only on voluntary disconnect.

---

**CR-ZI-13 — Mob Entity Management Interface**

Zone Instancing exposes to Mob Spawning:

| Method | Signature | Notes |
|--------|-----------|-------|
| `TryAddMob` | `(ZoneID, MobTypeID, Vector3) : EntityID` | Allocates mob slot, mints EntityID from zone counter. Returns `EntityID(0)` if at `MAX_MOBS_PER_ZONE`. |
| `RemoveMob` | `(ZoneID, EntityID)` | Frees the mob slot. Called when mob corpse timer expires. |
| `GetMobCount` | `(ZoneID) : int` | Current live mob count. Used by Mob Spawning for density enforcement. |

Mob Spawning exposes to Zone Instancing:
- `CancelAllSpawnTimers(ZoneID)` — called during teardown (CR-ZI-12 step 1).

---

**CR-ZI-14 — Party Zone Membership Notifications**

- **Zone entry**: `NotifyZoneEntry(PartyID, EntityID, ZoneID)` — clears `OutOfZone` for this member; triggers `PartyStateUpdate` broadcast.
- **Zone exit** (voluntary logout, SESSION_TTL expiry, teardown): `NotifyZoneExit(PartyID, EntityID)` — sets member `OutOfZone`; triggers `PartyStateUpdate` broadcast. **All three exit causes must call this notification**, including teardown (CR-ZI-12 step 5).

---

**CR-ZI-15 — Relevance Filter Entity List**

Zone Instancing exposes: `GetPlayerEntityIDsInZone(ZoneID) : IReadOnlyList<EntityID>` — a read-only view of all occupied player slot EntityIDs. Consumed by the Networking Relevance Filter each tick during R-U batch construction. Excludes empty slots and mob slots. The filter must not mutate this view.

---

**CR-ZI-16 — Zone Transfers (Deferred to Vertical Slice)**

Zone-to-zone player transfers are **out of scope for MVP**. With one authored zone template (`StartingZone`), no player can voluntarily transfer to a different zone. `DisconnectType.ZoneTransfer = 2` is reserved in the wire protocol for future use. The full transfer wire flow (initiation message, ghost session interaction, cross-zone session token handling, persistence write ordering, Party `OutOfZone` transition protocol) is deferred. A dedicated design pass is required before Vertical Slice adds zone 2.

---

**CR-ZI-17 — Stale Slot Reclamation on Reconnect**

If a player slot is allocated (CR-ZI-8 step 4) but the joining client exhausts `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` and initiates a full reconnect (per F-ZI-2), the original slot may still be occupied when the new `SessionHandshake` arrives.

On receipt of `SessionHandshake` from a `CharacterID` already occupying an allocated slot in the target zone:
1. The stale slot is zeroed and returned to the free-list. Zone occupied count decrements by 1.
2. New slot allocation proceeds normally (CR-ZI-8 step 4).
3. No `PlayerJoinedZone` broadcast was emitted for the stale entry (Party notify at CR-ZI-8 step 8 had not yet fired — it occurs after `SessionReady`). No `NotifyZoneExit` is required.
4. No `SaveSession` call is required — `LoadCharacter` in the original attempt was read-only (EC-ZI-4).

For the disconnection case (client's TCP connection drops rather than reconnects), ghost session machinery handles slot cleanup via `SESSION_TTL_SECONDS` expiry — this rule does not apply.

`ZONE_ENTRY_STALL_TIMEOUT_SECONDS` is a tuning knob (default 35s, slightly above `MaxZoneEntryStall = 30s`) representing the server-side staleness window before a duplicate-`CharacterID` handshake is considered a reconnect rather than a bug.

---

### States and Transitions

| State | Description | Accepts players | Mobs spawning |
|-------|-------------|----------------|---------------|
| **Loading** | Initializing: ZoneDefinition loaded, bounds validated, slot arrays zeroed, respawn point cached, mob spawner primed | No | No |
| **Active** | Normal gameplay | Yes | Yes |
| **Draining** | No new player assignments; existing sessions continue until released | No | No |
| **Closed** | Terminal. All resources released. ZoneID retired. | — | — |

| # | From | To | Trigger | Side Effects |
|---|------|----|---------|-------------|
| T-1 | (new) | Loading | Server allocates ZoneID for a template | Slot arrays zeroed; ZoneDefinition loaded; bounds validated; respawn point cached; `loadingInProgress` flag set for template |
| T-2 | Loading | Active | All initialization complete | Zone added to routing table; mob spawner starts; `loadingInProgress` flag cleared |
| T-3 | Loading | Closed | Init failure (bounds error, data missing, or `ZONE_LOAD_TIMEOUT_SECONDS` elapsed) | ZoneID retired; `ZoneDefinitionLoadFailed` critical alert logged; `loadingInProgress` flag cleared; any coalesced-waiting clients proceed to CR-ZI-6 rule 4 to create a new instance |
| T-4 | Active | Draining | Operator drain or server shutdown signal | New routing disabled; `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=ZONE_DRAIN_GRACE_SECONDS)` broadcast to Connected clients |
| T-5 | Draining | Closed | Last player session released (including via `SESSION_TTL_SECONDS` expiry) | Teardown sequence (CR-ZI-12) runs in full |
| T-6 | Active | Closed | Emergency shutdown | Immediate teardown; `ZoneSessionEnded(gracePeriodSeconds=0)` to all clients |

**ZoneSessionState enum** (`byte`): defines the per-slot session state written at CR-ZI-8 step 5.

| Value | Name | Description |
|-------|------|-------------|
| 0 | `Connected` | Player has an active TCP connection |
| 1 | `Disconnected_SessionActive` | TCP disconnected but SESSION_TTL not yet expired; slot held |
| 2 | `Dead` | Player is in death state awaiting respawn; slot held, counts toward capacity |
| 3 | `Respawning` | Respawn initiated, player transitioning back to Connected; slot held |

**Dead/Respawning during Draining:** A player in `Dead` or `Respawning` state when the zone enters T-4 (Draining) does NOT complete their respawn — the grace period (`ZONE_DRAIN_GRACE_SECONDS`) provides time for connected players to wrap up, but pending respawns are not executed. CR-ZI-12 step 2 calls `SaveSession(ZoneClosed, CurrentHP=0)` for all Dead/Respawning slots regardless of the grace window.

---

### Interactions with Other Systems

| System | Direction | Interface | Notes |
|--------|-----------|-----------|-------|
| **Authentication** | ← Zone Instancing serves | Assigned `ZoneID` included in `LoginResult` | Zone assignment computed during auth (CR-ZI-6). *(OQ-ZI-4 resolved 2026-05-29 — `LoginResult` amended in `auth-wire-messages.md` to carry `zoneId` on success path; `RoutingRedirectMessage` defined.)* |
| **Character Persistence** | ← Zone Instancing calls | `LoadCharacter(CharacterID)` on entry; `SaveSession(reason, HP)` on all exit paths | `LastZoneID = 0` → StartingZone routing. Teardown calls `SaveSession` per slot before freeing (CR-ZI-12). |
| **Networking Core / Tick Loop** | ↔ Zone Instancing | Zone Instancing provides entity roster per zone; tick loop drives per-zone combat evaluation | `LastBeatServerTick[]` is per-zone-instance, indexed by slot index (CR-ZI-4). |
| **Networking Session (Ghost)** | ↔ Zone Instancing | Ghost session handler caches `ZoneID` at ghost-death time; notified during teardown before slot release (CR-ZI-12 step 3) | Required for post-reconnect `ZoneSessionEnded(GhostDeath=3)` delivery. |
| **Networking Relevance Filter** | ← Zone Instancing provides | `GetPlayerEntityIDsInZone(ZoneID)` — read-only player entity list per tick (CR-ZI-15) | Filter must not mutate the list. |
| **Death & Respawn** | ← Zone Instancing provides | `GetTownRespawnPoint(ZoneID) : Vector3` (CR-ZI-11); `isInDeadState`/`respawnTicksRemaining` in snapshot (CR-ZI-8 step 6) | Resolves OQ-DR-2 and OQ-DR-6. |
| **Party System** | ← Zone Instancing calls | `NotifyZoneEntry` / `NotifyZoneExit` on all entry/exit causes (CR-ZI-14) | Teardown must call `NotifyZoneExit` for all occupied slots (CR-ZI-12 step 5). |
| **Mob Spawning** | ↔ Zone Instancing | `TryAddMob` / `RemoveMob` / `GetMobCount` from ZI; `CancelAllSpawnTimers(ZoneID)` from Mob Spawning (CR-ZI-12 step 1) | Mob Spawning owns spawn logic; Zone Instancing owns slots and EntityID minting. |
| **Movement System** | Zone Instancing constrains | Zone bounds define navmesh boundary (CR-ZI-10). All walkable bounds ≤ ±250m per axis. | `NAVMESH_STALL_RECOVERY_TICKS` teleport must not place entities outside zone bounds. |

## Formulas

**F-ZI-1 — ZoneStateSnapshot Fragment Count**

The number of `ZoneStateSnapshotFragment` packets required to transmit a complete zone snapshot:

```
F = max(1, ⌈(P × S_player + M × S_mob) / C_frag⌉)
```

Lower-bounded at 1: a zone snapshot always emits at least one fragment even if P=M=0 at the instant of assembly (joining client must receive a complete — though empty — snapshot for the dual-gate to open).

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Players in zone at snapshot time | `P` | int | [0, `MAX_PLAYERS_PER_ZONE`] | Occupied player slots at the moment of assembly |
| Mobs in zone at snapshot time | `M` | int | [0, `MAX_MOBS_PER_ZONE`] | Active (non-despawned) mob entities |
| Player EntityState byte size | `S_player` | int | 53–77 | Variable: tagged discriminated union (EntityType=1B header + shared header 32B + player fields 20–44B). Min 53B at empty name; max 77B at 24B name. Avg ~65B assumes ~11-char name. `isInDeadState` + `respawnTicksRemaining` add 5B vs. prior spec. OQ-ZI-2 resolved 2026-05-29. |
| Mob EntityState byte size | `S_mob` | int | 35 | Shared header (33B) + `MobTypeID : ushort` (2B). No `characterName`, no MP fields, no `level`, no `equipmentAppearanceFlags`. OQ-ZI-2 resolved 2026-05-29. |
| Fragment payload capacity | `C_frag` | int | 504 | `MAX_MESSAGE_BODY_BYTES − 8` (8-byte fragment header) per `networking-wire-protocol.md` (authoritative source for `MAX_MESSAGE_BODY_BYTES`). If `MAX_MESSAGE_BODY_BYTES` changes, recalculate. |
| Fragment count | `F` | int | ≥ 1 | Total fragments emitted |

**Output at design ceiling (P=50, M=150, avg sizes):**

```
F = max(1, ⌈(50 × 65 + 150 × 35) / 504⌉)
  = max(1, ⌈(3,250 + 5,250) / 504⌉)
  = max(1, ⌈8,500 / 504⌉)
  = 17 fragments
```

**Worst case (P=50 at max-length names, M=150):**

```
F = max(1, ⌈(50 × 77 + 150 × 35) / 504⌉)
  = max(1, ⌈(3,850 + 5,250) / 504⌉)
  = max(1, ⌈9,100 / 504⌉)
  = 19 fragments
```

Fragment count at peak load is in the range **[17, 19]** depending on character name lengths at snapshot time. AC-ZI-8 accepts either value. At `MAX_MOBS_PER_ZONE = 500` (safe range ceiling): F = max(1, ⌈(50×77 + 500×35)/504⌉) = max(1, ⌈21,350/504⌉) = 43 fragments — mobile join latency increases significantly; content designers should not raise mob cap without re-verifying join latency budget.

All fragments are R-OD (reliable ordered); fragments are not UDP-dropped at the application level. Additional fragments increase join latency only in the presence of transport-layer retransmits.

**Cross-reference note (B-22):** B-22 re-validated in `networking-wire-protocol.md` (2026-05-29) at 17–19 fragments. Bulk-transfer cap exemption holds regardless of fragment count. OQ-ZI-7 resolved — see `networking-wire-protocol.md` Open Questions B-22 re-validation entry for full analysis.

---

**F-ZI-2 — Worst-Case Zone Entry Stall**

```
MaxZoneEntryStall = MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS × FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS
```

| Variable | Value | Source |
|----------|-------|--------|
| `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` | 3 | `networking-session.md` |
| `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` | 10s | `networking-session.md` |
| **MaxZoneEntryStall** | **30s** | Derived |

If all 3 retransmit attempts fail, the client abandons and begins a full reconnect. 30 seconds is the maximum zone-entry wait before the client is told to retry. Both input constants are owned by `networking-session.md` — not tuning knobs for this GDD.

**Mobile lifecycle note:** 30 seconds exceeds the iOS app-backgrounding suspension window (~5s). If the player minimises the app during zone entry, the OS will suspend the process and reset the TCP connection. Zone Instancing's stale-slot reclamation (CR-ZI-17) handles the resulting reconnect. Client teams must handle mid-join process suspension as a normal path, not an error path.

## Edge Cases

**EC-ZI-1 — Join arrives while zone is in Loading state**
If a player's `SessionHandshake` arrives targeting a zone still in Loading state (e.g., the zone failed T-3 between routing assignment and join arrival): server sends `ZoneFullResponse(ZoneDoesNotExist=1)`. The routing layer (CR-ZI-6) assigns only Active zones; this race is only reachable if a zone was assigned during Loading and then failed. The `ZoneDoesNotExist` response signals the client to wait for a server redirect rather than loop-retrying.

**EC-ZI-2 — Zone enters Draining while player is mid-snapshot reassembly**
If `ZoneSessionEnded` arrives at the client before the dual-gate opens (before `SessionReady` + complete snapshot reassembly): the client aborts reassembly, discards the partial buffer, and begins a full reconnect. `ZoneSessionEnded` received before the dual-gate opens is an unconditional abort — the client must not attempt to open the gate after receiving it.

**EC-ZI-3 — Ghost slot during zone teardown**
If a `Disconnected_SessionActive` slot is present when the zone enters Closed: CR-ZI-12 step 3 notifies the ghost session handler with the `ZoneID` before the slot is freed. The handler caches this `ZoneID`. When the player next reconnects — even after the zone no longer exists — the handler delivers `ZoneSessionEnded(GhostDeath=3)` using the cached `ZoneID` per `networking-ghost-session.md` CR-GH-6.

**EC-ZI-4 — Concurrent join race at the last slot**
If two players atomically compete for the last available slot: one succeeds; the other receives `ZoneFullResponse(ZoneFull=0)`. The loser's `LoadCharacter` call has already completed (step 3). Character Persistence must not retain session-open side-effects from a rejected join — `LoadCharacter` is read-only. Loaded data is discarded; no `AbortLoad` call is required.

**EC-ZI-5 — Server crash after SessionReady but before snapshot delivery**
If the server crashes after emitting `SessionReady` but before the client receives any snapshot fragment: on reconnect, the client re-enters the full CR-ZI-8 entry sequence from step 2. There is no delta or resume path — every zone entry is a full snapshot. The client must not assume a prior `SessionReady` is still valid after a reconnect.

**EC-ZI-6 — ZoneSnapshotRequest arrives during Draining**
If the client sends `ZoneSnapshotRequest` while the zone is in Draining state: the server honors the retransmit — the zone still holds live state. If the zone has already reached Closed when the request arrives: server sends `ZoneFullResponse(ZoneDoesNotExist=1)`.

**EC-ZI-7 — Adversarial snapshotVersion in ZoneSnapshotRequest**
If a client sends a `ZoneSnapshotRequest` with a `snapshotVersion` greater than the current server tick (forged or corrupt): the server ignores the requested version field entirely and emits a fresh snapshot keyed to the current server tick. The client's stale-discard rule (`IsNewerVersion`, networking-wire-protocol.md CR-NET-7.5) correctly handles any version in response.

**EC-ZI-8 — All template instances full; new instance creation fails**
If zone routing attempts to create a new zone instance and that instance fails to reach Active (T-3 abort — bounds error, asset missing, or init timeout): server sends `ZoneFullResponse(ZoneDoesNotExist=1)` and logs a `ZoneDefinitionLoadFailed` critical alert. The client must not retry automatically — `ZoneDoesNotExist` signals a configuration error, not a transient capacity issue.

**EC-ZI-9 — Two simultaneous first logins trigger duplicate instance creation**
Handled by the coalescing rule in CR-ZI-6: if a same-template instance is already in Loading state, subsequent routing requests wait for it to reach Active or Closed, bounded by `ZONE_LOAD_TIMEOUT_SECONDS`. If the Loading instance fails (T-3), the waiting requests proceed to create a new instance. The per-template `loadingInProgress` flag ensures at most one Loading instance per template at any time.

**EC-ZI-10 — TryAddMob returns EntityID(0) at mob cap**
If `TryAddMob` returns `EntityID(0)` because the zone is at `MAX_MOBS_PER_ZONE`: the mob was not spawned this cycle. Zone Instancing does not retry. Mob Spawning reschedules the spawn attempt for the next cycle via its normal respawn timer. A sustained cap-full condition is logged as a `MobSlotExhausted` advisory once per zone per minute.

**EC-ZI-11 — NotifyZoneExit called twice for the same entity**
If a player logs out during Active state (triggering `NotifyZoneExit`), their slot is emptied, and teardown later iterates occupied slots: the slot is already empty and is skipped (CR-ZI-12 step 5 is guarded to occupied slots only). If a bug causes `NotifyZoneExit` to fire twice for the same EntityID, the Party System must treat a second call for an unknown or already-exited EntityID as a no-op.

**EC-ZI-12 — EntityID counter exhaustion within a zone instance**
If the per-zone-instance EntityID counter approaches `uint.MaxValue` (requires ~4.3 billion entity-lifetime events in one zone session — not a practical concern): the allocator must not wrap to `0` (Invalid). On potential wrap, the zone emits a `EntityIDCounterExhausted` critical alert and immediately enters T-6 emergency Closed.

## Dependencies

**Upstream (Zone Instancing depends on):**

| System | Status | What Zone Instancing needs from it |
|--------|--------|------------------------------------|
| **Networking Core** | Approved | `ZoneID` wire type (`uint`, 0=Invalid); `MAX_PLAYERS_PER_ZONE`; `TICK_RATE_HZ = 20`; `SESSION_TTL_SECONDS = 300`; tick loop for per-zone entity evaluation |
| **Authentication** | Approved | Session token validation; `CharacterID` and `AccountID` established before zone entry; `LoginResult` amended to carry assigned `ZoneID` (OQ-ZI-4) |
| **Character Persistence** | Approved | `LoadCharacter(CharacterID) : CharacterLoadResult` on entry; `SaveSession(reason, HP)` on all exit paths; `LastZoneID` field for routing (0 = new character → StartingZone) |

**Downstream (depends on Zone Instancing):**

| System | Status | What it needs from Zone Instancing |
|--------|--------|-------------------------------------|
| **Death & Respawn** | Approved | `GetTownRespawnPoint(ZoneID) : Vector3` (resolves OQ-DR-2); `isInDeadState`/`respawnTicksRemaining` in ZoneStateSnapshot entry for reconnect-while-DEAD (resolves OQ-DR-6) |
| **Movement System** | Approved | Zone bounds (≤±250m per axis, CR-ZI-10) define the navmesh boundary for each template |
| **Party System** | Approved | `NotifyZoneEntry(PartyID, EntityID, ZoneID)` and `NotifyZoneExit(PartyID, EntityID)` on all entry/exit causes (CR-ZI-14) |
| **Networking Relevance Filter** | Approved | `GetPlayerEntityIDsInZone(ZoneID) : IReadOnlyList<EntityID>` per tick (CR-ZI-15) |
| **Networking Ghost Session** | Approved | Ghost handler caches `ZoneID` before slot release during teardown (CR-ZI-12 step 3) |
| **Mob Spawning** | Approved | `TryAddMob`, `RemoveMob`, `GetMobCount` APIs (CR-ZI-13); `CancelAllSpawnTimers(ZoneID)` from Mob Spawning for teardown; `IMobDefinitionRegistry` injected into Mob Spawning at T-1 (reads `MaxHP` for zone snapshot) |
| **Navigation/Pathfinding** | Approved | Zone bounds define the pathfinding space for mob AI; `ZoneNavigationService.Initialize(ZoneID, NavMeshData)` called during zone `Loading` phase (CR-NAV-2); `Teardown()` called on zone `Closed` entry before per-mob despawn (CR-NAV-3) |
| **Client-Side Prediction** | Not Started | Zone authority model — server-authoritative positions per zone |

**Bidirectional consistency check:**
- Networking Core → references Zone Instancing in tick loop (mob entity count placeholder confirmed 150) ✓
- Networking Core → F-NET-9 (mob tick budget extension) confirms 150 mobs within the tick budget framework: mob CTBs not transmitted (Rhythm Mastery is player-only); mob HP/position subject to existing relevance filter (RFR-6) and drop policy; Beat evaluation O(200) ADR profiling requirement added. ✓ (OQ-ZI-8 resolved 2026-05-29)
- Character Persistence → `LastZoneID` field documented; first-login sentinel (0) handled by CR-ZI-6 ✓
- Character Persistence → `SaveSession(ZoneClosed[, hpOverride])` overload defined; `ZoneClosed` added to `SessionEndReason` ✓ (OQ-ZI-6 resolved 2026-05-29)
- Death & Respawn → OQ-DR-2 and OQ-DR-6 resolved by this GDD (pending re-review) ✓
- Party System → `OutOfZone` flag and zone membership notification interfaces match ✓
- Networking Relevance Filter → `GetPlayerEntityIDsInZone` noted as dependency ✓
- Authentication → `LoginResult` amended to carry `zoneId` on success path; `RoutingRedirectMessage` defined ✓ (OQ-ZI-4 resolved 2026-05-29)
- Networking Wire Protocol → B-22 re-validated at 17–19 fragments ✓ (OQ-ZI-7 resolved 2026-05-29); EntityState amended (EntityType, MobTypeID, isInDeadState, respawnTicksRemaining) ✓ (OQ-ZI-2 resolved); ZoneFullResponse + ZoneSnapshotRequest schemas defined ✓ (OQ-ZI-1, OQ-ZI-3 resolved)
- Mob Spawning → GDD Approved 2026-06-11; CR-ZI-13 bidirectional interface confirmed; `IMobDefinitionRegistry` injection at T-1 documented ✓
- Navigation/Pathfinding → GDD Approved 2026-06-14; Initialize/Teardown lifecycle confirmed (CR-NAV-2/3); `INavigationProvider` implementation confirmed ✓

## Tuning Knobs

| Knob | Constant | Default | Safe Range | Effect |
|------|----------|---------|------------|--------|
| Max zone population | `MAX_PLAYERS_PER_ZONE` | 50 | [10, 100] | Hard player slot ceiling. **Cascade:** (1) Raising to 100 requires `MAX_MESSAGE_BODY_BYTES ≥ 1,002` for the CycleBroadcast packet. (2) All mob OWL compensation indices shift: mob OWL index = `MAX_PLAYERS_PER_ZONE + mob_slot_index` — `networking-owl-compensation.md` must be updated concurrently. (3) Buffer pool grows ~4× at n=100 (~300 KB/zone vs. ~76.7 KB/zone at n=50). (4) F-ZI-1 fragment count increases. Authoritative definition in `networking-wire-protocol.md`; cross-referenced here. `byte` slot index type caps effective max at 255. |
| Max mob population | `MAX_MOBS_PER_ZONE` | 150 | [50, 500] | Hard mob slot ceiling. Drives `LastBeatServerTick[]` array size (`MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE`). Raising to 500: F-ZI-1 worst case = 44 fragments — significant mobile join latency risk. Content designers set actual density per template; this is the architecture ceiling. |
| Zone routing density threshold | `ZONE_OPEN_THRESHOLD` | 35 | [20, 48] | Minimum player count that ALL Active same-template instances must reach before a new instance is created (fill-first routing, CR-ZI-6). Must be < `MAX_PLAYERS_PER_ZONE` by at least 2 to prevent immediate re-trigger. Lower values = earlier sharding (less social density). Higher values = denser worlds (slower shard creation at cap). Drives Social Gravity pillar delivery. |
| Zone drain grace period | `ZONE_DRAIN_GRACE_SECONDS` | 60 | [30, 300] | Time given to Connected players after T-4 (ZoneSessionEnded broadcast) before teardown is forced. Does not prevent teardown if all sessions release before this window expires (T-5). Value is transmitted to clients in `ZoneSessionEnded.gracePeriodSeconds`. |
| Zone load timeout | `ZONE_LOAD_TIMEOUT_SECONDS` | 30 | [10, 120] | Maximum time a zone instance may remain in Loading state before T-3 abort is triggered. Prevents the per-template `loadingInProgress` coalescing flag from deadlocking routing indefinitely on asset load failure. |
| Zone entry stall timeout | `ZONE_ENTRY_STALL_TIMEOUT_SECONDS` | 35 | [31, 60] | Server-side window for detecting a stale allocated slot when the same `CharacterID` reconnects (CR-ZI-17). Must be > `MaxZoneEntryStall` (30s) so the client's own retry logic fires before this triggers. |
| Zone walkable bound | *(per-template, in ZoneDefinition asset)* | TBD per zone | max ±250m per axis | Hard constraint from CR-ZI-10 position encoding. Level Design authors bounds; Zone Instancing validates at Loading-phase init. Exceeding ±250m fails initialization (T-3). |
| Fragment reassembly timeout | `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` | 10s | [5, 30s] | Time before client retransmits `ZoneSnapshotRequest`. Authoritative in `networking-session.md`. |
| Snapshot retransmit limit | `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` | 3 | [1, 5] | Retransmit attempts before client abandons zone entry. `MaxZoneEntryStall = 3 × 10s = 30s` at defaults (F-ZI-2). Authoritative in `networking-session.md`. |

## Visual/Audio Requirements

None — Zone Instancing is pure infrastructure. All visual effects for zone entry (fade-in, spawn placement, HUD initialization) are owned by client systems consuming the snapshot data. Zone-entry UX belongs in the Combat UI GDD or a dedicated Zone Transition UX spec.

## UI Requirements

None — Zone Instancing has no player-facing UI. The dual-gate loading wait is invisible; any loading indicator is owned by the client app layer.

> **📌 UX Flag — Zone Entry:** If a loading screen or zone-entry transition is added (Vertical Slice scope), run `/ux-design` to create `design/ux/zone-entry.md` before writing stories.

## Acceptance Criteria

All Logic criteria are unit-testable without a running server. Integration criteria require a running zone instance and typically use `INetworkTestObserver` and/or mock Party System/notification bus.

**AC-ZI-1 (Logic)** — Given a player with `LastZoneID = 0` and no Active `StartingZone` instance exists, When zone routing runs (CR-ZI-6), Then a new `StartingZone` instance is created and the player is assigned to it; the returned `ZoneID` is non-zero and does not match any previously retired `ZoneID` in this server run.

**AC-ZI-2 (Logic)** — Given a player with `LastZoneID != 0` and that zone is still Active, When zone routing runs, Then the player is assigned to that exact zone instance; `ZoneID` in the response matches `LastZoneID`.

**AC-ZI-3 (Logic)** — Given a player with `LastZoneID != 0` and that zone no longer exists (Closed or never issued in this server run), When zone routing runs, Then the player is routed to any Active instance of the same template, or a new one is created; no `ZoneDoesNotExist` error is returned.

**AC-ZI-4 (Logic)** — Given a zone with exactly 49 of 50 player slots allocated, When two players submit entry requests targeting the same zone in the same server tick (concurrent request simulation), Then exactly one player receives a valid slot assignment and the other receives `ZoneFullResponse(ZoneFull=0)`; total occupied slot count after both requests is exactly 50, never 51.

**AC-ZI-5 (Integration)** — Given a zone template whose walkable bound exceeds ±250m on any axis (crafted test asset), When the zone Loading phase runs bounds validation (CR-ZI-10), Then the zone transitions to Closed via T-3 without reaching Active; a `ZoneBoundsExceedEncoding` critical alert is logged; the zone is never presented to the router.

**AC-ZI-6 (Integration)** — Given a player that has received `SessionReady` but whose `ZoneStateSnapshot` reassembly is incomplete (dual-gate not open), When the test observer captures that client's outbound message stream, Then zero outbound RPCs (`MovementIntentMessage`, `SkillCastRequest`, `NotifySkillUsed`) appear in the stream before the snapshot-reassembly-complete event is recorded. Verified via `INetworkTestObserver.OnZoneGateOpened(clientId)` hook.

**AC-ZI-7 (Logic)** — Given a client that has sent `ZoneSnapshotRequest` with `snapshotVersion = V`, When the server processes the request at current tick `T > V`, Then the retransmitted snapshot carries `snapshotVersion = T` (not V); the payload reflects zone state at tick T.

**AC-ZI-8 (Logic)** — Given a zone snapshot assembled at `P = 50` players and `M = 150` mobs with known `characterName` byte lengths, When F-ZI-1 is evaluated, Then the server emits exactly `F` `ZoneStateSnapshotFragment` messages where `F ∈ [17, 19]` — 17 if all character names encode to ≤ average length; 19 if all encode to maximum length (22 chars). The test must supply known character names to produce a deterministic expected value and assert the exact count via `INetworkTestObserver.GetOutboundMessageCount(clientId, ZoneStateSnapshotFragment.MessageTypeID)`.

**AC-ZI-9 (Logic)** — Given an Active zone with an authored town respawn point in its `ZoneDefinition`, When `GetTownRespawnPoint(ZoneID)` is called 1,000 consecutive times, Then every call returns the same `Vector3`; heap allocation across all 1,000 calls is zero bytes as measured by `GC.GetAllocatedBytesForCurrentThread()` delta (record before, record after, assert delta = 0). `GC.Collect()` must not be called during the measurement. Note: `GC.GetTotalMemory(false)` is NOT an acceptable substitute — it reflects heap state, not allocation delta, and will produce unreliable results under Unity IL2CPP.

**AC-ZI-10 (Logic)** — Given a zone whose `ZoneDefinition` has no town respawn point authored **and no valid walkable bounds** (a minimal/empty content-error ZoneDefinition that never passed loading validation), When `GetTownRespawnPoint(ZoneID)` is called, Then it returns `Vector3.zero`; a `TownRespawnPointMissing` critical alert is logged; no exception is thrown.

**AC-ZI-11 (Logic)** — Given a freshly created zone instance, When 10 entities (players or mobs) are added in sequence, Then they receive `EntityID` values `[1, 2, 3, …, 10]`; `EntityID = 0` never appears in any assignment.

**AC-ZI-12 (Integration)** — Given an Active zone with 3 connected players (1 in `Dead` state) and 5 mobs, When the teardown sequence runs to completion, Then: (a) `SaveSession` was called for the `Dead` player with `CurrentHP = 0`; (b) the other 2 players' sessions were saved; (c) the Party System mock received `NotifyZoneExit` for all 3 EntityIDs; (d) all player slots report `Empty`; (e) `ZoneSessionEnded(ZoneClosed=0)` appears in `INetworkTestObserver` capture for connected clients.

**AC-ZI-13 (Integration)** — Given a mock notification bus that counts `NotifyZoneExit` calls per EntityID, When a player exits a zone by any single cause (logout, SESSION_TTL expiry, or teardown), Then the mock records exactly 1 `NotifyZoneExit` call for that EntityID — never 0, never 2.

**AC-ZI-14 (Logic)** — Given a player whose zone entry stalls (slot allocated, snapshot not acked), When `MaxZoneEntryStall = 30s` elapses without reassembly (F-ZI-2), Then the player's slot is freed, zone player count decrements by 1, the player receives a timeout response, and no RPCs attributed to this player appear in subsequent tick batches.

**AC-ZI-15 (Logic)** — Given a call to `GetPlayerEntityIDsInZone(ZoneID)`, When the returned `IReadOnlyList<EntityID>` is inspected, Then any attempt to cast to `IList<EntityID>` and call a mutating method results in `InvalidCastException` or `NotSupportedException` — no silent state corruption.

**AC-ZI-16 (Logic)** — Given a server process that creates and closes 100 zone instances sequentially, When each `ZoneID` is recorded at allocation time, Then all 100 values are distinct; none equals `0`; none is reused after its zone enters Closed.

**AC-ZI-17 (Logic)** — Given a zone with 0 mobs, When `TryAddMob` is called 150 times, Then each call returns a distinct non-zero `EntityID` and `GetMobCount` returns 150. When `RemoveMob` is called for 50 entities, Then `GetMobCount` returns 100. When `TryAddMob` is called 51 more times, Then each returns `EntityID(0)` and `GetMobCount` remains 100.

**AC-ZI-18 (Logic)** — Given a zone whose `ZoneDefinition` has no town respawn point authored **but has valid walkable bounds** (the typical under-authored zone that passed loading validation), When `GetTownRespawnPoint(ZoneID)` is called, Then it returns the bounds centroid `Vector3` (computed as `(boundsMin + boundsMax) * 0.5f`); a `TownRespawnPointNotAuthored` **warning** (not critical alert) is logged; `Vector3.zero` is NOT returned; no exception is thrown.

**AC-ZI-19 (Logic)** — Given a server with exactly one Active `StartingZone` instance at exactly `ZONE_OPEN_THRESHOLD = 35` players, When a new character (no `LastZoneID`, not in a party) requests zone routing, Then routing creates a new `StartingZone` instance (transition T-1: Loading); the new player is assigned to the new instance; the existing 35-player instance receives no new assignment; the new `ZoneID` is non-zero and distinct from the existing zone's `ZoneID`.

**AC-ZI-20 (Logic)** — Given a server with two Active `StartingZone` instances — instance A with 20 players and instance B with 30 players (both < `ZONE_OPEN_THRESHOLD = 35`) — When a new character (no party, no `LastZoneID`) requests zone routing, Then the character is assigned to instance B (player count 30, the highest below threshold, fill-first per CR-ZI-6 rule 3); instance A's player count is unchanged; no new zone instance is created.

## Open Questions

| ID | Question | Blocking? | Owner | Status |
|----|----------|-----------|-------|--------|
| OQ-ZI-1 | **`ZoneFullResponse` wire schema** must be registered in `networking-wire-protocol.md`. Fields: `ZoneID zoneId`, `JoinRejectedReason reason : byte`. CR-ZI-7 defines the behavior but no wire schema exists. Referenced in ST-NET-2 (`networking-session.md`) as "overflow response" without a schema. | **BLOCKING** — before implementation | `networking-wire-protocol.md` | **RESOLVED 2026-05-29** — Schema defined in `networking-wire-protocol.md` Zone Instancing Messages section. Fields: `ZoneID zoneId`, `JoinRejectedReason reason`, `byte retryAfterSeconds`. `JoinRejectedReason` enum also defined (ZoneFull=0, ZoneDoesNotExist=1, LoadCharacterFailed=2, Other=255). |
| OQ-ZI-2 | **`EntityState` struct needs `EntityType : byte` and `MobTypeID : ushort` fields** to support mob entities in `ZoneStateSnapshot` (CR-ZI-8 step 7). S_mob ≈ 37B after removing `characterName` and MP fields. Wire protocol amendment required. | **BLOCKING** — before implementation | `networking-wire-protocol.md` | **RESOLVED 2026-05-29** — EntityState restructured as tagged discriminated union in `networking-wire-protocol.md`. `EntityType : byte` is now first field. Player body: 53–77B (adds `isInDeadState` + `respawnTicksRemaining`). Mob body: 35B (shared header 33B + `mobTypeId : ushort` 2B). `EntityType` enum defined (Player=0, Mob=1). |
| OQ-ZI-3 | **`ZoneSnapshotRequest` wire schema** is referenced in `networking-session.md` and CR-ZI-9 but defined nowhere. Fields: `ZoneID zoneId`, `uint snapshotVersion`. Must be added to `networking-wire-protocol.md`. | **BLOCKING** — before implementation | `networking-wire-protocol.md` | **RESOLVED 2026-05-29** — Schema defined in `networking-wire-protocol.md` Zone Instancing Messages section. Standalone: 22 bytes (R-OD, C→S). Rate-limiting and forged-version behavior documented. |
| OQ-ZI-4 | **`LoginResult` in `auth-wire-messages.md`** must be amended to carry the server-assigned `ZoneID`. Without it, the client has no zone to target in `SessionHandshake`. Also: add a `RoutingRedirectMessage` (S→C) for the case where the assigned zone transitions between routing assignment and handshake arrival — the client needs a redirect path, not just a retry loop. | **BLOCKING** — before implementation | `auth-wire-messages.md` | **RESOLVED 2026-05-29** — `LoginResult` amended to unified success+failure result (adds `zoneId: uint` on success path). `RoutingRedirectMessage` schema added (carries `newZoneId: uint`; triggers on zone becoming unavailable before `SessionHandshake`). Both registered in `entities.yaml`. |
| OQ-ZI-5 | **`INetworkTestObserver.OnZoneGateOpened(clientId)` hook** needed for AC-ZI-6 and AC-ZI-8 testability. Without this observer hook, the dual-gate invariant and snapshot fragment count cannot be independently verified. | **BLOCKING** — before implementation (previously under-triaged as "Recommended") | `networking-test-harness.md` | **RESOLVED 2026-05-29** — `OnZoneGateOpened(uint clientId)` added (fires when both dual-gate conditions met; marks gate-open instant for AC-ZI-6). `GetOutboundMessageCount(uint clientId, ushort messageTypeId)` added (query method for AC-ZI-8 fragment count assertion). AC-ZI-8 signature corrected (`serverId`→`clientId`, `typeof()`→`MessageTypeID`). |
| OQ-ZI-6 | **`SaveSession` signature mismatch:** CR-ZI-12 step 2 calls `SaveSession(ZoneClosed, CurrentHP=0)` for Dead/Respawning slots, but `character-persistence.md` does not define a `SaveSession` overload with an HP parameter. Either the Character Persistence API must be amended to accept an optional HP override, or Zone Instancing must call a separate HP-clear API before `SaveSession`. Resolve in `character-persistence.md`. | **BLOCKING** — before implementation | `character-persistence.md` | **RESOLVED 2026-05-29** — HP-override overload `SaveSession(CharacterID, SessionEndReason, float hpOverride, CancellationToken)` added to `ICharacterPersistence`; `ZoneClosed = 2` added to `SessionEndReason`. CR-ZI-12 step 2 call sites updated accordingly. |
| OQ-ZI-7 | **B-22 re-validation required.** Prior networking-wire-protocol.md review (B-22) analysed mass-join snapshot interaction with the 8-message priority cap assuming 7 fragments. This document establishes 17–19 fragments at peak. B-22 must be re-analysed at these fragment counts before zone instancing implementation begins. | **BLOCKING** — before implementation | `networking-wire-protocol.md` | **RESOLVED 2026-05-29** — B-22 re-validated in `networking-wire-protocol.md` (Zone Instancing amendment). Bulk-transfer exemption holds at any fragment count. Mass-join burst (~97KB) is a performance concern for OQ-NC-SER-3 load simulation, not a protocol issue. No protocol change required. |
| OQ-ZI-8 | **F-NET-9 (mob scope in tick budget) must be resolved before implementation.** Zone Instancing places up to 150 mobs in the tick loop (CR-ZI-8 step 10). F-NET-9 is an open BLOCKING item in `networking-core.md` establishing that mob entities are excluded from the F-NET-6 tick budget model. Until F-NET-9 is resolved and the per-zone tick budget impact of 150 mob entities is confirmed within budget, Zone Instancing's tick assumptions are unvalidated. | **BLOCKING** — before implementation | `networking-core.md` | **RESOLVED 2026-05-29** — F-NET-9 added to `networking-core.md`. Beat evaluation grows to O(200) at peak (4× n=50 baseline); ADR profiling requirement added. Mob CTBs not broadcast (Rhythm Mastery is player-only); mob HP scoped by RFR-6 relevance filter; mob positions subject to existing U-U drop policy. Per-client batch stays ≤118 U-U and ≤37 R-U sub-messages at standard density. OQ-NC-SER-3 in `networking-wire-protocol.md` updated with mob-density simulation tier. |
