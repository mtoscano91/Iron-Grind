using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Read-only lookup of mob definitions (design/gdd/enemy-ai.md, IMobDefinitionRegistry Interface).
    /// Consumers (Mob Spawning, Navigation, Combat) only read; only Enemy AI builds the registry.
    /// </summary>
    public interface IMobDefinitionRegistry
    {
        /// <summary>Returns the definition for <paramref name="id"/>, or null for an unknown id and for <see cref="MobTypeID.Invalid"/>.</summary>
        MobDefinition GetDefinition(MobTypeID id);
    }
}
