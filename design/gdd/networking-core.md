# Networking Core

> **Status**: Approved (Pass 2 lean, 2026-05-14)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-29
> **Implements Pillar**: Pillar 3 — Social Gravity (multiplayer world exists); all pillars require server authority

## Sub-Documents

This document was split after Pass 7. Detailed specifications live in:

| Sub-document | Contents |
|--------------|----------|
| `networking-session.md` | ST-NET-1/2 state machines, CR-NET-6 reconnection contract, session edge cases, ghost-entity policy |
| `networking-wire-protocol.md` | CR-NET-7 serialization rules, all formulas (F-NET-1–3, F-NET-6–7), message schemas, two-path delivery model |
| `networking-test-harness.md` | ITransportFaultInjector, IServerCrashInjector, INetworkTestObserver, release-build stripping, test infrastructure ACs |
| `networking-owl-compensation.md` | F-NET-OWL corrected algorithm, `LastBeatServerTick` data structure, entity slot allocation, tick-window constraint proof |
| `networking-message-criticality.md` | Per-message criticality classification, pillar→channel mapping, SelfDamageEvent R-OD assignment |
| `networking-channel-contract.md` | Canonical per-message channel routing table, Forbidden Patterns, SequenceNumber scope |

## Overview

Networking Core is the foundational multiplayer infrastructure that makes Project Iron Grind an online world. It provides a dedicated server-authoritative runtime where combat outcomes, item enhancement results, level-up events, and gold mutations are computed on the server and broadcast to clients — players cannot locally modify outcomes or spoof results. At MVP, the server hosts instanced zones of 10–50 concurrent players; the server maintains a tick loop that drives the auto-attack cadence and reconciles player state across all connected clients. Networking Core owns nothing visible to the player. Its job is to guarantee that every system that depends on multiplayer — Auto-Attack Combat, Currency System, Leveling System, Zone Instancing, Party System, Character Persistence — receives a consistent, authoritative view of game state. All cross-system network contracts (message serialization format, connection events, server tick timing, and RPC patterns) are defined here and in the sub-documents listed above; all must be respected by every dependent system. The choice of specific networking library (Netcode for GameObjects, Mirror, or Photon) is deferred to an Architecture Decision Record; this GDD specifies the behavioral contracts that any chosen library must satisfy.

## Player Fantasy

Networking Core has no player-facing interface — players never touch this system. What they feel is its guarantee: *nothing they do can be taken back.*

Every kill logged is logged. Every gold piece earned is earned. Every weapon destroyed in a +8 attempt is gone, forever — the server witnessed it, committed it, and told every other client what happened, in that order, before your phone played the destruction animation. The cost of that permanence is also the gift of it: when you walk into town with a +9 weapon, no one wonders if you cheesed it. There is nowhere to cheese. The server doesn't offer a save state, and this audience knows exactly what that's worth.

The same fact that makes outcomes permanent also makes the world synchronous. The auto-attack clock isn't on your device. When you slot a skill between beats and it lands clean, you read the shared rhythm correctly — the same rhythm the healer two screens away is reading when they time a heal to your pull. Parties feel better not just because of buff multipliers, but because synchronized effort against the same clock is something that doesn't happen alone.

When this system breaks, players don't think "the networking is bad." They think "this game is lying to me." A +8 attempt that can be undone on reconnect, a grind session that rolls back on server crash, a kill that never counted — these are the failures this system exists to prevent. Its success is invisible. Its failure is trust-destroying.

## Detailed Design

### Core Rules

**CR-NET-1 — Server Authority**

**CR-NET-1.1** The server is the sole authority for all gameplay state. There is no client prediction, no client-side rollback, and no trust in client-reported values for any consequential outcome. The canonical state of every entity — position, HP, gold balance, level, inventory, enhancement level — lives on the server.

**CR-NET-1.2** Clients are display terminals. They receive state updates from the server and render them. A client may not write any gameplay value to another client or to the server's authoritative state. All client inputs are instructions to the server; the server validates and executes them or rejects them.

**CR-NET-1.3** Server-side computed values are never overridden by client-submitted values of the same type. If a client submits a position, HP, gold amount, or level value that contradicts the server's record, the server discards the client value and may log an anomaly.

**CR-NET-1.4** All combat outcomes, economy mutations (gold debits and credits), enhancement outcomes, and stat changes are computed on the server before any result is transmitted to any client.

---

**CR-NET-2 — Server Tick Loop**

The server runs a fixed 20 Hz tick (50ms interval). The tick interval is fixed at `1.0 / TICK_RATE_HZ` seconds. The `deltaTime` value used by all tick-driven systems (including `AutoAttackCombat._cycleTimer`) is this fixed constant — **not** actual wall-clock elapsed time between ticks. If a tick runs long (tick drift, EC-NET-10), `deltaTime` is still `1.0 / TICK_RATE_HZ`. This resolves Auto-Attack GDD OQ-1. Work is divided into tick-driven, event-driven, and connection-driven execution categories.

**Tick-driven (fires every 50ms):**
- Advance all active `AutoAttackCombat._cycleTimer` values by `deltaTime` for every entity in `COMBAT_ACTIVE` state
- Evaluate Beat Events for any entity whose `_cycleTimer >= CycleDuration`; Beat resolution runs in the fixed order defined by Auto-Attack Combat GDD Rule 10
- Flush one per-tick batch packet per connected client (see `networking-wire-protocol.md` CR-NET-7.7)
- Include `_cycleTimer` snapshots in each client's batch packet (display only — charge bar animation)
- Evaluate all active server-side TTL timers; TTL expirations fire `ItemReservation.Release()` on the tick boundary
- Advance tick-registered cooldown and duration counters via registered system delegates
- **`LastBeatServerTick` update (replaces `BeatResolvedThisTick`):** After Beat evaluation fires for an entity, set `LastBeatServerTick[slot] = ServerTickNumber`. Data structure: a per-session `uint[]` indexed by entity slot, initialized to `uint.MaxValue` on zone load. Read by OWL-compensation logic in CR-NET-8.2 / `networking-owl-compensation.md` CR-OWL-2 during `NotifySkillUsed` evaluation. Never persisted. Slot allocation contract: `networking-owl-compensation.md` CR-OWL-4. Array size: `MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE` — see Tuning Knobs.

