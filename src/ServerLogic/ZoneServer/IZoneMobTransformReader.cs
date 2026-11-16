using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Narrow read view over the server-authoritative mob transform for <see cref="ZoneEntityStateSource"/>.
    /// Enemy AI provides the implementation (Enemy AI Story 008); this seam keeps Zone Server free of Enemy AI types.
    /// </summary>
    public interface IZoneMobTransformReader
    {
        /// <summary>Reads the committed transform of a mob.</summary>
        /// <param name="id">The mob entity.</param>
        /// <param name="position">World position in metres; default when not found.</param>
        /// <param name="facing">Facing in degrees x10; 0 when not found.</param>
        /// <returns>True when the reader holds the mob.</returns>
        bool TryGetTransform(EntityID id, out Vector3 position, out short facing);
    }
}
