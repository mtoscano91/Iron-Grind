# Story 009: Login — credential path

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 hours (credential check, two test files, timing test with the real hasher)

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

- [ ] `CredentialCheck.RunAsync(username, password, ct)` returns `CredentialOutcome` (`readonly record struct CredentialOutcome(CredentialResult Result, AccountID AccountID)`, `enum CredentialResult { Passed, InvalidCredentials, ServerError }`; `AccountID` is set only on `Passed`, otherwise `default`) following CR-AUTH-9 steps 3–9 in order: normalize → find → (not found: gate + `Verify` against the dummy hash, discard, fail) → (locked: `RecordFailure`, fail, no hash) → (banned: fail, no hash) → gate + `Verify` → mismatch: `RecordFailure`, fail → match: `ResetFailures`, pass
- [ ] AC-AUTH-10: `"alice"` registered; `"Alice"` and `"ALICE"` with the right password both pass
- [ ] AC-AUTH-17: count 4 + correct password → `ResetFailures` called before the outcome is returned; stored count 0, lockout null
- [ ] AC-AUTH-19 (a): locked account → `InvalidCredentials` in < 10 ms in the service (timed on the second call, after one untimed warm-up call on a separate locked account), hasher call count 0, `RecordFailureAsync` called once with the fake clock's `utcNow` *(the < 10 ms against a local test database is re-measured end to end in Story 012)*
- [ ] AC-AUTH-20: from count 9 and from 50 (no active lockout), a wrong password → `RecordFailureAsync` called once with the fake clock's `utcNow`. F-AUTH-1 is computed by the repository (Story 007, tested against the database); the fake repository applies the real `LockoutPolicy`, so the stored lockout is `utcNow + 600 s`
- [ ] Banned account (EC-AUTH-7): `InvalidCredentials`, no hash, no failure-count change
- [ ] Unknown user / wrong password / locked / banned all produce the same `InvalidCredentials` outcome (no distinguishing field)
- [ ] AC-AUTH-16: with the real `Argon2idPasswordHasher` (and a real `DummyHash` built from it), 20 unknown-username and 20 wrong-password attempts each take ≥ `AUTH_TIMING_FLOOR_MS` (150) and the mean delta is < `AUTH_TIMING_DELTA_THRESHOLD_MS` (20). The constants live in a static `AuthTimingTestConfig` class in the test project, not inline. The test's `AuthOptions` sets `ARGON2_MEMORY_SIZE_KB = 65536` (`AuthTimingTestConfig.ARGON2_MEMORY_SIZE_KB`, inside the GDD safe range 19456–65536), because the live parameters hash in ~69 ms on the dev machine (TD-093) and would fail the 150 ms floor. The test proves both paths run Argon2id at equal cost; the floor at live parameters on the VPS is Story 017 (TD-093)
- [ ] AC-AUTH-29 (i): repository throws (database unavailable) → `ServerError`, failure count unchanged (no write happened), exactly one `LogCritical` entry under `IronGrind.Gateway.Infrastructure`
- [ ] Gate full → `ServerError`, no failure-count change, no hash (CR-AUTH-4) — for a known and for an unknown username
- [ ] Errors: the hasher throws, or `FindByUsernameAsync` / `RecordFailureAsync` / `ResetFailuresAsync` throws → `ServerError` plus exactly one `LogCritical` under `IronGrind.Gateway.Infrastructure` (`AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY`); the gate permit is returned in every case; no log entry contains the password or the PHC
- [ ] Cancellation: `ct` is checked before the gate and again right after the hash (the abandoned-auth seam) → `OperationCanceledException`, no `RecordFailure` / `ResetFailures`, permit returned. `FindByUsernameAsync` receives `ct` (read only); `RecordFailureAsync` and `ResetFailuresAsync` receive `CancellationToken.None`
- [ ] Logging: a credential failure is logged at Information under the `CredentialCheck` category with the `AccountID` when known and no identifier for an unknown username — never the username, the password or the PHC

---

## Implementation Notes

*Derived from ADR-016 Decisions 1 and 4, GDD CR-AUTH-9:*

