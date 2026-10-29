# Story 011: Camera Drag, Facing and Auto-Run State

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (7 gaps) → fixed → Ready
> **Layer**: Core (client)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-3, CR-MOV-5, CR-MOV-8 (facing while Prohibited), F-MOV-3, F-MOV-5 (dead zone = at rest), States and Transitions (client sub-states), EC-MOV-5, 7, 9, 18; AC-MOV-4, 6, 12, 13, 19, 20 (client part)
**Requirement**: `TR-mov-008`, `TR-mov-009` (advisory: neither is in `docs/architecture/tr-registry.yaml` yet, like TR-mov-001 to 007)

**ADR Governing Implementation**: ADR-012 (client assembly)
**ADR Decision Summary**: client-only state stays in `IronGrind.Client`; auto-run is client-only — the server never sees it (CR-MOV-5).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — plain C# and `UnityEngine` math types; no scene, no MonoBehaviour.

**Control Manifest Rules (this layer)**:
- Required: logic testable without a scene

---

## API and Timing Contract (readiness decision 1)

Namespace `IronGrind.Client.Movement`, all in `src/Client/Movement/`:

- **`CameraYaw(MovementTuningValues tuning)`** — called **every frame**.
  - `ApplyDrag(Vector2 deltaPx)`: `YawDeg += deltaPx.x × tuning.CameraDragSensitivity`; `deltaPx.y` is ignored. The caller passes `TouchZoneRouter.CameraDragDeltaPx` (Story 010).
  - `YawDeg`: wrapped to [0, 360). Initial value 0.
  - `FlatForward`: `Quaternion.AngleAxis(YawDeg, Vector3.up) * Vector3.forward` (unit, Y = 0).
