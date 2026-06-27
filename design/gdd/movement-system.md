# Movement System

> **Status**: Designed — Pending Review
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-25
> **Implements Pillar**: Pillar 2 — Rhythm Mastery; Pillar 3 — Social Gravity

## Overview

The Movement System governs player locomotion in shared zones: touch input — a virtual joystick or screen drag — translates to a velocity direction that the server integrates into a canonical world position at each 20 Hz tick. Authoritative positions are wire-encoded as posX/Y/Z (short×100, 0.01-unit precision, ±327.67-unit max range) and broadcast to all zone clients in the per-tick batch. Players may also activate an auto-run mode via a dedicated button that drives the character forward in the current facing direction continuously; auto-run stops when the button is pressed again or the joystick is pulled back. Movement is unrestricted during combat — the auto-attack timer runs through repositioning and kiting without interruption. The server auto-faces the character toward the target on the first auto-attack beat (Auto-Attack Combat Rule 20), after which the player resumes free directional control. Base movement speed is a constant run speed; skills and Status Effect buffs may increase it temporarily. This system supplies the position data that Auto-Attack Combat range checks, Loot pickup proximity, Navigation/Pathfinding, and Client-Side Prediction all consume.

## Player Fantasy

Movement is the other half of the rhythm. Every mob telegraphs its attack — there is a wind-up, a commit, a moment where it is locked in and you are not. A skilled player reads that moment. One step clear before the swing resolves, and the damage misses entirely. One step back into range on the next beat, and you never lose the cadence. The difference between a new player and an experienced one isn't always in the skill bar; sometimes it is in where they were standing when the monster decided to swing. You read the tell, you moved, and the attack landed on empty ground.

The other ninety percent of play is the road. The same zone, the same path to the same pull, the same auto-run toggle so both thumbs are free for when it matters. Auto-run isn't a concession — it is the correct answer to a game that asks you to do this a thousand times. The grind is long. The walk does not have to be.

## Detailed Rules

### Core Rules

**CR-MOV-1 — Screen Input Zones**

The screen is partitioned into two zones using each touch's **start position**, not its current position. A touch claiming a zone retains it for its full lifetime even if the finger crosses the midpoint.

- **Joystick zone:** Left half of the safe-area-inset screen width. The inset equals `Screen.safeArea.x` on notched iPhones (iPhone X+).
- **Camera zone:** Right half of the safe-area-inset screen width. UI skill buttons occupy this half; camera drag is only recognized on non-UI areas of the camera zone. Skill button taps take precedence over camera drag.

---

**CR-MOV-2 — Virtual Joystick**

A floating joystick UI element appears centered at the touch-start position when the joystick zone is touched. The joystick radius is TK-MOV-1.

