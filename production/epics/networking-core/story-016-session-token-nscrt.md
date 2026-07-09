# Story 016: Session Token Generation, Validation & Rotation (NSCRT)

> **Epic**: Networking Core
> **Status**: Ready
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

**Control Manifest Rules (Foundation layer)**:
- Required: 16-byte token via `RandomNumberGenerator.GetBytes(16)`; never reused across sessions — source: CR-TOK-1
- Required: token comparison via `CryptographicOperations.FixedTimeEquals` (timing-attack resistant) — source: CR-TOK-4
- Required: `ActiveSessionTokens` is an in-process, non-persisted, thread-safe store (`ConcurrentDictionary` or lock-guarded) — source: CR-TOK-8
- Forbidden: persisting the token store to disk or a distributed cache — source: CR-TOK-8

---

## Acceptance Criteria

*From `design/gdd/networking-session-token.md`, scoped to this story:*

- [ ] **AC-TOK-1** [BLOCKING]: Given a `Connecting→Connected` transition, `SessionHandshake` contains a non-zero 16-byte token; the server has a matching `ActiveSessionTokens[AccountID]` entry.
- [ ] **AC-TOK-2** [BLOCKING]: Given a player in `Disconnected_SessionActive` sending a `ConnectionRequest` with the correct token, the server proceeds directly to `Reconnecting` — no second auth challenge.
- [ ] **AC-TOK-3** [BLOCKING]: Given a `ConnectionRequest` with one byte of the token modified, the server returns auth failure; the session remains `Disconnected_SessionActive`, TTL continues.
- [ ] **AC-TOK-4** [BLOCKING]: Given a successful `Reconnecting→Connected`, when the client subsequently sends the pre-reconnect (now-rotated) token, the server returns auth failure and does not advance to `Reconnecting`.
- [ ] **AC-TOK-5** [BLOCKING]: Given a server restart with empty `ActiveSessionTokens`, a client whose session data is in persistence connects within TTL, completes full auth, and receives a new token; character state is preserved.
- [ ] **AC-TOK-6** [BLOCKING]: Given two concurrent `ConnectionRequest`s from the same client, only the first succeeds; after rotation, the second returns auth failure.
- [ ] **AC-TOK-7** [BLOCKING]: Given session-stealing, the original token is invalidated before the new connection's `SessionHandshake` is sent; the original token returns auth failure from that point.
- [ ] **AC-TOK-8** [BLOCKING]: Given 1,000 `ConnectionRequest`s with random tokens targeting a valid session, `REAUTH_FAILURE_LIMIT` triggers before any succeed; no timing difference is observable between match/mismatch comparisons (p<0.05).

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None directly (self-contained crypto/storage logic); conceptually paired with Story 012 (issued at `Connecting→Connected`) and Story 013 (validated at `Reconnecting`)
- Unlocks: Story 013 (reconnect validation calls into this)
