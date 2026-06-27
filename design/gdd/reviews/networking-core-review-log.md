# Review Log: Networking Core

---

## Review — 2026-05-14 — Verdict: APPROVED (Pass 2 lean — revision applied in-session)
Scope signal: S
Specialists: None (lean mode)
Blocking items: 1 (fixed in-session) | Recommended: 6 (all applied in-session)
Summary: All 14 Pass 1 blockers confirmed closed. One new logic error introduced in the Pass 1 CR-NET-5.6 expansion: step 2 validation condition "character has heldFreePoints > 0" was logically inverted — respec is used precisely when heldFreePoints = 0 (all allocated), so this condition would have rejected every valid respec request. Fixed to "character has at least one allocated stat point (total free points > heldFreePoints)." Six recommended updates applied: document header updated, double separator removed, AC-NC-04/05 citations updated to reference INetworkTestObserver.OnTickCompleted, sub-documents table expanded to include 3 primitive specs, Dependencies section updated to list primitive specs as upstream contracts.
Prior verdict resolved: Yes — all 14 Pass 1 MAJOR REVISION NEEDED blockers confirmed closed; 1 new S-scope blocker found and fixed.

---

## Review — 2026-05-13 — Verdict: MAJOR REVISION NEEDED (Pass 1 — trimmed post-split, first independent review)
Scope signal: L
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, creative-director (synthesis)
Blocking items: 14 across 6 clusters (Cluster A 3 fixed in-session) | Recommended: 4
Summary: First independent review of the trimmed networking-core.md after the Pass 7 document split. Three clusters are architecturally critical: (A) Authority Inversion — CR-NET-3 channel table contradicted approved children (PartyMemberHealthUpdate U-U vs R-U Forbidden Pattern; SelfDamageEvent absent; GoldSyncEvent forced-delivery missing). Fixed in-session by replacing the per-message table with a cross-reference to networking-channel-contract.md and correcting the Interactions #5 entry. (B) Pillar 2 gap — F-NET-OWL wrap correction is structurally broken at design-target 100–150ms RTT because BeatResolvedThisTick is cleared at tick start; RPCs at 50ms OWL overwhelmingly arrive in the next tick after the Beat, making the correction ineffective. CD recommendation: extract networking-owl-compensation.md as a standalone primitive before revising CR-NET-8. (C–F) AC harness gaps (tick counter, BeatResolvedThisTick injection), respec commit path underspecified (no step-by-step sequence, no ACs), mob entities absent from tick budget, NotifySkillUsed unprotected from rate-based DoS. CD verdict: architecture is sound; do not re-split. Staged fix: Cluster A done; extract OWL primitive; batch Clusters C–F in one revision pass.
Prior verdict resolved: N/A — first review of post-split trimmed document (prior log entries cover the unified pre-split document through Pass 7).

---

## Review — 2026-05-11 — Verdict: MAJOR REVISION NEEDED (Pass 8 — networking-session.md)
Scope signal: XL (foundational protocol primitives undefined; pillar-critical ghost session behavior underspecified; 8 structural clusters; 24+ blockers)
Specialists: network-programmer, systems-designer, qa-lead, creative-director
Blocking items: 32 (24+ blocking, ~13 recommended) | Recommended: 13
Summary: Pass 8 is the first review of the isolated networking-session.md sub-document following the Pass 7 document split. Three specialists independently converged on the same upstream gap: core protocol primitives are referenced throughout but never defined. The 8 blocker clusters are: (A) Session token undefined — no type, length, generation, rotation, or replay-attack protection; (B) ST-NET-1/ST-NET-2 transition table holes — REAUTH_FAILURE_LIMIT path, Reconnecting→DSExpired on session-stealing, Draining→Active on re-auth failure, explicit-disconnect zone lifecycle all missing; (C) Ordering and race conditions at state boundaries — ghost-kill vs. TTL-expiry write ordering, death-during-final-Beat undefined, no canonical Disconnected_SessionExpired resource-release sequence; (D) Unbounded failure paths — Connecting has no timeout, dual-gate has no max retransmit, zone crash orphans ghost sessions; (E) Tuning knob coherence — GHOST_COMBAT_TTL can exceed SESSION_TTL at stated safe-range bounds; (F) F-NET-4 label backwards (maximum labeled as minimum) and missing status effects; (G) AC layer mechanically untestable — session-stealing/REAUTH_FAILURE_LIMIT/ghost forfeit all have no ACs, IZoneTestConfigurator missing, INetworkTestObserver missing 8 methods; (H) Ghost session behavior underspecified — forfeit guarantees, isGhost broadcast semantics, mob de-targeting all pillar-critical. Creative-director verdict: do NOT run Pass 9 as another revision sprint; extract three foundational sub-specs first (session token, formal state machine table, networking-ghost-session.md) then revise networking-session.md against those contracts.
Prior verdict resolved: First review of this sub-document (parent networking-core.md Passes 1–7 were all MAJOR REVISION NEEDED)

