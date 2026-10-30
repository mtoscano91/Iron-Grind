# Story 001: CheckHit and Constants

> **Epic**: Hit Detection
> **Status**: Ready
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

- [ ] `HitResult : byte` with `Hit = 0`, `MissOutOfRange = 1`, `MissFacing = 2`.
- [ ] `CheckHit(attackerPos, targetPos, attackRange, capturedForward)` follows the order range → point-blank guard → facing, Y ignored (AC-HD-1 to AC-HD-10).
- [ ] Any in-range target with `capturedForward` derived toward it and `d_sq ≥ POINT_BLANK_EPSILON_SQ` returns `Hit` (AC-HD-14).
- [ ] `CheckHit` runs with no `EntityState`, ECS world or server service in scope (AC-HD-17).
- [ ] Constants defined: `ATTACK_FACING_TOLERANCE_DEG` default 45, `POINT_BLANK_EPSILON_SQ` 0.0005, `ATTACK_RANGE_MIN` 0.5, `FACING_CHECK_DISABLED_DEG` 180.

---

## Implementation Notes

- Place the code in `src/ServerLogic/HitDetection/` and add its namespace to `ServerOnlyNamespaces` if the boundary test asks for it.
- `HitResult` must be reachable by the wire message in `IronGrind.Foundation` (Story 002), or the message carries a plain `byte`. Decide at `/story-readiness`.
- `ATTACK_FACING_TOLERANCE_DEG` is a tuning knob, but CR-HD-1 fixes a four-argument `CheckHit`. Decide at `/story-readiness` how the tolerance reaches it and how OQ-HD-3 stays open (per-attack override).
- `AttackRange` is not validated here (EC-HD-4); callers check `ATTACK_RANGE_MIN`.
- Facing derives XZ only. Do not read Y from `capturedForward` or the positions when computing `toTarget`.

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
- **Facing boundary**: a target exactly 45° off-axis → `Hit`; just beyond it → `MissFacing` (tolerance inclusive, `>=`).
- **Epsilon boundary**: `d_sq` just below and just above `POINT_BLANK_EPSILON_SQ`, reversed forward → `Hit`, then `MissFacing`.
- **Order**: co-located with range 0 returns the range or guard result the rule order gives (EC-HD-4).
- **Disabled facing**: tolerance `FACING_CHECK_DISABLED_DEG`, forward reversed, in range → `Hit` (EC-HD-7).
- **No allocation**: `CheckHit` inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/HitDetection/HitDetection_CheckHit_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Story 004
