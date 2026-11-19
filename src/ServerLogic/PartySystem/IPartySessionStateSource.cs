using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The narrow seam through which the Party System reads a member's connection state (Party System Story 009,
    /// D1). The production adapter is <see cref="SessionMachinePartySessionStateSource"/>.
    /// </summary>
    public interface IPartySessionStateSource
    {
        /// <summary>Reads whether a player's session is disconnected.</summary>
        /// <param name="entity">The player's entity.</param>
        /// <param name="disconnected">
        /// True for <c>Disconnected_SessionActive</c> or <c>Reconnecting</c>; false for <c>Connected</c>.
        /// </param>
        /// <returns>False when the entity has no slot or no session (the caller ignores the member).</returns>
        bool TryIsDisconnected(EntityID entity, out bool disconnected);
    }
}
