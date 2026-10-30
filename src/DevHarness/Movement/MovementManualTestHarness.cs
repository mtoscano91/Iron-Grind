using System.Threading;
using IronGrind.Client.Movement;
using IronGrind.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace IronGrind.DevHarness.Movement
{
    /// <summary>
    /// Manual-verification scaffolding for Movement Story 013 (joystick, auto-run button, stun overlay, camera
    /// follow). Not client prediction: it moves a stand-in locally from the auto-run state. Lives in the
    /// <c>IronGrind.DevHarness</c> assembly (Editor only).
    /// </summary>
    /// <remarks>
    /// Manual scene setup (the result is committed as <c>Assets/Scenes/DevHarness/MovementHarness.unity</c>; rebuild it with these steps if needed):
    /// <list type="number">
    /// <item><description>A GameObject with a <c>UIDocument</c> (Panel Settings <c>HUD_PanelSettings</c>, Source Asset <c>MovementHud.uxml</c>) and a <see cref="ClientMovementController"/>. If other HUD documents share the panel settings, give this one the highest Sort Order so the hit test sees its button.</description></item>
    /// <item><description>A capsule as the stand-in and a plain grey ground plane (the 8% stun tint is subtle).</description></item>
    /// <item><description>The Main Camera with <see cref="MovementCameraFollow"/>, Target = the capsule; assign it to the controller's Camera Follow field.</description></item>
    /// <item><description>An EventSystem with <c>InputSystemUIInputModule</c> (Input System only, <c>activeInputHandler: 1</c>).</description></item>
    /// <item><description>This harness on the UIDocument object: assign the controller and the capsule Transform.</description></item>
    /// <item><description>Game view at 1x scale, landscape, focused (click it). With "Use Mouse Input" ticked (default) the left mouse button acts as one finger, fed straight to the touch router (TouchSimulation did not reach EnhancedTouch on a machine with a hardware touchscreen). For the safe-area case use the Device Simulator view with a notched iPhone, landscape left and right, and untick "Use Mouse Input" so the Simulator's touches go through EnhancedTouch.</description></item>
    /// </list>
    /// Controls: a "Simulate zone entry" button (or the R key, usable while the mouse holds the joystick), a
    /// "Prohibited" toggle and a "Hitch 400 ms" button, all carrying the <c>touch-blocker</c> class. A debug label
    /// shows the yaw, the summed camera drag, the dead-zone flag and the tick count; a vertical line marks the
    /// safe-area midpoint (the joystick / camera zone split). Each auto-run click is logged, so a double delivery
    /// (mouse plus simulated touch) shows as two lines per tap. Tick "Allocation mode" in the Inspector before Play
    /// to use a silent sink and freeze the label, then profile <c>ClientMovementController.Update</c> with Deep
    /// Profile (Editor numbers are advisory; a Development build is authoritative).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MovementManualTestHarness : MonoBehaviour
    {
        private const int HITCH_MS = 400;

        [SerializeField] private ClientMovementController _controller;
        [SerializeField] private Transform _standIn;
        [SerializeField] private bool _allocationMode;
        [SerializeField] private bool _useMouseInput = true;

        private readonly FakeProhibitedSource _prohibited = new FakeProhibitedSource();
        private readonly CountingSink _countingSink = new CountingSink();
        private UIDocument _document;
        private VisualElement _builtRoot;
        private Label _debugLabel;
        private VisualElement _midLine;
        private Vector2 _dragTotalPx;
        private Rect _shownSafeArea;
        private float _shownYaw = float.NaN;
        private bool _shownDead;
        private int _shownTicks = -1;

        private void Awake()
        {
            if (_controller == null)
            {
                Debug.LogError("MovementManualTestHarness: assign the ClientMovementController.");
                enabled = false;
                return;
            }

            _document = GetComponent<UIDocument>();

            if (_useMouseInput)
            {
                _controller.SetTouchSource(new MouseTouchSource());
            }

            _countingSink.Inner = _allocationMode ? null : new LoggingMovementIntentSink();
            _controller.Initialize(_prohibited, _countingSink);
        }

        private void Update()
        {
            // The UIDocument rebuilds its tree on re-enable, dropping the debug controls: re-add them to the new root.
            VisualElement root = _document.rootVisualElement;
            if (root != null && root != _builtRoot)
            {
                BuildDebugControls(root);
            }

            AutoRunState state = _controller.State;
            if (state == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                _controller.ResetMovement();
            }

            MoveStandIn(state);
            if (!_allocationMode)
            {
                UpdateDebugReadout();
            }
        }

        private void MoveStandIn(AutoRunState state)
        {
            if (_standIn == null)
            {
                return;
            }

            _standIn.position += state.MoveDirection * (_controller.Tuning.BaseMoveSpeed * Time.deltaTime);
            Vector3 forward = state.Facing.Forward;
            if (forward != Vector3.zero)
            {
                _standIn.rotation = Quaternion.LookRotation(forward);
            }
        }

        private void UpdateDebugReadout()
        {
            _dragTotalPx += _controller.CameraDragDeltaPx;
            float yaw = _controller.Yaw.YawDeg;
            bool dead = _controller.Joystick.IsDead;
            if (yaw != _shownYaw || dead != _shownDead || _countingSink.Count != _shownTicks)
            {
                _shownYaw = yaw;
                _shownDead = dead;
                _shownTicks = _countingSink.Count;
                _debugLabel.text = $"yaw {yaw:F1}°  drag x {_dragTotalPx.x:F0} px  dead {dead}  intents {_shownTicks}";
            }

            Rect safeArea = Screen.safeArea;
            IPanel panel = _midLine.panel;
            if (panel != null && safeArea != _shownSafeArea)
            {
                _shownSafeArea = safeArea;
                float midX = safeArea.x + safeArea.width * 0.5f;
                _midLine.style.left = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(midX, 0f)).x;
            }
        }

        private void BuildDebugControls(VisualElement root)
        {
            _builtRoot = root;
            _shownSafeArea = default;
            _shownYaw = float.NaN;

            _midLine = new VisualElement { pickingMode = PickingMode.Ignore };
            _midLine.style.position = Position.Absolute;
            _midLine.style.top = 0;
            _midLine.style.bottom = 0;
            _midLine.style.width = 1;
            _midLine.style.backgroundColor = new Color(1f, 1f, 0f, 0.5f);
            root.Add(_midLine);

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.position = Position.Absolute;
            row.style.top = 4;
            row.style.left = 0;
            row.style.right = 0;
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;

            var resetButton = new Button(_controller.ResetMovement) { text = "Simulate zone entry (R)" };
            resetButton.AddToClassList(UiToolkitHitTest.TOUCH_BLOCKER_CLASS);

            var prohibitedToggle = new Toggle("Prohibited");
            prohibitedToggle.AddToClassList(UiToolkitHitTest.TOUCH_BLOCKER_CLASS);
            prohibitedToggle.RegisterValueChangedCallback(evt => _prohibited.Value = evt.newValue);

            var hitchButton = new Button(() => Thread.Sleep(HITCH_MS)) { text = "Hitch 400 ms" };
            hitchButton.AddToClassList(UiToolkitHitTest.TOUCH_BLOCKER_CLASS);

            _debugLabel = new Label { pickingMode = PickingMode.Ignore };
            _debugLabel.style.color = Color.white;

            row.Add(resetButton);
            row.Add(prohibitedToggle);
            row.Add(hitchButton);
            row.Add(_debugLabel);
            root.Add(row);

            Button autoRun = root.Q<Button>("AutoRunButton");
            autoRun?.RegisterCallback<ClickEvent>(_ => Debug.Log("[MovementHarness] auto-run click, frame " + Time.frameCount));
        }

        // Editor stand-in for a finger: the left mouse button as touch id 0 (TouchSimulation did not feed EnhancedTouch
        // on a machine with a hardware touchscreen). Input System mouse positions are bottom-left screen pixels.
        private sealed class MouseTouchSource : ITouchFrameSource
        {
            private const int MOUSE_TOUCH_ID = 0;
            private readonly TouchInput[] _buffer = new TouchInput[2];

            public void Enable()
            {
            }

            public void Disable()
            {
            }

            public System.ReadOnlySpan<TouchInput> ReadFrame()
            {
                Mouse mouse = Mouse.current;
                if (mouse == null)
                {
                    return System.ReadOnlySpan<TouchInput>.Empty;
                }

                ButtonControl button = mouse.leftButton;
                Vector2 position = mouse.position.ReadValue();
                int count = 0;
                if (button.wasPressedThisFrame)
                {
                    _buffer[count++] = new TouchInput(MOUSE_TOUCH_ID, TouchInputPhase.Began, position);
                }
                else if (button.isPressed)
                {
                    _buffer[count++] = new TouchInput(MOUSE_TOUCH_ID, TouchInputPhase.Moved, position);
                }

                // A click pressed and released within one frame sends Began then Ended, so no owner is left behind.
                if (button.wasReleasedThisFrame)
                {
                    _buffer[count++] = new TouchInput(MOUSE_TOUCH_ID, TouchInputPhase.Ended, position);
                }

                return new System.ReadOnlySpan<TouchInput>(_buffer, 0, count);
            }
        }

        private sealed class FakeProhibitedSource : IClientProhibitedSource
        {
            public bool Value;
            public bool IsProhibited => Value;
        }

        // Counts intents for the readout and forwards them to the logging sink (or nowhere in allocation mode).
        private sealed class CountingSink : IMovementIntentSink
        {
            public IMovementIntentSink Inner;
            public int Count;

            public void Send(in MovementIntentMessage message)
            {
                Count++;
                Inner?.Send(message);
            }
        }
    }
}
