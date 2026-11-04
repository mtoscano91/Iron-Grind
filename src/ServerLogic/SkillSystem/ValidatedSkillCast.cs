using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>A cast that passed V-0 to V-8, handed to the next stage (V-9 in Story 006).</summary>
    public readonly struct ValidatedSkillCast
    {
        /// <summary>The caster.</summary>
        public readonly EntityID Caster;

        /// <summary>The effective target: the caster for a <c>Self</c> skill, else the requested target.</summary>
        public readonly EntityID Target;

        /// <summary>The skill definition.</summary>
        public readonly SkillDefinition Definition;

        /// <summary>The skill's slot in the caster's store.</summary>
        public readonly int SkillSlot;

        /// <summary>The server tick being run.</summary>
        public readonly uint CurrentTick;

        /// <summary>Creates a validated cast.</summary>
        /// <param name="caster">The caster (the pending request's caster, never the slot owner).</param>
        /// <param name="target">The effective target: the caster for a Self skill, else the requested target.</param>
        /// <param name="definition">The skill definition.</param>
        /// <param name="skillSlot">The skill's slot in the caster's store.</param>
        /// <param name="currentTick">The server tick being run.</param>
        public ValidatedSkillCast(EntityID caster, EntityID target, in SkillDefinition definition, int skillSlot, uint currentTick)
        {
            Caster = caster;
            Target = target;
            Definition = definition;
            SkillSlot = skillSlot;
            CurrentTick = currentTick;
        }
    }
}
