# HUD (Heads-Up Display) — Review Log

## Review — 2026-06-20 — Verdict: APPROVED (via immediate revision)
Scope signal: L
Specialists: none (lean — single-session)
Blocking items: 1 | Recommended: 2 | Nice-to-have: 2
Summary: Revision 1 closed all 17 prior blockers. One new blocker found: AC-HUD-10 contained an incorrect assertion ("only Online may change the Ghost-frozen bar") that would cause QA to reject valid Ghost→Dead transitions. Two recommended fixes (CR-HUD-22 dimensional ambiguity, CR-HUD-7 CharacterID filter gap) and two nice-to-haves (CR-HUD-5 MP guard, new AC-HUD-26) also applied. All 5 items resolved in the same session. Three pre-implementation gates remain correctly tracked as open questions: OQ-HUD-1 (UI framework ADR), OQ-HUD-7 (UX spec), OQ-HUD-8 (Status Effects event interface). Document is implementation-ready pending those gates.
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (2026-06-19, 17 blockers) fully closed

## Review — 2026-06-19 — Verdict: MAJOR REVISION NEEDED
Scope signal: XL
Specialists: game-designer, ux-designer, qa-lead, systems-designer, unity-ui-specialist, performance-analyst, creative-director (senior synthesis)
Blocking items: 17 | Recommended: 12
Summary: Two guaranteed runtime bugs (F-HUD-3 integer division truncation, MaxHP=0 NaN propagation), a photosensitivity compliance gap (pulse frequency unspecified), an undefined Canvas dirty-isolation architecture, and a layout that physically overflows by ~75dp. Several blockers cannot be resolved inside hud.md alone — they require upstream contracts: the Status Effects event schema, OQ-HUD-1 re-scoped (world-space damage numbers make mixed-framework mandatory), Movement System reconciliation for auto-run placement, and a joystick exclusion boundary coordinate. The Player Fantasy ("peripheral sensing") has no supporting design mechanism in the spec. Re-review recommended only after Stage A upstream contracts are extracted.
Prior verdict resolved: No — first review
