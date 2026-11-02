namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Why a skill cast was rejected; values match entities.yaml. 0 is deliberately unassigned: 0 means
    /// accepted / no rejection. V-1 and V-2 failures produce no result.
    /// </summary>
    public enum SkillCastRejectionCode : byte
    {
        /// <summary>The skill id is not valid or not in the database.</summary>
        InvalidSkill = 1,

        /// <summary>The caster has not unlocked the skill.</summary>
        SkillLocked = 2,

        /// <summary>The skill is on cooldown.</summary>
        OnCooldown = 3,

        /// <summary>The target is not valid for the skill.</summary>
        InvalidTarget = 4,

        /// <summary>The target is out of range.</summary>
        OutOfRange = 5,

        /// <summary>The caster lacks the mana.</summary>
        InsufficientMana = 6,

        /// <summary>Internal server error.</summary>
        ServerError = 7,

        /// <summary>The caster is silenced.</summary>
        Silenced = 8,

        /// <summary>The caster is not alive.</summary>
        CasterNotAlive = 9,
    }
}
