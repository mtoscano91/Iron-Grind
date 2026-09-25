# Leveling System

> **Status**: Approved (2026-05-05 — pass 5 review; 8 edits applied; creative-director cleared for APPROVED; sprint gate on OQ-LS-7 + economy-designer sign-off + networking-core.md approval)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-28 (Skill System amendment: added to downstream dependents table; CR-2.10 tick-phase ordering constraint documented; cross-doc back-reference added)
> **Implements Pillar**: Earned Power — levels are receipts for time invested; Social Gravity — party play accelerates progress

## Overview

The Leveling System tracks character experience points and drives all stat growth that occurs when a player levels up. It owns the XP threshold table (L1–60), receives threshold-crossed notifications from Character Stats after each XP grant from Damage Calculation, and responds by writing updated primary attributes and derived base stats back to Character Stats via `SetBaseStat()`. At every level-up the player receives 5 attribute points — Warrior: 4 auto-allocated (+2 STR, +1 VIT, +1 DEX) + 1 free; Healer: 3 auto-allocated (+1 VIT, +2 INT) + 2 free — with free points held until allocated through the stat screen. At milestone levels 20, 40, and 60 the LevelTierMultiplier advances; the Leveling System fully re-derives all dependent base stats from accumulated totals with the new multiplier, creating discrete power spikes that mark the progression arc. The MVP level cap is 60; at cap, Experience locks and the HUD displays "MAX". The system also owns the respec mechanic: a rare item allows the player to reallocate previously spent free stat points via a transacted batch rewrite.

## Player Fantasy

A level in Iron Grind is a receipt. You didn't earn it from a story beat or a cutscene — you earned it by killing the same mobs, in the same zone, until the bar filled. When the level fires, the game stops for a moment. The XP bar empties. Five points land in your stat sheet. The screen opens and shows you, in numbers, what those hours bought. Your level floats above your head and every player in the zone can read it — it is a credential, not decoration.

The fantasy is not surprise. It is **confirmation**. You knew this was coming. You watched the bar climb for the last forty minutes. The level-up pays you back, visibly, in stats you chose where to spend.

**The Held Points** *(Earned Power)*: After leveling up you hold uncommitted free points — Warrior holds 1, Healer holds 2. You could spend them now. But you know your build; you might be waiting for a stat breakpoint, or for next level's auto-alloc to check first. The points sit in your pocket like currency. That decision — to hold, to plan, to commit deliberately — is yours. The game respects it and waits.

*Warrior and Healer hold differently: Warriors accumulate 1 point per level (59 at cap) — fewer, because their build identity is more tightly defined by auto-alloc. Healers accumulate 2 per level (118 at cap) — more, because balancing VIT and INT is a deeper per-build question. Both classes have the "points as currency" experience; the scale of the currency differs by design.*

**The Tier Wall** *(Earned Power, Rhythm Mastery)*: At L20, L40, and L60 the tier advances. MaxHP, AttackPower, CritChance, and attack cadence all jump at once. The mob that took twelve minutes per kill drops in three swings. You don't celebrate. You just notice. Then you pull something harder — and for a moment you remember the last zone you struggled in. You left it behind before you realized. The tier walls are the moments when the game shows you what you've become.

**The Level Above Your Head** *(Social Gravity)*: Your level is publicly visible — it floats above your name, readable by every player you pass. When you walk through the starter zone at L48, newer players see what the grind produces. The number is a posture.

*Pillar alignment: Earned Power — every level is a documented return on time invested; Rhythm Mastery — tier milestone jumps are the most direct expression of combat-stat growth; Social Gravity — the level is a public credential in a world built around visible hierarchy.*

## Detailed Design

### Core Rules

#### CR-1 — XP Accumulation Flow

**CR-1.1** On a kill (`DamageResult.IsKill = true`), the killer's controller — Auto-Attack Combat or Skill System, never Damage Calculation itself (see `damage-calculation.md`'s Option B decision) — calls `LevelingSystem.GetXPAward(TargetID): int` to determine the XP amount, then calls `AddExperience(killerEntityID, amount)` on the killing player entity. This is the sole XP faucet at MVP. *(Corrected 2026-09-24, OQ-LS-7 resolution — this rule previously said "Damage Calculation calls AddExperience," which contradicted `damage-calculation.md`'s already-Approved Option B text. See OQ-LS-7 below for `GetXPAward`'s full specification.)*

**CR-1.2** `AddExperience()` is owned by Character Stats. It adds `amount` to `StatID.Experience`, fires `OnStatChanged(EntityID, StatID.Experience)`, then calls `ILevelingSystemListener.OnExperienceThresholdCrossed(EntityID)` synchronously if the new total meets or exceeds the threshold for `Level + 1`. The Leveling System is the sole registered listener; only one system may register. Calling `RegisterListener()` when a listener is already registered throws `InvalidOperationException` in dev builds; logs an error and no-ops in release builds.

**CR-1.3** XP is accumulated as a monotonic running total in `StatID.Experience`. There is no "XP within current level" value stored. The HUD bar fill is derived at display time: `fill = (float)(Experience − XpThreshold[Level]) / (float)(XpThreshold[Level+1] − XpThreshold[Level])`. Both operands must be cast to `float` before division — C# integer division produces `0` for any non-threshold state if the cast is omitted.

**CR-1.4** `AddExperience()` with `amount ≤ 0` is rejected by Character Stats with an error log and no write. `AddExperience()` targeting a mob entity is a no-op (mob Experience is inert — see Character Stats EC-28).

**CR-1.5** Party XP bonus computation is owned by the Party System. The Leveling System receives only per-player final XP grants and is not party-aware.

---

#### CR-2 — Level-Up Sequence

When `OnExperienceThresholdCrossed(EntityID)` fires, the Leveling System executes the following steps in exact order. No reordering is permitted.

**CR-2.1 — At-Cap Guard:** If `GetBaseStat(StatID.Level) == 60`, abort. No level-up fires. Experience is clamped to `XpThreshold[60]` (see CR-5).

**CR-2.2 — Increment Level:** `SetBaseStat(EntityID, StatID.Level, currentLevel + 1)`.

**CR-2.2a — At-Cap XP Clamp (conditional):** If `newLevel == 60`, write `SetBaseStat(EntityID, StatID.Experience, XpThreshold[60])` immediately after CR-2.2. This clamps any overshoot within the level-up sequence so `GetBaseStat(Experience)` equals `XpThreshold[60]` for all subsequent steps and for the HUD. Without this step, the HUD bar fill formula (CR-1.3) would produce near-zero fill until the next `AddExperience()` call fires CR-5.2. This is the canonical clamp path for the L59→L60 transition; CR-5.2 handles subsequent kills.

**CR-2.3 — Determine LevelTierMultiplier:** Look up the multiplier for the new level (L1–19: ×1.0, L20–39: ×1.2, L40–59: ×1.5, L60: ×2.0). Flag if this is a tier transition: `isTierTransition = (newLevel == 20 || newLevel == 40 || newLevel == 60)`.

**CR-2.4 — Auto-Allocate Attribute Points:**
Read `ClassDefinition def` via `IClassRegistry.TryGetClass(classType, out def)`, where `classType` is the value cached per-entity during `InitializeAtL1()` (see CR-6.1 and Class System GDD LC-2). For each stat in {STR, DEX, VIT, INT}, if `def.AutoAllocIncrement[stat] > 0`, call `SetBaseStat(EntityID, stat, GetBaseStat(EntityID, stat) + def.AutoAllocIncrement[stat])`. This produces 3 SetBaseStat calls for Warrior (STR +=2, VIT +=1, DEX +=1) and 2 for Healer (VIT +=1, INT +=2), using authoritative values from `ClassDefinition`. Numeric values are not hardcoded here — they are owned by the Class System (see Class System GDD LC-1).

Each call fires `OnStatChanged` for the affected attribute.

**CR-2.5 — Read Current Primary Attribute Totals:**
Read `STR`, `DEX`, `VIT`, `INT` via `GetBaseStat()` after CR-2.4 (so auto-alloc is included in the derivation).

**CR-2.6 — Recompute Derived Base Stats:**
Evaluate F-3 through F-9 from the CR-2.5 totals using the new `LevelTierMultiplier`. Write each result via `SetBaseStat()`. Evaluation order:

