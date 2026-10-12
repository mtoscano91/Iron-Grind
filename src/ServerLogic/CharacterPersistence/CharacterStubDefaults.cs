using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>The Level 1 values of a new character's stub record (CR-CP-9, F-CP-3).</summary>
    /// <remarks>
    /// The values are the same for every class. Character Stats owns them and this system stores them verbatim
    /// (F-CP-3); they live here because Character Stats has no Level 1 constants in code.
    /// </remarks>
    public static class CharacterStubDefaults
    {
        /// <summary>Starting level. CR-CP-9 / F-CP-3.</summary>
        public const int LEVEL = 1;

        /// <summary>Starting experience. CR-CP-9 / F-CP-3.</summary>
        public const int EXPERIENCE = 0;

        /// <summary>Starting unspent attribute points. CR-CP-9 / F-CP-3.</summary>
        public const int HELD_FREE_POINTS = 0;

        /// <summary>Starting value of each of STR, DEX, VIT and INT. CR-CP-9 / F-CP-3.</summary>
        public const int BASE_ATTRIBUTE = 10;

        /// <summary>Starting maximum hit points. CR-CP-9 / F-CP-3.</summary>
        public const int MAX_HP = 400;

        /// <summary>Starting maximum mana points. CR-CP-9 / F-CP-3.</summary>
        public const int MAX_MP = 220;

        /// <summary>Starting attack power. CR-CP-9 / F-CP-3.</summary>
        public const int ATTACK_POWER = 30;

        /// <summary>Starting defense. CR-CP-9 / F-CP-3.</summary>
        public const int DEFENSE = 20;

        /// <summary>Starting magic defense. CR-CP-9 / F-CP-3.</summary>
        public const int MAGIC_DEFENSE = 4;

        /// <summary>Starting critical hit chance. CR-CP-9 / F-CP-3.</summary>
        public const float CRIT_CHANCE = 0.065f;

        /// <summary>Starting critical hit multiplier. CR-CP-9 / F-CP-3.</summary>
        public const float CRIT_MULTIPLIER = 1.5f;

        /// <summary>Starting attack range. CR-CP-9 / F-CP-3.</summary>
        public const float ATTACK_RANGE = 3.0f;

        /// <summary>Starting attack speed multiplier. CR-CP-9 / F-CP-3.</summary>
        public const float ATTACK_SPEED_MULTIPLIER = 1.030f;

        /// <summary>Starting movement speed. CR-CP-9 / F-CP-3.</summary>
        public const float MOVEMENT_SPEED = 5.0f;

        /// <summary>Starting current hit points. CR-CP-9 / F-CP-3.</summary>
        public const float CURRENT_HP = 400f;

        /// <summary>Starting current mana points. CR-CP-9 / F-CP-3.</summary>
        public const float CURRENT_MP = 220f;

        /// <summary>Starting gold balance. CR-CP-9 / F-CP-3.</summary>
        public const uint GOLD_BALANCE = 0u;

        /// <summary>Starting gold version. CR-CP-9 / F-CP-3.</summary>
        public const uint GOLD_VERSION = 0u;

        /// <summary>Starting save version; an insert does not add 1. CR-CP-9 / F-CP-3.</summary>
        public const uint SAVE_VERSION = 0u;

        /// <summary>Builds a new Level 1 record for the given account, character and class.</summary>
        /// <param name="accountId">The owning account.</param>
        /// <param name="characterId">The new character's id.</param>
        /// <param name="classType">Raw class type; stored as given, the stats do not depend on it.</param>
        /// <returns>
        /// A record with the Level 1 values, an empty name (the GDD gives the stub none), <see cref="ZoneID.Invalid"/>
        /// as the last zone (first-login sentinel), empty gear and inventory, and default timestamps (the store stamps them).
        /// </returns>
        public static CharacterRecord CreateRecord(AccountID accountId, CharacterID characterId, byte classType)
        {
            return new CharacterRecord
            {
                CharacterID = characterId,
                AccountID = accountId,
                CharacterName = string.Empty,
                ClassType = classType,
                Level = LEVEL,
                Experience = EXPERIENCE,
                HeldFreePoints = HELD_FREE_POINTS,
                STR = BASE_ATTRIBUTE,
                DEX = BASE_ATTRIBUTE,
                VIT = BASE_ATTRIBUTE,
                INT = BASE_ATTRIBUTE,
                MaxHP = MAX_HP,
                MaxMP = MAX_MP,
                AttackPower = ATTACK_POWER,
                Defense = DEFENSE,
                MagicDefense = MAGIC_DEFENSE,
                CritChance = CRIT_CHANCE,
                CritMultiplier = CRIT_MULTIPLIER,
                AttackRange = ATTACK_RANGE,
                AttackSpeedMultiplier = ATTACK_SPEED_MULTIPLIER,
                MovementSpeed = MOVEMENT_SPEED,
                CurrentHP = CURRENT_HP,
                CurrentMP = CURRENT_MP,
                GoldBalance = GOLD_BALANCE,
                GoldVersion = GOLD_VERSION,
                LastZoneID = ZoneID.Invalid,
                SaveVersion = SAVE_VERSION,
            };
        }
    }
}
