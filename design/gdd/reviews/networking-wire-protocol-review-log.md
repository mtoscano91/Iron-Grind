# Review Log — Networking Wire Protocol

---

## Review — 2026-05-11 — Verdict: MAJOR REVISION NEEDED (Pass 1)
Scope signal: XL
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst + creative-director synthesis
Blocking items: 4 clusters (C1–C4) | Recommended: 5
Summary: First formal review (document was self-assigned "Pass 1" status during the networking-core.md split with no prior /design-review run). Root causes: C1 — no upstream Message Criticality Contract (channel assignments ad-hoc; PartyMemberHealthUpdate in wrong channel; player-self DamageEvent must be R-OD for Pillar 2 feedback); C2 — no Direction/Channel Contract and SessionHandshake name collision (server-emitted state seed must be renamed SessionStateSnapshot); C3 — Scenario C position count wrong when party present, F-NET-6 pre-drop count understated by ~22%, fragment count must be dynamic; C4 — 9 rules with zero AC coverage, AC-NC-07 missing wraparound test, AC-NC-17 vacuous, AC-NC-30(b) discard layer unspecified. Recommended path: extract networking-message-criticality.md, networking-channel-contract.md, networking-relevance-filter.md before revising this document.
Prior verdict resolved: N/A — first review.

---

## Review — 2026-05-12 — Verdict: NEEDS REVISION (Pass 2)
Scope signal: XL (document scope); S (remaining revision work)
Specialists: lean — no specialist agents
Blocking items: 1 | Recommended: 2
Summary: All four Pass 1 blocker clusters (C1–C4) substantively closed. One arithmetic error survived C3: F-NET-6 peak row post-drop count was ~4,150 (incorrect) — correct value is ~5,650; pre-drop per-client was 148 (should be 147). AC-NC-21 referenced the pre-filter Scenario C figure (~29.6 KB/s) instead of the post-filter value (~26.0 KB/s). EnhancementOutcomeBroadcast was referenced in CR-NET-7.7 and AC-NC-35 but had no schema stub, making AC-NC-35 non-testable. All three items fixed in-session. Document is ready for Pass 3 lean re-review.
Prior verdict resolved: Yes — 4 Pass 1 blocking clusters closed.

---

## Review — 2026-05-12 — Verdict: NEEDS REVISION (Pass 3)
Scope signal: XL (document); S (remaining work)
Specialists: lean — no specialist agents
Blocking items: 1 | Recommended: 2
Summary: All Pass 2 fixes verified correct. New finding: MAX_MESSAGE_BODY_BYTES safe range lower bound stated as 192 but CycleBroadcast at default MAX_PLAYERS_PER_ZONE=50 is 502 bytes — any value below 502 drops CTBs every tick, directly violating the "CycleTimerBroadcast is never dropped" Pillar 2 invariant. Fixed to [502, 1,400] with cross-knob formula. EnhancementOutcomeBroadcast stub text corrected from "attacker's own client" to zone-wide semantics. AC-NC-36 precondition note added for test harness consistency. All three items fixed in-session.
Prior verdict resolved: Yes — all Pass 2 items closed.

---

## Review — 2026-05-12 — Verdict: APPROVED (Pass 4)
Scope signal: XL
Specialists: lean — no specialist agents
Blocking items: 0 | Recommended: 2
Summary: Full document sweep found zero blocking items. All formulas arithmetically verified. All 15 ACs are independently testable. All prior pass blockers confirmed closed. Pillar alignment coherent across all 4 game pillars. Two advisory ACs recommended (FloorToInt HP serialization, buffer-pool exhaustion) and one nice-to-have EC; none block implementation. Networking ADR (OQ-NC-SER-1–4) must be authored before implementation begins — that gate is external to this GDD.
Prior verdict resolved: Yes — all Pass 3 items closed.

---

