using System;
using IronGrind.CharacterStats;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// <see cref="IMovementSpeedSource"/> over <c>CharacterStats.GetEffectiveStatFloat</c> for
    /// <see cref="StatID.MovementSpeed"/>. Uses the float read, not the int <c>GetEffectiveStat</c>, which
    /// floors (9.6 would read as 9). Character Stats already clamps to [0.5, 20.0], so no second clamp here.
    /// </summary>
    /// <remarks>
    /// An entity Character Stats does not know reads as 0f, which is passed through; <see cref="MovementSpeedResolver"/>
    /// rejects it. This adapter does not catch exceptions.
    /// </remarks>
    /// <example>
    /// <code>
    /// IMovementSpeedSource source = new CharacterStatsMovementSpeedSource(characterStats);
    /// </code>
    /// </example>
    public sealed class CharacterStatsMovementSpeedSource : IMovementSpeedSource
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsMovementSpeedSource(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public bool TryGetMovementSpeed(EntityID id, out float speed)
        {
            speed = _stats.GetEffectiveStatFloat(id, StatID.MovementSpeed);
            return true;
        }
    }
}
