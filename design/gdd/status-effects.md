# Status Effects / Buffs

> **Status**: In Design — Revised (post-review)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-20 (lean re-review fixes)
> **Implements Pillar**: Social Gravity (primary), Earned Power (secondary)

## Overview

Status Effects / Buffs is the system responsible for applying, tracking, and expiring all temporary stat modifications and periodic resource restoration in Iron Grind. It owns the buff modifier layer of `CharacterStats` — the sole system permitted to call `AddBuffModifier()` and `RemoveBuffModifier()` — and drives all tick-counted duration expiry. It also owns all HP and MP regen events, issuing `ApplyRegen()` and `ApplyManaRegen()` calls to Character Stats on each server tick for entities with active regen effects.

In practice, a status effect is a named bundle of one or more stat modifiers (flat or percentage bonuses or penalties on any `StatID`) and/or a periodic regen rate, attached to a target entity for a fixed tick duration and identified by a `BuffID`. When a Skill System skill applies a buff, it routes through Status Effects — not directly to CharacterStats. When duration elapses, Status Effects calls `RemoveBuffModifier()`, and `OnStatChanged` fires so all subscribers (HUD, combat systems) see the updated effective value without polling. A second entry point, `ApplyInstantHeal(EntityID, float amount)`, delivers one-shot HP restoration without creating an `ActiveEffect` entry — intended for reactive Healer tools that respond to near-death moments (see Player Fantasy).

At MVP, two classes interact with this system: the Healer delivers the system's most visible output — sustained healing, defense buffs that make the party measurably harder to kill, and the HP bonus that lets Warrior players push further into elite zones. Debuffs (movement speed slows, attack power reductions) are in scope as mob abilities and future skill mechanics but require the Enemy AI and Skill System GDDs to define their concrete parameters. The wire protocol does not yet define buff-state broadcast messages; client display of buff icons and durations is a provisional dependency on networking additions and will not be resolved in this GDD.

## Player Fantasy

A mob's hit lands that should have put me on the floor — and the health bar holds. There's a sigil burning quietly above it that I didn't put there. Out here, where the world takes everything and gives nothing back, that small warm mark is the most honest kind of power: proof that another player chose to keep me standing. I don't have to be told the difference between grinding alone and grinding protected. I feel it in the hit I walked away from.

And if you're the one casting it — the regen pulse, the defense that turns a killing blow into a survivable one — your power isn't measured in your own kill count. It's measured in who's still standing when the pull is done. They know it was you. In a world this grim, being the one a party can't push deep without is its own quiet legend.

*Pillar alignment: Social Gravity (primary) — the buff is the party contract made visible on your own health bar; Earned Power (supporting) — protection the recipient could not grind alone; Legendary Gear (contextual) — the same "everyone knows who" social signal applied to a person rather than a weapon.*

## Detailed Design

### Core Rules

**CR-SE-1: Data model**

A status effect is represented by two value structs:

- `BuffDefinition` (readonly struct): `BuffID buffId`, `byte modifierCount` (number of populated entries, 0–8), `StatModifier modifier0` through `modifier7` (8 inline value fields; entries at index ≥ `modifierCount` are zeroed), `uint durationTicks`, `float hpRegenPerTick`, `float mpRegenPerTick`.
- `StatModifier` (struct): `StatID statId`, `float flatBonus`, `float pctBonus`.

Both are fully value types with no reference-type fields. No managed heap allocation occurs on copy or storage. On IL2CPP (required for iOS), both are treated as unmanaged structs, satisfying CR-SE-15 and AC-SE-25.