---

## Document Split — 2026-05-07 — Pass 7 Revision Applied (Document Split + All 22 Blockers Resolved)
Action: Unified networking-core.md split into 4 sub-documents. All 22 Pass 7 blockers applied during migration. 3 user design decisions implemented.
Files created: networking-session.md, networking-wire-protocol.md, networking-test-harness.md
Files modified: networking-core.md (trimmed), systems-index.md (added 26a–26c), entities.yaml (StatID added)
Design decisions applied:
  - GD-1: Ghost-period rewards → FORFEIT. No XP or loot credited during Disconnected_SessionActive.
  - NEW-SD-1/PA-P7-06: Damage event "never dropped" guarantee → RETRACTED. CR-NET-3 updated to best-effort.
  - GD-2: OWL hysteresis band ±15ms added to CR-NET-8.3. Entry threshold: 135ms; exit threshold: 105ms.
All 22 blockers resolved:
  B-NP-1 (F-NET-7 annotation), B-NP-2/NP-NEW-1 (zone join/leave removed from CR-NET-2 and R-U drop list),
  B-NP-3 (uint16 length prefix framing), B-NP-4 (ConnectionQualityUpdate → R-U batch),
  B-NP-5 (_cycleTimer annotation in CR-NET-8.2), B-NP-6 (enhancement tie-break rule),
  B-NP-7 (session-stealing policy), B-NP-8 (dual-gate dual-event),
  B-SD-2 (StatID added to entities.yaml), B-SD-3 (GoldSyncEvent demoted, dropped first),
  B-SD-4 (F-NET-4 size corrected), B-QA-3/SYSTEMIC (interfaces defined, stripping specified),
  B-QA-6 (AC-NC-36 added), NP-NEW-2 (Reconnecting→Disconnected_SessionExpired TTL transition),
  NP-NEW-5 (_skillUsedThisCycle reset on ghost entry), PA-P7-04 (GoldSyncEvent guarantee retracted),
  PA-P7-05 (position update gap acknowledged), PA-P7-13 (zone join/leave dead code confirmed removed),
  GAP-1 (heartbeat schema defined), GD-1 (forfeit policy), GD-2 (hysteresis band), NEW-SD-1 (damage best-effort)
Pass 8 readiness: Document split complete. Pass 8 must review each sub-document independently with the specialist panels defined in session state.

---

## Review — 2026-05-07 — Verdict: MAJOR REVISION NEEDED (Pass 7)
Scope signal: XL (third-tier structural collapse; cross-cutting wire-format, session state machine, pillar contracts, test harness; document split mandated)
Specialists: network-programmer, systems-designer, qa-lead, game-designer (first since Pass 5), performance-analyst (first since Pass 5), creative-director
Blocking items: 22 (13 carry-over from Pass 6 + 9 new) | Recommended: 6
Summary: Pass 7 confirmed all 13 of 14 Pass 6 blockers remain unresolved and uncovered 9 new blockers across all five specialist domains: the Reconnecting→Disconnected_SessionExpired TTL transition is absent from the ST-NET-1 state machine; "damage events are never dropped" is structurally contradicted by peak-density bandwidth math; position updates are never delivered at n=50 due to U-U batch saturation; the heartbeat message is entirely undefined; ghost-period reward policy is a Pillar 1 violation; OWL threshold lacks a hysteresis band. Three design decisions were made by the user: ghost rewards → forfeit (Option A); damage guarantee → retract, best-effort R-U; OWL hysteresis → add ±15ms band. Creative-director diagnosed "third-tier structural collapse" and mandated a document split into four sub-documents (networking-core, networking-session, networking-wire-protocol, networking-test-harness) before Pass 8. Pass 8 must not begin with a fix sprint on the unified document.
Prior verdict resolved: No — 13 of 14 Pass 6 blockers unresolved; 9 new blockers found in Pass 7; 3 design decisions made

