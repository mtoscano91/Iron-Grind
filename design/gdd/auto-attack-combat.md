# Auto-Attack Combat

> **Status**: Approved — Reactive Cooldown Model (2026-05-27)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-27
> **Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

## Overview

Auto-Attack Combat is the foundational combat loop of Iron Grind. Every melee character automatically attacks the selected target on a reactive 1.0-second cooldown — no button press required to maintain the attack rhythm. The auto-attack fires a fixed duration after the last action, where "action" means either a completed auto-attack or a skill cast. Skill damage is always additive: both the skill and the auto-attack deal full damage, with no suppression of either. The tradeoff is timing: casting a skill resets the auto-attack countdown, so a player who casts before the auto fires pushes the next auto further out. A player who waits for the auto to land and casts immediately after loses only 1–2 ticks of countdown — a negligible delay. An impatient player who casts repeatedly before the auto is ready compounds the delay with every press. Hits resolve when the character is within melee range of the target; the character can move freely and attack simultaneously — there is no stop-to-swing requirement. When auto-attack is first activated, the character rotates to face the selected target on the first auto; subsequent autos do not force a rotation. Mastering this system means feeling when the auto is about to fire, letting it land, and casting a skill immediately after — a discipline that separates high-performing players from button-mashers on identical gear.

## Player Fantasy

Combat in Iron Grind is not a dance — it is a job, done well. The auto-attack is your steady hand; your skills are the decisions you make around it. A new player mashes and watches their auto reset over and over, never landing. A veteran feels the timer in their bones — watches the charge bar peak, lets the auto land, and presses their skill a heartbeat after, doubling up without thinking about it. The fantasy is becoming that veteran — the kind of warrior other players watch fight and recognize, before they ever see the +9 on his sword.

**The anchor moment:** You're at 20% HP, fighting an enemy whose next attack will likely kill you. Your skill is off cooldown. Every instinct says cast now. But your charge bar is nearly full — the auto is a fraction of a second from firing — and you know that if you cast now, you push the auto back and the enemy survives. You hold. The auto lands. You skill. He dies. That held breath is the game.

## Detailed Design

### Core Rules

**Combat Activation**

1. Auto-attack activates when **both** conditions are met: (a) the player has a valid target selected, and (b) the auto-attack toggle is ON. Either condition alone is insufficient. If a target is selected while the toggle is OFF, the system stays in IDLE. If the toggle is turned ON with no target, the system stays in IDLE. A valid target is: alive, in the same zone instance, not a friendly unit. "Friendly" determination is provided by an external targeting system as `bool isValidTarget`.
2. On activation, the action timer `_actionTimer` (float, seconds) resets to 0.0. The first auto fires after `CycleDuration` seconds.
3. `_actionTimer` resets to 0.0 on every new target selection — including re-selecting the same target.
4. Auto-attack deactivates when: (a) the target dies, (b) the player manually deselects, (c) the auto-attack toggle is turned OFF, (d) an external system sets `SetCombatProhibited(true)`. On deactivation, `_actionTimer` freezes at its current value.

**Reactive Cooldown Model**

5. The server advances `_actionTimer` by `deltaTime` each server frame. When `_actionTimer >= CycleDuration`, the auto-attack fires (subject to state checks in Rule 10). After firing: `_actionTimer = 0.0`.
6. `CycleDuration = 1.0 / AttackSpeedMultiplier`. At `AttackSpeedMultiplier = 1.0`, this is exactly 1.0 second.
7. `_actionTimer` advances continuously and is never paused — including during stun, knockback, or any prohibited state. When the character is in a prohibited state, `_actionTimer` still advances but the auto-attack does not fire (Step 1 of Rule 10 skips it). This means the auto is "ready" as soon as prohibition clears.
8. Auto-attack resolution is server-authoritative only. The client maintains a predicted `_clientActionTimer` for charge bar animation only. It never drives damage.
9. The server broadcasts `_actionTimer` to all clients every N milliseconds (see Tuning Knobs). Clients apply the received value with smoothing to avoid visible bar stutter.

**Auto-Attack Resolution**

10. When `_actionTimer >= CycleDuration`, the server resolves in this fixed order:
    - **Step 1:** If `SetCombatProhibited` is active → skip to Step 5 (no damage). `_actionTimer` does NOT reset — it stays at its current value so the auto fires immediately when prohibition clears.
    - **Step 2:** Hit Detection — call `CheckHit(attackerPos, targetPos, AttackRange, capturedForward)` where `capturedForward = normalize(targetPos − attackerPos)` (player auto-attacks always pass the facing check). If `HitResult != Hit` → skip to Step 5. Server emits `HitCheckResult` (R-U batch) to the attacker's client regardless of result.
    - **Step 3 (auto fires):** Call Damage Calculation with `(BaseDamage, AttackerID, TargetID, DamageContext.PhysicalAuto)`. If `DamageResult.IsKill == true`, execute the kill sequence in this exact order, matching Skill System's CR-SK-6 (`skill-system.md`): `GetXPAward(TargetID): int` → `AddExperience(AttackerID, xpAmount)` → `ApplyDamage(TargetID, DamageResult.FinalDamage)`. If `IsKill == false`, apply `DamageResult.FinalDamage` to target health directly (no XP sequence). Use `DamageResult.IsCrit` for VFX routing and `DamageResult.IsKill` to trigger IDLE transition. *(Kill-sequence text added 2026-09-24, leveling-system.md OQ-LS-7 resolution — this GDD previously applied damage on a kill without specifying the XP-award step at all, unlike Skill System's already-explicit CR-SK-6; `damage-calculation.md`'s own downstream-dependency table already required this of Auto-Attack Combat, so this closes that gap rather than introducing new behavior.)*
    - **Step 4:** `_actionTimer = 0.0`. Broadcast reset to clients.
    - **Step 5 (no-fire exit):** `_actionTimer` is NOT reset. The auto will re-attempt on the next server tick.
