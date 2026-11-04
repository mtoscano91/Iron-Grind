namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Identifies a buff or debuff type. Backed by <see cref="uint"/> to support a large
    /// number of distinct status effects without collision risk.
    /// </summary>
    /// <remarks>
    /// The MVP values are the Skill System's 10 buff and debuff skills (skill-system.md OQ-SK-6, recorded in the
    /// Skill Roster section and in <c>entities.yaml</c>; Skill System Story 012). Values are stable: they are
    /// persisted in <c>SkillDatabase.asset</c> and will travel in buff-bar wire messages, so never renumber a
    /// member. Add new buffs at the end.
    /// </remarks>
    public enum BuffID : uint
    {
        /// <summary>Reserved: no buff (entities.yaml <c>BuffID(0)</c>). Never assigned to a definition.</summary>
        Invalid = 0,

        /// <summary>Warrior Rend: target Defense −15% for 100 ticks.</summary>
        Rend = 1,

        /// <summary>Warrior Rush: self MovementSpeed +40% for 60 ticks.</summary>
        Rush = 2,

        /// <summary>Warrior Shield Bash: target MovementSpeed −30% for 60 ticks.</summary>
        ShieldBash = 3,

        /// <summary>Warrior War Cry: self AttackPower +20% for 200 ticks.</summary>
        WarCry = 4,

        /// <summary>Warrior Savage Blow: target Defense −20% for 100 ticks.</summary>
        SavageBlow = 5,

        /// <summary>Warrior Mortal Wound: target AttackPower −15% for 200 ticks.</summary>
        MortalWound = 6,

        /// <summary>Healer Prayer: 2.0 HP per tick for 100 ticks.</summary>
        Prayer = 7,

        /// <summary>Healer Bless: AttackPower +10% and Defense +10% for 600 ticks.</summary>
        Bless = 8,

        /// <summary>Healer Smite Armor: target Defense −15% for 120 ticks.</summary>
        SmiteArmor = 9,

        /// <summary>Healer Holy Shield: self Defense +25% and MagicDefense +25% for 200 ticks.</summary>
        HolyShield = 10,
    }
}
