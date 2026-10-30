# Story 013: Joystick, Auto-Run Button and Camera Follow (UI)

> **Epic**: Movement System
> **Status**: Complete — 2026-10-29 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (7 gaps) → fixed → Ready
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

- [x] **Touch source adapter:** `EnhancedTouchSource` fills a pre-allocated `TouchInput[]` each frame from `Touch.activeTouches` through a pure `internal static` mapping (`Stationary` → `Moved`, `touch.touchId` → `TouchInput.Id`, never `finger.index`; positions unchanged, bottom-left origin), and passes the filled span to `TouchZoneRouter.ProcessFrame(span, Screen.safeArea)`.
- [x] **Joystick (AC-MOV-2 visual):** a UI Toolkit ring + nub appears centred on the touch-start point of a joystick-zone touch (`JoystickState.Anchor`), the nub follows the drag clamped to `JOYSTICK_RADIUS_PX`, the nub opacity is 80% outside the dead zone and 50% inside it (`JoystickState.IsDead`), and both hide on release. Ring diameter = 2 × `JOYSTICK_RADIUS_PX` converted to panel units; nub diameter = half the ring.
- [x] **Auto-run button:** shows its inactive / active look from `AutoRunState.IsActive` (instant swap, no animation) and calls `AutoRunState.PressButton()` on tap (`ClickEvent`); 44×44 pt tap target, right edge, inside the safe area, thumb-reachable in landscape.
- [x] **Real `IUiHitTest`:** `UiToolkitHitTest(VisualElement root)` returns true when the panel element picked at the screen position (Y flipped, `ScreenToPanel`, `panel.Pick`) or one of its ancestors has the USS class `touch-blocker`. The auto-run button carries `touch-blocker`, so a tap on it starts no camera drag and no joystick (EC-MOV-4/5); future skill buttons use the same class.
- [x] **Stun overlay placeholder:** a full-screen UI Toolkit element, `#7A8FA0` at 8% opacity, `picking-mode: Ignore`, visible while `IClientProhibitedSource.IsProhibited` is true, fade in 0.10 s / out 0.15 s through USS transitions. This replaces the GDD's URP Volume override for now (deviation noted; final effect is Presentation work).
- [x] **Camera follow:** a `MovementCameraFollow` component updates in `LateUpdate` (CR-MOV-3), orbiting the target at `CameraYaw.YawDeg` with serialized `distance` (default 15 units, from the GDD VFX note) and `pitchDeg` (default 45°).
- [x] **Client movement controller:** `ClientMovementController` each frame runs touch source → `router.ProcessFrame` → `cameraYaw.ApplyDrag(router.CameraDragDeltaPx)` → joystick / button / overlay view update; a 20 Hz accumulator (`1 / TICK_RATE_HZ`, fixed 0.050 s steps) calls `producer.OnTick(tick, router.Joystick, cameraYaw.FlatForward)` with a local `uint` tick counter (real tick driver: client networking). The sink is a logging `IMovementIntentSink` until the client networking epic exists.
- [x] **Resets:** on `OnApplicationFocus(false)` / `OnApplicationPause(true)` the controller calls `router.Reset()`; a public `ResetMovement()` calls `router.Reset()` and `producer.Reset()` (which resets `AutoRunState`) — the harness calls it from a "Simulate zone entry" button; real zone-entry / reconnect callers come with client networking.
- [x] **Dev harness:** `src/DevHarness/Movement/` holds a harness that moves a stand-in capsule locally each frame by `AutoRunState.MoveDirection × base MovementSpeed (MovementTuningValues) × Time.deltaTime` and rotates it to `AutoRunState.Facing` (harness-only, not client prediction), a fake `IClientProhibitedSource` toggle, the "Simulate zone entry" button, and `TouchSimulation.Enable()`. The scene was set up by hand and committed as `Assets/Scenes/DevHarness/MovementHarness.unity` (user decision 2026-10-29); the harness doc comment lists the setup steps. (TouchSimulation was later replaced by the harness mouse source — Completion Notes, deviation 2.)
- [x] **Performance:** no per-frame managed allocation in the controller, touch source and views (pre-allocated `TouchInput[]`, no LINQ, no string building per frame); styles are written only when the value changes (joystick visible / position / dead flag, button active, overlay visible).

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
  - Pass condition: the look changes on each tap and the harness logs one click line per tap (two lines per tap = mouse and simulated touch both delivered; note it in the evidence and retest on device); tapping does not rotate the camera (yaw readout unchanged); a rear pull past the dead zone turns it off; while on, the logging sink shows intents whose direction matches the camera's flat forward.
- **Stun overlay**
  - Setup: as above; turn the fake Prohibited toggle on and off.
  - Pass condition: the tint fades in and out; while on, the button tap is ignored and the stand-in does not move.
