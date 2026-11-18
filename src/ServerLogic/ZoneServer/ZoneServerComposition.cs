using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.LevelingSystem;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.Randomness;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using Npgsql;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;
// The simple name CharacterStats resolves to the IronGrind.CharacterStats namespace, so the class takes an alias.
using ZoneStats = IronGrind.CharacterStats.CharacterStats;
using ZoneCurrencySystem = IronGrind.Currency.CurrencySystem;
using EnhancementBonusProvider = IronGrind.EnhancementSystem.EnhancementBonusProvider;
using EnhancementConfig = IronGrind.EnhancementSystem.EnhancementConfig;
using EquipmentService = IronGrind.EquipmentSystem.EquipmentService;
using IItemDatabase = IronGrind.ItemDatabase.IItemDatabase;
using InventoryService = IronGrind.InventorySystem.InventoryService;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// A relay listener that can be started: <see cref="IZoneRelayListener"/> plus <c>Start()</c>. Internal because
    /// only the composition root starts a listener (ADR-015); <c>NpgsqlRelayListener</c> implements it and EditMode
    /// tests supply fakes through <c>ListenerFactoryOverride</c>.
    /// </summary>
    internal interface IStartableZoneRelayListener : IZoneRelayListener
    {
        /// <summary>Starts the listener; may throw (S7 then fails and teardown step 2a still stops it).</summary>
        void Start();
    }

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
    /// <c>Initialize</c> (<see cref="MarkNavigationInitialized"/>; it halts nothing itself: the tick is already
    /// unsubscribed at step 1, then <c>Teardown()</c> disables the agents and removes the NavMesh data, then the
    /// composition destroys the Unity pool roots of the factory it built, Navigation Story 010), CR-ZI-12 (4) only once the zone state owner
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
        private const string PASSWORD_VARIABLE = "IRONGRIND_PG_PASSWORD";
        private const string APPLICATION_NAME_PREFIX = "irongrind-zone-";

        /// <summary>Logged once at S5 when neither the override nor the S2 loader gives an item database (Character Persistence Story 020).</summary>
        internal const string NO_ITEM_DATABASE_ERROR = "[ZoneServer] S5: no item database from S2";

        /// <summary>Logged once at S5 when neither the override nor the S2 loader gives a mob registry (Enemy AI Story 013).</summary>
        internal const string NO_MOB_REGISTRY_ERROR = "[ZoneServer] S5: no mob registry from S2";

        /// <summary>
        /// Start of the info line logged once after the T-2 sweep (Mob Spawning Story 007 AC-6):
        /// <c>[MobSpawning] zone &lt;id&gt;: &lt;live&gt; of &lt;total&gt; points Live, &lt;deferred&gt; Deferred</c>.
        /// </summary>
        internal const string MOB_SPAWNING_POPULATED_LOG_PREFIX = "[MobSpawning] zone ";

        /// <summary>
        /// Seconds without a completed network tick, with a quit pending (or the zone Draining), after which
        /// <see cref="PollQuitStall"/> forces a Clean teardown. A quit in Active or Draining is applied by the next
        /// tick, so stopped ticks would otherwise leave the zone open and <c>wantsToQuit</c> answering false forever.
        /// </summary>
        public const float QUIT_TICK_STALL_SECONDS = 5f;

        // Internal step numbers 1..8 and the names the ADR gives them (used in logs).
        private static readonly string[] STEP_NAMES = { "", "1", "2a", "3", "4", "4b", "5", "6", "7" };

        // The watchdog duration (ADR-011 Decision 2): 200 ticks / 20 Hz = 10 s.
        private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromSeconds(
            (double)TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS / ServerTickLoop.TICK_RATE_HZ);

        private readonly IZoneApplication _application;
        private readonly IZoneNetwork _network;
        // Not readonly: with the real backend and a null seam, S5 builds the real ZoneNavigationService (Navigation Story 010).
        private IZoneNavigation _navigation;
        private ZoneNavigationService _navigationService;
        private UnityNavAgentFactory _unityNavAgentFactory;
        private NavigationConfig _ownedNavigationConfig;
        private readonly IZoneCloseSteps _closeSteps;
        private readonly IZoneScene _scene;
        private readonly IZoneBoundsValidator _boundsValidator;
        private readonly IZoneStaticDataLoader _staticDataLoader;
        private readonly IZoneStateOwner _zoneStateOwner;
        private readonly IGatewayRegistration _gateway;
        private readonly IZoneConfigSource _configSource;
        private IZoneBackendSteps _backend;
        private readonly Func<double> _clock;
        private readonly Func<uint> _currentTickReader;
        private readonly List<IZoneSimulationStep> _simulationSteps = new List<IZoneSimulationStep>();
        private readonly Stopwatch _stopwatch;
        private readonly Func<bool> _quitHandler;
        private readonly Action _networkTickHandler;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        private int _ngoShutdownWaitFrames;
        private ITickCompletionQueue _queue;
        private IZoneOutboundStep _outbound;
        private IZoneRelayListener _listener;
        private IZoneInboundTransport _transport;
        private ServerWideBroadcaster _broadcaster;
        private IZoneOutboundSender _outboundSender;
        private ServerTickLoop _tickLoop;
        private Func<uint> _currentTick;
        private Action _tickHandler;
        private TickTimestampLog _tickTimestampLog;

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
        private bool _tickFailureReported;
        private ZoneInstancingWiring _wiring;
        private ZoneDefinitionSlot _definitionSlot;
        private IGatewayLinkChannel _linkChannel;
        private IGatewayRegistration _linkRegistration;
        private GatewayLinkOutbox _linkOutbox;
        private int _gatewayLinkPort;
        private uint _lastLoadingTick;
        private double _lastTickSeconds;
        private double _quitStallStartSeconds = double.NaN;

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
            _currentTickReader = ReadCurrentTick;
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

        /// <summary>
        /// Creates the composition with the REAL backend (Networking Core Story 044): S3 random, S4 data source and
        /// warm-up, S5 object graph, S7 relay listener. This is the entry point Story 045's host uses. The seams are
        /// those of the internal constructor minus <c>backend</c>, plus the environment the database password is read
        /// from.
        /// </summary>
        /// <param name="application">Application seam.</param>
        /// <param name="network">NGO seam (also creates the send seam and the inbound transport).</param>
        /// <param name="navigation">
        /// Navigation seam. Null (the host passes null): S5 builds the real <see cref="ZoneNavigationService"/>, its
        /// <see cref="MobEventBus"/> and its phase step (Navigation Story 010). A non-null seam (tests) keeps the old
        /// behaviour: no bus, no navigation step.
        /// </param>
        /// <param name="closeSteps">CR-ZI-12 seam.</param>
        /// <param name="scene">Zone scene seam (S6).</param>
        /// <param name="boundsValidator">Bounds validator seam (S6).</param>
        /// <param name="staticDataLoader">Static data seam (S2).</param>
        /// <param name="zoneStateOwner">Zone state seam.</param>
        /// <param name="gateway">Gateway registration seam (S10).</param>
        /// <param name="configSource">Config file reader (S0).</param>
        /// <param name="environment">Environment variable seam (<c>IRONGRIND_PG_PASSWORD</c>).</param>
        /// <param name="clock">Seconds source for tick durations; null = a stopwatch started here.</param>
        /// <param name="definitionSlot">
        /// The host's zone definition holder; when set, S5 builds a <see cref="ZoneDefinitionSlotRespawnPointProvider"/>
        /// over it for Movement (the definition is filled at S6). Null: Movement keeps its null-provider branch.
        /// </param>
        /// <returns>The composition, ready for <see cref="Start"/>.</returns>
        /// <example>
        /// <code>
        /// var composition = ZoneServerComposition.CreateWithRealBackend(app, network, navigation, closeSteps, scene,
        ///     bounds, staticData, zoneState, gateway, configSource, new ProcessZoneEnvironment(), null);
        /// yield return composition.Start(Environment.GetCommandLineArgs());
        /// </code>
        /// </example>
        internal static ZoneServerComposition CreateWithRealBackend(
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
            IZoneEnvironment environment,
            Func<double> clock,
            ZoneDefinitionSlot definitionSlot = null)
        {
            var composition = new ZoneServerComposition(
                application, network, navigation, closeSteps, scene, boundsValidator, staticDataLoader,
                zoneStateOwner, gateway, configSource, null, clock);
            composition._definitionSlot = definitionSlot;
            composition._backend = new RealBackend(composition, environment);
            return composition;
        }

        /// <summary>
        /// Test seam: replaces the real warm-up (<c>SELECT 1</c>) at S4 when set. Null in production. The task is run
        /// with <c>Task.Run</c> and waited for <c>DbWarmupTimeoutMs</c>; the token is cancelled on a timeout.
        /// </summary>
        internal Func<CancellationToken, Task> WarmUpOverride;

        /// <summary>
        /// Test seam (Navigation Story 010): replaces the <see cref="UnityNavAgentFactory"/> the composition builds at S5
        /// when no navigation seam was passed. When set, no concrete factory is kept, so teardown step 3 has no pool
        /// roots to destroy. Null in production. Set before the run starts.
        /// </summary>
        internal Func<INavAgentFactory> NavAgentFactoryOverride;

        /// <summary>
        /// Test seam (Navigation Story 010): replaces the in-code default <see cref="NavigationConfig"/> the composition
        /// creates at S5 (an override is never destroyed by the composition). Null in production. Set before the run starts.
        /// </summary>
        internal NavigationConfig NavigationConfigOverride;

        /// <summary>
        /// Test seam (Navigation Story 010, an approved small addition to the two hooks above): replaces
        /// <see cref="UnityNavMeshInstanceOps"/>. EditMode fake scenes return a default (invalid) NavMeshDataInstance, so
        /// the real ops would fail S6 there. Null in production. Set before the run starts.
        /// </summary>
        internal Func<INavMeshInstanceOps> NavMeshInstanceOpsOverride;

        /// <summary>
        /// The zone's one <see cref="MobEventBus"/> (Navigation Story 010), created at S5 (Enemy AI Story 013: always, also with a navigation seam; Navigation Story 010 built it only
        /// without a seam); null before S5. Mob Spawning raises on it. The runtime store and Enemy AI subscribe at S5; the navigation
        /// subscribes at S6 and unsubscribes in its own teardown; the Dispose pass drops this reference.
        /// </summary>
        internal MobEventBus MobEvents { get; private set; }

        /// <summary>
        /// Test seam (Enemy AI Story 013): the mob definition registry. Null means the S2 loader's registry
        /// (<see cref="IZoneMobData"/>); when the loader has none, S5 fails with <see cref="NO_MOB_REGISTRY_ERROR"/>. Set before the run starts.
        /// </summary>
        internal IMobDefinitionRegistry MobRegistryOverride;

        /// <summary>The mob registry the store, navigation and Enemy AI actually use; null before S5.</summary>
        internal IMobDefinitionRegistry MobRegistryInUse { get; private set; }

        /// <summary>The zone's mob runtime store, built at S5 (Enemy AI Story 013), also with a navigation seam; null before S5 and after the Dispose pass.</summary>
        internal MobRuntimeStore MobStore { get; private set; }

        /// <summary>The zone's Enemy AI system; null before S5, after the Dispose pass, and when a navigation seam was passed (no INavigationProvider).</summary>
        internal EnemyAISystem EnemyAI { get; private set; }

        /// <summary>
        /// The zone's spawner (Mob Spawning Story 006), built at S6 by <see cref="MobSpawningZoneLoader"/>; null before S6,
        /// after the Dispose pass, when S6 failed, and when the composition has no definition slot or no spawn point
        /// validator (a navigation seam without <see cref="SpawnPointValidatorOverride"/>).
        /// </summary>
        internal MobSpawningService MobSpawner { get; private set; }

        /// <summary>
        /// The teardown seam passed to the Zone Instancing wiring at S5 as <c>MobSpawnTimers</c>; it forwards to
        /// <see cref="MobSpawner"/> once S6 built it. Null before S5.
        /// </summary>
        internal ForwardingZoneMobSpawnTimers MobSpawnTimers { get; private set; }

        /// <summary>The spawner's phase 3 step, built at S5 (listed by <c>AddPhaseThreeSteps</c>); null before S5.</summary>
        internal MobSpawningPhaseStep MobSpawningStep { get; private set; }

        /// <summary>
        /// Test seam (Mob Spawning Story 006): the spawn point validator S6 uses instead of one over
        /// <c>UnityNavMeshQuery</c> (EditMode has no NavMesh). Null in production. Set before the run starts.
        /// </summary>
        internal SpawnPointValidator SpawnPointValidatorOverride;

        /// <summary>
        /// Test seam (Mob Spawning Story 006): the Mob Spawning config values S6 uses instead of
        /// <see cref="MobSpawningConfigValues.Defaults"/>. Null in production. Set before the run starts.
        /// </summary>
        internal MobSpawningConfigValues? MobSpawningConfigOverride;

        // Mob Spawning Story 006: the Enemy AI config values, chosen once at S5 so Enemy AI and the spawner share one
        // DEATH_LINGER_TICKS; and the navigation config the composition built navigation with (the spawn point
        // validator's settings; null with a navigation seam).
        private EnemyAIConfigValues _enemyAIConfig;
        private NavigationConfig _navigationConfigInUse;

        /// <summary>The Enemy AI config values Enemy AI and the spawner use (chosen at S5).</summary>
        internal EnemyAIConfigValues EnemyAIConfigInUse => _enemyAIConfig;

        // The one DamageCalculator (Enemy AI Story 013), shared by player autos, skills and mob attacks; and RETURN_SPEED_CAP
        // of the navigation config the composition built (Enemy AI input).
        private DamageCalculator _damageCalculator;
        private float _returnSpeedCap;

        /// <summary>
        /// The composed navigation as <see cref="INavigationProvider"/> for the Enemy AI epic: the same instance as the
        /// service. Null when a navigation seam was passed in, and before S5.
        /// </summary>
        internal INavigationProvider NavigationProvider => _navigationService;

        /// <summary>The composed navigation service (test accessor); null when a seam was passed in, and before S5.</summary>
        internal ZoneNavigationService NavigationService => _navigationService;

        /// <summary>The zone id parsed at S0 (<c>ZoneID.Invalid</c> before it); read by the frame-rate probe.</summary>
        internal ZoneID ZoneId => _zoneId;

        /// <summary>The concrete factory teardown step 3 destroys pool roots of; null with a factory override or a passed seam.</summary>
        internal UnityNavAgentFactory UnityNavFactory => _unityNavAgentFactory;

        /// <summary>
        /// True once S9 subscribed the tick (the zone is Active) and no teardown was requested. The navigation frame-rate
        /// probe starts only then.
        /// </summary>
        internal bool IsTickRunning => _tickStarted && !_teardownRequested;

        /// <summary>
        /// Dev seam (Authentication Story 017, AC-AUTH-05b): opens the per-tick timestamp log writer at S9 from the parsed
        /// arguments. Null (the default, so tests create no file) disables the log; the host sets it in the Editor and
        /// development builds. A null result or a throwing factory runs the zone without the log. The composition
        /// disposes the returned writer at teardown.
        /// </summary>
        internal Func<ZoneStartupArgs, System.IO.TextWriter> TickLogWriterFactory;

        /// <summary>
        /// Test seam: replaces the creation of the relay listener at S7 when set (the real one connects from its own
        /// thread, which EditMode tests must not do). Receives the connection string and the receiver. Null in production.
        /// </summary>
        internal Func<string, CrossZoneRelayReceiver, IStartableZoneRelayListener> ListenerFactoryOverride;

        /// <summary>
        /// Test seam (Gateway Story 011): replaces <c>GatewayLinkClient.FromConfig</c> at S5 when set, so EditMode tests
        /// never open a socket. Receives the loaded config. Null in production.
        /// </summary>
        internal Func<ZoneServerConfig, IGatewayLinkChannel> GatewayLinkChannelFactoryOverride;

        /// <summary>The gateway link channel built at S5 (or attached by a test), or null.</summary>
        internal IGatewayLinkChannel LinkChannel => _linkChannel;

        /// <summary>
        /// Test seam: sets the link channel teardown step 2a stops, for compositions built without S5 (teardown tests).
        /// </summary>
        /// <param name="channel">The channel to stop at step 2a.</param>
        internal void AttachLinkChannel(IGatewayLinkChannel channel)
        {
            _linkChannel = channel;
        }

        /// <summary>
        /// When true, Editor and development builds log <c>[ZoneServer] teardown step &lt;n&gt; done in &lt;ms&gt; ms</c>
        /// after each teardown step and <c>[ZoneServer] teardown step 8: quit(&lt;code&gt;)</c> before the quit
        /// (Story 045 AC-10/AC-11 evidence; step 2a's time is the listener stop), and the zone lifecycle transitions
        /// through <see cref="ZoneInstancingWiringOptions.LogTransitions"/> (read at S5). Release builds never log these.
        /// The host sets it; off by default so EditMode tests see no extra log lines.
        /// </summary>
        public bool LogTeardownSteps { get; set; }

        /// <summary>
        /// Test seam: when set, S5 calls it with each constructed type name and once each with <c>Seal</c>,
        /// <c>RegisterTickDriven</c> and <c>CreateInboundTransport</c>. Null in production (no allocation).
        /// </summary>
        internal Action<string> ConstructionObserver;

        /// <summary>The one random provider created at S3 (ADR-013 Decision 4), or null before S3.</summary>
        internal IRandomProvider RandomProvider { get; private set; }

        /// <summary>The relay listener attached at S7 (before <c>Start()</c>), or null.</summary>
        internal IZoneRelayListener Listener => _listener;

        /// <summary>The tick loop built at S5, or null before it.</summary>
        internal ServerTickLoop TickLoop => _tickLoop;

        /// <summary>The character persistence service built at S5 (game-system stories take it from here), or null.</summary>
        internal CharacterPersistenceService PersistenceService { get; private set; }

        /// <summary>True once S4 created the data source (before the warm-up).</summary>
        internal bool DataSourceCreated { get; private set; }

        /// <summary>How many times the Dispose pass disposed the data source (0 or 1).</summary>
        internal int DataSourceDisposeCount { get; private set; }

        /// <summary>
        /// True while the real backend holds the connection string (S4 to the end of S7, or the Dispose pass). Lets a
        /// test check that the string, which carries the password, is cleared. False for an injected backend.
        /// </summary>
        internal bool HoldsConnectionString => (_backend as RealBackend)?.HasConnectionString ?? false;

        /// <summary>The zone's one Character Stats, built at S5 (Status Effects Story 009); null before S5. Later systems take it here.</summary>
        internal ZoneStats ZoneCharacterStats { get; private set; }

        /// <summary>
        /// Test seam: when set, S5 gives Movement this respawn provider instead of one built over the definition slot.
        /// Set before the run starts. Null in production.
        /// </summary>
        internal ITownRespawnPointProvider RespawnProviderOverride;

        /// <summary>
        /// Test seam (Death and Respawn Story 008): the Death and Respawn config asset. Null means
        /// <see cref="DeathAndRespawnConfigValues.Defaults"/> (no warning). An invalid asset makes the service
        /// constructor throw, so S5 fails. Set before the run starts.
        /// </summary>
        internal DeathAndRespawnConfig DeathAndRespawnConfigOverride;

        /// <summary>
        /// The zone's Death and Respawn service, built at S5 (Story 008); null before S5 and in a composition with no
        /// town respawn point provider (no definition slot and no <see cref="RespawnProviderOverride"/>).
        /// </summary>
        internal DeathAndRespawnService DeathAndRespawn { get; private set; }

        /// <summary>
        /// The phase 3 step that ticks <see cref="DeathAndRespawn"/>; null when no service was built.
        /// </summary>
        internal DeathAndRespawnPhaseStep DeathAndRespawnStep { get; private set; }

        /// <summary>
        /// The late-bound dead-ness reader shared by the snapshot builder, zone close, Enemy AI's candidate
        /// source and the entity state source; bound to <see cref="DeathAndRespawn"/> at S5. Null before S5.
        /// </summary>
        internal ForwardingPlayerDeathStateReader DeathStateReader { get; private set; }

        /// <summary>
        /// Test seam (like <see cref="RespawnProviderOverride"/>): the inner entity-state source under the Movement
        /// state source. Null: a <c>ForwardingZoneEntityStateSource</c> is bound to the production <c>ZoneEntityStateSource</c> after Auto-Attack Combat is built (Story 015). Set before the run starts.
        /// </summary>
        internal IZoneEntityStateSource EntityStateSourceOverride;

        /// <summary>The Movement-decorated entity state source the snapshot builder and Combat read (Story 015); set while the pipeline is built. Test accessor.</summary>
        internal IZoneEntityStateSource EntityStateSource { get; private set; }

        /// <summary>
        /// Test seam / production hook (Skill System Story 011): the class registry shared by Leveling and the Skill
        /// System. Set before the run starts. Null means the S2 loader's registry (<see cref="IZoneClassSkillData"/>, Story 013),
        /// or an empty registry when the loader has none (the stand-in loader: tests only).
        /// </summary>
        internal ClassRegistry ClassRegistryOverride;

        /// <summary>
        /// Test seam (Story 011): the skill roster for the Skill System. Null means the S2 loader's roster
        /// (<see cref="IZoneClassSkillData"/>, Story 013), or a silent empty in-code database when the loader has none
        /// (the stand-in loader: tests only). Set before the run starts.
        /// </summary>
        internal SkillDatabase SkillDatabaseOverride;

        /// <summary>Skill System config asset (Story 011). Null means <c>SkillSystemConfigValues.Default</c>. Set before the run starts.</summary>
        internal SkillSystemConfig SkillSystemConfigOverride;

        /// <summary>Test seam (Skill System Story 011 Part B): the skill damage calculator. Null builds a <see cref="DamageCalculatorSkillAdapter"/> over the zone's one DamageCalculator. Set before the run starts.</summary>
        internal ISkillDamageCalculator SkillDamageCalculatorOverride;

        /// <summary>The zone's Leveling service, built at S5 (Skill System Story 011); null before S5.</summary>
        internal LevelingService ZoneLeveling { get; private set; }

        /// <summary>The class registry Leveling and Skill actually use (the override or the empty fallback); null before S5.</summary>
        internal ClassRegistry ClassRegistryInUse { get; private set; }

        /// <summary>
        /// Test seam (Character Persistence Story 018): the record store under <see cref="PersistenceService"/>. Null builds
        /// <see cref="NpgsqlCharacterRecordStore"/> over the S4 data source. Set before the run starts.
        /// </summary>
        internal ICharacterRecordStore RecordStoreOverride;

        /// <summary>
        /// Test seam (Character Persistence Story 018): the entry token validator Zone Instancing admission uses. Null keeps
        /// <see cref="RejectingZoneEntryTokenValidator"/> (the session epic wires the real one). Set before the run starts.
        /// </summary>
        internal IZoneEntryTokenValidator EntryTokenValidatorOverride;

        /// <summary>Test seam (Story 016 AC-7): <c>CONNECTING_TIMEOUT_SECONDS</c> override; 0 keeps <see cref="AdmittedEntries.CONNECTING_TIMEOUT_SECONDS"/>. Read once at S5 (the startup check and the wiring options); setting it later has no effect.</summary>
        internal int ConnectingTimeoutSecondsOverride;

        internal int EffectiveConnectingTimeoutSeconds =>
            ConnectingTimeoutSecondsOverride > 0 ? ConnectingTimeoutSecondsOverride : AdmittedEntries.CONNECTING_TIMEOUT_SECONDS;

        /// <summary>Heartbeat sink that does nothing: the heartbeat counts as activity before decoding (Story 016).</summary>
        private sealed class NoOpHeartbeatSink : IConnectionMessageSink
        {
            public void OnConnectionMessage(uint clientId, ushort messageTypeId, IronGrind.CharacterStats.EntityID senderEntityId, ReadOnlySpan<byte> body) { }
        }

        /// <summary>The zone's Currency, built at S5 (Character Persistence Story 018: receives the gold restore on load); null before S5.</summary>
        internal ZoneCurrencySystem ZoneCurrency { get; private set; }

        /// <summary>
        /// Test seam (Character Persistence Story 019): the item database under Inventory and Equipment. Null means the S2
        /// loader's database (<see cref="IZoneItemData"/>); when the loader has none, S5 fails with <see cref="NO_ITEM_DATABASE_ERROR"/>
        /// (Story 020; the production host always provides one). Set before the run starts.
        /// </summary>
        internal IItemDatabase ItemDatabaseOverride;

        /// <summary>The item database Inventory and Equipment actually use (the override or S2's); null before S5.</summary>
        internal IItemDatabase ItemDatabaseInUse { get; private set; }

        /// <summary>The zone's Inventory, shared by Persistence (restore) and Zone Instancing (session end), built at S5 (Story 019); null before S5.</summary>
        internal InventoryService ZoneInventory { get; private set; }

        /// <summary>The zone's Equipment, shared by Persistence (restore) and Zone Instancing (session end), built at S5 (Story 019); null before S5.</summary>
        internal EquipmentService ZoneEquipment { get; private set; }

        /// <summary>The zone's Skill System service, built at S5 (Story 011); null before S5.</summary>
        internal SkillSystemService SkillService { get; private set; }

        /// <summary>The zone's skill-cast intake, built at S5 (Story 011); null before S5.</summary>
        internal SkillCastIntake SkillIntake { get; private set; }

        /// <summary>The zone's skill-cast validation step, built at S5 (Story 011); null before S5.</summary>
        internal SkillCastStep SkillStep { get; private set; }

        /// <summary>The zone's skill-cast executor, built at S5 (Story 011); null before S5.</summary>
        internal SkillCastExecutor SkillExecutor { get; private set; }

        /// <summary>The inner auto-attack damage applier (<c>CharacterStatsAutoAttackDamageApplier</c>) under <see cref="AutoAttackDamageApplier"/>; null before S5.</summary>
        internal IAutoAttackDamageApplier AutoAttackInnerDamageApplier { get; private set; }

        /// <summary>The late-bound zone entry responder (Story 011); null before S5.</summary>
        internal ForwardingZoneEntryResponder EntryResponder { get; private set; }

        /// <summary>The zone's Movement service, built at S5 (Movement Story 009); null before S5.</summary>
        internal MovementService Movement { get; private set; }

        /// <summary>The zone's per-tick position broadcast step, built at S5 (Movement Story 009); null before S5.</summary>
        internal PositionBroadcastStep PositionBroadcast { get; private set; }

        /// <summary>The zone's per-tick cycle-timer broadcast step, built at S5 (Auto-Attack Combat Story 008); null before S5.</summary>
        internal CycleTimerBroadcastStep CycleTimerBroadcast { get; private set; }

        /// <summary>The zone's per-tick R-U batch step, built at S5 (Hit Detection Story 003); null before S5.</summary>
        internal RUBatchOutboundStep RUBatch { get; private set; }

        /// <summary>The zone's hit result emitter, built at S5 right after the R-U batch step (Hit Detection Story 004); null before S5.</summary>
        internal HitResultEmitter HitResultEmitter { get; private set; }

        /// <summary>The Auto-Attack Combat system (Story 006); null until S5 has built it.</summary>
        internal AutoAttackCombatSystem AutoAttackCombat { get; private set; }

        /// <summary>
        /// The per-client SetTarget slot tracker (networking-relevance-filter.md RFR-3a). Exposed so a
        /// disconnect hook can call <see cref="TargetSlotTracker.Clear"/>; wiring deferred (Auto-Attack
        /// Combat Story 006 code review).
        /// </summary>
        internal TargetSlotTracker TargetSlotTracker { get; private set; }

        /// <summary>
        /// The damage applier handed to the Auto-Attack Combat system: a <see cref="DamageOutputApplier"/> over the
        /// inner applier (Auto-Attack Combat Story 007); null until S5 has built it.
        /// </summary>
        internal IAutoAttackDamageApplier AutoAttackDamageApplier { get; private set; }

        /// <summary>The auto-face output handed to Auto-Attack Combat (Story 009): an <see cref="AutoFaceEmitter"/>; null until S5 has built it.</summary>
        internal IAutoAttackFaceOutput AutoAttackFaceOutput { get; private set; }

        /// <summary>
        /// The zone's RPC guard chain; internal test seam so composition tests can register entity ownership and
        /// session-ready state (Auto-Attack Combat Story 006 code review).
        /// </summary>
        internal CrossCuttingRpcGuardChain GuardChain { get; private set; }

        /// <summary>The zone's Status Effects service, built at S5 (Story 009); null before S5.</summary>
        internal StatusEffectsService StatusEffects { get; private set; }

        /// <summary>The ordered phase-3 steps the composition lists: Zone Instancing, then Status Effects (Story 009).</summary>
        internal IList<IZoneSimulationStep> SimulationSteps => _simulationSteps;

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

                // The wait has no try around it; reading IsDone is guarded inside PollSceneDone. The server tick does not
                // advance before S9, so each polled frame ticks the lifecycle with the loading time (T-3 by timeout).
                double sceneWaitStart = _clock();
                while (!_teardownRequested && !PollSceneDone())
                {
                    if (TickLoadingLifecycle(sceneWaitStart))
                    {
                        break;
                    }

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

            // The first code is recorded before the wiring is called: the wiring's OnClosed calls back here and returns above.
            _exitCode = code;
            _teardownRequested = true;

            // A direct Fatal (tick failure) also closes the zone, so admission stops before the next Update.
            if (code == ZoneExitCode.Fatal && _wiring != null && _wiring.Lifecycle.State != ZoneState.Closed)
            {
                _wiring.RequestFatal(ReadCurrentTick());
            }
        }

        /// <summary>
        /// The quit path (Zone Instancing Story 014, T-4/T-6): before the wiring exists (before S5) or once the zone is
        /// Closed it requests a Clean teardown; while Loading it quits the zone at once (T-6); in Active or Draining the
        /// wiring applies it on the next tick (T-4, or T-6 for a second quit). Does nothing once a teardown was requested.
        /// The host's <c>-teardownAfterSeconds</c> trigger and <see cref="OnWantsToQuit"/> both call it.
        /// </summary>
        public void RequestQuit()
        {
            if (_teardownRequested)
            {
                return;
            }

            if (_wiring == null)
            {
                RequestTeardown(ZoneExitCode.Clean);
                return;
            }

            switch (_wiring.Lifecycle.State)
            {
                case ZoneState.Loading:
                    _wiring.RequestQuitNow(_lastLoadingTick);
                    break;
                case ZoneState.Active:
                case ZoneState.Draining:
                    // Only the first quit starts the stall clock, so repeated wantsToQuit calls cannot postpone the forced close.
                    if (double.IsNaN(_quitStallStartSeconds))
                    {
                        _quitStallStartSeconds = _clock();
                    }

                    _wiring.RequestQuit();
                    break;
                default:
                    RequestTeardown(ZoneExitCode.Clean);
                    break;
            }
        }

        /// <summary>
        /// Quit stall guard, called by the host's Update before <see cref="TryBeginTeardown"/>. A quit in Active or
        /// Draining is applied by the next server tick; if the network ticks have stopped, the zone would never close.
        /// When no teardown was requested, a wiring exists, a quit is pending or the zone is Draining, and
        /// <see cref="QUIT_TICK_STALL_SECONDS"/> have passed since the later of the last completed tick and the first
        /// quit request (so a quit right after a long gap is not forced at once), it logs one warning and requests a
        /// Clean teardown. Does nothing otherwise.
        /// </summary>
        public void PollQuitStall()
        {
            if (_teardownRequested || _wiring == null)
            {
                return;
            }

            if (_wiring.PendingQuitCount <= 0 && _wiring.Lifecycle.State != ZoneState.Draining)
            {
                return;
            }

            double reference = double.IsNaN(_quitStallStartSeconds)
                ? _lastTickSeconds
                : Math.Max(_lastTickSeconds, _quitStallStartSeconds);
            double stalled = _clock() - reference;
            if (stalled < QUIT_TICK_STALL_SECONDS)
            {
                return;
            }

            Debug.LogWarning(LOG_PREFIX + " no tick for "
                + stalled.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + " s with a quit pending: requesting a Clean teardown");
            RequestTeardown(ZoneExitCode.Clean);
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (LogTeardownSteps)
            {
                Debug.Log(LOG_PREFIX + " teardown step 8: quit(" + (int)_exitCode + ")");
            }
#endif
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
        /// otherwise takes the <see cref="RequestQuit"/> path and returns false. Does no teardown work itself.
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

            RequestQuit();
            return false;
        }

        /// <summary>
        /// Attaches the Zone Instancing wiring built at S5 (before the dispatcher is sealed). From now on the zone state
        /// owner, the close steps, the quit path and a direct Fatal go through it; without it the injected
        /// <see cref="IZoneStateOwner"/> and <see cref="IZoneCloseSteps"/> are used. Tests call it with a wiring over fakes.
        /// </summary>
        /// <param name="wiring">The wiring; its lifecycle starts Loading with loading tick 0.</param>
        internal void AttachZoneInstancing(ZoneInstancingWiring wiring)
        {
            _wiring = wiring;
        }

        /// <summary>The attached Zone Instancing wiring, or null before S5.</summary>
        internal ZoneInstancingWiring ZoneInstancing => _wiring;

        private IZoneStateOwner StateOwner => _wiring != null ? (IZoneStateOwner)_wiring : _zoneStateOwner;

        private IZoneCloseSteps CloseStepsOwner => _wiring != null ? (IZoneCloseSteps)_wiring : _closeSteps;

        // One loading-time tick of the lifecycle during the S6 scene wait. True when it closed the zone without a
        // teardown request (T-3 by timeout): the wait ends as S-fail, one error line like the other S6 failures.
        private bool TickLoadingLifecycle(double waitStartSeconds)
        {
            if (_wiring == null)
            {
                return false;
            }

            double elapsed = _clock() - waitStartSeconds;
            _lastLoadingTick = elapsed > 0d ? (uint)Math.Floor(elapsed * ServerTickLoop.TICK_RATE_HZ) : 0u;
            _wiring.OnLoadingTick(_lastLoadingTick);
            if (_wiring.Lifecycle.State != ZoneState.Closed || _teardownRequested)
            {
                return false;
            }

            LogStartupFailure(SCENE_STEP, "LoadTimeout");
            _sceneFaulted = true;
            return true;
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

        /// <summary>
        /// Attaches the relay listener S7 created, before it calls <c>Start()</c>, so teardown step 2a stops it even
        /// when <c>Start()</c> throws.
        /// </summary>
        /// <param name="listener">The listener to stop at teardown step 2a.</param>
        internal void AttachListener(IZoneRelayListener listener)
        {
            _listener = listener;
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

        private uint ReadCurrentTick()
        {
            return _tickLoop != null ? _tickLoop.ServerTickNumber : 0u;
        }

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
            try
            {
                _tickLoop.AdvanceTick(seconds);
            }
            catch (Exception ex)
            {
                // AC-15 (Story 045): no exception escapes into NGO's Tick event. The first failure is logged (type
                // name only: no message, no stack, nothing that could carry a connection detail) and requests a
                // Fatal teardown; later failing ticks do neither.
                if (!_tickFailureReported)
                {
                    _tickFailureReported = true;
                    Debug.LogError(LOG_PREFIX + " tick failed: " + ex.GetType().Name);
                    RequestTeardown(ZoneExitCode.Fatal);
                }
            }

            // The dev log records every tick, also one that threw (its duration is the evidence for a slow failing tick),
            // and sits outside the tick try so a log failure can neither be taken for a tick failure (Fatal) nor hide one.
            RecordTickLog(now);
            _lastTickSeconds = now;
        }

        // TickTimestampLog swallows writer I/O errors itself; this guard covers a throwing warning sink or a bug.
        private void RecordTickLog(double startSeconds)
        {
            if (_tickTimestampLog == null)
            {
                return;
            }

            try
            {
                _tickTimestampLog.Record(_tickLoop.ServerTickNumber, startSeconds, _clock() - startSeconds);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LOG_PREFIX + " tick log disabled: " + ex.GetType().Name);
                _tickTimestampLog = null;
            }
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

        // Story 016 AC-7: the connecting timeout must exceed the longest character load (the persistence watchdog), or a
        // player could be admitted after the session was already dropped. S-fail otherwise.
        private bool CheckSessionTimeoutInvariant()
        {
            long connectingTicks = (long)EffectiveConnectingTimeoutSeconds * ServerTickLoop.TICK_RATE_HZ;
            if (connectingTicks > TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + " S5: connecting timeout (" + connectingTicks + " ticks) must exceed the persistence load watchdog ("
                + TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS + " ticks)");
            return false;
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
                    return CheckSessionTimeoutInvariant() && _backend.ConstructObjects();
                case SCENE_STEP:
                    return part == 1 ? StartupBeginSceneLoad() : StartupFinishScene();
                case 7:
                    return _backend.StartListener();
                case 8:
                    return StartupStartNetwork();
                case 9:
                    return StartupSubscribeTick();
                default:
                    // The injected registration (tests) wins; otherwise the real link registration built at S5.
                    IGatewayRegistration registration = _gateway ?? _linkRegistration
                        ?? throw new InvalidOperationException("No gateway registration: none injected and S5 did not build the link.");
                    registration.RegisterZone(_zoneId, _args.Port);
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

        // S6 part 2. With the real navigation (Navigation Story 010) a zone whose definition has no baked NavMesh fails
        // here: ZoneNavigationService.Initialize throws on the invalid instance, which is S-fail at S6, and teardown
        // step 3 still runs because MarkNavigationInitialized is set first. Only StartingZone exists today and it has the
        // Story 008 bake.
        private bool StartupFinishScene()
        {
            if (!_scene.Succeeded)
            {
                return false;
            }

            NavMeshDataInstance instance = _scene.AddNavMeshData();
            Movement?.SetNavMeshAvailable(instance.valid);
            MarkNavigationInitialized();
            _navigation.Initialize(_zoneId, instance);
            if (!_boundsValidator.Validate(_zoneId))
            {
                MarkZoneStateEntered();
                StateOwner.MarkClosedInvalidBounds(_zoneId);
                return false;
            }

            return TryBuildMobSpawner();
        }

        // T-2 sweep (Story 006) and its one info line (Story 007 AC-6, the smoke check's observable). No spawner: nothing.
        private void PopulateMobSpawner()
        {
            MobSpawningService spawner = MobSpawner;
            if (spawner == null)
            {
                return;
            }

            spawner.PopulateOnActive();
            Debug.Log(MOB_SPAWNING_POPULATED_LOG_PREFIX + _zoneId + ": " + spawner.LiveCount + " of "
                + spawner.PointCount + " points Live, " + spawner.DeferredCount + " Deferred");
        }

        // Mob Spawning Story 006 (CR-MS-2, T-1): the spawner is built here, not at S5, because the scene loader fills the
        // definition slot during S6 and the NavMesh exists only after _navigation.Initialize (ADR-002). A failed load is
        // S-fail at S6 (the validators logged why). No slot (EditMode rigs) or no validator (a navigation seam without
        // SpawnPointValidatorOverride) builds no spawner and logs nothing; the S5 forwarder and step stay no-ops.
        private bool TryBuildMobSpawner()
        {
            if (_definitionSlot == null || MobEvents == null || ZoneInstancing == null)
            {
                return true;
            }

            SpawnPointValidator validator = SpawnPointValidatorOverride;
            if (validator == null)
            {
                if (_navigationConfigInUse == null)
                {
                    return true;
                }

                validator = new SpawnPointValidator(new UnityNavMeshQuery(), _navigationConfigInUse);
            }

            MobSpawningConfigValues config = MobSpawningConfigOverride ?? MobSpawningConfigValues.Defaults;
            if (!MobSpawningZoneLoader.TryLoad(_zoneId, _definitionSlot.Current, MobRegistryInUse, validator,
                    ZoneInstancing.MobRoster, MobEvents, config, _enemyAIConfig.DeathLingerTicks,
                    out MobSpawningService spawner))
            {
                return false;
            }

            MobSpawner = spawner;
            MobSpawnTimers.Spawner = spawner;
            MobSpawningStep.Spawner = spawner;
            return true;
        }

        private bool StartupStartNetwork()
        {
            // The NGO tick rate comes from the shared settings the client applies too (one config hash).
            _network.Configure(NgoSharedSettings.TICK_RATE, ServerTickLoop.TICK_RATE_HZ, 0, _args.Port);
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
            StateOwner.MarkActive(_zoneId);
            // Mob Spawning Story 006 (CR-MS-7, T-2): populate right after Active, before the first tick.
            PopulateMobSpawner();
            _previousTickSeconds = _clock();
            _lastTickSeconds = _previousTickSeconds;
            OpenTickLog();
            _network.SubscribeTick(_networkTickHandler);
            _tickHandler = _networkTickHandler;
            MarkTickStarted();
            return true;
        }

        // Dev tick timestamp log (Story 017, AC-AUTH-05b). Never fails startup: any problem runs the zone without it.
        private void OpenTickLog()
        {
            if (TickLogWriterFactory == null)
            {
                return;
            }

            System.IO.TextWriter writer = null;
            try
            {
                writer = TickLogWriterFactory(_args);
                if (writer == null)
                {
                    return;
                }

                _tickTimestampLog = new TickTimestampLog(writer, WriteTickLogLine);
                _disposables.Add(writer);
            }
            catch (Exception ex)
            {
                _tickTimestampLog = null;
                writer?.Dispose();
                Debug.LogWarning(LOG_PREFIX + " tick log disabled: " + ex.GetType().Name);
            }
        }

        private static void WriteTickLogLine(string line)
        {
            if (line.StartsWith(TickTimestampLog.GAP_WARNING_PREFIX, StringComparison.Ordinal)
                || line.StartsWith(TickTimestampLog.DURATION_WARNING_PREFIX, StringComparison.Ordinal))
            {
                Debug.LogWarning(LOG_PREFIX + " " + line);
            }
            else
            {
                Debug.Log(LOG_PREFIX + " " + line);
            }
        }

        /// <summary>
        /// Opens the dev tick timestamp log file: <c>-tickLogPath</c> when given, else
        /// <c>persistentDataPath/tick-log-zone&lt;zoneId&gt;.csv</c>. Logs the path once. Returns null (with a warning)
        /// when the file cannot be opened, so the zone runs without the log. The host assigns this to
        /// <see cref="TickLogWriterFactory"/> in the Editor and development builds only.
        /// </summary>
        /// <param name="args">The parsed startup arguments.</param>
        /// <returns>An unflushed-by-default writer, or null.</returns>
        internal static System.IO.TextWriter OpenTickLogFile(ZoneStartupArgs args)
        {
            try
            {
                string path = args.TickLogPath ?? System.IO.Path.Combine(
                    Application.persistentDataPath, "tick-log-zone" + args.ZoneId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".csv");
                var writer = new System.IO.StreamWriter(path, false) { AutoFlush = false };
                Debug.Log(LOG_PREFIX + " tick log: " + path);
                return writer;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LOG_PREFIX + " tick log file not opened: " + ex.GetType().Name);
                return null;
            }
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            long started = Stopwatch.GetTimestamp();
#endif
            try
            {
                RunStepBody(n);
            }
            catch (Exception ex)
            {
                LogStepFailure(n, ex);
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Story 045 AC-10/AC-11 evidence: one line per step, in order; step 2a's time is the listener stop.
            if (LogTeardownSteps)
            {
                long elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000L / Stopwatch.Frequency;
                Debug.Log(LOG_PREFIX + " teardown step " + STEP_NAMES[n] + " done in " + elapsedMs + " ms");
            }
#endif
        }

        private void RunStepBody(int n)
        {
            switch (n)
            {
                case 1:
                    UnsubscribeTick();
                    break;
                case 2:
                    // Step 2a: the relay listener, then the gateway link (Gateway Story 011); the link stops even if the
                    // listener's Stop throws. Stop is idempotent, so the OnDestroy path is harmless after a teardown.
                    try
                    {
                        _listener?.Stop();
                    }
                    finally
                    {
                        _linkChannel?.Stop();
                    }

                    break;
                case 3:
                    // The tick is already halted (step 1). Teardown() disables the agents and removes the NavMesh data; the
                    // pool roots (GameObjects) of the factory this composition built are destroyed after it, even if it threw.
                    if (_navigationInitialized)
                    {
                        try
                        {
                            _navigation?.Teardown();
                        }
                        finally
                        {
                            _unityNavAgentFactory?.DestroyPoolRoots();
                        }
                    }

                    break;
                case 4:
                    if (_zoneStateEntered)
                    {
                        CloseStepsOwner?.RunCloseSteps();
                    }

                    break;
                case 5:
                    if (_tickStarted)
                    {
                        FlushOutbound();
                    }

                    break;
                case 6:
                    // Also after the close steps when the tick never started (T-3 path): their save callbacks complete here.
                    if (_tickStarted || _zoneStateEntered)
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
            (_backend as RealBackend)?.ClearConnectionString();
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

            // Navigation Story 010: the navigation unsubscribed itself in Teardown (step 3); drop the bus reference and
            // destroy the config asset this composition created (never an injected override).
            MobEvents = null;
            MobStore = null;
            EnemyAI = null;
            // Mob Spawning Story 006: the spawner unsubscribed from the bus at close step 1; drop the references.
            MobSpawner = null;
            if (MobSpawnTimers != null)
            {
                MobSpawnTimers.Spawner = null;
            }

            if (MobSpawningStep != null)
            {
                MobSpawningStep.Spawner = null;
            }

            _navigationConfigInUse = null;
            MobRegistryInUse = null;
            _damageCalculator = null;
            _returnSpeedCap = 0f;
            if (_ownedNavigationConfig != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_ownedNavigationConfig);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_ownedNavigationConfig);
                }
            }

            _ownedNavigationConfig = null;
        }

        // ------------------------------------------------------------------ real backend (Story 044)

        /// <summary>
        /// Builds the Npgsql connection string for the zone (ADR-006 Amendment 3) from the server config and the
        /// password. The string holds the password: never log it. The application name is
        /// <c>irongrind-zone-&lt;zoneId&gt;</c>.
        /// </summary>
        /// <param name="config">The parsed server config.</param>
        /// <param name="password">The password from <c>IRONGRIND_PG_PASSWORD</c>.</param>
        /// <param name="zoneId">The zone id from the command line.</param>
        /// <returns>The connection string (escaped by the builder).</returns>
        internal static string BuildConnectionString(ZoneServerConfig config, string password, uint zoneId)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = config.Host,
                Port = config.Port,
                Database = config.Database,
                Username = config.Username,
                Password = password,
                Timeout = config.Timeout,
                CommandTimeout = config.CommandTimeout,
                Pooling = true,
                MinPoolSize = config.MinPoolSize,
                MaxPoolSize = config.MaxPoolSize,
                ConnectionIdleLifetime = config.ConnectionIdleLifetime,
                ApplicationName = ApplicationNameFor(zoneId)
            };
            return builder.ConnectionString;
        }

        /// <summary>The PostgreSQL <c>application_name</c> of a zone's connections: <c>irongrind-zone-&lt;zoneId&gt;</c>.</summary>
        /// <param name="zoneId">The zone id.</param>
        /// <returns>The application name.</returns>
        internal static string ApplicationNameFor(uint zoneId)
        {
            return APPLICATION_NAME_PREFIX + zoneId;
        }

        // Disposes the data source and counts it (the count is the AC-9 observation; it costs one int).
        private sealed class DataSourceHandle : IDisposable
        {
            private readonly ZoneServerComposition _owner;
            private readonly NpgsqlDataSource _dataSource;

            public DataSourceHandle(ZoneServerComposition owner, NpgsqlDataSource dataSource)
            {
                _owner = owner;
                _dataSource = dataSource;
            }

            public void Dispose()
            {
                _owner.DataSourceDisposeCount++;
                _dataSource.Dispose();
            }
        }

        // The transport close seam for the Zone Instancing wiring. The wiring is built before the dispatcher is sealed, the
        // NGO shell after it, so the target is set later; a close before then (or with a fake transport) does nothing.
        private sealed class DeferredTransportDisconnector : IInboundTransportDisconnector
        {
            public IInboundTransportDisconnector Target;

            public void DisconnectClient(ulong transportClientId)
            {
                Target?.DisconnectClient(transportClientId);
            }
        }

        // The pipeline's outbound step (Movement Story 009, Hit Detection Story 003): the outbox flush, then the R-U
        // batches, then the cycle-timer packets (Path 2b-i, Auto-Attack Combat Story 008), then the position packets
        // (Path 2b-ii). Teardown step 4b flushes the outbox only; pending R-U messages are discarded.
        private sealed class ZoneOutboundSteps : IZoneOutboundStep
        {
            private readonly IZoneOutboundStep _first;
            private readonly IZoneOutboundStep _second;
            private readonly IZoneOutboundStep _third;
            private readonly IZoneOutboundStep _fourth;

            public ZoneOutboundSteps(IZoneOutboundStep first, IZoneOutboundStep second, IZoneOutboundStep third,
                IZoneOutboundStep fourth)
            {
                _first = first;
                _second = second;
                _third = third;
                _fourth = fourth;
            }

            public void Flush(uint currentTick)
            {
                _first.Flush(currentTick);
                _second.Flush(currentTick);
                _third.Flush(currentTick);
                _fourth.Flush(currentTick);
            }
        }

        // The real S3, S4, S5, S7. A private nested class so the construction stays inside this file (AC-10).
        private sealed class RealBackend : IZoneBackendSteps
        {
            private readonly ZoneServerComposition _owner;
            private readonly IZoneEnvironment _environment;
            private string _connectionString;
            private NpgsqlDataSource _dataSource;
            private CrossZoneRelayReceiver _receiver;

            // S5 objects shared between its build methods.
            private TickCompletionQueue _queue;
            private ServerTickLoop _tickLoop;
            private CrossZoneRelayAllowlist _allowlist;
            private CrossZoneRelayInbox _inbox;
            private NpgsqlRelayPublisher _publisher;
            private CharacterMutationGate _gate;
            private ZoneConnectionCharacterDirectory _directory;
            private RecordingConnectionActivitySink _activitySink;
            private ConnectionStateMachine _sessionMachine;
            private readonly DeferredTransportDisconnector _disconnector = new DeferredTransportDisconnector();
            // Death and Respawn Story 008: created before the wiring, bound once the service is built.
            private readonly ForwardingPlayerDeathStateReader _deathReader = new ForwardingPlayerDeathStateReader();
            // The provider Movement got (Story 008 AC-2: the service gets the same instance); null without one.
            private ITownRespawnPointProvider _respawnProvider;

            public RealBackend(ZoneServerComposition owner, IZoneEnvironment environment)
            {
                _owner = owner;
                _environment = environment;
            }

            // The string holds the password; it lives here from S4 to the end of S7 (or the Dispose pass).
            public void ClearConnectionString()
            {
                _connectionString = null;
            }

            public bool HasConnectionString => _connectionString != null;

            public bool InitializeRandom()
            {
                // The factory logs the seed on one line ("[Random] PRNG seed: n"); nothing else logs it.
                _owner.RandomProvider = RandomProviderFactory.CreateSeededFromEntropy(out int _);
                return true;
            }

            public bool OpenDatabase()
            {
                ZoneServerConfig config = _owner._config;
                string password = _environment.GetVariable(PASSWORD_VARIABLE);
                if (string.IsNullOrEmpty(password))
                {
                    Debug.LogError(LOG_PREFIX + " S4: environment variable " + PASSWORD_VARIABLE + " is not set or empty");
                    return false;
                }

                _connectionString = BuildConnectionString(config, password, _owner._args.ZoneId);
                _dataSource = NpgsqlDataSource.Create(_connectionString);
                _owner.DataSourceCreated = true;

                // Registered first, so the Dispose pass (reverse order) disposes it last, also after a warm-up failure.
                _owner.RegisterDisposable(new DataSourceHandle(_owner, _dataSource));
                return WarmUp(config.DbWarmupTimeoutMs);
            }

            private bool WarmUp(int timeoutMs)
            {
                Func<CancellationToken, Task> warm = _owner.WarmUpOverride ?? WarmUpDataSource;
                var cancellation = new CancellationTokenSource();
                CancellationToken token = cancellation.Token;
                Task task = Task.Run(() => warm(token));
                bool completed;
                try
                {
                    completed = task.Wait(timeoutMs);
                }
                catch (AggregateException ex)
                {
                    cancellation.Dispose();
                    Debug.LogError(LOG_PREFIX + " S4: database warm-up failed: " + Describe(ex));
                    return false;
                }

                if (!completed)
                {
                    // Cancelled but not disposed: the abandoned task may still register on the token, which throws
                    // ObjectDisposedException on Mono after Dispose. A source without a timer holds no unmanaged
                    // resource, so the garbage collector reclaims it.
                    cancellation.Cancel();
                    task.ContinueWith(ObserveFault, TaskContinuationOptions.OnlyOnFaulted);
                    Debug.LogError(LOG_PREFIX + " S4: database warm-up timed out");
                    return false;
                }

                cancellation.Dispose();
                return true;
            }

            private async Task WarmUpDataSource(CancellationToken token)
            {
                using (NpgsqlCommand command = _dataSource.CreateCommand("SELECT 1"))
                {
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            private static void ObserveFault(Task task)
            {
                // Reading Exception marks it observed; nothing else to do for an abandoned warm-up.
                AggregateException ignored = task.Exception;
            }

            // The exception type name, and the PostgreSQL SqlState when the chain holds a PostgresException. Never the
            // message: Npgsql messages can carry the host and user name.
            private static string Describe(AggregateException aggregate)
            {
                Exception top = aggregate.InnerException ?? aggregate;
                string sqlState = null;
                for (Exception e = top; e != null && sqlState == null; e = e.InnerException)
                {
                    var postgres = e as PostgresException;
                    if (postgres != null)
                    {
                        sqlState = postgres.SqlState;
                    }
                }

                return sqlState == null ? top.GetType().Name : top.GetType().Name + " SqlState=" + sqlState;
            }

            private void Observe(string name)
            {
                _owner.ConstructionObserver?.Invoke(name);
            }

            // S5, in the AC-6 order: persistence, relay, dispatch, then outbound and the pipeline. Character Stats and
            // Leveling come first inside BuildPersistence (Character Persistence Story 018): the applier needs them.
            public bool ConstructObjects()
            {
                if (!BuildPersistence())
                {
                    return false;
                }

                BuildRelay();
                InboundRequestDispatcher dispatcher = BuildDispatcher();
                BuildOutboundAndPipeline(dispatcher);
                return true;
            }

            // False (S5 fails, nothing after Character Stats is built) when there is no item database (Story 020).
            private bool BuildPersistence()
            {
                _queue = new TickCompletionQueue();
                Observe(nameof(TickCompletionQueue));
                _tickLoop = new ServerTickLoop();
                Observe(nameof(ServerTickLoop));
                BuildCharacterStats();
                if (!BuildInventoryAndEquipment())
                {
                    return false;
                }

                if (!BuildMobRegistry())
                {
                    return false;
                }

                ICharacterRecordStore store = _owner.RecordStoreOverride ?? new NpgsqlCharacterRecordStore(_dataSource);
                // Observed under the production name even when the test override is used: the construction-order
                // check names the step, not the type that filled it.
                Observe(nameof(NpgsqlCharacterRecordStore));

                // Story 020 (TD-091): the save constructor. The zone locator reports this zone for every character; the
                // write queue serialises writes per character (CR-CP-7); the composition root is the one place that reads
                // the system clock (Character Persistence only measures write latency with it).
                var writeQueue = new CharacterWriteQueue();
                Observe(nameof(CharacterWriteQueue));
                var locator = new ZoneServerCharacterZoneLocator(_owner._zoneId);
                Observe(nameof(ZoneServerCharacterZoneLocator));
                _owner.ZoneCurrency = new ZoneCurrencySystem();
                _owner.PersistenceService = new CharacterPersistenceService(store, _owner.ZoneCharacterStats,
                    _owner.ZoneLeveling, _owner.ZoneInventory, _owner.ZoneCurrency, _owner.ZoneEquipment, locator, writeQueue,
                    () => DateTime.UtcNow, TimeSpan.FromSeconds(CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS));
                Observe(nameof(CharacterPersistenceService));
                return true;
            }

            // Status Effects Story 009 / Skill System Story 013, moved ahead of Persistence by Character Persistence Story 018:
            // one class registry, Leveling and Character Stats per zone. Needs no wiring.
            private void BuildCharacterStats()
            {
                // Story 013: the override, then the S2 loader's registry, then an empty one (stand-in loader: tests only).
                // The data is found by type on the loader itself: a decorator around ZoneClassSkillDataLoader must also
                // implement IZoneClassSkillData, or S5 silently falls back to the empty registry.
                ClassRegistry classRegistry = _owner.ClassRegistryOverride
                    ?? (_owner._staticDataLoader as IZoneClassSkillData)?.LoadedClassRegistry
                    ?? new ClassRegistry();

                var leveling = new LevelingService(XpThresholdTable.Values, classRegistry);
                var stats = new ZoneStats(leveling);
                leveling.AttachCharacterStats(stats);
                _owner.ZoneLeveling = leveling;
                _owner.ClassRegistryInUse = classRegistry;
                _owner.ZoneCharacterStats = stats;
                Observe("CharacterStats");
            }

            // Character Persistence Story 019: the item database from S2 (ADR-009 S2), then the real Inventory and Equipment
            // over Character Stats. Built before Persistence (the load applier restores into them). Needs no wiring.
            // Story 020: no database is a startup failure. With an empty one every bag would be imported empty and then
            // saved that way over the stored record.
            private bool BuildInventoryAndEquipment()
            {
                IItemDatabase items = _owner.ItemDatabaseOverride
                    ?? (_owner._staticDataLoader as IZoneItemData)?.LoadedItemDatabase;
                if (items == null)
                {
                    Debug.LogError(NO_ITEM_DATABASE_ERROR);
                    return false;
                }

                _owner.ItemDatabaseInUse = items;
                EnhancementConfig enhancement = EnhancementConfig.Default;
                var inventory = new InventoryService(items, () => _tickLoop.ServerTickNumber, enhancement.MaxEnhancementLevel);
                _owner.ZoneInventory = inventory;
                Observe(nameof(InventoryService));
                _owner.ZoneEquipment = new EquipmentService(inventory, _owner.ZoneCharacterStats, items,
                    new EnhancementBonusProvider(enhancement), enhancement);
                Observe(nameof(EquipmentService));
                return true;
            }

            // Enemy AI Story 013: the mob definition registry from the override or the S2 loader (IZoneMobData). No registry is a
            // startup failure, like a missing item database: the store, navigation speeds and Enemy AI all need it.
            private bool BuildMobRegistry()
            {
                IMobDefinitionRegistry registry = _owner.MobRegistryOverride
                    ?? (_owner._staticDataLoader as IZoneMobData)?.LoadedMobRegistry;
                if (registry == null)
                {
                    Debug.LogError(NO_MOB_REGISTRY_ERROR);
                    return false;
                }

                _owner.MobRegistryInUse = registry;
                return true;
            }

            private void BuildRelay()
            {
                _allowlist = CrossZoneRelayAllowlist.CreateProduction();
                Observe(nameof(CrossZoneRelayAllowlist));
                var origin = new CrossZoneRelayOrigin();
                Observe(nameof(CrossZoneRelayOrigin));
                _inbox = new CrossZoneRelayInbox(_allowlist.LargestMaxBodyBytes);
                Observe(nameof(CrossZoneRelayInbox));
                _receiver = new CrossZoneRelayReceiver(_allowlist, _inbox, origin);
                Observe(nameof(CrossZoneRelayReceiver));
                _publisher = new NpgsqlRelayPublisher(_dataSource, _queue, origin);
                Observe(nameof(NpgsqlRelayPublisher));
            }

            private InboundRequestDispatcher BuildDispatcher()
            {
                var guardChain = new CrossCuttingRpcGuardChain();
                _owner.GuardChain = guardChain;
                Observe(nameof(CrossCuttingRpcGuardChain));
                _gate = new CharacterMutationGate();
                Observe(nameof(CharacterMutationGate));
                // One directory, shared by the dispatcher and the Zone Instancing wiring (Story 014).
                _directory = new ZoneConnectionCharacterDirectory();
                // Story 016: the recording activity sink exists before the dispatcher and is drained by the wiring; the session machine
                // is built here so the ghost source can read it.
                _activitySink = new RecordingConnectionActivitySink(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
                _sessionMachine = new ConnectionStateMachine();
                var dispatcher = new InboundRequestDispatcher(
                    guardChain, _gate, _directory, _activitySink,
                    _tickLoop, MessageRoutingRegistry.TryGetEntry, BuildConfiguration.IsDevelopmentBuild);
                Observe(nameof(InboundRequestDispatcher));
                return dispatcher;
            }

            private void BuildOutboundAndPipeline(InboundRequestDispatcher dispatcher)
            {
                IClientMessageSendSeam sendSeam = _owner._network.CreateSendSeam();
                var outbox = new ClientMessageOutbox(sendSeam);
                Observe(nameof(ClientMessageOutbox));
                // Movement Story 009: the snapshot builder is built inside the wiring, before Movement exists.
                // Story 015 (TD-100): with no override the inner source is a late-bound forwarder; the production
                // ZoneEntityStateSource is bound right after BuildAutoAttackCombat. Remark: the Movement-decorated source is
                // also Combat's position source, so a player whose name lookup is not yet true (AC-2) has no position for Combat either.
                var entityStateForwarder = _owner.EntityStateSourceOverride == null ? new ForwardingZoneEntityStateSource() : null;
                var stateSource = new MovementZoneEntityStateSource(_owner.EntityStateSourceOverride ?? entityStateForwarder);
                _owner.EntityStateSource = stateSource;
                // Skill System Story 011: ZoneEntryCompletion takes its responder once, before Skill exists.
                _owner.EntryResponder = new ForwardingZoneEntryResponder();
                ZoneInstancingWiring wiring = BuildZoneInstancing(dispatcher, outbox, stateSource);

                NavigationPhaseStep navigationStep = BuildMobStoreAndNavigation(wiring, out MobEventBus mobBus,
                    out MobRuntimeStore mobStore, out IMobDefinitionRegistry mobRegistry);
                StatusEffectsService statusEffects = BuildStatusEffects(wiring);
                MovementService movement = BuildMovement(dispatcher, wiring, stateSource, statusEffects);
                var ruBatch = new RUBatchOutboundStep(sendSeam, outbox, wiring.Pool.MaxPlayers);
                Observe(nameof(RUBatchOutboundStep));
                _owner.RUBatch = ruBatch;
                _owner.HitResultEmitter = new HitResultEmitter(wiring.Pool, ruBatch, _tickLoop);
                Observe(nameof(HitResultEmitter));

                EnemyAISystem enemyAI = BuildCombatAndEnemyAI(dispatcher, wiring, stateSource, statusEffects, movement,
                    outbox, ruBatch, mobBus, mobStore, mobRegistry, entityStateForwarder);
                SkillSystemService skill = BuildSkillSystem(dispatcher, wiring, stateSource, statusEffects, outbox, ruBatch);
                // Gateway Story 011: the link pump drains the inbox at step 1b and sends PlayerCount at the END of phase 3
                // (after the Skill cast step), so it sees every session-count change of the tick.
                var linkPump = new GatewayLinkPump(_owner._linkChannel, _owner._linkOutbox, wiring);

                DeathAndRespawnService deathAndRespawn = BuildDeathAndRespawn(wiring, outbox, statusEffects, movement);

                AddPhaseThreeSteps(navigationStep, statusEffects, movement, enemyAI, linkPump, deathAndRespawn);

                _owner._linkRegistration = new GatewayLinkRegistration(_owner._linkChannel, linkPump, _owner._gatewayLinkPort,
                    () =>
                    {
                        ZoneDefinition definition = _owner._definitionSlot?.Current;
                        return definition != null ? (byte)definition.TemplateId : (byte)0;
                    });
                // Once, after both builds (the pool and the entry completion each take one listener). Order: Movement
                // first on entry; Combat last on release so BeforeRelease still finds the player's slot.
                wiring.EntryCompletion.SetEntryListener(new CompositeZonePlayerEntryListener(
                    movement, new SkillSpawnEntryListener(skill, _owner.ZoneLeveling)));
                // Death and Respawn (Story 008): resets the slot's death state; before Combat, which stays last.
                IZoneSlotReleaseListener releaseListeners = deathAndRespawn == null
                    ? new CompositeZoneSlotReleaseListener(
                        statusEffects, movement, new SkillSlotReleaseListener(skill), _owner.AutoAttackCombat)
                    : new CompositeZoneSlotReleaseListener(
                        statusEffects, movement, new SkillSlotReleaseListener(skill), deathAndRespawn,
                        _owner.AutoAttackCombat);
                wiring.Pool.SetReleaseListener(releaseListeners);
                var cycleBroadcast = new CycleTimerBroadcastStep(_owner.AutoAttackCombat, wiring.Pool, sendSeam, outbox);
                Observe(nameof(CycleTimerBroadcastStep));
                _owner.CycleTimerBroadcast = cycleBroadcast;
                var positions = new PositionBroadcastStep(movement, wiring.Pool, sendSeam, outbox);
                Observe(nameof(PositionBroadcastStep));
                _owner.PositionBroadcast = positions;
                var outboundStep = new ZoneOutboundSteps(outbox, ruBatch, cycleBroadcast, positions);

                // Built for the game-system stories to take; this story registers no handler.
                _owner._broadcaster = new ServerWideBroadcaster(outbox, _publisher, _allowlist);
                Observe(nameof(ServerWideBroadcaster));
                var relayHandler = new ZoneClientRelayHandler(outbox);
                Observe(nameof(ZoneClientRelayHandler));
                var simulation = new ZoneSimulationStep(_owner._simulationSteps);
                Observe(nameof(ZoneSimulationStep));
                var pipeline = new ZoneTickPipeline(_queue, _inbox, relayHandler, linkPump, dispatcher, simulation, outboundStep, _tickLoop);
                Observe(nameof(ZoneTickPipeline));

                dispatcher.Seal();
                Observe("Seal");
                _tickLoop.RegisterTickDriven(pipeline.Tick);
                Observe("RegisterTickDriven");
                IZoneInboundTransport transport = _owner._network.CreateInboundTransport(dispatcher, dispatcher,
                    // Gateway Story 012: outbox first, then admission (drops pending loads), then the handshake intake.
                    new ClientConnectionListenerFanOut(outbox, wiring.Admission, wiring.HandshakeIntake));
                Observe("CreateInboundTransport");

                // The NGO shell implements the disconnect seam but only exists after the dispatcher is sealed.
                _disconnector.Target = (transport as NgoZoneInboundTransport)?.Shell;

                _owner.AttachOwnedObjects(_queue, outbox, null, transport, _owner._currentTickReader, null);
                _owner.AttachTickLoop(_tickLoop);
            }

            // Enemy AI Story 013: the zone's one MobEventBus is always built (also with a navigation seam), then the
            // runtime store, which must subscribe before navigation (S6) and Enemy AI so mobs are seeded first; then the
            // navigation call. Returns the navigation phase step (null with a passed seam).
            private NavigationPhaseStep BuildMobStoreAndNavigation(ZoneInstancingWiring wiring, out MobEventBus mobBus,
                out MobRuntimeStore mobStore, out IMobDefinitionRegistry mobRegistry)
            {
                mobBus = new MobEventBus();
                _owner.MobEvents = mobBus;
                // Mob Spawning Story 006: chosen once, read by Enemy AI (S5) and the spawner (S6).
                _owner._enemyAIConfig = EnemyAIConfigValues.Defaults;
                mobRegistry = _owner.MobRegistryInUse;
                mobStore = new MobRuntimeStore(_owner._zoneId, mobBus, mobRegistry, _owner.RandomProvider,
                    new CharacterStatsMobStatWriter(_owner.ZoneCharacterStats), wiring.Pool.MaxMobs);
                _owner.MobStore = mobStore;
                _owner.RegisterDisposable(mobStore);

                return BuildNavigation(mobBus, mobStore, mobRegistry);
            }

            // Builds the zone's one DamageCalculator (player autos, skills and mob attacks; Enemy AI Story 012 / 013), then
            // Combat, then Enemy AI, then binds the production entity-state source with the mob-aware combat reader.
            // Construction order: Combat first (Enemy AI subscribes to its OnMobDied), then Enemy AI. This is NOT the
            // phase-3 step order, which AddPhaseThreeSteps lists. Returns Enemy AI (null with a passed navigation seam).
            private EnemyAISystem BuildCombatAndEnemyAI(InboundRequestDispatcher dispatcher, ZoneInstancingWiring wiring,
                MovementZoneEntityStateSource stateSource, StatusEffectsService statusEffects, MovementService movement,
                ClientMessageOutbox outbox, RUBatchOutboundStep ruBatch, MobEventBus mobBus, MobRuntimeStore mobStore,
                IMobDefinitionRegistry mobRegistry, ForwardingZoneEntityStateSource entityStateForwarder)
            {
                var damageCalculator = new DamageCalculator(_owner.ZoneCharacterStats, _owner.ZoneEquipment,
                    _owner.ItemDatabaseInUse, new EnhancementBonusProvider(EnhancementConfig.Default),
                    DamageCalculationConfig.Default, _owner.RandomProvider);
                _owner._damageCalculator = damageCalculator;
                var autoAttackAdapter = new DamageCalculatorAutoAttackAdapter(damageCalculator);

                BuildAutoAttackCombat(dispatcher, wiring, stateSource, statusEffects, movement, outbox, ruBatch,
                    mobStore, autoAttackAdapter);
                EnemyAISystem enemyAI = BuildEnemyAI(wiring, movement, outbox, ruBatch, mobBus, mobStore, mobRegistry,
                    autoAttackAdapter);
                IZoneEntityCombatReader combatReader = new AutoAttackZoneEntityCombatReader(_owner.AutoAttackCombat);
                if (enemyAI != null)
                {
                    combatReader = new MobAwareZoneEntityCombatReader(enemyAI, combatReader);
                }

                entityStateForwarder?.Bind(new ZoneEntityStateSource(wiring.Pool, wiring.MobRoster,
                    new CharacterStatsZoneEntityStatReader(_owner.ZoneCharacterStats), _owner.PersistenceService,
                    _owner.ZoneEquipment, combatReader, new MobRuntimeStoreTransformReader(mobStore), _deathReader));
                return enemyAI;
            }

            // The phase-3 order, listed in ONE place (ADR-014, CR-AI-2; construction order is not step order):
            // ZoneInstancingWiring (added in BuildZoneInstancing), NavigationPhaseStep (phases 1-2), Mob Spawning
            // (MobSpawningPhaseStep, Mob Spawning Story 006), Status Effects,
            // Movement, Enemy AI, Auto-Attack Combat, Skill cast, Gateway link pump, then Enemy AI's
            // MobPositionIntegrationStep (ADR-002 phase 4, last). Navigation step and Enemy AI exist only when the
            // composition built the navigation itself (no INavigationProvider with a passed seam).
            private void AddPhaseThreeSteps(NavigationPhaseStep navigationStep, StatusEffectsService statusEffects,
                MovementService movement, EnemyAISystem enemyAI, GatewayLinkPump linkPump,
                DeathAndRespawnService deathAndRespawn)
            {
                List<IZoneSimulationStep> steps = _owner._simulationSteps;
                if (navigationStep != null)
                {
                    steps.Add(navigationStep);
                }

                // Mob Spawning Story 006: the first game-system step, after navigation phases 1-2 (ADR-002) and before
                // Enemy AI, so a mob spawned on tick T has its store entry and controller before Enemy AI's Run on T
                // (MobSpawned subscribers: store, then Enemy AI, then navigation; ADR-010). Listed in every composition;
                // it ticks nothing until S6 sets a spawner.
                var spawningStep = new MobSpawningPhaseStep();
                _owner.MobSpawningStep = spawningStep;
                steps.Add(spawningStep);
                steps.Add(statusEffects);
                // Death and Respawn (Story 008, Decision 3): after Status Effects and before Movement, so a player
                // respawned on tick T is teleported and free to move on T, and targetable by Enemy AI and Auto-Attack
                // on T. Listed only when the service was built (it needs a town respawn point provider).
                if (deathAndRespawn != null)
                {
                    var deathStep = new DeathAndRespawnPhaseStep(deathAndRespawn);
                    _owner.DeathAndRespawnStep = deathStep;
                    steps.Add(deathStep);
                }

                steps.Add(movement);
                if (enemyAI != null)
                {
                    steps.Add(enemyAI);
                }

                steps.Add(_owner.AutoAttackCombat);
                steps.Add(_owner.SkillStep);
                steps.Add(linkPump);
                if (enemyAI != null)
                {
                    steps.Add(enemyAI.IntegrationStep);
                }
            }

            // Death and Respawn Story 008: one service per zone, built after Status Effects, Movement and Combat
            // exist (and the wiring with its SessionEnds). Returns null, building nothing, with one warning, when
            // there is no town respawn point provider (no definition slot and no RespawnProviderOverride: test
            // compositions); the forwarder then stays unbound and nobody is dead. An invalid config override makes
            // the constructor throw ArgumentException (the validation logs the reason); the S5 step wrapper then
            // logs "startup failed at S5".
            private DeathAndRespawnService BuildDeathAndRespawn(ZoneInstancingWiring wiring, ClientMessageOutbox outbox,
                StatusEffectsService statusEffects, MovementService movement)
            {
                _owner.DeathStateReader = _deathReader;
                if (_respawnProvider == null)
                {
                    Debug.LogWarning("[ZoneServer] Death and Respawn not built: no town respawn point provider; "
                        + "nobody is ever dead in this zone.");
                    return null;
                }

                ZoneStats stats = _owner.ZoneCharacterStats;
                DeathAndRespawnConfigValues config = _owner.DeathAndRespawnConfigOverride == null
                    ? DeathAndRespawnConfigValues.Defaults
                    : DeathAndRespawnConfigValues.FromAsset(_owner.DeathAndRespawnConfigOverride);
                var service = new DeathAndRespawnService(config, wiring.Pool, new CharacterStatsDeathSource(stats),
                    new ServerTickDeathTickSource(_tickLoop), _owner.AutoAttackCombat, movement,
                    new NoOpPartyMemberStatusSink(), new ZoneSlotGhostStatusSource(wiring.Pool),
                    new SessionEndGhostDeathSink(wiring.Pool, wiring.SessionEnds), _respawnProvider,
                    new CharacterStatsRespawnVitalsWriter(stats), movement, statusEffects,
                    new DeathMessageEmitter(wiring.Pool, outbox));
                Observe(nameof(DeathAndRespawnService));
                _deathReader.Bind(service);
                _owner.RegisterDisposable(service);
                _owner.DeathAndRespawn = service;
                return service;
            }

            // Zone Instancing (Story 014): built after the outbox exists and before the dispatcher is sealed. Its tick
            // step joins the phase 3 list; it is not registered with the tick loop (ADR-014).
            private ZoneInstancingWiring BuildZoneInstancing(InboundRequestDispatcher dispatcher, ClientMessageOutbox outbox,
                IZoneEntityStateSource entityState)
            {
                // Gateway Story 011: the link client is created here (no thread until S10); the real outbox encodes onto it.
                ZoneServerConfig linkConfig = _owner._config;
                if (linkConfig == null)
                {
                    // Only reachable when S5 runs without S0 (EditMode rigs); a real startup always has the config.
                    Debug.LogWarning("[ZoneServer] S5: no server config loaded; the gateway link uses the default settings.");
                    linkConfig = new ZoneServerConfig();
                }

                Func<ZoneServerConfig, IGatewayLinkChannel> channelFactory = _owner.GatewayLinkChannelFactoryOverride;
                IGatewayLinkChannel channel = channelFactory != null ? channelFactory(linkConfig) : GatewayLinkClient.FromConfig(linkConfig);
                _owner._linkChannel = channel;
                var linkOutbox = new GatewayLinkOutbox(channel);
                _owner._linkOutbox = linkOutbox;
                _owner._gatewayLinkPort = linkConfig.GatewayZoneLinkPort;
                var wiring = new ZoneInstancingWiring(_owner._zoneId, _tickLoop, _queue, dispatcher, outbox,
                    _owner.PersistenceService, _owner.PersistenceService, _owner.PersistenceService, _gate,
                    _owner.ZoneInventory, _disconnector, _owner.RequestTeardown,
                    new ZoneInstancingWiringOptions
                    {
                        Directory = _directory,
                        Sessions = _sessionMachine,
                        ActivitySink = _activitySink,
                        Ghosts = new SessionMachineGhostSource(_sessionMachine, ZoneSlotPool.MAX_PLAYERS_PER_ZONE),
                        // Death and Respawn Story 008: forwarder (bound at S5 once the service exists), the no-op HP
                        // source (no production pre-disconnect HP source yet) and the MaxHP fallback of a ghost save.
                        DeathStateReader = _deathReader,
                        PreDisconnectHp = new NoOpPreDisconnectHpSource(),
                        EffectiveMaxHp = entity => _owner.ZoneCharacterStats.GetEffectiveStat(
                            entity, IronGrind.CharacterStats.StatID.MaxHP),
                        ConnectingTimeoutSeconds = _owner.EffectiveConnectingTimeoutSeconds,
                        LogTransitions = _owner.LogTeardownSteps,
                        EntityState = entityState,
                        Responder = _owner.EntryResponder,
                        // Stories 018 / 019: the same Equipment Persistence restores into, so session end unregisters what load registered.
                        Equipment = _owner.ZoneEquipment,
                        Validator = _owner.EntryTokenValidatorOverride,
                        Currency = _owner.ZoneCurrency,
                        GatewayOutbox = linkOutbox,
                        // Mob Spawning Story 006: the spawner exists only at S6; the forwarder reaches it from close step 1.
                        MobSpawnTimers = _owner.MobSpawnTimers = new ForwardingZoneMobSpawnTimers()
                    });
                Observe(nameof(ZoneInstancingWiring));
                _owner._simulationSteps.Add(wiring);
                _owner.AttachZoneInstancing(wiring);
                return wiring;
            }

            // Navigation Story 010 / Enemy AI Story 013: runs only when no IZoneNavigation was passed (the host passes null).
            // Builds the Story 009 Unity adapters (or the test overrides), the ZoneNavigationService and its phase step over
            // the zone's bus (built by the caller), the mob registry (speeds) and the runtime store (committed positions).
            // The service subscribes to the bus in Initialize (S6) and unsubscribes in Teardown (step 3).
            // A caller that passed a seam keeps the old behaviour: no service, no step, no Observe (the bus still exists).
            // Returns the phase step (listed by the caller in the phase-3 order), or null with a passed seam.
            private NavigationPhaseStep BuildNavigation(MobEventBus bus, MobRuntimeStore store, IMobDefinitionRegistry registry)
            {
                if (_owner._navigation != null)
                {
                    return null;
                }

                NavigationConfig config = _owner.NavigationConfigOverride;
                if (config == null)
                {
                    // CreateInstance runs the field initializers, so the asset carries the in-code defaults. Destroyed in the Dispose pass.
                    config = ScriptableObject.CreateInstance<NavigationConfig>();
                    _owner._ownedNavigationConfig = config;
                }

                _owner._navigationConfigInUse = config;

                INavAgentFactory agentFactory;
                if (_owner.NavAgentFactoryOverride != null)
                {
                    agentFactory = _owner.NavAgentFactoryOverride();
                }
                else
                {
                    // Kept as the concrete type so teardown step 3 can destroy its pool roots.
                    _owner._unityNavAgentFactory = new UnityNavAgentFactory();
                    agentFactory = _owner._unityNavAgentFactory;
                }

                INavMeshInstanceOps instanceOps = _owner.NavMeshInstanceOpsOverride != null
                    ? _owner.NavMeshInstanceOpsOverride()
                    : new UnityNavMeshInstanceOps();

                var service = new ZoneNavigationService(config, agentFactory,
                    new MobDefinitionRegistryNavSpeedSource(registry),
                    new UnityNavMeshQuery(), instanceOps, bus);
                Observe(nameof(ZoneNavigationService));
                _owner._returnSpeedCap = NavigationConfigValues.FromAsset(config).ReturnSpeedCap;

                // ADR-002 Decision 1 and CR-AI-2: navigation phases 1 and 2 run before every phase-3 system (the caller
                // adds this step first); mob committed positions come from the runtime store.
                _owner._navigationService = service;
                _owner._navigation = service;
                return new NavigationPhaseStep(service, store);
            }

            // Enemy AI Story 013: the Enemy AI system, built AFTER Combat (it subscribes to OnMobDied) and Movement. Built
            // only when the composition built the navigation itself: with a passed seam there is no INavigationProvider, so
            // no Enemy AI and no integration step (store and combat seams still exist). Returns null in that case.
            private EnemyAISystem BuildEnemyAI(ZoneInstancingWiring wiring, MovementService movement,
                ClientMessageOutbox outbox, RUBatchOutboundStep ruBatch, MobEventBus bus, MobRuntimeStore store,
                IMobDefinitionRegistry registry, IAutoAttackDamageCalculator calculator)
            {
                if (_owner._navigationService == null)
                {
                    return null;
                }

                ZoneStats stats = _owner.ZoneCharacterStats;
                var resolver = new MobAttackResolver(new HitDetector(HitDetectionConfig.Default), _owner.HitResultEmitter,
                    calculator, stats, new AutoAttackDamageOutputEmitter(wiring.Pool, _owner._zoneId, outbox, ruBatch));
                var candidates = new ZoneAggroCandidateSource(_owner._zoneId, wiring.Pool,
                    new MovementPlayerPositionReader(movement), _deathReader);
                var enemyAI = new EnemyAISystem(_owner._zoneId, bus, store, registry, _owner._navigationService,
                    new CharacterStatsZoneEntityStatReader(stats), resolver, _owner._enemyAIConfig,
                    _owner._returnSpeedCap, candidates, _owner.AutoAttackCombat, new StandInLootTableService(),
                    wiring.Pool.MaxMobs);
                _owner.RegisterDisposable(enemyAI);
                _owner.EnemyAI = enemyAI;
                return enemyAI;
            }

            // Status Effects Story 009: the Status Effects service over the zone's Character Stats (built in
            // BuildCharacterStats, Story 018). The service joins phase 3 after Zone Instancing, listens for slot releases
            // and is disposed in the teardown Dispose pass (ADR-010).
            private StatusEffectsService BuildStatusEffects(ZoneInstancingWiring wiring)
            {
                ZoneStats stats = _owner.ZoneCharacterStats;
                var statusEffects = new StatusEffectsService(new CharacterStatsBuffModifierLayer(stats),
                    new CharacterStatsResourceSink(stats), new ZoneSlotPoolEntityIndex(wiring.Pool),
                    new CharacterStatsDeathSource(stats));
                Observe(nameof(StatusEffectsService));
                // The pool's release listener is set in BuildOutboundAndPipeline (composite: Status Effects, Movement, Skill, Combat).
                _owner.RegisterDisposable(statusEffects);
                _owner.StatusEffects = statusEffects;
                // Listed in the phase-3 order by BuildOutboundAndPipeline (ADR-014).
                return statusEffects;
            }

            // Movement Story 009: intent buffer and inbound registration (before Seal), the service after Status Effects
            // in phase 3 (integration last, ADR-002), and the three Story 008 hooks (snapshot, entry, release).
            private MovementService BuildMovement(InboundRequestDispatcher dispatcher, ZoneInstancingWiring wiring,
                MovementZoneEntityStateSource stateSource, StatusEffectsService statusEffects)
            {
                var slotIndex = new ZoneSlotPoolEntityIndex(wiring.Pool);
                var buffer = new MovementIntentBuffer(_tickLoop, slotIndex, MovementTuningValues.Defaults);
                Observe(nameof(MovementIntentBuffer));
                MovementInboundRegistration.Register(dispatcher, buffer);

                ITownRespawnPointProvider respawn = _owner.RespawnProviderOverride;
                if (respawn == null && _owner._definitionSlot != null)
                {
                    respawn = new ZoneDefinitionSlotRespawnPointProvider(_owner._zoneId, _owner._definitionSlot);
                }

                _respawnProvider = respawn;
                MovementService movement;
                if (respawn != null)
                {
                    movement = new MovementService(buffer, new CharacterStatsMovementSpeedSource(_owner.ZoneCharacterStats),
                        new UnityNavMeshSampler(), slotIndex, MovementTuningValues.Defaults,
                        new LoggingMovementGeometryErrorSink(), _owner._zoneId, respawn);
                }
                else
                {
                    // No respawn provider (test compositions): Movement keeps its null-provider branch.
                    movement = new MovementService(buffer, new CharacterStatsMovementSpeedSource(_owner.ZoneCharacterStats),
                        new UnityNavMeshSampler(), slotIndex, MovementTuningValues.Defaults,
                        new LoggingMovementGeometryErrorSink());
                }

                Observe(nameof(MovementService));
                stateSource.AttachMovement(movement);
                // The entry listener (Movement, then Skill) is set once in BuildOutboundAndPipeline after both builds.
                // The pool's release listener is set in BuildOutboundAndPipeline (composite: Status Effects, Movement, Skill, Combat).
                _owner.Movement = movement;
                // Listed in the phase-3 order by BuildOutboundAndPipeline (ADR-014).
                return movement;
            }

            // Auto-Attack Combat Story 006: the Combat step runs in phase 3 after Movement and Enemy AI (the fire check sees this
            // tick's positions; the order is listed by BuildOutboundAndPipeline); SetTarget and SetAutoAttack register before
            // Seal and apply in the dispatch step (EC-8); the slot release listener is set in BuildOutboundAndPipeline, once,
            // with Combat last so BeforeRelease still finds the slot.
            // Enemy AI Story 013: the mob seams are the real ones (MobTargetValidity, the one DamageCalculator through its
            // adapter, CharacterStatsAutoAttackDamageApplier, MobRuntimeStoreXpAward, ZoneMobRosterMobTypeSource).
            // Story 007: the damage applier is wrapped so each applied hit also sends SelfDamageEvent (R-OD, attacker) and
            // DamageEvent (R-U, other zone clients).
            private void BuildAutoAttackCombat(InboundRequestDispatcher dispatcher, ZoneInstancingWiring wiring,
                MovementZoneEntityStateSource stateSource, StatusEffectsService statusEffects, MovementService movement,
                ClientMessageOutbox outbox, RUBatchOutboundStep ruBatch, MobRuntimeStore mobStore,
                IAutoAttackDamageCalculator calculator)
            {
                ZoneStats stats = _owner.ZoneCharacterStats;
                var innerApplier = new CharacterStatsAutoAttackDamageApplier(stats);
                _owner.AutoAttackInnerDamageApplier = innerApplier;
                var damageApplier = new DamageOutputApplier(innerApplier,
                    new AutoAttackDamageOutputEmitter(wiring.Pool, _owner._zoneId, outbox, ruBatch));
                var faceEmitter = new AutoFaceEmitter(wiring.Pool, _owner._zoneId, movement, ruBatch);
                _owner.AutoAttackFaceOutput = faceEmitter;
                var combat = new AutoAttackCombatSystem(
                    wiring.Pool,
                    new MobTargetValidity(wiring.Pool, mobStore, stats),
                    new CharacterStatsAttackSpeedSource(stats),
                    stateSource,
                    new HitDetector(HitDetectionConfig.Default),
                    _owner.HitResultEmitter,
                    new CharacterStatsAutoAttackStatSource(stats),
                    calculator,
                    damageApplier,
                    new MobRuntimeStoreXpAward(mobStore),
                    new CharacterStatsExperienceSink(stats),
                    new ZoneMobRosterMobTypeSource(wiring.MobRoster),
                    _owner._zoneId,
                    faceEmitter);
                Observe(nameof(AutoAttackCombatSystem));

                // RFR-3a: any entity present in the zone (players and mobs) is a valid target-slot entry; combat
                // still rejects player targets via its own validity check (Rule 1).
                var tracker = new TargetSlotTracker();
                var setTarget = new SetTargetRequestHandler(tracker,
                    new ZoneEntityIdView(wiring.Pool.GetPlayerEntityIDsInZone(_owner._zoneId),
                        wiring.Pool.GetMobEntityIDsInZone(_owner._zoneId)), combat);
                _owner.TargetSlotTracker = tracker;
                _owner.AutoAttackDamageApplier = damageApplier;
                NetworkingCoreInboundRegistration.RegisterCombatInput(dispatcher, setTarget, new SetAutoAttackRequestHandler(combat));
                // Story 016: heartbeats are connection-level, no-op sink; the dispatcher already reported them as activity.
                NetworkingCoreInboundRegistration.RegisterHeartbeat(dispatcher, new NoOpHeartbeatSink());

                _owner.AutoAttackCombat = combat;
                // Listed in the phase-3 order by BuildOutboundAndPipeline (ADR-014).
            }

            // Skill System Story 011: builds the whole Skill System graph and joins phase 3 after Combat (epic
            // Decision 3, EC-SK-1); SkillCastRequest is registered before Seal and stored in dispatch.
            // Decisions (user, 2026-11-04):
            //  - Class registry: _owner.ClassRegistryOverride, else the S2 loader's (Story 013), shared with Leveling
            //    (BuildStatusEffects); an empty registry when neither (silent: composition tests fail on any unexpected log).
            //  - Damage: SkillDamageCalculatorOverride, else DamageCalculatorSkillAdapter over the zone's one DamageCalculator
            //    (Enemy AI Story 013); XP from the mob runtime store.
            //  - Roster / config: _owner.SkillDatabaseOverride (else the S2 loader's) and _owner.SkillSystemConfigOverride; the fallbacks are an
            //    empty in-code SkillDatabase (silent, destroyed in the Dispose pass) and SkillSystemConfigValues.Default.
            //  - Decision B: the damage step takes the INNER applier (CharacterStatsAutoAttackDamageApplier), not DamageOutputApplier, so a skill hit
            //    sends no DamageEvent / SelfDamageEvent.
            // The entry and release listeners are set once in BuildOutboundAndPipeline (Skill joins both composites); the
            // cooldown snapshot responder is bound to the forwarding responder here.
            private SkillSystemService BuildSkillSystem(InboundRequestDispatcher dispatcher, ZoneInstancingWiring wiring,
                MovementZoneEntityStateSource stateSource, StatusEffectsService statusEffects, ClientMessageOutbox outbox,
                RUBatchOutboundStep ruBatch)
            {
                ZoneStats stats = _owner.ZoneCharacterStats;
                ZoneSlotPool pool = wiring.Pool;

                // Story 013: the override, then the S2 loader's roster (owned by the scene, never destroyed here),
                // then an empty in-code database (stand-in loader: tests only). Unity's == null also treats a destroyed
                // asset as missing, so a destroyed roster would fall back silently; the scene owns it, so it is not
                // destroyed while the zone runs.
                SkillDatabase database = _owner.SkillDatabaseOverride;
                if (database == null)
                {
                    database = (_owner._staticDataLoader as IZoneClassSkillData)?.LoadedSkillDatabase;
                }

                SkillDatabase ownedDatabase = null;
                if (database == null)
                {
                    ownedDatabase = ScriptableObject.CreateInstance<SkillDatabase>();
                    database = ownedDatabase;
                }

                SkillSystemConfigValues configValues = _owner.SkillSystemConfigOverride != null
                    ? SkillSystemConfigValues.FromAsset(_owner.SkillSystemConfigOverride)
                    : SkillSystemConfigValues.Default;
                var configSource = new StaticSkillSystemConfigSource(configValues);

                var slotIndex = new ZoneSlotPoolEntityIndex(pool);
                var unlockOutput = new SkillUnlockOutputEmitter(pool, outbox);
                var service = new SkillSystemService(_owner.ClassRegistryInUse, database,
                    new CharacterStatsSkillLevelSource(stats), slotIndex, unlockOutput, pool.MaxPlayers);
                Observe(nameof(SkillSystemService));

                var intake = new SkillCastIntake(slotIndex, pool.MaxPlayers);
                var party = new ZoneSkillCastPartySource(pool, new NoPartyZonePresence());
                var output = new SkillCastOutputEmitter(pool, _owner._zoneId, _owner.TargetSlotTracker, party, ruBatch, service);

                BuildSkillCastPipeline(wiring, stateSource, statusEffects, service, database, configSource, output,
                    intake, out SkillCastExecutor executor, out SkillCastStep castStep);

                var handler = new SkillCastRequestHandler(intake, _tickLoop, configValues.CastStaleTickTolerance);
                NetworkingCoreInboundRegistration.RegisterSkillInput(dispatcher, handler);

                service.Subscribe(_owner.ZoneLeveling);
                _owner.RegisterDisposable(new SkillSystemTeardown(service, _owner.ZoneLeveling, ownedDatabase));

                _owner.EntryResponder.Target = new SkillCooldownSnapshotResponder(new LoggingZoneEntryResponder(), service, outbox);

                _owner.SkillService = service;
                _owner.SkillIntake = intake;
                _owner.SkillStep = castStep;
                _owner.SkillExecutor = executor;
                // The cast step is listed in the phase-3 order by BuildOutboundAndPipeline (ADR-014).
                return service;
            }

            // Skill System Story 011: the damage and effect steps, the executor and the validation step.
            private void BuildSkillCastPipeline(ZoneInstancingWiring wiring, MovementZoneEntityStateSource stateSource,
                StatusEffectsService statusEffects, SkillSystemService service, SkillDatabase database,
                ISkillSystemConfigSource configSource, SkillCastOutputEmitter output, SkillCastIntake intake,
                out SkillCastExecutor executor, out SkillCastStep castStep)
            {
                ZoneStats stats = _owner.ZoneCharacterStats;
                AutoAttackCombatSystem combat = _owner.AutoAttackCombat;
                var statSource = new CharacterStatsSkillStatSource(stats);
                var positions = new ZoneSkillPositionSource(stateSource);
                var damageStep = new SkillDamageExecutionStep(
                    combat,
                    statSource,
                    positions,
                    _owner.SkillDamageCalculatorOverride ?? new DamageCalculatorSkillAdapter(_owner._damageCalculator),
                    new MobRuntimeStoreXpAward(_owner.MobStore),
                    new CharacterStatsExperienceSink(stats),
                    _owner.AutoAttackInnerDamageApplier,
                    _owner.AutoAttackFaceOutput,
                    combat);
                var effectStep = new SkillEffectExecutionStep(new StatusEffectsSkillEffectSink(statusEffects), statSource);
                executor = new SkillCastExecutor(new CharacterStatsSkillManaSink(stats), configSource, service, output,
                    new CompositeSkillExecutionStep(damageStep, effectStep));
                castStep = new SkillCastStep(intake, service, database, new CharacterStatsSkillCasterAliveSource(stats),
                    new NoSilenceSource(), new ZoneSkillTargetSource(wiring.Pool, stateSource), positions, output, executor);
            }

            public bool StartListener()
            {
                try
                {
                    IStartableZoneRelayListener listener;
                    Func<string, CrossZoneRelayReceiver, IStartableZoneRelayListener> factory = _owner.ListenerFactoryOverride;
                    if (factory != null)
                    {
                        listener = factory(_connectionString, _receiver);
                    }
                    else
                    {
                        listener = new NpgsqlRelayListener(_connectionString, _receiver);
                    }

                    // Attached before Start(), so teardown step 2a stops it even when Start() throws.
                    _owner.AttachListener(listener);
                    listener.Start();
                    return true;
                }
                finally
                {
                    _connectionString = null;
                }
            }
        }
    }
}
