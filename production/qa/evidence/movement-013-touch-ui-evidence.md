# Test Evidence: Movement Story 013 — Joystick, Auto-Run Button and Camera Follow (UI)

> **Story**: `production/epics/movement-system/story-013-joystick-auto-run-button-and-camera-follow-ui.md`
> **Story Type**: UI
> **Verified**: 2026-10-29
> **Verified by**: Manuel Toscano, in a live Unity Editor (6000.3.10f1) Play Mode session, using the dev harness (`MovementManualTestHarness`, Editor-only `IronGrind.DevHarness` assembly)
> **Result**: all 13 manual cases passed (user report: "all tests passed as expected"); screenshots in `production/qa/evidence/movement-013/`
> **Automated**: `tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs` (15 tests) — see "Automated evidence" below

---

## Setup

Scene `Assets/Scenes/DevHarness/MovementHarness.unity`, built by hand and committed in `5ce9ab8` (user decision). The steps are in the class doc comment of `src/DevHarness/Movement/MovementManualTestHarness.cs`.

- **Scene contents:** URP Basic scene, ground plane, capsule stand-in with a "nose" cube, and an EventSystem with `InputSystemUIInputModule`.
- **`MovementHUD` object:** a `UIDocument` (`HUD_PanelSettings` + `MovementHud.uxml`), plus `ClientMovementController` and `MovementManualTestHarness`.
- **Main Camera:** `MovementCameraFollow` (distance 15, pitch 45).
- **Game view:** 1280×720 at scale 0.72×. At 1× the panel cropped the top row and the AUTO button, the same Game-view cropping that once hid the leveling harness buttons. Scale doesn't affect the measured values: touch positions, the drag readout and the joystick size are all in game-resolution pixels.
- **Safe-area case:** Device Simulator, Apple iPhone 13 Pro, landscape both ways.

## Problems found and fixed during the walkthrough

1. **TouchSimulation did not reach EnhancedTouch on this machine.**
   - The machine has a hardware touchscreen: the Input Debugger showed `Touchscreen` and no `SimulatedTouchscreen`, and its values did not change while the mouse dragged.
   - UI Toolkit clicks (AUTO) worked; the joystick and camera did not.
   - **Fix:** a new `ITouchFrameSource` seam. `EnhancedTouchSource` implements it, and `ClientMovementController.SetTouchSource()` swaps it. The harness feeds the left mouse button as one finger through an Editor-only `MouseTouchSource`, behind the harness toggle "Use Mouse Input" (default on). TouchSimulation was removed from the harness.
   - The game path (EnhancedTouch) is unchanged. The Device Simulator, with "Use Mouse Input" off, drove it successfully: screenshot `12-safe-area-left.png` shows a joystick spawned from a Simulator touch.
2. **`NullReferenceException` in `ClientMovementController.OnEnable`** after a script reload in Play mode (Unity calls `OnEnable` without `Awake` and clears non-serialized fields). **Fix:** `OnEnable` creates the touch source if it is missing. Recompiling during Play still leaves the controller inert until Play restarts; that's expected Editor behaviour.
3. **Case 9, first run: the harness's debug row and yellow line did not come back after re-enable.** The UIDocument rebuilds its tree, and the harness added its elements only in `Start`. **Fix:** the harness re-adds its controls whenever the UIDocument root changes. The movement HUD itself (AUTO button, joystick) came back on the first run. Case 9 passed on the re-run.

## Results

| # | Case | Result | Evidence |
|---|---|---|---|
| 1 | Joystick spawn (AC-MOV-2): the ring is centred on the press point in the left half; nothing appears in the right half | Pass | `01-joystick-spawn.png` |
| 2 | Dead zone / clamp / release: the nub is faded inside the dead zone (`dead True`) and solid outside, stops at the ring edge, and hides on release | Pass | `02-dead-zone.png`, `02-clamped.png` |
| 3 | Joystick movement: the stand-in moves and turns camera-relative; the logging sink prints intents | Pass | — |
| 4 | Auto-run button: toggles its look, one click log line per tap, yaw unchanged by the tap, a rear pull turns it off | Pass | `04-autorun-on.png` |
| 5 | Auto-run follows the camera on a right-half drag | Pass | — |
| 6 | Stun overlay: tint fades in and out; stand-in stops; AUTO ignored while Prohibited | Pass | `06-stun.png` |
| 7 | Zone-entry reset (R while holding): auto-run off, joystick hidden until a new touch | Pass | — |
| 8 | Focus-loss reset: joystick hidden; auto-run unchanged (router-only reset) | Pass | — |
| 9 | Re-enable of the HUD object: no stale joystick, AUTO toggles and still blocks camera drags, no errors | Pass (after fix 3) | — |
| 10 | Hitch 400 ms: no error, no teleport, tick numbers increasing | Pass | — |
| 11 | Camera sensitivity (AC-MOV-4): Δyaw = 0.25 × Δdrag-x ± 0.5°, rightward drag raises yaw, no jitter | Pass (the readout values were not written down) | — |
| 12 | Safe area, iPhone 13 Pro landscape left and right: AUTO and the top row inside the safe area, midpoint line at the safe-area centre, touches split at the line | Pass | `12-safe-area-left.png`, `12-safe-area-right.png` |
| 13 | Allocation: `ClientMovementController.Update() [Invoke]` GC Alloc in Allocation Mode, auto-run + camera drag and joystick drag recordings | Pass: 0 B on every frame checked | `13-gc-alloc.png` |

## Notes and limits

- **Editor only.** No device run yet. Touch on a real iPhone, panel scale on device, and the 44 pt tap target in physical units remain to be checked when an iOS build exists. This falls under AC-MOV-22 device profiling, which is deferred to the epic DoD. The allocation result is advisory until it's repeated in a Development build.
- **Mouse as finger.** Cases 1–11 used the harness mouse source, which feeds the same `TouchZoneRouter.ProcessFrame` path as EnhancedTouch. Multi-touch (a second finger while the joystick is held) was not exercised in the Editor.
- **Stun overlay** is a UI Toolkit placeholder, not the GDD's URP post-process Volume (a story deviation; the final effect is Presentation work).
- **Placeholder art.** Ring, nub and button are drawn in USS with the GDD colours; the GDD PNGs do not exist yet.

## Automated evidence

`tests/EditMode/Client/Movement/ClientMovement_TouchSource_tests.cs` covers:
- the EnhancedTouch → `TouchInput` mapping: Stationary → Moved, None skipped, id from `touchId`, position unchanged, buffer cap;
- the `touch-blocker` ancestry check and a hit test with no panel;
- the pin `CLIENT_TICK_RATE_HZ == ServerTickLoop.TICK_RATE_HZ`.

Full EditMode run after the walkthrough fixes (batch mode, Editor closed): 5431 tests, 5302 passed, 0 failed, 129 skipped, 0 compile errors (2026-10-30 01:12Z).
