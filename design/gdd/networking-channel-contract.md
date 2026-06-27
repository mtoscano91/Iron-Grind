# Networking Channel Contract

> **Status**: In Review — Revision Pass 1 (2026-05-18; applying 14-blocker fixes)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-22 (Pass 1 continued: added EquipRequest, EquipResult, AppearanceChangedEvent CCR-3 rows — Equipment System upstream contract, 2026-05-22)
> **Parent**: networking-wire-protocol.md

## Overview

The Channel Contract is the authoritative per-message routing table for Project Iron Grind's networking layer. For every wire message it specifies: the allowed direction (client→server, server→owning client, or server→all zone clients), the assigned channel (R-OD / R-U / U-U), the delivery context (batch membership or standalone path), and the invariant that must hold if the assignment is changed. Channel assignments here are derived from `networking-message-criticality.md` (MCR-2). This document resolves two open ambiguities from `networking-wire-protocol.md`: `SequenceNumber` counter scope and the `SessionHandshake` naming collision.

## Player Fantasy

None. This document is a pure infrastructure contract. Its correctness ensures that messages arrive at the right clients on the right channels — wrong directions cause phantom state on unintended clients, wrong channels lose Pillar guarantees.

## Detailed Rules

### CCR-1 — SequenceNumber Counter Scope

The `SequenceNumber` field in the 10-byte message envelope (CR-NET-7.1) is a **per-connection counter, shared across all message types** sent by that endpoint. A single monotonically-increasing counter is maintained per client connection. Implementors must not maintain separate per-message-type counters — doing so makes stale-discard fail silently across types (e.g., a `HeartbeatMessage` advances the shared counter, so the next `RttProbeEcho` correctly appears "newer" to the stale-discard comparison). Counter starts at 1; 0 = uninitialized and must never appear in a valid message.

---

### CCR-2 — SessionHandshake Naming Disambiguation

Two distinct session-establishment flows exist. Their naming must not be confused:

| Flow | Message name | Direction | Notes |
|------|-------------|-----------|-------|
| Zone entry request | `SessionHandshake` | C → S | Schema pending OQ-NC-SER-2. Only a client→server message exists with this name. |
| Zone entry response | `SessionReady` | S → C | Carries the player's own initial state. Part of the zone-entry dual-gate. |
| Zone state seed | `ZoneStateSnapshotFragment` | S → C | Bulk-transfer fragments; reassembled into an in-memory `ZoneStateSnapshot`. Not a wire message type by itself. |

`ZoneStateSnapshot` is the **in-memory reassembly result** after all `ZoneStateSnapshotFragment` messages are received. It is not a wire message type. Any reference to "server SessionHandshake" in code or documentation is incorrect; the intended concept is `SessionReady` (own player state) combined with `ZoneStateSnapshot` reassembly (zone entity state).

---

### CCR-3 — Per-Message Direction/Channel/Invariant Table

**Direction codes:** `C→S` client to server · `S→C` server to owning/affected client only · `S→ALL` server to all zone clients · `S→RELEVANT` server to clients for whom the entity is in their relevance set · `S→PARTY` server to all current party members (party membership defines the recipient set; subset of zone clients; resolves to S→C for solo players).

**Channel codes:** `R-OD` reliable ordered · `R-U` reliable unordered · `U-U` unreliable unordered.

**Delivery context codes:** `P1` Path 1 (priority, standalone) · `RU-B` R-U batch (Path 2a) · `CC-B` CycleBroadcast packet (Path 2b-i) · `POS-B` Position packet (Path 2b-ii).

