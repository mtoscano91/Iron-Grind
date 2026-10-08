using System;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.DamageCalculation
{
    /// <summary>
    /// Stateless damage resolver (design/gdd/damage-calculation.md Core Rules 1-3, Resolution Sequence,
    /// F-DC-1, F-DC-4). Reads stats, never writes them: it fires no event, awards no XP and holds no
    /// Leveling reference. Damage Calculation Story 001 implements Steps 2, 3, 9 and 11; Steps 1, 4-8 and
    /// 10 are marked below for later stories.
    /// </summary>
    /// <remarks>
    /// Server-side only. Nothing in this file references client, UI or networking types, so moving it to a
    /// server-only assembly later is an asmdef change only. Zero heap allocation on the normal path.
    /// </remarks>
    public sealed class DamageCalculator
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly DamageCalculationConfig _config;

        /// <summary>Builds a calculator over an injected stats container and tuning config.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> or <paramref name="config"/> is null.</exception>
        public DamageCalculator(IronGrind.CharacterStats.CharacterStats stats, DamageCalculationConfig config)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Resolves one hit. Invalid calls (<paramref name="baseDamage"/> at most 0 or above the config's
        /// MaxBaseDamage, or attacker equal to target) return <see cref="DamageResult.Rejected"/> and log
        /// an error in editor and development builds only; no stat is read in that case. A target with no
        /// stat record has Defense 0.
        /// </summary>
        /// <param name="baseDamage">Caller-computed physical base (the attacker's AttackPower), in [1, MaxBaseDamage].</param>
        /// <param name="attackerId">The attacking entity.</param>
        /// <param name="targetId">The entity being hit.</param>
        /// <param name="context">Echoed unchanged into the result.</param>
        public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
        {
            // Guards (Edge Cases: BaseDamage = 0, AttackerID == TargetID) - before any stat read.
            if (baseDamage <= 0)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] BaseDamage must be at least 1 (got {baseDamage}); returning 0 damage.");
#endif
                return DamageResult.Rejected(context);
            }

            if (baseDamage > _config.MaxBaseDamage)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] BaseDamage must be at most {_config.MaxBaseDamage} (got {baseDamage}); returning 0 damage.");
#endif
                return DamageResult.Rejected(context);
            }

            if (attackerId == targetId)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] AttackerID equals TargetID ({attackerId}); self-damage is illegal, returning 0 damage.");
#endif
                return DamageResult.Rejected(context);
            }

            // Step 1 - read attacker crit stats: Story 003.

            // Step 2 - read target physical defense.
            int defense = _stats.GetEffectiveStat(targetId, StatID.Defense);

            // Step 3 - physical mitigation (F-DC-1), float arithmetic.
            float physicalMitigated = Mathf.Max(baseDamage * _config.MinDamageFraction, baseDamage - defense);

            // Steps 4-7 - elemental weapon data, magic defense, elemental mitigation, pre-crit sum: Story 002.
            float damageAfterCrit = physicalMitigated;

            // Step 8 - critical strike evaluation: Story 003.

            // Step 9 - floor clamp (F-DC-4).
            int physicalDamage = Mathf.FloorToInt(physicalMitigated);
            int finalDamage = Mathf.Max(1, Mathf.FloorToInt(damageAfterCrit));

            // Step 10 - kill detection: Story 004.

            // Step 11 - return.
            return new DamageResult(
                physicalDamage: physicalDamage,
                elementalDamage: 0,
                finalDamage: finalDamage,
                isCrit: false,
                isKill: false,
                damageContext: context,
                hasElementalContribution: false);
        }
    }
}
