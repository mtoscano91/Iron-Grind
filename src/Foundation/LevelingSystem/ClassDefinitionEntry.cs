using System;
using IronGrind.SkillSystem;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Savable form of one <see cref="ClassDefinition"/> inside <see cref="ClassDatabase"/> (Skill System
    /// Story 012; class-system.md CD-1, CD-2). <see cref="ClassDefinition"/> is a readonly struct, which Unity does
    /// not serialize.
    /// </summary>
    [Serializable]
    public struct ClassDefinitionEntry
    {
        /// <summary>The class this entry defines.</summary>
        public ClassType ClassType;

        /// <summary>Strength gained per level-up, in [0, 2] at MVP (class-system.md CD-2).</summary>
        public int StrengthAutoAlloc;

        /// <summary>Dexterity gained per level-up, in [0, 2] at MVP.</summary>
        public int DexterityAutoAlloc;

        /// <summary>Vitality gained per level-up, in [0, 2] at MVP.</summary>
        public int VitalityAutoAlloc;

        /// <summary>Intelligence gained per level-up, in [0, 2] at MVP.</summary>
        public int IntelligenceAutoAlloc;

        /// <summary>Free stat points per level-up; auto-allocations plus this equal 5 at MVP (CD-10).</summary>
        public int FreePointsPerLevel;

        /// <summary>Raw skill ids of the class, in roster order (skill-system.md Skill Roster).</summary>
        public uint[] SkillList;

        /// <summary>Builds the runtime definition. The skill list is copied, so the asset is never shared.</summary>
        /// <returns>A <see cref="ClassDefinition"/> with the same values.</returns>
        public ClassDefinition ToClassDefinition()
        {
            uint[] skills = SkillList == null ? null : (uint[])SkillList.Clone();
            return new ClassDefinition(StrengthAutoAlloc, DexterityAutoAlloc, VitalityAutoAlloc, IntelligenceAutoAlloc,
                FreePointsPerLevel, skills);
        }
    }
}
