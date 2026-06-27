# Networking Ghost Session

> **Status**: Approved (Pass 3 lean, 2026-05-14; GD-CGS-2 propagation 2026-05-15)
> **Cluster**: H — Ghost Session (Pillar 1)
> **Extracted from**: networking-session.md Pass 8 blocker extraction
> **Co-authored by**: game-designer (pillar alignment), network-programmer (protocol contract)
> **State primitive**: networking-ghost-character-state.md (HP authority, IsGhost ownership, write-ordering)

---

## 1. Overview

When a player disconnects unexpectedly mid-session, their character does not vanish
instantly. Instead, the server promotes the character to a **Ghost Entity** — a
server-controlled stand-in that persists in the zone for a bounded TTL while the
client attempts to reconnect. The Ghost Entity stops all offensive and movement
actions immediately on disconnect, holds its position, and absorbs incoming damage
normally until the TTL expires or the player reconnects. The disconnecting player's
XP at the moment of disconnect is banked immediately and is never at risk; only
post-disconnect party XP shares (accumulated while `IsGhost = true`) are forfeited
on ghost death or TTL expiry without reconnect. On TTL expiry, the Ghost Entity is
removed cleanly: all aggroed mobs de-target it, any party slot it occupies is
released, and the ghost-period party XP shares are discarded. The party may also
voluntarily dismiss the ghost at any time, releasing the party slot while preserving
the disconnected player's banked XP. This document specifies the complete rules for
Ghost Entity promotion, state, combat interaction, forfeit policy, voluntary
dismissal, and cleanup. It is the authoritative contract for Cluster H of the
networking-session.md Pass 8 blocker extraction and is referenced by
networking-session.md, auto-attack-combat.md, and the party system.

---

## 2. Player Fantasy

The ghost session mechanic exists to protect the **social gravity** pillar of
Project Iron Grind. Players invest real time in party composition and zone
positioning; an instant-removal disconnect would feel like a punishment imposed
by network infrastructure rather than by gameplay. The intended feeling is:

- **For the disconnected player**: "My spot is held. I can reconnect and continue
  without losing progress or forcing my party to restart. My XP is safe — the only
  thing at risk is the party XP I would have shared while disconnected." The TTL
  is long enough to survive a typical mobile network blip, short enough that the
  party does not feel indefinitely blocked by a ghost.
- **For party members**: "The ghost is a vulnerable teammate. We can protect it by
  pulling mobs away, ignore it and fight around it, or dismiss it to free the slot
  — the choice is ours." The ghost is visually distinct (greyed nameplate) and
  clearly non-functional, but it is not neutral: mobs can kill it, and the party
  has agency over how that plays out.
- **For the game world**: Mob AI responds to presence honestly — a ghost is
  targetable but does not fight back, so mobs that aggro a ghost are making a
  choice the server validates, not an error.

This mechanic primarily serves the **Fellowship** aesthetic (MDA) — it preserves
cooperative state across infrastructure interruptions. The secondary aesthetic is
**Submission** — the system removes burden (instant-loss on disconnect) so the
player can trust the infrastructure and stay immersed.

---

## 3. Detailed Rules

### 3.1 Ghost Promotion

**CR-GH-1: Trigger**
A player session transitions to Ghost state when:
- The server receives no HEARTBEAT message from the client for
  `HEARTBEAT_TIMEOUT_SECONDS` consecutive seconds (see networking-session.md
  CR-NET-6.1), AND
- The session state machine is in `Connected` state at the time of timeout.

Sessions in `Connecting` or `Disconnected_SessionExpired` state MUST NOT
produce a Ghost Entity on timeout.

**CR-GH-2: Promotion sequence**
On Ghost promotion, the server MUST execute the following in order:
1. Transition session state from `Connected` to `Disconnected_SessionActive`.
   Take the pre-disconnect character state snapshot (HP, position, inventory,
   XP) per `networking-ghost-character-state.md` CGS-3.
2. Emit a `GhostPromotionEvent` (R-OD, S→ALL zone) to all clients in the same
   zone instance.
3. Freeze the character entity: clear all queued movement commands and queued
   attack commands on the server. The character MUST NOT move or attack for the
   remainder of the ghost period.
4. Broadcast `IsGhost = true` on the character's next zone sync tick (within one
   `ZONE_TICK_MS` interval), per `networking-ghost-character-state.md` CGS-2.
5. Start the `GHOST_COMBAT_TTL` countdown timer.

**CR-GH-3: IsGhost flag semantics**
`IsGhost` is a boolean field on the character's zone-visible state:
- When `IsGhost = true`, the character is frozen — it generates no XP or loot
  from its own actions (it does not attack, so it cannot contribute to kills),
  and cannot be selected as a valid auto-attack target by other **players**.
  The ghost still receives post-disconnect party XP shares as a passive member
  (see CR-GH-8.1). Enemy mobs MAY continue targeting and attacking the ghost
  (see CR-GH-7).
