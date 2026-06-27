# Class System

> **Status**: Approved (pass 1 — 2026-04-29)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-28 (Skill System amendment: SA-2 Step 5 null-conditional removed; interactions + dependencies tables updated to hard dependency; OQ-CS-1 resolved)
> **Implements Pillar**: Earned Power (primary), Social Gravity (secondary)

## Overview

The Class System defines the two playable archetypes for MVP — Warrior and Healer — and owns all infrastructure that bridges a player's class choice to the systems that read and write their character data. It serves two roles simultaneously. As an **allocator**: at player entity spawn, the Class System creates the `CharacterStats` container and initializes it with the class's starting attribute values and L1 derived stats — no other system allocates a player `CharacterStats` instance. As a **source of truth**: it stores each class's identity contract via a `ClassDefinition` data record, which holds the class archetype label (damage/tank or support), per-level auto-allocation increments (the values the Leveling System reads when executing CR-2.4), and a provisional skill-list reference (list of `SkillID`s the class may access — interface to be confirmed when the Skill System GDD is authored). Class selection happens once at character creation and is permanent; there is no class change or class respec in MVP. Mobs are not managed by the Class System — their `CharacterStats` instances are allocated by the Mob Spawning System from authored data tables.

## Player Fantasy

**The Role You Carry**

In Iron Grind, your class is not what you do alone — it is what you bring to a party. You choose once, at character creation, and you carry that choice permanently: there is no respec, no class change, no second attempt on this character. Warrior or Healer. You decide.

Warriors stand at the front. They absorb what the mob pack throws, hold the aggro so the group doesn't scatter, and hit hard enough that the zone clears before anyone runs out of MP. Whether a Warrior invests their free points into STR for raw damage or VIT for survivability is a build decision — but both builds walk into the pull first. That part is not optional.

Healers keep the party standing. Not by doing more damage — by making sure the damage the party takes never becomes fatal. The Healer who times a heal right keeps a party alive through a pull that should have killed them. Two free points per level build toward that: INT for stronger heals, or VIT to survive the pulls yourself. Both are valid. Neither is easy.

**The first time a stranger whispers "are you DPS or heals?" the answer is already locked.** You don't hedge. You don't say "well, technically..." — you say your class and they know what to expect from you, because the roles are real and the choice was permanent. This is the social contract class selection creates. It is also the fantasy: not "I am powerful," but "I am trusted with this role, and I chose to carry it."

*The permanence is the point. A player who joined a party with a Healer knows that Healer will be a Healer the next time they meet. The trust is structural.*

*Pillar alignment: Social Gravity (your class is the promise that makes parties function); Earned Power (the commitment to a role, made once and kept forever, is itself a cost — and a credential).*

## Detailed Design

### Core Rules

#### ClassDefinition Data Structure

**CD-1.** `ClassDefinition` is a `[Serializable]` struct held inside a `ClassRegistry` ScriptableObject (`assets/data/ClassRegistry.asset`). It is the authoritative data record for one playable class. Fields:

| Field | Type | Description |
|-------|------|-------------|
| `ClassType` | `enum ClassType : byte` | Unique class identifier. `Warrior = 0`, `Healer = 1`. Byte-backed for IL2CPP safety. |
| `DisplayName` | `string` | Human-readable class name shown in character creation UI. Not used for any game logic — identity logic always uses `ClassType`. |
| `ArchetypeRole` | `enum ArchetypeRole : byte` | `DamageTank = 0`, `Support = 1`. Read by Party System to surface role icon and validate composition. |
| `AutoAllocIncrement` | `AutoAllocIncrement` struct | Per-level attribute increments applied by the Leveling System at every CR-2.4 execution. See CD-2. |
| `FreePointsPerLevel` | `int` | Unallocated stat points granted each level-up, accumulated and tracked by the Leveling System. Warrior: 1. Healer: 2. |
| `SkillList` | `SkillID[]` | **Provisional.** List of skill identifiers this class may access. Type and interface confirmed by Skill System GDD. See CD-4. |

**CD-2 — AutoAllocIncrement struct.** A `[Serializable]` struct with four `int` fields representing per-level auto-allocation increments. Not a `Dictionary<StatID, int>` — Unity's serializer cannot serialize Dictionary without a custom wrapper (same trap as ValueTuple on ItemDefinition).

```
AutoAllocIncrement { int Str; int Dex; int Vit; int Int; public int Total() => Str + Dex + Vit + Int; }
```

`Total()` is used by CD-10 for sum-invariant validation. It is extension-proof: if a fifth field is added to the struct (e.g., `int Agi` for a future Agility stat), `Total()` must be updated to include it, and CD-10's validation automatically catches any `ClassDefinition` entry that does not satisfy the new sum.

MVP values (locked by Leveling System CR-2.4):

| Class | Str | Dex | Vit | Int | Auto Total | Free | Grand Total |
|-------|-----|-----|-----|-----|------------|------|-------------|
| Warrior | 2 | 1 | 1 | 0 | 4 | 1 | 5 |
| Healer | 0 | 0 | 1 | 2 | 3 | 2 | 5 |

**Startup invariant:** `Str + Dex + Vit + Int + FreePointsPerLevel == 5` for all classes at MVP. Validated by CD-10. This invariant may be revised when a third class is added.

**CD-3 — ClassType enum.** `enum ClassType : byte`. Consistent with `StatID : uint` and `BuffID : uint` patterns. Byte-backed because class count will remain small. **C# does not enforce switch exhaustiveness on enums** — adding a new `ClassType` value produces no compiler warning on non-exhaustive switches. Protection is achieved by adding `default: throw new ArgumentOutOfRangeException(nameof(classType), classType, "Unhandled ClassType")` to every switch statement over `ClassType`. This is a runtime exception on unrecognized values, not a compile-time warning. Every switch site must implement this pattern.

**CD-4 — SkillList (provisional).** `SkillList` field on `ClassDefinition` is stored as `uint[]` — raw skill identifiers. `SkillID` as a `readonly struct` is not `[Serializable]` by default; Unity's serializer silently produces an empty array for unrecognized struct types on ScriptableObjects, which would lose all authored skill data on device. Storing raw `uint[]` avoids this serialization failure. `GetClassSkills(ClassType)` on `IClassRegistry` wraps these into `IReadOnlyList<SkillID>` at call time by constructing `SkillID` from each `uint`. The Skill System GDD owns the final `SkillID` type definition and the exact interface for skill access. EC-CS-8 (null-safety) still applies: `GetClassSkills(classType)` returns `Array.Empty<SkillID>()` when the raw array is null or empty — callers never receive null.

