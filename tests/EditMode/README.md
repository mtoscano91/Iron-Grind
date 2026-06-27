# Edit Mode Tests

Unit tests that run without entering Play Mode. Use for pure logic: formulas,
state machines, data validation, economy calculations.

## When to use Edit Mode

- Balance formulas (damage, XP curves, enhancement costs)
- State machine transition logic
- Data schema validation
- Pure C# utility classes with no MonoBehaviour dependency

## Assembly Definition

Create `tests/EditMode/EditModeTests.asmdef` via:
Assets → Create → Testing → Assembly Definition (Edit Mode Test Assemblies)

Reference your game assemblies and set `Test Assemblies = true`.

## Example

See `SmokeTest.cs` in this directory for the minimum viable test structure.

## Naming

Files: `[System][Feature]Test.cs`
Methods: `[Scenario]_[ExpectedResult]()`
