using System;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.Events;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Per-player auto-attack state, toggle, target, action timer and Rule 10 resolution (design/gdd/auto-attack-combat.md
    /// Rules 1-4, 10, 12-15, 23, States and Transitions, EC-1 to EC-10, EC-17, EC-19, EC-21, EC-24, EC-27 to EC-30).
    /// Auto-Attack Combat Stories 001 to 005.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State.</b> Arrays of <see cref="ZoneSlotPool.MaxPlayers"/> entries indexed by the player slot. The state is
    /// not <see cref="AutoAttackState.Idle"/> only while a valid target is stored and the toggle is ON.
    /// The action timer counts whole ticks: +1 per <see cref="Run"/> while active, frozen while idle, 0 on every
    /// activation, target selection and fire (Rules 2, 3, 10).
    /// </para>
    /// <para>
    /// <b>Entry points.</b> <see cref="SetTarget"/>, <see cref="SetAutoAttack"/>, <see cref="SetCombatProhibited"/>,
    /// <see cref="NotifySkillUsed"/> and <see cref="ReleasePlayer"/>. An entity with no player slot is ignored by every
    /// method: no throw, no log. Story 006 wires them.
    /// </para>
    /// <para>
    /// <b>Cycle (Story 002, Rules 5-7, F-1).</b> Each slot holds a cycle length in ticks, sampled from
    /// <see cref="IAttackSpeedSource"/> whenever the timer is set to 0 and never mid-cycle. No clock is read;
    /// neither <c>deltaTime</c> nor <c>currentTick</c> affects timing.
    /// </para>
    /// <para>
    /// <b>Resolution (Story 003, Rule 10, F-2, F-3).</b> Once the timer reaches the cycle length: Step 1 a prohibited
    /// player does not fire; Step 2 <see cref="HitDetector.CheckHit"/> on the ground-plane positions, emitting every
    /// hit and each miss once per streak; Step 3 on a hit, damage is calculated and applied if it is above 0;
    /// Step 4 the timer resets, on any actual fire (even a rejected calculation). Any no-fire exit keeps the timer, so
    /// a ready auto retries every tick. A failed attacker or target position lookup is such a no-fire exit (like
    /// Step 5): nothing is emitted, the timer and state are kept, and it retries next tick; Story 006 wires validity
    /// and positions from the same zone state. The damage output (Story 007) is sent by the applier decorator <see cref="DamageOutputApplier"/>.
    /// </para>
    /// <para>
    /// <b>Kill sequence (Story 004, Rule 10 Step 3, TR-aa-004).</b> A result with <see cref="DamageResult.IsKill"/> and
    /// damage above 0 runs, by direct calls and in this order, <see cref="IAutoAttackXpAward.GetXPAward"/>,
    /// <see cref="IAutoAttackExperienceSink.AddExperience"/>, <see cref="IAutoAttackDamageApplier.Apply"/>; then
    /// <see cref="OnMobDied"/> is raised once (ADR-010 Decision 3; XP is not a subscriber). The mob type is read before
    /// the calls, since applying the damage may remove the mob. The attacker reaches its final state (idle, target
    /// cleared, timer frozen at 0, toggle unchanged) before the raise, so a subscriber that re-targets or releases the
    /// player acts on that state and is not overwritten. Each subscriber runs in its own try/catch: one that throws is
    /// logged and the others still run. A failed mob type lookup skips the raise and logs an error. An exception from
    /// the XP or damage seams is not caught: it leaves the XP granted and escapes <see cref="Run"/>.
    /// </para>
    /// <para>
    /// <b>Skill reset (Story 005, Rules 12-15, TR-aa-005).</b> <see cref="NotifySkillUsed"/> restarts an active player's
    /// cycle: timer 0, miss streak ended, cycle length re-sampled. An idle player only has the frozen timer zeroed. No
    /// resolved auto is reversed (EC-21), and no beat tracker is consulted: the reactive cooldown model replaces the
    /// OWL grace window.
    /// </para>
    /// <para>
    /// <b>Attacker id.</b> <see cref="Run"/> iterates slots, so each slot's <see cref="EntityID"/> is stored in an
    /// array when SetTarget or SetAutoAttack first touches it and cleared in <see cref="ReleasePlayer"/>
    /// (allocation-free; a slot that was never touched is always idle and never read).
    /// </para>
    /// <para>
    /// Tick thread only. Allocates nothing after construction.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var combat = new AutoAttackCombatSystem(pool, validity, attackSpeed, positions, hitDetector, emitter,
    ///     stats, damageCalculator, applier, xpAward, experienceSink, mobTypeSource, zoneId);
    /// combat.OnMobDied += OnMobDiedHandler;   // a named method, subscribed once
    /// combat.SetAutoAttack(playerId, true);
    /// combat.SetTarget(playerId, mobId);   // CombatActive, timer 0, cycle sampled
    /// combat.Run(tick, dt);                // timer 1; fires once the timer reaches GetCycleTicks and the target is in range
    /// </code>
    /// </example>
    public sealed class AutoAttackCombatSystem : IZoneSimulationStep, ICombatService, IAutoAttackSkillUseSink,
        IAutoAttackInputSink, IZoneSlotReleaseListener
    {
        /// <inheritdoc />
        /// <remarks>
        /// Subscribers are kept in an array rebuilt on add and remove (setup-time allocation only), so the raise
        /// allocates nothing and can isolate each subscriber. Removing a handler removes its last registration.
        /// </remarks>
        public event Action<MobDeathContext> OnMobDied
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                var next = new Action<MobDeathContext>[_mobDiedHandlers.Length + 1];
                Array.Copy(_mobDiedHandlers, next, _mobDiedHandlers.Length);
                next[_mobDiedHandlers.Length] = value;
                _mobDiedHandlers = next;
            }
            remove
            {
                int index = Array.LastIndexOf(_mobDiedHandlers, value);
                if (value == null || index < 0)
                {
                    return;
                }

                var next = new Action<MobDeathContext>[_mobDiedHandlers.Length - 1];
                Array.Copy(_mobDiedHandlers, 0, next, 0, index);
                Array.Copy(_mobDiedHandlers, index + 1, next, index, next.Length - index);
                _mobDiedHandlers = next;
            }
        }

        // Replaced, never mutated, on add/remove; the raise iterates a snapshot, so a subscriber may (un)subscribe.
        private Action<MobDeathContext>[] _mobDiedHandlers = Array.Empty<Action<MobDeathContext>>();

        // Wire positions are short x100 (WireFixedPointCodec.PositionScale is private); world = raw / 100.
        private const float WIRE_POSITION_SCALE = 100f;
        private const int NO_MISS = -1;
        private const long NEVER_RUN = -1L;

        private readonly ZoneSlotPool _pool;
        private readonly IAutoAttackTargetValidity _validity;
        private readonly int[] _actionTimer;
        private readonly bool[] _toggle;
        private readonly EntityID[] _target;
        private readonly AutoAttackState[] _state;
        private readonly IAttackSpeedSource _attackSpeed;
        private readonly IZoneEntityStateSource _positions;
        private readonly HitDetector _hitDetector;
        private readonly IHitResultEmitter _emitter;
        private readonly IAutoAttackStatSource _stats;
        private readonly IAutoAttackDamageCalculator _damage;
        private readonly IAutoAttackDamageApplier _applier;
        private readonly IAutoAttackXpAward _xpAward;
        private readonly IAutoAttackExperienceSink _experience;
        private readonly IAutoAttackMobTypeSource _mobTypes;
        private readonly ZoneID _zoneId;
        private readonly int[] _cycleTicks;
        private readonly EntityID[] _attacker;
        private readonly bool[] _prohibited;
        private readonly bool[] _outOfRange;
        private readonly int[] _lastMiss;     // last emitted miss HitResult of the streak, NO_MISS when none
        private readonly long[] _lastRunTick; // tick of the slot's last evaluation, NEVER_RUN when none

        /// <summary>Creates the system with every slot idle.</summary>
        /// <param name="pool">The zone's slot pool; resolves a player to its slot.</param>
        /// <param name="validity">Decides whether a target is valid.</param>
        /// <param name="attackSpeed">Supplies the attack speed multiplier sampled at each cycle start.</param>
        /// <param name="positions">Supplies attacker and target positions (wire units).</param>
        /// <param name="hitDetector">Range and facing check (Rule 10 Step 2).</param>
        /// <param name="emitter">Receives the hit and miss-streak results.</param>
        /// <param name="stats">Supplies BaseDamage and AttackRange per fire attempt.</param>
        /// <param name="damageCalculator">Resolves the hit (Rule 10 Step 3).</param>
        /// <param name="applier">Applies a resolved hit.</param>
        /// <param name="xpAward">Supplies the XP a killed mob is worth (Rule 10 Step 3).</param>
        /// <param name="experience">Receives the XP of a kill.</param>
        /// <param name="mobTypes">Resolves the killed mob's type for <see cref="OnMobDied"/>.</param>
        /// <param name="zoneId">The zone this process hosts (ADR-007); carried by <see cref="MobDeathContext"/>.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public AutoAttackCombatSystem(
            ZoneSlotPool pool,
            IAutoAttackTargetValidity validity,
            IAttackSpeedSource attackSpeed,
            IZoneEntityStateSource positions,
            HitDetector hitDetector,
            IHitResultEmitter emitter,
            IAutoAttackStatSource stats,
            IAutoAttackDamageCalculator damageCalculator,
            IAutoAttackDamageApplier applier,
            IAutoAttackXpAward xpAward,
            IAutoAttackExperienceSink experience,
            IAutoAttackMobTypeSource mobTypes,
            ZoneID zoneId)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _validity = validity ?? throw new ArgumentNullException(nameof(validity));
            _attackSpeed = attackSpeed ?? throw new ArgumentNullException(nameof(attackSpeed));
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
            _hitDetector = hitDetector ?? throw new ArgumentNullException(nameof(hitDetector));
            _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _damage = damageCalculator ?? throw new ArgumentNullException(nameof(damageCalculator));
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            _xpAward = xpAward ?? throw new ArgumentNullException(nameof(xpAward));
            _experience = experience ?? throw new ArgumentNullException(nameof(experience));
            _mobTypes = mobTypes ?? throw new ArgumentNullException(nameof(mobTypes));
            _zoneId = zoneId;
            int size = pool.MaxPlayers;
            _cycleTicks = new int[size];
            _attacker = new EntityID[size];
            _actionTimer = new int[size];
            _toggle = new bool[size];
            _target = new EntityID[size];
            _state = new AutoAttackState[size];
            _prohibited = new bool[size];
            _outOfRange = new bool[size];
            _lastMiss = new int[size];
            _lastRunTick = new long[size];
            for (int i = 0; i < size; i++)
            {
                _lastMiss[i] = NO_MISS;
                _lastRunTick[i] = NEVER_RUN;
            }
        }

        /// <summary>
        /// Selects a target, or deselects with <see cref="EntityID.Invalid"/> (Rules 1, 3, 4; EC-2, EC-3).
        /// A valid target resets the timer to 0 (even the same target) and activates when the toggle is ON.
        /// A deselect or a rejected target clears the target, goes idle and freezes the timer; the toggle is unchanged.
        /// Either way the miss streak ends.
        /// </summary>
        /// <param name="attacker">The player selecting.</param>
        /// <param name="target">The new target, or <see cref="EntityID.Invalid"/>.</param>
        /// <remarks>Tick thread. Does nothing for an entity with no player slot.</remarks>
        public void SetTarget(EntityID attacker, EntityID target)
        {
            if (!_pool.TryGetPlayerSlot(attacker, out byte slot))
            {
                return;
            }

            _attacker[slot] = attacker;
            if (target == EntityID.Invalid || !_validity.IsValidTarget(target))
            {
                _target[slot] = EntityID.Invalid;
                GoIdle(slot);
                return;
            }

            _target[slot] = target;
            ResetTimer(slot);
            _outOfRange[slot] = false;
            _state[slot] = _toggle[slot] ? ActiveState(slot) : AutoAttackState.Idle;
        }

        /// <summary>
        /// Sets the auto-attack toggle (Rules 1, 2, 4; EC-1, EC-4, EC-27 to EC-30). Only a change counts.
        /// OFF to ON activates (timer 0) when a still-valid target is stored; a stored target that is no longer
        /// valid is cleared. ON to OFF goes idle, freezes the timer and keeps the target.
        /// </summary>
        /// <param name="attacker">The player toggling.</param>
        /// <param name="enabled">The new toggle value.</param>
        /// <remarks>Tick thread. Does nothing for an entity with no player slot.</remarks>
        public void SetAutoAttack(EntityID attacker, bool enabled)
        {
            if (!_pool.TryGetPlayerSlot(attacker, out byte slot) || _toggle[slot] == enabled)
            {
                return;
            }

            _attacker[slot] = attacker;
            _toggle[slot] = enabled;
            if (!enabled)
            {
                GoIdle(slot);
                return;
            }

            EntityID stored = _target[slot];
            if (stored == EntityID.Invalid)
            {
                return;
            }

            if (!_validity.IsValidTarget(stored))
            {
                _target[slot] = EntityID.Invalid;
                return;
            }

            ResetTimer(slot);
            _outOfRange[slot] = false;
            _state[slot] = ActiveState(slot);
        }

        /// <summary>
        /// Sets or clears the external combat lock (stun, mount; Rule 10 Step 1, EC-7, EC-8). Not a deactivation:
        /// target, toggle and timer are untouched, so the timer keeps running and the auto fires on the first
        /// check after the flag clears. Call it in the request step; a clear there lets the same tick's check fire.
        /// </summary>
        /// <param name="entity">The player.</param>
        /// <param name="prohibited">True to lock, false to release.</param>
        /// <remarks>
        /// Tick thread. Does nothing for an entity with no player slot. The flag survives a toggle OFF/ON, a deselect and
        /// reselect, and idle, and shows as <see cref="AutoAttackState.Prohibited"/> on the next activation; only a clear
        /// or <see cref="ReleasePlayer"/> removes it. A miss streak survives a set/clear: a miss already emitted is not
        /// re-sent after the clear while it continues.
        /// </remarks>
        public void SetCombatProhibited(EntityID entity, bool prohibited)
        {
            if (!_pool.TryGetPlayerSlot(entity, out byte slot))
            {
                return;
            }

            _prohibited[slot] = prohibited;
            _outOfRange[slot] = false;   // range is unknown until the next fire attempt
            if (_state[slot] != AutoAttackState.Idle)
            {
                _state[slot] = ActiveState(slot);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rules 12-15, TR-aa-005, AC-04, AC-05, EC-21, EC-24; Story 005. Call it in the request step, before
        /// <see cref="Run"/> of the same tick (ADR-014 Decision 6): the next auto then fires one full cycle after the
        /// call. Idle: only the frozen timer is zeroed (activation resets it again, Rule 2); the attack speed is not read.
        /// Active (including out of range and prohibited): the timer resets like any other reset, ending the miss streak
        /// and sampling the cycle length (Rule 6). State, target, toggle, lock and range mark are unchanged, an auto that
        /// already resolved is never reversed, and repeated calls simply restart the cycle. Tick thread, no allocation.
        /// </remarks>
        public void NotifySkillUsed(EntityID casterEntityID)
        {
            if (!_pool.TryGetPlayerSlot(casterEntityID, out byte slot))
            {
                return;
            }

            if (_state[slot] == AutoAttackState.Idle)
            {
                _actionTimer[slot] = 0;
                return;
            }

            ResetTimer(slot);
        }

        // The state of an active slot: the lock wins, then the last range result.
        private AutoAttackState ActiveState(int slot)
        {
            if (_prohibited[slot])
            {
                return AutoAttackState.Prohibited;
            }

            return _outOfRange[slot] ? AutoAttackState.OutOfRange : AutoAttackState.CombatActive;
        }

        // Idle ends the miss streak and the out-of-range mark; the timer is left frozen.
        private void GoIdle(int slot)
        {
            _state[slot] = AutoAttackState.Idle;
            _lastMiss[slot] = NO_MISS;
            _outOfRange[slot] = false;
        }

        // Timer to 0 and a fresh cycle-length sample; the only place the attack speed is read. Ends the miss streak.
        private void ResetTimer(int slot)
        {
            _actionTimer[slot] = 0;
            _lastMiss[slot] = NO_MISS;
            _cycleTicks[slot] = AutoAttackCycle.ComputeCycleTicks(_attackSpeed.GetAttackSpeedMultiplier(_attacker[slot]));
        }

        /// <summary>
        /// Resets the player's slot: idle, timer 0, toggle OFF, no target, no lock, no streak. Called when the slot is
        /// released so a new occupant starts clean.
        /// </summary>
        /// <param name="attacker">The player leaving.</param>
        /// <remarks>
        /// Tick thread. Must not throw (it is reached from the pool's release listener). No-op without a slot, so it
        /// must be reached from <see cref="IZoneSlotReleaseListener.BeforeRelease"/>, while the player still holds the
        /// slot; a call after the release leaves the slot's state stale. <see cref="Run"/> does not check occupancy.
        /// </remarks>
        public void ReleasePlayer(EntityID attacker)
        {
            if (!_pool.TryGetPlayerSlot(attacker, out byte slot))
            {
                return;
            }

            _state[slot] = AutoAttackState.Idle;
            _actionTimer[slot] = 0;
            _cycleTicks[slot] = 0;
            _attacker[slot] = EntityID.Invalid;
            _toggle[slot] = false;
            _target[slot] = EntityID.Invalid;
            _prohibited[slot] = false;
            _outOfRange[slot] = false;
            _lastMiss[slot] = NO_MISS;
            _lastRunTick[slot] = NEVER_RUN;
        }

        /// <inheritdoc />
        /// <remarks>Story 006: the slot-release hook; resets the player's combat state while it still holds the slot. Same as <see cref="ReleasePlayer"/>.</remarks>
        public void BeforeRelease(EntityID id)
        {
            ReleasePlayer(id);
        }

        /// <summary>The cycle length in ticks sampled for the player's current cycle; 0 when the entity has no slot or none was sampled.</summary>
        /// <param name="attacker">The player.</param>
        /// <returns>The cycle length in ticks.</returns>
        public int GetCycleTicks(EntityID attacker)
        {
            return _pool.TryGetPlayerSlot(attacker, out byte slot) ? _cycleTicks[slot] : 0;
        }

        /// <summary>The player's state; <see cref="AutoAttackState.Idle"/> when the entity has no slot.</summary>
        /// <param name="attacker">The player.</param>
        /// <returns>The current state.</returns>
        public AutoAttackState GetState(EntityID attacker)
        {
            return _pool.TryGetPlayerSlot(attacker, out byte slot) ? _state[slot] : AutoAttackState.Idle;
        }

        /// <summary>The action timer in ticks; 0 when the entity has no slot.</summary>
        /// <param name="attacker">The player.</param>
        /// <returns>The timer value.</returns>
        public int GetActionTimer(EntityID attacker)
        {
            return _pool.TryGetPlayerSlot(attacker, out byte slot) ? _actionTimer[slot] : 0;
        }

        /// <summary>The stored target; <see cref="EntityID.Invalid"/> when none or the entity has no slot.</summary>
        /// <param name="attacker">The player.</param>
        /// <returns>The target.</returns>
        public EntityID GetTarget(EntityID attacker)
        {
            return _pool.TryGetPlayerSlot(attacker, out byte slot) ? _target[slot] : EntityID.Invalid;
        }

        /// <summary>
        /// Advances every active player by one tick and resolves the auto (Rules 4, 5, 10, 23; F-3; EC-10, EC-17,
        /// EC-19). A slot already evaluated for <paramref name="currentTick"/> is skipped. A target that is no longer
        /// valid is cleared, the player goes idle, the timer does not advance and nothing fires or emits. Otherwise the
        /// timer gains 1 and, once it reaches the cycle length, Rule 10 runs; only an actual fire resets the timer.
        /// </summary>
        /// <param name="currentTick">The tick being run; used only to reject a second evaluation in the same tick.</param>
        /// <param name="deltaTime">Ignored: the timer counts ticks.</param>
        /// <remarks>Tick thread, after the request step (ADR-014).</remarks>
        public void Run(uint currentTick, float deltaTime)
        {
            for (int slot = 0; slot < _state.Length; slot++)
            {
                if (_state[slot] == AutoAttackState.Idle || _lastRunTick[slot] == currentTick)
                {
                    continue;
                }

                if (!_validity.IsValidTarget(_target[slot]))
                {
                    _target[slot] = EntityID.Invalid;
                    GoIdle(slot);
                    continue;
                }

                _lastRunTick[slot] = currentTick;
                _actionTimer[slot]++;
                if (_actionTimer[slot] < _cycleTicks[slot] || _prohibited[slot])
                {
                    continue;
                }

                Resolve(slot);
            }
        }

        // Rule 10 Steps 2-4 for a ready, unprohibited slot.
        // A failed position lookup (attacker or target) is a no-fire exit like Step 5: nothing is emitted or applied, the
        // timer and state are kept, and the ready auto retries on the next tick. Story 006 wires target validity and the
        // positions from the same zone state, so a valid target without a position is a transient inconsistency.
        private void Resolve(int slot)
        {
            EntityID attacker = _attacker[slot];
            EntityID target = _target[slot];

            if (!_positions.TryGetPlayerState(attacker, out ZonePlayerStateView attackerView))
            {
                return;   // no-fire exit, retried next tick
            }

            if (!TryGetTargetPos(target, out Vector3 targetPos))
            {
                return;   // no-fire exit, retried next tick
            }

            Vector3 attackerPos = ToWorld(attackerView.PosX, attackerView.PosY, attackerView.PosZ);

            // The forward is captured toward the target, so the facing check is a deliberate pass-through for player
            // autos. The miss-type-change branch of the streak (MissOutOfRange <-> MissFacing) is therefore unreachable
            // with the real HitDetector today; it stays for correctness should the facing source change.
            Vector3 forward = ComputeForward(attackerPos, targetPos);
            float range = SanitiseRange(_stats.GetAttackRange(attacker));
            HitResult result = _hitDetector.CheckHit(attackerPos, targetPos, range, forward);
            if (result != HitResult.Hit)
            {
                RecordMiss(slot, attacker, target, result);
                return;
            }

            _emitter.Emit(attacker, target, HitResult.Hit);
            _outOfRange[slot] = false;
            FireDamage(slot, attacker, target, targetPos);
        }

        // HitDetector treats a NaN range as "skip the range check" and a negative range as its absolute value, and
        // EC-HD-4 leaves validation to the caller. A bad stat must never widen the reach: NaN, negative and +Infinity
        // become 0, which is out of range for any target beyond the point-blank epsilon (fail-safe).
        private static float SanitiseRange(float range)
        {
            return !(range >= 0f) || float.IsPositiveInfinity(range) ? 0f : range;
        }

        // A miss is sent once per streak: a repeat of the last emitted miss is not re-sent. The timer is kept.
        private void RecordMiss(int slot, EntityID attacker, EntityID target, HitResult result)
        {
            if (_lastMiss[slot] != (int)result)
            {
                _emitter.Emit(attacker, target, result);
                _lastMiss[slot] = (int)result;
            }

            _outOfRange[slot] = result == HitResult.MissOutOfRange;
            _state[slot] = ActiveState(slot);
        }

        // Rule 10 Steps 3-4 after a hit. A kill (IsKill with damage above 0) goes to ResolveKill (Story 004). A rejected
        // result (FinalDamage 0) ignores IsKill.
        private void FireDamage(int slot, EntityID attacker, EntityID target, Vector3 targetPos)
        {
            DamageResult damage = _damage.Calculate(_stats.GetBaseDamage(attacker), attacker, target);
            if (damage.FinalDamage > 0 && damage.IsKill)
            {
                ResolveKill(slot, attacker, target, targetPos, in damage);
                return;
            }

            if (damage.FinalDamage > 0)
            {
                _applier.Apply(attacker, target, in damage);
            }

            // Step 4: any actual fire resets the cycle, including a rejected calculation (ends the streak).
            ResetTimer(slot);
            _state[slot] = ActiveState(slot);
        }

        // Rule 10 Step 3 kill. The mob type is read first, since the applier may remove the mob; then the direct calls
        // GetXPAward -> AddExperience -> Apply. The slot reaches its final state (Step 4 reset, target cleared, IDLE,
        // toggle unchanged) before OnMobDied, so a subscriber that re-targets or releases the player is not overwritten.
        private void ResolveKill(int slot, EntityID attacker, EntityID target, Vector3 deathPosition, in DamageResult damage)
        {
            bool typeKnown = _mobTypes.TryGetMobType(target, out MobTypeID type);
            int xp = _xpAward.GetXPAward(target);
            _experience.AddExperience(attacker, xp);
            _applier.Apply(attacker, target, in damage);

            ResetTimer(slot);
            _target[slot] = EntityID.Invalid;
            GoIdle(slot);   // IDLE, timer frozen at 0

            if (!typeKnown)
            {
                Debug.LogError($"AutoAttackCombat: OnMobDied skipped, no mob type for target {target} killed by {attacker}.");
                return;
            }

            RaiseMobDied(new MobDeathContext(_zoneId, target, type, deathPosition, attacker));
        }

        // Each subscriber runs in its own try/catch, so one that throws is logged and the others still run. Iterates a
        // snapshot of the handler array; no allocation unless a subscriber throws.
        private void RaiseMobDied(in MobDeathContext context)
        {
            Action<MobDeathContext>[] handlers = _mobDiedHandlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // The target's ground position: a mob first, then a player. False when neither is known.
        private bool TryGetTargetPos(EntityID target, out Vector3 pos)
        {
            if (_positions.TryGetMobState(target, out ZoneMobStateView mobView))
            {
                pos = ToWorld(mobView.PosX, mobView.PosY, mobView.PosZ);
                return true;
            }

            if (_positions.TryGetPlayerState(target, out ZonePlayerStateView playerView))
            {
                pos = ToWorld(playerView.PosX, playerView.PosY, playerView.PosZ);
                return true;
            }

            pos = Vector3.zero;
            return false;
        }

        // The XZ direction to the target. Below the point-blank epsilon CheckHit returns Hit before it reads the
        // forward, so zero is returned instead of dividing by ~0.
        private static Vector3 ComputeForward(Vector3 attackerPos, Vector3 targetPos)
        {
            float dx = targetPos.x - attackerPos.x;
            float dz = targetPos.z - attackerPos.z;
            float dSq = dx * dx + dz * dz;
            if (dSq < HitDetectionConfig.POINT_BLANK_EPSILON_SQ)
            {
                return Vector3.zero;
            }

            float invLen = 1f / Mathf.Sqrt(dSq);
            return new Vector3(dx * invLen, 0f, dz * invLen);
        }

        private static Vector3 ToWorld(short x, short y, short z)
        {
            return new Vector3(x / WIRE_POSITION_SCALE, y / WIRE_POSITION_SCALE, z / WIRE_POSITION_SCALE);
        }
    }
}
