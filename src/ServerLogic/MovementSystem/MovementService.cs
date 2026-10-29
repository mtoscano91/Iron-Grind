using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The server movement tick step (movement-system.md CR-MOV-6 steps 1 to 5, CR-MOV-7, CR-MOV-8, CR-MOV-9,
    /// CR-MOV-10, AC-MOV-21; Movement Stories 005 and 006). For each occupied player slot: take the intent, apply a
    /// pending facing override, store the facing, skip movement while Prohibited, integrate on moving ticks,
    /// validate against the NavMesh and commit. Story 007 adds the NavMesh stall recovery (EC-MOV-11, AC-MOV-24),
    /// the unavailable-NavMesh defensive branch (EC-MOV-12, AC-MOV-25) and the F-MOV-6 encoding guard on the
    /// committed value. Also implements <see cref="IMovementSystem"/>.
    /// </summary>
    /// <remarks>
    /// A facing override lasts exactly one tick and is consumed even while Prohibited; Prohibited still lets the
    /// facing update (CR-MOV-8).
    /// Per-slot state lives in arrays preallocated at <see cref="ZoneSlotPool.MAX_PLAYERS_PER_ZONE"/>; a tick
    /// allocates nothing. A zero-direction tick reads no speed, integrates nothing and samples nothing. A speed
    /// read failure logs one error on the valid-to-failing transition only. A NavMesh miss keeps the previous
    /// position and logs nothing (CR-MOV-7) but adds to the slot's stall counter; at the threshold the next stepped
    /// tick runs the recovery sample instead of a move. A non-finite candidate holds the position without sampling. The production <see cref="INavMeshSampler"/> is main thread only (ADR-002
    /// Decision 4), so the tick that calls <see cref="Run"/> must run on the Unity main thread (Story 009 confirms).
    /// </remarks>
    /// <example>
    /// <code>
    /// var service = new MovementService(intentBuffer, speedSource, new UnityNavMeshSampler(), slotIndex);
    /// service.Run(tick, ServerTickLoop.FIXED_DELTA_TIME);
    /// </code>
    /// </example>
    public sealed class MovementService : IZoneSimulationStep, IMovementSystem
    {
        /// <summary>NavMesh sample radius in meters (movement-system.md CR-MOV-7; a GDD value, not a tuning knob).</summary>
        public const float NAVMESH_SAMPLE_RADIUS = 0.5f;

        /// <summary>
        /// NavMesh search radius in meters for the stall recovery teleport (movement-system.md EC-MOV-11; a GDD
        /// constant, not a tuning knob).
        /// </summary>
        public const float NAVMESH_RECOVERY_SEARCH_RADIUS = 5.0f;

        private const int SLOT_COUNT = ZoneSlotPool.MAX_PLAYERS_PER_ZONE;
        private const string LOG_PREFIX = "[Movement]";

        private readonly IMovementIntentSource _intents;
        private readonly IMovementSpeedSource _speeds;
        private readonly INavMeshSampler _sampler;
        private readonly IEntitySlotIndex _slotIndex;
        private readonly int _stallRecoveryTicks;
        private readonly IMovementGeometryErrorSink _geometryErrors;

        private readonly bool[] _occupied = new bool[SLOT_COUNT];
        private readonly EntityID[] _ids = new EntityID[SLOT_COUNT];
        private readonly Vector3[] _positions = new Vector3[SLOT_COUNT];
        private readonly short[] _facing = new short[SLOT_COUNT];
        private readonly float[] _lastValidSpeed = new float[SLOT_COUNT];
        private readonly bool[] _speedFailing = new bool[SLOT_COUNT];
        private readonly bool[] _prohibited = new bool[SLOT_COUNT];
        private readonly bool[] _hasOverride = new bool[SLOT_COUNT];
        private readonly Vector3[] _overrideDir = new Vector3[SLOT_COUNT];
        private readonly int[] _stallCounter = new int[SLOT_COUNT];
        private readonly bool[] _stuckReported = new bool[SLOT_COUNT];
        private readonly bool[] _overflowWarned = new bool[SLOT_COUNT];

        private bool _navMeshAvailable = true;
        private bool _unavailableLogged;

        /// <summary>Creates the service. All arguments are required.</summary>
        /// <param name="intents">Per-slot intent source (Story 004 buffer).</param>
        /// <param name="speeds">Effective-speed source (F-MOV-2).</param>
        /// <param name="sampler">NavMesh validation seam.</param>
        /// <param name="slotIndex">EntityID to slot mapping for the read API.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public MovementService(IMovementIntentSource intents, IMovementSpeedSource speeds, INavMeshSampler sampler,
            IEntitySlotIndex slotIndex)
            : this(intents, speeds, sampler, slotIndex, MovementTuningValues.Defaults,
                new LoggingMovementGeometryErrorSink())
        {
        }

        /// <summary>Creates the service with explicit tuning and a geometry-error sink (Story 007). All arguments are required.</summary>
        /// <param name="intents">Per-slot intent source (Story 004 buffer).</param>
        /// <param name="speeds">Effective-speed source (F-MOV-2).</param>
        /// <param name="sampler">NavMesh validation seam.</param>
        /// <param name="slotIndex">EntityID to slot mapping for the read API.</param>
        /// <param name="tuning">Tuning values; <c>NavMeshStallRecoveryTicks</c> (TK-MOV-6) is read here.</param>
        /// <param name="geometryErrors">Receives the unrecoverable-stall report (EC-MOV-11).</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tuning"/> has a stall threshold below 1 (for example <c>default</c>).</exception>
        public MovementService(IMovementIntentSource intents, IMovementSpeedSource speeds, INavMeshSampler sampler,
            IEntitySlotIndex slotIndex, MovementTuningValues tuning, IMovementGeometryErrorSink geometryErrors)
        {
            _intents = intents ?? throw new ArgumentNullException(nameof(intents));
            _speeds = speeds ?? throw new ArgumentNullException(nameof(speeds));
            _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            _slotIndex = slotIndex ?? throw new ArgumentNullException(nameof(slotIndex));
            _geometryErrors = geometryErrors ?? throw new ArgumentNullException(nameof(geometryErrors));

            // Also rejects default(MovementTuningValues), whose threshold is 0 (it would recover on every tick).
            if (tuning.NavMeshStallRecoveryTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(tuning),
                    "NavMeshStallRecoveryTicks must be at least 1; build tuning with MovementTuningValues.Defaults or Create");
            _stallRecoveryTicks = tuning.NavMeshStallRecoveryTicks;
        }

        /// <summary>Runs one movement tick for every occupied player slot.</summary>
        /// <param name="currentTick">The tick being run.</param>
        /// <param name="deltaTime">Tick delta in seconds.</param>
        public void Run(uint currentTick, float deltaTime)
        {
            for (int slot = 0; slot < SLOT_COUNT; slot++)
            {
                if (_occupied[slot])
                    StepSlot(slot, deltaTime);
            }
        }

        /// <summary>
        /// Sets whether the zone NavMesh is usable (movement-system.md EC-MOV-12 defensive branch). Default true.
        /// While false each tick still takes the intent and clears any facing override, but applies nothing, and one
        /// error is logged on the first rejected tick; setting true re-arms that log. Story 009 drives this from
        /// <c>NavMeshDataInstance.valid</c> at startup step S6.
        /// </summary>
        /// <param name="available">Whether the NavMesh can be queried.</param>
        public void SetNavMeshAvailable(bool available)
        {
            _navMeshAvailable = available;
            if (available)
                _unavailableLogged = false;
        }

        /// <summary>Reads the committed position of a registered player.</summary>
        /// <param name="id">The entity.</param>
        /// <param name="position">The committed position when found.</param>
        /// <returns>False (no log) when the entity has no slot or is not registered with movement.</returns>
        public bool TryGetPosition(EntityID id, out Vector3 position)
        {
            if (TryFindSlot(id, out int slot))
            {
                position = _positions[slot];
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>Reads the stored facing (degrees x 10, the wire form) of a registered player.</summary>
        /// <param name="id">The entity.</param>
        /// <param name="facing">The facing when found.</param>
        /// <returns>False (no log) when the entity has no slot or is not registered with movement.</returns>
        public bool TryGetFacing(EntityID id, out short facing)
        {
            if (TryFindSlot(id, out int slot))
            {
                facing = _facing[slot];
                return true;
            }

            facing = 0;
            return false;
        }

        /// <summary>
        /// Registers a player in a slot. Story 008 replaces this with real entry handling. Resets the last valid
        /// speed to 0f (none) and resets all per-slot state: the speed-failing flag, Prohibited, the pending override
        /// and the Story 007 stall counter, reported flag and overflow-warned flag.
        /// </summary>
        internal void SeedForTesting(int slot, EntityID id, Vector3 position, short facing)
        {
            _occupied[slot] = true;
            _ids[slot] = id;
            _positions[slot] = position;
            _facing[slot] = facing;
            _lastValidSpeed[slot] = 0f;
            _speedFailing[slot] = false;
            _prohibited[slot] = false;
            _hasOverride[slot] = false;
            _overrideDir[slot] = Vector3.zero;
            _stallCounter[slot] = 0;
            _stuckReported[slot] = false;
            _overflowWarned[slot] = false;
        }

        /// <inheritdoc/>
        public bool SetProhibited(EntityID id, bool prohibited)
        {
            if (!TryFindSlot(id, out int slot))
                return false;

            // CR-MOV-8: entry and exit leave the override state alone (no auto-face re-arm).
            _prohibited[slot] = prohibited;
            return true;
        }

        /// <inheritdoc/>
        public bool InjectFacingOverride(EntityID id, Vector3 direction)
        {
            if (!TryFindSlot(id, out int slot))
                return false;

            // Raw direction stored; validated when consumed. A second call before the tick replaces the first.
            _overrideDir[slot] = direction;
            _hasOverride[slot] = true;
            return true;
        }

        private bool TryFindSlot(EntityID id, out int slot)
        {
            // The id check guards against a slot the pool has reassigned to another entity before movement re-seeds it.
            if (_slotIndex.TryGetSlotIndex(id, out slot) && slot >= 0 && slot < SLOT_COUNT && _occupied[slot]
                && _ids[slot].Equals(id))
                return true;

            slot = -1;
            return false;
        }

        private void StepSlot(int slot, float dt)
        {
            // (1) to (3): take the intent and the override. Always taken so both clear, even while Prohibited or
            // with the NavMesh unavailable.
            TakeInput(slot, out Vector2 dir, out bool hasFacing, out short facing);

            // NavMesh unavailable (EC-MOV-12): nothing is applied.
            if (!_navMeshAvailable)
            {
                LogUnavailableOnce();
                return;
            }

            if (hasFacing)
                _facing[slot] = facing;

            // (3b) Prohibited (CR-MOV-8): facing above still applies; no speed read, no integrate, no sample.
            if (_prohibited[slot])
                return;

            // EC-MOV-11: the first stepped tick at the stall threshold recovers instead of moving, whatever the input.
            if (_stallCounter[slot] >= _stallRecoveryTicks && !_stuckReported[slot])
            {
                RecoverSlot(slot);
                return;
            }

            TryMove(slot, dir, dt);
        }

        // (1) take the intent, or zero input; (2) its facing is stored as received, even with a zero direction
        // (CR-MOV-10); (3) auto-face override (CR-MOV-9): consumed whatever the outcome, beats the client intent this
        // tick. A zero-direction override moves nothing and applies no facing this tick.
        private void TakeInput(int slot, out Vector2 dir, out bool hasFacing, out short facing)
        {
            dir = Vector2.zero;
            hasFacing = false;
            facing = 0;
            if (_intents.TryTakeIntent(slot, out MovementIntentMessage intent))
            {
                facing = intent.FacingAngle;
                hasFacing = true;
                dir = MovementIntentMessageCodec.DecodeDirection(intent.DirX, intent.DirZ);
            }

            if (_hasOverride[slot])
            {
                _hasOverride[slot] = false;
                hasFacing = TryResolveOverride(_overrideDir[slot], out dir, out facing);
            }
        }

        private void LogUnavailableOnce()
        {
            if (_unavailableLogged)
                return;

            _unavailableLogged = true;
            Debug.LogError($"{LOG_PREFIX} NavMesh unavailable; movement ticks are rejected until it is set available");
        }

        // (4) to (6): integrate, validate against the NavMesh and commit one normal move.
        private void TryMove(int slot, Vector2 dir, float dt)
        {
            // (4) zero direction: stop. No speed read, no integrate, no sample (F-MOV-1 guard).
            if (dir.x == 0f && dir.y == 0f)
                return;

            if (!TryResolveSpeed(slot, out float vEff))
                return;

            Vector3 candidate = MovementIntegrator.Integrate(_positions[slot], new Vector3(dir.x, 0f, dir.y), vEff, dt);

            // A non-finite candidate never reaches the NavMesh query (engine behavior unverified); hold position.
            if (!float.IsFinite(candidate.x) || !float.IsFinite(candidate.y) || !float.IsFinite(candidate.z))
                return;

            // (5) validate and (6) commit. A miss keeps the previous position and logs nothing (CR-MOV-7).
            // A miss or a guard-rejected hit adds to the stall counter; a committed hit clears it.
            if (!_sampler.TrySample(candidate, NAVMESH_SAMPLE_RADIUS, out Vector3 hit) || !TryCommit(slot, hit))
            {
                if (_stallCounter[slot] < int.MaxValue)
                    _stallCounter[slot]++;
            }
        }

        // EC-MOV-11 recovery: sample the committed position at the wide radius. Failure reports once per stall.
        private void RecoverSlot(int slot)
        {
            Vector3 from = _positions[slot];
            if (_sampler.TrySample(from, NAVMESH_RECOVERY_SEARCH_RADIUS, out Vector3 hit) && TryCommit(slot, hit))
            {
                Debug.LogWarning($"{LOG_PREFIX} NavMesh stall recovery for entity {_ids[slot].RawValue}: moved from {from} to {hit}");
                return;
            }

            _stuckReported[slot] = true;
            _geometryErrors.ReportStuck(_ids[slot], from);
        }

        // F-MOV-6: commits the value only if it can be wire-encoded. A commit clears the stall state.
        private bool TryCommit(int slot, Vector3 value)
        {
            if (!MovementPositionGuard.IsEncodable(value))
            {
                if (!_overflowWarned[slot])
                {
                    _overflowWarned[slot] = true;
                    Debug.LogWarning($"{LOG_PREFIX} MovementEncodeOverflow: entity {_ids[slot].RawValue} position {value} is outside the encodable range; holding the previous position");
                }

                return false;
            }

            _positions[slot] = value;
            _stallCounter[slot] = 0;
            _stuckReported[slot] = false;
            _overflowWarned[slot] = false;
            return true;
        }

        // F-MOV-2 read; logs one error on the valid-to-failing transition only. False = no movement this tick.
        private bool TryResolveSpeed(int slot, out float vEff)
        {
            bool usable = MovementSpeedResolver.ResolveCore(_speeds, _ids[slot], ref _lastValidSpeed[slot],
                out vEff, out MovementSpeedResolver.FailureReason reason, out float badValue, out Exception failure);
            if (reason == MovementSpeedResolver.FailureReason.None)
            {
                _speedFailing[slot] = false;
            }
            else if (!_speedFailing[slot])
            {
                _speedFailing[slot] = true;
                Debug.LogError(MovementSpeedResolver.DescribeFailure(_ids[slot], reason, badValue, failure,
                    _lastValidSpeed[slot]));
            }

            return usable;
        }

        // False (dir zero, no facing) for a zero XZ part or any non-finite component.
        private static bool TryResolveOverride(Vector3 o, out Vector2 dir, out short facing)
        {
            dir = Vector2.zero;
            facing = 0;
            if (!float.IsFinite(o.x) || !float.IsFinite(o.y) || !float.IsFinite(o.z))
                return false;

            // Scaled by the largest XZ component first so a huge or tiny finite direction neither overflows
            // nor underflows when squared.
            float scale = Mathf.Max(Mathf.Abs(o.x), Mathf.Abs(o.z));
            if (scale == 0f)
                return false;

            float x = o.x / scale;
            float z = o.z / scale;
            float length = Mathf.Sqrt(x * x + z * z);
            dir = new Vector2(x / length, z / length);

            // Unity yaw: 0 = +Z, clockwise positive, degrees x 10 (wire form).
            facing = MovementIntentMessageCodec.NormalizeFacing(
                (short)Mathf.RoundToInt(Mathf.Atan2(o.x, o.z) * 1800f / Mathf.PI));
            return true;
        }
    }
}