**CD-5 — ClassRegistry ScriptableObject.** A `ClassRegistry : ScriptableObject` asset holds a single `[SerializeField] ClassDefinition[]` field. Adding a new class requires authoring a new array entry in the Inspector — no code changes. Consumed via `IClassRegistry` (see CD-6); the concrete asset is never referenced directly in game logic. Validated at startup (CD-10).

**Value-semantics note (CD-5):** `ClassDefinition` is a struct — all `IClassRegistry` methods return it by value (copy). Modifying the returned struct does not affect the registry. Callers must not attempt to mutate the returned `ClassDefinition`; mutations are silently discarded. This applies to `AutoAllocIncrement` as well (nested struct, also returned by copy).

**Unity 6.3 field requirement:** All fields in `ClassDefinition` and `AutoAllocIncrement` must be declared as `public` or `[SerializeField] private` **fields**, not properties. In Unity 6.3, `[SerializeField]` on a property is a compile error (breaking change from Unity 2022 and earlier). When OQ-CS-2 (adding a `Description string`) is resolved, declare it as a field.

**CD-6 — IClassRegistry interface.** Exposed by the Class System. All callers (Leveling System, Skill System, Party System, HUD) receive it via dependency injection:

```
TryGetClass(ClassType, out ClassDefinition) → bool
GetClass(ClassType) → ClassDefinition        // throws if not found
GetAllClassTypes() → IReadOnlyList<ClassType>
GetClassSkills(ClassType) → IReadOnlyList<SkillID>
```

**Caching requirement:** `ClassRegistry` (the concrete `ScriptableObject` implementation) must cache the `IReadOnlyList<ClassType>` result, computed once in `OnEnable()` and returned as the same reference on every `GetAllClassTypes()` call. Constructing a new collection on each call allocates heap memory; if called per-frame by the Party System or HUD, this produces GC pressure that breaks the 60fps mobile budget. All other `IClassRegistry` methods operate on the fixed-size `ClassDefinition[]` array and do not allocate.

---

#### Class Selection Rules

**CS-1 — Class selection is part of character creation, not entity spawn.** Character creation is a distinct one-time flow: the player selects a class on the character creation screen, confirms, and the client sends `CreateCharacter(accountId, characterName, ClassType)` to the server. The server validates the request, writes `ClassType` to the character's persistent record, and responds with `CharacterCreated(entityId)`. Class selection is complete when the server acknowledges the write. No `CharacterStats` instance exists yet; no stats are initialized yet.

**CS-2 — Class selection is permanent.** No in-game mechanic, item, or operation changes a character's `ClassType` after `CharacterCreated` is acknowledged. The character persistence schema does not include a mutation path for `ClassType` after initial write. If this constraint is ever relaxed post-MVP, a new ADR is required and this GDD must be revised.

**CS-2.1 — Account character limit.** A player account may create up to **3 characters**. Each character's class selection is independently permanent. Multi-character accounts do not dilute the social contract — each character carries its own class identity and server reputation. A player who wants to experience a different class creates a new character rather than changing an existing one. This limit is enforced server-side at character creation; the client presents a "slot full" message if all three slots are occupied.

**CS-3 — Stat respec does not interact with class selection.** The Leveling System's respec mechanic (CR-4) allows reallocation of free-point spends. Auto-allocated points are permanent. Class selection itself is not part of respec.

---

#### CharacterStats Allocation Sequence

**SA-1 — Trigger.** Zone Instancing raises `IEntitySpawnRequest(entityId, entityType, classType, persistenceRecord)` when an entity enters or reconnects to an instance. `IEntitySpawnRequest` includes an `EntityType` field (`enum EntityType : byte { Player, Mob }`). **The Class System checks `if (request.EntityType != EntityType.Player) return;` as its first operation** — mob spawn events on the same event bus are discarded immediately. Mob `CharacterStats` instances are allocated by the Mob Spawning System from authored data tables — the Class System does not participate.

**SA-2 — Allocation sequence (exact order, no reordering permitted):**

```
Step 1 — Allocate:
  stats = _statsFactory.Create(entityId)
  // _statsFactory: ICharacterStatsFactory — injected into Class System at construction.
  // All base slots initialized to schema defaults (not gameplay values).

Step 2 — Register:
  _statsRegistry.Register(entityId, stats)
  // Entity is now resolvable via ICharacterStatsProvider.GetStats(entityId).
  // Must complete before Step 4 — the Leveling System calls SetBaseStat()
  // via the registered instance.

Step 3 — Resolve ClassDefinition:
  bool found = _classRegistry.TryGetClass(classType, out ClassDefinition def)
  if (!found) → log error, abort spawn, notify Zone Instancing of failure.

Step 4 — Initialize or restore:
  if persistenceRecord.HasStats:
    // Restore path: Character Persistence writes all base stats.
    _persistence.RestoreStats(entityId, persistenceRecord)
    _levelingSystem.RestoreLevelingState(entityId, persistenceRecord.LevelingState)
    // Skip to Step 5 — no L1 init needed.
  else:
    // Fresh spawn: Leveling System owns the full L1 write sequence.
    _levelingSystem.InitializeAtL1(entityId, classType)
    // InitializeAtL1 wraps all writes in BeginStatTransaction/EndStatTransaction
    // so OnStatChanged events fire only after the entity is fully initialized.
    // Sets Level=1, all primary attrs=10, derives F-3–F-9 with ×1.0,
    // sets CurrentHP=MaxHP, CurrentMP=MaxMP, heldFreePoints=0.

Step 5 — Notify Skill System:
  _skillSystem.OnEntitySpawned(entityId, classType)
  // Hard dependency — ISkillSystem declared by Skill System (Approved 2026-05-28).
  // Skill System pre-caches IsUnlocked flags for all 10 skill slots.

Step 6 — Broadcast spawn complete:
  _spawnBroadcaster.OnPlayerSpawned(entityId, classType)
  // Notifies Zone Instancing, HUD, Party System.
  // GetEffectiveStat() is safe to call on this entity from this point.
```

**SA-3 — Ownership invariant.** `CharacterStats` instances are created **only via `ICharacterStatsFactory`**. The `CharacterStats` constructor is `private`. `ICharacterStatsFactory` has a single method: `CharacterStats Create(EntityID entityId)`. The concrete factory (`CharacterStatsFactory`) is registered in the DI container and provided **only** to the Class System (for player entities) and Mob Spawning System (for mob entities). No other system holds an `ICharacterStatsFactory` reference or allocates a `CharacterStats` instance. This constraint is enforced by DI container configuration, not by access modifiers — it must be verified at code review before the Class System story is marked Done.

**SA-4 — Deregistration.** On `IEntityDespawnRequest(entityId)` from Zone Instancing, the Class System calls `_statsRegistry.Deregister(entityId)`. The Class System releases the reference; subsequent calls to `GetStats(entityId)` return null.

---

#### Data Contract with Leveling System

