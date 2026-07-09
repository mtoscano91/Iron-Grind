# Hit Detection

> **Status**: In Review
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-25
> **Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

## Overview

Hit Detection is the discrete, server-authoritative subsystem that validates whether an attack physically connects with its target before damage is calculated. It exposes a single `CheckHit` function that accepts attacker position, target position, attack range, and attacker forward direction, and returns a boolean result. An attack connects only if two conditions are both true: the target is within horizontal range of the attacker (Y-axis ignored — this is a ground-plane game) **and** the attacker is facing the target within an angular tolerance. Both conditions are evaluated by the same function, but their effective behavior is intentionally asymmetric: player auto-attacks always pass the facing check (capturedForward is derived toward the target at swing time), while monster attacks are subject to the facing check based on direction locked at wind-up start — the asymmetry that enables the Rhythm Mastery sidestep mechanic (see CR-HD-5). Hit Detection is called server-side only (assembly: `ServerLogic.asmdef`) as step 3 of the Auto-Attack Combat loop, immediately before `DamageCalculation.Compute`. A failed hit check produces no damage and no hit confirmation; the attacker's swing animation completes but has no mechanical effect. At MVP, the system handles single-target melee attacks only; area-of-effect and projectile paths are deferred.

## Player Fantasy

Combat in Iron Grind has a pulse: close the distance, square up to your target, strike on the beat. When you're in rhythm, your swings land like clockwork and the fight flows. Break the rhythm — turn too early, drift past your target, lunge before you're set — and the swing sails wide: the animation plays, but nothing connects, and you've lost a beat you can't get back. Mastery isn't twitch reflex; it's reading the pulse of the fight and never letting your footing fall out of time with your blade. And the rule cuts both ways: catch a monster turned away and your blade bites while its claws find only air.

## Detailed Design

### Core Rules

**CR-HD-1** — API

```
enum HitResult : byte { Hit = 0, MissOutOfRange = 1, MissFacing = 2 }

HitResult CheckHit(
    Vector3 attackerPos,
    Vector3 targetPos,
    float   attackRange,
    Vector3 capturedForward   // unit vector; caller provides, see CR-HD-5
);
```

Implemented in `ServerLogic.asmdef` only. No client equivalent. `ServerLogic.asmdef` must declare `defineConstraints: ["UNITY_SERVER"]` — this prevents `CheckHit` from being referenced by client assemblies and enforces server-authoritative architecture at the assembly boundary. The `: byte` declaration is required for IL2CPP wire serialization correctness; without it, the enum defaults to `int` (4 bytes) and corrupts message framing.

**CR-HD-2** — Range check (evaluated first). If `(T.x − A.x)² + (T.z − A.z)² > attackRange²`, return `MissOutOfRange`. Y-axis is ignored — this is a ground-plane game. Squared comparison avoids a `sqrt` call.

**CR-HD-3** — Point-blank epsilon guard (evaluated after range check passes). If `(T.x − A.x)² + (T.z − A.z)² < POINT_BLANK_EPSILON_SQ`, bypass the facing check and return `Hit` immediately. Guards against `normalize(Vector3.zero)` returning `Vector3.zero` when attacker and target are co-located, which would cause a spurious facing check failure at tolerance angles below 90°.

**CR-HD-4** — Facing check. `dot(capturedForward, normalize(targetPos − attackerPos)) >= cos(ATTACK_FACING_TOLERANCE_DEG × π / 180)`. If this condition is false, return `MissFacing`. If true, return `Hit`.

**CR-HD-5** — Caller responsibilities for `capturedForward`:

| Attacker | `capturedForward` value | When derived |
|----------|------------------------|--------------|
| Player (auto-attack) | `normalize(new Vector3(dx, 0f, dz))` where `dx = targetPos.x − attackerPos.x`, `dz = targetPos.z − attackerPos.z` | At swing execution (re-faces toward current target each swing). This always satisfies CR-HD-4 — the facing check is a no-op for player auto-attacks. |
| Monster | Unit vector derived from `EntityState.FacingAngle` **at wind-up start (T=0 of the wind-up animation)** | Locked by Enemy AI at wind-up start; held as a local variable through the full 600ms wind-up; provided to `CheckHit` at execution unchanged |

