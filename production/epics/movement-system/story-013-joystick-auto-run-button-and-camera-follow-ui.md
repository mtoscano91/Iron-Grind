# Story 013: Joystick, Auto-Run Button and Camera Follow (UI)

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core (client presentation)
> **Type**: UI
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/movement-system.md` — UI Requirements (Virtual Joystick, Auto-Run Toggle Button, Stun Overlay, Ownership), CR-MOV-1 (safe area), CR-MOV-3 (camera in `LateUpdate`); AC-MOV-2 (visual)
**Requirement**: `TR-mov-008`, `TR-mov-009`

**ADR Governing Implementation**: ADR-005 (HUD UI framework: UI Toolkit); ADR-012
**ADR Decision Summary**: HUD elements are UI Toolkit (UXML/USS) on a `UIDocument`; client-only code in `IronGrind.Client`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — UI Toolkit runtime on iOS; `VisualElement.transform` is deprecated in 6.2+ (use `style.translate` / `style.left/top`)
**Engine Notes**: read touches through one touch source adapter (Input System `EnhancedTouch` if the package is present, otherwise legacy `Input.touches` — decide at readiness from `Packages/manifest.json`); route to Story 010's `TouchZoneRouter`.

**Control Manifest Rules (this layer)**:
- Required: safe-area insets (`HudSafeAreaApplier` pattern); everything reachable by thumbs in landscape
- Required: UI work routed to the UI specialist (technical preferences routing table)
- Forbidden: hover-only interactions

---

## Acceptance Criteria

- [ ] A UI Toolkit joystick element (base + thumb, radius `JOYSTICK_RADIUS_PX`) appears at the touch-start point of a joystick-zone touch, the thumb follows the drag clamped to the radius, and both hide on release (**AC-MOV-2** visual).
- [ ] An auto-run toggle button shows its on/off state from `AutoRunState` (Story 011) and toggles it on tap; it sits inside the safe area and is thumb-reachable in landscape.
- [ ] A stun overlay placeholder shows while the client's Prohibited source (Story 011 seam) reports true — per the GDD UI Requirements; final art is out of scope.
- [ ] A camera follow component updates in `LateUpdate`, after the character transform is set (CR-MOV-3), and applies the yaw from Story 011.
- [ ] A client movement controller wires the touch source → router → joystick/camera/auto-run → `MovementIntentProducer.OnTick` on the client tick (with a logging `IMovementIntentSink` until the client networking epic exists).
- [ ] The components work in a dev harness scene (`src/DevHarness`) with a stand-in character, since no client game scene exists yet.

---

## Implementation Notes

- Spawn the `unity-ui-specialist` for the UXML/USS and the controller (technical preferences routing).
- Keep all decisions in Stories 010–012's logic classes; this story only adapts input and draws.
- Story 010 code-review carry-over (touch source → `TouchZoneRouter`): if EnhancedTouch is used, map `Stationary` to `Moved` (a zero delta is harmless; no new `TouchInputPhase` value), use `touch.touchId` for `TouchInput.Id` (not `finger.index`, which is reused), and call `TouchZoneRouter.Reset()` on application focus loss / pause or when the touch source re-initialises.
- Memory note: the HUD harness buttons were once "missing" because the Game view Scale cropped them — check the Game view at 1× before filing a layout bug.

---

## Out of Scope

- Real network send and server position rendering/interpolation (client networking / Client-Side Prediction)
- Animation (`NormalizedSpeed`), VFX and audio (GDD Visual/Audio Requirements — later Presentation work)
- AC-MOV-22 device profiling (deferred, epic DoD)

---

## QA Test Cases

*Manual verification (UI story):*

- **Joystick spawn (AC-MOV-2)**
  - Setup: dev harness scene, Game view in a landscape iPhone resolution with a notch safe area, 1× scale.
  - Verify: touch (mouse-simulated or device) at three left-half points.
  - Pass condition: the joystick base centres on each touch point; nothing appears for right-half touches.
- **Thumb clamp and release**
  - Setup: as above.
  - Verify: drag 200 px from the anchor; release.
  - Pass condition: the thumb stops at the radius; base and thumb disappear on release.
- **Auto-run button**
  - Setup: as above.
  - Verify: tap the button, then tap again; pull the joystick backwards while on.
  - Pass condition: the state indicator changes on each tap; a rear pull turns it off; the logging sink shows forward intents while on.
- **Safe area**
  - Setup: device simulator with a notch, landscape both ways.
  - Verify: joystick zone and button positions.
  - Pass condition: nothing under the notch or home indicator; the zone midpoint follows the safe area.
- **Camera follow**
  - Setup: as above; drag in the right half.
  - Verify: the camera rotates around the stand-in, no judder while the character moves.
  - Pass condition: yaw changes 25° per 100 px; no visible jitter.

---

## Test Evidence

**Story Type**: UI
**Required evidence**: `production/qa/evidence/movement-013-touch-ui-evidence.md` (walkthrough + screenshots)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 010, 011, 012
- Unlocks: None (client epic work continues with client networking)