- `IsGhost` is broadcast to all clients in the zone via the standard zone sync
  message. Clients receiving `IsGhost = true` MUST visually distinguish the
  character (greyed-out nameplate or equivalent visual indicator — art direction
  deferred to UX/art director).
- `IsGhost = false` is the default state. The field is omitted from sync
  messages when false to reduce message size.

### 3.2 Ghost State Constraints

**CR-GH-4: No offensive action**
While `IsGhost = true`, the server MUST reject all client-originated action
commands for that session (movement, attack, skill activation, item use). If
a reconnect occurs and the client sends buffered commands from the disconnected
period, the server MUST discard them without processing.

**CR-GH-5: Damage reception**
A Ghost Entity receives incoming damage normally. Damage events targeting a
ghost are processed by the standard damage pipeline (damage-calculation.md).
HP authority during the ghost period is server-only per
`networking-ghost-character-state.md` CGS-1. The ghost's HP can reach zero
during the ghost period.

**CR-GH-6: Death during ghost period**
If a Ghost Entity's HP reaches zero during the ghost period:
1. The server processes death normally per the Death & Respawn GDD (when authored).
   The death pipeline sets HP to respawn value and records
   `wasKilledWhileDisconnected = true` per
   `networking-ghost-character-state.md` CGS-5.
2. Issue `MobDeTargetCommand` to the zone's AI subsystem for every mob whose
   current target is the ghost entity, per CR-GH-7. The AI subsystem MUST
   process this within one AI tick (`MOB_AI_TICK_MS`).
3. The ghost period ends immediately.
4. The session transitions to `Disconnected_SessionExpired` with reason
   `GHOST_DEATH`. Pre-disconnect snapshot HP is persisted (GD-CGS-2: no HP
   penalty on ghost death); respawn position is applied — see CGS-5 for
   write-ordering. `GhostExpiredEvent(reason: GhostDeath)` is emitted to all
   zone clients after persistence is confirmed.
5. Post-disconnect party XP shares accumulated while `IsGhost = true` are
   forfeited (CR-GH-9.1). Pre-disconnect XP banked at disconnect is unaffected.
6. If the player reconnects after ghost death, they enter the respawn flow, not
   the reconnect flow. The client receives
   `ZoneSessionEnded(reason: DisconnectReason.GhostDeath)`.

**CR-GH-7: Mob de-targeting on ghost expiry**
When the `GHOST_COMBAT_TTL` timer expires and the ghost is removed (or when the
ghost dies per CR-GH-6), the server MUST:
1. Send a `MobDeTargetCommand` to the zone's AI subsystem for every mob whose
   current target is the expiring Ghost Entity.
2. The AI subsystem MUST process `MobDeTargetCommand` within one AI tick
   (`MOB_AI_TICK_MS`). After processing, the mob enters its default idle or
   patrol state unless it has another valid target in aggro range.
3. The server MUST NOT allow a mob to continue attacking a position where a
   ghost previously stood after de-targeting is confirmed.

### 3.3 Party Interaction

**CR-GH-8: Party slot retention**
A Ghost Entity retains its party slot for the full ghost period. Party members
see the character in the party UI with a ghost indicator. The party is not
dissolved and no party leadership transfer is triggered during the ghost period.

If the `GHOST_COMBAT_TTL` expires without reconnect, the character is removed
from the party at ghost expiry, not at disconnect. This gives the player the full
TTL window to reconnect without losing party membership.

**CR-GH-8.1: Party XP sharing during ghost period**
XP operates on two distinct pools during the ghost period:

- **Pre-disconnect XP** (banked at disconnect): The character's XP total at the
  moment of the `Connected → Disconnected_SessionActive` transition is captured
  in the pre-disconnect snapshot (CGS-3 in `networking-ghost-character-state.md`).
  This XP is **never forfeited** — it is safe regardless of what happens during
  the ghost period.
- **Post-disconnect party XP shares** (accumulated while `IsGhost = true`): The
  ghost character continues to receive party XP awards as a passive member. These
  shares accumulate in a separate in-memory pool on the server. This pool is the
  **only forfeitable portion** — it is discarded on ghost death (CR-GH-9.1) or
  TTL expiry without reconnect (CR-GH-9), and written to persistence on successful
  reconnect (CR-GH-9.2).

### 3.4 Forfeit Policy

**CR-GH-9: No rewards on expiry**
If the `GHOST_COMBAT_TTL` timer expires without the player reconnecting, the
server MUST:
1. Forfeit the post-disconnect party XP share pool accumulated while
   `IsGhost = true` (CR-GH-8.1) — it is discarded and never written to
   character persistence.
2. Pre-disconnect XP (captured in the CGS-3 snapshot) is unaffected — it will
   be written to persistence during cleanup (CR-GH-10 step 3).
3. Award zero loot drops attributable to the ghost period (any loot rolls
   performed while `IsGhost = true` are discarded).
4. Begin ghost cleanup (CR-GH-10). The session transition to
   `Disconnected_SessionExpired` with reason `GHOST_TTL_EXPIRED` occurs within
   the cleanup sequence (CR-GH-10 step 6) — do not perform it before calling
   CR-GH-10.

