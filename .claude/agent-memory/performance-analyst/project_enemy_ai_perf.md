---
name: enemy-ai-perf-pass1
description: Adversarial performance review of Enemy AI GDD for Iron Grind — 6 BLOCKING items covering missing tick budget, IsPathStale contract gap, FacingAngle write thrash, O(N×P) aggro scan without partitioning, duplicated O(N) passes, and missing worst-case state analysis
metadata:
  type: project
---

Enemy AI GDD adversarially reviewed 2026-05-25 (Pass 1). 6 BLOCKING items, 3 RECOMMENDED.

**Why:** The document has no tick budget allocation for the AI phase, leaving OQ-AI-4 with no pass/fail threshold. The navigation interface is a stub with no performance contract. The aggro scan is the only O(N×P) term (N_dormant × P = up to 7,500 checks/scan) with no spatial partitioning. The GDD specifies 4–5 independent O(N) per-tick passes (FacingAngle write, IsPathStale poll, leash check, target validity check) without a combined-loop specification. The mass-aggro scenario (all 150 mobs aggroing in one tick) is the highest-cost single tick and is not analyzed.

**How to apply:** When reviewing authoring session fixes, verify in order: budget table (PA-AI-01) → navigation contract (PA-AI-02) → spatial partitioning (PA-AI-04) → worst-case scenario table (PA-AI-06) → loop consolidation (PA-AI-03 + PA-AI-05) → mob data model (PA-AI-07, PA-AI-08, PA-AI-09). Coordinate F-NET-6 update with engine-programmer after PA-AI-01 establishes the AI phase budget ceiling.

**Cross-doc links:**
- F-NET-9 (Networking Core Pass 8 BLOCKING) is the same tick budget gap. Both must be resolved jointly — OQ-AI-4 alone does not close F-NET-9. [[Networking Core GDD adversarial review (all passes)]]
- PA-SE-01/PA-SE-02 (Status Effects BLOCKING) share the budget gap; mass-expiry and mass-aggro could coincide in the same tick — combined spike is unanalyzed. [[Status Effects / Buffs GDD adversarial review Pass 1]]
- CR-AI-2 defines tick order (Status Effects → Enemy AI → Auto-Attack Combat) but no budget partition across phases.

**Blocking items:**

| Tag | Issue |
|-----|-------|
| PA-AI-01 | No per-phase AI tick budget; OQ-AI-4 has no pass/fail threshold, no hardware spec, no AI_TICK_BUDGET_MS constant |
| PA-AI-02 | IsPathStale polled every Pursuing tick via opaque stub interface; no performance contract (could be 0.1ms/call = 15ms/tick at N=150) |
| PA-AI-03 | FacingAngle written every tick with no delta threshold; AutoFaceEvent emission gate owner undefined; downstream R-U batch fan-out unmodeled |
| PA-AI-04 | Aggro scan O(N_dormant × P) = up to 7,500 checks/scan; no spatial partitioning; scan-tick spike (5× cost every 5th tick) unanalyzed |
| PA-AI-05 | F-AI-4 and CR-AI-10 LeashRange check overlap; 4–5 independent O(N) passes per tick; combined-loop not specified; cache-hostile by default |
| PA-AI-06 | No per-state population caps; mass-aggro scenario (all 150 mobs transitioning to Pursuing in one tick) is highest-cost tick and is unanalyzed |

**Recommended items:**

| Tag | Issue |
|-----|-------|
| PA-AI-07 | transform.eulerAngles.y in server hot path implies Unity Transform (Quaternion→Euler per tick); mob server-side data model not specified |
| PA-AI-08 | Mob state layout (AoS vs SoA, contiguous vs Dictionary-keyed) not specified; four O(N) passes are cache-hostile without contiguous layout |
| PA-AI-09 | Dead mobs in DEATH_LINGER_TICKS (30 ticks) not specified for per-tick loop inclusion; at 2 kills/tick steady-state = 60 concurrent Dead mobs wasting loop iterations |
