# Networking Message Criticality Contract

> **Status**: Approved (lean re-review Pass 2, 2026-05-17)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-17
> **Parent**: networking-wire-protocol.md

## Overview

The Message Criticality Contract maps every wire message in Project Iron Grind's networking layer to the design pillar it serves and the delivery guarantee that pillar requires. All channel assignments in `networking-wire-protocol.md` (CR-NET-7.7) are derived from this contract. A message's pillar classification determines which transport channel it must use, what happens when delivery fails, and whether the server may drop it under overflow pressure. Adding a new message to the wire format requires a row in this document before the schema is accepted.

## Player Fantasy

None. This document is a pure infrastructure contract. Its correctness is felt indirectly: when messages are routed to the right channels, the game's three design pillars are preserved under adverse network conditions — charge bars animate smoothly under packet loss (Pillar 2), gold balances stay accurate under zone-wide AoE (Pillar 1), and party HP bars remain readable mid-fight (Pillar 3).

## Detailed Rules

### MCR-1 — Design Pillars and Delivery Guarantees

| Pillar | Definition | Delivery Guarantee Required |
|--------|------------|----------------------------|
| **Pillar 1 — Earned Power** | Permanent state changes that reflect player decisions (gold, stats, items, kills). Must never be silently lost. | R-OD for irreversible one-time events. R-U with version-based self-correction for frequently-mutating authoritative state. Forced R-OD delivery after `GOLD_MAX_CONSECUTIVE_DROP` drops (MCR-4). |
| **Pillar 2 — Rhythm Mastery** | Real-time feedback enabling precise timing. `CycleTimerBroadcast` drives the charge bar and must never be permanently suppressed. `SelfDamageEvent` is the direct feedback signal for a successful timing window and must be guaranteed. | R-OD for direct player feedback signals. R-U for combat state feedback that self-corrects. U-U for high-frequency positional-class data that self-corrects every tick. |
| **Pillar 3 — Social Signals** | Status and presence signals that make the world feel alive. One-time social events must arrive. Continuous data is self-correcting. | R-OD for one-time social events (join, leave, kill broadcast). R-U for continuous party state (party HP). U-U for position. |
| **Infrastructure** | Transport and session lifecycle. No pillar ownership. | R-OD for session state transitions that must not be missed. U-U for probing (self-correcting by design). |

---

### MCR-2 — Per-Message Criticality Table

