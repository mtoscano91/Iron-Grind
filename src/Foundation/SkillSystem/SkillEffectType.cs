using System;

namespace IronGrind.SkillSystem
{
    /// <summary>Effects a skill applies; combinable flags (skill-system.md SkillDefinition Schema).</summary>
    [Flags]
    public enum SkillEffectType : byte
    {
        /// <summary>No effect.</summary>
        None = 0,

        /// <summary>Physical damage.</summary>
        PhysicalDamage = 1,

        /// <summary>Magical damage.</summary>
        MagicalDamage = 2,

        /// <summary>Instant heal.</summary>
        InstantHeal = 4,

        /// <summary>Applies a buff.</summary>
        ApplyBuff = 8,

        /// <summary>Applies a debuff.</summary>
        ApplyDebuff = 16,
    }
}
