# Review Log — Networking Ghost Character State

---

## Review — 2026-05-15 — Verdict: APPROVED (Pass 2 lean)
Scope signal: M
Specialists: none (lean mode)
Blocking items: 0 | Recommended: 0 remaining
Summary: All 9 Pass 1 blockers verified closed. Lean review found 2 residuals from the revision: AC-CGS-3 SESSION_CLOSED text not updated alongside its rule (CGS-6 step 3), and CGS-6 step 1 "update HP" contradicting GD-CGS-2. Both fixed in-session (2 additional lines changed). Two recommended items also applied: CGS-4 step 3 now specifies GhostTtlExpired reason; Overview reworded to reflect GD-CGS-2 resolution. Document is structurally sound and fully implementable.
Prior verdict resolved: Yes — NEEDS REVISION (Pass 1 full, 2026-05-14)

---

## Revision — 2026-05-15 — Pass 1 revision applied (9 blockers closed)

**Design decisions collected:**
- GD-CGS-1: WAL on disconnect — snapshot written to WAL before IsGhost=true; survives all zone failure modes
- GD-CGS-2: No HP loss on ghost death — pre-disconnect snapshot HP persisted on both TTL expiry and ghost death paths; only respawn position applied on ghost death

**Blocker resolution:**
| Blocker | Fix |
|---------|-----|
| B-CGS-1: SESSION_CLOSED undefined | CGS-6 step 3 → ZoneSessionEnded(DisconnectReason.GhostDeath); GhostDeath=3 added to DisconnectReason enum in wire-protocol |
| B-CGS-2: GhostExpiredReason.GhostDeath missing | GhostDeath=2 added to GhostExpiredReason enum; CGS-5 now emits GhostExpiredEvent(GhostDeath) |
| B-CGS-3: No crash durability for 30s window | WAL write sequence added to CGS-3 Step 3 |
| B-CGS-4: disconnectTickNumber undefined | Defined in CGS-3 Step 1 as ServerTickNumber of transition tick |
| B-CGS-5: PersistenceWriteReason.GhostDeath missing | GhostDeath=5 added to test-harness PersistenceWriteReason enum |
| B-CGS-6: Snapshot fields non-exhaustive | Exhaustive field table added to CGS-3 Step 2 (15 named fields) |
| B-CGS-7: AC-CGS-4 unassertable timestamps | Fixed — callback sequence index replaces timestamp ordering |
| B-CGS-8: CGS-1 has no AC | AC-CGS-5 added (ghost HP server-authoritative, EntityHealthUpdate broadcast) |
| B-CGS-9: Ghost death full-death-penalty conflicts with Pillar 1 | CGS-5 fully rewritten for GD-CGS-2 |

**Propagation (5 files updated):** networking-ghost-character-state.md, networking-wire-protocol.md, networking-test-harness.md, networking-channel-contract.md, networking-ghost-session.md (CR-GH-6, CR-GH-9.1, CR-GH-10.1, EC-GH-2, AC-GH-4).

---

## Review — 2026-05-14 — Verdict: NEEDS REVISION (Pass 1 full)
Scope signal: M
Specialists: network-programmer, systems-designer, qa-lead, game-designer
Blocking items: 9 | Recommended: 6
Summary: First full review. Two blocker clusters: (1) undefined wire-layer primitives — SESSION_CLOSED/GHOST_DEATH has no wire schema, GhostExpiredReason.GhostDeath missing from enum (zone clients get no ghost-death removal signal), PersistenceWriteReason.GhostDeath missing from test-harness enum, disconnectTickNumber persistence key never formally defined; (2) unaddressed failure modes — pre-disconnect snapshot has no crash durability for the 30-second in-memory window, CGS-3 snapshot fields non-exhaustive ("all other character fields" not implementable), AC-CGS-4 timestamp ordering within a tick unassertable, CGS-1 has no AC, ghost death full-death-penalty conflicts with Earned Power (Pillar 1). Document structure is sound; all fixes are targeted — no structural rewrite required.
Prior verdict resolved: N/A — first review.