- **`IClientProhibitedSource { bool IsProhibited { get; } }`** — seam (decision 2).
- **`CharacterFacing`** — holds the character's forward: `Forward` (unit XZ vector) and `YawDeg` in [0, 360). Initial forward `Vector3.forward`, yaw 0 (EC-MOV-18: never a zero vector).
- **`AutoRunState(IClientProhibitedSource prohibited)`** — called **once per network tick** (Story 012's `OnTick` calls it first).
  - `PressButton()`: toggles the flag; may be called between ticks; takes effect on the next `Tick`. Ignored while Prohibited (decision 2).
  - `Tick(JoystickState joystick, Vector3 cameraFlatForward)`: reads the latest router joystick and the camera forward, updates the flag, the sub-state and the facing, and sets the outputs below.
  - `Reset()`: flag off, `SubState = Idle`, `MoveDirection = zero` (decision 6). The facing is kept.
  - Outputs: `bool IsActive` (the flag), `ClientMovementSubState SubState` (`Idle`, `MovingJoystick`, `MovingAutoRun`, `MovingCombined`, `Prohibited`), `Vector3 MoveDirection` (unit XZ or zero), `CharacterFacing Facing`.
- Joystick world direction = `CameraRelativeDirection.WorldMoveDir(joystick.AngleDeg, cameraFlatForward)` (Story 010).
- **At rest** = `!joystick.IsActive || joystick.IsDead` (F-MOV-5 output note).

### `Tick` order

1. **Prohibited** (`prohibited.IsProhibited`): flag cleared; `SubState = Prohibited`; `MoveDirection = zero`; the facing follows step 4 (CR-MOV-8 permits facing rotation). Stop.
2. If last tick was Prohibited and this tick is not: `SubState = Idle`, the flag is **not** re-armed (EC-MOV-7). Continue with the rules below (a held joystick moves this same tick).
3. **F-MOV-3 cancel** (decision 3): if the flag is on and the joystick is not at rest, `cancel = dot(joystickDirWorld, cameraFlatForward) < 0` (strict). True → flag cleared this tick.
4. **Facing**: joystick not at rest → `Facing = joystickDirWorld`; at rest → `Facing = cameraFlatForward` (CR-MOV-3, AC-MOV-6).
5. **Direction and sub-state**:
   - joystick not at rest, flag on → `MoveDirection = joystickDirWorld`, `MovingCombined`;
   - joystick not at rest, flag off (including a cancel this tick) → `MoveDirection = joystickDirWorld`, `MovingJoystick` (decision 5);
   - at rest, flag on → `MoveDirection = Facing.Forward`, `MovingAutoRun`;
   - at rest, flag off → `MoveDirection = zero`, `Idle`.

---

## Acceptance Criteria

- [ ] **AC-MOV-4**: horizontal camera-drag delta × `CAMERA_DRAG_SENSITIVITY` (0.25 °/px) → yaw change; 100 px → 25.0° ± 0.5°. Positive x raises the yaw (clockwise seen from above, the same sense as the joystick angle). Vertical drag does nothing (CR-MOV-3 is horizontal only). Yaw wraps to [0, 360) (decision 4).
- [ ] **AC-MOV-6 / CR-MOV-3**: while the joystick is held (not dead), character facing does not follow the camera; when the joystick is at rest (dead zone or no touch) facing equals the camera's flat forward each tick. While the joystick is held, facing = the joystick world direction.
- [ ] `AutoRunState` exposes the GDD's client sub-states as `ClientMovementSubState`: Idle, MovingJoystick, MovingAutoRun, MovingCombined, Prohibited — per the `Tick` order above.
- [ ] **AC-MOV-12**: the auto-run button toggles the flag on; with the joystick at rest the movement direction is the current facing every tick; a second press clears the flag and the direction is zero on the next tick.
- [ ] Combined: flag on + joystick not at rest + `dot(joystickDir, cameraFlatForward) ≥ 0` → joystick direction wins, flag stays on; joystick released → back to auto-run forward.
- [ ] **AC-MOV-13 / F-MOV-3**: flag on + `dot(joystickDir, cameraFlatForward) < 0` (strict) → flag cleared the same tick, joystick direction takes over, `SubState = MovingJoystick`; `dot = 0` (exactly 90°) → not cancelled. The reference forward is the camera's flat forward (the auto-run forward), not last tick's character facing (decision 3) — so a slow sweep to the rear cancels once it crosses 90°.
- [ ] **AC-MOV-19 / EC-MOV-7**: Prohibited entry clears the flag the same tick; no forward intent while Prohibited; Prohibited clear → Idle, the flag is not re-armed. The client learns Prohibited through `IClientProhibitedSource` (decision 2); a button press while Prohibited is ignored; the facing still updates while Prohibited (CR-MOV-8).
- [ ] **EC-MOV-5**: skill taps never change the flag.
- [ ] **AC-MOV-20 (client part) / EC-MOV-9**: the client keeps sending the auto-run forward intent through the server's one-tick override — the flag is unchanged (the client is not told about the override).
- [ ] **EC-MOV-18**: a new `CharacterFacing` has forward `Vector3.forward` (yaw 0), never a zero vector.
- [ ] CR-MOV-5 persistence: a new `AutoRunState`, and `Reset()` (called by Story 013 on zone entry and reconnect), starts with the flag off and `SubState = Idle`; `Reset()` keeps the facing and does not touch `CameraYaw`.
- [ ] No allocation: `CameraYaw.ApplyDrag` and `AutoRunState.Tick` allocate nothing (Story 012's send path builds on them).

---

## Implementation Notes

- Files: `src/Client/Movement/CameraYaw.cs`, `CharacterFacing.cs`, `AutoRunState.cs`, `ClientMovementSubState.cs`, `IClientProhibitedSource.cs`.
- Readiness decisions (2026-10-29):
  1. API and timing — see the contract above: camera yaw per frame, auto-run / facing / direction per network tick from the router's latest joystick.
  2. Prohibited arrives through `IClientProhibitedSource`; tests use a fake. No stun/root wire message exists yet → follow-up: the real source (Status Effects or client networking work). While Prohibited, the button press is ignored and the facing still follows the normal rule with direction zero.
  3. F-MOV-3 compares against the camera's flat forward (user decision). In AutoRun the character forward equals the camera forward, so this matches the GDD there; in Combined the GDD's literal "character forward" (= last joystick direction) would let a slow sweep steer to the rear without ever cancelling. Follow-up: reword F-MOV-3's `characterForward_world` in the GDD.
  4. Yaw: positive x drag raises the yaw (clockwise from above), wrap [0, 360), initial 0 → `Vector3.forward`.
  5. A rear-pull cancel goes to MovingJoystick (CR-MOV-5, AC-MOV-13), not Idle as the GDD state table's Moving row reads. Follow-up: reword that row. A dead-zone joystick counts as at rest (AutoRun, not Combined).
  6. `Reset()` is the zone-entry / reconnect entry point; the camera yaw is not reset (CR-MOV-5 covers auto-run only).
- Tuning comes from `MovementTuningValues` (`CameraDragSensitivity`); tests use `MovementTuningValues.Defaults`.
- Yaw and facing are floats wrapped once per call (`if < 0 += 360`, `if ≥ 360 -= 360`), the same as `JoystickState.FromDrag`.

---

## Performance

Client only, inside the frame and tick loop: a few vector operations per call, no allocation (tested). AC-MOV-22's per-frame budget is profiled on device with Story 012 (epic Definition of Done).

---

## Out of Scope

- Story 012: turning the direction into messages
- Story 013: the button, the camera transform in the scene, calling `ApplyDrag` / `Tick` / `Reset` from the client loop
- The real Prohibited source (no wire message yet — follow-up)

---

## QA Test Cases

- **AC-MOV-4**: drag +100 px → +25.0°; −40 px → −10.0° (wraps to 350.0°); (0, +100) vertical → 0°; yaw 350 + 40 px → 0.0°.
- **AC-MOV-6**: joystick held east, camera rotated 90° by a simultaneous drag → facing stays east; joystick released → facing = camera forward the next tick.
- **AC-MOV-12**: press → 20 ticks with no touch → direction = facing each tick, `MovingAutoRun`; press again → tick+1 direction zero, `Idle`.
- **Combined**: flag on, joystick at 45° forward-right → direction = joystick, flag on, `MovingCombined`; release → forward, `MovingAutoRun`. Dead-zone joystick + flag on → `MovingAutoRun`.
- **AC-MOV-13**: camera forward (1, 0, 0), joystick world (−0.7, 0, 0.7) → cancel, `MovingJoystick`; (0, 0, 1) → no cancel; (0.7, 0, 0.7) → no cancel.
- **Slow sweep (decision 3)**: camera forward (0, 0, 1), flag on, joystick angle swept 0°, 30°, …, 120° one step per tick → flag on through 90°, cleared on the tick at 120°.
- **AC-MOV-19**: flag on, Prohibited true → flag false the same tick, direction zero, `Prohibited`; facing still follows the joystick; button press → flag stays false; Prohibited false → `Idle`, flag false.
- **EC-MOV-5**: flag on; drive `TouchZoneRouter` with a hit-test fake that reports a skill button, feed `router.Joystick` to `Tick` → flag on, `MovingAutoRun`.
- **AC-MOV-20 (client)**: flag on, 20 ticks with no input change → flag on and direction = forward every tick.
- **EC-MOV-18**: new `CharacterFacing` → `Forward == Vector3.forward`.
- **New session / Reset**: new state → flag off, `Idle`; flag on → `Reset()` → flag off, `Idle`, facing unchanged.
- **No allocation**: 100 `ApplyDrag` + `Tick` calls inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Client/Movement/ClientMovement_CameraAndAutoRun_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 010 (Complete)
- Unlocks: Stories 012, 013