**Design rationale — intentional asymmetry**: This asymmetry is by design, not oversight.
- *Player attacks always pass the facing check* because tap-to-target UX guarantees the player is aimed at the enemy before swinging. The XZ direction to target is the only physically meaningful "facing" for a touch-input player.
- *Monster attacks lock `capturedForward` at wind-up start* because the wind-up animation is the telegraphed commitment window. A player who reads the wind-up and moves laterally or backs out of range during the 600ms window can avoid the attack. This is the Rhythm Mastery sidestep mechanic.

**CR-HD-6** — `EntityState.FacingAngle`: EntityState gains a `FacingAngle` field (server storage: `float`, degrees 0–359.9; wire encoding: `short`, value × 10, range 0–3599, yielding 0.1° precision). This is the canonical facing direction for all entities. It is managed separately from `capturedForward` — see CR-HD-7.

**CR-HD-7** — Facing update events:

| Event | `EntityState.FacingAngle` updated? | `capturedForward` |
|-------|-----------------------------------|-------------------|
| Player toggles auto-attack ON | Yes — set toward target | — |
| Player casts a skill | Yes — set toward target | — |
| Player auto-attack swing executes | No — `FacingAngle` unchanged | Derived on-the-fly as `normalize(targetPos − attackerPos)` by the Auto-Attack Combat loop |
| Monster attack initiates | Yes — set toward target player | Enemy AI derives `capturedForward` from this value immediately and holds it as a local variable |
| Monster attack executes | No — `FacingAngle` unchanged during swing | Enemy AI provides the `capturedForward` frozen at initiation |

**CR-HD-8** — `capturedForward` is a local variable held by the calling system (Enemy AI) between attack initiation and execution. It is not read from `EntityState` at execution time. The live `EntityState.FacingAngle` is a replication/display field; `CheckHit` never reads it directly.

**CR-HD-9** — `FacingAngle` wire placement: `FacingAngle` is NOT included in the per-tick `EntityPositionUpdate` (U-U batch). It is replicated via the existing `AutoFaceEvent` (R-U batch), which fires only when `FacingAngle` changes. Entities whose facing is not changing contribute zero bytes to the per-tick stream.

**CR-HD-10** — `HitCheckResult` message: After every `CheckHit` call, the server emits a `HitCheckResult` to the **player's client** (R-U batch, low priority). The player is always the recipient — for monster attacks the player is the defending target; for player attacks the player is the attacker. Monsters have no client and never receive this message. Payload: `{AttackerEntityID: uint (4B), TargetEntityID: uint (4B), Result: byte (1B) [0=Hit, 1=MissOutOfRange, 2=MissFacing], ServerTick: uint (4B)}` — 13 bytes payload, ~18 bytes with batch overhead. Emitted on every call regardless of `HitResult`. This message is the only signal the client uses to determine hit/miss state; the client does not infer miss from absence of `DamageEvent`. EntityID fields use `uint` (4 bytes each) per CR-NET-7.3.

**CR-HD-11** — On `MissOutOfRange` or `MissFacing`: no damage is applied, no `DamageEvent` is emitted. The swing animation on the client completes normally. The auto-attack loop continues its tick cycle. Miss visual/audio feedback is driven by `HitCheckResult`.

---

### States and Transitions

Hit Detection is stateless — `CheckHit` is a pure function with no internal state. The only state it reads is `capturedForward`, which is owned and managed by the calling system. `EntityState.FacingAngle` is updated by Enemy AI (for monsters) and the Auto-Attack Combat system (for players) per CR-HD-7 — these systems own their respective facing states; Hit Detection only consumes a pre-computed vector.

---

### Interactions with Other Systems

| System | Direction | Contract |
|--------|-----------|----------|
| Auto-Attack Combat | Caller | Provides `attackerPos`, `targetPos`, `attackRange`, `capturedForward = normalize(targetPos − attackerPos)`. Receives `HitResult`. On `Hit`: calls `DamageCalculation.Compute`. On miss: auto-attack loop continues; `HitCheckResult` message is the only client signal. |
| Enemy AI | Caller | Same API. Provides `capturedForward` captured at monster attack initiation, not at execution time. |
| Damage Calculation | Downstream | Called by the Auto-Attack Combat / Enemy AI caller only when `HitResult == Hit`. Hit Detection has no direct dependency on Damage Calculation. |
| Networking Wire Protocol | Downstream | Emits `HitCheckResult` (R-U batch, low priority) after every call. `FacingAngle` changes flow via existing `AutoFaceEvent` (R-U batch), not per-tick position stream. |
| EntityState | Read (via caller) | Enemy AI reads `EntityState.FacingAngle` at monster attack initiation only. Hit Detection itself does not read EntityState directly. |

