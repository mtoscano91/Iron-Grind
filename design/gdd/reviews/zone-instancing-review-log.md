# Zone Instancing — Design Review Log

## Review — 2026-10-23 (lean, `ZoneEntryAck` revision `a2872f4`) — Verdict: NEEDS REVISION (fixed same session; lean verify pending)
Scope signal: M (this revision; GDD overall XL)
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 | Recommended: 4
Summary: The ack design closed readiness gaps 1–4 (schema consistent across wire protocol, MCR-2, CCR-3, registry; knob conflict resolved; acked duplicate = session stealing). Blocker: reclamation left the session layer undefined — connection fate after case (a), session state and token after (a)/(c), client handling of a late `ZoneFullResponse(Other)` after the gate opened. Fixed (user decisions): (a) closes the connection; all cases `Connected → Disconnected_SessionExpired`, no write, token deleted (fresh connect); client tears down the zone view; propagated to networking-session.md ST-NET-1 (new row), networking-session-token.md CR-TOK-6, networking-wire-protocol.md `ZoneFullResponse` (fourth case). Recommended fixed: reclamation is the fourth exit cause (CR-ZI-14, Party row, AC-ZI-13); case (b) same instance only, reclaims at token validation before `LoadCharacter` (AC-ZI-28 both outcomes); teardown skips the save of an entering slot (CR-ZI-12 step 2, EC-ZI-15, AC-ZI-31). Nice-to-have fixed: CR-ZI-8 intro step 11; AC-ZI-30 heartbeat variant.
Prior verdict resolved: Yes — NEEDS REVISION (2026-10-22 lean #2, fixed and marked Approved)

## Review — 2026-10-22 (lean #2) — Verdict: NEEDS REVISION (fixed same session; marked Approved by user)
Scope signal: XL
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 | Recommended: 3
Summary: All four items from the first 2026-10-22 pass verified closed. Blocker: CR-ZI-12 step 3(a), EC-ZI-3 and the Interactions/Dependencies rows still cached the `ZoneID` so a ghost expired by teardown would get `ZoneSessionEnded(GhostDeath=3)` on reconnect — contradicting step 3 (not a death; CR-GH-6 is death-only) and impossible across the process exit (ADR-007/009). Fixed: step 3 saves from the CGS-3 snapshot, expires the session and invalidates the token; reconnect takes the EC-TOK-1 auth failure → fresh login → CR-ZI-6 rule 2 fall-through. Recommended fixed: step 4 stale ordering note and unclear notification wording; step 5 ordering covers step 2 and step 3 saves; `MAX_MOBS_PER_ZONE` 500 knob row 44 → 43 fragments. Nice-to-have fixed: CSP status, `JoinRejectedReason.Other`, AC-ZI-22/23 precision. `entities.yaml` `DisconnectReason` note updated.
Prior verdict resolved: Yes — NEEDS REVISION (2026-10-22 lean #1)

## Review — 2026-10-22 — Verdict: NEEDS REVISION (fixed same session)
Scope signal: XL
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 | Recommended: 3
Summary: Lean re-review of the lifecycle reconciliation (`6153908`): CR-ZI-2, T-1..T-7, ST-NET-2 occupancy table, knobs and registry consistent; `ZONE_CLOSE_GRACE_PERIOD_SECONDS` fully retired. Blocker: a ghost slot was saved twice at teardown with conflicting HP — CR-ZI-12 step 2 `SaveSession(ZoneClosed)` (live HP) plus step 3 "normal TTL expiry" (CR-NET-6.5 write), against CGS-3 (ghost-period damage never persisted). Fixed (user decision): step 2 skips ghosts; step 3 applies the CGS-3 snapshot as the ghost's only save, no `GhostExpiredEvent` / `PlayerLeftZone`; propagated to networking-session.md, networking-ghost-character-state.md (CGS-4 note), character-persistence.md (save-trigger row). Recommended items fixed: T-5 fires on the T-4 tick when no slot is occupied; CR-ZI-8 step 4 requires Active (a load completing after T-4 gets `ZoneDoesNotExist`); T-6 from Loading clears `loadingInProgress`; AC-ZI-23 revised, AC-ZI-25..27 added.
Prior verdict resolved: Yes — APPROVED 2026-05-30 (this pass reviews the 2026-10-22 revision)

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
