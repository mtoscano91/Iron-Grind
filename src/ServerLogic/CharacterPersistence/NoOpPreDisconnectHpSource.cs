using IronGrind.CharacterStats;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Production <see cref="IPreDisconnectHpSource"/> until Networking Core records disconnect snapshots: it never has
    /// one, so the ghost-death save always falls back to the effective MaxHP (Death and Respawn Story 010, Decision 1).
    /// </summary>
    public sealed class NoOpPreDisconnectHpSource : IPreDisconnectHpSource
    {
        /// <inheritdoc/>
        public bool TryGetHp(EntityID entity, out float hp)
        {
            hp = 0f;
            return false;
        }
    }
}