| Message | Pillar | Delivery Guarantee | Channel | Rationale |
|---------|--------|--------------------|---------|-----------|
| `CycleTimerBroadcast` | Pillar 2 | Self-correcting per-tick | U-U | Fresh value every tick. Separate CycleBroadcast packet guarantees no crowding by position data (PA-P7-05). MUST NOT be dropped from the CycleBroadcast packet — if the packet is lost in transit, the client interpolates for ≤ 3 ticks. |
| `EntityPositionUpdate` | Pillar 3 | Self-correcting per-tick | U-U | Position self-corrects every tick. Clients interpolate stale values. |
| `SelfDamageEvent` | Pillar 2 | Guaranteed delivery | R-OD | Direct feedback for player's own timing window. Must arrive once in order. Sent only to the attacker's own client. |
| `DamageEvent` | Pillar 2 | Best-effort | R-U | Cosmetic for non-attacker clients. High-frequency; missing one is unnoticeable. |
| `EntityHealthUpdate` | Pillar 2 + Pillar 3 | Reliable per-tick | R-U | Tactical combat feedback (Pillar 2) + party-visible health bar data (Pillar 3 — continuous state, not a one-time event; R-U is appropriate). Self-corrects on next tick. Relevance-filtered per `networking-relevance-filter.md`. |
| `AutoFaceEvent` | Pillar 2 | Best-effort | R-U | Entity orientation during combat. Visual only; stale value is tolerable for ≤ 2 ticks. Event-driven — missed event is not displayed (see MCR-5b). |
| `SkillUsedNotification` | Pillar 2 | Best-effort | R-U | Audio/visual trigger. Missing one is acceptable — idempotent display. Event-driven (see MCR-5b). |
| `ConnectionQualityUpdate` | Pillar 2 | Reliable | R-U | OWL compensation feedback for the local player only. Sent only to affected client on threshold crossings. Event-driven — missed crossing tolerable if next threshold crossing reflects current OWL state (see MCR-5b). |
| `PartyMemberHealthUpdate` | Pillar 1 + Pillar 3 | Reliable per-tick | R-U | Party HP bars are fellowship-critical (Pillar 3). XP-eligibility sequencing and death-state ordering depend on party HP reaching 0 (Pillar 1). **Ordering note:** `GhostPromotionEvent` (R-OD) is the authoritative death signal — client must not apply ghost-state transition until `GhostPromotionEvent` arrives; HP=0 is a precursor indicator only. **MCR-3 exception:** HP data self-corrects on next tick; death-state outcome guaranteed by `GhostPromotionEvent` (R-OD). |
| `GoldSyncEvent` | Pillar 1 | Reliable with forced fallback | R-U | Frequent mutations; version-based self-correction on next tick. Forced R-OD delivery after `GOLD_MAX_CONSECUTIVE_DROP` consecutive drops (MCR-4). |
| `LootBidUpdate` | Pillar 1 + Pillar 3 | Best-effort | R-U | Real-time auction feed. **MCR-3 exception:** bid data self-corrects on next bid update (each new bid supersedes the previous). Economic outcome guaranteed via `AuctionResolved` (R-OD). Consistent with `GoldSyncEvent` pattern — transient self-correcting economic data, not a one-time irreversible event. |
| `PlayerJoinedZone` | Pillar 3 | Guaranteed delivery | R-OD | One-time social event. Must arrive before any other message referencing this EntityID. |
| `PlayerLeftZone` | Pillar 3 | Guaranteed delivery | R-OD | Entity despawn must be authoritative — missing this causes phantom entities. |
| `GhostPromotionEvent` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Permanent session state change (Pillar 1: loot/XP eligibility affected) + zone-wide ghost state visibility (Pillar 3). Must arrive before any tick-rate message referencing this entity's `IsGhost` flag. |
| `GhostExpiredEvent` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Ghost session ends — entity despawns (Pillar 3: zone client cleanup) and session state terminates (Pillar 1: eligibility cleared). Authoritative despawn signal. |
| `GhostDismissRequest` | Pillar 3 | Guaranteed delivery | R-OD | Party member's voluntary ghost dismissal request (client → server). Irreversible social action affecting party composition — must reach server. |
| `GhostDismissRejected` | Pillar 3 | Guaranteed delivery | R-OD | Server-to-requesting-client validation error response when `GhostDismissRequest` fails. Must reach client to unblock party UI pending state (party frame shows ghost-dismiss as pending until success or rejection arrives). Schema pending in networking-channel-contract.md. |
| `KillEvent` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Kill credit (Pillar 1) and social broadcast (Pillar 3). Multi-pillar: highest guarantee wins (MCR-3). |
| `GroundItemSpawned` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Item drop is a permanent economic event (Pillar 1 — must know about it to pick it up). Sent to all party members for auction items (Pillar 3 social coordination). Missing = player never sees a drop they should receive. |
| `GroundItemAssigned` | Pillar 1 | Guaranteed delivery | R-OD | Assignment change is a one-time permanent economic event — which character receives the item. |
| `GroundItemDespawned` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Item expiry is an economic event (Pillar 1 — item gone) + zone-wide visual cleanup (Pillar 3). Missing causes phantom item beacons. |
| `AuctionResolved` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Permanent economic outcome (Pillar 1 — who wins the item, gold distribution) + social signal (Pillar 3 — result visible to all party members). |
| `LootBidRequest` | Pillar 1 | Guaranteed delivery | R-OD | Irreversible economic action (client → server). Bid submission must reach server. |
| `BagFullPickupBlocked` | Pillar 1 | Guaranteed delivery | R-OD | Informs player their item could not be picked up — needed to make informed inventory management decisions. |
| `GroundItemExpiryWarning` | Pillar 1 | Guaranteed delivery | R-OD | Time-sensitive notification about item expiry — player needs this to make informed pickup/discard decisions before item is lost. |
| `ClientBackgrounded` | Infrastructure | Guaranteed delivery | R-OD | Session lifecycle — triggers item TTL extension (CR-LT-13.1). Must not be missed. |
| `ClientForegrounded` | Infrastructure | Guaranteed delivery | R-OD | Session lifecycle — ends TTL extension window. Must not be missed. |
| `PartyStateUpdate` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Authoritative full party state snapshot (Pillar 1: XP eligibility, loot round-robin cursor) + social presence (Pillar 3: party composition visible to members). Must arrive for party HUD to be correct. |
| `PartyDisbanded` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Party dissolution affects loot distribution and XP eligibility (Pillar 1) + dissolves the social unit (Pillar 3). One-time event — missing causes phantom party state. |
| `PartyInviteRequest` | Pillar 3 | Guaranteed delivery | R-OD | Initiating a social connection (client → server). Must reach server to create the invite. |
| `PartyInviteReceived` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Invite notification is a social signal (Pillar 3) with downstream economic consequences (Pillar 1: joining affects loot/XP eligibility). One-time event — missing means player never sees the invite. |
| `PartyInviteResponse` | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Accept/decline is social (Pillar 3) with permanent economic implications (Pillar 1: party membership affects loot/XP). Irreversible. |
| `DiscardRequest` | Pillar 1 | Guaranteed delivery | R-OD | Irreversible economic action (client → server — destroying an item). Must reach server. |
| `DiscardResult` | Pillar 1 | Guaranteed delivery | R-OD | Confirms irreversible economic outcome. Prevents double-discard; player needs explicit confirmation. |
| `MoveRequest` | Pillar 1 | Guaranteed delivery | R-OD | Inventory rearrangement (client → server). Inventory state is authoritative economic state — must reach server. |
| `MoveResult` | Pillar 1 | Guaranteed delivery | R-OD | Authoritative post-operation inventory state for both affected slots. Required for optimistic UI rollback. |
| `InventoryFullNotification` | Pillar 1 | Guaranteed delivery | R-OD | Informs player their bag is full — affects loot pickup decisions. Rate-limited (30s deduplication). |
| `OpenNPCInteraction` | Pillar 1 | Guaranteed delivery | R-OD | Initiates an economic session (client → server). Must reach server to set NPCInteractionActive. Missed delivery leaves client blocked in shop-open pending state. |
| `NPCInteractionOpened` | Infrastructure | Guaranteed delivery | R-OD | Session state signal (server → owning client). Must arrive to open the shop window and start client-side SESSION_TTL countdown. |
| `CloseNPCInteraction` | Infrastructure | Guaranteed delivery | R-OD | Voluntary session close (client → server). Must reach server to clear NPCInteractionActive; missed delivery holds the session open until SESSION_TTL_SECONDS expires. |
| `BuyRequest` | Pillar 1 | Guaranteed delivery | R-OD | Irreversible economic action (client → server). `requestId` idempotency key per ADR-001. Gold debit + item grant are permanent state changes. |
| `BuyResult` | Pillar 1 | Guaranteed delivery | R-OD | Authoritative purchase outcome (server → owning client). Carries new gold balance. Client must not apply balance update until receipt — no optimistic update. |
| `SellRequest` | Pillar 1 | Guaranteed delivery | R-OD | Irreversible economic action (client → server). Item removal + gold credit are permanent. `requestId` idempotency per ADR-001. |
| `SellResult` | Pillar 1 | Guaranteed delivery | R-OD | Authoritative sell outcome. `goldEarned` carries actual credited amount (may be capped by GOLD_CAP — npc-shop.md Edge Cases). |
| `RejectedNotInTownHub` | Infrastructure | Guaranteed delivery | R-OD | Session validation failure response (OpenNPCInteraction). Must arrive to release client shop-open pending state. |
| `RejectedNoNPCSession` | Pillar 1 | Guaranteed delivery | R-OD | Request rejection (BuyRequest or SellRequest). Must arrive to release client transaction pending state (UI shows spinner until response). `requestId` correlates to rejected request. |
| `RejectedInvalidQuantity` | Pillar 1 | Guaranteed delivery | R-OD | Request rejection — must arrive to release pending state. Same `requestId` correlation. |
| `RejectedItemNotInCatalog` | Pillar 1 | Guaranteed delivery | R-OD | Buy rejection — item not in shop catalog. Must arrive to release pending state. |
| `RejectedInsufficientFunds` | Pillar 1 | Guaranteed delivery | R-OD | Buy rejection — gold balance insufficient. Client refreshes quantity selector from `GoldSyncEvent` on receipt. |
| `RejectedInventoryFull` | Pillar 1 | Guaranteed delivery | R-OD | Buy rejection — net gold effect is zero (compensating AddGold executed server-side before this message is sent). Client's gold balance is unchanged. |
| `RejectedRateLimited` | Infrastructure | Guaranteed delivery | R-OD | Rate-limit response (10 req/sec per character per ADR-001). Must arrive to release pending state and suppress client retry. |
| `RejectedInvalidSlot` | Pillar 1 | Guaranteed delivery | R-OD | Sell rejection — slotIndex out of range [0, 19]. Must arrive to release pending state. |
| `RejectedItemMismatch` | Pillar 1 | Guaranteed delivery | R-OD | Sell rejection — slot contents changed since Sell tab render. Client must refresh Sell tab on receipt. |
| `RejectedSlotLocked` | Pillar 1 | Guaranteed delivery | R-OD | Sell rejection — slot locked by Enhancement System between render and execution. Must arrive to release pending state. |
| `RejectedUnsellable` | Pillar 1 | Guaranteed delivery | R-OD | Sell rejection — SellPriceGold = 0 server guard (item bypassed client-side Sell tab filter). |
| `UseItemRequest` | Pillar 1 | Guaranteed delivery | R-OD | Irreversible item-use action (client → server). Consuming a potion permanently removes it from inventory and changes HP/MP. `requestId` idempotency per ADR-001 prevents double-consumption on retransmit. |
| `UseItemResult` | Pillar 1 | Guaranteed delivery | R-OD | Authoritative item-use outcome (server → owning client). Carries HP/MP value and updated inventory count. Must arrive — a lost result leaves client display in the predicted state with no self-correction path. |
| `UseItemRejected` | Pillar 1 | Guaranteed delivery | R-OD | Item-use rejection (server → owning client). Must arrive to release client predict state — a lost rejection permanently greys the hotbar slot and locks the cooldown timer with no self-correction mechanism. |
| `EnhancementAttemptRequest` | Pillar 1 | Guaranteed delivery | R-OD | Irreversible player action (client → server). |
| `EnhancementRequestReceived` | Pillar 1 | Guaranteed delivery | R-OD | Acknowledgment of irreversible action. Enhancement-path cap exemption applies. |
| Enhancement outcome broadcast *(schema pending)* | Pillar 1 + Pillar 3 | Guaranteed delivery | R-OD | Permanent item change (Pillar 1) + social signal (Pillar 3). Schema defined in Enhancement System GDD. |
| Level-up event + stat snapshot *(schema pending)* | Pillar 1 | Guaranteed delivery | R-OD | Permanent progression event. Schema defined in Leveling System GDD. |
| `AllocateFreePointRequest` / Response *(schema pending)* | Pillar 1 | Guaranteed delivery | R-OD | Permanent stat change. Schemas defined in Character Stats GDD. |
| Respec Phase 1/2 *(schemas pending)* | Pillar 1 | Guaranteed delivery | R-OD | Permanent stat respec. Schemas defined in Character Stats GDD. |
| Item consumption confirmation *(schema pending)* | Pillar 1 | Guaranteed delivery | R-OD | Permanent inventory change. Schema defined in Inventory System GDD. |
| `SetTarget` | Infrastructure | Guaranteed delivery | R-OD | Persistent server state change — updates the client's EntityHealthUpdate relevance set (networking-relevance-filter.md RFR-3a). A dropped `SetTarget` leaves the target-slot EHU stale indefinitely. Direction C→S. |
| `SessionHandshake` | Infrastructure | Guaranteed delivery | R-OD | Zone entry session establishment (client → server). |
| `SessionReady` | Infrastructure | Guaranteed delivery | R-OD | Zone-entry dual-gate signal (server → owning client). |
| `ZoneSessionEnded` | Infrastructure | Guaranteed delivery | R-OD | Authoritative zone teardown. |
| `ZoneStateSnapshotFragment` | Infrastructure | Guaranteed delivery | R-OD (bulk) | Fragment reassembly protocol. Uses `0xF000–0xFFFF` MessageTypeID range. |
| `HeartbeatMessage` | Infrastructure | Self-correcting | U-U | Keepalive. Dropped heartbeat self-corrects — next heartbeat resets timeout. |
| `RttProbe` / `RttProbeEcho` | Infrastructure | Self-correcting | U-U | RTT measurement. Loss is acceptable; next probe corrects the OWL estimate. |