**Entity scope note:** "Every entity in `COMBAT_ACTIVE` state" includes both player and mob entities. Mob entities run the same `_cycleTimer` / Beat evaluation loop as players. Zone Instancing GDD confirms `MAX_MOBS_PER_ZONE = 150` mobs in the tick loop (CR-ZI-8 step 10); total tick-loop entities at peak: n + m = 50 + 150 = 200. Tick budget impact of mob entities is modeled in **F-NET-9**.

**Event-driven (fires on condition; results queued into the next tick batch):**
- `GoldSyncEvent` — queued for the owning client's batch immediately after any successful `AddGold` or `TrySpendGold` mutation
- `OnLevelUp` — queued after all consecutive level-up iterations complete (Leveling System CR-2.10)
- Enhancement outcome — queued only after persistence commit confirms (see CR-NET-5)
- `AllocateFreePoint` response, respec commit/release response — queued for the current tick's batch
- Damage and kill notifications — queued on Beat resolution for the current tick's batch

**Connection-driven (exempt from batch buffering — sent immediately):**
- On new connection: authentication, zone assignment, session handshake emission
- On reconnection: session state recovery, handshake re-emission, respec reservation check (`networking-session.md` CR-NET-6.4)
- On clean disconnection: begin 5-minute session TTL countdown
- On session TTL expiry: write final character state to persistence, release all session resources

---

**CR-NET-3 — Message Channels**

Three channel types are defined. The networking library must implement or map to all three. Library selection is deferred to the Networking ADR.

| Channel | Delivery | Ordering | Duplicate protection |
|---------|----------|----------|---------------------|
| Reliable Ordered (R-OD) | Guaranteed | In-order per sender | Yes |
| Reliable Unordered (R-U) | Guaranteed | No ordering guarantee | Yes |
| Unreliable (U-U) | Best-effort, no retransmit | No ordering guarantee | No |

**Channel assignments:**

Per-message channel assignments are defined in **`networking-channel-contract.md`**, which is the sole authoritative routing source. Do not add or change channel assignments here — update `networking-channel-contract.md` instead.

Two corrections from prior versions of this table that `networking-channel-contract.md` resolves:
- `PartyMemberHealthUpdate` is **R-U** (not U-U — U-U for health data is a Forbidden Pattern per `networking-channel-contract.md`)
- `SelfDamageEvent` is **R-OD** (per `networking-message-criticality.md` — absent from prior versions of this table)

---

**CR-NET-4 — Connection Ownership**

**Server owns (authoritative, never overrideable by client):** all entity positions; all entity HP/MP and stat values; the auto-attack `_cycleTimer` and all Beat resolution outcomes; gold balances and `GoldSyncEvent.Version` counters; inventory state; enhancement levels; player level, `heldFreePoints`, and spent free point totals; all active TTL timers; zone membership; and combat gate flags.

**Clients may cache locally (display-only, invalidated by server update):** last-received position and facing; last-received `_cycleTimer` (for charge bar, smoothed per Auto-Attack GDD Rule 9); last-received gold balance and `cachedVersion` (for HUD, updated only via `GoldSyncEvent`); last-received HP/MP (for health bars); session handshake data.

**Clients must not:** compute damage values and apply them locally; compute gold balance deltas; report their own gold/HP/level/stat totals to the server as authoritative values; or initiate peer-to-peer state exchange.

**Stat derivation policy:** All derived stat values (`MaxHP`, `AttackPower`, `Defense`, `MagicDefense`, `CritChance`, `AttackSpeedMultiplier`) are computed server-side via Character Stats GDD formulas and transmitted to clients via `StatSnapshotEvent`. Clients never re-execute derivation formulas locally. `StatSnapshotEvent` is event-driven (fires on level-up and respec), not per-tick.

---

**CR-NET-5 — Commit-Before-Broadcast (Irreversible Outcomes)**

**CR-NET-5.1** Any outcome that cannot be reversed — item enhancement destruction, item consumption, level-up stat writes, gold mutation — must be fully committed to persistence on the server before any message describing the outcome is transmitted to any client.

**CR-NET-5.2** `EnhancementAttemptRequest` wire schema (R-OD, priority path, 12-byte body):

```
EnhancementAttemptRequest {
    EntityID entityId;   // 4 bytes — the requesting player's entity
    ItemID   itemId;     // 4 bytes — the item being enhanced
    uint     requestId;  // 4 bytes — client-generated monotonically increasing ID
}
```

`requestId` must differ from `LastEnhancementRequestID` in the character record. The server rejects any `requestId` matching the persisted value.

**CR-NET-5.3** Enhancement attempt commit-before-broadcast sequence:
1. Server receives `EnhancementAttemptRequest` from owning client
2. Server validates the request (item exists, materials present, `requestId != LastEnhancementRequestID`); if invalid, emits a rejection immediately and stops
3. Server emits `EnhancementRequestReceived { EntityID entityId; ItemID itemId; }` (R-OD, priority path) to owning client — **before** computing the outcome. Client enters a "processing" visual state. This is a request acknowledgment, not an outcome.
4. Server computes the outcome using Enhancement System logic
5. Server writes the outcome atomically to persistence; `LastEnhancementRequestID` is updated in the same write. Item's new state is durable.
6. Only after the write is confirmed durable does the server emit the outcome message to the owning client and any zone-visible broadcast
7. The owning client plays the outcome animation only after receiving the outcome message

**CR-NET-5.4** No speculative outcome is ever shown to the player. This rule is the technical implementation of the Player Fantasy: "the server witnessed it, committed it, and told every other client what happened, in that order, before your phone played the destruction animation."