## Formulas

**F-HD-1 — Range Check (Squared Distance)**

Checks whether the target is within horizontal striking distance of the attacker.

```
d_sq     = (T.x − A.x)² + (T.z − A.z)²
isInRange = d_sq ≤ AttackRange²
```

| Variable | Type | Description |
|----------|------|-------------|
| `T.x`, `T.z` | float | Target xz position (world units) |
| `A.x`, `A.z` | float | Attacker xz position (world units) |
| `AttackRange` | float | Strike radius in world units; default `3.0` |
| `d_sq` | float | Squared xz distance |
| `isInRange` | bool | True if target is within range |

*Y-axis excluded — height difference does not affect hit eligibility.*

**Example:** Attacker at `(0, 1, 0)`, Target at `(2, 5, 2)`, `AttackRange = 3.0`:
- `d_sq = (2−0)² + (2−0)² = 4 + 4 = 8.0`
- `AttackRange² = 9.0`
- `8.0 ≤ 9.0` → in range

**Boundary values:**

| Scenario | `d_sq` | Result |
|----------|--------|--------|
| Attacker on top of target | `0.0` | In range — epsilon guard (CR-HD-3) fires first → `Hit` |
| Target at exactly `AttackRange` | `= AttackRange²` | In range (inclusive boundary) |
| Target at `AttackRange + 0.01` | `> AttackRange²` | `MissOutOfRange` |
| `AttackRange = 0` | — | Only passes at zero distance; not a practical value — use `ATTACK_RANGE_MIN` |

---

**F-HD-2 — Facing Check (Dot Product)**

Checks whether the attacker's forward direction is within `ATTACK_FACING_TOLERANCE_DEG` of the vector toward the target.

```
dx        = targetPos.x − attackerPos.x
dz        = targetPos.z − attackerPos.z
toTarget  = normalize(Vector3(dx, 0f, dz))        // unit vector toward target (xz plane; Y component excluded)
isFacing  = dot(capturedForward, toTarget) ≥ cos(ATTACK_FACING_TOLERANCE_DEG × π / 180)
```

| Variable | Type | Description |
|----------|------|-------------|
| `capturedForward` | Vector3 (unit) | Attacker's forward direction at attack initiation (caller-provided) |
| `toTarget` | Vector3 (unit) | Unit vector from attacker toward target |
| `ATTACK_FACING_TOLERANCE_DEG` | float | Half-angle of the facing cone; default `60°` |
| `cos(60°)` | float | `≈ 0.5` — precomputed threshold |
| `isFacing` | bool | True if attacker is facing within the tolerance arc |

**Guard clause (CR-HD-3):** If `d_sq < POINT_BLANK_EPSILON_SQ`, skip F-HD-2 entirely and return `Hit`.

**Example:** Attacker at `(0, 0, 0)` facing `(1, 0, 0)`, Target at `(0.8, 0, 0.6)`, `ATTACK_FACING_TOLERANCE_DEG = 60°`:
- `toTarget = normalize((0.8, 0, 0.6)) = (0.8, 0, 0.6)` (already unit length)
- `dot((1,0,0), (0.8,0,0.6)) = 0.8`
- `cos(60°) = 0.5`
- `0.8 ≥ 0.5` → facing check passes → `Hit`

**Boundary values:**

| `ATTACK_FACING_TOLERANCE_DEG` | `cos(θ)` threshold | Meaning |
|-------------------------------|--------------------|---------|
| `0°` | `1.0` | Must be exactly facing target — unusable in production |
| `45°` | `≈ 0.707` | ±45° cone; strict |
| `60°` | `0.5` | ±60° cone; **default MVP** |
| `90°` | `0.0` | Full hemisphere; permissive — use for AoE skills |
| `180°` | `-1.0` | Any direction passes — use named `FACING_CHECK_DISABLED_DEG` constant |

---

**F-HD-3 — FacingAngle Wire Encoding**

```
wire_value     = (short)(facingAngleDeg × 10)      // encode: float → short
facingAngleDeg = wire_value / 10f                   // decode: short → float
```

