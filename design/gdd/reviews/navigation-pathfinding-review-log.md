# Review Log: Navigation / Pathfinding

---

## Review — 2026-06-14 (Pass 3, lean) — Verdict: APPROVED

Scope signal: L
Specialists: None (lean — single-session analysis)
Blocking items: 1 (applied in-session) | Recommended: 0
Prior verdict resolved: Yes — all 2 Pass 2 blockers verified closed; 1 residual stale annotation in CR-NAV-15 found and corrected in-session.

Summary: Fix 1 (CR-NAV-16 Resume() rule) fully confirmed closed — rule body, interface lists, AC-NAV-24, and traceability row all present and consistent. Fix 2 (F-NAV-2 stale tick values) was 95% closed — F-NAV-2, AC-NAV-17, and AC-NAV-19 all correctly updated to T=20 (1.0s), but CR-NAV-15 inline annotation still read "(default 60 ticks = 3.0s)". Single-line correction applied in-session. All blockers from all three review passes are now closed. Document marked Approved. Propagation to zone-instancing.md and enemy-ai.md pending (deferred to post-approval propagation pass).

---

## Review — 2026-06-14 (Pass 2, lean) — Verdict: NEEDS REVISION

Scope signal: L
Specialists: None (lean — single-session analysis)
Blocking items: 2 (applied in-session) | Recommended: 4
Prior verdict resolved: Partial — 4 of 6 prior blockers resolved in intervening authoring session; 2 targeted fixes applied in this review session

Summary: Both missing ADR primitives from the prior review are now authored and accepted (ADR-002 execution & concurrency contract; ADR-003 agent lifecycle state machine). Four of six prior blockers fully resolved before this review ran. Two targeted blockers identified and fixed in-session: (1) Resume() was referenced in CR-NAV-9 but had no formal CR rule, no interface entry, and no AC — added CR-NAV-16 with behavior spec and Dormant-state caveat, updated INavigationProvider interface lists, added AC-NAV-24; (2) PARTIAL_PATH_TIMEOUT_TICKS default was inconsistent — Tuning Knobs said T=20 (1.0s) but F-NAV-2 example and AC-NAV-17/19 all tested T=60 (3.0s) boundaries — corrected F-NAV-2 Output Range and Examples plus AC boundaries. Entity registry was already correct. Advisory items remaining: link.xml server-build reference in GDD body, _tornDown guard AC, speed-restore AC for Returning→Dormant.

---

## Review — 2026-06-14 — Verdict: MAJOR REVISION NEEDED

Scope signal: XL
Specialists: ai-programmer, network-programmer, systems-designer, performance-analyst, unity-specialist, qa-lead, game-designer → creative-director (synthesis)
Blocking items: 6 | Recommended: 11
Prior verdict resolved: N/A (first review)

Summary: Four independent specialists converged on two root causes driving most blockers. (1) The execution model contradicts how Unity NavMesh actually works — pathfinding is asynchronous, tick order is described three incompatible ways across CR-NAV-12/CR-NAV-14/EC-NAV-7, and `NavMeshAgent` simulation runs at Unity's frame rate not 20Hz. (2) The agent lifecycle state machine enumerates the happy path but not re-entrant and terminal transitions — mob permanently freezes after every attack cycle (no `Resume()` API), fast mobs get stuck in Returning forever (`autoBraking=false` overshoot), and the drain crashes with `KeyNotFoundException` on every Stop-during-pursuit event. Additional blockers: no performance budget, `UnityEngine.AI` will be stripped by IL2CPP in the Dedicated Server build without a `link.xml`, no post-validation spawn point health check, and AC-NAV-04 directly contradicts CR-NAV-5 (asserts exception; spec says log-and-return).

**Recommended corrective action:** Extract two missing primitive contracts before re-reviewing — (1) Tick Execution & Concurrency Contract (ADR), (2) Agent Lifecycle State Machine spec. Apply B3 drain crash fix and B5 spawn viability check directly to the GDD. Run `/design-review --depth lean` after contracts are approved.
