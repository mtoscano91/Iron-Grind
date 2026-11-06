# Story 012: HTTP endpoints and the gateway host

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 6 hours (host wiring, no-zone stand-ins, three test files, two Database cases, Kestrel binding test)

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-4, CR-AUTH-9 "Abandoned auth", States and Transitions, EC-AUTH-2, EC-AUTH-3; AC-AUTH-09, 19 (a, end to end), 26, 32 (b, c), 36 (login half, through HTTP); `design/gdd/auth-wire-messages.md` Transport
**Requirement**: `TR-auth-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decisions 1–2)
**ADR Decision Summary**: The gateway is an ASP.NET Core minimal-API app listening on `http://127.0.0.1:GATEWAY_HTTP_PORT` only, behind Caddy. `POST /v1/login` and `POST /v1/register` take and return `application/octet-stream` bodies; every outcome, failures included, is HTTP 200; non-200 means transport or server failure. An aborted request's hash result is discarded before any write.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: In-process tests use `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) — a test-only NuGet package; pin the latest 10.0.x (checked on NuGet at dev-story time, as Story 004 did), add the `PackageReference` to `IronGrind.Gateway.Tests.csproj` and a line to `technical-preferences.md` (gateway tests only). `Program` is already `public partial` (skeleton, 2026-11-06).

**Control Manifest Rules (this layer)**:
- Required: endpoints `POST /v1/login` and `POST /v1/register`, `Content-Type: application/octet-stream`; every outcome is HTTP 200 with the result in the body — ADR-016 Decision 2
- Required: the gateway listens on loopback only — ADR-016 Decision 1
- Forbidden: terminating client TLS in the gateway (`SslStream`) — Caddy does it (Story 016)

---

## Acceptance Criteria

- [ ] `Program.cs` wires DI: `AddGatewayOptions` (Story 004, `ValidateOnStart`); `IPasswordHasher` = `Argon2idPasswordHasher`; `IAuthHashGate` = `AuthHashGate(GatewayOptions.MaxAuthPending)` singleton (Story 008); `DummyHash` singleton computed eagerly at startup with the live parameters; one `NpgsqlDataSource` from `IRONGRIND_GATEWAY_DB` + `NpgsqlAccountRepository`; `LockoutPolicy`, `CredentialCheck`, `SessionEstablishment`, `TokenPath`, `AuthenticationService`; `TimeProvider.System`. Maps the two endpoints. Kestrel binds `127.0.0.1:{GATEWAY_HTTP_PORT}` only, through a helper `GatewayHost.ConfigureKestrel(KestrelServerOptions, int httpPort)` using `IPAddress.Loopback`
- [ ] No-zone stand-ins until the gateway epic: `Auth/NoZoneStandIns.cs` with `EmptyAccountZoneMap` (no entries), `NoZoneRouter` (`TryPick` and `TryGetPort` return `false`), `UnavailableZoneLink` (throws if called); each documented as replaced by the gateway epic. A correct login over HTTP → 200 + `ServerStarting`
- [ ] Startup fails when: `IRONGRIND_GATEWAY_DB` is missing; its connection string has `Include Error Detail=true`; an option is invalid (host-level `ValidateOnStart` test carried from Story 004 — e.g. `GATEWAY_HTTP_PORT=0`)
- [ ] TD-092 documented: new `gateway/README.md` (run, configuration, env vars) states that a `LOCKOUT_TABLE_SECONDS` override must supply the full table (arrays merge by index); TD-092 marked resolved in `docs/tech-debt-register.md`
- [ ] `POST /v1/login`: decode `ConnectionRequest` (Story 002) → `AuthenticationService.AuthenticateAsync(request, HttpContext.RequestAborted)` → encode `LoginResponse`, status 200, `application/octet-stream`. `POST /v1/register` likewise
- [ ] A malformed body → HTTP 400 (client treats non-200 as `ServerError`); a missing or wrong `Content-Type` → 415; a body over `MAX_REQUEST_BODY_BYTES = 1024` (constant in the endpoint code; the largest valid request is about 600 bytes) → 413 — none of these hash
- [ ] A request aborted by the client: the `OperationCanceledException` thrown when `RequestAborted` is set is caught by the endpoint, which returns without a body — not a 500, no Critical entry
- [ ] Any other unhandled exception → 500 with an empty body, one `LogCritical` under `IronGrind.Gateway.Infrastructure`
- [ ] AC-AUTH-26: client aborts during `Auth_Pending` (hash delayed in the test host), once with a wrong password and once with the correct one → `login_failure_count` and `lockout_until_utc` unchanged, no `Evict` or `Admit` sent; the hash still ran to completion
- [ ] AC-AUTH-32 (b, c): the client's timeout fires while the hash is delayed past it → failure count unchanged, no `Admit` (same discard path, driven by the client's cancellation)
- [ ] AC-AUTH-36 (login, end to end): with `MAX_AUTH_PENDING` hashes held, one more correct-password login → 200 with `ServerError`, no hash, failure count and lockout unchanged, no `Admit`
- [ ] AC-AUTH-09 (b): the gateway's log output during a register + correct login + wrong login (captured with an in-memory logger provider, GAP-6) contains no substring equal to either submitted password; (a): the stored `password_hash_phc` starts with `$argon2id$` (test database run)
- [ ] AC-AUTH-19 (a, end to end): with the database account locked, the handler time measured in the gateway (a test-only timing middleware added through an `IStartupFilter` in the `WebApplicationFactory`; no timer in production code; local test database) is < 10 ms
- [ ] Loopback binding: a test starts the real app on Kestrel with `GATEWAY_HTTP_PORT` set to a free port and asserts every address in `IServerAddressesFeature` is `127.0.0.1`

