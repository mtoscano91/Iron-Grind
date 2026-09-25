# Enemy AI

> **Status**: In Review
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-25
> **Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

## Overview

Enemy AI is the server-authoritative system that governs all monster behavior in Iron Grind: proximity-based target selection, pursuit movement toward the selected target, melee attack initiation and resolution, and optional debuff application. It runs server-side only (`ServerLogic.asmdef`) and processes each mob entity on the server tick loop. The defining mechanic of Enemy AI is the two-phase melee attack: when a monster enters its attack wind-up, the AI immediately reads `EntityState.FacingAngle`, derives `capturedForward`, and holds that vector for the full wind-up duration. When execution fires, the frozen `capturedForward` is passed to `CheckHit` — not the live facing at that moment. A player who reads the wind-up animation has a fixed window to reposition: back out of range (`MissOutOfRange`) or move laterally past the angular tolerance (`MissFacing`). The window is the mechanic. Enemy AI also owns the 600ms wind-up minimum constraint established in Hit Detection — all MVP monster attack animations must respect this duration, enforced in the attack state machine. At spawn time, each mob rolls independently for an **Enraged** state: an enraged monster has increased MaxHP and AttackPower, awards more XP on death, and applies a drop quality bonus to its loot roll — same wind-up timing and aggro behavior, harder numbers and better rewards. Enraged is an engagement-quality spike: the elevated challenge rewards attentive players who commit to a harder fight for better returns. The evasion geometry (angular tolerance for MissFacing) is owned by Hit Detection GDD (`ATTACK_FACING_TOLERANCE_DEG` in CheckHit) — Enemy AI delivers the locked forward vector; Hit Detection determines whether the player is outside it. Movement uses a provisional `INavigationProvider` interface whose implementation will be specified by the Navigation/Pathfinding GDD. **Server architecture**: Enemy AI runs in a headless Unity process; GameObjects and NavMeshAgent components exist on the server. `EntityState` is a data struct alongside the full Unity scene graph. At MVP, all monsters are melee, single-target, and use simple proximity aggro; ranged attacks, multi-ability patterns, and boss phases are deferred to Vertical Slice.

## Player Fantasy

The monster doesn't cheat you. When it swings, it *tells* you first — the arm draws back, the body commits, and for a breath the outcome is in your hands, not the dice. A green player tanks the blow and curses the numbers. You see the tell, step past its locked guard, and feel the swing miss by inches you chose. Nothing here is hidden from you. The monster is honest about what it's going to do — the only question the game ever asks is whether you were paying attention.

Most of them die on schedule. But sometimes you pull one that bites back — hits harder, lasts longer, and you feel it in the first exchange before you understand why. The timing never lies, so you survive the surprise long enough to make the call: this one's worth more, and it's earned the right to make you work for it. A bot grinding the same spot would die to it without ever noticing the difference. You notice. You read the same tell, hold the same beat, and take the richer kill because you're awake enough to deserve it.

These monsters owe you nothing and give you nothing for free. A skilled warrior and a clumsy one fight the same mob on the same gear — the difference is visible to anyone watching, and that difference is the whole point. You don't beat these things. You out-work them.

*Pillar alignment: Rhythm Mastery (primary — the locked-facing tell is the mechanic; reading it is the skill expression), Earned Power (secondary — the Enraged mob rewards engagement; the better drop was genuinely worked for).*

## Detailed Design

### Core Rules

**CR-AI-1: Server execution domain**
Enemy AI executes exclusively within `ServerLogic.asmdef` (`defineConstraints: ["UNITY_SERVER"]`). Clients receive no AI state — mob positions, HP, and facing angles are replicated as EntityState data. All transitions are server-authoritative.

**CR-AI-2: Tick processing order**
Within each 50ms server tick, the order is: (1) Status Effects, (2) Enemy AI, (3) Auto-Attack Combat. Enemy AI reads Entity State after Status Effects have applied and writes damage results before Auto-Attack Combat runs.

**CR-AI-3: Aggro detection**
A Dormant mob scans for players within its `AggroRange` once per `AGGRO_SCAN_INTERVAL_TICKS`. The first player within `AggroRange` (XZ-plane squared-distance) at scan time becomes `TargetEntityID`. If multiple players qualify, the first encountered in entity list order wins. Aggro does not transfer once assigned.

**CR-AI-4: capturedForward locking**
On `WindingUp` entry, the server reads the mob's `EntityState.FacingAngle`, converts to XZ-plane Vector3 (0° = +X / `Vector3.right`, counterclockwise; converts from Unity's clockwise-from-+Z `transform.eulerAngles.y`, which is available in the headless Unity server process — see Overview), and stores it as `capturedForward`. This field is not updated again until the mob next enters `WindingUp`. On `AttackExecution`, `capturedForward` is passed to `CheckHit` unmodified.

**CR-AI-5: Facing update during pursuit**
While in `Pursuing`, the server updates the mob's `EntityState.FacingAngle` every tick to face the current target position (XZ-plane). `FacingAngle` is stored as a `float` in degrees (0–359.9°, per CR-HD-6 in Hit Detection GDD); the network serialisation layer converts to wire encoding independently. The angle locked by CR-AI-4 is therefore the mob's honest bearing toward its target when it committed to the swing.

**CR-AI-6: Attack cadence and cooldown start**
`AttackCooldownTicks` (per-mob field in `MobDefinition`) begins counting on the `AttackExecution` tick; the implementation sets `hasAttackedOnce = true` and records `lastAttackExecutionTick = currentTick`. In `Pursuing`, the transition to `WindingUp` requires both: target within `AttackRange` AND (`!hasAttackedOnce` OR `currentTick − lastAttackExecutionTick ≥ AttackCooldownTicks`). The `!hasAttackedOnce` guard replaces the `int.MinValue` sentinel, eliminating C# int32 overflow on servers with extended uptimes. **No-op case**: when `AttackRecoveryTicks ≥ AttackCooldownTicks`, the cooldown term in F-AI-1 equals 0 and `AttackCooldownTicks` does not affect effective attack rate; it remains a tuning knob for mob designs where Recovery is shorter. The effective minimum attack interval is `AttackWindUpTicks + AttackRecoveryTicks + max(0, AttackCooldownTicks − AttackRecoveryTicks)`.

