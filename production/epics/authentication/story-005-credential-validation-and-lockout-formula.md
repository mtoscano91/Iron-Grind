# Story 005: Credential validation and the lockout formula

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-7, CR-AUTH-8 step 2, CR-AUTH-13, F-AUTH-1, EC-AUTH-9; AC-AUTH-11, 13, 18, 28, 35 (validation cases)
**Requirement**: `TR-auth-???` — *no TR-ID covers CR-AUTH-7 / CR-AUTH-13 / F-AUTH-1 yet (the registry has TR-auth-001 to 005). Epic placeholder; register at the next `/architecture-review`, as the zone-instancing epic did.*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 6 — auth rules live in the gateway only)
**ADR Decision Summary**: Username normalization and validation, character-name validation and F-AUTH-1 are gateway-only code, tested in `gateway/IronGrind.Gateway.Tests/`.

**Engine**: .NET 10 (no Unity) | **Risk**: LOW
**Engine Notes**: None — pure C#.

**Control Manifest Rules (this layer)**:
- Required: auth rules (username normalization, F-AUTH-1, …) live in the gateway only — ADR-016 Decision 6
- Required: knobs come from `AuthOptions` (Story 004), never literals in the rule code

---

## Acceptance Criteria

- [ ] `UsernameRules.Normalize` lowercases with `ToLowerInvariant`; `Validate(normalized)` checks, in order: length (in characters, post-normalization) [`USERNAME_MIN_LENGTH`, `USERNAME_MAX_LENGTH`] → `UsernameTooShort` / `UsernameTooLong`; charset `[a-z0-9_]` ASCII only → `UsernameInvalidChars`
- [ ] AC-AUTH-11: 2-char → `UsernameTooShort`; 25-char → `UsernameTooLong`; `"ab!"` and `"a b"` → `UsernameInvalidChars`; 3-char, 24-char and `"alice_0"` pass
- [ ] AC-AUTH-28: `"аlice"` (U+0430), `"ａlice"` (U+FF41), `"àlice"` (U+00E0), `"日本語"` → `UsernameInvalidChars` (four assertions)
- [ ] `PasswordRules.Validate`: length [`PASSWORD_MIN_LENGTH`, `PASSWORD_MAX_LENGTH`] → `PasswordTooShort` / `PasswordTooLong`. AC-AUTH-13: 7 → `PasswordTooShort`; 8 passes; 72 passes; 73 → `PasswordTooLong`
- [ ] `CharacterNameRules.Validate(characterName, normalizedUsername)` in CR-AUTH-13 order: (1) length in Unicode code points → `CharacterNameTooShort` / `CharacterNameTooLong`; (2) charset `[A-Za-z0-9_]` → `CharacterNameInvalidChars`; (3) equals username case-insensitively → `CharacterNameMatchesUsername`
- [ ] AC-AUTH-35 (validation cases; username `"alice_login"`): 2 chars → `TooShort`; 17 → `TooLong`; `"Bad Name"`, `"Évan"`, `"аlex"` → `InvalidChars`; `"ALICE_LOGIN"` → `MatchesUsername`; 3, 16 and `"Grim_Iron7"` pass *(the `CharacterNameTaken` case is Story 006)*
- [ ] `RegistrationValidator` runs CR-AUTH-8 step 2's order — username, password, character name, then `classType` is a playable class (`ClassInvalid`) — and returns the first failure
- [ ] AC-AUTH-18: `LockoutPolicy.Duration(F)` for F ∈ {0, 1, 4, 5, 6, 7, 8, 9, 15, 100} = {0, 0, 0, 30, 60, 120, 300, 600, 600, 600} s; `LockoutUntil(F, utcNow)` = `utcNow + Duration(F)` (null when 0)

---

## Implementation Notes

*Derived from the GDD rules and ADR-016 Decision 6:*

- Order matters: the first failing check wins. Write one test per boundary so a reordering breaks a test.
- Length in code points: count with `StringInfo`/`Rune` enumeration, not `string.Length` (UTF-16 units). For a username the charset check rejects non-ASCII anyway, but the length check comes first and must count characters, not bytes (EC-AUTH-9).
- `ToLowerInvariant`, never culture-sensitive lowering (Turkish-I).
- F-AUTH-1 is a lookup, not a formula (GDD implementation note): index `F - LOGIN_FAILURE_LIMIT`, clamp to the last table entry; `F < LOGIN_FAILURE_LIMIT` → 0. Use an injected clock value (`DateTime utcNow` parameter) — no `DateTime.UtcNow` inside.
- Playable classes: read `class-system.md` and the existing `ClassType` enum in `src/` for the playable set. The gateway cannot reference Unity code; if `ClassType` is not in a shared folder, copy the playable-set as a gateway-side table and note the duplication (or move the enum to `src/Foundation/Shared/` if it is Unity-free — decide at `/story-readiness`).
- All return `RegistrationError?` (null = valid) or a small result struct; no exceptions for invalid input.

---

## Out of Scope

- Story 006: `UsernameTaken` / `CharacterNameTaken` from the database
- Story 007/009: applying F-AUTH-1 to account rows

---

## QA Test Cases

- **AC-1 (AC-AUTH-11)**: Username boundaries
  - Given: default knobs 3/24
  - When: validating `"ab"`, `new string('a', 25)`, `"ab!"`, `"a b"`, `"abc"`, `new string('a', 24)`, `"alice_0"`
  - Then: TooShort, TooLong, InvalidChars, InvalidChars, ok, ok, ok
- **AC-2 (AC-AUTH-28)**: Homoglyphs
  - Given: the four strings in the AC
  - When: normalized then validated
  - Then: each `UsernameInvalidChars`
  - Edge cases: `"ALICE"` normalizes to `"alice"` and passes (AC-AUTH-10 rule part)
- **AC-3 (AC-AUTH-13)**: Password boundaries
  - When: lengths 7, 8, 72, 73
  - Then: TooShort, ok, ok, TooLong
- **AC-4 (AC-AUTH-35)**: Character names
  - Given: username `"alice_login"`, knobs 3/16
  - When: each name in the AC
  - Then: the listed result
  - Edge cases: `"ab!"` (2 chars + invalid) → TooShort (length first); `"Alice_Login"` → MatchesUsername
- **AC-5**: Validation order
  - Given: a request with a 2-char username, 3-char password, bad name, class 0
  - When: `RegistrationValidator.Validate`
  - Then: `UsernameTooShort` only; fix the username → `PasswordTooShort`; and so on down to `ClassInvalid`
- **AC-6 (AC-AUTH-18)**: Lockout table
  - When: `Duration(F)` for the ten values
  - Then: exactly the listed seconds; `LockoutUntil(7, t)` = `t + 120 s`; `LockoutUntil(3, t)` = null

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_CredentialValidation_tests.cs`
- `gateway/IronGrind.Gateway.Tests/Auth_LockoutPolicy_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (`AuthOptions`)
- Unlocks: Story 008, Story 009
