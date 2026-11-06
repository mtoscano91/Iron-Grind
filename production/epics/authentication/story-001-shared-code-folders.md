# Story 001: Shared code folders and `IronGrind.Shared`

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours (moves with `.meta`, comparer + tests, shared `.csproj`, one Unity-reference build check, full EditMode run)

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-2, AC-AUTH-02, AC-AUTH-03
**Requirement**: `TR-auth-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 6; Migration Plan step 6), with ADR-012 (assembly boundary, note 2026-11-06)
**ADR Decision Summary**: Files that both Unity and the .NET 10 gateway compile live in two source folders split by ADR-012's boundary — `src/Foundation/Shared/` (ships in the client) and `src/ServerLogic/Gateway/Shared/` (server only) — and a small `netstandard2.1` / C# 9 project, `gateway/IronGrind.Shared/`, compiles both so the gateway can reference them and so a Unity-only API in a shared file fails the gateway build.

**Engine**: Unity 6.3 LTS + .NET 10 SDK 10.0.403 | **Risk**: LOW
**Engine Notes**: Unity's `apiCompatibilityLevel` is 6 (.NET Standard 2.1) — the shared project targets the same surface. Moving a `.cs` file must move its `.meta` file with it, or Unity regenerates the GUID.

**Control Manifest Rules (this layer)**:
- Required: shared source compiled by Unity and by `gateway/IronGrind.Shared/` (`netstandard2.1`, `<LangVersion>9.0</LangVersion>`, no `.csproj` in the folders); `src/Foundation/Shared/` holds `CharacterID`, `ZoneID` and the client-facing auth bodies; `src/ServerLogic/Gateway/Shared/` holds the link codec and `AccountID`. Moved types keep their namespaces and `.meta` files; `AccountID`'s shared allow-list entry is removed when it moves — ADR-016 Decision 6, ADR-012
- Forbidden: `UnityEngine` / `Unity.*` references, APIs above .NET Standard 2.1, or C# above 9 in either shared folder
- Guardrail: shared-code drift is caught by the gateway build, which CI runs on every push

---

## Acceptance Criteria

*From GDD `design/gdd/authentication.md` and ADR-016 Decision 6 / Validation Criteria, scoped to this story:*

- [x] `CharacterID` (`src/Foundation/Currency/CharacterID.cs`) and `ZoneID` (`src/Foundation/CharacterPersistence/ZoneID.cs`) are moved to `src/Foundation/Shared/` with their `.meta` files; namespaces unchanged (`IronGrind.Currency`, `IronGrind.CharacterPersistence`)
- [x] `AccountID` (`src/Foundation/CharacterPersistence/AccountID.cs`) is moved to `src/ServerLogic/Gateway/Shared/` with its `.meta` file; namespace unchanged; the `IronGrind.CharacterPersistence.AccountID` entry is removed from `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`, and the assembly-boundary tests still pass
- [x] `AccountID` gets a nested `AccountIDComparer : IEqualityComparer<AccountID>` (CR-AUTH-2, IL2CPP-safe `Dictionary` keys)
- [x] AC-AUTH-02: `new AccountID(0)` equals `AccountID.Invalid` through `AccountIDComparer` *(the "first counter allocation is 1" half is Story 006)*
- [x] AC-AUTH-03: two `AccountID`s with the same `uint` are equal through `AccountIDComparer` with equal hash codes; different values are not equal
- [x] `gateway/IronGrind.Shared/IronGrind.Shared.csproj` (`netstandard2.1`, `<LangVersion>9.0</LangVersion>`, `<Compile Include="../../src/Foundation/Shared/**/*.cs" />` and `<Compile Include="../../src/ServerLogic/Gateway/Shared/**/*.cs" />`) is in `IronGrind.Gateway.sln`; `IronGrind.Gateway` references it; `dotnet test gateway/IronGrind.Gateway.sln` passes
- [x] ADR-016 Validation: a shared file that references `UnityEngine` makes `dotnet build` of the shared project fail (shown once, then reverted — recorded in Completion Notes)
- [x] The Unity project compiles and the full EditMode suite passes after the moves
- No runtime performance impact expected — file moves, a comparer class and a build-only project; no tick-path code changes.

---

## Implementation Notes

*Derived from ADR-016 Decision 6 and Migration Plan step 6:*

- Move with `git mv` for both `.cs` and `.cs.meta`. Do not create `.asmdef` files in `Shared/` folders: the files stay in their current assemblies (`IronGrind.Foundation`, `IronGrind.ServerLogic`) on the Unity side.
- `AccountID` leaves `IronGrind.Foundation` for `IronGrind.ServerLogic`. ADR-016 checked on 2026-11-06 that only `src/ServerLogic/` and the EditMode test assembly (which references `IronGrind.ServerLogic`) use it — re-grep `src/Foundation` and `src/Client` before moving; any hit stops the story.
- `AccountIDComparer` is a nested `public sealed class` with a static `Instance`; `Equals` compares `RawValue`, `GetHashCode` returns `RawValue.GetHashCode()`. Doc comments on all public members.
- The shared files must compile as C# 9 / netstandard2.1: no file-scoped namespaces, no `record struct`, no `required`, no `System.Text.Json`. Check `CharacterID.cs` and `ZoneID.cs` compile unchanged.
- `IronGrind.Gateway.csproj` gets `<ProjectReference Include="../IronGrind.Shared/IronGrind.Shared.csproj" />`. The test project reaches it transitively.
- The Unity-reference check: add `using UnityEngine;` to a shared file, run `dotnet build gateway/IronGrind.Shared/IronGrind.Shared.csproj`, record the error, revert.
- `.gitignore` already tracks `gateway/**/*.csproj` (2026-11-06).

---

## Out of Scope

- Story 002: the auth body schemas and codec in `src/Foundation/Shared/`
- Gateway epic: the gateway-link codec in `src/ServerLogic/Gateway/Shared/`

---

## QA Test Cases

- **AC-1 (AC-AUTH-02)**: Invalid sentinel
  - Given: `var a = new AccountID(0u)`
  - When: compared via `AccountID.AccountIDComparer.Instance.Equals(a, AccountID.Invalid)`
  - Then: `true`; also `a == AccountID.Invalid`
  - Edge cases: `new AccountID(1u)` is not equal to `Invalid`
- **AC-2 (AC-AUTH-03)**: Equality and hash
  - Given: `new AccountID(42u)` twice, and `new AccountID(43u)`
  - When: compared via the comparer
  - Then: 42/42 equal with equal `GetHashCode`; 42/43 not equal
  - Edge cases: `uint.MaxValue` round-trips through `RawValue`
- **AC-3**: Dictionary use
  - Given: `new Dictionary<AccountID, int>(AccountID.AccountIDComparer.Instance)` with key `AccountID(7)`
  - When: looked up with a fresh `new AccountID(7u)`
  - Then: found
- **AC-4**: Shared build
  - Given: the moved files
  - When: `dotnet test gateway/IronGrind.Gateway.sln`
  - Then: passes, and a gateway test can construct `AccountID`, `CharacterID` and `ZoneID`
- **AC-5**: Boundary
  - Given: the allow-list entry removed
  - When: EditMode `AssemblyBoundary_tests` run
  - Then: pass

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/Authentication/Authentication_AccountID_tests.cs` (AC-1 to AC-3, Unity side)
- `gateway/IronGrind.Gateway.Tests/Shared_Types_tests.cs` (AC-4, gateway side)

