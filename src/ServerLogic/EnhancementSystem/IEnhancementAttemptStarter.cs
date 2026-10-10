using IronGrind.Currency;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// The attempt-start seam (Enhancement Story 010): the request handler hands a request that is
    /// not a duplicate and passed the CR-ENH-15 step 2 checks to whoever runs the attempt. The
    /// production implementation (acknowledgment, begin, commit, result) is Story 015's.
    /// </summary>
    /// <example>
    /// <code>
    /// starter.StartAttempt(context.CharacterId, requestId, itemSlotIndex: 0, scrollSlotIndex: 1);
    /// </code>
    /// </example>
    public interface IEnhancementAttemptStarter
    {
        /// <summary>Starts an attempt for a validated, non-duplicate request (CR-ENH-15).</summary>
        /// <param name="characterId">The requesting character.</param>
        /// <param name="requestId">The client's <c>requestId</c> (EC-NET-9).</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance.</param>
        /// <param name="scrollSlotIndex">Bag slot of the scroll.</param>
        void StartAttempt(CharacterID characterId, uint requestId, int itemSlotIndex, int scrollSlotIndex);
    }
}
