---
name: project-csp-perf
description: Client-Side Prediction GDD adversarial performance review — Pass 1 had 6 BLOCKING (all resolved in current GDD); Pass 2 has 3 BLOCKING: GC prohibition incomplete (AC-CSP-9 unenforceable), cold-start extrapolation velocity undefined, snap event path during AC-CSP-9 window unspecified
metadata:
  type: project
---

Client-Side Prediction GDD (design/gdd/client-side-prediction.md) is Designed (pending review) as of 2026-06-14. Detailed Rules section is fully authored in current GDD.

**Why:** Designer asked for adversarial performance review; Pass 1 was pre-authoring (2026-06-14); Pass 2 is post-authoring adversarial review against the full Detailed Rules, Formulas, and Acceptance Criteria.

**How to apply:** Pass 1 BLOCKING items all resolved. Pass 2 has 3 BLOCKING items and 7 RECOMMENDED items to resolve before design review approval. The most dangerous open item is Finding 6 (extrapolation velocity undefined at cold-start — uninitialized data path to garbage entity position).

## Pass 1 BLOCKING Items (all resolved in current GDD)

- B-CSP-1: Wire protocol 35-cap vs 40-60 entity claim → resolved via CR-CSP-18
- B-CSP-2: Snapshot type unspecified → resolved via CR-CSP-19 (struct mandated)
- B-CSP-3: Divergence calc wrong without per-tick history → resolved via CR-CSP-7 (ring[T] lookup)
- B-CSP-4: Reconciliation lerp uncapped → resolved via CR-CSP-9 (MAX_CORRECTION_SPEED cap)
- B-CSP-5: t>1.0 interpolation undefined → resolved via CR-CSP-15 (75ms delay buffer)
- B-CSP-6: Reconciliation threshold undefined → resolved via F-CSP-3 (0.10 units, derivation documented)

## Pass 2 BLOCKING Items

**BLOCK-2-CSP-1 — CR-CSP-19 GC prohibition incomplete; AC-CSP-9 unenforceable**
Three missing prohibitions: (a) boxing of value types via interface method calls; (b) OnReconciliationSnap event type unspecified — delegate subscription allocates on heap; C# event Action vs UnityEvent vs interface all have different allocation profiles; (c) NGO NetworkManager.ServerTime.Tick allocation status unverified in NGO 2.x / Unity 6.3. AC-CSP-9 ("0 bytes GC in 300 frames") cannot be a CI gate until event type is specified and NGO clock path is verified.

**BLOCK-2-CSP-2 — AC-CSP-9 snap event window ambiguous**
AC-CSP-9 runs 300 frames (~5s). If CR-CSP-10 fires during the window, OnReconciliationSnap.Invoke() happens in the test period. Whether this allocates depends on event type (unresolved per BLOCK-2-CSP-1). Test specification does not state snap events must be absent from the 300-frame window. GC measurement mechanism (ProfilerRecorder vs GC.GetTotalMemory) also unspecified.

**BLOCK-2-CSP-3 — CR-CSP-17 extrapolation velocity undefined for first-snapshot entities**
CR-CSP-17 requires estimated velocity = (S0.pos - S_prev.pos) / dt. S_prev is the prior snapshot in the 3-entry buffer. At cold-start (1 snapshot only), S_prev does not exist. State table says EXTRAPOLATING entry condition = "1 snapshot; extrapolation age < MAX_EXTRAPOLATION_MS." Combined with undefined velocity source → uninitialized/garbage velocity → entity teleports to random position. Must specify: "if S_prev absent, treat velocity as zero; skip EXTRAPOLATING, enter HELD immediately."

## Pass 2 RECOMMENDED Items

**REC-2-CSP-1 — nlerp mandate incorrect**: CR-CSP-16 says "linear quaternion interpolation is not normalized" — nlerp IS normalized. The prohibition blocks a valid 5-10x optimization at zero visual cost at 50ms tick intervals with small rotation deltas. Correct to: "Slerp provides constant angular velocity; nlerp is permitted as a profiling optimization when rotation deltas are small."

**REC-2-CSP-2 — Bracket search O(1) claim misleading**: CR-CSP-19 says "O(1) array-index access" for entity slot lookup, but CR-CSP-16's bracket search (find S0/S1 in 3-entry circular buffer) is an O(3) conditional scan. Bracket search algorithm (how to find newest/oldest in circular buffer) is unspecified.

**REC-2-CSP-3 — Fallback rate 41.7% exceeds stated 40%**: At n=60, (60-35)/60 = 41.7%. Near-50/50 branch in the hot loop between INTERPOLATING and fallback states. Per-entity state storage location (byte in struct vs separate array vs computed each frame) is unspecified.

**REC-2-CSP-4 — Snapshot struct never formally defined**: CR-CSP-19 mandates struct type. Fields implied by F-CSP-2 but no explicit struct definition with field names, types, computed size. L1 working set: ~8KB at n=60 vs 128KB A14 L1 — fine.

**REC-2-CSP-5 — F-CSP-4 correction decay step formula absent**: correctionFrames formula implies linear decay but per-frame step formula is missing. Without direction-clamped magnitude check on final step, correction overshoots to negative → oscillation. AC-CSP-3 unverifiable.

**REC-2-CSP-6 — Entity despawn mid-session cleanup unspecified**: EC-CSP-2 covers zone transition but not individual entity despawn. Stale snapshot buffer survives slot reuse → new entity inherits old positions → interpolates to wrong position on first 2 server ticks.

**REC-2-CSP-7 — No frame-time AC criterion**: AC-CSP-9 tests GC only. No criterion tests frame cost. Estimated hot path: ~0.07ms at n=60 (well within budget) but no CI regression threshold exists. Suggested: ≤0.5ms for n=60 on A14+.

## Slerp Cost Analysis (Pass 2)

60 entities × 60fps = 3,600 Slerp calls/sec = 60/frame. At 300-500ns per call on A14 IL2CPP: 18-30μs/frame = 0.1-0.18% of 16.6ms budget. Not a blocking concern at n=60.

## Memory Analysis (Pass 2)

Snapshot buffer: 60 entities × 3 × ~40B = 7.2KB. Ring buffer: 32 × ~24B = 768B. Total: ~8KB. A14 L1 = 128KB. Entire CSP hot path fits in L1. No cache pressure concern at current scale.
