using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Respawn notification seam to Status Effects (death-and-respawn.md CR-DR-11a; Death and Respawn Story 004,
    /// AC-6). Implemented by <c>StatusEffectsService</c> through its existing <c>OnEntityRespawned</c>.
    /// </summary>
    public interface IStatusEffectsRespawnSink
    {
        /// <summary>Tells Status Effects the entity is alive again so it accepts effects.</summary>
        /// <param name="entity">The respawned player's EntityID.</param>
        void OnEntityRespawned(EntityID entity);
    }
}
