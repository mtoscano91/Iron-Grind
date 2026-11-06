# Review Log — Networking Session Token

---

## Review — 2026-05-14 — Verdict: APPROVED (Pass 1 lean)
Scope signal: S
Specialists: None (lean mode)
Blocking items: 1 | Recommended: 5
Summary: First review of this self-contained sub-spec. One blocker: AC-TOK-4's "byte-different" token comparison assertion was not independently testable without a harness extension; resolved by rewriting to an indirect test (old token must fail after rotation). Five recommended revisions applied in-session: EC-TOK-7 (token-rotated + handshake-delivery-failed mobile edge case), CR-TOK-4 AccountID source clarification, CR-TOK-7 sessionId purpose explanation, CR-TOK-8 thread safety requirement, F-TOK-1 formula label fix (birthday vs. preimage bound). Cryptographic rationale and state-machine integration with networking-session.md are sound. No structural issues found.
Prior verdict resolved: N/A — first review. All blockers resolved before marking Approved.

## Review — 2026-11-06 — Verdict: NEEDS REVISION (lean re-review after ADR-016) → fixes applied, Approved
Scope signal: S
Specialists: None (lean mode)
Blocking items: 2 | Recommended: 4
Summary: Re-review of the ADR-016 gateway alignment. Blockers: (1) CR-TOK-5 rotated "after Reconnecting → Connected" yet delivered the new token in LoginResponse, which precedes zone arrival — now rotation is atomic with validation at the zone's `Reconnect` (`TryValidateAndRotate`), session stays Disconnected_SessionActive until the handshake; (2) no rule covered the token check at `SessionHandshake` or the ST-NET-1 mapping on the token path — new CR-TOK-9 (CharacterID → AccountID via the zone's own record, FixedTimeEquals; match → Connecting→Connected or SessionActive→Reconnecting; mismatch closes the connection, not counted toward REAUTH_FAILURE_LIMIT — user decision) + AC-TOK-9. Recommended applied: CR-TOK-4 ban/unknown-account check and failure end state (SessionActive, not Expired); Dependencies rows (auth-wire-messages, authentication, zone-instancing, ADR-016); EC-TOK-5/AC-TOK-6 re-worded for concurrent `Reconnect`s; username-knower tradeoff cross-referenced. Nice-to-have: CR-TOK-8 thread note, header. Registry unchanged (already consistent). User accepted without a further pass.
Prior verdict resolved: Yes — 2026-05-14 items unaffected; this pass covered the 2026-11-04 revision.
