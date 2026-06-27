# Review Log: Death & Respawn

---

## Review — 2026-05-29 (lean re-review #2) — Verdict: APPROVED

Scope signal: S
Specialists: None (lean mode — single-session analysis)
Blocking items: 0 | Recommended: 0
Summary: All 3 prior blockers resolved. OQ-DR-1 confirmed resolved in party-system.md (MemberStatus.Dead=3 added). OQ-DR-2 confirmed resolved by zone-instancing.md CR-ZI-11 (GetTownRespawnPoint API fully specified). OQ-DR-6 resolved at design level by zone-instancing.md CR-ZI-8 step 6 (isInDeadState/respawnTicksRemaining semantics and computation specified; wire struct amendment tracked as OQ-ZI-2 in zone-instancing.md). Five doc-hygiene edits applied in-session: EC-DR-1 reconnect-while-DEAD behavior written from CR-ZI-8 step 6; CR-DR-8/CR-DR-14 provisional notes replaced with CR-ZI-11 references; upstream/downstream Dependencies tables updated; bidirectional status updated; AC-DR-13 OQ-DR-1 gate cleared. No new structural issues found.
Prior verdict resolved: Yes — all 3 prior cross-system blockers closed.

---

## Review — 2026-05-29 (lean re-review) — Verdict: NEEDS REVISION

Scope signal: L
Specialists: None (lean mode — single-session analysis)
Blocking items: 3 (all cross-system) | Recommended: 5 (all applied in-session)
Summary: Wire protocol coverage complete — 4 messages registered and RFR-7 added in the authoring session preceding this review. Lean re-review found no new structural issues. Remaining blockers are cross-system dependencies that cannot be resolved in this document: Party System (OQ-DR-1), Zone Instancing (OQ-DR-2), and a new gap (OQ-DR-6) where EntityState in ZoneStateSnapshotFragment needs isInDeadState/respawnTicksRemaining for the reconnect-while-DEAD case. All 5 recommended doc-hygiene fixes applied in-session: stale API names corrected, Networking Core rows updated to COMPLETED, Character Persistence row updated to PATCHED, CR-DR-10 clarified, AC-DR-NEW-9 added.
Prior verdict resolved: Partial — 2/5 prior cross-system blockers closed (wire registration + RFR-7). 3 remain.

---

## Review — 2026-05-29 — Verdict: NEEDS REVISION (post-revision state)

Scope signal: XL
Specialists: game-designer, systems-designer, qa-lead, network-programmer, performance-analyst, ux-designer, creative-director (synthesis)
Blocking items: 30 | Recommended: 8
Summary: First full review surfaced a structural Player Fantasy ↔ Detailed Rules contradiction: the Player Fantasy described town-return respawn with a recalibration walk, while CR-DR-8/CR-DR-14 specified same-zone nearest-anchor respawn. Creative-director ruled for town respawn. Revisions applied in-session resolved the respawn model contradiction, fixed the DeathState struct (uint → long), extended CR-DR-2 deduplication to Respawning state, corrected the ghost death wire-message description, specified the camera fade-to-black sequence, fixed UI accessibility (VoiceOver pattern, touch passthrough), and added 8 missing ACs. Remaining blockers require other GDDs: 5 wire message schemas in networking-wire-protocol.md, EntityDied relevance filter rule, ZoneStateSnapshot DeathState fields, Party System MemberStatus.Dead, Zone Instancing GetTownRespawnPoint API.
Prior verdict resolved: No — first review
