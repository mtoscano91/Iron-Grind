using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.Events
{
    /// <summary>
    /// The argument of the Tier 2 <c>OnMobDied</c> broadcast (ADR-010 Decision 3): everything loot, party kill credit and
    /// respawn need about one kill. A <c>readonly struct</c>, so raising the event does not allocate.
    /// </summary>
    public readonly struct MobDeathContext
    {
        /// <summary>The zone the mob died in.</summary>
        public readonly ZoneID ZoneId;

        /// <summary>The dead mob.</summary>
        public readonly EntityID MobEntityId;

        /// <summary>The dead mob's type.</summary>
        public readonly MobTypeID MobTypeId;

        /// <summary>Where the mob died, in world units.</summary>
        public readonly Vector3 DeathPosition;

        /// <summary>The entity that dealt the killing blow.</summary>
        public readonly EntityID KillerEntityId;

        /// <summary>Builds a death context.</summary>
        /// <param name="zoneId">The zone the mob died in.</param>
        /// <param name="mobId">The dead mob.</param>
        /// <param name="typeId">The dead mob's type.</param>
        /// <param name="pos">The death position in world units.</param>
        /// <param name="killer">The killing entity.</param>
        public MobDeathContext(ZoneID zoneId, EntityID mobId, MobTypeID typeId, Vector3 pos, EntityID killer)
        {
            ZoneId = zoneId;
            MobEntityId = mobId;
            MobTypeId = typeId;
            DeathPosition = pos;
            KillerEntityId = killer;
        }
    }
}
