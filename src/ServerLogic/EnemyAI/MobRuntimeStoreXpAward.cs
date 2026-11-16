using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// <see cref="IAutoAttackXpAward"/> over <see cref="MobRuntimeStore"/> (Enemy AI Story 012, AC-4): the XP a mob is worth is
    /// the per-spawn <c>Variant.EffectiveKillXP</c> (KillXP, or the Enraged value). No level-differential modifier (OQ-LS-7 b).
    /// A mob with no store entry awards 0 and logs one warning per call.
    /// </summary>
    /// <remarks>Tick thread. Allocates nothing for a known mob.</remarks>
    /// <example>
    /// <code>
    /// IAutoAttackXpAward xp = new MobRuntimeStoreXpAward(store);
    /// </code>
    /// </example>
    public sealed class MobRuntimeStoreXpAward : IAutoAttackXpAward
    {
        private const string LOG_PREFIX = "[MobRuntimeStoreXpAward]";

        private readonly MobRuntimeStore _store;

        /// <summary>Creates the award source.</summary>
        /// <param name="store">The zone's mob runtime store.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
        public MobRuntimeStoreXpAward(MobRuntimeStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc />
        public int GetXPAward(EntityID target)
        {
            if (_store.TryGet(target, out MobRuntimeEntry entry))
            {
                return entry.Variant.EffectiveKillXP;
            }

            Debug.LogWarning($"{LOG_PREFIX} no store entry for {target}; awarding 0 XP.");
            return 0;
        }
    }
}