**CR-GH-9.1: No rewards on ghost death**
If the ghost dies before TTL expiry (CR-GH-6), the post-disconnect party XP
share pool is forfeited and discarded. Pre-disconnect XP (CGS-3 snapshot) is
unaffected — it is persisted via CGS-5 (pre-disconnect snapshot HP preserved;
respawn position applied). Zero loot drops for the ghost period.

**CR-GH-9.2: Full rewards on successful reconnect**
If the player reconnects before TTL expiry:
1. Session transitions from `Reconnecting` to `Connected`.
2. `IsGhost` is set to `false` and broadcast on the next zone tick
   (CGS-2 in `networking-ghost-character-state.md`).
3. The post-disconnect party XP share pool accumulated while `IsGhost = true`
   is written to character persistence and added to the character's total XP.
   The pre-disconnect XP (CGS-3 snapshot) is also retained. Total XP on
   reconnect = pre-disconnect XP + post-disconnect party shares.
4. Any loot that dropped to the ghost's personal loot channel during the ghost
   period (if applicable per loot table rules) is retained.
5. The `GHOST_COMBAT_TTL` timer is cancelled.

### 3.5 Ghost Cleanup

**CR-GH-10: Cleanup sequence on TTL expiry**
When `GHOST_COMBAT_TTL` expires without reconnect, the server MUST execute
the following in order:
1. Issue `MobDeTargetCommand` for all mobs targeting the ghost (CR-GH-7).
2. Wait one AI tick (`MOB_AI_TICK_MS`) for de-targeting to complete.
3. Persist the pre-disconnect snapshot (taken at CR-GH-2 step 1, per
   `networking-ghost-character-state.md` CGS-4) to external persistence.
   Persistence MUST be confirmed before proceeding. This is the commit step
   of the commit-before-broadcast sequence (networking-core.md CR-NET-5).
4. Remove the ghost entity from the zone instance state.
5. Release the party slot (CR-GH-8).
6. Transition session state to `Disconnected_SessionExpired` with reason
   `GHOST_TTL_EXPIRED`.
7. Emit `GhostExpiredEvent` (R-OD, S→ALL zone) with a `reason` field set by
   the caller (`GHOST_TTL_EXPIRED` for timer expiry; `GHOST_DISMISSED` for
   voluntary dismissal — see CR-GH-12). Broadcast AFTER session close to
   ensure clients do not receive a ghost event for an already-closed session.

For voluntary dismissal cleanup, see CR-GH-12.

**CR-GH-10.1: Persistence snapshot timing**
The character state persisted at ghost cleanup TTL expiry (step 3 above) is the
pre-disconnect snapshot taken at the `Connected → Disconnected_SessionActive`
transition (CGS-3 in `networking-ghost-character-state.md`). Ghost-period HP
damage is NOT persisted — the character resumes at their pre-disconnect HP.
This prevents punitive HP loss from ghost-period attacks that the player could
not respond to. Ghost death also preserves pre-disconnect snapshot HP (GD-CGS-2:
no HP penalty on ghost death) — only respawn position is applied. See CGS-5.

**CR-GH-11: Zone crash during ghost period**
If the zone instance crashes while a ghost entity is present:
1. The zone crash handler MUST enumerate all sessions in `Disconnected_SessionActive`
   or `Reconnecting` state before terminating the instance.
2. For each such session, the crash handler writes the pre-disconnect character
   snapshot to persistence (equivalent to CR-GH-10 step 3). If a snapshot write
   is already in progress, the idempotency guarantee of CGS-3 (keyed on
   characterId + disconnectTickNumber) prevents double-write.
3. Sessions are transitioned to `Disconnected_SessionExpired` with reason
   `ZONE_CRASH`.
4. Ghost cleanup events (GhostExpiredEvent, MobDeTargetCommand) are NOT emitted
   — the zone instance is gone.

### 3.6 Voluntary Ghost Dismissal

**CR-GH-12: Voluntary dismissal**
Any party member (including the party leader) may trigger voluntary ghost
dismissal at any time during the ghost period. On receiving a
`GhostDismissRequest` (C→S, R-OD) from an authenticated party member, the
server MUST:
1. Validate that the requesting session is a valid party member of the ghost's
   party and that the ghost session is in `Disconnected_SessionActive` state.
2. Cancel the `GHOST_COMBAT_TTL` timer to prevent a concurrent TTL expiry
   from racing the dismissal cleanup.
3. Execute the cleanup sequence (CR-GH-10 steps 1–7), passing reason
   `GHOST_DISMISSED` to both step 6 (session close reason) and step 7
   (`GhostExpiredEvent` reason field). This is the only caller-specified
   substitution needed — CR-GH-10 is otherwise identical for dismissal
   and timer expiry paths.

