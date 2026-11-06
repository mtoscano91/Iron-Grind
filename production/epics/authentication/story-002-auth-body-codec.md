# Story 002: Auth body codec

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/auth-wire-messages.md` (schemas, body lengths); `design/gdd/authentication.md` — CR-AUTH-2, CR-AUTH-8, CR-AUTH-9, AC-AUTH-30
**Requirement**: `TR-auth-002` (AccountID never in a client body)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 2, Decision 6)
**ADR Decision Summary**: Login and registration bodies are HTTPS `application/octet-stream` bodies encoded with `networking-wire-protocol.md` field rules; the schemas and one codec live in `src/Foundation/Shared/` so the iOS client and the gateway share one implementation. `LoginResponse` merges the retired `ConnectionAccepted` + `LoginResult` and never carries `AccountID`.

**Engine**: Unity 6.3 LTS + .NET 10 | **Risk**: LOW
**Engine Notes**: Shared code — netstandard2.1, C# 9, no Unity references (Story 001's project enforces it). Use `System.Buffers.Binary.BinaryPrimitives` / `System.Text.Encoding.UTF8`, both in .NET Standard 2.1.

**Control Manifest Rules (this layer)**:
- Required: bodies `ConnectionRequest` / `RegistrationRequest` → `LoginResponse` / `RegistrationResult`, little-endian integers, `bool` as one byte, length-prefixed UTF-8, through one shared codec — ADR-016 Decision 2
- Required: `LoginResponse` = `success`, `errorCode`, then on success `sessionToken[16]`, `characterId`, `zoneId`, `zoneServerPort`; `AccountID` is never a field of any client body — ADR-016 Decision 2
- Forbidden: credentials or `AccountID` over NGO — Global rules

---

## Acceptance Criteria

- [ ] `src/Foundation/Shared/Auth/` holds `ConnectionRequest`, `RegistrationRequest`, `LoginResponse`, `RegistrationResult`, `AuthFailureReason : byte { None=0, InvalidCredentials=1, ServerStarting=2, ServerError=255 }` and `RegistrationError : byte { UsernameTaken=0 … CharacterNameMatchesUsername=11, ServerError=255 }` exactly as `auth-wire-messages.md` lists them
- [ ] `AuthBodyCodec` encodes and decodes all four bodies; each round-trips field-for-field
- [ ] AC-AUTH-30: `LoginResponse` has no `accountId` field (asserted by reflection over its public fields/properties) and the encoded success body is exactly **28 bytes**; the failure body is exactly **2 bytes**
- [ ] `tokenField` is exactly 16 bytes on encode; a `ConnectionRequest` with a token of any other length fails to encode (argument exception)
- [ ] Decoding a truncated body, a string length prefix past the end, or trailing bytes returns a failure result (`TryDecode… → false`) — never throws, never reads out of range
- [ ] The byte layouts are pinned by golden-byte tests (one per body) — `auth-wire-messages.md` says the first authentication story pins them
- [ ] The same tests pass in Unity EditMode and in the gateway test project (`dotnet test`)

---

## Implementation Notes

*Derived from ADR-016 Decision 2 and `auth-wire-messages.md`:*

- Field order is the schema order. `success` 1 byte (0/1), `errorCode` 1 byte. A `LoginResponse` with `success=false` encodes only those 2 bytes; with `success=true` it adds `sessionToken` (16) + `characterId` (uint32 LE) + `zoneId` (uint32 LE) + `zoneServerPort` (uint16 LE).
- Strings: follow the length-prefix rule in `networking-wire-protocol.md` (check the prefix width there; do not invent one). UTF-8, no terminator. `password` is `""` on the token path, never null.
- Decoders take `ReadOnlySpan<byte>` or `byte[]` + length and return `bool` with an `out` body. A decoded `success=false` `LoginResponse` with extra bytes is malformed.
- Keep the codec allocation-light but clarity first: these run once per login, not per tick.
- `characterId` / `zoneId` are carried as `CharacterID` / `ZoneID` (Story 001 moved both into `src/Foundation/Shared/`).
- No message-type ID or channel header — the endpoint identifies the body.
- Unity test folder: `tests/EditMode/Authentication/`. Gateway tests: compile the same test cases against the shared project (copy is fine; or link the file — note which in Completion Notes).

---

## Out of Scope

- Story 012: the HTTP endpoints that use the codec
- Story 015: the client that sends the bodies
- Gateway epic: the gateway-link codec

---

## QA Test Cases

- **AC-1**: Round-trip
  - Given: one instance of each body with non-default values (username `"alice_0"`, password `"pässwörd"` (multibyte), characterName `"Grim_Iron7"`, classType 3, token 0x01..0x10, characterId 77, zoneId 5, port 7777)
  - When: encoded then decoded
  - Then: every field equal
  - Edge cases: empty password `""`; all-zero token
- **AC-2 (AC-AUTH-30)**: No AccountID, fixed lengths
  - Given: `LoginResponse` success and failure instances
  - When: encoded; type reflected
  - Then: success length 28, failure length 2; no member named `accountId`/`AccountId`/`AccountID` and no member of type `AccountID`
- **AC-3**: Golden bytes
  - Given: fixed instances
  - When: encoded
  - Then: exact expected byte arrays (literal in the test — the bytes are the point)
- **AC-4**: Malformed input
  - Given: each body's valid encoding truncated at every length 0..n-1, plus one with a string length prefix of `int.MaxValue`/max, plus one with a trailing byte
  - When: decoded
  - Then: `false`, no exception
- **AC-5**: Token length
  - Given: a `ConnectionRequest` with a 15-byte and a 17-byte token
  - When: encoded
  - Then: `ArgumentException`

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/Authentication/Authentication_BodyCodec_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Auth_BodyCodec_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 012, Story 015