**CR-NET-5.5** If a `SaveIrreversibleOutcome` write fails, the server does not emit an outcome message. No retries are attempted — irreversible outcome writes are not idempotent and a retry risks a duplicate commit. The complete failure protocol is defined by `character-persistence.md` CR-CP-5: the calling system reverts its in-memory mutations (caller-owns rollback), the client is disconnected (`DisconnectReason.Other`), the session is preserved in server memory for `SESSION_TTL_SECONDS` so that `SaveSession(SessionTTLExpiry)` can write the rolled-back state, and a critical infrastructure alert fires. The client never saw an outcome to undo. `SaveSession` failure handling (non-irreversible saves) is governed separately by `character-persistence.md` CR-CP-11.

**CR-NET-5.6** Item consumption during respec: commit-before-broadcast sequence (mirrors CR-NET-5.3 for enhancement).

1. Server receives `RespecPhase2Request` from owning client (Phase 1 committed, reservation active per TTL)
2. Server validates: reservation is still active (TTL not expired); `itemId` matches the reserved scroll; character has at least one allocated stat point to reset (i.e., total free points > `heldFreePoints`; a character with `heldFreePoints = 0` is the normal respec use case and must not be rejected)
3. Server emits `RespecPhase2Received { EntityID entityId; }` (R-OD) to owning client — before executing the respec. Client enters a "processing" visual state.
4. Server executes `TryApplyRespec()`: rewrites the free point distribution atomically in the character record (Leveling System CR-4.1)
5. Only after `TryApplyRespec()` completes successfully does the server call `ItemReservation.Consume()` to remove the respec scroll
6. Server writes the updated character state (new point distribution + scroll removed) to persistence in a single atomic write
7. Only after the write confirms durable does the server emit `StatSnapshotEvent` + `RespecOutcome { success = true }` (both R-OD) to the owning client
8. If `TryApplyRespec()` fails or the persistence write fails: server retains the Phase 1 reservation (TTL continues), emits `RespecOutcome { success = false }`, and the player may retry Phase 2 before TTL expiry. The scroll is not consumed.

---

**CR-NET-8 — RTT-Adaptive Grace Window**

To preserve Pillar 2 (Rhythm Mastery) for players on typical mobile connections (100–150ms RTT), the server applies per-client one-way latency (OWL) compensation when evaluating the Auto-Attack grace window for `NotifySkillUsed` RPCs. At 100ms RTT, a player tapping on-beat on their display has their notification arrive ~50ms after the beat on the server — breaking the grace window for correctly-timed taps. This rule resolves Auto-Attack GDD Rule 19 and OQ-1.

**CR-NET-8.1** The server maintains a per-session estimated OWL for each client. A `RttProbe` message (U-U, outside batch) is sent every `RTT_PROBE_INTERVAL_SECONDS` seconds; the client echoes it immediately with no processing delay. The server computes `OWL = round_trip_time / 2`. Initial OWL is estimated from the session handshake exchange timing. The specific RTT measurement API is defined in the Networking ADR (library-dependent).

**CR-NET-8.2** When a `NotifySkillUsed` RPC is received and the Auto-Attack system evaluates the grace window threshold, the server applies OWL compensation using the algorithm defined in **`networking-owl-compensation.md` CR-OWL-2** (F-OWL-1). Implementation summary:

- `_cycleTimer` is the server's authoritative tracked value — never client-submitted (B-NP-5).
- The wrap correction uses `LastBeatServerTick[slot]` (not `BeatResolvedThisTick`) to cover design-target 100–150ms RTT. The tick-window constraint proof and the entity slot allocation contract are in `networking-owl-compensation.md` CR-OWL-1, CR-OWL-3, CR-OWL-4.
- Adversarial analysis showing the expanded window cannot be exploited: `networking-owl-compensation.md` CR-OWL-5.
- The `BeatResolvedThisTick bool[]` data structure is retired. Use `LastBeatServerTick uint[]` per CR-OWL-1.

**CR-NET-8.3** OWL threshold and hysteresis band (GD-2):

At `OWL > MAX_COMPENSATABLE_OWL_MS / 1000` seconds (default: 120ms / 240ms RTT), no OWL compensation is applied — `adjustedCycleTimer = _cycleTimer` (raw, unadjusted).

**Hysteresis band:** To prevent rapid oscillation of the `ConnectionQualityUpdate` signal when OWL fluctuates near the threshold, a hysteresis band of ±`OWL_HYSTERESIS_BAND_MS` (default: 15ms) is applied:

- A session **enters** uncompensated mode (compensation OFF) when OWL rises above `MAX_COMPENSATABLE_OWL_MS + OWL_HYSTERESIS_BAND_MS` (default: 135ms).
- A session **exits** uncompensated mode (compensation ON) only when OWL falls below `MAX_COMPENSATABLE_OWL_MS - OWL_HYSTERESIS_BAND_MS` (default: 105ms).
- While OWL is between 105ms and 135ms (the band), the mode does not change — hysteresis prevents flapping.
- A `ConnectionQualityUpdate` is emitted only when the mode actually changes (compensation ON ↔ OFF). No message is emitted for OWL changes that remain within the band.

`ConnectionQualityUpdate { bool rhythmCompensationActive; }` is sent **only to the affected client** — not broadcast to the zone. Other players have no visibility into another player's OWL status.

**Degraded-mode fallback experience:** When OWL > threshold (uncompensated mode), the player continues to participate in combat fully via auto-attack — they are not helpless or excluded. The only degradation is that Rhythm Mastery skill triggers become unreliable: taps arriving after the grace window fires the auto-attack instead of the skill. The auto-attack deals full unmodified damage; the player's combat effectiveness is reduced but not eliminated. The `ConnectionQualityUpdate` indicator must communicate "skill timing may be less reliable" rather than "you are broken" — the HUD GDD must use language and iconography that is informational, not punishing.

**Non-combat input latency:** Target selection, `AllocateFreePoint`, respec Phase 1/Phase 2, and other non-combat RPCs have inherent RTT latency with no server-side grace window compensation. Mitigation is presentation-layer: the UI/UX GDD for each action must implement optimistic client-side feedback. This GDD defines the server contracts; optimistic feedback is owned by the system that presents the action.

---

### Interactions with Other Systems

