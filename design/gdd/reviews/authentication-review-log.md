# Review Log: Authentication

## Review — 2026-11-04 (lean re-review #2) — Verdict: NEEDS REVISION → fixes applied, accepted as Approved
Scope signal: L
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 (resolved in session) | Recommended: 4 (resolved in session)
Summary: All 3 prior blockers verified closed; CR-AUTH-13 (separate character name) is consistent with auth-wire-messages.md, ADR-016 and the registry. One blocker: CHARACTER_NAME_MAX_LENGTH's ceiling of 26 (VARCHAR) exceeded the 24-byte EntityState.characterName wire cap, so legal tuning values would break snapshot encoding — ceiling lowered to 24. Recommended items applied: character-name check order, token-path tradeoff documented (live-session timing, REAUTH session expiry — limited by private usernames), OQ-AUTH-7 for offensive names, UI literal replaced by USERNAME_MAX_LENGTH; AC-AUTH-21(e) added. User accepted the fixes and marked the GDD Approved without a verify pass.
Prior verdict resolved: Yes

---

## Review — 2026-11-04 — Verdict: NEEDS REVISION
Scope signal: L
Specialists: None (lean mode — single-session analysis)
Blocking items: 3 (resolved in session) | Recommended: 7 (resolved in session)
Summary: Re-review of the ADR-016 revision (gateway authentication). The revision itself was coherent, but using the username as the character name made every login name public; with no-decay lockouts any player could keep any other locked out. Fixed by a separate characterName (new CR-AUTH-13, five RegistrationError codes, unique index in migration 003). Also fixed: two stale internal references (networking-session dependency row, CR-AUTH-12 step number), and the token path now checks isBanned. Recommended items applied: single abort check stated, Evict save failure proceeds, account→zone map lifecycle, UI (character name, class picker, error strings), ACs 35–38, AC-AUTH-19 measured in the gateway, networking-core / networking-session wording propagated. CR-AUTH-13 is new structure — re-review in a new session.
Prior verdict resolved: Yes — prior APPROVED (2026-05-21); this pass reviewed the 2026-11-04 ADR-016 revision.

---

## Review — 2026-05-21 — Verdict: APPROVED

Scope signal: L
Specialists: None (lean mode — single-session analysis)
Blocking items: 1 (resolved in session) | Recommended: 3 (resolved in session)
Summary: The revision pass from the prior session successfully resolved all 18 structural blockers. The only remaining issue was a false "no timing difference" claim in CR-AUTH-9 that contradicted the explicit fast-return rule for locked/banned paths (< 10ms) and AC-AUTH-19(a). The tradeoff — accepting timing oracle exposure for locked accounts in exchange for DoS resistance — was confirmed as intentional design policy. The claim was scoped to "identical response content" and the timing delta documented as a known tradeoff. Three recommended fixes applied: PasswordTooLong added to UI error mapping, hardcoded length values replaced with constant name references, AC-AUTH-31 atomicity scope clarified. Document is implementable.
Prior verdict resolved: Yes — prior MAJOR REVISION NEEDED fully addressed across two sessions (revision 2026-05-21, lean re-review 2026-05-21).

---

## Review — 2026-05-20 — Verdict: MAJOR REVISION NEEDED

Scope signal: L
Specialists: security-engineer, network-programmer, systems-designer, qa-lead, game-designer, performance-analyst, creative-director
Blocking items: 18 | Recommended: 13
Summary: The GDD demonstrates excellent security craftsmanship (Argon2id choice, constant-time comparison, sidecar isolation) but cannot be implemented as written due to three structural clusters requiring upstream primitive extraction before revision. Cluster A (protocol): LoginRequest vs ConnectionRequest naming conflict and the AccountID-on-wire contradiction between auth GDD and networking-session-token.md are cross-document blockers that three specialists found independently. Cluster B (security logic): The timing oracle protection is stated but not enforced for lockout/banned paths (which return in <10ms, enabling account enumeration); the lockout state machine contradicts itself on whether loginFailureCount increments during lockout. Cluster C (performance architecture): No MAX_AUTH_PENDING cap, completely unspecified IPC transport, and no formula linking CONNECTING_TIMEOUT_SECONDS to actual queue drain time under load. One vision-level blocker: EC-AUTH-8 (no failure-count decay) permanently locks out legitimate mobile players via admin-gated lockout, violating the Earned Power pillar at the first screen.
Prior verdict resolved: N/A (first review)
