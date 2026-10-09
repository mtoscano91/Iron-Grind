using System;
using IronGrind.CharacterStats;
using IronGrind.EnhancementSystem;
using IronGrind.ItemDatabase;
using IronGrind.Randomness;
using UnityEngine;

namespace IronGrind.DamageCalculation
{
    /// <summary>
    /// Stateless damage resolver (design/gdd/damage-calculation.md Core Rules 1-3, Resolution Sequence,
    /// F-DC-1, F-DC-2, F-DC-4). Reads stats, never writes them: it fires no event, awards no XP and holds
    /// no Leveling reference. Damage Calculation Story 001 implements Steps 2, 3, 9 and 11; Story 002
    /// adds Steps 4-7 and the elemental fields of Step 11; Story 004 adds Step 10 (kill detection and the
    /// dead-entity guard); Story 003 adds Step 1 (attacker crit stats) and Step 8 (critical strike roll,
    /// F-DC-3) with an injected <see cref="IRandomProvider"/> (ADR-013).
    /// </summary>
    /// <remarks>
    /// Server-side only. Nothing in this file references client, UI or networking types, so moving it to a
    /// server-only assembly later is an asmdef change only. Zero heap allocation on the normal path.
    /// Exactly one <see cref="IRandomProvider.NextFloat"/> draw per accepted <c>Calculate</c> call (also
    /// when CritChance is 0) and none on a rejected call.
    /// </remarks>
    public sealed class DamageCalculator
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly IEquippedWeaponQuery _weapons;
        private readonly IItemDatabase _items;
        private readonly IEnhancementBonusProvider _bonuses;
        private readonly DamageCalculationConfig _config;
        private readonly IRandomProvider _random;

