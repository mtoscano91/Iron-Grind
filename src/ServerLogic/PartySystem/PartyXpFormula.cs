using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// F-PS-1 party XP deduction (party-system.md; Party System Story 001, TR-party-005). Pure and allocation-free.
    /// </summary>
    public static class PartyXpFormula
    {
        /// <summary>
        /// <c>Mathf.RoundToInt(xpBase x (1f - rate x (nEligible - 1)))</c> in float, round half to even.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Float, not exact decimal: <c>1f - 0.10f</c> is 0.89999998, so <c>XP_base</c> 5 at N 2 is 4.4999998 and
        /// rounds to 4, not 5 (GDD edge case corrected 2026-11-18).
        /// </para>
        /// <para>
        /// A multiplier of 0 or less, or NaN (a NaN rate), returns 0. A multiplier of 1 or more (N 1, a rate of 0,
        /// or a negative rate the config refuses) returns <paramref name="xpBase"/> unchanged: the deduction never
        /// awards more than the base, and <c>(float)int.MaxValue</c> (2^31) never reaches the int cast.
        /// </para>
        /// </remarks>
        /// <param name="xpBase">The mob's base XP.</param>
        /// <param name="nEligible">
        /// Eligible party members; 0 or less returns 0 (unguarded N 0 would give x1.10).
        /// </param>
        /// <param name="rate">XP_PARTY_DEDUCTION_RATE.</param>
        /// <returns>The XP each eligible member receives; 0 for xpBase or nEligible of 0 or less, or a
        /// non-positive or NaN multiplier.</returns>
        public static int Compute(int xpBase, int nEligible, float rate)
        {
            if (nEligible <= 0 || xpBase <= 0)
            {
                return 0;
            }

            float multiplier = 1f - rate * (float)(nEligible - 1);
            if (!(multiplier > 0f))
            {
                return 0;
            }

            if (multiplier >= 1f)
            {
                return xpBase;
            }

            return Mathf.RoundToInt((float)xpBase * multiplier);
        }
    }
}