- **Direction:** `worldMoveDir = RotateAroundY(normalize(camera.forward.xz), joystickAngle)` — movement is always camera-relative.
- **Dead zone:** When drag distance < TK-MOV-2 (minimum dead zone), direction is treated as zero (no movement input).
- **Release:** Movement stops immediately on touch-end. No slide or deceleration.
- **Rear pull:** When `dot(joystickDir, characterForward) < 0` (joystick enters the rear half-plane of the character's world-space forward), if auto-run is active, auto-run cancels. The joystick direction then controls movement normally.

---

**CR-MOV-3 — Camera Rotation**

Horizontal drag in the camera zone rotates the camera around the character's Y-axis. The camera does not directly set character movement direction — facing is derived from camera only when the joystick is at rest:

- **Joystick at rest (magnitude < TK-MOV-2 or no joystick touch):** The character's world-space facing aligns with the camera's forward each tick. Auto-run uses this facing as its movement direction.
- **Joystick held (magnitude ≥ TK-MOV-2):** Joystick direction wins. Camera rotation has no effect on movement direction for that tick.
- **Camera update timing:** Camera follows character in `LateUpdate`, after position integration in `Update`, to prevent camera judder.

---

**CR-MOV-4 — Position Integration Formula**

The server integrates position once per tick:

```
newPos = oldPos + normalize(inputDir) × GetEffectiveStat(MovementSpeed) × (1.0 / TICK_RATE_HZ)
```

| Variable | Description |
|----------|-------------|
| `oldPos` | Committed position from the previous tick |
| `inputDir` | World-space movement direction from the current `MovementIntentMessage`; or the auto-run facing direction if auto-run is active and no joystick message was received this tick |
| `GetEffectiveStat(MovementSpeed)` | Queried each tick (not cached) — buff changes apply immediately |
| `1.0 / TICK_RATE_HZ` | Fixed deltaTime = 0.050s (Networking Core CR-NET-2; never actual elapsed wall-clock time) |

At base MovementSpeed = 6.0 u/s: character moves 0.30 units per tick. At cap = 20.0 u/s: 1.0 unit per tick.

---

**CR-MOV-5 — Auto-Run**

Auto-run is a **client-side state flag** only. The server receives no auto-run-specific message — the wire protocol is identical to manual movement; the client simply continues sending a forward-facing `MovementIntentMessage` each tick while auto-run is active.

**Activation:** Auto-run button tapped while idle or moving.

**Behavior while active:**
- Joystick at rest: character moves in current facing direction (camera-derived per CR-MOV-3).
- Joystick held in forward half-plane (`dot(joystickDir, characterForward) ≥ 0`): joystick direction overrides movement direction for that tick. Auto-run flag remains set.
- Joystick enters rear half-plane (`dot < 0`): auto-run cancels; joystick direction takes over.

**Auto-run cancels when any of the following occur:**
1. Auto-run button pressed again.
2. Joystick direction enters the rear half-plane (`dot(joystickDir, characterForward) < 0`).
3. Entity enters Prohibited state.

**Persistence:** Auto-run state is never persisted. On disconnect, reconnect, or zone transition, auto-run always begins inactive.

---

**CR-MOV-6 — Server Position Authority**

The server is the sole authority for all character positions (Networking Core CR-NET-1). Each tick:

1. Read the latest `MovementIntentMessage` received for this entity (if any).
2. Check Prohibited state. If Prohibited: discard the message, skip integration, hold previous position.
3. Compute candidate position using CR-MOV-4.
4. Validate candidate against zone NavMesh (CR-MOV-7).
5. Commit the validated position as canonical `EntityState.posX/Y/Z`.
6. Broadcast in the per-tick batch to all clients in zone.

At MVP, the client renders the last received server position only. No client-side movement prediction is implemented (deferred to Client-Side Prediction GDD). This produces ~50ms visual lag between touch input and rendered position — an accepted MVP trade-off.

---

**CR-MOV-7 — Zone Boundary and Walkability**

Each zone ships with a pre-baked NavMesh asset (baked in the editor; never at runtime — runtime baking is prohibited on iOS due to performance cost). The NavMesh asset is loaded by the server at zone load time. The client does **not** load the NavMesh asset (OQ-MOV-4 resolved 2026-06-15 — see CR-CSP-2 and CR-CSP-14 in `client-side-prediction.md`). Self-prediction applies F-MOV-1 without NavMesh validation; boundary corrections arrive via server reconciliation (CR-CSP-7 / CR-CSP-10).

After computing a candidate position, the server validates:

```
NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, searchRadius: 0.5f, NavMesh.AllAreas)
```

- Hit found: committed position = `hit.position` (constrains to walkable surface; handles irregular boundaries and interior obstacles).
- No hit: discard the move; committed position = previous tick's position.

No visual or audio feedback is given when the position is clamped at a boundary. The character's movement animation continues playing. This is intentional — zone boundaries are not meant to be discoverable as explicit walls.

---

**CR-MOV-8 — Prohibited State (Stun)**

When the `Prohibited` flag is set by an external system (Auto-Attack Combat or Status Effects):

- Server discards all `MovementIntentMessage` inputs for this entity. Position is not integrated.
- Auto-run cancels.
- **Facing rotation is permitted:** Camera drag and joystick facing updates continue to work, allowing the player to pre-aim their exit vector while position-locked. The client sends a `MovementIntentMessage` with `DirX = 0`, `DirZ = 0`, and the updated `FacingAngle` field (see CR-MOV-10). The server applies the new facing angle and discards only the movement direction. Facing rotation produces no position change.
- The Prohibited state does **not** reset the auto-face first-beat trigger. Auto-Attack Rule 20 fires once per auto-attack activation — Prohibited entry/exit does not re-arm it.

---

**CR-MOV-9 — Auto-Face Interaction (Auto-Attack Rule 20)**

On the first auto-attack beat after activation, Auto-Attack Combat injects a server-side facing override via a direct server-internal call — `IMovementSystem.InjectFacingOverride(entityId, facingDirection)`. No wire message is involved. The Movement System processes the override as follows:

- The server uses the auto-face direction as `inputDir` for position integration on that tick, replacing the client's `MovementIntentMessage`.
- The override lasts **exactly one tick**.
- On the next tick, the client's `MovementIntentMessage` resumes as the directional input.
- If the joystick was held at the moment of override, the following tick's joystick input immediately overrides the auto-face direction.
- Auto-run, if active, does not cancel from auto-face.

---

**CR-MOV-10 — Wire Message**

`MovementIntentMessage` schema:

| Field | Type | Description |
|-------|------|-------------|
| `SenderEntityID` | uint | Sending entity |
| `DirX`, `DirZ` | short × 2 | Normalized XZ direction, encoded per CR-NET-7.2. DirY excluded (always zero — ground-plane movement only) |
| `FacingAngle` | short × 10 | Character facing angle, encoded as `angle_degrees × 10` (range 0–3599 → 0.0°–359.9°). Matches `EntityState.facingAngle` encoding. Always sent; the server updates stored facing on every received message regardless of movement direction. |
| `TickNumber` | uint | Server tick this intent was generated for; stale messages (TickNumber < serverCurrentTick − STALE_TICK_TOLERANCE) are discarded |

Channel: U-U (unreliable, unordered). When no movement input is active, the client may suppress the send; the server treats a missing message as zero input for position integration. When the player is in Prohibited state and adjusts facing, the client sends a message with `DirX = 0`, `DirZ = 0`, and the updated `FacingAngle` — the server updates facing and discards the zero direction.

> **Primitive gap:** `MovementIntentMessage` is not yet registered in the Wire Protocol GDD. The Wire Protocol GDD must be amended to assign a `MessageTypeID` and add this message to its routing table.

---

### States and Transitions

| State | Description | Entry | Exit |
|-------|-------------|-------|------|
| **Idle** | No movement. Position static. | Zone entry; joystick released + auto-run inactive; auto-run cancelled | Joystick touched → Moving; Auto-run button → Moving; Prohibited flag set → Prohibited |
| **Moving** | Server integrates position each tick. Sub-states: **Joystick** (joystick direction wins), **AutoRun** (camera-steerable facing direction), **Combined** (auto-run active + joystick held in forward half-plane — enters when auto-run is active and `dot(joystickDir, characterForward) ≥ 0`; exits to AutoRun on joystick release; rear-pull cancels auto-run → Idle; joystick direction wins) | Joystick touched; auto-run button pressed | Joystick released + auto-run inactive → Idle; Rear-pull with no active auto-run → Idle; Prohibited flag set → Prohibited |
| **Prohibited** | Position locked. Facing rotation permitted. Movement input discarded. Auto-run cancelled. | Prohibited flag set by external system | Prohibited cleared → Idle (auto-run must be re-enabled manually) |

---

### Interactions with Other Systems

| System | Data In | Data Out | Owner |
|--------|---------|---------|-------|
| Networking Core | `TICK_RATE_HZ`, `EntityState` schema (posX/Y/Z, facingAngle), U-U channel | `MovementIntentMessage` (new — pending Wire Protocol amendment) | Networking Core owns wire protocol |
| Auto-Attack Combat | Auto-face direction override on beat 1 (one tick) | Character position (posX, posZ) per committed `EntityState` — used for range check F-2 | Auto-Attack reads committed `EntityState`; Movement receives the one-tick facing override |
| Status Effects / Buffs | `GetEffectiveStat(MovementSpeed)` per tick | — | Character Stats owns `GetEffectiveStat`; Status Effects applies modifiers |
| Loot Table System | `PICKUP_RADIUS_UNITS` = 2.0 world units | Player position per committed `EntityState` | Loot Table System performs proximity check; reads `EntityState` |
| Navigation / Pathfinding *(downstream)* | Pre-baked NavMesh assets per zone (shared) | NavMesh walkability definition for mob movement | Zone Instancing GDD defines zone geometry and NavMesh source |
| Client-Side Prediction *(downstream)* | Committed position + movement direction per tick | — | Client-Side Prediction reads `EntityState` for interpolation |
| Zone Instancing *(downstream)* | Zone NavMesh boundary; zone entry/exit trigger volumes | Player position change may trigger zone transition | Zone Instancing owns boundary and trigger definitions |

## Formulas

**F-MOV-1 — Position Integration**

```
newPos = oldPos + normalize(inputDir) × v_eff × dt
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `oldPos` | Vector3 (XZ, Y=0) | zone NavMesh bounds | Committed server position from the previous tick |
| `inputDir` | Vector3 (XZ, Y=0) | unit vector or zero | Movement direction from `MovementIntentMessage`; or auto-run facing direction; or zero if no input this tick |
| `v_eff` | float | [0.5, 20.0] u/s | Output of F-MOV-2 (effective movement speed) |
| `dt` | float | 0.050 (fixed) | Fixed tick interval = `1.0 / TICK_RATE_HZ` (Networking Core CR-NET-2) |
| `newPos` | Vector3 | zone NavMesh bounds | Candidate position before NavMesh validation (CR-MOV-7) |

**Zero-input guard:** When `inputDir == Vector3.zero`, skip integration entirely (`newPos = oldPos`). Do not normalize a zero vector.

**Output range:** Unbounded before NavMesh validation; clamped to walkable surface by CR-MOV-7.

**Example:** `oldPos=(10.0, 0.0, 5.0)`, `inputDir=(0,0,1)`, `v_eff=6.0`, `dt=0.050` → `newPos=(10.0, 0.0, 5.30)`. Wire-encoded posZ: `RoundToInt(5.30×100)=530` → decoded as 5.30 (exact, no rounding loss).

---

**F-MOV-2 — Effective Movement Speed**

Instantiates the Character Stats F-1 modifier stack for `MovementSpeed`. The Movement System does not recompute the stack — it calls `GetEffectiveStat(EntityID, StatID.MovementSpeed)` and consumes the result. Do not apply a secondary clamp on the return value.

```
v_eff = clamp(
  (v_base + ΣFlatEquip + ΣFlatBuff) × (1 + ΣPctEquip) × (1 + ΣPctBuff),
  SPEED_STAT_MIN,
  SPEED_STAT_MAX
)
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `v_base` | float | 6.0 u/s (at spawn) | Base speed from Character Stats |
| `ΣFlatBuff` | float | pre-clamp | Sum of flat buff/debuff modifier contributions to MovementSpeed |
| `ΣPctBuff` | float | pre-clamp | Sum of percentage buff/debuff contributions |
| `ΣFlatEquip`, `ΣPctEquip` | float | 0 (MVP) | No movement-speed gear defined at MVP |
| `SPEED_STAT_MIN` | float | 0.5 | Hard floor enforced by Character Stats |
| `SPEED_STAT_MAX` | float | 20.0 | Hard ceiling enforced by Character Stats |
| `v_eff` | float | [0.5, 20.0] u/s | Effective speed consumed by F-MOV-1 |

**Output range:** [0.5, 20.0] u/s, enforced by Character Stats clamp.

**Compounding intent:** Flat modifiers (ΣFlatEquip + ΣFlatBuff) are summed first, then the total is multiplied by each percent multiplier independently. Two ×20% percent buffs yield ×1.44 (not ×1.40). This is by design — percent stacking is multiplicative, not additive.

**Examples:**
- Base only: `v_eff = clamp(6.0 × 1.0 × 1.0, 0.5, 20.0) = 6.0 u/s` → 0.30 u/tick
- Speed buff (+3 flat, +20% pct): `v_eff = clamp((6.0+3.0) × 1.20, 0.5, 20.0) = 10.8 u/s` → 0.54 u/tick
- Slow debuff (−50% pct): `v_eff = clamp(6.0 × 0.50, 0.5, 20.0) = 3.0 u/s` → 0.15 u/tick
- At cap: `v_eff = 20.0 u/s` → 1.0 u/tick

---

**F-MOV-3 — Auto-Run Cancel Test**

```
cancelAutoRun = dot(joystickDir_world, characterForward_world) < 0.0
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `joystickDir_world` | Vector3 (XZ, Y=0) | any non-zero | World-space direction derived from joystick offset; sign is magnitude-independent |
| `characterForward_world` | Vector3 (XZ, Y=0) | unit vector | Character's world-space forward at the moment of joystick reading |
| `cancelAutoRun` | bool | {true, false} | true = auto-run flag cleared this frame |

**Output range:** Binary. Strict `< 0.0`: a joystick exactly at 90° (dot = 0.0) does not cancel auto-run; only entering the rear half-plane does.

**Examples:** Forward = (1,0,0), joystick = (−0.7, 0, 0.7) → dot = −0.7 → cancel. Joystick = (0.7, 0, 0.7) → dot = 0.7 → no cancel.

---

**F-MOV-4 — Camera-Relative Direction Derivation**

```
cameraForward_flat = normalize(Vector3(camera.forward.x, 0, camera.forward.z))
worldMoveDir = normalize(Quaternion.AngleAxis(joystickAngle_deg, Vector3.up) × cameraForward_flat)
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `camera.forward` | Vector3 | unit vector | Camera's world-space forward, including pitch |
| `cameraForward_flat` | Vector3 (XZ) | unit vector | Camera forward projected to ground plane, renormalized |
| `joystickAngle_deg` | float | [0.0, 360.0) | Clockwise angle from `cameraForward_flat` to joystick offset, derived from `atan2(offset.x, offset.y)` |
| `worldMoveDir` | Vector3 (XZ) | unit vector | World-space `inputDir` for F-MOV-1 |

**Edge case:** If `cameraForward_flat` magnitude is near-zero (magnitude < 0.001f — camera pitched ≥ 90°, impossible with a fixed third-person camera), fall back to `Vector3.forward`. Assert in dev builds.

---

**F-MOV-5 — Dead Zone Boundary Test**

```
normalizedDrag = length(touchCurrentPos − touchStartPos) / TK_MOV_1_JOYSTICK_RADIUS_PX
isDead = normalizedDrag < TK_MOV_2_DEAD_ZONE_NORMALIZED
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `touchCurrentPos` | Vector2 | screen pixels | Current touch position this frame |
| `touchStartPos` | Vector2 | screen pixels | Touch-start position (floating joystick anchor) |
| `TK_MOV_1_JOYSTICK_RADIUS_PX` | float | see TK-MOV-1 | Joystick visual radius in screen pixels |
| `TK_MOV_2_DEAD_ZONE_NORMALIZED` | float | [0.0, 0.5] | Dead zone threshold as a fraction of joystick radius (see TK-MOV-2) |
| `isDead` | bool | {true, false} | true = treat input as zero; false = compute `worldMoveDir` via F-MOV-4 |

**Output range:** Binary. `isDead = true` also satisfies the "joystick at rest" condition in CR-MOV-3 (enables camera-steered auto-run).

---

**F-MOV-6 — Wire Encoding Precision**

```
posEncoded = RoundToInt(posWorld × 100)
posDecoded = posEncoded / 100.0
roundingError = |posWorld − posDecoded| ≤ 0.005 units  (½ LSB at ×100 encoding)
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `posWorld` | float | [−327.67, +327.67] | Server world position (one axis) |
| `posEncoded` | short (int16) | [−32767, +32767] | Wire-encoded position |
| `roundingError` | float | [0.0, 0.005] | Maximum per-axis deviation between server truth and client display |

**Output range:** Error bounded to ±0.005 units per axis. At base speed (0.30 u/tick), wire noise is 1.7% of one tick's displacement — imperceptible at 60fps. Maximum zone diameter: 655.34 units per axis. **Encoding cap constraint:** All zone walkable geometry must stay within ±327.67 units per axis from the zone origin. The server must validate `Mathf.Abs(candidatePos.x) ≤ 327.67f && Mathf.Abs(candidatePos.z) ≤ 327.67f` before encoding — floating-point accumulation must not push a position past the encoding boundary (overshoot risk: 327.67f + 0.005f rounding error wraps to a negative short). Zone geometry is constrained below this limit by NavMesh baking (EC-MOV-20). Cross-reference: `POINT_BLANK_EPSILON_SQ = 0.0005` (Hit Detection) is derived from 2 axes × (0.01 u)² = 0.0002 — the encoding floor establishes that value.

---

**F-MOV-7 — Stale Message Discard**

```
isStale = (messageTickNumber < serverCurrentTick − STALE_TICK_TOLERANCE)
STALE_TICK_TOLERANCE = 3
```

| Variable | Type | Range | Description |
|----------|------|-------|-------------|
| `messageTickNumber` | uint | [0, 2³²−1] | Tick number in the received `MovementIntentMessage` |
| `serverCurrentTick` | uint | [0, 2³²−1] | Server's current tick at message processing time |
| `STALE_TICK_TOLERANCE` | int | 3 (default — TK-MOV-4) | Ticks behind current still accepted (matches CR-MOV-10) |
| `isStale` | bool | {true, false} | true = discard message |

**Output range:** Binary. **Missing-message behavior:** A tick with no received `MovementIntentMessage` is treated as zero input — identical to a message with `DirX = 0`, `DirZ = 0`. Position is not integrated on zero-input ticks (F-MOV-1 zero-input guard). **Overflow guard:** If `serverCurrentTick ≤ STALE_TICK_TOLERANCE`, accept all messages unconditionally — this prevents uint underflow on the first few ticks after server start (EC-MOV-22).

## Edge Cases

- **EC-MOV-1 — Second touch in joystick zone while joystick is already held:** The second touch is silently ignored. The first touch owns the floating joystick anchor for its entire lifetime. No blended or second joystick input is produced.

- **EC-MOV-2 — Second touch begins in camera zone while a camera drag is already active:** The new touch is ignored for camera input. Only the first active camera-zone touch drives camera rotation; subsequent touches (not on a skill button) are discarded until the first lifts.

- **EC-MOV-3 — Joystick finger crosses screen midpoint mid-drag:** The touch continues to drive the joystick. Zone assignment is locked at touch-start (CR-MOV-1). No camera input is produced from the finger's physical position on the right half.

- **EC-MOV-4 — Skill button tapped while a camera drag is in progress from another finger:** The skill button tap fires (skill takes precedence per CR-MOV-1). The pre-existing camera drag continues uninterrupted until that finger lifts.

- **EC-MOV-5 — Skill button tap while auto-run is active:** Auto-run continues. Skill taps do not generate joystick input and do not modify the auto-run flag.

- **EC-MOV-6 — Both thumbs touch simultaneously within the same display frame:** Process all touch-begins before processing deltas in the same frame. Both zones arm correctly. Each zone's first qualifying touch is the owner for that contact.

- **EC-MOV-7 — Auto-run is active when the entity enters Prohibited state:** Auto-run cancels on Prohibited entry (CR-MOV-5 item 3). On Prohibited clear, the entity enters Idle. Auto-run is not automatically re-armed; the player must re-enable it manually.

- **EC-MOV-8 — Auto-face override fires (CR-MOV-9) while joystick is held in the forward half-plane:** The server auto-face injection overrides the client's `MovementIntentMessage` for that one tick — auto-face wins. The joystick's direction resumes on the next tick. This is an exception to the "joystick wins over auto-run" sub-state rule, which governs client-side direction priority, not server-side injections.

- **EC-MOV-9 — Auto-face override fires while auto-run is active:** Auto-run does not cancel (CR-MOV-9). On the override tick the server uses auto-face direction as `inputDir`. On the next tick the auto-run facing direction (camera-derived) resumes. If the auto-face direction points into a NavMesh boundary and `SamplePosition` finds no hit, the auto-face tick produces zero displacement; auto-run resumes on the next tick.

- **EC-MOV-10 — v_eff at stat floor (0.5 u/s) due to stacked debuffs:** The character still moves at 0.025 u/tick (2.5 cm/tick) — this is not a root. The movement animation must remain playing and must not snap to the idle pose. Distinguish v_eff = 0.5 from v_eff = 0 in the animation state machine threshold.

- **EC-MOV-11 — NavMesh.SamplePosition finds no valid walkable point within 0.5 units of the candidate position:** The server holds the previous tick's position; the character's movement animation continues playing against the static position. If no hit is found for `NAVMESH_STALL_RECOVERY_TICKS` (TK-MOV-6, default: 10 ticks = 500ms) consecutive ticks, the server executes a recovery teleport with searchRadius: 5.0f. If still no hit, the entity is flagged for Zone Instancing to handle as a geometry error.

- **EC-MOV-12 — NavMesh asset is unavailable at zone load time:** The server must abort zone initialization and reject all movement until the NavMesh is confirmed loaded. This is a Zone Instancing responsibility; the Movement System documents the dependency.

- **EC-MOV-13 — Zone transition fires while the player is moving:** The server flushes all buffered `MovementIntentMessage`s for the entity on zone exit (F-MOV-7 stale tolerance is not sufficient for cross-zone protection). Auto-run begins inactive in the new zone. The Prohibited flag is treated as cleared on zone entry unless explicitly re-asserted by the external system.

- **EC-MOV-14 — Zone transition fires on the same tick that auto-face would fire:** The auto-face one-tick injection is cleared as part of entity teardown for the old zone. No auto-face carries into the new zone.

- **EC-MOV-15 — Player reconnects within SESSION_TTL_SECONDS:** The client snaps to the received `ZoneStateSnapshot` position unconditionally — no interpolation from any locally cached pre-disconnect position. Auto-run begins inactive.

- **EC-MOV-16 — MovementIntentMessage.SenderEntityID does not match the connection's authenticated EntityID:** The server discards the message and logs a security event. No movement is applied.

- **EC-MOV-17 — Two MovementIntentMessages arrive from the same entity within the same server tick (U-U channel reordering or duplicate):** The server uses only the last received message for that entity per tick. Earlier arrivals with the same TickNumber are discarded.

- **EC-MOV-18 — Entity's characterForward_world is a zero vector at zone entry:** Entity facing must be initialized to a non-zero value (minimum: `Vector3.forward`) before the first tick to prevent F-MOV-3 from evaluating with a zero forward vector.

- **EC-MOV-19 — GetEffectiveStat(MovementSpeed) returns an error or out-of-range value:** The server uses the last known valid `v_eff` for that entity on that tick and logs a critical warning. Position integration does not proceed with an undefined speed.

- **EC-MOV-20 — Zone geometry places walkable surfaces beyond ±327.67 units from zone origin:** `short × 100` wire encoding cannot represent positions beyond this range (F-MOV-6). All walkable NavMesh surfaces must remain within ±327 units on the X and Z axes from the zone coordinate origin. This is a constraint on Zone Instancing GDD authoring.

- **EC-MOV-21 — Joystick dead zone produces DirX = 0, DirZ = 0 after wire encoding:** The server receives inputDir = zero; F-MOV-1 zero-input guard fires; no movement is applied. Not an error. The dead zone threshold (TK-MOV-2) must be tuned so that any drag passing the dead zone produces at least one non-zero short component — verify during implementation.

- **EC-MOV-22 — serverCurrentTick = 0 at session start causing F-MOV-7 uint underflow:** `serverCurrentTick − STALE_TICK_TOLERANCE` underflows to 2³²−1. Accept all messages unconditionally when `serverCurrentTick ≤ STALE_TICK_TOLERANCE`.

## Dependencies

### Upstream Dependencies (Movement System requires these)

| System | Status | What Movement requires |
|--------|--------|----------------------|
| **Networking Core** | Approved | Server tick loop (CR-NET-2, TICK_RATE_HZ = 20); server-authoritative position model (CR-NET-1); EntityState schema (posX, posY, posZ, facingAngle wire fields); U-U channel routing for MovementIntentMessage |
| **Character Stats** | Approved | `GetEffectiveStat(MovementSpeed)` — F-MOV-2 uses this as v_base; v_eff floor (0.5) and ceiling (20.0) are enforced here, not in Character Stats |
| **Zone Instancing** | Not Started | Per-zone NavMesh asset (pre-baked, never runtime); zone boundary coordinates used for ±327.67-unit wire encoding constraint; zone load signals to trigger NavMesh load on both client and server |

**Hard dependencies:** All three must be resolved before Movement System can be implemented. Zone Instancing is not yet designed — the zone boundary contract and NavMesh provision interface are provisional (flagged in CR-MOV-7, CR-MOV-8).

### Soft Dependencies (influence behavior, not hard-blocked)

| System | Status | What Movement requires |
|--------|--------|----------------------|
| **Auto-Attack Combat** | Approved | Rule 20 auto-face injection — server writes one-tick direction override on first beat; Movement System must not overwrite this override for that tick (EC-MOV-11) |
| **Status Effects / Buffs** | Not Started | Speed buff/debuff modifiers feed F-MOV-2 as ΣFlatBuff and ΣPctBuff; Movement System defines the formula contract, Status Effects must conform to it when designed |

### Downstream Dependents (these systems require Movement System)

| System | Status | What it requires from Movement |
|--------|--------|-------------------------------|
| **Navigation / Pathfinding** | Not Started | Authoritative world positions (posX/Y/Z) for mob and NPC locomotion; NavMesh contract defined by Movement System (CR-MOV-7) |
| **Client-Side Prediction** | Not Started | MovementIntentMessage schema and tick semantics; F-MOV-1 integration formula; position reconciliation protocol (EC-MOV-12) |
| **Map / Minimap** | Not Started | Authoritative posX/posZ per tick for player dot rendering |

### Primitive Gap

**MovementIntentMessage** — defined in CR-MOV-10 and F-MOV-7 of this GDD but not yet registered in the Wire Protocol GDD. The Wire Protocol GDD must be amended to add this message (fields: SenderEntityID uint, DirX short×2, DirZ short×2, TickNumber uint; channel U-U) before Movement System implementation begins.

## Tuning Knobs

| ID | Name | Default | Safe Range | What it controls |
|----|------|---------|------------|-----------------|
| **TK-MOV-1** | `JOYSTICK_RADIUS_PX` | 80 px | 60–120 px | Visual size of the joystick thumb area. Below 60px the control becomes unreachable for wide thumbs; above 120px it overlaps skill buttons in landscape. |
| **TK-MOV-2** | `DEAD_ZONE_NORMALIZED` | 0.15 | 0.05–0.30 | Fraction of joystick radius before movement registers. Below 0.05 causes jitter from touch noise; above 0.30 the joystick feels unresponsive on short taps. Note: 0.15 may be at or below typical 3–5 mm touch imprecision on iOS — validate empirically on target devices before shipping. |
| **TK-MOV-3** | `BASE_MOVE_SPEED` | 6.0 u/s | 4.0–10.0 u/s | Default v_base fed into F-MOV-2 before equipment and buff modifiers. Below 4.0 the world feels sluggish and grind loops slow down disproportionately; above 10.0 players outrun mob aggro ranges in tested zones. |
| **TK-MOV-4** | `STALE_TICK_TOLERANCE` | 3 ticks | 2–5 ticks | How many ticks behind the server current tick a MovementIntentMessage can be before it is discarded (F-MOV-7). Default 3 covers 150ms OWL tolerance (3 ticks × 50ms), matching the 150ms RTT / 75ms OWL target — resolved OQ-CSP-1. Below 2: inputs on 100ms+ RTT connections begin being discarded; rubber-band visible on moderate-latency mobile. Above 5: server retains 250ms+ of stale-input state; security window widens. Coordinate with Networking Core before changing. |
| **TK-MOV-5** | `CAMERA_DRAG_SENSITIVITY` | 0.25 °/px | 0.10–0.50 °/px | How far the camera rotates per pixel of horizontal drag. Below 0.10 requires large sweeps to reorient; above 0.50 the camera overcorrects on fast swipes and induces nausea complaints on QA. |
| **TK-MOV-6** | `NAVMESH_STALL_RECOVERY_TICKS` | 10 ticks | 5–30 ticks | Consecutive ticks without a valid `NavMesh.SamplePosition` hit before the server escalates to a 5.0f-radius recovery teleport (EC-MOV-11). At default 10 ticks = 500ms. Below 5: triggers premature recovery from momentary geometry edges; above 30: player is visually stuck for >1.5s before recovery attempts. |

**Tuning authority:** All six knobs are safe to adjust during playtesting without code changes (expose via ScriptableObject). TK-MOV-3 interacts with zone geometry pacing — coordinate with level design before changing. TK-MOV-4 interacts with network latency targets — coordinate with Networking Core before changing. TK-MOV-6 interacts with NavMesh geometry edge-case recovery — coordinate with level design before reducing below 5.

## Visual/Audio Requirements

### Animations

**Idle**
- `char_[class]_idle_lod0.fbx` — Chest-rise breathing loop (Y scale 1.0 → 1.02, 0.6s cycle). Loops indefinitely. Blends out to Run on any movement input. Blend-out duration: 0.10s ease-out.
- Entry condition: `v_eff = 0` OR joystick released + auto-run inactive + Prohibited cleared.

**Run (base)**
- `char_[class]_run_lod0.fbx` — Full run cycle at `v_eff = 6.0 u/s`. Loops. Weighted, grounded footfalls; no bounce silhouette noise.
- Animator float `NormalizedSpeed = v_eff / 20.0`. Idle pose at 0.0, full run at 0.30. Values above 0.30 drive animation playback-speed scale only — no additional blend tree node required at MVP.
- Auto-run active: same animation as manual run. No additional asset.

**Prohibited (stun) — entry**
- `char_[class]_stun-enter_lod0.fbx` — One-shot, 0.20s. Character lurches into planted wide-stance crouch; weapon arm drops, head tilts. Blends from any active state into Prohibited idle loop.

**Prohibited (stun) — loop**
- `char_[class]_stun-idle_lod0.fbx` — Subtle torso tremor loop (0.05 Y-axis oscillation, 1.2s period). Holds stun-enter's crouched pose. Loops until Prohibited flag clears. Tremor distinguishes stun-idle from a network freeze (deliberate game-feel contract).

**Prohibited (stun) — exit**
- `char_[class]_stun-exit_lod0.fbx` — One-shot, 0.15s. Character straightens into Run or Idle depending on exit target state.

**Speed-buffed run** — no new animation asset. Speed-scale multiplier on existing run cycle handles cadence. VFX trail carries the visual read.

---

### VFX

**Stun entry — impact burst**
- `vfx_stun-enter_burst_small` — One-shot, 8-particle burst, ≤ 0.25s lifetime. Unlit/additive. Head/shoulder region. Color: `#7A8FA0`. Triggers on Prohibited flag set.
- Mobile: 8-particle max. URP unlit/additive shader only.

**Stun active — overhead marker**
- `vfx_stun-loop_icon_small` — Single diamond-shaped billboard sprite, 0.4 units above character head. Pulses opacity 1.0 → 0.5 → 1.0 at 0.8s cycle. Color: `#7A8FA0`. Zero particle budget cost. Minimum 16×16 px rendered at 15-unit camera distance on 1080p landscape.

**Stun exit — clear flash**
- `vfx_stun-exit_flash_small` — Single-frame additive silhouette flash, 0.12s fade, `#7A8FA0` at 30% opacity. One-shot. Overhead marker fades simultaneously. Triggers on Prohibited flag cleared.

**Speed buff active — ground trail**
- `vfx_speed-buff_trail_small` — Ground-level wake, 6-particle max, 0.3s lifetime each. Unlit/additive. Color: `#4A9EE0` (Skill Blue). Emits only when `v_eff > v_base`; emit rate scales to 8 particles/s at speed cap. Does NOT emit during auto-run at base speed.

**Zone boundary clamp** — no VFX. Per CR-MOV-7: boundaries are not discoverable as explicit walls. Silent clamp.

**Auto-run toggle** — no VFX. Visual delta is UI only (see UI Requirements).

---

### Audio

**Footstep loop**
- `sfx_footstep_[surface]_loop_01.wav` — Required MVP surface variants: `stone`, `dirt`. 3 randomized samples per variant (prevents machine-gun effect). Mono, 44.1 kHz, 16-bit → shipped as `.ogg` Vorbis q7.
- Triggers while `v_eff > 0` and Moving state. Stops immediately on Idle — no deceleration fade (matches zero-deceleration movement design).
- Playback rate scales: 1.0× at `v_base`, up to 1.6× at `v_cap`. Dry signal only; no reverb chain.

**Stun entry cue**
- `sfx_stun-enter_impact_01.wav` — One-shot, ≤ 0.4s. Character grunt + dull metallic thud. Lower-frequency fundamental (120–200 Hz) to distinguish from hit-received sounds. Fires on Prohibited flag set.

**Stun active hum**
- `sfx_stun-active_hum_loop_01.wav` — Low-volume (−12 dB relative to footstep) tonal hum, 1.0–1.2s loop. Subconscious "something is wrong" signal. Stops on stun exit. Load into memory on zone entry — do not stream.

**Stun exit cue**
- `sfx_stun-exit_clear_01.wav` — One-shot, ≤ 0.2s. Brief high-mid click/snap. Shorter and brighter than stun-enter to reinforce the state contrast.

**Speed buff activation**
- `sfx_speed-buff_activate_01.wav` — One-shot on buff application, ≤ 0.3s. Ascending 2-note figure. No looping audio while buff is active — footstep pitch shift carries the ongoing read.

**Auto-run toggle** — uses global UI button press SFX. No movement-specific audio event.

**Zone boundary clamp** — no audio. Matches no-VFX decision.

## UI Requirements

All movement UI must be positioned within safe-area insets (`Screen.safeArea`) to support notched iPhones (iPhone X+).

### Virtual Joystick

- **Zone:** Left half of safe-area-inset screen width. Touch-start position determines zone ownership for the full touch lifetime.
- **Appearance:** Spawns centered at touch-start position. Disappears on touch-end. No persistent ghost or resting-state ring between touch events.
- **Ring:** `ui_joystick-ring_default_128.png` — 80pt diameter at default `JOYSTICK_RADIUS_PX`. `#3A3D44` 2px outline, no fill.
- **Nub:** `ui_joystick-nub_default_64.png` — 40pt filled circle, `#4A9EE0` at 80% opacity. Tracks finger within ring bounds. Opacity drops to 50% inside dead zone (finger within 15% of ring radius) to provide dead zone feedback.
- **Minimum tap target:** 44×44 pt per iOS HIG. The ring's 80pt diameter exceeds this.

### Auto-Run Toggle Button

- **Position:** Right side of screen, reachable by right thumb in landscape two-hand hold. Must not overlap skill button area.
- **Size:** 44×28 pt visual / 44×44 pt tap target (iOS HIG minimum).
- **Inactive state:** `ui_btn_autorun_inactive_64.png` — `#1A1C1F` fill, `#3A3D44` 2px outline, label "AUTO" in `#9A9DA6` 11sp.
- **Active state:** `ui_btn_autorun_active_64.png` — `#4A9EE0` fill at 60% opacity, label "AUTO" in `#E8E6DF`. Instant state swap on toggle — no animation.

### Stun Overlay

- Full-viewport desaturated vignette, `#7A8FA0` at 8% opacity, URP post-process Volume override. Fades in 0.10s on stun entry, fades out 0.15s on stun exit. Active only while Prohibited flag is set — not a persistent per-frame cost.

### Ownership

UI Programmer implements joystick input logic. UX Designer owns screen layout and tap-target audit before implementation begins.

## Acceptance Criteria

### Input & Controls

**AC-MOV-1** — Dead zone boundary enforcement. When a joystick displacement magnitude is less than 15% of JOYSTICK_RADIUS_PX (12 px at default 80 px), the MovementIntentMessage DirX and DirZ fields are both zero and no position delta is applied on the server for that tick.

**AC-MOV-2** — Joystick spawn position. When a new touch begins anywhere on the left half of the screen, the virtual joystick center spawns at the exact screen pixel of the touch-start event — it does not snap to a fixed position.

**AC-MOV-3** — Left/right zone partition enforcement. A touch that begins on the right half of the screen does not activate the virtual joystick; a simultaneous touch that begins on the left half does not activate camera drag. Neither handler claims an event whose touch-start X coordinate falls in the opposing zone.

**AC-MOV-4** — Camera drag sensitivity. A right-half horizontal drag of exactly 100 pixels rotates the camera yaw by 25.0 ° ± 0.5 °, matching CAMERA_DRAG_SENSITIVITY = 0.25 °/px.

**AC-MOV-5** — Camera-relative movement direction. When the player faces 90 ° (east) via camera rotation and pushes the joystick fully forward, the server-computed movement direction vector is (1, 0, 0) ± 0.01 in world space.

**AC-MOV-6** — Character facing updates only on joystick rest. While a joystick touch is active (displacement > dead zone), rotating the camera via a simultaneous right-half drag does not change the character's world-space facing; facing updates to match the camera direction only after the joystick touch is released.

### Movement & Speed

**AC-MOV-7** — Immediate stop on input release. When a joystick touch is lifted, the character position delta in the immediately following tick is 0.0 units on both X and Z axes.

**AC-MOV-8** — F-MOV-1 position delta correctness at base speed. With no equipment or buff modifiers and BASE_MOVE_SPEED = 6.0 u/s, a fully-deflected joystick input produces a server-side position delta of exactly 0.3 units per tick (6.0 × 1/20) along the movement axis, within NavMesh-validated bounds.

**AC-MOV-9** — Speed formula clamp — minimum. When computed v_eff would fall below 0.5 u/s, the server clamps to 0.5 u/s and the per-tick delta is 0.025 units, not less.

**AC-MOV-10** — Speed formula clamp — maximum. When computed v_eff would exceed 20.0 u/s, the server clamps to 20.0 u/s and the per-tick delta is 1.0 unit, not more.

**AC-MOV-11** — Wire encoding precision (F-MOV-6). Encoding and decoding 1,000 random positions in the range ±327.0 units via short×100 produces a round-trip error ≤ 0.005 units on every axis across all samples.

### Auto-Run

**AC-MOV-12** — Auto-run toggle and cancel by re-press. One button press produces continuous forward MovementIntentMessages at every tick without further touch input; a second press returns movement direction to zero on the next tick.

**AC-MOV-13** — Auto-run cancels on rear-half joystick pull. While auto-run is active, deflecting the joystick such that `dot(joystickDir, characterForward) < 0` cancels auto-run within the same tick; the server stops receiving forward intent from that tick onward, and the joystick direction is applied instead.

### Server Authority & Wire

**AC-MOV-14** — Message send rate. Under continuous joystick input, the client sends one MovementIntentMessage per server tick at 20 Hz; over any 5-second window the message count is between 95 and 105, never fewer than 90 or more than 110.

**AC-MOV-15** — Stale message discard. A MovementIntentMessage whose TickNumber is less than (serverCurrentTick − STALE_TICK_TOLERANCE) is discarded; the server-side position for that entity is unchanged for that tick.

**AC-MOV-16** — Startup tick guard. When serverCurrentTick is ≤ STALE_TICK_TOLERANCE (default: 0 to 3), a message with TickNumber = 0 is accepted and processed normally — no false-stale discard occurs during startup ticks.

**AC-MOV-17** — Spoofed SenderEntityID security response. A MovementIntentMessage whose SenderEntityID does not match the authenticated session is discarded with no position update applied, and one security log entry is written containing both the received SenderEntityID and the session's authenticated entity ID.

### State Handling

**AC-MOV-18** — Stun locks position but permits facing rotation. While in Prohibited state, a non-zero direction MovementIntentMessage is discarded with no position delta; a simultaneous facing-rotation input updates the server's stored facing angle for the entity normally.

**AC-MOV-19** — Auto-run cancels on stun entry. When an entity with auto-run active enters Prohibited state, the auto-run flag is cleared within the same tick; no forward movement intent is generated in subsequent ticks while stun persists.

**AC-MOV-20** — Auto-face tick override does not cancel auto-run. During an auto-attack beat, the server injects a one-tick facing override; in the tick immediately following, auto-run resumes generating forward movement intent and the auto-run flag remains set.

### NavMesh & Performance

**AC-MOV-21** — NavMesh validation — no runtime bake. The server calls SamplePosition on every movement update; no runtime NavMesh bake API call is made after scene load, verified by asserting the bake call count is zero over a 60-second play session.

**AC-MOV-22** — Client-side wire throughput at 60 fps. On a reference device at sustained 60 fps, encoding and dispatching MovementIntentMessages does not exceed 0.5 ms per frame, measured via Unity Profiler over a 30-second sample.

### Prohibited State — Extended

**AC-MOV-23** — Idle-to-Prohibited transition. When an entity in Idle state receives a Prohibited flag set, it transitions to Prohibited: no position delta is applied on that tick or subsequent ticks while the flag is set, and no forward movement intent is generated.

**AC-MOV-24** — NavMesh stall recovery teleport (EC-MOV-11). When `NavMesh.SamplePosition` finds no hit for exactly NAVMESH_STALL_RECOVERY_TICKS (default 10) consecutive ticks, the server on tick 11 executes a recovery teleport using a 5.0f searchRadius. The entity's committed position after the teleport is within 5.0 world units of the stalled position, and a subsequent `SamplePosition` call with 0.5f radius at the teleported position returns a hit.

**AC-MOV-25** — NavMesh unavailable at zone load (EC-MOV-12). When the NavMesh asset fails to load at zone initialization, the server rejects all MovementIntentMessages for entities in that zone and no position integration occurs. Zone initialization is reported as failed to the Zone Instancing system; movement resumes only after NavMesh load is confirmed.

**AC-MOV-26** — Simultaneous zone-entry touches (EC-MOV-6). When two touches begin within the same display frame, one with a start X in the left half of the screen and one in the right half, both zone handlers arm correctly: the left touch activates the virtual joystick and the right touch activates camera drag, with no cross-contamination.

**AC-MOV-27** — Zone-exit message flush (EC-MOV-13). When an entity transitions zones while moving, all buffered MovementIntentMessages for that entity are discarded on zone exit; no position delta is applied in the new zone on the first tick from a message generated in the old zone.

**AC-MOV-28** — Duplicate message discard (EC-MOV-17). When two MovementIntentMessages with the same TickNumber arrive for the same entity within the same server tick, only the last received message is processed; the entity position reflects only that message's direction, and exactly one tick's worth of movement is applied.

## Open Questions

| ID | Question | Blocking? | Owner |
|----|----------|-----------|-------|
| **OQ-MOV-1** | ~~Wire Protocol GDD must be amended to register MovementIntentMessage~~ **CLOSED 2026-05-26** — `MovementIntentMessage` registered in `networking-wire-protocol.md` (Movement System Messages section). `EntityState.facingAngle` field also added. F-NET-7 inbound bandwidth updated. | ~~Yes — implementation blocker~~ **RESOLVED** | Wire Protocol GDD author |
| **OQ-MOV-2** | Zone Instancing GDD (Not Started) must specify: (a) per-zone NavMesh asset naming and load API, (b) zone boundary coordinates so the ±327.67-unit wire encoding constraint can be validated per zone, (c) zone-load signal that triggers NavMesh load on server and client. CR-MOV-7 and CR-MOV-8 are provisional until this contract is defined. | **Yes — implementation blocker** | Zone Instancing GDD author |
| **OQ-MOV-3** | Status Effects / Buffs GDD (Not Started) must conform to the F-MOV-2 modifier contract: flat stat additions (ΣFlatBuff, ΣFlatEquip) and percentage multipliers (ΣPctBuff, ΣPctEquip) provided via the same GetEffectiveStat interface used by other stats. Until this contract is confirmed, speed buff behavior is provisional. | No — not blocking MVP | Status Effects GDD author |
| **OQ-MOV-4** | ~~Whether the client needs to load the full NavMesh asset for local movement validation or whether client-side prediction handles it entirely.~~ **RESOLVED 2026-06-15** — Client does NOT load NavMesh. CR-CSP-2: self-prediction applies F-MOV-1 without NavMesh validation. CR-CSP-14: predicted_position is display-only; all server-side logic (hit detection, proximity, loot, zone transition) uses the server's NavMesh-committed position. Boundary violations are corrected via server reconciliation (snap or smooth correction per CR-CSP-7/CR-CSP-10). | ~~No~~ **RESOLVED** | `client-side-prediction.md` — CR-CSP-2, CR-CSP-14 |