**Schema-pending rows:** Five Pillar 1 messages listed above have schemas defined in downstream GDDs (Enhancement System, Leveling System, Character Stats, Inventory System). Each GDD must add the message schema to `networking-wire-protocol.md` and confirm the R-OD channel assignment when that GDD is authored. No schema-pending message may be dispatched in a build until its schema is defined.

---

### MCR-3 — Multi-Pillar Resolution

When a message serves multiple pillars with conflicting delivery requirements, the higher-guarantee channel wins:

- Pillar 1 (guaranteed) takes precedence over Pillar 2 (self-correcting) and Pillar 3 (self-correcting)
- Pillar 3 (one-time social event) takes precedence over Pillar 2 (real-time feedback) when both require reliable delivery
- Enhancement outcome: Pillar 1 + Pillar 3 → R-OD (both require it independently)
- KillEvent: Pillar 1 + Pillar 3 → R-OD (both require it independently)

The resolution applies to guaranteed-delivery messages: any message touching a pillar that requires R-OD must resolve to R-OD. **Exception for self-correcting data:** If a message's content is continuously overwritten by subsequent messages of the same type (every new message fully supersedes the last), it may remain on R-U even when tagged with a Pillar 1 classification, provided the ultimate economic outcome is guaranteed by a separate R-OD message. Such exceptions must be documented explicitly in the MCR-2 rationale column (see `GoldSyncEvent` and `LootBidUpdate`). If a new message's resolution is ambiguous, escalate to the game-designer before the schema is added.