| Message | Direction | Channel | Context | Key Invariant |
|---------|-----------|---------|---------|---------------|
| `CycleTimerBroadcast` | S→ALL (per-client tailored) | U-U | CC-B | Must remain in its own CycleBroadcast packet (Path 2b-i). Never merged with Position packet. One entry per zone entity other than the receiver. |
| `SelfDamageEvent` *(new)* | S→C (attacker only) | R-OD | P1 | Sent to the attacker's own client exclusively. Must never be broadcast to other zone clients. Same fields as `DamageEvent`. |
| `DamageEvent` | S→ALL | R-U | RU-B | Best-effort. Written first in R-U batch serialization order. Never sent to the attacker's own client as a self-damage notification — that role belongs to `SelfDamageEvent`. |
| `EntityHealthUpdate` | S→RELEVANT | R-U | RU-B | Sent only to clients for whom the entity is in their relevance set (party member or current target). See `networking-relevance-filter.md`. |
| `GoldSyncEvent` | S→C | R-U | RU-B | Sent only to the owning player's client. Overflow-dropped first under R-U batch pressure (B-SD-3). Absolute balance only — no deltas. **Forced R-OD path:** after `GOLD_MAX_CONSECUTIVE_DROP` consecutive overflow drops the server emits this message on R-OD (P1), placed at the front of the next tick's priority queue behind enhancement-exempt messages and ahead of non-exempt messages. The forced path uses the same `MessageTypeID` within the `0xE000–0xEFFF` priority range. See MCR-4. |
| `ConnectionQualityUpdate` | S→C (affected only) | R-U | RU-B | Sent only to the affected client on OWL threshold crossings. Never zone-wide. |
| `PartyMemberHealthUpdate` | S→C (per-client for party) | R-U | RU-B | Sent per-client about that client's party members. **Not in the Position packet (Path 2b-ii).** Reliable delivery required — party HP bars are fellowship-critical. |
| `AutoFaceEvent` | S→ALL | R-U | RU-B | Zone-wide. Quaternion + direction vector guards from CR-NET-7.2 apply. |
| `SkillUsedNotification` | S→ALL | R-U | RU-B | Zone-wide. Idempotent on client. |
| `LootBidUpdate` | S→PARTY | R-U | RU-B | MCR-3 exception: bid data self-corrects on each new bid (every new bid supersedes the last); `AuctionResolved` guarantees the economic outcome (R-OD). Sent to all party members participating in the loot instance. |
| `EntityPositionUpdate` | S→ALL (per-client tailored) | U-U | POS-B | Per-client: includes all zone entities except the receiver. Sorted ascending by EntityID. Highest EntityIDs dropped first under overflow. |
| `PlayerJoinedZone` | S→ALL | R-OD | P1 | **Server-side emission ordering guarantee:** the server must not include this EntityID in any R-U batch or U-U packet until `PlayerJoinedZone` has been placed in the R-OD send queue for the target client. Ordering across transport paths is not guaranteed at the receiver. **Client requirement:** buffer or discard messages referencing an unknown EntityID for up to 5 ticks; apply buffered messages after `PlayerJoinedZone` is received. |
| `PlayerLeftZone` | S→ALL (remaining) | R-OD | P1 | Server sends only to clients still in the zone after departure. Entity must be despawned on receipt. |
| `KillEvent` | S→ALL | R-OD | P1 | Zone-wide social broadcast. Must reference valid EntityIDs. |
| `GhostPromotionEvent` | S→ALL | R-OD | P1 | Zone-wide ghost-state broadcast. Must arrive before any tick-rate update (EntityHealthUpdate, CycleTimerBroadcast) that references this entity's `IsGhost` flag. Emitted once per `Connected → Disconnected_SessionActive` transition (CR-GH-2, networking-ghost-session.md). |
| `GhostExpiredEvent` | S→ALL | R-OD | P1 | Zone-wide ghost-slot release. Carries `GhostExpiredReason` field (`GhostTtlExpired = 0` \| `GhostDismissed = 1` \| `GhostDeath = 2`). Unknown bytes: substitute `GhostTtlExpired = 0` and continue. Emitted by CR-GH-10 step 7 (TTL expiry or voluntary dismissal) and CGS-5 step 4 (ghost death). See networking-ghost-session.md, networking-ghost-character-state.md. |
| `GhostDismissRequest` | C→S | R-OD | P1 | Party-initiated ghost slot release. Server validates: (1) sender is a party member of the ghost entity's party; (2) ghost session is in `Disconnected_SessionActive`. Duplicate requests (transport-layer retransmit) ignored if ghost cleanup already in progress. See CR-GH-12, networking-ghost-session.md. |
| `GhostDismissRejected` *(schema pending)* | S→C | R-OD | P1 | Sent to the requesting party member only when `GhostDismissRequest` validation fails. Must carry a `GhostDismissRejectedReason` field. MCR-2 entry required before schema acceptance. Never sent on successful dismissal — success produces `GhostExpiredEvent` (S→ALL). |
| `GroundItemSpawned` | S→PARTY | R-OD | P1 | Must be emitted before any `GroundItemAssigned`, `GroundItemExpiryWarning`, or `AuctionResolved` referencing this `ItemID`. Sent to all party members in the loot eligibility list. |
| `GroundItemAssigned` | S→PARTY | R-OD | P1 | Must follow `GroundItemSpawned` for the same `ItemID`. Carries the assigned `CharacterID`. |
| `GroundItemDespawned` | S→PARTY | R-OD | P1 | Item beacon removed on receipt. Must follow `GroundItemSpawned` for the same `ItemID`. Sent to the same party members who received `GroundItemSpawned`. |
| `AuctionResolved` | S→PARTY | R-OD | P1 | Final economic outcome: item winner and gold distribution. Must follow `GroundItemSpawned` for the same `ItemID`. All prior `LootBidUpdate` entries for this `ItemID` are superseded. |
| `LootBidRequest` | C→S | R-OD | P1 | Irreversible economic action. Server validates: sender is eligible bidder, auction is open, and bid exceeds current highest bid. |
| `BagFullPickupBlocked` | S→C | R-OD | P1 | Sent to the requesting player only. Emitted only for explicit pickup attempts, not automatic loot pickup failures. At most one per loot item per player per interaction. |
| `GroundItemExpiryWarning` | S→PARTY | R-OD | P1 | Sent to all remaining eligible players for the expiring item. Re-fires to eligible players if TTL is extended (at the new deadline). Always precedes `GroundItemDespawned` when expiry occurs without extension. |
| `PartyStateUpdate` | S→PARTY | R-OD | P1 | Full party state snapshot. Sent to all current party members after any membership change (join, leave, ghost promotion). Must precede any message that references party membership for XP or loot calculations. |
| `PartyDisbanded` | S→PARTY | R-OD | P1 | Sent to all members at time of disbanding. All party-state structures cleared on receipt. Must arrive before any loot distribution event for items in the disbanded party's loot pool. |
| `PartyInviteRequest` | C→S | R-OD | P1 | Irreversible social action. Server validates: target exists in zone, is not in a full party, and inviter has party-creation rights. |
| `PartyInviteReceived` | S→C | R-OD | P1 | Sent to the invited player only. Contains inviter `EntityID`. Expires server-side after `INVITE_TIMEOUT_SECONDS` with no response. |
| `PartyInviteResponse` | C→S | R-OD | P1 | Must correlate to an outstanding `PartyInviteReceived`. Server validates timing. Accept/decline is irreversible. |
| `DiscardRequest` | C→S | R-OD | P1 | Irreversible economic action. Server validates item ownership. Monotonically increasing client-generated `requestId` prevents double-discard on transport-layer retransmit. |
| `DiscardResult` | S→C | R-OD | P1 | Sent to requesting player only. Failure response must include `DiscardFailReason`. Client applies optimistic UI rollback on failure. |
| `MoveRequest` | C→S | R-OD | P1 | Server validates source slot is occupied by the claimed item. Client-generated sequence ID for deduplication. |
| `MoveResult` | S→C | R-OD | P1 | Carries authoritative post-operation state for both affected slots (source and destination). Required for optimistic UI rollback. Failure response includes `MoveFailReason`. |
| `InventoryFullNotification` | S→C | R-OD | P1 | Sent to the affected player only. Rate-limited: at most 1 per 30 seconds per player (per CR-INV-FULL-1). |
| `EquipRequest` | C→S | R-OD | P1 | Irreversible state action. Monotonically increasing `requestId` prevents double-execution on retransmit. Server range-validates `gearSlot` in [0, 6] before array access. |
| `EquipResult` | S→C | R-OD | P1 | Carries authoritative post-operation slot state. On success: server also emits `StatSnapshotEvent` per CR-NET-4. Failure response includes `EquipFailReason` and unchanged `slotItemId`. |
| `AppearanceChangedEvent` | S→ALL | R-OD | P1 | Zone-wide appearance update. R-OD required — Social Gravity pillar depends on delivery guarantee. Carries absolute `equipmentAppearanceFlags` byte. Initial flags delivered via `EntityState` in zone snapshot; this event covers mid-session changes only. |
| `OpenNPCInteraction` | C→S | R-OD | P1 | Irreversible economic session open. Must not be retransmitted after `NPCInteractionOpened` or a rejection is received — client suppresses duplicates after any response arrives. `npcId` must match an NPC present in the town hub zone. |
| `NPCInteractionOpened` | S→C | R-OD | P1 | Session state confirmation. Must precede any `BuyResult` or `SellResult` for this session. Client starts `SESSION_TTL_SECONDS` (300s) wall-clock countdown on receipt and opens the shop window to the Sell tab (CR-SHOP-3). |
| `CloseNPCInteraction` | C→S | R-OD | P1 | Voluntary session close. Fire-and-forget — no server response. Server clears `NPCInteractionActive` on receipt. |
| `BuyRequest` | C→S | R-OD | P1 | Economic action. `requestId` deduplication on `(SenderEntityID, messageType, requestId)` within `SESSION_TTL_SECONDS` — transport-layer retransmit returns cached `BuyResult` without re-executing (ADR-001 A1). Rate-limited: 10 req/sec per character. |
| `BuyResult` | S→C | R-OD | P1 | Success-only response. On any failure, a dedicated rejection message is sent instead. Never sent alongside a rejection for the same `requestId`. Carries `newGoldBalance` — treat as consistent with any concurrent `GoldSyncEvent` (same tick authority). |
| `SellRequest` | C→S | R-OD | P1 | Economic action. Same `requestId` deduplication as `BuyRequest` (ADR-001). Rate-limited per ADR-001. `requestId` namespace is shared with `BuyRequest` within the session. |
| `SellResult` | S→C | R-OD | P1 | Success-only response. `goldEarned` carries actual credited amount (may be less than `SellPriceGold × quantitySold` when GOLD_CAP is reached — see npc-shop.md Edge Cases). |
| `RejectedNotInTownHub` | S→C | R-OD | P1 | Sent to requesting client only. No `requestId` in body — response to `OpenNPCInteraction` which carries no `requestId`. |
| `RejectedNoNPCSession` | S→C | R-OD | P1 | Sent to requesting client only. `requestId` in body correlates to the rejected `BuyRequest` or `SellRequest`. |
| `RejectedInvalidQuantity` | S→C | R-OD | P1 | Sent to requesting client only. Same `requestId` correlation. |
| `RejectedItemNotInCatalog` | S→C | R-OD | P1 | Sent to requesting client only. Same. |
| `RejectedInsufficientFunds` | S→C | R-OD | P1 | Sent to requesting client only. `GoldSyncEvent` is also emitted to resync client balance on the same tick — treat both as consistent. |
| `RejectedInventoryFull` | S→C | R-OD | P1 | Sent to requesting client only. Net gold effect is zero (compensating `AddGold(CompensatingRefund)` already executed server-side before this message is sent). |
| `RejectedRateLimited` | S→C | R-OD | P1 | Sent to requesting client only. Client must not retry until a `BuyResult`, `SellResult`, or different rejection is received for a prior request — no immediate resend. |
| `RejectedInvalidSlot` | S→C | R-OD | P1 | Sell rejection sent to requesting client only. |
| `RejectedItemMismatch` | S→C | R-OD | P1 | Sell rejection. Client must refresh the Sell tab after receipt — slot contents changed since the last render. |
| `RejectedSlotLocked` | S→C | R-OD | P1 | Sell rejection. Slot was locked (by Enhancement System) between Sell tab render and `SellRequest` execution. |
| `RejectedUnsellable` | S→C | R-OD | P1 | Sell rejection. `SellPriceGold = 0` server guard — item bypassed the client-side Sell tab filter. |
| `UseItemRequest` | C→S | R-OD | P1 | Irreversible item-use action. `requestId` idempotency per ADR-001 A1 — transport retransmit returns cached `UseItemResult` without re-executing. Server deduplicates on `(SenderEntityID, messageType, requestId)` within `SESSION_TTL_SECONDS`. Rate-limited: 10 req/sec per character. |
| `UseItemResult` | S→C | R-OD | P1 | Authoritative item-use outcome. Carries `newResourceValue` (FloorToInt, CR-NET-7.2) and `newInventoryQuantity`. Client must not apply HP/MP or inventory update until receipt — no optimistic update for resource value (client only predicts cooldown start). |
| `UseItemRejected` | S→C | R-OD | P1 | Item-use rejection. Must arrive to release client predict state — resets `EffectTypeCooldownRemaining` to 0 and un-greys the hotbar slot. A lost rejection permanently locks the slot in the greyed state. `requestId` correlates to the rejected `UseItemRequest`. |
| `EnhancementAttemptRequest` | C→S | R-OD | P1 | Client-generated `requestId` is monotonically increasing. Server deduplicates by `requestId` to handle transport-layer retransmits. |
| `EnhancementRequestReceived` | S→C | R-OD | P1 | **Cap-exempt.** Placed at front of Path 1 queue before tick flush. |
| Enhancement outcome broadcast *(schema pending)* | S→ALL | R-OD | P1 | **Cap-exempt** (same exemption as EnhancementRequestReceived). Schema in Enhancement System GDD. |
| Level-up event + stat snapshot *(schema pending)* | S→C | R-OD | P1 | Schema in Leveling System GDD. |
| `AllocateFreePointRequest` *(schema pending)* | C→S | R-OD | P1 | Schema in Character Stats GDD. |
| `AllocateFreePointResponse` *(schema pending)* | S→C | R-OD | P1 | Schema in Character Stats GDD. |
| Respec Phase 1/2 *(schema pending)* | C→S / S→C | R-OD | P1 | Schema in Character Stats GDD. |
| Item consumption confirmation *(schema pending)* | S→C | R-OD | P1 | Schema in Inventory System GDD. |
| `SessionReady` | S→C | R-OD | P1 | Zone-entry dual-gate: client must not render or send RPCs until both this and `ZoneStateSnapshot` reassembly complete. |
| `ZoneSessionEnded` | S→C | R-OD | P1 | Client stops sending RPCs on receipt. Session teardown proceeds regardless of in-flight messages. |
| `ZoneStateSnapshotFragment` | S→C | R-OD | P1 (bulk, cap-exempt) | `totalFragments` computed from actual serialized byte count, not worst-case estimate. Bulk-transfer cap exemption applies. |
| `SetTarget` | C→S | R-OD | P1 | Validates per RFR-3a (networking-relevance-filter.md): `EntityID = 0` = deselect; self-target (`targetEntityId == client.ownEntityId`) rejected silently (logs `SelfTargetAttempt` anomaly). Must reach server — updates EntityHealthUpdate relevance set persistently; missed delivery leaves target-slot EHU stale indefinitely. |
| `SessionHandshake` | C→S | R-OD | P1 | Schema pending OQ-NC-SER-2. No server→client message of this name exists (see CCR-2). |
| `ClientBackgrounded` | C→S | R-OD | P1 | Session lifecycle — triggers item TTL extension (CR-LT-13.1). Sent when the app moves to background. Must reach server to prevent item expiry during a backgrounded session. |
| `ClientForegrounded` | C→S | R-OD | P1 | Session lifecycle — ends the TTL extension window opened by `ClientBackgrounded`. Must reach server. Sent when the app returns to foreground. |
| `HeartbeatMessage` | C→S | U-U | Standalone | Client skips sending if any outbound packet was sent in the preceding `HEARTBEAT_INTERVAL_SECONDS`. Any received packet resets the server's timeout counter. |
| `RttProbe` | S→C | U-U | Standalone | Sent once per `RTT_PROBE_INTERVAL_SECONDS`. Server does not retransmit on loss. |
| `RttProbeEcho` | C→S | U-U | Standalone | Client echoes immediately on receipt. Must echo the `RttProbe`'s envelope `SequenceNumber` as a `probeSequence` payload field. Server correlates by `probeSequence`, not by the echo's own envelope `SequenceNumber`. Server discards if `probeSequence` matches no outstanding probe (stale probe). See EC-CCR-3. |