The system tracks each live application as an `ActiveEffect` value struct: `BuffID buffId`, `EntityID entityId`, `EntityID casterEntityId` (for client buff-bar attribution — see CR-SE-13), `uint ticksRemaining`, `byte modifierCount`, `StatModifier modifier0` through `modifier7` (inline snapshot of the applied definition's modifier values), `float hpRegenPerTick`, `float mpRegenPerTick`. The internal table is a 1D array `ActiveEffect[MAX_ENTITY_SLOTS * MAX_ACTIVE_BUFFS_PER_ENTITY]`; entity `e`'s effect slots occupy indices `[e * MAX_ACTIVE_BUFFS_PER_ENTITY, (e+1) * MAX_ACTIVE_BUFFS_PER_ENTITY - 1]` (see CR-SE-15).

**CR-SE-2: Permitted and prohibited stat targets**

Any `StatID` except `Level`, `Experience`, `CurrentHP`, and `CurrentMP` may be targeted by a modifier. `MaxHP` and `MaxMP` are permitted (expiry clamping applies — see CR-SE-8). `CurrentHP` and `CurrentMP` are prohibited because resource restoration is delivered via `ApplyRegen()` / `ApplyManaRegen()` (CR-SE-6), not via the modifier layer.

**CR-SE-3: Public entry points**

Two public entry points exist. No external caller may invoke `CharacterStats.AddBuffModifier()` or `CharacterStats.ApplyRegen()` directly.

**`ApplyEffect(EntityID target, EntityID caster, BuffDefinition def)`** — applies a timed buff or debuff. The `caster` parameter is stored in the `ActiveEffect` snapshot for client buff-bar attribution display (see CR-SE-13); it is not passed to `CharacterStats`. Before applying: count the entity's current occupied modifier slots across all active effects. If adding `def.modifierCount` slots would exceed `CharacterStats`' 32-entry modifier capacity, return `ApplyResult.CapacityExceeded` and make no changes. Application is atomic: if any `AddBuffModifier()` call within a multi-stat definition fails mid-application, call `RemoveBuffModifier()` for each stat already applied in this attempt, then return failure.

**`ApplyInstantHeal(EntityID target, float amount)`** — delivers a one-shot HP restoration by calling `CharacterStats.ApplyRegen(target, amount)` immediately. No `ActiveEffect` entry is created; no duration, no modifier. Intended for reactive Healer tools (see Player Fantasy). Validation: reject `amount ≤ 0` with `ApplyResult.InvalidAmount`. Death check applies: reject with `ApplyResult.InvalidTarget` if the entity is in `PendingRemoval` state.

**CR-SE-4: Refresh-on-reapply**

If the target entity already has an `ActiveEffect` with the same `BuffID`, reset its `TicksRemaining` to `def.durationTicks`. Do NOT call `RemoveBuffModifier()` or `AddBuffModifier()` — the modifier values remain active on `CharacterStats` unchanged. Only one instance of each `BuffID` per entity is ever active.

Exception: if the new application carries different modifier values (e.g. an upgraded skill cast at higher rank), treat it as a new application: remove the old effect (CR-SE-8 expiry procedure), then apply the new one via CR-SE-3.

**CR-SE-5: Execution order**

Status Effects runs its `OnServerTick()` before Auto-Attack Combat each game tick. Buff expirations, regen, and stat changes from buff application are resolved before any damage formula reads effective stats.

**CR-SE-6: Per-tick processing**

Each tick, for every `ActiveEffect` entry:
1. Decrement `TicksRemaining` by 1.
2. If `hpRegenPerTick > 0`: call `CharacterStats.ApplyRegen(entityId, hpRegenPerTick)`.
3. If `mpRegenPerTick > 0`: call `CharacterStats.ApplyManaRegen(entityId, mpRegenPerTick)`.

Regen is not a modifier on `CharacterStats` and leaves no persistent state after expiry.

**CR-SE-7: Expiry detection**

An effect expires when `TicksRemaining` decrements to 0. Expiry is processed in the same tick as the final decrement; the effect is not active for any subsequent tick.

**CR-SE-8: Expiry procedure**

When an effect expires:
1. For each `StatModifier` in the expired `BuffDefinition`: call `CharacterStats.RemoveBuffModifier(entityId, statId, buffId)`.
2. If `MaxHP` was among the buffed stats: after removing the `MaxHP` modifier, call `CharacterStats.ApplyDamage(entityId, 0.0f)` to trigger the HP clamp-on-write invariant, ensuring `CurrentHP` is clamped to the reduced `MaxHP`. *Open question: confirm `ApplyDamage(0.0f)` triggers the clamp without producing a damage event visible to other systems — see OQ-SE-1.*
3. If `MaxMP` was among the buffed stats: equivalent MP clamp procedure pending OQ-SE-1 resolution.
4. Remove the `ActiveEffect` entry from the internal table and release the slot.

**CR-SE-9: Debuff handling**

Debuffs are buffs with negative `flatBonus` or `pctBonus` values in one or more `StatModifier` entries. They share the same data model, application path, refresh rule, expiry procedure, and stack rule as positive buffs — there is no separate code path. No debuff immunity system exists at MVP; all entities are equally susceptible.

**CR-SE-10: Death cleanup**

Status Effects subscribes to `CharacterStats.OnEntityDied`. On receipt of `OnEntityDied(entityId)`: mark all `ActiveEffect` entries for that entity as `PendingRemoval`. Do NOT call `RemoveBuffModifier()` inside the event handler.

On the next `OnServerTick()`, process all `PendingRemoval` entries: call `RemoveBuffModifier()` for each stat per effect, then clear the internal entries. This deferred pattern is required because `CharacterStats` Rule 8 prohibits write operations in `OnEntityDied` event handlers to prevent re-entrant stat modification during death processing.

**CR-SE-11: Zone departure and disconnect**

When a player leaves a zone or disconnects, all active buffs on that entity are cleared immediately using the expiry procedure (CR-SE-8) for each active effect. Buff state is not persisted across zone transitions at MVP — players enter new zones with a clean buff slate.

**Sequence contract:** Status Effects cleanup (and OWL compensation cleanup, if applicable) must complete before the entity slot is released to the slot pool for reassignment. The zone/session departure handler must invoke Status Effects cleanup as a synchronous step prior to entity slot reclamation. If this order is violated, a `RemoveBuffModifier()` call may target a slot index that has been reassigned to a new entity.

Status Effects subscribes to the zone session event that fires on player departure. The exact notification interface is not yet defined in any authored GDD (see OQ-SE-2).

When a mob's entity slot is freed (on mob death and slot reclamation), any remaining active effects on that slot are cleared as part of slot cleanup, using the same sequence contract above.

**CR-SE-12: Mob buff layer**

Mob entities have buff modifier arrays initialized by `CharacterStats` at spawn, identical to player entities. This overrides any prior `CharacterStats` design note restricting buff modifiers to players only. Status Effects may apply buffs and debuffs to mobs using the same `ApplyEffect()` path. The `CharacterStats` GDD must be updated before implementation to reflect this requirement.

**CR-SE-13: Caster liveness**

If a Healer disconnects mid-fight, active buffs on party members persist until their natural expiry. This is an intentional design decision: the Healer's protection commitment does not expire with their session. The choice already happened at cast-time; persistence honours that commitment.

To maintain the Social Gravity experience ("they know it was you"), the `ActiveEffect` snapshot stores the caster's `EntityID` in the `casterEntityId` field (see CR-SE-1). The client buff bar displays "cast by [name]" using this field even after the caster disconnects. This attribution data travels to the client via the wire protocol data contract (see Networking row in Interactions and Dependencies).

`CharacterStats.AddBuffModifier()` has no `CasterEntityID` parameter and cannot be changed. Caster identity is stored in Status Effects' own `ActiveEffect` snapshot exclusively — CharacterStats is not aware of it.

**CR-SE-14: Target selection**

Status Effects is target-agnostic. `ApplyEffect()` accepts a single target `EntityID`. The Skill System assembles the target list (self, party members, hostile entities within range) and calls `ApplyEffect()` once per target. AoE filtering, party membership checks, and self-targeting rules are the Skill System's responsibility.

**CR-SE-15: Performance architecture**

Internal active-effect storage is a single 1D array: `ActiveEffect[MAX_ENTITY_SLOTS * MAX_ACTIVE_BUFFS_PER_ENTITY]` (= `ActiveEffect[1600]` at current constant values). Entity `e`'s effect slots occupy the contiguous range `[e * MAX_ACTIVE_BUFFS_PER_ENTITY, (e+1) * MAX_ACTIVE_BUFFS_PER_ENTITY - 1]`. This layout requires one heap allocation at system initialization, achieves sequential cache access during per-entity tick iteration, and requires no pointer indirection — both conditions necessary to satisfy AC-SE-25 under IL2CPP.

`MAX_ENTITY_SLOTS = MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE = 200`. Entity slot indices match the slot-assignment scheme used by the OWL compensation system. Occupied slot counts per entity are tracked in a parallel `byte[MAX_ENTITY_SLOTS]` occupancy array (one allocation at system init). No `Dictionary`, no LINQ, and no heap allocation occur in the per-tick hot path.

**CR-SE-16: No cross-buff interactions at MVP**

Two buffs of the same stat stack additively through `CharacterStats`' existing modifier summation. There is no aura, dominance, or cancel relationship between `BuffID`s at MVP. Future expansion may introduce debuff cleanse, buff priority, and immunity flags.

**CR-SE-17: OnStatChanged propagation — batched per tick**

`CharacterStats` fires `OnStatChanged` once per affected `StatID` when a modifier is added or removed. Status Effects brackets each tick's `RemoveBuffModifier()` / `AddBuffModifier()` pass with a batch-flush contract: `CharacterStats` defers individual `OnStatChanged` emissions during the pass, then fires one event per unique `(EntityID, StatID)` pair after the flush signal. Subscribers (HUD, combat systems) receive at most one `OnStatChanged` per affected stat per tick, regardless of how many effects expire or are applied in that tick.

**Worst-case event count (bounded by this contract):** 200 entities × 8 stats = 1,600 unique `(EntityID, StatID)` events per tick maximum. Without batching, mass-expiry at tick N (e.g., zone-wide simultaneous buff application at pull start) would produce up to 12,800 events in one tick.

Status Effects brackets each tick's buff flush pass with `CharacterStats.BeginStatTransaction()` before processing and `CharacterStats.EndStatTransaction()` after — deferring all `OnStatChanged` events during the pass and firing one event per unique `(EntityID, StatID)` pair at `EndStatTransaction()`. See CharacterStats GDD respec policy section (Transaction constraints) for the full dedup and nesting rules. Both the Leveling System (respec) and Status Effects (tick-path buff flush) are permitted callers of this API; CharacterStats GDD has been updated accordingly.

---

### States and Transitions

States are per `ActiveEffect` slot (one row in the internal table per entity per BuffID):

| State | Description | Entry Conditions | Exit Conditions |
|-------|-------------|-----------------|-----------------|
| **Empty** | Slot is unoccupied | Initial state; expiry completes; cleanup completes | `ApplyEffect()` succeeds for this entity + BuffID |
| **Active** | `TicksRemaining > 0`; modifiers applied to CharacterStats | `ApplyEffect()` accepted | `TicksRemaining` reaches 0; `OnEntityDied` fires; entity leaves zone |
| **Refreshed** | Same as Active; `TicksRemaining` reset this tick | `ApplyEffect()` called on an already-Active entry with the same BuffID and matching modifier values | Immediately treated as Active on the same tick |
| **PendingRemoval** | Entity died; effect awaiting deferred cleanup | `OnEntityDied(entityId)` received while effect is Active | Next `OnServerTick()` cleanup pass calls `RemoveBuffModifier()` and returns slot to Empty |
| **Expiring** | `TicksRemaining` decremented to 0 this tick | Decrement in CR-SE-6 produces 0 | Expiry procedure (CR-SE-8) completes; slot returns to Empty in the same tick |

No effect ever skips from Active directly to Empty without passing through Expiring or PendingRemoval.

---

### Interactions with Other Systems

| System | Direction | Interface / Data Flow |
|--------|-----------|----------------------|
| **CharacterStats** | SE → CS | `AddBuffModifier(EntityID, StatID, float flat, float pct, uint durationTicks, BuffID)` — apply modifier at buff application time. *Note: `durationTicks` is passed for audit logging only; CharacterStats must not use this value for duration tracking — Status Effects owns expiry.* `RemoveBuffModifier(EntityID, StatID, BuffID)` — remove modifier at expiry or cleanup. `ApplyRegen(EntityID, float amount)` — HP regen per tick (also called from `ApplyInstantHeal()` for one-shot reactive heals). `ApplyManaRegen(EntityID, float amount)` — MP regen per tick. `ApplyDamage(EntityID, 0.0f)` — trigger HP clamp after MaxHP modifier removal (OQ-SE-1). `BeginStatTransaction()` / `EndStatTransaction()` — tick-path buff flush (defers `OnStatChanged` during the pass; fires once per unique `(EntityID, StatID)` pair at close; see CharacterStats GDD Transaction constraints). |
| **CharacterStats** | CS → SE | `OnEntityDied(EntityID)` event — triggers deferred cleanup (CR-SE-10). |
| **Skill System** | Skill → SE | `ApplyEffect(EntityID target, EntityID caster, BuffDefinition def)` — Skill System is the sole external caller of `ApplyEffect()`. Provides target EntityID, caster EntityID (for buff-bar attribution), and fully-formed `BuffDefinition`. All targeting logic (party filter, AoE, self-cast) is resolved before the call. `ApplyInstantHeal(EntityID target, float amount)` — also available to Skill System for reactive one-shot heals. |
| **Auto-Attack Combat** | Ordering | Status Effects `OnServerTick()` completes before Auto-Attack Combat `OnServerTick()` in every game tick. No data is exchanged at runtime; ordering is an execution-order contract. |
| **Networking** | SE → Net (indirect) | Status Effects fires no wire messages directly. `CharacterStats.OnStatChanged` (batched per CR-SE-17) carries stat changes to the networking layer. **Wire protocol data contract (to be encoded by the Networking GDD):** the client-side buff bar requires the following fields per active effect: `BuffID` (identifies the buff icon), `TicksRemaining` (drives the duration drain animation), `CasterEntityID` (enables "cast by [name]" attribution display per CR-SE-13). Replication triggers: on application, on refresh, on expiry. Wire encoding format and message schema are deferred to the Networking GDD. |
| **Zone / Session System** | Zone → SE | Player-departure and disconnect events trigger buff cleanup (CR-SE-11). Notification interface not yet defined in any authored GDD — see OQ-SE-2. |

## Formulas

**F-SE-1: Duration conversion**

Designer-facing durations are specified in seconds and stored internally as ticks:

```
DurationTicks = floor(DurationSeconds × TICK_RATE_HZ)
```

| Symbol | Type | Range | Description |
|--------|------|-------|-------------|
| `DurationSeconds` | float | (0.05, 300.0] | Designer-specified duration. Minimum 0.05s (1 tick). Maximum 300.0s (SESSION_TTL_SECONDS — buffs cannot outlive a zone session). |
| `TICK_RATE_HZ` | uint constant | 20 | Server ticks per second. |
| `DurationTicks` | uint | [1, 6000] | Stored tick count. |

Validation: `ApplyEffect()` rejects `def.durationTicks < 1` with `ApplyResult.InvalidDuration`. A `uint` field prevents negatives; zero is an explicit check. `floor()` is used for consistency with all other tick-expressed timeouts in this codebase (e.g., ghost TTL, OWL compensation).

| Input | DurationTicks | Verdict |
|-------|--------------|---------|
| 0.5s | 10 | Valid |
| 3.0s | 60 | Valid (typical HoT) |
| 0.0s | 0 | Rejected — `ApplyResult.InvalidDuration` |
| 300.0s | 6000 | Valid (maximum authoring ceiling) |
| 3600.0s | 72000 | Rejected by authoring ceiling (exceeds SESSION_TTL_SECONDS) |

---

**F-SE-2: Modifier slot capacity guard**

Before applying an effect, `ApplyEffect()` checks whether the incoming modifiers would exceed `CharacterStats`' 32-entry limit:

```
OccupiedSlots(entity) + def.modifierCount ≤ 32
```

If the guard fails, return `ApplyResult.CapacityExceeded` and make no changes.

Also reject: if `def.modifierCount == 0` AND `def.hpRegenPerTick == 0` AND `def.mpRegenPerTick == 0`, return `ApplyResult.NoOpDefinition`.

---

**F-SE-3: OccupiedSlots definition**

`OccupiedSlots` in F-SE-2 is the sum of populated `StatModifier` entries — not the count of active effects:

```
OccupiedSlots(entity) = Σ activeEffect[i].modifierCount
                        for all i in [entity * MAX_ACTIVE_BUFFS_PER_ENTITY,
                                      (entity+1) * MAX_ACTIVE_BUFFS_PER_ENTITY - 1]
                        where activeEffect[i].entityId == entity
```

| Symbol | Type | Range | Description |
|--------|------|-------|-------------|
| `activeEffect[i].modifierCount` | byte | [0, 8] | Populated `StatModifier` slot count in the i-th active effect for this entity |
| `OccupiedSlots(entity)` | int | [0, 32] | Total modifier entries currently consumed by this entity |

An entity with 4 active buffs each using 4 modifiers has OccupiedSlots = 16, not 4.

---

**F-SE-4: Refresh tick reset**

On refresh (CR-SE-4), `TicksRemaining` is fully reset — no blending, no extension from the current remaining value:

```
TicksRemaining_after_refresh = def.durationTicks
```

If a buff has 1 tick remaining when the caster re-applies, the full duration is restored. A caster cannot "top off" a buff by a partial amount — it is always a hard reset to the defined duration.

---

**F-SE-5: Regen clamp boundary (ownership note)**

Status Effects passes `hpRegenPerTick` as a raw float to `CharacterStats.ApplyRegen()` each tick. `CharacterStats` owns the clamp:

```
HP_after_regen = min(CurrentHP + HpRegenPerTick, MaxHP)  [owned by CharacterStats]
```

Status Effects does not check `CurrentHP` or `MaxHP` before calling `ApplyRegen()`. If the entity is at full HP, the call is still made; `CharacterStats` handles the no-op. This avoids a conditional HP-state branch in the per-tick hot path.

## Edge Cases

**EC-SE-1: DurationTicks = 1 (minimum viable duration)**

An effect applied with `durationTicks = 1` is active for exactly one tick. Within that tick: decrement runs first (TicksRemaining → 0), then regen fires (if any), then expiry detection triggers. The entity receives one regen pulse and the modifier is applied and removed in the same tick. This is valid — a 50ms buff is unusual but not prohibited.

**EC-SE-2: ApplyEffect() called on a dead entity**

If `OnEntityDied` has already fired for the target entity (effects are in `PendingRemoval` state), `ApplyEffect()` must reject the call: return `ApplyResult.InvalidTarget`. The entity is logically dead; adding a new buff that will be cleaned up one tick later is a waste of a slot and could produce a spurious `OnStatChanged` event. Status Effects checks its own `PendingRemoval` flag for the entity slot before processing the modifier capacity guard.

**EC-SE-3: Refresh or new application on a PendingRemoval entry**

If a `BuffID` already in `PendingRemoval` state is the target of an `ApplyEffect()` call (e.g., a Skill System event and an `OnEntityDied` event land in the same tick), the entity is treated as dead: reject with `ApplyResult.InvalidTarget`. The `PendingRemoval` flag takes precedence over the refresh rule (CR-SE-4).

**EC-SE-4: MaxHP buff expires while CurrentHP is above the new MaxHP**

Example: A +200 MaxHP buff expires. Before expiry: MaxHP = 600, CurrentHP = 580. After the `RemoveBuffModifier()` call: MaxHP = 400. `CurrentHP` (580) is now above `MaxHP` (400), which is an illegal state.

Resolution per CR-SE-8: `ApplyDamage(entityId, 0.0f)` is called immediately after the MaxHP modifier is removed. `CharacterStats` clamps `CurrentHP` to the new `MaxHP` during damage processing. The entity's HP drops from 580 to 400 as a result of the buff expiry, not as damage — this clamp must not generate a visible damage event (see OQ-SE-1).

If `CurrentHP ≤ new MaxHP` at expiry time (entity had taken damage since the buff was applied), `ApplyDamage(0)` is still called but is a no-op clamp. `CharacterStats` fires no event for a zero-damage call that does not change `CurrentHP`.

**EC-SE-5: MaxMP buff expiry (same pattern as EC-SE-4)**

Identical to EC-SE-4 with `MaxMP` and `CurrentMP`. The MP clamp procedure is pending OQ-SE-1 resolution — if `CharacterStats` does not expose a zero-amount `ApplyManaRegen()` path that triggers the clamp, a `ClampCurrentMPToMax(entityId)` method must be added (OQ-SE-1 scope).

**EC-SE-6: Active effect row limit hit (separate from modifier slot limit)**

`MAX_ACTIVE_BUFFS_PER_ENTITY = 8` is the row limit in the internal `ActiveEffect[][]` array. This is independent of the 32-modifier-entry guard in F-SE-2. If an entity already occupies all 8 rows and a new `ApplyEffect()` arrives with a different `BuffID` (not a refresh), the call is rejected with `ApplyResult.EffectSlotsFull`, even if `OccupiedSlots + def.modifierCount ≤ 32`. The check order in `ApplyEffect()` is: (1) dead/PendingRemoval check, (2) effect row availability, (3) modifier slot capacity.

**EC-SE-7: Multiple concurrent buffs modifying the same StatID**

Entity has two different `BuffID`s, both modifying `AttackPower`. `CharacterStats` stacks them additively per its modifier summation rule. When one expires, `RemoveBuffModifier(entityId, AttackPower, buffIdA)` removes only `buffIdA`'s entry; `buffIdB`'s entry remains untouched. `OnStatChanged(AttackPower)` fires once, reflecting the reduced (but non-zero) buff contribution. No cross-buff logic is required.

**EC-SE-8: Effect application arrives mid-tick**

If `ApplyEffect()` is called during `OnServerTick()` processing (e.g., a skill evaluation inside the same tick loop), the new `ActiveEffect` entry is inserted into the array but marked for processing starting on the **next** tick. The current tick's loop iterator has already passed or is mid-pass; processing the new entry in the current pass would give it an extra TicksRemaining decrement it has not earned. On first `OnServerTick()` after application, the effect receives its first decrement and, if applicable, its first regen pulse.

**EC-SE-9: Zone departure while PendingRemoval effects exist**

Entity dies in tick N (effects marked `PendingRemoval`). Before tick N+1 cleanup runs, the zone closes and the zone departure handler fires (CR-SE-11). Zone departure cleanup iterates all effect entries for the entity and clears them, regardless of `Active` or `PendingRemoval` state. The tick N+1 cleanup pass will find no entries for this entity and is a no-op. No double-RemoveBuffModifier occurs because the zone departure handler runs the full expiry procedure before the entity slot is released.

**EC-SE-10: Regen fire at full HP**

An entity is at `CurrentHP = MaxHP` when a regen buff is active. On each tick, `ApplyRegen(entityId, hpRegenPerTick)` is still called. `CharacterStats` clamps to MaxHP and fires no event (no state change). This is correct and intentional — Status Effects does not check HP state before calling `ApplyRegen()` (see F-SE-5). The no-op branch stays in `CharacterStats` where it belongs.

## Dependencies

| System | Direction | Interface | Notes |
|--------|-----------|-----------|-------|
| **Character Stats** | SE → CS | `AddBuffModifier(EntityID, StatID, float flat, float pct, uint durationTicks, BuffID)` (*`durationTicks` = audit logging only; CharacterStats must not use for duration tracking*) `RemoveBuffModifier(EntityID, StatID, BuffID)` `ApplyRegen(EntityID, float)` `ApplyManaRegen(EntityID, float)` `ApplyDamage(EntityID, 0.0f)` (HP clamp trigger — see OQ-SE-1) `BeginStatTransaction()` / `EndStatTransaction()` (tick-path buff flush — see CharacterStats GDD) | Upstream. CharacterStats owns all effective-stat computation (F-1); Status Effects only writes and removes modifiers. Bidirectional: CharacterStats must list Status Effects in its Dependencies as the sole caller of AddBuffModifier/RemoveBuffModifier. **CharacterStats propagation edits complete (2026-05-20): mob buff layer enabled in CharacterStats rule text (CR-SE-12 resolved); transaction API extended to Status Effects callers (CR-SE-17 resolved).** |
| **Character Stats** | CS → SE | `OnEntityDied(EntityID)` event | Status Effects subscribes to trigger deferred death cleanup (CR-SE-10). |
| **Skill System** | Skill → SE | `ApplyEffect(EntityID target, EntityID caster, BuffDefinition def)` `ApplyInstantHeal(EntityID target, float amount)` | Downstream of SE. Skill System is the sole external caller of both entry points. All targeting logic (party filter, AoE, range) is the Skill System's responsibility before the call. Skill System GDD must list Status Effects as a dependency. |
| **Auto-Attack Combat** | Order only | None | Status Effects `OnServerTick()` runs before Auto-Attack Combat each tick (CR-SE-5). No data exchange at runtime. Both systems subscribe to the same server tick; execution order is an engine-level scheduling contract. |
| **Networking** | SE → Net (indirect) | Wire data contract: `BuffID`, `TicksRemaining`, `CasterEntityID` per active effect | `CharacterStats.OnStatChanged` (batched per CR-SE-17) carries stat changes to the networking layer. Status Effects fires no wire messages directly. **Client-side data contract:** the networking layer must replicate per-entity active effect state including `BuffID`, `TicksRemaining`, and `CasterEntityID` on application, refresh, and expiry events. Wire encoding format and schema are deferred to the Networking GDD. |
| **Zone / Session System** | Zone → SE | Player-departure and disconnect notification (interface TBD) | Required for CR-SE-11 buff cleanup on zone exit. Notification interface is not yet defined in any authored GDD — see OQ-SE-2. Status Effects must subscribe once this interface is defined. |

## Tuning Knobs

| Constant | Value | Safe Range | Owner | Effect on Gameplay |
|----------|-------|-----------|-------|-------------------|
| `MAX_STATS_PER_BUFF` | 8 | [1, 32] — cannot exceed CharacterStats modifier capacity | Status Effects | Maximum number of `StatID` entries in a single `BuffDefinition`. Higher values allow richer multi-stat buffs but increase the cost of the modifier capacity guard (F-SE-2). Increasing beyond CharacterStats' 32-slot limit would allow a single buff to saturate the entire modifier layer. |
| `MAX_ACTIVE_BUFFS_PER_ENTITY` | 8 | [4, 16] | Status Effects | Maximum concurrent active effects per entity. Hard lower bound is floor(32 / MAX_STATS_PER_BUFF) = 4 — below this, a single maximally-complex buff would leave no room for any others. Upper bound is practical headroom; re-derive when adding new buff-heavy classes post-MVP. Memory: each unit increase costs ~128 bytes × 200 entities = 25.6 KB. |
| `DurationSeconds` (authoring ceiling) | 300.0s | (0.05, 300.0] — min 1 tick; max SESSION_TTL_SECONDS | Per `BuffDefinition` | Maximum buff duration. 300.0s (6000 ticks) is the ceiling because CR-SE-11 clears buffs on zone departure — a buff longer than one zone session is unreachable anyway. Authoring validation rejects definitions above this ceiling. |
| `HpRegenPerTick` | Per definition | [0.5, 50.0] | Per `BuffDefinition` | HP restored per server tick (50ms). **Design verification tool — not a runtime formula:** `TotalHpRegen = HpRegenPerTick × DurationTicks`. **Efficacy target (design anchor):** a standard 3s HoT at any tier should restore ~50% of that tier's MaxHP. At tier-1 (MaxHP ≈ 400): target = 200 HP over 3s → 200 / 60 ticks ≈ 3.4 HP/tick. Scale with tier MaxHP when authoring higher-tier definitions — do not reuse flat values across tiers. `ApplyInstantHeal()` amounts follow the same 50%-of-tier-MaxHP guideline per use, since they are one-shot rather than sustained. |
| `MpRegenPerTick` | Per definition | [0.5, 50.0] | Per `BuffDefinition` | MP restored per server tick. Analogous to `HpRegenPerTick`. Verification formula: `TotalMpRegen = MpRegenPerTick × DurationTicks`. Scale per tier. |

**Registry cascade note:** `MAX_STATS_PER_BUFF` and `MAX_ACTIVE_BUFFS_PER_ENTITY` must both be registered in `design/registry/entities.yaml` and referenced by this GDD. If `MAX_STATS_PER_BUFF` changes, the F-SE-2 capacity guard lower bound for `MAX_ACTIVE_BUFFS_PER_ENTITY` changes — re-derive safe range and update both registry entries together.

## Visual/Audio Requirements

*Note: Client display of buff state depends on a wire protocol extension not yet defined. All client-facing requirements below are provisional — they describe the intended experience but cannot be fully specified until the Networking GDD is updated with buff-state broadcast messages.*

**Application feedback**
- When a buff is applied to a player character, a brief visual cue fires on the target: a shimmer or particle burst styled to the buff type (heal = warm/golden; defense = cool/blue; attack = red/orange). Duration: ~0.3–0.5s. Does not impede gameplay visibility.
- Audio: a soft, distinctive application sound per buff category. Must be recognizable at low volume on mobile speakers. Not jarring when multiple buffs are applied in rapid succession (party pull scenario).

**Persistent indicator**
- A subtle persistent visual on the buffed entity: a small ambient glow or icon-attached shimmer that a player notices but does not find distracting. Must be legible on a mobile screen in landscape at typical combat distances.

**Expiry feedback**
- When a buff expires, a brief fade-out or dissipation visual on the target entity. Audio: a soft, distinct expiry cue — different enough from the application sound to be distinguishable, quiet enough not to interrupt audio mix during a long pull.

**Regen tick feedback**
- Regen pulses are batched and shown as a single floating number every 5–10 ticks (0.25–0.5s) rather than every tick. Art Director to specify final cadence.

## UI Requirements

*Provisional — depends on buff-state wire protocol (undefined).*

**Buff bar**
- A horizontal strip of buff icons on the player's HUD, anchored to the health bar or in a defined HUD slot. Maximum visible icons = `MAX_ACTIVE_BUFFS_PER_ENTITY` (8). Icons are ordered by application time (oldest first) or by buff category (TBD with UX).
- Each icon: a small square asset associated with the `BuffID`. Must be legible at mobile HUD scale (~32–40px).

**Duration timer**
- Each icon displays a radial or linear drain timer representing `TicksRemaining / durationTicks`. Text countdown optional (seconds remaining).

**Debuff display**
- Debuffs applied to the player are displayed in a separate strip with a distinct visual treatment (e.g., red tint) so the player can distinguish buffs from debuffs at a glance.

**Enemy buff/debuff display**
- Debuffs applied to enemies are displayed as small icons on the enemy's health bar or nameplate. Truncate at 4 visible; "+N more" indicator if above 4.

## Acceptance Criteria

**AC-SE-01 — Happy path: single-stat buff application** (Logic)
Call `ApplyEffect(entityId, def)` where `def` has 1 `StatModifier` (AttackPower, flat=+10, pct=0) and `durationTicks=40`. Assert: (1) `CharacterStats.AddBuffModifier(entityId, AttackPower, +10, 0, 40, buffId)` was called exactly once; (2) `GetEffectiveStat(entityId, AttackPower)` returns BaseStat + 10; (3) the entity's `ActiveEffect` row exists with `TicksRemaining=40`.

**AC-SE-02 — Multi-stat buff application and expiry** (Logic)
Apply a `BuffDefinition` with 3 `StatModifiers`. After `durationTicks` ticks: assert `RemoveBuffModifier()` was called exactly 3 times (once per stat), `ActiveEffect` row is freed, and `GetEffectiveStat()` returns base values for all 3 stats.

**AC-SE-03 — Capacity guard: modifier slot rejection** (Logic)
Fill an entity's CharacterStats modifier layer to 30 occupied slots (via 30 single-stat active effects). Attempt `ApplyEffect()` with a 3-modifier buff (would require 33 slots total). Assert: return value is `ApplyResult.CapacityExceeded`; no `AddBuffModifier()` call is made; entity's existing effects are unchanged.

**AC-SE-04 — OccupiedSlots counts modifier entries, not effect count** (Logic)
Apply 4 active effects, each with 7 `StatModifiers` (OccupiedSlots = 28). Attempt a 5-modifier buff (28 + 5 = 33 > 32). Assert: `ApplyResult.CapacityExceeded`. Then attempt a 4-modifier buff (28 + 4 = 32 ≤ 32). Assert: `ApplyResult.Success`. OccupiedSlots was computed by summing `modifierCount` across all active effects, not by counting effect rows.

**AC-SE-05 — Atomic rollback on partial application failure** (Logic)
Instrument CharacterStats to fail on the 2nd `AddBuffModifier()` call. Apply a 3-modifier buff. Assert: (1) the 1st modifier was added and then immediately removed; (2) the 2nd and 3rd modifiers were never added; (3) `GetEffectiveStat()` returns unmodified values for all 3 stats; (4) no `ActiveEffect` row was created.

**AC-SE-06 — NoOp definition rejected** (Logic)
Call `ApplyEffect()` with a `BuffDefinition` where `modifierCount == 0`, `hpRegenPerTick == 0`, and `mpRegenPerTick == 0`. Assert return value is `ApplyResult.NoOpDefinition` and no `AddBuffModifier()` call is made.

**AC-SE-07 — InvalidDuration rejected** (Logic)
Call `ApplyEffect()` with `durationTicks = 0` (from `DurationSeconds < 0.05`). Assert return value is `ApplyResult.InvalidDuration`. Call with `durationTicks = 1`. Assert `ApplyResult.Success`.

**AC-SE-08 — Prohibited stat targets rejected** (Logic)
Attempt `ApplyEffect()` with a `StatModifier` targeting `Level`, then `Experience`, then `CurrentHP`, then `CurrentMP` (four separate calls). Assert all four return `ApplyResult.InvalidStat`. Assert no `AddBuffModifier()` call is made in any case.

**AC-SE-09 — MaxHP and MaxMP are permitted buff targets** (Logic)
Apply a `BuffDefinition` with a `StatModifier` targeting `MaxHP` (flat=+100). Assert `ApplyResult.Success` and `GetEffectiveStat(MaxHP)` returns base + 100.

**AC-SE-10 — EffectSlotsFull: row limit enforced** (Logic)
Apply `MAX_ACTIVE_BUFFS_PER_ENTITY` (8) distinct `BuffID`s to the same entity. Attempt a 9th distinct `BuffID`. Assert return value is `ApplyResult.EffectSlotsFull` even if `OccupiedSlots + modifierCount ≤ 32`.

**AC-SE-11 — Refresh: TicksRemaining reset without CharacterStats calls** (Logic)
Apply a buff with `durationTicks=60`. Advance 30 ticks (TicksRemaining=30). Re-apply the same `BuffID` to the same entity. Assert: (1) TicksRemaining is reset to 60; (2) no `RemoveBuffModifier()` call is made; (3) no `AddBuffModifier()` call is made; (4) `GetEffectiveStat()` returns the same value as immediately after the first application.

**AC-SE-12 — Refresh is a hard reset, not an extension** (Logic)
Apply a buff with `durationTicks=40`. Advance 35 ticks (TicksRemaining=5). Re-apply. Assert TicksRemaining is 40, not 45 (TicksRemaining was not added to; it was replaced).

**AC-SE-13 — TicksRemaining decrements by 1 each tick** (Logic)
Apply a buff with `durationTicks=5`. After 1 tick: TicksRemaining=4. After 2 ticks: 3. After 3: 2. After 4: 1. After 5: expiry procedure runs.

**AC-SE-14 — Regen delivery per tick** (Logic)
Apply a buff with `hpRegenPerTick=5.0`, `durationTicks=3`, and no stat modifiers (regen-only). Assert `ApplyRegen(entityId, 5.0f)` is called exactly 3 times (once per tick, including the tick when TicksRemaining reaches 0). Assert no `AddBuffModifier()` calls are made.

**AC-SE-15 — Expiry: MaxHP buff triggers HP clamp** (Logic)
Apply a +200 MaxHP buff. Set entity `CurrentHP = MaxHP` (e.g., 600). Advance to expiry. Assert: (1) `RemoveBuffModifier(entityId, MaxHP, buffId)` called; (2) `ApplyDamage(entityId, 0.0f)` called immediately after; (3) `CurrentHP` after expiry equals the reduced MaxHP (400), not 600.

**AC-SE-16 — Deferred death cleanup: RemoveBuffModifier not called in OnEntityDied** (Logic)
Apply a buff with `durationTicks=100`. Trigger `OnEntityDied(entityId)`. Assert: (1) no `RemoveBuffModifier()` call occurs during the `OnEntityDied` handler; (2) effect is in `PendingRemoval` state. Advance one tick. Assert: (3) `RemoveBuffModifier()` is called for each stat; (4) `ActiveEffect` row is freed.

**AC-SE-17 — Dead entity: ApplyEffect rejected after OnEntityDied** (Logic)
Trigger `OnEntityDied(entityId)`. Immediately call `ApplyEffect(entityId, def)`. Assert return value is `ApplyResult.InvalidTarget` and no `AddBuffModifier()` call is made.

**AC-SE-18 — Debuffs use the same code path as buffs** (Logic)
Apply a `BuffDefinition` with `flatBonus = -15` on AttackPower. Assert `GetEffectiveStat(AttackPower)` returns base − 15. Advance to expiry. Assert `GetEffectiveStat(AttackPower)` returns base value.

**AC-SE-19 — Mid-tick application: first decrement on next tick** (Logic)
Using the tick driver's `[TestingOnly] InjectApplyEffectDuringTick(entityId, def)` seam (which calls `ApplyEffect()` synchronously from within the tick loop's iteration body): assert the new entry is NOT decremented in the current tick pass. Assert `TicksRemaining = durationTicks` at the end of the current tick. Assert `TicksRemaining = durationTicks − 1` after the next full tick.

