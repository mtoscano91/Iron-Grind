# Test Infrastructure

**Engine**: Unity 6.3 LTS (6000.4)
**Test Framework**: Unity Test Framework (NUnit, built-in)
**CI**: `.github/workflows/tests.yml`
**Setup date**: 2026-06-27

## Directory Layout

```
tests/
  EditMode/    # Unit tests — pure logic, no Play Mode entry required
  PlayMode/    # Integration tests — coroutines, physics, cross-system
  smoke/       # Critical path checklist for /smoke-check gate
  evidence/    # Screenshot logs and manual test sign-off records
```

## Running Tests

**In Editor**: Window → General → Test Runner → Run All

**Headless (CI)**:
```
unity-test-runner@v4 (editmode + playmode — see .github/workflows/tests.yml)
```

## Test Naming

- **Files**: `[System][Feature]Test.cs`
- **Classes**: `[System][Feature]Test`
- **Methods**: `[Scenario]_[ExpectedResult]()`
- **Example**: `CombatDamageTest.cs` → `BaseAttack_ReturnsDamageMatchingFormula()`

## Assembly Definitions

Each test directory needs a `.asmdef` to isolate tests from game code:

- `tests/EditMode/EditModeTests.asmdef` — references game assemblies, `nunit.framework`
- `tests/PlayMode/PlayModeTests.asmdef` — references game assemblies, `UnityEngine.TestRunner`

Create these via: Assets → Create → Testing → Assembly Definition (Edit/Play Mode)

## Story Type → Test Evidence

| Story Type | Required Evidence | Location | Gate Level |
|---|---|---|---|
| Logic | Automated unit test — must pass | `tests/EditMode/[system]/` | BLOCKING |
| Integration | Integration test OR documented playtest | `tests/PlayMode/[system]/` | BLOCKING |
| Visual/Feel | Screenshot + lead sign-off | `tests/evidence/` | ADVISORY |
| UI | Manual walkthrough OR interaction test | `tests/evidence/` | ADVISORY |
| Config/Data | Smoke check pass | `production/qa/smoke-*.md` | ADVISORY |

## CI

Tests run automatically on every push to `main` and on every pull request.
A failed test suite blocks merging.

**One-time setup required**: Add `UNITY_LICENSE` to GitHub repository secrets
(Settings → Secrets and variables → Actions → New repository secret).