The Networking Core is the transport and authority layer: it owns no game state itself but mediates every message that crosses the boundary between server logic and client display. **Reliability tiers**: `R-OD` = Reliable Ordered; `R-U` = Reliable Unordered; `U-U` = Unreliable Unordered. Wire schemas and sub-message sizes are defined in `networking-wire-protocol.md`.

---

**1. Auto-Attack Combat**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| Cycle timer broadcast | Server → All clients in zone | `CycleTimerBroadcast { EntityID attackerEntityId; ushort cycleTimer; }` | U-U | Charge bar animation only — never for damage logic. `cycleTimer` is a normalized fraction (÷CycleDuration ×10,000) per CR-NET-7.2. |
| `NotifySkillUsed` | Owning client → Server | `SkillUsedNotification { EntityID attackerEntityId; }` | R-U | Grace window logic owned by Auto-Attack Combat GDD Rule 17. Server applies OWL compensation per CR-NET-8 before evaluating the grace threshold. |
| Auto-face broadcast | Server → All clients in zone | `AutoFaceEvent { EntityID entityId; short rotX; rotY; rotZ; rotW; short dirX; dirY; dirZ; }` | R-U | Fires once per combat activation. Fixed-point quaternion and direction per CR-NET-7.2. |
| Damage result broadcast | Server → All clients in zone | See Hit Detection below | See Hit Detection | Auto-Attack Combat calls Damage Calculation server-side; result replicated via Hit Detection contract. |

*Trust risk: LOW.*

---

**2. Currency System**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| `GoldSyncEvent` delivery | Server → Owning client only | `GoldSyncEvent { CharacterID characterId; uint newBalance; uint version; GoldTransactionReason reason; }` | R-U | **`newBalance` must be absolute — never delta.** `version` is monotonically incrementing. Best-effort delivery — a dropped event is corrected on the next mutation. At n=50, uses forced-delivery override (displaces other R-U messages when batch is full) per `networking-wire-protocol.md` F-NET-1 Scenario C. Clients triggering rapid gold mutations create batch pressure for zone peers — expected behavior. Resolves Currency System OQ-CS-3. |
| Session handshake — gold seed | Server → Owning client (on connect and reconnect) | `SessionHandshake { ...; uint goldBalance; uint goldVersion; ... }` | R-OD | Required on every session establishment. Handshake must reflect post-mutation authoritative state. |

*Trust risk: MEDIUM. Handshake gold seed must be authoritative on reconnect to prevent a stale pre-debit balance from rendering.*

---

**3. Leveling System**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| Level-up event — owning client | Server → Owning client | `LevelUpEvent { EntityID entityId; int newLevel; bool isTierTransition; }` | R-OD | Fires after ALL consecutive level iterations complete. Sent in the same send call as the stat snapshot. |
| Stat snapshot — owning client | Server → Owning client | `StatSnapshotEvent { EntityID entityId; int level; int maxHP; int maxMP; int attackPower; int defense; int magicDefense; ushort critChance; ushort attackSpeedMultiplier; int currentHP; int currentMP; int heldFreePoints; }` | R-OD | Single atomic message — not field-by-field events. Fixed-point floats per CR-NET-7.2. Also sent after respec. |
| Level badge broadcast — zone peers | Server → All other clients in zone | `LevelBadgeUpdate { EntityID entityId; int newLevel; }` | R-U | Sent after owning-client packets complete. Social Gravity mechanic. |
| Zone announce — deferred | Server → All clients in zone | `LevelUpAnnounce { EntityID entityId; string characterName; int newLevel; bool isTierTransition; }` | R-U | **Stub — deferred per Leveling System OQ-LS-6.** |
| `AllocateFreePoint` RPC | Owning client → Server | `AllocateFreePointRequest { EntityID entityId; StatID targetStat; }` | R-OD | Rate-limit: server enforces minimum 200ms inter-request per entity. Excess calls rejected with `RateLimitExceeded` — not queued. |
| `AllocateFreePoint` result | Server → Owning client | `AllocateFreePointResult { ... }` + follow-up `StatSnapshotEvent` on success | R-OD | Full F-3–F-9 re-derivation delivered via `StatSnapshotEvent` after the result. |
| Respec stat replication | Server → Owning client | `StatSnapshotEvent` (same structure) | R-OD | Sent after `CharacterStats.EndStatTransaction()`. One atomic snapshot. |

*Trust risk: MEDIUM. Level-up and stat snapshot must be delivered in the same send call.*

---

**4. Zone Instancing**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| Zone session teardown | Server → All clients in zone | `ZoneSessionEnded { ZoneID zoneId; DisconnectReason reason; int gracePeriodSeconds; }` | R-OD | |
| Player join event | Server → All existing clients in zone | `PlayerJoinedZone { EntityID entityId; string characterName; int level; short posX; posY; posZ; }` | R-OD | Must arrive before any other messages referencing that `EntityID`. |
| Player leave event | Server → All remaining clients in zone | `PlayerLeftZone { EntityID entityId; DisconnectType disconnectType; }` | R-OD | Client despawns the entity on receipt. |
| Zone state snapshot on join | Server → Joining client | `ZoneStateSnapshot { EntityID[] entitiesInZone; EntityState[] entityStates; }` | R-OD | Bulk transfer (0xF000–0xFFFF). Requires fragmentation at n > ~7 entities. Zone-entry dual-gate: client must receive BOTH `SessionReady` AND complete `ZoneStateSnapshot` reassembly before rendering or sending RPCs — whichever arrives second opens the gate. |

*Trust risk: LOW.*

---

**5. Party System**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| Party state snapshot | Server → All party members | `PartyStateUpdate { PartyID partyId; PartyMember[] members; }` | R-OD | Full snapshot on any membership change. |
| Party member HP/MP tick | Server → All party members | `PartyMemberHealthUpdate { EntityID entityId; int currentHP; int currentMP; }` | R-U | 20 Hz update for healer HUD. U-U is a Forbidden Pattern for health data per `networking-channel-contract.md`. |
| Party invite RPC | Client → Server → Target client | `PartyInviteRequest / PartyInviteReceived` | R-OD | Two-hop routing. |
| Party disband | Server → All party members | `PartyDisbanded { PartyID partyId; EntityID initiatorId; }` | R-OD | |

