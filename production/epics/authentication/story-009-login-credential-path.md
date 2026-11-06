# Story 009: Login — credential path

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-9 steps 3–9, CR-AUTH-10, EC-AUTH-7 (ban on login), EC-AUTH-8, EC-AUTH-10 (database); AC-AUTH-10, 16, 17, 19 (a), 20, 29 (i)
**Requirement**: `TR-auth-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 1, Decision 4 step 1)
**ADR Decision Summary**: Fresh login step 1 is the hash check, lockout, ban and failure counting against the accounts table. An unknown username still runs Argon2id against the startup dummy hash. Locked and banned paths return immediately without hashing. Every credential failure returns the identical `InvalidCredentials`.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (this layer)**:
- Required: fresh login order (1) hash check, lockout, ban and failure counting (CR-AUTH-9/10) — ADR-016 Decision 4
- Required: hashing inside the shared `MAX_AUTH_PENDING` gate on the thread pool — ADR-016 Decision 1
- Required: the dummy hash for unknown usernames is computed at startup with the live parameters — ADR-016 Decision 1

---

## Acceptance Criteria

- [ ] `CredentialCheck.RunAsync(username, password, ct)` returns `CredentialOutcome { Passed, AccountID, InvalidCredentials, ServerError }` following CR-AUTH-9 steps 3–9 in order: normalize → find → (not found: gate + `Verify` against the dummy hash, discard, fail) → (locked: `RecordFailure`, fail, no hash) → (banned: fail, no hash) → gate + `Verify` → mismatch: `RecordFailure`, fail → match: `ResetFailures`, pass
- [ ] AC-AUTH-10: `"alice"` registered; `"Alice"` and `"ALICE"` with the right password both pass
- [ ] AC-AUTH-17: count 4 + correct password → `ResetFailures` called before the outcome is returned; stored count 0, lockout null
- [ ] AC-AUTH-19 (a): locked account → outcome in < 10 ms in the service, hasher call count 0, `RecordFailure` called once *(the < 10 ms against a local test database is re-measured end to end in Story 012)*
- [ ] AC-AUTH-20: from count 9 and from 50, a wrong password leads to `RecordFailure` and the stored lockout is `utcNow + 600 s`
- [ ] Banned account (EC-AUTH-7): `InvalidCredentials`, no hash, no failure-count change
- [ ] Unknown user / wrong password / locked / banned all produce the same `InvalidCredentials` outcome (no distinguishing field)
- [ ] AC-AUTH-16: with the real hasher, 20 unknown-username and 20 wrong-password attempts each take ≥ `AUTH_TIMING_FLOOR_MS` (150) and the mean delta is < `AUTH_TIMING_DELTA_THRESHOLD_MS` (20); both constants are defined in the test config, not inline
- [ ] AC-AUTH-29 (i): repository throws (database unavailable) → `ServerError`, failure count unchanged (no write happened), critical infrastructure log entry
- [ ] Gate full → `ServerError`, no failure-count change (CR-AUTH-4)

---

## Implementation Notes

*Derived from ADR-016 Decisions 1 and 4, GDD CR-AUTH-9:*

- Clock: inject `TimeProvider` (.NET 8+) and pass `GetUtcNow().UtcDateTime` to `RecordFailureAsync` — tests use `FakeTimeProvider` or a hand-rolled one.
- Lockout check is `lockoutUntilUtc != null && utcNow < lockoutUntilUtc` — then `RecordFailure` (the counter advances on the locked path, CR-AUTH-9 step 5).
- The unknown-user dummy `Verify` must go through the same gate and `Task.Run` as a real verify, so timing matches (AC-AUTH-16).
- The "abandoned auth" check (CR-AUTH-9) sits between the hash finishing and the first write. Leave a clearly named seam for it (`ct.IsCancellationRequested` checked after the hash, before `RecordFailure` / `ResetFailures`); Story 012 drives it from the HTTP request-aborted token and tests it.
- AC-AUTH-16 is a timing test with the real Argon2id (~40 hashes ≈ 10–15 s). Put it in `[Category("Timing")]` so it can be filtered if CI timing is noisy; it still runs by default.
- Log the outcome category, never the password, never the username on a failure if avoidable (log `AccountID` when known).

---

## Out of Scope

- Story 010: what happens after `Passed` (CharacterID, Evict, Admit)
- Story 011: the token path
- Story 012: abort/timeout discard (AC-AUTH-26, 32) and HTTP

---

## QA Test Cases

- **AC-1 (AC-AUTH-10)**: Case-insensitive login
  - Given: fake repo with `"alice"` and a PHC from the fake hasher
  - When: `RunAsync("Alice", pw)`, `RunAsync("ALICE", pw)`
  - Then: both `Passed` with the same AccountID
- **AC-2 (AC-AUTH-17)**: Reset on success
  - Given: count 4
  - When: correct password
  - Then: `ResetFailures` called once, before return; fake stores 0/null
- **AC-3 (AC-AUTH-19 a)**: Locked fast path
  - Given: lockout until now + 30 s, count 5
  - When: correct password
  - Then: `InvalidCredentials`; hasher calls 0; `RecordFailure` once; elapsed < 10 ms (Stopwatch)
- **AC-4 (AC-AUTH-20)**: Cap
  - Given: count 9 / 50, no active lockout
  - When: wrong password
  - Then: `RecordFailure` with `utcNow`; resulting lockout +600 s
- **AC-5**: Ban
  - Given: `IsBanned` true, correct password
  - Then: `InvalidCredentials`; no hash; no `RecordFailure`, no `ResetFailures`
- **AC-6 (AC-AUTH-16)**: Timing oracle
  - Given: real hasher, fake repo
  - When: 20 unknown-user and 20 wrong-password attempts, timed
  - Then: every sample ≥ 150 ms; |mean₁ − mean₂| < 20 ms
- **AC-7 (AC-AUTH-29 i)**: Database down
  - Given: repo throws on `FindByUsernameAsync`
  - Then: `ServerError`; no writes; one critical infrastructure log entry
- **AC-8**: Gate full
  - Given: all permits held
  - Then: `ServerError`; no repository write

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_CredentialPath_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Auth_TimingOracle_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 005, Story 007 (`IAccountRepository` login methods), Story 008 (`IAuthHashGate`)
- Unlocks: Story 010, Story 012
