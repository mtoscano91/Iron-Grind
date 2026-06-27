# Review Log: Combat UI

---

## Review — 2026-06-20 (lean re-review) — Verdict: APPROVED

Scope signal: XL
Specialists: None (lean mode)
Blocking items: 0 | Recommended: 4 | Nice-to-Have: 4
Summary: All 23 prior blockers confirmed resolved. F-CUI-1 arithmetic is sound (signed long subtraction, dual guard). Renderer is implementation-defined with perf AC. Silenced (8) and CasterNotAlive (9) rejection codes present. Bar state persisted server-side. EC-CUI-4 timeout recovery path complete. Four recommended revisions identified (none blocking): EC-CUI-8 claims a guard is "in F-CUI-1" but it isn't; character-persistence dependency entry stale (file now exists); missing AC for duplicate binding sync (CR-CUI-4); missing AC for SkillUnlockNotification transition (CR-CUI-14). All dependency GDDs confirmed on disk including character-persistence.md.
Prior verdict resolved: Yes — 23 blockers from 2026-06-20 NEEDS REVISION pass; 0 remain.

---

## Review — 2026-06-20 — Verdict: NEEDS REVISION

Scope signal: XL (multi-system UI + networking integration; 7 upstream dependencies; 2 ADR interactions; formula bugs with security implications)
Specialists: game-designer, systems-designer, qa-lead, ux-designer, performance-analyst, network-programmer, creative-director
Blocking items: 23 | Recommended: 0
Summary: The GDD was structurally complete but contained three compounding bugs in F-CUI-1 (uint underflow causing expired cooldowns to render as full arcs, NaN from division-by-zero when CooldownTicks=0, and a C# uint/int compile error), a missing primitive contract for SkillCooldownUpdate delivery (SkillCooldownUpdate was cited 3 times but had no formal delivery guarantee in skill-system.md), and a renderer mandate (Painter2D) that could not guarantee 60fps on the target device with 8 concurrent arcs. Missing server-side validation checks (CasterNotAlive liveness at V-0, Silenced status at V-5b) created an exploitable dead-player casting path under network jitter. All 23 blockers were resolved in the same session via collaborative revision — bar state persistence added, EC-CUI-4 timeout recovery added, CR-SK-23 delivery contract authored, and entities.yaml updated with Silenced=8 and CasterNotAlive=9.
Prior verdict resolved: First review (all blockers resolved same-session; lean re-review pending)