**AC-SE-20 — Mob entities accept ApplyEffect** (Integration)
Spawn a mob entity with a valid entity slot. Call `ApplyEffect(mobEntityId, def)`. Assert `ApplyResult.Success`. Assert `GetEffectiveStat(mobEntityId, [stat])` reflects the modifier. Assert expiry removes the modifier from the mob.

**AC-SE-21 — Caster disconnect does not remove buffs** (Integration)
Apply a buff from Caster entity A to Target entity B. Remove entity A from the zone (simulate disconnect). Assert B's `ActiveEffect` row is unchanged and TicksRemaining continues to decrement normally.

**AC-SE-22 — Zone departure clears all effects** (Integration)
Apply 3 active buffs to an entity. Trigger player zone departure event. Assert all 3 `RemoveBuffModifier()` calls are made. Assert all 3 `ActiveEffect` rows are freed. Assert `GetEffectiveStat()` returns base values for all affected stats.

**AC-SE-23 — Zone departure clears PendingRemoval effects without double-removal** (Logic)
Apply a buff to an entity. Trigger `OnEntityDied(entityId)` (effects now PendingRemoval, no removal yet). Immediately trigger zone departure. Assert: (1) `RemoveBuffModifier()` called exactly once per stat (not twice); (2) `ActiveEffect` rows freed; (3) subsequent tick cleanup pass finds no entries for this entity.

