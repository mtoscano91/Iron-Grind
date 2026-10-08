using UnityEngine;
using IronGrind.CharacterStats;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// The one copy of the Leveling display formulas (ADR-012 Decision 7, Leveling Story 014).
    /// <see cref="LevelingService"/> (server) calls these to decide outcomes; the HUD
    /// (<c>LevelUpOverlayPresenter</c>, <c>RespecScreenPresenter</c>) calls the same methods to
    /// show the player what the server will compute. Pure and stateless: no member reads or
    /// writes any field, so no multiplier can be cached (AC-LS-16, AC-LS-34).
    /// </summary>
    public static class LevelingDisplayFormulas
    {
        /// <summary>The seven F-3-F-9 derived stats computed for a set of primary-attribute totals and a tier.</summary>
        public readonly struct DerivedStats
        {
            /// <summary>Maximum hit points.</summary>
            public readonly int MaxHP;

            /// <summary>Maximum mana points, capped at 9999.</summary>
            public readonly int MaxMP;

            /// <summary>Attack power.</summary>
            public readonly int AttackPower;

            /// <summary>Physical defense.</summary>
            public readonly int Defense;

            /// <summary>Magic defense.</summary>
            public readonly int MagicDefense;

            /// <summary>Critical hit chance as a fraction (0.05 = 5 percent), unclamped.</summary>
            public readonly float CritChance;

            /// <summary>Attack speed multiplier, unclamped.</summary>
            public readonly float AttackSpeedMultiplier;

            /// <summary>Creates a value holding the seven derived stats.</summary>
            public DerivedStats(
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
        /// CR-2.3 tier lookup: L1–19 → ×1.0, L20–39 → ×1.2, L40–59 → ×1.5, L60 → ×2.0, based on
        /// the NEW level. Pure and stateless — every call reads only <paramref name="level"/> and
        /// returns immediately; there is no field this value could be cached in, satisfying
        /// AC-LS-16/AC-LS-34's "never a stored/cached multiplier" requirement structurally.
        /// Client consumers: <c>LevelUpOverlayPresenter</c> (tier-transition detection, AC-LS-46)
        /// and <c>RespecScreenPresenter</c> (preview tier, AC-LS-48).
        /// </summary>
        /// <param name="level">The character level.</param>
        public static float GetLevelTierMultiplier(int level)
        {
            if (level >= 60) return 2.0f;
            if (level >= 40) return 1.5f;
            if (level >= 20) return 1.2f;
            return 1.0f;
        }

        /// <summary>
        /// F-3-F-9 derived-stat formulas. <c>LevelingService.RecomputeDerivedStats</c> writes the
        /// result to <c>CharacterStats</c>; <c>RespecScreenPresenter</c> shows it as the projected
        /// preview column (AC-LS-48) and never writes it anywhere.
        /// </summary>
        /// <param name="strength">Strength total.</param>
        /// <param name="dexterity">Dexterity total.</param>
        /// <param name="vitality">Vitality total.</param>
        /// <param name="intelligence">Intelligence total.</param>
        /// <param name="tier">The tier multiplier from <see cref="GetLevelTierMultiplier"/>.</param>
        public static DerivedStats ComputeDerivedStats(
            int strength, int dexterity, int vitality, int intelligence, float tier)
        {
            int maxHp = Mathf.FloorToInt((200 + vitality * 20) * tier);
            int maxMp = Mathf.Min(Mathf.FloorToInt((100 + intelligence * 12) * tier), 9999);
            int attackPower = Mathf.FloorToInt((10 + strength * 2) * tier);
            int defense = Mathf.FloorToInt((5 + vitality * 1.5f) * tier);
            int magicDefense = Mathf.FloorToInt(intelligence * 0.4f * tier);
            float critChance = 0.05f + (dexterity * 0.0015f * tier);
            float attackSpeedMultiplier = 1.0f + (dexterity * 0.003f * tier);

            return new DerivedStats(
                maxHp, maxMp, attackPower, defense, magicDefense, critChance, attackSpeedMultiplier);
        }

        /// <summary>
        /// Maps a primary <see cref="StatID"/> to its per-level auto-alloc increment for
        /// <paramref name="def"/>. Returns 0 for any non-primary <see cref="StatID"/>.
        /// Client consumer: <c>RespecScreenPresenter</c> (respec floor display, AC-LS-48).
        /// </summary>
        /// <param name="def">The class definition holding the increments.</param>
        /// <param name="stat">The stat to look up.</param>
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
        /// CR-4.3 respec floor: <c>floor = 10 + (level - 1) * increment</c>. Client consumer:
        /// <c>RespecScreenPresenter</c> (floor labels and the never-editable minimum, AC-LS-48).
        /// </summary>
        /// <param name="level">The character level.</param>
        /// <param name="autoAllocIncrement">The per-level increment from <see cref="GetAutoAllocIncrement"/>.</param>
        public static int GetRespecFloor(int level, int autoAllocIncrement)
            => 10 + (level - 1) * autoAllocIncrement;
    }
}
