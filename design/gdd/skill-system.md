# Skill System

> **Status**: In Design
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-28 (lean re-review fixes: EC-SK-1 stale variable corrected to _actionTimer/CycleDuration; networking-wire-protocol.md downstream ref updated; OQ-SK-9 pending qualifier removed; OQ-SK-3 resolved — DamageContext.MagicalSkill now defined in damage-calculation.md)
> **Implements Pillar**: Rhythm Mastery (primary), Social Gravity (secondary), Earned Power (supporting)

## Overview

The Skill System is the active-ability layer for all player characters in Iron Grind. It owns the complete lifecycle of a skill activation — mana validation, BaseDamage computation for damaging skills, delegation to Damage Calculation for hit resolution, delegation to Status Effects for buff and healing application, mana consumption, and cooldown registration. All skill execution is server-authoritative; the client submits a cast request, the server resolves it on the next eligible tick.

Each class has a fixed set of skills assigned by the Class System. Skills are gated by level — they unlock progressively as the character levels up, following the Knight Online model: a fresh character has access to a small foundational kit, and new abilities open at defined level thresholds across the 1–60 range. Warrior skills are built for front-line aggression: active attacks that amplify physical burst or layer damage across the 1.0-second auto-attack cadence. Healer skills are built for party sustain: healing-over-time effects, defense and stat buffs that make Warrior players measurably harder to kill, and one INT-scaled offensive ability that keeps the Healer viable when grinding solo. A Warrior weaves a damage skill into the gap after an auto-attack fires; a Healer reads the party's health state and chooses between healing now or casting in the next beat. Skills slot between auto-attack beats without interrupting the cadence — the 1.0-second rhythm is the context skills live inside, not an obstacle they bypass.

At MVP, 10 skills ship per class, distributed across the level 1–60 range. There is no skill tree, no loadout selection, and no skill ranking at MVP — each skill has a single fixed version that unlocks at its threshold level.

## Player Fantasy

The skill system serves two distinct power fantasies that reflect each class's resource economy.

**Warrior — Commitment Under Scarcity.** Mana is a finite weapon. At level 60, a Warrior carries 440 MP — enough for a handful of skills before the well runs dry. Every cast is a deliberate choice: spend now, or save for the moment that counts. A skill slotted into the gap after an auto-attack fires without breaking the 1.0-second cadence — slotted so clean the beat never stutters — is the mark of a Warrior who's internalized the rhythm. Landing a skill at the right moment feels like a spent bullet finding its mark: definitive, costly, and right.

**Healer — Sustained Attention Under Load.** Mana is not the constraint; reading the room is. A Healer's 6,104 MP at level 60 means the question is never "can I cast?" — it's "what does the party need *right now*?" A HoT applied before a Warrior pulls buys three ticks of breathing room. A defense buff chosen over a heal is a bet that the next hit won't finish the job. The rhythm isn't the Healer's cadence to manage — it's the Warrior's cadence they're managing *against*. Sustaining a party through an extended grind, with the right response timed to the right beat, feels like holding a line nobody else can see.

**The shared fantasy.** Both classes experience the 1.0-second cadence as the stage their skills perform on. Skills don't interrupt the beat — they *use* it. A player who learns when to cast, what to cast, and what to hold feels not just effective but fluent. The system rewards players who internalize the rhythm, not players who spam the fastest button.

## Detailed Design

### Core Rules

**Skill Execution Lifecycle**

**CR-SK-1 — Cast Request.** The client submits a `SkillCastRequest { casterEntityID: uint, skillID: uint, targetEntityID: uint, clientTickNumber: uint }` message (16 bytes). The server enqueues it in the per-entity input queue. At most one `SkillCastRequest` per entity is processed per server tick; additional queued requests are discarded.

**CR-SK-2 — Validation Sequence.** The following checks execute in order on the next tick boundary. The first failure emits a `SkillCastRejectionCode` and exits; no subsequent checks run.

| Step | Check | Rejection Code |
|------|-------|---------------|
| V-0 | `ZoneSessionState(casterEntityID) == Alive` (not Dead or Respawning) | `CasterNotAlive` |
| V-1 | `casterEntityID` matches authenticated session | silently dropped; security event logged |
| V-2 | `clientTickNumber` is not stale: `serverTick - clientTickNumber ≤ CAST_STALE_TICK_TOLERANCE` | silently dropped |
| V-3 | `skillID` resolves in `SkillDatabase` | `InvalidSkill` |
| V-4 | `SkillDefinition.ClassType == caster.ClassType` | `InvalidSkill` |
| V-5 | `skillInstance.IsUnlocked == true` (pre-cached at spawn) | `SkillLocked` |
| V-5b | `StatusEffects.IsSilenced(casterEntityID) == false` | `Silenced` |
| V-6 | `skillInstance.CooldownExpiryTick <= serverCurrentTick` | `OnCooldown` |
| V-7 | `targetEntityID != 0`; target is alive and in zone; `TargetConstraint` matches target type | `InvalidTarget` |
| V-8 | `SkillDefinition.RangeUnits == 0` OR `distanceSq(caster, target) <= RangeUnits²` | `OutOfRange` |
| V-9 | `CharacterStats.ConsumeMana(casterEntityID, SkillDefinition.ManaCost) == true` | `InsufficientMana` |

Mana is consumed at V-9, the final check, immediately before execution begins. All checks that can fail must pass before any state mutation occurs.

**CR-SK-3 — Auto-Attack Notification.** Immediately after all validations pass, before any damage or effect resolution: call `CombatSystem.NotifySkillUsed(casterEntityID)`.

**Reactive cooldown model (authoritative).** The auto-attack timer is not fixed-cadence. It resets on any action — auto-attack or skill cast. When `NotifySkillUsed()` is called, the Auto-Attack Combat system resets `_actionTimer = 0.0` (float, seconds). The next auto-attack fires when `_actionTimer >= CycleDuration` (default 1.0s at `AttackSpeedMultiplier = 1.0`). See `auto-attack-combat.md` F-3 for the full fire-check formula.

Skill damage is always additive — it resolves independently and never suppresses the auto-attack. The only effect on the auto-attack cadence is a timer reset from the skill cast tick. Players who cast immediately after the auto fires delay the next auto by 1–2 ticks (negligible). Players who cast before the auto fires push the next auto back by however many ticks remained — nothing is cancelled, but the delay accumulates. Skilled play: wait for auto, then cast. Impatient play: cast early, next auto is pushed further out.

> ✅ **Cross-doc dependency resolved (2026-05-27).** `auto-attack-combat.md` has been revised and approved under the reactive cooldown model. The `_skillUsedThisCycle` flag, grace window, and `SKILL_COMMITTED` state have been removed. Both documents use `_actionTimer` (float, seconds) as the shared timer variable. Implementation may proceed once skill-system.md's remaining blockers are resolved.

**CR-SK-4 — BaseDamage Computation (damaging skills only).**
`BaseDamage = floor(GetEffectiveStat(casterEntityID, SkillDefinition.DamageBaseStat) × SkillDefinition.DamageMultiplier)`

`DamageBaseStat` is a field on `SkillDefinition` (see schema below). Default mapping: `PhysicalDamage` skills default to `StatID.AttackPower`; `MagicalDamage` skills default to `StatID.Intelligence`. Override to `StatID.Strength` for STR-scaling Healer skills (Divine Strike, Smite Armor, Judgment). The override stat replaces the default entirely — stats are never additive in BaseDamage computation.

**CR-SK-5 — Damage Calculation Delegation (damaging skills only).** Call `DamageCalculation(BaseDamage, casterEntityID, targetEntityID, damageContext)` where `damageContext = DamageContext.PhysicalSkill` for `PhysicalDamage` skills, and `DamageContext.MagicalSkill` for `MagicalDamage` skills. Returns `DamageResult { DamageDealt: int, IsCrit: bool, IsKill: bool }`.

**CR-SK-6 — Kill Sequence.** On `DamageResult.IsKill == true`, execute in this exact order: `GetXPAward(casterEntityID, targetEntityID)` → `AddExperience(casterEntityID, xpAmount)` → `ApplyDamage(targetEntityID, DamageResult.DamageDealt)`.

**CR-SK-7 — Effect Application.** After damage resolution (or instead of it, for non-damaging skills):
- If `SkillEffectType` includes `ApplyBuff` or `ApplyDebuff`: assemble the target list (Skill System responsibility, per CR-SE-14), then call `StatusEffects.ApplyEffect(targetEntityID, casterEntityID, SkillDefinition.BuffDefinition)` once per target. Do not call `ApplyEffect` on any target confirmed dead this tick (`IsKill == true`).
- If `SkillEffectType` includes `InstantHeal`: call `StatusEffects.ApplyInstantHeal(targetEntityID, healAmount)`. `healAmount` is defined in the Formulas section.
- Both paths may fire in the same cast (e.g., a skill that deals damage AND applies a debuff uses bitwise OR: `PhysicalDamage | ApplyDebuff`).

**CR-SK-8 — Cooldown Registration.** After all effect application is complete:
`skillInstance.CooldownExpiryTick = serverCurrentTick + SkillDefinition.CooldownTicks`

Cooldown is always registered when a cast fully validates, regardless of whether the skill hit or missed.

**CR-SK-9 — Result Broadcast.** The server emits `SkillCastResult { skillID, casterID, targetID, castAccepted, rejectionCode, damageDealt, healAmount, isCrit, isKill, newCooldownTicks }` with the following delivery rules:
- **Accepted casts** (`castAccepted == true`): sent to the caster unconditionally, and to all other clients for whom the caster or the target entity is in their relevance set (party member or current hostile target), per `networking-relevance-filter.md`.
- **Rejected casts** (`castAccepted == false`): sent to the caster only. No broadcast — no game state changed, so bystanders do not need notification.

---

**Cooldown Model**

**CR-SK-10 — Tick-Based Absolute Expiry.** Cooldowns are stored as absolute server tick timestamps, not decrementing counters. A skill is ready when `serverCurrentTick >= skillInstance.CooldownExpiryTick`. `CooldownTicksRemaining` for client display is computed on demand: `max(0, CooldownExpiryTick - serverCurrentTick)`. This avoids per-tick decrements across all entities and all skill slots.