*Trust risk: LOW.*

---

**6. Character Persistence**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| Character load gate | Server internal | Character Persistence loads character record, populates all `CharacterStats` base stats, restores `heldFreePoints`, gold balance, and `goldVersion`. | Internal | Networking Core must not allow the client to begin receiving zone messages or sending RPCs until character load completes. |
| Session-ready signal | Server → Owning client | `SessionReady { EntityID entityId; CharacterID characterId; ZoneID currentZone; short posX; posY; posZ; }` + full `StatSnapshotEvent` + gold seed in session handshake | R-OD | The gate that allows the client to begin rendering and sending input RPCs. Must not fire until: (1) character record loaded, (2) all `SetBaseStat()` calls complete, (3) zone instance confirmed. |
| Character save on disconnect | Server internal | On graceful logout or timeout detection, Networking Core fires a `CharacterDisconnected` event that Character Persistence subscribes to. | Internal | On timeout: Networking Core holds the session open for the 5-minute TTL before firing `CharacterDisconnected`. |

*Trust risk: HIGH — highest severity. Enhancement outcomes, level-up stat writes, and gold mutations must be committed to persistence before the outcome broadcast is sent.*

---

**7. Hit Detection**

| Flow | Direction | Data | Reliability | Notes |
|------|-----------|------|-------------|-------|
| Damage event broadcast | Server → All clients in zone | `DamageEvent { EntityID attackerEntityId; EntityID targetEntityId; int finalDamage; bool isCrit; DamageType damageType; }` | R-U | Best-effort. Brief out-of-order delivery produces cosmetic damage number flicker only. |
| Kill event broadcast | Server → All clients in zone | `KillEvent { EntityID killerEntityId; EntityID targetEntityId; string targetName; int finalDamage; bool isCrit; DamageType damageType; }` | R-OD | Triggers mob despawn, loot resolution, XP grant. `finalDamage`, `isCrit`, `damageType` carry the killing-blow data — clients display the killing-blow damage number from `KillEvent` directly and discard any subsequent `DamageEvent` for the same `targetEntityId` received after the kill. |
| Entity health broadcast | Server → All clients in zone | `EntityHealthUpdate { EntityID entityId; int currentHP; int maxHP; }` | R-U | Sent each tick for entities with changed HP via R-U batch. Best-effort; self-corrects next tick on drop. |

*Trust risk: LOW.*

---

**Cross-Cutting Constraints** (applied by Networking Core to all systems):

1. **EntityID validity gate.** Networking Core drops any inbound RPC referencing an `EntityID` not present in the current zone session. It does not forward the call to game logic.
2. **Session-ready gate.** No inbound RPCs from a client are forwarded to game logic until that client's `SessionReady` has been sent. RPCs arriving before session-ready are dropped, not queued.
3. **Rate limiting.** `AllocateFreePoint` RPCs: minimum `ALLOC_FREE_POINT_RATE_LIMIT_MS` (default 200ms) inter-request per entity. `NotifySkillUsed` RPCs: minimum `NOTIFY_SKILL_USED_RATE_LIMIT_MS` (default 50ms — one per tick at 20Hz) inter-request per entity; excess calls rejected with `RateLimitExceeded`, not queued. All other RPCs: no rate limit specified at MVP.
4. **Commit-before-broadcast policy.** For all irreversible high-stakes outcomes (enhancement result, level-up, gold mutation), the server must not emit the outcome broadcast until the persistence layer has confirmed the write.
5. **`GoldSyncEvent` absolute balance.** Networking Core must never re-encode `GoldSyncEvent.newBalance` as a delta during transport optimization or compression.

---

## Formulas

All bandwidth and session memory formulas are in the sub-documents:
- F-NET-1 through F-NET-3, F-NET-6, F-NET-7: `networking-wire-protocol.md`
- F-NET-4 (session memory), F-NET-5 (heartbeat timeout): `networking-session.md`

**F-NET-OWL — OWL Compensation**

The OWL grace-window adjustment formula is defined in **`networking-owl-compensation.md` F-OWL-1**. Key inputs: `_cycleTimer` (server-authoritative, never client-submitted), `OWL_seconds` (estimated per CR-NET-8.1), `LastBeatServerTick[slot]` (per-entity uint, updated at Beat evaluation), `MAX_WRAP_WINDOW_TICKS` (tuning knob, default 2), `CycleDuration` (server-owned, not transmitted). Output: `adjustedCycleTimer` compared against `BaseGraceThreshold × CycleDuration` to determine skill-trigger vs. auto-attack.

---

**F-NET-9 — Mob Entity Tick Budget Extension (resolves OQ-ZI-8)**

Zone Instancing adds `MAX_MOBS_PER_ZONE = 150` mob entities to the server tick loop (CR-ZI-8 step 10). Two budget components are affected.

**Part 1 — ServerLogic_ms: Beat evaluation with m mobs**

```
TotalTickEntities = MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE = 50 + 150 = 200
BeatEvalWork      = O(TotalTickEntities)   // one _cycleTimer advance per entity in COMBAT_ACTIVE
```

At peak density (all 200 entities in COMBAT_ACTIVE), Beat evaluation iterations are 4× the n=50 baseline in F-NET-6. The 30ms `TickBudget_ms` target in F-NET-6 is a design intent; the Networking ADR profiling pass must add a mob-density scenario (n=50 + m=150, all in COMBAT_ACTIVE) and confirm the total remains < 30ms.

**Part 2 — BatchFlush_ms: Mob state in player batches**

Mob entities do not receive batch packets — they are server-internal. Mob state must be broadcast to player clients, but the 512-byte per-client batch cap (F-NET-6) bounds the contribution.

