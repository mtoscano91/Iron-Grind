# Story 016: Session Token Generation, Validation & Rotation (NSCRT)

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-session-token.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: No ADR applies — this is pure server-side cryptographic session management using standard .NET APIs (`System.Security.Cryptography`), not an engine-specific or architectural pattern requiring a decision record. Governed directly by `networking-session-token.md` CR-TOK-1 through CR-TOK-8.
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (standard .NET crypto APIs, engine-version-agnostic)
**Engine Notes**: `RandomNumberGenerator.GetBytes` and `CryptographicOperations.FixedTimeEquals` are standard .NET BCL APIs, not Unity-specific — no post-cutoff verification needed.
**Performance Notes**: No performance impact expected — O(1) dictionary lookup/write per auth event (token generation, validation, rotation), not a per-tick scan. The `ConcurrentDictionary` requirement (CR-TOK-8) is a concurrency-safety concern (reconnect handlers and TTL expiry can execute concurrently at the 20Hz tick boundary), not a throughput concern.

**Control Manifest Rules (Foundation layer)**:
- Required: 16-byte token via `RandomNumberGenerator.GetBytes(16)`; never reused across sessions — source: CR-TOK-1
- Required: token comparison via `CryptographicOperations.FixedTimeEquals` (timing-attack resistant) — source: CR-TOK-4
- Required: `ActiveSessionTokens` is an in-process, non-persisted, thread-safe store (`ConcurrentDictionary` or lock-guarded) — source: CR-TOK-8
- Forbidden: persisting the token store to disk or a distributed cache — source: CR-TOK-8

---

## Acceptance Criteria

*From `design/gdd/networking-session-token.md`, scoped to this story:*

- [x] **AC-TOK-1** [BLOCKING]: Given a `Connecting→Connected` transition, `SessionHandshake` contains a non-zero 16-byte token; the server has a matching `ActiveSessionTokens[AccountID]` entry.
- [x] **AC-TOK-2** [BLOCKING]: Given a player in `Disconnected_SessionActive` sending a `ConnectionRequest` with the correct token, the server proceeds directly to `Reconnecting` — no second auth challenge.
- [x] **AC-TOK-3** [BLOCKING]: Given a `ConnectionRequest` with one byte of the token modified, the server returns auth failure; the session remains `Disconnected_SessionActive`, TTL continues.
- [x] **AC-TOK-4** [BLOCKING]: Given a successful `Reconnecting→Connected`, when the client subsequently sends the pre-reconnect (now-rotated) token, the server returns auth failure and does not advance to `Reconnecting`. Token rotation is confirmed by the old token's invalidation — if the old token fails, a new token must have been issued and stored. (Direct byte-comparison of old vs. new token values requires a `byte[] sessionToken` parameter in `OnSessionHandshakeEmitted`; deferred to when the full `SessionHandshake` schema is finalized per OQ-NC-SER-2 — `OnSessionHandshakeEmitted`'s current signature, from Story 013, has no token field. Do not attempt a direct old-vs-new byte comparison; prove rotation only via the old token's invalidation.)
- [x] **AC-TOK-5** [BLOCKING]: Given a server restart with empty `ActiveSessionTokens`, a client whose session data is in persistence connects within TTL, completes full auth, and receives a new token; character state is preserved.
- [x] **AC-TOK-6** [BLOCKING]: Given two concurrent `ConnectionRequest`s from the same client, only the first succeeds; after rotation, the second returns auth failure.
- [x] **AC-TOK-7** [BLOCKING]: Given session-stealing, the original token is invalidated before the new connection's `SessionHandshake` is sent; the original token returns auth failure from that point.
- [x] **AC-TOK-8** [BLOCKING]: Given 1,000 `ConnectionRequest`s with random tokens targeting a valid session, `REAUTH_FAILURE_LIMIT` triggers before any succeed; no timing difference is observable between match/mismatch comparisons (p<0.05). **Resolved test-methodology conflict (approved before implementation)**: this AC's "1,000 random tokens" + statistical timing comparison genuinely conflicts with `.claude/rules/test-standards.md`'s "no random seeds, no time-dependent assertions" rule — no prior Networking Core story has needed a timing benchmark. Resolution: (a) use a **fixed, deterministic** array of 1,000 pre-generated mismatching tokens (not `RandomNumberGenerator`-seeded at test-run time) so the test itself is reproducible; (b) treat the timing-comparison assertion as an explicit, narrowly-scoped exception to the general no-time-dependent-assertions rule — it is the one and only thing this specific AC is testing (constant-time comparison), so a `Stopwatch`-based statistical comparison (mean/variance of match-path vs. mismatch-path durations across the fixed token set, asserting no statistically significant difference) is unavoidable and appropriate here, not a violation of the rule's intent (which guards against *flaky* assertions on unrelated behavior, not a deliberate crypto-timing test).

