using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Source of the disconnected-session (ghost) accounts whose session TTL the wiring evaluates every tick
    /// (Zone Instancing Story 014). The session epic supplies the real enumeration; the composition default is
    /// <see cref="LoggingZoneGhostSessionSource"/>, which is empty. Tick thread only; implementations must not allocate.
    /// </summary>
    internal interface IZoneGhostSessionSource
    {
        /// <summary>Number of ghost accounts to evaluate this tick.</summary>
        int Count { get; }

        /// <summary>Reads one ghost account.</summary>
        /// <param name="index">In [0, <see cref="Count"/>).</param>
        /// <param name="accountId">The ghost's account.</param>
        /// <param name="sessionExpiryTick">The absolute tick at which its session TTL expires.</param>
        void Get(int index, out uint accountId, out uint sessionExpiryTick);
    }

    /// <summary>Stand-in <see cref="IZoneGhostSessionSource"/>: no ghosts, logs once that TTL enumeration is not implemented.</summary>
    internal sealed class LoggingZoneGhostSessionSource : IZoneGhostSessionSource
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("ghost session TTL enumeration");

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                _log.Log();
                return 0;
            }
        }

        /// <inheritdoc/>
        public void Get(int index, out uint accountId, out uint sessionExpiryTick)
        {
            accountId = 0u;
            sessionExpiryTick = 0u;
        }
    }

    /// <summary>
    /// Optional constructor inputs of <see cref="ZoneInstancingWiring"/>. Every field defaults to the production
    /// value or stand-in; tests override what they observe. Knobs stay <c>public const</c> defaults of their owners
    /// and are passed through here (Story 040 pattern, no config file entry).
    /// </summary>
    internal sealed class ZoneInstancingWiringOptions
    {
        /// <summary><c>ZONE_IDLE_CLOSE_SECONDS</c>.</summary>
        public int IdleCloseSeconds = ZoneLifecycle.ZONE_IDLE_CLOSE_SECONDS;

        /// <summary><c>ZONE_DRAIN_GRACE_SECONDS</c>.</summary>
        public int DrainGraceSeconds = ZoneLifecycle.ZONE_DRAIN_GRACE_SECONDS;

        /// <summary><c>ZONE_LOAD_TIMEOUT_SECONDS</c>.</summary>
        public int LoadTimeoutSeconds = ZoneLifecycle.ZONE_LOAD_TIMEOUT_SECONDS;

        /// <summary>Server ticks per second.</summary>
        public int TickRateHz = ServerTickLoop.TICK_RATE_HZ;

        /// <summary><c>MAX_PLAYERS_PER_ZONE</c>.</summary>
        public int MaxPlayers = ZoneSlotPool.MAX_PLAYERS_PER_ZONE;

        /// <summary><c>MAX_MOBS_PER_ZONE</c>.</summary>
        public int MaxMobs = ZoneSlotPool.MAX_MOBS_PER_ZONE;

        /// <summary>First EntityID issued; tests pass <see cref="uint.MaxValue"/> to reach EC-ZI-12.</summary>
        public uint FirstEntityId = 1u;

        /// <summary>
        /// The connection-to-character directory. The dispatcher needs one at its own construction (before the wiring
        /// exists), so the composition creates it first and passes the same instance to both. Null: the wiring creates one.
        /// </summary>
        public ZoneConnectionCharacterDirectory Directory;

        /// <summary>The session state machine. Null: the wiring creates one (nothing else in <c>src/</c> constructs it yet).</summary>
        public ConnectionStateMachine Sessions;

        /// <summary>Token validator. Null: <see cref="RejectingZoneEntryTokenValidator"/>.</summary>
        public IZoneEntryTokenValidator Validator;

        /// <summary><c>SessionReady</c> sender. Null: <see cref="LoggingZoneEntryResponder"/>.</summary>
        public IZoneEntryResponder Responder;

        /// <summary>Party seam. Null: <see cref="NoPartyZonePresence"/>.</summary>
        public IPartyZonePresence Party;

        /// <summary>Mob spawn timers. Null: <see cref="LoggingZoneMobSpawnTimers"/>.</summary>
        public IZoneMobSpawnTimers MobSpawnTimers;

        /// <summary>Reclaim session sink (TD-077). Null: <see cref="LoggingZoneEntryReclaimSessionSink"/>.</summary>
        public IZoneEntryReclaimSessionSink ReclaimSessionSink;

        /// <summary>Teardown session sink (TD-079). Null: <see cref="LoggingZoneTeardownSessionSink"/>.</summary>
        public IZoneTeardownSessionSink TeardownSessionSink;

        /// <summary>Entity state read seam. Null: <see cref="LoggingZoneEntityStateSource"/>.</summary>
        public IZoneEntityStateSource EntityState;

        /// <summary>Ghost TTL enumeration. Null: <see cref="LoggingZoneGhostSessionSource"/> (empty).</summary>
        public IZoneGhostSessionSource Ghosts;

        /// <summary>Leveling's <c>UnregisterPlayerEntity</c>. Null: no-op.</summary>
        public Action<EntityID> UnregisterPlayerEntity;

        /// <summary>
        /// When true, Editor and development builds log <c>[ZoneInstancingWiring] ZoneID(&lt;id&gt;) Active / Draining /
        /// Closed(&lt;transition&gt;) at tick &lt;n&gt;</c> (Story 014 verification evidence). Release builds never log these.
        /// The composition passes its <c>LogTeardownSteps</c>; off by default.
        /// </summary>
        public bool LogTransitions;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        /// <summary>Development-build lifecycle observer.</summary>
        public INetworkTestObserver Observer;
#endif
    }

    /// <summary>
    /// Constructs and owns the Zone Instancing systems of Stories 001-013 and connects them to the zone server
    /// composition (Zone Instancing Story 014, ADR-009 Amendment 1). Built once at S5 after the dispatcher exists and
    /// before it is sealed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Adapters.</b> This class is the zone-state owner (S9 <c>MarkActive</c> = T-2, S6 invalid bounds = T-3), the close
    /// steps (teardown step 4), the lifecycle listener, the slot pool listener, the drain notice log and the phase 3
    /// simulation step. Construction order: lifecycle and pool both take this object as their listener, so there is no
    /// cycle; the pool and lifecycle callbacks are no-ops until the lifecycle field is assigned.
    /// </para>
    /// <para>
    /// <b>Close cause.</b> <see cref="RequestFatal"/> and EntityID exhaustion record <see cref="ZoneExitCode.Fatal"/>
    /// before they call the lifecycle; a quit records nothing. <c>OnClosed</c> maps Drained / Idle to Clean, Emergency to
    /// Fatal when a fatal was recorded and Clean otherwise, and LoadFailed to no call (S-fail already requested
    /// StartupFailed). The listener catches and logs; it never throws.
    /// </para>
    /// <para>
    /// <b>Tick step.</b> <see cref="Run"/> applies the pending quits, activates the tick roster, ticks the entry
    /// reclamation, evaluates the session TTL of every ghost the injected source lists, ticks the lifecycle, and last
    /// serves the recorded snapshot requests. It allocates nothing in steady state.
    /// </para>
    /// <para>
    /// <b>Not wired here.</b> The <c>SessionHandshake</c> decode that calls <see cref="ZoneEntryAdmission.BeginEntry"/>, the
    /// <see cref="IClientConnectionListener"/> registration of <see cref="Admission"/>, the transport-disconnect and
    /// logout inputs of <see cref="SessionEnds"/> belong to the session epic.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var wiring = new ZoneInstancingWiring(zone, tickLoop, queue, dispatcher, outbox, persistence, persistence, persistence,
    ///     gate, inventory, disconnector, code =&gt; composition.RequestTeardown(code), options);
    /// simulationSteps.Add(wiring);           // phase 3
    /// dispatcher.Seal();
    /// // S9: wiring.MarkActive(zone);  quit hook: wiring.RequestQuit();  teardown step 4: wiring.RunCloseSteps();
    /// </code>
    /// </example>
    internal sealed class ZoneInstancingWiring : IZoneStateOwner, IZoneCloseSteps, IZoneLifecycleListener,
        IZoneSlotPoolListener, IZoneDrainNoticeLog, IZoneSimulationStep
    {
        private const string LOG_PREFIX = "[ZoneInstancingWiring]";

        private readonly ZoneID _zone;
        private readonly IServerTickSource _ticks;
        private readonly IClientMessageOutbox _outbox;
        private readonly Action<ZoneExitCode> _requestTeardown;
        private readonly IZoneGhostSessionSource _ghosts;
        private readonly ZoneLifecycle _lifecycle;
        private readonly ZoneSlotPool _pool;
        private readonly ZoneMobRoster _mobs;
        private readonly ZoneConnectionCharacterDirectory _directory;
        private readonly ZoneTickRoster _roster;
        private readonly ZoneEntryReclamation _reclamation;
        private readonly ZoneEntryAdmission _admission;
        private readonly ZoneEntryCompletion _completion;
        private readonly ZoneSnapshotBuilder _snapshotBuilder;
        private readonly ZoneSnapshotRequestService _snapshotRequests;
        private readonly ZoneCloseSteps _closeSteps;
        private readonly ZonePlayerExit _playerExit;
        private readonly ConnectionStateMachine _sessions;
        private readonly SessionEndCoordinator _sessionEnds;
        private readonly HashSet<uint> _notifiedAtDrain;
        private readonly byte[] _drainBody = new byte[ZoneSessionEnded.BodySize];
        private readonly int _drainBodySize;
        private readonly bool _logTransitions;
        private int _pendingQuits;
        private bool _fatalRecorded;

        /// <summary>Builds every Zone Instancing system for <paramref name="zone"/> and registers their dispatcher sinks.</summary>
        /// <param name="zone">The zone this process hosts (S0); not <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="ticks">The tick loop as <see cref="IServerTickSource"/>; the same source the pipeline uses.</param>
        /// <param name="queue">The tick completion queue.</param>
        /// <param name="dispatcher">The dispatcher, not yet sealed; the snapshot request and entry ack sinks are registered on it.</param>
        /// <param name="outbox">The client message outbox.</param>
        /// <param name="persistence">Character persistence.</param>
        /// <param name="sessionRelease">The persistence session-table release (normally the same service).</param>
        /// <param name="applier">The loaded-record applier (normally the same service).</param>
        /// <param name="gate">The character mutation gate shared with the dispatcher.</param>
        /// <param name="inventory">The Inventory service (<c>UnregisterCharacter</c> at session end).</param>
        /// <param name="disconnector">The transport close seam; the zone client id equals the transport client id.</param>
        /// <param name="requestTeardown">Called once when the zone closes (composition <c>RequestTeardown</c>).</param>
        /// <param name="options">Knobs and test seams; null for the production defaults.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ZoneInstancingWiring(ZoneID zone, IServerTickSource ticks, ITickCompletionQueue queue,
            IInboundRequestDispatcher dispatcher, IClientMessageOutbox outbox, ICharacterPersistence persistence,
            ICharacterSessionRelease sessionRelease, ILoadedRecordApplier applier, ICharacterMutationGate gate,
            IInventoryService inventory, IInboundTransportDisconnector disconnector, Action<ZoneExitCode> requestTeardown,
            ZoneInstancingWiringOptions options = null)
        {
            options = options ?? new ZoneInstancingWiringOptions();
            _zone = zone;
            _logTransitions = options.LogTransitions;
            _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _requestTeardown = requestTeardown ?? throw new ArgumentNullException(nameof(requestTeardown));
            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }

            if (persistence == null)
            {
                throw new ArgumentNullException(nameof(persistence));
            }

            IPartyZonePresence party = options.Party ?? new NoPartyZonePresence();
            IZoneEntityStateSource state = options.EntityState ?? new LoggingZoneEntityStateSource();
            _ghosts = options.Ghosts ?? new LoggingZoneGhostSessionSource();
            _directory = options.Directory ?? new ZoneConnectionCharacterDirectory();
            _sessions = options.Sessions ?? new ConnectionStateMachine();

            _lifecycle = new ZoneLifecycle(zone, this, 0u, options.IdleCloseSeconds, options.DrainGraceSeconds,
                options.LoadTimeoutSeconds, options.TickRateHz
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , options.Observer
#endif
                );
            _pool = new ZoneSlotPool(zone, ticks, this, options.MaxPlayers, options.MaxMobs, options.FirstEntityId);
            _mobs = new ZoneMobRoster(_pool, ticks);
            _roster = new ZoneTickRoster(_pool, ticks);
            _reclamation = new ZoneEntryReclamation(zone, _pool, _directory, _roster, party, outbox, disconnector,
                options.ReclaimSessionSink ?? new LoggingZoneEntryReclaimSessionSink(), ticks,
                tickRateHz: options.TickRateHz);
            ZoneEntryReclamation.Register(dispatcher, _reclamation);

            _snapshotBuilder = new ZoneSnapshotBuilder(zone, _pool, _mobs, state, ticks, outbox);
            _snapshotRequests = new ZoneSnapshotRequestService(zone, _pool, _lifecycle, _snapshotBuilder, outbox);
            ZoneSnapshotRequestService.Register(dispatcher, _snapshotRequests);

            _completion = new ZoneEntryCompletion(zone, _pool, _directory, _snapshotBuilder,
                options.Responder ?? new LoggingZoneEntryResponder(), party, state, outbox, _roster);
            _admission = new ZoneEntryAdmission(zone, _pool, _lifecycle, persistence, applier, queue,
                options.Validator ?? new RejectingZoneEntryTokenValidator(), outbox, _completion, _directory,
                reclamation: _reclamation);

            _notifiedAtDrain = new HashSet<uint>(options.MaxPlayers);
            _drainBodySize = ZoneSessionEndedCodec.WriteBody(_drainBody,
                new ZoneSessionEnded(zone, DisconnectReason.ZoneClosed, options.DrainGraceSeconds));

            _closeSteps = new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, persistence, queue, party,
                options.MobSpawnTimers ?? new LoggingZoneMobSpawnTimers(),
                options.TeardownSessionSink ?? new LoggingZoneTeardownSessionSink(), this, outbox, zone);
            _playerExit = new ZonePlayerExit(_pool, _reclamation);
            _sessionEnds = new SessionEndCoordinator(persistence, sessionRelease, queue, gate, _sessions, inventory,
                options.UnregisterPlayerEntity ?? (_ => { }), _ => { },
                (characterId, type) => _playerExit.ReleasePlayer(characterId, type));
        }

        /// <summary>The lifecycle state machine.</summary>
        public ZoneLifecycle Lifecycle => _lifecycle;

        /// <summary>The slot pool.</summary>
        public ZoneSlotPool Pool => _pool;

        /// <summary>The connection-to-character directory (the dispatcher's, when the composition passed it in).</summary>
        public ZoneConnectionCharacterDirectory Directory => _directory;

        /// <summary>The tick roster.</summary>
        public ZoneTickRoster TickRoster => _roster;

        /// <summary>The entry reclamation service.</summary>
        public ZoneEntryReclamation Reclamation => _reclamation;

        /// <summary>Admission; the session epic feeds it the decoded handshakes and registers it as a connection listener.</summary>
        public ZoneEntryAdmission Admission => _admission;

        /// <summary>The player exit service.</summary>
        public ZonePlayerExit PlayerExit => _playerExit;

        /// <summary>The session state machine (owned here until the session epic constructs it).</summary>
        public ConnectionStateMachine Sessions => _sessions;

        /// <summary>The session end coordinator: logout and TTL inputs come from the session epic.</summary>
        public SessionEndCoordinator SessionEnds => _sessionEnds;

        /// <summary>Number of quit requests recorded and not yet applied by a tick.</summary>
        public int PendingQuitCount => _pendingQuits;

        // ----- Inputs from the composition ----------------------------------------------------------

        /// <summary>Records a quit request (Active or Draining); the next <see cref="Run"/> applies it with that tick's number.</summary>
        public void RequestQuit()
        {
            _pendingQuits++;
        }

        /// <summary>Applies a quit at once. For Loading, where the server tick does not advance (T-6).</summary>
        /// <param name="tick">The tick number to apply it with.</param>
        public void RequestQuitNow(uint tick)
        {
            _lifecycle.RequestQuit(tick);
        }

        /// <summary>T-6 for a fatal error: records the Fatal cause, then closes the zone on <paramref name="tick"/>.</summary>
        /// <param name="tick">The current server tick.</param>
        public void RequestFatal(uint tick)
        {
            _fatalRecorded = true;
            _lifecycle.RequestFatal(tick);
        }

        /// <summary>The S6 wait: ticks the lifecycle while Loading so T-3 by timeout can fire.</summary>
        /// <param name="loadingTick">floor((clock - S6 start) x TICK_RATE_HZ).</param>
        public void OnLoadingTick(uint loadingTick)
        {
            _lifecycle.OnTick(loadingTick);
        }

        // ----- IZoneStateOwner ----------------------------------------------------------------------

        /// <inheritdoc/>
        public void MarkActive(ZoneID zone)
        {
            _lifecycle.MarkActive(_ticks.ServerTickNumber);
            LogTransition("Active", _ticks.ServerTickNumber);
        }

        /// <inheritdoc/>
        public void MarkClosedInvalidBounds(ZoneID zone)
        {
            _lifecycle.MarkInitFailed(_ticks.ServerTickNumber);
        }

        // ----- IZoneCloseSteps ----------------------------------------------------------------------

        /// <inheritdoc/>
        public void RunCloseSteps()
        {
            _closeSteps.RunCloseSteps();
        }

        // ----- IZoneSimulationStep ------------------------------------------------------------------

        /// <inheritdoc/>
        public void Run(uint currentTick, float deltaTime)
        {
            while (_pendingQuits > 0)
            {
                _pendingQuits--;
                _lifecycle.RequestQuit(currentTick);
            }

            _roster.ActivatePending(currentTick);
            _reclamation.OnTick(currentTick);

            int ghostCount = _ghosts.Count;
            for (int i = 0; i < ghostCount; i++)
            {
                _ghosts.Get(i, out uint accountId, out uint expiryTick);
                _sessionEnds.EvaluateSessionTTL(accountId, currentTick, expiryTick);
            }

            _lifecycle.OnTick(currentTick);
            _snapshotRequests.ServePending(currentTick);
        }

        // ----- IZoneLifecycleListener ---------------------------------------------------------------

        /// <inheritdoc/>
        public void OnDraining(uint serverTick)
        {
            try
            {
                LogTransition("Draining", serverTick);
                IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_zone);
                for (int i = 0; i < players.Count; i++)
                {
                    try
                    {
                        if (!_pool.TryGetPlayer(players[i], out ZonePlayerSlot slot) || slot.State != ZoneSessionState.Connected)
                        {
                            continue;
                        }

                        _outbox.Enqueue(slot.ClientId, ZoneSessionEnded.MessageTypeId,
                            new ReadOnlySpan<byte>(_drainBody, 0, _drainBodySize), isCapExempt: true);
                        _notifiedAtDrain.Add(slot.ClientId);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"{LOG_PREFIX} drain notice for {players[i]} failed ({ex.GetType().Name}); the next client runs.\n{ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} OnDraining failed ({ex.GetType().Name}).\n{ex}");
            }
        }

        /// <inheritdoc/>
        public void OnClosed(ZoneCloseTransition transition, uint serverTick)
        {
            try
            {
                LogTransition("Closed(" + transition + ")", serverTick);
                if (transition == ZoneCloseTransition.LoadFailed)
                {
                    return; // S-fail already requested StartupFailed
                }

                ZoneExitCode code = transition == ZoneCloseTransition.Emergency && _fatalRecorded
                    ? ZoneExitCode.Fatal
                    : ZoneExitCode.Clean;
                _requestTeardown(code);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} OnClosed({transition}) teardown request failed ({ex.GetType().Name}).\n{ex}");
            }
        }

        // Editor and development builds only, when LogTransitions is on (Story 014 verification evidence).
        private void LogTransition(string state, uint serverTick)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_logTransitions)
            {
                Debug.Log(LOG_PREFIX + " " + _zone + " " + state + " at tick " + serverTick);
            }
#endif
        }

        // ----- IZoneSlotPoolListener ----------------------------------------------------------------

        /// <inheritdoc/>
        public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick)
        {
            try
            {
                _lifecycle?.OnOccupiedPlayerSlotsChanged(occupied, serverTick);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} occupancy forward failed ({ex.GetType().Name}).\n{ex}");
            }
        }

        /// <inheritdoc/>
        public void OnEntityIDCounterExhausted(uint serverTick)
        {
            try
            {
                _fatalRecorded = true;
                _lifecycle?.RequestFatal(serverTick);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} EntityID exhaustion handling failed ({ex.GetType().Name}).\n{ex}");
            }
        }

        // ----- IZoneDrainNoticeLog ------------------------------------------------------------------

        /// <inheritdoc/>
        public bool WasNotifiedAtDrain(uint clientId)
        {
            return _notifiedAtDrain.Contains(clientId);
        }
    }
}
