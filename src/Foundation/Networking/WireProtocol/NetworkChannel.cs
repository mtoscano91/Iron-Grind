namespace IronGrind.Networking
{
    /// <summary>
    /// The three CR-NET-3 channel types (see <c>ADR-004-networking-library-ngo.md</c> Decision 2 for
    /// the binding NGO <c>NetworkDelivery</c> guarantee mapping — the exact NGO enum member names are
    /// verification-pending against the Unity 6.3 NGO API; this project-owned enum names the
    /// <i>guarantee</i>, not the transport identifier).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ordinal order is deliberate and load-bearing:</b> values are declared in ascending
    /// delivery-guarantee order (<see cref="Unreliable"/> &lt; <see cref="ReliableUnordered"/> &lt;
    /// <see cref="ReliableOrdered"/>) specifically so <see cref="MessageRoutingRegistry.ResolveMultiPillarChannel"/>
    /// (MCR-3's "highest guarantee wins" rule) can compute the max via a plain <c>&gt;</c> comparison
    /// rather than a lookup table. Do not reorder these members.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// NetworkChannel highest = NetworkChannel.ReliableUnordered &gt; NetworkChannel.Unreliable
    ///     ? NetworkChannel.ReliableUnordered : NetworkChannel.Unreliable; // ReliableUnordered
    /// </code>
    /// </example>
    public enum NetworkChannel : byte
    {
        /// <summary>U-U — best-effort, no retransmit, no ordering. Lowest guarantee.</summary>
        Unreliable = 0,

        /// <summary>R-U — guaranteed delivery, no ordering, dedup.</summary>
        ReliableUnordered = 1,

        /// <summary>R-OD — guaranteed, in-order per sender, dedup. Highest guarantee.</summary>
        ReliableOrdered = 2,
    }
}