**LC-1 — Leveling System reads auto-alloc from ClassDefinition.** The Leveling System receives `IClassRegistry` via constructor injection. At CR-2.4, it calls `TryGetClass(classType, out def)` and reads `def.AutoAllocIncrement`. It writes one `SetBaseStat()` per non-zero field. This replaces hardcoded values in CR-2.4 — the Leveling System GDD must be updated when this GDD is approved.

**LC-2 — ClassType is cached by the Leveling System.** `InitializeAtL1(entityId, classType)` stores `classType` per entity in the Leveling System's internal state. It does not re-query the Class System at subsequent level-ups — it reads from its own cache.

**LC-3 — FreePointsPerLevel is read from ClassDefinition.** At CR-2.8, the Leveling System reads `def.FreePointsPerLevel`. The running total (`heldFreePoints`) is per-entity state owned by the Leveling System, not a `StatID`. `AllocateFreePoint(entityId, StatID)` is the spend API, owned by the Leveling System.

**LC-4 — Auto-alloc floor for respec reads from ClassDefinition.** CR-4.3 derives the respec floor as: `floor[stat] = 10 + (Level − 1) × def.AutoAllocIncrement[stat]`. This is data-driven, not hardcoded.

---

#### Startup Validation

**CD-10.** On initialization, the Class System validates the `ClassRegistry` asset:
- No duplicate `ClassType` values across entries.
- All `ClassType` values in the array are recognized enum members.
- For each entry: each field of `AutoAllocIncrement` (Str, Dex, Vit, Int) is individually within [0, 2] at MVP. The sum check is not a substitute for per-field bounds — a single field set to 5 satisfies `Total() + FreePointsPerLevel == 5` while violating the output range invariant of F-CS-1.
- For each entry: `autoAllocIncrement.Total() + FreePointsPerLevel == 5` (MVP invariant). Uses the `Total()` method on `AutoAllocIncrement` to remain correct when new fields are added to the struct.

In dev builds, validation failure halts the spawn system and logs a critical error. In release builds, an error is logged but the spawn system continues — a malformed class definition is a data-authoring error, not a runtime exception condition.

---

#### Extensibility Rule

**EX-1 — Adding a new class requires no code changes to the Class System or Leveling System.** Add a `ClassType` enum value, author a new `ClassDefinition` entry in the `ClassRegistry` Inspector, author the `SkillList`. All code paths that read `AutoAllocIncrement` and `FreePointsPerLevel` from `IClassRegistry` handle the new class automatically. **All `switch (classType)` statements must include `default: throw new ArgumentOutOfRangeException(nameof(classType), classType, "Unhandled ClassType")` — this is the runtime signal that a new `ClassType` value was not handled.** C# does not provide compile-time exhaustiveness warnings on enum switches; the `default` throw is the enforcement mechanism.

---

#### Pillar 3 Delivery at MVP

**Pillar 3 — Social Gravity** states: "Solo always viable. Party always better." The Class System's contribution to this pillar must be explicit.

**At MVP (two classes):** Social Gravity is delivered through two mechanisms:

1. **Healer support kit (Skill System binding contract):** The Healer class exists to keep the party alive. Its support value — HP buffs, physical and magical defense buffs, continual healing — makes Warrior combat duration and survivability measurably better in a party than solo. The Class System provides the identity (Healer = Support archetype); the Skill System delivers the concrete buff and healing skills that make Healer presence felt. See OQ-CS-1 for the binding contract.

2. **Content gating:** Party-only elite zones with superior drop tables are the primary Social Gravity driver at MVP. A player can solo everything in the standard zones; the premium content — elite mob density, better loot tables, faster XP — requires a party. The Class System contributes by ensuring parties have a clear role split (DamageTank + Support), which is the minimum viable composition for elite zone content.

**Post-MVP class vision (binding for all future GDDs):** The full class roster expands to four archetypes:
- **Warrior** — Tanking + frontline DPS. Aggro control, high physical defense.
- **Rogue** — High single-target DPS. Skills: physical attack power bonus (party buff), movement speed boost (utility escape/pursuit).
- **Mage** — High magic damage. Skills: mass elemental attacks, party teleport.
- **Healer / Priest** — Party sustain. Skills: HP restore, physical defense buff, magical defense buff, continual healing.

With four classes, party composition becomes a genuine decision (no rogue in the party means no physical attack buff; no mage means no teleport/AOE; no healer means no sustain). Social Gravity deepens at each class addition. **No GDD authored for Vertical Slice or beyond may define a class that conflicts with these four archetypes without a GDD revision to this section.**

**Healer solo viability (binding constraint):** A Healer with no party must be able to grind solo in their appropriate zone tier. This is not satisfied by the auto-attack system alone — Healer auto-attack AP scales from STR (Healer STR auto-alloc = 0). See OQ-CS-1: the Skill System GDD **must** deliver a Healer offensive skill (INT-scaled) that enables solo kill rates constituting viable grinding. Until OQ-CS-1 is resolved, Healer solo viability is a BLOCKING open question.

---

### States and Transitions

| State | Scope | Description | Entry | Exit |
|-------|-------|-------------|-------|------|
| **Idle** | System-level | Registry loaded, spawn system ready | Startup — CD-10 validation passed | `IEntitySpawnRequest` received |
| **Spawning** | Per-entity, transient | SA-2 Steps 1–6 executing. `GetEffectiveStat()` must not be called on this entity by external systems during this window. | `IEntitySpawnRequest` received | Step 6 (`OnPlayerSpawned`) fires |
| **Spawned** | Per-entity, persistent | `CharacterStats` registered and fully initialized. Class System holds no further per-entity mutable state. | Step 6 completes | `IEntityDespawnRequest` received |
| **Despawning** | Per-entity, transient | `_statsRegistry.Deregister(entityId)` executing. | `IEntityDespawnRequest` received | Deregistration complete |

After spawn is complete, the Class System is stateless per entity. `classType` per entity lives in the character's persistence record and the Leveling System's internal state cache.

---

### Interactions with Other Systems

