# Zone Instancing — Design Review Log

## Review — 2026-05-30 — Verdict: APPROVED
Scope signal: XL
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 | Recommended: 6
Summary: Lean re-review after all 8 OQ-ZI blockers were resolved across networking-wire-protocol.md, auth-wire-messages.md, networking-test-harness.md, character-persistence.md, and networking-core.md. One new blocker found introduced during the first review's routing fix: CR-ZI-6 Rule 3 routed to any below-cap instance (≥ ZONE_OPEN_THRESHOLD eligible), making Rule 4's threshold condition unreachable — new instances only spawned at hard cap (50), not at social density target (35). Fixed by restricting Rule 3 to instances < ZONE_OPEN_THRESHOLD. Six recommended items resolved: F-ZI-1 formula intermediates updated to post-OQ-ZI-2 sizes (result [17,19] unchanged), three stale OQ annotations cleaned, Death & Respawn status corrected to Approved, AC-ZI-10 ambiguity resolved, AC-ZI-18/19/20 added for centroid fallback and fill-first routing invariant coverage.
Prior verdict resolved: Yes — NEEDS REVISION (2026-05-29, 8 blocking OQs)

## Review — 2026-05-29 — Verdict: NEEDS REVISION
Scope signal: XL
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, unity-specialist, creative-director
Blocking items: 8 | Recommended: 12
Summary: First-pass full review identified 5 root-cause clusters. Engineering quality of the document is strong; the two critical gaps were (1) Social Gravity routing — lowest-count routing dispersed players, violating the "world that didn't wait for you" fantasy, fixed this session with fill-first routing + ZONE_OPEN_THRESHOLD + party co-location; and (2) four missing wire protocol primitives (OQ-ZI-1–4) in networking-wire-protocol.md and auth-wire-messages.md which remain BLOCKING before implementation. Additional fixes applied: ZoneSessionState enum values, CR-ZI-17 stale slot reclamation, 4 new tuning knobs, F-ZI-1 degenerate clamp and worst-case F=19, CR-ZI-11 dict key fix, party notify ordering correction, Unity headless runtime model section. Re-review recommended with --depth lean after the 8 wire-primitive OQs are resolved in their respective documents.
Prior verdict resolved: N/A — first review