**CR-SK-11 — No Global Cooldown at MVP.** Each skill has its own independent cooldown. The 1.0-second auto-attack cadence functions as the natural inter-skill pacing constraint. Adding a GCD on top of individual cooldowns would double-gate Warrior play without design benefit.

**CR-SK-12 — Cooldown Client Sync.** On each tick, the server includes `SkillCooldownUpdate { skillID, cooldownExpiryTick }` in the R-U batch for any skill whose cooldown state changed (started or expired). `cooldownExpiryTick` is an absolute server tick at which the cooldown expires; amended 2026-06-20 from `ticksRemaining` (relative duration) to `cooldownExpiryTick` (absolute tick) to enable drift-correct cooldown fraction rendering in the Combat UI. **Amendment (2026-06-20):** When a cast fails V-6 (`OnCooldown` rejection), the server MUST include `SkillCooldownUpdate { skillID, cooldownExpiryTick }` for the rejected skill in the same R-U batch as the `SkillCastResult`. This guarantees the Combat UI's EC-CUI-4 self-correction path — a client whose snapshot was lost can recover accurate cooldown state from the first rejected cast. On zone join or reconnect, the server sends a full `SkillCooldownSnapshot` covering all 10 of the caster's skills. The client renders cooldown UI from server-authoritative values only; it does not compute cooldown state independently.

**CR-SK-23 — SkillCooldownUpdate Delivery Contract.** `SkillCooldownUpdate { skillID, cooldownExpiryTick }` is emitted in two cases: (1) after any accepted cast — carries the newly registered `CooldownExpiryTick` (CR-SK-8); (2) as a companion to an `OnCooldown` rejection (V-6) — carries the current `CooldownExpiryTick` for the rejected skill. In both cases the update is included in the R-U batch containing the `SkillCastResult`. `SkillCooldownUpdate` is NOT emitted for cooldown expiry events — expiry is derived by the client from `cooldownExpiryTick` vs. `NetworkManager.ServerTime.Tick`. The `SkillCooldownSnapshot` (zone join / reconnect) is the bulk equivalent covering all 10 skills; it uses the same `cooldownExpiryTick` format.

---

**Mana Cost Model**

**CR-SK-13 — Mana Consumed at Cast-Start.** Mana is consumed at V-9, before execution. There is no mana refund on miss or on target death during execution. The resource cost is the cost to act, not to succeed.

**CR-SK-14 — Warrior Cost Constraint.** Individual Warrior skill costs must fall in the range [50, 150] MP. At 440 MaxMP (L60), this yields approximately 4–8 casts before mana is exhausted, requiring a potion or return to town. No Warrior skill grants mana regeneration.

**CR-SK-15 — Healer Cost Constraint.** Individual Healer skill costs must fall in the range [80, 200] MP. At 6,104 MaxMP (L60 INT build), costs are always affordable for a Healer investing in INT.

**CR-SK-16 — Global Mana Cost Multiplier.** A float `MANA_COST_MULTIPLIER` (default 1.0, safe range [0.5, 2.0]) applies at cast time: `adjustedCost = Ceil(SkillDefinition.ManaCost × MANA_COST_MULTIPLIER)`. Stored in `assets/data/SkillSystemConfig.asset`. Not hardcoded.

---

**Targeting Model**

**CR-SK-17 — TargetConstraint Values at MVP.**

| Value | Description | Used by |
|-------|-------------|---------|
| `SingleHostile` | One enemy entity; must not be a friendly | All Warrior skills; Healer offensive skill |
| `SingleFriendly` | One party member or self; must not be hostile | Healer targeted heals and buffs |
| `Self` | Caster only; server replaces client-submitted targetID with casterID | Healer self-buffs; Warrior Rush (L20) and War Cry (L38) |

No AoE at MVP. Multi-target resolution is post-MVP.

**CR-SK-18 — Warrior: Single-Target Only.** 8 of 10 Warrior skills use `SingleHostile`. Rush (L20) and War Cry (L38) use `Self` — the server applies CR-SK-19 auto-resolution identically to how it handles Healer self-buffs. AoE changes the calculus of when to cast in ways that require different pacing design; deferred post-MVP.

**CR-SK-19 — Healer Self-Target Auto-Resolve.** If `TargetConstraint == Self`, the server replaces any client-submitted `targetEntityID` with `casterEntityID` before validation runs. A malicious client cannot direct a `Self` skill at another entity.

---

**Level Unlock System**

**CR-SK-20 — Pre-Cached Unlock State.** Unlock thresholds are stored in `SkillDefinition.UnlockLevel` (int, range [1, 60]). At entity spawn, the Skill System pre-caches `SkillInstance.IsUnlocked` for each of the entity's 10 skills. Cast validation reads `skillInstance.IsUnlocked` (a direct array read), not `entity.Level`.

**CR-SK-21 — Unlock on Level-Up.** The Skill System subscribes to the Leveling System's `OnLevelUp(EntityID, newLevel)` event. On firing, it iterates the entity's 10 `SkillInstance` entries; for any where `IsUnlocked == false` and `SkillDefinition.UnlockLevel == newLevel`, it sets `IsUnlocked = true` and sends `SkillUnlockNotification { skillID }` to the client (R-OD delivery). Unlock is permanent — once set, `IsUnlocked` never reverts to false.

**CR-SK-22 — Unlock Distribution Authoring Constraint.** Across 10 skills per class: at least 2 unlock in levels 1–10 (first skill at L1, second at L5 or earlier), at least 3 in levels 11–30, and at least 5 in levels 31–60. Both classes must have at least one skill available at L1.

---

**SkillDefinition Schema**

`SkillDefinition` is a `[Serializable]` struct in `SkillDatabase : ScriptableObject`. Fields are public fields, not properties (Unity 6.3 `[SerializeField]` fields-only constraint).

| Field | Type | Range / Notes |
|-------|------|---------------|
| `ID` | `SkillID` | `SkillID(0)` = Invalid, reserved |
| `DisplayName` | `string` | HUD and tooltip display only; not used in game logic |
| `ClassType` | `ClassType` (enum : byte) | `Warrior=0, Healer=1` |
| `UnlockLevel` | `int` | [1, 60] |
| `ManaCost` | `int` | [0, MaxMP] |
| `CooldownTicks` | `int` | [0, 6000] — 0 = no cooldown (design flag; requires review) |
| `RangeUnits` | `float` | [0.0, 20.0] — 0 = no range check |
| `TargetConstraint` | `TargetConstraint` (enum : byte) | `SingleHostile=0, SingleFriendly=1, Self=2` |
| `SkillEffectType` | `SkillEffectType` (enum : byte, flags) | `PhysicalDamage=1, MagicalDamage=2, InstantHeal=4, ApplyBuff=8, ApplyDebuff=16` |
| `DamageMultiplier` | `float` | [0.0, 5.0]; ignored when no damage flag set |
| `DamageBaseStat` | `DamageStat` (enum : byte) | `AttackPower=0, Intelligence=1, Strength=2`; default `AttackPower` for `PhysicalDamage`, `Intelligence` for `MagicalDamage`; override to `Strength` for STR-scaling Healer skills |
| `DamageContext` | `DamageContext` (enum) | `PhysicalSkill` or `MagicalSkill`; passed to `DamageCalculation()` |
| `HealMultiplier` | `float` | [0.0, 5.0]; ignored unless `InstantHeal` flag set |
| `BuffID` | `BuffID` (enum : uint) | `BuffID.Invalid` = no buff; required when `ApplyBuff` or `ApplyDebuff` |
| `BuffDefinition` | `BuffDefinition` (inline struct) | Passed to `StatusEffects.ApplyEffect()`; no heap allocation |

---

**OQ-CS-1 Resolution — Healer Offensive Skill and Class System Contracts**

The Class System has a binding open question (OQ-CS-1) requiring: (a) `SkillID` type definition, (b) `OnEntitySpawned` interface definition, (c) at least one INT-scaled Healer offensive skill, (d) distinct per-class combat feel within the first 3 levels. This GDD resolves all four.

(a) `SkillID` is a `readonly struct wrapping uint`. `SkillID(0)` = Invalid. `ClassDefinition.SkillList` stores `SkillID[]`. `IClassRegistry.GetClassSkills(ClassType)` returns `IReadOnlyList<SkillID>`. The null-conditional in the Class System's SA-2 Step 5 must be replaced with a hard dependency once this GDD is approved.

(b) `ISkillSystem` interface (declared by Skill System, consumed by Class System): `void OnEntitySpawned(EntityID, ClassType)`, `void OnEntityDespawned(EntityID)`, `void OnLevelUp(EntityID, int)`.

(c) The Healer's L1 skill "Holy Smite" (working name) uses `SkillEffectType = MagicalDamage`, `TargetConstraint = SingleHostile`, `DamageContext = DamageContext.MagicalSkill`. BaseDamage uses INT in place of AP. At L60 INT=246 with `DamageMultiplier = 1.5`: `BaseDamage = floor(246 × 1.5) = 369`. This is intentionally below Warrior damage output (~450–600) — it enables solo viability without competing with Warrior's damage identity.

(d) Both classes have at least one skill at L1. The Warrior L1 skill is `PhysicalDamage`; the Healer L1 skill is `MagicalDamage`. Different resource economies (440 vs 6,104 MaxMP) and different damage stats (AP vs INT) produce distinct gameplay feel from the first session.

---

### Skill Roster — MVP

Full authored skill list for both classes. Resolves OQ-SK-1 and OQ-SK-2. Values are initial tuning targets; final balance to be verified against playtesting within the safe ranges defined in CR-SK-14, CR-SK-15, and the Tuning Knobs section.

#### Warrior (10 skills — AP-scaled)