Precision: 0.1° per unit. Range: `0–3599` (maps to `0.0°–359.9°`). `capturedForward` derived from `facingAngleDeg`:
```
facingRad      = facingAngleDeg × π / 180
capturedForward = Vector3(Mathf.Cos(facingRad), 0, Mathf.Sin(facingRad))
```

**Axis convention**: `0°` maps to `+X` (`Vector3.right`); `90°` maps to `+Z` (`Vector3.forward`). This is standard mathematical convention (counterclockwise from +X in the XZ plane). Unity's `transform.eulerAngles.y` uses clockwise-from-+Z, which is a different convention. Enemy AI must convert when writing `EntityState.FacingAngle`.

---

**Constants introduced by this system:**

| Constant | Value | Type | Notes |
|----------|-------|------|-------|
| `ATTACK_FACING_TOLERANCE_DEG` | `60.0f` | float, degrees | Safe tuning range: `[45°, 90°]` |
| `POINT_BLANK_EPSILON_SQ` | `0.0005f` | float, units² | Must exceed worst-case wire encoding noise (2 axes × (0.01 units)² = 0.0002f). Set to 0.0005f (2.5× margin). Not a tunable gameplay value. |
| `ATTACK_RANGE_MIN` | `0.5f` | float, units | Practical minimum; `AttackRange = 0` reserved/unused |
| `FACING_CHECK_DISABLED_DEG` | `180.0f` | float, degrees | Named sentinel for omnidirectional skills; not a tuning value |

## Edge Cases

**EC-HD-1 — Attacker and target at the same position (co-located)**
`normalize(Vector3.zero)` returns `Vector3.zero` in Unity — no exception is thrown. `dot(anyVector, Vector3.zero) = 0.0f`, which fails the facing check at any `ATTACK_FACING_TOLERANCE_DEG < 90°`. Guard: if `d_sq < POINT_BLANK_EPSILON_SQ`, bypass the facing check and return `Hit` (CR-HD-3). An attacker standing directly on their target always hits if range is satisfied (which it is at zero distance).

**EC-HD-2 — Target moves out of range between attack scheduling and execution**
In the auto-attack loop, an attack is scheduled at the start of a Beat. The target's position on the server may be up to 50ms stale (one tick). If the target has moved beyond `AttackRange` by execution time, `CheckHit` returns `MissOutOfRange`. The auto-attack loop does not re-evaluate or retry; it waits for the next Beat.

**EC-HD-3 — Monster swings while player moves during the wind-up window**
`capturedForward` is locked at wind-up start (T=0). The player has the full **600ms wind-up window** to move before hit resolution. Two avoidance paths exist:

- **Back away (MissOutOfRange)**: Move beyond `AttackRange` in 600ms. At default `AttackRange = 3.0` and movement speed 4–6 units/s, the player travels 2.4–3.6 units in 600ms; starting near the range boundary, backing away is the reliable low-skill avoidance path.

- **Sidestep (MissFacing)**: Move laterally until the angle between `capturedForward` and the current direction from monster to player exceeds `ATTACK_FACING_TOLERANCE_DEG`. Required lateral displacement at distance R from monster: `d > R × tan(ATTACK_FACING_TOLERANCE_DEG)`. At `60°` tolerance and `R = 3m`: `d > 5.2m` — not achievable at movement speed 4–6 units/s (max 3.6m in 600ms). At `45°` tolerance and `R = 3m`: `d > 3.0m` — achievable at ≥5 units/s. **To deliver the sidestep fantasy at the default 3m attack range, set `ATTACK_FACING_TOLERANCE_DEG` to `45°` during initial tuning.** The default `60°` cone supports lateral sidestep only at close quarters (R ≤ 2.1m); designers should lower tolerance to match intended engagement range.

**EC-HD-4 — `AttackRange = 0` configured by mistake**
`AttackRange = 0` means `AttackRange² = 0`. Positions reconstructed from `short (×100)` wire encoding can differ by up to ±0.01 units per axis at co-location, so `d_sq` at co-location may be up to `0.0002` — exceeding `0.0` and returning `MissOutOfRange`. The caller must validate `AttackRange >= ATTACK_RANGE_MIN` before invoking `CheckHit`. Hit Detection does not validate `AttackRange` internally.

