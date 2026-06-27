# Networking Relevance Filter

> **Status**: Approved (lean re-review, 2026-05-19)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-29 (Death & Respawn amendment: RFR-7 added for EntityDied delivery scope; dependency table updated. Prior: 2026-05-19.)
> **Parent**: networking-wire-protocol.md

## Overview

The Relevance Filter defines which entities a client receives `EntityHealthUpdate` messages for. Without filtering, the server broadcasts 49 HP updates per tick at n=50, saturating the R-U batch and forcing DamageEvent and PartyMemberHealthUpdate to be dropped. With filtering, each client receives HP updates only for entities in its relevance set: its party members and its current target. This reduces HP traffic by approximately 92% at n=50, restoring full R-U batch capacity for combat-critical messages.

## Player Fantasy

None. This document is a pure infrastructure contract. Its correctness is felt as HP bar accuracy for the entities that matter — party frames update in real time, target HP tracks correctly, and DamageEvents are no longer crowded out by zone-wide HP broadcasts.

## Detailed Rules

### RFR-1 — Relevance Set Definitions

Per client, the server maintains two separate sets that govern which health-update messages are sent each tick.

**EntityHealthUpdate set** — governs `EntityHealthUpdate` sub-message delivery:

| Slot | Contents | Max size | Owner |
|------|----------|----------|-------|
| Self slot | The client's own EntityID | 1 (always present) | This spec (RFR-5) |
| Target slot | The entity the client currently has targeted, only if NOT a party member | 0 or 1 | Auto-Attack Combat GDD / Skill System GDD (via `SetTarget` RPC — see RFR-3a) |

Maximum `EntityHealthUpdate` sub-messages per client per tick: **2** (self + one non-party target). If the client has no target, or targets a party member, only the self-slot EntityHealthUpdate is sent.

**PartyMemberHealthUpdate set** — governs `PartyMemberHealthUpdate` sub-message delivery:

| Component | Contents | Max size | Owner |
|-----------|----------|----------|-------|
| Party set | All party members of this client, excluding the client's own EntityID | 3 (4-person party − self) | Party System GDD |

Maximum `PartyMemberHealthUpdate` sub-messages per client per tick: **3** (full 4-person party minus self).

**Separation invariant:** `EntityHealthUpdate` and `PartyMemberHealthUpdate` deliver HP data for disjoint sets of entities. Party members' HP is delivered exclusively via `PartyMemberHealthUpdate`. Non-party entities' HP is delivered exclusively via `EntityHealthUpdate`. The client's own HP is delivered via self-slot `EntityHealthUpdate`. No entity receives HP data from both message types in the same tick — this eliminates redundant `currentHP` delivery when a party member is also the current target. If the current target is a party member, the target-slot EntityHealthUpdate is suppressed; that entity already receives a `PartyMemberHealthUpdate` with equivalent and richer data.

Both sets are **server-side only**. Neither is transmitted to the client. The client already knows its own party members (via `PlayerJoinedZone` + party state messages) and its own current target (local UI state).

---

### RFR-2 — Server-Side Filter Algorithm

At each tick, during R-U batch construction for a given client, the server writes health-update sub-messages directly from the two sets defined in RFR-1:

```
// Per-client, per-tick, during R-U batch flush

// EntityHealthUpdate: self-slot always; target-slot only if non-party (RFR-1 separation invariant)
batch.Append(EntityHealthUpdate(client.ownEntityId))              // RFR-5: self always included
if client.targetEntityId != null                                   // has a target
   AND !client.partySet.Contains(client.targetEntityId):          // target is not a party member
    batch.Append(EntityHealthUpdate(client.targetEntityId))

// PartyMemberHealthUpdate: one per party member excluding self (O(|partySet|) ≤ O(3))
foreach EntityID memberId in client.partySet:
    batch.Append(PartyMemberHealthUpdate(memberId))
```

The filter runs before the R-U overflow check.

**Complexity:** Per-client cost is **O(1 + |partySet|) ≤ O(4)** — constant in practice (|partySet| ≤ 3). Per-tick total cost is **O(n × MAX_PARTY_SIZE)** — linear in player count. This replaces the prior O(n) zone-entity iteration per client (which was O(n²) total per tick). No HashSet.Contains() scan over all zone entities is needed; both sets are bounded and directly known.

---

### RFR-3 — Relevance Set Update Protocol

The relevance set is updated synchronously within the server tick in response to two event types:

**Party membership change:** When a player joins or leaves a party, the Party System notifies the networking layer, which updates the party-set component for all affected clients within the same tick. The new set takes effect for that tick's batch serialization if the change occurs before the flush point; otherwise it takes effect the following tick.

