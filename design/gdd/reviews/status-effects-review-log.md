# Status Effects / Buffs — Review Log

## Review — 2026-05-20 (lean re-review) — Verdict: APPROVED

Scope signal: L
Specialists: lean mode (single-session analysis)
Blocking items: 3 found and resolved inline | Recommended: 2
Prior verdict resolved: Yes — MAJOR REVISION NEEDED (14 blockers, 2026-05-20 first pass)

Summary: All 14 first-pass blockers confirmed resolved. Three new blockers found and fixed in this session: (1) CharacterStats transaction API (`BeginStatTransaction`/`EndStatTransaction`) was restricted to Leveling System only — extended to also permit Status Effects as a caller for tick-path buff flush (CR-SE-17); (2) CharacterStats mob modifier rule text still said "no equipment or buff layer" for mobs, contradicting EC-21 and CR-SE-12 — rule text corrected; (3) four occurrences of stale `modifiers.Length` terminology remained in EC-SE-6, AC-SE-04, AC-SE-06, and AC-SE-10 — all replaced with `modifierCount`. Two recommended items noted (AC-SE-24 test hook, AC-SE-15 OQ-SE-1 annotation) but not blocking.

---

## Review — 2026-05-20 — Verdict: MAJOR REVISION NEEDED → All Blockers Resolved

Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, network-programmer, performance-analyst, unity-specialist, creative-director (senior)
Blocking items: 14 | Recommended: 3
Prior verdict resolved: No — first review

Summary: The GDD's mechanical core (tick-based expiry, refresh semantics, deferred cleanup, execution order) is sound. Two structural issues drove the MAJOR REVISION NEEDED verdict: (1) the player fantasy described a reactive save moment but the mechanics only delivered proactive regen — resolved by adding `ApplyInstantHeal()` as a second public entry point; (2) the `BuffDefinition` struct used a `StatModifier[]` reference-type field, which is not immutable under IL2CPP and produces managed heap allocation — resolved by switching to 8 inline value fields plus a `modifierCount` byte. All 14 blockers were addressed in the same session as the review. Key additional fixes: 1D array layout specified (`ActiveEffect[1600]`), OnStatChanged batching contract added to CR-SE-17, wire protocol data contract declared (BuffID/TicksRemaining/CasterEntityID), caster attribution added to `ActiveEffect` snapshot, zone departure sequence contract added, 5 new ACs written (AC-SE-27–31), AC-SE-19 and AC-SE-25 rewritten for testability.

**Recommended next step:** `/design-review --depth lean design/gdd/status-effects.md` in a new session after `/clear` to verify all blockers are closed without structural regression.
