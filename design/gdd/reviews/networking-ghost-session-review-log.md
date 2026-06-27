# Review Log — Networking Ghost Session

---

## Review — 2026-05-14 — Verdict: APPROVED (Pass 3 lean)
Scope signal: L
Specialists: lean (no specialist agents)
Blocking items: 0 | Recommended: 3
Summary: All 4 Pass 2 blockers confirmed closed. Zero new blockers found. Three recommended items remain: GhostDismissRequest not yet in networking-channel-contract.md (cross-doc amendment needed before implementation); no AC for mob de-targeting on ghost death path (CR-GH-6 step 2 added in Pass 2); CR-GH-12 validation failure response unspecified. Document is structurally complete, internally consistent, and implementable as written.
Prior verdict resolved: Yes — all 4 Pass 2 blockers closed.

---

## Review — 2026-05-14 — Verdict: NEEDS REVISION (Pass 2 lean)
Scope signal: L
Specialists: lean (no specialist agents)
Blocking items: 4 | Recommended: 3
Summary: All 14 Pass 1 blockers confirmed resolved. Four precision-level blockers found: CR-GH-6 missing MobDeTargetCommand on ghost death path (CR-GH-7 cross-ref existed but was not called); CR-GH-9 steps 4–5 duplicated the session state transition already inside CR-GH-10 step 6; CR-GH-12 step 4 emitted a duplicate GhostExpiredEvent already covered by CR-GH-10 step 7 (fixed by adding a caller-supplied reason parameter to step 7); EC-GH-1 named the wrong state after a second transport drop (Reconnecting vs Disconnected_SessionActive per ST-NET-1). All 4 blockers fixed in-session. GhostDismissRequest not yet in networking-channel-contract.md — flagged as R-GH-1 (targeted amendment required before implementation). Ready for Pass 3 lean.
Prior verdict resolved: Yes — all 14 Pass 1 blockers closed.

## Fixes Applied In-Session (Pass 2)
- B-GH-1: CR-GH-6 step 2 added (MobDeTargetCommand per CR-GH-7 on ghost death)
- B-GH-2: CR-GH-9 steps 4–5 collapsed; transition delegated entirely to CR-GH-10 step 6
- B-GH-3: CR-GH-10 step 7 now accepts caller reason field; CR-GH-12 step 4 removed; timer cancellation moved to CR-GH-12 step 2 (before cleanup)
- B-GH-4: EC-GH-1 corrected to Disconnected_SessionActive
- R-GH-2: AC-GH-5 tick-type description corrected
- R-GH-3: AC-GH-18 pass condition restated with harness extension flag

---

## Authoring Session — 2026-05-14 — Pass 1 Revision (all 4 clusters addressed)

**Scope**: Full revision applying all 14 blockers from Pass 1 review.
**Changes applied**:
- **Cluster A** (broken contract references): state names corrected throughout (`Active` → `Connected`, `Draining`/`Closed` → session-state names, `Reconnecting` in CR-GH-2 step 1 → `Disconnected_SessionActive`); dead CR-NET-3 ref removed from CR-GH-5; `GhostPromotionEvent` and `GhostExpiredEvent` assigned channel (R-OD, S→ALL zone); `SESSION_TTL_S` = 300s added to F-GH-1 constraint; `Closed` → `Disconnected_SessionExpired` in CR-GH-6/9/10/11; CR-GH-10 cleanup reordered (persist at step 3 before session close at step 6, broadcast at step 7 — commit-before-broadcast compliant).
- **Cluster B** (ghost-character-state primitive): `networking-ghost-character-state.md` authored (CGS-1–6: HP authority, IsGhost lifecycle, pre-disconnect snapshot, write-ordering on TTL expiry, write-ordering on ghost death, death-vs-reconnect ordering guarantee); resolves CR-GH-10.1 vs CR-NET-6.5 contradiction; F-GH-2 corrected to `ZONE_TICK_MS + 2 × MOB_AI_TICK_MS = 250ms`; EC-GH-2 updated with CGS-6 ordering guarantee reference.
- **Cluster C** (design decisions applied): GD-GHOST-1 applied — CR-GH-8.1 restructured into two-pool model (pre-disconnect XP banked/safe; post-disconnect party shares forfeitable); CR-GH-9/9.1/9.2 revised accordingly; Overview updated. GD-GHOST-2 applied — Player Fantasy revised to "vulnerable teammate; party can protect, ignore, or dismiss"; CR-GH-12 + CR-GH-12.1 added (voluntary dismissal rule, dismiss-request API, reward policy).
- **Cluster D** (AC coverage): all existing ACs revised with specific harness method citations (`OnSessionStateTransitioned`, `OnGhostPromotionEventEmitted`, `OnGhostCombatTTLExpired`, `OnPersistenceWriteCompleted`, `IZoneTestConfigurator`, `IServerCrashInjector`); 10 new functional ACs added (AC-GH-11 through AC-GH-20); AC-GH-EXP-2 revised for voluntary dismissal clarity; AC-GH-EXP-4 added.
**Triad**: GDD revised; entities.yaml updated (GHOST_COMBAT_TTL_MIN_S added); session state updated.
**Next**: `/design-review --depth lean` on networking-ghost-session.md in a new session.

