# Story 001: CheckHit and Constants

> **Epic**: Hit Detection
> **Status**: Complete — 2026-10-30 (`/story-done`: COMPLETE WITH NOTES); `/story-readiness` 2026-10-30: NEEDS WORK (5 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/hit-detection.md` — CR-HD-1 to CR-HD-4, F-HD-1, F-HD-2, EC-HD-1, EC-HD-4, EC-HD-6, EC-HD-7
**Requirement**: `TR-hd-001`, `TR-hd-002`, `TR-hd-003`, `TR-hd-004` (placeholders; not yet in `docs/architecture/tr-registry.yaml`)

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: server-only logic lives in `IronGrind.ServerLogic`, whose define constraint keeps it out of client builds.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure math)
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: public APIs have doc comments; dependency injection over singletons
- Forbidden: `CheckHit` reading `EntityState` or any service
- Guardrail: no allocation (called once per attack, up to 150 mobs per zone)

---

## Acceptance Criteria

*From GDD `design/gdd/hit-detection.md`, scoped to this story:*

- [x] `HitResult : byte` in `src/Foundation/Networking/HitResult.cs`, with `Hit = 0`, `MissOutOfRange = 1`, `MissFacing = 2`.
- [x] `HitDetector(HitDetectionConfig)` in `src/ServerLogic/HitDetection/` (namespace `IronGrind.HitDetection`, added to `ServerOnlyNamespaces`), with the tolerance cosine cached in the constructor.
- [x] Instance method `CheckHit(attackerPos, targetPos, attackRange, capturedForward)` follows the order range → point-blank guard → facing, Y ignored (AC-HD-1 to AC-HD-10).
- [x] `HitDetectionConfig` logs one warning when the tolerance is outside 45–90°, does not clamp it, and accepts exactly `FACING_CHECK_DISABLED_DEG` with no warning.
- [x] Any in-range target with `capturedForward` derived toward it and `d_sq ≥ POINT_BLANK_EPSILON_SQ` returns `Hit` (AC-HD-14).
- [x] `CheckHit` runs with no `EntityState`, ECS world or server service in scope (AC-HD-17).
- [x] Constants defined: `ATTACK_FACING_TOLERANCE_DEG` default 45, `POINT_BLANK_EPSILON_SQ` 0.0005, `ATTACK_RANGE_MIN` 0.5, `FACING_CHECK_DISABLED_DEG` 180.

---

## Implementation Notes

- Follow `DamageCalculator` + `DamageCalculationConfig` for the constructor-injected config. `CheckHit` keeps CR-HD-1's four arguments, and it stays stateless apart from the cached cosine.
- `HitResult` lives in Foundation, like `DamageType`, because the Story 002 message in `IronGrind.Foundation` cannot reference `IronGrind.ServerLogic`. `CheckHit` itself stays in ServerLogic.
- OQ-HD-3 (per-attack tolerance) stays open; this design leaves room for an overload later. Do not add one here.
- `AttackRange` is not validated here (EC-HD-4); callers check `ATTACK_RANGE_MIN`.
- `capturedForward` being unit length with Y = 0 is a caller precondition. Do not normalize or validate it. A zero vector gives `dot = 0`, so `MissFacing` at any tolerance below 90°.
- `toTarget` is computed from XZ only. Do not read Y from the positions.

---

## Out of Scope

- Story 002: `HitCheckResult` wire type
- Story 004: emitting the result
- Auto-Attack Combat and Enemy AI: deriving `capturedForward`, liveness checks, `ATTACK_RANGE_MIN` validation

---

## QA Test Cases

- **AC-HD-1**: attacker (0,0,0), target (2,0,2), range 3 → `Hit`.
- **AC-HD-2**: target (3,0,0), range 3 → `Hit` (inclusive boundary).
- **AC-HD-3**: target (3.01,0,0), range 3 → `MissOutOfRange`.
- **AC-HD-4**: target (2,10,0), forward (1,0,0), range 3 → `Hit` (Y ignored).
- **AC-HD-5**: forward (1,0,0), target (1,0,0), tolerance 45 → `Hit`.
- **AC-HD-6**: forward (1,0,0), target (0,0,1), tolerance 45 → `MissFacing`.
- **AC-HD-7**: forward (1,0,0), target (0.866,0,0.5), tolerance 45 → `Hit`.
- **AC-HD-8**: forward (−1,0,0), target (1,0,0) → `MissFacing`.
- **AC-HD-9**: co-located at (5,0,5), any forward → `Hit`.
- **AC-HD-10**: target (0.001,0,0), forward (−1,0,0) → `Hit`.
- **AC-HD-14**: for a fixed set of in-range XZ displacements, forward derived toward the target → `Hit`, never `MissFacing`.
- **AC-HD-17**: the test calls `CheckHit` with plain vectors and creates no scene, service or `EntityState`.
- **Facing boundary**: tolerance 45, a target 44.9° off-axis → `Hit`; 45.1° off-axis → `MissFacing`.
- **Epsilon boundary**: `d_sq` just below and just above `POINT_BLANK_EPSILON_SQ`, reversed forward → `Hit`, then `MissFacing`.
- **Order**: co-located with range 0 → `Hit` (`0 > 0` is false, then the guard fires; EC-HD-4).
- **Disabled facing and inclusive `>=`**: tolerance `FACING_CHECK_DISABLED_DEG`, forward reversed, in range → `Hit` (`dot = -1`, threshold `-1`; EC-HD-7).
- **Zero forward**: forward (0,0,0), in range, beyond the point-blank guard, tolerance 45 → `MissFacing`.
- **Config warning**: tolerance 30 and 120 each log one warning and are kept unclamped; 45, 90 and 180 log none.
- **No allocation**: `CheckHit` inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/HitDetection/HitDetection_CheckHit_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/HitDetection/HitDetection_CheckHit_tests.cs`, 42 cases, all passing (EditMode 5473 / 5344 passed / 0 failed / 129 skipped, 2026-10-30)

---

## Dependencies

- Depends on: None
- Unlocks: Story 004

---

## Completion Notes

**Completed**: 2026-10-30
**Criteria**: 7/7 passing
**Deviations** (advisory):
1. `HitDetectionConfig` throws on NaN, infinity or a tolerance outside (0, 180], as `DamageCalculationConfig` does.
2. The story's `ATTACK_FACING_TOLERANCE_DEG` is `ATTACK_FACING_TOLERANCE_DEG_DEFAULT`, with `_SAFE_MIN` and `_SAFE_MAX`.
3. The 180° sentinel uses a `_facingDisabled` flag, because a reversed diagonal `dot` can round below −1 (code review).
4. Tolerance comes from a constructor-injected config with defaults, not an external data file yet; no production code constructs `HitDetector` until Auto-Attack.
5. `TR-hd-001` to `004` are not in the TR registry; checked against the GDD.
**Test Evidence**: Logic: `tests/EditMode/HitDetection/HitDetection_CheckHit_tests.cs`
**Code Review**: Complete (CHANGES REQUIRED → applied; lean, QL-TEST-COVERAGE and LP-CODE-REVIEW skipped)
**Tech debt**: None logged