---

### MCR-4 — GoldSyncEvent Forced Delivery Policy

When `GoldSyncEvent` for a given client has been overflow-dropped for `GOLD_MAX_CONSECUTIVE_DROP` consecutive ticks, the server MUST emit a standalone `GoldSyncEvent` on the R-OD priority path on the next available tick. The forced delivery uses a `MessageTypeID` within the `0xE000–0xEFFF` priority range.

**Priority path cap interaction:** Forced `GoldSyncEvent` delivery is subject to `PRIORITY_PATH_CAP`. If the priority path is at cap on the trigger tick, the forced delivery is placed at the front of the next tick's priority queue ahead of non-exempt messages (behind enhancement-path-exempt messages). "Regardless of batch pressure" in the original intent refers to R-U batch overflow — it does not bypass the R-OD priority path cap.

**Consecutive-drop counter semantics:** The counter increments on each tick where `GoldSyncEvent` is overflow-dropped for a given client. The counter resets to 0 on the first tick where `GoldSyncEvent` is successfully delivered via either the R-U batch or the forced R-OD path. The counter does not reset on zone transition or disconnect — only on confirmed delivery.

**Anomaly threshold:** This is a safety valve — it signals a systemic bandwidth problem, not normal behavior. If forced delivery has fired for more than `FORCED_DELIVERY_CONSECUTIVE_TICKS` consecutive ticks without a successful normal R-U delivery for any single client, the server logs a `GoldSyncForcedDelivery` critical anomaly including the affected EntityID and the consecutive forced-delivery tick count. At the default of 100 ticks (5 seconds at 20Hz), the anomaly captures sustained degradation while ignoring brief AoE overflow bursts that self-resolve.

