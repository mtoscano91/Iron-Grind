# Client-Side Prediction

> **Status**: Approved (lean re-review #2, 2026-06-15)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-06-15
> **Implements Pillar**: Pillar 2 — Rhythm Mastery (responsive input feel); Pillar 3 — Social Gravity (smooth entity movement)

## Overview

Client-Side Prediction is a client-only display layer that eliminates the perceptible latency between touch input and rendered character movement, and smooths the 20 Hz server tick stream into a continuous 60fps rendering experience for all entities in the zone. It owns two responsibilities: **(1) self-prediction** — the owning client applies its own movement intent using the same position integration formula the server uses (Movement System F-MOV-1), buffering a short history of inputs so that when the authoritative server position arrives, divergence can be detected and reconciled; and **(2) entity interpolation** — other players' and mobs' positions arrive at 20 Hz in the server batch; this system renders them at 60fps by interpolating between the two most recently received snapshots. Prediction scope at MVP is strictly positional: combat outcomes, HP, gold, and inventory changes are never predicted (Networking Core CR-NET-1.1) — their authoritative values are applied directly on receipt. This GDD also resolves Movement System OQ-MOV-4: whether the client loads a zone NavMesh for local movement boundary validation during prediction, or defers entirely to server correction. Client-Side Prediction introduces no server-side logic changes; all authority contracts remain in the Movement System and Networking Core.

## Player Fantasy

Client-Side Prediction is never seen — it is felt.

The player feels that their character answers their hands with no hesitation between them. When they swipe to reposition the instant before a telegraphed slam lands, the rogue slides clear *now* — the dodge reads as a reflex they earned, not a command the world got around to honoring. The intended fantasy is direct authorship over the character's body: intent and motion are a single event.

The shared zone feels genuinely *occupied*. Other players and creatures move like real bodies traveling continuous paths, not markers blinking between positions. As the player holds a contested chokepoint, allies stream past in smooth arcs and an enemy circles wide to flank — every motion reads as deliberate and physical. This continuity is what makes the space feel social and dangerous rather than a slideshow of snapshots.

Success for this system is measured by what the player does not notice. The technology should vanish so completely that the player credits the smoothness to their own skill and the world's presence. If a player ever thinks "the network is getting in the way," Client-Side Prediction has failed.

## Detailed Rules

### Core Rules

**CR-CSP-1 — System scope**
Client-Side Prediction owns two subsystems running in parallel: **(SP) self-prediction** — applies movement locally for the owning client's character each render frame — and **(EI) entity interpolation** — smooths all other entities' positions from 20 Hz server snapshots into 60fps render output. No prediction is performed for HP, gold, inventory, or combat outcomes; those values are applied directly on server receipt (CR-NET-1.1).

**CR-CSP-2 — Self-prediction: local movement application**
Each render frame, the owning client applies F-MOV-1 to a locally tracked `predicted_position` without waiting for server confirmation:
```
predicted_position += normalize(inputDir) × v_eff × dt_clamped
```
This position is used immediately for rendering the local character and for camera follow (CR-MOV-3). The server runs the same formula independently; divergence is corrected by reconciliation (CR-CSP-7).

`predicted_position` is XZ only. For rendering, the Y world-space coordinate is sourced from `SelfPositionUpdate.posY` in the last received R-U batch (F-MOV-6 `short × 100` Y-encoding); the Y axis is not predicted. `inputDir` and all ring buffer entries are also XZ-only (see CR-CSP-6).

**CR-CSP-3 — Client tick definition**
A client tick aligns with the 50ms server tick cycle. The client derives its current tick from `NetworkManager.ServerTime.Tick` (NGO NetworkTickSystem) — no independent tick counter is maintained. Input is sampled at the render frame rate but aggregated into one record per server tick boundary. One `MovementIntentMessage` is sent per tick (AC-MOV-14), and one ring buffer entry is written per tick (CR-CSP-6).

**CR-CSP-4 — Maximum dt clamp**
Before applying F-MOV-1, actual frame delta is clamped:
```
dt_clamped = min(actualFrameDelta, 1.0 / TICK_RATE_HZ)   // max 50ms
```
This prevents GC pauses, thermal throttle stalls, or OS interruptions from producing single-frame prediction overruns that exceed one server tick of travel.

**CR-CSP-5 — Effective speed source for prediction**
The client uses its last received authoritative stat snapshot to compute v_eff. Stat values are never self-reported to the server. Divergence from a buff or debuff the server applied but the client hasn't yet received is corrected by reconciliation — it is not a correctness failure.

**CR-CSP-6 — Input history ring buffer**
A fixed-size ring buffer (`INPUT_HISTORY_SIZE = 32`) stores one struct entry per server tick. Structs are value types; the buffer is pre-allocated at zone join and never reallocated. When the write pointer wraps past 32, the oldest entry is overwritten.

| Field | Type | Purpose |
|-------|------|---------|
| `tickNumber` | uint | Server tick this entry belongs to |
| `predictedPosition` | Vector2 (XZ) | Client's predicted position at this tick |
| `inputDir` | Vector2 (XZ) | Input direction sent to server |
| `movementStateFlags` | byte | Local movement state at this tick |

The buffer is used for divergence calculation at reconciliation time (CR-CSP-7). Rollback and replay are out of scope at MVP.

**Write timing:** the entry for tick T is written at the server tick boundary — when `NetworkManager.ServerTime.Tick` increments from T to T+1, after all render-frame integrations belonging to tick T have been applied to `predicted_position`. Writing at any render frame mid-tick would capture an intermediate predicted position (not the end-of-tick value), producing false reconciliation on every server update even under zero-latency conditions.

**CR-CSP-7 — Reconciliation trigger**
When the server position update for the local player arrives via `SelfPositionUpdate` (R-U batch):
1. Read server authoritative position for tick T: `serverPos_T` (XZ from `SelfPositionUpdate.posX/.posZ`)
2. Look up ring buffer entry for tick T: `ring[T & 0x1F].predictedPosition`
3. Compute true error: `error = serverPos_T − ring[T].predictedPosition`
4. `|error| < RECONCILE_THRESHOLD_UNITS` → no action; discard
5. `RECONCILE_THRESHOLD_UNITS ≤ |error| < RECONCILE_SNAP_THRESHOLD_UNITS` → smooth correction (CR-CSP-8)
6. `|error| ≥ RECONCILE_SNAP_THRESHOLD_UNITS` → immediate snap (CR-CSP-10)

Comparing against `ring[T]` rather than the current predicted position is required: the client is rendering tick T+N where N ≈ RTT_ticks (~3 ticks at 150ms RTT). Comparing server tick T against current predicted position T+N would include N ticks of legitimate movement in the error, triggering false reconciliation on every update.

**CR-CSP-8 — Reconciliation algorithm (additive correction offset)**
Smooth correction uses an additive offset; prediction continues uninterrupted:
- `correction_offset` is set to `error` (CR-CSP-7) when reconciliation fires
- Each render frame: `correction_offset = Vector2.MoveTowards(correction_offset, Vector2.zero, MAX_CORRECTION_SPEED × dt_clamped)` (speed cap per CR-CSP-9; F-CSP-4 gives the explicit equation)
- `rendered_position = predicted_position + correction_offset`

The local character's gameplay position for camera and UI reads is `predicted_position`. Only the transform submitted to the renderer includes the offset.

**CR-CSP-9 — Correction speed cap**
```
MAX_CORRECTION_SPEED = 1.5 × v_eff       (units/second)
```
Correction velocity is capped so that large-delta corrections appear as smooth drift rather than rubber-band snaps. At base speed 6 u/s: cap = 9 u/s. A 0.10-unit error resolves in ~1 frame (invisible). A 1.50-unit error resolves in ~10 render frames (~166ms) — reads as natural drift. Errors at or above `RECONCILE_SNAP_THRESHOLD_UNITS` trigger CR-CSP-10 instead.

**CR-CSP-10 — Snap threshold**
If `|error| ≥ RECONCILE_SNAP_THRESHOLD_UNITS`, skip lerp: snap `predicted_position` to `serverPos_T` immediately and clear `correction_offset`. Snaps signal boundary corrections (OQ-MOV-4 result), severe network outages, or server-enforced state overrides. Snap events are candidates for a visual mask (VFX/camera cut); see Visual/Audio Requirements.

**CR-CSP-11 — New server update mid-correction**
When a server update for tick T2 arrives while a correction for tick T1 is still blending:
1. Compute new error: `new_error = serverPos_T2 − ring[T2 & 0x1F].predictedPosition`
2. If `|new_error| ≥ RECONCILE_SNAP_THRESHOLD_UNITS` → trigger CR-CSP-10 (immediate snap) and discard the in-progress blend
3. Otherwise: set `correction_offset` to `new_error`; restart the blend
The previous blend is discarded. This is the expected case — at 20 Hz updates and sub-5-frame blends, nearly every reconciliation lerp receives a second update before completing.

**CR-CSP-12 — Prohibited state: prediction suspension**
On receiving a Prohibited movement flag in the local player's EntityState (CR-MOV-8):
1. Suspend self-prediction immediately
2. Snap `predicted_position` to server position (ignore delta; bypass lerp regardless of size)
3. Clear `correction_offset`
4. Resume prediction when Prohibited flag is cleared in a subsequent batch

Prediction suspension takes priority over any active reconciliation lerp.

**CR-CSP-13 — Auto-face divergence (expected, non-bug)**
Server-side auto-face injection (CR-MOV-9) overrides `inputDir` for one tick on the server. The client predicts using its own input during this tick, producing a guaranteed small divergence corrected by the next server update. QA must not file this as a defect.

**CR-CSP-14 — Client authority boundary (security invariant)**
`predicted_position` is used only for local rendering. It carries zero server authority. All server-side logic — hit detection, proximity checks, loot pickup, zone transitions — uses the server's committed position (CR-MOV-6). A client that suppresses reconciliation affects only its own display; all other clients and all server-side game logic are unaffected. The server never accepts a client-submitted position, and `v_eff` is always derived from server-authoritative stats (CR-CSP-5).

**CR-CSP-15 — Interpolation delay buffer**
All entities other than the local player are rendered at:
```
render_time = server_time − INTERP_DELAY_MS     // = server_time − 75ms
```
At 75ms delay (1.5× the 50ms snapshot interval), the client always has two bracketing snapshots available before `render_time` is reached, providing immunity to ±25ms network jitter. The delay applies to visual position and rotation only; HP, status effects, and ability casts apply at actual receipt time.

**CR-CSP-16 — Entity interpolation algorithm**
For each entity with ≥2 snapshots, each render frame:
1. Find the two snapshots (S0, S1) bracketing `render_time`: `S0.serverTick ≤ render_time_tick < S1.serverTick`
2. `t = (render_time − S0.serverTime) / (S1.serverTime − S0.serverTime)`, t ∈ [0, 1]
3. `position = Vector3.Lerp(S0.position, S1.position, t)`
4. `rotation = Quaternion.Slerp(S0.rotation, S1.rotation, t)` — slerp is mandatory; linear quaternion interpolation is not normalized and produces visual artifacts
5. Animation state: apply S0's discrete animation flags at the bracket boundary; do not interpolate discrete state values
6. Snapshot lookup uses server-assigned `serverTick` numbers, not client wall-clock arrival times

Mob path-following produces NavMesh-snapped server positions. Linear interpolation between those positions will geometrically cut corners. This is an accepted visual trade-off at MVP.

**CR-CSP-17 — Packet loss and stale snapshot fallback**
If snapshot S1 is not available when `render_time` exceeds S0.serverTime:
1. **Extrapolate** for up to `MAX_EXTRAPOLATION_MS = 50ms` using S0's estimated velocity
2. If extrapolation age exceeds 50ms: **hold** at S0.position
3. When S1 arrives: resume interpolation from current held/extrapolated position (no snap, no correction lerp on resume)

The hold-then-resume produces a micro-stutter that is preferable to a visible snap or wall-clipping extrapolation.

**CR-CSP-18 — Wire protocol delivery cap**
The wire protocol delivers at most 35 entity position updates per tick (networking-wire-protocol.md PA-P7-05). At zone capacity (40–60 entities), 10–40% of entities receive no update in a given tick. These entities enter the CR-CSP-17 fallback path. The entity interpolation system does not distinguish "update not sent" from "update lost in transit" — both are handled identically.

**CR-CSP-19 — Snapshot storage per entity**
Each entity's snapshot buffer is a pre-allocated fixed-size value-type struct array (length 4 — last 4 snapshots). Entity slot lookup is O(1) array-index access. Four entries are required: at the maximum `INTERP_DELAY_MS` = 150ms (3 tick-intervals), the render-time bracket consumes snapshots at T−3 and T−2; the buffer must retain T−3 while T−1 and T arrive. Three entries would evict T−3 before it is consumed, causing a fallback to extrapolation at maximum delay. Forbidden patterns in the interpolation hot path (IL2CPP GC constraint): `Dictionary` lookup, `List<T>` or `Queue<T>` for snapshot storage, LINQ expressions, closure captures, per-frame `string` formatting.

**CR-CSP-20 — Entity cold-start (fewer than 2 snapshots)**
On entity spawn (zone join ZoneStateSnapshot EC-MOV-15, or mid-session spawn):
- 0 snapshots: entity not rendered
- 1 snapshot: entity snaps to snapshot[0].position; animation state = idle
- 2+ snapshots: normal interpolation begins

The initial snap-in is expected and acceptable.

**CR-CSP-21 — Clock source**
All tick numbering uses `NetworkManager.ServerTime.Tick` (NGO NetworkTickSystem). The client does not maintain an independent tick counter. This ensures `MovementIntentMessage.TickNumber` values align with the server's stale-check in F-MOV-7, and entity snapshot `serverTime` values align with the interpolation time base.

**CR-CSP-22 — Script execution order**
- Self-prediction and entity interpolation: `Update()` phase (before any `LateUpdate`)
- Camera follow (CR-MOV-3): `LateUpdate()` phase
- No prediction or interpolation writes occur in `LateUpdate()`

This ordering ensures the camera reads current-frame predicted/interpolated positions, not last-frame values.

### States and Transitions

**Self-Prediction States**

| State | Entry Condition | Exit Condition | Rendered Position |
|-------|-----------------|----------------|-------------------|
| PREDICTING | Default | Correction triggers or Prohibited received | `predicted_position` |
| CORRECTING | `|error| ≥ RECONCILE_THRESHOLD_UNITS` | `|correction_offset| → 0` or Prohibited received | `predicted_position + correction_offset` |
| SUSPENDED | Prohibited flag set in EntityState | Prohibited flag cleared | `server_position` (direct snap) |

**Entity Interpolation States (per entity)**

| State | Entry Condition | Exit Condition |
|-------|-----------------|----------------|
| INTERPOLATING | ≥2 snapshots bracket render_time | Snapshot gap opens |
| EXTRAPOLATING | 1 snapshot; extrapolation age < MAX_EXTRAPOLATION_MS | S1 arrives OR age limit exceeded |
| HELD | Extrapolation age ≥ 50ms | New snapshot arrives |

### Interactions with Other Systems

**Movement System (movement-system.md)**: CSP replicates F-MOV-1 exactly (same formula and variables; dt clamped per CR-CSP-4). v_eff derived from authoritative stat snapshot (CR-CSP-5). Prohibited flag per CR-CSP-12 (CR-MOV-8). Auto-face divergence acknowledged per CR-CSP-13 (CR-MOV-9). No NavMesh loaded on client (OQ-MOV-4 resolved).

**Networking Core (networking-core.md)**: CSP constrains prediction to non-consequential outcomes (CR-NET-1.1). No server-side logic changes. Server authority contracts (CR-NET-1, CR-NET-2) are unchanged.

**Wire Protocol (networking-wire-protocol.md)**: Two separate per-tick delivery paths: (1) `SelfPositionUpdate` (R-U batch, CR-NET-7.7) — per-tick authoritative self-position for the owning client only; consumed by CR-CSP-7 reconciliation; (2) `EntityPositionUpdate` (U-U position packet, CR-NET-7.7) — other entities' positions; 35-entity delivery cap (PA-P7-05) drives CR-CSP-18. No `NetworkVariable<Vector3>` is used for any entity position (NGO built-in smoothing would double-interpolate).

**Navigation/Pathfinding (navigation-pathfinding.md)**: No `INavigationProvider` calls on client; NavMesh not loaded in client process (OQ-MOV-4 resolved). Corner-cutting artifact from linear interpolation of NavMesh paths is an accepted visual trade-off at MVP.

## Formulas

**F-CSP-1 — Self-prediction position update (applied per render frame)**

```
predicted_position(n) = predicted_position(n−1) + normalize(inputDir) × v_eff × dt_clamped
```

| Variable | Type | Source | Range |
|----------|------|--------|-------|
| `predicted_position(n−1)` | Vector2 (XZ) | Previous frame result | Zone bounds |
| `inputDir` | Vector2 (XZ, normalized) | Touch input sample for current server tick | magnitude ∈ {0, 1} |
| `v_eff` | float (units/second) | Last authoritative stat snapshot | [0, 20.0] (see Movement System TK-MOV-1) |
| `dt_clamped` | float (seconds) | `min(actualFrameDelta, 0.05)` | [0, 0.05] |

*This is an exact replication of Movement System F-MOV-1, applied per render frame with `dt_clamped` instead of the server's fixed 50ms dt.*

**Example** (base speed, 60fps): v_eff = 6.0, inputDir = (1, 0), dt_clamped = 0.01667s → `predicted_position += 0.10 units/frame` → 6.0 u/s total ✓

---

**F-CSP-2 — Entity interpolation time parameter**

```
t = (render_time − S0.serverTime) / (S1.serverTime − S0.serverTime)
render_time = NetworkManager.ServerTime.Time − (INTERP_DELAY_MS × 0.001f)
```

> `NetworkManager.ServerTime.Time` is the float-seconds form of the NGO server clock (ADR-004). `INTERP_DELAY_MS` is an integer millisecond value; multiplying by `0.001f` converts it to seconds. Using raw `INTERP_DELAY_MS` (= 75) without conversion would subtract 75 seconds, not 75 milliseconds — a 1000× error.

| Variable | Type | Source | Range |
|----------|------|--------|-------|
| `render_time` | float (s) | `NetworkManager.ServerTime.Time − (INTERP_DELAY_MS × 0.001f)` | current server time − 0.075s |
| `S0.serverTime` | float (s) | Snapshot S0 server tick timestamp | render_time − 0.05 to render_time |
| `S1.serverTime` | float (s) | Snapshot S1 (one tick after S0) | `S0.serverTime + 0.05` |
| `t` | float | — | [0, 1] |

**Example** (normal case): Server time = 10.000s → render_time = 9.925s. S0 at 9.90s, S1 at 9.95s → t = (9.925 − 9.90) / 0.05 = **0.5**. Entity renders at midpoint between S0 and S1 ✓

---

**F-CSP-3 — Reconciliation error**

```
error = serverPos_T − ring[T].predictedPosition
```

| Variable | Type | Source | Normal range |
|----------|------|--------|-------------|
| `serverPos_T` | Vector2 (XZ) | `SelfPositionUpdate` (R-U batch), local player, tick T | Zone bounds |
| `ring[T].predictedPosition` | Vector2 (XZ) | Input history buffer, index `T & 0x1F` | Zone bounds |
| `error` | Vector2 (XZ) | — | [−0.5, 0.5] per axis under normal conditions |

Decision thresholds:
```
|error| < 0.10 (RECONCILE_THRESHOLD_UNITS)         → no action
|error| ∈ [0.10, 2.0)                              → smooth correction (CR-CSP-8)
|error| ≥ 2.0 (RECONCILE_SNAP_THRESHOLD_UNITS)     → immediate snap (CR-CSP-10)
```

---

**F-CSP-4 — Correction speed and blend duration**

```
correction_offset(n) = MoveTowards(correction_offset(n−1), Vector2.zero, MAX_CORRECTION_SPEED × dt_clamped)
MAX_CORRECTION_SPEED = 1.5 × v_eff                    (units/second)
correctionFrames ≈ |error| / (MAX_CORRECTION_SPEED × dt_clamped)
```

`MoveTowards` advances `correction_offset` toward zero by at most `MAX_CORRECTION_SPEED × dt_clamped` per frame — equivalent to `Unity.Vector2.MoveTowards`. The offset reaches zero (blend complete) after `correctionFrames` frames.

| Error magnitude | Duration at base speed (v_eff = 6 u/s) | Visual result |
|-----------------|----------------------------------------|---------------|
| 0.10 u | < 1 frame (< 12ms) | Invisible |
| 0.30 u | ~2 frames (33ms) | Imperceptible |
| 1.50 u | ~10 frames (167ms) | Slight drift |
| ≥ 2.0 u | Immediate snap (CR-CSP-10 applies) | — |

---

**F-CSP-5 — Ring buffer write index**

```
writeIndex = tickNumber & 0x1F     // equivalent to tickNumber mod 32
```

Power-of-2 size enables bitmask indexing, avoiding integer division in the per-tick hot path. Read at reconciliation: `ring[serverTickT & 0x1F].predictedPosition`.

## Edge Cases

**EC-CSP-1 — Zone join cold-start**
On zone join, the client receives a `ZoneStateSnapshot` (EC-MOV-15) containing the local player's initial authoritative position. `predicted_position` is initialized to this value before prediction begins. The ring buffer is zeroed; no reconciliation attempts occur until the first server tick completes. Entity interpolation for other entities begins with 0 snapshots per entity (CR-CSP-20 applies).

**EC-CSP-2 — Zone transition (teleport/new zone)**
On zone transition, prediction is suspended immediately:
1. `predicted_position` is snapped to the new zone entry position received from the server (treated as a Prohibited-class snap)
2. Ring buffer is cleared
3. Entity interpolation buffers for all prior-zone entities are discarded
4. Prediction resumes after the first complete server tick in the new zone

**EC-CSP-3 — Network disconnect**
During network interruption, self-prediction continues running; entity interpolation exhausts extrapolation budgets and all non-local entities enter HELD state. On reconnect, the server position may have diverged by multiple seconds of travel; reconciliation error will likely exceed `RECONCILE_SNAP_THRESHOLD_UNITS` → snap correction applies.

**EC-CSP-4 — Clock desync**
This system assumes a healthy NGO clock (`NetworkManager.ServerTime`). Severe desync events (> 3 tick offset, as determined by NGO internals) are treated as reconnect events: clear the ring buffer and snap to the next received server position.

**EC-CSP-5 — v_eff changes mid-prediction (buff or terrain change)**
When a speed modifier arrives from the server, `v_eff` for self-prediction updates immediately on the next render frame. The server applied the modifier ~75ms earlier; the lag produces a one-to-two-tick divergence in predicted position that is corrected by the next reconciliation. This is expected and non-zero.

**EC-CSP-6 — iOS app backgrounding**
On foreground return, `actualFrameDelta` for the first frame is clamped by CR-CSP-4 (no prediction overrun). If background duration exceeded 32 ticks (1.6s), the ring buffer contains entirely stale data; the first server update post-resume will likely trigger a snap correction (CR-CSP-10). Entity interpolation buffers are stale; all non-local entities hold or cold-start for 75ms after resume.

**EC-CSP-7 — Persistent wire cap starvation (zone at capacity)**
When the zone has 50–60 entities, the wire protocol cap (35 positions/tick) permanently leaves 15–25 entities without updates each tick. Those entities cycle between EXTRAPOLATING and HELD on a per-tick basis (CR-CSP-17/18). Visual result: a subset of distant entities exhibit a subtle 20 Hz jitter. This is an accepted trade-off of the wire budget design (PA-P7-05). Lowest-priority entities (highest EntityID or lowest proximity rank) are dropped first, keeping nearby entities smooth.

**EC-CSP-8 — Local player character death**
On death, the server sets the Prohibited flag. Self-prediction suspends per CR-CSP-12 and `predicted_position` snaps to the server-authoritative death position. Ring buffer is not cleared; prediction resumes from the respawn position on the next cycle. Entity interpolation for other entities is unaffected.

**EC-CSP-9 — Sudden latency spike (50ms → 500ms RTT)**
During a spike, inputs with RTT > 300ms one-way (6+ ticks) are discarded by F-MOV-7 (STALE_TICK_TOLERANCE = 3). Server position diverges from client prediction. When connectivity stabilizes, a large accumulated error triggers reconciliation — likely a snap if error ≥ 2.0 units. Entity interpolation runs EXTRAPOLATING/HELD during the spike and resumes normally on packet recovery. At the 150ms RTT baseline, inputs arrive within tolerance; this case only fires on genuine latency spikes (OQ-CSP-1 resolved 2026-06-15).

**EC-CSP-10 — inputDir = zero (no input)**
When touch is released, `normalize(Vector2.zero)` is undefined. Self-prediction applies zero displacement, writing `inputDir = (0, 0)` and the unchanged `predictedPosition` to the ring buffer. This matches server behavior for zero-input ticks exactly.

## Dependencies

**Upstream dependencies:**

| Dependency | GDD Status | Contract Used |
|-----------|-----------|---------------|
| Movement System | Approved | F-MOV-1 (replicated client-side), CR-MOV-3 (camera), CR-MOV-6 (server position authority), CR-MOV-8 (Prohibited flag), CR-MOV-9 (auto-face), AC-MOV-14 (input send rate), EC-MOV-15 (ZoneStateSnapshot), TK-MOV-1 (v_eff range) |
| Networking Core | Approved | CR-NET-1 (server authority), CR-NET-1.1 (no prediction for consequential outcomes), CR-NET-2 (authoritative game state), TICK_RATE_HZ = 20 |
| Wire Protocol | Approved | CR-NET-7.2 (EntityState encoding), CR-NET-7.7 (tick batch delivery), PA-P7-05 (35-entity position cap) |
| Navigation/Pathfinding | Approved | OQ-MOV-4 resolved: client does not load NavMesh |

**Downstream dependents:**

| Dependent | Relationship |
|-----------|-------------|
| Movement System | OQ-MOV-4 resolved in this GDD; movement-system.md open question is now closed — update that doc to reference CR-CSP-14 and CR-CSP-2 |
| Hit Detection | Must confirm it uses server-committed position (CR-MOV-6), not client predicted position (CR-CSP-14) |

## Tuning Knobs

| Knob | Default | Safe Range | Gameplay Effect |
|------|---------|------------|----------------|
| `INPUT_HISTORY_SIZE` | 32 | [8, 64] | Number of server ticks of input history retained for reconciliation. 32 covers 1.6s (1600ms) at 20Hz. Values below 8 miss reconciliation matches at ≥400ms RTT. Must remain a power of 2 (bitmask indexing). |
| `INTERP_DELAY_MS` | 75 | [50, 150] | Milliseconds other entities lag behind server time. Lower = more responsive but higher jitter risk under packet loss. Values above 150ms make delays perceptible in fast exchanges. Buffer constraint: `INTERP_DELAY_MS` must satisfy `ceil(INTERP_DELAY_MS / 50) ≤ 3` (delay spans at most 3 tick-intervals), which the 4-snapshot buffer (CR-CSP-19) satisfies up to the 150ms cap. Raising the cap above 150ms requires increasing the buffer. |
| `RECONCILE_THRESHOLD_UNITS` | 0.10 | [0.02, 0.30] | Minimum position error (world units) to trigger smooth correction. Wire encoding quantizes positions to 0.01 units per axis (F-MOV-6); diagonal noise reaches ~0.014 units. Below 0.02: encoding noise triggers corrections every frame on diagonal movement. Above 0.30: visible rubber-banding accumulates before correction fires. |
| `RECONCILE_SNAP_THRESHOLD_UNITS` | 2.0 | [1.0, 5.0] | Error above which correction snaps immediately rather than lerping. Should be at least 10× `RECONCILE_THRESHOLD_UNITS`. Below 1.0: snaps on normal high-latency connections. Above 5.0: boundary violations appear as brief wall-walking before correction. |
| `MAX_CORRECTION_SPEED_MULTIPLIER` | 1.5 | [1.0, 3.0] | Multiplier on `v_eff` that caps correction velocity (F-CSP-4). Lower: invisible but may stack if corrections arrive faster than they resolve. Higher: corrections complete faster but appear as rubber-band at large deltas. |
| `MAX_EXTRAPOLATION_MS` | 50 | [33, 100] | How long (ms) entity interpolation extrapolates before freezing on packet loss or wire cap starvation. 50ms = one server tick. Values above 100ms cause entities to visually pass through walls before freeze. |

## Visual/Audio Requirements

Client-Side Prediction has no audio output of its own.

**Reconciliation snap masking (CR-CSP-10)**: When `|error| ≥ RECONCILE_SNAP_THRESHOLD_UNITS`, the character teleports to the server position. This pop may be player-visible. Art direction should supply a brief visual mask — options include a 1-frame screen flash, a camera shake, or a VFX warp burst at the character's feet. The GDD requires only that the implementation exposes a hook event (`OnReconciliationSnap`) that art/VFX can subscribe to.

**Entity cold-start snap (CR-CSP-20)**: The snap from invisible to snapshot[0] position should be masked by the entity's spawn VFX (owned by the entity spawn system, not CSP).

**No audio**: Reconciliation corrections and interpolation are inaudible by design. Audible cues would make the system's latency visible to the player, contrary to the Player Fantasy.

## UI Requirements

None. Client-Side Prediction is pure infrastructure with no player-facing UI. Debug visualization (predicted position gizmo, correction offset vector, interpolation delay indicator) is a developer-mode tool only — not part of the shipped game.

## Acceptance Criteria

**AC-CSP-1 — Self-prediction is responsive within one render frame**
GIVEN a simulated one-way latency of 200ms, WHEN the player touches the screen and holds a direction, THEN the local character begins moving on the next render frame (< 17ms) without waiting for a server response. Pass: character position changes on frame N+1 of input. Fail: character remains stationary until a server packet arrives.

**AC-CSP-2 — Reconciliation threshold suppresses noise**
GIVEN a simulated server correction of 0.09 units injected via the test harness (below `RECONCILE_THRESHOLD_UNITS` = 0.10), WHEN the correction is received, THEN the local character's rendered position does not change in the 10 render frames following receipt. Pass: maximum position delta across all 10 post-correction frames = 0.0 units. Fail: any rendered position shift occurs in those frames.

**AC-CSP-3 — Smooth correction does not produce visible pop**
GIVEN a simulated server correction of 0.30 units (above threshold, below snap threshold), WHEN the correction arrives, THEN the character drifts to the correct position over multiple frames with no single-frame position jump exceeding `MAX_CORRECTION_SPEED × dt`. Pass: per-frame delta within cap. Fail: any single frame exceeds the cap.

**AC-CSP-4 — Large-error correction snaps**
GIVEN a simulated server correction of 2.0+ units, WHEN the correction arrives, THEN the character snaps to the server position in exactly one frame and `OnReconciliationSnap` fires. Pass: frame N+1 shows server position; event fires. Fail: lerp applied.

**AC-CSP-5 — Prohibited flag suspends prediction immediately**
GIVEN the server sends a Prohibited flag in the EntityState batch, WHEN the batch is processed, THEN self-prediction halts on the same frame, `predicted_position` snaps to server position, and the character does not move until the flag is cleared. Pass: no additional displacement after the flag frame. Fail: character continues moving.

**AC-CSP-6 — Entity interpolation renders at 60fps (no 20Hz stutter)**
GIVEN 40+ entities active in the zone, WHEN observing any non-local entity for 5 seconds, THEN the entity's rendered position updates every render frame, not only on 20Hz boundaries. Pass: profiler shows continuous per-frame position changes. Fail: position steps discretely at 50ms intervals.

**AC-CSP-7 — Entity interpolation delay is 75ms**
GIVEN a known entity at position B at server time 9.950s and position A at server time 10.000s, WHEN the client's server clock reads 10.050s, THEN render_time = 10.050s − 0.075s = 9.975s; S0 = snapshot at 9.950s (position B), S1 = snapshot at 10.000s (position A); t = (9.975 − 9.950) / (10.000 − 9.950) = 0.5; the entity's rendered position = Lerp(B, A, 0.5). Pass: rendered position matches Lerp(B, A, 0.5); entity is not at position A (server time 10.000s) or at the live server position (10.050s value). Fail: entity rendered at current server clock position or with no interpolation delay.

**AC-CSP-8 — Packet loss / stale entity freezes gracefully**
GIVEN a simulated 100ms packet gap for a single entity, WHEN 50ms of the gap has elapsed, THEN the entity's rendered position is identical to its position at the 49ms mark (no further movement during the hold window). WHEN the next snapshot arrives, the entity's rendered position changes by ≤ 0.1 units from its held position (no visible snap). Pass: position delta between 50ms and 100ms marks = 0.0 units; position delta at resume ≤ 0.1 units. Fail: position changes during the hold window, or delta ≥ 0.5 units at resume.

**AC-CSP-9 — No managed heap allocations in the CSP hot path**
GIVEN the game running in a zone with 40+ entities at 60fps, WHEN the Unity Profiler records 300 consecutive frames with GC Alloc tracking, THEN the CSP interpolation and self-prediction code paths show 0 bytes of GC allocation. Pass: GC column = 0 for CSP. Fail: any non-zero allocation.

**AC-CSP-10 — dt clamp prevents prediction overrun on frame stalls**
GIVEN a simulated frame delta of 500ms, WHEN self-prediction processes that frame, THEN the character advances no more than `v_eff × 0.05s` from its previous position. Pass: position delta ≤ one server tick of travel. Fail: character jumps by more than that.

**AC-CSP-11 — No NavMesh loaded on client**
GIVEN a session running in any zone, WHEN the Unity Memory Profiler captures a snapshot during active gameplay, THEN no NavMesh asset is present in the client process memory. Pass: no NavMesh data allocated. Fail: NavMesh asset present.

**AC-CSP-12 — Ring buffer wraps correctly at 32-tick boundary**
GIVEN 33 consecutive server ticks of continuous movement (forcing one full ring buffer wrap), WHEN reconciliation fires for tick 33, THEN the ring buffer entry read is the one written at tick 33, not the stale entry from tick 1 overwritten in the same slot. Pass: no spurious correction fires at tick 33 from a stale prior-cycle entry; reconciliation uses the correct predicted position from tick 33. Fail: reconciliation reads a ghost position from the previous wrap cycle and fires an unexpected correction.

**AC-CSP-13 — Auto-face divergence self-corrects without snap**
GIVEN an auto-attack cycle fires (CR-MOV-9) while the player is moving in a direction that differs from the auto-face target, WHEN the auto-face override tick is processed and the next server update is received, THEN the position divergence resolves via the smooth correction path (CR-CSP-8) within 3 render frames. Pass: position delta between rendered and server-authoritative position = 0.0 units within 3 frames; `OnReconciliationSnap` does not fire. Fail: divergence persists beyond 3 frames, or a snap event fires for auto-face divergence. *Note for QA test plan: auto-face reconciliation events are expected behavior (CR-CSP-13); do not file as a defect.*

## Open Questions

**OQ-CSP-1 — STALE_TICK_TOLERANCE conflict with 150ms latency target — RESOLVED 2026-06-15**
`STALE_TICK_TOLERANCE` raised from 1 → **3** in Movement System TK-MOV-4 (F-MOV-7, AC-MOV-15, AC-MOV-16 updated). Coverage: 3 ticks × 50ms = 150ms OWL tolerance, exactly matching the 150ms RTT / 75ms OWL target. At the new value, inputs from the 150ms RTT baseline arrive within tolerance in steady state; latency spikes above 300ms one-way still trigger discards and produce the rubber-band described in EC-CSP-9, but this is now a degenerate case rather than the steady-state behavior. Resolution chose 3 over the originally recommended 4 — 3 is the theoretically tight fit for the stated target; adding a tick of jitter headroom trades against a wider server-side stale-input window.

**OQ-CSP-2 — v_eff at first prediction tick (bootstrap) — RESOLVED 2026-06-15**
Verification: `ZoneStateSnapshot` (EntityState player fields) carries position, HP, maxHP, MP, maxMP, level, appearance flags, dead state, and respawn ticks — **no stat data** (no MovementSpeed, no base stats, no buff values). Option C is not viable; EC-MOV-15 covers position snapping only. `SessionReady` pending fields (OQ-NC-SER-2) are HP, MP, level, gold — also no movement stats. **Resolution: Option A** — the client bootstraps `v_eff` from `BASE_MOVE_SPEED` (TK-MOV-3 = 6.0 u/s, compile-time constant). At MVP `ΣFlatEquip = ΣPctEquip = 0` (F-MOV-2 note: no movement-speed gear defined at MVP), so the constant is always correct at zone entry. Any active buff/debuff mismatch resolves within the first `SelfPositionUpdate` (≤50ms, 1 tick), well within `RECONCILE_THRESHOLD_UNITS`. Option B (suppress prediction) rejected — introduces visible input lag at zone entry with no correctness benefit.

**OQ-CSP-3 — OQ-MOV-4 closure propagation (post-approval action)**
This GDD resolves OQ-MOV-4. After approval, `movement-system.md` must be updated to close OQ-MOV-4 with references to CR-CSP-2 and CR-CSP-14. Mandatory post-approval propagation step.