**CR-GH-12.1: Dismissal reward policy**
Voluntary dismissal applies the same forfeit policy as TTL expiry (CR-GH-9):
post-disconnect party XP shares accumulated while `IsGhost = true` are
forfeited; pre-disconnect XP (CGS-3 snapshot) is persisted and unaffected. The
disconnected player retains their banked XP and can start a new session normally
after dismissal. They are NOT eligible to reconnect to the same session — the
session has transitioned to `Disconnected_SessionExpired`.

---

## 4. Formulas

### F-GH-1: Ghost TTL window

```
GHOST_COMBAT_TTL_S = GHOST_COMBAT_TTL_MIN_S + k × OWL_P95_S
```

Where:
- `GHOST_COMBAT_TTL_MIN_S` = baseline TTL in seconds (default: 30s). Range: [15, 60].
- `k` = OWL scaling factor (default: 2.0). Range: [0.0, 5.0].
- `OWL_P95_S` = 95th-percentile one-way latency for the zone instance, in seconds.
  Updated every 60 seconds from the OWL measurement system (networking-core.md CR-NET-8).
  If OWL_P95 is unavailable, use `OWL_FALLBACK_S` (default: 0.060s / 60ms).

Example (default parameters, OWL_P95 = 80ms):
```
GHOST_COMBAT_TTL_S = 30 + 2.0 × 0.080 = 30.16s ≈ 30s
```

Example (high-latency zone, OWL_P95 = 200ms):
```
GHOST_COMBAT_TTL_S = 30 + 2.0 × 0.200 = 30.4s ≈ 30s
```

In practice, the OWL scaling term is a minor adjustment at typical OWL values.
The dominant factor is `GHOST_COMBAT_TTL_MIN_S`. The scaling term exists to
give high-latency players marginally more reconnect window without requiring
manual tuning per region.

**Constraint**: `GHOST_COMBAT_TTL_S` MUST be strictly less than
`SESSION_TTL_S` (300s — defined in networking-session.md; see
`design/registry/entities.yaml` SESSION_TTL_SECONDS). If the computed value
meets or exceeds `SESSION_TTL_S`, clamp to `SESSION_TTL_S - 5` (295s).

**Rationale for default 30s**: Mobile network blips that require a full TCP
reconnect and session re-authentication typically resolve within 10–20 seconds
on 4G/LTE. 30 seconds provides margin for app-layer reconnect retry and
re-authentication round trips. Values above 60 seconds cause party members
to wait too long; values below 15 seconds do not cover reconnect retry overhead.

### F-GH-2: Mob de-targeting latency budget

```
DE_TARGET_DEADLINE_MS = ZONE_TICK_MS + 2 × MOB_AI_TICK_MS
```

Where:
- `ZONE_TICK_MS` = zone state sync interval (default: 50ms). Range: [33, 100].
- `MOB_AI_TICK_MS` = AI subsystem tick interval (default: 100ms). Range: [50, 200].

Example (defaults):
```
DE_TARGET_DEADLINE_MS = 50 + 2 × 100 = 250ms
```

The formula accounts for worst-case three-step propagation: one zone tick for
the ghost expiry event to be detected by the AI subsystem, one AI tick for the
`MobDeTargetCommand` to be processed and de-targeting to begin, and one
additional AI tick for the de-target state to be confirmed and broadcast. The
previous formula (`MOB_AI_TICK_MS + ZONE_TICK_MS = 150ms`) understated the
worst-case by omitting the second AI tick.

The server MUST log a warning if any mob remains targeted on the expired ghost
after `DE_TARGET_DEADLINE_MS` has elapsed. This metric is used by QA acceptance
criterion AC-GH-5.

---

## 5. Edge Cases

**EC-GH-1: Double disconnect (ghost during reconnect)**
If a client drops the transport connection during the `Reconnecting` state (e.g.,
the reconnect attempt itself drops), the re-authentication fails and the session
transitions back to `Disconnected_SessionActive` (per ST-NET-1: `Reconnecting →
Disconnected_SessionActive` on re-auth failure). The `GHOST_COMBAT_TTL` timer
continues counting from the original disconnect moment — it is not reset. If the
TTL expires while in `Disconnected_SessionActive`, ghost cleanup proceeds normally.

**EC-GH-2: Ghost HP at zero on reconnect**
If a killing blow and a reconnect acknowledgment arrive in the same server tick,
the server resolves the race via the single-tick priority rule defined in
`networking-ghost-character-state.md` CGS-6: death processing takes priority.
Death is processed first (session → `Disconnected_SessionExpired` with reason
`GHOST_DEATH`), the reconnect acknowledgment is discarded, and the client
receives `ZoneSessionEnded(reason: DisconnectReason.GhostDeath)` and enters
the respawn flow. The
tick loop's single-threaded event dispatch (death queue before reconnect queue)
is the serialization point — no external lock is required.

**EC-GH-3: Ghost promoted during in-flight attack**
If the player disconnects while an auto-attack swing is in-flight (the attack
event has been processed by the server but damage has not yet been resolved),
the in-flight attack completes normally — the server does not roll back
committed attack events. However, no further attacks are queued after promotion.

