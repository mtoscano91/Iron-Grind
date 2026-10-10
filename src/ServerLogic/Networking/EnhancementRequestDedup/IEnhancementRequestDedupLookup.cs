using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Finds a character's <see cref="EnhancementRequestDeduplicator"/> (Enhancement Story 010; EC-NET-9).
    /// The request handler only reads <see cref="EnhancementRequestDeduplicator.LastEnhancementRequestId"/>
    /// through it; recording a new id is Story 015's.
    /// </summary>
    /// <example>
    /// <code>
    /// if (lookup.TryGetDeduplicator(characterId, out EnhancementRequestDeduplicator dedup)) { }
    /// </code>
    /// </example>
    public interface IEnhancementRequestDedupLookup
    {
        /// <summary>Gets the character's deduplicator.</summary>
        /// <param name="characterId">The character.</param>
        /// <param name="deduplicator">The deduplicator when found; otherwise null.</param>
        /// <returns>True iff the character has a deduplicator.</returns>
        bool TryGetDeduplicator(CharacterID characterId, out EnhancementRequestDeduplicator deduplicator);
    }
}
