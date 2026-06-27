# Review Log: Client-Side Prediction

---

## Review — 2026-06-15 — Verdict: APPROVED (after in-session NEEDS REVISION fix)
Scope signal: XL
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 (resolved in-session) | Recommended: 4 (resolved in-session)
Prior verdict resolved: Yes — all 17 blockers from 2026-06-14 full review confirmed closed

**Summary**: All 17 prior blockers from the first pass confirmed resolved. One new blocker found: three locations in the GDD used stale "EntityState batch" terminology from before the wire protocol's `SelfPositionUpdate` amendment (2026-06-14). CR-CSP-7, the Interactions table, and F-CSP-3's source column all needed updating to reference `SelfPositionUpdate` (R-U batch) for reconciliation and `EntityPositionUpdate` (U-U) for entity interpolation. Additionally, CR-CSP-2's Y-source reference was updated to `SelfPositionUpdate.posY` (per-tick, per user decision), ring buffer notation was made consistent (`ring[T & 0x1F]`), and AC-CSP-7 was rewritten with concrete timestamps to eliminate mixed T reference frames. All fixes applied in-session. Architecture confirmed sound; all 13 ACs independently testable; all 4 upstream dependencies Approved.

**Open non-blocking:**
- OQ-CSP-2 (v_eff bootstrap): recommend Option C (ZoneStateSnapshot + EC-MOV-15 stat inclusion); verify EC-MOV-15 includes stats
- OQ-CSP-3 (post-approval): update movement-system.md to close OQ-MOV-4 with references to CR-CSP-2 and CR-CSP-14

---

## Review — 2026-06-14 — Verdict: MAJOR REVISION NEEDED

Scope signal: XL
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, unity-specialist, creative-director (senior synthesis)
Blocking items: 17 | Recommended: 8
Prior verdict resolved: N/A — first review

**Summary**: The conceptual architecture (local self-prediction + server reconciliation + entity interpolation on a delay buffer) is sound and industry-proven. The Player Fantasy ("never seen — only felt") is excellent. However, the specification depends on two upstream contracts that do not exist on disk and contains one genuine design error that breaks the system at its own stated RTT target. Six specialists from six domains independently converged on the same three root causes.

**Three root causes (creative-director synthesis):**
1. The wire protocol has no per-tick self-position message — reconciliation's data source does not exist. `EntityPositionUpdate` excludes the local player; a new wire message must be authored in `networking-wire-protocol.md` first.
2. NGO/`NetworkManager.ServerTime.Tick` is hardcoded throughout despite the project's Networking Core GDD deferring the library choice to an unresolved Technical Director ADR. Requires `INetworkClock` abstraction or library ADR resolution first.
3. OQ-CSP-1 (STALE_TICK_TOLERANCE=1): at 150ms RTT, ~50% of inputs are discarded in steady state. Character appears frozen/teleporting to all other players — breaks both Pillar 2 and Pillar 3 simultaneously. This is a design error requiring a co-authored design decision, not a tuning adjustment.

**Additional notable blockers:** ring buffer write timing unspecified (causes false reconciliation every tick under perfect conditions), correction decay formula absent, CR-CSP-11 bypasses snap threshold on mid-blend updates, snapshot buffer length=3 insufficient at max tuning value, reconcile threshold minimum below wire encoding noise floor, F-CSP-2 unit mismatch (75× error if formula header copied literally), Vector2/Vector3 Y-component unspecified, AC section fundamentally non-executable (12 blocking items including ACs testing internal state and code review items masquerading as QA criteria).

**Recommended next action**: Do not open a revision session on this GDD yet. Author the three missing upstream contracts in order: (1) wire protocol self-position message amendment, (2) networking library ADR (Technical Director), (3) OQ-CSP-1 design decision (game-designer + network-programmer co-author). Then lower CSP to implementation altitude (fix math, formulas, ACs). Then lean re-review.
