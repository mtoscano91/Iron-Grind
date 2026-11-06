# Story 007: Account repository — login state

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-9 (atomicity), CR-AUTH-10, CR-AUTH-12, F-AUTH-1; AC-AUTH-19 (b, c), 20, 22
**Requirement**: `TR-auth-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 5, Key Interfaces `IAccountRepository`)
**ADR Decision Summary**: Failure counting is one transaction — `SELECT … FOR UPDATE` on the account row, F-AUTH-1 computed in C#, `UPDATE`. The row lock serializes concurrent attempts on one account. `GetCharacterIDAsync` reads `character_records.account_id`.

**Engine**: .NET 10 + PostgreSQL 16 | **Risk**: LOW
**Engine Notes**: `TIMESTAMPTZ` maps to `DateTime` with `Kind=Utc` in Npgsql 6+; pass UTC values only.

**Control Manifest Rules (this layer)**:
- Required: failure counting is one transaction: `SELECT … FOR UPDATE`, F-AUTH-1 in C#, `UPDATE` — ADR-016 Decision 5
- Forbidden: plain read-modify-write of `login_failure_count` without the row lock (CR-AUTH-9 atomicity)
- Required: the `gateway` role reads only `character_id, account_id, last_zone_id` of `character_records`

---

## Acceptance Criteria

- [ ] `FindByUsernameAsync(usernameLower)` returns `AccountRecord { AccountID, PasswordHashPhc, IsBanned, LoginFailureCount, LockoutUntilUtc }` or null
- [ ] `RecordFailureAsync(account, utcNow)` increments `login_failure_count` and sets `lockout_until_utc = LockoutPolicy.LockoutUntil(newCount, utcNow)` (Story 005) in one locked transaction; returns the new `LoginFailureState`
- [ ] AC-AUTH-19 (b, c): with `lockout_until_utc` in the future, one `RecordFailureAsync` raises the count by 1 and resets `lockout_until_utc` to `utcNow + LockoutDuration(new count)` (no "already locked, skip" shortcut)
- [ ] AC-AUTH-20: from count 9 → 10 and `utcNow + 600 s`; from count 50 → 51 and `utcNow + 600 s`
- [ ] `ResetFailuresAsync(account)` sets count 0 and lockout null (AC-AUTH-17's storage half)
- [ ] Concurrency: 10 parallel `RecordFailureAsync` calls on one account from count 0 end at exactly count 10 (no lost update)
- [ ] AC-AUTH-22: `GetCharacterIDAsync(account)` returns the `CharacterID` created at registration; the `accounts` table has no character columns and the character row carries `account_id` (asserted from `information_schema.columns`)
- [ ] `GetCharacterIDAsync` for an account with no character row returns `CharacterID.Invalid` (EC-AUTH-5 is handled by the caller, Story 010)
- [ ] `GetLastZoneIdAsync(account)` returns `character_records.last_zone_id` (for CR-ZI-6 rule 2, used by the gateway epic's router)

---

## Implementation Notes

*Derived from ADR-016 Decision 5 and Key Interfaces:*

```csharp
Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct);
Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct); // row lock, F-AUTH-1
Task ResetFailuresAsync(AccountID account, CancellationToken ct);
Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct);
```

- `RecordFailureAsync`: `BEGIN; SELECT login_failure_count FROM accounts WHERE account_id = @id FOR UPDATE; (C#: n+1, LockoutUntil); UPDATE …; COMMIT;`. F-AUTH-1 stays in C# (`LockoutPolicy`), not SQL.
- `utcNow` is a parameter (injected clock upstream) so tests are deterministic.
- `GetLastZoneIdAsync` is not in ADR-016's interface listing but follows from Decision 4 step 4 ("Rule 2 reads `character_records.last_zone_id`") and the role's grant; add it to `IAccountRepository` with a doc comment citing that.
- Same test fixture and `[Category("Database")]` pattern as Story 006.

---

## Out of Scope

- Story 009: the login flow that decides when to call these
- Gateway epic: the router that uses `last_zone_id`

---

## QA Test Cases

- **AC-1**: Find
  - Given: registered `"alice"`
  - When: `FindByUsernameAsync("alice")`, `("nobody")`
  - Then: record with count 0, not banned, lockout null; null
- **AC-2 (AC-AUTH-19 b,c)**: Failure during lockout
  - Given: count 5, `lockout_until_utc = t0 + 30 s`
  - When: `RecordFailureAsync(acc, t0 + 1 s)`
  - Then: count 6; lockout = `t0 + 1 s + 60 s` (± 1 s)
- **AC-3 (AC-AUTH-20)**: Cap
  - Given: count 9; separately count 50
  - When: `RecordFailureAsync(acc, t)`
  - Then: 10 / 51; lockout `t + 600 s`
- **AC-4**: No lost update
  - Given: count 0
  - When: 10 parallel calls
  - Then: count 10
- **AC-5**: Reset
  - Given: count 4, lockout set
  - When: `ResetFailuresAsync`
  - Then: 0, null
- **AC-6 (AC-AUTH-22)**: Mapping and separation
  - When: `GetCharacterIDAsync` for a registered account and for an account row inserted with no character
  - Then: the registered CharacterID; `CharacterID.Invalid`
  - Edge cases: schema check — `accounts` columns are exactly the seven of migration 004

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_AccountRepository_LoginState_tests.cs` (`[Category("Database")]`), run locally — result recorded in Completion Notes

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003, Story 004, Story 005 (`LockoutPolicy`)
- Unlocks: Story 009, Story 010, Story 011
