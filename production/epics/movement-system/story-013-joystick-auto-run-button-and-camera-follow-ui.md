# Story 013: Joystick, Auto-Run Button and Camera Follow (UI)

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (7 gaps) → fixed → Ready
> **Layer**: Core (client presentation)
> **Type**: UI
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours (5 + 1 for the touch-mapping EditMode test)

## Context

**GDD**: `design/gdd/movement-system.md` — UI Requirements (Virtual Joystick, Auto-Run Toggle Button, Stun Overlay, Ownership), CR-MOV-1 (safe area), CR-MOV-3 (camera in `LateUpdate`); AC-MOV-2 (visual)
**Requirement**: `TR-mov-008`, `TR-mov-009` (advisory: neither is in `docs/architecture/tr-registry.yaml` yet)

**ADR Governing Implementation**: ADR-005 (HUD UI framework: UI Toolkit; Accepted 2026-06-27); ADR-012 (Accepted 2026-10-08)
**ADR Decision Summary**: HUD elements are UI Toolkit (UXML/USS) on a `UIDocument`; client-only code in `IronGrind.Client`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — UI Toolkit runtime on iOS; EnhancedTouch on Input System 1.18.0; `VisualElement.transform` is deprecated in 6.2+ (use `style.translate` / `style.left/top`)
**Engine Notes**:
- **Touch source = Input System `EnhancedTouch` (decided at readiness).** `ProjectSettings.asset` has `activeInputHandler: 1` (Input System only), so legacy `Input.touches` throws; `com.unity.inputsystem` 1.18.0 is in `Packages/manifest.json`. Add the `Unity.InputSystem` reference to `src/Client/IronGrind.Client.asmdef`.
- Call `EnhancedTouchSupport.Enable()` in the controller's `OnEnable` and `Disable()` in `OnDisable`. The dev harness also calls `TouchSimulation.Enable()` so the mouse drives touches in the Editor.
- Screen positions from EnhancedTouch are bottom-left origin (same as `TouchInput` and `Screen.safeArea`). UI Toolkit panel coordinates are top-left: convert with `RuntimePanelUtils.ScreenToPanel(panel, new Vector2(x, Screen.height - y))`.
- Position UI elements with `style.translate` or `style.left/top`, never `VisualElement.transform`.

**Control Manifest Rules (this layer)**:
- Required: safe-area insets (`HudSafeAreaApplier` pattern); everything reachable by thumbs in landscape
- Required: UI work routed to the UI specialist (technical preferences routing table)
- Forbidden: hover-only interactions

---

## Acceptance Criteria

- [ ] **Touch source adapter:** `EnhancedTouchSource` fills a pre-allocated `TouchInput[]` each frame from `Touch.activeTouches` through a pure `internal static` mapping (`Stationary` → `Moved`, `touch.touchId` → `TouchInput.Id`, never `finger.index`; positions unchanged, bottom-left origin), and passes the filled span to `TouchZoneRouter.ProcessFrame(span, Screen.safeArea)`.
- [ ] **Joystick (AC-MOV-2 visual):** a UI Toolkit ring + nub appears centred on the touch-start point of a joystick-zone touch (`JoystickState.Anchor`), the nub follows the drag clamped to `JOYSTICK_RADIUS_PX`, the nub opacity is 80% outside the dead zone and 50% inside it (`JoystickState.IsDead`), and both hide on release. Ring diameter = 2 × `JOYSTICK_RADIUS_PX` converted to panel units; nub diameter = half the ring.
- [ ] **Auto-run button:** shows its inactive / active look from `AutoRunState.IsActive` (instant swap, no animation) and calls `AutoRunState.PressButton()` on tap (`ClickEvent`); 44×44 pt tap target, right edge, inside the safe area, thumb-reachable in landscape.
- [ ] **Real `IUiHitTest`:** `UiToolkitHitTest(VisualElement root)` returns true when the panel element picked at the screen position (Y flipped, `ScreenToPanel`, `panel.Pick`) or one of its ancestors has the USS class `touch-blocker`. The auto-run button carries `touch-blocker`, so a tap on it starts no camera drag and no joystick (EC-MOV-4/5); future skill buttons use the same class.
- [ ] **Stun overlay placeholder:** a full-screen UI Toolkit element, `#7A8FA0` at 8% opacity, `picking-mode: Ignore`, visible while `IClientProhibitedSource.IsProhibited` is true, fade in 0.10 s / out 0.15 s through USS transitions. This replaces the GDD's URP Volume override for now (deviation noted; final effect is Presentation work).
- [ ] **Camera follow:** a `MovementCameraFollow` component updates in `LateUpdate` (CR-MOV-3), orbiting the target at `CameraYaw.YawDeg` with serialized `distance` (default 15 units, from the GDD VFX note) and `pitchDeg` (default 45°).
- [ ] **Client movement controller:** `ClientMovementController` each frame runs touch source → `router.ProcessFrame` → `cameraYaw.ApplyDrag(router.CameraDragDeltaPx)` → joystick / button / overlay view update; a 20 Hz accumulator (`1 / TICK_RATE_HZ`, fixed 0.050 s steps) calls `producer.OnTick(tick, router.Joystick, cameraYaw.FlatForward)` with a local `uint` tick counter (real tick driver: client networking). The sink is a logging `IMovementIntentSink` until the client networking epic exists.
- [ ] **Resets:** on `OnApplicationFocus(false)` / `OnApplicationPause(true)` the controller calls `router.Reset()`; a public `ResetMovement()` calls `router.Reset()` and `producer.Reset()` (which resets `AutoRunState`) — the harness calls it from a "Simulate zone entry" button; real zone-entry / reconnect callers come with client networking.
- [ ] **Dev harness:** `src/DevHarness/Movement/` holds a harness that moves a stand-in capsule locally each frame by `AutoRunState.MoveDirection × base MovementSpeed (MovementTuningValues) × Time.deltaTime` and rotates it to `AutoRunState.Facing` (harness-only, not client prediction), a fake `IClientProhibitedSource` toggle, the "Simulate zone entry" button, and `TouchSimulation.Enable()`. The scene is set up by hand and not committed (as for the leveling HUD harness); the evidence doc lists the setup steps.
- [ ] **Performance:** no per-frame managed allocation in the controller, touch source and views (pre-allocated `TouchInput[]`, no LINQ, no string building per frame); styles are written only when the value changes (joystick visible / position / dead flag, button active, overlay visible).