| # | Name | Unlock | Effect Type | Target | Mana | CD (ticks / s) | Multiplier | Notes |
|---|------|--------|-------------|--------|------|----------------|------------|-------|
| 1 | Slash | L1 | PhysicalDamage | SingleHostile | 50 | 0 / — | ×0.45 | No cooldown — mana is the only gate |
| 2 | Stab | L5 | PhysicalDamage | SingleHostile | 60 | 40 / 2s | ×0.75 | First rhythm skill; teaches cadence |
| 3 | Rend | L15 | ApplyDebuff | SingleHostile | 70 | 100 / 5s | — | Armor reduction; damage via Status Effects |
| 4 | Rush | L20 | ApplyBuff | Self | 50 | 400 / 20s | — | +40% movement speed for 60 ticks (3s) |
| 5 | Shield Bash | L25 | ApplyDebuff | SingleHostile | 80 | 80 / 4s | — | Slow debuff |
| 6 | Cross Slash | L35 | PhysicalDamage | SingleHostile | 100 | 60 / 3s | ×0.90 | Reliable 3-beat rotation anchor post-L35 |
| 7 | War Cry | L38 | ApplyBuff | Self | 70 | 600 / 30s | — | +20% AP for 200 ticks (10s); see design note |
| 8 | Fury Strike | L42 | PhysicalDamage | SingleHostile | 140 | 200 / 10s | ×1.30 | Big mana spend; save for priority targets |
| 9 | Savage Blow | L50 | PhysicalDamage \| ApplyDebuff | SingleHostile | 110 | 140 / 7s | ×0.85 | Damage + defense reduction combo |
| 10 | Mortal Wound | L58 | PhysicalDamage \| ApplyDebuff | SingleHostile | 150 | 400 / 20s | ×1.60 | Signature finisher; applies -healing received debuff |

**Warrior rotation character at L60:** Slash (free), Stab (2s), Shield Bash (4s), Rend (5s), Savage Blow (7s), Fury Strike (10s), Mortal Wound/Rush (20s), War Cry (30s). Interlocking cooldown windows create real rotation management within the 1.0s auto-attack cadence. 440 MaxMP at L60 is meaningfully scarce across a full cycle.

> ⚠️ **Design Note — Level Distribution.** The 2/3/5 unlock distribution (CR-SK-22) concentrates 5 of 10 Warrior skills in the L31–60 range. Given that Iron Grind's leveling is intentionally grindy, players will spend a large portion of early-to-mid levels with a limited active skill set. Before `SkillDatabase.asset` is finalized, consider shifting some L31+ skills to earlier thresholds (e.g., Cross Slash to L28, War Cry to L32) to maintain rotation interest during the bulk of the leveling experience. The exact thresholds in this table are first-pass proposals, not locked values.

> ⚠️ **Design Note — War Cry AP Buff / Rogue Class Identity.** War Cry currently grants +20% AttackPower. When Rogue is designed, Rogue should be the only class with AP-boosting capability — Warrior already fulfills the tanker role, and AP amplification as a Rogue exclusive reinforces the intended party dynamic (Warrior holds aggro and absorbs damage; Rogue maximizes damage output). Before Rogue design begins, War Cry's buff type must be reconsidered. Candidates: vitality/MaxHP boost, physical defense increase, or incoming-damage reduction — all of which reinforce Warrior's tank identity without overlapping Rogue's AP space. Note also that Warrior has two `Self`-target skills (Rush and War Cry); this is intentional and within scope, but should be reviewed when Rogue's self-buff kit is authored to avoid redundant self-cast patterns across classes.

---

#### Healer (10 skills — INT and STR scaled)

| # | Name | Unlock | Effect Type | Target | Mana | CD (ticks / s) | Scale | Mult | Notes |
|---|------|--------|-------------|--------|------|----------------|-------|------|-------|
| 1 | Holy Smite | L1 | MagicalDamage | SingleHostile | 90 | 40 / 2s | INT | ×1.5 | Solo offensive starter |
| 2 | Minor Heal | L8 | InstantHeal | SingleFriendly | 80 | 40 / 2s | INT | ×1.5 | Baseline reactive heal |
| 3 | Prayer | L12 | ApplyBuff (HoT) | SingleFriendly | 100 | 200 / 10s | INT | per-tick in BuffDef | Pre-pull HoT; 5-tick duration |
| 4 | Bless | L18 | ApplyBuff | SingleFriendly | 120 | 600 / 30s | — | — | AP/DEF buff on party member |
| 5 | Holy Blast | L25 | MagicalDamage | SingleHostile | 130 | 80 / 4s | INT | ×2.0 | Stronger INT offensive option |
| 6 | Divine Strike | L32 | PhysicalDamage | SingleHostile | 120 | 60 / 3s | STR | ×1.2 | Paper build enabler — first STR skill |
| 7 | Greater Heal | L38 | InstantHeal | SingleFriendly | 160 | 80 / 4s | INT | ×2.5 | Emergency high-value heal |
| 8 | Smite Armor | L45 | PhysicalDamage \| ApplyDebuff | SingleHostile | 140 | 120 / 6s | STR | ×1.5 | STR damage + defense reduction |
| 9 | Holy Shield | L52 | ApplyBuff | Self | 180 | 600 / 30s | — | — | Defense + magic resistance on self |
| 10 | Judgment | L58 | PhysicalDamage | SingleHostile | 200 | 200 / 10s | STR | ×2.2 | Paper build signature finisher |

**Build paths at L60:**
- **INT Priest** (standard): 7/10 skills fully effective — Holy Smite, Minor Heal, Prayer, Bless, Holy Blast, Greater Heal, Holy Shield. Strong party healer with real solo grinding capability. STR skills deal negligible damage at low STR stats.
- **STR Priest** ("paper"): Divine Strike, Smite Armor, and Judgment form the damage core. Minor Heal and Prayer remain functional at reduced output due to low INT stat. True glass cannon — viable for solo grinding, fragile to sustained damage.
- **Hybrid**: Mid-tier across all skills; viable for casual play.

**Prayer (HoT) implementation note.** Prayer uses `SkillEffectType.ApplyBuff`. The per-tick heal amount and HoT duration live in `BuffDefinition` and are owned by the Status Effects system. The Skill System fires `ApplyEffect()` and does not track per-tick delivery. `HealMultiplier` in `SkillDefinition` is unused for Prayer; all healing parameters are authored in the buff definition.

> ⚠️ **Design Note — Level Distribution.** Same concern as Warrior: 5 of 10 Healer skills unlock in L31–60. At L25 the Healer has a functional kit (Holy Smite, Minor Heal, Prayer, Bless, Holy Blast). If playtesting reveals players feel skill-starved in the L25–40 range, shift some L31+ skills earlier before `SkillDatabase.asset` is finalized.

---

### States and Transitions

Each skill instance per entity exists in exactly one of the following states. The server is the sole authority for all transitions.

| State | Meaning |
|-------|---------|
| `Locked` | Entity level < `SkillDefinition.UnlockLevel`. Cannot be cast. |
| `Ready` | Unlocked and `CooldownExpiryTick <= serverCurrentTick`. Available to cast. |
| `Executing` | Server accepted a `CastRequest`; resolving within the current tick. Transient — never persists to the next tick. |
| `OnCooldown` | Execution complete; `CooldownExpiryTick > serverCurrentTick`. |

| From | To | Trigger | Owner |
|------|----|---------|-------|
| — | `Locked` | `OnEntitySpawned`: `entity.Level < def.UnlockLevel` | Skill System |
| — | `Ready` | `OnEntitySpawned`: `entity.Level >= def.UnlockLevel` | Skill System |
| `Locked` | `Ready` | `OnLevelUp`: `newLevel >= def.UnlockLevel` | Skill System (event subscriber) |
| `Ready` | `Executing` | `CastRequest` passes V-1 through V-9 | Skill System |
| `Executing` | `OnCooldown` | Effect resolution completes; `CooldownExpiryTick` written | Skill System |
| `Executing` | `Ready` | `CooldownTicks == 0` in definition | Skill System |
| `OnCooldown` | `Ready` | `serverCurrentTick >= CooldownExpiryTick` | Skill System (tick loop) |
| Any | `Locked` | Impossible — unlock is permanent once set | N/A |

`Executing` is never visible to the client. On server crash mid-tick, the skill returns to `Ready` on reconnect (cooldown written only on completion).

**SkillInstance (per entity, per skill — runtime state):**

| Field | Type | Notes |
|-------|------|-------|
| `SkillID` | `SkillID` | Reference to the definition |
| `IsUnlocked` | `bool` | Pre-cached at spawn; updated on `OnLevelUp`. Never reverts. |
| `CooldownExpiryTick` | `uint` | Absolute server tick. 0 = no cooldown active. Reset on `OnEntityDespawned`. |

The per-entity skill store is a flat `SkillInstance[10]` array positionally aligned with `IClassRegistry.GetClassSkills(classType)`. No dictionary; cooldown check is a direct index read. Memory: ~12 KB for 200 entities.

### Interactions with Other Systems

| System | Data IN | Data OUT | Interface Owner | Sequence Constraints |
|--------|---------|----------|-----------------|---------------------|
| **Character Stats** | `GetEffectiveStat(entityID, StatID.Level)` at spawn; `GetEffectiveStat(entityID, StatID.AP)` / `StatID.INT` at cast; `ConsumeMana(entityID, amount) → bool` at V-9 | None — does not write stats directly | `ICharacterStatsProvider` — owned by Character Stats | Valid from SA-2 Step 5 onward |
| **Auto-Attack Combat** | None — Skill System does not read auto-attack state | `CombatSystem.NotifySkillUsed(casterEntityID)` — resets `_actionTimer` to 0.0 in the reactive cooldown model | `ICombatSystem` — declared by Auto-Attack Combat (approved 2026-05-27) | Called after all validation passes (CR-SK-3), before damage resolution; never called on rejected casts |
| **Damage Calculation** | `DamageResult { DamageDealt, IsCrit, IsKill }` | `BaseDamage`, `AttackerID`, `TargetID`, `DamageContext` | `DamageCalculation()` — owned by Damage Calculation | Called only for damage-flagged skills; after CR-SK-3 |
| **Status Effects / Buffs** | `ApplyResult` from `ApplyEffect()`; `ApplyInstantHeal` is fire-and-forget | `BuffDefinition` (fully assembled); target list assembled by Skill System before first call; `float healAmount` | `ApplyEffect()` and `ApplyInstantHeal()` — owned by Status Effects; Skill System is sole external caller (CR-SE-14) | Do not call `ApplyEffect` on a target with `IsKill == true` from the same tick |
| **Class System** | `IClassRegistry.GetClassSkills(ClassType) → IReadOnlyList<SkillID>` at spawn; `ClassType` via `OnEntitySpawned` | `ISkillSystem.OnEntitySpawned(EntityID, ClassType)`; `OnEntityDespawned(EntityID)`; `OnLevelUp(EntityID, int)` | `ISkillSystem` declared by Skill System; `IClassRegistry` declared by Class System | `OnEntitySpawned` always called after SA-2 Step 4, before SA-2 Step 6 |
| **Leveling System** | `OnLevelUp(EntityID, newLevel)` event | `IsUnlocked = true` written to `SkillInstance`; `SkillUnlockNotification` sent to client | Event subscription — Skill System subscribes at initialization | Must be subscribed before first entity spawns; event carries post-increment level |
| **Client** | `SkillCastRequest { casterEntityID, skillID, targetEntityID, clientTickNumber }` (C→S, U-U, 16 bytes) | `SkillCastResult` (S→C, R-U batch); `SkillUnlockNotification` (S→C, R-OD on unlock); `SkillCooldownSnapshot` (S→C on zone join/reconnect) | Wire protocol owned by Networking Core; Skill System produces payloads | Stale requests discarded; `CooldownSnapshot` sent as part of zone join snapshot |