`CycleTimerBroadcast` (CTB) is **not transmitted for mob entities.** CTB is a Rhythm Mastery display aid — a player-only UX mechanic (the player taps on the charge bar beat; mobs have no player-visible timing window). Mob attack timing reaches clients via `DamageEvent` and `KillEvent` (Interactions §7). Omitting mob CTBs eliminates up to 150 × 6 = 900 bytes of U-U batch pressure per client per tick — pressure that would overflow the 512-byte cap 1.8× if sent unfiltered.

Mob state included in player batches:

| State type | Channel | Delivery model | Expected per-client volume |
|-----------|---------|----------------|--------------------------|
| `EntityHealthUpdate` (mob HP changes) | R-U | Event-driven; emitted only when mob HP changes; scoped to mobs in client's relevance set (RFR-6 in `networking-relevance-filter.md`) | 0–20 sub-messages/tick |
| `EntityPositionUpdate` (mob position) | U-U | Per-tick; scoped to mobs in client's relevance set; subject to existing drop policy | 0–20 sub-messages/tick |

Per-client batch growth under this model (vs F-NET-6 Scenario C, n=50):

```
U-U sub-messages/client: ~98 (F-NET-6) + 0 mob CTB + ≤20 mob pos  = ≤118
R-U sub-messages/client: ~17 (F-NET-6) + ≤20 mob HP               = ≤37
```

Both counts remain within the 512-byte cap at standard mob density. Peak mob-combat density (all 150 mobs taking damage simultaneously) must be validated in the OQ-NC-SER-3 load simulation in `networking-wire-protocol.md` — a third density tier (n=50 + m=150 all-active) must be added to that simulation.

---

## Edge Cases

**EC-NET-2 — Server Crash During Commit-Before-Broadcast**

- If the persistence write completed before the crash: item's new state is durable. On restart, the character record reflects the outcome. The client reconnects to the correct post-outcome state via the session handshake. The player did not see the result animation but the item state is correct.
- If the write did not complete before the crash: the pre-attempt state is intact in persistence. On reconnect, the client receives the unchanged state. The attempt is treated as never having occurred.
- In neither case does the client see a result that contradicts the server's durable state.

---

**EC-NET-10 — Tick Drift (Server Falls Behind 50ms Target)**

If a tick takes longer than 50ms, `ServerTickNumber` still advances by 1 — it does not compensate by running multiple ticks in the next interval. Clients experience slightly less frequent updates during a drift window, manifesting as a brief animation stutter. Tick drift exceeding 25ms average over a 60-second window is a performance alert requiring profiling; it is not handled gracefully at runtime.

---

*For session edge cases (ghost entity behavior, zone-full on reconnect, rapid reconnect attempts, duplicate enhancement dedup), see `networking-session.md` Edge Cases. For wire-level edge cases (SequenceNumber wrap, GoldSyncEvent Version overflow), see `networking-wire-protocol.md` Edge Cases.*

---

## Dependencies

### Design Dependencies (upstream)

Three primitive specs now serve as upstream contracts for this document:

| Primitive Spec | Relationship |
|---------------|-------------|
| `networking-channel-contract.md` | Canonical routing authority for CR-NET-3; changes to channel assignments must be made there |
| `networking-message-criticality.md` | Defines criticality classification referenced in CR-NET-3 corrections and Interactions tables |
| `networking-owl-compensation.md` | Defines the F-NET-OWL algorithm referenced by CR-NET-8.2 |

Implementation additionally depends on three infrastructure components selected via ADR:

| Component | Decision Owner | Status | Note |
|-----------|---------------|--------|------|
| Networking library (NGO / Mirror / Photon) | Technical Director | **Accepted — ADR-004 (2026-06-15)** | NGO selected. GDD behavioral contracts are satisfied; see `docs/architecture/ADR-004-networking-library-ngo.md` for channel mapping, RTT API, and envelope ownership. |
| Hosting backend (self-hosted VPS / Unity Relay / Photon) | Technical Director | Deferred to ADR | Multiplay Hosting shut down March 31, 2026 — not an option |
| Persistence layer (database engine, ORM) | Technical Director | Deferred to ADR | CR-NET-5 commit-before-broadcast requires synchronous or write-ahead-log durability confirmation |

### Downstream Dependents

| System | GDD Status | Interface Required | Specified in this GDD? |
|--------|------------|-------------------|----------------------|
| Authentication | Not Started | Session handshake, connection lifecycle events | Partially — handshake structure defined; auth protocol deferred |
| Zone Instancing | Not Started | Zone session lifecycle, player join/leave events, zone state snapshot | Yes — Interactions #4 |
| Party System | Not Started | Party state sync, HP/MP tick, invite routing | Yes — Interactions #5 |
| Character Persistence | Not Started | Session load gate, save-on-disconnect event, commit-before-broadcast | Yes — Interactions #6 |
| Hit Detection | Not Started | Damage event broadcast, kill event, entity health tick | Yes — Interactions #7 |
| Movement System | Not Started | Position/movement channel, tick integration | Channel (U-U) assigned in CR-NET-3; Movement GDD defines message format |
| Auto-Attack Combat | Complete | Cycle timer broadcast, NotifySkillUsed RPC, damage result replication | Yes — Interactions #1 |
| Currency System | Approved | GoldSyncEvent delivery, session handshake gold seed | Yes — Interactions #2. Resolves OQ-CS-3. |
| Leveling System | Approved | Level-up event, stat snapshot, AllocateFreePoint RPC, respec replication | Yes — Interactions #3 |

### Implementation Prerequisite — Library ADR

**ADR-004 accepted 2026-06-15 — Networking Core implementation is unblocked** (library gate cleared). NGO selected; channel type mapping, envelope field ownership, RTT API, and clock source are specified in `docs/architecture/ADR-004-networking-library-ngo.md`. Remaining prerequisites: Hosting Backend ADR (deferred), Persistence Layer ADR (OQ-NET-5, deferred). See `networking-wire-protocol.md` for any remaining wire-level requirements.

---

## Visual/Audio Requirements

None. Networking Core is pure infrastructure. All player-facing effects triggered by networked events are owned by the dependent system.

## UI Requirements

