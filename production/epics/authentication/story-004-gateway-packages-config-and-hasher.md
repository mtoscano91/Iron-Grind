# Story 004: Gateway packages, config and the Argon2id hasher

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 hours (NuGet pins + 2 options classes with validation + hasher + dummy hash, tests in the gateway project, 20-run benchmark)

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-5, CR-AUTH-9 step 4 (dummy hash), AC-AUTH-06, AC-AUTH-07, Tuning Knobs
**Requirement**: `TR-auth-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 1, Decision 7, Key Interfaces `IPasswordHasher`), with ADR-013 (randomness)
**ADR Decision Summary**: The gateway hashes with Argon2id via `Konscious.Security.Cryptography.Argon2` (CR-AUTH-5 parameters), stores a PHC string with a 16-byte salt from `RandomNumberGenerator`, and verifies with `FixedTimeEquals`. The first gateway story pins the NuGet versions and records them in `technical-preferences.md`.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: Konscious performance on the VPS may differ from the ~300 ms estimate (ADR-016 Risks) — benchmark here on the dev machine and record it; VPS benchmark is Story 017.

**Control Manifest Rules (this layer)**:
- Required: Argon2id via `Konscious.Security.Cryptography.Argon2` with CR-AUTH-5 parameters, PHC string, 16-byte salt from `RandomNumberGenerator`; `Verify` uses `FixedTimeEquals`; the dummy hash is computed at gateway startup with the live parameters — ADR-016 Decision 1
- Required: gateway libraries are NuGet `PackageReference`s; the first gateway story pins versions and adds them to `technical-preferences.md` — ADR-016 Decision 7
- Required: gateway tuning knobs are config, not hardcoded — ADR-016 Tuning Knobs
- Guardrail: Argon2id ~200–400 ms per hash; benchmark and re-derive `MAX_AUTH_PENDING` and the timeout margin if it differs — ADR-016 Performance

---

## Acceptance Criteria

- [ ] `Konscious.Security.Cryptography.Argon2`, `Npgsql` (current line for .NET 10) and `Dapper` are added as `PackageReference`s with exact versions checked on NuGet on the day; the versions are added to `.claude/docs/technical-preferences.md` Allowed Libraries (gateway only) with the date
- [ ] `GatewayOptions` binds from `appsettings.json` + environment: `GATEWAY_HTTP_PORT` 8080, `GATEWAY_ZONE_LINK_PORT` 7900, `GATEWAY_ZONE_REQUEST_TIMEOUT_MS` 2000, `GATEWAY_EVICT_TIMEOUT_MS` 11000, `GATEWAY_LINK_RETRY_SECONDS` 2, `MAX_AUTH_PENDING` 10; `AuthOptions` binds the GDD knobs: `LOGIN_FAILURE_LIMIT` 5, lockout table {30,60,120,300,600}, Argon2 `Iterations` 2 / `MemorySizeKb` 19456 / `Parallelism` 1, username 3–24, character name 3–16, password 8–72
- [ ] Options validation at startup rejects values outside the GDD safe ranges and the joint constraints (lockout table monotonic; `CHARACTER_NAME_MAX_LENGTH` ≤ 24; `USERNAME_MAX_LENGTH` ≤ 32; min < max; Argon2 iterations ≥ 2 and memory ≥ 19456). `GatewayOptions`: ports 1–65535 and different from each other; timeouts, retry seconds and `MAX_AUTH_PENDING` > 0; `GATEWAY_EVICT_TIMEOUT_MS` > `GATEWAY_ZONE_REQUEST_TIMEOUT_MS`. No other ranges (ADR-016 defines none)
- [ ] AC-AUTH-06: `IPasswordHasher.Hash` returns a PHC string `$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>`; parsed, it has those three parameters and a 16-byte salt; 5 consecutive hashes of the same password have 5 different salts
- [ ] AC-AUTH-07: `Verify(password, Hash(password))` is `true`; `Verify(other, Hash(password))` is `false`
- [ ] `Verify` reads the parameters from the PHC string (so an older-parameter hash still verifies after tuning) and returns `false` — never throws — for a malformed PHC string
- [ ] A dummy hash is computed once at startup with the live parameters (`DummyHash` singleton) for CR-AUTH-9 step 4
- [ ] Benchmark: mean and p95 `Hash` time over 20 runs on the dev machine recorded in Completion Notes; if outside 200–400 ms, flag it (do not change `MAX_AUTH_PENDING` here)

---

## Implementation Notes

*Derived from ADR-016 Decision 1, Decision 7, Key Interfaces:*

```csharp
public interface IPasswordHasher
{
    string Hash(string password);                 // PHC string, 16-byte salt from RandomNumberGenerator
    bool Verify(string password, string phc);     // FixedTimeEquals on the derived hash
}
```

- Salt: `RandomNumberGenerator.GetBytes(16)` (ADR-013 — never `System.Random`). Hash output length 32 bytes. PHC fields base64 without padding (standard PHC encoding).
- Password bytes: UTF-8. No pre-processing, no truncation (bcrypt is rejected precisely for truncation).
- Compare with `CryptographicOperations.FixedTimeEquals`.
- `Hash` / `Verify` are synchronous CPU work; the services (Stories 008/009) call them inside `Task.Run` and the `MAX_AUTH_PENDING` semaphore — not here.
- Never log the password, the PHC string or the derived hash.
- Unit tests may construct the hasher with the real parameters (fast enough for a handful of calls); avoid loops of hundreds.

---

## Out of Scope

- Story 005: validation rules and F-AUTH-1
- Stories 008/009: the `MAX_AUTH_PENDING` semaphore and `Task.Run`
- Story 012: host wiring and `Program.cs` endpoints

---

## QA Test Cases

- **AC-1 (AC-AUTH-06)**: PHC parameters
  - Given: the hasher with default `AuthOptions`
  - When: `Hash("correct horse")` 5 times
  - Then: each string starts `$argon2id$v=19$m=19456,t=2,p=1$`; decoded salt is 16 bytes; the 5 salts are pairwise distinct
- **AC-2 (AC-AUTH-07)**: Round trip
  - Given: `phc = Hash("correct horse")`
  - When: `Verify("correct horse", phc)` and `Verify("correct hors", phc)`
  - Then: `true`, `false`
  - Edge cases: 72-char password and a 73rd-char-different password (no truncation: second is `false`); multibyte password `"пароль123"`
- **AC-3**: Malformed PHC
  - Given: `""`, `"$argon2i$…"`, a PHC with bad base64
  - When: `Verify`
  - Then: `false`, no exception
- **AC-4**: Options validation
  - Given: `CHARACTER_NAME_MAX_LENGTH = 25`; lockout table `{30, 20, …}`; `Iterations = 1`; `GATEWAY_HTTP_PORT = 0`; `GATEWAY_HTTP_PORT = GATEWAY_ZONE_LINK_PORT`
  - When: options validated
  - Then: each fails with a message naming the knob
- **AC-5**: Dummy hash
  - Given: `DummyHash` built from `AuthOptions` with non-default parameters (`Iterations = 3`)
  - When: its PHC is read twice and `Verify` is called with any password
  - Then: the PHC carries `t=3`; `Verify` returns `false` without throwing; both reads return the same instance (computed once)
- **Benchmark**: an `[Explicit]` test (or a short console run) prints the mean and p95 `Hash` time over 20 runs; the result goes in Completion Notes, not into an assertion

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_PasswordHasher_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Gateway_Options_tests.cs`

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: Story 001 (the gateway references `IronGrind.Shared`)
- Unlocks: Story 006, Story 007, Story 008, Story 009

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 8/8 passing (all covered by tests in the two evidence files; package pins checked in the csproj and `technical-preferences.md`)
**Benchmark** (dev machine, Release, 20 runs after one warm-up): mean 68.6 ms, p95 128.5 ms (min 55.3, max 135.4) — **outside the 200–400 ms estimate (faster); flagged, `MAX_AUTH_PENDING` unchanged** (TD-093, re-check after the VPS benchmark in Story 017)
**Deviations** (advisory, accepted):
- Argon2 properties named `Argon2Iterations` / `Argon2MemorySizeKb` / `Argon2Parallelism` (config keys `ARGON2_*`)
- `LockoutTableSeconds` is `int[]?` with `EffectiveLockoutTableSeconds` falling back to the GDD default, because config array binding appends to a non-empty default; a partial env override merges by index with `appsettings.json` (TD-092, Story 012 documents it)
- Konscious rejects an empty password: `Hash("")` throws `ArgumentException`; `Verify("")` returns `false` (found by a code-review test)
- Extra checks: `GATEWAY_EVICT_TIMEOUT_MS` > 0, lockout table non-empty; `Verify` rejects PHC parameters m > 65536 KB (GDD max), t/p > 16, salt outside 8–64 bytes, hash outside 16–64 bytes
**Test Evidence**: Logic — `gateway/IronGrind.Gateway.Tests/Auth_PasswordHasher_tests.cs`, `gateway/IronGrind.Gateway.Tests/Gateway_Options_tests.cs` (63 tests incl. 34 boundary cases, + 1 Explicit benchmark); gateway suite 122/122
**Code Review**: Complete (2026-11-06, APPROVED WITH SUGGESTIONS; suggestions 1–5 applied). Carried to Story 012: host-level `ValidateOnStart` test, full-table override note
