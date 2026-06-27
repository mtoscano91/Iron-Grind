---
name: party-system-perf-review
description: Party System GDD adversarial review Pass 1 — 3 BLOCKING items; HP traffic unmodeled in wire protocol batch, lastSentHP semantics undefined, sequenceId undefined on PartyStateUpdate
metadata:
  type: project
---

Party System GDD adversarially reviewed 2026-05-16 (Pass 1).

**3 BLOCKING items:**

**PA-PS-01 (BLOCKING) — Party HP traffic absent from wire protocol batch model**
R-U batch is capped at 512 bytes (wire protocol, Approved). In a 6-member party, HP updates consume 50-110 bytes per tick per receiving client (5 sub-messages × 10-22 bytes). This is 10-21% of the batch cap before combat/position/zone-state traffic. Wire protocol Scenario B/C/D models solo players only — party membership is not a modeled load term. HP updates will be dropped under the existing drop policy during peak combat. GDD does not specify drop behavior or recovery for HP updates. Healer's party frame goes stale during maximum-pressure combat. Escalate to technical-director for wire protocol re-analysis.

**PA-PS-02 (BLOCKING) — lastSentHP update semantics unspecified**
GDD does not state whether lastSentHP is updated per-tick or per-send. Per-send update allows HP to drift by up to 4.9% × maxHP on party members' displays during stable oscillating combat, indefinitely without triggering an update. For maxHP=5000 this is 245 HP of silent display error — a healer-decision correctness failure.

**PA-PS-03 (BLOCKING) — PartyStateUpdate sequenceId undefined**
sequenceId (uint32) is present on PartyStateUpdate but client validation rule is unspecified. On a reliable-ordered channel, sequenceId is either dead code (remove it) or a guard against a specific race condition (name the race, specify the rule). Unspecified = inconsistent implementations.

**5 REVISION items:**

PA-PS-04: HP_DELTA_THRESHOLD_FRACTION integer floor not specified. At maxHP ≤ 19, threshold=0 regardless of knob value — dead zone undocumented.

PA-PS-05: N_eligible zone-membership consistency model unspecified. Kill events during zone transitions can silently miscalculate XP splits.

PA-PS-06: RrNextIndex/loot UI race not specified. R-OD delivery latency means client loot display may show wrong recipient transiently. Suppress-until-confirmed vs. provisional-display policy unspecified.

PA-PS-07: Invite state during Ghost transition undefined. Silent cancellation during 5s grace window without notification to inviter is a UX failure.

PA-PS-08: lastSentHP data structure unspecified. 2D array by member slot [0..5][0..5] mandated over Dictionary for GC hygiene and cache locality. Ties to F-NET-8 GC risk in Networking Core GDD.

**Key bandwidth numbers:**
- HP update bandwidth per client (worst case, no suppression): 0.98-2.15 KB/s (trivial in isolation)
- Party HP updates as batch fraction: 10-21% of 512-byte R-U cap (significant under combat load)
- PartyStateUpdate size: ~40 bytes; 6 sends per common drop event; bandwidth cost trivial
- Suppression check CPU: ~48,000 comparisons/second at 8 parties/zone — negligible

**Suppression strategy assessment:**
Delta suppression is effective when HP moves slowly (< 5% maxHP/tick). Full-health suppression is nearly useless during active combat — members rarely have HP=maxHP AND MP=maxMP simultaneously in combat. Suppression is optimized for the out-of-combat case (when bandwidth is least constrained) and provides limited relief during peak load.

**Cross-document links:**
- Wire protocol batch model must be re-analyzed to include party HP traffic (PA-PS-01 → wire protocol GDD)
- F-NET-9 (mob scope, unresolved) makes batch overflow more frequent than party system GDD anticipates
- Character Stats HP/MP ceiling must be confirmed — if maxHP > 32,767, HP fields need int32 (doubles wire cost)

**Why:** Wire protocol approved without party-system load term; all three blocking items are design-completeness gaps that will produce inconsistent implementations if shipped as-is.

**How to apply:** When reviewing any downstream system that reads party member HP for UI or healing decisions, flag the lastSentHP semantics gap (PA-PS-02). When reviewing wire protocol updates, flag that party HP updates must be added to Scenario B/C/D batch math.