---

## Review — 2026-05-07 — Verdict: MAJOR REVISION NEEDED (Pass 6)
Scope signal: XL (cross-cutting infrastructure; wire-format contract gaps; security/session model gaps; QA test infrastructure gaps; 2 of 5 specialists absent due to rate limits)
Specialists: network-programmer, systems-designer, qa-lead (game-designer and performance-analyst unavailable — rate limits)
Blocking items: 14 | Recommended: 13
Summary: Pass 6 uncovered 14 blockers, 6 of which are NEW structural gaps not touched by any prior pass: the R-U batch sub-message deserializer framing contract is undefined (no length prefix, no documented static-length-table); ConnectionQualityUpdate is absent from the canonical R-U batch message list despite being described as "batch path"; the session-stealing / concurrent-connection policy is entirely absent from ST-NET-1; the GoldSyncEvent serialization order and overflow drop priority are logically inverted; StatID is missing from entities.yaml despite the cross-doc correction being applied to both GDDs; and the QA test infrastructure has three systemic gaps (ITransportFaultInjector undefined, release-build stripping mechanism unspecified, 8-message cap has no AC). The creative-director diagnosed "second-tier patch decay" and recommended a structured Phase A fix sprint in a fresh session, sequencing R-U batch framing first before touching any dependent channel-placement or drop-priority fixes. Document split into networking-core / networking-session / networking-test-harness was suggested if Pass 7 surfaces another tier of structural blockers.
Prior verdict resolved: Yes — all 27 Pass 5 blockers resolved; 14 new blockers found in Pass 6

---

## Review — 2026-05-06 — Verdict: MAJOR REVISION NEEDED (Pass 5) — Revision Complete
Scope signal: XL (cross-cutting infrastructure; channel-assignment structural repair; density model rebuild; ghost lifecycle; 5+ specialist domains)
Specialists: network-programmer, systems-designer, qa-lead, performance-analyst, game-designer, creative-director
Blocking items: 27 | Recommended: 4
Summary: Pass 5 diagnosed "patch-decay mode" — the document was diverging rather than converging, with Pass 5 finding 27 unique blockers across all five specialist domains. The creative-director called for structural intervention before spot-fixes. Phase A fixed the canonical channel-assignment table (zone join/leave → R-OD; EntityHealthUpdate → R-U with CR-NET-3 as authoritative source), documented the IsTickExpired/IsNewerVersion equality-case asymmetry with cross-references, and rebuilt the bandwidth model with explicit density assumptions and a two-tier OQ-NC-SER-3 simulation requirement (baseline + 4× peak density). Phase B addressed ghost lifecycle: ghost mid-session transitions documented as an intentional MVP gap (isGhost snapshot-only), ghost entity death triggers KillEvent broadcast, ghost sessions during zone forced-close immediately enter Disconnected_SessionExpired, OWL probe interval tightened from 30s to 10s (Pillar 2 protection). Phase C closed wire protocol gaps (enum defaults, MessageTypeID ADR delegation, fragment worst-case, dual-gate conjunction rule) and fixed five AC testability issues (IClientTestObserver, HEARTBEAT test value, zone entry point precondition, AC-NC-35(c) render-frame assertion). All 27 blockers resolved; Pass 6 review recommended in a fresh session.
Prior verdict resolved: Yes — all 27 Pass 4 blockers resolved; 27 new blockers found in Pass 5 (now also resolved)

