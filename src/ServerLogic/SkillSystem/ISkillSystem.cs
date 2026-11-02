using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Skill System entity lifecycle surface (entities.yaml; skill-system.md CR-SK-20; Skill System Story 002).
    /// </summary>
    public interface ISkillSystem
    {
        /// <summary>Builds the entity's skill store from its class and level (CR-SK-20, EC-SK-12).</summary>
        /// <param name="entity">The spawned player entity.</param>
        /// <param name="classType">The entity's class.</param>
        void OnEntitySpawned(EntityID entity, ClassType classType);

        /// <summary>Releases the entity's skill store (CR-SK-20).</summary>
        /// <param name="entity">The despawned entity.</param>
        void OnEntityDespawned(EntityID entity);

        /// <summary>Unlocks skills reached by a level-up (Story 003).</summary>
        /// <param name="entity">The entity that levelled up.</param>
        /// <param name="newLevel">Its new level.</param>
        void OnLevelUp(EntityID entity, int newLevel);
    }
}