**AC-SE-24 — Execution order: Status Effects tick completes before Auto-Attack Combat tick** (Integration)
Apply a +100 AttackPower buff expiring in 1 tick. On the tick of expiry: assert the buff expiry (`RemoveBuffModifier`) is processed before Auto-Attack Combat reads `EffectiveStat(AttackPower)`. The combat calculation must use the post-expiry stat value.

**AC-SE-25 — No heap allocation in OnServerTick() hot path** (CI)
In a NUnit test: record `GC.GetTotalMemory(false)` before calling `OnServerTick()` under load (200 entities, 8 active effects each, mixed regen and stat-modifier effects). Assert `GC.GetTotalMemory(false)` delta after the call equals zero bytes. No `Dictionary`, `List`, `LINQ`, or `new` expressions may appear in the tick-path code that executes per entity per tick. (Unity Profiler is not available in CI; this test runs headlessly via `unity-test-runner@v4`.)

**AC-SE-26 — Constant constraint guard** (CI)
At build time: assert `MAX_ACTIVE_BUFFS_PER_ENTITY ≥ floor(32 / MAX_STATS_PER_BUFF)`. If this invariant is violated (e.g., someone reduces `MAX_ACTIVE_BUFFS_PER_ENTITY` below 4 while `MAX_STATS_PER_BUFF` remains 8), the build fails with a diagnostic message identifying the conflicting constants.

