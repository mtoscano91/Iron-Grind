using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using IronGrind.CharacterPersistence;
using IronGrind.Networking;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The zone server composition root: startup S0-S10 (Networking Core Story 043), teardown 1-8 (Story 042)
    /// (ADR-009 Amendment 1 Decisions 2, 3 and 5). A trigger only calls <see cref="RequestTeardown"/>; the host's
    /// Update starts <see cref="RunTeardown"/> once when <see cref="TryBeginTeardown"/> returns true. The real
    /// object graph (Story 044) is attached through the <c>Attach*</c> methods and <c>IZoneBackendSteps</c>.
    /// </summary>
    /// <remarks>
    /// Main thread only (the tick thread). Teardown order: 1 tick unsubscribe, 2a listener stop, 3 navigation,
    /// 4 CR-ZI-12, 4b outbound flush, 5 queue drain, 6 transport stop + NGO shutdown, 7 Dispose pass,
    /// 8 quit. Each step has its own try/catch; a failure logs the step and exception type name only and the
    /// teardown goes on; the exit code is never changed by a failure. A destroyed NGO manager
    /// (<see cref="IZoneNetwork.IsAlive"/> false) skips the tick unsubscribe and the NGO shutdown. The teardown
    /// starts from the first step with something to undo: navigation teardown (3) runs only once S6 called
    /// <c>Initialize</c> (<see cref="MarkNavigationInitialized"/>), CR-ZI-12 (4) only once the zone state owner
    /// was told of T-2 or T-3 (<see cref="MarkZoneStateEntered"/>), and the flush (4b) and the drain (5) only
    /// after startup S9 completed the tick subscription (<see cref="MarkTickStarted"/>).
    /// <para>
    /// Internally the non-yielding teardown steps are numbered 1-8 (<c>_step</c>, <see cref="RunStepsForTest"/>):
    /// internal 1 = ADR 1, 2 = 2a, 3 = 3, 4 = 4, 5 = 4b, 6 = 5, 7 = 6 (without the wait), 8 = 7. ADR step 8
    /// (quit) and the step-6 wait live only in <see cref="RunTeardown"/>.
    /// </para>
    /// <para>
    /// Startup: <see cref="Start"/> runs S0-S10, each as a non-yielding step in its own try/catch; it yields
    /// only during the S6 scene wait. A step that throws or returns false is S-fail: one error log line
    /// <c>[ZoneServer] startup failed at S&lt;n&gt;: &lt;ExceptionType&gt;</c> (a step that returned false
    /// reports the token <c>StepReturnedFalse</c>), <c>RequestTeardown(StartupFailed)</c>, no later step, no
    /// retry. An S0 failure first logs <c>[ZoneServer] S0: &lt;reason&gt;</c>, which names the flag or config
    /// field and never a value or the path. <see cref="Start"/> never runs the teardown itself; the host's
    /// Update does, as for every trigger. A required seam that is null makes its step throw
    /// <see cref="NullReferenceException"/>, i.e. S-fail. S1 subscribes <see cref="OnWantsToQuit"/>;
    /// <see cref="TeardownNow"/> unsubscribes it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Startup-capable composition (internal constructor; the host lives in this assembly):
    /// var composition = new ZoneServerComposition(app, network, navigation, closeSteps, scene, boundsValidator,
    ///     staticDataLoader, zoneStateOwner, gateway, configSource, backend, null);
    /// // host Start (S1 subscribes OnWantsToQuit itself; do not subscribe it again):
    /// yield return composition.Start(Environment.GetCommandLineArgs());
    /// // host Update:
    /// if (composition.TryBeginTeardown()) { StartCoroutine(composition.RunTeardown()); }
    /// // host OnDestroy:
    /// composition.TeardownNow();
    /// </code>
    /// </example>
    public sealed class ZoneServerComposition : IDisposable
    {
        private const string LOG_PREFIX = "[ZoneServer]";
        private const int LAST_INTERNAL_STEP = 8;
        private const int TRANSPORT_AND_NGO_STEP = 7;
        private const int LAST_STARTUP_STEP = 10;
        private const int SCENE_STEP = 6;
        private const string STEP_RETURNED_FALSE = "StepReturnedFalse";

        // Internal step numbers 1..8 and the names the ADR gives them (used in logs).
        private static readonly string[] STEP_NAMES = { "", "1", "2a", "3", "4", "4b", "5", "6", "7" };

        // The watchdog duration (ADR-011 Decision 2): 200 ticks / 20 Hz = 10 s.
        private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromSeconds(
            (double)TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS / ServerTickLoop.TICK_RATE_HZ);

        private readonly IZoneApplication _application;
        private readonly IZoneNetwork _network;
        private readonly IZoneNavigation _navigation;
        private readonly IZoneCloseSteps _closeSteps;
        private readonly IZoneScene _scene;
        private readonly IZoneBoundsValidator _boundsValidator;
        private readonly IZoneStaticDataLoader _staticDataLoader;
        private readonly IZoneStateOwner _zoneStateOwner;
        private readonly IGatewayRegistration _gateway;
        private readonly IZoneConfigSource _configSource;
        private readonly IZoneBackendSteps _backend;
        private readonly Func<double> _clock;
        private readonly Stopwatch _stopwatch;
        private readonly Func<bool> _quitHandler;
        private readonly Action _networkTickHandler;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        private int _ngoShutdownWaitFrames;
        private ITickCompletionQueue _queue;
        private IZoneOutboundStep _outbound;
        private IZoneRelayListener _listener;
        private IZoneInboundTransport _transport;
        private IZoneOutboundSender _outboundSender;
        private ServerTickLoop _tickLoop;
        private Func<uint> _currentTick;
        private Action _tickHandler;

        private bool _teardownRequested;
        private bool _beginConsumed;
        private ZoneExitCode _exitCode = ZoneExitCode.Clean;
        private int _step;
        private bool _runStarted;
        private bool _syncDone;
        private bool _quitAllowed;
        private bool _tickStarted;
        private bool _navigationInitialized;
        private bool _zoneStateEntered;
        private bool _quitSubscribed;

        private bool _startCalled;
        private bool _sceneFaulted;
        private ZoneStartupArgs _args;
        private ZoneServerConfig _config;
        private ZoneID _zoneId;
        private double _previousTickSeconds;

        /// <summary>Creates the composition around its Unity-facing seams (teardown-only seams).</summary>
        /// <param name="application">Application seam. May be null in tests of other steps.</param>
        /// <param name="network">NGO seam. May be null (a missing manager skips its steps).</param>
        /// <param name="navigation">Navigation seam (a stand-in until its epic ships). May be null.</param>
        /// <param name="closeSteps">CR-ZI-12 seam (a stand-in until its epic ships). May be null.</param>
        /// <param name="ngoShutdownWaitFrames">Server config NGO_SHUTDOWN_WAIT_FRAMES; must be greater than 0.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ngoShutdownWaitFrames"/> is not positive.</exception>
        public ZoneServerComposition(
            IZoneApplication application,
            IZoneNetwork network,
            IZoneNavigation navigation,
            IZoneCloseSteps closeSteps,
            int ngoShutdownWaitFrames)
            : this(application, network, navigation, closeSteps, null, null, null, null, null, null, null, null)
        {
            if (ngoShutdownWaitFrames <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ngoShutdownWaitFrames), "Must be greater than 0.");
            }

            _ngoShutdownWaitFrames = ngoShutdownWaitFrames;
        }

        /// <summary>
        /// Creates the composition with every startup seam. The NGO shutdown wait starts at the config default
        /// and is replaced from the server config at S0. A seam that is null makes the step needing it S-fail.
        /// </summary>
        /// <param name="application">Application seam.</param>
        /// <param name="network">NGO seam.</param>
        /// <param name="navigation">Navigation seam.</param>
        /// <param name="closeSteps">CR-ZI-12 seam.</param>
        /// <param name="scene">Zone scene seam (S6).</param>
        /// <param name="boundsValidator">Bounds validator seam (S6).</param>
        /// <param name="staticDataLoader">Static data seam (S2).</param>
        /// <param name="zoneStateOwner">Zone state seam (S6 T-3, S9 T-2).</param>
        /// <param name="gateway">Gateway registration seam (S10).</param>
        /// <param name="configSource">Config file reader (S0).</param>
        /// <param name="backend">Backend steps S3, S4, S5, S7.</param>
        /// <param name="clock">Seconds source for tick durations; null = a <see cref="Stopwatch"/> started here.</param>
        internal ZoneServerComposition(
            IZoneApplication application,
            IZoneNetwork network,
            IZoneNavigation navigation,
            IZoneCloseSteps closeSteps,
            IZoneScene scene,
            IZoneBoundsValidator boundsValidator,
            IZoneStaticDataLoader staticDataLoader,
            IZoneStateOwner zoneStateOwner,
            IGatewayRegistration gateway,
            IZoneConfigSource configSource,
            IZoneBackendSteps backend,
            Func<double> clock)
        {
            _application = application;
            _network = network;
            _navigation = navigation;
            _closeSteps = closeSteps;
            _scene = scene;
            _boundsValidator = boundsValidator;
            _staticDataLoader = staticDataLoader;
            _zoneStateOwner = zoneStateOwner;
            _gateway = gateway;
            _configSource = configSource;
            _backend = backend;
            _ngoShutdownWaitFrames = ZoneServerConfig.DEFAULT_NGO_SHUTDOWN_WAIT_FRAMES;
            _quitHandler = OnWantsToQuit;
            _networkTickHandler = OnNetworkTick;
            if (clock != null)
            {
                _clock = clock;
            }
            else
            {
                _stopwatch = Stopwatch.StartNew();
                _clock = ReadStopwatchSeconds;
            }
        }

        /// <summary>Duration in seconds passed to the last <c>AdvanceTick</c> by the tick handler (0 before any).</summary>
        internal float LastTickDurationSeconds { get; private set; }

        /// <summary>The config parsed at S0, or null before S0 succeeded.</summary>
        internal ZoneServerConfig Config => _config;

        /// <summary>
        /// Runs startup S0-S10 as a coroutine; yields only while the S6 scene load is in progress. Single use:
        /// a second call yields nothing. On failure it requests <c>StartupFailed</c> and stops; on a quit
        /// request it stops at the next step boundary (S6: also abandons the scene wait). It never tears down.
        /// </summary>
        /// <param name="commandLine">The process arguments (<c>-zoneTemplate</c>, <c>-zoneId</c>, <c>-port</c>,
        /// <c>-serverConfig</c>); parse failures go through S-fail at S0.</param>
        /// <returns>The coroutine for the host to run.</returns>
        public IEnumerator Start(string[] commandLine)
        {
            if (_startCalled)
            {
                yield break;
            }

            _startCalled = true;

            for (int n = 0; n <= LAST_STARTUP_STEP; n++)
            {
                if (_teardownRequested)
                {
                    yield break;
                }

                if (!RunStartupStep(n, 1, commandLine))
                {
                    RequestTeardown(ZoneExitCode.StartupFailed);
                    yield break;
                }

                if (n != SCENE_STEP)
                {
                    continue;
                }

                // The wait has no try around it; reading IsDone is guarded inside PollSceneDone.
                while (!_teardownRequested && !PollSceneDone())
                {
                    yield return null;
                }

                if (_sceneFaulted)
                {
                    RequestTeardown(ZoneExitCode.StartupFailed);
                    yield break;
                }

                if (_teardownRequested)
                {
                    yield break;
                }

                if (!RunStartupStep(SCENE_STEP, 2, commandLine))
                {
                    RequestTeardown(ZoneExitCode.StartupFailed);
                    yield break;
                }
            }
        }

        /// <summary>
        /// Records the first exit code and sets the request flag; ignores later codes. Tears nothing down.
        /// Safe to call any number of times from any state.
        /// </summary>
        /// <param name="code">The exit code to quit with.</param>
        public void RequestTeardown(ZoneExitCode code)
        {
            if (_teardownRequested)
            {
                return;
            }

            _exitCode = code;
            _teardownRequested = true;
        }

        /// <summary>
        /// Host contract: true exactly once, on the first call after <see cref="RequestTeardown"/>; false
        /// before any request and on every later call.
        /// </summary>
        /// <returns>True when the host should start <see cref="RunTeardown"/> now.</returns>
        public bool TryBeginTeardown()
        {
            if (!_teardownRequested || _beginConsumed)
            {
                return false;
            }

            _beginConsumed = true;
            return true;
        }

        /// <summary>
        /// Teardown steps 1-8 as a coroutine; only the NGO shutdown wait of step 6 yields. A second call, or a
        /// call after <see cref="TeardownNow"/>/<see cref="Dispose"/>, yields nothing and does nothing.
        /// </summary>
        /// <returns>The coroutine for the host to run.</returns>
        public IEnumerator RunTeardown()
        {
            if (_runStarted || _syncDone)
            {
                yield break;
            }

            _runStarted = true;

            for (int n = 1; n <= 7; n++)
            {
                RunStep(n);
            }

            // The wait sits outside any try/catch (C# forbids yield inside a try with a catch).
            int frames = 0;
            while (!_syncDone && frames < _ngoShutdownWaitFrames && IsShutdownPending())
            {
                frames++;
                yield return null;
            }

            if (_syncDone)
            {
                yield break;
            }

            if (frames >= _ngoShutdownWaitFrames && IsShutdownPending())
            {
                Debug.LogWarning(LOG_PREFIX + " NGO shutdown still in progress after "
                    + _ngoShutdownWaitFrames + " frames; going on.");
            }

            RunStep(LAST_INTERNAL_STEP);

            _quitAllowed = true;
            try
            {
                _application?.Quit((int)_exitCode);
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + " teardown step 8 failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// Steps 1-7 synchronously, for the host's OnDestroy: step 6 issues the NGO shutdown without waiting
        /// and the process is never quit. Runs only the steps not yet done, then unsubscribes the
        /// <c>wantsToQuit</c> handler S1 added (so the Editor keeps no stale handler when domain reload is off;
        /// <see cref="RunTeardown"/> never unsubscribes, because <c>Application.Quit</c> raises the event again).
        /// Does nothing when finished.
        /// </summary>
        public void TeardownNow()
        {
            if (_syncDone)
            {
                return;
            }

            for (int n = 1; n <= LAST_INTERNAL_STEP; n++)
            {
                RunStep(n);
            }

            UnsubscribeQuitHandler();
            _syncDone = true;
        }

        /// <summary>Same as <see cref="TeardownNow"/>: steps 1-7, no quit, idempotent.</summary>
        public void Dispose()
        {
            TeardownNow();
        }

        /// <summary>
        /// Application.wantsToQuit handler. True in the Editor and once teardown step 8 set the quit flag;
        /// otherwise requests a Clean teardown and returns false. Does no teardown work itself.
        /// </summary>
        /// <returns>True when the application may quit now.</returns>
        public bool OnWantsToQuit()
        {
            if (_application != null && _application.IsEditor)
            {
                return true;
            }

            if (_quitAllowed)
            {
                return true;
            }

            RequestTeardown(ZoneExitCode.Clean);
            return false;
        }

        /// <summary>
        /// Appends an object to the Dispose pass (teardown step 7), which disposes in reverse order. Register
        /// the data source first so it is disposed last. Story 044 calls this from startup S4/S5.
        /// </summary>
        /// <param name="disposable">The object to dispose; null is ignored.</param>
        internal void RegisterDisposable(IDisposable disposable)
        {
            if (disposable != null)
            {
                _disposables.Add(disposable);
            }
        }

        /// <summary>
        /// Sets the owned objects the teardown touches. Story 044 calls this from startup S5 once it has
        /// built the objects; tests call it with fakes. Any argument may be null (its step is skipped).
        /// </summary>
        /// <param name="queue">Completion queue drained at step 5.</param>
        /// <param name="outbound">Outbound step flushed once at step 4b.</param>
        /// <param name="listener">Relay listener stopped at step 2a.</param>
        /// <param name="transport">Inbound transport started at S8 and stopped at step 6.</param>
        /// <param name="currentTick">Source of the last tick number passed to the flush.</param>
        /// <param name="tickHandler">The delegate subscribed to the network tick; step 1 unsubscribes it. S9
        /// replaces it with the composition's own handler once the subscription succeeded.</param>
        internal void AttachOwnedObjects(
            ITickCompletionQueue queue,
            IZoneOutboundStep outbound,
            IZoneRelayListener listener,
            IZoneInboundTransport transport,
            Func<uint> currentTick,
            Action tickHandler)
        {
            _queue = queue;
            _outbound = outbound;
            _listener = listener;
            _transport = transport;
            _currentTick = currentTick;
            _tickHandler = tickHandler;
        }

        /// <summary>Attaches the tick loop built in S5; S9 fails without it.</summary>
        /// <param name="tickLoop">The server tick loop the network tick event advances.</param>
        internal void AttachTickLoop(ServerTickLoop tickLoop)
        {
            _tickLoop = tickLoop;
        }

        /// <summary>Attaches the outbound sender started in S8; null (the default) skips that call.</summary>
        /// <param name="sender">The sender to start.</param>
        internal void AttachOutboundSender(IZoneOutboundSender sender)
        {
            _outboundSender = sender;
        }

        /// <summary>
        /// Marks that S9 completed the tick subscription; from now on teardown runs the outbound flush (4b)
        /// and the queue drain (5). Called once by S9; tests call it after attaching fakes.
        /// </summary>
        internal void MarkTickStarted()
        {
            _tickStarted = true;
        }

        /// <summary>
        /// Marks that S6 is calling <see cref="IZoneNavigation.Initialize"/>; from now on teardown step 3 runs
        /// (also when <c>Initialize</c> threw, so a partly initialised navigation and its NavMesh data are
        /// released). Set by S6 just before the call; tests call it after attaching fakes.
        /// </summary>
        internal void MarkNavigationInitialized()
        {
            _navigationInitialized = true;
        }

        /// <summary>
        /// Marks that the zone state owner was told of T-2 (S9) or T-3 (S6 invalid bounds); from now on
        /// teardown step 4 (CR-ZI-12) runs. Set just before that call; tests call it after attaching fakes.
        /// </summary>
        internal void MarkZoneStateEntered()
        {
            _zoneStateEntered = true;
        }

        /// <summary>
        /// Test hook: runs the non-yielding steps 1..<paramref name="lastStep"/> (internal numbering: 1 tick
        /// unsubscribe, 2 listener, 3 navigation, 4 close steps, 5 flush, 6 drain, 7 transport + NGO shutdown,
        /// 8 Dispose pass) to simulate a teardown stopped part-way.
        /// </summary>
        /// <param name="lastStep">The last internal step to run, 1 to 8.</param>
        internal void RunStepsForTest(int lastStep)
        {
            for (int n = 1; n <= lastStep && n <= LAST_INTERNAL_STEP; n++)
            {
                RunStep(n);
            }
        }

        // ------------------------------------------------------------------ startup

        private double ReadStopwatchSeconds()
        {
            return _stopwatch.Elapsed.TotalSeconds;
        }

        // The named tick handler (never a lambda): one AdvanceTick per NGO tick event, with the measured time.
        private void OnNetworkTick()
        {
            double now = _clock();
            float seconds = (float)(now - _previousTickSeconds);
            _previousTickSeconds = now;
            LastTickDurationSeconds = seconds;
            _tickLoop.AdvanceTick(seconds);
        }

        // Reads the scene seam's IsDone; an exception becomes S-fail at S6 and ends the wait.
        private bool PollSceneDone()
        {
            try
            {
                return _scene.IsDone;
            }
            catch (Exception ex)
            {
                LogStartupFailure(SCENE_STEP, ex.GetType().Name);
                _sceneFaulted = true;
                return true;
            }
        }

        // Part 1 is the step; S6 has a second part (after the scene wait). Each call is one try/catch.
        private bool RunStartupStep(int n, int part, string[] commandLine)
        {
            try
            {
                bool ok = StartupStepBody(n, part, commandLine);
                if (!ok)
                {
                    LogStartupFailure(n, STEP_RETURNED_FALSE);
                }

                return ok;
            }
            catch (Exception ex)
            {
                LogStartupFailure(n, ex.GetType().Name);
                return false;
            }
        }

        private static void LogStartupFailure(int n, string typeName)
        {
            Debug.LogError(LOG_PREFIX + " startup failed at S" + n + ": " + typeName);
        }

        // The S0 reason: the parser and the config loader name the flag or field only, never a value or the path.
        private static void LogStartupDetail(string reason)
        {
            Debug.LogError(LOG_PREFIX + " S0: " + reason);
        }

        private bool StartupStepBody(int n, int part, string[] commandLine)
        {
            switch (n)
            {
                case 0:
                    return StartupReadArgsAndConfig(commandLine);
                case 1:
                    _application.SubscribeWantsToQuit(_quitHandler);
                    _quitSubscribed = true;
                    _application.KeepAcrossSceneLoads();
                    _network.PrepareObject();
                    return true;
                case 2:
                    return _staticDataLoader.LoadAndValidate();
                case 3:
                    return _backend.InitializeRandom();
                case 4:
                    return _backend.OpenDatabase();
                case 5:
                    return _backend.ConstructObjects();
                case SCENE_STEP:
                    return part == 1 ? StartupBeginSceneLoad() : StartupFinishScene();
                case 7:
                    return _backend.StartListener();
                case 8:
                    return StartupStartNetwork();
                case 9:
                    return StartupSubscribeTick();
                default:
                    _gateway.RegisterZone(_zoneId, _args.Port);
                    return true;
            }
        }

        private bool StartupReadArgsAndConfig(string[] commandLine)
        {
            if (!ZoneStartupArgs.Parse(commandLine, out ZoneStartupArgs args, out string argsError))
            {
                LogStartupDetail(argsError);
                return false;
            }

            string json = _configSource.ReadAllText(args.ServerConfigPath);
            if (!ZoneServerConfig.TryLoad(json, out ZoneServerConfig config, out string configError))
            {
                LogStartupDetail(configError);
                return false;
            }

            _args = args;
            _config = config;
            _zoneId = new ZoneID(args.ZoneId);
            _ngoShutdownWaitFrames = config.NgoShutdownWaitFrames;
            return true;
        }

        private bool StartupBeginSceneLoad()
        {
            _scene.BeginLoad(_args.ZoneTemplate);
            return true;
        }

        private bool StartupFinishScene()
        {
            if (!_scene.Succeeded)
            {
                return false;
            }

            NavMeshDataInstance instance = _scene.AddNavMeshData();
            MarkNavigationInitialized();
            _navigation.Initialize(_zoneId, instance);
            if (!_boundsValidator.Validate(_zoneId))
            {
                MarkZoneStateEntered();
                _zoneStateOwner.MarkClosedInvalidBounds(_zoneId);
                return false;
            }

            return true;
        }

        private bool StartupStartNetwork()
        {
            _network.Configure((uint)ServerTickLoop.TICK_RATE_HZ, ServerTickLoop.TICK_RATE_HZ, 0);
            if (!_network.StartServer())
            {
                return false;
            }

            _transport?.Start();
            _outboundSender?.Start();
            return true;
        }

        private bool StartupSubscribeTick()
        {
            if (_tickLoop == null)
            {
                throw new InvalidOperationException("No tick loop attached.");
            }

            MarkZoneStateEntered();
            _zoneStateOwner.MarkActive(_zoneId);
            _previousTickSeconds = _clock();
            _network.SubscribeTick(_networkTickHandler);
            _tickHandler = _networkTickHandler;
            MarkTickStarted();
            return true;
        }

        // ------------------------------------------------------------------ teardown

        private bool IsShutdownPending()
        {
            try
            {
                return IsNetworkAlive() && _network.ShutdownInProgress;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsNetworkAlive()
        {
            return _network != null && _network.IsAlive;
        }

        private void RunStep(int n)
        {
            if (_step >= n)
            {
                return;
            }

            // Marked before the body runs, so a step that re-enters TeardownNow() is not run a second time.
            _step = n;
            try
            {
                RunStepBody(n);
            }
            catch (Exception ex)
            {
                LogStepFailure(n, ex);
            }
        }

        private void RunStepBody(int n)
        {
            switch (n)
            {
                case 1:
                    UnsubscribeTick();
                    break;
                case 2:
                    _listener?.Stop();
                    break;
                case 3:
                    if (_navigationInitialized)
                    {
                        _navigation?.Teardown();
                    }

                    break;
                case 4:
                    if (_zoneStateEntered)
                    {
                        _closeSteps?.RunCloseSteps();
                    }

                    break;
                case 5:
                    if (_tickStarted)
                    {
                        FlushOutbound();
                    }

                    break;
                case 6:
                    if (_tickStarted)
                    {
                        _queue?.DrainOnShutdown(DRAIN_TIMEOUT);
                    }

                    break;
                case TRANSPORT_AND_NGO_STEP:
                    StopTransportAndShutDownNetwork();
                    break;
                case LAST_INTERNAL_STEP:
                    DisposePass();
                    break;
            }
        }

        private static void LogStepFailure(int n, Exception ex)
        {
            Debug.LogError(LOG_PREFIX + " teardown step " + STEP_NAMES[n] + " failed: " + ex.GetType().Name);
        }

        private void UnsubscribeQuitHandler()
        {
            if (!_quitSubscribed)
            {
                return;
            }

            _quitSubscribed = false;
            try
            {
                _application?.UnsubscribeWantsToQuit(_quitHandler);
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + " wantsToQuit unsubscribe failed: " + ex.GetType().Name);
            }
        }

        private void UnsubscribeTick()
        {
            if (_tickHandler != null && IsNetworkAlive())
            {
                _network.UnsubscribeTick(_tickHandler);
            }
        }

        private void FlushOutbound()
        {
            if (_outbound != null)
            {
                uint tick = 0u;
                if (_currentTick != null)
                {
                    tick = _currentTick();
                }
                else if (_tickLoop != null)
                {
                    tick = _tickLoop.ServerTickNumber;
                }

                _outbound.Flush(tick);
            }
        }

        // The transport stop has its own try so a failure there never skips the NGO shutdown.
        private void StopTransportAndShutDownNetwork()
        {
            try
            {
                _transport?.Stop();
            }
            catch (Exception ex)
            {
                LogStepFailure(TRANSPORT_AND_NGO_STEP, ex);
            }

            if (IsNetworkAlive())
            {
                _network.Shutdown();
            }
        }

        private void DisposePass()
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i].Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError(LOG_PREFIX + " teardown step 7 Dispose failed: " + ex.GetType().Name);
                }
            }

            _disposables.Clear();
        }
    }
}
