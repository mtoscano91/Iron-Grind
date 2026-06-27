# Play Mode Tests

Integration tests that run inside a real game scene (Unity enters Play Mode).
Use for cross-system interactions, coroutines, physics, and MonoBehaviour lifecycle.

## When to use Play Mode

- Multi-system integration (e.g. combat → stat → persistence chain)
- Coroutine-based flows (async save, skill cooldown timers)
- NavMesh pathfinding integration
- NetworkManager session bring-up/tear-down

## Assembly Definition

Create `tests/PlayMode/PlayModeTests.asmdef` via:
Assets → Create → Testing → Assembly Definition (Play Mode Test Assemblies)

Reference your game assemblies and set `Test Assemblies = true`.

## Performance Note

Play Mode tests are slower than Edit Mode — Unity enters/exits Play Mode for
each test class. Keep Play Mode tests focused on integration seams, not unit logic.
Unit logic belongs in `tests/EditMode/`.

## Naming

Files: `[System][Feature]IntegrationTest.cs`
Methods: `[Scenario]_[ExpectedResult]()`
