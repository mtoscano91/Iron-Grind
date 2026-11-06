# Story 006: Account repository — registration

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-3, CR-AUTH-8 steps 4–5, CR-AUTH-12, CR-AUTH-13, EC-AUTH-1, EC-AUTH-4; AC-AUTH-02 (counter half), 04, 12 (a, b), 14, 25, 31, 34 (rows), 35 (`CharacterNameTaken`)
**Requirement**: `TR-auth-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 5, Key Interfaces `IAccountRepository`), with ADR-006
**ADR Decision Summary**: Registration is one transaction — `nextval` both sequences, `INSERT accounts`, `INSERT character_records (character_id, account_id, character_name, class_type)` with all other columns at their DDL defaults. A `23505` is told apart by constraint name: `username_lower` → `UsernameTaken`, `ux_character_records_name_lower` → `CharacterNameTaken`. Sequence values are consumed even on rollback.

**Engine**: .NET 10 + PostgreSQL 16 | **Risk**: LOW
**Engine Notes**: Npgsql exposes the violated constraint as `PostgresException.ConstraintName`. The unique constraint on `username_lower` is the column's inline `UNIQUE` — its generated name is `accounts_username_lower_key`; confirm with `\d accounts` and use a named constant.

**Control Manifest Rules (this layer)**:
- Required: registration is one transaction; `23505` told apart by constraint name — ADR-016 Decision 5
- Required: the gateway connects as `gateway` with its own pool — ADR-016 Decision 5
- Forbidden: application-level read-then-write uniqueness checks (EC-AUTH-1 TOCTOU)

---

## Acceptance Criteria

- [ ] `NpgsqlAccountRepository.RegisterAsync(usernameLower, passwordHashPhc, characterName, classType, ct)` returns `RegistrationOutcome { Success, AccountID, CharacterID, Error }`
- [ ] AC-AUTH-12 (a, b): after success the account row has all CR-AUTH-8 step 4 fields (`is_banned` false, `login_failure_count` 0, `lockout_until_utc` null, `created_at_utc` set), and the character row has `account_id` = the account, `character_name` exactly as sent (case kept), `class_type` as sent, and every other column equal to its DDL default
- [ ] AC-AUTH-02 (counter) / AC-AUTH-04: on a fresh database three registrations get `AccountID` 1, 2, 3; a new repository instance (gateway restart) gets 4
- [ ] AC-AUTH-14: `"alice"` registered; registering `"alice"` again (the caller normalizes `"Alice"`) returns `UsernameTaken`; exactly one `accounts` row has `username_lower = 'alice'`
- [ ] AC-AUTH-25: two concurrent `RegisterAsync` calls for the same username → one success, one `UsernameTaken`, one account row
- [ ] AC-AUTH-35 (taken): `"Grim_Iron7"` registered; `"grim_iron7"` for another username → `CharacterNameTaken`; exactly one character row with `lower(character_name) = 'grim_iron7'`; and no account row was left for the loser
- [ ] AC-AUTH-31: with the fault hook firing between the two inserts: (a) neither row exists; (b) the next successful registration gets an `AccountID` and `CharacterID` higher than the ones the failed attempt took; (c) the outcome is `ServerError`
- [ ] AC-AUTH-34 (rows): one registration per playable class stores that `class_type`
- [ ] Any other database exception → `ServerError` outcome and the exception is logged (never the password hash)

---

## Implementation Notes

*Derived from ADR-016 Decision 5 and Key Interfaces:*

```csharp
Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct);
```

- One `NpgsqlTransaction`: `SELECT nextval('account_id_seq'), nextval('character_id_seq')` → `INSERT accounts` → **fault hook** → `INSERT character_records (character_id, account_id, character_name, class_type)` → commit. Dapper for the statements.
- Fault hook (GAP-5): an injected `IRegistrationFaultHook` (no-op in production) called between the inserts; the test throws from it.
- `uint` IDs: sequences are `BIGINT` capped at `uint.MaxValue`; cast with `checked`.
- Catch `PostgresException { SqlState: "23505" }` and map by `ConstraintName`; an unknown constraint name → `ServerError` + log.
- `NpgsqlDataSource` built once from the `gateway` connection string (config); the repository takes it by constructor.
- Tests: `[Category("Database")]`, each test in its own throwaway schema or with `TRUNCATE accounts, character_records … RESTART IDENTITY`-equivalent reset of the sequences (`ALTER SEQUENCE … RESTART`) — the reset needs a privileged connection, not the `gateway` role; keep that in the test fixture only.

---

## Out of Scope

- Story 007: login-side repository methods
- Story 008: validation and hashing before the repository call

---

## QA Test Cases

- **AC-1 (AC-AUTH-12 a,b)**: Rows written
  - Given: empty tables, sequences restarted
  - When: `RegisterAsync("alice", phc, "Grim_Iron7", 1)`
  - Then: success, AccountID 1, CharacterID 1; account and character rows as listed; character's other columns equal a row inserted with only the four columns (compare against DDL defaults read from `information_schema.columns`)
- **AC-2 (AC-AUTH-04)**: Monotonic and durable
  - When: three registrations; dispose the data source; new repository; a fourth
  - Then: AccountIDs 1, 2, 3, 4
- **AC-3 (AC-AUTH-14, 25)**: Username taken
  - When: sequential duplicate; then two `Task.WhenAll` registrations of `"bob"`
  - Then: `UsernameTaken`; exactly one success + one `UsernameTaken`; one row each time
- **AC-4 (AC-AUTH-35 taken)**: Character name taken
  - When: `"Grim_Iron7"` then `"grim_iron7"` under a different username
  - Then: `CharacterNameTaken`; one character row; the second username has no account row
- **AC-5 (AC-AUTH-31)**: Fault between inserts
  - Given: fault hook throws on the next call; sequences at known values
  - When: `RegisterAsync`
  - Then: `ServerError`; no rows; next successful registration's AccountID and CharacterID are both greater than the values consumed
- **AC-6 (AC-AUTH-34 rows)**: Class stored
  - When: one registration per playable class
  - Then: `class_type` equals the request

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Auth_AccountRepository_Registration_tests.cs` (`[Category("Database")]`), run locally — result recorded in Completion Notes

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (migration 004, role), Story 004 (Npgsql/Dapper pinned)
- Unlocks: Story 008
