# Story 008: Registration service

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours (service + hash gate + test fakes, one Logic test file)

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-4 (`MAX_AUTH_PENDING`), CR-AUTH-8, EC-AUTH-4, EC-AUTH-10 (database); AC-AUTH-12 (c, d), 34, 36 (registration half)
**Requirement**: `TR-auth-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 1)
**ADR Decision Summary**: Hashing runs on the thread pool inside a `SemaphoreSlim(MAX_AUTH_PENDING)`; when the semaphore is full the request gets `ServerError` immediately. All validation runs before hashing.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (this layer)**:
- Required: hashing (login and registration) runs on the thread pool (`Task.Run`) inside a `SemaphoreSlim` of `MAX_AUTH_PENDING`; when it is full the request gets `ServerError` at once — ADR-016 Decision 1
- Required: dependency injection over singletons (coding standards) — `IAccountRepository`, `IPasswordHasher`, `IAuthHashGate`, `ILoggerFactory` injected; the service takes no `IZoneLink` (Story 010 adds it for login)

---

## Acceptance Criteria

- [ ] `AuthenticationService.RegisterAsync(RegistrationRequest, ct)` → `RegistrationResult`: normalize username → `RegistrationValidator` (Story 005) → acquire the hash gate → `Task.Run(Hash)` → `IAccountRepository.RegisterAsync` → map the outcome
- [ ] A validation failure returns its `RegistrationError` with no hash and no repository call (asserted via fakes)
- [ ] AC-AUTH-12 (c, d): success → `RegistrationResult { success:true }`; no session token is produced and no zone call can happen — asserted structurally: the `AuthenticationService` constructor in this story takes no `IZoneLink` (that interface arrives in Story 010) and `RegistrationResult` has no token field
- [ ] AC-AUTH-34 (service): unknown `classType` 2 and 255 → `ClassInvalid` with no hash and no repository call (0 Warrior and 1 Healer are playable — `PlayableClasses`, Story 005)
- [ ] AC-AUTH-36 (registration): with `MAX_AUTH_PENDING` permits held, one more registration returns `ServerError` without hashing and without a repository call
- [ ] EC-AUTH-4 / EC-AUTH-10: repository `ServerError` outcome or exception → `RegistrationResult { success:false, errorCode:ServerError }`, and the service writes exactly one `LogCritical` entry under the logger category `IronGrind.Gateway.Infrastructure` (separate from auth-failure logging) for both the outcome and the exception; no log entry contains the password or the PHC. (The real repository already logs the exception at Error under its own category and returns `ServerError`; the service's Critical entry is the infrastructure event.)
- [ ] `IAuthHashGate` (the `MAX_AUTH_PENDING` semaphore) is a single shared instance used by login (Story 009) too, with `TryEnter()` non-blocking; `AuthHashGate(GatewayOptions.MaxAuthPending)` is registered as a singleton in Story 012
- [ ] The gate covers hashing only: acquired after validation, released in `finally` right after the hash and before the repository call. The permit is returned when the hasher throws (→ `ServerError` + one Critical infrastructure entry) and when the request is cancelled
- [ ] Cancellation: `ct` is checked before acquiring the gate and again right after hashing (`OperationCanceledException` propagates, no repository call); from the repository call on, the service passes `CancellationToken.None`

---

## Implementation Notes

*Derived from ADR-016 Decision 1:*

- `IAuthHashGate { bool TryEnter(); void Exit(); }` wrapping `SemaphoreSlim(MAX_AUTH_PENDING)` with `Wait(0)`. Release in `finally`.
- Hash with `await Task.Run(() => _hasher.Hash(password), ct)` so it never runs on the request's I/O thread.
- Username is normalized before validation and stored normalized; `characterName` is stored as sent.
- Test fakes: `FakeAccountRepository` (records calls; returns a set outcome or throws), `FakePasswordHasher` (records calls, returns a fixed PHC, can block on a `TaskCompletionSource` to hold permits — the delay injection for AC-AUTH-36 — and can throw), a recording logger provider capturing category, level and message. No zone-link fake: `IZoneLink` does not exist until Story 010.
- The request's abort check is the `ct` check right after hashing. Call `IAccountRepository.RegisterAsync` with `CancellationToken.None` once past it: a cancellation during the commit leaves the outcome unknown (Story 006 code review). Check `RegistrationOutcome.Success` before `Error` — a success carries `default` (`UsernameTaken`).
- `IAuthenticationService` per the GDD ends up with `RegisterAsync`, `AuthenticateAsync`, `GetCharacterIDAsync`. This story creates the interface and the class with `RegisterAsync` only; Stories 009–011 add `AuthenticateAsync` and `GetCharacterIDAsync` (same incremental pattern as `IAccountRepository` in Stories 006/007).
- Infrastructure log category: `ILoggerFactory.CreateLogger("IronGrind.Gateway.Infrastructure")`; Story 009 reuses it for login.

---

## Out of Scope

- Story 009: login credential path (shares the gate)
- Story 012: the `/v1/register` endpoint, HTTP status handling, and DI registration (`AuthHashGate` singleton)
- Stories 009–011: `AuthenticateAsync`, `GetCharacterIDAsync`
- Story 010: `IZoneLink` and its strict test fake

---

## QA Test Cases

- **AC-1**: Happy path
  - Given: fakes, valid request
  - When: `RegisterAsync`
  - Then: `success:true`; repository called once with `"alice"` (from `"Alice"`), the fake PHC, `"Grim_Iron7"`, class, and `CancellationToken.None`; hasher called once
- **AC-2**: Validation short-circuit
  - Given: password length 7
  - When: `RegisterAsync`
  - Then: `PasswordTooShort`; hasher and repository call counts 0
  - Edge cases: `classType` 2 and 255 → `ClassInvalid`, counts 0; classes 0 and 1 → success
- **AC-3 (AC-AUTH-36)**: Gate full
  - Given: `MAX_AUTH_PENDING = 2`; two registrations blocked inside the fake hasher
  - When: a third arrives
  - Then: `ServerError` immediately (completes while the two are still blocked); hasher call count stays 2; repository not called for the third
  - Edge cases: release the two → both complete normally; a fourth then succeeds (permits returned)
- **AC-4 (EC-AUTH-4/10)**: Repository failure
  - Given: repository returns `ServerError`; separately throws `NpgsqlException`
  - When: `RegisterAsync`
  - Then: `ServerError`; in both cases exactly one `Critical` entry in category `IronGrind.Gateway.Infrastructure`; no log entry contains the password or the PHC
- **AC-5**: Gate release on failure and cancellation
  - Given: `MAX_AUTH_PENDING = 1`
  - When: the hasher throws; separately, `ct` is cancelled while the hash is blocked
  - Then: the throw → `ServerError` + one Critical entry; the cancellation → `OperationCanceledException`, repository not called; after each, a new registration acquires the permit and succeeds
  - Edge cases: `ct` already cancelled → `OperationCanceledException` before the gate, hasher count 0

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_RegistrationService_tests.cs`

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: Story 004, Story 005, Story 006 (`RegistrationOutcome` shape)
- Unlocks: Story 012

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 9/9 passing (all covered by `Auth_RegistrationService_tests.cs`, 17 methods / 21 cases, pure fakes)
**Deviations**: Advisory only — (1) a full hash gate returns `ServerError` with no log entry (load, not an infrastructure failure); (2) cancellation while a hash runs does not stop the `Task.Run` delegate: the permit returns when the hash finishes and the `OperationCanceledException` comes from the post-hash `ct` check, which keeps concurrent hashes bounded by `MAX_AUTH_PENDING`
**Test Evidence**: Logic — `gateway/IronGrind.Gateway.Tests/Auth_RegistrationService_tests.cs`; gateway suite (`TestCategory!=Database`) 202/202, 0 warnings
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, 4 applied (`RegisterAsync` split into `HashInsideGateAsync` / `StoreAsync`; OCE rethrown only when the request is cancelled — a hasher-internal OCE → `ServerError` + one Critical; repo-throws test uses `NpgsqlException`; pre-cancelled test proves no permit taken)