**EC-HD-5 — Target dies between attack scheduling and `CheckHit` execution**
Hit Detection operates on positions only — it does not check entity liveness. If the target was killed by another attacker before `CheckHit` runs, the calling system must check target liveness before invoking `CheckHit`. A liveness check is step 2 of the Auto-Attack Combat Beat resolution sequence; this is the caller's responsibility, not Hit Detection's.

**EC-HD-6 — Multiple attackers targeting the same entity simultaneously**
Each `CheckHit` call is independent — there is no shared state between calls. If two players attack the same monster in the same tick, each call is evaluated separately against the monster's `capturedForward`. Both can independently result in `Hit`, `MissOutOfRange`, or `MissFacing`. No coordination or queuing is required.

**EC-HD-7 — `ATTACK_FACING_TOLERANCE_DEG` set to `180°` (check disabled)**
`cos(180°) = −1.0`. Every `dot()` result is `>= −1.0`, so all attacks pass the facing check regardless of direction. This is the intended behavior of the `FACING_CHECK_DISABLED_DEG = 180f` sentinel — Hit Detection becomes range-only. Intended for omnidirectional attacks (future AoE skills). Must not be used as a tuning value for normal auto-attacks.

## Dependencies

### Upstream (systems this GDD depends on)

| System | GDD | Status | What Hit Detection consumes |
|--------|-----|--------|-----------------------------|
| Networking Core | `networking-core.md` | Approved | `EntityState` struct (position fields, wire encoding); R-U/U-U batch message routing; server tick rate (`TICK_RATE_HZ = 20`) |
| Damage Calculation | `damage-calculation.md` | Approved | `ServerLogic.asmdef` assembly pattern — Hit Detection co-located in the same server-only assembly |
| Auto-Attack Combat | `auto-attack-combat.md` | Complete | Beat resolution sequence (step 3 calls `CheckHit`); `AttackRange = 3.0` default; confirmed Hit Detection is discrete (closes OQ-3 in that GDD) |

### Downstream (systems that consume this GDD)

| System | GDD | Status | What they consume |
|--------|-----|--------|-------------------|
| Auto-Attack Combat | `auto-attack-combat.md` | Complete | `HitResult` enum; `CheckHit` API; `HitCheckResult` message. Propagation complete — Step 3 updated 2026-05-24; OQ-3 closed. |
| Enemy AI | `enemy-ai.md` | Not Started | `CheckHit` API; `capturedForward` capture-at-initiation pattern; `FacingAngle` update at monster attack initiation |
| Skill System | `skill-system.md` | Not Started | `CheckHit` API; `FACING_CHECK_DISABLED_DEG` sentinel for omnidirectional skills; per-attack-type tolerance parameter |
| Networking Wire Protocol | `networking-wire-protocol.md` | Approved | `HitCheckResult` message definition (new — not yet added); `FacingAngle: short (×10)` field on `EntityState` (schema update — not yet added); wire size 69B → 71B |

### Required Propagation Before Implementation

1. **`networking-wire-protocol.md`** — Add `HitCheckResult` message definition (payload: AttackerEntityID uint 4B, TargetEntityID uint 4B, Result byte 1B, ServerTick uint 4B = 13B). Add `FacingAngle: short (×10)` to `EntityState` schema. Update EntityState wire size 69B → 71B. Assign MessageTypeID in Networking ADR. Define recipient routing: player's client (defender for monster attacks, attacker for player attacks).

*(Completed: `auto-attack-combat.md` — Step 3 updated, OQ-3 closed, 2026-05-24. `design/registry/entities.yaml` — FacingAngle, all constants, and HitResult registered, 2026-05-25.)*

## Tuning Knobs

| Knob | Default | Safe Range | What it controls |
|------|---------|------------|-----------------|
| `ATTACK_FACING_TOLERANCE_DEG` | `60°` | `[45°, 90°]` | Half-angle of the monster facing arc. Lower = monster misses more on player sidestep; higher = more forgiving. At `45°` combat feels strict; at `90°` the check rarely fires. Pull down if monsters feel too easy to dodge; push to 75° if players report misses when standing still in front of a monster. Do not set below `30°` (too strict for mobile) or above `90°` (hemisphere, effectively omnidirectional). |
| `AttackRange` (per attack / mob type) | `3.0` | `[0.5, 10.0]` | Horizontal strike radius in world units. Controls how close the attacker must be for the range check to pass. Each monster type or attack type may define its own value. Minimum value is `ATTACK_RANGE_MIN = 0.5` — validated by the caller before invoking `CheckHit`. |
| `TICK_RATE_HZ` | `20` | `[10, 30]` | Server tick rate — not owned by Hit Detection, but determines how often `CheckHit` is called and the maximum positional staleness window (1 tick = 50ms at 20Hz). Owned by Networking Core; changes here have cascade effects on all server-side systems. |