**CR-AI-7: Wind-up minimum enforcement**
At server startup, assert for every loaded `MobDefinition`: `AttackWindUpTicks ≥ MIN_WIND_UP_TICKS`, where `MIN_WIND_UP_TICKS = Mathf.CeilToInt(0.6f × TICK_RATE_HZ)`. Also assert: `MoveSpeed > 0f`, `AggroRange > 0f`, `LeashRange ≥ AggroRange + 2.0f` (minimum gap prevents immediate leash-return at aggro boundary). Violation on any assertion: log error, refuse to spawn that mob type. On any write to `EntityState.FacingAngle`, validate the value is in [0f, 360f); clamp and log an error if out of range.

**CR-AI-8: Natural wind-up desync**
No jitter or stagger is applied on simultaneous `WindingUp` entry. Mobs naturally reach attack range at different ticks due to varied approach distances and player movement. No additional rule is required.

**CR-AI-9: Leash enforcement**
Each tick, mobs in `Pursuing`, `WindingUp`, `AttackExecution`, `Recovering`, and `Returning` check whether their XZ-plane distance from spawn point exceeds `LeashRange`. On breach: if in `WindingUp` or `AttackExecution`, set `pendingReturn` flag — transition to `Returning` occurs after the attack cycle resolves. In all other states, transition to `Returning` is immediate.

**CR-AI-10: Target invalidation**
Each tick, `TargetEntityID` is validated. Invalidation cases and responses:

**HP ≤ 0 (target killed):** Immediately re-scan for any player within `AggroRange` (same algorithm as CR-AI-3, excluding entities with HP ≤ 0). If a new target is found: assign as `TargetEntityID` and continue in the current state without interruption. If no player is within `AggroRange`: clear `TargetEntityID` and apply the standard invalidation response below. In `WindingUp` or `AttackExecution` when the target dies: set `pendingReturn`; complete the attack cycle; apply the re-scan-or-return logic on `Recovering` exit (not immediately).

**Disconnected or target outside mob's `LeashRange` from spawn (standard invalidation):**
- In `Dormant`, `Pursuing`, `Recovering`, `Returning`: transition to `Returning` immediately; clear `TargetEntityID`.
- In `WindingUp` or `AttackExecution`: set `pendingReturn` flag; complete the attack cycle; transition to `Returning` when it ends. On `AttackExecution` with an invalid target, `CheckHit` is skipped.

**CR-AI-11: Navigation delegation**
All movement delegates to `INavigationProvider` (provisional). Before any navigation call, verify the agent is on the NavMesh (`NavMeshAgent.isOnNavMesh`); if not, skip the call and log a warning. In `Pursuing`, `SetDestination(EntityID, Vector3)` is called only when the target moves more than `PURSUIT_REPOSITION_THRESHOLD` from the last issued destination — not every tick. `IsPathStale(EntityID)` is polled each tick in `Pursuing`; if stale, `SetDestination` is refreshed regardless of threshold. `IsPathStale` must be O(1) — it may not trigger path recalculation. On `WindingUp` entry, `Stop(EntityID)` is called; the implementation must halt movement without clearing path data (so re-entry into `Pursuing` can resume without a new `SetDestination` call). On `Returning` entry, `SetDestination(spawnPoint)` is called once. Dormant mobs have `NavMeshAgent` disabled.

**CR-AI-12: Enraged spawn roll**
At spawn, roll `_rng.NextDouble() < EnragedChance` using an injected `IRandomProvider` instance (`_rng`) — not `UnityEngine.Random.value` (thread-unsafe) and not a `System.Random` instantiated inline (untestable without injection). `MobController` accepts `IRandomProvider` at construction. Minimum interface:

```csharp
interface IRandomProvider {
    double NextDouble(); // returns a value in [0.0, 1.0)
}
```

Production implementation wraps `new System.Random()` seeded at mob spawn. Test implementations return controlled values (see AC-AI-13). On success: call `SetBaseStat(EntityID, StatType.MaxHP, BaseMaxHP × EnragedMaxHPMultiplier)` and `SetBaseStat(EntityID, StatType.AttackPower, BaseAttackPower × EnragedAttackPowerMultiplier)`. Set `IsEnraged = true`; set current HP to the new MaxHP. `SetBaseStat` is used — not `AddBuffModifier` — because Enraged is a permanent spawn variant, not a timed buff.

**CR-AI-13: Dead check at tick start**
At the start of each mob's AI tick, if HP ≤ 0 and the mob is not already in `Dead` state, transition to `Dead` immediately and skip all further processing for that tick. This ensures the global HP ≤ 0 → `Dead` override is enforced before any state-specific logic runs, regardless of which system decremented HP.

**CR-AI-14: pendingReturn lifecycle**
`pendingReturn` is initialised to `false` at mob spawn. It is set to `true` when a leash breach or target invalidation occurs during `WindingUp` or `AttackExecution`. It is cleared to `false` on `Dormant` entry. It is not cleared by `Returning` entry alone — the `Dormant` entry invariant clears it as part of resetting mob state.

### States and Transitions

| State | Entry | Per-Tick Behaviour | Transitions Out |
|---|---|---|---|
| **Dormant** | Spawn default; arrive at spawn from `Returning` | Scan players every `AGGRO_SCAN_INTERVAL_TICKS`; `NavMeshAgent` disabled | → `Pursuing`: player within `AggroRange` detected |
| **Pursuing** | Target assigned; exit `Recovering` with valid target outside `AttackRange` | Update `FacingAngle` toward target each tick; call `SetDestination` if target moved > `PURSUIT_REPOSITION_THRESHOLD` or path stale; check leash and target validity | → `WindingUp`: target within `AttackRange` (XZ) AND cooldown elapsed |
| | | | → `Returning`: target invalid OR leash exceeded |
| **WindingUp** | Enter from `Pursuing` when attack condition met; set `windUpTicksRemaining = AttackWindUpTicks`; lock `capturedForward` (CR-AI-4) | Decrement `windUpTicksRemaining`; do NOT update `FacingAngle`; `Stop` already issued on entry; check leash/target (set `pendingReturn` if breached — do NOT exit yet) | → `AttackExecution`: `windUpTicksRemaining == 0` |
| **AttackExecution** | `windUpTicksRemaining == 0` (single-tick state) | Call `CheckHit(capturedForward)` if target valid; set `hasAttackedOnce = true`, `lastAttackExecutionTick = currentTick`; apply damage if `HitResult.Hit`; note `pendingReturn` flag for Recovering exit | → `Recovering`: always (enter with `recoveryTicksRemaining = AttackRecoveryTicks`) |
| **Recovering** | After `AttackExecution` | Each tick: decrement `recoveryTicksRemaining`, then immediately check — if remaining ≤ 0 the exit transition fires within that same tick (see EC-AI-8 for the Recovery=0 case) | → `WindingUp`: remaining ≤ 0 AND target valid AND within `AttackRange` AND cooldown elapsed (CR-AI-6) |
| | | | → `Pursuing`: remaining ≤ 0 AND target valid AND outside `AttackRange` |
| | | | → `Returning`: remaining ≤ 0 AND (target invalid OR `pendingReturn` was set); apply re-scan logic from CR-AI-10 if target died |
| **Returning** | Target invalid; leash exceeded; `Recovering` exits with `pendingReturn` | `SetDestination(spawnPoint)` on entry; clear `TargetEntityID` | → `Dormant`: arrived within `RETURN_ARRIVAL_THRESHOLD` of spawn |
| **Dead** | HP ≤ 0 (overrides any state) | Call `ILootTableSystem.ResolveMobDrop(mobEntityID, IsEnraged ? EnragedLootQualityBonusTier : 0)`; raise `MobEventBus.MobDied(ZoneID, EntityID)` (loot first, spawn lifecycle second — CR-MS-5); hold `DEATH_LINGER_TICKS`; despawn. **XP is NOT awarded here** — it was already awarded by the killer's controller before `ApplyDamage` triggered this transition (Damage Calculation Option B / Leveling System CR-1.1 — see leveling-system.md OQ-LS-7 resolution, 2026-09-24) | → *(despawned)* |

