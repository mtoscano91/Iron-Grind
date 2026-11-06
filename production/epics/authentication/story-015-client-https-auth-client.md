# Story 015: Client HTTPS auth client

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06

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

---

## Acceptance Criteria

- [ ] `AuthClient` in `src/Client/Auth/` (assembly `IronGrind.Client`) exposes `Login(username, password, token)` and `Register(...)` returning a `LoginResponse` / `RegistrationResult` (as the shared codec types) via a coroutine/callback or awaitable that fits the client's existing style
- [ ] The HTTP call goes through an `IAuthHttpTransport` seam (real: `UnityWebRequest`; test: fake) so EditMode tests need no network
- [ ] Gateway base URL and `LOGIN_HTTP_TIMEOUT_SECONDS`, `LOGIN_SERVER_STARTING_RETRY_SECONDS` (5), `LOGIN_SERVER_STARTING_MAX_RETRIES` (8) come from client config (ScriptableObject or existing config), not literals; the URL must be `https://` (an `http://` URL is rejected at load)
- [ ] Mapping: 200 + decodable body → that body; 200 + undecodable body, non-200, transport error, or timeout → `LoginResponse { success:false, errorCode:ServerError }` / `RegistrationResult { ServerError }`
- [ ] AC-AUTH-32 (a): a request that exceeds the timeout reports `ServerError`
- [ ] `ServerStarting`: the same `ConnectionRequest` is resent after `LOGIN_SERVER_STARTING_RETRY_SECONDS`, at most `LOGIN_SERVER_STARTING_MAX_RETRIES` times; then `ServerError`; a callback reports "server starting" status while retrying
- [ ] The password is sent as entered (UTF-8 plaintext in the body) — no hashing, no trimming
- [ ] No `CertificateHandler` subclass exists in `IronGrind.Client` (an EditMode architecture test greps the client assembly's types); Player Settings "Allow downloads over HTTP" is "Not allowed" (asserted from `PlayerSettings.insecureHttpOption` in an EditMode test)
- [ ] ADR-016 Verification 3: an iOS device build logs in through Caddy over TLS with default validation (needs Story 016's production-like certificate — Let's Encrypt on the VPS, or a publicly trusted test certificate); result recorded in `production/qa/evidence/auth-client-ios-ats-evidence.md`

---

## Implementation Notes

*Derived from ADR-016 Decision 2:*

- Body bytes come from the shared `AuthBodyCodec` (Story 002). No JSON.
- Retry loop lives in `AuthClient`, not in the transport; the transport does one request.
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
  - Given: fake returns `ServerStarting` 3 times then success; retry seconds set small for the test (or a fake clock)
  - Then: 4 identical request bodies sent; status callback "starting" ×3; final success
  - Edge cases: `ServerStarting` 9 times with max 8 → 9 requests total then `ServerError`
- **AC-4**: HTTPS-only config
  - Given: base URL `http://…`
  - Then: config load fails with a clear error
- **AC-5**: No trust-all handler / HTTP disallowed
  - When: reflect over `IronGrind.Client` types; read `PlayerSettings.insecureHttpOption`
  - Then: no `CertificateHandler` subclass; `NotAllowed`
- **AC-6 (Verification 3)**: iOS device
  - Setup: iOS build, gateway + Caddy with a publicly trusted certificate
  - Verify: a DevHarness login returns success
  - Pass condition: `LoginResponse.success = true` logged on device; no ATS error in the Xcode console

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Client/Auth/AuthClient_tests.cs`
- `production/qa/evidence/auth-client-ios-ats-evidence.md` (device run)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (codec); device check also needs Story 012 and Story 016
- Unlocks: Story 017
