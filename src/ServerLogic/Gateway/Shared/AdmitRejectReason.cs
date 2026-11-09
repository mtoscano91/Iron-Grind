namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Why the zone refused a gateway <c>Admit</c> (Authentication Story 013, ADR-016 Decision 4). Carried by
    /// <c>AdmitResult { requestId, accepted, reason }</c> on the gateway link. Shared source (ADR-016 Decision 6): compiled by Unity and by the gateway.
    /// </summary>
    public enum AdmitRejectReason : byte
    {
        /// <summary>Accepted; no rejection.</summary>
        None = 0,

        /// <summary>The zone is not Active (Loading, Draining or Closed).</summary>
        NotActive = 1,

        /// <summary>Occupied player slots plus reserved admissions already reach the zone's player capacity.</summary>
        Full = 2,

        /// <summary>The token is null, not 16 bytes, or all zeros.</summary>
        InvalidToken = 3,
    }
}
