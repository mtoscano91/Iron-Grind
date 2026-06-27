# Networking Session Lifecycle

> **Status**: In Review (Pass 9 lean re-review 2026-05-11 — 5 minor revisions applied)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-11
> **Parent**: networking-core.md

## Overview

Networking Session Lifecycle owns the definition of a player session in Project Iron Grind: how sessions are established, maintained across network interruptions, and eventually terminated. It defines the Player Connection State Machine (ST-NET-1), the Zone Session State Machine (ST-NET-2), the reconnection contract (CR-NET-6), and all session edge cases including ghost-entity behavior when a player disconnects mid-combat.

This document is a sub-document of Networking Core. It depends on the authority model and tick architecture defined in `networking-core.md` and the wire format defined in `networking-wire-protocol.md`.

## Player Fantasy

Players don't see session management. What they feel is its guarantee: the world doesn't forget them.

Closing the app mid-grind on a 4G connection doesn't end the session. Their character keeps fighting for a minute, and when they reopen the app they're right back in the zone — or if something went badly wrong while they were gone, they get a clear notification and respawn. No ambiguity about whether the gold they earned before the drop counted. It didn't (GD-1 forfeit policy) — but the party kill they contributed to was real, and the session itself is intact. When this system fails, players experience it as "my progress rolled back." Its success is invisible; its failure is trust-destroying.

## Detailed Rules

### CR-NET-6 — Reconnection Contract

**CR-NET-6.1** When a client disconnects, the server preserves the player's session state for a TTL of **5 minutes** (`SESSION_TTL_SECONDS`). During this TTL, the server continues processing the player's entity (auto-attack tick, incoming damage, status effect expirations) as if the player were connected.

**CR-NET-6.2** State preserved during the session TTL: zone membership and entity position; HP/MP and all stat values; gold balance and `Version`; active `heldFreePoints`; active respec item reservation (with its own 30-second TTL running independently); inventory and equipment state.

**CR-NET-6.3** State not preserved across session TTL expiry: any active respec item reservation (its 30-second TTL will have expired long before the 5-minute session TTL); any in-progress UI flow.

**Session handshake vs. SessionReady relationship:** `SessionHandshake` carries character state (gold, level, HP, etc.) and is emitted early in the connect/reconnect flow. `SessionReady` is a separate, later signal that gates client rendering and input RPCs — sent only after (1) the handshake is delivered, (2) the zone instance is confirmed, and (3) `ZoneStateSnapshot` delivery has been queued. The client must not begin rendering or sending RPCs until `SessionReady` is received. `SessionHandshake` delivers state; `SessionReady` opens the gate.

**CR-NET-6.4** On reconnect within the TTL, the server:
1. Re-authenticates the session
2. **PendingPurchase reconciliation** (ADR-001 Decision 4 — applied 2026-06-27): Before emitting the session handshake, query all `PendingPurchase` records for this `charId` with `state = GoldDebited`. For each record:
   a. Call `AddGold(charId, record.totalCost, CompensatingRefund)` — credits the refund before any further session activity.
   b. Update the record to `state = Refunded` and delete it.
   c. Log the reconciliation event: `charId`, `itemId`, `quantity`, `totalCost`, `requestId`.
   This step completes before step 3 (handshake emission) so that the gold balance in the handshake already reflects any compensating refunds. No `BuyRequest` or `SellRequest` is accepted until this step is complete.
3. Emits the session handshake — `CharacterID`, current gold balance (post-reconciliation), `GoldSyncEvent.Version`, zone assignment, current HP, current MP, current XP, current level, `heldFreePoints`, `ClassType`; and `wasKilledWhileDisconnected: bool` (true if HP reached 0 while in `Disconnected_SessionActive` — client shows "You were defeated while offline" on reconnect); and the following per-subsystem requestId continuity fields for dedup-safe reconnect (OQ-CUS-1 resolved 2026-06-11):
   - `lastSeenUseItemRequestId: uint` — highest `UseItemRequest.requestId` processed for this character in the current session; `0` if none. Client sets its `UseItemRequest` counter to `lastSeenUseItemRequestId + 1` immediately on reconnect.
   - `lastSeenBuyRequestId: uint` — highest `BuyRequest` or `SellRequest` requestId processed for this character in the current session (shared namespace — NPC Shop Buy/Sell counters share one field); `0` if none. Client sets its NPC Shop counter to `lastSeenBuyRequestId + 1` on reconnect.
   These fields prevent the client from starting a reconnect-session counter at `0` and colliding with SESSION_TTL-scoped dedup cache entries from the pre-disconnect session.
4. Checks for any active respec reservation — if TTL has not expired, re-presents Phase 2 to the client; if expired, calls `ItemReservation.Release()` and notifies "Respec scroll returned to inventory"
5. The client seeds its `cachedVersion` from the handshake — the handshake value is authoritative

Equipment state, active buff list, and full inventory are included in a follow-up `ZoneStateSnapshot` bulk message. Note: complete handshake field list is an open item pending Character Persistence and HP-sync GDDs — see OQ-NC-SER-2.

**Session-stealing policy (B-NP-7):** If a new authenticated connection for the same account arrives while a session is active (`Connected`, `Disconnected_SessionActive`, or `Reconnecting`), the prior session is immediately invalidated: the server transitions it to `Disconnected_SessionExpired`, writes its final character state to persistence, and releases all prior session resources. The new connection proceeds through the normal `Connecting` → `Connected` flow. The prior client receives no notification — its transport connection is closed. This prevents two simultaneous sessions for the same account.