1. `MaxHP = Mathf.FloorToInt((200 + VIT × 20) × tier)` → SetBaseStat
2. `MaxMP = min(Mathf.FloorToInt((100 + INT × 12) × tier), 9999)` → SetBaseStat *(Leveling System clamps to ≤9,999 before calling — see MaxMP schema ceiling in Character Stats)*
3. `AttackPower = Mathf.FloorToInt((10 + STR × 2) × tier)` → SetBaseStat
4. `Defense = Mathf.FloorToInt((5 + VIT × 1.5) × tier)` → SetBaseStat
5. `MagicDefense = Mathf.FloorToInt(INT × 0.4 × tier)` → SetBaseStat
6. `CritChance = 0.05f + (DEX × 0.0015f × tier)` → SetBaseStat *(Character Stats' F-1 clamp ≤0.75 applied at query time; Leveling System writes raw output)*
7. `AttackSpeedMultiplier = 1.0f + (DEX × 0.003f × tier)` → SetBaseStat *(Character Stats clamps to [0.5, 2.0] at query time)*

**CR-2.7 — Full HP/MP Restore:** After all derived stats are written:
- `SetBaseStat(EntityID, StatID.CurrentHP, GetBaseStat(EntityID, StatID.MaxHP))`
- `SetBaseStat(EntityID, StatID.CurrentMP, GetBaseStat(EntityID, StatID.MaxMP))`

*This is the only time the Leveling System writes CurrentHP or CurrentMP. SetBaseStat is used here (not ApplyRegen) because a level-up is a discrete full-state reset, not a regen delta — the same mechanism used during spawn initialization. GetBaseStat(MaxHP) must return the value just written in CR-2.6; Character Stats must not have a deferred propagation path.*

**CR-2.8 — Grant Free Points:** `heldFreePoints += freePointsPerLevel[class]` (Warrior: 1; Healer: 2). `heldFreePoints` is owned by the Leveling System, not stored in Character Stats. It is persisted separately as part of the Leveling System's serialized state. **`heldFreePoints` and the Level write (CR-2.2) must be committed to the database atomically in a single transaction** — a server crash between the two writes must not produce a state where Level advanced without `heldFreePoints` incrementing (free point lost) or `heldFreePoints` incremented without Level advancing (free point gained spuriously).

**CR-2.9 — Consecutive Level-Up Check:** Re-evaluate whether `Experience >= XpThreshold[newLevel + 1]`. If yes, repeat from CR-2.1. This handles single large XP grants that span multiple level thresholds (most common at very low levels). Each iteration reads fresh attribute totals after its own auto-alloc writes.

**CR-2.10 — Broadcast Level-Up Complete:** Fire `ILevelingEventBroadcaster.OnLevelUp(EntityID, newLevel)` after ALL consecutive level iterations (CR-2.1–CR-2.9) are complete — not after each individual iteration. `GetBaseStat(Level)` must reach its final value before any broadcast fires. For three consecutive levels gained (e.g., L3→L4→L5→L6): all three iterations execute to completion, then `OnLevelUp(EntityID, 4)`, `OnLevelUp(EntityID, 5)`, and `OnLevelUp(EntityID, 6)` fire in sequence. Subscribers: HUD (level-up animation), Audio (level-up sound), Skill System (updates `skillInstance.IsUnlocked` flags; sends `SkillUnlockNotification` to client for newly unlocked skills). (Zone social system announcement deferred — see OQ-LS-6.) **Tick-phase ordering constraint:** All `OnLevelUp` notifications must complete before the Skill System processes cast requests within the same server tick. This guarantees a skill unlocked on tick T has `IsUnlocked == true` by the time V-5 validation runs on tick T. See `skill-system.md` EC-SK-6, AC-SK-49.

**Broadcast exception policy:** Subscriber exceptions during `OnLevelUp` are caught per-subscriber, logged, and do not interrupt remaining subscribers or subsequent levels in a consecutive level-up sequence. A single failing subscriber never blocks the broadcast loop.

**HUD implementer note:** In a consecutive level-up scenario, `OnStatChanged` events for all levels precede all `OnLevelUp` broadcasts (see EC-LS-37). HUD must key level-up animation start to `OnLevelUp`, not to `OnStatChanged(StatID.Level)`. `OnLevelUp` is also the canonical signal for network replication of level-up state — the networking layer must not replicate per-`OnStatChanged` during a level-up sequence.

**Network consistency contract:** Level badge and all F-3–F-9 derived stats must appear updated atomically to all observers — within one visible frame delta on the client. The Leveling System owns this contract; the wire protocol and client-side state machine that fulfil it are specified in the Network Architecture ADR (owned by network-programmer). The networking layer may use `_levelingUpInProgress` (exposed as `IsLevelingUpInProgress` read-only property) to suppress per-`OnStatChanged` replication during a level-up sequence.

---

#### CR-3 — Free Point Allocation

**CR-3.1** `heldFreePoints` is initialized to 0 at spawn. It increments per CR-2.8 at each level-up. It is never negative and has no per-level expiry — uncommitted points persist indefinitely. Maximum held: Warrior 59, Healer 118 (if no points ever spent by L60).

**CR-3.2** Allocation path: the stat screen calls `AllocateFreePoint(EntityID, StatID targetStat)`. Valid targets: STR, DEX, VIT, INT only.
1. Guard: `heldFreePoints == 0` → reject with UI feedback; no write.
2. Guard: `targetStat` not in {STR, DEX, VIT, INT} → reject with error log; no write.
3. `heldFreePoints -= 1`
4. `SetBaseStat(EntityID, targetStat, GetBaseStat(EntityID, targetStat) + 1)`
5. Re-evaluate F-3 through F-9 with current `LevelTierMultiplier` (derived from `GetBaseStat(StatID.Level)`), write via `SetBaseStat()` — same sequence as CR-2.6.
6. **HP and MP are NOT restored** when spending free points. CurrentHP and CurrentMP are unchanged.

**CR-3.3** Each `AllocateFreePoint()` call is immediately committed. There is no preview-then-confirm step at the Leveling System layer; if the UI wants a preview mode, it implements it above this API. **UX requirement:** Any client surfacing this API on a touch platform must implement a preview-and-confirm flow before calling `AllocateFreePoint()` — immediate-commit with no undo path is a misclick trap on touch inputs. The player-facing flow specification belongs in the UX GDD (`design/ux/`).

---

#### CR-4 — Respec Sequence

**CR-4.1** Respec uses a two-phase commit to prevent item loss from infrastructure failures (network drops, server crashes). Phase 1 — Gate and Reserve: the Inventory System calls `HasCombatTaggedEffect(EntityID)`; if in combat the call fails and the item is not touched. If clear, the item is marked reserved (removed from active inventory into a reserved hold slot managed by the Inventory System). **Reservation TTL: 30 seconds.** If Phase 2 is not received within 30 seconds of Phase 1 completing (e.g., mobile network drop), the Inventory System automatically calls `ItemReservation.Release()` — the item returns to active inventory and the reservation is cleared. Phase 2 — Commit: the Inventory System calls `LevelingSystem.TryApplyRespec(EntityID)`, which executes CR-4.3–CR-4.4 and may throw on failure. (CR-4.2 — presenting the respec screen to the player — is triggered by Phase 1 completing successfully; it occurs between Phase 1 and Phase 2 and is not part of `TryApplyRespec()`. The Inventory System owns the `ItemReservation` handle throughout. On successful return from `TryApplyRespec()`, the Inventory System calls `ItemReservation.Consume()` — the item is permanently destroyed. On any exception from `TryApplyRespec()`, the Inventory System calls `ItemReservation.Release()` — the item is returned to active inventory. The player loses the item only when the commit fully succeeds. **Reconnect recovery:** On player reconnect, the server checks for any active reservation for that entity. If a reservation exists and the TTL has not expired, the server re-presents the respec screen (re-enters Phase 2) and surfaces the remaining TTL seconds to the client. If the TTL has expired, the server calls `Release()` and notifies the client ("respec scroll returned to inventory"). Reservation state is persisted to the database during Phase 1 so it survives a server crash. **Accepted limitation — allocation state lost on disconnect:** If the player had configured a new allocation in the respec screen before disconnecting, that state is not preserved server-side — it existed only in the client UI. On reconnect the player must re-configure from scratch within the remaining TTL. The item is always safe (returned on TTL expiry if re-confirmation does not complete in time). See OQ-LS-1 (resolved).

**CR-4.2** `BeginRespec()` opens the stat reallocation screen. No stats change until the player confirms. The screen displays points available to redistribute = `spentFreePoints = (Level − 1) × freePointsPerLevel[class] − heldFreePoints` (Warrior at L60 with no held points: 59; Healer at L60 with no held points: 118). **`heldFreePoints` are NOT included in the redistributable pool** — they remain held and unchanged after commit. Per-attribute floors are enforced in real time — the player cannot commit an allocation below any attribute's auto-alloc minimum.

**CR-4.3** Auto-alloc floors at level L (non-redistributable). Read from `IClassRegistry`: `floor[stat] = 10 + (L − 1) × def.AutoAllocIncrement[stat]`. For the current MVP classes:
- Warrior: STR floor = 10 + (L−1)×2; DEX floor = 10 + (L−1)×1; VIT floor = 10 + (L−1)×1
- Healer: VIT floor = 10 + (L−1)×1; INT floor = 10 + (L−1)×2
- Non-auto-allocated attributes (INT for Warrior; STR, DEX for Healer) have `AutoAllocIncrement = 0` → floor = 10.

Numeric values are derived from `ClassDefinition.AutoAllocIncrement` — not hardcoded. Adding a new class requires no changes to this formula.

**CR-4.4** Respec commit sequence:
1. `CharacterStats.BeginStatTransaction()` — defers all `OnStatChanged` events
2. For each primary attribute: `SetBaseStat(EntityID, statId, autoAllocTotal[statId] + newFreeAlloc[statId])`
3. Re-evaluate F-3 through F-9 with current `LevelTierMultiplier`, write via `SetBaseStat()`
4. `heldFreePoints` UNCHANGED — held unspent points are not part of the redistributable pool (CR-4.2) and survive respec with the same value
5. `CharacterStats.EndStatTransaction()` — all deferred `OnStatChanged` fire once per modified stat

**CR-4.5** HP/MP are NOT restored by respec. If the respec reduces MaxMP below CurrentMP, Character Stats' EC-05 reconciliation fires inside `SetBaseStat(MaxMP)` during step 3 — the reconciliation is Character Stats' responsibility.

**CR-4.6** Respec may not be initiated while the player is in combat (`AutoAttack.IsAttacking == true` or any combat-tagged Status Effect active). The **Inventory System** owns this gate check — it calls `HasCombatTaggedEffect(EntityID)` during CR-4.1 Phase 1, before marking the item reserved. If the check returns true, the Inventory System returns an error; the item is not touched. The stat screen UI receives the error and displays a "cannot respec in combat" message. The UI does not independently call `HasCombatTaggedEffect` and does not own the gate. **The combat gate check is evaluated entirely on the server using server-authoritative `AutoAttack.IsAttacking` and server-side Status Effects state. Client-reported combat state is not trusted for this gate.** (Security policy: confirmed by technical-director — a client that falsely reports "not in combat" must not be able to bypass this gate.)

---

#### CR-5 — Level Cap Behavior

**CR-5.1** Level cap is 60. `GetBaseStat(StatID.Level)` never exceeds 60.

**CR-5.2** At L60, `AddExperience()` clamps `StatID.Experience` to `XpThreshold[60]`. XP beyond the L60 threshold is discarded. If Experience is already equal to `XpThreshold[60]`, no write occurs and no `OnStatChanged` fires.

**CR-5.3** The XP threshold table carries a sentinel at index 61 = `int.MaxValue` so the consecutive level-up check in CR-2.9 (`Experience >= XpThreshold[currentLevel + 1]`) is always valid without a bounds-check branch at L60.

**CR-5.4** `heldFreePoints` continue to accumulate and can be spent normally at L60.

**CR-5.5** `GetXPToNextLevel(EntityID)` returns `0` when `Level == 60`. HUD implementations must guard against division by zero before evaluating the bar fill formula — the UI Requirements L60 guard note (below) covers the canonical handling.

---

#### CR-6 — Spawn Initialization

**CR-6.1** The Class System calls `LevelingSystem.InitializeAtL1(EntityID, ClassType)` after allocating the `CharacterStats` instance (see Class System GDD SA-2). This is not a level-up — `OnLevelUp` does not fire. The `ClassType` value is cached per-entity by the Leveling System during this call and used for all subsequent auto-alloc reads via `IClassRegistry` (see Class System GDD LC-2 — classType is not re-queried at level-up).

**CR-6.2** Initialization sequence (executed within `BeginStatTransaction()` / `EndStatTransaction()` per Class System SA-2):
1. `SetBaseStat(Level, 1)`
2. Write starting attributes: STR=10, DEX=10, VIT=10, INT=10 via `SetBaseStat()`
3. Evaluate F-3 through F-9 with `LevelTierMultiplier = ×1.0`, write via `SetBaseStat()`
4. `SetBaseStat(CurrentHP, MaxHP)`, `SetBaseStat(CurrentMP, MaxMP)`
5. `heldFreePoints = 0`, `SetBaseStat(Experience, 0)`

**CR-6.3** On load from persistence: Character Persistence restores all base stats via `SetBaseStat()`. `OnExperienceThresholdCrossed` is not triggered during load. The Leveling System's only action on load is `RestoreLevelingState(EntityID, {heldFreePoints: savedValue})`. `LevelTierMultiplier` is derived on demand from `GetBaseStat(Level)` — it is never stored.

---

### States and Transitions

| State | Description | Entry | Exit |
|-------|-------------|-------|------|
| **Accumulating** | Normal state. XP received via `AddExperience()`. | Spawn at L1 OR level-up sequence completes (CR-2.10) | XP meets `XpThreshold[Level+1]` AND Level < 60 |
| **LevelingUp** | CR-2.1–CR-2.10 executing. `_levelingUpInProgress = true`. | XP threshold crossed while Accumulating | CR-2.10 fires — all writes complete, broadcast sent |
| **AtCap** | L60. XP received but clamped. No level-up will fire. Terminal state. | CR-2.1 guard triggers (Level == 60) OR load with Level == 60 | Never (terminal for MVP lifetime) |

*State is implicit — derived from `_levelingUpInProgress` flag and `GetBaseStat(Level)`. No explicit state enum is maintained.*

---

### Interactions with Other Systems

| System | Direction | Leveling System provides | Other system provides |
|--------|-----------|------------------------|----------------------|
| Character Stats | Bidirectional | Writes Level, STR/DEX/VIT/INT, all F-3–F-9 derived stats, CurrentHP, CurrentMP via `SetBaseStat()` on level-up, init, and free-point spend | `AddExperience()` API; `OnExperienceThresholdCrossed` notification; `BeginStatTransaction()` / `EndStatTransaction()` for respec |
| Damage Calculation | Downstream | — | Calls `AddExperience(EntityID, amount)` on kill; does not interact with Leveling System directly |
| Class System | Upstream | `InitializeAtL1(EntityID, ClassType)` entry point for spawn (per Class System SA-2); reads auto-alloc values via `IClassRegistry.TryGetClass(classType, out def)` at level-up and floor calc | Allocates CharacterStats instance; calls `InitializeAtL1` after spawn; provides ClassType to resolve auto-alloc template; caches classType per entity (LC-2) |
| Party System | Upstream | — | Computes per-member effective XP (base + party bonus) before calling `AddExperience()` per eligible member; owns party size and XP range radius |
| Character Persistence | Bidirectional | `GetLevelingState(EntityID) → {heldFreePoints}` for save | `RestoreLevelingState(EntityID, {heldFreePoints})` on load |
| HUD | Consumer | `GetXPToNextLevel(EntityID) → int`; `GetHeldFreePoints(EntityID) → int` | Subscribes to `OnStatChanged(Experience, Level)`; polls Leveling System for thresholds; display only |
| Inventory System (Respec) | Caller | `TryApplyRespec(EntityID)` entry point (may throw; Inventory System catches and calls Release on exception, Consume on success) | Owns respec item reservation handle; calls combat gate check; calls `TryApplyRespec()` after item reserved |
| Stat Screen UI | Caller | `AllocateFreePoint(EntityID, StatID)` API | Calls on player tap; polls `GetHeldFreePoints()` to determine available point count |

## Formulas

### F-LS-1 — XP Threshold

```
XP(L) = Mathf.RoundToInt(C × L^α × R^L)
```

**Variables:**

| Symbol | Type | Range | Description |
|--------|------|-------|-------------|
| L | int | [1, 59] | Target level (XP required to reach level L from L−1) |
| C | float | 178.6 | Base coefficient — primary tuning lever for overall XP scale |
| α | float | 0.648 | Polynomial exponent — controls early-game growth rate |
| R | float | 1.1198 | Exponential base (≈ e^0.1132) — controls late-game acceleration |
| XP(L) | int | [200, ~2,000,000] | XP required to advance to level L |

**Output range:** XP(1) ≈ 200; XP(59) ≈ 2,000,000. No integer overflow risk within int range.

**XpThreshold array (implementation):** The formula above gives `XP(L)` — the per-level XP required while AT level L to advance to level L+1 (the width of the level-L bar). The implementation array `XpThreshold` is a **cumulative baseline**, not a per-level cost table: `XpThreshold[1] = 0` (start of L1, no XP accumulated), `XpThreshold[2] = XP(1) ≈ 200`, `XpThreshold[L] = Σ XP(i) for i = [1, L−1]`. This cumulative form is required by the bar fill formula (CR-1.3) and the consecutive level-up check (CR-2.9). The "Cumulative XP" column in the table below represents `XpThreshold[L+1]` — the total XP earned to complete level L and begin level L+1. Do not confuse `XP(L)` (per-level cost, formula output) with `XpThreshold[L]` (cumulative baseline, array index).

**Sentinel:** `XpThreshold[61] = int.MaxValue` — guards the consecutive level-up check (`Experience >= XpThreshold[currentLevel + 1]`) at L60 without a bounds-check branch.

**Cumulative XP (to reach level L from L1):**

| Level | Per-Level XP | Cumulative XP | Est. Kill Time |
|-------|-------------|---------------|----------------|
| L1 | 200 | 200 | < 1 min |
| L10 | 2,452 | ~13,100 | ~11 min |
| L20 | 11,961 | ~77,800 | ~62 min |
| L30 | 50,751 | ~315,000 | ~4.4 h |
| L40 | 194,534 | ~1,230,000 | ~17 h |
| L50 | 711,610 | ~4,680,000 | ~65 h |
| L59 | 2,000,000 | ~14,800,000 | ~205 h total |

*Kill time assumes ~200 XP/min sustained combat (L1 mob rate baseline). This produces a **floor estimate** — the 205h total assumes grinding at L1 mob rates throughout, which no player would do. Tier-appropriate mobs award proportionally more XP, compressing the L40-59 bracket below the 185h floor figure above. Final per-tier pacing must be validated against mob XP award data from the zone-XP GDD before content investment is sized against these figures.*

*Correction (2026-09-25): the L20 row's Per-Level XP (was `12,172`) and Cumulative XP (was `~74,900`) were stale hand-arithmetic figures left over from the 2026-09-24 correction pass — they were inconsistent with this same document's own corrected F-LS-1 worked example (`XP(20)=11,961`) directly below. Corrected here to match. The other rows (L1, L10, L30, L40, L50, L59) have not been independently re-verified and may carry the same class of imprecision — see TD-038, which already tracks a full economy-designer re-validation of this table.*

**Example calculation (L20):**
`XP(20) = Mathf.RoundToInt(178.6 × 20^0.648 × 1.1198^20)`
`= Mathf.RoundToInt(178.6 × 6.967 × 9.612)`
`= Mathf.RoundToInt(11,961)` → **11,961**

*(Corrected 2026-09-24, Story 010 implementation: this example previously stated `20^0.648 = 7.246` and `1.1198^20 = 9.410`, giving `XP(20) = 12,173` — independently re-verified via two methods (direct exponentiation and the `exp(ln(x)×n)` identity) that the correct double-precision values are `6.967` and `9.612`, giving `11,961`. Hand-arithmetic slip in the original illustrative example, not a change to `C`/`α`/`R` — same class of error as TD-033.)*

---

### F-LS-2 — Party XP Bonus *(SUPERSEDED — canonical formula is F-PS-1 in party-system.md)*

> **SUPERSEDED (2026-05-16):** The Party System GDD authored **F-PS-1_PartyXpDeduction** with opposite direction — a detriment (10% less per additional member, multiplier range ×0.70–×1.00 at N=4) rather than this bonus. F-PS-1 is the canonical party XP formula. This section is retained for historical context; do not implement F-LS-2.

```
XP_member = XP_base × (1.0f + 0.10f × (N − 1))
```

**Variables:**

| Symbol | Type | Range | Description |
|--------|------|-------|-------------|
| XP_base | int | [1, ~2,000,000] | Base XP grant from the kill (post F-LS-1 lookup, pre-party adjustment) |
| N | int | [1, 4] | Party members within XP range (solo = 1) |
| PartyBonusIncrement | float | 0.10 | Bonus per additional member. Tuning knob — see Tuning Knobs. |
| XP_member | float | XP_base × [1.0, 1.3] | Final XP grant per eligible member |

**Bonus by party size:**

| N | Multiplier | Example (XP_base = 1,000) |
|---|-----------|--------------------------|
| 1 (solo) | ×1.00 | 1,000 XP |
| 2 | ×1.10 | 1,100 XP |
| 3 | ×1.20 | 1,200 XP |
| 4 (full) | ×1.30 | 1,300 XP |

**Application (superseded):** The canonical formula is **F-PS-1_PartyXpDeduction** in `party-system.md`. The Party System computes the detriment multiplier (`1.0f − 0.10f × (N − 1)`) and calls `AddExperience(EntityID, amount)` with a `Mathf.RoundToInt` conversion (OQ-LS-4 resolved 2026-05-17). The Leveling System receives only the final per-player grant and is not party-aware.

**Design note (historical — F-LS-2 superseded):** F-LS-2 described a +30% bonus direction. The canonical F-PS-1 in `party-system.md` goes the opposite direction: a detriment (×0.70 at N=4). Do not implement F-LS-2.

**Calibration anchor:** The 205-hour solo cap-time estimate (F-LS-1 table) is the design baseline. Party play at N=4 delivers ~1.30× XP, compressing cap-time to roughly 158 hours for each member — this is intentional Social Gravity upside, not a calibration concern. The solo anchor is the floor; party acceleration is the reward. If the 205h solo anchor shifts, recalibrate C, α, R together (see Tuning Knobs interaction constraint) — do not adjust `PartyBonusIncrement` to compensate for formula changes.

---

### F-LS-3 — LevelTierMultiplier

*Defined and owned by the Character Stats GDD. Referenced here for completeness.*

| Level Range | Multiplier |
|-------------|-----------|
| L1–19 | ×1.0 |
| L20–39 | ×1.2 |
| L40–59 | ×1.5 |
| L60 | ×2.0 |

Applied to F-3 through F-9 (MaxHP, MaxMP, AttackPower, Defense, MagicDefense, CritChance, AttackSpeedMultiplier) during level-up re-derivation (CR-2.6) and free-point spend (CR-3.2 step 5). Derived on demand from `GetBaseStat(Level)` — never stored.

---

### F-LS-4 — Attribute Auto-Allocation

*Per-level auto-alloc is deterministic by class. Free points are held in `heldFreePoints`.*

**Starting attributes (L1, all classes):** STR=10, DEX=10, VIT=10, INT=10.

**Auto-alloc per level-up:**

| Class | STR | DEX | VIT | INT | Free | Total |
|-------|-----|-----|-----|-----|------|-------|
| Warrior | +2 | +1 | +1 | 0 | +1 | 5 |
| Healer | 0 | 0 | +1 | +2 | +2 | 5 |

**Accumulated auto-alloc at L60 (59 level-ups from L1 to L60):**

| Class | STR | DEX | VIT | INT | Free held (max) |
|-------|-----|-----|-----|-----|-----------------|
| Warrior | 10 + 118 = 128 | 10 + 59 = 69 | 10 + 59 = 69 | 10 | 59 |
| Healer | 10 | 10 | 10 + 59 = 69 | 10 + 118 = 128 | 118 |

*These totals match the Character Stats GDD L60 snapshot for zero free-point spend. Free points add on top of the auto-alloc floor.*

## Edge Cases

### Group 1 — XP Grant Boundary Conditions

**EC-LS-01 — XP grant lands exactly on a threshold**
Trigger: `currentXP + amount == XpThreshold[Level + 1]` exactly.
Behavior: `OnExperienceThresholdCrossed` fires (condition is `>=`, not `>`). Full CR-2 sequence executes. After level-up, HUD bar fill evaluates to exactly 0.0 — bar is empty at the new level's start. No residual XP above threshold.

**EC-LS-02 — XP grant undershoots threshold**
Trigger: `currentXP + amount < XpThreshold[Level + 1]`.
Behavior: Character Stats writes XP, fires `OnStatChanged(Experience)`. `OnExperienceThresholdCrossed` is NOT called. Leveling System takes no action.

**EC-LS-03 — XP grant crosses one threshold with leftover**
Trigger: `currentXP + amount > XpThreshold[Level + 1]` but `< XpThreshold[Level + 2]`.
Behavior: CR-2.9 check fires once, evaluates false. One level gained. Leftover XP visible in HUD bar fill at new level.

**EC-LS-04 — Large XP grant crosses multiple thresholds**
Trigger: `currentXP + amount >= XpThreshold[Level + 3]` or more.
Behavior: CR-2.9 iterates. Each iteration executes the full CR-2.1–CR-2.8 sequence before re-checking. Each iteration reads fresh `GetBaseStat()` totals after its own auto-alloc writes — incremental attribute growth is correctly compounded. `OnLevelUp` fires once per level gained, in sequence, after all iterations resolve. A Warrior who gains three consecutive levels accumulates 3 free points.

**EC-LS-05 — XP grant at L59 reaches XpThreshold[60] (cap + tier transition)**
Trigger: L59 character's XP crosses `XpThreshold[60]`.
Behavior: CR-2.1 guard passes (Level == 59). Level set to 60. CR-2.2a fires immediately: `SetBaseStat(Experience, XpThreshold[60])` — any overshoot is clamped within this sequence so `GetBaseStat(Experience) == XpThreshold[60]` before any further steps. `LevelTierMultiplier` advances to ×2.0, `isTierTransition = true`. Full F-3–F-9 recomputed with ×2.0 — the single largest power spike in the game. Full HP/MP restore to new values. CR-2.9 check: `Experience >= XpThreshold[61]` (= `int.MaxValue`) is always false — loop terminates cleanly. *Note: CR-5.2's clamp path in `AddExperience()` handles subsequent kills after L60; CR-2.2a handles the transition kill itself.*

**EC-LS-06 — XP grant when already at L60**
Trigger: `AddExperience()` called while Level == 60.
Behavior: CR-5.2 clamp path. If `currentXP == XpThreshold[60]`, no write occurs and `OnStatChanged` does not fire. `XpThreshold[61] = int.MaxValue` makes `XP >= XpThreshold[61]` permanently false — `OnExperienceThresholdCrossed` never fires. CR-2.1 at-cap guard provides a second defense.

**EC-LS-07 — `AddExperience` with `amount == 0` or `amount < 0`**
Trigger: Caller passes zero or negative amount.
Behavior: Character Stats rejects with error log. No write to `StatID.Experience`. `OnStatChanged` and `OnExperienceThresholdCrossed` do not fire. Monotonic invariant preserved.

**EC-LS-08 — `AddExperience` targeting a mob entity**
Trigger: `AddExperience(mobEntityId, amount)` called (e.g., wrong `EntityID` from Damage Calculation).
Behavior: Character Stats identifies entity as mob — no-op (no write, no error log). `OnExperienceThresholdCrossed` is not fired. The Leveling System is not invoked. The mob-vs-player check occurs inside `AddExperience()` in Character Stats; the Leveling System is not in the call path.

---

### Group 2 — Consecutive Level-Up and Re-entrancy

**EC-LS-09 — `_levelingUpInProgress` flag: free point spend attempted mid-sequence**
Trigger: `AllocateFreePoint()` called while `_levelingUpInProgress == true` (stat screen or concurrent server call during CR-2.1–CR-2.10).
Behavior: `AllocateFreePoint()` is rejected. An interleaved free-point spend would write derived stats atop partially-updated primary attributes, producing incorrect F-3–F-9 results. Rejection is a "system busy" response. The UI disables the allocate button while `_levelingUpInProgress == true`, but the system layer defends independently. Rejection must not consume a free point — the guard fires before the decrement.

**EC-LS-10 — `OnExperienceThresholdCrossed` fires recursively mid-sequence**
Trigger: An `OnStatChanged` subscriber calls `AddExperience()` during CR-2, triggering a second `OnExperienceThresholdCrossed` before the first sequence exits.
Behavior: `_levelingUpInProgress == true` causes the second invocation to abort (the XP is still written to `StatID.Experience` — only the level-up sequence is blocked). After the first sequence completes and the flag is cleared, CR-2.9 correctly evaluates the accumulated XP for any additional levels. Distinguish from EC-LS-04 (legitimate consecutive levels via CR-2.9 loop) — this is stack re-entry, not loop re-entry.

---

### Group 3 — Free Point Allocation Edge Cases

**EC-LS-11 — `AllocateFreePoint` with `heldFreePoints == 0`**
Trigger: Stat screen calls `AllocateFreePoint()` when no free points are available.
Behavior: CR-3.2 guard 1 fires. Rejected with UI feedback. `heldFreePoints` remains 0 — it does not go negative. No `SetBaseStat` call. No F-3–F-9 recompute.

**EC-LS-12 — `AllocateFreePoint` targeting an invalid `StatID`**
Trigger: `targetStat` outside {STR, DEX, VIT, INT} (e.g., `StatID.MaxHP`, `StatID.Level`, `StatID.Experience`).
Behavior: CR-3.2 guard 2 fires. Error log, no write. **`heldFreePoints` is NOT decremented** — the guard fires before the decrement step. The CR-3.2 ordering is load-bearing: guard 1 → guard 2 → decrement → write. `StatID.Level` has separate write ownership (Leveling System only, via CR-2.2) — free-point allocation must never touch it.

**EC-LS-13 — Free-point spend pushes MaxMP above 9,999**
Trigger: High-INT Healer spends a free point in INT, pushing the F-4 raw result above 9,999.
Behavior: During CR-3.2 step 5, F-4 is recomputed. The Leveling System clamps to `min(rawResult, 9999)` before `SetBaseStat(MaxMP, ...)`. The free point is still consumed — the stat ceiling is a schema constraint, not an allocation rejection. Subsequent INT allocations continue consuming `heldFreePoints` and triggering recompute, but MaxMP remains at 9,999.

**EC-LS-14 — Free-point spend does NOT restore HP/MP**
Trigger: Player allocates a free point to VIT mid-combat. MaxHP increases via F-3 recompute.
Behavior: CR-3.2 step 6 prohibits HP/MP restore. `SetBaseStat(MaxHP, newValue)` is called. Character Stats EC-06 applies: CurrentHP is unchanged. The player gains a larger HP pool but no instant healing. Contrast with CR-2.7 (level-up does fully restore HP/MP).

**EC-LS-15 — Free-point spend at L60**
Trigger: L60 character with `heldFreePoints > 0` calls `AllocateFreePoint()`.
Behavior: Normal allocation proceeds. `LevelTierMultiplier = ×2.0` derived on demand from `GetBaseStat(Level) == 60`. F-3–F-9 recomputed with ×2.0. The at-cap state only blocks XP accumulation and level-up — free-point spending is valid per CR-5.4.

**EC-LS-16 — Last free point spent: counter goes to 0, not −1**
Trigger: Player spends their last free point (`heldFreePoints == 1`).
Behavior: Guard 1 passes (1 > 0). Decrement fires: `heldFreePoints = 0`. Subsequent calls immediately hit guard 1 and are rejected. `heldFreePoints` is never −1.

---

### Group 4 — Respec Edge Cases

**EC-LS-17 — Respec with held unspent free points**
Trigger: Player enters respec with `heldFreePoints > 0` (e.g., 5 unspent at L20 Warrior = 14 spent + 5 held = 19 total free points accumulated, 14 spent in stats).
Behavior: CR-4.2 redistributable pool = `spentFreePoints = (Level − 1) × freePointsPerLevel − heldFreePoints`. For this Warrior: `(20−1)×1 − 5 = 14 points`. **`heldFreePoints` (5) are NOT in the pool** — they remain held and unchanged after commit. After commit, `heldFreePoints` remains 5. The respec screen must not allow commit with unallocated redistributable points remaining.

**EC-LS-18 — Respec initiated while `IsAttacking == true` or combat-tagged Status Effect active**
Trigger: Player tries to initiate respec while in combat.
Behavior: The Inventory System calls `HasCombatTaggedEffect(EntityID)` during CR-4.1 Phase 1 — before the item is reserved. If in combat, the Inventory System returns an error; the item is untouched. The stat screen UI receives the error and surfaces feedback ("cannot respec in combat"). `TryApplyRespec()` is never called. The two-phase commit design ensures the item cannot be lost to a failed combat-gate check: the item is only reserved after the gate clears, and only consumed after `TryApplyRespec()` returns successfully.

**EC-LS-19 — Respec reduces MaxMP below CurrentMP**
Trigger: Respec moves INT points to another stat, lowering MaxMP. `SetBaseStat(MaxMP, lowerValue)` fires during CR-4.4 step 3.
Behavior: Character Stats EC-05 fires within the `SetBaseStat` call: `CurrentMP = min(CurrentMP, newMaxMP)`. Executes inside the `BeginStatTransaction()` window. `OnStatChanged(CurrentMP)` is deferred until `EndStatTransaction()`. HUD sees both the new MaxMP and corrected CurrentMP in a single notification pass. The player loses mana but is not killed.

**EC-LS-20 — Respec reduces MaxHP below CurrentHP**
Trigger: Respec moves VIT points to another stat, lowering MaxHP. `SetBaseStat(MaxHP, lowerValue)` fires during CR-4.4 step 3.
Behavior: Character Stats EC-05 fires: `CurrentHP = min(CurrentHP, newMaxHP)`. `OnEntityDied` does NOT fire — this is a schema clamp, not a damage event. MaxHP schema minimum is 1; the clamp cannot produce CurrentHP = 0. The player emerges from respec with reduced HP. Event is deferred within the transaction.

**EC-LS-21 — Respec at L1 (zero redistributable points)**
Trigger: L1 character uses a respec item.
Behavior: Pool = `(1 − 1) × N − heldFreePoints = 0` redistributable points (both terms are 0 at L1). All attributes are already at the floor (base 10). The respec screen opens showing 0 redistributable points — existing allocation is already at floor. Player can commit immediately (no-op respec) or cancel. Item is consumed regardless. `heldFreePoints` is unchanged (was already 0 at L1, remains 0). No stat writes occur. No F-3–F-9 recompute required. Do NOT special-case this with an error — execute the normal sequence.

**EC-LS-22 — Nested `BeginStatTransaction()` during respec**
Trigger: A programming error leaves a transaction open; `BeginStatTransaction()` is called again inside `TryApplyRespec()`.
Behavior: Character Stats throws `InvalidOperationException` on the second call. `TryApplyRespec()` propagates the exception. `RollbackStatTransaction()` called unconditionally in the catch block (safe as a no-op if no transaction is open, per Character Stats F-10). Error is logged. Respec fails. The Inventory System catches the exception from `TryApplyRespec()` and calls `ItemReservation.Release()` — **item is returned to active inventory** (CR-4.1 two-phase commit).

**EC-LS-23 — Exception mid-respec transaction: item returned, stats rolled back**
Trigger: Any exception during CR-4.4 steps 2–4 inside `TryApplyRespec()` (e.g., entity despawned mid-respec).
Behavior: `TryApplyRespec()` catches the exception, calls `RollbackStatTransaction()` unconditionally (deferred event queue discarded; stats revert to pre-transaction base values; `EndStatTransaction()` never called — HUD sees no stat changes), then re-throws. The Inventory System catches the re-thrown exception and calls `ItemReservation.Release()` — **item is returned to active inventory**. The player does NOT lose the item. This is the key guarantee of the two-phase commit model (CR-4.1).

**EC-LS-24 — Respec: non-auto-allocated stat can return to base floor of 10**
Trigger: A Warrior spent free points in INT. Player respecs and wants INT returned to minimum.
Behavior: INT floor for Warrior = 10 (non-auto-allocated, per CR-4.3). The player can move all free points out of INT, leaving INT = 10. This is valid — not a floor violation. F-4 and F-7 recomputed with INT=10 at L60: MaxMP = 440, MagicDefense = 8.

---

### Group 5 — Level Cap and Tier Transitions

**EC-LS-25 — Level-up at L19→L20: first tier transition**
Trigger: Character levels from L19 to L20.
Behavior: `LevelTierMultiplier` changes from ×1.0 to ×1.2. CR-2.6 recomputes **all F-3–F-9 from scratch** using the new multiplier and the full accumulated attribute totals — not an incremental delta from old derived stats. The from-scratch recompute is the correctness invariant. Power spike = one level's auto-alloc + multiplier change applied retroactively to all accumulated attributes. MaxHP jump is larger than any non-tier level-up.

**EC-LS-26 — Level-up at L39→L40 and L59→L60**
Trigger: Same structure as EC-LS-25 at L39 and L59 respectively.
Behavior: ×1.2→×1.5 at L40; ×1.5→×2.0 at L60. Each applies the from-scratch F-3–F-9 recompute. L59→L60 is the largest tier jump in the game (33% multiplier increase on the highest accumulated totals). These are three independent test cases for Acceptance Criteria.

**EC-LS-27 — CritChance and ASM raw output exceeds clamp ceiling after tier transition**
Trigger: Character with extreme DEX at L60. CritChance raw = 0.05 + (DEX × 0.0015 × 2.0) exceeds 0.75. ASM raw = 1.0 + (DEX × 0.003 × 2.0) exceeds 2.0.
Behavior: The Leveling System writes the raw formula output via `SetBaseStat()` — it does NOT pre-clamp. `GetEffectiveStat()` applies F-1's clamp at query time. `GetBaseStat()` returns the stored raw value. Pre-clamping in the Leveling System would corrupt persistence and future recompute.

---

### Group 6 — Spawn and Load Initialization

**EC-LS-28 — Spawn initialization: `OnExperienceThresholdCrossed` must NOT fire**
Trigger: `InitializeAtL1()` writes `SetBaseStat(Experience, 0)` directly.
Behavior: `AddExperience()` is the only path that calls `OnExperienceThresholdCrossed`. Spawn uses `SetBaseStat(Experience, 0)` directly, bypassing `AddExperience()` entirely. Character Stats must not fire `OnExperienceThresholdCrossed` from within `SetBaseStat`. `OnLevelUp` also does not fire (CR-6.1). Character begins at L1 with no level-up events.

**EC-LS-29 — Load from persistence: `OnExperienceThresholdCrossed` must NOT fire**
Trigger: Character Persistence calls `SetBaseStat(Experience, savedValue)` on reload.
Behavior: Same guarantee as EC-LS-28. No F-3–F-9 recalculation on load — Character Persistence restores already-computed derived stats directly via `SetBaseStat`. `OnLevelUp` does not fire. A L60 character logging in produces no spurious level-up events to any subscriber.

**EC-LS-30 — L60 character loaded: system begins in AtCap state**
Trigger: `RestoreLevelingState()` called for a character with `GetBaseStat(Level) == 60`.
Behavior: AtCap state is derived from `GetBaseStat(Level) == 60` and `_levelingUpInProgress == false`. No explicit state enum. First `AddExperience()` after load enters the clamp path immediately. `heldFreePoints` restored to saved value.

**EC-LS-31 — Uninitialized CharacterStats queried before `InitializeAtL1()` completes**
Trigger: `Level` field defaults to C# default (0) before initialization. A system queries stats between entity allocation and `InitializeAtL1()` completion.
Behavior: No system may hold or query a `CharacterStats` reference before `InitializeAtL1()` completes. The Class System must not inject the reference to downstream systems prematurely. In dev builds, `GetBaseStat()` and `GetEffectiveStat()` check an `_isInitialized` flag and throw if called before initialization.

---

### Group 7 — Party XP

**EC-LS-32 — Party member leaves mid-kill**
Trigger: Party composition changes between engage and kill.
Behavior: The Leveling System is party-unaware (CR-1.5) — it receives only the final per-player grant from the Party System. Which N value the Party System uses at kill resolution (and whether the departed member is eligible) is entirely owned by the Party System GDD. This is a confirmed non-case for the Leveling System.

**EC-LS-33 — F-PS-1 float-to-int conversion: interface contract (resolved)**
Trigger: F-PS-1_PartyXpDeduction (canonical, supersedes F-LS-2) returns a `float`. `AddExperience()` takes `int`. For N=4, `XP_base = 333`: `333 × 0.7 = 233.1f`. `Mathf.RoundToInt(233.1f) = 233`. `Mathf.FloorToInt(233.1f) = 233`.
Behavior: `AddExperience(EntityID, amount)` declares `amount` as `int`. The Party System must convert via `Mathf.RoundToInt` before calling. Conversion method confirmed in Party System GDD (OQ-LS-4 resolved 2026-05-17).

---

### Group 8 — Data Structure and Formula Boundaries

**EC-LS-34 — `XpThreshold[61]` sentinel: array bounds safety**
Trigger: CR-2.9 at L60 accesses `XpThreshold[currentLevel + 1]` = `XpThreshold[61]`.
Behavior: Index 61 is a valid authored entry (`int.MaxValue`) — not an out-of-bounds access. The array must be declared with at least 62 entries (indices 0–61). The sentinel prevents the consecutive level-up loop from requiring a bounds-check branch at cap.

**EC-LS-35 — F-LS-1 intermediate float precision**
Trigger: Computing `178.6 × 59^0.648 × 1.1198^59`.
Behavior: Within `float`'s 7 significant digits. `Mathf.RoundToInt` converts to nearest int — no silent truncation from `(int)` cast. No overflow risk. Sentinel `int.MaxValue` is separately authored, not computed from F-LS-1.

*(Corrected 2026-09-24, Story 010 implementation: the illustrative intermediate values previously stated here (`1.1198^59 ≈ 800`, product `≈ 2,004,600`, cumulative-to-L60 `≈ 14,800,000`) were hand-arithmetic approximations, not independently verified. Precise double-precision computation (verified via `Math.pow`, confirmed via the `exp(ln(x)×n)` identity) gives: `59^0.648 = 14.045`, `1.1198^59 = 793.03`, `XP(59) = 1,989,211`; cumulative `XpThreshold[60] = 16,769,995` — about 13% above the prior illustrative estimate. Both are well within `int` range (no overflow risk either way); this correction affects only the documented illustrative numbers, not the formula, constants, or overflow-safety conclusion. Flagged as TD-038 for economy-designer attention, since the "~205h cap-time anchor" narrative estimate elsewhere in this document was built against the lower, imprecise total.)*

**EC-LS-38 — Corrupted Level value on load: clamped to [1, 60]**
Trigger: Character Persistence restores `GetBaseStat(Level)` with a value outside `[1, 60]` — e.g., `Level = 0` or `Level = 70` — due to save corruption or tamper.
Behavior: `RestoreLevelingState()` reads `GetBaseStat(Level)` after Character Persistence writes. If outside `[1, 60]`, clamp to the nearest valid bound (below 1 → 1; above 60 → 60) and log an error. Proceed with the clamped value. This guard is required because `Level = 70` causes `CR-2.9` to access `XpThreshold[71]` — beyond the 62-entry array — producing `IndexOutOfRangeException`. AtCap state is derived normally if clamped to 60. This is a server-side tamper defense.

**EC-LS-36 — `heldFreePoints` restored with a value exceeding theoretical maximum**
Trigger: Corrupted or tampered save restores `heldFreePoints = 500` for a Warrior (max is 59).
Behavior: `RestoreLevelingState()` validates the restored value against `(Level − 1) × freePointsPerLevel[class]`. If exceeded, clamp to the maximum legal value and log an error. The corrupted value is not written. Server-side tamper defense — required for an MMORPG.

**Atomicity recovery invariant (partial-write crash):** A server crash between writing Level and writing `heldFreePoints` (or vice versa) can produce an inconsistent state not caught by the above max-clamp (because neither value individually exceeds its own maximum). On `RestoreLevelingState()`, additionally enforce: `heldFreePoints ≤ (Level − 1) × freePointsPerLevel[class]`. If this would be violated (meaning `heldFreePoints` is higher than total accumulated for the restored Level — i.e., Level was written but heldFreePoints was not incremented for the new level, then heldFreePoints for the *next* tick was written out of order), clamp `heldFreePoints` down to the maximum for the restored Level and log a warning. This prevents phantom redistributable free points from appearing in the respec pool formula `(Level − 1) × fpp − heldFreePoints` as a result of a partial write. The inverse crash (heldFreePoints written, Level not advanced) produces a respec pool one point smaller than correct — the player loses one redistributable point from that level-up; this is accepted as a safe-fail (undercount, not overcount).

**EC-LS-37 — Level-up fires `OnStatChanged` per-write: HUD may see intermediate values**
Trigger: CR-2.4 and CR-2.6 each fire `OnStatChanged` per `SetBaseStat` call. A HUD subscriber querying `GetEffectiveStat(AttackPower)` during a CR-2.4 handler sees updated STR but the old-tier AttackPower (base stat not yet written).
Behavior: The level-up sequence does not use `BeginStatTransaction()`. Each `SetBaseStat` fires `OnStatChanged` immediately. HUD subscribers may see transient intermediate states during the sequence. This is known and accepted — if the level-up animation is displaying, it is not user-visible. This must be explicitly documented for HUD implementers so intermediate values during level-up are not treated as bugs.

## Dependencies

**Upstream dependencies (systems the Leveling System depends on):**

| System | Direction | Interface | Status |
|--------|-----------|-----------|--------|
| **Character Stats** | Bidirectional | Leveling System writes to `StatID.Level`, `StatID.Experience`, all primary attributes, and all F-3–F-9 derived stats via `SetBaseStat()`. Reads attribute totals via `GetBaseStat()`. Subscribes to `ILevelingSystemListener.OnExperienceThresholdCrossed`. Uses `BeginStatTransaction()` / `EndStatTransaction()` / `RollbackStatTransaction()` for respec. | Approved GDD |
| **Class System** | Upstream | Class System calls `LevelingSystem.InitializeAtL1(EntityID, ClassType)` after allocating the `CharacterStats` instance (per Class System GDD SA-2). Leveling System reads auto-alloc values via `IClassRegistry.TryGetClass(classType, out def)` at every level-up and during floor calculation — not hardcoded. Must not inject `CharacterStats` to any downstream system before `InitializeAtL1()` completes. | Approved (2026-04-29) |

**Downstream dependents (systems that depend on Leveling System output):**

| System | Direction | What it consumes | Status |
|--------|-----------|-----------------|--------|
| **Damage Calculation** | Upstream → Leveling System | Calls `AddExperience(EntityID, amount)` on the killing player entity when a mob's `CurrentHP` reaches 0. Does not interact with the Leveling System directly — routes through `AddExperience()` on Character Stats. | Approved GDD (Pass 2 pending) |
| **Party System** | Upstream → Leveling System | Computes per-member effective XP via **F-PS-1_PartyXpDeduction** in `party-system.md` (detriment: `XP_base × (1.0f − 0.10f × (N − 1))`; multiplier range ×0.70–×1.00) before calling `AddExperience()` per eligible member. Owns party size and XP range radius. Must call `AddExperience()` with `int` — conversion via `Mathf.RoundToInt` confirmed (OQ-LS-4 resolved 2026-05-17). | Approved (2026-05-17) |
| **Character Persistence** | Bidirectional | Calls `GetLevelingState(EntityID) → {heldFreePoints}` for save. Calls `RestoreLevelingState(EntityID, {heldFreePoints})` on load. `heldFreePoints` is NOT stored in Character Stats and is NOT recoverable from `GetBaseStat()` — these two endpoints are the only serialization interface. | Not yet designed |
| **HUD** | Consumer | Subscribes to `OnStatChanged(Experience)` and `OnStatChanged(Level)`. Calls `GetXPToNextLevel(EntityID) → int` and `GetHeldFreePoints(EntityID) → int`. Derives bar fill: `(Experience − XpThreshold[Level]) / (XpThreshold[Level+1] − XpThreshold[Level])`. Must tolerate intermediate stat values during the level-up sequence — level-up is not wrapped in a transaction (see EC-LS-37). | Not yet designed |
| **Inventory System (Respec)** | Caller | Phase 1 (gate + reserve): calls `HasCombatTaggedEffect(EntityID)` — if in combat, fails with no inventory change; if clear, marks item reserved. Phase 2 (commit): calls `LevelingSystem.TryApplyRespec(EntityID)` (may throw); on success calls `ItemReservation.Consume()` (item destroyed); on exception calls `ItemReservation.Release()` (item returned). **Owns the combat gate check and the ItemReservation handle.** (CR-4.1, CR-4.6, OQ-LS-1 resolved) | Not yet designed |
| **Stat Screen UI** | Caller | Calls `AllocateFreePoint(EntityID, StatID)` on player tap. Polls `GetHeldFreePoints()` to determine available count. Must disable the allocate button while `_levelingUpInProgress == true` (EC-LS-09). Must not allow commit during respec with unallocated **spent** free points remaining in the pool (pool = `spentFreePoints`, not total free points — see CR-4.2). Does NOT own the respec combat gate check — that belongs to the Inventory System. | Not yet designed |
| **Status Effects System** | Dependency (respec gate) | Must provide a query API (`HasCombatTaggedEffect(EntityID)`) called by the **Inventory System** during CR-4.1 Phase 1. Definition of "combat-tagged" must be specified in the Status Effects GDD. | Not yet designed |
| **Skill System** | Consumer | Subscribes to `ILevelingEventBroadcaster.OnLevelUp(EntityID, newLevel)` to update `skillInstance.IsUnlocked` flags and send `SkillUnlockNotification` to the client for newly unlocked skills. The server tick must complete all `OnLevelUp` notifications before the Skill System processes cast requests for that tick (see CR-2.10 tick-phase ordering constraint). | skill-system.md (In Design) |

**Cross-doc back-references required:**
- Character Stats GDD — already references the Leveling System as the sole `ILevelingSystemListener` registrant and as the writer of Level/attribute/derived base stats.
- Damage Calculation GDD — must reference the Leveling System as the downstream consumer of XP from kills (via `AddExperience()`).
- Skill System GDD — references Leveling System as the source of `OnLevelUp(EntityID, newLevel)` event; documents the tick-phase ordering constraint in EC-SK-6 and AC-SK-49.

**Cross-team dependencies (blocking sprint commitment):**

| Dependency | Owner | Gate |
|---|---|---|
| Economy-designer sign-off on full `XpThreshold` cumulative table (AC-LS-31) | Economy Designer | Must be obtained before sprint start — not an in-sprint deliverable. `GetXPAward` (OQ-LS-7) is now resolved (2026-09-24) — economy-designer can validate the 205h cap-time anchor and tier pacing against real `MobDefinition.KillXP` ranges once Enemy AI's mob roster is authored. |
| ~~`GetXPAward(EntityID)` specification (OQ-LS-7)~~ | — | **RESOLVED 2026-09-24** — see OQ-LS-7 in Open Questions. `GetXPAward` reads `MobDefinition.KillXP`/`EnragedKillXP` (already-Approved `enemy-ai.md` schema) via `IMobDefinitionRegistry`; no new sub-GDD needed. |
| Network Architecture ADR | Network Programmer | Must cover: level-up packet protocol, client-side state machine for atomic stat+level application, `AllocateFreePoint` per-entity serialization, and respec stat replication approach. |
| `networking-core.md` Approved status | Network Programmer | **Blocking implementation sprint gate.** The Leveling System's network integration (CR-2.10 atomicity contract, respec Phase 1/2 protocol, `AllocateFreePoint` rate-limiting) cannot be implemented until `networking-core.md` reaches Approved. This GDD may be approved before `networking-core.md` is; the implementation sprint's network integration work is gated on `networking-core.md` approval. |
| Technical Director sign-off on server-authoritative combat gate (CR-4.6) | Technical Director | Security policy decision — required before respec implementation sprint. |

## Tuning Knobs

All knobs are data-driven constants. Safe ranges reflect constraints derived from the formula calibration and the game's design targets.

| Knob | Current Value | Type | Safe Range | What Changes | Effect if Pushed |
|------|--------------|------|------------|--------------|-----------------|
| **C** (XP base coefficient) | 178.6 | float | [50, 500] | Scales all XP thresholds proportionally | Lower: faster overall progression. Higher: slower. Does not change curve shape — only the global time scale. |
| **α** (polynomial exponent) | 0.648 | float | [0.4, 1.0] | Controls early-game growth rate | Lower: curve flatter early, sharper late. Higher: L1–10 slows down more. |
| **R** (exponential base) | 1.1198 | float | [1.05, 1.20] | Controls late-game acceleration | Lower: end-game grind shorter. Higher: L50–59 expands dramatically — very sensitive; R=1.15 nearly doubles L59 XP. |
| ~~**PartyBonusIncrement**~~ | *(superseded)* | | | Described F-LS-2 bonus direction (SUPERSEDED). The active constant is `XP_PARTY_DEDUCTION_RATE = 0.10` in `party-system.md` F-PS-1 — a per-member detriment, not a bonus. |
| **Level cap** | 60 | int | [30, 100] | Maximum achievable level | Lower: shorter arc, tiers compressed. Higher: requires extending XpThreshold table and tier breakpoints. |
| **LevelTierMultiplier breakpoints** | L20, L40, L60 | int | [15–25], [35–45], [55–60] | Where tier power spikes occur | Shifting earlier: mid-game power arrives sooner, low-level content obsoletes faster. Later: longer time at each power tier. |
| **LevelTierMultiplier values** | ×1.0 / ×1.2 / ×1.5 / ×2.0 | float | [×1.0–×1.1] / [×1.1–×1.5] / [×1.3–×2.0] / [×1.5–×3.0] | Magnitude of power spikes at tier transitions | Compressing the spread makes tier walls subtle. Widening makes each tier feel like a new game. |
| **Warrior auto-alloc** | +2 STR, +1 VIT, +1 DEX / level | int per stat | STR: [1–3], VIT: [0–2], DEX: [0–2] | Warrior L60 stat totals and class identity | Must recalibrate Character Stats L60 snapshots if changed. |
| **Healer auto-alloc** | +1 VIT, +2 INT / level | int per stat | VIT: [0–2], INT: [1–3] | Healer L60 stat totals and class identity | Must recalibrate L60 snapshots. Raising VIT reduces HealPower ceiling. |
| **Warrior free points / level** | 1 | int | [1–2] | Max held free points at L60 (59 max); respec pool size | Raising to 2 doubles build flexibility and reduces class distinctiveness. |
| **Healer free points / level** | 2 | int | [1–3] | Max held free points at L60 (118 max); respec pool size | Same consideration as Warrior free points. |

**Interaction constraint:** C, α, and R are interdependent — calibrated together against three anchors (L1=200 XP, L20≈12,000 XP, L59≈2,000,000 XP). Changing any one in isolation shifts all three anchors. When retuning, fix the desired L1, L20, and L59 targets and re-derive C, α, R as a system, not individually.

**Live-service warning:** A change to R by 0.01 is amplified at L59 due to exponential compounding. Always compute and review the full updated table before shipping any tuning change.

## Visual/Audio Requirements

**Level-Up Event (CR-2.10 broadcast)**

| Element | Requirement |
|---------|-------------|
| Screen overlay | Brief overlay flash at level-up. Duration: ~0.5s, non-blocking (player can move). Consistent intensity for all level-ups — tier transitions (L20, L40, L60) do NOT receive a more prominent overlay effect. The power reveals itself in the stats, not the animation. |
| Level number display | New level displayed prominently center-screen. Animates in (scale from 0 → 1.2 → 1.0). Hold for ~1.5s for normal level-ups; ~2.5s for tier transitions (L20, L40, L60). The longer hold is the only visual distinction for tier walls — no additional effects. |
| XP bar | Fills to threshold, then flashes white, empties, and resets to 0 fill for the new level. On consecutive level-ups (EC-LS-04), **cut to the final state**: display one level-up overlay showing the final level gained only. Do not animate each intermediate level individually. The bar resets to the fill position corresponding to any overshoot XP at the final level. |
| Floating text | "+Level [N]!" floats above the character. Visible to nearby players (social signal). |
| Level badge (above name) | Level number updates immediately. All nearby players see the updated badge. Must not display the intermediate level during consecutive level-ups — only final level. |
| Sound: normal level-up | Distinct, satisfying chime. Clean confirmation — earned, not triumphant. Not a fanfare. |
| Sound: tier transition | Same instrument and character as the normal level-up chime, with a slightly longer sustain and reverb tail. No ambient audio pause. No stinger. The confirmation is heavier, not louder — consistent with "you just notice." |
| Music | No music interruption for any level-up, including tier transitions. The numbers speak for themselves. |

**Free Point Grant**

| Element | Requirement |
|---------|-------------|
| Stat screen notification | Pulsing indicator on the stat screen entry point whenever `heldFreePoints > 0`. Not a modal interrupt — player opens the screen at their own time. |
| Sound | Soft UI chime plays once when free points become available (not looping). |

**Respec Screen**

| Element | Requirement |
|---------|-------------|
| Visual state | Attributes shown as editable sliders or +/− buttons. Auto-alloc floor displayed in a muted color to indicate it is non-redistributable. Free pool counter visible and decrements in real time as points are allocated. |
| Commit confirmation | Modal confirmation dialog before finalizing. Displays: "This will consume your Respec Scroll. Are you sure?" **The modal has one button: Confirm. There is no Cancel button.** Once the respec screen opens (Phase 1 complete — item reserved), the player either confirms or force-quits the app; the 30-second TTL then expires and the item returns to active inventory automatically. This is a deliberate UX choice: a Cancel button would imply the item can be safely returned mid-flow, but the reservation/commit model makes this unnecessary — the item is already safe (returned on TTL expiry or exception). |
| Sound: commit | Weight — a "lock-in" sound distinct from normal UI taps. |

## UI Requirements

**XP Bar (HUD)**
- Always visible in HUD.
- Fill = `(Experience − XpThreshold[Level]) / (XpThreshold[Level+1] − XpThreshold[Level])`.
- At L60: bar is full and static. Level display shows "MAX" instead of a number. No XP text shown.
- **L60 bar fill guard (implementer note):** The bar fill formula (CR-1.3) must NOT be evaluated at L60. After CR-2.2a clamps `Experience` to `XpThreshold[60]`, the formula would produce `0.0 / (int.MaxValue − XpThreshold[60]) ≈ 0.0` — a near-empty bar. The HUD must special-case `Level == 60`: render `fill = 1.0` constant, bypass the formula entirely.
- Tapping the bar opens a tooltip: current XP / XP to next level. Estimated time-to-level displayed if recent kill rate is available; omitted otherwise.

**Level Badge (above character name)**
- Visible to all players in range.
- Updates immediately on level-up (no animation delay on the badge itself).
- Font size large enough to read at combat distance. Must not overlap the player's health bar.

**Stat Screen (free point allocation)**
- Accessed from the main HUD (dedicated button or character portrait tap).
- Shows: current Level, current XP, held free points count.
- Each primary attribute (STR, DEX, VIT, INT) displayed with: current base value, + button (active only when `heldFreePoints > 0`).
- Each + tap calls `AllocateFreePoint(EntityID, targetStat)` immediately — no preview-then-confirm at the system layer (CR-3.3). If the UI wants a preview mode, it implements that above the API.
- Free point counter decrements in real time with each allocation.
- The + button is disabled while `_levelingUpInProgress == true` (EC-LS-09).
- F-3–F-9 derived stat values update immediately after each allocation via `OnStatChanged` subscription.

**Respec Screen**
- Opened by the Inventory System after Phase 1 item reservation (item reserved into hold slot — not yet consumed).
- Blocked entry if `IsAttacking == true` or combat-tagged Status Effect active (EC-LS-18) — the Inventory System enforces this before item reservation.
- Displays the full attribute allocation grid. Auto-alloc floor values displayed in a muted color (non-redistributable portion). Redistributable pool counter (`spentFreePoints = total accumulated − heldFreePoints`) shown at top. `heldFreePoints` shown separately and NOT part of the redistributable pool.
- Commit button disabled while any redistributable points remain unallocated.
- Modal confirmation required before commit. **The modal has one button: Confirm. There is no Cancel button.** Once Phase 2 begins (TryApplyRespec in flight), the outcome is commit-or-rollback, not commit-or-cancel. If the player force-quits after the respec screen opens, the 30-second reservation TTL expires and the item returns automatically.
- Projected derived stats (MaxHP, MaxMP, AP, etc.) displayed in a preview column alongside current values before commit.

## Acceptance Criteria

### Group 1 — XP Accumulation and Level-Up Sequence (CR-1, CR-2)

**AC-LS-01** — AddExperience writes XP and fires OnStatChanged for Experience only
Given: A Warrior at L5 with `Experience = 5,000`.
When: `AddExperience(entity, 500)` is called.
Then: `GetBaseStat(StatID.Experience)` returns `5,500`. `OnStatChanged(EntityID, StatID.Experience)` fires exactly once for `StatID.Experience` — no other `StatID` fires. No level-up event fires.
Type: Unit | Blocks: Implementation

**AC-LS-02** — XP at exact threshold triggers level-up
Given: A Warrior at L5 where `XpThreshold[6] = T`. Current `Experience = T − 100`.
When: `AddExperience(entity, 100)` is called.
Then: `OnExperienceThresholdCrossed` fires. CR-2 executes. `GetBaseStat(Level)` returns `6`. `GetBaseStat(Experience)` returns `T` exactly.
Type: Unit | Blocks: Implementation

**AC-LS-03** — Level-up sequence step order: Level written before derived stats
Given: A Warrior at L19 with sufficient XP to cross `XpThreshold[20]`. A `SetBaseStat` call-order spy registered on the `ILevelingSystem` test seam (`ITestableCallOrderObserver`) before the test.
When: CR-2 executes.
Then: The observer's recorded sequence has `SetBaseStat(Level, 20)` at index 0, followed by at least one STR/DEX/VIT write (CR-2.4), followed by derived stat writes (CR-2.6). `LevelTierMultiplier` used in CR-2.6 is ×1.2, not ×1.0. **Test seam required:** The Leveling System must expose `ITestableCallOrderObserver` (or equivalent) on its public interface — confirm the interface name and registration API with Lead Programmer before the implementation sprint begins. This is a sprint-planning dependency, not an in-sprint decision.
Type: Unit | Blocks: Implementation

**AC-LS-04** — Auto-alloc writes: Warrior
Given: A Warrior at L1 with STR=10, DEX=10, VIT=10.
When: Level-up to L2 executes CR-2.4.
Then: `SetBaseStat(STR, 12)`, `SetBaseStat(DEX, 11)`, `SetBaseStat(VIT, 11)` fire in that order (matching CR-2.4's iteration order over `{STR, DEX, VIT, INT}`). INT is not touched. Each fires `OnStatChanged` for its stat.
Type: Unit | Blocks: Implementation

**AC-LS-05** — Auto-alloc writes: Healer
Given: A Healer at L1 with VIT=10, INT=10.
When: Level-up to L2 executes CR-2.4.
Then: `SetBaseStat(VIT, 11)`, `SetBaseStat(INT, 12)` fire. STR and DEX not touched. `heldFreePoints` increments by 2.
Type: Unit | Blocks: Implementation

**AC-LS-06** — Full HP/MP restore uses freshly written MaxHP (CR-2.7)
Given: A Warrior mid-combat at L9 (8 level-ups from L1: VIT = 10 + 8×1 = 18) with `CurrentHP = 50`. Pre-level-up `MaxHP_old = Mathf.FloorToInt((200 + 18×20) × 1.0) = 560`. Level-up to L10 fires CR-2.4 auto-alloc (+1 VIT → VIT=19), then CR-2.6 writes `MaxHP_new = Mathf.FloorToInt((200 + 19×20) × 1.0) = 580`.
When: CR-2.6 writes `SetBaseStat(MaxHP, 580)`, then CR-2.7 executes.
Then: `SetBaseStat(CurrentHP, 580)` is called using the value just written in CR-2.6. `GetBaseStat(CurrentHP)` returns `580` (not `50`, not `560`). The pre-level-up MaxHP value of `560` is not used in CR-2.7.
Type: Unit | Blocks: Implementation

**AC-LS-07** — heldFreePoints increments correctly per class
Given: A Warrior at L4 with `heldFreePoints = 0`; a Healer at L4 with `heldFreePoints = 0`.
When: Each gains one level.
Then: Warrior's `heldFreePoints` becomes `1`. Healer's `heldFreePoints` becomes `2`. Value is accessible only via `GetHeldFreePoints(EntityID)` — not via `GetBaseStat`.
Type: Unit | Blocks: Implementation

**AC-LS-08** — OnLevelUp broadcasts once per level, after all consecutive levels resolve
Given: A Warrior at L3 receives a single XP grant crossing `XpThreshold[4]`, `[5]`, and `[6]`.
When: `AddExperience(entity, largeAmount)` resolves.
Then: `OnLevelUp(EntityID, 4)`, `OnLevelUp(EntityID, 5)`, and `OnLevelUp(EntityID, 6)` each fire exactly once in that sequence, all after all CR-2.1–CR-2.9 iterations are complete. `GetBaseStat(Level)` returns `6` before any broadcast fires. *(This AC tests broadcast timing only — that broadcasts are deferred until all iterations complete. AC-LS-09 tests the complementary property: that each iteration reads fresh stat totals from prior iterations, not cached values.)*
Type: Unit | Blocks: Implementation

**AC-LS-09** — Consecutive level-up: each iteration reads fresh attribute totals
Given: A Warrior at L2 gaining two consecutive levels. L3 auto-alloc reads the totals written by L2 auto-alloc.
When: CR-2.9 iterates twice.
Then: `GetBaseStat()` reads in CR-2.5 of the second iteration return values written by CR-2.4 of the second iteration. Final MaxHP matches the formula applied to L3 totals, not L2 totals.
Type: Unit | Blocks: Implementation

**AC-LS-10** — `_levelingUpInProgress` blocks re-entrant AllocateFreePoint
Given: `_levelingUpInProgress == true`. Warrior has `heldFreePoints = 3`.
When: `AllocateFreePoint(entity, StatID.STR)` is called.
Then: Call is rejected. `heldFreePoints` remains `3`. No `SetBaseStat` call fires. A "system busy" rejection is returned.
Type: Unit | Blocks: Implementation

**AC-LS-49** — Re-entrant `OnExperienceThresholdCrossed` (EC-LS-10): XP retained, sequence aborted, CR-2.9 catches up
Given: `_levelingUpInProgress == true`. A test subscriber calls `AddExperience(entity, amount)` from within an `OnStatChanged` handler during CR-2, triggering a second `OnExperienceThresholdCrossed` before the first sequence exits. The amount is sufficient to cross the next threshold.
When: The re-entrant invocation reaches the Leveling System's handler.
Then: (1) No `SetBaseStat(Level, ...)` fires during the re-entrant invocation — `GetBaseStat(Level)` does NOT advance during the re-entrant call. (2) `GetBaseStat(Experience)` reflects the XP written by the re-entrant `AddExperience` — the XP write is NOT blocked, only the level-up sequence is. (3) After the original CR-2 exits and clears `_levelingUpInProgress`, CR-2.9 evaluates the updated Experience and fires an additional level-up iteration if the threshold is met. Observable: final `GetBaseStat(Level)` equals the number of thresholds crossed (including the re-entrant one); `OnLevelUp` fires once per final level gained. Distinguish from EC-LS-04 (legitimate consecutive levels via loop) — this is stack re-entry, not loop re-entry.
Type: Unit | Blocks: Implementation

---

### Group 2 — Free Point Allocation (CR-3)

**AC-LS-11** — Valid allocation commits stat and triggers re-derive
Given: A Warrior at L10 (tier ×1.0), STR=28. `heldFreePoints = 2`.
When: `AllocateFreePoint(entity, StatID.STR)` is called.
Then: `heldFreePoints` becomes `1`. `GetBaseStat(STR)` returns `29`. `SetBaseStat(AttackPower, Mathf.FloorToInt((10 + 29×2) × 1.0)) = SetBaseStat(AttackPower, 68)` fires. `CurrentHP` and `CurrentMP` are NOT modified.
Type: Unit | Blocks: Implementation

**AC-LS-12** — Rejected when `heldFreePoints == 0`
Given: A Warrior with `heldFreePoints = 0`.
When: `AllocateFreePoint(entity, StatID.STR)` is called.
Then: Guard 1 fires. `heldFreePoints` remains `0`. No `SetBaseStat` call. UI feedback returned.
Type: Unit | Blocks: Implementation

**AC-LS-13** — Invalid StatID rejected: free point NOT consumed
Given: A Warrior with `heldFreePoints = 2`.
When: `AllocateFreePoint(entity, StatID.MaxHP)` is called.
Then: Guard 2 fires (before decrement). Error logged. `heldFreePoints` remains `2`. No `SetBaseStat` call.
Type: Unit | Blocks: Implementation

**AC-LS-14** — Free-point spend does NOT restore CurrentHP or CurrentMP
Given: A Warrior mid-combat with `CurrentHP = 150`. `heldFreePoints = 1`.
When: `AllocateFreePoint(entity, StatID.VIT)` is called.
Then: MaxHP increases via F-3 recompute. `CurrentHP` remains `150`. The Leveling System does not call `SetBaseStat(CurrentHP, ...)` during free-point allocation.
Type: Unit | Blocks: Implementation

**AC-LS-15** — Last free point spent: counter reaches 0, not −1
Given: A Warrior with `heldFreePoints = 1`.
When: `AllocateFreePoint(entity, StatID.DEX)` is called.
Then: `heldFreePoints` becomes `0`. Subsequent call immediately hits guard 1 and is rejected. `GetHeldFreePoints(entity)` returns `0`.
Type: Unit | Blocks: Implementation

**AC-LS-16** — Tier multiplier derived on-demand during free-point spend
Given: A Warrior at L20 (`LevelTierMultiplier = ×1.2`). `heldFreePoints = 1`.
When: `AllocateFreePoint(entity, StatID.STR)` is called.
Then: F-3–F-9 recompute uses ×1.2. `AttackPower = Mathf.FloorToInt((10 + STR_new × 2) × 1.2)`. Multiplier is derived on-demand from `GetBaseStat(Level)` — no stored multiplier field.
Type: Unit | Blocks: Implementation

---

### Group 3 — Respec (CR-4)

**AC-LS-17** — Respec commit uses BeginStatTransaction / EndStatTransaction
Given: A Warrior at L10. Phase 1 is complete — respec item is reserved and the respec screen is open. The player has configured a new allocation that differs from the current stat distribution.
When: The player confirms commit — Inventory System calls `TryApplyRespec(entity)` (CR-4.3–CR-4.4 execute).
Then: `BeginStatTransaction()` is called before any `SetBaseStat` in the commit. All `OnStatChanged` events are deferred until `EndStatTransaction()` fires. No `OnStatChanged` fires between the two transaction calls.
Type: Integration | Blocks: Implementation

**AC-LS-18a** — Respec combat gate enforced before item reservation (unit, runnable with stub)
Given: A Warrior with `HasCombatTaggedEffect(EntityID)` stubbed to return `true`.
When: The Inventory System's Phase 1 gate check fires.
Then: `TryApplyRespec()` is never called. The respec item remains in active inventory (not reserved, not consumed). No stats change. The stubbed interface verifies the gate contract independently of the Status Effects GDD.
Type: Unit | Blocks: Implementation

**AC-LS-18b** — Respec combat gate: full integration path (blocked)
Given: A Warrior with an active combat-tagged Status Effect (as defined by the Status Effects GDD — OQ-LS-3).
When: The UI flow attempts to initiate respec.
Then: The real `HasCombatTaggedEffect()` returns `true`. Inventory System gate rejects. Item untouched. `TryApplyRespec()` is never called.
Type: Integration | Blocks: Implementation | **BLOCKED on OQ-LS-3 / Status Effects GDD (not yet designed)**

**AC-LS-19** — Respec preserves `heldFreePoints` after commit
Given: A Warrior at L20 with `heldFreePoints = 5`.
When: Respec commits (CR-4.4 step 4).
Then: `heldFreePoints` remains `5` (unchanged). `GetHeldFreePoints(entity)` returns `5`. The redistributable pool was `(20−1)×1 − 5 = 14` spent points — only those were reallocated; the 5 held points were never touched.
Type: Unit | Blocks: Implementation

**AC-LS-20** — Respec auto-alloc floor enforced in real time
Given: A Warrior at L10. STR auto-alloc floor = `10 + (10−1)×2 = 28`. Player attempts to set STR to `27` in the respec screen.
When: The allocation is attempted.
Then: The UI snaps STR back to the floor of 28 within the same input event. Commit button remains disabled while any stat is below floor or any redistributable points remain unallocated. `TryApplyRespec()` is never called with below-floor values — the system layer must also reject below-floor submissions as a defense-in-depth check.
Type: Integration | Blocks: Implementation

**AC-LS-21** — Respec: MaxMP below CurrentMP triggers Character Stats EC-05
Given: A Healer at L20 with `CurrentMP = 800`, `MaxMP = 900`. Respec lowers MaxMP to `700`.
When: `SetBaseStat(MaxMP, 700)` fires inside the `BeginStatTransaction` window.
Then: Character Stats EC-05 fires: `CurrentMP = min(800, 700) = 700`. `OnStatChanged(CurrentMP)` deferred until `EndStatTransaction`. HUD sees both `MaxMP = 700` and `CurrentMP = 700` in one pass. Player is not killed.
Type: Integration | Blocks: Implementation

**AC-LS-22** — Exception mid-respec: stats rolled back, item returned to inventory
Given: An exception is injected during CR-4.4 step 2.
When: The exception propagates.
Then: `RollbackStatTransaction()` called unconditionally inside `TryApplyRespec()` catch block. Deferred events discarded. All stats return to pre-transaction values. `EndStatTransaction()` never called. `TryApplyRespec()` re-throws — Inventory System catches and calls `ItemReservation.Release()`. Observable: player's inventory count of respec items is unchanged (item returned). No `OnStatChanged` fires to HUD for any stat modified in the aborted transaction.
Type: Integration | Blocks: Implementation

**AC-LS-52** — Respec reduces MaxHP below CurrentHP: `OnEntityDied` does not fire (EC-LS-20)
Given: A Warrior at L20 with `CurrentHP = 400`. Respec moves VIT points to another stat, reducing MaxHP to `350`. `SetBaseStat(MaxHP, 350)` fires inside the `BeginStatTransaction()` window (CR-4.4 step 3).
When: The `SetBaseStat(MaxHP, 350)` call executes.
Then: Character Stats EC-05 fires: `CurrentHP = min(400, 350) = 350`. `OnEntityDied` does **NOT** fire — this is a schema clamp, not a damage event. MaxHP schema minimum is 1; the clamp cannot produce `CurrentHP = 0`. `OnStatChanged(CurrentHP)` is deferred until `EndStatTransaction()`. HUD sees corrected `CurrentHP = 350` in a single notification pass. Player is not killed.
Type: Integration | Blocks: Implementation

---

### Group 4 — Level Cap (CR-5)

**AC-LS-23** — XP at cap: no write, no events if already at `XpThreshold[60]`
Given: L60 character, `Experience == XpThreshold[60]`.
When: `AddExperience(entity, 500)` is called.
Then: `Experience` unchanged. `OnStatChanged` does NOT fire. `OnExperienceThresholdCrossed` does NOT fire. Zero `SetBaseStat` calls on a spy.
Type: Unit | Blocks: Implementation

**AC-LS-24** — CR-2.1 at-cap guard aborts sequence when Level == 60
Given: `GetBaseStat(Level) == 60` when `OnExperienceThresholdCrossed` is invoked.
When: The Leveling System's handler executes.
Then: CR-2.1 guard fires. `SetBaseStat(Level, 61)` never called. No auto-alloc, no F-3–F-9 recompute, no `OnLevelUp` broadcast.
Type: Unit | Blocks: Implementation

**AC-LS-25** — XpThreshold sentinel at index 61 prevents out-of-bounds
Given: `XpThreshold` initialized with `XpThreshold[61] = int.MaxValue`.
When: CR-2.9 evaluates `Experience >= XpThreshold[61]` at L60.
Then: No `IndexOutOfRangeException`. Condition evaluates false. `XpThreshold.Length >= 62` and `XpThreshold[61] == int.MaxValue` asserted.
Type: Unit | Blocks: Implementation

**AC-LS-26** — `heldFreePoints` spendable at L60
Given: L60 Warrior with `heldFreePoints = 3`.
When: `AllocateFreePoint(entity, StatID.STR)` is called.
Then: Allocation proceeds normally. `heldFreePoints` becomes `2`. F-3–F-9 recomputed with ×2.0.
Type: Unit | Blocks: Implementation

---

### Group 5 — Spawn and Load (CR-6)

**AC-LS-27** — InitializeAtL1: no OnLevelUp, no OnExperienceThresholdCrossed
Given: Freshly allocated `CharacterStats`. Spies on `OnLevelUp` and `OnExperienceThresholdCrossed`.
When: `LevelingSystem.InitializeAtL1(entity, ClassType.Warrior)` is called.
Then: `GetBaseStat(Level)` = 1. All primary attributes = 10. `Experience` = 0. `heldFreePoints` = 0. `OnLevelUp` fires zero times. `OnExperienceThresholdCrossed` fires zero times. F-3–F-9 computed with ×1.0.
Type: Unit | Blocks: Implementation

**AC-LS-28** — Load from persistence: no level-up events, no F-3–F-9 recompute
Given: Character Persistence restores a L35 Warrior via `SetBaseStat()` calls. Spies on `OnLevelUp` and `OnExperienceThresholdCrossed`.
When: `RestoreLevelingState(entity, {heldFreePoints: 7})` is called.
Then: `OnLevelUp` fires zero times. `OnExperienceThresholdCrossed` fires zero times. No F-3–F-9 recompute triggered. `GetHeldFreePoints(entity)` returns `7`.
Type: Integration | Blocks: Implementation

**AC-LS-29** — `heldFreePoints` persistence round-trip is lossless
Given: A Healer at L30 with `heldFreePoints = 14`.
When: `GetLevelingState(entity)` is called (save), then `RestoreLevelingState(entity, {heldFreePoints: 14})` on a fresh instance.
Then: `GetHeldFreePoints(entity)` returns `14`. The value is NOT recoverable from `GetBaseStat()` — `StatID.heldFreePoints` does not exist in the schema.
Type: Integration | Blocks: Implementation

**AC-LS-30** — `heldFreePoints` validation clamps corrupted values on load
Given: Corrupted save provides `heldFreePoints = 500` for a L60 Warrior (max = 59). Separately, `heldFreePoints = -5`.
When: `RestoreLevelingState()` is called for each.
Then: `500` clamped to `59`. `-5` clamped to `0`. Error logged in each case.
Type: Unit | Blocks: Implementation

**AC-LS-51** — Corrupted Level value on load: clamped to valid range (EC-LS-38)
Given: Character Persistence restores `Level = 0` (Case A) and `Level = 70` (Case B).
When: `RestoreLevelingState()` executes after the `SetBaseStat(Level, ...)` calls.
Then: Case A: `GetBaseStat(Level)` returns `1` (clamped from 0). Error logged. Case B: `GetBaseStat(Level)` returns `60` (clamped from 70). Error logged. In both cases: no `IndexOutOfRangeException`. `LevelTierMultiplier` derived correctly from clamped value. AtCap state (`Level == 60`) derived correctly for Case B.
Type: Unit | Blocks: Implementation

---

### Group 6 — Formula Verification (F-LS-1 through F-LS-4)

**AC-LS-31** — F-LS-1: spot-check XpThreshold cumulative values
Given: `XpThreshold` array fully populated as a cumulative baseline (see F-LS-1 XpThreshold array note).
When: Queried at key indices.
Then: `XpThreshold[1] = 0` (start of L1, no XP accumulated). `XpThreshold[2] = 200` (XP to reach L2). `XpThreshold[20] = 65,824` (formula-derived, exact: XpThreshold[21] = 77,785 minus XP(20) = 11,961 — verified in double precision and implemented in Story 010; corrected 2026-09-25, see F-LS-1 worked example, EC-LS-35, and TD-038 — the original ≈62,728 figure was an imprecise hand-arithmetic approximation). `XpThreshold[61] = int.MaxValue` (sentinel). No value in `[1, 60]` exceeds `int.MaxValue`. The exact XpThreshold integer constants must be pre-computed in double precision and stored as a reference table, with the runtime float formula output verified against that table at build time — **shipped in Story 010** (`src/Foundation/LevelingSystem/XpThresholdTable.cs`), quadruple-verified across four independent computations, and **economy-designer sign-off granted (2026-09-25)** on the array/curve itself (pacing, hook strength, and curve shape confirmed sound for MVP).
*Split out of this criterion (2026-09-25):* validating the "~205h cap-time anchor" against realistic per-tier mob XP rates and producing a co-signed per-tier-hours estimate is **not** satisfiable yet — it depends on `MobDefinition.KillXP` values and a zone-XP GDD, neither of which exist. That work is descoped to whichever future story populates real mob XP data; see TD-039 for the specific divergence found in the meantime (the GDD's own "Est. Kill Time" column does not currently reconcile with its stated methodology).
Type: Unit | Blocks: Implementation

**AC-LS-32** — F-LS-1: XpThreshold is monotonically increasing L1–L59
Given: `XpThreshold` fully populated.
When: Iterated from index 1 to 59.
Then: `XpThreshold[i] < XpThreshold[i+1]` for all `i` in `[1, 58]`.
Type: Unit | Blocks: Implementation

**AC-LS-33** — F-PS-1: party XP detriment multipliers correct at all party sizes *(canonical; F-LS-2 superseded)*
Given: `XP_base = 1000`. `XP_base = 333` for the float-rounding edge case.
When: F-PS-1 (`party-system.md`) is evaluated for N=1, 2, 3, 4.
Then: N=1 → 1000. N=2 → 900. N=3 → 800. N=4 → 700. For `XP_base = 333`, N=4: `Mathf.RoundToInt(333 × 0.7f) = 233`. Conversion method is `Mathf.RoundToInt`, not `(int)` cast.
Type: Unit | Blocks: Implementation

**AC-LS-34** — F-LS-3: LevelTierMultiplier boundary values correct
Given: Characters at levels 1, 19, 20, 39, 40, 59, 60.
When: `LevelTierMultiplier` derived on-demand for each.
Then: L1=×1.0, L19=×1.0, L20=×1.2, L39=×1.2, L40=×1.5, L59=×1.5, L60=×2.0. No stored multiplier field — derived from `GetBaseStat(Level)`.
Type: Unit | Blocks: Implementation

**AC-LS-35** — F-LS-4: Warrior L60 auto-alloc snapshot
Given: Warrior initialized at L1, levels to L60 with zero free-point spends.
When: Stats queried at L60.
Then: STR=128, DEX=69, VIT=69, INT=10. `heldFreePoints = 59`.
Type: Unit | Blocks: Implementation

**AC-LS-36** — F-LS-4: Healer L60 auto-alloc snapshot
Given: Healer initialized at L1, levels to L60 with zero free-point spends.
When: Stats queried at L60.
Then: STR=10, DEX=10, VIT=69, INT=128. `heldFreePoints = 118`.
Type: Unit | Blocks: Implementation

---

### Group 7 — Edge Cases

**AC-LS-37** — Multi-level XP grant compounds attribute growth correctly
Given: A Warrior at L2 (STR=12: base 10 + one level-up of +2) receives a single XP grant crossing three thresholds (L2→L3→L4→L5).
When: `AddExperience(entity, largeAmount)` resolves.
Then: `GetBaseStat(Level)` returns `5`. STR at L5 = `18` (STR=12 at L2, three auto-alloc iterations of +2 each: 12+2+2+2=18). `heldFreePoints = 3`. `OnLevelUp` fires three times (L3, L4, L5 in sequence), all after all iterations complete.
Type: Unit | Blocks: Implementation

**AC-LS-38** — L19→L20 tier transition uses from-scratch ×1.2 recompute
Given: A Warrior at L19 (18 level-ups from L1: STR=10+18×2=46, VIT=10+18×1=28, DEX=10+18×1=28). After L20 auto-alloc (CR-2.4): STR=48, VIT=29, DEX=29.
When: L20 CR-2.6 executes with `LevelTierMultiplier = ×1.2`.
Then: `MaxHP = Mathf.FloorToInt((200 + 29×20) × 1.2) = Mathf.FloorToInt(780 × 1.2) = Mathf.FloorToInt(936) = 936`. Not computed as a delta from the L19 value. All F-3–F-9 computed from scratch with post-auto-alloc totals × ×1.2.
Type: Unit | Blocks: Implementation

**AC-LS-53** — L39→L40 tier transition uses from-scratch ×1.5 recompute
Given: A Warrior at L39 (38 level-ups from L1: STR=10+38×2=86, VIT=10+38×1=48, DEX=10+38×1=48). After L40 auto-alloc (CR-2.4): STR=88, VIT=49, DEX=49.
When: L40 CR-2.6 executes with `LevelTierMultiplier = ×1.5`.
Then: `MaxHP = Mathf.FloorToInt((200 + 49×20) × 1.5) = Mathf.FloorToInt(1180 × 1.5) = Mathf.FloorToInt(1770) = 1770`. `LevelTierMultiplier` = ×1.5 (not ×1.2). All F-3–F-9 computed from scratch with post-auto-alloc totals × ×1.5.
Type: Unit | Blocks: Implementation

**AC-LS-54** — L59→L60 tier transition: from-scratch ×2.0 recompute, XP clamped by CR-2.2a
Given: A Warrior at L59 with STR=126, VIT=68, DEX=68 (L59 = 58 level-ups from L1: STR=10+58×2=126, VIT=10+58=68, DEX=10+58=68; full auto-alloc, zero free-point spends). After L60 auto-alloc (the 59th level-up): STR=128, VIT=69, DEX=69.
When: L60 CR-2.2a then CR-2.6 execute.
Then: CR-2.2a fires: `GetBaseStat(Experience) == XpThreshold[60]` (any overshoot clamped). `MaxHP = Mathf.FloorToInt((200 + 69×20) × 2.0) = Mathf.FloorToInt(3160) = 3160`. `LevelTierMultiplier` = ×2.0 (not ×1.5). All F-3–F-9 computed from scratch with new totals × ×2.0. HUD bar fill: Level==60 guard renders `fill = 1.0` constant (formula bypassed — see UI Requirements L60 guard note).
Type: Unit | Blocks: Implementation

**AC-LS-40** — AddExperience(0) and AddExperience(−1) rejected
Given: A Warrior at L5 with `Experience = 3000`.
When: `AddExperience(entity, 0)` and `AddExperience(entity, -100)` are each called.
Then: `Experience` remains `3000`. Error logged. `OnStatChanged` does NOT fire. Monotonic invariant preserved.
Type: Unit | Blocks: Implementation

**AC-LS-41** — `heldFreePoints` never goes negative
Given: A Warrior with `heldFreePoints = 1`. First `AllocateFreePoint` succeeds (→ 0). Second call made.
When: Second call executes.
Then: Guard 1 fires. `GetHeldFreePoints(entity)` returns `0`. No path produces a value below zero.
Type: Unit | Blocks: Implementation

**AC-LS-42** — CritChance and ASM written raw (unclamped) to SetBaseStat
Given: Entity with DEX injected directly via test seam to `DEX = 467` (above what any class can accumulate under current rules — this is a **future-proofing constant**, unreachable in normal play with current Warrior/Healer stat caps; test uses stat injection to validate the ceiling path). `CritChance_raw = 0.05 + (467 × 0.0015 × 2.0) = 1.451f`.
When: CR-2.6 executes (or `AllocateFreePoint` triggers F-9 recompute).
Then: `SetBaseStat(CritChance, 1.451f)` called — raw value, not pre-clamped. `GetBaseStat(CritChance)` returns `1.451f`. `GetEffectiveStat(CritChance)` returns `0.75f` (F-1 clamp applied at query time). The Leveling System does not pre-clamp. **Note:** At current MVP stat caps (max DEX=187), the CritChance ceiling (0.75) is unreachable in normal play. This AC tests a protective constant for future classes or balance changes — not a live production scenario.
Type: Unit | Blocks: Implementation

**AC-LS-43** — MaxMP ceiling: Leveling System clamps to 9,999 before SetBaseStat
Given: Entity with INT injected directly via test seam to `INT = 420` (above what any class can accumulate under current rules — this is a **future-proofing constant**, unreachable in normal play with current stat caps; max INT for Healer is 246 yielding MaxMP=6,104, well below 9,999. Test uses stat injection to validate the ceiling path). F-4 raw = `Mathf.FloorToInt((100 + 420×12) × 2.0) = Mathf.FloorToInt(10,168) > 9,999`.
When: CR-2.6 step 2 (or CR-3.2 step 5) executes.
Then: `SetBaseStat(MaxMP, 9999)` called — clamped, not the raw 10,168. `GetBaseStat(MaxMP)` returns `9999`. Free point (if applicable) still consumed. **Note:** At current MVP stat caps, MaxMP ceiling is unreachable in normal play. This AC tests a protective constant for future classes or balance changes.
Type: Unit | Blocks: Implementation

**AC-LS-44** — Corrupted `heldFreePoints` negative value clamped to 0 on load
Given: Corrupted save provides `heldFreePoints = -5`.
When: `RestoreLevelingState(entity, {heldFreePoints: -5})` is called.
Then: Error logged. `GetHeldFreePoints(entity)` returns `0`.
Type: Unit | Blocks: Implementation

**AC-LS-45** — L60 character loaded: AtCap state derived immediately, no spurious events
Given: Character Persistence restores a L60 Warrior. First post-load `AddExperience` call follows.
When: `AddExperience(entity, 1000)` is called after load.
Then: CR-5.2 clamp fires immediately. Zero `OnLevelUp` during load or post-load XP call. AtCap state derived from `GetBaseStat(Level) == 60`, not a stored enum.
Type: Integration | Blocks: Implementation

**AC-LS-46** — Tier-transition level-up: longer hold, same overlay intensity *(ADVISORY)*
Given: Normal level-up (L5→L6) and tier-transition level-up (L19→L20).
When: `OnLevelUp` broadcasts in each case.
Then: Both level-ups produce the **same overlay intensity**. The tier-transition level number hold is **2.5s** (vs 1.5s for normal). Tier-transition audio has a slightly longer sustain and reverb tail — same instrument and character as the normal chime, not louder. No additional visual effects, stingers, or ambient audio pauses for the tier transition. The longer hold is the only observable distinction. Lead sign-off required. Screenshot + audio review saved to `production/qa/evidence/`.
Type: Manual | Blocks: ADVISORY

**AC-LS-47** — HUD XP bar: full and static at L60, "MAX" displayed *(ADVISORY)*
Given: A L60 character.
When: HUD renders.
Then: Bar fill = 1.0. Bar does not animate. Level display shows "MAX", not "60". No XP text shown. Screenshot saved to `production/qa/evidence/`.
Type: Manual | Blocks: ADVISORY

**AC-LS-48** — Respec screen: floor in muted color, commit blocked with unallocated points *(ADVISORY)*
Given: A Warrior at L10 opens the respec screen.
When: Screen renders.
Then: Auto-alloc floor values (STR=28, DEX=19, VIT=19) in muted color. Commit button disabled while any redistributable points unallocated.
Type: Manual | Blocks: ADVISORY

---

*Implementation notes:*
- *AC-LS-10 requires `_levelingUpInProgress` to be testably observable (e.g., `IsLevelingUpInProgress` on the public interface). Confirm seam with Lead Programmer before implementation sprint.*
- *AC-LS-18 (respec combat gate) depends on Status Effects GDD defining "combat-tagged." Mark as pending dependency in sprint planning.*
- *AC-LS-33 (F-PS-1 float-to-int) — Party System GDD confirmed `Mathf.RoundToInt` (OQ-LS-4 resolved 2026-05-17). AC test values should use F-PS-1 detriment direction (×0.70 at N=4), not F-LS-2 bonus values.*

## Open Questions

**OQ-LS-1** — RESOLVED (2026-04-30): Two-phase commit with `TryApplyRespec` ownership model adopted (CR-4.1 updated).
The Inventory System marks the item reserved (Phase 1) then calls `LevelingSystem.TryApplyRespec(EntityID)` (Phase 2). `TryApplyRespec` executes the stat rewrite and may throw; the Inventory System owns the `ItemReservation` handle and calls `Release()` on exception or `Consume()` on success. Item loss occurs only on successful commit. ADR required before implementation to document the reservation protocol interface between Inventory System and Leveling System.

**OQ-LS-2** — Interface gap: respec item cost formula
Who defines the gold/item cost of a respec scroll? The Leveling System owns the mechanic; the Economy Designer owns the item economy. The scroll's cost cannot be calibrated until the Enhancement System GDD is authored (a respec should cost less than total grind investment but more than trivial). Carries to Economy System GDD.

**OQ-LS-3** — Interface gap: "combat-tagged" Status Effect definition
CR-4.6 requires `HasCombatTaggedEffect(EntityID)` on the Status Effects System. What constitutes "combat-tagged" (debuffs only? all non-passive effects? player-applied buffs?) must be defined in the Status Effects GDD before AC-LS-18 can be implemented or tested. Carries to Status Effects GDD.

**OQ-LS-4** — RESOLVED (2026-05-17): Party System GDD (Approved 2026-05-17) confirmed `Mathf.RoundToInt` as the float→int conversion before calling `AddExperience()`. Interface contract closed. F-LS-2 superseded by F-PS-1_PartyXpDeduction — canonical formula is in party-system.md.

**OQ-LS-5** — Implementation concern: `_levelingUpInProgress` testability seam
AC-LS-10 requires `_levelingUpInProgress` to be observable from a unit test. The Leveling System must expose a read-only property (e.g., `bool IsLevelingUpInProgress`) on its public interface. Confirm seam design with Lead Programmer before implementation sprint.

**OQ-LS-6** — Design gap: zone social announcement on level-up
CR-2.10 defers a zone-wide social announcement ("Player X reached Level N!") to post-MVP. This decision affects the Social Gravity pillar: the level badge updating silently on nearby players' screens is a different signal than a broadcast announcement. **Design intent:** A zone-wide announcement is planned for a post-MVP patch. At MVP, the only social signal is the level badge update on the observing client's entity view. The networking layer must not design around the assumption of a broadcast announcement at MVP. When the zone social system is designed, it must specify: (a) which level milestones trigger an announcement (all levels, tier walls only, or milestone levels only), (b) announcement range (zone-wide vs. AOI radius), and (c) whether the text is visible to the leveling player or only to observers.

**OQ-LS-7** — RESOLVED (2026-09-24): `GetXPAward(EntityID): int` specification

`LevelingSystem.GetXPAward(EntityID targetId): int` looks up `targetId`'s `MobDefinition` via `IMobDefinitionRegistry.GetDefinition(MobTypeID)` (Enemy AI, already Approved) and returns `IsEnraged ? EnragedKillXP : KillXP` — both already-Approved flat `int` fields on `MobDefinition` (`enemy-ai.md`'s `MobDefinition` schema: `KillXP > 0`; `EnragedKillXP = Mathf.RoundToInt(KillXP × EnragedXPMultiplier)`, computed once at spawn per F-AI-E-1). Resolving the four sub-questions this OQ originally raised:

(a) **Data source**: flat per-mob-type field on `MobDefinition` (`KillXP`) — not a dynamic formula from mob level, not a separate lookup table. Already authored and Approved in `enemy-ai.md`.
(b) **Level-differential modifier**: none at MVP. `GetXPAward` does not take the attacker's level as a parameter, and there is no penalty for farming below-level mobs — killing any instance of a given mob type yields that mob type's flat `KillXP` (or `EnragedKillXP`) regardless of the killer's level. If a future milestone wants level-differential XP, that is a new, separately-scoped design change, not part of this resolution.
(c) **Schema**: already specified — see `enemy-ai.md`'s `MobDefinition` schema (`KillXP: int, > 0`; `EnragedXPMultiplier: float`, default 1.5, range [1.0, 3.0]).
(d) **Authoring/maintenance**: whoever authors `MobDefinition` assets (Enemy AI's existing content-authoring process) — the same owner as `MaxHP`/`AttackPower`/every other per-mob-type field, not a new "Mob Definition sub-GDD" or "Economy System XP Rate appendix" as originally speculated; no such separate document is needed since `MobDefinition` already exists and already owns this data.

**Cross-document correction applied alongside this resolution**: `enemy-ai.md`'s `Dead`-state transition table and its "Character Persistence" interactions subsection incorrectly described Enemy AI awarding XP directly (`CharacterPersistence.AwardXP(killerEntityID, ...)`) on kill — this predated `damage-calculation.md`'s Option B decision and, if left as written, would have double-awarded XP (once via Enemy AI's own Dead-state entry, again via the killer's controller's CR-1.1 sequence, since `ApplyDamage` — which triggers the `Dead` transition — is the LAST step in that sequence, after XP has already been awarded). Corrected in `enemy-ai.md` to remove Enemy AI's own redundant XP-award claim; `MobDefinition.KillXP`/`EnragedKillXP` remain exactly where they were, now correctly documented as data `GetXPAward` reads rather than data Enemy AI acts on directly.

This resolves the 205h cap-time anchor's dependency: `XpThreshold`'s cumulative derivation (Story 010, AC-LS-31) can now be validated against real `KillXP` ranges once Enemy AI's mob roster is authored, and economy-designer sign-off is unblocked to proceed.