---

## Implementation Notes

*Derived from ADR-016 Decisions 1–2 and the GDD "Abandoned auth" rule:*

- The abort check is the seam Story 009 left: after the hash completes, if `RequestAborted.IsCancellationRequested`, return without any write and without calling Story 010. This is the **only** abort check — after it, the login runs to completion even if the client is gone (CR-AUTH-9).
- Do not pass `RequestAborted` into `Task.Run(hash)` as a cancellation that stops the hash — the hash finishes, then the result is discarded (EC-AUTH-2 rationale).
- Delay injection (GAP-3/GAP-4): replace `IPasswordHasher` in the test host with a wrapper that awaits a `TaskCompletionSource` before calling the real one.
- Abort injection: `HttpClient` + `CancellationTokenSource.Cancel()` mid-request against `WebApplicationFactory`'s handler; assert on the repository/link fakes (or the test database) after the server-side handler finishes (await a server-side completion signal, not a sleep).
- Logging: never log request bodies; add a test that would fail if a body logger were added.
- `Content-Type` check is exact `application/octet-stream`.
- Build one `NpgsqlDataSource` from `IRONGRIND_GATEWAY_DB` at startup and register it in DI for `NpgsqlAccountRepository` (Story 006 left this to the host); a missing variable fails startup, and so does a connection string with `Include Error Detail=true` (it would let database errors log the failing row, password hash included — Story 006 code review).
- CI: these tests run with fakes (no database) except the `[Category("Database")]` ones for AC-AUTH-09 (a) and AC-AUTH-19 (a). Those use the `AuthTestDatabase` fixture with `IRONGRIND_GATEWAY_DB` + `IRONGRIND_GATEWAY_TEST_ADMIN_DB` against `irongrind_auth_test`, and `Assert.Ignore` when either is unset.
- Fake-service tests replace services in `WebApplicationFactory.WithWebHostBuilder(... ConfigureTestServices ...)`; they must not need `IRONGRIND_GATEWAY_DB` (register the data source lazily or let the test host override the repository before it is resolved).

---

## Out of Scope

- Story 016: Caddy / TLS (AC-AUTH-08)
- Story 015: the client side of the timeout (AC-AUTH-32 (a))
- Gateway epic: the zone-link listener on `GATEWAY_ZONE_LINK_PORT`

---

## QA Test Cases