| System | Relationship | Class System provides | Other system provides |
|--------|-------------|----------------------|----------------------|
| **Character Stats** | Class System → CS | Allocates `CharacterStats` via `new CharacterStats(entityId)` (SA-2 Step 1); registers and deregisters via `_statsRegistry` | `CharacterStats` constructor (internal); `ICharacterStatsProvider` for downstream readers |
| **Leveling System** | Bidirectional | Provides `IClassRegistry` so LS reads `AutoAllocIncrement` and `FreePointsPerLevel` per level-up. Calls `InitializeAtL1(entityId, classType)` at fresh spawn (SA-2 Step 4). | `InitializeAtL1()` API; `RestoreLevelingState()` API (load path) |
| **Skill System** | Class System → SS | Calls `_skillSystem.OnEntitySpawned(entityId, classType)` at SA-2 Step 5 (hard dependency). Provides `IClassRegistry.GetClassSkills(classType)`. | Interface confirmed: `ISkillSystem.OnEntitySpawned(EntityID, ClassType)` — declared by Skill System (Approved 2026-05-28). Null-conditional removed. |
| **Zone Instancing** | Zone → Class System | Handles `IEntitySpawnRequest` and `IEntityDespawnRequest` | Raises spawn/despawn events; receives `OnPlayerSpawned` broadcast |
| **Character Persistence** | CP → Class System (load path) | Reads `persistenceRecord.ClassType` to resolve `ClassDefinition` at SA-2 Step 3. Delegates `_persistence.RestoreStats()` at Step 4 load path. | `RestoreStats()` API; persistence record with `ClassType` and saved base stats |
| **Party System** | Downstream read-only | Provides `IClassRegistry.TryGetClass()` and `GetAllClassTypes()` for role icon display and composition validation | Reads `ArchetypeRole` from `ClassDefinition` |
| **HUD** | Downstream read-only | `OnPlayerSpawned` broadcast triggers HUD init. Provides `IClassRegistry` for role icon. | Subscribes to `OnPlayerSpawned`; reads `IClassRegistry` for display data |
| **Onboarding / Character Creation** | Onboarding → Class System | Provides `IClassRegistry.GetAllClassTypes()` and `DisplayName` for the class selection screen | Presents selection UI; sends `CreateCharacter(accountId, characterName, classType)` to server |

## Formulas

**F-CS-1 — Respec Floor per Primary Attribute**

The `FloorValue` formula is defined as:

`FloorValue(stat) = BASE_ATTR + (Level − 1) × AutoAllocIncrement[stat]`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Floor value | `FloorValue` | int | [10, 128] | Minimum value the attribute may be reduced to during a respec |
| Base attribute | `BASE_ATTR` | int constant | 10 | All primary attributes initialize to 10 at L1 (SA-2 Step 4 invariant) |
| Current level | `Level` | int | [1, 60] | Character's level at respec time; read via `GetBaseStat(StatID.Level)` |
| Auto-alloc increment | `AutoAllocIncrement[stat]` | int | [0, 2] | Per-level increment for this stat; read from `ClassDefinition.AutoAllocIncrement` in `IClassRegistry` |
| Target stat | `stat` | StatID | {STR, DEX, VIT, INT} | The primary attribute being floor-checked |

**Output Range:** [10, 128].
- Minimum: L1, any class, any stat → `10 + (1−1) × any = 10`.
- Maximum: L60 Warrior STR → `10 + (60−1) × 2 = 128`.
- Stats with `AutoAllocIncrement = 0` (Warrior INT, Healer STR/DEX) always floor at 10 regardless of level — every point in those stats was a free-point spend and is fully recoverable.

**Examples:**

| Case | Formula | Floor |
|------|---------|-------|
| L30 Warrior STR | `10 + 29 × 2` | 68 |
| L30 Healer INT | `10 + 29 × 2` | 68 |
| L30 Warrior INT (zero-increment) | `10 + 29 × 0` | 10 |
| L60 Warrior STR (max) | `10 + 59 × 2` | 128 |

**Usage:** The Leveling System reads this formula at CR-4.3 during respec. The Class System is the source of `AutoAllocIncrement` — the Leveling System reads it from `IClassRegistry`. Neither system hardcodes these values.

---

**L1 Spawn Values — Reference Table (not evaluated by Class System)**

Produced by the Leveling System executing F-3 through F-9 with all primary attributes = 10 and LevelTierMultiplier = ×1.0. The Class System guarantees these values are written during SA-2 Step 4 (`InitializeAtL1`) for every fresh player spawn.

| Stat | L1 Value |
|------|----------|
| STR / DEX / VIT / INT | 10 (each) |
| MaxHP | 400 |
| MaxMP | 220 |
| AttackPower | 30 |
| Defense | 20 |
| MagicDefense | 4 |
| CritChance | 0.05 |
| AttackSpeedMultiplier | 1.0 |

Both Warrior and Healer start at identical L1 values — class differentiation begins at L2 when auto-alloc first diverges. Source formulas: Leveling System GDD, Section D (F-3 through F-9).

*MagicDefense at L1 = `FloorToInt(INT × 0.4 × tier)` = `FloorToInt(10 × 0.4 × 1.0)` = **4**. The MagicDefense formula has no base constant — it is purely INT-driven, so even at INT=10 and tier ×1.0 the result is non-zero. Any test asserting MagicDefense=0 at L1 is incorrect.*

## Edge Cases

- **If `IEntitySpawnRequest` arrives for an `entityId` already in Spawned state (EC-CS-1):** The Class System rejects the request, logs a warning with the `entityId`, and takes no action. The existing `CharacterStats` registration is preserved. Zone Instancing receives no `OnPlayerSpawned` broadcast — absence of that broadcast is rejection.

- **If `IEntityDespawnRequest` arrives while the entity is in Spawning state (EC-CS-2):** The despawn is queued via a `bool _pendingDespawn` flag (not a queue data structure — the entity either needs despawning or it does not; duplicate events collapse to the same `true` state). Immediately after Step 6 (`OnPlayerSpawned`) fires, if `_pendingDespawn == true`, the despawn sequence executes. The spawn sequence is never interrupted mid-execution. An entity is either fully spawned or fully gone — never half-registered. **Interaction with EC-CS-10:** Any additional `IEntityDespawnRequest` events arriving after the first but before Step 6 also set `_pendingDespawn = true` (already true — no additional effect). After Step 6 fires and deregistration executes, any further `IEntityDespawnRequest` events hit the EC-CS-10 no-op path.

- **If `persistenceRecord.ClassType` is not a recognized `ClassType` enum value (EC-CS-3):** `TryGetClass()` returns false. Log critical error with the raw byte value, abort spawn, notify Zone Instancing of failure. Character Persistence must surface a recovery prompt. Initializing stats from a missing definition produces silent corruption — aborting is the only safe behavior.

- **If `BeginStatTransaction` is called while a transaction is already open during `InitializeAtL1` (EC-CS-4):** Dev builds throw `InvalidOperationException`. Release builds silently drop the second `Begin` call and continue with the already-open transaction. One `EndStatTransaction` closes the transaction regardless of dropped `Begin` calls.

- **If `GetBaseStat(stat)` returns a value below `FloorValue(stat)` at respec time — attribute below its own F-CS-1 floor (EC-CS-5):** Log critical error with entityId, stat, current value, and computed floor. Clamp displayed floor to the corrupted current value so the player is not locked out of respec. Write a background audit flag for Character Persistence to correct the record on next save. Preventing the player from playing is the wrong failure mode for a data error they did not cause.

