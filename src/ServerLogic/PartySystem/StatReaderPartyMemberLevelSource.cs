using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneServer;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Production <see cref="IPartyMemberLevelSource"/>: reads <c>StatID.Level</c> through the zone's stat reader
    /// (the same read as <c>ZoneEntityStateSource</c>). Character Stats returns 0 for an entity it does not know, and
    /// a real level is at least 1, so a level of 0 or less is unreadable.
    /// </summary>
    public sealed class StatReaderPartyMemberLevelSource : IPartyMemberLevelSource
    {
        private readonly IZoneEntityStatReader _stats;

        /// <summary>Creates the source.</summary>
        /// <param name="stats">The stat reader.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public StatReaderPartyMemberLevelSource(IZoneEntityStatReader stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc/>
        public bool TryGetLevel(EntityID entity, out int level)
        {
            level = 0;
            if (entity == EntityID.Invalid)
            {
                return false;
            }

            int value = _stats.GetEffectiveStat(entity, StatID.Level);
            if (value <= 0)
            {
                return false;
            }

            level = value;
            return true;
        }
    }
}