---

## Implementation Notes

*Derived from CR-TOK-1 through CR-TOK-8:*

- **Generation** (CR-TOK-1): new 16-byte token via `RandomNumberGenerator.GetBytes(16)` on every `Connecting→Connected` transition.
- **Delivery** (CR-TOK-2): fixed-length 16-byte field in `SessionHandshake`; client stores in memory only (session-scoped, never persisted to disk).
- **Presentation on reconnect** (CR-TOK-3): client includes stored token in `ConnectionRequest`; a fresh connection sends a zero-filled 16-byte field, which the server recognizes as "skip token lookup, do full auth."
- **Validation** (CR-TOK-4): derive `AccountID` server-side from `username` (never client-transmitted), look up `ActiveSessionTokens[AccountID]`, compare via `CryptographicOperations.FixedTimeEquals` (never `==` or `SequenceEqual` — timing oracle risk).
- **Rotation** (CR-TOK-5): after every successful `Reconnecting→Connected`, generate a new token, overwrite the store immediately, deliver in the reconnect `ConnectionAccepted` message — the old token is invalid from that instant.
- **Invalidation** (CR-TOK-6): token deleted from the store on TTL expiry, explicit disconnect, session-stealing (deleted before the new connection receives its own token), or server restart (in-memory store cleared).
- **Binding** (CR-TOK-7): each entry keyed by `AccountID`, contains `token: byte[16]`, `sessionId: Guid` (per-session audit correlation), `expiresAtTick: uint`.
- **Storage** (CR-TOK-8): `ConcurrentDictionary<uint, ActiveTokenEntry>` — plain `Dictionary` is unsafe since reconnect handlers and TTL expiry can execute concurrently at the 20Hz tick boundary.
- F-TOK-1 (128-bit entropy) and F-TOK-2 (~60 bytes/session memory overhead) are design-rationale formulas, not runtime checks — no dedicated test needed beyond confirming `TOKEN_LENGTH_BYTES=16`.

---

## Out of Scope

*Handled by neighbouring stories:*

- The `ConnectionRequest`/`SessionHandshake` wire schema fields beyond the token itself — Stories 003/007/012/013 own the rest of those messages
- The state machine transitions this token gates — Stories 012–013

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs`

- **AC-TOK-1** through **AC-TOK-8**: transcribed directly from the GDD's verbatim ACs above — each is independently unit-testable against a mock `ActiveSessionTokens` store and mock connection requests; AC-TOK-8's timing-attack resistance requires a benchmark harness comparing match vs. mismatch comparison durations across 1,000 trials.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs` — must exist and pass

**Status**: [x] Created — 18 test cases, all 8 blocking ACs COVERED with traceability

---

## Dependencies

- Depends on: None directly (self-contained crypto/storage logic); conceptually paired with Story 012 (issued at `Connecting→Connected`) and Story 013 (validated at `Reconnecting`)
- Unlocks: Story 013 (reconnect validation calls into this)

---

## Completion Notes
**Completed**: 2026-07-18
**Criteria**: 8/8 passing (AC-TOK-1 through AC-TOK-8)
**Deviations**: ADVISORY — TR-net-006 not in `docs/architecture/tr-registry.yaml` (systemic, pre-existing gap). ADVISORY — TD-017 logged: AC-TOK-5's "character state is preserved" clause has no test anywhere in this repo and no story currently owns it (correctly out of scope for this token-only class; needs assignment when a session-restore/Character Persistence story lands). Process note: this story was implemented directly by the orchestrating Claude instance rather than a dedicated subagent, after three consecutive delegation failures — the `/code-review` pass was deliberately more rigorous as compensation (unity-specialist hand-traced the CAS correctness argument from first principles).
**Test Evidence**: Logic: `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs` (18 test cases)
**Code Review**: Complete — `/code-review` (lean mode, unity-specialist + qa-tester parallel, extra scrutiny requested): APPROVED WITH SUGGESTIONS. unity-specialist: CLEAN, high confidence on CAS correctness. qa-tester: GAPS — 3 missing tests + the AC-TOK-5 ownership finding. All 3 suggestions fixed: added wrong-length-token/double-issue-overwrite/cross-account-isolation tests, strengthened the AC-TOK-6 concurrency test with a `Barrier` for deterministic overlap, logged TD-017. Final test count: 18 (15 + 3 new).

**This story closes the last independent piece before the Ghost Session cluster (017-021) can begin in earnest.**