11. Auto-attack damage and skill damage are always independent and additive. Skills never suppress auto-attack damage; the auto-attack never suppresses skill damage.

**Skill Interaction**

12. When the Skill system confirms a skill cast, it calls `CombatSystem.NotifySkillUsed(casterEntityID)` server-side. The only effect on this system is: `_actionTimer = 0.0`. The auto-attack countdown restarts from zero.
13. `NotifySkillUsed()` is called only for validated, accepted skill casts. Rejected casts do not reset the timer.
14. A skill that misses or is absorbed still resets `_actionTimer`. The reset records intent, not outcome.
15. Skill and auto-attack damage are fully independent. A skill cast does not cancel, suppress, or delay a currently-resolving auto-attack.

**Auto-Face**

20. On the first Beat Event after auto-attack activates (and only the first), the server resolves auto-face: the attacker's rotation is set to face the target AND the movement direction is redirected toward the target. Both are broadcast to all clients as server-authoritative.
21. Subsequent beats do not modify facing or movement direction. The player controls both freely after the first beat.
22. Auto-face fires regardless of the angular difference between attacker's current facing and the target direction — even a 180° snap is allowed. *(Flag for visual review — may feel jarring at extreme angles.)*

**Target Invalid Mid-Cycle**

23. If the target becomes invalid (dies, disconnects, phases out) while `_actionTimer` is counting up: enter IDLE, freeze `_actionTimer` at its current value. Skill damage already resolved by the Skill System is unaffected — this system owns only the auto-attack countdown.

---

### States and Transitions

**Top-Level Combat States**

| State | Description | Entry | Exit |
|-------|-------------|-------|------|
| `IDLE` | No target. `_actionTimer` frozen. | Startup; target lost or deselected; auto-attack toggled off | Valid target selected AND auto-attack activated |
| `COMBAT_ACTIVE` | `_actionTimer` running. Auto fires when timer expires. | Valid target selected AND auto-attack activated | Target dies; deselected; auto-attack toggled off; prohibited |
| `OUT_OF_RANGE` | Target selected, attacker outside `AttackRange`. Timer runs; auto skipped at Step 2 but timer does NOT reset. | Attacker moves beyond range | Attacker re-enters range; target deselected |
| `PROHIBITED` | External lock (stun, mount). Timer runs; auto skipped at Step 1 but timer does NOT reset. | `SetCombatProhibited(true)` | `SetCombatProhibited(false)` |

There are no within-cycle sub-states. `_actionTimer` is the sole state variable for the auto-attack countdown. Skill casts interact with this system only by resetting `_actionTimer` via `NotifySkillUsed()`.

---

### Interactions with Other Systems

| System | Direction | Data | Notes |
|--------|-----------|------|-------|
| Character Stats | → Combat | `BaseDamage` (float), `AttackRange` (float), `AttackSpeedMultiplier` (float) | Queried once per Beat Event. Not cached — picks up mid-combat buff changes. *Provisional: BaseDamage assumed flat, not a range.* |
| Damage Calculation | Combat → | `BaseDamage`, `AttackerID`, `TargetID`, `DamageContext.PhysicalAuto` | Returns `DamageResult` struct (`FinalDamage`, `IsCrit`, `IsKill`, `DamageContext`). DamageCalc reads target Defense and MagicDefense internally via TargetID. |
| Hit Detection | Combat → | `attackerPos`, `targetPos`, `AttackRange`, `capturedForward = normalize(targetPos − attackerPos)` | Returns `HitResult` enum (Hit / MissOutOfRange / MissFacing). Player auto-attacks always pass the facing check. Server emits `HitCheckResult` (R-U batch) on every call. |
| Skill System | Skill → Combat | `NotifySkillUsed(casterEntityID)` | Resets `_actionTimer = 0.0` server-side. No other effect on this system. |
| Leveling System | Combat → | `GetXPAward(TargetID): int`, `AddExperience(AttackerID, xpAmount)` | Called on `DamageResult.IsKill == true` only, before `ApplyDamage` (Rule 10 Step 3). Added 2026-09-24, `leveling-system.md` OQ-LS-7 resolution — see that GDD for `GetXPAward`'s full specification. |
| Networking Core | ↔ Combat | Server clock sync; `CycleTimerBroadcast`; `AutoFaceRotation` broadcast | Mirror `NetworkTime.time` as clock source. *Provisional: Mirror confirmed; NGO avoided (Multiplay shut down March 2026).* |
| Targeting System | → Combat | `isValidTarget` (bool), auto-attack toggle state | Combat does not evaluate targeting or toggle logic. |

## Formulas

### F-1: Cycle Duration

```
CycleDuration = 1.0 / AttackSpeedMultiplier
```

| Variable | Type | Range | Owner |
|----------|------|-------|-------|
| `AttackSpeedMultiplier` | float | [0.5, 2.0] | Character Stats |
| `CycleDuration` | float (seconds) | [0.5s, 2.0s] | Combat System (computed) |

**Clamping rule**: `AttackSpeedMultiplier = max(AttackSpeedMultiplier, 0.5)` before evaluation. Division by zero is impossible after clamping; values below 0.5 are treated as 0.5.

**Worked examples**:
- Multiplier = 1.0 → CycleDuration = 1.0s (base cadence)
- Multiplier = 2.0 → CycleDuration = 0.5s (fastest allowed)
- Multiplier = 0.5 → CycleDuration = 2.0s (slowest allowed)

---

### F-2: Range Check

```
isInRange = sqrt((T.x − A.x)² + (T.z − A.z)²) <= AttackRange
```

| Variable | Type | Notes |
|----------|------|-------|
| `A.x`, `A.z` | float | Attacker world position (X and Z axes only) |
| `T.x`, `T.z` | float | Target world position (X and Z axes only) |
| `AttackRange` | float | Base: 3.0 Unity world units. Provided by Character Stats. |
| `isInRange` | bool | Return value. No clamping needed. |

**Y-axis is ignored.** Vertical displacement between attacker and target does not affect hit detection. The check is a flat ground-plane circle.

