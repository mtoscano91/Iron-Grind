namespace IronGrind.Networking
{
    /// <summary>
    /// What the dispatcher needs to know about one request type (ADR-014 Decision 3). The channel
    /// of the type is not here: registration reads it from the type's
    /// <see cref="MessageRoutingEntry"/>, so it cannot disagree with the routing table.
    /// </summary>
    /// <example>
    /// <code>
    /// var descriptor = new InboundRequestDescriptor(SetTarget.MessageTypeId, RpcTypeTag.SetTarget,
    ///     heldDuringIrreversibleWrite: false, maxBodyBytes: SetTarget.BodySize);
    /// </code>
    /// </example>
    public readonly struct InboundRequestDescriptor
    {
        /// <summary>The wire <c>MessageTypeID</c> of the request.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>The rate-limit bucket the guard chain uses for this request (a member of <see cref="RpcTypeTag"/>).</summary>
        public readonly RpcTypeTag RpcTypeTag;

        /// <summary>True when the request is held while the character's mutation gate is closed (ADR-011 Decision 4).</summary>
        public readonly bool HeldDuringIrreversibleWrite;

        /// <summary>Longest body, in bytes, after the 14-byte envelope.</summary>
        public readonly ushort MaxBodyBytes;

        /// <summary>Creates a request descriptor.</summary>
        /// <param name="messageTypeId">The wire <c>MessageTypeID</c>.</param>
        /// <param name="rpcTypeTag">The rate-limit bucket.</param>
        /// <param name="heldDuringIrreversibleWrite">Whether the request is held while the gate is closed.</param>
        /// <param name="maxBodyBytes">Longest body in bytes.</param>
        public InboundRequestDescriptor(ushort messageTypeId, RpcTypeTag rpcTypeTag, bool heldDuringIrreversibleWrite, ushort maxBodyBytes)
        {
            MessageTypeId = messageTypeId;
            RpcTypeTag = rpcTypeTag;
            HeldDuringIrreversibleWrite = heldDuringIrreversibleWrite;
            MaxBodyBytes = maxBodyBytes;
        }
    }
}