**Target change:** When a client sends a `SetTarget` RPC (see RFR-3a), the server updates the target-slot component for that client within the same tick. The previous target is removed from the target slot, the new target is added, and the EntityHealthUpdate set is updated atomically before the batch flush. A `SetTarget` RPC that arrives after the batch flush applies to the following tick.

**Concurrent mutation order:** When both a party membership change and a `SetTarget` RPC arrive in the same tick, party set updates are applied first, then target slot updates. The combined relevance state is always consistent at the batch flush point. If the new `SetTarget` target is the newly joined party member, the separation invariant (RFR-1) suppresses the EntityHealthUpdate for that entity — it will receive a `PartyMemberHealthUpdate` instead.

---

### RFR-3a — SetTarget RPC (inline contract; pending external doc registration)

Target selection is triggered by the client sending a `SetTarget` RPC to the server:

```
SetTarget {
    EntityID targetEntityId;  // 4 bytes. EntityID = 0 means "deselect current target".
}
```

Direction: C→S. Criticality: R-OD (P1) — target changes affect the server-side EntityHealthUpdate set persistently. A dropped `SetTarget` leaves the target-slot EntityHealthUpdate stale indefinitely; R-U channel drops are unacceptable for a persistent state change.

Server validation: `targetEntityId` must be a valid EntityID present in the current zone, or 0 (deselect). `targetEntityId == client.ownEntityId` is rejected silently (see RFR-5). Invalid EntityIDs are discarded silently; the server logs an `InvalidTargetEntityId` advisory anomaly.

> **Follow-up required (OQ-RFR-2):** This schema must be registered in `networking-message-criticality.md` MCR-2, added to `networking-channel-contract.md` CCR-3 (direction C→S, channel R-OD, priority P1), and added to `networking-wire-protocol.md` message schema table before implementation begins.

---

### RFR-4 — Target Slot Exclusivity

A client may hold at most one entity in the target slot at a time. A new target-selection RPC replaces the previous target atomically — no transition window where both are in the set. The target slot may be empty (no target selected).

---

### RFR-5 — Own-Character HP Delivery

The client's own HP is delivered via two paths:

1. **Initial state:** Authoritative HP value included in `SessionReady` (R-OD) on zone entry.
2. **Live combat updates:** `EntityHealthUpdate` for the client's own EntityID is sent in the R-U batch each tick via the self-slot (RFR-1). This ensures the player's HP bar updates in real time during combat — damage received, healing, and DoT effects are all reflected.

The self-slot is always active regardless of party membership or target state. The client's own EntityID must never appear in the **party set** or the **target-slot** EntityHealthUpdate set — it is exclusively handled by the self-slot.

If a client's UI sends a `SetTarget` RPC with their own EntityID as the target, the server rejects the target change (logs `SelfTargetAttempt` advisory anomaly), leaves the target slot unchanged, and does not add the self EntityID to the target-slot set.

---

### RFR-6 — Mob and NPC Targeting

The relevance filter applies to all zone entity types. If the client targets a mob or NPC, that entity's `EntityHealthUpdate` is included in the relevance set. This is intentional — the client needs HP feedback for the entity it is attacking. Mob/NPC targeting slots follow the same exclusivity rule as player targeting (RFR-4).

---

### RFR-7 — EntityDied Delivery Scope

`EntityDied` is an entity-state change notification, not a health-update sub-message. It does not participate in the `EntityHealthUpdate` or `PartyMemberHealthUpdate` relevance sets defined in RFR-1.

**Delivery model:** `EntityDied` is an **R-OD zone-wide broadcast** — delivered to all zone clients regardless of party membership, targeting state, or relevance set membership. Zone-wide delivery is required because all clients need to:
- Remove the nameplate from the dead entity's overhead display
- Apply the greyscale death material override (visible to any zone client in the zone)
- Update their party panel if the dead entity is a party member

**Ghost-death exclusion:** `EntityDied` is **not** sent for ghost deaths (`EntityState.isGhost == true`). See CR-DR-Ghost in `death-and-respawn.md` and Event 5 in the Visual/Audio section. Ghost deaths are session terminations — the entity despawns as a standard zone departure via `PlayerLeftZone`, not as a death event.

**Bandwidth impact:** `EntityDied` is event-driven and infrequent. At n=50, worst case (all 50 players die simultaneously): 50 × 14 bytes × 50 clients = 35,000 bytes zone-wide in that tick. This is a pathological scenario; during normal play `EntityDied` contributes < 1% of outbound bandwidth per zone.