## Review — 2026-10-09 — Verdict: NEEDS REVISION (TD-046 amendment, lean)
Scope signal: XL (document); S (revision)
Specialists: lean — no specialist agents
Blocking items: 2 | Recommended: 6
Summary: Lean re-review of the TD-046 amendment (Enhancement message set, `InventorySlotUpdate`, `InventoryFullSync`, `EnhancementResultCode`, AC-NC-35, AC-NC-40 to AC-NC-44) with its MCR-2 and CCR-3 rows. Sizes, fixed-point encoding, enum order, channel and cap-exemption rows are consistent across the wire protocol, networking-core.md, the two contract documents and enhancement-system.md. Blocking: (1) a duplicate `EnhancementAttemptRequest` has no defined wire response — the schema cites both CR-NET-5.2 (`LastEnhancementRequestID`, "emits a rejection") and ADR-001 A1 (cached result), `EnhancementResultCode` 0–9 has no duplicate code, and the result is "never re-sent"; user decision needed; (2) `InventoryFullSync` is outside the CR-ENH-11 hold, so a client that reconnects while the step 6b commit is in flight (EC-ENH-1) receives the uncommitted bag (CR-NET-5.1, CR-NET-5.4). Recommended: (3) acknowledgment followed by a step 4 `RejectedScrollNotFound` contradicts "sent instead of `EnhancementRequestReceived`" (CCR-3), and the hold has no stated end on that path; (4) CR-NET-7.3 / AC-NC-31 (ID 0 never written, serializer throws) against the nullable `itemId` of the two inventory messages and AC-NC-41; (5) AC-NC-41's truncation case does not fix `playerName` or single-byte characters, and has no multi-byte case; (6) no AC for the `EnhancementRequestReceived` cap exemption, the non-exempt `ServerBroadcast_Enhancement9`, the single response to `EnhancementPreviewRequest`, or the missing response to `CancelEnhancement`; (7) item-database.md puts no length bound on `DisplayName` (24-byte truncation in the +9 broadcast); (8) no full equipment sync to the owning client on zone entry, and no open question records it.
Prior verdict resolved: n/a — prior verdict was APPROVED (Pass 4); this pass reviews new content only.

## Review — 2026-10-09 — Verdict: APPROVED (TD-046 revision, lean verify)
Scope signal: XL (document); S (revision)
Specialists: lean — no specialist agents
Blocking items: 0 | Recommended: 2
Summary: Lean verify of the revision that followed the entry above. All eight items are closed: (1) a duplicate `EnhancementAttemptRequest` is dropped with no response and one `DuplicateEnhancementRequest` anomaly, stated the same way in the schema note, networking-core.md CR-NET-5.2 / CR-NET-5.3 step 2, CCR-3, networking-session.md EC-NET-9 / AC-NC-27 and AC-NC-49; (2) the CR-ENH-11 hold covers `InventoryFullSync` (message note, CCR-3, enhancement-system.md CR-ENH-11, entities.yaml, AC-NC-43 reconnect case); (3) the acknowledgment follows CR-ENH-15 step 4 and the hold ends on a step 4 failure; (4) CR-NET-7.3 nullable-ID exception, AC-NC-31 scoped; (5) AC-NC-41 cases give 4 / 52 / 52 / 50 bytes; (6) AC-NC-35 (d), AC-NC-47, AC-NC-48; (7) the 24-byte `itemName` cut is accepted and documented; (8) OQ-NC-SER-5 recorded. Recommended, for an authoring session: (R1) when the client reconnects while the step 6b commit is in flight and the commit succeeds, the document does not say whether `EnhancementAttemptResult` goes to the new connection — the `InventorySlotUpdate` note says "together with `EnhancementAttemptResult`", the result's note and EC-ENH-1 say "never re-sent", the AC-NC-43 reconnect case asserts nothing; user decision 2026-10-09: not sent, the client reads the outcome from the deferred `InventoryFullSync` (one sentence and one assertion to write); (R2) `MoveResult.toSlotItemId` has no nullable declaration in its schema comment, so under the new CR-NET-7.3 rule the serializer would throw on a failed move whose destination slot is empty. Dependency files were not re-globbed in full; every document the revision cites exists.
Prior verdict resolved: Yes — both blocking items and the six recommended items of the entry above.