**Non-tunable constants (correctness guards — do not adjust without architecture review):**

| Constant | Value | Why fixed |
|----------|-------|-----------|
| `POINT_BLANK_EPSILON_SQ` | `0.0005f` | Guards the `normalize(zero)` path. Value must exceed worst-case wire encoding noise (0.0002f). Change only after recalculating wire rounding floor — this is a correctness guard, not a gameplay parameter. |
| `FACING_CHECK_DISABLED_DEG` | `180f` | Named sentinel, not a gameplay parameter. Semantics are undefined if changed — use this constant by name, not the literal value. |
| `ATTACK_RANGE_MIN` | `0.5f` | Below this, wire encoding rounding (±0.01 units per axis) makes the range check unreliable at near-zero distance. |

## Visual/Audio Requirements

All visual and audio cues for hit/miss feedback are driven by the `HitCheckResult` message (CR-HD-10). Hit Detection has no direct ownership of these cues — it emits the result; the client's audio/visual systems interpret it.

| `HitResult` | Required client feedback | Priority |
|-------------|--------------------------|----------|
| `Hit` | Hit impact VFX on target; damage number float; weapon swing sound (impact variant) | MVP |
| `MissOutOfRange` | Swing animation completes with no impact; optional "whoosh" miss SFX; no VFX on target | MVP |
| `MissFacing` | Same visual/audio as `MissOutOfRange` at MVP. Future: distinct audio cue (e.g., "clank" or "blocked" variant) to teach the player that the monster was not facing them | Post-MVP |

**Monster wind-up legibility (required for the facing mechanic to serve Rhythm Mastery):**
The "facing locks at initiation" mechanic is only playable if the player can perceive and react to the initiation window. Monster attack animations must include a wind-up phase of at least **600ms** before the hit frame fires. This value must be surfaced as a constraint in the Enemy AI GDD and enforced as a minimum in the animation rig specification. Without a legible wind-up, sidestepping a monster to cause a `MissFacing` becomes a random outcome rather than a skill moment.

**Client-side animation decoupling:**
The client's character facing animation (visual rotation of the character mesh toward the target) is a local presentation concern and must not be fed back to the server. The server's canonical `EntityState.FacingAngle` drives `CheckHit`; the client's visual rotation may diverge within a tick. Clients must tolerate this divergence without snapping or jitter.

## UI Requirements

Hit Detection has no dedicated UI. The `HitCheckResult` message (CR-HD-10) drives existing HUD systems:

| Signal | UI element | Owner |
|--------|-----------|-------|
| `HitResult.Hit` | Damage number float on target | HUD / Combat Feedback System |
| `HitResult.MissOutOfRange` | "Miss" text float or no-display (to be decided in HUD GDD) | HUD / Combat Feedback System |
| `HitResult.MissFacing` | Same as `MissOutOfRange` at MVP | HUD / Combat Feedback System |

No new UI screens, panels, or HUD elements are introduced by this system. The `Result` byte in `HitCheckResult` provides the reason code that the HUD system will consume to determine display variant. Display decisions are deferred to the HUD GDD (not yet authored).

## Acceptance Criteria

All ACs are server-side unit tests unless otherwise noted. "Pass" means `CheckHit` returns the specified `HitResult` value.

**Range check (F-HD-1):**

**AC-HD-1** — Given attacker at `(0,0,0)`, target at `(2,0,2)`, `AttackRange = 3.0`: `CheckHit` returns `Hit`. (`d_sq = 8.0 ≤ 9.0`)

**AC-HD-2** — Given attacker at `(0,0,0)`, target at `(3,0,0)`, `AttackRange = 3.0`: `CheckHit` returns `Hit`. (Target at exactly `AttackRange` — inclusive boundary.)