---

### MCR-5 — Permanent Suppression Invariant

A message is **permanently suppressed** when it is never delivered and no self-correction mechanism exists within the same pillar.

#### MCR-5a — Periodic Self-Correcting Messages

- **Pillar 1 messages (R-OD):** Permanent suppression is never permitted. The transport retries until delivered or the session is terminated.
- **`SelfDamageEvent` (R-OD):** Same invariant as Pillar 1. If delivery fails at the transport layer (session terminated), the feedback is lost — no compensation mechanism. If the session is active and the message is not acknowledged within the transport's retry window, the transport retransmits (R-OD guarantee).
- **Pillar 2 U-U messages (`CycleTimerBroadcast`):** Packet loss is acceptable. The client interpolates using the last received value for at most 3 consecutive lost packets (150ms at 20Hz). After 3 consecutive losses the charge bar may show stale position, but must not freeze or reset. This is a transport constraint — the server cannot guarantee U-U delivery and is not responsible for client-side graceful degradation.
- **Pillar 2 R-U periodic messages (`DamageEvent`, `EntityHealthUpdate`, `GoldSyncEvent`, `PartyMemberHealthUpdate`):** Overflow drops are acceptable if the next tick delivers a fresh self-correcting value. A value is self-correcting when the receiver can display a correct state from the next message of the same type without requiring the dropped message.

