namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Lifecycle state of a party (party-system.md States and Transitions; Party System Story 002). Named
    /// <c>PartyLifecycleState</c> so it does not clash with the <see cref="PartyState"/> class.
    /// </summary>
    public enum PartyLifecycleState
    {
        /// <summary>A real party with a single member (the leader), waiting for a second member.</summary>
        Forming = 0,

        /// <summary>
        /// A real party with two or more members. A solo party is also created in this state (it is a stable,
        /// fully formed party of one that simply never accepts members).
        /// </summary>
        Active = 1,

        /// <summary>The party is being torn down (set by later stories; the registry never sets it).</summary>
        Disbanding = 2,

        /// <summary>The party has only ghost members (set by later stories; the registry never sets it).</summary>
        Ghost = 3,
    }
}