**Zone-entry dual-gate (B-NP-8):** The client evaluates the zone-entry rendering gate on **two events**: `SessionReady` receipt AND `ZoneStateSnapshot` reassembly completion. Whichever arrives second opens the gate. Both conditions must be true before the client renders any zone entities or forwards any input RPCs to game logic. The server sequences `SessionReady` to not be emitted until the snapshot has been fully queued for transmission — but transmission time (especially fragment retransmit on lossy connections) may extend past `SessionReady` arrival at the client, so the client must handle either arrival order.

**Snapshot retransmit limit:** The client may emit `ZoneSnapshotRequest` at most `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` times per zone-entry attempt. If all attempts are exhausted without successful reassembly, the client drops the transport connection and begins a fresh reconnect from `Connecting`. This bounds the zone-entry stall duration on persistently lossy connections to `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS × FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS`.

**CR-NET-6.5** When the 5-minute session TTL expires, the server executes the following sequence in order:
1. **Complete the current Beat boundary.** If the entity is mid-cycle in active combat, allow the current Beat to resolve completely — including all pending damage applications for this tick. No partial-Beat state is ever written to persistence.
2. **Process death if HP reached 0.** If `CurrentHP == 0` at any point during or prior to step 1 (including a killing blow delivered during the final Beat), process death server-side: set respawn position to zone entry point, restore HP to respawn value, set `wasKilledWhileDisconnected = true` in the session record, and apply no death penalties (Pillar 1 — EC-NET-1). Death is processed here, before the persistence write, so the persisted state reflects the post-death position and HP.
3. **Write final character state to persistence.** Includes post-death state if step 2 applied. This write completes atomically before any resource release (commit-before-release invariant, analogous to CR-NET-5 in `networking-core.md`).
4. **Broadcast `PlayerLeftZone` (R-OD)** to all transport-connected clients in the zone.
5. **Remove entity from zone instance and release all session resources:** session slot, ghost session memory record, `ActiveSessionTokens` entry (per CR-TOK-6 in `networking-session-token.md`), zone entity slot.
6. **Any subsequent connection from this account starts at `Connecting`.** No session resume is possible.

**Kill during final Beat (step 1 race):** If the ghost entity's `CurrentHP` reaches 0 during the Beat resolution in step 1, the `KillEvent` (R-OD) is broadcast to all transport-connected zone peers on that same Beat boundary — before TTL cleanup begins. Step 2 then processes death. The kill is never silently suppressed by TTL expiry: it is real, it is broadcast, and it is persisted.

---

### States and Transitions

**ST-NET-1 — Player Connection State Machine**

| State | Description |
|-------|-------------|
| `Connecting` | Client has initiated a connection. Authentication and zone assignment in progress. No game state delivered yet. |
| `Connected` | Session fully established. Client received the session handshake. Server delivering tick batches; client sending input RPCs. |
| `Disconnected_SessionActive` | Client has not sent a packet beyond the heartbeat timeout. 5-minute TTL running. Server continues processing the player's entity. |
| `Reconnecting` | Client re-established the transport connection within the TTL. Server is re-authenticating. |
| `Disconnected_SessionExpired` | TTL elapsed. Final state written to persistence. All session resources released. Any subsequent connection is a new session. |

| From | To | Trigger | Server action |
|------|----|---------|---------------|
| — | `Connecting` | Client initiates transport connection | Allocate pending session slot; begin authentication |
| `Connecting` | `Connected` | Auth succeeds, zone assignment completes | Emit session handshake; load entity into zone; begin tick delivery |
| `Connecting` | `Disconnected_SessionExpired` | Auth fails, zone full, client drops during handshake, or `CONNECTING_TIMEOUT_SECONDS` elapses without auth completion | Release session slot; log failure reason; no TTL started |
| `Connected` | `Disconnected_SessionActive` | Heartbeat timeout — no packet from client for >N seconds (OQ-NET-1) | Freeze client output; begin 5-minute TTL; continue processing entity; reset `_skillUsedThisCycle` to `false` |
| `Connected` | `Disconnected_SessionExpired` | Client sends explicit disconnect | Skip TTL; write final state to persistence immediately; release entity and session |
| `Connected` | `Disconnected_SessionExpired` | New authenticated connection for same account arrives (session-stealing) | Invalidate prior session; write final state; release resources; new connection starts at `Connecting` |
| `Disconnected_SessionActive` | `Reconnecting` | Client re-establishes transport within TTL; session token matches | Stop TTL countdown; begin re-authentication |
| `Disconnected_SessionActive` | `Disconnected_SessionExpired` | 5-minute TTL elapses | Execute expiry sequence per CR-NET-6.5 |
| `Reconnecting` | `Connected` | Re-auth succeeds | Emit full session handshake; resolve respec reservation per CR-NET-6.4; resume tick delivery |
| `Reconnecting` | `Disconnected_SessionActive` | Re-auth fails and TTL has not elapsed | Resume TTL countdown from original start (no TTL reset — see EC-NET-7); log attempt |
| `Reconnecting` | `Disconnected_SessionExpired` | Session TTL elapses while re-authentication is in progress (NP-NEW-2) | Stop re-auth; execute TTL expiry sequence per CR-NET-6.5; no session resume |
| `Reconnecting` | `Disconnected_SessionExpired` | `REAUTH_FAILURE_LIMIT` failed re-auth attempts exhausted within the TTL window (EC-NET-7) | Execute TTL expiry sequence per CR-NET-6.5; log auth failure count |
| `Reconnecting` | `Disconnected_SessionExpired` | New authenticated connection for same account arrives while re-authentication is in progress (session-stealing) | Abort re-auth; invalidate token per CR-TOK-6; write final state to persistence; release session; new connection starts at `Connecting` |