**AC-SE-27 — CR-SE-4 exception: different modifier values triggers remove-then-reapply** (Logic)
Apply Buff A with `BuffID = X`, `flatBonus = +10` on AttackPower. Re-apply the same `BuffID = X` with `flatBonus = +20` on AttackPower (different modifier value). Assert: (1) `RemoveBuffModifier(entityId, AttackPower, X)` called exactly once; (2) `AddBuffModifier(entityId, AttackPower, +20, 0, durationTicks, X)` called exactly once with the new value; (3) `GetEffectiveStat(AttackPower)` returns base + 20 (not base + 10, not base + 30); (4) `TicksRemaining` is reset to full `durationTicks`.

**AC-SE-28 — CR-SE-16: stacked buffs on same stat expire independently** (Logic)
Apply Buff A (BuffID=1, AttackPower +10, durationTicks=10) and Buff B (BuffID=2, AttackPower +15, durationTicks=20) to the same entity. Advance 10 ticks (Buff A expires). Assert: (1) `RemoveBuffModifier(entityId, AttackPower, BuffID=1)` called exactly once; (2) `RemoveBuffModifier` with `BuffID=2` is NOT called; (3) `GetEffectiveStat(AttackPower)` returns base + 15 (Buff B modifier still active). Advance 10 more ticks (Buff B expires). Assert: (4) `GetEffectiveStat(AttackPower)` returns base.

