# Story 003: MobController Core — States, Spawn Init, Dead Check, Facing Conversion

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — States and Transitions, CR-AI-4 (conversion part), CR-AI-5 (encoding), CR-AI-7 (facing validation), CR-AI-13, CR-AI-14, F-AI-3, AC-AI-16
**Requirement**: `TR-ai-002`, `TR-ai-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-012
**ADR Decision Summary**: Phase 3 systems are ticked by the composition's simulation step in an explicit order; the controller exposes a per-tick method and holds no Unity objects, so it is unit-testable.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Facing convention matches Movement and Hit Detection: 0° = +Z, clockwise from above, the same yaw as `transform.eulerAngles.y`; stored as `short` degrees × 10 (0–3599).

**Control Manifest Rules (this layer)**:
- Required: dependency injection over singletons (all inputs are interfaces)
- Forbidden: `async` / `await` in tick-driven code — ADR-011

---

## Acceptance Criteria

- [ ] `MobState` enum: Dormant, Pursuing, WindingUp, AttackExecution, Recovering, Returning, Dead.
- [ ] `MobController` constructed per mob with its `EntityID`, `MobDefinition`, spawn point and injected seams (navigation, a world/target reader, hit detector, clock — fakes in tests). After construction: state Dormant, `pendingReturn` false, `hasAttackedOnce` false, `TargetEntityID` unset, `IsEnraged` false.
- [ ] `Tick(currentTick)` skeleton dispatches by state; the per-state behaviour is filled in by Stories 004–006.
- [ ] **CR-AI-13**: at tick start, mob HP ≤ 0 and state ≠ Dead → state Dead, no further processing that tick (no navigation call, no `CheckHit`); already Dead → nothing. The Dead-entry routine is a hook Story 011 fills (here: records entry once).
- [ ] **CR-AI-14**: `pendingReturn` is cleared on Dormant entry and only there.
- [ ] **F-AI-3 / AC-AI-16**: `FacingToForward(short)` — 0 → (0,0,1), 900 → (1,0,0), 1800 → (0,0,−1), 2700 → (−1,0,0), each component within 0.001.
- [ ] **Facing write**: `BearingToFacing(from, to)` returns the XZ bearing as `short` ×10 normalised to [0, 3599] (3600 → 0, negative wraps); a value outside the range from any write path is clamped and logs one error (CR-AI-7 last sentence). Zero-length bearing (target on top of mob) keeps the previous facing.

---

## Implementation Notes

- Keep the controller free of `UnityEngine.Object` references; `Vector3` and `Mathf` are fine.
- Reuse an existing facing helper if Movement/Hit Detection already has one (search `NormalizeFacing`); do not create a second convention.
- State fields the ACs inspect (`State`, `PendingReturn`, `TargetEntityID`, `CapturedForward`, `HasAttackedOnce`, `LastAttackExecutionTick`) are readable by tests (internal + `InternalsVisibleTo` for the EditMode test assembly, the project's pattern).

---

## Out of Scope

- Story 004: Dormant scan, Pursuing behaviour
- Story 005: attack cycle
- Story 006: leash / invalidation / Returning
- Story 011: what the Dead-entry routine does

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Controller_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 004, 005, 006, 007