None. Networking Core has no player-facing UI. Connection status display is owned by the UI system that consumes connection lifecycle events — this GDD defines the events but not the presentation.

---

## Tuning Knobs

| Knob | Default | Safe Range | Gameplay Impact |
|------|---------|------------|-----------------|
| `TICK_RATE_HZ` | 20 | [10, 30] | Server tick frequency. Changing requires re-validating auto-attack cadence (1.0s = 20 ticks), all tick-expressed timeouts, and `networking-wire-protocol.md` F-NET-6. **Do not change without a full cross-system audit.** |
| `MAX_PLAYERS_PER_ZONE` | 50 | [10, 100] | Zone instance capacity. Below 10: social gravity diminishes. Above 50: server tick budget and bandwidth must be re-validated (F-NET-2 in `networking-wire-protocol.md`); Zone Instancing mob density must also be re-authored. |
| `ZONE_CLOSE_GRACE_PERIOD_SECONDS` | 30 | [10, 120] | See `networking-session.md`. |
| `ALLOC_FREE_POINT_RATE_LIMIT_MS` | 200 | [100, 1000] | Minimum milliseconds between `AllocateFreePoint` RPCs per entity. Below 100ms: an auto-attack beat could overlap with an open stat transaction. Above 500ms: the stat allocation UI becomes noticeably sluggish. |
| `MAX_COMPENSATABLE_OWL_MS` | 120 | [60, 200] | Maximum one-way latency (ms) at which CR-NET-8 applies OWL compensation. Above this (adjusted for hysteresis band) compensation is suspended. Raising helps extreme-latency players but risks compensating artificially delayed inputs. |
| `OWL_HYSTERESIS_BAND_MS` | 15 | [5, 30] | Hysteresis band applied to the OWL threshold. Entry into uncompensated mode: OWL > `MAX_COMPENSATABLE_OWL_MS + OWL_HYSTERESIS_BAND_MS`. Exit: OWL < `MAX_COMPENSATABLE_OWL_MS - OWL_HYSTERESIS_BAND_MS`. Prevents rapid oscillation of `ConnectionQualityUpdate`. |
| `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` | 200 | [100, 400] | Maximum acceptable delay (ms) from player tapping "Enhance" to client receiving `EnhancementRequestReceived`. Consumed by network RTT + server validation + priority-path dispatch. |
| `NOTIFY_SKILL_USED_RATE_LIMIT_MS` | 50 | [25, 200] | Minimum milliseconds between `NotifySkillUsed` RPCs per entity. Default 50ms = one per tick at 20Hz (the physical maximum for a legitimate client). Below 25ms: server tick overhead increases; above 200ms: legitimate rapid skill use during high-speed combat could be incorrectly rate-limited. Excess calls rejected with `RateLimitExceeded`. |
| `MAX_MOBS_PER_ZONE` | 150 | [50, 500] | Slot array size for `LastBeatServerTick` (see `networking-owl-compensation.md` CR-OWL-4). Confirmed at 150 by Zone Instancing GDD (CR-ZI-8 step 10). Tick CPU budget impact: see F-NET-9. Raising above 150 requires re-validating F-NET-9 ServerLogic_ms and OQ-NC-SER-3 bandwidth projections. |

*Session-specific knobs (SESSION_TTL_SECONDS, HEARTBEAT_TIMEOUT_SECONDS, GHOST_COMBAT_TTL_MINUTES, REAUTH_FAILURE_LIMIT, FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS) are in `networking-session.md`. Wire-specific knobs (MAX_MESSAGE_BODY_BYTES, RTT_PROBE_INTERVAL_SECONDS) are in `networking-wire-protocol.md`.*

---

## Acceptance Criteria

**Server Authority**

**AC-NC-01** — Given a test client that sends a position value for its own entity outside the valid zone boundary, When the server processes the message, Then the server discards the client value, entity's server-side position is unchanged, and the server emits an anomaly log entry. *Precondition: Zone Instancing GDD must define zone boundary before this AC is testable.*

**AC-NC-02** — Given a test client that sends an `AllocateFreePointRequest` for an `EntityID` it does not own, When the server processes the message, Then the request is discarded, the target entity's stats are unchanged, and the anomaly is logged.

**AC-NC-03** — Given two clients connected to the same zone and client A taking damage, When the damage event reaches client B, Then the damage value displayed on client B matches the value the server computed. *Verification: server-side `INetworkTestObserver.OnServerDamageEventSerialized` captures server value; client-side `OnClientDamageEventReceived` captures received value; assert equality. See `networking-test-harness.md`.*

---

**Tick Loop**

**AC-NC-04** — Given a running server with at least one connected player, When the server runs for 10 seconds, Then the server completes exactly 200 tick iterations (±2 for jitter), verified by counting `INetworkTestObserver.OnTickCompleted` callbacks over the window. See `networking-test-harness.md`.

**AC-NC-05** — Given a Warrior entity in `COMBAT_ACTIVE` state, When the server tick advances `_cycleTimer` by `deltaTime` each tick, Then a Beat event fires every 20 ticks (1.0s at 20 Hz), verified by counting `INetworkTestObserver.OnTickCompleted` callbacks between consecutive Beat-resolved events. See `networking-test-harness.md`.

**AC-NC-06** — Given a player who begins a respec (Phase 1 commit, 30-second TTL running), When 30 seconds elapse without completing Phase 2, Then `ItemReservation.Release()` fires within one tick boundary (≤50ms) after TTL expiry and the respec scroll is returned to inventory. *Partially blocked until Inventory GDD authored.*

---

**Commit-Before-Broadcast**

**AC-NC-09** — Given a simulated database write that takes 200ms to confirm, When a player submits an enhancement attempt, Then the client does not receive the enhancement outcome message and does not play the animation until at least 200ms after submission, verified by comparing client-side receipt timestamp against server-side write confirmation timestamp.

**AC-NC-15** — Given test instrumentation that delays the persistence write by 500ms, When a player submits an enhancement attempt, Then the client does not receive the outcome until at least 500ms after submission. *Uses `IServerCrashInjector` delay variant from `networking-test-harness.md`.*

---