**Worked example**: Attacker at (0, 1, 0), Target at (2.5, 3, 0) → distance = sqrt(2.5² + 0²) = 2.5 ≤ 3.0 → `isInRange = true`.

---

### F-3: Auto-Attack Fire Check

```
autoShouldFire = (_actionTimer >= CycleDuration)
                 AND (combatState == COMBAT_ACTIVE)
                 AND (NOT isProhibited)
                 AND (isInRange)
```

| Variable | Type | Notes |
|----------|------|-------|
| `_actionTimer` | float (seconds) | Time elapsed since last action (auto fire or skill cast). Resets to 0.0 on any action. |
| `CycleDuration` | float (seconds) | From F-1. |
| `combatState` | enum | Must be `COMBAT_ACTIVE`. |
| `isProhibited` | bool | Set by `SetCombatProhibited()`. |
| `isInRange` | bool | From F-2. |

When `autoShouldFire == true`: execute Step 3 (damage resolution) and reset `_actionTimer = 0.0`.
When any condition is false: `_actionTimer` continues advancing. The auto fires as soon as all conditions are met.

**NotifySkillUsed() effect on timer:**

```
_actionTimer = 0.0   // resets on any accepted skill cast
```

| Scenario | Timer at cast | Timer after cast | Next auto fires at |
|----------|--------------|-----------------|-------------------|
| Cast right after auto (t+0.05s) | 0.05s | 0.0s | 1.0s later |
| Cast halfway through (t+0.50s) | 0.50s | 0.0s | 1.0s later (0.5s delay vs. no cast) |
| Cast just before auto (t+0.95s) | 0.95s | 0.0s | 1.0s later (0.95s delay vs. no cast) |

## Edge Cases

### Category 1 — Activation / Deactivation

**EC-1: Toggle ON with no target**
System stays in IDLE. `_actionTimer` stays frozen. No auto fires. When a valid target is subsequently selected, activation proceeds per Rule 2.

**EC-2: Target deselected mid-cycle**
System immediately transitions to IDLE. `_actionTimer` freezes at its current value.

**EC-3: Target deselected and re-selected in the same server frame**
Net result treated as fresh activation. `_actionTimer` resets to 0.0, system enters COMBAT_ACTIVE. Auto-face fires on the first auto per Rules 20–22.

**EC-4: Toggle OFF then ON again in the same server frame (target still valid)**
Net result is a re-activation. `_actionTimer` resets to 0.0. No partial-cycle state carries forward.

---

### Category 2 — State Transitions

**EC-5: Attacker re-enters range on the exact server frame the Beat fires**
State changes and Beat resolution are evaluated at Beat time. If range is true at Step 3 evaluation, auto fires normally. The re-entry is not penalized.

**EC-6: Attacker walks back into range (OUT_OF_RANGE → COMBAT_ACTIVE mid-cycle)**
No action at re-entry. `_actionTimer` continues advancing. If `_actionTimer >= CycleDuration` when re-entry is detected, auto fires immediately on the next server tick. No timer reset on range re-entry.

**EC-7: Prohibition lifted mid-cycle**
System transitions to COMBAT_ACTIVE (or OUT_OF_RANGE). `_actionTimer` continues from current value. If `_actionTimer >= CycleDuration` at the moment prohibition clears, auto fires on the next tick.

**EC-8: Prohibition lifted on the same frame the auto would fire**
State changes are applied at the start of the server tick before the fire check runs. If prohibition is cleared before the F-3 check evaluates, the auto fires. This ordering must be enforced explicitly by the server tick loop.

**EC-9: Target invalidated while `_actionTimer` is counting up**
System transitions to IDLE. `_actionTimer` freezes. Any damage already resolved stands. This is Rule 23.

**EC-10: Target invalidated at the exact tick `_actionTimer` reaches `CycleDuration`**
State change and fire check are evaluated in the same tick. State change runs first (per server tick ordering). Target is invalid when fire check runs — Step 2 (range/hit check) detects invalid target, auto does not fire, `_actionTimer` does NOT reset.

---

### Category 3 — Speed / Formula Boundary Cases

**EC-11: `AttackSpeedMultiplier` set to exactly 0.5 during combat**
`CycleDuration` becomes 2.0s on the next Beat. The current cycle completes at its prior CycleDuration. No timer reset from the stat change alone.

**EC-12: `AttackSpeedMultiplier` set below 0.5**
Clamped to 0.5 at query time by the Combat System: `max(x, 0.5)`. Character Stats is not required to enforce this. `CycleDuration = 2.0s`. The unclamped value is never used.

**EC-13: `AttackSpeedMultiplier` is exactly 0.0**
Clamped to 0.5 per EC-12. Division by zero cannot occur. If future design requires a "cannot attack" state, it must use `SetCombatProhibited(true)` — not a zero multiplier.

**EC-14: `AttackSpeedMultiplier` is negative**
Clamped to 0.5 per EC-12. Behavior identical to EC-13.

**EC-15: Buff/debuff changes `AttackSpeedMultiplier` mid-cycle**
Change does not affect the current cycle. New `CycleDuration` applies from the next Beat onward. No timer adjustment on stat change.

---

### Category 4 — Timer Edge Cases

**EC-16: Delta time spike causes `_actionTimer` to overshoot `CycleDuration` by more than one cycle**
Only one auto fires per server tick regardless of overshoot magnitude. After resolution: `_actionTimer = 0.0`. No double-firing. Sustained server drops slow effective attack rate but cannot cause simultaneous multiple autos.

**EC-17: Delta time spike causes `_actionTimer` to land in `[CycleDuration, 2 × CycleDuration)`**
Handled identically to EC-16. One auto fires; timer resets to 0.0.

**EC-18: Floating-point drift in `_actionTimer` over long session**
At each auto fire, `_actionTimer = 0.0` (hard reset, not subtraction). This eliminates all float drift — the timer starts clean after every auto. No correction pass required.

---

### Category 5 — Multiplayer / Networking Edge Cases

