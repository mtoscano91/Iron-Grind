namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The start side of the outbound sender, startup S8 (after the inbound shell start). No startable
    /// outbound sender exists in code yet; Story 044 attaches one through
    /// <c>ZoneServerComposition.AttachOutboundSender</c>. When none is attached S8 skips the call.
    /// </summary>
    /// <example>
    /// <code>
    /// composition.AttachOutboundSender(sender); // S5
    /// // S8: sender.Start();
    /// </code>
    /// </example>
    public interface IZoneOutboundSender
    {
        /// <summary>Starts sending.</summary>
        void Start();
    }
}
