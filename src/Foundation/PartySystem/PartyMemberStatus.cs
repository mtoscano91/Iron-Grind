namespace IronGrind.PartySystem
{
    /// <summary>
    /// A party member's status (design/gdd/party-system.md CR-PS-3; TR-party-010; Party System Story 004).
    /// Mirrors <c>design/gdd/networking-wire-protocol.md</c> section PartyMemberStatus value-for-value so the
    /// <c>PartyStateUpdate</c> codec maps 1:1 - do not add or renumber members without updating the wire GDD first.
    /// </summary>
    /// <remarks>
    /// The GDD / TR name "Disconnected" is <see cref="Ghost"/> at MVP: a disconnected player with a live session is a
    /// ghost (ADR-011). <see cref="Online"/> is 0 so an uninitialised value never reads as Dead.
    /// </remarks>
    public enum PartyMemberStatus : byte
    {
        /// <summary>In the party's zone and alive. The default.</summary>
        Online = 0,

        /// <summary>Disconnected with a live session (ghost).</summary>
        Ghost = 1,

        /// <summary>In another zone.</summary>
        OutOfZone = 2,

        /// <summary>Dead; the slot is retained during the respawn countdown (CR-DR-5).</summary>
        Dead = 3
    }
}
