---
name: Networking Wire Protocol adversarial performance review
description: 7 BLOCKING and 4 REVISION items across two passes; new findings include n=100 analysis gap, GoldSyncEvent sustained-load stale, deterministic entity freeze, and transport protocol unspecified
type: project
---

Wire protocol document for Project Iron Grind adversarially reviewed across two passes (2026-05-11).

**Pass 1 key findings (5 BLOCKING):**

1. F-NET-6 sub-message count of ~6,400 at n=50 is computed post-drop, not pre-drop. Pre-drop standard-combat worst case is ~7,550-7,900; peak-combat (40 DamageEvents/tick) is ~9,350. All tick budget claims derived from it are invalid before profiling.

2. The 30ms tick budget has no hardware specification, no runtime specification (Mono vs. CoreCLR vs. IL2CPP server), and no prototype measurement. No pass/fail threshold exists — profiling without a threshold is not a constraint.

3. The drop policy caps packet size, not serialization work. The server enumerates all n-1 entities per client (O(n²) total) before dropping. Combat floor is 18 bytes (DamageEvent) giving 28 messages per 512B packet, not 102 (which uses the 5-byte ConnectionQualityUpdate floor — never the combat-path floor).

4. PA-P7-05 (zero position updates at n=50) is a gameplay-correctness failure — all 49 entities freeze at last-known position during live combat. Mitigation 2 (separate CycleTimerBroadcast packet) is the only solution that does not sacrifice other message types. Must be BLOCKING.

5. F-NET-6 profiling requirement omits peak-combat tier (n=50, 40 DamageEvents/tick). The 30ms budget only needs to hold at 10 events/tick — peak-combat is 4× higher work.

**Pass 2 new findings (2 additional BLOCKING, 4 REVISION):**

6. [BLOCKING] n=100 within stated safe range has no scenario analysis. CycleBroadcast at n=100 = 1,002 bytes, requiring MAX_MESSAGE_BODY_BYTES ≥ 1,014. Buffer pool grows 4× (300 KB/zone vs. 76.7 KB/zone); at 100 zones, buffer pools alone = ~30 MB. R-U drop policy changes are unanalyzed. The safe range claims n=100 is supported but no Scenario D exists.

7. [BLOCKING] GoldSyncEvent "self-corrects on next mutation" guarantee fails under sustained peak load. At n=50 sustained combat, R-U batch overflows every tick and GoldSyncEvent is dropped every tick. The next mutation's GoldSyncEvent is ALSO dropped. Gold balance becomes permanently stale for the duration of any sustained peak period. This is a correctness failure with direct monetization impact (players see stale balances during high-engagement moments).

8. [REVISION] Position packet overflow drops entities by deterministic sort order (by entityID). The same 14 highest-entityID entities receive zero position updates every tick — they freeze or teleport predictably. This is worse than random: a consistent cohort of players is penalized, and the frozen-entity pattern is potentially exploitable. Pass 1 identified this as PA-P7-05; Pass 2 adds that the deterministic ordering makes it a systematic bias, not random noise.

9. [REVISION] Transport protocol (TCP vs. UDP) is unspecified in the document. send()-path cost is the dark term in the tick budget: UDP sendmmsg() batches all 150 packets in ~50 μs; TCP with Nagle or per-packet send() could add 0.75-7.5ms. The dominant tick-budget term cannot be identified without knowing the transport. Budget analysis is incomplete.

10. [REVISION] ArrayPool<T>.Shared prohibition is correct given unresolved B-23 (ghost session overflow), but should be conditional: "FORBIDDEN until B-23 ghost-session overflow is resolved." Post-B-23 fix, ArrayPool with pool size = MAX_PLAYERS_PER_ZONE × 3 + burst headroom is viable and saves ~7.86 MB of idle buffer memory across 100 empty zones.

11. [REVISION] F-NET-2 "50-zone ceiling: ~590 Mbit/s" uses "ceiling" imprecisely — this is an estimated outbound load at full utilization, not a hard limit. 590 Mbit/s requires a 1 Gbps NIC (62% utilization) or better. Multi-zone server deployment is not analyzed. Egress cost at this rate (~$567/day on AWS at standard pricing) is absent from the document.

**Cross-document links:**
- F-NET-1 damage density understatement (BLOCKING in Networking Core GDD Pass 5, items B-17/B-18) has propagated into F-NET-6 tick budget. Both documents must be reconciled.
- B-23 ghost session buffer pool overflow (Networking Core GDD) blocks ArrayPool prohibition relaxation.

**How to apply:** When reviewing Zone Instancing GDD, Movement GDD, or any system depending on server tick headroom or bandwidth ceilings:
- Use 9,350 sub-messages/tick (peak-combat, n=50) as the stress scenario.
- Treat the 30ms budget as UNVERIFIED until hardware, runtime, and transport are specified and measured.
- Do not use n=50 Scenario C bandwidth figures for n=100 planning — buffer and packet analysis changes nonlinearly.
- GoldSyncEvent drop-first policy creates guaranteed gold-balance staleness under sustained peak load; any downstream economy system that reads displayed balance must account for this.
