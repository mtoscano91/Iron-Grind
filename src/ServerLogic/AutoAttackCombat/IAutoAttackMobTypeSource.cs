using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Resolves a mob's type for the <c>MobDeathContext</c> raised on a kill (ADR-010 Decision 3).
    /// Auto-Attack Combat Story 004.
    /// </summary>
    /// <remarks>Tick thread. Implementations must not allocate.</remarks>
    public interface IAutoAttackMobTypeSource
    {
        /// <summary>Reads the type of a live mob.</summary>
        /// <param name="mob">The mob's EntityID.</param>
        /// <param name="type">The mob type, or default when unknown.</param>
        /// <returns>True when <paramref name="mob"/> is a known mob.</returns>
        bool TryGetMobType(EntityID mob, out MobTypeID type);
    }
}