#### MCR-5b — Event-Driven Messages

Some R-U messages are event-driven rather than periodic — they fire once in response to a specific trigger and carry data that is **not** superseded by a subsequent message of the same type. For these messages, an overflow drop is a permanently missed event with no self-correction:

- **`AutoFaceEvent`, `SkillUsedNotification`, `ConnectionQualityUpdate`:** A dropped event means the associated visual, audio, or OWL compensation trigger is silently missed. This is accepted: these messages are designed such that a single missed event does not cause incorrect persistent game state — entity orientation recovers from the next update; skill audio/visual is cosmetic; OWL state is re-evaluated on the next threshold crossing.
- **Rule:** Event-driven R-U messages must be designed such that a dropped message leaves the client in a tolerable display state, not in an incorrect authoritative state. If a missed event would cause persistent incorrect game state, the message must be classified as R-OD instead.

## Formulas

### F-MCR-1 — Forced Gold Delivery Rate (maximum tolerable)

```
ForcedDeliveryRate_per_client = TICK_RATE_HZ ÷ (GOLD_MAX_CONSECUTIVE_DROP + 1)
```

**Variables:**

| Variable | Definition | Units | Valid Range |
|----------|-----------|-------|-------------|
| `ForcedDeliveryRate_per_client` | R-OD forced deliveries per second, per client in sustained overflow | deliveries/s | [3.3, 10] at default tuning range |
| `TICK_RATE_HZ` | Server tick rate | Hz | 20 (fixed) |
| `GOLD_MAX_CONSECUTIVE_DROP` | Maximum ticks `GoldSyncEvent` may be overflow-dropped before forced R-OD delivery | ticks | [1, 5] (see Tuning Knobs) |

**Output range:** 3.3/s at `GOLD_MAX_CONSECUTIVE_DROP = 5` (minimum forced rate, most tolerant) to 10/s at `GOLD_MAX_CONSECUTIVE_DROP = 1` (maximum forced rate, most accurate).

At `GOLD_MAX_CONSECUTIVE_DROP = 3`, `TICK_RATE_HZ = 20`:
- A single client in sustained R-U overflow triggers forced R-OD delivery every 4 ticks (200ms cycle)
- `ForcedDeliveryRate = 20 ÷ 4 = 5 forced deliveries/second per overflowing client`

At n=50 with all clients simultaneously in sustained overflow (pathological case):
```
ForcedDeliveryRate_total = 5 × 50 = 250 forced deliveries/second (zone-wide)
```

This rate is only reachable in a sustained AoE scenario where all 50 clients have full R-U batches for every tick. At 20 bytes per forced `GoldSyncEvent` + 10-byte envelope, zone-wide pathological traffic = 250 × 30 bytes = 7.5 KB/s additional R-OD overhead (within the F-NET-2 server headroom, but warrants anomaly logging per MCR-4). Under normal play the forced delivery rate is near zero.

**Boundary values:**

| `GOLD_MAX_CONSECUTIVE_DROP` | Forced delivery rate (1 client, sustained overflow) | Max gold display lag |
|-----------------------------|-----------------------------------------------------|----------------------|
| 1 | 10/s | 100ms |
| 3 *(default)* | 5/s | 200ms |
| 5 | 3.3/s | 300ms |

## Edge Cases

### EC-MCR-1 — Message Added Without Classification

If a message type is dispatched at runtime with no MCR-2 row, the dispatcher routes it to R-U as the safe default and logs an `UnclassifiedMessageType` anomaly containing the `MessageTypeID`. The message is not dropped — it is delivered best-effort. The anomaly is fatal in debug builds (assert-on-unclassified) and advisory in release builds. This ensures unclassified messages surface during development, not in production.

---

### EC-MCR-2 — SelfDamageEvent Not Received