**EC-19: Server evaluates auto resolution twice in one server tick due to jitter**
Not possible by architecture. The fire check evaluates `_actionTimer >= CycleDuration` once per tick. After the first fire, `_actionTimer = 0.0` (hard reset). The threshold cannot be met again in the same tick. A spurious second evaluation in the same tick finds `_actionTimer = 0.0 < CycleDuration` and does nothing.

**EC-20: Client `_clientActionTimer` diverges from server by more than one full cycle**
On receipt of the server broadcast: if the delta between client and server timers exceeds 50% of `CycleDuration`, snap directly (no smoothing). Below that threshold, standard smoothing applies. Snap is visual only — no damage or logic effect. The snap threshold is a tuning knob.

**EC-21: `NotifySkillUsed()` arrives at server after an auto-attack already resolved**
Call resets `_actionTimer = 0.0` in the new cycle. The auto for the completed cycle already resolved. No reversal. This is correct behavior — the skill input arrived after the auto fired, and the countdown restarts from zero.

**EC-22: `NotifySkillUsed()` packet is lost in transit**
Auto fires on its normal cadence — the timer was never reset. Client charge bar may diverge from server result if the client predicted a reset. Reconciliation of lost skill packets is owned by the Networking Core GDD. No special handling within this system.

**EC-23: Server clock source resets or jumps (Mirror NetworkTime resync)**
The combat system must clamp incoming `deltaTime` to a maximum of `1 × CycleDuration` before adding to `_actionTimer`. This bounds worst-case behavior regardless of upstream clock jump. The clamp maximum is a tuning knob.

---

### Category 6 — Multi-Skill Edge Cases

**EC-24: Two `NotifySkillUsed()` calls arrive in quick succession**
Each call resets `_actionTimer = 0.0`. The second call resets the timer again — effectively extending the countdown by another full `CycleDuration` from the second cast. Both skills' damage resolves independently through the Skill system. A player who chains two skills back-to-back incurs two consecutive timer resets, delaying the next auto by a full `CycleDuration` after the second skill.

---

### Category 7 — Toggle OFF Mid-Cycle

**EC-27: Toggle OFF while `_actionTimer` is mid-cycle**
System enters IDLE. `_actionTimer` freezes at its current value. Any damage already resolved stands. On next activation, Rule 2 resets `_actionTimer` to 0.0 — the frozen value is discarded.

**EC-28: Toggle OFF after a `NotifySkillUsed()` reset (timer near 0.0)**
System enters IDLE. `_actionTimer` freezes near 0.0. No auto was pending. On next activation, Rule 2 resets to 0.0 and the full cycle begins fresh.

**EC-29: Toggle OFF immediately after an auto fires (timer at 0.0)**
System enters IDLE with `_actionTimer = 0.0`. No partial state carries forward. On next activation, Rule 2 resets to 0.0 — equivalent to a fresh activation.

**EC-30: Toggle OFF and ON in quick succession (no target change)**
Each toggle-ON triggers Rule 2: `_actionTimer = 0.0`. The cycle restarts from zero regardless of how much time elapsed during the OFF period.

## Dependencies

### Upstream (systems this one consumes)

| System | Data Consumed | Interface | Status |
|--------|--------------|-----------|--------|
| **Character Stats** | `BaseDamage` (float), `AttackRange` (float), `AttackSpeedMultiplier` (float) | Queried once per Beat Event. Not cached — picks up mid-combat changes. | No GDD yet — interface provisional |
| **Targeting System** | `isValidTarget` (bool), auto-attack toggle state | Read-only. Combat does not own targeting or toggle logic. | No GDD yet — interface provisional |
| **Networking Core** | `deltaTime` (server frame time), `NetworkTime.time` (clock), `CycleTimerBroadcast` (float from server to clients) | Server-authoritative tick source. Mirror confirmed; NGO avoided. | No GDD yet — interface provisional |
| **Damage Calculation** | `FinalDamage` (float) — returned when called with `(BaseDamage, AttackerID, TargetID, DamageContext.PhysicalAuto)` | One-way call per auto-attack. DamageCalc reads target defense internally via TargetID. | No GDD yet — interface provisional |
| **Hit Detection** | `HitResult` enum — returned when called with `(attackerPos, targetPos, AttackRange, capturedForward)` | Player auto-attacks pass `capturedForward = normalize(targetPos − attackerPos)` — facing check always passes. Y-axis ignored. Server emits `HitCheckResult` (R-U) on every call. | GDD authored 2026-05-25 |

### Downstream (systems that consume this one)

| System | Data Provided | Interface | Notes |
|--------|--------------|-----------|-------|
| **Skill System** | `NotifySkillUsed()` — called by Skill System to reset `_actionTimer = 0.0` | One-way notification. Skill system calls this; Combat resets the auto countdown immediately. | No flag semantics. Reset is the only effect. |
| **Client Charge Bar (UI)** | `_actionTimer` broadcast (server → clients every N ms) | Clients apply with smoothing for charge bar animation only. Never drives damage. | UI Requirements section owns the display spec. |
| **Audio System** | Beat Event notification | Audio cue fires on each Beat. Owned by Visual/Audio Requirements section. | No GDD yet |

### Provisional Assumptions

All upstream dependency interfaces are provisional pending those systems' GDDs. Conflicts must be resolved before implementation begins. Flagged assumptions:

- **A**: `BaseDamage` is a flat float, not a range or dice value.
- **B**: `DamageCalc` reads target defense internally via `TargetID` — Combat does not pass defense values.
- **C**: `Hit Detection` is a discrete subsystem with its own API, not inline distance math in Combat.
- **D**: `Networking Core` insulates `deltaTime` from raw `NetworkTime.time` clock jumps — or Combat clamps `deltaTime` to `1 × CycleDuration` as a fallback (EC-23).

## Tuning Knobs