**EC-GH-4: GHOST_COMBAT_TTL exceeds SESSION_TTL**
Handled by the clamp in F-GH-1. The ghost TTL cannot equal or exceed the session
TTL, ensuring the session state machine always has a clean expiry sequence.
If clamping is triggered, the server MUST log a warning with the unclamped and
clamped values for monitoring.

**EC-GH-5: Zone instance at capacity during ghost period**
If the zone instance reaches its player capacity limit during a ghost period, the
ghost entity DOES count against the capacity limit (it occupies a slot). No new
players may join the instance until the ghost expires or the player reconnects.
The ghost cannot be evicted early to free capacity — TTL is a hard guarantee.

**EC-GH-6: Simultaneous ghost expiry and zone tick**
If the ghost TTL expires in the same server frame as a zone sync tick, the server
MUST process ghost cleanup before generating the zone tick message. Clients MUST
NOT receive a zone tick containing a ghost entity whose expiry has been processed.

**EC-GH-7: Party disbanded during ghost period**
If the party is disbanded by the party leader (or by the last non-ghost member
leaving) while a ghost is present, the ghost's party slot is released immediately
upon disbanding. The ghost TTL continues. The character remains in the zone as a
solo ghost until TTL expiry. XP sharing ends at the moment of party disbanding.

**EC-GH-8: isGhost flag and client-side prediction**
Clients applying client-side prediction MUST NOT predict actions for entities
with `IsGhost = true`. Ghost entities are server-authoritative-only for their
full ghost period. Any locally-predicted ghost actions MUST be discarded when
the authoritative server state arrives.

---

## 6. Dependencies

### What this system requires from others

| System | Contract required |
|--------|------------------|
| **Ghost Character State** (`networking-ghost-character-state.md`) | HP authority, IsGhost flag ownership, pre-disconnect snapshot timing (CGS-3), write-ordering on TTL expiry (CGS-4) and ghost death (CGS-5), death-vs-reconnect race ordering (CGS-6). |
| **Networking Session** (`networking-session.md`) | Session state machine: `Connected`, `Disconnected_SessionActive`, `Reconnecting`, `Disconnected_SessionExpired` states and transition triggers. `HEARTBEAT_TIMEOUT_SECONDS`, `SESSION_TTL_S` (300s) values. CR-NET-6.1 (heartbeat monitoring). CR-NET-6.5 (session expiry sequence). |
| **Networking Core** (`networking-core.md`) | Zone sync tick (`ZONE_TICK_MS`). OWL measurement system (CR-NET-8) for `OWL_P95_S`. CR-NET-5 (commit-before-broadcast) governs CR-GH-10 cleanup ordering. |
| **Networking Channel Contract** (`networking-channel-contract.md`) | Channel assignments: `GhostPromotionEvent` (R-OD, S→ALL zone), `GhostExpiredEvent` (R-OD, S→ALL zone), `EntityHealthUpdate` (R-U, S→RELEVANT). |
| **Auto-Attack Combat** (`auto-attack-combat.md`) | In-flight attack completion semantics (EC-GH-3). The combat system MUST expose a "freeze character" API that ghost promotion calls to clear queued attacks. |
| **Damage Calculation** (`damage-calculation.md`) | Standard damage pipeline must accept ghost entities as valid damage targets. No ghost-specific damage path required. |
| **Death & Respawn** *(not yet authored)* | Death processing contract (CR-GH-6 defers to this GDD). Ghost death must enter the standard death flow. Respawn HP value used in CGS-5 write-ordering. |
| **Party System** *(not yet authored)* | Party slot retention API (CR-GH-8). Party XP sharing must accept ghost characters as passive recipients. Party disbanding must notify the ghost session handler (EC-GH-7). Voluntary dismissal `GhostDismissRequest` API (CR-GH-12). |
| **Zone Instancing** *(not yet authored)* | Zone instance capacity limit contract (EC-GH-5). Zone crash handler (CR-GH-11). Mob AI subsystem tick (`MOB_AI_TICK_MS`). |

### What this system provides to others

| System | Contract provided |
|--------|-----------------|
| **Networking Session** | Ghost promotion is the `Disconnected_SessionActive` sub-state — this document is the full spec for that sub-state's rules. |
| **Auto-Attack Combat** | `IsGhost = true` characters cannot be selected as valid player attack targets. The combat system reads this flag from zone state. |
| **Party System** | Ghost entities retain party slots (CR-GH-8). XP sharing two-pool policy (pre-disconnect banked, post-disconnect forfeitable) defined in CR-GH-8.1. Voluntary dismissal API contract (CR-GH-12). |
| **Enemy AI / Zone Instancing** | `MobDeTargetCommand` interface: issued by the ghost session handler to the AI subsystem on ghost expiry, death, or voluntary dismissal. |
| **Client (all)** | `GhostPromotionEvent` (R-OD, S→ALL) and `GhostExpiredEvent` (R-OD, S→ALL) messages with reason fields: emitted to all zone clients on ghost state transitions. |