        /// <summary>Builds a calculator over injected collaborators and a tuning config.</summary>
        /// <param name="stats">Stat container read for Defense and MagicDefense.</param>
        /// <param name="weapons">Source of the attacker's equipped weapon id and enhancement level.</param>
        /// <param name="items">Item Database used to read the weapon's element, base elemental damage and tier.</param>
        /// <param name="bonuses">Enhancement bonus provider (F-ENH-2) giving the enhanced elemental damage.</param>
        /// <param name="config">Tuning values.</param>
        /// <param name="random">Server random source for the crit roll (ADR-013); one generator per zone process.</param>
        /// <exception cref="ArgumentNullException">Any parameter is null.</exception>
        public DamageCalculator(
            IronGrind.CharacterStats.CharacterStats stats,
            IEquippedWeaponQuery weapons,
            IItemDatabase items,
            IEnhancementBonusProvider bonuses,
            DamageCalculationConfig config,
            IRandomProvider random)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _bonuses = bonuses ?? throw new ArgumentNullException(nameof(bonuses));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// Resolves one hit. Invalid calls (<paramref name="baseDamage"/> at most 0 or above the config's
        /// MaxBaseDamage, or attacker equal to target) return <see cref="DamageResult.Rejected"/> and log
        /// an error in editor and development builds only; no stat is read in that case.
        /// </summary>
        /// <remarks>
        /// <para>A target with no stat record has Defense 0 and MagicDefense 0.</para>
        /// <para>When the attacker's equipped weapon id is not in the Item Database, or the item has no
        /// equipment data, the hit has no elemental contribution, a dev error is logged (editor and
        /// development builds only) and the physical result is still returned.</para>
        /// <para>Kill detection (Step 10) sets <see cref="DamageResult.IsKill"/> when the final damage is
        /// at least the target's current HP; it only reports, and never applies damage, awards XP or fires
        /// an event. When the target's current HP is not above 0 (dead entity), IsKill is false, a dev
        /// error is logged (editor and development builds only) and every other field is still computed.
        /// A target with no HP record reads as 0 HP and takes the same path (user decision 2026-10-08).</para>
        /// <para>Critical strike (Steps 1 and 8): the attacker's CritChance and CritMultiplier are read
        /// (a stat never set reads 0), one float is drawn from the injected provider and the hit is a crit
        /// when the roll is strictly below CritChance. A crit multiplies the pre-crit sum once;
        /// <see cref="DamageResult.PhysicalDamage"/> and <see cref="DamageResult.ElementalDamage"/> stay
        /// pre-crit. A crit whose CritMultiplier reads below 1.0 uses 1.0 and logs a dev error (editor and
        /// development builds only); a non-crit hit never checks the multiplier (user decision 2026-10-08).</para>
        /// </remarks>
        /// <param name="baseDamage">Caller-computed physical base (the attacker's AttackPower), in [1, MaxBaseDamage].</param>
        /// <param name="attackerId">The attacking entity.</param>
        /// <param name="targetId">The entity being hit.</param>
        /// <param name="context">Echoed unchanged into the result.</param>
        /// <returns>The resolved hit; a rejected call has <see cref="DamageResult.FinalDamage"/> 0.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Propagated unchanged from <see cref="IEnhancementBonusProvider.GetElementalBonus"/> when an
        /// elemental weapon has a <see cref="GearTier.None"/> tier or an out-of-range enhancement level;
        /// the resolver does not catch it.
        /// </exception>
        public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
        {
            // Guards (Edge Cases: BaseDamage = 0, AttackerID == TargetID) - before any stat read.
            if (IsInvalidRequest(baseDamage, attackerId, targetId))
                return DamageResult.Rejected(context);

            // Step 1 - read attacker crit stats (a stat never set reads 0; Character Stats clamps set values).
            float critChance = _stats.GetEffectiveStatFloat(attackerId, StatID.CritChance);
            float critMultiplier = _stats.GetEffectiveStatFloat(attackerId, StatID.CritMultiplier);

            // Step 2 - read target physical defense.
            int defense = _stats.GetEffectiveStat(targetId, StatID.Defense);

            // Step 3 - physical mitigation (F-DC-1), float arithmetic.
            float physicalMitigated = Mathf.Max(baseDamage * _config.MinDamageFraction, baseDamage - defense);

            // Step 4 - elemental weapon data (0 when no elemental weapon).
            int elementalBonus = ResolveElementalBonus(attackerId);

            // Steps 5-6 - target magic defense and elemental mitigation (F-DC-2); skipped at bonus 0.
            float elementalMitigated = ResolveElementalMitigated(targetId, elementalBonus);

            // Step 7 - pre-crit sum.
            float preCritDamage = physicalMitigated + elementalMitigated;
            float damageAfterCrit = preCritDamage;

            // Step 8 - critical strike (F-DC-3): one unconditional draw, strict <, multiplier applied once.
            bool isCrit = _random.NextFloat() < critChance;
            if (isCrit)
            {
                // Negated so that a NaN multiplier also takes the fallback path.
                if (!(critMultiplier >= 1f))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[DamageCalculator] Attacker {attackerId} crit with CritMultiplier below 1.0 (got {critMultiplier}); using 1.0.");
#endif
                    critMultiplier = 1f;
                }

                damageAfterCrit = preCritDamage * critMultiplier;
            }

            // Step 9 - floor clamp (F-DC-4).
            int physicalDamage = Mathf.FloorToInt(physicalMitigated);
            int finalDamage = Mathf.Max(1, Mathf.FloorToInt(damageAfterCrit));

            // Step 10 - kill detection (Core Rule 4); report only, no write.
            bool isKill = ResolveIsKill(targetId, finalDamage);