| Knob | Variable | Default | Safe Range | Effect |
|------|----------|---------|------------|--------|
| Base attack cadence | `CycleDuration` (via `AttackSpeedMultiplier = 1.0`) | 1.0s | 0.5s–2.0s | Core rhythm feel. Slower = more forgiving timing windows; faster = tighter skill gaps. Do not change without full playtest — every other system assumes 1.0s as base. |
| Attack speed floor | Minimum `AttackSpeedMultiplier` | 0.5 | 0.25–0.75 | Slowest possible cadence from debuffs/gear. Floor prevents degenerate 3+ second cycles. |
| Attack speed ceiling | Maximum `AttackSpeedMultiplier` | 2.0 | 1.5–3.0 | Fastest possible cadence. At 2.0, `CycleDuration = 0.5s` — skill timing windows tighten to 500ms. Raise ceiling only after latency profiling. |
| Server broadcast interval | `N` ms (CycleTimerBroadcast cadence) | TBD (provisional: 100ms) | 50ms–250ms | How often the server pushes `_actionTimer` to clients. Lower = smoother charge bar; higher = less bandwidth. Below 50ms gives no meaningful improvement. Above 200ms causes visible bar stutter. |
| Client timer snap threshold | 50% of `CycleDuration` | 0.5s (at base speed) | 25%–75% of `CycleDuration` | Delta above which client charge bar snaps to server value instead of smoothing. Set too low → frequent visual snaps. Set too high → bar visually lags far behind actual server state. |
| `deltaTime` clamp | Max `deltaTime` accepted before adding to `_actionTimer` | 1.0 × `CycleDuration` | 0.5×–2.0× `CycleDuration` | Caps damage from frame hitches. At 1.0× ceiling, worst case is one extra missed auto per hitch. Raise only if testing shows legitimate long frames (e.g., zone load spikes). |
| Melee attack range | `AttackRange` (base) | 3.0 units | 2.0–5.0 units | Ground-plane radius for hit detection. Below 2.0 feels like you must stand inside the enemy. Above 5.0 allows hitting through obstacles and breaks the visual read of "I'm too far away." |

## Visual/Audio Requirements

### Event Summary

| Event | Trigger | Visual | Audio | Duration |
|---|---|---|---|---|
| Auto lands | Beat: Step 4 resolves | 2-frame weapon flash `#F0EFE8`; 6–8 particle burst at contact; damage number white | Thunk transient (3-pool random pitch) | Flash 33ms; burst 250ms; number 400–500ms |
| Skill cast | `NotifySkillUsed()` fires | Charge bar snaps to 0% fill instantly (timer reset); no color change | Owned by Skill System | Instant |
| Charge bar — COMBAT_ACTIVE | Timer advancing | Linear Gold `#D4AF37` fill; final 150ms lerp to Ascension White `#F0EFE8` | None | Continuous |
| Charge bar — IDLE | No target / toggle off | Bar at 30% opacity, empty, no animation | None | Persistent |
| Charge bar — OUT_OF_RANGE | Outside AttackRange | Desaturated fill `#7A7060`; border pulses Danger Alert red 25% opacity 0.8s loop | Dry pop `sfx_auto_oor_alert_01.ogg` on state entry | Persistent while OOR |
| Charge bar — PROHIBITED | `SetCombatProhibited(true)` | Fill frozen + desaturated to Ash Mid `#4A4E57`; 8×8px Threat Red `#C0392B` diamond at left endcap | Low wump `sfx_auto_prohibited_01.ogg` on entry | Persistent while prohibited |
| Auto-face snap | First Beat post-activation | 50ms Y-rotation ease on client (no VFX, no audio) | None | 50ms |
| Combat activation | IDLE → COMBAT_ACTIVE | Bar opacity 30% → 100% over 120ms ease-out | None (Targeting System owns activation audio) | 120ms |
| Out of range indicator | OUT_OF_RANGE state | Partial 90° arc at player feet, Ash Mid `#4A4E57` at 60% opacity, radius = AttackRange, facing target | See OOR alert above | Persistent while OOR |

---

### Detailed Specs

#### A. Auto Lands — VFX and Audio

**Weapon flash (attacker side):** 2-frame additive emission tint `#F0EFE8` on weapon mesh via `MaterialPropertyBlock`. Hard cut at frame 2 — no fade. No bloom at +0 to +3 enhancement; existing enhancement bloom amplifies this at +8/+9 at no extra cost.

**Hit burst (contact point):** Spawn `vfx_hit_melee_burst` at target's world-space origin. 6–8 particles, unlit additive, `#F0EFE8` at spawn fading to `#4A4E57` Ash Mid. Radial outward, 60° cone facing camera. Scale: 0.15 world units at spawn, shrinks to 0.0 over 0.25s (shrink-out, not fade — reads cleaner at mobile scale). No Threat Red or Enhancement Gold in this burst.

**Damage number:** World-space origin at contact point, floats upward 18px (screen space) over 200ms, holds 100ms, fades 100ms.
- Normal hit: `#E8E6DF` Primary Text color, 18sp bold.
- Critical hit: `#F0EFE8` Ascension White, 26sp, spring-bounce scale 1.0 → 1.3 → 1.0 in first 150ms, then hold, then fade. Total: 500ms.

**Hit audio:** `sfx_auto_hit_base_01–03.ogg` — 3-sample pool, randomly selected per beat. Character: dull metal-on-mass "thunk" with fast high-frequency tail. Mono, 44.1kHz, q7, 0.12–0.18s transient. Normalized -6 dBFS. 3D positional at target position. Pitch variation ±8 semitones across the pool (prevents machine-gun artifact at 1.0s cadence). Critical hit: same pool, +4 semitones and +2 dBFS gain modifier.

---

#### B. Skill Cast — Timer Reset Visual

When `NotifySkillUsed()` fires, the charge bar snaps to 0% fill instantly — the same visual reset as after an auto fires. No color change; the bar immediately resumes advancing from zero. This gives skilled players immediate feedback that casting a skill resets the auto countdown: an early cast extends the wait by up to a full `CycleDuration`.

---

#### C. Charge Bar — COMBAT_ACTIVE