---

### CCR-4 — Channel Invariants

Rules that apply to all messages on a given channel. Violations produce undefined behavior at the receiver.

**R-OD invariants:**
- Every R-OD **server-to-client** message (S→C, S→ALL, S→PARTY, S→RELEVANT) must encode absolute state, never deltas. The receiver may not have received the preceding message of the same type. **C→S command messages are exempt** — they describe actions to perform, not state snapshots (e.g., `LootBidRequest`, `DiscardRequest`, `MoveRequest`).
- Receivers may assume exactly-once, in-order delivery. Duplicate detection is the transport layer's responsibility.
- No R-OD message may be silently dropped by the application layer. If the session is active, the transport retransmits until ACK'd.

**R-U invariants:**
- Receivers must not assume ordering. `DamageEvent` from tick T may arrive after `EntityHealthUpdate` from tick T+1.
- All R-U state sync messages (`GoldSyncEvent`, `EntityHealthUpdate`) must carry a version or tick number. Receivers apply `IsNewerVersion` or `IsTickExpired` comparison — raw `uint` comparison is forbidden.
- R-U overflow drops are permitted only for messages with a self-correction mechanism: the next tick delivers a fresh authoritative value.

**U-U invariants:**
- Every U-U message must encode enough information for the receiver to produce a correct display state from that message alone, without requiring the previous U-U message of the same type. No delta encoding. No sequence-dependent state.
- Senders must not check for U-U delivery acknowledgment. No application-layer retry for U-U messages.
- Receivers must handle missing U-U messages gracefully: interpolate positions, hold last cycle timer value.

