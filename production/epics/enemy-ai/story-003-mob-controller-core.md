# Story 003: MobController Core — States, Spawn Init, Dead Check, Facing Conversion

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 h

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
- [ ] `MobController` constructed per mob with its `EntityID`, `MobDefinition`, spawn point and injected seams: `INavigationProvider` (existing), `IZoneEntityStatReader` (existing; `GetCurrentHP` for CR-AI-13) and `HitDetector` (the concrete pure class, as `AutoAttackCombatSystem` uses it). No clock seam — `Tick(currentTick)` carries the tick. The target/world reader is added by Story 004, which first needs it. Fakes in tests. After construction: state Dormant, `pendingReturn` false, `hasAttackedOnce` false, `TargetEntityID` unset, `IsEnraged` false, `DeadEnteredTick == -1`, `DeadEntryCount == 0`.
- [ ] `Tick(currentTick)` skeleton dispatches by state; the per-state behaviour is filled in by Stories 004–006 (here: per-state stubs do nothing).
- [ ] **CR-AI-13**: at tick start, mob HP ≤ 0 and state ≠ Dead → state Dead, no further processing that tick (no navigation call, no `CheckHit`); already Dead → nothing. The Dead-entry routine is a hook Story 011 fills; here it sets `DeadEnteredTick = currentTick` and increments `DeadEntryCount` (exactly once per mob).
- [ ] **CR-AI-14**: `pendingReturn` is cleared on Dormant entry and only there.
- [ ] **F-AI-3 / AC-AI-16**: `FacingToForward(short)` — 0 → (0,0,1), 900 → (1,0,0), 1800 → (0,0,−1), 2700 → (−1,0,0), each component within 0.001.
- [ ] **Facing write**: `BearingToFacing(from, to)` = `MovementIntentMessageCodec.NormalizeFacing((short)Mathf.RoundToInt(Mathf.Atan2(dx, dz) × Mathf.Rad2Deg × 10f))` — XZ only, wraps (359.97° → 3600 → 0; negative wraps). Zero-length bearing (XZ distance² < 1e-6) keeps the previous facing. Every write to the controller's `FacingAngle` goes through one guard `WriteFacing(int)`: a value outside [0, 3599] is clamped (3600 → 3599, −1 → 0) and logs one error per bad write (CR-AI-7 last sentence, in the stored ×10 unit). The controller holds `FacingAngle`; publishing it to the zone state view is out of scope here.

---

## Implementation Notes

- Keep the controller free of `UnityEngine.Object` references; `Vector3` and `Mathf` are fine.
- Reuse an existing facing helper if Movement/Hit Detection already has one (search `NormalizeFacing`); do not create a second convention.
- **Performance**: constant work per mob per tick (one HP read + a state switch); no allocation on the tick path. Measured at scale by the 150-mob fixture in a later story.
- State fields the ACs inspect (`State`, `PendingReturn`, `TargetEntityID`, `CapturedForward`, `HasAttackedOnce`, `LastAttackExecutionTick`, `FacingAngle`, `DeadEnteredTick`, `DeadEntryCount`) are readable by tests (internal + `InternalsVisibleTo` for the EditMode test assembly, the project's pattern).

---

## Out of Scope

- Story 004: Dormant scan, Pursuing behaviour
- Story 005: attack cycle
- Story 006: leash / invalidation / Returning
- Story 011: what the Dead-entry routine does

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-13.*

- **Construction**: state Dormant, `PendingReturn` false, `HasAttackedOnce` false, `TargetEntityID` unset, `IsEnraged` false, `DeadEnteredTick == -1`, `DeadEntryCount == 0`.
- **CR-AI-13**: HP 0 while Dormant → Tick → Dead, 0 navigation calls, `DeadEnteredTick` = that tick. HP −5 → same. HP 0.001 → stays Dormant. Already Dead, tick again → `DeadEntryCount` stays 1, no calls. HP 0 with state forced to WindingUp → Dead, 0 navigation calls.
- **CR-AI-14**: `PendingReturn` set true, state forced to Returning → still true; enter Dormant → false; transitions to Pursuing / WindingUp leave it unchanged.
- **AC-AI-16**: `FacingToForward` 0 / 900 / 1800 / 2700 → cardinal vectors within 0.001; 450 → (0.7071, 0, 0.7071); 3599 → z > 0.9999.
- **BearingToFacing**: target at +Z → 0, +X → 900, −Z → 1800, −X → 2700; a bearing of 359.97° → 0 (not 3600); Y offset ignored; zero-length bearing keeps the previous value (seed 1234).
- **WriteFacing**: 3600 → 3599 + 1 error; −1 → 0 + 1 error; 0 and 3599 → no error (`LogAssert`).
- **Tick dispatch**: one tick in each non-Dead state with HP > 0 → no exception, state unchanged (stubs).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_Controller_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 004, 005, 006, 007