**Status**: [x] Created and passing (2026-11-06)

---

## Dependencies

- Depends on: None (ADR-016 Migration Plan step 4 done 2026-11-06)
- Unlocks: Story 002, Story 004

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 8/8 passing (none deferred)
**Deviations**: None. TR-auth-002's monotonic counter half is Story 006 by design. Extra file touched: `tests/EditMode/CharacterPersistence/CharacterPersistence_Contract_tests.cs` (old assertion expected `AccountID` in Foundation; split into `AccountID_LivesInServerLogicAssembly`).
**Test Evidence**: Logic — `tests/EditMode/Authentication/Authentication_AccountID_tests.cs` (9 methods, 11 cases) and `gateway/IronGrind.Gateway.Tests/Shared_Types_tests.cs` (2 tests). `dotnet test gateway/IronGrind.Gateway.sln`: 3/3. Full EditMode: 6520 total, 6391 passed, 0 failed, 129 skipped.
**Unity-reference check (ADR-016 Validation)**: `using UnityEngine;` added to `ZoneID.cs` → `dotnet build` of `IronGrind.Shared` failed with `ZoneID.cs(1,7): error CS0246: The type or namespace name 'UnityEngine' could not be found`; reverted.
**Code Review**: Complete — APPROVED WITH SUGGESTIONS (2026-11-06); `.sln` x64/x86 platform noise and BOM trimmed. LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean).
