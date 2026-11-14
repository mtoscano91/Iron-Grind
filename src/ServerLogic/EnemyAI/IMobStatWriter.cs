using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Narrow write seam the <see cref="MobSpawnSeeder"/> uses to seed a freshly spawned mob's stats
    /// (Enemy AI Story 007, CR-AI-12, F-AI-E-1).
    /// </summary>
    /// <remarks>
    /// Deliberately exposes no buff-modifier method: Enraged scaling is written into the base stats,
    /// never applied as a buff (AC-AI-15). Production uses <see cref="CharacterStatsMobStatWriter"/>.
    /// </remarks>
    public interface IMobStatWriter
    {
        /// <summary>Writes a base stat value for the entity.</summary>
        /// <param name="entityId">The mob entity.</param>
        /// <param name="statId">The stat to write.</param>
        /// <param name="value">The base value.</param>
        void SetBaseStat(EntityID entityId, StatID statId, int value);

        /// <summary>Sets the entity's current hit points (clamped by the implementation to effective MaxHP).</summary>
        /// <param name="entityId">The mob entity.</param>
        /// <param name="value">The current HP.</param>
        void SetCurrentHP(EntityID entityId, float value);
    }
}
