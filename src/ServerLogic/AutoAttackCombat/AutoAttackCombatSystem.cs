using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Per-player auto-attack state, toggle, target and action timer (design/gdd/auto-attack-combat.md Rules 1-4, 23,
    /// States and Transitions, EC-1 to EC-4, EC-27 to EC-30). Auto-Attack Combat Story 001.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State.</b> Arrays of <see cref="ZoneSlotPool.MaxPlayers"/> entries indexed by the player slot. The state is
    /// <see cref="AutoAttackState.CombatActive"/> only while a valid target is stored and the toggle is ON.
    /// The action timer counts whole ticks: +1 per <see cref="Run"/> while active, frozen while idle, 0 on every
    /// activation and every target selection (Rules 2, 3).
    /// </para>
    /// <para>
    /// <b>Entry points.</b> <see cref="SetTarget"/>, <see cref="SetAutoAttack"/> and <see cref="ReleasePlayer"/>.
    /// An entity with no player slot is ignored by every method: no throw, no log. Story 006 wires them.
    /// </para>
    /// <para>
    /// Tick thread only. Allocates nothing after construction. Story 002 adds the cycle and ready check.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var combat = new AutoAttackCombatSystem(pool, validity);
    /// combat.SetAutoAttack(playerId, true);
    /// combat.SetTarget(playerId, mobId);   // CombatActive, timer 0
    /// combat.Run(tick, dt);                // timer 1
    /// </code>
    /// </example>
    public sealed class AutoAttackCombatSystem : IZoneSimulationStep
    {
        private readonly ZoneSlotPool _pool;
        private readonly IAutoAttackTargetValidity _validity;
        private readonly int[] _actionTimer;
        private readonly bool[] _toggle;
        private readonly EntityID[] _target;
        private readonly AutoAttackState[] _state;

        /// <summary>Creates the system with every slot idle.</summary>
        /// <param name="pool">The zone's slot pool; resolves a player to its slot.</param>
        /// <param name="validity">Decides whether a target is valid.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public AutoAttackCombatSystem(ZoneSlotPool pool, IAutoAttackTargetValidity validity)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _validity = validity ?? throw new ArgumentNullException(nameof(validity));
            int size = pool.MaxPlayers;
            _actionTimer = new int[size];
            _toggle = new bool[size];
            _target = new EntityID[size];
            _state = new AutoAttackState[size];
        }

        /// <summary>
        /// Selects a target, or deselects with <see cref="EntityID.Invalid"/> (Rules 1, 3, 4; EC-2, EC-3).
        /// A valid target resets the timer to 0 (even the same target) and activates when the toggle is ON.
        /// A deselect or a rejected target clears the target, goes idle and freezes the timer; the toggle is unchanged.
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

            if (target == EntityID.Invalid || !_validity.IsValidTarget(target))
            {
                _target[slot] = EntityID.Invalid;
                _state[slot] = AutoAttackState.Idle;
                return;
            }

            _target[slot] = target;
            _actionTimer[slot] = 0;
            _state[slot] = _toggle[slot] ? AutoAttackState.CombatActive : AutoAttackState.Idle;
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

            _toggle[slot] = enabled;
            if (!enabled)
            {
                _state[slot] = AutoAttackState.Idle;
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

            _actionTimer[slot] = 0;
            _state[slot] = AutoAttackState.CombatActive;
        }

        /// <summary>
        /// Resets the player's slot: idle, timer 0, toggle OFF, no target. Called when the slot is released so a
        /// new occupant starts clean.
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
            _toggle[slot] = false;
            _target[slot] = EntityID.Invalid;
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
        /// Advances every active player by one tick (Rules 4, 5, 23). A target that is no longer valid is cleared,
        /// the player goes idle and the timer does not advance (frozen); otherwise the timer gains 1.
        /// </summary>
        /// <param name="currentTick">The tick being run; unused.</param>
        /// <param name="deltaTime">Ignored: the timer counts ticks.</param>
        /// <remarks>Tick thread, after the request step (ADR-014).</remarks>
        public void Run(uint currentTick, float deltaTime)
        {
            for (int slot = 0; slot < _state.Length; slot++)
            {
                if (_state[slot] == AutoAttackState.Idle)
                {
                    continue;
                }

                if (!_validity.IsValidTarget(_target[slot]))
                {
                    _target[slot] = EntityID.Invalid;
                    _state[slot] = AutoAttackState.Idle;
                    continue;
                }

                _actionTimer[slot]++;
            }
        }
    }
}
