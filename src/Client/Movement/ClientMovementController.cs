using IronGrind.MovementSystem;
using IronGrind.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Client movement loop and views (Movement Story 013). Each frame: touch source, router, camera yaw, a 20 Hz
    /// tick accumulator that feeds <see cref="MovementIntentProducer"/>, then the joystick, auto-run button and stun
    /// overlay views. All decisions live in the Story 010-012 logic classes; this component adapts input and draws.
    /// No per-frame managed allocation; styles are written only when a value changes.
    /// </summary>
    /// <remarks>
    /// The UIDocument rebuilds its tree when it is re-enabled, so <c>OnEnable</c> re-fetches the root, retargets the
    /// hit test and re-binds the views. <c>OnDisable</c> drops touch owners (an Ended touch is never delivered while
    /// EnhancedTouch is off).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ClientMovementController : MonoBehaviour
    {
        /// <summary>
        /// Client intent rate in Hz. Must equal <c>ServerTickLoop.TICK_RATE_HZ</c> (the client cannot reference
        /// ServerLogic); a unit test pins the equality. Placeholder for the real tick driver (client networking).
        /// </summary>
        public const int CLIENT_TICK_RATE_HZ = 20;

        private const float TICK_STEP_SECONDS = 1f / CLIENT_TICK_RATE_HZ;
        private const int MAX_CATCHUP_TICKS = 5;
        private const float NUB_OPACITY_ACTIVE = 0.8f;
        private const float NUB_OPACITY_DEAD = 0.5f;
        private const float NUB_TO_RING_RATIO = 0.5f;
        private const string ROOT_NAME = "MovementHudRoot";
        private const string RING_NAME = "JoystickRing";
        private const string NUB_NAME = "JoystickNub";
        private const string BUTTON_NAME = "AutoRunButton";
        private const string STUN_NAME = "StunOverlay";
        private const string AUTORUN_ACTIVE_CLASS = "autorun--active";
        private const string STUN_VISIBLE_CLASS = "stun-overlay--visible";

        [SerializeField] private MovementCameraFollow _cameraFollow;

        private IClientProhibitedSource _prohibited;
        private IMovementIntentSink _sink;
        private bool _started;

        private MovementTuningValues _tuning;
        private ITouchFrameSource _source;
        private UiToolkitHitTest _hitTest;
        private TouchZoneRouter _router;
        private CameraYaw _cameraYaw;
        private AutoRunState _autoRun;
        private MovementIntentProducer _producer;
        private uint _tick;
        private float _accumulator;

        private VisualElement _root;
        private VisualElement _hudRoot;
        private float _ringHalfSize;
        private float _nubHalfSize;
        private VisualElement _ring;
        private VisualElement _nub;
        private VisualElement _stunOverlay;
        private Button _autoRunButton;
        private bool _viewsBound;

        // Safe area last applied (re-applied when the safe area or the screen size changes).
        private bool _safeAreaApplied;
        private Rect _appliedSafeArea;
        private Vector2Int _appliedScreenSize;

        // Last written view values.
        private bool _shownVisible;
        private Vector2 _shownAnchor;
        private Vector2 _shownCurrent;
        private bool _shownDead;
        private bool _shownAutoRun;
        private bool _shownProhibited;

        /// <summary>The auto-run state (flag, move direction, facing); null before Start.</summary>
        public AutoRunState State => _autoRun;

        /// <summary>The camera yaw; null before Start.</summary>
        public CameraYaw Yaw => _cameraYaw;

        /// <summary>The latest joystick state; <see cref="JoystickState.Inactive"/> before Start.</summary>
        public JoystickState Joystick => _router != null ? _router.Joystick : JoystickState.Inactive;

        /// <summary>This frame's camera drag in screen pixels (zero before Start).</summary>
        public Vector2 CameraDragDeltaPx => _router != null ? _router.CameraDragDeltaPx : Vector2.zero;

        /// <summary>The tuning values the router and the views use.</summary>
        public MovementTuningValues Tuning => _tuning;

        /// <summary>
        /// Supplies the Prohibited source and the intent sink. Call before Start; if nothing has by Start, a
        /// never-prohibited source and a <see cref="LoggingMovementIntentSink"/> are used. Called after Start, it
        /// rebuilds the logic (tick counter restarts at zero).
        /// </summary>
        /// <param name="prohibited">The Prohibited (stun/root) source.</param>
        /// <param name="sink">Where intent messages go.</param>
        public void Initialize(IClientProhibitedSource prohibited, IMovementIntentSink sink)
        {
            _prohibited = prohibited;
            _sink = sink;
            if (_started)
            {
                BuildLogic();
                UnbindViews();
            }
        }

        /// <summary>
        /// Replaces the touch source (default <see cref="EnhancedTouchSource"/>); the dev harness uses it for mouse
        /// input in the Editor. Safe to call at any time: the old source is disabled, the new one enabled if this
        /// component is, and touch owners are dropped.
        /// </summary>
        /// <param name="source">The new source; must not be null.</param>
        public void SetTouchSource(ITouchFrameSource source)
        {
            if (source == null)
            {
                throw new System.ArgumentNullException(nameof(source));
            }

            if (isActiveAndEnabled)
            {
                _source?.Disable();
                source.Enable();
            }

            _source = source;
            _router?.Reset();
        }

        /// <summary>Zone entry / reconnect: drops touch owners and resets the producer and auto-run state.</summary>
        public void ResetMovement()
        {
            _router?.Reset();
            _producer?.Reset();
        }

        private void Awake()
        {
            _tuning = MovementTuningValues.Defaults;
            _source ??= new EnhancedTouchSource();
        }

        private void OnEnable()
        {
            // A script reload in Play mode calls OnEnable without Awake and clears non-serialized fields.
            _source ??= new EnhancedTouchSource();
            _source.Enable();
            if (_started)
            {
                AttachRoot();
            }
        }

        private void OnDisable()
        {
            _source.Disable();
            _router?.Reset();
            _accumulator = 0f;
            UnbindViews();
        }

        private void Start()
        {
            _started = true;
            BuildLogic();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _router?.Reset();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _router?.Reset();
            }
        }

        private void BuildLogic()
        {
            _prohibited ??= new NeverProhibitedSource();
            _sink ??= new LoggingMovementIntentSink();

            _hitTest = new UiToolkitHitTest(null);
            _router = new TouchZoneRouter(_tuning, _hitTest);
            _cameraYaw = new CameraYaw(_tuning);
            _autoRun = new AutoRunState(_prohibited);
            _producer = new MovementIntentProducer(_autoRun, _sink);
            _tick = 0;
            _accumulator = 0f;
            if (_cameraFollow != null)
            {
                _cameraFollow.SetYawSource(_cameraYaw);
            }

            AttachRoot();
        }

        // Re-fetches the UIDocument root (rebuilt on re-enable) and retargets the hit test; views re-bind next Update.
        private void AttachRoot()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _hitTest?.SetRoot(_root);
            UnbindViews();
            if (_root == null)
            {
                Debug.LogError("ClientMovementController: the UIDocument has no root (Panel Settings or Source Asset missing).");
            }
        }

        private void Update()
        {
            if (_router == null)
            {
                return;
            }

            if (!_viewsBound)
            {
                BindViews();
            }

            if (_viewsBound)
            {
                ApplySafeAreaIfChanged();
            }

            _router.ProcessFrame(_source.ReadFrame(), Screen.safeArea);
            _cameraYaw.ApplyDrag(_router.CameraDragDeltaPx);
            RunTicks(Time.deltaTime);

            if (_viewsBound)
            {
                UpdateViews();
            }
        }

        private void RunTicks(float deltaTime)
        {
            _accumulator += deltaTime;
            int ticks = 0;
            while (_accumulator >= TICK_STEP_SECONDS)
            {
                _accumulator -= TICK_STEP_SECONDS;
                if (ticks < MAX_CATCHUP_TICKS)
                {
                    _producer.OnTick(++_tick, _router.Joystick, _cameraYaw.FlatForward);
                    ticks++;
                }
            }
        }

        private void ApplySafeAreaIfChanged()
        {
            if (_root.panel == null)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (_safeAreaApplied && safeArea == _appliedSafeArea && screenSize == _appliedScreenSize)
            {
                return;
            }

            HudSafeAreaApplier.Apply(_hudRoot);
            _safeAreaApplied = true;
            _appliedSafeArea = safeArea;
            _appliedScreenSize = screenSize;
            _shownVisible = false; // Ring size depends on the panel scale: recompute on the next show.
            HideJoystick();
        }

        private void BindViews()
        {
            if (_root == null)
            {
                return;
            }

            _hudRoot = _root.Q<VisualElement>(ROOT_NAME);
            _ring = _hudRoot?.Q<VisualElement>(RING_NAME);
            _nub = _hudRoot?.Q<VisualElement>(NUB_NAME);
            _stunOverlay = _root.Q<VisualElement>(STUN_NAME);
            _autoRunButton = _hudRoot?.Q<Button>(BUTTON_NAME);
            if (_hudRoot == null || _ring == null || _nub == null || _stunOverlay == null || _autoRunButton == null)
            {
                Debug.LogError("ClientMovementController: MovementHud.uxml elements are missing.");
                _hudRoot = null;
                _root = null;
                return;
            }

            _autoRunButton.RegisterCallback<ClickEvent>(OnAutoRunClicked);

            // Sync every view with the current state, whatever the tree showed before.
            _shownVisible = false;
            HideJoystick();
            _shownAutoRun = _autoRun.IsActive;
            _autoRunButton.EnableInClassList(AUTORUN_ACTIVE_CLASS, _shownAutoRun);
            _shownProhibited = _prohibited.IsProhibited;
            _stunOverlay.EnableInClassList(STUN_VISIBLE_CLASS, _shownProhibited);
            _viewsBound = true;
        }

        private void UnbindViews()
        {
            if (_autoRunButton != null)
            {
                _autoRunButton.UnregisterCallback<ClickEvent>(OnAutoRunClicked);
            }

            _autoRunButton = null;
            _viewsBound = false;
            _safeAreaApplied = false;
        }

        private void OnAutoRunClicked(ClickEvent evt)
        {
            _autoRun.PressButton();
        }

        private void UpdateViews()
        {
            UpdateJoystickView();

            bool autoRun = _autoRun.IsActive;
            if (autoRun != _shownAutoRun)
            {
                _shownAutoRun = autoRun;
                _autoRunButton.EnableInClassList(AUTORUN_ACTIVE_CLASS, autoRun);
            }

            bool prohibited = _prohibited.IsProhibited;
            if (prohibited != _shownProhibited)
            {
                _shownProhibited = prohibited;
                _stunOverlay.EnableInClassList(STUN_VISIBLE_CLASS, prohibited);
            }
        }

        private void UpdateJoystickView()
        {
            IPanel panel = _root.panel;
            if (panel == null)
            {
                return;
            }

            JoystickState joystick = _router.Joystick;
            if (!joystick.IsActive)
            {
                if (_shownVisible)
                {
                    _shownVisible = false;
                    HideJoystick();
                }

                return;
            }

            bool justShown = !_shownVisible;
            if (justShown)
            {
                ShowJoystick(panel);
            }

            if (justShown || joystick.IsDead != _shownDead)
            {
                _shownDead = joystick.IsDead;
                _nub.style.opacity = joystick.IsDead ? NUB_OPACITY_DEAD : NUB_OPACITY_ACTIVE;
            }

            Vector2 nubScreen = ClampedNub(joystick);
            if (justShown || joystick.Anchor != _shownAnchor || nubScreen != _shownCurrent)
            {
                _shownAnchor = joystick.Anchor;
                _shownCurrent = nubScreen;
                Center(_ring, ScreenToLocal(panel, joystick.Anchor), _ringHalfSize);
                Center(_nub, ScreenToLocal(panel, nubScreen), _nubHalfSize);
            }
        }

        // Shows ring and nub and sizes them from JoystickRadiusPx in the current panel scale.
        private void ShowJoystick(IPanel panel)
        {
            _shownVisible = true;
            float ringDiameter = ScreenLengthToPanel(panel, 2f * _tuning.JoystickRadiusPx);
            float nubDiameter = ringDiameter * NUB_TO_RING_RATIO;
            _ring.style.width = ringDiameter;
            _ring.style.height = ringDiameter;
            _nub.style.width = nubDiameter;
            _nub.style.height = nubDiameter;
            _ringHalfSize = ringDiameter * 0.5f;
            _nubHalfSize = nubDiameter * 0.5f;
            _ring.style.display = DisplayStyle.Flex;
            _nub.style.display = DisplayStyle.Flex;
        }

        private void HideJoystick()
        {
            if (_ring == null || _nub == null)
            {
                return;
            }

            _ring.style.display = DisplayStyle.None;
            _nub.style.display = DisplayStyle.None;
        }

        // The nub's screen position: the touch, clamped to the ring radius around the anchor.
        private Vector2 ClampedNub(JoystickState joystick)
        {
            float radiusPx = _tuning.JoystickRadiusPx;
            Vector2 offset = joystick.Current - joystick.Anchor;
            if (offset.sqrMagnitude > radiusPx * radiusPx)
            {
                offset = offset.normalized * radiusPx;
            }

            return joystick.Anchor + offset;
        }

        // Screen pixels (bottom-left) to the local space of the movement HUD root (top-left, after its margins).
        private Vector2 ScreenToLocal(IPanel panel, Vector2 screen)
        {
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return _hudRoot.WorldToLocal(panelPos);
        }

        private static float ScreenLengthToPanel(IPanel panel, float lengthPx)
        {
            Vector2 a = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            Vector2 b = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(lengthPx, 0f));
            return b.x - a.x;
        }

        private static void Center(VisualElement element, Vector2 center, float halfSize)
        {
            element.style.left = center.x - halfSize;
            element.style.top = center.y - halfSize;
        }

        private sealed class NeverProhibitedSource : IClientProhibitedSource
        {
            public bool IsProhibited => false;
        }
    }
}
