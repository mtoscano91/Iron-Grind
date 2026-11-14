using System;
using IronGrind.CharacterStats;
using IronGrind.Randomness;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Rolls the Enraged variant for a new spawn and seeds the mob's base stats (CR-AI-12, F-AI-E-1,
    /// AC-AI-13, AC-AI-14, AC-AI-15). Stateless.
    /// </summary>
    /// <remarks>
    /// <para>CR-AI-12 deviation (user decision 2026-11-13): this seeder, not the <c>MobController</c>
    /// constructor, takes the <see cref="IRandomProvider"/>. Story 008 calls it from the spawn handler.</para>
    /// <para>ADR-013: exactly one <see cref="IRandomProvider.NextDouble"/> per spawn, always, even when
    /// the chance is 0 or 1, so the shared stream stays deterministic. No per-mob System.Random,
    /// no UnityEngine.Random.</para>
    /// <para><see cref="Mathf.RoundToInt(float)"/> rounds .5 to even (4.5 gives 4, 7.5 gives 8).</para>
    /// <para>Caller contract: (1) do not call from inside a <c>CharacterStats.OnStatChanged</c> handler —
    /// re-entrant writes throw in Editor/Development builds and are silently dropped in release builds,
    /// leaving partial stats; (2) pass a fresh or cleared <c>EntityID</c> — a stale buff or equipment
    /// modifier on MaxHP would change the effective MaxHP that <c>SetCurrentHP</c> clamps to.</para>
    /// </remarks>
    public static class MobSpawnSeeder
    {
        /// <summary>
        /// Rolls the Enraged variant and writes MaxHP, AttackPower, Defense and CurrentHP through
        /// <paramref name="writer"/>.
        /// </summary>
        /// <param name="entityId">The spawned mob entity.</param>
        /// <param name="definition">The mob's definition. Must not be null.</param>
        /// <param name="random">The process-level random provider. Must not be null.</param>
        /// <param name="writer">Stat writer. Must not be null.</param>
        /// <returns>The spawn variant (Enraged flag, effective kill XP, loot tier shift).</returns>
        /// <exception cref="ArgumentNullException">
        /// An argument is null; thrown before any RNG call or stat write.
        /// </exception>
        public static MobSpawnVariant Seed(
            EntityID entityId,
            MobDefinition definition,
            IRandomProvider random,
            IMobStatWriter writer)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            // Always exactly one draw (no short-circuit at chance 0 or 1). Float chance widened to double.
            double roll = random.NextDouble();
            bool isEnraged = roll < (double)definition.EnragedChance;

            int maxHp = isEnraged
                ? Mathf.RoundToInt(definition.BaseMaxHP * definition.EnragedMaxHPMultiplier)
                : definition.BaseMaxHP;
            int attackPower = isEnraged
                ? Mathf.RoundToInt(definition.BaseAttackPower * definition.EnragedAttackPowerMultiplier)
                : definition.BaseAttackPower;

            // Order matters: SetCurrentHP clamps to the effective MaxHP, so MaxHP is written first.
            // SetBaseStat(StatID.CurrentHP) would not set HP (GetCurrentHP does not read the int array).
            writer.SetBaseStat(entityId, StatID.MaxHP, maxHp);
            writer.SetBaseStat(entityId, StatID.AttackPower, attackPower);
            writer.SetBaseStat(entityId, StatID.Defense, definition.BaseDefense);
            writer.SetCurrentHP(entityId, maxHp);

            int effectiveKillXP = isEnraged
                ? Mathf.RoundToInt(definition.KillXP * definition.EnragedXPMultiplier)
                : definition.KillXP;
            int tierShift = isEnraged ? definition.EnragedLootQualityBonusTier : 0;

            return new MobSpawnVariant(isEnraged, effectiveKillXP, tierShift);
        }
    }
}
