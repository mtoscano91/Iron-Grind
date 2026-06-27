---
name: Networking Message Criticality Contract adversarial review Pass 1
description: 4 BLOCKING items — anomaly threshold fires after 0.4s of sustained AoE, forced gold counter reset semantics undefined, anomaly log diagnostic value near zero, PRIORITY_PATH_CAP deferral interaction unspecified
metadata:
  type: project
---

Adversarial performance review of networking-message-criticality.md, 2026-05-17.

**Key fact:** Scenario C R-U batch = 350 bytes (no overflow at standard combat density). GoldSyncEvent ONLY drops when DamageEvents exceed 20/tick (AoE-heavy bursts). Forced delivery mechanism is irrelevant at steady-state Scenario C.

**4 BLOCKING items:**

1. **B-MCR-1 — Anomaly threshold fires after 0.4s of sustained AoE (not a 30s monitor)**
At GOLD_MAX_CONSECUTIVE_DROP=3, the 3rd forced delivery occurs at t=400ms of sustained overflow. The anomaly threshold ("more than 2 in 30s") fires within the same second that overflow begins. During peak AoE combat (>20 DamageEvents/tick, which triggers GoldSyncEvent drops), the anomaly fires constantly, creating log spam that will mask real anomalies. The threshold is calibrated for "rare event" monitoring but fires during normal high-engagement gameplay.

2. **B-MCR-2 — Forced delivery counter reset semantics undefined**
MCR-4 does not specify whether the consecutive-drop counter resets on (a) emission of the forced R-OD delivery, or (b) successful inclusion of GoldSyncEvent in an R-U batch. If it resets on emission, the counter starts fresh after each forced delivery even if the R-U batch is still overflowing — the next forced delivery fires after 3 more drops (600ms later). If it resets only on successful R-U delivery, forced deliveries fire every 4 ticks (200ms) indefinitely during sustained overflow. These two behaviors differ by 3× in forced-delivery rate. This is an implementation-correctness gap.

3. **B-MCR-3 — PRIORITY_PATH_CAP deferral with forced gold delivery is unspecified**
The forced GoldSyncEvent is not exempt from PRIORITY_PATH_CAP=8. AC-MCR-01 only tests the T+3 tick delivery scenario. It does not specify behavior when T+3's Path 1 queue already holds 8 non-exempt messages (e.g., mass zone event + SelfDamageEvent + KillEvent burst). In that case, the forced delivery is deferred to T+4. Does the drop counter continue incrementing, increment by 2 (the deferral tick), or hold? Does an additional anomaly fire? None of this is defined. Practically: deferral can extend gold display lag beyond the stated 200ms (at GOLD_MAX_CONSECUTIVE_DROP=3).

4. **B-MCR-4 — Anomaly log's "consecutive drop count" field carries no diagnostic signal**
MCR-4 specifies logging "consecutive drop count" in the GoldSyncForcedDelivery critical anomaly. At GOLD_MAX_CONSECUTIVE_DROP=3 (default), every single anomaly log entry will record "consecutive_drop_count = 3" — because the counter always reaches exactly 3 before the forced delivery fires and (presumably) resets. The field is invariant under the default tuning and adds no diagnostic value. The useful diagnostic is the sustained forced-delivery rate (how many forced deliveries in the last N ticks), not the drop count at trigger.

**2 REVISION items:**

5. **R-MCR-1 — Forced GoldSyncEvent MessageTypeID is unspecified within 0xE000-0xEFFF**
MCR-4 says the forced delivery uses a MessageTypeID "within the 0xE000-0xEFFF priority range" but does not assign a specific value or specify whether it uses the same TypeID as the batch GoldSyncEvent. The receiver cannot distinguish a forced priority-path GoldSyncEvent from a normal one at the wire level unless the TypeID differs. This is an ADR-level gap, but it means the forced-delivery path has no unique identifier for debugging or metrics.

6. **R-MCR-2 — F-MCR-1 pathological case (250 deliveries/s, 12.5/tick) is non-actionable**
The formula correctly identifies 250 forced deliveries/second zone-wide as the worst case, then immediately says it is "only reachable in a sustained AoE scenario." No threshold or circuit breaker is defined for the zone-wide case. At 12.5 forced gold messages per tick distributed across 50 clients, each client still gets at most 1 — no single queue overflows. But 250 additional R-OD messages per second zone-wide is measurable server load. The document should state the server-side CPU impact of this worst case (50 additional Path 1 serializations per tick).

**What is NOT a problem (verified):**
- Forced deliveries do NOT pile up per client — max 1 per tick per client.
- At Scenario C steady state (350 bytes), GoldSyncEvent is not dropped. Forced delivery is inactive at standard combat density.
- The 1-per-client forced delivery fits within PRIORITY_PATH_CAP=8 at normal R-OD traffic levels. Crowding only occurs if 7+ other R-OD messages are simultaneously queued for the same client (unusual but possible during mass zone events).

**How to apply:** When reviewing any AoE-heavy combat scenario (>20 DamageEvents/tick per client), flag that the GoldSyncForcedDelivery anomaly will fire constantly and must be suppressed or re-thresholded. The counter reset semantics gap must be resolved before implementation — it determines whether the server sends 1 or 3 forced deliveries per second during sustained AoE overflow.