**Shape:** Chamfered rectangle (45° corner cuts per art bible UI grammar). Width: full width of the bottom-left info panel cluster. Height: 8px — thinner than HP (20px) and MP (14px), subordinate status information. Fills left-to-right.

**Fill:** Linear — no easing. Linear fill is required for rhythm training. Ease curves would make the beat-approach rate non-intuitive.

**Color:** Gold `#D4AF37`. Final 150ms of cycle: lerp to Ascension White `#F0EFE8` — the tension signal that the beat is imminent.

**Background:** Panel Dark `#1A1C1F`. 1px chamfer border in Panel Border `#3A3D44`.

**Beat reset:** Instant snap to 0.0. No animation. The instant reset is the visual beat — the analogue of a metronome click.

---

#### E. PROHIBITED State

Fill freezes and desaturates to Ash Mid `#4A4E57`. Small Threat Red `#C0392B` diamond icon (8×8px) appears at the bar's left endcap — consistent with the art bible's shape grammar (diamond = active status effect / time-critical alert). Fill level remains visible at whatever it was when prohibition began (informational).

On exit: diamond removed immediately; fill color lerps back to Gold over 120ms; fill advance resumes from frozen level.

---

#### F. Out of Range — Arc Indicator

Partial 90° arc mesh at player's feet, world-space, radius = `AttackRange` base 3.0 units. Arc faces toward the selected target. Ash Mid `#4A4E57` at 60% opacity, unlit, single-pixel line texture. Visible only in OUT_OF_RANGE state — disappears immediately (no fade) on re-entry. Never shown in COMBAT_ACTIVE.

---

### Art Bible Conflict Flags

**Flag 1 — Ascension White on charge bar approach:** Expands `#F0EFE8` beyond its primary enhancement bloom role. Semantic domains are separate (timing readiness vs. gear pinnacle), so conflict is unlikely. If the charge bar white visually competes with a +9 weapon bloom in testing, replace with `#E8C060` bright warm gold. Confirm in visual integration testing.

**Flag 2 — Auto-face 180° snap:** 50ms client-side ease is the maximum intervention specified here. If playtesting confirms the snap is disorienting, extend ease window to 100ms — do not add a visual signal to mask it.

**Flag 3 — OUT_OF_RANGE border pulse color:** Uses Danger Alert Red `#E84040`, which the art bible reserves for low-HP warnings. If player testing shows confusion, replace with Ash Mid `#4A4E57` blink at 60% opacity.

---

### Required Assets

| Asset | File | Notes |
|---|---|---|
| Hit burst VFX | `vfx_hit_melee_burst.png` | 6–8 particles, 256×256 atlas slot, unlit additive |
| Range arc indicator | `vfx_auto_rangeind_arc_small.png` | Single-pixel arc line, atlas slot |
| Auto hit SFX | `sfx_auto_hit_base_01–03.ogg` | 3-pool, mono, q7, 0.12–0.18s |
| Out-of-range alert SFX | `sfx_auto_oor_alert_01.ogg` | Dry pop, 0.06s, mono, q7 |
| Prohibited entry SFX | `sfx_auto_prohibited_01.ogg` | Low wump, 0.1s, mono, q7 |

## UI Requirements

### Charge Bar Widget

**Component:** `VisualElement` (UI Toolkit — per ADR-005). The fill element uses `position: absolute` and `transform-origin: left center` in USS. Fill is driven by X-axis scale: `fillElement.style.scale = new StyleScale(new Scale(new Vector3(_clientActionTimer / CycleDuration, 1f, 1f)))`. Set `usageHints = UsageHints.DynamicTransform` on initialization. UGUI `Image.Type.Filled` / `FillMethod.Horizontal` / `fillAmount` are superseded by ADR-005.

**Placement:** Bottom-left HUD cluster, below the MP bar, above the joystick dead zone. Horizontally anchored to the full width of the player info panel. Vertically spaced 4px below the MP bar.

**Dimensions:** Full panel width × 8px height.

**Color states** (driven by state machine, not timer value alone):

| State | Fill Color | Background | Border |
|---|---|---|---|
| IDLE | — (empty) | `#1A1C1F` at 30% opacity | `#3A3D44` at 30% opacity |
| COMBAT_ACTIVE | `#D4AF37` Gold → lerps to `#F0EFE8` in final 150ms | `#1A1C1F` | `#3A3D44` |
| OUT_OF_RANGE | `#7A7060` desaturated warm grey (fill continues) | `#1A1C1F` | `#E84040` pulse 25% opacity 0.8s |
| PROHIBITED | `#4A4E57` Ash Mid (fill frozen) | `#1A1C1F` | `#3A3D44` + Threat Red diamond icon |

**Diamond icon (PROHIBITED):** 8×8px `VisualElement` with `#C0392B` background, diamond clip-path (45° rotation), positioned at left endcap of the bar. Hidden in all other states.

**Interaction:** Charge bar is display-only. No touch input is registered on it.

---

### Auto-Attack Toggle Button

**Placement:** Skill bar area (bottom-right HUD cluster), leftmost position or dedicated slot — per art bible HUD layout (Section 7.1). Confirmed by the art bible: "auto-attack toggle" is an explicit HUD element.

**Visual states:**

| State | Appearance |
|---|---|
| OFF | Dark panel, inactive icon, no border glow |
| ON — IDLE or OUT_OF_RANGE | Active icon, Gold `#D4AF37` border (1px glow), no pulse |
| ON — COMBAT_ACTIVE | Active icon, Gold border, subtle pulse synchronized to charge bar beat (80ms scale punch from 1.0→1.05→1.0 at each Beat Event reset) |
| ON — PROHIBITED | Active icon, Ash Mid `#4A4E57` border — button is active but locked state visible |

**Touch target:** Minimum 44×44px per iOS Human Interface Guidelines. The visual icon may be smaller (32×32px) but the touch hitbox must be 44×44px.

**Feedback on press:** 80ms scale punch (1.0→1.15→1.0) and a short tap haptic (UIImpactFeedbackGenerator.light on iOS). Owned by the Targeting System for toggle-on; this document specifies the visual behavior only.