## Formulas

### F-CCR-1 — Priority Path Effective Capacity per Client per Tick

```
PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued
```

**Variables:**

| Variable | Definition | Units | Valid Range |
|----------|-----------|-------|-------------|
| `PathCapacity_effective` | Total R-OD messages deliverable per destination client per tick (exempt + non-exempt) | messages/tick | [PRIORITY_PATH_CAP, PRIORITY_PATH_CAP + 9] in normal operation |
| `PRIORITY_PATH_CAP` | Maximum non-exempt R-OD messages per destination client per tick | messages/tick | [4, 16] (see Tuning Knobs) |
| `ExemptMessages_queued` | Count of cap-exempt messages (enhancement-path + bulk-transfer) queued before the current tick's flush for this client | messages | [0, 9] in normal operation |

| Scenario | ExemptMessages_queued | PathCapacity_effective |
|----------|-----------------------|------------------------|
| Steady-state (no enhancement, no zone entry) | 0 | 8 |
| Enhancement tick (EnhancementRequestReceived + outcome) | 2 | 10 |
| Zone entry at n=50 (7 snapshot fragments) | 7 | 15 |

The cap applies per destination client per tick. Zone-entry effective capacity (15) is bounded — once all fragments are sent, the cap returns to PRIORITY_PATH_CAP.

