using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.LootTableSystem;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Pure startup validation of one <see cref="MobDefinition"/> (CR-AI-7, EC-AI-4, EC-AI-12, F-AI-E-1 ranges).
    /// Performs no logging (<see cref="MobDefinitionRegistry.Build"/> is the only place that logs).
    /// Each failing rule yields exactly one issue; a NaN or infinite float yields one issue
    /// for that field and skips its range check. Issue messages start with the field name and a colon.
    /// Numbers in messages are formatted with the invariant culture.
    /// </summary>
    /// <remarks>
    /// One rule goes beyond the story's rule list: <see cref="MobTier"/> must be a defined value
    /// (code review 2026-11-13), so a corrupt serialized tier is refused rather than silently accepted.
    /// </remarks>
    public static class MobDefinitionValidator
    {
        /// <summary>
        /// Minimum attack wind-up ticks = ceil(0.6 s x TICK_RATE_HZ) (CR-AI-7; 12 at 20 Hz).
        /// Integer form of the ceiling: (6 * hz + 9) / 10.
        /// </summary>
        public const int MIN_WIND_UP_TICKS = (6 * ServerTickLoop.TICK_RATE_HZ + 9) / 10;

        /// <summary>Minimum gap in metres between aggro and leash range (CR-AI-7, EC-AI-4).</summary>
        public const float MIN_LEASH_AGGRO_GAP = 2.0f;

        /// <summary>Minimum attack range in metres.</summary>
        public const float ATTACK_RANGE_MIN = 0.5f;

        /// <summary>Cooldown below this accepts the definition but warns (non-fatal).</summary>
        public const int RECOMMENDED_MIN_COOLDOWN_TICKS = 10;

        /// <summary>Enraged max HP multiplier lower bound.</summary>
        public const float ENRAGED_MAX_HP_MULTIPLIER_MIN = 1.0f;
        /// <summary>Enraged max HP multiplier upper bound.</summary>
        public const float ENRAGED_MAX_HP_MULTIPLIER_MAX = 3.0f;
        /// <summary>Enraged attack power multiplier lower bound.</summary>
        public const float ENRAGED_ATTACK_POWER_MULTIPLIER_MIN = 1.0f;
        /// <summary>Enraged attack power multiplier upper bound.</summary>
        public const float ENRAGED_ATTACK_POWER_MULTIPLIER_MAX = 2.0f;
        /// <summary>Enraged XP multiplier lower bound.</summary>
        public const float ENRAGED_XP_MULTIPLIER_MIN = 1.0f;
        /// <summary>Enraged XP multiplier upper bound.</summary>
        public const float ENRAGED_XP_MULTIPLIER_MAX = 3.0f;
        /// <summary>Enraged loot quality bonus tier upper bound (lower bound is 0).</summary>
        public const int ENRAGED_LOOT_QUALITY_BONUS_TIER_MAX = 2;

        /// <summary>
        /// Validates one definition (duplicate ids are the registry's concern).
        /// </summary>
        /// <param name="def">The definition; null yields one issue.</param>
        /// <returns>One issue per failed rule; empty means valid.</returns>
        public static List<MobDefinitionValidationIssue> Validate(MobDefinition def)
        {
            var issues = new List<MobDefinitionValidationIssue>();
            if (def == null)
            {
                issues.Add(new MobDefinitionValidationIssue(MobTypeID.Invalid, "Definition: definition is null."));
                return issues;
            }

            MobTypeID id = def.MobTypeID;
            if (id == MobTypeID.Invalid)
            {
                issues.Add(new MobDefinitionValidationIssue(id, "MobTypeID: Invalid sentinel (0) cannot be used."));
            }

            CheckIntegers(issues, id, def);
            CheckCombatRanges(issues, id, def);
            CheckEnraged(issues, id, def);
            return issues;
        }

        /// <summary>
        /// True when the definition is non-null and its AttackCooldownTicks is below
        /// <see cref="RECOMMENDED_MIN_COOLDOWN_TICKS"/>: accepted, but worth one warning.
        /// </summary>
        public static bool HasCooldownWarning(MobDefinition def)
        {
            return def != null && def.AttackCooldownTicks < RECOMMENDED_MIN_COOLDOWN_TICKS;
        }

        // Integer and enum fields: wind-up, recovery, HP, AP, defense, KillXP, loot tier, MobTier.
        private static void CheckIntegers(List<MobDefinitionValidationIssue> issues, MobTypeID id, MobDefinition def)
        {
            if (def.AttackWindUpTicks < MIN_WIND_UP_TICKS)
            {
                Add(issues, id, $"AttackWindUpTicks: {def.AttackWindUpTicks} is below MIN_WIND_UP_TICKS ({MIN_WIND_UP_TICKS}).");
            }

            if (def.AttackRecoveryTicks < 0)
            {
                Add(issues, id, $"AttackRecoveryTicks: {def.AttackRecoveryTicks} must be 0 or more.");
            }

            if (def.BaseMaxHP <= 0)
            {
                Add(issues, id, $"BaseMaxHP: {def.BaseMaxHP} must be greater than 0.");
            }

            if (def.BaseAttackPower <= 0)
            {
                Add(issues, id, $"BaseAttackPower: {def.BaseAttackPower} must be greater than 0.");
            }

            if (def.BaseDefense < 0)
            {
                Add(issues, id, $"BaseDefense: {def.BaseDefense} must be 0 or more.");
            }

            if (def.KillXP <= 0)
            {
                Add(issues, id, $"KillXP: {def.KillXP} must be greater than 0.");
            }

            if (def.EnragedLootQualityBonusTier < 0 || def.EnragedLootQualityBonusTier > ENRAGED_LOOT_QUALITY_BONUS_TIER_MAX)
            {
                Add(issues, id, $"EnragedLootQualityBonusTier: {def.EnragedLootQualityBonusTier} is outside [0, {ENRAGED_LOOT_QUALITY_BONUS_TIER_MAX}].");
            }

            if (!Enum.IsDefined(typeof(MobTier), def.MobTier))
            {
                Add(issues, id, $"MobTier: {(int)def.MobTier} is not a defined tier.");
            }
        }

        // Movement and range fields; a non-finite aggro or leash range skips the leash gap check.
        private static void CheckCombatRanges(List<MobDefinitionValidationIssue> issues, MobTypeID id, MobDefinition def)
        {
            if (IsFinite(issues, id, "MoveSpeed", def.MoveSpeed) && !(def.MoveSpeed > 0f))
            {
                Add(issues, id, $"MoveSpeed: {def.MoveSpeed} must be greater than 0.");
            }

            if (IsFinite(issues, id, "AttackRange", def.AttackRange) && !(def.AttackRange >= ATTACK_RANGE_MIN))
            {
                Add(issues, id, $"AttackRange: {def.AttackRange} is below ATTACK_RANGE_MIN ({ATTACK_RANGE_MIN}).");
            }

            bool aggroFinite = IsFinite(issues, id, "AggroRange", def.AggroRange);
            if (aggroFinite && !(def.AggroRange > 0f))
            {
                Add(issues, id, $"AggroRange: {def.AggroRange} must be greater than 0.");
            }

            bool leashFinite = IsFinite(issues, id, "LeashRange", def.LeashRange);
            if (aggroFinite && leashFinite && !(def.LeashRange >= def.AggroRange + MIN_LEASH_AGGRO_GAP))
            {
                Add(issues, id, $"LeashRange: {def.LeashRange} must be at least AggroRange ({def.AggroRange}) + {MIN_LEASH_AGGRO_GAP}.");
            }
        }

        // Enraged chance and the three enraged multipliers.
        private static void CheckEnraged(List<MobDefinitionValidationIssue> issues, MobTypeID id, MobDefinition def)
        {
            CheckRange(issues, id, "EnragedChance", def.EnragedChance, 0f, 1f);
            CheckRange(issues, id, "EnragedMaxHPMultiplier", def.EnragedMaxHPMultiplier, ENRAGED_MAX_HP_MULTIPLIER_MIN, ENRAGED_MAX_HP_MULTIPLIER_MAX);
            CheckRange(issues, id, "EnragedAttackPowerMultiplier", def.EnragedAttackPowerMultiplier, ENRAGED_ATTACK_POWER_MULTIPLIER_MIN, ENRAGED_ATTACK_POWER_MULTIPLIER_MAX);
            CheckRange(issues, id, "EnragedXPMultiplier", def.EnragedXPMultiplier, ENRAGED_XP_MULTIPLIER_MIN, ENRAGED_XP_MULTIPLIER_MAX);
        }

        // Formats the interpolated message with the invariant culture and adds it as one issue.
        private static void Add(List<MobDefinitionValidationIssue> issues, MobTypeID id, FormattableString message)
        {
            issues.Add(new MobDefinitionValidationIssue(id, message.ToString(CultureInfo.InvariantCulture)));
        }

        // Adds one issue and returns false when the value is NaN or infinite, so the caller skips its range check.
        private static bool IsFinite(List<MobDefinitionValidationIssue> issues, MobTypeID id, string field, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Add(issues, id, $"{field}: {value} is not a finite number.");
                return false;
            }

            return true;
        }

        private static void CheckRange(List<MobDefinitionValidationIssue> issues, MobTypeID id, string field, float value, float min, float max)
        {
            if (IsFinite(issues, id, field, value) && !(value >= min && value <= max))
            {
                Add(issues, id, $"{field}: {value} is outside [{min}, {max}].");
            }
        }
    }
}