---

## Review — 2026-05-06 — Verdict: MAJOR REVISION NEEDED (Pass 4)
Scope signal: XL (cross-cutting infrastructure; 8+ downstream dependents; multiple ADRs required; pillar violations in Pillars 1, 2, 3, 4; bandwidth model rebuild required)
Specialists: network-programmer, systems-designer, qa-lead, performance-analyst, game-designer, creative-director
Blocking items: 27 | Recommended: 12
Summary: Pass 4 surfaced 27 genuine blockers across five domains. The most critical cluster: the Irreversible Commit Pipeline has documented trust-model holes — priority-path cap can defer or lose committed enhancement outcomes, LastEnhancementRequestID resets on TTL expiry creating a dedup bypass, and the batch R-U/U-U reliability model is architecturally contradictory. F-NET-1/F-NET-2 bandwidth claims are mathematically invalid above ~14 players (1,764 bytes baseline vs. 512-byte cap). Four pillar violations identified: ghost entity indistinguishable from connected player (Pillar 3 + Pillar 1 asymmetry), Rhythm Mastery completely unavailable above OWL threshold with no fallback (Pillar 2), and 150ms non-combat input dead zone undocumented (all pillars). Wire protocol has six undefined types (EnhancementAttemptRequest schema, ZoneID, PartyID, DamageType, DisconnectReason, DisconnectType). Six ACs fail the Logic/Integration gate (server debug output not assertable, TTL-based ACs not CI-parametric, simultaneity not unit-testable). Creative-director recommends beginning Pass 5 with an Irreversible Commit Protocol subsystem design before any blocker-by-blocker edits.
Prior verdict resolved: Yes — all 22 Pass 3 blockers resolved; 27 new blockers found in Pass 4

---

## Revision — 2026-05-06 — Pass 3 blockers resolved (full — all 22)
All 22 Pass 3 blockers resolved across three domain sessions: (Encoding) Quaternion pre-encode + zero-guard; Vector3 overflow guard + Zone Instancing confirmation required; GoldTransactionReason enum declared (7 values + Other=255, entities.yaml updated); cycleTimer encoding changed from absolute ×1,000 to normalized ÷CycleDuration ×10,000 (rippled to CycleTimerBroadcast, EntityState); unit direction vector encoding category added to CR-NET-7.2; priority-path cap 8 msgs/client/tick added (deferred not dropped); ArrayPool exhaustion policy: defer flush, log anomaly, no data loss; primitive serializer non-generics requirement in CR-NET-7.8; AC-NC-28 fixed-point round-trip. (Timing) CR-NET-8.2 timer-wrap fix via BeatResolvedThisTick modular arithmetic; KillEvent augmented with finalDamage/isCrit/damageType to eliminate R-OD/R-U cross-path race; OQ-NC-SER-3 promoted to BLOCKING (64 sub-message ceiling removed, batch is size-capped not count-capped); ConnectionQualityUpdate event added for Pillar 2 high-OWL UI indicator; AC-NC-29/30/31. (Ghost Combat) Enhancement dedup time window removed — persistent LastEnhancementRequestID check, no time limit; FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS=5s + ZoneSnapshotRequest retransmit; GHOST_COMBAT_TTL_MINUTES default 5→1 min to limit farming dominant strategy; EC-NET-1 disconnectTickNumber:uint mandated, DateTime.UtcNow forbidden; AC-NC-32/33/34/35. ENHANCEMENT_DEDUP_WINDOW_SECONDS tuning knob removed; FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS added.
Status: Revised (Pass 3 — Full) — Pending Re-Review (Pass 4)

---

