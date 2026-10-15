namespace IronGrind.Currency
{
    /// <summary>
    /// Narrow persistence contract for Character Persistence: restore on load (CR-CP-3 step 6, Story 004)
    /// and read on save (CR-CP-10 step 5, Story 007). Kept apart from <see cref="ICurrencyService"/> so that gameplay
    /// systems, which hold that interface, cannot overwrite a balance (Currency GDD Rule 9).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ICurrencyService.RegisterCharacter"/>, which resets the version epoch to 0,
    /// this restores the persisted version so the optimistic-concurrency counter continues across
    /// sessions (ADR-010 Tier 1 direct call).
    /// </remarks>
    public interface ICurrencyStateRestore
    {
        /// <summary>
        /// Restores a character's persisted gold state on load: sets the balance and the version,
        /// and registers the character if it is new. A state restore, not a mutation: it raises no
        /// <see cref="ICurrencyService.OnGoldSync"/> event and writes no transaction. Calling it
        /// again for a registered character overwrites both values unconditionally.
        /// </summary>
        /// <param name="charId">The character being loaded.</param>
        /// <param name="balance">The persisted gold balance.</param>
        /// <param name="version">The persisted gold version.</param>
        void RestoreCharacter(CharacterID charId, uint balance, uint version);

        /// <summary>
        /// Reads a character's gold balance and version together, for Character Persistence's save path
        /// (CR-CP-10 step 5, Story 007). The two values are read under the same per-character lock, so they
        /// are a consistent pair. This is the persistence seam in both directions
        /// (<see cref="RestoreCharacter"/> in, this out).
        /// </summary>
        /// <param name="charId">The character being saved.</param>
        /// <param name="balance">The current balance; 0 when the character is not registered.</param>
        /// <param name="version">The current version; 0 when the character is not registered.</param>
        /// <returns><c>true</c> when the character is registered; <c>false</c> otherwise.</returns>
        bool TryGetState(CharacterID charId, out uint balance, out uint version);
    }
}
