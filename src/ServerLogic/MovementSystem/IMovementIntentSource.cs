using IronGrind.Networking;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Per-tick source of the latest movement intent for a player slot (CR-MOV-10). The Story 004 intent buffer
    /// implements it; Story 005 tests use a fake.
    /// </summary>
    /// <example>
    /// <code>
    /// if (intents.TryTakeIntent(slot, out MovementIntentMessage intent)) { /* apply */ }
    /// </code>
    /// </example>
    public interface IMovementIntentSource
    {
        /// <summary>Takes (and clears) the intent buffered for the slot.</summary>
        /// <param name="playerSlot">Player slot index in [0, MAX_PLAYERS_PER_ZONE).</param>
        /// <param name="intent">The buffered intent when the method returns true.</param>
        /// <returns>True when a message was buffered; false means no message this tick (zero input).</returns>
        bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent);
    }
}