- **If `ClassRegistry` asset is missing or fails to load at startup (EC-CS-6):** Spawn system halts entirely in both dev and release builds. A missing registry means no class can ever be resolved — accepting spawn requests would be meaningless. This is the one case where dev and release behavior converge on halt.

- **If `InitializeAtL1()` is called before SA-2 Step 2 completes — entity not yet registered in `_statsRegistry` (EC-CS-7):** Dev build: an assertion in `InitializeAtL1` verifies registration before proceeding; throws `InvalidOperationException` if not. This is a Class System implementation bug, not a runtime data error — it must be loud and fail fast.

- **If `ClassDefinition.SkillList` is null at runtime (Unity serializer produces null for an uninitialized array field) (EC-CS-8):** `GetClassSkills(classType)` returns `Array.Empty<SkillID>()` rather than null. All callers must not receive a null reference where a list is expected.

- **If a zero-increment stat (e.g., Warrior INT) holds many free-point spends at high level and the player reclaims all via respec (EC-CS-9):** Correct and intended behavior. `FloorValue = 10 + (Level − 1) × 0 = 10` regardless of level. The Leveling System must not add protection beyond the F-CS-1 floor. Zero-increment stats are entirely free-point territory — full recovery is the design promise.

- **If `_statsRegistry.Deregister(entityId)` is called for an entityId that is not registered — double-despawn or EC-CS-2 queue flush (EC-CS-10):** No-op with a warning log. Must not throw. Despawn events may arrive more than once due to network delivery; the registry being already empty is the desired post-condition.

## Dependencies

#### Upstream Dependencies (Class System depends on)

| System | Type | Data interface | Notes |
|--------|------|----------------|-------|
| **Character Stats** | Hard | `CharacterStats(entityId)` constructor (internal); `ICharacterStatsProvider` for registration/deregistration; `SetBaseStat()` / `GetBaseStat()` called via Leveling System during init | Class System is the allocator — cannot function without CharacterStats |
| **Leveling System** | Hard | `InitializeAtL1(entityId, classType)` at fresh spawn; `RestoreLevelingState(entityId, record)` at load path; Leveling System reads `IClassRegistry` from Class System (bidirectional) | Provides the full L1 initialization sequence; Class System provides the ClassDefinition data the Leveling System reads at every level-up |
| **Skill System** | Hard | `ISkillSystem.OnEntitySpawned(entityId, classType)` (hard — null-conditional removed); `IClassRegistry.GetClassSkills(classType)` exposed for Skill System to query | Interface confirmed by Skill System GDD (Approved 2026-05-28). |
| **Zone Instancing** | Hard | `IEntitySpawnRequest(entityId, classType, persistenceRecord)` — trigger for spawn; `IEntityDespawnRequest(entityId)` — trigger for deregistration | Zone Instancing drives the entity lifecycle; Class System responds |
| **Character Persistence** | Hard (load path only) | `RestoreStats(entityId, persistenceRecord)` at SA-2 Step 4 load path; persistence record provides `ClassType` (read at Step 3) | Fresh spawn path bypasses Character Persistence entirely |

#### Downstream Dependents (depend on Class System)

| System | What it needs | Interface consumed |
|--------|--------------|-------------------|
| **Party System** | `ArchetypeRole` per ClassType for composition display | `IClassRegistry.TryGetClass()` |
| **HUD** | Role icon on spawn; triggers HUD initialization | `OnPlayerSpawned` broadcast; `IClassRegistry` |
| **Onboarding / Beginner Zone** | Class list and display names for character creation screen | `IClassRegistry.GetAllClassTypes()`, `ClassDefinition.DisplayName` |
| **Mob Spawning System** | Shared assembly access to `CharacterStats` internal constructor | Assembly membership — not an IClassRegistry consumer; allocates mob stats independently |

#### Bidirectional Consistency Notes

- **Leveling System**: The Leveling System GDD (CR-2.4) currently hardcodes auto-alloc values — this must be updated to read from `IClassRegistry.TryGetClass()` when this GDD is approved. Both GDDs must agree on the interface.
- **Character Stats**: character-stats.md states "The Class System allocates a CharacterStats instance per entity at spawn" — consistent with SA-2 ✅.
- **Skill System**: Provisional. The Skill System GDD must reference this GDD in its `referenced_by` list when authored.

## Tuning Knobs

All tuning knobs are authored in the `ClassRegistry` ScriptableObject in the Unity Inspector — no code changes required to adjust them.

| Knob | Current Value | Safe Range | What breaks at extremes |
|------|--------------|------------|------------------------|
| **Warrior `AutoAllocIncrement.Str`** | 2 | [1, 3] | Below 1: Warrior AttackPower growth becomes too slow, the "damage identity" fantasy breaks. Above 3: Warrior outscales all content by midgame; Healer support value collapses. |
| **Warrior `AutoAllocIncrement.Dex`** | 1 | [0, 2] | Below 0: impossible. Above 2: Warrior CritChance and ASM become too high relative to Healer, undermining class-role distinction. Note: both Warrior builds (DPS and Tank) share identical DEX auto-alloc — changing this affects both simultaneously. |
| **Warrior `AutoAllocIncrement.Vit`** | 1 | [0, 2] | Below 0: impossible. Above 2: Tank builds become too durable, elite content becomes soloable, eliminating party incentive. |
| **Warrior `FreePointsPerLevel`** | 1 | [1, 2] | Below 1: Warrior build identity becomes fully rigid — at least one meaningful per-level decision is required. Above 2: Warrior becomes as flexible as Healer, the class-role distinction blurs. |
| **Healer `AutoAllocIncrement.Vit`** | 1 | [1, 2] | Below 1: Healer solo survivability breaks Pillar 3 (solo always viable). Above 2: Healer Tank becomes dominant off-meta at far lower opportunity cost than Warrior Tank. |
| **Healer `AutoAllocIncrement.Int`** | 2 | [1, 3] | Below 1: HealPower (INT-scaled, provisional) grows too slowly without heavy free-point investment. Above 3: Healer MagicDefense becomes so high that elemental damage builds are invalidated. |
| **Healer `FreePointsPerLevel`** | 2 | [1, 3] | Below 1: Healer loses the INT-vs-VIT per-level decision that defines its build identity. Above 3: stat totals can exceed design bounds at current sum invariant. |

⚠️ **Warrior build depth flag:** With 1 free point/level (59 at L60), the Tank-vs-DPS decision reduces to "invest in STR or VIT on top of an already-diverging auto-alloc trajectory." This is a shallow decision space. The choice is valid but not structurally differentiated — both builds are marginal amplifications of the same auto-alloc curve. Flagged for pre-Beta playtest re-evaluation. If stat screen testing reveals players feel the Warrior choice is meaningless, increase `FreePointsPerLevel` to 2 and reduce one auto-alloc increment by 1 to maintain the sum invariant (CD-10 must be updated accordingly).