## Edge Cases

### EC-CCR-1 — Zone Transition Floods Path 1 with PlayerJoinedZone

During a server-restart repopulation at n=50, up to 49 `PlayerJoinedZone` messages queue for each reconnecting client. At `PRIORITY_PATH_CAP = 8`, delivery drains over ⌈49/8⌉ = 7 ticks (350ms at 20Hz). This is acceptable — `PlayerJoinedZone` ordering matters (each must precede messages referencing that EntityID) but the 350ms window occurs only at initial population, not during normal play. The cap ensures no single tick is overwhelmed.

---

### EC-CCR-2 — SelfDamageEvent Delivered to Wrong Client

If `SelfDamageEvent` is mistakenly sent to a non-attacker client, the receiver displays a phantom damage number. Mitigation: the client asserts `attackerEntityId == localPlayerEntityId` on receipt. A mismatch logs a `SelfDamageDirectionViolation` anomaly and suppresses the display. Server-side: the recipient set for `SelfDamageEvent` must be constructed as a singleton `{attackerEntityId}` — not derived from the zone-wide broadcast list.

---

### EC-CCR-3 — Stale RttProbeEcho

An `RttProbeEcho` may arrive after the corresponding probe's RTT window has closed (common at high latency). Correlation is done by `probeSequence`, a payload field in `RttProbeEcho` that echoes the `SequenceNumber` from the corresponding `RttProbe`'s envelope. Server behavior: if the echo's `probeSequence` payload field matches an outstanding probe's envelope `SequenceNumber`, compute RTT. If no matching probe exists, discard silently — no anomaly. Do not apply stale-discard to `RttProbeEcho`; the echo's own envelope `SequenceNumber` serves the shared counter (CCR-1), not probe correlation.

