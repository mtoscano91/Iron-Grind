using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;

namespace IronGrind.Events
{
    /// <summary>
    /// The argument of <c>MobEventBus.OnMobDied</c>: one mob that has just died. A <c>readonly struct</c>, so raising
    /// the event does not allocate.
    /// </summary>
    public readonly struct MobDiedArgs
    {
        /// <summary>The zone the mob died in.</summary>
        public readonly ZoneID Zone;

        /// <summary>The dead mob.</summary>
        public readonly EntityID Entity;

        /// <summary>Builds the args.</summary>
        /// <param name="zone">The zone the mob died in.</param>
        /// <param name="entity">The dead mob.</param>
        public MobDiedArgs(ZoneID zone, EntityID entity)
        {
            Zone = zone;
            Entity = entity;
        }
    }
}