**MVP scope note:** Proximity-based filtering for `EntityDied` is out of scope for MVP. At `MAX_PLAYERS_PER_ZONE = 50`, zone-wide delivery is correct and affordable.

## Formulas

### F-RFR-1 — HP Update Message Count per Client per Tick

**EntityHealthUpdate count** (self-slot + target-slot, per RFR-1):
```
EntityHealthUpdates_per_client = 1 (self-slot)
                                + (1 if targetEntityId ≠ null
                                      AND targetEntityId ∉ partySet, else 0)
```
Maximum: **2** per client per tick.

**PartyMemberHealthUpdate count** (party set, per RFR-1):
```
PartyMemberHealthUpdates_per_client = |partySet|
```
Maximum: **3** per client per tick (4-person party minus self).

| Play state | EHU (EntityHealthUpdates) | PMHU (PartyMemberHealthUpdates) | Total HP-msg bytes |
|------------|--------------------------|----------------------------------|-------------------|
| Solo, no target | 1 (self) | 0 | **16** |
| Solo, targeting 1 entity | 2 (self + target) | 0 | **32** |
| Full party (3 others), targeting non-party entity | 2 (self + target) | 3 | **32 + 72 = 104** |
| Full party (3 others), targeting a party member | 1 (self only — target-slot suppressed) | 3 | **16 + 72 = 88** |
| Full party (3 others), no target | 1 (self) | 3 | **16 + 72 = 88** |

**At n=50, previous zone-wide EntityHealthUpdate broadcast:** 49 EHUs/tick per client (784 bytes EHU alone).
**At n=50, relevance-filtered (worst case — full party + non-party target):** 2 EHU + 3 PMHU = 104 bytes. **~87% HP-message reduction.**

---

### F-RFR-2 — R-U Batch Re-Validation at n=50 with Filter

Post-filter R-U batch at n=50 Scenario C (10 DamageEvents/tick, full party + non-party target — worst case):

```
R-U batch = header(12)
           + 10 DamageEvents           (10 × 18 = 180 bytes)
           + 2 EntityHealthUpdates     (2 × 16  =  32 bytes)   [self + non-party target]
           + 3 PartyMemberHealthUpdates(3 × 24  =  72 bytes)   [24 bytes post OQ-PS-6 expansion]
           + 1 GoldSyncEvent           (          17 bytes)
           + 1 ConnectionQualityUpdate (           5 bytes, worst case)
           = 12 + 180 + 32 + 72 + 17 + 5 = 318 bytes
```

**318 bytes — no overflow.** All messages delivered at n=50 Scenario C with the relevance filter active. This compares to 512 bytes (at cap, ~29 HP updates dropped) without the filter.

> **OQ-RFR-3 RESOLVED (2026-05-19):** `networking-wire-protocol.md` Scenario C updated: 2 EntityHealthUpdates (32 bytes), total 318 bytes. All Scenarios A/B/C and F-NET-2 figures updated to reflect separation invariant.

## Edge Cases

### EC-RFR-1 — Target Entity Leaves Zone

If the targeted entity leaves the zone within the same tick as an `EntityHealthUpdate`, the server removes the departed EntityID from all relevance sets before the batch flush. A `PlayerLeftZone` message (R-OD) informs all remaining clients of the departure. If the batch flush has already serialized for that tick, one stale `EntityHealthUpdate` may have been appended for the departed entity — this is acceptable; the client will despawn the entity on receipt of `PlayerLeftZone` and ignore subsequent HP updates for the despawned EntityID.

---

### EC-RFR-2 — Dead Party Member Stays in PartyMemberHealthUpdate Set

A party member with HP = 0 (dead) remains in the **PartyMemberHealthUpdate set** until they leave the zone or the party is dissolved. The client continues receiving their `PartyMemberHealthUpdate` sub-messages. This is intentional — dead party members can be revived, and the client needs to track the HP state transition from 0 to a respawn value. The Party System GDD governs when dead members are removed from the party.

Dead party members are not in the EntityHealthUpdate set (party members are excluded from the target-slot per RFR-1 separation invariant). The EntityHealthUpdate self-slot and target-slot are unaffected by party member death state.

---

### EC-RFR-3 — Solo Player, No Target

A solo player with no current target has an empty party set and empty target slot. The server sends exactly 1 `EntityHealthUpdate` per tick for that client (self-slot only) and zero `PartyMemberHealthUpdate` sub-messages. The R-U batch contains the self-slot EHU plus `DamageEvent`, `GoldSyncEvent`, and `ConnectionQualityUpdate` sub-messages when applicable. The player's own HP bar always reflects the server-authoritative HP value.

