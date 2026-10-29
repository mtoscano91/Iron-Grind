using IronGrind.CharacterStats;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Seam through which Movement reads an entity's effective movement speed (F-MOV-2). Read every tick,
    /// never cached across ticks. Injected so Movement does not depend on a Character Stats singleton.
    /// </summary>
    public interface IMovementSpeedSource
    {
        /// <summary>Reads the current effective movement speed for <paramref name="id"/>.</summary>
        /// <param name="id">The entity to read.</param>
        /// <param name="speed">Speed in meters per second; undefined when the method returns false.</param>
        /// <returns>True when a value was read; false when the read failed.</returns>
        bool TryGetMovementSpeed(EntityID id, out float speed);
    }
}