---

## Review — 2026-05-14 — Verdict: MAJOR REVISION NEEDED (Pass 1)
Scope signal: L
Specialists: network-programmer, game-designer, qa-lead, systems-designer, creative-director (synthesis)
Blocking items: 14 across 4 clusters | Recommended: 5
Summary: First review. Root cause is a missing ghost-character-state primitive — the GDD was authored without a spec that defines HP authority, IsGhost flag ownership, and ghost state lifecycle. Cluster A (broken contract references): state names from the wrong state machine (ST-NET-1 has no `Active` state; `Draining`/`Closed` are zone states); a nonexistent `Connected → Reconnecting` transition; dead CR-NET-3 citation; no channel assignments for GhostPromotionEvent/GhostExpiredEvent; SESSION_TTL_S treated as unknown despite being 300s in entities.yaml. Cluster B (persistence/data integrity): CR-GH-10.1 contradicts CR-NET-6.5 on persisted HP for ghost death; CR-GH-10 cleanup ordering persists after session closure (violates commit-before-broadcast); IsGhost flag authority unspecified; EC-GH-2 race condition underspecified; F-GH-2 formula understates worst-case de-targeting latency. Cluster C (design decisions): "parked car" Player Fantasy contradicted by killable ghost that shifts mob aggro and changes fight outcomes; binary forfeit policy conflicts with Pillar 1 Earned Power. Cluster D (AC coverage): 7 structurally untestable ACs; 12 rules with no corresponding AC. Creative-director: write ghost-character-state primitive first; make two design decisions (forfeit policy, ghost vulnerability + Player Fantasy) before authoring resumes. Re-review at --depth lean once primitive exists.
Prior verdict resolved: N/A — first review.

## Design Decisions Made Post-Review — 2026-05-14

**GD-GHOST-1 — Forfeit Policy (Cluster C)**
Decision: Bank earned XP up to the moment of disconnect. Only post-disconnect rewards (party XP shares accumulated while IsGhost = true) are at risk of forfeit on ghost death or TTL expiry. Pre-disconnect XP is never lost due to ghost-period events. Consistent with Pillar 1 Earned Power; eliminates the disproportionate punishment for infrastructure failures; closes the boss-snipe exploit (brief disconnect before kill → reconnect and retain) because post-disconnect party XP shares are the only forfeitable pool, and they are always less than the full-kill XP on a fast reconnect.

**GD-GHOST-2 — Ghost Vulnerability + Voluntary Dismissal (Cluster C)**
Decision: Ghost remains killable by mobs (as designed in CR-GH-5/6). Party gains a voluntary ghost dismissal option: any party member (or the party leader) can trigger early ghost slot release at any time during the TTL, with pre-disconnect banked XP preserved. This eliminates the "only way to free the slot early is to let the ghost die (and trigger forfeit + respawn)" incentive structure. Player Fantasy revised to: "The ghost is a vulnerable teammate. Your party can protect it, ignore it, or dismiss it — the choice is yours." This is honest about the ghost's combat presence and frames the party decision as agency, not helplessness.
Apply to: CR-GH-9 (forfeit policy split), CR-GH-9.1 (ghost death forfeit: post-disconnect XP only), CR-GH-9.2 (reconnect: all rewards retained), CR-GH-10 (add voluntary dismissal step), Player Fantasy section, new AC for voluntary dismissal.
