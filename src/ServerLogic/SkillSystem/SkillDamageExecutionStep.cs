using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Execution step for an accepted cast: auto-attack timer reset, facing, BaseDamage, damage, kill sequence
    /// (skill-system.md CR-SK-3 to CR-SK-6, F-SK-1, F-SK-2, CR-SK-4; hit-detection.md CR-HD-7; Skill System Story 007).
    /// </summary>
    /// <remarks>
    /// Order: <c>NotifySkillUsed</c>, facing, then for a damage skill BaseDamage, <c>Calculate</c>, and on a kill the
    /// target position read, <c>GetXPAward</c>, <c>AddExperience</c>, <c>Apply</c>, <c>ReportKill</c>; otherwise <c>Apply</c> only.
    /// Damage goes through the inner applier only (Decision B): no DamageEvent or SelfDamageEvent. Facing runs for
    /// SingleHostile and SingleFriendly casts on another entity (Decision C). The damage context is derived from the
    /// effect flags (MagicalDamage without PhysicalDamage is MagicalSkill, otherwise PhysicalSkill). No random numbers;
    /// allocation-free. A death position is XZ from the position source with Y = 0, or <see cref="Vector3.zero"/> when unknown.
    /// A result with <c>FinalDamage &lt;= 0</c> (a miss, or a kill flag with no damage) ends the step right after Calculate:
    /// no Apply, no XP and no ReportKill, matching the auto-attack path; the mana stays spent (AC-SK-16).
    /// <para>
    /// <c>SkillDefinition.DamageContext</c> is deliberately not read: the context comes from the effect flags per GDD CR-SK-5,
    /// so a Physical|Magical skill takes the physical path. A load-time agreement check is carried to Story 012.
    /// The death position has Y = 0, a known limit until a 3D position seam exists.
    /// A throw partway through the kill sequence (after AddExperience, before Apply) leaves the XP awarded. This is
    /// accepted under Decision E; there is no rollback.
    /// </para>
    /// </remarks>
    public sealed class SkillDamageExecutionStep : ISkillExecutionStep
    {
        private const byte PHYSICAL_FLAG = (byte)SkillEffectType.PhysicalDamage;
        private const byte MAGICAL_FLAG = (byte)SkillEffectType.MagicalDamage;

        private readonly IAutoAttackSkillUseSink _skillUse;
        private readonly ISkillStatSource _stats;
        private readonly ISkillPositionSource _positions;
        private readonly ISkillDamageCalculator _damage;
        private readonly IAutoAttackXpAward _xpAward;
        private readonly IAutoAttackExperienceSink _experience;
        private readonly IAutoAttackDamageApplier _applier;
        private readonly IAutoAttackFaceOutput _face;
        private readonly IMobKillReporter _killReporter;

        /// <summary>Creates the step.</summary>
        /// <param name="skillUse">Resets the caster's auto-attack timer.</param>
        /// <param name="stats">Reads the scaling stat.</param>
        /// <param name="positions">Caster, target and death positions.</param>
        /// <param name="damage">Resolves the hit.</param>
        /// <param name="xpAward">XP a killed mob is worth.</param>
        /// <param name="experience">Receives the XP of a kill.</param>
        /// <param name="applier">The inner damage applier (not the DamageEvent-sending wrapper).</param>
        /// <param name="face">Facing output.</param>
        /// <param name="killReporter">Raises OnMobDied for a skill kill.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public SkillDamageExecutionStep(
            IAutoAttackSkillUseSink skillUse,
            ISkillStatSource stats,
            ISkillPositionSource positions,
            ISkillDamageCalculator damage,
            IAutoAttackXpAward xpAward,
            IAutoAttackExperienceSink experience,
            IAutoAttackDamageApplier applier,
            IAutoAttackFaceOutput face,
            IMobKillReporter killReporter)
        {
            _skillUse = skillUse ?? throw new ArgumentNullException(nameof(skillUse));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
            _damage = damage ?? throw new ArgumentNullException(nameof(damage));
            _xpAward = xpAward ?? throw new ArgumentNullException(nameof(xpAward));
            _experience = experience ?? throw new ArgumentNullException(nameof(experience));
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            _face = face ?? throw new ArgumentNullException(nameof(face));
            _killReporter = killReporter ?? throw new ArgumentNullException(nameof(killReporter));
        }

        /// <inheritdoc />
        public void Execute(in ValidatedSkillCast cast)
        {
            EntityID caster = cast.Caster;
            EntityID target = cast.Target;

            _skillUse.NotifySkillUsed(caster);
            Face(caster, target, cast.Definition.TargetConstraint);

            byte flags = (byte)cast.Definition.SkillEffectType;
            bool physical = (flags & PHYSICAL_FLAG) != 0;
            bool magical = (flags & MAGICAL_FLAG) != 0;
            if (!physical && !magical)
            {
                return;
            }

            DamageContext context = magical && !physical ? DamageContext.MagicalSkill : DamageContext.PhysicalSkill;
            int stat = _stats.GetStat(caster, MapStat(cast.Definition.DamageBaseStat));
            int baseDamage = ComputeBaseDamage(stat, cast.Definition.DamageMultiplier);

            DamageResult result = _damage.Calculate(baseDamage, caster, target, context);
            if (result.FinalDamage <= 0)
            {
                return;
            }

            if (!result.IsKill)
            {
                _applier.Apply(caster, target, in result);
                return;
            }

            // The applier may remove the mob, so the position is read first (Decision D).
            Vector3 deathPosition = Vector3.zero;
            if (_positions.TryGetPositionXZ(target, out float x, out float z))
            {
                deathPosition = new Vector3(x, 0f, z);
            }

            int xp = _xpAward.GetXPAward(target);
            _experience.AddExperience(caster, xp);
            _applier.Apply(caster, target, in result);
            _killReporter.ReportKill(caster, target, deathPosition);
        }

        /// <summary>F-SK-1: <c>Floor(stat * multiplier)</c> in double, clamped to [0, int.MaxValue].</summary>
        /// <param name="stat">The scaling stat value.</param>
        /// <param name="multiplier">The skill's damage multiplier.</param>
        /// <returns>The base damage; 0 for NaN, infinity or a non-positive product, <see cref="int.MaxValue"/> on overflow.</returns>
        public static int ComputeBaseDamage(int stat, float multiplier)
        {
            double d = Math.Floor(stat * (double)multiplier);
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0d)
            {
                return 0;
            }

            return d >= int.MaxValue ? int.MaxValue : (int)d;
        }

        private static StatID MapStat(DamageStat stat)
        {
            switch (stat)
            {
                case DamageStat.AttackPower:
                    return StatID.AttackPower;
                case DamageStat.Intelligence:
                    return StatID.Intelligence;
                case DamageStat.Strength:
                    return StatID.Strength;
                default:
                    Debug.LogError($"SkillCast: unmapped DamageStat {(int)stat}; falling back to AttackPower.");
                    return StatID.AttackPower;
            }
        }

        private void Face(EntityID caster, EntityID target, TargetConstraint constraint)
        {
            if (constraint == TargetConstraint.Self || target == caster)
            {
                return;
            }

            if (!_positions.TryGetPositionXZ(caster, out float cx, out float cz)
                || !_positions.TryGetPositionXZ(target, out float tx, out float tz))
            {
                return;
            }

            _face.Face(caster, new Vector3(tx - cx, 0f, tz - cz));
        }
    }
}
