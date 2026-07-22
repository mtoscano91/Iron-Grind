namespace IronGrind.Networking
{
    /// <summary>
    /// The CCR-3 delivery-context codes (<c>networking-channel-contract.md</c>) — which batch, packet,
    /// or standalone path carries this message.
    /// </summary>
    /// <remarks>
    /// <see cref="Standalone"/> is not one of the four codes CCR-3's own legend paragraph names
    /// (<c>P1</c>/<c>RU-B</c>/<c>CC-B</c>/<c>POS-B</c>), but the CCR-3 table itself uses the literal
    /// word "Standalone" for <c>HeartbeatMessage</c>/<c>RttProbe</c>/<c>RttProbeEcho</c> — messages
    /// sent outside any batch and outside the R-OD priority path. Included here so the registry can
    /// represent every row the real table contains, not just the four legend-defined codes.
    /// </remarks>
    public enum MessageDeliveryContext : byte
    {
        /// <summary>Sent on its own, outside any batch and outside the R-OD priority path (e.g. <c>HeartbeatMessage</c>).</summary>
        Standalone = 0,

        /// <summary>P1 — Path 1, the R-OD priority path.</summary>
        PriorityPath = 1,

        /// <summary>RU-B — the R-U batch (Path 2a).</summary>
        ReliableUnorderedBatch = 2,

        /// <summary>CC-B — the CycleBroadcast packet (Path 2b-i).</summary>
        CycleBroadcastPacket = 3,

        /// <summary>POS-B — the Position packet (Path 2b-ii).</summary>
        PositionPacket = 4,
    }
}