## Formulas

All formulas execute server-side. All intermediate results use 32-bit integer or 32-bit float arithmetic. Final values are floored to `int` unless specified otherwise.

---

**F-SK-1 — Physical Skill BaseDamage**

```
BaseDamage_Physical = floor(AP × DamageMultiplier)
```

| Variable | Source | Range at L60 |
|----------|--------|-------------|
| `AP` | `GetEffectiveStat(casterID, StatID.AttackPower)` | 532–768 (Warrior) |
| `DamageMultiplier` | `SkillDefinition.DamageMultiplier` | [0.5, 5.0] |
| `BaseDamage_Physical` | Output — passed to `DamageCalculation()` | [266, 3840] |

Example (L60 Warrior, AP=650, DamageMultiplier=1.5): `BaseDamage = floor(650 × 1.5) = 975`

This BaseDamage is then modified by `DamageCalculation(975, casterID, targetID, DamageContext.PhysicalSkill)` which applies hit/miss/crit resolution. The Skill System is responsible only for BaseDamage; final damage dealt is owned by Damage Calculation.

---

**F-SK-2 — Magical Skill BaseDamage (Healer Offensive)**

```
BaseDamage_Magical = floor(INT × DamageMultiplier)
```

| Variable | Source | Range at L60 |
|----------|--------|-------------|
| `INT` | `GetEffectiveStat(casterID, StatID.Intelligence)` | ~246 (Healer INT build) |
| `DamageMultiplier` | `SkillDefinition.DamageMultiplier` | [0.5, 5.0] |
| `BaseDamage_Magical` | Output — passed to `DamageCalculation()` | [123, 1230] |

INT replaces AP entirely; `StatID.AttackPower` is not read. Passes to `DamageCalculation(BaseDamage, casterID, targetID, DamageContext.MagicalSkill)`.

Design target: at L60 Healer with DamageMultiplier=1.5: `floor(246 × 1.5) = 369`. Warrior physical skill at comparable multiplier yields ~975. Healer offensive output is intentionally ~37% of Warrior peak damage — sufficient for solo grinding, not competitive as a primary damage dealer. `DamageMultiplier` on this skill is the primary tuning lever for Healer solo viability.

Example (L60 Healer, INT=246, DamageMultiplier=1.5): `BaseDamage = floor(246 × 1.5) = 369`

---

**F-SK-3 — Instant Heal Amount**

```
HealAmount = floor(INT × HealMultiplier)
```

| Variable | Source | Range at L60 |
|----------|--------|-------------|
| `INT` | `GetEffectiveStat(casterID, StatID.Intelligence)` | ~246 (Healer INT build) |
| `HealMultiplier` | `SkillDefinition.HealMultiplier` | [0.5, 5.0] |
| `HealAmount` | Output — passed to `StatusEffects.ApplyInstantHeal()` | [123, 1230] |

Healing power scales with INT for the same reason magical damage does: both are expressions of the Healer's investment in their primary stat.

Example (L60 Healer, INT=246, HealMultiplier=2.0): `HealAmount = floor(246 × 2.0) = 492`

---

**F-SK-4 — Effective Mana Cost**

```
effectiveManaCost = Ceil(ManaCost × MANA_COST_MULTIPLIER)
```

| Variable | Source | Range |
|----------|--------|-------|
| `ManaCost` | `SkillDefinition.ManaCost` | Warrior [50, 150]; Healer [80, 200] |
| `MANA_COST_MULTIPLIER` | `SkillSystemConfig.asset` | [0.5, 2.0], default 1.0 |
| `effectiveManaCost` | Output — passed to `ConsumeMana()` | Never negative; always ≥ 1 if ManaCost > 0 |

`Ceil` is used so that fractional multipliers always produce at least the minimum intended cost. Rounding down would allow a 0.5× multiplier to make odd ManaCost values cheaper than designed.

Example (ManaCost=100, MANA_COST_MULTIPLIER=0.75): `effectiveManaCost = Ceil(100 × 0.75) = Ceil(75.0) = 75`

---

**F-SK-5 — Cooldown Remaining in Seconds (Client Display Only)**

```
cooldownSeconds = CooldownTicksRemaining × (1 / TICK_RATE_HZ) = CooldownTicksRemaining × 0.05
```

| Variable | Source | Range |
|----------|--------|-------|
| `CooldownTicksRemaining` | `max(0, skillInstance.CooldownExpiryTick - serverCurrentTick)` | [0, 6000] ticks |
| `TICK_RATE_HZ` | Registry constant: 20 | Fixed |
| `cooldownSeconds` | Output — client UI display only | [0.0, 300.0] seconds |

Server validation always works in ticks. This conversion is display logic only, computed client-side from the `newCooldownTicks` value in `SkillCastResult`.

Example (CooldownTicks=40): `cooldownSeconds = 40 × 0.05 = 2.0 seconds`
Example (CooldownTicks=200): `cooldownSeconds = 200 × 0.05 = 10.0 seconds`

---

**Summary — Variable Ranges by Formula**

| Formula | Input min | Input max | Output min | Output max |
|---------|-----------|-----------|------------|------------|
| F-SK-1 BaseDamage Physical | AP=532, Mult=0.5 | AP=768, Mult=5.0 | 266 | 3840 |
| F-SK-2 BaseDamage Magical | INT=1, Mult=0.5 | INT=246, Mult=5.0 | 0 | 1230 |
| F-SK-3 HealAmount | INT=1, Mult=0.5 | INT=246, Mult=5.0 | 0 | 1230 |
| F-SK-4 EffectiveManaCost | ManaCost=50, Mult=0.5 | ManaCost=200, Mult=2.0 | 25 | 400 |
| F-SK-5 CooldownSeconds | Remaining=0 | Remaining=6000 | 0.0s | 300.0s |

## Edge Cases

