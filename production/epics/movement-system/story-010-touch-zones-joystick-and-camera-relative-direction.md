# Story 010: Touch Zones, Floating Joystick and Camera-Relative Direction

> **Epic**: Movement System
> **Status**: Complete — 2026-10-29 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (6 gaps) → fixed → Ready
> **Layer**: Core (client)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

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

- [ ] **API**: `TouchZoneRouter(MovementTuningValues tuning, IUiHitTest hitTest)` with `ProcessFrame(ReadOnlySpan<TouchInput> events, Rect safeArea)`; `TouchInput` = (`int Id`, `TouchInputPhase Phase` Began/Moved/Ended/Canceled, `Vector2 Position`). Outputs after each call: `Joystick` (`JoystickState { IsActive, Anchor, Current, IsDead, AngleDeg }`), `CameraDragActive` (bool) and `CameraDragDeltaPx` (`Vector2`, sum of the camera owner's Moved deltas this frame, reset to zero at the start of every `ProcessFrame`; Story 011 consumes it).
- [ ] **CR-MOV-1 / EC-MOV-3**: each touch is assigned **by its start position** for its whole lifetime: `start.x < safeArea.x + safeArea.width / 2` → Joystick, otherwise → Camera. Y is ignored; a touch in the left inset (x < safeArea.xMin) is Joystick, in the right inset Camera. `safeArea` and touch positions are both Unity screen pixels, bottom-left origin.
- [ ] **EC-MOV-4 / EC-MOV-5**: every Began is hit-tested first (`IUiHitTest.IsOnSkillButton(position)`, either half); a touch on a skill button claims no zone for its lifetime and produces no joystick or camera output; an existing camera drag or joystick continues.
- [ ] **EC-MOV-1 / EC-MOV-2**: a second touch in a zone that already has an owner is ignored for its whole lifetime (it does not take over when the owner lifts); a touch that begins after the owner lifts becomes the owner. Moved/Ended/Canceled for unknown or unclaimed ids are ignored.
- [ ] **AC-MOV-26 / EC-MOV-6 — per-frame order**: (1) Ended/Canceled of touches that began in an earlier frame, (2) all Began, (3) all Moved, (4) Ended/Canceled of touches that began this frame. A left and a right Began in the same frame both arm; an owner Ended + a new left Began in the same frame → the new touch owns the joystick.
- [ ] **AC-MOV-3**: a right-half Began never activates the joystick; a left-half Began never produces camera drag.
- [ ] **AC-MOV-2**: the joystick anchor is the exact Began pixel (floating, no snap).
- [ ] **F-MOV-5 / AC-MOV-1 (client part)**: `normalizedDrag = |current − start| / JOYSTICK_RADIUS_PX`; `< DEAD_ZONE_NORMALIZED` → direction zero (at defaults, below 12 px). At or above → `joystickAngle_deg = atan2(offset.x, offset.y)` clockwise from up, wrapped to [0, 360) (negative + 360).
- [ ] **CR-MOV-2 release**: Ended/Canceled → direction zero immediately.
- [ ] **F-MOV-4 / AC-MOV-5**: `worldMoveDir = normalize(AngleAxis(joystickAngle_deg, up) × normalize(camForward.xz))`; camera facing 90° (east) + joystick full up → (1, 0, 0) ± 0.01. `|camForward.xz| < 0.001` → `Vector3.forward` is used as `cameraForward_flat` (the joystick angle still applies) + `Debug.Assert` in dev builds.

---

## Implementation Notes

- Files: `src/Client/Movement/` (namespace `IronGrind.Client.Movement`): `TouchZoneRouter`, `TouchInput` (+ `TouchInputPhase`), `JoystickState`, `IUiHitTest`, `CameraRelativeDirection`. The real `IUiHitTest` (UI Toolkit picking) comes in Story 013; tests use a fake.
- **Performance**: per-frame input path — no allocation per frame (input is a span; owner state is fixed fields: one joystick owner, one camera owner, plus a small fixed array for ignored / skill-button ids if needed). Expected cost negligible (≤ ~10 touches per frame).
- Tuning comes from Story 001's `MovementTuningValues` in `IronGrind.Foundation` (radius, dead zone; placed there at Story 001 readiness so the client can read it — ADR-012).
- Add the new client types to the ADR-012 boundary lists if the architecture test requires.

---

## Out of Scope

- Story 011: camera yaw from drags, facing, auto-run
- Story 012: building messages
- Story 013: the touch source, the visible joystick

---

## QA Test Cases

- **AC-MOV-3**: safe area x 0–2000: Began at x 999 → joystick; 1000 → camera; 1001 → camera; with a 44 px left inset (area 44–2000) the midpoint is 1022; Began at x 20 (inside the inset) → joystick.
- **EC-MOV-3**: joystick touch Began at 500, Moved to 1500 → still joystick, no camera output.
- **EC-MOV-1/2**: second left Began while the first is held → ignored; after the first Ended, a new left Began → owner; the ignored second touch never becomes owner, even after the first lifts.
- **Same-frame handover**: frame with owner Ended + new left Began → the new touch owns the joystick.
- **Unknown ids**: Moved/Ended for an id never seen → no output, no exception.
- **AC-MOV-26**: frame with right Began + left Began + left Moved → both armed, the left Moved applies to the joystick.
- **EC-MOV-4**: camera drag held; Began on a skill button in the right half → camera keeps the original touch, the button touch claims nothing; a Began on a skill button in the left half → no joystick.
- **AC-MOV-2**: Began at (321, 456) → anchor (321, 456).
- **AC-MOV-1**: radius 80, dead zone 0.15: offset 11.9 px → zero; 12.0 px → non-zero.
- **Angle**: offset (−80, 0) → `AngleDeg` 270; (0, −80) → 180.
- **Release**: Ended → zero the same frame; Canceled → zero.
- **AC-MOV-5**: camera forward (1, −0.5, 0) and joystick (0, +80 px) → (1, 0, 0) ± 0.01; joystick right (+80, 0) with camera north → (1, 0, 0); camera straight down + joystick up → `Vector3.forward`; camera straight down + joystick right → (1, 0, 0) (assert expected via `LogAssert.Expect(LogType.Assert, …)`; assertions are on in the Editor).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Client/Movement/ClientMovement_TouchInput_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/Client/Movement/ClientMovement_TouchInput_tests.cs`, 48 methods / 53 cases, all passing (EditMode 5328 / 5199 passed / 0 failed / 129 skipped, 2026-10-29)

---

## Dependencies

- Depends on: Story 001 (client knobs)
- Unlocks: Stories 011, 012, 013

---

## Completion Notes
**Completed**: 2026-10-29
**Criteria**: 9/9 passing (all covered by EditMode tests; none deferred)
**Deviations** (advisory):
- `JoystickState.FromDrag` casts `|offset| / radius` to float before the dead-zone compare — Mono kept 12/80 at higher precision than `0.15f`, which made the AC-MOV-1 12 px boundary dead.
- Added `TouchZoneRouter.Reset()` (code review): drops both owners when touches are lost without Canceled; Story 013 calls it on focus loss / pause.
- Camera drag delta is zero on the Began frame; an Ended+Began with the same id in one frame is not handled (Unity gives one entry per finger per frame).
- GDD AC-MOV-5 says "server-computed" direction; the direction is computed on the client (this story) and the server applies the received direction.
- TR-mov-008 not in `tr-registry.yaml` (no TR-mov-* registered yet).
**Test Evidence**: Logic — `tests/EditMode/Client/Movement/ClientMovement_TouchInput_tests.cs` (48 methods / 53 cases)
**Code Review**: Complete — APPROVED WITH SUGGESTIONS (unity-specialist CLEAN, qa-tester TESTABLE); 7 suggestions applied (Reset(), 6 tests, Story 013 EnhancedTouch carry-over note)
