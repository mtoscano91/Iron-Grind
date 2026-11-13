using System;
using System.Globalization;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Development probe for the ADR-002 5a frame-rate pin (Navigation Story 010, TR-nav-008): in a server build it
    /// claims one real agent through the composition's <c>MobEventBus</c>, sends it to (40, 0, 0) and for 10 real seconds
    /// logs, once per second, <c>[NavProbe] t=… frames=… movingFrames=… meanStep=… minStep=… maxStep=… totalAdvance=…</c>
    /// (frames are <c>Update</c> calls; <c>movingFrames</c> are the frames whose <c>nextPosition</c> step exceeded
    /// <see cref="MOVING_STEP_EPSILON"/> and show the agent simulation cadence; step statistics are over moving frames
    /// only). Pass at 20 Hz: movingFrames ≈ frames ≈ 20 and meanStep ≈ 0.175. It measures the cadence of agent
    /// simulation, not the distance (agent movement is delta-time scaled). The first <see cref="WARMUP_SECONDS"/> after
    /// the start are not measured (async path computation); the 10 s window starts after the warm-up. If the zone
    /// leaves Active mid-run the probe raises <c>OnMobDied</c> for its mob (if still registered) so it is not left
    /// registered. Any exception in a measuring frame logs <c>[NavProbe] aborted: &lt;ExceptionTypeName&gt;</c> and finishes.
    /// A plain class: the host drives it from <c>Update</c> through <see cref="Tick"/>, once per frame.
    /// </summary>
    /// <remarks>
    /// Enabled by the command-line flag <see cref="FLAG"/> (no value) and honoured only when the injected debug-build
    /// check says so; otherwise one warning is logged and nothing is spawned. It starts only once the zone is Active
    /// (<c>ZoneServerComposition.IsTickRunning</c>). With no navigation built by the composition (a seam was passed) it
    /// logs one warning and does nothing.
    /// <para>
    /// Probe entity id: <see cref="PROBE_ENTITY_RAW"/> (<c>uint.MaxValue - 1</c>). The zone slot pool mints entity ids
    /// upward from 1 (players and mobs alike), so a live id would need about four billion mints in one zone process
    /// to reach it; the id <c>0</c> is <c>EntityID.Invalid</c> and is not used.
    /// </para>
    /// Time and the zone state are read through the composition and the <c>unscaledTime</c> argument, so a test drives the
    /// probe with fed time.
    /// </remarks>
    /// <example>
    /// <code>
    /// if (NavFrameRateProbe.IsRequested(args)) { probe = new NavFrameRateProbe(composition, () => Debug.isDebugBuild); }
    /// // Update:
    /// probe?.Tick(Time.unscaledTime);
    /// </code>
    /// </example>
    internal sealed class NavFrameRateProbe
    {
        /// <summary>The command-line flag that turns the probe on (no value).</summary>
        public const string FLAG = "-navFrameRateProbe";

        /// <summary>Probe mob id, outside the live id range (see the type remarks).</summary>
        public const uint PROBE_ENTITY_RAW = uint.MaxValue - 1u;

        /// <summary>Mob type given to the probe mob; the stand-in speed source accepts any type.</summary>
        public const uint PROBE_MOB_TYPE_RAW = 1u;

        /// <summary>Distance of the probe destination from the origin along +X, in metres.</summary>
        public const float DESTINATION_X = 40f;

        /// <summary>How long the probe measures, in real seconds, counted after the warm-up.</summary>
        public const float DURATION_SECONDS = 10f;

        /// <summary>
        /// Real seconds after the start that are not measured: the path request is queued and drained at the next tick
        /// and the path is computed asynchronously, so the first frames do not move the agent.
        /// </summary>
        public const float WARMUP_SECONDS = 0.5f;

        /// <summary>A <c>nextPosition</c> step above this (metres) counts as a moving frame.</summary>
        public const float MOVING_STEP_EPSILON = 1e-5f;

        /// <summary>Length of one log interval, in real seconds.</summary>
        public const float LOG_INTERVAL_SECONDS = 1f;

        /// <summary>Prefix of every probe log line.</summary>
        public const string LOG_PREFIX = "[NavProbe] ";

        /// <summary>Text of the final line.</summary>
        public const string DONE_LINE = LOG_PREFIX + "done";

        private enum State
        {
            Waiting,
            Running,
            Finished
        }

        private readonly ZoneServerComposition _composition;
        private readonly Func<bool> _isDebugBuild;
        private State _state = State.Waiting;

        private ZoneNavigationService _service;
        private MobEventBus _bus;
        private ZoneID _zone;
        private EntityID _entity;
        private Vector3 _startPosition;
        private Vector3 _previousPosition;
        private float _startTime;
        private float _intervalStart;
        private int _intervalFrames;
        private float _intervalStepSum;
        private bool _warmedUp;
        private float _measureStart;
        private int _intervalMovingFrames;
        private float _intervalMovingStepSum;
        private float _intervalMinStep = float.MaxValue;
        private float _intervalMaxStep;

        /// <summary>Creates the probe; nothing happens until <see cref="Tick"/> sees the zone Active.</summary>
        /// <param name="composition">The zone composition (its bus, service and state are read lazily).</param>
        /// <param name="isDebugBuild">Whether this is a debug build (<c>Debug.isDebugBuild</c> in production).</param>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public NavFrameRateProbe(ZoneServerComposition composition, Func<bool> isDebugBuild)
        {
            _composition = composition ?? throw new ArgumentNullException(nameof(composition));
            _isDebugBuild = isDebugBuild ?? throw new ArgumentNullException(nameof(isDebugBuild));
        }

        /// <summary>True once the probe finished (done, refused or aborted); it never restarts.</summary>
        public bool IsFinished => _state == State.Finished;

        /// <summary>True while it is measuring (the mob was raised and not yet reported dead).</summary>
        public bool IsRunning => _state == State.Running;

        /// <summary>Whether the command line carries <see cref="FLAG"/>.</summary>
        /// <param name="args">The raw command line; null counts as absent.</param>
        /// <returns>True when the flag is present.</returns>
        public static bool IsRequested(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == FLAG)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One frame. Call from the host's <c>Update</c>.</summary>
        /// <param name="unscaledTime">Real time in seconds (<c>Time.unscaledTime</c>).</param>
        public void Tick(float unscaledTime)
        {
            switch (_state)
            {
                case State.Waiting:
                    TryStart(unscaledTime);
                    break;
                case State.Running:
                    Measure(unscaledTime);
                    break;
            }
        }

        private void TryStart(float now)
        {
            if (!_composition.IsTickRunning)
            {
                return;
            }

            if (!_isDebugBuild())
            {
                Debug.LogWarning(LOG_PREFIX + FLAG + " ignored: not a debug build.");
                _state = State.Finished;
                return;
            }

            _service = _composition.NavigationService;
            _bus = _composition.MobEvents;
            if (_service == null || _bus == null)
            {
                Debug.LogWarning(LOG_PREFIX + "the composition built no navigation (a seam was passed); nothing to probe.");
                _state = State.Finished;
                return;
            }

            _zone = _composition.ZoneId;
            _entity = new EntityID(PROBE_ENTITY_RAW);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(_zone, _entity, new MobTypeID(PROBE_MOB_TYPE_RAW), Vector3.zero));
            if (!_service.TryGetAgentNextPosition(_entity, out _startPosition))
            {
                Debug.LogWarning(LOG_PREFIX + "the probe mob was not claimed (see the [Navigation] log); aborting.");
                _state = State.Finished;
                return;
            }

            _service.SetDestination(_entity, new Vector3(DESTINATION_X, 0f, 0f));
            _previousPosition = _startPosition;
            _startTime = now;
            _warmedUp = false;
            ResetInterval(now);
            _state = State.Running;
            Debug.Log(LOG_PREFIX + "started: mob " + _entity + " at the origin, destination (" + DESTINATION_X.ToString(CultureInfo.InvariantCulture) + ", 0, 0).");
        }

        private void Measure(float now)
        {
            try
            {
                MeasureCore(now);
            }
            catch (Exception ex)
            {
                // A destroyed agent (MissingReferenceException) or any other failure must not break the host's Update.
                Debug.LogWarning(LOG_PREFIX + "aborted: " + ex.GetType().Name);
                _state = State.Finished;
            }
        }

        private void MeasureCore(float now)
        {
            if (!_composition.IsTickRunning)
            {
                // Release the probe mob so it is not left registered (only if it still is).
                if (_service.TryGetAgentNextPosition(_entity, out _))
                {
                    _bus.RaiseMobDied(new MobDiedArgs(_zone, _entity));
                }

                Debug.LogWarning(LOG_PREFIX + "zone left Active; probe aborted.");
                _state = State.Finished;
                return;
            }

            if (!_service.TryGetAgentNextPosition(_entity, out Vector3 position))
            {
                Debug.LogWarning(LOG_PREFIX + "the probe agent is no longer registered; aborting.");
                _state = State.Finished;
                return;
            }

            if (!_warmedUp)
            {
                if (now - _startTime < WARMUP_SECONDS)
                {
                    return;
                }

                // The first frame past the warm-up is the baseline: it opens the measurement window and is not counted.
                _warmedUp = true;
                _measureStart = now;
                _intervalStart = now;
                _previousPosition = position;
                return;
            }

            float step = Vector3.Distance(position, _previousPosition);
            _intervalFrames++;
            _intervalStepSum += step;
            if (step > MOVING_STEP_EPSILON)
            {
                _intervalMovingFrames++;
                _intervalMovingStepSum += step;
                _intervalMinStep = Mathf.Min(_intervalMinStep, step);
                _intervalMaxStep = Mathf.Max(_intervalMaxStep, step);
            }

            _previousPosition = position;

            if (now - _intervalStart >= LOG_INTERVAL_SECONDS)
            {
                LogInterval(now, position);
                ResetInterval(now);
            }

            if (now - _measureStart >= DURATION_SECONDS)
            {
                if (_intervalFrames > 0)
                {
                    LogInterval(now, position);
                }

                _bus.RaiseMobDied(new MobDiedArgs(_zone, _entity));
                Debug.Log(DONE_LINE);
                _state = State.Finished;
            }
        }

        private void ResetInterval(float now)
        {
            _intervalStart = now;
            _intervalFrames = 0;
            _intervalMovingFrames = 0;
            _intervalStepSum = 0f;
            _intervalMovingStepSum = 0f;
            _intervalMinStep = float.MaxValue;
            _intervalMaxStep = 0f;
        }

        private void LogInterval(float now, Vector3 position)
        {
            float meanStep = _intervalMovingFrames > 0 ? _intervalMovingStepSum / _intervalMovingFrames : 0f;
            float minStep = _intervalMovingFrames > 0 ? _intervalMinStep : 0f;
            float advance = Vector3.Distance(position, _startPosition);
            Debug.Log(LOG_PREFIX + "t=" + (now - _measureStart).ToString("F1", CultureInfo.InvariantCulture)
                + "s frames=" + _intervalFrames.ToString(CultureInfo.InvariantCulture)
                + " movingFrames=" + _intervalMovingFrames.ToString(CultureInfo.InvariantCulture)
                + " meanStep=" + meanStep.ToString("F4", CultureInfo.InvariantCulture)
                + " minStep=" + minStep.ToString("F4", CultureInfo.InvariantCulture)
                + " maxStep=" + _intervalMaxStep.ToString("F4", CultureInfo.InvariantCulture)
                + " totalAdvance=" + advance.ToString("F2", CultureInfo.InvariantCulture));
        }
    }
}