---

### Damage Numbers

Damage numbers are rendered as world-space UI elements (not screen-space Canvas), positioned at the target's world-space origin and floating upward in screen space. Implemented via a pooled `WorldSpaceDamageNumber` component. No interaction. Max 8 simultaneous damage numbers visible per player to prevent screen clutter.

**Overflow behavior:** When the pool maximum is reached, the oldest number is recycled immediately (no queue). This is preferable to queuing at mobile frame budgets.

---

### Out-of-Range Arc Indicator

**Component:** A 3D world-space mesh arc, not a UI Canvas element. Rendered as an unlit transparent mesh at the player's feet. The arc is a pre-baked quarter-circle mesh with a 1px line texture, scaled to `AttackRange`.

**Faces:** The arc is oriented to always face up (Y+), flat on the ground plane, facing the selected target direction. Updated once per server frame (not per render frame — the target position update rate determines the arc orientation update rate).

**Visibility rule:** Visible only in OUT_OF_RANGE state. `SetActive(false)` in all other states.

---

### Accessibility

**Charge bar:** Must remain readable if Gold is invisible to the player (deuteranopia / protanopia). The bar's fill level is readable from shape alone (empty vs. full) — the color is secondary. No additional accommodation is required beyond what shape and position provide.

**Toggle button:** Must have a screen-reader label: "Auto-Attack: On" / "Auto-Attack: Off" accessible label. Toggle state must not rely solely on color (the icon must change between on/off, not just the border color).

**Damage numbers:** Font size must not fall below 14sp on any supported screen size. 18sp (normal) and 26sp (crit) are both above the floor.

## Acceptance Criteria

All logic criteria are BLOCKING gates — no story is Done without a passing automated test in `tests/unit/combat/` or `tests/integration/combat/`. Visual/feel criteria require screenshot/frame-capture evidence plus lead sign-off.

| AC | Story Type | Gate |
|---|---|---|
| AC-01 through AC-05, AC-08 through AC-15, AC-20 | Logic | BLOCKING |
| AC-16 | Integration | BLOCKING |
| AC-17, AC-18, AC-19 | Visual/Feel | ADVISORY |

---

**AC-01 — Core Cadence: Base 1.0s Cycle**
Given AttackSpeedMultiplier = 1.0 and COMBAT_ACTIVE, when the timer runs for 10 cycles, then each Beat Event fires at 1.000s ±16ms (one frame budget at 60fps).
Pass: 10 events recorded; all deltas within [0.984s, 1.016s]. Fail: Any delta outside range or wrong event count.

**AC-02 — Cadence Scales with AttackSpeedMultiplier**
Given AttackSpeedMultiplier = 2.0, when 5 cycles run, then CycleDuration = 0.5s per F-1 and beats fire at 0.500s ±16ms.
Pass: 5 events in ~2.5s; all deltas within [0.484s, 0.516s]. Fail: Events at 1.0s (multiplier ignored) or outside tolerance.

**AC-03 — AttackSpeedMultiplier Clamped at 0.5**
Given AttackSpeedMultiplier = 0.0 (or any value < 0.5), when CycleDuration is evaluated, then CycleDuration = 2.000s (F-1 clamp applied) and no exception is thrown.
Pass: Beats fire at 2.0s ±16ms; no error logged. Fail: Divide-by-zero, cycle faster than 2.0s, or crash.

**AC-04 — Timer Reset on Skill Cast**
Given COMBAT_ACTIVE and `_actionTimer = 0.40s` into a 1.0s cycle, when `NotifySkillUsed()` is called, then `_actionTimer` resets to 0.0 and the next auto fires 1.0s later (not 0.60s later).
Pass: Server timer reads 0.0 immediately after `NotifySkillUsed()`; next auto fires at t = 1.40s ±16ms (not at t = 1.00s). Fail: Timer not reset (auto fires early) or timer value unchanged.

**AC-05 — Auto and Skill Damage Are Independent and Additive**
Given COMBAT_ACTIVE, when an auto fires at t = 1.0s and `NotifySkillUsed()` is called at t = 1.05s (just after the auto), then both auto damage and skill damage are applied at full value as two independent events.
Pass: Server logs two separate damage events — one auto, one skill — each at full value; `_actionTimer` resets to 0.0 at t = 1.05s. Fail: Either event is zeroed, suppressed, or combined.

**AC-08 — Server Authority: Client and Server Damage Values Match**
Given a known AttackSpeedMultiplier and base damage, when an auto-attack beat fires, then the damage number on the client UI matches the damage value recorded by the server for that hit.
Pass: Client shows value X; server log records value X for the same hit ID. Fail: Client-server divergence after reconciliation.

**AC-09 — Range Check: Auto Does Not Fire When Out of AttackRange**
Given AttackRange = 3.0 units and target distance > 3.0 units (F-2), when a Beat Event fires, then no auto-attack damage packet is sent; timer continues running.
Pass: Zero auto damage events on server; next beat fires ~1.0s later. Fail: Damage applied despite OOR, or timer pauses.

**AC-10 — Range Check: Y-Axis Ignored**
Given the player and target differ by 5.0 units on the Y-axis but are within 3.0 units on the XZ plane, when the beat fires, then auto-attack damage is applied (Y delta excluded from F-2).
Pass: Damage event fires; server confirms hit. Fail: Attack blocked due to 3D distance exceeding AttackRange.

**AC-11 — PROHIBITED State: Timer Runs, No Damage**
Given PROHIBITED is active, when two consecutive beats fire, then no auto-attack damage is applied in either cycle; timer continues at normal cadence; first beat after flag clears fires within ±16ms of expected time.
Pass: Zero damage during PROHIBITED; beat timing holds after exit. Fail: Damage applied during PROHIBITED, or timer resets to 0 on exit.