**AC-SE-29 — CR-SE-17: OnStatChanged event count on expiry** (Logic)
Apply a `BuffDefinition` with 3 `StatModifiers` (AttackPower, MaxHP, DefencePower). Advance to expiry. Using a test event counter subscribed to `CharacterStats.OnStatChanged`: assert exactly 3 events fired (one per unique StatID). Assert no additional events fired during the expiry tick. Repeat with batched expiry of two effects sharing one StatID in the same tick: assert the shared stat fires exactly once (batching contract per CR-SE-17).

**AC-SE-30 — ApplyInstantHeal: one-shot HP restoration, no ActiveEffect created** (Logic)
Call `ApplyInstantHeal(entityId, 100.0f)`. Assert: (1) `CharacterStats.ApplyRegen(entityId, 100.0f)` called exactly once; (2) no `ActiveEffect` entry exists for this entity afterward; (3) entity's occupancy counter is unchanged. Assert `ApplyInstantHeal(entityId, 0.0f)` returns `ApplyResult.InvalidAmount`. Assert `ApplyInstantHeal` on a `PendingRemoval` entity returns `ApplyResult.InvalidTarget`.

**AC-SE-31 — Caster attribution stored and accessible** (Logic)
Call `ApplyEffect(target, caster, def)` where caster EntityID = 42. Assert the `ActiveEffect` entry for this application has `casterEntityId = 42`. Assert this field is accessible to the wire protocol layer (i.e., the field is readable from the `ActiveEffect` array without calling any CharacterStats method).

