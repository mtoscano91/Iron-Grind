namespace IronGrind.Currency
{
    /// <summary>
    /// Narrow restore contract for Character Persistence's load path (CR-CP-3 step 6, Character
    /// Persistence Story 004). Kept apart from <see cref="ICurrencyService"/> so that gameplay
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
    }
}
