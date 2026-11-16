using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// <see cref="IAutoAttackTargetValidity"/> for the real zone (Enemy AI Story 012, AC-1): a target is valid only
    /// while it holds a mob slot of this zone's pool, has a <see cref="MobRuntimeStore"/> entry and has HP above 0.
    /// Players, unknown IDs, mobs without a store entry and corpses (HP 0, including the Dead linger) are invalid.
    /// </summary>
    /// <remarks>
    /// Does not read <see cref="EnemyAISystem"/> (Combat is built before it): a mob enters Dead only once its HP is 0, so
    /// the HP check covers Dead. Tick thread. Allocates nothing and does not throw.
    /// </remarks>
    /// <example>
    /// <code>
    /// IAutoAttackTargetValidity validity = new MobTargetValidity(pool, store, characterStats);
    /// </code>
    /// </example>
    public sealed class MobTargetValidity : IAutoAttackTargetValidity
    {
        private readonly ZoneSlotPool _pool;
        private readonly MobRuntimeStore _store;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the validity check.</summary>
        /// <param name="pool">The zone's slot pool.</param>
        /// <param name="store">The zone's mob runtime store.</param>
        /// <param name="stats">The zone's Character Stats.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public MobTargetValidity(ZoneSlotPool pool, MobRuntimeStore store, IronGrind.CharacterStats.CharacterStats stats)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public bool IsValidTarget(EntityID target)
        {
            if (!_pool.TryGetMobSlot(target, out ushort _) || !_store.TryGet(target, out MobRuntimeEntry _))
            {
                return false;
            }

            // A NaN HP fails the comparison, so it is invalid too.
            return _stats.GetCurrentHP(target) > 0f;
        }
    }
}