---

### EC-RFR-4 — Client Sends Self-Target RPC

If a client sends a `SetTarget` RPC with their own EntityID as the target, the server rejects the target-change silently (no client-visible error), leaves the target slot unchanged, and logs a `SelfTargetAttempt` advisory anomaly. The client's own EntityID may only appear in the self-slot (RFR-5) — never as a target-slot entry.

---

### EC-RFR-5 — Target Is a Party Member (Separation Invariant Applied)

When a client targets one of their own party members, the RFR-1 separation invariant applies: the target-slot `EntityHealthUpdate` for that party member is suppressed. That entity already receives a `PartyMemberHealthUpdate` each tick, which carries equivalent (and richer) HP data. The client's R-U batch contains: 1 self-slot EHU + 3 PMHU (the target-party-member's HP is delivered via their PMHU). No HP data is double-counted for the targeted party member.

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-wire-protocol.md` | Consumer — `EntityHealthUpdate` and `PartyMemberHealthUpdate` schemas; R-U batch serialization rules. `EntityDied` delivery scope defined in RFR-7 (zone-wide R-OD, not subject to EntityHealthUpdate set filtering). OQ-RFR-3 resolved 2026-05-19 — Scenario C updated to 2 EHU (32 bytes). |
| `networking-channel-contract.md` | Consumer — `EntityHealthUpdate` direction is S→RELEVANT per CCR-3. `SetTarget` row added to CCR-3 (OQ-RFR-2 resolved 2026-05-19). |
| `networking-message-criticality.md` | Consumer — `EntityHealthUpdate` Pillar 2 classification; R-U channel assignment. `SetTarget` row added to MCR-2 (OQ-RFR-2 resolved 2026-05-19). |
| `networking-test-harness.md` | Consumer — `INetworkTestObserver` captures delivery for verification. `OnRUBatchEntityHealthUpdates(uint clientId, IReadOnlyList<uint> deliveredEntityIds)` hook added to `INetworkTestObserver` (OQ-RFR-4 resolved 2026-05-19). |
| `party-system.md` | Producer — provides party membership list; triggers PartyMemberHealthUpdate set updates on join/leave |
| Auto-Attack Combat GDD | Producer — `SetTarget` RPC schema defined in RFR-3a of this doc; Auto-Attack Combat GDD must reference this document for target-change networking contract. |
| `death-and-respawn.md` | Producer — `EntityDied` delivery scope governed by RFR-7 (zone-wide R-OD). Dead party members remain in the `PartyMemberHealthUpdate` set per EC-RFR-2 (HP = 0 state tracked until respawn or zone exit). |

**Bidirectional note:** Auto-Attack Combat GDD must reference this document when specifying the target-selection RPC, since target changes trigger EntityHealthUpdate set updates.

## Tuning Knobs

| Knob | Default | Safe Range | Impact |
|------|---------|------------|--------|
| `MAX_EHU_PER_CLIENT` | 2 | Fixed = 2 (self + 1 non-party target) | Maximum `EntityHealthUpdate` sub-messages per client per tick. Fixed at 2 — not affected by `MAX_PARTY_SIZE`. Increasing this requires adding more target slots (multi-target design change) or expanding self-slot semantics. |
| `MAX_PMHU_PER_CLIENT` | 3 | `MAX_PARTY_SIZE − 1` | Maximum `PartyMemberHealthUpdate` sub-messages per client per tick. Derived from Party System GDD `MAX_PARTY_SIZE`. **Cascade:** when `MAX_PARTY_SIZE` changes, re-derive `MAX_PMHU_PER_CLIENT`, update F-RFR-1, F-RFR-2, and notify `networking-wire-protocol.md` maintainer of batch size implications. |

## Acceptance Criteria

**AC-RFR-01 (Integration)** — Given a zone with 50 players where client A is solo with no current target, When the server serialises 50 ticks of R-U batches for client A, Then each of client A's R-U batches contains exactly **1** `EntityHealthUpdate` sub-message (for client A's own EntityID — the self-slot) and zero `PartyMemberHealthUpdate` sub-messages. Verified via `INetworkTestObserver.OnRUBatchEntityHealthUpdates(clientId, deliveredEntityIds)` — `deliveredEntityIds` must contain exactly `[client A's EntityID]`.

**AC-RFR-02 (Integration)** — Given a zone where client A is in a 4-person party (members B, C, D) and has no target, When the server processes 1 tick, Then client A's R-U batch contains exactly 3 `PartyMemberHealthUpdate` sub-messages (one for each of B, C, D) and exactly 1 `EntityHealthUpdate` sub-message (self-slot, client A's EntityID only) — no EntityHealthUpdate for B, C, D, or any other zone entity. Verified via `INetworkTestObserver`.

