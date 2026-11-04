using System;
using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// <see cref="ISkillCasterAliveSource"/> over Character Stats: alive means CurrentHP &gt; 0 (Story 005).
    /// </summary>
    /// <remarks>
    /// MVP note for the Death and Respawn epic: <c>ZoneSessionState</c> Dead / Respawning is not implemented, so HP is
    /// the only alive test. Replace or extend this adapter when that state exists.
    /// </remarks>
    public sealed class CharacterStatsSkillCasterAliveSource : ISkillCasterAliveSource
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsSkillCasterAliveSource(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public bool IsAlive(EntityID entity)
        {
            return _stats.GetCurrentHP(entity) > 0f;
        }
    }
}