*Notes: While in `Disconnected_SessionActive`, the player's entity remains in the zone and can receive damage, be killed, and auto-attack (subject to `GHOST_COMBAT_TTL_MINUTES` — see EC-NET-1). `Disconnected_SessionExpired` never transitions back to any active state via session resume — post-expiry connections start at `Connecting`.*

---

**ST-NET-2 — Zone Session State Machine**

| State | Description |
|-------|-------------|
| `Empty` | Zone instance exists, zero players present. Tick loop suspended. |
| `Active` | One or more players present (Connected or Disconnected_SessionActive). Tick loop running. |
| `Draining` | No `Connected` players remain; one or more sessions in `Disconnected_SessionActive` still have active TTLs. Tick loop continues. |
| `Closed` | All session TTLs expired. Zone teardown eligible. Resources released. |

| From | To | Trigger | Server action |
|------|----|---------|---------------|
| `Empty` | `Active` | First player enters `Connected` state | Start tick loop; initialize zone-level state |
| `Active` | `Draining` | Last remaining player enters `Disconnected_SessionActive` | Continue tick loop; monitor final session TTL |
| `Active` | `Active` | Any player connects, reconnects, or disconnects while others remain | No zone-level state change |
| `Active` | `Draining` | Last `Connected` player explicitly disconnects (`→ Disconnected_SessionExpired`) while one or more `Disconnected_SessionActive` sessions remain | Continue tick loop; monitor remaining session TTLs — same Draining behavior as heartbeat-timeout path |
| `Active` | `Closed` | Last session of any kind enters `Disconnected_SessionExpired` via explicit disconnect — zero sessions remain | Stop tick loop; write final state to persistence for all sessions; release all instance resources |
| `Draining` | `Active` | A player reconnects (reaches `Connected`) or a new player enters | Cancel Draining; zone remains Active |
| `Draining` | `Draining` | Re-authentication attempt fails for a `Reconnecting` session; player returns to `Disconnected_SessionActive` | No zone-level state change; TTL countdown continues from original start time |
| `Draining` | `Closed` | All remaining session TTLs expire | Stop tick loop; write zone-level state to persistence (see OQ-NET-2); release all instance resources |
| `Closed` | `Empty` | Zone re-allocated for a new session | Reinitialize from template |

*Notes: Zone capacity enforcement (10–50 players) applied at `Empty`→`Active` and on each subsequent join. Full zones reject new joins with an overflow response. Teardown in `Closed` must complete any in-flight Beat resolution before stopping the tick loop.*

**Zone teardown enforcement:** When the zone transitions toward `Closed`, the server emits `ZoneSessionEnded` (R-OD) to all **transport-connected** clients with `gracePeriodSeconds = ZONE_CLOSE_GRACE_PERIOD_SECONDS`. Any client still transport-connected at countdown expiry is **forcibly disconnected**: the server closes the transport connection without waiting for graceful logout. Forcibly disconnected clients enter `Disconnected_SessionExpired` immediately — the zone is closing, so no session resume is possible.

**Ghost sessions during zone forced-close:** Sessions in `Disconnected_SessionActive` have no transport connection and cannot receive `ZoneSessionEnded`. When `ZONE_CLOSE_GRACE_PERIOD_SECONDS` expires, all remaining ghost sessions are immediately transitioned to `Disconnected_SessionExpired` — their 5-minute TTL is aborted. Final character state is written to persistence for all sessions before teardown completes. Ghost-session state at zone-close is treated as normal TTL expiry: EC-NET-1 death rules apply, and `wasKilledWhileDisconnected` is set correctly in the persisted record.

---

## Formulas

**F-NET-4 — Session Memory Overhead per Disconnected Player (B-SD-4 corrected)**

Approximate **maximum** (MVP character state — fully-stocked inventory, all stat fields populated):

| Component | Size | Source |
|-----------|------|--------|
| CharacterStats snapshot (10 base stats × 4 bytes) | 40 bytes | character-stats.md approved schema — 10 base stats at MVP |
| ClassType (1-byte enum) | 1 byte | class-system.md |
| heldFreePoints + Level + XP (3 × 4 bytes) | 12 bytes | leveling-system.md |
| GoldBalance + GoldVersion (2 × 4 bytes) | 8 bytes | currency-system.md |
| Zone position (Vector3 = 3 × 4 bytes) | 12 bytes | — |
| Inventory slot array (up to 30 slots × 12 bytes) | 360 bytes | item-database.md (30 = MVP max slots) |
| Metadata (EntityID, CharacterID, TTL timestamp) | 12 bytes | — |
| Active status effects | TBD | Excluded from MVP calculation — pending Status Effects GDD |
| **Total per session (MVP max)** | **~445 bytes** | Minimum (empty inventory): ~85 bytes |

*Inventory slot: 12 bytes = ItemID (4) + enhancementLevel (1) + slotIndex (1) + padding (2) + quantity (4). A character with zero inventory items holds ~85 bytes. 445 bytes is the upper bound for a fully-stocked character.*

At 50 simultaneous ghost sessions (max zone, all disconnected): `445 × 50 = ~22 KB`. Negligible.

---

**F-NET-5 — Heartbeat Timeout Constraints**