**AC-12 — COMBAT_ACTIVE Requires Target AND Toggle**
Given toggle ON but no target, or target selected but toggle OFF, when a beat would normally fire, then no beat fires and no damage is applied for either condition independently.
Pass: Both conditions independently prevent COMBAT_ACTIVE and damage. Fail: Either condition alone triggers auto-attack.

**AC-13 — IDLE State: Timer Frozen**
Given IDLE state, when 5.0 seconds elapse, then `_actionTimer` does not advance; zero auto-attack events fire.
Pass: Timer reads same value at start and end; no autos logged. Fail: Timer increments or autos fire during IDLE.

**AC-14 — Toggle OFF Freezes Timer (Does Not Zero It)**
Given `_actionTimer = 0.60s` when toggle is turned OFF, when toggle is turned ON 3.0s later, then `_actionTimer` resumes from 0.60s and the next auto fires approximately 0.40s after re-toggle (±16ms).
Pass: Auto fires within [0.384s, 0.416s] of toggle-ON. Fail: Timer resets to 0.0 (next auto fires ~1.0s after re-toggle).

**AC-15 — Timer Resets on Target Selection**
Given `_actionTimer` at an arbitrary value, when a new target is selected, then `_actionTimer` resets to 0.0 and the first auto fires exactly 1.0s later (±16ms).
Pass: Auto fires 1.0s ±16ms after selection regardless of prior timer value. Fail: Partial cycle carries over (auto fires too soon), or auto delayed beyond 1.016s.

**AC-16 — One Auto Maximum Per Server Tick (Integration)**
Given a server tick processes multiple elapsed beat signals, when the beat resolution runs, then the server applies at most one auto-attack damage event per tick.
Pass: Server combat log shows exactly one auto damage entry per tick. Fail: Multiple damage events in a single tick.

**AC-17 — Charge Bar Resets Instantly at Each Beat (Visual/Feel — Advisory)**
Given the charge bar is visible and COMBAT_ACTIVE, when a Beat Event fires, then the charge bar resets to 0% fill on the same frame.
Pass: Frame capture at beat time shows bar at 0%; fill begins the following frame. Fail: Bar lingers at > 0% for more than one frame after beat.

**AC-18 — Auto-Face: Fires on First Beat Only (Visual/Feel — Advisory)**
Given the player faces away from the target when combat begins, when the first beat fires, then the character snaps to face the target. When the second beat fires with the player facing a different direction, then no forced rotation occurs.
Pass: Snap confirmed on beat 1 via frame capture; facing unchanged on beat 2+. Fail: No snap on beat 1, or rotation forced on beat 2.

**AC-19 — Auto-Face: Movement Direction Redirects on First Beat (Visual/Feel — Advisory)**
Given the player moves in a direction different from the target bearing when the first beat fires, when beat 1 fires, then velocity redirects to align with auto-face. On beat 2+, movement direction is not overridden.
Pass: Velocity reads target-aligned on frame of beat 1; player-controlled on beat 2+. Fail: Movement continues original direction after beat 1, or is redirected on beat 2.

**AC-20 — Out-of-Range Miss Does Not Reset Timer**
Given OUT_OF_RANGE when `_actionTimer >= CycleDuration`, when the auto-fire check runs and misses (Step 2 — range), then `_actionTimer` is NOT reset; the auto fires immediately when the attacker re-enters range on the next tick.
Pass: Timer not reset after OOR miss; auto fires on first in-range tick without waiting a full `CycleDuration`. Fail: Timer resets after OOR miss (player penalized for moving out of range).

## Open Questions

| # | Question | Owner | Priority |
|---|----------|-------|----------|
| OQ-1 | **Does `Networking Core` insulate `deltaTime` from `NetworkTime.time` clock jumps, or must Combat clamp it?** If Networking Core does not guarantee a stable `deltaTime`, the Combat system must clamp incoming `deltaTime` to `1 × CycleDuration` (EC-23). Resolution required before Networking Core GDD is written. | Networking Core GDD | HIGH |
| OQ-2 | **What format does `BaseDamage` take?** Current assumption (Provisional A): flat float. If Character Stats returns a damage range (e.g., min/max) or a dice-style value, F-2 in Damage Calculation changes and this system's call signature changes. | Character Stats GDD | HIGH |
| OQ-3 | ~~**Is `Hit Detection` a discrete system with its own API, or inline distance math in Combat?**~~ **CLOSED 2026-05-25**: Hit Detection is a discrete subsystem with its own GDD (`design/gdd/hit-detection.md`). API: `HitResult CheckHit(attackerPos, targetPos, AttackRange, capturedForward)`. Step 3 above updated accordingly. | hit-detection.md | CLOSED |
| OQ-4 | ~~**Does the cancel-source tag get broadcast?**~~ **CLOSED 2026-05-27**: Auto-cancellation concept removed in reactive cooldown model revision. No cancel-source tagging required. | — | CLOSED |
| OQ-5 | ~~**AoE / multi-target model for `_skillUsedThisCycle`?**~~ **CLOSED 2026-05-27**: `_skillUsedThisCycle` flag removed in reactive cooldown model revision. `NotifySkillUsed()` resets `_actionTimer` — no per-target flag needed. AoE skills call `NotifySkillUsed()` once on the caster regardless of target count. | — | CLOSED |
| OQ-6 | **Does the OUT_OF_RANGE border pulse color (`#E84040` Danger Alert Red) cause player confusion with low-HP warnings?** Art Bible Flag 3 in Visual/Audio Requirements. Resolve during visual integration testing. Fallback: Ash Mid `#4A4E57` blink. | QA / Playtesting | LOW |
| OQ-7 | **Auto-face 180° snap feel at extreme angles.** Rule 22 flags this for visual review. The 50ms client-side ease may not be sufficient to mask a full-rotation snap. Resolve during animation integration testing. | Animation System / QA | LOW |
| OQ-8 | **Server broadcast interval for `CycleTimerBroadcast`.** Currently provisional at 100ms. Final value requires bandwidth profiling against target player density and mobile connection quality targets. | Networking Core GDD | MEDIUM |
