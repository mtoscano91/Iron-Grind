# Story 001: Respawn Config and F-DR-1

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 2 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — F-DR-1, Tuning Knobs, EC-DR-5, EC-DR-9 (retry budget)
**Requirement**: `TR-dr-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: Server-only feature code and its config live in `IronGrind.ServerLogic`; nothing here is needed by the client.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C# + a ScriptableObject data asset).

**Control Manifest Rules (this layer)**:
- Required: feature services and their configs are `IronGrind.ServerLogic` (ADR-012)
- Required: gameplay values data-driven, never hardcoded (coding standards)
- Forbidden: silent clamping of an out-of-range respawn delay (EC-DR-5)

---

## Acceptance Criteria

- [x] **AC-1 Data asset**: a `DeathAndRespawnConfig` ScriptableObject in `src/ServerLogic/DeathAndRespawn/` with `RespawnDelaySeconds` (default 7.0) and `RespawnAnchorRetryTicks` (default 40), with `[CreateAssetMenu]` (same as `MobSpawningConfig`). A `readonly struct DeathAndRespawnConfigValues` snapshot is what the service takes, with `Defaults` (7.0 / 40) and `FromAsset(DeathAndRespawnConfig)`. No shipped `.asset` (Mob Spawning convention): Story 008 uses `Defaults`, with an internal test override like `ZoneServerComposition.MobSpawningConfigOverride`.
- [x] **AC-2 F-DR-1 (AC-DR-14)**: `RespawnDelayTicks = Mathf.RoundToInt(RespawnDelaySeconds * TICK_RATE_HZ)` with `TICK_RATE_HZ = 20` taken from the existing networking constant (not a new literal). 7.0 → 140, 5.0 → 100, 10.0 → 200 exactly. No `(int)` truncation.
- [x] **AC-3 Startup rejection (AC-DR-12, EC-DR-5)**: validation returns a failure (error logged with the offending value and the allowed range) for every `RespawnDelaySeconds` < 5.0 (including 0.0, negative, 4.9, NaN) and for > 10.0. No clamping. Story 008 makes a failure stop server startup.
- [x] **AC-4 Retry budget range**: `RespawnAnchorRetryTicks` outside [20, 200] is a validation failure, same pattern.
- [x] **AC-5 Derived assertion**: after a passing validation, `RespawnDelayTicks >= 100` holds (asserted in code per F-DR-1).

---

## Implementation Notes

- Follow the `MobSpawningConfig` / `MobSpawningConfigValues` pattern (Mob Spawning Story 001): validation as a static method returning a result plus issues, no exceptions on bad data.
- The tick values are `int`; Story 002 widens to `long` when adding to `DeathTick`.

---

## Out of Scope

- Story 008: taking the config (`Defaults` or the test override) at server startup and refusing to start on failure

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-2**: Given seconds 7.0 / 5.0 / 10.0, When ticks are derived, Then 140 / 100 / 200. Edge: 7.05 → 141 (round), 6.999 → 140.
- **AC-3**: Given seconds 4.9, 0.0, −1.0, `float.NaN`, 10.01, When validated, Then failure with one issue naming the value; 5.0 and 10.0 pass.
- **AC-4**: Given retry 19 / 201, Then failure; 20 / 200 pass.
- **AC-1**: `DeathAndRespawnConfigValues.Defaults` has 7.0 / 40 and validates. `FromAsset` on a `ScriptableObject.CreateInstance<DeathAndRespawnConfig>()` with non-default values (e.g. 8.0 / 60) copies both fields.

*Refined at `/story-readiness` 2026-11-17: AC-1 no shipped asset (follows Mob Spawning: `Defaults` + test override). Implementer note: NaN needs its own check (`float.IsNaN(x) || x < MIN || x > MAX`); a plain range compare lets NaN through.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Config_tests.cs` — must exist and pass

**Status**: [x] Created and passing (35 cases)

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002, 004, 008

---

## Completion Notes

**Completed**: 2026-11-17
**Criteria**: 5/5 passing (none deferred)
**Deviations** (advisory):
- Validation is an instance `bool Validate()` that logs one error per bad knob (the actual `MobSpawningConfigValues` pattern), not "a static method returning a result plus issues" as the Implementation Notes said. Same behaviour.
- `internal static CheckDelayTicksFloor(int)` added at code review so the F-DR-1 ≥ 100 floor (unreachable at 20 Hz: 5.0 s = 100 ticks) is tested directly.
**Test Evidence**: Logic: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Config_tests.cs`, 35/35; full EditMode `-testCategory "!Database"` 7869/7869, 0 `error CS`
**Code Review**: Complete (unity-specialist + qa-tester; 3 required + all suggestions applied, APPROVED)
