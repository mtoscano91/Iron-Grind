# Hit Detection — Design Review Log

---

## Review — 2026-05-25 — Verdict: APPROVED (lean re-review)
Scope signal: S
Specialists: none (lean mode)
Blocking items: 1 | Recommended: 3
Prior verdict resolved: Yes — 18 blockers from full-pass (2026-05-25) verified addressed

Summary: All 18 prior blockers confirmed fixed in GDD text. One additional blocker found and fixed inline: entities.yaml EntityState.facingAngle range note was stale (0–3590, pre-fix value) and did not reflect the corrected range (0–3599) from blocker #6 in the prior session. The GDD Revision Triad from that session was one note-field short. Fix: one-line string update in entities.yaml. GDD text is mechanically complete, internally consistent, fully implementable, and has 100% rule → AC coverage. All formulas verified at boundary values. Sole pre-implementation gate: networking-wire-protocol.md must still receive the HitCheckResult message definition + FacingAngle schema update (EntityState 69B → 71B) before implementation begins.

---

## Review — 2026-05-25 — Verdict: MAJOR REVISION NEEDED → Blockers Fixed Inline

Scope signal: XL
Specialists: game-designer, systems-designer, network-programmer, qa-lead, performance-analyst, unity-specialist, creative-director (senior)
Blocking items: 18 | Recommended: 9
Prior verdict resolved: No — first review

Summary: Three root causes drove all 18 blockers. (1) Feedback channel inversion: HitCheckResult was addressed to the attacker's client; for monster attacks the attacker has no client, making the Rhythm Mastery sidestep invisible to the player it was designed to reward. Fixed by redirecting to the defending player's client. (2) Missing spatial-conventions primitive: F-HD-2 used full 3D normalization where capturedForward was XZ-only (Y=0), POINT_BLANK_EPSILON_SQ was smaller than worst-case wire encoding noise (0.0001 < 0.0002), and F-HD-3 wire range 0–3590 truncated angles 359.1°–359.9°. All three formula bugs fixed. (3) Pillar aspiration not delivered by mechanics: capturedForward was documented as captured "at initiation" but EC-HD-3 calculated based on a one-tick (50ms) window — inconsistent with the stated 600ms wind-up. Design decision made: capturedForward locked at wind-up start; EC-HD-3 rewritten with correct 600ms geometry and tuning recommendation (45° tolerance needed for sidestep at default 3m range). Additional fixes: HitResult enum declared `: byte` for IL2CPP safety; EntityID changed ushort → uint per CR-NET-7.3; asmdef defineConstraints required; 6 new ACs added for uncovered rules (CR-HD-7, CR-HD-8, CR-HD-9); CR-HD-5 asymmetry documented as intentional design decision.