```
// Required invariants — must hold for all deployed configurations:
HEARTBEAT_TIMEOUT_SECONDS < SESSION_TTL_SECONDS
HEARTBEAT_TIMEOUT_SECONDS < GHOST_COMBAT_TTL_MINUTES × 60

// Derived tick values:
HeartbeatTimeout_ticks    = HEARTBEAT_TIMEOUT_SECONDS    × TICK_RATE_HZ  // silent ticks before disconnect
CONNECTING_TIMEOUT_TICKS  = CONNECTING_TIMEOUT_SECONDS   × TICK_RATE_HZ  // ticks before Connecting abandoned

// Maximum safe HEARTBEAT_TIMEOUT_SECONDS given current defaults:
// = min(SESSION_TTL_SECONDS, GHOST_COMBAT_TTL_MINUTES × 60) - 1
// = min(300, 60) - 1 = 59s  (well above the recommended 8–12s range)
```

The second constraint (`< GHOST_COMBAT_TTL_MINUTES × 60`) ensures that the heartbeat timeout fires before the ghost combat TTL expires — guaranteeing the ghost period always starts with some combat TTL remaining.

| Parameter | Value | Notes |
|-----------|-------|-------|
| `SESSION_TTL_SECONDS` | 300 (5 minutes) | CR-NET-6.1 |
| `GHOST_COMBAT_TTL_MINUTES` | 1 (60 seconds) | EC-NET-1; must be > `HEARTBEAT_TIMEOUT_SECONDS` |
| `HEARTBEAT_TIMEOUT_SECONDS` | N — see OQ-NET-1 (recommended 8–12s) | Transition: `Connected` → `Disconnected_SessionActive` |
| `HeartbeatTimeout_ticks` | N × 20 | At N = 10s, a dropped client is detected after 200 ticks of silence |

*Constraint violation: if `HEARTBEAT_TIMEOUT_SECONDS ≥ GHOST_COMBAT_TTL_MINUTES × 60`, the ghost entity disengages before the heartbeat can even detect the disconnect — the entity stands idle with no auto-attack from the moment of disconnect. This is unintentional and must be caught by configuration validation on startup.*

---

## Edge Cases

**EC-NET-1 — Player Killed While Disconnected**

When a player is in `Disconnected_SessionActive`, their entity remains in the zone as a ghost. Ghost entity behavior:

- **`_skillUsedThisCycle` reset on ghost entry (NP-NEW-5):** On the `Connected` → `Disconnected_SessionActive` transition, `_skillUsedThisCycle` is reset to `false` immediately. No input can set it while disconnected, so every Beat fires a plain auto-attack.
- The entity continues auto-attacking its last locked target every Beat until `GHOST_COMBAT_TTL_MINUTES` expires.
- The entity continues receiving damage from active attackers.

**Ghost-period rewards — two-pool policy (GD-GHOST-1, see `networking-ghost-session.md` CR-GH-8.1):**

- **Pre-disconnect XP** (earned before the `Connected → Disconnected_SessionActive` transition): captured in the pre-disconnect state snapshot (CGS-3 in `networking-ghost-character-state.md`) and **never forfeited**. The player retains this XP regardless of what happens during the ghost period.
- **Ghost entity auto-attack XP**: XP from kills attributed to the ghost entity's own auto-attacks is **not credited** to the disconnected player (unchanged from prior policy). Ghost combat still benefits the party, but the disconnected player receives no XP for kills made by their automated ghost.
- **Post-disconnect party XP shares**: XP shared to the ghost as a passive party member (while `IsGhost = true`) accumulates in a separate pool and is **forfeited** if the ghost dies or the ghost TTL expires without reconnect. If the player reconnects before TTL expiry, this pool is retained and added to persistent XP.

Loot drops attributable to the ghost period are forfeited on death or TTL expiry (zero loot for ghost period regardless of reconnect outcome). Full detail in `networking-ghost-session.md` CR-GH-9/9.1/9.2.

**Ghost combat time limit:** After `GHOST_COMBAT_TTL_MINUTES` of `Disconnected_SessionActive` time in combat, the ghost entity disengages — `_cycleTimer` stops advancing and no further Beat events fire. The entity remains in the zone inert until the player reconnects. The 1-minute default covers legitimate mobile reconnect scenarios (typical LTE reconnect < 30s) while limiting the social utility of disconnecting mid-combat.

**Ghost TTL must use tick-based arithmetic, not wall-clock time.** The server records `disconnectTickNumber: uint` at the disconnect moment:

```csharp
uint ghostCombatExpiryTick = disconnectTickNumber + (uint)(GHOST_COMBAT_TTL_MINUTES * 60 * TICK_RATE_HZ);
uint sessionExpiryTick     = disconnectTickNumber + (uint)(SESSION_TTL_SECONDS * TICK_RATE_HZ);
```

**Expiry comparison must use RFC 1982 serial arithmetic** — raw `uint` comparison (`currentTick >= expiryTick`) fails after `uint` wraparound (~6.8 years uptime). Use the `IsTickExpired` helper defined in `networking-wire-protocol.md`:

```csharp
// Returns true when currentTick is at or past expiryTick (handles uint wraparound).
// Equality: true when currentTick == expiryTick (expired exactly now).
static bool IsTickExpired(uint currentTick, uint expiryTick) =>
    (uint)(currentTick - expiryTick) < 0x80000000u;
```

Using `DateTime.UtcNow` is forbidden — not synchronized with the tick loop and creates correctness hazards across process restarts and clock skew.

**isGhost mid-session visual:** The `isGhost` flag is set in `ZoneStateSnapshot` on zone join. When a player goes ghost mid-session, a `GhostPromotionEvent` (R-OD, S→ALL zone) is broadcast to all zone clients (CR-GH-2 step 2 in `networking-ghost-session.md`). Clients receiving `GhostPromotionEvent` update the affected entity's visual state (greyed nameplate or equivalent — art direction deferred to UX/art director). This replaces the prior MVP gap note — the broadcast is now defined and required.

