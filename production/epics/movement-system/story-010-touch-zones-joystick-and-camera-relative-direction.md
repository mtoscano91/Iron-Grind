# Story 010: Touch Zones, Floating Joystick and Camera-Relative Direction

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core (client)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-1, CR-MOV-2, F-MOV-4, F-MOV-5, EC-MOV-1–6; AC-MOV-1 (client part), 2, 3, 5, 26
**Requirement**: `TR-mov-008`

**ADR Governing Implementation**: ADR-012 (client code in `IronGrind.Client`, which must not reference `IronGrind.ServerLogic`); ADR-005 (UI Toolkit — the view comes in Story 013)
**ADR Decision Summary**: client-only logic lives in the client assembly; presentation is UI Toolkit.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure logic; the touch source is a seam)
**Engine Notes**: the real touch source (Input System `Touchscreen` / `EnhancedTouch`, or legacy `Input.touches`) is chosen in Story 013; this story takes touch events as plain structs. `Screen.safeArea` is read through a seam (as `HudSafeAreaApplier` does).

**Control Manifest Rules (this layer)**:
- Required: touch-only, no hover (technical preferences); safe-area insets respected
- Required: logic testable without a scene (coding standards: DI)

---

## Acceptance Criteria

- [ ] `TouchZoneRouter` takes per-frame touch events (`id`, `phase` Began/Moved/Ended/Canceled, `position`) and a safe area rect; it assigns each touch to Joystick (start X in the left half of the safe-area width) or Camera (right half) **by its start position**, for its whole lifetime (CR-MOV-1, **EC-MOV-3**).
- [ ] A touch that begins on a skill button (hit-test seam `IUiHitTest.IsOnSkillButton(position)`) is not claimed by the camera zone (**EC-MOV-4**: an existing camera drag continues).
- [ ] **EC-MOV-1 / EC-MOV-2**: a second touch in a zone that already has an owner is ignored until the owner lifts.
- [ ] **AC-MOV-26 / EC-MOV-6**: all Began events of a frame are processed before any Moved delta; a left and a right Began in the same frame both arm.
- [ ] **AC-MOV-3**: a right-half Began never activates the joystick; a left-half Began never produces camera drag.
- [ ] **AC-MOV-2**: the joystick anchor is the exact Began pixel (floating, no snap).
- [ ] **F-MOV-5 / AC-MOV-1 (client part)**: `normalizedDrag = |current − start| / JOYSTICK_RADIUS_PX`; `< DEAD_ZONE_NORMALIZED` → direction zero (at defaults, below 12 px). At or above → `joystickAngle_deg = atan2(offset.x, offset.y)` clockwise from up.
- [ ] **CR-MOV-2 release**: Ended/Canceled → direction zero immediately.
- [ ] **F-MOV-4 / AC-MOV-5**: `worldMoveDir = normalize(AngleAxis(joystickAngle_deg, up) × normalize(camForward.xz))`; camera facing 90° (east) + joystick full up → (1, 0, 0) ± 0.01. `|camForward.xz| < 0.001` → `Vector3.forward` fallback + `Debug.Assert` in dev builds.

---

## Implementation Notes

- Files: `src/Client/Movement/` (namespace `IronGrind.Client.Movement`): `TouchZoneRouter`, `JoystickState`, `CameraRelativeDirection`.
- Tuning comes from Story 001's `MovementTuningValues` in `IronGrind.Foundation` (radius, dead zone; placed there at Story 001 readiness so the client can read it — ADR-012).
- Add the new client types to the ADR-012 boundary lists if the architecture test requires.

---

## Out of Scope

- Story 011: camera yaw from drags, facing, auto-run
- Story 012: building messages
- Story 013: the touch source, the visible joystick

---

## QA Test Cases

- **AC-MOV-3**: safe area x 0–2000: Began at x 999 → joystick; 1001 → camera; with a 44 px left inset (area 44–2000) the midpoint is 1022.
- **EC-MOV-3**: joystick touch Began at 500, Moved to 1500 → still joystick, no camera output.
- **EC-MOV-1/2**: second left Began while the first is held → ignored; after the first Ended, a new left Began → owner.
- **AC-MOV-26**: frame with right Began + left Began + left Moved → both armed, the left Moved applies to the joystick.
- **EC-MOV-4**: camera drag held; Began on a skill button in the right half → camera keeps the original touch, the button touch claims nothing.
- **AC-MOV-2**: Began at (321, 456) → anchor (321, 456).
- **AC-MOV-1**: radius 80, dead zone 0.15: offset 11.9 px → zero; 12.0 px → non-zero.
- **Release**: Ended → zero the same frame; Canceled → zero.
- **AC-MOV-5**: camera forward (1, −0.5, 0) and joystick (0, +80 px) → (1, 0, 0) ± 0.01; joystick right (+80, 0) with camera north → (1, 0, 0); camera straight down → `Vector3.forward` (assert expected via `LogAssert`).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Client/Movement/ClientMovement_TouchInput_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (client knobs)
- Unlocks: Stories 011, 012, 013
