using System;
using IronGrind.CharacterPersistence;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The zone lifecycle state machine (<c>zone-instancing.md</c> CR-ZI-2, transitions T-1 to T-7).
    /// One zone per process (ADR-007, ADR-009), so this class tracks a single zone, not a registry.
    /// States: <see cref="ZoneState.Loading"/> → <see cref="ZoneState.Active"/> →
    /// <see cref="ZoneState.Draining"/> → <see cref="ZoneState.Closed"/> (terminal).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inputs.</b> Three kinds, each carrying the current server tick: <see cref="OnTick"/>,
    /// occupancy changes (<see cref="OnOccupiedPlayerSlotsChanged"/>, fed by the slot pool of Zone
    /// Instancing Story 002) and requests (<see cref="MarkActive"/>, <see cref="MarkInitFailed"/>,
    /// <see cref="RequestQuit"/>, <see cref="RequestFatal"/>).
    /// </para>
    /// <para>
    /// <b>Time.</b> Every timer is an absolute server-tick deadline (<c>eventTick + seconds ×
    /// tickRateHz</c>); no wall-clock time is read. Per-tick work is two comparisons and nothing is
    /// allocated.
    /// </para>
    /// <para>
    /// <b>Closing.</b> Every path into <see cref="ZoneState.Closed"/> sets the state first, then
    /// notifies the optional <c>INetworkTestObserver</c> (development builds), then calls
    /// <see cref="IZoneLifecycleListener.OnClosed"/>, which happens exactly once. A request that does
    /// not apply in the current state is a no-op that logs one warning; it never throws.
    /// </para>
    /// <para>
    /// <b>Threading and listeners.</b> Main thread only (ADR-011): the caller serialises every input.
    /// <see cref="IZoneLifecycleListener"/> implementations must not throw; a throwing listener leaves
    /// the state already changed (a throw from <c>OnDraining</c> defers T-5 to the grace deadline).
    /// </para>
    /// <para>
    /// <b>Load timeout.</b> T-3 by timeout is evaluated only in <see cref="OnTick"/>, so the owner must
    /// tick the lifecycle while it is Loading (Zone Instancing Story 014: the startup frame polling).
    /// A <see cref="MarkActive"/> that arrives after the deadline but before any <see cref="OnTick"/>
    /// still activates the zone — the load finished.
    /// </para>
    /// <para>
    /// <b>Out of scope.</b> The per-template <c>loadingInProgress</c> flag and the routing disable at
    /// T-4 belong to the gateway, not here.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var lifecycle = new ZoneLifecycle(new ZoneID(7u), listener, loadingStartTick: 0u);
    /// lifecycle.MarkActive(serverTick: 40u);                           // T-2
    /// lifecycle.OnOccupiedPlayerSlotsChanged(1, serverTick: 100u);
    /// lifecycle.RequestQuit(serverTick: 500u);                         // T-4, listener.OnDraining
    /// lifecycle.OnOccupiedPlayerSlotsChanged(0, serverTick: 520u);     // T-5, listener.OnClosed(Drained)
    /// </code>
    /// </example>
    public sealed class ZoneLifecycle
    {
        /// <summary>Default <c>ZONE_IDLE_CLOSE_SECONDS</c> (GDD tuning knob, range [60, 600]).</summary>
        public const int ZONE_IDLE_CLOSE_SECONDS = 120;

        /// <summary>Default <c>ZONE_DRAIN_GRACE_SECONDS</c> (GDD tuning knob).</summary>
        public const int ZONE_DRAIN_GRACE_SECONDS = 60;

        /// <summary>Default <c>ZONE_LOAD_TIMEOUT_SECONDS</c> (GDD tuning knob).</summary>
        public const int ZONE_LOAD_TIMEOUT_SECONDS = 30;

        private const string LOG_PREFIX = "[ZoneLifecycle]";

        private readonly ZoneID _zone;
        private readonly IZoneLifecycleListener _listener;
        private readonly long _idleCloseTicks;
        private readonly long _drainGraceTicks;
        private readonly long _loadDeadlineTick;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        private readonly INetworkTestObserver _observer;
#endif

        private ZoneState _state = ZoneState.Loading;
        private int _occupiedPlayerSlots;
        private bool _idleArmed;
        private long _idleDeadlineTick;
        private long _graceDeadlineTick;

        /// <summary>
        /// Creates a lifecycle for <paramref name="zone"/> in <see cref="ZoneState.Loading"/> (T-1).
        /// </summary>
        /// <param name="zone">The zone this process hosts. Must not be <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="listener">Receiver of <c>OnDraining</c> / <c>OnClosed</c>. Must not be null.</param>
        /// <param name="loadingStartTick">The server tick on which loading started (T-1).</param>
        /// <param name="idleCloseSeconds"><c>ZONE_IDLE_CLOSE_SECONDS</c>. Must be positive.</param>
        /// <param name="drainGraceSeconds"><c>ZONE_DRAIN_GRACE_SECONDS</c>. Must be positive.</param>
        /// <param name="loadTimeoutSeconds"><c>ZONE_LOAD_TIMEOUT_SECONDS</c>. Must be positive.</param>
        /// <param name="tickRateHz">Server ticks per second. Must be positive.</param>
        /// <param name="observer">Optional development-build observer; sees every state transition.</param>
        /// <exception cref="ArgumentNullException"><paramref name="listener"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is the invalid (0) zone id.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Any seconds or rate argument is not positive.</exception>
        public ZoneLifecycle(
            ZoneID zone,
            IZoneLifecycleListener listener,
            uint loadingStartTick,
            int idleCloseSeconds = ZONE_IDLE_CLOSE_SECONDS,
            int drainGraceSeconds = ZONE_DRAIN_GRACE_SECONDS,
            int loadTimeoutSeconds = ZONE_LOAD_TIMEOUT_SECONDS,
            int tickRateHz = ServerTickLoop.TICK_RATE_HZ
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            RequirePositive(idleCloseSeconds, nameof(idleCloseSeconds));
            RequirePositive(drainGraceSeconds, nameof(drainGraceSeconds));
            RequirePositive(loadTimeoutSeconds, nameof(loadTimeoutSeconds));
            RequirePositive(tickRateHz, nameof(tickRateHz));

            _zone = zone;
            _listener = listener;
            _idleCloseTicks = (long)idleCloseSeconds * tickRateHz;
            _drainGraceTicks = (long)drainGraceSeconds * tickRateHz;
            _loadDeadlineTick = loadingStartTick + (long)loadTimeoutSeconds * tickRateHz;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            _observer = observer;
#endif
        }

        /// <summary>The zone this lifecycle tracks.</summary>
        public ZoneID Zone => _zone;

        /// <summary>The current lifecycle state (CR-ZI-2).</summary>
        public ZoneState State => _state;

        /// <summary>The join gate: true only in <see cref="ZoneState.Active"/>.</summary>
        public bool AcceptsPlayers => _state == ZoneState.Active;

        /// <summary>The occupied player slot count most recently reported by the slot pool.</summary>
        public int OccupiedPlayerSlots => _occupiedPlayerSlots;

        /// <summary>
        /// T-2: <see cref="ZoneState.Loading"/> → <see cref="ZoneState.Active"/>. If no player slot is
        /// occupied, the idle timer starts on this tick (AC-ZI-21).
        /// </summary>
        /// <param name="serverTick">The current server tick.</param>
        public void MarkActive(uint serverTick)
        {
            if (!RequireState(ZoneState.Loading, nameof(MarkActive)))
            {
                return;
            }

            SetState(ZoneState.Active);
            if (_occupiedPlayerSlots == 0)
            {
                ArmIdle(serverTick);
            }
        }

        /// <summary>
        /// T-3: init failure (bounds, missing data). <see cref="ZoneState.Loading"/> →
        /// <see cref="ZoneState.Closed"/>, <see cref="ZoneCloseTransition.LoadFailed"/>.
        /// </summary>
        /// <param name="serverTick">The current server tick.</param>
        public void MarkInitFailed(uint serverTick)
        {
            if (!RequireState(ZoneState.Loading, nameof(MarkInitFailed)))
            {
                return;
            }

            Close(ZoneCloseTransition.LoadFailed, serverTick);
        }

        /// <summary>
        /// Reports the occupied player slot count (from the slot pool). In Active, reaching 0 starts the
        /// idle timer and any positive count cancels it (T-7 precondition). In Draining, reaching 0 fires
        /// T-5 on this tick. Never re-enters Active from Draining. In Loading or Closed the count is
        /// only recorded (slots are released during teardown, which is not an error).
        /// </summary>
        /// <param name="occupied">The current occupied player slot count; must not be negative.</param>
        /// <param name="serverTick">The current server tick.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="occupied"/> is negative.</exception>
        public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick)
        {
            if (occupied < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(occupied),
                    $"{LOG_PREFIX} {nameof(OnOccupiedPlayerSlotsChanged)}: occupied must not be negative (was {occupied}).");
            }

            _occupiedPlayerSlots = occupied;

            if (_state == ZoneState.Active)
            {
                if (occupied == 0)
                {
                    if (!_idleArmed)
                    {
                        ArmIdle(serverTick);
                    }
                }
                else
                {
                    _idleArmed = false;
                }
            }
            else if (_state == ZoneState.Draining && occupied == 0)
            {
                Close(ZoneCloseTransition.Drained, serverTick);
            }
        }

        /// <summary>
        /// A quit request. Loading → Closed via T-6; Active → Draining via T-4 (idle timer cancelled,
        /// grace countdown started, <see cref="IZoneLifecycleListener.OnDraining"/> called, and if no
        /// slot is occupied T-5 fires on the same tick, AC-ZI-25); Draining → Closed via T-6 (second
        /// quit, AC-ZI-24).
        /// </summary>
        /// <param name="serverTick">The current server tick.</param>
        public void RequestQuit(uint serverTick)
        {
            switch (_state)
            {
                case ZoneState.Loading:
                case ZoneState.Draining:
                    Close(ZoneCloseTransition.Emergency, serverTick);
                    break;

                case ZoneState.Active:
                    _idleArmed = false;
                    _graceDeadlineTick = serverTick + _drainGraceTicks;
                    SetState(ZoneState.Draining);
                    _listener.OnDraining(serverTick);

                    // The listener may already have closed the zone (re-entrant RequestFatal); keep OnClosed once.
                    if (_state == ZoneState.Draining && _occupiedPlayerSlots == 0)
                    {
                        Close(ZoneCloseTransition.Drained, serverTick);
                    }
                    break;

                default:
                    LogIgnored(nameof(RequestQuit));
                    break;
            }
        }

        /// <summary>
        /// T-6: a fatal error or <c>EntityIDCounterExhausted</c>. Any non-Closed state → Closed on this
        /// tick, <see cref="ZoneCloseTransition.Emergency"/>.
        /// </summary>
        /// <param name="serverTick">The current server tick.</param>
        public void RequestFatal(uint serverTick)
        {
            if (_state == ZoneState.Closed)
            {
                LogIgnored(nameof(RequestFatal));
                return;
            }

            Close(ZoneCloseTransition.Emergency, serverTick);
        }

        /// <summary>
        /// Advances the timers: load timeout (T-3) in Loading, idle close (T-7) in Active when the idle
        /// timer is armed, grace expiry (T-5) in Draining. A silent no-op after Closed.
        /// </summary>
        /// <param name="serverTick">The current server tick.</param>
        public void OnTick(uint serverTick)
        {
            switch (_state)
            {
                case ZoneState.Loading:
                    if (serverTick >= _loadDeadlineTick)
                    {
                        Close(ZoneCloseTransition.LoadFailed, serverTick);
                    }
                    break;

                case ZoneState.Active:
                    if (_idleArmed && serverTick >= _idleDeadlineTick)
                    {
                        Close(ZoneCloseTransition.Idle, serverTick);
                    }
                    break;

                case ZoneState.Draining:
                    if (serverTick >= _graceDeadlineTick)
                    {
                        Close(ZoneCloseTransition.Drained, serverTick);
                    }
                    break;
            }
        }

        private void ArmIdle(uint serverTick)
        {
            _idleArmed = true;
            _idleDeadlineTick = serverTick + _idleCloseTicks;
        }

        private void Close(ZoneCloseTransition transition, uint serverTick)
        {
            // OnClosed exactly once, whatever path or re-entrant call gets here.
            if (_state == ZoneState.Closed)
            {
                return;
            }

            _idleArmed = false;
            SetState(ZoneState.Closed);
            _listener.OnClosed(transition, serverTick);
        }

        private void SetState(ZoneState to)
        {
            ZoneState from = _state;
            _state = to;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            _observer?.OnZoneStateTransitioned(_zone.RawValue, from, to);
#endif
        }

        private bool RequireState(ZoneState required, string callerName)
        {
            if (_state == required)
            {
                return true;
            }

            Debug.LogWarning($"{LOG_PREFIX} {callerName} ignored: zone {_zone.RawValue} is {_state}, expected {required}.");
            return false;
        }

        private void LogIgnored(string callerName)
        {
            Debug.LogWarning($"{LOG_PREFIX} {callerName} ignored: zone {_zone.RawValue} is {_state}.");
        }

        private static void RequirePositive(int value, string paramName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(paramName,
                    $"{LOG_PREFIX} {paramName} must be positive (was {value}).");
            }
        }
    }
}