If `CurrentHP` reaches 0:
1. The auto-attack tick processes the kill on the next Beat boundary
2. A `KillEvent` is broadcast to all clients in the zone
3. Death is computed server-side per the ghost-death contract in `networking-ghost-session.md` (EC-GH-1 steps 1–3: kill event broadcast, ghost state cleared, mob retarget fired). Full death penalties are governed by the Death & Respawn GDD (not yet authored — see OQ-NET-3).
4. The player respawns at the zone entry point; state written to persistence immediately
5. On reconnect, `wasKilledWhileDisconnected: bool` is `true` — client shows "You were defeated while offline" notification
6. No death animation plays for the reconnecting player — they reconnect already respawned

**Ghost-mode death penalty exception (Pillar 1 — Earned Power):** When a player's entity dies while in `Disconnected_SessionActive`, any death penalties defined by the Death & Respawn GDD are **not applied**. The entity respawns at the zone entry point with no consequence beyond position change. This is a binding constraint on the Death & Respawn GDD — Pillar 1 requires that permanent negative consequences only apply when the player had agency to avoid them. An auto-attacking ghost entity has no player agency.

---

**EC-NET-3 — Zone Full on Reconnect**

A player in `Disconnected_SessionActive` retains their zone slot for the full session TTL. Zone capacity enforcement counts `Connected` + `Reconnecting` + `Disconnected_SessionActive` sessions toward the zone cap. A zone is never "full" because of ghost sessions from its own players — only new players joining from outside are rejected at max capacity.

*Ghost entity bandwidth cost (known externality):* Ghost entities continue auto-attacking (until `GHOST_COMBAT_TTL_MINUTES` expires) and take damage, so their `EntityHealthUpdate` and `_cycleTimer` values are broadcast normally. Accepted design constraint — ghost sessions are designed to be brief.

---

**EC-NET-4 — Zone Closes While Player is Disconnected**

If the disconnected player's session TTL expires while the zone is `Draining`, the zone transitions to `Closed` and all resources (including the player's session) are released. A subsequent connection is treated as a fresh connection. Any in-progress activity (respec reservation, active combat) is not restored.

---

**EC-NET-5 — RPC Arrives Before Session-Ready**

Any inbound RPC arriving before `SessionReady` has been sent for that session is dropped — not queued, not forwarded to game logic. The session log records the anomaly. Character state is never corrupted because no game logic was invoked.

---

**EC-NET-6 — In-Flight RPC at Disconnect Boundary**

A client in `Connected` state may have sent an RPC (e.g., `AllocateFreePointRequest`) that is in the network buffer when the server detects the heartbeat timeout and transitions the session to `Disconnected_SessionActive`. The transition is the authority boundary:

- If the server processes the RPC before the state machine advances: it is applied normally. The commit-before-broadcast invariant (CR-NET-5 in `networking-core.md`) ensures it is written to persistence before any broadcast.
- If the state machine advances to `Disconnected_SessionActive` or `Disconnected_SessionExpired` before the RPC is dequeued: the RPC is dropped without processing. No partial-application occurs — character state is never left in an intermediate form by a dropped in-flight RPC.

No game-logic RPC is processed in `Disconnected_SessionActive`, `Reconnecting`, or `Disconnected_SessionExpired` state. Game-logic RPCs are requests that modify game state (e.g., `AllocateFreePointRequest`, `UseSkillRequest`, `BeginEnhancementRequest`). Transport-layer and authentication messages used in the reconnect flow (`ConnectionRequest`, `Heartbeat`, `SessionHandshake`) are handled at a lower layer and are not subject to this rule — they are what enable the session to return to `Connected` so game-logic RPCs can resume. The reconnect flow (CR-NET-6.4) fully re-establishes client state from persistence before any game-logic RPCs are accepted.

---

**EC-NET-7 — Rapid Reconnect Attempts**

Each failed re-authentication attempt does not reset the session TTL — it continues from original start time. After `REAUTH_FAILURE_LIMIT` failed attempts within one TTL window, the server transitions immediately to `Disconnected_SessionExpired`. "No TTL extension on failure" is a rule; the 3-attempt threshold is a tuning knob.

---

**EC-NET-9 — Duplicate Enhancement Attempt Request**

The transport layer deduplicates `R-OD` retransmits in the normal case. For cross-session duplicates, the server uses a per-character `LastEnhancementRequestID` field.

The dedup check has **no time window**: if `request.RequestID == character.LastEnhancementRequestID`, the request is rejected unconditionally — regardless of elapsed time. A new legitimate enhancement uses a new `RequestID` incremented by the client; the persisted field is overwritten only when a new enhancement is successfully committed.

**Dedup scope is per-character:** `LastEnhancementRequestID` is keyed on `CharacterID`. Two characters can independently use `requestId=42` without collision. A character who uses `requestId=42` in session N cannot reuse it in session N+1.

`LastEnhancementRequestID` must be **persisted atomically with the enhancement outcome** (CR-NET-5 step 5 in `networking-core.md`). Without persistence, a server crash between write and broadcast would allow a reconnecting client to re-submit the same `RequestID`. The field never resets across sessions.

*The exploit path this closes:* submit RequestID=X; server commits; player disconnects before broadcast; player reconnects; re-submits RequestID=X. `LastEnhancementRequestID = X` in the character record rejects unconditionally.

---

**EC-NET-10 — Zone Process Crash (Ungraceful Shutdown)**

If the zone server process crashes without executing the ST-NET-2 graceful teardown sequence (no `ZoneSessionEnded` broadcast, no ordered `Draining → Closed` transition), recovery behavior is:

1. **Sessions with completed persistence writes before the crash:** Character state is intact at the last write checkpoint. On reconnect, the server re-establishes the session from the persisted record and issues a new session token. The `ActiveSessionTokens` in-process store is cleared by the restart (EC-TOK-4 in `networking-session-token.md`) — reconnecting clients fall through to full auth.
2. **Ghost sessions whose final-state write (CR-NET-6.5 step 3) had not completed:** The most recent successful persistence checkpoint is authoritative. Progress since that write (HP changes, XP accumulation during ghost period) is lost. `wasKilledWhileDisconnected` may not be set correctly if death occurred after the last write.
3. **In-flight enhancements:** The `LastEnhancementRequestID` persistence atomicity guarantee (EC-NET-9) prevents double-enhancement across a crash. Any uncommitted enhancement is rolled back.

Zone crash recovery for replacement instance routing is a Zone Instancing GDD concern. This document's contract: crash = rollback to last persistence checkpoint; no session memory survives the restart.

---

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-core.md` | Parent — authority model, tick architecture, commit-before-broadcast (CR-NET-5) |
| `networking-wire-protocol.md` | Wire format for session handshake, `ZoneStateSnapshot`, `SessionReady`, heartbeat schema, `IsTickExpired` helper; `MobRetargetEvent` schema |
| `networking-session-token.md` | Session token spec — CR-TOK-1–8 govern token generation, validation, and rotation used in all `Reconnecting` state transitions |
| `networking-ghost-session.md` | Ghost session spec — CR-GH-1–6 govern ghost combat behavior, reward attribution, mob retarget contracts; authoritative detail for EC-NET-1 |
| `networking-test-harness.md` | `IServerCrashInjector` required for AC-NC-16; `ITransportFaultInjector` for AC-NC-35 |

**Downstream:** Zone Instancing GDD (zone capacity, zone entry-point coordinates for AC-NC-33a), Death & Respawn GDD (EC-NET-1 full death rules — OQ-NET-3, blocking for implementation), Character Persistence GDD (complete session handshake schema — OQ-NC-SER-2).

---

## Tuning Knobs

| Knob | Default | Safe Range | Gameplay Impact |
|------|---------|------------|-----------------|
| `SESSION_TTL_SECONDS` | 300 (5 min) | [60, 600] | How long the server holds a disconnected player's session. Below 60s: players on flaky connections experience frequent reload cycles. Above 600s: server holds session memory and entity slots for abandoned sessions too long. |
| `HEARTBEAT_TIMEOUT_SECONDS` | TBD — OQ-NET-1 (recommended 8–12) | [3, 30] | Seconds of silence before `Connected` → `Disconnected_SessionActive`. Must satisfy F-NET-5 constraints (strict `<` GHOST_COMBAT_TTL_MINUTES × 60). **Cross-knob note:** At minimum `GHOST_COMBAT_TTL_MINUTES` (0.5 min = 30s), the F-NET-5 strict inequality reduces the effective upper bound to 29s — 30s would violate the constraint. Too low: false positives on normal mobile jitter. Too high: crashed clients occupy the zone as ghost entities longer before the TTL starts. |
| `GHOST_COMBAT_TTL_MINUTES` | 1 | [0.5, min(SESSION_TTL_SECONDS/60, 10)] | Minutes of auto-attack combat a ghost entity continues before disengaging. Upper bound is the lower of `SESSION_TTL_SECONDS/60` and 10 minutes — exceeding `SESSION_TTL_SECONDS/60` produces a combat TTL that outlasts the session (EC-GH-6 in `networking-ghost-session.md`). At 1 minute: ~20 mob hits of ghost combat with no reward to the disconnected player. |
| `CONNECTING_TIMEOUT_SECONDS` | 30 | [10, 120] | Seconds a session may remain in `Connecting` before the server abandons it and releases the session slot. Prevents abandoned auth attempts from holding pending session slots indefinitely. Below 10s: slow auth backends may produce false timeouts. |
| `ZONE_CLOSE_GRACE_PERIOD_SECONDS` | 30 | [10, 120] | Time clients have to react to `ZoneSessionEnded` before being forcibly disconnected. |
| `REAUTH_FAILURE_LIMIT` | 3 | [1, 10] | Maximum failed re-authentication attempts before the server expires the session. Below 2: a single transient failure strands the player. Above 5: extends the window for brute-force session token attempts. |
| `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` | 10 | [5, 30] | Seconds the client waits for all `ZoneStateSnapshot` fragments before emitting `ZoneSnapshotRequest`. Default is 10s for production mobile deployments (4G congestion can delay packets 5–8s at the cellular retransmit layer). Use 5s for development/Wi-Fi environments. Below 5s: false retransmit requests on weak-signal 4G connections. Above 30s: failed zone entries stall too long before self-healing. |
| `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` | 3 | [1, 10] | Maximum `ZoneSnapshotRequest` retransmit attempts per zone-entry before the client abandons and starts a fresh reconnect. Bounds worst-case zone-entry stall to `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS × FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` = 30s at defaults. |

---

## Acceptance Criteria

**AC-NC-10 (Logic)** — Given a connected player with `HEARTBEAT_TIMEOUT_SECONDS = 3` (test config), When the server tick loop advances `HeartbeatTimeout_ticks + 1 = 61` ticks without any inbound packet from this client injected into the server's input queue, Then: `OnSessionStateTransitioned(accountId, Connected, Disconnected_SessionActive, "HeartbeatTimeout")` fires; the entity remains in the zone; no session resources are released. *No wall-clock wait — advance tick counter programmatically. Do not use `Thread.Sleep`.*

**AC-NC-11 (Logic)** — Given a player in `Disconnected_SessionActive` with gold = 1,000g and Level = 15, When the player reconnects while `IsTickExpired(currentTick, sessionExpiryTick)` is false (advance tick counter to `sessionExpiryTick - 100` to place the test inside the TTL window), Then: `OnSessionHandshakeEmitted(characterId, wasKilledWhileDisconnected: false, goldBalance: 1000, level: 15)` fires. *No wall-clock wait — session TTL verified via tick position, not elapsed time.*

**AC-NC-12 (Logic)** — Given a player in `Disconnected_SessionActive`, When the tick counter advances to `sessionExpiryTick` (making `IsTickExpired(currentTick, sessionExpiryTick)` true), Then: `OnPersistenceWriteCompleted(characterId, SessionExpiry)` fires before any resource release; `OnSessionStateTransitioned(accountId, Disconnected_SessionActive, Disconnected_SessionExpired, "TTLExpired")` fires; entity is removed from zone; session resources are released. A subsequent connection from the same account starts at `Connecting`. *No wall-clock wait — advance tick counter to `sessionExpiryTick` programmatically.*

**AC-NC-13** — Given a player who submitted Phase 1 of a respec and then disconnected: (a) When reconnecting within 30 seconds (respec TTL valid), Then Phase 2 is re-presented to the client. (b) When reconnecting after 30 seconds but within 5 minutes (respec TTL expired, session TTL valid), Then the client receives "Respec scroll returned to inventory" and the item is in inventory.

**AC-NC-14** — Given a zone with one connected player who disconnects, When the player enters `Disconnected_SessionActive`, Then the zone transitions to `Draining` (tick loop continues) — not `Closed`.

**AC-NC-16** — Given a test server that crashes immediately after the persistence write completes and before emitting the outcome message (using `IServerCrashInjector` from `networking-test-harness.md`), When the server restarts and the player reconnects, Then the session handshake delivers the post-enhancement item state. The outcome is displayed without replaying the animation.

**AC-NC-23** — Given a newly connected client that has not yet received `SessionReady`, When the client sends a valid `AllocateFreePointRequest`, Then the server drops the RPC without forwarding it to game logic, and logs the anomaly. The character's `heldFreePoints` and stats are unchanged.

**AC-NC-24** — Given a zone with 49 connected players and 1 player in `Disconnected_SessionActive`, When a new player attempts to join, Then the server returns an overflow response — the ghost session counts toward the 50-player cap. The disconnected player's slot is not relinquished until their session TTL expires.

**AC-NC-26** — Given a connected player who sends an explicit disconnect (graceful logout), When the server processes it, Then: (a) the 5-minute session TTL is skipped entirely; (b) final character state is written to persistence immediately; (c) the player's entity is removed from the zone; (d) all other clients receive `PlayerLeftZone` with `disconnectType = graceful`. No `Disconnected_SessionActive` state is entered.

**AC-NC-27** — Given a player who submits enhancement attempt (`RequestID = X`) that is processed successfully, When the same client sends a second request with `RequestID = X` (simulating network retry or reconnect-retransmit), Then the server rejects it as a duplicate — no second enhancement is computed, item state is unchanged. The rejection must occur even when >30 seconds have elapsed (verifies no time window on dedup).

**AC-NC-32 (Logic)** — Given a player who enters `Disconnected_SessionActive` at tick T with `GHOST_COMBAT_TTL_MINUTES = 1` and one mob in combat range, When `currentServerTick >= T + (1 × 60 × 20) = T + 1200`, Then: within one tick boundary (≤50ms), the ghost entity's `_cycleTimer` stops advancing, no further Beat events fire for that entity, and the mob stops targeting the ghost entity. TTL expiry evaluated using tick comparison — not wall-clock time. *Unit-testable by injecting a fixed `disconnectTickNumber` and advancing the server tick counter.*

**AC-NC-33a (Integration — testable now)** — Given a player in `Disconnected_SessionActive` whose entity HP reaches 0 from mob damage, When death is processed server-side, Then: (b) the entity's position is set to the zone entry point and HP is set to the respawn value; (c) on reconnect, `wasKilledWhileDisconnected = true` in the session handshake; the client receives "You were defeated while offline". *Precondition for (b): inject a test zone with a known entry-point coordinate (e.g., `posX=0, posY=0, posZ=0`); assert the ghost entity's server-side position equals that coordinate after death processing.*

**AC-NC-33b (BLOCKED — pending Death & Respawn GDD)** — Given the same scenario, Then: (a) no death penalty defined by the Death & Respawn GDD is applied (XP loss, equipment damage, item drop, etc.); (d) the player's HUD receives no death-penalty notification. *Blocked until Death & Respawn GDD enumerates all penalties.*

**AC-NC-34 (Integration)** — Given a player who submits enhancement (`RequestID = X`) committed to persistence, and `IServerCrashInjector` triggers a crash after write but before broadcast, When the server restarts and the player reconnects, Then: (a) session handshake delivers post-enhancement item state; (b) re-submitted `RequestID = X` is rejected; (c) item state is unchanged by the rejected re-submit. Verified by comparing pre-crash character record against post-restart session handshake and rejection log.

**AC-NC-35 (Integration)** — Given a client joining a zone where `ZoneStateSnapshot` is fragmented into N fragments and `ITransportFaultInjector` (from `networking-test-harness.md`) drops fragment N: (a) When `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` elapses after fragment 1 arrives, the client emits `ZoneSnapshotRequest`; (b) the zone-entry gate remains closed — no entity is rendered and no input RPCs are accepted; (c) when the server re-sends and the client reassembles, the gate opens within 100ms of the final fragment being received. Verified by checking no entity render or RPC occurs before gate opens.

**AC-NC-37 (Logic) — Session-stealing**
Given a player in `Connected` state (token T, character C), When a new authenticated connection arrives for the same account from a second client, Then: (a) `OnSessionInvalidatedBySteal(accountId, characterId)` fires for the prior session; (b) `OnPersistenceWriteCompleted(characterId, SessionSteal)` fires; (c) `OnSessionStateTransitioned(accountId, Connected, Disconnected_SessionExpired, "SessionSteal")` fires; (d) prior transport connection is closed; (e) `OnSessionStateTransitioned(accountId, -, Connecting, "NewConnection")` fires for the new connection. Ordering guarantee: (a)–(d) all complete before (e). *Inject two concurrent connections for the same account in a controlled test tick.*

**AC-NC-38 (Logic) — REAUTH_FAILURE_LIMIT exhausted**
Given a player in `Disconnected_SessionActive` with `REAUTH_FAILURE_LIMIT = 3`, When the client sends 3 reconnect requests with tokens corrupted by `ITransportFaultInjector` (one-byte modification per CR-TOK-4), Then: `OnReAuthAttemptFailed(accountId, 1, 2)`, `OnReAuthAttemptFailed(accountId, 2, 1)`, `OnReAuthAttemptFailed(accountId, 3, 0)` fire in order; immediately after the third, `OnSessionStateTransitioned(accountId, Reconnecting, Disconnected_SessionExpired, "ReauthLimitExceeded")` fires; `OnPersistenceWriteCompleted(characterId, SessionExpiry)` fires. Session TTL does not reset between failures — `sessionExpiryTick` is unchanged throughout. *No tick advance required — limit exhaustion is synchronous.*

**AC-NC-39 (Logic) — Connecting timeout**
Given `CONNECTING_TIMEOUT_SECONDS = 10` (test config), When a client initiates transport connection and the tick counter advances by `CONNECTING_TIMEOUT_TICKS = 10 × 20 = 200` ticks without auth completion (no auth response injected into the server's input queue), Then: `OnSessionStateTransitioned(accountId, Connecting, Disconnected_SessionExpired, "ConnectingTimeout")` fires; the pending session slot is released. `OnPersistenceWriteCompleted` does NOT fire (no session was established). *No wall-clock wait — advance tick counter to `connectInitiatedTick + 200`.*

**AC-NC-40 (Logic) — Snapshot retransmit limit**
Given `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS = 3` and `ITransportFaultInjector.DropSnapshotFragment(N-1)` injected on each of 3 consecutive retransmit cycles, When the 3rd `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` elapses without reassembly, Then: `OnSnapshotRetransmitAttempt(characterId, 1, 3)`, `...(2, 3)`, `...(3, 3)` fired in order; after the 3rd, the client drops the transport connection and begins a fresh reconnect; the zone-entry gate never opened. *Verifies MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS bounds the stall window.*

**AC-NC-41 (Logic) — Active→Closed via explicit disconnect (last session)**
Given a zone with exactly 1 connected player and no `Disconnected_SessionActive` sessions, When the player sends an explicit disconnect, Then: `OnZoneStateTransitioned(zoneId, Active, Closed)` fires — no intermediate `Draining` state is entered; `OnPersistenceWriteCompleted(characterId, ExplicitDisconnect)` fires; `IZoneTestConfigurator.GetCurrentZoneState(zoneId)` returns `Closed` on the same tick boundary. *Verifies the new ST-NET-2 Active→Closed row — no Draining when zero sessions remain.*

**AC-NC-42 (Logic) — In-flight RPC at disconnect boundary**
Given a connected player with `heldFreePoints = 1`, When the server processes the heartbeat timeout on tick T (transitioning to `Disconnected_SessionActive`) while an `AllocateFreePointRequest` is simultaneously present in the server's input queue (inject with test client sending RPC at tick T-1), Then: either (a) the RPC was processed before the state machine advanced — `OnPersistenceWriteCompleted(characterId, ...)` fires with updated stats, `heldFreePoints = 0` in persistence; OR (b) the RPC was dropped without processing — `heldFreePoints = 1` in persistence. In both cases, no intermediate state (e.g., `heldFreePoints = 0` but stat not updated) is present in the persisted record. *Verifies EC-NET-6: transition is the authority boundary; no partial-application.*

---

## Open Questions

**OQ-NET-1 — BLOCKING (implementation) — Heartbeat timeout value**
The `Connected` → `Disconnected_SessionActive` transition fires after N seconds of no packets. Setting too low (2–3s) produces false disconnects on normal mobile jitter; too high (30s) means a crashed client's entity stays in `Connected` state and receives combat targeting too long. Recommended range: 8–12 seconds. The Networking ADR must specify the exact value based on the chosen library's keep-alive mechanism and mobile latency measurement data. Note: the heartbeat message schema (GAP-1) is defined in `networking-wire-protocol.md`.

**OQ-NET-2 — Zone-level persistence on teardown**
The `Draining` → `Closed` transition notes writing any zone-level state that requires persistence. Whether mob respawn timers, chest states, or any per-zone world state survives zone teardown is not defined in any current GDD. If zone state resets on every instance, this is trivial. Blocking for Zone Instancing GDD.

**OQ-NET-3 — Death processing while disconnected**
EC-NET-1 specifies server-side death processing during `Disconnected_SessionActive`. The exact death resolution rules (respawn point, HP restore value, death penalty) are not yet authored. Non-blocking for this GDD's completion; blocking for implementation. Also see OQ-NC-SER-2 for complete session handshake schema dependency.
