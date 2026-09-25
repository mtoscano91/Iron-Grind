using UnityEngine;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// UI-side preview-only duplication of two small pieces of <see cref="LevelingService"/>
    /// logic that Story 013's Respec Screen (AC-LS-48) needs but has no public accessor for:
    /// the F-3-F-9 derived-stat formulas, and the CR-4.3 respec floor formula.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this duplicates instead of calling <see cref="LevelingService"/> directly.</b>
    /// <c>LevelingService.RecomputeDerivedStats</c> and its private <c>GetAutoAllocIncrement</c>
    /// helper are <c>private</c>. Story 013's scope explicitly forbids touching Leveling
    /// System production code ("this story only consumes existing APIs... as read-only/call-only
    /// consumers") -- so even though this UI code lives in the same <c>IronGrind.Foundation</c>
    /// assembly and could otherwise call an <c>internal</c> member directly (as
    /// <see cref="LevelingService.GetLevelTierMultiplier"/> already IS called directly by
    /// <see cref="RespecScreenPresenter"/> and <see cref="LevelUpOverlayPresenter"/> -- that one
    /// is already <c>internal</c>, so no duplication was needed there), widening
    /// <c>RecomputeDerivedStats</c>'s or <c>GetAutoAllocIncrement</c>'s visibility was not an
    /// option this session.</para>
    /// <para><b>KNOWN DRIFT RISK.</b> The formula bodies below are copied verbatim from
    /// <c>LevelingService.RecomputeDerivedStats</c> (F-3-F-9) and from
    /// <c>LevelingService.TryApplyRespec</c>'s CR-4.3 floor check
    /// (<c>floor = 10 + (level - 1) * increment</c>), as of Story 013's implementation date.
    /// There is no compiler enforcement keeping these two copies in sync -- if either formula
    /// set changes in <see cref="LevelingService"/>, this file must be updated by hand.
    /// Flagged in Story 013's completion report for the Leveling System owner: promoting
    /// <c>RecomputeDerivedStats</c> and <c>GetAutoAllocIncrement</c> to <c>internal</c> in a
    /// future story would let this file be deleted in favor of calling them directly.</para>
    /// <para>Read-only: this class never writes to <see cref="CharacterStats"/>.</para>
    /// </remarks>
    public static class LevelingFormulaPreview
    {
        /// <summary>Projected F-3-F-9 derived stats for a given set of primary-attribute totals and tier.</summary>
        public readonly struct DerivedStatsPreview
        {
            public readonly int MaxHP;
            public readonly int MaxMP;
            public readonly int AttackPower;
            public readonly int Defense;
            public readonly int MagicDefense;
            public readonly float CritChance;
            public readonly float AttackSpeedMultiplier;

            public DerivedStatsPreview(
                int maxHp, int maxMp, int attackPower, int defense,
                int magicDefense, float critChance, float attackSpeedMultiplier)
            {
                MaxHP = maxHp;
                MaxMP = maxMp;
                AttackPower = attackPower;
                Defense = defense;
                MagicDefense = magicDefense;
                CritChance = critChance;
                AttackSpeedMultiplier = attackSpeedMultiplier;
            }
        }

        /// <summary>
        /// Duplicated verbatim from <c>LevelingService.RecomputeDerivedStats</c> (F-3-F-9).
        /// Preview-only -- never written back to <see cref="CharacterStats"/>.
        /// </summary>
        public static DerivedStatsPreview ComputeDerivedStatsPreview(
            int strength, int dexterity, int vitality, int intelligence, float tier)
        {
            int maxHp = Mathf.FloorToInt((200 + vitality * 20) * tier);
            int maxMp = Mathf.Min(Mathf.FloorToInt((100 + intelligence * 12) * tier), 9999);
            int attackPower = Mathf.FloorToInt((10 + strength * 2) * tier);
            int defense = Mathf.FloorToInt((5 + vitality * 1.5f) * tier);
            int magicDefense = Mathf.FloorToInt(intelligence * 0.4f * tier);
            float critChance = 0.05f + (dexterity * 0.0015f * tier);
            float attackSpeedMultiplier = 1.0f + (dexterity * 0.003f * tier);

            return new DerivedStatsPreview(
                maxHp, maxMp, attackPower, defense, magicDefense, critChance, attackSpeedMultiplier);
        }

        /// <summary>
        /// Duplicated verbatim from <see cref="LevelingService"/>'s private
        /// <c>GetAutoAllocIncrement</c> switch. Maps a primary <see cref="StatID"/> to its
        /// per-level auto-alloc increment for <paramref name="def"/>. Returns 0 for any
        /// non-primary <see cref="StatID"/>, matching the source switch's default case.
        /// </summary>
        public static int GetAutoAllocIncrement(ClassDefinition def, StatID stat)
        {
            switch (stat)
            {
                case StatID.Strength: return def.StrengthAutoAlloc;
                case StatID.Dexterity: return def.DexterityAutoAlloc;
                case StatID.Vitality: return def.VitalityAutoAlloc;
                case StatID.Intelligence: return def.IntelligenceAutoAlloc;
                default: return 0;
            }
        }

        /// <summary>
        /// CR-4.3 respec floor formula, duplicated verbatim from the floor-validation loop in
        /// <c>LevelingService.TryApplyRespec</c>: <c>floor = 10 + (level - 1) * increment</c>.
        /// </summary>
        public static int GetRespecFloor(int level, int autoAllocIncrement)
            => 10 + (level - 1) * autoAllocIncrement;
    }
}