**AC-HD-3** — Given attacker at `(0,0,0)`, target at `(3.01,0,0)`, `AttackRange = 3.0`: `CheckHit` returns `MissOutOfRange`. (Target just outside range.)

**AC-HD-4** — Given attacker at `(0,0,0)`, target at `(2,10,0)` (`d_sq_xz = 4.0 ≤ 9.0`), `AttackRange = 3.0`, `capturedForward = (1,0,0)`: `CheckHit` returns `Hit`. (Target is within XZ range despite large Y-axis difference — Y-axis excluded from range check. If Y were included, `d_sq = 4 + 100 = 104 > 9` would return `MissOutOfRange`; the distinction proves XZ-only evaluation.)

**Facing check (F-HD-2):**

**AC-HD-5** — Given attacker at `(0,0,0)` with `capturedForward = (1,0,0)`, target at `(1,0,0)`, `AttackRange = 3.0`, `ATTACK_FACING_TOLERANCE_DEG = 60`: `CheckHit` returns `Hit`. (Directly facing target.)

**AC-HD-6** — Given attacker at `(0,0,0)` with `capturedForward = (1,0,0)`, target at `(0,0,1)` (90° to the side), `AttackRange = 3.0`, `ATTACK_FACING_TOLERANCE_DEG = 60`: `CheckHit` returns `MissFacing`. (`dot = 0.0 < 0.5`)

**AC-HD-7** — Given attacker at `(0,0,0)` with `capturedForward = (1,0,0)`, target at `(0.866,0,0.5)` (30° off-axis), `AttackRange = 3.0`, `ATTACK_FACING_TOLERANCE_DEG = 60`: `CheckHit` returns `Hit`. (`dot ≈ 0.866 ≥ 0.5`)

**AC-HD-8** — Given attacker at `(0,0,0)` with `capturedForward = (-1,0,0)` (facing directly away from target), target at `(1,0,0)`, `AttackRange = 3.0`: `CheckHit` returns `MissFacing`. (`dot = -1.0 < 0.5`)

**Point-blank epsilon guard (CR-HD-3):**

**AC-HD-9** — Given attacker and target at the same position `(5,0,5)`, any `capturedForward`, `AttackRange = 3.0`: `CheckHit` returns `Hit`. (Epsilon guard fires; `normalize(zero)` is never called.)

**AC-HD-10** — Given attacker at `(0,0,0)`, target at `(0.001,0,0)` (`d_sq = 0.000001 < POINT_BLANK_EPSILON_SQ`), `capturedForward = (-1,0,0)` (facing away): `CheckHit` returns `Hit`. (Epsilon guard bypasses facing check even when `capturedForward` is reversed.)

**HitCheckResult message (CR-HD-10):**

**AC-HD-11a** — Given a player attacker and any `CheckHit` result `R`, a `HitCheckResult` message is enqueued in the R-U batch for the **attacking player's** client. The `Result` byte must equal the exact `HitResult` value returned by `CheckHit` (0=Hit, 1=MissOutOfRange, 2=MissFacing). `AttackerEntityID` and `TargetEntityID` must match the call inputs exactly. `ServerTick` must equal the current server tick at call time. (Integration test.)

**AC-HD-11b** — Given a monster attacker, a `HitCheckResult` message is enqueued in the R-U batch for the **defending player's** client (the target being attacked), not the monster. The `Result` byte, entity IDs, and `ServerTick` requirements are identical to AC-HD-11a. No message is sent to the monster. (Integration test.)

**AC-HD-12** — After a `CheckHit` call returning `MissOutOfRange`, no `DamageEvent` is emitted and `DamageCalculation.Compute` is not invoked. (Integration test with Auto-Attack Combat loop.)

**AC-HD-13** — After a `CheckHit` call returning `MissFacing`, no `DamageEvent` is emitted and `DamageCalculation.Compute` is not invoked.

**Caller contract — player vs monster capturedForward:**

**AC-HD-14** — Given `capturedForward = normalize(new Vector3(dx, 0f, dz))` where `(dx, dz)` is the XZ displacement toward the target, `CheckHit` returns `Hit` (not `MissFacing`) for any attacker and in-range target where `d_sq ≥ POINT_BLANK_EPSILON_SQ`. (Unit test of `CheckHit` directly — verifies that the player auto-attack capturedForward derivation, when applied as input to `CheckHit`, never produces `MissFacing`. Tests CheckHit behaviour, not the caller.)

