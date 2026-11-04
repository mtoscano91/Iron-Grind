using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>Narrow read seam for V-8: an entity's position on the XZ plane, in world units (Story 005).</summary>
    /// <remarks>Called on the tick thread. Must not allocate or throw.</remarks>
    public interface ISkillPositionSource
    {
        /// <summary>Reads the entity's current X and Z position (Y is ignored by V-8).</summary>
        /// <param name="entity">A player or a mob.</param>
        /// <param name="x">X position in units.</param>
        /// <param name="z">Z position in units.</param>
        /// <returns>False when the position is unknown.</returns>
        bool TryGetPositionXZ(EntityID entity, out float x, out float z);
    }
}
