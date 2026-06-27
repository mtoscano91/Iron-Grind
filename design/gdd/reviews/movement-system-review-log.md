# Review Log: Movement System

> **GDD:** design/gdd/movement-system.md
> **System:** Movement System (Core+ Layer, #16 in design order)

---

## Review — 2026-05-26 — Verdict: APPROVED
Scope signal: M
Specialists: None (lean — single-session analysis)
Blocking items: 0 | Recommended: 1
Summary: All 12 blockers from the 2026-05-26 MAJOR REVISION NEEDED pass confirmed closed. The anticipatory positioning Player Fantasy rewrite resolves the core ludonarrative dissonance (pillar/architecture mismatch). Formulas self-consistent at boundary values, state machine handles all transitions, FacingAngle wire encoding complete, all 28 acceptance criteria independently testable. One pre-implementation action remains: OQ-MOV-1 (Wire Protocol GDD must register MovementIntentMessage before coding starts), correctly tracked as an open question. One recommended revision: CR-MOV-10 should explicitly state that active auto-run counts as movement input to prevent send-suppression implementation bugs.
Prior verdict resolved: Yes

---

## Review — 2026-05-26 — Verdict: MAJOR REVISION NEEDED

Scope signal: L
Specialists: game-designer (full), systems-designer (full), network-programmer (lean), qa-lead (lean), ux-designer (lean), performance-analyst (lean), creative-director (synthesis)
Blocking items: 12 | Recommended: 13 | Nice-to-have: 8
Prior verdict resolved: No — first review

**Summary:** The GDD is structurally complete (8/8 sections, detailed formulas, edge cases, state machine) but contains one pillar-level creative decision and a cluster of correctness bugs that block implementation. The primary blocker is a ludonarrative dissonance: the Player Fantasy promises frame-accurate kiting footwork that a 20 Hz server-only architecture (no client-side prediction, 150–250ms mobile RTT) cannot physically deliver. This requires a creative decision before any other fixes — either reframe Section B toward anticipatory beat-timing (cheap) or promote client-side prediction into MVP scope (expensive). Secondary blockers include: all F-MOV-2 worked examples using the wrong v_base (5.0 vs. 6.0), the facing-rotation-during-stun mechanic having no wire protocol support, EC-MOV-11 referencing a nonexistent tuning knob, the F-MOV-7 overflow guard hardcoding the STALE_TICK_TOLERANCE value, CR-MOV-7 contradicting OQ-MOV-4 on client NavMesh load, the state machine missing Idle→Prohibited, and the auto-face IPC mechanism being unspecified.

**Key decisions needed from author before next revision sprint:**
1. Is frame-accurate twitch kiting the soul of Rhythm Mastery, or is anticipatory positioning (1-2 ticks ahead) the intended skill? Answer determines Section B rewrite and scope of client-side prediction.
2. Is "no client-side prediction at MVP" a hard scope constraint, or can it move if the fantasy demands it?
