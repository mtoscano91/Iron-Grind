---
name: status-effects-buffs-perf-pass1
description: Adversarial performance review of Status Effects / Buffs GDD for Iron Grind — 6 BLOCKING items covering hot-path call volume, mass-expiry spike, OnStatChanged fan-out, allocation claim integrity, array layout ambiguity, and CI-unenforeable AC
metadata:
  type: project
---

Status Effects / Buffs GDD adversarially reviewed 2026-05-20 (Pass 1). 6 BLOCKING items, 2 RECOMMENDED.

**Why:** The document's zero-allocation claim and flat-array layout are underspecified at the C# type level, making the core design claim unverifiable. The hot path and expiry path are unbudgeted. The OnStatChanged fan-out design (CR-SE-17) produces a bandwidth spike in the same mass-expiry scenario the GDD does not analyze.

**How to apply:** When reviewing the authoring session fixes, verify in order: struct layout decision (PA-SE-04, PA-SE-05), then tick budget table (PA-SE-01), then expiry mitigation strategy (PA-SE-02, PA-SE-03), then AC-SE-25 rewrite (PA-SE-06).

**Cross-doc links:**
- CharacterStats no-cache policy and OQ-5 batch API are directly load-bearing for PA-SE-01 (64,000 calls/second). If OQ-5 is unresolved, the tick budget analysis cannot be completed. [[Character Stats performance review findings]]
- Party System PA-PS-01 (HP traffic unmodeled in wire protocol) is amplified by PA-SE-03 (OnStatChanged fan-out). Both must be resolved before the wire protocol bandwidth model is considered complete.
- Networking Core tick budget (mob scope excluded, Pass 8 BLOCKING) creates a shared-budget conflict with PA-SE-01 — status effects tick cost must be allocated within whatever budget remains after mobs are accounted for.

**Blocking items:**

| Tag | Issue |
|-----|-------|
| PA-SE-01 | 64,000 CharacterStats calls/second unbudgeted; no tick budget allocation table |
| PA-SE-02 | Mass-expiry spike (12,800 RemoveBuffModifier calls/tick) unanalyzed; no mitigation |
| PA-SE-03 | OnStatChanged unbatched; 12,800-event fan-out per tick in mass-expiry scenario |
| PA-SE-04 | Zero-allocation claim unverifiable; snapshot field type unspecified (array vs value-type) |
| PA-SE-05 | "Flat 2D array" implies jagged C# layout; 1D contiguous layout not specified |
| PA-SE-06 | AC-SE-25 CI verification methods (Profiler, GC.GetTotalMemory) both inadequate; GC.TryStartNoGCRegion() not mentioned |

**Recommended items:**

| Tag | Issue |
|-----|-------|
| PA-SE-07 | Capacity counter threading model unspecified; single-threaded assumption undocumented |
| PA-SE-08 | Death-path deferred removal creates stale-state risk on respawn; no guarding AC |

**Recommended fix sequence:** PA-SE-04 + PA-SE-05 first (defines the struct), then PA-SE-01 (budget knowable after struct is fixed), then PA-SE-02 + PA-SE-03 together (shared expiry-storm root), then PA-SE-06 (AC rewrite follows struct decision).
