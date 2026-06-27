# Review Log: Networking OWL Compensation

## Review — 2026-05-18 — Verdict: NEEDS REVISION
Scope signal: M
Specialists: network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, creative-director
Blocking items: 7 | Recommended: 3
Summary: Seven blockers across correctness, safety, and testability. Most critical: the wrapCorrectionWindow formula lacks an explicit sentinel guard — at server ticks 0–1 and for freshly recycled mob slots, `0 - uint.MaxValue = 1 ≤ 2` in uint arithmetic produces a false-positive without the guard. Free-list mob slots are never sanitized (no reset-to-sentinel rule), allowing stale tick values to persist into new occupants. CR-OWL-3's coverage proof assumes fixed-step tick advancement without stating it, and hardcodes the bound "2" without referencing the registry constants it derives from. AC gaps: slot allocation policy (player stability, mob reset, ghost preservation) and CI constraint guard for the MAX_WRAP_WINDOW_TICKS bound have no ACs. AC-OWL-01 and AC-OWL-02 identify values but not which code branches are exercised.
Prior verdict resolved: No — first review

## Review — 2026-05-18 — Verdict: NEEDS REVISION (lean re-review, Pass 1 verify)
Scope signal: S
Specialists: None (lean mode)
Blocking items: 1 | Recommended: 2
Summary: All 7 original blockers confirmed closed. One new blocker introduced during Pass 1 revision: CR-OWL-1 reconnect sentence ("restored from uint.MaxValue on reconnect") directly contradicts EC-OWL-2 (ghost-session slot preserved with current tick value). Implementation hazard — a programmer could implement slot reset on reconnect. Fix is a single sentence rewrite with no design decision required. Two recommended items: CR-OWL-5 T+1 boundary not explicitly covered in adversarial analysis (proof is sound but incomplete); F-OWL-1 "auto-attack fires" comment may mislead implementers.
Prior verdict resolved: Yes — 7/7 Pass 1 blockers closed

## Authoring Pass 2 — 2026-05-18
Blockers addressed: 1/1 | Recommended: 0 addressed (deferred)
Summary: Rewrote CR-OWL-1 reconnect sentence. Removed "restored from uint.MaxValue on reconnect." Explicitly states ghost-session reconnect does NOT reset the slot (retains current tick value). Clarifies post-TTL/zone-close slot deallocation path (new slot naturally starts at uint.MaxValue by array initialization). Subject clarified from ambiguous to explicit: "LastBeatServerTick is never persisted."
Ready for: lean re-review (Pass 2 verify)

## Review — 2026-05-18 — Verdict: APPROVED (lean re-review, Pass 2 verify)
Scope signal: M
Specialists: None (lean mode)
Blocking items: 0 | Recommended: 2
Summary: Pass 2 blocker confirmed closed — CR-OWL-1 reconnect clause now states explicitly that ghost-session reconnect does NOT reset the slot and that LastBeatServerTick retains its current tick value, matching EC-OWL-2 and AC-OWL-06a exactly. No new issues introduced. Two deferred advisory items remain (CR-OWL-5 T+1 boundary not explicitly spelled out in adversarial analysis; F-OWL-1 "auto-attack fires" comment is technically correct but slightly ambiguous). Neither affects correctness or implementability. Document is internally consistent and ready for implementation.
Prior verdict resolved: Yes — 1/1 Pass 2 blocker closed

## Authoring Pass 1 — 2026-05-18
Blockers addressed: 7/7 | Recommended: 0 addressed (deferred)
Summary: Added explicit `LastBeatServerTick[slot] != uint.MaxValue` sentinel guard to CR-OWL-1, CR-OWL-2, and F-OWL-1 with unsigned arithmetic documentation note. Added CR-OWL-4 sanitization invariant (reset to uint.MaxValue on free-list return). Updated CR-OWL-3 with tick-interval determinism assumption, coverage floor (guaranteed ≤75ms OWL; degrades 76–120ms), and re-derivation requirement when registry constants change. Updated Tuning Knobs safe range to reference derived expression. Rewrote EC-OWL-3 to reflect that explicit guard (not uint wraparound) prevents startup false-positive. Updated AC-OWL-01/02 with branch identification; added AC-OWL-03 startup sub-cases; added AC-OWL-06a (player slot stability), AC-OWL-06b (mob slot reset), AC-OWL-06c (ghost slot preservation), AC-OWL-07 (CI constraint guard). Registered MAX_COMPENSATABLE_OWL_MS, MAX_PLAYERS_PER_ZONE, MAX_WRAP_WINDOW_TICKS, MAX_MOBS_PER_ZONE in entities.yaml; added networking-owl-compensation.md to TICK_RATE_HZ referenced_by.
Ready for: lean re-review
