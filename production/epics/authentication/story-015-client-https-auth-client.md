# Story 015: Client HTTPS auth client

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 6 h

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-6, EC-AUTH-3, EC-AUTH-10 (gateway down), UI Requirements (error categories); AC-AUTH-32 (a); `design/gdd/auth-wire-messages.md` Transport, `ServerStarting` retry knobs
**Requirement**: `TR-auth-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 2, Decision 6, Verification item 3)
**ADR Decision Summary**: The client posts the shared-codec bodies with `UnityWebRequest` over HTTPS, default certificate validation and `timeout = LOGIN_HTTP_TIMEOUT_SECONDS` (20 s). A timeout, transport error or non-200 is `ServerError`. `ServerStarting` means retry. No certificate handler that accepts everything may ship; a dev-only handler for Caddy's `tls internal` may live only in `IronGrind.DevHarness`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — `UnityWebRequest` HTTPS POST with a binary body on iOS under App Transport Security (ADR-016 Verification 3)
**Engine Notes**: `UnityWebRequest.timeout` is in whole seconds. Use `UploadHandlerRaw` + `DownloadHandlerBuffer`, set `Content-Type: application/octet-stream`. Check `docs/engine-reference/unity/` for any 6.x `UnityWebRequest` change before coding.

**Control Manifest Rules (this layer)**:
- Required: the HTTPS login and registration client is in `src/Client/Auth/`; `UnityWebRequest`, default certificate validation, explicit `timeout` of `LOGIN_HTTP_TIMEOUT_SECONDS`; timeout / transport error / non-200 → `ServerError`; `ServerStarting` → retry — ADR-016 Decisions 2, 6 (Presentation rules)
- Required: client builds use HTTPS only; Player Settings keep HTTP downloads disallowed — ADR-016 Decision 2
- Forbidden: a `CertificateHandler` that accepts every certificate in any shipping assembly; pre-hashing the password on the client (CR-AUTH-6)

**Performance**: no performance impact — outside the gameplay loop, one HTTPS request per login attempt (plus at most `LOGIN_SERVER_STARTING_MAX_RETRIES` retries).

---

## Acceptance Criteria

- [ ] `AuthClient` in `src/Client/Auth/` (assembly `IronGrind.Client`) is plain C# with callbacks (the client has no async or coroutine style yet): `Login(string username, string password, byte[] token, Action<LoginResponse> onDone, Action onServerStarting)` and `Register(string username, string password, string characterName, byte classType, Action<RegistrationResult> onDone)`, using the shared codec types. `onDone` is called exactly once per call. On the token path (`token` non-zero) the password is sent as `""` (empty, never null — `auth-wire-messages.md`); a null `token` means all-zeros (credential path)
- [ ] The HTTP call goes through an `IAuthHttpTransport` seam: `Post(string url, byte[] body, int timeoutSeconds, Action<AuthHttpResult> onDone)`, where `AuthHttpResult` carries an outcome {`Ok`, `HttpError`, `TransportError`, `Timeout`}, the status code and the body bytes. Real: `UnityWebRequestAuthTransport`, a `MonoBehaviour` running `UnityWebRequest` in a coroutine (`UploadHandlerRaw` + `DownloadHandlerBuffer`, `Content-Type: application/octet-stream`). Test: a fake that answers on demand, so EditMode tests need no network
- [ ] The `ServerStarting` wait goes through an `IAuthRetryScheduler` seam: `Schedule(int delaySeconds, Action callback)`. Real: a coroutine wait (can live on the same `MonoBehaviour` as the transport). Test: a fake the test fires by hand — no real-time waits in tests
- [ ] Config is a new `AuthClientConfig` `ScriptableObject` in `src/Client/Auth/`: `GatewayBaseUrl`, `LoginHttpTimeoutSeconds` (`LOGIN_HTTP_TIMEOUT_SECONDS`, 20), `ServerStartingRetrySeconds` (`LOGIN_SERVER_STARTING_RETRY_SECONDS`, 5), `ServerStartingMaxRetries` (`LOGIN_SERVER_STARTING_MAX_RETRIES`, 8) — no literals in `AuthClient`. `Validate()` rejects a URL that is not `https://` (and non-positive values) with a clear error, and `AuthClient`'s constructor calls it. No `.asset` instance is part of this story
- [ ] Mapping: 200 + decodable body → that body; 200 + undecodable body, non-200, transport error, or timeout → `LoginResponse { success:false, errorCode:ServerError }` / `RegistrationResult { ServerError }`
- [ ] AC-AUTH-32 (a): a request that exceeds the timeout reports `ServerError`
- [ ] `ServerStarting`: the same `ConnectionRequest` is resent after `LOGIN_SERVER_STARTING_RETRY_SECONDS`, at most `LOGIN_SERVER_STARTING_MAX_RETRIES` times; then `ServerError`; a callback reports "server starting" status while retrying
- [ ] The password is sent as entered (UTF-8 plaintext in the body) — no hashing, no trimming
- [ ] *(added 2026-11-06, Story 005 readiness; revised at Story 015 readiness)* A field over 255 UTF-8 bytes (`AuthBodyCodec.MAX_STRING_BYTES`) is rejected before sending, with no request made and `onDone` called at once. **Register**: username → `UsernameTooLong`, password → `PasswordTooLong`, character name → `CharacterNameTooLong`. **Login**: username or password → `LoginResponse { success:false, InvalidCredentials }` (`AuthFailureReason` has no length code; registration can never create such an account, so the answer is truthful). Test case: a 72-code-point password of 4-byte characters (288 bytes)
- [ ] No `CertificateHandler` subclass exists in `IronGrind.Client` (an EditMode architecture test reflects over the client assembly's types); Player Settings "Allow downloads over HTTP" is "Not allowed" (asserted from `PlayerSettings.insecureHttpOption` in an EditMode test; currently `0` = `NotAllowed` in `ProjectSettings.asset`)
- [ ] *(moved 2026-11-06, Story 015 readiness)* ADR-016 Verification 3 (iOS device build logs in over TLS with default validation, App Transport Security) is **not** in this story: it needs a publicly trusted certificate, which only exists with the VPS run — moved to Story 017. Nothing to verify here

---

## Implementation Notes

*Derived from ADR-016 Decision 2:*

- Body bytes come from the shared `AuthBodyCodec` (Story 002). No JSON.
- Retry loop lives in `AuthClient`, not in the transport; the transport does one request. The retry resends the same encoded body bytes.
- Engine check before coding: `docs/engine-reference/unity/` has no `UnityWebRequest` entry — verify `UploadHandlerRaw`, `DownloadHandlerBuffer`, `timeout` (whole seconds) and `UnityWebRequest.Result` against the Unity 6.3 manual. Map a timeout to `Timeout` only if the 6.3 API gives a reliable signal for it; otherwise report it as `TransportError` — both end as `ServerError`, so the client behaviour is the same.
- Dev: if the Editor cannot trust Caddy's `tls internal` root, the dev-only certificate handler lives only in `IronGrind.DevHarness` and is injected through the transport seam in dev scenes — never referenced by `IronGrind.Client`.
- Login and registration screens are not in this story (they wait for `design/ux/login-screen.md` / `registration-screen.md`). A minimal DevHarness button to drive a login is fine for manual verification.
- Never log the password or the token.

---

## Out of Scope

- Login and registration screens (UX specs first — `/ux-design`)
- Story 016: Caddy
- Story 017: connecting to the zone with the returned token

---

## QA Test Cases

- **AC-1**: Mapping
  - Given: fake transport returning (200, success body), (200, `InvalidCredentials`), (200, garbage), (500), (transport error), (timeout)
  - When: `Login`
  - Then: success; `InvalidCredentials`; `ServerError` ×4
- **AC-2 (AC-AUTH-32 a)**: Timeout
  - Given: fake transport that reports a timeout
  - Then: `ServerError`
- **AC-3**: ServerStarting retry
  - Given: fake returns `ServerStarting` 3 times then success; fake retry scheduler fired by the test (each scheduled delay = `ServerStartingRetrySeconds`)
  - Then: 4 identical request bodies sent; status callback "starting" ×3; final success
  - Edge cases: `ServerStarting` 9 times with max 8 → 9 requests total then `ServerError`
- **AC-4**: HTTPS-only config
  - Given: base URL `http://…`
  - Then: `AuthClientConfig.Validate()` throws with a message naming the URL; `AuthClient` cannot be constructed with it
- **AC-5**: No trust-all handler / HTTP disallowed
  - When: reflect over `IronGrind.Client` types; read `PlayerSettings.insecureHttpOption`
  - Then: no `CertificateHandler` subclass; `NotAllowed`
- **AC-6**: Oversized fields
  - Given: a 72-code-point password of 4-byte characters (288 bytes); a 256-byte username; a 256-byte character name
  - When: `Register` with each; `Login` with the long password and with the long username
  - Then: `PasswordTooLong`, `UsernameTooLong`, `CharacterNameTooLong`; `Login` → `InvalidCredentials` both times; the fake transport received 0 requests
- **AC-7**: Plaintext password and token path
  - Given: password `"  pa ss  "`; separately a non-zero token
  - Then: the decoded request body has the password byte-for-byte unchanged; on the token path the decoded password is `""` and the token field equals the token

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Client/Auth/AuthClient_tests.cs`
- (the iOS device evidence moved to Story 017)

**Status**: [x] Created — 54 tests, all passing (2026-11-06)

---

## Dependencies

- Depends on: Story 002 (codec) — Complete. None other (the device check moved to Story 017)
- Unlocks: Story 017

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 10/10 passing (ADR-016 Verification 3 — iOS device check — moved to Story 017 at readiness)
**Deviations**: None. Beyond the ACs (from /code-review): the real transport follows no redirects (`redirectLimit = 0`) and keeps the exactly-once contract when its host is inactive, disabled or destroyed (in-flight posts → `TransportError`, pending waits run). Mid-request disable is not automated (needs Play mode).
**Test Evidence**: Integration: `tests/EditMode/Client/Auth/AuthClient_tests.cs` — 54 tests pass; full EditMode suite 6673 total, 0 failed, 129 skipped (Database category)
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, all 7 suggestions applied
