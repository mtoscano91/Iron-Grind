using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Per-mob server-side behaviour controller (Enemy AI Stories 003 to 006: states, spawn init, dead check, facing,
    /// aggro and pursuit, attack cycle, leash, target invalidation and Returning). Holds no Unity objects; all inputs are injected seams.
    /// Dead handling is filled in by Story 011.
    /// </summary>
    public sealed class MobController
    {
        /// <summary>Largest valid stored facing value (degrees x 10).</summary>
        internal const int MAX_FACING = 3599;

        /// <summary>Squared XZ distance below which a bearing is treated as zero length.</summary>
        internal const float ZERO_BEARING_SQR_EPSILON = 1e-6f;

        private readonly INavigationProvider _navigation;
        private readonly IZoneEntityStatReader _stats;
        private readonly IMobAttackResolver _attackResolver;
        private readonly float _returnSpeedCap;
        private readonly EnemyAIConfigValues _config;
        private readonly IAggroCandidateSource _candidates;
        private readonly List<(EntityID Id, Vector3 Position)> _scanBuffer = new List<(EntityID Id, Vector3 Position)>();
        private int _ticksUntilScan;
        private int _windUpTicksRemaining;
        private int _recoveryTicksRemaining;
        private Vector3 _lastIssuedDestination;
        private long _lastIssueTick = -1;
        private bool _repathOnResume;
        private bool _pendingReturnRescanOnly;

        /// <summary>Creates a controller for one mob. It starts Dormant.</summary>
        /// <param name="entityID">The mob's entity id.</param>
        /// <param name="definition">The mob's static definition. Must not be null.</param>
        /// <param name="spawnPoint">World-space spawn point.</param>
        /// <param name="navigation">Navigation seam. Must not be null.</param>
        /// <param name="stats">Stat reader (HP for CR-AI-13). Must not be null.</param>
        /// <param name="attackResolver">Attack seam, called on AttackExecution (Story 005). Must not be null.</param>
        /// <param name="config">Enemy AI knobs (scan interval, reposition threshold). A struct: build it with
        /// <see cref="EnemyAIConfigValues.Defaults"/>, <see cref="EnemyAIConfigValues.Create"/> or
        /// <see cref="EnemyAIConfigValues.FromAsset"/>; <c>default(EnemyAIConfigValues)</c> is rejected.</param>
        /// <param name="returnSpeedCap">RETURN_SPEED_CAP in m/s (owned by Navigation), applied on Returning entry. Must be finite and greater than 0.</param>
        /// <param name="candidates">Player seam for the aggro scan and pursuit. Must not be null.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The scan interval is below 1, the reposition threshold is not greater than 0 (or is NaN), or <paramref name="returnSpeedCap"/> is not finite and greater than 0.</exception>
        public MobController(
            EntityID entityID,
            MobDefinition definition,
            Vector3 spawnPoint,
            INavigationProvider navigation,
            IZoneEntityStatReader stats,
            IMobAttackResolver attackResolver,
            EnemyAIConfigValues config,
            float returnSpeedCap,
            IAggroCandidateSource candidates)
        {
            EntityID = entityID;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SpawnPoint = spawnPoint;
            Position = spawnPoint;
            _config = config;
            if (!(returnSpeedCap > 0f) || float.IsInfinity(returnSpeedCap))
            {
                throw new ArgumentOutOfRangeException(nameof(returnSpeedCap), "RETURN_SPEED_CAP must be finite and greater than 0.");
            }

            _returnSpeedCap = returnSpeedCap;
            if (config.AggroScanIntervalTicks < 1 || !(config.PursuitRepositionThreshold > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(config),
                    "Invalid EnemyAIConfigValues (scan interval < 1 or reposition threshold not > 0); "
                    + "build it via EnemyAIConfigValues.Defaults, Create or FromAsset.");
            }

            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _attackResolver = attackResolver ?? throw new ArgumentNullException(nameof(attackResolver));
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

        /// <summary>CR-AI-14: a return to spawn is pending. Cleared on Dormant entry, and (documented deviation, AC-AI-12b) when a re-scan after target death succeeds at Recovering exit.</summary>
        internal bool PendingReturn { get; set; }

        /// <summary>The current target, or null when there is none.</summary>
        internal EntityID? TargetEntityID { get; set; }

        /// <summary>Forward vector captured at wind-up start (Story 005, CR-AI-4).</summary>
        internal Vector3 CapturedForward { get; set; }

        /// <summary>True once the mob has executed an attack since acquiring its target (set on AttackExecution, CR-AI-6).</summary>
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

        /// <summary>
        /// Test hook: forces a state through <see cref="EnterState"/> only. It does NOT run the attack-cycle entry work:
        /// a forced WindingUp has no wind-up counter (it resolves on its first tick), no captured forward and no Stop;
        /// a forced Recovering has a 0 counter (it exits on its first tick). Drive real transitions for those states.
        /// </summary>
        /// <param name="state">The state to enter.</param>
        /// <param name="currentTick">Tick recorded if the state is Dead.</param>
        internal void ForceStateForTest(MobState state, uint currentTick = 0)
        {
            EnterState(state, currentTick);
        }

        /// <summary>
        /// Single state-entry routine. Dormant entry clears <see cref="PendingReturn"/> (CR-AI-14), the target, the cooldown state
        /// (<see cref="HasAttackedOnce"/>, <see cref="LastAttackExecutionTick"/>) and the re-scan-only flag, and restarts the
        /// aggro scan countdown (the first scan runs AGGRO_SCAN_INTERVAL_TICKS Dormant ticks later).
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
                _pendingReturnRescanOnly = false;
                TargetEntityID = null;
                HasAttackedOnce = false;
                LastAttackExecutionTick = -1;
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
        /// one SetDestination (never Resume, ADR-003). EC-AI-3: if the target is already within attack range the
        /// attack check then runs on the same tick and the mob enters WindingUp (Stop after SetDestination).
        /// </summary>
        private void TickDormant(uint currentTick)
        {
            _ticksUntilScan--;
            if (_ticksUntilScan > 0)
            {
                return;
            }

            _ticksUntilScan = _config.AggroScanIntervalTicks;
            if (TryFindAggroTarget(out EntityID id, out Vector3 pos))
            {
                OnAggroAcquired(id, pos, currentTick);
            }
        }

        /// <summary>
        /// F-AI-2 scan: the first player in list order with a finite XZ position within AggroRange (inclusive) and HP above 0.
        /// HP is read only for in-range candidates.
        /// </summary>
        private bool TryFindAggroTarget(out EntityID id, out Vector3 pos)
        {
            _scanBuffer.Clear();
            _candidates.CopyPlayers(_scanBuffer);
            float range = Definition.AggroRange;
            float rangeSqr = range * range;
            for (int i = 0; i < _scanBuffer.Count; i++)
            {
                (id, pos) = _scanBuffer[i];
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

                return true;
            }

            id = default;
            pos = default;
            return false;
        }

        /// <summary>
        /// Aggro entry: Pursuing, facing toward the target (CR-AI-5), one SetDestination (never Resume, ADR-003), then
        /// the EC-AI-3 same-tick attack check (WindingUp, Stop after SetDestination).
        /// </summary>
        private void OnAggroAcquired(EntityID id, Vector3 pos, uint currentTick)
        {
            TargetEntityID = id;
            EnterState(MobState.Pursuing, currentTick);
            WriteFacing(BearingToFacing(Position, pos, FacingAngle));
            IssueDestination(pos, currentTick);
            if (AttackConditionMet(currentTick, pos))
            {
                EnterWindingUp(currentTick);
            }
        }

        /// <summary>
        /// All SetDestination calls go through here so the reposition basis is never stale and a same-tick Stop can be
        /// detected. Pursuit (including the re-path after a target change), aggro entry and Returning entry (spawn point) all use it.
        /// </summary>
        /// <param name="destination">World-space destination.</param>
        /// <param name="currentTick">The current tick.</param>
        private void IssueDestination(Vector3 destination, uint currentTick)
        {
            _navigation.SetDestination(EntityID, destination);
            _lastIssuedDestination = destination;
            _lastIssueTick = currentTick;
            _repathOnResume = false;
        }

        private static bool IsFiniteXZ(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.z) || float.IsInfinity(v.z));
        }

        /// <summary>Reads the current target position; false when there is no target, it is unreadable or non-finite on X or Z.</summary>
        private bool TryReadTarget(out Vector3 target)
        {
            if (!TargetEntityID.HasValue
                || !_candidates.TryGetPlayerPosition(TargetEntityID.Value, out target))
            {
                target = Vector3.zero;
                return false;
            }

            return IsFiniteXZ(target);
        }

        /// <summary>XZ squared distance from <see cref="Position"/> to <paramref name="target"/> is at most AttackRange squared (inclusive, Y ignored).</summary>
        private bool InAttackRange(Vector3 target)
        {
            float dx = target.x - Position.x;
            float dz = target.z - Position.z;
            float range = Definition.AttackRange;
            return dx * dx + dz * dz <= range * range;
        }

        /// <summary>CR-AI-6 cooldown gate: the first attack is ungated; later ones need AttackCooldownTicks since the last execution.</summary>
        private bool CooldownElapsed(uint currentTick)
        {
            return !HasAttackedOnce
                || (long)currentTick - LastAttackExecutionTick >= Definition.AttackCooldownTicks;
        }

        private bool AttackConditionMet(uint currentTick, Vector3 target)
        {
            return InAttackRange(target) && CooldownElapsed(currentTick);
        }

        /// <summary>
        /// Shared WindingUp entry (from Pursuing, the aggro tick and Recovering): arms the counter, captures the forward
        /// vector from the current facing (CR-AI-4) and stops the agent once (ADR-003: Stop keeps the path).
        /// Stop discards a destination requested but not yet issued (INavigationProvider.Stop), so a SetDestination on
        /// this same tick (EC-AI-3 aggro tick) is lost: the next reposition check then re-paths.
        /// </summary>
        private void EnterWindingUp(uint currentTick)
        {
            EnterState(MobState.WindingUp, currentTick);
            _windUpTicksRemaining = Definition.AttackWindUpTicks;
            CapturedForward = FacingToForward((short)FacingAngle);
            if (_lastIssueTick == currentTick)
            {
                _repathOnResume = true;
            }

            _navigation.Stop(EntityID);
        }

        /// <summary>
        /// Story 004 reposition check: polls IsPathStale once and re-issues SetDestination when stale, when a request was
        /// discarded by a same-tick Stop, when the target changed since the last issue (Story 006, AC-AI-26: the threshold
        /// is bypassed), or when the target is more than PURSUIT_REPOSITION_THRESHOLD from the last issued destination
        /// (AC-AI-17, AC-AI-24; Story 005 adds the threshold test on Pursuing re-entry).
        /// </summary>
        private void RepositionIfNeeded(Vector3 target, uint currentTick)
        {
            bool stale = _navigation.IsPathStale(EntityID);
            float dx = target.x - _lastIssuedDestination.x;
            float dz = target.z - _lastIssuedDestination.z;
            float threshold = _config.PursuitRepositionThreshold;
            if (stale || _repathOnResume || dx * dx + dz * dz > threshold * threshold)
            {
                IssueDestination(target, currentTick);
            }
        }

        /// <summary>Result of the per-tick leash and target-validity evaluation (Story 006 Tick Model section 1).</summary>
        private enum TargetCheck
        {
            /// <summary>Leash intact and the target is valid and alive.</summary>
            Ok,

            /// <summary>The mob is beyond LeashRange from its spawn point (F-AI-4).</summary>
            Leash,

            /// <summary>Standard invalidation: no target, unreadable, non-finite or outside the leash from spawn.</summary>
            Invalid,

            /// <summary>The target is alive in the zone but its HP is 0 or NaN (CR-AI-10).</summary>
            Dead
        }

        /// <summary>F-AI-4: XZ squared distance from <see cref="Position"/> to <see cref="SpawnPoint"/> is above LeashRange squared (equal is not a breach).</summary>
        private bool LeashBreached()
        {
            float dx = Position.x - SpawnPoint.x;
            float dz = Position.z - SpawnPoint.z;
            float range = Definition.LeashRange;
            return dx * dx + dz * dz > range * range;
        }

        /// <summary>
        /// Target validity (Tick Model section 1, step 2): unreadable or non-finite is <see cref="TargetCheck.Invalid"/>, a target beyond
        /// LeashRange from the spawn point is <see cref="TargetCheck.Invalid"/>, HP at or below 0 or NaN is <see cref="TargetCheck.Dead"/>.
        /// </summary>
        private TargetCheck CheckTarget(out Vector3 target)
        {
            if (!TryReadTarget(out target))
            {
                return TargetCheck.Invalid;
            }

            float dx = target.x - SpawnPoint.x;
            float dz = target.z - SpawnPoint.z;
            float range = Definition.LeashRange;
            if (dx * dx + dz * dz > range * range)
            {
                return TargetCheck.Invalid;
            }

            float hp = _stats.GetCurrentHP(TargetEntityID.Value);
            return (hp <= 0f || float.IsNaN(hp)) ? TargetCheck.Dead : TargetCheck.Ok;
        }

        /// <summary>
        /// Leash first (it wins), then target validity. Used outside the attack cycle. <paramref name="target"/> is only
        /// meaningful for <see cref="TargetCheck.Ok"/> and <see cref="TargetCheck.Dead"/>; on a leash result it is not read.
        /// </summary>
        private TargetCheck EvaluateLeashAndTarget(out Vector3 target)
        {
            if (LeashBreached())
            {
                target = Vector3.zero;
                return TargetCheck.Leash;
            }

            return CheckTarget(out target);
        }

        /// <summary>
        /// Attack-cycle hook (WindingUp start, tick X, AttackExecution): a leash breach or invalid or dead target sets
        /// <see cref="PendingReturn"/>; the cycle never exits early. Returns the target check so tick X can skip the resolver.
        /// </summary>
        private TargetCheck RunCycleHook(out Vector3 target)
        {
            bool leash = LeashBreached();
            TargetCheck check = CheckTarget(out target);
            if (leash || check == TargetCheck.Invalid)
            {
                NotePendingReturn(false);
            }
            else if (check == TargetCheck.Dead)
            {
                NotePendingReturn(true);
            }

            return check;
        }

        /// <summary>Sets <see cref="PendingReturn"/>; the re-scan-only flag stays true only while target death is the sole cause.</summary>
        private void NotePendingReturn(bool deathOnly)
        {
            if (!PendingReturn)
            {
                PendingReturn = true;
                _pendingReturnRescanOnly = deathOnly;
            }
            else if (!deathOnly)
            {
                _pendingReturnRescanOnly = false;
            }
        }

        /// <summary>
        /// Target change: new target, cooldown state reset so the new target does not inherit the old cooldown, and the next
        /// reposition check re-paths whatever the threshold (AC-AI-26: the new target can be within PURSUIT_REPOSITION_THRESHOLD
        /// of the old target's last destination). Covers both Pursuing and a swap in Recovering followed by the Pursuing exit.
        /// </summary>
        private void ChangeTarget(EntityID id)
        {
            TargetEntityID = id;
            HasAttackedOnce = false;
            LastAttackExecutionTick = -1;
            _repathOnResume = true;
        }

        /// <summary>CR-AI-10 re-scan (reuses the aggro scan, so the dead target is excluded by its HP). On success the target is changed.</summary>
        private bool TryRescan(out Vector3 pos)
        {
            if (TryFindAggroTarget(out EntityID id, out pos))
            {
                ChangeTarget(id);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returning entry (ADR-003): SetSpeed(RETURN_SPEED_CAP) then one SetDestination(spawn). No Resume (the nav drain clears
        /// IsStopped), no Stop, no facing write. The target and cooldown state are cleared. <see cref="PendingReturn"/> is NOT
        /// cleared here (CR-AI-14: Dormant entry only).
        /// </summary>
        private void EnterReturning(uint currentTick)
        {
            EnterState(MobState.Returning, currentTick);
            _navigation.SetSpeed(EntityID, _returnSpeedCap);
            IssueDestination(SpawnPoint, currentTick);
            TargetEntityID = null;
            HasAttackedOnce = false;
            LastAttackExecutionTick = -1;
        }

        /// <summary>
        /// CR-AI-5 / CR-AI-11 / CR-AI-6 / CR-AI-9 / CR-AI-10 (Pursuing). Leash breach or standard invalidation enters Returning on
        /// this tick. A dead target triggers an immediate re-scan: none found enters Returning; found changes the target and the
        /// rest of the tick runs against it (facing, attack check, then the reposition check, which re-paths toward the new
        /// target regardless of the threshold, AC-AI-26). Otherwise: facing toward the target, then WindingUp (attack condition
        /// met; no reposition that tick) or the reposition check.
        /// </summary>
        private void TickPursuing(uint currentTick)
        {
            TargetCheck check = EvaluateLeashAndTarget(out Vector3 target);
            if (check == TargetCheck.Leash || check == TargetCheck.Invalid)
            {
                EnterReturning(currentTick);
                return;
            }

            if (check == TargetCheck.Dead && !TryRescan(out target))
            {
                EnterReturning(currentTick);
                return;
            }

            WriteFacing(BearingToFacing(Position, target, FacingAngle));
            if (AttackConditionMet(currentTick, target))
            {
                EnterWindingUp(currentTick);
                return;
            }

            RepositionIfNeeded(target, currentTick);
        }

        /// <summary>
        /// Runs the attack-cycle hook first (leash / target validity set <see cref="PendingReturn"/>, never an early exit), then
        /// counts the wind-up down; on the tick it reaches 0 the mob enters AttackExecution and the attack resolves on that
        /// same tick (attack tick X) unless the target is invalid or dead. Facing is never written.
        /// </summary>
        private void TickWindingUp(uint currentTick)
        {
            TargetCheck check = RunCycleHook(out Vector3 target);
            _windUpTicksRemaining--;
            if (_windUpTicksRemaining > 0)
            {
                return;
            }

            EnterState(MobState.AttackExecution, currentTick);

            // Cooldown bookkeeping first, so a throwing resolver (Story 010) cannot leave the attack unrecorded.
            HasAttackedOnce = true;
            LastAttackExecutionTick = currentTick;

            // A leash breach alone does not skip the attack (AC-AI-9); an invalid or dead target does.
            if (check == TargetCheck.Ok)
            {
                _attackResolver.ResolveAttack(EntityID, TargetEntityID.Value, Position, target,
                    Definition.AttackRange, CapturedForward);
            }
        }

        /// <summary>Tick X+1: runs the cycle hook, enters Recovering and runs the first Recovering decrement-then-check in the same tick.</summary>
        private void TickAttackExecution(uint currentTick)
        {
            RunCycleHook(out _);
            EnterState(MobState.Recovering, currentTick);
            _recoveryTicksRemaining = Definition.AttackRecoveryTicks;
            TickRecovering(currentTick);
        }

        /// <summary>
        /// Every Recovering tick (including while counting down) runs the leash and target checks: leash breach or standard
        /// invalidation enters Returning. A dead target is re-scanned (none: Returning; found: the target is swapped, with no nav
        /// calls and the counter untouched). While <see cref="PendingReturn"/> is set, a dead target is held until the exit tick
        /// instead (CR-AI-10: "on Recovering exit, not immediately"). So the re-scan timing depends on where the death was first
        /// seen: during the cycle (the cycle hook set PendingReturn) it waits for the exit; first seen in Recovering with no
        /// pending return it runs at once. On the exit tick a pending return that is not death-only enters Returning without a
        /// re-scan. Decrement-then-check (EC-AI-8: R = 0 exits on the first Recovering tick); no facing writes or nav calls while
        /// counting down. On exit, <see cref="TickRecoveringExit"/> applies the pending return and the Story 005 exit rules.
        /// </summary>
        private void TickRecovering(uint currentTick)
        {
            TargetCheck check = EvaluateLeashAndTarget(out Vector3 target);
            if (check == TargetCheck.Leash || check == TargetCheck.Invalid)
            {
                EnterReturning(currentTick);
                return;
            }

            if (_recoveryTicksRemaining > 0)
            {
                _recoveryTicksRemaining--;
            }

            bool exiting = _recoveryTicksRemaining <= 0;
            if (check == TargetCheck.Dead)
            {
                if (PendingReturn && !exiting)
                {
                    return;
                }

                // A leash / invalidation cause already decides the exit: skip the wasted re-scan.
                if ((PendingReturn && !_pendingReturnRescanOnly) || !TryRescan(out target))
                {
                    EnterReturning(currentTick);
                    return;
                }
            }

            if (!exiting)
            {
                return;
            }

            TickRecoveringExit(currentTick, target);
        }

        /// <summary>
        /// Recovering exit with a valid, alive target (the per-tick checks already ran). A pending return that is not
        /// re-scan-only enters Returning. A re-scan-only pending return (the target died during the cycle) means the dead target
        /// was already replaced by a successful re-scan, or the original target has HP above 0 again (combat continues against
        /// it and its cooldown state is kept): <see cref="PendingReturn"/> and the re-scan-only flag are cleared here.
        /// This deviates from CR-AI-14 (which clears pendingReturn only on Dormant entry) because AC-AI-12b requires a
        /// successful re-scan to continue combat. Then the Story 005 exit rules: in range with the cooldown elapsed refreshes
        /// facing and re-enters WindingUp; otherwise Pursuing via Resume (CR-AI-11, ADR-003).
        /// </summary>
        private void TickRecoveringExit(uint currentTick, Vector3 target)
        {
            if (PendingReturn)
            {
                if (!_pendingReturnRescanOnly)
                {
                    EnterReturning(currentTick);
                    return;
                }

                PendingReturn = false;
                _pendingReturnRescanOnly = false;
            }

            if (AttackConditionMet(currentTick, target))
            {
                WriteFacing(BearingToFacing(Position, target, FacingAngle));
                EnterWindingUp(currentTick);
                return;
            }

            EnterState(MobState.Pursuing, currentTick);
            _navigation.Resume(EntityID);
            WriteFacing(BearingToFacing(Position, target, FacingAngle));
            RepositionIfNeeded(target, currentTick);
        }

        /// <summary>
        /// Returning: from the first tick after entry, arrival (XZ squared distance to spawn at most ReturnArrivalThreshold
        /// squared, inclusive) calls Stop, then SetSpeed(MoveSpeed), then enters Dormant. Nothing else runs: no aggro, no target
        /// checks, and a leash breach keeps the mob Returning.
        /// </summary>
        private void TickReturning(uint currentTick)
        {
            float dx = Position.x - SpawnPoint.x;
            float dz = Position.z - SpawnPoint.z;
            float threshold = _config.ReturnArrivalThreshold;
            if (dx * dx + dz * dz > threshold * threshold)
            {
                return;
            }

            _navigation.Stop(EntityID);
            _navigation.SetSpeed(EntityID, Definition.MoveSpeed);
            EnterState(MobState.Dormant, currentTick);
        }
    }
}
