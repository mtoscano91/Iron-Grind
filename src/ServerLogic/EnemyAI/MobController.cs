using System;
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

        /// <summary>Creates a controller for one mob. It starts Dormant.</summary>
        /// <param name="entityID">The mob's entity id.</param>
        /// <param name="definition">The mob's static definition. Must not be null.</param>
        /// <param name="spawnPoint">World-space spawn point.</param>
        /// <param name="navigation">Navigation seam. Must not be null.</param>
        /// <param name="stats">Stat reader (HP for CR-AI-13). Must not be null.</param>
        /// <param name="hitDetector">Hit detector, used by the attack cycle (Story 005). Must not be null.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public MobController(
            EntityID entityID,
            MobDefinition definition,
            Vector3 spawnPoint,
            INavigationProvider navigation,
            IZoneEntityStatReader stats,
            HitDetector hitDetector)
        {
            EntityID = entityID;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SpawnPoint = spawnPoint;
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

        private void TickDormant(uint currentTick) { }

        private void TickPursuing(uint currentTick) { }

        private void TickWindingUp(uint currentTick) { }

        private void TickAttackExecution(uint currentTick) { }

        private void TickRecovering(uint currentTick) { }

        private void TickReturning(uint currentTick) { }
    }
}
