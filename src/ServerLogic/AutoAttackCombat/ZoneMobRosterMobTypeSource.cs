using System;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// <see cref="IAutoAttackMobTypeSource"/> over <see cref="IZoneMobRoster.TryGetMob"/>. The spawn position is discarded:
    /// the death position is the target position decoded at the kill.
    /// </summary>
    /// <example>
    /// <code>
    /// IAutoAttackMobTypeSource source = new ZoneMobRosterMobTypeSource(roster);
    /// </code>
    /// </example>
    public sealed class ZoneMobRosterMobTypeSource : IAutoAttackMobTypeSource
    {
        private readonly IZoneMobRoster _roster;

        /// <summary>Creates the adapter.</summary>
        /// <param name="roster">The zone's mob roster.</param>
        /// <exception cref="ArgumentNullException"><paramref name="roster"/> is null.</exception>
        public ZoneMobRosterMobTypeSource(IZoneMobRoster roster)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        /// <inheritdoc />
        public bool TryGetMobType(EntityID mob, out MobTypeID type)
        {
            return _roster.TryGetMob(mob, out type, out Vector3 _);
        }
    }
}