---

## Implementation Notes

- Spawn the `unity-ui-specialist` for the UXML/USS and the controller (technical preferences routing).
- Keep all decisions in Stories 010–012's logic classes; this story only adapts input and draws.
- Files: `Assets/UI/HUD/Movement/MovementHud.uxml` + `MovementHud.uss` (on the existing `HUD_PanelSettings.asset`); `src/Client/Movement/` for `EnhancedTouchSource`, `UiToolkitHitTest`, `ClientMovementController`, `MovementCameraFollow`, `LoggingMovementIntentSink`; `src/DevHarness/Movement/` for the harness and the fake Prohibited source. Apply `HudSafeAreaApplier.Apply` to the movement HUD root.
- Placeholder art: the GDD PNGs (`ui_joystick-ring_default_128`, `ui_joystick-nub_default_64`, `ui_btn_autorun_*_64`) do not exist; draw with USS `border-radius` and the GDD colours — ring `#3A3D44` 2 px outline, no fill; nub `#4A9EE0`; button inactive `#1A1C1F` fill, `#3A3D44` 2 px outline, label "AUTO" `#9A9DA6`; active `#4A9EE0` at 60%, label `#E8E6DF`.
- Joystick / overlay elements use `picking-mode: Ignore` so they never block the hit test.
- `JOYSTICK_RADIUS_PX` is in screen pixels; convert to panel units through `ScreenToPanel` (or the panel scale) — do not assume 1:1.
- Story 010 code-review carry-over is in the touch source criterion (`Stationary` → `Moved`, `touchId`, `Reset()` on focus loss / pause / source re-init).
- Memory note: the HUD harness buttons were once "missing" because the Game view Scale cropped them — check the Game view at 1× before filing a layout bug.

---

## Out of Scope

- Real network send and server position rendering/interpolation (client networking / Client-Side Prediction)
- The real client tick driver, real zone-entry / reconnect callers of `ResetMovement()`, and the real Prohibited source (no stun/root wire message yet)
- Final art (PNGs) and the URP post-process stun Volume
- Animation (`NormalizedSpeed`), VFX and audio (GDD Visual/Audio Requirements — later Presentation work)
- AC-MOV-22 device profiling (deferred, epic DoD)

---

## QA Test Cases

*Automated (EditMode):*

- **Touch mapping** — `tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs`
  - `Stationary` maps to `Moved`; `Began` / `Moved` / `Ended` / `Canceled` map one-to-one.
  - `TouchInput.Id` comes from `touchId`: two touches with the same finger index but different touch ids keep different ids.
  - The position passes through unchanged (bottom-left origin).
  - Filling more touches than the buffer holds writes up to the buffer length and reports the count (no allocation, no exception).

*Manual verification (UI story):*

- **Joystick spawn (AC-MOV-2)**
  - Setup: dev harness scene, Game view in a landscape iPhone resolution with a notch safe area, 1× scale.
  - Verify: touch (mouse-simulated or device) at three left-half points.
  - Pass condition: the joystick base centres on each touch point; nothing appears for right-half touches.
- **Thumb clamp, dead zone and release**
  - Setup: as above.
  - Verify: hold near the anchor, then drag 200 px from the anchor; release.
  - Pass condition: the nub is at 50% opacity near the anchor and 80% outside the dead zone; it stops at the radius; base and nub disappear on release.
- **Auto-run button**
  - Setup: as above.
  - Verify: tap the button, then tap again; pull the joystick backwards while on.
  - Pass condition: the look changes on each tap; tapping does not rotate the camera; a rear pull turns it off; the logging sink shows forward intents while on.
- **Stun overlay**
  - Setup: as above; turn the fake Prohibited toggle on and off.
  - Pass condition: the tint fades in and out; while on, the button tap is ignored and the stand-in does not move.
- **Reset**
  - Setup: auto-run on, joystick held.
  - Verify: press "Simulate zone entry"; also switch focus away from the Game view and back.
  - Pass condition: auto-run is off, the joystick is hidden until a new touch.
- **Safe area**
  - Setup: device simulator with a notch, landscape both ways.
  - Verify: joystick zone and button positions.
  - Pass condition: nothing under the notch or home indicator; the zone midpoint follows the safe area.
- **Camera follow**
  - Setup: as above; drag in the right half.
  - Verify: the camera rotates around the stand-in, no judder while the character moves.
  - Pass condition: yaw changes 25° per 100 px (AC-MOV-4); no visible jitter.
- **Allocation**
  - Setup: Profiler, harness running with a joystick drag and a camera drag.
  - Pass condition: GC Alloc 0 B per frame in the controller, touch source and views.

---

## Test Evidence

**Story Type**: UI
**Required evidence**: `production/qa/evidence/movement-013-touch-ui-evidence.md` (harness setup steps, walkthrough + screenshots) and `tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 010, 011, 012 (all Complete)
- Unlocks: None (client epic work continues with client networking)