**Level-up Atomic Delivery**

**AC-NC-08a (Logic)** — Given a player who levels up triggering a tier transition, When the server sends the `LevelUpEvent` and `StatSnapshotEvent`, Then after the level-up animation completes, the HUD displays the new level AND the new MaxHP simultaneously — not new level + old MaxHP.

**AC-NC-08b (Visual/Feel)** — Given the same scenario, When the level-up animation is playing, Then no intermediate state is visible where the new level badge appears alongside the old MaxHP value. *Requires screenshot evidence and UI lead sign-off — not automatable.*

---

**Rate Limiting**

**AC-NC-20** — Given a player who sends `AllocateFreePoint` requests at 100ms intervals, When the server processes the requests, Then requests sent within 200ms of a prior accepted request are rejected with `RateLimitExceeded`. Requests separated by ≥200ms are accepted.

---

**Zone Capacity**

**AC-NC-22** — Given a zone at capacity (50 players), When a 51st player attempts to join, Then the server returns an overflow response, the 51st player is not added, and all 50 existing sessions are unaffected.

---

**Version Initialization and Monotonicity**

**AC-NC-25** — Given a newly created character record, When the character's first `GoldSyncEvent` is emitted, Then `GoldSyncEvent.Version` equals 1. Subsequent events have strictly increasing `Version` values. No event carries `Version = 0`.

---

**RTT Compensation — Timer-Wrap Case**

**AC-NC-29 (Logic)** — Given a server with `CycleDuration = 1.0s`, `BaseGraceThreshold = 0.92`, `OWL = 0.05s`, `MAX_WRAP_WINDOW_TICKS = 2`. When the server receives a `NotifySkillUsed` RPC with server-side `_cycleTimer = 0.02s` and `LastBeatServerTick[slot]` set to `ServerTickNumber - 1` (Beat fired last tick), Then: wrap correction activates → `adjustedCycleTimer = 1.0 + (0.02 − 0.05) = 0.97` → `0.97 > 0.92` → skill triggers. Contrast: `LastBeatServerTick[slot]` = `ServerTickNumber - 10` (Beat fired 10 ticks ago, outside window), Then `adjustedCycleTimer = max(0.02 − 0.05, 0) = 0` → auto-attack fires. *Unit-testable — inject via `IZoneTestConfigurator.SetLastBeatServerTick(EntityID, uint)`. See `networking-owl-compensation.md` AC-OWL-01 and AC-OWL-02.*

---

**Connection Quality Signal — Hysteresis**

**AC-NC-31 (Logic)** — Given a test client whose OWL estimate rises from 80ms to 150ms (default `MAX_COMPENSATABLE_OWL_MS = 120ms`, `OWL_HYSTERESIS_BAND_MS = 15ms`): When OWL crosses 135ms (120 + 15 hysteresis entry threshold), Then the server emits `ConnectionQualityUpdate { rhythmCompensationActive = false }`. When OWL subsequently drops below 105ms (120 − 15 hysteresis exit threshold), Then the server emits `ConnectionQualityUpdate { rhythmCompensationActive = true }`. No `ConnectionQualityUpdate` is emitted for OWL changes that remain within the band (e.g., 110ms → 125ms → 130ms — all within band). Verified by comparing server event log against client-received message log.

---

**Stat Snapshot — Event-Driven Only**

**AC-NC-44 (Logic)** — Given a running zone with 10 connected players over 60 seconds of idle state (no level-ups, no respecs), When the server completes 1,200 ticks, Then the server emits zero `StatSnapshotEvent` messages to any client. `StatSnapshotEvent` is event-driven — it fires only on level-up and respec, not on tick boundaries. *Verified by counting `INetworkTestObserver.OnStatSnapshotEmitted` calls over the test window.*

---

**Respec Commit Path**

**AC-NC-45 (Logic)** — Given test instrumentation that delays the persistence write by 300ms, When a player submits a `RespecPhase2Request`, Then the client does not receive `StatSnapshotEvent` or `RespecOutcome { success = true }` until at least 300ms after submission, verified by comparing client-side receipt timestamp against server-side write confirmation timestamp. The respec scroll must be confirmed removed from inventory only after the write confirms. *Uses `IServerCrashInjector` delay variant from `networking-test-harness.md`.*

---

**NotifySkillUsed Rate Limiting**

**AC-NC-46 (Logic)** — Given a test client that sends `NotifySkillUsed` RPCs at 10ms intervals (5× the default rate limit), When the server processes 10 consecutive RPCs from this client within 100ms, Then at most 2 RPCs are accepted (one per 50ms default `NOTIFY_SKILL_USED_RATE_LIMIT_MS`), the remaining 8 are rejected with `RateLimitExceeded`, and the server's game state is not affected by the rejected RPCs. *Verified by counting accepted RPCs via `INetworkTestObserver.OnSkillUsedRateLimitRejected`.*

---

*For session state machine ACs (10–14, 16, 23–24, 26–27, 32–35), see `networking-session.md`. For serialization and bandwidth ACs (17–19, 21, 28, 30), see `networking-wire-protocol.md`. For test infrastructure ACs (36, TC-01 through TC-02), see `networking-test-harness.md`.*

---

## Open Questions

**OQ-NET-5 — BLOCKING (architecture) — Persistence write confirmation latency**
CR-NET-5.2 requires the enhancement outcome persistence write to be confirmed durable before the outcome message is sent. Write latency directly adds to the player-perceived delay between tapping "Enhance" and seeing the result. On a co-located database this might be 1–5ms; on a managed cloud database it could be 20–100ms. The Networking ADR must include a latency budget for this write path and specify the mitigation if latency exceeds `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` (default 200ms). Mitigation options: brief loading spinner during write; write-ahead log with local durability guarantee; co-located write-ahead log. Joint decision: network-programmer + technical director.

*Session lifecycle open questions (OQ-NET-1, OQ-NET-2, OQ-NET-3) are in `networking-session.md`. Wire format open questions (OQ-NC-SER-1 through OQ-NC-SER-4) are in `networking-wire-protocol.md`.*