---

## 7. Tuning Knobs

All values live in `assets/data/networking/ghost-session-config.json`. No
ghost session values are hardcoded.

| Knob | Category | Default | Safe Range | Effect |
|------|----------|---------|-----------|--------|
| `GHOST_COMBAT_TTL_MIN_S` | Gate | 30 | [15, 60] | Baseline ghost TTL. Lower = faster cleanup, more party disruption; higher = more reconnect window, longer party wait. |
| `k` (OWL scaling factor) | Feel | 2.0 | [0.0, 5.0] | Scales TTL by P95 OWL. At 0.0, TTL is flat; at 5.0, high-latency zones get up to 1s extra. Negligible at typical OWL values. |
| `OWL_FALLBACK_S` | Gate | 0.060 | [0.020, 0.150] | OWL value used when P95 measurement is unavailable. Set to regional median OWL at launch. |
| `MOB_AI_TICK_MS` | Feel | 100 | [50, 200] | AI subsystem tick rate. Lower = faster de-targeting; higher = cheaper server CPU. This is shared with the broader AI system — do not tune in isolation. |
| `ZONE_TICK_MS` | Gate | 50 | [33, 100] | Zone state sync rate. Shared with networking-core.md. |

**Tuning guidance**: `GHOST_COMBAT_TTL_MIN_S` is the primary player-experience
knob. Increase it if playtest data shows players frequently timing out on
reconnect (connection telemetry: reconnect attempts after TTL_EXPIRED events).
Decrease it if party members report waiting too long for ghost cleanup.
Target: < 5% of ghost sessions result in TTL_EXPIRED. If > 5%, raise TTL or
investigate reconnect failure root causes first.

---

## 8. Acceptance Criteria

### Functional criteria

**AC-GH-1: Ghost promotion on heartbeat timeout**
Given a session in `Connected` state,
when the server receives no HEARTBEAT for `HEARTBEAT_TIMEOUT_SECONDS` seconds,
then the session transitions to `Disconnected_SessionActive`, `IsGhost = true`
is broadcast within one `ZONE_TICK_MS` interval, and `GhostPromotionEvent`
(R-OD) is emitted to all zone clients.
Pass condition: `OnSessionStateTransitioned(accountId, Connected, Disconnected_SessionActive, "HeartbeatTimeout")` fires; `OnGhostPromotionEventEmitted(characterId)` fires within one `ZONE_TICK_MS` of the transition tick. Automatable.

**AC-GH-2: Ghost entity does not move or attack**
Given `IsGhost = true`,
when the server processes 10 consecutive zone ticks,
then the ghost character's server-side position and attack queue are unchanged
from the values captured at the moment of disconnect.
Pass condition: `IZoneTestConfigurator.GetEntityPosition(entityId)` returns identical values across all 10 ticks; attack queue depth reported by `OnTickCompleted` hook = 0 for each tick. Automatable.

**AC-GH-3: Ghost receives damage normally**
Given `IsGhost = true` and a mob configured to attack the ghost,
when the mob executes an attack,
then the ghost's HP is reduced by the amount computed by the standard damage
pipeline.
Pass condition: `OnServerDamageEventSerialized(mobEntityId, ghostEntityId, computedDamage)` fires; ghost HP reported on next zone tick = prior HP minus `computedDamage` (within ±1 for integer rounding). Automatable.

**AC-GH-4: Ghost death closes session correctly**
Given `IsGhost = true` and ghost HP = 1,
when an attack deals >= 1 damage to the ghost,
then HP reaches zero, death processing begins, session transitions to
`Disconnected_SessionExpired` with reason `GHOST_DEATH`, pre-disconnect
snapshot HP is persisted (no HP penalty per GD-CGS-2), and post-disconnect
party XP shares = 0 are written to persistence.
Pass condition: `OnSessionStateTransitioned(accountId, Disconnected_SessionActive, Disconnected_SessionExpired, "GhostDeath")` fires; `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath)` fires; persisted HP = pre-disconnect snapshot HP (not respawn HP); persisted position = respawn position; XP delta in persistence = pre-disconnect XP only (post-disconnect pool discarded). Automatable.

**AC-GH-5: Mob de-targeting on ghost expiry**
Given a mob with `IsGhost = true` character as its current target,
when `GHOST_COMBAT_TTL` expires,
then within `DE_TARGET_DEADLINE_MS` = `ZONE_TICK_MS + 2 × MOB_AI_TICK_MS` (250ms default),
the mob's target is cleared and it enters idle or patrol state.
Pass condition: `OnGhostCombatTTLExpired(entityId, disconnectTickNumber, expiryTick)` fires at `expiryTick`; mob target field = null within 250ms (1 zone tick at 50ms + 2 AI ticks at 100ms each, at defaults) of that event. Automatable.

