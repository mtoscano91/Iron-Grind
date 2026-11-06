# Story 002: Auth body codec

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours (4 bodies + codec, golden-byte, round-trip, malformed-input, string-limit and reflection tests, mirrored into the gateway test project)

## Context

**GDD**: `design/gdd/auth-wire-messages.md` (schemas, body lengths); `design/gdd/authentication.md` — CR-AUTH-2, CR-AUTH-8, CR-AUTH-9, AC-AUTH-30
**Requirement**: `TR-auth-002` (AccountID type, CR-AUTH-2); AC-AUTH-30 (no AccountID in any client body)
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

- [x] `src/Foundation/Shared/Auth/` holds `ConnectionRequest`, `RegistrationRequest`, `LoginResponse`, `RegistrationResult`, `AuthFailureReason : byte { None=0, InvalidCredentials=1, ServerStarting=2, ServerError=255 }` and `RegistrationError : byte { UsernameTaken=0 … CharacterNameMatchesUsername=11, ServerError=255 }` exactly as `auth-wire-messages.md` lists them
- [x] `AuthBodyCodec` encodes and decodes all four bodies; each round-trips field-for-field
- [x] AC-AUTH-30: `LoginResponse` has no `accountId` field (asserted by reflection over its public fields/properties) and the encoded success body is exactly **28 bytes**; the failure body is exactly **2 bytes**
- [x] `tokenField` is exactly 16 bytes on encode; a `ConnectionRequest` with a token of any other length fails to encode (argument exception)
- [x] Decoding a truncated body, a string length prefix past the end, or trailing bytes returns a failure result (`TryDecode… → false`) — never throws, never reads out of range
- [x] Strings follow the wire-protocol limit (`networking-wire-protocol.md`: `ushort` byte count + UTF-8, max 255 bytes): encoding a string whose UTF-8 form exceeds 255 bytes throws `ArgumentException`; decoding a length prefix > 255 returns `false` (even if enough bytes follow)
- [x] The byte layouts are pinned by golden-byte tests (one per body) — `auth-wire-messages.md` says the first authentication story pins them
- [x] The same tests pass in Unity EditMode and in the gateway test project (`dotnet test`)

---

## Implementation Notes

*Derived from ADR-016 Decision 2 and `auth-wire-messages.md`:*

- Field order is the schema order. `success` 1 byte (0/1), `errorCode` 1 byte. A `LoginResponse` with `success=false` encodes only those 2 bytes; with `success=true` it adds `sessionToken` (16) + `characterId` (uint32 LE) + `zoneId` (uint32 LE) + `zoneServerPort` (uint16 LE).
- Strings: `ushort` LE byte count + UTF-8 bytes, no terminator, max 255 bytes (`networking-wire-protocol.md` field rules). The limit is in bytes, not characters: a 72-character password (`PASSWORD_MAX_LENGTH`) of 4-byte characters is 288 bytes and fails to encode. Open item for Story 005 / Story 015: string limits must also hold in UTF-8 bytes (≤ 255), or the client must check the byte length before sending — otherwise the user sees a failed request instead of `PasswordTooLong`. `password` is `""` on the token path, never null.
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
- **AC-6**: String byte limit
  - Given: a username of exactly 255 UTF-8 bytes; one of 256 ASCII bytes; a password of 64 four-byte characters (256 bytes); separately, a valid body whose string length prefix is patched to 256 with 256 bytes following
  - When: encoded / decoded
  - Then: 255 bytes encodes and round-trips; both 256-byte strings throw `ArgumentException`; the patched body decodes to `false`, no exception
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

**Status**: [x] Created and passing (2026-11-06) — 56 tests per file. The gateway file is a **copy** of the Unity file (only the `namespace` line differs; regenerate it with `sed` after any test change), not a linked file.
**Also changed**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` — the 7 new `IronGrind.Authentication` types added to the ADR-012 shared allow-list (consumer: Story 015 client auth client, planned).

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 012, Story 015

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 8/8 passing (none deferred)
**Deviations**: None. Out of the stated file list (valid, required by the ADR-012 boundary test): `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` — 7 `IronGrind.Authentication` allow-list entries. Carried open item: `PASSWORD_MAX_LENGTH` (72 chars) can exceed the 255-byte wire limit in UTF-8 — Story 005 / Story 015 must bound the byte length.
**Test Evidence**: Logic — `tests/EditMode/Authentication/Authentication_BodyCodec_tests.cs` (56/56; EditMode suite 6580 total, 6451 passed, 0 failed, 129 skipped) and `gateway/IronGrind.Gateway.Tests/Auth_BodyCodec_tests.cs` (`dotnet test` 59/59). The gateway file is a **copy** (only the `namespace` line differs), not a linked file.
**Code Review**: Complete (`/code-review` 2026-11-06, APPROVED WITH SUGGESTIONS, suggestions 1/2/3/5 applied; LP-CODE-REVIEW skipped — lean)