**Invariant constraint:** `AutoAllocIncrement.Str + AutoAllocIncrement.Dex + AutoAllocIncrement.Vit + AutoAllocIncrement.Int + FreePointsPerLevel == 5` for all classes. Tuning any increment requires compensating another. Changing the total requires updating CD-10 and a GDD revision.

**Knob interactions:**
- Warrior STR and VIT increments interact — increasing one while decreasing the other shifts both build identities (Tank and DPS) simultaneously.
- Healer VIT and INT increments are amplified most at L60 (×2.0 tier multiplier) — late-game balance is more sensitive to these than early-game.
- Increasing `FreePointsPerLevel` without reducing auto-alloc increments requires revising the sum invariant in CD-10.

## Visual/Audio Requirements

> Scope note: The Class System is not a visual owner of character models, level-up effects, combat hit feedback, or enhancement glow — those systems own those assets. This section specifies what the Class System contributes directly: role icon design direction, the class confirmation moment, and HUD/party frame differentiation.

**Role Icons**

Two icons are required at MVP: one for `ArchetypeRole.DamageTank` (Warrior) and one for `ArchetypeRole.Support` (Healer). Both follow the art bible iconography standard (art-bible.md §7.3): outlined, 2px single-weight stroke, 44×44px canvas, 36×36px active area, no gradients, no interior fill in default state, flat tint only.

**Warrior role icon** (`ui_icon_role_damagetank_default.png`): A chamfered-rectangle shield face. The shield outline matches the UI chamfer language. A single horizontal bar occupies the center third of the shield interior. The shield is the primary class identifier per the art bible (§5.2).

**Healer role icon** (`ui_icon_role_support_default.png`): A centered plus sign (four equal arms, 2px stroke). At 32px — the minimum readability floor for party frame use — the plus sign reads unambiguously against Panel Dark (`#1A1C1F`) without any color cue. Passes colorblind safety by shape alone.

Both icons render in Primary Text (`#E8E6DF`) on Panel Dark. Enhancement Gold and Ascension White are not used for role icons — gold means "enhanced," white means "pinnacle state." Neither applies to a static role marker.

**Readability gate:** Both icons must be identifiable at 32×32px on `#1A1C1F` background on a physical 5-inch device.

---

**Class Confirmation Moment (Character Creation)**

Class selection is permanent. The confirmation feedback must communicate finality and weight — not triumph. Triumph is earned through enhancement. This is the beginning of the commitment.

**Sequence (triggered when player confirms class selection):**

1. Screen vignette increases from ambient to 0.4 opacity black overlay over 200ms (ease-in).
2. A single warm burst of Enhancement Gold (`#D4AF37`) radiates outward from the selected class card. Burst radius: card edge to 40px beyond card boundary. Duration: 400ms. Ease-out. Does not loop. Does not repeat.
3. The selected class card acquires a permanent thin chamfer border in Enhancement Gold at 60% opacity. This border persists until the character creation UI closes.
4. Vignette fades back to ambient over 200ms (ease-out).
5. Audio: `sfx_class_confirm_01.wav` plays at step 2 onset.

**Design constraints:**
- Must not use Ascension White — white is the pinnacle enhancement signal; a Level 1 class choice has not earned it.
- Must not use Threat Red or Vital Amber — red signals loss; amber signals HP state.
- Must not loop — looping devalues permanence (see art-bible.md §2.5: "Do not loop. Loop = devalued.").
- No fanfare, no multi-note sting. A single strike followed by silence. The silence is intentional.

This event is authored by the Onboarding System, which owns the character creation screen. This GDD specifies the intent and constraints; the Onboarding GDD owns the implementation details.

---

**Class Identity Markers on Character Model**

At MVP, the only class visual markers on the character model are those already defined in the art bible (§5.2):

| Class | Accent Region | Tint |
|---|---|---|
| Warrior | Shoulder armor inner face | Subdued Threat Red `#8B2A22` |
| Healer | Staff ornament or grip wrap | Vital Amber `#E8A020` at 0.6 opacity |

No additional markers, silhouette changes, or armor-type differentiation are introduced by this GDD. Deferred to Vertical Slice: additional cosmetic differentiation and class-linked visual variants.

---

**HUD and Party Frame Role Display**

**Player HUD (self):** Role icon at the left endcap of the player name/level row. Size: 16×16px visual within a 44pt touch-safe row. Color: Primary Text (`#E8E6DF`).

**Party frames (others):** Role icon at the left endcap of each pill-shaped party frame. Size: 16×16px visual. Color: Primary Text. Frame border remains Panel Border (`#3A3D44`) regardless of class — no class-coded border color. HP bar fill remains Vital Amber for all party members.

**Color coding rule:** Role is communicated by icon shape alone. No class-specific color is applied to frame backgrounds, borders, or HP bar fills. Amber = life state (not class). Red = threat or loss (not class affiliation).

Party frame role icon tap target: 24×24pt minimum. Display only at MVP — no interaction on role icon tap.

---

**Audio**

| Asset ID | Name | Event | Spec |
|---|---|---|---|
| CS-SFX-01 | `sfx_class_confirm_01.wav` | Class selection confirmed | Single resonant strike (hammer-on-anvil). Attack 20ms, sustain 300ms, decay 500ms. Mono. 44,100Hz / 16-bit. Non-randomized — plays identically every time. |
| CS-SFX-02 | *(UI system dependency)* | Class card hover/tap highlight | Generic UI button tap. Owned by UI audio pass. Reference only. |

