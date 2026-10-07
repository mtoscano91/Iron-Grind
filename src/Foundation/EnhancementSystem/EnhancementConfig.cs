using System;
using System.Collections.Generic;
using IronGrind.ItemDatabase;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Immutable, validated Enhancement System configuration: level cap, prestige thresholds,
    /// success probability table, per-tier bonuses and the elemental damage ceiling
    /// (design/gdd/enhancement-system.md CR-ENH-1, CR-ENH-2, CR-ENH-9, CR-ENH-10, CR-ENH-12,
    /// F-ENH-4, F-ENH-5, TK-ENH-1 to TK-ENH-8). Injected into consumers by constructor; there is
    /// no static lookup. Enhancement Story 001.
    /// </summary>
    /// <remarks>
    /// The tuning knobs' "safe ranges" are guidance and are not enforced. Only the probability
    /// constraints, the table length and the threshold ordering are validated.
    /// </remarks>
    public sealed class EnhancementConfig
    {
        private readonly double[] _successProbabilities;
        private readonly int _bronzeBonus;
        private readonly int _ironBonus;
        private readonly int _steelBonus;
        private readonly int _darkSteelBonus;
        private readonly int _bronzeElementalBonus;
        private readonly int _ironElementalBonus;
        private readonly int _steelElementalBonus;
        private readonly int _darkSteelElementalBonus;
        private readonly double[] _expectedScrolls;

        /// <summary>Builds and validates a config.</summary>
        /// <param name="maxEnhancementLevel">Level cap (CR-ENH-2, TK-ENH-3).</param>
        /// <param name="prestigeMidThreshold">First level of the visible band (TK-ENH-5).</param>
        /// <param name="enhancementGlowThreshold">First level of the glow band (TK-ENH-4).</param>
        /// <param name="prestigeHighThreshold">First level of the high band (TK-ENH-6).</param>
        /// <param name="successProbabilities">P_s[k], index = current level k; copied (F-ENH-4).</param>
        /// <param name="bronzeBonusPerLevel">Flat bonus per level, Bronze.</param>
        /// <param name="ironBonusPerLevel">Flat bonus per level, Iron.</param>
        /// <param name="steelBonusPerLevel">Flat bonus per level, Steel.</param>
        /// <param name="darkSteelBonusPerLevel">Flat bonus per level, Dark Steel.</param>
        /// <param name="bronzeElementalBonusPerLevel">Elemental bonus per level, Bronze.</param>
        /// <param name="ironElementalBonusPerLevel">Elemental bonus per level, Iron.</param>
        /// <param name="steelElementalBonusPerLevel">Elemental bonus per level, Steel.</param>
        /// <param name="darkSteelElementalBonusPerLevel">Elemental bonus per level, Dark Steel.</param>
        /// <param name="elementalDamageCeiling">Elemental damage ceiling (Item Database owned value).</param>
        /// <exception cref="ArgumentNullException">The probability table is null.</exception>
        /// <exception cref="ArgumentException">A probability or threshold constraint is broken.</exception>
        public EnhancementConfig(
            byte maxEnhancementLevel,
            byte prestigeMidThreshold,
            byte enhancementGlowThreshold,
            byte prestigeHighThreshold,
            IReadOnlyList<double> successProbabilities,
            int bronzeBonusPerLevel, int ironBonusPerLevel, int steelBonusPerLevel, int darkSteelBonusPerLevel,
            int bronzeElementalBonusPerLevel, int ironElementalBonusPerLevel, int steelElementalBonusPerLevel, int darkSteelElementalBonusPerLevel,
            int elementalDamageCeiling)
        {
            ValidateProbabilities(maxEnhancementLevel, successProbabilities);
            ValidateThresholds(maxEnhancementLevel, prestigeMidThreshold, enhancementGlowThreshold, prestigeHighThreshold);

            MaxEnhancementLevel = maxEnhancementLevel;
            PrestigeMidThreshold = prestigeMidThreshold;
            EnhancementGlowThreshold = enhancementGlowThreshold;
            PrestigeHighThreshold = prestigeHighThreshold;
            ElementalDamageCeiling = elementalDamageCeiling;

            _successProbabilities = new double[successProbabilities.Count];
            for (int i = 0; i < _successProbabilities.Length; i++)
                _successProbabilities[i] = successProbabilities[i];

            _bronzeBonus = bronzeBonusPerLevel;
            _ironBonus = ironBonusPerLevel;
            _steelBonus = steelBonusPerLevel;
            _darkSteelBonus = darkSteelBonusPerLevel;
            _bronzeElementalBonus = bronzeElementalBonusPerLevel;
            _ironElementalBonus = ironElementalBonusPerLevel;
            _steelElementalBonus = steelElementalBonusPerLevel;
            _darkSteelElementalBonus = darkSteelElementalBonusPerLevel;

            _expectedScrolls = ComputeExpectedScrolls(_successProbabilities);
        }

        /// <summary>The GDD default configuration (F-ENH-4 table and TK-ENH-1 to TK-ENH-8 values).</summary>
        public static EnhancementConfig Default { get; } = CreateDefault();

        // The default F-ENH-4 table is a local here so that Default never depends on the
        // declaration order of static fields.
        private static EnhancementConfig CreateDefault()
        {
            double[] successProbabilities = { 0.95, 0.90, 0.85, 0.80, 0.65, 0.50, 0.35, 0.22, 0.12, 0.06 };
            return new EnhancementConfig(
                EnhancementConstants.MAX_ENHANCEMENT_LEVEL,
                EnhancementConstants.PRESTIGE_MID_THRESHOLD,
                EnhancementConstants.ENHANCEMENT_GLOW_THRESHOLD,
                EnhancementConstants.PRESTIGE_HIGH_THRESHOLD,
                successProbabilities,
                EnhancementConstants.BONUS_PER_LEVEL_BRONZE,
                EnhancementConstants.BONUS_PER_LEVEL_IRON,
                EnhancementConstants.BONUS_PER_LEVEL_STEEL,
                EnhancementConstants.BONUS_PER_LEVEL_DARK_STEEL,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_BRONZE,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_IRON,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_STEEL,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_DARK_STEEL,
                EnhancementConstants.ELEMENTAL_DAMAGE_CEILING);
        }

        /// <summary>Highest enhancement level (CR-ENH-2).</summary>
        public byte MaxEnhancementLevel { get; }

        /// <summary>First level of the visible prestige band (TK-ENH-5).</summary>
        public byte PrestigeMidThreshold { get; }

        /// <summary>First level of the glow prestige band (TK-ENH-4).</summary>
        public byte EnhancementGlowThreshold { get; }

        /// <summary>First level of the high prestige band (TK-ENH-6).</summary>
        public byte PrestigeHighThreshold { get; }

        /// <summary>Elemental damage ceiling; must match the Item Database's registry value.</summary>
        public int ElementalDamageCeiling { get; }

        /// <summary>Success probability of an attempt from <paramref name="level"/> (F-ENH-4).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Level is negative or at/above the cap (no attempt exists from the cap).</exception>
        public double GetSuccessProbability(int level)
        {
            RequireAttemptLevel(level);
            return _successProbabilities[level];
        }

        /// <summary>Destruction probability of an attempt from <paramref name="level"/>: 1 - P_s[level] (CR-ENH-10, F-ENH-4).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Same rule as <see cref="GetSuccessProbability"/>.</exception>
        public double GetDestructionProbability(int level)
        {
            RequireAttemptLevel(level);
            return 1.0 - _successProbabilities[level];
        }

        /// <summary>
        /// Resolves an attempt from a caller-supplied draw in [0, 1) (CR-ENH-9): Success iff
        /// <c>draw &lt; P_s[level]</c>; a draw equal to P_s is Destruction.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Same rule as <see cref="GetSuccessProbability"/>.</exception>
        public EnhancementOutcome ResolveOutcome(int level, double draw)
        {
            RequireAttemptLevel(level);
            return draw < _successProbabilities[level] ? EnhancementOutcome.Success : EnhancementOutcome.Destruction;
        }

        /// <summary>
        /// Maps a level to its prestige band from the three thresholds only (CR-ENH-12). Never
        /// throws; a level above the cap maps to <see cref="PrestigeBand.High"/>.
        /// </summary>
        public PrestigeBand GetPrestigeBand(byte level)
        {
            if (level >= PrestigeHighThreshold) return PrestigeBand.High;
            if (level >= EnhancementGlowThreshold) return PrestigeBand.GlowLow;
            if (level >= PrestigeMidThreshold) return PrestigeBand.VisibleNoGlow;
            return PrestigeBand.None;
        }

        /// <summary>Flat bonus per enhancement level for a tier (consumed by Story 002).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="GearTier.None"/> or an undefined value.</exception>
        public int GetBonusPerLevel(GearTier tier)
        {
            switch (tier)
            {
                case GearTier.Bronze: return _bronzeBonus;
                case GearTier.Iron: return _ironBonus;
                case GearTier.Steel: return _steelBonus;
                case GearTier.DarkSteel: return _darkSteelBonus;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "No BonusPerLevel entry for this tier.");
            }
        }

        /// <summary>Elemental bonus per enhancement level for a tier (consumed by Story 002).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="GearTier.None"/> or an undefined value.</exception>
        public int GetElementalBonusPerLevel(GearTier tier)
        {
            switch (tier)
            {
                case GearTier.Bronze: return _bronzeElementalBonus;
                case GearTier.Iron: return _ironElementalBonus;
                case GearTier.Steel: return _steelElementalBonus;
                case GearTier.DarkSteel: return _darkSteelElementalBonus;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "No ElementalBonusPerLevel entry for this tier.");
            }
        }

        /// <summary>
        /// Expected scrolls to go from +0 to <paramref name="targetLevel"/> (F-ENH-5): T[targetLevel],
        /// with T[0] = 0. Pins the table against the GDD economy numbers; not used at runtime.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Target is negative or above the cap.</exception>
        public double GetExpectedScrolls(int targetLevel)
        {
            if (targetLevel < 0 || targetLevel > MaxEnhancementLevel)
                throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, "Target level must be within [0, MaxEnhancementLevel].");
            return _expectedScrolls[targetLevel];
        }

        private void RequireAttemptLevel(int level)
        {
            if (level < 0 || level >= MaxEnhancementLevel)
                throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be within [0, MaxEnhancementLevel); no attempt exists from the cap.");
        }

        // F-ENH-5: A[k] = (1 + P_d[k] * T[k]) / P_s[k]; T[0] = 0; T[k] = T[k-1] + A[k-1].
        private static double[] ComputeExpectedScrolls(double[] ps)
        {
            var t = new double[ps.Length + 1];
            for (int k = 1; k <= ps.Length; k++)
            {
                int prev = k - 1;
                double pd = 1.0 - ps[prev];
                t[k] = t[prev] + (1.0 + pd * t[prev]) / ps[prev];
            }
            return t;
        }

        private static void ValidateProbabilities(byte maxLevel, IReadOnlyList<double> table)
        {
            if (table == null)
                throw new ArgumentNullException("successProbabilities", "Success probability table must not be null.");
            if (table.Count != maxLevel)
                throw new ArgumentException(
                    "Success probability table length (" + table.Count + ") must equal MAX_ENHANCEMENT_LEVEL (" + maxLevel + ").");

            for (int k = 0; k < table.Count; k++)
            {
                double p = table[k];
                if (double.IsNaN(p) || p < EnhancementConstants.MIN_SUCCESS_PROBABILITY || p > EnhancementConstants.MAX_SUCCESS_PROBABILITY)
                    throw new ArgumentException(
                        "P_s[" + k + "] = " + p + " is outside the allowed range [" + EnhancementConstants.MIN_SUCCESS_PROBABILITY
                        + ", " + EnhancementConstants.MAX_SUCCESS_PROBABILITY + "] (TK-ENH-1).");
                if (k > 0 && !(p < table[k - 1]))
                    throw new ArgumentException(
                        "P_s must be strictly decreasing (TK-ENH-1): P_s[" + k + "] = " + p + " is not below P_s[" + (k - 1) + "] = " + table[k - 1] + ".");
            }
        }

        private static void ValidateThresholds(byte maxLevel, byte mid, byte glow, byte high)
        {
            if (!(mid < glow))
                throw new ArgumentException(
                    "PRESTIGE_MID_THRESHOLD (" + mid + ") must be less than ENHANCEMENT_GLOW_THRESHOLD (" + glow + ") (TK-ENH-4, TK-ENH-5).");
            if (!(glow < high))
                throw new ArgumentException(
                    "ENHANCEMENT_GLOW_THRESHOLD (" + glow + ") must be less than PRESTIGE_HIGH_THRESHOLD (" + high + ") (TK-ENH-4, TK-ENH-6).");
            if (high > maxLevel)
                throw new ArgumentException(
                    "PRESTIGE_HIGH_THRESHOLD (" + high + ") must not exceed MAX_ENHANCEMENT_LEVEL (" + maxLevel + ") (TK-ENH-6).");
        }
    }
}
