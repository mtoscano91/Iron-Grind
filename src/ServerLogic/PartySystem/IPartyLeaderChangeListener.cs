using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Observes leader changes in <see cref="PartyRegistry"/> (Party System Story 003). Called only when the leader
    /// actually changes: the leader leaves while members remain, or a transfer succeeds. Never for a non-leader
    /// removal.
    /// </summary>
    /// <remarks>
    /// During a removal, at <see cref="OnLeaderChanged"/> the departing member is already out of the party slots,
    /// but the registry's character-to-party mapping is not updated yet (<c>GetPartyID</c> still returns the old
    /// party for that character). The registry holds a single listener: <c>SetLeaderChangeListener</c> replaces
    /// any previous one, and null clears it. Listeners (and notice sinks they call) must not mutate the registry
    /// from these callbacks; the registry is not reentrant.
    /// </remarks>
    public interface IPartyLeaderChangeListener
    {
        /// <summary>Called BEFORE any mutation that changes the leader (the party is still unchanged).</summary>
        /// <param name="partyId">The party.</param>
        void OnLeaderChanging(PartyID partyId);

        /// <summary>Called AFTER the leader changed and the slots are compacted.</summary>
        /// <param name="partyId">The party.</param>
        void OnLeaderChanged(PartyID partyId);
    }
}