- Shape: `public sealed class CredentialCheck` with constructor `(IAccountRepository, IPasswordHasher, IAuthHashGate, DummyHash, TimeProvider, ILoggerFactory)`. `IAuthenticationService` is unchanged in this story; Story 010 adds `AuthenticateAsync` on top of `CredentialCheck`.
- Clock: inject `TimeProvider` (.NET 8+) and pass `GetUtcNow().UtcDateTime` to `RecordFailureAsync` — tests use `FakeTimeProvider` or a hand-rolled one.
- Lockout check is `lockoutUntilUtc != null && utcNow < lockoutUntilUtc` — then `RecordFailure` (the counter advances on the locked path, CR-AUTH-9 step 5).
- The unknown-user dummy `Verify` must go through the same gate and `Task.Run` as a real verify, so timing matches (AC-AUTH-16).
- The "abandoned auth" check (CR-AUTH-9) sits between the hash finishing and the first write. Leave a clearly named seam for it (`ct.IsCancellationRequested` checked after the hash, before `RecordFailure` / `ResetFailures`); Story 012 drives it from the HTTP request-aborted token and tests it.
- AC-AUTH-16 is a timing test with the real Argon2id at `ARGON2_MEMORY_SIZE_KB = 65536` (~40 hashes × ~230 ms ≈ 10 s on the dev machine). Put it in `[Category("Timing")]` so it can be filtered if CI timing is noisy; it still runs by default.
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
  - Then: every sample ≥ `AUTH_TIMING_FLOOR_MS`; |mean₁ − mean₂| < `AUTH_TIMING_DELTA_THRESHOLD_MS` (test `AuthOptions` memory 65536 KB)
- **AC-7 (AC-AUTH-29 i)**: Database down
  - Given: repo throws on `FindByUsernameAsync`
  - Then: `ServerError`; no writes; one critical infrastructure log entry
- **AC-8**: Gate full
  - Given: all permits held
  - Then: `ServerError`; no repository write; hasher not called — for a known and an unknown username
- **AC-9**: Errors and cancellation
  - Given: the hasher throws; separately `RecordFailureAsync` throws; separately `ResetFailuresAsync` throws
  - Then: `ServerError`, one Critical infrastructure entry each, permit returned (a next check acquires it)
  - Given: `ct` cancelled while the hash is blocked → `OperationCanceledException`, no write, permit returned; `ct` already cancelled → OCE before the gate, hasher count 0
  - Given: a fake repository recording tokens → `RecordFailureAsync` / `ResetFailuresAsync` received `CancellationToken.None`

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_CredentialPath_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Auth_TimingOracle_tests.cs`

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: Story 004, Story 005, Story 007 (`IAccountRepository` login methods), Story 008 (`IAuthHashGate`)
- Unlocks: Story 010, Story 012

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 13/13 passing (`Auth_CredentialPath_tests.cs` 31 cases, pure fakes; `Auth_TimingOracle_tests.cs` 1 case, `[Category("Timing")]`)
**Deviations**: Advisory only — (1) the AC-AUTH-16 test uses `ARGON2_MEMORY_SIZE_KB = 65536` (readiness decision; live parameters hash ~69 ms here, TD-093) — measured means 191–197 ms both paths, min sample 184.7 ms, delta 0.4–1.8 ms; the live-parameter floor on the VPS is Story 017; (2) the timing test's fake repository ignores `RecordFailureAsync` writes so samples stay on the hash path — the end-to-end write cost is noted in Story 017; (3) the locked path records the clock read that decided "locked", the wrong-password path reads the clock again after the hash
**Test Evidence**: Logic — `gateway/IronGrind.Gateway.Tests/Auth_CredentialPath_tests.cs`, `gateway/IronGrind.Gateway.Tests/Auth_TimingOracle_tests.cs` (constants in `AuthTimingTestConfig.cs`); gateway suite (`TestCategory!=Database`) 234/234, 0 warnings
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, 4 applied (`RunAsync` split into `FindAsync` / `PassAsync`; clock reads per path; wall-clock comment on the < 10 ms test; Story 017 note on AC-AUTH-16 end to end)