**EC-SK-1 — Skill cast submitted on the same tick as an auto-attack.** If both auto-attack resolution and a skill cast request arrive for the same entity on the same tick, auto-attack resolution runs first (per Auto-Attack Combat's tick ordering), then the skill cast is validated. Both deal damage independently. `NotifySkillUsed` fires after the auto-attack completes; since both events share the same tick, `_actionTimer` is reset to `0.0` in either case and the next auto fires when `_actionTimer >= CycleDuration` — the same as if only the auto had fired.

**EC-SK-2 — Insufficient mana at V-9 after earlier checks passed.** If checks V-1 through V-8 pass but `ConsumeMana()` returns false, the cast is rejected with `InsufficientMana`. The skill remains in `Ready` state — no cooldown is registered, no auto-attack notification fires.

**EC-SK-3 — Skill cast while already OnCooldown (stale client UI).** The server validates V-6 independently of client display state. A stale client may show a skill as ready when it is still on cooldown server-side. The server rejects with `OnCooldown`. No game state changes. The client receives the rejection and updates its display.

**EC-SK-4 — Target dies between V-7 (target validation) and effect application.** If the target is killed during the same tick by a different entity, its `IsAlive` flag is false by the time `ApplyEffect` is called. Status Effects rejects the application with `InvalidTarget`. Mana is consumed and cooldown is registered regardless. If the Skill System's own damage step produced the kill (`IsKill == true` in `DamageResult`), buff application on that same target is skipped explicitly before calling `ApplyEffect`.

**EC-SK-5 — Self-targeted skill submitted with an invalid targetEntityID.** The server auto-resolves `Self`-constraint skills by replacing any client-submitted `targetEntityID` with `casterEntityID` before V-7 runs (CR-SK-19). A hostile entity ID or `0` on a `Self` skill is silently corrected, not rejected. This is the only validation step where the server overrides client input.

**EC-SK-6 — Level-up occurs on the same tick as a cast of the newly unlocked skill.** The Leveling System must process `OnLevelUp` before the Skill System processes cast requests within the same tick. If this ordering holds, `skillInstance.IsUnlocked` is `true` by the time V-5 runs and the cast succeeds. The tick-phase ordering (Leveling System before Skill System) is a required dependency constraint.

**EC-SK-7 — Entity despawned while a skill is in mid-execution (Executing state).** Cannot happen. `Executing` is transient and resolves entirely within one tick. `OnEntityDespawned` is not called mid-tick — despawn processing happens at end-of-tick, after all skill execution for that tick is complete.

**EC-SK-8 — `SkillDatabase` does not contain a definition for the submitted `skillID`.** Caught at V-3. The server rejects with `InvalidSkill` and logs a warning. An unknown skillID is unexpected and indicates a version mismatch or a tampered client.

**EC-SK-9 — `MAX_ACTIVE_BUFFS_PER_ENTITY` limit reached on buff application.** `StatusEffects.ApplyEffect()` returns a capacity-exceeded result. The buff is not applied. Mana is already consumed and cooldown is already registered — the resource cost is paid even if the buff was rejected. The `SkillCastResult` broadcast reflects damage dealt (if any) but the buff payload is absent. No retry.

**EC-SK-10 — Healer casts a `SingleFriendly` skill on a hostile target.** Caught at V-7. `TargetConstraint` check detects a hostile target. Rejected with `InvalidTarget`. No game state changes.

**EC-SK-11 — Multiple `SkillCastRequest` messages from the same entity in the same tick.** The server processes the oldest (first-received) request and discards the rest without notification. Discarded requests are not queued for the next tick.

**EC-SK-12 — `OnEntitySpawned` called with a class that has no defined skills.** `GetClassSkills()` returns `Array.Empty<SkillID>()`. The Skill System allocates an empty `SkillInstance[0]` array for the entity, logs a warning, and returns without throwing. The entity cannot cast any skills.

**EC-SK-13 — Server crash between V-9 (mana consumed) and CR-SK-8 (cooldown written).** If the server crashes after `ConsumeMana()` returns `true` but before `CooldownExpiryTick` is written, mana has been consumed and the effect may or may not have fully resolved. On reconnect: the skill returns to `Ready` state — `CooldownExpiryTick` was never persisted, so it resets to 0. The mana loss is accepted; there is no automatic rollback. Character Persistence may or may not have captured the post-consume mana value depending on whether a session save completed before the crash. This is the documented behavior: mana is the cost of attempting the action, not of completing it, and the cooldown-free reconnect prevents the player from being double-penalized (paid mana and stuck on cooldown).

## Dependencies

**Upstream — systems this GDD depends on:**

**Character Stats** *(GDD exists, approved)*
- Reads: `GetEffectiveStat(entityID, StatID.AttackPower)`, `StatID.Intelligence`, `StatID.Level`
- Calls: `ConsumeMana(entityID, amount) → bool`
- Contract: `ConsumeMana()` is atomic — if it returns false, no mana is deducted.
- Reverse reference required: Character Stats GDD must note that Skill System is a consumer of `ConsumeMana()` and `GetEffectiveStat()`.

**Damage Calculation** *(GDD exists, approved)*
- Calls: `DamageCalculation(BaseDamage, AttackerID, TargetID, DamageContext) → DamageResult`
- New contract this GDD adds: `DamageContext.MagicalSkill` must be a valid enum value in the `DamageContext` enum (owned by Damage Calculation). The Damage Calculation GDD must be updated to add this value.
- Reverse reference required: Damage Calculation GDD must note Skill System calls it with `PhysicalSkill` and `MagicalSkill` contexts.

**Status Effects / Buffs** *(GDD exists, approved)*
- Calls: `ApplyEffect(target, caster, BuffDefinition) → ApplyResult`, `ApplyInstantHeal(target, amount)`
- Skill System is the sole external caller of both APIs (CR-SE-14).
- Skill System assembles the target list before calling `ApplyEffect` — Status Effects does not perform targeting.
- Skill System must assign specific `BuffID` values for all buff-granting skills; they must not conflict with values assigned by other systems.
- Reverse reference: Status Effects GDD already documents Skill System as sole caller (CR-SE-14). No additional update required.

**Class System** *(GDD exists, approved)*
- Reads: `IClassRegistry.GetClassSkills(ClassType) → IReadOnlyList<SkillID>`
- Receives: `OnEntitySpawned(EntityID, ClassType)` and `OnEntityDespawned(EntityID)` calls from Class System at SA-2 Step 5 and despawn respectively.
- Contract this GDD closes: OQ-CS-1 — `SkillID` type (`readonly struct wrapping uint`), `ISkillSystem` interface, Healer INT-scaled offensive skill, distinct per-class feel from L1.
- Reverse reference required: Class System GDD's provisional null-conditional `_skillSystem?.OnEntitySpawned(...)` must be replaced with a hard `ISkillSystem` dependency once this GDD is approved.

**Leveling System** *(GDD status: not read this session)*
- Subscribes to: `OnLevelUp(EntityID, newLevel: int)` event
- Tick-phase constraint: the Leveling System must fire `OnLevelUp` before the Skill System processes cast requests within the same tick. This ordering must be documented in both GDDs.
- Reverse reference required: Leveling System GDD must note Skill System subscribes to `OnLevelUp`.

---

**Downstream — systems that depend on this GDD:**

**Class System** — depends on `ISkillSystem` interface and `SkillID` type definition (both canonicalized here); depends on `OnEntitySpawned` not throwing on empty skill lists (EC-SK-12).

**Networking / Wire Protocol GDD** (`networking-wire-protocol.md`, Approved — Skill System amendment 2026-05-28) — depends on `SkillCastRequest`, `SkillCastResult`, `SkillUnlockNotification`, `SkillCooldownSnapshot` message payload definitions (owned by this GDD; wire serialization format owned by Wire Protocol GDD).

**HUD / UI** *(not yet designed)* — depends on `SkillCooldownUpdate`, `SkillCastResult`, `SkillUnlockNotification` payloads for skill bar rendering; depends on `TargetConstraint` enum for target reticle behavior.

**Damage Calculation** — Damage Calculation must not attempt to compute skill BaseDamage. The Skill System is the sole authority on `BaseDamage` for skill damage paths.

## Tuning Knobs

All tuning knobs are stored in `assets/data/SkillSystemConfig.asset` unless noted. None are hardcoded. Per-skill knobs are authored in `SkillDatabase : ScriptableObject`.

| Knob | Type | Default | Safe Range | What it affects |
|------|------|---------|------------|----------------|
| `MANA_COST_MULTIPLIER` | float | 1.0 | [0.5, 2.0] | Scales all skill mana costs globally. Below 1.0: cheaper skills, reduced mana pressure on Warrior. Above 1.0: higher costs, increased Warrior commitment, may price out some Healer builds. |
| `DamageMultiplier` (per skill) | float | varies | [0.5, 5.0] | Per-skill output scaler applied to AP (physical) or INT (magical) before calling `DamageCalculation()`. Primary knob for balancing individual skill damage relative to auto-attack output. |
| `HealMultiplier` (per skill) | float | varies | [0.5, 5.0] | Per-skill output scaler applied to INT for `InstantHeal` skills. Primary knob for balancing Healer heal output relative to incoming enemy damage. |
| `CooldownTicks` (per skill) | int | varies | [0, 6000] | Per-skill cooldown in server ticks (50ms per tick). Values below 10 (0.5s) risk multiple casts per auto-attack cycle; values above 400 (20s) make a skill infrequent enough to lose moment-to-moment gameplay relevance at MVP. |
| `ManaCost` (per skill) | int | varies | Warrior [50, 150]; Healer [80, 200] | Per-skill mana cost before `MANA_COST_MULTIPLIER`. Must respect class cost constraint ranges from CR-SK-14 and CR-SK-15. |
| `UnlockLevel` (per skill) | int | varies | [1, 60] | Level at which the skill becomes available. Must respect the unlock distribution constraint from CR-SK-22. |
| `RangeUnits` (per skill) | float | varies | [0.0, 20.0] | Maximum cast range in world units. 0 = no range limit (self-cast). Values above 12.0 risk off-screen casting on small mobile displays — verify against camera field-of-view before exceeding. |
| `CAST_STALE_TICK_TOLERANCE` | int | 5 | [2, 10] | Maximum tick age of an accepted `CastRequest`. Tolerance of 5 = 250ms; tolerance of 10 = 500ms. Below 2: frequent false stale-drops on high-latency connections. Above 10: delayed inputs execute, which can feel unresponsive or exploitable. Distinct from `STALE_TICK_TOLERANCE` (movement-system.md, value=3) which governs `MovementIntentMessage` staleness. |

**Knobs NOT exposed at MVP:** global cooldown reduction multiplier (no GCD at MVP), per-class `DamageMultiplier` modifier (balance through individual skill values), skill damage spread/variance (deterministic only at MVP).

## Visual/Audio Requirements

### Visual Requirements

**Governing Principles:** All VFX fire on receipt of `SkillCastResult`, not on button press (server-authoritative constraint). No speculative pre-cast effects. All effects use unlit/additive blending only (URP mobile constraint). All emissive flashes use `MaterialPropertyBlock`, never new material instances. Maximum 80 particles simultaneously; no single effect exceeds 15% screen coverage.

---

**Skill Cast Initiation VFX** (plays on caster on `SkillCastResult.castAccepted == true`):

| Trigger | Asset | Description | Particles |
|---------|-------|-------------|-----------|
| Warrior `PhysicalDamage` or `ApplyDebuff` | `vfx_cast_init_warrior_slash_small` | 4-frame weapon-blade edge trace in `#F0EFE8` Ascension White at intensity 1.2. No particles — pure `MaterialPropertyBlock` emissive flash on weapon material. | 0 |
| Healer `MagicalDamage` | `vfx_cast_init_healer_smite_small` | 6-frame staff-tip emissive burst at intensity 2.0 + 4 radial spark particles traveling 0.5 world units outward, `#F0EFE8` at 70% alpha fading over 0.2s. Radial direction distinguishes magical from physical. | 4 |
| Healer `InstantHeal` | `vfx_cast_init_healer_heal_small` | 8-frame emissive pulse on caster hands and staff in Vital Amber `#E8A020` (HP-recovery semantic color), intensity 0→1.0→0. No particles. | 0 |
| Healer `ApplyBuff` | `vfx_cast_init_healer_buff_small` | Same amber emissive pulse, 6 frames. The buff's visual weight lives on the target, not the caster. | 0 |

---

**Skill Hit/Effect VFX** (plays at target position on `SkillCastResult`):

| Condition | Asset | Description | Particles |
|-----------|-------|-------------|-----------|
| `PhysicalDamage`, `IsCrit == false` | `vfx_hit_physical_normal_small` | 6 spark particles in 120° directional cone facing away from attacker, `#E8E6DF` warm off-white, 0.3–0.5 world units travel, 0.25s lifetime. Directional force vector distinguishes physical from magical. | 6 |
| `PhysicalDamage`, `IsCrit == true` | `vfx_hit_physical_crit_small` | 1-frame full-body target silhouette flash at `#F0EFE8` Ascension White intensity 2.5. Plus 12 spark particles in 180° hemisphere, same white, 0.5–0.8 world units, 0.35s lifetime. Scale and count distinguish crits — not color. | 12 |
| `MagicalDamage`, `IsCrit == false` | `vfx_hit_magical_normal_small` | Single disc-sprite ring expanding from 0 to 1.0 world unit radius over 0.3s, `#F0EFE8` at 60%→0% alpha. Radial symmetry communicates energy through the target (vs force applied to target). | 1 |
| `MagicalDamage`, `IsCrit == true` | `vfx_hit_magical_crit_small` | Same ring at 1.5x scale, 80% initial alpha. Plus 6 circular fragment particles orbiting outward from ring edge at peak, 0.4 world units, 0.35s lifetime. | 7 |
| `InstantHeal` | `vfx_hit_heal_small` | 5 upward-drifting dot particles on target torso, Vital Amber `#E8A020` at 70%→0% alpha, 0.5 world units vertical travel, 0.4s lifetime. Upward direction = recovery (semantic motion). | 5 |
| `ApplyBuff` | `vfx_hit_buff_apply_small` | 2-frame albedo brightness +15% on target material via `MaterialPropertyBlock`. Buff icon then appears in target's buff tray. | 0 |
| `ApplyDebuff` | `vfx_hit_debuff_apply_small` | 2-frame emissive tint toward Threat Red `#C0392B` at 40% blend intensity — a tint, not full red (full red is reserved for danger state). Debuff icon then appears on enemy health bar area. | 0 |

---

**Skill Button States (HUD):**

| State | Appearance |
|-------|-----------|
| `Ready` | Full icon opacity. Panel Dark `#1A1C1F` background. Interactive Blue `#4A9EE0` 1px chamfer highlight. No animation. |
| `OnCooldown` | Clockwise sweep overlay (`#1A1C1F` at 60% alpha) retreating as cooldown expires. Centered Bold 11sp countdown in `#E8E6DF`. Updated from server-authoritative `CooldownTicksRemaining`; never extrapolated. |
| `CooldownExpiry` | 200ms radial wipe inward revealing full icon (Art Bible 7.4). Returns to `Ready`. |
| `Locked` | Icon at 20% alpha with desaturated overlay. Diamond lock badge `ui_icon_locked_16` at bottom-center (diamond = alert, per UI shape grammar). No sweep timer. |

---

**Skill Unlock Sequence** (on `SkillUnlockNotification`):

1. Locked → Ready: lock badge fades out over 200ms; icon fades to 100% alpha over 300ms.
2. Diamond "NEW" badge in Interactive Blue `#4A9EE0` appears at top-right of button.
3. Toast slides up from bottom edge (160ms ease-out): skill icon + "[Skill Name] Unlocked" in 13sp Primary Text. Auto-dismisses after 3s with 100ms fade. No full-screen interrupt — unlocks happen mid-combat.

---

**Buff/Debuff Persistent Indicators:**

- Active buffs on party frames: up to 3 `ui_bufficon_[buffname]_active_24` icons below HP bar, Interactive Blue `#4A9EE0` stroke. Downward-sweep duration timer per icon. "+N" overflow badge if >3 active.
- Active debuffs on target frame: `ui_debufficon_[debuffname]_active_24` icons with Threat Red `#C0392B` stroke (color distinguishes debuffs on enemy from buffs on ally at a glance). Downward-sweep duration timer per icon.
- No HoT VFX at MVP (no HoT skills exist at MVP; deferred to post-MVP).

---

**Particle Atlas:** All skill VFX sprites live in the single shared `vfx_particles_shared_256.png` atlas (ASTC). Required sprites: circular dot (heal drift), spark (physical hit), ring disc (magical hit), circular fragment (magical crit). No per-skill texture atlases.

**Performance budget reconciliation (worst case):** 4 Warriors all critting simultaneously = 12 × 4 = 48 particles. Within the 80-particle global cap.

---

### Audio Requirements

**Governing Principles:** All audio triggers on `SkillCastResult`, not on button press. Skill sounds accent the 1.0-second auto-attack cadence — they do not compete with it. All assets mono OGG, 44.1kHz. 3D positional audio at target position; 2D at caster and for UI events.

---

**Cast Audio** (2D, at caster, on `SkillCastResult.castAccepted == true`):

| Trigger | Event | Character | Level |
|---------|-------|-----------|-------|
| Warrior `PhysicalDamage` / `ApplyDebuff` | `sfx_skill_warrior_cast_physical_01–02` | Fast dry metallic hiss — blade/gauntlet through air. 0.08–0.12s attack, 0.10s decay, no reverb. 2-variant pool. | -8 dBFS |
| Healer `MagicalDamage` | `sfx_skill_healer_cast_magical_01` | Short resonant hum 800–1200Hz, 0.15s total. Tuning fork tapped and immediately dampened. | -8 dBFS |
| Healer `InstantHeal` | `sfx_skill_healer_cast_heal_01` | Soft airy pulse 200–400Hz, 0.10–0.15s. Warm, not sharp. | -9 dBFS |

---

**Hit/Effect Audio** (3D positional at target on `SkillCastResult`):

| Condition | Event | Character | Level |
|-----------|-------|-----------|-------|
| `PhysicalDamage`, `!IsCrit` | `sfx_skill_hit_physical_01–03` | Heavier than auto-attack thunk — dense impact, 2–5ms crack, 0.05s body, 0.08s decay. Pitch center a minor third lower than auto pool average. 3-variant pool. | -4 dBFS |
| `PhysicalDamage`, `IsCrit` | Base physical + playback-time: +3 semitones, +3 dBFS | Same asset; crit is a playback modifier, not a separate file. | -1 dBFS |
| `MagicalDamage`, `!IsCrit` | `sfx_skill_hit_magical_01` | High-crack concussive transient (0.03s, 2–4kHz) with hollow resonant body (0.10s). Force arriving from distance. | -5 dBFS |
| `MagicalDamage`, `IsCrit` | Base magical + playback-time: +2 semitones, +2 dBFS | Same asset; crit is a playback modifier. | -3 dBFS |
| `InstantHeal` | `sfx_skill_hit_heal_01` | Soft upward shimmer 600–900Hz, 0.15s. Plays 2D at the recipient's client. | -7 dBFS |
| `ApplyBuff` | `sfx_skill_buff_apply_01` | Short low-frequency hum with upward sweep, 150–350Hz, 0.15s. Protective, grounded. | -8 dBFS |
| `ApplyDebuff` | `sfx_skill_debuff_apply_01` | Short descending dry scrape, 300–800Hz, inharmonic texture, 0.10s. Constraining, not explosive. | -7 dBFS |

Combined-effect skills (`PhysicalDamage | ApplyDebuff`): hit sound plays first, debuff apply sound staggered 50ms later.

---

**Rejection Audio** (on `castAccepted == false`, non-security codes):

`sfx_skill_reject_01` — flat dry deny click, 0.06s, 400–800Hz. 2D, -10 dBFS. Single sound for all rejection codes. UI carries the specific diagnosis. Do not play for V-1/V-2 silent drops.

---

**Cooldown Expiry Audio:**

Silence for cooldowns ≤ 300 ticks (15 seconds). For cooldowns > 300 ticks only: `sfx_skill_ready_01` — brief clean mid-range tone, 0.20s, -12 dBFS. Threshold configurable: `SKILL_READY_AUDIO_COOLDOWN_THRESHOLD_TICKS` (default 300, range [0, 6000]).

---

**Skill Unlock Audio:**

`sfx_skill_unlock_01` — two-tone ascending chime, 0.40s, fundamental + perfect fourth harmonic. Warm, 20% wet reverb tail (0.3s decay). 2D, -5 dBFS. Fires 300ms after level-up audio (exact offset to be confirmed once Leveling System audio is specified).

---

**Audio Priority Hierarchy:**

| Priority | Source | Level |
|----------|--------|-------|
| 1 | Auto-attack hit — never ducked | -6 dBFS |
| 2 | Skill hit, physical | -4 dBFS |
| 3 | Skill hit, magical | -5 dBFS |
| 4 | Buff / debuff apply | -7 to -8 dBFS |
| 5 | Skill cast confirmation | -8 to -9 dBFS |
| 6 | Skill rejection | -10 dBFS |
| 7 | Skill ready tone | -12 dBFS |

No dynamic ducking. Fixed mix hierarchy is the control mechanism. Skill hit and auto-attack hit are mutually exclusive at the beat level (auto-attack system rule) — audio system inherits this exclusivity without additional logic.

Max simultaneous skill audio voices: 3 (1 cast + 1 hit + 1 buff/debuff). Plus 1 auto-attack = 4 total combat voices per player.

**Mobile speaker rule:** All audio identity must be carried in the 300–3000Hz range. Physical hit cracks concentrate in 2–4kHz (most reliably reproduced range on phone speakers). No sub-bass design; use iOS haptics for sub-100Hz feedback instead. Validate all assets on iPhone 12 phone speaker before locking.

## UI Requirements

**Governing Principle:** All touch targets respect iOS HIG 44×44pt minimum. All positions anchor to `Screen.safeArea` bounds, not `Screen.width/height`. Touch-down (not touch-up) triggers cast submission.

---

**Skill Bar Layout:**

Location: lower-right corner, anchored to `safeArea.xMax - 12pt` (right) and `safeArea.yMin + 12pt` (bottom). Respects the Dynamic Island right-side inset on iPhone 14 Pro+ in landscape.

2-column × up to 5-row grid. Button size 56×56pt, 8pt gap both axes. Filled bottom-to-top, right-to-left. Total footprint: 120pt wide × up to 312pt tall at 10 skills.

The grid renders all 10 slot positions from level 1 — locked skills are visible at 20% alpha with lock badge. The grid never collapses. This shows the full progression path upfront and sets expectation (Korean MMO convention aligned with Earned Power pillar).

| Skills Unlocked | Rows Active | Height |
|----------------|-------------|--------|
| 1–2 | 1 | 56pt |
| 3–4 | 2 | 120pt |
| 5–6 | 3 | 184pt |
| 7–8 | 4 | 248pt |
| 9–10 | 5 | 312pt |

Lowest row is most thumb-reachable; early-unlocked skills naturally populate it. Rows 4–5 (skills 7–10) are less thumb-reachable — flag for playtest ergonomics check at full 10-skill loadout.

---

**Target Selection:**

- `SingleHostile` — tap an enemy in the world (touch-down). Sets hostile target, reveals Target Frame. Enemy world-space colliders must be minimum 44×44pt; inflate on small enemies. Tapping empty space clears hostile target.
- `SingleFriendly` — tap a party portrait in the Party Frame (touch-down). Sets friendly target; portrait receives selection highlight. Tapping currently selected portrait de-selects.
- `Self` — no target interaction required. Server auto-resolves to `casterEntityID`. Tapping a `Self` skill submits immediately.

Hostile and friendly target slots are independent. The Healer uses both: Holy Smite uses `SingleHostile` (tap enemy); heals and buffs use `SingleFriendly` (tap party portrait).

---

**Skill Tap Interaction:**

- **Single tap (touch-down)** — submits cast request immediately. Touch-down submission eliminates the micro-latency gap that becomes perceptible during rhythmic skill rotation.
- **Long-press (≥500ms, no movement)** — opens skill tooltip. Cast request is NOT submitted on long-press. Timer-based: if touch duration exceeds 500ms without significant movement, suppress cast and show tooltip instead.
- **Double-tap** — treated as two separate taps. No special behavior; second tap submits a second cast request.
- **Touch consumer priority** — skill buttons consume touch-down first. If touch-down does not land within a skill button bounding box, the event passes to the camera drag zone. No gesture conflict.

---

**Error Feedback:**

| Rejection | Visual Response | Duration |
|-----------|----------------|---------|
| `OnCooldown` | Skill button horizontal shake — 3 cycles, ±4pt displacement | 200ms |
| `InsufficientMana` | Skill button shake + mana bar 2-flash (150ms on/off each) | 200ms + 300ms |
| `InvalidTarget` / `OutOfRange` | Skill button shake + target frame outline pulse (150ms) if target is empty | 200ms |

All error animations complete within 300ms and do not block subsequent input. No red flash on the skill button — the clockwise sweep already communicates cooldown state; a red flash would be semantically redundant. No error response for tapping locked skills.

---

**Skill Tooltip (Long-Press):**

Appears adjacent to the pressed button (above or left, whichever has more space). Content:
- Skill name (bold)
- Mana cost (value + "MP")
- Cooldown duration (value + "s", from `CooldownTicks × 0.05`)
- Target type ("Single Enemy", "Single Ally", or "Self")
- One-line effect description (max 60 characters)

Dismisses on any tap outside the tooltip, on long-press release, or after 4 seconds. Does not block cast input — tapping another skill button while tooltip is open closes it and submits the cast.

Skill rank, unlock level, and lore text are NOT in the combat tooltip. That information belongs in the Skill Book menu (out of scope for combat HUD).

---

**Locked Skill Tap:**

A small label appears adjacent to the tapped button: "Unlocks Lv. [N]". Fades after 2 seconds. No shake, no rejection audio — locked skills are not errors; they are unreached content. Label appears on the opposite side if near a screen edge.

---

**Party Frame (Healer Friendly Targeting):**

Location: upper-left corner, anchored to `safeArea.xMin + 12pt` (left) and `safeArea.yMax - 12pt` (top). Portraits stacked vertically, top-to-bottom.

Portrait tap target: 64×64pt (larger than HIG minimum; justified by precision required for reactive Healer target-switching). Portrait art may render at 52pt within 64pt tap area.

MVP party size: 2 (Warrior + Healer). Healer's own portrait is first (top) for consistent self-targeting thumb position. Layout accommodates up to 6 portraits without repositioning the frame anchor.

Target switching: tap any portrait to switch (touch-down, no confirm step). Tap current portrait to de-select.

---

**HUD Placement Summary:**

```
LANDSCAPE SCREEN (safe area bounds)

+----------------------------------------------------------------+
| [Party Frame]              [Target Frame]                       |
| safeArea TL +12pt          safeArea top-center +12pt           |
| 64×64pt portraits          ~200×60pt                           |
| Stacked vertically                                             |
|                                                                |
|   [Left half — joystick spawn zone]  [Right half — camera drag]|
|                                                                |
|                                      [Skill Bar]               |
|                                      safeArea BR +12pt         |
|                                      2-col × 5-row             |
|                                      120pt wide, 312pt tall max|
|                                                                |
| [HP/MP Bars — safeArea bottom-center]                          |
+----------------------------------------------------------------+
```

Dynamic Island (iPhone 14 Pro+): safe area anchoring handles the ~59pt right-side inset automatically in landscape-right orientation. Skill bar must anchor to `Screen.safeArea.xMax`, not `Screen.width`. Confirm Unity build has safe area support enabled.

| Element | Tap Target | HIG Compliant |
|---------|-----------|--------------|
| Skill button | 56×56pt | Yes |
| Party portrait | 64×64pt | Yes |
| Enemy in world | ≥44×44pt via inflated collider | Yes (enforce in implementation) |
| Locked skill button | 56×56pt | Yes |

## Acceptance Criteria

All criteria are binary pass/fail and independently verifiable by a QA tester.

---

**Validation Sequence**

**AC-SK-01:** A cast request that fails any of V-1 through V-8 results in zero mana deducted from the caster; verified by reading caster mana before and after the rejected cast and confirming the values are identical.

**AC-SK-02:** A cast request that fails any of V-1 through V-8 results in the skill remaining in `Ready` state (`CooldownExpiryTick` unchanged); verified by querying the skill instance after rejection and confirming no cooldown was written.

**AC-SK-03:** A cast request that fails any validation step does not trigger `CombatSystem.NotifySkillUsed`; verified by confirming `_actionTimer` on the caster's auto-attack state machine is unchanged after the rejection (not reset to 0.0 by the rejected cast).

**AC-SK-04:** A cast request failing V-2 (stale tick) produces no `SkillCastResult` message to any client and generates a server-side security log entry; verified by submitting a request with `clientTickNumber = serverTick - (CAST_STALE_TICK_TOLERANCE + 1)` and asserting no broadcast and a log entry.

**AC-SK-05:** A cast failing V-6 (on cooldown) produces `SkillCastResult` with `castAccepted == false` and `rejectionCode == OnCooldown`, and modifies neither mana, cooldown state, nor auto-attack timer (`_actionTimer`); verified with four independent assertions on a single rejected cast.

---

**Cooldown Model**

**AC-SK-06:** A skill in `OnCooldown` state is rejected at V-6 when `serverCurrentTick < CooldownExpiryTick`; verified by submitting a cast on tick N after a successful cast that wrote `CooldownExpiryTick = N + 20`, and asserting rejection code `OnCooldown`.

**AC-SK-07:** A skill transitions from `OnCooldown` to `Ready` at exactly the tick where `serverCurrentTick >= CooldownExpiryTick`; verified by submitting a cast on the tick equal to `CooldownExpiryTick` and confirming it passes V-6.

**AC-SK-08:** After a successful cast, two different skills on the same entity carry independent `CooldownExpiryTick` values; verified by casting Skill A and confirming Skill B's `CooldownExpiryTick` is unchanged.

**AC-SK-09:** A `SkillCooldownSnapshot` covering all 10 skill slots is sent on zone join; verified by joining a zone with at least one skill on cooldown and confirming the snapshot message contains the correct `ticksRemaining` for that skill.

---

**Level Unlock Gating**

**AC-SK-10:** A skill with `UnlockLevel = N` has `IsUnlocked == false` when `OnEntitySpawned` is called with an entity at level `N - 1`; verified by spawning such an entity and querying `skillInstance.IsUnlocked`.

**AC-SK-11:** A skill with `UnlockLevel = N` has `IsUnlocked == true` immediately after `OnLevelUp(entityID, N)` fires; verified by querying `skillInstance.IsUnlocked` before the next tick processes any cast requests.

**AC-SK-12:** A cast request for a `Locked` skill returns `rejectionCode == SkillLocked`, consumes no mana, and registers no cooldown; verified with three independent assertions on a single rejected cast.

**AC-SK-13:** Every `SkillDefinition` with `UnlockLevel > entity.Level` has `IsUnlocked == false` immediately after `OnEntitySpawned` completes; verified by iterating all 10 skill slots and asserting the invariant for a freshly spawned level-1 entity.

**AC-SK-14:** `IsUnlocked` never reverts from `true` to `false`; verified by calling `OnLevelUp` with increasing levels and asserting no unlock flag decrements.

---

**Mana Cost**

**AC-SK-15:** On a successful cast, mana is deducted before any damage or effect step executes; verified by reading mana immediately after V-9 passes (before CR-SK-4) and confirming the deduction equals `Ceil(ManaCost × MANA_COST_MULTIPLIER)`. *Note: requires a test seam between V-9 and CR-SK-4; flag as testability concern for lead programmer.*

**AC-SK-16:** When a skill results in a miss (zero `DamageDealt`), no mana is refunded; verified by recording mana before and after a missed hit and confirming the difference equals the effective mana cost.

**AC-SK-17:** With `MANA_COST_MULTIPLIER = 0.75` and `ManaCost = 100`, the value passed to `ConsumeMana()` is exactly 75; verified by intercepting the `ConsumeMana` call argument.

**AC-SK-18:** With `MANA_COST_MULTIPLIER = 1.5` and `ManaCost = 101`, the value passed to `ConsumeMana()` is exactly 152 (`Ceil(151.5) = 152`); verified by intercepting the `ConsumeMana` call argument.

**AC-SK-19:** `MANA_COST_MULTIPLIER` is read from `assets/data/SkillSystemConfig.asset` at cast time and is not hardcoded; verified by changing the asset value without a code recompile and confirming the effective cost changes on the next cast.

---

**Skill Execution Path**

**AC-SK-20:** A `PhysicalDamage` skill calls `DamageCalculation()` with `damageContext == DamageContext.PhysicalSkill`; verified by intercepting the call and asserting the context argument.

**AC-SK-21:** A `MagicalDamage` skill calls `DamageCalculation()` with `damageContext == DamageContext.MagicalSkill`; verified by intercepting the call and asserting the context argument.

**AC-SK-22:** For a `PhysicalDamage` skill, `BaseDamage = floor(AP × DamageMultiplier)`; verified by fixing AP and DamageMultiplier to known values and asserting the intercepted argument matches the formula.

**AC-SK-23:** For a `MagicalDamage` skill, `BaseDamage = floor(INT × DamageMultiplier)` and `StatID.AttackPower` is never read; verified by asserting the formula result AND confirming no `GetEffectiveStat(_, StatID.AttackPower)` call occurs on the magical damage path.

**AC-SK-24:** A `PhysicalDamage` skill with `AP = 650` and `DamageMultiplier = 1.5` passes `BaseDamage = 975` to `DamageCalculation()`; verified by fixing both inputs and asserting the intercepted argument.

**AC-SK-25:** A `MagicalDamage` skill with `INT = 246` and `DamageMultiplier = 1.5` passes `BaseDamage = 369` to `DamageCalculation()`; verified by fixing both inputs and asserting the intercepted argument.

---

**Status Effects Delegation**

**AC-SK-26:** `StatusEffects.ApplyEffect()` is not called until after `DamageCalculation()` returns, for any skill combining a damage flag with `ApplyBuff` or `ApplyDebuff`; verified by recording the call sequence and asserting `DamageCalculation` return precedes `ApplyEffect` invocation.

**AC-SK-27:** `StatusEffects.ApplyEffect()` is not called on a target for which `DamageResult.IsKill == true` in the same tick; verified by staging a kill-producing hit and confirming `ApplyEffect` is not invoked for that target entity.

**AC-SK-28:** The Skill System never calls `AddBuffModifier()` directly; verified by text-searching the Skill System implementation files for any direct invocation and asserting zero matches. *Best enforced as a CI static analysis rule, not a runtime test.*

**AC-SK-29:** `ApplyInstantHeal()` is called with `healAmount = floor(INT × HealMultiplier)`; verified by fixing `INT = 246` and `HealMultiplier = 2.0` and asserting the intercepted argument equals 492.

---

**Auto-Attack Cadence**

**AC-SK-30:** After a successful skill cast, the caster's auto-attack `_actionTimer` resets to 0.0; verified by reading `_actionTimer` before the cast (any value in `[0, CycleDuration]`) and asserting the value is 0.0 immediately after `NotifySkillUsed()` is called, before any further tick advance.

**AC-SK-31:** `CombatSystem.NotifySkillUsed(casterEntityID)` is called exactly once per accepted cast, after validation passes but before damage resolution; verified by asserting a count of 1 per cast across 10 casts, and by confirming call order relative to `DamageCalculation`.

**AC-SK-32:** A cast rejected at any validation step does not result in a call to `CombatSystem.NotifySkillUsed`; verified for each rejection code by staging the failure condition and asserting zero invocations.

---

**SkillID Type**

**AC-SK-33:** `SkillID` is a `readonly struct` wrapping a single `uint` field; verified by reflection — asserting `typeof(SkillID).IsValueType == true`, `GetFields()` returns exactly one field of type `uint`, and the type is `readonly`.

**AC-SK-34:** A `SkillCastRequest` with `skillID == 0` (i.e., `SkillID(0)`) is rejected at V-3 with `rejectionCode == InvalidSkill`; verified by submitting such a request and asserting the rejection code.

**AC-SK-35:** `ClassDefinition.SkillList` is declared as `SkillID[]`; verified by reflection — asserting `classDefinition.GetType().GetField("SkillList").FieldType == typeof(SkillID[])`.

---

**OQ-CS-1 Class Contracts**

**AC-SK-36:** The Healer class has exactly one skill with `SkillEffectType` flag `MagicalDamage` and `UnlockLevel == 1`; verified by iterating `IClassRegistry.GetClassSkills(ClassType.Healer)`, filtering for `UnlockLevel == 1`, and asserting exactly one entry has the `MagicalDamage` flag.

**AC-SK-37:** The Healer's level-1 `MagicalDamage` skill reads `StatID.Intelligence` and never reads `StatID.AttackPower` when computing `BaseDamage`; verified by intercepting `GetEffectiveStat` calls on that skill's cast path and asserting no call with `StatID.AttackPower` occurs.

**AC-SK-38:** The Warrior class has at least one skill with `UnlockLevel == 1` and `SkillEffectType` flag `PhysicalDamage`; verified by iterating `IClassRegistry.GetClassSkills(ClassType.Warrior)` and asserting at least one entry satisfies both conditions.

---

**Targeting Constraint Enforcement**

**AC-SK-39:** A `SingleHostile` skill cast targeting a friendly entity is rejected at V-7 with `rejectionCode == InvalidTarget`, with no mana, cooldown, or auto-attack state changes; verified with four independent assertions.

**AC-SK-40:** A `SingleFriendly` skill cast targeting a hostile entity is rejected at V-7 with `rejectionCode == InvalidTarget`, with no mana, cooldown, or auto-attack state changes; verified with four independent assertions.

**AC-SK-41:** A `Self` skill cast succeeds regardless of the `targetEntityID` submitted (including `0` or a hostile entity ID), and the effect resolves on the caster; verified by submitting with `targetEntityID = 0` and with a hostile entity ID respectively and asserting both casts succeed with the caster as the effect recipient.

---

**Client Communication**

**AC-SK-42:** The caster receives a `SkillCastResult` for every cast request that passes V-1 and V-2, whether accepted or rejected; verified by submitting 5 accepted and 5 rejected casts (at V-3 through V-9) and asserting the caster receives exactly 10 `SkillCastResult` messages.

**AC-SK-43:** V-1 and V-2 failures produce no `SkillCastResult` message to any client; verified by submitting a mismatched entity ID (V-1) and a stale tick number (V-2) and asserting zero `SkillCastResult` broadcasts.

**AC-SK-44:** A `SkillCooldownSnapshot` containing all 10 skill slots with correct `ticksRemaining` values is sent during zone join; verified by capturing all messages during zone join with at least one skill on cooldown and asserting 10-entry snapshot with correct values.

**AC-SK-45:** A `SkillUnlockNotification { skillID }` is sent to the client when `OnLevelUp` causes a skill's `IsUnlocked` to transition from `false` to `true`; verified by triggering a level-up that unlocks exactly one skill and asserting exactly one notification containing the correct `skillID`.

**AC-SK-46:** No `SkillUnlockNotification` is sent when a level-up crosses no unlock threshold; verified by triggering such a level-up and asserting zero `SkillUnlockNotification` messages.

---

**OnEntitySpawned**

**AC-SK-47:** Immediately after `OnEntitySpawned(entityID, ClassType.Warrior)` completes for a level-1 entity, every skill with `UnlockLevel == 1` has `IsUnlocked == true` and every skill with `UnlockLevel > 1` has `IsUnlocked == false`; verified by iterating all 10 entries and asserting the predicate for each.

**AC-SK-48:** `OnEntitySpawned` called with a `ClassType` for which `GetClassSkills()` returns an empty collection allocates a `SkillInstance[0]` array without throwing; verified by invoking with such a class type and asserting no exception is raised and the skill array has length 0.

---

**Tick-Phase Ordering**

**AC-SK-49:** When the Leveling System fires `OnLevelUp(entityID, N)` and a `SkillCastRequest` for a skill with `UnlockLevel == N` is processed on the same server tick T, the cast succeeds at V-5 (`IsUnlocked == true`); verified by staging `OnLevelUp` and the cast request in the same tick with Leveling System processing occurring before Skill System cast processing, and asserting the cast is accepted rather than rejected with `SkillLocked`. This test encodes the required tick-phase ordering constraint from EC-SK-6 as an executable invariant.

## Open Questions

**OQ-SK-1 — RESOLVED.** Full skill roster for both classes authored in the Skill Roster section above. Blocking state for `SkillDatabase.asset` authoring is lifted. See design notes in that section regarding level distribution and War Cry AP buff.

**OQ-SK-2 — RESOLVED.** Per-skill cooldown and mana cost values authored in the Skill Roster section. All values are initial tuning targets subject to playtest adjustment within the safe ranges defined in CR-SK-14, CR-SK-15, and the Tuning Knobs section.

**OQ-SK-3 — RESOLVED (2026-05-28).** `DamageContext.MagicalSkill` added to `damage-calculation.md`. Maps to `DamageType.Magical` (wire value 1). Formula pipeline is identical to `PhysicalSkill` at MVP. Skill System implementation is unblocked.

**OQ-SK-4 — Healer INT at L60 confirmation.** Example calculations use INT=246 for the Healer L60 value. This must be verified against the Character Stats GDD formula before `SkillDatabase.asset` authoring begins.

**OQ-SK-5 — BLOCKING — Leveling System GDD tick-phase ordering.** EC-SK-6 requires the Leveling System to fire `OnLevelUp` before Skill System cast processing within the same tick. The Leveling System GDD's tick-phase ordering has not been confirmed. Blocking for: EC-SK-6 correctness guarantee. Must be resolved before skill-system.md can be approved — implementation cannot begin until the ordering contract is documented in both GDDs.

**OQ-SK-6 — BuffID values for Skill System buff-granting skills.** The `BuffID enum : uint` registry entry defers specific value assignment to this GDD. No Warrior or Healer buff/debuff BuffID values have been authored. Required before buff-granting skill entries in `SkillDatabase.asset` can be completed.

**OQ-SK-7 — Skill unlock audio timing coordination.** The unlock chime fires 300ms after the Leveling System's level-up audio. The Leveling System's audio spec does not yet exist. Non-blocking for implementation; blocking for audio asset finalization.

**OQ-SK-8 — Maximum party size at launch.** Party Frame UI is designed for 2-player MVP with headroom for up to 6. If launch party size is 5 or 6, the Party Frame vertical height must be validated before UI implementation begins.

**OQ-SK-9 — RESOLVED (2026-05-27).** `auto-attack-combat.md` revised to reactive cooldown model and approved. `_actionTimer` (float) is now the shared timer variable across both GDDs. All `_skillUsedThisCycle` flag logic, grace windows, and `SKILL_COMMITTED` state removed from auto-attack-combat.md.
