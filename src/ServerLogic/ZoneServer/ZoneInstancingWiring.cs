using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Source of the disconnected-session (ghost) accounts whose session TTL the wiring evaluates every tick
    /// (Zone Instancing Story 014). The composition supplies <see cref="SessionMachineGhostSource"/> (Story 016); when none is passed (tests) the default is
    /// <see cref="LoggingZoneGhostSessionSource"/>, which is empty. Tick thread only; implementations must not allocate.
    /// </summary>
    internal interface IZoneGhostSessionSource
    {
        /// <summary>Re-reads the ghosts once per tick, before the TTL loop; <see cref="Count"/> and <see cref="Get"/> then read that snapshot.</summary>
        void Refresh();

        /// <summary>Number of ghost accounts to evaluate this tick.</summary>
        int Count { get; }

        /// <summary>Reads one ghost account.</summary>
        /// <param name="index">In [0, <see cref="Count"/>).</param>
        /// <param name="accountId">The ghost's account.</param>
        /// <param name="sessionExpiryTick">The absolute tick at which its session TTL expires.</param>
        void Get(int index, out uint accountId, out uint sessionExpiryTick);
    }

    /// <summary>Test and default stand-in <see cref="IZoneGhostSessionSource"/> used when no source is passed: no ghosts, logs once that TTL enumeration is not implemented. The composition does not use it.</summary>
    internal sealed class LoggingZoneGhostSessionSource : IZoneGhostSessionSource
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("ghost session TTL enumeration");

        /// <inheritdoc/>
        public void Refresh() { }

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

        /// <summary>The session state machine. The composition constructs it and passes it here (Story 016). Null: the wiring creates a new one.</summary>
        public ConnectionStateMachine Sessions;

        /// <summary>
        /// Token validator. Null: <see cref="GatewayZoneEntryTokenValidator"/> over the wiring's admitted entries and
        /// token store. <see cref="RejectingZoneEntryTokenValidator"/> stays available for tests that need it.
        /// </summary>
        public IZoneEntryTokenValidator Validator;

        /// <summary>The zone process's session token store holding the gateway's tokens. Null: the wiring creates one.</summary>
        public SessionTokenStore TokenStore;

        /// <summary>Zone-to-gateway link outbox. Null: <see cref="LoggingGatewayLinkOutbox"/>.</summary>
        public IGatewayLinkOutbox GatewayOutbox;

        /// <summary><c>REAUTH_FAILURE_LIMIT</c> for the gateway <c>Reconnect</c> handler (GDD default 3).</summary>
        public int ReauthFailureLimit = 3;

        /// <summary><c>CONNECTING_TIMEOUT_SECONDS</c>; ticks = seconds x <see cref="TickRateHz"/>.</summary>
        public int ConnectingTimeoutSeconds = AdmittedEntries.CONNECTING_TIMEOUT_SECONDS;

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

        /// <summary>
        /// The recording activity sink the dispatcher reports to (Story 016). The composition builds it before the
        /// dispatcher and passes the same instance here; <see cref="ZoneInstancingWiring.Run"/> drains it each tick.
        /// Null: no activity drain (tests that drive <c>RecordInboundActivity</c> directly).
        /// </summary>
        public RecordingConnectionActivitySink ActivitySink;

        /// <summary><c>HEARTBEAT_TIMEOUT_SECONDS</c>; ticks = seconds x <see cref="TickRateHz"/>.</summary>
        public int HeartbeatTimeoutSeconds = ConnectionStateMachine.HEARTBEAT_TIMEOUT_SECONDS;

        /// <summary>Currency service the token-path reconnect reads its post-reconciliation gold balance from (CR-NET-6.4). Null: a new empty <see cref="CurrencySystem"/> (tests).</summary>
        public ICurrencyService Currency;

        /// <summary>Leveling's <c>UnregisterPlayerEntity</c>. Null: no-op.</summary>
        public Action<EntityID> UnregisterPlayerEntity;

        /// <summary>
        /// The Equipment System, unregistered at session end (Equipment Story 008). Required: the wiring constructor throws
        /// <see cref="ArgumentNullException"/> when it is null (Character Persistence Story 019 removed the stand-in).
        /// </summary>
        public IronGrind.EquipmentSystem.IEquipmentService Equipment;

        /// <summary>
        /// <c>EquipmentClientRequestHandlers.ClearSession</c>, called at session end (Equipment Story 008). Null: no-op
        /// (the composition registers no <c>EquipRequest</c> handler yet).
        /// </summary>
        public Action<CharacterID> ClearEquipRequestStore;

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
    /// reclamation, evaluates the session TTL of every ghost the injected source lists, drops timed-out gateway
    /// admissions (<see cref="AdmittedEntries.OnTick"/>), ticks the lifecycle, and last
    /// serves the recorded snapshot requests. It allocates nothing in steady state.
    /// </para>
    /// <para>
    /// <b>Gateway link.</b> The composition's <c>GatewayLinkPump</c> (Gateway Story 011) drains the link inbox into
    /// <see cref="GatewayAdmit"/>, <see cref="GatewayEvict"/> and <see cref="GatewayReconnect"/> at tick start, and
    /// <see cref="ZoneInstancingWiringOptions.GatewayOutbox"/> carries the replies, <c>SessionReleased</c> and
    /// <c>ZoneDraining</c> (sent here when the zone leaves Active, before the reservations are released).
    /// </para>
    /// <para>
    /// <b>Client entry (Gateway Story 012).</b> <see cref="HandshakeIntake"/> is registered here as the connection-level sink
    /// of <c>SessionHandshake</c> (0xE136); <see cref="Run"/> drains it right after the entry reclamation, so each
    /// recorded handshake reaches <see cref="ZoneEntryAdmission.BeginEntry"/> on the tick. Admission reports the
    /// <see cref="Sessions"/> transitions (Connecting after the token validates, Connected on admission, failed
    /// connecting when the entry is rejected). The composition passes a fan-out of the outbox, <see cref="Admission"/>
    /// and <see cref="HandshakeIntake"/> to the transport as its <see cref="IClientConnectionListener"/>.
    /// </para>
    /// <para>
    /// <b>Session end inputs.</b> Story 016 wires the TTL input (the ghost loop in <see cref="Run"/>), the heartbeat timeout
    /// (<see cref="ConnectionStateMachine.EvaluateTimeouts"/> every tick) and transport close as silence (a closed transport
    /// stops producing activity, so the heartbeat timeout ghosts the session). Only the clean logout input of
    /// <see cref="SessionEnds"/> is not wired (TD-103).
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

        /// <summary>Handshake intake queue size per player slot (a client may resend while the first is still queued).</summary>
        private const int HANDSHAKE_QUEUE_PER_PLAYER = 2;

        /// <summary>
        /// Moves the networking session of an entering account: Connecting when the token validated, Connected when the
        /// player is admitted, failed connecting otherwise (Gateway Story 012). Only touches sessions it created: an
        /// account that already has a registered session (a second entry for a live account) is left alone with one warning.
        /// The token-path reconnect of a ghost session does not come through here: <see cref="ReconnectSessionAdapter"/> handles it.
        /// </summary>
        private sealed class SessionSinkAdapter : IZoneEntrySessionSink
        {
            private readonly ConnectionStateMachine _machine;
            private readonly HashSet<uint> _owned = new HashSet<uint>();

            public SessionSinkAdapter(ConnectionStateMachine machine)
            {
                _machine = machine;
            }

            public void OnTokenAccepted(AccountID account, uint clientId, uint tick)
            {
                uint raw = account.RawValue;
                if (_machine.IsAccountRegistered(raw))
                {
                    Debug.LogWarning($"{LOG_PREFIX} SessionAlreadyRegistered: account {raw} already has a session; client {clientId} enters without changing it.");
                    return;
                }

                _machine.EnterConnecting(raw, tick);
                _owned.Add(raw);
            }

            public void OnEntryAdmitted(AccountID account, CharacterID character, uint tick)
            {
                uint raw = account.RawValue;
                if (_owned.Remove(raw) && _machine.TryGetSessionState(raw, out SessionState state) && state == SessionState.Connecting)
                {
                    _machine.CompleteAuthSuccess(raw, character.RawValue, tick);
                }
            }

            public void OnEntryFailed(AccountID account, string trigger)
            {
                uint raw = account.RawValue;
                if (_owned.Remove(raw) && _machine.TryGetSessionState(raw, out SessionState state) && state == SessionState.Connecting)
                {
                    _machine.FailConnecting(raw, trigger);
                }
            }
        }

        /// <summary>
        /// Session side of the token-path reconnect (CR-TOK-9): <c>Disconnected_SessionActive -&gt; Reconnecting</c>, then
        /// <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/> (CR-NET-6.4). Pending purchases and respec reservations have no real store yet,
        /// so their seams are stand-ins until the Shop and Respec systems own a store (no purchases, no reservation); the handshake data carries level and HP from the entity state source
        /// and zero for gold version, MP, free points and class (they only reach the dev observer today).
        /// </summary>
        private sealed class ReconnectSessionAdapter : IZoneReconnectSessionSink
        {
            private static readonly IReadOnlyList<IronGrind.Networking.PendingPurchaseRecord> NO_PURCHASES =
                Array.Empty<IronGrind.Networking.PendingPurchaseRecord>();

            private readonly ConnectionStateMachine _machine;
            private readonly Func<uint, bool> _isEndInFlight;
            private readonly ICurrencyService _currency;
            private readonly IZoneEntityStateSource _state;

            public ReconnectSessionAdapter(ConnectionStateMachine machine, Func<uint, bool> isEndInFlight, ICurrencyService currency,
                IZoneEntityStateSource state)
            {
                _machine = machine;
                _isEndInFlight = isEndInFlight;
                _currency = currency;
                _state = state;
            }

            public bool TryBeginReconnect(AccountID account, uint tick)
            {
                uint raw = account.RawValue;
                if (!_machine.TryGetSessionState(raw, out SessionState state) || state != SessionState.Disconnected_SessionActive
                    || !_machine.TryGetSessionExpiryTick(raw, out uint expiry) || _isEndInFlight(raw))
                {
                    return false;
                }

                _machine.EnterReconnecting(raw, expiry);
                return true;
            }

            public void CompleteReconnect(AccountID account, CharacterID character, EntityID entityId, uint tick)
            {
                int level = 0;
                int hp = 0;
                int mp = 0;
                if (_state.TryGetPlayerState(entityId, out ZonePlayerStateView view))
                {
                    level = view.Level;
                    hp = view.CurrentHp;
                    mp = view.CurrentMp;
                }

                _machine.CompleteReAuthSuccess(account.RawValue, tick, _currency, QueryNoPurchases, MarkNoPurchase,
                    QueryNoRespec, NoOpCharacter, NoOpCharacter,
                    new SessionHandshakeData(false, 0u, level, hp, mp, 0, 0));
            }

            public bool RevertToGhost(AccountID account) => _machine.RevertToGhost(account.RawValue);

            private static IReadOnlyList<IronGrind.Networking.PendingPurchaseRecord> QueryNoPurchases(uint characterId) => NO_PURCHASES;

            private static void MarkNoPurchase(IronGrind.Networking.PendingPurchaseRecord record) { }

            private static RespecReservationStatus QueryNoRespec(uint characterId) => RespecReservationStatus.NoActiveReservation;

            private static void NoOpCharacter(uint characterId) { }
        }

        private readonly SessionHandshakeIntake _handshakeIntake;
        private readonly ZoneID _zone;
        private readonly IServerTickSource _ticks;
        private readonly IClientMessageOutbox _outbox;
        private readonly Action<ZoneExitCode> _requestTeardown;
        private readonly IZoneGhostSessionSource _ghosts;
        private readonly SessionTokenStore _tokenStore;
        private readonly AdmittedEntries _admitted;
        private readonly GatewayAdmitHandler _gatewayAdmit;
        private readonly GatewayEvictHandler _gatewayEvict;
        private readonly GatewayReconnectHandler _gatewayReconnect;
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
        private readonly IGatewayLinkOutbox _gatewayOutbox;
        private readonly RecordingConnectionActivitySink _activitySink;
        private readonly uint _heartbeatTimeoutTicks;
        private readonly uint _connectingTimeoutTicks;
        private int _pendingQuits;
        private bool _fatalRecorded;
        private bool _zoneDrainingSent;

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
        /// <param name="options">Knobs and test seams; required for <see cref="ZoneInstancingWiringOptions.Equipment"/>.</param>
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

            IronGrind.EquipmentSystem.IEquipmentService equipment = options.Equipment
                ?? throw new ArgumentNullException(nameof(options), "options.Equipment is required (Character Persistence Story 019).");

            IPartyZonePresence party = options.Party ?? new NoPartyZonePresence();
            IZoneEntityStateSource state = options.EntityState ?? new LoggingZoneEntityStateSource();
            _ghosts = options.Ghosts ?? new LoggingZoneGhostSessionSource();
            _directory = options.Directory ?? new ZoneConnectionCharacterDirectory();
            _sessions = options.Sessions ?? new ConnectionStateMachine();
            _activitySink = options.ActivitySink;
            _heartbeatTimeoutTicks = (uint)(options.HeartbeatTimeoutSeconds * options.TickRateHz);
            _connectingTimeoutTicks = (uint)(options.ConnectingTimeoutSeconds * options.TickRateHz);

            _tokenStore = options.TokenStore ?? new SessionTokenStore();
            IGatewayLinkOutbox gatewayOutbox = options.GatewayOutbox ?? new LoggingGatewayLinkOutbox();
            _gatewayOutbox = gatewayOutbox;
            _admitted = new AdmittedEntries(_tokenStore, gatewayOutbox,
                options.ConnectingTimeoutSeconds * options.TickRateHz, OnReservedCountChanged,
                character => _admission != null && _admission.IsLoadPending(character));

            _lifecycle = new ZoneLifecycle(zone, this, 0u, options.IdleCloseSeconds, options.DrainGraceSeconds,
                options.LoadTimeoutSeconds, options.TickRateHz
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , options.Observer
#endif
                );
            _pool = new ZoneSlotPool(zone, ticks, this, options.MaxPlayers, options.MaxMobs, options.FirstEntityId);
            _gatewayAdmit = new GatewayAdmitHandler(_admitted, _tokenStore, gatewayOutbox,
                () => _lifecycle.AcceptsPlayers, () => SessionCount, options.MaxPlayers);
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
                options.Validator ?? new GatewayZoneEntryTokenValidator(_admitted, _tokenStore, _sessions), disconnector, outbox,
                _completion, _directory, reclamation: _reclamation, ticks: ticks,
                sessionSink: new SessionSinkAdapter(_sessions));
            _admission.SetReservationSink(_admitted);

            // Gateway Story 012: the client's SessionHandshake (0xE136) is recorded in the receive callback and drained on the tick.
            _handshakeIntake = new SessionHandshakeIntake(_admission, disconnector, options.MaxPlayers * HANDSHAKE_QUEUE_PER_PLAYER);
            SessionHandshakeIntake.Register(dispatcher, _handshakeIntake);

            _notifiedAtDrain = new HashSet<uint>(options.MaxPlayers);
            _drainBodySize = ZoneSessionEndedCodec.WriteBody(_drainBody,
                new ZoneSessionEnded(zone, DisconnectReason.ZoneClosed, options.DrainGraceSeconds));

            _closeSteps = new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, persistence, queue, party,
                options.MobSpawnTimers ?? new LoggingZoneMobSpawnTimers(),
                options.TeardownSessionSink ?? new LoggingZoneTeardownSessionSink(), this, outbox, zone);
            _playerExit = new ZonePlayerExit(_pool, _reclamation);
            _sessionEnds = new SessionEndCoordinator(persistence, sessionRelease, queue, gate, _sessions, inventory,
                equipment, options.ClearEquipRequestStore ?? (_ => { }),
                options.UnregisterPlayerEntity ?? (_ => { }), _ => { },
                (characterId, type) => _playerExit.ReleasePlayer(characterId, type),
                CloseClientOfAccount);
            _gatewayEvict = new GatewayEvictHandler(_admitted, _tokenStore, gatewayOutbox, _admission, _reclamation, _sessions, _sessionEnds);
            _gatewayReconnect = new GatewayReconnectHandler(_sessions, _tokenStore, _sessionEnds, gatewayOutbox, options.ReauthFailureLimit);

            // Token-path reconnect (Authentication Story 017 AC-6): the ghost's slot is reused, no LoadCharacter.
            _admission.SetReconnectSeams(
                new ReconnectSessionAdapter(_sessions, _sessionEnds.IsEndInFlight, options.Currency ?? new CurrencySystem(), state),
                _completion);
        }

        // Closes the transport connection of the account's slot, if it holds one (gateway eviction, Story 014).
        private void CloseClientOfAccount(uint accountId)
        {
            System.Collections.Generic.IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_pool.Zone);
            for (int i = 0; i < players.Count; i++)
            {
                if (_pool.TryGetPlayer(players[i], out ZonePlayerSlot slot) && slot.Account.RawValue == accountId)
                {
                    _reclamation.CloseConnection(slot.ClientId);
                    return;
                }
            }
        }

        /// <summary>The <c>Evict</c> handler (Authentication Story 014); the gateway link feeds it decoded messages on the tick thread.</summary>
        internal GatewayEvictHandler GatewayEvict => _gatewayEvict;

        /// <summary>The <c>Reconnect</c> handler (Authentication Story 014).</summary>
        internal GatewayReconnectHandler GatewayReconnect => _gatewayReconnect;

        /// <summary>The lifecycle state machine.</summary>
        public ZoneLifecycle Lifecycle => _lifecycle;

        /// <summary>The slot pool.</summary>
        public ZoneSlotPool Pool => _pool;

        /// <summary>The mob roster over <see cref="Pool"/>; the composition reads it for the production entity state source (Story 015).</summary>
        internal ZoneMobRoster MobRoster => _mobs;

        /// <summary>The entry completion; the composition registers Movement as its entry listener (Movement Story 009).</summary>
        internal ZoneEntryCompletion EntryCompletion => _completion;

        /// <summary>The connection-to-character directory (the dispatcher's, when the composition passed it in).</summary>
        public ZoneConnectionCharacterDirectory Directory => _directory;

        /// <summary>The tick roster.</summary>
        public ZoneTickRoster TickRoster => _roster;

        /// <summary>The entry reclamation service.</summary>
        public ZoneEntryReclamation Reclamation => _reclamation;

        /// <summary>Admission; <see cref="HandshakeIntake"/> feeds it the decoded handshakes and the composition registers it as a connection listener.</summary>
        public ZoneEntryAdmission Admission => _admission;

        /// <summary>The <c>SessionHandshake</c> intake (Gateway Story 012); also a connection listener (drops a removed client's record).</summary>
        public SessionHandshakeIntake HandshakeIntake => _handshakeIntake;

        /// <summary>
        /// The <c>Admit</c> handler. The gateway link (gateway epic) feeds it decoded <c>Admit</c> messages on the tick
        /// thread; the handshake decode that reaches <see cref="Admission"/> is unowned (Authentication Story 013, Dependencies).
        /// </summary>
        internal GatewayAdmitHandler GatewayAdmit => _gatewayAdmit;

        /// <summary>The gateway admissions awaiting their handshake.</summary>
        internal AdmittedEntries Admitted => _admitted;

        /// <summary>The zone's session token store (supplied through the options, or created here).</summary>
        internal SessionTokenStore TokenStore => _tokenStore;

        /// <summary>The zone's session count: occupied player slots plus reserved gateway admissions. The one definition, used by the Admit capacity check and the lifecycle idle input.</summary>
        internal int SessionCount => SessionsFor(_pool.OccupiedPlayerCount);

        private int SessionsFor(int occupied) => occupied + (_admitted?.ReservedCount ?? 0);

        // Reserved admissions changed: the lifecycle idle input follows the session count.
        private void OnReservedCountChanged(uint serverTick)
        {
            try
            {
                _lifecycle?.OnOccupiedPlayerSlotsChanged(SessionsFor(_pool.OccupiedPlayerCount), serverTick);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} reserved count forward failed ({ex.GetType().Name}).\n{ex}");
            }
        }

        /// <summary>The player exit service.</summary>
        public ZonePlayerExit PlayerExit => _playerExit;

        /// <summary>The session state machine: the composition constructs it and passes it through <see cref="ZoneInstancingWiringOptions.Sessions"/>; the default is a new machine.</summary>
        public ConnectionStateMachine Sessions => _sessions;

        /// <summary>The session end coordinator: the TTL input comes from the ghost loop in <see cref="Run"/> (Story 016); the clean logout input is deferred (TD-103).</summary>
        public SessionEndCoordinator SessionEnds => _sessionEnds;

        /// <summary>The activity sink this wiring drains each tick (the composition's, shared with the dispatcher); null when none was passed.</summary>
        internal RecordingConnectionActivitySink ActivitySink => _activitySink;

        /// <summary>The ghost source the TTL loop reads each tick.</summary>
        internal IZoneGhostSessionSource GhostSource => _ghosts;

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
            _handshakeIntake.Drain(currentTick); // Gateway Story 012: recorded SessionHandshakes start their entries.

            DrainActivity();

            // Story 016 (AC-6): heartbeat timeout and connecting timeout, after the activity drain so a packet received
            // this tick prevents a timeout evaluated this tick (AC-8).
            _sessions.EvaluateTimeouts(currentTick, _heartbeatTimeoutTicks, _connectingTimeoutTicks);

            _ghosts.Refresh();
            int ghostCount = _ghosts.Count;
            for (int i = 0; i < ghostCount; i++)
            {
                _ghosts.Get(i, out uint accountId, out uint expiryTick);
                _sessionEnds.EvaluateSessionTTL(accountId, currentTick, expiryTick);
            }

            _admitted.OnTick(currentTick);
            _lifecycle.OnTick(currentTick);
            _snapshotRequests.ServePending(currentTick);
        }

        // Story 016 (AC-1): moves the activity the receive callback recorded into the session machine. Scans the player
        // slots (at most MAX_PLAYERS_PER_ZONE) for each client's account; a client without a slot is ignored. No allocation.
        private void DrainActivity()
        {
            if (_activitySink == null)
            {
                return;
            }

            if (_activitySink.Count > 0)
            {
                int capacity = _pool.MaxPlayers;
                for (int i = 0; i < capacity; i++)
                {
                    if (_pool.TryGetPlayerAtSlot(i, out EntityID id, out uint clientId)
                        && _activitySink.TryTake(clientId, out uint tick)
                        && _pool.TryGetPlayer(id, out ZonePlayerSlot slot))
                    {
                        _sessions.RecordInboundActivity(slot.Account.RawValue, tick);
                    }
                }
            }

            _activitySink.Clear();
        }

        // ----- IZoneLifecycleListener ---------------------------------------------------------------

        /// <inheritdoc/>
        public void OnDraining(uint serverTick)
        {
            try
            {
                LogTransition("Draining", serverTick);
                SendZoneDrainingOnce();
                try
                {
                    _admitted.OnLeftActive(serverTick); // T-4: reservations end, tokens are kept (AC-ZI-35)
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} admitted entries T-4 release failed ({ex.GetType().Name}).\n{ex}");
                }

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

                // Active -> Closed (fatal, idle close) skips OnDraining; the gateway still has to stop routing here.
                SendZoneDrainingOnce();

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

        // Gateway Story 011: ZoneDraining goes out once, when the zone first leaves Active (never throws).
        private void SendZoneDrainingOnce()
        {
            if (_zoneDrainingSent)
            {
                return;
            }

            _zoneDrainingSent = true;
            try
            {
                _gatewayOutbox.EnqueueZoneDraining();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} ZoneDraining send failed ({ex.GetType().Name}).\n{ex}");
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
                _lifecycle?.OnOccupiedPlayerSlotsChanged(SessionsFor(occupied), serverTick);
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
