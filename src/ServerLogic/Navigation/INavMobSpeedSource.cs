using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Source of a mob type's base move speed (CR-NAV-5). Enemy AI's mob definition registry replaces
    /// <see cref="StandInNavMobSpeedSource"/>.
    /// </summary>
    public interface INavMobSpeedSource
    {
        /// <summary>Looks up the move speed of a mob type.</summary>
        /// <param name="mobType">The mob type.</param>
        /// <param name="speed">The move speed in m/s when found.</param>
        /// <returns>True when the type is known.</returns>
        bool TryGetMoveSpeed(MobTypeID mobType, out float speed);
    }
}