## Open Questions

**OQ-SE-1 — MaxHP/MaxMP expiry clamp mechanism** (Blocking before implementation)
CR-SE-8 calls `ApplyDamage(entityId, 0.0f)` to trigger the `CurrentHP` clamp-on-write after a MaxHP modifier is removed. This requires confirmation from the CharacterStats GDD: (a) Does `ApplyDamage(0.0f)` trigger the HP ≤ MaxHP clamp internally? (b) Does a zero-damage call fire `OnEntityDied` or any damage event visible to combat systems? If the answer to (b) is yes, a dedicated `ClampCurrentHPToMax(entityId)` method must be added to CharacterStats. The MP equivalent (`ApplyManaRegen(0.0f)` or `ClampCurrentMPToMax`) must be addressed simultaneously.
*Owner: CharacterStats author. Resolution required before Status Effects implementation begins.*

**OQ-SE-2 — Zone departure notification interface** (Blocking before implementation)
CR-SE-11 requires Status Effects to subscribe to a zone/session event that fires when a player leaves a zone or disconnects. No authored GDD currently defines this event interface. The Zone System and/or Session System GDDs must be authored and must include this notification contract before Status Effects can implement CR-SE-11.
*Owner: Zone System / Session System GDD author. Resolution required before Status Effects implementation begins.*
