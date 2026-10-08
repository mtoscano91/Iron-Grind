namespace IronGrind.DamageCalculation
{
    /// <summary>
    /// Server-side call context of a damage resolution (design/gdd/damage-calculation.md Core Rule 2).
    /// Echoed unchanged in <see cref="DamageResult.DamageContext"/> for VFX/audio routing; it has no
    /// effect on the formula at MVP. Not the wire type <c>DamageType</c> - no mapping happens here.
    /// </summary>
    public enum DamageContext
    {
        /// <summary>Physical damage from an auto-attack.</summary>
        PhysicalAuto = 0,

        /// <summary>Physical damage from a skill.</summary>
        PhysicalSkill = 1,

        /// <summary>Magical damage from a skill.</summary>
        MagicalSkill = 2,
    }
}
