using System;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.StatusEffects;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// One authored skill (skill-system.md SkillDefinition Schema; Skill System Story 001). A plain struct with
    /// public fields so Unity serializes it inside <see cref="SkillDatabase"/>.
    /// </summary>
    [Serializable]
    public struct SkillDefinition
    {
        /// <summary>
        /// Raw skill id; 0 = <see cref="SkillID.Invalid"/>, reserved. Stored as <see cref="uint"/> because Unity
        /// does not serialize a readonly struct (Class System CD-4). Use <see cref="SkillId"/> for the typed id.
        /// </summary>
        public uint ID;

        /// <summary>HUD and tooltip display only; not used in game logic.</summary>
        public string DisplayName;

        /// <summary>Class that owns the skill: Warrior=0, Healer=1.</summary>
        public ClassType ClassType;

        /// <summary>Level at which the skill unlocks, in [1, 60].</summary>
        public int UnlockLevel;

        /// <summary>Mana cost, in [0, MaxMP].</summary>
        public int ManaCost;

        /// <summary>Cooldown in ticks, in [0, 6000]; 0 = no cooldown (design flag; requires review).</summary>
        public int CooldownTicks;

        /// <summary>Range in units, in [0.0, 20.0]; 0 = no range check.</summary>
        public float RangeUnits;

        /// <summary>Accepted targets: SingleHostile=0, SingleFriendly=1, Self=2.</summary>
        public TargetConstraint TargetConstraint;

        /// <summary>Effect flags: PhysicalDamage=1, MagicalDamage=2, InstantHeal=4, ApplyBuff=8, ApplyDebuff=16.</summary>
        public SkillEffectType SkillEffectType;

        /// <summary>Damage multiplier, in [0.0, 5.0]; ignored when no damage flag is set.</summary>
        public float DamageMultiplier;

        /// <summary>
        /// Stat the damage scales from. Default AttackPower for PhysicalDamage, Intelligence for MagicalDamage;
        /// Strength for STR-scaling Healer skills.
        /// </summary>
        public DamageStat DamageBaseStat;

        /// <summary>Damage context passed to DamageCalculation: PhysicalSkill or MagicalSkill.</summary>
        public DamageContext DamageContext;

        /// <summary>Heal multiplier, in [0.0, 5.0]; ignored unless InstantHeal is set.</summary>
        public float HealMultiplier;

        /// <summary>
        /// <c>default(BuffID)</c> (0) = no buff; required when ApplyBuff or ApplyDebuff is set. <see cref="CharacterStats.BuffID"/>
        /// has no members yet: the roster (Story 012) assigns the values.
        /// </summary>
        public BuffID BuffID;

        /// <summary>Passed to StatusEffects.ApplyEffect; no heap allocation.</summary>
        /// <remarks>
        /// <see cref="StatusEffects.BuffDefinition"/> is a readonly struct, so Unity's serializer will not persist
        /// it in an asset. The roster asset (Story 012) must resolve that.
        /// </remarks>
        public BuffDefinition BuffDefinition;

        /// <summary>The typed skill id.</summary>
        public SkillID SkillId => new SkillID(ID);
    }
}