**Global override**: Any state → `Dead` when HP drops to ≤ 0.

### Interactions with Other Systems

**Hit Detection**
On `AttackExecution`: calls `CheckHit(attackerID: mob EntityID, defenderID: TargetEntityID, capturedForward, attackPower: mob's current AttackPower)`. Receives `HitCheckResult` with `HitResult`. If `HitResult.Hit`: writes damage to target's `EntityState.HP`. The mob entity has no client — `HitCheckResult` is routed to the defending player's client per CR-HD-10.

**Entity State**
Reads: `Position` (every tick in `Pursuing`, at scan in `Dormant`); `FacingAngle` (at `WindingUp` entry); target `HP` (for invalidation check). Writes: mob `FacingAngle` (every tick in `Pursuing`); target `HP` (on hit).

**INavigationProvider (confirmed — navigation-pathfinding.md Approved 2026-06-14)**
Implementation confirmed in `navigation-pathfinding.md`. Full interface consumed by Enemy AI (per CR-NAV-7/8/9/10/11/16 and ADR-003 Decision 1):
```csharp
void SetDestination(EntityID agent, Vector3 target);
void SetSpeed(EntityID agent, float speed);
void Stop(EntityID agent);
void Resume(EntityID agent);      // CR-NAV-16: Pursuing re-entry after WindingUp/Recovering
bool IsPathStale(EntityID agent);
Vector3 GetCurrentVelocity(EntityID agent);
```
Enemy AI usage contract (ADR-003 Decision 1): on `Pursuing` re-entry after `WindingUp`/`Recovering`, call `Resume(entityId)` first, then poll `IsPathStale()`. If stale, follow up with `SetDestination`. Do NOT call `Resume` on `Dormant → Pursuing` re-aggro — Dormant mobs require a fresh `SetDestination`.

**Loot Table System**
On `Dead` entry, Enemy AI calls `ILootTableSystem.ResolveMobDrop(EntityID mobEntityID, int tierShift)` where `tierShift = IsEnraged ? EnragedLootQualityBonusTier : 0`. The Loot Table System uses the mob's damage record to resolve tag ownership, then runs its drop roll and delivers items and gold. Minimum interface consumed by Enemy AI:

```csharp
void ResolveMobDrop(EntityID mobEntityID, int tierShift); // tierShift ∈ [0, 2]
```

Caller-side API contract is now defined (OQ-AI-1 partially resolved). The Loot Table GDD must still author CR-LT-N exposing this entry point — the amendment must add the tier-shift parameter to its drop resolution path. Note: CR-LT-5 is already taken by Drop Tier Classification; the amendment must use the next available rule number. Tracking: OQ-AI-1.

**Stat System**
At spawn only: `SetBaseStat(MaxHP)` and `SetBaseStat(AttackPower)` for Enraged variants. No ongoing Stat System calls during combat — all base values are read from `MobDefinition` at spawn time.

