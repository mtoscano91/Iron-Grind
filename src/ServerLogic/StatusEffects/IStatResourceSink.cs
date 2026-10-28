using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Seam between Status Effects and the Character Stats resource pools (HP and MP).
    /// The production adapter is <see cref="CharacterStatsResourceSink"/>. The per-tick regen of
    /// <see cref="StatusEffectsService"/> uses it (Story 005, CR-SE-6); the instant heal of Story 007 reuses it.
    /// </summary>
    public interface IStatResourceSink
    {
        /// <summary>Restores HP. Character Stats clamps the result to the entity's maximum HP.</summary>
        /// <param name="entityId">The entity that is healed.</param>
        /// <param name="amount">HP to restore.</param>
        void ApplyRegen(EntityID entityId, float amount);

        /// <summary>Restores MP. Character Stats clamps the result to the entity's maximum MP.</summary>
        /// <param name="entityId">The entity that receives the mana.</param>
        /// <param name="amount">MP to restore.</param>
        void ApplyManaRegen(EntityID entityId, float amount);

        /// <summary>
        /// Clamps current HP to the entity's effective maximum HP (Story 006, CR-SE-8 step 3, OQ-SE-1). Called after
        /// a MaxHP modifier is removed. Fires no event unless HP reaches 0 (then the death event).
        /// </summary>
        /// <param name="entityId">The entity.</param>
        void ClampHPToMax(EntityID entityId);

        /// <summary>
        /// Clamps current MP to the entity's effective maximum MP (Story 006, EC-SE-5, OQ-SE-1). Called after a
        /// MaxMP modifier is removed. Fires no event when MP is unchanged.
        /// </summary>
        /// <param name="entityId">The entity.</param>
        void ClampMPToMax(EntityID entityId);
    }
}
