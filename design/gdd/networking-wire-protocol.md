# Networking Wire Protocol

> **Status**: Approved (Pass 4 lean, 2026-05-12; ghost session amendment 2026-05-14; wire schema additions 2026-05-17; Zone Instancing amendment 2026-05-29; CSP amendment 2026-06-14)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-06-14 (CSP amendment: `SelfPositionUpdate` added to R-U batch — per-tick authoritative self-position delivery for client-side prediction reconciliation. Resolves CR-CSP-7 data-source gap. Channel: R-U (not U-U) — reconciliation most critical under packet loss. Body: 10B; batch: 14B; Scenario C R-U batch 332B (no overflow; 180B headroom). F-NET-1/F-NET-2 updated. Prior: Zone Instancing amendment 2026-05-29; OQ-CUS-1 amendment 2026-06-11.)
> **Parent**: networking-core.md

## Overview

Networking Wire Protocol specifies every bit on the wire for Project Iron Grind's server-authoritative networking layer. It defines the message envelope format, all primitive type encodings, the sub-message framing contract for per-tick batch packets, the two-path delivery model (priority path + R-U batch + U-U batch), all wire type declarations, and the bandwidth formulas. All other Networking sub-documents defer to this document for serialization rules and message size calculations.

This document is a sub-document of Networking Core. Channel assignments (R-OD, R-U, U-U) are defined here per CR-NET-3 but the authority policy that motivates those assignments lives in `networking-core.md`.

## Player Fantasy

None. This document is pure infrastructure — no player-facing interface. Its correctness is felt indirectly: when the wire protocol works, actions feel immediate and authoritative. When it breaks, players see desynced health bars, gold that doesn't update, and animations that fire on corpses.

## Detailed Rules

### CR-NET-7 — Serialization Contract

**CR-NET-7.1 — Message Envelope**

Every message includes these required fields:

| Field | Type | Wire Size | Description |
|-------|------|-----------|-------------|
| `MessageTypeID` | `ushort` | 2 bytes | Identifies the message schema; dispatches to the correct deserializer |
| `SequenceNumber` | `uint` | 4 bytes | Monotonically increasing per-connection counter, **shared across all message types** sent by that endpoint (one counter per connection, not one per message type); used for ordering and stale-discard. Starts at 1; 0 = uninitialized, must never appear in a valid message. See `networking-channel-contract.md` CCR-1. |
| `ServerTickNumber` | `uint` | 4 bytes | The server tick during which this message was authored; never wall-clock time |

Envelope total: **10 bytes**. Client-to-server messages that reference a runtime entity add `SenderEntityID` (`uint`, 4 bytes) = **14 bytes total**.

---

**CR-NET-7.2 — Primitive Mappings**

All primitives serialize as fixed-width binary, little-endian. No variable-length encoding at MVP. `bool` = 1 byte (`0` = false, `1` = true). `string` = `ushort` byte-count + UTF-8 bytes, max 255 bytes; used for character names and reason strings only — never on the hot path.

**Authoritative state float rule:** Fields that participate in server-authoritative computation (gold, HP, XP, levels, stat totals that feed back into server logic) must serialize as integer types. `CharacterStats.CurrentHP` (internal `float`) is serialized as `Mathf.FloorToInt(CurrentHP)` at the wire boundary — all serialization paths must use FloorToInt, not truncation or RoundToInt, to ensure all clients receive the same HP value.

**Display-only float encoding:** Fields transmitted for client display only (values that do not feed back into server computation) must be encoded as fixed-point integers. Defined encodings:

- `critChance` → `ushort` (multiply by 10,000; range 0–7,500; client divides by 10,000)
- `attackSpeedMultiplier` → `ushort` (multiply by 1,000; range 500–2,000; client divides by 1,000)
- `cycleTimer` → `ushort` (normalized fraction: `cycleTimer ÷ CycleDuration × 10,000`; range 0–10,000 where 10,000 = 100% of cycle; client divides by 10,000 for charge bar fraction 0.0–1.0). CycleDuration is NOT transmitted — normalization is server-side. **Pre-encode guards:** (1) `CycleDuration` must be > 0 — a zone loading an entity with `CycleDuration ≤ 0` must reject it, log an anomaly, and skip it. (2) `cycleTimer` must be clamped to `[0, CycleDuration]` before normalization — if `cycleTimer > CycleDuration`, clamp, log an anomaly, encode as `10,000`. Encoded value is always in `[0, 10,000]`.
- `finalDamage` → `int` (already integer in `DamageResult` — no encoding needed)
- Vector3 position → 3 × `short` (multiply by 100 — centimeter precision; range ±327.67m per axis; client divides by 100). Field names: `posX`, `posY`, `posZ`. **Overflow guard:** Assert each component ∈ [−327.67, 327.67] before encoding; if out of range, clamp and log an anomaly. The Zone Instancing GDD must confirm all walkable zone bounds fit within ±327.67m before this encoding is finalized.
- Quaternion rotation → 4 × `short` (multiply by 32,767; range [−1.0, 1.0] per component; client divides by 32,767 and renormalizes). Field names: `rotX`, `rotY`, `rotZ`, `rotW`. Normalization error < 0.003%. **Pre-encode guard:** Normalize before encoding; clamp each component to [−1.0, 1.0] after normalization. **Zero-quaternion guard:** If magnitude < 1e-5, substitute identity `(0, 0, 0, 1)` and log an anomaly.
- Unit direction vector → 3 × `short` (multiply by 32,767; range [−1.0, 1.0]; client divides by 32,767). Field names: `dirX`, `dirY`, `dirZ`. **Pre-encode guard:** Normalize before encoding; if magnitude < 1e-5, substitute `(1, 0, 0)` and log an anomaly.

No raw `float`, `Vector3`, or `Quaternion` fields are permitted in any network message.

---

**CR-NET-7.3 — Entity ID Serialization**

`EntityID`, `ItemID`, and `CharacterID` each serialize as a 4-byte unsigned integer, little-endian. The reserved value `0` (Invalid) must never appear in a valid game message body — the serialization layer must assert this before writing. All ID struct serializers must be concrete non-generic methods (no `Serialize<T>` reflection paths — IL2CPP AOT safety requirement).

---

**CR-NET-7.4 — Enum Serialization**

All game-message enums must declare an explicit underlying type (`enum : byte` for types with <256 values). Enums serialize by numeric value, never by name. Receivers must range-check the received byte before casting — `Enum.IsDefined()` is forbidden on IL2CPP; use explicit range guards instead.

**StatID exception (cross-doc correction required):** `StatID` is currently defined as `enum StatID : uint` in the Character Stats GDD. This conflicts with the `enum : byte` requirement. Resolution: the Character Stats GDD must change `StatID` to `enum StatID : byte`, accepting a 256-stat ceiling (sufficient for this game). Until that change is made, `StatID` must be transmitted as a `byte` wire value with explicit range validation (`value < (byte)StatID.MAX_STAT_ID`). Implementation must not proceed until the Character Stats GDD update is confirmed.

---

**CR-NET-7.5 — Version Field Policy**

The `Version: uint` stale-discard pattern applies to all authoritative state sync messages. Version counters are per-character, per-field, starting at 1 (`0` = uninitialized). Server-assigned only.

**Stale-discard comparison must use RFC 1982 serial arithmetic**, not raw `uint` comparison. Raw `uint` comparison (`incoming.Version > cachedVersion`) is incorrect after wraparound:

```csharp
// RFC 1982 serial number comparison — handles uint wraparound correctly.
// Returns false at equality (same version = not newer, keep current state).
static bool IsNewerVersion(uint incoming, uint cached) =>
    (uint)(incoming - cached) < 0x80000000u && incoming != cached;
```

This correctly identifies incoming version 2 as newer than cached 4,294,967,294 because `(uint)(2 - 4294967294) = 4 < 0x80000000`. This same algorithm applies to `SequenceNumber` stale-discard.

**IsTickExpired (companion helper — used by networking-session.md):**

```csharp
// Returns true when currentTick is at or past expiryTick (handles uint wraparound).
// Equality semantics: true when currentTick == expiryTick (expired exactly now).
// Differs from IsNewerVersion which returns false at equality.
static bool IsTickExpired(uint currentTick, uint expiryTick) =>
    (uint)(currentTick - expiryTick) < 0x80000000u;
```

---

**CR-NET-7.6 — Message Size Budget**

Maximum message body = **512 bytes**; maximum total message = 522 bytes. Messages exceeding 512 bytes (initial character load, zone population snapshot) use `MessageTypeID` range `0xF000–0xFFFF` (bulk transfer messages) and are only sent on connection events, never at tick rate. Priority/control messages use `MessageTypeID` range `0xE000–0xEFFF` and may be sent outside the batch.

**Per-tick batch outer envelopes** use the `0x0100–0x01FF` batch-header range. Reserved values:
- `TICK_BATCH_RU = 0x0101` — per-tick R-U batch packet (Path 2a)
- `TICK_BATCH_UU = 0x0102` — per-tick position/party U-U batch packet (Path 2b-ii)
- `TICK_BATCH_UU_CYCLE = 0x0103` — per-tick CycleBroadcast U-U packet (Path 2b-i)

Application message types occupy `0x0200–0xDFFF`; specific values are assigned in the Networking ADR. Specific `MessageTypeID` values within `0xE000–0xFFFF` ranges are also defined in the Networking ADR.

---

**CR-NET-7.7 — Two-Path Delivery Model**

Because a single packet cannot carry mixed reliability guarantees, messages are split into two delivery paths per tick:

**Path 1 — Priority path (outside batch, sent immediately):** R-OD messages are sent individually as they are emitted, using the `0xE000–0xEFFF` priority `MessageTypeID` range. These include: session handshake, `SelfDamageEvent` (attacker's own client only — Pillar 2 direct feedback, see `networking-message-criticality.md` MCR-2), level-up event + stat snapshot, `AllocateFreePoint` request and response, respec Phase 1 / Phase 2 messages, enhancement attempt request, enhancement outcome broadcast, `EnhancementRequestReceived`, item consumption confirmation, zone session teardown, player join/leave events, kill event broadcast, respec stat replication, party state updates, session-ready signal, `ZoneStateSnapshot` (bulk path), death & respawn zone events (`EntityDied`, `EntityRespawned`), death & respawn client events (`DeathStateEntered`, `RespawnConfirmed`). Channel assignments for all messages are derived from `networking-message-criticality.md` (MCR-2) and `networking-channel-contract.md` (CCR-3).

**Priority-path capacity cap:** Priority-path traffic is capped at **8 messages per client per tick boundary**. If more than 8 priority-path messages are queued for a single client within one tick, excess messages are held and sent in the following tick(s) in emission order — no message is dropped, only deferred. The cap applies per destination client.

**Enhancement-path exemption:** `EnhancementRequestReceived` and the enhancement outcome broadcast are **exempt from the 8-message cap** and are placed at the front of the application-layer Path 1 queue. This does not violate R-OD transport ordering because the queue-jump operates at the application layer before handoff to the transport; messages already in-flight are unaffected.

**Exemption timing (clarification):** The exemption applies at queue-insertion time, before the tick's batch-flush point. If the enhancement outcome is enqueued before the flush, it is included in the current tick's outbound payload (the tick may emit up to `PRIORITY_PATH_CAP + 1` or more messages that tick). If the flush has already occurred when the outcome is enqueued, the outcome is placed at the front of the next tick's Path 1 queue with cap exemption — it is sent at the start of the next tick's flush. Implementers must not check whether the flush has occurred and retroactively insert into an already-serialised batch.

If the priority path is at cap when an enhancement outcome is enqueued before the flush, the outcome takes slot position 1 in the queue; the oldest non-exempt message is displaced to the next tick.

**Tie-break rule (B-NP-6):** When multiple enhancement outcomes are ready simultaneously (e.g., two players' enhancements commit in the same tick), they are placed at the front of the Path 1 queue in **commit-to-persistence order** (the order their persistence writes were confirmed). No other message class receives this exemption.

**Bulk-transfer exemption:** Bulk-transfer fragments (`0xF000–0xFFFF`) are exempt from the 8-message Path 1 cap — they are a connection-phase one-time transmission, not ongoing priority traffic. Without this exemption, 7 fragments from a `ZoneStateSnapshot` would consume 7 of 8 Path 1 slots in that tick.

---

**Path 2a — Reliable Batch (one R-U packet per tick per client):**

R-U messages are accumulated and flushed in a single `R-U` packet. **Batch format:**

```
[Envelope: MessageTypeID=TICK_BATCH_RU, SequenceNumber, ServerTickNumber] (10 bytes)
[uint16: sub-message count N]
[Sub-message 0]: [uint16 length][MessageTypeID][payload]
[Sub-message 1]: [uint16 length][MessageTypeID][payload]
...
[Sub-message N-1]: [uint16 length][MessageTypeID][payload]
```

The `uint16 length` prefix precedes each sub-message and records the byte count of `MessageTypeID + payload` (not including the 2-byte length field itself). Receivers must use the length field to advance past unknown sub-messages — this enables forward compatibility when new R-U batch message types are added. Senders must never write sub-messages whose `MessageTypeID + payload` exceeds `MAX_MESSAGE_BODY_BYTES - 14` (to guarantee a single sub-message always fits in a fresh batch).

**Batch envelope extension (clarification re CR-NET-7.1):** Batch packets extend the standard 10-byte CR-NET-7.1 envelope with an additional 2-byte `uint16` sub-message count field appended immediately after the envelope, giving all batch packet types a **12-byte header** total. CR-NET-7.1 defines the 10-byte base envelope; the 2-byte count field is exclusive to message types in the `0x0100–0x01FF` batch-header range.

**Canonical R-U batch message list:**
- Beat resolution damage events (`DamageEvent`)
- Entity health updates (`EntityHealthUpdate`) — relevance-filtered per `networking-relevance-filter.md`; sent only to clients for whom the entity is in their relevance set (party member or current target)
- Party member health updates (`PartyMemberHealthUpdate`) — **moved from U-U Position packet to R-U batch** per `networking-message-criticality.md` MCR-2 (Pillar 3 fellowship-critical; reliable delivery required)
- `GoldSyncEvent`
- `SkillCastResult` — accepted casts: sent to caster + all clients for whom caster or target is in their relevance set; rejected casts: caster only. Event-driven; at most one per entity per tick. See `skill-system.md` CR-SK-9.
- `SkillCooldownUpdate` — sent to caster only when a skill's cooldown state changes (started or expired). Event-driven. See `skill-system.md` CR-SK-12.
- `LootBidUpdate` — real-time bid feed during active Steel/DarkSteel auctions; sent to all eligible party members; event-driven and excluded from F-NET-1 baseline bandwidth scenarios
- `SelfPositionUpdate` — per-tick authoritative self-position delivery for client-side prediction reconciliation (R-U not U-U — see schema note); sent to owning client only
- `ConnectionQualityUpdate` (B-NP-4)

Zone join/leave notifications are R-OD and travel via Path 1 — they do not appear in the R-U batch.

**R-U serialization order within batch (highest priority written first):** damage events → `EntityHealthUpdate` → `PartyMemberHealthUpdate` → `SelfPositionUpdate` → `SkillCastResult` → `GoldSyncEvent` → `SkillCooldownUpdate` → `LootBidUpdate` → `ConnectionQualityUpdate`.

Writing highest-priority sub-messages first prevents buffer exhaustion from lower-priority messages blocking critical events.

**R-U Batch overflow policy:** When the accumulated R-U batch would exceed `MAX_MESSAGE_BODY_BYTES`, sub-messages are dropped in this order (lowest priority dropped first):

1. `LootBidUpdate` — dropped first; superseded by subsequent bid updates within the same auction tick; only present during active auctions.
2. `SkillCooldownUpdate` — dropped second; cooldown display self-corrects within one or two ticks as the server continues emitting state-change events. Only present when a skill enters or exits cooldown.
3. `GoldSyncEvent` — dropped third (B-SD-3). Self-corrects via version-based stale-discard on the next mutation. **Forced R-OD delivery invariant:** if `GoldSyncEvent` has been overflow-dropped for `GOLD_MAX_CONSECUTIVE_DROP` consecutive ticks for the same client, the server must emit a standalone `GoldSyncEvent` on the R-OD priority path on the next tick (see `networking-message-criticality.md` MCR-4). This prevents sustained gold display lag under peak-combat overflow.
4. `SkillCastResult` — dropped fourth; event-driven (at most one per entity per tick), so overflow contribution is minimal. If dropped, the caster's cooldown state self-corrects via the next `SkillCooldownUpdate`; VFX/audio will not play for the missed cast event.
5. `SelfPositionUpdate` — dropped fifth. A missed reconciliation tick leaves the client's predicted position uncorrected for one tick; the next delivered `SelfPositionUpdate` re-anchors reconciliation. Self-corrects within one tick at 20 Hz; does not cause permanent desync.
6. `EntityHealthUpdate` — dropped sixth. Self-corrects on the next tick; relevance filter (see `networking-relevance-filter.md`) should eliminate overflow at n≤50 under Scenario C density.
7. `PartyMemberHealthUpdate` — dropped seventh if present after EntityHealthUpdate drops.
8. `ConnectionQualityUpdate` — dropped last; rare message that fires only on OWL threshold crossings.

Damage events (`DamageEvent`) are best-effort (see `networking-core.md` CR-NET-3 note) but are written first and are the last to be crowded out by the serialization order. A single overflow alert is logged if any drop occurs for 3 consecutive ticks for the same client.

**Path 2b — Unreliable Batches (two U-U packets per tick per client — PA-P7-05 resolution):**

U-U messages are split across **two separate U-U packets** per tick to prevent `CycleTimerBroadcast` from crowding out `EntityPositionUpdate` at high player counts. Both use the same sub-message framing as the R-U batch (uint16 length prefix per sub-message). If either U-U packet is lost in transit, no retransmit occurs — the next tick carries fresh values.

**Path 2b-i — CycleBroadcast packet (`TICK_BATCH_UU_CYCLE = 0x0103`):**

```
[Envelope: MessageTypeID=0x0103, SequenceNumber, ServerTickNumber] (10 bytes)
[uint16: sub-message count N]
[Sub-message 0]: [uint16 length][0x????][CycleTimerBroadcast payload]
...
```

Contains: one `CycleTimerBroadcast` sub-message per zone entity other than the receiving client. This packet never overflows at `MAX_PLAYERS_PER_ZONE ≤ 50`: header(12) + 49 × CTB(10) = **502 bytes** (within 512-byte cap). At `MAX_PLAYERS_PER_ZONE = 100`, this packet reaches 1,002 bytes — require `MAX_MESSAGE_BODY_BYTES ≥ 1,000` or split further (see Tuning Knobs). CycleTimerBroadcast is never dropped — it drives Rhythm Mastery charge bar continuity (Pillar 2).

**Path 2b-ii — Position packet (`TICK_BATCH_UU = 0x0102`):**

```
[Envelope: MessageTypeID=0x0102, SequenceNumber, ServerTickNumber] (10 bytes)
[uint16: sub-message count N]
[Sub-message 0]: [uint16 length][MessageTypeID][EntityPositionUpdate or PartyMemberHealthUpdate payload]
...
```

Contains: `EntityPositionUpdate` sub-messages only — all zone entities other than the receiver. **`PartyMemberHealthUpdate` is no longer in this packet** — it has been moved to the R-U batch per `networking-message-criticality.md` MCR-2 (Pillar 3 fellowship reliability requirement).

**Position packet serialisation order:** `EntityPositionUpdate` entries sorted by ascending `entityId`.

**Position packet overflow policy:** If total sub-messages exceed `MAX_MESSAGE_BODY_BYTES`, drop `EntityPositionUpdate` entries highest-ID-first (consistent across all clients). Dropped U-U data self-corrects on the next tick. At n=50: 49 positions × 14B = 686B + 12B header = 698B → overflow; available = 500B; ⌊500/14⌋ = **35 positions delivered**, 14 dropped.

**Why three packets total:** `CycleTimerBroadcast` and position updates share U-U reliability but differ in capacity dynamics. Before this fix (PA-P7-05), at n=50 `CycleTimerBroadcast` alone saturated the 512-byte cap (502 bytes), delivering **zero** position updates. Separating them guarantees full CycleBroadcast delivery (Pillar 2 correctness) while restoring position delivery to 35/49 entities at n=50 (71%, vs. 0% before).

**Buffer allocation (per-zone):** Each client connection holds three pre-allocated fixed-size byte arrays — one R-U buffer, one CycleBroadcast buffer, one Position buffer — each `MAX_MESSAGE_BODY_BYTES + 12` bytes. Buffers are allocated when the zone instance is created and released when the zone closes. **Do not use `ArrayPool<T>.Shared`** — under IL2CPP, `Rent()` may allocate when exhausted, causing GC spikes. Pre-allocation at zone creation guarantees zero GC on the hot path. Pool size per zone = `MAX_PLAYERS_PER_ZONE × 3` buffer arrays.

**Buffer exhaustion policy:** If the pool cannot allocate for a new connection (zone at max capacity), the connection is rejected at the transport layer before session establishment. This is a configuration error, not a runtime condition. The server logs a `BufferPoolExhausted` critical anomaly.

**DamageEvent intra-class overflow policy:** In the rare case where `DamageEvent` sub-messages alone saturate the R-U batch (e.g., 49 simultaneous hits on one target in an AoE scenario: 49 × 18B = 882B), the batch holds at most ⌊(MAX_MESSAGE_BODY_BYTES − 12) / 18⌋ `DamageEvent` entries. Overflow `DamageEvent` sub-messages are dropped in **oldest-first order** (lowest tick-sequence number dropped first). Each overflow event logs a `DamageEventIntraclassOverflow` anomaly with the drop count and the tick number. This is a best-effort channel — clients treat missing damage numbers as cosmetic, not as a desynced state.

---

**CR-NET-7.8 — IL2CPP / AOT Requirements**

No generic serializers using `typeof(T)` dispatch for value types. No `[StructLayout(LayoutKind.Explicit)]` without device testing. No `BinaryFormatter` or `JsonUtility`. Avoid boxing value types on the hot path. `enum : byte` range validation must use explicit guards — `Enum.IsDefined()` is forbidden.

Additional IL2CPP pitfalls (must avoid in the networking stack):
- **Interface dispatch on value types:** Calling an interface method on a struct causes boxing at each call site on IL2CPP. Message handler registration must use concrete delegates, not interfaces on structs.
- **Virtual dispatch on generic value-type parameters:** `struct MessageBuffer<T> where T : struct` calling a virtual/interface method on `T` fails on IL2CPP while working in the Editor. Do not use virtual dispatch on generic value-type parameters.
- **`System.Reflection.Emit` unavailable on IL2CPP.** Any third-party serialization library that uses `Emit` for performance must be explicitly tested in IL2CPP mode before adoption.
- **LINQ forbidden on the message dispatch and serialization hot paths.** `Where`, `Select`, and `OrderBy` allocate enumerators. Use index-based loops.
- **Lambda capture on handler registration must be avoided.** Closures allocate on every registration. Register message handlers as named method delegates.
- **Primitive serializer generics forbidden on the hot path.** All primitive field serializers must be concrete non-generic methods with no runtime type dispatch. The Networking ADR must audit the chosen library's primitive serialization paths and confirm compliance.

---

**CR-NET-7.9 — Wire Type Declarations**

All ID types serialize as 4-byte unsigned integers (CR-NET-7.3):

| Type | Wire type | Reserved value | Notes |
|------|-----------|----------------|-------|
| `ZoneID` | `uint` (4 bytes) | `0` = Invalid | Identifies a zone instance; never reused within a server run |
| `PartyID` | `uint` (4 bytes) | `0` = Invalid | Identifies a party; `0` means no party |
| `GroundItemID` | `uint` (4 bytes) | `0` = Invalid | Identifies a spawned ground item within a zone session; not persisted across sessions |

All enum types used in game messages declare explicit underlying types per CR-NET-7.4:

```csharp
enum DamageType : byte
{
    Physical = 0,
    Magical  = 1,
    True     = 2,   // Ignores defense; reserved for future use
}

enum DisconnectReason : byte
{
    ZoneClosed     = 0,  // Zone instance shutting down
    ServerShutdown = 1,  // Server maintenance
    AdminKick      = 2,
    GhostDeath     = 3,  // Reconnect rejected — ghost entity died during disconnect period (CGS-5/CGS-6)
    Other          = 255,
}

enum DisconnectType : byte
{
    Graceful    = 0,  // Player sent explicit logout
    Timeout     = 1,  // Heartbeat timeout — session TTL may still be active
    ZoneTransfer = 2, // Player moving to a different zone
}
```

Receivers must range-check received bytes before casting. Per-enum unknown-byte handling:

- `DamageType`: substitute `Physical = 0` and continue — damage number displays with misclassified type rather than being dropped.
- `DisconnectReason` (in `ZoneSessionEnded`): substitute `Other = 255` and continue — zone teardown still processes. `GhostDeath = 3` triggers the respawn flow on the client (not the reconnect flow).
- `DisconnectType` (in `PlayerLeftZone`): substitute `Timeout = 1` and continue — entity is still despawned.

Unknown bytes outside declared range must be logged as anomalies.

---

**CR-NET-7.10 — Heartbeat Message Schema (GAP-1)**

The heartbeat is a client-to-server keep-alive that resets the server's inactivity timeout counter for the sending session. Its mere receipt (at the transport layer) is the signal — no body fields are required.

```
HeartbeatMessage {
    // No body fields. Wire size = envelope only (10 bytes).
    // Any packet received from the client — including this one — resets the
    // heartbeat timeout counter. The HeartbeatMessage exists to ensure the
    // client has a defined message to send when no other RPC is pending.
}
```

**Channel:** U-U (client → server), standalone packet (not batched). A dropped heartbeat is self-correcting — the next heartbeat arrives within `HEARTBEAT_INTERVAL_SECONDS`. The server's timeout fires only after `HEARTBEAT_TIMEOUT_SECONDS` of total silence (no packet of any type).

**Rate:** The client sends one `HeartbeatMessage` every `HEARTBEAT_INTERVAL_SECONDS` when no other outbound RPC (movement, `NotifySkillUsed`, etc.) has been sent in the preceding interval. If another packet was sent, the heartbeat is skipped for that interval — any packet resets the timeout counter.

**Wire size:** 10 bytes (envelope only). See F-NET-7 for inbound bandwidth contribution.

---

### Message Schemas

All schemas listed here are body-only (envelope is prepended as defined in CR-NET-7.1). Wire sizes:
- **Standalone**: body + 10-byte envelope (+ 4 bytes SenderEntityID for client-to-server messages)
- **Batch sub-message**: `2 (uint16 length prefix) + 2 (MessageTypeID) + body bytes`

---

#### Tick-Rate Messages (per-tick batch)

**DamageEvent** (R-U batch, 14-byte body; **18 bytes in batch**; 24 bytes standalone):
```
DamageEvent {
    EntityID   attackerEntityId; // 4 bytes
    EntityID   targetEntityId;   // 4 bytes
    int        finalDamage;      // 4 bytes
    bool       isCrit;           // 1 byte
    DamageType damageType;       // 1 byte
}
```
*Batch size: 2 (prefix) + 2 (TypeID) + 14 (body) = 18 bytes. Sent to all zone clients **except** the attacker — the attacker's own client receives `SelfDamageEvent` via R-OD instead.*

**SelfDamageEvent** (R-OD priority path, 14-byte body; 24 bytes standalone):
```
SelfDamageEvent {
    EntityID   attackerEntityId; // 4 bytes — matches the receiving client's EntityID
    EntityID   targetEntityId;   // 4 bytes
    int        finalDamage;      // 4 bytes
    bool       isCrit;           // 1 byte
    DamageType damageType;       // 1 byte
}
```
*Body: 14 bytes — identical fields to `DamageEvent`. Standalone size: 10 (envelope) + 14 = 24 bytes. Sent **only** to the attacker's own client via R-OD (Pillar 2 direct feedback signal — guaranteed delivery per `networking-message-criticality.md` MCR-2). The receiving client must assert `attackerEntityId == localPlayerEntityId`; mismatch logs a `SelfDamageDirectionViolation` anomaly and suppresses display.*

**EntityHealthUpdate** (R-U batch, 12-byte body; **16 bytes in batch**; 22 bytes standalone):
```
EntityHealthUpdate {
    EntityID entityId;  // 4 bytes
    int      currentHP; // 4 bytes
    int      maxHP;     // 4 bytes
}
```
*Batch size: 2 + 2 + 12 = 16 bytes.*

**GoldSyncEvent** (R-U batch, 13-byte body; **17 bytes in batch**; 23 bytes standalone):
```
GoldSyncEvent {
    CharacterID           characterId; // 4 bytes
    uint                  newBalance;  // 4 bytes — absolute balance, never delta
    uint                  version;     // 4 bytes
    GoldTransactionReason reason;      // 1 byte
}
```
*Batch size: 2 + 2 + 13 = 17 bytes.*

**ConnectionQualityUpdate** (R-U batch, 1-byte body; **5 bytes in batch**; 11 bytes standalone):
```
ConnectionQualityUpdate {
    bool rhythmCompensationActive; // 1 byte
}
```
*Batch size: 2 + 2 + 1 = 5 bytes. Sent only to the affected client on OWL threshold crossings. Never broadcast zone-wide.*

**CycleTimerBroadcast** (U-U CycleBroadcast packet, 6-byte body; **10 bytes in batch**):
```
CycleTimerBroadcast {
    EntityID entityId;   // 4 bytes
    ushort   cycleTimer; // 2 bytes — normalized fraction (0–10,000)
}
```
*Batch size: 2 + 2 + 6 = 10 bytes.*

**EntityPositionUpdate** (U-U Position packet, 10-byte body; **14 bytes in batch**):
```
EntityPositionUpdate {
    EntityID entityId; // 4 bytes
    short    posX;     // 2 bytes — fixed-point (×100, cm)
    short    posY;     // 2 bytes
    short    posZ;     // 2 bytes
}
```
*Batch size: 2 + 2 + 10 = 14 bytes.*

**PartyMemberHealthUpdate** (R-U batch, 20-byte body; **24 bytes in batch**):
```
PartyMemberHealthUpdate {
    EntityID entityId;  // 4 bytes — the party member's entity
    int      currentHP; // 4 bytes — current HP (FloorToInt per CR-NET-7.2)
    int      maxHP;     // 4 bytes — max HP (required to render HP bar fraction in HUD)
    int      currentMP; // 4 bytes — current MP
    int      maxMP;     // 4 bytes — max MP (required to render MP bar fraction in HUD)
}
```
*Batch size: 2 + 2 + 20 = 24 bytes. **Moved from U-U Position packet to R-U batch** (Pillar 3 fellowship-critical — reliable delivery required, per `networking-message-criticality.md` MCR-2). Sent per-client about that client's party members only — not zone-wide. See `networking-channel-contract.md` CCR-3. `maxHP`/`maxMP` are required (not cached separately) because either may change mid-session via level-up stat grants or buff effects.*

**AutoFaceEvent** (R-U batch, 18-byte body; **22 bytes in batch**):
```
AutoFaceEvent {
    EntityID entityId; // 4 bytes
    short    rotX;     // 2 bytes — fixed-point Quaternion (×32,767)
    short    rotY;     // 2 bytes
    short    rotZ;     // 2 bytes
    short    rotW;     // 2 bytes
    short    dirX;     // 2 bytes — fixed-point unit direction (×32,767)
    short    dirY;     // 2 bytes
    short    dirZ;     // 2 bytes
}
```
*Batch size: 2 + 2 + 18 = 22 bytes. Encoding rules per CR-NET-7.2 (Quaternion + direction vector guards apply).*

**SkillUsedNotification** (R-U batch, 4-byte body; **8 bytes in batch**):
```
SkillUsedNotification {
    EntityID attackerEntityId; // 4 bytes
}
```
*Batch size: 2 + 2 + 4 = 8 bytes.*

**LootBidUpdate** (R-U batch, 12-byte body; **16 bytes in batch**):
```
LootBidUpdate {
    GroundItemID groundItemId; // 4 bytes — identifies the active auction
    CharacterID  bidderId;     // 4 bytes — party member who submitted this bid
    uint         bidAmount;    // 4 bytes — gold amount of this bid
}
```
*Batch size: 2 + 2 + 12 = 16 bytes. Sent to all party members eligible for the auction (party size ≥ 2) when a valid `LootBidRequest` passes server validation. Invalid bids (below floor, post-`windowCloseTick`, or from non-party members) are rejected silently — no `LootBidUpdate` emitted. See CR-LT-8 in `loot-table-system.md`. Excluded from F-NET-1 bandwidth scenarios — event-driven, only present during active auctions.*

---

#### Priority-Path Messages (R-OD, standalone)

**EnhancementAttemptRequest** (R-OD, client → server, 12-byte body; 26 bytes standalone):
```
EnhancementAttemptRequest {
    EntityID entityId;  // 4 bytes — the requesting player's entity
    ItemID   itemId;    // 4 bytes — the item being enhanced
    uint     requestId; // 4 bytes — client-generated monotonically increasing ID
}
```

**EnhancementRequestReceived** (R-OD, server → client, 8-byte body; 18 bytes standalone):
```
EnhancementRequestReceived {
    EntityID entityId; // 4 bytes — player whose enhancement request was acknowledged
    ItemID   itemId;   // 4 bytes — the item being enhanced
}
```

**EnhancementOutcomeBroadcast** (R-OD, server → zone clients, body TBD — pending Enhancement System GDD):
```
EnhancementOutcomeBroadcast {
    // Full schema pending Enhancement System GDD.
    // Minimum expected fields: EntityID (4B), ItemID (4B), outcomeType (1B — success/fail/destroy),
    //   resultEnhancementLevel (1B), itemDestroyed (bool, 1B).
    // Sent to all zone clients via R-OD — zone-wide broadcast is the Pillar 3 social signal
    //   (high-enhancement success/failure visible to the entire zone).
    // Exempt from PRIORITY_PATH_CAP (CR-NET-7.7 enhancement-path exemption).
}
```
*Exempt from priority-path cap per CR-NET-7.7 tie-break rule B-NP-6. `MessageTypeID` assigned in Networking ADR.*

---

**KillEvent** (R-OD, priority path, variable body; **maximum 40-byte body**, minimum 16-byte body; maximum 50 bytes standalone):
```
KillEvent {
    EntityID   killerEntityId; // 4 bytes
    EntityID   targetEntityId; // 4 bytes
    string     targetName;     // ushort(2) + UTF-8 actual bytes; max 24 bytes UTF-8 = 26 bytes max; min 2 bytes (empty name)
    int        finalDamage;    // 4 bytes
    bool       isCrit;         // 1 byte
    DamageType damageType;     // 1 byte
}
```
*Variable-length field: wire size depends on `targetName` UTF-8 byte count. Maximum 40-byte body at max name; minimum 16-byte body at empty name. String byte cap: 24 UTF-8 bytes (not 24 characters — multi-byte characters may yield fewer than 24 glyphs). Standalone maximum: 10 + 40 = 50 bytes (within 512-byte body budget).*

**GhostPromotionEvent** (R-OD, server → all zone clients, 4-byte body; 14 bytes standalone):
```
GhostPromotionEvent {
    EntityID entityId; // 4 bytes — the entity entering ghost state (IsGhost = true)
}
```
*Emitted once per `Connected → Disconnected_SessionActive` transition (CR-GH-2, networking-ghost-session.md). Must arrive at all zone clients before any tick-rate message (EntityHealthUpdate, CycleTimerBroadcast) referencing this entity's `IsGhost` flag.*

**GhostExpiredEvent** (R-OD, server → all zone clients, 5-byte body; 15 bytes standalone):
```
GhostExpiredEvent {
    EntityID           entityId; // 4 bytes — the ghost entity being removed from the zone
    GhostExpiredReason reason;   // 1 byte — see GhostExpiredReason enum
}
```
*Emitted by CR-GH-10 step 7 with a caller-supplied reason. `GhostTtlExpired = 0` when the session TTL elapses without reconnect; `GhostDismissed = 1` when a party member triggers voluntary dismissal (CR-GH-12); `GhostDeath = 2` when ghost HP reaches zero during the ghost period (CR-GH-6, CGS-5). Receivers must range-check `reason` before casting — unknown bytes substitute `GhostTtlExpired = 0` and continue.*

**GhostDismissRequest** (R-OD, client → server, 8-byte body; 22 bytes standalone):
```
GhostDismissRequest {
    EntityID ghostEntityId; // 4 bytes — the ghost entity to dismiss
    PartyID  partyId;       // 4 bytes — server validates sender is a member of this party
}
```
*Standalone size: 10 (envelope) + 4 (SenderEntityID per CR-NET-7.1 C→S extension) + 8 (body) = 22 bytes. Server validates: (1) sender's session is in a zone containing the ghost entity; (2) sender is a member of the party identified by `partyId`; (3) ghost session is in `Disconnected_SessionActive`. Validation failure: server discards request and logs a `GhostDismissValidationFailed` anomaly — no error response is sent to the client. Duplicate requests (transport-layer retransmit) are idempotent: ignored if ghost cleanup is already in progress. See CR-GH-12, networking-ghost-session.md.*

**SetTarget** (R-OD, client → server, 4-byte body; 18 bytes standalone):
```
SetTarget {
    EntityID targetEntityId;  // 4 bytes. EntityID = 0 = deselect current target.
}
```
*Standalone size: 10 (envelope) + 4 (SenderEntityID per CR-NET-7.1 C→S extension) + 4 (body) = 18 bytes. Server validates: `targetEntityId` must be a valid EntityID in the current zone, or 0 (deselect). Self-target (`targetEntityId == client.ownEntityId`) rejected silently — logs `SelfTargetAttempt` advisory anomaly, target slot unchanged. Invalid EntityIDs discarded silently — logs `InvalidTargetEntityId` advisory anomaly. On valid target change: server updates the EntityHealthUpdate relevance set atomically before next batch flush. See RFR-3, RFR-3a (networking-relevance-filter.md).*

**PlayerJoinedZone** (R-OD, server → all zone clients, variable body; maximum 40-byte body):
```
PlayerJoinedZone {
    EntityID entityId;      // 4 bytes
    string   characterName; // ushort(2) + UTF-8 actual bytes; max 24 bytes UTF-8 = 26 bytes max
    int      level;         // 4 bytes
    short    posX;          // 2 bytes — fixed-point (×100, cm)
    short    posY;          // 2 bytes
    short    posZ;          // 2 bytes
}
```
*Must arrive before any other messages referencing this `EntityID`. Variable-length: maximum 40-byte body.*

**PlayerLeftZone** (R-OD, server → all remaining clients, 5-byte body; 15 bytes standalone):
```
PlayerLeftZone {
    EntityID       entityId;       // 4 bytes — the departing entity
    DisconnectType disconnectType; // 1 byte
}
```

**ZoneSessionEnded** (R-OD, server → client, 9-byte body; 19 bytes standalone):
```
ZoneSessionEnded {
    ZoneID           zoneId;             // 4 bytes
    DisconnectReason reason;             // 1 byte
    int              gracePeriodSeconds; // 4 bytes — 0 for immediate; >0 for planned shutdown
}
```

**SessionReady** (R-OD, server → owning client, minimum 18-byte body — additional fields pending):
```
SessionReady {
    EntityID    entityId;    // 4 bytes
    CharacterID characterId; // 4 bytes
    ZoneID      currentZone; // 4 bytes
    short       posX;        // 2 bytes — initial spawn position
    short       posY;        // 2 bytes
    short       posZ;        // 2 bytes
    // Additional fields pending Character Persistence GDD (OQ-NC-SER-2):
    // currentHP, maxHP, currentMP, maxMP, level, heldFreePoints, goldBalance, goldVersion
}
```
*Zone-entry dual-gate: client must receive BOTH `SessionReady` AND a complete `ZoneStateSnapshot` reassembly before rendering or sending RPCs.*

**SessionHandshake** (R-OD, client → server — schema pending):
```
SessionHandshake {
    // Full schema pending Character Persistence GDD and Authentication ADR (OQ-NC-SER-2)
    // Fields: session token, CharacterID, target ZoneID
    // Wire size: TBD
}
```

---

#### Death & Respawn Messages

All Death & Respawn messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope. `EntityDied` and `EntityRespawned` are zone-wide broadcasts; `DeathStateEntered` and `RespawnConfirmed` are sent to the dying/respawning player's client only. Defined by `death-and-respawn.md` CR-DR-6, CR-DR-7, CR-DR-13.

---

**EntityDied** (R-OD, server → all zone clients, 4-byte body; 14 bytes standalone):
```
EntityDied {
    EntityID entityId; // 4 bytes — the entity that died; receivers remove nameplate and apply greyscale material
}
```
*Not sent for ghost deaths (`EntityState.isGhost == true`) — CR-DR-Ghost. Zone-wide broadcast to all remaining zone clients. Drives death VFX, nameplate removal, and party HP bar zero on all clients. See `networking-relevance-filter.md` RFR-7 for delivery scope definition.*

---

**DeathStateEntered** (R-OD, server → dying player's client only, 5-byte body; 15 bytes standalone):
```
DeathStateEntered {
    EntityID entityId;            // 4 bytes — dying entity (receiver asserts entityId == localPlayerEntityId)
    byte     respawnTimerSeconds; // 1 byte — display-only countdown seed; range [5, 10]; Mathf.RoundToInt(RESPAWN_DELAY_SECONDS)
}
```
*Sent to the dying player's client only — not zone-wide. `respawnTimerSeconds` is display-only: the client drives the countdown from this seed value; the server's authoritative respawn is the `DeathTick + RESPAWN_DELAY_TICKS` comparison. Client asserts `entityId == localPlayerEntityId`; mismatch logs `DeathStateDirectionViolation` anomaly and suppresses UI. Countdown gap: when client counter reaches 0 before `RespawnConfirmed` arrives, overlay shows "Respawning…" rather than freezing at "0s".*

---

**EntityRespawned** (R-OD, server → all zone clients, 10-byte body; 20 bytes standalone):
```
EntityRespawned {
    EntityID entityId; // 4 bytes — the entity that has respawned
    short    posX;     // 2 bytes — fixed-point (×100, cm) — authoritative town respawn position
    short    posY;     // 2 bytes
    short    posZ;     // 2 bytes
}
```
*Zone-wide broadcast to all zone clients. Drives greyscale removal, warm gold pulse ring VFX, nameplate restore, and party HP bar refresh. Position fields carry the authoritative town respawn point; CR-NET-7.2 position encoding and overflow guards apply. The respawning player's client also receives `RespawnConfirmed` to trigger the camera fade sequence. Both messages are enqueued in the same tick as T-3 completion (CR-DR-13) and flushed at the end of that tick's network step.*

---

**RespawnConfirmed** (R-OD, server → respawning player's client only, 4-byte body; 14 bytes standalone):
```
RespawnConfirmed {
    EntityID entityId; // 4 bytes — confirms successful respawn (receiver asserts entityId == localPlayerEntityId)
}
```
*Sent to the respawning player's client only — not zone-wide. Dismisses the death countdown overlay. Client begins the 0.3s camera fade-to-black sequence on receipt (`death-and-respawn.md` Event 4); camera fade must complete before touch input is re-enabled. Client asserts `entityId == localPlayerEntityId`; mismatch logs `RespawnConfirmedDirectionViolation` anomaly and suppresses the UI transition. Sent in the same tick as `EntityRespawned` (CR-DR-13).*

---

#### Zone Instancing Messages

Zone Instancing messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). Defined by `zone-instancing.md` CR-ZI-7 and CR-ZI-9. Resolves OQ-ZI-1 and OQ-ZI-3.

---

**ZoneFullResponse** (R-OD, server → client, 6-byte body; 16 bytes standalone):
```
ZoneFullResponse {
    ZoneID             zoneId;           // 4 bytes — zone the client attempted to join or retransmit against
    JoinRejectedReason reason;           // 1 byte — see JoinRejectedReason enum
    byte               retryAfterSeconds; // 1 byte — advisory backoff before client retries; 0 when reason is not ZoneFull; server recommendation only (not enforced); range [0, 255]
}
```
*Sent when a zone entry attempt is rejected. Three cases: (1) `ZoneFull` — zone at `MAX_PLAYERS_PER_ZONE` capacity (CR-ZI-8 step 4); `retryAfterSeconds` carries the fill-first advisory backoff (proposed default: 30). (2) `LoadCharacterFailed` — Character Persistence returned an error code for `LoadCharacter` (CR-ZI-8 step 3); `retryAfterSeconds = 0`. (3) `ZoneDoesNotExist` — zone is Closed or never existed, returned on `ZoneSnapshotRequest` retransmit when the zone has since closed (EC-ZI-6), or when zone initialization fails (EC-ZI-8); `retryAfterSeconds = 0`. Client must display a retry prompt — the server does not queue the player. `retryAfterSeconds` is advisory: client may retry sooner if the user requests it.*

---

**ZoneSnapshotRequest** (R-OD, client → server, 8-byte body; 22 bytes standalone):
```
ZoneSnapshotRequest {
    ZoneID zoneId;          // 4 bytes — zone to retransmit snapshot for
    uint   snapshotVersion; // 4 bytes — the version the client was assembling when reassembly timed out
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID per CR-NET-7.1 C→S extension) + 8 (body) = 22 bytes. Sent by the client when `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` expires before all fragments arrive (CR-ZI-9). Server responds with a fresh snapshot of current zone state keyed to the current tick — NOT the requested `snapshotVersion`. Client uses `snapshotVersion` to discard lingering old-version fragments (stale-discard on `fragmentIndex=0` arrival with a newer version). Rate-limited server-side: at most 1 retransmit per `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` per connection (CR-ZI-9); excess requests silently dropped and logged as `SnapshotRequestRateLimited`. If the zone has closed since the request was generated: server responds with `ZoneFullResponse(ZoneDoesNotExist=1)`. Forged or future `snapshotVersion` values are ignored — server emits a fresh snapshot regardless (EC-ZI-7).*

---

#### Loot System Messages

All loot messages are R-OD (priority path) except `LootBidUpdate` (R-U batch — see Tick-Rate Messages above). Wire schemas below are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). `GroundItemID` type: `uint` (4 bytes), `0` = Invalid (CR-NET-7.9).

---

**GroundItemSpawned** (R-OD, server → client(s), 20-byte body; 30 bytes standalone):
```
GroundItemSpawned {
    GroundItemID groundItemId;  // 4 bytes — unique ID for this ground item instance
    ItemID       itemId;        // 4 bytes
    GearTier     gearTier;      // 1 byte — enum : byte; drives beacon color and tier classification
    bool         isAuction;     // 1 byte — true when item enters Auctioning state (Steel/DarkSteel, party ≥ 2)
    short        posX;          // 2 bytes — fixed-point (×100, cm) per CR-NET-7.2
    short        posY;          // 2 bytes
    short        posZ;          // 2 bytes
    uint         expiryTick;    // 4 bytes — server tick at which item despawns (CR-LT-12)
}
```
*Delivery: sent to the assigned character only for common drops (`isAuction = false`); sent to all current party members for rare drops (`isAuction = true`). `GearTier` range-check required on receipt per CR-NET-7.4. Position encoding guards per CR-NET-7.2 apply.*

---

**GroundItemAssigned** (R-OD, server → assigned character, 8-byte body; 18 bytes standalone):
```
GroundItemAssigned {
    GroundItemID groundItemId;  // 4 bytes
    CharacterID  assignedTo;    // 4 bytes — must not be CharacterID.Invalid(0); assert per CR-NET-7.3
}
```
*Sent when an item's assignment changes after initial spawn — specifically for CR-LT-10 zero-bid auction fallback to round-robin. Common drops do not emit a separate `GroundItemAssigned` — the `GroundItemSpawned` sent exclusively to the assigned character implies assignment.*

---

**GroundItemDespawned** (R-OD, server → all zone clients, 4-byte body; 14 bytes standalone):
```
GroundItemDespawned {
    GroundItemID groundItemId;  // 4 bytes
}
```
*Sent to all zone clients when any ground item reaches `expiryTick` (CR-LT-12) or during zone teardown. Client removes the ground item's visual beacon regardless of current item state.*

---

**AuctionResolved** (R-OD, server → all party members, 13-byte body; 23 bytes standalone):
```
AuctionResolved {
    GroundItemID groundItemId;         // 4 bytes
    CharacterID  winnerCharacterId;    // 4 bytes — CharacterID.Invalid(0) when isRoundRobinFallback = true
    uint         goldPerMember;        // 4 bytes — pool share each party member receives; 0 when isRoundRobinFallback = true
    bool         isRoundRobinFallback; // 1 byte — true when zero valid bids or all bidders failed TrySpendGold (CR-LT-10)
}
```
*Sent to all party members when `windowCloseTick` is reached or when `expiryTick` fires during `Auctioning` state. When `isRoundRobinFallback = true`, `winnerCharacterId` must be treated as `CharacterID.Invalid` — clients must not attempt to resolve the winner display from this field.*

---

**LootBidRequest** (R-OD, client → server, 8-byte body; 22 bytes standalone):
```
LootBidRequest {
    GroundItemID groundItemId;  // 4 bytes — identifies the active auction
    uint         bidAmount;     // 4 bytes — gold amount; must be ≥ itemDef.SellPriceGold for the item's tier
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 8 (body) = 22 bytes. Server validates: (1) `bidAmount ≥ itemDef.SellPriceGold`; (2) sender is an eligible party member; (3) `receivedTick ≤ windowCloseTick`. All three must pass for a `LootBidUpdate` to be emitted. Any validation failure: silent discard, no error response sent. See CR-LT-8 and AC-LT-11.*

---

**BagFullPickupBlocked** (R-OD, server → assigned character, variable body; 14–40 bytes; 24–50 bytes standalone):
```
BagFullPickupBlocked {
    GroundItemID groundItemId;   // 4 bytes
    ItemID       itemId;         // 4 bytes
    string       displayName;    // ushort(2) + UTF-8 bytes; max 24 UTF-8 bytes = 26 bytes max
    uint         remainingTicks; // 4 bytes — ticks until expiryTick at time of emission (informational)
}
```
*Variable-length: `displayName` string field. Minimum body (empty name): 14 bytes. Maximum body (24 UTF-8 bytes): 40 bytes. `displayName` cap: 24 UTF-8 bytes (not characters — multi-byte sequences count). Sent when `PickupResult.Fail` (bag full) fires within pickup radius (CR-LT-13.2). Client renders the discard modal.*

---

**GroundItemExpiryWarning** (R-OD, server → assigned character, variable body; 14–40 bytes; 24–50 bytes standalone):
```
GroundItemExpiryWarning {
    GroundItemID groundItemId;   // 4 bytes
    ItemID       itemId;         // 4 bytes
    string       displayName;    // ushort(2) + UTF-8 bytes; max 24 UTF-8 bytes = 26 bytes max
    uint         remainingTicks; // 4 bytes — should equal EXPIRY_WARNING_TICKS at emission
}
```
*Identical schema to `BagFullPickupBlocked`; distinguished by `MessageTypeID`. Sent at each deadline threshold crossing: once when `expiryTick − currentTick == EXPIRY_WARNING_TICKS`, and again after any CR-LT-13.1 TTL extension that creates a new threshold crossing. See CR-LT-13.3.*

---

**ClientBackgrounded** (R-OD, client → server, no body; 14 bytes standalone):
```
ClientBackgrounded {
    // No body. Wire size = 14 bytes (10B envelope + 4B SenderEntityID per CR-NET-7.1).
    // Server records ServerTickNumber from envelope as backgroundedAtTick for the session (CR-LT-13.1).
    // Client sends on iOS applicationDidEnterBackground / Android onStop.
}
```

---

**ClientForegrounded** (R-OD, client → server, no body; 14 bytes standalone):
```
ClientForegrounded {
    // No body. Wire size = 14 bytes (10B envelope + 4B SenderEntityID per CR-NET-7.1).
    // Server computes pausedTicks = foregroundTick − backgroundedAtTick on receipt and extends
    // expiryTick for all bag-full assigned items per CR-LT-13.1.
    // Client sends on iOS applicationWillEnterForeground / Android onStart.
}
```

---

#### Party System Messages

All party messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). `PartyID` and `CharacterID` types: `uint` (4 bytes), `0` = Invalid (CR-NET-7.9). `EntityID`: `uint` (4 bytes), `0` = not in current zone.

`PartyStateUpdate` is the authoritative full-state snapshot of a party, sent on **any** membership change (join, leave, disconnect, leader transfer, XP-eligibility change, rrNextIndex advance). It is never a delta. `sequenceId` is a per-party monotonic counter (starting at 1, server-assigned) used for stale-discard; apply `IsNewerVersion` (CR-NET-7.5) to discard out-of-order deliveries. The `members[4]` array is fixed-size (`MAX_PARTY_SIZE = 4`); empty slots have `characterId = 0`.

---

**`PartyMemberSlot` inline struct (11 bytes, used in `PartyStateUpdate`):**
```
PartyMemberSlot {
    CharacterID       characterId; // 4 bytes — CharacterID(0) = empty slot
    EntityID          entityId;    // 4 bytes — EntityID(0) = not in current zone
    byte              level;       // 1 byte — 0 if empty slot
    PartyMemberStatus status;      // 1 byte — see PartyMemberStatus enum
    bool              xpEligible;  // 1 byte — true if member passes CR-PS-5 eligibility at last state update
}
```

---

**PartyStateUpdate** (R-OD, server → all party members, 56-byte body; 66 bytes standalone):
```
PartyStateUpdate {
    PartyID         partyId;      // 4 bytes
    uint            sequenceId;   // 4 bytes — per-party monotonic version; stale-discard via IsNewerVersion
    bool            isSoloParty;  // 1 byte — true when party has exactly 1 member (CR-PS-1)
    byte            memberCount;  // 1 byte — count of slots with characterId != 0 (1 for solo)
    byte            leaderIndex;  // 1 byte — index into members[] of current leader (CR-PS-8)
    byte            rrNextIndex;  // 1 byte — round-robin cursor (CR-PS-7)
    PartyMemberSlot members[4];   // 44 bytes — fixed MAX_PARTY_SIZE slots; empty slots: characterId=0
}
```
*Sent to all online party members (status == Online) on any state change. Ghost and OutOfZone members do NOT receive this message — they are updated on reconnect or zone re-entry via `SessionReady`. The client must apply `IsNewerVersion(incoming.sequenceId, cached.sequenceId)` before updating cached party state. Client must range-check `leaderIndex` and `rrNextIndex` against `memberCount` before use.*

---

**PartyDisbanded** (R-OD, server → all party members, 4-byte body; 14 bytes standalone):
```
PartyDisbanded {
    PartyID partyId; // 4 bytes — identifies the disbanded party
}
```
*Sent when the leader calls disband (CR-PS-9) or when the last member leaves. Client collapses the party panel and shows the system message. Online members receive this before their solo `PartyStateUpdate`.*

---

**PartyInviteRequest** (R-OD, client → server, 4-byte body; 18 bytes standalone):
```
PartyInviteRequest {
    CharacterID targetCharacterId; // 4 bytes — character to invite
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 4 (body) = 18 bytes. Server validates: (1) sender has an active session; (2) sender's party is not full (`MemberCount < MAX_PARTY_SIZE`); (3) target is not already in a party; (4) level range gate passes (CR-PS-3). Validation failure: server discards silently and logs — no error response at MVP. One pending invite per recipient at a time; further invites queue (CR-PS-4).*

---

**PartyInviteReceived** (R-OD, server → target client, variable body; 15–39 bytes; 25–49 bytes standalone):
```
PartyInviteReceived {
    CharacterID inviterCharacterId; // 4 bytes — identifies the specific invite (for response routing)
    EntityID    inviterEntityId;    // 4 bytes — inviting player's zone entity
    string      inviterName;        // ushort(2) + UTF-8 bytes; max 24 UTF-8 bytes = 26 bytes max
    byte        inviterLevel;       // 1 byte
    uint        inviteExpiryTick;   // 4 bytes — tick at which this invite expires (60s window, CR-PS-4)
}
```
*Variable-length: minimum body 15 bytes (empty name), maximum 39 bytes (24 UTF-8 bytes). Standalone range: 25–49 bytes. Server re-sends once after 5 server-side seconds if no `PartyInviteResponse` is received (1 retry only, CR-PS-4). Both sends are identical messages — client deduplicates by `inviterCharacterId`. Client renders the invite banner with `inviterName`, `inviterLevel`, Accept/Decline buttons, and a countdown to `inviteExpiryTick`.*

---

**PartyInviteResponse** (R-OD, client → server, 5-byte body; 19 bytes standalone):
```
PartyInviteResponse {
    CharacterID inviterCharacterId; // 4 bytes — identifies which invite this responds to
    bool        accepted;           // 1 byte — true = Accept, false = Decline
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 5 (body) = 19 bytes. Server validates: (1) invite identified by `inviterCharacterId` is still pending (not expired or already responded to); (2) if `accepted=true`, party capacity still available and level range still valid. On valid accept: party state updated; `PartyStateUpdate` broadcast to all members. On decline or expired: invite cleared; next queued invite (if any) delivered. On invalid: silently discarded.*

---

#### Inventory System Messages

All inventory messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). `byte` slot indices are in range `[0, INVENTORY_SLOT_COUNT − 1]` (i.e., 0–19); the server must range-validate before accessing the slot array. `ItemID`: `uint` (4 bytes), `0` = `ItemID.Invalid` (empty slot).

---

**DiscardRequest** (R-OD, client → server, 5-byte body; 19 bytes standalone):
```
DiscardRequest {
    byte slotIndex; // 1 byte — inventory slot (0–19); range-checked server-side
    int  quantity;  // 4 bytes — units to discard; must satisfy 1 ≤ quantity ≤ current slot Quantity
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 5 (body) = 19 bytes. Server validates: (1) `slotIndex` in [0, 19]; (2) slot is not empty; (3) slot is not locked; (4) `1 ≤ quantity ≤ currentSlotQuantity`. Any validation failure: respond with `DiscardResult(success=false, reason=<specific>)`.*

---

**DiscardResult** (R-OD, server → client, 3-byte body; 13 bytes standalone):
```
DiscardResult {
    byte             slotIndex; // 1 byte — echoes the requested slot index
    bool             success;   // 1 byte
    DiscardFailReason reason;   // 1 byte — DiscardFailReason.None (0) when success=true
}
```
*Standalone: 10 + 3 = 13 bytes. On success: `InventoryChangedEvent` is also emitted server-internally (per inventory-system.md Rule 6). On failure: no slot mutation occurs and no `InventoryChangedEvent` fires.*

---

**MoveRequest** (R-OD, client → server, 2-byte body; 16 bytes standalone):
```
MoveRequest {
    byte fromSlot; // 1 byte — source slot index (0–19)
    byte toSlot;   // 1 byte — destination slot index (0–19)
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 2 (body) = 16 bytes. If `fromSlot == toSlot`: server responds with `MoveResult(success=true, reason=None)` with no mutation (per inventory-system.md edge case). Server validates: (1) both indices in [0, 19]; (2) source slot not locked; (3) destination slot not locked. Merge or swap semantics per inventory-system.md Rule 7.*

---

**MoveResult** (R-OD, server → client, 20-byte body; 30 bytes standalone):
```
MoveResult {
    bool          success;           // 1 byte
    MoveFailReason reason;           // 1 byte — MoveFailReason.None (0) when success=true
    byte          fromSlot;          // 1 byte — echoes the requested fromSlot index
    ItemID        fromSlotItemId;    // 4 bytes — authoritative post-operation state (ItemID.Invalid if empty)
    int           fromSlotQuantity;  // 4 bytes — 0 if slot is empty
    byte          toSlot;            // 1 byte — echoes the requested toSlot index
    ItemID        toSlotItemId;      // 4 bytes — authoritative post-operation state
    int           toSlotQuantity;    // 4 bytes — 0 if slot is empty
}
```
*Standalone: 10 + 20 = 30 bytes. Always carries authoritative post-operation slot states for both affected slots — on success (new states after merge/swap) and on failure (unchanged states, matching the pre-operation values). Client uses these to commit or roll back optimistic UI rendering without a separate re-fetch. On success: `InventoryChangedEvent` also fires server-internally.*

---

**InventoryFullNotification** (R-OD, server → client, no body; 10 bytes standalone):
```
InventoryFullNotification {
    // No body fields. Wire size = 10-byte envelope only.
    // Sent when a pickup fails due to a full bag (inventory-system.md Rule 4).
    // Rate-limited: at most one per character per INVENTORY_FULL_NOTIF_WINDOW_SECONDS (30s
    // deduplication window). The HUD bag-full indicator remains active independently
    // of the notification suppression; this message drives the toast component only.
    // Window resets when the character makes a successful pickup (server-side).
}
```

---

#### Equipment System Messages

All equipment messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). `byte gearSlot` values are in range `[0, 6]` matching the `GearSlot` enum (Weapon=0 through Necklace=6); the server must range-validate before accessing the slot array. `uint itemId` = 0 means `ItemID.Invalid` (unequip the current slot occupant).

---

**EquipRequest** (R-OD, client → server, 9-byte body; 23 bytes standalone):
```
EquipRequest {
    uint requestId;  // 4 bytes — monotonically increasing per-client counter; server deduplicates
                     //   on retransmit using requestId + SenderEntityID pair
    byte gearSlot;   // 1 byte — GearSlot enum (0–6); range-checked server-side before array access
    uint itemId;     // 4 bytes — ItemID to equip (must be in player's inventory);
                     //   itemId=0 (ItemID.Invalid) = unequip the current occupant of gearSlot
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 9 (body) = 23 bytes. Server validates: (1) gearSlot in [0, 6]; (2) itemId present in the player's inventory (if ≠ 0); (3) item's GearSlot matches target slot; (4) stat gate met (GetBaseStat); (5) inventory has a free slot if a swap is required. Duplicate requests (transport-layer retransmit) detected by requestId + SenderEntityID — server returns EquipResult from the original processing, does not re-execute.*

---

**EquipResult** (R-OD, server → client, 11-byte body; 21 bytes standalone):
```
EquipResult {
    uint requestId;       // 4 bytes — correlates to the originating EquipRequest
    bool success;         // 1 byte  — true when operation completed without error
    byte failReason;      // 1 byte  — EquipFailReason (0 = None when success=true)
    byte affectedSlot;    // 1 byte  — GearSlot that was operated on
    uint slotItemId;      // 4 bytes — authoritative ItemID now occupying affectedSlot after the
                          //   operation (0 = slot is empty); client must apply this as ground truth
}
```
*Standalone: 10 + 11 = 21 bytes. On success: `slotItemId` reflects the newly equipped item. On unequip success: `slotItemId = 0`. On failure: `slotItemId` reflects the unchanged slot state (old item remains). **Stat delivery:** On success, server also emits `StatSnapshotEvent` (defined in Leveling System GDD) to the equipping player. This is the authoritative stat delivery path per CR-NET-4 — `EquipResult` does not carry stat values. For `failReason = StatRequirementNotMet`: the client displays the shortfall using its locally cached stat values (base stat from last StatSnapshotEvent); the server does not echo required/actual stat values in this message. The `StatRequirementNotMet` error message must display base stat, not effective stat — document this in Inventory UI GDD.*

---

**AppearanceChangedEvent** (R-OD, server → all zone clients, 5-byte body; 15 bytes standalone):
```
AppearanceChangedEvent {
    uint entityId;                  // 4 bytes — entity whose equipment appearance changed
    byte equipmentAppearanceFlags;  // 1 byte  — authoritative new flags byte (Equipment System GDD CR-EQS-11)
}
```
*Standalone: 10 + 5 = 15 bytes. R-OD guarantees delivery — no version field required (CCR-4: R-OD is exactly-once in-order). Server emits this event after every successful equip, unequip, or swap operation that changes the flags byte. Future: must also emit after any Enhancement System outcome that changes `PrestigeBand` bits (once Enhancement System GDD defines the callback interface). **Zone join:** Initial appearance flags are carried in `EntityState.equipmentAppearanceFlags` within `ZoneStateSnapshotFragment` — `AppearanceChangedEvent` covers only mid-session changes. Clients must update their local appearance render for `entityId` on receipt regardless of the entity's current combat or movement state.*

---

#### NPC Shop System Messages

All NPC Shop messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). Per ADR-001 (`docs/architecture/ADR-001-purchase-transaction-integrity.md`), `BuyRequest` and `SellRequest` carry `requestId: uint` — the server deduplicates on `(SenderEntityID, requestId)` within `SESSION_TTL_SECONDS`. `BuyResult` and `SellResult` are success-only; all failure cases use dedicated rejection messages. Rejection messages for `BuyRequest`/`SellRequest` carry `requestId` for client correlation. `CloseNPCInteraction` is fire-and-forget (no server response). Defined in `design/gdd/npc-shop.md`.

---

**OpenNPCInteraction** (R-OD, client → server, 4-byte body; 18 bytes standalone):
```
OpenNPCInteraction {
    uint npcId; // 4 bytes — server validates npcId is a shop NPC present in the town hub zone
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 4 (body) = 18 bytes. Server responds with `NPCInteractionOpened` (success) or `RejectedNotInTownHub` (failure). If an existing NPC session is active (any NPC type), the server clears it before setting the new session — Enhancement System is notified before clearing (OQ-NS-6 / CR-SHOP-3 step 3).*

---

**NPCInteractionOpened** (R-OD, server → client, no body; 10 bytes standalone):
```
NPCInteractionOpened {
    // No body. Signals NPCInteractionActive = true for this character.
    // Wall-clock session lifetime: SESSION_TTL_SECONDS (300s) from receipt.
    // Client starts local countdown timer on receipt and opens shop window to Sell tab.
}
```
*Standalone: 10 bytes (envelope only).*

---

**CloseNPCInteraction** (R-OD, client → server, no body; 14 bytes standalone):
```
CloseNPCInteraction {
    // No body. Voluntary session close.
    // Fire-and-forget — no server response. Server clears NPCInteractionActive on receipt.
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) = 14 bytes. Session also closes on zone change, SESSION_TTL expiry, or a new OpenNPCInteraction (CR-SHOP-4).*

---

**BuyRequest** (R-OD, client → server, 9-byte body; 23 bytes standalone):
```
BuyRequest {
    uint requestId; // 4 bytes — monotonically increasing per-session counter; reset to 0 at
                    //   OpenNPCInteraction. Server deduplicates on (SenderEntityID, messageType,
                    //   requestId) within SESSION_TTL_SECONDS — duplicate returns cached BuyResult
                    //   without re-executing (ADR-001 A1 idempotency).
    uint itemId;    // 4 bytes — ItemID of catalog item to purchase
    byte quantity;  // 1 byte  — units to purchase; server enforces 1 ≤ quantity ≤ QUANTITY_SELECTOR_CAP (99)
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 9 (body) = 23 bytes. Server validation order (CR-SHOP-5): (1) NPCInteractionActive; (2) quantity ≥ 1; (3) itemId in catalog; (4) TrySpendGold; (5) PickupRequest. Rate-limited: 10 requests/sec per character (ADR-001) — excess returns `RejectedRateLimited`. Per ADR-001, server creates a `PendingPurchase` record before calling TrySpendGold.*

---

**BuyResult** (R-OD, server → client, 13-byte body; 23 bytes standalone):
```
BuyResult {
    uint requestId;      // 4 bytes — correlates to originating BuyRequest
    uint itemId;         // 4 bytes — purchased item (echoes BuyRequest.itemId)
    byte quantity;       // 1 byte  — purchased quantity (echoes BuyRequest.quantity)
    uint newGoldBalance; // 4 bytes — authoritative gold balance after debit; client must not
                         //   apply any balance update until BuyResult is received (no optimistic update).
                         //   Treat as consistent with any concurrent GoldSyncEvent.
}
```
*Standalone: 10 + 13 = 23 bytes. Success-only — sent only when purchase completes successfully. A concurrent `GoldSyncEvent` will also fire from the Currency System. On failure, the appropriate `RejectedXxx` message is sent instead.*

---

**SellRequest** (R-OD, client → server, 10-byte body; 24 bytes standalone):
```
SellRequest {
    uint requestId; // 4 bytes — monotonically increasing per-session counter; shares namespace
                    //   with BuyRequest requestId within the session. Same deduplication per ADR-001.
    byte slotIndex; // 1 byte  — inventory slot (0–19); server range-validates before slot access
    uint itemId;    // 4 bytes — ItemID the client believes occupies slotIndex (stale-render guard;
                    //   server returns RejectedItemMismatch if actual slot content differs)
    byte quantity;  // 1 byte  — units to sell; server enforces 1 ≤ quantity ≤ slotStackCount
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 10 (body) = 24 bytes. Server validation order (CR-SHOP-7): (1) NPCInteractionActive; (2) slotIndex ∈ [0, 19]; (3) slot holds itemId; (4) slot not locked; (5) SellPriceGold > 0; (6) quantity ∈ [1, slotStackCount]. Rate-limited per ADR-001.*

---

**SellResult** (R-OD, server → client, 17-byte body; 27 bytes standalone):
```
SellResult {
    uint requestId;      // 4 bytes — correlates to originating SellRequest
    uint itemId;         // 4 bytes — sold item (echoes SellRequest.itemId)
    byte quantitySold;   // 1 byte  — actual units removed from inventory slot
    uint goldEarned;     // 4 bytes — actual gold credited by AddGold; may be less than
                         //   SellPriceGold × quantitySold if player's balance was near GOLD_CAP
                         //   (Currency System clamps to GOLD_CAP — see npc-shop.md Edge Cases)
    uint newGoldBalance; // 4 bytes — authoritative gold balance after credit
}
```
*Standalone: 10 + 17 = 27 bytes. Success-only. A concurrent `GoldSyncEvent` will also fire. On failure, the appropriate `RejectedXxx` message is sent.*

---

**RejectedNotInTownHub** (R-OD, server → client, no body; 10 bytes standalone):
```
RejectedNotInTownHub {
    // No body. Response to OpenNPCInteraction when character is not in the town hub zone
    // (CR-SHOP-3 step 2). No requestId — OpenNPCInteraction carries none.
}
```
*Standalone: 10 bytes.*

---

**NPC Shop Rejection Messages — shared schema** (R-OD, server → client, 4-byte body; 14 bytes each):

The following 10 rejection messages share an identical wire schema. Each carries only a `requestId` to correlate with the rejected `BuyRequest` or `SellRequest`. The message type (dispatched by `MessageTypeID`) identifies the specific failure reason.

```
RejectedNoNPCSession        |
RejectedInvalidQuantity     |
RejectedItemNotInCatalog    |  {
RejectedInsufficientFunds   |      uint requestId; // 4 bytes — echoes BuyRequest.requestId or SellRequest.requestId
RejectedInventoryFull       |  }
RejectedRateLimited         |
RejectedInvalidSlot         |
RejectedItemMismatch        |
RejectedSlotLocked          |
RejectedUnsellable          |
```

*Standalone: 10 + 4 = 14 bytes each. The client uses `requestId` to release the pending transaction UI state (spinner dismissed, interaction re-enabled). See npc-shop.md CR-SHOP-5 and CR-SHOP-7 for the validation step each rejection maps to. `RejectedInventoryFull` indicates net gold effect is zero — compensating `AddGold(CompensatingRefund)` executed server-side before this message is sent (ADR-001).*

---

#### Consumable Use System Messages

All Consumable Use System messages are R-OD (priority path). Wire schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). Per ADR-001 (`docs/architecture/ADR-001-purchase-transaction-integrity.md`), `UseItemRequest` carries `requestId: uint` — the server deduplicates on `(SenderEntityID, messageType, requestId)` within `SESSION_TTL_SECONDS`. The `requestId` counter persists across same-session reconnect within SESSION_TTL_SECONDS; on reconnect, the client seeds the counter from `SessionHandshake.lastSeenUseItemRequestId + 1` (CR-NET-6.4 amendment — OQ-CUS-1 resolved 2026-06-11). `UseItemResult` is success-only; all validation failures use `UseItemRejected`. `UseItemRejected` must arrive to reset the client-side predict state (EffectTypeCooldownRemaining reset to 0, hotbar slot un-greyed). **CR-NET-7.2 note:** `newResourceValue` in `UseItemResult` is transmitted as `int` (FloorToInt at the serialisation boundary — the CUS GDD's internal representation uses `float`; no float may appear in authoritative wire state). Defined in `design/gdd/consumable-use-system.md`.

---

**UseItemRequest** (R-OD, client → server, 9-byte body; 23 bytes standalone):
```
UseItemRequest {
    uint requestId; // 4 bytes — monotonically increasing per-session counter; independent of
                    //   NPC Shop requestId namespace. Reset policy:
                    //   • New session (post SESSION_TTL expiry or first zone entry): reset to 0.
                    //   • Same-session reconnect within SESSION_TTL_SECONDS: client seeds counter
                    //     from SessionHandshake.lastSeenUseItemRequestId + 1 (CR-NET-6.4 step 2).
                    //   Prevents collision with server dedup cache entries from before disconnect.
                    //   Server deduplicates on (SenderEntityID, messageType, requestId) within
                    //   SESSION_TTL_SECONDS per ADR-001 A1 — transport retransmit returns cached
                    //   UseItemResult without re-executing.
    uint itemId;    // 4 bytes — ItemID of the consumable; server validates HasItem(itemId) ≥ 1
    byte quantity;  // 1 byte  — MVP: always 1; server rejects quantity ≠ 1 with UseItemRejected
                    //   (ServerError reason). Field reserved for future multi-use extension.
}
```
*Standalone: 10 (envelope) + 4 (SenderEntityID) + 9 (body) = 23 bytes. Server validation order (CR-CUS-3 Step 4): (a) ZoneSessionState ≠ Dead && ≠ Respawning; (b) HasItem(itemId) ≥ 1; (c) server-side EffectType cooldown = 0. On any failure → `UseItemRejected`. On all checks pass: `ConsumeItem(itemId, 1)` → `ApplyRegen` or `ApplyManaRegen` → start server-side cooldown → `UseItemResult`. ADR-001 idempotency: server records pending-request keyed on (SenderEntityID, requestId) before execution; transport retransmit returns cached result. Rate-limiting: 10 req/sec per character (ADR-001 baseline). OQ-CUS-1 resolved 2026-06-11: counter persists across same-session reconnect; client seeds from SessionHandshake.lastSeenUseItemRequestId + 1 (see CR-NET-6.4 in networking-session.md).*

---

**UseItemResult** (R-OD, server → client, 16-byte body; 26 bytes standalone):
```
UseItemResult {
    uint requestId;            // 4 bytes — correlates to originating UseItemRequest
    uint itemId;               // 4 bytes — consumed item (echoes UseItemRequest.itemId)
    int  newResourceValue;     // 4 bytes — authoritative HP or MP value post-clamp (FloorToInt
                               //   per CR-NET-7.2; CUS GDD internal type is float — wire encoding
                               //   truncates fractional part at the serialisation boundary)
    int  newInventoryQuantity; // 4 bytes — remaining stack count after ConsumeItem; 0 = slot
                               //   transitions to Assigned-Out-of-Stock (AssignedItemID retained)
}
```
*Standalone: 10 + 16 = 26 bytes. Success-only — sent only after ConsumeItem and ApplyRegen/ApplyManaRegen both complete. The `EffectType` (HP vs MP) is inferred from the item's `ConsumableData.EffectType` in the Item Database — not echoed in this message; the client uses `requestId` to look up the locally-predicted EffectType. Client applies `newResourceValue` to the corresponding HP or MP bar and updates the hotbar slot badge with `newInventoryQuantity`. **CR-NET-7.2 cross-doc note:** The CUS GDD Interactions table specifies `newResourceValue: float`; wire encoding uses `int` (FloorToInt). The CUS GDD Interactions table must be updated to `int` before implementation begins.*

---

**UseItemRejected** (R-OD, server → client, 5-byte body; 15 bytes standalone):
```
UseItemRejected {
    uint requestId; // 4 bytes — correlates to originating UseItemRequest
    byte reason;    // 1 byte  — UseItemRejectedReason enum : byte
                    //   CharacterDead=0     — ZoneSessionState is Dead or Respawning (Step 4a)
                    //   RejectedOnCooldown=1 — server-side EffectType cooldown > 0 (Step 4c)
                    //   RejectedNoItem=2    — HasItem(itemId) = 0 (Step 4b)
                    //   ServerError=255     — unexpected server-side fault
}
```
*Standalone: 10 + 5 = 15 bytes. Must arrive — a lost rejection leaves the client in the predict state (slot greyed, cooldown ticking) indefinitely. Client action on receipt: reset EffectTypeCooldownRemaining to 0 for the EffectType of the pending request; un-grey the hotbar slot; show reason-specific toast. `requestId` correlates to the in-flight predict state set at CR-CUS-3 Step 2. See consumable-use-system.md EC-2 and CR-CUS-3 Step 6b.*

---

#### Movement System Messages

**MovementIntentMessage** (U-U, client → server, standalone, 10-byte body; 24 bytes standalone):
```
MovementIntentMessage {
    short  dirX;        // 2 bytes — normalized movement direction X; encoded per CR-NET-7.2 unit direction (×32,767)
    short  dirZ;        // 2 bytes — normalized movement direction Z; encoded per CR-NET-7.2 unit direction (×32,767)
    short  facingAngle; // 2 bytes — character facing yaw; angle_degrees × 10 (range 0–3599 → 0.0°–359.9°); matches EntityState.facingAngle encoding
    uint   tickNumber;  // 4 bytes — server tick this intent was generated for; stale messages (tickNumber < serverCurrentTick − STALE_TICK_TOLERANCE) are discarded server-side
}
```
*Standalone size: 10 (envelope) + 4 (SenderEntityID per CR-NET-7.1 C→S extension) + 10 (body) = 24 bytes. Channel: U-U (unreliable, unordered) — a dropped message self-corrects on the next tick (server holds previous position). Rate: one per server tick at 20 Hz while movement input is active; the client may suppress the send when no movement input is active (no joystick touch, auto-run inactive). Active auto-run counts as active movement input — the client must not suppress the send while auto-run is set. Direction encoding: `dirX`/`dirZ` use CR-NET-7.2 unit-direction encoding (×32,767); DirY is excluded (ground-plane movement only). Zero vector (`dirX=0`, `dirZ=0`) indicates no movement intent; the server applies the F-MOV-1 zero-input guard and does not integrate position that tick. Pre-encode guard: assert `sqrt(dirX² + dirZ²) ≈ 32,767` (within ±100) for non-zero moves — substitute `(32767, 0)` and log an anomaly if direction is non-unit. SenderEntityID validation: server discards message and logs a security event if `SenderEntityID` does not match the authenticated session (EC-MOV-16 in `movement-system.md`). Defined in `movement-system.md` CR-MOV-10.*

---

**SelfPositionUpdate** (R-U batch, server → owning client, 10-byte body; **14 bytes in batch**):
```
SelfPositionUpdate {
    EntityID entityId; // 4 bytes — receiver asserts entityId == localPlayerEntityId;
                       //   mismatch logs SelfPositionDirectionViolation anomaly and
                       //   suppresses reconciliation for that tick
    short    posX;     // 2 bytes — server-authoritative position, fixed-point (×100, cm) per CR-NET-7.2
    short    posY;     // 2 bytes
    short    posZ;     // 2 bytes
}
```
*Batch size: 2 (prefix) + 2 (TypeID) + 10 (body) = 14 bytes. Sent every tick to the owning client only — never zone-broadcast. This is the per-tick authoritative self-position delivery that CR-CSP-7 reconciliation depends on: the client compares `(posX, posY, posZ)` against its predicted position snapshot for `ServerTickNumber` (carried in the R-U batch envelope, CR-NET-7.1) to compute the correction vector. Channel: R-U, not U-U — reconciliation data is most critical when packet loss is highest; a dropped `SelfPositionUpdate` at the same moment as a `MovementIntentMessage` loss would leave the client unable to reconcile for that tick, compounding prediction error exactly when correction is most needed. See `networking-message-criticality.md` for channel policy authority. Suppressed when `ZoneSessionState` is Dead or Respawning — no prediction is active during those states and the server controls the authoritative respawn position via `EntityRespawned`. Resumption begins at the tick following `RespawnConfirmed` delivery. Pre-encode guard: CR-NET-7.2 position overflow guard applies (assert each component ∈ [−327.67, 327.67] before encoding; clamp and log `SelfPositionEncodeOverflow` anomaly if out of range). Defined by `client-side-prediction.md` CR-CSP-7.*

---

#### Skill System Messages

All schemas are body-only; prepend the 10-byte CR-NET-7.1 envelope (+ 4-byte `SenderEntityID` for client→server messages). Defined in `skill-system.md`.

---

**SkillCastRequest** (U-U, client → server, standalone, 16-byte body; 30 bytes standalone):
```
SkillCastRequest {
    uint casterEntityID;    // 4 bytes — server validates against authenticated session (V-1); redundant with C→S extension but included for explicit validation
    uint skillID;           // 4 bytes — resolves to SkillID; SkillID(0) = Invalid, rejected at V-3
    uint targetEntityID;    // 4 bytes — EntityID of cast target; server replaces with casterEntityID for Self-constraint skills (CR-SK-19)
    uint clientTickNumber;  // 4 bytes — staleness check: discarded if serverTick − clientTickNumber > CAST_STALE_TICK_TOLERANCE (V-2)
}
```
*Standalone size: 10 (envelope) + 4 (SenderEntityID, CR-NET-7.1 C→S extension) + 16 (body) = 30 bytes. Channel: U-U — dropped requests are not retransmitted; the client may re-submit next beat. At most one request per entity processed per tick; additional queued requests discarded without notification (CR-SK-2). Rate at 20 Hz: 30 bytes × 20 = 600 bytes/s inbound per actively-casting player. Defined in `skill-system.md` CR-SK-1.*

---

**SkillCastResult** (R-U batch, server → clients, 28-byte body; **32 bytes in batch**):
```
SkillCastResult {
    uint  skillID;          // 4 bytes
    uint  casterID;         // 4 bytes
    uint  targetID;         // 4 bytes
    bool  castAccepted;     // 1 byte — false = rejection; damage/heal fields are 0 on rejection
    byte  rejectionCode;    // 1 byte — SkillCastRejectionCode enum : byte; 0 when castAccepted == true
    int   damageDealt;      // 4 bytes — 0 when no damage flag or on rejection
    int   healAmount;       // 4 bytes — 0 when no heal flag or on rejection
    bool  isCrit;           // 1 byte — false on rejection or non-damage skill
    bool  isKill;           // 1 byte — false on rejection or non-damage skill
    int   newCooldownTicks; // 4 bytes — ticks until cooldown expires; 0 on rejection or CooldownTicks==0 skill
}
```
*Batch size: 2 (prefix) + 2 (TypeID) + 28 (body) = 32 bytes. Delivery per CR-SK-9: accepted casts → caster + all clients for whom caster or target is in their relevance set; rejected casts → caster only. Event-driven — at most one per entity per tick. Receivers must range-check `rejectionCode` before casting to `SkillCastRejectionCode`. Defined in `skill-system.md` CR-SK-9.*

---

**SkillCooldownUpdate** (R-U batch, server → caster only, 8-byte body; **12 bytes in batch**):
```
SkillCooldownUpdate {
    uint skillID;        // 4 bytes
    uint ticksRemaining; // 4 bytes — 0 when cooldown just expired (skill transitions to Ready)
}
```
*Batch size: 2 + 2 + 8 = 12 bytes. Sent to caster only when a skill's cooldown state changes: on cooldown start (tick = CooldownExpiryTick − serverCurrentTick) and on expiry (ticksRemaining = 0). Client updates cooldown sweep overlay from this value; never extrapolates cooldown state independently. Defined in `skill-system.md` CR-SK-12.*

---

**SkillCooldownSnapshot** (R-OD, server → owning client on zone join / reconnect, 80-byte body; 90 bytes standalone):
```
SkillCooldownSnapshot {
    // Fixed array of 10 entries, positionally aligned with IClassRegistry.GetClassSkills()
    SkillCooldownEntry[10] slots; // 10 × 8 bytes = 80 bytes
    // Per entry:
    //   uint skillID;        // 4 bytes
    //   uint ticksRemaining; // 4 bytes — 0 if skill is Ready or Locked
}
```
*Standalone size: 10 (envelope) + 80 (body) = 90 bytes. Fixed-size — always exactly 10 entries. Sent once per zone join or reconnect as part of the join-snapshot sequence, after `SessionReady`. Locked skills carry `ticksRemaining=0`; the client derives `Locked` vs `Ready` from the `IsUnlocked` flags established at spawn. `MessageTypeID` assigned in Networking ADR. Defined in `skill-system.md` CR-SK-12.*

---

**SkillUnlockNotification** (R-OD, server → owning client, 4-byte body; 14 bytes standalone):
```
SkillUnlockNotification {
    uint skillID; // 4 bytes — SkillID of the newly unlocked skill
}
```
*Standalone size: 10 (envelope) + 4 (body) = 14 bytes. Sent once when `OnLevelUp` causes a skill's `IsUnlocked` to transition from false to true (CR-SK-21). R-OD delivery — must not be dropped; controls the unlock animation and "NEW" badge in the HUD skill bar. `MessageTypeID` assigned in Networking ADR. Defined in `skill-system.md` CR-SK-21.*

---

#### Probing Messages

**RttProbe** (server → client, no body; 10 bytes standalone):
```
RttProbe {
    // No body. Wire size = 10-byte envelope only.
    // Server sends every RTT_PROBE_INTERVAL_SECONDS. Client echoes immediately with RttProbeEcho.
}
```

**RttProbeEcho** (client → server, no body beyond standard extension; 14 bytes standalone):
```
RttProbeEcho {
    // No body. Wire size = 14 bytes (10B envelope + 4B SenderEntityID per CR-NET-7.1).
    // Client echoes immediately on receipt of any RttProbe.
}
```

---

#### Bulk-Transfer Messages (connection-phase only, 0xF000–0xFFFF range)

**EntityState** (used in `ZoneStateSnapshotFragment` payload — tagged discriminated union):

`EntityState` is a **tagged discriminated union**. `entityType` is ALWAYS the first field serialized and must be read first by the deserializer to determine which remaining fields to parse. Player and mob entries share a 33-byte common header; type-specific fields follow.

```
// --- Shared header (ALL entity types, 33 bytes) ---
EntityType entityType;   // 1 byte  — Player=0, Mob=1; MUST be read first
EntityID   entityId;     // 4 bytes
short      posX;         // 2 bytes — fixed-point (×100, cm)
short      posY;         // 2 bytes
short      posZ;         // 2 bytes
short      rotX;         // 2 bytes — fixed-point Quaternion (×32,767)
short      rotY;         // 2 bytes
short      rotZ;         // 2 bytes
short      rotW;         // 2 bytes
int        currentHP;    // 4 bytes
int        maxHP;        // 4 bytes
bool       isInCombat;   // 1 byte
bool       isGhost;      // 1 byte  — always false for mobs (ghost mechanic is player-only)
ushort     cycleTimer;   // 2 bytes — normalized fraction (0–10,000); mob entries transmit 0 if no auto-attack cycle
short      facingAngle;  // 2 bytes — angle_degrees × 10 (range 0–3599 → 0.0°–359.9°); matches MovementIntentMessage.facingAngle
// Header total: 1+4+6+8+4+4+1+1+2+2 = 33 bytes

// --- Player-specific fields (entityType == Player, 20–44 bytes) ---
string   characterName;             // ushort(2) + UTF-8 max 24B = 26B max
int      level;                     // 4 bytes
int      currentMP;                 // 4 bytes
int      maxMP;                     // 4 bytes
byte     equipmentAppearanceFlags;  // 1 byte — bitfield (Equipment System GDD CR-EQS-11)
bool     isInDeadState;             // 1 byte  — true when entity is in DEAD state (death-and-respawn.md CR-DR-6); false when Alive or Respawning [OQ-DR-6 resolved]
uint     respawnTicksRemaining;     // 4 bytes — ticks until T-3 fires; max(0, RespawnTick − currentTick); 0 when isInDeadState==false [OQ-DR-6 resolved]

// --- Mob-specific fields (entityType == Mob, 2 bytes) ---
ushort   mobTypeId;  // 2 bytes — mob type registry key; replaces characterName for mob entries
```

*Player body: 53–77 bytes (33B header + 20–44B player fields). Minimum at empty characterName: 33+2+4+4+4+1+1+4 = 53B. Maximum at 24-byte name: 33+26+4+4+4+1+1+4 = 77B. Mob body: 35 bytes (33B header + 2B mobTypeId). Variable-width: deserializers must read `entityType` first, then read the type-specific length-prefixed or fixed fields in sequence.*

**ZoneStateSnapshotFragment** (R-OD bulk-transfer, `0xF000–0xFFFF` range):
```
ZoneStateSnapshotFragment {
    uint   snapshotVersion; // 4 bytes — server tick when snapshot was taken; monotonically increasing
    uint16 totalFragments;  // 2 bytes — MUST be computed from actual serialized snapshot byte count
                            //           (not worst-case estimate); see note below
    uint16 fragmentIndex;   // 2 bytes — 0-based index (0 to totalFragments−1)
    // payload: packed EntityState entries, up to (MAX_MESSAGE_BODY_BYTES − 8) bytes per fragment
}
```

**`totalFragments` computation (REQUIRED — do not use worst-case formula):** The server must fully serialize the complete `EntityState[]` array before fragmenting. `totalFragments = ⌈actualSnapshotByteCount / payloadCapacity⌉` where `payloadCapacity = MAX_MESSAGE_BODY_BYTES − 8`. Using a worst-case per-entity estimate to pre-compute `totalFragments` is **incorrect** — `EntityState` bodies are variable-width. If the server over-estimates `totalFragments`, the client allocates a buffer awaiting fragments that will never arrive, and reassembly never completes. The server must serialize first, then compute the fragment count from the actual byte count.

*Player entries: avg ~65B (11-char name), min 53B, max 77B. Mob entries: 35B fixed. Payload capacity: 504B.*
*Player-only zone (n=50, no mobs): avg 3,250B → 7 fragments; max 3,850B → 8 fragments.*
*MVP full zone (n=50 players + M=150 mobs, F-ZI-1): avg (3,250 + 5,250) = 8,500B → 17 fragments; max (3,850 + 5,250) = 9,100B → 19 fragments. Fragment range [17, 19] — see B-22 re-validation in Open Questions.*

**Reassembly protocol:**
1. On receipt of `fragmentIndex = 0`: allocate reassembly buffer of `totalFragments × payloadCapacity` bytes; record `snapshotVersion` and `totalFragments`.
2. On receipt of each fragment: copy payload into reassembly buffer at `fragmentIndex × payloadCapacity` offset; mark fragment received.
3. When all `totalFragments` fragments received: parse `EntityState[]` from buffer (using length-prefixed EntityState deserialization to handle variable-width entries); apply to zone render state; open zone-entry dual-gate (requires `SessionReady` also received).
4. On `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` expiry without all fragments: discard partial buffer; send `ZoneSnapshotRequest` (defined in `networking-session.md`) to request retransmit.
5. Discard fragments whose `snapshotVersion` is older than the currently-assembling snapshot (per `IsNewerVersion` — CR-NET-7.5).

*Fragment envelope is exempt from the Path 1 8-message cap (bulk-transfer exemption, CR-NET-7.7).*

---

#### Enum Types

**GoldTransactionReason** (used in `GoldSyncEvent`):
```csharp
enum GoldTransactionReason : byte
{
    MonsterDrop    = 0,
    ScrollPurchase = 1,
    RespecStat     = 2,
    RespecSkill    = 3,
    AdminAdjust    = 4,
    Enhancement    = 5,
    ItemPurchase   = 6,
    ItemSell       = 7,   // re-added 2026-05-15 per inventory-system.md design review
    Other          = 255,
}
```
*Unknown bytes: log anomaly, treat as `Other`; gold balance update is still applied.*

**PartyMemberStatus** (used in `PartyStateUpdate` → `PartyMemberSlot`):
```csharp
enum PartyMemberStatus : byte
{
    Online    = 0,  // Connected and in the current zone
    Ghost     = 1,  // Disconnected, session TTL active — ghost entity still present in zone (CR-GH-2)
    OutOfZone = 2,  // Member in a different zone instance (future Zone Instancing — provisional)
    Dead      = 3,  // Entity HP = 0; respawn countdown active (CR-DR-5, death-and-respawn.md); slot retained, not released
}
```
*Unknown bytes: substitute `Online = 0` and continue — party panel renders member as online rather than crashing. Log anomaly.*

**DiscardFailReason** (used in `DiscardResult`):
```csharp
enum DiscardFailReason : byte
{
    None            = 0,  // Success (reason field when success=true)
    SlotLocked      = 1,  // Item is locked by Enhancement System (inventory-system.md Rule 5)
    InvalidQuantity = 2,  // quantity=0 or quantity > currentSlotQuantity
    SlotEmpty       = 3,  // Slot holds no item (ItemID.Invalid)
}
```
*Unknown bytes: substitute `None = 0` and continue. Log anomaly.*

**MoveFailReason** (used in `MoveResult`):
```csharp
enum MoveFailReason : byte
{
    None         = 0,  // Success (or same-slot no-op — both count as success)
    SourceLocked = 1,  // Source slot is locked by Enhancement System
    DestLocked   = 2,  // Destination slot is locked by Enhancement System
    InvalidSlot  = 3,  // fromSlot or toSlot out of [0, INVENTORY_SLOT_COUNT−1] range
}
```
*Unknown bytes: substitute `None = 0` and continue. Log anomaly.*

**EquipFailReason** (used in `EquipResult`):
```csharp
enum EquipFailReason : byte
{
    None                  = 0,   // Success (reason field when success=true)
    StatRequirementNotMet = 1,   // GetBaseStat(entityID, item.EquipRequirementStat) < item.EquipRequirementMin; client displays shortfall from local stat cache (last StatSnapshotEvent)
    InventoryFull         = 2,   // Slot occupied, no free inventory slot for displaced item (ForceInsert + MoveItemIn both failed)
    SlotMismatch          = 3,   // item.GearSlot != requested gearSlot
    ItemLocked            = 4,   // Item locked by Enhancement System
    ItemNotInInventory    = 5,   // itemId not found in player's inventory
    CriticalFailure       = 255, // ForceInsert + MoveItemIn both failed; slot left Empty; server logs full context
}
```
*Unknown bytes: substitute `None = 0` and continue. Log anomaly.*

**GhostExpiredReason** (used in `GhostExpiredEvent`):
```csharp
enum GhostExpiredReason : byte
{
    GhostTtlExpired = 0,  // Session TTL elapsed without reconnect (CR-GH-10)
    GhostDismissed  = 1,  // Voluntary party dismissal (CR-GH-12)
    GhostDeath      = 2,  // Ghost HP reached zero during ghost period (CR-GH-6, CGS-5)
}
```
*Unknown bytes: substitute `GhostTtlExpired = 0` and continue — ghost slot release still processes.*

---

**EntityType** (used in `EntityState` header — added 2026-05-29, Zone Instancing amendment):
```csharp
enum EntityType : byte
{
    Player = 0,  // Player character entity — carries full EntityState player fields
    Mob    = 1,  // Non-player mob entity — carries mob-specific EntityState fields
}
```
*Unknown bytes: log anomaly, discard the entire EntityState entry and skip to the next — do not attempt to deserialize type-specific fields for an unknown entity type.*

---

**JoinRejectedReason** (used in `ZoneFullResponse` — added 2026-05-29, Zone Instancing amendment):
```csharp
enum JoinRejectedReason : byte
{
    ZoneFull            = 0,   // Zone is at MAX_PLAYERS_PER_ZONE capacity (CR-ZI-8 step 4)
    ZoneDoesNotExist    = 1,   // Zone is no longer Active — configuration error or closed (EC-ZI-1, EC-ZI-6, EC-ZI-8)
    LoadCharacterFailed = 2,   // Character Persistence LoadCharacter returned an error code (CR-ZI-8 step 3)
    Other               = 255,
}
```
*Unknown bytes: substitute `Other = 255` and continue — client displays a generic rejection message. Log anomaly.*

---

## Formulas

All formulas below reflect sub-message sizes with the uint16 length prefix (+2 bytes per sub-message in batch context, per B-NP-3).

---

**F-NET-1 — Server Outbound Bandwidth per Client**

Two batch packets are sent per client per tick: one R-U and one U-U (CR-NET-7.7). Sub-message sizes in batch context (includes 2-byte uint16 length prefix + 2-byte MessageTypeID + payload):

| Sub-message | Batch size (prefix + TypeID + payload) |
|-------------|----------------------------------------|
| `CycleTimerBroadcast` | 2 + 2 + 4 + 2 = **10 bytes** |
| `EntityHealthUpdate` | 2 + 2 + 4 + 4 + 4 = **16 bytes** |
| `EntityPositionUpdate` | 2 + 2 + 4 + 2 + 2 + 2 = **14 bytes** |
| `DamageEvent` | 2 + 2 + 4 + 4 + 4 + 1 + 1 = **18 bytes** |
| `PartyMemberHealthUpdate` | 2 + 2 + 4 + 4 + 4 + 4 + 4 = **24 bytes** |
| `SelfPositionUpdate` | 2 + 2 + 4 + 2 + 2 + 2 = **14 bytes** |
| `GoldSyncEvent` | 2 + 2 + 4 + 4 + 4 + 1 = **17 bytes** |
| `ConnectionQualityUpdate` | 2 + 2 + 1 = **5 bytes** |
| Batch envelope (per packet) | 2 + 4 + 4 + 2 = **12 bytes** |

*Density assumptions:* One auto-attack per player per 20-tick cycle = 50/20 = 2.5 events/tick at n=50 baseline. Scenarios model "steady-state combat." Conservative estimates — not worst-case.

*Three packets per client per tick: R-U batch + CycleBroadcast packet + Position packet (PA-P7-05 resolution — separate CycleBroadcast and position packets).*

*All scenarios assume a full party of 3 other members and the client targeting 1 non-party entity. EntityHealthUpdate relevance set: max 2 (self-slot + 1 non-party target-slot); PartyMemberHealthUpdate set: max 3 (party members). Separation invariant per networking-relevance-filter.md RFR-1. PartyMemberHealthUpdate is in the R-U batch (moved from Position packet per `networking-message-criticality.md` MCR-2).*

**Scenario A — n=10 players (9 other entities), light combat (~2 damage events/tick):**
- R-U batch: header(12) + 2 DamageEvents(36) + 2 EntityHealthUpdates, filtered(32) + 3 PartyMemberHealthUpdates(72) + 1 SelfPositionUpdate(14) = **166 bytes** (no overflow; GoldSyncEvent +17B and ConnectionQualityUpdate +5B when triggered)
- CycleBroadcast packet: header(12) + 9 × CTB(10) = **102 bytes** (no overflow)
- Position packet: header(12) + 9 Positions(126) = **138 bytes** (no overflow; all 9 positions delivered)
- Per-client/tick: 166 + 102 + 138 = **406 bytes**
- Bandwidth: 406 × 20 = **8,120 bytes/s ≈ 7.9 KB/s per client**

**Scenario B — n=25 players (24 other entities), active combat (~5 damage events/tick):**
- R-U batch: header(12) + 5 DamageEvents(90) + 2 EntityHealthUpdates, filtered(32) + 3 PartyMemberHealthUpdates(72) + 1 SelfPositionUpdate(14) = **220 bytes** (no overflow)
- CycleBroadcast packet: header(12) + 24 × CTB(10) = **252 bytes** (no overflow)
- Position packet: header(12) + 24 Positions(336) = **348 bytes** (no overflow; all 24 positions delivered)
- Per-client/tick: 220 + 252 + 348 = **820 bytes**
- Bandwidth: 820 × 20 = **16,400 bytes/s ≈ 16.0 KB/s per client**

**Scenario C — n=50 players (49 other entities), dense combat (~10 damage events/tick):**
- R-U batch: header(12) + 10 DamageEvents(180) + 2 EntityHealthUpdates, filtered(32) + 3 PartyMemberHealthUpdates(72) + 1 SelfPositionUpdate(14) + 1 GoldSyncEvent(17) + 1 ConnectionQualityUpdate(5) = **332 bytes** (**no overflow** — relevance filter + separation invariant eliminated all HP-update drops from Pass 1 Scenario C; SelfPositionUpdate adds 14B, R-U batch 180B below cap)
- CycleBroadcast packet: header(12) + 49 × CTB(10) = **502 bytes** (no overflow; all 49 cycleTimers delivered)
- Position packet: header(12) + 35 × 14 = **502 bytes** (35 of 49 positions delivered, **14 dropped**; party HP now in R-U batch)
- Per-client/tick: 332 + 502 + 502 = **1,336 bytes**
- Bandwidth: 1,336 × 20 = **26,720 bytes/s ≈ 26.1 KB/s per client**

*Note (n=50): R-U batch is 332 bytes — no overflow (180B headroom to 512B cap). Separation invariant (networking-relevance-filter.md) limits EHU to 2 per client (self-slot + 1 non-party target-slot); party HP delivered exclusively via 3 PartyMemberHealthUpdate sub-messages. Position delivery is 35/49 (71%), 14 dropped per tick — clients interpolate stale positions. CycleTimerBroadcast fully delivered. Party HP bars fully delivered (R-U reliable). EHU reduction: ~96% from zone-wide broadcast (2 vs. 49 per client per tick).*

**Peak-combat note (n=50, AoE-heavy, ~30–40 damage events/tick):** With the relevance filter active, the fixed R-U payload (header + 2 EntityHealthUpdates + 3 PartyMemberHealthUpdates + SelfPositionUpdate + GoldSyncEvent + CQU) is 152 bytes (12 + 32 + 72 + 14 + 17 + 5; EHU capped at 2 by separation invariant — see networking-relevance-filter.md). Available for DamageEvents: 512 − 152 = 360 bytes → **20 DamageEvents fit per client per tick** before overflow. Overflow threshold is 21 DamageEvents/tick. At 30 events/tick: 30 − 20 = **10 DamageEvents overflow-dropped**. At 40 events/tick: 40 − 20 = **20 DamageEvents overflow-dropped**. The Networking ADR load simulation must test at 4× Scenario C density. See OQ-NC-SER-3.

---

**F-NET-2 — Total Server Outbound Bandwidth (per zone)**

```
BytesPerSecond_total = BytesPerSecond_client × PlayersInZone
```

| Scenario | Per-client | Total (zone) | Mbit/s |
|----------|-----------|--------------|--------|
| n=10 (light combat) | ~7.9 KB/s | ~79 KB/s | ~0.63 Mbit/s |
| n=25 (active combat) | ~16.0 KB/s | ~400 KB/s | ~3.2 Mbit/s |
| n=50 (dense combat) | ~26.1 KB/s | ~1,305 KB/s | ~10.4 Mbit/s |

*Bandwidth increased ~0.3 KB/s vs. prior figures across all tiers due to `SelfPositionUpdate` addition (+14 bytes/client/tick in R-U batch). R-U batch headroom at Scenario C: 332 of 512 bytes — no overflow introduced. Still well within mobile budget. Prior figures (~7.7 / ~15.7 / ~25.8 KB/s, CSP amendment 2026-06-14) are superseded. The PA-P7-05 separate CycleBroadcast packet cost is preserved — required for Pillar 2 correctness.*

Peak ~10.4 Mbit/s per zone for a single 50-player zone. **This figure is valid for one zone only** — a mid-tier cloud server can support approximately 50–80 concurrent zones before egress becomes a budget concern. Scale linearly; the Networking ADR infrastructure section must specify the target zone count before the "manageable" claim can be validated.

---

**F-NET-3 — GoldSyncEvent Wire Size**

Standalone packet: `Envelope(10) + CharacterID(4) + NewBalance(4) + Version(4) + Reason(1) = 23 bytes`

As a batch sub-message: `uint16 prefix(2) + MessageTypeID(2) + CharacterID(4) + NewBalance(4) + Version(4) + Reason(1) = 17 bytes`

---

**F-NET-6 — Tick Budget Breakdown (per 50ms tick)**

```
TickBudget_ms = 50ms
  ServerLogic_ms = Beat resolution + TTL evaluation + state mutation
  BatchFlush_ms  = serialize R-U batch + serialize U-U batch, per connected client
  Slack_ms       = TickBudget_ms − ServerLogic_ms − BatchFlush_ms  (must be > 0)
```

**Per-player scaling analysis:**

| Phase | Complexity | Notes |
|-------|-----------|-------|
| Beat evaluation | O(n) | One BeatEval per entity in COMBAT_ACTIVE |
| TTL expiry check | O(n) | One `IsTickExpired` check per session |
| Batch serialization | O(n) per client × n clients = **O(n²) total** | Each client's batches include sub-messages about all other n−1 entities |

O(n²) serialization is the dominant scaling concern. **Two distinct counts must be tracked:**

| Scenario | Pre-drop sub-messages (serialisation work) | Post-drop sub-messages (client delivery) |
|----------|--------------------------------------------|------------------------------------------|
| n=10, standard combat | ~116 (all 3 packets) × 10 clients = **1,160** total | ~1,160 (no overflow at n=10) |
| n=25, standard combat | ~116 × 25 = **2,900** total | ~2,900 (no overflow at n=25) |
| n=50, standard combat | ~116 × 50 = **5,800** total | ~5,100 (14 positions dropped/client; no R-U overflow with filter) |
| n=50, peak combat (40 DamageEvents/tick) | ~146 × 50 = **7,300** total | ~5,600 (14 positions + 20 DamageEvents dropped/client; GoldSyncEvent/EHU/PMHU/SPU all fit — no additional drops) |

*Per-client sub-message count at standard Scenario C density: 49 CTB + 49 positions + 10 damage + 2 filtered HP + 3 party HP + 1 SelfPositionUpdate + 1 gold + 1 CQU = 116. Previous Pass 1 figure of ~6,400 was incorrect (used 128/client instead of the correct 159/client for zone-wide HP; the relevance filter now legitimately reduces to 115). EntityHealthUpdate serialization is no longer O(n²): the relevance filter (networking-relevance-filter.md RFR-2) writes directly from bounded per-client sets. Position and cycle timer enumeration remain O(n²).*

*The drop policy caps packet size, not serialisation work. For position and cycle timer updates, the server enumerates all n−1 entities per client before the drop policy runs — O(n²) total. EntityHealthUpdate and PartyMemberHealthUpdate serialization is O(n × MAX_PARTY_SIZE) since the relevance filter writes directly from bounded per-client sets.*

**The overflow drop policy caps per-client packet size at `MAX_MESSAGE_BODY_BYTES` per packet. The per-client work ceiling** (pre-drop) for the O(n²) bound is determined by `PRIORITY_PATH_CAP`, `MAX_PLAYERS_PER_ZONE`, and sub-message sizes — not by `min_sub_message_size` alone. `min_sub_message_size = 5 bytes` (ConnectionQualityUpdate) gives a theoretical ceiling of 512/5 = 102 sub-messages per client per packet; the combat-relevant floor is 18 bytes (DamageEvent), giving 28 per client. Profiling must measure pre-drop work at peak-combat density.

**Priority-path cost:** Enhancement outcome serialization runs outside the tick loop. At high activity (10 players enhancing simultaneously), this adds ~10 × serialization cost to the inter-tick window. The ADR profiling pass must measure this under load and confirm it does not push the inter-tick window past `TickBudget_ms`.

**Tick budget caveat:** The 30ms target is a design intent, not a measured budget. Before it can be treated as a real constraint, the Networking ADR must specify: (1) the target hardware tier (not "a cloud VPS" — a named instance type with vCPU count and memory); (2) the server runtime (Mono vs. CoreCLR vs. IL2CPP headless build); (3) a prototype measurement of `BatchFlush_ms` at n=50, peak-combat density. Until then, the 30ms figure is a planning estimate only.

The Networking ADR must profile `ServerLogic_ms + BatchFlush_ms` at n=10, n=25, and n=50 at **both baseline density and peak-combat density (40 DamageEvents/tick)** and confirm the total remains < 30ms at all tiers. The peak-combat scenario is mandatory — serialisation work at peak is ~4× standard-combat at n=50.

---

**F-NET-7 — Server Inbound Bandwidth per Client (B-NP-1 corrected)**

Per-client inbound (steady-state combat, client → server direction, 14-byte envelope per standalone message):

| Message | Rate | Bytes/s |
|---------|------|---------|
| `NotifySkillUsed` | ~1 per beat = ~1/s | ~14 bytes/s |
| `RttProbe` echo | 1 per `RTT_PROBE_INTERVAL_SECONDS` (10s) | ~1.4 bytes/s |
| `HeartbeatMessage` (GAP-1) | ~1 per `HEARTBEAT_INTERVAL_SECONDS` (~3–4s) | ~2.5–3.3 bytes/s |
| `MovementIntentMessage` | 20 Hz (one per tick; suppressed when no input active) | ~480 bytes/s while moving |
| **Baseline (no movement)** | — | **~18 bytes/s per client** |
| **Active movement** | — | **~498 bytes/s per client** |

At 50 players (all moving): 498 × 50 = **~24.9 KB/s inbound total per zone** — still negligible relative to outbound (~25.8 KB/s outbound per client at Scenario C). Movement GDD confirmed 20 Hz rate; estimate is now final (2026-05-26).

---

## Edge Cases

**EC-NET-6 — SequenceNumber Wrap**

At 20 Hz with 50 clients, a server sends at most 1,000 messages per second. `uint.MaxValue / 1,000 ≈ 49.7 days` before `SequenceNumber` wraps. Not a practical concern for MVP. However, stale-discard comparison must **not** use raw `uint` comparison — `(uint)2 > (uint)4,294,967,294` evaluates to `false` in C#, causing all post-wrap messages to be permanently discarded. Use `IsNewerVersion` (CR-NET-7.5) for both `SequenceNumber` and `Version` stale-discard comparisons.

---

**EC-NET-8 — GoldSyncEvent Version Overflow**

At the theoretical maximum mutation rate (one gold event per tick = 20/sec), `GoldSyncEvent.Version` overflows after ~6.8 years. Not a practical concern for MVP. The stale-discard comparison must use `IsNewerVersion` (CR-NET-7.5) — raw `uint` comparison fails after wraparound (see EC-NET-6).

---

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-core.md` | Parent — channel policy (CR-NET-3), authority model, tick loop that drives batch flush |
| `networking-message-criticality.md` | **New upstream contract** — all channel assignments in CR-NET-7.7 are derived from the MCR-2 per-message criticality table. This document must be consulted before changing any channel assignment. |
| `networking-channel-contract.md` | **New upstream contract** — authoritative per-message direction/channel/invariant table (CCR-3). Resolves SequenceNumber scope (CCR-1) and SessionHandshake naming (CCR-2). |
| `networking-relevance-filter.md` | **New upstream contract** — defines the relevance set for EntityHealthUpdate delivery (party + targeting). Governs the S→RELEVANT direction noted in CCR-3. |
| `networking-session.md` | Consumes `IsTickExpired` helper defined here; `ZoneSessionEnded`, `PlayerJoinedZone`, `PlayerLeftZone`, `SessionReady` (partial), and `ZoneStateSnapshotFragment` reassembly protocol defined here. **Note:** `SessionReady` and `SessionHandshake` full schemas are pending Character Persistence GDD (OQ-NC-SER-2) — the stub schemas in this document define the currently-known fields only. |
| `networking-test-harness.md` | `INetworkTestObserver` captures at the serialization boundary defined by CR-NET-7; `ITransportFaultInjector` operates at the channel level defined by CR-NET-3 |

| `movement-system.md` | **New upstream** — defines `MovementIntentMessage` (CR-MOV-10), the `FacingAngle` field encoding that matches `EntityState.facingAngle`, and `STALE_TICK_TOLERANCE` used in stale-discard logic. `EntityState` schema updated 2026-05-26 to add `facingAngle: short(×10)`. OQ-MOV-1 closed by this amendment. |
| `death-and-respawn.md` | **New downstream** — `EntityDied`, `DeathStateEntered`, `EntityRespawned`, `RespawnConfirmed` schemas defined in this document and consumed by `death-and-respawn.md` for the death lifecycle wire contract (CR-DR-6, CR-DR-7, CR-DR-13). |
| `zone-instancing.md` | **New downstream** — Zone Instancing owns `ZoneStateSnapshot` assembly (CR-ZI-8 step 6). `EntityState` schema (amended 2026-05-29) consumed for snapshot serialization — EntityType, MobTypeID, isInDeadState, respawnTicksRemaining. `ZoneFullResponse` (OQ-ZI-1) and `ZoneSnapshotRequest` (OQ-ZI-3) defined here and consumed by zone entry (CR-ZI-7) and reassembly retry (CR-ZI-9). OQ-ZI-1, OQ-ZI-2, OQ-ZI-3, OQ-ZI-7 resolved by this amendment. |
| `consumable-use-system.md` | **New downstream** — `UseItemRequest`, `UseItemResult`, `UseItemRejected` schemas defined here (registered 2026-06-09, OQ-CUS-2 resolved). `newResourceValue` uses `int` wire encoding per CR-NET-7.2 (CUS GDD internal type is `float`). OQ-CUS-1 (requestId reconnect continuity) remains open pre-implementation. |
| `client-side-prediction.md` | **New downstream** — `SelfPositionUpdate` defined here (CSP amendment 2026-06-14) and consumed by CR-CSP-7 for per-tick reconciliation. Channel selection (R-U, not U-U) is driven by CSP reconciliation criticality under packet loss. CSP GDD not yet approved — amendment authored to unblock review. |

**Downstream:** All game systems that send or receive network messages must conform to the schemas and serialization rules defined here. The Networking ADR must resolve OQ-NC-SER-1 through OQ-NC-SER-4 before implementation begins.

---

## Tuning Knobs

| Knob | Default | Safe Range | Impact |
|------|---------|------------|--------|
| `MAX_MESSAGE_BODY_BYTES` | 512 | [**502**, 1,400] at default `MAX_PLAYERS_PER_ZONE = 50` | Hard ceiling on message body size per packet. **Lower bound formula (cross-knob dependency):** `min = max(160, 12 + (MAX_PLAYERS_PER_ZONE − 1) × 10)` — the CycleBroadcast packet at n=50 is 502 bytes; any value below 502 drops CTBs every tick, violating the Pillar 2 "CycleTimerBroadcast is never dropped" invariant. At `MAX_PLAYERS_PER_ZONE = 10`, minimum is 102 bytes; at 100 players, 1,002 bytes. Do not lower this knob without rechecking `CycleBroadcast_size = 12 + (MAX_PLAYERS_PER_ZONE − 1) × 10`. Must stay below ~1,200 bytes to avoid IP fragmentation on mobile. Raising above 800 bytes requires re-validating per-tick batch budgets in F-NET-1. |
| `RTT_PROBE_INTERVAL_SECONDS` | 10 | [5, 60] | How often the server sends an `RttProbe` to each client to estimate OWL for grace-window compensation (CR-NET-8 in `networking-core.md`). At 10s, a player with suddenly-degraded mobile connection plays ≤10s with unexplained Rhythm Mastery failures before OWL updates. |
| `HEARTBEAT_INTERVAL_SECONDS` | 3 | [1, 10] | Seconds between client heartbeat sends when no other packet was sent in the preceding interval. Lower values increase resilience against false-positive timeout detection at the cost of ~3.3 bytes/s additional inbound bandwidth per client (F-NET-7). Must be < `HEARTBEAT_TIMEOUT_SECONDS / 2` to ensure at least two heartbeats fit within the timeout window. |
| `HEARTBEAT_TIMEOUT_SECONDS` | TBD — see `networking-session.md` | [8, 12] recommended | Server fires session timeout after this many seconds of total client silence. Authoritative definition lives in `networking-session.md`; cross-referenced here because it determines whether the F-NET-7 heartbeat contribution is steady-state or burst. Until the `networking-session.md` value is finalised, F-NET-7 inbound baseline uses 3.3 bytes/s (from default `HEARTBEAT_INTERVAL_SECONDS = 3`). |
| `MAX_PLAYERS_PER_ZONE` | 50 | [10, 100] | Maximum simultaneous players per zone instance. **Primary scaling variable** for all F-NET-1 and F-NET-2 bandwidth calculations, the PA-P7-05 CycleBroadcast saturation threshold, and the zone buffer pool size (`MAX_PLAYERS_PER_ZONE × 3` buffer arrays). Authoritative definition in `networking-core.md`; cross-referenced here because changing it invalidates all F-NET scenarios. Raising to 100 requires `MAX_MESSAGE_BODY_BYTES ≥ 1,002` for the CycleBroadcast packet. |
| `PRIORITY_PATH_CAP` | 8 | [4, 16] | Maximum non-exempt messages per client per tick on Path 1 (R-OD channel). Raising increases enhancement acknowledgment throughput under high-priority-message load at the cost of potential head-of-line blocking for lower-priority R-OD messages. Enhancement-path and bulk-transfer exemptions are not subject to this cap. See `networking-channel-contract.md` F-CCR-1. |
| `GOLD_MAX_CONSECUTIVE_DROP` | 3 | [1, 5] | Maximum ticks `GoldSyncEvent` may be overflow-dropped before forced R-OD delivery. Authoritative definition in `networking-message-criticality.md` MCR-4; cross-referenced here because it affects the R-U batch overflow policy. At 1, gold is always delivered within 2 ticks (100ms); at 5, up to 300ms display lag in sustained overflow. |

---

## Acceptance Criteria

**AC-NC-07** — Given two `GoldSyncEvent` stale-discard scenarios for the same character via `ITransportFaultInjector.ReorderNext`: **(A)** Version=5 (500g) and Version=6 (600g) arrive out of order (Version=6 first); **(B, wraparound)** Version=4,294,967,295 (500g) arrives first, then Version=1 (600g) arrives. When the client applies `IsNewerVersion` comparison in both cases, Then: scenario A — client displays 600g (Version=6) and discards Version=5; scenario B — client displays 600g (Version=1) and discards Version=4,294,967,295 as stale (post-wraparound is newer). A raw `uint` comparison `1 > 4,294,967,295` evaluates to `false` — this AC verifies `IsNewerVersion` is used, not raw comparison. *Precondition: `ITransportFaultInjector` from `networking-test-harness.md` required.*

---

**AC-NC-17 (Integration)** — Given a deterministic load fixture that drives the server through exactly 500 server ticks with 10 scripted entities exchanging `DamageEvent` at every tick (seeded deterministic load generator, no wall-clock dependency), When all emitted R-U batch packets are captured and every `attackerEntityId` and `targetEntityId` field in every `DamageEvent` sub-message is inspected, Then: (a) at least **5,000 `DamageEvent` sub-messages** are captured (10 entities × 500 ticks — verifies the fixture emitted the expected volume); (b) no `attackerEntityId` or `targetEntityId` field contains the value `0` (Invalid) — verifies the CR-NET-7.3 wire-boundary assertion is active for the message type under sustained load. *Story type: Integration. No wall-clock time dependency; fixture setup and teardown must be deterministic.*

---

**AC-NC-18** — Given a test client that sends an `AllocateFreePointRequest` with `StatID` set to a byte value not present in the `StatID` enum (e.g., `0xFF`), When the server receives the message, Then the message is dropped, no stat change is applied, the anomaly is logged, and the server does not crash.

---

**AC-NC-19** — Given three sequential gold mutations on the same character (add 100g, spend 50g, add 200g), When three `GoldSyncEvent` sub-messages are captured from the R-U batch, Then each `NewBalance` field equals the absolute post-mutation balance (100g, 50g, 250g) — not deltas.

---

**AC-NC-21 (Integration)** — Given a deterministic load fixture with 50 simulated clients all in active combat for exactly 200 consecutive server ticks, with a total of **10 `DamageEvent` sub-messages emitted per tick zone-wide** (not per client; corresponds to F-NET-1 Scenario C density), When the total outbound byte count per client is measured across all 200 ticks, Then no client's total exceeds **200 × 1,500 bytes** (= 300,000 bytes, equivalent to ~30 KB/s at 20 Hz). *Threshold: F-NET-1 Scenario C post-filter = ~26.0 KB/s per client (1,330 bytes/tick); 1,500 bytes/tick × 200 ticks provides ~13% headroom for priority-path burst (raised from ~1.4% after Pass 2 relevance-filter revision). Story type: Integration. No wall-clock time dependency.*

---

**AC-NC-28 (Logic)** — Given the server encoding: (a) a Vector3 position of `(12.75, 0.00, −85.23)` metres; (b) a Quaternion rotation of `(0.707, 0.0, 0.707, 0.0)` (normalized 90° Y-rotation); (c) a `cycleTimer` value of `0.37 × CycleDuration`. When a client receives and decodes these values, Then: decoded position is within ±0.01m per axis; decoded quaternion, after client-side renormalization, has dot product ≥ 0.9999997 with the original; decoded `cycleTimer` fraction is 0.37 ± 0.0001. Additionally: boundary-value test at `(±327.67, 0, 0)` must not overflow; degenerate-quaternion test with input `(0, 0, 0, 0)` must encode as identity `(0, 0, 0, 1)` and log an anomaly. *Unit-testable without a running server.*

---

**AC-NC-30 (Integration)** — Given a test client observing a zone where entity B is killed by entity A in a single blow, using `ITransportFaultInjector.DelayNextOutbound(DamageEvent.MessageTypeID, 1, 200)` to hold the damage event past the `KillEvent` arrival: (a) the client displays the killing-blow damage number using `KillEvent.finalDamage` — the number appears before entity B is despawned; (b) the `DamageEvent` for the same `(killerEntityId, targetEntityId)` pair arriving after the `KillEvent` is **discarded at the application layer** — the client's game-logic layer maintains a `lastKillTick[(attackerEntityId, targetEntityId)]` map; any `DamageEvent` whose `ServerTickNumber ≤ lastKillTick` for the same `(attacker, target)` pair is dropped before display and logged as a `StalePostKillDamageEvent` anomaly; (c) the killing-blow damage number appears within **100ms** of the `KillEvent` being received. *Precondition: `ITransportFaultInjector` and `INetworkTestObserver` from `networking-test-harness.md` required.*

---

**AC-NC-31 (Logic)** — Given the serialization layer encoding a valid game message (e.g., `DamageEvent`, `EntityHealthUpdate`, or `KillEvent`) where any ID field (`EntityID`, `ItemID`, `CharacterID`) in the message body is `0` (Invalid), When the serializer attempts to write the message to the wire buffer, Then the serializer throws before writing any bytes for that message, a `InvalidIdZeroWrite` anomaly is logged with the message type and field name, and no partial message appears in the buffer. *Verifies CR-NET-7.3 "must assert before writing" requirement. Unit-testable without a running server.*

---

**AC-NC-32 (Logic)** — Given a receiver processing a message containing a `DamageType` enum field with byte value `100` (outside declared range 0–2), and a separate `DisconnectType` field with byte value `50` (outside declared range 0–2), When each field is range-checked and cast: (a) `DamageType` is substituted with `Physical = 0` and processing continues; (b) `DisconnectType` is substituted with `Timeout = 1` and the entity is still despawned; (c) both substitutions are logged as anomalies with the received byte value and message type. No received message is silently dropped for an unknown enum byte. *Verifies CR-NET-7.4 per-enum fallback table. Unit-testable without a running server.*

---

**AC-NC-33 (Integration)** — Given a load fixture driving the server at Scenario C density (n=50, 10 `DamageEvent` sub-messages/tick zone-wide) for 100 consecutive ticks, with `INetworkTestObserver` capturing all outbound packets, When every R-U batch packet, CycleBroadcast packet, and Position packet body is measured, Then no packet body exceeds `MAX_MESSAGE_BODY_BYTES` (512 bytes). *Verifies CR-NET-7.6 hard cap is enforced by the overflow-drop policy before serialization completes. Overflow-drop correctness is covered separately by OQ-NC-SER-3 load simulation.*

---

**AC-NC-34 (Integration)** — Given a server tick where 12 R-OD messages are queued for client A (4 above `PRIORITY_PATH_CAP = 8`), When the tick's Path 1 flush occurs, Then: (a) exactly 8 messages are emitted in that tick (in emission order); (b) the remaining 4 messages appear in the client's `INetworkTestObserver` capture in the following tick(s), in the same emission order; (c) all 12 messages are delivered within ≤ 2 ticks total — none are dropped. *Verifies CR-NET-7.7 deferral semantics: excess messages are held, not discarded.*

---

**AC-NC-35 (Integration)** — Given a server tick where the Path 1 queue for client A already holds exactly 8 queued R-OD messages (at cap), When an `EnhancementOutcomeBroadcast` is enqueued for client A before the tick's flush, Then: (a) the `EnhancementOutcomeBroadcast` is placed at position 1 in the current tick's Path 1 queue (displacing the oldest non-exempt message to the next tick); (b) the `EnhancementOutcomeBroadcast` appears in the current tick's `INetworkTestObserver` capture; (c) the displaced message appears at position 1 in the following tick's capture. *Verifies the CR-NET-7.7 queue-front-insertion timing rule.*

---

**AC-NC-36 (Integration)** — Given a test connection whose server-side `SequenceNumber` counter is initialised to `4,294,967,293` (3 below `uint.MaxValue`) via `ITransportFaultInjector.SetSequenceNumber`, When the server emits 5 consecutive messages to that client, Then the `SequenceNumber` values observed in order are: `4,294,967,294` → `4,294,967,295` → `1` → `2` → `3` (wraps from max to 1, skipping 0); the receiver's `IsNewerVersion` check accepts all 5 messages; `SequenceNumber = 0` must never appear in any captured message. *Verifies EC-NET-6 wraparound behaviour and the CCR-1 "0 = uninitialized" invariant. Precondition: `ITransportFaultInjector.SetSequenceNumber` from `networking-test-harness.md` required.*

---

**AC-NC-37 (Integration)** — Given a zone with client A (attacker entity) and client B (target entity), When A attacks B in a single server tick, Then: (a) client A's `INetworkTestObserver` capture contains exactly one `SelfDamageEvent` on the R-OD path with `attackerEntityId == A.EntityID` and `targetEntityId == B.EntityID`; (b) client A does **not** receive a `DamageEvent` for that same attack; (c) client B's capture contains exactly one `DamageEvent` in the R-U batch with the same `(attackerEntityId, targetEntityId)` pair; (d) client B's capture contains **no** `SelfDamageEvent`. *Verifies MCR-2 delivery exclusivity: `SelfDamageEvent` → attacker only via R-OD; `DamageEvent` → all non-attacker zone clients via R-U.*

---

**AC-NC-38 (Integration)** — Given a test client that sends a `NotifySkillUsed` RPC in tick T, When tick T completes and the heartbeat timer has not yet elapsed, Then no `HeartbeatMessage` is emitted for tick T (the RPC resets the heartbeat counter). Additionally: when no outbound RPC of any type has been sent for exactly `HEARTBEAT_INTERVAL_SECONDS` after the last packet, a `HeartbeatMessage` is sent; after that `HeartbeatMessage` is sent, a second `HeartbeatMessage` is NOT sent until another `HEARTBEAT_INTERVAL_SECONDS` of silence elapses. *Verifies CR-NET-7.10 skip-on-activity semantics — the heartbeat is not rate-limited independently, only deferred when another packet has already been sent.*

---

**AC-NC-39 (Integration)** — Given a live zone with client A in an active session (`ZoneSessionState == Alive`), When 10 consecutive server ticks are observed via `INetworkTestObserver`, Then: (a) exactly 10 `SelfPositionUpdate` sub-messages appear in client A's R-U batch capture; (b) every captured `entityId` field equals client A's authenticated `EntityID` — any mismatch triggers a `SelfPositionDirectionViolation` anomaly and suppresses reconciliation for that tick; (c) no `SelfPositionUpdate` appears in any other client's R-U batch (`GetOutboundMessageCount` returns 0 for all non-A clients). Additionally: when client A transitions to `ZoneSessionState == Dead` (following `EntityDied` receipt), the server emits zero `SelfPositionUpdate` sub-messages for the duration of the Dead state — resumption begins at the tick following `RespawnConfirmed` delivery and produces a `SelfPositionUpdate` carrying the authoritative respawn coordinates. *Verifies unicast delivery, Dead-state suppression, entityId assertion, and reconciliation restart after respawn. Precondition: `INetworkTestObserver.GetOutboundMessageCount` from `networking-test-harness.md` required.*

---

## Open Questions

**OQ-NC-SER-1 — BLOCKING (library ADR) — Library-provided envelope fields**
The chosen networking library may provide its own sequence number and timestamp fields. The Networking ADR must audit whether library-provided fields satisfy CR-NET-7.1 (`SequenceNumber`, `ServerTickNumber`) or whether a custom envelope layer is required.

**OQ-NC-SER-2 — Session handshake message format**
The session handshake must carry current gold balance, gold version, HP, level, `heldFreePoints`, and zone assignment (CR-NET-6.4 in `networking-session.md`). The complete handshake schema is an open item to be finalized once Character Persistence, Leveling System, and HP sync contracts are authored. Blocking for Character Persistence GDD.

**OQ-NC-SER-3 — BLOCKING (implementation) — Batch sub-message capacity under dense combat load**

*Owner:* network-programmer + performance-analyst.

F-NET-1 provides actual per-entity sub-message sizes and per-scenario overflow analysis (with B-NP-3 uint16 prefix). Key findings: at n=50, R-U batch saturates at 512 bytes (29 HP updates dropped); U-U batch saturates at 502 bytes with zero position updates delivered. These are preliminary calculations requiring load simulation validation.

**Two density tiers must be simulated:**
1. **Baseline density** — as modeled in F-NET-1 Scenarios A–C: ~2 events at n=10, ~5 at n=25, ~10 at n=50.
2. **Peak density** — 4× baseline (coordinated AoE, multi-hit skills): ~8 at n=10, ~20 at n=25, ~40 at n=50.

**Pass/fail thresholds (baseline density):**
- **PASS:** Damage events never dropped at any player count tier; overflow-drop ticks ≤ 20% of all ticks at n=50.
- **FAIL:** Any damage event dropped, OR overflow occurs on >50% of ticks at n=25.

**Pass/fail thresholds (peak density):**
- **PASS at n ≤ 25:** Damage events never dropped.
- **PASS at n=50:** Acceptable if damage events drop on ≤ 10% of ticks; overflow must never cause permanent client desync.
- If n=50 at peak density exceeds the PASS threshold: choose a mitigation from F-NET-1 before implementation begins.

Simulation results must be appended to the Networking ADR before Networking Core implementation begins.

**Third density tier (Zone Instancing mob scenario, required per F-NET-9):**

3. **Mob-density scenario** — n=50 players + m=150 mobs, all in COMBAT_ACTIVE, all mobs taking damage simultaneously. Validate that mob HP event pressure (R-U) does not cause `EntityHealthUpdate` for players to be dropped; validate that mob position overflow (U-U drop policy) stays within the ≤20 dropped mob positions per client threshold established in F-NET-9. Pass: player `EntityHealthUpdate` is never dropped due to mob HP event pressure.

**OQ-NC-SER-4 — BLOCKING (library ADR) — Library-owned vs. application-owned serialization**
After the library ADR is finalized, the technical director must confirm which CR-NET-7 items are library-owned vs. application-owned. Library-owned layers must be validated against CR-NET-7.2 (no float in authoritative state), CR-NET-7.3 (ID reserved values), CR-NET-7.4 (enum range validation), and CR-NET-7.5 (version discard logic).

---

**B-22 Re-validation — Fragment count impact at 17–19 fragments (Zone Instancing amendment, 2026-05-29)**

*OQ-ZI-7 owner: networking-wire-protocol.md. Prior B-22 analysis assumed 7 player-only fragments at n=50. Zone Instancing amendment establishes [17, 19] fragments at MVP full-zone density (n=50 players + M=150 mobs). Re-validation findings:*

1. **Priority cap interaction — unchanged.** Bulk-transfer fragments (`0xF000–0xFFFF`) are exempt from the 8-message Path 1 cap per CR-NET-7.7. This exemption applies regardless of fragment count. At 7 fragments: no cap interaction. At 17–19 fragments: same result — no cap interaction. B-22 cap analysis holds.

2. **B-NP-8 "fully queued before SessionReady" invariant — timing concern at 19 fragments.** Zone Instancing (CR-ZI-8 step 6–7) queues all fragments before emitting `SessionReady`. At 19 fragments × 512B each ≈ 9.7KB enqueued per joining client before `SessionReady` is sent. On a healthy connection all fragments can be transmitted in 1–2 ticks (bulk-transfer path, no cap). The `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` default is sufficient; no protocol change required. Tuning note: at 1% per-fragment packet loss, probability of at least 1 of 19 fragments lost ≈ 17% — clients should expect retransmit on first zone entry approximately 1 in 6 times. This is acceptable for a one-time connection event.

3. **Mass-join serialization burst.** Peak mass-join (10 simultaneous zone entries): 10 × 19 = 190 fragments to serialize = ~97KB queued in a single processing step. This is a performance concern, not a protocol design issue. The Networking ADR load simulation (OQ-NC-SER-3) must include a mass-join scenario at 17–19 fragments per client before implementation begins.

4. **Verdict: no protocol change required.** B-22 analysis generalizes correctly from 7 to 17–19 fragments. OQ-ZI-7 resolved by this note.*
