using System;
using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// <see cref="ISkillCasterAliveSource"/> over Character Stats: alive means CurrentHP &gt; 0 (Story 005).
    /// </summary>
    /// <remarks>
    /// Death and Respawn: a dead player has CurrentHP 0 until T-3 restores it, so the HP test already rejects a dead
    /// caster (Story 006). Dead-ness itself lives in <c>DeathState</c> (Story 007), not in <c>ZoneSessionState</c>.
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
