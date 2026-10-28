namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Seam over Character Stats' entity-death notification (status-effects.md CR-SE-10, ADR-010).
    /// <see cref="StatusEffectsService"/> subscribes once in its constructor and unsubscribes in Dispose, so tests
    /// can raise a death without a full Character Stats container.
    /// </summary>
    public interface IEntityDeathSource
    {
        /// <summary>Registers <paramref name="handler"/> to be called when an entity dies.</summary>
        /// <param name="handler">The handler; it must not write to Character Stats.</param>
        void Subscribe(CharacterStats.CharacterStats.EntityDiedHandler handler);

        /// <summary>Removes <paramref name="handler"/>; matched by delegate instance. No-op if not subscribed.</summary>
        /// <param name="handler">The handler passed to <see cref="Subscribe"/>.</param>
        void Unsubscribe(CharacterStats.CharacterStats.EntityDiedHandler handler);
    }
}