**AC-GH-6: Party slot retained during ghost period**
Given a party of 2+ players and one member transitioning to ghost,
when `IsGhost = true` is active,
then the ghost member remains in the party roster for the full ghost period.
Pass condition: `OnSessionStateTransitioned` shows session in `Disconnected_SessionActive`; `IZoneTestConfigurator.GetCurrentZoneState` shows zone remains `Active` (not `Draining`) if other players are present; party roster query at ghost expiry or reconnect shows member count unchanged from pre-disconnect. Automatable.

**AC-GH-7: No post-disconnect party XP on TTL expiry**
Given `IsGhost = true` with party XP shares accumulating, and `GHOST_COMBAT_TTL`
expiring without reconnect,
when the ghost is cleaned up,
then the character persistence record XP = the pre-disconnect XP snapshot value
(no ghost-period party shares added).
Pass condition: `OnGhostCombatTTLExpired` fires; `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry)` fires; character persistence XP = XP at `disconnectTickNumber` (compare snapshot captured via `OnSessionHandshakeEmitted` on prior login). Automatable.

**AC-GH-8: Post-disconnect party XP retained on successful reconnect**
Given `IsGhost = true` with `N` post-disconnect party XP shares accumulated,
when the player reconnects before `GHOST_COMBAT_TTL` expires,
then `IsGhost` is set to `false` within one `ZONE_TICK_MS`, and the character's
persistent XP = pre-disconnect XP + N.
Pass condition: `OnSessionStateTransitioned(accountId, Reconnecting, Connected, "ReauthSuccess")` fires; `OnSessionHandshakeEmitted` next tick reports `currentXp = preDisconnectXp + N`; observer log contains no `OnGhostCombatTTLExpired` event for this session. Automatable.

**AC-GH-9: Pre-disconnect HP persisted on TTL expiry**
Given a ghost entity that received D damage during the ghost period and then
expired via TTL without reconnect,
when character state is written to persistence at ghost cleanup (CR-GH-10 step 3),
then the persisted HP equals the HP at the moment of disconnect (not the
ghost-period-reduced HP = disconnectHP - D).
Pass condition: `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry)` fires; character persistence HP = HP captured at `Connected → Disconnected_SessionActive` transition. Automatable (compare against value logged at session state transition).

**AC-GH-10: Zone crash does not lose pre-disconnect character state**
Given a ghost entity in a zone configured with `IServerCrashInjector.RegisterCrashAt(CrashStep.AfterGhostCleanupPersistenceWrite)`,
when the crash fires after step 3 of CR-GH-10,
then character persistence record exists after server restart and equals the
pre-disconnect snapshot.
Pass condition: after crash recovery, character persistence record HP = disconnect-moment HP; record present with correct characterId. Automatable via `IServerCrashInjector.AfterGhostCleanupPersistenceWrite` (networking-test-harness.md).

**AC-GH-11: Voluntary dismissal releases party slot and preserves banked XP**
Given a party member sends `GhostDismissRequest` for a ghost in
`Disconnected_SessionActive` state,
when the server processes the request,
then: `GHOST_COMBAT_TTL` timer cancelled; ghost entity removed from zone;
party slot released; `GhostExpiredEvent` (R-OD) emitted with reason
`GHOST_DISMISSED`; character persistence XP = pre-disconnect XP (banked);
session transitions to `Disconnected_SessionExpired` with reason
`GHOST_DISMISSED`.
Pass condition: `OnSessionStateTransitioned(accountId, Disconnected_SessionActive, Disconnected_SessionExpired, "GhostDismissed")` fires; `OnPersistenceWriteCompleted` fires before session close; `GhostExpiredEvent` received with `GHOST_DISMISSED` reason on all zone clients. Automatable.

**AC-GH-12: Pre-disconnect XP never forfeited on ghost death**
Given a character who earned 500 XP before disconnecting, then accrued 100 XP
post-disconnect party shares while `IsGhost = true`, and the ghost dies before
TTL expiry,
when the session closes with reason `GHOST_DEATH`,
then the character persistence record XP = 500 (pre-disconnect, banked), not
500 + 100 (party shares forfeited) and not 0.
Pass condition: `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath)` fires; character XP in persistence record = 500. Automatable.

**AC-GH-13: Server rejects buffered client commands after ghost promotion**
Given a client that sends buffered action commands (movement, attack, skill) upon
reconnecting after a ghost period,
when the server receives those commands while the session is still completing
re-authentication,
then the commands are discarded without processing; no position change, attack,
or skill activation occurs.
Pass condition: `OnSessionStateTransitioned` shows `Reconnecting` → `Connected`; no `OnServerDamageEventSerialized` or `OnServerCycleTimerBroadcastSerialized` attributable to buffered commands fires during or before the `Reconnecting` phase. Automatable via `ITransportFaultInjector` (queue commands before transport reconnect completes).

