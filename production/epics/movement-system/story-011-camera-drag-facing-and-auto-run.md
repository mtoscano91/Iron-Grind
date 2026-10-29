# Story 011: Camera Drag, Facing and Auto-Run State

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core (client)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-3, CR-MOV-5, F-MOV-3, States and Transitions (client sub-states), EC-MOV-5, 7, 9; AC-MOV-4, 6, 12, 13, 19, 20 (client part)
**Requirement**: `TR-mov-008`, `TR-mov-009`

**ADR Governing Implementation**: ADR-012 (client assembly)
**ADR Decision Summary**: client-only state stays in `IronGrind.Client`; auto-run is client-only — the server never sees it (CR-MOV-5).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: logic testable without a scene

---

## Acceptance Criteria

- [ ] **AC-MOV-4**: horizontal camera-drag delta × `CAMERA_DRAG_SENSITIVITY` (0.25 °/px) → yaw change; 100 px → 25.0° ± 0.5°. Vertical drag does nothing (CR-MOV-3 is horizontal only).
- [ ] **AC-MOV-6 / CR-MOV-3**: while the joystick is held (not dead), character facing does not follow the camera; when the joystick is at rest (dead zone or no touch) facing equals the camera's flat forward each tick. While the joystick is held, facing = the joystick world direction.
- [ ] `AutoRunState` flag with the GDD's client sub-states: Idle, Moving-Joystick, Moving-AutoRun, Moving-Combined, Prohibited.
- [ ] **AC-MOV-12**: the auto-run button toggles the flag on; with the joystick at rest the movement direction is the current facing every tick; a second press clears the flag and the direction is zero on the next tick.
- [ ] Combined: flag on + joystick in the forward half-plane (`dot ≥ 0`) → joystick direction wins, flag stays on; joystick released → back to auto-run forward.
- [ ] **AC-MOV-13 / F-MOV-3**: flag on + `dot(joystickDir, characterForward) < 0` (strict) → flag cleared the same tick, joystick direction takes over; `dot = 0` (exactly 90°) → not cancelled.
- [ ] **AC-MOV-19 / EC-MOV-7**: Prohibited entry clears the flag the same tick; no forward intent while Prohibited; Prohibited clear → Idle, the flag is not re-armed. **How the client learns Prohibited is decided at readiness** — no wire message for stun or root exists yet; this story takes it through an `IClientProhibitedSource` seam.
- [ ] **EC-MOV-5**: skill taps never change the flag.
- [ ] **AC-MOV-20 (client part) / EC-MOV-9**: the client keeps sending the auto-run forward intent through the server's one-tick override — the flag is unchanged (the client is not told about the override).
- [ ] CR-MOV-5 persistence: a new `AutoRunState` (disconnect, reconnect, zone change) starts with the flag off.

---

## Implementation Notes

- Files: `src/Client/Movement/CameraYaw.cs`, `CharacterFacing.cs`, `AutoRunState.cs`.
- Evaluate F-MOV-3 against the character forward **at the moment of the joystick reading** (before this tick's facing update).

---

## Out of Scope

- Story 012: turning the direction into messages
- Story 013: the button and the camera transform in the scene

---

## QA Test Cases

- **AC-MOV-4**: drag +100 px → +25.0°; −40 px → −10.0°; (0, +100) vertical → 0°.
- **AC-MOV-6**: joystick held east, camera rotated 90° by a simultaneous drag → facing stays east; joystick released → facing = camera forward the next tick.
- **AC-MOV-12**: press → 20 ticks with no touch → direction = facing each tick; press again → tick+1 direction zero.
- **Combined**: flag on, joystick at 45° forward-right → direction = joystick, flag on; release → forward.
- **AC-MOV-13**: forward (1, 0, 0), joystick (−0.7, 0, 0.7) → cancel; (0, 0, 1) → no cancel; (0.7, 0, 0.7) → no cancel.
- **AC-MOV-19**: flag on, Prohibited true → flag false the same tick, direction zero; Prohibited false → still Idle, flag false.
- **EC-MOV-5**: flag on, skill tap → flag on.
- **New session**: new state → flag off.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Client/Movement/ClientMovement_CameraAndAutoRun_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 010
- Unlocks: Stories 012, 013
