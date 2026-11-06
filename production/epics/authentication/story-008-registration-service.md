# Story 008: Registration service

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

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
- Required: dependency injection over singletons (coding standards) — `IAccountRepository`, `IPasswordHasher`, `IAuthHashGate` injected

---

## Acceptance Criteria

- [ ] `AuthenticationService.RegisterAsync(RegistrationRequest, ct)` → `RegistrationResult`: normalize username → `RegistrationValidator` (Story 005) → acquire the hash gate → `Task.Run(Hash)` → `IAccountRepository.RegisterAsync` → map the outcome
- [ ] A validation failure returns its `RegistrationError` with no hash and no repository call (asserted via fakes)
- [ ] AC-AUTH-12 (c, d): success → `RegistrationResult { success:true }`; no session token is produced and no `IZoneLink` call happens (the service has no zone-link dependency on this path — asserted with a strict fake)
- [ ] AC-AUTH-34 (service): unknown `classType` 0 and 255 → `ClassInvalid` with no repository call
- [ ] AC-AUTH-36 (registration): with `MAX_AUTH_PENDING` permits held, one more registration returns `ServerError` without hashing and without a repository call
- [ ] EC-AUTH-4 / EC-AUTH-10: repository `ServerError` outcome or exception → `RegistrationResult { success:false, errorCode:ServerError }`; a database exception is logged as a critical infrastructure event (separate log category from auth failures), never with the password
- [ ] `IAuthHashGate` (the `MAX_AUTH_PENDING` semaphore) is a single shared instance used by login (Story 009) too, with `TryEnter()` non-blocking

---

## Implementation Notes

*Derived from ADR-016 Decision 1:*

- `IAuthHashGate { bool TryEnter(); void Exit(); }` wrapping `SemaphoreSlim(MAX_AUTH_PENDING)` with `Wait(0)`. Release in `finally`.
- Hash with `await Task.Run(() => _hasher.Hash(password), ct)` so it never runs on the request's I/O thread.
- Username is normalized before validation and stored normalized; `characterName` is stored as sent.
- Test fakes: `FakeAccountRepository`, `FakePasswordHasher` (records calls, returns a fixed PHC, can block on a `TaskCompletionSource` to hold permits — the delay injection for AC-AUTH-36), `StrictZoneLink` that fails the test on any call.
- `IAuthenticationService` per the GDD: `RegisterAsync`, `AuthenticateAsync` (Story 009–011), `GetCharacterIDAsync`. This story creates the class and the registration method.

---

## Out of Scope

- Story 009: login credential path (shares the gate)
- Story 012: the `/v1/register` endpoint and HTTP status handling

---

## QA Test Cases

- **AC-1**: Happy path
  - Given: fakes, valid request
  - When: `RegisterAsync`
  - Then: `success:true`; repository called once with `"alice"` (from `"Alice"`), the fake PHC, `"Grim_Iron7"`, class; hasher called once; zone link never called
- **AC-2**: Validation short-circuit
  - Given: password length 7
  - When: `RegisterAsync`
  - Then: `PasswordTooShort`; hasher and repository call counts 0
  - Edge cases: `classType` 0 and 255 → `ClassInvalid`, counts 0
- **AC-3 (AC-AUTH-36)**: Gate full
  - Given: `MAX_AUTH_PENDING = 2`; two registrations blocked inside the fake hasher
  - When: a third arrives
  - Then: `ServerError` immediately (completes while the two are still blocked); hasher call count stays 2; repository not called for the third
  - Edge cases: release the two → both complete normally; a fourth then succeeds (permits returned)
- **AC-4 (EC-AUTH-4/10)**: Repository failure
  - Given: repository returns `ServerError`; separately throws `NpgsqlException`
  - When: `RegisterAsync`
  - Then: `ServerError`; for the exception, one critical log entry in the infrastructure category; no log entry contains the password

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_RegistrationService_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004, Story 005, Story 006 (`RegistrationOutcome` shape)
- Unlocks: Story 012