If the client does not receive a `SelfDamageEvent` within 5 tick periods (250ms) of the triggering beat resolution, the client must suppress the damage number entirely — no phantom display. The client must not substitute a `DamageEvent` as a fallback: `DamageEvent` is sent to all zone clients *except* the attacker (the server routes the attacker's own feedback exclusively via `SelfDamageEvent`). A `DamageEvent` matching the attacker's own `attackerEntityId` will never arrive at the attacker's client, making any such fallback path unreachable. The client must also suppress any `SelfDamageEvent` whose `ServerTickNumber` is more than 5 ticks older than the current server tick — stale delivery must not display phantom damage numbers.

---

### EC-MCR-3 — Multi-Pillar Conflict Cannot Be Resolved Automatically

If a proposed new message serves two pillars where the resolution rule (MCR-3) produces an ambiguous result (i.e., both pillars require the same guaranteed channel but for different reasons), the game-designer must document the primary pillar in the MCR-2 table before the schema is merged. The resolution rule is deterministic for all currently-defined messages; this edge case applies only to future messages that cross pillar boundaries in unexpected ways.

---

### EC-MCR-4 — Schema-Pending Message Built Into a Binary

If a schema-pending message (Enhancement outcome, level-up, AllocateFreePoint, Respec, item consumption) is dispatched in a build before its schema is defined in `networking-wire-protocol.md`, the dispatcher must reject it at registration time — a `PendingSchemaDispatch` fatal error must be thrown during server startup, not at runtime dispatch. This prevents silent malformed packets from reaching clients.

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-core.md` | Parent — defines the three design pillars and authority model that this contract formalises |
| `networking-wire-protocol.md` | Consumer — CR-NET-7.7 channel assignments are derived from MCR-2; must be updated whenever MCR-2 changes |
| `networking-channel-contract.md` | Extension — builds an authoritative per-message direction/channel/invariant table on top of the pillar classifications defined here |
| `networking-relevance-filter.md` | Peer — defines the relevance set that governs EntityHealthUpdate delivery (MCR-2 row) |
| `networking-session.md` | Consumer — session lifecycle messages (SessionReady, ZoneSessionEnded) rely on the R-OD Infrastructure classification |
| All game system GDDs | Any GDD that introduces a new RPC or server-emitted event must add a row to MCR-2 before that schema is accepted into `networking-wire-protocol.md` |
| `consumable-use-system.md` | Downstream — `UseItemRequest`, `UseItemResult`, `UseItemRejected` MCR-2 rows added 2026-06-09 (OQ-CUS-2 resolved). All three Pillar 1, Guaranteed delivery, R-OD. |

**Bidirectional note:** `networking-wire-protocol.md` must add a reference to this document in its Dependencies section, since CR-NET-7.7 is now derived from MCR-2.

## Tuning Knobs

| Knob | Default | Safe Range | Impact |
|------|---------|------------|--------|
| `GOLD_MAX_CONSECUTIVE_DROP` | 3 | [1, 5] | Maximum ticks `GoldSyncEvent` may be overflow-dropped before forced R-OD delivery. Lower values increase gold display accuracy at the cost of additional R-OD traffic during n=50 overflow bursts. At 1, gold is always delivered within 2 ticks (100ms); at 5, up to 300ms display lag in sustained overflow. **Values outside [1, 5] are not meaningful:** 0 disables forced delivery entirely (GoldSyncEvent permanently subject to overflow drops with no R-OD fallback); 6+ risks noticeable gold discrepancies under normal play. |
| `FORCED_DELIVERY_CONSECUTIVE_TICKS` | 100 | [60, 300] | Consecutive ticks of forced `GoldSyncEvent` delivery (without a successful normal R-U delivery) before logging a `GoldSyncForcedDelivery` critical anomaly. At 100 ticks (5 seconds at 20Hz), captures sustained bandwidth degradation while ignoring brief AoE burst scenarios that self-resolve within 1–2 seconds. Lower values increase anomaly sensitivity at the cost of false positives during normal peak-combat overflow; higher values delay detection of persistent bandwidth problems. |

## Acceptance Criteria

**AC-MCR-01 (Integration)** — Given a simulated zone with 50 players in active combat causing R-U batch overflow for 3 consecutive ticks for at least one client, When `GoldSyncEvent` for that client is overflow-dropped on ticks T, T+1, and T+2, Then on tick T+3 the server emits a standalone `GoldSyncEvent` on the R-OD priority path, And the client's gold balance display matches the server's authoritative balance within 1 tick of receiving the forced delivery. *Requires `INetworkTestObserver` from `networking-test-harness.md` to observe forced R-OD emission. Also requires `IZoneTestConfigurator.SetBatchSizeLimit(int bytes)` to artificially constrain the R-U batch capacity and trigger deterministic overflow — without this injection point, overflow cannot be forced reliably in an integration test environment. If `SetBatchSizeLimit` is not present in `IZoneTestConfigurator`, this AC is blocked pending test harness update.*

**AC-MCR-02 (Integration)** — Given a client whose character successfully lands an auto-attack timing window, When the server processes the beat resolution on the server tick, Then the attacker's own client receives a `SelfDamageEvent` via the R-OD priority path within 2 tick periods (100ms), And the `finalDamage` value in `SelfDamageEvent` matches the `DamageResult.FinalDamage` computed by the Damage Calculation system for that hit. *Requires `INetworkTestObserver` to capture the SelfDamageEvent path separately from the zone-wide DamageEvent.*

**AC-MCR-03 (Logic)** — Given a `MessageTypeID` value that has no row in the MCR-2 criticality table, When the server's message dispatcher attempts to register a handler for that type, Then in debug builds the registration raises a `PendingSchemaDispatch` fatal error at startup; in release builds the message is routed to R-U and a `UnclassifiedMessageType` anomaly is logged containing the `MessageTypeID`. No crash occurs in either configuration.

**AC-MCR-04 (CI)** — Given the set of `MessageTypeID` values defined in `networking-wire-protocol.md` and the set of message rows in MCR-2, When a CI check compares both sets, Then every defined `MessageTypeID` appears in exactly one MCR-2 row, and every MCR-2 row references a defined schema or is explicitly marked `*(schema pending)*`. Any mismatch fails the CI gate.

**AC-MCR-05 (Integration)** — Given a client receiving `CycleTimerBroadcast` at 20Hz, When `ITransportFaultInjector` drops 3 consecutive CycleBroadcast packets, Then the charge bar animation continues advancing at the correct rate using client-side linear interpolation between the last two received values, and when the 4th packet arrives the charge bar position differs from the server's authoritative position by no more than 10% of full cycle. *Interpolation algorithm: linear extrapolation from the last two received `cycleTimer` values. The 10% tolerance bounds linear extrapolation error across 3 lost packets (150ms at 20Hz) assuming maximum cycle speed ≤ 1.0 full cycle/second. Requires `ITransportFaultInjector` and `INetworkTestObserver` from `networking-test-harness.md`.*

**AC-MCR-06 (Logic)** — Given any message in the MCR-2 table tagged with multiple pillars, When the MCR-3 resolution rule is applied (R-OD > R-U > U-U), Then the Channel column must reflect the highest-guarantee channel among all tagged pillars, except for messages explicitly documented with an MCR-3 exception in their rationale column (`GoldSyncEvent`, `LootBidUpdate`, `PartyMemberHealthUpdate`). Pass criterion: for every multi-pillar row without an explicit MCR-3 exception, `channel = max_guarantee(pillar_channels)`. Verified by design review of the MCR-2 table and automated MCR-2 audit at CI time (AC-MCR-04).

**AC-MCR-07 (Integration)** — Given a client where `GoldSyncEvent` forced delivery has fired continuously for `FORCED_DELIVERY_CONSECUTIVE_TICKS` consecutive ticks without a successful normal R-U delivery, When the consecutive threshold is reached, Then the server logs a `GoldSyncForcedDelivery` critical anomaly containing the affected EntityID and the consecutive forced-delivery tick count. *Requires `IZoneTestConfigurator.SetBatchSizeLimit()` to maintain forced delivery condition, and `INetworkTestObserver` to capture server log output.*

**AC-MCR-08 (Integration)** — Given a client receiving `CycleTimerBroadcast` at 20Hz, When `ITransportFaultInjector` drops all CycleBroadcast packets for exactly 3 consecutive ticks, Then (a) the charge bar position must be monotonically non-decreasing throughout the drop window — the bar must not freeze at a fixed value or reset to zero — and (b) when the 4th packet arrives, charge bar position must converge to within 10% of the server's authoritative position per the AC-MCR-05 tolerance. *Requires `ITransportFaultInjector` and a mechanism to sample charge bar state during the drop window via `INetworkTestObserver`.*
