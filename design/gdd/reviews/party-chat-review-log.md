# Party Chat — Review Log

---

## Review — 2026-06-19 — Verdict: APPROVED
Scope signal: M
Specialists: none (lean)
Blocking items: 1 | Recommended: 4
Summary: All three structural blockers from review #1 were resolved in the prior authoring session. Lean re-review found one intra-document inconsistency: the Dependencies upstream table still referenced `GetPartyMembers` (old method) instead of `GetPartyMembersWithStatus`. Fixed in-session. Wire protocol amendments (OQ-PC-1) remain a pre-implementation gate, correctly flagged in the GDD. UX spec (`design/ux/party-chat-panel.md`) does not exist yet — correctly flagged as a pre-implementation gate. All 13 ACs are independently testable; all formulas produce correct boundary values.
Prior verdict resolved: Yes

---

## Review — 2026-06-19 — Verdict: MAJOR REVISION NEEDED

Scope signal: M
Specialists: network-programmer, systems-designer, qa-lead, game-designer, ux-designer, creative-director
Blocking items: 20 (collapsed to 3 structural root causes + in-document batches) | Recommended: 12
Summary: Party Chat is a sound server-authoritative relay design with clear rules and complete sections, but three structural issues block approval: (1) party-system.md exposes no member-status query contract (PartyMemberSlot type and GetPartyMembersWithStatus method are undefined upstream, blocking CR-PC-2 eligibility filtering and CR-PC-3 step-ordering); (2) an internal contradiction between the immediacy Player Fantasy and the server-echo-only rule — adjudicated in favor of optimistic display with pending/failed states, requiring CR-PC-1 and AC-PC-2 to be revised; (3) the tap-anywhere chat-panel input model directly conflicts with the Rhythm Mastery pillar (joystick adjacency → accidental keyboard open mid-combat). Remaining blockers are 7 broken/missing ACs and 2 tuning-range fixes, all in-document and suitable for a single batch pass. Lean re-review recommended after the upstream contract is written and the two rule changes are applied.
Prior verdict resolved: No — first review

### Root Causes (structural — resolve before lean re-review)
1. Write IPartySystem member-status contract in party-system.md (PartyMemberSlot struct + GetPartyMembersWithStatus method; option for single-member GetMemberStatus or reorder CR-PC-3 check 1 to step 5)
2. Apply optimistic-display rule change (CR-PC-1 + AC-PC-2): sender sees message immediately in pending state; confirmed on echo; error-stated in place on rejection
3. Replace tap-anywhere activation with dedicated affordance outside joystick zone

### Batch AC/Formula Corrections (in-document, after structural fixes)
- AC-PC-6: split into AC-PC-6a (EC-PC-10 wire-cap → silent discard) and AC-PC-6b (CR-PC-3 codepoint overflow → MessageTooLong)
- AC-PC-7: remove ChatMalformedPayload log assertion (belongs to EC-PC-10 path, not CR-PC-3 check 2)
- AC-PC-3: split into AC-PC-3a (Dead), AC-PC-3b (sub-5s grace-window), AC-PC-3c (actual Ghost)
- AC-PC-11: use valid payloads ≤384B (428B messages are discarded by EC-PC-10 before relay)
- Add AC for CR-PC-4 throttle persistence across reconnect
- Add AC for SenderNotEligible sender path (OutOfZone/Ghost sender)
- EC-PC-10: add textByteCount vs. buffer-length mismatch check (buffer overread protection)
- TK-PC-1 safe range: replace [32, 256] with [32, floor(PARTY_CHAT_MAX_BYTES / 3)]
- CR-PC-4: specify true sliding window algorithm (ring buffer of 5 timestamps per CharacterID)
- PartyChatMessage schema: correct max from 428B to 426B; update F-PC-1 ceiling to 852 bytes/s
- UI Requirements: add keyboard-active layout spec; safe area anchoring; opacity/contrast rule

### Deferred to UX Spec (design/ux/party-chat-panel.md) — Required Compliance
- Panel-shift layout when keyboard active (exact anchor, HUD conflict rules)
- 40% opacity WCAG compliance (background fades; text layer at full opacity)
- Safe area anchoring on iPhone X+ landscape (Screen.safeArea bounds)
- Keyboard dismiss convention (keep keyboard open after send)