            // Step 11 - return.
            return new DamageResult(
                physicalDamage: physicalDamage,
                elementalDamage: Mathf.FloorToInt(elementalMitigated),
                finalDamage: finalDamage,
                isCrit: isCrit,
                isKill: isKill,
                damageContext: context,
                hasElementalContribution: elementalMitigated > 0f);
        }

        /// <summary>
        /// Step 10: true when <paramref name="finalDamage"/> is at least the target's current HP (equality
        /// is a kill). HP is read as the float pool value through <c>GetCurrentHP</c>, not the floored int
        /// of <c>GetEffectiveStat</c>, so 100.5 HP is not killed by 100 damage. Dead-entity guard: a target
        /// at 0 HP (or with no HP record, which reads as 0, or a NaN HP) is never reported as a kill and a dev error is
        /// logged (editor and development builds only). Reads only; never writes HP or fires an event.
        /// </summary>
        private bool ResolveIsKill(EntityID targetId, int finalDamage)
        {
            float currentHp = _stats.GetCurrentHP(targetId);
            // Negated so that a NaN HP also takes the guard path instead of failing both comparisons silently.
            if (!(currentHp > 0f))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] Target {targetId} has 0 HP, NaN HP or no HP record (dead-entity guard); IsKill is false.");
#endif
                return false;
            }

            return finalDamage >= currentHp;
        }

        /// <summary>
        /// Guards: true when the call must be rejected (base damage outside [1, MaxBaseDamage], or
        /// attacker equal to target). Logs the reason in editor and development builds only.
        /// </summary>
        private bool IsInvalidRequest(int baseDamage, EntityID attackerId, EntityID targetId)
        {
            if (baseDamage <= 0)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] BaseDamage must be at least 1 (got {baseDamage}); returning 0 damage.");
#endif
                return true;
            }

            if (baseDamage > _config.MaxBaseDamage)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] BaseDamage must be at most {_config.MaxBaseDamage} (got {baseDamage}); returning 0 damage.");
#endif
                return true;
            }

            if (attackerId == targetId)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] AttackerID equals TargetID ({attackerId}); self-damage is illegal, returning 0 damage.");
#endif
                return true;
            }

            return false;
        }

        /// <summary>
        /// Steps 5-6 (F-DC-2): the elemental damage left after the target's MagicDefense, as a float.
        /// Returns 0 without reading MagicDefense when <paramref name="elementalBonus"/> is 0 or less.
        /// </summary>
        private float ResolveElementalMitigated(EntityID targetId, int elementalBonus)
        {
            if (elementalBonus <= 0)
                return 0f;

            int magicDefense = _stats.GetEffectiveStat(targetId, StatID.MagicDefense);
            float absorbed = 1f - magicDefense / (magicDefense + _config.KMagic);
            return elementalBonus * Mathf.Max(_config.MinElementalFraction, absorbed);
        }

        /// <summary>
        /// Step 4: the enhanced elemental damage of the attacker's weapon, or 0 when there is no weapon,
        /// the weapon is non-elemental, or its definition is missing or not equipment. The Item Database
        /// is not touched when no weapon is equipped; the enhancement-level query and the bonus provider
        /// are not touched for a non-elemental weapon. Exceptions from the provider propagate.
        /// </summary>
        private int ResolveElementalBonus(EntityID attackerId)
        {
            ItemID weaponId = _weapons.GetEquippedWeaponID(attackerId);
            if (weaponId == ItemID.Invalid)
                return 0;

            // EquipmentData is a [SerializeReference] field, so it is a true null on non-equipment items.
            if (!_items.TryGetItem(weaponId, out ItemDefinition item) || item == null || item.EquipmentData == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[DamageCalculator] Equipped weapon {weaponId} of {attackerId} is missing from the Item Database or is not equipment; no elemental damage applied.");
#endif
                return 0;
            }

            EquipmentData equipment = item.EquipmentData;
            if (equipment.ElementType == ElementType.None)
                return 0;

            int level = _weapons.GetEquippedWeaponEnhancementLevel(attackerId);
            return _bonuses.GetElementalBonus(level, equipment.ElementalDamage, equipment.GearTier, isWeapon: true);
        }
    }
}