**Character Persistence**
No direct coupling — Enemy AI makes no calls to Character Persistence. *(Correction, 2026-09-24, leveling-system.md OQ-LS-7 resolution: this subsection previously claimed Enemy AI awards XP directly via `CharacterPersistence.AwardXP(killerEntityID, IsEnraged ? EnragedKillXP : KillXP)` and delivers loot via a "Character Persistence write path" — both were stale and incorrect, and the XP claim specifically would have double-awarded XP alongside the killer's controller's own CR-1.1 sequence. XP is awarded by the killer's controller (Auto-Attack Combat / Skill System) via `LevelingSystem.GetXPAward` + `AddExperience`, per `damage-calculation.md`'s Option B — reading the same `KillXP`/`EnragedKillXP` fields from this class's own `MobDefinition`, which Enemy AI already exposes read-only via `IMobDefinitionRegistry`. Loot delivery is via the Loot Table System — see the dedicated subsection above, unaffected by this correction.)*

**Mob Spawning (MobEventBus)**
Enemy AI and Mob Spawning interact exclusively through C# events on a per-zone-instance `MobEventBus`. Neither system holds a direct reference to the other.

- **Subscribes to** `MobEventBus.MobSpawned(ZoneID, EntityID, MobTypeID, Vector3)` — raised by Mob Spawning after each successful `TryAddMob`. On receipt, Enemy AI instantiates and initializes a `MobController` for the new entity, including the Enraged spawn roll (CR-AI-12).
- **Raises** `MobEventBus.MobDied(ZoneID, EntityID)` — published on `Dead` state entry, after `ILootTableSystem.ResolveMobDrop` completes (loot first, spawn lifecycle second — CR-MS-5). Mob Spawning subscribes to this event to trigger corpse-linger and respawn timers.

`MobEventBus` lifetime is managed by Zone Instancing: created at T-1, injected into both systems, destroyed when the zone reaches `Closed` (see Mob Spawning CR-MS-5).

## Formulas

### F-AI-1: Effective Attack Interval

The time between two consecutive `AttackExecution` ticks for the same mob.

```
EffectiveAttackIntervalTicks = AttackWindUpTicks + AttackRecoveryTicks
                             + max(0, AttackCooldownTicks − AttackRecoveryTicks)
EffectiveAttackIntervalMs    = EffectiveAttackIntervalTicks × (1000 ÷ TICK_RATE_HZ)
```

| Variable | Type | Range | Source |
|---|---|---|---|
| `AttackWindUpTicks` | int | ≥ 12 (MIN_WIND_UP_TICKS) | MobDefinition |
| `AttackRecoveryTicks` | int | ≥ 0 | MobDefinition |
| `AttackCooldownTicks` | int | ≥ 10 (recommended minimum) | MobDefinition |
| `TICK_RATE_HZ` | int | 20 | Global constant |

**Output floor**: the structural minimum is `AttackWindUpTicks + AttackRecoveryTicks` (22 ticks / 1,100ms at MVP defaults), reached when `AttackCooldownTicks ≤ AttackRecoveryTicks`. Below this floor the formula does not go — `max(0, …)` guarantees it. Do not tune `AttackCooldownTicks` below `AttackRecoveryTicks` unless you intend the no-op case.

**No-op case**: when `AttackRecoveryTicks ≥ AttackCooldownTicks`, the cooldown term equals 0. `AttackCooldownTicks` has no effect on attack rate in this configuration but remains a tuning knob for designs where Recovery is shorter.

Example (defaults: WindUp=12, Recovery=6, Cooldown=10):
`12 + 6 + max(0, 10 − 6) = 22 ticks = 1,100ms`

Upper guidance: avoid `EffectiveAttackIntervalMs > 5,000ms` — intervals beyond 5s feel unresponsive rather than telegraphed.

### F-AI-2: Aggro Range Check

Used in Dormant scan to detect player entry.

```
sqrDistXZ = (mobX − playerX)² + (mobZ − playerZ)²
Aggroed   = sqrDistXZ ≤ AggroRange × AggroRange
```

XZ-plane only; Y component ignored.

### F-AI-3: capturedForward Conversion

Converts `EntityState.FacingAngle` (float, degrees 0–359.9°, per CR-HD-6) to XZ-plane Vector3 at `WindingUp` entry.

```
angleRad        = FacingAngle × (Mathf.PI / 180f)
capturedForward = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad))
```

Axis convention: 0° = +X (`Vector3.right`), 90° = +Z (`Vector3.forward`), counterclockwise in XZ-plane. Consistent with Hit Detection F-HD-2 and F-HD-3.

FacingAngle write during `Pursuing` each tick (converting from Unity's clockwise-from-+Z `transform.eulerAngles.y`):

```
ourAngleDeg = (90f − transform.eulerAngles.y + 360f) % 360f
FacingAngle = ourAngleDeg   // stored as float, 0–359.9°, per CR-HD-6
```

The network serialisation layer converts this float to the wire encoding (decidegrees short) when replicating `EntityState` to clients. Enemy AI does not perform that conversion — the serialisation layer owns it.

### F-AI-4: Leash Check

Used each tick in active states.

```
sqrDistToSpawn = (mobX − spawnX)² + (mobZ − spawnZ)²
LeashBreached  = sqrDistToSpawn > LeashRange × LeashRange
```

XZ-plane only.

### F-AI-E-1: Enraged Stat Scaling

Applied once at spawn if `_rng.NextDouble() < EnragedChance` (uses the injected `IRandomProvider` — see CR-AI-12).

```
EnragedMaxHP       = Mathf.RoundToInt(BaseMaxHP       × EnragedMaxHPMultiplier)
EnragedAttackPower = Mathf.RoundToInt(BaseAttackPower × EnragedAttackPowerMultiplier)
EnragedKillXP      = Mathf.RoundToInt(KillXP          × EnragedXPMultiplier)
```

| Variable | Default | Range |
|---|---|---|
| `EnragedMaxHPMultiplier` | 1.5 | [1.0, 3.0] |
| `EnragedAttackPowerMultiplier` | 1.3 | [1.0, 2.0] |
| `EnragedXPMultiplier` | 1.5 | [1.0, 3.0] |
| `EnragedLootQualityBonusTier` | 1 | [0, 2] |
| `EnragedChance` | 0.07 | [0.0, 1.0] |

Example (BaseMaxHP=100): `Mathf.RoundToInt(100 × 1.5) = 150`

### MobDefinition Schema

Authoring-time config class. One per mob type, loaded at server startup. Must be a `sealed class` (not a struct) — IL2CPP on Unity 6.3 does not support `string` fields in value types in all AOT configurations.

```csharp
sealed class MobDefinition {
    MobTypeID   MobTypeID;             // readonly struct wrapping uint; 0 = Invalid
    string      DisplayName;
    int         BaseMaxHP;             // > 0
    int         BaseAttackPower;       // > 0
    int         BaseDefense;           // ≥ 0
    float       AttackRange;           // ≥ ATTACK_RANGE_MIN (0.5m)
    float       MoveSpeed;             // > 0; m/s — asserted in CR-AI-7
    float       AggroRange;            // > 0; must satisfy LeashRange ≥ AggroRange + 2.0f — asserted in CR-AI-7
    float       LeashRange;            // ≥ AggroRange + 2.0f — asserted in CR-AI-7
    int         AttackWindUpTicks;     // ≥ MIN_WIND_UP_TICKS (12) — asserted in CR-AI-7
    int         AttackRecoveryTicks;   // ≥ 0
    int         AttackCooldownTicks;   // ≥ 10 (recommended minimum)
    int         KillXP;                // > 0
    int         GoldMin;               // ≥ 0
    int         GoldMax;               // ≥ GoldMin
    LootTableID LootTableRef;
    MobTier     MobTier;                 // {Common, Uncommon, Rare, Elite} — governs respawn delay tier (Mob Spawning CR-MS-9)
    float       EnragedChance;         // [0.0, 1.0]; 0.0 = never enraged; no NaN allowed
    float       EnragedMaxHPMultiplier;
    float       EnragedAttackPowerMultiplier;
    float       EnragedXPMultiplier;
    int         EnragedLootQualityBonusTier;  // [0, 2]
}
```

All `float` fields must be validated against NaN (`float.IsNaN`) at load time. NaN in any field causes server startup to refuse loading that mob type.

### IMobDefinitionRegistry Interface

Enemy AI owns and implements `IMobDefinitionRegistry`. Mob Spawning calls this interface to read mob type data without holding a direct reference to Enemy AI's implementation class.

```csharp
interface IMobDefinitionRegistry {
    MobDefinition GetDefinition(MobTypeID id); // Returns null for unknown ids; callers must null-check
}
```

Registered once at server startup and injected into Mob Spawning during zone initialization (T-1). Read-only from Mob Spawning's perspective — only Enemy AI may add or remove entries.

## Edge Cases

**EC-AI-1: Status effect kills mob before AttackExecution**
Tick order is: (1) Status Effects, (2) Enemy AI. If a status effect drives mob HP to ≤ 0 before Enemy AI processes that mob in the same tick, the mob transitions to `Dead` at the start of its Enemy AI processing. `AttackExecution` does not fire. The player who killed the mob during its wind-up avoids the attack entirely — this is intentional.

**EC-AI-2: Target disconnects during WindingUp or AttackExecution**
Target invalid flag is set (CR-AI-10). In `WindingUp` or `AttackExecution`: `pendingReturn` flag is set. On `AttackExecution` tick, Enemy AI checks target validity before calling `CheckHit`. If `TargetEntityID` no longer maps to a live entity: skip `CheckHit` call entirely, proceed to `Recovering` normally, then `Returning` via `pendingReturn`.

**EC-AI-3: Mob spawned within AttackRange of a player**
At spawn, mob starts `Dormant`. The first aggro scan fires within `AGGRO_SCAN_INTERVAL_TICKS`. If player is within `AggroRange` at scan time, mob enters `Pursuing`. If already within `AttackRange` at that point, the Pursuing-to-WindingUp condition can be satisfied on the same tick. The 600ms wind-up (CR-AI-7) is the guaranteed telegraph regardless of approach distance.

**EC-AI-4: `LeashRange ≤ AggroRange` in MobDefinition**
Asserted at server startup alongside CR-AI-7: if `LeashRange ≤ AggroRange`, log error and refuse to spawn that mob type. A mob with this configuration would leash-return the moment it aggroed any player within its aggro radius.

**EC-AI-5: Mob arrives at spawn point while already there (Returning → Dormant)**
`RETURN_ARRIVAL_THRESHOLD` is a squared-distance check (XZ-plane). If the mob never moved — or returned from a very short pursuit — the arrival check passes on the first `Returning` tick. The mob re-enters `Dormant` immediately. No special case needed; the check is distance-based and location-agnostic.

**EC-AI-6: No line-of-sight check at MVP**
Aggro detection (F-AI-2) is a pure distance check. Mobs can aggro through walls. This is accepted MVP behaviour — line-of-sight checks require raycasting against the navigation mesh, which depends on the Navigation/Pathfinding system (not yet designed). Deferred to Vertical Slice. Flagged as OQ-AI-2.

**EC-AI-7: Multiple mobs targeting the same player**
Each mob tracks its own `TargetEntityID` independently. No cap on how many mobs can target the same player. This is intentional design: mob piles reward parties with healers and punish solo players who pull too many. No special handling required.

**EC-AI-8: `AttackRecoveryTicks = 0` (immediate recovery)**
`recoveryTicksRemaining` initialises to `AttackRecoveryTicks` on `Recovering` entry. When `AttackRecoveryTicks = 0`, `recoveryTicksRemaining` starts at 0; the first tick's decrement yields −1, and the exit check (remaining ≤ 0) passes immediately. Valid configuration — the mob transitions to `Pursuing` or `WindingUp` on the tick after `AttackExecution`. The `AttackCooldownTicks` check in `Pursuing` governs actual re-attack timing regardless of recovery duration.

**EC-AI-9: Auto-Attack Combat processes player killed in same tick**
Tick order is Status Effects → Enemy AI → Auto-Attack Combat. If Enemy AI kills a player and that player's entry is still processed by Auto-Attack Combat in the same tick, Auto-Attack Combat must check target liveness before applying its own attack. This is Auto-Attack Combat's responsibility — Enemy AI does not gate or delay its own writes.

**EC-AI-10: FacingAngle tracks real target even in a party**
In a party scenario, a mob's `FacingAngle` reflects its actual bearing toward `TargetEntityID`, which may be one specific player while other party members are nearby. This is intentional — the mob honestly tracks its committed target; party members who are not targeted see the tell oriented away from them and can exploit that safely. Do not "fix" this to aim at the nearest party member.

**EC-AI-11: Enraged mob with empty `damageRecord`**
If an Enraged mob dies without any player dealing damage (e.g., a status-effect kill with no attacker, or a zone-reset), the Enraged loot quality bonus still applies to the drop roll. The loot tier shift is a property of the mob, not a reward gated on player participation. The `EnragedLootQualityBonusTier` is passed to the Loot Table System regardless of `damageRecord` state.

**EC-AI-12: `LeashRange − AggroRange < 2.0m`**
A mob whose `LeashRange` is within 2m of its `AggroRange` would leash-return immediately upon aggroing a player near the edge of the aggro radius. This is asserted at startup (CR-AI-7): `LeashRange ≥ AggroRange + 2.0f`. The 2m minimum gap is a geometry safety margin, not a design value — increase it if specific mob sizes require more clearance.

## Dependencies

### Upstream Dependencies

| System | What Enemy AI Needs | Status |
|---|---|---|
| **Hit Detection** | `CheckHit(attackerID, defenderID, capturedForward, attackPower)` on `AttackExecution`; owns `MIN_WIND_UP_TICKS = 12` | Approved |
| **Entity State** | Read: `Position`, `FacingAngle`, `HP` per entity. Write: mob `FacingAngle` each tick, target `HP` on hit | Approved (Networking Core) |
| **Character Persistence** | `AwardXP(killerEntityID, xpAmount)` on mob death | Approved |
| **Stat System** | `SetBaseStat(EntityID, StatType, value)` at spawn for Enraged variants. `StatType` is the Character Stats GDD's stat type enum — Enemy AI uses `StatType.MaxHP` and `StatType.AttackPower` | Approved |
| **Loot Table System** | Tier-shift drop trigger on mob death (exact API pending CR-LT-N amendment — see OQ-AI-1) | Approved |
| **Navigation/Pathfinding** | `INavigationProvider` (stub defined in CR-AI-11; implementation confirmed in navigation-pathfinding.md — CR-NAV-7/8/9/10/11/16; `Resume` added per ADR-003 Decision 1) | Approved |
| **Status Effects** | Processes before Enemy AI in tick order (CR-AI-2); Enemy AI reads post-effect HP | Approved |
| **Auto-Attack Combat** | Processes after Enemy AI in tick order (CR-AI-2); no direct interface call | Approved |

### Downstream Dependencies

| System | What It Needs From Enemy AI |
|---|---|
| **Navigation/Pathfinding** | Must implement the `INavigationProvider` stub interface defined in CR-AI-11 |
| **Loot Table System** | Must expose a tier-shift parameter in `LootTable.Roll` to accept `EnragedLootQualityBonusTier` (OQ-AI-1) |
| **Mob Spawning** | Calls `IMobDefinitionRegistry.GetDefinition(MobTypeID)` (reads `MobTier`, `MaxHP`); subscribes to `MobEventBus.MobDied` (raised by Enemy AI on `Dead` entry) |
| **Animation System** | Must produce attack wind-up animations ≥ `AttackWindUpTicks × 50ms` per mob type; enforced at startup by CR-AI-7 |
| **Audio System** | `WindingUp` entry and `Dead` entry are trigger points for mob audio cues |
| **VFX System** | `WindingUp` entry (swing tell), `AttackExecution` (impact), and `Dead` (death effect) are trigger points |

### Pre-Implementation Gate

All upstream dependencies are now Approved. Navigation/Pathfinding GDD Approved 2026-06-14 — `INavigationProvider` implementation is confirmed (CR-NAV-7/8/9/10/11/16). Mob `EntityID` allocation is owned by Zone Instancing (CR-ZI-13) and Mob Spawning (Approved 2026-06-11) — Enemy AI assumes EntityIDs are assigned and `MobEventBus.MobSpawned` is raised before `MobController.Initialize()` is called. No blocking pre-implementation dependencies remain.

## Tuning Knobs

### Global Server Constants

| Knob | Default | Safe Range | Gameplay Effect |
|---|---|---|---|
| `AGGRO_SCAN_INTERVAL_TICKS` | 5 (250ms) | [1, 10] | Lower = faster aggro response; higher = visible aggro "pop-in" delay. Below 3 ticks has no meaningful feel benefit and increases scan cost at mob scale. |
| `PURSUIT_REPOSITION_THRESHOLD` | 1.0m | [0.5, 3.0] | How far the target must move before `SetDestination` is re-issued. Lower = NavMesh calls per tick increase; higher = mobs visibly lag behind fast-moving targets. |
| `RETURN_ARRIVAL_THRESHOLD` | 0.5m | [0.25, 2.0] | How close to spawn point triggers `Dormant` re-entry. Too small risks float precision jitter. Too large means mobs reset visibly off their spawn mark. |
| `DEATH_LINGER_TICKS` | 30 (1,500ms) | [10, 100] | How long a dead mob body persists before despawning. Affects loot pickup window feel. |
| `ATTACK_RANGE_MIN` | 0.5m | fixed floor | Minimum `AttackRange` any MobDefinition can specify; enforced at startup. Do not tune — set by geometry constraints. |

### Per-Mob Parameters (in MobDefinition)

| Parameter | Guidance | Effect |
|---|---|---|
| `AttackWindUpTicks` | Min 12 (600ms); feel-test at 14–20 for harder mobs | Longer = more generous sidestep window; shorter mobs feel snappy and demanding |
| `AttackCooldownTicks` | Min 10 (500ms). Combined with F-AI-1, drives effective attack rate. No effect when `AttackRecoveryTicks ≥ AttackCooldownTicks` (see F-AI-1 no-op case) | Lower = faster mob tempo; raise this before raising HP for difficulty tuning |
| `AttackRecoveryTicks` | 0–20 typical. Longer = mob feels heavier and committed after a swing | Long recovery rewards aggressive play; short recovery keeps pressure on defenders |
| `AggroRange` | Must be > 0; must satisfy `LeashRange ≥ AggroRange + 2.0f` (asserted at startup) | Drives how much of the zone players navigate passively vs. actively |
| `LeashRange` | Must be ≥ `AggroRange + 2.0f` | Too tight = mob resets mid-pull (frustrating); too wide = mobs follow into adjacent zones |
| `MoveSpeed` | Must be > 0; tune slower than player sprint speed at MVP | Mobs should be catchable but require deliberate movement to escape |
| `EnragedChance` | Default 0.07 (~4 Enraged per 60-kill session) | Raise to increase density of harder encounters; 0.0 to disable per mob type. Above 0.15 the spike loses surprise value. |
| `EnragedMaxHPMultiplier` | Default 1.5; safe [1.1, 2.5] | Above 2.5, non-Enraged equivalents feel trivially easy by comparison |
| `EnragedAttackPowerMultiplier` | Default 1.3; safe [1.1, 2.0] | Primary signal to the player that the mob is different in the first exchange |
| `EnragedXPMultiplier` | Default 1.5; safe [1.1, 3.0] | Should feel like a meaningful reward; match with HP multiplier to keep XP-per-second roughly constant |
| `EnragedLootQualityBonusTier` | Default 1; safe [0, 2] | 0 = no loot bonus; 2 = two tier jumps up in loot quality |

## Visual/Audio Requirements

### Critical Visual Requirements

**V-AI-1: Wind-up tell animation (mandatory)**
Every mob's attack animation must visually commit for the full `AttackWindUpTicks × 50ms` duration. The animation must be frame-accurate to when `capturedForward` is locked (WindingUp entry). Animation artists must not shorten or loop-blend the wind-up — the visible tell IS the mechanic. Minimum 600ms, enforced at startup by CR-AI-7.

**V-AI-2: Enraged visual indicator (mandatory)**
Enraged mobs must be visually distinguishable from normal variants within the first second of encounter — before the player reads the first wind-up. Suggested approaches: persistent particle aura, red-tinted materials, or subtle scale increase (≤ 10% to avoid hitbox confusion). Final art direction is an Art Director decision; this GDD requires only that a clear indicator exists.

**V-AI-3: Facing lock during wind-up**
The mob's body must hold its locked orientation during `WindingUp`. No procedural look-at or IK should re-aim the mob during this phase. The frozen swing direction is the visual promise to the player.

### State Transition Cues

| Event | Required Cue |
|---|---|
| Dormant → Pursuing | Aggro audio + brief visual reaction (eyes, posture shift) |
| WindingUp entry | Wind-up animation start; attack charge audio begins |
| AttackExecution | Impact VFX on hit target (client-side, triggered by `HitCheckResult`); swing audio |
| Dead | Death animation + death audio; loot drop VFX at mob position |
| Enraged spawn | Distinctive audio pulse at spawn location; persistent aura activates |

### Audio Requirements

**A-AI-1**: Wind-up audio must begin at `WindingUp` entry and complete by `AttackExecution`. Duration must match `AttackWindUpTicks × 50ms`. No looping variants — the sound must track the mechanic duration.

**A-AI-2**: Enraged mobs require a distinct ambient audio cue (persistent, low-level) distinguishable from normal variants while the mob is alive.

## UI Requirements

Enemy AI has no direct HUD or screen UI.

**UI-AI-1**: Damage numbers and `Miss!` feedback are owned by Hit Detection (CR-HD-10) and delivered via `HitCheckResult` to the defending player's client. No additional UI output originates from Enemy AI.

**UI-AI-2**: Enraged status is communicated exclusively through in-world visual and audio cues (V-AI-2, A-AI-2). No health bar label, icon, or name-tag modifier is required at MVP. Polish-stage differentiation is deferred (see OQ-AI-3).

## Acceptance Criteria

**AC-AI-1: Dormant aggro scan**
Place a mob in `Dormant` state. Place a player at `AggroRange + 0.1m`. Wait `AGGRO_SCAN_INTERVAL_TICKS`. Assert mob remains `Dormant`. Move player to `AggroRange − 0.1m`. Wait `AGGRO_SCAN_INTERVAL_TICKS`. Assert mob transitions to `Pursuing` with `TargetEntityID` set to that player.

**AC-AI-2: First-player aggro (multi-player)**
Place two players within `AggroRange` simultaneously. Assert `TargetEntityID` is the player with the lower entity list index. Assert the other player is not targeted.

**AC-AI-3: Pursuing → WindingUp cooldown gate**
Set `hasAttackedOnce = true` and `lastAttackExecutionTick = currentTick`. Place mob within `AttackRange`. Assert mob does NOT enter `WindingUp` until `AttackCooldownTicks` ticks have elapsed. On tick `currentTick + AttackCooldownTicks`, assert mob enters `WindingUp`.

**AC-AI-4: capturedForward locked at WindingUp entry**
Record `EntityState.FacingAngle` at `WindingUp` entry (T=0). Rotate the mob's `FacingAngle` by 90° on tick T+1. On `AttackExecution`, assert `capturedForward` equals the Vector3 derived from the T=0 angle, not the T+1 angle. Assert `CheckHit` is called with the T=0 `capturedForward`.

**AC-AI-5: FacingAngle update rate by state**
In `Pursuing`: assert `EntityState.FacingAngle` is updated every tick to face target. On `WindingUp` entry: assert `EntityState.FacingAngle` does not change for the duration of wind-up. On `Recovering` entry: assert `EntityState.FacingAngle` does not change.

**AC-AI-6: AttackExecution is single-tick**
Enter `WindingUp` with `windUpTicksRemaining = 1`. Assert state is `AttackExecution` for exactly 1 tick. Assert state is `Recovering` on the immediately following tick.

**AC-AI-7: Wind-up minimum startup assertion (CR-AI-7)**
Load a `MobDefinition` with `AttackWindUpTicks = MIN_WIND_UP_TICKS − 1` (= 11 at 20Hz). Assert server logs an error and the mob type is refused at spawn. Load the same definition with `AttackWindUpTicks = MIN_WIND_UP_TICKS`. Assert spawn succeeds.

**AC-AI-8: Invalid MobDefinition startup assertion (EC-AI-4)**
Load a `MobDefinition` with `LeashRange = AggroRange`. Assert server refuses to spawn that mob type. Load with `LeashRange = AggroRange + 0.01f`. Assert spawn succeeds.

**AC-AI-9: Leash enforcement with pendingReturn**
Place mob in `WindingUp` at `windUpTicksRemaining = 3`. Move mob position to `LeashRange + 0.1m` from spawn. Assert mob does NOT exit `WindingUp`. Assert `pendingReturn` flag is set. Allow `windUpTicksRemaining` to reach 0. Assert `AttackExecution` fires. Assert mob transitions to `Recovering`, then to `Returning` (not `Pursuing` or `WindingUp`).

**AC-AI-10: Target invalidation with pendingReturn during WindingUp**
Place mob in `WindingUp` at `windUpTicksRemaining = 3`. Set target HP to 0. Assert mob does NOT exit `WindingUp`. Assert `pendingReturn` flag is set. On `AttackExecution` tick: assert `CheckHit` is NOT called (dead target). Assert mob transitions to `Recovering`, then to `Returning`.

**AC-AI-11: Returning → Dormant arrival**
Place mob in `Returning` state. Set spawn point position. Move mob to within `RETURN_ARRIVAL_THRESHOLD` of spawn point. Assert mob transitions to `Dormant`. Assert `TargetEntityID` is cleared.

**AC-AI-12a: Global override — mob death in WindingUp (mob HP → 0)**
Place mob in `WindingUp` at `windUpTicksRemaining = 3`. Set mob HP to 0. Assert mob transitions to `Dead` at the start of the next Enemy AI tick (CR-AI-13). Assert `AttackExecution` does NOT fire. Assert `CheckHit` is not called.

**AC-AI-12b: Target death during WindingUp — AttackExecution DOES fire**
Place mob in `WindingUp` at `windUpTicksRemaining = 3`. Set target HP to 0. Assert mob does NOT exit `WindingUp`. Assert `pendingReturn` flag is set. On `AttackExecution` tick: assert `CheckHit` is NOT called (dead target). Assert mob transitions to `Recovering`, then applies re-scan logic (CR-AI-10): if a new target is in `AggroRange`, assert `TargetEntityID` is updated; if none, assert mob transitions to `Returning`.

**AC-AI-13: Enraged spawn — chance boundaries**
Inject a test double `IRandomProvider` that returns 0.0 (always below threshold). Set `EnragedChance = 1.0`. Spawn mob. Assert `IsEnraged = true`. Inject a test double that returns 1.0 (always at or above threshold). Set `EnragedChance = 0.99`. Spawn mob. Assert `IsEnraged = false`. Inject a test double returning 0.0. Set `EnragedChance = 0.0`. Spawn mob. Assert `IsEnraged = false`.

**AC-AI-14: Enraged stat application (F-AI-E-1)**
Set `BaseMaxHP = 100`, `EnragedMaxHPMultiplier = 1.5`. Spawn Enraged mob (force `IsEnraged = true`). Assert `GetStat(MaxHP) == 150`. Assert `GetStat(AttackPower) == Mathf.RoundToInt(BaseAttackPower × EnragedAttackPowerMultiplier)`. Assert current HP equals the new MaxHP (not BaseMaxHP).

**AC-AI-15: Enraged uses SetBaseStat not AddBuffModifier**
Spawn Enraged mob. Assert buff modifier list contains no entry for MaxHP or AttackPower. Assert only `SetBaseStat` was called (verify via test double on Stat System).

**AC-AI-16: F-AI-3 capturedForward conversion accuracy**
Test four cardinal inputs (FacingAngle is now float degrees per CR-HD-6): FacingAngle = 0.0° → assert `capturedForward ≈ (1, 0, 0)`. FacingAngle = 90.0° → assert `≈ (0, 0, 1)`. FacingAngle = 180.0° → assert `≈ (−1, 0, 0)`. FacingAngle = 270.0° → assert `≈ (0, 0, −1)`. Tolerance: each component within `0.001f`.

**AC-AI-17: SetDestination call rate in Pursuing (CR-AI-11)**
In `Pursuing`, move target by `PURSUIT_REPOSITION_THRESHOLD − 0.01m`. Assert `INavigationProvider.SetDestination` is NOT called. Move target by `PURSUIT_REPOSITION_THRESHOLD + 0.01m` from last issued destination. Assert `SetDestination` IS called exactly once.

**AC-AI-18: Stop called on WindingUp entry (CR-AI-11)**
Transition mob from `Pursuing` to `WindingUp`. Assert `INavigationProvider.Stop` is called exactly once on the entry tick.

**AC-AI-19: Tick processing order — status effect pre-empts AttackExecution**
Arrange: mob in `WindingUp` at `windUpTicksRemaining = 1`. Schedule a status effect tick that sets mob HP to 0 in the same game tick. Assert: after Status Effects phase, mob HP is 0. Assert: in Enemy AI phase, mob transitions to `Dead` without executing `AttackExecution`. Assert: `CheckHit` is not called.

**AC-AI-20a: EffectiveAttackInterval formula check (F-AI-1)**
Set `AttackWindUpTicks = 12`, `AttackRecoveryTicks = 6`, `AttackCooldownTicks = 10`. Assert formula output = `12 + 6 + max(0, 10 − 6) = 22 ticks`. Also assert no-op case: set `AttackCooldownTicks = 4` (< Recovery=6). Assert formula output = `12 + 6 + max(0, 4 − 6) = 18 ticks` (cooldown term = 0).

**AC-AI-20b: EffectiveAttackInterval behavioral check**
Configure mob with `AttackWindUpTicks = 12`, `AttackRecoveryTicks = 6`, `AttackCooldownTicks = 10`. Record tick of first `AttackExecution`. Record tick of second `AttackExecution`. Assert `secondTick − firstTick == 22`.

**AC-AI-21: Assembly isolation — no client references in ServerLogic**
Static analysis: assert that `ServerLogic.asmdef` contains no references to client-only assemblies. Assert no type from a client-only namespace is used in any Enemy AI file.

**AC-AI-22: Empty aggro scan — mob stays Dormant**
Place mob in `Dormant`. Ensure no players exist within `AggroRange`. Wait `AGGRO_SCAN_INTERVAL_TICKS × 3` ticks. Assert mob remains in `Dormant`. Assert `TargetEntityID` is unset.

**AC-AI-23: Target disconnects during Pursuing — mob transitions to Returning**
Place mob in `Pursuing` with a valid target. Simulate target disconnect (remove entity from session). On the next tick, assert mob transitions to `Returning`. Assert `TargetEntityID` is cleared.

**AC-AI-24: IsPathStale true → SetDestination called regardless of threshold**
Place mob in `Pursuing`. Move target by less than `PURSUIT_REPOSITION_THRESHOLD`. Assert `SetDestination` is NOT called. Simulate `INavigationProvider.IsPathStale` returning `true`. Assert `SetDestination` IS called on the next tick regardless of target movement distance.

**AC-AI-25: First attack fires immediately — no overflow (CR-AI-6)**
Spawn mob with `hasAttackedOnce = false`. Place target within `AttackRange`. Assert mob transitions to `WindingUp` on the first eligible tick (no cooldown gate on first attack). Assert `lastAttackExecutionTick` is not `int.MinValue` at any point — it is set only after the first `AttackExecution`.

**AC-AI-26: OQ-AI-5 resolved — re-scan on target death in party (CR-AI-10)**
Place mob in `Pursuing` with TargetEntityID = Player A. Place Player B within `AggroRange`. Set Player A HP to 0. On the next tick, assert mob does NOT transition to `Returning`. Assert `TargetEntityID` is reassigned to Player B. Assert mob continues in `Pursuing` toward Player B.

## Open Questions

**OQ-AI-1: Loot Table System amendment (CR-LT-N) — partially resolved**
Enemy AI's caller-side API contract is now defined: `ILootTableSystem.ResolveMobDrop(EntityID mobEntityID, int tierShift)` where `tierShift ∈ [0, 2]`. The Loot Table GDD must still author CR-LT-N (next available number after the current approved set — **not CR-LT-5**, which is already taken by Drop Tier Classification) exposing this entry point and adding the tier-shift parameter to its drop resolution path. **Still blocking for Enraged loot reward feature.** Owner: Loot Table System GDD author.

**OQ-AI-2: Line-of-sight aggro check**
MVP aggro detection is a pure distance check (F-AI-2). Mobs aggro through walls (EC-AI-6). LOS requires navigation mesh raycasting from the Navigation/Pathfinding system. Deferred to Vertical Slice. When Navigation GDD is authored, this should be revisited.

**OQ-AI-3: Enraged UI differentiation**
Enraged status is communicated by in-world VFX/audio only at MVP (UI-AI-2). A name-tag modifier or health bar label may improve legibility in dense combat. Deferred to Polish. Requires UX spec if adopted.

**OQ-AI-4: Mob AI tick budget at target density (F-NET-6)**
Carried from Hit Detection review (prior session blocker #18). The server processes up to 150 mob entities per zone per tick. Enemy AI adds: one FacingAngle write per Pursuing mob, one `CheckHit` call per AttackExecution mob, and `AGGRO_SCAN_INTERVAL_TICKS`-rate scans for Dormant mobs. Total per-tick cost must be profiled against the 16.6ms server frame budget before enabling at target density. **Must be profiled before first zone load test.**

**OQ-AI-5: RESOLVED — Mob re-aggro after target death in party context**
**Decision (2026-05-25):** Option A adopted. On target death, mob immediately re-scans for any player within `AggroRange`. If found, assigns as new `TargetEntityID` and continues pursuit. If no player is in range, transitions to `Returning`. Rule added in CR-AI-10. AC-AI-26 covers the testable behavior.

**OQ-AI-6: AwardXP party distribution alignment**
`CharacterPersistence.AwardXP(killerEntityID, xpAmount)` passes a single killer entity. The Character Progression GDD formula F-PS-1 (if party XP sharing is defined there) may distribute XP differently from the single-killer model. If F-PS-1 uses a party-share distribution, Enemy AI must pass the full party's entity IDs rather than just `killerEntityID`. Needs resolution when the Character Progression GDD is authored. **Blocking for party XP accuracy.** Owner: Character Progression GDD author.