No audio plays on role icon render. Party join audio is owned by the Party System.

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:class-system` to produce per-asset visual descriptions, dimensions, and generation prompts from this section.

## UI Requirements

The Class System has no standalone UI screens. It is a data provider — all player-facing class UI is owned by other systems, which query `IClassRegistry` for the data they display.

**Data provided to other systems' UIs:**

| UI Screen | Owner GDD | Data consumed from Class System |
|-----------|-----------|--------------------------------|
| Character creation / class selection | Onboarding GDD | `GetAllClassTypes()`, `ClassDefinition.DisplayName`, `ClassDefinition.ArchetypeRole`, `ClassDefinition.SkillList` (provisional) |
| Player HUD (role icon) | HUD GDD | `ArchetypeRole` via `TryGetClass()` on `OnPlayerSpawned` |
| Party frames (role icons) | HUD GDD | `ArchetypeRole` for each party member's `ClassType` |
| Respec / stat screen (floor display) | Leveling System GDD | F-CS-1 result — computed and enforced by Leveling System, displayed in the stat screen |

> **📌 UX Flag — Class System**: The character creation class selection screen has UI requirements (class cards, confirmation moment, permanent choice framing). In Phase 4 (Pre-Production), run `/ux-design` to create a UX spec for the character creation screen **before** writing epics. Stories that implement the character creation UI should cite `design/ux/character-creation.md`, not this GDD directly.

## Acceptance Criteria

*(BLOCKING = automated unit/integration test required before Done; ADVISORY = manual evidence or UI walkthrough)*

**Character Creation & Permanence**

- **AC-CS-1** *(BLOCKING — Integration)*: **GIVEN** a character creation request is submitted with `ClassType=Warrior` and the server write succeeds, **WHEN** the character record is queried from persistence, **THEN** the stored `ClassType` field equals `0` (Warrior) and no `CharacterStats` entry exists yet for this entity.

- **AC-CS-2** *(ADVISORY — Code-review gate)*: **GIVEN** the `IClassRegistry` and `CharacterStats` public API surfaces, **WHEN** a developer searches for a setter, mutation method, or write path for `ClassType` on an existing entity, **THEN** no such method or property exists — `ClassType` is read-only after initial character creation. *Verified by code review before the Class System story is marked Done. Optionally enforced by a reflection-based Logic unit test: `Assert.IsNull(typeof(CharacterStats).GetProperty("ClassType")?.SetMethod)`.* Reclassified ADVISORY because the enforcement mechanism is process-based (code review), not CI-automated; a BLOCKING classification requires an automated test.

- **AC-CS-3** *(BLOCKING — Integration)*: **GIVEN** a Warrior character reconnects after being previously disconnected, **WHEN** Character Persistence loads the character record, **THEN** `ClassType=Warrior(0)` is retrieved from storage and `CharacterStats` is initialized with the Warrior `ClassDefinition`.

**L1 Spawn Values**

- **AC-CS-4** *(BLOCKING — Integration)*: **GIVEN** a fresh Warrior character with no prior persistence record, **WHEN** `InitializeAtL1(entityId, ClassType.Warrior)` completes, **THEN** STR=10, DEX=10, VIT=10, INT=10, MaxHP=400, MaxMP=220, AttackPower=30, Defense=20, **MagicDefense=4**, CritChance=0.05, AttackSpeedMultiplier=1.0. *(MagicDefense=4 from `FloorToInt(10 × 0.4 × 1.0)` — not 0.)*

- **AC-CS-5** *(BLOCKING — Integration)*: **GIVEN** a fresh Healer character with no prior persistence record, **WHEN** `InitializeAtL1(entityId, ClassType.Healer)` completes, **THEN** all stat values equal those in AC-CS-4 — both classes are identical at L1.

**Auto-Alloc Divergence**

- **AC-CS-6** *(BLOCKING — Logic)*: **GIVEN** the Leveling System initialized with a mock `IClassRegistry` returning Warrior `AutoAllocIncrement(Str=X, Dex=Y, Vit=Z, Int=W)` for arbitrary values X/Y/Z/W, **WHEN** a Warrior `LevelUp()` is called once (advancing L1→L2), **THEN** STR = `10 + X`, DEX = `10 + Y`, VIT = `10 + Z`, INT = `10 + W` — confirming the Leveling System reads from the registry and does not hardcode values. The mock values (not the real asset) are set in the test. *(AC-CS-14 covers the same behavioral guarantee with a concrete example; AC-CS-6 should use a randomized X/Y/Z/W to prevent hardcoded-value false passes.)*

- **AC-CS-7** *(BLOCKING — Logic)*: Same as AC-CS-6 for Healer — mock `IClassRegistry` returns Healer `AutoAllocIncrement` with arbitrary values; test asserts the Leveling System applies them correctly.

**Respec Floor (F-CS-1)**

- **AC-CS-8** *(BLOCKING — Logic)*: **GIVEN** a Warrior at Level L with STR currently at `FloorValue(STR, L) + N` (N > 0 free-point spends above the floor), **WHEN** the Leveling System's `AllocateFreePoint(entityId, STR, targetValue)` is called with `targetValue < FloorValue(STR, L)`, **THEN** the call returns `RespecResult.BelowFloor` (or equivalent error code defined in the Leveling System GDD — *blocked on Leveling System GDD defining its error return contract explicitly*) AND `GetBaseStat(STR)` remains unchanged. *(This AC cannot be written as a runnable test until the Leveling System GDD defines what "rejected" means at the API level — return value, exception type, or error enum.)*

- **AC-CS-9** *(BLOCKING — Logic)*: **GIVEN** a Warrior at any level with INT spent above 10, **WHEN** the Leveling System respec reduces INT to 10 (the F-CS-1 floor for a zero-increment stat), **THEN** the respec succeeds and INT is set to 10 — no additional floor protection is applied beyond F-CS-1.

- **AC-CS-10** *(ADVISORY — UI)*: **GIVEN** a Warrior at L30 in the respec screen, **WHEN** the respec UI renders, **THEN** the STR slider minimum is `10 + (29 × ClassDefinition[Warrior].AutoAllocIncrement.Str)` and cannot be dragged below that value.

**Respec Does Not Change ClassType**

- **AC-CS-11** *(BLOCKING — Logic)*: **GIVEN** a Warrior at any level, **WHEN** the player completes a full stat respec, **THEN** `ClassType` remains `Warrior(0)` and `AutoAllocIncrement` values remain the Warrior `ClassDefinition` values throughout.

**Free-Point Accumulation**

- **AC-CS-12** *(BLOCKING — Logic)*: **GIVEN** a Warrior at L1 with 0 free points, **WHEN** the character levels to L5, **THEN** `LevelingSystem.GetHeldFreePoints(entityId) == 4` (`(5 − 1) × ClassDefinition[Warrior].FreePointsPerLevel`), AND STR=18, DEX=14, VIT=14, INT=10 — confirming auto-alloc ran correctly and free points were not silently merged into auto-alloc.

- **AC-CS-13** *(BLOCKING — Logic)*: **GIVEN** a Healer at L1 with 0 free points, **WHEN** the character levels to L5, **THEN** `LevelingSystem.GetHeldFreePoints(entityId) == 8` (`(5 − 1) × ClassDefinition[Healer].FreePointsPerLevel`), AND STR=10, DEX=10, VIT=14, INT=18 — confirming auto-alloc ran correctly and free points were not silently merged into auto-alloc.

**IClassRegistry Data Contract**

- **AC-CS-14** *(BLOCKING — Integration)*: **GIVEN** the Leveling System is initialized with a mock `IClassRegistry` returning `AutoAllocIncrement(Str=3, Dex=0, Vit=0, Int=0)` for `ClassType.Warrior`, **WHEN** a Warrior levels from L1 to L2, **THEN** STR=13, DEX=10, VIT=10, INT=10 — confirming the Leveling System reads from the registry and does not hardcode values.

- **AC-CS-15** *(BLOCKING — Logic)*: **GIVEN** a `ClassDefinition` with a null or empty `SkillList` field, **WHEN** `IClassRegistry.GetClassSkills(classType)` is called, **THEN** the return value is `Array.Empty<SkillID>()` and is not null.

**Startup Validation (CD-10)**

- **AC-CS-16** *(BLOCKING — Logic)*: **GIVEN** a `ClassRegistry` asset with two entries sharing the same `ClassType` value, **WHEN** the Class System initializes in a **dev/debug build**, **THEN** the spawn system halts and a critical error is logged identifying the duplicate `ClassType`. *(In release builds, per CD-10, a critical error is logged but the spawn system continues — this path requires a separate editor-mode test.)*

- **AC-CS-17** *(BLOCKING — Logic)*: **GIVEN** a `ClassDefinition` entry where `autoAllocIncrement.Total() + FreePointsPerLevel ≠ 5`, **OR** any individual `AutoAllocIncrement` field is outside [0, 2], **WHEN** the Class System initializes in a **dev/debug build**, **THEN** the spawn system halts and a critical error is logged identifying the violating class. *(In release builds, error is logged but spawn continues.)*

- **AC-CS-18** *(BLOCKING — Logic)*: **GIVEN** the `ClassRegistry` asset is absent from the project, **WHEN** the Class System initializes, **THEN** the spawn system halts with a critical error in both dev and release builds.

**Spawn Lifecycle**

- **AC-CS-19** *(BLOCKING — Integration)*: **GIVEN** an entity already in the Spawned state, **WHEN** a second `IEntitySpawnRequest` arrives for the same `entityId`, **THEN** the request is rejected, the existing `CharacterStats` registration is preserved, and no `OnPlayerSpawned` broadcast fires.

- **AC-CS-20** *(BLOCKING — Integration)*: **GIVEN** an entityId is tracked in the Class System's spawning-in-progress set (simulated via an injectable `ISpawnLifecycleHook` or a test-facing `SimulateSpawningState(entityId)` method — **lead programmer must design the test hook before this story is implemented**), **WHEN** one `IEntityDespawnRequest(entityId)` is processed, **THEN** `_pendingDespawn == true` AND after `CompleteSpawn(entityId)` is called (simulating Step 6), the entity is deregistered and `ICharacterStatsProvider.GetStats(entityId)` returns null.

- **AC-CS-20b** *(BLOCKING — Logic)*: **GIVEN** the same spawning-in-progress state, **WHEN** two `IEntityDespawnRequest` events arrive for the same `entityId`, **THEN** after Step 6, `_statsRegistry.Deregister(entityId)` is called **exactly once** (not twice), no exception is thrown, and the entity is not registered afterward.

- **AC-CS-21** *(BLOCKING — Integration)*: **GIVEN** a character record in persistence with an unrecognized `ClassType` byte value, **WHEN** the spawn system attempts to initialize `CharacterStats` for that entity, **THEN** the spawn is aborted, a critical error is logged with the entityId and the raw byte value, and no `CharacterStats` entry is registered.

- **AC-CS-22** *(BLOCKING — Logic)*: **GIVEN** an entity has been despawned (deregistered), **WHEN** a second `IEntityDespawnRequest` arrives for the same `entityId`, **THEN** no exception is thrown and the system remains in a valid state.

- **AC-CS-23** *(BLOCKING — Logic)*: **GIVEN** `InitializeAtL1()` is called before SA-2 Step 2 registers the entity, **WHEN** `InitializeAtL1` runs, **THEN** an `InvalidOperationException` is thrown in dev builds — confirming registration must precede initialization.

**SA-3 Ownership Invariant** *(ADVISORY — Code-review gate)*: `CharacterStats` is constructed only via `ICharacterStatsFactory`. Verified at code review by confirming that (a) the `CharacterStats` constructor is `private`, and (b) `ICharacterStatsFactory` is registered in the DI container with only Class System and Mob Spawning System as recipients. Optionally: add a reflection-based unit test `Assert.IsTrue(typeof(CharacterStats).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0)`.

**Respec Item Integrity (two-phase commit)**

- **AC-CS-24** *(BLOCKING — Integration)*: **GIVEN** a character with a respec item in active inventory has entered Phase 1 of CR-4.1 (item moved to reserved hold slot), **WHEN** `RollbackStatTransaction()` is called during Phase 2 execution, **THEN** `ItemReservation.Release()` is called, the item is returned to active inventory, no stat changes persist, and `GetHeldFreePoints(entityId)` is unchanged.

- **AC-CS-25** *(BLOCKING — Integration)*: **GIVEN** a character has completed a respec via `EndStatTransaction()` successfully, **WHEN** the Inventory System is notified, **THEN** `ItemReservation.Consume()` is called **exactly once** and the respec item no longer appears in active inventory or the reserved hold slot.

## Open Questions

- **OQ-CS-1 — RESOLVED (2026-05-28 — Skill System GDD Approved)**. `SkillID` type (`readonly struct wrapping uint`), `ISkillSystem` interface (`OnEntitySpawned`, `OnEntityDespawned`, `OnLevelUp`), Healer INT-scaled offensive skill (Holy Smite, L1 `MagicalDamage`), and distinct per-class combat feel from L1 are all defined in `skill-system.md`. SA-2 Step 5 null-conditional replaced with hard `ISkillSystem` dependency. Pillar 3 solo viability confirmed: L60 INT Healer deals `BaseDamage = floor(246 × 1.5) = 369` via Holy Smite, sufficient for solo grinding without competing with Warrior damage identity (~975 at comparable multiplier).

- **OQ-CS-2 — ClassDefinition description string**: Should `ClassDefinition` include a `Description` string field for the character creation screen? Currently only `DisplayName` is defined. *Owner: Onboarding GDD. Resolve before character creation screen is specced.*

- **OQ-CS-3 — Auto-alloc sum invariant at Vertical Slice**: CD-10 validates `auto-alloc sum + FreePointsPerLevel == 5`. A third class at Vertical Slice may require a different total. This invariant must be revised before VS class authoring. *Owner: this GDD revision.*

- **OQ-CS-4 — BLOCKING (Leveling System GDD update)**: LC-1 requires the Leveling System to read `AutoAllocIncrement` from `IClassRegistry` instead of hardcoded values in CR-2.4. The Leveling System GDD must be updated before its pending re-review can pass. *Owner: Leveling System GDD.*

- **OQ-CS-5 — Character creation screen destination**: After class confirmation, which screen does the player navigate to? (Name entry? Direct zone entry? Confirmation summary?) *Owner: Onboarding GDD.*
