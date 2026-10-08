using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Supplies a character's world position to the Loot Table System (design/gdd/loot-table-system.md
    /// CR-LT-7 proximity pickup). A consumer-side interface: no movement system exists in code yet, so
    /// the loot system declares what it needs and the owner of positions implements it later
    /// (ADR-010 Tier 1, constructor-injected).
    /// </summary>
    /// <remarks>
    /// The loot system measures the pickup distance in 3D from this position to the ground item's
    /// position, so both must use the same reference point (the character's and the mob's pivot).
    /// The loot system calls <see cref="TryGetPosition"/> while it iterates its ground items: an
    /// implementation must be a plain read and must not call back into the ground item service.
    /// </remarks>
    public interface ICharacterPositionProvider
    {
        /// <summary>Reads a character's current world position.</summary>
        /// <param name="characterId">The character to look up.</param>
        /// <param name="position">The position when the method returns true; otherwise default.</param>
        /// <returns>
        /// False when the character has no position (disconnected, or not in the zone). The loot
        /// system treats an unknown position as outside every pickup radius.
        /// </returns>
        bool TryGetPosition(CharacterID characterId, out UnityEngine.Vector3 position);
    }
}