**AC-GH-14: Double disconnect does not reset TTL**
Given a ghost in `Disconnected_SessionActive` (TTL started at T=0), and a
reconnect attempt at T=15s that fails (re-auth failure),
when the session returns to `Disconnected_SessionActive` at T=20s,
then the TTL timer continues from T=20s (not reset); the session expires at T = `GHOST_COMBAT_TTL_S` from the original T=0, not from T=20s.
Pass condition: `OnGhostCombatTTLExpired` fires at `expiryTick = disconnectTickNumber + ghostCombatTTLTicks` (original disconnect tick); no re-arm of the timer observed in observer log. Automatable.

**AC-GH-15: In-flight attack completes on disconnect**
Given a player whose auto-attack swing is in-flight (attack event committed, damage
not yet resolved) when the heartbeat timeout fires,
when the server processes the disconnect,
then the in-flight attack damage is resolved normally, and no further attacks are
queued after `IsGhost = true` is set.
Pass condition: `OnServerDamageEventSerialized` fires for the in-flight attack within the same tick or the next tick after `OnGhostPromotionEventEmitted`; no subsequent `OnServerDamageEventSerialized` fires for that ghost entity. Automatable.

**AC-GH-16: Ghost counts against zone capacity**
Given a zone at capacity N-1 (one slot remaining), when a player disconnects
and becomes a ghost, and a new player attempts to join,
then the join is rejected with an overflow response (the ghost occupies the
final slot).
Pass condition: `IZoneTestConfigurator.SetZoneCapacity(zoneInstanceId, N)` with N players present (one ghost); new join attempt receives overflow rejection. Automatable.

**AC-GH-17: Ghost expiry processed before zone tick delivery**
Given a ghost whose TTL expires in the same server frame as a zone sync tick,
when both events are processed,
then the zone tick delivered to clients does NOT contain the ghost entity; the
`GhostExpiredEvent` is delivered before or in the same network batch as the tick.
Pass condition: `OnGhostCombatTTLExpired` fires at `expiryTick` T; zone tick delivered to clients at T contains no entry for the expired ghost entity. Automatable via `INetworkTestObserver` packet inspection.

**AC-GH-18: Party XP sharing ends at party disbanding**
Given a ghost entity in `Disconnected_SessionActive` and the party being
disbanded by the party leader,
when the party is disbanded,
then the ghost's post-disconnect party XP share accumulation stops at the
moment of disbanding; no further XP is added to the post-disconnect pool.
Pass condition: record the tick number when the party disband event is processed
(via `OnSessionStateTransitioned` or a future `OnPartyDisbanded` harness hook);
assert that ghost session post-disconnect XP pool delta = 0 for all ticks after
that tick number. Requires `INetworkTestObserver` extension: `OnPartyDisbanded(partyId, tickNumber)`. Automatable once extension is added.

**AC-GH-19: IsGhost field omitted from zone sync when false**
Given a character entity with `IsGhost = false`,
when zone sync tick is serialized,
then the IsGhost field is absent from that entity's zone sync entry.
Pass condition: `INetworkTestObserver` packet inspection of zone tick payload for
non-ghost entities contains no IsGhost field byte; byte count matches expected
non-ghost entity encoding. Automatable.

**AC-GH-20: Zone crash persists all ghost sessions in Disconnected_SessionActive or Reconnecting**
Given a zone with two ghost sessions (one in `Disconnected_SessionActive`, one in
`Reconnecting`) and configured `IServerCrashInjector` to crash before cleanup
completes,
when the crash handler runs,
then both characters' pre-disconnect snapshots are written to persistence before
zone termination.
Pass condition: after crash recovery, both character persistence records exist and
equal their pre-disconnect snapshots; `ZONE_CRASH` close reason in session log
for both sessions. Automatable via `IServerCrashInjector`.

### Experiential criteria (playtest validation)

**AC-GH-EXP-1: Ghost period feels like a hold, not an exploit**
In a party playtest session (3+ players), when one player simulates a disconnect
(kill app), other players should observe a greyed nameplate and verbally confirm
the ghost feels "paused" rather than "active" or "abandoned". Target: 4/5
playtesters report the ghost feels clearly non-functional within 5 seconds of
the disconnect event.

**AC-GH-EXP-2: Voluntary dismissal feels like a clear party choice**
When a party member triggers voluntary dismissal, the dismissing player and
remaining party members should understand the action's effect (ghost removed,
disconnected player unharmed XP-wise) within 5 seconds of the event. Target:
4/5 playtesters can correctly describe what voluntary dismissal does immediately
after using it.

**AC-GH-EXP-3: Reconnect feels seamless**
When a player reconnects within the ghost TTL, they should be able to resume
combat within 3 seconds of reconnect without perceiving the ghost period as
disruptive. Target: 4/5 playtesters report no meaningful interruption to their
session flow after reconnect.

**AC-GH-EXP-4: TTL duration feels appropriate**
After testing with the default 30-second TTL, survey party members: "Did the
wait for ghost cleanup feel too long, too short, or appropriate?" Target: < 20%
report "too long". If > 20%: reduce TTL. If reconnect telemetry shows > 5%
TTL_EXPIRED events: increase TTL. Tune TTL iteratively from this baseline.
