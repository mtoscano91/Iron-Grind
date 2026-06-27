# Mob Spawning

> **Status**: Approved (lean review #1, 2026-06-11)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-06-11 (lean review fixes applied)
> **Implements Pillar**: Earned Power (primary — mobs are the XP/loot source), Social Gravity (secondary — populated fields create the social world)

## Overview

The Mob Spawning system is the server-authoritative runtime that populates zone instances with enemy entities. It owns two distinct layers: a static data layer (authored `MobDefinition` assets and zone spawn tables) and a runtime layer (timers, density management, and zone lifecycle integration).

Each mob belongs to a **family** — a named group of thematically related variants at different power tiers (e.g., the Bandicoot family: Bandicoot, Wild Bandicoot, Bandicoot Scavenger, Giant Bandicoot). Each variant is a distinct `MobDefinition` with its own stats, attack parameters, and loot table reference, identified by a unique `MobTypeID`. Zone spawn tables reference specific `MobTypeID` entries, authoring which variants appear in which areas of the zone.

At runtime the system manages per-spawn-point respawn timers within each Active zone instance. When a mob dies, its spawn point's timer starts; when the timer fires, the system calls `IZoneInstancing.TryAddMob(ZoneID, MobTypeID, Vector3)` to register the new entity and receive its `EntityID`. Density is enforced against `MAX_MOBS_PER_ZONE = 150`: if the zone is at capacity, the spawn is deferred until a slot opens. The system exposes `CancelAllSpawnTimers(ZoneID)` to Zone Instancing for clean teardown on zone close. MVP ships with 20 mob types spanning the full family/variant range of the starting zone.

The player feels this system as field density: the zone is never empty, mobs are always respawning, and the range of variants from weak to powerful is visible across the map. Mob Spawning is why the grind loop always has a target and why the world looks populated whether you arrived five minutes ago or five hours ago.

## Player Fantasy

The field is a hunting ground, and it runs as deep as you've earned.

Near the town gate the Bandicoots are weak — the first tier, the entry tax. A new player clears them in two hits and calls it warming up. But fifty meters deeper the variant changes: Wild Bandicoots, faster and meaner, the same mob family wearing harder numbers. And deeper still: Bandicoot Scavengers that punish the wrong timing, and at the map's edge, Giant Bandicoots that hit back hard enough to end a session in two mistakes. The mob family is a gradient painted across the world, and your progress is the distance between you and the gate.

The fantasy is **Earned Power made spatial**. A character sheet shows you a number. The map shows you where you can stand.

The moment this system is alive: you're at the threshold between tiers — the line where Scavengers start appearing on your minimap. Last week you died in two hits here. You check your gear, feel your rhythm with the Beat, and step forward. The same type of mob that ended your session now dies on your cadence. You didn't buy that. You didn't unlock it. You ground it, and the map moved.

The momentum underneath this — the texture — is that the field never runs out. Every corpse is a placeholder for the next spawn. The grind never stops because the spawns never stop. There is no "cleared zone." There is only "how long are you willing to stay?" That endlessness is not a flaw; it is the offer. The field is always full and it always runs deeper. The only question is how deep you've earned the right to go.

*Pillar alignment: Earned Power (primary — mob tiers are a spatial ledger of grinded strength) / Rhythm Mastery (secondary — the respawn cadence feeds Beat timing; a field that never stalls keeps the rhythm unbroken) / Social Gravity (supporting note — the shared field is the stage other players occupy; mob density is a social signal showing where the hunting is good).*

*Design test: If debating whether respawn timers should be long (slower, fewer mobs) or short (dense, always full), this fantasy says short — the "never waits on you" texture depends on the field staying populated. If debating whether mob variants should be geographically separated or randomly mixed across the zone, this fantasy says separated — the depth gradient requires readable spatial progression, not a random distribution. If a higher-tier mob variant drops the same loot as a lower-tier one, this fantasy is broken — the depth must have reward attached to it, not just difficulty.*

## Detailed Design

### Core Rules

**CR-MS-1 — MobDefinition (Read-Only Reference)**

Mob Spawning does not own `MobDefinition`. It reads mob type data via `IMobDefinitionRegistry.GetDefinition(MobTypeID)`, an interface owned and implemented by the Enemy AI system. Each `MobDefinition` exposes to Mob Spawning:
- `MobTypeID` — unique identifier (see entities.yaml)
- `FamilyName: string` — display grouping (e.g., "Bandicoot"); runtime-unused by Mob Spawning
- `DisplayName: string` — variant display name (e.g., "Wild Bandicoot")
- `MaxHP: int` — required by Zone Instancing to populate `EntityState.maxHP` in the zone snapshot
- `MobTier: MobTier` — enum `{Common, Uncommon, Rare, Elite}`; determines respawn delay tier (CR-MS-9)
- `AttackWindUpTicks: int` — asserted ≥ `MIN_WIND_UP_TICKS = 12` at server startup by Enemy AI

**Enemy AI GDD update required**: `MobDefinition` must include `MobTier: MobTier` field. Zone Instancing GDD update required: add `IMobDefinitionRegistry` as a read-only dependency.

---

**CR-MS-2 — SpawnPoint Definition**

Level Design authors a `ZoneSpawnTable` per `ZoneTemplateID` — a static data asset (read-only at runtime) containing a `SpawnPoint[]` where each entry specifies:
- `SpawnPointID: uint` (unique within the zone template)
- `MobTypeID: MobTypeID`
- `Position: Vector3` (within zone walkable bounds ≤ ±250m per axis — CR-ZI-10)
- `RespawnDelayOverrideTicks: int` (`0` = use global tier-based delay per CR-MS-9; non-zero = explicit override, reserved for boss/event spawns — OQ-MS-1)

**Invariant**: `SpawnPointCount ≤ MAX_MOBS_PER_ZONE = 150`. Zone T-1 initialization must reject the template and fail to Active if violated, logging `SpawnTableOverCapacity`. This is a Level Design authoring error — not a runtime recovery case.

**Authoring convention (Level Design)**: Cluster spawn points in groups of ≥4 within a 15m radius to create readable encounter areas. Assign weaker variants near the zone entry point and stronger variants toward deeper/peripheral regions. Front-load ~40% of slots (~60 points) with Common-tier variants near town — this is where new players spend the most time and density matters most for retention.

---

**CR-MS-3 — Runtime SpawnPoint State**

Each `SpawnPoint` is instantiated from the `ZoneSpawnTable` on zone T-1. Runtime `SpawnPoint` is a struct (no GC pressure) tracking:
- `State: SpawnPointState` — `{Uninitialized, Live, Dying, Respawning, Deferred, Cancelled}`
- `CurrentEntityID: EntityID` — valid in `Live` and `Dying`; `EntityID.Invalid` otherwise
- `TimerTick: int` — countdown for current phase; `0` in all other states

The `SpawnPoint[]` array (pre-allocated at `MAX_MOBS_PER_ZONE` = 150 capacity) is iterated every server tick. Cost: 150 int decrements and state checks — sub-millisecond within the 50ms tick budget; ~9.6 KB array fits in L1 cache.

`TimerTick` is a reused countdown field: in `Dying` it counts down `DEATH_LINGER_TICKS` (owned by Enemy AI GDD); in `Respawning` it counts down the tier-based respawn delay (CR-MS-9). Both decrement to zero; the active state determines the semantics.

**Thread safety**: The spawner runs exclusively on the server main tick thread. The `GetMobCount` check and the `TryAddMob` call must not be separated by an async yield — concurrent mutation between check and call is not supported and will cause inconsistent density state.

---

**CR-MS-4 — SpawnPoint State Transitions**

| # | From | To | Trigger | Side Effects |
|---|------|----|---------|-------------|
| 1 | `Uninitialized` | `Live` | Zone T-2; `GetMobCount < MAX_MOBS_PER_ZONE` | `TryAddMob(ZoneID, MobTypeID, Position)` called; `CurrentEntityID` set; `MobSpawned` published |
| 2 | `Uninitialized` | `Deferred` | Zone T-2; `GetMobCount = MAX_MOBS_PER_ZONE` | `SpawnPointID` added to deferred list |
| 3 | `Live` | `Dying` | `MobEventBus.MobDied(ZoneID, EntityID)` received for matching `EntityID` | `TimerTick = DEATH_LINGER_TICKS` |
| 4 | `Dying` | `Respawning` | `TimerTick` reaches 0 | `RemoveMob(ZoneID, CurrentEntityID)` called; `CurrentEntityID = EntityID.Invalid`; `TimerTick` set to effective respawn delay (CR-MS-9) |
| 5 | `Respawning` | `Live` | `TimerTick` reaches 0; `GetMobCount < MAX_MOBS_PER_ZONE` | `TryAddMob` called; `CurrentEntityID` set; `MobSpawned` published |
| 6 | `Respawning` | `Deferred` | `TimerTick` reaches 0; `GetMobCount = MAX_MOBS_PER_ZONE` | `SpawnPointID` added to deferred list |
| 7 | `Deferred` | `Live` | Deferred list polled at `SPAWN_DENSITY_RETRY_INTERVAL_TICKS`; `GetMobCount < MAX_MOBS_PER_ZONE` | `TryAddMob` called; swap-removed from deferred list; `MobSpawned` published |
| 8 | Any | `Cancelled` | `CancelAllSpawnTimers(ZoneID)` | All timers cleared; deferred list emptied; no further `TryAddMob`/`RemoveMob` |

**Note on transition 4**: `RemoveMob` is called when the corpse linger expires, using `DEATH_LINGER_TICKS` as the duration. This is the single timer governing the corpse window — Mob Spawning does not define a separate corpse linger constant.

---

**CR-MS-5 — Event Interface (MobEventBus)**

Mob Spawning and Enemy AI interact exclusively through C# events on a per-zone-instance `MobEventBus`. Neither system holds a reference to the other's interface.

**Mob Spawning subscribes to** (raised by Enemy AI):
- `MobEventBus.MobDied(ZoneID zoneId, EntityID entityId)` — triggers transition 3; Enemy AI raises this after calling `ILootTableSystem.ResolveMobDrop` (loot first, spawn lifecycle second)

**Mob Spawning publishes** (consumed by Enemy AI):
- `MobEventBus.MobSpawned(ZoneID zoneId, EntityID entityId, MobTypeID mobTypeId, Vector3 position)` — raised after every successful `TryAddMob`; Enemy AI factory subscribes to initialize `MobController` for the new entity

**MobEventBus lifetime**: Zone Instancing creates one `MobEventBus` instance per zone at T-1 and passes references to both Mob Spawning and Enemy AI during their zone initialization. Mob Spawning subscribes to `MobDied` at T-1. When `CancelAllSpawnTimers(ZoneID)` returns, Mob Spawning clears its `MobDied` subscription. Zone Instancing destroys the bus when the zone reaches Closed.

---

**CR-MS-6 — Deferred Queue**

Deferred spawn points are tracked per zone in a `List<int>` (pre-allocated capacity = `MAX_MOBS_PER_ZONE`; stores `SpawnPointID` values). Every `SPAWN_DENSITY_RETRY_INTERVAL_TICKS`, Mob Spawning iterates forward through the list: call `GetMobCount`; if below cap, call `TryAddMob`, transition to `Live`, swap-remove the entry. A sorted queue provides no ordering benefit — all deferred entries share the same retry cadence.

---

**CR-MS-7 — Zone Active Population (T-2)**

On zone T-2 transition, Mob Spawning iterates the `SpawnPoint[]` in declaration order:
1. For each `Uninitialized` point: call `GetMobCount` before `TryAddMob`
2. `GetMobCount < MAX_MOBS_PER_ZONE` → call `TryAddMob`, transition to `Live`, publish `MobSpawned`
3. `GetMobCount = MAX_MOBS_PER_ZONE` → transition to `Deferred`; remaining points fill via normal deferred polling

If `SpawnPointCount ≤ MAX_MOBS_PER_ZONE` (enforced at T-1), step 3 is a safety fallback and should not be reached under normal conditions.

---

**CR-MS-8 — Zone Teardown**

On `CancelAllSpawnTimers(ZoneID)` (called during CR-ZI-12 step 1, before slot zeroing):
1. All `Dying`/`Respawning` points: `TimerTick = 0`, transition to `Cancelled` — `RemoveMob` is NOT called; Zone Instancing clears all slots in step 7
2. Deferred list cleared
3. No further events published or subscribed after this call returns

---

**CR-MS-9 — Respawn Delay**

The effective respawn delay for a spawn point is:

```
effectiveRespawnDelay =
  (SpawnPoint.RespawnDelayOverrideTicks > 0)
    ? SpawnPoint.RespawnDelayOverrideTicks          // boss/event override (OQ-MS-1)
    : RespawnDelayForTier(MobDefinition.MobTier)   // per-tier global knob
```

Where `RespawnDelayForTier` returns:

| `MobTier` | Tuning Knob | Default | Default (seconds) |
|-----------|------------|---------|-------------------|
| `Common` | `RESPAWN_DELAY_COMMON_TICKS` | 60 | 3s |
| `Uncommon` | `RESPAWN_DELAY_UNCOMMON_TICKS` | 120 | 6s |
| `Rare` | `RESPAWN_DELAY_RARE_TICKS` | 300 | 15s |
| `Elite` | `RESPAWN_DELAY_ELITE_TICKS` | 600 | 30s |

The respawn countdown begins after transition 4 (`Dying → Respawning`), i.e., after `RemoveMob` is called. The slot is freed before the respawn timer starts.

---

### States and Transitions

| State | Description |
|-------|-------------|
| `Uninitialized` | Zone T-1 initialization complete; T-2 population sweep not yet run |
| `Live` | Mob is alive in zone; `CurrentEntityID` valid |
| `Dying` | HP=0; `MobDied` received; corpse linger countdown (`DEATH_LINGER_TICKS`) |
| `Respawning` | Corpse removed; respawn delay countdown (tier-based per CR-MS-9) |
| `Deferred` | Respawn ready but zone at `MAX_MOBS_PER_ZONE`; awaiting slot |
| `Cancelled` | Zone draining/closed; all timers discarded |

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Zone Instancing** | → calls | `TryAddMob(ZoneID, MobTypeID, Vector3) : EntityID` | Per spawn: T-2 fill, respawn, deferred recovery |
| **Zone Instancing** | → calls | `RemoveMob(ZoneID, EntityID)` | On corpse linger expiry (Dying → Respawning) |
| **Zone Instancing** | → calls | `GetMobCount(ZoneID) : int` | Before every `TryAddMob` |
| **Zone Instancing** | ← exposes | `CancelAllSpawnTimers(ZoneID)` | Called during zone teardown (CR-ZI-12 step 1) |
| **Enemy AI** | ← subscribes | `MobEventBus.MobDied(ZoneID, EntityID)` | On mob death — starts corpse linger |
| **Enemy AI** | → publishes | `MobEventBus.MobSpawned(ZoneID, EntityID, MobTypeID, Vector3)` | After successful `TryAddMob`; Enemy AI initializes `MobController` |
| **Enemy AI (IMobDefinitionRegistry)** | ← reads | `GetDefinition(MobTypeID) → MobDefinition` | T-2 fill and each respawn; reads `MobTier` for respawn delay, `MaxHP` for zone snapshot |

## Formulas

**F-MS-1: Effective Respawn Delay**

The respawn delay applied to a spawn point on the `Dying → Respawning` transition:

```
effectiveRespawnDelay =
  SpawnPoint.RespawnDelayOverrideTicks > 0
    ? SpawnPoint.RespawnDelayOverrideTicks
    : RespawnDelayTable[MobDefinition.MobTier]
```

Where `RespawnDelayTable` is keyed on `MobTier`:

| `MobTier` | Tuning Knob | Default (ticks) | Default (seconds at TICK_RATE_HZ = 20) |
|-----------|------------|-----------------|----------------------------------------|
| `Common` | `RESPAWN_DELAY_COMMON_TICKS` | 60 | 3.0s |
| `Uncommon` | `RESPAWN_DELAY_UNCOMMON_TICKS` | 120 | 6.0s |
| `Rare` | `RESPAWN_DELAY_RARE_TICKS` | 300 | 15.0s |
| `Elite` | `RESPAWN_DELAY_ELITE_TICKS` | 600 | 30.0s |

**Output range** (at default knob values): [60, `SpawnPoint.RespawnDelayOverrideTicks`]. The floor of 60 reflects `RESPAWN_DELAY_COMMON_TICKS` at its default; if that knob is tuned down, the floor adjusts accordingly. `RespawnDelayOverrideTicks = 0` is the sentinel for "use global table" and is never a valid effective delay value.

**Example**: Wild Bandicoot (`MobTier.Uncommon`), no override → `effectiveRespawnDelay = 120 ticks = 6.0s`.

---

**F-MS-2: SpawnPoint Cycle Time (Informational)**

The total time from mob spawn to next mob spawn at the same point, assuming the mob is killed immediately on becoming Live:

```
cycleTime = killTime + DEATH_LINGER_TICKS + effectiveRespawnDelay
```

**Variables:**

| Variable | Symbol | Type | Description |
|----------|--------|------|-------------|
| Time to kill the mob | `killTime` | float (seconds) | Combat duration; tier-dependent; not a Mob Spawning value — used here for calibration only |
| Corpse linger | `DEATH_LINGER_TICKS` | int | Owned by Enemy AI GDD (default 30 ticks / 1.5s) |
| Respawn delay | `effectiveRespawnDelay` | int | Per F-MS-1 |

**Output** (informational — for Level Design tuning, not evaluated at runtime):

| `MobTier` | Approx. kill time | Cycle time at defaults | Mob available fraction |
|-----------|------------------|----------------------|----------------------|
| `Common` | ~5s | 5 + 1.5 + 3 = **9.5s** | ~53% |
| `Uncommon` | ~10s | 10 + 1.5 + 6 = **17.5s** | ~57% |
| `Rare` | ~25s | 25 + 1.5 + 15 = **41.5s** | ~60% |
| `Elite` | ~45s | 45 + 1.5 + 30 = **76.5s** | ~59% |

**Notes:** "Mob available fraction" = `killTime / cycleTime`. Higher tiers have longer cycles but similar availability fractions — the depth gradient is primarily expressed through engagement time per mob, not spawn point downtime. Adjust `RESPAWN_DELAY_*_TICKS` to shift the balance: lower values increase density, higher values make each kill feel more significant.

## Edge Cases

**If `CancelAllSpawnTimers` fires while spawn points are in `Dying` or `Respawning` state:** All are transitioned immediately to `Cancelled`. `RemoveMob` is NOT called — Zone Instancing clears all mob slots during teardown (CR-ZI-12 step 7). No orphaned timers remain after `CancelAllSpawnTimers` returns.

**If all spawn points are deferred at T-2 (SpawnPointCount = MAX_MOBS_PER_ZONE and the cap is hit during initial fill):** All remaining uninitialized points enter `Deferred`. They fill via normal deferred polling (CR-MS-6). Expected behavior when the zone template has exactly 150 spawn points — the first points to pass the `GetMobCount < 150` check fill immediately; the remainder queue and fill as capacity permits. Not an error condition.

**If `TryAddMob` returns `EntityID.Invalid`:** `GetMobCount` is checked immediately before every `TryAddMob` call. On the single-threaded main loop, no concurrent mutation can occur between the check and the call — `TryAddMob` returning `EntityID.Invalid` after a passing check is not possible under normal conditions. As a defensive guard: if `EntityID.Invalid` is ever returned, the spawn point transitions to `Deferred` and logs `UnexpectedTryAddMobFailure` for diagnostics.

**If `MobEventBus.MobDied` is received for an EntityID with no matching spawn point:** The mob was spawned outside the normal `ZoneSpawnTable` (e.g., a scripted or event mob). Mob Spawning ignores the event — no state transition, no timer started. Expected for boss/event mobs managed independently of the spawn point system.

**If `MobDied` is received for a spawn point already in `Dying` state (duplicate event):** Ignored. `TimerTick` is not reset. The first event's timer runs to completion.

**If a spawn point's respawn timer fires while the zone is in `Draining` state:** Zone Instancing's `Draining` state disables new mob spawning. `TryAddMob` returns `EntityID.Invalid` regardless of `GetMobCount`. In correct teardown ordering, `CancelAllSpawnTimers` (CR-ZI-12 step 1) cancels all pending timers before any respawn can fire — this edge case should not be reachable. If it is: the `EntityID.Invalid` guard above handles it.

**If `RESPAWN_DELAY_*_TICKS` knobs are changed while timers are running:** Each value is read once per `Dying → Respawning` transition to commit `TimerTick`. Changing a knob mid-countdown has no effect on in-flight timers. New spawns after the change use the updated value. No retroactive adjustment is applied.

## Dependencies

**Upstream dependencies — systems Mob Spawning depends on:**

| System | GDD | Dependency Type | Interface | Hard/Soft |
|--------|-----|----------------|-----------|-----------|
| **Zone Instancing** | Approved | Calls | `TryAddMob(ZoneID, MobTypeID, Vector3) : EntityID`; `RemoveMob(ZoneID, EntityID)`; `GetMobCount(ZoneID) : int` | **Hard** — cannot spawn or remove mob entities without Zone Instancing slot management |
| **Enemy AI** | Approved | Reads + Events | `IMobDefinitionRegistry.GetDefinition(MobTypeID) → MobDefinition` (reads `MobTier`, `MaxHP`); subscribes to `MobEventBus.MobDied`; publishes `MobEventBus.MobSpawned` | **Hard** — MobDefinition schema and the MobEventBus are both owned by Enemy AI |

**Note on Item Database**: Listed as a dependency in `systems-index.md` but this GDD's design does not create a direct runtime dependency. Mob Spawning reads `MobDefinition` (owned by Enemy AI), not item records. Loot Table System reads Item Database independently when resolving drops. The systems-index entry should be reclassified as **indirect** (Mob Spawning → Enemy AI → Loot Table → Item Database). Update systems-index.md accordingly.

---

**Downstream dependents — systems that depend on Mob Spawning:**

| System | GDD | Dependency Type | What they need | Hard/Soft |
|--------|-----|----------------|----------------|-----------|
| **Enemy AI** | Approved | Consumes events | Subscribes to `MobEventBus.MobSpawned(ZoneID, EntityID, MobTypeID, Vector3)` to initialize `MobController` for each new mob entity; also relies on Mob Spawning to call `TryAddMob` before Enemy AI can manage any live mob | **Hard** — Enemy AI has no mob entities to manage without Mob Spawning |
| **Loot Table System** | Approved | Indirect | Mob Spawning establishes the `(EntityID ↔ MobTypeID)` binding used by Enemy AI when calling `ILootTableSystem.ResolveMobDrop(EntityID, tierShift)`. The Loot Table System reads `MobTypeID` from the zone's entity registry at drop resolution time | **Indirect** — mediated by Zone Instancing's entity registry and the Enemy AI kill event |
| **Zone Instancing** | Approved | Exposes interface | Must implement `CancelAllSpawnTimers(ZoneID)` call path (CR-ZI-12 step 1) — calls into Mob Spawning during zone teardown. Already specified in CR-ZI-13 | **Hard** — zone teardown cannot complete cleanly without Mob Spawning teardown |

---

**Bidirectionality notes:**
- `design/gdd/zone-instancing.md` — CR-ZI-13 already names Mob Spawning as an interface consumer. Enemy AI GDD update required: add `MobSpawned` event subscription and `IMobDefinitionRegistry` cross-reference to Mob Spawning. Loot Table GDD already lists Mob Spawning as a downstream dependent (row currently says "Not yet designed" — update to "Approved" when this GDD is approved). `systems-index.md`: reclassify Item Database dependency for Mob Spawning from Hard to Not Direct.

## Tuning Knobs

**Respawn Delays (per MobTier)**

| Knob | Default | Ticks | Safe Range | What Breaks |
|------|---------|-------|-----------|-------------|
| `RESPAWN_DELAY_COMMON_TICKS` | 60 | 3s | [20, 300] | Too low (→1s): mobs feel like a treadmill — killed and instantly reborn, no weight. Too high (→30s): near-town areas strip out; new players see empty fields. At 3s, a stripped 20-mob near-town area refills within ~6.5s even under peak player density. |
| `RESPAWN_DELAY_UNCOMMON_TICKS` | 120 | 6s | [60, 600] | Too low: Uncommon mobs feel as disposable as Common. Too high: mid-zone density thins. Must be > `RESPAWN_DELAY_COMMON_TICKS` to maintain the tier-rhythm gradient. |
| `RESPAWN_DELAY_RARE_TICKS` | 300 | 15s | [120, 1200] | Too low: collapses the rhythm distinction from Uncommon. Too high (→120s): a single killing of 10 Rare mobs creates a 2-minute dead zone that blocks grind flow. |
| `RESPAWN_DELAY_ELITE_TICKS` | 600 | 30s | [300, 3600] | Too low: Elite mobs lose the "this spawn point just got cleared" feeling. Too high (→5 min): solo players who clear an Elite feel they can't chain progress in that area. Default 30s is the sweet spot for single-player sessions; reduce if competitive group farming dominates. |

**Density and Deferred Queue**

| Knob | Default | Ticks | Safe Range | What Breaks |
|------|---------|-------|-----------|-------------|
| `SPAWN_DENSITY_RETRY_INTERVAL_TICKS` | 20 | 1s | [10, 200] | Too low (→1 tick): deferred list polled every tick — harmless at N=150 and O(1) per entry, but wasteful given mobs rarely free slots faster than once per second. Too high (→10s): a freed slot takes up to 10s to be claimed by a deferred spawn — visible "holes" in the field. |

**Referenced constants (owned by other systems — do not tune here):**

| Constant | Owner | Value | Relevance to Mob Spawning |
|----------|-------|-------|--------------------------|
| `MAX_MOBS_PER_ZONE` | Zone Instancing | 150 | Hard ceiling for all `TryAddMob` calls; defines deferred queue scope |
| `DEATH_LINGER_TICKS` | Enemy AI GDD | 30 (1.5s) | Corpse linger duration; used as `TimerTick` seed in `Dying` state |
| `TICK_RATE_HZ` | Networking Core | 20 | Tick-to-seconds conversion for all values above |

## Visual/Audio Requirements

**Mob Spawn (TryAddMob succeeds)**
When `MobSpawned` is published and Enemy AI initializes the entity, the client receives a `ZoneStateSnapshot` update (on zone join) or a `PlayerJoinedZone`-equivalent mob-entity broadcast on subsequent spawns. The visual: a brief materialize effect (shimmer/fade-in, ~0.5s) at the spawn position. Tier-differentiated: Common mobs — subtle; Elite mobs — more pronounced (larger burst). Sound: a short ambient noise matching the mob family (e.g., chittering for Bandicoots), fading into the mob's idle audio loop. No fanfare — spawn should feel natural, not alarming.

**Mob Corpse Despawn (Dying → Respawning transition / RemoveMob called)**
When `DEATH_LINGER_TICKS` expires and `RemoveMob` is called, the mob entity is removed from the zone. Visual: corpse fades out over ~0.5s. No special particle effect — the loot pickup and death animation (owned by Enemy AI/VFX) are the primary visual payoffs of the kill moment; the corpse disappearance should be quiet.

**Audio Director Handoff**
This section specifies trigger events and emotional intent only. Exact asset names, mix categories, priority levels, and occlusion rules are owned by the Audio Director.

## UI Requirements

No dedicated UI. Mob positions are visible on the minimap via `ZoneStateSnapshot` mob entity data (mob EntityStates included in snapshot per CR-ZI-8 step 6). Respawn timers are server-side state and are not displayed to players — the field repopulates naturally and the exact timing is intentionally opaque. No "spawn countdown" indicator exists or should be added.

## Acceptance Criteria

**AC-MS-1** [BLOCKING]
GIVEN a `ZoneSpawnTable` authored with `SpawnPointCount > MAX_MOBS_PER_ZONE` (e.g., 151 entries),
WHEN zone T-1 initialization attempts to load the template,
THEN initialization fails, the zone does not transition to Active, `SpawnTableOverCapacity` is logged, and no `TryAddMob` calls are made. A zone template with exactly 150 entries loads successfully.

**AC-MS-2** [BLOCKING]
GIVEN a zone with a `ZoneSpawnTable` of 10 spawn points and `GetMobCount = 0` at T-2,
WHEN the zone transitions to Active (T-2),
THEN `TryAddMob` is called exactly 10 times (once per spawn point) within the same server tick as T-2; all 10 spawn points transition to `Live`; `MobSpawned` is published 10 times. No spawn points remain in `Uninitialized` state after T-2 completes.

**AC-MS-3** [BLOCKING] [Integration — requires Zone Instancing stub at cap]
GIVEN a zone at `MAX_MOBS_PER_ZONE = 150` when T-2 fires (pre-populated from another source), and a `ZoneSpawnTable` with 5 entries,
WHEN T-2 population sweep runs,
THEN all 5 spawn points transition to `Deferred` without calling `TryAddMob`; they enter the deferred list. `GetMobCount < MAX_MOBS_PER_ZONE` on a subsequent retry tick causes one `TryAddMob` call and transitions one deferred point to `Live`.

**AC-MS-4** [BLOCKING]
GIVEN a spawn point in `Live` state with `CurrentEntityID = E`,
WHEN `MobEventBus.MobDied(ZoneID, E)` is received,
THEN the spawn point transitions to `Dying`; `TimerTick = DEATH_LINGER_TICKS`; `CurrentEntityID` remains `E`. `RemoveMob` is NOT called at this point.

**AC-MS-5** [BLOCKING] [Integration — requires Zone Instancing stub]
GIVEN a spawn point in `Dying` state with `TimerTick = 1`,
WHEN the tick loop decrements `TimerTick` to 0,
THEN `RemoveMob(ZoneID, E)` is called exactly once; `CurrentEntityID` is set to `EntityID.Invalid`; the spawn point transitions to `Respawning`; `TimerTick` is set to the effective respawn delay for the mob's `MobTier` (F-MS-1). No `TryAddMob` call is made at this transition.

**AC-MS-6** [BLOCKING] [Integration — requires Zone Instancing stub]
GIVEN a spawn point in `Respawning` state with `TimerTick = 1` and `GetMobCount < MAX_MOBS_PER_ZONE`,
WHEN `TimerTick` decrements to 0,
THEN `TryAddMob(ZoneID, MobTypeID, Position)` is called exactly once; `CurrentEntityID` is set to the returned `EntityID`; the spawn point transitions to `Live`; `MobEventBus.MobSpawned` is published with the new `EntityID`, `MobTypeID`, and `Position`.

**AC-MS-7** [BLOCKING] [Integration — requires Zone Instancing stub at cap]
GIVEN a spawn point in `Respawning` state with `TimerTick = 1` and `GetMobCount = MAX_MOBS_PER_ZONE`,
WHEN `TimerTick` decrements to 0,
THEN `TryAddMob` is NOT called; the spawn point transitions to `Deferred`; its `SpawnPointID` is added to the deferred list; `MobSpawned` is NOT published. `RemoveMob` was already called on the prior `Dying → Respawning` transition (AC-MS-5) — it is not called again here.

**AC-MS-8** [BLOCKING] [Integration — requires Zone Instancing stub]
GIVEN a spawn point in `Deferred` state and `SPAWN_DENSITY_RETRY_INTERVAL_TICKS = 20`,
WHEN 20 ticks elapse and `GetMobCount < MAX_MOBS_PER_ZONE`,
THEN `TryAddMob` is called; on success: the point transitions to `Live`, is swap-removed from the deferred list, and `MobSpawned` is published. If `GetMobCount = MAX_MOBS_PER_ZONE` at retry time, the point remains in `Deferred` and retries at the next interval.

**AC-MS-9** [BLOCKING]
GIVEN a zone with spawn points in states `Live`, `Dying`, `Respawning`, and `Deferred`,
WHEN `CancelAllSpawnTimers(ZoneID)` is called,
THEN all points transition to `Cancelled`; the deferred list is cleared; no `TryAddMob` or `RemoveMob` calls are made after the function returns; no `MobSpawned` events are published after the function returns. Verified via call log showing zero outbound calls post-cancellation.

**AC-MS-10** [BLOCKING]
GIVEN a `MobTier.Common` mob (no override) and `RESPAWN_DELAY_COMMON_TICKS = 60`,
WHEN the spawn point transitions from `Dying → Respawning`,
THEN `TimerTick` is set to exactly 60. Given a `MobTier.Elite` mob with `RESPAWN_DELAY_ELITE_TICKS = 600`: `TimerTick` is set to exactly 600.
Given the same spawn point with `RespawnDelayOverrideTicks = 200` (non-zero override): `TimerTick` is set to exactly 200, regardless of `MobTier`.

**AC-MS-11** [BLOCKING]
GIVEN `MobEventBus.MobDied(ZoneID, E)` received where `E` is not the `CurrentEntityID` of any spawn point in `Live` state,
WHEN the event is processed,
THEN no spawn point state changes; no timers are started; no `RemoveMob` call is made. Verified via state snapshot showing all spawn points unchanged.

**AC-MS-12** [BLOCKING]
GIVEN `MobEventBus.MobDied(ZoneID, E)` received for a spawn point already in `Dying` state,
WHEN the duplicate event arrives,
THEN `TimerTick` is NOT reset; the existing countdown continues unaffected. Verified via timer log showing single countdown expiry.

**AC-MS-13** [ADVISORY] [Integration — requires Zone Instancing stub]
GIVEN a `TryAddMob` call that returns `EntityID.Invalid` (simulated Zone Instancing failure),
WHEN Mob Spawning processes the result,
THEN the spawn point transitions to `Deferred` (not `Live`); `UnexpectedTryAddMobFailure` is logged; `MobSpawned` is NOT published.

**AC-MS-14** [ADVISORY] [Integration — requires zone lifecycle management]
GIVEN a spawn point in `Respawning` state when `CancelAllSpawnTimers` fires (Draining zone teardown),
WHEN the function runs,
THEN `RemoveMob` is NOT called for this point; the point transitions to `Cancelled`; Zone Instancing clears all mob slots independently (CR-ZI-12 step 7). Verified via call log showing no `RemoveMob` calls during teardown.

**BLOCKING: 12 | ADVISORY: 2 | Total: 14**

## Open Questions

**OQ-MS-1 — Boss/Event Spawn Mechanic (BLOCKING pre-implementation for any boss spawn point)**
Boss mobs use a `RespawnDelayOverrideTicks > 0` override in the `SpawnPoint` definition. However, the user confirmed bosses require "a completely different mechanic" and the inclusion of bosses in MVP is uncertain. Until this is resolved, boss-type spawn points must not be authored in the MVP starting zone's `ZoneSpawnTable`. The `RespawnDelayOverrideTicks` field is reserved and will carry the override when boss spawn design is finalized.

Requires decision: Are any boss/elite spawn points included in MVP? If yes, what is the boss spawn mechanic (single-instance, timer, trigger-based)? This decision must be made before the starting zone's `ZoneSpawnTable` is authored.

**Pre-Implementation Cross-System Update Checklist (advisory — complete before sprint)**
These are propagation items from this GDD that must be applied before the implementation sprint:

- [ ] `design/gdd/enemy-ai.md` — Add `MobTier: MobTier` field to `MobDefinition` schema; define `IMobDefinitionRegistry` interface; add `MobSpawned` event subscription note; add Mob Spawning as a downstream dependent
- [ ] `design/gdd/zone-instancing.md` — Add `IMobDefinitionRegistry` as a read-only dependency (Zone Instancing reads `MaxHP` for snapshot)
- [ ] `design/gdd/loot-table-system.md` — Update Mob Spawning dependency row from "Not yet designed" to "Approved"
- [ ] `design/registry/entities.yaml` — Add `design/gdd/mob-spawning.md` to `referenced_by` for `MobTypeID`; register `MobTier` enum, `RESPAWN_DELAY_*_TICKS` constants, `SPAWN_DENSITY_RETRY_INTERVAL_TICKS`
- [ ] `design/gdd/systems-index.md` — Reclassify Item Database dependency for Mob Spawning from Hard to Not Direct
