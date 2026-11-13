using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Per-mob server-side behaviour controller (Enemy AI Story 003: states, spawn init, dead check, facing).
    /// Holds no Unity objects; all inputs are injected seams. Per-state behaviour is filled in by Stories 004 to 006.
    /// </summary>
    public sealed class MobController
    {
        /// <summary>Largest valid stored facing value (degrees x 10).</summary>
        internal const int MAX_FACING = 3599;

        /// <summary>Squared XZ distance below which a bearing is treated as zero length.</summary>
        internal const float ZERO_BEARING_SQR_EPSILON = 1e-6f;

        private readonly INavigationProvider _navigation;
        private readonly IZoneEntityStatReader _stats;
        private readonly HitDetector _hitDetector;
        private readonly EnemyAIConfigValues _config;
        private readonly IAggroCandidateSource _candidates;
        private readonly List<(EntityID Id, Vector3 Position)> _scanBuffer = new List<(EntityID Id, Vector3 Position)>();
        private int _ticksUntilScan;
        private Vector3 _lastIssuedDestination;

        /// <summary>Creates a controller for one mob. It starts Dormant.</summary>
        /// <param name="entityID">The mob's entity id.</param>
        /// <param name="definition">The mob's static definition. Must not be null.</param>
        /// <param name="spawnPoint">World-space spawn point.</param>
        /// <param name="navigation">Navigation seam. Must not be null.</param>
        /// <param name="stats">Stat reader (HP for CR-AI-13). Must not be null.</param>
        /// <param name="hitDetector">Hit detector, used by the attack cycle (Story 005). Must not be null.</param>
        /// <param name="config">Enemy AI knobs (scan interval, reposition threshold). A struct: build it with
        /// <see cref="EnemyAIConfigValues.Defaults"/>, <see cref="EnemyAIConfigValues.Create"/> or
        /// <see cref="EnemyAIConfigValues.FromAsset"/>; <c>default(EnemyAIConfigValues)</c> is rejected.</param>
        /// <param name="candidates">Player seam for the aggro scan and pursuit. Must not be null.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The scan interval is below 1 or the reposition threshold is not greater than 0 (or is NaN).</exception>
        public MobController(
            EntityID entityID,
            MobDefinition definition,
            Vector3 spawnPoint,
            INavigationProvider navigation,
            IZoneEntityStatReader stats,
            HitDetector hitDetector,
            EnemyAIConfigValues config,
            IAggroCandidateSource candidates)
        {
            EntityID = entityID;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SpawnPoint = spawnPoint;
            Position = spawnPoint;
            _config = config;
            if (config.AggroScanIntervalTicks < 1 || !(config.PursuitRepositionThreshold > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(config),
                    "Invalid EnemyAIConfigValues (scan interval < 1 or reposition threshold not > 0); "
                    + "build it via EnemyAIConfigValues.Defaults, Create or FromAsset.");
            }

            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _hitDetector = hitDetector ?? throw new ArgumentNullException(nameof(hitDetector));
            DeadEnteredTick = -1;
            EnterState(MobState.Dormant, 0);
        }

        /// <summary>The mob's entity id.</summary>
        public EntityID EntityID { get; }

        /// <summary>The mob's static definition.</summary>
        public MobDefinition Definition { get; }

        /// <summary>World-space spawn point.</summary>
        public Vector3 SpawnPoint { get; }

        /// <summary>Mob world position; starts at <see cref="SpawnPoint"/>. Story 009 integrates movement into it.</summary>
        public Vector3 Position { get; internal set; }

        /// <summary>Current behaviour state.</summary>
        internal MobState State { get; private set; }

        /// <summary>CR-AI-14: a return to spawn is pending. Cleared on Dormant entry and only there.</summary>
        internal bool PendingReturn { get; set; }

        /// <summary>The current target, or null when there is none.</summary>
        internal EntityID? TargetEntityID { get; set; }

        /// <summary>Forward vector captured at attack start (Story 005).</summary>
        internal Vector3 CapturedForward { get; set; }

        /// <summary>True once the mob has started an attack since acquiring its target.</summary>
        internal bool HasAttackedOnce { get; set; }

        /// <summary>Tick of the last attack execution; -1 when none.</summary>
        internal long LastAttackExecutionTick { get; set; } = -1;

        /// <summary>True when the mob spawned Enraged.</summary>
        internal bool IsEnraged { get; set; }

        /// <summary>Facing in degrees x 10, in [0, 3599]; 0 = +Z, clockwise from above.</summary>
        internal int FacingAngle { get; private set; }

        /// <summary>Tick the mob entered Dead; -1 until then.</summary>
        internal long DeadEnteredTick { get; private set; }

        /// <summary>How many times the Dead-entry routine ran (exactly 1 once dead).</summary>
        internal int DeadEntryCount { get; private set; }

        /// <summary>Navigation seam, for the per-state stories.</summary>
        internal INavigationProvider Navigation => _navigation;

        /// <summary>Hit detector, for the attack cycle (Story 005).</summary>
        internal HitDetector HitDetector => _hitDetector;

        /// <summary>
        /// Advances the mob one tick. CR-AI-13: a mob with HP at or below 0 (or NaN HP) becomes Dead and nothing
        /// else runs that tick. Dead is terminal: a Dead mob is never ticked again, even if its HP rises above 0.
        /// </summary>
        /// <param name="currentTick">The current simulation tick.</param>
        public void Tick(uint currentTick)
        {
            if (State == MobState.Dead)
            {
                return;
            }

            float hp = _stats.GetCurrentHP(EntityID);
            if (hp <= 0f || float.IsNaN(hp))
            {
                EnterState(MobState.Dead, currentTick);
                return;
            }

            switch (State)
            {
                case MobState.Dormant: TickDormant(currentTick); break;
                case MobState.Pursuing: TickPursuing(currentTick); break;
                case MobState.WindingUp: TickWindingUp(currentTick); break;
                case MobState.AttackExecution: TickAttackExecution(currentTick); break;
                case MobState.Recovering: TickRecovering(currentTick); break;
                case MobState.Returning: TickReturning(currentTick); break;
            }
        }

        /// <summary>Test hook: forces a state through the real entry routine.</summary>
        /// <param name="state">The state to enter.</param>
        /// <param name="currentTick">Tick recorded if the state is Dead.</param>
        internal void ForceStateForTest(MobState state, uint currentTick = 0)
        {
            EnterState(state, currentTick);
        }

        /// <summary>
        /// Single state-entry routine. Dormant entry clears <see cref="PendingReturn"/> (CR-AI-14).
        /// Dead is terminal: once Dead, every call is a no-op, so the Dead-entry routine runs exactly once.
        /// </summary>
        /// <param name="state">The state to enter.</param>
        /// <param name="currentTick">The current tick.</param>
        internal void EnterState(MobState state, uint currentTick)
        {
            if (State == MobState.Dead && DeadEntryCount > 0)
            {
                return;
            }

            State = state;
            if (state == MobState.Dormant)
            {
                PendingReturn = false;
                _ticksUntilScan = _config.AggroScanIntervalTicks;
            }
            else if (state == MobState.Dead)
            {
                OnDeadEntered(currentTick);
            }
        }

        /// <summary>Dead-entry hook (Story 011 fills it). Records the tick and counts the entry.</summary>
        private void OnDeadEntered(uint currentTick)
        {
            DeadEnteredTick = currentTick;
            DeadEntryCount++;
        }

        /// <summary>
        /// Guard for every write to <see cref="FacingAngle"/>: clamps to [0, 3599] and logs one error per bad write
        /// (CR-AI-7 last sentence, stored x10 unit).
        /// </summary>
        /// <param name="value">Facing in degrees x 10.</param>
        internal void WriteFacing(int value)
        {
            if (value < 0 || value > MAX_FACING)
            {
                Debug.LogError($"MobController: facing {value} outside [0, {MAX_FACING}] for mob {EntityID.RawValue}; clamped.");
                value = value < 0 ? 0 : MAX_FACING;
            }

            FacingAngle = value;
        }

        /// <summary>
        /// F-AI-3: converts a stored facing (degrees x 10) to a unit forward vector on the XZ plane.
        /// Takes <c>short</c> (the wire/EntityState unit); callers holding <see cref="FacingAngle"/> (an <c>int</c>
        /// guarded to [0, 3599]) cast with <c>(short)FacingAngle</c>, which cannot overflow.
        /// </summary>
        /// <param name="facing">Facing, 0 = +Z, clockwise from above.</param>
        /// <returns>Unit forward vector with y = 0.</returns>
        internal static Vector3 FacingToForward(short facing)
        {
            float rad = facing * 0.1f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>CR-AI-5: facing from <paramref name="from"/> toward <paramref name="to"/>, XZ only, wrapped to [0, 3599].</summary>
        /// <param name="from">Origin.</param>
        /// <param name="to">Target.</param>
        /// <param name="previous">Returned when the XZ bearing has zero length.</param>
        /// <returns>The facing, degrees x 10.</returns>
        internal static int BearingToFacing(Vector3 from, Vector3 to, int previous)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            if (dx * dx + dz * dz < ZERO_BEARING_SQR_EPSILON)
            {
                return previous;
            }

            int raw = Mathf.RoundToInt(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg * 10f);
            return MovementIntentMessageCodec.NormalizeFacing((short)raw);
        }

        /// <summary>
        /// CR-AI-3: counts down and scans for a target every AGGRO_SCAN_INTERVAL_TICKS Dormant ticks.
        /// F-AI-2: XZ squared distance, inclusive; the first player in list order wins (AC-AI-2); HP at or below 0 or NaN is skipped.
        /// Players with a non-finite position are skipped. HP is read only for in-range candidates.
        /// On a hit the mob enters Pursuing, writes facing toward the target on that same tick (CR-AI-5) and issues
        /// one SetDestination (never Resume, ADR-003).
        /// </summary>
        private void TickDormant(uint currentTick)
        {
            _ticksUntilScan--;
            if (_ticksUntilScan > 0)
            {
                return;
            }

            _ticksUntilScan = _config.AggroScanIntervalTicks;
            _scanBuffer.Clear();
            _candidates.CopyPlayers(_scanBuffer);
            float range = Definition.AggroRange;
            float rangeSqr = range * range;
            for (int i = 0; i < _scanBuffer.Count; i++)
            {
                (EntityID id, Vector3 pos) = _scanBuffer[i];
                if (!IsFiniteXZ(pos))
                {
                    continue;
                }

                float dx = pos.x - Position.x;
                float dz = pos.z - Position.z;
                if (!(dx * dx + dz * dz <= rangeSqr))
                {
                    continue;
                }

                float hp = _stats.GetCurrentHP(id);
                if (hp <= 0f || float.IsNaN(hp))
                {
                    continue;
                }

                TargetEntityID = id;
                EnterState(MobState.Pursuing, currentTick);
                WriteFacing(BearingToFacing(Position, pos, FacingAngle));
                IssueDestination(pos);
                return;
            }
        }

        /// <summary>
        /// All SetDestination calls go through here so the reposition basis is never stale. Story 005/006 must use it.
        /// </summary>
        /// <param name="destination">World-space destination.</param>
        private void IssueDestination(Vector3 destination)
        {
            _navigation.SetDestination(EntityID, destination);
            _lastIssuedDestination = destination;
        }

        private static bool IsFiniteXZ(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.z) || float.IsInfinity(v.z));
        }

        /// <summary>
        /// CR-AI-5 / CR-AI-11 (Pursuing): writes facing toward the target every tick, then re-issues SetDestination when
        /// the path is stale or the target moved more than PURSUIT_REPOSITION_THRESHOLD from the last issued destination (AC-AI-17, AC-AI-24).
        /// A target position that cannot be read, or is non-finite on X or Z, makes the tick a no-op (exit handled by Story 006).
        /// </summary>
        private void TickPursuing(uint currentTick)
        {
            if (!TargetEntityID.HasValue
                || !_candidates.TryGetPlayerPosition(TargetEntityID.Value, out Vector3 target))
            {
                return;
            }

            if (!IsFiniteXZ(target))
            {
                return;
            }

            WriteFacing(BearingToFacing(Position, target, FacingAngle));
            bool stale = _navigation.IsPathStale(EntityID);
            float dx = target.x - _lastIssuedDestination.x;
            float dz = target.z - _lastIssuedDestination.z;
            float threshold = _config.PursuitRepositionThreshold;
            if (stale || dx * dx + dz * dz > threshold * threshold)
            {
                IssueDestination(target);
            }
        }

        private void TickWindingUp(uint currentTick) { }

        private void TickAttackExecution(uint currentTick) { }

        private void TickRecovering(uint currentTick) { }

        private void TickReturning(uint currentTick) { }
    }
}