**AC-HD-15** — When Enemy AI calls `CheckHit` for a monster attack, `capturedForward` is derived from `EntityState.FacingAngle` captured at attack initiation, not at execution time. Test: mutate `EntityState.FacingAngle` between initiation and execution; verify `CheckHit` receives the pre-mutation value.

**Monster wind-up legibility:**

**AC-HD-16** — Monster attack animations include a wind-up phase of at least 600ms before the hit frame fires. Verified by integration test: assert that the frame offset between wind-up start event and hit frame event in the animation timeline is ≥ 600ms for all MVP monster types.

**CheckHit isolation (CR-HD-8):**

**AC-HD-17** — `CheckHit` is callable with only `attackerPos`, `targetPos`, `attackRange`, and `capturedForward` as inputs and produces the correct result without any `EntityState` present in scope. Verified by unit test: `CheckHit` executes in a context with no EntityState instance, no ECS world, and no server services — any internal read of EntityState will fail the test environment and produce a compile or runtime error.

**FacingAngle pipeline (CR-HD-9):**

**AC-HD-18** — When `EntityState.FacingAngle` changes, the update is transmitted via `AutoFaceEvent` (R-U batch) and NOT included in the per-tick `EntityPositionUpdate` (U-U batch). Verified by integration test: mutate `FacingAngle`; assert `AutoFaceEvent` is emitted and the per-tick position stream byte count does not change.

**FacingAngle update triggers (CR-HD-7):**

**AC-HD-19** — `EntityState.FacingAngle` is updated exactly on the events listed in CR-HD-7: (a) player toggles auto-attack ON, (b) player casts a skill, (c) monster attack initiates (wind-up start). Verified by integration tests: for each trigger event, assert `FacingAngle` changes; for all other events (player auto-attack swing executes, monster attack executes), assert `FacingAngle` is unchanged.

**Miss type distinction (Post-MVP):**

**AC-HD-20** — [Polish milestone] `HitResult.MissFacing` produces a distinct client audio/visual cue from `HitResult.MissOutOfRange`. At MVP both produce identical 'Miss!' feedback (see OQ-HD-2). This AC blocks the Polish-stage implementation of distinct miss cues and is not in scope for the MVP test pass.

## Open Questions

**OQ-HD-1** — *Monster 600ms wind-up enforcement* — The facing-at-initiation mechanic requires monster attack animations to have at least a 600ms wind-up (Visual/Audio Requirements). This constraint must be documented in the Enemy AI GDD and communicated to the animation team. Who owns this constraint across those two systems? *(Action required before Enemy AI GDD is authored.)*

**OQ-HD-2** — *`MissFacing` distinct feedback* — At MVP, `MissFacing` uses the same client feedback as `MissOutOfRange`. The `HitCheckResult` reason code allows a distinct `MissFacing` signal (e.g., "blocked" sound) to be added post-MVP without a server change. *(Enhancement — post-MVP, depends on HUD GDD.)*

**OQ-HD-3** — *Per-attack-type tolerance override* — When the Skill System GDD is authored, determine whether `CheckHit` should accept an optional `facingToleranceDeg` parameter (cleaner for AoE/ranged skills that need `FACING_CHECK_DISABLED_DEG`), or whether callers force-pass the facing check by passing `normalize(targetPos − attackerPos)` as `capturedForward`. *(Decision required when Skill System GDD is authored.)*

**OQ-HD-4** — *`AutoFaceEvent` fire rule for mid-combat facing changes* — No rule currently exists for when `AutoFaceEvent` fires for non-first-beat facing changes (e.g., skill cast mid-combat updates `FacingAngle`). This rule belongs in the Networking Wire Protocol GDD. *(Action required before networking-wire-protocol.md review closes.)*

**OQ-HD-5** — *Player auto-attack facing-miss omission* — Confirmed acceptable at design time (2026-07-04): player auto-attacks bypass the facing check entirely (`capturedForward` always points at target) — the player can only miss via `MissOutOfRange`, never `MissFacing`, regardless of actual orientation. Validate during playtesting whether this reads as satisfying, or whether players expect/want a facing-based miss risk on their own attacks too (would require moving away from the CR-HD-5 "always pass" caller contract for player auto-attacks). *(Playtest validation — pre-Polish milestone.)*