- **Zone-entry reset**
  - Setup: auto-run on; hold the joystick with the mouse.
  - Verify: press R while holding (in the Editor the mouse cannot also click the button; on device, hold with one finger and tap the button with another).
  - Pass condition: auto-run is off and the joystick is hidden; it does not come back until a new touch begins (dragging the same held mouse does not re-spawn it).
- **Focus-loss reset**
  - Setup: hold the joystick with the mouse.
  - Verify: switch focus away from the Game view and back (Alt+Tab).
  - Pass condition: the joystick is hidden until a new touch; auto-run is unchanged (focus loss resets the touch router only, per the Resets criterion).
- **Re-enable**
  - Setup: hold a joystick drag; disable the UIDocument GameObject in the Hierarchy, then enable it.
  - Pass condition: no joystick is shown after re-enable; the auto-run button still toggles; a tap on it still starts no camera drag (yaw readout unchanged).
- **Hitch**
  - Setup: auto-run on; press "Hitch 400 ms".
  - Pass condition: the intent count advances by at most 5 in the frame after the hitch; tick numbers in the log stay increasing; no error.
- **Safe area**
  - Setup: Device Simulator view, a notched iPhone, landscape left then landscape right (the Game view has no notch).
  - Verify: button position and the yellow midpoint line; start touches just left and just right of the line.
  - Pass condition: nothing under the notch or home indicator; the line sits at the safe-area midpoint and moves when the orientation changes; touches left of it spawn the joystick, right of it rotate the camera; the button stays inside the safe area after the change.
- **Camera follow**
  - Setup: Game view at 1×; drag right in the right half while the stand-in moves.
  - Verify: read the debug label's "drag x" and "yaw" before and after the drag.
  - Pass condition: Δyaw = 0.25 × Δdrag-x ± 0.5° (AC-MOV-4), yaw increases on a rightward drag; the camera orbits the stand-in with no visible jitter.
- **Allocation**
  - Setup: tick "Allocation mode" on the harness before Play (silent sink, frozen label); Profiler with Deep Profile; hold a joystick drag and a camera drag.
  - Pass condition: GC Alloc 0 B per frame under `ClientMovementController.Update` (touch source and views included). Editor result is advisory; repeat in a Development build when one exists.

---

## Test Evidence

**Story Type**: UI
**Required evidence**: `production/qa/evidence/movement-013-touch-ui-evidence.md` (harness setup steps, walkthrough + screenshots) and `tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs`

**Status**: [x] Created — `production/qa/evidence/movement-013-touch-ui-evidence.md` (13 manual cases passed, 8 screenshots in `production/qa/evidence/movement-013/`) and `tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs` (15 tests); EditMode 5431 / 5302 passed / 0 failed / 129 skipped (2026-10-30 01:12Z)

---

## Dependencies

- Depends on: Stories 010, 011, 012 (all Complete)
- Unlocks: None (client epic work continues with client networking)

---

## Completion Notes
**Completed**: 2026-10-29 (`/story-done`, lean — QL-TEST-COVERAGE and LP-CODE-REVIEW skipped)
**Criteria**: 10/10 — touch source and hit-test ancestry by EditMode test; every criterion by the manual walkthrough (`production/qa/evidence/movement-013-touch-ui-evidence.md`); allocation 0 B is an Editor result (advisory).
**Deviations** (accepted, notes only):
1. `IronGrind.Client` and `IronGrind.DevHarness` reference `Unity.InputSystem`; ADR-012 dated note 2026-10-29, boundary tests updated (user decision).
2. New `ITouchFrameSource` seam + `ClientMovementController.SetTouchSource()`; the harness feeds the mouse as one finger (`MouseTouchSource`, toggle "Use Mouse Input") because TouchSimulation did not reach EnhancedTouch on a machine with a hardware touchscreen. The Device Simulator drove the EnhancedTouch path.
3. The stun overlay is a full-screen sibling of the safe-area root and a UI Toolkit placeholder (not the GDD URP Volume).
4. The joystick is positioned with `style.left/top` + `WorldToLocal`, not `style.translate` (ADR-005 positional-animation note; minor).
5. Tick catch-up capped at 5 ticks per frame (excess time dropped); `CLIENT_TICK_RATE_HZ` = 20 pinned to `ServerTickLoop.TICK_RATE_HZ` by test.
6. Verification limits: case 11 readout values not recorded; Editor only (no iPhone run); `/code-review` (CHANGES REQUIRED → all applied) not re-run after the review fixes and the walkthrough fixes.
**Test Evidence**: UI — evidence doc above; EditMode `tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs`.
**Code Review**: Complete 2026-10-29 (unity-ui-specialist + qa-tester; fixes applied; no re-run).
**Follow-ups**: TR-mov-008 / TR-mov-009 not in `tr-registry.yaml`; real tick driver, zone-entry / reconnect callers of `ResetMovement()` and real Prohibited source come with client networking; device check (touch, panel scale, 44 pt target) with AC-MOV-22 profiling.