- **AC-1**: Endpoint contract
  - Given: test host with fake services returning each outcome
  - When: POST valid bodies
  - Then: always 200, `application/octet-stream`, body decodes to the outcome (`InvalidCredentials`, `ServerError`, `ServerStarting`, success; each `RegistrationError`)
  - Edge cases: GET → 405; truncated body → 400 and hasher calls 0; `text/plain` → 415; 2 KiB body → 413
- **AC-2 (AC-AUTH-26)**: Abort discards
  - Given: account count 2, hasher held; client cancels; then hasher released
  - When: server-side handler completes
  - Then: count 2, lockout unchanged, link calls 0 — for both wrong and correct password
- **AC-3 (AC-AUTH-32 b,c)**: Client timeout
  - Given: `HttpClient.Timeout` 200 ms, hasher held 500 ms
  - Then: same assertions as AC-2
- **AC-4 (AC-AUTH-36)**: Gate full over HTTP
  - Given: `MAX_AUTH_PENDING` 2, two logins held
  - When: third login (correct password)
  - Then: 200 + `ServerError` while the two are held; count unchanged; link calls 0
- **AC-5 (AC-AUTH-09)**: No password in logs
  - Given: in-memory logger at `Trace`
  - When: register `"S3cret!pass"`, login right, login with `"Wr0ng!pass"`
  - Then: no log message or scope contains either password; (DB) PHC starts with `$argon2id$`
- **AC-6 (AC-AUTH-19 a)**: Locked fast path end to end
  - Given: test DB account with an active lockout
  - Then: handler time < 10 ms
- **AC-7**: Host wiring
  - Given: the real `Program` with no-zone stand-ins and fake repository/hasher
  - Then: correct login → 200 + `ServerStarting`; `GATEWAY_HTTP_PORT=0` → startup throws `OptionsValidationException`; `IRONGRIND_GATEWAY_DB` unset or with `Include Error Detail=true` → startup throws; Kestrel on a free port → all addresses `127.0.0.1`
- **AC-8**: Aborted request and unhandled exception
  - Given: client cancels during the held hash → no 500, no Critical entry; a fake service that throws `InvalidOperationException` → 500, empty body, one Critical in the infrastructure category

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_Endpoints_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Gateway_AbandonedAuth_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Gateway_LogRedaction_tests.cs` (Database cases `[Category("Database")]`)
- `gateway/IronGrind.Gateway.Tests/Gateway_Host_tests.cs` (startup validation, stand-ins, loopback binding)

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: Story 002, Story 008, Story 009, Story 010, Story 011
- Unlocks: Story 015, Story 016, Story 017

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 14/14 passing (`Gateway_Endpoints_tests.cs`, `Gateway_AbandonedAuth_tests.cs`, `Gateway_LogRedaction_tests.cs` incl. 2 `[Category("Database")]` cases, `Gateway_Host_tests.cs`; shared helper `GatewayTestSupport.cs`)
**Deviations**: Advisory only — (1) the `NpgsqlDataSource` is a lazy singleton and the hosted `GatewayStartupCheck` resolves the repository and `DummyHash` at start (real host fails fast; test hosts with a fake repository need no `IRONGRIND_GATEWAY_DB`); (2) the loopback test runs `GatewayHost.CreateApp` on real Kestrel (TestServer has no Kestrel); (3) code-review additions: `Cache-Control: no-store` on 200 responses, one generic endpoint handler; (4) fixed in implementation: the Database lockout SQL wrote `now() AT TIME ZONE 'utc'` into a `TIMESTAMPTZ` column → `now() + interval '1 hour'`. `Microsoft.AspNetCore.Mvc.Testing` pinned at 10.0.14 (tests only). TD-092 resolved (`gateway/README.md`)
**Test Evidence**: Integration — the four files above; full gateway suite with both env vars against `irongrind_auth_test` 379/379, 0 warnings
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, 3 applied (`Cache-Control: no-store`; `Gateway_Host_tests` `[NonParallelizable]`; `HandleAsync<TRequest, TResult>`)
