# Review Log: Authentication

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
