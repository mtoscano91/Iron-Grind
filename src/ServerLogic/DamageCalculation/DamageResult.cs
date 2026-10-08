namespace IronGrind.DamageCalculation
{
    /// <summary>
    /// Immutable result of one damage resolution (design/gdd/damage-calculation.md, DamageResult table
    /// and Step 11). <see cref="FinalDamage"/> is the only authoritative value applied to health;
    /// <see cref="PhysicalDamage"/> and <see cref="ElementalDamage"/> are pre-crit breakdowns for
    /// VFX/logging, so callers must not assume they sum to <see cref="FinalDamage"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="FinalDamage"/> of 0 means the call was rejected (see <see cref="Rejected"/>): any
    /// connecting hit deals at least 1. <c>default(DamageResult)</c> therefore reads as a rejected
    /// <see cref="DamageContext.PhysicalAuto"/> call.
    /// </remarks>
    public readonly struct DamageResult
    {
        /// <summary>
        /// The result of a rejected call: no damage, no flags, <paramref name="damageContext"/> echoed.
        /// </summary>
        public static DamageResult Rejected(DamageContext damageContext)
        {
            return new DamageResult(
                physicalDamage: 0,
                elementalDamage: 0,
                finalDamage: 0,
                isCrit: false,
                isKill: false,
                damageContext: damageContext,
                hasElementalContribution: false);
        }

        /// <summary>Builds a result. See the property docs for each field's contract.</summary>
        public DamageResult(
            int physicalDamage,
            int elementalDamage,
            int finalDamage,
            bool isCrit,
            bool isKill,
            DamageContext damageContext,
            bool hasElementalContribution)
        {
            PhysicalDamage = physicalDamage;
            ElementalDamage = elementalDamage;
            FinalDamage = finalDamage;
            IsCrit = isCrit;
            IsKill = isKill;
            DamageContext = damageContext;
            HasElementalContribution = hasElementalContribution;
        }

        /// <summary>FloorToInt of the physical mitigated value - pre-crit physical contribution.</summary>
        public int PhysicalDamage { get; }

        /// <summary>FloorToInt of the elemental mitigated value - pre-crit elemental contribution; may be 0 for an elemental weapon.</summary>
        public int ElementalDamage { get; }

        /// <summary>Post-crit authoritative damage, at least 1 for any connecting hit and 0 for a rejected call. Pass to ApplyDamage.</summary>
        public int FinalDamage { get; }

        /// <summary>Whether the crit roll succeeded; independent of damage magnitude.</summary>
        public bool IsCrit { get; }

        /// <summary>Whether FinalDamage is at least the target's CurrentHP at kill detection.</summary>
        public bool IsKill { get; }

        /// <summary>The call context, echoed from the input.</summary>
        public DamageContext DamageContext { get; }

        /// <summary>True when the elemental mitigated value is above zero; key elemental VFX/audio on this, not on ElementalDamage.</summary>
        public bool HasElementalContribution { get; }
    }
}
