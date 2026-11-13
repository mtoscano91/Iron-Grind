using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Static data for one mob type (design/gdd/enemy-ai.md, MobDefinition Schema). A sealed, immutable reference
    /// type shared by the registry and every consumer (the GDD schema chose a sealed class).
    /// </summary>
    /// <remarks>
    /// <c>LootTableRef</c>, <c>GoldMin</c> and <c>GoldMax</c> are deliberately absent: Loot Table owns them, keyed by
    /// <see cref="MobTypeID"/>. Values are not checked here; see <see cref="MobDefinitionValidator"/> (CR-AI-7).
    /// </remarks>
    public sealed class MobDefinition
    {
        /// <summary>Initializes a definition. Use named arguments at call sites.</summary>
        public MobDefinition(
            MobTypeID mobTypeID,
            string displayName,
            int baseMaxHP,
            int baseAttackPower,
            int baseDefense,
            float attackRange,
            float moveSpeed,
            float aggroRange,
            float leashRange,
            int attackWindUpTicks,
            int attackRecoveryTicks,
            int attackCooldownTicks,
            int killXP,
            MobTier mobTier,
            float enragedChance,
            float enragedMaxHPMultiplier,
            float enragedAttackPowerMultiplier,
            float enragedXPMultiplier,
            int enragedLootQualityBonusTier)
        {
            MobTypeID = mobTypeID;
            DisplayName = displayName;
            BaseMaxHP = baseMaxHP;
            BaseAttackPower = baseAttackPower;
            BaseDefense = baseDefense;
            AttackRange = attackRange;
            MoveSpeed = moveSpeed;
            AggroRange = aggroRange;
            LeashRange = leashRange;
            AttackWindUpTicks = attackWindUpTicks;
            AttackRecoveryTicks = attackRecoveryTicks;
            AttackCooldownTicks = attackCooldownTicks;
            KillXP = killXP;
            MobTier = mobTier;
            EnragedChance = enragedChance;
            EnragedMaxHPMultiplier = enragedMaxHPMultiplier;
            EnragedAttackPowerMultiplier = enragedAttackPowerMultiplier;
            EnragedXPMultiplier = enragedXPMultiplier;
            EnragedLootQualityBonusTier = enragedLootQualityBonusTier;
        }

        /// <summary>Registry key and join key to Loot Table. Never <see cref="MobTypeID.Invalid"/>.</summary>
        public MobTypeID MobTypeID { get; }

        /// <summary>Display name.</summary>
        public string DisplayName { get; }

        /// <summary>Base maximum hit points; must be greater than 0.</summary>
        public int BaseMaxHP { get; }

        /// <summary>Base attack power; must be greater than 0.</summary>
        public int BaseAttackPower { get; }

        /// <summary>Base defense; must be 0 or more.</summary>
        public int BaseDefense { get; }

        /// <summary>Attack reach in metres; at least <see cref="MobDefinitionValidator.ATTACK_RANGE_MIN"/>.</summary>
        public float AttackRange { get; }

        /// <summary>Move speed in metres per second; must be greater than 0.</summary>
        public float MoveSpeed { get; }

        /// <summary>Aggro radius in metres; must be greater than 0.</summary>
        public float AggroRange { get; }

        /// <summary>Leash radius in metres; at least <see cref="AggroRange"/> plus <see cref="MobDefinitionValidator.MIN_LEASH_AGGRO_GAP"/> (CR-AI-7, EC-AI-4).</summary>
        public float LeashRange { get; }

        /// <summary>Attack wind-up in ticks (telegraph); at least <see cref="MobDefinitionValidator.MIN_WIND_UP_TICKS"/> (CR-AI-7).</summary>
        public int AttackWindUpTicks { get; }

        /// <summary>Attack recovery in ticks; 0 or more.</summary>
        public int AttackRecoveryTicks { get; }

        /// <summary>Attack cooldown in ticks; below the recommended minimum of 10 it only warns.</summary>
        public int AttackCooldownTicks { get; }

        /// <summary>XP awarded on kill; must be greater than 0.</summary>
        public int KillXP { get; }

        /// <summary>Tier governing the respawn delay (Mob Spawning CR-MS-9).</summary>
        public MobTier MobTier { get; }

        /// <summary>Chance a spawn is Enraged, in [0, 1].</summary>
        public float EnragedChance { get; }

        /// <summary>Enraged max HP multiplier, in [1, 3].</summary>
        public float EnragedMaxHPMultiplier { get; }

        /// <summary>Enraged attack power multiplier, in [1, 2].</summary>
        public float EnragedAttackPowerMultiplier { get; }

        /// <summary>Enraged XP multiplier, in [1, 3].</summary>
        public float EnragedXPMultiplier { get; }

        /// <summary>Enraged loot quality bonus tier, in [0, 2].</summary>
        public int EnragedLootQualityBonusTier { get; }

        /// <summary>
        /// F-AI-1: ticks between attack starts = WindUp + Recovery + max(0, Cooldown - Recovery).
        /// </summary>
        /// <param name="def">The definition. Must not be null.</param>
        public static int EffectiveAttackIntervalTicks(MobDefinition def)
        {
            int extraCooldown = def.AttackCooldownTicks - def.AttackRecoveryTicks;
            if (extraCooldown < 0)
            {
                extraCooldown = 0;
            }

            return def.AttackWindUpTicks + def.AttackRecoveryTicks + extraCooldown;
        }
    }
}