**AC-RFR-03 (Integration)** — Given client A targeting entity E (non-party), When client A sends a `SetTarget` RPC (`targetEntityId = F`) on tick T and it arrives before the batch flush, Then on tick T client A's R-U batch contains an `EntityHealthUpdate` for F (and self) but not for E (assuming E is not a party member). When the `SetTarget` arrives after the batch flush for tick T, Then tick T's batch still contains E's EntityHealthUpdate and tick T+1's batch contains F's EntityHealthUpdate. Verifies atomic target-slot replacement and before-flush / after-flush timing (RFR-3). Verified via `INetworkTestObserver.OnRUBatchEntityHealthUpdates` — inspect `deliveredEntityIds` for tick T and tick T+1 batches.

**AC-RFR-04 (Integration)** — Given client A in a 4-person party targeting a non-party entity, When the server serialises the R-U batch at n=50 Scenario C density (10 DamageEvents/tick), Then the total R-U batch size is ≤ 400 bytes (below the 512-byte cap), and no `EntityHealthUpdate`, `PartyMemberHealthUpdate`, or `DamageEvent` sub-messages are dropped.

**AC-RFR-05 (Logic)** — Given client A sending a `SetTarget` RPC with `targetEntityId = client A's own EntityID`, When the server processes the RPC, Then the target slot is unchanged, a `SelfTargetAttempt` anomaly is logged, and in subsequent R-U batches client A's EntityHealthUpdate appears exactly once (via self-slot) — it does not appear a second time as a target-slot entry.

**AC-RFR-06 (Logic)** — Given client A in a 4-person party (members B, C, D) targeting non-party entity E, When client A sends a `SetTarget` RPC with `targetEntityId = B` (a party member), Then from the next batch flush: `EntityHealthUpdate` for B is suppressed (separation invariant — B receives `PartyMemberHealthUpdate`); `EntityHealthUpdate` for E is removed (prior target replaced). Client A's batch contains 1 EHU (self only) + 3 PMHU (B, C, D). Unit-testable via mock zone session manager.

**AC-RFR-07 (Integration)** — Given client A with HP = 750 at zone entry, When the server processes 10 ticks of active combat during which client A takes damage (HP decrements are applied by the Damage Calculation system), Then each of client A's R-U batches contains a self-slot `EntityHealthUpdate` reflecting the current authoritative HP value, and the most recently delivered self-slot EHU matches the server's `EntityHP[clientA]`. Verified via `INetworkTestObserver.OnRUBatchEntityHealthUpdates` — self-slot EHU present in every tick regardless of whether client A has a target or party.

---

## Open Questions

**OQ-RFR-1 — RTT probe spoofing impact on SetTarget delivery:** SetTarget is classified R-OD (P1). If a malicious client deliberately delays sending SetTarget (e.g., to hold a stale relevance set that includes a high-value target's HP), the server cannot detect this directly — it simply processes SetTarget when it arrives. The only attack surface: a client that never sends SetTarget receives no target-slot EHU, which harms only themselves. No security concern. Non-blocking.

**OQ-RFR-2 — RESOLVED (2026-05-19):** `SetTarget` registered in all three approved docs: MCR-2 row (Infrastructure/R-OD, direction C→S) added to networking-message-criticality.md; CCR-3 row (C→S/R-OD/P1) added to networking-channel-contract.md; schema entry (`SetTarget { EntityID targetEntityId; }`, 18 bytes standalone) added to networking-wire-protocol.md.

**OQ-RFR-3 — RESOLVED (2026-05-19):** networking-wire-protocol.md updated — Scenario C corrected to 2 EHU (32 bytes) + 3 PMHU (72 bytes) = 318 bytes. All scenarios (A, B, C), peak-combat note, and F-NET-2 bandwidth/sub-message tables updated for separation invariant. O(n²) note corrected: EHU serialization is now O(n × MAX_PARTY_SIZE); position/CTB enumeration remains O(n²).

**OQ-RFR-4 — RESOLVED (2026-05-19):** `OnRUBatchEntityHealthUpdates(uint clientId, IReadOnlyList<uint> deliveredEntityIds)` added to `INetworkTestObserver` in `networking-test-harness.md`. Hook fires after the R-U batch is fully serialized for a client each tick; `deliveredEntityIds` lists all EntityIDs that received `EntityHealthUpdate` sub-messages in that batch.
