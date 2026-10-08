using System;

namespace IronGrind.DamageCalculation
{
    /// <summary>
    /// Immutable, validated Damage Calculation tuning values (design/gdd/damage-calculation.md Tuning
    /// Knobs: MIN_DAMAGE_FRACTION, K_MAGIC, MIN_ELEMENTAL_FRACTION) plus the highest base damage the
    /// resolver accepts. Injected into <see cref="DamageCalculator"/> by constructor; there is no static
    /// lookup. Damage Calculation Story 001.
    /// </summary>
    /// <remarks>
    /// The GDD's safe ranges are guidance and are not enforced; only the ranges the formulas need to stay
    /// well defined are validated.
    /// </remarks>
    public sealed class DamageCalculationConfig
    {
        /// <summary>
        /// Highest value <see cref="MaxBaseDamage"/> may take: 2^24, the largest integer up to which every
        /// int converts to float exactly, so the float formulas cannot lose precision on the base.
        /// </summary>
        public const int MAX_BASE_DAMAGE_LIMIT = 1 << 24;

        private const float DEFAULT_MIN_DAMAGE_FRACTION = 0.05f;
        private const float DEFAULT_K_MAGIC = 200f;
        private const float DEFAULT_MIN_ELEMENTAL_FRACTION = 0.10f;
        // The AttackPower ceiling of the Character Stats schema.
        private const int DEFAULT_MAX_BASE_DAMAGE = 99999;

        /// <summary>Builds and validates a config.</summary>
        /// <param name="minDamageFraction">MIN_DAMAGE_FRACTION, in (0, 1].</param>
        /// <param name="kMagic">K_MAGIC, greater than 0 and finite.</param>
        /// <param name="minElementalFraction">MIN_ELEMENTAL_FRACTION, in (0, 1].</param>
        /// <param name="maxBaseDamage">Highest accepted base damage, in [1, <see cref="MAX_BASE_DAMAGE_LIMIT"/>].</param>
        /// <exception cref="ArgumentOutOfRangeException">A value is outside its valid range (NaN and infinity included).</exception>
        public DamageCalculationConfig(float minDamageFraction, float kMagic, float minElementalFraction, int maxBaseDamage)
        {
            if (!(minDamageFraction > 0f && minDamageFraction <= 1f))
                throw new ArgumentOutOfRangeException(nameof(minDamageFraction), minDamageFraction, "Must be in (0, 1].");
            if (!(kMagic > 0f) || float.IsPositiveInfinity(kMagic))
                throw new ArgumentOutOfRangeException(nameof(kMagic), kMagic, "Must be greater than 0 and finite.");
            if (!(minElementalFraction > 0f && minElementalFraction <= 1f))
                throw new ArgumentOutOfRangeException(nameof(minElementalFraction), minElementalFraction, "Must be in (0, 1].");
            if (maxBaseDamage < 1 || maxBaseDamage > MAX_BASE_DAMAGE_LIMIT)
                throw new ArgumentOutOfRangeException(nameof(maxBaseDamage), maxBaseDamage, "Must be in [1, 16777216].");

            MinDamageFraction = minDamageFraction;
            KMagic = kMagic;
            MinElementalFraction = minElementalFraction;
            MaxBaseDamage = maxBaseDamage;
        }

        /// <summary>The default configuration: the GDD tuning values (0.05, 200, 0.10) and a base damage ceiling of 99999.</summary>
        public static DamageCalculationConfig Default { get; } = new DamageCalculationConfig(
            DEFAULT_MIN_DAMAGE_FRACTION, DEFAULT_K_MAGIC, DEFAULT_MIN_ELEMENTAL_FRACTION, DEFAULT_MAX_BASE_DAMAGE);

        /// <summary>MIN_DAMAGE_FRACTION: lowest share of BaseDamage the physical component can reach (F-DC-1).</summary>
        public float MinDamageFraction { get; }

        /// <summary>K_MAGIC: magic-defense scaling constant (F-DC-2).</summary>
        public float KMagic { get; }

        /// <summary>MIN_ELEMENTAL_FRACTION: lowest share of the elemental bonus that gets through (F-DC-2).</summary>
        public float MinElementalFraction { get; }

        /// <summary>Highest base damage the resolver accepts; a call above it is rejected.</summary>
        public int MaxBaseDamage { get; }
    }
}
