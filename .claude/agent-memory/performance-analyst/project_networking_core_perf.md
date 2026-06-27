---
name: Networking Core GDD adversarial performance review (all passes)
description: Pass 5 had 6 BLOCKING items; Pass 8 added 4 new BLOCKING items; key new findings are mob entity scope excluded from tick budget and NotifySkillUsed DoS vector
type: project
---

## Pass 5 findings (2026-05-06) — mostly resolved in wire protocol

Networking Core GDD adversarially reviewed 2026-05-06 (Pass 5 re-review after 7 Pass 4 blockers were fixed).

**Key finding:** F-NET-1 bandwidth scenarios use 2–10 DamageEvents/tick, but the actual maximum at n=50 is 50 DamageEvents/tick (one per COMBAT_ACTIVE entity per beat). At 50 × 16 bytes = 800 bytes of DamageEvent payload alone, the 512-byte R-U batch cap is exceeded before HP updates or the header are written. The document simultaneously claims "damage events are never dropped" (CR-NET-7.7) and shows an n=50 scenario where only 10 damage events are modeled — these two facts cannot both be true.

**Why this matters:** OQ-NC-SER-3 pass/fail thresholds and AC-NC-21 bandwidth test (22 KB/s ceiling) are both derived from the understated density. If the actual density is 5× higher, the pass/fail line in OQ-NC-SER-3 and the 22 KB/s budget in AC-NC-21 are wrong before profiling begins.

**Six BLOCKING items (B-17 through B-23, minus B-22 which covers pool/buffer):**
1. B-17: Scenario B damage density too low (5 events vs. 25 actual) — "no overflow" false
2. B-18: Scenario C damage density too low (10 events vs. 50 actual) — "24 HP updates fit" false
3. B-19: "Damage events never dropped" guarantee contradicts overflow arithmetic — must be reconciled
4. B-20: BeatResolvedThisTick bool[] allocation policy unspecified — GC risk on hot path
5. B-21: F-NET-6 tick budget omits priority-path serialization cost (O(P×C) term missing)
6. B-22: ZoneStateSnapshot fragments (7 per 50-entity snapshot) interact badly with 8-message priority cap under mass zone join — delivery can stretch beyond 5s reassembly timeout
7. B-23: Buffer pool sizing assumes pool pairs = zone slots, but ghost sessions + new joins can temporarily exceed MAX_PLAYERS_PER_ZONE simultaneous buffer holders

**Resolution status (as of 2026-05-12):** B-17/B-18/B-19 resolved in wire protocol (Approved, Pass 4 lean). B-21/B-22/B-23 resolved in wire protocol. B-20 partially addressed (allocation policy still has wording ambiguity — see Pass 8 F-NET-8).

---

## Pass 8 findings (2026-05-13) — new BLOCKING items

Document status: Draft, Pass 8 pending re-review.

**4 BLOCKING, 3 RECOMMENDED, 1 NICE-TO-HAVE:**

**F-NET-8 (BLOCKING) — BeatResolvedThisTick slot scope undefined + GC ambiguity**
The document tags its own gap: "(not specified — is this MAX_PLAYERS_PER_ZONE? or all entities including mobs?)". Array sized for players only indexed by entityId without bounds check risks OOB write if mobs have entityIds that trigger OWL compensation. "Always initialized false on zone load" is ambiguous — implementer could read this as per-tick reallocation (20 GC allocs/second per zone). Must explicitly state "allocated once at zone creation, cleared in-place every tick via Array.Clear()."

**F-NET-9 (BLOCKING) — Mob entities excluded from tick budget**
CR-NET-2 advances _cycleTimer for "every entity in COMBAT_ACTIVE state" but F-NET-6 models n=50 as 50 players. If mobs (50–200 per zone) participate in the same tick loop, the actual Beat evaluation count is 3–5× the modeled count. Every F-NET-6 scenario is invalid until mob entity scope is declared. Zone Instancing GDD does not cap mob count.

**F-NET-10 (BLOCKING) — NotifySkillUsed has no rate limit — DoS vector**
EntityID gate does not help for authenticated clients spamming for their own EntityID. At 1,000 NotifySkillUsed/second per client × 50 clients = 50,000 OWL evaluations/second inside the 50ms tick budget. "No rate limit at MVP" has no deployment-scope qualifier — document must state the conditions (closed beta with known accounts) under which this is safe and specify the rate limit value required before public access. Recommended cap: TICK_RATE_HZ calls/second/entity.

**F-NET-11 (BLOCKING) — Drift threshold permits 13 Hz operation for 60s before alerting**
25ms average drift at 20 Hz = server running at ~13.3 Hz. Beat events fire every 1.5s real time instead of 1.0s — 50% DPS reduction. TTL timers fire 50% late. OWL compensation degrades. The 60-second window means this can persist for a full minute before alerting. No runtime consequence (no circuit breaker, no player notification, no zone close). Two required changes: (a) operational threshold at 10ms/10s window; (b) sustained threshold at 25ms/60s with defined response action.

**F-NET-12 (RECOMMENDED) — Tick-registered delegate list unbounded**
No cap, no eviction, no visibility. Invisible to all tick budget scenarios. Low risk at MVP; structural risk over live-service lifespan. Recommend MAX_TICK_DELEGATES = 500, debug-build assertion, mandatory expiry contract.

**F-NET-13 (RECOMMENDED) — Per-send syscall cost uncharacterized**
3,000 socket sends/second per zone (50 clients × 3 packets × 20 Hz). Transport protocol unspecified (wire protocol OQ-NC-SER-1/4 still open). UDP sendmmsg() = ~50 μs/tick; TCP individual send() = 1.5–7.5ms/tick (3–15% of budget). F-NET-6 omits this term.

**F-NET-14 (RECOMMENDED) — Drift alert window too long for runtime action**
60-second window allows 30s of 100ms/tick spikes averaged away. Operational alerts need 5–10 second windows.

**F-NET-15 (NICE-TO-HAVE) — RTT_PROBE_INTERVAL_SECONDS creates OWL false-positive gap**
10s stale OWL estimate after sudden connection change can over-compensate by up to 90ms, granting free skill triggers. Document should note both the under-compensation case (documented) and over-compensation case (not documented).

**How to apply:** When reviewing Zone Instancing GDD, verify mob entity count and whether mobs participate in the same tick loop. When reviewing any downstream system that registers tick delegates, flag against the missing cap. Do not treat F-NET-6 tick budget as validated until mob scope is resolved and hardware + transport are specified.