## Review — 2026-05-02 — Verdict: MAJOR REVISION NEEDED
Scope signal: XL (cross-cutting infrastructure; 8+ downstream dependents; multiple new ADRs required)
Specialists: game-designer, systems-designer, network-programmer, qa-lead, creative-director
Blocking items: 14 | Recommended: 4
Summary: The GDD's core authority model and tick design were sound, but the spec had structural failures across serialization, the delivery model, and latency handling. The most critical issue was that the grace window mechanic would have broken Pillar 2 (Rhythm Mastery) for all mobile players on typical LTE connections (100ms RTT), requiring a new CR-NET-8 RTT-adaptive compensation rule. A batch/channel contradiction in the two-path model required a complete rewrite of CR-NET-7.7. The `uint` wraparound comparison bug in EC-NET-6/EC-NET-8 would have caused permanent message discard after 50 days of uptime. The creative-director noted the GDD's Player Fantasy section was the strongest element and worth preserving intact.
Prior verdict resolved: First review

---

## Revision — 2026-05-05 — Pass 2 blockers resolved
16 blockers applied in one batch: (1–6) Float rule violations fixed in 6 schemas — CycleTimerBroadcast, EntityState, AutoFaceEvent, StatSnapshotEvent, PlayerJoinedZone, SessionReady all converted to fixed-point integer encoding per CR-NET-7.2; cycleTimer→ushort(×1,000), Vector3→3×short(×100), Quaternion→4×short(×32,767). (7) CR-NET-3 kill event split from damage events: damage=R-U, kill=R-OD matching Hit Detection section and CR-NET-7.7. (8) EntityState byte size corrected: 84→68 bytes (after float→fixed conversions). (9) Fragmentation policy added: ZoneStateSnapshot at 3.4KB requires FragmentIndex+TotalFragments chunking; reassembly gate before zone render. (10) CR-NET-8.2 formula: clamp adjustedCycleTimer to max(…,0). (11) CR-NET-8.3 rewritten: clamp at 0.80×CycleDuration removed (was always below 0.92 grace threshold, preventing skill fires); replaced with no-compensation pass-through for OWL above cap. (12) EC-NET-1: ghost-death-no-penalty rule added (Pillar 1 constraint imposed on Death & Respawn GDD). (13) EC-NET-1: GHOST_COMBAT_TTL_MINUTES limit added (ghost entity disengages after 5 min default). (14) EC-NET-9: LastEnhancementRequestID must be persisted with atomic enhancement write, not held in session memory. (15–16) Tuning knobs: GHOST_COMBAT_TTL_MINUTES added; MAX_COMPENSATABLE_OWL_MS description updated to match fixed CR-NET-8.3.
Status: Revised (Pass 2) — Pending Re-Review (Pass 3)

---

## Review — 2026-05-04 — Verdict: MAJOR REVISION NEEDED
Scope signal: XL (cross-cutting infrastructure; 16 blockers; pillar-level design gaps introduced in pass 2)
Specialists: network-programmer, systems-designer, game-designer, qa-lead, performance-analyst, unity-specialist, creative-director
Blocking items: 16 | Recommended: 3
Summary: Pass 2 resolved all 14 pass-1 blockers and added valuable new rules (RFC 1982 wraparound, two-path delivery model, IL2CPP hot-path constraints, CR-NET-8 RTT-adaptive grace window), but introduced new contradictions in the same pass. The float rule defined in CR-NET-7.2 is violated in 6 message schemas (CycleTimerBroadcast, EntityState, AutoFaceEvent, StatSnapshotEvent) in the same document. The kill event has three contradicting channel assignments across CR-NET-3, the Hit Detection section, and CR-NET-7.7. EntityState byte-size arithmetic is wrong (84 bytes actual vs 80 stated), and the fragmentation protocol for 50-entity snapshots (4.2KB) is absent. Four pillar-level violations were found: ghost entity death breaks Pillar 1 (Earned Power), ghost farming is unaddressed, RTT compensation inversion degrades Pillar 2 (Rhythm Mastery) at higher attack speeds, and enhancement idempotency gap threatens Pillar 4 (Legendary Gear). The creative-director observed that the spec is architecturally ambitious and directionally correct, but cannot be handed to programmers in current state — the contradictions would generate implementation debt that propagates into all 15 dependent systems.
Prior verdict resolved: No — 14 pass-1 blockers resolved, 16 new blockers introduced
