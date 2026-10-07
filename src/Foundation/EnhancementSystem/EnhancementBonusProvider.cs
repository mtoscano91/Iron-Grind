using System;
using IronGrind.ItemDatabase;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Config-driven implementation of <see cref="IEnhancementBonusProvider"/> (F-ENH-1, F-ENH-2,
    /// AC-ENH-19 to AC-ENH-21). Stateless apart from the injected <see cref="EnhancementConfig"/>;
    /// pure arithmetic, no allocation on the success path. Enhancement Story 002.
    /// </summary>
    public sealed class EnhancementBonusProvider : IEnhancementBonusProvider
    {
        private readonly EnhancementConfig _config;

        /// <summary>Creates a provider over <paramref name="config"/>.</summary>
        /// <param name="config">Per-tier bonuses, level cap and elemental ceiling.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public EnhancementBonusProvider(EnhancementConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config;
        }

        /// <inheritdoc/>
        public int GetFlatBonus(int level, int baseFlatBonus, GearTier gearTier)
        {
            RequireLevel(level);
            return baseFlatBonus + level * _config.GetBonusPerLevel(gearTier);
        }

        /// <inheritdoc/>
        public int GetElementalBonus(int level, int baseElementalDamage, GearTier gearTier, bool isWeapon)
        {
            if (!isWeapon) return 0;

            RequireLevel(level);
            int enhanced = baseElementalDamage + level * _config.GetElementalBonusPerLevel(gearTier);
            return Math.Min(enhanced, _config.ElementalDamageCeiling);
        }

        private void RequireLevel(int level)
        {
            if (level < 0 || level > _config.MaxEnhancementLevel)
                throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be within [0, MaxEnhancementLevel].");
        }
    }
}