---

### EC-CCR-4 — PartyMemberHealthUpdate in Position Packet (Forbidden Pattern)

Before the C1 fix (CCR-3), `PartyMemberHealthUpdate` appeared in the U-U Position packet. Implementations must assert that `PartyMemberHealthUpdate` is only constructed within the R-U batch context. Any future code that writes `PartyMemberHealthUpdate` into a U-U packet violates the fellowship pillar guarantee (Pillar 3) and must fail CI.

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-message-criticality.md` | Parent contract — pillar classifications that determine channel assignments in CCR-3 |
| `networking-wire-protocol.md` | Consumer — CR-NET-7.7 two-path delivery model and all message schemas; must reference this document |
| `networking-session.md` | Consumer — session lifecycle direction rules (CCR-2 disambiguation applies) |
| `networking-relevance-filter.md` | Peer — EntityHealthUpdate S→RELEVANT direction rule (CCR-3 row) |
| `networking-test-harness.md` | Consumer — `INetworkTestObserver` hooks capture at the channel/direction boundaries defined here |
| `consumable-use-system.md` | Downstream — `UseItemRequest`, `UseItemResult`, `UseItemRejected` CCR-3 rows added 2026-06-09 (OQ-CUS-2 resolved). All three are R-OD P1. |

## Tuning Knobs

| Knob | Default | Safe Range | Impact |
|------|---------|------------|--------|
| `PRIORITY_PATH_CAP` | 8 | [4, 16] | Maximum non-exempt messages per destination client per tick on Path 1. Authoritative definition in `networking-wire-protocol.md`; cross-referenced here because it directly governs the capacity calculation in F-CCR-1. Raising above 16 risks head-of-line blocking in Path 1 — enhancement-path messages may wait behind a burst of social events. |

## Acceptance Criteria

**AC-CCR-01 (CI)** — Given the full codebase and all documentation files, When a CI text search is run for `"server SessionHandshake"` and for `"SessionHandshake"` in server-outbound contexts, Then no match is found. All server→client zone-entry references use `SessionReady` and/or `ZoneStateSnapshot`. Any match fails the CI gate.

**AC-CCR-02 (Logic)** — Given a client connection in a controlled test environment where `HeartbeatMessage` and `RttProbeEcho` are the only two messages sent (no other messages interspersed), When the server inspects the `SequenceNumber` envelope fields of both messages, Then `RttProbeEcho.SequenceNumber = HeartbeatMessage.SequenceNumber + 1`. This verifies the shared-counter rule (CCR-1). Fails if either message has an independent per-type counter or if the counter is non-monotonic across message types.

**AC-CCR-03 (Integration)** — Given a zone with 4 party members in active combat, When the server serialises per-tick batches for one party member client, Then `PartyMemberHealthUpdate` sub-messages appear only in the R-U batch deserialiser path and never in any U-U packet deserialiser path. Verified by `INetworkTestObserver` packet-type tagging on delivery context.

**AC-CCR-04 (Integration)** — Given a player who lands a hit on an enemy, When the server emits `SelfDamageEvent` and `DamageEvent` for that hit, Then `SelfDamageEvent` is delivered to exactly one client (the attacker), and `DamageEvent` is delivered to all zone clients except the attacker. The attacker's client receives `SelfDamageEvent` via R-OD and does not receive a `DamageEvent` for the same `(attackerEntityId, targetEntityId, ServerTickNumber)` triple.

**AC-CCR-05 (Integration)** — Given a client with 9 non-exempt R-OD messages queued for the same tick, When the tick flush runs, Then exactly 8 messages are delivered in that tick and the 9th is sent at the start of the next tick's flush. No message is dropped. Verified by `INetworkTestObserver` counting P1 deliveries per tick boundary.

**AC-CCR-06 (Logic)** — Given any R-OD server-to-client message schema (direction S→C, S→ALL, S→PARTY, or S→RELEVANT), When the schema is reviewed, Then no payload field may require the preceding message of the same type to produce a correct receiver state — no delta-only fields, no "offset from prior value" encodings. C→S command messages (e.g., `LootBidRequest`, `DiscardRequest`) are exempt. Pass criterion: design review of every R-OD S→C schema confirms no delta-only fields. Verified at design review and enforced by AC-CCR-09 routing audit.

**AC-CCR-07 (Integration)** — Given a client that receives `GoldSyncEvent` and `EntityHealthUpdate` messages delivered out of emission order (simulated by `ITransportFaultInjector` reordering R-U packets), When the client's R-U deserializer processes these messages, Then (a) `GoldSyncEvent.goldBalance` is accepted or rejected using `IsNewerVersion` (tick-based comparison), not raw `uint` comparison; and (b) `EntityHealthUpdate.currentHP` is accepted or rejected using `IsTickExpired`, not raw sequence comparison. Any raw `uint` comparison path (without tick/version wrapping) fails this AC. Verified by static analysis of the R-U deserializer and by `INetworkTestObserver` observing stale-discard decisions during out-of-order injection.

**AC-CCR-08 (Integration)** — Given a client whose `EntityPositionUpdate` stream is interrupted for 3 consecutive ticks by `ITransportFaultInjector`, When U-U packets resume on tick T+4, Then (a) the client's position renderer produces a correct display state from the single resumed packet without referencing any dropped packet's data (no delta decoding path); and (b) the `EntityPositionUpdate` payload fields are sufficient to compute absolute world position. Verified by `ITransportFaultInjector` dropping packets and `INetworkTestObserver` confirming the resumed packet is self-contained and the renderer produces a valid state.

**AC-CCR-09 (CI)** — Given the set of message rows in MCR-2 and the set of rows in the CCR-3 routing table, When a CI check compares both sets, Then every non-pending message in MCR-2 has exactly one routing entry in CCR-3 specifying direction, channel, and delivery context; and every MCR-2 message marked `*(schema pending)*` appears in CCR-3 as pending or fully specified. Any MCR-2 message with no CCR-3 entry fails the CI gate.
